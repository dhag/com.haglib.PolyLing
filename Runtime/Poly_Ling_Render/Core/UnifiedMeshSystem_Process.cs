// Assets/Editor/Poly_Ling/Core/UnifiedMeshSystem_Process.cs
// 統合メッシュシステム - 更新処理の実装

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
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
        /// 選択メッシュのみ位置更新（TransformDragging軽量パス用）
        /// 非選択メッシュはドラッグ中位置不変のため更新不要。
        /// </summary>
        public void ProcessTransformUpdateSelectedOnly()
        {
            if (_currentModel == null)
                return;

            foreach (int contextIdx in _currentModel.SelectedDrawableMeshIndices)
            {
                ProcessTransformUpdate(contextIdx);
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

            // ============================================================
            // 【スクリーン座標をここで計算しない理由】 2026-08-28（残件 3-3）
            //
            //   ExecuteUpdates は Topology / Transform / Camera のどの分岐でも
            //   ProcessCameraUpdate() の直後に ProcessMouseUpdate() を呼ぶ。
            //   ProcessMouseUpdate は GPU 版 ComputeScreenPositionsGPU で
            //   _screenPositions / _screenPositions4 を上書きするため、
            //   ここで CPU 版を回しても結果は必ず捨てられていた。
            //   全頂点ぶんの投影計算を CPU と GPU で二重に行っていたことになる。
            //
            //   ただし後段の GPU 版が走らない条件が 2 つある。そのときだけ
            //   CPU 版を回す。ここを外すと _screenPositions が古いまま残り、
            //   矩形選択・投げ縄選択・ウェイトペイントのブラシ判定が
            //   前フレームの座標で行われる。
            //
            //     ・SuppressHover == true
            //         ProcessMouseUpdate がホバーを消して早期 return する。
            //         ブラシ系ツール（SkinWeightPaint 等）がこの状態になる。
            //     ・GPU が使えない、または GPU ヒットテストが無効
            //         ProcessMouseUpdate が CPU 版ヒットテストへ分岐する。
            //         CPU 版は _screenPositions を「読むだけ」で埋めない。
            //
            //   条件式は ProcessMouseUpdate の分岐と一字一句そろえること。
            //   片方だけ変えると座標が誰にも埋められないフレームが生まれる。
            // ============================================================
            bool gpuWillRecomputeScreenPositions =
                !SuppressHover
                && _bufferManager.GpuComputeAvailable
                && _useGpuHitTest;

            if (!gpuWillRecomputeScreenPositions)
                _bufferManager.ComputeScreenPositions(viewProjection, _viewport);

            // ミラースクリーン座標の CPU 計算（ComputeMirrorScreenPositions）は撤去した。
            // 出力先 _mirrorScreenPosBuffer を ComputeShader へ渡す箇所が 0 件で、
            // 誰も読まない計算だった。GPU が使うミラースクリーン座標は
            // ComputeScreenPositions カーネルが _UseMirror > 0 のときに直接埋める。
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
        /// AllowSelectedDrawableMeshSync=trueで選択フラグが再設定されるが、Idle時は再設定されず
        /// 選択表示が消えてちらつく。CPU版はスクリーン座標配列を読むだけで
        /// GPUバッファを一切変更しないため安全。
        /// </param>
        public void ProcessMouseUpdate(bool cpuOnly = false)
        {
            //Debug.Log($"MeshSelectMode ");
            // ホバー抑止モード（ブラシ系ツール等）
            if (SuppressHover)
            {
                bool hadHover = _hoveredVertexIndex >= 0 || _hoveredLineIndex >= 0 || _hoveredFaceIndex >= 0;
                _hoveredVertexIndex = -1;
                _hoveredLineIndex = -1;
                _hoveredFaceIndex = -1;
                _snapHoveredVertexIndex = -1;
                if (hadHover && !cpuOnly)
                    _bufferManager.ClearHover();
                return;
            }

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
                // ============================================================
                // 【この経路が書き込む slot は HitTestSlot ただ 1 つ】 2026-08-28
                //
                //   使うカメラは「今ポインタが乗っているビューポート」のもの。
                //   ビューポート表示用の slot 0〜3 へ書くと、別のビューポートの
                //   カメラで計算した結果がそのビューの表示用カリングになる。
                //   以前は slot 0（= Perspective 表示用）を共用しており、
                //   Top ビュー上でマウスを動かすと Perspective の表示が壊れていた。
                //
                //   ここを表示用 slot へ戻してはならない。
                // ============================================================
                const int hitSlot = Poly_Ling.Core.UnifiedBufferManager.HitTestSlot;

                _bufferManager.DispatchClearBuffersGPU();
                // per-slot カリングバッファを「全カリング済み=1u」に初期化する。
                // この呼び出しがないと DispatchFaceVisibilityGPU が書き込む前の
                // 不定値（または前フレームの残留値）がカリング判定に使われ、
                // 常にカリング済みと判定されてしまう。
                _bufferManager.DispatchClearCulledBuffersGPU(hitSlot);
                // readback: true。直後の FindNearestVertexFromGPU / FindNearestLineFromGPU が
                // _screenPositions を読んでスクリーン距離のバンド量子化に使うため、
                // この経路だけは同期読み戻しが要る。
                _bufferManager.ComputeScreenPositionsGPU(
                    viewProjection, _viewport, hitSlot, "hover", readback: true);
                if (_backfaceCullingEnabled)
                {
                    _bufferManager.DispatchFaceVisibilityGPU(hitSlot);
                    _bufferManager.DispatchLineVisibilityGPU(hitSlot);
                }
                else
                {
                    // カリングOFF: 全頂点・辺・面を可視（0u）に設定
                    _bufferManager.ClearCulledFlagsGPU(hitSlot);
                }

                // 永続ミラーの表示トグル（ApplyMirrorCull）はここでは適用しない。
                // 非表示にしているミラーでも吸着・ナイフ等の判定対象に残す必要があるため。
                // 表示側の抑止は slot 0〜3 の DispatchCullingForDisplay が行う。
                _bufferManager.DispatchVertexHitTestGPU(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                _bufferManager.DispatchLineHitTestGPU(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                _bufferManager.DispatchFaceHitTestGPU(_mousePosition, _backfaceCullingEnabled);

                newHoveredVertex = _bufferManager.FindNearestVertexFromGPU(_hitRadius);
                newHoveredLine = _bufferManager.FindNearestLineFromGPU(_hitRadius);
                newHoveredFace = _bufferManager.FindNearestFaceFromGPU();

                // 吸着用ヒットテスト（メッシュ選択を無視）。
                // スクリーン座標とカリングは上で計算済みのものをそのまま使う。
                // 有効時のみ実行する。頂点数ぶんの GetData が 1 回増えるため。
                if (EnableSnapHitTest)
                {
                    _bufferManager.DispatchVertexSnapHitTestGPU(
                        _mousePosition, _hitRadius, _backfaceCullingEnabled);
                    _snapHoveredVertexIndex = _bufferManager.FindNearestSnapVertexFromGPU(_hitRadius);
                }
                else
                {
                    _snapHoveredVertexIndex = -1;
                }
            }
            else
            {
                // CPU版ヒットテスト
                newHoveredVertex = _bufferManager.FindNearestVertex(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                newHoveredLine = _bufferManager.FindNearestLine(_mousePosition, _hitRadius, _backfaceCullingEnabled);
                newHoveredFace = _bufferManager.FindNearestFace(_mousePosition, _backfaceCullingEnabled);

                // CPU 経路には吸着用の実装を用意しない（GPU 経路専用）。
                _snapHoveredVertexIndex = -1;
            }

            // ================================================================
            // メッシュ選択フィルタリング（統合）
            // 選択されていないメッシュの要素はホバー対象外にする。
            // これにより描画側（シェーダーのメッシュ選択フラグ）と
            // 入力側（ホバー検出）のフィルタリングが一致する。
            //
            // 【注意】_snapHoveredVertexIndex にはこのフィルタを適用しない。
            // 非選択オブジェクトの頂点を返すことが吸着用ヒットテストの目的のため。
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

            // 辺（面の辺）と補助線分（2頂点の独立線分）は別種別。
            // GPU のライン当たり判定は両者を 1 本の配列で返すため、ここで
            // IsAuxLine を見て「モードで無効な方」を落とす。
            // これを行わないと「辺だけ ON」でも補助線分がホバーし、
            // 「線分だけ ON」でも面の辺がホバーする。
            if (newHoveredLine >= 0)
            {
                var lineArray = _bufferManager?.Lines;
                bool isAuxLine = lineArray != null
                                 && newHoveredLine < lineArray.Length
                                 && lineArray[newHoveredLine].IsAuxLine;
                if (isAuxLine ? !hasLineMode : !hasEdgeMode)
                    newHoveredLine = -1;
            }

            // 選択モードと優先度に基づいてホバーをフィルタリング
            // 優先度: 頂点 > 線分 > 面
            // ただし、そのモードが有効な場合のみ
            
            int effectiveVertex = -1;
            int effectiveLine = -1;
            int effectiveFace = -1;

            //Debug.Log($"MeshSelectMode{mode}");

            // 頂点モードが有効で頂点ヒットあり → 頂点ホバー
            if (hasVertexMode && newHoveredVertex >= 0)
            {
                effectiveVertex = newHoveredVertex;
                // 頂点ヒット時は下位をクリア
            }
            // 辺／補助線分ヒットあり → 線分ホバー
            // （モードによる種別の絞り込みは上の IsAuxLine 判定で済んでいる）
            else if (newHoveredLine >= 0)
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
                ProcessMouseUpdate();//Debug.Log("ProcessMouseUpdate1");
                return; // 全て処理済み
            }

            if (level.Has(DirtyLevel.Transform))
            {
                ProcessTransformUpdate();
                ProcessCameraUpdate(); // 位置変更後はスクリーン座標も更新
                ProcessMouseUpdate(); //Debug.Log("ProcessMouseUpdate2");  // スクリーン座標変更後はヒットテストも更新
                return;
            }

            if (level.Has(DirtyLevel.Selection))
            {
                // Selection更新はPrepareUnifiedDrawingのAllowSelectedDrawableMeshSyncブロックで実行。
                // そちらはSyncSelectionFromModel + SetActiveMesh + UpdateAllSelectionFlagsの
                // 完全なシーケンスを持つため、ここでの重複実行を排除。
            }

            if (level.Has(DirtyLevel.Camera))
            {
                ProcessCameraUpdate();
                ProcessMouseUpdate(); //Debug.Log("ProcessMouseUpdate3");  // スクリーン座標変更後はヒットテストも更新
                return;
            }

            if (level.Has(DirtyLevel.Mouse))
            {
                ProcessMouseUpdate(); //Debug.Log("ProcessMouseUpdate4");
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
            _snapHoveredVertexIndex = -1;
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
        // 軽量ホバー更新パス（撤去済み） 2026-08-28
        // ============================================================
        //
        // 【ProcessHoverOnly を撤去した理由】
        //   唯一の呼出元だった UnifiedSystemAdapter.UpdateHoverOnly(Vector2, Rect) の
        //   呼出元が 0 件で、経路全体が死んでいた。本体は無条件の
        //   Debug.LogError("cpuOnly: trueは禁止") を含んでおり、生きていれば
        //   毎回エラーを吐く実装だった（残件 5-2）。
        //
        //   Player の正規ホバー経路は
        //   PlayerViewportPanel.OnPointerHover
        //     → PlayerViewportManager.NotifyPointerHover
        //     → UnifiedSystemAdapter.UpdateFrame
        //     → ProcessMouseUpdate(cpuOnly: false)
        //   であり、GPU ヒットテストで完結する。
        //   CPU ヒットテストへ戻す軽量パスを再び作ってはならない。
        //
        //   ProcessMouseUpdate の cpuOnly 引数自体は残してある。GPU 非対応環境で
        //   GpuComputeAvailable が false のときの分岐で使う。

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
