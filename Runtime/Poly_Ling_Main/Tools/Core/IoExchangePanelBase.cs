// Assets/Editor/Poly_Ling_Main/Tools/Core/IoExchangePanelBase.cs
// インポート/エクスポートパネル共通基底クラス
//
// 【目的】
// Import/Exportパネル 8本に共通するボイラープレートを統一:
//   - ToolContext保持 / SetContext(ToolContext)
//   - PanelContext保持 / SetContext(PanelContext)
//   - OnEnable での再接続
//   - UXML/USS デュアルパス読み込み（UxmlPackagePath が null なら IMGUI 継続）
//   - OnDisable/OnDestroy クリーンアップ
//
// 【使い方】
//   public class PMXImportPanel : IoExchangePanelBase
//   {
//       protected override string UxmlPackagePath => "Packages/...uxml";
//       protected override string UxmlAssetsPath  => "Assets/...uxml";
//       protected override string UssPackagePath  => "Packages/...uss";
//       protected override string UssAssetsPath   => "Assets/...uss";
//
//       // UIElements 版
//       protected override void OnCreateGUI(VisualElement root) { /* UI構築 */ }
//       protected override void RefreshAll() { /* 表示更新 */ }
//
//       // IMGUI 版（UxmlPackagePath == null の場合）
//       private void OnGUI() { /* IMGUI描画 */ }
//   }

using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// インポート/エクスポートパネル共通基底クラス。
    /// IToolContextReceiver と IPanelContextReceiver を両方実装する。
    /// </summary>
    public abstract class IoExchangePanelBase : EditorWindow,
        IToolContextReceiver,
        IPanelContextReceiver
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] protected ToolContext _toolCtx;
        [NonSerialized] protected PanelContext _panelCtx;

        /// <summary>現在の ModelContext</summary>
        protected ModelContext Model => _toolCtx?.Model;

        /// <summary>現在の MeshUndoController</summary>
        protected MeshUndoController UndoController => _toolCtx?.UndoController;

        // ================================================================
        // UXML/USS パス（派生クラスで指定。null ならスキップ）
        // ================================================================

        protected virtual string UxmlPackagePath => null;
        protected virtual string UxmlAssetsPath  => null;
        protected virtual string UssPackagePath  => null;
        protected virtual string UssAssetsPath   => null;

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            _toolCtx = ctx;
            OnToolContextSet();
            RefreshAll();
        }

        /// <summary>ToolContext 設定後の追加処理（派生クラスでオーバーライド）</summary>
        protected virtual void OnToolContextSet() { }

        // ================================================================
        // IPanelContextReceiver
        // ================================================================

        public void SetContext(PanelContext ctx)
        {
            UnsubscribePanelContext();
            _panelCtx = ctx;
            SubscribePanelContext();
            OnPanelContextSet();
            RefreshAll();
        }

        /// <summary>PanelContext 設定後の追加処理（派生クラスでオーバーライド）</summary>
        protected virtual void OnPanelContextSet() { }

        private void SubscribePanelContext()
        {
            if (_panelCtx == null) return;
            _panelCtx.OnViewChanged += HandleViewChanged;
        }

        private void UnsubscribePanelContext()
        {
            if (_panelCtx == null) return;
            _panelCtx.OnViewChanged -= HandleViewChanged;
        }

        private void HandleViewChanged(IProjectView view, ChangeKind kind)
            => OnViewChanged(view, kind);

        /// <summary>PanelContext.OnViewChanged 時の処理（派生クラスでオーバーライド）</summary>
        protected virtual void OnViewChanged(IProjectView view, ChangeKind kind) { }

        // ================================================================
        // UIElements 構築
        // ================================================================

        private void CreateGUI()
        {
            // UXML が指定されていない場合は IMGUI 継続（CreateGUI を呼ばない）
            if (UxmlPackagePath == null && UxmlAssetsPath == null)
                return;

            var root = rootVisualElement;

            var visualTree = TryLoadAsset<VisualTreeAsset>(UxmlPackagePath, UxmlAssetsPath);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPackagePath ?? UxmlAssetsPath}"));
                return;
            }

            if (UssPackagePath != null || UssAssetsPath != null)
            {
                var styleSheet = TryLoadAsset<StyleSheet>(UssPackagePath, UssAssetsPath);
                if (styleSheet != null)
                    root.styleSheets.Add(styleSheet);
            }

            OnCreateGUI(root);
            RefreshAll();
        }

        private static T TryLoadAsset<T>(string packagePath, string assetsPath)
            where T : UnityEngine.Object
        {
            T asset = null;
            if (!string.IsNullOrEmpty(packagePath))
                asset = AssetDatabase.LoadAssetAtPath<T>(packagePath);
            if (asset == null && !string.IsNullOrEmpty(assetsPath))
                asset = AssetDatabase.LoadAssetAtPath<T>(assetsPath);
            return asset;
        }

        /// <summary>UIElements UI構築。UXML クローン後に呼ばれる。</summary>
        protected virtual void OnCreateGUI(VisualElement root) { }

        /// <summary>全体表示更新。コンテキスト変更・ビュー更新時に呼ばれる。</summary>
        protected virtual void RefreshAll() { }

        // ================================================================
        // ライフサイクル
        // ================================================================

        /// <summary>
        /// OnEnable: ドメインリロード後など _panelCtx が残っている場合に再購読。
        /// 派生クラスでオーバーライドする場合は base.OnEnable() を呼ぶこと。
        /// </summary>
        protected virtual void OnEnable()
        {
            // _panelCtx がシリアライズされていないので、ドメインリロード後は null になる。
            // それでも、ReconnectAllPanelContexts が再呼び出しされれば SetContext が来る。
            if (_panelCtx != null)
            {
                UnsubscribePanelContext();
                SubscribePanelContext();
            }
        }

        protected virtual void OnDisable() => UnsubscribePanelContext();
        protected virtual void OnDestroy() => UnsubscribePanelContext();
    }
}
