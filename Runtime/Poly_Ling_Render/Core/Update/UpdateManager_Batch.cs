// Assets/Editor/Poly_Ling/Core/Update/UpdateManager_Batch.cs
// 更新管理クラス - バッチ更新とユーティリティ

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Core
{
    public partial class UpdateManager
    {
        // ============================================================
        // バッチ更新
        // ============================================================

        private bool _batchMode = false;
        private DirtyLevel _batchAccumulatedFlags = DirtyLevel.None;

        /// <summary>
        /// バッチ更新を開始
        /// 複数のDirty操作をまとめて1回の更新で処理
        /// </summary>
        public void BeginBatch()
        {
            if (_batchMode)
            {
                Debug.LogWarning("[UpdateManager] BeginBatch called while already in batch mode");
                return;
            }

            _batchMode = true;
            _batchAccumulatedFlags = DirtyLevel.None;
        }

        /// <summary>
        /// バッチ更新を終了し、累積したフラグで更新を実行
        /// </summary>
        public DirtyLevel EndBatch()
        {
            if (!_batchMode)
            {
                Debug.LogWarning("[UpdateManager] EndBatch called without BeginBatch");
                return DirtyLevel.None;
            }

            _batchMode = false;

            // 累積フラグをメインフラグにマージ
            _dirtyFlags |= _batchAccumulatedFlags;
            _batchAccumulatedFlags = DirtyLevel.None;

            // 更新を実行
            return ProcessUpdates();
        }

        /// <summary>
        /// バッチ更新をキャンセル
        /// </summary>
        public void CancelBatch()
        {
            _batchMode = false;
            _batchAccumulatedFlags = DirtyLevel.None;
        }

        /// <summary>
        /// バッチモード中かどうか
        /// </summary>
        public bool IsBatchMode => _batchMode;

        // ============================================================
        // スコープベースのバッチ更新
        // ============================================================

        /// <summary>
        /// バッチ更新スコープを作成
        /// using文で使用: using (updateManager.BatchScope()) { ... }
        /// </summary>
        public BatchUpdateScope BatchScope()
        {
            return new BatchUpdateScope(this);
        }

        /// <summary>
        /// バッチ更新スコープ
        /// </summary>
        public struct BatchUpdateScope : IDisposable
        {
            private UpdateManager _manager;
            private bool _disposed;

            public BatchUpdateScope(UpdateManager manager)
            {
                _manager = manager;
                _disposed = false;
                _manager.BeginBatch();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _manager?.EndBatch();
                    _manager = null;
                }
            }
        }

        // ============================================================
        // 条件付き更新
        // ============================================================

        /// <summary>
        /// 指定レベル以上の更新が必要か
        /// </summary>
        public bool NeedsUpdateAtLevel(int level)
        {
            return _dirtyFlags.GetHighestLevel() >= level;
        }

        /// <summary>
        /// 指定フラグの更新が必要か
        /// </summary>
        public bool NeedsUpdate(DirtyLevel level)
        {
            return _dirtyFlags.Has(level);
        }

        /// <summary>
        /// 指定レベルのみ更新を実行（他のフラグは保持）
        /// </summary>
        public void ProcessUpdateAtLevel(DirtyLevel level)
        {
            if (!_dirtyFlags.Has(level))
                return;

            // 指定レベルのみ処理
            switch (level)
            {
                case DirtyLevel.Topology:
                    OnTopologyUpdate();
                    break;
                case DirtyLevel.Transform:
                    OnTransformUpdate();
                    break;
                case DirtyLevel.Selection:
                    OnSelectionUpdate();
                    break;
                case DirtyLevel.Camera:
                    OnCameraUpdate();
                    break;
                case DirtyLevel.Mouse:
                    OnMouseUpdate();
                    break;
            }

            // 処理したフラグを除去
            _dirtyFlags = _dirtyFlags.Without(level);
        }

        // ============================================================
        // 変更トラッキング
        // ============================================================

        /// <summary>
        /// 変更追跡コンテキスト
        /// 変更前後の状態を比較して自動的にDirtyフラグを設定
        /// </summary>
        public class ChangeTracker
        {
            private UpdateManager _manager;
            private Vector3[] _originalPositions;
            private int _originalVertexCount;
            private int _originalFaceCount;

            public ChangeTracker(UpdateManager manager)
            {
                _manager = manager;
            }

            /// <summary>
            /// 変更追跡を開始（位置情報をキャプチャ）
            /// </summary>
            public void BeginTracking(Vector3[] positions, int vertexCount, int faceCount)
            {
                _originalPositions = positions != null ? (Vector3[])positions.Clone() : null;
                _originalVertexCount = vertexCount;
                _originalFaceCount = faceCount;
            }

            /// <summary>
            /// 変更追跡を終了し、適切なDirtyフラグを設定
            /// </summary>
            public void EndTracking(Vector3[] newPositions, int newVertexCount, int newFaceCount)
            {
                // トポロジー変更チェック
                if (_originalVertexCount != newVertexCount || _originalFaceCount != newFaceCount)
                {
                    _manager.MarkTopologyDirty();
                    return;
                }

                // 位置変更チェック
                if (_originalPositions != null && newPositions != null)
                {
                    for (int i = 0; i < _originalPositions.Length && i < newPositions.Length; i++)
                    {
                        if (_originalPositions[i] != newPositions[i])
                        {
                            _manager.MarkTransformDirty();
                            return;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 変更追跡コンテキストを作成
        /// </summary>
        public ChangeTracker CreateChangeTracker()
        {
            return new ChangeTracker(this);
        }

        // ============================================================
        // デバッグ
        // ============================================================

        /// <summary>
        /// 現在の状態をログ出力
        /// </summary>
        public void LogStatus()
        {
            Debug.Log($"[UpdateManager] DirtyFlags: {_dirtyFlags} ({_dirtyFlags.GetLevelName()}), " +
                     $"BatchMode: {_batchMode}, " +
                     $"FrameUpdates: {_updateCountThisFrame}, " +
                     $"TotalUpdates: {_totalUpdates}");
        }

        /// <summary>
        /// 統計情報をリセット
        /// </summary>
        public void ResetStatistics()
        {
            _totalUpdates = 0;
            _updateCountThisFrame = 0;
        }

        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public UpdateStatistics GetStatistics()
        {
            return new UpdateStatistics
            {
                TotalUpdates = _totalUpdates,
                UpdatesThisFrame = _updateCountThisFrame,
                LastUpdateLevel = _lastUpdateLevel,
                CurrentDirtyFlags = _dirtyFlags,
                IsBatchMode = _batchMode
            };
        }

        /// <summary>
        /// 統計情報構造体
        /// </summary>
        public struct UpdateStatistics
        {
            public int TotalUpdates;
            public int UpdatesThisFrame;
            public DirtyLevel LastUpdateLevel;
            public DirtyLevel CurrentDirtyFlags;
            public bool IsBatchMode;
        }
    }
}
