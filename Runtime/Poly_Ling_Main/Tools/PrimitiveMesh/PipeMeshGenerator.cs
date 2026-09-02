// PipeMeshGenerator.cs
// 基準ベルト（梯子状の四角形群）＋断面プロファイルからパイプメッシュを生成する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【構成】梯子の rung（横木）ごとに断面を置き、rung 間を面でつなぐ。
//   断面が閉ループなら筒になり、基準ベルトが閉じた梯子ならドーナツ状に閉じる。
//
// 【断面の座標系】rung i ごとに次の系で解釈する。
//   原点 = Left[i]
//   X 軸 = normalize(Right[i] - Left[i])  … rung 方向
//   Y 軸 = 基準ベルトの面法線（X と直交化） … 法線方向
//   断面座標は rung 長で正規化（x=1 / y=1 が その rung の長さ）。
//
// 【巻き順】取り込み時に判定した基準ベルトの巻き順に従う。
//   断面が 2 点 (0,0)-(1,0) かつ開いた断面のときは基準ベルトと同一の面になる。
//   ただし閉じた断面（3 点以上）は、断面の周り順だけで筒の内外が決まってしまう
//   （flipWinding は quad の巻き順と yDir を同時に反転するので相殺し、内外は変わらない）。
//   そのため符号付き面積が正の断面は側面の巻き順を反転し、常に外向きの筒にする。
//
// 【蓋の巻き順】隣接する側面が rung の境界辺を通る向きの逆を通す（多様体一貫性）。
//   幾何的な外向き判定は使わない。先端頂点は元メッシュの先端三角形の頂点であり、
//   断面の第1辺 (0,0)-(1,0) と同一平面に載るため、内積による判定は 0 になって働かない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Pipe
{
    public static class PipeMeshGenerator
    {
        /// <summary>
        /// パイプメッシュを生成する。基準ベルトまたは断面が不足していれば空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            IReadOnlyList<Vector2> profile, bool profileClosed, bool capEnds,
            Vector3? startPoint, Vector3? endPoint,
            string meshName)
            => Generate(left, right, beltClosed, flipWinding,
                        profile, profileClosed, capEnds, startPoint, endPoint, meshName, null);

        /// <summary>
        /// パーツIDを採番する版。梯子1本＝パーツ1つとして partsIds から1つ取り、
        /// この梯子で作った全頂点（側面・蓋）へ書く。partsIds が null なら書かない。
        ///
        /// サブIDはここでは触らない。厚み付けや重複頂点の結合で頂点数が変わるため、
        /// メッシュ確定後に PrimitiveMeshPostProcess.AssignSubIdByPartsId で振り直す。
        /// 頂点が1つも出来なかったとき（基準ベルト／断面が不足）はIDを消費しない。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            IReadOnlyList<Vector2> profile, bool profileClosed, bool capEnds,
            Vector3? startPoint, Vector3? endPoint,
            string meshName, PartsIdCounter partsIds)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Pipe" : meshName);

            int n = (left == null || right == null) ? 0 : Mathf.Min(left.Count, right.Count);
            int m = (profile == null) ? 0 : profile.Count;
            if (n < 2 || m < 2) return mo;

            var xDir = new Vector3[n];
            var yDir = new Vector3[n];
            var len  = new float[n];
            BuildFrames(left, right, beltClosed, flipWinding, xDir, yDir, len);

            // 側面の巻き順。閉じた断面（3点以上）が正の向き（符号付き面積 > 0）のときは
            // 筒が内向きになるため反転し、断面をどちら回りで描いても外向きへ揃える。
            // 開いた断面・2点断面は基準ベルトの巻き順のまま
            //（断面 2 点 (0,0)-(1,0) が基準ベルトと同一面になる規約を保つ）。
            bool invertSide = profileClosed && m >= 3 && SignedProfileArea(profile) > 0f;
            bool quadFlip   = flipWinding ^ invertSide;

            int   segments = beltClosed ? n : n - 1;
            float den      = beltClosed ? n : n - 1;

            // 頂点: rung i × 断面点 k
            for (int i = 0; i < n; i++)
            {
                float u = i / den;
                for (int k = 0; k < m; k++)
                {
                    Vector2 p   = profile[k];
                    Vector3 pos = left[i] + xDir[i] * (p.x * len[i]) + yDir[i] * (p.y * len[i]);
                    float   v   = profileClosed ? k / (float)m : k / (float)(m - 1);
                    mo.Vertices.Add(new Vertex(pos, new Vector2(u, v)));
                }
            }

            // 側面: rung 間 × 断面区間
            int rings = profileClosed ? m : m - 1;
            for (int s = 0; s < segments; s++)
            {
                int j = (s + 1) % n;
                for (int k = 0; k < rings; k++)
                {
                    int k1 = (k + 1) % m;

                    int a = s * m + k;
                    int b = s * m + k1;
                    int c = j * m + k1;
                    int d = j * m + k;

                    if (quadFlip) mo.AddQuad(a, d, c, b);
                    else          mo.AddQuad(a, b, c, d);
                }
            }

            // 蓋（開いた梯子・閉じた断面のときのみ）
            // 巻き順は隣接する側面の境界辺から決める。
            //   rung 0 の境界辺を側面は k→k+1（quadFlip=false）／k+1→k（true）で通るので、
            //   先端の蓋はその逆を通る。rung n-1 では側面が逆順に通るため条件も逆になる。
            if (capEnds && !beltClosed && profileClosed && m >= 3)
            {
                bool headReverse = !quadFlip;
                bool tailReverse =  quadFlip;

                // 先端が与えられていれば、その点へ収束させる三角ファンで閉じる
                if (startPoint.HasValue) AddPointCap(mo, 0,           m, startPoint.Value, headReverse);
                else                     AddCap     (mo, 0,           m,                  headReverse);

                if (endPoint.HasValue)   AddPointCap(mo, (n - 1) * m, m, endPoint.Value,   tailReverse);
                else                     AddCap     (mo, (n - 1) * m, m,                  tailReverse);
            }

            // この梯子で作った全頂点へ同じパーツIDを書く（側面・蓋をまとめて1パーツ）。
            if (partsIds != null && mo.VertexCount > 0)
            {
                int pid = partsIds.Take();
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    if (mo.Vertices[v] != null) mo.Vertices[v].PartsId = pid;
                }
            }

            mo.RecalculateNormals();
            return mo;
        }

        // ================================================================
        // 断面
        // ================================================================

        /// <summary>断面の符号付き面積。正 = 正の向き（この向きだと筒が内向きになる）。</summary>
        private static float SignedProfileArea(IReadOnlyList<Vector2> profile)
        {
            int m = profile.Count;
            float sum = 0f;
            for (int k = 0; k < m; k++)
            {
                Vector2 p = profile[k];
                Vector2 q = profile[(k + 1) % m];
                sum += p.x * q.y - q.x * p.y;
            }
            return sum * 0.5f;
        }

        // ================================================================
        // rung ローカル系
        // ================================================================

        private static void BuildFrames(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            Vector3[] xDir, Vector3[] yDir, float[] len)
        {
            int n = xDir.Length;
            int segments = beltClosed ? n : n - 1;

            // セグメント面法線（基準ベルトの巻き順に合わせる）
            var segN = new Vector3[segments];
            for (int s = 0; s < segments; s++)
            {
                int j = (s + 1) % n;
                Vector3 a = left[s], b = right[s], c = right[j], d = left[j];
                segN[s] = flipWinding
                    ? NormalHelper.CalculateFaceNormal(a, d, c)
                    : NormalHelper.CalculateFaceNormal(a, b, c);
            }

            for (int i = 0; i < n; i++)
            {
                Vector3 axis = right[i] - left[i];
                len[i]  = axis.magnitude;
                xDir[i] = (len[i] > 1e-6f) ? axis / len[i] : Vector3.right;

                Vector3 sum = Vector3.zero;
                if (beltClosed)
                {
                    sum += segN[(i - 1 + segments) % segments];
                    sum += segN[i % segments];
                }
                else
                {
                    if (i > 0)     sum += segN[i - 1];
                    if (i < n - 1) sum += segN[i];
                }

                Vector3 y = (sum.sqrMagnitude > 1e-10f) ? sum.normalized : Vector3.up;

                Vector3 ortho = y - xDir[i] * Vector3.Dot(y, xDir[i]);
                yDir[i] = (ortho.sqrMagnitude > 1e-10f) ? ortho.normalized : y;
            }
        }

        // ================================================================
        // 蓋
        // ================================================================

        /// <summary>
        /// baseIdx から m 個の断面リング頂点を、指定した先端へ収束させる三角ファンで閉じる。
        /// reverse=false でリングを k→k+1 の向きに、true で k+1→k の向きに通る。
        /// </summary>
        private static void AddPointCap(MeshObject mo, int baseIdx, int m, Vector3 apex, bool reverse)
        {
            int apexIdx = mo.VertexCount;
            mo.Vertices.Add(new Vertex(apex, new Vector2(0.5f, 0.5f)));

            for (int k = 0; k < m; k++)
            {
                int a = baseIdx + k;
                int b = baseIdx + (k + 1) % m;

                var f = new Face();
                f.VertexIndices.Add(apexIdx);
                if (reverse) { f.VertexIndices.Add(b); f.VertexIndices.Add(a); }
                else         { f.VertexIndices.Add(a); f.VertexIndices.Add(b); }
                for (int i = 0; i < 3; i++) { f.UVIndices.Add(0); f.NormalIndices.Add(0); }
                mo.AddFace(f);
            }
        }

        /// <summary>
        /// baseIdx から m 個の断面リング頂点で N 角形の蓋を張る。
        /// reverse=false でリングを k→k+1 の向きに、true で k+1→k の向きに通る。
        /// </summary>
        private static void AddCap(MeshObject mo, int baseIdx, int m, bool reverse)
        {
            var idx = new List<int>(m);
            for (int k = 0; k < m; k++) idx.Add(baseIdx + k);
            if (reverse) idx.Reverse();

            var face = new Face();
            face.VertexIndices.AddRange(idx);
            for (int k = 0; k < m; k++)
            {
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }
            mo.AddFace(face);
        }
    }
}
