// Assets/Editor/UndoSystem/Core/UndoGroup.cs
// 複数のUndoノードを束ねるグループ実装
// ConcurrentQueue対応版
//
// ★★★ 禁忌事項（絶対厳守） ★★★
// タイムスタンプ（DateTime.Now.Ticks）による順序管理は禁止。
// 理由: 同一フレーム内で複数スタックにRecord()した場合、
//        Ticks値が同一または微小差となり、Undo/Redo順序が不定になる。
//        これはマルチスタック環境において致命的な欠陥であり、
//        頂点移動・ボーン回転・マテリアル変更等すべてのUndoを破壊する。
//
// 正解: OperationLog方式。
//        Record()された順序をグローバルログ（List）で管理し、
//        Undo/Redoは常にログの末尾/先頭から順に実行する。
//        分散開発におけるオペレーションログと同じ手法。
// ★★★★★★★★★★★★★★★★★★★★

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// オペレーションログのエントリ
    /// どのスタックのどのグループが記録されたかを保持
    /// </summary>
    public struct OperationLogEntry
    {
        /// <summary>記録先スタックのID</summary>
        public string StackId;
        
        /// <summary>操作のグループID（同一グループは1回のUndoでまとめて戻す）</summary>
        public int GroupId;
        
        public OperationLogEntry(string stackId, int groupId)
        {
            StackId = stackId;
            GroupId = groupId;
        }
    }

    /// <summary>
    /// Undoノードのグループ
    /// 複数のスタックや子グループを束ねて、調停しながらUndo/Redoを実行
    /// </summary>
    public class UndoGroup : IUndoGroup
    {
        // === フィールド ===
        private readonly List<IUndoNode> _children = new();
        private string _focusedChildId;

        // === オペレーションログ ===
        // Record()された順序を記録するグローバルログ。
        // タイムスタンプを使わず、記録順がそのままUndo/Redo順序となる。
        private readonly List<OperationLogEntry> _undoLog = new();
        private readonly List<OperationLogEntry> _redoLog = new();

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
                // OperationLog方式: ログ末尾のスタックから取得
                if (ResolutionPolicy == UndoResolutionPolicy.OperationLog && _undoLog.Count > 0)
                {
                    var lastEntry = _undoLog[^1];
                    var node = FindById(lastEntry.StackId);
                    return node?.LatestOperation;
                }
                
                // レガシー: タイムスタンプ順（非推奨）
                return _children
                    .Select(c => c.LatestOperation)
                    .Where(op => op != null)
                    .OrderByDescending(op => op.Timestamp)
                    .FirstOrDefault();
            }
        }

        /// <summary>
        /// 次にRedoされる操作の情報
        /// </summary>
        public UndoOperationInfo NextRedoOperation
        {
            get
            {
                // OperationLog方式: Redoログ末尾のスタックから取得
                if (ResolutionPolicy == UndoResolutionPolicy.OperationLog && _redoLog.Count > 0)
                {
                    var lastEntry = _redoLog[^1];
                    var node = FindById(lastEntry.StackId);
                    return node?.NextRedoOperation;
                }
                
                // レガシー: タイムスタンプ順（非推奨）
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

        public UndoResolutionPolicy ResolutionPolicy { get; set; } = UndoResolutionPolicy.OperationLog;

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

        // === オペレーションログ参照（デバッグ・外部参照用） ===
        public IReadOnlyList<OperationLogEntry> UndoLog => _undoLog;
        public IReadOnlyList<OperationLogEntry> RedoLog => _redoLog;

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
            
            // OnOperationRecorded: ログに追記してからイベント転送
            child.OnOperationRecorded += info =>
            {
                AppendToUndoLog(info);
                OnOperationRecorded?.Invoke(info);
            };
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
                
                // 削除されたスタックのログエントリを除去
                _undoLog.RemoveAll(e => e.StackId == child.Id);
                _redoLog.RemoveAll(e => e.StackId == child.Id);
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

        // === オペレーションログ管理 ===

        /// <summary>
        /// 操作記録をUndoログに追記
        /// 同一(StackId, GroupId)の連続エントリは1つに集約する
        /// </summary>
        private void AppendToUndoLog(UndoOperationInfo info)
        {
            if (ResolutionPolicy != UndoResolutionPolicy.OperationLog)
                return;

            var entry = new OperationLogEntry(info.StackId, info.GroupId);
            
            // 同一(StackId, GroupId)の連続は集約
            if (_undoLog.Count > 0)
            {
                var last = _undoLog[^1];
                if (last.StackId == entry.StackId && last.GroupId == entry.GroupId)
                    return; // 既に記録済み
            }
            
            _undoLog.Add(entry);
            
            // 新規記録時はRedoログをクリア
            // （各スタック内のRedoも Record→ProcessRecord→_redoStack.Clear で消えている）
            _redoLog.Clear();
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
            
            var target = ResolveUndoTarget();
            if (target == null)
                return false;

            // OperationLog方式: Undo前にログエントリをRedoログに移動
            if (ResolutionPolicy == UndoResolutionPolicy.OperationLog && _undoLog.Count > 0)
            {
                var entry = _undoLog[^1];
                _undoLog.RemoveAt(_undoLog.Count - 1);
                _redoLog.Add(entry);
            }

            return target.PerformUndo();
        }

        /// <summary>
        /// Redo実行（調停ポリシーに基づく）
        /// </summary>
        public bool PerformRedo()
        {
            // ★重要: Redo前に全キューを処理
            ProcessPendingQueue();
            
            var target = ResolveRedoTarget();
            if (target == null)
                return false;
            
            // OperationLog方式: Redo前にログエントリをUndoログに戻す
            if (ResolutionPolicy == UndoResolutionPolicy.OperationLog && _redoLog.Count > 0)
            {
                var entry = _redoLog[^1];
                _redoLog.RemoveAt(_redoLog.Count - 1);
                _undoLog.Add(entry);
            }

            return target.PerformRedo();
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
            
            _undoLog.Clear();
            _redoLog.Clear();
        }

        // === 調停ロジック ===

        /// <summary>
        /// Undo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveUndoTarget()
        {
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.OperationLog:
                    return ResolveFromOperationLog(isUndo: true);

                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanUndo);

#pragma warning disable CS0618 // Obsolete warning suppressed for legacy fallback
                case UndoResolutionPolicy.TimestampOnly:
                    Debug.LogError("[UndoGroup] TimestampOnly は使用禁止。OperationLog を使用せよ。");
                    return ResolveTimestampPriority(n => n.CanUndo);

                case UndoResolutionPolicy.FocusThenTimestamp:
                    Debug.LogError("[UndoGroup] FocusThenTimestamp は使用禁止。OperationLog を使用せよ。");
                    return ResolveFocusThenTimestamp(n => n.CanUndo);
#pragma warning restore CS0618

                default:
                    return null;
            }
        }

        /// <summary>
        /// Redo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveRedoTarget()
        {
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.OperationLog:
                    return ResolveFromOperationLog(isUndo: false);

                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanRedo);

