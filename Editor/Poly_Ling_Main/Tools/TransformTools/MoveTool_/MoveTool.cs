// Tools/MoveTool.cs
// 頂点移動ツール（マグネット、軸ドラッグ含む）
// 改善版: 正確な軸方向表示、中央ドラッグ対応
// Edge/Face/Line選択時も移動可能
// IToolSettings対応版
// ローカライズ対応版
// Phase 6: ホバー/クリック整合性対応（ToolContext.LastHoverHitResult使用）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Transforms;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Localization;
using Poly_Ling.Rendering;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点移動ツール
    /// 
    /// 【ホバー/クリック整合性（Phase 6）】
    /// このツールはToolContext.LastHoverHitResultを優先的に使用して
    /// ホバーとクリックの整合性を保つ。従来のCPU版ヒットテストは
    /// フォールバックとしてのみ使用する。
    /// </summary>
    public partial class MoveTool : IEditTool
    {
        public string Name => "Move";
        public string DisplayName => "Move";

        /// <summary>
        /// ローカライズされた表示名を取得
        /// </summary>
        public string GetLocalizedDisplayName() => L.Get("Tool_Move");


        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private MoveSettings _settings = new MoveSettings();

        /// <summary>
        /// ツール設定（Undo対応）
        /// </summary>
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ（後方互換 + 内部使用）
        public bool UseMagnet
        {
            get => _settings.UseMagnet;
            set => _settings.UseMagnet = value;
        }

        public float MagnetRadius
        {
            get => _settings.MagnetRadius;
            set => _settings.MagnetRadius = value;
        }

        public FalloffType MagnetFalloff
        {
            get => _settings.MagnetFalloff;
            set => _settings.MagnetFalloff = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum MoveState
        {
            Idle,
            PendingAction,
            MovingVertices,
            AxisDragging,
            CenterDragging  // 中央ドラッグ（自由移動）
        }
        private MoveState _state = MoveState.Idle;

        // 軸
        private enum AxisType { None, X, Y, Z, Center }
        private AxisType _hitAxisOnMouseDown = AxisType.None;
        private AxisType _draggingAxis = AxisType.None;
        private AxisType _hoveredAxis = AxisType.None;  // ホバー中の軸
        private Vector3 _gizmoCenter = Vector3.zero;
        private Vector3 _selectionCenter = Vector3.zero;  // 選択頂点の重心
        private Vector2 _lastAxisDragScreenPos;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private int _hitVertexOnMouseDown = -1;
        private const float DragThreshold = 4f;

        // 全選択メッシュの影響頂点（メッシュインデックス→頂点セット）
        private Dictionary<int, HashSet<int>> _multiMeshAffectedVertices = new Dictionary<int, HashSet<int>>();
        // 全選択メッシュのドラッグ開始位置（メッシュインデックス→位置配列）
        private Dictionary<int, Vector3[]> _multiMeshDragStartPositions = new Dictionary<int, Vector3[]>();
        // 全選択メッシュのトランスフォーム（メッシュインデックス→IVertexTransform）
        private Dictionary<int, IVertexTransform> _meshTransforms = new Dictionary<int, IVertexTransform>();

        // ドラッグ開始時のヒット情報
        /// <summary>
        /// 保留中のヒットタイプ
        /// 
        /// 【Phase 6簡素化】
        /// 選択処理はSimpleMeshFactoryが担当するため、
        /// MoveToolは「選択済み」かどうかだけを判断すればよい。
        /// </summary>
        private enum PendingHitType
        {
            None,
            Selection,      // ★ Phase 6: 汎用的な「選択済み要素がある」
            // 以下は削除候補（互換性のため残す）
            Vertex,
            Edge,
            Face,
            Line,
            SelectedEdge,
            SelectedFace,
            SelectedLine
        }
        private PendingHitType _pendingHitType = PendingHitType.None;
        private VertexPair _pendingEdgePair;
        private int _pendingFaceIndex = -1;
        private int _pendingLineIndex = -1;

        // ギズモ設定
        private Vector2 _gizmoScreenOffset = new Vector2(60, -60);  // 重心からのスクリーンオフセット（右上）
        private float _handleHitRadius = 10f;  // 軸先端のヒット半径（ピクセル）

        // 共有AxisGizmo（描画・ヒットテスト・デルタ計算用）
        private AxisGizmo _axisGizmo = new AxisGizmo();

        private float _handleSize = 8f;  // 軸先端のハンドルサイズ（ピクセル）
        private float _centerSize = 14f;  // 中央四角のサイズ（ピクセル）
        private float _screenAxisLength = 50f;  // 軸の長さ（ピクセル）

        // 最後のマウス位置（ホバー検出用）
        private Vector2 _lastMousePos;
        private ToolContext _lastContext;

        // 修飾キー状態（ドラッグ開始時に保存）
        private bool _shiftHeld = false;
        private bool _ctrlHeld = false;

        // === IEditTool実装 ===

        /// <summary>
        /// MouseDown処理
        /// 
        /// 【Phase 6: 責任分離】
        /// 選択処理はSimpleMeshFactoryが担当（MouseDown時に即座に反映）。
        /// MoveToolは以下のみを担当:
        /// 1. 軸ギズモのヒットテスト → 軸ドラッグ開始
        /// 2. 既に選択されているものがあれば → 移動準備
        /// 
        /// return true: イベントを消費（軸ドラッグ開始時）
        /// return false: SimpleMeshFactoryに処理を委譲
        /// </summary>
        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_state != MoveState.Idle)
            {
                // 前回の操作が完了していない場合（MouseUpが届かなかった場合など）
                // または二段階呼び出しの2回目で選択がクリアされた後の再呼び出し時
                // 強制的にリセットして新しい操作を受け付ける
                Reset();
            }

            _mouseDownScreenPos = mousePos;
            _lastMousePos = mousePos;
            _lastContext = ctx;

            // 修飾キー状態を保存
            _shiftHeld = Event.current != null && Event.current.shift;
            _ctrlHeld = Event.current != null && Event.current.control;

            // ================================================================
            // ★ ホバー結果から「マウス直下の要素」を保存
            //
            // 【必須機能：クリック＋ドラッグ = 選択と同時に移動】
            // この機能はモデリングにおいて必須の操作パターンである。
            // 選択処理はPolyLing_Input側が担当するが、MoveToolは
            // 「何がクリックされたか」を独自に保持しておく必要がある。
            // 理由: PolyLing_Inputは二段階呼び出し（1回目:軸チェック、
            //        選択処理後に2回目:移動準備）を行うが、2回目の
            //        UpdateAffectedVerticesが選択更新を拾えないケースがある。
            //        ホバー結果を保持することで、StartMoveFromSelectionで
            //        フォールバック追加が可能になる。
            //
            // 【仕様】
            // ・クリック＋ドラッグ: 選択と同時に移動開始（Shift/修飾キー不問）
            // ・ドラッグ開始時はCtrlキーを無視する（除外選択はクリックのみ）
            // ================================================================
            _hitVertexOnMouseDown = ctx.GetHoverVertexIndex();

            // ================================================================
            // 選択はPolyLing_Inputが処理する
            // ここでは現在の選択済み頂点を取得する
            // ================================================================
            UpdateAffectedVertices(ctx);

            // v2.1: 全メッシュの選択頂点数を計算
            int totalAffectedCount = GetTotalAffectedCount(ctx);

            // ================================================================
            // 1. 軸ギズモのヒットテスト（最優先）
            // ================================================================
            if (totalAffectedCount > 0)
            {
                UpdateGizmoCenter(ctx);
                _hitAxisOnMouseDown = FindAxisHandleAtScreenPos(mousePos, ctx);
                if (_hitAxisOnMouseDown != AxisType.None)
                {
                    if (_hitAxisOnMouseDown == AxisType.Center)
                    {
                        StartCenterDrag(ctx);
                    }
                    else
                    {
                        StartAxisDrag(ctx, _hitAxisOnMouseDown);
                    }
                    return true;  // 軸ドラッグ開始、イベント消費
                }
            }

            // ================================================================
            // 2. 移動準備
            //    - 選択済み要素がある → そのままPendingAction
            //    - 選択はないがホバー頂点がある → PolyLing_Inputがこの後選択するので
            //      PendingActionに入って待機（StartMoveFromSelectionで解決）
            // ================================================================
            if (totalAffectedCount > 0 || _hitVertexOnMouseDown >= 0)
            {
                _state = MoveState.PendingAction;
                _pendingHitType = PendingHitType.Selection;
                return false;  // イベントは消費しない（PolyLing_Inputの選択処理を妨げない）
            }

            return false;  // 何もない（Idle維持を明示）
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            _lastMousePos = mousePos;
            _lastContext = ctx;

            switch (_state)
            {
                case MoveState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        // ★ 選択済み要素の移動開始（選択と同時移動を含む）
                        // 【仕様】ドラッグ開始時はCtrlキーを無視する。
                        //   Ctrl+クリック（ドラッグなし）→ 除外選択（PolyLing_InputのHandleClickが処理）
                        //   Ctrl+ドラッグ → 通常の移動（ここで処理。Ctrl状態は見ない）
                        StartMoveFromSelection(ctx);
                    }
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.MovingVertices:
                    MoveSelectedVertices(delta, ctx);
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.AxisDragging:
                    MoveVerticesAlongAxis(mousePos, ctx);
                    _lastAxisDragScreenPos = mousePos;
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.CenterDragging:
                    MoveSelectedVertices(delta, ctx);
                    ctx.Repaint?.Invoke();
                    return true;
            }

            // ホバー更新
            UpdateAffectedVertices(ctx);
            if (_state == MoveState.Idle && GetTotalAffectedCount(ctx) > 0)
            {
                UpdateGizmoCenter(ctx);
                var newHovered = FindAxisHandleAtScreenPos(mousePos, ctx);
                if (newHovered != _hoveredAxis)
                {
                    _hoveredAxis = newHovered;
                    ctx.Repaint?.Invoke();
                }
            }

            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            bool handled = false;

            switch (_state)
            {
                case MoveState.MovingVertices:
                    EndVertexMove(ctx);
                    handled = true;
                    break;

                case MoveState.AxisDragging:
                    EndAxisDrag(ctx);
                    handled = true;
                    break;

                case MoveState.CenterDragging:
                    EndCenterDrag(ctx);
                    handled = true;
                    break;

                case MoveState.PendingAction:
                    // クリック（ドラッグなし）はSimpleMeshFactory側で選択処理
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateAffectedVertices(ctx);

            // v2.1: プライマリ＋セカンダリの全選択頂点数
            int totalAffected = GetTotalAffectedCount(ctx);
            if (totalAffected == 0)
                return;

            UpdateGizmoCenter(ctx);

            // ホバー更新（MouseMoveイベントがない場合用）
            if (_state == MoveState.Idle)
            {
                _hoveredAxis = FindAxisHandleAtScreenPos(_lastMousePos, ctx);
            }

            DrawAxisGizmo(ctx);
        }

        /// <summary>
        /// v2.1: 全メッシュの選択頂点数を計算
        /// </summary>
        private int GetTotalAffectedCount(ToolContext ctx)
        {
            int total = 0;
            foreach (var kv in _multiMeshAffectedVertices)
            {
                total += kv.Value.Count;
            }
            return total;
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Magnet"), EditorStyles.miniBoldLabel);

            // MoveSettingsを直接編集（Undo検出はGUI_Tools側で行う）
            _settings.UseMagnet = EditorGUILayout.Toggle(T("Enable"), _settings.UseMagnet);

            using (new EditorGUI.DisabledScope(!_settings.UseMagnet))
            {
                _settings.MagnetRadius = EditorGUILayout.Slider(T("Radius"), _settings.MagnetRadius, _settings.MIN_MAGNET_RADIUS, _settings.MAX_MAGNET_RADIUS);//スライダーの上限下限
                _settings.MagnetFalloff = (FalloffType)EditorGUILayout.EnumPopup(T("Falloff"), _settings.MagnetFalloff);
            }

            // ギズモ設定（Undo対象外）
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Gizmo"), EditorStyles.miniBoldLabel);
            _gizmoScreenOffset.x = EditorGUILayout.Slider(T("OffsetX"), _gizmoScreenOffset.x, _settings.MIN_SCREEN_OFFSET_X, _settings.MAX_SCREEN_OFFSET_X);//スライダーの上限下限
            _gizmoScreenOffset.y = EditorGUILayout.Slider(T("OffsetY"), _gizmoScreenOffset.y, _settings.MIN_SCREEN_OFFSET_Y, _settings.MAX_SCREEN_OFFSET_Y);//スライダーの上限下限

            // 選択情報表示
            EditorGUILayout.Space(5);
            int totalAffected = GetTotalAffectedCount(_lastContext);
            if (totalAffected > 0)
            {
                EditorGUILayout.HelpBox(T("TargetVertices", totalAffected), MessageType.None);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateAffectedVertices(ctx);
        }

        public void OnDeactivate(ToolContext ctx) { Reset(); }

        public void Reset()
        {
            _state = MoveState.Idle;
            _hitVertexOnMouseDown = -1;
            _hitAxisOnMouseDown = AxisType.None;
            _draggingAxis = AxisType.None;
            _hoveredAxis = AxisType.None;
            _meshTransforms.Clear();
            _multiMeshDragStartPositions.Clear();
            _multiMeshAffectedVertices.Clear();

            // Pending情報クリア
            _pendingHitType = PendingHitType.None;
            _pendingEdgePair = default;
            _pendingFaceIndex = -1;
            _pendingLineIndex = -1;

            // 修飾キー状態クリア
            _shiftHeld = false;
            _ctrlHeld = false;
        }

        // === 影響を受ける頂点の更新 ===

        private void UpdateAffectedVertices(ToolContext ctx)
        {
            _multiMeshAffectedVertices.Clear();

            var model = ctx.Model;
            if (model == null) return;

            // 全選択メッシュを対等にイテレート
            foreach (int meshIdx in model.SelectedMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext == null || !meshContext.HasSelection)
                    continue;

                var meshObject = meshContext.MeshObject;
                if (meshObject == null)
                    continue;

                var affected = new HashSet<int>();
                foreach (var v in meshContext.SelectedVertices)
                    affected.Add(v);
                foreach (var edge in meshContext.SelectedEdges)
                {
                    affected.Add(edge.V1);
                    affected.Add(edge.V2);
                }
                foreach (var faceIdx in meshContext.SelectedFaces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                    {
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                            affected.Add(vIdx);
                    }
                }
                foreach (var lineIdx in meshContext.SelectedLines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2)
                        {
                            affected.Add(face.VertexIndices[0]);
                            affected.Add(face.VertexIndices[1]);
                        }
                    }
                }

                if (affected.Count > 0)
                {
                    _multiMeshAffectedVertices[meshIdx] = affected;
                }
            }
        }

        // === 頂点移動処理 ===

        private void StartVertexMove(ToolContext ctx)
        {
            var oldSelection = new HashSet<int>(ctx.SelectedVertices);
            bool selectionChanged = false;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (_hitVertexOnMouseDown >= 0 && ctx.SelectedVertices.Contains(_hitVertexOnMouseDown))
                {
                    ctx.SelectedVertices.Remove(_hitVertexOnMouseDown);
                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.DeselectVertex(_hitVertexOnMouseDown);
                    }
                    selectionChanged = true;
                }

                if (selectionChanged)
                {
                    ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
                }

                _state = MoveState.Idle;
                return;  // 移動しない
            }

            // 未選択頂点からドラッグ開始した場合
            // _hitVertexOnMouseDownはctx.FirstSelectedMeshObject（選択メッシュリストの先頭）のローカル頂点インデックス
            bool hitVertexIsAffected = false;
            if (_hitVertexOnMouseDown >= 0)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    if (kv.Value.Contains(_hitVertexOnMouseDown))
                    {
                        hitVertexIsAffected = true;
                        break;
                    }
                }
            }
            if (_hitVertexOnMouseDown >= 0 && !hitVertexIsAffected)
            {
                if (_shiftHeld)
                {
                    // Shift: 追加選択（既存維持）
                    ctx.SelectedVertices.Add(_hitVertexOnMouseDown);
                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.SelectVertex(_hitVertexOnMouseDown, true);  // addToSelection = true
                    }
                }
                else
                {
                    // 修飾なし: 新規選択（既存クリア）
                    ctx.SelectedVertices.Clear();
                    ctx.SelectedVertices.Add(_hitVertexOnMouseDown);

                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.ClearAll();
                        ctx.SelectionState.SelectVertex(_hitVertexOnMouseDown, false);
                    }
                }

                selectionChanged = true;
                UpdateAffectedVertices(ctx);
            }

            if (selectionChanged)
            {
                ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
            }

            BeginMove(ctx);
        }

        private void SelectAndStartMove_Edge(ToolContext ctx)
        {
            if (ctx.SelectionState == null) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Edges.Contains(_pendingEdgePair))
                {
                    ctx.SelectionState.DeselectEdge(_pendingEdgePair);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectEdge(_pendingEdgePair, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectEdge(_pendingEdgePair, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Face(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingFaceIndex < 0) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Faces.Contains(_pendingFaceIndex))
                {
                    ctx.SelectionState.DeselectFace(_pendingFaceIndex);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectFace(_pendingFaceIndex, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectFace(_pendingFaceIndex, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Line(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingLineIndex < 0) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Lines.Contains(_pendingLineIndex))
                {
                    ctx.SelectionState.DeselectLine(_pendingLineIndex);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectLine(_pendingLineIndex, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectLine(_pendingLineIndex, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        /// <summary>
        /// 選択済み要素の移動を開始する
        ///
        /// 【必須機能：クリック＋ドラッグ = 選択と同時に移動】
        /// モデリング操作の基本パターンであり、以下の全ケースで動作する必要がある:
        /// ・何も選択されていない状態で頂点をクリック＋ドラッグ → その頂点を移動
        /// ・既存選択がある状態でShift+新頂点クリック＋ドラッグ → 全選択頂点を移動（新頂点含む）
        /// ・既存選択がある状態でクリック＋ドラッグ → 全選択頂点を移動
        /// ・ドラッグ開始時はCtrlキーを無視する（除外選択はクリックのみで行う仕様）
        ///
        /// 【アーキテクチャ上の注意】
        /// 選択処理はPolyLing_Input側が担当し、MoveToolは移動のみ担当するが、
        /// 二段階呼び出しのタイミング問題で、UpdateAffectedVerticesが最新の
        /// 選択状態を取得できないケースがある。EnsureHitVertexInAffectedSetは
        /// その場合のフォールバックとして、ホバーで検出した頂点を影響セットに
        /// 明示的に追加する。
        /// </summary>
        private void StartMoveFromSelection(ToolContext ctx)
        {
            UpdateAffectedVertices(ctx);

            // ★ フォールバック: ホバーで検出した頂点が影響セットに含まれていない場合、
            //    明示的に追加する（選択と同時移動のエッジケース対策）
            EnsureHitVertexInAffectedSet(ctx);

            BeginMove(ctx);
        }

        /// <summary>
        /// OnMouseDownで保存したホバー頂点が影響セットに含まれていることを保証する。
        /// 
        /// PolyLing_InputのApplySelectionOnMouseDownで選択は追加されているはずだが、
        /// UpdateAffectedVerticesのタイミングで拾えない場合に備えた防御的コード。
        /// 選択されたメッシュを走査し、ヒット頂点がそのメッシュの選択に含まれていれば
        /// 影響セットに追加する。選択にも含まれていない場合は、頂点インデックスが有効な
        /// 最初のメッシュの選択と影響セットの両方に追加する。
        /// </summary>
        private void EnsureHitVertexInAffectedSet(ToolContext ctx)
        {
            if (_hitVertexOnMouseDown < 0 || ctx.Model == null)
                return;

            // 既に影響セットに含まれていればOK
            foreach (var kv in _multiMeshAffectedVertices)
            {
                if (kv.Value.Contains(_hitVertexOnMouseDown))
                    return;
            }

            // 影響セットに含まれていない → 選択済みメッシュから探して追加
            // まず、選択に含まれているメッシュを探す
            foreach (int meshIdx in ctx.Model.SelectedMeshIndices)
            {
                var mc = ctx.Model.GetMeshContext(meshIdx);
                if (mc?.MeshObject == null) continue;
                if (_hitVertexOnMouseDown >= mc.MeshObject.VertexCount) continue;

                if (mc.SelectedVertices.Contains(_hitVertexOnMouseDown))
                {
                    // 選択には含まれているがUpdateAffectedVerticesで拾えなかった
                    if (!_multiMeshAffectedVertices.ContainsKey(meshIdx))
                        _multiMeshAffectedVertices[meshIdx] = new HashSet<int>();
                    _multiMeshAffectedVertices[meshIdx].Add(_hitVertexOnMouseDown);
                    return;
                }
            }

            // 最終フォールバック: どのメッシュの選択にも含まれていない場合
            // （PolyLing_Inputの選択処理が間に合わなかったケース）
            // 頂点インデックスが有効な最初のメッシュに追加する
            foreach (int meshIdx in ctx.Model.SelectedMeshIndices)
            {
                var mc = ctx.Model.GetMeshContext(meshIdx);
                if (mc?.MeshObject == null) continue;
                if (_hitVertexOnMouseDown >= mc.MeshObject.VertexCount) continue;

                mc.SelectedVertices.Add(_hitVertexOnMouseDown);
                if (!_multiMeshAffectedVertices.ContainsKey(meshIdx))
                    _multiMeshAffectedVertices[meshIdx] = new HashSet<int>();
                _multiMeshAffectedVertices[meshIdx].Add(_hitVertexOnMouseDown);
                return;
            }
        }

        private void BeginMove(ToolContext ctx)
        {
            if (GetTotalAffectedCount(ctx) == 0)
            {
                _state = MoveState.Idle;
                return;
            }

            // 全選択メッシュの開始位置を保存し、per-meshトランスフォームを作成
            _multiMeshDragStartPositions.Clear();
            _meshTransforms.Clear();

            if (ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject == null) continue;

                    var startPos = (Vector3[])meshContext.MeshObject.Positions.Clone();
                    _multiMeshDragStartPositions[kv.Key] = startPos;

                    IVertexTransform transform;
                    if (UseMagnet)
                        transform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
                    else
                        transform = new SimpleMoveTransform();

                    transform.Begin(meshContext.MeshObject, kv.Value, startPos);
                    _meshTransforms[kv.Key] = transform;
                }
            }

            _state = MoveState.MovingVertices;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void MoveSelectedVertices(Vector2 screenDelta, ToolContext ctx)
        {
            if (GetTotalAffectedCount(ctx) == 0 || _meshTransforms.Count == 0)
                return;

            // AxisGizmoで自由移動デルタを計算
            SyncAxisGizmo(ctx);
            Vector3 worldDelta = _axisGizmo.ComputeFreeDelta(screenDelta, ctx);

            // 全メッシュのトランスフォームにdeltaを適用
            foreach (var kv in _meshTransforms)
            {
                kv.Value.Apply(worldDelta);
            }

            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndVertexMove(ToolContext ctx)
        {
            if (_meshTransforms.Count == 0)
            {
                _meshTransforms.Clear();
                _multiMeshDragStartPositions.Clear();
                ctx.ExitTransformDragging?.Invoke();
                return;
            }

            // 全メッシュからdiffを収集
            var allEntries = new List<MeshMoveEntry>();

            foreach (var kv in _meshTransforms)
            {
                int meshIdx = kv.Key;
                var transform = kv.Value;
                transform.End();

                var affectedIndices = transform.GetAffectedIndices();
                var originalPositions = transform.GetOriginalPositions();
                var currentPositions = transform.GetCurrentPositions();

                var movedIndices = new List<int>();
                var oldPositions = new List<Vector3>();
                var newPositions = new List<Vector3>();

                for (int i = 0; i < affectedIndices.Length; i++)
                {
                    if (Vector3.Distance(currentPositions[i], originalPositions[i]) > 0.0001f)
                    {
                        movedIndices.Add(affectedIndices[i]);
                        oldPositions.Add(originalPositions[i]);
                        newPositions.Add(currentPositions[i]);
                    }
                }

                if (movedIndices.Count > 0)
                {
                    allEntries.Add(new MeshMoveEntry
                    {
                        MeshContextIndex = meshIdx,
                        Indices = movedIndices.ToArray(),
                        OldPositions = oldPositions.ToArray(),
                        NewPositions = newPositions.ToArray()
                    });

                    // OriginalPositions更新
                    var meshContext = ctx.Model?.GetMeshContext(meshIdx);
                    if (meshContext?.OriginalPositions != null)
                    {
                        var meshObject = meshContext.MeshObject;
                        foreach (int vIdx in movedIndices)
                        {
                            if (vIdx >= 0 && vIdx < meshContext.OriginalPositions.Length && meshObject != null)
                                meshContext.OriginalPositions[vIdx] = meshObject.Vertices[vIdx].Position;
                        }
                    }
                }
            }

            if (allEntries.Count > 0 && ctx.UndoController != null)
            {
                int totalMoved = 0;
                foreach (var e in allEntries) totalMoved += e.Indices.Length;

                string actionName = UseMagnet
                    ? $"Magnet Move {totalMoved} Vertices"
                    : $"Move {totalMoved} Vertices";

                var record = new MultiMeshVertexMoveRecord(allEntries.ToArray());
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, actionName);
            }

            _meshTransforms.Clear();
            _multiMeshDragStartPositions.Clear();
            ctx.ExitTransformDragging?.Invoke();
        }

        // === 軸ドラッグ処理 ===

        private void StartAxisDrag(ToolContext ctx, AxisType axis)
        {
            _draggingAxis = axis;
            _lastAxisDragScreenPos = _mouseDownScreenPos;

            // 全選択メッシュの開始位置を保存し、per-meshトランスフォームを作成
            _multiMeshDragStartPositions.Clear();
            _meshTransforms.Clear();

            if (ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject == null) continue;

                    var startPos = (Vector3[])meshContext.MeshObject.Positions.Clone();
                    _multiMeshDragStartPositions[kv.Key] = startPos;

                    IVertexTransform transform;
                    if (UseMagnet)
                        transform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
                    else
                        transform = new SimpleMoveTransform();

                    transform.Begin(meshContext.MeshObject, kv.Value, startPos);
                    _meshTransforms[kv.Key] = transform;
                }
            }

            _state = MoveState.AxisDragging;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void MoveVerticesAlongAxis(Vector2 currentScreenPos, ToolContext ctx)
        {
            if (_meshTransforms.Count == 0)
                return;

            // スクリーン上での移動量を計算（フレーム差分）
            Vector2 screenDelta = currentScreenPos - _lastAxisDragScreenPos;
            _lastAxisDragScreenPos = currentScreenPos;
            
            if (screenDelta.sqrMagnitude < 0.001f)
                return;

            // AxisGizmoで軸拘束デルタを計算
            SyncAxisGizmo(ctx);
            Vector3 worldDeltaFrame = _axisGizmo.ComputeAxisDelta(
                screenDelta, (AxisGizmo.AxisType)(int)_draggingAxis, ctx);

            // 全メッシュのトランスフォームにdeltaを適用
            foreach (var kv in _meshTransforms)
            {
                kv.Value.Apply(worldDeltaFrame);
            }

            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndAxisDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
            _draggingAxis = AxisType.None;
        }

        // === 中央ドラッグ処理 ===

        private void StartCenterDrag(ToolContext ctx)
        {
            // 全選択メッシュの開始位置を保存し、per-meshトランスフォームを作成
            _multiMeshDragStartPositions.Clear();
            _meshTransforms.Clear();

            if (ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject == null) continue;

                    var startPos = (Vector3[])meshContext.MeshObject.Positions.Clone();
                    _multiMeshDragStartPositions[kv.Key] = startPos;

                    IVertexTransform transform;
                    if (UseMagnet)
                        transform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
                    else
                        transform = new SimpleMoveTransform();

                    transform.Begin(meshContext.MeshObject, kv.Value, startPos);
                    _meshTransforms[kv.Key] = transform;
                }
            }

            _state = MoveState.CenterDragging;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void EndCenterDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
        }

        // === ギズモ計算・描画 ===

        private void UpdateGizmoCenter(ToolContext ctx)
        {
            // v2.1: 複数メッシュ対応
            int totalVertices = 0;
            Vector3 sum = Vector3.zero;
            
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject == null)
                        continue;
                    
                    var meshObject = meshContext.MeshObject;
                    foreach (int vi in kv.Value)
                    {
                        if (vi >= 0 && vi < meshObject.VertexCount)
                        {
                            sum += meshObject.Vertices[vi].Position;
                            totalVertices++;
                        }
                    }
                }
            }
            
            if (totalVertices == 0)
            {
                _selectionCenter = Vector3.zero;
                _gizmoCenter = Vector3.zero;
                return;
            }

            _selectionCenter = sum / totalVertices;
            _gizmoCenter = _selectionCenter;
        }

        // GetGizmoOriginScreen, GetAxisScreenDirection, GetAxisScreenEnd は AxisGizmo に移行済み

        private void DrawAxisGizmo(ToolContext ctx)
        {
            // AxisGizmo状態を同期して描画委譲
            SyncAxisGizmo(ctx);
            _axisGizmo.Draw(ctx);
        }

        /// <summary>AxisGizmoの状態をMoveTool内部状態と同期</summary>
        private void SyncAxisGizmo(ToolContext ctx)
        {
            _axisGizmo.Center = _gizmoCenter;
            _axisGizmo.ScreenOffset = _gizmoScreenOffset;
            _axisGizmo.HandleHitRadius = _handleHitRadius;
            _axisGizmo.HandleSize = _handleSize;
            _axisGizmo.CenterSize = _centerSize;
            _axisGizmo.ScreenAxisLength = _screenAxisLength;

            // AxisType列挙値は同じ値を持つためキャスト変換
            _axisGizmo.HoveredAxis = (AxisGizmo.AxisType)(int)_hoveredAxis;
            _axisGizmo.DraggingAxis = (AxisGizmo.AxisType)(int)_draggingAxis;
        }

        // === 旧ギズモヘルパー → AxisGizmoに移行済み ===
        // DrawAxisLine, DrawAxisHandle, GetGizmoOriginScreen,
        // GetAxisScreenEnd, GetAxisScreenDirection は AxisGizmo クラスに統合

        private AxisType FindAxisHandleAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            // v2.1: プライマリ＋セカンダリの全選択頂点数
            if (GetTotalAffectedCount(ctx) == 0)
                return AxisType.None;

            UpdateGizmoCenter(ctx);
            SyncAxisGizmo(ctx);
            var result = _axisGizmo.FindAxisAtScreenPos(screenPos, ctx);
            return (AxisType)(int)result;
        }

        private Vector3 GetAxisDirection(AxisType axis)
        {
            return AxisGizmo.GetAxisDirection((AxisGizmo.AxisType)(int)axis);
        }

        // === 状態アクセス ===

        public bool IsIdle => _state == MoveState.Idle;
        public bool IsMoving => _state == MoveState.MovingVertices || _state == MoveState.AxisDragging || _state == MoveState.CenterDragging;

        // === 辺・面のヒットテスト ===

        private bool IsClickOnSelectedEdge(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (var edge in ctx.SelectionState.Edges)
            {
                if (edge.V1 < 0 || edge.V1 >= ctx.FirstSelectedMeshObject.VertexCount ||
                    edge.V2 < 0 || edge.V2 >= ctx.FirstSelectedMeshObject.VertexCount)
                    continue;

                Vector3 p1 = ctx.FirstSelectedMeshObject.Vertices[edge.V1].Position;
                Vector3 p2 = ctx.FirstSelectedMeshObject.Vertices[edge.V2].Position;

                // DisplayMatrixを適用（表示座標系に変換）
                p1 = ctx.DisplayMatrix.MultiplyPoint3x4(p1);
                p2 = ctx.DisplayMatrix.MultiplyPoint3x4(p2);

                Vector2 sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < hitDistance)
                    return true;
            }

            return false;
        }

        private bool IsClickOnSelectedFace(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
                return false;

            foreach (int faceIdx in ctx.SelectionState.Faces)
            {
                if (faceIdx < 0 || faceIdx >= ctx.FirstSelectedMeshObject.FaceCount)
                    continue;

                var face = ctx.FirstSelectedMeshObject.Faces[faceIdx];
                if (face.VertexCount < 3)
                    continue;

                var screenPoints = new List<Vector2>();
                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < ctx.FirstSelectedMeshObject.VertexCount)
                    {
                        Vector3 p = ctx.FirstSelectedMeshObject.Vertices[vIdx].Position;
                        // DisplayMatrixを適用（表示座標系に変換）
                        p = ctx.DisplayMatrix.MultiplyPoint3x4(p);
                        Vector2 sp = ctx.WorldToScreenPos(p, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                        screenPoints.Add(sp);
                    }
                }

                if (screenPoints.Count >= 3 && IsPointInPolygon(mousePos, screenPoints))
                    return true;
            }

            return false;
        }

        private bool IsClickOnSelectedLine(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (int lineIdx in ctx.SelectionState.Lines)
            {
                if (lineIdx < 0 || lineIdx >= ctx.FirstSelectedMeshObject.FaceCount)
                    continue;

                var face = ctx.FirstSelectedMeshObject.Faces[lineIdx];
                if (face.VertexCount != 2)
                    continue;

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                if (v1 < 0 || v1 >= ctx.FirstSelectedMeshObject.VertexCount ||
                    v2 < 0 || v2 >= ctx.FirstSelectedMeshObject.VertexCount)
                    continue;

                Vector3 p1 = ctx.FirstSelectedMeshObject.Vertices[v1].Position;
                Vector3 p2 = ctx.FirstSelectedMeshObject.Vertices[v2].Position;

                // DisplayMatrixを適用（表示座標系に変換）
                p1 = ctx.DisplayMatrix.MultiplyPoint3x4(p1);
                p2 = ctx.DisplayMatrix.MultiplyPoint3x4(p2);

                Vector2 sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < hitDistance)
                    return true;
            }

            return false;
        }

        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }
    }
}