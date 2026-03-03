// QuadDecimatorData.cs
// Quadトポロジ優先減数化 - データ構造定義

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.Panels.QuadDecimator
{
    // ================================================================
    // エッジ
    // ================================================================

    /// <summary>
    /// 無向エッジ（a < b で正規化）
    /// </summary>
    public struct UndirectedEdge : System.IEquatable<UndirectedEdge>
    {
        public int A; // 小さい方
        public int B; // 大きい方

        public UndirectedEdge(int a, int b)
        {
            if (a < b) { A = a; B = b; }
            else { A = b; B = a; }
        }

        public bool Equals(UndirectedEdge other) => A == other.A && B == other.B;
        public override bool Equals(object obj) => obj is UndirectedEdge e && Equals(e);
        public override int GetHashCode() => A * 397 ^ B;
    }

    // ================================================================
    // 隣接情報付きエッジ
    // ================================================================

    public class MeshEdge
    {
        public int Id;
        public int A, B;             // A < B 正規化
        public int FaceL = -1;       // 片側の面インデックス
        public int FaceR = -1;       // もう片側の面インデックス
        public bool IsBoundary;      // 境界エッジ（片側のみ）
        public bool IsSeam;          // UVシーム
        public bool IsHard;          // ハードエッジ
        public bool IsCut => IsBoundary || IsSeam || IsHard;
    }

    // ================================================================
    // Quad隣接グラフ
    // ================================================================

    public enum DirClass { Unknown, U, V }

    /// <summary>
    /// Quad間リンク（QuadGraph上のエッジ）
    /// </summary>
    public class QuadLink
    {
        public int ToQuadIndex;       // 隣接Quad（Facesインデックス）
        public int SharedEdgeId;      // 共有MeshEdge.Id
        public DirClass Dir = DirClass.Unknown;
    }

    /// <summary>
    /// QuadGraphノード（1つのQuad面に対応）
    /// </summary>
    public class QuadNode
    {
        public int FaceIndex;         // Faces配列インデックス
        public Vector3 Center;        // Quad中心座標
        public Vector3 Normal;        // Quad法線
        public List<QuadLink> Links = new List<QuadLink>();
    }

    /// <summary>
    /// Quad隣接グラフ
    /// </summary>
    public class QuadGraph
    {
        public Dictionary<int, QuadNode> Nodes = new Dictionary<int, QuadNode>();
    }

    // ================================================================
    // QuadPatch（CutEdgeで区切られた連結成分）
    // ================================================================

    public class QuadPatch
    {
        public List<int> QuadFaceIndices = new List<int>();
        public Vector3 AvgNormal;
    }

    // ================================================================
    // ストリップ（同一方向のQuad列）
    // ================================================================

    public class QuadStrip
    {
        public List<int> QuadFaceIndices = new List<int>();
        public float SortKey; // 直交軸への射影値（ソート用）
    }

    // ================================================================
    // 置換操作
    // ================================================================

    /// <summary>
    /// Quad2枚→Quad1枚の置換操作
    /// </summary>
    public class ReplaceOp
    {
        public int QuadAFaceIndex;    // 元のQuad面A（Facesインデックス）
        public int QuadBFaceIndex;    // 元のQuad面B（Facesインデックス）
        public int SharedEdgeId;      // 共有エッジID

        /// <summary>新しいQuadの頂点インデックス（周回順、4頂点）</summary>
        public int[] NewVertexIndices;

        /// <summary>新しいQuadのUVサブインデックス（4要素）</summary>
        public int[] NewUVIndices;

        /// <summary>新しいQuadの法線サブインデックス（4要素）</summary>
        public int[] NewNormalIndices;

        /// <summary>新しいQuadのマテリアルインデックス</summary>
        public int MaterialIndex;

        /// <summary>採用優先度スコア（高い方が優先）</summary>
        public float Score;
    }

    // ================================================================
    // パラメータ
    // ================================================================

    public class DecimatorParams
    {
        /// <summary>目標三角形比率（0.0〜1.0）</summary>
        public float TargetRatio = 0.5f;

        /// <summary>最大パス数</summary>
        public int MaxPasses = 5;

        /// <summary>QuadPair隣接面の法線角度閾値（度）</summary>
        public float NormalAngleDeg = 15f;

        /// <summary>ハードエッジ判定角度（度）</summary>
        public float HardAngleDeg = 25f;

        /// <summary>UVシーム判定距離閾値（これ未満ならシームとしない）</summary>
        public float UvSeamThreshold = 0.01f;

        /// <summary>位置量子化精度（バウンディングボックス比）</summary>
        public float PosQuantFactor = 1e-5f;
    }

    // ================================================================
    // 実行結果
    // ================================================================

    public class DecimatorResult
    {
        public int OriginalFaceCount;
        public int ResultFaceCount;
        public int TotalCollapsed;
        public int PassCount;
        public List<string> PassLogs = new List<string>();
    }
}
