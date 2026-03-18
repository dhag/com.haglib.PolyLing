// Assets/Editor/Poly_Ling/Core/Update/UpdateManager.cs
// 更新管理クラス - コア機能
// DirtyFlagsに基づいてバッファ更新を制御

using System;
using UnityEngine;

namespace Poly_Ling.Core
{
    /// <summary>
    /// 更新管理クラス
    /// データ変更を検知し、必要なバッファのみを更新する
    /// </summary>
    public partial class UpdateManager : IDisposable
    {
        // ============================================================
        // フィールド
        // ============================================================

        private DirtyLevel _dirtyFlags = DirtyLevel.None;
        private DirtyLevel _pendingFlags = DirtyLevel.None; // 次フレーム用

        // 統計情報（デバッグ用）
        private int _updateCountThisFrame = 0;
        private int _totalUpdates = 0;
        private DirtyLevel _lastUpdateLevel = DirtyLevel.None;

        // コールバック
        public event Action<DirtyLevel> OnBeforeUpdate;
        public event Action<DirtyLevel> OnAfterUpdate;

        // ============================================================
        // プロパティ
        // ============================================================

        /// <summary>現在のDirtyフラグ</summary>
        public DirtyLevel CurrentDirtyFlags => _dirtyFlags;

        /// <summary>更新が必要か</summary>
        public bool HasAnyDirty => _dirtyFlags != DirtyLevel.None;

        /// <summary>最高更新レベル（0-5）</summary>
        public int HighestDirtyLevel => _dirtyFlags.GetHighestLevel();

        /// <summary>最後の更新レベル</summary>
        public DirtyLevel LastUpdateLevel => _lastUpdateLevel;

        /// <summary>このフレームの更新回数</summary>
        public int UpdateCountThisFrame => _updateCountThisFrame;

        /// <summary>総更新回数</summary>
        public int TotalUpdates => _totalUpdates;

        // ============================================================
        // Dirty設定メソッド
        // ============================================================

        /// <summary>
        /// トポロジー変更をマーク（Level 5）
        /// 頂点/面の追加・削除など
        /// </summary>
        public void MarkTopologyDirty()
        {
            _dirtyFlags |= DirtyLevel.Topology;
        }

        /// <summary>
        /// 頂点位置変更をマーク（Level 4）
        /// 移動・スケール・回転など（トポロジー不変）
        /// </summary>
        public void MarkTransformDirty()
        {
            // Topology変更がある場合は不要（上位レベルに含まれる）
            if (!_dirtyFlags.Has(DirtyLevel.Topology))
            {
                _dirtyFlags |= DirtyLevel.Transform;
            }
        }

        /// <summary>
        /// 選択状態変更をマーク（Level 3）
        /// </summary>
        public void MarkSelectionDirty()
        {
            _dirtyFlags |= DirtyLevel.Selection;
        }

        /// <summary>
        /// カメラパラメータ変更をマーク（Level 2）
        /// </summary>
        public void MarkCameraDirty()
        {
            _dirtyFlags |= DirtyLevel.Camera;
        }

        /// <summary>
        /// マウス位置変更をマーク（Level 1）
        /// </summary>
        public void MarkMouseDirty()
        {
            _dirtyFlags |= DirtyLevel.Mouse;
        }

        /// <summary>
        /// 全てをDirtyにマーク
        /// </summary>
        public void MarkAllDirty()
        {
            _dirtyFlags = DirtyLevel.All;
        }

        /// <summary>
        /// 指定レベルをマーク
        /// </summary>
        public void MarkDirty(DirtyLevel level)
        {
            _dirtyFlags |= level;
        }

        /// <summary>
        /// 次フレームで処理するDirtyをマーク
        /// （現在フレームの更新後に適用）
        /// </summary>
        public void MarkDirtyNextFrame(DirtyLevel level)
        {
            _pendingFlags |= level;
        }

