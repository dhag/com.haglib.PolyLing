// Assets/Editor/Poly_Ling/Tools/Panels/MeshListWindow.cs
// メッシュリスト管理ウィンドウ（統合版）
// 選択・削除・複製・順序変更・名前変更・情報表示
// ローカライズ対応版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

////using MeshContext = MeshContext;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// メッシュリスト管理ウィンドウ
    /// </summary>
    public class NullPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "NullPanel";
        public override string Title => "NullPanel";
        public override IToolSettings Settings => null;

        /// <summary>
        /// ローカライズされたタイトルを取得
        /// </summary>
        public override string GetLocalizedTitle() => L.Get("Window_NullPanel");

        // ================================================================
        // ウィンドウ固有ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
        };

        /// <summary>ウィンドウ内ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // UIの状態
        // ================================================================

        private Vector2 _scrollPos;
        //private bool _showInfo = true;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var toolPanel = GetWindow<MeshListPanel>();
            toolPanel.titleContent = new GUIContent(L.Get("Window_NullPanel"));
            toolPanel.minSize = new Vector2(300, 250);
            toolPanel.SetContext(ctx);
            toolPanel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            // コンテキストチェック
            if (!DrawNoContextWarning())
                return;

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            // ヘッダー
            DrawHeader(model);

            // メッシュリスト
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                //DrawMeshContext(i, model);
            }

            EditorGUILayout.EndScrollView();

        }

        // ================================================================
        // ヘッダー描画
        // ================================================================

        private void DrawHeader(ModelContext model)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);


            EditorGUILayout.EndHorizontal();
        }



        // ================================================================
        // コンテキスト更新時
        // ================================================================

        protected override void OnContextSet()
        {
            _scrollPos = Vector2.zero;
        }
    }
}