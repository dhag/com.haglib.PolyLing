// HoleSeed.cs
// 穴（エッジ＝1面だけが使う辺のグループ）の「種」を選択から拾うときの共通型。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【なぜ共通型にするか】
//   選択から種を拾う処理（PolyLingPlayerViewerCore.PickHoleSeeds）は、
//   ブリッジ（図形生成パネル）と穴頂点数合わせ（編集パネル）の両方が使う。
//   拾い方を 1 箇所に保つため、受け渡しの型もパネルから独立させる。
//
// 【マーカー表示の共通化】
//   種マーカーをビューポートへ出す条件と対象は IHoleSeedSource で表す。
//   Viewer は「今どのパネルが種を持っているか」だけを見て、同じ描画経路を通す。

namespace Poly_Ling.Player
{
    /// <summary>選択から拾った種 1 件。</summary>
    public struct HoleSeedPick
    {
        /// <summary>拾えたか。false のとき Message に理由が入る。</summary>
        public bool   Ok;
        /// <summary>拾えなかった理由。</summary>
        public string Message;
        /// <summary>所属する描画オブジェクトのインデックス。</summary>
        public int    MeshIndex;
        /// <summary>種頂点。</summary>
        public int    Vertex;
        /// <summary>辺を拾ったときの進行方向側の頂点。頂点を拾ったときは -1。</summary>
        public int    DirectionHint;
    }

    /// <summary>
    /// 種マーカーの描画対象を提供するパネル。
    /// A / B は用途によって呼び名が変わる（ブリッジ＝穴A・穴B、
    /// 穴頂点数合わせ＝基準穴・対象穴）が、描き方は同じ。
    /// </summary>
    public interface IHoleSeedSource
    {
        /// <summary>いま種マーカーを出す状態か（パネルが表示中で、その機能を選んでいる）。</summary>
        bool HoleSeedOverlayActive { get; }

        /// <summary>A 側（ブリッジ＝穴A / 穴頂点数合わせ＝基準穴）。未取り込みは -1。</summary>
        int HoleSeedMeshIndexA { get; }
        int HoleSeedVertexA    { get; }
        int HoleSeedDirHintA   { get; }

        /// <summary>B 側（ブリッジ＝穴B / 穴頂点数合わせ＝対象穴）。未取り込みは -1。</summary>
        int HoleSeedMeshIndexB { get; }
        int HoleSeedVertexB    { get; }
        int HoleSeedDirHintB   { get; }
    }
}
