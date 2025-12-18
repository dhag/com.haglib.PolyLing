// Assets/Editor/MeshFactory/Tools/PrimitiveMeshTool.cs
// プリミティブメッシュ生成ツール
// 各種形状のCreatorウィンドウを一元管理

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools.Creators;
using MeshFactory.Localization;

namespace MeshFactory.Tools
{
    /// <summary>
    /// プリミティブメッシュ生成ツール
    /// </summary>
    public class PrimitiveMeshTool : IEditTool
    {
        // ================================================================
        // IEditTool 基本プロパティ
        // ================================================================

        public string Name => "Primitive";
        public string DisplayName => "Primitive";

        private PrimitiveMeshSettings _settings = new PrimitiveMeshSettings();
        public IToolSettings Settings => _settings;

        // ================================================================
        // Creator登録
        // ================================================================

        /// <summary>
        /// 登録されているCreator一覧
        /// </summary>
        private static readonly List<MeshCreatorEntry> _creators = new List<MeshCreatorEntry>
        {
            new MeshCreatorEntry("+ Cube...", (cb) => CubeMeshCreatorWindow.Open(cb), "Basic", 0),
            new MeshCreatorEntry("+ Sphere...", (cb) => SphereMeshCreatorWindow.Open(cb), "Basic", 1),
            new MeshCreatorEntry("+ Cylinder...", (cb) => CylinderMeshCreatorWindow.Open(cb), "Basic", 2),
            new MeshCreatorEntry("+ Capsule...", (cb) => CapsuleMeshCreatorWindow.Open(cb), "Basic", 3),
            new MeshCreatorEntry("+ Plane...", (cb) => PlaneMeshCreatorWindow.Open(cb), "Basic", 4),
            new MeshCreatorEntry("+ Pyramid...", (cb) => PyramidMeshCreatorWindow.Open(cb), "Basic", 5),
            new MeshCreatorEntry("+ Revolution...", (cb) => RevolutionMeshCreatorWindow.Open(cb), "Advanced", 10),
            new MeshCreatorEntry("+ 2D Profile...", (cb) => Profile2DExtrudeWindow.Open(cb), "Advanced", 11),
            new MeshCreatorEntry("+ NohMask...", (cb) => NohMaskMeshCreatorWindow.Open(cb), "Special", 20),
        };

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        // ================================================================
        // IEditTool 実装（マウスイベント - パススルー）
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        public void DrawGizmo(ToolContext ctx)
        {
            // ギズモなし
        }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            // リセット処理なし
        }

        // ================================================================
        // 設定UI
        // ================================================================

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Primitive Mesh", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 追加モード設定
            DrawAddModeUI();

            EditorGUILayout.Space(10);

            // Creator ボタン一覧
            DrawCreatorButtons();
        }

        /// <summary>
        /// 追加モードUIを描画
        /// </summary>
        private void DrawAddModeUI()
        {
            EditorGUILayout.BeginHorizontal();

            bool newAddToCurrentMesh = EditorGUILayout.ToggleLeft(
                L.Get("AddToCurrent"),
                _settings.AddToCurrentMesh,
                GUILayout.Width(110));

            if (newAddToCurrentMesh != _settings.AddToCurrentMesh)
            {
                _settings.AddToCurrentMesh = newAddToCurrentMesh;
            }

            // 追加先がない場合は警告
            if (_settings.AddToCurrentMesh && _context != null && _context.MeshData == null)
            {
                EditorGUILayout.LabelField(L.Get("NoMeshSelected"), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Creatorボタンを描画
        /// </summary>
        private void DrawCreatorButtons()
        {
            EditorGUILayout.LabelField(L.Get("CreateMesh"), EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(3);

            // 2列レイアウト
            int buttonsPerRow = 2;
            int buttonCount = 0;

            EditorGUILayout.BeginHorizontal();

            foreach (var creator in _creators)
            {
                if (GUILayout.Button(creator.ButtonLabel, GUILayout.MinWidth(100)))
                {
                    OpenCreatorWindow(creator);
                }

                buttonCount++;
                if (buttonCount % buttonsPerRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            // 最後の行を閉じる
            if (buttonCount % buttonsPerRow != 0)
            {
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Creatorウィンドウを開く
        /// </summary>
        private void OpenCreatorWindow(MeshCreatorEntry creator)
        {
            // コールバックを設定してウィンドウを開く
            creator.OpenAction?.Invoke(OnMeshDataCreated);
        }

        /// <summary>
        /// メッシュ生成完了時のコールバック
        /// </summary>
        private void OnMeshDataCreated(MeshData meshData, string name)
        {
            if (_context == null)
            {
                Debug.LogWarning("[PrimitiveMeshTool] Context is null, cannot add mesh");
                return;
            }

            // ToolContext経由でメッシュを追加
            if (_settings.AddToCurrentMesh)
            {
                _context.AddMeshDataToCurrentMesh?.Invoke(meshData, name);
            }
            else
            {
                _context.CreateNewMeshContext?.Invoke(meshData, name);
            }
        }

        // ================================================================
        // 静的ヘルパー（Creator登録）
        // ================================================================

        /// <summary>
        /// Creatorを追加（拡張用）
        /// </summary>
        public static void RegisterCreator(MeshCreatorEntry entry)
        {
            if (entry != null && !_creators.Contains(entry))
            {
                _creators.Add(entry);
                // Order順でソート
                _creators.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }

        /// <summary>
        /// Creatorを削除（拡張用）
        /// </summary>
        public static void UnregisterCreator(string buttonLabel)
        {
            _creators.RemoveAll(c => c.ButtonLabel == buttonLabel);
        }
    }
}
