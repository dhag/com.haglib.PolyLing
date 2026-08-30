// PlayerCameraSubPanel.cs
// カメラ調整パネル（UIToolkit・右ペイン）。
// メインカメラ（左上 Perspective）と3面カメラ（Top/Front/Side）を
// スライダ / 数値フィールドで調整する。ビューポート側のギズモ操作
// （CameraToolHandler）と同じ値を読み書きする。
//
// 3面カメラは Target / リグ回転 / ズーム / 画角 / 投影方式を共有し、
// 軸の相対関係を固定したまま3台が連動する。フリップのみビューごと。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Camera/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerCameraSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<CameraToolHandler>     GetH;
        public Func<OrbitCameraController> GetOrbit;
        public Func<OrthoViewController>   GetTri;

        /// <summary>3面のフリップ状態。index: 0=Top / 1=Front / 2=Side。</summary>
        public Func<int, bool>   GetTriFlip;
        public Action<int, bool> SetTriFlip;

        /// <summary>メインカメラの正投影切替（ビューポートヘッダのトグルとも同期する）。</summary>
        public Action<bool> SetMainOrthographic;

        /// <summary>メインカメラの視線反転（Target を挟んで反対側へ回り込む）。</summary>
        public Action FlipMainView;

        /// <summary>メインカメラの値を変更した後の再描画要求。</summary>
        public Action OnMainChanged;

        /// <summary>3面カメラの値を変更した後の再描画要求。</summary>
        public Action OnTriChanged;

        /// <summary>ギズモの再描画要求（操作対象の切替時）。</summary>
        public Action OnGizmoChanged;

        // ================================================================
        // UI 要素
        // ================================================================

        private DropdownField _targetDropdown;
        private DropdownField _gizmoOpDropdown;

        private VisualElement _mainGroup;
        private VisualElement _triGroup;

        private Slider _mainTargetXS, _mainTargetYS, _mainTargetZS;
        private FloatField _mainTargetXF, _mainTargetYF, _mainTargetZF;
        private Slider _mainRotXS, _mainRotYS, _mainRotZS;
        private FloatField _mainRotXF, _mainRotYF, _mainRotZF;
        private Slider _mainDistS, _mainFovS;
        private FloatField _mainDistF, _mainFovF;
        private Toggle _mainOrthoToggle;

        private Slider _triTargetXS, _triTargetYS, _triTargetZS;
        private FloatField _triTargetXF, _triTargetYF, _triTargetZF;
        private Slider _triRotXS, _triRotYS, _triRotZS;
        private FloatField _triRotXF, _triRotYF, _triRotZF;
        private Slider _triZoomS, _triFovS;
        private FloatField _triZoomF, _triFovF;
        private Toggle _triPerspToggle;
        private Toggle _triFlipTop, _triFlipFront, _triFlipSide;

        private bool _suppress;

        private static readonly List<string> TargetNames = new List<string>
        {
            "メインカメラ（左上）", "３面カメラ（TOP/Front/Side）",
        };

        private static readonly List<string> GizmoOpNames = new List<string>
        {
            "カメラ（姿勢・位置）", "注視点",
        };

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();

            parent.Add(PlayerIoUiKit.Title("カメラ調整"));

            var note = new Label(
                "ギズモは調整対象以外の画面に出ます（メイン→3面、3面→メイン）。" +
                "注視点は4画面すべてから動かせます。");
            note.style.fontSize     = 10;
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            parent.Add(note);

            _targetDropdown = new DropdownField("調整対象", TargetNames, 0);
            _targetDropdown.style.marginBottom = 2;
            _targetDropdown.RegisterValueChangedCallback(_ => OnTargetKindChanged());
            parent.Add(_targetDropdown);

            _gizmoOpDropdown = new DropdownField("ギズモ操作", GizmoOpNames, 0);
            _gizmoOpDropdown.style.marginBottom = 6;
            _gizmoOpDropdown.RegisterValueChangedCallback(_ => OnGizmoOpChanged());
            parent.Add(_gizmoOpDropdown);

            BuildMainGroup(parent);
            BuildTriGroup(parent);

            Refresh();
        }

        // ================================================================
        // メインカメラ
        // ================================================================

        private void BuildMainGroup(VisualElement parent)
        {
            _mainGroup = new VisualElement();
            parent.Add(_mainGroup);

            _mainGroup.Add(PlayerIoUiKit.SectionLabel("注視点（ワールド）"));
            MakeSliderRow(_mainGroup, "X", -10f, 10f, out _mainTargetXS, out _mainTargetXF, v => SetMainTarget(0, v));
            MakeSliderRow(_mainGroup, "Y", -10f, 10f, out _mainTargetYS, out _mainTargetYF, v => SetMainTarget(1, v));
            MakeSliderRow(_mainGroup, "Z", -10f, 10f, out _mainTargetZS, out _mainTargetZF, v => SetMainTarget(2, v));

            _mainGroup.Add(PlayerIoUiKit.SectionLabel("姿勢（度）"));
            MakeSliderRow(_mainGroup, "X（仰角）", -89f, 89f, out _mainRotXS, out _mainRotXF, v =>
            {
                var o = GetOrbit?.Invoke(); if (o == null) return;
                o.RotX = v; NotifyMain();
            });
            MakeSliderRow(_mainGroup, "Y（方位）", -180f, 180f, out _mainRotYS, out _mainRotYF, v =>
            {
                var o = GetOrbit?.Invoke(); if (o == null) return;
                o.RotY = v; NotifyMain();
            });
            MakeSliderRow(_mainGroup, "Z（ロール）", -180f, 180f, out _mainRotZS, out _mainRotZF, v =>
            {
                var o = GetOrbit?.Invoke(); if (o == null) return;
                o.RotZ = v; NotifyMain();
            });

            _mainGroup.Add(PlayerIoUiKit.SectionLabel("投影"));
            MakeSliderRow(_mainGroup, "距離", 0.05f, 20f, out _mainDistS, out _mainDistF, v =>
            {
                var o = GetOrbit?.Invoke(); if (o == null) return;
                o.Distance = v; NotifyMain();
            });
            MakeSliderRow(_mainGroup, "画角", 5f, 120f, out _mainFovS, out _mainFovF, v =>
            {
                var o = GetOrbit?.Invoke(); if (o == null) return;
                o.Fov = Mathf.Clamp(v, 1f, 179f); NotifyMain();
            });

            _mainOrthoToggle = new Toggle("オルソ表示") { value = false };
            _mainOrthoToggle.style.marginBottom = 2;
            _mainOrthoToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                SetMainOrthographic?.Invoke(e.newValue);
            });
            _mainGroup.Add(_mainOrthoToggle);

            var flipBtn = new Button(() => FlipMainView?.Invoke()) { text = "視線を反転（反対側へ回り込む）" };
            flipBtn.style.marginTop = 4;
            flipBtn.style.height    = 24;
            _mainGroup.Add(flipBtn);
        }

        private void SetMainTarget(int axis, float v)
        {
            var o = GetOrbit?.Invoke();
            if (o == null) return;
            var t = o.Target;
            if      (axis == 0) t.x = v;
            else if (axis == 1) t.y = v;
            else                t.z = v;
            o.Target = t;
            NotifyMain();
        }

        // ================================================================
        // 3面カメラ
        // ================================================================

        private void BuildTriGroup(VisualElement parent)
        {
            _triGroup = new VisualElement();
            parent.Add(_triGroup);

            var triNote = new Label("Target / 姿勢 / ズーム / 画角 / 投影は3台共通です。");
            triNote.style.fontSize     = 10;
            triNote.style.whiteSpace   = WhiteSpace.Normal;
            triNote.style.marginBottom = 4;
            _triGroup.Add(triNote);

            _triGroup.Add(PlayerIoUiKit.SectionLabel("注視点（ワールド）"));
            MakeSliderRow(_triGroup, "X", -10f, 10f, out _triTargetXS, out _triTargetXF, v => SetTriTarget(0, v));
            MakeSliderRow(_triGroup, "Y", -10f, 10f, out _triTargetYS, out _triTargetYF, v => SetTriTarget(1, v));
            MakeSliderRow(_triGroup, "Z", -10f, 10f, out _triTargetZS, out _triTargetZF, v => SetTriTarget(2, v));

            _triGroup.Add(PlayerIoUiKit.SectionLabel("姿勢（リグ回転・度）"));
            MakeSliderRow(_triGroup, "X", -180f, 180f, out _triRotXS, out _triRotXF, v => SetTriRot(0, v));
            MakeSliderRow(_triGroup, "Y", -180f, 180f, out _triRotYS, out _triRotYF, v => SetTriRot(1, v));
            MakeSliderRow(_triGroup, "Z", -180f, 180f, out _triRotZS, out _triRotZF, v => SetTriRot(2, v));

            _triGroup.Add(PlayerIoUiKit.SectionLabel("投影"));
            MakeSliderRow(_triGroup, "ズーム", 0.0002f, 0.05f, out _triZoomS, out _triZoomF, v =>
            {
                var t = GetTri?.Invoke(); if (t == null) return;
                t.WorldHeightPerPixel = v; NotifyTri();
            });
            MakeSliderRow(_triGroup, "画角", 5f, 120f, out _triFovS, out _triFovF, v =>
            {
                var t = GetTri?.Invoke(); if (t == null) return;
                t.Fov = v; NotifyTri();
            });

            _triPerspToggle = new Toggle("パース表示（3台連動）") { value = false };
            _triPerspToggle.style.marginBottom = 2;
            _triPerspToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                var t = GetTri?.Invoke(); if (t == null) return;
                t.Perspective = e.newValue;
                NotifyTri();
            });
            _triGroup.Add(_triPerspToggle);

            _triGroup.Add(PlayerIoUiKit.SectionLabel("フリップ"));
            _triFlipTop   = MakeFlipToggle("TOP → BOTTOM", 0);
            _triFlipFront = MakeFlipToggle("Front → Back", 1);
            _triFlipSide  = MakeFlipToggle("Right → Left", 2);
            _triGroup.Add(_triFlipTop);
            _triGroup.Add(_triFlipFront);
            _triGroup.Add(_triFlipSide);
        }

        private Toggle MakeFlipToggle(string label, int index)
        {
            var t = new Toggle(label) { value = false };
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                SetTriFlip?.Invoke(index, e.newValue);
            });
            return t;
        }

        private void SetTriTarget(int axis, float v)
        {
            var t = GetTri?.Invoke();
            if (t == null) return;
            var p = t.Target;
            if      (axis == 0) p.x = v;
            else if (axis == 1) p.y = v;
            else                p.z = v;
            t.Target = p;
            NotifyTri();
        }

        private void SetTriRot(int axis, float v)
        {
            var t = GetTri?.Invoke();
            if (t == null) return;
            var e = t.RigRotation.eulerAngles;
            if      (axis == 0) e.x = v;
            else if (axis == 1) e.y = v;
            else                e.z = v;
            t.RigRotation = Quaternion.Euler(e);
            NotifyTri();
        }

        // ================================================================
        // 操作対象の切替
        // ================================================================

        private void OnTargetKindChanged()
        {
            if (_suppress) return;
            var h = GetH?.Invoke();
            if (h != null)
            {
                h.TargetKind = _targetDropdown.index == 1
                    ? CameraToolHandler.CameraTargetKind.Tri
                    : CameraToolHandler.CameraTargetKind.Main;
            }
            Refresh();
            OnGizmoChanged?.Invoke();
        }

        private void OnGizmoOpChanged()
        {
            if (_suppress) return;
            var h = GetH?.Invoke();
            if (h != null)
            {
                h.GizmoOp = _gizmoOpDropdown.index == 1
                    ? CameraToolHandler.CameraGizmoOp.LookAt
                    : CameraToolHandler.CameraGizmoOp.Camera;
            }
            OnGizmoChanged?.Invoke();
        }

        // ================================================================
        // 同期
        // ================================================================

        /// <summary>現在のカメラ値をフィールドへ反映する。</summary>
        public void Refresh()
        {
            if (_targetDropdown == null) return;

            var h    = GetH?.Invoke();
            bool tri = h != null && h.TargetKind == CameraToolHandler.CameraTargetKind.Tri;

            _suppress = true;
            try
            {
                _targetDropdown.SetValueWithoutNotify(TargetNames[tri ? 1 : 0]);
                _gizmoOpDropdown.SetValueWithoutNotify(
                    GizmoOpNames[(h != null && h.GizmoOp == CameraToolHandler.CameraGizmoOp.LookAt) ? 1 : 0]);

                _mainGroup.style.display = tri ? DisplayStyle.None : DisplayStyle.Flex;
                _triGroup .style.display = tri ? DisplayStyle.Flex : DisplayStyle.None;

                var orbit = GetOrbit?.Invoke();
                if (orbit != null)
                {
                    SetPair(_mainTargetXS, _mainTargetXF, orbit.Target.x);
                    SetPair(_mainTargetYS, _mainTargetYF, orbit.Target.y);
                    SetPair(_mainTargetZS, _mainTargetZF, orbit.Target.z);
                    SetPair(_mainRotXS,    _mainRotXF,    orbit.RotX);
                    SetPair(_mainRotYS,    _mainRotYF,    orbit.RotY);
                    SetPair(_mainRotZS,    _mainRotZF,    orbit.RotZ);
                    SetPair(_mainDistS,    _mainDistF,    orbit.Distance);
                    SetPair(_mainFovS,     _mainFovF,     orbit.Fov);
                    _mainOrthoToggle.SetValueWithoutNotify(orbit.Orthographic);
                }

                var t = GetTri?.Invoke();
                if (t != null)
                {
                    var e = t.RigRotation.eulerAngles;
                    SetPair(_triTargetXS, _triTargetXF, t.Target.x);
                    SetPair(_triTargetYS, _triTargetYF, t.Target.y);
                    SetPair(_triTargetZS, _triTargetZF, t.Target.z);
                    SetPair(_triRotXS,    _triRotXF,    Normalize180(e.x));
                    SetPair(_triRotYS,    _triRotYF,    Normalize180(e.y));
                    SetPair(_triRotZS,    _triRotZF,    Normalize180(e.z));
                    SetPair(_triZoomS,    _triZoomF,    t.WorldHeightPerPixel);
                    SetPair(_triFovS,     _triFovF,     t.Fov);
                    _triPerspToggle.SetValueWithoutNotify(t.Perspective);
                }

                if (GetTriFlip != null)
                {
                    _triFlipTop  .SetValueWithoutNotify(GetTriFlip(0));
                    _triFlipFront.SetValueWithoutNotify(GetTriFlip(1));
                    _triFlipSide .SetValueWithoutNotify(GetTriFlip(2));
                }
            }
            finally
            {
                _suppress = false;
            }
        }

        private void NotifyMain()
        {
            if (_suppress) return;
            OnMainChanged?.Invoke();
        }

        private void NotifyTri()
        {
            if (_suppress) return;
            OnTriChanged?.Invoke();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>0..360 の角度を (-180, 180] へ直す。</summary>
        private static float Normalize180(float deg)
        {
            deg -= 360f * Mathf.Floor((deg + 180f) / 360f);
            return deg <= -180f ? deg + 360f : deg;
        }

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
                if (_suppress) return;
                _suppress = true;
                try { f.SetValueWithoutNotify(e.newValue); }
                finally { _suppress = false; }
                onChange(e.newValue);
            });

            f.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                // スライダ範囲外の値も数値入力では許す。スライダは端で止める。
                _suppress = true;
                try { s.SetValueWithoutNotify(Mathf.Clamp(e.newValue, min, max)); }
                finally { _suppress = false; }
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
    }
}
