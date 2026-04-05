// MorphPreviewState.cs
// モーフプレビュー状態管理・適用ロジック
// UnityEditor非依存 → Runtime/に移行可能

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class MorphPreviewState
    {
        private bool                                                    _isPreviewActive             = false;
        private readonly HashSet<int>                                   _previewBaseIndices          = new();
        private readonly List<(int morphIndex, int baseIndex, float entryWeight)> _previewPairs = new();
        private int                                                     _previewMorphExpressionIndex = -1;

        public bool IsActive     => _isPreviewActive;
        public int  ActiveSetIndex => _previewMorphExpressionIndex;

        // ================================================================
        // プレビュー開始
        // ================================================================

        public void Start(ModelContext model,
            List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)> pairs,
            int setIndex)
        {
            End(model, null);
            _previewBaseIndices.Clear();
            _previewPairs.Clear();

            foreach (var (morphIndex, baseIndex, morphCtx, baseCtx, weight) in pairs)
            {
                if (baseCtx?.MeshObject == null) continue;
                _previewBaseIndices.Add(baseIndex);
                _previewPairs.Add((morphIndex, baseIndex, weight));
            }

            _previewMorphExpressionIndex = setIndex;
            _isPreviewActive             = true;
        }

        // ================================================================
        // プレビュー適用
        // ================================================================

        public void Apply(ModelContext model, float weight, ToolContext toolCtx)
        {
            if (!_isPreviewActive || _previewBaseIndices.Count == 0) return;

            // Step 1: WorkingPositions をゼロクリア（モーフオフセット用バッファとして初期化）
            // Vertices[i].Position（頂点移動結果）は変更しない
            foreach (int baseIndex in _previewBaseIndices)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var mo = baseCtx.MeshObject;

                if (baseCtx.WorkingPositions == null || baseCtx.WorkingPositions.Length != mo.VertexCount)
                    baseCtx.WorkingPositions = new Vector3[mo.VertexCount];
                else
                    System.Array.Clear(baseCtx.WorkingPositions, 0, baseCtx.WorkingPositions.Length);
            }

            // Step 2: モーフオフセットのみを WorkingPositions に加算
            // GPU書き込み時に Vertices[i].Position + WorkingPositions[i] として合成される
            foreach (var (morphIndex, baseIndex, entryWeight) in _previewPairs)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                var baseCtx  = model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.WorkingPositions == null) continue;

                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseCtx.WorkingPositions.Length)
                        baseCtx.WorkingPositions[vertexIndex] += offset * (entryWeight * weight);
            }

            // Step 3: 視覚更新
            foreach (int baseIndex in _previewBaseIndices)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx != null) toolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
            }
            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // プレビュー終了
        // ================================================================

        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (_isPreviewActive && model != null)
            {
                foreach (int baseIndex in _previewBaseIndices)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx == null) continue;
                    baseCtx.WorkingPositions = null;
                    if (baseCtx.MeshObject != null)
                        toolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }

            _previewBaseIndices.Clear();
            _previewPairs.Clear();
            _previewMorphExpressionIndex = -1;
            _isPreviewActive             = false;
            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // ペア構築
        // ================================================================

        public static List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)>
            BuildMorphBasePairs(ModelContext model, MorphExpression set)
        {
            var pairs = new List<(int, int, MeshContext, MeshContext, float)>();
            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry    = set.MeshEntries[i];
                var morphCtx = model.GetMeshContext(entry.MeshIndex);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIndex = FindBaseMeshIndex(model, morphCtx);
                var baseCtx   = baseIndex >= 0 ? model.GetMeshContext(baseIndex) : null;
                if (baseCtx?.MeshObject != null)
                    pairs.Add((entry.MeshIndex, baseIndex, morphCtx, baseCtx, entry.Weight));
            }
            return pairs;
        }

        public static int FindBaseMeshIndex(ModelContext model, MeshContext morphCtx)
        {
            if (morphCtx == null) return -1;
            if (morphCtx.MorphParentIndex >= 0) return morphCtx.MorphParentIndex;

            string morphName = morphCtx.MorphName;
            string meshName  = morphCtx.Name;
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var ctx = model.GetMeshContext(i);
                    if (ctx != null &&
                        (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror || ctx.Type == MeshType.MirrorSide) &&
                        ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }
    }
}
