// Remote/RemoteBinaryProtocol.cs
// バイナリ転送プロトコル定義
//
// ■ データタイプ（先頭4バイトのマジックで識別）
//
//   [S→C プログレッシブプロトコル]
//   PLRH = ProjectHeader   プロジェクト名・モデル数
//   PLRM = ModelMeta       モデルメタデータ（ジオメトリなし）
//   PLRS = MeshSummary     メッシュサマリ（ジオメトリなし）
//   PLRD = MeshData        メッシュジオメトリ本体
//   PLRI = ImageList       画像リスト（変更なし）
//
//   [C→S クライアントアップロード]
//   PLRM = Mesh            クライアントがアップロードする単体メッシュ
//          (S→Cの ModelMeta と同じマジック値だが方向で区別)
//
// ■ PLRH フォーマット
// [Header 8B+]
//   Magic             : 4B "PLRH"
//   Version           : 1B (現在 1)
//   CurrentModelIndex : 1B
//   ModelCount        : 2B (uint16)
//   ProjectName       : string (length-prefixed UTF8)
//
// ■ PLRM フォーマット（S→C ModelMeta）
// [Header 8B+]
//   Magic      : 4B "PLRM"
//   Version    : 1B
//   Padding    : 1B
//   ModelIndex : 2B (int16)
//   ModelName  : string
//   MeshCount  : 2B (uint16)
//   ... (詳細はRemoteProgressiveSerializer参照)
//
// ■ PLRS フォーマット（S→C MeshSummary）
// [Header 10B+]
//   Magic      : 4B "PLRS"
//   Version    : 1B
//   Padding    : 1B
//   ModelIndex : 2B (int16)
//   MeshIndex  : 2B (int16)
//   ... (詳細はRemoteProgressiveSerializer参照)
//
// ■ PLRD フォーマット（S→C MeshData）
// [Header 24B]
//   Magic       : 4B "PLRD"
//   Version     : 1B
//   Padding     : 1B
//   ModelIndex  : 2B (int16)
//   MeshIndex   : 2B (int16)
//   FieldFlags  : 4B (uint32)
//   VertexCount : 4B (uint32)
//   FaceCount   : 4B (uint32)
//   Reserved    : 2B
// [Body] PLRM ボディと同一フォーマット
//
// ■ PLRI フォーマット（画像リスト）
// [Header 8B]
//   Magic      : 4B "PLRI"
//   Version    : 1B
//   ImageCount : 2B (uint16)
//   Reserved   : 1B
// [Entry × ImageCount] ...
//
// ■ PLRM フォーマット（C→S Mesh、RemoteBinarySerializer使用）
// [Header 20B]
//   Magic       : 4B "PLRM"
//   Version     : 1B
//   MessageType : 1B (0=MeshData, 1=PositionsOnly, 2=File)
//   FieldFlags  : 4B
//   VertexCount : 4B
//   FaceCount   : 4B
//   Reserved    : 2B
// [Body] フラグ順にフィールドバイト列

using System;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Remote
{
    // ================================================================
    // マジック定数
    // ================================================================

    public static class RemoteMagic
    {
        // S→C プログレッシブプロトコル
        /// <summary>ProjectHeader "PLRH"</summary>
        public const uint ProjectHeader = 0x48524C50;
        /// <summary>ModelMeta "PLRM"</summary>
        public const uint ModelMeta     = 0x4D524C50;
        /// <summary>MeshSummary "PLRS"</summary>
        public const uint MeshSummary   = 0x53524C50;
        /// <summary>MeshData "PLRD"</summary>
        public const uint MeshData      = 0x44524C50;
        /// <summary>BatchFrame "PLRB" 複数フレームの一括送信</summary>
        public const uint Batch         = 0x42524C50;
        /// <summary>ImageList "PLRI"</summary>
        public const uint Image         = 0x49524C50;

        // C→S クライアントアップロード（ModelMetaと同値、方向で区別）
        /// <summary>クライアントアップロードメッシュ "PLRM"</summary>
        public const uint Mesh          = 0x4D524C50;

        /// <summary>先頭4バイトからマジックを読み取り</summary>
        public static uint Read(byte[] data)
        {
            if (data == null || data.Length < 4) return 0;
            return (uint)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
        }
    }

    // ================================================================
    // C→S メッシュアップロード (PLRM) 関連定義
    // RemoteBinarySerializer が使用
    // ================================================================

    /// <summary>C→S メッシュバイナリメッセージタイプ</summary>
    public enum BinaryMessageType : byte
    {
        MeshData      = 0,
        PositionsOnly = 1,
        RawFile       = 2,
    }

    /// <summary>メッシュフィールドフラグ（ビットマスク）</summary>
    [Flags]
    public enum MeshFieldFlags : uint
    {
        None              = 0,

        // 頂点系
        Positions         = 0x0001,
        Normals           = 0x0002,
        UVs               = 0x0004,
        BoneWeights       = 0x0008,
        VertexFlags       = 0x0010,
        VertexIds         = 0x0020,

        // 面系
        FaceIndices       = 0x0100,
        FaceMaterials     = 0x0200,
        FaceFlags         = 0x0400,
        FaceIds           = 0x0800,
        FaceUVIndices     = 0x1000,
        FaceNormalIndices = 0x2000,

        // 複合
        VertexBasic    = Positions | Normals | UVs,
        AllVertex      = Positions | Normals | UVs | BoneWeights | VertexFlags | VertexIds,
        AllFace        = FaceIndices | FaceMaterials | FaceFlags | FaceIds | FaceUVIndices | FaceNormalIndices,
        All            = AllVertex | AllFace,
    }

    /// <summary>C→S メッシュバイナリヘッダ (PLRM, 20B)</summary>
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
    // 画像リスト (PLRI) — 変更なし
    // ================================================================

    public enum ImageFormat : byte { PNG = 0, JPEG = 1 }

    public struct ImageListHeader
    {
        public const int Size = 8;
        public byte Version;
        public ushort ImageCount;
    }

    public struct ImageEntryHeader
    {
        public const int Size = 15;
        public ushort ImageId;
        public ImageFormat Format;
        public uint Width;
        public uint Height;
        public uint DataLength;
    }
}
