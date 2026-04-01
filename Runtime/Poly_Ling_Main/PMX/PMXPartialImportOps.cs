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
