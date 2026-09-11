// PlayerLatticeSubPanel.cs
// 格子変形のサブパネル。状態機（Idle / Placement / Deform）に応じて
// 操作できるウィジェットを切り替える。
//
// 【操作の流れ】
//   1. メッシュ頂点を選ぶ
//   2. 「格子変形開始」→ 選択範囲へ格子が生成される（Placement）
//   3. 分割数・中心・サイズを決め、必要なら「選択フィット」で合わせ直す
//      配置中はビューポートでメッシュ頂点を選び直せる。選び直したら「選択フィット」を押す
//   4. 「変形開始」→ 格子点をビューポートで選び、移動 / 拡大縮小 / 回転する（Deform）
//      対象頂点は「変形開始」の時点の選択で確定する
//   5. 「適用」で確定、「取消」で開始前へ戻す
//
// 分割数の変更を Placement 中だけに限るのは仕様どおり。変形してから分割数を
// 変えたい場合は「変形リセット」で制御点を戻すか、取消してやり直す。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerLatticeSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<LatticeToolHandler> GetH;

        // ================================================================
        // ウィジェット
        // ================================================================

        // UI 自動操作の ID は "lattice.<下の Id>"（UiControlAttribute.cs）。
        // 手順は「格子の姿勢設定」→（分割数・中心・大きさ・選択フィット）→「変形開始」→
        // 格子点を動かす →「適用」。各段で使えない項目は無効になる（uiSetValue / uiClick が拒否する）。
        [UiControl(Ignore = true)]
        private VisualElement _root;

        [UiControl("cells.x", Description = "格子の X 方向の分割数（姿勢設定中だけ変えられる）")]
        private IntegerField _cellsX;
        [UiControl("cells.y", Description = "格子の Y 方向の分割数（姿勢設定中だけ変えられる）")]
        private IntegerField _cellsY;
        [UiControl("cells.z", Description = "格子の Z 方向の分割数（姿勢設定中だけ変えられる）")]
        private IntegerField _cellsZ;
        [UiControl(Ignore = true)]
        private VisualElement _cellsRow;

        // 格子全体の位置と大きさ（作業軸ローカル）。Placement 中のみ編集できる。
        [UiControl("center.x", Description = "格子の中心 X（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _centerX;
        [UiControl("center.y", Description = "格子の中心 Y（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _centerY;
        [UiControl("center.z", Description = "格子の中心 Z（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _centerZ;
        [UiControl("size.x", Description = "格子の大きさ X（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _sizeX;
        [UiControl("size.y", Description = "格子の大きさ Y（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _sizeY;
        [UiControl("size.z", Description = "格子の大きさ Z（作業軸ローカル、姿勢設定中だけ変えられる）")]
        private FloatField _sizeZ;
        [UiControl(Ignore = true)]
        private VisualElement _boundsGroup;

        // Deform 中の格子点ギズモのサブモード。
        [UiControl(Ignore = true)]
        private VisualElement _modeGroup;
        [UiControl("gizmo.move", Safety = UiSafety.SafeWrite, Description = "格子点ギズモを移動にする（変形中だけ）")]
        private Button _modeMoveBtn;
        [UiControl("gizmo.scale", Safety = UiSafety.SafeWrite, Description = "格子点ギズモを拡大縮小にする（変形中だけ）")]
        private Button _modeScaleBtn;
        [UiControl("gizmo.rotate", Safety = UiSafety.SafeWrite, Description = "格子点ギズモを回転にする（変形中だけ）")]
        private Button _modeRotateBtn;

        [UiControl("begin", Safety = UiSafety.SafeWrite, Description = "格子の姿勢設定を始める")]
        private Button _beginBtn;
        [UiControl("fitToSelection", Safety = UiSafety.SafeWrite, Description = "格子を選択に合わせる（姿勢設定中だけ）")]
        private Button _fitBtn;
        [UiControl("beginDeform", Safety = UiSafety.SafeWrite, Description = "変形を始める（姿勢設定中だけ）")]
        private Button _deformBtn;
        [UiControl("resetDeform", Safety = UiSafety.SafeWrite, Description = "格子点の変形をリセットする（変形中だけ）")]
        private Button _resetBtn;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "適用する")]
        private Button _applyBtn;
        [UiControl("cancel", Safety = UiSafety.SafeWrite, Description = "取り消す")]
        private Button _cancelBtn;

        private static readonly Color ActiveBtnColor   = new Color(0.20f, 0.45f, 0.25f);
        private static readonly Color InactiveBtnColor = new Color(0.25f, 0.25f, 0.25f);

        [UiControl("state", Safety = UiSafety.ReadOnly, Description = "今の段階")]
        private Label _stateLabel;
        [UiControl("info", Safety = UiSafety.ReadOnly, Description = "案内")]
        private Label _infoLabel;

        // スピナー → ハンドラ → Refresh の往復で無限ループしないようにする。
        private bool _suppressCallback;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("格子変形 (Lattice)"));

            var help = new HelpBox(
                "頂点を選んでから「格子の姿勢設定」を押します。\n" +
                "配置中はビューポートでメッシュ頂点を選び直せます。\n" +
                "選び直したら「選択フィット」で格子を合わせ直してください。\n" +
                "格子全体の移動・回転は「作業軸」パネルで行います\n" +
                "（作業軸へ移っても格子は保持されます）。\n" +
                "「変形開始」後はビューポートで格子点をクリック / 矩形選択し、\n" +
                "ギズモで移動 / 拡大縮小 / 回転します（Ctrl クリックで追加・解除）。\n" +
                "拡大縮小と回転の基準点は選択格子点の重心です。",
                HelpBoxMessageType.Info);
            help.style.color = new StyleColor(Color.white);
            help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(help);

            _stateLabel = new Label();
            _stateLabel.style.color     = new StyleColor(Color.white);
            _stateLabel.style.marginTop = 4;
            _root.Add(_stateLabel);

            // ── 分割数 ────────────────────────────────────────────────
            _root.Add(Header("分割数（セル数）"));

            _cellsRow = new VisualElement();
            _cellsRow.style.flexDirection = FlexDirection.Row;
            _cellsRow.style.marginBottom  = 3;

            _cellsX = MakeCellField("X");
            _cellsY = MakeCellField("Y");
            _cellsZ = MakeCellField("Z");
            _cellsRow.Add(_cellsX); _cellsRow.Add(_cellsY); _cellsRow.Add(_cellsZ);
            _root.Add(_cellsRow);

            _fitBtn = new Button(() => { GetH?.Invoke()?.FitToSelection(); Refresh(); })
            { text = "選択フィット" };
            _fitBtn.style.marginBottom = 2;
            _root.Add(_fitBtn);

            // ── 格子全体の位置と大きさ ────────────────────────────────
            // 作業軸にはスケールが無いため、格子の拡大縮小はここで行う。
            // 中心・サイズはどちらも作業軸ローカルの値。
            _boundsGroup = new VisualElement();

            _boundsGroup.Add(Header("格子の中心（作業軸ローカル）"));
            var centerRow = new VisualElement();
            centerRow.style.flexDirection = FlexDirection.Row;
            centerRow.style.marginBottom  = 3;
            _centerX = MakeField("X"); _centerY = MakeField("Y"); _centerZ = MakeField("Z");
            centerRow.Add(_centerX); centerRow.Add(_centerY); centerRow.Add(_centerZ);
            _boundsGroup.Add(centerRow);

            _boundsGroup.Add(Header("格子のサイズ"));
            var sizeRow = new VisualElement();
            sizeRow.style.flexDirection = FlexDirection.Row;
            sizeRow.style.marginBottom  = 3;
            _sizeX = MakeField("X"); _sizeY = MakeField("Y"); _sizeZ = MakeField("Z");
            sizeRow.Add(_sizeX); sizeRow.Add(_sizeY); sizeRow.Add(_sizeZ);
            _boundsGroup.Add(sizeRow);

            _root.Add(_boundsGroup);

            // ── 状態遷移 ──────────────────────────────────────────────
            _beginBtn = new Button(() => { GetH?.Invoke()?.BeginPlacement(); Refresh(); })
            { text = "格子の姿勢設定" };
            _beginBtn.style.marginTop = 6;
            _root.Add(_beginBtn);

            _deformBtn = new Button(() => { GetH?.Invoke()?.BeginDeform(); Refresh(); })
            { text = "変形開始" };
            _root.Add(_deformBtn);

            _resetBtn = new Button(() => { GetH?.Invoke()?.ResetDeformation(); Refresh(); })
            { text = "変形リセット" };
            _root.Add(_resetBtn);

            // ── 格子点ギズモのサブモード ──────────────────────────────
            // 矢印 / キューブ / リングは同時に描けないため切り替えて使う。
            _modeGroup = new VisualElement();
            _modeGroup.Add(Header("格子点の操作"));

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom  = 3;

            _modeMoveBtn   = new Button(() => SetMode(LatticeToolHandler.PointGizmoMode.Move))   { text = "移動" };
            _modeScaleBtn  = new Button(() => SetMode(LatticeToolHandler.PointGizmoMode.Scale))  { text = "拡大縮小" };
            _modeRotateBtn = new Button(() => SetMode(LatticeToolHandler.PointGizmoMode.Rotate)) { text = "回転" };
            _modeMoveBtn.style.flexGrow  = 1; _modeMoveBtn.style.marginRight  = 2;
            _modeScaleBtn.style.flexGrow = 1; _modeScaleBtn.style.marginRight = 2;
            _modeRotateBtn.style.flexGrow = 1;

            modeRow.Add(_modeMoveBtn); modeRow.Add(_modeScaleBtn); modeRow.Add(_modeRotateBtn);
            _modeGroup.Add(modeRow);
            _root.Add(_modeGroup);

            // ── 確定 / 取消 ───────────────────────────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 6;

            // 確定はコマンド経由。CommitViaCommand が開始位置へ戻して
            // ApplyLatticeDeformCommand を送り、その受け口が変形と Undo 記録を行う。
            _applyBtn = new Button(() => { GetH?.Invoke()?.CommitViaCommand(); Refresh(); }) { text = "適用" };
            _applyBtn.style.flexGrow = 1; _applyBtn.style.marginRight = 2;

            _cancelBtn = new Button(() => { GetH?.Invoke()?.Cancel(); Refresh(); }) { text = "取消" };
            _cancelBtn.style.flexGrow = 1;

            btnRow.Add(_applyBtn); btnRow.Add(_cancelBtn);
            _root.Add(btnRow);

            _infoLabel = new Label();
            _infoLabel.style.fontSize  = 10;
            _infoLabel.style.marginTop = 4;
            _infoLabel.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(_infoLabel);

            Refresh();
        }

        private IntegerField MakeCellField(string label)
        {
            var f = new IntegerField(label) { value = 2 };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.style.color       = new StyleColor(Color.white);
            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                ApplyCells();
            });
            return f;
        }

        private void ApplyCells()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            h.SetCells(_cellsX.value, _cellsY.value, _cellsZ.value);
            Refresh();
        }

        private FloatField MakeField(string label)
        {
            var f = new FloatField(label) { value = 0f };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                ApplyBounds();
            });
            return f;
        }

        /// <summary>
        /// 中心とサイズを格子へ渡す。厚みが極端に薄い軸は格子側で最小値まで
        /// 広げられるため、確定値は Refresh で読み直して書き戻す。
        /// </summary>
        private void ApplyBounds()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            h.SetBounds(
                new Vector3(_centerX.value, _centerY.value, _centerZ.value),
                new Vector3(_sizeX.value,   _sizeY.value,   _sizeZ.value));
            Refresh();
        }

        private void SetMode(LatticeToolHandler.PointGizmoMode mode)
        {
            var h = GetH?.Invoke();
            if (h != null) h.Mode = mode;
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH?.Invoke();
            if (_root == null) return;

            var state = h?.State ?? LatticeToolHandler.LatticeState.Idle;
            bool idle      = state == LatticeToolHandler.LatticeState.Idle;
            bool placement = state == LatticeToolHandler.LatticeState.Placement;
            bool deform    = state == LatticeToolHandler.LatticeState.Deform;

            _suppressCallback = true;
            try
            {
                if (h != null)
                {
                    _cellsX?.SetValueWithoutNotify(h.Grid.CellsX);
                    _cellsY?.SetValueWithoutNotify(h.Grid.CellsY);
                    _cellsZ?.SetValueWithoutNotify(h.Grid.CellsZ);

                    Vector3 c = h.Grid.BaseCenter;
                    Vector3 sz = h.Grid.BaseSize;
                    _centerX?.SetValueWithoutNotify(c.x);
                    _centerY?.SetValueWithoutNotify(c.y);
                    _centerZ?.SetValueWithoutNotify(c.z);
                    _sizeX?.SetValueWithoutNotify(sz.x);
                    _sizeY?.SetValueWithoutNotify(sz.y);
                    _sizeZ?.SetValueWithoutNotify(sz.z);
                }
            }
            finally { _suppressCallback = false; }

            // 分割数と格子の中心・サイズの変更は配置中のみ。
            // どちらも制御点を作り直すため、変形中は禁止する。
            _cellsRow?.SetEnabled(placement);
            _boundsGroup?.SetEnabled(placement);
            _fitBtn?.SetEnabled(placement);
            _beginBtn?.SetEnabled(idle);
            _deformBtn?.SetEnabled(placement);
            _resetBtn?.SetEnabled(deform);
            _modeGroup?.SetEnabled(deform);
            _applyBtn?.SetEnabled(!idle);
            _cancelBtn?.SetEnabled(!idle);

            RepaintModeButtons();

            if (_stateLabel != null)
            {
                _stateLabel.text =
                    idle      ? "状態: 未開始" :
                    placement ? "状態: 格子配置中（頂点を選び直せます / メッシュは変形しません）"
                              : "状態: 格子変形中";
            }

            if (_infoLabel != null)
            {
                if (h == null || idle)
                {
                    _infoLabel.text = "頂点を選択してから開始してください。";
                }
                else
                {
                    _infoLabel.text =
                        $"対象 {h.AffectedCount} 頂点 / 格子点 {h.ControlPointCount} 個"
                        + (deform
                            ? $" / 選択 {h.SelectedPointCount} 個"
                            : "（対象頂点は「選択フィット」か「変形開始」で取り直します）");
                }
            }
        }

        private void RepaintModeButtons()
        {
            var cur = GetH?.Invoke()?.Mode ?? LatticeToolHandler.PointGizmoMode.Move;

            if (_modeMoveBtn != null)
                _modeMoveBtn.style.backgroundColor =
                    (cur == LatticeToolHandler.PointGizmoMode.Move) ? ActiveBtnColor : InactiveBtnColor;
            if (_modeScaleBtn != null)
                _modeScaleBtn.style.backgroundColor =
                    (cur == LatticeToolHandler.PointGizmoMode.Scale) ? ActiveBtnColor : InactiveBtnColor;
            if (_modeRotateBtn != null)
                _modeRotateBtn.style.backgroundColor =
                    (cur == LatticeToolHandler.PointGizmoMode.Rotate) ? ActiveBtnColor : InactiveBtnColor;
        }

        // ================================================================
        // ウィジェットヘルパー
        // ================================================================

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(Color.white);
            l.style.marginTop    = 6;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
