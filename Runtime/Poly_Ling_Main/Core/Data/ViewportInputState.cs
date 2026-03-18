// ViewportInputState.cs
// HandleInput関連の入力状態を集約
// PolyLingのpartial classフィールドから抽出
// ViewportPanelとPolyLing本体の両方からアクセス可能

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Rendering;
using Poly_Ling.Tools;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Data
{
    // ================================================================
    // 入力状態の列挙型（PolyLingのネスト型から移動）
    // ================================================================

    public enum VertexEditState
    {
        Idle,              // 待機
        PendingAction,     // MouseDown後、ドラッグかクリックか判定中
        BoxSelecting,      // 矩形選択中
        LassoSelecting     // 投げ縄選択中
    }

    public enum DragSelectMode
    {
        Box,    // 矩形選択
        Lasso   // 投げ縄選択
    }

    // ================================================================
    // 入力状態クラス
    // ================================================================

    public class ViewportInputState
    {
        // --- 編集状態 ---
        public VertexEditState EditState = VertexEditState.Idle;
        public DragSelectMode DragSelectMode = DragSelectMode.Box;

        // --- MouseDown時のスナップショット ---
        public Vector2 MouseDownScreenPos;
        public int HitVertexOnMouseDown = -1;
        public HitResult HitResultOnMouseDown;
        public int HitMeshIndexOnMouseDown = -1;
        public SelectionSnapshot SelectionSnapshotOnMouseDown;
        public WorkPlaneSnapshot? WorkPlaneSnapshotOnMouseDown;
        public bool TopologyChangedDuringMouseOp;

        // --- 矩形/投げ縄選択 ---
        public Vector2 BoxSelectStart;
        public Vector2 BoxSelectEnd;
        public List<Vector2> LassoPoints = new List<Vector2>();

        // --- ホバー ---
        public Vector2 LastHoverMousePos;
        public GPUHitTestResult LastHoverHitResult;
        public int LastHoverMeshIndex = -1;

        // --- 定数 ---
        public const float DragThreshold = 0f;
    }
}
