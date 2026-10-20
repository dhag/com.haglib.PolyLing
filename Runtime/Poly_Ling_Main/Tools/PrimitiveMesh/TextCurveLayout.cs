// TextCurveLayout.cs
// 文字列メッシュの曲面配置（円筒・円盤のふち）。
// 1 文字ずつ平らに生成したメッシュを、文字全体を 1 つの塊として回転＋移動する。
// 頂点ごとに曲げると字形が歪むため、文字単位の剛体変換に限る。
// 確定側（PrimitiveMeshFactory.GenerateText）とプレビュー側（PlayerPrimitiveMeshSubPanel.Text）の双方が使う。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.GlyphText
{
    public static class TextCurveLayout
    {
        /// <summary>
        /// 1 文字分のメッシュを配置モードに従って動かす。平面なら何もしない。
        ///
        /// 基準点は「字送り幅の中央 × その行のベースライン」。
        /// 回転角は「行頭からの距離（基準点の X）÷ 半径」。行頭が角度 0。
        ///
        /// 向きの説明はすべて正面ビュー（+Z 側から見た画面）での見え方。平面の文字と同じく読む方向は画面の右（ワールド -X）。
        /// 円筒：Y 軸まわり。角度 0 は +Z 側で、文字の表（+Z）を外へ向け、読む方向（ワールド -X 側）へ回る。
        ///       平面時の Z（厚み方向）は半径方向になる。行の上下（Y）はそのまま。
        /// 円盤のふち（XY 面、表は +Z のまま）：
        ///   flip=false … 文字の上側を中心へ向ける。円の下端から反時計回り。下の行ほど外側。
        ///   flip=true  … 文字の下側を中心へ向ける。円の上端から時計回り。下の行ほど内側。
        ///   半径は 1 行目のベースラインの位置。
        /// </summary>
        /// <param name="mo">1 文字分のメッシュ（Profile2DGenerateParams.Scale = size で生成済み）。</param>
        /// <param name="g">その文字の配置情報（em 単位）。</param>
        /// <param name="size">1em の大きさ（生成時の Scale と同じ値）。</param>
        /// <param name="startAngle">
        /// 円盤のふちのみ。並び全体を正面から見て時計回りに回す角度（度）。
        /// </param>
        /// <param name="fitLine">
        /// 円盤のふちのみ。x（その行の字送りの全長、em）が 0 より大きいときは、その行を 1 周に均等割り付けする。
        /// y は行頭の文字の基準点 X（em）。回転角は「(基準点の X − y) ÷ x × 360°」で、行頭の文字が角度 0 に来る。
        /// FitLineWidths の値をそのまま渡す。
        /// </param>
        public static void Apply(MeshObject mo, GlyphLoops g, float size,
            TextCurveMode mode, float radius, bool flip,
            float startAngle = 0f, Vector2 fitLine = default)
        {
            if (mo == null || mo.Vertices == null) return;
            if (mode == TextCurveMode.Flat) return;

            float r = Mathf.Max(TextMeshParams.CurveRadiusMin, radius);

            float ax    = g.AnchorX   * size;
            float by    = g.BaselineY * size;
            float lineY = g.LineY     * size;

            float theta = ax / r;
            float deg   = theta * Mathf.Rad2Deg;

            bool rim = mode == TextCurveMode.DiskRim;
            if (rim && fitLine.x > 1e-6f)
                deg = (g.AnchorX - fitLine.y) / fitLine.x * 360f;

            Quaternion rot;
            Vector3 origin;

            switch (mode)
            {
                case TextCurveMode.Cylinder:
                    // 基準点を (0, Y そのまま, R) に置き、Y 軸まわりに回す。
                    rot    = Quaternion.AngleAxis(deg, Vector3.up);
                    origin = new Vector3(0f, by, r);
                    break;

                case TextCurveMode.DiskRim:
                    if (flip)
                    {
                        // 円の上端 (0, R) に下側を中心へ向けて立て、時計回りに回す。
                        rot    = Quaternion.AngleAxis(-deg, Vector3.forward);
                        origin = new Vector3(0f, r + lineY, 0f);
                    }
                    else
                    {
                        // 円の下端 (0, -R) に上側を中心へ向けて立て、反時計回りに回す。
                        rot    = Quaternion.AngleAxis(deg, Vector3.forward);
                        origin = new Vector3(0f, lineY - r, 0f);
                    }
                    break;

                default:
                    return;
            }

            // 開始角。編集面の座標は正面ビューの画面座標と同じ向き（x が画面右）なので、
            // 画面で時計回りは Z 軸まわりの負の回転。
            if (rim && Mathf.Abs(startAngle) > 1e-6f)
                rot = Quaternion.AngleAxis(-startAngle, Vector3.forward) * rot;

            // Profile2DExtrudeMeshGenerator は最後に X を鏡映して出力する（編集面の x → ワールド -X）。
            // 配置の計算は編集面（字送りが +x の座標）で行うため、X を戻してから回し、再び鏡映する。
            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];

                // 基準点からの相対位置（編集面での文字のローカル座標）。
                Vector3 local = new Vector3(-v.Position.x - ax, v.Position.y - by, v.Position.z);
                Vector3 p = rot * (origin + local);
                v.Position = new Vector3(-p.x, p.y, p.z);

                if (v.Normals != null)
                {
                    for (int k = 0; k < v.Normals.Count; k++)
                    {
                        Vector3 n0 = v.Normals[k];
                        Vector3 n  = rot * new Vector3(-n0.x, n0.y, n0.z);
                        v.Normals[k] = new Vector3(-n.x, n.y, n.z);
                    }
                }
            }
        }

        /// <summary>
        /// 1 周への均等割り付けに使う、行ごとの（字送りの全長, 行頭の文字の基準点 X）。em 単位。キーは行の LineY。
        ///
        /// 全長は「行頭の文字の基準点 X ＋ 行末の文字の基準点 X」とする。
        /// 基準点は字送り幅の中央なので、行頭と行末の字送りが同じなら行の全長と一致し、
        /// 1 周したときの行末と行頭の間隔が文字どうしの間隔とそろう。
        /// </summary>
        public static System.Collections.Generic.Dictionary<float, Vector2> FitLineWidths(
            System.Collections.Generic.IReadOnlyList<GlyphLoops> glyphs)
        {
            var min = new System.Collections.Generic.Dictionary<float, float>();
            var max = new System.Collections.Generic.Dictionary<float, float>();
            if (glyphs != null)
            {
                foreach (var g in glyphs)
                {
                    if (!min.TryGetValue(g.LineY, out float a) || g.AnchorX < a) min[g.LineY] = g.AnchorX;
                    if (!max.TryGetValue(g.LineY, out float b) || g.AnchorX > b) max[g.LineY] = g.AnchorX;
                }
            }

            var result = new System.Collections.Generic.Dictionary<float, Vector2>();
            foreach (var kv in min) result[kv.Key] = new Vector2(kv.Value + max[kv.Key], kv.Value);
            return result;
        }
    }
}
