// PlayerDeformSubPanel.cs
// デフォーマ（回転 / 曲げ / ねじり）のサブパネル。数値入力とスライダのみで操作する。
//
// デフォーマは DeformerRegistry から取得し、パラメータ UI は選択中の型で
// 出し分ける。新しいデフォーマを足したときは Build のグループ生成、Refresh、
// UpdateGroupVisibility の3箇所へ1ブロックずつ足す。
//
// 【ドロップダウン】表示は DisplayName（日本語）、選択の受け渡しは index。
//   Name は DeformerRegistry の検索キーなので UI へは出さない。
//   HiddenDeformerNames に挙げたものはこのパネルのリストから外す。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;

namespace Poly_Ling.Player
{
    public class PlayerDeformSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<DeformToolHandler> GetH;

        // ================================================================
        // ウィジェット
        // ================================================================

        /// <summary>
        /// このパネルのドロップダウンから外すデフォーマの Name（内部 ID）。
        /// レジストリからは消していないので「歪み複製」パネルでは従来どおり使える。
        /// </summary>
        private static readonly string[] HiddenDeformerNames = { "Wave" };

        /// <summary>
        /// 右ペイン先頭へ埋め込む作業軸パネル。Viewer が Build 前に設定する。
        /// 左ペインの「作業軸」ツールが持つものとは別インスタンス。
        /// </summary>
        // UI 自動操作では "deform.workAxis.*"。作業軸フェーズのときだけ表示されるので、
        // 表示の下準備（RevealAxisPhase）でフェーズを「作業軸設定」に切り替える。
        [UiNested("workAxis", Reveal = nameof(RevealAxisPhase))]
        public PlayerWorkAxisSubPanel WorkAxisPanel;

        // UI 自動操作の ID は "deform.<下の Id>"（UiControlAttribute.cs）。
        // 変形の欄は変形フェーズのときだけ、種類ごとの欄はその種類のデフォーマのときだけ表示される。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("deformer", Description = "デフォーマの種類（表示名。選択肢は uiGetValue の choices）")]
        private DropdownField _deformerDropdown;
        [UiControl("info", Safety = UiSafety.ReadOnly, Reveal = nameof(RevealDeformPhase), Description = "対象頂点数と軸ローカルの範囲")]
        private Label         _infoLabel;

