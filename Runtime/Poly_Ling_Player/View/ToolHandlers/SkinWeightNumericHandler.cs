// SkinWeightNumericHandler.cs
// ボーンウェイト数値入力パネルの読み取り（選択頂点の共通ウェイトの取り込み、合計の検査、
// 対象オブジェクトと選択頂点数）を受け持つ（操作経路統一計画.md E）。
//
// PlayerSkinWeightNumericSubPanel が ModelContext を直接読んで SkinWeightOperations を
// 呼んでいたものを移した。データは変えない（書き込みは従来どおり
// SetSkinWeightNumericCommand・NormalizeAllSkinWeightsCommand）。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    [PLTool("skinWeightNumeric", Description = "ボーンウェイト数値入力の読み取り（共通ウェイトの取り込み・合計の検査・対象）")]
    public sealed class SkinWeightNumericHandler
    {
        public Func<ModelContext> GetModel;

        [PLToolState(Description = "直近の gather の結果のボーン（スロット順。-1 は空き）")]
        public int[]   GatheredBones   { get; private set; } = Array.Empty<int>();
        [PLToolState(Description = "直近の gather の結果のウェイト（スロット順）")]
        public float[] GatheredWeights { get; private set; } = Array.Empty<float>();
        [PLToolState(Description = "直近の gather の失敗理由（成功なら空）")]
        public string  GatherError     { get; private set; } = "";

        [PLToolState(Description = "直近の checkSums: 検査した頂点数")]
        public int     SumChecked  { get; private set; }
        [PLToolState(Description = "直近の checkSums: ウェイトの無い頂点数")]
        public int     SumNoWeight { get; private set; }
        [PLToolState(Description = "直近の checkSums: 合計が 1 でない頂点数")]
        public int     SumBroken   { get; private set; }
        [PLToolState(Description = "直近の checkSums: 合計が 1 でない頂点の合計の最小値")]
        public float   SumMin      { get; private set; }
        [PLToolState(Description = "直近の checkSums: 合計が 1 でない頂点の合計の最大値")]
        public float   SumMax      { get; private set; }
        [PLToolState(Description = "直近の checkSums: 合計が 1 でない頂点を持つオブジェクト名")]
        public string[] SumBrokenMeshNames { get; private set; } = Array.Empty<string>();

        [PLToolState(Description = "対象オブジェクト（適用先と同じ「選択中の描画オブジェクト全件」）の名前")]
        public string[] TargetNames
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return Array.Empty<string>();
                var list = new List<string>();
                foreach (var mc in SkinWeightOperations.CollectTargetMeshContexts(model))
                    list.Add(string.IsNullOrEmpty(mc?.Name) ? "?" : mc.Name);
                return list.ToArray();
            }
        }

        [PLToolState(Description = "対象オブジェクトごとの選択頂点数（TargetNames と同じ並び）")]
        public int[] TargetSelectedVertexCounts
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return Array.Empty<int>();
                var list = new List<int>();
                foreach (var mc in SkinWeightOperations.CollectTargetMeshContexts(model))
                    list.Add(mc?.SelectedVertices?.Count ?? 0);
                return list.ToArray();
            }
        }

        /// <summary>選択頂点の現在のボーンウェイトを取り込む。複数頂点では全頂点で同じボーン・同じウェイトの組だけ。</summary>
        [PLToolAction(Description = "選択頂点の共通ボーンウェイトを取り込む（gatheredBones・gatheredWeights・gatherError に入れる）")]
        public void Gather(float tolerance)
        {
            GatheredBones = Array.Empty<int>(); GatheredWeights = Array.Empty<float>(); GatherError = "";
            var model = GetModel?.Invoke();
            if (model == null) { GatherError = "モデルがありません。"; return; }
            string err = null;
            var common = SkinWeightOperations.GatherCommonBoneWeights(model, tolerance, m => err = m);
            if (common == null) { GatherError = err ?? "取り込めませんでした。"; return; }
            var b = new int[common.Length]; var w = new float[common.Length];
            for (int i = 0; i < common.Length; i++) { b[i] = common[i].bone; w[i] = common[i].weight; }
            GatheredBones = b; GatheredWeights = w;
        }

        [PLToolAction(Description = "対象オブジェクト全頂点のウェイト合計を検査する（sum* に入れる）")]
        public void CheckSums(float tolerance)
        {
            SumChecked = SumNoWeight = SumBroken = 0; SumMin = SumMax = 0f; SumBrokenMeshNames = Array.Empty<string>();
            var model = GetModel?.Invoke();
            if (model == null) return;
            var rep = SkinWeightOperations.CheckWeightSums(model, tolerance);
            SumChecked = rep.Checked; SumNoWeight = rep.NoWeight; SumBroken = rep.Broken;
            SumMin = rep.MinSum; SumMax = rep.MaxSum;
            SumBrokenMeshNames = rep.BrokenMeshNames?.ToArray() ?? Array.Empty<string>();
        }
    }
}
