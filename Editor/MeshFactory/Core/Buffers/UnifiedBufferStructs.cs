// Assets/Editor/MeshFactory/Core/Buffers/UnifiedBufferStructs.cs
// 統合バッファ用構造体定義
// GPUバッファと共有するデータレイアウト

using System.Runtime.InteropServices;
using UnityEngine;

namespace MeshFactory.Core
{
    // ============================================================
    // 頂点データ
    // ============================================================

    /// <summary>
    /// 統合頂点データ
    /// 全モデル・全メッシュを1つのバッファで管理
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnifiedVertex
    {
        /// <summary>頂点位置</summary>
        public Vector3 Position;

        /// <summary>法線</summary>
        public Vector3 Normal;

        /// <summary>UV座標</summary>
        public Vector2 UV;

        /// <summary>選択フラグ</summary>
        public uint Flags;

        /// <summary>所属メッシュインデックス</summary>
        public uint MeshIndex;

        /// <summary>所属モデルインデックス</summary>
        public uint ModelIndex;

        /// <summary>ローカル頂点インデックス（メッシュ内）</summary>
        public uint LocalIndex;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 12 + 12 + 8 + 4 + 4 + 4 + 4; // 48 bytes

        /// <summary>デフォルト値で初期化</summary>
        public static UnifiedVertex Default => new UnifiedVertex
        {
            Position = Vector3.zero,
            Normal = Vector3.up,
            UV = Vector2.zero,
            Flags = 0,
            MeshIndex = 0,
            ModelIndex = 0,
            LocalIndex = 0
        };
    }

    // ============================================================
    // ラインデータ（エッジ/補助線共通）
    // ============================================================

    /// <summary>
    /// 統合ラインデータ
    /// エッジと補助線を統一管理
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnifiedLine
    {
        /// <summary>頂点インデックス1（グローバル）</summary>
        public uint V1;

        /// <summary>頂点インデックス2（グローバル）</summary>
        public uint V2;

        /// <summary>選択フラグ</summary>
        public uint Flags;

        /// <summary>所属面インデックス（グローバル、補助線の場合は自身のインデックス）</summary>
        public uint FaceIndex;

        /// <summary>所属メッシュインデックス</summary>
        public uint MeshIndex;

        /// <summary>所属モデルインデックス</summary>
        public uint ModelIndex;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 4 * 6; // 24 bytes

        /// <summary>補助線判定用の無効な面インデックス</summary>
        public const uint InvalidFaceIndex = 0xFFFFFFFF;

        /// <summary>補助線かどうか</summary>
        public bool IsAuxLine => (Flags & (uint)SelectionFlags.IsAuxLine) != 0;
    }

    // ============================================================
    // 面データ
    // ============================================================

    /// <summary>
    /// 統合面データ
    /// 面の基本情報とフラグ
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UnifiedFace
    {
        /// <summary>頂点インデックス開始位置（IndexBuffer内）</summary>
        public uint IndexStart;

        /// <summary>頂点数（3または4以上）</summary>
        public uint VertexCount;

        /// <summary>選択フラグ</summary>
        public uint Flags;

        /// <summary>マテリアルインデックス</summary>
        public uint MaterialIndex;

        /// <summary>所属メッシュインデックス</summary>
        public uint MeshIndex;

        /// <summary>所属モデルインデックス</summary>
        public uint ModelIndex;

        /// <summary>面法線</summary>
        public Vector3 Normal;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 4 * 6 + 12; // 36 bytes
    }

    // ============================================================
    // メッシュ情報
    // ============================================================

    /// <summary>
    /// メッシュ情報
    /// 間接参照用のオフセット・サイズ情報
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshInfo
    {
        /// <summary>頂点バッファ内の開始位置</summary>
        public uint VertexStart;

        /// <summary>頂点数</summary>
        public uint VertexCount;

        /// <summary>ラインバッファ内の開始位置</summary>
        public uint LineStart;

        /// <summary>ライン数</summary>
        public uint LineCount;

        /// <summary>面バッファ内の開始位置</summary>
        public uint FaceStart;

        /// <summary>面数</summary>
        public uint FaceCount;

        /// <summary>インデックスバッファ内の開始位置</summary>
        public uint IndexStart;

        /// <summary>インデックス数</summary>
        public uint IndexCount;

        /// <summary>メッシュレベルのフラグ</summary>
        public uint Flags;

        /// <summary>所属モデルインデックス</summary>
        public uint ModelIndex;

        /// <summary>パディング（16バイトアラインメント用）</summary>
        public uint Padding0;
        public uint Padding1;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 4 * 12; // 48 bytes
    }

