// MQOExporter.Text.cs
// MQO エクスポート：ミラースキップ・マテリアル索引・座標変換・メッシュ統合・テキスト生成。
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
        // ================================================================
        // ミラースキップ・ウェイト保存
        // ================================================================

        /// <summary>
        /// メッシュがミラースキップ対象かを判定
        /// B: SkipBakedMirror かつ Type=BakedMirror
        /// C: SkipNamedMirror かつ 名前末尾が+
        /// </summary>
        /// <summary>
        /// __IK__セクションをMQODocumentに出力
        /// IKボーンごとに __IK__ボーン名 → __IKTarget__ターゲット名 → __IKLink__リンク名... の構造
        /// </summary>
        private static void EmitIKObjects(
            MQODocument document,
            List<MeshContext> boneContexts,
            IList<MeshContext> allContexts)
        {
            var ikBones = new List<MeshContext>();
            foreach (var bc in boneContexts)
            {
                if (bc.IsIK && bc.IKLinks != null && bc.IKLinks.Count > 0)
                    ikBones.Add(bc);
            }
            if (ikBones.Count == 0) return;

            // __IK__ ルートオブジェクト
            var ikRootObj = new MQOObject { Name = "__IK__" };
            ikRootObj.Attributes.Add(new MQOAttribute("depth", 0));
            ikRootObj.Attributes.Add(new MQOAttribute("visible", 0));
            ikRootObj.Attributes.Add(new MQOAttribute("locking", 1));
            ikRootObj.Attributes.Add(new MQOAttribute("shading", 1));
            ikRootObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            ikRootObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
            ikRootObj.Attributes.Add(new MQOAttribute("color_type", 0));
            document.Objects.Add(ikRootObj);

            foreach (var ikBone in ikBones)
            {
                // __IK__ボーン名
                var ikObj = new MQOObject { Name = "__IK__" + (ikBone.Name ?? "IK") };
                ikObj.Attributes.Add(new MQOAttribute("depth", 1));
                ikObj.Attributes.Add(new MQOAttribute("visible", 0));
                ikObj.Attributes.Add(new MQOAttribute("locking", 1));
                ikObj.Attributes.Add(new MQOAttribute("shading", 1));
                ikObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                ikObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                ikObj.Attributes.Add(new MQOAttribute("color_type", 0));
                document.Objects.Add(ikObj);

                // __IKTarget__ターゲットボーン名
                string targetName = "Unknown";
                if (ikBone.IKTargetIndex >= 0 && ikBone.IKTargetIndex < allContexts.Count)
                {
                    targetName = allContexts[ikBone.IKTargetIndex]?.Name ?? "Unknown";
                }
                var targetObj = new MQOObject { Name = "__IKTarget__" + targetName };
                targetObj.Attributes.Add(new MQOAttribute("depth", 2));
                targetObj.Attributes.Add(new MQOAttribute("visible", 0));
                targetObj.Attributes.Add(new MQOAttribute("locking", 1));
                targetObj.Attributes.Add(new MQOAttribute("shading", 1));
                targetObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                targetObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                targetObj.Attributes.Add(new MQOAttribute("color_type", 0));
                document.Objects.Add(targetObj);

                // __IKLink__リンクボーン名
                foreach (var link in ikBone.IKLinks)
                {
                    string linkName = "Unknown";
                    if (link.BoneIndex >= 0 && link.BoneIndex < allContexts.Count)
                    {
                        linkName = allContexts[link.BoneIndex]?.Name ?? "Unknown";
                    }
                    var linkObj = new MQOObject { Name = "__IKLink__" + linkName };
                    linkObj.Attributes.Add(new MQOAttribute("depth", 2));
                    linkObj.Attributes.Add(new MQOAttribute("visible", 0));
                    linkObj.Attributes.Add(new MQOAttribute("locking", 1));
                    linkObj.Attributes.Add(new MQOAttribute("shading", 1));
                    linkObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                    linkObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                    linkObj.Attributes.Add(new MQOAttribute("color_type", 0));
                    document.Objects.Add(linkObj);
                }
            }
        }

        private static bool ShouldSkipAsMirror(MeshContext mc, MQOExportSettings settings)
        {
            // B: ミラー側の型（BakedMirror / MirrorSide）。
            //    名前ではなく型で判定する。ミラー名は「左腕+」とは限らず
            //    「右腕」になり得るため、接尾辞に頼ると MirrorSide を取りこぼす。
            if (settings.SkipBakedMirror && Poly_Ling.Ops.MirrorBranchOps.IsMirrorSideContext(mc))
                return true;

            // C: 名前末尾が+（型情報を持たない古いデータ向けの保険）
            if (settings.SkipNamedMirror && !Poly_Ling.Ops.MirrorBranchOps.IsMirrorSideContext(mc) &&
                !string.IsNullOrEmpty(mc.Name) && mc.Name.EndsWith("+"))
                return true;

            return false;
        }

        /// <summary>
        /// ミラーメッシュの実体側MeshContextを検索
        /// B: BakedMirrorSourceIndex
        /// C: 名前から+を除いた名前で検索
        /// </summary>
        private static MeshContext FindMirrorSource(MeshContext mirrorMc, IList<MeshContext> allMeshContexts)
        {
            // B: BakedMirrorSourceIndex が有効
            if (mirrorMc.BakedMirrorSourceIndex >= 0 &&
                mirrorMc.BakedMirrorSourceIndex < allMeshContexts.Count)
            {
                return allMeshContexts[mirrorMc.BakedMirrorSourceIndex];
            }

            // C: 名前末尾+を除いて検索
            if (!string.IsNullOrEmpty(mirrorMc.Name) && mirrorMc.Name.EndsWith("+"))
            {
                string sourceName = mirrorMc.Name.Substring(0, mirrorMc.Name.Length - 1);
                foreach (var mc in allMeshContexts)
                {
                    if (mc != null && mc != mirrorMc && mc.Name == sourceName)
                        return mc;
                }
            }

            return null;
        }

        /// <summary>
        /// ミラーメッシュのボーンウェイトと頂点IDを実体側MQOObjectに特殊面として保存
        /// </summary>
        /// <param name="mirrorMc">スキップされるミラーメッシュ</param>
        /// <param name="sourceMqoObj">実体側のMQOObject（特殊面を追加する先）</param>
        /// <param name="settings">エクスポート設定</param>
        private static void SaveMirrorWeightsToSource(
            MeshContext mirrorMc,
            MQOObject sourceMqoObj,
            MQOExportSettings settings)
        {
            if (mirrorMc?.MeshObject == null || sourceMqoObj == null) return;
            if (!settings.EmbedBoneWeightsInMQO) return;

            var meshObject = mirrorMc.MeshObject;

            for (int i = 0; i < meshObject.Vertices.Count; i++)
            {
                var vertex = meshObject.Vertices[i];

                // ボーンウェイト特殊面（isMirror=true）
                if (vertex.HasBoneWeight)
                {
                    var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertex.BoneWeight.Value);
                    sourceMqoObj.Faces.Add(
                        VertexIdHelper.CreateSpecialFaceForBoneWeight(i, boneWeightData, true, 0));
                }

                // 頂点ID特殊面（ミラー側にIDがある場合）
                // ミラー側の頂点IDはisMirror=true特殊面では保存できない（三角形特殊面にはミラーフラグがない）
                // → 頂点IDは実体側と共有されるため、個別保存は不要
            }

            Debug.Log($"[MQOExporter] Saved mirror weights from '{mirrorMc.Name}' to source object ({meshObject.VertexCount} vertices)");

            // ソースオブジェクトにミラーフラグを立てる（mirror属性がまだない場合）
            if (sourceMqoObj.Attributes.Find(a => a.Name == "mirror") == null)
            {
                sourceMqoObj.Attributes.Add(new MQOAttribute("mirror", 1));
                sourceMqoObj.Attributes.Add(new MQOAttribute("mirror_axis", 1));
            }
        }

        // ================================================================
        // マテリアルインデックス取得
        // ================================================================

        /// <summary>
        /// マテリアルインデックスを取得
        /// Phase 5: Face.MaterialIndexはグローバルマテリアルインデックス
        /// </summary>
        private static int GetMaterialIndex(
            MeshContext meshContext,
            int materialIndex,
            Dictionary<Material, int> materialMap)
        {
            // Phase 5以降: Face.MaterialIndexはグローバルインデックス
            // materialMapのサイズ（エクスポートされたマテリアル数）を超えなければそのまま使用
            if (materialIndex >= 0 && materialIndex < materialMap.Count)
            {
                return materialIndex;
            }

            // 範囲外の場合はデフォルト（0）
            return 0;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pos, MQOExportSettings settings)
        {
            // スケール
            pos *= settings.Scale;

            // Y-Z入れ替え（Unity Y-up → MQO Z-up）
            if (settings.SwapYZ)
            {
                pos = new Vector3(pos.x, pos.z, pos.y);
            }

            // 軸反転
            pos = AxisFlipOps.Position(settings.Flip, pos);

            return pos;
        }

        private static Vector2 ConvertUV(Vector2 uv, MQOExportSettings settings)
        {
            if (settings.FlipUV_V)
            {
                uv.y = 1f - uv.y;
            }
            return uv;
        }

        // ================================================================
        // メッシュ統合
        // ================================================================

        private static MeshContext MergeMeshContexts(
            IList<MeshContext> meshContexts,
            string name)
        {
            var mergedData = new MeshObject(name);
            var mergedMaterials = new List<Material>();

            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;

                int vertexOffset = mergedData.VertexCount;

                // 頂点コピー
                foreach (var v in mc.MeshObject.Vertices)
                {
                    Vector2 uv = v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero;
                    Vector3 normal = v.Normals.Count > 0 ? v.Normals[0] : Vector3.zero;
                    mergedData.AddVertex(v.Position, uv, normal);
                }

                // 面コピー（インデックスオフセット）
                foreach (var face in mc.MeshObject.Faces)
                {
                    var newFace = new Face
                    {
                        MaterialIndex = face.MaterialIndex
                    };
                    foreach (int idx in face.VertexIndices)
                    {
                        newFace.VertexIndices.Add(idx + vertexOffset);
                    }
                    // UVインデックスもコピー（オフセットなし、頂点内インデックスのため）
                    newFace.UVIndices.AddRange(face.UVIndices);
                    newFace.NormalIndices.AddRange(face.NormalIndices);
                    mergedData.AddFace(newFace);
                }

                // マテリアルコピー
                if (mc.Materials != null)
                {
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !mergedMaterials.Contains(mat))
                        {
                            mergedMaterials.Add(mat);
                        }
                    }
                }
            }

            return new MeshContext
            {
                Name = name,
                MeshObject = mergedData,
                Materials = mergedMaterials,
            };
        }

        // ================================================================
        // テキスト生成
        // ================================================================

        internal static string GenerateMQOText(MQODocument document, MQOExportSettings settings)
        {
            var sb = new StringBuilder();
            string fmt = $"F{settings.DecimalPrecision}";

            // ヘッダー
            sb.AppendLine("Metasequoia Document");
            sb.AppendLine("Format Text Ver 1.1");
            sb.AppendLine();

            // Scene
            sb.AppendLine("Scene {");
            foreach (var attr in document.Scene.Attributes)
            {
                sb.Append($"\t{attr.Name}");
                foreach (var v in attr.Values)
                {
                    sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("}");

            // Material
            if (document.Materials.Count > 0)
            {
                sb.AppendLine($"Material {document.Materials.Count} {{");
                foreach (var mat in document.Materials)
                {
                    sb.Append($"\t\"{mat.Name}\"");
                    sb.Append($" col({mat.Color.r.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.g.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.b.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.a.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" dif({mat.Diffuse.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" amb({mat.Ambient.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" emi({mat.Emissive.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" spc({mat.Specular.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" power({mat.Power.ToString("F2", CultureInfo.InvariantCulture)})");
                    if (!string.IsNullOrEmpty(mat.TexturePath))
                    {
                        sb.Append($" tex(\"{mat.TexturePath}\")");
                    }
                    if (!string.IsNullOrEmpty(mat.AlphaMapPath))
                    {
                        sb.Append($" aplane(\"{mat.AlphaMapPath}\")");
                    }
                    if (!string.IsNullOrEmpty(mat.BumpMapPath))
                    {
                        sb.Append($" bump(\"{mat.BumpMapPath}\")");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("}");
            }

            // Objects
            foreach (var obj in document.Objects)
            {
                sb.AppendLine($"Object \"{obj.Name}\" {{");

                // 属性
                foreach (var attr in obj.Attributes)
                {
                    sb.Append($"\t{attr.Name}");
                    foreach (var v in attr.Values)
                    {
                        if (v == (int)v)
                            sb.Append($" {(int)v}");
                        else
                            sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }

                // 頂点
                sb.AppendLine($"\tvertex {obj.Vertices.Count} {{");
                foreach (var v in obj.Vertices)
                {
                    sb.Append("\t\t");
                    sb.Append(v.Position.x.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.Append(v.Position.y.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.AppendLine(v.Position.z.ToString(fmt, CultureInfo.InvariantCulture));
                }
                sb.AppendLine("\t}");

                // 面
                sb.AppendLine($"\tface {obj.Faces.Count} {{");
                foreach (var face in obj.Faces)
                {
                    sb.Append($"\t\t{face.VertexCount} V(");
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        if (i > 0) sb.Append(" ");
                        sb.Append(face.VertexIndices[i]);
                    }
                    sb.Append(")");

                    if (face.MaterialIndex >= 0)
                    {
                        sb.Append($" M({face.MaterialIndex})");
                    }

                    if (face.UVs != null && face.UVs.Length > 0)
                    {
                        sb.Append(" UV(");
                        for (int i = 0; i < face.UVs.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.UVs[i].x.ToString(fmt, CultureInfo.InvariantCulture));
                            sb.Append(" ");
                            sb.Append(face.UVs[i].y.ToString(fmt, CultureInfo.InvariantCulture));
                        }
                        sb.Append(")");
                    }

                    // COL属性（頂点カラー/頂点ID用）
                    if (face.VertexColors != null && face.VertexColors.Length > 0)
                    {
                        sb.Append(" COL(");
                        for (int i = 0; i < face.VertexColors.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.VertexColors[i]);
                        }
                        sb.Append(")");
                    }

                    sb.AppendLine();
                }
                sb.AppendLine("\t}");

                sb.AppendLine("}");
            }

            sb.AppendLine("Eof");

            return sb.ToString();
        }
    }
}
