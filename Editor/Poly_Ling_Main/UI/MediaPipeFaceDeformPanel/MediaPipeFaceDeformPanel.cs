// MediaPipeFaceDeformPanel.cs
// MediaPipeフェイスメッシュ変形ツールパネル (UIToolkit)
// BEFORE/AFTER/TRIANGLESの固定ファイルを読み込み、
// カレントメッシュの頂点XYをMediaPipe変形に基づいて変形し、新メッシュを生成する。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Tools.MediaPipe;

namespace Poly_Ling.UI
{
    /// <summary>
    /// MediaPipeフェイス変形パネル（UIToolkit版）
    /// </summary>
    public class MediaPipeFaceDeformPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // 固定ファイルパス
        // ================================================================

        private const string BasePath       = "Assets/MediaPipe";
        private const string BeforeFileName = "before.json";
        private const string AfterFileName  = "after.json";
        private const string TriFileName    = "triangles.json";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MediaPipeFaceDeformPanel/MediaPipeFaceDeformPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MediaPipeFaceDeformPanel/MediaPipeFaceDeformPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MediaPipeFaceDeformPanel/MediaPipeFaceDeformPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MediaPipeFaceDeformPanel/MediaPipeFaceDeformPanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            { "WindowTitle", new() { { "en", "MediaPipe Face Deform" }, { "ja", "MediaPipeフェイス変形" } } },
            { "Execute", new() { { "en", "Execute" }, { "ja", "実行" } } },
            { "NoContext", new() { { "en", "toolContext not set. Open from Poly_Ling window." }, { "ja", "toolContext未設定。Poly_Lingウィンドウから開いてください。" } } },
            { "NoMesh", new() { { "en", "No mesh selected." }, { "ja", "メッシュが選択されていません。" } } },
            { "FileNotFound", new() { { "en", "File not found: {0}" }, { "ja", "ファイルが見つかりません: {0}" } } },
            { "Success", new() { { "en", "Created deformed mesh. Bound: {0}/{1} vertices" },
                                  { "ja", "変形メッシュを作成しました。バインド: {0}/{1} 頂点" } } },
            { "Error", new() { { "en", "Error: {0}" }, { "ja", "エラー: {0}" } } },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;
        private MeshContext FirstSelectedMeshContext => _toolContext?.FirstDrawableMeshContext;
        private MeshObject FirstSelectedMeshObject => FirstSelectedMeshContext?.MeshObject;
        private bool HasValidSelection => _toolContext?.HasValidMeshSelection ?? false;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private VisualElement _mainContent;
        private Button _btnExecute;
        private VisualElement _statusSection;
        private Label _statusLabel;

        // ================================================================
        // Open
        // ================================================================

        public static MediaPipeFaceDeformPanel Open(ToolContext ctx)
        {
            var panel = GetWindow<MediaPipeFaceDeformPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(300, 120);
            panel.SetContext(ctx);
            panel.Show();
            return panel;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
        private void Cleanup() { }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            _toolContext = ctx;
            ClearStatus();
            Refresh();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<VisualElement>("main-content");
            _btnExecute = root.Q<Button>("btn-execute");
            _statusSection = root.Q<VisualElement>("status-section");
            _statusLabel = root.Q<Label>("status-label");

            _btnExecute.clicked += OnExecute;
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            if (!HasValidSelection)
            {
                ShowWarning(T("NoMesh"));
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            _btnExecute.text = T("Execute");
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // ステータス表示
        // ================================================================

        private void ShowStatus(string message, bool isError = false)
        {
            if (_statusSection == null) return;

            _statusSection.style.display = DisplayStyle.Flex;
            _statusLabel.text = message;

            _statusLabel.RemoveFromClassList("mpd-status--info");
            _statusLabel.RemoveFromClassList("mpd-status--error");
            _statusLabel.AddToClassList(isError ? "mpd-status--error" : "mpd-status--info");
        }

        private void ClearStatus()
        {
            if (_statusSection != null)
                _statusSection.style.display = DisplayStyle.None;
        }

        // ================================================================
        // 実行
        // ================================================================

        private void OnExecute()
        {
            ClearStatus();

            try
            {
                // ファイルパス
                string beforePath = Path.Combine(BasePath, BeforeFileName);
                string afterPath  = Path.Combine(BasePath, AfterFileName);
                string triPath    = Path.Combine(BasePath, TriFileName);

                // ファイル存在チェック
                if (!File.Exists(beforePath))
                {
                    ShowStatus(T("FileNotFound", beforePath), true);
                    return;
                }
                if (!File.Exists(afterPath))
                {
                    ShowStatus(T("FileNotFound", afterPath), true);
                    return;
                }
                if (!File.Exists(triPath))
                {
                    ShowStatus(T("FileNotFound", triPath), true);
                    return;
                }

                // JSON読み込み
                Vector2[] beforeLandmarks = MediaPipeFaceDeformer.LoadLandmarks(beforePath);
                Vector2[] afterLandmarks  = MediaPipeFaceDeformer.LoadLandmarks(afterPath);
                int[][] triangles         = MediaPipeFaceDeformer.ParseTrianglesJson(
                                                File.ReadAllText(triPath));

                // ソースメッシュ
                MeshObject sourceMesh = FirstSelectedMeshObject;
                MeshContext sourceMeshContext = FirstSelectedMeshContext;
                int vertexCount = sourceMesh.VertexCount;

                // 頂点位置配列を作成
                var positions = new Vector3[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    positions[i] = sourceMesh.Vertices[i].Position;
                }

                // バインド
                var deformer = new MediaPipeFaceDeformer();
                deformer.SetBaseMesh(beforeLandmarks, triangles);
                int bindCount = deformer.Bind(positions);

                // 変形適用
                deformer.Apply(afterLandmarks, positions);

                // クローンして頂点位置を書き換え
                MeshObject clonedMesh = sourceMesh.Clone();
                clonedMesh.Name = sourceMesh.Name + "_MP";
                for (int i = 0; i < vertexCount; i++)
                {
                    clonedMesh.Vertices[i].Position = positions[i];
                }

                // MeshContext作成
                var newMeshContext = new MeshContext
                {
                    MeshObject = clonedMesh,
                    Materials = new List<Material>(sourceMeshContext.Materials ?? new List<Material>())
                };
                newMeshContext.UnityMesh = clonedMesh.ToUnityMesh();
                newMeshContext.UnityMesh.name = clonedMesh.Name;
                newMeshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

                // メッシュリストに追加
                _toolContext.AddMeshContext?.Invoke(newMeshContext);

                // ステータス
                ShowStatus(T("Success", bindCount, vertexCount));

                _toolContext.Repaint?.Invoke();
            }
            catch (Exception ex)
            {
                ShowStatus(T("Error", ex.Message), true);
                Debug.LogException(ex);
            }
        }
    }
}
