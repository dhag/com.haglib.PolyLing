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
using UnityEditor;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Materials;

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

            // マテリアル
            var matRefs = model.MaterialReferences;
            w.Write((ushort)matRefs.Count);
            for (int i = 0; i < matRefs.Count; i++)
            {
                var data = matRefs[i]?.Data ?? new MaterialData();
                WriteMaterialData(w, data);
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

            // マテリアル
            ushort matCount = r.ReadUInt16();
            var matList = new System.Collections.Generic.List<UnityEngine.Material>(matCount);
            for (int i = 0; i < matCount; i++)
            {
                var (data, tex) = ReadMaterialData(r);
                var mat = MaterialDataConverter.ToMaterial(data);
                if (mat != null && tex != null)
                {
                    if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap",  tex);
                    if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex",  tex);
                }
                matList.Add(mat);
            }
            if (matList.Count > 0)
                model.Materials = matList;

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
            // BoneTransform setter は MeshObject != null が必要なため、
            // ここでは値を一時保持し MeshObject 設定後に適用する
            bool hasBoneTransform = r.ReadBoolean();
            Vector3 btPosition = Vector3.zero;
            Vector3 btRotation = Vector3.zero;
            Vector3 btScale = Vector3.one;
            if (hasBoneTransform)
            {
                btPosition = ReadVector3(r);
                btRotation = ReadVector3(r);
                btScale = ReadVector3(r);
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

            // meshDataLen==0 でも BoneTransform が必要な場合は空 MeshObject を生成
            if (hasBoneTransform && mc.MeshObject == null)
            {
                mc.MeshObject = new MeshObject();
            }

            // MeshObject 設定後に BoneTransform を適用
            if (hasBoneTransform)
            {
                mc.BoneTransform = new Tools.BoneTransform();
                mc.BoneTransform.Position = btPosition;
                mc.BoneTransform.Rotation = btRotation;
                mc.BoneTransform.Scale = btScale;
            }

            return mc;
        }

        // ================================================================
        // マテリアル シリアライズ
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

            // BaseMapテクスチャ (PNG inline, 0=なし)
            byte[] pngData = null;
            if (!string.IsNullOrEmpty(d.BaseMapPath))
            {
                var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(d.BaseMapPath);
                if (tex != null)
                    pngData = EncodeTextureAsPNG(tex);
            }
            // フォールバック: AssetDB外テクスチャ（MQO等）はSourceTexturePathから直接読む
            if (pngData == null && !string.IsNullOrEmpty(d.SourceTexturePath) && System.IO.File.Exists(d.SourceTexturePath))
            {
                pngData = System.IO.File.ReadAllBytes(d.SourceTexturePath);
            }
            if (pngData != null)
            {
                w.Write((uint)pngData.Length);
                w.Write(pngData);
            }
            else
            {
                w.Write((uint)0);
            }
        }

        private static (MaterialData data, Texture2D tex) ReadMaterialData(BinaryReader r)
        {
            var d = new MaterialData();
            d.Name        = ReadString(r);
            d.ShaderType  = (ShaderType)r.ReadByte();
            float r0 = r.ReadSingle(), g0 = r.ReadSingle(), b0 = r.ReadSingle(), a0 = r.ReadSingle();
            d.BaseColor   = new float[] { r0, g0, b0, a0 };
            d.Surface     = (SurfaceType)r.ReadByte();
            d.CullMode    = (CullModeType)r.ReadByte();
            d.AlphaClipEnabled = r.ReadBoolean();
            d.AlphaCutoff = r.ReadSingle();
            d.EmissionEnabled = r.ReadBoolean();
            float er = r.ReadSingle(), eg = r.ReadSingle(), eb = r.ReadSingle(), ea = r.ReadSingle();
            d.EmissionColor = new float[] { er, eg, eb, ea };

            // BaseMapテクスチャ
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

        // ================================================================
        // プリミティブ書き込みヘルパー
        // ================================================================

        /// <summary>
        /// Read/Write無効テクスチャもRenderTexture経由でPNGエンコード
        /// </summary>
        private static byte[] EncodeTextureAsPNG(Texture2D src)
        {
            if (src == null) return null;

            // isReadable なら直接エンコード
            if (src.isReadable)
            {
                var data = src.EncodeToPNG();
                return (data != null && data.Length > 0) ? data : null;
            }

            // 非Readable: RenderTexture経由でピクセル読み返し
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
