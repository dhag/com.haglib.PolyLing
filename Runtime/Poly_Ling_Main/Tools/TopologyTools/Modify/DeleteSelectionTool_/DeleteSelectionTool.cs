// DeleteSelectionTool.cs
// 選択削除ツール - 選択中の頂点 / 面 / 線分を削除する。
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）× 各オブジェクトの
//   選択要素。VertexDissolveTool / VertexHoleTool と同じ方針。
//
// ================================================================
// 【削除範囲】
//   - SelectionState.Vertices : 削除する。その頂点を参照する面・線分も一緒に削除する。
//   - SelectionState.Faces    : 削除する。
//   - SelectionState.Lines    : 削除する。線分 = 2頂点の面であり、Faces と同じ
//                               MeshObject.Faces のインデックスを指す。
//   - SelectionState.Edges    : 削除しない。メッシュの一部としての辺は面の構成要素で
//                               あって単独の実体を持たないため、対象外とする。
//   - 面/線分の削除で参照ゼロになった頂点は削除する (孤立頂点の掃除)。
//     ただし操作前から面に参照されていなかった浮き頂点は、明示的に選択されていない
//     限り残す (無関係な浮き頂点を巻き込まないため)。
//
// 位相が変わる (頂点数/面数が変化する) ため OnTopologyChanged() を使う。
//
// 【Undo】複数メッシュを書き換えるため MeshUndoContext.MeshObject 経由の
//   MeshObjectSnapshot は使えない (先頭メッシュだけが復元される)。MeshContextIndex を
//   持つ MultiMeshTopologySnapshotRecord を MeshListStack へ 1 件だけ記録する
//   (メッシュが何個でも Undo は1手)。VertexDissolveTool と同じ方針。
//
// ================================================================
// 【ミラー側への伝播】
//   実体側の位相が変わったらミラー側も同じ形にする。対象の引き当ては
//   MirrorBranchOps.CollectMirrorPeers（MirrorPairs と BakedMirrorSourceIndex）に
//   一本化し、MirrorGeometryDerived では絞らない。同フラグは実効ワールドに
//   共役 S·H·S を掛けるかという描画側の都合であって、ミラーの連結ではない。
//   これで「生成ミラー」「スキンド変換後のミラー」「PMX 由来のミラー」の
//   3系統が同じ経路を通る。
//
//   前提は添字恒等対応（頂点・面が 1:1、面内の巻き順は逆順）。位相を変える前に
//   CaptureMirrorRebuildPlan が実測で検証し、成立しないペアは触らずログを出す。
//
//   伝播は「実体側と同じ面添字・同じ頂点添字を、ミラー側でも消す」だけで足りる。
//   実体側から写す作業は無い。生き残ったミラー側の頂点オブジェクトはそのまま
//   残るので、位置・UV・法線・ボーンウェイトは何もしなくても保存される。
//   面の UVIndices / NormalIndices は頂点内のスロット番号なので、頂点添字の
//   再マップの影響を受けない。
//
// ================================================================
// 【MeshMergeHelper.DeleteVertices を使わない理由】
//   同関数は全ての面を走査して「残存頂点数が 3 未満の面」を一律削除するため、
//   削除頂点に触れていない線分 (残存 2 頂点) まで消えてしまう。
//   本ツールは「削除頂点を参照する面は丸ごと削除する」方式にしており、
//   その結果、残存面は削除頂点を一切参照しない。頂点インデックスの再マップが
//   単純なシフトだけで済み、Face.UVIndices (頂点内 UV スロット番号) を
//   部分的に作り直す必要も無くなる。
// ================================================================

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 選択削除ツール
    /// </summary>
    public class DeleteSelectionTool : IEditTool
    {
        public string Name        => "DeleteSelection";
        public string DisplayName => "Delete Selection";

        public IToolSettings Settings => null;

        // ================================================================
        // IEditTool 実装
        //   ビューポート操作を持たない (ボタン / ショートカットで即時実行する) ため、
        //   マウス系とギズモは全て空実装。
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)                => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                  => false;
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { }
        public void OnDeactivate(ToolContext ctx) { }
        public void Reset() { }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1メッシュぶんの対象。</summary>
        public struct DeleteTarget
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、削除対象を持つものだけを返す。
        /// ボーン表示用メッシュは編集対象外。
        /// </summary>
        private static List<DeleteTarget> EnumerateTargets(ModelContext model)
        {
            var list = new List<DeleteTarget>();
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var sel = mc.Selection;
                if (sel == null) continue;
                if (sel.Vertices.Count == 0 && sel.Faces.Count == 0 && sel.Lines.Count == 0) continue;

                list.Add(new DeleteTarget { MeshIndex = idx, MeshContext = mc });
            }

            return list;
        }

        // ================================================================
        // 公開 API
        //   ctx を引数で受け取る。ツールをアクティブにせず (InteractionMode を
        //   切り替えず) 呼べるようにするため、_context のような内部保持はしない。
        // ================================================================

        /// <summary>
        /// 削除対象の要素数を返す (実行可否判定用)。Edges は数えない。
        /// 選択中の描画オブジェクト全部の合計。
        /// </summary>
        public static int GetDeletableCount(ModelContext model)
        {
            int total = 0;
            foreach (var t in EnumerateTargets(model))
            {
                var sel = t.MeshContext.Selection;
                total += sel.Vertices.Count + sel.Faces.Count + sel.Lines.Count;
            }
            return total;
        }

        /// <summary>
        /// 選択されている頂点 / 面 / 線分を削除する。
        /// 対象メッシュすべてを 1 回の Undo にまとめる。
        /// </summary>
        public void Execute(ToolContext ctx)
        {
            if (ctx == null)
            {
                Debug.LogWarning("[DeleteSelectionTool] EARLY RETURN: ctx is null");
                return;
            }

            var model   = ctx.Model;
            var targets = EnumerateTargets(model);

            if (model == null || targets.Count == 0)
            {
                Debug.LogWarning($"[DeleteSelectionTool] EARLY RETURN: model={model != null}, "
                               + $"targets={targets.Count} <- edges are not deleted by design");
                return;
            }

            var undo = ctx.UndoController;

            // 生成ミラーは実体側から作り直すため、Undo の記録対象に含める。
            // 片側だけ記録すると Undo で実体とミラーが食い違う。
            var realIndices = new List<int>();
            foreach (var t in targets) realIndices.Add(t.MeshIndex);
            var captureIndices = MirrorBranchOps.CollectMirrorCaptureIndices(model, realIndices);

            // ミラー側への伝播計画。添字恒等対応の検証を含むため、位相を変える前に取る。
            var mirrorPlan = MirrorBranchOps.CaptureMirrorRebuildPlan(model, realIndices);

            var before = new MultiMeshTopologySnapshot();
            if (undo != null)
                foreach (int idx in captureIndices) before.CaptureMesh(model, idx);

            int selectedVertexTotal = 0;
            int selectedFaceTotal   = 0;
            int killedVerticesTotal = 0;
            int killedFacesTotal    = 0;
            int okMeshes            = 0;

            // 実体側で消した添字。同じものをミラー側にも掛ける。
            var killSets = new Dictionary<int, KillSet>();

            foreach (var t in targets)
            {
                bool ok = ComputeKillSet(
                    t.MeshContext.MeshObject, t.MeshContext.Selection,
                    out KillSet kill,
                    out int selectedVerts, out int selectedFaces);

                if (!ok) continue;

                ApplyKill(t.MeshContext.MeshObject, kill);
                killSets[t.MeshIndex] = kill;

                selectedVertexTotal += selectedVerts;
                selectedFaceTotal   += selectedFaces;
                killedVerticesTotal += kill.Vertices.Count;
                killedFacesTotal    += kill.Faces.Count;
                okMeshes++;

                // 消えた要素を指したままの選択を残さない。
                t.MeshContext.Selection.ClearAll();
            }

            if (okMeshes == 0)
            {
                Debug.LogWarning("[DeleteSelectionTool] EARLY RETURN: nothing to delete "
                               + "<- edges are not deleted by design");
                return;
            }

            // 実体側と同じ添字をミラー側でも消す。
            // 検証を落ちたペアは触らず、理由を ApplyToMirrors が出す。
            int mirrorApplied = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (!killSets.TryGetValue(realIdx, out var kill)) return false;
                ApplyKill(mirrorMo, kill);
                return true;
            });

            // 位相変更の通知 (SyncMesh → GPU 再構築 → 再描画)
            ctx.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in captureIndices) after.CaptureMesh(model, idx);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Delete Selection ({okMeshes} objs / {killedFacesTotal} faces / {killedVerticesTotal} vertices)";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            Debug.Log($"[DeleteSelectionTool] オブジェクト {okMeshes} / selected: {selectedVertexTotal} verts, "
                    + $"{selectedFaceTotal} faces/lines -> deleted: {killedVerticesTotal} verts, "
                    + $"{killedFacesTotal} faces/lines / ミラー伝播 {mirrorApplied}"
                    + $" (対象 {mirrorPlan.Entries.Count} / 検証落ち {mirrorPlan.RejectedCount})");
        }

        // ================================================================
        // 1 メッシュぶんの削除
        //
        // 「何を消すか」を決める ComputeKillSet と、「消す」ApplyKill に分ける。
        // 実体側は両方を通し、ミラー側は同じ KillSet で ApplyKill だけを通す。
        // これで左右がまったく同じ添字操作を受ける。
        // ================================================================

        /// <summary>削除対象の添字（削除前の番号）。</summary>
        public struct KillSet
        {
            /// <summary>削除する面 / 線分（MeshObject.Faces の添字）。</summary>
            public HashSet<int> Faces;

            /// <summary>削除する頂点（MeshObject.Vertices の添字）。</summary>
            public HashSet<int> Vertices;
        }

        /// <summary>
        /// 選択内容と位相から、削除する面と頂点の添字を決める。対象が無ければ false。
        /// メッシュは変更しない。
        /// </summary>
        private static bool ComputeKillSet(
            MeshObject mo, SelectionState sel,
            out KillSet kill,
            out int selectedVertexCount, out int selectedFaceCount)
        {
            kill = new KillSet { Faces = new HashSet<int>(), Vertices = new HashSet<int>() };
            selectedVertexCount = 0;
            selectedFaceCount   = 0;

            if (mo == null || sel == null) return false;

            var faceKill = kill.Faces;
            var vertKill = kill.Vertices;

            // ------------------------------------------------------------
            // 1. 明示的に選択されている削除対象を集める
            //    Faces と Lines はどちらも MeshObject.Faces のインデックス。
            //    Edges (VertexPair) は対象外。
            // ------------------------------------------------------------
            foreach (int fi in sel.Faces)
                if (fi >= 0 && fi < mo.FaceCount) faceKill.Add(fi);
            foreach (int fi in sel.Lines)
                if (fi >= 0 && fi < mo.FaceCount) faceKill.Add(fi);

            foreach (int vi in sel.Vertices)
                if (vi >= 0 && vi < mo.VertexCount) vertKill.Add(vi);

            if (faceKill.Count == 0 && vertKill.Count == 0) return false;

            selectedFaceCount   = faceKill.Count;
            selectedVertexCount = vertKill.Count;

            // ------------------------------------------------------------
            // 2. 削除頂点を参照する面・線分を削除対象に加える
            //    (「頂点を削除したら関する面や線分も削除する」)
            // ------------------------------------------------------------
            if (vertKill.Count > 0)
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    if (faceKill.Contains(fi)) continue;
                    var vidx = mo.Faces[fi].VertexIndices;
                    for (int j = 0; j < vidx.Count; j++)
                    {
                        if (vertKill.Contains(vidx[j])) { faceKill.Add(fi); break; }
                    }
                }
            }

            // ------------------------------------------------------------
            // 3. 孤立する頂点を削除対象に加える
            //    (「面が削除されて孤立頂点になるなら頂点も削除する」)
            //    候補は「削除される面に参照されていた頂点」だけに限定する。
            //    こうすることで、操作前から面に参照されていなかった浮き頂点は
            //    (選択されていない限り) 巻き込まれない。
            // ------------------------------------------------------------
            var candidates = new HashSet<int>();
            foreach (int fi in faceKill)
                foreach (int vi in mo.Faces[fi].VertexIndices)
                    if (!vertKill.Contains(vi)) candidates.Add(vi);

            if (candidates.Count > 0)
            {
                var survivingRef = new HashSet<int>();
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    if (faceKill.Contains(fi)) continue;
                    foreach (int vi in mo.Faces[fi].VertexIndices) survivingRef.Add(vi);
                }
                foreach (int vi in candidates)
                    if (!survivingRef.Contains(vi)) vertKill.Add(vi);
            }

            return true;
        }

        /// <summary>
        /// KillSet の添字を実際に削除する。ミラー側にも同じ KillSet で掛ける。
        ///
        /// 面の UVIndices / NormalIndices は頂点内のスロット番号なので触らない。
        /// 生き残った頂点オブジェクトはそのまま残るため、位置・UV・法線・
        /// ボーンウェイトは自動的に保存される。
        /// </summary>
        private static void ApplyKill(MeshObject mo, KillSet kill)
        {
            if (mo == null || kill.Faces == null || kill.Vertices == null) return;

            // ------------------------------------------------------------
            // 面を降順で削除 (先頭から消すと後続インデックスがずれる)
            // ------------------------------------------------------------
            foreach (int fi in kill.Faces.OrderByDescending(i => i))
            {
                if (fi >= 0 && fi < mo.FaceCount) mo.Faces.RemoveAt(fi);
            }

            if (kill.Vertices.Count == 0) return;

            // ------------------------------------------------------------
            // 頂点を降順で削除し、残存面の頂点インデックスを再マップ
            //    手順 2 により残存面は kill.Vertices の頂点を一切参照しないため、
            //    ここは純粋なインデックスのシフトになる。面の部分的な作り直し
            //    (UVIndices / NormalIndices の詰め直し) は発生しない。
            // ------------------------------------------------------------
            int originalCount = mo.VertexCount;
            var indexMap = new int[originalCount];
            int newIndex = 0;
            for (int i = 0; i < originalCount; i++)
                indexMap[i] = kill.Vertices.Contains(i) ? -1 : newIndex++;

            foreach (var face in mo.Faces)
            {
                var vidx = face.VertexIndices;
                for (int j = 0; j < vidx.Count; j++)
                {
                    int old = vidx[j];
                    if (old >= 0 && old < originalCount && indexMap[old] >= 0)
                        vidx[j] = indexMap[old];
                }
            }

            foreach (int vi in kill.Vertices.OrderByDescending(i => i))
            {
                if (vi >= 0 && vi < mo.VertexCount) mo.Vertices.RemoveAt(vi);
            }

            // Vertices を直接操作したので Position 配列キャッシュを無効化する。
            mo.InvalidatePositionCache();
        }
    }
}
