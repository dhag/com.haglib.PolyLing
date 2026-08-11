// ObjectPoseWedgeReader.cs
// くさびオブジェクトの列を読み、メッシュオブジェクトの姿勢へ戻すための
// 「名前 → ワールド姿勢」を取り出す。ModelContext は読むだけ。
// Runtime/Poly_Ling_Main/Tools/ObjectPose/ に配置
//
// 【くさび自身のトランスフォームも見る理由】
//   生成直後のくさびは BoneTransform が単位で、頂点がワールド座標に入っている。
//   ただし生成後に「オブジェクト姿勢」ツールでくさびごと動かした場合は
//   頂点ではなく BoneTransform 側が変わる。両方を掛けたものが実際の見た目なので、
//   ワールド行列 × 形状から読んだローカル姿勢 を最終的な姿勢とする。
//
// 【空のオブジェクトを飛ばす理由】
//   姿勢を持たない（＝生成時にローカル単位だった、または頂点を持たなかった）
//   ノードには情報が無い。触らないことで元の姿勢がそのまま残る。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.ObjectPose
{
    /// <summary>くさび1個から読んだ姿勢。</summary>
    public sealed class ObjectPoseEntry
    {
        /// <summary>適用先の元メッシュ名（くさび名から "_bone" を外したもの）。</summary>
        public string Name;

        /// <summary>くさびオブジェクト自身の名前。</summary>
        public string WedgeName;

        /// <summary>根のワールド位置。</summary>
        public Vector3 WorldPosition;

        /// <summary>くさびのワールド姿勢。</summary>
        public Quaternion WorldRotation;

        /// <summary>読み取り元の MeshContextList 索引。</summary>
        public int SourceIndex = -1;
    }

    public static class ObjectPoseWedgeReader
    {
        // ================================================================
        // コンテナ探索
        // ================================================================

        /// <summary>
        /// くさびのコンテナ（ルート直下・名前が baseName で始まる空オブジェクト）を探す。
        /// 複数あれば最も後ろのものを返す。見つからなければ -1。
        /// </summary>
        public static int FindContainer(ModelContext model, string baseName)
        {
            if (model == null) return -1;
            if (string.IsNullOrEmpty(baseName))
                baseName = ObjectPoseWedgeGenerator.DefaultContainerName;

            int found = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Mesh) continue;
                if (mc.HierarchyParentIndex >= 0) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Name != baseName && !mc.Name.StartsWith(baseName + "_")) continue;
                found = i;
            }
            return found;
        }

        /// <summary>
        /// くさびを最も多く配下に持つコンテキストを探す。
        ///
        /// 名前に依存しないので、コンテナを改名しても、別の親に付け替えても、
        /// MQO を往復して素性が変わっても見つかる。
        ///
        /// 計算量は O(n + くさび数 × 深さ)。全ノードで部分木を作り直すより軽い。
        /// </summary>
        /// <param name="wedgeCount">見つかったコンテナ配下のくさび数</param>
        public static int FindBestContainer(ModelContext model, out int wedgeCount)
        {
            wedgeCount = 0;
            if (model == null) return -1;

            int n = model.MeshContextCount;
            if (n == 0) return -1;

            // 1) くさびとして読めるノードを洗い出す
            var isWedge = new bool[n];
            int totalWedges = 0;
            for (int i = 0; i < n; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (ObjectPoseWedgeShape.TryReadPose(mc.MeshObject, out _, out _))
                {
                    isWedge[i] = true;
                    totalWedges++;
                }
            }
            if (totalWedges == 0) return -1;

            // 2) 各くさびから親をさかのぼり、祖先の得票を増やす
            var votes = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (!isWedge[i]) continue;

                int cur = model.GetMeshContext(i)?.HierarchyParentIndex ?? -1;
                int safety = n + 1;
                while (cur >= 0 && cur < n && safety-- > 0)
                {
                    votes[cur]++;
                    cur = model.GetMeshContext(cur)?.HierarchyParentIndex ?? -1;
                }
            }

            // 3) 得票が最大のもの。同数なら浅い方（＝より根に近い方）を採る
            int best = -1, bestVotes = 0, bestDepth = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (votes[i] <= 0) continue;
                if (isWedge[i]) continue;             // くさび自身はコンテナにしない

                int depth = 0, cur = model.GetMeshContext(i)?.HierarchyParentIndex ?? -1;
                int safety = n + 1;
                while (cur >= 0 && cur < n && safety-- > 0)
                {
                    depth++;
                    cur = model.GetMeshContext(cur)?.HierarchyParentIndex ?? -1;
                }

                if (votes[i] > bestVotes || (votes[i] == bestVotes && depth < bestDepth))
                {
                    best = i; bestVotes = votes[i]; bestDepth = depth;
                }
            }

            wedgeCount = bestVotes;
            return best;
        }

        /// <summary>
        /// 取り込みに使うコンテナを決める。
        ///   1) 明示指定（選択）が実際にくさびを持っていればそれ
        ///   2) 既定名で見つかり、かつくさびを持っていればそれ
        ///   3) くさびを最も多く持つノード
        /// どれも駄目なら -1。reason に判断の経緯が入る。
        /// </summary>
        public static int ResolveContainer(
            ModelContext model, int explicitIndex, string baseName, out string reason)
        {
            reason = "";
            if (model == null) { reason = "モデルがありません"; return -1; }

            if (explicitIndex >= 0 && explicitIndex < model.MeshContextCount)
            {
                int c = CountWedgesUnder(model, explicitIndex);
                if (c > 0)
                {
                    reason = $"選択中の [{explicitIndex}]{model.GetMeshContext(explicitIndex)?.Name} を使用（くさび {c} 件）";
                    return explicitIndex;
                }
                reason = $"選択中の [{explicitIndex}]{model.GetMeshContext(explicitIndex)?.Name} " +
                         "の配下にくさびが無いため、自動検出に切り替え / ";
            }

            int named = FindContainer(model, baseName);
            if (named >= 0)
            {
                int c = CountWedgesUnder(model, named);
                if (c > 0)
                {
                    reason += $"名前一致の [{named}]{model.GetMeshContext(named)?.Name} を使用（くさび {c} 件）";
                    return named;
                }
                reason += $"名前一致の [{named}] は配下にくさびが無い / ";
            }

            int best = FindBestContainer(model, out int bestCount);
            if (best >= 0)
            {
                reason += $"くさびを最も多く持つ [{best}]{model.GetMeshContext(best)?.Name} を使用（くさび {bestCount} 件）";
                return best;
            }

            reason += "モデル内にくさびとして読めるオブジェクトが1つもありません";
            return -1;
        }

        /// <summary>指定ノードの配下にあるくさびの数。</summary>
        public static int CountWedgesUnder(ModelContext model, int index)
        {
            var subtree = CollectSubtree(model, index);
            int c = 0;
            foreach (int i in subtree)
            {
                if (i == index) continue;
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (ObjectPoseWedgeShape.TryReadPose(mc.MeshObject, out _, out _)) c++;
            }
            return c;
        }

        /// <summary>
        /// くさび関連のコンテキスト索引をすべて集める。
        /// 原点CSVなど「モデル本体のオブジェクトだけを対象にしたい」処理で除外集合として使う。
        ///
        /// 判定は生成時の規則に合わせて2つ。
        ///   1. 名前が接尾辞 "_bone" で終わる（くさび本体・空ノードともこの規則で付く）
        ///   2. コンテナ（ルート直下・名前が baseName で始まる）の配下
        ///
        /// 中身（形状がくさびとして読めるか）では判定しない。くさびを実オブジェクトの
        /// 下に付け替えていた場合、その親ごと巻き込んで除外してしまうため。
        /// 名前を変えれば対象から外せる、という予測しやすさを優先する。
        /// </summary>
        public static HashSet<int> CollectWedgeIndices(ModelContext model, string baseName = null)
        {
            var result = new HashSet<int>();
            if (model == null) return result;

            if (string.IsNullOrEmpty(baseName))
                baseName = ObjectPoseWedgeGenerator.DefaultContainerName;

            // 1) 接尾辞
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                string name = model.GetMeshContext(i)?.Name;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Length > ObjectPoseWedgeGenerator.WedgeNameSuffix.Length &&
                    name.EndsWith(ObjectPoseWedgeGenerator.WedgeNameSuffix))
                    result.Add(i);
            }

            // 2) コンテナ配下（複数世代ぶん。ObjectPose / ObjectPose_1 …）
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Mesh) continue;
                if (mc.HierarchyParentIndex >= 0) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Name != baseName && !mc.Name.StartsWith(baseName + "_")) continue;

                foreach (int k in CollectSubtree(model, i)) result.Add(k);
            }

            return result;
        }

        // ================================================================
        // 部分木の収集
        // ================================================================

        /// <summary>コンテナ自身とその全子孫の索引。適用先を探すときの除外集合になる。</summary>
        public static HashSet<int> CollectSubtree(ModelContext model, int containerIndex)
        {
            var result = new HashSet<int>();
            if (model == null) return result;

            int count = model.MeshContextCount;
            if (containerIndex < 0 || containerIndex >= count) return result;

            result.Add(containerIndex);

            // 親が確定済みなら子も確定する。前後どちらに並んでいても拾えるよう回す。
            for (int pass = 0; pass < count; pass++)
            {
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    if (result.Contains(i)) continue;
                    int p = model.GetMeshContext(i)?.HierarchyParentIndex ?? -1;
                    if (p < 0 || p >= count) continue;
                    if (result.Contains(p)) { result.Add(i); changed = true; }
                }
                if (!changed) break;
            }

            return result;
        }

        // ================================================================
        // 読み取り
        // ================================================================

        /// <summary>
        /// コンテナ配下のくさびから姿勢を読む。読めなかったノード（空のオブジェクト等）は飛ばす。
        /// </summary>
        public static List<ObjectPoseEntry> Read(ModelContext model, int containerIndex)
        {
            var entries = new List<ObjectPoseEntry>();
            if (model == null) return entries;

            var subtree = CollectSubtree(model, containerIndex);
            if (subtree.Count == 0) return entries;

            model.ComputeWorldMatrices();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (i == containerIndex || !subtree.Contains(i)) continue;

                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;

                if (!ObjectPoseWedgeShape.TryReadPose(mc.MeshObject, out var localPos, out var localRot))
                    continue;

                Matrix4x4 world = mc.WorldMatrix * Matrix4x4.TRS(localPos, localRot, Vector3.one);

                entries.Add(new ObjectPoseEntry
                {
                    Name          = ObjectPoseWedgeGenerator.ToMeshName(mc.Name),
                    WedgeName     = mc.Name,
                    WorldPosition = ObjectPoseWedgeShape.PositionOf(world),
                    WorldRotation = ObjectPoseWedgeShape.RotationOf(world),
                    SourceIndex   = i,
                });
            }

            return entries;
        }
    }
}