        // フェーズ切替。2つのボタンで排他選択する（作業軸パネルの 移動/回転 と同じ作り）。
        [UiControl("phase.workAxis", Safety = UiSafety.SafeWrite, Description = "作業軸設定のフェーズにする")]
        private Button _phaseAxisBtn;
        [UiControl("phase.deform", Safety = UiSafety.SafeWrite, Description = "変形のフェーズにする（文言はデフォーマ名から作る。例「曲げ開始」）")]
        private Button _phaseDeformBtn;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDeformPhase), Description = "変形を適用する")]
        private Button _applyBtn;
        [UiControl("revert", Safety = UiSafety.SafeWrite, Reveal = nameof(RevealDeformPhase), Description = "適用していない変形を取り消す")]
        private Button _revertBtn;

        // フェーズごとに出し分ける本体。見出し・説明・種類は共通で常に出す。
        [UiControl(Ignore = true)]
        private VisualElement _workAxisBody;
        [UiControl(Ignore = true)]
        private VisualElement _deformBody;

        private static readonly Color ActiveBtnColor   = new Color(0.20f, 0.40f, 0.62f);
        private static readonly Color InactiveBtnColor = new Color(0.28f, 0.28f, 0.28f);

        // ドロップダウンの index に対応する内部 ID。表示は DisplayName なので、
        // 選択結果を DeformToolHandler.SelectDeformer へ渡すために別に持つ。
        private readonly List<string> _deformerIds = new List<string>();

        // 形状プレビュー表示
        [UiControl("shapePreview", Reveal = nameof(RevealDeformPhase), Description = "軸の形状プレビューを表示する")]
        private Toggle _shapePreviewToggle;

        // Rotate
        [UiControl(Ignore = true)]
        private VisualElement _rotateGroup;
        [UiControl("rotate.x", Reveal = nameof(RevealRotate), Description = "回転角 X（度）")]
        private Slider        _rotSliderX;
        [UiControl("rotate.y", Reveal = nameof(RevealRotate), Description = "回転角 Y（度）")]
        private Slider        _rotSliderY;
        [UiControl("rotate.z", Reveal = nameof(RevealRotate), Description = "回転角 Z（度）")]
        private Slider        _rotSliderZ;
        [UiControl("rotate.xValue", Reveal = nameof(RevealRotate), Description = "回転角 X の数値入力")]
        private FloatField    _rotFieldX;
        [UiControl("rotate.yValue", Reveal = nameof(RevealRotate), Description = "回転角 Y の数値入力")]
        private FloatField    _rotFieldY;
        [UiControl("rotate.zValue", Reveal = nameof(RevealRotate), Description = "回転角 Z の数値入力")]
        private FloatField    _rotFieldZ;

        // Bend
        [UiControl(Ignore = true)]
        private VisualElement _bendGroup;
        [UiControl("bend.angle", Reveal = nameof(RevealBend), Description = "曲げ角度（度）")]
        private Slider        _bendAngleSlider;
        [UiControl("bend.direction", Reveal = nameof(RevealBend), Description = "まげ方向（度）")]
        private Slider        _bendPlaneSlider;
        [UiControl("bend.angleValue", Reveal = nameof(RevealBend), Description = "曲げ角度の数値入力")]
        private FloatField    _bendAngleField;
        [UiControl("bend.directionValue", Reveal = nameof(RevealBend), Description = "まげ方向の数値入力")]
        private FloatField    _bendPlaneField;
        [UiControl("bend.fromWorkAxisOrigin", Reveal = nameof(RevealBend), Description = "作業軸の原点を起点にする")]
        private Toggle        _bendPivotToggle;
        [UiControl("bend.cameraDepthAxis", Reveal = nameof(RevealBend), Description = "カメラ奥行軸で曲げる")]
        private Toggle        _bendCameraPlaneToggle;

        // Move
        [UiControl(Ignore = true)]
        private VisualElement _moveGroup;
        [UiControl("move.x", Reveal = nameof(RevealMove), Description = "移動量 X（作業軸ローカル）")]
        private Slider        _movSliderX;
        [UiControl("move.y", Reveal = nameof(RevealMove), Description = "移動量 Y（作業軸ローカル）")]
        private Slider        _movSliderY;
        [UiControl("move.z", Reveal = nameof(RevealMove), Description = "移動量 Z（作業軸ローカル）")]
        private Slider        _movSliderZ;
        [UiControl("move.xValue", Reveal = nameof(RevealMove), Description = "移動量 X の数値入力")]
        private FloatField    _movFieldX;
        [UiControl("move.yValue", Reveal = nameof(RevealMove), Description = "移動量 Y の数値入力")]
        private FloatField    _movFieldY;
        [UiControl("move.zValue", Reveal = nameof(RevealMove), Description = "移動量 Z の数値入力")]
        private FloatField    _movFieldZ;

        // Scale
        [UiControl(Ignore = true)]
        private VisualElement _scaleGroup;
        [UiControl("scale.x", Reveal = nameof(RevealScale), Description = "倍率 X（作業軸ローカル）")]
        private Slider        _sclSliderX;
        [UiControl("scale.y", Reveal = nameof(RevealScale), Description = "倍率 Y（作業軸ローカル）")]
        private Slider        _sclSliderY;
        [UiControl("scale.z", Reveal = nameof(RevealScale), Description = "倍率 Z（作業軸ローカル）")]
        private Slider        _sclSliderZ;
        [UiControl("scale.xValue", Reveal = nameof(RevealScale), Description = "倍率 X の数値入力")]
        private FloatField    _sclFieldX;
        [UiControl("scale.yValue", Reveal = nameof(RevealScale), Description = "倍率 Y の数値入力")]
        private FloatField    _sclFieldY;
        [UiControl("scale.zValue", Reveal = nameof(RevealScale), Description = "倍率 Z の数値入力")]
        private FloatField    _sclFieldZ;

        // Twist
        [UiControl(Ignore = true)]
        private VisualElement _twistGroup;
        [UiControl("twist.angle", Reveal = nameof(RevealTwist), Description = "ねじり角度（度）")]
        private Slider        _twistAngleSlider;
        [UiControl("twist.angleValue", Reveal = nameof(RevealTwist), Description = "ねじり角度の数値入力")]
        private FloatField    _twistAngleField;
        [UiControl("twist.fromWorkAxisOrigin", Reveal = nameof(RevealTwist), Description = "作業軸の原点を起点にする")]
        private Toggle        _twistPivotToggle;

        // Magnet
        [UiControl("magnet.enabled", Reveal = nameof(RevealDeformPhase), Description = "マグネット（比例編集）を使う")]
        private Toggle    _magnetToggle;
        [UiControl("magnet.radius", Reveal = nameof(RevealDeformPhase), Description = "マグネットの半径")]
        private Slider    _magnetRadius;
        [UiControl("magnet.falloff", Reveal = nameof(RevealDeformPhase), Description = "マグネットの減衰")]
        private EnumField _magnetFalloff;
        [UiControl("magnet.distanceMode", Reveal = nameof(RevealDeformPhase), Description = "マグネットの距離")]
        private EnumField _magnetDistance;

        // ================================================================
        // UI 自動操作の表示の下準備（UiControl / UiNested の Reveal）。
        // 利用者と同じくフェーズボタン・種類のドロップダウンで切り替える。
        // ================================================================

        private bool RevealAxisPhase()
        {
            var h = GetH?.Invoke();
            if (h == null || h.Phase == DeformToolHandler.DeformPhase.WorkAxis) return false;
            SetPhase(DeformToolHandler.DeformPhase.WorkAxis);
            return true;
        }

        private bool RevealDeformPhase()
        {
            var h = GetH?.Invoke();
            if (h == null || h.Phase == DeformToolHandler.DeformPhase.Deform) return false;
            SetPhase(DeformToolHandler.DeformPhase.Deform);
            return true;
        }

        /// <summary>
        /// 変形フェーズにし、Params が TParams のデフォーマを選ぶ。
        /// 候補は種類のドロップダウンと同じ並び（_deformerIds）で、DeformerRegistry.Create で
        /// 作った既定インスタンスの Params の型で見分ける。選ぶのはドロップダウンの index 代入
        /// （利用者の選択と同じ ChangeEvent を通る）。
        /// </summary>
        private bool RevealDeformer<TParams>() where TParams : class
        {
            bool changed = RevealDeformPhase();
            if (GetH?.Invoke()?.Deformer?.Params is TParams) return changed;
            if (_deformerDropdown == null) return changed;

            for (int i = 0; i < _deformerIds.Count; i++)
            {
                if (DeformerRegistry.Create(_deformerIds[i])?.Params is TParams)
                {
                    _deformerDropdown.index = i;
                    return true;
                }
            }
            return changed;
        }

        private bool RevealRotate() => RevealDeformer<RotateDeformerParams>();
        private bool RevealMove()   => RevealDeformer<MoveDeformerParams>();
        private bool RevealScale()  => RevealDeformer<ScaleDeformerParams>();
        private bool RevealBend()   => RevealDeformer<BendDeformerParams>();
        private bool RevealTwist()  => RevealDeformer<TwistDeformerParams>();

        // 再入防止。スライダ→フィールドの書き戻しで無限ループしないようにする。
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

            _root.Add(Header("変形 (Deform)"));

            var help = new HelpBox(
                "「作業軸」パネルで設定した軸を基準に変形します。\n" +
                "軸ローカルの +Y がライン方向、+X がたわみ方向です。",
                HelpBoxMessageType.Info);
            help.style.color = new StyleColor(Color.white);
            help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(help);

            // ── デフォーマ選択 ────────────────────────────────────────
            var labels = BuildDeformerLists();
            _deformerDropdown = new DropdownField("種類", labels, labels.Count > 0 ? 0 : -1);
            _deformerDropdown.style.color = new StyleColor(Color.white);
            _deformerDropdown.style.marginTop = 4;
            _deformerDropdown.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;

                // 表示名は重複し得るので index で引く。
                int idx = _deformerDropdown.index;
                if (idx < 0 || idx >= _deformerIds.Count) return;

                GetH?.Invoke()?.SelectDeformer(_deformerIds[idx]);
                UpdateGroupVisibility();
                Refresh();
            });
            _root.Add(_deformerDropdown);

            // ── フェーズ切替 ──────────────────────────────────────────
            // 「軸を決める」→「変形を掛ける」の2段。どちらを選んでいるかを
            // 色で示し、下の本体を丸ごと入れ替える。
            var phaseRow = new VisualElement();
            phaseRow.style.flexDirection = FlexDirection.Row;
            phaseRow.style.marginTop     = 6;
            phaseRow.style.marginBottom  = 4;

            _phaseAxisBtn = MakePhaseButton(
                "作業軸設定", () => SetPhase(DeformToolHandler.DeformPhase.WorkAxis));
            _phaseDeformBtn = MakePhaseButton(
                "変形開始", () => SetPhase(DeformToolHandler.DeformPhase.Deform));

            phaseRow.Add(_phaseAxisBtn);
            phaseRow.Add(_phaseDeformBtn);
            _root.Add(phaseRow);

            // ── 作業軸フェーズの本体 ──────────────────────────────────
            // 見出しと説明は上で出しているので、埋め込みぶんは省く。
            _workAxisBody = new VisualElement();
            _root.Add(_workAxisBody);
            if (WorkAxisPanel != null)
            {
                WorkAxisPanel.ShowHeader = false;
                WorkAxisPanel.Build(_workAxisBody);
            }

            // ── 変形フェーズの本体 ────────────────────────────────────
            _deformBody = new VisualElement();
            _root.Add(_deformBody);

            // ── 形状プレビュー ────────────────────────────────────────
            _shapePreviewToggle = new Toggle("軸の形状プレビュー") { value = true };
            _shapePreviewToggle.style.color = new StyleColor(Color.white);
            _shapePreviewToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                var h = GetH?.Invoke(); if (h == null) return;
                h.ShowShapePreview = e.newValue;
                // ギズモを組み直させる。頂点は動かさない。
                h.RequestGizmoRefresh();
            });
            _deformBody.Add(_shapePreviewToggle);

            BuildRotateGroup();
            BuildMoveGroup();
            BuildScaleGroup();
            BuildBendGroup();
            BuildTwistGroup();
            BuildMagnetGroup();

            // ── 確定 / 取消 ───────────────────────────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 6;
            // 確定はコマンド経由。CommitViaCommand が開始位置へ戻して
            // ApplyDeformCommand を送り、その受け口が変形と Undo 記録を行う。
            var applyBtn = new Button(() => { GetH?.Invoke()?.CommitViaCommand(); Refresh(); }) { text = "適用" };
            applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            var revertBtn = new Button(() => { GetH?.Invoke()?.Revert(); ResetWidgets(); Refresh(); }) { text = "取消" };
            revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn);
            _deformBody.Add(btnRow);
            _applyBtn  = applyBtn;
            _revertBtn = revertBtn;

            _infoLabel = new Label();
            _infoLabel.style.fontSize  = 10;
            _infoLabel.style.marginTop = 4;
            _infoLabel.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _deformBody.Add(_infoLabel);

            UpdateGroupVisibility();
            RefreshPhaseButtons();
        }

        private Button MakePhaseButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow    = 1;
            b.style.height      = 24;
            b.style.marginRight = 2;
            b.style.color       = new StyleColor(Color.white);
            return b;
        }

        /// <summary>
        /// 非表示指定を除いたデフォーマ一覧を作り、_deformerIds を埋める。
        /// 戻り値はドロップダウンへ出す表示名で、_deformerIds と同じ並び。
        /// </summary>
        private List<string> BuildDeformerLists()
        {
            _deformerIds.Clear();

            var ids    = DeformerRegistry.GetNames();
            var labels = DeformerRegistry.GetDisplayNames();
            var shown  = new List<string>(ids.Count);

            for (int i = 0; i < ids.Count && i < labels.Count; i++)
            {
                if (System.Array.IndexOf(HiddenDeformerNames, ids[i]) >= 0) continue;
                _deformerIds.Add(ids[i]);
                shown.Add(labels[i]);
            }
            return shown;
        }

        // ================================================================
        // フェーズ
        // ================================================================

        private void SetPhase(DeformToolHandler.DeformPhase phase)
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            h.Phase = phase;
            RefreshPhaseButtons();
        }

        /// <summary>
        /// フェーズボタンの色と文言、および本体の出し分けを更新する。
        /// 「変形開始」側の文言はデフォーマの表示名から作る（例「曲げ開始」）。
        /// </summary>
        private void RefreshPhaseButtons()
        {
            var h = GetH?.Invoke();
            bool axisPhase = h == null || h.Phase == DeformToolHandler.DeformPhase.WorkAxis;

            if (_phaseDeformBtn != null)
            {
                string name = h?.Deformer?.DisplayName;
                _phaseDeformBtn.text = string.IsNullOrEmpty(name) ? "変形開始" : name + "開始";
            }

            if (_phaseAxisBtn != null)
                _phaseAxisBtn.style.backgroundColor =
                    new StyleColor(axisPhase ? ActiveBtnColor : InactiveBtnColor);
            if (_phaseDeformBtn != null)
                _phaseDeformBtn.style.backgroundColor =
                    new StyleColor(axisPhase ? InactiveBtnColor : ActiveBtnColor);

            if (_workAxisBody != null)
                _workAxisBody.style.display = axisPhase ? DisplayStyle.Flex : DisplayStyle.None;
            if (_deformBody != null)
                _deformBody.style.display   = axisPhase ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ================================================================
        // Rotate グループ
        // ================================================================

        private void BuildRotateGroup()
        {
            _rotateGroup = new VisualElement();
            _rotateGroup.Add(Header("回転角（度）"));

            MakeSliderRow(_rotateGroup, "X", -180f, 180f, out _rotSliderX, out _rotFieldX,
                v => WithRotate(p => p.AngleX = v));
            MakeSliderRow(_rotateGroup, "Y", -180f, 180f, out _rotSliderY, out _rotFieldY,
                v => WithRotate(p => p.AngleY = v));
            MakeSliderRow(_rotateGroup, "Z", -180f, 180f, out _rotSliderZ, out _rotFieldZ,
                v => WithRotate(p => p.AngleZ = v));

            _deformBody.Add(_rotateGroup);
        }

        private void WithRotate(Action<RotateDeformerParams> set)
        {
            var h = GetH?.Invoke();
            if (h?.Deformer?.Params is RotateDeformerParams p)
            {
                set(p);
                h.ApplyPreview();
                RefreshInfo();
            }
        }

        // ================================================================
        // Move グループ
        // ================================================================

        private void BuildMoveGroup()
        {
            _moveGroup = new VisualElement();
            _moveGroup.Add(Header("移動量（作業軸ローカル）"));

            MakeSliderRow(_moveGroup, "X", -10f, 10f, out _movSliderX, out _movFieldX,
                v => WithMove(p => p.OffsetX = v));
            MakeSliderRow(_moveGroup, "Y", -10f, 10f, out _movSliderY, out _movFieldY,
                v => WithMove(p => p.OffsetY = v));
            MakeSliderRow(_moveGroup, "Z", -10f, 10f, out _movSliderZ, out _movFieldZ,
                v => WithMove(p => p.OffsetZ = v));

            _deformBody.Add(_moveGroup);
        }

        private void WithMove(Action<MoveDeformerParams> set)
        {
            var h = GetH?.Invoke();
            if (h?.Deformer?.Params is MoveDeformerParams p)
            {
                set(p);
                h.ApplyPreview();
                RefreshInfo();
            }
        }

        // ================================================================
        // Scale グループ
        // ================================================================

        private void BuildScaleGroup()
        {
            _scaleGroup = new VisualElement();
            _scaleGroup.Add(Header("倍率（作業軸ローカル）"));

            // 下限のクランプは ScaleDeformerParams 側に集約されている。
            MakeSliderRow(_scaleGroup, "X", 0.01f, 5f, out _sclSliderX, out _sclFieldX,
                v => WithScale(p => p.ScaleX = v));
            MakeSliderRow(_scaleGroup, "Y", 0.01f, 5f, out _sclSliderY, out _sclFieldY,
                v => WithScale(p => p.ScaleY = v));
            MakeSliderRow(_scaleGroup, "Z", 0.01f, 5f, out _sclSliderZ, out _sclFieldZ,
                v => WithScale(p => p.ScaleZ = v));

            _deformBody.Add(_scaleGroup);
        }

        private void WithScale(Action<ScaleDeformerParams> set)
        {
            var h = GetH?.Invoke();
            if (h?.Deformer?.Params is ScaleDeformerParams p)
            {
                set(p);
                h.ApplyPreview();
                RefreshInfo();
            }
        }

        // ================================================================
        // Bend グループ
        // ================================================================

        private void BuildBendGroup()
        {
            _bendGroup = new VisualElement();
            _bendGroup.Add(Header("曲げ"));

            MakeSliderRow(_bendGroup, "曲げ角度", -360f, 360f, out _bendAngleSlider, out _bendAngleField,
                v => WithBend(p => p.TotalAngleDeg = v));
            // カメラ奥行軸モード。ON のあいだ「まげ方向」は自動計算になるので
            // 入力を止め、算出値を表示するだけにする。
            _bendCameraPlaneToggle = new Toggle("カメラ奥行軸で曲げる") { value = true };
            _bendCameraPlaneToggle.style.color = new StyleColor(Color.white);
            _bendCameraPlaneToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithBend(p => p.UseCameraBendPlane = e.newValue);
                RefreshBendPlaneEnabled();
            });
            _bendGroup.Add(_bendCameraPlaneToggle);

            MakeSliderRow(_bendGroup, "まげ方向", -180f, 180f, out _bendPlaneSlider, out _bendPlaneField,
                v => WithBend(p => p.BendPlaneAngleDeg = v));

            _bendPivotToggle = new Toggle("作業軸の原点を起点にする") { value = false };
            _bendPivotToggle.style.color = new StyleColor(Color.white);
            _bendPivotToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithBend(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _bendGroup.Add(_bendPivotToggle);

            _deformBody.Add(_bendGroup);
        }

        private void WithBend(Action<BendDeformerParams> set)
        {
            var h = GetH?.Invoke();
            if (h?.Deformer?.Params is BendDeformerParams p)
            {
                set(p);
                h.ApplyPreview();
                RefreshInfo();
            }
        }

        /// <summary>
        /// カメラ奥行軸モードのとき「まげ方向」の入力を止める。
        /// 値そのものは Refresh で書き戻すので、算出結果は表示され続ける。
        /// </summary>
        private void RefreshBendPlaneEnabled()
        {
            bool manual = !(GetH?.Invoke()?.Deformer?.Params is BendDeformerParams bp)
                          || !bp.UseCameraBendPlane;

            _bendPlaneSlider?.SetEnabled(manual);
            _bendPlaneField ?.SetEnabled(manual);
        }

        // ================================================================
        // Twist グループ
        // ================================================================

        private void BuildTwistGroup()
        {
            _twistGroup = new VisualElement();
            _twistGroup.Add(Header("ねじり"));

            // 1回転を超えるねじりも実用上あるので範囲は広めに取る。
            MakeSliderRow(_twistGroup, "ねじり角度", -720f, 720f, out _twistAngleSlider, out _twistAngleField,
                v => WithTwist(p => p.TotalAngleDeg = v));

            _twistPivotToggle = new Toggle("作業軸の原点を起点にする") { value = false };
            _twistPivotToggle.style.color = new StyleColor(Color.white);
            _twistPivotToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithTwist(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _twistGroup.Add(_twistPivotToggle);

            _deformBody.Add(_twistGroup);
        }

        private void WithTwist(Action<TwistDeformerParams> set)
        {
            var h = GetH?.Invoke();
            if (h?.Deformer?.Params is TwistDeformerParams p)
            {
                set(p);
                h.ApplyPreview();
                RefreshInfo();
            }
        }

        // ================================================================
        // Magnet グループ
        // ================================================================

        private void BuildMagnetGroup()
        {
            _deformBody.Add(Header("マグネット（比例編集）"));

            _magnetToggle = new Toggle("有効") { value = false };
            _magnetToggle.style.color = new StyleColor(Color.white);
            _magnetToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                var h = GetH?.Invoke(); if (h == null) return;
                // 影響頂点の集合が変わるため、プレビューを張り直す。
                h.Revert();
                h.UseMagnet = e.newValue;
                h.ApplyPreview();
                RefreshInfo();
            });
            _deformBody.Add(_magnetToggle);

            _magnetRadius = new Slider("半径", 0.01f, 1f) { value = 0.5f };
            _magnetRadius.style.marginBottom = 3;
            _magnetRadius.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                var h = GetH?.Invoke(); if (h == null) return;
                h.Revert();
                h.MagnetRadius = e.newValue;
                h.ApplyPreview();
                RefreshInfo();
            });
            _deformBody.Add(_magnetRadius);

            _magnetDistance = new EnumField("距離", DistanceMode.Euclidean);
            _magnetDistance.style.color = new StyleColor(Color.white);
            _magnetDistance.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                var h = GetH?.Invoke(); if (h == null) return;
                h.Revert();
                h.MagnetDistanceMode = (DistanceMode)e.newValue;
                h.ApplyPreview();
            });
            _deformBody.Add(_magnetDistance);

            _magnetFalloff = new EnumField("減衰", FalloffType.Smooth);
            _magnetFalloff.style.color = new StyleColor(Color.white);
            _magnetFalloff.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                var h = GetH?.Invoke(); if (h == null) return;
                h.Revert();
                h.MagnetFalloff = (FalloffType)e.newValue;
                h.ApplyPreview();
            });
            _deformBody.Add(_magnetFalloff);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            // 埋め込んだ作業軸パネルも一緒に更新する。
            WorkAxisPanel?.Refresh();

            var h = GetH?.Invoke();
            if (h == null) return;

            _suppressCallback = true;
            try
            {
                // 内部 ID から index を引いて、表示名を書き戻す。
                if (_deformerDropdown != null && !string.IsNullOrEmpty(h.DeformerName))
                {
                    int idx = _deformerIds.IndexOf(h.DeformerName);
                    if (idx >= 0) _deformerDropdown.index = idx;
                }

                _shapePreviewToggle?.SetValueWithoutNotify(h.ShowShapePreview);
                RefreshPhaseButtons();

                if (h.Deformer?.Params is RotateDeformerParams rp)
                {
                    SetPair(_rotSliderX, _rotFieldX, rp.AngleX);
                    SetPair(_rotSliderY, _rotFieldY, rp.AngleY);
                    SetPair(_rotSliderZ, _rotFieldZ, rp.AngleZ);
                }
                else if (h.Deformer?.Params is MoveDeformerParams mp)
                {
                    SetPair(_movSliderX, _movFieldX, mp.OffsetX);
                    SetPair(_movSliderY, _movFieldY, mp.OffsetY);
                    SetPair(_movSliderZ, _movFieldZ, mp.OffsetZ);
                }
                else if (h.Deformer?.Params is ScaleDeformerParams sp)
                {
                    SetPair(_sclSliderX, _sclFieldX, sp.ScaleX);
                    SetPair(_sclSliderY, _sclFieldY, sp.ScaleY);
                    SetPair(_sclSliderZ, _sclFieldZ, sp.ScaleZ);
                }
                else if (h.Deformer?.Params is BendDeformerParams bp)
                {
                    SetPair(_bendAngleSlider, _bendAngleField, bp.TotalAngleDeg);
                    SetPair(_bendPlaneSlider, _bendPlaneField, bp.BendPlaneAngleDeg);
                    _bendCameraPlaneToggle?.SetValueWithoutNotify(bp.UseCameraBendPlane);
                    _bendPivotToggle?.SetValueWithoutNotify(bp.PivotAtAxisOrigin);
                    RefreshBendPlaneEnabled();
                }
                else if (h.Deformer?.Params is TwistDeformerParams tp)
                {
                    SetPair(_twistAngleSlider, _twistAngleField, tp.TotalAngleDeg);
                    _twistPivotToggle?.SetValueWithoutNotify(tp.PivotAtAxisOrigin);
                }

                _magnetToggle?.SetValueWithoutNotify(h.UseMagnet);
                _magnetRadius?.SetValueWithoutNotify(h.MagnetRadius);
                _magnetFalloff?.SetValueWithoutNotify(h.MagnetFalloff);
                _magnetDistance?.SetValueWithoutNotify(h.MagnetDistanceMode);
            }
            finally { _suppressCallback = false; }

            UpdateGroupVisibility();
            RefreshInfo();
        }

        private void RefreshInfo()
        {
            if (_infoLabel == null) return;

            var h = GetH?.Invoke();
            if (h == null) { _infoLabel.text = string.Empty; return; }

            if (!h.IsPreviewing)
            {
                _infoLabel.text = "対象なし（頂点を選択してスライダを動かしてください）";
                return;
            }

            var c = h.PreviewContext;
            _infoLabel.text =
                $"対象 {h.AffectedCount} 頂点 / 軸ローカル s = {c.SMin:F3} 〜 {c.SMax:F3}"
                + (c.HasRange ? string.Empty : "（範囲なし。曲げ・ねじりは効きません）");
        }

        private void UpdateGroupVisibility()
        {
            var p = GetH?.Invoke()?.Deformer?.Params;
            bool isRotate = p is RotateDeformerParams;
            bool isMove   = p is MoveDeformerParams;
            bool isScale  = p is ScaleDeformerParams;
            bool isBend   = p is BendDeformerParams;
            bool isTwist  = p is TwistDeformerParams;

            if (_rotateGroup != null)
                _rotateGroup.style.display = isRotate ? DisplayStyle.Flex : DisplayStyle.None;
            if (_moveGroup != null)
                _moveGroup.style.display   = isMove   ? DisplayStyle.Flex : DisplayStyle.None;
            if (_scaleGroup != null)
                _scaleGroup.style.display  = isScale  ? DisplayStyle.Flex : DisplayStyle.None;
            if (_bendGroup != null)
                _bendGroup.style.display   = isBend   ? DisplayStyle.Flex : DisplayStyle.None;
            if (_twistGroup != null)
                _twistGroup.style.display  = isTwist  ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>取消時にウィジェットを 0 へ戻す。</summary>
        private void ResetWidgets()
        {
            GetH?.Invoke()?.ResetParams();
        }

        // ================================================================
        // ウィジェットヘルパー
        // ================================================================

        /// <summary>
        /// スライダと数値フィールドを1行に並べ、両方から同じ値を書き込む。
        /// 片方を動かしたらもう片方へ Notify なしで書き戻す。
        /// </summary>
        private void MakeSliderRow(
            VisualElement parent, string label, float min, float max,
            out Slider slider, out FloatField field, Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;

            var s = new Slider(label, min, max) { value = 0f };
            s.style.flexGrow = 1;

            var f = new FloatField { value = 0f };
            f.style.width      = 60;
            f.style.marginLeft = 4;

            s.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                _suppressCallback = true;
                try { f.SetValueWithoutNotify(e.newValue); }
                finally { _suppressCallback = false; }
                onChange(e.newValue);
            });

            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                // スライダ範囲外の値も数値入力では許す。スライダは端で止める。
                _suppressCallback = true;
                try { s.SetValueWithoutNotify(Mathf.Clamp(e.newValue, min, max)); }
                finally { _suppressCallback = false; }
                onChange(e.newValue);
            });

            row.Add(s); row.Add(f);
            parent.Add(row);

            slider = s;
            field  = f;
        }

        private static void SetPair(Slider s, FloatField f, float v)
        {
            s?.SetValueWithoutNotify(v);
            f?.SetValueWithoutNotify(v);
        }

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
