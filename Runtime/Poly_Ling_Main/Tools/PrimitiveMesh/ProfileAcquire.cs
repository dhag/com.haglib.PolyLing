// ProfileAcquire.cs
// プロファイル（断面の点列・輪郭のループ群）の取り込み方を表す列挙と、
// その手順を掛け直す入口。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ「取り込み方」を持つか】
//   BeltAcquire と同じ理由。プロファイルは取り込んだ時点の点列をコマンドへ
//   焼き込むので、そのままだと取り込み元を直しても出力先は古いままになる。
//   点ごとの参照（頂点ID・頂点番号）を控えると手で作る人に番号の管理を強いる。
//   取り込みは「どの描画オブジェクトの 2 頂点ラインを、折れ線として読むか
//   閉ループとして読むか」だけで再現できるので、その手順を控える。
//
// 【取り込み元は 2 頂点ラインだけの描画オブジェクト】
//   図形生成パネルの「反映(→メッシュ)」が作る形。頂点 2 個だけの面を線とみなす。
//   三角形や四角形の面しか無いオブジェクトからは読めない。
//
// 【正規化】
//   帯系の断面（フリル・パイプ）は rung 長で正規化された系にあるため、
//   取り込みのときだけ長辺が 1 になるよう等方スケールする
//   （LineProfileExtractor.NormalizeToUnitSpan）。
//   回転体・2D 押し出しはモデルのローカル座標をそのまま使うので掛けない。
//   どちらかは呼び出し側（PLParam.ProfileNormalize）が決める。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>プロファイルをどうやって取り込んだか。</summary>
    public enum ProfileAcquireMethod
    {
        /// <summary>取り込み方を記録していない。作り直しでは控えた値をそのまま使う。</summary>
        Baked = 0,

        /// <summary>2 頂点ラインを開いた折れ線として読む（回転体・帯系の断面）。</summary>
        LinePolyline = 1,

        /// <summary>2 頂点ラインを閉ループ群として読む（2D 押し出し・閉じた断面）。</summary>
        LineLoops = 2,
    }

    /// <summary>取り直しの結果。</summary>
    public sealed class ProfileAcquireResult
    {
        /// <summary>取り直せたか。false のときは控えた値を使うこと。</summary>
        public bool Ok;

        /// <summary>
        /// 折れ線として読んだ点列。LineLoops のときは点数が最多のループを
        /// 1 本の点列として入れる（点列 1 本しか置けない受け口のため）。
        /// </summary>
        public List<Vector2> Points = new List<Vector2>();

        /// <summary>ループ群。LinePolyline のときは Points 1 本ぶんだけ入る。</summary>
        public List<Loop> Loops = new List<Loop>();

        /// <summary>人へ出す説明。成否にかかわらず入れる。</summary>
        public string Message = "";

        public static ProfileAcquireResult Fail(string message)
            => new ProfileAcquireResult { Ok = false, Message = message ?? "" };
    }

    /// <summary>プロファイルの取り込みを掛け直す。</summary>
    public static class ProfileAcquire
    {
        /// <summary>
        /// 記録した取り込み方で、取り込み元から点列／ループ群を取り直す。
        /// </summary>
        /// <param name="source">取り込み元の描画オブジェクト。</param>
        /// <param name="method">取り込み方。</param>
        /// <param name="normalize">長辺が 1 になるよう等方スケールするか（帯系の断面のみ true）。</param>
        public static ProfileAcquireResult Acquire(
            MeshContext source, ProfileAcquireMethod method, bool normalize)
        {
            if (method == ProfileAcquireMethod.Baked)
                return ProfileAcquireResult.Fail("取り込み方が記録されていない（控えた値を使う）");

            var mesh = source?.MeshObject;
            if (mesh == null)
                return ProfileAcquireResult.Fail("取り込み元のメッシュがない");

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mesh);
            if (lineFaces.Count == 0)
                return ProfileAcquireResult.Fail(
                    "2 頂点ラインが 1 本も無い（三角形や四角形の面からは読めない）");

            var result = new ProfileAcquireResult { Ok = true };

            if (method == ProfileAcquireMethod.LineLoops)
            {
                var loops = LineProfileExtractor.ExtractLoops(mesh, lineFaces);
                if (loops == null || loops.Count == 0)
                    return ProfileAcquireResult.Fail("線をつなげて閉ループにできない");

                // 点数が最多のループを、点列 1 本しか置けない受け口のために抜き出す。
                List<Vector2> best = null;
                foreach (var lp in loops)
                {
                    if (lp?.Points == null || lp.Points.Count < 3) continue;
                    result.Loops.Add(lp);
                    if (best == null || lp.Points.Count > best.Count) best = lp.Points;
                }
                if (result.Loops.Count == 0)
                    return ProfileAcquireResult.Fail("3 点以上のループが無い");

                result.Points  = Normalize(best, normalize);
                result.Message = $"線 {lineFaces.Count} 本 → ループ {result.Loops.Count} 本";

                if (normalize) NormalizeLoops(result.Loops);
                return result.Points != null
                    ? result
                    : ProfileAcquireResult.Fail("ループがつぶれている（全点が同じ位置）");
            }

            var pts = LineProfileExtractor.ExtractPolyline(mesh, lineFaces);
            if (pts == null || pts.Count < 2)
                return ProfileAcquireResult.Fail("線をつなげて折れ線にできない");

            var norm = Normalize(pts, normalize);
            if (norm == null)
                return ProfileAcquireResult.Fail("折れ線がつぶれている（全点が同じ位置）");

            result.Points  = norm;
            result.Loops.Add(new Loop { Points = new List<Vector2>(norm) });
            result.Message = $"線 {lineFaces.Count} 本 → 折れ線 {norm.Count} 点";
            return result;
        }

        private static List<Vector2> Normalize(IReadOnlyList<Vector2> src, bool normalize)
        {
            if (src == null) return null;
            if (!normalize) return new List<Vector2>(src);
            return LineProfileExtractor.NormalizeToUnitSpan(src);
        }

        /// <summary>
        /// ループ群をまとめて正規化する。
        /// ループごとに別々の倍率を掛けると相対位置が崩れるので、
        /// 全ループを合わせた AABB で 1 回だけ決める。
        /// </summary>
        private static void NormalizeLoops(List<Loop> loops)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var lp in loops)
            {
                if (lp?.Points == null) continue;
                foreach (var q in lp.Points)
                {
                    minX = Mathf.Min(minX, q.x); maxX = Mathf.Max(maxX, q.x);
                    minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y);
                }
            }

            float span = Mathf.Max(maxX - minX, maxY - minY);
            if (span <= 1e-6f) return;

            float k = 1f / span;
            foreach (var lp in loops)
            {
                if (lp?.Points == null) continue;
                for (int i = 0; i < lp.Points.Count; i++)
                    lp.Points[i] = new Vector2((lp.Points[i].x - minX) * k, (lp.Points[i].y - minY) * k);
            }
        }
    }
}
