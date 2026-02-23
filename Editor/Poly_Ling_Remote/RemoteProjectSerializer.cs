// Remote/RemoteProjectSerializer.cs
// ProjectContext全体のバイナリシリアライザ (PLRP)
//
// ■ フォーマット
// [Header 8B]
//   Magic: 4B "PLRP"
//   Version: 1B
//   ModelCount: 2B (uint16)
//   CurrentModelIndex: 1B
//
// [Project metadata]
//   Name: string (length-prefixed UTF8)
//
// [Model × ModelCount]
//   Name: string
//   MeshCount: uint16
//   ActiveCategory: byte
//   SelectedMeshIndicesCount: uint16
//   SelectedMeshIndices[]: int32[]
//
//   [Mesh × MeshCount]
//     --- メタデータ ---
//     Name: string
//     Type: byte (MeshType)
//     IsVisible: byte
//     IsLocked: byte
//     Depth: int16
//     ParentIndex: int16
//     MirrorType: byte
//     MirrorAxis: byte
//     MirrorDistance: float
//     ExcludeFromExport: byte
//     BakedMirrorSourceIndex: int16
//     HasBakedMirrorChild: byte
//     --- モーフ ---
//     IsMorph: byte
//     (if IsMorph) MorphName: string
//     (if IsMorph) MorphPanel: int32
//     (if IsMorph) MorphParentIndex: int16
//     --- ボーン ---
//     HasBoneTransform: byte
//     (if HasBoneTransform) Position: Vector3
//     (if HasBoneTransform) Rotation: Vector3
//     (if HasBoneTransform) Scale: Vector3
//     WorldMatrix: Matrix4x4 (16 floats)
//     BindPose: Matrix4x4 (16 floats)
//     BoneModelRotation: Quaternion (4 floats)
//     IsIK: byte
//     IKTargetIndex: int16
//     IKLoopCount: int16
//     IKLimitAngle: float
//     --- メッシュデータ ---
//     MeshDataLength: uint32
//     MeshData[]: byte[] (PLRM形式、MeshFieldFlags.All)

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.Remote
{
    public static class RemoteProjectSerializer
    {
        // ================================================================
        // Serialize: ProjectContext → byte[]
        // ================================================================

        public static byte[] Serialize(ProjectContext project)
        {
            if (project == null) return null;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                // ヘッダ
                w.Write(RemoteMagic.Project);               // 4B
                w.Write((byte)1);                           // Version
                w.Write((ushort)project.ModelCount);        // ModelCount
                w.Write((byte)project.CurrentModelIndex);   // CurrentModelIndex

                // プロジェクトメタ
                WriteString(w, project.Name);

                // 各モデル
                for (int mi = 0; mi < project.ModelCount; mi++)
                {
                    var model = project.Models[mi];
                    WriteModel(w, model);
                }

                return ms.ToArray();
            }
        }

        // ================================================================
        // Deserialize: byte[] → ProjectContext
        // ================================================================

        public static ProjectContext Deserialize(byte[] data)
        {
            if (data == null || data.Length < 8) return null;

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Project)
                {
                    Debug.LogError("[RemoteProject] Invalid magic");
                    return null;
                }

                byte version = r.ReadByte();
                ushort modelCount = r.ReadUInt16();
                byte currentModelIndex = r.ReadByte();

                var project = new ProjectContext();
                project.Name = ReadString(r);

                for (int mi = 0; mi < modelCount; mi++)
                {
                    var model = ReadModel(r);
                    if (model != null)
                        project.Models.Add(model);
                }

                if (currentModelIndex < project.ModelCount)
                    project.CurrentModelIndex = currentModelIndex;

                return project;
            }
        }

        // ================================================================
        // モデル書き込み
        // ================================================================

        private static void WriteModel(BinaryWriter w, ModelContext model)
        {
            WriteString(w, model.Name);
            w.Write((ushort)model.Count);
            w.Write((byte)model.ActiveCategory);

            // 選択状態
            var sel = model.SelectedMeshIndices;
            w.Write((ushort)sel.Count);
            for (int i = 0; i < sel.Count; i++)
                w.Write(sel[i]);

            // 各メッシュ
            for (int i = 0; i < model.Count; i++)
            {
                var mc = model.MeshContextList[i];
                WriteMeshContext(w, mc);
            }
        }

        private static ModelContext ReadModel(BinaryReader r)
        {
            string name = ReadString(r);
            ushort meshCount = r.ReadUInt16();
            byte activeCategory = r.ReadByte();

            ushort selCount = r.ReadUInt16();
            var selectedIndices = new List<int>(selCount);
            for (int i = 0; i < selCount; i++)
                selectedIndices.Add(r.ReadInt32());

            var model = new ModelContext(name);

            for (int i = 0; i < meshCount; i++)
            {
                var mc = ReadMeshContext(r);
                if (mc != null)
                    model.MeshContextList.Add(mc);
            }

            // 選択状態復元
            model.SelectedMeshIndices = selectedIndices;

            return model;
        }

        // ================================================================
        // MeshContext書き込み
        // ================================================================

        private static void WriteMeshContext(BinaryWriter w, MeshContext mc)
        {
            // --- 基本属性 ---
            WriteString(w, mc.Name);
            w.Write((byte)mc.Type);
            w.Write(mc.IsVisible);
            w.Write(mc.IsLocked);
            w.Write((short)mc.Depth);
            w.Write((short)mc.ParentIndex);
            w.Write((byte)mc.MirrorType);
            w.Write((byte)mc.MirrorAxis);
            w.Write(mc.MirrorDistance);
            w.Write(mc.ExcludeFromExport);
            w.Write((short)mc.BakedMirrorSourceIndex);
            w.Write(mc.HasBakedMirrorChild);

            // --- モーフ ---
            bool isMorph = mc.IsMorph;
            w.Write(isMorph);
            if (isMorph)
            {
                WriteString(w, mc.MorphName);
                w.Write(mc.MorphPanel);
                w.Write((short)mc.MorphParentIndex);
            }

            // --- ボーン ---
            bool hasBoneTransform = mc.BoneTransform != null;
            w.Write(hasBoneTransform);
            if (hasBoneTransform)
            {
                WriteVector3(w, mc.BoneTransform.Position);
                WriteVector3(w, mc.BoneTransform.Rotation);
                WriteVector3(w, mc.BoneTransform.Scale);
            }

            WriteMatrix4x4(w, mc.WorldMatrix);
            WriteMatrix4x4(w, mc.BindPose);
            WriteQuaternion(w, mc.BoneModelRotation);

            w.Write(mc.IsIK);
            w.Write((short)mc.IKTargetIndex);
            w.Write((short)mc.IKLoopCount);
            w.Write(mc.IKLimitAngle);

            // --- メッシュデータ (PLRM) ---
            if (mc.MeshObject != null && mc.MeshObject.VertexCount > 0)
            {
                byte[] meshData = RemoteBinarySerializer.Serialize(mc.MeshObject, MeshFieldFlags.All);
                w.Write((uint)meshData.Length);
                w.Write(meshData);
            }
            else
            {
                w.Write((uint)0);
            }
        }

        private static MeshContext ReadMeshContext(BinaryReader r)
        {
            var mc = new MeshContext();

            // --- 基本属性 ---
            mc.Name = ReadString(r);
            mc.Type = (MeshType)r.ReadByte();
            mc.IsVisible = r.ReadBoolean();
            mc.IsLocked = r.ReadBoolean();
            mc.Depth = r.ReadInt16();
            mc.ParentIndex = r.ReadInt16();
            mc.MirrorType = r.ReadByte();
            mc.MirrorAxis = r.ReadByte();
            mc.MirrorDistance = r.ReadSingle();
            mc.ExcludeFromExport = r.ReadBoolean();
            mc.BakedMirrorSourceIndex = r.ReadInt16();
            mc.HasBakedMirrorChild = r.ReadBoolean();

            // --- モーフ ---
            bool isMorph = r.ReadBoolean();
            if (isMorph)
            {
                string morphName = ReadString(r);
                int morphPanel = r.ReadInt32();
                short morphParentIndex = r.ReadInt16();
                mc.SetAsMorph(morphName);
                mc.MorphPanel = morphPanel;
                mc.MorphParentIndex = morphParentIndex;
            }

            // --- ボーン ---
            bool hasBoneTransform = r.ReadBoolean();
            if (hasBoneTransform)
            {
                mc.BoneTransform = new Tools.BoneTransform();
                mc.BoneTransform.Position = ReadVector3(r);
                mc.BoneTransform.Rotation = ReadVector3(r);
                mc.BoneTransform.Scale = ReadVector3(r);
            }

            mc.WorldMatrix = ReadMatrix4x4(r);
            mc.BindPose = ReadMatrix4x4(r);
            mc.BoneModelRotation = ReadQuaternion(r);

            mc.IsIK = r.ReadBoolean();
            mc.IKTargetIndex = r.ReadInt16();
            mc.IKLoopCount = r.ReadInt16();
            mc.IKLimitAngle = r.ReadSingle();

            // --- メッシュデータ (PLRM) ---
            uint meshDataLen = r.ReadUInt32();
            if (meshDataLen > 0)
            {
                byte[] meshData = r.ReadBytes((int)meshDataLen);
                mc.MeshObject = RemoteBinarySerializer.Deserialize(meshData);
            }

            return mc;
        }

        // ================================================================
        // プリミティブ書き込みヘルパー
        // ================================================================

        private static void WriteString(BinaryWriter w, string s)
        {
            if (s == null) s = "";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
            w.Write((ushort)bytes.Length);
            w.Write(bytes);
        }

        private static string ReadString(BinaryReader r)
        {
            ushort len = r.ReadUInt16();
            if (len == 0) return "";
            byte[] bytes = r.ReadBytes(len);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        private static void WriteVector3(BinaryWriter w, Vector3 v)
        {
            w.Write(v.x); w.Write(v.y); w.Write(v.z);
        }

        private static Vector3 ReadVector3(BinaryReader r)
        {
            return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        private static void WriteQuaternion(BinaryWriter w, Quaternion q)
        {
            w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w);
        }

        private static Quaternion ReadQuaternion(BinaryReader r)
        {
            return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }

        private static void WriteMatrix4x4(BinaryWriter w, Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++)
                w.Write(m[i]);
        }

        private static Matrix4x4 ReadMatrix4x4(BinaryReader r)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                m[i] = r.ReadSingle();
            return m;
        }
    }
}
