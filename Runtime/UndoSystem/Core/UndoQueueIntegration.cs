// Assets/Editor/UndoSystem/Examples/UndoQueueIntegration.cs
// ConcurrentQueue対応Undoシステムの統合サンプル
// EditorWindowでの使用例

using UnityEditor;
using UnityEngine;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem.Examples
{
    /// <summary>
    /// Undoキュー処理の統合例
    /// EditorWindowで使用する場合の典型的なパターン
    /// </summary>
    public class UndoQueueIntegrationExample : EditorWindow
    {
        // === Undoシステムとの統合 ===
        
        private void OnEnable()
        {
            // 定期的なキュー処理を登録
            EditorApplication.update += ProcessUndoQueues;
            
            // オプション: キュー処理イベントを購読
            UndoManager.Instance.OnQueueProcessed += OnUndoQueueProcessed;
        }

        private void OnDisable()
        {
            // クリーンアップ
            EditorApplication.update -= ProcessUndoQueues;
            UndoManager.Instance.OnQueueProcessed -= OnUndoQueueProcessed;
        }

        /// <summary>
        /// 定期的なキュー処理
        /// EditorApplication.update から呼び出される
        /// </summary>
        private void ProcessUndoQueues()
        {
            int processed = UndoManager.Instance.ProcessAllQueues();
            
            if (processed > 0)
            {
                // キューが処理されたらUIを更新
                Repaint();
            }
        }

        /// <summary>
        /// キュー処理完了時のコールバック
        /// </summary>
        private void OnUndoQueueProcessed(int processedCount)
        {
            // デバッグログ（本番では削除）
            // Debug.Log($"[UndoQueue] Processed {processedCount} records");
        }

        // === 別スレッドからの記録例 ===

        /// <summary>
        /// 別スレッド/プロセスからUndo記録を追加する例
        /// </summary>
        private void RecordFromAnotherThread()
        {
            // スタックを取得
            var stack = UndoManager.Instance.GetStack<MyContext>("MyStack");
            if (stack == null) return;

            // Record()はスレッドセーフ
            // ConcurrentQueueに追加され、後でメインスレッドで処理される
            stack.Record(new MyUndoRecord
            {
                OldValue = 10,
                NewValue = 20
            }, "Change Value");
        }

        // === サンプルコンテキストとレコード ===

        private class MyContext
        {
            public int Value;
        }

        private class MyUndoRecord : IUndoRecord<MyContext>
        {
            public UndoOperationInfo Info { get; set; }
            public int OldValue;
            public int NewValue;

            public void Undo(MyContext ctx) => ctx.Value = OldValue;
            public void Redo(MyContext ctx) => ctx.Value = NewValue;
        }
    }

    // ========================================================================
    // 別プロセス/ワーカーからの統合パターン
    // ========================================================================

    /// <summary>
    /// 分散ワーカーからのUndo記録を受け付ける統合クラス
    /// </summary>
    public static class DistributedUndoReceiver
    {
        /// <summary>
        /// 外部ソースからUndo記録を受け付け
        /// 例: ネットワーク経由、別プロセスからのIPC
        /// </summary>
        /// <typeparam name="TContext">コンテキスト型</typeparam>
        /// <param name="stackId">スタックID</param>
        /// <param name="record">記録するレコード</param>
        /// <param name="description">説明</param>
        public static void ReceiveRecord<TContext>(
            string stackId, 
            IUndoRecord<TContext> record, 
            string description = null)
        {
            var stack = UndoManager.Instance.GetStack<TContext>(stackId);
            if (stack == null)
            {
                Debug.LogWarning($"[DistributedUndoReceiver] Stack not found: {stackId}");
                return;
            }

            // スレッドセーフにキューに追加
            stack.Record(record, description);
        }

        /// <summary>
        /// 複数レコードを一括受け付け
        /// </summary>
        public static void ReceiveRecords<TContext>(
            string stackId,
            (IUndoRecord<TContext> Record, string Description)[] records)
        {
            var stack = UndoManager.Instance.GetStack<TContext>(stackId);
            if (stack == null)
            {
                Debug.LogWarning($"[DistributedUndoReceiver] Stack not found: {stackId}");
                return;
            }

            // グループとして記録
            int groupId = stack.BeginGroup("Batch Operation");
            try
            {
                foreach (var (record, description) in records)
                {
                    stack.Record(record, description);
                }
            }
            finally
            {
                stack.EndGroup();
            }
        }
    }

    // ========================================================================
    // MeshUndoController への統合例（既存システムとの連携）
    // ========================================================================

    /// <summary>
    /// MeshUndoControllerとの統合ヘルパー
    /// SimpleMeshFactoryのOnEnable/OnDisableで使用
    /// </summary>
    public static class MeshUndoQueueHelper
    {
        private static bool _isRegistered = false;

        /// <summary>
        /// EditorWindowのOnEnableで呼び出し
        /// </summary>
        public static void Register()
        {
            if (_isRegistered) return;
            
            EditorApplication.update += ProcessQueues;
            _isRegistered = true;
        }

        /// <summary>
        /// EditorWindowのOnDisableで呼び出し
        /// </summary>
        public static void Unregister()
        {
            if (!_isRegistered) return;
            
            EditorApplication.update -= ProcessQueues;
            _isRegistered = false;
        }

        private static void ProcessQueues()
        {
            UndoManager.Instance.ProcessAllQueues();
        }

        /// <summary>
        /// 保留レコードがあるかチェック
        /// </summary>
        public static bool HasPendingRecords => UndoManager.Instance.HasPendingRecords;

        /// <summary>
        /// 保留レコード数
        /// </summary>
        public static int PendingCount => UndoManager.Instance.PendingCount;
    }
}
