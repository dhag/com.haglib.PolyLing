// FrillMeshGenerator.cs
// 基準ベルト（梯子状の四角形群）＋断面プロファイルからフリルメッシュを生成する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【構成】梯子を縦に置いた見立てで、rung 1区間（1ステップ）ごとに同じ波形を繰り返す。
//   左右の手すり（レール）それぞれに、その区間長で正規化した波形を置き、面でつなぐ。
//   左右のレール長が等しければ平坦なリボン、異なればスカートのフリル状になる。
//
// 【プロファイルの座標系】ステップ s（rung s → rung s+1）ごとに次の系で解釈する。
//   X = 進行方向（x=0 が rung s、x=1 が rung s+1）
//   Y = 基準ベルトの面法線方向（そのレール区間長で正規化。y=1 が区間長）
//   位置は p0 + dir * x + nrm * (y * len)。x も y も同じ len で拡大されるため、
//   区間が長いほど波形は相似形のまま大きくなる。
//
// 【巻き順】取り込み時に判定した基準ベルトの巻き順に従う。
//   断面が 2 点 (0,0)-(1,0) のときは基準ベルトと同一の面になる。
//
// 【共有レールの接続】connectShared = true のとき、同一のレール線分
//   （縦置きなら左右、横置きなら上下）を共有する梯子どうしを溶接する。
//   レール線分ごとに面法線を加算し、正規化和を使って頂点を1度だけ生成する。
//   レール線分は始点→終点の向きまで一致した場合のみ共有する
//   （逆向きではプロファイル x=0 の位置が入れ替わり、生成点が一致しないため）。
//
// 【rung 境界の段差】ステップ s の終端（プロファイル index m-1）と
//   ステップ s+1 の始端（index 0）は、プロファイル両端の y が違う／梯子が曲がっている／
//   rung 間隔が不均一、のいずれかでずれる。
//   FrillRungSeam.Merge は両者の生成位置を平均して1頂点にまとめ、段差を消す。
//   FrillRungSeam.Split は別頂点のまま残し、段差をそのまま出す。
//
// 【2プロファイル】twoProfiles = true のとき、レールごとに補間パラメータ t を持ち、
//   プロファイルを Lerp(A[k], B[k], t) で解決する。梯子の TLeft / TRight が各レールの t。
//   段グループなら t=0 側の最外レールが A、t=1 側の最外レールが B、中間の段は線形補間になる。
//   A と B の点数が違うときは点数の少ない側を両方に使う。
//   t はレールキーにも含めるため、位置が同じでも t が違うレールは溶接されない
//   （縦に閉じた形では、そこが上下を分ける裂け目になる）。
//
// 【高さ倍率】FrillBeltInput.HeightScale は法線方向成分 (y * len) だけに掛ける。
//   進行方向成分 (x) には掛けないため、レール上の位置は変えずに波の高さだけが変わる。
//   connectShared で梯子どうしがレール線分を共有した場合、法線を合成するのと同じく
//   倍率も寄与した梯子ぶんを平均する（1本のレールに1つの高さしか持てないため）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Frill
{
    /// <summary>複数梯子をまとめて生成するときの梯子1本ぶんの入力。</summary>
    public sealed class FrillBeltInput
    {
        public IReadOnlyList<Vector3> Left;
        public IReadOnlyList<Vector3> Right;
        public bool Closed;
        public bool FlipWinding;

        /// <summary>この梯子の高さ倍率（法線方向成分に掛ける）。1 で従来どおり。</summary>
        public float HeightScale = 1f;

        /// <summary>左レールのプロファイル補間パラメータ（0 = A / 1 = B）。</summary>
        public float TLeft = 0f;

        /// <summary>右レールのプロファイル補間パラメータ（0 = A / 1 = B）。</summary>
        public float TRight = 1f;
    }

    public static class FrillMeshGenerator
    {
        /// <summary>位置キーの量子化幅。</summary>
        private const float PosEps = 1e-5f;

        /// <summary>プロファイル補間パラメータ t の量子化幅。</summary>
        private const float TEps = 1e-4f;

        // ================================================================
        // 単一梯子（既存API・挙動そのまま）
        // ================================================================

        /// <summary>
        /// フリルメッシュを生成する。基準ベルトまたは断面が不足していれば空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right,
            bool closed, bool flipWinding,
            IReadOnlyList<Vector2> profile, string meshName)
        {
            var one = new List<FrillBeltInput>(1)
            {
                new FrillBeltInput { Left = left, Right = right, Closed = closed, FlipWinding = flipWinding }
            };
            return Generate(one, profile, false, FrillRungSeam.Split, meshName);
        }

        // ================================================================
        // 複数梯子
        // ================================================================

        /// <summary>
        /// 複数の基準ベルトからフリルメッシュを1つ生成する。
        /// connectShared = true のとき、同一向きのレール線分を共有する梯子どうしを溶接する。
        /// seam = Merge のとき、rung 境界の頂点を平均位置で1つにまとめる。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<FrillBeltInput> belts,
            IReadOnlyList<Vector2> profile,
            bool connectShared,
            FrillRungSeam seam,
            string meshName)
            => Generate(belts, profile, null, false, connectShared, seam, meshName);

        /// <summary>
        /// 断面プロファイルを A / B の2本にできる版。
        /// twoProfiles = false なら profileA だけを使い、従来と同じ結果になる。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<FrillBeltInput> belts,
            IReadOnlyList<Vector2> profileA,
            IReadOnlyList<Vector2> profileB,
            bool twoProfiles,
            bool connectShared,
            FrillRungSeam seam,
            string meshName)
            => Generate(belts, profileA, profileB, twoProfiles, connectShared, seam, meshName, null);

        /// <summary>
        /// パーツIDを採番する版。partsIds が null なら書かない。
        ///
        /// 【非融合】梯子1本＝パーツ1つ。左右レールとも同じIDになる。
        /// 【融合】  レール行（溶接後に1本になったレール）ごとにパーツを分ける。
        ///   段0上 = 0、段0下（= 段1上）= 1、… 最後の段の下レール = 段数 となり、
        ///   梯子1本の並びなら「段数 + 1」個のパーツが出来る。
        ///   位置・向き・プロファイル補間パラメータが一致せず溶接されなかった
        ///   レール行は、その梯子ぶんだけ独立したパーツになる。
        ///
        /// サブIDはここでは触らない。厚み付けや重複頂点の結合で頂点数が変わるため、
        /// メッシュ確定後に PrimitiveMeshPostProcess.AssignSubIdByPartsId で振り直す。
        /// </summary>
        public static MeshObject Generate(
            IReadOnlyList<FrillBeltInput> belts,
            IReadOnlyList<Vector2> profileA,
            IReadOnlyList<Vector2> profileB,
            bool twoProfiles,
            bool connectShared,
            FrillRungSeam seam,
            string meshName,
            PartsIdCounter partsIds)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Frill" : meshName);

            int ca = (profileA == null) ? 0 : profileA.Count;
            int cb = (profileB == null) ? 0 : profileB.Count;

            bool two = twoProfiles && ca >= 2 && cb >= 2;

            var pa = profileA;
            var pb = profileA;

            if (two)
            {
                // 点数が違うときは点数の少ない側を両方に使う。
                if (ca == cb)      { pb = profileB; }
                else if (ca < cb)  { pb = profileA; two = false; }
                else               { pa = profileB; pb = profileB; two = false; }
            }

            int m = (pa == null) ? 0 : pa.Count;
            if (belts == null || belts.Count == 0 || m < 2) return mo;

            var steps = BuildSteps(belts);
            if (steps.Count == 0) return mo;

            // ── パス1: レール記録を作り、面法線を合成する ──
            var rails     = new List<RailRec>();
            var railIndex = connectShared ? new Dictionary<RailKey, int>() : null;
            var stepRailL = new int[steps.Count];
            var stepRailR = new int[steps.Count];

            // パーツIDの割り当て単位 → パーツID。
            // 融合ありはレール行（梯子index, 左右の別）単位、融合なしは梯子単位。
            var rowParts = new Dictionary<long, int>();

            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                stepRailL[i] = GetOrAddRail(rails, railIndex, connectShared, two, st.A0, st.A1, st, 0f, st.TA,
                                            rowParts, partsIds, 0);
                stepRailR[i] = GetOrAddRail(rails, railIndex, connectShared, two, st.B0, st.B1, st, 1f, st.TB,
                                            rowParts, partsIds, 1);
            }

            foreach (var r in rails)
            {
                r.Normal = r.NormalSum.sqrMagnitude > 1e-12f ? r.NormalSum.normalized : r.FirstNormal;
                r.HeightScale = r.HeightCount > 0 ? r.HeightSum / r.HeightCount : 1f;
            }

            // ── パス2: rung 境界の平均位置を求める ──
            Dictionary<PosKey, Vector3> boundarySum   = null;
            Dictionary<PosKey, int>     boundaryCount = null;

            if (seam == FrillRungSeam.Merge)
            {
                boundarySum   = new Dictionary<PosKey, Vector3>();
                boundaryCount = new Dictionary<PosKey, int>();

                foreach (var r in rails)
                {
                    Vector3 dir = r.P1 - r.P0;
                    float   len = dir.magnitude;

                    AccumBoundary(boundarySum, boundaryCount, new PosKey(r.P0, r.Scope, r.TKey),
                                  ProfilePos(r.P0, dir, len, r.Normal,
                                             ProfileAt(pa, pb, two, 0, r.T), r.HeightScale));
                    AccumBoundary(boundarySum, boundaryCount, new PosKey(r.P1, r.Scope, r.TKey),
                                  ProfilePos(r.P0, dir, len, r.Normal,
                                             ProfileAt(pa, pb, two, m - 1, r.T), r.HeightScale));
                }
            }

            // ── パス3: 頂点生成 ──
            var boundaryVert = (seam == FrillRungSeam.Merge) ? new Dictionary<PosKey, int>() : null;

            foreach (var r in rails)
            {
                r.Verts = new int[m];

                Vector3 dir = r.P1 - r.P0;
                float   len = dir.magnitude;

                for (int k = 0; k < m; k++)
                {
                    Vector2 p  = ProfileAt(pa, pb, two, k, r.T);
                    Vector2 uv = new Vector2(r.U0 + p.x * r.UStep, r.V);

                    bool isStart = (k == 0);
                    bool isEnd   = (k == m - 1);

                    if (seam == FrillRungSeam.Merge && (isStart || isEnd))
                    {
                        var bk = new PosKey(isStart ? r.P0 : r.P1, r.Scope, r.TKey);
                        if (boundaryVert.TryGetValue(bk, out int reuse)) { r.Verts[k] = reuse; continue; }

                        Vector3 avg = boundarySum[bk] / boundaryCount[bk];
                        r.Verts[k] = mo.VertexCount;
                        var bv = new Vertex(avg, uv);
                        if (partsIds != null) bv.PartsId = r.PartsId;
                        mo.Vertices.Add(bv);
                        boundaryVert[bk] = r.Verts[k];
                        continue;
                    }

                    r.Verts[k] = mo.VertexCount;
                    var nv = new Vertex(ProfilePos(r.P0, dir, len, r.Normal, p, r.HeightScale), uv);
                    if (partsIds != null) nv.PartsId = r.PartsId;
                    mo.Vertices.Add(nv);
                }
            }

            // ── 面 ──
            for (int i = 0; i < steps.Count; i++)
            {
                var li = rails[stepRailL[i]].Verts;
                var ri = rails[stepRailR[i]].Verts;
                bool flip = steps[i].FlipWinding;

                for (int k = 0; k < m - 1; k++)
                {
                    if (flip) mo.AddQuad(li[k], li[k + 1], ri[k + 1], ri[k]);
                    else      mo.AddQuad(li[k], ri[k], ri[k + 1], li[k + 1]);
                }
            }

            mo.RecalculateNormals();
            return mo;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 断面プロファイル点 p を実座標へ写す。
        /// heightScale は法線方向成分だけに掛ける（進行方向はレール上の位置なので変えない）。
        /// </summary>
        private static Vector3 ProfilePos(
            Vector3 p0, Vector3 dir, float len, Vector3 nrm, Vector2 p, float heightScale)
            => p0 + dir * p.x + nrm * (p.y * len * heightScale);

        /// <summary>レールの補間パラメータ t で断面プロファイル点を解決する。</summary>
        private static Vector2 ProfileAt(
            IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b, bool two, int k, float t)
            => two ? Vector2.Lerp(a[k], b[k], t) : a[k];

        /// <summary>t の量子化。2プロファイル無効時は 0 固定にして従来と同じキーにする。</summary>
        private static long TKeyOf(bool two, float t) => two ? (long)Mathf.Round(t / TEps) : 0L;

        /// <summary>1ステップぶんの生成情報。</summary>
        private struct StepInfo
        {
            public Vector3 A0, A1;   // 左レール線分
            public Vector3 B0, B1;   // 右レール線分
            public Vector3 Normal;   // このステップの基準面法線
            public float   HeightScale; // この梯子の高さ倍率
            public float   TA, TB;   // 左右レールのプロファイル補間パラメータ
            public bool    FlipWinding;
            public float   U0;       // UV の u（プロファイル x=0 のとき）
            public float   UStep;    // UV の u の1ステップぶん
            public int     BeltIndex;
        }

        /// <summary>レール線分1本ぶんの生成記録。</summary>
        private sealed class RailRec
        {
            public Vector3 P0, P1;
            public Vector3 NormalSum;
            public Vector3 FirstNormal;
            public Vector3 Normal;
            public float   HeightSum;    // 寄与した各ステップの高さ倍率の和
            public int     HeightCount;  // 寄与したステップ数
            public float   HeightScale;  // 確定値（HeightSum / HeightCount）
            public float   T;            // プロファイル補間パラメータ
            public long    TKey;         // T の量子化値（キー用）
            public float   U0, UStep, V;
            public int     Scope;    // 境界溶接のスコープ（共有あり=0 / 共有なし=梯子index）
            public int     PartsId;  // このレールの頂点へ書くパーツID
            public int[]   Verts;
        }

        /// <summary>全梯子のステップを1本のリストに展開する。</summary>
        private static List<StepInfo> BuildSteps(IReadOnlyList<FrillBeltInput> belts)
        {
            var list = new List<StepInfo>();

            for (int bi = 0; bi < belts.Count; bi++)
            {
                var belt = belts[bi];
                if (belt == null || belt.Left == null || belt.Right == null) continue;

                int n = Mathf.Min(belt.Left.Count, belt.Right.Count);
                if (n < 2) continue;

                int stepCount = belt.Closed ? n : n - 1;
                if (stepCount < 1) continue;

                for (int s = 0; s < stepCount; s++)
                {
                    int j = (s + 1) % n;

                    Vector3 a0 = belt.Left[s],  a1 = belt.Left[j];
                    Vector3 b0 = belt.Right[s], b1 = belt.Right[j];

                    list.Add(new StepInfo
                    {
                        A0          = a0,
                        A1          = a1,
                        B0          = b0,
                        B1          = b1,
                        Normal      = StepNormal(a0, b0, b1, a1, belt.FlipWinding),
                        HeightScale = belt.HeightScale,
                        TA          = belt.TLeft,
                        TB          = belt.TRight,
                        FlipWinding = belt.FlipWinding,
                        U0          = (float)s / stepCount,
                        UStep       = 1f / stepCount,
                        BeltIndex   = bi,
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// レール記録を取得または追加する。
        /// 共有ありのときは同一キーへ法線を加算し、既存の記録を使い回す。
        /// </summary>
        private static int GetOrAddRail(
            List<RailRec> rails, Dictionary<RailKey, int> railIndex, bool connectShared, bool two,
            Vector3 p0, Vector3 p1, StepInfo st, float v, float t,
            Dictionary<long, int> rowParts, PartsIdCounter partsIds, int side)
        {
            long tKey = TKeyOf(two, t);

            // 融合ありはレール行（梯子index, 左右の別）単位、融合なしは梯子単位でパーツを分ける。
            long rowKey = connectShared ? ((long)st.BeltIndex * 2 + side) : st.BeltIndex;

            if (connectShared)
            {
                var key = new RailKey(p0, p1, tKey);
                if (railIndex.TryGetValue(key, out int idx))
                {
                    rails[idx].NormalSum   += st.Normal;
                    rails[idx].HeightSum   += st.HeightScale;
                    rails[idx].HeightCount += 1;

                    // 溶接で相手のレールを使う行も、同じパーツIDとして覚えておく。
                    // 同じ行の別ステップが溶接されず新規レールになったとき、同じIDを使うため。
                    if (partsIds != null && !rowParts.ContainsKey(rowKey))
                        rowParts[rowKey] = rails[idx].PartsId;

                    return idx;
                }

                idx = rails.Count;
                var newRec = NewRail(p0, p1, st, v, 0, t, tKey);
                newRec.PartsId = ResolvePartsId(rowParts, partsIds, rowKey);
                rails.Add(newRec);
                railIndex[key] = idx;
                return idx;
            }

            var rec = NewRail(p0, p1, st, v, st.BeltIndex, t, tKey);
            rec.PartsId = ResolvePartsId(rowParts, partsIds, rowKey);
            rails.Add(rec);
            return rails.Count - 1;
        }

        /// <summary>
        /// 割り当て単位のパーツIDを引く。初出なら採番する。partsIds が null なら 0 を返す。
        /// </summary>
        private static int ResolvePartsId(
            Dictionary<long, int> rowParts, PartsIdCounter partsIds, long rowKey)
        {
            if (partsIds == null) return 0;
            if (rowParts.TryGetValue(rowKey, out int id)) return id;

            id = partsIds.Take();
            rowParts[rowKey] = id;
            return id;
        }

        private static RailRec NewRail(
            Vector3 p0, Vector3 p1, StepInfo st, float v, int scope, float t, long tKey)
            => new RailRec
            {
                P0          = p0,
                P1          = p1,
                NormalSum   = st.Normal,
                FirstNormal = st.Normal,
                HeightSum   = st.HeightScale,
                HeightCount = 1,
                T           = t,
                TKey        = tKey,
                U0          = st.U0,
                UStep       = st.UStep,
                V           = v,
                Scope       = scope,
            };

        private static void AccumBoundary(
            Dictionary<PosKey, Vector3> sum, Dictionary<PosKey, int> count, PosKey key, Vector3 pos)
        {
            if (sum.TryGetValue(key, out Vector3 cur)) sum[key] = cur + pos;
            else                                       sum[key] = pos;

            count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
        }

        /// <summary>
        /// ステップの基準面法線。基準ベルトの巻き順 (a0, b0, b1, a1) / 反転時 (a0, a1, b1, b0) で算出する。
        /// </summary>
        private static Vector3 StepNormal(Vector3 a0, Vector3 b0, Vector3 b1, Vector3 a1, bool flipWinding)
        {
            return flipWinding
                ? NormalHelper.CalculateFaceNormal(a0, a1, b1)
                : NormalHelper.CalculateFaceNormal(a0, b0, b1);
        }

        private static long Q(float f) => (long)Mathf.Round(f / PosEps);

        // ================================================================
        // キー
        // ================================================================

        /// <summary>
        /// 量子化した始点→終点の順序付きペア＋プロファイル補間パラメータ。
        /// 向きが逆なら別キーになる。t が違うレールも別キーになる（裂け目）。
        /// </summary>
        private readonly struct RailKey : System.IEquatable<RailKey>
        {
            private readonly long _x0, _y0, _z0, _x1, _y1, _z1, _t;

            public RailKey(Vector3 p0, Vector3 p1, long tKey)
            {
                _x0 = Q(p0.x); _y0 = Q(p0.y); _z0 = Q(p0.z);
                _x1 = Q(p1.x); _y1 = Q(p1.y); _z1 = Q(p1.z);
                _t  = tKey;
            }

            public bool Equals(RailKey o)
                => _x0 == o._x0 && _y0 == o._y0 && _z0 == o._z0
                && _x1 == o._x1 && _y1 == o._y1 && _z1 == o._z1
                && _t  == o._t;

            public override bool Equals(object obj) => obj is RailKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    long h = _x0;
                    h = h * 31 + _y0;
                    h = h * 31 + _z0;
                    h = h * 31 + _x1;
                    h = h * 31 + _y1;
                    h = h * 31 + _z1;
                    h = h * 31 + _t;
                    return (int)(h ^ (h >> 32));
                }
            }
        }

        /// <summary>量子化したレール頂点位置 + 溶接スコープ + プロファイル補間パラメータ。</summary>
        private readonly struct PosKey : System.IEquatable<PosKey>
        {
            private readonly long _x, _y, _z, _t;
            private readonly int  _scope;

            public PosKey(Vector3 p, int scope, long tKey)
            {
                _x = Q(p.x); _y = Q(p.y); _z = Q(p.z);
                _t = tKey;
                _scope = scope;
            }

            public bool Equals(PosKey o)
                => _x == o._x && _y == o._y && _z == o._z
                && _t == o._t && _scope == o._scope;

            public override bool Equals(object obj) => obj is PosKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    long h = _x;
                    h = h * 31 + _y;
                    h = h * 31 + _z;
                    h = h * 31 + _t;
                    h = h * 31 + _scope;
                    return (int)(h ^ (h >> 32));
                }
            }
        }
    }
}
