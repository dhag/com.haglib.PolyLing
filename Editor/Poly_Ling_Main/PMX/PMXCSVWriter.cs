// Assets/Editor/Poly_Ling/PMX/Export/PMXCSVWriter.cs
// PMX CSV形式での出力 — PMXEditor互換フォーマット

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Poly_Ling.MQO;
using UnityEngine;

namespace Poly_Ling.PMX
{
    public static class PMXCSVWriter
    {
        public static void Save(PMXDocument document, string filePath, int decimalPrecision = MQOExportSettings.DefaultDecimalPrecision)
        {
            var sb = new StringBuilder();
            string fmt = $"F{decimalPrecision}";

            WriteHeader(sb, document);
            WriteModelInfo(sb, document.ModelInfo);
            WriteVertices(sb, document, fmt);
            WriteMaterials(sb, document, fmt);
            WriteFaces(sb, document);
            WriteBones(sb, document, fmt);
            WriteMorphs(sb, document, fmt);
            WriteDisplayFrames(sb, document);
            WriteBodies(sb, document, fmt);
            WriteJoints(sb, document, fmt);
            WriteSoftBodies(sb, document, fmt);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PMXCSVWriter] Saved to: {filePath}");
        }

        private static void WriteHeader(StringBuilder sb, PMXDocument document)
        {
            sb.AppendLine(";PmxHeader,ver,文字エンコード(0:UTF16/1:UTF8),追加UV数");
            sb.AppendLine($"PmxHeader,{document.Version:F1},{document.CharacterEncoding},{document.AdditionalUVCount}");
        }

        private static void WriteModelInfo(StringBuilder sb, PMXModelInfo info)
        {
            sb.AppendLine(";PmxModelInfo,モデル名,モデル名(英),コメント,コメント(英)");
            sb.AppendLine($"PmxModelInfo,{Escape(info.Name)},{Escape(info.NameEnglish)},{EscapeMultiLine(info.Comment)},{EscapeMultiLine(info.CommentEnglish)}");
        }

        private static void WriteVertices(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine(";PmxVertex,頂点Index,位置_x,位置_y,位置_z,法線_x,法線_y,法線_z,エッジ倍率,UV_u,UV_v," +
                "追加UV1_x,追加UV1_y,追加UV1_z,追加UV1_w,追加UV2_x,追加UV2_y,追加UV2_z,追加UV2_w," +
                "追加UV3_x,追加UV3_y,追加UV3_z,追加UV3_w,追加UV4_x,追加UV4_y,追加UV4_z,追加UV4_w," +
                "ウェイト変形タイプ(0:BDEF1/1:BDEF2/2:BDEF4/3:SDEF/4:QDEF)," +
                "ウェイト1_ボーン名,ウェイト1_ウェイト値,ウェイト2_ボーン名,ウェイト2_ウェイト値," +
                "ウェイト3_ボーン名,ウェイト3_ウェイト値,ウェイト4_ボーン名,ウェイト4_ウェイト値," +
                "C_x,C_y,C_z,R0_x,R0_y,R0_z,R1_x,R1_y,R1_z");

            foreach (var v in document.Vertices)
            {
                var bw = v.BoneWeights ?? new PMXBoneWeight[0];
                var uvs = v.AdditionalUVs;
                string auv = "";
                for (int i = 0; i < 4; i++)
                {
                    if (uvs != null && i < uvs.Length)
                        auv += $",{F(uvs[i].x, fmt)},{F(uvs[i].y, fmt)},{F(uvs[i].z, fmt)},{F(uvs[i].w, fmt)}";
                    else
                        auv += ",0,0,0,0";
                }
                string b1 = bw.Length > 0 ? Escape(bw[0].BoneName ?? "") : "\"\"";
                float w1 = bw.Length > 0 ? bw[0].Weight : 0;
                string b2 = bw.Length > 1 ? Escape(bw[1].BoneName ?? "") : "\"\"";
                float w2 = bw.Length > 1 ? bw[1].Weight : 0;
                string b3 = bw.Length > 2 ? Escape(bw[2].BoneName ?? "") : "\"\"";
                float w3 = bw.Length > 2 ? bw[2].Weight : 0;
                string b4 = bw.Length > 3 ? Escape(bw[3].BoneName ?? "") : "\"\"";
                float w4 = bw.Length > 3 ? bw[3].Weight : 0;
                var sC = v.SDEF_C; var sR0 = v.SDEF_R0; var sR1 = v.SDEF_R1;

                sb.AppendLine($"PmxVertex,{v.Index}," +
                    $"{F(v.Position.x,fmt)},{F(v.Position.y,fmt)},{F(v.Position.z,fmt)}," +
                    $"{F(v.Normal.x,fmt)},{F(v.Normal.y,fmt)},{F(v.Normal.z,fmt)}," +
                    $"{F(v.EdgeScale,fmt)},{F(v.UV.x,fmt)},{F(v.UV.y,fmt)}" +
                    auv + $",{v.WeightType}," +
                    $"{b1},{F(w1,fmt)},{b2},{F(w2,fmt)},{b3},{F(w3,fmt)},{b4},{F(w4,fmt)}," +
                    $"{F(sC.x,fmt)},{F(sC.y,fmt)},{F(sC.z,fmt)}," +
                    $"{F(sR0.x,fmt)},{F(sR0.y,fmt)},{F(sR0.z,fmt)}," +
                    $"{F(sR1.x,fmt)},{F(sR1.y,fmt)},{F(sR1.z,fmt)}");
            }
        }

