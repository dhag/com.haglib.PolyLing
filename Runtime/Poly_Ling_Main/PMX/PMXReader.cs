// PMXReader.cs - PMXバイナリ読み込み
// BinaryReaderを直接使用、PMXDocumentへ出力

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXファイルリーダー（バイナリ）
    /// </summary>
    public static class PMXReader
    {
        // インデックスサイズ（ヘッダーから読み取り）
        private static int _vertexIndexSize;
        private static int _textureIndexSize;
        private static int _materialIndexSize;
        private static int _boneIndexSize;
        private static int _morphIndexSize;
        private static int _rigidBodyIndexSize;

        /// <summary>
        /// PMXファイルを読み込む
        /// </summary>
        public static PMXDocument Load(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                var doc = new PMXDocument
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath)
                };

                // マジックナンバー "PMX "
                var magic = reader.ReadBytes(4);
                var magicStr = Encoding.ASCII.GetString(magic);
                if (magicStr != "PMX ")
                    throw new InvalidDataException($"Invalid PMX magic number: {magicStr}");

                // ヘッダー
                ReadHeader(reader, doc);

                // モデル情報
                ReadModelInfo(reader, doc);

                // 頂点
                int vertexCount = reader.ReadInt32();
                for (int i = 0; i < vertexCount; i++)
                    doc.Vertices.Add(ReadVertex(reader, doc, i));

                // 面
                int indexCount = reader.ReadInt32();
                int faceCount = indexCount / 3;
                for (int i = 0; i < faceCount; i++)
                    doc.Faces.Add(ReadFace(reader, i));

                // テクスチャ
                int textureCount = reader.ReadInt32();
                for (int i = 0; i < textureCount; i++)
                    doc.TexturePaths.Add(ReadText(reader, doc.CharacterEncoding));

                // マテリアル
                int materialCount = reader.ReadInt32();
                int faceOffset = 0;
                for (int i = 0; i < materialCount; i++)
                {
                    var mat = ReadMaterial(reader, doc);
                    doc.Materials.Add(mat);

                    // 面にマテリアル情報を設定
                    for (int j = 0; j < mat.FaceCount && faceOffset + j < doc.Faces.Count; j++)
                    {
                        doc.Faces[faceOffset + j].MaterialIndex = i;
                        doc.Faces[faceOffset + j].MaterialName = mat.Name;
                    }
                    faceOffset += mat.FaceCount;
                }

                // ボーン
                int boneCount = reader.ReadInt32();
                for (int i = 0; i < boneCount; i++)
                    doc.Bones.Add(ReadBone(reader, doc));

                // ボーン名を解決
                ResolveBoneNames(doc);

                // モーフ
                int morphCount = reader.ReadInt32();
                for (int i = 0; i < morphCount; i++)
                    doc.Morphs.Add(ReadMorph(reader, doc));

                // 表示枠
                int frameCount = reader.ReadInt32();
                for (int i = 0; i < frameCount; i++)
                    doc.DisplayFrames.Add(ReadDisplayFrame(reader, doc));

                // 剛体
                int rigidBodyCount = reader.ReadInt32();
                for (int i = 0; i < rigidBodyCount; i++)
                    doc.RigidBodies.Add(ReadRigidBody(reader, doc));

                // 剛体のボーン名を解決
                ResolveRigidBodyNames(doc);

                // ジョイント
                int jointCount = reader.ReadInt32();
                for (int i = 0; i < jointCount; i++)
                    doc.Joints.Add(ReadJoint(reader, doc));

                // ソフトボディ（PMX 2.1以降）
                if (doc.Version >= 2.1f && fs.Position < fs.Length)
                {
                    int softBodyCount = reader.ReadInt32();
                    for (int i = 0; i < softBodyCount; i++)
                        doc.SoftBodies.Add(ReadSoftBody(reader, doc));
                }

                return doc;
            }
        }

        #region Header & ModelInfo

        private static void ReadHeader(BinaryReader reader, PMXDocument doc)
        {
            doc.Version = reader.ReadSingle();

            int dataSize = reader.ReadByte();
            if (dataSize != 8)
                throw new InvalidDataException($"Unexpected header data size: {dataSize}");

            byte[] data = reader.ReadBytes(8);
            doc.CharacterEncoding = data[0];  // 0:UTF16, 1:UTF8
            doc.AdditionalUVCount = data[1];
            _vertexIndexSize = data[2];
            _textureIndexSize = data[3];
            _materialIndexSize = data[4];
            _boneIndexSize = data[5];
            _morphIndexSize = data[6];
            _rigidBodyIndexSize = data[7];
        }

        private static void ReadModelInfo(BinaryReader reader, PMXDocument doc)
        {
            doc.ModelInfo.Name = ReadText(reader, doc.CharacterEncoding);
            doc.ModelInfo.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            doc.ModelInfo.Comment = ReadText(reader, doc.CharacterEncoding);
            doc.ModelInfo.CommentEnglish = ReadText(reader, doc.CharacterEncoding);
        }

        private static void ResolveBoneNames(PMXDocument doc)
        {
            foreach (var bone in doc.Bones)
            {
                if (bone.ParentIndex >= 0 && bone.ParentIndex < doc.Bones.Count)
                    bone.ParentBoneName = doc.Bones[bone.ParentIndex].Name;

                if (bone.ConnectBoneIndex >= 0 && bone.ConnectBoneIndex < doc.Bones.Count)
                    bone.ConnectBoneName = doc.Bones[bone.ConnectBoneIndex].Name;

                if (bone.GrantParentIndex >= 0 && bone.GrantParentIndex < doc.Bones.Count)
                    bone.GrantParentBoneName = doc.Bones[bone.GrantParentIndex].Name;

                if (bone.IKTargetIndex >= 0 && bone.IKTargetIndex < doc.Bones.Count)
                    bone.IKTargetBoneName = doc.Bones[bone.IKTargetIndex].Name;

                foreach (var link in bone.IKLinks)
                {
                    if (link.BoneIndex >= 0 && link.BoneIndex < doc.Bones.Count)
                        link.BoneName = doc.Bones[link.BoneIndex].Name;
                }
            }

            foreach (var vertex in doc.Vertices)
            {
                if (vertex.BoneWeights == null) continue;
                foreach (var bw in vertex.BoneWeights)
                {
                    if (bw.BoneIndex >= 0 && bw.BoneIndex < doc.Bones.Count)
                        bw.BoneName = doc.Bones[bw.BoneIndex].Name;
                }
            }
        }

        private static void ResolveRigidBodyNames(PMXDocument doc)
        {
            foreach (var body in doc.RigidBodies)
            {
                if (body.BoneIndex >= 0 && body.BoneIndex < doc.Bones.Count)
                    body.RelatedBoneName = doc.Bones[body.BoneIndex].Name;
            }

            foreach (var joint in doc.Joints)
            {
                if (joint.RigidBodyIndexA >= 0 && joint.RigidBodyIndexA < doc.RigidBodies.Count)
                    joint.BodyAName = doc.RigidBodies[joint.RigidBodyIndexA].Name;
                if (joint.RigidBodyIndexB >= 0 && joint.RigidBodyIndexB < doc.RigidBodies.Count)
                    joint.BodyBName = doc.RigidBodies[joint.RigidBodyIndexB].Name;
            }
        }

        #endregion

        #region Vertex & Face

        private static PMXVertex ReadVertex(BinaryReader reader, PMXDocument doc, int index)
        {
            var vertex = new PMXVertex { Index = index };

            vertex.Position = ReadVector3(reader);
            vertex.Normal = ReadVector3(reader);
            vertex.UV = ReadVector2(reader);

            vertex.AdditionalUVs = new Vector4[doc.AdditionalUVCount];
            for (int i = 0; i < doc.AdditionalUVCount; i++)
                vertex.AdditionalUVs[i] = ReadVector4(reader);

            vertex.WeightType = reader.ReadByte();
            var boneWeights = new List<PMXBoneWeight>();

            switch (vertex.WeightType)
            {
                case 0: // BDEF1
                    boneWeights.Add(new PMXBoneWeight { BoneIndex = ReadIndex(reader, _boneIndexSize), Weight = 1.0f });
                    break;
                case 1: // BDEF2
                    {
                        int b0 = ReadIndex(reader, _boneIndexSize);
                        int b1 = ReadIndex(reader, _boneIndexSize);
                        float w0 = reader.ReadSingle();
                        boneWeights.Add(new PMXBoneWeight { BoneIndex = b0, Weight = w0 });
                        boneWeights.Add(new PMXBoneWeight { BoneIndex = b1, Weight = 1.0f - w0 });
                    }
                    break;
                case 2: // BDEF4
                case 4: // QDEF
                    {
                        int[] bones = new int[4];
                        float[] weights = new float[4];
                        for (int i = 0; i < 4; i++) bones[i] = ReadIndex(reader, _boneIndexSize);
                        for (int i = 0; i < 4; i++) weights[i] = reader.ReadSingle();
                        for (int i = 0; i < 4; i++) boneWeights.Add(new PMXBoneWeight { BoneIndex = bones[i], Weight = weights[i] });
                    }
                    break;
                case 3: // SDEF
                    {
                        int b0 = ReadIndex(reader, _boneIndexSize);
                        int b1 = ReadIndex(reader, _boneIndexSize);
                        float w0 = reader.ReadSingle();
                        boneWeights.Add(new PMXBoneWeight { BoneIndex = b0, Weight = w0 });
                        boneWeights.Add(new PMXBoneWeight { BoneIndex = b1, Weight = 1.0f - w0 });
                        vertex.SDEF_C = ReadVector3(reader);
                        vertex.SDEF_R0 = ReadVector3(reader);
                        vertex.SDEF_R1 = ReadVector3(reader);
                    }
                    break;
            }

            vertex.BoneWeights = boneWeights.ToArray();
            vertex.EdgeScale = reader.ReadSingle();

            return vertex;
        }

        private static PMXFace ReadFace(BinaryReader reader, int index)
        {
            return new PMXFace
            {
                FaceIndex = index,
                VertexIndex1 = ReadUnsignedIndex(reader, _vertexIndexSize),
                VertexIndex2 = ReadUnsignedIndex(reader, _vertexIndexSize),
                VertexIndex3 = ReadUnsignedIndex(reader, _vertexIndexSize)
            };
        }

        #endregion

        #region Material

        private static PMXMaterial ReadMaterial(BinaryReader reader, PMXDocument doc)
        {
            var mat = new PMXMaterial();

            mat.Name = ReadText(reader, doc.CharacterEncoding);
            mat.NameEnglish = ReadText(reader, doc.CharacterEncoding);

            mat.Diffuse = ReadColor4(reader);
            mat.Specular = ReadColor3(reader);
            mat.SpecularPower = reader.ReadSingle();
            mat.Ambient = ReadColor3(reader);

            mat.DrawFlags = reader.ReadByte();
            mat.EdgeColor = ReadColor4(reader);
            mat.EdgeSize = reader.ReadSingle();

            mat.TextureIndex = ReadIndex(reader, _textureIndexSize);
            mat.SphereTextureIndex = ReadIndex(reader, _textureIndexSize);
            mat.SphereMode = reader.ReadByte();

            mat.SharedToon = reader.ReadByte() == 1;
            if (mat.SharedToon)
                mat.ToonTextureIndex = reader.ReadByte();
            else
                mat.ToonTextureIndex = ReadIndex(reader, _textureIndexSize);

            mat.Memo = ReadText(reader, doc.CharacterEncoding);
            mat.FaceCount = reader.ReadInt32() / 3;

            // テクスチャパスを解決
            if (mat.TextureIndex >= 0 && mat.TextureIndex < doc.TexturePaths.Count)
                mat.TexturePath = doc.TexturePaths[mat.TextureIndex];
            if (mat.SphereTextureIndex >= 0 && mat.SphereTextureIndex < doc.TexturePaths.Count)
                mat.SphereTexturePath = doc.TexturePaths[mat.SphereTextureIndex];

            return mat;
        }

        #endregion

        #region Bone

        private static PMXBone ReadBone(BinaryReader reader, PMXDocument doc)
        {
            var bone = new PMXBone();

            bone.Name = ReadText(reader, doc.CharacterEncoding);
            bone.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            bone.Position = ReadVector3(reader);
            bone.ParentIndex = ReadIndex(reader, _boneIndexSize);
            bone.TransformLevel = reader.ReadInt32();
            bone.Flags = reader.ReadUInt16();

            bool connected = (bone.Flags & 0x0001) != 0;
            if (connected)
                bone.ConnectBoneIndex = ReadIndex(reader, _boneIndexSize);
            else
                bone.ConnectOffset = ReadVector3(reader);

            bool hasGrant = (bone.Flags & 0x0100) != 0 || (bone.Flags & 0x0200) != 0;
            if (hasGrant)
            {
                bone.GrantParentIndex = ReadIndex(reader, _boneIndexSize);
                bone.GrantRate = reader.ReadSingle();
            }

            if ((bone.Flags & 0x0400) != 0)
                bone.FixedAxis = ReadVector3(reader);

            if ((bone.Flags & 0x0800) != 0)
            {
                bone.LocalAxisX = ReadVector3(reader);
                bone.LocalAxisZ = ReadVector3(reader);
            }

            if ((bone.Flags & 0x2000) != 0)
                bone.ExternalParentKey = reader.ReadInt32();

            if ((bone.Flags & 0x0020) != 0)
            {
                bone.IKTargetIndex = ReadIndex(reader, _boneIndexSize);
                bone.IKLoopCount = reader.ReadInt32();
                bone.IKLimitAngle = reader.ReadSingle();

                int linkCount = reader.ReadInt32();
                for (int i = 0; i < linkCount; i++)
                {
                    var link = new PMXIKLink();
                    link.BoneIndex = ReadIndex(reader, _boneIndexSize);
                    link.HasLimit = reader.ReadByte() == 1;
                    if (link.HasLimit)
                    {
                        link.LimitMin = ReadVector3(reader);
                        link.LimitMax = ReadVector3(reader);
                    }
                    bone.IKLinks.Add(link);
                }
            }

            return bone;
        }

        #endregion

        #region Morph

        private static PMXMorph ReadMorph(BinaryReader reader, PMXDocument doc)
        {
            var morph = new PMXMorph();

            morph.Name = ReadText(reader, doc.CharacterEncoding);
            morph.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            morph.Panel = reader.ReadByte();
            morph.MorphType = reader.ReadByte();

            int offsetCount = reader.ReadInt32();
            for (int i = 0; i < offsetCount; i++)
            {
                PMXMorphOffset offset = null;

                switch (morph.MorphType)
                {
                    case 0: // Group
                    case 9: // Flip
                        offset = new PMXGroupMorphOffset
                        {
                            Type = morph.MorphType,
                            MorphIndex = ReadIndex(reader, _morphIndexSize),
                            Weight = reader.ReadSingle()
                        };
                        break;
                    case 1: // Vertex
                        offset = new PMXVertexMorphOffset
                        {
                            Type = 1,
                            VertexIndex = ReadUnsignedIndex(reader, _vertexIndexSize),
                            Offset = ReadVector3(reader)
                        };
                        break;
                    case 2: // Bone
                        offset = new PMXBoneMorphOffset
                        {
                            Type = 2,
                            BoneIndex = ReadIndex(reader, _boneIndexSize),
                            Translation = ReadVector3(reader),
                            Rotation = ReadQuaternion(reader)
                        };
                        break;
                    case 3:
                    case 4:
                    case 5:
                    case 6:
                    case 7: // UV
                        offset = new PMXUVMorphOffset
                        {
                            Type = morph.MorphType,
                            VertexIndex = ReadUnsignedIndex(reader, _vertexIndexSize),
                            Offset = ReadVector4(reader)
                        };
                        break;
                    case 8: // Material
                        offset = new PMXMaterialMorphOffset
                        {
                            Type = 8,
                            MaterialIndex = ReadIndex(reader, _materialIndexSize),
                            Operation = reader.ReadByte(),
                            Diffuse = ReadColor4(reader),
                            Specular = ReadColor3(reader),
                            SpecularPower = reader.ReadSingle(),
                            Ambient = ReadColor3(reader),
                            EdgeColor = ReadColor4(reader),
                            EdgeSize = reader.ReadSingle(),
                            TextureCoef = ReadColor4(reader),
                            SphereCoef = ReadColor4(reader),
                            ToonCoef = ReadColor4(reader)
                        };
                        break;
                    case 10: // Impulse
                        offset = new PMXImpulseMorphOffset
                        {
                            Type = 10,
                            RigidBodyIndex = ReadIndex(reader, _rigidBodyIndexSize),
                            IsLocal = reader.ReadByte() == 1,
                            Velocity = ReadVector3(reader),
                            Torque = ReadVector3(reader)
                        };
                        break;
                    default:
                        offset = new PMXMorphOffset { Type = morph.MorphType };
                        break;
                }

                if (offset != null)
                    morph.Offsets.Add(offset);
            }

            return morph;
        }

        #endregion

        #region DisplayFrame

        private static PMXDisplayFrame ReadDisplayFrame(BinaryReader reader, PMXDocument doc)
        {
            var frame = new PMXDisplayFrame();

            frame.Name = ReadText(reader, doc.CharacterEncoding);
            frame.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            frame.IsSpecial = reader.ReadByte() == 1;

            int elementCount = reader.ReadInt32();
            for (int i = 0; i < elementCount; i++)
            {
                var element = new PMXDisplayElement();
                element.IsMorph = reader.ReadByte() == 1;
                element.Index = element.IsMorph
                    ? ReadIndex(reader, _morphIndexSize)
                    : ReadIndex(reader, _boneIndexSize);
                frame.Elements.Add(element);
            }

            return frame;
        }

        #endregion

        #region RigidBody & Joint

        private static PMXRigidBody ReadRigidBody(BinaryReader reader, PMXDocument doc)
        {
            var body = new PMXRigidBody();

            body.Name = ReadText(reader, doc.CharacterEncoding);
            body.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            body.BoneIndex = ReadIndex(reader, _boneIndexSize);
            body.Group = reader.ReadByte();
            body.CollisionMask = reader.ReadUInt16();
            body.Shape = reader.ReadByte();
            body.Size = ReadVector3(reader);
            body.Position = ReadVector3(reader);
            body.Rotation = ReadVector3(reader);
            body.Mass = reader.ReadSingle();
            body.LinearDamping = reader.ReadSingle();
            body.AngularDamping = reader.ReadSingle();
            body.Restitution = reader.ReadSingle();
            body.Friction = reader.ReadSingle();
            body.PhysicsMode = reader.ReadByte();

            return body;
        }

        private static PMXJoint ReadJoint(BinaryReader reader, PMXDocument doc)
        {
            var joint = new PMXJoint();

            joint.Name = ReadText(reader, doc.CharacterEncoding);
            joint.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            joint.JointType = reader.ReadByte();
            joint.RigidBodyIndexA = ReadIndex(reader, _rigidBodyIndexSize);
            joint.RigidBodyIndexB = ReadIndex(reader, _rigidBodyIndexSize);
            joint.Position = ReadVector3(reader);
            joint.Rotation = ReadVector3(reader);
            joint.TranslationMin = ReadVector3(reader);
            joint.TranslationMax = ReadVector3(reader);
            joint.RotationMin = ReadVector3(reader);
            joint.RotationMax = ReadVector3(reader);
            joint.SpringTranslation = ReadVector3(reader);
            joint.SpringRotation = ReadVector3(reader);

            return joint;
        }

        #endregion

        #region SoftBody (PMX 2.1)

        private static PMXSoftBody ReadSoftBody(BinaryReader reader, PMXDocument doc)
        {
            var body = new PMXSoftBody();

            body.Name = ReadText(reader, doc.CharacterEncoding);
            body.NameEnglish = ReadText(reader, doc.CharacterEncoding);
            body.Shape = reader.ReadByte();
            body.MaterialIndex = ReadIndex(reader, _materialIndexSize);
            body.Group = reader.ReadByte();
            body.CollisionMask = reader.ReadUInt16();
            body.Flags = reader.ReadByte();
            body.BendingLinkDistance = reader.ReadInt32();
            body.ClusterCount = reader.ReadInt32();
            body.TotalMass = reader.ReadSingle();
            body.Margin = reader.ReadSingle();

            body.AeroModel = reader.ReadInt32();
            body.VCF = reader.ReadSingle(); body.DP = reader.ReadSingle();
            body.DG = reader.ReadSingle(); body.LF = reader.ReadSingle();
            body.PR = reader.ReadSingle(); body.VC = reader.ReadSingle();
            body.DF = reader.ReadSingle(); body.MT = reader.ReadSingle();
            body.CHR = reader.ReadSingle(); body.KHR = reader.ReadSingle();
            body.SHR = reader.ReadSingle(); body.AHR = reader.ReadSingle();
            body.SRHR_CL = reader.ReadSingle(); body.SKHR_CL = reader.ReadSingle();
            body.SSHR_CL = reader.ReadSingle(); body.SR_SPLT_CL = reader.ReadSingle();
            body.SK_SPLT_CL = reader.ReadSingle(); body.SS_SPLT_CL = reader.ReadSingle();
            body.V_IT = reader.ReadInt32(); body.P_IT = reader.ReadInt32();
            body.D_IT = reader.ReadInt32(); body.C_IT = reader.ReadInt32();
            body.LST = reader.ReadSingle(); body.AST = reader.ReadSingle();
            body.VST = reader.ReadSingle();

            int anchorCount = reader.ReadInt32();
            for (int i = 0; i < anchorCount; i++)
            {
                body.Anchors.Add(new PMXSoftBodyAnchor
                {
                    RigidBodyIndex = ReadIndex(reader, _rigidBodyIndexSize),
                    VertexIndex = ReadIndex(reader, _vertexIndexSize),
                    NearMode = reader.ReadByte() == 1
                });
            }

            int pinCount = reader.ReadInt32();
            for (int i = 0; i < pinCount; i++)
                body.PinnedVertices.Add(ReadIndex(reader, _vertexIndexSize));

            return body;
        }

        #endregion

        #region Primitive Readers

        private static string ReadText(BinaryReader reader, int encoding)
        {
            int length = reader.ReadInt32();
            if (length == 0) return string.Empty;

            byte[] bytes = reader.ReadBytes(length);
            return encoding == 1
                ? Encoding.UTF8.GetString(bytes)
                : Encoding.Unicode.GetString(bytes);
        }

        private static int ReadIndex(BinaryReader reader, int size)
        {
            switch (size)
            {
                case 1: sbyte b = reader.ReadSByte(); return b == -1 ? -1 : b;
                case 2: short s = reader.ReadInt16(); return s == -1 ? -1 : s;
                case 4: return reader.ReadInt32();
                default: throw new InvalidDataException($"Invalid index size: {size}");
            }
        }

        private static int ReadUnsignedIndex(BinaryReader reader, int size)
        {
            switch (size)
            {
                case 1: return reader.ReadByte();
                case 2: return reader.ReadUInt16();
                case 4: return (int)reader.ReadUInt32();
                default: throw new InvalidDataException($"Invalid index size: {size}");
            }
        }

        private static Vector2 ReadVector2(BinaryReader reader) =>
            new Vector2(reader.ReadSingle(), reader.ReadSingle());

        private static Vector3 ReadVector3(BinaryReader reader) =>
            new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static Vector4 ReadVector4(BinaryReader reader) =>
            new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static Quaternion ReadQuaternion(BinaryReader reader) =>
            new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static Color ReadColor3(BinaryReader reader) =>
            new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 1.0f);

        private static Color ReadColor4(BinaryReader reader) =>
            new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        #endregion
    }
}