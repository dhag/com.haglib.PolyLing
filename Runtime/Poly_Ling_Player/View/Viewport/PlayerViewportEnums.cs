// PlayerViewportEnums.cs
// ビューポート管理の正規入口カテゴリと段階・ホバー対象の列挙（PlayerViewportManager から分離）。
// Runtime/Poly_Ling_Player/View/Viewport/ に配置（PlayerViewportManager.cs と同じ名前空間。PlayerViewportManager.cs から分割）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    // ================================================================
    // ★★★ 正規入口カテゴリ（Phase 2a）★★★
    //
    // PlayerViewportManager / MeshSceneRenderer / UnifiedSystemAdapter への
    // 外部アクセスは以下の 6 入口のみを使うこと。
    //   1. EnterProjectChanged         — プロジェクト/モデル変更
    //   2. EnterTopologyChanged        — トポロジ変更・選択変更
    //   3. EnterCameraChanged          — 視点・カメラパラメータ変更
    //   4. EnterVerticesMoved          — 頂点位置変更
    //   5. EnterHoverChanged           — ホバー状態変更
    //   6. EnterDisplaySettingsChanged — per-viewport 表示トグル
    //
    // ライフサイクル API（Initialize / Dispose / RegisterMoveToolHandler）は
    // 別グループ。純粋 getter（GetCurrentToolContext, PerspectiveViewport 等）も
    // 対象外。
    //
    // 新規入口の追加には明示的な承認が必須。
    // ================================================================

    /// <summary>カメラ操作のフェーズ。</summary>
    public enum CameraChangePhase
    {
        /// <summary>ドラッグ開始（軽量プロファイル切替）</summary>
        DragBegin,
        /// <summary>ドラッグ中の連続更新（軽量パス）</summary>
        Dragging,
        /// <summary>ドラッグ終了（確定 + 重いパイプライン）</summary>
        DragEnd,
        /// <summary>単発のカメラパラメータ確定（スクロール等、ドラッグ無関係）</summary>
        Committed,
        /// <summary>カメラ位置をリセット（ResetToMesh）</summary>
        Reset,
    }

    /// <summary>頂点移動のフェーズ。</summary>
    public enum VerticesMovedPhase
    {
        /// <summary>ドラッグ開始（軽量プロファイル切替）</summary>
        DragBegin,
        /// <summary>ドラッグ中の連続更新（軽量パス）</summary>
        Dragging,
        /// <summary>ドラッグ終了（確定）</summary>
        DragEnd,
    }

    /// <summary>ホバー対象の種類。ツールごとにホバー対象を限定することで、
    /// 辺ツール中に頂点色が変わる等の誤動作を防ぐ。</summary>
    public enum HoverTargetKind
    {
        /// <summary>ホバー表示を消す。</summary>
        None,
        /// <summary>頂点編集系ツール</summary>
        Vertex,
        /// <summary>辺編集系ツール（EdgeTopology, EdgeBevel 等）</summary>
        Edge,
        /// <summary>面編集系ツール（AddFace, FaceExtrude 等）</summary>
        Face,
        /// <summary>ボーン編集系ツール</summary>
        Bone,
        /// <summary>ツールギズモ（Move/Rotate/Scale 等のギズモ軸）</summary>
        Gizmo,
    }
}
