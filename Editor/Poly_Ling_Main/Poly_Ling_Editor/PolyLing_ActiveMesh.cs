// Assets/Editor/PolyLing_ActiveMesh.cs
// アクティブメッシュ管理（パーシャルクラス）
//
// 【ルール】
// _selectedIndex への直接代入は禁止。
// 必ず SetSelectedIndex() を経由すること。
// grepで「_selectedIndex =」が本ファイル以外にヒットしたら違反。

public partial class PolyLing
{
    /// <summary>
    /// アクティブメッシュインデックスを設定する唯一の入口。
    /// _selectedIndex への直接代入はこのメソッドのみ許可。
    /// 変更後、描画システムにNormalモード1フレームを要求する。
    /// </summary>
    /// <param name="index">新しいインデックス（-1で未選択）</param>
    private void SetSelectedIndex(int index)
    {
        _selectedIndex = index;
        _unifiedAdapter?.RequestNormal();
    }
}
