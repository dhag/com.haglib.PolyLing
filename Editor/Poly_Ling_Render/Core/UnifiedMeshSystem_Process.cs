// Assets/Editor/Poly_Ling/Core/UnifiedMeshSystem_Process.cs
// 統合メッシュシステム - 更新処理の実装

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Selection;

namespace Poly_Ling.Core
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
        public void ProcessTransformUpdate(int contextIndex)
        {
            var meshContext = _currentModel?.GetMeshContext(contextIndex);
            if (meshContext?.MeshObject == null)
                return;

            // ContextIndex → UnifiedMeshIndex に変換
            int unifiedMeshIndex = _bufferManager.ContextToUnifiedMeshIndex(contextIndex);
            if (unifiedMeshIndex < 0)
                return;

            _bufferManager.UpdatePositions(meshContext.MeshObject, unifiedMeshIndex);

            // ミラー位置も更新
            if (_symmetrySettings != null && _symmetrySettings.IsEnabled)
            {
                _bufferManager.UpdateMirrorPositions(unifiedMeshIndex);
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
        /// <param name="cpuOnly">
        /// trueの場合、GPU版ヒットテストを使わずCPU版のみ使用する。
        /// ProcessHoverOnly（軽量ホバー更新パス）から呼ばれる場合はtrue。
        ///
        /// 【理由】GPU版はDispatchClearBuffersGPUで_VertexFlagsBuffer（選択フラグ含む）を
        /// ゼロクリアする。フルパイプライン内（Normal時）ではPrepareUnifiedDrawingの
        /// AllowSelectionSync=trueで選択フラグが再設定されるが、Idle時は再設定されず
        /// 選択表示が消えてちらつく。CPU版はスクリーン座標配列を読むだけで
        /// GPUバッファを一切変更しないため安全。
        /// </param>
        public void ProcessMouseUpdate(bool cpuOnly = false)
        {
            // ヒットテスト入力設定
            _bufferManager.SetHitTestInput(_mousePosition, _hitRadius, _viewport);

            int newHoveredVertex;
            int newHoveredLine;
            int newHoveredFace;

            // GPU計算が利用可能かつcpuOnly=falseならGPU版を使用
            if (!cpuOnly && _bufferManager.GpuComputeAvailable && _useGpuHitTest)
            {
                Matrix4x4 viewProjection = _projectionMatrix * _viewMatrix;

                // 正しい順序でGPU計算を実行
                // ★注意: DispatchClearBuffersGPUは_VertexFlagsBuffer等を全クリアする。
                //   フルパイプライン外（cpuOnly=true）では絶対に実行してはならない。
                _bufferManager.DispatchClearBuffersGPU();
                _bufferManager.ComputeScreenPositionsGPU(viewProjection, _viewport);
                _bufferManager.DispatchFaceVisibilityGPU();
                _bufferManager.DispatchLineVisibilityGPU();
                _bufferManager.DispatchVertexHitTestGPU(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                _bufferManager.DispatchLineHitTestGPU(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                _bufferManager.DispatchFaceHitTestGPU(_mousePosition, _backfaceCullingEnabled);

                newHoveredVertex = _bufferManager.FindNearestVertexFromGPU(_hitRadius);
                newHoveredLine = _bufferManager.FindNearestLineFromGPU(_hitRadius);
                newHoveredFace = _bufferManager.FindNearestFaceFromGPU();
            }
            else
            {
                // CPU版ヒットテスト
                newHoveredVertex = _bufferManager.FindNearestVertex(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                newHoveredLine = _bufferManager.FindNearestLine(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                newHoveredFace = _bufferManager.FindNearestFace(_mousePosition, _backfaceCullingEnabled);
            }

            // ================================================================
            // メッシュ選択フィルタリング（統合）
            // 選択されていないメッシュの要素はホバー対象外にする。
            // これにより描画側（シェーダーのMeshSelectedフラグ）と
            // 入力側（ホバー検出）のフィルタリングが一致する。
            // ================================================================
            if (newHoveredVertex >= 0)
            {
                if (_bufferManager.GlobalToLocalVertexIndex(newHoveredVertex, out int vMeshIdx, out int vLocalIdx))
                {
                    if (!_flagManager.IsMeshSelected(vMeshIdx))
                        newHoveredVertex = -1;
                }
            }
            if (newHoveredLine >= 0)
            {
                if (_bufferManager.GlobalToLocalLineIndex(newHoveredLine, out int lMeshIdx, out int lLocalIdx))
                {
                    if (!_flagManager.IsMeshSelected(lMeshIdx))
                        newHoveredLine = -1;
                }
            }
            if (newHoveredFace >= 0)
            {
                if (_bufferManager.GlobalToLocalFaceIndex(newHoveredFace, out int fMeshIdx, out int fLocalIdx))
                {
                    if (!_flagManager.IsMeshSelected(fMeshIdx))
                        newHoveredFace = -1;
                }
            }

            // 選択モードを取得
            var mode = _selectionState?.Mode ?? MeshSelectMode.Vertex;
            bool hasVertexMode = (mode & MeshSelectMode.Vertex) != 0;
            bool hasEdgeMode = (mode & MeshSelectMode.Edge) != 0;
            bool hasFaceMode = (mode & MeshSelectMode.Face) != 0;
            bool hasLineMode = (mode & MeshSelectMode.Line) != 0;
            bool hasEdgeOrLineMode = hasEdgeMode || hasLineMode;

            // 選択モードと優先度に基づいてホバーをフィルタリング
            // 優先度: 頂点 > 線分 > 面
            // ただし、そのモードが有効な場合のみ
            
            int effectiveVertex = -1;
            int effectiveLine = -1;
            int effectiveFace = -1;

            // 頂点モードが有効で頂点ヒットあり → 頂点ホバー
            if (hasVertexMode && newHoveredVertex >= 0)
            {
                effectiveVertex = newHoveredVertex;
                // 頂点ヒット時は下位をクリア
            }
            // 線分モードが有効で線分ヒットあり → 線分ホバー
            else if (hasEdgeOrLineMode && newHoveredLine >= 0)
            {
                effectiveLine = newHoveredLine;
                // 線分ヒット時は面をクリア
            }
            // 面モードが有効で面ヒットあり → 面ホバー
            else if (hasFaceMode && newHoveredFace >= 0)
            {
                effectiveFace = newHoveredFace;
            }

            // ホバー状態を更新
            bool changed = false;

            if (effectiveVertex != _hoveredVertexIndex)
            {
                _hoveredVertexIndex = effectiveVertex;
                changed = true;
            }

            if (effectiveLine != _hoveredLineIndex)
            {
                _hoveredLineIndex = effectiveLine;
                changed = true;
            }

            if (effectiveFace != _hoveredFaceIndex)
            {
                _hoveredFaceIndex = effectiveFace;
                changed = true;
            }

            if (changed)
            {
                // ★ cpuOnly時はGPUバッファを触らない。
                // ClearHover()は_vertexFlagsBuffer.SetData(_vertexFlags, ...)を実行するが、
                // CPU側_vertexFlagsにはGPU専用フラグ（Culled等）が含まれていないため、
                // SetDataでGPUバッファが上書きされてCulledフラグが消失し、
                // 描画が破壊される（選択表示の消失、ちらつき等）。
                // cpuOnly時はホバーインデックス変数のみ更新し、
                // GPUバッファへの反映は次のNormal時フルパイプラインに任せる。
                if (!cpuOnly)
                {
                    // ホバーフラグを更新（有効なもののみ）
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
                // Selection更新はPrepareUnifiedDrawingのAllowSelectionSyncブロックで実行。
                // そちらはSyncSelectionFromModel + SetActiveMesh + UpdateAllSelectionFlagsの
                // 完全なシーケンスを持つため、ここでの重複実行を排除。
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
        // ホバー状態クリア
        // ============================================================

        /// <summary>
        /// ホバー状態を全てクリアする。
        /// マウスが表示エリア外に出た場合に呼び出す。
        /// </summary>
        public void ClearAllHover()
        {
            _hoveredVertexIndex = -1;
            _hoveredLineIndex = -1;
            _hoveredFaceIndex = -1;
            _bufferManager.ClearHover();
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
        // 軽量ホバー更新パス
        // ============================================================
        //
        // 【背景と経緯】
        // 通常のホバー更新はUpdateFrame（Repaintイベント時、AllowHitTest=true時のみ）で行われる。
        // しかしワンショット方式（Normal→Idle自動降格）により、Idle時はAllowHitTest=falseとなり
        // UpdateFrame全体がスキップされ、_hoveredVertexIndex等が凍結する。
        //
        // この凍結により「マウス直下の要素」が古いまま残り、以下の問題が発生した:
        //   - OnMouseDownでホバー結果から_hitResultOnMouseDownを構築 → 古い要素を参照
        //   - クリック+ドラッグ=選択同時移動が失敗（頂点がない位置でもヒット判定される等）
        //   - Shift+新頂点クリック+ドラッグで新頂点が取り残される
        //
        // 【解決策】
        // MouseMoveイベント時（かつプレビューエリア内のみ）に、ヒットテストだけを実行する
        // 軽量パスを追加した。フルパイプライン（Topology/Transform/Selection/Camera全更新）は不要で、
        // ProcessMouseUpdate（CPU/GPUヒットテスト）のみを実行する。
        //
        // 【★★★ 禁忌（絶対厳守） ★★★】
        // このメソッドをRepaintイベントやUpdate/OnGUIの毎フレーム処理から呼んではならない。
        // MouseMoveイベント（ユーザーがマウスを実際に動かした時のみ発火）専用である。
        // 毎フレーム呼び出すとGPUヒットテストが毎フレーム走り、パフォーマンスが劣化する。
        //
        // 呼び出し元: UnifiedSystemAdapter.UpdateHoverOnly()
        //           → PolyLing_Input.UpdateHoverOnMouseMove()（MouseMoveイベント内）
        // ★★★★★★★★★★★★★★★★★★★★

        /// <summary>
        /// ホバー専用の軽量更新
        /// マウス位置を設定し、ヒットテスト（ProcessMouseUpdate）のみ実行する。
        /// カメラ/トポロジ/選択の再計算は行わない。
        /// スクリーン座標は直前のProcessCameraUpdateで計算済みのものを使用する。
        /// </summary>
        /// <param name="mousePosition">ローカル座標系のマウス位置（BeginClip後相当）</param>
        public void ProcessHoverOnly(Vector2 mousePosition)
        {
            _mousePosition = mousePosition;
            // ★ cpuOnly=true: GPUバッファ（選択フラグ等）を破壊しない。
            // GPU版はDispatchClearBuffersGPUで_VertexFlagsBufferをゼロクリアし、
            // Idle時はAllowSelectionSync=falseのため再設定されずちらつく。
            ProcessMouseUpdate(cpuOnly: true);
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
