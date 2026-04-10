// ObjectMoveTRSPanel.cs
// オブジェクト移動モード時に表示する TRS（回転・スケール）操作パネル。
// 選択中アイテム（SelectedBoneIndices + SelectedMeshIndices）の
// LocalRotation / LocalScale を SetBoneTransformValueCommand 経由で変更する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class ObjectMoveTRSPanel
    {
        // ================================================================
        // 依存
        // ================================================================

        private PanelContext           _ctx;
        private Func<ProjectContext>   _getProject;

        // ================================================================
        // UI
        // ================================================================

        private VisualElement _root;

        // 回転
        private FloatField _rotXField, _rotYField, _rotZField;
        private Slider     _rotXSlider, _rotYSlider, _rotZSlider;

        // スケール
        private FloatField _sclXField, _sclYField, _sclZField;

        private Toggle _ignorePoseToggle;
        private bool _isSyncing;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent, PanelContext ctx, Func<ProjectContext> getProject)
        {
            _ctx        = ctx;
            _getProject = getProject;

            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            AddSectionLabel("オブジェクト TRS");

            AddSep();
            AddSectionLabel("回転 (°)");

            AddRotRow("X", -180f, 180f,
                out _rotXSlider, out _rotXField,
                SetBoneTransformValueCommand.Field.RotationX);
            AddRotRow("Y", -180f, 180f,
                out _rotYSlider, out _rotYField,
                SetBoneTransformValueCommand.Field.RotationY);
            AddRotRow("Z", -180f, 180f,
                out _rotZSlider, out _rotZField,
                SetBoneTransformValueCommand.Field.RotationZ);

            AddSep();
            AddSectionLabel("スケール");

            AddScaleRow("X", out _sclXField, SetBoneTransformValueCommand.Field.ScaleX);
            AddScaleRow("Y", out _sclYField, SetBoneTransformValueCommand.Field.ScaleY);
            AddScaleRow("Z", out _sclZField, SetBoneTransformValueCommand.Field.ScaleZ);

            AddSep();
            _ignorePoseToggle = new Toggle("姿勢無視(アーマチャ)");
            _ignorePoseToggle.style.color = new StyleColor(Color.white);
            _ignorePoseToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncing) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    _ctx?.SendCommand(new SetIgnorePoseCommand(0, indices, e.newValue));
            });
            _root.Add(_ignorePoseToggle);
        }

        // ================================================================
        // 値同期（外部から呼ぶ）
        // ================================================================

        /// <summary>
        /// モデルの現在選択状態をUIに反映する。
        /// NotifyPanels(ChangeKind.Attributes / Selection) 後に呼ぶ。
        /// </summary>
        public void Refresh()
        {
            if (_root == null) return;
            var model = _getProject?.Invoke()?.CurrentModel;
            if (model == null) { ClearUI(); return; }

            // SelectedBoneIndices + SelectedMeshIndices の和集合
            var indices = model.SelectedBoneIndices
                .Concat(model.SelectedMeshIndices.Where(i => !model.SelectedBoneIndices.Contains(i)))
                .Where(i => i >= 0 && i < model.MeshContextCount)
                .ToList();

            if (indices.Count == 0) { ClearUI(); return; }

            _isSyncing = true;
            try
            {
                // 単一選択 or 全同値のとき値を表示、複数異なる値は空欄
                void SyncF(FloatField f, Func<int, float> get)
                {
                    if (f == null) return;
                    float first = get(indices[0]);
                    bool same = indices.All(i => Mathf.Approximately(get(i), first));
                    f.SetValueWithoutNotify(same ? first : 0f);
                    f.showMixedValue = !same;
                }
                void SyncS(Slider s, FloatField f, Func<int, float> get)
                {
                    if (s == null || f == null) return;
                    float first = get(indices[0]);
                    bool same = indices.All(i => Mathf.Approximately(get(i), first));
                    float disp = same ? NormAngle(first) : 0f;
                    s.SetValueWithoutNotify(disp);
                    f.SetValueWithoutNotify(same ? first : 0f);
                    f.showMixedValue = !same;
                }

                var ctx0 = model.GetMeshContext(indices[0]);
                SyncS(_rotXSlider, _rotXField, i => model.GetMeshContext(i)?.BoneTransform?.Rotation.x ?? 0f);
                SyncS(_rotYSlider, _rotYField, i => model.GetMeshContext(i)?.BoneTransform?.Rotation.y ?? 0f);
                SyncS(_rotZSlider, _rotZField, i => model.GetMeshContext(i)?.BoneTransform?.Rotation.z ?? 0f);
                SyncF(_sclXField, i => model.GetMeshContext(i)?.BoneTransform?.Scale.x ?? 1f);
                SyncF(_sclYField, i => model.GetMeshContext(i)?.BoneTransform?.Scale.y ?? 1f);
                SyncF(_sclZField, i => model.GetMeshContext(i)?.BoneTransform?.Scale.z ?? 1f);

                // IgnorePoseInArmature 同期
                if (_ignorePoseToggle != null)
                {
                    bool first = model.GetMeshContext(indices[0])?.IgnorePoseInArmature ?? false;
                    bool same  = indices.All(i => (model.GetMeshContext(i)?.IgnorePoseInArmature ?? false) == first);
                    _ignorePoseToggle.SetValueWithoutNotify(same && first);
                }
            }
            finally { _isSyncing = false; }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void ClearUI()
        {
            _isSyncing = true;
            try
            {
                _rotXSlider?.SetValueWithoutNotify(0f); _rotXField?.SetValueWithoutNotify(0f);
                _rotYSlider?.SetValueWithoutNotify(0f); _rotYField?.SetValueWithoutNotify(0f);
                _rotZSlider?.SetValueWithoutNotify(0f); _rotZField?.SetValueWithoutNotify(0f);
                _sclXField?.SetValueWithoutNotify(1f);
                _sclYField?.SetValueWithoutNotify(1f);
                _sclZField?.SetValueWithoutNotify(1f);
            }
            finally { _isSyncing = false; }
        }

        private int[] SelIndices()
        {
            var model = _getProject?.Invoke()?.CurrentModel;
            if (model == null) return Array.Empty<int>();
            return model.SelectedBoneIndices
                .Concat(model.SelectedMeshIndices.Where(i => !model.SelectedBoneIndices.Contains(i)))
                .Where(i => i >= 0 && i < model.MeshContextCount)
                .ToArray();
        }

        private void Send(SetBoneTransformValueCommand.Field field, float value)
        {
            var idx = SelIndices();
            if (idx.Length == 0) return;
            // モデルインデックス0固定（現状シングルモデル）
            _ctx?.SendCommand(new BeginBoneTransformSliderDragCommand(0, idx));
            _ctx?.SendCommand(new SetBoneTransformValueCommand(0, idx, field, value));
        }

        private void SendEnd()
        {
            _ctx?.SendCommand(new EndBoneTransformSliderDragCommand(0, "オブジェクト回転/スケール変更"));
        }

        // ================================================================
        // UI構築ヘルパー
        // ================================================================

        private void AddRotRow(string label, float min, float max,
            out Slider slider, out FloatField field,
            SetBoneTransformValueCommand.Field cmdField)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.color = new StyleColor(Color.white);
            lbl.style.width           = 16;
            lbl.style.unityTextAlign  = TextAnchor.MiddleLeft;
            lbl.style.fontSize        = 10;
            row.Add(lbl);

            var sl = new Slider(min, max) { value = 0f };
            sl.style.color = new StyleColor(Color.white);
            sl.style.flexGrow = 1;
            row.Add(sl);

            var nf = new FloatField { value = 0f };
            nf.style.color = new StyleColor(Color.black);
            nf.style.width = 48;
            row.Add(nf);

            slider = sl;
            field  = nf;

            sl.RegisterValueChangedCallback(e =>
            {
                if (_isSyncing) return;
                _isSyncing = true;
                try { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 2)); }
                finally { _isSyncing = false; }
                Send(cmdField, e.newValue);
            });
            sl.RegisterCallback<PointerCaptureOutEvent>(_ => SendEnd());

            nf.RegisterValueChangedCallback(e =>
            {
                if (_isSyncing) return;
                float v = e.newValue;
                _isSyncing = true;
                try { sl.SetValueWithoutNotify(NormAngle(v)); }
                finally { _isSyncing = false; }
                Send(cmdField, v);
                SendEnd();
            });

            _root.Add(row);
        }

        private void AddScaleRow(string label, out FloatField field,
            SetBoneTransformValueCommand.Field cmdField)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.color = new StyleColor(Color.white);
            lbl.style.width          = 16;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;
            row.Add(lbl);

            var nf = new FloatField { value = 1f };
            nf.style.color = new StyleColor(Color.black);
            nf.style.flexGrow = 1;
            row.Add(nf);

            field = nf;

            nf.RegisterValueChangedCallback(e =>
            {
                if (_isSyncing) return;
                Send(cmdField, e.newValue);
                SendEnd();
            });

            _root.Add(row);
        }

        private void AddSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color      = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize   = 10;
            l.style.marginTop  = 4;
            l.style.marginBottom = 2;
            _root.Add(l);
        }

        private void AddSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            _root.Add(v);
        }

        private static float NormAngle(float deg)
        {
            deg %= 360f;
            if (deg >  180f) deg -= 360f;
            if (deg < -180f) deg += 360f;
            return deg;
        }
    }
}
