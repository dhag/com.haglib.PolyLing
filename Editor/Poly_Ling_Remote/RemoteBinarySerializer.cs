// Remote/RemoteBinarySerializer.cs
// MeshContext/MeshObject ⇔ byte[] バイナリシリアライザ
//
// BinaryWriter/BinaryReaderで直接読み書き。
// フィールドフラグで送受信するデータを選択可能。
//
// 使用例:
//   // 位置のみ送信（モーフプレビュー等）
//   byte[] data = RemoteBinarySerializer.Serialize(meshCtx, MeshFieldFlags.Positions);
//
//   // フルメッシュ受信
//   MeshObject mesh = RemoteBinarySerializer.Deserialize(data);

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    public static class RemoteBinarySerializer
    {
        // ================================================================
        // Serialize: MeshContext → byte[]
        // ================================================================

        /// <summary>
        /// MeshContextから指定フィールドをバイナリにシリアライズ
        /// </summary>
        public static byte[] Serialize(MeshContext mc, MeshFieldFlags flags)
        {
            if (mc?.MeshObject == null) return null;
            return Serialize(mc.MeshObject, flags);
        }

        /// <summary>
        /// MeshObjectから指定フィールドをバイナリにシリアライズ
        /// </summary>
        public static byte[] Serialize(MeshObject mesh, MeshFieldFlags flags)
        {
            if (mesh == null) return null;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                // ヘッダ
                w.Write(RemoteMagic.Mesh);                    // 4B
                w.Write((byte)1);                               // Version
                w.Write((byte)BinaryMessageType.MeshData);      // MessageType
                w.Write((uint)flags);                           // FieldFlags
                w.Write((uint)mesh.VertexCount);                // VertexCount
                w.Write((uint)mesh.FaceCount);                  // FaceCount
                w.Write((ushort)0);                             // Reserved

                // 頂点系フィールド
                if (flags.HasFlag(MeshFieldFlags.Positions))
                    WritePositions(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.Normals))
                    WriteNormals(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.UVs))
                    WriteUVs(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.BoneWeights))
                    WriteBoneWeights(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.VertexFlags))
                    WriteVertexFlags(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.VertexIds))
                    WriteVertexIds(w, mesh);

                // 面系フィールド
                if (flags.HasFlag(MeshFieldFlags.FaceIndices))
                    WriteFaceIndices(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.FaceMaterials))
                    WriteFaceMaterials(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.FaceFlags))
                    WriteFaceFlags(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.FaceIds))
                    WriteFaceIds(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.FaceUVIndices))
                    WriteFaceUVIndices(w, mesh);

                if (flags.HasFlag(MeshFieldFlags.FaceNormalIndices))
                    WriteFaceNormalIndices(w, mesh);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// 位置のみの軽量シリアライズ（PositionsOnlyメッセージ）
        /// </summary>
        public static byte[] SerializePositionsOnly(MeshObject mesh)
        {
            if (mesh == null) return null;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Mesh);
                w.Write((byte)1);
                w.Write((byte)BinaryMessageType.PositionsOnly);
                w.Write((uint)MeshFieldFlags.Positions);
                w.Write((uint)mesh.VertexCount);
                w.Write((uint)0); // FaceCount = 0
                w.Write((ushort)0);

                WritePositions(w, mesh);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// ファイル丸ごとのラッピング（PMX等）
        /// </summary>
        public static byte[] WrapRawFile(byte[] fileData, string extension = "")
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(RemoteMagic.Mesh);
                w.Write((byte)1);
                w.Write((byte)BinaryMessageType.RawFile);
                w.Write((uint)0); // FieldFlags unused
                w.Write((uint)fileData.Length); // VertexCountフィールドをファイルサイズに流用
                w.Write((uint)0);
                w.Write((ushort)0);

                // 拡張子（固定8バイト、パディング）
                byte[] extBytes = new byte[8];
                if (!string.IsNullOrEmpty(extension))
                {
                    byte[] src = System.Text.Encoding.ASCII.GetBytes(extension);
                    Array.Copy(src, extBytes, Math.Min(src.Length, 8));
                }
                w.Write(extBytes);

                // ファイル本体
                w.Write(fileData);

                return ms.ToArray();
            }
        }

        // ================================================================
        // Deserialize: byte[] → MeshObject
        // ================================================================

        /// <summary>
        /// バイナリデータからMeshObjectを復元
        /// 既存MeshObjectへの部分適用も可能（targetを指定）
        /// </summary>
        public static MeshObject Deserialize(byte[] data, MeshObject target = null)
        {
            if (data == null || data.Length < BinaryHeader.Size)
                return null;

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                // ヘッダ読み取り
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Mesh)
                {
                    Debug.LogError("[RemoteBinary] Invalid magic number");
                    return null;
                }

                byte version = r.ReadByte();
                var msgType = (BinaryMessageType)r.ReadByte();
                var flags = (MeshFieldFlags)r.ReadUInt32();
                uint vertexCount = r.ReadUInt32();
                uint faceCount = r.ReadUInt32();
                ushort reserved = r.ReadUInt16();

                // RawFileの場合はデシリアライズ対象外
                if (msgType == BinaryMessageType.RawFile)
                {
                    Debug.LogWarning("[RemoteBinary] RawFile type - use ExtractRawFile() instead");
                    return null;
                }

                // ターゲットMeshObject
                MeshObject mesh = target;
                if (mesh == null)
                {
                    mesh = new MeshObject();
                    // 頂点・面を事前確保
                    for (int i = 0; i < vertexCount; i++)
                        mesh.Vertices.Add(new Vertex());
                    for (int i = 0; i < faceCount; i++)
                        mesh.Faces.Add(new Face());
                }

                // 頂点系
                if (flags.HasFlag(MeshFieldFlags.Positions))
                    ReadPositions(r, mesh, vertexCount);

                if (flags.HasFlag(MeshFieldFlags.Normals))
                    ReadNormals(r, mesh, vertexCount);

                if (flags.HasFlag(MeshFieldFlags.UVs))
                    ReadUVs(r, mesh, vertexCount);

                if (flags.HasFlag(MeshFieldFlags.BoneWeights))
                    ReadBoneWeights(r, mesh, vertexCount);

                if (flags.HasFlag(MeshFieldFlags.VertexFlags))
                    ReadVertexFlags(r, mesh, vertexCount);

                if (flags.HasFlag(MeshFieldFlags.VertexIds))
                    ReadVertexIds(r, mesh, vertexCount);

                // 面系
                if (flags.HasFlag(MeshFieldFlags.FaceIndices))
                    ReadFaceIndices(r, mesh, faceCount);

                if (flags.HasFlag(MeshFieldFlags.FaceMaterials))
                    ReadFaceMaterials(r, mesh, faceCount);

                if (flags.HasFlag(MeshFieldFlags.FaceFlags))
                    ReadFaceFlags(r, mesh, faceCount);

                if (flags.HasFlag(MeshFieldFlags.FaceIds))
                    ReadFaceIds(r, mesh, faceCount);

                if (flags.HasFlag(MeshFieldFlags.FaceUVIndices))
                    ReadFaceUVIndices(r, mesh, faceCount);

                if (flags.HasFlag(MeshFieldFlags.FaceNormalIndices))
                    ReadFaceNormalIndices(r, mesh, faceCount);

                return mesh;
            }
        }

        /// <summary>
        /// RawFileメッセージからファイルデータと拡張子を抽出
        /// </summary>
        public static (byte[] fileData, string extension) ExtractRawFile(byte[] data)
        {
            if (data == null || data.Length < BinaryHeader.Size + 8)
                return (null, "");

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Mesh) return (null, "");

                r.ReadByte(); // version
                var msgType = (BinaryMessageType)r.ReadByte();
                if (msgType != BinaryMessageType.RawFile) return (null, "");

                r.ReadUInt32(); // flags
                uint fileSize = r.ReadUInt32();
                r.ReadUInt32(); // faceCount
                r.ReadUInt16(); // reserved

                byte[] extBytes = r.ReadBytes(8);
                string ext = System.Text.Encoding.ASCII.GetString(extBytes).TrimEnd('\0');

                byte[] fileData = r.ReadBytes((int)fileSize);
                return (fileData, ext);
            }
        }

        /// <summary>
        /// ヘッダのみ読み取り（フィールドフラグ確認用）
        /// </summary>
        public static BinaryHeader? ReadHeader(byte[] data)
        {
            if (data == null || data.Length < BinaryHeader.Size) return null;

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Mesh) return null;

                return new BinaryHeader
                {
                    Version = r.ReadByte(),
                    MessageType = (BinaryMessageType)r.ReadByte(),
                    FieldFlags = (MeshFieldFlags)r.ReadUInt32(),
                    VertexCount = r.ReadUInt32(),
                    FaceCount = r.ReadUInt32(),
                };
            }
        }

        // ================================================================
        // Write（頂点系）
        // ================================================================

        private static void WritePositions(BinaryWriter w, MeshObject mesh)
        {
            // Positionsプロパティ使用（キャッシュ済み配列）
            var positions = mesh.Positions;
            for (int i = 0; i < positions.Length; i++)
            {
                w.Write(positions[i].x);
                w.Write(positions[i].y);
                w.Write(positions[i].z);
            }
        }

        private static void WriteNormals(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];
                Vector3 n = (v.Normals != null && v.Normals.Count > 0)
                    ? v.Normals[0] : Vector3.up;
                w.Write(n.x);
                w.Write(n.y);
                w.Write(n.z);
            }
        }

        private static void WriteUVs(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];
                Vector2 uv = (v.UVs != null && v.UVs.Count > 0)
                    ? v.UVs[0] : Vector2.zero;
                w.Write(uv.x);
                w.Write(uv.y);
            }
        }

        private static void WriteBoneWeights(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var v = mesh.Vertices[i];
                BoneWeight bw = v.BoneWeight ?? default;
                w.Write(bw.boneIndex0);
                w.Write(bw.boneIndex1);
                w.Write(bw.boneIndex2);
                w.Write(bw.boneIndex3);
                w.Write(bw.weight0);
                w.Write(bw.weight1);
                w.Write(bw.weight2);
                w.Write(bw.weight3);
            }
        }

        private static void WriteVertexFlags(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
                w.Write((byte)mesh.Vertices[i].Flags);
        }

        private static void WriteVertexIds(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
                w.Write(mesh.Vertices[i].Id);
        }

        // ================================================================
        // Write（面系）— N角形対応で可変長
        // ================================================================

        private static void WriteFaceIndices(BinaryWriter w, MeshObject mesh)
        {
            // フォーマット: [頂点数(byte)] [index0(int)] [index1(int)] ... 
            for (int i = 0; i < mesh.FaceCount; i++)
            {
                var f = mesh.Faces[i];
                w.Write((byte)f.VertexCount);
                for (int j = 0; j < f.VertexCount; j++)
                    w.Write(f.VertexIndices[j]);
            }
        }

        private static void WriteFaceMaterials(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.FaceCount; i++)
                w.Write(mesh.Faces[i].MaterialIndex);
        }

        private static void WriteFaceFlags(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.FaceCount; i++)
                w.Write((byte)mesh.Faces[i].Flags);
        }

        private static void WriteFaceIds(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.FaceCount; i++)
                w.Write(mesh.Faces[i].Id);
        }

        private static void WriteFaceUVIndices(BinaryWriter w, MeshObject mesh)
        {
            // FaceIndicesと同じ頂点数分のUVインデックス
            for (int i = 0; i < mesh.FaceCount; i++)
            {
                var f = mesh.Faces[i];
                w.Write((byte)f.VertexCount);
                for (int j = 0; j < f.VertexCount; j++)
                {
                    int uvIdx = (f.UVIndices != null && j < f.UVIndices.Count)
                        ? f.UVIndices[j] : 0;
                    w.Write(uvIdx);
                }
            }
        }

        private static void WriteFaceNormalIndices(BinaryWriter w, MeshObject mesh)
        {
            for (int i = 0; i < mesh.FaceCount; i++)
            {
                var f = mesh.Faces[i];
                w.Write((byte)f.VertexCount);
                for (int j = 0; j < f.VertexCount; j++)
                {
                    int nIdx = (f.NormalIndices != null && j < f.NormalIndices.Count)
                        ? f.NormalIndices[j] : 0;
                    w.Write(nIdx);
                }
            }
        }

        // ================================================================
        // Read（頂点系）
        // ================================================================

        private static void ReadPositions(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                mesh.Vertices[i].Position = new Vector3(
                    r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }
            mesh.InvalidatePositionCache();
        }

        private static void ReadNormals(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                var v = mesh.Vertices[i];
                var n = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                if (v.Normals == null) v.Normals = new List<Vector3>();
                if (v.Normals.Count == 0) v.Normals.Add(n);
                else v.Normals[0] = n;
            }
        }

        private static void ReadUVs(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                var v = mesh.Vertices[i];
                var uv = new Vector2(r.ReadSingle(), r.ReadSingle());
                if (v.UVs == null) v.UVs = new List<Vector2>();
                if (v.UVs.Count == 0) v.UVs.Add(uv);
                else v.UVs[0] = uv;
            }
        }

        private static void ReadBoneWeights(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                var bw = new BoneWeight
                {
                    boneIndex0 = r.ReadInt32(),
                    boneIndex1 = r.ReadInt32(),
                    boneIndex2 = r.ReadInt32(),
                    boneIndex3 = r.ReadInt32(),
                    weight0 = r.ReadSingle(),
                    weight1 = r.ReadSingle(),
                    weight2 = r.ReadSingle(),
                    weight3 = r.ReadSingle(),
                };
                mesh.Vertices[i].BoneWeight = bw;
            }
        }

        private static void ReadVertexFlags(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
                mesh.Vertices[i].Flags = (VertexFlags)r.ReadByte();
        }

        private static void ReadVertexIds(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureVertexCount(mesh, count);
            for (int i = 0; i < count; i++)
                mesh.Vertices[i].Id = r.ReadInt32();
        }

        // ================================================================
        // Read（面系）
        // ================================================================

        private static void ReadFaceIndices(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                int vertCount = r.ReadByte();
                var f = mesh.Faces[i];
                f.VertexIndices.Clear();
                for (int j = 0; j < vertCount; j++)
                    f.VertexIndices.Add(r.ReadInt32());
            }
        }

        private static void ReadFaceMaterials(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
                mesh.Faces[i].MaterialIndex = r.ReadInt32();
        }

        private static void ReadFaceFlags(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
                mesh.Faces[i].Flags = (Data.FaceFlags)r.ReadByte();
        }

        private static void ReadFaceIds(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
                mesh.Faces[i].Id = r.ReadInt32();
        }

        private static void ReadFaceUVIndices(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                int vertCount = r.ReadByte();
                var f = mesh.Faces[i];
                f.UVIndices.Clear();
                for (int j = 0; j < vertCount; j++)
                    f.UVIndices.Add(r.ReadInt32());
            }
        }

        private static void ReadFaceNormalIndices(BinaryReader r, MeshObject mesh, uint count)
        {
            EnsureFaceCount(mesh, count);
            for (int i = 0; i < count; i++)
            {
                int vertCount = r.ReadByte();
                var f = mesh.Faces[i];
                f.NormalIndices.Clear();
                for (int j = 0; j < vertCount; j++)
                    f.NormalIndices.Add(r.ReadInt32());
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static void EnsureVertexCount(MeshObject mesh, uint count)
        {
            while (mesh.Vertices.Count < count)
                mesh.Vertices.Add(new Vertex());
        }

        private static void EnsureFaceCount(MeshObject mesh, uint count)
        {
            while (mesh.Faces.Count < count)
                mesh.Faces.Add(new Face());
        }
    }
}
