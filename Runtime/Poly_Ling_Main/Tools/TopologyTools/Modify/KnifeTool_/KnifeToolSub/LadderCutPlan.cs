// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/LadderCutPlan.cs
// ラダー切断の解決結果（インデックスベース）。
// LadderCutResolver が生成し、LadderCutExecutor が消費する。

using System.Collections.Generic;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 切断アンカーの種別。
    /// </summary>
    public enum LadderAnchorKind
    {
        /// <summary>既存頂点（端点）。新頂点を作らない。</summary>
        Vertex,
        /// <summary>ラング辺上の点（中間）。新頂点を作る。</summary>
        RungEdge
    }

    /// <summary>
    /// 1つの切断端点。既存頂点 or ラング辺上の点。
    /// </summary>
    public readonly struct LadderAnchor
    {
        public readonly LadderAnchorKind Kind;
        public readonly int        VertexIndex; // Kind==Vertex
        public readonly VertexPair RungEdge;    // Kind==RungEdge

        private LadderAnchor(LadderAnchorKind kind, int vertexIndex, VertexPair rung)
        {
            Kind = kind; VertexIndex = vertexIndex; RungEdge = rung;
        }

        public static LadderAnchor AtVertex(int v) => new LadderAnchor(LadderAnchorKind.Vertex, v, default);
        public static LadderAnchor AtRung(VertexPair e) => new LadderAnchor(LadderAnchorKind.RungEdge, -1, e);
    }

    /// <summary>
    /// 1つの面に対する切断指定（端点 A → 端点 B）。
    /// </summary>
    public readonly struct LadderFaceCut
    {
        public readonly int FaceIndex;
        public readonly LadderAnchor A;
        public readonly LadderAnchor B;

        public LadderFaceCut(int faceIndex, LadderAnchor a, LadderAnchor b)
        {
            FaceIndex = faceIndex; A = a; B = b;
        }
    }

    /// <summary>
    /// ラダー切断の解決結果。
    /// </summary>
    public sealed class LadderCutPlan
    {
        /// <summary>解決に成功したか。</summary>
        public bool Ok;

        /// <summary>失敗理由（Ok==false のとき）。</summary>
        public string Error = "";

        /// <summary>面ごとの切断指定（処理順）。</summary>
        public readonly List<LadderFaceCut> FaceCuts = new List<LadderFaceCut>();

        /// <summary>新頂点を作るラング辺（重複なし、各 1 頂点）。</summary>
        public readonly List<VertexPair> Rungs = new List<VertexPair>();

        public static LadderCutPlan Fail(string reason)
            => new LadderCutPlan { Ok = false, Error = reason };
    }
}
