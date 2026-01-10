// Assets/Editor/MeshFactory/Tools/PrimitiveMeshTool.cs
// プリミティブメッシュ生成ツール
// 各種形状のCreatorウィンドウを一元管理
// ローカライズ対応版

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
    public partial class PrimitiveMeshTool : IEditTool
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
        /// Creator情報（ローカライズキーとアクション）
        /// </summary>
        private class CreatorInfo
        {
            public string LabelKey { get; set; }
            public Action<Action<MeshObject, string>> OpenAction { get; set; }
            public string Category { get; set; }
            public int Order { get; set; }

            public CreatorInfo(string labelKey, Action<Action<MeshObject, string>> openAction, string category = null, int order = 0)
            {
                LabelKey = labelKey;
                OpenAction = openAction;
                Category = category;
                Order = order;
            }
        }

        /// <summary>
        /// 登録されているCreator一覧
        /// </summary>
        private static readonly List<CreatorInfo> _creators = new List<CreatorInfo>
        {
            new CreatorInfo("BtnCube", (cb) => CubeMeshCreatorWindow.Open(cb), "Basic", 0),
            new CreatorInfo("BtnSphere", (cb) => SphereMeshCreatorWindow.Open(cb), "Basic", 1),
            new CreatorInfo("BtnCylinder", (cb) => CylinderMeshCreatorWindow.Open(cb), "Basic", 2),
            new CreatorInfo("BtnCapsule", (cb) => CapsuleMeshCreatorWindow.Open(cb), "Basic", 3),
            new CreatorInfo("BtnPlane", (cb) => PlaneMeshCreatorWindow.Open(cb), "Basic", 4),
            new CreatorInfo("BtnPyramid", (cb) => PyramidMeshCreatorWindow.Open(cb), "Basic", 5),
            new CreatorInfo("BtnRevolution", (cb) => RevolutionMeshCreatorWindow.Open(cb), "Advanced", 10),
            new CreatorInfo("BtnProfile2D", (cb) => Profile2DExtrudeWindow.Open(cb), "Advanced", 11),
            new CreatorInfo("BtnNohMask", (cb) => NohMaskMeshCreatorWindow.Open(cb), "Special", 20),
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
            EditorGUILayout.LabelField(T("PrimitiveMesh"), EditorStyles.boldLabel);
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
                T("AddToCurrent"),
                _settings.AddToCurrentMesh,
                GUILayout.Width(130));

            if (newAddToCurrentMesh != _settings.AddToCurrentMesh)
            {
                _settings.AddToCurrentMesh = newAddToCurrentMesh;
            }

            // 追加先がない場合は警告
            if (_settings.AddToCurrentMesh && _context != null && _context.MeshObject == null)
            {
                EditorGUILayout.LabelField(T("NoMeshSelected"), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Creatorボタンを描画
        /// </summary>
        private void DrawCreatorButtons()
        {
            EditorGUILayout.LabelField(T("CreateMesh"), EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(3);

            // 2列レイアウト
            int buttonsPerRow = 2;
            int buttonCount = 0;

            EditorGUILayout.BeginHorizontal();

            foreach (var creator in _creators)
            {
                if (GUILayout.Button(T(creator.LabelKey), GUILayout.MinWidth(100)))
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
        private void OpenCreatorWindow(CreatorInfo creator)
        {
            // コールバックを設定してウィンドウを開く
            creator.OpenAction?.Invoke(OnMeshObjectCreated);
        }

        /// <summary>
        /// メッシュ生成完了時のコールバック
        /// </summary>
        private void OnMeshObjectCreated(MeshObject meshObject, string name)
        {
            if (_context == null)
            {
                Debug.LogWarning($"[PrimitiveMeshTool] {T("ContextNull")}");
                return;
            }

            // ToolContext経由でメッシュを追加
            if (_settings.AddToCurrentMesh)
            {
                _context.AddMeshObjectToCurrentMesh?.Invoke(meshObject, name);
            }
            else
            {
                _context.CreateNewMeshContext?.Invoke(meshObject, name);
            }
        }

        // ================================================================
        // 静的ヘルパー（Creator登録）
        // ================================================================

        /// <summary>
        /// Creatorを追加（拡張用）
        /// </summary>
        public static void RegisterCreator(string labelKey, Action<Action<MeshObject, string>> openAction, string category = null, int order = 0)
        {
            var entry = new CreatorInfo(labelKey, openAction, category, order);
            if (!_creators.Exists(c => c.LabelKey == labelKey))
            {
                _creators.Add(entry);
                // Order順でソート
                _creators.Sort((a, b) => a.Order.CompareTo(b.Order));
            }
        }

        /// <summary>
        /// Creatorを削除（拡張用）
        /// </summary>
        public static void UnregisterCreator(string labelKey)
        {
            _creators.RemoveAll(c => c.LabelKey == labelKey);
        }
    }
}