        private static void WriteMaterials(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine(";PmxMaterial,材質名,材質名(英),拡散色_R,拡散色_G,拡散色_B,拡散色_A(非透過度)," +
                "反射色_R,反射色_G,反射色_B,反射強度,環境色_R,環境色_G,環境色_B," +
                "両面描画(0/1),地面影(0/1),セルフ影マップ(0/1),セルフ影(0/1),頂点色(0/1)," +
                "描画(0:Tri/1:Point/2:Line),エッジ(0/1),エッジサイズ,エッジ色_R,エッジ色_G,エッジ色_B,エッジ色_A," +
                "テクスチャパス,スフィアテクスチャパス,スフィアモード(0:無効/1:乗算/2:加算/3:サブテクスチャ)," +
                "Toonテクスチャパス,メモ");
            foreach (var mat in document.Materials)
            {
                int fl = mat.DrawFlags;
                int ds = (fl&0x01)!=0?1:0, gs = (fl&0x02)!=0?1:0, ssm = (fl&0x04)!=0?1:0;
                int ss = (fl&0x08)!=0?1:0, vc = 0;
                int dm = 0, edge = (fl&0x10)!=0?1:0;
                sb.AppendLine($"PmxMaterial,{Escape(mat.Name)},{Escape(mat.NameEnglish)}," +
                    $"{F(mat.Diffuse.r,fmt)},{F(mat.Diffuse.g,fmt)},{F(mat.Diffuse.b,fmt)},{F(mat.Diffuse.a,fmt)}," +
                    $"{F(mat.Specular.r,fmt)},{F(mat.Specular.g,fmt)},{F(mat.Specular.b,fmt)},{F(mat.SpecularPower,fmt)}," +
                    $"{F(mat.Ambient.r,fmt)},{F(mat.Ambient.g,fmt)},{F(mat.Ambient.b,fmt)}," +
                    $"{ds},{gs},{ssm},{ss},{vc},{dm},{edge},{F(mat.EdgeSize,fmt)}," +
                    $"{F(mat.EdgeColor.r,fmt)},{F(mat.EdgeColor.g,fmt)},{F(mat.EdgeColor.b,fmt)},{F(mat.EdgeColor.a,fmt)}," +
                    $"{Escape(mat.TexturePath??"")},{Escape(mat.SphereTexturePath??"")},{mat.SphereMode}," +
                    $"{Escape(mat.ToonTexturePath??"")},{Escape(mat.Memo??"")}");
            }
        }

