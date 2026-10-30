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
//   Z 軸 = X × Y                           … 断面の点の z を置く向き
//   断面座標は rung 長で正規化（x=1 / y=1 / z=1 が その rung の長さ）。
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
    /// <summary>
    /// 生成の細かい切り替え（旧ツール互換・終端ぱっつん）。既定値（default）は現行の作り方。
    /// PipeParams の「一括」と「個別」を合わせた実効値を入れる（FromParams）。
    /// </summary>
    public struct PipeGenOptions
    {
        public bool    LegacyFrame;
        public bool    LegacyRawProfile;
        public bool    LegacyVertexOrder;
        public bool    LegacyUV;
        public Vector3 LegacyCutEndScale;
        public bool    PattsunEnd;

        public static PipeGenOptions FromParams(PipeParams p) => new PipeGenOptions
        {
            LegacyFrame       = p.UseLegacyFrame,
            LegacyRawProfile  = p.UseLegacyRawProfile,
            LegacyVertexOrder = p.UseLegacyVertexOrder,
            LegacyUV          = p.UseLegacyUV,
            LegacyCutEndScale = p.LegacyCutEndScale,
            PattsunEnd        = p.PattsunEnd,
        };
    }

    public static class PipeMeshGenerator
    {
        /// <summary>
        /// パイプメッシュを生成する。基準ベルトまたは断面が不足していれば空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            IReadOnlyList<Vector3> profile, bool profileClosed, bool capEnds,
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
            IReadOnlyList<Vector3> profile, bool profileClosed, bool capEnds,
            Vector3? startPoint, Vector3? endPoint,
            string meshName, PartsIdCounter partsIds)
            => Generate(left, right, beltClosed, flipWinding,
                        profile, profileClosed, capEnds, startPoint, endPoint,
                        meshName, partsIds, null, null);

        /// <summary>
        /// はしごのウェイトを引き継ぐ版。
        ///
        /// 断面点の x は左レール（x=0）から右レール（x=1）への位置なので、
        /// 頂点のウェイトは rung i の左右を x で混ぜたものになる。
        /// leftWeights / rightWeights が null なら何も書かない（従来と同じ）。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, bool flipWinding,
            IReadOnlyList<Vector3> profile, bool profileClosed, bool capEnds,
            Vector3? startPoint, Vector3? endPoint,
            string meshName, PartsIdCounter partsIds,
            IReadOnlyList<BoneWeight?> leftWeights, IReadOnlyList<BoneWeight?> rightWeights,
            PipeGenOptions opt = default)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Pipe" : meshName);

            int n = (left == null || right == null) ? 0 : Mathf.Min(left.Count, right.Count);
            int m = (profile == null) ? 0 : profile.Count;
            if (n < 2 || m < 2) return mo;

            var xDir = new Vector3[n];
            var yDir = new Vector3[n];
            var len  = new float[n];
            BuildFrames(left, right, beltClosed, flipWinding, xDir, yDir, len);

            // 旧方式の上方向（段の両端と次の段の中点が作る三角形の法線）。
            // 現行の上方向と逆を向く段が多ければ、面の巻き順を反転して外向きを保つ。
            bool legacyUpFlipsSide = false;
            if (opt.LegacyFrame)
                legacyUpFlipsSide = ApplyLegacyUp(left, right, beltClosed, endPoint, yDir);

            // 側面の巻き順。閉じた断面（3点以上）が正の向き（符号付き面積 > 0）のときは
            // 筒が内向きになるため反転し、断面をどちら回りで描いても外向きへ揃える。
            // 開いた断面・2点断面は基準ベルトの巻き順のまま
            //（断面 2 点 (0,0)-(1,0) が基準ベルトと同一面になる規約を保つ）。
            bool invertSide = profileClosed && m >= 3 && SignedProfileArea(profile) > 0f;
            bool quadFlip   = flipWinding ^ invertSide ^ legacyUpFlipsSide;

            int   segments = beltClosed ? n : n - 1;
            float den      = beltClosed ? n : n - 1;

            // 頂点: rung i × 断面点 k
            for (int i = 0; i < n; i++)
            {
                float u = i / den;
                BoneWeight? wl = WeightAt(leftWeights,  i);
                BoneWeight? wr = WeightAt(rightWeights, i);

                // 断面を置く原点と各軸の倍率。
                //   現行：原点は左レール。断面は正規化済みなので、各軸に段の長さを掛ける。
                //   旧方式：原点は段の中点。生の断面に「段の長さ × 断面スケール」を掛ける
                //           （負の成分は絶対の大きさ。NCSHAGLIB の CreateAndAddVertex0 と同じ）。
                Vector3 origin = opt.LegacyRawProfile ? (left[i] + right[i]) * 0.5f : left[i];
                float sx = opt.LegacyRawProfile ? LegacyScale(opt.LegacyCutEndScale.x, len[i]) : len[i];
                float sy = opt.LegacyRawProfile ? LegacyScale(opt.LegacyCutEndScale.y, len[i]) : len[i];
                float sz = opt.LegacyRawProfile ? LegacyScale(opt.LegacyCutEndScale.z, len[i]) : len[i];

                // 断面の z は rung 系の 3 つ目の軸（xDir × yDir）へ置く。
                Vector3 zDir = Vector3.Cross(xDir[i], yDir[i]);

                for (int k = 0; k < m; k++)
                {
                    Vector3 p   = profile[k];
                    Vector3 pos = origin + xDir[i] * (p.x * sx) + yDir[i] * (p.y * sy) + zDir * (p.z * sz);

                    // 旧方式の UV は PMX/MQO の向き（上が v=0）で v = 段 / (段数+1) だった。
                    // PolyLing は Unity の向き（下が v=0）で持ち、書き出しで反転するので、ここでは 1 − それを入れる。
                    Vector2 uv = opt.LegacyUV
                        ? new Vector2(k / (float)m, 1f - i / (float)(n + 1))
                        : new Vector2(u, profileClosed ? k / (float)m : k / (float)(m - 1));

                    // ウェイトは rung 上の位置（左 0 → 右 1）で左右を混ぜる。
                    float t = opt.LegacyRawProfile
                        ? (len[i] > 1e-6f ? Mathf.Clamp01(0.5f + p.x * sx / len[i]) : 0.5f)
                        : p.x;

                    var vert = new Vertex(pos, uv);
                    vert.BoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(wl, wr, t);
                    mo.Vertices.Add(vert);
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
            int startApex = -1, endApex = -1;
            if (capEnds && !beltClosed && profileClosed && m >= 3)
            {
                bool headReverse = !quadFlip;
                bool tailReverse =  quadFlip;

                // 先端が与えられていれば、その点へ収束させる三角ファンで閉じる。
                // 収束点は rung のどちらのレールでもないので、左右の真ん中を入れる。
                int capFrom = mo.VertexCount;

                if (startPoint.HasValue)
                {
                    startApex = mo.VertexCount;
                    AddPointCap(mo, 0, m, startPoint.Value, headReverse);
                    if (opt.LegacyUV) mo.Vertices[startApex].UVs[0] = new Vector2(0.5f, 1f);
                }
                else AddCap(mo, 0, m, headReverse);

                FillCapWeights(mo, capFrom,
                    Poly_Ling.UI.SkinWeightOps.LerpNullable(
                        WeightAt(leftWeights, 0), WeightAt(rightWeights, 0), 0.5f));

                capFrom = mo.VertexCount;

                // 終端ぱっつん：毛先の要を最後の輪の平均位置へ置く（NCSHAGLIB の pattsun）。
                Vector3? tip = endPoint;
                if (opt.PattsunEnd && tip.HasValue) tip = RingAverage(mo, (n - 1) * m, m);

                if (tip.HasValue)
                {
                    endApex = mo.VertexCount;
                    AddPointCap(mo, (n - 1) * m, m, tip.Value, tailReverse);
                    if (opt.LegacyUV) mo.Vertices[endApex].UVs[0] = new Vector2(0.5f, 0f);
                }
                else AddCap(mo, (n - 1) * m, m, tailReverse);

                FillCapWeights(mo, capFrom,
                    Poly_Ling.UI.SkinWeightOps.LerpNullable(
                        WeightAt(leftWeights, n - 1), WeightAt(rightWeights, n - 1), 0.5f));
            }

            // 逆方向の控え（ModifierFrameTrace）：輪 i の頂点と先端の要。並べ替えの前に印を付ける。
            int[] frameTag = null;
            if (ModifierFrameTrace.Current != null)
            {
                frameTag = new int[mo.VertexCount];
                for (int v = 0; v < frameTag.Length; v++)
                    frameTag[v] = v < n * m ? v / m : v == startApex ? -1 : v == endApex ? -2 : -3;
            }

            // 旧方式の頂点の並び：始点・断面×段・終点。
            int[] remap = null;
            if (opt.LegacyVertexOrder && (startApex >= 0 || endApex >= 0))
                remap = ReorderLegacy(mo, startApex, endApex);

            if (frameTag != null)
            {
                var ringVerts = new List<int>[n];
                for (int i = 0; i < n; i++) ringVerts[i] = new List<int>(m);
                int newStart = -1, newEnd = -1;
                for (int v = 0; v < frameTag.Length; v++)
                {
                    int nv = remap != null ? remap[v] : v;
                    int tag = frameTag[v];
                    if (tag >= 0) ringVerts[tag].Add(nv);
                    else if (tag == -1) newStart = nv;
                    else if (tag == -2) newEnd = nv;
                }
                for (int i = 0; i < n; i++) ModifierFrameTrace.RecordVertices(mo, ringVerts[i], i);
                if (newStart >= 0) ModifierFrameTrace.RecordVertices(mo, new[] { newStart }, 0, ModifierFrameKind.StartApex);
                if (newEnd   >= 0) ModifierFrameTrace.RecordVertices(mo, new[] { newEnd },   0, ModifierFrameKind.EndApex);
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

        /// <summary>段のウェイトを引く。無ければ null。</summary>
        private static BoneWeight? WeightAt(IReadOnlyList<BoneWeight?> list, int i)
            => (list != null && i >= 0 && i < list.Count) ? list[i] : null;

        /// <summary>蓋で増えた頂点（収束点）へウェイトを入れる。増えていなければ何もしない。</summary>
        private static void FillCapWeights(MeshObject mo, int from, BoneWeight? w)
        {
            for (int v = from; v < mo.VertexCount; v++)
                if (mo.Vertices[v] != null) mo.Vertices[v].BoneWeight = w;
        }

        /// <summary>断面の符号付き面積（xy で求める）。正 = 正の向き（この向きだと筒が内向きになる）。</summary>
        private static float SignedProfileArea(IReadOnlyList<Vector3> profile)
        {
            int m = profile.Count;
            float sum = 0f;
            for (int k = 0; k < m; k++)
            {
                Vector3 p = profile[k];
                Vector3 q = profile[(k + 1) % m];
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
        // 旧ツール互換（NCSHAGLIB の髪パイプ生成）
        // ================================================================

        /// <summary>
        /// yDir を旧方式の上方向で置き換える。
        /// 段 i の上方向 = normalize(cross(v0 − c, R[i] − c))。c は段の中点、v0 は次の段の中点
        /// （最後の段は終点。終点が無ければ前の段からの延長）。NCSHAGLIB の CreateBaseTriangles /
        /// CreateAndAddVertex0 と同じ式。縮退した段は現行の上方向のまま残す。
        /// </summary>
        /// <returns>現行の上方向と逆を向く段が多ければ true（呼び出し側が巻き順を反転して外向きを保つ）。</returns>
        private static bool ApplyLegacyUp(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool beltClosed, Vector3? endPoint, Vector3[] yDir)
        {
            int n = yDir.Length;
            int opposite = 0, same = 0;

            for (int i = 0; i < n; i++)
            {
                Vector3 c = (left[i] + right[i]) * 0.5f;
                Vector3 v0;
                if (i < n - 1)        v0 = (left[i + 1] + right[i + 1]) * 0.5f;
                else if (beltClosed)  v0 = (left[0] + right[0]) * 0.5f;
                else if (endPoint.HasValue) v0 = endPoint.Value;
                else
                {
                    Vector3 prev = (left[i - 1] + right[i - 1]) * 0.5f;
                    v0 = c + (c - prev);
                }

                // 縮退の判定は辺の長さに対する比で行う（髪の根元のように段が 1mm 前後だと、
                // 外積の大きさそのものは 1e-12 を下回る）。
                Vector3 a  = v0 - c, b = right[i] - c;
                Vector3 up = Vector3.Cross(a, b);
                float scale = a.sqrMagnitude * b.sqrMagnitude;
                if (scale <= 0f || up.sqrMagnitude <= scale * 1e-12f) continue;
                up /= Mathf.Sqrt(up.sqrMagnitude);

                if (Vector3.Dot(up, yDir[i]) < 0f) opposite++; else same++;
                yDir[i] = up;
            }
            return opposite > same;
        }

        /// <summary>旧方式の断面スケール 1 成分。0 以上は段の長さに対する倍率、負は絶対の大きさ。</summary>
        private static float LegacyScale(float e, float rungLength)
            => e >= 0f ? rungLength * e : -e;

        /// <summary>baseIdx から m 個の輪の頂点の平均位置。</summary>
        private static Vector3 RingAverage(MeshObject mo, int baseIdx, int m)
        {
            Vector3 sum = Vector3.zero;
            for (int k = 0; k < m; k++) sum += mo.Vertices[baseIdx + k].Position;
            return sum / m;
        }

        /// <summary>
        /// 頂点を「始点・断面×段・終点」の順に並べ替える。面の頂点番号も付け替える。
        /// 始点・終点の要が無い側はそのまま（輪の頂点の並びは変えない）。
        /// </summary>
        /// <returns>元の番号 → 新しい番号。</returns>
        private static int[] ReorderLegacy(MeshObject mo, int startApex, int endApex)
        {
            int count = mo.Vertices.Count;
            var order = new List<int>(count);
            if (startApex >= 0) order.Add(startApex);
            for (int v = 0; v < count; v++)
                if (v != startApex && v != endApex) order.Add(v);
            if (endApex >= 0) order.Add(endApex);

            var map   = new int[count];
            var verts = new List<Vertex>(count);
            for (int i = 0; i < order.Count; i++)
            {
                map[order[i]] = i;
                verts.Add(mo.Vertices[order[i]]);
            }

            mo.Vertices.Clear();
            mo.Vertices.AddRange(verts);

            for (int f = 0; f < mo.FaceCount; f++)
            {
                var vi = mo.Faces[f]?.VertexIndices;
                if (vi == null) continue;
                for (int j = 0; j < vi.Count; j++) vi[j] = map[vi[j]];
            }
            return map;
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
