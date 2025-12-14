// Assets/Editor/MeshFactory/Tools/Core/ToolManager.cs
// ツールの登録・管理・切り替えを一元化するマネージャ
// Phase 1: SimpleMeshFactoryからツール管理を分離

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshFactory.Tools
{
    /// <summary>
    /// ツールの登録・管理・切り替えを一元化
    /// </summary>
    public class ToolManager
    {
        // ================================================================
        // フィールド
        // ================================================================

        /// <summary>登録されたツール（名前→インスタンス）</summary>
        private readonly Dictionary<string, IEditTool> _tools = new Dictionary<string, IEditTool>();

        /// <summary>登録順序を保持（UI表示用）</summary>
        private readonly List<string> _toolOrder = new List<string>();

        /// <summary>現在のツール</summary>
        private IEditTool _currentTool;

        /// <summary>共有ToolContext</summary>
        private ToolContext _toolContext;

        /// <summary>デフォルトツール名</summary>
        private string _defaultToolName = "Select";

        // ================================================================
        // イベント
        // ================================================================

        /// <summary>ツール切り替え時のイベント</summary>
        public event Action<IEditTool, IEditTool> OnToolChanged;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>現在のツール</summary>
        public IEditTool CurrentTool => _currentTool;

        /// <summary>現在のツール名</summary>
        public string CurrentToolName => _currentTool?.Name ?? "";

        /// <summary>ToolContext</summary>
        public ToolContext toolContext => _toolContext;

        /// <summary>登録されているツール数</summary>
        public int ToolCount => _tools.Count;

        /// <summary>登録されているツール名一覧（登録順）</summary>
        public IReadOnlyList<string> ToolNames => _toolOrder;

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public ToolManager()
        {
            _toolContext = new ToolContext();
        }

        /// <summary>
        /// ツールを登録
        /// </summary>
        /// <param name="tool">登録するツール</param>
        /// <returns>チェーン用にthisを返す</returns>
        public ToolManager Register(IEditTool tool)
        {
            if (tool == null)
                throw new ArgumentNullException(nameof(tool));

            string name = tool.Name;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Tool name cannot be null or empty");

            if (_tools.ContainsKey(name))
            {
                Debug.LogWarning($"[ToolManager] Tool '{name}' is already registered. Replacing.");
                _toolOrder.Remove(name);
            }

            _tools[name] = tool;
            _toolOrder.Add(name);

            // 最初に登録されたツールをデフォルトとして設定
            if (_currentTool == null)
            {
                _currentTool = tool;
            }

            return this;
        }

        /// <summary>
        /// 複数のツールを一括登録
        /// </summary>
        public ToolManager RegisterAll(params IEditTool[] tools)
        {
            foreach (var tool in tools)
            {
                Register(tool);
            }
            return this;
        }

        /// <summary>
        /// デフォルトツールを設定
        /// </summary>
        public ToolManager SetDefault(string toolName)
        {
            _defaultToolName = toolName;
            if (_currentTool == null && _tools.TryGetValue(toolName, out var tool))
            {
                _currentTool = tool;
            }
            return this;
        }

        // ================================================================
        // ツール取得
        // ================================================================

        /// <summary>
        /// 名前でツールを取得
        /// </summary>
        public IEditTool GetTool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return _tools.TryGetValue(name, out var tool) ? tool : null;
        }

        /// <summary>
        /// 型でツールを取得
        /// </summary>
        public T GetTool<T>() where T : class, IEditTool
        {
            foreach (var tool in _tools.Values)
            {
                if (tool is T typedTool)
                    return typedTool;
            }
            return null;
        }

        /// <summary>
        /// ツールが登録されているか
        /// </summary>
        public bool HasTool(string name)
        {
            return !string.IsNullOrEmpty(name) && _tools.ContainsKey(name);
        }

        /// <summary>
        /// 全ツールを列挙
        /// </summary>
        public IEnumerable<IEditTool> AllTools => _tools.Values;

        // ================================================================
        // ツール切り替え
        // ================================================================

        /// <summary>
        /// 名前でツールを切り替え
        /// </summary>
        /// <returns>切り替えが成功したか</returns>
        public bool SetTool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (!_tools.TryGetValue(name, out var newTool))
            {
                Debug.LogWarning($"[ToolManager] Tool '{name}' not found.");
                return false;
            }

            return SetToolInternal(newTool);
        }

        /// <summary>
        /// インスタンスでツールを切り替え
        /// </summary>
        public bool SetTool(IEditTool tool)
        {
            if (tool == null)
                return false;

            // 登録済みか確認
            if (!_tools.ContainsKey(tool.Name))
            {
                Debug.LogWarning($"[ToolManager] Tool '{tool.Name}' is not registered.");
                return false;
            }

            return SetToolInternal(tool);
        }

        /// <summary>
        /// 内部切り替え処理
        /// </summary>
        private bool SetToolInternal(IEditTool newTool)
        {
            if (newTool == _currentTool)
                return false;

            var oldTool = _currentTool;

            // 旧ツールを非アクティブ化
            oldTool?.OnDeactivate(_toolContext);

            // 切り替え
            _currentTool = newTool;

            // 新ツールをアクティブ化
            _currentTool?.OnActivate(_toolContext);

            // イベント発火
            OnToolChanged?.Invoke(oldTool, _currentTool);

            return true;
        }

        /// <summary>
        /// デフォルトツールに戻す
        /// </summary>
        public void ResetToDefault()
        {
            SetTool(_defaultToolName);
        }

        // ================================================================
        // ツール操作
        // ================================================================

        /// <summary>
        /// 現在のツールをリセット
        /// </summary>
        public void ResetCurrentTool()
        {
            _currentTool?.Reset();
        }

        /// <summary>
        /// 全ツールをリセット
        /// </summary>
        public void ResetAllTools()
        {
            foreach (var tool in _tools.Values)
            {
                tool.Reset();
            }
        }

        // ================================================================
        // 設定の保存/復元
        // ================================================================

        /// <summary>
        /// 全ツールの設定をストレージに保存
        /// </summary>
        public void SaveSettings(ToolSettingsStorage storage)
        {
            if (storage == null) return;

            foreach (var tool in _tools.Values)
            {
                if (tool.Settings != null)
                {
                    storage.Set(tool.Name, tool.Settings);
                }
            }
        }

        /// <summary>
        /// ストレージから全ツールに設定を復元
        /// </summary>
        public void LoadSettings(ToolSettingsStorage storage)
        {
            if (storage == null) return;

            foreach (var tool in _tools.Values)
            {
                storage.ApplyToTool(tool);
            }
        }

        /// <summary>
        /// 指定ツールの設定を復元
        /// </summary>
        public void LoadToolSettings(string toolName, ToolSettingsStorage storage)
        {
            if (storage == null || string.IsNullOrEmpty(toolName))
                return;

            if (_tools.TryGetValue(toolName, out var tool))
            {
                storage.ApplyToTool(tool);
            }
        }

        // ================================================================
        // マウスイベント委譲
        // ================================================================

        /// <summary>
        /// マウスダウンを現在のツールに委譲
        /// </summary>
        public bool OnMouseDown(Vector2 mousePos)
        {
            return _currentTool?.OnMouseDown(_toolContext, mousePos) ?? false;
        }

        /// <summary>
        /// マウスドラッグを現在のツールに委譲
        /// </summary>
        public bool OnMouseDrag(Vector2 mousePos, Vector2 delta)
        {
            return _currentTool?.OnMouseDrag(_toolContext, mousePos, delta) ?? false;
        }

        /// <summary>
        /// マウスアップを現在のツールに委譲
        /// </summary>
        public bool OnMouseUp(Vector2 mousePos)
        {
            return _currentTool?.OnMouseUp(_toolContext, mousePos) ?? false;
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// 現在のツールのギズモを描画
        /// </summary>
        public void DrawGizmo()
        {
            _currentTool?.DrawGizmo(_toolContext);
        }

        /// <summary>
        /// 現在のツールの設定UIを描画
        /// </summary>
        public void DrawSettingsUI()
        {
            _currentTool?.DrawSettingsUI();
        }

        // ================================================================
        // 現在のツールが特定の型かチェック
        // ================================================================

        /// <summary>
        /// 現在のツールが指定した型か判定
        /// </summary>
        public bool IsCurrentTool<T>() where T : class, IEditTool
        {
            return _currentTool is T;
        }

        /// <summary>
        /// 現在のツールが指定した名前か判定
        /// </summary>
        public bool IsCurrentTool(string name)
        {
            return _currentTool?.Name == name;
        }

        /// <summary>
        /// 現在のツールを型キャストして取得
        /// </summary>
        public T GetCurrentToolAs<T>() where T : class, IEditTool
        {
            return _currentTool as T;
        }
    }
}
