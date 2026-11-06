// MeshModifierAudit.cs
// 処理列の種類の検査。queryMeshModifierAudit が呼ぶ。モデルもプロジェクトも見ない。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【見るもの】
//   1. 発見 … 登録された種類と引数の宣言、発見のときの問題（MeshModifierRegistry.Problems）
//   2. 往復 … 全種類について、既定値と 3 種類の処理範囲（全体・ID の組・パーツ選択辞書）で
//             ToArgs → FromArgs → ToArgs が同じ文字列の対になるか
//   3. 範囲 … メモリ上に作ったメッシュで、頂点の並びと面の並びを入れ替えたあとも
//             ID の組・パーツ選択辞書が同じ頂点・辺・面へ引き直されるか。
//             利用者の辞書を書き換えないか、控えの無い辞書・無い名前が失敗になるか、
//             位置だけの種類が範囲内の頂点だけを動かすか
//   4. 指定の引数 … kind と引数が渡されたら、その引数で FromArgs → ToArgs → FromArgs → ToArgs を回す
//   5. 種類 … 登録されたデフォーマがすべて種類になっているか、位置だけの全種類で Evaluate と位置の口が
//             一致し範囲の外が動かないか、厚みの頂点数・面数・ID（段 6）

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    public static class MeshModifierAudit
    {
        public sealed class Result
        {
            public string       Report;
            public List<string> Kinds = new List<string>();
            public int          Problems;
            public int          RoundTripFailures;
            public int          ScopeFailures;
            /// <summary>種類の自己試験（包む漏れ・位置の口と Evaluate の一致・厚み）の失敗の件数。</summary>
            public int          KindFailures;
            /// <summary>指定の引数の往復で得た文字列の対（"キー=値"）。指定が無ければ空。</summary>
            public List<string> Args = new List<string>();
        }

        public static Result Run(string kind, string[] argKeys, string[] argValues)
        {
            var r  = new Result();
            var sb = new StringBuilder();

            // ── 1. 発見 ─────────────────────────────────────────────
            sb.Append("── 種類 ──");
            foreach (var info in MeshModifierRegistry.All)
            {
                r.Kinds.Add(info.Kind);
                sb.Append('\n').Append("  ").Append(info.Kind)
                  .Append("（").Append(info.DisplayName).Append("） ")
                  .Append(info.Change)
                  .Append(info.HasPositionsPath ? " 位置の口あり" : "")
                  .Append(" ").Append(info.Type.FullName)
                  .Append(" [").Append(info.Type.Assembly.GetName().Name).Append("]");
                foreach (var p in info.Params)
                {
                    sb.Append('\n').Append("    ").Append(p.Key).Append(" : ").Append(p.Type.Name);
                    if (p.Attr.HasMin)  sb.Append(" min=").Append(p.Attr.Min);
                    if (p.Attr.HasMax)  sb.Append(" max=").Append(p.Attr.Max);
                    if (p.Attr.HasStep) sb.Append(" step=").Append(p.Attr.Step);
                    if (p.Attr.Required)  sb.Append(" required");
                    if (p.Attr.IsMeshRef) sb.Append(" meshRef");
                }
            }
            if (r.Kinds.Count == 0) sb.Append('\n').Append("  （無し）");

            r.Problems = MeshModifierRegistry.Problems.Count;
            sb.Append('\n').Append("── 発見の問題 ").Append(r.Problems).Append(" 件 ──");
            foreach (var p in MeshModifierRegistry.Problems) sb.Append('\n').Append("  ").Append(p);

            // ── 2. 往復 ─────────────────────────────────────────────
            var scopes = new[]
            {
                ("all",      MeshModifierScope.All()),
                ("ids",      new MeshModifierScope
                             {
                                 Kind          = MeshModifierScopeKind.Ids,
                                 VertexIds     = new[] { 11, 12 },
                                 EdgeVertexIds = new[] { 12, 13 },
                                 FaceVertexIds = new[] { 12, 15, 16, 13 },
                                 FaceSizes     = new[] { 4 },
                             }),
                ("partsSet", MeshModifierScope.PartsSet("辞書,\"引用\"")),
                ("invert",   new MeshModifierScope { Kind = MeshModifierScopeKind.Ids, VertexIds = new[] { 11 }, Invert = true }),
            };
            var rtLines = new List<string>();
            foreach (var info in MeshModifierRegistry.All)
                foreach (var (label, scope) in scopes)
                {
                    var m1 = MeshModifierRegistry.Create(info.Kind);
                    var a1 = MeshModifierRegistry.ToArgs(m1, scope);
                    string why = RoundTrip(info.Kind, a1, out _);
                    if (why != null) { r.RoundTripFailures++; rtLines.Add($"  {info.Kind}/{label}: {why}"); }
                }
            sb.Append('\n').Append("── 往復の不一致 ").Append(r.RoundTripFailures).Append(" 件 ──");
            foreach (var l in rtLines) sb.Append('\n').Append(l);

            // ── 3. 範囲 ─────────────────────────────────────────────
            var scLines = new List<string>();
            ScopeSelfTest(scLines, ref r.ScopeFailures);
            sb.Append('\n').Append("── 範囲の自己試験 失敗 ").Append(r.ScopeFailures).Append(" 件 ──");
            foreach (var l in scLines) sb.Append('\n').Append(l);

            // ── 5. 種類の自己試験 ─────────────────────────────────────
            var kdLines = new List<string>();
            KindSelfTest(kdLines, ref r.KindFailures);
            sb.Append('\n').Append("── 種類の自己試験 失敗 ").Append(r.KindFailures).Append(" 件 ──");
            foreach (var l in kdLines) sb.Append('\n').Append(l);

            // ── 4. 指定の引数 ─────────────────────────────────────────
            if (!string.IsNullOrEmpty(kind))
            {
                sb.Append('\n').Append("── 指定の引数（").Append(kind).Append("） ──");
                var input = new Dictionary<string, string>(StringComparer.Ordinal);
                int n = Math.Max(argKeys?.Length ?? 0, argValues?.Length ?? 0);
                string argErr = null;
                if ((argKeys?.Length ?? 0) != (argValues?.Length ?? 0))
                    argErr = $"argKeys（{argKeys?.Length ?? 0} 個）と argValues（{argValues?.Length ?? 0} 個）の数が合わない";
                else
                    for (int i = 0; i < n; i++) input[argKeys[i]] = argValues[i];

                if (argErr != null)
                {
                    r.RoundTripFailures++;
                    sb.Append('\n').Append("  失敗: ").Append(argErr);
                }
                else if (!MeshModifierRegistry.FromArgs(kind, input, out var m, out var s, out string err))
                {
                    r.RoundTripFailures++;
                    sb.Append('\n').Append("  失敗: ").Append(err);
                }
                else
                {
                    var a1 = MeshModifierRegistry.ToArgs(m, s);
                    // 渡した値が文字列の対にそのまま残っているか（書き方の違いは差として出す）
                    foreach (var kv in input)
                        if (!a1.TryGetValue(kv.Key, out var got) || got != kv.Value)
                            sb.Append('\n').Append("  差: ").Append(kv.Key).Append(" 入力=").Append(kv.Value)
                              .Append(" 出力=").Append(got ?? "（無し）");
                    string why = RoundTrip(kind, a1, out _);
                    if (why != null) { r.RoundTripFailures++; sb.Append('\n').Append("  失敗: ").Append(why); }
                    foreach (var kv in a1.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        r.Args.Add(kv.Key + "=" + kv.Value);
                        sb.Append('\n').Append("  ").Append(kv.Key).Append('=').Append(kv.Value);
                    }
                }
            }

            r.Report = sb.ToString();
            return r;
        }

        /// <summary>a1 を FromArgs → ToArgs に通し、a1 と同じになるか。違えば理由。</summary>
        private static string RoundTrip(string kind, Dictionary<string, string> a1, out Dictionary<string, string> a2)
        {
            a2 = null;
            if (!MeshModifierRegistry.FromArgs(kind, a1, out var m2, out var s2, out string err))
                return "FromArgs: " + err;
            a2 = MeshModifierRegistry.ToArgs(m2, s2);
            return Diff(a1, a2);
        }

        private static string Diff(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            var diffs = new List<string>();
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)
                    diffs.Add($"{kv.Key}: {kv.Value} → {v ?? "（無し）"}");
            foreach (var kv in b)
                if (!a.ContainsKey(kv.Key)) diffs.Add($"{kv.Key}: （無し） → {kv.Value}");
            return diffs.Count == 0 ? null : string.Join(" / ", diffs);
        }

        // ================================================================
        // 範囲の自己試験
        // ================================================================

        /// <summary>
        /// 四角形 2 枚（辺 1 本を共有）の試験メッシュ。
        ///   頂点 0..5 の ID は 11..16、面 0 = (0,1,2,3)・面 1 = (1,4,5,2)、面 ID は 201・202。
        /// reversed=true なら頂点の並びを逆にし（索引 i → 5-i）、面の並びも入れ替える。
        /// </summary>
        private static MeshObject BuildTestMesh(bool reversed)
        {
            var pos = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0),
                new Vector3(0, 1, 0), new Vector3(2, 0, 0), new Vector3(2, 1, 0),
            };
            int Map(int i) => reversed ? 5 - i : i;

            var mo = new MeshObject("modifierScopeTest");
            var verts = new Vertex[6];
            for (int i = 0; i < 6; i++) verts[Map(i)] = new Vertex(11 + i, pos[i]);
            mo.Vertices = verts.ToList();

            // 材質番号を明示する。省くと 4 引数が Face(v0, v1, v2, materialIndex) に解決され三角形になる（Face.cs:95）。
            var f0 = new Face(Map(0), Map(1), Map(2), Map(3), 0) { Id = 201 };
            var f1 = new Face(Map(1), Map(4), Map(5), Map(2), 0) { Id = 202 };
            mo.Faces = reversed ? new List<Face> { f1, f0 } : new List<Face> { f0, f1 };
            return mo;
        }

        private static string IdText(MeshObject mo, ResolvedMeshModifierScope s)
        {
            string V(IEnumerable<int> idx) => string.Join(",", idx.Select(i => mo.Vertices[i].Id).OrderBy(x => x));
            string E(IEnumerable<VertexPair> es) => string.Join(",", es
                .Select(e => Math.Min(mo.Vertices[e.V1].Id, mo.Vertices[e.V2].Id) + "-" + Math.Max(mo.Vertices[e.V1].Id, mo.Vertices[e.V2].Id))
                .OrderBy(x => x, StringComparer.Ordinal));
            string F(IEnumerable<int> fs) => string.Join(",", fs.Select(f => mo.Faces[f].Id).OrderBy(x => x));
            return $"V[{V(s.Vertices)}] E[{E(s.Edges)}] F[{F(s.Faces)}] A[{V(s.AffectedVertices)}]";
        }

        /// <summary>閉じた単位立方体（頂点 ID 31〜38、面 ID 301〜306）。</summary>
        private static MeshObject BuildTestCube()
        {
            var p = new[]
            {
                new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
                new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
            };
            var mo = new MeshObject("bevelJoinCube");
            for (int i = 0; i < 8; i++) mo.Vertices.Add(new Vertex(31 + i, p[i]));
            int[][] f =
            {
                new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 },
            };
            for (int i = 0; i < f.Length; i++)
                mo.Faces.Add(new Face(f[i][0], f[i][1], f[i][2], f[i][3], 0) { Id = 301 + i });
            return mo;
        }

        /// <summary>3 枚の四角の帯（下の辺 y=0・上の辺 y=1、頂点 ID 41〜48、面 ID 401〜403）。</summary>
        private static MeshObject BuildTestStrip()
        {
            var mo = new MeshObject("bevelJoinStrip");
            for (int i = 0; i < 4; i++) mo.Vertices.Add(new Vertex(41 + i, new Vector3(i, 0, 0)));
            for (int i = 0; i < 4; i++) mo.Vertices.Add(new Vertex(45 + i, new Vector3(i, 1, 0)));
            for (int i = 0; i < 3; i++)
                mo.Faces.Add(new Face(i, i + 1, i + 5, i + 4, 0) { Id = 401 + i });
            return mo;
        }

        private static MeshObject BevelByEdges(MeshObject src, VertexPair[] edges, int segments, out string error)
        {
            var sc = MeshModifierScope.FromIndices(src, null, edges, null, out _);
            sc.TryResolve(src, null, out var r, out _);
            var bv = new BevelMeshModifier { Amount = 0.1f, Segments = segments };
            return bv.Evaluate(new MeshModifierInput { Source = src, Scope = r }, out error);
        }

        private static string BevelStats(MeshObject m)
            => m == null ? "null" : $"頂点 {m.Vertices.Count}・面 {m.Faces.Count}・境界辺 {Poly_Ling.Ops.BoundaryEdgeOps.CollectBoundaryEdges(m).Count}";

        private static int Boundary(MeshObject m) => m == null ? -1 : Poly_Ling.Ops.BoundaryEdgeOps.CollectBoundaryEdges(m).Count;

        /// <summary>
        /// 段 9b：2 本以上のベベル辺が集まる頂点で穴が空かないこと、3 本ではふたが付くこと、
        /// 角度で辺を決めること、厚みの後の辞書が表と裏の両方の辺に当たること。
        /// </summary>
        private static void BevelJoinSelfTest(List<string> lines, ref int failures)
        {
            var cube = BuildTestCube();

            var two = BevelByEdges(cube, new[] { new VertexPair(0, 1), new VertexPair(0, 3) }, 1, out string e1);
            // 残る元の頂点 5 ＋ 新しい頂点 6（辺ごとに両端 2 個ずつの 8 個から、角の 2 領域でまとめた 2 個を引く）
            Check(lines, ref failures, "ベベル：角の 2 辺（分割 1）で穴が空かない：頂点 11・面 8・境界辺 0",
                  two != null && two.Vertices.Count == 11 && two.Faces.Count == 8 && Boundary(two) == 0,
                  two == null ? e1 : BevelStats(two));

            var three = BevelByEdges(cube, new[] { new VertexPair(0, 1), new VertexPair(0, 3), new VertexPair(0, 4) }, 1, out string e2);
            Check(lines, ref failures, "ベベル：角の 3 辺（分割 1）でふたの三角形が付く：頂点 13・面 10・境界辺 0",
                  three != null && three.Vertices.Count == 13 && three.Faces.Count == 10 && Boundary(three) == 0
                  && three.Faces.Count(f => f.VertexIndices.Count == 3) == 1,
                  three == null ? e2 : BevelStats(three));

            var three2 = BevelByEdges(cube, new[] { new VertexPair(0, 1), new VertexPair(0, 3), new VertexPair(0, 4) }, 3, out string e3);
            Check(lines, ref failures, "ベベル：角の 3 辺（分割 3）で境界辺 0",
                  three2 != null && Boundary(three2) == 0, three2 == null ? e3 : BevelStats(three2));

            var all = new BevelMeshModifier { Amount = 0.1f, Segments = 1, LimitMethod = BevelLimitMethod.Angle, Angle = 30f }
                .Evaluate(new MeshModifierInput { Source = cube, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e4);
            Check(lines, ref failures, "ベベル（角度・全体・分割 1）：箱の 12 辺すべて：頂点 24・面 26・境界辺 0",
                  all != null && all.Vertices.Count == 24 && all.Faces.Count == 26 && Boundary(all) == 0,
                  all == null ? e4 : BevelStats(all));

            var all2 = new BevelMeshModifier { Amount = 0.1f, Segments = 2, Fillet = true, LimitMethod = BevelLimitMethod.Angle, Angle = 30f }
                .Evaluate(new MeshModifierInput { Source = cube, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e5);
            Check(lines, ref failures, "ベベル（角度・全体・分割 2・フィレット）：境界辺 0",
                  all2 != null && Boundary(all2) == 0, all2 == null ? e5 : BevelStats(all2));

            var flat = new BevelMeshModifier { LimitMethod = BevelLimitMethod.Angle, Angle = 30f }
                .Evaluate(new MeshModifierInput { Source = BuildTestStrip(), Scope = ResolvedMeshModifierScope.CreateAll() }, out string e6);
            Check(lines, ref failures, "ベベル（角度）：平らな帯は角度以上の辺が無いので失敗", flat == null, e6);

            // 厚みの後：一直線に並ぶ上の 2 辺（ID 45-46・46-47）は表と裏の両方に当たり、つながった帯になる
            var strip = BuildTestStrip();
            var thick = new ThicknessMeshModifier { Thickness = 0.2f }
                .Evaluate(new MeshModifierInput { Source = strip, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e7);
            if (thick == null)
            {
                Check(lines, ref failures, "厚みの帯を作る", false, e7);
                return;
            }
            var idScope = MeshModifierScope.FromIndices(strip, null, new[] { new VertexPair(4, 5), new VertexPair(5, 6) }, null, out _);
            idScope.TryResolve(thick, null, out var rid, out _);
            var bvThick = new BevelMeshModifier { Amount = 0.05f, Segments = 2 }
                .Evaluate(new MeshModifierInput { Source = thick, Scope = rid }, out string e8);
            Check(lines, ref failures, "ベベル：厚みの後の一直線の 2 辺（表と裏・分割 2）で境界辺 0",
                  rid.Edges.Count == 4 && bvThick != null && Boundary(bvThick) == 0,
                  bvThick == null ? e8 : $"範囲の辺 {rid.Edges.Count}・{BevelStats(bvThick)}");

            var set = new PartsSelectionSet("上の辺") { Edges = new HashSet<VertexPair> { new VertexPair(4, 5) } };
            set.CaptureIds(strip);
            bool okSet = MeshModifierScope.PartsSet("上の辺").TryResolve(thick, new List<PartsSelectionSet> { set }, out var rs, out string e9);
            Check(lines, ref failures, "パーツ選択辞書：厚みの後は同じ ID の辺（表と裏）の両方に当たる：辺 2",
                  okSet && rs.Edges.Count == 2, okSet ? $"辺 {rs.Edges.Count}" : e9);
        }

        private static void Check(List<string> lines, ref int failures, string name, bool ok, string detail)
        {
            if (!ok) failures++;
            lines.Add($"  {(ok ? "合格" : "失敗")} {name}{(string.IsNullOrEmpty(detail) ? "" : "：" + detail)}");
        }

        private static void ScopeSelfTest(List<string> lines, ref int failures)
        {
            var a = BuildTestMesh(false);
            var b = BuildTestMesh(true);

            // 選択：頂点 0、辺 (1,2)、面 1（ID では 頂点 11・辺 12-13・面 202）
            var selV = new HashSet<int> { 0 };
            var selE = new HashSet<VertexPair> { new VertexPair(1, 2) };
            var selF = new HashSet<int> { 1 };
            const string expected = "V[11] E[12-13] F[202] A[11,12,13,15,16]";

            // ID の組
            var ids = MeshModifierScope.FromIndices(a, selV, selE, selF, out int noId);
            Check(lines, ref failures, "ID の組を作る", noId == 0, $"noId={noId}");

            bool okA = ids.TryResolve(a, null, out var ra, out string ea);
            Check(lines, ref failures, "ID の組 → 元のメッシュ", okA && IdText(a, ra) == expected,
                  okA ? IdText(a, ra) : ea);

            bool okB = ids.TryResolve(b, null, out var rb, out string eb);
            Check(lines, ref failures, "ID の組 → 並びを入れ替えたメッシュ", okB && IdText(b, rb) == expected,
                  okB ? IdText(b, rb) : eb);

            // 反転（頂点で決める。頂点 14 だけが外側に残る）
            var inv = ids.Clone();
            inv.Invert = true;
            bool okI = inv.TryResolve(b, null, out var ri, out string ei);
            Check(lines, ref failures, "反転 → 範囲が触れる頂点の外側", okI && IdText(b, ri) == "V[14] E[] F[] A[14]",
                  okI ? IdText(b, ri) : ei);

            var invAll = MeshModifierScope.All();
            invAll.Invert = true;
            bool okIA = invAll.TryResolve(b, null, out var ria, out _);
            Check(lines, ref failures, "全体の反転は全体のまま", okIA && ria.IsAll, null);

            var invEdge = MeshModifierScope.FromIndices(a, null, new[] { new VertexPair(0, 1) }, null, out _);
            invEdge.Invert = true;
            bool okIE = invEdge.TryResolve(a, null, out var rie, out string eie);
            Check(lines, ref failures, "辺の反転は残りの頂点でできた辺と面だけ：辺 12-11 が外れ 15-16・13-14 ほかが残る",
                  okIE && rie.AffectedVertices.Count == 4 && !rie.ContainsVertex(0) && !rie.ContainsVertex(1) && rie.Faces.Count == 0,
                  okIE ? IdText(a, rie) : eie);

            // パーツ選択辞書
            var set = new PartsSelectionSet("試験辞書")
            {
                Vertices = new HashSet<int>(selV),
                Edges    = new HashSet<VertexPair>(selE),
                Faces    = new HashSet<int>(selF),
            };
            set.CaptureIds(a);
            var sets = new List<PartsSelectionSet> { set };
            var ps = MeshModifierScope.PartsSet("試験辞書");

            bool okP = ps.TryResolve(b, sets, out var rp, out string ep);
            Check(lines, ref failures, "パーツ選択辞書 → 並びを入れ替えたメッシュ", okP && IdText(b, rp) == expected,
                  okP ? IdText(b, rp) : ep);
            Check(lines, ref failures, "利用者の辞書を書き換えない",
                  set.Vertices.SetEquals(selV) && set.Edges.SetEquals(selE) && set.Faces.SetEquals(selF), null);

            var noIdSet = new PartsSelectionSet("控え無し") { Vertices = new HashSet<int>(selV) };
            bool okN = MeshModifierScope.PartsSet("控え無し").TryResolve(b, new List<PartsSelectionSet> { noIdSet }, out _, out string en);
            Check(lines, ref failures, "控えの無い辞書は失敗", !okN, en);

            bool okM = MeshModifierScope.PartsSet("無い名前").TryResolve(b, sets, out _, out string em);
            Check(lines, ref failures, "無い名前は失敗", !okM, em);

            // 位置だけの種類が範囲内の頂点だけを動かすか（並びを入れ替えたメッシュで）
            if (okB)
            {
                var tr = new TranslateMeshModifier { Offset = new Vector3(0, 0, 1) };
                var input = new MeshModifierInput { Source = b, Scope = rb };
                var outMo = tr.Evaluate(input, out string te);
                bool moveOk = outMo != null;
                var movedIds = new List<int>();
                if (moveOk)
                    for (int i = 0; i < b.Vertices.Count; i++)
                        if (outMo.Vertices[i].Position != b.Vertices[i].Position) movedIds.Add(b.Vertices[i].Id);
                movedIds.Sort();
                string moved = string.Join(",", movedIds);
                Check(lines, ref failures, "平行移動が範囲内だけを動かす（Evaluate）",
                      moveOk && moved == "11,12,13,15,16" && b.Vertices.All(v => v.Position.z == 0f),
                      moveOk ? $"動いた頂点 ID [{moved}]" : te);

                var src = b.Vertices.Select(v => v.Position).ToArray();
                var dst = (Vector3[])src.Clone();
                bool pOk = tr.EvaluatePositions(input, src, dst, out string pe);
                bool same = pOk && outMo != null;
                if (same) for (int i = 0; i < dst.Length; i++) if (dst[i] != outMo.Vertices[i].Position) { same = false; break; }
                Check(lines, ref failures, "位置の口が Evaluate と一致", same, pOk ? null : pe);
            }
        }

        // ================================================================
        // 種類の自己試験（段 6）
        // ================================================================

        /// <summary>
        /// 1. 登録されたデフォーマがすべて種類として包まれているか（格子は未登録なので対象外）
        /// 2. 位置だけの全種類で、Evaluate と位置の口が一致し、範囲の外が動かないか
        /// 3. 厚み：全体と面 1 枚の範囲で頂点数・面数、範囲外の面が残るか、立体の頂点 ID が元の ID か
        /// 4. ベベル：辺 1 本で頂点数・面数・ID の写し、全体の範囲は失敗、入力を書き換えない（段 7a）
        /// 5. 配列：全体と面 1 枚の範囲で頂点数・面数・ずれ・ID（段 7a）
        /// 6. ブーリアン：箱 2 つの差（相手は行列で動かす）で穴 0・ID の写し・面 ID 0、頂点を動かした相手と同じ結果、
        ///    全体以外・相手なしは失敗、入力を書き換えない（段 7b）
        /// 7. 格子：1×1×1 で角 1 点を動かした補間値、長さ違い・索引の範囲外は失敗、入力を書き換えない（段 7b）
        /// 8. サブディビジョン：箱 1 回の頂点数・面数・ID、追随の口と Evaluate の一致、全体以外は失敗（段 8）
        /// 9. ミラー（実体化）：板の頂点数・面数・ID、追随の口と Evaluate の一致、全体以外は失敗（段 8）
        /// 10. ミラー（表示）の末尾固定、追随の口を持つ種類（段 8）
        /// </summary>
        private static void KindSelfTest(List<string> lines, ref int failures)
        {
            // ── 0. 畳む形（並列・分岐を壊さない） ──────────────────────
            failures += ModifierFoldSelfTest.Run(lines);

            // ── 1. 包む漏れ ──────────────────────────────────────────
            var wrapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var info in MeshModifierRegistry.All)
                if (typeof(IDeformerMeshModifier).IsAssignableFrom(info.Type))
                    wrapped.Add(((IDeformerMeshModifier)MeshModifierRegistry.Create(info.Kind)).GetDeformerName());
            var missing = Poly_Ling.Tools.Deformers.DeformerRegistry.GetNames().Where(n => !wrapped.Contains(n)).ToList();
            Check(lines, ref failures, "登録されたデフォーマがすべて種類になっている", missing.Count == 0,
                  missing.Count == 0 ? $"{wrapped.Count} 個" : "包んでいない: " + string.Join(",", missing));

            // ── 2. 位置だけの種類 ────────────────────────────────────
            var b = BuildTestMesh(true);
            var scopeIds = MeshModifierScope.FromIndices(BuildTestMesh(false), new[] { 0 },
                new[] { new VertexPair(1, 2) }, new[] { 1 }, out _);
            scopeIds.TryResolve(b, null, out var rb, out _);
            foreach (var info in MeshModifierRegistry.All)
            {
                if (info.Change != MeshModifierChange.PositionsOnly) continue;
                var m = MeshModifierRegistry.Create(info.Kind);
                // 恒等にならないよう、範囲のある float は範囲の 37% の値、真偽は true にする
                foreach (var p in info.Params)
                {
                    if (p.Type == typeof(float) && p.Attr.HasMin && p.Attr.HasMax)
                        p.Property.SetValue(m, (float)(p.Attr.Min + 0.37 * (p.Attr.Max - p.Attr.Min)));
                    else if (p.Type == typeof(bool))
                        p.Property.SetValue(m, true);
                }
                var input = new MeshModifierInput { Source = b, Scope = rb };
                var outMo = m.Evaluate(input, out string err);
                if (outMo == null) { Check(lines, ref failures, $"{info.Kind}: Evaluate", false, err); continue; }

                bool outsideKept = true;
                for (int i = 0; i < b.Vertices.Count; i++)
                    if (!rb.ContainsVertex(i) && outMo.Vertices[i].Position != b.Vertices[i].Position) outsideKept = false;
                Check(lines, ref failures, $"{info.Kind}: 範囲の外が動かない", outsideKept, null);

                if (m is IMeshModifierPositions pp)
                {
                    var src = b.Vertices.Select(v => v.Position).ToArray();
                    var dst = (Vector3[])src.Clone();
                    bool ok = pp.EvaluatePositions(input, src, dst, out string pe);
                    float maxDiff = 0f;
                    if (ok) for (int i = 0; i < dst.Length; i++) maxDiff = Mathf.Max(maxDiff, (dst[i] - outMo.Vertices[i].Position).magnitude);
                    Check(lines, ref failures, $"{info.Kind}: 位置の口が Evaluate と一致", ok && maxDiff < 1e-5f,
                          ok ? $"最大差 {maxDiff}" : pe);
                }
            }

            // ── 3. 厚み ──────────────────────────────────────────────
            var a = BuildTestMesh(false);
            var th = new ThicknessMeshModifier();

            var all = th.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e1);
            bool okAll = all != null && all.Vertices.Count == 12 && all.Faces.Count == 10;
            string idsAll = all == null ? "" : string.Join(",", all.Vertices.Select(v => v.Id).OrderBy(x => x));
            string fidsAll = all == null ? "" : string.Join(",", all.Faces.Select(f => f.Id).OrderBy(x => x));
            Check(lines, ref failures, "厚み（全体）：頂点 12・面 10、頂点 ID は元の ID が 2 個ずつ、面 ID は殻が元の ID・側面が 0",
                  okAll && idsAll == "11,11,12,12,13,13,14,14,15,15,16,16" && fidsAll == "0,0,0,0,0,0,201,201,202,202",
                  all == null ? e1 : $"頂点 {all.Vertices.Count}・面 {all.Faces.Count}・頂点 ID [{idsAll}]・面 ID [{fidsAll}]");

            var oneFace = MeshModifierScope.FromIndices(a, null, null, new[] { 1 }, out _);
            oneFace.TryResolve(a, null, out var r1, out _);
            var part = th.Evaluate(new MeshModifierInput { Source = a, Scope = r1 }, out string e2);
            bool keeps201 = part != null && part.Faces.Count(f => f.Id == 201) == 1;
            Check(lines, ref failures, "厚み（面 202 だけ）：頂点 12・面 7、面 201 が残る",
                  part != null && part.Vertices.Count == 12 && part.Faces.Count == 7 && keeps201,
                  part == null ? e2 : $"頂点 {part.Vertices.Count}・面 {part.Faces.Count}");
            Check(lines, ref failures, "厚みは入力を書き換えない", a.Vertices.Count == 6 && a.Faces.Count == 2, null);

            // ── 3b. ウェイトの署名と写し（チュートリアル計画 3.8） ─────────
            var wm = BuildTestMesh(false);
            ulong sigNone = Poly_Ling.Ops.MeshSignature.Weights(wm);
            Check(lines, ref failures, "ウェイトの署名：同じ中身なら同じ（写しでも）",
                  sigNone == Poly_Ling.Ops.MeshSignature.Weights(wm.Clone()), null);
            for (int vi = 0; vi < wm.Vertices.Count; vi++)
                wm.Vertices[vi].BoneWeight = new BoneWeight { boneIndex0 = vi + 1, weight0 = 1f };
            ulong sigPainted = Poly_Ling.Ops.MeshSignature.Weights(wm);
            Check(lines, ref failures, "ウェイトの署名：塗ると変わる", sigNone != sigPainted, null);
            wm.Vertices[0].BoneWeight = new BoneWeight { boneIndex0 = 1, weight0 = 0.5f, boneIndex1 = 2, weight1 = 0.5f };
            Check(lines, ref failures, "ウェイトの署名：重みだけ変えても変わる",
                  sigPainted != Poly_Ling.Ops.MeshSignature.Weights(wm), null);
            wm.Vertices[0].BoneWeight = new BoneWeight { boneIndex0 = 1, weight0 = 1f };
            Check(lines, ref failures, "ウェイトの署名：元へ戻すと元の値",
                  sigPainted == Poly_Ling.Ops.MeshSignature.Weights(wm), null);
            wm.Vertices[0].MirrorBoneWeight = new BoneWeight { boneIndex0 = 9, weight0 = 1f };
            Check(lines, ref failures, "ウェイトの署名：ミラー側のウェイトも見る",
                  sigPainted != Poly_Ling.Ops.MeshSignature.Weights(wm), null);
            wm.Vertices[0].MirrorBoneWeight = null;

            var wThick = th.Evaluate(new MeshModifierInput { Source = wm, Scope = ResolvedMeshModifierScope.CreateAll() }, out string eW);
            bool weightsCopied = wThick != null;
            if (wThick != null)
                foreach (var ov in wThick.Vertices)
                {
                    var from = wm.Vertices.FirstOrDefault(sv => sv.Id == ov.Id);
                    if (from == null || !ov.BoneWeight.HasValue || !from.BoneWeight.HasValue
                        || ov.BoneWeight.Value.boneIndex0 != from.BoneWeight.Value.boneIndex0)
                    { weightsCopied = false; break; }
                }
            Check(lines, ref failures, "厚み：作った頂点が元の頂点のウェイトを写す", weightsCopied, wThick == null ? eW : null);

            // ── 4. ベベル（段 7a）────────────────────────────────────
            // 共有の辺 (1,2)（ID 12-13）を分割 1 でベベル：辺の両端 2 頂点が 4 頂点に置き換わり、面が 1 枚増える。
            var bv = new BevelMeshModifier { Amount = 0.1f, Segments = 1 };
            var edgeScope = MeshModifierScope.FromIndices(a, null, new[] { new VertexPair(1, 2) }, null, out _);
            edgeScope.TryResolve(a, null, out var re, out _);
            var bvOut = bv.Evaluate(new MeshModifierInput { Source = a, Scope = re }, out string e3);
            string bvIds = bvOut == null ? "" : string.Join(",", bvOut.Vertices.Select(v => v.Id).OrderBy(x => x));
            string bvFids = bvOut == null ? "" : string.Join(",", bvOut.Faces.Select(f => f.Id).OrderBy(x => x));
            Check(lines, ref failures, "ベベル（辺 12-13）：頂点 8・面 3、新しい頂点は元の ID、足した面は 0",
                  bvOut != null && bvOut.Vertices.Count == 8 && bvOut.Faces.Count == 3
                  && bvIds == "11,12,12,13,13,14,15,16" && bvFids == "0,201,202",
                  bvOut == null ? e3 : $"頂点 {bvOut.Vertices.Count}・面 {bvOut.Faces.Count}・頂点 ID [{bvIds}]・面 ID [{bvFids}]");
            var bvAll = bv.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e4);
            Check(lines, ref failures, "ベベル：全体の範囲は失敗", bvAll == null, e4);
            Check(lines, ref failures, "ベベルは入力を書き換えない", a.Vertices.Count == 6 && a.Faces.Count == 2
                  && a.Faces[0].VertexIndices.Count == 4 && a.Faces[1].VertexIndices.Count == 4, null);

            // ── 4b. ベベルの頂点のまとめ方とふた・角度・辞書（段 9b）──────────
            BevelJoinSelfTest(lines, ref failures);

            // ── 5. 配列（段 7a）──────────────────────────────────────
            // 全体を 3 個、相対の間隔 (1,0,0)：外接箱の幅 2 なので x に 2・4 ずれた写しが足される。
            var ar = new ArrayMeshModifier { Count = 3, RelativeOffset = new Vector3(1, 0, 0), ConstantOffset = Vector3.zero };
            var arAll = ar.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e5);
            string arIds = arAll == null ? "" : string.Join(",", arAll.Vertices.Select(v => v.Id).OrderBy(x => x));
            float arMaxX = arAll == null ? 0f : arAll.Vertices.Max(v => v.Position.x);
            Check(lines, ref failures, "配列（全体・3 個）：頂点 18・面 6、ID は元の 3 回ずつ、x の最大 6",
                  arAll != null && arAll.Vertices.Count == 18 && arAll.Faces.Count == 6
                  && arIds == "11,11,11,12,12,12,13,13,13,14,14,14,15,15,15,16,16,16" && Mathf.Abs(arMaxX - 6f) < 1e-5f,
                  arAll == null ? e5 : $"頂点 {arAll.Vertices.Count}・面 {arAll.Faces.Count}・x の最大 {arMaxX}");
            var arPart = ar.Evaluate(new MeshModifierInput { Source = a, Scope = r1 }, out string e6);
            float arPartMaxX = arPart == null ? 0f : arPart.Vertices.Max(v => v.Position.x);
            Check(lines, ref failures, "配列（面 202 だけ・3 個）：頂点 14・面 4、x の最大 4",
                  arPart != null && arPart.Vertices.Count == 14 && arPart.Faces.Count == 4 && Mathf.Abs(arPartMaxX - 4f) < 1e-5f,
                  arPart == null ? e6 : $"頂点 {arPart.Vertices.Count}・面 {arPart.Faces.Count}・x の最大 {arPartMaxX}");
            Check(lines, ref failures, "配列は入力を書き換えない", a.Vertices.Count == 6 && a.Faces.Count == 2, null);

            // ── 6. ブーリアン（段 7b）────────────────────────────────
            // 辺 2 の箱 A（原点中心）から、原点に作った同じ箱 B を ReferenceToSource で (1,1,1) へ動かして引く。
            var ba = BuildTestBox(2f, 101);
            var bb = BuildTestBox(2f, 0);
            var shift = Matrix4x4.Translate(new Vector3(1f, 1f, 1f));
            var bo = new BooleanMeshModifier { Op = Poly_Ling.Ops.BooleanOpKind.Subtract };
            MeshModifierInput BoolInput(MeshObject other, Matrix4x4 m, ResolvedMeshModifierScope sc) => new MeshModifierInput
            {
                Source = ba, Scope = sc,
                References = new Dictionary<string, MeshObject> { { "other", other } },
                ReferenceToSource = new Dictionary<string, Matrix4x4> { { "other", m } },
            };
            int baVerts = ba.Vertices.Count, baFaces = ba.Faces.Count;
            var boOut = bo.Evaluate(BoolInput(bb, shift, ResolvedMeshModifierScope.CreateAll()), out string e7);
            if (boOut == null) Check(lines, ref failures, "ブーリアン（差）", false, e7);
            else
            {
                var topo = Poly_Ling.Ops.BoundaryEdgeOps.AnalyzeTopology(boOut);
                bool idsAtSource = true, cornerKept = false, cornerRemoved = true, innerZero = false;
                foreach (var v in boOut.Vertices)
                {
                    if (v.Id != 0)
                    {
                        var sv = ba.Vertices.FirstOrDefault(x => x.Id == v.Id);
                        if (sv == null || (sv.Position - v.Position).magnitude > 1e-4f) idsAtSource = false;
                    }
                    if ((v.Position - new Vector3(-1, -1, -1)).magnitude < 1e-4f && v.Id != 0) cornerKept = true;
                    if ((v.Position - new Vector3(1, 1, 1)).magnitude < 1e-4f) cornerRemoved = false;
                    if (v.Position.magnitude < 1e-4f && v.Id == 0) innerZero = true;
                }
                bool facesZero = boOut.Faces.All(f => f.Id == 0);
                Check(lines, ref failures, "ブーリアン（差・相手を行列で (1,1,1) へ）：穴 0、ID は同じ位置の入力の ID、角 (1,1,1) が消え内角 (0,0,0) は 0、面 ID は 0",
                      topo.BoundaryEdges == 0 && idsAtSource && cornerKept && cornerRemoved && innerZero && facesZero,
                      $"頂点 {boOut.Vertices.Count}・面 {boOut.Faces.Count}・境界辺 {topo.BoundaryEdges}・ID の位置 {idsAtSource}・角 {cornerKept}/{cornerRemoved}・内角 {innerZero}・面 ID {facesZero}");

                var bbMoved = BuildTestBox(2f, 0);
                foreach (var v in bbMoved.Vertices) v.Position += new Vector3(1f, 1f, 1f);
                bbMoved.InvalidatePositionCache();
                var boOut2 = bo.Evaluate(BoolInput(bbMoved, Matrix4x4.identity, ResolvedMeshModifierScope.CreateAll()), out string e8);
                bool same = boOut2 != null && boOut2.Vertices.Count == boOut.Vertices.Count && boOut2.Faces.Count == boOut.Faces.Count;
                Check(lines, ref failures, "ブーリアン：行列で動かした相手と、頂点を動かした相手が同じ結果", same,
                      boOut2 == null ? e8 : $"頂点 {boOut2.Vertices.Count}/{boOut.Vertices.Count}・面 {boOut2.Faces.Count}/{boOut.Faces.Count}");
            }
            var oneFaceBox = MeshModifierScope.FromIndices(ba, null, null, new[] { 0 }, out _);
            oneFaceBox.TryResolve(ba, null, out var rbox, out _);
            var boPart = bo.Evaluate(BoolInput(bb, shift, rbox), out string e9);
            Check(lines, ref failures, "ブーリアン：全体以外の範囲は失敗", boPart == null, e9);
            var boNoRef = bo.Evaluate(new MeshModifierInput { Source = ba, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e10);
            Check(lines, ref failures, "ブーリアン：相手が無ければ入力をそのまま通す（後ろのモディファイアを止めない）",
                  boNoRef != null && boNoRef.Vertices.Count == baVerts && boNoRef.Faces.Count == baFaces, e10);
            Check(lines, ref failures, "ブーリアン：相手が無ければカードに次の操作を出す", !string.IsNullOrEmpty(bo.GetHint()), null);
            Check(lines, ref failures, "ブーリアンは入力を書き換えない", ba.Vertices.Count == baVerts && ba.Faces.Count == baFaces, null);

            // 角丸＋分割の直方体が閉じていること。曲げた後でもブーリアンが通ること。
            var roundP = Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams.Default;
            roundP.MeshName = "modifierBoolTest";
            roundP.WidthTop = roundP.WidthBottom = roundP.DepthTop = roundP.DepthBottom = 0.6f;
            roundP.Height = 2f;
            roundP.CornerRadius = 0.1f;
            roundP.Subdivisions = new Vector3Int(2, 8, 2);
            var rounded = Poly_Ling.PrimitiveMesh.CubeMeshGenerator.Generate(roundP);
            var roundedWelded = rounded.Clone();
            Poly_Ling.Ops.MeshMergeHelper.MergeAllVerticesAtSamePosition(roundedWelded, 1e-5f);
            var roundedTopo = Poly_Ling.Ops.BoundaryEdgeOps.AnalyzeTopology(roundedWelded);
            Check(lines, ref failures, "角丸＋分割の直方体は閉じている", roundedTopo.BoundaryEdges == 0, $"境界辺 {roundedTopo.BoundaryEdges}");

            var bentBend = new BendMeshModifier { TotalAngleDeg = 90f };
            var bent = bentBend.Evaluate(new MeshModifierInput { Source = rounded, Scope = ResolvedMeshModifierScope.CreateAll() }, out string eBent);
            var bentCut = BuildTestBox(1f, 0);
            var bentBool = bent == null ? default : Poly_Ling.Ops.BooleanOps.Perform(
                Poly_Ling.Ops.BooleanOpKind.Subtract, bent, Matrix4x4.identity,
                bentCut, Matrix4x4.Translate(new Vector3(0.4f, 0.3f, 0f)), Matrix4x4.identity);
            Check(lines, ref failures, "曲げた角丸＋分割の直方体からブーリアンで引ける", bentBool.Success, eBent ?? bentBool.Message);

            // 小さい三角形でも法線が 0 にならないこと（Plane.cs の注記。一辺 2mm の箱どうし）。
            var tinyA = BuildTestBox(0.002f, 0);
            var tinyB = BuildTestBox(0.002f, 0);
            var tinyBool = Poly_Ling.Ops.BooleanOps.Perform(
                Poly_Ling.Ops.BooleanOpKind.Subtract, tinyA, Matrix4x4.identity,
                tinyB, Matrix4x4.Translate(new Vector3(0.001f, 0.001f, 0.001f)), Matrix4x4.identity,
                epsilon: 1e-7f, mergeThreshold: 1e-6f);
            Check(lines, ref failures, "一辺 2mm の箱どうしのブーリアンが閉じる", tinyBool.Success, tinyBool.Message);

            // ── 7. 格子（段 7b）──────────────────────────────────────
            // 試験メッシュ（z=0 の板。x 0〜2・y 0〜1）を 1×1×1 の格子（中心 (1,0.5,0)・大きさ (2,1,1)）で包み、
            // 角 (1,1,1)（索引 7）を z へ +1 動かす：(2,1,0) は z 0.5、(1,1,0) は z 0.25、(0,0,0) は動かない。
            var lt = new LatticeMeshModifier
            {
                CellsX = 1, CellsY = 1, CellsZ = 1,
                Center = new Vector3(1f, 0.5f, 0f), Size = new Vector3(2f, 1f, 1f),
                PointIndices = new[] { 7 }, PointOffsets = new[] { 0f, 0f, 1f },
            };
            var ltOut = lt.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e11);
            float Z(int i) => ltOut.Vertices[i].Position.z;
            Check(lines, ref failures, "格子（角 1 点を z+1）：(2,1,0)→z 0.5、(1,1,0)→z 0.25、(0,0,0) は動かない",
                  ltOut != null && Mathf.Abs(Z(5) - 0.5f) < 1e-5f && Mathf.Abs(Z(2) - 0.25f) < 1e-5f
                  && ltOut.Vertices[0].Position == a.Vertices[0].Position,
                  ltOut == null ? e11 : $"z = {Z(5)}, {Z(2)}, {Z(0)}");
            lt.PointOffsets = new[] { 0f, 1f };
            var ltBadLen = lt.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e12);
            Check(lines, ref failures, "格子：ずれの長さが索引の 3 倍でなければ失敗", ltBadLen == null, e12);
            lt.PointIndices = new[] { 8 }; lt.PointOffsets = new[] { 0f, 0f, 1f };
            var ltBadIdx = lt.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e13);
            Check(lines, ref failures, "格子：索引が範囲外なら失敗", ltBadIdx == null, e13);
            Check(lines, ref failures, "格子は入力を書き換えない", a.Vertices[5].Position == new Vector3(2, 1, 0), null);

            // ── 8. サブディビジョン（段 8）────────────────────────────
            // 閉じた箱（ID 301〜）を 1 回：頂点 = V + 辺 + 面、面 = 角の数の和。先頭の V 個は元の頂点の ID、他と面は 0。
            var sbox = BuildTestBox(2f, 301);
            int sv0 = sbox.Vertices.Count, sf0 = sbox.Faces.Count;
            var sedges = new HashSet<long>();
            int corners = 0;
            foreach (var f in sbox.Faces)
            {
                int k = f.VertexIndices.Count;
                corners += k;
                for (int i = 0; i < k; i++)
                {
                    int p = f.VertexIndices[i], q = f.VertexIndices[(i + 1) % k];
                    sedges.Add(((long)Mathf.Min(p, q) << 32) | (uint)Mathf.Max(p, q));
                }
            }
            var sd = new SubdivisionMeshModifier { Level = 1 };
            var sdOut = sd.Evaluate(new MeshModifierInput { Source = sbox, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e14);
            bool sdIds = sdOut != null;
            if (sdOut != null)
                for (int i = 0; i < sdOut.Vertices.Count; i++)
                {
                    int want = i < sv0 ? sbox.Vertices[i].Id : 0;
                    if (sdOut.Vertices[i].Id != want) { sdIds = false; break; }
                }
            Check(lines, ref failures, "サブディビジョン（箱・1 回）：頂点 V+辺+面・面は角の数の和、頂点の点は元の ID、他と面は 0",
                  sdOut != null && sdOut.Vertices.Count == sv0 + sedges.Count + sf0 && sdOut.Faces.Count == corners
                  && sdIds && sdOut.Faces.All(f => f.Id == 0),
                  sdOut == null ? e14 : $"頂点 {sdOut.Vertices.Count}（期待 {sv0 + sedges.Count + sf0}）・面 {sdOut.Faces.Count}（期待 {corners}）・ID {sdIds}");
            if (sdOut != null)
            {
                var moved = sbox.Clone();
                for (int i = 0; i < moved.Vertices.Count; i++) moved.Vertices[i].Position += new Vector3(0.1f * i, -0.05f * i, 0.02f * i);
                moved.InvalidatePositionCache();
                var sdRef = new SubdivisionMeshModifier { Level = 1 }.Evaluate(
                    new MeshModifierInput { Source = moved, Scope = ResolvedMeshModifierScope.CreateAll() }, out _);
                bool fok = sd.FollowPositions(moved.Vertices.Select(v => v.Position).ToArray(), out var fpos);
                float fdiff = float.MaxValue;
                if (fok && sdRef != null && fpos.Length == sdRef.Vertices.Count)
                {
                    fdiff = 0f;
                    for (int i = 0; i < fpos.Length; i++) fdiff = Mathf.Max(fdiff, (fpos[i] - sdRef.Vertices[i].Position).magnitude);
                }
                Check(lines, ref failures, "サブディビジョン：追随の口が、位置を動かした入力の Evaluate と一致", fok && fdiff < 1e-5f, $"最大差 {fdiff}");
            }
            var sdPart = sd.Evaluate(new MeshModifierInput { Source = a, Scope = r1 }, out string e15);
            Check(lines, ref failures, "サブディビジョン：全体以外の範囲は失敗", sdPart == null, e15);
            Check(lines, ref failures, "サブディビジョンは入力を書き換えない", sbox.Vertices.Count == sv0 && sbox.Faces.Count == sf0, null);

            // ── 9. ミラー（実体化）（段 8）─────────────────────────────
            // 試験メッシュ（x 0〜2 の板）を X で鏡映：x=0 の頂点は 1 つにまとまる。頂点 = 2N − 境界、面 = 2F。
            // 頂点 ID は両側とも元の ID（境界は 1 回、他は 2 回）、面は 0。
            var mb = new MirrorBakeMeshModifier { Axis = Poly_Ling.Symmetry.SymmetryAxis.X };
            int boundary = a.Vertices.Count(v => Mathf.Abs(v.Position.x) < mb.Threshold);
            var mbOut = mb.Evaluate(new MeshModifierInput { Source = a, Scope = ResolvedMeshModifierScope.CreateAll() }, out string e16);
            bool mbIds = mbOut != null;
            if (mbOut != null)
                foreach (var sv in a.Vertices)
                {
                    int want = Mathf.Abs(sv.Position.x) < mb.Threshold ? 1 : 2;
                    if (mbOut.Vertices.Count(v => v.Id == sv.Id) != want) { mbIds = false; break; }
                }
            Check(lines, ref failures, "ミラー（実体化）（板・X）：頂点 2N−境界・面 2F、頂点 ID は元の ID（境界 1 回・他 2 回）、面は 0",
                  mbOut != null && boundary > 0 && mbOut.Vertices.Count == 2 * a.Vertices.Count - boundary
                  && mbOut.Faces.Count == 2 * a.Faces.Count && mbIds && mbOut.Faces.All(f => f.Id == 0),
                  mbOut == null ? e16 : $"頂点 {mbOut.Vertices.Count}・面 {mbOut.Faces.Count}・境界 {boundary}・ID {mbIds}");
            if (mbOut != null)
            {
                // 境界の組を変えないよう、x は動かさない。
                var moved = a.Clone();
                for (int i = 0; i < moved.Vertices.Count; i++) moved.Vertices[i].Position += new Vector3(0f, 0.1f * i, -0.03f * i);
                moved.InvalidatePositionCache();
                var mbRef = new MirrorBakeMeshModifier { Axis = Poly_Ling.Symmetry.SymmetryAxis.X }.Evaluate(
                    new MeshModifierInput { Source = moved, Scope = ResolvedMeshModifierScope.CreateAll() }, out _);
                bool fok = mb.FollowPositions(moved.Vertices.Select(v => v.Position).ToArray(), out var fpos);
                float fdiff = float.MaxValue;
                if (fok && mbRef != null && fpos.Length == mbRef.Vertices.Count)
                {
                    fdiff = 0f;
                    for (int i = 0; i < fpos.Length; i++) fdiff = Mathf.Max(fdiff, (fpos[i] - mbRef.Vertices[i].Position).magnitude);
                }
                Check(lines, ref failures, "ミラー（実体化）：追随の口が、位置を動かした入力の Evaluate と一致", fok && fdiff < 1e-5f, $"最大差 {fdiff}");
            }
            var mbPart = mb.Evaluate(new MeshModifierInput { Source = a, Scope = r1 }, out string e17);
            Check(lines, ref failures, "ミラー（実体化）：全体以外の範囲は失敗", mbPart == null, e17);
            Check(lines, ref failures, "ミラー（実体化）は入力を書き換えない", a.Vertices.Count == 6 && a.Faces.Count == 2, null);

            // ── 10. ミラー（表示）と宣言（段 8）─────────────────────────
            var mdInfo = MeshModifierRegistry.Find("mirrorDisplay");
            Check(lines, ref failures, "ミラー（表示）は末尾固定・位置だけ",
                  mdInfo != null && mdInfo.PinnedLast && mdInfo.Change == MeshModifierChange.PositionsOnly, null);
            var followKinds = MeshModifierRegistry.All.Where(i => i.HasDragFollow).Select(i => i.Kind).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Check(lines, ref failures, "追随の口を持つ種類は mirrorBake・subdivision",
                  string.Join(",", followKinds) == "mirrorBake,subdivision", string.Join(",", followKinds));
        }

        /// <summary>辺 size の閉じた箱（原点中心、角丸なし。CubeMeshGenerator）。頂点 ID は idBase から振る（0 なら振らない）。</summary>
        private static MeshObject BuildTestBox(float size, int idBase)
        {
            var p = Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams.Default;
            p.MeshName = "modifierBoolTest";
            p.WidthTop = p.WidthBottom = p.DepthTop = p.DepthBottom = p.Height = size;
            p.CornerRadius = 0f;
            p.Subdivisions = Vector3Int.one;
            var mo = Poly_Ling.PrimitiveMesh.CubeMeshGenerator.Generate(p);
            for (int i = 0; i < mo.Vertices.Count; i++) mo.Vertices[i].Id = idBase == 0 ? 0 : idBase + i;
            mo.RebuildIdSets();
            return mo;
        }
    }
}
