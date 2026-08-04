// DeleteSelectionTool.cs
// 選択削除ツール - 選択中の頂点 / 面 / 線分 (2角形) を削除する。
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
// Undo は MeshObjectSnapshot + RecordTopologyChangeCommand の SelectionState 付き
// オーバーロードで記録する (Edge/Line 選択の復元に必要。MeshUndoController の
// RecordTopologyChange のコメント参照)。
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
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

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
        // 公開 API
        //   ctx を引数で受け取る。ツールをアクティブにせず (InteractionMode を
        //   切り替えず) 呼べるようにするため、_context のような内部保持はしない。
        // ================================================================

        /// <summary>
        /// 削除対象の要素数を返す (実行可否判定用)。Edges は数えない。
        /// </summary>
        public static int GetDeletableCount(SelectionState sel)
        {
            if (sel == null) return 0;
            return sel.Vertices.Count + sel.Faces.Count + sel.Lines.Count;
        }

        /// <summary>
        /// 選択されている頂点 / 面 / 線分を削除する。
        /// </summary>
        public void Execute(ToolContext ctx)
        {
            if (ctx == null)
            {
                Debug.LogWarning("[DeleteSelectionTool] EARLY RETURN: ctx is null");
                return;
            }

            var mc  = ctx.ActiveMeshContext;
            var mo  = mc?.MeshObject;
            var sel = ctx.SelectionState;
            if (mo == null || sel == null)
            {
                Debug.LogWarning($"[DeleteSelectionTool] EARLY RETURN: model={ctx.Model != null}, "
                               + $"activeMeshContext={mc != null}, meshObject={mo != null}, selectionState={sel != null}");
                return;
            }

            // ボーン表示用メッシュは編集対象外 (選択モード適用でも同じ扱い)。
            if (mc.Type == MeshType.Bone)
            {
                Debug.LogWarning("[DeleteSelectionTool] EARLY RETURN: active mesh is MeshType.Bone");
                return;
            }

            // ------------------------------------------------------------
            // 1. 明示的に選択されている削除対象を集める
            //    Faces と Lines はどちらも MeshObject.Faces のインデックス。
            //    Edges (VertexPair) は対象外。
            // ------------------------------------------------------------
            var faceKill = new HashSet<int>();
            foreach (int fi in sel.Faces)
                if (fi >= 0 && fi < mo.FaceCount) faceKill.Add(fi);
            foreach (int fi in sel.Lines)
                if (fi >= 0 && fi < mo.FaceCount) faceKill.Add(fi);

            var vertKill = new HashSet<int>();
            foreach (int vi in sel.Vertices)
                if (vi >= 0 && vi < mo.VertexCount) vertKill.Add(vi);

            if (faceKill.Count == 0 && vertKill.Count == 0)
            {
                Debug.LogWarning($"[DeleteSelectionTool] EARLY RETURN: nothing selected to delete "
                               + $"(verts={sel.Vertices.Count}, faces={sel.Faces.Count}, lines={sel.Lines.Count}, "
                               + $"edges={sel.Edges.Count} <- edges are not deleted by design)");
                return;
            }

            int selectedFaceCount   = faceKill.Count;
            int selectedVertexCount = vertKill.Count;

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

            // ------------------------------------------------------------
            // 4. Undo 用スナップショット (操作前)
            //    SelectionState 付きで撮ることで Undo 時に Edge/Line 選択も戻る。
            // ------------------------------------------------------------
            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(mc, ctx.UndoController.MeshUndoContext, sel)
                : default;

            int killedFaces    = faceKill.Count;
            int killedVertices = vertKill.Count;

            // ------------------------------------------------------------
            // 5. 面を降順で削除 (先頭から消すと後続インデックスがずれる)
            // ------------------------------------------------------------
            foreach (int fi in faceKill.OrderByDescending(i => i))
            {
                if (fi >= 0 && fi < mo.FaceCount) mo.Faces.RemoveAt(fi);
            }

            // ------------------------------------------------------------
            // 6. 頂点を降順で削除し、残存面の頂点インデックスを再マップ
            //    手順 2 により残存面は vertKill の頂点を一切参照しないため、
            //    ここは純粋なインデックスのシフトになる。面の部分的な作り直し
            //    (UVIndices / NormalIndices の詰め直し) は発生しない。
            // ------------------------------------------------------------
            if (vertKill.Count > 0)
            {
                int originalCount = mo.VertexCount;
                var indexMap = new int[originalCount];
                int newIndex = 0;
                for (int i = 0; i < originalCount; i++)
                    indexMap[i] = vertKill.Contains(i) ? -1 : newIndex++;

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

                foreach (int vi in vertKill.OrderByDescending(i => i))
                {
                    if (vi >= 0 && vi < mo.VertexCount) mo.Vertices.RemoveAt(vi);
                }

                // Vertices を直接操作したので Position 配列キャッシュを無効化する。
                mo.InvalidatePositionCache();
            }

            // ------------------------------------------------------------
            // 7. 位相変更の通知 (選択の全クリア → SyncMesh → GPU 再構築 → 再描画)
            // ------------------------------------------------------------
            ctx.OnTopologyChanged();

            // ------------------------------------------------------------
            // 8. Undo 記録 (操作後スナップショット)
            // ------------------------------------------------------------
            if (ctx.UndoController != null)
            {
                var after = MeshObjectSnapshot.Capture(mc, ctx.UndoController.MeshUndoContext, sel);
                ctx.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    ctx.UndoController, before, after, sel,
                    $"Delete Selection ({killedFaces} faces / {killedVertices} vertices)"));
            }

            Debug.Log($"[DeleteSelectionTool] selected: {selectedVertexCount} verts, {selectedFaceCount} faces/lines"
                    + $" -> deleted: {killedVertices} verts, {killedFaces} faces/lines");
        }
    }
}
