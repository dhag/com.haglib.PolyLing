// Assets/Editor/Poly_Ling/Core/Update/FlagManager.cs
// フラグ管理クラス
// 階層・選択・表示フラグの計算と更新

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Model;

namespace Poly_Ling.Core
{
    /// <summary>
    /// フラグ管理クラス
    /// SelectionFlagsの計算と更新を担当
    /// </summary>
    public class FlagManager
    {
        // ============================================================
        // 設定
        // ============================================================

        /// <summary>選択モデルインデックス（-1 = なし）</summary>
        public int SelectedModelIndex { get; set; } = -1;

        /// <summary>選択メッシュインデックス（-1 = なし）- 後方互換</summary>
        public int SelectedMeshIndex { get; set; } = -1;

        // v2.0: カテゴリ別選択インデックス
        /// <summary>選択メッシュインデックス（Mesh/BakedMirror タイプ）</summary>
        public int SelectedDrawableMeshIndex { get; set; } = -1;

        /// <summary>選択ボーンインデックス（Bone タイプ）</summary>
        public int SelectedBoneIndex { get; set; } = -1;

        /// <summary>選択頂点モーフインデックス（Morph タイプ）</summary>
        public int SelectedMorphIndex { get; set; } = -1;

        /// <summary>アクティブモデルインデックス（編集対象）</summary>
        public int ActiveModelIndex { get; set; } = 0;

        /// <summary>アクティブメッシュインデックス（編集対象）</summary>
        public int ActiveMeshIndex { get; set; } = 0;

        // v2.1: 複数メッシュ選択
        /// <summary>選択されているメッシュインデックスのリスト</summary>
        public List<int> SelectedMeshIndices { get; set; } = new List<int>();

        /// <summary>
        /// 指定メッシュが選択されているかチェック（複数選択対応）
        /// </summary>
        public bool IsMeshSelected(int meshIndex)
        {
            // v2.1: 複数選択をチェック
            if (SelectedMeshIndices != null && SelectedMeshIndices.Contains(meshIndex))
                return true;
            
            // 後方互換: 単一選択もチェック
            if (meshIndex == SelectedMeshIndex)
                return true;
            
            // カテゴリ別選択もチェック
            if (meshIndex == SelectedDrawableMeshIndex ||
                meshIndex == SelectedBoneIndex ||
                meshIndex == SelectedMorphIndex)
                return true;
            
            return false;
        }

        // ============================================================
        // 選択状態参照
        // ============================================================

        /// <summary>SelectionState参照（外部から設定）</summary>
        public SelectionState SelectionState { get; set; }

        // ============================================================
        // 頂点フラグ計算
        // ============================================================

        /// <summary>
        /// 頂点の階層フラグを計算
        /// v2.1: 複数メッシュ選択対応
        /// </summary>
        /// <param name="modelIndex">モデルインデックス</param>
        /// <param name="meshIndex">メッシュインデックス（モデル内）</param>
        /// <returns>階層フラグ</returns>
        public SelectionFlags ComputeHierarchyFlags(int modelIndex, int meshIndex)
        {
            SelectionFlags flags = SelectionFlags.None;

            // モデルレベル
            if (modelIndex == SelectedModelIndex)
                flags |= SelectionFlags.ModelSelected;

            if (modelIndex == ActiveModelIndex)
                flags |= SelectionFlags.ModelActive;

            // メッシュレベル - v2.1: IsMeshSelected()で複数選択をチェック
            if (modelIndex == SelectedModelIndex && IsMeshSelected(meshIndex))
            {
                flags |= SelectionFlags.MeshSelected;
            }

            if (modelIndex == ActiveModelIndex && meshIndex == ActiveMeshIndex)
                flags |= SelectionFlags.MeshActive;

            return flags;
        }

        /// <summary>
        /// 頂点の選択フラグを計算
        /// </summary>
        /// <param name="localVertexIndex">メッシュ内の頂点インデックス</param>
        /// <param name="isActiveMesh">アクティブメッシュかどうか</param>
        /// <returns>要素選択フラグ</returns>
        public SelectionFlags ComputeVertexSelectionFlags(int localVertexIndex, bool isActiveMesh)
        {
            if (!isActiveMesh || SelectionState == null)
                return SelectionFlags.None;

            SelectionFlags flags = SelectionFlags.None;

            if (SelectionState.Vertices.Contains(localVertexIndex))
                flags |= SelectionFlags.VertexSelected;

            return flags;
        }

        /// <summary>
        /// 頂点の全フラグを計算
        /// </summary>
        public SelectionFlags ComputeVertexFlags(
            int modelIndex,
            int meshIndex,
            int localVertexIndex,
            bool isVisible,
            bool isLocked,
            bool isMirror = false)
        {
            SelectionFlags flags = ComputeHierarchyFlags(modelIndex, meshIndex);

            bool isActiveMesh = flags.IsActive();

            // 要素選択
            flags |= ComputeVertexSelectionFlags(localVertexIndex, isActiveMesh);

            // 表示制御
            if (!isVisible) flags |= SelectionFlags.Hidden;
            if (isLocked) flags |= SelectionFlags.Locked;
            if (isMirror) flags |= SelectionFlags.Mirror;

            return flags;
        }

