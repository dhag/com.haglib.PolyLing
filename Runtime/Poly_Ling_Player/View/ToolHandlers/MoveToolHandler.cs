// MoveToolHandler.cs
// 移動モードの IPlayerToolHandler 実装。
// Editor MoveTool と同等の状態機・IVertexTransform・AxisGizmo を使用する。
// Runtime/Poly_Ling_Player/View/ に配置
//
// ================================================================
// 【他ツールでの流用について】
//
// 本ハンドラは VertexMove モード専用ではなく、カテゴリ 1 の編集ツール
// (EdgeBevel / FlipFace / FaceExtrude / Solidify 等) の「選択・矩形選択・
// Shift/Ctrl 修飾による選択追加・Ctrl 抑止ロジック」の共通基盤としても使う。
//
// 各ツールハンドラは内部に MoveToolHandler を 1 つ参照し、以下の 2 種類のフック
// を差し込むことでツール固有の動作を乗せる:
//
//   1) OnLeftClickExtra:
//        クリック (ドラッグ閾値を超えずに離した瞬間) でツール動作を発火させたい
//        ツール用 (例: FlipFace は対象面クリックで即反転)。
//        OnLeftClick の選択処理が終わった末尾で呼ばれる。
//
//   2) OnDragStartExtra:
//        ドラッグ開始時 (PendingAction → 移動開始の直前) で「移動」の代わりに
//        ツール動作を発火させたいツール用 (例: EdgeBevel は辺ドラッグで幅調整)。
//        戻り値 true で通常の移動処理 (BeginMove + MovingVertices 遷移) を抑制する。
//
// Selection.Mode を各ツール進入時に絞る (EdgeBevel → Edge、FlipFace → Face 等)
// ことで、MoveToolHandler 内部の GetHoverElement / 矩形選択 / 単独クリック選択は
// 自動的にその要素タイプだけに応答する。要素タイプ切替は MoveToolHandler の
// 既存ロジック (mode.Has(...) 判定) がそのまま活きる。
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public class MoveToolHandler : IPlayerToolHandler, IPlayerGizmoProvider, IPlayerPressHandler
    {
        // ================================================================
        // 状態機（Editor MoveTool と同等）
        // ================================================================
        private enum MoveState { Idle, PendingAction, MovingVertices, AxisDragging, CenterDragging, ToolDragging }
        private MoveState _state = MoveState.Idle;

        // ================================================================
        // 外部注入コールバック
        // ================================================================
        public Action<MeshContext> OnSyncMeshPositions;
        public Action OnRepaint;
        public Action<Vector2, Vector2> OnBoxSelectUpdate;
        public Action OnBoxSelectEnd;
        public Action<System.Collections.Generic.List<Vector2>> OnLassoSelectUpdate;
        public Action OnLassoSelectEnd;
        public Action OnEnterTransformDragging;
        public Action OnExitTransformDragging;

        /// <summary>
        /// 選択確定を GPU まで同期させる（移動開始時に 1 回だけ使う）。
        ///
        /// 【なぜ必要か】掴んだ瞬間に選択を確定すると、その通知は
        /// PlayerSelectionOps.OnSelectionChanged → MeshSceneRenderer.NotifySelectionChanged
        /// → UnifiedSystemAdapter.RequestNormal() となり、Normal フレームを 1 回
        /// 要求するだけで同期自体は次フレームに委ねられる。ところが直後に
        /// OnEnterTransformDragging で TransformDragging へ入るため、その Normal は
        /// 一度も実行されずに握り潰される。TransformDragging は
        /// AllowSelectedDrawableMeshSync=false かつ AllowUnselectedOverlay=false で、
        /// さらに RequestNormal() 自体がドラッグ中は無視されるため、
        /// ドラッグが終わるまで選択が GPU へ届かず表示が追従しなくなる。
        /// そこで TransformDragging へ入る前に、同期的に選択を反映させる。
        /// 選択が実際に変わったときだけ呼ぶこと（毎フレーム呼ぶと全再描画になる）。
        /// </summary>
        public Action OnCommitSelectionSync;
        public Action OnEnterBoxSelecting;
        public Action OnReadBackVertexFlags;
        public Action OnExitBoxSelecting;
        public Action OnRequestNormal;
        public Action OnClearMouseHover;

        // ================================================================
        // ツール流用フック (EdgeBevel / FlipFace / FaceExtrude / Solidify 等)
        // 未設定 (null) なら MoveToolHandler は純粋な移動モードとして動作。
        // 各ツール進入時に設定、脱出時に null に戻すこと。
        // ================================================================
        /// <summary>
        /// クリック (ドラッグ閾値を超えずに離した) 完了時に呼ばれる追加フック。
        /// 引数: (クリック時のヒット要素, 修飾キー)。
        /// 用途例: FlipFace が対象面の単独クリックで即座に反転を実行するなど。
        /// 選択処理 (ApplyElementClick) は既に終わっているので、
        /// このフックはツール固有の追加動作だけを書く。
        /// </summary>
        public Action<PlayerHoverElement, ModifierKeys> OnLeftClickExtra;

        /// <summary>
        /// ドラッグ開始確定時 (閾値超え、未選択要素の選択処理も完了) に呼ばれる追加フック。
        /// 引数: (ドラッグ開始時のヒット要素, 修飾キー)。
        /// 戻り値: true を返すと通常の移動処理 (BeginMove + MovingVertices 遷移) を
        ///         抑制し、_state を ToolDragging に遷移させ、以降の OnLeftDrag /
        ///         OnLeftDragEnd は OnToolDragExtra / OnToolDragEndExtra に委譲する。
        ///         false なら通常の移動動作が発火する。
        /// 用途例: EdgeBevel は辺上ドラッグ開始でベベルセッションを開始し true を返す。
        /// </summary>
        public Func<PlayerHoverElement, ModifierKeys, bool> OnDragStartExtra;

        /// <summary>
        /// OnDragStartExtra が true を返した後のドラッグ継続時に呼ばれる。
        /// 引数: (現在スクリーン座標, 前フレームからの差分, 修飾キー)。
        /// 用途例: EdgeBevel の幅更新、FaceExtrude の押し出し距離更新。
        /// </summary>
        public Action<Vector2, Vector2, ModifierKeys> OnToolDragExtra;

        /// <summary>
        /// OnDragStartExtra が true を返した後のドラッグ終了時に呼ばれる。
        /// 引数: (終了時スクリーン座標, 修飾キー)。
        /// 用途例: EdgeBevel のベベル確定 + Undo 記録。
        /// </summary>
        public Action<Vector2, ModifierKeys> OnToolDragEndExtra;


        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;
        public Func<Vector2[]>  GetScreenPositions;
        public Func<int, int>   GetVertexOffset;
        public Func<int, bool>  IsVertexVisible;
        public Func<float>      GetViewportHeight;
        public Func<Camera>     GetCamera;
        /// <summary>
        /// パネル高さ（ピクセル）。AxisGizmo のヒットテスト・描画用に
        /// ToViewportCoord（Y=0が下）→ IMGUI 系（Y=0が上）のY反転に使う。
        /// </summary>
        public Func<float>      GetPanelHeight;
        /// <summary>毎フレーム最新の ToolContext を返すコールバック（AxisGizmo 用）。</summary>
        public Func<ToolContext> GetToolContext;

        // ================================================================
        // マグネット設定
        // ================================================================
        public bool        UseMagnet     { get; set; } = false;
        public float       MagnetRadius  { get; set; } = 0.5f;
        public FalloffType MagnetFalloff { get; set; } = FalloffType.Smooth;
        public DistanceMode MagnetDistanceMode { get; set; } = DistanceMode.Euclidean;

        // ================================================================
        // ギズモオフセット設定（エディタ版 MoveTool.DrawSettingsUI のギズモ設定に対応）
        // ================================================================

        /// <summary>ギズモのスクリーンオフセット X。</summary>
        public float GizmoScreenOffsetX
        {
            get => _axisGizmo.ScreenOffset.x;
            set => _axisGizmo.ScreenOffset = new Vector2(value, _axisGizmo.ScreenOffset.y);
        }

        /// <summary>ギズモのスクリーンオフセット Y。</summary>
        public float GizmoScreenOffsetY
        {
            get => _axisGizmo.ScreenOffset.y;
            set => _axisGizmo.ScreenOffset = new Vector2(_axisGizmo.ScreenOffset.x, value);
        }

        /// <summary>
        /// 現在の選択状態で移動対象となる頂点の総数を返す。
        /// エディタ版 MoveTool.GetTotalAffectedCount() に対応。
        /// </summary>
        public int GetTotalAffectedCount()
        {
            int total = 0;
            foreach (var kv in _affectedVertices)
                total += kv.Value.Count;
            return total;
        }

        // ================================================================
        // 内部
        // ================================================================
        private readonly PlayerSelectionOps               _selectionOps;
        private          ProjectContext                    _project;
        private          MeshUndoController               _undoController;

        private const float DragThreshold = 4f;
        private Vector2  _mouseDownPos;

        /// <summary>
        /// マウスダウン時のスクリーン座標（パネルローカル, Y=0下）。OnLeftDragBegin で設定される。
        /// ToolDragging 系ツール（EdgeBevel / EdgeExtrude / FaceExtrude）はドラッグ開始フック
        /// (OnDragStartExtra) が座標を持たないため、開始原点としてこれを参照する。
        /// これが無いと _mouseDownScreenPos に画面隅(zero)が入り、量がマウス移動と連動しなくなる。
        /// </summary>
        public Vector2 MouseDownPos => _mouseDownPos;

        private bool     _shiftHeld;
        private bool     _ctrlHeld;
        private PlayerHoverElement _elemOnMouseDown;

        // ── 押下フェーズ（ボタンダウン〜しきい値超え）────────────────────
        //
        // 従来はしきい値を越えた瞬間に「押下位置→現在位置」の差分を一括適用していたため、
        // 開始時に 4px 超の飛びが出ていた。押下時に対象と原点を確定し、
        // しきい値未満の移動から絶対量で追従することでこれを無くす。
        //
        // 適用条件は「素の頂点移動」に限る。半径ドラッグ / 選択専用 /
        // ツール流用フック (OnDragStartExtra) / ギズモ差し替え (GizmoHitTestOverride) /
        // 軸ギズモヒット時は従来経路のままにして、影響範囲を絞る。

        /// <summary>押下時追従が有効化された（対象と原点が確定した）。</summary>
        private bool _pressActive;

        /// <summary>
        /// 押下時に掴んだギズモ軸。None なら自由移動。
        ///
        /// 軸ドラッグも自由移動と同じく、押下位置を原点とした総差分から
        /// 移動後位置を毎回 1 回で算出する。差分の足し込みはしない。
        /// </summary>
        private AxisGizmo.AxisType _pressAxis = AxisGizmo.AxisType.None;

        /// <summary>押下位置（ToImgui 済み, Y=0 上）。軸ドラッグの換算原点。</summary>
        private Vector2 _pressOriginImgui;

        /// <summary>
        /// 押下時追従で開始したのが矩形／投げ縄選択である。
        ///
        /// 移動と同じく押下時点で開始し、1px の移動から矩形が伸びる。
        /// しきい値未満で離した場合は確定せずに取り消し、クリック処理へ渡す。
        /// 移動と違い頂点位置は触らないので、巻き戻しはオーバーレイを消すだけでよい。
        /// </summary>
        private bool _pressDragSelect;

        /// <summary>移動の原点。押下位置（パネルローカル, Y=0 下）。</summary>
        private Vector2 _pressOriginPos;

        /// <summary>押下後に実際に頂点位置を動かした（キャンセル時の巻き戻し要否）。</summary>
        private bool _pressMoveStarted;

        /// <summary>押下時に選択を変更した場合の、変更前スナップショット。</summary>
        private Dictionary<int, SelectionSnapshot> _pressSelectionBefore;

        /// <summary>押下時に選択を変更した。</summary>
        private bool _pressSelectionApplied;

        /// <summary>
        /// スクリーン→ワールド換算の基準点。ドラッグ開始時の重心で固定する。
        ///
        /// UpdateGizmoState は移動後の頂点位置から重心を計算し直すため、
        /// 毎回呼ぶと換算倍率が移動に応じて変化し、総移動量が
        /// 「総スクリーン差分を 1 回で換算した値」と一致しなくなる。
        /// </summary>
        private Vector3 _frozenGizmoCenter;

        private bool _gizmoCenterFrozen;

        private Dictionary<int, HashSet<int>>     _affectedVertices = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, IVertexTransform> _meshTransforms   = new Dictionary<int, IVertexTransform>();

        private AxisGizmo          _axisGizmo      = new AxisGizmo();

        /// <summary>
        /// 選択専用モード。true のとき移動ギズモのヒットテスト/描画と移動フックを無効化し、
        /// ドラッグは常に box/lasso 選択、クリックは選択のみになる（パネル文脈での誤移動を防ぐ）。
        /// </summary>
        public bool SelectOnly;

        /// <summary>
        /// 組み込み移動ギズモ（AxisGizmo）の描画/ヒットテストを抑止する。
        /// 将来、回転・拡大など移動以外のギズモを持つモードが、自前ギズモを描くために true にする。
        /// </summary>
        public bool SuppressBuiltinGizmo;

        /// <summary>
        /// 矩形 / 投げ縄によるドラッグ選択を抑止する。
        /// true のとき、要素に当たらない位置からのドラッグは何も開始せず無反応になる。
        /// SelectOnly と併用すると「クリック選択のみ許すモード」になる（面削除モード等）。
        /// </summary>
        public bool SuppressDragSelect;

        /// <summary>
        /// 将来ギズモ用のヒットテスト差し替え。設定されている場合、組み込み移動ギズモの代わりに
        /// これを呼び、true（＝自前ギズモに当たった）ならツール操作フック（OnDragStartExtra 経由）へ入る。
        /// SelectOnly 時は呼ばれない（選択専用のためツール操作を一切行わない）。
        /// </summary>
        public Func<Vector2, ToolContext, bool> GizmoHitTestOverride;

        private AxisGizmo.AxisType _draggingAxis   = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _hoveredAxis    = AxisGizmo.AxisType.None;
        private Vector2            _lastAxisDragPos;
        private Vector2            _lastMousePos;

        private const float GizmoHandleHitRadius = 12f;
        private const float GizmoHandleSize      = 10f;
        private const float GizmoCenterSize      = 16f;
        private const float GizmoAxisLength      = 55f;

        // ================================================================
        // ドラッグ選択モード（Box / Lasso）
        // ================================================================
        public enum SelectionDragMode { Box, Lasso }
        public SelectionDragMode DragSelectMode { get; set; } = SelectionDragMode.Box;

        // ================================================================
        // 一時サブツール用「操作 1 回で終了」通知
        // ================================================================

        /// <summary>
        /// 矩形確定 / 投げ縄確定 / クリック確定のいずれかで 1 度だけ呼ばれる。
        /// 発火の直前に自身を null へ戻すため、受け側での解除は不要。
        /// </summary>
        public Action OneShotFinished;

        private void FireOneShotFinished()
        {
            var cb = OneShotFinished;
            if (cb == null) return;
            OneShotFinished = null;
            cb.Invoke();
        }

        private enum DragMode { None, Moving, BoxSelecting, LassoSelecting }
        private DragMode _dragMode = DragMode.None;

        // ================================================================
        // マグネット半径範囲・ドラッグ指定モード
        // ================================================================
        public float MinMagnetRadius { get; set; } = 0.01f;
        public float MaxMagnetRadius { get; set; } = 1.0f;

        /// <summary>
        /// MaxMagnetRadius として受け付ける絶対上限（メートル）。
        /// パネルの数値入力は上下限を自動で広げるため、歯止めが無いと
        /// 桁を打ち間違えただけで半径 100 m のような操作不能な状態になる。
        /// ParameterLimits の HardLimits と同じ役割。
        /// </summary>
        public const float MagnetRadiusHardMax = 1.0f;

        /// <summary>マグネット半径が変更されたときに呼ばれるコールバック（UIパネル更新用）。</summary>
        public Action<float> OnRadiusChanged;

        /// <summary>
        /// true の間、次のドラッグ操作は移動ではなくマグネット半径の設定として扱われる。
        /// ドラッグ終了後に自動的に false に戻る。
        /// </summary>
        public bool IsRadiusDragMode { get; set; } = false;

        private Vector2 _radiusDragStartPos;
        private bool    _inRadiusDrag;

        /// <summary>頂点移動確定時に、影響した各メッシュコンテキストを通知する（リモート連動用）。</summary>
        public Action<MeshContext> OnVerticesCommitted;

        // ================================================================
        // 初期化
        // ================================================================
        public MoveToolHandler(PlayerSelectionOps selectionOps, ProjectContext project)
        {
            _selectionOps = selectionOps ?? throw new ArgumentNullException(nameof(selectionOps));
            _project      = project;
            _axisGizmo.ScreenOffset     = new Vector2(60, -60);
            _axisGizmo.HandleHitRadius  = GizmoHandleHitRadius;
            _axisGizmo.HandleSize       = GizmoHandleSize;
            _axisGizmo.CenterSize       = GizmoCenterSize;
            _axisGizmo.ScreenAxisLength = GizmoAxisLength;
        }

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var before = CaptureAllSelectionSnapshots();

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            var clickedElem = GetHoverElement != null
                ? GetHoverElement(mode)
                : PlayerHoverElement.None;

            // 診断: クリック前の選択要素総数（ドラッグ経路と揃えて比較するため）
            int selCountBefore = CountSelectedElements();

            if (GetHoverElement != null)
                _selectionOps.ApplyElementClick(clickedElem, mods);
            else
                _selectionOps.ApplyClick(hit, mods);

            ExpandLinkedVertices();

            // 診断: クリックで何を掴み、選択が何件になったか。
            // ドラッグ経路 ("MTH.Pending") と対比できるよう同じ並びで記録する。
            PLDiag.PickRec("MTH.Click",
                (int)clickedElem.Kind, clickedElem.MeshIndex, clickedElem.VertexIndex,
                _project?.CurrentModel?.SelectedDrawableMeshIndices?.Count ?? -1,
                selCountBefore, CountSelectedElements(),
                screenPos.x, screenPos.y);

            // 診断: 掴んだメッシュが操作対象外なら、選択は書けても移動対象にならない。
            if (clickedElem.HasHit && !IsMeshOperable(clickedElem.MeshIndex))
                PLDiag.PickDump("click-mesh-not-operable");

            OnRequestNormal?.Invoke();
            RecordSelectionChange(before, CaptureAllSelectionSnapshots());

            // ツール流用フック: クリック系ツール (FlipFace 等) がここで発火
            OnLeftClickExtra?.Invoke(clickedElem, mods);

            // 一時サブツール: ドラッグに至らないクリックでも 1 回で終了させる
            FireOneShotFinished();
        }

        // ================================================================
        // IPlayerPressHandler（押下時追従）
        // ================================================================

        /// <summary>
        /// 左ボタン押下。押下位置を原点として対象を確定し、移動の準備まで済ませる。
        /// ここで BeginMove まで行うため、押下のたびに対象メッシュの Positions が
        /// 1 回コピーされる。移動せず離した場合はそのコピーが無駄になるが、
        /// 移動中につまづくよりクリック時に払う方が操作感が良い、という判断による。
        /// </summary>
        public void OnLeftButtonDown(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            ResetPressState();

            // 押下時追従の対象外（従来経路）。
            // OnDragStartExtra / GizmoHitTestOverride を持つツールは DragBegin で
            // 独自のドラッグセッションを張るため、押下時に先回りすると
            // そのフックが一度も呼ばれなくなる。ここは従来どおりに残す。
            if (IsRadiusDragMode) return;
            if (OnDragStartExtra != null || GizmoHitTestOverride != null) return;

            _shiftHeld = mods.Shift;
            _ctrlHeld  = mods.Ctrl;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            var elem    = PlayerHoverElement.None;
            var axisHit = AxisGizmo.AxisType.None;

            // 選択専用モードは要素もギズモも掴まない。常に矩形／投げ縄へ進む。
            if (!SelectOnly)
            {
                var mode = _selectionOps.SelectionState?.Mode
                        ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                            MeshSelectMode.Face   | MeshSelectMode.Line);

                // 直前の PointerMove で GPU が確定したホバーをそのまま使う。
                // PointerDown ではホバーを再計算しない（判定位置が飛ぶため）。
                elem = GetHoverElement != null
                    ? GetHoverElement(mode)
                    : (hit.HasHit
                        ? new PlayerHoverElement { Kind = PlayerHoverKind.Vertex,
                              MeshIndex = hit.MeshIndex, VertexIndex = hit.VertexIndex }
                        : PlayerHoverElement.None);

                PLDiag.PickRec("MTH.Press",
                    (int)elem.Kind, elem.MeshIndex, elem.VertexIndex,
                    elem.EdgeV1, elem.EdgeV2, elem.FaceIndex,
                    screenPos.x, screenPos.y);

                // 軸ギズモに当たっていたら、押下追従のまま軸ドラッグとして開始する。
                //
                // 判定は elem.HasHit の判定より前で行う。ギズモの矢印は
                // 重心から画面上へオフセットして描かれるため、その上に頂点が
                // 無いのが普通で、後段に置くと軸を掴めなくなる。
                if (!SuppressBuiltinGizmo)
                {
                    UpdateAffectedVertices();
                    if (HasAnyAffected())
                    {
                        UpdateGizmoState(ctx);
                        axisHit = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
                    }
                }
            }

            // 要素にもギズモにも当たっていない → 矩形／投げ縄選択を押下時点で開始する。
            if (axisHit == AxisGizmo.AxisType.None && !elem.HasHit)
            {
                BeginPressDragSelect(screenPos);
                return;
            }

            _elemOnMouseDown = elem;
            _pressOriginPos  = screenPos;

            // 未選択の要素を掴んだ場合はここで選択する。
            // Undo はまだ積まない（しきい値未満で離したら巻き戻すため）。
            // 軸ギズモを掴んだときは既存選択をその軸へ動かす操作なので選択を変えない。
            if (axisHit == AxisGizmo.AxisType.None && !IsElemSelected(elem))
            {
                _pressSelectionBefore  = CaptureAllSelectionSnapshots();
                _selectionOps.ApplyElementClick(elem, new ModifierKeys());
                _pressSelectionApplied = true;
            }

            UpdateAffectedVertices();
            if (!HasAnyAffected())
            {
                PLDiag.PickDump("no-affected-after-press");
                RollbackPressSelection();
                return;
            }

            BeginMove();
            if (_meshTransforms.Count == 0)
            {
                PLDiag.PickDump("press-begin-move-empty");
                RollbackPressSelection();
                return;
            }

            // 換算基準を固定する（以降 UpdateGizmoState は呼ばない）。
            // ギズモ表示もこの固定中心を使う。移動に応じて重心が動くと、
            // 換算倍率とギズモ原点の両方が毎フレームずれる。
            UpdateGizmoState(ctx);
            _frozenGizmoCenter = _axisGizmo.Center;
            _gizmoCenterFrozen = true;

            _pressAxis        = axisHit;
            _pressOriginImgui = ToImgui(screenPos);
            _draggingAxis     = axisHit;

            _mouseDownPos = screenPos;
            _dragMode     = DragMode.Moving;
            _state        = axisHit == AxisGizmo.AxisType.Center
                            ? MoveState.CenterDragging
                            : (axisHit == AxisGizmo.AxisType.None
                                ? MoveState.MovingVertices
                                : MoveState.AxisDragging);
            _pressActive  = true;

            // 掴んだ瞬間に選択を変えた場合は、TransformDragging へ入る前に
            // GPU へ同期させる。詳細は OnCommitSelectionSync の宣言部を参照。
            if (_pressSelectionApplied) OnCommitSelectionSync?.Invoke();

            // 以降ドラッグ終了まで GPU ヒットテストを止め、ホバー索引を凍結する。
            OnEnterTransformDragging?.Invoke();

            PLDiag.PickRec("MTH.PressBegin",
                _meshTransforms.Count, _affectedVertices.Count, CountAffectedVertices());
        }

        /// <summary>
        /// しきい値未満の移動。押下位置からの総差分を毎回まとめて換算し、絶対量で適用する。
        /// </summary>
        public void OnLeftPressMove(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_pressActive) return;

            if (_pressDragSelect)
            {
                UpdatePressDragSelect(screenPos);
                return;
            }

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            ApplyAbsoluteFromOrigin(screenPos, ctx);
        }

        // ================================================================
        // 押下時点から始める矩形／投げ縄選択
        // ================================================================

        /// <summary>
        /// 空振り押下で矩形／投げ縄選択を開始する。
        /// 移動と同じく押下位置を原点にし、1px の移動から伸び始める。
        /// SuppressDragSelect のときは何もしない（従来どおりドラッグ全体が無反応）。
        /// </summary>
        private void BeginPressDragSelect(Vector2 screenPos)
        {
            if (SuppressDragSelect) return;

            _pressOriginPos = screenPos;
            _mouseDownPos   = screenPos;
            _state          = MoveState.Idle;

            if (DragSelectMode == SelectionDragMode.Lasso)
            {
                _dragMode = DragMode.LassoSelecting;
                _selectionOps.BeginLassoSelect(screenPos);
            }
            else
            {
                _dragMode = DragMode.BoxSelecting;
                _selectionOps.BeginBoxSelect(screenPos);
            }
            OnEnterBoxSelecting?.Invoke();

            _pressActive     = true;
            _pressDragSelect = true;

            PLDiag.PickRec("MTH.PressDragSelect",
                (int)_dragMode, x: screenPos.x, y: screenPos.y);
        }

        /// <summary>押下フェーズ中の矩形／投げ縄更新。OnLeftDrag の分岐と同じ内容。</summary>
        private void UpdatePressDragSelect(Vector2 screenPos)
        {
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnBoxSelectUpdate?.Invoke(_selectionOps.BoxStart, screenPos);
                OnRepaint?.Invoke();
            }
            else if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnLassoSelectUpdate?.Invoke(_selectionOps.LassoPoints);
                OnRepaint?.Invoke();
            }
        }

        /// <summary>
        /// しきい値未満で離されたときの矩形／投げ縄の取り消し。
        /// 頂点位置も選択も触っていないので、オーバーレイを消して状態を戻すだけでよい。
        /// </summary>
        private void CancelPressDragSelect()
        {
            if (_dragMode == DragMode.LassoSelecting) OnLassoSelectEnd?.Invoke();
            else                                      OnBoxSelectEnd?.Invoke();
            OnExitBoxSelecting?.Invoke();

            _dragMode = DragMode.None;
            _state    = MoveState.Idle;
        }

        /// <summary>
        /// しきい値を越えずに離された。押下時に開始した移動と選択を巻き戻す。
        /// 呼び出し元（PlayerVertexInteractor）はこの直後に OnLeftClick を呼ぶ。
        /// </summary>
        public void OnLeftPressCancel(Vector2 screenPos, ModifierKeys mods)
        {
            if (!_pressActive) return;

            if (_pressDragSelect)
            {
                PLDiag.PickRec("MTH.CancelDragSelect",
                    (int)_dragMode, x: screenPos.x, y: screenPos.y);
                CancelPressDragSelect();
                ResetPressState();
                return;
            }

            PLDiag.PickRec("MTH.Cancel",
                _meshTransforms.Count, _pressMoveStarted ? 1 : 0,
                _pressSelectionApplied ? 1 : 0,
                x: screenPos.x, y: screenPos.y);

            // 頂点位置を押下時点へ戻す。IVertexTransform は元位置を保持しているので
            // 累積量をゼロにするだけで復元できる。
            if (_pressMoveStarted)
                SetTotalDeltaAll(Vector3.zero);

            RollbackPressSelection();

            _meshTransforms.Clear();
            _state        = MoveState.Idle;
            _dragMode     = DragMode.None;
            _draggingAxis = AxisGizmo.AxisType.None;
            ResetPressState();

            // OnLeftButtonDown で入った TransformDragging を必ず抜ける。
            // 抜けないと GPU ヒットテストが止まったままホバーが更新されない。
            OnExitTransformDragging?.Invoke();
        }

        /// <summary>押下時に変更した選択を戻す。変更していなければ何もしない。</summary>
        private void RollbackPressSelection()
        {
            if (!_pressSelectionApplied || _pressSelectionBefore == null) return;

            var model = _project?.CurrentModel;
            if (model != null)
            {
                foreach (var kv in _pressSelectionBefore)
                {
                    var sel = model.GetMeshContext(kv.Key)?.Selection;
                    sel?.RestoreFromSnapshot(kv.Value);
                }
            }
            _pressSelectionApplied = false;
            _pressSelectionBefore  = null;
        }

        private void ResetPressState()
        {
            _pressActive           = false;
            _pressMoveStarted      = false;
            _pressSelectionApplied = false;
            _pressSelectionBefore  = null;
            _gizmoCenterFrozen     = false;
            _pressAxis             = AxisGizmo.AxisType.None;
            _pressDragSelect       = false;
        }

        /// <summary>
        /// 押下位置から現在位置までの総スクリーン差分を 1 回で換算し、絶対量として適用する。
        /// 差分の足し込みではないため、換算誤差が累積しない。
        ///
        /// 軸ギズモを掴んでいる場合も同じ規則で、押下位置からの総差分を
        /// 軸方向へ 1 回で射影する。Center は押下時の重心で固定してあるので、
        /// 移動しても換算倍率と軸のスクリーン方向が変わらない。
        /// </summary>
        private void ApplyAbsoluteFromOrigin(Vector2 screenPos, ToolContext ctx)
        {
            if (_gizmoCenterFrozen) _axisGizmo.Center = _frozenGizmoCenter;

            Vector3 worldTotal;
            if (_pressAxis != AxisGizmo.AxisType.None && _pressAxis != AxisGizmo.AxisType.Center)
            {
                // ComputeAxisDelta は +Y が画面下の差分を要求する（AxisGizmo の規約）。
                // ToImgui 済み座標どうしの差分をそのまま渡す。
                Vector2 sdYDown = ToImgui(screenPos) - _pressOriginImgui;
                worldTotal = _axisGizmo.ComputeAxisDelta(sdYDown, _pressAxis, ctx);
            }
            else
            {
                // 自由移動（ギズモ中央を掴んだ場合も含む）。
                // ComputeFreeDelta は +Y が画面上の差分を要求する。
                worldTotal = _axisGizmo.ComputeFreeDelta(screenPos - _pressOriginPos, ctx);
            }

            SetTotalDeltaAll(worldTotal);
            _pressMoveStarted = true;
        }

        /// <summary>累積移動量を全対象メッシュへ絶対値で設定し、GPU へ同期する。</summary>
        private void SetTotalDeltaAll(Vector3 worldTotal)
        {
            var model = _project?.CurrentModel;
            foreach (var kv in _meshTransforms)
            {
                var mc = model?.GetMeshContext(kv.Key);
                // IVertexTransform はローカル座標へ加算するため、メッシュごとにローカル化する。
                Vector3 localTotal = mc != null
                    ? mc.WorldMatrixInverse.MultiplyVector(worldTotal)
                    : worldTotal;
                kv.Value.SetTotalDelta(localTotal);
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            }
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // 押下時に対象・原点・transform を確定済みなら、ここでやり直さない。
            // やり直すと BeginMove が「移動後の位置」を元位置として取り込んでしまう。
            if (_pressActive)
            {
                PLDiag.PickRec("MTH.DragBeginPress",
                    (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                    _elemOnMouseDown.VertexIndex, _meshTransforms.Count,
                    _affectedVertices.Count, CountAffectedVertices(),
                    _pressOriginPos.x, _pressOriginPos.y);
                return;
            }

            if (IsRadiusDragMode)
            {
                _radiusDragStartPos = screenPos;
                _inRadiusDrag       = true;
                return;
            }
            _mouseDownPos = screenPos;
            _shiftHeld    = mods.Shift;
            _ctrlHeld     = mods.Ctrl;
            _dragMode     = DragMode.None;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            _elemOnMouseDown = GetHoverElement != null
                ? GetHoverElement(mode)
                : (hit.HasHit
                    ? new PlayerHoverElement { Kind = PlayerHoverKind.Vertex,
                          MeshIndex = hit.MeshIndex, VertexIndex = hit.VertexIndex }
                    : PlayerHoverElement.None);

            // 診断: 掴んだ要素と押下位置を記録する。_mouseDownPos は押下位置、
            // _elemOnMouseDown は「しきい値を越えた現在位置」のホバー由来。
            PLDiag.PickRec("MTH.DragBegin",
                (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                _elemOnMouseDown.VertexIndex, _elemOnMouseDown.EdgeV1,
                _elemOnMouseDown.EdgeV2, _elemOnMouseDown.FaceIndex,
                _mouseDownPos.x, _mouseDownPos.y);

            // 診断: ドラッグ開始と同じフレームでホバー要素が入れ替わっていたら、
            // 「押下時に見えていた色」と「掴んだ要素」が食い違いうる状態だった。
            if (PLDiag.LastHoverFrame == UnityEngine.Time.frameCount && PLDiag.LastHoverChanged)
                PLDiag.PickDump("hover-changed-at-dragbegin");

            // 診断: 掴んだメッシュが操作対象 (SelectedDrawableMeshIndices) に無い場合、
            // 選択は書けるが移動対象にならない。UpdateAffectedVertices と経路が食い違う。
            if (_elemOnMouseDown.HasHit && !IsMeshOperable(_elemOnMouseDown.MeshIndex))
                PLDiag.PickDump("hover-mesh-not-operable");

            // 軸ギズモヒットテスト（最優先）。SelectOnly 時は移動を一切行わないためスキップし、
            // 常に box/lasso 選択へ進む。SuppressBuiltinGizmo 時も組み込み移動ギズモは使わない。
            var ctx = GetToolContext?.Invoke();
            if (!SelectOnly && !SuppressBuiltinGizmo && ctx != null)
            {
                UpdateAffectedVertices();
                if (HasAnyAffected())
                {
                    UpdateGizmoState(ctx);
                    var axisHit = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
                    if (axisHit != AxisGizmo.AxisType.None)
                    {
                        _draggingAxis    = axisHit;
                        _lastAxisDragPos = ToImgui(screenPos);
                        BeginMove();
                        _state    = axisHit == AxisGizmo.AxisType.Center
                                    ? MoveState.CenterDragging : MoveState.AxisDragging;
                        _dragMode = DragMode.Moving;
                        OnEnterTransformDragging?.Invoke();
                        return;
                    }
                }
            }

            // 将来ギズモ（回転/拡大等）のヒットテスト差し替え。当たったらツール操作フックへ委譲する。
            // （PendingAction 経由で OnDragStartExtra が呼ばれ、ツール側が処理する）。
            if (!SelectOnly && GizmoHitTestOverride != null && ctx != null
                && GizmoHitTestOverride(screenPos, ctx))
            {
                // 掴んだのはギズモであって要素ではない。ここで _elemOnMouseDown を
                // 残すと、しきい値超過時の選択差し替え（PendingAction の
                // ApplyElementClick）が走り、カーソル下の未選択頂点1個へ選択が
                // 潰れる。複数頂点を選んでギズモを回しても1頂点しか動かなくなる。
                _elemOnMouseDown = PlayerHoverElement.None;

                UpdateAffectedVertices();
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 要素ヒット → PendingAction（SelectOnly 時は移動/ツール操作を行わず box/lasso 選択へ）
            UpdateAffectedVertices();
            if (!SelectOnly && _elemOnMouseDown.HasHit)
            {
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 矩形/投げ縄選択開始
            // SuppressDragSelect 時は開始しない。_dragMode/_state を Idle のまま残すので
            // 以降の OnLeftDrag は switch のどの case にも入らず、OnLeftDragEnd も
            // 末尾のリセットへ落ちるだけになる（＝ドラッグ全体が無反応）。
            if (SuppressDragSelect)
            {
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                return;
            }

            if (DragSelectMode == SelectionDragMode.Lasso)
            {
                _dragMode = DragMode.LassoSelecting;
                _state    = MoveState.Idle;
                _selectionOps.BeginLassoSelect(screenPos);
                OnEnterBoxSelecting?.Invoke();
            }
            else
            {
                _dragMode = DragMode.BoxSelecting;
                _state    = MoveState.Idle;
                _selectionOps.BeginBoxSelect(screenPos);
                OnEnterBoxSelecting?.Invoke();
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                var rdCtx = GetToolContext?.Invoke();
                if (rdCtx != null)
                {
                    float screenDist = Vector2.Distance(screenPos, _radiusDragStartPos);
                    float newRadius  = MoveScreenDistToWorldRadius(screenDist, rdCtx);
                    newRadius = Mathf.Clamp(newRadius, MinMagnetRadius, MaxMagnetRadius);
                    MagnetRadius = newRadius;
                    OnRadiusChanged?.Invoke(newRadius);
                }
                return;
            }
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnBoxSelectUpdate?.Invoke(_selectionOps.BoxStart, screenPos);
                OnRepaint?.Invoke();
                return;
            }

            if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnLassoSelectUpdate?.Invoke(_selectionOps.LassoPoints);
                OnRepaint?.Invoke();
                return;
            }

            var ctx = GetToolContext?.Invoke();

            switch (_state)
            {
                case MoveState.PendingAction:
                    if (Vector2.Distance(screenPos, _mouseDownPos) > DragThreshold)
                    {
                        // ヒット要素が未選択なら選択
                        if (_elemOnMouseDown.HasHit && !IsElemSelected(_elemOnMouseDown))
                        {
                            var selBefore = CaptureAllSelectionSnapshots();
                            _selectionOps.ApplyElementClick(_elemOnMouseDown, new ModifierKeys());
                            RecordSelectionChange(selBefore, CaptureAllSelectionSnapshots());

                            // 下の OnEnterTransformDragging で TransformDragging へ入ると
                            // 選択が GPU へ届かなくなるため、ここで同期的に反映させる。
                            // 詳細は OnCommitSelectionSync の宣言部を参照。
                            OnCommitSelectionSync?.Invoke();
                        }

                        UpdateAffectedVertices();

                        // 診断: 選択適用後の対象集計結果を記録する。
                        PLDiag.PickRec("MTH.Pending",
                            (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                            IsElemSelected(_elemOnMouseDown) ? 1 : 0,
                            _project?.CurrentModel?.SelectedDrawableMeshIndices?.Count ?? -1,
                            _affectedVertices.Count, CountAffectedVertices(),
                            screenPos.x, screenPos.y);

                        // 診断: 要素を掴んでいるのに移動対象が 0 件。
                        // 「選択と同時の移動が反映されない」の直接条件。
                        if (_elemOnMouseDown.HasHit && !HasAnyAffected())
                            PLDiag.PickDump("no-affected-after-select");

                        // Ctrl + 未選択 → 移動キャンセル
                        if (_ctrlHeld && !HasAnyAffected()) { _state = MoveState.Idle; return; }

                        // ツール流用フック: ドラッグ系ツール (EdgeBevel / FaceExtrude 等) が
                        // ここで発火し、true を返したら通常の移動処理を抑制する。
                        // ツール側で独自のドラッグセッションを開始するため、
                        // 以降の OnLeftDrag / OnLeftDragEnd はツールハンドラ側で処理される想定。
                        bool suppressMove = false;
                        if (OnDragStartExtra != null)
                        {
                            var modsForHook = new ModifierKeys
                            {
                                Shift = _shiftHeld,
                                Ctrl  = _ctrlHeld,
                            };
                            suppressMove = OnDragStartExtra(_elemOnMouseDown, modsForHook);
                        }
                        if (suppressMove)
                        {
                            // ツール固有ドラッグセッション開始。以降の OnLeftDrag /
                            // OnLeftDragEnd は OnToolDragExtra / OnToolDragEndExtra に委譲。
                            _state = MoveState.ToolDragging;
                            return;
                        }

                        BeginMove();
                        _state = MoveState.MovingVertices;

                        // TransformDragging へ入る。ここで呼ばないと、この経路だけ
                        // アダプタが Normal モードのままになり、ドラッグ中も発火し続ける
                        // OnPointerHover（PlayerViewportPanel.OnPointerMove）から
                        // NotifyPointerHover → ProcessMouseUpdate が走って
                        // ホバー索引が別の要素へ書き換わる。
                        // UpdateModeProfile.TransformDragging は AllowHitTest=false なので、
                        // 入ってさえいれば再計算は止まる。
                        // 終了側 OnLeftDragEnd は MovingVertices でも
                        // OnExitTransformDragging を呼んでおり、これで対になる。
                        OnEnterTransformDragging?.Invoke();

                        // 診断: transform が 0 件のまま MovingVertices へ入ると、
                        // 以降の ApplyDelta が何も動かさずドラッグが空振りになる。
                        PLDiag.PickRec("MTH.BeginMove",
                            _meshTransforms.Count, _affectedVertices.Count, CountAffectedVertices());
                        if (_meshTransforms.Count == 0)
                            PLDiag.PickDump("begin-move-empty");

                        // 押下位置から現在位置までの移動量を取りこぼさずに適用する。
                        // ここで適用しないと、ドラッグ開始しきい値（パネル側
                        // PlayerViewportPanel.DragThreshold と本クラスの DragThreshold）
                        // ぶんの移動が捨てられ、カーソルと対象の間に恒久的なオフセットが残る。
                        // _mouseDownPos は OnLeftDragBegin で受け取った押下位置。
                        if (ctx != null) ApplyFreeDelta(screenPos - _mouseDownPos, ctx);

                        OnRepaint?.Invoke();
                    }
                    break;

                case MoveState.MovingVertices:
                case MoveState.CenterDragging:
                    if (ctx == null) break;
                    // 押下時追従が有効なら押下位置からの総差分を絶対量で適用する。
                    // 無効な経路（旧ディスパッチャ等）は従来の差分累積のまま。
                    if (_pressActive) ApplyAbsoluteFromOrigin(screenPos, ctx);
                    else              ApplyFreeDelta(delta, ctx);
                    break;

                case MoveState.AxisDragging:
                    if (ctx == null) break;
                    if (_pressActive)
                    {
                        // 押下位置からの総差分を軸方向へ 1 回で射影して絶対量で適用する。
                        ApplyAbsoluteFromOrigin(screenPos, ctx);
                    }
                    else
                    {
                        Vector2 imguiPos = ToImgui(screenPos);
                        Vector2 sd = imguiPos - _lastAxisDragPos;
                        _lastAxisDragPos = imguiPos;
                        if (sd.sqrMagnitude > 0.001f)
                        {
                            UpdateGizmoState(ctx);
                            Vector3 wd = _axisGizmo.ComputeAxisDelta(sd, _draggingAxis, ctx);
                            ApplyDelta(wd);
                        }
                    }
                    break;

                case MoveState.ToolDragging:
                    // ツール流用フック: EdgeBevel / FaceExtrude 等がドラッグ中の
                    // 幅調整・押し出し量更新をここで受け取る
                    OnToolDragExtra?.Invoke(screenPos, delta, mods);
                    break;
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            // 押下時に適用した選択変更は、ドラッグが確定したこの時点で Undo へ積む。
            // （しきい値未満で離した場合は OnLeftPressCancel で巻き戻すため積まない）
            if (_pressSelectionApplied && _pressSelectionBefore != null)
            {
                RecordSelectionChange(_pressSelectionBefore, CaptureAllSelectionSnapshots());
                _pressSelectionApplied = false;
                _pressSelectionBefore  = null;
            }

            if (_inRadiusDrag)
            {
                _inRadiusDrag    = false;
                IsRadiusDragMode = false;
                return;
            }
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnReadBackVertexFlags?.Invoke();
                CommitBoxSelect(mods);
                OnBoxSelectEnd?.Invoke();
                OnExitBoxSelecting?.Invoke();
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                ResetPressState();   // 押下時に開始した場合の後片付け
                OnClearMouseHover?.Invoke();
                FireOneShotFinished();
                return;
            }

            if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnReadBackVertexFlags?.Invoke();
                CommitLassoSelect(mods);
                OnLassoSelectEnd?.Invoke();
                OnExitBoxSelecting?.Invoke();
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                ResetPressState();   // 押下時に開始した場合の後片付け
                OnClearMouseHover?.Invoke();
                FireOneShotFinished();
                return;
            }

            bool moved = _state == MoveState.MovingVertices
                      || _state == MoveState.AxisDragging
                      || _state == MoveState.CenterDragging;
            if (moved)
            {
                EndMove();
                OnExitTransformDragging?.Invoke();
            }

            // ツール流用フック: ツール固有ドラッグの終了 (Bevel 確定、Extrude 確定等)
            if (_state == MoveState.ToolDragging)
            {
                OnToolDragEndExtra?.Invoke(screenPos, mods);
            }

            _state        = MoveState.Idle;
            _dragMode     = DragMode.None;
            _draggingAxis = AxisGizmo.AxisType.None;
            ResetPressState();
            OnClearMouseHover?.Invoke();
        }

        // ================================================================
        // ギズモスクリーン座標取得（UIToolkit generateVisualContent から呼ぶ）
        // ================================================================

        /// <summary>
        /// AxisGizmo のスクリーン座標を返す。
        /// 選択なし・ctx null の場合は false を返す。
        /// UIToolkit の generateVisualContent で軸を描画するために使う。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (SelectOnly) return false;   // 選択専用モードではギズモを描画しない
            if (SuppressBuiltinGizmo) return false;   // 自前ギズモを使うモードでは組み込みギズモを描画しない
            if (ctx == null) return false;
            UpdateAffectedVertices();
            if (!HasAnyAffected()) return false;

            // 押下中は換算基準と同じ固定重心を使う。UpdateGizmoState を呼ぶと
            // 移動後の頂点位置から重心を計算し直すため、ギズモ原点が
            // 掴んだ対象と一緒に動いてしまう。
            if (_gizmoCenterFrozen) _axisGizmo.Center = _frozenGizmoCenter;
            else                    UpdateGizmoState(ctx);

            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _hoveredAxis;
            return true;
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// 頂点移動は矢印スタイル（キューブ / ダイヤ / リングのいずれも立てない）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (!TryGetGizmoScreenPositions(ctx, out var o, out var xe, out var ye, out var ze, out var ha))
                return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo    = true,
                Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis = ha,
            };
            return true;
        }

        /// <summary>ポインター移動時に呼んでホバー軸を更新する。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            _lastMousePos = ToImgui(screenPos);
            if (SelectOnly) return;   // 選択専用モードではギズモ軸ホバーを更新しない
            if (SuppressBuiltinGizmo) return;   // 自前ギズモを使うモードでは組み込みギズモをホバーしない
            if (ctx == null || _state != MoveState.Idle) return;
            UpdateAffectedVertices();
            if (!HasAnyAffected()) return;
            UpdateGizmoState(ctx);
            var newHovered = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
            if (newHovered != _hoveredAxis)
            {
                _hoveredAxis = newHovered;
                OnRepaint?.Invoke();
            }
        }

        // ================================================================
        // 内部
        // ================================================================
        // ================================================================
        // 選択変更 Undo ヘルパー
        // ================================================================

        /// <summary>
        /// 選択メッシュ全ての選択状態を、MeshContextList インデックス付きで控える。
        ///
        /// SelectionSnapshot はメッシュ内ローカル番号を持つため 1 メッシュしか表せない。
        /// 本ハンドラのクリック／矩形／投げ縄は複数メッシュにまたがるので、
        /// メッシュごとに 1 個ずつ控えて MultiMeshSelectionChangeRecord に渡す。
        /// </summary>
        private Dictionary<int, SelectionSnapshot> CaptureAllSelectionSnapshots()
        {
            var result = new Dictionary<int, SelectionSnapshot>();
            var model  = _project?.CurrentModel;
            if (model == null) return result;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc  = model.GetMeshContext(ctxIdx);
                var sel = mc?.Selection;
                if (sel == null) continue;

                result[ctxIdx] = new SelectionSnapshot
                {
                    Mode     = sel.Mode,
                    Vertices = new HashSet<int>(sel.Vertices),
                    Edges    = new HashSet<VertexPair>(sel.Edges),
                    Faces    = new HashSet<int>(sel.Faces),
                    Lines    = new HashSet<int>(sel.Lines),
                };
            }
            return result;
        }

        /// <summary>指定モードの空スナップショット（片側にしか存在しないメッシュの補完用）。</summary>
        private static SelectionSnapshot EmptySelectionSnapshot(MeshSelectMode mode)
        {
            return new SelectionSnapshot
            {
                Mode     = mode,
                Vertices = new HashSet<int>(),
                Edges    = new HashSet<VertexPair>(),
                Faces    = new HashSet<int>(),
                Lines    = new HashSet<int>(),
            };
        }

        /// <summary>
        /// 選択変更を Undo スタックへ記録する。
        ///
        /// 変化のあったメッシュだけをエントリ化し、1 件も無ければ記録しない。
        /// Record は MultiMeshSelectionChangeRecord を使う。SelectionChangeRecord は
        /// 復元先が ActiveMeshContext 固定のため、複数メッシュには使えない。
        /// </summary>
        private void RecordSelectionChange(
            Dictionary<int, SelectionSnapshot> before,
            Dictionary<int, SelectionSnapshot> after)
        {
            if (_undoController == null || before == null || after == null) return;

            var model = _project?.CurrentModel;
            if (model == null) return;

            // before / after の和集合で比較する。
            // 途中でメッシュ選択自体が変わった場合に片側にしか無いキーが出るため。
            var keys = new HashSet<int>(before.Keys);
            keys.UnionWith(after.Keys);

            var entries = new List<MeshSelectionEntry>();
            foreach (int ctxIdx in keys)
            {
                before.TryGetValue(ctxIdx, out var b);
                after.TryGetValue(ctxIdx, out var a);

                if (b == null) b = EmptySelectionSnapshot(a?.Mode ?? MeshSelectMode.Vertex);
                if (a == null) a = EmptySelectionSnapshot(b.Mode);

                if (!b.IsDifferentFrom(a)) continue;

                entries.Add(new MeshSelectionEntry
                {
                    MeshContextIndex = ctxIdx,
                    Old              = b,
                    New              = a,
                });
            }

            if (entries.Count == 0) return;

            _undoController.MeshUndoContext.ParentModelContext = model;
            var record = new MultiMeshSelectionChangeRecord(entries.ToArray());
            _undoController.FocusVertexEdit();
            Poly_Ling.Diagnostics.PLDiag.UndoVerboseLog(
                $"Push MultiMeshSelectionChangeRecord (model={model.Name}, " +
                $"entries={entries.Count})");
            _undoController.VertexEditStack.Record(record, "選択変更");
            Poly_Ling.Diagnostics.PLDiag.UndoVerboseLog(
                $"  after Record: VertexEdit.Undo={_undoController.VertexEditStack.UndoCount}, " +
                $"VertexEdit.Pending={_undoController.VertexEditStack.PendingCount}, " +
                $"MeshList.Undo={_undoController.MeshListStack.UndoCount}, " +
                $"MeshList.Pending={_undoController.MeshListStack.PendingCount}");
        }

        /// <summary>
        /// 移動対象頂点を集計する。
        ///
        /// 選択メッシュ（ModelContext.SelectedDrawableMeshIndices）を全て走査し、
        /// 各 MeshContext.Selection から自メッシュぶんの頂点を集める。
        /// SelectionState の Vertices/Faces/Lines はメッシュ内ローカル番号のため、
        /// 単一 SelectionState を全メッシュに適用してはならない。
        ///
        /// 集計後の _affectedVertices は BeginMove / UpdateGizmoState / ApplyDelta /
        /// EndMove がそのまま複数メッシュとして扱う（いずれも元から辞書全走査）。
        /// </summary>
        private void UpdateAffectedVertices()
        {
            _affectedVertices.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null) continue;

                var sel = mc.Selection;
                if (sel == null) continue;

                var mo       = mc.MeshObject;
                var affected = new HashSet<int>();

                // 選択モードで無効な種別は移動対象に入れない。
                // これを見ないと「頂点だけチェックしているのに辺が動く」状態になる
                // （モードを絞るツールから戻った直後や、クリアが間に合っていない場合）。
                var selMode = sel.Mode;

                if (selMode.Has(MeshSelectMode.Vertex))
                    foreach (var v  in sel.Vertices) affected.Add(v);
                if (selMode.Has(MeshSelectMode.Edge))
                    foreach (var e  in sel.Edges)    { affected.Add(e.V1); affected.Add(e.V2); }
                if (selMode.Has(MeshSelectMode.Face))
                    foreach (var fi in sel.Faces)
                        if (fi >= 0 && fi < mo.FaceCount)
                            foreach (var vi in mo.Faces[fi].VertexIndices)
                                affected.Add(vi);
                if (selMode.Has(MeshSelectMode.Line))
                    foreach (var li in sel.Lines)
                        if (li >= 0 && li < mo.FaceCount)
                        {
                            var face = mo.Faces[li];
                            if (face.VertexCount == 2)
                            { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                        }

                if (affected.Count > 0) _affectedVertices[ctxIdx] = affected;
            }
        }

        private bool HasAnyAffected()
        {
            foreach (var kv in _affectedVertices)
                if (kv.Value.Count > 0) return true;
            return false;
        }

        /// <summary>診断用: _affectedVertices の頂点総数。</summary>
        private int CountAffectedVertices()
        {
            int n = 0;
            foreach (var kv in _affectedVertices) n += kv.Value.Count;
            return n;
        }

        /// <summary>
        /// 診断用: 操作対象メッシュの選択要素総数（頂点 + 辺 + 面 + 線分）。
        /// _affectedVertices を変更しないため、クリック経路でも安全に呼べる。
        /// </summary>
        private int CountSelectedElements()
        {
            var model = _project?.CurrentModel;
            if (model == null) return -1;
            int n = 0;
            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var sel = model.GetMeshContext(ctxIdx)?.Selection;
                if (sel == null) continue;
                n += sel.Vertices.Count + sel.Edges.Count + sel.Faces.Count + sel.Lines.Count;
            }
            return n;
        }

        /// <summary>
        /// 診断用: 指定 MeshContextList インデックスが操作対象
        /// (ModelContext.SelectedDrawableMeshIndices) に含まれるか。
        ///
        /// ホバーの可否は GPU 側 (FlagManager.IsMeshSelected) が決めるため、
        /// ここと食い違うと「ホバーはできるが移動対象にならない」状態になる。
        /// </summary>
        private bool IsMeshOperable(int meshContextIndex)
        {
            if (meshContextIndex < 0) return false;
            var list = _project?.CurrentModel?.SelectedDrawableMeshIndices;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == meshContextIndex) return true;
            return false;
        }

        private void UpdateGizmoState(ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var kv in _affectedVertices)
            {
                var mc = model?.GetMeshContext(kv.Key);
                if (mc?.MeshObject == null) continue;
                // ローカル頂点をワールド変換してから集計する。
                // WorldToScreenPos はワールド空間を期待するため、この変換が抜けると
                // Player（WorldMatrix 非 identity）でギズモが実頂点から離れて描画される。
                // 変換は頂点単位で行う。スキンド頂点に実際に適用される行列は
                // メッシュの WorldMatrix ではなくボーンの SkinningMatrix のブレンドであり
                // （MeshContext.VertexMatrix）、メッシュの行列を使うとギズモが
                // 親ボーンのワールド移動量ぶんずれる。
                foreach (int vi in kv.Value)
                    if (vi >= 0 && vi < mc.MeshObject.VertexCount)
                    { sum += mc.LocalToWorld(vi, mc.MeshObject.Vertices[vi].Position); count++; }
            }
            _axisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        private void BeginMove()
        {
            _meshTransforms.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            foreach (var kv in _affectedVertices)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc?.MeshObject == null) continue;

                var startPos = (Vector3[])mc.MeshObject.Positions.Clone();
                IVertexTransform t = UseMagnet
                    ? (IVertexTransform)new MagnetMoveTransform(MagnetRadius, MagnetFalloff, MagnetDistanceMode)
                    : new SimpleMoveTransform();
                t.Begin(mc.MeshObject, kv.Value, startPos);
                _meshTransforms[kv.Key] = t;
            }
        }

        private void ApplyFreeDelta(Vector2 screenDelta, ToolContext ctx)
        {
            UpdateGizmoState(ctx);
            Vector3 wd = _axisGizmo.ComputeFreeDelta(screenDelta, ctx);
            ApplyDelta(wd);
        }

        private void ApplyDelta(Vector3 worldDelta)
        {
            if (worldDelta == Vector3.zero) return;

            // 診断: 移動量はあるのに transform が 0 件 → 画面に何も反映されない。
            PLDiag.PickRec("MTH.ApplyDelta", _meshTransforms.Count,
                x: worldDelta.x, y: worldDelta.y, z: worldDelta.z);
            if (_meshTransforms.Count == 0)
                PLDiag.PickDump("apply-delta-empty");

            var model = _project?.CurrentModel;
            foreach (var kv in _meshTransforms)
            {
                var mc = model?.GetMeshContext(kv.Key);
                // IVertexTransform.Apply は Vertices[].Position（ローカル座標）に直接加算する。
                // ワールドデルタをそのまま渡すと、WorldMatrix に回転／スケールがある場合に
                // ギズモの指す向きと実際の移動方向がずれる。メッシュごとにローカル化する。
                Vector3 localDelta = mc != null
                    ? mc.WorldMatrixInverse.MultiplyVector(worldDelta)
                    : worldDelta;
                kv.Value.Apply(localDelta);
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            }
            OnRepaint?.Invoke();
        }

        private void EndMove()
        {
            if (_undoController != null)
            {
                var model = _project?.CurrentModel;
                if (model != null)
                {
                    var entries = new List<MeshMoveEntry>();
                    foreach (var kv in _meshTransforms)
                    {
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc?.MeshObject == null) continue;
                        var indices = kv.Value.GetAffectedIndices();
                        var oldPos  = kv.Value.GetOriginalPositions();
                        var newPos  = kv.Value.GetCurrentPositions();
                        if (indices.Length == 0) continue;
                        entries.Add(new MeshMoveEntry
                        {
                            MeshContextIndex = kv.Key,
                            Indices          = indices,
                            OldPositions     = oldPos,
                            NewPositions     = newPos,
                        });
                    }
                    if (entries.Count > 0)
                    {
                        _undoController.MeshUndoContext.ParentModelContext = model;
                        var record = new MultiMeshVertexMoveRecord(entries.ToArray());
                        _undoController.FocusVertexEdit();
                        int totalVerts = 0;
                        foreach (var e in entries) totalVerts += e.Indices?.Length ?? 0;
                        UnityEngine.Debug.Log(
                            $"[UndoDbg] Push MultiMeshVertexMoveRecord (model={model.Name}, " +
                            $"entries={entries.Count}, totalVerts={totalVerts})");
                        _undoController.VertexEditStack.Record(record, "Move Vertices");
                        UnityEngine.Debug.Log(
                            $"[UndoDbg]   after Record: VertexEdit.Undo={_undoController.VertexEditStack.UndoCount}, " +
                            $"VertexEdit.Pending={_undoController.VertexEditStack.PendingCount}, " +
                            $"MeshList.Undo={_undoController.MeshListStack.UndoCount}, " +
                            $"MeshList.Pending={_undoController.MeshListStack.PendingCount}");

                        // リモート連動: 影響した各メッシュを通知する。
                        if (OnVerticesCommitted != null)
                        {
                            UnityEngine.Debug.Log($"[EditSync] OnVerticesCommitted fire: entries={entries.Count}");
                            foreach (var e in entries)
                            {
                                var emc = model.GetMeshContext(e.MeshContextIndex);
                                if (emc?.MeshObject != null) OnVerticesCommitted.Invoke(emc);
                            }
                        }
                    }
                }
            }
            foreach (var kv in _meshTransforms) kv.Value.End();
            _meshTransforms.Clear();
            _affectedVertices.Clear();
        }

        /// <summary>
        /// ワールド空間の移動量を数値指定で適用し、Undo 1 件として確定する。
        /// サブパネルの数値入力から呼ぶ。ドラッグ状態機 (_state) には入らない。
        ///
        /// 対象は選択メッシュの選択要素 (UpdateAffectedVertices と同じ規則)。
        /// ワールドデルタは ApplyDelta 内でメッシュごとに WorldMatrixInverse で
        /// ローカル化されるため、ここではワールド量をそのまま渡す。
        /// </summary>
        public void ApplyNumericMove(Vector3 worldDelta)
        {
            if (worldDelta == Vector3.zero) return;

            OnEnterTransformDragging?.Invoke();

            UpdateAffectedVertices();
            if (!HasAnyAffected())
            {
                OnExitTransformDragging?.Invoke();
                return;
            }

            BeginMove();
            ApplyDelta(worldDelta);
            EndMove();

            OnExitTransformDragging?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 辺／面／線分の選択を、対応する頂点選択へ展開する。
        /// 選択メッシュごとに自分の MeshObject を参照する
        /// （面インデックスはメッシュ内ローカル番号のため）。
        /// </summary>
        private void ExpandLinkedVertices()
        {
            var model = _project?.CurrentModel;
            if (model == null) return;

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                var meshObject = mc?.MeshObject;
                var sel = mc?.Selection;
                if (meshObject == null || sel == null) continue;

                foreach (var edge in sel.Edges)
                {
                    sel.Vertices.Add(edge.V1);
                    sel.Vertices.Add(edge.V2);
                }
                foreach (var faceIdx in sel.Faces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                            sel.Vertices.Add(vIdx);
                }
                foreach (var lineIdx in sel.Lines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2)
                        {
                            sel.Vertices.Add(face.VertexIndices[0]);
                            sel.Vertices.Add(face.VertexIndices[1]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ヒット要素が既に選択済みかを判定する。
        /// 判定先は「当たったメッシュ」の Selection。先頭メッシュの Selection を
        /// 見ると、別メッシュの同番号要素が選択済みかどうかで判定してしまう。
        /// </summary>
        private bool IsElemSelected(PlayerHoverElement elem)
        {
            var sel = ResolveMeshSelection(elem.MeshIndex);
            if (sel == null) return false;
            return elem.Kind switch
            {
                PlayerHoverKind.Vertex => sel.Vertices.Contains(elem.VertexIndex),
                PlayerHoverKind.Edge   => sel.Edges.Contains(
                    new VertexPair(elem.EdgeV1, elem.EdgeV2)),
                PlayerHoverKind.Face   => sel.Faces.Contains(elem.FaceIndex),
                PlayerHoverKind.Line   => sel.Lines.Contains(elem.FaceIndex),
                _                      => false,
            };
        }

        /// <summary>
        /// MeshContextList インデックスから SelectionState を解決する。
        /// 解決できない場合は先頭選択メッシュのものを返す。
        /// </summary>
        private Poly_Ling.Selection.SelectionState ResolveMeshSelection(int meshContextIndex)
        {
            var model = _project?.CurrentModel;
            if (model != null && meshContextIndex >= 0)
            {
                var mc = model.GetMeshContext(meshContextIndex);
                if (mc?.Selection != null) return mc.Selection;
            }
            return _selectionOps.SelectionState;
        }

        private float MoveScreenDistToWorldRadius(float screenDist, ToolContext ctx)
        {
            Vector3 target   = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector2 sp1 = ctx.WorldToScreenPos(target,           ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(target + camRight, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float pxPerUnit = Vector2.Distance(sp1, sp2);
            if (pxPerUnit < 0.001f) return screenDist * 0.01f;
            return screenDist / pxPerUnit;
        }

        /// <summary>
        /// ToViewportCoord（Y=0が下）の座標を IMGUI 系（Y=0が上）に変換する。
        /// AxisGizmo は GL.LoadPixelMatrix（Y=0が上）を前提に描画・判定する。
        /// </summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        /// <summary>
        /// 矩形選択を確定する。
        ///
        /// 選択メッシュを全て走査する。頂点オフセット（GetVertexOffset）は
        /// メッシュごとに異なるため、必ずメッシュ単位で取り直すこと。
        /// GetScreenPositions() は全メッシュぶんを連結したグローバル配列なので
        /// そのまま使える。
        /// </summary>
        private void CommitBoxSelect(ModifierKeys mods)
        {
            if (GetScreenPositions == null)
            { _selectionOps.EndBoxSelect(Enumerable.Empty<MeshVertexRef>(), mods); return; }

            var model = _project?.CurrentModel;
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0)
            { _selectionOps.EndBoxSelect(Enumerable.Empty<MeshVertexRef>(), mods); return; }

            var rect      = _selectionOps.BoxRect;
            var screenPos = GetScreenPositions();
            float vpH     = GetViewportHeight?.Invoke() ?? 0f;

            var selBefore = CaptureAllSelectionSnapshots();

            bool additive = mods.Shift || mods.Ctrl;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line);

            var inBox = new List<MeshVertexRef>();

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null || mc.Selection == null) continue;

                var mo = mc.MeshObject;
                int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;

                // スクリーン座標取得ヘルパー（このメッシュのローカル頂点番号 → 画面座標）
                Func<int, Vector2> vertexScreen = (i) =>
                {
                    if (screenPos == null || vertexOffset + i >= screenPos.Length)
                        return new Vector2(-10000, -10000);
                    return new Vector2(screenPos[vertexOffset + i].x, vpH - screenPos[vertexOffset + i].y);
                };

                if (!additive)
                {
                    mc.Selection.ClearAll();
                }

                // 頂点選択
                if (mode.Has(MeshSelectMode.Vertex))
                {
                    for (int i = 0; i < mo.Vertices.Count; i++)
                    {
                        if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + i)) continue;
                        if (rect.Contains(vertexScreen(i), true)) inBox.Add(new MeshVertexRef(ctxIdx, i));
                    }
                }

                // 辺選択
                // GPU 計算済みの頂点可視フラグ (IsVertexVisible) で両端頂点を判定し、
                // 表面の面に属さない頂点から成る辺は除外する。
                // 厳密には「両端が可視でも辺自体は裏を通る」ケースも稀に拾うが、
                // GPU 側でも辺単位の可視判定は無く頂点ベースなので同じ挙動で OK。
                if (mode.Has(MeshSelectMode.Edge))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 2) continue;
                        for (int ei = 0; ei < face.VertexCount; ei++)
                        {
                            int v1 = face.VertexIndices[ei];
                            int v2 = face.VertexIndices[(ei + 1) % face.VertexCount];
                            if (IsVertexVisible != null
                                && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                                continue;
                            if (rect.Contains(vertexScreen(v1), true) &&
                                rect.Contains(vertexScreen(v2), true))
                            {
                                mc.Selection.SelectEdge(v1, v2, true);
                            }
                        }
                    }
                }

                // 面選択
                // 面の全頂点が IsVertexVisible で可視のとき表面扱い (裏側に属する面は除外)。
                if (mode.Has(MeshSelectMode.Face))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3) continue;
                        bool allIn = true;
                        foreach (int vi in face.VertexIndices)
                        {
                            if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + vi)) { allIn = false; break; }
                            if (!rect.Contains(vertexScreen(vi), true)) { allIn = false; break; }
                        }
                        if (allIn) mc.Selection.SelectFace(fi, true);
                    }
                }

                // 線分選択
                // 両端頂点が可視のときのみ選択対象 (辺と同じ扱い)。
                if (mode.Has(MeshSelectMode.Line))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount != 2) continue;
                        int v1 = face.VertexIndices[0];
                        int v2 = face.VertexIndices[1];
                        if (IsVertexVisible != null
                            && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                            continue;
                        if (rect.Contains(vertexScreen(v1), true) &&
                            rect.Contains(vertexScreen(v2), true))
                        {
                            mc.Selection.SelectLine(fi, true);
                        }
                    }
                }
            }

            _selectionOps.EndBoxSelect(inBox, mods);
            ExpandLinkedVertices();
            RecordSelectionChange(selBefore, CaptureAllSelectionSnapshots());
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 投げ縄選択を確定する。走査規則は CommitBoxSelect と同じ
        /// （選択メッシュ全走査・頂点オフセットはメッシュ単位）。
        /// </summary>
        private void CommitLassoSelect(ModifierKeys mods)
        {
            var lasso = _selectionOps.LassoPoints;
            if (lasso.Count < 3)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<MeshVertexRef>(), mods); return; }

            if (GetScreenPositions == null)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<MeshVertexRef>(), mods); return; }

            var model = _project?.CurrentModel;
            if (model == null || model.SelectedDrawableMeshIndices.Count == 0)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<MeshVertexRef>(), mods); return; }

            var screenPos = GetScreenPositions();
            float vpH     = GetViewportHeight?.Invoke() ?? 0f;

            // 座標系の確認：
            // GetScreenPositions() は NDC から screenY = (1 - ndcY) * height で計算 → UIToolkit Y（Y=0上）
            // vertexScreen は vpH - UIToolkitY → GPU Y（Y=0下）
            // LassoPoints は ToViewportCoord()（h - local.y）→ GPU Y（Y=0下）
            // → vertexScreen と LassoPoints は同じ GPU Y。変換不要。
            var lassoGPU = lasso;

            var selBefore = CaptureAllSelectionSnapshots();

            bool additive = mods.Shift || mods.Ctrl;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line);

            var inLasso = new List<MeshVertexRef>();

            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null || mc.Selection == null) continue;

                var mo = mc.MeshObject;
                int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;

                Func<int, Vector2> vertexScreen = (i) =>
                {
                    if (screenPos == null || vertexOffset + i >= screenPos.Length)
                        return new Vector2(-10000, -10000);
                    return new Vector2(screenPos[vertexOffset + i].x, vpH - screenPos[vertexOffset + i].y);
                };

                if (!additive)
                    mc.Selection.ClearAll();

                // 頂点選択
                if (mode.Has(MeshSelectMode.Vertex))
                {
                    for (int i = 0; i < mo.Vertices.Count; i++)
                    {
                        if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + i)) continue;
                        if (IsPointInLasso(vertexScreen(i), lassoGPU)) inLasso.Add(new MeshVertexRef(ctxIdx, i));
                    }
                }

                // 辺選択
                // GPU 計算済みの頂点可視フラグ (IsVertexVisible) で両端頂点を判定し、
                // 表面の面に属さない頂点から成る辺は除外する。矩形選択と同じ扱い。
                if (mode.Has(MeshSelectMode.Edge))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 2) continue;
                        for (int ei = 0; ei < face.VertexCount; ei++)
                        {
                            int v1 = face.VertexIndices[ei];
                            int v2 = face.VertexIndices[(ei + 1) % face.VertexCount];
                            if (IsVertexVisible != null
                                && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                                continue;
                            if (IsPointInLasso(vertexScreen(v1), lassoGPU) &&
                                IsPointInLasso(vertexScreen(v2), lassoGPU))
                            {
                                mc.Selection.SelectEdge(v1, v2, true);
                            }
                        }
                    }
                }

                // 面選択
                // 面の全頂点が IsVertexVisible で可視のとき表面扱い (裏側に属する面は除外)。
                if (mode.Has(MeshSelectMode.Face))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3) continue;
                        bool allIn = true;
                        foreach (int vi in face.VertexIndices)
                        {
                            if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + vi)) { allIn = false; break; }
                            if (!IsPointInLasso(vertexScreen(vi), lassoGPU)) { allIn = false; break; }
                        }
                        if (allIn) mc.Selection.SelectFace(fi, true);
                    }
                }

                // 線分選択
                // 両端頂点が可視のときのみ選択対象 (辺と同じ扱い)。
                if (mode.Has(MeshSelectMode.Line))
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount != 2) continue;
                        int v1 = face.VertexIndices[0];
                        int v2 = face.VertexIndices[1];
                        if (IsVertexVisible != null
                            && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                            continue;
                        if (IsPointInLasso(vertexScreen(v1), lassoGPU) &&
                            IsPointInLasso(vertexScreen(v2), lassoGPU))
                        {
                            mc.Selection.SelectLine(fi, true);
                        }
                    }
                }
            }

            _selectionOps.EndLassoSelect(inLasso, mods);
            ExpandLinkedVertices();
            RecordSelectionChange(selBefore, CaptureAllSelectionSnapshots());
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// Ray Casting アルゴリズムによる投げ縄内外判定。
        /// エディタ側 PolyLing_Input.IsPointInLasso と同一実装。
        /// </summary>
        private static bool IsPointInLasso(Vector2 point, System.Collections.Generic.List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            bool inside = false;
            int count = polygon.Count;
            int j = count - 1;
            for (int i = 0; i < count; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
