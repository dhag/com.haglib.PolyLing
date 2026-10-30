// PlayerCommandDispatcher.ModifierEval.cs
// コマンドディスパッチャ：オブジェクトグループの項目（モディファイア）を依存の順に流す。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【何をするか】（PolyLing_作り直し整備計画.md 2.4・段 4）
//   評価器（ModifierEvaluator）がきっかけ 2 か所で立てた要評価の印を見て、
//   処理器の無い項目を依存の順に作り直す。1 項目は RunObjectGroupStep（出力先へ中身だけ書き戻す）。
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
//   出力の保護は RunObjectGroupStep が _modifierWriteBackDepth で外す。

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
        /// グループの AutoUpdate の値は見ない（評価器は入力に応じて流す）。
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
        private void RunModifierEvaluation(string trigger, bool recheckInputs = false)
        {
            if (ModifierEvaluator.IsEvaluating) return;

            var project = _getProject();
            var model   = project?.CurrentModel;
            if (project == null || model == null) return;
            int modelIndex = project.CurrentModelIndex;

            if (recheckInputs) ModifierEvaluator.MarkChangedInputs(model);

            // モデル空間で読む入力の行列（段 10）。BoneTransform・親・オブジェクト移動のように
            // 頂点の格納値を変えずにバインド姿勢を変える操作は、きっかけ 2 か所の位置の署名では拾えない。
            model.ComputeWorldMatrices();
            ModifierEvaluator.MarkMatrixChanged(model);

            ModifierEvaluator.PassInfo pass;
            _undoController?.SuspendRecording();
            try
            {
                pass = ModifierEvaluator.EvaluatePending(model,
                    (ModifierEvaluator.Entry e, out string err, out string note) =>
                    {
                        note = null;
                        // 見張り（段 D）：書くと宣言していない入力が変わったら宣言漏れ。
                        var readOnly = ModifierEvaluator.SnapshotReadOnlyInputs(e);
                        bool ok = RunObjectGroupStep(project, model, modelIndex, false, e.Group, e.StepIndex, out err);
                        string undeclared = ModifierEvaluator.UndeclaredWrites(e, readOnly);
                        if (undeclared != null)
                            Debug.LogError($"[ModifierWrite] {e.Label} {e.Step?.Action} が、書くと宣言していない入力を書き換えた：{undeclared}"
                                           + "（PLParam の MeshRefAccess・WriteWhen を実際の書き込みに合わせる）");
                        if (ok)
                        {
                            e.Group.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, e.Group);
                            note = JoinNotes(ObjectGroupOps.LastBeltMessage, ObjectGroupOps.LastProfileMessage);
                        }
                        return ok;
                    },
                    trigger);
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            if (pass.Ran + pass.Failed == 0) return;

            if (pass.Failed > 0)
                Debug.LogWarning("[ObjectGroup] 依存の評価で失敗した項目があります（" + trigger + "）:\n"
                                 + string.Join("\n", pass.Lines));

            model.ComputeWorldMatrices();
            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);
        }

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
                actions.Add(e.Step.Action ?? "");

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
                  + (pass.Lines.Count > 0 ? "\n" + string.Join("\n", pass.Lines) : "");

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
