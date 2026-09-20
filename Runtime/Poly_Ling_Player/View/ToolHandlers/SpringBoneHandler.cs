// SpringBoneHandler.cs
// 揺れもの編集パネルの一覧（鎖・検査）と 3D 画面の強調表示を受け持つ（操作経路統一計画.md E）。
//
// PlayerSpringBoneSubPanel がモデルを直接読んで組み立てていた一覧（鎖の名前と段数、検査結果、
// 評価設定）と、ModelContext.SpringBoneHighlight* へ直接書いていた強調表示を、ここへ移した。
// パネルは refreshSummary を呼んで概要を読み、強調は updateHighlight / clearHighlight で頼む。
// 強調表示は保存しない画面上の状態で、データ（揺れもの設定）は変えない。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    [PLTool("springBone", Description = "揺れもの編集の一覧（鎖・検査）と 3D 画面の強調表示")]
    public sealed class SpringBoneHandler
    {
        public Func<ModelContext> GetModel;
        /// <summary>強調表示を書き換えた後に呼ぶ（ビューポートに作り直させる）。</summary>
        public Action OnHighlightChanged;

        [PLToolState(Description = "鎖の一覧の行（名前・根元・段数）")]
        public string[] ChainRows { get; private set; } = Array.Empty<string>();
        [PLToolState(Description = "鎖の一覧の各行の根元の MeshContextList 索引")]
        public int[]    ChainMasters { get; private set; } = Array.Empty<int>();
        [PLToolState(Description = "検査結果の行")]
        public string[] IssueRows { get; private set; } = Array.Empty<string>();
        [PLToolState(Description = "検査結果の各行の対象の索引（-1 は対象なし）")]
        public int[]    IssueMasters { get; private set; } = Array.Empty<int>();
        [PLToolState(Description = "評価設定: 固定時間刻み")]
        public float    FixedDeltaTime => GetModel?.Invoke()?.SpringBoneFixedDeltaTime ?? 0f;
        [PLToolState(Description = "評価設定: 助走フレーム数")]
        public int      WarmupFrames   => GetModel?.Invoke()?.SpringBoneWarmupFrames ?? 0;
        [PLToolState(Description = "直近の updateHighlight の「いまの場所」の文（空なら表示なし）")]
        public string   PlaceText { get; private set; } = "";

        [PLToolAction(Description = "鎖の一覧・検査結果を作り直す（概要 chainRows・issueRows などに入れる）")]
        public void RefreshSummary()
        {
            var model = GetModel?.Invoke();
            var chainRows = new List<string>(); var chainMasters = new List<int>();
            var issueRows = new List<string>(); var issueMasters = new List<int>();
            if (model != null)
            {
                var childrenOf = MeshHierarchyOps.BuildChildrenTable(model);
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    var mo = mc?.MeshObject;
                    if (mo?.SpringBoneChainRoot == null) continue;
                    int members = SpringBoneOps.CollectJointMembers(model, childrenOf, i).Count;
                    chainMasters.Add(i);
                    chainRows.Add($"{SpringBoneOps.ChainName(mo, mc)}  ({mc.Name} / {members} 段)");
                }
                foreach (var issue in SpringBoneOps.Validate(model))
                {
                    issueRows.Add((issue.IsError ? "NG  " : "注意  ") + issue.Message);
                    issueMasters.Add(issue.MasterIndex);
                }
            }
            ChainRows = chainRows.ToArray(); ChainMasters = chainMasters.ToArray();
            IssueRows = issueRows.ToArray(); IssueMasters = issueMasters.ToArray();
        }

        /// <summary>
        /// いま触っている鎖と、その中の何段目かを 3D 画面へ伝える。
        /// 鎖は active から親を辿った いちばん上の「揺れの根元」。見つからなければ active を根元とみなす。
        /// </summary>
        [PLToolAction(Description = "指定したボーン（-1 で無し）を含む鎖を 3D 画面で強調し、placeText を作る")]
        public void UpdateHighlight(int active)
        {
            var model = GetModel?.Invoke();
            if (model == null) return;

            var members = new List<int>();
            int rootIndex = -1;
            if (active >= 0)
            {
                rootIndex = FindChainRoot(model, active);
                if (rootIndex >= 0)
                    members = SpringBoneOps.CollectJointMembers(model, MeshHierarchyOps.BuildChildrenTable(model), rootIndex);
            }

            model.SpringBoneHighlightIndices     = members;
            model.SpringBoneHighlightActiveIndex = active;

            // 文字でも同じことを出す。色だけでは何段目かまでは読めない。
            if (active < 0 || members.Count == 0)
            {
                PlaceText = "";
            }
            else
            {
                int step = members.IndexOf(active);
                var rootMc = model.GetMeshContext(rootIndex);
                string chainName  = SpringBoneOps.ChainName(rootMc?.MeshObject, rootMc);
                string activeName = model.GetMeshContext(active)?.Name ?? "";
                PlaceText = step >= 0
                    ? $"いまの場所: 鎖「{chainName}」の {step + 1} / {members.Count} 段目（{activeName}）"
                    : $"いまの場所: 鎖「{chainName}」の外（{activeName}）";
            }
            OnHighlightChanged?.Invoke();
        }

        [PLToolAction(Description = "3D 画面の強調表示を消す（出していなければ何もしない）")]
        public void ClearHighlight()
        {
            var model = GetModel?.Invoke();
            if (model == null) return;
            bool had = model.SpringBoneHighlightActiveIndex >= 0
                    || (model.SpringBoneHighlightIndices != null && model.SpringBoneHighlightIndices.Count > 0);
            PlaceText = "";
            if (!had) return;
            model.ClearSpringBoneHighlight();
            OnHighlightChanged?.Invoke();
        }

        /// <summary>親を辿って、いちばん上の「揺れの根元」を返す。見つからなければ自分自身。</summary>
        private static int FindChainRoot(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return -1;
            var parents = MeshHierarchyOps.BuildParentIndicesFromDepth(model);
            int cur = index, found = -1;
            var guard = new HashSet<int>();
            while (cur >= 0 && cur < model.MeshContextCount && guard.Add(cur))
            {
                var mo = model.GetMeshContext(cur)?.MeshObject;
                if (mo?.SpringBoneChainRoot != null) found = cur;
                cur = (parents != null && cur < parents.Length) ? parents[cur] : -1;
            }
            return found >= 0 ? found : index;
        }
    }
}
