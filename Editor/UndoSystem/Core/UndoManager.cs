// Assets/Editor/UndoSystem/Core/UndoManager.cs
// グローバルUndoマネージャー（ルートノード + ショートカット処理）
// ConcurrentQueue対応版 - 全スタックのキュー処理を統括

using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// グローバルUndoマネージャー
    /// アプリケーション全体のUndoツリーのルートとして機能
    /// ConcurrentQueue経由で別スレッド/プロセスからの記録を受け付け
    /// </summary>
    public class UndoManager : IUndoGroup
    {
        // === シングルトン ===
        private static UndoManager _instance;
        public static UndoManager Instance => _instance ??= new UndoManager();

        // === 内部グループ ===
        private readonly UndoGroup _root;

        // === プロパティ: IUndoNode ===
        public string Id => _root.Id;
        public string DisplayName { get => _root.DisplayName; set => _root.DisplayName = value; }
        public IUndoNode Parent { get => null; set { } }  // ルートなので常にnull
        public bool CanUndo => _root.CanUndo;
        public bool CanRedo => _root.CanRedo;
        public UndoOperationInfo LatestOperation => _root.LatestOperation;
        public UndoOperationInfo NextRedoOperation => _root.NextRedoOperation;

        // === プロパティ: IUndoGroup ===
        public IReadOnlyList<IUndoNode> Children => _root.Children;
        public string FocusedChildId 
        { 
            get => _root.FocusedChildId; 
            set => _root.FocusedChildId = value; 
        }
        public UndoResolutionPolicy ResolutionPolicy 
        { 
            get => _root.ResolutionPolicy; 
            set => _root.ResolutionPolicy = value; 
        }

        // === プロパティ: IQueueableUndoNode ===
        public int PendingCount => _root.PendingCount;
        public bool HasPendingRecords => _root.HasPendingRecords;

        // === イベント ===
        public event Action<UndoOperationInfo> OnUndoPerformed
        {
            add => _root.OnUndoPerformed += value;
            remove => _root.OnUndoPerformed -= value;
        }
        public event Action<UndoOperationInfo> OnRedoPerformed
        {
            add => _root.OnRedoPerformed += value;
            remove => _root.OnRedoPerformed -= value;
        }
        public event Action<UndoOperationInfo> OnOperationRecorded
        {
            add => _root.OnOperationRecorded += value;
            remove => _root.OnOperationRecorded -= value;
        }
        
        /// <summary>
        /// キュー処理後に発火するイベント（処理した合計レコード数）
        /// </summary>
        public event Action<int> OnQueueProcessed
        {
            add => _root.OnQueueProcessed += value;
            remove => _root.OnQueueProcessed -= value;
        }

        // === コンストラクタ ===
        private UndoManager()
        {
            _root = new UndoGroup("Root", "Undo Manager");
            _root.ResolutionPolicy = UndoResolutionPolicy.FocusThenTimestamp;
        }

        /// <summary>
        /// インスタンスをリセット（テスト用）
        /// </summary>
        public static void ResetInstance()
        {
            _instance = null;
        }

        // === IUndoGroup 実装 ===

        public void AddChild(IUndoNode child) => _root.AddChild(child);
        public bool RemoveChild(IUndoNode child) => _root.RemoveChild(child);
        public IUndoNode FindById(string id) => _root.FindById(id);
        public bool PerformUndo() => _root.PerformUndo();
        public bool PerformRedo() => _root.PerformRedo();
        public void Clear() => _root.Clear();

        // === キュー処理 ===

        /// <summary>
        /// 全スタックの保留キューを処理
        /// メインスレッドで定期的に呼び出す
        /// 推奨: EditorApplication.update に登録
        /// </summary>
        /// <returns>処理した合計レコード数</returns>
        public int ProcessPendingQueue() => _root.ProcessPendingQueue();

        /// <summary>
        /// 全スタックの保留キューを処理（エイリアス）
        /// </summary>
        public int ProcessAllQueues() => ProcessPendingQueue();

        // === 便利メソッド ===

        /// <summary>
        /// 新しいスタックを作成してルートに追加
        /// </summary>
        public UndoStack<TContext> CreateStack<TContext>(
            string id, 
            string displayName = null, 
            TContext context = default)
        {
            var stack = new UndoStack<TContext>(id, displayName, context);
            _root.AddChild(stack);
            return stack;
        }

        /// <summary>
        /// 新しいグループを作成してルートに追加
        /// </summary>
        public UndoGroup CreateGroup(string id, string displayName = null)
        {
            var group = new UndoGroup(id, displayName);
            _root.AddChild(group);
            return group;
        }

        /// <summary>
        /// 特定のスタックを取得
        /// </summary>
        public UndoStack<TContext> GetStack<TContext>(string id)
        {
            return _root.FindById(id) as UndoStack<TContext>;
        }

        /// <summary>
        /// 特定のグループを取得
        /// </summary>
        public UndoGroup GetGroup(string id)
        {
            return _root.FindById(id) as UndoGroup;
        }

        /// <summary>
        /// フォーカスを設定（パス指定）
        /// 例: "MainWindow/VertexEdit" → MainWindowグループのVertexEditスタックにフォーカス
        /// </summary>
        public void SetFocus(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                _root.FocusedChildId = null;
                return;
            }

            var parts = path.Split('/');
            IUndoGroup currentGroup = _root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var child = currentGroup.FindById(part);

                if (child == null)
                {
                    Debug.LogWarning($"[UndoManager] Node not found: {part} in path {path}");
                    return;
                }

                currentGroup.FocusedChildId = part;

                if (i < parts.Length - 1)
                {
                    if (child is IUndoGroup childGroup)
                    {
                        currentGroup = childGroup;
                    }
                    else
                    {
                        Debug.LogWarning($"[UndoManager] {part} is not a group, cannot traverse further");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// キーボードショートカット処理
        /// OnGUIで呼び出す
        /// </summary>
        public bool HandleKeyboardShortcuts(Event e)
        {
            if (e.type != EventType.KeyDown)
                return false;

            bool ctrl = e.control || e.command;

            // Ctrl+Z: Undo
            if (ctrl && e.keyCode == KeyCode.Z && !e.shift)
            {
                if (PerformUndo())
                {
                    e.Use();
                    return true;
                }
            }

            // Ctrl+Y または Ctrl+Shift+Z: Redo
            if ((ctrl && e.keyCode == KeyCode.Y) || 
                (ctrl && e.shift && e.keyCode == KeyCode.Z))
            {
                if (PerformRedo())
                {
                    e.Use();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 特定スタックでUndo
        /// </summary>
        public bool UndoOn(string stackId)
        {
            var node = _root.FindById(stackId);
            return node?.PerformUndo() ?? false;
        }

        /// <summary>
        /// 特定スタックでRedo
        /// </summary>
        public bool RedoOn(string stackId)
        {
            var node = _root.FindById(stackId);
            return node?.PerformRedo() ?? false;
        }

        // === デバッグ ===

        /// <summary>
        /// ツリー構造をログ出力
        /// </summary>
        public void LogTree()
        {
            Debug.Log("[UndoManager] Tree Structure:\n" + _root.GetTreeInfo());
        }

        /// <summary>
        /// ツリー構造を文字列で取得
        /// </summary>
        public string GetTreeInfo()
        {
            return _root.GetTreeInfo();
        }

        /// <summary>
        /// キュー状態をログ出力
        /// </summary>
        public void LogQueueStatus()
        {
            Debug.Log($"[UndoManager] Pending Records: {PendingCount}, HasPending: {HasPendingRecords}");
        }
    }
}
