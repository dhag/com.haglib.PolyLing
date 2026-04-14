// SplitVerticesTool.cs
// 頂点分割ツール - 選択頂点を面ごとに独立したコピーに分離する
// 複数面に共有されている頂点を、面ごとの独立した頂点に分割する
// 位相が変わる（頂点数増加）ため OnTopologyChanged() を使用する

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点分割ツール
    /// </summary>
    public class SplitVerticesTool : IEditTool
    {
        public string Name        => "SplitVertices";
        public string DisplayName => "Split Vertices";

        public IToolSettings Settings => null;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        public int SelectedVertexCount => _context?.SelectedVertices?.Count ?? 0;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)             => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)               => false;
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }
        public void Reset() { }

        // ================================================================
        // 公開 API
        // ================================================================

        public void TriggerSplit() => ExecuteSplit();

        /// <summary>
        /// 選択頂点のうち、実際に2面以上に共有されているものの数を返す（実行可能判定用）
        /// </summary>
        public int GetSplittableCount()
        {
            var mesh = _context?.FirstSelectedMeshObject;
            var sel  = _context?.SelectedVertices;
            if (mesh == null || sel == null || sel.Count == 0) return 0;

            var facesByVertex = BuildFacesByVertex(mesh);
            return sel.Count(idx => facesByVertex.TryGetValue(idx, out var faces) && faces.Count >= 2);
        }

        // ================================================================
        // 分割実行
        // ================================================================

        private void ExecuteSplit()
        {
            var mesh = _context?.FirstSelectedMeshObject;
            var sel  = _context?.SelectedVertices;
            if (mesh == null || sel == null || sel.Count == 0) return;

            MeshObjectSnapshot before = _context.UndoController != null
                ? MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext)
                : default;

            var facesByVertex = BuildFacesByVertex(mesh);

            int splitCount = 0;
            // 選択頂点インデックスのスナップショット（ループ中に頂点数が増えるため）
            var selectedSnapshot = sel.ToList();

            foreach (int origIdx in selectedSnapshot)
            {
                if (!facesByVertex.TryGetValue(origIdx, out var faceIndices)) continue;
                if (faceIndices.Count < 2) continue;

                // 1面目はそのまま、2面目以降はクローンを新規追加して差し替え
                for (int fi = 1; fi < faceIndices.Count; fi++)
                {
                    var face   = mesh.Faces[faceIndices[fi]];
                    var clone  = mesh.Vertices[origIdx].Clone();
                    clone.Id   = 0; // AddVertex に ID 自動割り当てさせる
                    int newIdx = mesh.AddVertex(clone);

                    for (int j = 0; j < face.VertexIndices.Count; j++)
                    {
                        if (face.VertexIndices[j] == origIdx)
                        {
                            face.VertexIndices[j] = newIdx;
                            break; // 1面に同一頂点が複数ある場合でも最初の1つを置換
                        }
                    }
                    splitCount++;
                }
            }

            if (splitCount > 0)
            {
                _context.OnTopologyChanged();

                if (_context.UndoController != null)
                {
                    var after = MeshObjectSnapshot.Capture(_context.UndoController.MeshUndoContext);
                    _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        _context.UndoController, before, after,
                        $"Split {selectedSnapshot.Count} Vertices"));
                }

                Debug.Log($"[SplitVerticesTool] Split {splitCount} vertex-face connections");
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 各頂点インデックス → その頂点を参照している面インデックスのリスト
        /// </summary>
        private static Dictionary<int, List<int>> BuildFacesByVertex(MeshObject mesh)
        {
            var result = new Dictionary<int, List<int>>();
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                foreach (int vi in mesh.Faces[fi].VertexIndices)
                {
                    if (!result.TryGetValue(vi, out var list))
                        result[vi] = list = new List<int>();
                    list.Add(fi);
                }
            }
            return result;
        }
    }
}
