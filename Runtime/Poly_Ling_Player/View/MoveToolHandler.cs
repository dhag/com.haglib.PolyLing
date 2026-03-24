// MoveToolHandler.cs
// 移動モードの IPlayerToolHandler 実装。
// 左クリック・左ドラッグ（ヒット有り→頂点移動、ヒット無し→矩形選択）を処理する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 移動モード。
    /// <list type="bullet">
    ///   <item>左クリック → <see cref="PlayerSelectionOps.ApplyClick"/>（選択共通）</item>
    ///   <item>左ドラッグ・ヒット有り → 選択頂点を移動</item>
    ///   <item>左ドラッグ・ヒット無し → 矩形選択</item>
    /// </list>
    /// </summary>
    public class MoveToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 外部注入（Viewer が設定する）
        // ================================================================

        /// <summary>ワールド座標 → スクリーン座標変換。矩形選択の判定に使う。</summary>
        public Func<Vector3, Vector2> WorldToScreen;

        /// <summary>頂点移動確定時のメッシュ同期コールバック。</summary>
        public Action<MeshContext> OnSyncMeshPositions;

        /// <summary>再描画要求コールバック。</summary>
        public Action OnRepaint;

        // ================================================================
        // 依存
        // ================================================================

        private readonly PlayerSelectionOps _selectionOps;
        private          ProjectContext      _project;

        // ================================================================
        // 内部状態
        // ================================================================

        private enum DragMode { None, Moving, BoxSelecting }

        private DragMode _dragMode = DragMode.None;

        // 頂点移動用
        private int          _moveMeshIndex = -1;
        private List<int>    _moveVertices;       // 移動対象頂点インデックス
        private Vector3[]    _moveStartPositions; // 移動開始時の頂点座標（ローカル）
        private ModifierKeys _dragStartMods;

        // ================================================================
        // 初期化
        // ================================================================

        public MoveToolHandler(PlayerSelectionOps selectionOps, ProjectContext project)
        {
            _selectionOps = selectionOps ?? throw new ArgumentNullException(nameof(selectionOps));
            _project      = project;
        }

        public void SetProject(ProjectContext project)
        {
            _project = project;
        }

        // ================================================================
        // IPlayerToolHandler 実装
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // 移動モード互換の選択をそのまま委譲
            _selectionOps.ApplyClick(hit, mods);
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _dragStartMods = mods;

            if (hit.HasHit)
            {
                // ヒット頂点が未選択ならまず単独選択してから移動
                if (!_selectionOps.SelectionState.Vertices.Contains(hit.VertexIndex))
                    _selectionOps.ApplyClick(hit, new ModifierKeys());

                var mc = GetMeshContext(hit.MeshIndex);
                if (mc?.MeshObject == null) return;

                _dragMode      = DragMode.Moving;
                _moveMeshIndex = hit.MeshIndex;
                _moveVertices  = new List<int>(_selectionOps.SelectionState.Vertices);

                // 移動開始時の座標をスナップショット
                _moveStartPositions = new Vector3[_moveVertices.Count];
                for (int i = 0; i < _moveVertices.Count; i++)
                    _moveStartPositions[i] = mc.MeshObject.Vertices[_moveVertices[i]].Position;
            }
            else
            {
                _dragMode = DragMode.BoxSelecting;
                _selectionOps.BeginBoxSelect(screenPos);
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragMode == DragMode.Moving)
            {
                // 頂点をスクリーン差分に応じて移動
                // スクリーン差分 → ワールド差分はカメラ距離比例の簡易換算。
                // 精密な変換は PolyLingCoreConfig.ScreenDeltaToWorldDelta 相当を
                // Viewer から注入することで置き換えられる。
                ApplyVertexDelta(delta);
            }
            else if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnRepaint?.Invoke();
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_dragMode == DragMode.Moving)
            {
                var mc = GetMeshContext(_moveMeshIndex);
                if (mc != null)
                    OnSyncMeshPositions?.Invoke(mc);
                _dragMode = DragMode.None;
            }
            else if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                CommitBoxSelect(mods);
                _dragMode = DragMode.None;
            }
        }

        // ================================================================
        // 内部処理
        // ================================================================

        /// <summary>スクリーン delta に応じて選択頂点を移動する。</summary>
        public Func<Vector2, Vector2, Vector3> ScreenDeltaToWorldDelta;

        private void ApplyVertexDelta(Vector2 screenDelta)
        {
            if (_moveVertices == null || _moveVertices.Count == 0) return;
            var mc = GetMeshContext(_moveMeshIndex);
            if (mc?.MeshObject == null) return;

            Vector3 worldDelta = ScreenDeltaToWorldDelta != null
                ? ScreenDeltaToWorldDelta(Vector2.zero, screenDelta)
                : Vector3.zero;

            if (worldDelta == Vector3.zero) return;

            var verts = mc.MeshObject.Vertices;
            foreach (int vi in _moveVertices)
                verts[vi].Position += worldDelta;

            OnSyncMeshPositions?.Invoke(mc);
            OnRepaint?.Invoke();
        }

        private void CommitBoxSelect(ModifierKeys mods)
        {
            if (WorldToScreen == null) return;
            var mc = _project?.CurrentModel?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null)
            {
                _selectionOps.EndBoxSelect(System.Linq.Enumerable.Empty<int>(), mods);
                return;
            }

            var rect     = _selectionOps.BoxRect;
            var inBox    = new List<int>();
            var verts    = mc.MeshObject.Vertices;

            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 sp = WorldToScreen(verts[i].Position);
                if (rect.Contains(sp, true))
                    inBox.Add(i);
            }

            _selectionOps.EndBoxSelect(inBox, mods);
            OnRepaint?.Invoke();
        }

        private MeshContext GetMeshContext(int meshIndex)
        {
            var model = _project?.CurrentModel;
            if (model == null || meshIndex < 0 || meshIndex >= model.MeshContextList.Count)
                return null;
            return model.MeshContextList[meshIndex];
        }
    }
}
