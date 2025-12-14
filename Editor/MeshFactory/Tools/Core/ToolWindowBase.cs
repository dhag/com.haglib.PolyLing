// Assets/Editor/MeshFactory/Tools/ToolWindowBase.cs
// 独立ウィンドウ型ツールの基底クラス
// Phase 4: ModelContext統合、Undo対応

using System;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Model;

// MeshContentはSimpleMeshFactoryのネストクラスを参照
using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 独立ウィンドウ型ツールのインターフェース
    /// </summary>
    public interface IToolWindow
    {
        /// <summary>ウィンドウ識別名</summary>
        string Name { get; }

        /// <summary>表示タイトル</summary>
        string Title { get; }

        /// <summary>設定（Undo対応、不要ならnull）</summary>
        IToolSettings Settings { get; }

        /// <summary>コンテキスト設定</summary>
        void SetContext(ToolContext ctx);
    }

    /// <summary>
    /// 独立ウィンドウ型ツールの基底クラス
    /// EditorWindow + IToolWindowを実装
    /// </summary>
    public abstract class ToolWindowBase : EditorWindow, IToolWindow
    {
        // ================================================================
        // 抽象メンバー
        // ================================================================

        /// <summary>ウィンドウ識別名</summary>
        public abstract string Name { get; }

        /// <summary>表示タイトル</summary>
        public abstract string Title { get; }

        /// <summary>設定（Undo対応、不要ならnull）</summary>
        public virtual IToolSettings Settings => null;

        // ================================================================
        // コンテキスト管理
        // ================================================================

        protected ToolContext _context;

        /// <summary>現在のコンテキスト</summary>
        public ToolContext Context => _context;

        /// <summary>現在のModelContext</summary>
        protected ModelContext Model => _context?.Model;

        /// <summary>現在のMeshContext</summary>
        protected MeshContext CurrentMeshContent => _context?.CurrentMeshContent;

        /// <summary>現在のMeshData</summary>
        protected MeshData CurrentMeshData => CurrentMeshContent?.Data;

        /// <summary>有効なメッシュが選択されているか</summary>
        protected bool HasValidSelection => _context?.HasValidMeshSelection ?? false;

        /// <summary>
        /// コンテキストを設定
        /// </summary>
        public void SetContext(ToolContext ctx)
        {
            // 旧コンテキストのイベント解除
            UnsubscribeUndo();

            _context = ctx;

            // 新コンテキストのイベント購読
            SubscribeUndo();

            // 設定を復元
            RestoreSettings();

            // 派生クラスへの通知
            OnContextSet();

            Repaint();
        }

        /// <summary>
        /// コンテキスト設定時のコールバック（派生クラスでオーバーライド）
        /// </summary>
        protected virtual void OnContextSet() { }

        // ================================================================
        // Undoイベント購読
        // ================================================================

        private void SubscribeUndo()
        {
            if (_context?.UndoController != null)
            {
                _context.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
            }
        }

        private void UnsubscribeUndo()
        {
            if (_context?.UndoController != null)
            {
                _context.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            }
        }

        /// <summary>
        /// Undo/Redo実行後のコールバック
        /// </summary>
        protected virtual void OnUndoRedoPerformed()
        {
            // 設定を復元
            RestoreSettings();
            Repaint();
        }

        /// <summary>
        /// ウィンドウ破棄時のクリーンアップ
        /// </summary>
        protected virtual void OnDestroy()
        {
            UnsubscribeUndo();
        }

        // ================================================================
        // メッシュ操作Undo（トポロジ変更）
        // ================================================================

        /// <summary>
        /// トポロジ変更を記録（面追加/削除、頂点マージなど）
        /// </summary>
        /// <param name="operationName">操作名（Undo履歴に表示）</param>
        /// <param name="action">MeshDataを変更するアクション</param>
        protected void RecordTopologyChange(string operationName, Action<MeshData> action)
        {
            if (CurrentMeshData == null) return;

            var undo = _context?.UndoController;
            var before = undo?.CaptureMeshDataSnapshot();

            // 操作実行
            action(CurrentMeshData);

            // Unity Meshに反映
            _context?.SyncMesh?.Invoke();

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshDataSnapshot();
                undo.RecordTopologyChange(before, after, operationName);
            }

            // 再描画
            _context?.Repaint?.Invoke();
            Repaint();
        }

        /// <summary>
        /// トポロジ変更を記録（戻り値あり版）
        /// </summary>
        protected T RecordTopologyChange<T>(string operationName, Func<MeshData, T> action)
        {
            if (CurrentMeshData == null) return default;

            var undo = _context?.UndoController;
            var before = undo?.CaptureMeshDataSnapshot();

            // 操作実行
            T result = action(CurrentMeshData);

            // Unity Meshに反映
            _context?.SyncMesh?.Invoke();

            // Undo記録
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshDataSnapshot();
                undo.RecordTopologyChange(before, after, operationName);
            }

            // 再描画
            _context?.Repaint?.Invoke();
            Repaint();

            return result;
        }

        // ================================================================
        // 設定Undo
        // ================================================================

        /// <summary>
        /// 設定変更を記録
        /// </summary>
        protected void RecordSettingsChange(string operationName = null)
        {
            if (_context?.UndoController == null || Settings == null) return;

            var editorState = _context.UndoController.EditorState;
            if (editorState.ToolSettings == null)
                editorState.ToolSettings = new ToolSettingsStorage();

            // Before
            var before = Settings.Clone();
            editorState.ToolSettings.Set(Name, before);
            _context.UndoController.BeginEditorStateDrag();

            // After
            editorState.ToolSettings.Set(Name, Settings);
            _context.UndoController.EndEditorStateDrag(operationName ?? $"Change {Title} Settings");
        }

        /// <summary>
        /// 設定を復元（Undo/Redo時）
        /// </summary>
        private void RestoreSettings()
        {
            if (_context?.UndoController == null || Settings == null) return;

            var stored = _context.UndoController.EditorState.ToolSettings?.Get<IToolSettings>(Name);
            if (stored != null)
            {
                Settings.CopyFrom(stored);
            }
        }

        // ================================================================
        // メッシュリスト操作ヘルパー
        // ================================================================

        /// <summary>
        /// メッシュエントリを追加
        /// </summary>
        protected void AddMesh(MeshContext entry)
        {
            _context?.AddMeshContext?.Invoke(entry);
        }

        /// <summary>
        /// メッシュエントリを削除
        /// </summary>
        protected void RemoveMesh(int index)
        {
            _context?.RemoveMeshContext?.Invoke(index);
        }

        /// <summary>
        /// メッシュを選択
        /// </summary>
        protected void SelectMesh(int index)
        {
            _context?.SelectMeshContext?.Invoke(index);
        }

        /// <summary>
        /// メッシュを複製
        /// </summary>
        protected void DuplicateMesh(int index)
        {
            _context?.DuplicateMeshContent?.Invoke(index);
        }

        /// <summary>
        /// メッシュの順序を変更
        /// </summary>
        protected void ReorderMesh(int fromIndex, int toIndex)
        {
            _context?.ReorderMeshContext?.Invoke(fromIndex, toIndex);
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        /// <summary>
        /// メッシュが選択されていない場合の警告を表示
        /// </summary>
        /// <returns>有効なメッシュが選択されていればtrue</returns>
        protected bool DrawNoMeshWarning(string message = "No mesh selected")
        {
            if (!HasValidSelection)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                return false;
            }
            return true;
        }

        /// <summary>
        /// コンテキストが設定されていない場合の警告を表示
        /// </summary>
        /// <returns>コンテキストが有効であればtrue</returns>
        protected bool DrawNoContextWarning(string message = "Context not set. Open from MeshFactory window.")
        {
            if (_context == null)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 変更検出付きでフィールドを描画
        /// </summary>
        protected bool DrawFieldWithChangeCheck(Action drawAction, string settingsChangeName = null)
        {
            EditorGUI.BeginChangeCheck();
            drawAction();
            if (EditorGUI.EndChangeCheck())
            {
                if (Settings != null && settingsChangeName != null)
                {
                    RecordSettingsChange(settingsChangeName);
                }
                return true;
            }
            return false;
        }
    }

    // ================================================================
    // ToolWindowRegistry
    // ================================================================

    /// <summary>
    /// ToolWindow登録用レジストリ
    /// </summary>
    public static class ToolWindowRegistry
    {
        /// <summary>
        /// 登録されたウィンドウオープナー
        /// (タイトル, オープン関数) のペア
        /// </summary>
        public static readonly (string Title, Action<ToolContext> Open)[] Windows = new (string, Action<ToolContext>)[]
        {
            // 例: ("Deform", DeformWindow.Open),
            // 例: ("UV Editor", UVEditorWindow.Open),
            // 例: ("UnityMesh List", MeshListWindow.Open),
        };
    }
}
