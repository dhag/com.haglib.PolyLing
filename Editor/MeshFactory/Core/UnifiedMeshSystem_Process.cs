// Assets/Editor/MeshFactory/Core/UnifiedMeshSystem_Process.cs
// 統合メッシュシステム - 更新処理の実装

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;
using MeshFactory.Selection;

namespace MeshFactory.Core
{
    public partial class UnifiedMeshSystem
    {
        // ============================================================
        // 個別更新処理
        // ============================================================

        /// <summary>
        /// トポロジー更新（Level 5）
        /// </summary>
        public void ProcessTopologyUpdate()
        {
            if (_currentModel == null)
            {
                _bufferManager.ClearData();
                return;
            }

            _bufferManager.BuildFromModel(_currentModel, _activeModelIndex);

            // フラグも再設定
            _bufferManager.SetActiveMesh(_activeModelIndex, _activeMeshIndex);
            _bufferManager.SetSelectionState(_selectionState);
            _bufferManager.UpdateAllSelectionFlags();

            // ミラー更新
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                _bufferManager.UpdateMirrorPositions();
            }
        }

        /// <summary>
        /// 位置更新（Level 4）
        /// </summary>
        public void ProcessTransformUpdate()
        {
            if (_currentModel == null)
                return;

            _bufferManager.UpdateAllPositions(_currentModel.MeshContextList);

            // ミラー位置も更新
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                _bufferManager.UpdateMirrorPositions();
            }
        }

        /// <summary>
        /// 特定メッシュの位置を更新
        /// </summary>
        public void ProcessTransformUpdate(int meshIndex)
        {
            var meshContext = _currentModel?.GetMeshContext(meshIndex);
            if (meshContext?.MeshObject == null)
                return;

            _bufferManager.UpdatePositions(meshContext.MeshObject, meshIndex);

            // ミラー位置も更新
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                _bufferManager.UpdateMirrorPositions(meshIndex);
            }
        }

        /// <summary>
        /// 選択フラグ更新（Level 3）
        /// </summary>
        public void ProcessSelectionUpdate()
        {
            _bufferManager.UpdateAllSelectionFlags();
        }

        /// <summary>
        /// 選択差分更新
        /// </summary>
        public void ProcessSelectionUpdate(HashSet<int> oldSelection, HashSet<int> newSelection)
        {
            _bufferManager.UpdateVertexSelectionDiff(oldSelection, newSelection, _activeMeshIndex);
        }

        /// <summary>
        /// カメラ更新（Level 2）
        /// </summary>
        public void ProcessCameraUpdate()
        {
            Matrix4x4 viewProjection = _projectionMatrix * _viewMatrix;

            _bufferManager.UpdateCamera(
                _viewMatrix,
                _projectionMatrix,
                _cameraPosition,
                _cameraTarget,
                _viewport);

            _bufferManager.ComputeScreenPositions(viewProjection, _viewport);

            // ミラースクリーン座標も計算
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                _bufferManager.ComputeMirrorScreenPositions(viewProjection, _viewport);
            }
        }

        /// <summary>
        /// マウス/ヒットテスト更新（Level 1）
        /// </summary>
        public void ProcessMouseUpdate()
        {
            // ヒットテスト入力設定
            _bufferManager.SetHitTestInput(_mousePosition, _hitRadius, _viewport);

            int newHoveredVertex;
            int newHoveredLine;
            int newHoveredFace;

            // GPU計算が利用可能ならGPU版を使用
            if (_bufferManager.GpuComputeAvailable && _useGpuHitTest)
            {
                Matrix4x4 viewProjection = _projectionMatrix * _viewMatrix;

                // 正しい順序でGPU計算を実行
                _bufferManager.DispatchClearBuffersGPU();
                _bufferManager.ComputeScreenPositionsGPU(viewProjection, _viewport);
                _bufferManager.DispatchFaceVisibilityGPU();
                _bufferManager.DispatchLineVisibilityGPU();
                _bufferManager.DispatchVertexHitTestGPU(_mousePosition, _hitRadius);
                _bufferManager.DispatchLineHitTestGPU(_mousePosition, _hitRadius);
                _bufferManager.DispatchFaceHitTestGPU(_mousePosition);

                newHoveredVertex = _bufferManager.FindNearestVertexFromGPU(_hitRadius);
                newHoveredLine = _bufferManager.FindNearestLineFromGPU(_hitRadius);
                newHoveredFace = _bufferManager.FindNearestFaceFromGPU();
            }
            else
            {
                // CPU版ヒットテスト
                newHoveredVertex = _bufferManager.FindNearestVertex(_mousePosition, _hitRadius);
                newHoveredLine = _bufferManager.FindNearestLine(_mousePosition, _hitRadius);
                newHoveredFace = _bufferManager.FindNearestFace(_mousePosition);
            }

            // ホバー状態更新
            bool changed = false;

            if (newHoveredVertex != _hoveredVertexIndex)
            {
                _hoveredVertexIndex = newHoveredVertex;
                changed = true;
            }

            if (newHoveredLine != _hoveredLineIndex)
            {
                _hoveredLineIndex = newHoveredLine;
                changed = true;
            }

            if (newHoveredFace != _hoveredFaceIndex)
            {
                _hoveredFaceIndex = newHoveredFace;
                changed = true;
            }

            if (changed)
            {
                // ホバーフラグを更新（頂点・線分・面すべて）
                _bufferManager.ClearHover();
                
                if (_hoveredVertexIndex >= 0)
                {
                    _bufferManager.SetHoverVertex(_hoveredVertexIndex);
                }
                if (_hoveredLineIndex >= 0)
                {
                    _bufferManager.SetHoverLine(_hoveredLineIndex);
                }
                if (_hoveredFaceIndex >= 0)
                {
                    _bufferManager.SetHoverFace(_hoveredFaceIndex);
                }
            }
        }

        // ============================================================
        // 統合更新処理
        // ============================================================

        /// <summary>
        /// DirtyLevelに基づいて更新を実行
        /// </summary>
        public void ExecuteUpdates(DirtyLevel level)
        {
            if (level == DirtyLevel.None)
                return;

            // カスケード実行
            if (level.Has(DirtyLevel.Topology))
            {
                ProcessTopologyUpdate();
                ProcessCameraUpdate();
                ProcessMouseUpdate();
                return; // 全て処理済み
            }

            if (level.Has(DirtyLevel.Transform))
            {
                ProcessTransformUpdate();
                ProcessCameraUpdate(); // 位置変更後はスクリーン座標も更新
                ProcessMouseUpdate();  // スクリーン座標変更後はヒットテストも更新
                return;
            }

            if (level.Has(DirtyLevel.Selection))
            {
                ProcessSelectionUpdate();
            }

            if (level.Has(DirtyLevel.Camera))
            {
                ProcessCameraUpdate();
                ProcessMouseUpdate();  // スクリーン座標変更後はヒットテストも更新
                return;
            }

            if (level.Has(DirtyLevel.Mouse))
            {
                ProcessMouseUpdate();
            }
        }

        // ============================================================
        // ヒットテスト結果取得
        // ============================================================

        /// <summary>
        /// ホバー中の頂点のローカルインデックスを取得
        /// </summary>
        public bool GetHoveredVertexLocal(out int meshIndex, out int localIndex)
        {
            return _bufferManager.GlobalToLocalVertexIndex(_hoveredVertexIndex, out meshIndex, out localIndex);
        }

        /// <summary>
        /// ホバー中のラインのローカル情報を取得
        /// </summary>
        public bool GetHoveredLineLocal(out int meshIndex, out int localIndex)
        {
            return _bufferManager.GlobalToLocalLineIndex(_hoveredLineIndex, out meshIndex, out localIndex);
        }

        /// <summary>
        /// ホバー中の面のローカル情報を取得
        /// </summary>
        public bool GetHoveredFaceLocal(out int meshIndex, out int localIndex)
        {
            return _bufferManager.GlobalToLocalFaceIndex(_hoveredFaceIndex, out meshIndex, out localIndex);
        }

        /// <summary>
        /// スクリーン位置から頂点を検索
        /// </summary>
        public int FindVertexAtScreenPos(Vector2 screenPos, float radius)
        {
            return _bufferManager.FindNearestVertex(screenPos, radius);
        }

        /// <summary>
        /// スクリーン位置からラインを検索
        /// </summary>
        public int FindLineAtScreenPos(Vector2 screenPos, float radius)
        {
            return _bufferManager.FindNearestLine(screenPos, radius);
        }

        /// <summary>
        /// グローバルインデックスをローカルに変換
        /// </summary>
        public bool GlobalToLocal(int globalVertexIndex, out int meshIndex, out int localIndex)
        {
            return _bufferManager.GlobalToLocalVertexIndex(globalVertexIndex, out meshIndex, out localIndex);
        }

        /// <summary>
        /// ローカルインデックスをグローバルに変換
        /// </summary>
        public int LocalToGlobal(int meshIndex, int localIndex)
        {
            return _bufferManager.LocalToGlobalVertexIndex(meshIndex, localIndex);
        }

        // ============================================================
        // バッチ操作
        // ============================================================

        /// <summary>
        /// バッチ更新を開始
        /// 複数の変更をまとめて1回の更新で処理
        /// </summary>
        public IDisposable BeginBatchUpdate()
        {
            return _updateManager.BatchScope();
        }

        /// <summary>
        /// 選択を一括変更
        /// </summary>
        public void BatchSelectVertices(IEnumerable<int> localIndices, bool additive = false)
        {
            if (_selectionState == null)
                return;

            using (_updateManager.BatchScope())
            {
                if (!additive)
                {
                    _selectionState.Vertices.Clear();
                }

                foreach (int idx in localIndices)
                {
                    _selectionState.Vertices.Add(idx);
                }

                _updateManager.MarkSelectionDirty();
            }
        }

        // ============================================================
        // デバッグ
        // ============================================================

        /// <summary>
        /// システム状態をログ出力
        /// </summary>
        public void LogStatus()
        {
            Debug.Log($"[UnifiedMeshSystem] Vertices: {TotalVertexCount}, Lines: {TotalLineCount}, Meshes: {MeshCount}");
            Debug.Log($"[UnifiedMeshSystem] Active: Model={_activeModelIndex}, Mesh={_activeMeshIndex}");
            Debug.Log($"[UnifiedMeshSystem] Hover: Vertex={_hoveredVertexIndex}, Line={_hoveredLineIndex}");
            _updateManager.LogStatus();
        }

        /// <summary>
        /// 更新統計を取得
        /// </summary>
        public UpdateManager.UpdateStatistics GetUpdateStatistics()
        {
            return _updateManager.GetStatistics();
        }
    }
}