        // ============================================================
        // エッジ/ラインフラグ計算
        // ============================================================

        /// <summary>
        /// エッジの選択フラグを計算
        /// </summary>
        public SelectionFlags ComputeEdgeSelectionFlags(int v1, int v2, bool isActiveMesh)
        {
            if (!isActiveMesh || SelectionState == null)
                return SelectionFlags.None;

            SelectionFlags flags = SelectionFlags.None;

            var pair = new VertexPair(v1, v2);
            if (SelectionState.Edges.Contains(pair))
                flags |= SelectionFlags.EdgeSelected;

            return flags;
        }

        /// <summary>
        /// 補助線の選択フラグを計算
        /// </summary>
        public SelectionFlags ComputeLineSelectionFlags(int faceIndex, bool isActiveMesh)
        {
            if (!isActiveMesh || SelectionState == null)
                return SelectionFlags.None;

            SelectionFlags flags = SelectionFlags.None;

            if (SelectionState.Lines.Contains(faceIndex))
                flags |= SelectionFlags.LineSelected;

            return flags;
        }

        /// <summary>
        /// ラインの全フラグを計算（エッジまたは補助線）
        /// </summary>
        public SelectionFlags ComputeLineFlags(
            int modelIndex,
            int meshIndex,
            int v1Local,
            int v2Local,
            int faceIndex,
            bool isAuxLine,
            bool isVisible,
            bool isLocked,
            bool isMirror = false,
            bool isBoundary = false)
        {
            SelectionFlags flags = ComputeHierarchyFlags(modelIndex, meshIndex);

            bool isActiveMesh = flags.IsActive();

            // 要素選択
            if (isAuxLine)
            {
                flags |= ComputeLineSelectionFlags(faceIndex, isActiveMesh);
                flags |= SelectionFlags.IsAuxLine;
            }
            else
            {
                flags |= ComputeEdgeSelectionFlags(v1Local, v2Local, isActiveMesh);
            }

            // 表示制御
            if (!isVisible) flags |= SelectionFlags.Hidden;
            if (isLocked) flags |= SelectionFlags.Locked;
            if (isMirror) flags |= SelectionFlags.Mirror;

            // 属性
            if (isBoundary) flags |= SelectionFlags.IsBoundary;

            return flags;
        }

        // ============================================================
        // 面フラグ計算
        // ============================================================

        /// <summary>
        /// 面の選択フラグを計算
        /// 
        /// 【複数メッシュ対応】
        /// - プライマリ: SelectionState.Facesを見る
        /// - セカンダリ: MeshContext.SelectedFacesを見る（呼び出し側で処理）
        /// </summary>
        public SelectionFlags ComputeFaceSelectionFlags(int faceIndex, bool isActiveMesh, Poly_Ling.Data.MeshContext meshContext = null)
        {
            SelectionFlags flags = SelectionFlags.None;

            if (isActiveMesh && SelectionState != null)
            {
                // プライマリメッシュ: SelectionStateを見る
                if (SelectionState.Faces.Contains(faceIndex))
                    flags |= SelectionFlags.FaceSelected;
            }
            else if (meshContext != null)
            {
                // セカンダリメッシュ: MeshContextを見る
                if (meshContext.SelectedFaces.Contains(faceIndex))
                    flags |= SelectionFlags.FaceSelected;
            }

            return flags;
        }

        /// <summary>
        /// 面の全フラグを計算
        /// </summary>
        public SelectionFlags ComputeFaceFlags(
            int modelIndex,
            int meshIndex,
            int faceIndex,
            bool isVisible,
            bool isLocked,
            bool isMirror = false,
            Poly_Ling.Data.MeshContext meshContext = null)
        {
            SelectionFlags flags = ComputeHierarchyFlags(modelIndex, meshIndex);

            bool isActiveMesh = flags.IsActive();

            // 要素選択
            flags |= ComputeFaceSelectionFlags(faceIndex, isActiveMesh, meshContext);

            // 表示制御
            if (!isVisible) flags |= SelectionFlags.Hidden;
            if (isLocked) flags |= SelectionFlags.Locked;
            if (isMirror) flags |= SelectionFlags.Mirror;

            return flags;
        }

        // ============================================================
        // バッチ更新
        // ============================================================

