// PMXExporter.Mesh.cs
// PMX エクスポート：メッシュ変換と座標変換。
// Runtime/Poly_Ling_Main/PMX/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.PMX
{
    public static partial class PMXExporter
    {
        // ================================================================
        // メッシュ変換
        // ================================================================

        /// <summary>
        /// メッシュを PMX 頂点・面へ変換する。
        /// 戻り値: 描画オブジェクト名 → (vIdx, uvIdx) → PMX 頂点番号。
        /// モーフのオフセットを頂点番号へ写すのに使う。
        /// </summary>
        private static Dictionary<string, Dictionary<(int vIdx, int uvIdx), int>> ConvertMeshes(
            List<MeshContext> meshContexts,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            var vertexMaps = new Dictionary<string, Dictionary<(int vIdx, int uvIdx), int>>();

            // メッシュをObjectName別にグループ化
            // MeshContext.Name をObjectNameとして使用
            var objectGroups = new Dictionary<string, List<MeshContext>>();
            var groupOrder = new List<string>();

            foreach (var ctx in meshContexts)
            {
                if (ctx?.MeshObject == null) continue;
                // 空メッシュかつミラーはスキップ（ミラー材質を作らない）
                if (ctx.MeshObject.VertexCount == 0 && (ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide)) continue;
                if (ctx.Type == MeshType.Morph) continue;  // モーフは除外
                if (ctx.Type == MeshType.RigidBody || ctx.Type == MeshType.RigidBodyJoint) continue;  // 剛体/JOINTはメッシュ変換対象外（頂点ゼロのメタデータ専用）
                if (ctx.ExcludeFromExport) continue;       // エクスポート除外

                string objectName = ctx.Name ?? "Unnamed";
                
                // BakedMirror/MirrorSideはミラー側として扱う
                bool isMirror = ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide;
                // BakedMirrorのみ"+"除去して実体側と同一グループに統合
                if (ctx.IsBakedMirror && objectName.EndsWith("+"))
                {
                    objectName = objectName.TrimEnd('+');
                }

                if (!objectGroups.ContainsKey(objectName))
                {
                    objectGroups[objectName] = new List<MeshContext>();
                    groupOrder.Add(objectName);
                }
                objectGroups[objectName].Add(ctx);
            }

            // 頂点はオブジェクト順のまま出す（取り込み時の順序＝元の PMX の頂点順）。
            // 面はいったん貯めておき、全オブジェクトを処理したあとで材質順に並べ替える。
            //
            // PMX は「材質 i の面は面配列の連続した区間」という前提で読まれるが、
            // 1 つのオブジェクトが複数材質を持つ場合や、複数のオブジェクトが同じ材質を
            // 参照する場合があるため、オブジェクト単位で面を出すと材質の区間が分断される。
            // PMX 追加仕様の「面リストは並び替えてもよいが同一材質内での並び順は保持する」
            // に従い、頂点順と面順を切り離す。
            var pendingFaces = new List<(int matIndex, PMXFace face)>();

            foreach (var objectName in groupOrder)
            {
                foreach (var ctx in objectGroups[objectName])
                {
                    bool isMirror = ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide;
                    var map = ConvertSingleMeshWithObjectName(
                        ctx, document, boneNameToIndex, settings, objectName, isMirror, pendingFaces);
                    if (ctx?.Name != null) vertexMaps[ctx.Name] = map;
                }
            }

            // 材質番号で安定ソートして書き出す。
            // OrderBy は安定なので、同一材質内ではオブジェクト順・面順がそのまま残る。
            foreach (var (_, face) in pendingFaces.OrderBy(e => e.matIndex))
            {
                face.FaceIndex = document.Faces.Count;
                document.Faces.Add(face);
            }

            // 材質の面数を更新
            UpdateMaterialFaceCounts(document);

            Debug.Log($"[PMXExporter] Converted {document.Vertices.Count} vertices, {document.Faces.Count} faces");

            return vertexMaps;
        }

        /// <summary>
        /// 単一MeshContextをPMX形式に変換（ObjectName対応）
        /// </summary>
        private static Dictionary<(int vIdx, int uvIdx), int> ConvertSingleMeshWithObjectName(
            MeshContext ctx,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings,
            string objectName,
            bool isMirror,
            List<(int matIndex, PMXFace face)> pendingFaces)
        {
            var meshObject = ctx.MeshObject;

            // UV展開しながら頂点をdocumentに追加
            var vertexMapping = AppendExpandedVertices(meshObject, document, boneNameToIndex, settings);

            // 面を材質ごとにグループ化（同一材質内の面順序は保持）
            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in meshObject.Faces)
            {
                if (!facesByMaterial.ContainsKey(face.MaterialIndex))
                    facesByMaterial[face.MaterialIndex] = new List<Face>();
                facesByMaterial[face.MaterialIndex].Add(face);
            }

            // 材質インデックス順に面を追加
            foreach (var matIndex in facesByMaterial.Keys.OrderBy(k => k))
            {
                var faces = facesByMaterial[matIndex];
                string materialName = matIndex < document.Materials.Count
                    ? document.Materials[matIndex].Name
                    : $"Material_{matIndex}";

                foreach (var face in faces)
                {
                    if (face.VertexIndices.Count < 3) continue;

                    // 三角形に分割（fan triangulation）
                    for (int i = 0; i < face.VertexIndices.Count - 2; i++)
                    {
                        int vi0 = face.VertexIndices[0];
                        int vi1 = face.VertexIndices[i + 1];
                        int vi2 = face.VertexIndices[i + 2];
                        int uv0 = face.UVIndices.Count > 0 ? face.UVIndices[0] : 0;
                        int uv1 = face.UVIndices.Count > i + 1 ? face.UVIndices[i + 1] : 0;
                        int uv2 = face.UVIndices.Count > i + 2 ? face.UVIndices[i + 2] : 0;

                        if (!vertexMapping.TryGetValue((vi0, uv0), out int v0)) continue;
                        if (!vertexMapping.TryGetValue((vi1, uv1), out int v1)) continue;
                        if (!vertexMapping.TryGetValue((vi2, uv2), out int v2)) continue;

                        var pmxFace = new PMXFace
                        {
                            MaterialName = materialName,
                            MaterialIndex = matIndex,
                            VertexIndex1 = v0,
                            VertexIndex2 = AxisFlipOps.ReverseWinding(settings.Flip) ? v2 : v1,
                            VertexIndex3 = AxisFlipOps.ReverseWinding(settings.Flip) ? v1 : v2
                        };

                        // FaceIndex は材質順に並べ替えたあとで振る
                        pendingFaces.Add((matIndex, pmxFace));
                    }
                }

                // 材質のMemo欄にObjectNameを設定
                SetMaterialObjectName(document, matIndex, objectName, isMirror, ctx.Depth);
            }

            // 空メッシュの場合: facesByMaterialが空のためMemoが設定されない → PMXMaterialNamesから設定
            if (meshObject.Faces.Count == 0 && ctx.PMXMaterialNames != null)
            {
                foreach (var matName in ctx.PMXMaterialNames)
                {
                    int matIdx = document.GetMaterialIndex(matName);
                    if (matIdx >= 0)
                        SetMaterialObjectName(document, matIdx, objectName, isMirror, ctx.Depth);
                }
            }

            // PolyLingメタUVモーフを生成（頂点ID・UVサブインデックス保存用）
            BuildPolyLingMetaMorph(ctx, document, vertexMapping, objectName);

            return vertexMapping;
        }

        /// <summary>
        /// MeshObject を (vIdx, uvIdx) で UV展開しながら PMX 頂点を document に追加する。
        /// 戻り値: (vIdx, uvIdx) → document上のPMX頂点インデックス
        /// </summary>
        private static Dictionary<(int vIdx, int uvIdx), int> AppendExpandedVertices(
            MeshObject meshObject,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            int meshVertexStart = document.Vertices.Count;
            var localMap = meshObject.BuildExpansionMap();

            // ローカルインデックスをdocumentグローバルインデックスにオフセット
            var vertexMapping = new Dictionary<(int vIdx, int uvIdx), int>(localMap.Count);
            foreach (var kv in localMap)
                vertexMapping[kv.Key] = kv.Value + meshVertexStart;

            // 展開順と孤立判定は MeshExpansion が唯一の実装（手書きしない）。
            // localMap も MeshObject.BuildExpansionMap 経由で同じ規則を使っている。
            MeshExpansion.Enumerate(meshObject, (vIdx, uvIdx, expIdx) =>
            {
                var vertex    = meshObject.Vertices[vIdx];
                var pmxVertex = ConvertVertex(vertex, boneNameToIndex, settings);

                Vector2 uv = uvIdx < vertex.UVs.Count ? vertex.UVs[uvIdx] : Vector2.zero;
                if (settings.FlipUV_V) uv.y = 1f - uv.y;

                // 法線は UV と対のスロット（基本データ仕様: UV Vector2[n] / Normal Vector3[n]）。
                // ConvertVertex はスロット 0 で埋めるので、ここで uvIdx のものに差し替える。
                // 差し替えないと、UV が分かれている頂点の 2 番目以降が
                // スロット 0 の法線を受け取り、陰影が崩れる。
                if (uvIdx < vertex.Normals.Count)
                    pmxVertex.Normal = ConvertNormal(vertex.Normals[uvIdx], settings);

                pmxVertex.UV    = uv;
                pmxVertex.Index = document.Vertices.Count;
                document.Vertices.Add(pmxVertex);
            });

            return vertexMapping;
        }

        /// <summary>
        /// 材質のMemo欄にObjectNameを設定
        /// </summary>
        // PolyLingメタモーフのプレフィックス
        private const string PolyLingMetaMorphPrefix = "__PLM_";

        /// <summary>
        /// PolyLingメタUVモーフを生成。
        /// 頂点ID・UVサブインデックスをPMXのUVモーフに記録する。
        /// Offset = (-1, uvSubIndex, localVertexIndex, Vertex.Id)
        /// </summary>
        private static void BuildPolyLingMetaMorph(
            MeshContext ctx,
            PMXDocument document,
            Dictionary<(int vIdx, int uvIdx), int> vertexMapping,
            string objectName)
        {
            var meshObject = ctx.MeshObject;
            if (meshObject == null || meshObject.VertexCount == 0) return;

            // 頂点インデックス → UVサブインデックス（最初に見つかった面コーナーの値）
            var vertexUVSubIndex = new Dictionary<int, int>();
            foreach (var face in meshObject.Faces)
            {
                for (int ci = 0; ci < face.VertexIndices.Count && ci < face.UVIndices.Count; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (!vertexUVSubIndex.ContainsKey(vi))
                        vertexUVSubIndex[vi] = face.UVIndices[ci];
                }
            }

            var morph = new PMXMorph
            {
                Name = PolyLingMetaMorphPrefix + objectName,
                NameEnglish = PolyLingMetaMorphPrefix + objectName,
                Panel = 0,
                MorphType = 3  // UVモーフ
            };

            for (int localIndex = 0; localIndex < meshObject.VertexCount; localIndex++)
            {
                var vertex = meshObject.Vertices[localIndex];
                int uvSubIndex = vertexUVSubIndex.TryGetValue(localIndex, out int s) ? s : 0;

                if (!vertexMapping.TryGetValue((localIndex, uvSubIndex), out int pmxIdx)) continue;

                morph.Offsets.Add(new PMXUVMorphOffset
                {
                    VertexIndex = pmxIdx,
                    Offset = new Vector4(-1f, uvSubIndex, localIndex, vertex.Id)
                });
            }

            document.Morphs.Add(morph);
        }

        private static void SetMaterialObjectName(
            PMXDocument document,
            int materialIndex,
            string objectName,
            bool isMirror,
            int depth = -1)
        {
            if (materialIndex < 0 || materialIndex >= document.Materials.Count)
                return;

            var mat = document.Materials[materialIndex];
            string newMemo = PMXHelper.BuildMaterialMemo(objectName, isMirror, depth);

            if (string.IsNullOrEmpty(newMemo))
                return;

            // 既存のMemoがある場合、ObjectName関連の既存データを削除してから追加
            if (!string.IsNullOrEmpty(mat.Memo))
            {
                // 既存のObjectName/IsMirrorを除去
                var existingParts = mat.Memo.Split(',')
                    .Select(p => p.Trim())
                    .ToList();

                var cleanedParts = new List<string>();
                for (int i = 0; i < existingParts.Count; i++)
                {
                    var part = existingParts[i];
                    if (part.Equals("ObjectName", StringComparison.OrdinalIgnoreCase))
                    {
                        // ObjectName,値 の値部分もスキップ
                        if (i + 1 < existingParts.Count)
                            i++;
                        continue;
                    }
                    if (part.Equals("IsMirror", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    cleanedParts.Add(part);
                }

                // クリーンアップ後の既存データと新しいデータを結合
                if (cleanedParts.Count > 0)
                    mat.Memo = string.Join(",", cleanedParts) + "," + newMemo;
                else
                    mat.Memo = newMemo;
            }
            else
            {
                mat.Memo = newMemo;
            }
        }

        private static PMXVertex ConvertVertex(
            Vertex vertex,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            // 座標変換
            Vector3 position = ConvertPosition(vertex.Position, settings);
            Vector3 normal = vertex.Normals.Count > 0
                ? ConvertNormal(vertex.Normals[0], settings)
                : Vector3.up;
            Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
            if (settings.FlipUV_V)
                uv.y = 1f - uv.y;

            var pmxVertex = new PMXVertex
            {
                Position = position,
                Normal = normal,
                UV = uv,
                EdgeScale = 1f,
                WeightType = 0  // BDEF1
            };

            // ボーンウェイト変換
            if (vertex.HasBoneWeight)
            {
                var bw = vertex.BoneWeight.Value;
                var weights = new List<PMXBoneWeight>();

                // ウェイトがある場合のみ追加
                if (bw.weight0 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex0, boneNameToIndex), Weight = bw.weight0 });
                if (bw.weight1 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex1, boneNameToIndex), Weight = bw.weight1 });
                if (bw.weight2 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex2, boneNameToIndex), Weight = bw.weight2 });
                if (bw.weight3 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex3, boneNameToIndex), Weight = bw.weight3 });

                pmxVertex.BoneWeights = weights.ToArray();
                pmxVertex.WeightType = weights.Count switch
                {
                    1 => 0,  // BDEF1
                    2 => 1,  // BDEF2
                    _ => 2   // BDEF4
                };
            }
            else
            {
                // デフォルト：最初のボーンに100%
                pmxVertex.BoneWeights = new[]
                {
                    new PMXBoneWeight { BoneName = boneNameToIndex.Keys.FirstOrDefault() ?? "", Weight = 1f }
                };
            }

            return pmxVertex;
        }

        private static string GetBoneName(int boneIndex, Dictionary<string, int> boneNameToIndex)
        {
            foreach (var kvp in boneNameToIndex)
            {
                if (kvp.Value == boneIndex)
                    return kvp.Key;
            }
            return boneNameToIndex.Keys.FirstOrDefault() ?? "";
        }

        private static void UpdateMaterialFaceCounts(PMXDocument document)
        {
            // 材質ごとの面数をカウント
            var materialFaceCounts = new Dictionary<string, int>();
            foreach (var face in document.Faces)
            {
                if (!materialFaceCounts.ContainsKey(face.MaterialName))
                    materialFaceCounts[face.MaterialName] = 0;
                materialFaceCounts[face.MaterialName]++;
            }

            // 材質に設定
            foreach (var mat in document.Materials)
            {
                mat.FaceCount = materialFaceCounts.TryGetValue(mat.Name, out int count) ? count : 0;
            }
        }

        private class VertexReplaceData
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pos, PMXExportSettings settings)
        {
            return AxisFlipOps.Position(settings.Flip, pos, settings.Scale);
        }

        private static Vector3 ConvertNormal(Vector3 normal, PMXExportSettings settings)
        {
            return AxisFlipOps.Normal(settings.Flip, normal);
        }
    }
}
