using Poly_Ling.Data;
// ChangeKind.cs
// パネル通知時の変更種別
// パネルはこれに応じて差分更新を行う

namespace Poly_Ling.Data
{
    public enum ChangeKind
    {
        /// <summary>選択変更のみ。ツリー再構築不要。</summary>
        Selection,

        /// <summary>属性変更（可視性/ロック/名前/ミラー等）。再バインドのみ。</summary>
        Attributes,

        /// <summary>リスト構造変更（追加/削除/並べ替え）。フルリビルド必要。</summary>
        ListStructure,

        /// <summary>モデル切り替え。フルリビルド必要。</summary>
        ModelSwitch
    }
}
