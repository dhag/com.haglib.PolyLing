// PanelCommand.BridgeByName.cs
// 名前で指した 2 つの描画オブジェクトの穴どうしを橋渡しする操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【何のためにあるか】
//   createHoleBridge と matchHoleRingCount は、穴を「種頂点の番号」で指す。
//   頂点番号は生成順に依存するので手順の記録（手本）に焼けない
//   （PlayerRobotBuildTestSubPanel.Stages.cs:38-41）。
//   照会コマンドで引き直す形にすると、関節 1 本あたり
//   「基準側の種を引く」「対象側の種を引く」「点数を合わせる」「橋を張る」の
//   4 段になり、しかも種の値を人が写す必要がある（@prev は 1 個しか覚えない）。
//   ここが名前から種までを一息で決め、関節 1 本を 1 段にする。
//
// 【選び方は既存の Ops と同じ】
//   種の選び方はパネルと同じ BridgeAutoPairOps を通す。
//   ・橋を張る種 … SelectPair（頂点数が同数の穴ペアだけを候補にする）
//   ・点数を合わせる種 … 重心距離が最小の穴ペアから最短の頂点ペア
//     （SelectPair は同数しか候補にしないので、揃える前には使えない）
//   別の場所へ書き写すと、パネルと結果が割れる。
//
// 【限界】
//   点数合わせの種は重心距離だけで穴の組を決める。穴が近接して並ぶ形状では
//   意図しない組を選ぶ。合ったかどうかは戻り値の ringA / ringB で確かめること。

namespace Poly_Ling.Data
{
    /// <summary>名前で指した 2 つのオブジェクトの穴を橋渡しする。</summary>
    [PLCommand(Description = "名前で指した 2 つの描画オブジェクトの、いちばん近い穴どうしを橋で張る。種頂点は実データから選び直すので、頂点番号を手順に焼かなくてよい。matchCounts を立てると、張る前に穴の頂点数を揃える。")]
    [PLResult("ok",          PLResultKind.Flag,    Description = "橋を張れたか")]
    [PLResult("message",     PLResultKind.Text,    Description = "失敗した理由。成功時は空")]
    [PLResult("baseIndex",   PLResultKind.Integer, Description = "基準側の masterIndex")]
    [PLResult("targetIndex", PLResultKind.Integer, Description = "対象側の masterIndex")]
    [PLResult("seedA",       PLResultKind.Integer, Description = "基準側の種頂点。選べなかったときは -1")]
    [PLResult("seedB",       PLResultKind.Integer, Description = "対象側の種頂点。選べなかったときは -1")]
    [PLResult("ringA",       PLResultKind.Integer, Description = "張る直前の基準側の穴の頂点数")]
    [PLResult("ringB",       PLResultKind.Integer, Description = "張る直前の対象側の穴の頂点数。ringA と同じでないと張れない")]
    public class BridgeHolesByNameCommand : PanelCommand
    {
        [PLParam(Description = "基準側の描画オブジェクトの名前", Required = true)]
        public string BaseName { get; }

        [PLParam(Description = "対象側の描画オブジェクトの名前", Required = true)]
        public string TargetName { get; }

        [PLParam(Description = "張る前に穴の頂点数を揃える。六角柱（6 点）を胴側の穴に合わせるときに要る")]
        public bool MatchCounts { get; }

        [PLParam(Description = "橋の中間分割数。0 だと面 1 枚でつながり、曲げたときに折れる", Min = 0)]
        public int Subdivisions { get; }

        [PLParam(Description = "作る橋の名前。空なら Bridge_基準名_対象名")]
        public string BridgeName { get; }

        public BridgeHolesByNameCommand(
            int modelIndex, string baseName, string targetName,
            bool matchCounts = true, int subdivisions = 3, string bridgeName = "")
            : base(modelIndex)
        {
            BaseName     = baseName ?? "";
            TargetName   = targetName ?? "";
            MatchCounts  = matchCounts;
            Subdivisions = subdivisions;
            BridgeName   = bridgeName ?? "";
        }
    }
}
