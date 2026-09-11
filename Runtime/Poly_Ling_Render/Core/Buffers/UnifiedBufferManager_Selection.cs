// UnifiedBufferManager_Selection.cs
// 統合バッファ：Level 3 選択フラグ更新。
// Runtime/Poly_Ling_Render/Core/Buffers/ に配置

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
    }
}
