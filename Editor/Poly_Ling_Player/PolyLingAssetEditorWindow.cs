// PolyLingAssetEditorWindow.cs
// アセットベースの保存など、エディタ拡張専用操作を行うウィンドウ。
// データは PolyLingEditorWindow.Instance.Core 経由でメインパネルと共用。
//
// Editor/Poly_Ling_Player/ に配置

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Player;

namespace Poly_Ling.Editor.Player
{
    public class PolyLingAssetEditorWindow : EditorWindow
    {
        private PolyLingAssetEditorLayout _layout;

        public static void Open()
        {
            GetWindow<PolyLingAssetEditorWindow>("PolyLing Asset Editor");
        }

        private void CreateGUI()
        {
            _layout = new PolyLingAssetEditorLayout();
            _layout.Build(rootVisualElement);

            // ダミーボタンのログ確認用（後続実装時に置き換え）
            _layout.LeftDummyBtnA.clicked  += () => Debug.Log("[PolyLingAssetEditor] Left Dummy A");
            _layout.LeftDummyBtnB.clicked  += () => Debug.Log("[PolyLingAssetEditor] Left Dummy B");
            _layout.RightDummyBtnA.clicked += () => Debug.Log("[PolyLingAssetEditor] Right Dummy A");
            _layout.RightDummyBtnB.clicked += () => Debug.Log("[PolyLingAssetEditor] Right Dummy B");
        }

        // ================================================================
        // メインパネルとのデータ共用
        // ================================================================

        /// <summary>
        /// メインパネル（PolyLingEditorWindow）が開いている場合に
        /// アクティブな ProjectContext を返す。開いていない場合は null。
        /// </summary>
        private Poly_Ling.Context.ProjectContext GetSharedProject()
        {
            return PolyLingEditorWindow.Instance?.Core?.GetActiveProject();
        }
    }
}
