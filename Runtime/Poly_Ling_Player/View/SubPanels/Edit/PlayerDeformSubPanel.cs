// PlayerDeformSubPanel.cs
// デフォーマ（回転 / 曲げ）のサブパネル。数値入力とスライダのみで操作する。
//
// デフォーマは DeformerRegistry から取得し、パラメータ UI は選択中の型で
// 出し分ける。新しいデフォーマを足したときは BuildParamGroups と
// RefreshParamGroups に1ブロック足せばよい。
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

        private VisualElement _root;
        private DropdownField _deformerDropdown;
        private Label         _infoLabel;

        // Rotate
        private VisualElement _rotateGroup;
        private Slider        _rotSliderX, _rotSliderY, _rotSliderZ;
        private FloatField    _rotFieldX,  _rotFieldY,  _rotFieldZ;

        // Bend
        private VisualElement _bendGroup;
        private Slider        _bendAngleSlider, _bendPlaneSlider;
        private FloatField    _bendAngleField,  _bendPlaneField;
        private Toggle        _bendPivotToggle;

        // Twist
        private VisualElement _twistGroup;
        private Slider        _twistAngleSlider;
        private FloatField    _twistAngleField;
        private Toggle        _twistPivotToggle;

        // Magnet
        private Toggle    _magnetToggle;
        private Slider    _magnetRadius;
        private EnumField _magnetFalloff, _magnetDistance;

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
            var names = DeformerRegistry.GetNames();
            _deformerDropdown = new DropdownField("種類", names, names.Count > 0 ? 0 : -1);
            _deformerDropdown.style.color = new StyleColor(Color.white);
            _deformerDropdown.style.marginTop = 4;
            _deformerDropdown.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                GetH?.Invoke()?.SelectDeformer(e.newValue);
                UpdateGroupVisibility();
                Refresh();
            });
            _root.Add(_deformerDropdown);

            BuildRotateGroup();
            BuildBendGroup();
            BuildTwistGroup();
            BuildMagnetGroup();

            // ── 確定 / 取消 ───────────────────────────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 6;
            var applyBtn = new Button(() => { GetH?.Invoke()?.Commit(); Refresh(); }) { text = "適用" };
            applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            var revertBtn = new Button(() => { GetH?.Invoke()?.Revert(); ResetWidgets(); Refresh(); }) { text = "取消" };
            revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn);
            _root.Add(btnRow);

            _infoLabel = new Label();
            _infoLabel.style.fontSize  = 10;
            _infoLabel.style.marginTop = 4;
            _infoLabel.style.color     = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(_infoLabel);

            UpdateGroupVisibility();
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

            _root.Add(_rotateGroup);
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
        // Bend グループ
        // ================================================================

        private void BuildBendGroup()
        {
            _bendGroup = new VisualElement();
            _bendGroup.Add(Header("曲げ"));

            MakeSliderRow(_bendGroup, "合計角", -360f, 360f, out _bendAngleSlider, out _bendAngleField,
                v => WithBend(p => p.TotalAngleDeg = v));
            MakeSliderRow(_bendGroup, "たわみ方向", -180f, 180f, out _bendPlaneSlider, out _bendPlaneField,
                v => WithBend(p => p.BendPlaneAngleDeg = v));

            _bendPivotToggle = new Toggle("作業軸の原点を起点にする") { value = false };
            _bendPivotToggle.style.color = new StyleColor(Color.white);
            _bendPivotToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithBend(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _bendGroup.Add(_bendPivotToggle);

            _root.Add(_bendGroup);
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

        // ================================================================
        // Twist グループ
        // ================================================================

        private void BuildTwistGroup()
        {
            _twistGroup = new VisualElement();
            _twistGroup.Add(Header("ねじり"));

            // 1回転を超えるねじりも実用上あるので範囲は広めに取る。
            MakeSliderRow(_twistGroup, "合計角", -720f, 720f, out _twistAngleSlider, out _twistAngleField,
                v => WithTwist(p => p.TotalAngleDeg = v));

            _twistPivotToggle = new Toggle("作業軸の原点を起点にする") { value = false };
            _twistPivotToggle.style.color = new StyleColor(Color.white);
            _twistPivotToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressCallback) return;
                WithTwist(p => p.PivotAtAxisOrigin = e.newValue);
            });
            _twistGroup.Add(_twistPivotToggle);

            _root.Add(_twistGroup);
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
            _root.Add(Header("マグネット（比例編集）"));

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
            _root.Add(_magnetToggle);

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
            _root.Add(_magnetRadius);

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
            _root.Add(_magnetDistance);

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
            _root.Add(_magnetFalloff);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            _suppressCallback = true;
            try
            {
                if (_deformerDropdown != null && !string.IsNullOrEmpty(h.DeformerName))
                    _deformerDropdown.SetValueWithoutNotify(h.DeformerName);

                if (h.Deformer?.Params is RotateDeformerParams rp)
                {
                    SetPair(_rotSliderX, _rotFieldX, rp.AngleX);
                    SetPair(_rotSliderY, _rotFieldY, rp.AngleY);
                    SetPair(_rotSliderZ, _rotFieldZ, rp.AngleZ);
                }
                else if (h.Deformer?.Params is BendDeformerParams bp)
                {
                    SetPair(_bendAngleSlider, _bendAngleField, bp.TotalAngleDeg);
                    SetPair(_bendPlaneSlider, _bendPlaneField, bp.BendPlaneAngleDeg);
                    _bendPivotToggle?.SetValueWithoutNotify(bp.PivotAtAxisOrigin);
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
            bool isBend   = p is BendDeformerParams;
            bool isTwist  = p is TwistDeformerParams;

            if (_rotateGroup != null)
                _rotateGroup.style.display = isRotate ? DisplayStyle.Flex : DisplayStyle.None;
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
            f.style.color      = new StyleColor(Color.black);

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