#pragma warning disable CS0618
                case UndoResolutionPolicy.TimestampOnly:
                    Debug.LogError("[UndoGroup] TimestampOnly は使用禁止。OperationLog を使用せよ。");
                    return ResolveTimestampPriorityForRedo();

                case UndoResolutionPolicy.FocusThenTimestamp:
                    Debug.LogError("[UndoGroup] FocusThenTimestamp は使用禁止。OperationLog を使用せよ。");
                    return ResolveFocusThenTimestamp(n => n.CanRedo);
#pragma warning restore CS0618

                default:
                    return null;
            }
        }

        /// <summary>
        /// OperationLog方式: ログの末尾から対象スタックを特定
        /// </summary>
        private IUndoNode ResolveFromOperationLog(bool isUndo)
        {
            var log = isUndo ? _undoLog : _redoLog;
            
            if (log.Count == 0)
                return null;
            
            var entry = log[^1];
            var node = FindById(entry.StackId);
            
            if (node == null)
            {
                // スタックが既に削除されている場合、エントリを除去して再試行
                log.RemoveAt(log.Count - 1);
                return ResolveFromOperationLog(isUndo);
            }
            
            // 対象スタックが操作可能か確認
            bool canPerform = isUndo ? node.CanUndo : node.CanRedo;
            if (!canPerform)
            {
                // スタックは存在するがUndo/Redo不可（Clear等で空になった場合）
                // エントリを除去して再試行
                log.RemoveAt(log.Count - 1);
                return ResolveFromOperationLog(isUndo);
            }
            
            return node;
        }

        private IUndoNode ResolveFocusPriority(Func<IUndoNode, bool> canPerform)
        {
            if (!string.IsNullOrEmpty(_focusedChildId))
            {
                var focused = FindById(_focusedChildId);
                if (focused != null && canPerform(focused))
                    return focused;
            }
            return null;
        }

        // === レガシー解決（非推奨・後方互換用） ===

        private IUndoNode ResolveTimestampPriority(Func<IUndoNode, bool> canPerform)
        {
            return _children
                .Where(canPerform)
                .Where(c => c.LatestOperation != null)
                .OrderByDescending(c => c.LatestOperation.Timestamp)
                .FirstOrDefault();
        }

        private IUndoNode ResolveTimestampPriorityForRedo()
        {
            return _children
                .Where(c => c.CanRedo)
                .Where(c => c.NextRedoOperation != null)
                .OrderBy(c => c.NextRedoOperation.Timestamp)
                .FirstOrDefault();
        }

        private IUndoNode ResolveFocusThenTimestamp(Func<IUndoNode, bool> canPerform)
        {
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
            var logMark = $" [UndoLog: {_undoLog.Count}, RedoLog: {_redoLog.Count}]";
            var result = $"{prefix}[Group] {Id} Policy={ResolutionPolicy}{focusMark}{pendingMark}{logMark}\n";

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

        /// <summary>
        /// オペレーションログを文字列で取得（デバッグ用）
        /// </summary>
        public string GetOperationLogInfo()
        {
            var result = $"[OperationLog] UndoLog({_undoLog.Count}):\n";
            for (int i = 0; i < _undoLog.Count; i++)
            {
                var e = _undoLog[i];
                result += $"  [{i}] Stack={e.StackId}, Group={e.GroupId}\n";
            }
            result += $"RedoLog({_redoLog.Count}):\n";
            for (int i = 0; i < _redoLog.Count; i++)
            {
                var e = _redoLog[i];
                result += $"  [{i}] Stack={e.StackId}, Group={e.GroupId}\n";
            }
            return result;
        }
    }
}
