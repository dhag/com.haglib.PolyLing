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
        private bool                                    _isPreviewActive            = false;
        private readonly Dictionary<int, Vector3[]>    _previewBackups             = new();
        private readonly List<(int morphIndex, int baseIndex, float entryWeight)> _previewPairs = new();
        private int                                     _previewMorphExpressionIndex = -1;

        public bool IsActive => _isPreviewActive;
        public int  ActiveSetIndex => _previewMorphExpressionIndex;

        // ================================================================
        // プレビュー開始
        // ================================================================

        public void Start(ModelContext model,
            List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)> pairs,
            int setIndex)
        {
            End(model, null);
            _previewBackups.Clear();
            _previewPairs.Clear();

            foreach (var (morphIndex, baseIndex, morphCtx, baseCtx, weight) in pairs)
            {
                if (baseCtx?.MeshObject == null) continue;
                if (!_previewBackups.ContainsKey(baseIndex))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup   = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _previewBackups[baseIndex] = backup;
                }
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
            if (!_isPreviewActive || _previewBackups.Count == 0) return;

            foreach (var (baseIndex, backup) in _previewBackups)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count    = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            foreach (var (morphIndex, baseIndex, entryWeight) in _previewPairs)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                var baseCtx  = model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * (entryWeight * weight);
            }

            foreach (var baseIndex in _previewBackups.Keys)
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
                foreach (var (baseIndex, backup) in _previewBackups)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count    = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    toolCtx?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }

            _previewBackups.Clear();
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