        /// <summary>
        /// 頂点フラグを一括計算
        /// </summary>
        /// <param name="vertexFlags">出力先フラグ配列</param>
        /// <param name="meshInfos">メッシュ情報リスト</param>
        /// <param name="meshContexts">メッシュコンテキストリスト</param>
        /// <param name="modelIndex">モデルインデックス</param>
        public void ComputeAllVertexFlags(
            uint[] vertexFlags,
            List<MeshInfo> meshInfos,
            List<MeshContext> meshContexts,
            int modelIndex)
        {
            if (vertexFlags == null || meshContexts == null)
                return;

            for (int meshIdx = 0; meshIdx < meshContexts.Count && meshIdx < meshInfos.Count; meshIdx++)
            {
                var meshContext = meshContexts[meshIdx];
                var meshInfo = meshInfos[meshIdx];

                if (meshContext?.MeshObject == null)
                    continue;

                SelectionFlags hierarchyFlags = ComputeHierarchyFlags(modelIndex, meshIdx);
                bool isActiveMesh = hierarchyFlags.IsActive();
                bool isVisible = meshContext.IsVisible;
                bool isLocked = meshContext.IsLocked;

                int vertexCount = meshContext.MeshObject.VertexCount;
                uint baseOffset = meshInfo.VertexStart;

                for (int v = 0; v < vertexCount; v++)
                {
                    uint globalIdx = baseOffset + (uint)v;
                    if (globalIdx >= vertexFlags.Length)
                        break;

                    SelectionFlags flags = hierarchyFlags;

                    // 要素選択
                    if (isActiveMesh && SelectionState != null)
                    {
                        if (SelectionState.Vertices.Contains(v))
                            flags |= SelectionFlags.VertexSelected;
                    }

                    // 表示制御
                    if (!isVisible) flags |= SelectionFlags.Hidden;
                    if (isLocked) flags |= SelectionFlags.Locked;

                    vertexFlags[globalIdx] = (uint)flags;
                }
            }
        }

        /// <summary>
        /// 選択フラグのみ差分更新
        /// </summary>
        /// <param name="vertexFlags">フラグ配列</param>
        /// <param name="vertexStart">更新開始位置</param>
        /// <param name="oldSelection">変更前の選択</param>
        /// <param name="newSelection">変更後の選択</param>
        public void UpdateVertexSelectionFlags(
            uint[] vertexFlags,
            uint vertexStart,
            HashSet<int> oldSelection,
            HashSet<int> newSelection)
        {
            if (vertexFlags == null)
                return;

            // 変更のあった頂点を特定
            var changed = new HashSet<int>(oldSelection);
            changed.SymmetricExceptWith(newSelection);

            foreach (int v in changed)
            {
                uint globalIdx = vertexStart + (uint)v;
                if (globalIdx >= vertexFlags.Length)
                    continue;

                if (newSelection.Contains(v))
                {
                    vertexFlags[globalIdx] |= (uint)SelectionFlags.VertexSelected;
                }
                else
                {
                    vertexFlags[globalIdx] &= ~(uint)SelectionFlags.VertexSelected;
                }
            }
        }

        // ============================================================
        // インタラクションフラグ更新
        // ============================================================

        /// <summary>
        /// ホバーフラグを設定
        /// </summary>
        public void SetHoverFlag(uint[] flags, int globalIndex, bool isHovered)
        {
            if (flags == null || globalIndex < 0 || globalIndex >= flags.Length)
                return;

            if (isHovered)
                flags[globalIndex] |= (uint)SelectionFlags.Hovered;
            else
                flags[globalIndex] &= ~(uint)SelectionFlags.Hovered;
        }

        /// <summary>
        /// 全ホバーフラグをクリア
        /// </summary>
        public void ClearAllHoverFlags(uint[] flags)
        {
            if (flags == null)
                return;

            uint mask = ~(uint)SelectionFlags.Hovered;
            for (int i = 0; i < flags.Length; i++)
            {
                flags[i] &= mask;
            }
        }

        /// <summary>
        /// ドラッグフラグを設定
        /// </summary>
        public void SetDragFlag(uint[] flags, IEnumerable<int> indices, bool isDragging)
        {
            if (flags == null || indices == null)
                return;

            foreach (int idx in indices)
            {
                if (idx < 0 || idx >= flags.Length)
                    continue;

                if (isDragging)
                    flags[idx] |= (uint)SelectionFlags.Dragging;
                else
                    flags[idx] &= ~(uint)SelectionFlags.Dragging;
            }
        }

        // ============================================================
        // ミラーフラグ
        // ============================================================

        /// <summary>
        /// ミラーフラグを設定
        /// </summary>
        public void SetMirrorFlags(uint[] flags, int startIndex, int count)
        {
            if (flags == null)
                return;

            for (int i = startIndex; i < startIndex + count && i < flags.Length; i++)
            {
                flags[i] |= (uint)SelectionFlags.Mirror;
            }
        }

        /// <summary>
        /// カリングフラグを設定
        /// </summary>
        public void SetCulledFlag(uint[] flags, int globalIndex, bool isCulled)
        {
            if (flags == null || globalIndex < 0 || globalIndex >= flags.Length)
                return;

            if (isCulled)
                flags[globalIndex] |= (uint)SelectionFlags.Culled;
            else
                flags[globalIndex] &= ~(uint)SelectionFlags.Culled;
        }
    }
}
