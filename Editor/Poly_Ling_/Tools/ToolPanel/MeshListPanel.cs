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
    public class MeshListPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "MeshContextList";
        public override string Title => "Mesh List";
        public override IToolSettings Settings => null;

        /// <summary>
        /// ローカライズされたタイトルを取得
        /// </summary>
        public override string GetLocalizedTitle() => L.Get("Window_MeshContextList");

        // ================================================================
        // ウィンドウ固有ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            // ヘッダー
            ["Meshes"] = new() { ["en"] = "Meshes", ["ja"] = "メッシュ", ["hi"] = "ずけい" },
            ["Info"] = new() { ["en"] = "Info", ["ja"] = "情報", ["hi"] = "じょうほう" },
            
            // メッセージ
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません", ["hi"] = "もでるがないよ" },
            ["NoMeshSelected"] = new() { ["en"] = "No mesh selected", ["ja"] = "メッシュ未選択", ["hi"] = "えらんでないよ" },
            
            // 詳細情報
            ["SelectedMesh"] = new() { ["en"] = "Selected Mesh", ["ja"] = "選択中のメッシュ", ["hi"] = "えらんでるずけい" },
            ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点", ["hi"] = "てん" },
            ["Faces"] = new() { ["en"] = "Faces", ["ja"] = "面", ["hi"] = "めん" },
            ["Triangles"] = new() { ["en"] = "Triangles", ["ja"] = "三角形", ["hi"] = "さんかく" },
            ["Quads"] = new() { ["en"] = "Quads", ["ja"] = "四角形", ["hi"] = "しかく" },
            ["NGons"] = new() { ["en"] = "N-Gons", ["ja"] = "多角形", ["hi"] = "たかく" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "マテリアル", ["hi"] = "ざいりょう" },
            
            // ボタン
            ["MoveToTop"] = new() { ["en"] = "Move to Top", ["ja"] = "先頭へ", ["hi"] = "いちばんうえへ" },
            ["MoveToBottom"] = new() { ["en"] = "Move to Bottom", ["ja"] = "末尾へ", ["hi"] = "いちばんしたへ" },
            ["Duplicate"] = new() { ["en"] = "Duplicate", ["ja"] = "複製", ["hi"] = "コピー" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除", ["hi"] = "けす" },
            
            // ダイアログ
            ["DeleteMeshTitle"] = new() { ["en"] = "Delete Mesh", ["ja"] = "メッシュを削除", ["hi"] = "けす" },
            ["DeleteMeshMessage"] = new() { ["en"] = "Delete '{0}'?", ["ja"] = "'{0}' を削除しますか？", ["hi"] = "'{0}' をけす？" },
            ["Cancel"] = new() { ["en"] = "Cancel", ["ja"] = "キャンセル", ["hi"] = "やめる" },
            
            // メニュー
            ["EmptyMesh"] = new() { ["en"] = "Empty Mesh", ["ja"] = "空のメッシュ", ["hi"] = "からっぽ" },
            ["UseMainWindow"] = new() { ["en"] = "(Use mesh creators in main toolPanel)", ["ja"] = "(メインウィンドウで作成)", ["hi"] = "(メインでつくってね)" },
        };

        /// <summary>ウィンドウ内ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // UIの状態
        // ================================================================

        private Vector2 _scrollPos;
        private bool _showInfo = true;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var toolPanel = GetWindow<MeshListPanel>();
            toolPanel.titleContent = new GUIContent(L.Get("Window_MeshContextList"));
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
                DrawMeshContext(i, model);
            }

            EditorGUILayout.EndScrollView();

            // 選択中メッシュの詳細情報
            EditorGUILayout.Space();
            DrawSelectedMeshInfo();
        }

        // ================================================================
        // ヘッダー描画
        // ================================================================

        private void DrawHeader(ModelContext model)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField($"{T("Meshes")}: {model.MeshContextCount}", EditorStyles.boldLabel, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            // 情報表示トグル
            _showInfo = GUILayout.Toggle(_showInfo, T("Info"), EditorStyles.toolbarButton, GUILayout.Width(50));

            // 新規メッシュ追加
            if (GUILayout.Button("+", EditorStyles.toolbarButton, GUILayout.Width(24)))
            {
                ShowAddMeshMenu();
            }

            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // メッシュコンテキスト描画
        // ================================================================

        private void DrawMeshContext(int index, ModelContext model)
        {
            var meshContext = model.GetMeshContext(index);
            if (meshContext == null) return;

            bool isSelected = (model.SelectedMeshContextIndex == index);
            bool isFirst = (index == 0);
            bool isLast = (index == model.MeshContextCount - 1);

            // 選択中は背景色を変える
            if (isSelected)
            {
                var bgRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            }
            else
            {
                EditorGUILayout.BeginVertical();
            }

            EditorGUILayout.BeginHorizontal();

            // 選択マーカー
            string marker = isSelected ? "▶" : "  ";
            if (GUILayout.Button(marker, EditorStyles.label, GUILayout.Width(16)))
            {
                SelectMesh(index);
            }

            // 名前（クリックで選択）
            if (GUILayout.Button(meshContext.Name, EditorStyles.label, GUILayout.ExpandWidth(true)))
            {
                SelectMesh(index);
            }

            // 情報表示
            if (_showInfo && meshContext.MeshObject != null)
            {
                EditorGUILayout.LabelField($"V:{meshContext.MeshObject.VertexCount}", EditorStyles.miniLabel, GUILayout.Width(50));
            }

            // ↑ 上に移動
            using (new EditorGUI.DisabledScope(isFirst))
            {
                if (GUILayout.Button("↑", GUILayout.Width(22)))
                {
                    ReorderMesh(index, index - 1);
                }
            }

            // ↓ 下に移動
            using (new EditorGUI.DisabledScope(isLast))
            {
                if (GUILayout.Button("↓", GUILayout.Width(22)))
                {
                    ReorderMesh(index, index + 1);
                }
            }

            // D 複製
            if (GUILayout.Button("D", GUILayout.Width(22)))
            {
                DuplicateMesh(index);
            }

            // X 削除
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                if (EditorUtility.DisplayDialog(T("DeleteMeshTitle"),
                    T("DeleteMeshMessage", meshContext.Name), T("Delete"), T("Cancel")))
                {
                    RemoveMesh(index);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ================================================================
        // 選択中メッシュの詳細情報
        // ================================================================

        private void DrawSelectedMeshInfo()
        {
            var meshContext = CurrentMeshContent;
            if (meshContext == null)
            {
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(T("SelectedMesh"), EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                // 名前（編集可能）
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField(T("Name"), meshContext.Name);
                if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName))
                {
                    meshContext.Name = newName;
                    Repaint();
                }

                // 統計情報
                if (meshContext.MeshObject != null)
                {
                    EditorGUILayout.LabelField(T("Vertices"), meshContext.MeshObject.VertexCount.ToString());
                    EditorGUILayout.LabelField(T("Faces"), meshContext.MeshObject.FaceCount.ToString());

                    // 面タイプ内訳
                    int triCount = 0, quadCount = 0, nGonCount = 0;
                    foreach (var face in meshContext.MeshObject.Faces)
                    {
                        if (face.IsTriangle) triCount++;
                        else if (face.IsQuad) quadCount++;
                        else nGonCount++;
                    }
                    EditorGUILayout.LabelField($"  {T("Triangles")}", triCount.ToString());
                    EditorGUILayout.LabelField($"  {T("Quads")}", quadCount.ToString());
                    if (nGonCount > 0)
                        EditorGUILayout.LabelField($"  {T("NGons")}", nGonCount.ToString());

                    EditorGUILayout.LabelField(T("Materials"), (meshContext.Materials?.Count ?? 0).ToString());
                }

                EditorGUILayout.Space();

                // 操作ボタン
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(T("MoveToTop")))
                {
                    int current = _context.SelectedMeshIndex;
                    if (current > 0)
                    {
                        ReorderMesh(current, 0);
                    }
                }

                if (GUILayout.Button(T("MoveToBottom")))
                {
                    int current = _context.SelectedMeshIndex;
                    int last = Model.MeshContextCount - 1;
                    if (current < last)
                    {
                        ReorderMesh(current, last);
                    }
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(T("Duplicate")))
                {
                    DuplicateMesh(_context.SelectedMeshIndex);
                }

                if (GUILayout.Button(T("Delete")))
                {
                    if (EditorUtility.DisplayDialog(T("DeleteMeshTitle"),
                        T("DeleteMeshMessage", meshContext.Name), T("Delete"), T("Cancel")))
                    {
                        RemoveMesh(_context.SelectedMeshIndex);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // ================================================================
        // メッシュ追加メニュー
        // ================================================================

        private void ShowAddMeshMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent(T("EmptyMesh")), false, () =>
            {
                var newMeshContext = new MeshContext
                {
                    Name = "New Mesh",
                    MeshObject = new MeshObject("New Mesh"),
                    UnityMesh = new Mesh(),
                    OriginalPositions = new Vector3[0],
                    Materials = new System.Collections.Generic.List<Material> { null }
                };
                AddMesh(newMeshContext);
            });

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent(T("UseMainWindow")));

            menu.ShowAsContext();
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