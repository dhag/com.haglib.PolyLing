// DeformToolHandler.cs
// デフォーマ（回転 / 曲げ / 将来の任意変形）を Player へ橋渡しする
// IPlayerToolHandler 実装。
//
// 変形の中身は DeformApplier と IMeshDeformer が持つ。本クラスは
//   ・どのデフォーマを選んでいるか
//   ・プレビュー中か
//   ・確定時の Undo 記録
// だけを面倒みる。
//
// 【ドラッグ操作】IPlayerToolHandler のマウスイベントは空実装のまま。
//   ビューポートの選択は MoveToolHandler を流用し、回転ハンドルだけを
//   GizmoHitTest / BeginGizmoDrag / GizmoDrag / EndGizmoDrag のフック経由で
//   受け取る（PrimitivePlaceToolHandler と同じ構成）。
//   こうしないと頂点・辺の選択がビューポートでできなくなる。
//
// 【ハンドル】種類ごとに 3D 画面でパラメータを直接操作できる。
//   回す系（円弧＋両端の矢印）
//     曲げ    : 作業軸原点に1本。半径は軸長級
//     ねじり  : プレビュー柱の先端に1本
//     回転    : 作業軸原点に X/Y/Z の3本。軸色で色分け
//     どれも「角度が正のとき AngleAxis(+θ, ハンドル軸)」に揃えてあり、
//     ドラッグ方向・ハンドルの回り方・実際の変形が一致する。
//   直線系（軸線＋先端マーカー）
//     移動    : 先端は矢じり。X/Y/Z の3本
//     拡大縮小: 先端は立方体。X/Y/Z の3本
//   いずれも押下位置基準の絶対計算なので、往復させても誤差が溜まらない。
//
// 【形状プレビュー】デフォーマの種類ごとに、作業軸の矢印へ追加で
//   六角柱ワイヤを重ねる。
//     回転  … 追加なし（矢印だけ）
//     曲げ  … 現在のパラメータで曲げた六角柱。軸上に置く
//     ねじり… 現在のパラメータでねじった六角柱。軸から半径ぶん +X へずらす
//   ねじりをずらすのは、Y まわりの回転では軸上の柱が自分の円周上を動くだけで
//   外形が変わらず、ねじれが見えないため。
//   プレビュー用のデフォーマは実体とは別インスタンスにしてある。実体を
//   使うと Prepare が DeformApplier 用の内部状態を壊すため。
//
// 【プレビューは常に絶対計算】DeformApplier.Apply は Begin で記録した
//   開始位置を基準に毎回計算し直すため、スライダを往復させても誤差が
//   蓄積しない。パラメータが変わるたび Apply を呼んでよい。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>デフォーマ適用ハンドラ。</summary>
    public class DeformToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>    GetToolContext;
        public Func<float>          GetPanelHeight;
        public Action               OnRepaint;

        /// <summary>変形の基準となる作業軸。null なら何もしない。</summary>
        public Func<WorkAxisContext> GetWorkAxis;

        /// <summary>対象モデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>頂点位置を GPU へ同期する。メッシュごとに呼ばれる。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>確定後に呼ばれる。パネル更新に使う。</summary>
        public Action OnApplyCompleted;

        /// <summary>
        /// 回転ハンドルのドラッグでパラメータが変わったときに呼ばれる。
        /// サブパネルのスライダへ書き戻すために使う。
        /// </summary>
        public Action OnParamsChangedByGizmo;

        /// <summary>
        /// 作業軸フェーズで使うギズモ供給元（WorkAxisToolHandler）。Viewer が結線する。
        /// null なら作業軸フェーズでも変形フェーズと同じ表示になる。
        /// </summary>
        public IPlayerGizmoProvider WorkAxisGizmoProvider;

        /// <summary>フェーズが変わったときに呼ばれる。Viewer が入力経路を張り替える。</summary>
        public Action OnPhaseChanged;

        // ================================================================
        // フェーズ
        // ================================================================

        /// <summary>
        /// 変形モード内の作業段階。3D 画面でどちらのハンドルを掴めるかを決める。
        /// パラメータ欄はどちらのフェーズでも編集できる。
        /// </summary>
        public enum DeformPhase
        {
            /// <summary>作業軸を決める。変形ハンドルはグレーアウト表示のみ。</summary>
            WorkAxis,
            /// <summary>変形を掛ける。作業軸の Y 先端六角形は出さない。</summary>
            Deform,
        }

        private DeformPhase _phase = DeformPhase.WorkAxis;

        /// <summary>現在のフェーズ。変えると入力経路とギズモ表示が切り替わる。</summary>
        public DeformPhase Phase
        {
            get => _phase;
            set
            {
                if (_phase == value) return;
                _phase = value;

                // 掴みかけの状態を持ち越さない。
                _pendingHandle = HandleKind.None;
                _dragHandle    = HandleKind.None;
                _hoverHandle   = HandleKind.None;

                OnPhaseChanged?.Invoke();
                OnRepaint?.Invoke();
            }
        }

        // ================================================================
        // 設定
        // ================================================================

        private MeshUndoController _undoController;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // 状態
        // ================================================================

        private readonly DeformApplier _applier = new DeformApplier();

        private IMeshDeformer _deformer;

        /// <summary>現在選択中のデフォーマ。既定は登録順の先頭。</summary>
        public IMeshDeformer Deformer
        {
            get
            {
                if (_deformer == null)
                {
                    var all = DeformerRegistry.CreateAll();
                    if (all.Count > 0) _deformer = all[0];
                }
                return _deformer;
            }
        }

        /// <summary>デフォーマ名。未選択時は空文字。</summary>
        public string DeformerName => Deformer?.Name ?? string.Empty;

        /// <summary>
        /// 曲げ・ねじりの形状プレビュー六角柱を描くか。既定は表示。
        /// 回転には形状プレビューが無いのでこの値は影響しない。
        /// </summary>
        public bool ShowShapePreview { get; set; } = true;

        // マグネット（比例編集）
        public bool         UseMagnet          { get; set; } = false;
        public float        MagnetRadius       { get; set; } = 0.5f;
        public FalloffType  MagnetFalloff      { get; set; } = FalloffType.Smooth;
        public DistanceMode MagnetDistanceMode { get; set; } = DistanceMode.Euclidean;

        /// <summary>プレビュー中か。</summary>
        public bool IsPreviewing => _applier.IsActive;

        /// <summary>対象頂点数。プレビュー外は 0。</summary>
        public int AffectedCount => _applier.AffectedCount;

        /// <summary>作業軸ローカルでの s（= y）範囲。UI 表示用。</summary>
        public DeformContext PreviewContext => _applier.Context;

        // ================================================================
        // デフォーマ選択
        // ================================================================

        /// <summary>
        /// デフォーマを切り替える。プレビュー中なら一度巻き戻してから
        /// 新しいデフォーマで再計算する（パラメータの意味が変わるため）。
        /// </summary>
        public bool SelectDeformer(string name)
        {
            var next = DeformerRegistry.Create(name);
            if (next == null) return false;

            bool wasPreviewing = _applier.IsActive;
            if (wasPreviewing) _applier.Revert();

            _deformer = next;

            if (wasPreviewing) ApplyPreview();
            else               SyncMeshes();

            OnRepaint?.Invoke();
            return true;
        }

        // ================================================================
        // プレビュー
        // ================================================================

        /// <summary>
        /// プレビューを開始する。選択が無ければ false。
        /// 既に開始済みなら何もせず true を返す。
        /// </summary>
        public bool BeginPreview()
        {
            if (_applier.IsActive) return true;

            var model = GetModel?.Invoke();
            var axis  = GetWorkAxis?.Invoke();
            if (model == null || axis == null) return false;

            float radius = UseMagnet ? MagnetRadius : 0f;
            if (!_applier.Begin(model, axis, radius, MagnetFalloff, MagnetDistanceMode))
                return false;

            GetToolContext?.Invoke()?.EnterTransformDragging?.Invoke();
            return true;
        }

        /// <summary>
        /// 現在のパラメータでプレビューを更新する。
        /// 未開始なら自動で BeginPreview する。
        /// </summary>
        public void ApplyPreview()
        {
            if (!_applier.IsActive && !BeginPreview()) return;

            var d = Deformer;
            if (d == null) return;

            SyncCameraBendPlane();

            _applier.Apply(d);
            SyncMeshes();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// カメラ奥行軸モードのとき、曲げのたわみ方向をカメラから決める。
        ///
        /// 曲げの回転軸は作業軸ローカルで R_y(φ) * back。
        /// R_y(φ) * (0,0,-1) = (-sinφ, 0, -cosφ) なので、カメラ前方を作業軸ローカルへ
        /// 移して XZ 平面へ射影した (lx, lz) に対し φ = Atan2(-lx, -lz) とすれば
        /// 回転軸が視線に一致する。画面上で見たとおりに曲がる。
        ///
        /// カメラが Y 軸とほぼ平行のときは XZ 射影が消えて向きが決まらないので、
        /// 直前の φ をそのまま残す。
        /// </summary>
        /// <remarks>
        /// 内部で GetToolContext（アクティブなビューポート）を取るため、
        /// 各ビューポートの ctx を使っている最中に呼んではいけない。
        /// 呼ぶのは描画ループへ入る前か、ドラッグ処理の中だけ。
        /// </remarks>
        public bool SyncCameraBendPlane()
        {
            if (!(Deformer?.Params is BendDeformerParams bp)) return false;
            if (!bp.UseCameraBendPlane) return false;

            // 参照するのは常にアクティブなビューポートのカメラ。ここで ctx を
            // 引数で受けると、4面のどれを描いたかで値が変わってしまう。
            var ctx = GetToolContext?.Invoke();
            var wa  = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null) return false;

            Vector3 fwd = ctx.CameraTarget - ctx.CameraPosition;
            if (fwd.sqrMagnitude < 1e-12f) return false;

            Vector3 local = Quaternion.Inverse(wa.Rotation) * fwd.normalized;

            // XZ 成分が小さすぎるとき（視線がローカル Y とほぼ平行）は決められない。
            if (new Vector2(local.x, local.z).sqrMagnitude < 1e-6f) return false;

            float phi = Mathf.Atan2(-local.x, -local.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(bp.BendPlaneAngleDeg, phi)) < 0.01f) return false;

            bp.BendPlaneAngleDeg = phi;
            return true;
        }

        // ================================================================
        // 確定 / 巻き戻し
        // ================================================================

        /// <summary>
        /// 変形を確定して Undo に記録する。
        /// 記録の組み立ては DeformApplier、Record 呼び出しはここ
        /// （RotateTool.ApplyRotation と同じ責務分割）。
        /// </summary>
        public void Commit()
        {
            if (!_applier.IsActive) return;

            var entries = _applier.BuildUndoEntries();

            if (entries.Length > 0 && _undoController != null)
            {
                _undoController.FocusVertexEdit();
                var record = new MultiMeshVertexMoveRecord(entries);
                _undoController.VertexEditStack.Record(record, $"Deform ({DeformerName})");
            }

            // VertexOffsets の基準を現在位置へ追従させる。
            _applier.SyncOriginalPositions();

            ExitPreview();
            OnApplyCompleted?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>変形を捨てて開始位置へ戻す。</summary>
        public void Revert()
        {
            if (!_applier.IsActive) return;

            _applier.Revert();
            SyncMeshes();

            ExitPreview();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 頂点を触らずにギズモだけ組み直させる。形状プレビューの表示切替用。
        /// OnRepaint はギズモデータの再構築まで行う結線になっている。
        /// </summary>
        public void RequestGizmoRefresh()
        {
            OnRepaint?.Invoke();
        }

        /// <summary>デフォーマのパラメータを既定値へ戻す。プレビューは維持する。</summary>
        public void ResetParams()
        {
            Deformer?.Params?.Reset();
            if (_applier.IsActive) ApplyPreview();
            else                   OnRepaint?.Invoke();
        }

        private void ExitPreview()
        {
            _applier.Reset();
            GetToolContext?.Invoke()?.ExitTransformDragging?.Invoke();
        }

        // ================================================================
        // IPlayerToolHandler（ドラッグ操作は持たない）
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }

        /// <summary>回転ハンドルのホバー強調のみ。作業軸そのものは操作対象ではない。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (_dragHandle != HandleKind.None) return;

            if (_phase != DeformPhase.Deform)
            {
                if (_hoverHandle == HandleKind.None) return;
                _hoverHandle = HandleKind.None;
                OnRepaint?.Invoke();
                return;
            }

            var hit = FindHandleAt(screenPos, ctx);
            if (hit == _hoverHandle) return;

            _hoverHandle = hit;
            OnRepaint?.Invoke();
        }

        // ================================================================
        // ギズモ（作業軸の表示のみ）
        // ================================================================

        // ScreenOffset は既定 (60,-60) だが、作業軸は原点そのものが基準なので
        // ずらさず描く。WorkAxisToolHandler と同じ扱い。
        private readonly AxisGizmo _axisGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };

        /// <summary>
        /// 変形の基準になっている作業軸を矢印で表示する。
        /// 操作はできない（ホバー・ドラッグを受けない）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            var wa = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null || !wa.IsVisible) return false;

            // ここで GetToolContext を呼んではいけない。
            // PlayerToolContext は WorldToScreenPos のラムダが共有インスタンスの
            // _cam を参照しており、GetCurrentToolContext を呼ぶたびに踏み替わる。
            // 引数の ctx を非アクティブなビューポートから渡されている最中に
            // アクティブ側の ctx を取ると、この ctx の投影先まで一緒に
            // アクティブカメラへ変わり、他画面の表示がずれる。
            // カメラ奥行軸モードの φ 更新は Viewer 側が描画ループの外で行う。

            // 作業軸フェーズ。作業軸ツールと同じギズモをそのまま出し、
            // 変形ハンドルはグレーアウトで添えるだけにする（掴めないことを示す）。
            if (_phase == DeformPhase.WorkAxis && WorkAxisGizmoProvider != null &&
                WorkAxisGizmoProvider.TryBuildGizmoData(ctx, out data))
            {
                data.ExtraLines = Concat(data.ExtraLines, BuildHandles(wa, ctx, true));
                return true;
            }

            _axisGizmo.Center       = wa.Origin;
            _axisGizmo.Orientation  = wa.Rotation;
            _axisGizmo.HoveredAxis  = AxisGizmo.AxisType.None;
            _axisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _axisGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo    = true,
                Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis = AxisGizmo.AxisType.None,
                ExtraLines  = BuildExtraLines(wa, ctx),
            };
            return true;
        }

        /// <summary>
        /// 六角錐の作業軸 ＋ 変形プレビュー柱 ＋ 回転ハンドルを1本の配列にまとめる。
        ///
        /// 作業軸は「作業軸」ツールと同じ形（WorkAxisGizmoShape.Build）を使うが、
        /// Y 先端の六角形は出さない。ここでは掴めないハンドルだから。
        /// </summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildExtraLines(
            WorkAxisContext wa, ToolContext ctx)
        {
            var axis   = WorkAxisGizmoShape.Build(
                wa, ctx, AxisGizmo.AxisType.None, false, null, null, false);
            var shape  = BuildShapePreview(wa, ctx);
            var handle = BuildHandles(wa, ctx, false);

            return Concat(axis, shape, handle);
        }

        private static PlayerViewportPanel.ScreenPolyline[] Concat(
            params PlayerViewportPanel.ScreenPolyline[][] parts)
        {
            int n = 0;
            foreach (var p in parts) if (p != null) n += p.Length;
            if (n == 0) return null;

            var all = new PlayerViewportPanel.ScreenPolyline[n];
            int k = 0;
            foreach (var p in parts)
            {
                if (p == null) continue;
                System.Array.Copy(p, 0, all, k, p.Length);
                k += p.Length;
            }
            return all;
        }

        // ================================================================
        // 形状プレビュー
        // ================================================================

        // プレビュー専用インスタンス。実体（Deformer）を使うと Prepare が
        // DeformApplier 用の内部状態を書き換えてしまうので分けている。
        private BendDeformer  _bendPreview;
        private TwistDeformer _twistPreview;

        /// <summary>
        /// デフォーマの種類に応じた六角柱ワイヤを返す。無いときは null。
        ///
        /// 曲げ・ねじりとも現在のパラメータをプレビュー用インスタンスへ写してから
        /// 通すので、合計角などを変えるとその場で形が変わる。
        ///
        /// ねじりだけ柱を軸から +X へずらす。ねじりは Y まわりの回転なので、
        /// 断面が Y 軸を中心にしていると各点が自分の円周上を動くだけで外形が
        /// 変わらず、ねじれが見えない。
        ///
        /// なお柱の s 範囲は 0 〜 軸長で渡している（BuildDeformedPrism）。
        /// 起点は PivotAtAxisOrigin の値によらず s = 0 になるので、
        /// 合計角がそのまま柱の端から端までのねじれ角になる。
        /// </summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildShapePreview(
            WorkAxisContext wa, ToolContext ctx)
        {
            if (!ShowShapePreview) return null;

            var d = Deformer;
            if (d == null) return null;

            if (d is BendDeformer bend)
            {
                if (_bendPreview == null) _bendPreview = new BendDeformer();
                _bendPreview.Params.CopyFrom(bend.Params);
                return WorkAxisGizmoShape.BuildDeformedPrism(
                    wa, ctx, _bendPreview, wa.Length, false);
            }

            if (d is TwistDeformer twist)
            {
                if (_twistPreview == null) _twistPreview = new TwistDeformer();
                _twistPreview.Params.CopyFrom(twist.Params);
                return WorkAxisGizmoShape.BuildDeformedPrism(
                    wa, ctx, _twistPreview, wa.Length, true);
            }

            // 回転には形状プレビューを付けない（従来どおり矢印だけ）。
            return null;
        }

        // ================================================================
        // 回転ハンドル
        // ================================================================

        /// <summary>ドラッグ中の角度計算に使う。作業軸ローカル軸まわりのリングとして扱う。</summary>
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        /// <summary>どのパラメータを操作するハンドルか。</summary>
        private enum HandleKind
        {
            None,
            Bend, Twist,
            RotateX, RotateY, RotateZ,
            MoveX,   MoveY,   MoveZ,
            ScaleX,  ScaleY,  ScaleZ,
        }

        /// <summary>ハンドルの操作種別。ドラッグ計算の分岐に使う。</summary>
        private enum HandleMode { Rotate, Move, Scale }

        private HandleKind _hoverHandle = HandleKind.None;
        private HandleKind _dragHandle  = HandleKind.None;
        private float      _dragStartValue;      // ドラッグ開始時のパラメータ値

        /// <summary>直線ハンドル（移動 / 拡大縮小）の軸方向計算に使う。</summary>
        private readonly AxisGizmo _linearGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };

        // GizmoHitTest で当てた結果を BeginGizmoDrag まで持ち越すための控え。
        // フック経由の呼び出しは (ヒットテスト) → (ドラッグ開始) の2段になり、
        // 後者はスクリーン座標を受け取らない（PrimitivePlaceToolHandler と同じ事情）。
        private HandleKind _pendingHandle = HandleKind.None;
        private Vector2    _pendingScreen = Vector2.zero;

        // 角度ドラッグは RotateRingGizmo に集約する。リングは1本ずつ扱うので
        // 軸種別は Y 固定で使い、実際の回転軸は Orientation 側で与える。
        private static readonly AxisGizmo.AxisType HandleAxisSlot = AxisGizmo.AxisType.Y;

        /// <summary>ハンドルの当たり半径（px）。</summary>
        private const float HandleHitRadius = 9f;

        /// <summary>曲げ角度の可動範囲（度）。パネルのスライダと合わせる。</summary>
        private const float BendAngleLimit = 360f;

        /// <summary>ねじり角度の可動範囲（度）。パネルのスライダと合わせる。</summary>
        private const float TwistAngleLimit = 720f;

        /// <summary>回転角の可動範囲（度）。パネルのスライダと合わせる。</summary>
        private const float RotateAngleLimit = 180f;

        /// <summary>移動量の可動範囲。パネルのスライダと合わせる。</summary>
        private const float MoveLimit = 10f;

        /// <summary>倍率の上限。パネルのスライダと合わせる（下限は ScaleDeformerParams.MinScale）。</summary>
        private const float ScaleLimit = 5f;

        /// <summary>直線ハンドルの長さ。軸長に対する比。六角錐より外へ出して掴みやすくする。</summary>
        private const float LinearHandleLengthRatio = 1.2f;

        /// <summary>倍率ハンドルの手応え。1px あたりの倍率変化。</summary>
        private const float ScaleDragSensitivity = 0.01f;

        /// <summary>
        /// 曲げハンドルの半径。軸長に対する比。
        /// 曲げは弧の大きさが読み取れないと意味がないので、Y 軸と同じくらい大きく取る。
        /// </summary>
        private const float BendHandleRadiusRatio = 0.9f;

        /// <summary>
        /// ねじりハンドルの半径。軸長に対する比。断面の回転なのでプレビュー柱を囲む大きさ。
        /// </summary>
        private static float TwistHandleRadiusRatio
            => WorkAxisGizmoShape.PreviewRadiusRatio * WorkAxisGizmoShape.RotateHandleRadiusRatio;

        /// <summary>
        /// 回転ハンドルの半径。軸長に対する比。
        /// 六角錐が軸の向きを示しているので、一番短い Z の六角錐と同程度で足りる。
        /// </summary>
        private static float RotateHandleRadiusRatio => WorkAxisGizmoShape.LengthRatioZ;

        // ================================================================
        // ハンドル定義
        // ================================================================

        /// <summary>1本ぶんのハンドル。表示・当たり判定・ドラッグで共通に使う。</summary>
        private struct HandleDef
        {
            public HandleKind Kind;
            public HandleMode Mode;
            public Vector3    AxisLocal;     // 回転軸 / 伸ばす向き（作業軸ローカル）
            public Vector3    CenterLocal;   // 円弧の中心 / 軸線の始点（作業軸ローカル）
            public float      Radius;        // 円弧の半径 / 軸線の長さ（ワールド単位）
            public float      Value;         // 現在値。回転は角度、移動は距離、拡大縮小は倍率
            public float      Min;           // 可動範囲の下限
            public float      Max;           // 可動範囲の上限
            public AxisGizmo.AxisType ColorAxis;   // 色分け用。None なら既定色
            public AxisGizmo.AxisType DragAxisSlot; // 直線ドラッグで AxisGizmo に渡す軸
        }

        /// <summary>
        /// 現在のデフォーマが出すハンドル一覧。無い種類では空。
        ///
        /// 回す系の軸はどれも「値が正のとき材料が AngleAxis(+θ, この軸) だけ回る」向きに揃える。
        /// こう揃えておかないとドラッグ方向と実際の変形が裏返る。
        ///
        ///   ねじり: Evaluate が AngleAxis(+θ, up) なので +Y そのもの。
        ///   曲げ  : 先端の接線が +Y から +X 側へ倒れる。これは曲げ平面の法線 +Z まわりの
        ///           AngleAxis(-θ) にあたるので、軸としては -Z（back）を採る。
        ///           BendPlaneAngleDeg に追従して向きが変わる。
        ///   回転  : Quaternion.Euler は単一軸なら AngleAxis(+θ, その軸) と一致するので
        ///           right / up / forward をそのまま使う。
        ///           複数軸に値が入っているときは Euler の合成順の都合で、1本を回しても
        ///           その世界軸まわりの純回転にはならない。パラメータが3軸のオイラー角である
        ///           以上、1本ずつ設定する道具として割り切る。
        ///
        /// 直線系（移動 / 拡大縮小）は作業軸ローカル X/Y/Z をそのまま使う。
        /// </summary>
        private void CollectHandles(WorkAxisContext wa, List<HandleDef> dst)
        {
            dst.Clear();
            if (wa == null) return;

            float len = Mathf.Max(WorkAxisContext.MinLength, wa.Length);

            if (Deformer is BendDeformer && Deformer.Params is BendDeformerParams bp)
            {
                dst.Add(new HandleDef
                {
                    Kind        = HandleKind.Bend,
                    Mode        = HandleMode.Rotate,
                    AxisLocal   = Quaternion.Euler(0f, bp.BendPlaneAngleDeg, 0f) * Vector3.back,
                    CenterLocal = Vector3.zero,
                    Radius      = len * BendHandleRadiusRatio,
                    Value       = bp.TotalAngleDeg,
                    Min         = -BendAngleLimit,
                    Max         =  BendAngleLimit,
                    ColorAxis   = AxisGizmo.AxisType.None,
                });
                return;
            }

            if (Deformer is TwistDeformer && Deformer.Params is TwistDeformerParams tp)
            {
                dst.Add(new HandleDef
                {
                    Kind        = HandleKind.Twist,
                    Mode        = HandleMode.Rotate,
                    AxisLocal   = Vector3.up,
                    CenterLocal = new Vector3(0f, len, 0f),
                    Radius      = len * TwistHandleRadiusRatio,
                    Value       = tp.TotalAngleDeg,
                    Min         = -TwistAngleLimit,
                    Max         =  TwistAngleLimit,
                    ColorAxis   = AxisGizmo.AxisType.None,
                });
                return;
            }

            if (Deformer is RotateDeformer && Deformer.Params is RotateDeformerParams rp)
            {
                float r = len * RotateHandleRadiusRatio;

                AddRotate(dst, HandleKind.RotateX, Vector3.right,   AxisGizmo.AxisType.X, rp.AngleX, r);
                AddRotate(dst, HandleKind.RotateY, Vector3.up,      AxisGizmo.AxisType.Y, rp.AngleY, r);
                AddRotate(dst, HandleKind.RotateZ, Vector3.forward, AxisGizmo.AxisType.Z, rp.AngleZ, r);
                return;
            }

            if (Deformer is MoveDeformer && Deformer.Params is MoveDeformerParams mp)
            {
                float l = len * LinearHandleLengthRatio;

                AddLinear(dst, HandleKind.MoveX, HandleMode.Move, Vector3.right,   AxisGizmo.AxisType.X, mp.OffsetX, l, -MoveLimit, MoveLimit);
                AddLinear(dst, HandleKind.MoveY, HandleMode.Move, Vector3.up,      AxisGizmo.AxisType.Y, mp.OffsetY, l, -MoveLimit, MoveLimit);
                AddLinear(dst, HandleKind.MoveZ, HandleMode.Move, Vector3.forward, AxisGizmo.AxisType.Z, mp.OffsetZ, l, -MoveLimit, MoveLimit);
                return;
            }

            if (Deformer is ScaleDeformer && Deformer.Params is ScaleDeformerParams sp)
            {
                float l = len * LinearHandleLengthRatio;

                AddLinear(dst, HandleKind.ScaleX, HandleMode.Scale, Vector3.right,   AxisGizmo.AxisType.X, sp.ScaleX, l, ScaleDeformerParams.MinScale, ScaleLimit);
                AddLinear(dst, HandleKind.ScaleY, HandleMode.Scale, Vector3.up,      AxisGizmo.AxisType.Y, sp.ScaleY, l, ScaleDeformerParams.MinScale, ScaleLimit);
                AddLinear(dst, HandleKind.ScaleZ, HandleMode.Scale, Vector3.forward, AxisGizmo.AxisType.Z, sp.ScaleZ, l, ScaleDeformerParams.MinScale, ScaleLimit);
            }
        }

        private static void AddRotate(
            List<HandleDef> dst, HandleKind kind, Vector3 axisLocal,
            AxisGizmo.AxisType colorAxis, float angleDeg, float radius)
        {
            dst.Add(new HandleDef
            {
                Kind = kind, Mode = HandleMode.Rotate,
                AxisLocal = axisLocal, CenterLocal = Vector3.zero, Radius = radius,
                Value = angleDeg, Min = -RotateAngleLimit, Max = RotateAngleLimit,
                ColorAxis = colorAxis,
            });
        }

        private static void AddLinear(
            List<HandleDef> dst, HandleKind kind, HandleMode mode, Vector3 axisLocal,
            AxisGizmo.AxisType axisSlot, float value, float length, float min, float max)
        {
            dst.Add(new HandleDef
            {
                Kind = kind, Mode = mode,
                AxisLocal = axisLocal, CenterLocal = Vector3.zero, Radius = length,
                Value = value, Min = min, Max = max,
                ColorAxis = axisSlot, DragAxisSlot = axisSlot,
            });
        }

        // 毎フレーム作り直さないための使い回し。
        private readonly List<HandleDef> _handleDefs = new List<HandleDef>(3);

        private bool TryGetHandle(HandleKind kind, WorkAxisContext wa, out HandleDef def)
        {
            CollectHandles(wa, _handleDefs);
            foreach (var h in _handleDefs)
                if (h.Kind == kind) { def = h; return true; }

            def = default;
            return false;
        }

        // ================================================================
        // 表示・当たり判定
        // ================================================================

        /// <summary>グレーアウト時のアルファ倍率。掴めないことを見た目で示す。</summary>
        private const float DimmedAlpha = 0.35f;

        /// <summary>1本ぶんの形状を作る。ヒットテスト用の点列も同時に返す。</summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildOneHandle(
            WorkAxisContext wa, ToolContext ctx, HandleDef h, bool highlighted, bool dimmed,
            out Vector3[] worldPath)
        {
            Color color = h.ColorAxis == AxisGizmo.AxisType.None
                ? (highlighted ? WorkAxisGizmoShape.RotateHandleColorHi
                               : WorkAxisGizmoShape.RotateHandleColor)
                : WorkAxisGizmoShape.AxisColor(h.ColorAxis, highlighted);

            if (dimmed) color.a *= DimmedAlpha;

            switch (h.Mode)
            {
                case HandleMode.Rotate:
                    // 円弧の起点を現在角だけずらすと、実際の変形と同じだけ回って見える。
                    return WorkAxisGizmoShape.BuildRotateHandle(
                        wa, ctx, h.CenterLocal, h.AxisLocal, h.Radius, h.Value,
                        color, highlighted, out worldPath);

                case HandleMode.Move:
                    return WorkAxisGizmoShape.BuildLinearHandle(
                        wa, ctx, h.CenterLocal, h.AxisLocal, h.Radius,
                        WorkAxisGizmoShape.LinearTip.Arrow, color, highlighted, out worldPath);

                default:
                    return WorkAxisGizmoShape.BuildLinearHandle(
                        wa, ctx, h.CenterLocal, h.AxisLocal, h.Radius,
                        WorkAxisGizmoShape.LinearTip.Cube, color, highlighted, out worldPath);
            }
        }

        /// <summary>
        /// 現在のデフォーマのハンドルを全部組み立てる。
        /// dimmed のときはグレーアウトし、ホバー強調も付けない（掴めないため）。
        /// </summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildHandles(
            WorkAxisContext wa, ToolContext ctx, bool dimmed)
        {
            CollectHandles(wa, _handleDefs);
            if (_handleDefs.Count == 0) return null;

            var parts = new PlayerViewportPanel.ScreenPolyline[_handleDefs.Count][];
            for (int i = 0; i < _handleDefs.Count; i++)
            {
                var h  = _handleDefs[i];
                bool hi = !dimmed
                       && (_dragHandle == h.Kind
                           || (_dragHandle == HandleKind.None && _hoverHandle == h.Kind));

                parts[i] = BuildOneHandle(wa, ctx, h, hi, dimmed, out _);
            }
            return Concat(parts);
        }

        /// <summary>
        /// 一番近いハンドルを探す。当たり半径の外なら None。
        /// 表示と同じ形状を組み立てて判定するので、見えている線と掴める位置がずれない。
        /// </summary>
        private HandleKind FindHandleAt(Vector2 screenPos, ToolContext ctx)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null || ctx == null) return HandleKind.None;

            CollectHandles(wa, _handleDefs);
            if (_handleDefs.Count == 0) return HandleKind.None;

            Vector2 imgui = ToImgui(screenPos);
            var   best     = HandleKind.None;
            float bestDist = HandleHitRadius;

            foreach (var h in _handleDefs)
            {
                BuildOneHandle(wa, ctx, h, false, false, out var path);
                if (path == null || path.Length < 2) continue;

                for (int i = 0; i < path.Length - 1; i++)
                {
                    float d = AxisGizmo.DistanceToSegment(
                        imgui, ctx.WorldToScreen(path[i]), ctx.WorldToScreen(path[i + 1]));
                    if (d < bestDist) { bestDist = d; best = h.Kind; }
                }
            }
            return best;
        }

        /// <summary>スクリーン系（Y 下）→ ctx 系。WorkAxisToolHandler と同じ変換。</summary>
        private Vector2 ToImgui(Vector2 screenPos)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPos.x, h - screenPos.y);
        }

        /// <summary>
        /// ハンドルに当たったか（MoveToolHandler.GizmoHitTestOverride 用）。
        /// 当たったハンドルと押下位置は BeginGizmoDrag のために控えておく。
        /// </summary>
        public bool GizmoHitTest(Vector2 screenPos, ToolContext ctx)
        {
            // 作業軸フェーズでは変形ハンドルを掴ませない（表示はグレーアウトのみ）。
            if (_phase != DeformPhase.Deform)
            {
                _pendingHandle = HandleKind.None;
                return false;
            }

            _pendingHandle = FindHandleAt(screenPos, ctx);
            if (_pendingHandle != HandleKind.None) _pendingScreen = screenPos;
            return _pendingHandle != HandleKind.None;
        }

        /// <summary>
        /// ドラッグ開始（OnDragStartExtra 用）。座標を受け取らないので
        /// GizmoHitTest で控えた押下位置を使う。
        /// ハンドルに当たっていないドラッグでは何もしない（矩形／投げ縄選択が続く）。
        /// </summary>
        public void BeginGizmoDrag()
        {
            _dragHandle = HandleKind.None;

            var kind = _pendingHandle;
            _pendingHandle = HandleKind.None;
            if (kind == HandleKind.None) return;

            var ctx = GetToolContext?.Invoke();
            var wa  = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null) return;
            if (!TryGetHandle(kind, wa, out var h)) return;

            if (h.Mode == HandleMode.Rotate)
            {
                // リングは1本なので軸種別は Y 固定で使い、実際の向きは Orientation で与える。
                // こうすると RotateRingGizmo 内の「軸がカメラ側を向くか」の符号判定が
                // そのまま効き、視点が裏へ回ってもドラッグ方向が反転しない。
                _ringGizmo.Center      = wa.LocalToWorld(h.CenterLocal);
                _ringGizmo.Orientation = wa.Rotation * Quaternion.FromToRotation(Vector3.up, h.AxisLocal);

                if (!_ringGizmo.BeginAngleDrag(ctx, ToImgui(_pendingScreen), HandleAxisSlot)) return;
            }
            else
            {
                // 直線系は AxisGizmo の軸方向計算を借りる。Orientation に作業軸の姿勢を
                // 入れておけば、ローカル X/Y/Z がそのまま画面上の軸方向へ写る。
                _linearGizmo.Center      = wa.LocalToWorld(h.CenterLocal);
                _linearGizmo.Orientation = wa.Rotation;
            }

            // ドラッグ中はこの値を基準に絶対計算する。誤差が溜まらない。
            _dragStartValue = h.Value;
            _dragHandle     = kind;

            BeginPreview();
        }

        /// <summary>ドラッグ中（OnToolDragExtra 用）。</summary>
        public void GizmoDrag(Vector2 screenPos)
        {
            if (_dragHandle == HandleKind.None) return;

            var wa = GetWorkAxis?.Invoke();
            if (wa == null || !TryGetHandle(_dragHandle, wa, out var h)) return;

            float value;
            switch (h.Mode)
            {
                case HandleMode.Rotate:
                    value = _dragStartValue + _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));
                    break;

                case HandleMode.Move:
                    if (!TryComputeMoveDelta(screenPos, h, out float moved)) return;
                    value = _dragStartValue + moved;
                    break;

                default:
                    value = _dragStartValue * ComputeScaleFactor(screenPos, h);
                    break;
            }

            SetValue(_dragHandle, Mathf.Clamp(value, h.Min, h.Max));

            ApplyPreview();
            OnParamsChangedByGizmo?.Invoke();
        }

        /// <summary>
        /// 押下位置からの累計移動量を、作業軸ローカル軸方向の距離へ直す。
        ///
        /// 差分を毎フレーム足し込まず、押下位置からの総差分を一度に換算する。
        /// これで往復させても誤差が溜まらない。
        /// ComputeAxisDelta は WorldToScreenPos 系（+Y が画面下）を要求するが、
        /// ここへ来るスクリーン座標はパネルの ToViewportCoord 系（+Y が画面上）なので
        /// Y を反転して渡す。
        /// </summary>
        private bool TryComputeMoveDelta(Vector2 screenPos, HandleDef h, out float moved)
        {
            moved = 0f;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return false;

            Vector2 total = screenPos - _pendingScreen;
            Vector3 world = _linearGizmo.ComputeAxisDelta(
                new Vector2(total.x, -total.y), h.DragAxisSlot, ctx);

            if (world == Vector3.zero) return false;

            // ワールド差分を軸方向へ射影して符号付きの距離にする。
            Vector3 axisWorld = _linearGizmo.GetOrientedAxisDirection(h.DragAxisSlot);
            if (axisWorld.sqrMagnitude < 1e-12f) return false;

            moved = Vector3.Dot(world, axisWorld.normalized);
            return true;
        }

        /// <summary>
        /// 押下位置からの倍率。軸の画面上の向きへ引いた量を倍率へ直す。
        ///
        /// AxisGizmo.ComputeScaleFactor は自前の画面基準を持つが、こちらは
        /// 直線ハンドルの実際の見え方（作業軸ローカル軸の投影）で測りたいので
        /// ここで計算する。
        /// </summary>
        private float ComputeScaleFactor(Vector2 screenPos, HandleDef h)
        {
            var ctx = GetToolContext?.Invoke();
            var wa  = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null) return 1f;

            Vector2 origin = ctx.WorldToScreen(wa.LocalToWorld(h.CenterLocal));
            Vector2 tip    = ctx.WorldToScreen(wa.LocalToWorld(h.CenterLocal + h.AxisLocal * h.Radius));

            Vector2 dir = tip - origin;
            if (dir.sqrMagnitude < 1e-4f) return 1f;
            dir.Normalize();

            // ToImgui 済みの座標で測る。押下位置も同じ系へ揃える。
            float along = Vector2.Dot(ToImgui(screenPos) - ToImgui(_pendingScreen), dir);

            return Mathf.Max(0.01f, 1f + along * ScaleDragSensitivity);
        }

        /// <summary>ドラッグ終了（OnToolDragEndExtra 用）。</summary>
        public void EndGizmoDrag()
        {
            if (_dragHandle == HandleKind.None) return;

            _dragHandle    = HandleKind.None;
            _pendingHandle = HandleKind.None;
            _ringGizmo.EndAngleDrag();
            OnParamsChangedByGizmo?.Invoke();
            OnRepaint?.Invoke();
        }

        private void SetValue(HandleKind kind, float v)
        {
            switch (kind)
            {
                case HandleKind.Bend:
                    if (Deformer?.Params is BendDeformerParams bp) bp.TotalAngleDeg = v;
                    break;
                case HandleKind.Twist:
                    if (Deformer?.Params is TwistDeformerParams tp) tp.TotalAngleDeg = v;
                    break;
                case HandleKind.RotateX:
                case HandleKind.RotateY:
                case HandleKind.RotateZ:
                    if (Deformer?.Params is RotateDeformerParams rp)
                    {
                        if      (kind == HandleKind.RotateX) rp.AngleX = v;
                        else if (kind == HandleKind.RotateY) rp.AngleY = v;
                        else                                 rp.AngleZ = v;
                    }
                    break;
                case HandleKind.MoveX:
                case HandleKind.MoveY:
                case HandleKind.MoveZ:
                    if (Deformer?.Params is MoveDeformerParams mp)
                    {
                        if      (kind == HandleKind.MoveX) mp.OffsetX = v;
                        else if (kind == HandleKind.MoveY) mp.OffsetY = v;
                        else                               mp.OffsetZ = v;
                    }
                    break;
                case HandleKind.ScaleX:
                case HandleKind.ScaleY:
                case HandleKind.ScaleZ:
                    if (Deformer?.Params is ScaleDeformerParams sp)
                    {
                        if      (kind == HandleKind.ScaleX) sp.ScaleX = v;
                        else if (kind == HandleKind.ScaleY) sp.ScaleY = v;
                        else                                sp.ScaleZ = v;
                    }
                    break;
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>変形した全メッシュを GPU へ同期する。</summary>
        private void SyncMeshes()
        {
            var model = GetModel?.Invoke();
            if (model == null || OnSyncMeshPositions == null) return;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) OnSyncMeshPositions(mc);
            }
        }
    }
}
