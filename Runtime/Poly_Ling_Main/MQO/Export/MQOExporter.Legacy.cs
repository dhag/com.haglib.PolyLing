// MQOExporter.Legacy.cs
// MQO エクスポート：旧ドキュメント変換・既定シーン・マテリアル・オブジェクト変換。
// Runtime/Poly_Ling_Main/MQO/Export/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;

namespace Poly_Ling.MQO
{
    public static partial class MQOExporter
    {
        /// <summary>
        /// MQOドキュメント変換（後方互換: MeshContext.Materialsを使用）
        /// </summary>
        private static MQODocument ConvertToDocumentLegacy(
            IList<MeshContext> meshContexts,
            MQOExportSettings settings,
            MQOExportStats stats)
        {
            EnsureWorldMatrices(meshContexts);

            var document = new MQODocument
            {
                Version = 1.1m,
            };

            // デフォルトシーン情報
            document.Scene = CreateDefaultScene();

            // マテリアル収集（MeshContext.Materialsから）
            var materialMap = new Dictionary<Material, int>();
            if (settings.ExportMaterials)
            {
                foreach (var mc in meshContexts)
                {
                    if (mc?.Materials == null) continue;
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !materialMap.ContainsKey(mat))
                        {
                            materialMap[mat] = document.Materials.Count;
                            document.Materials.Add(ConvertMaterial(mat, settings.TextureFolder));
                        }
                    }
                }
            }

            // デフォルトマテリアルがない場合は追加
            if (document.Materials.Count == 0)
            {
                document.Materials.Add(new MQOMaterial
                {
                    Name = "Default",
                    Color = Color.white,
                    Diffuse = 0.8f,
                    Ambient = 0.6f,
                });
            }

            // オブジェクト変換（Legacy版もマテリアル数を渡す）
            int materialCount = document.Materials.Count;
            
