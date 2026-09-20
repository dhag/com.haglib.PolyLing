// BlendToolHandler.cs
// メッシュブレンドの試し表示（プレビュー）を受け持つ（操作経路統一計画.md E。M-2 と同じ方式）。
//
// 【何を持つか】
//   PlayerBlendSubPanel が持っていた BlendPreviewState（宛先の頂点を書き換えて見せ、終了時に戻す）、
//   プレビュー前の担当者判定とロック、ソースの一時非表示、対応率の集計、対応方式の注意書きを持つ。
//   ソースと重み・宛先の選択はパネルが持ち、操作のたびに引数で渡す。
//
// 【確定】
//   確定は ApplyBlendCommand（パネルが送る）。受け口は自前の BlendPreviewState を作り直すため、
//   パネルは確定の前に endPreview でこちらのプレビューを畳む（従来と同じ順序）。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    [PLTool("blend", Description = "メッシュブレンドの試し表示")]
    public sealed class BlendToolHandler
    {
        public Func<ModelContext>      GetModel;
        public Func<int, ModelContext> GetModelContext;
        public Func<int>               GetModelIndex;
        public Func<Poly_Ling.Tools.ToolContext> BuildToolContext;
        /// <summary>法線を GPU へ送る（プレビュー中の法線再計算を画面へ反映する）。</summary>
        public Action<MeshContext>     OnSyncMeshNormals;
        /// <summary>MeshContext.IsVisible を書き換えた直後に呼ぶ（頂点と辺の描画フラグの書き戻し）。</summary>
        public Action                  OnMeshVisibilityChanged;
        /// <summary>プレビューを始める前に対象の担当者判定とロック取得を行う（H-2）。null なら常に許可。</summary>
        public Func<IList<int>, bool>  TryLockForPreview;
        /// <summary>プレビューが終わったときに呼ぶ（ロックを外す）。</summary>
        public Action                  UnlockAfterPreview;

        private readonly BlendPreviewState _preview = new BlendPreviewState();
        private bool _locked;

        [PLToolState(Description = "プレビュー中か")]
        public bool IsPreviewing => _preview.IsActive;

        [PLToolState(Description = "直近のプレビューのソースごとの対応率の文（preview に渡したソースの並び）")]
        public string[] StatsLines { get; private set; } = Array.Empty<string>();

        [PLToolState(Description = "直近のプレビューのソースごとの警告の有無（未対応あり・対象なし。StatsLines と同じ並び）")]
        public bool[] StatsWarn { get; private set; } = Array.Empty<bool>();

        [PLToolState(Description = "直近の inspectMatchMode の注意書き（空なら注意なし）")]
        public string MatchModeHint { get; private set; } = "";

        /// <summary>
        /// 今の設定でプレビューを始める（未開始なら担当者判定とロックを取って開始）か、適用し直す。
        /// srcModels・srcMasters・weights は有効なソースだけを同じ並びで渡す。hideSources が true なら、
        /// カレントモデル内のソース（宛先を除く）をプレビュー中だけ隠す。
        /// </summary>
        [PLToolAction(Description = "プレビューを始める（未開始なら）か、今の設定で適用し直す")]
        public void Preview(int dest, int[] srcModels, int[] srcMasters, float[] weights,
            bool selectedVerticesOnly, BlendMatchMode matchMode, bool recalculateNormals, bool hideSources)
        {
            var model = GetModel?.Invoke();
            if (model == null || dest < 0) return;

            var sources = new List<BlendSourceEntry>();
            int n = Math.Min(srcModels?.Length ?? 0, Math.Min(srcMasters?.Length ?? 0, weights?.Length ?? 0));
            for (int i = 0; i < n; i++)
            {
                var ctx = GetModelContext?.Invoke(srcModels[i])?.GetMeshContext(srcMasters[i]);
                if (ctx?.MeshObject == null || weights[i] <= 0f) continue;
                sources.Add(new BlendSourceEntry(ctx, weights[i]));
            }

            var hide = new List<int>();
            if (hideSources)
            {
                int cur = GetModelIndex?.Invoke() ?? 0;
                for (int i = 0; i < n; i++)
                    if (srcModels[i] == cur && srcMasters[i] != dest && weights[i] > 0f) hide.Add(srcMasters[i]);
            }

            if (sources.Count == 0)
            {
                // 有効なソースが無くなった場合。隠したメッシュを戻す。
                if (_preview.IsActive && _preview.UpdateHiddenSources(model, hide)) OnMeshVisibilityChanged?.Invoke();
                return;
            }

            if (!_preview.IsActive)
            {
                // プレビューは宛先の頂点と、隠すソースの表示を書き換えるので、
                // 始める前に担当者判定とロック取得（操作経路統一計画.md H-2）。
                if (!_locked && TryLockForPreview != null)
                {
                    var targets = new List<int> { dest };
                    foreach (var h in hide) if (!targets.Contains(h)) targets.Add(h);
                    if (!TryLockForPreview(targets)) return;
                    _locked = true;
                }
                _preview.Start(model, dest, hide);
                if (_preview.IsActive) OnMeshVisibilityChanged?.Invoke();
                else if (_locked) { _locked = false; UnlockAfterPreview?.Invoke(); return; }
            }

            // ソースの差し替え・追加・削除へ追随する（隠す対象を取り直す）。
            if (_preview.UpdateHiddenSources(model, hide)) OnMeshVisibilityChanged?.Invoke();

            var stats = _preview.Apply(model, sources, selectedVerticesOnly, matchMode, recalculateNormals,
                OnSyncMeshNormals, BuildToolContext?.Invoke());
            BuildStats(stats);
        }

        [PLToolAction(Description = "プレビューを終える（宛先の頂点と隠したソースを元へ戻す）")]
        public void EndPreview()
        {
            if (_locked) { _locked = false; UnlockAfterPreview?.Invoke(); }
            bool wasActive = _preview.IsActive;
            _preview.End(GetModel?.Invoke(), BuildToolContext?.Invoke());
            if (wasActive) OnMeshVisibilityChanged?.Invoke();
            StatsLines = Array.Empty<string>();
            StatsWarn  = Array.Empty<bool>();
        }

        /// <summary>
        /// 選んだ対応方式が実際に使える状態かを調べ、MatchModeHint に入れる。
        /// 頂点ID照合は未設定IDや重複IDがあると対応が取れない頂点が出る。展開インデックス経由は、
        /// 両者の IsTriangulated が同じなら頂点インデックス直結と同じ動きになる。
        /// </summary>
        [PLToolAction(Description = "対応方式の注意書きを作る（MatchModeHint に入れる）")]
        public void InspectMatchMode(int dest, int[] srcModels, int[] srcMasters, BlendMatchMode matchMode)
        {
            MatchModeHint = "";
            var destCtx = dest >= 0 ? GetModel?.Invoke()?.GetMeshContext(dest) : null;
            var destMo  = destCtx?.MeshObject;
            if (destMo == null) return;

            var lines = new List<string>();
            int n = Math.Min(srcModels?.Length ?? 0, srcMasters?.Length ?? 0);
            if (matchMode == BlendMatchMode.VertexId)
            {
                var (dUnset, dDup) = BlendVertexResolver.InspectVertexIds(destMo);
                if (dUnset > 0 || dDup > 0)
                    lines.Add($"宛先「{destCtx.Name}」の頂点ID: 未設定 {dUnset} / 重複 {dDup}");
                for (int i = 0; i < n; i++)
                {
                    var ctx = GetModelContext?.Invoke(srcModels[i])?.GetMeshContext(srcMasters[i]);
                    if (ctx?.MeshObject == null) continue;
                    var (u, d) = BlendVertexResolver.InspectVertexIds(ctx.MeshObject);
                    if (u > 0 || d > 0) lines.Add($"ソース「{ctx.Name}」の頂点ID: 未設定 {u} / 重複 {d}");
                }
                if (lines.Count > 0) lines.Add("未設定IDの頂点は対応対象外、重複IDは先勝ちになります。");
            }
            else if (matchMode == BlendMatchMode.Expanded)
            {
                for (int i = 0; i < n; i++)
                {
                    var ctx = GetModelContext?.Invoke(srcModels[i])?.GetMeshContext(srcMasters[i]);
                    if (ctx?.MeshObject == null) continue;
                    if (ctx.MeshObject.IsTriangulated == destMo.IsTriangulated)
                        lines.Add($"「{ctx.Name}」と宛先は三角形化状態が同じため、頂点インデックス直結と同じ動きになります。");
                }
            }
            MatchModeHint = string.Join("\n", lines);
        }

        private void BuildStats(BlendMatchStats[] stats)
        {
            if (stats == null) { StatsLines = Array.Empty<string>(); StatsWarn = Array.Empty<bool>(); return; }
            var lines = new string[stats.Length];
            var warn  = new bool[stats.Length];
            for (int k = 0; k < stats.Length; k++)
            {
                var st = stats[k];
                if (st.TargetVertexCount == 0)
                {
                    lines[k] = "対象頂点がありません（孤立頂点のみ、または選択頂点が空）";
                    warn[k]  = true;
                }
                else
                {
                    lines[k] = $"対応 {st.MatchedVertexCount} / {st.TargetVertexCount}"
                             + $"（{st.MatchRatio * 100f:F1}%）"
                             + (st.UnmatchedVertexCount > 0
                                 ? $"　未対応 {st.UnmatchedVertexCount} 頂点は元位置のまま"
                                 : "");
                    warn[k] = st.UnmatchedVertexCount > 0;
                }
            }
            StatsLines = lines;
            StatsWarn  = warn;
        }
    }
}
