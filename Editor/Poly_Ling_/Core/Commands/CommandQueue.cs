// Assets/Editor/Poly_Ling_/Core/Commands/CommandQueue.cs
// 全操作をキュー化して順序を保証するシステム

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// コマンドインターフェース
    /// 全ての操作はこのインターフェースを実装する
    /// </summary>
    public interface ICommand
    {
        /// <summary>コマンドの説明（デバッグ用）</summary>
        string Description { get; }
        
        /// <summary>
        /// この操作のUndo/Redo後に必要な更新レベル
        /// </summary>
        MeshUpdateLevel UpdateLevel { get; }
        
        /// <summary>コマンドを実行</summary>
        void Execute();
    }

    /// <summary>
    /// コマンドキュー
    /// 全操作を順次処理し、競合を防ぐ
    /// </summary>
    public class CommandQueue
    {
        private readonly Queue<ICommand> _queue = new Queue<ICommand>();
        private bool _isProcessing = false;
        
        /// <summary>キュー内のコマンド数</summary>
        public int Count => _queue.Count;
        
        /// <summary>処理中かどうか</summary>
        public bool IsProcessing => _isProcessing;

        /// <summary>デバッグログを出力するか</summary>
        public bool EnableDebugLog { get; set; } = false;

        /// <summary>
        /// コマンドをキューに追加
        /// </summary>
        public void Enqueue(ICommand command)
        {
            if (command == null)
            {
                Debug.LogWarning("[CommandQueue] null command ignored");
                return;
            }
            
            _queue.Enqueue(command);
            
            // 常にログ出力（デバッグ用）
            Debug.Log($"[CommandQueue.Enqueue] {command.Description} (queue size: {_queue.Count})\n{UnityEngine.StackTraceUtility.ExtractStackTrace()}");
        }

        /// <summary>
        /// キュー内の全コマンドを順次処理
        /// EditorApplication.update から呼び出される
        /// </summary>
        public void ProcessAll()
        {
            if (_isProcessing)
            {
                // 再入防止
                return;
            }

            if (_queue.Count == 0)
            {
                return;
            }

            _isProcessing = true;
            
            try
            {
                while (_queue.Count > 0)
                {
                    ICommand command = _queue.Dequeue();
                    
                    if (EnableDebugLog)
                    {
                        Debug.Log($"[CommandQueue] Executing: {command.Description}");
                    }
                    
                    try
                    {
                        command.Execute();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[CommandQueue] Error executing '{command.Description}': {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// キューをクリア
        /// </summary>
        public void Clear()
        {
            _queue.Clear();
            
            if (EnableDebugLog)
            {
                Debug.Log("[CommandQueue] Cleared");
            }
        }
    }
}
