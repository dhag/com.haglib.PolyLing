// PointDefinedParams.cs
// 点指定図形（線分：円筒・角柱 / 三角：板 / 四角：板）のパラメータと、
// 指定点・状態表示の型。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 仕様は PolyLing_点指定図形生成モード作成計画.md。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>点指定図形の種類。</summary>
    public enum PointPrimitiveMode
    {
        Line,
        Triangle,
        Quad,
    }

    /// <summary>
    /// 3D プレビュー上で指定した 1 点。
    /// 既存頂点へ吸着した点は、その頂点が属するメッシュと頂点番号を持つ。
    /// 番号を再利用するのは、書き込み先（編集対象）のメッシュの頂点だけ。
    /// </summary>
    public struct PointPick
    {
        public Vector3 WorldPosition;
        /// <summary>既存メッシュに属さない場合は -1。</summary>
        public int MeshIndex;
        /// <summary>既存頂点でない場合は -1。</summary>
        public int VertexIndex;
    }

    /// <summary>点指定図形のパラメータ。</summary>
    [Serializable]
    public struct PointDefinedParams
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        public const float RadiusMin = 0.001f;
        public const float RadiusMax = 2f;

        public const int SidesMin = 3;
        public const int SidesMax = 64;

        /// <summary>分割数（エッジ数）の下限・上限。既存経路の探索もこの上限までしか探さない。</summary>
        public const int SegmentsMin = 1;
        public const int SegmentsMax = 64;

        public const float DepthMin = 0f;
        public const float DepthMax = 2f;

        public const int DepthSegmentsMin = 1;
        public const int DepthSegmentsMax = 32;

        public const int ApexIndexMin = 0;
        public const int ApexIndexMax = 2;

        // ── 線分 ─────────────────────────────────────────────────

        [PLParam(TextKey = "PointDefinedRadius", Description = "線分：円筒・角柱の半径",
                 Min = RadiusMin, Max = RadiusMax)]
        public float Radius;

        [PLParam(TextKey = "PointDefinedSides", Description = "線分：断面の角数。3 で三角柱、多いほど円筒に近い",
                 Min = SidesMin, Max = SidesMax, Step = 1)]
        public int Sides;

        [PLParam(TextKey = "PointDefinedLengthSeg", Description = "線分：軸方向の分割数",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int LengthSegments;

        [PLParam(TextKey = "PointDefinedCap", Description = "線分：両端に面を張る")]
        public bool Cap;

        // ── 四角 ─────────────────────────────────────────────────

        [PLParam(TextKey = "PointDefinedUSeg", Description = "四角：横方向（P0-P1 と P3-P2）の分割数",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int USegments;

        [PLParam(TextKey = "PointDefinedVSeg", Description = "四角：縦方向（P0-P3 と P1-P2）の分割数",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int VSegments;

        // ── 三角 ─────────────────────────────────────────────────

        [PLParam(TextKey = "PointDefinedBaseSeg", Description = "三角：底辺の分割数。どの段も同じ分割数になる",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int BaseSegments;

        [PLParam(TextKey = "PointDefinedHeightSeg", Description = "三角：頂点から底辺までの段数。斜辺の分割数でもある",
                 Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
        public int HeightSegments;

        [PLParam(TextKey = "PointDefinedApex", Description = "三角：頂点とみなす点の番号。残りの 2 点が底辺になる",
                 Min = ApexIndexMin, Max = ApexIndexMax, Step = 1)]
        public int ApexIndex;

        // ── 三角・四角の奥行き ───────────────────────────────────

        [PLParam(TextKey = "PointDefinedDepth", Description = "三角・四角：視線方向の奥行き。0 で表面だけを作る",
                 Min = DepthMin, Max = DepthMax)]
        public float Depth;

        [PLParam(TextKey = "PointDefinedDepthSeg", Description = "三角・四角：奥行き方向の分割数",
                 Min = DepthSegmentsMin, Max = DepthSegmentsMax, Step = 1)]
        public int DepthSegments;

        public static PointDefinedParams Default => new PointDefinedParams
        {
            Radius         = 0.05f,
            Sides          = 8,
            LengthSegments = 4,
            Cap            = true,
            USegments      = 4,
            VSegments      = 4,
            BaseSegments   = 4,
            HeightSegments = 4,
            ApexIndex      = 0,
            Depth          = 0f,
            DepthSegments  = 1,
        };
    }

    /// <summary>
    /// 境界の 1 辺についての既存経路との共有状態。パネルの状態表示に使う。
    /// </summary>
    public struct EdgeShareInfo
    {
        /// <summary>辺の種類の表示名キー（PrimitiveMeshTexts）。</summary>
        public string KindKey;
        /// <summary>辺の始点・終点の指定点番号（P0〜P3）。</summary>
        public int PointA, PointB;
        /// <summary>この辺の分割数（エッジ数）。</summary>
        public int Subdivisions;
        /// <summary>既存の最短経路のエッジ数。経路が無い・両端が既存頂点でないときは -1。</summary>
        public int PathEdges;
        /// <summary>分割数と一致して完全共有できるか。</summary>
        public bool CanShare;
    }

    /// <summary>直近のプレビュー生成の結果。</summary>
    public sealed class PointDefinedStatus
    {
        public int  Placed;
        public int  Required;
        public bool Valid;
        /// <summary>生成できない理由。生成できるとき・点が揃っていないときは null。</summary>
        public string Reason;
        public List<EdgeShareInfo> Edges = new List<EdgeShareInfo>();
    }
}
