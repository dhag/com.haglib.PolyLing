// ModifierFoldSelfTest.cs
// 「何を 1 オブジェクトに畳むか」の自己試験。queryMeshModifierAudit の種類の自己試験から呼ぶ（失敗 0 が完了条件）。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【目的】1 つの入力から複数の出力を並列に出す設計を、畳む処理が壊さないことを機械で確かめる。
//   単純・多段は畳む。並列・分岐・結果を生成が読む形は畳まず、書き出しで全部の行が残ること。
//   形はモデルをその場で組み立てる（プロジェクトを見ない）。形を足すときはここへ足す。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    public static class ModifierFoldSelfTest
    {
        private sealed class Shape
        {
            public readonly ModelContext Model = new ModelContext("foldTest");
            private readonly Dictionary<string, MeshContext> _meshes = new Dictionary<string, MeshContext>();
            private ulong _nextId = 900000000000UL;

            public MeshContext Mesh(string name, int vertexCount = 1)
            {
                if (_meshes.TryGetValue(name, out var mc)) return mc;
                var mo = new MeshObject(name);
                for (int i = 0; i < vertexCount; i++) mo.Vertices.Add(new Vertex(new UnityEngine.Vector3(i, 0, 0)));
                mc = new MeshContext { MeshObject = mo, Name = name, ObjectId = _nextId++ };
                Model.Add(mc);
                _meshes[name] = mc;
                return mc;
            }

            private void AddStep(string stackName, bool chain, string action, string key, MeshContext input, MeshContext output)
            {
                var g = new ModifierStack(stackName) { IsChain = chain };
                g.Steps.Clear();
                var s = new ModifierStackStep { Kind = ModifierStackStepKind.Command, Action = action };
                s.SetMeshRefIds(key, new List<ulong> { input.ObjectId });
                s.OutputObjectIds = new List<ulong> { output.ObjectId };
                g.AddStep(s);
                Model.AddModifierStack(g);
            }

            public void Chain(string input, string output, int outputVertices = 2)
            {
                AddStep(input + ">" + output, true, "subdivision", ModifierChainHandler.SourceKey, Mesh(input), Mesh(output, outputVertices));
                Mesh(output).Depth = Mesh(input).Depth + 1;
            }

            public void Subdiv(string input, string output, int outputVertices = 2)
                => AddStep(input + ">" + output, false, SubdivisionSync.Action, SubdivisionSync.CageKey, Mesh(input), Mesh(output, outputVertices));

            /// <summary>生成の履歴（入力を読んで別のものを出す）。ボーンの鎖やパイプに当たる。</summary>
            public void Generator(string input, string output)
                => AddStep(input + ">" + output, false, "placeSpringBoneLadderChains", "sourceMasterIndex", Mesh(input), Mesh(output));

            /// <summary>処理列の相手の参照（ブーリアン）。</summary>
            public void ChainWithOther(string input, string output, string other)
            {
                Chain(input, output);
                var g = Model.ModifierStacks[Model.ModifierStacks.Count - 1];
                g.Steps[0].SetMeshRefIds("other", new List<ulong> { Mesh(other).ObjectId });
            }
        }

        /// <summary>書き出しに残る行の名前（並べ替えて比べる）。</summary>
        private static string ExportNames(ModelContext model, bool apply)
        {
            var view = ModifierExportView.For(model, apply);
            var names = new List<string>();
            foreach (var mc in view.MeshContextList)
                if (mc != null && !ModifierExportView.IsPlaceholder(mc)) names.Add(mc.Name);
            names.Sort(string.CompareOrdinal);
            return string.Join(",", names);
        }

        private static string HiddenNames(ModelContext model)
        {
            var names = new List<string>();
            foreach (int i in ModifierResultOps.ResultIndices(model)) names.Add(model.GetMeshContext(i).Name);
            names.Sort(string.CompareOrdinal);
            return string.Join(",", names);
        }

        private static void Expect(List<string> lines, ref int failures, string title, Shape shape,
                                   string hidden, string exported, string exportedNoApply)
        {
            string h = HiddenNames(shape.Model), e = ExportNames(shape.Model, true), n = ExportNames(shape.Model, false);
            bool ok = h == hidden && e == exported && n == exportedNoApply;
            if (!ok) failures++;
            lines.Add($"  {(ok ? "合格" : "失敗")} 形 {title}：隠す[{h}] 書き出し[{e}] 適用なし[{n}]"
                      + (ok ? "" : $"（期待 隠す[{hidden}] 書き出し[{exported}] 適用なし[{exportedNoApply}]）"));
        }

        public static int Run(List<string> lines)
        {
            int failures = 0;

            var simple = new Shape();
            simple.Chain("A", "A_mod", 7);
            Expect(lines, ref failures, "単純", simple, "A_mod", "A", "A");
            int bakedCount = ModifierExportView.For(simple.Model, true).MeshContextList.First(m => m.Name == "A").MeshObject.VertexCount;
            if (bakedCount != 7) failures++;
            lines.Add($"  {(bakedCount == 7 ? "合格" : "失敗")} 形 単純：元の名前で結果の形を書く：{bakedCount} 頂点");

            var multi = new Shape();
            multi.Subdiv("A", "A_sub", 3);
            multi.Chain("A_sub", "A_sub_mod", 9);
            Expect(lines, ref failures, "多段", multi, "A_sub,A_sub_mod", "A", "A");
            int leafCount = ModifierExportView.For(multi.Model, true).MeshContextList.First(m => m.Name == "A").MeshObject.VertexCount;
            if (leafCount != 9) failures++;
            lines.Add($"  {(leafCount == 9 ? "合格" : "失敗")} 形 多段：根の名前で末端の形を書く：{leafCount} 頂点");
            bool rootOk = ReferenceEquals(ModifierResultOps.FoldedRootOf(multi.Model, multi.Mesh("A_sub_mod")), multi.Mesh("A"));
            if (!rootOk) failures++;
            lines.Add($"  {(rootOk ? "合格" : "失敗")} 形 多段：末端の選択は根へ回る");

            var branch = new Shape();
            branch.Subdiv("A", "A_sub");
            branch.Chain("A", "A_mod");
            Expect(lines, ref failures, "分岐", branch, "", "A,A_mod,A_sub", "A,A_mod,A_sub");
            bool branchSelf = ModifierResultOps.FoldedRootOf(branch.Model, branch.Mesh("A_mod")) == null;
            if (!branchSelf) failures++;
            lines.Add($"  {(branchSelf ? "合格" : "失敗")} 形 分岐：結果は自分で選べる");

            var parallel = new Shape();
            parallel.Chain("L", "L_mod");
            parallel.Generator("L", "Bone");
            Expect(lines, ref failures, "並列（入力 1 つから結果とボーン）", parallel, "", "Bone,L,L_mod", "Bone,L,L_mod");

            var input = new Shape();
            input.Chain("A", "A_mod");
            input.Generator("A_mod", "Pipe");
            Expect(lines, ref failures, "結果を生成の入力にする", input, "", "A,A_mod,Pipe", "A,A_mod,Pipe");

            var lower = new Shape();
            lower.Chain("A", "B");
            lower.Chain("B", "C");
            lower.Subdiv("B", "D");
            Expect(lines, ref failures, "途中で分岐", lower, "", "A,B,C,D", "A,B,C,D");

            var other = new Shape();
            other.Chain("B", "B_mod");
            other.ChainWithOther("A", "A_mod", "B_mod");
            Expect(lines, ref failures, "ブーリアンの相手", other, "A_mod,B_mod", "A,B", "A,B");

            // 結果がリストの末尾にあっても、別のオブジェクトの子にならない。
            var order = new Shape();
            order.Mesh("A");
            order.Mesh("Later");
            order.Chain("A", "A_mod");
            var parents = MeshHierarchyOps.BuildParentIndicesFromDepth(order.Model);
            int parentOfResult = parents[order.Model.MeshContextList.IndexOf(order.Mesh("A_mod"))];
            bool parentOk = parentOfResult == order.Model.MeshContextList.IndexOf(order.Mesh("A"));
            if (!parentOk) failures++;
            lines.Add($"  {(parentOk ? "合格" : "失敗")} 形 並び：結果の親は元：親の索引 {parentOfResult}");

            AddDefaultsTest(lines, ref failures);
            StageThreeShapes(lines, ref failures);
            return failures;
        }

        // ================================================================
        // 操作統合計画 段 3：追加メニューの既定値と古いデータの読み方
        // ================================================================

        private static MeshObject TestBox(Vector3 min, Vector3 size)
        {
            var mo = new MeshObject("addDefaultsBox");
            for (int i = 0; i < 8; i++)
                mo.Vertices.Add(new Vertex(100 + i, min + Vector3.Scale(size, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1))));
            int[][] f =
            {
                new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 },
                new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 },
            };
            foreach (var q in f) mo.Faces.Add(new Face(q[0], q[1], q[2], q[3], 0));
            return mo;
        }

        /// <summary>平らな帯（四角 3 枚）。normalAxis の向きに厚みが無い。</summary>
        private static MeshObject TestStrip(int normalAxis)
        {
            var mo = new MeshObject("addDefaultsStrip");
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = normalAxis == 1 ? new Vector3(0.5f + i, 0f, row)     // Y に厚みが無い（XZ 平面）
                              : normalAxis == 0 ? new Vector3(0f, 0.5f + i, row)     // X に厚みが無い（YZ 平面）
                              : new Vector3(0.5f + i, row, 0f);                      // Z に厚みが無い（XY 平面）
                    mo.Vertices.Add(new Vertex(200 + row * 4 + i, p));
                }
            for (int i = 0; i < 3; i++) mo.Faces.Add(new Face(i, i + 1, i + 5, i + 4, 0));
            return mo;
        }

        private static bool ShapeChanged(MeshObject a, MeshObject b)
        {
            if (a.Vertices.Count != b.Vertices.Count || a.Faces.Count != b.Faces.Count) return true;
            for (int i = 0; i < a.Vertices.Count; i++)
                if ((a.Vertices[i].Position - b.Vertices[i].Position).sqrMagnitude > 1e-8f) return true;
            return false;
        }

        /// <summary>メニューの全種類について、足した直後に形が変わるか、理由つきで止まるかを形ごとに見る。</summary>
        private static void AddDefaultsTest(List<string> lines, ref int failures)
        {
            var subdivBox = new SubdivisionMeshModifier().Evaluate(new MeshModifierInput
            {
                Source = TestBox(new Vector3(1f, 0f, 0f), Vector3.one),
                Scope  = ResolvedMeshModifierScope.CreateAll(),
            }, out _);
            var shapes = new (string name, MeshObject mesh)[]
            {
                ("箱", TestBox(new Vector3(1f, 0f, 0f), Vector3.one)),
                ("細長い箱", TestBox(new Vector3(1f, -2f, 0.5f), new Vector3(0.2f, 4f, 0.2f))),
                ("XZ 平面の帯", TestStrip(1)),
                ("YZ 平面の帯", TestStrip(0)),
                ("XY 平面の帯", TestStrip(2)),
                ("多段（サブディビジョンの結果）", subdivBox),
            };

            var bad = new List<string>();
            int menuKinds = 0, cases = 0, reasoned = 0;
            foreach (var info in MeshModifierRegistry.All)
            {
                if (info.HideFromMenu) continue;
                menuKinds++;
                foreach (var (name, mesh) in shapes)
                {
                    cases++;
                    var args = MeshModifierRegistry.AddDefaultArgs(info.Kind, mesh);
                    if (!MeshModifierRegistry.FromArgs(info.Kind, args, out var m, out _, out string fe))
                    { bad.Add($"{info.Kind}/{name}: 引数を読めない（{fe}）"); continue; }
                    MeshObject outMesh = null; string error = null;
                    try { outMesh = m.Evaluate(new MeshModifierInput { Source = mesh, Scope = ResolvedMeshModifierScope.CreateAll() }, out error); }
                    catch (System.Exception ex) { error = ex.Message; }

                    // 相手が要るブーリアンだけが、理由つきで止まってよい。
                    bool mayStop = info.Kind == "boolean";
                    if (outMesh == null)
                    {
                        if (mayStop && !string.IsNullOrEmpty(error)) { reasoned++; continue; }
                        bad.Add($"{info.Kind}/{name}: 止まった（{error}）");
                        continue;
                    }
                    bool hinted = m is IMeshModifierHint h && !string.IsNullOrEmpty(h.GetHint());
                    if (ShapeChanged(mesh, outMesh) == hinted) bad.Add($"{info.Kind}/{name}: " + (hinted ? "案内を出しているのに形が変わる" : "形が変わらない"));
                    else if (hinted) reasoned++;
                }
            }
            failures += bad.Count;
            lines.Add($"  {(bad.Count == 0 ? "合格" : "失敗")} 既定値：メニューの {menuKinds} 種類 × 形 {shapes.Length} の {cases} 件で形が変わるか理由つきで止まる（理由つき {reasoned} 件）"
                      + (bad.Count == 0 ? "" : "：" + string.Join(" / ", bad)));

            // キーの無いデータは、プロパティの初期値で読む（保存データとシナリオの結果を変えない）。
            var empty = new Dictionary<string, string>();
            bool bevelOld = MeshModifierRegistry.FromArgs("bevel", empty, out var bm, out var bs, out _)
                         && ((BevelMeshModifier)bm).LimitMethod == BevelLimitMethod.Scope && !bs.Invert;
            bool bendOld = MeshModifierRegistry.FromArgs("bend", empty, out var bd, out _, out _)
                        && ((BendMeshModifier)bd).TotalAngleDeg == 0f;
            bool waveOld = MeshModifierRegistry.FromArgs("wave", empty, out var wv, out _, out _)
                        && ((WaveMeshModifier)wv).AmplitudeX == 0f && ((WaveMeshModifier)wv).CyclesX == 1f;
            bool latticeOld = MeshModifierRegistry.FromArgs("lattice", empty, out var lt, out _, out _)
                           && ((LatticeMeshModifier)lt).Size == Vector3.one && ((LatticeMeshModifier)lt).PointIndices.Length == 0;
            bool oldOk = bevelOld && bendOld && waveOld && latticeOld;
            if (!oldOk) failures++;
            lines.Add($"  {(oldOk ? "合格" : "失敗")} 古いデータ：キーの無い項目はベベル＝頂点グループ・反転なし・曲げ 0 度・波の振幅 0・ラティス 1×1×1 で読む"
                      + (oldOk ? "" : $"（ベベル {bevelOld} 曲げ {bendOld} 波 {waveOld} ラティス {latticeOld}）"));
        }

        /// <summary>段 3 の変更が並列・分岐・多段の見え方を変えないこと。</summary>
        private static void StageThreeShapes(List<string> lines, ref int failures)
        {
            // 並列の入力に頂点 ID を振っても、畳み方と書き出しの行は変わらず、入力の位置も変わらない。
            var parallel = new Shape();
            parallel.Chain("L", "L_mod");
            parallel.Generator("L", "Bone");
            var input = parallel.Mesh("L");
            var posBefore = input.MeshObject.Vertices[0].Position;
            VertexIdOps.AssignMissing(input);
            Expect(lines, ref failures, "並列の入力に頂点 ID を振る", parallel, "", "Bone,L,L_mod", "Bone,L,L_mod");
            bool idOk = !MeshObject.IsUnsetId(input.MeshObject.Vertices[0].Id) && input.MeshObject.Vertices[0].Position == posBefore;
            if (!idOk) failures++;
            lines.Add($"  {(idOk ? "合格" : "失敗")} 形 並列：ID が振られ、位置は変わらない");

            // ミラーの持ち方を切り替えても、並列・分岐・多段の畳み方は変わらない（種類で畳み方を変えない）。
            foreach (string kind in new[] { "mirrorBake", "mirrorDisplay" })
            {
                var pl = new Shape();
                pl.Chain("L", "L_mod");
                pl.Generator("L", "Bone");
                SetLastChainKind(pl, 1, kind);
                Expect(lines, ref failures, $"並列（{kind}）", pl, "", "Bone,L,L_mod", "Bone,L,L_mod");

                var br = new Shape();
                br.Subdiv("A", "A_sub");
                br.Chain("A", "A_mod");
                SetLastChainKind(br, 0, kind);
                Expect(lines, ref failures, $"分岐（{kind}）", br, "", "A,A_mod,A_sub", "A,A_mod,A_sub");

                var mu = new Shape();
                mu.Subdiv("A", "A_sub", 3);
                mu.Chain("A_sub", "A_sub_mod", 9);
                SetLastChainKind(mu, 0, kind);
                Expect(lines, ref failures, $"多段（{kind}）", mu, "A_sub,A_sub_mod", "A", "A");
            }
        }

        private static void SetLastChainKind(Shape shape, int fromEnd, string kind)
        {
            var stacks = shape.Model.ModifierStacks;
            stacks[stacks.Count - 1 - fromEnd].Steps[0].Action = kind;
        }
    }
}
