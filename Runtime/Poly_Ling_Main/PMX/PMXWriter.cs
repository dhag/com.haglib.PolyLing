// Assets/Editor/Poly_Ling/PMX/Export/PMXWriter.cs
// PMXバイナリ形式での出力

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXバイナリファイルライター
    /// PMX 2.1形式で出力
    /// </summary>
    public static class PMXWriter
    {
        // PMXマジックナンバー
        private static readonly byte[] PMX_MAGIC = { 0x50, 0x4D, 0x58, 0x20 };  // "PMX "

        // エンコーディング設定（Save時に設定）
        private static int _characterEncoding = 0;

        /// <summary>
        /// PMXファイルを書き出し
        /// </summary>
        public static void Save(PMXDocument document, string filePath)
        {
            // エンコーディングを設定
            _characterEncoding = document.CharacterEncoding;

            using (var stream = new FileStream(filePath, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, document);
                WriteModelInfo(writer, document.ModelInfo);
                WriteVertices(writer, document);
                WriteFaces(writer, document);
                WriteTextures(writer, document);
                WriteMaterials(writer, document);
                WriteBones(writer, document);
                WriteMorphs(writer, document);
                WriteDisplayFrames(writer, document);
                WriteBodies(writer, document);
                WriteJoints(writer, document);

                // SoftBody (PMX 2.1以降)
                if (document.Version >= 2.1f)
                {
                    WriteSoftBodies(writer, document);
                }
            }

            Debug.Log($"[PMXWriter] Saved to: {filePath}");
        }

        // ================================================================
        // ヘッダー
        // ================================================================

        private static void WriteHeader(BinaryWriter writer, PMXDocument document)
        {
            // マジックナンバー "PMX "
            writer.Write(PMX_MAGIC);

            // バージョン (float)
            writer.Write(document.Version);

            // グローバル情報のバイト数
            writer.Write((byte)8);

            // エンコード (0:UTF-16, 1:UTF-8)
            writer.Write((byte)document.CharacterEncoding);

            // 追加UV数
            writer.Write((byte)document.AdditionalUVCount);

            // 各インデックスサイズ
            int vertexCount = document.Vertices.Count;
            int textureCount = GetTextureCount(document);
            int materialCount = document.Materials.Count;
            int boneCount = document.Bones.Count;
            int morphCount = document.Morphs.Count;
            int bodyCount = document.RigidBodies.Count;

            writer.Write(GetVertexIndexSize(vertexCount));    // 頂点インデックスサイズ（符号なし）
            writer.Write(GetSignedIndexSize(textureCount));   // テクスチャインデックスサイズ（符号あり）
            writer.Write(GetSignedIndexSize(materialCount));  // マテリアルインデックスサイズ（符号あり）
            writer.Write(GetSignedIndexSize(boneCount));      // ボーンインデックスサイズ（符号あり）
            writer.Write(GetSignedIndexSize(morphCount));     // モーフインデックスサイズ（符号あり）
            writer.Write(GetSignedIndexSize(bodyCount));      // 剛体インデックスサイズ（符号あり）
        }

        /// <summary>
        /// 頂点インデックスサイズを取得（符号なし）
        /// </summary>
        private static byte GetVertexIndexSize(int count)
        {
            // 頂点は符号なし: 1→0-255, 2→256-65535, 4→65536以上
            if (count <= 255) return 1;
            if (count <= 65535) return 2;
            return 4;
        }

        /// <summary>
        /// 符号ありインデックスサイズを取得（ボーン/テクスチャ/材質/モーフ/剛体用）
        /// </summary>
        private static byte GetSignedIndexSize(int count)
        {
            // 符号あり: 1→0-127, 2→128-32767, 4→32768以上
            // -1を非参照値として使うため符号あり
            if (count <= 127) return 1;
            if (count <= 32767) return 2;
            return 4;
        }

        // ================================================================
        // モデル情報
        // ================================================================

        private static void WriteModelInfo(BinaryWriter writer, PMXModelInfo info)
        {
            WriteText(writer, info.Name ?? "");
            WriteText(writer, info.NameEnglish ?? "");
            WriteText(writer, info.Comment ?? "");
            WriteText(writer, info.CommentEnglish ?? "");
        }

        // ================================================================
        // 頂点
        // ================================================================

        private static void WriteVertices(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Vertices.Count);

            int boneIndexSize = GetSignedIndexSize(document.Bones.Count);

            foreach (var vertex in document.Vertices)
            {
                // 位置
                WriteVector3(writer, vertex.Position);

                // 法線
                WriteVector3(writer, vertex.Normal);

                // UV
                WriteVector2(writer, vertex.UV);

                // 追加UV
                for (int i = 0; i < document.AdditionalUVCount; i++)
                {
                    if (vertex.AdditionalUVs != null && i < vertex.AdditionalUVs.Length)
                        WriteVector4(writer, vertex.AdditionalUVs[i]);
                    else
                        WriteVector4(writer, Vector4.zero);
                }

                // ウェイトタイプ
                writer.Write((byte)vertex.WeightType);

                // ボーンウェイト
                WriteBoneWeight(writer, document, vertex, boneIndexSize);

                // エッジ倍率
                writer.Write(vertex.EdgeScale);
            }
        }

        private static void WriteBoneWeight(BinaryWriter writer, PMXDocument document, PMXVertex vertex, int boneIndexSize)
        {
            var boneWeights = vertex.BoneWeights ?? new PMXBoneWeight[0];

            switch (vertex.WeightType)
            {
                case 0:  // BDEF1
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 0), boneIndexSize);
                    break;

                case 1:  // BDEF2
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 0), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 1), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    break;

                case 2:  // BDEF4
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 0), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 1), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 2), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 3), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    writer.Write(GetWeight(boneWeights, 1));
                    writer.Write(GetWeight(boneWeights, 2));
                    writer.Write(GetWeight(boneWeights, 3));
                    break;

                case 3:  // SDEF
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 0), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 1), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    WriteVector3(writer, vertex.SDEF_C);
                    WriteVector3(writer, vertex.SDEF_R0);
                    WriteVector3(writer, vertex.SDEF_R1);
                    break;

                case 4:  // QDEF
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 0), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 1), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 2), boneIndexSize);
                    WriteSignedIndex(writer, GetBoneIdx(boneWeights, 3), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    writer.Write(GetWeight(boneWeights, 1));
                    writer.Write(GetWeight(boneWeights, 2));
                    writer.Write(GetWeight(boneWeights, 3));
                    break;
            }
        }

        private static int GetBoneIdx(PMXBoneWeight[] weights, int index)
        {
            return (weights != null && index < weights.Length) ? weights[index].BoneIndex : 0;
        }

        private static float GetWeight(PMXBoneWeight[] weights, int index)
        {
            return (weights != null && index < weights.Length) ? weights[index].Weight : 0f;
        }

        // ================================================================
        // 面
        // ================================================================

        private static void WriteFaces(BinaryWriter writer, PMXDocument document)
        {
            // 面数 × 3 = 頂点インデックス数
            writer.Write(document.Faces.Count * 3);

            int vertexIndexSize = GetVertexIndexSize(document.Vertices.Count);

            foreach (var face in document.Faces)
            {
                WriteUnsignedIndex(writer, face.VertexIndex1, vertexIndexSize);
                WriteUnsignedIndex(writer, face.VertexIndex2, vertexIndexSize);
                WriteUnsignedIndex(writer, face.VertexIndex3, vertexIndexSize);
            }
        }

        // ================================================================
        // テクスチャ
        // ================================================================

        private static void WriteTextures(BinaryWriter writer, PMXDocument document)
        {
            // 使用されているテクスチャパスを収集
            var textures = new List<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath) && !textures.Contains(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath) && !textures.Contains(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon && !textures.Contains(mat.ToonTexturePath))
                    textures.Add(mat.ToonTexturePath);
            }

            writer.Write(textures.Count);
            foreach (var tex in textures)
            {
                WriteText(writer, tex);
            }
        }

        private static int GetTextureCount(PMXDocument document)
        {
            var textures = new HashSet<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon)
                    textures.Add(mat.ToonTexturePath);
            }
            return textures.Count;
        }

        private static int GetTextureIndex(PMXDocument document, string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath)) return -1;

            var textures = new List<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath) && !textures.Contains(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath) && !textures.Contains(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon && !textures.Contains(mat.ToonTexturePath))
                    textures.Add(mat.ToonTexturePath);
            }

            return textures.IndexOf(texturePath);
        }

        // ================================================================
        // マテリアル
        // ================================================================

        private static void WriteMaterials(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Materials.Count);

            int textureIndexSize = GetSignedIndexSize(GetTextureCount(document));

            foreach (var mat in document.Materials)
            {
                WriteText(writer, mat.Name ?? "");
                WriteText(writer, mat.NameEnglish ?? "");

                // Diffuse (RGBA)
                WriteColor4(writer, mat.Diffuse);

                // Specular (RGB)
                WriteColor3(writer, mat.Specular);

                // SpecularPower
                writer.Write(mat.SpecularPower);

                // Ambient (RGB)
                WriteColor3(writer, mat.Ambient);

                // 描画フラグ
                writer.Write((byte)mat.DrawFlags);

                // エッジ色
                WriteColor4(writer, mat.EdgeColor);

                // エッジサイズ
                writer.Write(mat.EdgeSize);

                // テクスチャインデックス
                WriteSignedIndex(writer, GetTextureIndex(document, mat.TexturePath), textureIndexSize);

                // スフィアテクスチャインデックス
                WriteSignedIndex(writer, GetTextureIndex(document, mat.SphereTexturePath), textureIndexSize);

                // スフィアモード
                writer.Write((byte)mat.SphereMode);

                // 共有Toonフラグ
                writer.Write((byte)(mat.SharedToon ? 1 : 0));

                // Toonテクスチャ
                if (mat.SharedToon)
                {
                    int toonIndex = mat.ToonTextureIndex >= 0 ? mat.ToonTextureIndex : 0;
                    writer.Write((byte)toonIndex);
                }
                else
                {
                    WriteSignedIndex(writer, GetTextureIndex(document, mat.ToonTexturePath), textureIndexSize);
                }

                // メモ
                WriteText(writer, mat.Memo ?? "");

                // 面数（頂点数ではなく、インデックス数 = 面数 × 3）
                writer.Write(mat.FaceCount * 3);
            }
        }

        // ================================================================
        // ボーン
        // ================================================================

        private static void WriteBones(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Bones.Count);

            int boneIndexSize = GetSignedIndexSize(document.Bones.Count);

            foreach (var bone in document.Bones)
            {
                WriteText(writer, bone.Name ?? "");
                WriteText(writer, bone.NameEnglish ?? "");

                // 位置
                WriteVector3(writer, bone.Position);

                // 親ボーンインデックス（読み込み時のインデックスを使用）
                int parentIndex = bone.ParentIndex;
                if (parentIndex == 0 && !string.IsNullOrEmpty(bone.ParentBoneName))
                {
                    // インデックスが0で名前が設定されている場合は名前から検索
                    parentIndex = document.GetBoneIndex(bone.ParentBoneName);
                }
                WriteSignedIndex(writer, parentIndex, boneIndexSize);

                // 変形階層
                writer.Write(bone.TransformLevel);

                // フラグ
                writer.Write((ushort)bone.Flags);

                // 接続先
                bool connectByBone = (bone.Flags & 0x0001) != 0;
                if (connectByBone)
                {
                    int connectIndex = bone.ConnectBoneIndex;
                    if (connectIndex == 0 && !string.IsNullOrEmpty(bone.ConnectBoneName))
                    {
                        connectIndex = document.GetBoneIndex(bone.ConnectBoneName);
                    }
                    WriteSignedIndex(writer, connectIndex, boneIndexSize);
                }
                else
                {
                    WriteVector3(writer, bone.ConnectOffset);
                }

                // 回転付与・移動付与
                bool hasGrant = (bone.Flags & 0x0100) != 0 || (bone.Flags & 0x0200) != 0;
                if (hasGrant)
                {
                    int grantParentIndex = bone.GrantParentIndex;
                    if (grantParentIndex == 0 && !string.IsNullOrEmpty(bone.GrantParentBoneName))
                    {
                        grantParentIndex = document.GetBoneIndex(bone.GrantParentBoneName);
                    }
                    WriteSignedIndex(writer, grantParentIndex, boneIndexSize);
                    writer.Write(bone.GrantRate);
                }

                // 軸固定
                bool hasFixedAxis = (bone.Flags & 0x0400) != 0;
                if (hasFixedAxis)
                {
                    WriteVector3(writer, bone.FixedAxis);
                }

                // ローカル軸
                bool hasLocalAxis = (bone.Flags & 0x0800) != 0;
                if (hasLocalAxis)
                {
                    WriteVector3(writer, bone.LocalAxisX);
                    WriteVector3(writer, bone.LocalAxisZ);
                }

                // 外部親
                bool hasExternalParent = (bone.Flags & 0x2000) != 0;
                if (hasExternalParent)
                {
                    writer.Write(bone.ExternalParentKey);
                }

                // IK
                bool hasIK = (bone.Flags & 0x0020) != 0;
                if (hasIK)
                {
                    int ikTargetIndex = bone.IKTargetIndex;
                    if (ikTargetIndex == 0 && !string.IsNullOrEmpty(bone.IKTargetBoneName))
                    {
                        ikTargetIndex = document.GetBoneIndex(bone.IKTargetBoneName);
                    }
                    WriteSignedIndex(writer, ikTargetIndex, boneIndexSize);
                    writer.Write(bone.IKLoopCount);
                    writer.Write(bone.IKLimitAngle);

                    writer.Write(bone.IKLinks.Count);
                    foreach (var link in bone.IKLinks)
                    {
                        int linkIndex = link.BoneIndex;
                        if (linkIndex == 0 && !string.IsNullOrEmpty(link.BoneName))
                        {
                            linkIndex = document.GetBoneIndex(link.BoneName);
                        }
                        WriteSignedIndex(writer, linkIndex, boneIndexSize);
                        writer.Write((byte)(link.HasLimit ? 1 : 0));
                        if (link.HasLimit)
                        {
                            WriteVector3(writer, link.LimitMin);
                            WriteVector3(writer, link.LimitMax);
                        }
                    }
                }
            }
        }

        // ================================================================
        // モーフ
        // ================================================================

        private static void WriteMorphs(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Morphs.Count);

            int vertexIndexSize = GetVertexIndexSize(document.Vertices.Count);
            int boneIndexSize = GetSignedIndexSize(document.Bones.Count);
            int materialIndexSize = GetSignedIndexSize(document.Materials.Count);
            int morphIndexSize = GetSignedIndexSize(document.Morphs.Count);
            int rigidBodyIndexSize = GetSignedIndexSize(document.RigidBodies.Count);

            foreach (var morph in document.Morphs)
            {
                WriteText(writer, morph.Name ?? "");
                WriteText(writer, morph.NameEnglish ?? "");

                writer.Write((byte)morph.Panel);
                writer.Write((byte)morph.MorphType);

                writer.Write(morph.Offsets.Count);

                foreach (var offset in morph.Offsets)
                {
                    switch (morph.MorphType)
                    {
                        case 0: // Group
                        case 9: // Flip
                            if (offset is PMXGroupMorphOffset groupOffset)
                            {
                                WriteSignedIndex(writer, groupOffset.MorphIndex, morphIndexSize);
                                writer.Write(groupOffset.Weight);
                            }
                            break;

                        case 1: // Vertex
                            if (offset is PMXVertexMorphOffset vertexOffset)
                            {
                                WriteUnsignedIndex(writer, vertexOffset.VertexIndex, vertexIndexSize);
                                WriteVector3(writer, vertexOffset.Offset);
                            }
                            break;

                        case 2: // Bone
                            if (offset is PMXBoneMorphOffset boneOffset)
                            {
                                WriteSignedIndex(writer, boneOffset.BoneIndex, boneIndexSize);
                                WriteVector3(writer, boneOffset.Translation);
                                WriteQuaternion(writer, boneOffset.Rotation);
                            }
                            break;

                        case 3:
                        case 4:
                        case 5:
                        case 6:
                        case 7: // UV
                            if (offset is PMXUVMorphOffset uvOffset)
                            {
                                WriteUnsignedIndex(writer, uvOffset.VertexIndex, vertexIndexSize);
                                WriteVector4(writer, uvOffset.Offset);
                            }
                            break;

                        case 8: // Material
                            if (offset is PMXMaterialMorphOffset matOffset)
                            {
                                WriteSignedIndex(writer, matOffset.MaterialIndex, materialIndexSize);
                                writer.Write((byte)matOffset.Operation);
                                WriteColor4(writer, matOffset.Diffuse);
                                WriteColor3(writer, matOffset.Specular);
                                writer.Write(matOffset.SpecularPower);
                                WriteColor3(writer, matOffset.Ambient);
                                WriteColor4(writer, matOffset.EdgeColor);
                                writer.Write(matOffset.EdgeSize);
                                WriteColor4(writer, matOffset.TextureCoef);
                                WriteColor4(writer, matOffset.SphereCoef);
                                WriteColor4(writer, matOffset.ToonCoef);
                            }
                            break;

                        case 10: // Impulse
                            if (offset is PMXImpulseMorphOffset impulseOffset)
                            {
                                WriteSignedIndex(writer, impulseOffset.RigidBodyIndex, rigidBodyIndexSize);
                                writer.Write((byte)(impulseOffset.IsLocal ? 1 : 0));
                                WriteVector3(writer, impulseOffset.Velocity);
                                WriteVector3(writer, impulseOffset.Torque);
                            }
                            break;
                    }
                }
            }
        }

        // ================================================================
        // 表示枠
        // ================================================================

        private static void WriteDisplayFrames(BinaryWriter writer, PMXDocument document)
        {
            // 読み込んだ表示枠がある場合はそれを書き込む
            if (document.DisplayFrames.Count > 0)
            {
                writer.Write(document.DisplayFrames.Count);

                int boneIndexSize = GetSignedIndexSize(document.Bones.Count);
                int morphIndexSize = GetSignedIndexSize(document.Morphs.Count);

                foreach (var frame in document.DisplayFrames)
                {
                    WriteText(writer, frame.Name ?? "");
                    WriteText(writer, frame.NameEnglish ?? "");
                    writer.Write((byte)(frame.IsSpecial ? 1 : 0));

                    writer.Write(frame.Elements.Count);
                    foreach (var element in frame.Elements)
                    {
                        writer.Write((byte)(element.IsMorph ? 1 : 0));
                        if (element.IsMorph)
                            WriteSignedIndex(writer, element.Index, morphIndexSize);
                        else
                            WriteSignedIndex(writer, element.Index, boneIndexSize);
                    }
                }
            }
            else
            {
                // 最小限の表示枠を出力
                writer.Write(2);  // Root + 表情

                // Root
                WriteText(writer, "Root");
                WriteText(writer, "Root");
                writer.Write((byte)1);  // 特殊枠
                writer.Write(0);  // 要素数

                // 表情
                WriteText(writer, "表情");
                WriteText(writer, "Exp");
                writer.Write((byte)1);  // 特殊枠
                writer.Write(0);  // 要素数
            }
        }

        // ================================================================
        // 剛体
        // ================================================================

        private static void WriteBodies(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.RigidBodies.Count);

            int boneIndexSize = GetSignedIndexSize(document.Bones.Count);

            foreach (var body in document.RigidBodies)
            {
                WriteText(writer, body.Name ?? "");
                WriteText(writer, body.NameEnglish ?? "");

                // ボーンインデックス（読み込み時に保存したものを使用、なければ名前から検索）
                int boneIndex = body.BoneIndex;
                if (boneIndex < 0 && !string.IsNullOrEmpty(body.RelatedBoneName))
                {
                    boneIndex = document.GetBoneIndex(body.RelatedBoneName);
                }
                WriteSignedIndex(writer, boneIndex, boneIndexSize);

                writer.Write((byte)body.Group);

                // 非衝突グループフラグ（読み込み時のushort値をそのまま使用）
                writer.Write(body.CollisionMask);

                writer.Write((byte)body.Shape);
                WriteVector3(writer, body.Size);
                WriteVector3(writer, body.Position);
                WriteVector3(writer, body.Rotation);  // ラジアンのまま（変換しない）

                writer.Write(body.Mass);
                writer.Write(body.LinearDamping);
                writer.Write(body.AngularDamping);
                writer.Write(body.Restitution);
                writer.Write(body.Friction);

                writer.Write((byte)body.PhysicsMode);
            }
        }

        // ParseNonCollisionGroupsは不要になったが、互換性のために残す
        private static ushort ParseNonCollisionGroups(string groups)
        {
            if (string.IsNullOrEmpty(groups)) return 0;

            ushort result = 0;
            foreach (var part in groups.Split(',', ' '))
            {
                if (int.TryParse(part.Trim(), out int group) && group >= 0 && group < 16)
                {
                    result |= (ushort)(1 << group);
                }
            }
            return result;
        }

        // ================================================================
        // ジョイント
        // ================================================================

        private static void WriteJoints(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Joints.Count);

            int bodyIndexSize = GetSignedIndexSize(document.RigidBodies.Count);

            foreach (var joint in document.Joints)
            {
                WriteText(writer, joint.Name ?? "");
                WriteText(writer, joint.NameEnglish ?? "");

                writer.Write((byte)joint.JointType);

                // 剛体インデックス（読み込み時に保存したものを使用、なければ名前から検索）
                int bodyAIndex = joint.RigidBodyIndexA;
                if (bodyAIndex < -1 && !string.IsNullOrEmpty(joint.BodyAName))
                {
                    bodyAIndex = GetBodyIndex(document, joint.BodyAName);
                }
                int bodyBIndex = joint.RigidBodyIndexB;
                if (bodyBIndex < -1 && !string.IsNullOrEmpty(joint.BodyBName))
                {
                    bodyBIndex = GetBodyIndex(document, joint.BodyBName);
                }
                WriteSignedIndex(writer, bodyAIndex, bodyIndexSize);
                WriteSignedIndex(writer, bodyBIndex, bodyIndexSize);

                WriteVector3(writer, joint.Position);
                WriteVector3(writer, joint.Rotation);  // ラジアンのまま（変換しない）

                WriteVector3(writer, joint.TranslationMin);
                WriteVector3(writer, joint.TranslationMax);
                WriteVector3(writer, joint.RotationMin);  // ラジアンのまま（変換しない）
                WriteVector3(writer, joint.RotationMax);  // ラジアンのまま（変換しない）

                WriteVector3(writer, joint.SpringTranslation);
                WriteVector3(writer, joint.SpringRotation);
            }
        }

        private static int GetBodyIndex(PMXDocument document, string bodyName)
        {
            for (int i = 0; i < document.RigidBodies.Count; i++)
            {
                if (document.RigidBodies[i].Name == bodyName)
                    return i;
            }
            return -1;
        }

        // ================================================================
        // ソフトボディ (PMX 2.1)
        // ================================================================

        private static void WriteSoftBodies(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.SoftBodies.Count);

            int materialIndexSize = GetSignedIndexSize(document.Materials.Count);
            int rigidBodyIndexSize = GetSignedIndexSize(document.RigidBodies.Count);
            int vertexIndexSize = GetVertexIndexSize(document.Vertices.Count);

            foreach (var body in document.SoftBodies)
            {
                WriteText(writer, body.Name ?? "");
                WriteText(writer, body.NameEnglish ?? "");

                writer.Write((byte)body.Shape);
                WriteSignedIndex(writer, body.MaterialIndex, materialIndexSize);

                writer.Write((byte)body.Group);
                writer.Write(body.CollisionMask);

                writer.Write((byte)body.Flags);
                writer.Write(body.BendingLinkDistance);
                writer.Write(body.ClusterCount);

                writer.Write(body.TotalMass);
                writer.Write(body.Margin);

                writer.Write(body.AeroModel);

                // Config
                writer.Write(body.VCF);
                writer.Write(body.DP);
                writer.Write(body.DG);
                writer.Write(body.LF);
                writer.Write(body.PR);
                writer.Write(body.VC);
                writer.Write(body.DF);
                writer.Write(body.MT);
                writer.Write(body.CHR);
                writer.Write(body.KHR);
                writer.Write(body.SHR);
                writer.Write(body.AHR);

                // Cluster
                writer.Write(body.SRHR_CL);
                writer.Write(body.SKHR_CL);
                writer.Write(body.SSHR_CL);
                writer.Write(body.SR_SPLT_CL);
                writer.Write(body.SK_SPLT_CL);
                writer.Write(body.SS_SPLT_CL);

                // Iteration
                writer.Write(body.V_IT);
                writer.Write(body.P_IT);
                writer.Write(body.D_IT);
                writer.Write(body.C_IT);

                // Material
                writer.Write(body.LST);
                writer.Write(body.AST);
                writer.Write(body.VST);

                // Anchors
                writer.Write(body.Anchors.Count);
                foreach (var anchor in body.Anchors)
                {
                    WriteSignedIndex(writer, anchor.RigidBodyIndex, rigidBodyIndexSize);
                    WriteUnsignedIndex(writer, anchor.VertexIndex, vertexIndexSize);
                    writer.Write((byte)(anchor.NearMode ? 1 : 0));
                }

                // Pin vertices
                writer.Write(body.PinnedVertices.Count);
                foreach (var vertexIndex in body.PinnedVertices)
                {
                    WriteUnsignedIndex(writer, vertexIndex, vertexIndexSize);
                }
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes;
            if (_characterEncoding == 1)
            {
                // UTF-8
                bytes = Encoding.UTF8.GetBytes(text ?? "");
            }
            else
            {
                // UTF-16 (デフォルト)
                bytes = Encoding.Unicode.GetBytes(text ?? "");
            }
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteVector2(BinaryWriter writer, Vector2 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        private static void WriteVector4(BinaryWriter writer, Vector4 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
            writer.Write(v.w);
        }

        private static void WriteQuaternion(BinaryWriter writer, Quaternion q)
        {
            writer.Write(q.x);
            writer.Write(q.y);
            writer.Write(q.z);
            writer.Write(q.w);
        }

        private static void WriteColor3(BinaryWriter writer, Color c)
        {
            writer.Write(c.r);
            writer.Write(c.g);
            writer.Write(c.b);
        }

        private static void WriteColor4(BinaryWriter writer, Color c)
        {
            writer.Write(c.r);
            writer.Write(c.g);
            writer.Write(c.b);
            writer.Write(c.a);
        }

        /// <summary>
        /// 符号なしインデックスを書き込む（頂点用）
        /// </summary>
        private static void WriteUnsignedIndex(BinaryWriter writer, int index, int indexSize)
        {
            switch (indexSize)
            {
                case 1:
                    writer.Write((byte)index);
                    break;
                case 2:
                    writer.Write((ushort)index);
                    break;
                case 4:
                    writer.Write((uint)index);
                    break;
            }
        }

        /// <summary>
        /// 符号ありインデックスを書き込む（ボーン/テクスチャ/材質/モーフ/剛体用）
        /// -1は非参照を意味する
        /// </summary>
        private static void WriteSignedIndex(BinaryWriter writer, int index, int indexSize)
        {
            switch (indexSize)
            {
                case 1:
                    writer.Write((sbyte)index);
                    break;
                case 2:
                    writer.Write((short)index);
                    break;
                case 4:
                    writer.Write(index);
                    break;
            }
        }
    }
}