// PMXPartialImportOps.cs
// PMX部分インポートのロジック層。Editor依存なし。
// MQOPartialMatchHelper と対称な設計。
// Runtime/Poly_Ling_Main/PMX/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.MQO;
using Poly_Ling.UI;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX側メッシュエントリ
    /// </summary>
    public class PartialPMXEntry
    {
        public bool        Selected;
        public int         Index;
        public string      Name;
        public int         VertexCount;
        public MeshContext MeshContext;
    }

    /// <summary>
    /// PMX部分インポートのロジック。
    /// リスト構築・マッチング・各種インポート実行を担う。
    /// UI・Undo・_context 同期は Editor 側パネルが行う。
    /// </summary>
    public class PMXPartialImportOps
    {
        // ================================================================
        // データ
        // ================================================================

        public List<PartialMeshEntry> ModelMeshes    { get; } = new List<PartialMeshEntry>();
        public List<PartialPMXEntry>  PMXMeshes      { get; } = new List<PartialPMXEntry>();
        public PMXImportResult        PMXImportResult { get; private set; }

        // ================================================================
        // モデルリスト構築
        // PMX は個別マッチなのでミラーペア統合しない。
        // ================================================================

        public void BuildModelList(ModelContext model)
        {
            ModelMeshes.Clear();
            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx?.MeshObject == null) continue;

                var mo = ctx.MeshObject;
                if (mo.VertexCount == 0) continue;

                var isolated      = MQOVertexExpandHelper.GetIsolatedVertices(mo);
                int expandedCount = MQOVertexExpandHelper.CalculateExpandedVertexCount(mo, isolated);

                ModelMeshes.Add(new PartialMeshEntry
                {
                    Selected            = false,
                    Index               = i,
                    Name                = ctx.Name,
                    VertexCount         = mo.VertexCount,
                    ExpandedVertexCount = expandedCount,
                    IsBakedMirror       = ctx.IsBakedMirror,
                    Context             = ctx,
                    IsolatedVertices    = isolated,
                    BakedMirrorPeer     = null   // PMX は個別マッチ
                });
            }
        }

        // ================================================================
        // PMXリスト構築
        // ================================================================

        /// <summary>
        /// PMXImporter.ImportFile の結果を受け取り内部リストを構築する。
        /// ファイルIO・ImportSettings の組み立ては Editor 側が行う。
        /// </summary>
        public void LoadPMXResult(PMXImportResult result)
        {
            PMXImportResult = result;
            BuildPMXList();
        }

        private void BuildPMXList()
        {
            PMXMeshes.Clear();
            if (PMXImportResult == null) return;

            int idx = 0;
            foreach (var meshContext in PMXImportResult.MeshContexts)
            {
                if (meshContext.Type == MeshType.Bone) continue;

                var mo = meshContext.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                PMXMeshes.Add(new PartialPMXEntry
                {
                    Selected    = false,
                    Index       = idx++,
                    Name        = meshContext.Name,
                    VertexCount = mo.VertexCount,
                    MeshContext = meshContext
                });
            }
        }

        // ================================================================
        // 自動マッチング（名前優先 → 頂点数フォールバック）
        // ================================================================

        public void AutoMatch()
        {
            foreach (var m in ModelMeshes) m.Selected = false;
            foreach (var p in PMXMeshes)   p.Selected = false;

            // Pass 1: 名前完全一致
            foreach (var model in ModelMeshes)
            {
                if (string.IsNullOrEmpty(model.Name)) continue;
                var match = PMXMeshes.FirstOrDefault(p => !p.Selected && p.Name == model.Name);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }

            // Pass 2: 頂点数一致（未マッチのみ）
            foreach (var model in ModelMeshes)
            {
                if (model.Selected) continue;
                if (model.ExpandedVertexCount == 0) continue;
                var match = PMXMeshes.FirstOrDefault(p =>
                    !p.Selected && p.VertexCount == model.ExpandedVertexCount);
                if (match != null)
                {
                    model.Selected = true;
                    match.Selected = true;
                }
            }
        }

        // ================================================================
        // 選択情報
        // ================================================================

        public List<PartialMeshEntry> SelectedModelMeshes   => ModelMeshes.Where(m => m.Selected).ToList();
        public List<PartialPMXEntry>  SelectedPMXMeshes     => PMXMeshes.Where(m => m.Selected).ToList();
        public int SelectedModelVertexCount => ModelMeshes.Where(m => m.Selected).Sum(m => m.ExpandedVertexCount);
        public int SelectedPMXVertexCount   => PMXMeshes.Where(m => m.Selected).Sum(m => m.VertexCount);

        // ================================================================
        // 頂点属性インポート（位置 / UV / BoneWeight 独立）
        // PMX は展開済みなので 1:1 マッピング。
        // 頂点数が異なる場合は min(N, M) 個を転送し Warning を出す。
        // ================================================================

        public int ExecuteVertexAttributeImport(
            List<PartialMeshEntry> modelMeshes,
            List<PartialPMXEntry>  pmxMeshes,
            bool position,
            bool uv,
            bool boneWeight)
        {
            if (!position && !uv && !boneWeight) return 0;

            int totalUpdated = 0;
            int pairCount    = Math.Min(modelMeshes.Count, pmxMeshes.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelMo = modelMeshes[p].Context?.MeshObject;
                var pmxMo   = pmxMeshes[p].MeshContext?.MeshObject;
                if (modelMo == null || pmxMo == null) continue;

                int count = Math.Min(modelMo.VertexCount, pmxMo.VertexCount);

                if (modelMo.VertexCount != pmxMo.VertexCount)
                {
                    Debug.LogWarning(
                        $"[PMXPartialImport] '{modelMeshes[p].Name}' " +
                        $"model={modelMo.VertexCount} ≠ pmx={pmxMo.VertexCount}, importing {count}");
                }

                for (int i = 0; i < count; i++)
                {
                    var dst = modelMo.Vertices[i];
                    var src = pmxMo.Vertices[i];

                    if (position)
                        dst.Position = src.Position;

                    if (uv)
                    {
                        dst.UVs.Clear();
                        foreach (var v in src.UVs)
                            dst.UVs.Add(v);
                    }

                    if (boneWeight)
                        dst.BoneWeight = src.BoneWeight;
                }

                totalUpdated += count;
            }

            return totalUpdated;
        }

        // ================================================================
        // 面構成インポート（頂点リストは変えず面のみ置き換え）
        // ================================================================

        public int ExecuteFaceStructureImport(
            List<PartialMeshEntry> modelMeshes,
            List<PartialPMXEntry>  pmxMeshes)
        {
            int meshesUpdated = 0;
            int pairCount     = Math.Min(modelMeshes.Count, pmxMeshes.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelMo = modelMeshes[p].Context?.MeshObject;
                var pmxMo   = pmxMeshes[p].MeshContext?.MeshObject;
                if (modelMo == null || pmxMo == null) continue;

                int oldFaces = modelMo.FaceCount;

                modelMo.Faces.Clear();
                foreach (var f in pmxMo.Faces)
                    modelMo.Faces.Add(f.Clone());

                Debug.Log(
                    $"[PMXPartialImport] FaceStructure '{modelMeshes[p].Name}': {oldFaces} → {modelMo.FaceCount} faces");
                meshesUpdated++;
            }

            return meshesUpdated;
        }

        // ================================================================
        // 材質インポート（名前マッチング）
        // ================================================================

        public struct PMXMaterialMatch
        {
            public string            PmxMatName;
            public PMXMaterial       PmxMat;
            public MaterialReference ModelMatRef;
        }

        public List<PMXMaterialMatch> BuildMaterialMatches(ModelContext model)
        {
            var matches = new List<PMXMaterialMatch>();
            if (PMXImportResult?.Document == null || model == null) return matches;

            var modelMats = model.MaterialReferences;
            if (modelMats == null) return matches;

            foreach (var pmxMat in PMXImportResult.Document.Materials)
            {
                var modelRef = modelMats.FirstOrDefault(r => r.Name == pmxMat.Name);
                if (modelRef != null)
                {
                    matches.Add(new PMXMaterialMatch
                    {
                        PmxMatName  = pmxMat.Name,
                        PmxMat      = pmxMat,
                        ModelMatRef = modelRef
                    });
                }
            }

            return matches;
        }

        // ================================================================
        // モーフ基準データ再マッピング
        // 頂点位置またはUVをインポートした後、そのベースメッシュを親に持つ
        // モーフ子メッシュの MorphBaseData を新しいベース形状に合わせて更新する。
        // oldOffset = morphVertex.Position - BasePositions[i] を保持しつつ
        // BasePositions[i] を新ベース位置で更新し、morphVertex.Position も追従させる。
        // indexMaps が指定された場合は old→new インデックスで BasePositions を並べ替える。
        // ================================================================

        /// <summary>
        /// インデックスマップなし版（頂点数・順序が変わらない PMX 頂点属性インポート用）。
        /// </summary>
        public static void RemapMorphBasesAfterVertexChange(
            List<PartialMeshEntry> modelMeshes,
            ModelContext           model,
            bool                   remapPosition,
            bool                   remapUV)
        {
            RemapMorphBasesAfterVertexChange(modelMeshes, model, remapPosition, remapUV, null);
        }

        /// <summary>
        /// インデックスマップあり版（MQO メッシュ構造インポート用）。
        /// indexMaps は modelMeshes と同順で old→new インデックスマップを格納する
        /// （BakedMirrorPeer 分が含まれる場合は modelMeshes よりエントリ数が多くなる）。
        /// </summary>
        public static void RemapMorphBasesAfterVertexChange(
            List<PartialMeshEntry>          modelMeshes,
            ModelContext                    model,
            bool                            remapPosition,
            bool                            remapUV,
            List<Dictionary<int,int>>       indexMaps)
        {
            if (!remapPosition && !remapUV) return;
            if (model == null) return;

            // modelMeshes と indexMaps を対応付ける
            // indexMaps は BakedMirrorPeer 分も含むため、ContextList を使って対応を取る
            // ここでは modelMeshes[p].Context / BakedMirrorPeer.Context の順に対応すると仮定
            var ctxToMap = new Dictionary<MeshContext, Dictionary<int,int>>();
            if (indexMaps != null)
            {
                int mapIdx = 0;
                foreach (var entry in modelMeshes)
                {
                    if (mapIdx < indexMaps.Count)
                        ctxToMap[entry.Context] = indexMaps[mapIdx++];
                    if (entry.BakedMirrorPeer?.Context != null && mapIdx < indexMaps.Count)
                        ctxToMap[entry.BakedMirrorPeer.Context] = indexMaps[mapIdx++];
                }
            }

            foreach (var entry in modelMeshes)
            {
                RemapMorphsForBase(entry.Context, model, remapPosition, remapUV,
                    ctxToMap.TryGetValue(entry.Context, out var m) ? m : null);

                if (entry.BakedMirrorPeer?.Context != null)
                    RemapMorphsForBase(entry.BakedMirrorPeer.Context, model, remapPosition, remapUV,
                        ctxToMap.TryGetValue(entry.BakedMirrorPeer.Context, out var pm) ? pm : null);
            }
        }

        private static void RemapMorphsForBase(
            MeshContext             baseCtx,
            ModelContext            model,
            bool                    remapPosition,
            bool                    remapUV,
            Dictionary<int,int>     indexMap)
        {
            if (baseCtx == null) return;
            var baseMo = baseCtx.MeshObject;
            if (baseMo == null) return;

            int baseIdx = model.MeshContextList.IndexOf(baseCtx);

            foreach (var morphCtx in model.MeshContextList)
            {
                if (!morphCtx.IsMorph) continue;
                if (morphCtx.MorphBaseData == null) continue;

                // 親特定: MorphParentIndex 優先、-1 なら名前マッチング
                int parentIdx = morphCtx.MorphParentIndex;
                if (parentIdx < 0)
                    parentIdx = MorphPreviewState.FindBaseMeshIndex(model, morphCtx);
                if (parentIdx != baseIdx) continue;

                var morphMo = morphCtx.MeshObject;
                if (morphMo == null) continue;

                if (indexMap != null)
                {
                    // インデックス並べ替え: 新頂点数で配列を再構築
                    int newCount = baseMo.VertexCount;

                    if (remapPosition && morphCtx.MorphBaseData.BasePositions != null)
                    {
                        var oldPos = morphCtx.MorphBaseData.BasePositions;
                        var newPos = new Vector3[newCount];
                        foreach (var kvp in indexMap)
                        {
                            int oldI = kvp.Key; int newI = kvp.Value;
                            if (oldI < oldPos.Length && newI < newCount)
                                newPos[newI] = oldPos[oldI];
                        }
                        // morph Vertices も並べ替え
                        if (morphMo.VertexCount >= newCount)
                        {
                            var oldVerts = morphMo.Vertices.Select(v => v.Clone()).ToList();
                            foreach (var kvp in indexMap)
                            {
                                int oldI = kvp.Key; int newI = kvp.Value;
                                if (oldI < oldVerts.Count && newI < morphMo.VertexCount)
                                    morphMo.Vertices[newI].Position = oldVerts[oldI].Position;
                            }
                        }
                        morphCtx.MorphBaseData.BasePositions = newPos;
                    }

                    if (remapUV && morphCtx.MorphBaseData.BaseUVs != null)
                    {
                        var oldUV = morphCtx.MorphBaseData.BaseUVs;
                        var newUV = new Vector2[newCount];
                        foreach (var kvp in indexMap)
                        {
                            int oldI = kvp.Key; int newI = kvp.Value;
                            if (oldI < oldUV.Length && newI < newCount)
                                newUV[newI] = oldUV[oldI];
                        }
                        if (morphMo.VertexCount >= newCount)
                        {
                            var oldVerts = morphMo.Vertices.Select(v => v.Clone()).ToList();
                            foreach (var kvp in indexMap)
                            {
                                int oldI = kvp.Key; int newI = kvp.Value;
                                if (oldI < oldVerts.Count && newI < morphMo.VertexCount
                                    && oldVerts[oldI].UVs.Count > 0)
                                {
                                    if (morphMo.Vertices[newI].UVs.Count == 0)
                                        morphMo.Vertices[newI].UVs.Add(oldVerts[oldI].UVs[0]);
                                    else
                                        morphMo.Vertices[newI].UVs[0] = oldVerts[oldI].UVs[0];
                                }
                            }
                        }
                        morphCtx.MorphBaseData.BaseUVs = newUV;
                    }
                }
                else
                {
                    // 1:1 更新（インデックス変化なし）
                    int count = Mathf.Min(baseMo.VertexCount, morphMo.VertexCount);

                    if (remapPosition && morphCtx.MorphBaseData.BasePositions != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (i >= morphCtx.MorphBaseData.BasePositions.Length) break;
                            Vector3 oldBase   = morphCtx.MorphBaseData.BasePositions[i];
                            Vector3 oldOffset = morphMo.Vertices[i].Position - oldBase;
                            Vector3 newBase   = baseMo.Vertices[i].Position;
                            morphMo.Vertices[i].Position            = newBase + oldOffset;
                            morphCtx.MorphBaseData.BasePositions[i] = newBase;
                        }
                    }

                    if (remapUV && morphCtx.MorphBaseData.BaseUVs != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            if (i >= morphCtx.MorphBaseData.BaseUVs.Length) break;
                            if (morphMo.Vertices[i].UVs.Count == 0) continue;
                            if (baseMo.Vertices[i].UVs.Count  == 0) continue;
                            Vector2 oldBase   = morphCtx.MorphBaseData.BaseUVs[i];
                            Vector2 oldOffset = morphMo.Vertices[i].UVs[0] - oldBase;
                            Vector2 newBase   = baseMo.Vertices[i].UVs[0];
                            morphMo.Vertices[i].UVs[0]           = newBase + oldOffset;
                            morphCtx.MorphBaseData.BaseUVs[i]    = newBase;
                        }
                    }
                }
            }
        }

        public int ExecuteMaterialImport(ModelContext model)
        {
            var matches = BuildMaterialMatches(model);
            int count   = 0;

            foreach (var match in matches)
            {
                var mat = match.ModelMatRef.Material;
                if (mat == null) continue;

                mat.color = match.PmxMat.Diffuse;
                count++;
            }

            return count;
        }
    }
}
