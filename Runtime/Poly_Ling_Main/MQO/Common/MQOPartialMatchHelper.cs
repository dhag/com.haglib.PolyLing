// Runtime/Poly_Ling_Main/MQO/Common/MQOPartialMatchHelper.cs
// MQO部分エクスポート/インポート共通ロジック（純粋データ・ロジック部分）
// Editor GUI部分は Editor/Poly_Ling_Main/MQO/Common/MQOPartialMatchHelper.cs (partial)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// モデル側メッシュエントリ
    /// </summary>
    public class PartialMeshEntry
    {
        public bool Selected;
        public int Index;
        public string Name;
        public int VertexCount;
        public int ExpandedVertexCount;
        public bool IsBakedMirror;
        public MeshContext Context;
        public HashSet<int> IsolatedVertices;

        public PartialMeshEntry BakedMirrorPeer;

        public int TotalExpandedVertexCount => ExpandedVertexCount + (BakedMirrorPeer?.ExpandedVertexCount ?? 0);
    }

    /// <summary>
    /// MQO側オブジェクトエントリ
    /// </summary>
    public class PartialMQOEntry
    {
        public bool Selected;
        public int Index;
        public string Name;
        public int VertexCount;
        public int ExpandedVertexCount;
        public MeshContext MeshContext;

        public bool IsMirrored;
        public int MirrorType;
        public int MirrorAxis;
        public float MirrorDistance;
        public int MirrorMaterialOffset;

        public int ExpandedVertexCountWithMirror => ExpandedVertexCount * (IsMirrored ? 2 : 1);
    }

    /// <summary>
    /// MQO部分エクスポート/インポート共通ヘルパー（ロジック部分）
    /// </summary>
    public partial class MQOPartialMatchHelper
    {
        // ================================================================
        // データ
        // ================================================================

        public List<PartialMeshEntry> ModelMeshes { get; } = new List<PartialMeshEntry>();
        public List<PartialMQOEntry> MQOObjects { get; } = new List<PartialMQOEntry>();
        public MQODocument MQODocument { get; private set; }
        public MQOImportResult MQOImportResult { get; private set; }

        // ================================================================
        // モデルリスト構築
        // ================================================================

        public void BuildModelList(ModelContext model, bool skipBakedMirror, bool skipNamedMirror, bool pairMirrors = false)
        {
            ModelMeshes.Clear();
            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            if (pairMirrors)
                BuildModelListWithPairing(drawables);
            else
                BuildModelListSimple(drawables, skipBakedMirror, skipNamedMirror);
        }

        private void BuildModelListSimple(IReadOnlyList<TypedMeshEntry> drawables, bool skipBakedMirror, bool skipNamedMirror)
        {
            for (int i = 0; i < drawables.Count; i++)
            {
                var entry = drawables[i];
                var ctx = entry.Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                if (skipBakedMirror && ctx.IsBakedMirror) continue;
                if (skipNamedMirror && !ctx.IsBakedMirror &&
                    !string.IsNullOrEmpty(ctx.Name) && ctx.Name.EndsWith("+")) continue;

                var isolated = MQOVertexExpandHelper.GetIsolatedVertices(mo);
                int expandedCount = MQOVertexExpandHelper.CalculateExpandedVertexCount(mo, isolated);

                ModelMeshes.Add(new PartialMeshEntry
                {
                    Selected = false,
                    Index = i,
                    Name = ctx.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror = ctx.IsBakedMirror,
                    Context = ctx,
                    IsolatedVertices = isolated
                });
            }
        }

        private void BuildModelListWithPairing(IReadOnlyList<TypedMeshEntry> drawables)
        {
            var pairedIndices = new HashSet<int>();

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx == null) continue;
                if (ctx.IsBakedMirror) pairedIndices.Add(i);
            }

            for (int i = 0; i < drawables.Count; i++)
            {
                if (pairedIndices.Contains(i)) continue;
                var ctx = drawables[i].Context;
                if (ctx == null) continue;
                if (!string.IsNullOrEmpty(ctx.Name) && ctx.Name.EndsWith("+"))
                    pairedIndices.Add(i);
            }

            for (int i = 0; i < drawables.Count; i++)
            {
                if (pairedIndices.Contains(i)) continue;

                var ctx = drawables[i].Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                var isolated = MQOVertexExpandHelper.GetIsolatedVertices(mo);
                int expandedCount = MQOVertexExpandHelper.CalculateExpandedVertexCount(mo, isolated);

                var meshEntry = new PartialMeshEntry
                {
                    Selected = false,
                    Index = i,
                    Name = ctx.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror = false,
                    Context = ctx,
                    IsolatedVertices = isolated
                };

                if (ctx.HasBakedMirrorChild)
                {
                    for (int j = i + 1; j < drawables.Count; j++)
                    {
                        var peerCtx = drawables[j].Context;
                        if (peerCtx == null) continue;
                        if (!peerCtx.IsBakedMirror) break;
                        meshEntry.BakedMirrorPeer = BuildPeerEntry(j, peerCtx);
                        break;
                    }
                }

                if (meshEntry.BakedMirrorPeer == null && !string.IsNullOrEmpty(ctx.Name))
                {
                    string peerName = ctx.Name + "+";
                    for (int j = 0; j < drawables.Count; j++)
                    {
                        if (j == i) continue;
                        if (!pairedIndices.Contains(j)) continue;
                        var peerCtx = drawables[j].Context;
                        if (peerCtx?.Name == peerName && peerCtx.MeshObject != null && peerCtx.MeshObject.VertexCount > 0)
                        {
                            meshEntry.BakedMirrorPeer = BuildPeerEntry(j, peerCtx);
                            break;
                        }
                    }
                }

                ModelMeshes.Add(meshEntry);
            }
        }

        private PartialMeshEntry BuildPeerEntry(int index, MeshContext peerCtx)
        {
            var peerMo = peerCtx.MeshObject;
            var peerIsolated = MQOVertexExpandHelper.GetIsolatedVertices(peerMo);
            int peerExpanded = MQOVertexExpandHelper.CalculateExpandedVertexCount(peerMo, peerIsolated);

            return new PartialMeshEntry
            {
                Selected = false,
                Index = index,
                Name = peerCtx.Name,
                VertexCount = peerMo.VertexCount,
                ExpandedVertexCount = peerExpanded,
                IsBakedMirror = true,
                Context = peerCtx,
                IsolatedVertices = peerIsolated
            };
        }

        // ================================================================
        // MQOリスト構築
        // ================================================================

        public bool LoadMQO(string filePath, bool flipZ, bool visibleOnly)
        {
            MQODocument = null;
            MQOImportResult = null;
            MQOObjects.Clear();

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            try
            {
                MQODocument = MQOParser.ParseFile(filePath);

                var settings = new MQOImportSettings
                {
                    ImportMaterials = false,
                    SkipHiddenObjects = visibleOnly,
                    MergeObjects = false,
                    Scale = 1f,    // 座標変換はTransfer側で行うため生MQO座標のまま格納
                    FlipZ = false, // 同上
                    FlipUV_V = false,
                    BakeMirror = false
                };
                MQOImportResult = MQOImporter.ImportFile(filePath, settings);

                if (MQOImportResult == null || !MQOImportResult.Success)
                    return false;

                BuildMQOList();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MQOPartialMatchHelper] Load failed: {ex.Message}");
                MQODocument = null;
                MQOImportResult = null;
                return false;
            }
        }

        public void BuildMQOList()
        {
            MQOObjects.Clear();
            if (MQOImportResult == null || !MQOImportResult.Success) return;

            foreach (var meshContext in MQOImportResult.MeshContexts)
            {
                var mo = meshContext.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;
                if (meshContext.IsBakedMirror) continue;

                var isolated = MQOVertexExpandHelper.GetIsolatedVertices(mo);
                int expandedCount = MQOVertexExpandHelper.CalculateExpandedVertexCount(mo, isolated);

                MQOObjects.Add(new PartialMQOEntry
                {
                    Selected = false,
                    Index = MQOObjects.Count,
                    Name = meshContext.Name,
                    VertexCount = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    MeshContext = meshContext,
                    IsMirrored = meshContext.IsMirrored,
                    MirrorType = meshContext.MirrorType,
                    MirrorAxis = meshContext.MirrorAxis,
                    MirrorDistance = meshContext.MirrorDistance,
                    MirrorMaterialOffset = meshContext.MirrorMaterialOffset
                });
            }
        }

        // ================================================================
        // 半自動マッチング
        // ================================================================

        public void AutoMatch()
        {
            foreach (var model in ModelMeshes) model.Selected = false;
            foreach (var mqo in MQOObjects) mqo.Selected = false;

            foreach (var model in ModelMeshes)
            {
                int modelTotal = model.TotalExpandedVertexCount;
                if (modelTotal == 0) continue;

                var match = MQOObjects.FirstOrDefault(m =>
                    !m.Selected &&
                    m.ExpandedVertexCountWithMirror == modelTotal &&
                    m.ExpandedVertexCount > 0);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }
        }

        // ================================================================
        // 選択情報取得
        // ================================================================

        public List<PartialMeshEntry> SelectedModelMeshes => ModelMeshes.Where(m => m.Selected).ToList();
        public List<PartialMQOEntry> SelectedMQOObjects => MQOObjects.Where(m => m.Selected).ToList();
        public int SelectedModelVertexCount => ModelMeshes.Where(m => m.Selected).Sum(m => m.TotalExpandedVertexCount);
        public int SelectedMQOVertexCount => MQOObjects.Where(m => m.Selected).Sum(m => m.ExpandedVertexCountWithMirror);
    }
}
