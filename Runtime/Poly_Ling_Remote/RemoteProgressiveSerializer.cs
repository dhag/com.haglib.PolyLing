// Remote/RemoteProgressiveSerializer.cs
// プログレッシブ転送プロトコル シリアライザ/デシリアライザ
//
// 送受信フレーム:
//   PLRH SerializeProjectHeader / DeserializeProjectHeader
//   PLRM SerializeModelMeta     / DeserializeModelMeta
//   PLRS SerializeMeshSummary   / DeserializeMeshSummary
//   PLRD SerializeMeshData      / DeserializeMeshData

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Materials;

namespace Poly_Ling.Remote
{
    public static class RemoteProgressiveSerializer
    {
        // ================================================================
        // PLRH — ProjectHeader
        // [4B] Magic  [1B] Version  [1B] CurrentModelIndex  [2B] ModelCount
        // [string] ProjectName
        // ================================================================

        public static byte[] SerializeProjectHeader(ProjectContext project)
        {
            if (project == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.ProjectHeader);
                w.Write((byte)1);
                w.Write((byte)project.CurrentModelIndex);
                w.Write((ushort)project.ModelCount);
                WriteString(w, project.Name);
                return ms.ToArray();
            }
        }

        public static (string projectName, int modelCount, int currentModelIndex)? DeserializeProjectHeader(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.ProjectHeader) return null;
                r.ReadByte(); // version
                int currentModelIndex = r.ReadByte();
                int modelCount = r.ReadUInt16();
                string name = ReadString(r);
                return (name, modelCount, currentModelIndex);
            }
        }

        // ================================================================
        // PLRM — ModelMeta
        // [4B] Magic  [1B] Version  [1B] Padding  [2B] ModelIndex
        // [string] ModelName  [2B] MeshCount  [1B] ActiveCategory
        // [2B] SelectedCount  [int32 × SelectedCount]
        // [2B] MaterialCount  [MaterialData × MaterialCount]
        // ================================================================

        public static byte[] SerializeModelMeta(ModelContext model, int modelIndex)
        {
            if (model == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.ModelMeta);
                w.Write((byte)1);
                w.Write((byte)0); // padding
                w.Write((short)modelIndex);

                WriteString(w, model.Name);
                w.Write((ushort)model.Count);
                w.Write((byte)model.ActiveCategory);

                var sel = model.SelectedMeshIndices;
                w.Write((ushort)sel.Count);
                for (int i = 0; i < sel.Count; i++)
                    w.Write(sel[i]);

                var matRefs = model.MaterialReferences;
                w.Write((ushort)matRefs.Count);
                for (int i = 0; i < matRefs.Count; i++)
                    WriteMaterialData(w, matRefs[i]?.Data ?? new MaterialData());

                return ms.ToArray();
            }
        }

        public static (int modelIndex, ModelContext model)? DeserializeModelMeta(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.ModelMeta) return null;
                r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex = r.ReadInt16();

                string name = ReadString(r);
                ushort meshCount = r.ReadUInt16();
                byte activeCategory = r.ReadByte();

                ushort selCount = r.ReadUInt16();
                var selectedIndices = new List<int>(selCount);
                for (int i = 0; i < selCount; i++)
                    selectedIndices.Add(r.ReadInt32());

                var model = new ModelContext(name);

                // メッシュスロットをスタブで事前確保（後続PLRSで上書き）
                for (int i = 0; i < meshCount; i++)
                    model.MeshContextList.Add(new MeshContext { Name = $"Mesh{i}" });

                model.SelectedMeshIndices = selectedIndices;

                ushort matCount = r.ReadUInt16();
                var matList = new List<UnityEngine.Material>(matCount);
                for (int i = 0; i < matCount; i++)
                {
                    var (mdata, tex) = ReadMaterialData(r);
                    var mat = MaterialDataConverter.ToMaterial(mdata);
                    if (mat != null && tex != null)
                    {
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                    }
                    matList.Add(mat);
                }
                if (matList.Count > 0)
                    model.Materials = matList;

                return (modelIndex, model);
            }
        }

        // ================================================================
        // PLRS — MeshSummary
        // [4B] Magic  [1B] Version  [1B] Padding  [2B] ModelIndex  [2B] MeshIndex
        // --- メッシュメタデータ（ジオメトリなし） ---
        // [string] Name  [1B] Type  [1B] IsVisible  [1B] IsLocked
        // [2B] Depth  [2B] ParentIndex  [1B] MirrorType  [1B] MirrorAxis
        // [4B] MirrorDistance  [1B] ExcludeFromExport
        // [2B] BakedMirrorSourceIndex  [1B] HasBakedMirrorChild
        // [1B] IsMorph  (if) [string] MorphName  [4B] MorphPanel  [2B] MorphParentIndex
        // [1B] HasBoneTransform  (if) Position[12B] Rotation[12B] Scale[12B]
        // [64B] WorldMatrix  [64B] BindPose  [16B] BoneModelRotation
        // [1B] IsIK  [2B] IKTargetIndex  [2B] IKLoopCount  [4B] IKLimitAngle
        // [4B] VertexCount  [4B] FaceCount
        // ================================================================

        public static byte[] SerializeMeshSummary(MeshContext mc, int modelIndex, int meshIndex)
        {
            if (mc == null) return null;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.MeshSummary);
                w.Write((byte)1);
                w.Write((byte)0); // padding
                w.Write((short)modelIndex);
                w.Write((short)meshIndex);

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

                bool isMorph = mc.IsMorph;
                w.Write(isMorph);
                if (isMorph)
                {
                    WriteString(w, mc.MorphName);
                    w.Write(mc.MorphPanel);
                    w.Write((short)mc.MorphParentIndex);
                }

                bool hasBone = mc.BoneTransform != null;
                w.Write(hasBone);
                if (hasBone)
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

                // ジオメトリなし、サマリ用カウントのみ
                w.Write((uint)(mc.MeshObject?.VertexCount ?? 0));
                w.Write((uint)(mc.MeshObject?.FaceCount ?? 0));

                return ms.ToArray();
            }
        }

        public static (int modelIndex, int meshIndex, MeshContext mc, int vertexCount, int faceCount)? DeserializeMeshSummary(byte[] data)
        {
            if (data == null || data.Length < 10) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.MeshSummary) return null;
                r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex = r.ReadInt16();
                int meshIndex = r.ReadInt16();

                var mc = new MeshContext();
                mc.MeshObject = new MeshObject();
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

                bool hasBone = r.ReadBoolean();
                Vector3 btPos = Vector3.zero, btRot = Vector3.zero, btScale = Vector3.one;
                if (hasBone)
                {
                    btPos   = ReadVector3(r);
                    btRot   = ReadVector3(r);
                    btScale = ReadVector3(r);
                }

                mc.WorldMatrix       = ReadMatrix4x4(r);
                mc.BindPose          = ReadMatrix4x4(r);
                mc.BoneModelRotation = ReadQuaternion(r);

                mc.IsIK          = r.ReadBoolean();
                mc.IKTargetIndex = r.ReadInt16();
                mc.IKLoopCount   = r.ReadInt16();
                mc.IKLimitAngle  = r.ReadSingle();

                int vertexCount = (int)r.ReadUInt32();
                int faceCount   = (int)r.ReadUInt32();

                // BoneTransform は MeshObject 設定後に適用
                // mc.MeshObject は先頭で生成済み（Name/Type書き込み済み）なのでそのまま使う
                if (hasBone)
                {
                    mc.BoneTransform = new Poly_Ling.Data.BoneTransform
                    {
                        Position        = btPos,
                        Rotation        = btRot,
                        Scale           = btScale,
                        UseLocalTransform = true,
                    };
                }

                return (modelIndex, meshIndex, mc, vertexCount, faceCount);
            }
        }

        // ================================================================
        // PLRD — MeshData（ジオメトリ本体）
        // [Header 24B]
        //   Magic 4B, Version 1B, Padding 1B, ModelIndex 2B, MeshIndex 2B
        //   FieldFlags 4B, VertexCount 4B, FaceCount 4B, Reserved 2B
        // [Body] PLRM ボディと同一フォーマット
        // ================================================================

        public static byte[] SerializeMeshData(MeshContext mc, int modelIndex, int meshIndex, MeshFieldFlags flags)
        {
            if (mc?.MeshObject == null) return null;

            // RemoteBinarySerializer でボディを含む PLRM を生成し、ヘッダ部分(20B)を差し替える
            byte[] plrm = RemoteBinarySerializer.Serialize(mc, flags);
            if (plrm == null || plrm.Length < 20) return null;

            int bodyLen = plrm.Length - 20;
            using (var ms = new MemoryStream(24 + bodyLen))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.MeshData);                      // 0-3
                w.Write((byte)1);                                    // 4
                w.Write((byte)0);                                    // 5 padding
                w.Write((short)modelIndex);                          // 6-7
                w.Write((short)meshIndex);                           // 8-9
                w.Write((uint)flags);                                // 10-13
                w.Write((uint)(mc.MeshObject?.VertexCount ?? 0));    // 14-17
                w.Write((uint)(mc.MeshObject?.FaceCount ?? 0));      // 18-21
                w.Write((ushort)0);                                  // 22-23 reserved
                w.Write(plrm, 20, bodyLen);                          // 24..
                return ms.ToArray();
            }
        }

        public static (int modelIndex, int meshIndex, MeshObject mesh)? DeserializeMeshData(byte[] data)
        {
            if (data == null || data.Length < 24) return null;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (r.ReadUInt32() != RemoteMagic.MeshData) return null;
                r.ReadByte(); // version
                r.ReadByte(); // padding
                int modelIndex  = r.ReadInt16();
                int meshIndex   = r.ReadInt16();
                uint fieldFlags = r.ReadUInt32();
                uint vertexCount= r.ReadUInt32();
                uint faceCount  = r.ReadUInt32();
                r.ReadUInt16(); // reserved

                int bodyLen = data.Length - 24;

                // PLRM ヘッダ(20B) + ボディ を再構築して RemoteBinarySerializer に渡す
                byte[] plrm;
                using (var plrmMs = new MemoryStream(20 + bodyLen))
                using (var w = new BinaryWriter(plrmMs))
                {
                    w.Write(RemoteMagic.Mesh);                      // PLRM magic
                    w.Write((byte)1);                                // version
                    w.Write((byte)BinaryMessageType.MeshData);      // message type
                    w.Write(fieldFlags);
                    w.Write(vertexCount);
                    w.Write(faceCount);
                    w.Write((ushort)0);                              // reserved
                    w.Write(data, 24, bodyLen);
                    plrm = plrmMs.ToArray();
                }

                var mesh = RemoteBinarySerializer.Deserialize(plrm);
                return (modelIndex, meshIndex, mesh);
            }
        }

        // ================================================================
        // マテリアルシリアライズ（内部共用）
        // ================================================================

        private static void WriteMaterialData(BinaryWriter w, MaterialData d)
        {
            WriteString(w, d.Name ?? string.Empty);
            w.Write((byte)d.ShaderType);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[0] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[1] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[2] : 1f);
            w.Write(d.BaseColor.Length >= 4 ? d.BaseColor[3] : 1f);
            w.Write((byte)d.Surface);
            w.Write((byte)d.CullMode);
            w.Write(d.AlphaClipEnabled);
            w.Write(d.AlphaCutoff);
            w.Write(d.EmissionEnabled);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[0] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[1] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[2] : 0f);
            w.Write(d.EmissionColor.Length >= 4 ? d.EmissionColor[3] : 1f);

            byte[] pngData = null;
            if (!string.IsNullOrEmpty(d.BaseMapPath))
            {
                var tex = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(d.BaseMapPath);
                if (tex != null) pngData = EncodeTextureAsPNG(tex);
            }
            if (pngData == null && !string.IsNullOrEmpty(d.SourceTexturePath) && File.Exists(d.SourceTexturePath))
                pngData = File.ReadAllBytes(d.SourceTexturePath);

            if (pngData != null) { w.Write((uint)pngData.Length); w.Write(pngData); }
            else                 { w.Write((uint)0); }
        }

        private static (MaterialData data, Texture2D tex) ReadMaterialData(BinaryReader r)
        {
            var d = new MaterialData();
            d.Name       = ReadString(r);
            d.ShaderType = (ShaderType)r.ReadByte();
            d.BaseColor  = new float[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
            d.Surface    = (SurfaceType)r.ReadByte();
            d.CullMode   = (CullModeType)r.ReadByte();
            d.AlphaClipEnabled = r.ReadBoolean();
            d.AlphaCutoff      = r.ReadSingle();
            d.EmissionEnabled  = r.ReadBoolean();
            d.EmissionColor    = new float[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };

            uint texLen = r.ReadUInt32();
            Texture2D tex = null;
            if (texLen > 0)
            {
                byte[] pngData = r.ReadBytes((int)texLen);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(pngData);
                tex.name = d.Name;
            }
            return (d, tex);
        }

        private static byte[] EncodeTextureAsPNG(Texture2D src)
        {
            if (src == null) return null;
            if (src.isReadable)
            {
                var d = src.EncodeToPNG();
                return (d != null && d.Length > 0) ? d : null;
            }
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var result = readable.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(readable);
            return (result != null && result.Length > 0) ? result : null;
        }

        // ================================================================
        // プリミティブ読み書きヘルパー
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
            return System.Text.Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        private static void WriteVector3(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 ReadVector3(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteQuaternion(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        private static Quaternion ReadQuaternion(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        private static void WriteMatrix4x4(BinaryWriter w, Matrix4x4 m) { for (int i = 0; i < 16; i++) w.Write(m[i]); }
        private static Matrix4x4 ReadMatrix4x4(BinaryReader r) { var m = new Matrix4x4(); for (int i = 0; i < 16; i++) m[i] = r.ReadSingle(); return m; }
    }
}
