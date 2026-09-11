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
//
// 【分割先】このファイルから次へ分けてある。
//   MoveToolHandler.Commands.cs  移動ツールハンドラ：コマンドからの実行・数値移動・連結頂点の展開・範囲選択の確定。
//   MoveToolHandler.Move.cs      移動ツールハンドラ：ギズモ・選択スナップショットと Undo・影響頂点・移動の開始／適用／確定。
//   MoveToolHandler.Press.cs     移動ツールハンドラ：押下・ドラッグ・クリックの入力処理と、押下時点から始める矩形／投げ縄選択。

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
    public partial class MoveToolHandler : IPlayerToolHandler, IPlayerGizmoProvider, IPlayerPressHandler
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

        /// <summary>
        /// 辺／面／線分の選択を、構成頂点の頂点選択へ展開する種別を返す
        /// （Viewer の左ペイン「選んだ要素の頂点も選択する」チェックボックス）。
        ///
        /// 返す MeshSelectMode は「どの種別を頂点へ展開するか」の集合であり、
        /// 選択モード（何を選べるか）とは別物。Edge / Face / Line の 3 ビットだけを見る。
        /// 未結線のときは 3 種すべて展開する（従来の挙動）。
        /// </summary>
        public Func<MeshSelectMode> GetExpandToVertexKinds;

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

        /// <summary>
        /// ドラッグ 1 ストロークのワールド総移動量。
        ///
        /// IVertexTransform の累積はメッシュごとのローカル量なので、コマンドへ載せる
        /// ワールドの総量はここで持つ。BeginMove で 0 にし、ApplyDelta で足し、
        /// ApplyAbsoluteFromOrigin（絶対指定）では代入する。
        /// </summary>
        private Vector3 _dragWorldTotal;

        /// <summary>
        /// コマンド送信口。マウス経路の確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        // コマンド経由の対象指定。null のときはモデルの現在の選択を使う（マウス経路）。
        // UpdateAffectedVertices だけがこれを見る。以降の BeginMove / ApplyDelta /
        // EndMove は _affectedVertices と _meshTransforms を辿るので、絞り込みは
        // 1 か所で足りる。
        private HashSet<int> _targetOverride;

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
            // スナップショットは ApplyClick の直呼び経路でしか使わない。
            // コマンド経路の Undo は ExecuteFromCommand が自前で取る。
            var before = GetHoverElement == null
                ? CaptureAllSelectionSnapshots()
                : null;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            var clickedElem = GetHoverElement != null
                ? GetHoverElement(mode)
                : PlayerHoverElement.None;

            // 診断: クリック前の選択要素総数（ドラッグ経路と揃えて比較するため）
            int selCountBefore = CountSelectedElements();

            // 【コマンド発行に寄せている経路】
            //   GetHoverElement が結線されているときは、修飾キーの解釈だけを
            //   ResolveElementClick に取らせ、結果を SelectElementsCommand として
            //   送る。書き込み・頂点展開・選択 Undo はディスパッチャ経由で
            //   ExecuteFromCommand へ戻り、そこで 1 本の実装を通る。
            //
            //   未結線時の ApplyClick は直呼びのまま残す。PlayerHitResult.MeshIndex は
            //   GlobalToLocalVertexIndex 由来の unified インデックスで
            //   （ApplyClick の remarks を参照）、MeshContextList インデックスを
            //   期待するコマンドには載せられない。
            if (GetHoverElement != null)
            {
                if (SendCommand != null &&
                    _selectionOps.ResolveElementClick(clickedElem, mods, out var r))
                {
                    SendCommand(new Poly_Ling.Data.SelectElementsCommand(
                        _project?.CurrentModelIndex ?? 0,
                        r.ClearTargets,
                        r.VertexIndices, r.VertexMeshIndices,
                        r.EdgePairs,     r.EdgeMeshIndices,
                        r.FaceIndices,   r.FaceMeshIndices,
                        r.LineIndices,   r.LineMeshIndices,
                        r.Op));
                }
            }
            else
            {
                _selectionOps.ApplyClick(hit, mods);
                ExpandLinkedVertices();
            }

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

            // 選択 Undo は、コマンド経路では ExecuteFromCommand が積む。
            // ここで積むのは ApplyClick の直呼び経路だけ。
            if (GetHoverElement == null)
                RecordSelectionChange(before, CaptureAllSelectionSnapshots());

            // ツール流用フック: クリック系ツール (FlipFace 等) がここで発火
            OnLeftClickExtra?.Invoke(clickedElem, mods);

            // 一時サブツール: ドラッグに至らないクリックでも 1 回で終了させる
            FireOneShotFinished();
        }
    }
}
