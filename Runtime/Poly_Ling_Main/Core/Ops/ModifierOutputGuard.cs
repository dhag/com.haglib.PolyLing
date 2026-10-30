// ModifierOutputGuard.cs
// オブジェクトグループの出力（モディファイアの出力）を書き換えるコマンドを、受け口で止める判定。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何のためにあるか】（PolyLing_作り直し整備計画.md 2.5・段 2）
//   出力は入力と引数から導かれるもので、手で書き換えても次の作り直しで消える。
//   書き換えたいときはグループを解除（適用）して普通のメッシュにする。
//   逆方向を持つ項目（PLModifierAttribute.Inverse）は、頂点の位置だけを動かすコマンド
//   （分類 geometry.position）を通す。編集は未確定になり、確定で入力へ書き戻す（段 6）。
//   ミラーの有無の切り替え（setMirrorEnabled）はどの出力にも通す（段 6）。
//
// 【呼ぶ所】PlayerCommandDispatcher.Dispatch の担当者判定の直後。各深さで呼ぶ。
//   作り直しの書き戻し（RunObjectGroupStep の入れ子の Dispatch）の中では呼ばない。
//   評価器（ModifierEvaluator の処理器）は Dispatch を通らずに書くので、ここに掛からない。
//
// 【判定の対象】
//   ・Writes が None・AddOnly のものは判定しない（既存オブジェクトを書かない）
//   ・分類（PLCommand.Category）がメッシュの中身を書かない族のものは判定しない（NonMeshCategory）。
//     未知の分類・分類なしは判定する側に倒す
//
// 【書き込み先の決め方】
//   ・Write 引数（WriteWhen が成り立つもの）の値で 0 以上のものはその索引
//   ・Targets で成り立つ Write 引数が 1 つも無いものは判定しない（担当者判定と同じ）
//   ・-1 の値があるとき、または Writes が ModelWide・Unspecified で引数から決まらないときは、
//     実行時の選択中の描画オブジェクト（無ければ編集対象）を書き込み先とみなす
//     （PlayerCommandDispatcher.CollectSelectedMeshContexts と同じ決め方）
//   ・作業空間の反映・取消し・閉じる（applyEditSpace 等）は、引数でも選択でもなく作業空間が
//     書く先を呼び出し側が渡す（DenyReason の explicitTargets。段 8）
//   書き込み先がどれかのグループの実行項目の出力なら拒否する。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class ModifierOutputGuard
    {
        /// <summary>頂点の位置だけを動かすコマンドの分類（逆方向を持つ項目の出力に掛けてよい）。</summary>
        public const string PositionCategory = "geometry.position";

        /// <summary>
        /// メッシュの中身を書かない分類。完全一致か、末尾が "." の項目は前方一致で見る。
        /// ここに無い分類（geometry.* / uv / linegroup / mirror / rig.skinning / query.data /
        /// io.import / 分類なし など）は判定する。
        /// rig.skeleton.pose（姿勢の焼き込み）は判定しない。出力の頂点は書かず、バインド姿勢の変化の
        /// 検知は段 6 以降へ持ち越した（作り直し整備計画 段 5）。
        /// </summary>
        private static readonly string[] NonMeshCategory =
        {
            "undo",
            "object.",
            "selection",
            "selection.",
            "material",
            "vrm",
            "ui",
            "workaxis",
            "morph",
            "dynamics.spring",
            "rig.skeleton",
            "rig.skeleton.",
            "camera",
            "query",
            "mcp",
            "scenario",
            "scenario.",
            "io.project",
            "io.export",
            "remote",
            "animation",
            "tool",
            "underlay",
            "external.",
        };

        /// <summary>
        /// 拒否する理由。書いてよければ null。
        /// </summary>
        public static string DenyReason(ProjectContext project, PanelCommand cmd)
            => DenyReason(project, cmd, null);

        /// <summary>
        /// 拒否する理由。書いてよければ null。
        /// explicitTargets を渡すと、引数・選択からではなくそれを書き込み先とみなす
        /// （作業空間の反映など、書く先がコマンドの引数でも選択でもないもの。段 8）。
        /// 空の列は「既存の描画オブジェクトを書かない」。
        /// </summary>
        public static string DenyReason(ProjectContext project, PanelCommand cmd, IList<MeshContext> explicitTargets)
        {
            if (project == null || cmd == null) return null;

            var t    = cmd.GetType();
            var attr = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
            var scope = attr?.Writes ?? PLWriteScope.Unspecified;
            if (scope == PLWriteScope.None || scope == PLWriteScope.AddOnly) return null;
            if (IsNonMeshCategory(attr?.Category)) return null;

            // 種別の変換（MeshFilter ⇔ スキンド）は出力にも掛けてよい（段 5）。
            // 形は変えずに格納空間だけを変え、評価器はスキンド化のあとの空間で作り直す（ModifierSpace）。
            // 塗りなど同じ分類（rig.skinning）のほかのコマンドは判定する（作り直しで上書きされるため）。
            if (t == typeof(ConvertMeshFilterToSkinnedCommand)
             || t == typeof(ConvertToSkinnedCommand)
             || t == typeof(ConvertToMeshFilterCommand)) return null;

            // ミラーの有無の切り替えは出力にも掛けてよい（段 6）。出力の頂点は書かず、ミラー側の
            // オブジェクトを作る／片付けるだけで、作り直しのたびにミラー側も実体側から作り直される
            // （PrimitiveMeshReplaceExisting → ReplaceMirrorSideContents）。
            if (t == typeof(SetMirrorEnabledCommand)) return null;

            // 頂点の位置だけを動かすコマンドは、逆方向を持つ項目の出力に掛けてよい（段 6）。
            // 編集は「未確定」になり、確定（commitModifierOutputEdit）で入力へ書き戻す。
            bool positionOnly = string.Equals(attr?.Category, PositionCategory, StringComparison.Ordinal);

            var mainModel = GetModel(project, cmd.ModelIndex);
            if (mainModel == null) return null;

            // モデルにグループの出力が 1 つも無ければ何もしない（大半のコマンドはここで抜ける）。
            if (!HasAnyOutput(project)) return null;

            var targets = new List<KeyValuePair<ModelContext, MeshContext>>();
            if (explicitTargets != null)
            {
                foreach (var mc in explicitTargets)
                    if (mc != null) targets.Add(new KeyValuePair<ModelContext, MeshContext>(mainModel, mc));
                return CheckTargets(targets, positionOnly);
            }

            bool useSelection = scope != PLWriteScope.Targets;
            bool anyDeclared  = false;

            var args = PanelCommandFactory.ToArgs(cmd);
            foreach (var k in PanelCommandFactory.MeshRefKeys(t))
            {
                if (!PanelCommandFactory.IsWriteActive(t, k, args)) continue;
                anyDeclared = true;
                if (!args.TryGetValue(k.Key, out string raw) || string.IsNullOrEmpty(raw))
                {
                    useSelection = true;
                    continue;
                }

                int[] values = ParseIntCsv(raw);
                if (values == null) { useSelection = true; continue; }

                int[] modelIdx = null;
                if (k.HasModelKey && args.TryGetValue(k.ModelKey, out string mraw))
                {
                    modelIdx = ParseIntCsv(mraw);
                    if (modelIdx != null && modelIdx.Length != values.Length) modelIdx = null;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] < 0) { useSelection = true; continue; }
                    var m  = modelIdx != null ? GetModel(project, modelIdx[i]) : mainModel;
                    var mc = m?.GetMeshContext(values[i]);
                    if (mc != null) targets.Add(new KeyValuePair<ModelContext, MeshContext>(m, mc));
                }
            }

            // ModelWide・Unspecified で、Write 引数から 1 つも決まらなかったものは選択で見る。
            // Write 引数で決まったものがあれば（setRawData の masterIndex など）それだけを見る。
            // Targets で書き込み先の Write 引数が 1 つも成り立たないもの（新規に作るモードの生成など）は
            // 既存オブジェクトを書かないので、選択では見ない（担当者判定 RemoteOwnership.TryAuthorize と同じ）。
            if (scope != PLWriteScope.Targets && anyDeclared && targets.Count > 0) useSelection = false;

            if (useSelection) AddSelection(mainModel, targets);

            return CheckTargets(targets, positionOnly);
        }

        /// <summary>書き込み先のどれかがグループの出力なら拒否の理由。</summary>
        private static string CheckTargets(List<KeyValuePair<ModelContext, MeshContext>> targets, bool positionOnly)
        {
            foreach (var kv in targets)
            {
                var mc = kv.Value;
                if (mc.ObjectId == 0UL) continue;
                if (!ModifierEvaluator.TryFindOutputOwner(kv.Key, mc.ObjectId, out var owner)) continue;

                string group  = owner.Group?.Name ?? "";
                string action = owner.Step?.Action ?? "";
                bool hasInverse = ModifierInverse.HasInverse(PanelCommandFactory.ResolveType(action));
                if (hasInverse && positionOnly) continue;

                if (hasInverse)
                    return $"出力は位置の編集だけができます → {mc.Name}（グループ「{group}」の項目 {owner.StepIndex} {action} の出力。"
                         + "逆方向で入力へ戻せるのは頂点の位置の編集だけ）。面の追加・削除などはグループを解除（適用）してから行ってください。";
                return $"出力は編集できません → {mc.Name}（グループ「{group}」の項目 {owner.StepIndex} {action} の出力）。"
                     + "編集するにはグループを解除（適用）してください。";
            }
            return null;
        }

        private static bool IsNonMeshCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return false;
            foreach (var c in NonMeshCategory)
            {
                if (c.EndsWith(".", StringComparison.Ordinal))
                {
                    if (category.StartsWith(c, StringComparison.Ordinal)) return true;
                }
                else if (string.Equals(category, c, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool HasAnyOutput(ProjectContext project)
        {
            for (int m = 0; m < project.ModelCount; m++)
            {
                var model = project.GetModel(m);
                if (model?.ObjectGroups == null) continue;
                foreach (var g in model.ObjectGroups)
                {
                    if (g?.Steps == null) continue;
                    foreach (var s in g.Steps)
                        if (s != null && s.IsExecutable && s.OutputObjectIds != null && s.OutputObjectIds.Count > 0)
                            return true;
                }
            }
            return false;
        }

        /// <summary>選択中の描画オブジェクト。無ければ編集対象。</summary>
        private static void AddSelection(ModelContext model, List<KeyValuePair<ModelContext, MeshContext>> into)
        {
            bool any = false;
            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                into.Add(new KeyValuePair<ModelContext, MeshContext>(model, mc));
                any = true;
            }
            if (!any)
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) into.Add(new KeyValuePair<ModelContext, MeshContext>(model, mc));
            }
        }

        private static ModelContext GetModel(ProjectContext project, int index)
            => (index >= 0 && index < project.ModelCount) ? project.GetModel(index) : null;

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
    }
}