    // ============================================================
    // モデル情報
    // ============================================================

    /// <summary>
    /// モデル情報
    /// モデルレベルのオフセット・サイズ情報
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ModelInfo
    {
        /// <summary>メッシュ情報バッファ内の開始位置</summary>
        public uint MeshStart;

        /// <summary>メッシュ数</summary>
        public uint MeshCount;

        /// <summary>頂点の総開始位置（全メッシュ合計）</summary>
        public uint VertexStart;

        /// <summary>頂点の総数</summary>
        public uint VertexCount;

        /// <summary>モデルレベルのフラグ</summary>
        public uint Flags;

        /// <summary>パディング</summary>
        public uint Padding0;
        public uint Padding1;
        public uint Padding2;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 4 * 8; // 32 bytes
    }

    // ============================================================
    // カメラ情報
    // ============================================================

    /// <summary>
    /// カメラ情報（Uniform）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CameraInfo
    {
        /// <summary>ビュー行列</summary>
        public Matrix4x4 ViewMatrix;

        /// <summary>プロジェクション行列</summary>
        public Matrix4x4 ProjectionMatrix;

        /// <summary>ビュー・プロジェクション行列</summary>
        public Matrix4x4 ViewProjectionMatrix;

        /// <summary>カメラ位置（ワールド座標）</summary>
        public Vector4 CameraPosition;

        /// <summary>カメラ注視点</summary>
        public Vector4 CameraTarget;

        /// <summary>ビューポートサイズ (width, height, 1/width, 1/height)</summary>
        public Vector4 ViewportSize;

        /// <summary>ニア/ファークリップ (near, far, 0, 0)</summary>
        public Vector4 ClipPlanes;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 64 * 3 + 16 * 4; // 256 bytes
    }

    // ============================================================
    // ヒットテスト入力
    // ============================================================

    /// <summary>
    /// ヒットテスト入力データ
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HitTestInput
    {
        /// <summary>マウス位置（スクリーン座標）</summary>
        public Vector2 MousePosition;

        /// <summary>ヒット判定半径（ピクセル）</summary>
        public float HitRadius;

        /// <summary>ヒットテストモード（フラグ）</summary>
        public uint HitMode;

        /// <summary>プレビュー領域 (x, y, width, height)</summary>
        public Vector4 PreviewRect;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 8 + 4 + 4 + 16; // 32 bytes
    }

    // ============================================================
    // ヒットテスト出力
    // ============================================================

    /// <summary>
    /// ヒットテスト結果
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HitTestResult
    {
        /// <summary>ヒットした頂点インデックス（-1 = なし）</summary>
        public int HitVertexIndex;

        /// <summary>ヒットしたライン/エッジインデックス（-1 = なし）</summary>
        public int HitLineIndex;

        /// <summary>ヒットした面インデックス（-1 = なし）</summary>
        public int HitFaceIndex;

        /// <summary>ヒットタイプ（0=None, 1=Vertex, 2=Edge, 3=Face, 4=Line）</summary>
        public uint HitType;

        /// <summary>ヒット距離（スクリーン空間）</summary>
        public float HitDistance;

        /// <summary>ヒット位置（ワールド座標）</summary>
        public Vector3 HitPosition;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 4 * 5 + 12; // 32 bytes
    }

    // ============================================================
    // バウンディングボックス
    // ============================================================

    /// <summary>
    /// AABB（軸平行バウンディングボックス）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AABB
    {
        /// <summary>最小座標</summary>
        public Vector3 Min;

        /// <summary>パディング</summary>
        public float Padding0;

        /// <summary>最大座標</summary>
        public Vector3 Max;

        /// <summary>パディング</summary>
        public float Padding1;

        /// <summary>構造体サイズ（バイト）</summary>
        public const int Stride = 16 * 2; // 32 bytes

        /// <summary>Boundsから作成</summary>
        public static AABB FromBounds(Bounds bounds)
        {
            return new AABB
            {
                Min = bounds.min,
                Max = bounds.max
            };
        }

        /// <summary>Boundsに変換</summary>
        public Bounds ToBounds()
        {
            Bounds b = new Bounds();
            b.SetMinMax(Min, Max);
            return b;
        }
    }
}
