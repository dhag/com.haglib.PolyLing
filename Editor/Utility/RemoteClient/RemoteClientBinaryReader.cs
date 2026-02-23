// RemoteClient/RemoteClientBinaryReader.cs
// PLRMバイナリフォーマットのパーサー（PolyLing.Data非依存）
// ヘッダ読み取り、フィールド別データ抽出を提供

using System;
using System.IO;

namespace PolyLingRemoteClient
{
    // ================================================================
    // 共通マジック定数（サーバー側RemoteMagicと同一値）
    // ================================================================

    public static class RemoteMagic
    {
        public const uint Mesh    = 0x4D524C50; // "PLRM"
        public const uint Image   = 0x49524C50; // "PLRI"
        public const uint Model   = 0x44524C50; // "PLRD"
        public const uint Project = 0x50524C50; // "PLRP"

        public static uint Read(byte[] data)
        {
            if (data == null || data.Length < 4) return 0;
            return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        }
    }

    // ================================================================
    // プロトコル定義（サーバー側と同一値）
    // ================================================================

    public enum BinaryMessageType : byte
    {
        MeshData = 0,
        PositionsOnly = 1,
        RawFile = 2,
    }

    [Flags]
    public enum MeshFieldFlags : uint
    {
        None           = 0,
        Positions      = 0x0001,
        Normals        = 0x0002,
        UVs            = 0x0004,
        BoneWeights    = 0x0008,
        VertexFlags    = 0x0010,
        VertexIds      = 0x0020,
        FaceIndices    = 0x0100,
        FaceMaterials  = 0x0200,
        FaceFlags      = 0x0400,
        FaceIds        = 0x0800,
        FaceUVIndices  = 0x1000,
        FaceNormalIndices = 0x2000,

        VertexBasic    = Positions | Normals | UVs,
        AllVertex      = Positions | Normals | UVs | BoneWeights | VertexFlags | VertexIds,
        AllFace        = FaceIndices | FaceMaterials | FaceFlags | FaceIds | FaceUVIndices | FaceNormalIndices,
        All            = AllVertex | AllFace,
    }

    // ================================================================
    // ヘッダ
    // ================================================================

    public struct BinaryHeader
    {
        public const int Size = 20;

        public byte Version;
        public BinaryMessageType MessageType;
        public MeshFieldFlags FieldFlags;
        public uint VertexCount;
        public uint FaceCount;

        public bool HasField(MeshFieldFlags flag) => (FieldFlags & flag) != 0;
    }

    // ================================================================
    // パース済みメッシュデータ（軽量構造体）
    // ================================================================

    /// <summary>
    /// パース結果。必要なフィールドだけnon-null。
    /// </summary>
    public class ParsedMeshData
    {
        public BinaryHeader Header;

        // 頂点系
        public float[] Positions;       // [x,y,z, x,y,z, ...] length = VertexCount*3
        public float[] Normals;         // 同上
        public float[] UVs;             // [u,v, u,v, ...] length = VertexCount*2
        public int[] BoneIndices;       // [i0,i1,i2,i3, ...] length = VertexCount*4
        public float[] BoneWeightValues;// [w0,w1,w2,w3, ...] length = VertexCount*4
        public byte[] VertexFlags;
        public int[] VertexIds;

        // 面系
        public FaceData[] Faces;
        public int[] FaceMaterialIndices;
        public byte[] FaceFlags;
        public int[] FaceIds;
    }

    /// <summary>
    /// 1面分のデータ
    /// </summary>
    public struct FaceData
    {
        public int[] VertexIndices;
        public int[] UVIndices;
        public int[] NormalIndices;
    }

    // ================================================================
    // パーサー
    // ================================================================

