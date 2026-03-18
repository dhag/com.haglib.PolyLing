// IEditorToolUI.cs
// Editor専用ツールUI描画インターフェース
// IEditToolからDrawSettingsUI()を分離したもの
// Runtime環境では存在しない

#if UNITY_EDITOR
namespace Poly_Ling.Tools
{
    /// <summary>
    /// ツール固有のEditorGUI設定UIを描画する。
    /// EditorWindowからのみ呼び出される。
    /// IEditToolと同じクラスがpartialで実装する。
    /// </summary>
    public interface IEditorToolUI
    {
        void DrawSettingsUI();
    }
}
#endif