            if (settings.MergeObjects)
            {
                // 全メッシュを統合
                var merged = MergeMeshContexts(meshContexts, "MergedObject");
                var mqoObj = ConvertObject(merged, materialCount, settings, stats);
                if (mqoObj != null)
                {
                    document.Objects.Add(mqoObj);
                }
            }
            else
            {
                // ボーンとメッシュを分離
                var boneContexts = new List<MeshContext>();
                var meshOnlyContexts = new List<MeshContext>();
                
                foreach (var mc in meshContexts)
                {
                    if (mc == null) continue;
                    
                    if (mc.Type == MeshType.Bone)
                    {
                        boneContexts.Add(mc);
                    }
                    else
                    {
                        meshOnlyContexts.Add(mc);
                    }
                }
                
                // ボーン出力（ExportBones=trueの場合）
                if (settings.ExportBones && boneContexts.Count > 0)
                {
                    // __Armature__オブジェクトを作成（ツリー構造用）
                    var armatureObj = new MQOObject
                    {
                        Name = "__Armature__"
                    };
                    armatureObj.Attributes.Add(new MQOAttribute("depth", 0));
                    armatureObj.Attributes.Add(new MQOAttribute("visible", 15));
                    armatureObj.Attributes.Add(new MQOAttribute("locking", 0));
                    armatureObj.Attributes.Add(new MQOAttribute("shading", 1));
                    armatureObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                    armatureObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
                    armatureObj.Attributes.Add(new MQOAttribute("color_type", 0));
                    document.Objects.Add(armatureObj);
                    
                    // ボーンをツリー順（深さ優先）でソート
                    var sortedBones = SortBonesDepthFirst(boneContexts, meshContexts);
                    var boneDepths = CalculateBoneDepths(boneContexts, meshContexts);
                    
                    // ボーンを出力（ツリー順）
                    foreach (var bc in sortedBones)
                    {
                        var mqoObj = ConvertBoneObject(bc, boneDepths, settings, stats);
                        if (mqoObj != null)
                        {
                            document.Objects.Add(mqoObj);
                        }
                    }
                    
                    // __ArmatureName__オブジェクトを作成（リスト順インデックス用）
                    var armatureNameObj = new MQOObject
                    {
                        Name = "__ArmatureName__"
                    };
                    armatureNameObj.Attributes.Add(new MQOAttribute("depth", 0));
                    armatureNameObj.Attributes.Add(new MQOAttribute("visible", 0));  // 非表示
                    armatureNameObj.Attributes.Add(new MQOAttribute("locking", 1));  // ロック
                    armatureNameObj.Attributes.Add(new MQOAttribute("shading", 1));
                    armatureNameObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                    armatureNameObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                    armatureNameObj.Attributes.Add(new MQOAttribute("color_type", 0));
                    document.Objects.Add(armatureNameObj);
                    
                    // ボーン名をリスト順（元のインデックス順）で出力
                    foreach (var bc in boneContexts)
                    {
                        var nameObj = new MQOObject
                        {
                            Name = "__ArmatureName__" + (bc.Name ?? "Bone")
                        };
                        nameObj.Attributes.Add(new MQOAttribute("depth", 1));
                        nameObj.Attributes.Add(new MQOAttribute("visible", 0));
                        nameObj.Attributes.Add(new MQOAttribute("locking", 1));
                        nameObj.Attributes.Add(new MQOAttribute("shading", 1));
                        nameObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                        nameObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                        nameObj.Attributes.Add(new MQOAttribute("color_type", 0));
                        document.Objects.Add(nameObj);
                    }
                }
                    
                    // __IK__セクション: IKボーン情報を出力
                    EmitIKObjects(document, boneContexts, meshContexts);
                
                // メッシュを出力（2パス: 実体を先に出力し、ミラーは後でウェイト保存）
                var skippedMirrors = new List<MeshContext>();
                var mcToMqoObj = new Dictionary<MeshContext, MQOObject>();

                foreach (var mc in meshOnlyContexts)
                {
                    // ミラースキップ判定（B: Type=BakedMirror, C: 名前末尾+）
                    if (ShouldSkipAsMirror(mc, settings))
                    {
                        skippedMirrors.Add(mc);
                        continue;
                    }

                    // 空オブジェクトスキップ（SkipEmptyObjectsがtrueかつMeshObjectがnullまたは空の場合）
                    if (settings.SkipEmptyObjects)
                    {
                        if (mc.MeshObject == null || 
                            (mc.MeshObject.VertexCount == 0 && mc.MeshObject.FaceCount == 0))
                        {
                            continue;
                        }
                    }

                    var mqoObj = ConvertObject(mc, materialCount, settings, stats);
                    if (mqoObj != null)
                    {
                        document.Objects.Add(mqoObj);
                        mcToMqoObj[mc] = mqoObj;
                    }
                }

                // スキップしたミラーメッシュのウェイトを実体側に保存
                foreach (var mirrorMc in skippedMirrors)
                {
                    var sourceMc = FindMirrorSource(mirrorMc, meshContexts);
                    if (sourceMc != null && mcToMqoObj.TryGetValue(sourceMc, out var sourceMqoObj))
                    {
                        SaveMirrorWeightsToSource(mirrorMc, sourceMqoObj, settings);
                    }
                    else
                    {
                        Debug.LogWarning($"[MQOExporter] Mirror source not found for '{mirrorMc.Name}', weights will be lost");
                    }
                }
            }

