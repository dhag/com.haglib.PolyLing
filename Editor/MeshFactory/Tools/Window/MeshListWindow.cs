// Assets/Editor/MeshFactory/Tools/Windows/MeshListWindow.cs
// メッシュリスト管理ウィンドウ（統合版）
// 選択・削除・複製・順序変更・名前変更・情報表示

using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;

using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Tools.Windows
{
    /// <summary>
    /// メッシュリスト管理ウィンドウ
    /// </summary>
    public class MeshListWindow : ToolWindowBase
    {
        // ================================================================
        // IToolWindow実装
        // ================================================================

        public override string Name => "MeshContextList";
        public override string Title => "UnityMesh List";
        public override IToolSettings Settings => null;

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
            var window = GetWindow<MeshListWindow>();
            window.titleContent = new GUIContent("UnityMesh List");
            window.minSize = new Vector2(300, 250);
            window.SetContext(ctx);
            window.Show();
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
                EditorGUILayout.HelpBox("Model not available", MessageType.Warning);
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

            EditorGUILayout.LabelField($"Meshes: {model.MeshContextCount}", EditorStyles.boldLabel, GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            // 情報表示トグル
            _showInfo = GUILayout.Toggle(_showInfo, "Info", EditorStyles.toolbarButton, GUILayout.Width(40));

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

            bool isSelected = (model.SelectedIndex == index);
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
            if (_showInfo && meshContext.Data != null)
            {
                EditorGUILayout.LabelField($"V:{meshContext.Data.VertexCount}", EditorStyles.miniLabel, GUILayout.Width(50));
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
                if (EditorUtility.DisplayDialog("Delete UnityMesh",
                    $"Delete '{meshContext.Name}'?", "Delete", "Cancel"))
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
                EditorGUILayout.HelpBox("No mesh selected", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Selected UnityMesh", EditorStyles.boldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                // 名前（編集可能）
                EditorGUI.BeginChangeCheck();
                string newName = EditorGUILayout.TextField("Name", meshContext.Name);
                if (EditorGUI.EndChangeCheck() && !string.IsNullOrEmpty(newName))
                {
                    meshContext.Name = newName;
                    Repaint();
                }

                // 統計情報
                if (meshContext.Data != null)
                {
                    EditorGUILayout.LabelField("Vertices", meshContext.Data.VertexCount.ToString());
                    EditorGUILayout.LabelField("Faces", meshContext.Data.FaceCount.ToString());

                    // 面タイプ内訳
                    int triCount = 0, quadCount = 0, nGonCount = 0;
                    foreach (var face in meshContext.Data.Faces)
                    {
                        if (face.IsTriangle) triCount++;
                        else if (face.IsQuad) quadCount++;
                        else nGonCount++;
                    }
                    EditorGUILayout.LabelField("  Triangles", triCount.ToString());
                    EditorGUILayout.LabelField("  Quads", quadCount.ToString());
                    if (nGonCount > 0)
                        EditorGUILayout.LabelField("  N-Gons", nGonCount.ToString());

                    EditorGUILayout.LabelField("Materials", (meshContext.Materials?.Count ?? 0).ToString());
                }

                EditorGUILayout.Space();

                // 操作ボタン
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Move to Top"))
                {
                    int current = _context.SelectedMeshIndex;
                    if (current > 0)
                    {
                        ReorderMesh(current, 0);
                    }
                }

                if (GUILayout.Button("Move to Bottom"))
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

                if (GUILayout.Button("Duplicate"))
                {
                    DuplicateMesh(_context.SelectedMeshIndex);
                }

                if (GUILayout.Button("Delete"))
                {
                    if (EditorUtility.DisplayDialog("Delete UnityMesh",
                        $"Delete '{meshContext.Name}'?", "Delete", "Cancel"))
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

            menu.AddItem(new GUIContent("Empty UnityMesh"), false, () =>
            {
                var newMeshContext = new MeshContext
                {
                    Name = "New UnityMesh",
                    Data = new MeshData("New UnityMesh"),
                    UnityMesh = new Mesh(),
                    OriginalPositions = new Vector3[0],
                    Materials = new System.Collections.Generic.List<Material> { null }
                };
                AddMesh(newMeshContext);
            });

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("(Use mesh creators in main window)"));

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