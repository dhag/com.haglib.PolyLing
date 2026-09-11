// MQOImporter.Object.cs
// MQO インポート：オブジェクト変換と面変換。
// Runtime/Poly_Ling_Main/MQO/Import/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.EditorBridge;
using Poly_Ling.PMX;
using Poly_Ling.Symmetry;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.MQO
{
    public static partial class MQOImporter
    {
        // ================================================================
        // オブジェクト変換
        // ================================================================

        private static MeshContext ConvertObject(
            MQOObject mqoObj,
            List<MQOMaterial> mqoMaterials,
            List<Material> unityMaterials,
            MQOImportSettings settings,
            MQOImportStats stats,
            int mirrorMaterialOffset = 0)
        {
            var meshObject = new MeshObject();
            meshObject.Type = MeshType.Mesh;  // 明示的に設定

            // MQO は形式として頂点法線を持たず、読み込み時に必ずスムージング角から
            // 計算する（下の NormalMode 分岐を参照）。保持すべき元の法線が存在しない
            // ため、自動計算を有効にする（= PreserveNormals を false にする）。
            //
            // NormalMode.Unity の場合は ToUnityMesh 側の RecalculateNormals に委ねるが、
            // そちらは PreserveNormals で gate されている（MeshBridgeDefault ほか）ため、
            // MeshObject の既定値 true のままだと法線が一切生成されない。
            // よって NormalMode に依存させず無条件に設定する。
            meshObject.PreserveNormals = false;

            // 頂点変換（IDは後で設定）
            foreach (var mqoVert in mqoObj.Vertices)
            {
                Vector3 pos = ConvertPosition(mqoVert.Position, settings);
                var vertex = new Vertex(pos);
                vertex.Id = -1;  // 初期値: IDなし
                meshObject.AddVertexRaw(vertex);  // ID管理なしで追加
                stats.TotalVertices++;
            }

            // 特殊面から頂点の識別子を抽出（VertexIdHelper使用）
            // 三角形特殊面の COL を (PartsID, SubID, ID) として読む。
            // COL の値によるパターン判定は行わない。
            var vertexIdMap = VertexIdHelper.ExtractIdsFromSpecialFaces(mqoObj.Faces);
            foreach (var kvp in vertexIdMap)
            {
                int vertIndex = kvp.Key;
                var ids = kvp.Value;

                if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                {
                    var vertex = meshObject.Vertices[vertIndex];
                    vertex.Id      = ids.Id;
                    vertex.SubId   = ids.SubId;
                    vertex.PartsId = ids.PartsId;
                    meshObject.RegisterVertexId(ids.Id);
                }
            }

            // 特殊面の数をカウント
            stats.SkippedSpecialFaces += mqoObj.Faces.Count(f => f.IsSpecialFace);

            // 四角形特殊面からボーンウェイトを抽出（VertexIdHelper使用）
            // 実体側ウェイト（UV[3].y == 0）
            if (!settings.SkipMqoBoneIndices || !settings.SkipMqoBoneWeights)
            {
                //辞書形式で取得（頂点インデックス → ボーンウェイト情報）
                var boneWeightMap = VertexIdHelper.ExtractBoneWeightsFromSpecialFaces(mqoObj.Faces);
                foreach (var kvp in boneWeightMap)
                {
                    int vertIndex = kvp.Key;
                    var bw = kvp.Value;

                    if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                    {
                        //Debug.Log($"[MQOImporter] Applying bone weight from special face: vertexIndex={vertIndex}, boneIndices=({bw.BoneIndex0},{bw.BoneIndex1},{bw.BoneIndex2},{bw.BoneIndex3}), weights=({bw.Weight0},{bw.Weight1},{bw.Weight2},{bw.Weight3})");
                        var vertex = meshObject.Vertices[vertIndex];
                        vertex.BoneWeight = new BoneWeight
                        {
                            boneIndex0 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex0,
                            boneIndex1 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex1,
                            boneIndex2 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex2,
                            boneIndex3 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex3,
                            weight0 = settings.SkipMqoBoneWeights ? 0f : bw.Weight0,
                            weight1 = settings.SkipMqoBoneWeights ? 0f : bw.Weight1,
                            weight2 = settings.SkipMqoBoneWeights ? 0f : bw.Weight2,
                            weight3 = settings.SkipMqoBoneWeights ? 0f : bw.Weight3
                        };
                    }
                }

                // ミラー側ウェイト（UV[3].y == 1）
                var mirrorBoneWeightMap = VertexIdHelper.ExtractMirrorBoneWeightsFromSpecialFaces(mqoObj.Faces);
                foreach (var kvp in mirrorBoneWeightMap)
                {
                    int vertIndex = kvp.Key;
                    var bw = kvp.Value;

                    if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                    {
                        var vertex = meshObject.Vertices[vertIndex];
                        vertex.MirrorBoneWeight = new BoneWeight
                        {
                            boneIndex0 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex0,
                            boneIndex1 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex1,
                            boneIndex2 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex2,
                            boneIndex3 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex3,
                            weight0 = settings.SkipMqoBoneWeights ? 0f : bw.Weight0,
                            weight1 = settings.SkipMqoBoneWeights ? 0f : bw.Weight1,
                            weight2 = settings.SkipMqoBoneWeights ? 0f : bw.Weight2,
                            weight3 = settings.SkipMqoBoneWeights ? 0f : bw.Weight3
                        };
                    }
                }
            }

            // 面変換
            // SmoothFacet モードではスロット確保を後段（NormalSmoothingOps）へ委ねるため、
            // ここでは面コーナーのUVだけを保持しておく（faceCornerUVs は Faces と同じ添字）。
            bool useFacetPath = settings.NormalMode == NormalMode.SmoothFacet;
            List<Vector2[]> faceCornerUVs = useFacetPath ? new List<Vector2[]>() : null;

            foreach (var mqoFace in mqoObj.Faces)
            {
                // 特殊面（メタデータ）はスキップ（既に処理済み）
                if (mqoFace.IsSpecialFace)
                    continue;

                // 1頂点（点）、2頂点（線）は補助線として扱う
                if (mqoFace.VertexCount < 3)
                {
                    ConvertLine(mqoFace, meshObject, settings);
                    if (useFacetPath)
                    {
                        while (faceCornerUVs.Count < meshObject.FaceCount)
                            faceCornerUVs.Add(null);
                    }
                    continue;
                }

                // 3頂点以上は面として変換
                if (useFacetPath)
                {
                    var cornerUVs = ConvertFaceDeferred(mqoFace, meshObject, settings);
                    while (faceCornerUVs.Count < meshObject.FaceCount - 1)
                        faceCornerUVs.Add(null);
                    faceCornerUVs.Add(cornerUVs);
                }
                else
                {
                    ConvertFace(mqoFace, meshObject, settings);
                }
                stats.TotalFaces++;
            }

            if (useFacetPath && faceCornerUVs.Count != meshObject.FaceCount)
            {
                Debug.LogError($"[MQOImporter] faceCornerUVs 不整合 obj=\"{mqoObj.Name}\" " +
                               $"cornerUVs={faceCornerUVs.Count} faces={meshObject.FaceCount}");
                while (faceCornerUVs.Count < meshObject.FaceCount)
                    faceCornerUVs.Add(null);
            }

            // IDが未設定（-1）の頂点はそのまま（外部からIDが与えられていない）

            // OriginalPositions作成
            var originalPositions = new Vector3[meshObject.VertexCount];
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                originalPositions[i] = meshObject.Vertices[i].Position;
            }

            // MeshContext作成
            var meshContext = new MeshContext
            {
                Name = mqoObj.Name,
                MeshObject = meshObject,
                OriginalPositions = originalPositions,
                // オブジェクト属性をコピー
                Depth = mqoObj.Depth,
                IsVisible = mqoObj.IsVisible,
                IsLocked = mqoObj.IsLocked,
                IsFolding = mqoObj.IsFolding,
                // ミラー設定をコピー
                MirrorType = mqoObj.MirrorMode,
                MirrorAxis = mqoObj.MirrorAxis,
                MirrorDistance = mqoObj.MirrorDistance,
                MirrorMaterialOffset = mirrorMaterialOffset
            };

            // MQOオブジェクトのTRS（translation/rotation/scale）をBoneTransformに設定する。
            // ComputeWorldMatrices() が LocalMatrix → WorldMatrix を計算するために必要。
            // デフォルト値（位置ゼロ・回転ゼロ・スケール1）の場合は設定しない
            // （UseLocalTransform=false のまま → LocalMatrix=identity → WorldMatrix=identity）。
            // エディタ側 PolyLing_MeshLoad の MeshFilter 処理と同じ判定ロジック。
            {
                Vector3 translationScaled = AxisFlipOps.Position(settings.Flip, mqoObj.Translation, settings.Scale);

                // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
                Vector3 rotationConverted = MQOLocalRotationOps.ToUnityEuler(mqoObj.Rotation, settings.Flip);

                bool isDefaultTransform =
                    translationScaled == Vector3.zero &&
                    mqoObj.Rotation == Vector3.zero &&
                    mqoObj.Scale == Vector3.one;

                if (!isDefaultTransform)
                {
                    var meshBoneTransform = new BoneTransform
                    {
                        Position = translationScaled,
                        Rotation = rotationConverted,
                        Scale    = mqoObj.Scale,
                        UseLocalTransform = true,
                    };
                    meshContext.BoneTransform = meshBoneTransform;
                }
            }

            // マテリアル設定
            // Phase 5: マテリアルはMQOImportResultにグローバルリストとして保存される
            // MeshContext.Materialsへの設定は不要（ModelContext.Materialsで管理）
            // 代わりに、ToUnityMeshにマテリアル数を渡してサブメッシュを正しく生成

            // 使用されているマテリアルの最大インデックスを取得
            int maxMaterialIndex = 0;
            foreach (var face in meshObject.Faces)
            {
                if (face.MaterialIndex > maxMaterialIndex)
                    maxMaterialIndex = face.MaterialIndex;
            }
            int materialCount = settings.ImportMaterials && unityMaterials.Count > 0
                ? unityMaterials.Count
                : maxMaterialIndex + 1;

            // メッシュ名を設定
            meshObject.Name = mqoObj.Name;

            // 法線スムージング
            if (settings.NormalMode == NormalMode.SmoothFacet)
            {
                // オブジェクト単位の shading / facet を使う（UseMqoFacet=false なら設定値で上書き）
                bool  flatShading = settings.UseMqoFacet && mqoObj.Shading == 0;
                float facetAngle  = settings.UseMqoFacet ? mqoObj.Facet : settings.SmoothingAngle;

                NormalSmoothingOps.ApplyFacetSmoothing(
                    meshObject, faceCornerUVs, facetAngle, flatShading, mqoObj.Name);
            }
            else if (settings.NormalMode == NormalMode.Smooth)
            {
                CalculateSmoothNormals(meshObject, settings.SmoothingAngle);
            }
            else if (settings.NormalMode == NormalMode.Unity)
            {
                // Unity標準のRecalculateNormalsを使用（ToUnityMeshShared後に呼ばれる）
            }
            // NormalMode.FaceNormalの場合はCalculateFaceNormalで設定済みの面法線をそのまま使用

            // Unity Mesh生成（マテリアル数を渡す）
            meshContext.UnityMesh = meshObject.ToUnityMeshShared(materialCount);

            // NormalMode.Unityの場合はUnity標準のRecalculateNormalsを使用
            if (settings.NormalMode == NormalMode.Unity && meshContext.UnityMesh != null)
            {
                meshContext.UnityMesh.RecalculateNormals();
                Debug.Log($"[MQOImporter] Unity RecalculateNormals applied");
            }

            // 頂点デバッグ出力
            if (settings.DebugVertexInfo)
            {
                OutputVertexDebugInfo(mqoObj.Name, mqoObj, meshObject, settings.DebugVertexNearUVCount);
            }

            /*
            // デバッグ出力（ミラー属性確認用）
            Debug.Log($"[MQOImporter] ConvertObject: {mqoObj.Name}");
            Debug.Log($"  - MeshObject: V={meshObject.VertexCount}, F={meshObject.FaceCount}");
            Debug.Log($"  - MQO Mirror: Mode={mqoObj.MirrorMode}, Axis={mqoObj.MirrorAxis}, Dist={mqoObj.MirrorDistance}");
            Debug.Log($"  - MeshUndoContext: IsMirrored={meshContext.IsMirrored}, MirrorType={meshContext.MirrorType}, MirrorAxis={meshContext.MirrorAxis}");
            Debug.Log($"  - UnityMesh: V={meshContext.UnityMesh?.vertexCount ?? 0}, T={meshContext.UnityMesh?.triangles?.Length ?? 0}");
            */
            return meshContext;
        }

        // ================================================================
        // 面変換
        // ================================================================

        private static void ConvertFace(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            int vertexCount = mqoFace.VertexCount;

            // 頂点インデックス
            var vertexIndices = new List<int>(mqoFace.VertexIndices);

            // UVサブインデックスを計算
            var uvSubIndices = new List<int>();
            for (int i = 0; i < vertexCount; i++)
            {
                int vertIndex = mqoFace.VertexIndices[i];
                Vector2 uv = (mqoFace.UVs != null && i < mqoFace.UVs.Length)
                    ? ConvertUV(mqoFace.UVs[i], settings)
                    : Vector2.zero;

                // 頂点にUVを追加し、サブインデックスを取得
                var vertex = meshObject.Vertices[vertIndex];
                int uvSubIndex = AddOrGetUVIndex(vertex, uv);
                uvSubIndices.Add(uvSubIndex);
            }

            // Face作成
            var face = new Face
            {
                MaterialIndex = mqoFace.MaterialIndex >= 0 ? mqoFace.MaterialIndex : 0
            };

            // 頂点とUVサブインデックスを追加（元の順序のまま）
            for (int i = 0; i < vertexCount; i++)
            {
                face.VertexIndices.Add(vertexIndices[i]);
                face.UVIndices.Add(uvSubIndices[i]);
                // 法線サブindexはUVサブindexと同値に保つ（不変条件）。
                // 0固定にすると、AddOrGetUVIndex が確保したスロット1以降へ法線が書かれない。
                face.NormalIndices.Add(uvSubIndices[i]);
            }

            meshObject.Faces.Add(face);

            // 法線計算
            CalculateFaceNormal(face, meshObject);
        }

        /// <summary>
        /// SmoothFacet モード用の面変換。
        /// UV/法線スロットはまだ確保せず（AddOrGetUVIndex を呼ばない）、
        /// 面コーナーのUVだけを返す。スロット確保は NormalSmoothingOps が行う。
        /// </summary>
        private static Vector2[] ConvertFaceDeferred(
            MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            int vertexCount = mqoFace.VertexCount;

            var cornerUVs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                cornerUVs[i] = (mqoFace.UVs != null && i < mqoFace.UVs.Length)
                    ? ConvertUV(mqoFace.UVs[i], settings)
                    : Vector2.zero;
            }

            var face = new Face
            {
                MaterialIndex = mqoFace.MaterialIndex >= 0 ? mqoFace.MaterialIndex : 0
            };

            for (int i = 0; i < vertexCount; i++)
            {
                face.VertexIndices.Add(mqoFace.VertexIndices[i]);
                // スロット番号は後段で確定させる。ここでは仮に 0 を入れておく。
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);

            return cornerUVs;
        }

        private static void ConvertLine(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            if (mqoFace.VertexCount < 2) return;

            // 2頂点の補助線として追加
            var face = new Face
            {
                MaterialIndex = 0
            };

            for (int i = 0; i < mqoFace.VertexCount; i++)
            {
                face.VertexIndices.Add(mqoFace.VertexIndices[i]);
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);
        }
    }
}
