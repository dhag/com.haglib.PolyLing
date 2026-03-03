// Assets/Editor/Poly_Ling_Main/Tools/Core/IToolPanelBaseUXML.cs
// UIToolkit系パネルの共通基底クラス
//
// 【目的】
// EditorWindow + IToolContextReceiver を実装するUIToolkitパネルが
// 各自で手書きしていた以下のボイラープレートを統一:
//   - ToolContext保持 / SetContext
//   - Model.OnListChanged 購読/解除（_subscribedModelパターン）
//   - UndoController.OnUndoRedoPerformed 購読/解除
//   - ToolContext.OnModelChanged 購読/解除（opt-in）
//   - ToolContext.OnMeshSelectionChanged 購読/解除（opt-in）
//   - UXML/USS デュアルパス読み込み
//   - OnDisable/OnDestroy クリーンアップ
//
// 【使い方】
// public class MyPanel : IToolPanelBaseUXML
// {
//     protected override string UxmlPackagePath => "Packages/com.haglib.polyling/Editor/.../MyPanel.uxml";
//     protected override string UxmlAssetsPath  => "Assets/Editor/.../MyPanel.uxml";
//     protected override string UssPackagePath  => "Packages/com.haglib.polyling/Editor/.../MyPanel.uss";
//     protected override string UssAssetsPath   => "Assets/Editor/.../MyPanel.uss";
//
//     protected override void OnCreateGUI(VisualElement root) { /* UI構築 */ }
//     protected override void RefreshAll() { /* 表示更新 */ }
// }

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// UIToolkit系パネルの共通基底クラス。
    /// IToolPanelBase（IMGUI版）と対になる存在。
    /// </summary>
    public abstract class IToolPanelBaseUXML : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        [NonSerialized] private ModelContext _subscribedModel;

        /// <summary>現在のToolContext</summary>
        protected ToolContext ToolCtx => _toolContext;

        /// <summary>現在のModelContext</summary>
        protected ModelContext Model => _toolContext?.Model;

        /// <summary>現在のMeshUndoController</summary>
        protected MeshUndoController UndoController => _toolContext?.UndoController;

        // ================================================================
        // UXML/USSパス（派生クラスで指定）
        // ================================================================

        /// <summary>Package配置時のUXMLパス</summary>
        protected virtual string UxmlPackagePath => null;

        /// <summary>Assets配置時のUXMLパス（フォールバック）</summary>
        protected virtual string UxmlAssetsPath => null;

        /// <summary>Package配置時のUSSパス</summary>
        protected virtual string UssPackagePath => null;

        /// <summary>Assets配置時のUSSパス（フォールバック）</summary>
        protected virtual string UssAssetsPath => null;

        // ================================================================
        // イベント購読制御（派生クラスでオーバーライド）
        // ================================================================

        /// <summary>Model.OnListChanged を購読するか（デフォルト: true）</summary>
        protected virtual bool SubscribeModelListChanged => true;

        /// <summary>UndoController.OnUndoRedoPerformed を購読するか（デフォルト: true）</summary>
        protected virtual bool SubscribeUndoRedo => true;

        /// <summary>ToolContext.OnModelChanged（モデル参照変更）を購読するか（デフォルト: false）</summary>
        protected virtual bool SubscribeModelReferenceChanged => false;

        /// <summary>ToolContext.OnMeshSelectionChanged を購読するか（デフォルト: false）</summary>
        protected virtual bool SubscribeMeshSelectionChanged => false;

        // ================================================================
        // 抽象メンバー
        // ================================================================

        /// <summary>
        /// 全体表示更新。コンテキスト変更、Undo/Redo、モデル変更時に呼ばれる。
        /// </summary>
        protected abstract void RefreshAll();

        // ================================================================
        // UI構築
        // ================================================================

        /// <summary>
        /// UIToolkitのUI構築。UXML/USS読み込み後に呼ばれる。
        /// パスが未指定の場合はUXML読み込みをスキップし直接呼ばれる。
        /// </summary>
        protected virtual void OnCreateGUI(VisualElement root) { }

        private void CreateGUI()
        {
            var root = rootVisualElement;

            // UXML読み込み
            if (UxmlPackagePath != null || UxmlAssetsPath != null)
            {
                var visualTree = TryLoadAsset<VisualTreeAsset>(UxmlPackagePath, UxmlAssetsPath);
                if (visualTree != null)
                    visualTree.CloneTree(root);
                else
                {
                    root.Add(new Label($"UXML not found: {UxmlPackagePath ?? UxmlAssetsPath}"));
                    return;
                }
            }

            // USS読み込み
            if (UssPackagePath != null || UssAssetsPath != null)
            {
                var styleSheet = TryLoadAsset<StyleSheet>(UssPackagePath, UssAssetsPath);
                if (styleSheet != null)
                    root.styleSheets.Add(styleSheet);
            }

            OnCreateGUI(root);
            RefreshAll();
        }

        /// <summary>
        /// Packageパス → Assetsパスの順でアセットを読み込む
        /// </summary>
        private static T TryLoadAsset<T>(string packagePath, string assetsPath) where T : UnityEngine.Object
        {
            T asset = null;
            if (!string.IsNullOrEmpty(packagePath))
                asset = AssetDatabase.LoadAssetAtPath<T>(packagePath);
            if (asset == null && !string.IsNullOrEmpty(assetsPath))
                asset = AssetDatabase.LoadAssetAtPath<T>(assetsPath);
            return asset;
        }

        // ================================================================
        // SetContext
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeAll();

            _toolContext = ctx;

            SubscribeAll();
            OnContextSet();
            RefreshAll();
        }

        /// <summary>
        /// コンテキスト設定後の追加処理（派生クラスでオーバーライド）。
        /// SubscribeAll()完了後、RefreshAll()の前に呼ばれる。
        /// </summary>
        protected virtual void OnContextSet() { }

        // ================================================================
        // イベント購読管理
        // ================================================================

        private void SubscribeAll()
        {
            if (_toolContext == null) return;

            // Model.OnListChanged
            if (SubscribeModelListChanged && _toolContext.Model != null)
            {
                _subscribedModel = _toolContext.Model;
                _subscribedModel.OnListChanged += HandleModelListChanged;
            }

            // UndoController.OnUndoRedoPerformed
            if (SubscribeUndoRedo && _toolContext.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed += HandleUndoRedoPerformed;

            // ToolContext.OnModelChanged（モデル参照変更）
            if (SubscribeModelReferenceChanged)
                _toolContext.OnModelChanged += HandleModelReferenceChanged;

            // ToolContext.OnMeshSelectionChanged
            if (SubscribeMeshSelectionChanged)
                _toolContext.OnMeshSelectionChanged += HandleMeshSelectionChanged;
        }

        private void UnsubscribeAll()
        {
            // Model.OnListChanged（_subscribedModelから解除）
            if (_subscribedModel != null)
            {
                _subscribedModel.OnListChanged -= HandleModelListChanged;
                _subscribedModel = null;
            }

            if (_toolContext == null) return;

            // UndoController.OnUndoRedoPerformed
            if (_toolContext.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= HandleUndoRedoPerformed;

            // ToolContext.OnModelChanged
            if (SubscribeModelReferenceChanged)
                _toolContext.OnModelChanged -= HandleModelReferenceChanged;

            // ToolContext.OnMeshSelectionChanged
            if (SubscribeMeshSelectionChanged)
                _toolContext.OnMeshSelectionChanged -= HandleMeshSelectionChanged;
        }

        // ================================================================
        // イベントハンドラ（派生クラスでオーバーライド可能）
        // ================================================================

        private void HandleModelListChanged() => OnModelListChanged();
        private void HandleUndoRedoPerformed() => OnUndoRedoPerformed();
        private void HandleMeshSelectionChanged() => OnMeshSelectionChanged();

        /// <summary>Model.OnListChanged時の処理（デフォルト: RefreshAll）</summary>
        protected virtual void OnModelListChanged() => RefreshAll();

        /// <summary>Undo/Redo実行時の処理（デフォルト: RefreshAll）</summary>
        protected virtual void OnUndoRedoPerformed() => RefreshAll();

        /// <summary>ToolContext.OnMeshSelectionChanged時の処理</summary>
        protected virtual void OnMeshSelectionChanged() { }

        /// <summary>
        /// モデル参照が変更された時の処理。
        /// デフォルト実装: 旧モデルのイベントを解除し、新モデルに再購読してRefreshAll。
        /// </summary>
        private void HandleModelReferenceChanged()
        {
            // 旧モデルのOnListChanged解除
            if (_subscribedModel != null)
            {
                _subscribedModel.OnListChanged -= HandleModelListChanged;
                _subscribedModel = null;
            }

            // 新モデルのOnListChanged購読
            if (SubscribeModelListChanged && _toolContext?.Model != null)
            {
                _subscribedModel = _toolContext.Model;
                _subscribedModel.OnListChanged += HandleModelListChanged;
            }

            // UndoControllerの再購読（モデル変更でUndoControllerも変わる場合）
            if (SubscribeUndoRedo && _toolContext?.UndoController != null)
            {
                _toolContext.UndoController.OnUndoRedoPerformed -= HandleUndoRedoPerformed;
                _toolContext.UndoController.OnUndoRedoPerformed += HandleUndoRedoPerformed;
            }

            OnModelReferenceChanged();
        }

        /// <summary>モデル参照変更後の追加処理（派生クラスでオーバーライド）</summary>
        protected virtual void OnModelReferenceChanged() => RefreshAll();

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => CleanupBase();
        private void OnDestroy() => CleanupBase();

        private void CleanupBase()
        {
            UnsubscribeAll();
            OnCleanup();
        }

        /// <summary>派生クラス固有のクリーンアップ処理</summary>
        protected virtual void OnCleanup() { }
    }
}
