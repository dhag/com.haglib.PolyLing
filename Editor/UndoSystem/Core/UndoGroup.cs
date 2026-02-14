// Assets/Editor/UndoSystem/Core/UndoGroup.cs
// 複数のUndoノードを束ねるグループ実装
// ConcurrentQueue対応版 - 子ノードのキュー処理を統括
// デバッグログ追加版

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// Undoノードのグループ
    /// 複数のスタックや子グループを束ねて、調停しながらUndo/Redoを実行
    /// </summary>
    public class UndoGroup : IUndoGroup
    {
        // === フィールド ===
        private readonly List<IUndoNode> _children = new();
        private string _focusedChildId;

        // === プロパティ: IUndoNode ===
        public string Id { get; }
        public string DisplayName { get; set; }
        public IUndoNode Parent { get; set; }

        public bool CanUndo => _children.Any(c => c.CanUndo) || HasPendingRecords;
        public bool CanRedo => _children.Any(c => c.CanRedo);

        public UndoOperationInfo LatestOperation
        {
            get
            {
                return _children
                    .Select(c => c.LatestOperation)
                    .Where(op => op != null)
                    .OrderByDescending(op => op.Timestamp)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// 次にRedoされる操作の情報（子ノードの中で最も古いタイムスタンプを持つもの）
        /// </summary>
        public UndoOperationInfo NextRedoOperation
        {
            get
            {
                return _children
                    .Select(c => c.NextRedoOperation)
                    .Where(op => op != null)
                    .OrderBy(op => op.Timestamp)
                    .FirstOrDefault();
            }
        }

        // === プロパティ: IUndoGroup ===
        public IReadOnlyList<IUndoNode> Children => _children;
        
        public string FocusedChildId
        {
            get => _focusedChildId;
            set
            {
                if (_focusedChildId != value)
                {
                    _focusedChildId = value;
                    OnFocusChanged?.Invoke(value);
                }
            }
        }

        public UndoResolutionPolicy ResolutionPolicy { get; set; } = UndoResolutionPolicy.FocusThenTimestamp;

        // === プロパティ: IQueueableUndoNode ===
        
        /// <summary>
        /// 子ノード全体の保留レコード数
        /// </summary>
        public int PendingCount
        {
            get
            {
                int total = 0;
                foreach (var child in _children)
                {
                    if (child is IQueueableUndoNode queueable)
                    {
                        total += queueable.PendingCount;
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// 保留中のレコードがあるか
        /// </summary>
        public bool HasPendingRecords
        {
            get
            {
                foreach (var child in _children)
                {
                    if (child is IQueueableUndoNode queueable && queueable.HasPendingRecords)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        // === イベント ===
        public event Action<UndoOperationInfo> OnUndoPerformed;
        public event Action<UndoOperationInfo> OnRedoPerformed;
        public event Action<UndoOperationInfo> OnOperationRecorded;
        public event Action<string> OnFocusChanged;
        
        /// <summary>
        /// キュー処理後に発火するイベント（処理した合計レコード数）
        /// </summary>
        public event Action<int> OnQueueProcessed;

        // === コンストラクタ ===
        public UndoGroup(string id, string displayName = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
        }

        // === 子ノード管理 ===

        /// <summary>
        /// 子ノードを追加
        /// </summary>
        public void AddChild(IUndoNode child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            if (_children.Any(c => c.Id == child.Id))
                throw new InvalidOperationException($"Child with ID '{child.Id}' already exists");

            child.Parent = this;
            _children.Add(child);

            // イベント転送
            child.OnUndoPerformed += info => OnUndoPerformed?.Invoke(info);
            child.OnRedoPerformed += info => OnRedoPerformed?.Invoke(info);
            child.OnOperationRecorded += info => OnOperationRecorded?.Invoke(info);
        }

        /// <summary>
        /// 子ノードを削除
        /// </summary>
        public bool RemoveChild(IUndoNode child)
        {
            if (child == null)
                return false;

            var removed = _children.Remove(child);
            if (removed)
            {
                child.Parent = null;
                if (_focusedChildId == child.Id)
                    _focusedChildId = null;
            }
            return removed;
        }

        /// <summary>
        /// IDで子ノードを検索（再帰）
        /// </summary>
        public IUndoNode FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (var child in _children)
            {
                if (child.Id == id)
                    return child;

                if (child is IUndoGroup group)
                {
                    var found = group.FindById(id);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        // === キュー処理 ===

        /// <summary>
        /// 全子ノードの保留キューを処理
        /// メインスレッドで定期的に呼び出す
        /// </summary>
        /// <returns>処理した合計レコード数</returns>
        public int ProcessPendingQueue()
        {
            int totalProcessed = 0;
            
            foreach (var child in _children)
            {
                if (child is IQueueableUndoNode queueable)
                {
                    totalProcessed += queueable.ProcessPendingQueue();
                }
            }
            
            if (totalProcessed > 0)
            {
                OnQueueProcessed?.Invoke(totalProcessed);
            }
            
            return totalProcessed;
        }

        // === Undo/Redo実行 ===

        /// <summary>
        /// Undo実行（調停ポリシーに基づく）
        /// </summary>
        public bool PerformUndo()
        {
            // ★重要: Undo前に全キューを処理
            ProcessPendingQueue();
            
            // Debug.Log($"[UndoGroup.PerformUndo] Group={Id}, FocusedChildId={_focusedChildId}, Policy={ResolutionPolicy}");
            
            var target = ResolveUndoTarget();
            
            // Debug.Log($"[UndoGroup.PerformUndo] Group={Id}, ResolvedTarget={target?.Id ?? "null"}");
            
            return target?.PerformUndo() ?? false;
        }

        /// <summary>
        /// Redo実行（調停ポリシーに基づく）
        /// </summary>
        public bool PerformRedo()
        {
            // ★重要: Redo前に全キューを処理
            ProcessPendingQueue();
            
            var target = ResolveRedoTarget();
            return target?.PerformRedo() ?? false;
        }

        /// <summary>
        /// 全履歴をクリア
        /// </summary>
        public void Clear()
        {
            foreach (var child in _children)
            {
                child.Clear();
            }
        }

        // === 調停ロジック ===

        /// <summary>
        /// Undo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveUndoTarget()
        {
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanUndo);

                case UndoResolutionPolicy.TimestampOnly:
                    return ResolveTimestampPriority(n => n.CanUndo);

                case UndoResolutionPolicy.FocusThenTimestamp:
                default:
                    return ResolveFocusThenTimestamp(n => n.CanUndo);
            }
        }

        /// <summary>
        /// Redo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveRedoTarget()
        {
            // Redoは基本的にUndoと同じノードで行う
            // ただしRedoスタックを持つノードを優先
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanRedo);

                case UndoResolutionPolicy.TimestampOnly:
                    return ResolveTimestampPriorityForRedo();

                case UndoResolutionPolicy.FocusThenTimestamp:
                default:
                    return ResolveFocusThenTimestamp(n => n.CanRedo);
            }
        }

        private IUndoNode ResolveFocusPriority(Func<IUndoNode, bool> canPerform)
        {
            // フォーカス中のノードが操作可能ならそれを返す
            if (!string.IsNullOrEmpty(_focusedChildId))
            {
                var focused = FindById(_focusedChildId);
                if (focused != null && canPerform(focused))
                    return focused;
            }
            return null;
        }

        private IUndoNode ResolveTimestampPriority(Func<IUndoNode, bool> canPerform)
        {
            // 最新タイムスタンプのノードを返す
            return _children
                .Where(canPerform)
                .Where(c => c.LatestOperation != null)
                .OrderByDescending(c => c.LatestOperation.Timestamp)
                .FirstOrDefault();
        }

        private IUndoNode ResolveTimestampPriorityForRedo()
        {
            // Redo: 次にRedoされる操作の中で最も古いタイムスタンプを持つノードを優先（元の実行順でRedo）
            return _children
                .Where(c => c.CanRedo)
                .Where(c => c.NextRedoOperation != null)
                .OrderBy(c => c.NextRedoOperation.Timestamp)
                .FirstOrDefault();
        }

        private IUndoNode ResolveFocusThenTimestamp(Func<IUndoNode, bool> canPerform)
        {
            // フォーカス優先、なければタイムスタンプ
            var focused = ResolveFocusPriority(canPerform);
            if (focused != null)
                return focused;

            return ResolveTimestampPriority(canPerform);
        }

        // === ユーティリティ ===

        /// <summary>
        /// 特定のスタックでUndo
        /// </summary>
        public bool PerformUndoOn(string stackId)
        {
            var node = FindById(stackId);
            return node?.PerformUndo() ?? false;
        }

        /// <summary>
        /// 特定のスタックでRedo
        /// </summary>
        public bool PerformRedoOn(string stackId)
        {
            var node = FindById(stackId);
            return node?.PerformRedo() ?? false;
        }

        /// <summary>
        /// 子ノードを型指定で取得
        /// </summary>
        public T GetChild<T>(string id) where T : class, IUndoNode
        {
            return FindById(id) as T;
        }

        // === デバッグ用 ===

        /// <summary>
        /// ツリー構造を文字列で取得
        /// </summary>
        public string GetTreeInfo(int indent = 0)
        {
            var prefix = new string(' ', indent * 2);
            var focusMark = _focusedChildId != null ? $" (Focus: {_focusedChildId})" : "";
            var pendingMark = PendingCount > 0 ? $" [Pending: {PendingCount}]" : "";
            var result = $"{prefix}[Group] {Id}{focusMark}{pendingMark}\n";

            foreach (var child in _children)
            {
                if (child is UndoGroup childGroup)
                {
                    result += childGroup.GetTreeInfo(indent + 1);
                }
                else if (child is IUndoNode node)
                {
                    var canUndo = node.CanUndo ? "U" : "-";
                    var canRedo = node.CanRedo ? "R" : "-";
                    var pending = "";
                    if (child is IQueueableUndoNode queueable && queueable.PendingCount > 0)
                    {
                        pending = $" [P:{queueable.PendingCount}]";
                    }
                    result += $"{prefix}  [{canUndo}{canRedo}] {child.Id}: {child.DisplayName}{pending}\n";
                }
            }

            return result;
        }
    }
}