        private static void WriteFaces(StringBuilder sb, PMXDocument document)
        {
            string cur = null;
            foreach (var face in document.Faces)
            {
                if (face.MaterialName != cur)
                {
                    sb.AppendLine(";PmxFace,親材質名,面Index,頂点Index1,頂点Index2,頂点Index3");
                    cur = face.MaterialName;
                }
                sb.AppendLine($"PmxFace,{Escape(face.MaterialName)},{face.FaceIndex},{face.VertexIndex1},{face.VertexIndex2},{face.VertexIndex3}");
            }
        }

        private static void WriteBones(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine(";PmxBone,ボーン名,ボーン名(英),変形階層,物理後(0/1),位置_x,位置_y,位置_z," +
                "回転(0/1),移動(0/1),IK(0/1),表示(0/1),操作(0/1),親ボーン名," +
                "表示先(0:オフセット/1:ボーン),表示先ボーン名,表示先オフセット_x,表示先オフセット_y,表示先オフセット_z," +
                "ローカル付与(0/1),回転付与(0/1),移動付与(0/1),付与率,付与親名," +
                "軸制限(0/1),制限軸_x,制限軸_y,制限軸_z," +
                "ローカル軸(0/1),ローカルX軸_x,ローカルX軸_y,ローカルX軸_z,ローカルZ軸_x,ローカルZ軸_y,ローカルZ軸_z," +
                "外部親(0/1),外部親Key,IKTarget名,IKLoop,IK単位角[deg]");

            foreach (var bone in document.Bones)
            {
                int f = bone.Flags;
                int ct=(f&0x0001)!=0?1:0, rot=(f&0x0002)!=0?1:0, mov=(f&0x0004)!=0?1:0;
                int vis=(f&0x0008)!=0?1:0, op=(f&0x0010)!=0?1:0, ik=(f&0x0020)!=0?1:0;
                int lg=(f&0x0080)!=0?1:0, gr=(f&0x0100)!=0?1:0, gm=(f&0x0200)!=0?1:0;
                int fa=(f&0x0400)!=0?1:0, la=(f&0x0800)!=0?1:0;
                int pa=(f&0x1000)!=0?1:0, ep=(f&0x2000)!=0?1:0;
                float ikDeg = bone.IKLimitAngle * (180f / Mathf.PI);

                sb.AppendLine($"PmxBone,{Escape(bone.Name)},{Escape(bone.NameEnglish??"")}," +
                    $"{bone.TransformLevel},{pa}," +
                    $"{F(bone.Position.x,fmt)},{F(bone.Position.y,fmt)},{F(bone.Position.z,fmt)}," +
                    $"{rot},{mov},{ik},{vis},{op},{Escape(bone.ParentBoneName??"")}," +
                    $"{ct},{Escape(bone.ConnectBoneName??"")}," +
                    $"{F(bone.ConnectOffset.x,fmt)},{F(bone.ConnectOffset.y,fmt)},{F(bone.ConnectOffset.z,fmt)}," +
                    $"{lg},{gr},{gm},{F(bone.GrantRate,fmt)},{Escape(bone.GrantParentBoneName??"")}," +
                    $"{fa},{F(bone.FixedAxis.x,fmt)},{F(bone.FixedAxis.y,fmt)},{F(bone.FixedAxis.z,fmt)}," +
                    $"{la},{F(bone.LocalAxisX.x,fmt)},{F(bone.LocalAxisX.y,fmt)},{F(bone.LocalAxisX.z,fmt)}," +
                    $"{F(bone.LocalAxisZ.x,fmt)},{F(bone.LocalAxisZ.y,fmt)},{F(bone.LocalAxisZ.z,fmt)}," +
                    $"{ep},{bone.ExternalParentKey}," +
                    $"{Escape(bone.IKTargetBoneName??"")},{bone.IKLoopCount},{F(ikDeg,fmt)}");

                if (ik != 0 && bone.IKLinks.Count > 0)
                {
                    sb.AppendLine(";PmxIKLink,親ボーン名,Linkボーン名,角度制限(0/1),XL[deg],XH[deg],YL[deg],YH[deg],ZL[deg],ZH[deg]");
                    foreach (var lk in bone.IKLinks)
                    {
                        float r2d = 180f / Mathf.PI;
                        sb.AppendLine($"PmxIKLink,{Escape(bone.Name)},{Escape(lk.BoneName??"")}," +
                            $"{(lk.HasLimit?1:0)}," +
                            $"{F(lk.LimitMin.x*r2d,fmt)},{F(lk.LimitMax.x*r2d,fmt)}," +
                            $"{F(lk.LimitMin.y*r2d,fmt)},{F(lk.LimitMax.y*r2d,fmt)}," +
                            $"{F(lk.LimitMin.z*r2d,fmt)},{F(lk.LimitMax.z*r2d,fmt)}");
                    }
                }
            }
        }

