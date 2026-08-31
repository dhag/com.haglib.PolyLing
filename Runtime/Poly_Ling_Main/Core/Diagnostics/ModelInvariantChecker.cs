// ModelInvariantChecker.cs
// ModelContext が満たすべき不変条件を検査する。UI 非依存。
// Runtime/Poly_Ling_Main/Core/Diagnostics/ に配置
//
// 【何のためにあるか】
//   壊れたモデルは画面上ですぐには判らない。オブジェクトリストは Depth から
//   組むので階層索引が壊れても正常に見えるし、ウェイトの誤りは姿勢を付けるまで
//   出てこない。操作の直後に機械で検査して、どの段で何が壊れたかを確定させる。
//
// 【検査は 2 種類】
//   Snapshot 比較 … 段の前後で「減ってはいけないもの」を見る（ミラーペア本数など）
//   単体検査     … その時点のモデル単体で成立すべきもの
//
// 【方針】
//   ここは判定だけを行い、修復も例外送出もしない。呼び出し側が結果を表示する。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Diagnostics
{
    /// <summary>検査違反 1 件。</summary>
    public class InvariantViolation
    {
        /// <summary>検査項目名。</summary>
        public string Rule = "";

        /// <summary>違反の内容。</summary>
        public string Detail = "";

        public override string ToString() => $"[{Rule}] {Detail}";
    }

    /// <summary>段ごとに必ず出す状態の要約。合否とは別に、値そのものを見せる。</summary>
    public class ModelSummary
    {
        public int MeshContextCount;
        public int BoneCount;
        public int MirrorPairCount;

        /// <summary>左右対（MirrorBoneIndex）が設定されているボーンの本数。</summary>
        public int PairedBoneCount;

        /// <summary>MirrorBoneWeight を持つ頂点の数。</summary>
        public int MirrorWeightedVertexCount;

        public override string ToString()
            => $"要素={MeshContextCount} ボーン={BoneCount} ミラーペア={MirrorPairCount} " +
               $"左右対ボーン={PairedBoneCount} MirrorBW頂点={MirrorWeightedVertexCount}";

        public static ModelSummary Capture(ModelContext model)
        {
            var s = new ModelSummary();
            if (model == null) return s;

            s.MeshContextCount = model.MeshContextCount;
            s.MirrorPairCount  = model.MirrorPairs?.Count ?? 0;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                if (mc.Type == MeshType.Bone)
                {
                    s.BoneCount++;
                    if (mc.MirrorBoneIndex >= 0) s.PairedBoneCount++;
                    continue;
                }

                var mo = mc.MeshObject;
                if (mo == null) continue;
                foreach (var v in mo.Vertices)
                    if (v.MirrorBoneWeight.HasValue) s.MirrorWeightedVertexCount++;
            }
            return s;
        }
    }

    /// <summary>
    /// 段の前後で比較するために控える構造。
    /// MeshContext の参照は持たず、数と種別だけを持つ（比較対象が作り直されても効くように）。
    /// </summary>
    public class ModelStructureSnapshot
    {
        public int MirrorPairCount;
        public int MeshContextCount;
        public int BoneCount;

        /// <summary>ミラー側メッシュの名前。降格の検出に使う。</summary>
        public readonly HashSet<string> MirrorSideNames = new HashSet<string>();

        /// <summary>ミラー実体側の名前。</summary>
        public readonly HashSet<string> RealSideNames = new HashSet<string>();

        /// <summary>
        /// 各要素の「親の実体」。索引ではなく MeshContext 参照で控える。
        ///
        /// 挿入・削除・並べ替えは索引を動かすが、親子の相手（実体）は変わらない。
        /// 索引で比べると必ず食い違うので、実体で比べる。
        /// 親が居ない要素は値 null で入れる。
        /// </summary>
        public readonly Dictionary<MeshContext, MeshContext> ParentOf
            = new Dictionary<MeshContext, MeshContext>();

        /// <summary>各要素のそのときの索引。違反の説明に数値を出すために持つ。</summary>
        public readonly Dictionary<MeshContext, int> IndexOf
            = new Dictionary<MeshContext, int>();

        /// <summary>各要素のそのときの親索引（生値）。</summary>
        public readonly Dictionary<MeshContext, int> ParentIndexOf
            = new Dictionary<MeshContext, int>();

        public static ModelStructureSnapshot Capture(ModelContext model)
        {
            var s = new ModelStructureSnapshot();
            if (model == null) return s;

            s.MirrorPairCount  = model.MirrorPairs?.Count ?? 0;
            s.MeshContextCount = model.MeshContextCount;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                if (mc.Type == MeshType.Bone) s.BoneCount++;
                if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror)
                    s.MirrorSideNames.Add(mc.Name ?? "");

                int par = mc.HierarchyParentIndex;
                s.ParentOf[mc] = (par >= 0 && par < model.MeshContextCount)
                    ? model.GetMeshContext(par)
                    : null;
                s.IndexOf[mc]       = i;
                s.ParentIndexOf[mc] = par;
            }

            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    if (pair?.Real != null) s.RealSideNames.Add(pair.Real.Name ?? "");
                }
            }

            return s;
        }
    }

    public static class ModelInvariantChecker
    {
        private static readonly Dictionary<MeshContext, MeshContext> EmptyParentMap
            = new Dictionary<MeshContext, MeshContext>();

        // ================================================================
        // 段の前後で比較する
        // ================================================================

        /// <summary>
        /// 前の段からの退行を検出する。編集操作で減ってはいけないものを見る。
        /// </summary>
        /// <param name="checkParentIdentity">
        /// 親の相手が変わっていないかを見るか。
        /// スキンド変換はメッシュの親をボーンへ張り替えるのが仕様なので、その段では false。
        /// </param>
        public static List<InvariantViolation> CompareWithPrevious(
            ModelStructureSnapshot before, ModelContext model, bool checkParentIdentity = true)
        {
            var list = new List<InvariantViolation>();
            if (before == null || model == null) return list;

            var after = ModelStructureSnapshot.Capture(model);

            if (after.MirrorPairCount < before.MirrorPairCount)
            {
                list.Add(new InvariantViolation
                {
                    Rule   = "ミラーペアの本数",
                    Detail = $"{before.MirrorPairCount} → {after.MirrorPairCount} に減った。" +
                             "編集操作でペアを解体してはならない。",
                });
            }

            foreach (string name in before.MirrorSideNames)
            {
                if (after.MirrorSideNames.Contains(name)) continue;
                list.Add(new InvariantViolation
                {
                    Rule   = "ミラー側の降格",
                    Detail = $"\"{name}\" がミラー側ではなくなった（Mesh へ降格したか消えた）。",
                });
            }

            foreach (string name in before.RealSideNames)
            {
                if (after.RealSideNames.Contains(name)) continue;
                list.Add(new InvariantViolation
                {
                    Rule   = "実体側の喪失",
                    Detail = $"\"{name}\" がミラーの実体側ではなくなった。",
                });
            }

            // 親の相手（実体）が変わっていないか。
            // 挿入・削除で索引は動くが、親子の相手は変わってはいけない。
            // 索引の付け替え漏れはここで出る。
            foreach (var kv in checkParentIdentity ? before.ParentOf : EmptyParentMap)
            {
                var child = kv.Key;
                if (child == null) continue;
                if (!after.ParentOf.TryGetValue(child, out var nowParent)) continue;   // 消えた要素は別項目

                var wasParent = kv.Value;
                if (ReferenceEquals(wasParent, nowParent)) continue;

                before.IndexOf.TryGetValue(child, out int oldChildIdx);
                after.IndexOf.TryGetValue(child, out int newChildIdx);
                before.ParentIndexOf.TryGetValue(child, out int oldParIdx);
                after.ParentIndexOf.TryGetValue(child, out int newParIdx);

                int expectParIdx = (wasParent != null && after.IndexOf.TryGetValue(wasParent, out int wp))
                    ? wp : -1;

                list.Add(new InvariantViolation
                {
                    Rule   = "親の付け替わり",
                    Detail = $"\"{child.Name}\" 索引 {oldChildIdx}→{newChildIdx}: " +
                             $"親索引 {oldParIdx}→{newParIdx}（期待 {expectParIdx}）。" +
                             $"相手が \"{wasParent?.Name ?? "（なし）"}\" から " +
                             $"\"{nowParent?.Name ?? "（なし）"}\" へ変わった。",
                });
            }

            return list;
        }

        // ================================================================
        // 単体検査
        // ================================================================

        /// <summary>その時点のモデル単体で成立すべき条件を全部見る。</summary>
        public static List<InvariantViolation> CheckAll(ModelContext model)
        {
            var list = new List<InvariantViolation>();
            if (model == null)
            {
                list.Add(new InvariantViolation { Rule = "モデル", Detail = "モデルが無い" });
                return list;
            }

            CheckSharedMeshObject(model, list);
            CheckIndexReferences(model, list);
            CheckBoneWeightTargets(model, list);
            CheckRestPoseDisplacement(model, list);
            CheckMirrorPairWeights(model, list);
            CheckBranchMirrorCoverage(model, list);

            return list;
        }

        /// <summary>
        /// ミラー分岐ルート配下のメッシュが、全部ミラーを持っているか。
        ///
        /// 分岐ルートを立てた枝は、根から末端まで左右 2 本になるのが仕様。
        /// 途中で止まっていたら、枝の走査が親子を辿れていない。
        /// スキンド変換後（ボーンが在る）だけ見る。
        /// </summary>
        private static void CheckBranchMirrorCoverage(ModelContext model, List<InvariantViolation> list)
        {
            int n = model.MeshContextCount;

            bool hasAnyBone = false;
            for (int i = 0; i < n; i++)
                if (model.GetMeshContext(i)?.Type == MeshType.Bone) { hasAnyBone = true; break; }
            if (!hasAnyBone) return;

            // ミラー側を持つ実体の集合
            var hasMirror = new HashSet<MeshContext>();
            if (model.MirrorPairs != null)
                foreach (var pair in model.MirrorPairs)
                    if (pair?.Real != null) hasMirror.Add(pair.Real);

            // 分岐ルートの子孫を辿る
            for (int i = 0; i < n; i++)
            {
                var root = model.GetMeshContext(i);
                if (root == null || !root.IsMirrorBranchRoot) continue;

                foreach (int d in DescendantsOf(model, i))
                {
                    var mc = model.GetMeshContext(d);
                    if (mc?.MeshObject == null) continue;
                    if (mc.Type == MeshType.Bone) continue;
                    if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) continue;
                    if (mc.MeshObject.VertexCount == 0) continue;   // 関節はボーン側で複製される
                    if (hasMirror.Contains(mc)) continue;

                    list.Add(new InvariantViolation
                    {
                        Rule   = "分岐配下のミラー欠落",
                        Detail = $"分岐ルート \"{root.Name}\" 配下の \"{mc.Name}\" にミラーが無い。",
                    });
                }
            }
        }

        /// <summary>root 自身と、HierarchyParentIndex を辿って root に至る全要素。</summary>
        private static List<int> DescendantsOf(ModelContext model, int root)
        {
            var result = new List<int>();
            int n = model.MeshContextCount;

            for (int i = 0; i < n; i++)
            {
                int cur = i;
                for (int step = 0; step <= n; step++)
                {
                    if (cur == root) { result.Add(i); break; }
                    var mc = model.GetMeshContext(cur);
                    if (mc == null) break;
                    int par = mc.HierarchyParentIndex;
                    if (par < 0 || par >= n || par == cur) break;
                    cur = par;
                }
            }
            return result;
        }

        /// <summary>2 つの MeshContext が同じ MeshObject を共有していないか。</summary>
        private static void CheckSharedMeshObject(ModelContext model, List<InvariantViolation> list)
        {
            var seen = new Dictionary<MeshObject, int>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                if (seen.TryGetValue(mo, out int first))
                {
                    list.Add(new InvariantViolation
                    {
                        Rule   = "MeshObject の共有",
                        Detail = $"要素 {first} と要素 {i} が同じ MeshObject を指している" +
                                 $"（\"{mc.Name}\"）。片方の書き込みがもう片方を壊す。",
                    });
                }
                else seen[mo] = i;
            }
        }

        /// <summary>索引参照が範囲内で、自己参照・循環が無いか。</summary>
        private static void CheckIndexReferences(ModelContext model, List<InvariantViolation> list)
        {
            int n = model.MeshContextCount;

            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                CheckOneIndex(list, "HierarchyParentIndex", i, mc.Name, mc.HierarchyParentIndex, n);
                CheckOneIndex(list, "ParentIndex",          i, mc.Name, mc.ParentIndex,          n);
                CheckOneIndex(list, "MorphParentIndex",     i, mc.Name, mc.MorphParentIndex,     n);
                CheckOneIndex(list, "MirrorOfMorphIndex",   i, mc.Name, mc.MirrorOfMorphIndex,   n);
                CheckOneIndex(list, "BakedMirrorSourceIndex", i, mc.Name, mc.BakedMirrorSourceIndex, n);
                CheckOneIndex(list, "MirrorBoneIndex",      i, mc.Name, mc.MirrorBoneIndex,      n);

                // ParentIndex と HierarchyParentIndex の食い違いは検査しない。
                // 2 つは同じ入れ物になったので、構造的に食い違えない
                // （MeshObject.ParentIndex が HierarchyParentIndex への委譲）。

                // 左右対の相互性。A の相手が B なら B の相手は A。
                if (mc.MirrorBoneIndex >= 0 && mc.MirrorBoneIndex < n)
                {
                    var peer = model.GetMeshContext(mc.MirrorBoneIndex);
                    if (peer != null && peer.MirrorBoneIndex != i)
                    {
                        list.Add(new InvariantViolation
                        {
                            Rule   = "左右対の相互性",
                            Detail = $"要素 {i} \"{mc.Name}\" の相手は {mc.MirrorBoneIndex} だが、" +
                                     $"相手側の相手は {peer.MirrorBoneIndex}",
                        });
                    }
                }
            }

            // 階層の循環
            for (int i = 0; i < n; i++)
            {
                int cur = i;
                for (int step = 0; step <= n; step++)
                {
                    var mc = model.GetMeshContext(cur);
                    if (mc == null) break;
                    int par = mc.HierarchyParentIndex;
                    if (par < 0 || par >= n) break;
                    if (par == cur)
                    {
                        list.Add(new InvariantViolation
                        {
                            Rule   = "階層の自己参照",
                            Detail = $"要素 {cur} \"{mc.Name}\" が自分自身を親にしている",
                        });
                        break;
                    }
                    cur = par;
                    if (step == n)
                    {
                        list.Add(new InvariantViolation
                        {
                            Rule   = "階層の循環",
                            Detail = $"要素 {i} から親を辿ると循環する",
                        });
                    }
                }
            }
        }

        private static void CheckOneIndex(
            List<InvariantViolation> list, string field, int index, string name, int value, int count)
        {
            if (value < 0) return;
            if (value >= count)
            {
                list.Add(new InvariantViolation
                {
                    Rule   = "索引の範囲外",
                    Detail = $"要素 {index} \"{name}\" の {field}={value}（要素数 {count}）",
                });
            }
        }

        /// <summary>BoneWeight / MirrorBoneWeight の参照先が Bone 種別を指しているか。</summary>
        private static void CheckBoneWeightTargets(ModelContext model, List<InvariantViolation> list)
        {
            int n = model.MeshContextCount;
            bool hasAnyBone = false;
            for (int i = 0; i < n; i++)
                if (model.GetMeshContext(i)?.Type == MeshType.Bone) { hasAnyBone = true; break; }

            if (!hasAnyBone) return;   // スキン前のモデルは対象外

            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null || mc.Type == MeshType.Bone) continue;

                var badNormal = new HashSet<int>();
                var badMirror = new HashSet<int>();

                foreach (var v in mo.Vertices)
                {
                    if (v.BoneWeight.HasValue)
                        CollectBadBones(model, v.BoneWeight.Value, badNormal);
                    if (v.MirrorBoneWeight.HasValue)
                        CollectBadBones(model, v.MirrorBoneWeight.Value, badMirror);
                }

                if (badNormal.Count > 0)
                {
                    list.Add(new InvariantViolation
                    {
                        Rule   = "BoneWeight の参照先",
                        Detail = $"\"{mc.Name}\": ボーンでない索引を参照している " +
                                 $"[{string.Join(",", badNormal.OrderBy(x => x))}]",
                    });
                }
                if (badMirror.Count > 0)
                {
                    list.Add(new InvariantViolation
                    {
                        Rule   = "MirrorBoneWeight の参照先",
                        Detail = $"\"{mc.Name}\": ボーンでない索引を参照している " +
                                 $"[{string.Join(",", badMirror.OrderBy(x => x))}]",
                    });
                }
            }
        }

        private static void CollectBadBones(ModelContext model, BoneWeight bw, HashSet<int> bad)
        {
            void One(int idx, float w)
            {
                if (w <= 0f) return;
                if (idx < 0 || idx >= model.MeshContextCount) { bad.Add(idx); return; }
                if (model.GetMeshContext(idx)?.Type != MeshType.Bone) bad.Add(idx);
            }
            One(bw.boneIndex0, bw.weight0);
            One(bw.boneIndex1, bw.weight1);
            One(bw.boneIndex2, bw.weight2);
            One(bw.boneIndex3, bw.weight3);
        }

        /// <summary>
        /// 静止時のスキン変位。ボーンの現在ワールド行列と BindPose を合成した
        /// スキン行列は、姿勢を付けていない状態では単位行列でなければならない。
        /// ここが 0 でなければ、ウェイトの参照先か BindPose のどちらかが壊れている。
        /// </summary>
        private static void CheckRestPoseDisplacement(ModelContext model, List<InvariantViolation> list)
        {
            const float tolerance = 1e-3f;

            int n = model.MeshContextCount;
            bool hasAnyBone = false;
            for (int i = 0; i < n; i++)
                if (model.GetMeshContext(i)?.Type == MeshType.Bone) { hasAnyBone = true; break; }
            if (!hasAnyBone) return;

            model.ComputeWorldMatrices();

            // ボーンごとのスキン行列
            var skin = new Dictionary<int, Matrix4x4>();
            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                skin[i] = mc.WorldMatrix * mc.BindPose;
            }

            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null || mc.Type == MeshType.Bone) continue;

                float worst = 0f;
                int worstVertex = -1;

                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var vert = mo.Vertices[v];
                    if (!vert.BoneWeight.HasValue) continue;

                    var bw  = vert.BoneWeight.Value;
                    var pos = vert.Position;

                    Vector3 acc = Vector3.zero;
                    float wsum = 0f;

                    void Add(int idx, float w)
                    {
                        if (w <= 0f) return;
                        if (!skin.TryGetValue(idx, out var m)) return;
                        acc  += m.MultiplyPoint3x4(pos) * w;
                        wsum += w;
                    }
                    Add(bw.boneIndex0, bw.weight0);
                    Add(bw.boneIndex1, bw.weight1);
                    Add(bw.boneIndex2, bw.weight2);
                    Add(bw.boneIndex3, bw.weight3);

                    if (wsum <= 0f) continue;

                    float d = Vector3.Distance(acc, pos * wsum);
                    if (d > worst) { worst = d; worstVertex = v; }
                }

                if (worst > tolerance)
                {
                    list.Add(new InvariantViolation
                    {
                        Rule   = "静止時のスキン変位",
                        Detail = $"\"{mc.Name}\": 頂点 {worstVertex} が {worst:F4} ずれる。" +
                                 "ウェイトの参照先か BindPose が壊れている。",
                    });
                }
            }
        }

        /// <summary>
        /// ミラーペアの左右で、対応する頂点のウェイトが左右対応ボーンどうしか。
        /// 左右対応は MirrorBoneIndex（スキンド変換の確定値）を正本とする。
        /// </summary>
        private static void CheckMirrorPairWeights(ModelContext model, List<InvariantViolation> list)
        {
            if (model.MirrorPairs == null) return;

            foreach (var pair in model.MirrorPairs)
            {
                if (pair?.Real?.MeshObject == null || pair.Mirror?.MeshObject == null) continue;
                if (pair.VertexMap == null) continue;

                var realMesh   = pair.Real.MeshObject;
                var mirrorMesh = pair.Mirror.MeshObject;

                int mismatches = 0;
                string firstDetail = null;

                for (int i = 0; i < pair.VertexMap.Length && i < realMesh.VertexCount; i++)
                {
                    int mi = pair.VertexMap[i];
                    if (mi < 0 || mi >= mirrorMesh.VertexCount) continue;

                    var rv = realMesh.Vertices[i];
                    var mv = mirrorMesh.Vertices[mi];
                    if (!rv.BoneWeight.HasValue || !mv.BoneWeight.HasValue) continue;

                    var rb = rv.BoneWeight.Value;
                    var mb = mv.BoneWeight.Value;

                    string bad = CompareSlots(model, rb, mb);
                    if (bad == null) continue;

                    mismatches++;
                    if (firstDetail == null)
                        firstDetail = $"頂点 {i}/{mi}: {bad}";
                }

                if (mismatches > 0)
                {
                    list.Add(new InvariantViolation
                    {
                        Rule   = "ミラーペアのウェイト対応",
                        Detail = $"\"{pair.Real.Name}\" ↔ \"{pair.Mirror.Name}\": " +
                                 $"{mismatches} 頂点が左右対応ボーンになっていない。{firstDetail}",
                    });
                }
            }
        }

        /// <summary>不一致があれば説明を返す。一致なら null。</summary>
        private static string CompareSlots(ModelContext model, BoneWeight rb, BoneWeight mb)
        {
            int[]   rBones = { rb.boneIndex0, rb.boneIndex1, rb.boneIndex2, rb.boneIndex3 };
            float[] rW     = { rb.weight0,    rb.weight1,    rb.weight2,    rb.weight3    };
            int[]   mBones = { mb.boneIndex0, mb.boneIndex1, mb.boneIndex2, mb.boneIndex3 };
            float[] mW     = { mb.weight0,    mb.weight1,    mb.weight2,    mb.weight3    };

            for (int s = 0; s < 4; s++)
            {
                if (rW[s] <= 0f && mW[s] <= 0f) continue;

                if (Mathf.Abs(rW[s] - mW[s]) > 1e-3f)
                    return $"スロット {s} のウェイトが違う（{rW[s]:F3} / {mW[s]:F3}）";

                var realBoneCtx = (rBones[s] >= 0 && rBones[s] < model.MeshContextCount)
                    ? model.GetMeshContext(rBones[s]) : null;
                if (realBoneCtx == null) return $"スロット {s} の実体側ボーン索引が範囲外";

                int expected = realBoneCtx.MirrorBoneIndex;
                if (expected < 0) expected = rBones[s];   // 中心線上のボーンは共有

                if (expected != mBones[s])
                {
                    var got = (mBones[s] >= 0 && mBones[s] < model.MeshContextCount)
                        ? model.GetMeshContext(mBones[s])?.Name : "?";
                    var want = (expected >= 0 && expected < model.MeshContextCount)
                        ? model.GetMeshContext(expected)?.Name : "?";
                    return $"スロット {s} は \"{want}\"({expected}) のはずが \"{got}\"({mBones[s]})";
                }
            }
            return null;
        }
    }
}
