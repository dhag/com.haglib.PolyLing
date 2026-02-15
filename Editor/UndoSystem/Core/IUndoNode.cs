// Assets/Editor/UndoSystem/Core/IUndoNode.cs
// 階層型Undoシステムのコアインターフェース
// ConcurrentQueue対応版

using System;
using System.Collections.Generic;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// Undo操作のメタ情報
    /// タイムスタンプベース + フォーカス併用の調停に使用
    /// </summary>
    public class UndoOperationInfo
    {
        /// <summary>操作のユニークID</summary>
        public long OperationId { get; }
        
        /// <summary>操作が記録された時刻（ティック）</summary>
        public long Timestamp { get; }
        
        /// <summary>操作の説明（"Move Vertices", "Delete Face"等）</summary>
        public string Description { get; }
        
        /// <summary>この操作が属するスタックのID</summary>
        public string StackId { get; }
        
        /// <summary>グループID（複数操作を1つのUndoにまとめる場合）</summary>
        public int GroupId { get; set; }

        private static long _nextOperationId = 0;
        private static readonly object _idLock = new object();

        public UndoOperationInfo(string description, string stackId, int groupId = -1)
        {
            lock (_idLock)
            {
                OperationId = _nextOperationId++;
            }
            Timestamp = DateTime.Now.Ticks;
            Description = description;
            StackId = stackId;
            GroupId = groupId;
        }
    }

    /// <summary>
    /// 汎用Undo記録インターフェース
    /// 任意のコンテキスト型に対応
    /// </summary>
    public interface IUndoRecord<TContext>
    {
        /// <summary>操作のメタ情報</summary>
        UndoOperationInfo Info { get; set; }
        
        /// <summary>Undo実行</summary>
        void Undo(TContext context);
        
        /// <summary>Redo実行</summary>
        void Redo(TContext context);
    }

    /// <summary>
    /// Undoツリーのノード共通インターフェース
    /// 末端スタックと中間グループの両方がこれを実装
    /// </summary>
    public interface IUndoNode
    {
        /// <summary>ノードの一意識別子</summary>
        string Id { get; }
        
        /// <summary>表示名</summary>
        string DisplayName { get; }
        
        /// <summary>親ノード（ルートの場合はnull）</summary>
        IUndoNode Parent { get; set; }
        
        /// <summary>このノードまたは子孫にUndo可能な操作があるか</summary>
        bool CanUndo { get; }
        
        /// <summary>このノードまたは子孫にRedo可能な操作があるか</summary>
        bool CanRedo { get; }
        
        /// <summary>最新の操作情報（Undo調停用）</summary>
        UndoOperationInfo LatestOperation { get; }
        
        /// <summary>
        /// 次にRedoされる操作の情報
        /// Redoスタックは末尾から取り出すため、末尾の要素を返す
        /// </summary>
        UndoOperationInfo NextRedoOperation { get; }
        
        /// <summary>このスコープ内でUndo（子を含む）</summary>
        bool PerformUndo();
        
        /// <summary>このスコープ内でRedo（子を含む）</summary>
        bool PerformRedo();
        
        /// <summary>全履歴をクリア</summary>
        void Clear();
        
        /// <summary>Undo/Redo実行時のイベント</summary>
        event Action<UndoOperationInfo> OnUndoPerformed;
        event Action<UndoOperationInfo> OnRedoPerformed;
        event Action<UndoOperationInfo> OnOperationRecorded;
    }

    /// <summary>
    /// キュー処理対応のUndoノードインターフェース
    /// ConcurrentQueue経由で別スレッドから記録を受け付ける
    /// </summary>
    public interface IQueueableUndoNode : IUndoNode
    {
        /// <summary>保留中のレコード数</summary>
        int PendingCount { get; }
        
        /// <summary>
        /// 保留キューを処理してスタックに積む
        /// メインスレッドで定期的に呼び出す
        /// </summary>
        /// <returns>処理したレコード数</returns>
        int ProcessPendingQueue();
        
        /// <summary>保留中のレコードがあるか</summary>
        bool HasPendingRecords { get; }
    }

    /// <summary>
    /// 末端のUndoスタックインターフェース（型付き）
    /// 特定のコンテキスト型に対してUndo/Redoを実行
    /// </summary>
    public interface IUndoStack<TContext> : IUndoNode, IQueueableUndoNode
    {
        /// <summary>操作対象のコンテキスト</summary>
        TContext Context { get; set; }
        
        /// <summary>Undo可能な操作数</summary>
        int UndoCount { get; }
        
        /// <summary>Redo可能な操作数</summary>
        int RedoCount { get; }
        
        /// <summary>最大履歴サイズ</summary>
        int MaxSize { get; set; }
        
        /// <summary>操作を記録（スレッドセーフ、キューに追加）</summary>
        void Record(IUndoRecord<TContext> record, string description = null);
        
        /// <summary>現在のグループを開始（複数操作を1つにまとめる）</summary>
        int BeginGroup(string groupName = null);
        
        /// <summary>グループを終了</summary>
        void EndGroup();
        
        /// <summary>指定グループIDまでの操作をまとめる</summary>
        void CollapseToGroup(int groupId);
    }

    /// <summary>
    /// 複数のUndoノードを束ねるグループインターフェース
    /// </summary>
    public interface IUndoGroup : IUndoNode, IQueueableUndoNode
    {
        /// <summary>子ノード一覧</summary>
        IReadOnlyList<IUndoNode> Children { get; }
        
        /// <summary>子ノードを追加</summary>
        void AddChild(IUndoNode child);
        
        /// <summary>子ノードを削除</summary>
        bool RemoveChild(IUndoNode child);
        
        /// <summary>IDで子ノードを検索（再帰）</summary>
        IUndoNode FindById(string id);
        
        /// <summary>現在フォーカスされている子ノードのID</summary>
        string FocusedChildId { get; set; }
        
        /// <summary>子のUndoを調停するポリシー</summary>
        UndoResolutionPolicy ResolutionPolicy { get; set; }
    }

    /// <summary>
    /// グローバルUndo時の調停ポリシー
    /// 
    /// ★★★ 禁忌事項 ★★★
    /// TimestampOnly / FocusThenTimestamp は使用禁止。
    /// DateTime.Now.Ticks による順序管理は、同一フレーム内で同値になり得るため
    /// Undo/Redo順序が不定になる。マルチスタック環境では致命的な欠陥となる。
    /// 必ず OperationLog を使用すること。
    /// </summary>
    public enum UndoResolutionPolicy
    {
        /// <summary>フォーカス中のノードを優先</summary>
        FocusPriority,
        
        /// <summary>
        /// 【使用禁止】最新タイムスタンプの操作を優先。
        /// DateTime.Now.Ticks は同一フレーム内で同値になり得るため順序不定。
        /// OperationLog を使用すること。
        /// </summary>
        [Obsolete("TimestampOnly は順序不定バグの原因。OperationLog を使用せよ。", error: false)]
        TimestampOnly,
        
        /// <summary>
        /// 【使用禁止】フォーカス優先、空ならタイムスタンプ。
        /// タイムスタンプへのフォールバックが順序不定を引き起こす。
        /// OperationLog を使用すること。
        /// </summary>
        [Obsolete("FocusThenTimestamp は順序不定バグの原因。OperationLog を使用せよ。", error: false)]
        FocusThenTimestamp,
        
        /// <summary>
        /// オペレーションログ方式（推奨・唯一の正解）。
        /// Record()された順序をグローバルログで管理し、
        /// Undo/Redoは常にログの末尾/先頭から順に実行する。
        /// タイムスタンプ不使用。記録順がそのままUndo/Redo順序。
        /// </summary>
        OperationLog
    }
}
