// TextSignboard.cs
// 文字列メッシュの看板（文字の裏に貼る直方体）と、看板の裏面の文字。
// 確定側（PrimitiveMeshFactory.GenerateText）とプレビュー側（PlayerPrimitiveMeshSubPanel.Text）の双方が使う。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.GlyphText
{
    public static class TextSignboard
    {
        /// <summary>
        /// 看板を作るか。配置が平面で、文字に厚みがあるときだけ。
        /// 厚み 0 の文字は Z=0 の 1 枚板なので、看板の表面と同じ面に重なってしまう。
        /// </summary>
        public static bool IsEnabled(in TextMeshParams p)
            => p.Signboard && p.CurveMode == TextCurveMode.Flat && p.Thickness > 0.001f;

        /// <summary>
        /// 配置まで終えた文字メッシュの外形を測り、看板（と裏面の文字）を連結する。
        ///
        /// 文字は Profile2DExtrudeMeshGenerator により Z=±厚み/2 にあり、表は +Z。
        /// 看板：幅＝文字の左右端＋左右の余白×2、高さ＝文字の上下端＋上下の余白×2。
        ///       表面を文字の裏面（Z=−厚み/2）に合わせ、そこから奥行きぶん −Z へ伸ばす。四角面 6 枚。
        /// 裏面の文字：連結前の文字を看板の中心を通る Y 軸まわりに 180° 回し、
        ///       文字の表側だった面が看板の裏面（Z=−厚み/2−奥行き）から −Z へ出るように置く。
        /// パーツID：看板は nextPartsId、裏面の文字は元の ID ＋ nextPartsId ＋ 1。
        /// </summary>
        /// <param name="text">文字ごとに連結し終えた文字メッシュ。ここへ連結する。</param>
        /// <param name="nextPartsId">文字が使った次のパーツID（＝文字数）。</param>
        public static void Apply(MeshObject text, in TextMeshParams p, int nextPartsId)
        {
            if (text == null || text.Vertices == null || text.VertexCount == 0) return;
            if (!IsEnabled(p)) return;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (var v in text.Vertices)
            {
                if (v == null) continue;
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            if (min.x > max.x) return;

            float halfT  = p.Thickness * 0.5f;
            float depth  = Mathf.Max(TextMeshParams.SignDepthMin, p.SignDepth);
            float marginH = Mathf.Max(0f, p.SignMarginH);
            float marginV = Mathf.Max(0f, p.SignMarginV);

            float cx = (min.x + max.x) * 0.5f;
            float cy = (min.y + max.y) * 0.5f;
            float w  = (max.x - min.x) + marginH * 2f;
            float h  = (max.y - min.y) + marginV * 2f;

            // 裏面の文字は看板を足す前の文字だけを写す。
            MeshObject back = p.SignBackText ? text.Clone() : null;

            // ── 看板 ──
            var cp = CubeMeshGenerator.CubeParams.Default;
            cp.MeshName       = text.Name;
            cp.WidthTop       = w;
            cp.WidthBottom    = w;
            cp.DepthTop       = depth;
            cp.DepthBottom    = depth;
            cp.Height         = h;
            cp.CornerRadius   = 0f;
            cp.Subdivisions   = Vector3Int.one;
            cp.Pivot          = Vector3.zero;

            var board = CubeMeshGenerator.Generate(cp);
            if (board != null && board.VertexCount > 0)
            {
                Vector3 off = new Vector3(cx, cy, -halfT - depth * 0.5f);
                foreach (var v in board.Vertices)
                    if (v != null) v.Position += off;

                PartsIdOps.SetPartsId(board, nextPartsId);
                MeshObjectAppendOps.Append(text, board, copyNormals: true);
            }

            // ── 裏面の文字 ──
            if (back != null && back.VertexCount > 0)
            {
                // Y 軸まわり 180°（x → 2cx − x、z → −z）のあと、−(2×厚み/2 ＋ 奥行き) だけ Z を下げる。
                // 回転なので面の向き（頂点の巡回順）はそのまま正しい。
                float zShift = halfT * 2f + depth;
                foreach (var v in back.Vertices)
                {
                    if (v == null) continue;
                    var q = v.Position;
                    v.Position = new Vector3(2f * cx - q.x, q.y, -q.z - zShift);

                    if (v.Normals != null)
                        for (int k = 0; k < v.Normals.Count; k++)
                        {
                            var n = v.Normals[k];
                            v.Normals[k] = new Vector3(-n.x, n.y, -n.z);
                        }
                }

                PartsIdOps.OffsetPartsId(back, nextPartsId + 1);
                MeshObjectAppendOps.Append(text, back, copyNormals: true);
            }
        }
    }
}