        private static void WriteMorphs(StringBuilder sb, PMXDocument document, string fmt)
        {
            var boneN = new Dictionary<int,string>();
            for (int i=0;i<document.Bones.Count;i++) boneN[i]=document.Bones[i].Name??"";
            var matN = new Dictionary<int,string>();
            for (int i=0;i<document.Materials.Count;i++) matN[i]=document.Materials[i].Name??"";
            var morphN = new Dictionary<int,string>();
            for (int i=0;i<document.Morphs.Count;i++) morphN[i]=document.Morphs[i].Name??"";

            foreach (var morph in document.Morphs)
            {
                sb.AppendLine(";PmxMorph,モーフ名,モーフ名(英),パネル(0:無効/1:眉(左下)/2:目(左上)/3:口(右上)/4:その他(右下))," +
                    "モーフ種類(0:グループモーフ/1:頂点モーフ/2:ボーンモーフ/3:UV(Tex)モーフ/4:追加UV1モーフ/5:追加UV2モーフ/" +
                    "6:追加UV3モーフ/7:追加UV4モーフ/8:材質モーフ/9:フリップモーフ/10:インパルスモーフ)");
                sb.AppendLine($"PmxMorph,{Escape(morph.Name)},{Escape(morph.NameEnglish??"")},{morph.Panel},{morph.MorphType}");

                int idx = 0;
                foreach (var off in morph.Offsets)
                {
                    switch (off)
                    {
                        case PMXVertexMorphOffset vo:
                            sb.AppendLine(";PmxVertexMorph,親モーフ名,オフセットIndex,頂点Index,位置オフセット_x,位置オフセット_y,位置オフセット_z");
                            sb.AppendLine($"PmxVertexMorph,{Escape(morph.Name)},{idx},{vo.VertexIndex},{F(vo.Offset.x,fmt)},{F(vo.Offset.y,fmt)},{F(vo.Offset.z,fmt)}");
                            break;
                        case PMXUVMorphOffset uvo:
                            sb.AppendLine(";PmxUVMorph,親モーフ名,オフセットIndex,頂点Index,UVオフセット_x,UVオフセット_y,UVオフセット_z,UVオフセット_w");
                            sb.AppendLine($"PmxUVMorph,{Escape(morph.Name)},{idx},{uvo.VertexIndex},{F(uvo.Offset.x,fmt)},{F(uvo.Offset.y,fmt)},{F(uvo.Offset.z,fmt)},{F(uvo.Offset.w,fmt)}");
                            break;
                        case PMXBoneMorphOffset bo:
                        {
                            string bn = bo.BoneName ?? (boneN.TryGetValue(bo.BoneIndex, out var n)?n:"");
                            sb.AppendLine(";PmxBoneMorph,親モーフ名,オフセットIndex,ボーン名,移動_x,移動_y,移動_z,回転_x,回転_y,回転_z,回転_w");
                            sb.AppendLine($"PmxBoneMorph,{Escape(morph.Name)},{idx},{Escape(bn)},{F(bo.Translation.x,fmt)},{F(bo.Translation.y,fmt)},{F(bo.Translation.z,fmt)},{F(bo.Rotation.x,fmt)},{F(bo.Rotation.y,fmt)},{F(bo.Rotation.z,fmt)},{F(bo.Rotation.w,fmt)}");
                            break;
                        }
                        case PMXMaterialMorphOffset mo:
                        {
                            string mn = mo.MaterialName ?? (matN.TryGetValue(mo.MaterialIndex, out var n)?n:"");
                            sb.AppendLine(";PmxMaterialMorph,親モーフ名,オフセットIndex,材質名,演算タイプ(0:乗算/1:加算),拡散色_R,拡散色_G,拡散色_B,拡散色_A(非透過度),反射色_R,反射色_G,反射色_B,反射強度,環境色_R,環境色_G,環境色_B,エッジサイズ,エッジ色_R,エッジ色_G,エッジ色_B,エッジ色_A,Tex_R,Tex_G,Tex_B,Tex_A,スフィア_R,スフィア_G,スフィア_B,スフィア_A,Toon_R,Toon_G,Toon_B,Toon_A");
                            sb.AppendLine($"PmxMaterialMorph,{Escape(morph.Name)},{idx},{Escape(mn)},{mo.Operation}," +
                                $"{F(mo.Diffuse.r,fmt)},{F(mo.Diffuse.g,fmt)},{F(mo.Diffuse.b,fmt)},{F(mo.Diffuse.a,fmt)}," +
                                $"{F(mo.Specular.r,fmt)},{F(mo.Specular.g,fmt)},{F(mo.Specular.b,fmt)},{F(mo.SpecularPower,fmt)}," +
                                $"{F(mo.Ambient.r,fmt)},{F(mo.Ambient.g,fmt)},{F(mo.Ambient.b,fmt)}," +
                                $"{F(mo.EdgeSize,fmt)},{F(mo.EdgeColor.r,fmt)},{F(mo.EdgeColor.g,fmt)},{F(mo.EdgeColor.b,fmt)},{F(mo.EdgeColor.a,fmt)}," +
                                $"{F(mo.TextureCoef.r,fmt)},{F(mo.TextureCoef.g,fmt)},{F(mo.TextureCoef.b,fmt)},{F(mo.TextureCoef.a,fmt)}," +
                                $"{F(mo.SphereCoef.r,fmt)},{F(mo.SphereCoef.g,fmt)},{F(mo.SphereCoef.b,fmt)},{F(mo.SphereCoef.a,fmt)}," +
                                $"{F(mo.ToonCoef.r,fmt)},{F(mo.ToonCoef.g,fmt)},{F(mo.ToonCoef.b,fmt)},{F(mo.ToonCoef.a,fmt)}");
                            break;
                        }
                        case PMXGroupMorphOffset go:
                        {
                            string gn = go.MorphName ?? (morphN.TryGetValue(go.MorphIndex, out var n)?n:"");
                            sb.AppendLine(";PmxGroupMorph,親モーフ名,オフセットIndex,モーフ名,影響度");
                            sb.AppendLine($"PmxGroupMorph,{Escape(morph.Name)},{idx},{Escape(gn)},{F(go.Weight,fmt)}");
                            break;
                        }
                        case PMXImpulseMorphOffset imo:
                        {
                            sb.AppendLine(";PmxImpulseMorph,親モーフ名,オフセットIndex,剛体Index,ローカル(0/1),速度_x,速度_y,速度_z,トルク_x,トルク_y,トルク_z");
                            sb.AppendLine($"PmxImpulseMorph,{Escape(morph.Name)},{idx},{imo.RigidBodyIndex},{(imo.IsLocal?1:0)},{F(imo.Velocity.x,fmt)},{F(imo.Velocity.y,fmt)},{F(imo.Velocity.z,fmt)},{F(imo.Torque.x,fmt)},{F(imo.Torque.y,fmt)},{F(imo.Torque.z,fmt)}");
                            break;
                        }
                    }
                    idx++;
                }
            }
        }

