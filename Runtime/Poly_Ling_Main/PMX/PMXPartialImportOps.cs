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
using Poly_Ling.MeshBridge;
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

                // 展開規則は MeshExpansion が唯一の実装（MeshExpansion.cs 冒頭の規則）。
                // PMX エクスポータも MeshObject.BuildExpansionMap() 経由で同じ規則を使うので、
                // ここで別の数え方をすると AutoMatch が PMX の実頂点数と合わなくなる。
                var nonIsolated   = MeshExpansion.BuildNonIsolatedSet(mo);
                int expandedCount = MeshExpansion.CountExpanded(mo, nonIsolated);

                var isolated = new HashSet<int>();
                for (int v = 0; v < mo.VertexCount; v++)
                    if (!nonIsolated.Contains(v)) isolated.Add(v);

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
        //
        // モデル側の頂点は非展開形（1 頂点が UV スロットを n 個持つ）。
        // PMX 側は展開済み（1 頂点 1 UV）。両者の対応は
        // MeshExpansion の展開順（頂点 index 昇順 → UV スロット昇順、
        // 3 頂点以上の面から参照されない頂点は除外）で決まる。
        // PMX エクスポータも MeshObject.BuildExpansionMap() 経由で同じ規則を使う。
        //
        // 位置とボーンウェイトは展開時にスロット数ぶん複製されるので、
        // 逆方向はスロット 0 の 1 個だけを取る。
        // UV はスロットごとに別の値なので、スロットごとに PMX 頂点から受け取る。
        //
        // モデルが PMX 由来の場合は全頂点が UV スロット 1 個で孤立も無いため、
        // 展開は恒等写像になり、従来の 1:1 転送と同じ結果になる。
        //
        // 展開順をここに手書きしてはならない（MeshExpansion.cs 冒頭の指示）。
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

                var nonIsolated  = MeshExpansion.BuildNonIsolatedSet(modelMo);
                int expandedCount = MeshExpansion.CountExpanded(modelMo, nonIsolated);

                if (expandedCount != pmxMo.VertexCount)
                {
                    Debug.LogWarning(
                        $"[PMXPartialImport] '{modelMeshes[p].Name}' " +
                        $"model expanded={expandedCount} ≠ pmx={pmxMo.VertexCount}, " +
                        $"importing {Math.Min(expandedCount, pmxMo.VertexCount)}");
                }

                int updated = 0;

                MeshExpansion.Enumerate(modelMo, (vIdx, uvIdx, expIdx) =>
                {
                    if (expIdx >= pmxMo.VertexCount) return;

                    var dst = modelMo.Vertices[vIdx];
                    var src = pmxMo.Vertices[expIdx];

                    if (uvIdx == 0)
                    {
                        if (position)   dst.Position   = src.Position;
                        if (boneWeight) dst.BoneWeight = src.BoneWeight;
                        updated++;
                    }

                    if (uv && uvIdx < dst.UVs.Count && src.UVs.Count > 0)
                        dst.UVs[uvIdx] = src.UVs[0];
                }, nonIsolated);

                // ウェイトを取り込んだ場合は取り込み先の種別を確定させる。
                if (boneWeight)
                    modelMo.RecomputeSkinKind();

                totalUpdated += updated;
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
                // 面だけを入れ替えたので、線分が無くなった区間で線分群を切る。
                Poly_Ling.Ops.LineGroupOps.ReconcileWithFaces(modelMo);

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

        // 段 7 で旧モーフの基準データの付け替え（RemapMorphBasesAfterVertexChange）を撤去した。
        // 形は頂点が持つので、頂点を差し替えてもこちらで追従させるものは無い。

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
