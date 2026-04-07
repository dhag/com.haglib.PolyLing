// MQOPartialImportOps.cs
// MQO部分インポートのロジック層。Editor依存なし。
// PMXPartialImportOps と対称な設計。
// Runtime/Poly_Ling_Main/MQO/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.Ops;
using Poly_Ling.Symmetry;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分インポートのロジック。
    /// リスト構築は MQOPartialMatchHelper に委譲。
    /// 各種インポート実行・法線再計算・材質インポートをここで担う。
    /// UI・Undo・_context 同期は Editor 側パネルが行う。
    /// </summary>
    public class MQOPartialImportOps
    {
        // ================================================================
        // 頂点位置インポート
        // MQO側の頂点位置をモデル側に展開辞書ベースで転送。
        // MQO頂点1個 → PMX展開頂点 UVs.Count 個に同一位置を設定。
        // BakedMirrorPeer がある場合はミラー変換した位置も設定する。
        // ================================================================

        public int ExecuteVertexPositionImport(
            List<PartialMeshEntry> modelMeshes,
            List<PartialMQOEntry>  mqoObjects,
            float importScale,
            bool  flipZ)
        {
            int totalUpdated = 0;
            int pairCount    = Math.Min(modelMeshes.Count, mqoObjects.Count);

            for (int p = 0; p < pairCount; p++)
                totalUpdated += TransferVertexPositions(modelMeshes[p], mqoObjects[p], importScale, flipZ);

            return totalUpdated;
        }

        private int TransferVertexPositions(
            PartialMeshEntry modelEntry,
            PartialMQOEntry  mqoEntry,
            float importScale,
            bool  flipZ)
        {
            var modelMo = modelEntry.Context?.MeshObject;
            var mqoMo   = mqoEntry.MeshContext?.MeshObject;
            if (modelMo == null || mqoMo == null) return 0;

            bool isMirrored = mqoEntry.IsMirrored;
            bool hasPeer    = modelEntry.BakedMirrorPeer != null;
            var  peerMo     = hasPeer ? modelEntry.BakedMirrorPeer.Context?.MeshObject : null;

            var mqoUsed = BuildUsedVertexSet(mqoMo);

            int pmxOffset = 0;
            int updated   = 0;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;

                var    mqoVertex = mqoMo.Vertices[vIdx];
                int    uvCount   = Math.Max(1, mqoVertex.UVs.Count);
                Vector3 pos      = TransformPosition(mqoVertex.Position, importScale, flipZ);

                for (int u = 0; u < uvCount; u++)
                {
                    int idx = pmxOffset + u;
                    if (idx < modelMo.VertexCount)
                    {
                        modelMo.Vertices[idx].Position = pos;
                        updated++;
                    }
                }

                if (isMirrored && hasPeer && peerMo != null)
                {
                    var     mirrorAxis = mqoEntry.MeshContext.GetMirrorSymmetryAxis();
                    Vector3 mirrorPos  = MirrorPosition(pos, mirrorAxis);

                    for (int u = 0; u < uvCount; u++)
                    {
                        int idx = pmxOffset + u;
                        if (idx < peerMo.VertexCount)
                        {
                            peerMo.Vertices[idx].Position = mirrorPos;
                            updated++;
                        }
                    }
                }

                pmxOffset += uvCount;
            }

            return updated;
        }

        // ================================================================
        // 頂点IDインポート
        // MQO側の頂点IDをモデル側に展開辞書に従い転送（1→N対応）。
        // ================================================================

        public int ExecuteVertexIdImport(
            List<PartialMeshEntry> modelMeshes,
            List<PartialMQOEntry>  mqoObjects)
        {
            int totalUpdated = 0;
            int pairCount    = Math.Min(modelMeshes.Count, mqoObjects.Count);

            for (int p = 0; p < pairCount; p++)
                totalUpdated += TransferVertexIds(modelMeshes[p], mqoObjects[p]);

            return totalUpdated;
        }

        private int TransferVertexIds(PartialMeshEntry modelEntry, PartialMQOEntry mqoEntry)
        {
            var modelMo = modelEntry.Context?.MeshObject;
            var mqoMo   = mqoEntry.MeshContext?.MeshObject;
            if (modelMo == null || mqoMo == null) return 0;

            var mqoUsed  = BuildUsedVertexSet(mqoMo);
            int pmxOffset = 0;
            int updated   = 0;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;

                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount   = Math.Max(1, mqoVertex.UVs.Count);
                int vertexId  = mqoVertex.Id;

                if (vertexId >= 0)
                {
                    for (int u = 0; u < uvCount; u++)
                    {
                        int idx = pmxOffset + u;
                        if (idx < modelMo.VertexCount)
                        {
                            modelMo.Vertices[idx].Id = vertexId;
                            modelMo.RegisterVertexId(vertexId);
                            updated++;
                        }
                    }
                }

                pmxOffset += uvCount;
            }

            return updated;
        }

        // ================================================================
        // メッシュ構造インポート
        // MQO側のFace構成・UVをモデル側に転送。
        // PMX展開済み頂点からBoneWeightを引き継ぎ、MQOのUV/面構成で再構築。
        //
        // ミラー分岐:
        //   BakedMirrorPeer あり + ベイク  → 実体→modelMo、ミラー→peerMo に分離書き込み
        //   BakedMirrorPeer なし + ベイク  → 同一 modelMo に実体+ミラー頂点を連結
        //   フラグモード（bakeMirror=false）→ 実体のみ modelMo に書き込み、MirrorBoneWeight 退避
        // ================================================================

        /// <summary>
        /// int 戻り値版ラッパー（Editor 側との後方互換用）。
        /// インデックスマップが不要な呼び出し元はこちらを使用する。
        /// </summary>
        public int ExecuteMeshStructureImportCount(
            List<PartialMeshEntry> modelMeshes,
            List<PartialMQOEntry>  mqoObjects,
            bool  alsoImportPosition,
            float importScale,
            bool  flipZ,
            bool  flipUV_V,
            bool  bakeMirror,
            bool  recalcNormals,
            NormalMode normalMode,
            float smoothingAngle)
        {
            return ExecuteMeshStructureImportWithMap(
                modelMeshes, mqoObjects,
                alsoImportPosition, importScale, flipZ, flipUV_V,
                bakeMirror, recalcNormals, normalMode, smoothingAngle)
                .Count(m => m != null);
        }

        public int ExecuteMeshStructureImport(
            List<PartialMeshEntry> modelMeshes,
            List<PartialMQOEntry>  mqoObjects,
            bool  alsoImportPosition,
            float importScale,
            bool  flipZ,
            bool  flipUV_V,
            bool  bakeMirror,
            bool  recalcNormals,
            NormalMode normalMode,
            float smoothingAngle)
        {
            return ExecuteMeshStructureImportCount(
                modelMeshes, mqoObjects,
                alsoImportPosition, importScale, flipZ, flipUV_V,
                bakeMirror, recalcNormals, normalMode, smoothingAngle);
        }

        public List<Dictionary<int,int>> ExecuteMeshStructureImportWithMap(
            List<PartialMeshEntry> modelMeshes,
            List<PartialMQOEntry>  mqoObjects,
            bool  alsoImportPosition,
            float importScale,
            bool  flipZ,
            bool  flipUV_V,
            bool  bakeMirror,
            bool  recalcNormals,
            NormalMode normalMode,
            float smoothingAngle)
        {
            // 戻り値: modelMeshes 順に対応する old→new インデックスマップのリスト
            // BakedMirrorPeer がある場合は peerMo 分のマップも続けて格納
            var allMaps   = new List<Dictionary<int,int>>();
            int pairCount = Math.Min(modelMeshes.Count, mqoObjects.Count);

            for (int p = 0; p < pairCount; p++)
            {
                var modelEntry = modelMeshes[p];
                var mqoEntry   = mqoObjects[p];

                var mqoMo = mqoEntry.MeshContext?.MeshObject;
                if (modelEntry.Context?.MeshObject == null || mqoMo == null)
                {
                    allMaps.Add(null);
                    continue;
                }

                var maps = TransferMeshStructure(
                    modelEntry, mqoEntry,
                    alsoImportPosition,
                    importScale, flipZ, flipUV_V,
                    bakeMirror, recalcNormals, normalMode, smoothingAngle);

                allMaps.Add(maps.realMap);
                if (maps.mirrorMap != null)
                    allMaps.Add(maps.mirrorMap);
            }

            return allMaps;
        }

        private (Dictionary<int,int> realMap, Dictionary<int,int> mirrorMap) TransferMeshStructure(
            PartialMeshEntry modelEntry,
            PartialMQOEntry  mqoEntry,
            bool  alsoImportPosition,
            float importScale,
            bool  flipZ,
            bool  flipUV_V,
            bool  bakeMirror,
            bool  recalcNormals,
            NormalMode normalMode,
            float smoothingAngle)
        {
            var modelMo     = modelEntry.Context.MeshObject;
            var mqoMo       = mqoEntry.MeshContext.MeshObject;
            bool isMirrored = mqoEntry.IsMirrored;
            bool hasPeer    = modelEntry.BakedMirrorPeer != null;
            var  peerMo     = hasPeer ? modelEntry.BakedMirrorPeer.Context?.MeshObject : null;

            // ── Step1: 現在の頂点リストを退避 ──────────────────────────────

            var oldRealVertices = new List<Vertex>(modelMo.VertexCount);
            foreach (var v in modelMo.Vertices) oldRealVertices.Add(v.Clone());

            var oldMirrorVertices = new List<Vertex>();
            if (hasPeer && peerMo != null)
            {
                foreach (var v in peerMo.Vertices) oldMirrorVertices.Add(v.Clone());
            }

            // ── Step2: MQO非孤立頂点の展開辞書 ────────────────────────────

            var mqoUsed      = BuildUsedVertexSet(mqoMo);
            var expandedStart = new Dictionary<int, int>();
            int realVertexCount = 0;

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;
                expandedStart[vIdx] = realVertexCount;
                realVertexCount    += Math.Max(1, mqoMo.Vertices[vIdx].UVs.Count);
            }

            // ペアなし時、ミラー頂点の参照元オフセット
            int mirrorOffsetInOld = hasPeer ? 0 : realVertexCount;

            // ── Step3: 実体側の新頂点リスト構築 ───────────────────────────

            var newRealVertices = new List<Vertex>();
            var newRealStartMap = new Dictionary<int, int>();

            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                if (!mqoUsed.Contains(vIdx)) continue;
                newRealStartMap[vIdx] = newRealVertices.Count;

                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount   = Math.Max(1, mqoVertex.UVs.Count);
                int pmxStart  = expandedStart[vIdx];

                for (int u = 0; u < uvCount; u++)
                {
                    int    oldIdx = pmxStart + u;
                    Vertex newV   = (oldIdx < oldRealVertices.Count)
                        ? oldRealVertices[oldIdx].Clone() : new Vertex();

                    newV.UVs.Clear();
                    Vector2 uv = (u < mqoVertex.UVs.Count) ? mqoVertex.UVs[u] : Vector2.zero;
                    if (flipUV_V) uv.y = 1f - uv.y;
                    newV.UVs.Add(uv);

                    if (alsoImportPosition)
                        newV.Position = TransformPosition(mqoVertex.Position, importScale, flipZ);

                    if (isMirrored && !bakeMirror)
                    {
                        var mirrorSrc  = hasPeer ? oldMirrorVertices : oldRealVertices;
                        int mirrorIdx  = mirrorOffsetInOld + pmxStart + u;
                        if (mirrorIdx < mirrorSrc.Count)
                            newV.MirrorBoneWeight = mirrorSrc[mirrorIdx].BoneWeight;
                    }

                    newRealVertices.Add(newV);
                }
            }

            // ── Step4: ミラー側の新頂点リスト構築（ベイク時のみ） ──────────

            var newMirrorVertices = new List<Vertex>();
            var newMirrorStartMap = new Dictionary<int, int>();

            if (isMirrored && bakeMirror)
            {
                var     mirrorAxis = mqoEntry.MeshContext.GetMirrorSymmetryAxis();
                var     mirrorSrc  = hasPeer ? oldMirrorVertices : oldRealVertices;

                for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
                {
                    if (!mqoUsed.Contains(vIdx)) continue;
                    newMirrorStartMap[vIdx] = newMirrorVertices.Count;

                    var mqoVertex = mqoMo.Vertices[vIdx];
                    int uvCount   = Math.Max(1, mqoVertex.UVs.Count);
                    int pmxStart  = expandedStart[vIdx];

                    for (int u = 0; u < uvCount; u++)
                    {
                        int    oldMirrorIdx = mirrorOffsetInOld + pmxStart + u;
                        Vertex newV         = (oldMirrorIdx < mirrorSrc.Count)
                            ? mirrorSrc[oldMirrorIdx].Clone() : new Vertex();

                        newV.UVs.Clear();
                        Vector2 uv = (u < mqoVertex.UVs.Count) ? mqoVertex.UVs[u] : Vector2.zero;
                        if (flipUV_V) uv.y = 1f - uv.y;
                        newV.UVs.Add(uv);

                        if (alsoImportPosition)
                        {
                            Vector3 pos = TransformPosition(mqoVertex.Position, importScale, flipZ);
                            newV.Position = MirrorPosition(pos, mirrorAxis);
                        }

                        newV.MirrorBoneWeight = null;
                        newMirrorVertices.Add(newV);
                    }
                }
            }

            // ── Step5: 面のインデックスをリマップ ─────────────────────────

            var newRealFaces = BuildRemappedFaces(mqoMo, newRealStartMap);

            var newMirrorFaces = new List<Face>();
            if (isMirrored && bakeMirror)
            {
                int matOffset = mqoEntry.MirrorMaterialOffset;
                newMirrorFaces = BuildRemappedMirrorFaces(mqoMo, newMirrorStartMap, matOffset);
            }

            // ── MeshObjectに反映 ──────────────────────────────────────────

            if (hasPeer && peerMo != null && isMirrored && bakeMirror)
            {
                // ペアあり: 実体→modelMo、ミラー→peerMo に分離書き込み
                ApplyToMeshObject(modelMo, newRealVertices,   newRealFaces,   recalcNormals, normalMode, smoothingAngle);
                ApplyToMeshObject(peerMo,  newMirrorVertices, newMirrorFaces, recalcNormals, normalMode, smoothingAngle);

                Debug.Log(
                    $"[MQOPartialImport] MeshStructure: " +
                    $"real={newRealVertices.Count}v/{newRealFaces.Count}f, " +
                    $"mirror={newMirrorVertices.Count}v/{newMirrorFaces.Count}f (peer split)");

                return (BuildIndexMap(expandedStart, newRealStartMap, mqoMo),
                        BuildIndexMap(expandedStart, newMirrorStartMap, mqoMo));
            }
            else
            {
                // ペアなし or フラグモード: すべて modelMo に書き込み
                var finalVertices = new List<Vertex>(newRealVertices);
                var finalFaces    = new List<Face>(newRealFaces);

                if (isMirrored && bakeMirror)
                {
                    int offset = newRealVertices.Count;
                    foreach (var face in newMirrorFaces)
                    {
                        for (int i = 0; i < face.VertexIndices.Count; i++)
                            face.VertexIndices[i] += offset;
                    }
                    finalVertices.AddRange(newMirrorVertices);
                    finalFaces.AddRange(newMirrorFaces);
                }

                ApplyToMeshObject(modelMo, finalVertices, finalFaces, recalcNormals, normalMode, smoothingAngle);

                Debug.Log(
                    $"[MQOPartialImport] MeshStructure: " +
                    $"old={oldRealVertices.Count} → new={modelMo.VertexCount}v/{modelMo.FaceCount}f" +
                    (isMirrored ? $" (mirror={(bakeMirror ? "bake" : "flag")})" : ""));

                return (BuildIndexMap(expandedStart, newRealStartMap, mqoMo), null);
            }
        }

        /// <summary>
        /// MQO頂点ごとの旧展開インデックス(expandedStart)と新インデックス(newStartMap)から
        /// oldIdx→newIdx の全展開頂点マッピングを構築する。
        /// </summary>
        private static Dictionary<int,int> BuildIndexMap(
            Dictionary<int,int> expandedStart,
            Dictionary<int,int> newStartMap,
            MeshObject          mqoMo)
        {
            var map = new Dictionary<int,int>();
            foreach (var kvp in expandedStart)
            {
                int vIdx    = kvp.Key;
                int oldBase = kvp.Value;
                if (!newStartMap.TryGetValue(vIdx, out int newBase)) continue;
                int uvCount = Math.Max(1, mqoMo.Vertices[vIdx].UVs.Count);
                for (int u = 0; u < uvCount; u++)
                    map[oldBase + u] = newBase + u;
            }
            return map;
        }

        // ================================================================
        // 材質インポート（名前マッチング）
        // ================================================================

        public struct MQOMaterialMatch
        {
            public MQOMaterial       MqoMaterial;
            public MaterialReference ModelMaterialRef;
            public int               ModelMaterialIndex;
        }

        public List<MQOMaterialMatch> BuildMaterialMatches(
            ModelContext model,
            MQODocument  mqoDocument)
        {
            var matches = new List<MQOMaterialMatch>();
            if (mqoDocument == null || model == null) return matches;

            var modelMatRefs = model.MaterialReferences;
            if (modelMatRefs == null) return matches;

            foreach (var mqoMat in mqoDocument.Materials)
            {
                for (int i = 0; i < modelMatRefs.Count; i++)
                {
                    var modelRef = modelMatRefs[i];
                    if (modelRef?.Name == mqoMat.Name)
                    {
                        matches.Add(new MQOMaterialMatch
                        {
                            MqoMaterial        = mqoMat,
                            ModelMaterialRef   = modelRef,
                            ModelMaterialIndex = i
                        });
                        break;
                    }
                }
            }

            return matches;
        }

        public int ExecuteMaterialImport(ModelContext model, MQODocument mqoDocument)
        {
            var matches = BuildMaterialMatches(model, mqoDocument);
            int updated = 0;

            foreach (var match in matches)
            {
                var mqoMat   = match.MqoMaterial;
                var modelRef = match.ModelMaterialRef;

                var data = modelRef.Data;
                if (data == null) continue;

                data.SetBaseColor(mqoMat.Color);
                data.Smoothness = mqoMat.Specular;

                if (!string.IsNullOrEmpty(mqoMat.TexturePath))
                    data.SourceTexturePath = mqoMat.TexturePath;
                if (!string.IsNullOrEmpty(mqoMat.AlphaMapPath))
                    data.SourceAlphaMapPath = mqoMat.AlphaMapPath;
                if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
                    data.SourceBumpMapPath = mqoMat.BumpMapPath;

                if (mqoMat.Color.a < 1f - 0.001f)
                    data.Surface = SurfaceType.Transparent;

                modelRef.RefreshFromData();
                updated++;

                Debug.Log($"[MQOPartialImport] Material updated: {mqoMat.Name}");
            }

            return updated;
        }

        // ================================================================
        // 法線再計算
        // ================================================================

        public void RecalculateNormals(MeshObject mo, NormalMode normalMode, float smoothingAngle)
        {
            if (normalMode == NormalMode.FaceNormal)
            {
                foreach (var vertex in mo.Vertices)
                    vertex.Normals.Clear();

                foreach (var face in mo.Faces)
                {
                    if (face.VertexCount < 3) continue;
                    var v0     = mo.Vertices[face.VertexIndices[0]].Position;
                    var v1     = mo.Vertices[face.VertexIndices[1]].Position;
                    var v2     = mo.Vertices[face.VertexIndices[2]].Position;
                    var normal = NormalHelper.CalculateFaceNormal(v0, v1, v2);

                    face.NormalIndices.Clear();
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int vIdx = face.VertexIndices[i];
                        int nIdx = mo.Vertices[vIdx].GetOrAddNormal(normal, 0.0001f);
                        face.NormalIndices.Add(nIdx);
                    }
                }
            }
            else if (normalMode == NormalMode.Smooth)
            {
                SmoothNormals(mo, smoothingAngle);
            }
            // NormalMode.Unity: ToUnityMeshShared 側で RecalculateNormals が呼ばれるため何もしない
        }

        private void SmoothNormals(MeshObject mo, float angle)
        {
            float cosThreshold = Mathf.Cos(angle * Mathf.Deg2Rad);

            var faceNormals = new List<Vector3>(mo.Faces.Count);
            foreach (var face in mo.Faces)
            {
                if (face.VertexCount < 3) { faceNormals.Add(Vector3.up); continue; }
                var v0 = mo.Vertices[face.VertexIndices[0]].Position;
                var v1 = mo.Vertices[face.VertexIndices[1]].Position;
                var v2 = mo.Vertices[face.VertexIndices[2]].Position;
                faceNormals.Add(NormalHelper.CalculateFaceNormal(v0, v1, v2));
            }

            var vertexFaces = new Dictionary<int, List<int>>();
            for (int fIdx = 0; fIdx < mo.Faces.Count; fIdx++)
            {
                foreach (var vIdx in mo.Faces[fIdx].VertexIndices)
                {
                    if (!vertexFaces.ContainsKey(vIdx))
                        vertexFaces[vIdx] = new List<int>();
                    vertexFaces[vIdx].Add(fIdx);
                }
            }

            foreach (var vertex in mo.Vertices) vertex.Normals.Clear();

            for (int fIdx = 0; fIdx < mo.Faces.Count; fIdx++)
            {
                var face = mo.Faces[fIdx];
                var fn   = faceNormals[fIdx];
                face.NormalIndices.Clear();

                for (int i = 0; i < face.VertexCount; i++)
                {
                    int     vIdx    = face.VertexIndices[i];
                    Vector3 smoothed = fn;

                    if (vertexFaces.TryGetValue(vIdx, out var adjFaces))
                    {
                        smoothed = Vector3.zero;
                        foreach (var adjFIdx in adjFaces)
                        {
                            if (Vector3.Dot(fn, faceNormals[adjFIdx]) >= cosThreshold)
                                smoothed += faceNormals[adjFIdx];
                        }
                        smoothed = smoothed.normalized;
                        if (smoothed == Vector3.zero) smoothed = fn;
                    }

                    int nIdx = mo.Vertices[vIdx].GetOrAddNormal(smoothed, 0.001f);
                    face.NormalIndices.Add(nIdx);
                }
            }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private static HashSet<int> BuildUsedVertexSet(MeshObject mo)
        {
            var used = new HashSet<int>();
            foreach (var face in mo.Faces)
                foreach (var vi in face.VertexIndices)
                    used.Add(vi);
            return used;
        }

        private static Vector3 TransformPosition(Vector3 pos, float scale, bool flipZ)
        {
            pos *= scale;
            if (flipZ) pos.z = -pos.z;
            return pos;
        }

        private static Vector3 MirrorPosition(Vector3 pos, SymmetryAxis axis)
        {
            switch (axis)
            {
                case SymmetryAxis.X: return new Vector3(-pos.x,  pos.y,  pos.z);
                case SymmetryAxis.Y: return new Vector3( pos.x, -pos.y,  pos.z);
                case SymmetryAxis.Z: return new Vector3( pos.x,  pos.y, -pos.z);
                default:             return new Vector3(-pos.x,  pos.y,  pos.z);
            }
        }

        private static List<Face> BuildRemappedFaces(
            MeshObject          mqoMo,
            Dictionary<int,int> startMap)
        {
            var faces = new List<Face>();
            foreach (var mqoFace in mqoMo.Faces)
            {
                if (mqoFace.VertexCount < 3) continue;
                var newFace = new Face { MaterialIndex = mqoFace.MaterialIndex };

                for (int i = 0; i < mqoFace.VertexCount; i++)
                {
                    int bIdx     = mqoFace.VertexIndices[i];
                    int uvSubIdx = (i < mqoFace.UVIndices.Count) ? mqoFace.UVIndices[i] : 0;
                    int newBase  = startMap.TryGetValue(bIdx, out int nb) ? nb : 0;

                    newFace.VertexIndices.Add(newBase + uvSubIdx);
                    newFace.UVIndices.Add(0);
                    newFace.NormalIndices.Add(0);
                }
                faces.Add(newFace);
            }
            return faces;
        }

        private static List<Face> BuildRemappedMirrorFaces(
            MeshObject          mqoMo,
            Dictionary<int,int> mirrorStartMap,
            int                 matOffset)
        {
            var faces = new List<Face>();
            foreach (var mqoFace in mqoMo.Faces)
            {
                if (mqoFace.VertexCount < 3) continue;
                var mirrorFace = new Face { MaterialIndex = mqoFace.MaterialIndex + matOffset };

                // 頂点順序を反転（法線方向維持）
                for (int i = mqoFace.VertexCount - 1; i >= 0; i--)
                {
                    int bIdx     = mqoFace.VertexIndices[i];
                    int uvSubIdx = (i < mqoFace.UVIndices.Count) ? mqoFace.UVIndices[i] : 0;
                    int newBase  = mirrorStartMap.TryGetValue(bIdx, out int nb) ? nb : 0;

                    mirrorFace.VertexIndices.Add(newBase + uvSubIdx);
                    mirrorFace.UVIndices.Add(0);
                    mirrorFace.NormalIndices.Add(0);
                }
                faces.Add(mirrorFace);
            }
            return faces;
        }

        private void ApplyToMeshObject(
            MeshObject    mo,
            List<Vertex>  vertices,
            List<Face>    faces,
            bool          recalcNormals,
            NormalMode    normalMode,
            float         smoothingAngle)
        {
            mo.Vertices.Clear();
            mo.Vertices.AddRange(vertices);
            mo.Faces.Clear();
            mo.Faces.AddRange(faces);

            if (recalcNormals)
                RecalculateNormals(mo, normalMode, smoothingAngle);
        }
    }
}