            return document;
        }

        private static MQOScene CreateDefaultScene()
        {
            var scene = new MQOScene();
            scene.Attributes.Add(new MQOAttribute("pos", 0, 0, 1500));
            scene.Attributes.Add(new MQOAttribute("lookat", 0, 0, 0));
            scene.Attributes.Add(new MQOAttribute("head", -0.5236f));
            scene.Attributes.Add(new MQOAttribute("pich", 0.5236f));
            scene.Attributes.Add(new MQOAttribute("ortho", 0));
            scene.Attributes.Add(new MQOAttribute("zoom2", 5));
            scene.Attributes.Add(new MQOAttribute("amb", 0.25f, 0.25f, 0.25f));
            return scene;
        }

        private static MQOMaterial ConvertMaterial(Material mat, string textureFolder, Poly_Ling.Materials.MaterialReference matRef = null)
        {
            var mqoMat = new MQOMaterial
            {
                Name = mat.name,
                Color = mat.HasProperty("_Color") ? mat.color : Color.white,
                Diffuse = 0.8f,
                Ambient = 0.6f,
                Specular = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor").grayscale : 0f,
                Power = mat.HasProperty("_Shininess") ? mat.GetFloat("_Shininess") : 5f,
            };

            // ソーステクスチャパスを優先的に使用（インポート時のパスを保持）
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceTexturePath))
            {
                mqoMat.TexturePath = matRef.Data.SourceTexturePath;
            }
            // フォールバック: 現在のテクスチャから生成
            else if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
            {
                string texName = mat.mainTexture.name;
                // 拡張子がなければ.pngを追加
                if (!texName.Contains("."))
                {
                    texName += ".png";
                }
                // フォルダパスを付加
                if (!string.IsNullOrEmpty(textureFolder))
                {
                    // フォルダパスの末尾にスラッシュがなければ追加
                    string folder = textureFolder;
                    if (!folder.EndsWith("/") && !folder.EndsWith("\\"))
                    {
                        folder += "/";
                    }
                    texName = folder + texName;
                }
                mqoMat.TexturePath = texName;
            }

            // バンプマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceBumpMapPath))
            {
                mqoMat.BumpMapPath = matRef.Data.SourceBumpMapPath;
            }

            // アルファマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceAlphaMapPath))
            {
                mqoMat.AlphaMapPath = matRef.Data.SourceAlphaMapPath;
            }

            return mqoMat;
        }

        /// <summary>
        /// MeshContextをMQOObjectに変換
        /// Phase 5: materialMapの代わりにmaterialCountを使用
        /// </summary>
        private static MQOObject ConvertObject(
            MeshContext meshContext,
            int materialCount,
            MQOExportSettings settings,
            MQOExportStats stats,
            Dictionary<int, int> materialIndexMap = null)
        {
            var meshObject = meshContext.MeshObject;
            
            // 空オブジェクトでも属性は出力する（階層構造保持のため）
            var mqoObj = new MQOObject
            {
                Name = meshContext.Name ?? "Object",
            };

            // 属性設定（オブジェクト属性を保持する場合）
            if (settings.PreserveObjectAttributes)
            {
                // depth（階層深度）
                if (meshContext.Depth > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("depth", meshContext.Depth));
                }
                
                // folding（折りたたみ状態）
                if (meshContext.IsFolding)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("folding", 1));
                }
                
                // visible（表示状態）
                // MQOのvisible: 15=表示, 0=非表示
                int visibleValue = meshContext.IsVisible ? 15 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("visible", visibleValue));
                
                // locking（ロック状態）
                int lockingValue = meshContext.IsLocked ? 1 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("locking", lockingValue));
                
                // mirror（ミラー設定）
                if (meshContext.MirrorType > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("mirror", meshContext.MirrorType));
                    mqoObj.Attributes.Add(new MQOAttribute("mirror_axis", meshContext.MirrorAxis));
                    if (meshContext.MirrorDistance != 0f)
                    {
                        mqoObj.Attributes.Add(new MQOAttribute("mirror_dis", meshContext.MirrorDistance));
                    }
                }
            }
            else
            {
                // デフォルト属性
                mqoObj.Attributes.Add(new MQOAttribute("visible", 15));
                mqoObj.Attributes.Add(new MQOAttribute("locking", 0));
            }
            
            // 共通属性
            mqoObj.Attributes.Add(new MQOAttribute("shading", 1));
            mqoObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            mqoObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
            mqoObj.Attributes.Add(new MQOAttribute("color_type", 0));

            // ローカルトランスフォーム出力
            if (settings.ExportLocalTransform && meshContext.BoneTransform != null)
            {
                var bt = meshContext.BoneTransform;
                if (bt.UseLocalTransform)
                {
                    // 位置（スケールと軸反転を適用）
                    Vector3 pos = AxisFlipOps.Position(settings.Flip, bt.Position, settings.Scale);
                    mqoObj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));
                    
                    // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
                    Vector3 rot = MQOLocalRotationOps.ToMqoRotation(bt.Rotation, settings.Flip);
                    mqoObj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));
                    
                    // スケール
                    Vector3 scale = bt.Scale;
                    mqoObj.Attributes.Add(new MQOAttribute("scale", scale.x, scale.y, scale.z));
                }
            }

            // MeshObjectがある場合のみ頂点・面を出力
            if (meshObject != null)
            {
                // 頂点変換
                // メタセコイアはローカル座標（translation/rotation/scale）をピボットとして扱い、
                // 形状は動かさない。頂点は常に絶対座標として解釈されるので、
                // PolyLing の階層で累積したワールド行列を頂点へ畳み込んでから出す。
                // ローカル変換が単位なら WorldMatrix も単位で、従来と同じ出力になる。
                Matrix4x4 toWorld = settings.ExportVerticesInWorldSpace
                    ? meshContext.WorldMatrix
                    : Matrix4x4.identity;
                bool bakeWorld = !toWorld.isIdentity;

                foreach (var v in meshObject.Vertices)
                {
                    Vector3 pos = bakeWorld ? toWorld.MultiplyPoint3x4(v.Position) : v.Position;
                    var mqoVert = new MQOVertex
                    {
                        Position = ConvertPosition(pos, settings),
                        Index = mqoObj.Vertices.Count,
                    };
                    mqoObj.Vertices.Add(mqoVert);
                    stats.TotalVertices++;
                }

                // 面変換
                foreach (var face in meshObject.Faces)
                {
                    if (face.VertexIndices == null || face.VertexIndices.Count == 0)
                        continue;

                    // マテリアルインデックスを変換（マッピングがあれば使用）
                    int exportMatIdx = 0;
                    if (face.MaterialIndex >= 0)
                    {
                        if (materialIndexMap != null && materialIndexMap.TryGetValue(face.MaterialIndex, out int mappedIdx))
                        {
                            exportMatIdx = mappedIdx;
                        }
                        else if (face.MaterialIndex < materialCount)
                        {
                            exportMatIdx = face.MaterialIndex;
                        }
                    }

                    var mqoFace = new MQOFace
                    {
                        VertexIndices = face.VertexIndices.ToArray(),
                        MaterialIndex = exportMatIdx,
                    };

                    // UV変換
                    if (face.UVIndices != null && face.UVIndices.Count > 0)
                    {
                        var uvs = new Vector2[face.UVIndices.Count];
                        for (int i = 0; i < face.UVIndices.Count; i++)
                        {
                            int vertIdx = face.VertexIndices[i];
                            int uvIdx = face.UVIndices[i];
                            if (vertIdx >= 0 && vertIdx < meshObject.Vertices.Count)
                            {
                                var vertex = meshObject.Vertices[vertIdx];
                                // UVインデックスが有効範囲内か確認
                                Vector2 uv = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                                    ? vertex.UVs[uvIdx]
                                    : (vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero);
                                uvs[i] = ConvertUV(uv, settings);
                            }
                        }
                        mqoFace.UVs = uvs;
                    }

                    mqoObj.Faces.Add(mqoFace);
                    stats.TotalFaces++;
                }

                // 頂点識別子用の特殊面を追加。COL(PartsID, SubID, ID) で書く。
                // 出力対象は「ID が -1 でない」か「SubID / PartsID のいずれかが設定済み」。
                // 前半の条件は従来どおりなので、既存の出力対象は 1 件も減らない。
                for (int i = 0; i < meshObject.Vertices.Count; i++)
                {
                    var vertex = meshObject.Vertices[i];
                    if (vertex.Id != -1 || vertex.SubId != 0 || vertex.PartsId != 0)
                    {
                        mqoObj.Faces.Add(VertexIdHelper.CreateSpecialFaceForVertexId(
                            i, vertex.Id, vertex.SubId, vertex.PartsId, 0));
                    }
                }

                // ボーンウェイト用の四角形特殊面を追加（BoneWeightを持つ頂点のみ）
                // VertexIdHelper.CreateSpecialFaceForBoneWeightを使用
                if (settings.EmbedBoneWeightsInMQO)
                {
                    for (int i = 0; i < meshObject.Vertices.Count; i++)
                    {
                        var vertex = meshObject.Vertices[i];
                        // 実体側ボーンウェイト
                        if (vertex.HasBoneWeight)
                        {
                            var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertex.BoneWeight.Value);
                            mqoObj.Faces.Add(VertexIdHelper.CreateSpecialFaceForBoneWeight(i, boneWeightData, false, 0));
                        }
                        // タイプA: ミラー側ボーンウェイト（実体メッシュ内にMirrorBoneWeightとして保持されている場合）
                        if (vertex.HasMirrorBoneWeight)
                        {
                            var mirrorBoneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertex.MirrorBoneWeight.Value);
                            mqoObj.Faces.Add(VertexIdHelper.CreateSpecialFaceForBoneWeight(i, mirrorBoneWeightData, true, 0));
                        }
                    }
                }
            }

            // ミラーウェイトを保存したオブジェクトにミラーフラグを立てる（タイプA）
            if (settings.EmbedBoneWeightsInMQO && meshObject != null)
            {
                bool hasMirrorWeight = false;
                for (int i = 0; i < meshObject.VertexCount; i++)
                {
                    if (meshObject.Vertices[i].HasMirrorBoneWeight)
                    {
                        hasMirrorWeight = true;
                        break;
                    }
                }
                if (hasMirrorWeight && meshContext.MirrorType == 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("mirror", 1));
                    mqoObj.Attributes.Add(new MQOAttribute("mirror_axis", 1));
                }
            }

            return mqoObj;
        }
    }
}
