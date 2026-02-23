// Remote/RemoteBinaryProtocol.cs
// バイナリ転送プロトコル定義
//
// ■ データタイプ（先頭4バイトのマジックで識別）
//   PLRM = メッシュデータ
//   PLRI = 画像リスト
//   PLRD = モデルデータ（将来用）
//   PLRP = プロジェクトデータ（将来用）
//
// ■ メッシュデータ (PLRM) フォーマット
// [Header 20B]
//   Magic       : 4B "PLRM"
//   Version     : 1B (現在 1)
//   MessageType : 1B (0=MeshData, 1=PositionsOnly, 2=File)
//   FieldFlags  : 4B (ビットマスク)
//   VertexCount : 4B (uint)
//   FaceCount   : 4B (uint)
//   Reserved    : 2B
// [Body] フラグ順にフィールドバイト列が連結
//
// ■ 画像リスト (PLRI) フォーマット
// [Header 8B]
//   Magic      : 4B "PLRI"
//   Version    : 1B
//   ImageCount : 2B (uint16)
//   Reserved   : 1B
// [Entry × ImageCount]
//   ImageId    : 2B (uint16)
//   Format     : 1B (0=PNG, 1=JPEG)
//   Width      : 4B (uint32)
//   Height     : 4B (uint32)
//   DataLength : 4B (uint32)
//   ImageData  : [DataLength bytes]

using System;

namespace Poly_Ling.Remote
{
    // ================================================================
    // 共通マジック定数
    // ================================================================

    /// <summary>
    /// バイナリフレームのデータタイプ識別（先頭4バイト）
    /// </summary>
    public static class RemoteMagic
    {
        /// <summary>メッシュデータ "PLRM"</summary>
        public const uint Mesh    = 0x4D524C50;
        /// <summary>画像リスト "PLRI"</summary>
        public const uint Image   = 0x49524C50;
        /// <summary>モデルデータ "PLRD" （将来用）</summary>
        public const uint Model   = 0x44524C50;
        /// <summary>プロジェクトデータ "PLRP" （将来用）</summary>
        public const uint Project = 0x50524C50;

        /// <summary>先頭4バイトからマジックを読み取り</summary>
        public static uint Read(byte[] data)
        {
            if (data == null || data.Length < 4) return 0;
            return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        }
    }

    // ================================================================
    // メッシュデータ (PLRM)
    // ================================================================

    /// <summary>
    /// メッシュバイナリメッセージタイプ
    /// </summary>
    public enum BinaryMessageType : byte
    {
        /// <summary>メッシュデータ（フィールドフラグで内容選択）</summary>
        MeshData = 0,
        /// <summary>位置のみ更新（軽量）</summary>
        PositionsOnly = 1,
        /// <summary>ファイル丸ごと転送（PMX等）</summary>
        RawFile = 2,
    }

    /// <summary>
    /// メッシュフィールドフラグ（ビットマスク）
    /// </summary>
    [Flags]
    public enum MeshFieldFlags : uint
    {
        None           = 0,

        // === 頂点系（0x00FF） ===
        Positions      = 0x0001,
        Normals        = 0x0002,
        UVs            = 0x0004,
        BoneWeights    = 0x0008,
        VertexFlags    = 0x0010,
        VertexIds      = 0x0020,

        // === 面系（0xFF00） ===
        FaceIndices    = 0x0100,
        FaceMaterials  = 0x0200,
        FaceFlags      = 0x0400,
        FaceIds        = 0x0800,
        FaceUVIndices  = 0x1000,
        FaceNormalIndices = 0x2000,

        // === 複合ショートカット ===
        VertexBasic    = Positions | Normals | UVs,
        AllVertex      = Positions | Normals | UVs | BoneWeights | VertexFlags | VertexIds,
        AllFace        = FaceIndices | FaceMaterials | FaceFlags | FaceIds | FaceUVIndices | FaceNormalIndices,
        All            = AllVertex | AllFace,
    }

    /// <summary>
    /// メッシュバイナリヘッダ (PLRM)
    /// </summary>
    public struct BinaryHeader
    {
        public const int Size = 20;

        public byte Version;
        public BinaryMessageType MessageType;
        public MeshFieldFlags FieldFlags;
        public uint VertexCount;
        public uint FaceCount;
    }

    // ================================================================
    // 画像リスト (PLRI)
    // ================================================================

    /// <summary>
    /// 画像フォーマット
    /// </summary>
    public enum ImageFormat : byte
    {
        PNG = 0,
        JPEG = 1,
    }

    /// <summary>
    /// 画像リストヘッダ (PLRI)
    /// </summary>
    public struct ImageListHeader
    {
        public const int Size = 8;

        public byte Version;
        public ushort ImageCount;
    }

    /// <summary>
    /// 画像エントリヘッダ（各画像の先頭）
    /// </summary>
    public struct ImageEntryHeader
    {
        public const int Size = 15;

        public ushort ImageId;
        public ImageFormat Format;
        public uint Width;
        public uint Height;
        public uint DataLength;
    }

    // ================================================================
    // モデルデータ (PLRD) — 将来用
    // ================================================================

    /// <summary>
    /// モデルデータヘッダ (PLRD) — 将来用
    /// </summary>
    public struct ModelDataHeader
    {
        public const int Size = 8;

        public byte Version;
        public byte Flags;        // 将来用
        public ushort MeshCount;   // 含まれるメッシュ数
        public ushort BoneCount;   // 含まれるボーン数
        public ushort MorphCount;  // 含まれるモーフ数
        // Body: MeshHeader × MeshCount + ... (将来定義)
    }

    // ================================================================
    // プロジェクトデータ (PLRP) — 将来用
    // ================================================================

    /// <summary>
    /// プロジェクトデータヘッダ (PLRP) — 将来用
    /// </summary>
    public struct ProjectDataHeader
    {
        public const int Size = 8;

        public byte Version;
        public byte Flags;         // 将来用
        public ushort ModelCount;   // 含まれるモデル数
        public uint Reserved;
        // Body: ModelHeader × ModelCount + ... (将来定義)
    }
}