        private static void WriteDisplayFrames(StringBuilder sb, PMXDocument document)
        {
            if (document.DisplayFrames.Count == 0) return;
            var boneN = new Dictionary<int,string>();
            for (int i=0;i<document.Bones.Count;i++) boneN[i]=document.Bones[i].Name??"";
            var morphN = new Dictionary<int,string>();
            for (int i=0;i<document.Morphs.Count;i++) morphN[i]=document.Morphs[i].Name??"";

            foreach (var frame in document.DisplayFrames)
            {
                sb.AppendLine(";PmxNode,表示枠名,表示枠名(英)");
                sb.AppendLine($"PmxNode,{Escape(frame.Name)},{Escape(frame.NameEnglish??"")}");
                foreach (var elem in frame.Elements)
                {
                    int type = elem.IsMorph ? 1 : 0;
                    string name = elem.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        if (elem.IsMorph) morphN.TryGetValue(elem.Index, out name);
                        else boneN.TryGetValue(elem.Index, out name);
                    }
                    sb.AppendLine(";PmxNodeItem,親表示枠名,対象(0:ボーン/1:モーフ),ボーン名／モーフ名");
                    sb.AppendLine($"PmxNodeItem,{Escape(frame.Name)},{type},{Escape(name??"")}");
                }
            }
        }

        private static void WriteBodies(StringBuilder sb, PMXDocument document, string fmt)
        {
            if (document.RigidBodies.Count == 0) return;
            sb.AppendLine(";PmxBody,剛体名,剛体名(英),関連ボーン名,剛体タイプ(0:Bone/1:物理演算/2:物理演算+ボーン追従)," +
                "グループ(0~15),非衝突グループ文字列(ex:1 2 3 4),形状(0:球/1:箱/2:カプセル)," +
                "サイズ_x,サイズ_y,サイズ_z,位置_x,位置_y,位置_z,回転_x[deg],回転_y[deg],回転_z[deg]," +
                "質量,移動減衰,回転減衰,反発力,摩擦力");
            foreach (var b in document.RigidBodies)
            {
                sb.AppendLine($"PmxBody,{Escape(b.Name)},{Escape(b.NameEnglish??"")}," +
                    $"{Escape(b.RelatedBoneName??"")},{b.PhysicsMode}," +
                    $"{b.Group},{Escape(b.NonCollisionGroups??"")},{b.Shape}," +
                    $"{F(b.Size.x,fmt)},{F(b.Size.y,fmt)},{F(b.Size.z,fmt)}," +
                    $"{F(b.Position.x,fmt)},{F(b.Position.y,fmt)},{F(b.Position.z,fmt)}," +
                    $"{F(b.Rotation.x,fmt)},{F(b.Rotation.y,fmt)},{F(b.Rotation.z,fmt)}," +
                    $"{F(b.Mass,fmt)},{F(b.LinearDamping,fmt)},{F(b.AngularDamping,fmt)}," +
                    $"{F(b.Restitution,fmt)},{F(b.Friction,fmt)}");
            }
        }

        private static void WriteJoints(StringBuilder sb, PMXDocument document, string fmt)
        {
            if (document.Joints.Count == 0) return;
            sb.AppendLine(";PmxJoint,Joint名,Joint名(英),剛体名A,剛体名B," +
                "Jointタイプ(0:ﾊﾞﾈ付6DOF/1:6DOF/2:P2P/3:ConeTwist/4:Slider/5:Hinge/)," +
                "位置_x,位置_y,位置_z,回転_x[deg],回転_y[deg],回転_z[deg]," +
                "移動下限_x,移動下限_y,移動下限_z,移動上限_x,移動上限_y,移動上限_z," +
                "回転下限_x[deg],回転下限_y[deg],回転下限_z[deg],回転上限_x[deg],回転上限_y[deg],回転上限_z[deg]," +
                "バネ定数-移動_x,バネ定数-移動_y,バネ定数-移動_z,バネ定数-回転_x,バネ定数-回転_y,バネ定数-回転_z");
            foreach (var j in document.Joints)
            {
                sb.AppendLine($"PmxJoint,{Escape(j.Name)},{Escape(j.NameEnglish??"")}," +
                    $"{Escape(j.BodyAName??"")},{Escape(j.BodyBName??"")},{j.JointType}," +
                    $"{F(j.Position.x,fmt)},{F(j.Position.y,fmt)},{F(j.Position.z,fmt)}," +
                    $"{F(j.Rotation.x,fmt)},{F(j.Rotation.y,fmt)},{F(j.Rotation.z,fmt)}," +
                    $"{F(j.TranslationMin.x,fmt)},{F(j.TranslationMin.y,fmt)},{F(j.TranslationMin.z,fmt)}," +
                    $"{F(j.TranslationMax.x,fmt)},{F(j.TranslationMax.y,fmt)},{F(j.TranslationMax.z,fmt)}," +
                    $"{F(j.RotationMin.x,fmt)},{F(j.RotationMin.y,fmt)},{F(j.RotationMin.z,fmt)}," +
                    $"{F(j.RotationMax.x,fmt)},{F(j.RotationMax.y,fmt)},{F(j.RotationMax.z,fmt)}," +
                    $"{F(j.SpringTranslation.x,fmt)},{F(j.SpringTranslation.y,fmt)},{F(j.SpringTranslation.z,fmt)}," +
                    $"{F(j.SpringRotation.x,fmt)},{F(j.SpringRotation.y,fmt)},{F(j.SpringRotation.z,fmt)}");
            }
        }

        private static void WriteSoftBodies(StringBuilder sb, PMXDocument document, string fmt)
        {
            if (document.SoftBodies.Count == 0) return;
            sb.AppendLine(";PmxSoftBody,SoftBody名,SoftBody名(英),形状(0:TriMesh/1:Rope),関連材質名," +
                "グループ(0~15),非衝突グループ文字列(ex:1 2 3 4),B-Link(0/1),B-Link距離,クラスタ(0/1),クラスタ数," +
                "リンク交雑(0/1),総質量,衝突マージン,Aero(0-4),VCF,DP,DG,LF,PR,VC,DF,MT," +
                "CHR,KHR,SHR,AHR,SRHR_CL,SKHR_CL,SSHR_CL,SR_SPLT_CL,SK_SPLT_CL,SS_SPLT_CL," +
                "V_IT,P_IT,D_IT,C_IT,LST,AST,VST");
            var matN = new Dictionary<int,string>();
            for (int i=0;i<document.Materials.Count;i++) matN[i]=document.Materials[i].Name??"";
            foreach (var s in document.SoftBodies)
            {
                string mn = matN.TryGetValue(s.MaterialIndex, out var n)?n:"";
                int bl=(s.Flags&0x01)!=0?1:0, cl=(s.Flags&0x02)!=0?1:0, lc=(s.Flags&0x04)!=0?1:0;
                sb.AppendLine($"PmxSoftBody,{Escape(s.Name)},{Escape(s.NameEnglish??"")},{s.Shape},{Escape(mn)}," +
                    $"{s.Group},,{bl},{s.BendingLinkDistance},{cl},{s.ClusterCount}," +
                    $"{lc},{F(s.TotalMass,fmt)},{F(s.Margin,fmt)},{s.AeroModel}," +
                    $"{F(s.VCF,fmt)},{F(s.DP,fmt)},{F(s.DG,fmt)},{F(s.LF,fmt)}," +
                    $"{F(s.PR,fmt)},{F(s.VC,fmt)},{F(s.DF,fmt)},{F(s.MT,fmt)}," +
                    $"{F(s.CHR,fmt)},{F(s.KHR,fmt)},{F(s.SHR,fmt)},{F(s.AHR,fmt)}," +
                    $"{F(s.SRHR_CL,fmt)},{F(s.SKHR_CL,fmt)},{F(s.SSHR_CL,fmt)}," +
                    $"{F(s.SR_SPLT_CL,fmt)},{F(s.SK_SPLT_CL,fmt)},{F(s.SS_SPLT_CL,fmt)}," +
                    $"{s.V_IT},{s.P_IT},{s.D_IT},{s.C_IT}," +
                    $"{F(s.LST,fmt)},{F(s.AST,fmt)},{F(s.VST,fmt)}");
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string F(float v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\"\"";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string EscapeMultiLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "\"\"";
            text = text.Replace("\\", "\\\\");
            text = text.Replace("\r\n", "\\r\\n");
            text = text.Replace("\n", "\\r\\n");
            text = text.Replace(" ", "\\ ");
            text = text.Replace(".", "\\.");
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
