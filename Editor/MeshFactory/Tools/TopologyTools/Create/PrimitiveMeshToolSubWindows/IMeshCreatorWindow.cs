// Assets/Editor/MeshFactory/Tools/Creators/IMeshCreatorWindow.cs
// メッシュ生成ウィンドウのインターフェース

using System;
using MeshFactory.Data;

namespace MeshFactory.Tools.Creators
{
    /// <summary>
    /// メッシュ生成ウィンドウのインターフェース
    /// </summary>
    public interface IMeshCreatorWindow
    {
        /// <summary>ウィンドウ識別名</summary>
        string Name { get; }

        /// <summary>表示タイトル</summary>
        string Title { get; }

        /// <summary>ボタン表示名（ローカライズキー）</summary>
        string ButtonLabel { get; }

        /// <summary>
        /// ウィンドウを開く
        /// </summary>
        /// <param name="onMeshDataCreated">メッシュ生成完了時のコールバック</param>
        void Open(Action<MeshData, string> onMeshDataCreated);
    }

    /// <summary>
    /// メッシュ生成ウィンドウの登録情報
    /// </summary>
    public class MeshCreatorEntry
    {
        /// <summary>ボタン表示名</summary>
        public string ButtonLabel { get; set; }

        /// <summary>ウィンドウを開くアクション</summary>
        public Action<Action<MeshData, string>> OpenAction { get; set; }

        /// <summary>カテゴリ（オプション）</summary>
        public string Category { get; set; }

        /// <summary>表示順序</summary>
        public int Order { get; set; }

        public MeshCreatorEntry(string label, Action<Action<MeshData, string>> openAction, string category = null, int order = 0)
        {
            ButtonLabel = label;
            OpenAction = openAction;
            Category = category;
            Order = order;
        }
    }
}
