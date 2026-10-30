// PLModifierAttribute.cs
// 生成コマンドがオブジェクトグループ（モディファイア）としてどう残るかの宣言。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   「グループに記録されるか」は型からは導けない。記録しているのは受け口
//   （PlayerCommandDispatcher の各区画）のコードで、型やパラメータには何も現れない。
//   作り直し整備計画（PolyLing_作り直し整備計画.md）の棚卸しで全件を機械的に
//   出すために、記録のされ方と逆方向をコマンド型に宣言しておく。
//   型から導けるもの（読む入力・書き戻せるか）はここに書かない（ModifierInventory が導く）。
//
// 【記録のされ方の根拠（2026-10-29 時点）】
//   OnKeepAsGroup … PlayerCommandDispatcher.MeshAttributes.cs（図形生成）、.EditTools.cs（帯面）、
//                    .VertexBillboard.cs（頂点藤壺）、.Blend.cs（ブレンド）、.SpringBone.cs（揺れ鎖）
//   Always        … PlayerCommandDispatcher.Subdivision.cs（新しく子を作るとき）
//   ViaSteps      … PlayerCommandDispatcher.EdgePipe.cs（帯面＋パイプ）、.LatticeTower.cs（デッキ追加）
//   Assembly      … PlayerCommandDispatcher.MagicCircle.cs ExecuteEmptyParentAssembly（魔法陣・鉄塔）
//   属性が無いもの … 記録しない（None）
//   受け口の記録のしかたを変えたら、ここの宣言も直すこと。

using System;

namespace Poly_Ling.Data
{
    /// <summary>生成コマンドがオブジェクトグループへどう記録されるか。</summary>
    public enum PLGroupCapture
    {
        /// <summary>記録しない。</summary>
        None = 0,

        /// <summary>「グループとして残す」（KeepAsGroup）が立っているときだけ、自身を項目 1 つとして記録する。</summary>
        OnKeepAsGroup = 1,

        /// <summary>常に自身を項目 1 つとして記録する。</summary>
        Always = 2,

        /// <summary>自身は記録せず、内部で流した別のコマンドを項目として記録する。</summary>
        ViaSteps = 3,

        /// <summary>組立。空の親を作り、部品（図形生成コマンド）を項目として並べたグループを作る。自身は記録しない。</summary>
        Assembly = 4,
    }

    /// <summary>逆方向の方法の名前（PLModifierAttribute.Inverse の値）。</summary>
    public static class ModifierInverseMethod
    {
        /// <summary>梯子の段ごとの枠（パイプの輪・藤壺の 1 個）→ 梯子の段の左右・先端。</summary>
        public const string BeltFrames    = "beltFrames";
        /// <summary>頂点へ藤壺の 1 個 → 選択辞書の頂点の位置。</summary>
        public const string VertexFrames  = "vertexFrames";
        /// <summary>ネジ配置の 1 個 → PCD の半径・開始角（長方形は幅・高さ）・倍率。</summary>
        public const string BoltPattern   = "boltPattern";
        /// <summary>円周配置の 1 個 → 半径・開始角・倍率。</summary>
        public const string RadialPlace   = "radialPlace";
        /// <summary>鉄塔のデッキ → 高さ・Y 軸まわりの回転・倍率。</summary>
        public const string TowerDeck     = "towerDeck";
        /// <summary>まつげの 1 本（補間した段）→ 元の梯子の段（最小二乗で当てはめ）と回転の始点・終点（段 7）。</summary>
        public const string LushFrames    = "lushFrames";
        /// <summary>写した頂点（パイプの梯子化の始点・レール・終点）→ 元のパイプの頂点（段 14）。</summary>
        public const string CopiedVertices = "copiedVertices";
        /// <summary>帯の左右の中点 → 元の辺の頂点（段 14）。</summary>
        public const string RibbonCenters  = "ribbonCenters";
        /// <summary>貼り付けた頂点 → 入力の頂点（平面内の位置と、参照面からの高さ。段 14）。</summary>
        public const string SurfacePaste   = "surfacePaste";
    }

    /// <summary>
    /// 生成コマンドのモディファイアとしての宣言。派生クラスへ引き継ぐ
    /// （CreatePrimitiveMeshCommand に付けたものが全図形へ効く）。派生側に付ければ上書きになる。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class PLModifierAttribute : Attribute
    {
        /// <summary>グループへの記録のされ方。</summary>
        public PLGroupCapture Capture { get; set; } = PLGroupCapture.None;

        /// <summary>
        /// 逆方向（出力の編集を入力へ戻す）の方法の名前。空＝逆方向を持たない。
        /// 値は ModifierInverseMethod の定数（ModifierInverse が方法ごとに戻し方を持つ）。
        /// 逆方向を持つ項目は、出力の位置の編集を許し、確定で入力を書き直す（作り直し整備計画 段 6）。
        /// </summary>
        public string Inverse { get; set; } = "";

        /// <summary>逆方向で保つもの（人に見せる説明）。</summary>
        public string InverseKeeps { get; set; } = "";

        /// <summary>逆方向で失うもの（人に見せる説明）。確定の前に知らせる。</summary>
        public string InverseLoses { get; set; } = "";

        /// <summary>このコマンド自身がどのコマンドの逆方向か（action 名）。空＝逆方向ではない。</summary>
        public string InverseOf { get; set; } = "";

        /// <summary>
        /// 入力の頂点ドラッグ中にも出力を追随させるか。既定 false。
        /// true の項目は評価器（ModifierEvaluator）がドラッグ中の位置の同期
        /// （PlayerViewportManager.SyncMeshPositionsAndTransform）で処理器を呼ぶ。
        /// false の項目はドラッグ中は要評価の印だけ立てる（重い生成をドラッグの毎フレームで流さない）。
        /// 例: サブディビジョンはステンシルを掛けるだけで軽いので true。
        /// </summary>
        public bool FollowsDrag { get; set; } = false;
    }
}
