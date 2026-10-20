// PlayerCommandDispatcher.LatticeTower.cs
// コマンドディスパッチャ：鉄塔（空の親＋塔体・デッキの子）、デッキの後付け、
// グループの項目の配置元の差し替え（デッキの取り替え）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【鉄塔】
//   魔法陣と同じ ExecuteEmptyParentAssembly を通す。先頭の部品は塔体であること。
//
// 【デッキの後付け】
//   指したオブジェクトから鉄塔のグループ（項目 0 が createLatticeTower）を探す。
//   指したものがグループのどれかの項目の出力先か、項目 0 の出力先（塔体）の親であれば、
//   そのグループの鉄塔とみなす。デッキは塔体の親の子として作り、グループの末尾へ足す。
//
// 【配置元の差し替え】
//   指したオブジェクトを出力先に持つ項目を探す。その項目の索引の引数のうち、
//   placement 以外で 0 でない ObjectId を控えているものがちょうど 1 つなら、
//   それを新しい配置元の ObjectId へ差し替え、その項目だけ作り直す（RunObjectGroupStep）。
//
// 【Undo】
//   どれもオブジェクト一覧のスナップショットとグループの変更を 1 件にまとめる。

using System;
using System.Collections.Generic;
using System.Globalization;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>塔体の action 名。鉄塔のグループの見分けに使う。</summary>
        private static string TowerBodyAction
            => PanelCommandFactory.ActionOf(typeof(CreateLatticeTowerCommand));

        /// <summary>
        /// DispatchCore の分担：鉄塔。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchLatticeTower(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case CreateTowerAssemblyCommand c:
                {
                    if (c.Elements == null || c.Elements.Length == 0) { Fail("部品が 1 つもありません"); return true; }
                    string head = c.Elements[0] ?? "";
                    int semi = head.IndexOf(';');
                    string action = Uri.UnescapeDataString((semi >= 0 ? head.Substring(0, semi) : head).Trim());
                    if (!string.Equals(action, TowerBodyAction, StringComparison.OrdinalIgnoreCase))
                    { Fail($"先頭の部品は {TowerBodyAction} にしてください（{action}）"); return true; }

                    int modelIndex = c.ModelIndex;
                    string why = EnsureModelForAssembly(ref project, ref model, ref modelIndex);
                    if (why != null) { Fail(why); return true; }

                    string reason = ExecuteEmptyParentAssembly(
                        project, model, modelIndex, c.Name, "Tower",
                        c.Position, c.Rotation, c.Scale, c.Elements, "鉄塔", out _);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case AddTowerDeckCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteAddTowerDeck(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case ReplaceObjectGroupSourceCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteReplaceGroupSource(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // デッキの後付け
        // ================================================================

        private string ExecuteAddTowerDeck(ProjectContext project, ModelContext model, AddTowerDeckCommand c)
        {
            if (OnCreatePrimitiveMesh == null) return "primitive mesh handler not wired";
            if (c.SourceMasterIndices == null || c.SourceMasterIndices.Length == 0) return "配置元が選ばれていません";

            var picked = model.GetMeshContext(c.TowerIndex);
            if (picked == null) return $"オブジェクトがありません（索引 {c.TowerIndex}）";

            if (!FindTowerGroup(project, model, picked, out var g, out var parent))
                return $"「{picked.Name}」を含む鉄塔のグループが見つかりません";

            int gIndex = model.ObjectGroups.IndexOf(g);
            var oldSnapshot = g.Clone();

            var prm = c.Params;
            if (string.IsNullOrEmpty(prm.MeshName)) prm.MeshName = parent.Name + "_Deck";
            var deckCmd = new CreateTowerDeckCommand(
                c.ModelIndex, prm, c.SourceMasterIndices,
                MagicCircleElementCodec.ChildPlacement(c.MaterialIndex, false));

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);

            MeshContext child = null;
            string fail = null;

            _undoController?.SuspendRecording();
            try
            {
                child = CreateChildPart(model, parent, deckCmd, out string r);
                if (child == null) fail = $"デッキ: {r}";
                else ObjectGroupOps.AppendStep(project, c.ModelIndex, g, deckCmd, new List<ulong> { child.ObjectId });
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount,
                new List<ObjectGroupChangeRecord>
                {
                    new ObjectGroupChangeRecord { ReplacedIndex = gIndex, OldGroup = oldSnapshot, NewGroup = g.Clone() },
                },
                "鉄塔のデッキ追加");

            FinishGroupEdit(project, model);
            if (fail != null) return fail;

            int ci = model.MeshContextList.IndexOf(child);
            ReportTargets(new[] { ci }, child.ObjectId != 0UL ? new[] { child.ObjectId } : null);
            return null;
        }

        /// <summary>
        /// picked を含む鉄塔のグループを探す。parent は塔体の親（空の親）。
        /// picked が項目の出力先か、塔体の親そのものなら見つかったとする。
        /// </summary>
        private static bool FindTowerGroup(
            ProjectContext project, ModelContext model, MeshContext picked,
            out Poly_Ling.Data.ObjectGroup group, out MeshContext parent)
        {
            group  = null;
            parent = null;
            if (model?.ObjectGroups == null) return false;

            foreach (var g in model.ObjectGroups)
            {
                if (g == null || g.StepCount == 0) continue;
                var s0 = g.GetStep(0);
                if (s0 == null || !string.Equals(s0.Action, TowerBodyAction, StringComparison.OrdinalIgnoreCase)) continue;

                var body = ObjectGroupOps.Resolve(project, s0.FirstOutputId);
                if (body == null) continue;
                int pi = body.HierarchyParentIndex;
                var par = pi >= 0 ? model.GetMeshContext(pi) : null;
                if (par == null) continue;

                bool hit = ReferenceEquals(picked, par);
                for (int si = 0; !hit && si < g.StepCount; si++)
                    hit = g.GetStep(si)?.ContainsOutput(picked.ObjectId) ?? false;

                if (!hit) continue;
                group  = g;
                parent = par;
                return true;
            }
            return false;
        }

        // ================================================================
        // 配置元の差し替え
        // ================================================================

        private string ExecuteReplaceGroupSource(ProjectContext project, ModelContext model, ReplaceObjectGroupSourceCommand c)
        {
            if (c.SourceMasterIndices == null || c.SourceMasterIndices.Length == 0) return "新しい配置元が選ばれていません";

            var target = model.GetMeshContext(c.OutputIndex);
            if (target == null || target.ObjectId == 0UL) return $"オブジェクトがありません（索引 {c.OutputIndex}）";

            // 出力先として target を持つ項目を探す。
            Poly_Ling.Data.ObjectGroup g = null;
            int si = -1;
            foreach (var cand in model.ObjectGroups)
            {
                if (cand == null) continue;
                for (int k = 0; k < cand.StepCount; k++)
                {
                    if (cand.GetStep(k)?.ContainsOutput(target.ObjectId) ?? false) { g = cand; si = k; break; }
                }
                if (g != null) break;
            }
            if (g == null) return $"「{target.Name}」を作ったグループの項目が見つかりません";

            var step = g.GetStep(si);
            Type t = PanelCommandFactory.ResolveType(step.Action);
            if (t == null) return $"未対応の action: {step.Action}";

            // 差し替える引数：placement 以外で、0 でない ObjectId を控えているもの。
            string key = null;
            int found = 0;
            foreach (var mr in PanelCommandFactory.MeshRefKeys(t))
            {
                if (mr.Key.StartsWith("placement.", StringComparison.OrdinalIgnoreCase)) continue;
                bool any = false;
                foreach (ulong id in step.GetMeshRefIds(mr.Key)) if (id != 0UL) { any = true; break; }
                if (!any) continue;
                key = mr.Key;
                found++;
            }
            if (found != 1)
                return found == 0
                    ? $"項目 {step.Action} は配置元を控えていません"
                    : $"項目 {step.Action} は配置元の引数が {found} 個あり、どれを差し替えるか決められません";

            var newIds = new List<ulong>(c.SourceMasterIndices.Length);
            var parts  = new List<string>(c.SourceMasterIndices.Length);
            foreach (int idx in c.SourceMasterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null || mc.ObjectId == 0UL) return $"配置元がありません（索引 {idx}）";
                if (ReferenceEquals(mc, target)) return "出力先そのものは配置元にできません";
                newIds.Add(mc.ObjectId);
                parts.Add(idx.ToString(CultureInfo.InvariantCulture));
            }

            int gIndex = model.ObjectGroups.IndexOf(g);
            var oldSnapshot = g.Clone();
            var oldIds = new List<ulong>(step.GetMeshRefIds(key));
            step.Args.TryGetValue(key, out string oldArg);

            step.SetMeshRefIds(key, newIds);
            step.Args[key] = string.Join(",", parts);

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);

            string fail = null;
            _undoController?.SuspendRecording();
            try
            {
                if (!RunObjectGroupStep(project, model, c.ModelIndex, false, g, si, out string err))
                    fail = err ?? "作り直しに失敗しました";
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            // 作り直せなかったときは控えを元へ戻す（グループと出力先の中身を食い違わせない）。
            if (fail != null)
            {
                step.SetMeshRefIds(key, oldIds);
                if (oldArg != null) step.Args[key] = oldArg; else step.Args.Remove(key);
            }

            g.SourceDigest = ObjectGroupOps.ComputeSourceDigest(project, g);

            RecordSubdivisionUndo(model, listBefore, model.ObjectGroupCount,
                new List<ObjectGroupChangeRecord>
                {
                    new ObjectGroupChangeRecord { ReplacedIndex = gIndex, OldGroup = oldSnapshot, NewGroup = g.Clone() },
                },
                "配置元の差し替え");

            FinishGroupEdit(project, model);
            if (fail != null) return fail;

            int ti = model.MeshContextList.IndexOf(target);
            ReportTargets(new[] { ti }, new[] { target.ObjectId });
            return null;
        }

        /// <summary>グループの編集のあとで画面とパネルへ配る。</summary>
        private void FinishGroupEdit(ProjectContext project, ModelContext model)
        {
            model.ComputeWorldMatrices();
            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);
        }
    }
}