    public static class RemoteClientBinaryReader
    {
        /// <summary>
        /// ヘッダのみ読み取り
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
                    // reserved 2B
                };
            }
        }

        /// <summary>
        /// 全フィールドをパース
        /// </summary>
        public static ParsedMeshData Parse(byte[] data)
        {
            if (data == null || data.Length < BinaryHeader.Size) return null;

            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                // ヘッダ
                uint magic = r.ReadUInt32();
                if (magic != RemoteMagic.Mesh) return null;

                var result = new ParsedMeshData();
                result.Header.Version = r.ReadByte();
                result.Header.MessageType = (BinaryMessageType)r.ReadByte();
                result.Header.FieldFlags = (MeshFieldFlags)r.ReadUInt32();
                result.Header.VertexCount = r.ReadUInt32();
                result.Header.FaceCount = r.ReadUInt32();
                r.ReadUInt16(); // reserved

                var flags = result.Header.FieldFlags;
                uint vc = result.Header.VertexCount;
                uint fc = result.Header.FaceCount;

                // --- 頂点系 ---
                if (flags.HasFlag(MeshFieldFlags.Positions))
                {
                    result.Positions = new float[vc * 3];
                    for (int i = 0; i < vc * 3; i++)
                        result.Positions[i] = r.ReadSingle();
                }

                if (flags.HasFlag(MeshFieldFlags.Normals))
                {
                    result.Normals = new float[vc * 3];
                    for (int i = 0; i < vc * 3; i++)
                        result.Normals[i] = r.ReadSingle();
                }

                if (flags.HasFlag(MeshFieldFlags.UVs))
                {
                    result.UVs = new float[vc * 2];
                    for (int i = 0; i < vc * 2; i++)
                        result.UVs[i] = r.ReadSingle();
                }

                if (flags.HasFlag(MeshFieldFlags.BoneWeights))
                {
                    result.BoneIndices = new int[vc * 4];
                    result.BoneWeightValues = new float[vc * 4];
                    for (int i = 0; i < vc; i++)
                    {
                        result.BoneIndices[i * 4 + 0] = r.ReadInt32();
                        result.BoneIndices[i * 4 + 1] = r.ReadInt32();
                        result.BoneIndices[i * 4 + 2] = r.ReadInt32();
                        result.BoneIndices[i * 4 + 3] = r.ReadInt32();
                        result.BoneWeightValues[i * 4 + 0] = r.ReadSingle();
                        result.BoneWeightValues[i * 4 + 1] = r.ReadSingle();
                        result.BoneWeightValues[i * 4 + 2] = r.ReadSingle();
                        result.BoneWeightValues[i * 4 + 3] = r.ReadSingle();
                    }
                }

                if (flags.HasFlag(MeshFieldFlags.VertexFlags))
                {
                    result.VertexFlags = r.ReadBytes((int)vc);
                }

                if (flags.HasFlag(MeshFieldFlags.VertexIds))
                {
                    result.VertexIds = new int[vc];
                    for (int i = 0; i < vc; i++)
                        result.VertexIds[i] = r.ReadInt32();
                }

                // --- 面系 ---
                if (flags.HasFlag(MeshFieldFlags.FaceIndices))
                {
                    result.Faces = new FaceData[fc];
                    for (int i = 0; i < fc; i++)
                    {
                        int n = r.ReadByte();
                        result.Faces[i].VertexIndices = new int[n];
                        for (int j = 0; j < n; j++)
                            result.Faces[i].VertexIndices[j] = r.ReadInt32();
                    }
                }

                if (flags.HasFlag(MeshFieldFlags.FaceMaterials))
                {
                    result.FaceMaterialIndices = new int[fc];
                    for (int i = 0; i < fc; i++)
                        result.FaceMaterialIndices[i] = r.ReadInt32();
                }

                if (flags.HasFlag(MeshFieldFlags.FaceFlags))
                {
                    result.FaceFlags = r.ReadBytes((int)fc);
                }

                if (flags.HasFlag(MeshFieldFlags.FaceIds))
                {
                    result.FaceIds = new int[fc];
                    for (int i = 0; i < fc; i++)
                        result.FaceIds[i] = r.ReadInt32();
                }

                if (flags.HasFlag(MeshFieldFlags.FaceUVIndices))
                {
                    if (result.Faces == null)
                        result.Faces = new FaceData[fc];

                    for (int i = 0; i < fc; i++)
                    {
                        int n = r.ReadByte();
                        result.Faces[i].UVIndices = new int[n];
                        for (int j = 0; j < n; j++)
                            result.Faces[i].UVIndices[j] = r.ReadInt32();
                    }
                }

                if (flags.HasFlag(MeshFieldFlags.FaceNormalIndices))
                {
                    if (result.Faces == null)
                        result.Faces = new FaceData[fc];

                    for (int i = 0; i < fc; i++)
                    {
                        int n = r.ReadByte();
                        result.Faces[i].NormalIndices = new int[n];
                        for (int j = 0; j < n; j++)
                            result.Faces[i].NormalIndices[j] = r.ReadInt32();
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// 受信バイト数から検算用の推定頂点数を返す
        /// （Positionsフラグの場合: dataSize / 12）
        /// </summary>
        public static int EstimateVertexCountFromPositions(int dataBytes, int headerSize = BinaryHeader.Size)
        {
            int bodyBytes = dataBytes - headerSize;
            if (bodyBytes <= 0) return 0;
            return bodyBytes / 12; // float×3 = 12B/vertex
        }
    }
}
