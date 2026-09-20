// Remote/RemoteOwnership.cs
// 協働編集（グループワーク）のための所有権判定。
//
// 【真値の所在】
//   「誰が作業中か」は MeshContext.EditorName が単一の真値。
//   担当は作業中だけの一時ロックで、プロジェクト保存にも Undo にも含めない
//   （操作経路統一計画.md L-1）。期限の控え（最終操作時刻）はこのクラスが実行時に持つ。
//
// 【判定規則】
//   書き込み可 ⟺ EditorName が空（担当者なし） または EditorName == 要求者名
//   担当者なしの編集を禁止したい運用では AllowUnownedEdit=false にする。
//
// 【ロックが外れるとき】
//   本人の解放、LockTimeoutSeconds 秒操作が無いとき（L-2）、リモートの切断（L-4）、
//   ホストの強制解放。MCP は書き込み先を自動でロックする（L-3）。
//
// 【ズレ検出】
//   クライアントは masterIndices と対にして objectIds（安定ID）を送る。
//   サーバは「その位置に本当にそのIDのオブジェクトがあるか」を照合し、
//   食い違えばコマンド全体を拒否して再取得を促す。
//   これが無いと、他人の追加/削除/並べ替え直後に届いた古いビュー由来の
//   インデックスが別オブジェクトへ適用されてしまう。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Remote
{
    /// <summary>認可結果。</summary>
    public struct OwnershipVerdict
    {
        public bool   Allowed;
        /// <summary>拒否理由（クライアントへ返すメッセージ）。</summary>
        public string Reason;
        /// <summary>リスト構造のズレを検出した（クライアントに再取得させるべき）。</summary>
        public bool   StaleView;

        public static OwnershipVerdict Ok => new OwnershipVerdict { Allowed = true };

        public static OwnershipVerdict Deny(string reason)
            => new OwnershipVerdict { Allowed = false, Reason = reason };

        public static OwnershipVerdict Stale(string reason)
            => new OwnershipVerdict { Allowed = false, Reason = reason, StaleView = true };
    }

    /// <summary>
    /// PanelCommand が触るオブジェクトを解決し、要求者が編集してよいかを判定する。
    /// 状態を持たない純関数の集まり。
    /// </summary>
    public static class RemoteOwnership
    {
        // ================================================================
        // 設定
        // ================================================================

        /// <summary>
        /// 担当者が設定されていないオブジェクトを誰でも編集できるか。
        /// false にすると claim 必須の厳格運用になる。
        /// </summary>
        public static bool AllowUnownedEdit = true;

        // ================================================================
        // 一時ロックの期限（操作経路統一計画.md L-2）
        // ================================================================

        /// <summary>
        /// 担当（EditorName）を一時ロックとして保つ秒数。
        /// ロックを持つ本人の操作があるたびに延び、この秒数操作が無ければ外れる。
        /// </summary>
        public static float LockTimeoutSeconds = 10f;

        /// <summary>ObjectId → ロックの最終操作時刻。保存しない実行時の控え。</summary>
        private static readonly Dictionary<ulong, DateTime> _lockActivity = new Dictionary<ulong, DateTime>();

        // ================================================================
        // プレビュー中のロック（操作経路統一計画.md H-1）
        // ================================================================

        /// <summary>プレビュー中の対象（期限で外さない）。</summary>
        private static readonly HashSet<ulong> _previewHeld = new HashSet<ulong>();

        /// <summary>プレビューのために新たに掛けたロック（終了時に外す）と、その名前。</summary>
        private static readonly Dictionary<ulong, string> _previewClaimed = new Dictionary<ulong, string>();

        /// <summary>
        /// プレビューを始める前に、対象（ミラー側を含む）へ書き込めるかを判定し、
        /// 通ればプレビューが終わるまで操作者のロックにしておく。
        ///
        /// 【何のために要るか】
        ///   プレビューはコマンドを通らずに対象を直接動かし、確定時に索引で書き戻す。
        ///   その間に他の操作者が同じ対象を変えると上書き・取り違えが起きる（8.5）。
        ///   開始時に判定してロックを保持すれば、他の操作者の書き込みは判定で止まる。
        /// </summary>
        /// <returns>始めてよいか。false のとき reason に理由が入る。</returns>
        public static bool TryBeginPreview(
            ProjectContext project, int modelIndex, IList<int> targets, CommandActor actor, out string reason)
        {
            reason = null;
            if (project == null || actor == null || targets == null || targets.Count == 0) return true;
            var model = GetModel(project, modelIndex);
            if (model == null) return true;

            var list = MirrorBranchOps.CollectMirrorCaptureIndices(model, new List<int>(targets));
            var blocked = new List<string>();
            foreach (int idx in list)
            {
                var mc = GetMesh(model, idx);
                if (mc == null) continue;
                if (!AllowUnownedEdit && !mc.HasEditor) { blocked.Add($"{mc.Name}（担当者未設定）"); continue; }
                if (!mc.IsEditableBy(actor.Name)) blocked.Add($"{mc.Name}（担当: {mc.EditorName}）");
            }
            if (blocked.Count > 0)
            {
                reason = "編集できません → " + Join(blocked, 3);
                return false;
            }

            if (string.IsNullOrEmpty(actor.Name)) return true;
            foreach (int idx in list)
            {
                var mc = GetMesh(model, idx);
                if (mc == null) continue;
                if (!mc.HasEditor)
                {
                    mc.EditorName = actor.Name;
                    _previewClaimed[mc.ObjectId] = actor.Name;
                }
                _previewHeld.Add(mc.ObjectId);
                TouchLock(mc);
            }
            return true;
        }

        /// <summary>
        /// プレビューを終える。プレビューのために掛けたロックを外し、
        /// 元から持っていたロックは期限の数え直しから再開する。
        /// </summary>
        public static void EndPreview(ProjectContext project)
        {
            if (project != null)
            {
                for (int mi = 0; mi < project.ModelCount; mi++)
                {
                    var model = project.GetModel(mi);
                    if (model == null) continue;
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || !_previewHeld.Contains(mc.ObjectId)) continue;
                        if (_previewClaimed.TryGetValue(mc.ObjectId, out var name) &&
                            string.Equals(mc.EditorName, name, StringComparison.Ordinal))
                        {
                            mc.EditorName = "";
                            _lockActivity.Remove(mc.ObjectId);
                        }
                        else
                        {
                            TouchLock(mc);
                        }
                    }
                }
            }
            _previewHeld.Clear();
            _previewClaimed.Clear();
        }

        /// <summary>ロックの最終操作時刻を今にする（期限を延ばす）。</summary>
        public static void TouchLock(MeshContext mc)
        {
            if (mc == null || !mc.HasEditor) return;
            _lockActivity[mc.ObjectId] = DateTime.UtcNow;
        }

        /// <summary>
        /// 期限切れのロックを外す。外したら true。
        /// 最終操作時刻の控えが無いロック（控えを取る前に掛かったもの）は、今から期限を数える。
        /// </summary>
        public static bool ReleaseExpiredLocks(ProjectContext project)
        {
            if (project == null) return false;
            var now = DateTime.UtcNow;
            var limit = TimeSpan.FromSeconds(Math.Max(0.1f, LockTimeoutSeconds));
            bool released = false;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.GetModel(mi);
                if (model == null) continue;
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || !mc.HasEditor) continue;

                    if (!_lockActivity.TryGetValue(mc.ObjectId, out var last))
                    {
                        _lockActivity[mc.ObjectId] = now;
                        continue;
                    }
                    // プレビュー中は期限で外さない（H-1）。
                    if (_previewHeld.Contains(mc.ObjectId)) continue;
                    if (now - last < limit) continue;

                    mc.EditorName = "";
                    _lockActivity.Remove(mc.ObjectId);
                    released = true;
                }
            }
            return released;
        }

        /// <summary>指定した名前のロックをすべて外す（切断時）。外したら true。</summary>
        public static bool ReleaseLocksOf(ProjectContext project, string editorName)
        {
            if (project == null || string.IsNullOrEmpty(editorName)) return false;
            bool released = false;
            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.GetModel(mi);
                if (model == null) continue;
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || !string.Equals(mc.EditorName, editorName, StringComparison.Ordinal)) continue;
                    mc.EditorName = "";
                    _lockActivity.Remove(mc.ObjectId);
                    released = true;
                }
            }
            return released;
        }

        /// <summary>
        /// 実行が済んだコマンドの書き込み先について、操作者のロックを延ばす。
        /// autoClaim が true なら、担当者なしの書き込み先を操作者のロックにする（MCP 用、L-3）。
        /// </summary>
        public static void RenewLocksAfter(ProjectContext project, PanelCommand cmd, CommandActor actor, bool autoClaim)
        {
            if (project == null || cmd == null || actor == null || string.IsNullOrEmpty(actor.Name)) return;
            if (WriteScopeOf(cmd) != PLWriteScope.Targets) return;
            if (!TryCollectWriteTargets(project, cmd, out _, out var byModel)) return;

            bool withMirror = WritesMirrorSide(cmd);
            foreach (var kv in byModel)
            {
                var m = kv.Key;
                var list = withMirror ? MirrorBranchOps.CollectMirrorCaptureIndices(m, kv.Value) : kv.Value;
                foreach (int idx in list)
                {
                    var mc = GetMesh(m, idx);
                    if (mc == null) continue;
                    if (!mc.HasEditor && autoClaim) mc.EditorName = actor.Name;
                    if (string.Equals(mc.EditorName, actor.Name, StringComparison.Ordinal))
                        TouchLock(mc);
                }
            }
        }

        /// <summary>
        /// ユーザー名未登録（register で userName を送っていない）クライアントの
        /// 書き込みを許可するか。false 推奨（名無しの編集は追跡できないため）。
        /// </summary>
        public static bool AllowAnonymousEdit = false;

        // ================================================================
        // 本体
        // ================================================================

        /// <summary>
        /// コマンドの実行可否を判定する。
        /// </summary>
        /// <param name="project">サーバが保持する権威プロジェクト</param>
        /// <param name="cmd">実行しようとしているコマンド</param>
        /// <param name="actor">操作者。名前（空＝名無し）と種別、封筒の安定 ID を持つ</param>
        /// <param name="verifyIds">
        /// 安定 ID の照合をするか。一番外側のコマンドだけ true にする
        /// （入れ子のコマンドには封筒の ID が対応しない）。
        /// </param>
        public static OwnershipVerdict TryAuthorize(
            ProjectContext project, PanelCommand cmd, CommandActor actor, bool verifyIds = true)
        {
            if (cmd == null) return OwnershipVerdict.Deny("コマンドがありません");
            if (actor == null) return OwnershipVerdict.Deny("操作者がありません");

            string requesterName = actor.Name;
            ulong[] objectIds = verifyIds ? actor.ObjectIds : null;
            bool isRemote = actor.Kind == CommandActorKind.Remote;

            // UI 自動操作はホストの画面を動かす。リモートの参加者からは受けない。
            if (isRemote && IsUiAutomation(cmd))
                return OwnershipVerdict.Deny("UI 自動操作はリモート接続からは実行できません");

            // 手本（シナリオ）の置き場はホストの持ち物で、モデルにも属さない。
            // リモートの参加者が書き換える筋合いがないので同じく受けない。
            if (isRemote && IsScenario(cmd))
                return OwnershipVerdict.Deny("手本の操作はリモート接続からは実行できません");

            // 編集者の設定・解放そのものは専用判定へ
            if (cmd is SetObjectEditorCommand sec)
                return AuthorizeSetEditor(project, sec, actor, objectIds);

            // 書き込み範囲はコマンド定義の属性（PLCommand.Writes）から引く。
            // コマンド種別ごとの手書き表は持たない（PanelCommandMeshRefs.cs 冒頭と同じ方針）。
            var scope = WriteScopeOf(cmd);

            // モデルを書き換えないものは素通し。
            if (scope == PLWriteScope.None) return OwnershipVerdict.Ok;

            if (string.IsNullOrEmpty(requesterName) && !AllowAnonymousEdit)
                return OwnershipVerdict.Deny(
                    "ユーザー名が未登録です。名前を設定して接続し直してください。");

            var model = GetModel(project, cmd.ModelIndex);
            if (model == null) return OwnershipVerdict.Deny($"モデルがありません: {cmd.ModelIndex}");

            // 既存オブジェクトを書き換えず新規に足すだけのものは担当と無関係。
            if (scope == PLWriteScope.AddOnly) return OwnershipVerdict.Ok;

            // 対象を引数から特定できないもの（未宣言を含む）は
            // 「そのモデルに他人の担当が1つでもあれば拒否」の保守的判定にする。
            if (scope != PLWriteScope.Targets)
                return AuthorizeModelWide(model, cmd, requesterName);

            if (!TryCollectWriteTargets(project, cmd, out var primary, out var byModel))
                return AuthorizeModelWide(model, cmd, requesterName);

            if (byModel.Count == 0) return OwnershipVerdict.Ok;

            // 安定IDの照合（ズレ検出）。クライアントは主モデルの対象（ミラー追加前）の並びで送る。
            var stale = VerifyObjectIds(model, primary.ToArray(), objectIds);
            if (!stale.Allowed) return stale;

            // 担当者チェック。ミラー側も書き換わるコマンドはミラー側を含める。
            bool withMirror = WritesMirrorSide(cmd);
            var blocked = new List<string>();
            foreach (var kv in byModel)
            {
                var m = kv.Key;
                var checkList = withMirror
                    ? MirrorBranchOps.CollectMirrorCaptureIndices(m, kv.Value)
                    : kv.Value;
                foreach (int idx in checkList)
                {
                    var mc = GetMesh(m, idx);
                    if (mc == null) continue;

                    if (!AllowUnownedEdit && !mc.HasEditor)
                    {
                        blocked.Add($"{mc.Name}（担当者未設定）");
                        continue;
                    }
                    if (!mc.IsEditableBy(requesterName))
                        blocked.Add($"{mc.Name}（担当: {mc.EditorName}）");
                }
            }

            if (blocked.Count > 0)
                return OwnershipVerdict.Deny("編集できません → " + Join(blocked, 3));

            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // 編集者の設定・解放の判定
        // ================================================================

        private static OwnershipVerdict AuthorizeSetEditor(
            ProjectContext project, SetObjectEditorCommand cmd, CommandActor actor, ulong[] objectIds)
        {
            // ホストは担当の割り当て・強制解放を行う管理者。規則を掛けない
            // （ディスパッチャは force で適用する：PlayerCommandDispatcher.MeshAttributes.cs）。
            if (actor.Kind == CommandActorKind.Host) return OwnershipVerdict.Ok;

            string requesterName = actor.Name;
            if (string.IsNullOrEmpty(requesterName) && !AllowAnonymousEdit)
                return OwnershipVerdict.Deny(
                    "ユーザー名が未登録です。名前を設定して接続し直してください。");

            var model = GetModel(project, cmd.ModelIndex);
            if (model == null) return OwnershipVerdict.Deny($"モデルがありません: {cmd.ModelIndex}");

            // 強制上書きはホストだけ
            if (cmd.Force)
                return OwnershipVerdict.Deny("強制解放はホスト側でのみ実行できます。");

            // 自分以外の名前を勝手に設定させない（解放は "" なので対象外）
            if (cmd.EditorName.Length > 0 &&
                !string.Equals(cmd.EditorName, requesterName, StringComparison.Ordinal))
                return OwnershipVerdict.Deny("他のユーザー名を設定することはできません。");

            var ids = objectIds ?? cmd.ObjectIds;
            var stale = VerifyObjectIds(model, cmd.MasterIndices, ids);
            if (!stale.Allowed) return stale;

            // 取得は「担当者なし」または「既に自分」のときのみ。
            // 解放は「自分が担当」のときのみ。
            var blocked = new List<string>();
            foreach (int idx in cmd.MasterIndices)
            {
                var mc = GetMesh(model, idx);
                if (mc == null) continue;
                if (!mc.IsEditableBy(requesterName))
                    blocked.Add($"{mc.Name}（担当: {mc.EditorName}）");
            }

            if (blocked.Count > 0)
                return OwnershipVerdict.Deny(
                    (cmd.EditorName.Length == 0 ? "解放できません → " : "取得できません → ")
                    + Join(blocked, 3));

            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // 安定IDの照合
        // ================================================================

        /// <summary>
        /// masterIndices[i] の位置にあるオブジェクトの ObjectId が
        /// objectIds[i] と一致するかを確認する。
        /// objectIds が null / 該当要素が 0 の場合は照合しない（旧クライアント互換）。
        /// </summary>
        public static OwnershipVerdict VerifyObjectIds(
            ModelContext model, int[] masterIndices, ulong[] objectIds)
        {
            if (model == null || masterIndices == null || objectIds == null)
                return OwnershipVerdict.Ok;

            int n = Math.Min(masterIndices.Length, objectIds.Length);
            for (int i = 0; i < n; i++)
            {
                ulong expected = objectIds[i];
                if (expected == 0UL) continue;   // 未申告

                var mc = GetMesh(model, masterIndices[i]);
                if (mc == null || mc.ObjectId != expected)
                    return OwnershipVerdict.Stale(
                        "リスト構造が変化しています。最新の状態を取得してからやり直してください。");
            }
            return OwnershipVerdict.Ok;
        }

        // ================================================================
        // コマンド → 対象オブジェクト（MasterIndex）の解決
        // ================================================================

        /// <summary>
        /// コマンドの書き込み範囲（PLCommand.Writes）。属性が無ければ Unspecified。
        /// </summary>
        public static PLWriteScope WriteScopeOf(PanelCommand cmd)
        {
            if (cmd == null) return PLWriteScope.Unspecified;
            var a = cmd.GetType().GetCustomAttribute<PLCommandAttribute>(inherit: false);
            return a?.Writes ?? PLWriteScope.Unspecified;
        }

        /// <summary>
        /// 対象のミラー側オブジェクトも書き換えるコマンドか（PLCommand.WritesMirrorSide）。
        /// 属性が無ければ true（安全側）。
        /// </summary>
        public static bool WritesMirrorSide(PanelCommand cmd)
        {
            if (cmd == null) return true;
            var a = cmd.GetType().GetCustomAttribute<PLCommandAttribute>(inherit: false);
            return a?.WritesMirrorSide ?? true;
        }

        /// <summary>
        /// Writes = Targets のコマンドについて、書き換える対象を引数の属性から集める。
        ///
        /// 【集め方】
        ///   PLParam(IsMeshRef, MeshRefAccess = Write) の引数のうち、WriteWhen が無いもの、
        ///   または WriteWhen がこのコマンドの値で成り立つものの値を対象にする。
        ///   別モデルを指す引数（MeshRefModelKey）はそのモデルの対象として分けて持つ。
        ///
        /// 【false を返すとき（呼び出し側はモデル全体判定へ回す）】
        ///   書き込み先の値に負数（-1 = 実行時の選択・編集対象・アクティブで決まる）が
        ///   含まれるとき。引数だけでは対象を決められない。
        ///
        /// primary は主モデル（cmd.ModelIndex）の対象を引数の並び順で持つ。
        /// クライアントはこの並びで objectIds を送る（VerifyObjectIds が照合する）。
        /// </summary>
        public static bool TryCollectWriteTargets(
            ProjectContext project, PanelCommand cmd,
            out List<int> primary, out Dictionary<ModelContext, List<int>> byModel)
        {
            primary = new List<int>();
            byModel = new Dictionary<ModelContext, List<int>>();
            if (cmd == null) return false;

            var mainModel = GetModel(project, cmd.ModelIndex);
            if (mainModel == null) return false;

            var args = PanelCommandFactory.ToArgs(cmd);

            foreach (var k in PanelCommandFactory.MeshRefKeys(cmd.GetType()))
            {
                if (!PanelCommandFactory.IsWriteActive(cmd.GetType(), k, args)) continue;
                if (!args.TryGetValue(k.Key, out string raw) || string.IsNullOrEmpty(raw)) continue;

                int[] values = ParseIntCsv(raw);
                if (values == null) return false;

                int[] modelIdx = null;
                if (!string.IsNullOrEmpty(k.ModelKey))
                {
                    if (!args.TryGetValue(k.ModelKey, out string mraw)) return false;
                    modelIdx = ParseIntCsv(mraw);
                    if (modelIdx == null || modelIdx.Length != values.Length) return false;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] < 0) return false;

                    var m = modelIdx != null ? GetModel(project, modelIdx[i]) : mainModel;
                    if (m == null) return false;

                    if (!byModel.TryGetValue(m, out var list))
                    {
                        list = new List<int>();
                        byModel[m] = list;
                    }
                    if (!list.Contains(values[i])) list.Add(values[i]);
                    if (ReferenceEquals(m, mainModel) && !primary.Contains(values[i]))
                        primary.Add(values[i]);
                }
            }
            return true;
        }

        private static int[] ParseIntCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return Array.Empty<int>();
            var parts = csv.Split(',');
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r[i]))
                    return null;
            return r;
        }

        /// <summary>
        /// 対象を特定できないコマンドの保守的判定。
        /// モデル内に「他人の担当」が1つでもあれば拒否する。
        /// </summary>
        private static OwnershipVerdict AuthorizeModelWide(
            ModelContext model, PanelCommand cmd, string requesterName)
        {
            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.HasEditor) continue;
                if (!mc.IsEditableBy(requesterName))
                    return OwnershipVerdict.Deny(
                        $"このモデルには他ユーザーの担当オブジェクトがあります"
                        + $"（例: {mc.Name} → {mc.EditorName}）。"
                        + $"{cmd.GetType().Name} はモデル全体に影響するため実行できません。");
            }
            return OwnershipVerdict.Ok;
        }

        /// <summary>UI 自動操作のコマンドか（PanelCommand.UiAutomation.cs）。</summary>
        private static bool IsUiAutomation(PanelCommand cmd)
        {
            switch (cmd)
            {
                case UiDescribeCommand _:
                case UiShowPanelCommand _:
                case UiRevealCommand _:
                case UiGetValueCommand _:
                case UiSetValueCommand _:
                case UiHighlightCommand _:
                case UiCaptureCommand _:
                case UiCaptureStatusCommand _:
                case UiClickCommand _:
                case QueryUiAutomationAuditCommand _:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>手本（シナリオ）のコマンドか（PanelCommand.Scenario.cs）。</summary>
        private static bool IsScenario(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryScenariosCommand _:
                case DescribeScenarioCommand _:
                case CreateScenarioCommand _:
                case DeleteScenarioCommand _:
                case ForkScenarioCommand _:
                case SaveScenarioFromGroupCommand _:
                case SetScenarioMetaCommand _:
                case AddScenarioStepCommand _:
                case SetScenarioStepCommand _:
                case RemoveScenarioStepCommand _:
                case SetScenarioStepArgCommand _:
                case MoveScenarioStepCommand _:
                case ExpandScenarioRefCommand _:
                case RunScenarioCommand _:
                case ContinueScenarioCommand _:
                case QueryScenarioRunCommand _:
                case StopScenarioRunCommand _:
                case StartScenarioRecordingCommand _:
                case StopScenarioRecordingCommand _:
                case QueryScenarioAuditCommand _:
                    return true;
                default:
                    return false;
            }
        }

        // ================================================================
        // 担当状況のスナップショット（push 用）
        // ================================================================

        /// <summary>
        /// 現在の担当状況を JSON 配列にする。
        /// 形式: [{"id":"12345","index":3,"name":"頭","editor":"hagihara"}, ...]
        /// 担当者なしのオブジェクトは含めない。
        /// </summary>
        public static string BuildOwnershipJson(ModelContext model, int modelIndex)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("modelIndex", modelIndex);
            jb.Key("owners").BeginArray();

            if (model != null)
            {
                int count = model.MeshContextCount;
                for (int i = 0; i < count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || !mc.HasEditor) continue;
                    jb.BeginObject();
                    // ulong は JSON の数値精度を超えうるので文字列で送る
                    jb.KeyValue("id",     mc.ObjectId.ToString());
                    jb.KeyValue("index",  i);
                    jb.KeyValue("name",   mc.Name ?? "");
                    jb.KeyValue("editor", mc.EditorName ?? "");
                    jb.EndObject();
                }
            }

            jb.EndArray();
            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>担当状況の変化検出用シグネチャ（push の抑止に使う）。</summary>
        public static string BuildOwnershipSignature(ModelContext model, int modelIndex)
        {
            var sb = new StringBuilder();
            sb.Append(modelIndex).Append('|');
            if (model == null) return sb.ToString();

            int count = model.MeshContextCount;
            for (int i = 0; i < count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.HasEditor) continue;
                sb.Append(mc.ObjectId).Append(':').Append(mc.EditorName).Append(';');
            }
            return sb.ToString();
        }

        // ================================================================
        // ヘルパー
        // ================================================================


        private static ModelContext GetModel(ProjectContext project, int modelIndex)
        {
            if (project == null) return null;
            if (modelIndex < 0 || modelIndex >= project.ModelCount) return null;
            return project.Models[modelIndex];
        }

        private static MeshContext GetMesh(ModelContext model, int masterIndex)
        {
            if (model == null) return null;
            if (masterIndex < 0 || masterIndex >= model.MeshContextCount) return null;
            return model.GetMeshContext(masterIndex);
        }

        private static string Join(List<string> items, int max)
        {
            if (items == null || items.Count == 0) return "";
            var sb = new StringBuilder();
            int n = Math.Min(items.Count, max);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(items[i]);
            }
            if (items.Count > n) sb.Append($" ほか{items.Count - n}件");
            return sb.ToString();
        }

        /// <summary>CSV文字列 "1,2,3" を ulong[] に。空なら null。</summary>
        public static ulong[] ParseIdCsv(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            var parts = csv.Split(',');
            var result = new ulong[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                ulong.TryParse(parts[i].Trim(), out result[i]);
            return result;
        }

        /// <summary>ulong[] を CSV 文字列に。</summary>
        public static string ToIdCsv(ulong[] ids)
        {
            if (ids == null || ids.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ids[i]);
            }
            return sb.ToString();
        }
    }
}
