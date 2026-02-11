// Assets/Editor/UndoSystem/Core/UndoStack.cs
// 汎用Undoスタック実装
// ConcurrentQueue対応版 - 別スレッド/プロセスからの記録を受け付け可能
// デバッグログ追加版

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 保留中のレコード情報
    /// Record()時点でグループIDを確定させて保持
    /// </summary>
    internal struct PendingRecord<TContext>
    {
        public IUndoRecord<TContext> Record;
        public string Description;
        public int GroupId;
        public long EnqueueTimestamp;
    }

    /// <summary>
    /// 汎用Undoスタック
    /// 任意のコンテキスト型に対応した末端スタック
    /// ConcurrentQueue経由で別スレッドからの記録を受け付け
    /// </summary>
    public class UndoStack<TContext> : IUndoStack<TContext>
    {
        // === フィールド ===
        private readonly List<IUndoRecord<TContext>> _undoStack = new();
        private readonly List<IUndoRecord<TContext>> _redoStack = new();
        
        // ConcurrentQueue: 別スレッドからの記録を保留
        private readonly ConcurrentQueue<PendingRecord<TContext>> _pendingQueue = new();
        
        // グループID管理（スレッドセーフ）
        private int _currentGroupId = 0;
        private int _activeGroupId = -1;
        private string _activeGroupName;
        private readonly object _groupLock = new object();

        // === プロパティ: IUndoNode ===
        public string Id { get; }
        public string DisplayName { get; set; }
        public IUndoNode Parent { get; set; }
        
        public bool CanUndo => _undoStack.Count > 0 || !_pendingQueue.IsEmpty;
        public bool CanRedo => _redoStack.Count > 0;
        
        public UndoOperationInfo LatestOperation => 
            _undoStack.Count > 0 ? _undoStack[^1].Info : null;

        /// <summary>
        /// 次にRedoされる操作の情報
        /// Redoスタックは末尾から取り出すため、末尾の要素を返す
        /// </summary>
        public UndoOperationInfo NextRedoOperation => 
            _redoStack.Count > 0 ? _redoStack[^1].Info : null;

        // === プロパティ: IUndoStack ===
        public TContext Context { get; set; }
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public int MaxSize { get; set; } = 50;

        // === プロパティ: IQueueableUndoNode ===
        public int PendingCount => _pendingQueue.Count;
        public bool HasPendingRecords => !_pendingQueue.IsEmpty;

        // === イベント ===
        public event Action<UndoOperationInfo> OnUndoPerformed;
        public event Action<UndoOperationInfo> OnRedoPerformed;
        public event Action<UndoOperationInfo> OnOperationRecorded;
        
        /// <summary>
        /// キュー処理後に発火するイベント（UI更新用）
        /// </summary>
        public event Action<int> OnQueueProcessed;

        /// <summary>
        /// 最後にUndo/Redoで実行されたレコード
        /// OnUndoPerformed/OnRedoPerformed ハンドラ内で参照可能
        /// </summary>
        public IUndoRecord<TContext> LastExecutedRecord { get; private set; }

        // === コンストラクタ ===
        public UndoStack(string id, string displayName = null, TContext context = default)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            Context = context;
        }

        // === 操作記録（スレッドセーフ） ===
        
        /// <summary>
        /// 操作を記録（スレッドセーフ）
        /// 即座にスタックには積まず、ConcurrentQueueに追加
        /// メインスレッドでProcessPendingQueue()が呼ばれた時にスタックに積まれる
        /// </summary>
        public void Record(IUndoRecord<TContext> record, string description = null)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            // グループIDをRecord時点で確定（スレッドセーフ）
            int groupId;
            lock (_groupLock)
            {
                groupId = _activeGroupId >= 0 ? _activeGroupId : Interlocked.Increment(ref _currentGroupId);
            }

            //Debug.Log($"[UndoStack.Record] Stack={Id}, Desc={description}, GroupId={groupId}, ActiveGroupId={_activeGroupId}, RecordType={record.GetType().Name}");

            // キューに追加（スレッドセーフ）
            _pendingQueue.Enqueue(new PendingRecord<TContext>
            {
                Record = record,
                Description = description,
                GroupId = groupId,
                EnqueueTimestamp = DateTime.Now.Ticks
            });
        }

        /// <summary>
        /// グループを開始（複数操作を1つのUndoにまとめる）
        /// </summary>
        public int BeginGroup(string groupName = null)
        {
            lock (_groupLock)
            {
                _activeGroupId = Interlocked.Increment(ref _currentGroupId);
                _activeGroupName = groupName;
                Debug.Log($"[UndoStack.BeginGroup] Stack={Id}, GroupName={groupName}, NewGroupId={_activeGroupId}");
                return _activeGroupId;
            }
        }

        /// <summary>
        /// グループを終了
        /// </summary>
        public void EndGroup()
        {
            lock (_groupLock)
            {
                int oldGroupId = _activeGroupId;
                _activeGroupId = -1;
                _activeGroupName = null;
                Debug.Log($"[UndoStack.EndGroup] Stack={Id}, EndedGroupId={oldGroupId}");
            }
        }

        /// <summary>
        /// 指定グループIDまでの操作をまとめる
        /// </summary>
        public void CollapseToGroup(int groupId)
        {
            // まず保留キューを処理
            ProcessPendingQueue();
            
            // 指定グループID以降の操作を同じグループIDに設定
            for (int i = _undoStack.Count - 1; i >= 0; i--)
            {
                if (_undoStack[i].Info.GroupId < groupId)
                    break;
                _undoStack[i].Info.GroupId = groupId;
            }
        }

        // === キュー処理（メインスレッドで呼び出し） ===

        /// <summary>
        /// 保留キューを処理してスタックに積む
        /// メインスレッドで定期的に呼び出す（例: EditorApplication.update）
        /// </summary>
        /// <returns>処理したレコード数</returns>
        public int ProcessPendingQueue()
        {
            int processed = 0;
            
            while (_pendingQueue.TryDequeue(out var pending))
            {
                ProcessRecord(pending);
                processed++;
            }
            
            if (processed > 0)
            {
                OnQueueProcessed?.Invoke(processed);
            }
            
            return processed;
        }

        /// <summary>
        /// 内部処理: レコードをスタックに積む
        /// </summary>
        private void ProcessRecord(PendingRecord<TContext> pending)
        {
            var record = pending.Record;
            
            // メタ情報を設定
            record.Info = new UndoOperationInfo(
                pending.Description ?? "Operation",
                Id,
                pending.GroupId
            );

            _undoStack.Add(record);
            _redoStack.Clear();

            // サイズ制限
            EnforceMaxSize();

            OnOperationRecorded?.Invoke(record.Info);
        }

        // === Undo/Redo実行 ===

        /// <summary>
        /// Undo実行
        /// </summary>
        public bool PerformUndo()
        {
            // ★重要: Undo前に保留キューを処理
            // これにより、Record()直後のUndo要求に正しく対応できる
            ProcessPendingQueue();
            
            if (_undoStack.Count == 0 || Context == null)
            {
                Debug.Log($"[UndoStack.PerformUndo] Stack={Id}, Cannot undo: Count={_undoStack.Count}, HasContext={Context != null}");
                return false;
            }

            // 同じグループIDの操作をまとめてUndo
            var lastGroupId = _undoStack[^1].Info.GroupId;
            var undoneRecords = new List<IUndoRecord<TContext>>();

            Debug.Log($"[UndoStack.PerformUndo] Stack={Id}, Starting undo. StackCount={_undoStack.Count}, TargetGroupId={lastGroupId}");

            while (_undoStack.Count > 0 && _undoStack[^1].Info.GroupId == lastGroupId)
            {
                var record = _undoStack[^1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                
                Debug.Log($"[UndoStack.PerformUndo] Stack={Id}, Undoing: {record.GetType().Name}, GroupId={record.Info.GroupId}, Desc={record.Info.Description}");
                
                record.Undo(Context);
                undoneRecords.Add(record);
            }

            // Redoスタックに逆順で追加（Redo時は元の順序で実行）
            for (int i = undoneRecords.Count - 1; i >= 0; i--)
            {
                _redoStack.Add(undoneRecords[i]);
            }

            if (undoneRecords.Count > 0)
            {
                Debug.Log($"[UndoStack.PerformUndo] Stack={Id}, Completed. UndoneCount={undoneRecords.Count}, RemainingStack={_undoStack.Count}");
                LastExecutedRecord = undoneRecords[0];
                OnUndoPerformed?.Invoke(undoneRecords[0].Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Redo実行
        /// </summary>
        public bool PerformRedo()
        {
            // ★重要: Redo前にも保留キューを処理
            ProcessPendingQueue();
            
            if (_redoStack.Count == 0 || Context == null)
                return false;

            // 同じグループIDの操作をまとめてRedo
            var lastGroupId = _redoStack[^1].Info.GroupId;
            var redoneRecords = new List<IUndoRecord<TContext>>();

            while (_redoStack.Count > 0 && _redoStack[^1].Info.GroupId == lastGroupId)
            {
                var record = _redoStack[^1];
                _redoStack.RemoveAt(_redoStack.Count - 1);
                
                record.Redo(Context);
                redoneRecords.Add(record);
            }

            // Undoスタックに逆順で追加
            for (int i = redoneRecords.Count - 1; i >= 0; i--)
            {
                _undoStack.Add(redoneRecords[i]);
            }

            if (redoneRecords.Count > 0)
            {
                LastExecutedRecord = redoneRecords[0];
                OnRedoPerformed?.Invoke(redoneRecords[0].Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 履歴をクリア
        /// </summary>
        public void Clear()
        {
            // 保留キューもクリア
            while (_pendingQueue.TryDequeue(out _)) { }
            
            _undoStack.Clear();
            _redoStack.Clear();
            
            lock (_groupLock)
            {
                _activeGroupId = -1;
            }
        }

        // === 内部メソッド ===

        private void EnforceMaxSize()
        {
            while (_undoStack.Count > MaxSize)
            {
                _undoStack.RemoveAt(0);
            }
        }

        // === デバッグ用 ===

        /// <summary>
        /// スタックの状態を文字列で取得
        /// </summary>
        public string GetDebugInfo()
        {
            return $"[{Id}] Undo: {_undoStack.Count}, Redo: {_redoStack.Count}, " +
                   $"Pending: {_pendingQueue.Count}, Group: {_activeGroupId}, " +
                   $"Latest: {LatestOperation?.Description ?? "none"}";
        }
    }
}