        /// <summary>
        /// Dirtyフラグをクリア
        /// </summary>
        public void ClearDirty()
        {
            _dirtyFlags = DirtyLevel.None;
        }

        // ============================================================
        // フレーム管理
        // ============================================================

        /// <summary>
        /// フレーム開始時に呼び出す
        /// </summary>
        public void BeginFrame()
        {
            _updateCountThisFrame = 0;

            // 前フレームからのペンディングフラグを適用
            if (_pendingFlags != DirtyLevel.None)
            {
                _dirtyFlags |= _pendingFlags;
                _pendingFlags = DirtyLevel.None;
            }
        }

        /// <summary>
        /// フレーム終了時に呼び出す
        /// </summary>
        public void EndFrame()
        {
            // 統計リセットなど（必要に応じて）
        }

        // ============================================================
        // 更新実行
        // ============================================================

        /// <summary>
        /// 更新を実行
        /// </summary>
        /// <returns>実行された更新レベル</returns>
        public DirtyLevel ProcessUpdates()
        {
            if (_dirtyFlags == DirtyLevel.None)
                return DirtyLevel.None;

            DirtyLevel processingFlags = _dirtyFlags;
            _lastUpdateLevel = processingFlags;

            // コールバック
            OnBeforeUpdate?.Invoke(processingFlags);

            // カスケード更新
            ProcessUpdatesCascade(processingFlags);

            // 統計更新
            _updateCountThisFrame++;
            _totalUpdates++;

            // フラグクリア
            _dirtyFlags = DirtyLevel.None;

            // コールバック
            OnAfterUpdate?.Invoke(processingFlags);

            return processingFlags;
        }

        /// <summary>
        /// カスケード更新の実行
        /// 上位レベルから順に処理
        /// </summary>
        private void ProcessUpdatesCascade(DirtyLevel flags)
        {
            // Level 5: Topology
            if (flags.Has(DirtyLevel.Topology))
            {
                OnTopologyUpdate();
                // Topology変更は全てを含む
                OnTransformUpdate();
                OnSelectionUpdate();
                OnCameraUpdate();
                OnMouseUpdate();
                return; // 全て処理済み
            }

            // Level 4: Transform
            if (flags.Has(DirtyLevel.Transform))
            {
                OnTransformUpdate();
                // Transform変更後はカメラ関連も更新が必要
                OnCameraUpdate();
            }

            // Level 3: Selection
            if (flags.Has(DirtyLevel.Selection))
            {
                OnSelectionUpdate();
            }

            // Level 2: Camera
            if (flags.Has(DirtyLevel.Camera))
            {
                OnCameraUpdate();
            }

            // Level 1: Mouse
            if (flags.Has(DirtyLevel.Mouse))
            {
                OnMouseUpdate();
            }
        }

        // ============================================================
        // 更新コールバック（派生クラスまたは外部でオーバーライド）
        // ============================================================

        /// <summary>トポロジー更新（全バッファ再構築）</summary>
        protected virtual void OnTopologyUpdate()
        {
            // BufferManagerで実装
        }

        /// <summary>位置更新</summary>
        protected virtual void OnTransformUpdate()
        {
            // BufferManagerで実装
        }

        /// <summary>選択フラグ更新</summary>
        protected virtual void OnSelectionUpdate()
        {
            // BufferManagerで実装
        }

        /// <summary>カメラ関連更新</summary>
        protected virtual void OnCameraUpdate()
        {
            // BufferManagerで実装
        }

        /// <summary>マウス/ヒットテスト更新</summary>
        protected virtual void OnMouseUpdate()
        {
            // BufferManagerで実装
        }

        // ============================================================
        // IDisposable
        // ============================================================

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // マネージドリソースの解放
                    OnBeforeUpdate = null;
                    OnAfterUpdate = null;
                }

                _disposed = true;
            }
        }

        ~UpdateManager()
        {
            Dispose(false);
        }
    }
}
