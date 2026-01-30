// Assets/Editor/Poly_Ling/Core/Buffers/UnifiedBufferManager_Update.cs
// 統合バッファ管理クラス - 更新処理
// 選択、カメラ、ヒットテストの更新

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Core
{
    public partial class UnifiedBufferManager
    {
        // ============================================================
        // Level 3: 選択フラグ更新
        // ============================================================

        /// <summary>
        /// 選択状態を設定
        /// </summary>
        public void SetSelectionState(SelectionState selectionState)
        {
            _flagManager.SelectionState = selectionState;
        }

        /// <summary>
        /// アクティブメッシュを設定
        /// </summary>
        public void SetActiveMesh(int modelIndex, int meshIndex)
        {
            _flagManager.ActiveModelIndex = modelIndex;
            _flagManager.ActiveMeshIndex = meshIndex;
            _flagManager.SelectedModelIndex = modelIndex;
            _flagManager.SelectedMeshIndex = meshIndex;
        }

        /// <summary>
        /// 全頂点の選択フラグを更新
        /// </summary>
        public void UpdateAllSelectionFlags()
        {
            int activeMesh = _flagManager.ActiveMeshIndex;
            int activeModel = _flagManager.ActiveModelIndex;
            bool hasSelectionState = _flagManager.SelectionState != null;

            // デバッグログ
            if (hasSelectionState)
            {
                var ss = _flagManager.SelectionState;
                if (ss.Edges.Count > 0 || ss.Faces.Count > 0 || ss.Lines.Count > 0)
                {
                    Debug.Log($"[UpdateAllSelectionFlags] V={ss.Vertices.Count}, E={ss.Edges.Count}, F={ss.Faces.Count}, L={ss.Lines.Count}, activeMesh={activeMesh}, meshCount={_meshCount}");
                }
            }

            for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
            {
                var meshInfo = _meshInfos[meshIdx];
                bool isActiveMesh = (meshIdx == activeMesh) && ((int)meshInfo.ModelIndex == activeModel);

                SelectionFlags hierarchyFlags = _flagManager.ComputeHierarchyFlags(
                    (int)meshInfo.ModelIndex, meshIdx);

                for (uint v = 0; v < meshInfo.VertexCount; v++)
                {
                    uint globalIdx = meshInfo.VertexStart + v;
                    if (globalIdx >= _totalVertexCount)
                        break;

                    // 既存フラグから階層・選択フラグをクリア（Culledは保持）
                    uint flags = _vertexFlags[globalIdx];
                    flags &= ~((uint)SelectionFlags.HierarchyMask | (uint)SelectionFlags.ElementSelectionMask);

                    // 新しいフラグを設定
                    flags |= (uint)hierarchyFlags;

                    if (hasSelectionState && isActiveMesh && _flagManager.SelectionState.Vertices.Contains((int)v))
                    {
                        flags |= (uint)SelectionFlags.VertexSelected;
                    }

                    _vertexFlags[globalIdx] = flags;
                }
            }

            // GPUにアップロード
            if (_totalVertexCount > 0)
            {
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }

            // ラインフラグも更新
            UpdateAllLineSelectionFlags();
        }

        /// <summary>
        /// ラインの選択フラグを更新
        /// </summary>
        private void UpdateAllLineSelectionFlags()
        {
            int activeMesh = _flagManager.ActiveMeshIndex;
            int activeModel = _flagManager.ActiveModelIndex;
            bool hasSelectionState = _flagManager.SelectionState != null;

            int edgeSelectedCount = 0;
            int lineSelectedCount = 0;
            
            for (int lineIdx = 0; lineIdx < _totalLineCount; lineIdx++)
            {
                var line = _lines[lineIdx];
                bool isActiveMesh = ((int)line.MeshIndex == activeMesh) && ((int)line.ModelIndex == activeModel);

                // 既存フラグから選択フラグをクリア
                uint flags = _lineFlags[lineIdx];
                flags &= ~((uint)SelectionFlags.HierarchyMask | (uint)SelectionFlags.EdgeSelected | (uint)SelectionFlags.LineSelected);

                // 階層フラグ
                flags |= (uint)_flagManager.ComputeHierarchyFlags((int)line.ModelIndex, (int)line.MeshIndex);

                if (hasSelectionState && isActiveMesh)
                {
                    bool isAuxLine = (flags & (uint)SelectionFlags.IsAuxLine) != 0;

                    if (isAuxLine)
                    {
                        // 補助線選択
                        if (_flagManager.SelectionState.Lines.Contains((int)line.FaceIndex))
                        {
                            flags |= (uint)SelectionFlags.LineSelected;
                            lineSelectedCount++;
                        }
                    }
                    else
                    {
                        // エッジ選択（ローカルインデックスで判定が必要）
                        // Note: ここではグローバルインデックスなので、変換が必要
                        // 簡易実装: メッシュ内ローカルインデックスを計算
                        var meshInfo = _meshInfos[line.MeshIndex];
                        int localV1 = (int)(line.V1 - meshInfo.VertexStart);
                        int localV2 = (int)(line.V2 - meshInfo.VertexStart);
                        var pair = new VertexPair(localV1, localV2);

                        if (_flagManager.SelectionState.Edges.Contains(pair))
                        {
                            flags |= (uint)SelectionFlags.EdgeSelected;
                            edgeSelectedCount++;
                        }
                    }
                }

                _lineFlags[lineIdx] = flags;
            }

            // デバッグ: 選択フラグ設定数
            if (hasSelectionState && (_flagManager.SelectionState.Edges.Count > 0 || _flagManager.SelectionState.Lines.Count > 0))
            {
                Debug.Log($"[UpdateAllSelectionFlags] EdgeSelected flags set: {edgeSelectedCount}, LineSelected flags set: {lineSelectedCount}");
            }

            // GPUにアップロード
            if (_totalLineCount > 0)
            {
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
        }

        /// <summary>
        /// 頂点選択の差分更新
        /// </summary>
        public void UpdateVertexSelectionDiff(HashSet<int> oldSelection, HashSet<int> newSelection, int meshIndex)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return;

            var meshInfo = _meshInfos[meshIndex];

            _flagManager.UpdateVertexSelectionFlags(
                _vertexFlags,
                meshInfo.VertexStart,
                oldSelection,
                newSelection);

            // 差分のみアップロード
            var changed = new HashSet<int>(oldSelection);
            changed.SymmetricExceptWith(newSelection);

            if (changed.Count > 0)
            {
                // 効率化: 連続範囲を検出してまとめてアップロード
                // 簡易実装: 全範囲をアップロード
                _vertexFlagsBuffer.SetData(_vertexFlags,
                    (int)meshInfo.VertexStart,
                    (int)meshInfo.VertexStart,
                    (int)meshInfo.VertexCount);
            }
        }

        // ============================================================
        // Level 2: カメラ更新
        // ============================================================

        /// <summary>
        /// カメラ情報を更新
        /// </summary>
        public void UpdateCamera(
            Matrix4x4 viewMatrix,
            Matrix4x4 projectionMatrix,
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            Rect viewport)
        {
            _cameraInfo[0] = new CameraInfo
            {
                ViewMatrix = viewMatrix,
                ProjectionMatrix = projectionMatrix,
                ViewProjectionMatrix = projectionMatrix * viewMatrix,
                CameraPosition = new Vector4(cameraPosition.x, cameraPosition.y, cameraPosition.z, 1),
                CameraTarget = new Vector4(cameraTarget.x, cameraTarget.y, cameraTarget.z, 1),
                ViewportSize = new Vector4(viewport.width, viewport.height, 1f / viewport.width, 1f / viewport.height),
                ClipPlanes = new Vector4(0.01f, 1000f, 0, 0)
            };

            _cameraBuffer.SetData(_cameraInfo);
        }

        /// <summary>
        /// スクリーン座標を計算（CPU側）
        /// 
        /// 【座標系の設計 - 重要】
        /// 既存システム（MeshGPURenderer.cs + Compute2D_GPU.compute）との互換性のため、
        /// スクリーン座標は viewport.x/y 付きの「グローバル座標」を使用する。
        /// 
        /// 呼び出し側では:
        /// - viewport に adjustedRect（タブオフセット付き）を渡すこと
        ///   adjustedRect.y = rect.y + tabHeight
        ///   tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y
        /// 
        /// - マウス座標も adjustedRect 座標系に変換してから比較すること
        ///   float rY = mousePos.y / rect.height;
        ///   float adjMouseY = tabHeight + rY * (rect.height - tabHeight);
        /// 
        /// GPU版（UnifiedCompute.compute）も同じ計算式を使用。
        /// </summary>
        public void ComputeScreenPositions(Matrix4x4 viewProjection, Rect viewport)
        {
            // ワールド変換が有効な場合はワールド座標を使用
            var positions = UseWorldPositions && _worldPositions != null ? _worldPositions : _positions;
            
            for (int i = 0; i < _totalVertexCount; i++)
            {
                Vector4 clipPos = viewProjection * new Vector4(
                    positions[i].x,
                    positions[i].y,
                    positions[i].z,
                    1f);

                if (clipPos.w <= 0)
                {
                    _screenPositions[i] = new Vector2(-10000, -10000); // 画面外
                    _screenPositions4[i] = new Vector4(-10000, -10000, 1f, 0f); // w=0で無効
                    _cullingResults[i] = (uint)SelectionFlags.Culled;
                }
                else
                {
                    Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);
                    float screenX = viewport.x + (ndc.x * 0.5f + 0.5f) * viewport.width;
                    float screenY = viewport.y + (1f - (ndc.y * 0.5f + 0.5f)) * viewport.height;
                    float depth = clipPos.z / clipPos.w;
                    
                    _screenPositions[i] = new Vector2(screenX, screenY);
                    _screenPositions4[i] = new Vector4(screenX, screenY, depth, 1f); // w=1で有効
                    _cullingResults[i] = 0;
                }
            }

            if (_totalVertexCount > 0)
            {
                _screenPosBuffer.SetData(_screenPositions, 0, 0, _totalVertexCount);
            }
        }

        // ============================================================
        // Level 1: ヒットテスト
        // ============================================================

        /// <summary>
        /// ヒットテスト入力を設定
        /// </summary>
        public void SetHitTestInput(Vector2 mousePosition, float hitRadius, Rect previewRect, uint hitMode = 0xF)
        {
            _hitTestInput[0] = new HitTestInput
            {
                MousePosition = mousePosition,
                HitRadius = hitRadius,
                HitMode = hitMode,
                PreviewRect = new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height)
            };

            _hitTestInputBuffer.SetData(_hitTestInput);
        }

        /// <summary>
        /// 頂点ヒットテスト（CPU実行）
        /// 一定距離内の頂点群のうち、Zが最も小さい（手前の）ものを返す
        /// </summary>
        public int FindNearestVertex(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                // 非表示チェック
                uint flags = _vertexFlags[i];
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                float dist = Vector2.Distance(mousePosition, _screenPositions[i]);
                if (dist < hitRadius)
                {
                    // 距離内の頂点の中で最も手前（Z小）を選択
                    float depth = GetVertexDepth((uint)i);
                    if (depth < nearestDepth)
                    {
                        nearestDepth = depth;
                        nearestIdx = i;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// ラインヒットテスト（CPU実行）
        /// 一定距離内の線分群のうち、Zが最も小さい（手前の）ものを返す
        /// </summary>
        public int FindNearestLine(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                // 非表示チェック
                uint flags = _lineFlags[i];
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                var line = _lines[i];
                Vector2 p1 = _screenPositions[line.V1];
                Vector2 p2 = _screenPositions[line.V2];

                float dist = DistanceToLineSegment(mousePosition, p1, p2);
                if (dist < hitRadius)
                {
                    // 距離内の線分の中で最も手前（Z小）を選択
                    // 線分の深度は両端の平均
                    float depth1 = GetVertexDepth(line.V1);
                    float depth2 = GetVertexDepth(line.V2);
                    float avgDepth = (depth1 + depth2) * 0.5f;
                    
                    if (avgDepth < nearestDepth)
                    {
                        nearestDepth = avgDepth;
                        nearestIdx = i;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// 面ヒットテスト（CPU実行、レイキャスト法）
        /// </summary>
        public int FindNearestFace(Vector2 mousePosition, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < _totalFaceCount; faceIdx++)
            {
                // 非表示チェック
                uint flags = _faceFlags[faceIdx];
                if ((flags & (uint)SelectionFlags.Hidden) != 0)
                    continue;
                
                // カリングチェック（バックフェースカリング有効時のみ）
                if (backfaceCullingEnabled && (flags & (uint)SelectionFlags.Culled) != 0)
                    continue;

                var face = _faces[faceIdx];
                int vertexCount = (int)face.VertexCount;

                if (vertexCount < 3 || vertexCount > 16)
                    continue;

                // 多角形の頂点をスクリーン座標で取得
                Vector2[] polygon = new Vector2[vertexCount];
                float totalDepth = 0;
                bool allValid = true;

                // 三角形ファンからN-gonの頂点を復元
                int triCount = vertexCount - 2;

                // 最初の頂点
                uint baseIdx = _indices[face.IndexStart];
                if (baseIdx >= _totalVertexCount) { allValid = false; }
                else
                {
                    polygon[0] = _screenPositions[baseIdx];
                    totalDepth += GetVertexDepth(baseIdx);
                }

                // 各三角形の2番目の頂点
                for (int i = 0; i < triCount && allValid; i++)
                {
                    uint idx = _indices[face.IndexStart + i * 3 + 1];
                    if (idx >= _totalVertexCount) { allValid = false; break; }
                    polygon[i + 1] = _screenPositions[idx];
                    totalDepth += GetVertexDepth(idx);
                }

                // 最後の頂点
                if (allValid && triCount > 0)
                {
                    uint lastIdx = _indices[face.IndexStart + (triCount - 1) * 3 + 2];
                    if (lastIdx >= _totalVertexCount) { allValid = false; }
                    else
                    {
                        polygon[vertexCount - 1] = _screenPositions[lastIdx];
                        totalDepth += GetVertexDepth(lastIdx);
                    }
                }

                if (!allValid)
                    continue;

                // レイキャスト法で内外判定
                if (IsPointInPolygon(mousePosition, polygon, vertexCount))
                {
                    float avgDepth = totalDepth / vertexCount;
                    if (avgDepth < nearestDepth)
                    {
                        nearestDepth = avgDepth;
                        nearestIdx = faceIdx;
                    }
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// 頂点の深度を取得
        /// _screenPositions4.z にクリップ空間の深度が保存されている
        /// </summary>
        private float GetVertexDepth(uint vertexIndex)
        {
            if (vertexIndex < _totalVertexCount && _screenPositions4 != null)
            {
                // _screenPositions4.z = clipPos.z / clipPos.w（正規化デバイス座標の深度）
                // w=0なら無効な頂点なので最大値を返す
                if (_screenPositions4[vertexIndex].w > 0.5f)
                {
                    return _screenPositions4[vertexIndex].z;
                }
            }
            return float.MaxValue; // 無効な頂点は最も奥
        }

        /// <summary>
        /// 点が多角形内にあるか判定（レイキャスト法）
        /// </summary>
        private bool IsPointInPolygon(Vector2 point, Vector2[] polygon, int vertexCount)
        {
            int crossings = 0;

            for (int i = 0; i < vertexCount; i++)
            {
                int next = (i + 1) % vertexCount;
                Vector2 v0 = polygon[i];
                Vector2 v1 = polygon[next];

                // 右方向へのレイが辺と交差するか
                if ((v0.y <= point.y && v1.y > point.y) || (v1.y <= point.y && v0.y > point.y))
                {
                    float vt = (point.y - v0.y) / (v1.y - v0.y);
                    float xIntersect = v0.x + vt * (v1.x - v0.x);
                    if (point.x < xIntersect)
                    {
                        crossings++;
                    }
                }
            }

            // 奇数回交差 = 内部
            return (crossings & 1) != 0;
        }

        /// <summary>
        /// 点と線分の距離
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lenSq = line.sqrMagnitude;

            if (lenSq < 0.000001f)
                return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lenSq);
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        /// <summary>
        /// 頂点ホバーフラグを設定
        /// </summary>
        public void SetHoverVertex(int globalVertexIndex)
        {
            // 既存の頂点ホバーをクリア
            _flagManager.ClearAllHoverFlags(_vertexFlags);

            // 新しいホバーを設定
            if (globalVertexIndex >= 0 && globalVertexIndex < _totalVertexCount)
            {
                _flagManager.SetHoverFlag(_vertexFlags, globalVertexIndex, true);
            }

            // アップロード
            if (_totalVertexCount > 0)
            {
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
        }

        /// <summary>
        /// 線分ホバーフラグを設定
        /// 同じV1-V2を持つ全エントリにホバーフラグを設定（共有エッジ対応）
        /// </summary>
        public void SetHoverLine(int globalLineIndex)
        {
            // 既存の線分ホバーをクリア
            _flagManager.ClearAllHoverFlags(_lineFlags);

            // 新しいホバーを設定
            if (globalLineIndex >= 0 && globalLineIndex < _totalLineCount)
            {
                // 指定された線分のV1-V2を取得
                var targetLine = _lines[globalLineIndex];
                uint v1 = targetLine.V1;
                uint v2 = targetLine.V2;

                // 同じV1-V2を持つ全エントリにホバーフラグを設定
                for (int i = 0; i < _totalLineCount; i++)
                {
                    var line = _lines[i];
                    if ((line.V1 == v1 && line.V2 == v2) || (line.V1 == v2 && line.V2 == v1))
                    {
                        _flagManager.SetHoverFlag(_lineFlags, i, true);
                    }
                }
            }

            // アップロード
            if (_totalLineCount > 0)
            {
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
        }

        /// <summary>
        /// 面ホバーフラグを設定
        /// </summary>
        public void SetHoverFace(int globalFaceIndex)
        {
            // 既存の面ホバーをクリア
            _flagManager.ClearAllHoverFlags(_faceFlags);

            // 新しいホバーを設定
            if (globalFaceIndex >= 0 && globalFaceIndex < _totalFaceCount)
            {
                _flagManager.SetHoverFlag(_faceFlags, globalFaceIndex, true);
            }

            // アップロード
            if (_totalFaceCount > 0)
            {
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
            }
        }

        /// <summary>
        /// 全てのホバーフラグをクリア
        /// </summary>
        public void ClearHover()
        {
            _flagManager.ClearAllHoverFlags(_vertexFlags);
            _flagManager.ClearAllHoverFlags(_lineFlags);
            _flagManager.ClearAllHoverFlags(_faceFlags);

            if (_totalVertexCount > 0)
            {
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
            if (_totalLineCount > 0)
            {
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
            if (_totalFaceCount > 0)
            {
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
            }
        }

        // ============================================================
        // インデックス変換
        // ============================================================

        /// <summary>
        /// グローバル頂点インデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalVertexIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalVertexCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.VertexStart && globalIndex < info.VertexStart + info.VertexCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.VertexStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ローカル頂点インデックスからグローバルインデックスを取得
        /// </summary>
        public int LocalToGlobalVertexIndex(int meshIndex, int localIndex)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return -1;

            var info = _meshInfos[meshIndex];
            if (localIndex < 0 || localIndex >= info.VertexCount)
                return -1;

            return (int)info.VertexStart + localIndex;
        }

        /// <summary>
        /// グローバルラインインデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalLineIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalLineCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.LineStart && globalIndex < info.LineStart + info.LineCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.LineStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// グローバル面インデックスからメッシュインデックスとローカルインデックスを取得
        /// </summary>
        public bool GlobalToLocalFaceIndex(int globalIndex, out int meshIndex, out int localIndex)
        {
            meshIndex = -1;
            localIndex = -1;

            if (globalIndex < 0 || globalIndex >= _totalFaceCount)
                return false;

            for (int i = 0; i < _meshCount; i++)
            {
                var info = _meshInfos[i];
                if (globalIndex >= info.FaceStart && globalIndex < info.FaceStart + info.FaceCount)
                {
                    meshIndex = i;
                    localIndex = globalIndex - (int)info.FaceStart;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// ラインの頂点インデックスを取得（グローバルインデックス）
        /// </summary>
        public bool GetLineVertices(int globalLineIndex, out uint v1, out uint v2)
        {
            v1 = 0;
            v2 = 0;

            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            var line = _lines[globalLineIndex];
            v1 = line.V1;
            v2 = line.V2;
            return true;
        }

        /// <summary>
        /// ラインの頂点インデックスを取得（ローカルインデックス）
        /// </summary>
        public bool GetLineVerticesLocal(int globalLineIndex, out int meshIndex, out int localV1, out int localV2)
        {
            meshIndex = -1;
            localV1 = -1;
            localV2 = -1;

            if (!GetLineVertices(globalLineIndex, out uint gV1, out uint gV2))
                return false;

            // ラインの所属メッシュを取得
            if (!GlobalToLocalLineIndex(globalLineIndex, out meshIndex, out int _))
                return false;

            // 頂点のローカルインデックスを計算
            var info = _meshInfos[meshIndex];
            localV1 = (int)(gV1 - info.VertexStart);
            localV2 = (int)(gV2 - info.VertexStart);
            
            return true;
        }

        /// <summary>
        /// 線分が補助線かどうかを取得
        /// </summary>
        public bool GetLineType(int globalLineIndex, out bool isAuxLine)
        {
            isAuxLine = false;
            
            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            uint flags = _lineFlags[globalLineIndex];
            isAuxLine = (flags & (uint)SelectionFlags.IsAuxLine) != 0;
            return true;
        }

        /// <summary>
        /// 線分の所属面インデックス（ローカル）を取得
        /// </summary>
        public bool GetLineFaceIndex(int globalLineIndex, out int localFaceIndex)
        {
            localFaceIndex = -1;
            
            if (globalLineIndex < 0 || globalLineIndex >= _totalLineCount)
                return false;

            var line = _lines[globalLineIndex];
            
            // グローバル面インデックスをローカルに変換
            if (!GlobalToLocalFaceIndex((int)line.FaceIndex, out int meshIdx, out int localIdx))
                return false;
                
            localFaceIndex = localIdx;
            return true;
        }

        // ============================================================
        // GPU計算
        // ============================================================

        private int ThreadGroups(int count) => Mathf.CeilToInt(count / 64f);

        /// <summary>
        /// GPUでスクリーン座標を計算
        /// </summary>
        public void ComputeScreenPositionsGPU(Matrix4x4 viewProjection, Rect viewport)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
            {
                ComputeScreenPositions(viewProjection, viewport);
                return;
            }

            // パラメータ設定
            _computeShader.SetMatrix("_ViewProjectionMatrix", viewProjection);
            _computeShader.SetVector("_ViewportParams", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);
            _computeShader.SetInt("_UseMirror", _mirrorEnabled ? 1 : 0);

            // バッファバインド（ワールド変換が有効な場合はWorldPositionBufferを使用）
            var posBuffer = UseWorldPositions ? _worldPositionBuffer : _positionBuffer;
            _computeShader.SetBuffer(_kernelScreenPos, "_PositionBuffer", posBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelScreenPos, "_VertexFlagsBuffer", _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_MirrorPositionBuffer", _mirrorPositionBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_MirrorScreenPositionBuffer", _mirrorScreenPosBuffer4);

            // ディスパッチ
            int groups = ThreadGroups(_totalVertexCount);
            _computeShader.Dispatch(_kernelScreenPos, groups, 1, 1);

            // 結果を読み戻してfloat2に変換
            _screenPosBuffer4.GetData(_screenPositions4, 0, 0, _totalVertexCount);
            
            for (int i = 0; i < _totalVertexCount; i++)
            {
                _screenPositions[i] = new Vector2(_screenPositions4[i].x, _screenPositions4[i].y);
            }
        }

        /// <summary>
        /// GPUで頂点ヒットテストを実行
        /// </summary>
        public void DispatchVertexHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelVertexHit, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexFlagsBuffer", _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);

            _computeShader.Dispatch(_kernelVertexHit, ThreadGroups(_totalVertexCount), 1, 1);

            // 結果を読み戻し
            _hitVertexDistBuffer.GetData(_hitVertexDistances, 0, 0, _totalVertexCount);
        }

        /// <summary>
        /// GPUで線分ヒットテストを実行
        /// </summary>
        public void DispatchLineHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalLineCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelLineHit, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelLineHit, "_LineBuffer", _lineBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineFlagsBuffer", _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineHitDistanceBuffer", _hitLineDistBuffer);

            _computeShader.Dispatch(_kernelLineHit, ThreadGroups(_totalLineCount), 1, 1);

            // 結果を読み戻し
            _hitLineDistBuffer.GetData(_hitLineDistances, 0, 0, _totalLineCount);
        }

        /// <summary>
        /// GPUで面可視性を計算
        /// 注意: ClearBuffersの後、ComputeScreenPositionsGPUの後に実行すること
        /// </summary>
        public void DispatchFaceVisibilityGPU()
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            _computeShader.SetInt("_FaceCount", _totalFaceCount);
            _computeShader.SetInt("_VertexCount", _totalVertexCount);

            _computeShader.SetBuffer(_kernelFaceVisibility, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceBuffer", _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceFlagsBuffer", _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_IndexBuffer", _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_VertexFlagsBuffer", _vertexFlagsBuffer);

            _computeShader.Dispatch(_kernelFaceVisibility, ThreadGroups(_totalFaceCount), 1, 1);
        }

        /// <summary>
        /// GPUで線分可視性を計算（面ベース）
        /// 注意: DispatchFaceVisibilityGPUの後に実行すること
        /// 入力：面、出力：線分フラグ
        /// </summary>
        public void DispatchLineVisibilityGPU()
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceBuffer", _faceBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceFlagsBuffer", _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_LineFlagsBuffer", _lineFlagsBuffer);

            // 面数でディスパッチ（面ベースのアルゴリズム）
            _computeShader.Dispatch(_kernelLineVisibility, ThreadGroups(_totalFaceCount), 1, 1);
        }

        /// <summary>
        /// GPUの頂点フラグをCPU配列に読み戻す
        /// 背面カリング結果を取得するために使用
        /// </summary>
        public void ReadBackVertexFlags()
        {
            if (_vertexFlagsBuffer == null || _totalVertexCount <= 0)
                return;
            _vertexFlagsBuffer.GetData(_vertexFlags, 0, 0, _totalVertexCount);
        }

        /// <summary>
        /// デバッグ: 頂点・線分・面のカリング状態をカウント
        /// </summary>
        public void DebugPrintCullingStats(string label = "")
        {
            const uint FLAG_CULLED = 0x00004000;
            
            // 頂点フラグを読み戻し
            _vertexFlagsBuffer.GetData(_vertexFlags, 0, 0, _totalVertexCount);
            int frontVertices = 0, backVertices = 0;
            for (int i = 0; i < _totalVertexCount; i++)
            {
                if ((_vertexFlags[i] & FLAG_CULLED) == 0)
                    frontVertices++;
                else
                    backVertices++;
            }
            
            // 面フラグを読み戻し
            _faceFlagsBuffer.GetData(_faceFlags, 0, 0, _totalFaceCount);
            int frontFaces = 0, backFaces = 0;
            for (int i = 0; i < _totalFaceCount; i++)
            {
                if ((_faceFlags[i] & FLAG_CULLED) == 0)
                    frontFaces++;
                else
                    backFaces++;
            }
            
            // 最初の頂点と面のフラグ生値を出力
            string v0Flag = _totalVertexCount > 0 ? $"0x{_vertexFlags[0]:X8}" : "N/A";
            string f0Flag = _totalFaceCount > 0 ? $"0x{_faceFlags[0]:X8}" : "N/A";
            
            Debug.Log($"[{label}] V: front={frontVertices}, back={backVertices} (v0={v0Flag}) | F: front={frontFaces}, back={backFaces} (f0={f0Flag})");
        }

        /// <summary>
        /// GPUで面ヒットテストを実行
        /// </summary>
        public void DispatchFaceHitTestGPU(Vector2 mousePosition, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelFaceHit, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceBuffer", _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceFlagsBuffer", _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_IndexBuffer", _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceHitBuffer", _faceHitBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceHitDepthBuffer", _faceHitDepthBuffer);

            _computeShader.Dispatch(_kernelFaceHit, ThreadGroups(_totalFaceCount), 1, 1);

            // 結果を読み戻し
            _faceHitBuffer.GetData(_faceHitResults, 0, 0, _totalFaceCount);
            _faceHitDepthBuffer.GetData(_faceHitDepths, 0, 0, _totalFaceCount);
        }

        /// <summary>
        /// GPU版: 最近接頂点を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の頂点のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// </summary>
        public int FindNearestVertexFromGPU(float hitRadius)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                // GPU側でhitRadius外は1e10が書き込まれている
                // 1e9より大きい値は無効とみなす（深度は通常-1〜1の範囲）
                float depth = _hitVertexDistances[i];
                if (depth < 1e9f && depth < nearestDepth)
                {
                    nearestDepth = depth;
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }



        /// <summary>
        /// GPU版: 最近接線分を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の線分のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// </summary>
        public int FindNearestLineFromGPU(float hitRadius)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                // GPU側でhitRadius外は1e10が書き込まれている
                // 1e9より大きい値は無効とみなす
                float depth = _hitLineDistances[i];
                if (depth < 1e9f && depth < nearestDepth)
                {
                    nearestDepth = depth;
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版: 最近接面を検索（ヒットバッファから）
        /// </summary>
        public int FindNearestFaceFromGPU()
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalFaceCount; i++)
            {
                if (_faceHitResults[i] > 0.5f && _faceHitDepths[i] < nearestDepth)
                {
                    nearestDepth = _faceHitDepths[i];
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版: 全ヒットテストを一括実行
        /// </summary>
        public void DispatchAllHitTestsGPU(Matrix4x4 viewProjection, Rect viewport, Vector2 mousePosition, float hitRadius)
        {
            if (!_gpuComputeAvailable)
                return;

            // 1. バッファクリア（カリングフラグを初期化）
            DispatchClearBuffersGPU();

            // 2. スクリーン座標計算
            ComputeScreenPositionsGPU(viewProjection, viewport);

            // 3. 面可視性計算（表面の頂点カリングもクリア）
            DispatchFaceVisibilityGPU();

            // 4. 線分可視性計算（面のカリングから継承）
            DispatchLineVisibilityGPU();

            // 5. ヒットテスト
            DispatchVertexHitTestGPU(mousePosition, hitRadius);
            DispatchLineHitTestGPU(mousePosition, hitRadius);
            DispatchFaceHitTestGPU(mousePosition);
        }

        /// <summary>
        /// GPUでバッファをクリア（カリングフラグを初期化）
        /// D3D11.0のUAV制限(8個)のため、2つのカーネルに分割
        /// </summary>
        public void DispatchClearBuffersGPU()
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);
            _computeShader.SetInt("_UseMirror", _mirrorEnabled ? 1 : 0);

            // カーネル1: 頂点・線分関連（UAV 6個）
            _computeShader.SetBuffer(_kernelClear, "_ScreenPositionBuffer", _screenPosBuffer4);
            _computeShader.SetBuffer(_kernelClear, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);
            _computeShader.SetBuffer(_kernelClear, "_VertexFlagsBuffer", _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelClear, "_MirrorScreenPositionBuffer", _mirrorScreenPosBuffer4);
            _computeShader.SetBuffer(_kernelClear, "_LineHitDistanceBuffer", _hitLineDistBuffer);
            _computeShader.SetBuffer(_kernelClear, "_LineFlagsBuffer", _lineFlagsBuffer);

            int maxVertexLine = Mathf.Max(_totalVertexCount, _totalLineCount);
            if (maxVertexLine > 0)
            {
                _computeShader.Dispatch(_kernelClear, ThreadGroups(maxVertexLine), 1, 1);
            }

            // カーネル2: 面関連（UAV 3個）
            _computeShader.SetBuffer(_kernelClearFace, "_FaceHitBuffer", _faceHitBuffer);
            _computeShader.SetBuffer(_kernelClearFace, "_FaceHitDepthBuffer", _faceHitDepthBuffer);
            _computeShader.SetBuffer(_kernelClearFace, "_FaceFlagsBuffer", _faceFlagsBuffer);

            if (_totalFaceCount > 0)
            {
                _computeShader.Dispatch(_kernelClearFace, ThreadGroups(_totalFaceCount), 1, 1);
            }
        }

        // ============================================================
        // Level 4: Transform Matrix 更新
        // ============================================================

        /// <summary>
        /// 変換行列をGPUバッファにアップロード
        /// ModelContext.ComputeWorldMatrices() 呼び出し後に使用
        /// ボーンを含む全MeshContextの行列をアップロード
        /// </summary>
        public void UpdateTransformMatrices(List<MeshContext> meshContexts, bool useWorldTransform)
        {
            if (meshContexts == null || _transformMatrixBuffer == null)
                return;

            int contextCount = meshContexts.Count;
            
            // 配列サイズを確保（全MeshContext分）
            if (_transformMatrices == null || _transformMatrices.Length < contextCount)
            {
                _transformMatrices = new Matrix4x4[Mathf.Max(contextCount, 256)];
            }

            // バッファサイズが足りない場合は再作成
            if (_transformMatrixBuffer.count < contextCount)
            {
                _transformMatrixBuffer?.Release();
                _transformMatrixBuffer = new ComputeBuffer(Mathf.Max(contextCount, 256), sizeof(float) * 16);
            }

            // 全MeshContext（ボーン含む）の変換行列を設定
            for (int i = 0; i < contextCount; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null)
                {
                    _transformMatrices[i] = Matrix4x4.identity;
                    continue;
                }

                if (useWorldTransform)
                {
                    // スキニング用: WorldMatrix × BindPose
                    _transformMatrices[i] = ctx.SkinningMatrix;
                }
                else
                {
                    _transformMatrices[i] = ctx.LocalMatrix;
                }
            }

            // GPUにアップロード
            if (contextCount > 0)
            {
                _transformMatrixBuffer.SetData(_transformMatrices, 0, 0, contextCount);
            }
        }

        /// <summary>
        /// 単一メッシュの変換行列を更新
        /// </summary>
        public void UpdateTransformMatrix(int meshIndex, Matrix4x4 matrix)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return;

            if (_transformMatrices == null || _transformMatrices.Length <= meshIndex)
                return;

            _transformMatrices[meshIndex] = matrix;
            
            // GPUにアップロード（部分更新）
            _transformMatrixBuffer?.SetData(_transformMatrices, meshIndex, meshIndex, 1);
        }

        // ============================================================
        // TransformVertices カーネル実行
        // ============================================================

        private int _kernelTransformVertices = -1;
        private int _kernelExpandVertices = -1;

        /// <summary>
        /// TransformVerticesカーネルを実行
        /// ローカル座標をワールド座標に変換
        /// </summary>
        /// <param name="useWorldTransform">true: ワールド変換適用, false: ローカル座標コピー</param>
        /// <param name="transformNormals">true: 法線も変換</param>
        /// <param name="readbackToCPU">true: 結果をCPU側に読み戻す（非推奨、後方互換用）</param>
        public void DispatchTransformVertices(bool useWorldTransform, bool transformNormals = false, bool readbackToCPU = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            if (_totalVertexCount == 0)
                return;

            // UseWorldPositionsフラグを設定
            UseWorldPositions = useWorldTransform;

            // カーネルを取得（初回のみ）
            if (_kernelTransformVertices < 0)
            {
                _kernelTransformVertices = _computeShader.FindKernel("TransformVertices");
                if (_kernelTransformVertices < 0)
                {
                    Debug.LogWarning("[UnifiedBufferManager] TransformVertices kernel not found");
                    return;
                }
            }

            // バッファをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_PositionBuffer", _positionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_WorldPositionBuffer", _worldPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_TransformMatrixBuffer", _transformMatrixBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_BoneWeightsBuffer", _boneWeightsBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_BoneIndicesBuffer", _boneIndicesBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_NormalBuffer", _normalBuffer);
            // WorldNormalBufferは未実装のため、ダミーとしてNormalBufferをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_WorldNormalBuffer", _normalBuffer);
            
            // ミラーバッファをバインド
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorPositionBuffer", _mirrorPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_SkinnedMirrorPositionBuffer", _skinnedMirrorPositionBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorBoneWeightsBuffer", _mirrorBoneWeightsBuffer);
            _computeShader.SetBuffer(_kernelTransformVertices, "_MirrorBoneIndicesBuffer", _mirrorBoneIndicesBuffer);

            // パラメータを設定
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_UseWorldTransform", useWorldTransform ? 1 : 0);
            _computeShader.SetInt("_TransformNormals", transformNormals ? 1 : 0);
            _computeShader.SetInt("_ComputeMirror", _mirrorEnabled ? 1 : 0);

            // ディスパッチ
            int threadGroups = Mathf.CeilToInt(_totalVertexCount / 256.0f);
            _computeShader.Dispatch(_kernelTransformVertices, threadGroups, 1, 1);

            // CPU側に読み戻し（描画用）- 非推奨、後方互換用
            if (readbackToCPU && useWorldTransform)
            {
                if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                    _worldPositions = new Vector3[_totalVertexCount];
                
                _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
            }
        }

        /// <summary>
        /// ExpandVerticesカーネルを実行
        /// ワールド変換済み頂点をUV展開済み配列に展開
        /// </summary>
        /// <param name="transformNormals">法線も展開するか</param>
        public void DispatchExpandVertices(bool transformNormals = false)
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            if (_totalExpandedVertexCount == 0)
                return;

            // 必要なバッファがすべて存在するか確認
            if (_expandedToOriginalBuffer == null || 
                _expandedPositionBuffer == null || 
                _expandedNormalBuffer == null ||
                _worldPositionBuffer == null ||
                _normalBuffer == null)
            {
                Debug.LogWarning("[UnifiedBufferManager] ExpandVertices: Required buffers not initialized");
                return;
            }

            // カーネルを取得（初回のみ）
            if (_kernelExpandVertices < 0)
            {
                _kernelExpandVertices = _computeShader.FindKernel("ExpandVertices");
                if (_kernelExpandVertices < 0)
                {
                    Debug.LogWarning("[UnifiedBufferManager] ExpandVertices kernel not found");
                    return;
                }
            }

            // バッファをバインド
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedToOriginalBuffer", _expandedToOriginalBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_WorldPositionBuffer", _worldPositionBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedPositionBuffer", _expandedPositionBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_NormalBuffer", _normalBuffer);
            _computeShader.SetBuffer(_kernelExpandVertices, "_WorldNormalBuffer", _normalBuffer);  // TODO: 変換済み法線
            _computeShader.SetBuffer(_kernelExpandVertices, "_ExpandedNormalBuffer", _expandedNormalBuffer);

            // パラメータを設定
            _computeShader.SetInt("_ExpandedVertexCount", _totalExpandedVertexCount);
            _computeShader.SetInt("_TransformNormals", transformNormals ? 1 : 0);

            // ディスパッチ
            int threadGroups = Mathf.CeilToInt(_totalExpandedVertexCount / 256.0f);
            _computeShader.Dispatch(_kernelExpandVertices, threadGroups, 1, 1);
        }

        /// <summary>
        /// ワールド座標バッファの内容を取得（デバッグ用）
        /// </summary>
        public Vector3[] GetWorldPositions()
        {
            if (_worldPositionBuffer == null || _totalVertexCount == 0)
                return null;

            if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                _worldPositions = new Vector3[_totalVertexCount];

            _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
            return _worldPositions;
        }

        // 展開済み頂点のCPU配列（ReadBack用）
        private Vector3[] _expandedPositions;

        /// <summary>
        /// 展開済み頂点座標バッファの内容を取得
        /// </summary>
        public Vector3[] GetExpandedPositions()
        {
            if (_expandedPositionBuffer == null || _totalExpandedVertexCount == 0)
                return null;

            if (_expandedPositions == null || _expandedPositions.Length < _totalExpandedVertexCount)
                _expandedPositions = new Vector3[_totalExpandedVertexCount];

            _expandedPositionBuffer.GetData(_expandedPositions, 0, 0, _totalExpandedVertexCount);
            return _expandedPositions;
        }
    }
}
