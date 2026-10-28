// SkinWeightHeatOps.cs
// ボーンヒートによる自動ウェイト（Blender の「自動のウェイトで」と同じ方式）。
//
// 【出典】
//   Blender source/blender/editors/armature/meshlaplacian.cc の heat_bone_weighting。
//   元は Baran & Popović "Automatic Rigging and Animation of 3D Characters"（SIGGRAPH 2007）第 4 節。
//   ラプラシアンの重みは Meyer et al. 2002（3.5 節 式(8)）を Blender が非多様体向けに手直ししたもの。
//
// 【解く式】ボーン j ごとに (L + H) w_j = H p_j
//   L … コタンジェント・ラプラシアン（行を頂点面積でスケール）
//   H … 対角。最も近い「見える」ボーンの数 / 距離²（見えるボーンが無ければ 0）
//   p_j … ボーン j が頂点の最近傍の一つなら 1/最近傍数、そうでなければ 0
//   定数・距離の補正・可視判定・低ウェイトの圧縮は Blender と同じ値を使う。
//
// 【Blender との違い】
//   ・ボーンにテールが無いので、関節から「階層を下って最初に出会う、使うボーン」の関節までを線分にする
//     （使うボーンを Humanoid などに絞ったとき、間の部品のボーンを飛ばすため）。
//     先が複数あれば先ごとに線分を作り同じボーンへ足す（Blender の B-Bone の区分と同じ足し方）。
//     子が無いボーンは関節の点。
//   ・Blender の行は「頂点面積の逆数」で拡大されていて対称でないため、各行に 2×頂点面積を掛けて
//     対称にし、共役勾配法（対角前処理）で解く。解は同じ。
//     頂点面積が 0 の頂点（三角形に属さない頂点）はラプラシアンの項が無く、w = p（H>0 のとき）か 0 に決まるので、
//     未知数から外して既知の値として右辺へ移す。
//   ・PolyLing のウェイトは 4 本までなので、ボーンごとの値を集めて大きい順に 4 本残し、合計 1 に正規化する。
//     どのボーンも 0 になった頂点（どのボーンからも届かない部分）は書き換えない。
//
// 【座標】範囲塗りと同じくバインド空間（頂点の格納値と BindPose⁻¹ の原点）。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Ops
{
    /// <summary>ボーンヒートで使うボーンの範囲。</summary>
    public enum HeatBoneScope
    {
        /// <summary>IK ボーンを除く全ボーン。</summary>
        AllExceptIK = 0,
        /// <summary>Humanoid に割り当てたボーンだけ。</summary>
        Humanoid    = 1,
        /// <summary>選択中のボーンだけ。</summary>
        Selected    = 2,
    }

    /// <summary>
    /// 直近のボーンヒートの結果（パネルの結果表示用。PlayerCommandDispatcher が持つ）。
    /// </summary>
    public sealed class HeatRunResult
    {
        public bool   Success;
        /// <summary>失敗理由。成功時は null。</summary>
        public string Reason;
        public int    Vertices;
        public int    Meshes;
        public int    Unreached;
        public int    Bones;

        public string Summary => Success
            ? $"完了：{Meshes} 個の描画オブジェクト・{Vertices} 頂点を書き換えました（使ったボーン {Bones} 本"
              + (Unreached > 0 ? $"、どのボーンからも届かず変えなかった頂点 {Unreached}）" : "）")
            : $"失敗：{(string.IsNullOrEmpty(Reason) ? "理由不明" : Reason)}";
    }

    /// <summary>ボーンヒートの熱源（ボーンの線分。根元と先端が同じなら点）。</summary>
    public struct HeatBoneSource
    {
        /// <summary>ボーンの masterIndex。</summary>
        public int     Bone;
        public Vector3 Root;
        public Vector3 Tip;
    }

    /// <summary>1 メッシュの計算結果（まだ書き込んでいない）。</summary>
    public sealed class HeatWeightResult
    {
        public MeshContext Mesh;
        /// <summary>頂点ごとの新しいウェイト。書き換えない頂点は null。</summary>
        public BoneWeight?[] Weights;
        /// <summary>書き換える頂点数。</summary>
        public int Written;
        /// <summary>どのボーンからも届かず書き換えない頂点数（対象頂点のうち）。</summary>
        public int Unreached;
    }

    public static class SkinWeightHeatOps
    {
        // Blender meshlaplacian.cc と同じ値
        private const float C_WEIGHT           = 1.0f;
        private const float WEIGHT_LIMIT_START = 0.05f;
        private const float WEIGHT_LIMIT_END   = 0.025f;
        private const float DISTANCE_EPSILON   = 1e-4f;

        private const float  Epsilon     = 1e-6f;
        private const double CgTolerance = 1e-8;

        // ================================================================
        // 熱源（ボーンの線分）
        // ================================================================

        /// <summary>
        /// 範囲から使うボーンを決める。Selected は selectedBones（ModelContext.SelectedBoneIndices）を使う。
        /// </summary>
        public static bool TryResolveScope(
            ModelContext model, HeatBoneScope scope, IList<int> selectedBones,
            out int[] bones, out string error)
        {
            bones = null;
            error = null;
            if (model == null) { error = "モデルがありません。"; return false; }

            var list = new List<int>();
            switch (scope)
            {
                case HeatBoneScope.Selected:
                    if (selectedBones != null)
                        foreach (int b in selectedBones)
                        {
                            var mc = model.GetMeshContext(b);
                            if (mc != null && mc.Type == MeshType.Bone && !list.Contains(b)) list.Add(b);
                        }
                    if (list.Count == 0) { error = "ボーンが選択されていません。"; return false; }
                    break;

                case HeatBoneScope.Humanoid:
                    var map = model.HumanoidMapping;
                    if (map == null || map.IsEmpty) { error = "Humanoid が割り当てられていません。"; return false; }
                    foreach (var kv in map.BoneIndexMap)
                    {
                        var mc = model.GetMeshContext(kv.Value);
                        if (mc != null && mc.Type == MeshType.Bone && !list.Contains(kv.Value)) list.Add(kv.Value);
                    }
                    if (list.Count == 0) { error = "Humanoid に割り当てたボーンが見つかりません。"; return false; }
                    break;

                default:
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        if (mc.MeshObject?.IKData != null) continue;   // 非 null ⇔ IK ボーン（MeshObject.cs）
                        list.Add(i);
                    }
                    if (list.Count == 0) { error = "使えるボーンがありません。"; return false; }
                    break;
            }
            list.Sort();
            bones = list.ToArray();
            return true;
        }

        /// <summary>
        /// 使うボーンから熱源を作る。線分の先は「階層を下って最初に出会う、使うボーン」の関節。
        /// 使うボーンを絞ったとき（Humanoid だけなど）、間にある使わないボーン（部品のボーンなど）を
        /// 飛ばして次の関節まで線分を伸ばすため。先が見つからないボーンは関節の点。
        /// </summary>
        public static bool TryBuildSources(
            ModelContext model, int[] bones, out List<HeatBoneSource> sources, out string error)
        {
            sources = new List<HeatBoneSource>();
            error   = null;
            if (model == null) { error = "モデルがありません。"; return false; }
            if (bones == null || bones.Length == 0) { error = "使えるボーンがありません。"; return false; }

            var use = new HashSet<int>();
            foreach (int b in bones)
            {
                var mc = model.GetMeshContext(b);
                if (mc == null || mc.Type != MeshType.Bone) { error = $"{b} 番はボーンではありません。"; return false; }
                use.Add(b);
            }

            // 階層の子の一覧（オブジェクト全種。ボーンの間に描画オブジェクトが挟まっていても辿れる）
            var children = new Dictionary<int, List<int>>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var c = model.GetMeshContext(i);
                if (c == null) continue;
                int p = c.HierarchyParentIndex;
                if (p < 0) continue;
                if (!children.TryGetValue(p, out var l)) children[p] = l = new List<int>();
                l.Add(i);
            }

            var tips  = new List<int>();
            var stack = new Stack<int>();
            var seen  = new HashSet<int>();
            foreach (int b in bones)
            {
                Vector3 j = SkinWeightVolumeOps.BindJointPosition(model.GetMeshContext(b));

                tips.Clear(); stack.Clear(); seen.Clear();
                if (children.TryGetValue(b, out var first)) foreach (int c in first) stack.Push(c);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    if (!seen.Add(c)) continue;
                    if (use.Contains(c)) { tips.Add(c); continue; }   // 使うボーンで止める
                    if (children.TryGetValue(c, out var next)) foreach (int n in next) stack.Push(n);
                }

                bool any = false;
                foreach (int t in tips)
                {
                    Vector3 tj = SkinWeightVolumeOps.BindJointPosition(model.GetMeshContext(t));
                    if ((tj - j).sqrMagnitude <= Epsilon * Epsilon) continue;
                    sources.Add(new HeatBoneSource { Bone = b, Root = j, Tip = tj });
                    any = true;
                }
                if (!any) sources.Add(new HeatBoneSource { Bone = b, Root = j, Tip = j });
            }
            return true;
        }

        // ================================================================
        // 1 メッシュの計算
        // ================================================================

        /// <summary>
        /// 1 メッシュのウェイトを計算する（データは変えない）。解けなければ false と理由。
        /// </summary>
        public static bool TryCompute(
            MeshContext mc, List<HeatBoneSource> sources, bool selectedOnly,
            out HeatWeightResult result, out string error)
        {
            result = new HeatWeightResult { Mesh = mc };
            error  = null;

            var mo = mc?.MeshObject;
            if (mo == null || mo.SkinKind != SkinKind.Skinned) { error = "スキンドの描画オブジェクトではありません。"; return false; }
            int n = mo.VertexCount;
            result.Weights = new BoneWeight?[n];
            if (n == 0 || sources == null || sources.Count == 0) return true;

            // 書き換える頂点
            var mask = new bool[n];
            if (selectedOnly)
            {
                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) return true;
                foreach (int vi in sel) if (vi >= 0 && vi < n) mask[vi] = true;
            }
            else
            {
                for (int i = 0; i < n; i++) mask[i] = true;
            }

            var pos = new Vector3[n];
            for (int i = 0; i < n; i++) pos[i] = mo.Vertices[i].Position;

            // ── 三角形（扇形分割。3 頂点未満の面は使わない）
            var tris = new List<int>();
            foreach (var f in mo.Faces)
            {
                if (f == null || f.VertexCount < 3) continue;
                var vi = f.VertexIndices;
                for (int k = 1; k < vi.Count - 1; k++)
                {
                    int a = vi[0], b = vi[k], c = vi[k + 1];
                    if (a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n) continue;
                    if (a == b || b == c || a == c) continue;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
            }
            int triCount = tris.Count / 3;

            // ── 頂点法線（三角形の単位法線の和）と三角形に属するか
            var vnor  = new Vector3[n];
            var inTri = new bool[n];
            var tnor  = new Vector3[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
                Vector3 cr = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
                float len = cr.magnitude;
                Vector3 nn = len > 0f ? cr / len : Vector3.zero;
                tnor[t] = nn;
                vnor[a] += nn; vnor[b] += nn; vnor[c] += nn;
                inTri[a] = inTri[b] = inTri[c] = true;
            }
            for (int i = 0; i < n; i++) vnor[i] = vnor[i].sqrMagnitude > 0f ? vnor[i].normalized : Vector3.zero;

            var bvh = new TriBvh(pos, tris, tnor);

            // ── ボーン単位にまとめる
            var boneOrder  = new List<int>();
            var boneOfSrc  = new int[sources.Count];
            var boneSlot   = new Dictionary<int, int>();
            for (int s = 0; s < sources.Count; s++)
            {
                if (!boneSlot.TryGetValue(sources[s].Bone, out int slot))
                {
                    slot = boneOrder.Count;
                    boneOrder.Add(sources[s].Bone);
                    boneSlot[sources[s].Bone] = slot;
                }
                boneOfSrc[s] = slot;
            }

            // ── H・p と各頂点の最近傍の熱源（heat_set_H）
            var H        = new double[n];
            var P        = new double[n];
            var closeStart = new int[n + 1];
            var closeList  = new List<int>();
            for (int i = 0; i < n; i++)
            {
                closeStart[i] = closeList.Count;
                float mindist = 1e10f;
                for (int s = 0; s < sources.Count; s++)
                    mindist = Mathf.Min(mindist, SourceDistance(pos[i], vnor[i], sources[s]));

                int numclosest = 0;
                for (int s = 0; s < sources.Count; s++)
                {
                    float d = SourceDistance(pos[i], vnor[i], sources[s]);
                    if (d > mindist * (1f + DISTANCE_EPSILON)) continue;
                    if (inTri[i] && !bvh.Visible(pos[i], sources[s])) continue;
                    closeList.Add(s);
                    numclosest++;
                }
                if (numclosest > 0)
                {
                    float md = Mathf.Max(mindist, 1e-4f);
                    P[i] = 1.0 / numclosest;
                    H[i] = numclosest * C_WEIGHT / ((double)md * md);
                }
            }
            closeStart[n] = closeList.Count;

            // ── ラプラシアン（laplacian_triangle_area / laplacian_triangle_weights）
            var edgeCount = new Dictionary<long, int>();
            for (int t = 0; t < triCount; t++)
            {
                int a = tris[3 * t], b = tris[3 * t + 1], c = tris[3 * t + 2];
                AddEdge(edgeCount, a, b); AddEdge(edgeCount, b, c); AddEdge(edgeCount, c, a);
            }

            var area = new double[n];
            for (int t = 0; t < triCount; t++)
                AccumulateArea(pos, tris[3 * t], tris[3 * t + 1], tris[3 * t + 2], area);

            // 対称化した行列：非対角 -c_ij、対角 Σc_ij + 2A_i H_i（Blender の行 i に 2A_i を掛けた形）
            var off  = new Dictionary<long, double>();
            var diag = new double[n];
            for (int t = 0; t < triCount; t++)
            {
                int i1 = tris[3 * t], i2 = tris[3 * t + 1], i3 = tris[3 * t + 2];
                double t1 = Cot(pos[i1], pos[i2], pos[i3]) / edgeCount[Key(i2, i3)];
                double t2 = Cot(pos[i2], pos[i3], pos[i1]) / edgeCount[Key(i3, i1)];
                double t3 = Cot(pos[i3], pos[i1], pos[i2]) / edgeCount[Key(i1, i2)];
                // Blender は varea==0 の行の項を 0 にする（行全体が 0 倍）。対称化後も 0 にそろえる。
                bool r1 = area[i1] != 0.0, r2 = area[i2] != 0.0, r3 = area[i3] != 0.0;
                if (r1) diag[i1] += t2 + t3;
                if (r2) diag[i2] += t1 + t3;
                if (r3) diag[i3] += t1 + t2;
                if (r1 && r2) AddOff(off, i1, i2, -t3);
                if (r2 && r3) AddOff(off, i2, i3, -t1);
                if (r3 && r1) AddOff(off, i3, i1, -t2);
            }

            // 未知数（頂点面積が 0 でない頂点）と既知の頂点
            var unknownOf = new int[n];
            int m = 0;
            for (int i = 0; i < n; i++) unknownOf[i] = area[i] != 0.0 ? m++ : -1;

            // CSR（未知数どうしのみ。既知の頂点は面積 0 なので上の行列に列として現れない）
            var rowCount = new int[m + 1];
            foreach (var kv in off)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
                rowCount[unknownOf[a] + 1]++; rowCount[unknownOf[b] + 1]++;
            }
            for (int r = 0; r < m; r++) rowCount[r + 1] += rowCount[r];
            var colIdx = new int[rowCount[m]];
            var vals   = new double[rowCount[m]];
            var fill   = (int[])rowCount.Clone();
            foreach (var kv in off)
            {
                int a = unknownOf[(int)(kv.Key >> 32)], b = unknownOf[(int)(kv.Key & 0xffffffff)];
                colIdx[fill[a]] = b; vals[fill[a]++] = kv.Value;
                colIdx[fill[b]] = a; vals[fill[b]++] = kv.Value;
            }
            var dvec = new double[m];
            var hScaled = new double[n];   // 2A_i H_i
            for (int i = 0; i < n; i++)
            {
                int u = unknownOf[i];
                if (u < 0) continue;
                hScaled[i] = 2.0 * area[i] * H[i];
                dvec[u] = diag[i] + hScaled[i];
            }

            // ── ボーンごとに解く
            var acc = new List<(int bone, float w)>[n];
            var rhs = new double[m];
            var x   = new double[m];
            var cg  = new Cg(m, rowCount, colIdx, vals, dvec);

            for (int bs = 0; bs < boneOrder.Count; bs++)
            {
                System.Array.Clear(rhs, 0, m);
                // 1 つの熱源ごとに H p を足す（同じボーンの線分が最近傍に複数入れば、その数だけ足す）
                var rhsFull = new double[n];
                for (int i = 0; i < n; i++)
                    for (int k = closeStart[i]; k < closeStart[i + 1]; k++)
                        if (boneOfSrc[closeList[k]] == bs) rhsFull[i] += H[i] * P[i];

                for (int i = 0; i < n; i++)
                {
                    int u = unknownOf[i];
                    if (u >= 0) rhs[u] = 2.0 * area[i] * rhsFull[i];
                }

                System.Array.Clear(x, 0, m);
                if (!cg.Solve(rhs, x))
                {
                    error = $"ボーンヒート：{mc.Name} でボーン {boneOrder[bs]} の解が見つかりませんでした。";
                    return false;
                }

                for (int i = 0; i < n; i++)
                {
                    if (!mask[i]) continue;
                    int u = unknownOf[i];
                    double sol = u >= 0 ? x[u] : (H[i] > 0.0 ? rhsFull[i] / H[i] : 0.0);
                    float w = LimitWeight((float)sol);
                    if (w <= 0f) continue;
                    (acc[i] ??= new List<(int, float)>()).Add((boneOrder[bs], w));
                }
            }

            // ── 4 本に絞って正規化
            for (int i = 0; i < n; i++)
            {
                if (!mask[i]) continue;
                var list = acc[i];
                if (list == null || list.Count == 0) { result.Unreached++; continue; }
                list.Sort((a, b) => b.w.CompareTo(a.w));
                var slots = new (int idx, float w)[4];
                float sum = 0f;
                for (int k = 0; k < 4; k++)
                {
                    slots[k] = k < list.Count ? list[k] : (0, 0f);
                    sum += slots[k].w;
                }
                for (int k = 0; k < 4; k++) slots[k].w /= sum;
                result.Weights[i] = SkinWeightOps.Pack(slots);
                result.Written++;
            }
            return true;
        }

        /// <summary>計算結果を書き込む。Undo・同期は呼び出し側。</summary>
        public static int Apply(HeatWeightResult r)
        {
            var mo = r?.Mesh?.MeshObject;
            if (mo == null || r.Weights == null) return 0;
            int count = 0;
            for (int i = 0; i < r.Weights.Length && i < mo.VertexCount; i++)
            {
                if (!r.Weights[i].HasValue) continue;
                mo.Vertices[i].BoneWeight = r.Weights[i].Value;
                count++;
            }
            if (count > 0) mo.RecomputeSkinKind();
            return count;
        }

        // ================================================================
        // Blender と同じ部品
        // ================================================================

        /// <summary>heat_source_distance：線分への距離を、法線がボーンへ向かないほど大きくする。</summary>
        private static float SourceDistance(Vector3 v, Vector3 normal, in HeatBoneSource s)
        {
            Vector3 closest = ClosestOnSegment(v, s.Root, s.Tip);
            Vector3 d = v - closest;
            float dist = d.magnitude;
            Vector3 dn = dist > 0f ? d / dist : Vector3.zero;
            float cosine = Vector3.Dot(dn, normal);
            return dist / (0.5f * (cosine + 1.001f));
        }

        private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 <= 0f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return a + ab * t;
        }

        /// <summary>heat_limit_weight：0.025 未満は 0、0.025〜0.05 は 0〜0.05 へ圧縮。</summary>
        private static float LimitWeight(float w)
        {
            if (w < WEIGHT_LIMIT_END) return 0f;
            if (w < WEIGHT_LIMIT_START)
            {
                float t = (w - WEIGHT_LIMIT_END) / (WEIGHT_LIMIT_START - WEIGHT_LIMIT_END);
                return t * WEIGHT_LIMIT_START;
            }
            return w;
        }

        /// <summary>cotangent_tri_weight_v3：v1 での角のコタンジェント。</summary>
        private static double Cot(Vector3 v1, Vector3 v2, Vector3 v3)
        {
            Vector3 a = v2 - v1, b = v3 - v1;
            float clen = Vector3.Cross(a, b).magnitude;
            return clen > float.Epsilon ? Vector3.Dot(a, b) / clen : 0.0;
        }

        /// <summary>laplacian_triangle_area：頂点面積を足す。</summary>
        private static void AccumulateArea(Vector3[] p, int i1, int i2, int i3, double[] varea)
        {
            Vector3 v1 = p[i1], v2 = p[i2], v3 = p[i3];
            double t1 = Cot(v1, v2, v3), t2 = Cot(v2, v3, v1), t3 = Cot(v3, v1, v2);
            int obtuse = 0;
            if      (Vector3.Angle(v2 - v1, v3 - v1) > 90f) obtuse = 1;
            else if (Vector3.Angle(v1 - v2, v3 - v2) > 90f) obtuse = 2;
            else if (Vector3.Angle(v1 - v3, v2 - v3) > 90f) obtuse = 3;

            if (obtuse > 0)
            {
                double ar = 0.5 * Vector3.Cross(v2 - v1, v3 - v1).magnitude;
                varea[i1] += obtuse == 1 ? ar : ar * 0.5;
                varea[i2] += obtuse == 2 ? ar : ar * 0.5;
                varea[i3] += obtuse == 3 ? ar : ar * 0.5;
            }
            else
            {
                double l1 = (v2 - v3).sqrMagnitude, l2 = (v1 - v3).sqrMagnitude, l3 = (v1 - v2).sqrMagnitude;
                t1 *= l1; t2 *= l2; t3 *= l3;
                varea[i1] += (t2 + t3) * 0.25;
                varea[i2] += (t1 + t3) * 0.25;
                varea[i3] += (t1 + t2) * 0.25;
            }
        }

        private static long Key(int a, int b)
            => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static void AddEdge(Dictionary<long, int> d, int a, int b)
        {
            long k = Key(a, b);
            d[k] = d.TryGetValue(k, out int c) ? c + 1 : 1;
        }

        private static void AddOff(Dictionary<long, double> d, int a, int b, double v)
        {
            long k = Key(a, b);
            d[k] = d.TryGetValue(k, out double c) ? c + v : v;
        }

        // ================================================================
        // 共役勾配法（対角前処理）
        // ================================================================

        private sealed class Cg
        {
            private readonly int _n;
            private readonly int[] _rows, _cols;
            private readonly double[] _vals, _diag, _invDiag, _r, _z, _p, _q;

            public Cg(int n, int[] rows, int[] cols, double[] vals, double[] diag)
            {
                _n = n; _rows = rows; _cols = cols; _vals = vals; _diag = diag;
                _invDiag = new double[n];
                for (int i = 0; i < n; i++) _invDiag[i] = diag[i] != 0.0 ? 1.0 / diag[i] : 0.0;
                _r = new double[n]; _z = new double[n]; _p = new double[n]; _q = new double[n];
            }

            private void Mul(double[] v, double[] o)
            {
                for (int i = 0; i < _n; i++)
                {
                    double s = _diag[i] * v[i];
                    for (int k = _rows[i]; k < _rows[i + 1]; k++) s += _vals[k] * v[_cols[k]];
                    o[i] = s;
                }
            }

            /// <summary>x を初期値として A x = b を解く。収束しなければ false。</summary>
            public bool Solve(double[] b, double[] x)
            {
                double bnorm = 0.0;
                for (int i = 0; i < _n; i++) bnorm += b[i] * b[i];
                bnorm = System.Math.Sqrt(bnorm);
                if (bnorm == 0.0) { System.Array.Clear(x, 0, _n); return true; }

                Mul(x, _q);
                double rz = 0.0;
                for (int i = 0; i < _n; i++)
                {
                    _r[i] = b[i] - _q[i];
                    _z[i] = _invDiag[i] * _r[i];
                    _p[i] = _z[i];
                    rz += _r[i] * _z[i];
                }

                int maxIter = System.Math.Max(1000, 10 * _n);
                for (int it = 0; it < maxIter; it++)
                {
                    double rnorm = 0.0;
                    for (int i = 0; i < _n; i++) rnorm += _r[i] * _r[i];
                    if (System.Math.Sqrt(rnorm) <= CgTolerance * bnorm) return true;

                    Mul(_p, _q);
                    double pq = 0.0;
                    for (int i = 0; i < _n; i++) pq += _p[i] * _q[i];
                    if (pq == 0.0 || double.IsNaN(pq)) return false;
                    double alpha = rz / pq;

                    double rzNew = 0.0;
                    for (int i = 0; i < _n; i++)
                    {
                        x[i]  += alpha * _p[i];
                        _r[i] -= alpha * _q[i];
                        _z[i]  = _invDiag[i] * _r[i];
                        rzNew += _r[i] * _z[i];
                    }
                    double beta = rzNew / rz;
                    rz = rzNew;
                    for (int i = 0; i < _n; i++) _p[i] = _z[i] + beta * _p[i];
                }
                return false;
            }
        }

        // ================================================================
        // 遮蔽判定用の BVH（heat_ray_source_visible）
        // ================================================================

        private sealed class TriBvh
        {
            private readonly Vector3[] _pos;
            private readonly List<int> _tris;
            private readonly Vector3[] _tnor;
            private readonly List<Node> _nodes = new List<Node>();
            private readonly int[] _order;

            private struct Node
            {
                public Vector3 Min, Max;
                public int Left, Right;   // 葉なら -1
                public int Start, Count;  // 葉の三角形（_order の範囲）
            }

            public TriBvh(Vector3[] pos, List<int> tris, Vector3[] tnor)
            {
                _pos = pos; _tris = tris; _tnor = tnor;
                int tc = tris.Count / 3;
                _order = new int[tc];
                for (int i = 0; i < tc; i++) _order[i] = i;
                if (tc > 0) Build(0, tc);
            }

            private Vector3 Centroid(int t)
                => (_pos[_tris[3 * t]] + _pos[_tris[3 * t + 1]] + _pos[_tris[3 * t + 2]]) / 3f;

            private int Build(int start, int count)
            {
                Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                Vector3 mx = -mn;
                for (int i = start; i < start + count; i++)
                {
                    int t = _order[i];
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 v = _pos[_tris[3 * t + k]];
                        mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
                    }
                }
                int idx = _nodes.Count;
                _nodes.Add(new Node { Min = mn, Max = mx, Left = -1, Right = -1, Start = start, Count = count });
                if (count <= 4) return idx;

                Vector3 ext = mx - mn;
                int axis = ext.x >= ext.y && ext.x >= ext.z ? 0 : (ext.y >= ext.z ? 1 : 2);
                System.Array.Sort(_order, start, count,
                    Comparer<int>.Create((a, b) => Centroid(a)[axis].CompareTo(Centroid(b)[axis])));
                int half = count / 2;
                int l = Build(start, half);
                int r = Build(start + half, count - half);
                var node = _nodes[idx];
                node.Left = l; node.Right = r;
                _nodes[idx] = node;
                return idx;
            }

            /// <summary>頂点から熱源の最近点へのレイが、表側から入る三角形に遮られないか。</summary>
            public bool Visible(Vector3 vertex, in HeatBoneSource s)
            {
                if (_nodes.Count == 0) return true;
                Vector3 end   = ClosestOnSegment(vertex, s.Root, s.Tip);
                Vector3 vec   = end - vertex;
                Vector3 start = vertex + vec * 1e-5f;
                vec *= 1f - 2e-5f;
                float dist = vec.magnitude;
                if (dist <= 0f) return true;
                Vector3 dir = vec / dist;
                return !AnyHit(0, start, dir, dist);
            }

            private bool AnyHit(int ni, Vector3 o, Vector3 d, float maxT)
            {
                var node = _nodes[ni];
                if (!RayBox(o, d, maxT, node.Min, node.Max)) return false;
                if (node.Left < 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        int t = _order[i];
                        if (Vector3.Dot(_tnor[t], d) >= -1e-5f) continue;
                        if (RayTri(o, d, maxT, _pos[_tris[3 * t]], _pos[_tris[3 * t + 1]], _pos[_tris[3 * t + 2]]))
                            return true;
                    }
                    return false;
                }
                return AnyHit(node.Left, o, d, maxT) || AnyHit(node.Right, o, d, maxT);
            }

            private static bool RayBox(Vector3 o, Vector3 d, float maxT, Vector3 mn, Vector3 mx)
            {
                float t0 = 0f, t1 = maxT;
                for (int a = 0; a < 3; a++)
                {
                    float inv = 1f / d[a];
                    float tn = (mn[a] - o[a]) * inv, tf = (mx[a] - o[a]) * inv;
                    if (tn > tf) { float tmp = tn; tn = tf; tf = tmp; }
                    if (float.IsNaN(tn) || float.IsNaN(tf)) { if (o[a] < mn[a] || o[a] > mx[a]) return false; continue; }
                    t0 = Mathf.Max(t0, tn); t1 = Mathf.Min(t1, tf);
                    if (t0 > t1) return false;
                }
                return true;
            }

            /// <summary>Möller–Trumbore。0 &lt; t &lt; maxT で当たれば true。</summary>
            private static bool RayTri(Vector3 o, Vector3 d, float maxT, Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 pv = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-12f) return false;
                float inv = 1f / det;
                Vector3 tv = o - a;
                float u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) return false;
                Vector3 qv = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, qv) * inv;
                if (v < 0f || u + v > 1f) return false;
                float t = Vector3.Dot(e2, qv) * inv;
                return t > 0f && t < maxT;
            }
        }
    }
}
