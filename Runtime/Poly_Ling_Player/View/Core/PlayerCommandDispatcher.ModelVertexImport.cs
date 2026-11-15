// PlayerCommandDispatcher.ModelVertexImport.cs
// コマンドディスパッチャ：2 つのモデルの間の頂点位置の取り込み（importModelVertices）と、2 モデル表示の開閉。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 書き込みの決まりは Ops/ModelVertexImportOps.cs の冒頭注記。Undo は ModelVertexImportRecord 1 件
// （写し先のモデルを記録が持つ。今のモデルでないモデルへも書くため）。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>2 モデル表示を開く受け口（Viewer）。戻り値は失敗理由。成功時は null。</summary>
        public Func<ShowTwoModelViewCommand, string> OnShowTwoModelView;

        /// <summary>2 モデル表示を閉じる受け口（Viewer）。戻り値は失敗理由。成功時は null。</summary>
        public Func<CloseTwoModelViewCommand, string> OnCloseTwoModelView;

        /// <summary>importModelVertices の直近の結果の一文（2 モデル表示のパネルが状態の欄へ出す）。</summary>
        public string LastModelVertexImportSummary { get; private set; } = "";

        private void FailModelVertex(string msg)
        {
            LastModelVertexImportSummary = "失敗: " + msg;
            Fail(msg);
        }

        private bool DispatchModelVertexImport(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case ShowTwoModelViewCommand c:
                {
                    if (OnShowTwoModelView == null) { Fail("two model view handler not wired"); return true; }
                    string reason = OnShowTwoModelView(c);
                    if (reason != null) Fail(reason);
                    MarkNotRecorded();
                    return true;
                }

                case CloseTwoModelViewCommand c:
                {
                    if (OnCloseTwoModelView == null) { Fail("two model view handler not wired"); return true; }
                    string reason = OnCloseTwoModelView(c);
                    if (reason != null) Fail(reason);
                    MarkNotRecorded();
                    return true;
                }

                case ImportModelVerticesCommand c:
                    ExecuteImportModelVertices(c, project);
                    return true;

                default:
                    return false;
            }
        }

        private void ExecuteImportModelVertices(ImportModelVerticesCommand c, ProjectContext project)
        {
            LastModelVertexImportSummary = "";
            var src = project?.GetModel(c.SourceModelIndex);
            var dst = project?.GetModel(c.TargetModelIndex);
            if (src == null || dst == null) { FailModelVertex("写し元か写し先のモデルがありません"); return; }
            if (c.SourceMeshIndices.Length == 0 || c.SourceMeshIndices.Length != c.TargetMeshIndices.Length)
            {
                FailModelVertex($"sourceMeshIndices が {c.SourceMeshIndices.Length} 件、targetMeshIndices が {c.TargetMeshIndices.Length} 件で、長さが合わないか空です");
                return;
            }

            // ── 書き込み先の形
            int shapeId;
            ShapeDef namedDef = null;
            switch (c.Target)
            {
                case ModelVertexTarget.BaseShape:
                    shapeId = ShapeOps.BaseShapeId;
                    break;
                case ModelVertexTarget.NamedShape:
                    if (string.IsNullOrEmpty(c.ShapeName)) { FailModelVertex("target が NamedShape なのに shapeName が空です"); return; }
                    namedDef = dst.FindShapeByName(c.ShapeName);
                    if (namedDef != null && namedDef.IsUV) { FailModelVertex($"形「{c.ShapeName}」は UV の形なので、頂点位置は書けません"); return; }
                    shapeId = namedDef?.Id ?? -1;
                    break;
                default:
                    shapeId = dst.CurrentShapeId;
                    if (dst.FindShapeById(shapeId)?.IsUV == true)
                    { FailModelVertex($"今の形「{dst.FindShapeById(shapeId).Name}」は UV の形なので、頂点位置は書けません"); return; }
                    break;
            }
            bool canAppend = c.AppendIfMismatch && c.Target == ModelVertexTarget.CurrentShape;

            var before = ShapeModelState.Capture(dst);
            var materialsBefore = MeshListChangeRecord.CloneMaterialRefs(dst.MaterialReferences);

            bool created = false;
            if (c.Target == ModelVertexTarget.NamedShape && namedDef == null)
            {
                namedDef = new ShapeDef { Id = dst.NextShapeId(), Name = c.ShapeName, NameEnglish = "", Panel = 3 };
                dst.Shapes.Add(namedDef);
                shapeId = namedDef.Id;
                created = true;
            }

            int pairs = 0, transferred = 0, shifted = 0, dropped = 0;
            var mismatched = new List<string>();
            var toAppend = new List<MeshContext>();
            var touched = new List<int>();

            for (int p = 0; p < c.SourceMeshIndices.Length; p++)
            {
                var smc = src.GetMeshContext(c.SourceMeshIndices[p]);
                if (smc?.MeshObject == null) { mismatched.Add($"写し元の索引 {c.SourceMeshIndices[p]} にオブジェクトがありません"); continue; }
                int di = c.TargetMeshIndices[p];
                string sLabel = $"{smc.Name}({smc.MeshObject.VertexCount})";

                if (di < 0)
                {
                    if (canAppend) toAppend.Add(smc);
                    else mismatched.Add($"{sLabel} / 末尾（appendIfMismatch が true で書き込み先が今の形のときだけ足す）");
                    continue;
                }

                var dmc = dst.GetMeshContext(di);
                if (dmc?.MeshObject == null) { mismatched.Add($"{sLabel} / 写し先の索引 {di} にオブジェクトがありません"); continue; }
                string dLabel = $"{dmc.Name}({dmc.MeshObject.VertexCount})";
                if (dmc.Type == MeshType.MirrorSide || dmc.MirrorGeometryDerived)
                { mismatched.Add($"{sLabel} / {dLabel}（ミラー側は実体側から作られるので書かない）"); continue; }

                if (ModelVertexImportOps.BuildMap(smc, dmc, c.MatchMode, out var map, out _))
                {
                    transferred += ModelVertexImportOps.WriteShape(dmc.MeshObject, smc.MeshObject, map,
                        dst.CurrentShapeId, shapeId, c.KeepShapeOffsets, out int sh, out int dr);
                    shifted += sh; dropped += dr;
                    pairs++;
                    touched.Add(di);
                }
                else if (canAppend) toAppend.Add(smc);
                else mismatched.Add($"{sLabel} / {dLabel}");
            }

            if (pairs == 0 && toAppend.Count == 0)
            {
                before.Restore(dst);
                FailModelVertex("書ける組がありません" + (mismatched.Count > 0 ? "。合わない: " + string.Join(", ", mismatched) : ""));
                return;
            }

            // ミラー側を実体側から取り直す（形の控えも MirrorPair.SyncPositions が作り直す）。
            if (pairs > 0)
            {
                dst.SyncAllMirrorPositions();
                MirrorBranchOps.RebakeDerivedMirrorVertices(dst.MeshContextList);
            }

            var after = ShapeModelState.Capture(dst);
            var record = new ModelVertexImportRecord { TargetModel = dst, Before = before, After = after };

            // ── 末尾へ足す（After を控えた後。足したものは Appended の控えで出し入れする）
            int weightDropped = 0, materialsAdded = 0;
            var appendedNames = new List<string>();
            foreach (var smc in toAppend)
            {
                var mc = ModelVertexImportOps.CloneForAppend(src, smc, dst, out int wd, out int ma);
                if (mc == null) continue;
                weightDropped += wd; materialsAdded += ma;
                int idx = dst.Add(mc);
                mc.ReplaceUnityMesh(mc.MeshObject.ToUnityMesh());
                record.Appended.Add((idx, MeshContextSnapshot.Capture(mc)));
                appendedNames.Add(mc.Name);
                touched.Add(idx);
            }
            if (materialsAdded > 0)
            {
                record.MaterialsBefore = materialsBefore;
                record.MaterialsAfter  = MeshListChangeRecord.CloneMaterialRefs(dst.MaterialReferences);
            }

            // 表示用メッシュを作り直す（書いたオブジェクトとミラー側。Undo の戻しと同じ範囲）。
            if (pairs > 0)
                foreach (var mc in dst.MeshContextList)
                {
                    var mo = mc?.MeshObject;
                    if (mo == null || mo.VertexCount == 0) continue;
                    mc.ReplaceUnityMesh(mo.ToUnityMesh());
                }

            dst.IsDirty = true;
            string shapeLabel = shapeId == ShapeOps.BaseShapeId ? "基本の形" : $"形「{dst.FindShapeById(shapeId)?.Name}」";
            RecordMeshListUndo(record, $"2 モデル間の頂点取り込み: {dst.Name} の{shapeLabel}");

            if (ReferenceEquals(dst, project.CurrentModel)) _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(toAppend.Count > 0 ? ChangeKind.ListStructure : ChangeKind.Attributes);

            string summary = $"{dst.Name} の{shapeLabel}へ {pairs} 組・{transferred} 頂点";
            if (created) summary += "（形を新しく作った）";
            if (appendedNames.Count > 0) summary += $"。末尾に足した: {string.Join(", ", appendedNames)}";
            if (weightDropped > 0) summary += $"（ボーン名で引けずウェイトを外した頂点 {weightDropped}）";
            if (mismatched.Count > 0) summary += $"。合わず書かない: {string.Join(", ", mismatched)}";
            LastModelVertexImportSummary = summary;

            var ids = new List<ulong>();
            foreach (int i in touched) ids.Add(dst.GetMeshContext(i)?.ObjectId ?? 0UL);
            ReportData(CommandDataJson.New()
                .Int  ("pairs",          pairs)
                .Int  ("transferred",    transferred)
                .Text ("shape",          shapeId == ShapeOps.BaseShapeId ? "" : dst.FindShapeById(shapeId)?.Name ?? "")
                .Int  ("shapeCreated",   created ? 1 : 0)
                .Texts("appended",       appendedNames)
                .Texts("mismatched",     mismatched)
                .Int  ("weightDropped",  weightDropped)
                .Int  ("materialsAdded", materialsAdded)
                .Int  ("shiftedEntries", shifted)
                .Int  ("droppedEntries", dropped)
                .Ints ("masterIndices",  touched)
                .Build(),
                touched.ToArray(), ids.ToArray());
        }
    }
}
