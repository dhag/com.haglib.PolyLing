// PlayerCommandDispatcher.ModifierEval.cs
// コマンドディスパッチャ：モディファイアスタックの項目（モディファイア）を依存の順に流す。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【何をするか】（PolyLing_作り直し整備計画.md 2.4・段 4）
//   評価器（ModifierEvaluator）がきっかけ 2 か所で立てた要評価の印を見て、
//   処理器の無い項目を依存の順に作り直す。1 項目は RunModifierStackStep（出力先へ中身だけ書き戻す）。
//
// 【いつ流すか】
//   ・一番外側のコマンドの終わり（DispatchAuthorized）。書き込みの無いコマンド（照会）では流さない。
//     頂点ドラッグ中は流さない
//   ・頂点ドラッグを離したとき（PlayerViewportManager.OnVerticesDragEnded）。
//     コマンドの途中で DragEnd が来たときは流さず、そのコマンドの終わりに流す
//   ・スキンド化の 2 経路の直後（ReevaluateAfterSkinning。段 5）。変換した描画オブジェクトを
//     入力に持つ項目へ印を立てて流す（旧 RunAutoUpdateGroups の置き換え）
//   ・シナリオの書き込みのある項目のあと（TryExecuteRunStep。段 11）。項目は入れ子の Dispatch なので
//     一番外側の終わりに乗らない。署名を取り直してから流す
//   流す前に、モデル空間で読む入力の行列の署名を取り直す（MarkMatrixChanged。段 10）。
//   きっかけ（RebuildAdapter・SyncMeshPositionsAndTransform）の中では流さない。
//   書き戻しが GPU バッファの作り直しを呼ぶので再入になる。
//
// 【Undo】評価結果は積まない（記録を止めて流す）。入力の Undo で入力が戻れば、
//   署名の変化で出力も作り直される。入力の編集と下流の作り直しは Undo 1 回で両方戻る。
//
// 【書き戻しは入れ子の Dispatch】深さ 2 以上で走るので、シナリオの記録・版の対象にならない。
//   出力の保護は RunModifierStackStep が _modifierWriteBackDepth で外す。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 頂点ドラッグを離したときに呼ぶ。コマンドの途中なら何もしない（そのコマンドの終わりで流す）。
        /// </summary>
        public void EvaluateModifiersAfterDrag()
        {
            if (_dispatchDepth > 0) return;
            RunModifierEvaluation("ドラッグを離した");
        }

        /// <summary>
        /// スキンド化の 2 経路から呼ぶ（段 5。旧 RunAutoUpdateGroups の置き換え）。
        /// 変換した描画オブジェクトを入力に持つ項目に印を立て、依存の順に流す
        /// （揺れ鎖が梯子へ塗る → フリル・パイプが引き継ぐ）。そのあと全項目の署名を控え直し、
        /// 焼き直しを入力の変化として二重に流さない。
        /// 呼び出し側が記録を止めた中で呼び、外で Undo を 1 件だけ積む。
        /// スタックの AutoUpdate の値は見ない（評価器は入力に応じて流す）。
        /// </summary>
        private void ReevaluateAfterSkinning(ModelContext model, IReadOnlyList<ulong> changedObjectIds)
        {
            if (model == null) return;
            model.ComputeWorldMatrices();
            if (changedObjectIds != null && changedObjectIds.Count > 0
                && ModifierEvaluator.MarkInputsChanged(model, new List<ulong>(changedObjectIds)) > 0)
                RunModifierEvaluation("スキンド化");
            ModifierEvaluator.PrimeAll(model);
        }

        /// <summary>一番外側のコマンドの終わりに流すか。</summary>
        private bool ShouldEvaluateModifiersAfter(PanelCommand cmd)
        {
            if (cmd == null) return false;
            if (_viewportManager != null && _viewportManager.IsVerticesDragging) return false;
            return Poly_Ling.Remote.RemoteOwnership.WriteScopeOf(cmd) != PLWriteScope.None;
        }

        /// <summary>
        /// 要評価の項目を依存の順に流す。流したものがあれば画面とパネルへ 1 回だけ配る。
        /// </summary>
        /// <param name="recheckInputs">
        /// 印に頼らず、入力の署名を取り直してから流す（Undo・Redo の後）。
        /// </param>
        /// <param name="displayAtCommandEnd">
        /// 一番外側のコマンドの終わり（DispatchAuthorized）から呼ぶときだけ true。
        /// 直後にコマンドの終わりの SettleWorldPositions が必ず走るので、ここでは画面とパネルへ配らず、
        /// その Settle の後に 1 回だけ知らせる（FlushModifierPanelNotify。モディファイア高速化計画 段 8）。
        /// </param>
        private void RunModifierEvaluation(string trigger, bool recheckInputs = false, bool displayAtCommandEnd = false)
        {
            if (ModifierEvaluator.IsEvaluating) return;
            // ドラッグ中の追随の控えはこのドラッグ限り（面構成が変わり得る）。
            // 計測を入れているときは、追随が書いた位置と作り直しの結果の差をパスの後で出す。
            var followedToCheck = Poly_Ling.Diagnostics.PLDiag.Timing ? TakeFollowedPositions() : null;
            _dragFollows.Clear();

            var project = _getProject();
            var model   = project?.CurrentModel;
            if (project == null || model == null) return;
            int modelIndex = project.CurrentModelIndex;

            // 計測（モディファイア高速化計画 段 0）：パス全体の時間と GPU バッファの作り直しの回数。
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            int  rebuild0 = ModifierEvaluator.RefreshCount;
            int    present0  = PlayerViewportManager.PresentCount;
            double presentMs0 = PlayerViewportManager.PresentMs;
            double panelMs0   = PlayerViewportManager.PanelNotifyMs;

            if (recheckInputs) ModifierEvaluator.MarkChangedInputs(model);

            // モデル空間で読む入力の行列（段 10）。BoneTransform・親・オブジェクト移動のように
            // 頂点の格納値を変えずにバインド姿勢を変える操作は、きっかけ 2 か所の位置の署名では拾えない。
            model.ComputeWorldMatrices();
            ModifierEvaluator.MarkMatrixChanged(model);
            // 処理列の入力のウェイト（チュートリアル計画 3.8）。ウェイトだけの変更は
            // GPU バッファの作り直しも位置の同期も通らない。
            ModifierEvaluator.MarkChainWeightsChanged(model);
            double preMs = MsSince(t0);

            // 内訳（モディファイア高速化計画 段 7）。項目ごとに 作り直し・見張り・バッファ作り直しの回数。
            var timing = new List<string>();
            double watchTotal = 0;

            ModifierEvaluator.PassInfo pass;
            var digestStacks = new HashSet<ModifierStack>();
            _undoController?.SuspendRecording();
            try
            {
                pass = ModifierEvaluator.EvaluatePending(model,
                    (ModifierEvaluator.Entry e, out string err, out string note) =>
                    {
                        note = null;
                        long tw0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        // 見張り（段 D）：書くと宣言していない入力が変わったら宣言漏れ。
                        var readOnly = ModifierEvaluator.SnapshotReadOnlyInputs(e);
                        long ts0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        int  rb0 = ModifierEvaluator.RefreshCount;
                        ModifierStepProfiler.Begin();
                        bool ok;
                        string sections;
                        try { ok = RunModifierStackStep(project, model, modelIndex, false, e.Group, e.StepIndex, out err); }
                        finally { sections = ModifierStepProfiler.End(); }
                        double stepMs = MsSince(ts0);
                        int    stepRb = ModifierEvaluator.RefreshCount - rb0;
                        long tu0 = System.Diagnostics.Stopwatch.GetTimestamp();
                        string undeclared = ModifierEvaluator.UndeclaredWrites(e, readOnly);
                        double watchMs = (ts0 - tw0 + System.Diagnostics.Stopwatch.GetTimestamp() - tu0) * 1000.0
                                         / System.Diagnostics.Stopwatch.Frequency;
                        watchTotal += watchMs;
                        timing.Add($"  {e.Label}: 作り直し {F1(stepMs)} ms（バッファ作り直し {stepRb} 回） / 見張り {F1(watchMs)} ms");
                        if (!string.IsNullOrEmpty(sections)) timing.Add($"    区間: {sections}");
                        if (undeclared != null)
                            Debug.LogError($"[ModifierWrite] {e.Label} {e.Step?.Action} が、書くと宣言していない入力を書き換えた：{undeclared}"
                                           + "（PLParam の MeshRefAccess・WriteWhen を実際の書き込みに合わせる）");
                        if (ok)
                        {
                            // ダイジェストはパスの終わりにスタックごとに 1 回取る（段 5）。
                            digestStacks.Add(e.Group);
                            note = JoinNotes(ModifierStackOps.LastBeltMessage, ModifierStackOps.LastProfileMessage);
                        }
                        return ok;
                    },
                    trigger);
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            LogFollowDifference(followedToCheck);

            // 処理列が書いた結果のウェイト・法線を GPU へ送る（チュートリアル計画 3.8）。
            // 面の構成が同じならバッファは作り直されないので、部分転送を予約する。
            var writtenResults = new List<MeshContext>();
            Poly_Ling.Modifiers.ModifierChainHandler.DrainWrittenResults(writtenResults);
            foreach (var written in writtenResults)
            {
                _viewportManager?.QueueVertexAttributesUpload(written);
                var writtenPair = model.GetMirrorPair(written);
                if (writtenPair != null && ReferenceEquals(writtenPair.Real, written))
                    _viewportManager?.QueueVertexAttributesUpload(writtenPair.Mirror);
            }

            long td0 = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var g in digestStacks)
                g.SourceDigest = ModifierStackOps.ComputeSourceDigest(project, g);
            double digestMs = MsSince(td0);

            if (pass.Ran + pass.Failed == 0) return;

            if (pass.Failed > 0)
                Debug.LogWarning("[ModifierStack] 依存の評価で失敗した項目があります（" + trigger + "）:\n"
                                 + string.Join("\n", pass.Lines));

            model.ComputeWorldMatrices();
            model.IsDirty = true;

            // 表示（モディファイア高速化計画 段 3）。書き戻しは描画オブジェクトの中身の入れ替えだけで、
            // 一覧は変えない。位相の有無の判定は SettleWorldPositions に任せる（面の構成が同じなら
            // 位置とウェイト・法線の部分転送、違えば 1 回だけ作り直す）。ドラッグを離したときは
            // コマンドの外なので、ここが唯一の表示の更新になる。
            // パネルへは行の再バインドだけ知らせる（ListStructure は全バッファの作り直しを伴う）。
            // Settle の後に出すこと（属性の経路は面の構成が合っているバッファを前提にする）。
            // コマンドの終わりから呼んだときは、直後のコマンドの終わりの Settle に任せ、
            // パネルへの通知はその後で 1 回だけ出す（段 8。以前は Settle が 2 回走っていた）。
            pass.Timing.Add($"前処理（行列・署名） {F1(preMs)} ms");
            pass.Timing.AddRange(timing);
            pass.Timing.Add($"見張りの計 {F1(watchTotal)} ms / ダイジェスト {F1(digestMs)} ms");

            if (displayAtCommandEnd)
            {
                _modifierPanelNotifyPending = true;
                pass.Timing.Add("パスの終わりの Settle・パネルへの通知：コマンドの終わりへ回した");
            }
            else
            {
                long tset0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int  rbSet0 = ModifierEvaluator.RefreshCount;
                _viewportManager.SettleWorldPositions(project);
                double settleMs = MsSince(tset0);
                int    settleRb = ModifierEvaluator.RefreshCount - rbSet0;
                double matchMs  = _viewportManager.LastMatchMs;
                long tn0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int  rbN0 = ModifierEvaluator.RefreshCount;
                _notifyPanels(ChangeKind.Attributes);
                double notifyMs = MsSince(tn0);
                int    notifyRb = ModifierEvaluator.RefreshCount - rbN0;
                pass.Timing.Add($"パスの終わりの Settle {F1(settleMs)} ms（照合 {F1(matchMs)} ms・バッファ作り直し {settleRb} 回）");
                pass.Timing.Add($"パネルへの通知 {F1(notifyMs)} ms（バッファ作り直し {notifyRb} 回）");
            }

            pass.RebuildCount = ModifierEvaluator.RefreshCount - rebuild0;
            pass.ElapsedMs    = MsSince(t0);
            pass.Timing.Add($"パスの中の PresentAll {PlayerViewportManager.PresentCount - present0} 回 "
                            + $"{F1(PlayerViewportManager.PresentMs - presentMs0)} ms / パネル側 "
                            + $"{F1(PlayerViewportManager.PanelNotifyMs - panelMs0)} ms");
            _passOfThisCommand = pass;
        }

        /// <summary>この一番外側のコマンドの中で流したパス（コマンドの終わりの Settle の時間を足すため。段 7）。</summary>
        private ModifierEvaluator.PassInfo _passOfThisCommand;

        /// <summary>コマンドの終わりで流したパスの、パネルへの通知を Settle の後へ回したか（段 8）。</summary>
        private bool _modifierPanelNotifyPending;

        // 一番外側のコマンドの始めの計測の控え（段 8）。
        private long   _cmdT0;
        private int    _cmdPresent0;
        private double _cmdPresentMs0;
        private double _cmdPanelMs0;

        /// <summary>一番外側のコマンドの始めに呼ぶ。計測の控えを取る（段 8）。</summary>
        private void BeginCommandTiming()
        {
            _passOfThisCommand          = null;
            _modifierPanelNotifyPending = false;
            _cmdT0         = System.Diagnostics.Stopwatch.GetTimestamp();
            _cmdPresent0   = PlayerViewportManager.PresentCount;
            _cmdPresentMs0 = PlayerViewportManager.PresentMs;
            _cmdPanelMs0   = PlayerViewportManager.PanelNotifyMs;
        }

        /// <summary>
        /// 一番外側のコマンドの終わりの SettleWorldPositions の直後に呼ぶ。
        /// パスの通知を回してあれば出し（段 8）、時間をそのコマンドで流したパスの内訳へ足す（段 7）。
        /// </summary>
        private void FinishCommandSettle(double settleMs, int rebuilds)
        {
            double notifyMs = 0;
            int    notifyRb = 0;
            bool   notified = _modifierPanelNotifyPending;
            if (_modifierPanelNotifyPending)
            {
                _modifierPanelNotifyPending = false;
                long tn0 = System.Diagnostics.Stopwatch.GetTimestamp();
                int  rb0 = ModifierEvaluator.RefreshCount;
                _notifyPanels(ChangeKind.Attributes);
                notifyMs = MsSince(tn0);
                notifyRb = ModifierEvaluator.RefreshCount - rb0;
            }

            var pass = _passOfThisCommand;
            if (pass == null) return;
            // 照合の取り分け（段 13）は診断スイッチ PLDiag.Timing が入のときだけ出す（段 15）。
            string matchSplit = Poly_Ling.Diagnostics.PLDiag.Timing
                ? $"〔集合の判定 {F1(Poly_Ling.Core.UnifiedBufferManager.LastMatchMembershipMs)}"
                  + $" / メッシュごとの照合 {F1(Poly_Ling.Core.UnifiedBufferManager.LastMatchTopologyMs)}〕"
                : "";
            pass.Timing.Add($"コマンドの終わりの Settle {F1(settleMs)} ms（照合 {F1(_viewportManager.LastMatchMs)} ms"
                            + matchSplit + $"・バッファ作り直し {rebuilds} 回）");
            if (notified)
                pass.Timing.Add($"コマンドの終わりのパネルへの通知 {F1(notifyMs)} ms（バッファ作り直し {notifyRb} 回）");
        }

        /// <summary>
        /// 一番外側のコマンドの最後（後へ回した描画準備のあと）に呼ぶ。
        /// そのコマンドで流したパスの内訳へ、コマンド全体の時間と PresentAll・パネル側の計を足す（段 8・段 9）。
        /// </summary>
        private void AppendCommandTotalTiming()
        {
            var pass = _passOfThisCommand;
            _passOfThisCommand = null;
            if (pass == null) return;
            pass.Timing.Add($"コマンド全体 {F1(MsSince(_cmdT0))} ms / PresentAll {PlayerViewportManager.PresentCount - _cmdPresent0} 回 "
                            + $"{F1(PlayerViewportManager.PresentMs - _cmdPresentMs0)} ms / パネル側 "
                            + $"{F1(PlayerViewportManager.PanelNotifyMs - _cmdPanelMs0)} ms");
            // 描画準備・通知・コマンドの区間の累計（段 13）。P: は PresentAll の中、C: はコマンドの外枠。
            // 診断スイッチ PLDiag.Timing が入のときだけ（段 15）。
            if (Poly_Ling.Diagnostics.PLDiag.Timing)
                pass.Timing.Add("区間の累計: " + PlayerViewportManager.ViewTimingText());
        }

        private static double MsSince(long t)
            => (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        private static string F1(double v)
            => v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// 照会 queryModifierGraph の結果。依存の順の項目ごとに、入力・出力・下流・状態・
        /// 要評価の印・最後の評価の結果を並べる。モデルは変えない。
        /// </summary>
        private static string BuildModifierGraphData(ModelContext model)
        {
            var order = ModifierEvaluator.GetGraph(model);

            var labels   = new List<string>(order.Count);
            var actions  = new List<string>(order.Count);
            var inputs   = new List<string>(order.Count);
            var outputs  = new List<string>(order.Count);
            var downs    = new List<string>(order.Count);
            var statuses = new List<string>(order.Count);
            var reasons  = new List<string>(order.Count);
            var spaces   = new List<string>(order.Count);
            var dirty    = new List<int>(order.Count);
            var pending  = new List<int>(order.Count);
            var results  = new List<string>(order.Count);
            var messages = new List<string>(order.Count);

            string Names(IEnumerable<MeshContext> list)
            {
                var parts = new List<string>();
                foreach (var mc in list) parts.Add($"{mc.Name}({mc.ObjectId})");
                return string.Join(",", parts);
            }

            foreach (var e in order)
            {
                labels.Add(e.Label);
                actions.Add(e.ActionLabel);

                var ins = new List<MeshContext>();
                foreach (var kv in e.Inputs) if (!ins.Contains(kv.Value)) ins.Add(kv.Value);
                inputs.Add(Names(ins));
                outputs.Add(Names(e.Outputs));

                var ds = new List<string>();
                foreach (var d in e.Downstream) ds.Add(d.Order.ToString(System.Globalization.CultureInfo.InvariantCulture));
                downs.Add(string.Join(",", ds));

                statuses.Add(e.Status.ToString());
                reasons.Add(e.StatusReason ?? "");
                spaces.Add(ModifierSpace.Label(e.Space)
                           + (string.IsNullOrEmpty(e.SpaceReason) ? "" : $"（{e.SpaceReason}）"));

                ModifierEvaluator.GetEntryState(model, e, out bool dt, out var res, out string msg);
                dirty.Add(dt ? 1 : 0);
                pending.Add(ModifierEvaluator.IsOutputEditPending(model, e) ? 1 : 0);
                results.Add(res.ToString());
                messages.Add(msg ?? "");
            }

            var pass = ModifierEvaluator.LastPass(model);
            string passText = pass == null
                ? ""
                : $"#{pass.Sequence} {pass.Trigger}: 作り直し {pass.Ran} / 失敗 {pass.Failed} / 止めた {pass.Blocked}"
                  + $" / バッファ作り直し {pass.RebuildCount} 回 / {pass.ElapsedMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} ms"
                  + (pass.Lines.Count > 0 ? "\n" + string.Join("\n", pass.Lines) : "")
                  + (pass.Timing.Count > 0 ? "\n内訳:\n" + string.Join("\n", pass.Timing) : "");

            return CommandDataJson.New()
                .Int  ("entries",    order.Count)
                .Texts("labels",     labels)
                .Texts("actions",    actions)
                .Texts("inputs",     inputs)
                .Texts("outputs",    outputs)
                .Texts("downstream", downs)
                .Texts("statuses",   statuses)
                .Texts("reasons",    reasons)
                .Texts("spaces",     spaces)
                .Ints ("dirty",      dirty)
                .Ints ("pending",    pending)
                .Texts("results",    results)
                .Texts("messages",   messages)
                .Text ("lastPass",   passText)
                .Text ("structureKey", ModifierGraph.StructureKey(model).ToString("X16", System.Globalization.CultureInfo.InvariantCulture))
                .Build();
        }

        private static string JoinNotes(string a, string b)
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrEmpty(a)) parts.Add(a);
            if (!string.IsNullOrEmpty(b)) parts.Add(b);
            return string.Join(" / ", parts);
        }
    }
}
