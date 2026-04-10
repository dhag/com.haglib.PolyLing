// PlayerBoneEditorSubPanel.cs
// ボーンエディタサブパネル（Player ビルド用）。
// BoneEditorPanelV2 の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerBoneEditorSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext>        GetModel;
        public Func<MeshUndoController>  GetUndoController;
        public Action                    OnRepaint;
        public Action<Vector3>           OnFocusCamera;
        public Action<PanelCommand>      SendCommand;
        public Func<int>                 GetModelIndex;

        // ================================================================
        // UI 要素
        // ================================================================

        private DropdownField _boneDropdown;
        private bool          _suppressBoneDropdown;

        private Label         _warningLabel;
        private Label         _selectionCountLabel;
        private VisualElement _boneDetail;
        private Label         _boneNameLabel;
        private Label         _masterIndexLabel;
        private Label         _boneIndexLabel;
        private Label         _parentBoneLabel;
        private Label         _worldPosLabel;
        private Label         _statusLabel;

        // ── ボーンポーズ UI
        private Toggle        _bonePoseActiveToggle;
        private Button        _btnInitPose;
        private Button        _btnResetLayers;
        private Button        _btnBakePose;

        // ── TRS UI
        private FloatField _posX, _posY, _posZ;
        private FloatField _rotX, _rotY, _rotZ;
        private Slider     _rotSliderX, _rotSliderY, _rotSliderZ;
        private FloatField _sclX, _sclY, _sclZ;
        private bool       _suppressTRS;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            // ── ボーン選択ドロップダウン ─────────────────────────────
            var dropLabel = new Label("ボーン選択:");
            dropLabel.style.color        = new StyleColor(Color.white);
            dropLabel.style.fontSize     = 10;
            dropLabel.style.marginBottom = 2;
            root.Add(dropLabel);

            _boneDropdown = new DropdownField();
            _boneDropdown.style.marginBottom = 6;
            root.Add(_boneDropdown);

            _boneDropdown.RegisterValueChangedCallback(_ =>
            {
                if (_suppressBoneDropdown) return;
                var model = GetModel?.Invoke();
                if (model == null) return;
                int idx   = _boneDropdown.index;
                var bones = model.Bones;
                if (idx < 0 || bones == null || idx >= bones.Count) return;
                int masterIdx = bones[idx].MasterIndex;
                model.SelectBone(masterIdx);
                OnRepaint?.Invoke();
                Refresh();
            });

            root.Add(MakeSep());

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            _selectionCountLabel = new Label();
            _selectionCountLabel.style.color        = new StyleColor(Color.white);
            _selectionCountLabel.style.marginBottom = 4;
            _selectionCountLabel.style.fontSize     = 10;
            root.Add(_selectionCountLabel);

            // ── ボタン行（リセット / フォーカス）────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            var btnReset = new Button(OnResetPose) { text = "ポーズリセット" };
            btnReset.style.flexGrow    = 1;
            btnReset.style.marginRight = 4;
            btnReset.style.height      = 22;
            var btnFocus = new Button(OnFocusBone) { text = "フォーカス" };
            btnFocus.style.flexGrow = 1;
            btnFocus.style.height   = 22;
            btnRow.Add(btnReset);
            btnRow.Add(btnFocus);
            root.Add(btnRow);

            // ── ボーンポーズセクション ───────────────────────────────
            root.Add(MakeSep());
            root.Add(MakeSecLabel("ボーンポーズ"));

            _bonePoseActiveToggle = new Toggle("ポーズ有効") { value = false };
            _bonePoseActiveToggle.style.marginBottom = 4;
            _bonePoseActiveToggle.RegisterValueChangedCallback(e =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] indices = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new SetBonePoseActiveCommand(
                    GetModelIndex?.Invoke() ?? 0, indices, e.newValue));
            });
            root.Add(_bonePoseActiveToggle);

            var poseRow = new VisualElement();
            poseRow.style.flexDirection = FlexDirection.Row;
            poseRow.style.marginBottom  = 4;

            _btnInitPose    = MakeSmallBtn("初期化");
            _btnResetLayers = MakeSmallBtn("レイヤークリア");
            _btnBakePose    = MakeSmallBtn("BindPoseへベイク");
            _btnInitPose.style.flexGrow     = 1;
            _btnInitPose.style.marginRight  = 2;
            _btnResetLayers.style.flexGrow  = 1;
            _btnResetLayers.style.marginRight = 2;
            _btnBakePose.style.flexGrow     = 1;

            _btnInitPose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] indices = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new InitBonePoseCommand(
                    GetModelIndex?.Invoke() ?? 0, indices));
            };
            _btnResetLayers.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] indices = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new ResetBonePoseLayersCommand(
                    GetModelIndex?.Invoke() ?? 0, indices));
            };
            _btnBakePose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] indices = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new BakePoseToBindPoseCommand(
                    GetModelIndex?.Invoke() ?? 0, indices));
            };

            poseRow.Add(_btnInitPose);
            poseRow.Add(_btnResetLayers);
            poseRow.Add(_btnBakePose);
            root.Add(poseRow);

            // ── REST POSE TRS ────────────────────────────────────────
            root.Add(MakeSep());
            root.Add(MakeSecLabel("位置"));
            AddXYZFields(root, "pos", out _posX, out _posY, out _posZ);
            RegTF(_posX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_posY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_posZ, SetBoneTransformValueCommand.Field.PositionZ);

            root.Add(MakeSecLabel("回転"));
            AddRotFields(root, "rot",
                out _rotX, out _rotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _rotY, out _rotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _rotZ, out _rotSliderZ, SetBoneTransformValueCommand.Field.RotationZ);
            RegTF(_rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegTF(_rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegTF(_rotZ, SetBoneTransformValueCommand.Field.RotationZ);
            RegRotSlider(_rotSliderX, _rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegRotSlider(_rotSliderY, _rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegRotSlider(_rotSliderZ, _rotZ, SetBoneTransformValueCommand.Field.RotationZ);

            root.Add(MakeSecLabel("スケール"));
            AddXYZFields(root, "scl", out _sclX, out _sclY, out _sclZ);
            RegTF(_sclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_sclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_sclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            // ── 詳細エリア ───────────────────────────────────────────
            root.Add(MakeSep());

            _boneDetail = new VisualElement();
            _boneDetail.style.display = DisplayStyle.None;
            root.Add(_boneDetail);

            AddRow(_boneDetail, "ボーン名",    out _boneNameLabel);
            AddRow(_boneDetail, "マスターIdx", out _masterIndexLabel);
            AddRow(_boneDetail, "ボーンIdx",   out _boneIndexLabel);
            AddRow(_boneDetail, "親ボーン",    out _parentBoneLabel);

            _boneDetail.Add(MakeSep());
            _boneDetail.Add(MakeSecLabel("ワールド座標"));
            AddRow(_boneDetail, "位置", out _worldPosLabel);

            // ステータス
            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 6;
            root.Add(_statusLabel);
        }

        // ================================================================
        // Refresh（Viewer から呼ぶ）
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var model = GetModel?.Invoke();

            // ── ドロップダウン更新 ──────────────────────────────────
            if (_boneDropdown != null)
            {
                _suppressBoneDropdown = true;
                if (model != null)
                {
                    var bones = model.Bones;
                    var choices = new List<string>();
                    if (bones != null)
                        foreach (var entry in bones)
                            choices.Add(entry.Name);
                    _boneDropdown.choices = choices;

                    int dropIdx = -1;
                    if (model.HasBoneSelection && bones != null)
                    {
                        int firstMaster = model.SelectedBoneIndices[0];
                        for (int i = 0; i < bones.Count; i++)
                            if (bones[i].MasterIndex == firstMaster) { dropIdx = i; break; }
                    }
                    _boneDropdown.index = dropIdx;
                }
                else
                {
                    _boneDropdown.choices = new List<string>();
                    _boneDropdown.index   = -1;
                }
                _suppressBoneDropdown = false;
            }

            if (model == null)
            {
                SetWarning("モデルがありません");
                if (_boneDetail != null) _boneDetail.style.display = DisplayStyle.None;
                SetPoseUIEnabled(false);
                return;
            }

            var bonesAll = model.Bones;
            if (bonesAll == null || bonesAll.Count == 0)
            {
                SetWarning("モデルにボーンがありません");
                if (_boneDetail != null) _boneDetail.style.display = DisplayStyle.None;
                SetPoseUIEnabled(false);
                return;
            }

            SetPoseUIEnabled(model.HasBoneSelection);

            if (!model.HasBoneSelection)
            {
                SetWarning("");
                _selectionCountLabel.text = "未選択 — ビューポートかドロップダウンで選択";
                if (_boneDetail != null) _boneDetail.style.display = DisplayStyle.None;
                _statusLabel.text         = $"Bones: {bonesAll.Count}";
                return;
            }

            SetWarning("");
            if (_boneDetail != null) _boneDetail.style.display = DisplayStyle.Flex;

            var indices = model.SelectedBoneIndices;
            int count   = indices.Count;
            int first   = indices[0];

            _selectionCountLabel.text = count == 1 ? "1 ボーン選択中" : $"{count} ボーン選択中";

            // ── ボーンポーズ Toggle 同期（SetValueWithoutNotify でループなし）──
            if (_bonePoseActiveToggle != null)
            {
                var ctx0   = model.GetMeshContext(first);
                bool active = ctx0?.BonePoseData?.IsActive ?? false;
                _bonePoseActiveToggle.SetValueWithoutNotify(active);
            }

            // ── 詳細情報 ─────────────────────────────────────────────
            var ctx = model.GetMeshContext(first);
            if (ctx == null) return;

            _boneNameLabel.text    = ctx.Name ?? "(no name)";
            _masterIndexLabel.text = first.ToString();

            int boneIdx = model.TypedIndices?.MasterToBoneIndex(first) ?? -1;
            _boneIndexLabel.text = boneIdx >= 0 ? boneIdx.ToString() : "-";

            int parentIdx = ctx.ParentIndex;
            if (parentIdx >= 0 && parentIdx < model.MeshContextCount)
            {
                var parentCtx = model.GetMeshContext(parentIdx);
                _parentBoneLabel.text = $"{parentCtx?.Name ?? "-"} [{parentIdx}]";
            }
            else
            {
                _parentBoneLabel.text = "(なし)";
            }

            var wm = ctx.WorldMatrix;
            _worldPosLabel.text = $"({wm.m03:F4}, {wm.m13:F4}, {wm.m23:F4})";
            _statusLabel.text   = $"Bones: {bonesAll.Count}  Selected: {count}";

            // ── TRS 同期 ─────────────────────────────────────────────
            _suppressTRS = true;
            var bt = ctx.BoneTransform;
            if (bt != null)
            {
                SF(_posX, bt.Position.x); SF(_posY, bt.Position.y); SF(_posZ, bt.Position.z);
                SF(_rotX, bt.Rotation.x); SF(_rotY, bt.Rotation.y); SF(_rotZ, bt.Rotation.z);
                SS(_rotSliderX, bt.Rotation.x); SS(_rotSliderY, bt.Rotation.y); SS(_rotSliderZ, bt.Rotation.z);
                SF(_sclX, bt.Scale.x);    SF(_sclY, bt.Scale.y);    SF(_sclZ, bt.Scale.z);
            }
            else
            {
                SF(_posX,0); SF(_posY,0); SF(_posZ,0);
                SF(_rotX,0); SF(_rotY,0); SF(_rotZ,0);
                SS(_rotSliderX,0); SS(_rotSliderY,0); SS(_rotSliderZ,0);
                SF(_sclX,1); SF(_sclY,1); SF(_sclZ,1);
            }
            _suppressTRS = false;
        }

        // ================================================================
        // ボタンアクション
        // ================================================================

        private void OnResetPose()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;

            var indices = new List<int>(model.SelectedBoneIndices);
            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();
            var contexts = new List<(int idx, MeshContext ctx)>();

            foreach (var idx in indices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx == null) continue;
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData          = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
                beforeSnapshots[idx] = ctx.BonePoseData.CreateSnapshot();
                contexts.Add((idx, ctx));
            }

            if (contexts.Count == 0) return;

            foreach (var (_, ctx) in contexts)
            {
                ctx.BonePoseData.ClearAllLayers();
                ctx.BonePoseData.SetDirty();
            }

            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var (idx, ctx) in contexts)
                {
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = idx,
                        OldSnapshot = beforeSnapshots.TryGetValue(idx, out var b)
                            ? b : (BonePoseDataSnapshot?)null,
                        NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    });
                }
                undo.MeshListStack.Record(record, "ボーンポーズリセット");
                undo.FocusMeshList();
            }

            model.OnListChanged?.Invoke();
            OnRepaint?.Invoke();
            Refresh();
        }

        private void OnFocusBone()
        {
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            int first = model.SelectedBoneIndices[0];
            var ctx   = model.GetMeshContext(first);
            if (ctx == null) return;
            var wm = ctx.WorldMatrix;
            OnFocusCamera?.Invoke(new Vector3(wm.m03, wm.m13, wm.m23));
        }

        // ================================================================
        // TRS ヘルパー
        // ================================================================

        private void RegTF(FloatField f, SetBoneTransformValueCommand.Field field)
        {
            f.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] idx = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new SetBoneTransformValueCommand(
                    GetModelIndex?.Invoke() ?? 0, idx, field, e.newValue));
                OnRepaint?.Invoke();
            });
        }

        private void RegRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field field)
        {
            s.RegisterCallback<PointerDownEvent>(_ =>
                SendCommand?.Invoke(new BeginBoneTransformSliderDragCommand(
                    GetModelIndex?.Invoke() ?? 0,
                    GetModel?.Invoke()?.SelectedBoneIndices?.ToArray() ?? System.Array.Empty<int>())));
            s.RegisterCallback<PointerCaptureOutEvent>(_ =>
                SendCommand?.Invoke(new EndBoneTransformSliderDragCommand(
                    GetModelIndex?.Invoke() ?? 0, "ボーン回転変更")));
            s.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                int[] idx = model.SelectedBoneIndices.ToArray();
                SendCommand?.Invoke(new SetBoneTransformValueCommand(
                    GetModelIndex?.Invoke() ?? 0, idx, field, e.newValue));
                if (f != null) { _suppressTRS = true; SF(f, e.newValue); _suppressTRS = false; }
                OnRepaint?.Invoke();
            });
        }

        private void AddXYZFields(VisualElement parent, string prefix,
            out FloatField fx, out FloatField fy, out FloatField fz)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            fx = MakeFloatField(prefix + "-x", "X"); row.Add(fx);
            fy = MakeFloatField(prefix + "-y", "Y"); row.Add(fy);
            fz = MakeFloatField(prefix + "-z", "Z"); row.Add(fz);
            parent.Add(row);
        }

        private void AddRotFields(VisualElement parent, string prefix,
            out FloatField fx, out Slider sx, SetBoneTransformValueCommand.Field fx_field,
            out FloatField fy, out Slider sy, SetBoneTransformValueCommand.Field fy_field,
            out FloatField fz, out Slider sz, SetBoneTransformValueCommand.Field fz_field)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            fx = MakeFloatField(prefix + "-x", "X"); row.Add(fx);
            fy = MakeFloatField(prefix + "-y", "Y"); row.Add(fy);
            fz = MakeFloatField(prefix + "-z", "Z"); row.Add(fz);
            parent.Add(row);

            sx = MakeRotSlider(prefix + "-sx"); parent.Add(sx);
            sy = MakeRotSlider(prefix + "-sy"); parent.Add(sy);
            sz = MakeRotSlider(prefix + "-sz"); parent.Add(sz);
        }

        private static FloatField MakeFloatField(string name, string label)
        {
            var f = new FloatField(label) { name = name };
            f.style.flexGrow  = 1;
            f.style.marginRight = 2;
            f.style.color     = new StyleColor(Color.black);
            return f;
        }

        private static Slider MakeRotSlider(string name)
        {
            var s = new Slider(-180f, 180f) { name = name };
            s.style.marginBottom = 2;
            return s;
        }

        private static void SF(FloatField f, float v)
        {
            if (f != null) f.SetValueWithoutNotify((float)System.Math.Round(v, 4));
        }

        private static void SS(Slider s, float v)
        {
            if (s != null) s.SetValueWithoutNotify(Mathf.Clamp(v, -180f, 180f));
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private void SetPoseUIEnabled(bool enabled)
        {
            _bonePoseActiveToggle?.SetEnabled(enabled);
            _btnInitPose?.SetEnabled(enabled);
            _btnResetLayers?.SetEnabled(enabled);
            _btnBakePose?.SetEnabled(enabled);
            _posX?.SetEnabled(enabled); _posY?.SetEnabled(enabled); _posZ?.SetEnabled(enabled);
            _rotX?.SetEnabled(enabled); _rotY?.SetEnabled(enabled); _rotZ?.SetEnabled(enabled);
            _rotSliderX?.SetEnabled(enabled); _rotSliderY?.SetEnabled(enabled); _rotSliderZ?.SetEnabled(enabled);
            _sclX?.SetEnabled(enabled); _sclY?.SetEnabled(enabled); _sclZ?.SetEnabled(enabled);
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display =
                string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static Button MakeSmallBtn(string text)
        {
            var b = new Button { text = text };
            b.style.fontSize      = 9;
            b.style.height        = 20;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.marginBottom  = 2;
            return b;
        }

        private static void AddRow(VisualElement parent, string labelText, out Label valueLabel)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var val = new Label();
            val.style.color    = new StyleColor(Color.white);
            val.style.flexGrow = 1;
            val.style.fontSize = 10;
            row.Add(key); row.Add(val);
            parent.Add(row);
            valueLabel = val;
        }

        private static Label MakeSecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
