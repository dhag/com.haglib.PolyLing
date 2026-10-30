// SurfacePasteOps.cs
// 平面化＋貼り付け（旧 NCSHAGLIB の「福笑い貼り付け」_PasteFlatParts と「Z を 0 にする」_SetFixedPositionForced）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【手順】（PolyLing_作り直し整備計画.md 3.5・段 7）
//   方向 n（正規化）と、原点を通り n に直交する平面を使う。点はモデル空間（バインド姿勢のワールド）で扱う。
//   1. 平面化（任意）：各頂点 v を平面へ落とす（v − (v·n) n）。旧ツールの「Z を 0」は n = (0,0,±1) の場合
//   2. 貼り付け（参照面があるとき）：
//        vs = v を平面へ射影した点、vm = v − vs（平面からの高さ）
//        vs を通り n に沿う直線（両向き）と参照面の三角形の交点 p を探す
//        符号付きの値 s = offset + n·(p − vs) が min ≤ s ≤ max のものから |s| が最も小さいものを選ぶ
//        v' = p + vm（参照面から元の高さだけ浮かせる）
//        交点が無い頂点は動かさない
//
// 【旧ツールとの違い】
//   ・旧ツールは比べる値（|offset + 距離|）と控える値（直線の始点からの距離）が違い、
//     offset ≠ 0 のとき最も近い交点を選べないことがあった。比べる値と控える値を |s| にそろえた
//   ・交差の判定は旧ツールと同じく直線（線分ではない）。三角形の内外は重心座標で判定し、
//     縁に乗る点は小さな許容（EdgeEpsilon）で拾う（旧ツールは縁までの距離 0.001 以下を拾った）
//
// 生成物は入力の格納空間へ戻して返す（面構成・UV・パーツ ID・サブ ID は入力のまま）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>平面化＋貼り付けの引数。</summary>
    public struct SurfacePasteParams
    {
        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        /// <summary>貼り付けの方向（モデル空間）。平面はこれに直交し原点を通る。</summary>
        [PLParam(TextKey = "SurfacePasteDirection", Description = "貼り付けの方向（モデル空間）。平面はこれに直交し原点を通る。旧ツールは (0,0,-1)")]
        public Vector3 Direction;

        /// <summary>先に平面へ落とすか（旧ツールの「梯子の Z を 0 にする」）。</summary>
        [PLParam(TextKey = "SurfacePasteFlatten", Description = "貼り付けの前に頂点を平面へ落とす（旧ツールの「Z を 0 にする」）")]
        public bool Flatten;

        /// <summary>仮想の基準面のずれ（旧 lefrength）。</summary>
        [PLParam(TextKey = "SurfacePasteOffset", Description = "交点を選ぶときの基準面のずれ（旧ツールの lefrength）")]
        public float Offset;

        /// <summary>交点として認める符号付きの値の上限。</summary>
        [PLParam(TextKey = "SurfacePasteMax", Description = "交点として認める符号付きの値（offset＋方向への距離）の上限")]
        public float Max;

        /// <summary>交点として認める符号付きの値の下限。</summary>
        [PLParam(TextKey = "SurfacePasteMin", Description = "交点として認める符号付きの値（offset＋方向への距離）の下限")]
        public float Min;

        public static SurfacePasteParams Default => new SurfacePasteParams
        {
            MeshName  = "SurfacePaste",
            Direction = new Vector3(0f, 0f, -1f),
            Flatten   = false,
            Offset    = 0f,
            Max       = 1000f,
            Min       = -1000f,
        };
    }

    /// <summary>
    /// 貼り付けの入力。PrimitiveMeshFactory の呼び出し側（モデルを持つ側）が組む。
    /// Source は入力の格納値のまま、Reference はモデル空間へ移した参照面（無ければ null）。
    /// </summary>
    public sealed class SurfacePasteInput
    {
        public MeshObject Source;
        /// <summary>入力の 格納 → モデル空間。</summary>
        public Matrix4x4 SourceToModel = Matrix4x4.identity;
        /// <summary>モデル空間の参照面。null なら平面化だけ。</summary>
        public MeshObject ReferenceInModel;
    }

    /// <summary>貼り付けの入力を索引から組む口。</summary>
    public delegate SurfacePasteInput SurfacePasteResolver(int sourceIndex, int referenceIndex);

    public static class SurfacePasteOps
    {
        /// <summary>三角形の縁に乗る点を拾う許容（重心座標）。</summary>
        public const float EdgeEpsilon = 1e-5f;

        /// <summary>直前の生成で、交点が見つからず動かさなかった頂点の数（表示用）。</summary>
        public static int LastMissed { get; private set; }

        /// <summary>
        /// 入力を複製して平面化・貼り付けし、入力の格納空間で返す。入力が無ければ null。
        /// </summary>
        public static MeshObject Build(SurfacePasteInput input, SurfacePasteParams p)
        {
            LastMissed = 0;
            if (input?.Source == null) return null;

            var mo = input.Source.Clone();
            mo.Name = string.IsNullOrEmpty(p.MeshName) ? "SurfacePaste" : p.MeshName;

            var tris = input.ReferenceInModel != null ? Triangles(input.ReferenceInModel) : null;

            int missed = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;

                v.Position = PastePoint(input, p, tris, v.Position, out bool hit);
                if (tris != null && !hit) missed++;

                // 逆方向（段 14）：出力の頂点 i は入力の頂点 i から作る。
                Poly_Ling.Ops.ModifierFrameTrace.RecordSource(mo, new[] { i }, input.Source, i);
            }

            mo.InvalidatePositionCache();
            mo.RecalculateNormals();
            LastMissed = missed;
            return mo;
        }

        /// <summary>方向（正規化）。</summary>
        private static Vector3 DirectionOf(SurfacePasteParams p)
            => p.Direction.sqrMagnitude > 1e-12f ? p.Direction.normalized : new Vector3(0f, 0f, -1f);

        /// <summary>
        /// 入力の格納値 1 点を平面化・貼り付けした点（入力の格納空間）。tris は Triangles(参照面)、無ければ null。
        /// hit は参照面との交点が見つかったか（参照面が無いときは false）。
        /// </summary>
        public static Vector3 PastePoint(
            SurfacePasteInput input, SurfacePasteParams p,
            List<(Vector3 a, Vector3 b, Vector3 c)> tris, Vector3 sourcePos, out bool hit)
        {
            hit = false;
            Vector3 n = DirectionOf(p);
            Matrix4x4 toModel = input.SourceToModel;

            Vector3 w = toModel.MultiplyPoint3x4(sourcePos);
            if (p.Flatten) w -= n * Vector3.Dot(w, n);

            if (tris != null)
            {
                float h  = Vector3.Dot(w, n);
                Vector3 vs = w - n * h;     // 平面への射影
                Vector3 vm = n * h;         // 平面からの高さ

                if (TryNearestHit(tris, vs, n, p.Offset, p.Min, p.Max, out Vector3 at))
                { w = at + vm; hit = true; }
            }
            return toModel.inverse.MultiplyPoint3x4(w);
        }

        /// <summary>
        /// 逆方向（段 14）：貼り付けた点 pasted（入力の格納空間）になる入力の点（入力の格納空間）。
        ///   平面内の位置 … 出力の平面内の位置そのもの（正方向は平面内の位置を変えない）
        ///   方向の成分   … 平面化しないとき：出力の方向の成分 − 新しい平面内の位置での参照面の交点の方向の成分
        ///                   （交点が無ければ出力の方向の成分そのもの）
        ///                   平面化するとき：出力へ効かないので元の入力 oldSource の値を保つ
        /// </summary>
        public static Vector3 InvertPoint(
            SurfacePasteInput input, SurfacePasteParams p,
            List<(Vector3 a, Vector3 b, Vector3 c)> tris, Vector3 oldSource, Vector3 pasted)
        {
            Vector3 n = DirectionOf(p);
            Matrix4x4 toModel = input.SourceToModel;

            Vector3 e  = toModel.MultiplyPoint3x4(pasted);
            float  he  = Vector3.Dot(e, n);
            Vector3 q  = e - n * he;                       // 平面内の位置

            float h;
            if (p.Flatten)
            {
                h = Vector3.Dot(toModel.MultiplyPoint3x4(oldSource), n);
            }
            else if (tris != null && TryNearestHit(tris, q, n, p.Offset, p.Min, p.Max, out Vector3 at))
            {
                h = he - Vector3.Dot(at, n);
            }
            else
            {
                h = he;
            }
            return toModel.inverse.MultiplyPoint3x4(q + n * h);
        }

        /// <summary>
        /// 貼り付けの入力を組む（ビューアの作成・作り直しと逆方向で共有する。段 14）。
        /// 入力は格納値のまま、参照面はモデル空間（バインド姿勢のワールド。VertexToBindWorldMatrix）へ移した複製にする。
        /// 入力が引けなければ null。
        /// </summary>
        public static SurfacePasteInput ResolveInput(Poly_Ling.Context.ModelContext model, int sourceIndex, int referenceIndex)
        {
            var src = model?.GetMeshContext(sourceIndex);
            if (src?.MeshObject == null) return null;
            model.ComputeWorldMatrices();

            var input = new SurfacePasteInput
            {
                Source        = src.MeshObject,
                SourceToModel = src.VertexToBindWorldMatrix,
            };

            var rf = referenceIndex >= 0 ? model.GetMeshContext(referenceIndex) : null;
            if (rf?.MeshObject != null)
            {
                var r = rf.MeshObject.Clone();
                Matrix4x4 m = rf.VertexToBindWorldMatrix;
                foreach (var v in r.Vertices) if (v != null) v.Position = m.MultiplyPoint3x4(v.Position);
                r.InvalidatePositionCache();
                input.ReferenceInModel = r;
            }
            return input;
        }

        /// <summary>参照面の三角形（面を扇に割る）。</summary>
        public static List<(Vector3 a, Vector3 b, Vector3 c)> Triangles(MeshObject mo)
        {
            var list = new List<(Vector3, Vector3, Vector3)>();
            foreach (var f in mo.Faces)
            {
                var vi = f?.VertexIndices;
                if (vi == null || vi.Count < 3) continue;
                Vector3 a = mo.Vertices[vi[0]].Position;
                for (int k = 1; k + 1 < vi.Count; k++)
                    list.Add((a, mo.Vertices[vi[k]].Position, mo.Vertices[vi[k + 1]].Position));
            }
            return list;
        }

        /// <summary>
        /// q を通り n に沿う直線（両向き）と三角形の交点のうち、s = offset + n·(p − q) が
        /// min〜max に入り |s| が最小のもの。
        /// </summary>
        private static bool TryNearestHit(
            List<(Vector3 a, Vector3 b, Vector3 c)> tris, Vector3 q, Vector3 n,
            float offset, float min, float max, out Vector3 best)
        {
            best = q;
            float bestAbs = float.MaxValue;
            bool found = false;

            foreach (var t in tris)
            {
                Vector3 e1 = t.b - t.a, e2 = t.c - t.a;
                Vector3 pv = Vector3.Cross(n, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-12f) continue;          // 直線と平行
                float inv = 1f / det;
                Vector3 tv = q - t.a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < -EdgeEpsilon || u > 1f + EdgeEpsilon) continue;
                Vector3 qv = Vector3.Cross(tv, e1);
                float w = Vector3.Dot(n, qv) * inv;
                if (w < -EdgeEpsilon || u + w > 1f + EdgeEpsilon) continue;
                float d = Vector3.Dot(e2, qv) * inv;            // q からの符号付き距離（n は単位）

                float s = offset + d;
                if (s < min || s > max) continue;
                float a = Mathf.Abs(s);
                if (a < bestAbs) { bestAbs = a; best = q + n * d; found = true; }
            }
            return found;
        }
    }
}
