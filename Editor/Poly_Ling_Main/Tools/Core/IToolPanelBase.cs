// Assets/Editor/Poly_Ling/Tools/IToolPanelBase.cs
// 独立ウィンドウ型ツールの基底クラス
// Phase 4: ModelContext統合、Undo対応

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Context;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 独立ウィンドウ型ツールのインターフェース
    /// </summary>
    public interface IToolPanel
    {
        string Name  { get; }
        string Title { get; }
        IToolSettings Settings { get; }
        void SetContext(ToolContext ctx);
        string GetLocalizedTitle() => null;
    }

    /// <summary>
    /// 独立ウィンドウ型ツールの基底クラス（EditorWindow非依存）
    /// </summary>
    public abstract class IToolPanelBase : EditorWindow, IToolPanel, IToolContextReceiver
    {
        // ================================================================
        // 抽象メンバー
        // ================================================================

        public abstract string Name  { get; }
        public abstract string Title { get; }
        public virtual IToolSettings Settings => null;
        public virtual string GetLocalizedTitle() => null;

        // ================================================================
        // コンテキスト管理
        // ================================================================

        protected ToolContext _context;

        public ToolContext Context                        => _context;
        protected ModelContext  Model                    => _context?.Model;
        protected MeshContext   FirstSelectedMeshContext => _context?.FirstSelectedMeshContext;
        protected MeshObject    FirstSelectedMeshObject  => FirstSelectedMeshContext?.MeshObject;
        protected bool          HasValidSelection        => _context?.HasValidMeshSelection ?? false;

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeUndo();
            _context = ctx;
            SubscribeUndo();
            RestoreSettings();
            OnContextSet();
            Repaint();
        }

        protected virtual void OnContextSet() { }

        // ================================================================
        // Undoイベント購読
        // ================================================================

        private void SubscribeUndo()
        {
            if (_context?.UndoController != null)
                _context.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        private void UnsubscribeUndo()
        {
            if (_context?.UndoController != null)
                _context.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        protected virtual void OnUndoRedoPerformed()
        {
            RestoreSettings();
            Repaint();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeUndo();
        }

        // ================================================================
        // メッシュ操作Undo
        // ================================================================

        protected void RecordTopologyChange(string operationName, Action<MeshObject> action)
        {
            if (FirstSelectedMeshObject == null) return;
            var undo   = _context?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();
            action(FirstSelectedMeshObject);
            _context?.SyncMesh?.Invoke();
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _context?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, operationName));
            }
            _context?.Repaint?.Invoke();
            Repaint();
        }

        protected T RecordTopologyChange<T>(string operationName, Func<MeshObject, T> action)
        {
            if (FirstSelectedMeshObject == null) return default;
            var undo   = _context?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();
            T result   = action(FirstSelectedMeshObject);
            _context?.SyncMesh?.Invoke();
            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                _context?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, operationName));
            }
            _context?.Repaint?.Invoke();
            Repaint();
            return result;
        }

        // ================================================================
        // 設定Undo
        // ================================================================

        protected void RecordSettingsChange(string operationName = null)
        {
            if (_context?.UndoController == null || Settings == null) return;
            var editorState = _context.UndoController.EditorState;
            if (editorState.ToolSettings == null)
                editorState.ToolSettings = new ToolSettingsStorage();
            IToolSettings before = Settings.Clone();
            editorState.ToolSettings.Set(Name, before);
            _context.UndoController.BeginEditorStateDrag();
            editorState.ToolSettings.Set(Name, Settings);
            _context.UndoController.EndEditorStateDrag(operationName ?? $"Change {Title} Settings");
        }

        private void RestoreSettings()
        {
            if (_context?.UndoController == null || Settings == null) return;
            var stored = _context.UndoController.EditorState.ToolSettings?.Get<IToolSettings>(Name);
            if (stored != null) Settings.CopyFrom(stored);
        }

        // ================================================================
        // メッシュリスト操作ヘルパー
        // ================================================================

        protected void AddMesh(MeshContext meshContext)       => _context?.AddMeshContext?.Invoke(meshContext);
        protected void RemoveMesh(int index)                  => _context?.RemoveMeshContext?.Invoke(index);
        protected void SelectMesh(int index)                  => _context?.SelectMeshContext?.Invoke(index);
        protected void DuplicateMesh(int index)               => _context?.DuplicateMeshContent?.Invoke(index);
        protected void ReorderMesh(int from, int to)          => _context?.ReorderMeshContext?.Invoke(from, to);

        // ================================================================
        // UIヘルパー
        // ================================================================

        protected bool DrawNoMeshWarning(string message = "No mesh selected")
        {
            if (!HasValidSelection)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                return false;
            }
            return true;
        }

        protected bool DrawNoContextWarning(string message = "toolContext not set. Open from Poly_Ling window.")
        {
            if (_context == null)
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                return false;
            }
            return true;
        }

        protected bool DrawFieldWithChangeCheck(Action drawAction, string settingsChangeName = null)
        {
            EditorGUI.BeginChangeCheck();
            drawAction();
            if (EditorGUI.EndChangeCheck())
            {
                if (Settings != null && settingsChangeName != null)
                    RecordSettingsChange(settingsChangeName);
                return true;
            }
            return false;
        }
    }

    // ================================================================
    // ToolPanelRegistry
    // ================================================================

    public static class ToolPanelRegistry
    {
        public static readonly (string Title, Action<ToolContext> Open)[] Windows
            = new (string, Action<ToolContext>)[0];
    }
}
