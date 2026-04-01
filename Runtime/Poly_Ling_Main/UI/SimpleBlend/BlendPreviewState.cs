// BlendPreviewState.cs
// SimpleBlend プレビュー状態管理・適用ロジック
// UnityEditor非依存 → Runtime/移行可能

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class BlendPreviewState
    {
        private bool                            _isActive       = false;
        private readonly Dictionary<int, Vector3[]> _backups    = new();
        private readonly Dictionary<int, bool>  _savedVisibility = new();

        public bool IsActive => _isActive;

        public void Start(ModelContext model, List<int> targetIndices, int sourceIndex)
        {
            if (_isActive) return;
            _backups.Clear();
            _savedVisibility.Clear();

            foreach (int idx in targetIndices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo     = ctx.MeshObject;
                var backup = new Vector3[mo.VertexCount];
                for (int i = 0; i < mo.VertexCount; i++) backup[i] = mo.Vertices[i].Position;
                _backups[idx] = backup;
                _savedVisibility[idx] = ctx.IsVisible;
                ctx.IsVisible = true;
            }

            if (sourceIndex >= 0)
            {
                var srcCtx = model.GetMeshContext(sourceIndex);
                if (srcCtx != null)
                {
                    _savedVisibility[sourceIndex] = srcCtx.IsVisible;
                    srcCtx.IsVisible = false;
                }
            }

            _isActive = true;
        }

        public void Apply(ModelContext model, int sourceIndex, float weight,
            bool selectedVertsOnly, HashSet<int> selectedVerts,
            bool matchByVertexId, ToolContext toolCtx)
        {
            if (!_isActive) return;
            var srcCtx = model.GetMeshContext(sourceIndex);
            if (srcCtx?.MeshObject == null) return;
            var srcMo = srcCtx.MeshObject;

            Dictionary<int, int> srcIdMap = matchByVertexId ? BuildVertexIdMap(srcMo) : null;
            var verts = selectedVertsOnly ? selectedVerts : null;

            foreach (var (idx, backup) in _backups)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var mo          = ctx.MeshObject;
                var nonIsolated = BuildNonIsolatedSet(mo);
                BlendVertices(mo, backup, srcMo, weight, nonIsolated, verts, srcIdMap);
                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                BlendOperation.SyncMirrorSide(model, ctx, toolCtx);
            }
            toolCtx?.Repaint?.Invoke();
        }

        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (!_isActive) return;

            if (model != null)
            {
                foreach (var (idx, backup) in _backups)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx?.MeshObject == null) continue;
                    var mo    = ctx.MeshObject;
                    int count = Mathf.Min(backup.Length, mo.VertexCount);
                    for (int i = 0; i < count; i++) mo.Vertices[i].Position = backup[i];
                    toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                    BlendOperation.SyncMirrorSide(model, ctx, toolCtx);
                }
                foreach (var (idx, visible) in _savedVisibility)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx != null) ctx.IsVisible = visible;
                }
            }

            _backups.Clear();
            _savedVisibility.Clear();
            _isActive = false;
            toolCtx?.Repaint?.Invoke();
        }

        public Dictionary<int, Vector3[]> Backups => _backups;
        public Dictionary<int, bool> SavedVisibility => _savedVisibility;

        // ================================================================
        // 静的ヘルパー
        // ================================================================

        public static void BlendVertices(MeshObject mo, Vector3[] backup, MeshObject srcMo,
            float w, HashSet<int> nonIsolated, HashSet<int> selectedVerts, Dictionary<int, int> srcIdMap)
        {
            if (srcIdMap != null)
            {
                for (int i = 0; i < mo.VertexCount; i++)
                {
                    if (!nonIsolated.Contains(i)) continue;
                    if (selectedVerts != null && !selectedVerts.Contains(i)) continue;
                    int vertId = mo.Vertices[i].Id;
                    mo.Vertices[i].Position = srcIdMap.TryGetValue(vertId, out int si)
                        ? Vector3.Lerp(backup[i], srcMo.Vertices[si].Position, w)
                        : backup[i];
                }
            }
            else
            {
                int count = Mathf.Min(mo.VertexCount, srcMo.VertexCount);
                for (int i = 0; i < mo.VertexCount; i++)
                {
                    if (!nonIsolated.Contains(i)) continue;
                    if (selectedVerts != null && !selectedVerts.Contains(i)) continue;
                    mo.Vertices[i].Position = i < count
                        ? Vector3.Lerp(backup[i], srcMo.Vertices[i].Position, w)
                        : backup[i];
                }
            }
        }

        public static Dictionary<int, int> BuildVertexIdMap(MeshObject mo)
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (!map.ContainsKey(id)) map[id] = i;
            }
            return map;
        }

        public static HashSet<int> BuildNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            return set;
        }
    }
}
