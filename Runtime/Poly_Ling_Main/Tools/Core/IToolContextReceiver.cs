// Assets/Editor/Poly_Ling_Main/Tools/Core/IToolContextReceiver.cs
// ToolContextを受け取れるEditorWindowの最小インターフェース
// メインパネル再起動時の再接続に使用

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ToolContextを受け取れるEditorWindowが実装するインターフェース。
    /// メインパネル再起動時に開いているパネルへToolContextを再配布するために使用。
    /// </summary>
    public interface IToolContextReceiver
    {
        void SetContext(ToolContext ctx);
    }
}
