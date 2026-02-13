// Assets/Editor/Poly_Ling/ToolPanels/MediaPipe/MediaPipeFaceDeformPanel.cs
// MediaPipeフェイスメッシュ変形ツールパネル
// BEFORE/AFTER/TRIANGLESの固定ファイルを読み込み、
// カレントメッシュの頂点XYをMediaPipe変形に基づいて変形し、新メッシュを生成する。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Model;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>
    /// MediaPipeフェイス変形パネル
    /// </summary>
    public class MediaPipeFaceDeformPanel : IToolPanelBase
    {
        // ================================================================
        // 固定ファイルパス
        // ================================================================

        private const string BasePath       = "Assets/MediaPipe";
        private const string BeforeFileName = "before.json";
        private const string AfterFileName  = "after.json";
        private const string TriFileName    = "triangles.json";

        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "MediaPipeFaceDeform";
        public override string Title => "MediaPipe Face Deform";
        public override IToolSettings Settings => null;

        public override string GetLocalizedTitle() => L.Get("Window_MediaPipeFaceDeform");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            { "Execute", new() { { "en", "Execute" }, { "ja", "実行" } } },
            { "NoMesh", new() { { "en", "No mesh selected." }, { "ja", "メッシュが選択されていません。" } } },
            { "FileNotFound", new() { { "en", "File not found: {0}" }, { "ja", "ファイルが見つかりません: {0}" } } },
            { "Success", new() { { "en", "Created deformed mesh. Bound: {0}/{1} vertices" },
                                  { "ja", "変形メッシュを作成しました。バインド: {0}/{1} 頂点" } } },
            { "Error", new() { { "en", "Error: {0}" }, { "ja", "エラー: {0}" } } },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // 状態
        // ================================================================

        private string _statusMessage;
        private MessageType _statusType;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MediaPipeFaceDeformPanel>();
            panel.titleContent = new GUIContent("MediaPipe Face Deform");
            panel.minSize = new Vector2(300, 120);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning())
                return;

            if (!DrawNoMeshWarning(T("NoMesh")))
                return;

            EditorGUILayout.Space(8);

            // 実行ボタン
            if (GUILayout.Button(T("Execute"), GUILayout.Height(30)))
            {
                Execute();
            }

            // ステータス表示
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
            }
        }

        // ================================================================
        // 実行
        // ================================================================

        private void Execute()
        {
            _statusMessage = null;

            try
            {
                // ファイルパス
                string beforePath = Path.Combine(BasePath, BeforeFileName);
                string afterPath  = Path.Combine(BasePath, AfterFileName);
                string triPath    = Path.Combine(BasePath, TriFileName);

                // ファイル存在チェック
                if (!File.Exists(beforePath))
                {
                    _statusMessage = T("FileNotFound", beforePath);
                    _statusType = MessageType.Error;
                    return;
                }
                if (!File.Exists(afterPath))
                {
                    _statusMessage = T("FileNotFound", afterPath);
                    _statusType = MessageType.Error;
                    return;
                }
                if (!File.Exists(triPath))
                {
                    _statusMessage = T("FileNotFound", triPath);
                    _statusType = MessageType.Error;
                    return;
                }

                // JSON読み込み
                Vector2[] beforeLandmarks = MediaPipeFaceDeformer.LoadLandmarks(beforePath);
                Vector2[] afterLandmarks  = MediaPipeFaceDeformer.LoadLandmarks(afterPath);
                int[][] triangles         = MediaPipeFaceDeformer.ParseTrianglesJson(
                                                File.ReadAllText(triPath));

                // ソースメッシュ
                MeshObject sourceMesh = CurrentMeshObject;
                MeshContext sourceMeshContext = CurrentMeshContent;
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
                _context.AddMeshContext?.Invoke(newMeshContext);

                // ステータス
                _statusMessage = T("Success", bindCount, vertexCount);
                _statusType = MessageType.Info;

                _context.Repaint?.Invoke();
            }
            catch (Exception ex)
            {
                _statusMessage = T("Error", ex.Message);
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }

        // ================================================================
        // コンテキスト更新時
        // ================================================================

        protected override void OnContextSet()
        {
            _statusMessage = null;
        }
    }
}
