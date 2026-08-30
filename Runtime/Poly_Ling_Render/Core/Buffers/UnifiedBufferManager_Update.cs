// Assets/Editor/Poly_Ling/Core/Buffers/UnifiedBufferManager_Update.cs
// 統合バッファ管理クラス - 更新処理
// 選択、カメラ、ヒットテストの更新

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
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
        /// v2.1: 複数メッシュ選択をModelContextから同期
        /// Context→Unified変換を正しく行う
        /// </summary>
        public void SyncSelectionFromModel(Poly_Ling.Context.ModelContext model)
        {
            if (model == null) return;
            
            _modelContext = model;
            
            // unified→context逆引きマップを構築
            _unifiedToContextMap.Clear();
            foreach (var ctxIdx in model.SelectedDrawableMeshIndices)
            {
                int unifiedIdx = ContextToUnifiedMeshIndex(ctxIdx);
                if (unifiedIdx >= 0)
                {
                    _unifiedToContextMap[unifiedIdx] = ctxIdx;
                }
            }
            
            // ContextインデックスをUnifiedインデックスに変換して同期
            _flagManager.SelectedUnifiedMeshIndices.Clear();
            foreach (var kv in _unifiedToContextMap)
            {
                _flagManager.SelectedUnifiedMeshIndices.Add(kv.Key);
            }
            
            // 先頭メッシュも同期。
            //
            // 【-1 に落とす理由】
            //   不可視・頂点0のメッシュは GPU バッファに載らない
            //   （UnifiedBufferManager_Build.ShouldIncludeInBuffers）。
            //   そういうオブジェクトを選ぶと ContextToUnifiedMeshIndex が -1 を返す。
            //   従来はそのとき代入を飛ばしていたため、前に選んでいたメッシュの
            //   ActiveMeshIndex / SelectedMeshIndex が残り、選択したはずのものと
            //   画面上の選択が食い違っていた。何も選んでいない状態からだと
            //   ActiveMeshIndex の初期値 0 が残り、0 番のメッシュが選択に見える。
            //   載っていない＝描画上は選択なし、として明示的に落とす。
            //   FlagManager は -1 を「該当なし」として扱う（比較のみ）。
            int firstCtx = model.FirstMeshIndex;
            int firstUnified = (firstCtx >= 0) ? ContextToUnifiedMeshIndex(firstCtx) : -1;

            _flagManager.ActiveMeshIndex   = firstUnified;
            _flagManager.SelectedMeshIndex = firstUnified;
        }
        
        // v2.1: ModelContext参照（複数メッシュ選択用）
        private Poly_Ling.Context.ModelContext _modelContext;
        // unified→contextインデックスの逆引きマップ
        private Dictionary<int, int> _unifiedToContextMap = new Dictionary<int, int>();

        /// <summary>
        /// 全頂点の選択フラグを更新
        /// </summary>
        public void UpdateAllSelectionFlags()
        {
            for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
            {
                var meshInfo = _meshInfos[meshIdx];

                // 選択メッシュならMeshContextから選択頂点を取得
                HashSet<int> selectedVertices = null;
                if (_modelContext != null && _unifiedToContextMap.TryGetValue(meshIdx, out int ctxIdx))
                {
                    var meshContext = _modelContext.GetMeshContext(ctxIdx);
                    if (meshContext != null && meshContext.SelectedVertices.Count > 0)
                    {
                        selectedVertices = meshContext.SelectedVertices;
                    }
                }

                SelectionFlags hierarchyFlags = _flagManager.ComputeHierarchyFlags(
                    (int)meshInfo.ModelIndex, meshIdx);

                for (uint v = 0; v < meshInfo.VertexCount; v++)
                {
                    uint globalIdx = meshInfo.VertexStart + v;
                    if (globalIdx >= _totalVertexCount)
                        break;

                    uint flags = _vertexFlags[globalIdx];
                    flags &= ~((uint)SelectionFlags.HierarchyMask | (uint)SelectionFlags.ElementSelectionMask);

                    flags |= (uint)hierarchyFlags;

                    if (selectedVertices != null && selectedVertices.Contains((int)v))
                    {
                        flags |= (uint)SelectionFlags.VertexSelected;
                    }

                    _vertexFlags[globalIdx] = flags;
                }
            }

            // GPUにアップロード
            if (_totalVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }

            // ライン・面フラグも更新
            UpdateAllLineSelectionFlags();
            UpdateAllFaceSelectionFlags();

            // Hidden / Locked も同じ場で立て直す。
            // この関数は PrepareWireframeAndVertices（MeshSceneRenderer）からも呼ばれ、
            // 各フラグ配列を GPU へ再転送する。可視性を別経路で書いてからここを通ると、
            // カリング済みの結果と転送順がずれて反映が1フレーム遅れる。
            // 順序に依存しないよう、選択と可視性を必ずひと組で確定させる。
            UpdateAllVisibilityFlags();
        }

        /// <summary>
        /// v2.1: 個別頂点の選択フラグを設定（複数メッシュ選択用）
        /// </summary>
        public void SetVertexSelectedFlag(int globalVertexIndex, bool selected)
        {
            if (globalVertexIndex < 0 || globalVertexIndex >= _totalVertexCount)
                return;
            
            if (selected)
            {
                _vertexFlags[globalVertexIndex] |= (uint)SelectionFlags.VertexSelected;
            }
            else
            {
                _vertexFlags[globalVertexIndex] &= ~(uint)SelectionFlags.VertexSelected;
            }
        }
        
        /// <summary>
        /// v2.1: 全頂点の選択フラグをクリア（複数メッシュ選択用）
        /// </summary>
        public void ClearAllVertexSelectedFlags()
        {
            for (int i = 0; i < _totalVertexCount; i++)
            {
                _vertexFlags[i] &= ~(uint)SelectionFlags.VertexSelected;
            }
        }
        
        /// <summary>
        /// v2.1: 頂点フラグをGPUにアップロード
        /// </summary>
        public void UploadVertexFlags()
        {
            if (_totalVertexCount > 0 && _vertexFlagsBuffer != null)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
        }

        /// <summary>
        /// ラインの選択フラグを更新
        /// 
        /// 【複数メッシュ対応】
        /// - プライマリメッシュ: _flagManager.SelectionStateを見る
        /// - セカンダリメッシュ: MeshContextのSelectedEdges/SelectedLinesを見る
        /// </summary>
        private void UpdateAllLineSelectionFlags()
        {
            for (int lineIdx = 0; lineIdx < _totalLineCount; lineIdx++)
            {
                var line = _lines[lineIdx];
                int meshIdx = (int)line.MeshIndex;

                // 既存フラグから選択フラグをクリア
                uint flags = _lineFlags[lineIdx];
                flags &= ~((uint)SelectionFlags.HierarchyMask | (uint)SelectionFlags.EdgeSelected | (uint)SelectionFlags.LineSelected);

                // 階層フラグ
                flags |= (uint)_flagManager.ComputeHierarchyFlags((int)line.ModelIndex, meshIdx);

                bool isMeshSelected = (flags & (uint)SelectionFlags.MeshSelected) != 0;

                // 選択メッシュならMeshContextからエッジ/線分選択を取得
                if (isMeshSelected && _modelContext != null && _unifiedToContextMap.TryGetValue(meshIdx, out int ctxIdx))
                {
                    var meshContext = _modelContext.GetMeshContext(ctxIdx);
                    if (meshContext != null)
                    {
                        bool isAuxLine = (flags & (uint)SelectionFlags.IsAuxLine) != 0;
                        var meshInfo = _meshInfos[line.MeshIndex];

                        if (isAuxLine)
                        {
                            // line.FaceIndexはグローバル → ローカルに変換
                            int localFaceIndex = (int)(line.FaceIndex - meshInfo.FaceStart);
                            if (meshContext.SelectedLines.Contains(localFaceIndex))
                            {
                                flags |= (uint)SelectionFlags.LineSelected;
                            }
                        }
                        else
                        {
                            int localV1 = (int)(line.V1 - meshInfo.VertexStart);
                            int localV2 = (int)(line.V2 - meshInfo.VertexStart);
                            var pair = new VertexPair(localV1, localV2);
                            if (meshContext.SelectedEdges.Contains(pair))
                            {
                                flags |= (uint)SelectionFlags.EdgeSelected;
                            }
                        }
                    }
                }

                _lineFlags[lineIdx] = flags;
            }

            // GPUにアップロード
            if (_totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_lineFlagsBuffer", _lineFlagsBuffer, 0);
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
        }

        /// <summary>
        /// 面の選択フラグを更新
        /// 選択メッシュのMeshContext.SelectedFacesからフラグを反映
        /// </summary>
        private void UpdateAllFaceSelectionFlags()
        {
            for (int meshIdx = 0; meshIdx < _meshCount; meshIdx++)
            {
                var meshInfo = _meshInfos[meshIdx];

                // 選択メッシュならMeshContextから選択面を取得
                HashSet<int> selectedFaces = null;
                if (_modelContext != null && _unifiedToContextMap.TryGetValue(meshIdx, out int ctxIdx))
                {
                    var meshContext = _modelContext.GetMeshContext(ctxIdx);
                    if (meshContext != null && meshContext.SelectedFaces.Count > 0)
                    {
                        selectedFaces = meshContext.SelectedFaces;
                    }
                }

                for (uint f = 0; f < meshInfo.FaceCount; f++)
                {
                    uint globalIdx = meshInfo.FaceStart + f;
                    if (globalIdx >= _totalFaceCount)
                        break;

                    uint flags = _faceFlags[globalIdx];

                    // 階層フラグと面選択フラグをクリアして再設定
                    flags &= ~((uint)SelectionFlags.HierarchyMask | (uint)SelectionFlags.FaceSelected);
                    flags |= (uint)_flagManager.ComputeHierarchyFlags((int)meshInfo.ModelIndex, meshIdx);

                    if (selectedFaces != null && selectedFaces.Contains((int)f))
                    {
                        flags |= (uint)SelectionFlags.FaceSelected;
                    }

                    _faceFlags[globalIdx] = flags;
                }
            }

            // GPUにアップロード
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_faceFlagsBuffer", _faceFlagsBuffer, 0);
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
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

            Poly_Ling.Diagnostics.PLCamDbg.Wr("_cameraBuffer", _cameraBuffer, 0);
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
                }
                else
                {
                    Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);
                    float screenX = viewport.x + (ndc.x * 0.5f + 0.5f) * viewport.width;
                    float screenY = viewport.y + (1f - (ndc.y * 0.5f + 0.5f)) * viewport.height;
                    float depth = clipPos.z / clipPos.w;

                    _screenPositions[i] = new Vector2(screenX, screenY);
                    _screenPositions4[i] = new Vector4(screenX, screenY, depth, 1f); // w=1で有効
                }
            }

            // 【_screenPosBuffer への SetData を撤去した理由】
            //   このバッファを ComputeShader.SetBuffer へ渡している箇所が 0 件で、
            //   シェーダーからも参照されていなかった。GPU が読むのは
            //   _screenPosBuffer4 と per-slot バッファで、そちらは
            //   ComputeScreenPositionsGPU が GPU 側で直接埋める。
            //   _cullingResults も同じ理由で撤去した（公開プロパティの呼出元 0 件）。
        }

        // ============================================================
        // Level 1: ヒットテスト
        // ============================================================

        /// <summary>
        /// ホバー要素を決めるときの「スクリーン距離の許容差」（ピクセル）。
        ///
        /// 【なぜ必要か】GPU 側のヒットテスト (UnifiedCompute.compute の
        /// ComputeVertexHitTest) は、スクリーン距離を「ヒット半径内かどうか」の
        /// 可否判定にしか使わず、半径内の要素には深度だけを書き込む。そのため
        /// 順位付けを深度だけで行うと、カーソルから 1px の頂点よりも 9px 離れた
        /// 手前の頂点が勝つ。密なメッシュではカーソルが 1px 動いただけで
        /// 半径 10px の円に出入りする要素集合が変わり、ホバーが画面上の遠い
        /// 別要素へ飛ぶ。押下時の微小なブレでも起きるため、掴む対象が
        /// 見えていたものと食い違う。
        ///
        /// そこでスクリーン距離をこの幅で量子化した「バンド」を第一キー、
        /// 深度を第二キーとして順位付けする。すなわち、この幅の中の差は
        /// 人間が識別できないので従来どおり手前を優先し、この幅を超えて
        /// 明らかに近い要素があればそちらを優先する。
        ///
        /// ヒット半径以上の値にすると全要素が同一バンドになり、
        /// 深度のみで選ぶ従来の挙動へ完全に戻る。
        /// </summary>
        public float HoverDistanceTolerance { get; set; } = 3f;

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

            Poly_Ling.Diagnostics.PLCamDbg.Wr("_hitTestInputBuffer", _hitTestInputBuffer, 0);
            _hitTestInputBuffer.SetData(_hitTestInput);
        }

        /// <summary>
        /// 頂点ヒットテスト（CPU実行）
        /// 一定距離内の頂点群のうち、Zが最も小さい（手前の）ものを返す
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する頂点のみ候補とする。
        /// </summary>
        public int FindNearestVertex(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                uint flags = _vertexFlags[i];

                // 選択メッシュに属さない頂点はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
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
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する線分のみ候補とする。
        /// </summary>
        public int FindNearestLine(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                uint flags = _lineFlags[i];

                // 選択メッシュに属さない線分はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
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
        ///
        /// 【フィルタ】選択中メッシュ（MeshSelected）に属する面のみ候補とする。
        /// </summary>
        public int FindNearestFace(Vector2 mousePosition, bool backfaceCullingEnabled = true)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < _totalFaceCount; faceIdx++)
            {
                uint flags = _faceFlags[faceIdx];

                // 選択メッシュに属さない面はスキップ
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0)
                    continue;

                // 非表示チェック
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
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
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
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_lineFlagsBuffer", _lineFlagsBuffer, 0);
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
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_faceFlagsBuffer", _faceFlagsBuffer, 0);
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
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_vertexFlagsBuffer", _vertexFlagsBuffer, 0);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }
            if (_totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_lineFlagsBuffer", _lineFlagsBuffer, 0);
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_faceFlagsBuffer", _faceFlagsBuffer, 0);
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
        /// メッシュインデックス(unified)とローカル面インデックスからグローバル面インデックスを取得
        /// </summary>
        public int LocalToGlobalFaceIndex(int meshIndex, int localFaceIndex)
        {
            if (meshIndex < 0 || meshIndex >= _meshCount)
                return -1;

            var info = _meshInfos[meshIndex];
            if (localFaceIndex < 0 || localFaceIndex >= info.FaceCount)
                return -1;

            return (int)info.FaceStart + localFaceIndex;
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
        /// GPU でスクリーン座標を計算し、slot 専用バッファへ書き込む。
        /// </summary>
        /// <param name="readback">
        /// GPU → CPU の同期読み戻し（<c>ComputeBuffer.GetData</c>）を行うかどうか。
        ///
        /// 【必ず明示すること。既定値を付けない】
        ///   GetData はコマンドキューのフラッシュと GPU 完了待ちを伴う。呼び出しごとに
        ///   全頂点ぶん（float4 × 頂点数）を転送し、そのあと CPU で全頂点ループを
        ///   回して _screenPositions へ展開する。結果を CPU が読まない経路で
        ///   これを行うと、転送もループも丸ごと無駄になる。
        ///
        /// 【true にしてよい経路（2 つだけ）】
        ///   1. ホバーのヒットテスト（UnifiedMeshSystem.ProcessMouseUpdate）
        ///      FindNearestVertexFromGPU / FindNearestLineFromGPU が
        ///      _screenPositions を読む。
        ///   2. PresentAll 末尾のアクティブ slot 最終確定
        ///      矩形選択・投げ縄選択が GetScreenPositions() を読む。
        ///
        /// 【false にする経路】
        ///   表示用カリング（DispatchCullingForDisplay の per-slot 呼び出し、
        ///   MeshSceneRenderer の cullSubmit）。後続は GPU 内で完結する
        ///   FaceVisibility / LineVisibility / ApplyMirrorCull だけで、
        ///   CPU 側は結果を一切参照しない。
        ///
        /// 【_screenPositions は 1 本しかないことに注意】
        ///   slot ごとの配列ではないので、複数 slot が readback: true で呼ぶと
        ///   最後の 1 回しか残らない。読み戻す slot は常に 1 つに保つこと。
        /// </param>
        public void ComputeScreenPositionsGPU(
            Matrix4x4 viewProjection, Rect viewport, int slot, string dbgSrc, bool readback)
        {
            Poly_Ling.Diagnostics.PLCamDbg.Cap("v=" + _totalVertexCount + "/" + _vertexCapacity
                + " l=" + _totalLineCount + "/" + _lineCapacity
                + " f=" + _totalFaceCount + "/" + _faceCapacity
                + " mesh=" + (_meshInfos == null ? -1 : _meshInfos.Length)
                + " sp4=" + (_screenPositions4 == null ? -1 : _screenPositions4.Length));
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
            {
                // CPU フォールバックは readback の指定に関係なく _screenPositions を埋める。
                // CPU で計算する以上、結果は最初から CPU 側にあるため。
                ComputeScreenPositions(viewProjection, viewport);
                return;
            }

            // slot バッファは Initialize で CullingSlotCount 本を必ず確保する。
            // null になるのは範囲外の slot 番号を渡されたときだけで、それは呼び出し側の誤り。
            // 旧コードは _screenPosBuffer4 へフォールバックしていたが、そのバッファは
            // 読み手が 0 件で、書いても誰も使わなかった（撤去済み）。
            var screenBuf = GetSlotScreenPosBuffer(slot);
            if (screenBuf == null)
            {
                Debug.LogError($"[ComputeScreenPositionsGPU] slot={slot} のスクリーン座標バッファが無い。src={dbgSrc}");
                return;
            }

            // パラメータ設定
            _computeShader.SetMatrix("_ViewProjectionMatrix", viewProjection);
            _computeShader.SetVector("_ViewportParams", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            var posBuffer = UseWorldPositions ? _worldPositionBuffer : _positionBuffer;
            _computeShader.SetBuffer(_kernelScreenPos, "_PositionBuffer",            posBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_ScreenPositionBuffer",      screenBuf);
            _computeShader.SetBuffer(_kernelScreenPos, "_VertexFlagsBuffer",         _vertexFlagsBuffer);

            int groups = ThreadGroups(_totalVertexCount);
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("ScreenPos", 0, null, groups);
            _computeShader.Dispatch(_kernelScreenPos, groups, 1, 1);

            // 読み戻しを求められていない呼び出しはここで終わる。
            // 表示用カリングは後続の FaceVisibility / LineVisibility / ApplyMirrorCull が
            // GPU 内で slot バッファを読むだけなので、CPU へ戻す必要がない。
            if (!readback)
                return;

            // CPU 読み戻し（ホバー・ヒットテスト・CommitBoxSelect 用）
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G1 before n=" + _totalVertexCount + " buf=" + screenBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + screenBuf.count + " arr=" + _screenPositions4.Length + " slot=" + slot + " src=" + dbgSrc);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                screenBuf.GetData(_screenPositions4, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G1 after");
            for (int i = 0; i < _totalVertexCount; i++)
                _screenPositions[i] = new Vector2(_screenPositions4[i].x, _screenPositions4[i].y);
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

            // 参照するのはヒットテスト専用 slot。表示用 slot 0〜3 と共用してはならない。
            _computeShader.SetBuffer(_kernelVertexHit, "_ScreenPositionBuffer",   GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexFlagsBuffer",       _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexCulledBuffer",      GetVertexCulledBuffer(HitTestSlot) ?? _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexHit, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("VertexHit", 0, null, ThreadGroups(_totalVertexCount));
            _computeShader.Dispatch(_kernelVertexHit, ThreadGroups(_totalVertexCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G2 before n=" + _totalVertexCount + " buf=" + _hitVertexDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _hitVertexDistBuffer.count + " arr=" + _hitVertexDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _hitVertexDistBuffer.GetData(_hitVertexDistances, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G2 after");
        }

        /// <summary>
        /// GPUで頂点ヒットテストを実行（吸着用・メッシュ選択を無視）。
        ///
        /// 出力は _snapHitVertexDistBuffer で、通常のホバー結果には影響しない。
        /// スクリーン座標とカリングフラグは DispatchVertexHitTestGPU と同じものを
        /// 使うため、必ず ComputeScreenPositionsGPU / DispatchFaceVisibilityGPU の
        /// 後に呼ぶこと。
        ///
        /// 【コスト】頂点数ぶんの GetData が 1 回増える。
        /// 呼び出し側（UnifiedMeshSystem）は必要なときだけ実行すること。
        /// </summary>
        public void DispatchVertexSnapHitTestGPU(Vector2 mousePosition, float hitRadius, bool backfaceCullingEnabled = true)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalVertexCount <= 0)
                return;

            _computeShader.SetVector("_MousePosition", mousePosition);
            _computeShader.SetFloat("_HitRadius", hitRadius);
            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_EnableBackfaceCulling", backfaceCullingEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelVertexSnapHit, "_ScreenPositionBuffer",       GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexFlagsBuffer",           _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexCulledBuffer",          GetVertexCulledBuffer(HitTestSlot) ?? _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelVertexSnapHit, "_VertexSnapHitDistanceBuffer", _snapHitVertexDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("VertexSnapHit", 0, null, ThreadGroups(_totalVertexCount));
            _computeShader.Dispatch(_kernelVertexSnapHit, ThreadGroups(_totalVertexCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G3 before n=" + _totalVertexCount + " buf=" + _snapHitVertexDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _snapHitVertexDistBuffer.count + " arr=" + _snapHitVertexDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _snapHitVertexDistBuffer.GetData(_snapHitVertexDistances, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G3 after");
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

            _computeShader.SetBuffer(_kernelLineHit, "_ScreenPositionBuffer", GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelLineHit, "_LineBuffer",           _lineBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineFlagsBuffer",      _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineCulledBuffer",     GetLineCulledBuffer(HitTestSlot) ?? _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineHit, "_LineHitDistanceBuffer",_hitLineDistBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("LineHit", 0, null, ThreadGroups(_totalLineCount));
            _computeShader.Dispatch(_kernelLineHit, ThreadGroups(_totalLineCount), 1, 1);

            // 結果を読み戻し
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G4 before n=" + _totalLineCount + " buf=" + _hitLineDistBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _hitLineDistBuffer.count + " arr=" + _hitLineDistances.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _hitLineDistBuffer.GetData(_hitLineDistances, 0, 0, _totalLineCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G4 after");
        }

        /// <summary>
        /// GPUで面可視性を計算
        /// 注意: ClearBuffersの後、ComputeScreenPositionsGPUの後に実行すること
        /// </summary>
        /// <param name="slot">
        /// カリングスロット。表示用は 0〜ViewportSlotCount-1、ヒットテストは HitTestSlot。
        /// 既定値は付けない。付けると呼び出し側が slot を意識せず書けてしまい、
        /// 表示用 slot 0 をヒットテストが上書きする事故に戻る。
        /// </param>
        public void DispatchFaceVisibilityGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            var screenBuf  = GetSlotScreenPosBuffer(slot);
            if (screenBuf == null) return;
            var vCulledBuf = GetVertexCulledBuffer(slot);
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (vCulledBuf == null || fCulledBuf == null) return;

            _computeShader.SetInt("_FaceCount",   _totalFaceCount);
            _computeShader.SetInt("_VertexCount",  _totalVertexCount);

            _computeShader.SetBuffer(_kernelFaceVisibility, "_ScreenPositionBuffer", screenBuf);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceBuffer",           _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceFlagsBuffer",      _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_FaceCulledBuffer",     fCulledBuf);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_IndexBuffer",          _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_VertexFlagsBuffer",    _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceVisibility, "_VertexCulledBuffer",   vCulledBuf);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("FaceVisibility", 0, null, ThreadGroups(_totalFaceCount));
            _computeShader.Dispatch(_kernelFaceVisibility, ThreadGroups(_totalFaceCount), 1, 1);
        }

        /// <summary>
        /// GPUで線分可視性を計算（面ベース）
        /// 注意: DispatchFaceVisibilityGPUの後に実行すること
        /// 入力：面、出力：線分フラグ
        /// </summary>
        /// <param name="slot">カリングスロット（0〜CullingSlotCount-1）</param>
        /// <param name="slot">既定値を付けない理由は DispatchFaceVisibilityGPU を参照。</param>
        public void DispatchLineVisibilityGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null || _totalFaceCount <= 0)
                return;

            var lCulledBuf = GetLineCulledBuffer(slot);
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (lCulledBuf == null || fCulledBuf == null) return;

            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceBuffer",       _faceBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceFlagsBuffer",  _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_FaceCulledBuffer", fCulledBuf);
            _computeShader.SetBuffer(_kernelLineVisibility, "_LineFlagsBuffer",  _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelLineVisibility, "_LineCulledBuffer", lCulledBuf);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("LineVisibility", 0, null, ThreadGroups(_totalFaceCount));
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
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G7 before n=" + _totalVertexCount + " buf=" + _vertexFlagsBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _vertexFlagsBuffer.count + " arr=" + _vertexFlags.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _vertexFlagsBuffer.GetData(_vertexFlags, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G7 after");
        }

        /// <summary>
        /// 指定スロットの GPU 頂点カリングバッファ (_VertexCulledBuffer) を
        /// CPU キャッシュ配列 (_vertexCulledCache) に読み戻す。
        ///
        /// 矩形選択・投げ縄選択の CPU ループで「表面の面に属さない頂点」を除外する
        /// ために使用。_vertexFlags は CPU 側の編集対象のためカリング情報を混ぜられない
        /// (CPU→GPU の SetData で消失する) ので、独立したキャッシュを持つ。
        ///
        /// 呼び出しタイミング: 矩形/投げ縄選択の確定直前 (OnLeftDragEnd 内)。
        /// GPU 計算 (DispatchFaceVisibilityGPU) は別経路で毎フレーム走っているので、
        /// ReadBack するだけで最新の結果が得られる。
        /// </summary>
        public void ReadBackVertexCulled(int slot = 0)
        {
            if (_totalVertexCount <= 0) return;
            var vCulledBuf = GetVertexCulledBuffer(slot);
            if (vCulledBuf == null) return;

            if (_vertexCulledCache == null || _vertexCulledCache.Length < _totalVertexCount)
                _vertexCulledCache = new uint[_totalVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G8 before n=" + _totalVertexCount + " buf=" + vCulledBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + vCulledBuf.count + " arr=" + _vertexCulledCache.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                vCulledBuf.GetData(_vertexCulledCache, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G8 after");
        }

        /// <summary>
        /// 指定スロットの GPU 面カリングバッファ (_FaceCulledBuffer) を
        /// CPU キャッシュ (_faceCulledCache) に読み戻す。ReadBackVertexCulled と同型。
        /// 呼出前に該当スロットの ComputeScreenPositions + DispatchFaceVisibility を実行しておくこと。
        /// </summary>
        public void ReadBackFaceCulled(int slot = 0)
        {
            if (_totalFaceCount <= 0) return;
            var fCulledBuf = GetFaceCulledBuffer(slot);
            if (fCulledBuf == null) return;

            if (_faceCulledCache == null || _faceCulledCache.Length < _totalFaceCount)
                _faceCulledCache = new uint[_totalFaceCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G9 before n=" + _totalFaceCount + " buf=" + fCulledBuf.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + fCulledBuf.count + " arr=" + _faceCulledCache.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                fCulledBuf.GetData(_faceCulledCache, 0, 0, _totalFaceCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G9 after");
        }

        // 【DebugPrintCullingStats を撤去した理由】 2026-08-28
        //   呼出元 0 件。内部に _vertexFlagsBuffer / _faceFlagsBuffer の
        //   同期 GetData を 2 本抱えたまま死んでいた（診断マーク G10 / G11）。
        //   復活させる場合は同期読み戻しを伴うことを踏まえて呼ぶこと。

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

            _computeShader.SetBuffer(_kernelFaceHit, "_ScreenPositionBuffer", GetSlotScreenPosBuffer(HitTestSlot));
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceBuffer",          _faceBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceFlagsBuffer",     _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceCulledBuffer",    GetFaceCulledBuffer(HitTestSlot) ?? _faceFlagsBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_IndexBuffer",         _indexBuffer);
            _computeShader.SetBuffer(_kernelFaceHit, "_FaceHitBuffer",       _faceHitBuffer);

            Poly_Ling.Diagnostics.PLCamDbg.Dsp("FaceHit", 0, null, ThreadGroups(_totalFaceCount));
            _computeShader.Dispatch(_kernelFaceHit, ThreadGroups(_totalFaceCount), 1, 1);

            // 結果を読み戻し（旧 G5 / G6 の 2 本を 1 本に統合。診断マークは G5 に統一）
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G5 before n=" + _totalFaceCount + " buf=" + _faceHitBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _faceHitBuffer.count + " arr=" + _faceHit.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData && Poly_Ling.Diagnostics.PLCamDbg.SwHotGetData)
                _faceHitBuffer.GetData(_faceHit, 0, 0, _totalFaceCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G5 after");
        }

        /// <summary>
        /// GPU版: 最近接頂点を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の頂点のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// </summary>
        /// <summary>
        /// GPU版: 最近接頂点を検索（深度バッファから）
        /// GPU側で距離がhitRadius内の頂点のみ深度を書き込んでいる
        /// hitRadius外は1e10が書き込まれている
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュは1e10）
        /// </summary>
        public int FindNearestVertexFromGPU(float hitRadius)
        {
            Vector2 mouse = _hitTestInput != null && _hitTestInput.Length > 0
                ? _hitTestInput[0].MousePosition
                : Vector2.zero;
            float tol = Mathf.Max(0.01f, HoverDistanceTolerance);

            int nearestIdx   = -1;
            int nearestBand  = int.MaxValue;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                // GPU側でhitRadius外・非選択メッシュは1e10が書き込まれている。
                // ヒット可否の判定はここでやり直さない（半径・メッシュ選択・
                // カリングの判定は全て GPU 側で済んでいる）。
                float depth = _hitVertexDistances[i];
                if (depth >= 1e9f) continue;

                int band = (int)(Vector2.Distance(mouse, _screenPositions[i]) / tol);

                if (band < nearestBand || (band == nearestBand && depth < nearestDepth))
                {
                    nearestBand  = band;
                    nearestDepth = depth;
                    nearestIdx   = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版（吸着用）: 最近接頂点を検索（深度バッファから）。
        /// DispatchVertexSnapHitTestGPU の結果を読む。メッシュ選択で絞られていないため
        /// 非選択オブジェクトの頂点も返り得る。未ヒットは -1。
        /// </summary>
        public int FindNearestSnapVertexFromGPU(float hitRadius)
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalVertexCount; i++)
            {
                float depth = _snapHitVertexDistances[i];
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
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュは1e10）
        /// </summary>
        public int FindNearestLineFromGPU(float hitRadius)
        {
            Vector2 mouse = _hitTestInput != null && _hitTestInput.Length > 0
                ? _hitTestInput[0].MousePosition
                : Vector2.zero;
            float tol = Mathf.Max(0.01f, HoverDistanceTolerance);

            int nearestIdx   = -1;
            int nearestBand  = int.MaxValue;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalLineCount; i++)
            {
                // GPU側でhitRadius外・非選択メッシュは1e10が書き込まれている。
                // ヒット可否の判定はここでやり直さない。
                float depth = _hitLineDistances[i];
                if (depth >= 1e9f) continue;

                // 線分のスクリーン距離は両端の投影座標から線分距離で求める。
                // CPU 版ヒットテスト (FindNearestLine) と同じ式を使う。
                var line = _lines[i];
                float dist = DistanceToLineSegment(
                    mouse, _screenPositions[line.V1], _screenPositions[line.V2]);
                int band = (int)(dist / tol);

                if (band < nearestBand || (band == nearestBand && depth < nearestDepth))
                {
                    nearestBand  = band;
                    nearestDepth = depth;
                    nearestIdx   = i;
                }
            }

            return nearestIdx;
        }

        /// <summary>
        /// GPU版: 最近接面を検索（ヒットバッファから）
        /// GPU側でFLAG_MESH_SELECTEDチェック済み（非選択メッシュはhit=0, depth=1e10）
        /// </summary>
        public int FindNearestFaceFromGPU()
        {
            int nearestIdx = -1;
            float nearestDepth = float.MaxValue;

            for (int i = 0; i < _totalFaceCount; i++)
            {
                // x = ヒット可否、y = 深度。GPU 側で必ずまとめて書かれる。
                if (_faceHit[i].x > 0.5f && _faceHit[i].y < nearestDepth)
                {
                    nearestDepth = _faceHit[i].y;
                    nearestIdx = i;
                }
            }

            return nearestIdx;
        }

        // 【DispatchAllHitTestsGPU を撤去した理由】 2026-08-28
        //   呼出元 0 件。実際のホバー経路は UnifiedMeshSystem.ProcessMouseUpdate が
        //   Clear → ClearCulled → ScreenPos → Visibility → 各 HitTest の順で
        //   個別に呼んでおり、こちらは同じ手順の古い複製だった。
        //   手順を変えるときは ProcessMouseUpdate 側だけを直すこと。

        /// <summary>
        /// GPUでバッファをクリア（スクリーン座標・ヒット距離を初期化）
        /// per-slot カリングバッファは DispatchClearCulledBuffersGPU で別途クリアする。
        /// D3D11.0のUAV制限(8個)のため、2つのカーネルに分割
        /// </summary>
        public void DispatchClearBuffersGPU()
        {
            if (!_gpuComputeAvailable || _computeShader == null)
                return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount", _totalLineCount);
            _computeShader.SetInt("_FaceCount", _totalFaceCount);

            // カーネル1: 頂点・線分のヒット距離のみ。
            // スクリーン座標のクリアは撤去した。ClearBuffers が書いていた
            // _ScreenPositionBuffer の実体（_screenPosBuffer4）は読み手 0 件で、
            // 実際に読まれる per-slot バッファは ComputeScreenPositions が
            // 全頂点を無条件に書くためクリアが要らない。
            // _VertexFlagsBuffer / _LineFlagsBuffer もカーネル本体が参照していないので
            // バインドをやめた（クリア対象でもない）。
            _computeShader.SetBuffer(_kernelClear, "_VertexHitDistanceBuffer", _hitVertexDistBuffer);
            _computeShader.SetBuffer(_kernelClear, "_LineHitDistanceBuffer",   _hitLineDistBuffer);

            int maxVertexLine = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 【括弧を補った理由】診断行を後から挿入したとき中括弧を付けなかったため、
            // Dispatch が if の外に出て maxVertexLine == 0 でも実行されていた。
            // ThreadGroups(0) == 0 なので Dispatch(0,1,1) になる。
            if (maxVertexLine > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("Clear", 0, null, ThreadGroups(maxVertexLine));
                _computeShader.Dispatch(_kernelClear, ThreadGroups(maxVertexLine), 1, 1);
            }

            // カーネル2: 面ヒット（x=ヒット, y=深度 の float2 1 本）。
            // _FaceFlagsBuffer はカーネル本体が参照していないのでバインドをやめた。
            _computeShader.SetBuffer(_kernelClearFace, "_FaceHitBuffer", _faceHitBuffer);

            // 括弧を補った理由は上の Clear と同じ。
            if (_totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearFace", 0, null, ThreadGroups(_totalFaceCount));
                _computeShader.Dispatch(_kernelClearFace, ThreadGroups(_totalFaceCount), 1, 1);
            }
        }

        /// <summary>
        /// per-slot カリングバッファをクリア（全頂点・辺・面を「カリング済み」で初期化）。
        /// ComputeScreenPositionsGPU の前に呼ぶこと。
        /// </summary>
        // 永続ミラー（MirrorSide/BakedMirror）の per-slot 表示状態。
        // 描画準備（ClearCulledBuffers 発行）前に SetMirrorDisplay で slot ごとに設定する。
        // 既定は全 slot 表示(1)。要素数は CullingSlotCount に一致させること。
        // 末尾の HitTestSlot は常に表示(1)のまま使う。ヒットテスト経路は
        // DispatchApplyMirrorCullGPU を呼ばないため、ここの値は参照されない。
        //
        // 【頂点と辺を別に持つ理由】 2026-08-28
        //   ApplyMirrorCull カーネルの頂点ブロックと線分ブロックが同じ値を
        //   読んでいたため、「ミラーの辺だけ消して頂点は残す」ができなかった。
        private readonly int[] _showSelectedMirrorVertex   = CreateMirrorDisplayDefaults();
        private readonly int[] _showSelectedMirrorLine     = CreateMirrorDisplayDefaults();
        private readonly int[] _showUnselectedMirrorVertex = CreateMirrorDisplayDefaults();
        private readonly int[] _showUnselectedMirrorLine   = CreateMirrorDisplayDefaults();

        private static int[] CreateMirrorDisplayDefaults()
        {
            var a = new int[CullingSlotCount];
            for (int i = 0; i < a.Length; i++) a[i] = 1;
            return a;
        }

        /// <summary>
        /// 永続ミラーの表示可否を slot 単位で設定する（次回の per-slot カリング適用に反映）。
        ///
        /// 【引数をまとめた 2 引数版を用意しない理由】
        ///   「選択・非選択」だけを渡す旧 API を残すと、辺と頂点を指定し忘れた
        ///   呼び出しがまた生まれる。呼出元は PrepareViewport の 1 か所だけなので、
        ///   4 つとも明示させる。
        /// </summary>
        public void SetMirrorDisplay(
            int slot,
            bool showSelectedVertex,   bool showSelectedLine,
            bool showUnselectedVertex, bool showUnselectedLine)
        {
            if (slot < 0 || slot >= CullingSlotCount) return;
            _showSelectedMirrorVertex[slot]   = showSelectedVertex   ? 1 : 0;
            _showSelectedMirrorLine[slot]     = showSelectedLine     ? 1 : 0;
            _showUnselectedMirrorVertex[slot] = showUnselectedVertex ? 1 : 0;
            _showUnselectedMirrorLine[slot]   = showUnselectedLine   ? 1 : 0;
        }

        public void DispatchClearCulledBuffersGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null) return;
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            var fBuf = GetFaceCulledBuffer(slot);
            if (vBuf == null) return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount",   _totalLineCount);
            _computeShader.SetInt("_FaceCount",   _totalFaceCount);

            // 頂点・辺
            _computeShader.SetBuffer(_kernelClearCulled, "_VertexFlagsBuffer",  _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelClearCulled, "_VertexCulledBuffer", vBuf);
            _computeShader.SetBuffer(_kernelClearCulled, "_LineFlagsBuffer",    _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelClearCulled, "_LineCulledBuffer",   lBuf);
            int maxVL = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 括弧を補った理由は DispatchClearBuffersGPU と同じ。
            if (maxVL > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearCulled", 0, null, ThreadGroups(maxVL));
                _computeShader.Dispatch(_kernelClearCulled, ThreadGroups(maxVL), 1, 1);
            }

            // 面
            if (fBuf != null)
            {
                _computeShader.SetBuffer(_kernelClearFaceCulled, "_FaceFlagsBuffer",  _faceFlagsBuffer);
                _computeShader.SetBuffer(_kernelClearFaceCulled, "_FaceCulledBuffer", fBuf);
                if (_totalFaceCount > 0)
                {
                    Poly_Ling.Diagnostics.PLCamDbg.Dsp("ClearFaceCulled", 0, null, ThreadGroups(_totalFaceCount));
                    _computeShader.Dispatch(_kernelClearFaceCulled, ThreadGroups(_totalFaceCount), 1, 1);
                }
            }
        }

        /// <summary>
        /// 永続ミラー（MirrorSide/BakedMirror）要素の最終カリングを per-slot で適用する。
        /// 表向き面による un-cull 上書きを受けないよう、ComputeFace/LineVisibility の後に呼ぶこと。
        /// SetMirrorDisplay(slot, ...) で設定した slot ごとの表示状態を参照する。
        /// </summary>
        public void DispatchApplyMirrorCullGPU(int slot)
        {
            if (!_gpuComputeAvailable || _computeShader == null) return;
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            if (vBuf == null || lBuf == null) return;

            bool inRange = slot >= 0 && slot < CullingSlotCount;
            int selVtx   = inRange ? _showSelectedMirrorVertex[slot]   : 1;
            int selLine  = inRange ? _showSelectedMirrorLine[slot]     : 1;
            int unselVtx = inRange ? _showUnselectedMirrorVertex[slot] : 1;
            int unselLine= inRange ? _showUnselectedMirrorLine[slot]   : 1;

            // 4 つとも表示なら、このカーネルは何も変えないので dispatch を省く。
            // 1 つでも 0 があれば実行する。
            if (selVtx != 0 && selLine != 0 && unselVtx != 0 && unselLine != 0) return;

            _computeShader.SetInt("_VertexCount", _totalVertexCount);
            _computeShader.SetInt("_LineCount",   _totalLineCount);
            _computeShader.SetInt("_ShowSelectedMirrorVertex",   selVtx);
            _computeShader.SetInt("_ShowSelectedMirrorLine",     selLine);
            _computeShader.SetInt("_ShowUnselectedMirrorVertex", unselVtx);
            _computeShader.SetInt("_ShowUnselectedMirrorLine",   unselLine);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_VertexFlagsBuffer",  _vertexFlagsBuffer);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_VertexCulledBuffer", vBuf);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_LineFlagsBuffer",    _lineFlagsBuffer);
            _computeShader.SetBuffer(_kernelApplyMirrorCull, "_LineCulledBuffer",   lBuf);
            int maxVL = Mathf.Max(_totalVertexCount, _totalLineCount);
            // 括弧を補った理由は DispatchClearBuffersGPU と同じ。
            if (maxVL > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Dsp("ApplyMirrorCull", 0, null, ThreadGroups(maxVL));
                _computeShader.Dispatch(_kernelApplyMirrorCull, ThreadGroups(maxVL), 1, 1);
            }
        }

        /// <summary>
        /// 背面カリング無効時: 指定スロットの全カリングバッファをゼロ（可視）にクリアする。
        /// DispatchClearCulledBuffersGPU の後に呼ぶこと。
        /// </summary>
        /// <param name="slot">既定値を付けない理由は DispatchFaceVisibilityGPU を参照。</param>
        public void ClearCulledFlagsGPU(int slot)
        {
            var vBuf = GetVertexCulledBuffer(slot);
            var lBuf = GetLineCulledBuffer(slot);
            var fBuf = GetFaceCulledBuffer(slot);

            // zeros キャッシュが未確保の場合は確保する
            if (_zeroVertexCache == null || _zeroVertexCache.Length < _totalVertexCount)
                Array.Resize(ref _zeroVertexCache, Mathf.NextPowerOfTwo(Mathf.Max(1, _totalVertexCount)));
            if (_zeroLineCache   == null || _zeroLineCache.Length   < _totalLineCount)
                Array.Resize(ref _zeroLineCache,   Mathf.NextPowerOfTwo(Mathf.Max(1, _totalLineCount)));
            if (_zeroFaceCache   == null || _zeroFaceCache.Length   < _totalFaceCount)
                Array.Resize(ref _zeroFaceCache,   Mathf.NextPowerOfTwo(Mathf.Max(1, _totalFaceCount)));

            // 【括弧を補った理由】診断行を後から挿入したとき中括弧を付けなかったため、
            // SetData が if の外に出て「バッファが null でも呼ぶ」状態になっていた。
            // 本メソッドは背面カリング OFF のとき毎回通る経路。
            if (vBuf != null && _totalVertexCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("vBuf", vBuf, 0);
                vBuf.SetData(_zeroVertexCache, 0, 0, _totalVertexCount);
            }
            if (lBuf != null && _totalLineCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("lBuf", lBuf, 0);
                lBuf.SetData(_zeroLineCache, 0, 0, _totalLineCount);
            }
            if (fBuf != null && _totalFaceCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("fBuf", fBuf, 0);
                fBuf.SetData(_zeroFaceCache, 0, 0, _totalFaceCount);
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
                if (_transformMatrixBuffer != null) Poly_Ling.Diagnostics.PLResStat.LiveCB--;
                _transformMatrixBuffer?.Release();
                _transformMatrixBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(Mathf.Max(contextCount, 256), sizeof(float) * 16));
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
                    // MeshFilter（Type=Mesh かつ BoneWeight なし）: WorldMatrix を直接使用
                    //   → ローカル座標に WorldMatrix を適用してワールド座標を得る
                    // スキンドメッシュ（Type=Mesh かつ BoneWeight あり）: SkinningMatrix を使用
                    // ボーン（Type=Bone）: SkinningMatrix を使用
                    //   → スキンドメッシュ頂点の boneIndex がボーンを指すため、
                    //     SkinningMatrix = BoneWorldMatrix × BoneBindPose が必要
                    // ミラー側（MirrorSide / BakedMirror）も、スキンを持たなければ
                    // MeshFilter と同じ実体である。Type だけで弾くと SkinningMatrix 経路に
                    // 落ち、BindPose が WorldMatrix⁻¹ に更新された瞬間（Tポーズ変換など）
                    // SkinningMatrix = W·W⁻¹ = 単位 となって変換が丸ごと消える。
                    // ここは「行列表の中身」を決める場所であって、頂点の座標系の判定では
                    // ない。対象の型を明示すること。型で絞らずに IsSkinned だけで分けると、
                    // ボーンの欄が WorldMatrix になり、その欄を boneIndex で引く
                    // スキンド頂点が全部ボーンのワールド位置ぶん飛ぶ。
                    bool usesWorldMatrixDirect =
                        (ctx.Type == MeshType.Mesh ||
                         ctx.Type == MeshType.MirrorSide ||
                         ctx.Type == MeshType.BakedMirror) &&
                        !ctx.IsSkinned;
                    _transformMatrices[i] = usesWorldMatrixDirect ? ctx.WorldMatrix : ctx.SkinningMatrix;
                }
                else
                {
                    _transformMatrices[i] = ctx.LocalMatrix;
                }
            }

            // GPUにアップロード
            if (contextCount > 0)
            {
                Poly_Ling.Diagnostics.PLCamDbg.Wr("_transformMatrixBuffer", _transformMatrixBuffer, 0);
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
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("TransformVertices", 0, null, threadGroups);
            _computeShader.Dispatch(_kernelTransformVertices, threadGroups, 1, 1);

            // CPU側に読み戻し（描画用）- 非推奨、後方互換用
            if (readbackToCPU && useWorldTransform)
            {
                if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                    _worldPositions = new Vector3[_totalVertexCount];
                
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G12 before n=" + _totalVertexCount + " buf=" + _worldPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _worldPositionBuffer.count + " arr=" + _worldPositions.Length);
                // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
                if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                    _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
                // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
                //   GetData = フラッシュ + GPU 完了待ち
                //   GL.Flush = フラッシュのみ（待たない）
                //   どちらが引き金かを分離するための診断。
                else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                    UnityEngine.GL.Flush();
                else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                    Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G12 after");
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
            Poly_Ling.Diagnostics.PLCamDbg.Dsp("ExpandVertices", 0, null, threadGroups);
            _computeShader.Dispatch(_kernelExpandVertices, threadGroups, 1, 1);
        }

        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

        /// <summary>
        /// ワールド座標バッファの内容を取得（デバッグ用）
        /// </summary>
        public Vector3[] GetWorldPositions()
        {
            if (_worldPositionBuffer == null || _totalVertexCount == 0)
                return null;

            if (_worldPositions == null || _worldPositions.Length < _totalVertexCount)
                _worldPositions = new Vector3[_totalVertexCount];

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G13 before n=" + _totalVertexCount + " buf=" + _worldPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _worldPositionBuffer.count + " arr=" + _worldPositions.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _worldPositionBuffer.GetData(_worldPositions, 0, 0, _totalVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G13 after");
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

            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G14 before n=" + _totalExpandedVertexCount + " buf=" + _expandedPositionBuffer.GetHashCode() + " f=" + Poly_Ling.Diagnostics.PLCamDbg.Frame + " cnt=" + _expandedPositionBuffer.count + " arr=" + _expandedPositions.Length);
            // [CamDbg] getdata=0 のとき同期読み戻しを飛ばす。診断専用。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwGetData)
                _expandedPositionBuffer.GetData(_expandedPositions, 0, 0, _totalExpandedVertexCount);
            // [CamDbg] flush=1 のとき、読み戻しの代わりにフラッシュのみ行う。
            //   GetData = フラッシュ + GPU 完了待ち
            //   GL.Flush = フラッシュのみ（待たない）
            //   どちらが引き金かを分離するための診断。
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushOnly)
                UnityEngine.GL.Flush();
            else if (Poly_Ling.Diagnostics.PLCamDbg.SwFlushDeferred)
                Poly_Ling.Diagnostics.PLCamDbg.FlushPending = true;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("G14 after");
            return _expandedPositions;
        }
    }
}
