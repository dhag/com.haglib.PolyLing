// TypedMeshListPanel.Bone.cs
// Bone - BonePose UI, editing, Undo

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public partial class TypedMeshListPanel
    {
        // BonePoseセクション（Phase BonePose追加）
        private VisualElement _bonePoseSection;
        private Foldout _poseFoldout, _bindposeFoldout;
        private Toggle _poseActiveToggle;
        private FloatField _restPosX, _restPosY, _restPosZ;
        private FloatField _restRotX, _restRotY, _restRotZ;
        private Slider _restRotSliderX, _restRotSliderY, _restRotSliderZ;
        private FloatField _restSclX, _restSclY, _restSclZ;
        private VisualElement _poseLayersContainer;
        private Label _poseNoLayersLabel;
        private Label _poseResultPos, _poseResultRot;
        private Button _btnInitPose, _btnResetLayers;
        private Label _bindposePos, _bindposeRot, _bindposeScl;
        private Button _btnBakePose;
        private bool _isSyncingPoseUI = false;

        // BonePose Undo用（スライダードラッグ中のスナップショット保持）
        private Dictionary<int, BonePoseDataSnapshot> _sliderDragBeforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();


        // BonePose セクション（Phase BonePose追加）
        // ================================================================

        /// <summary>
        /// BonePoseセクションのUI要素をバインド
        /// </summary>
        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = root.Q<VisualElement>("bone-pose-section");
            _poseFoldout = root.Q<Foldout>("pose-foldout");
            _bindposeFoldout = root.Q<Foldout>("bindpose-foldout");

            _poseActiveToggle = root.Q<Toggle>("pose-active-toggle");

            _restPosX = root.Q<FloatField>("rest-pos-x");
            _restPosY = root.Q<FloatField>("rest-pos-y");
            _restPosZ = root.Q<FloatField>("rest-pos-z");
            _restRotX = root.Q<FloatField>("rest-rot-x");
            _restRotY = root.Q<FloatField>("rest-rot-y");
            _restRotZ = root.Q<FloatField>("rest-rot-z");
            _restRotSliderX = root.Q<Slider>("rest-rot-slider-x");
            _restRotSliderY = root.Q<Slider>("rest-rot-slider-y");
            _restRotSliderZ = root.Q<Slider>("rest-rot-slider-z");
            _restSclX = root.Q<FloatField>("rest-scl-x");
            _restSclY = root.Q<FloatField>("rest-scl-y");
            _restSclZ = root.Q<FloatField>("rest-scl-z");

            _poseLayersContainer = root.Q<VisualElement>("pose-layers-container");
            _poseNoLayersLabel = root.Q<Label>("pose-no-layers-label");

            _poseResultPos = root.Q<Label>("pose-result-pos");
            _poseResultRot = root.Q<Label>("pose-result-rot");

            _btnInitPose = root.Q<Button>("btn-init-pose");
            _btnResetLayers = root.Q<Button>("btn-reset-layers");

            _bindposePos = root.Q<Label>("bindpose-pos");
            _bindposeRot = root.Q<Label>("bindpose-rot");
            _bindposeScl = root.Q<Label>("bindpose-scl");
            _btnBakePose = root.Q<Button>("btn-bake-pose");

            // イベント登録
            _poseActiveToggle?.RegisterValueChangedCallback(OnPoseActiveChanged);

            RegisterRestPoseField(_restPosX, (pose, v) => pose.RestPosition = SetX(pose.RestPosition, v));
            RegisterRestPoseField(_restPosY, (pose, v) => pose.RestPosition = SetY(pose.RestPosition, v));
            RegisterRestPoseField(_restPosZ, (pose, v) => pose.RestPosition = SetZ(pose.RestPosition, v));

            RegisterRestRotField(_restRotX, 0);
            RegisterRestRotField(_restRotY, 1);
            RegisterRestRotField(_restRotZ, 2);

            RegisterRestRotSlider(_restRotSliderX, 0);
            RegisterRestRotSlider(_restRotSliderY, 1);
            RegisterRestRotSlider(_restRotSliderZ, 2);

            RegisterRestPoseField(_restSclX, (pose, v) => pose.RestScale = SetX(pose.RestScale, v));
            RegisterRestPoseField(_restSclY, (pose, v) => pose.RestScale = SetY(pose.RestScale, v));
            RegisterRestPoseField(_restSclZ, (pose, v) => pose.RestScale = SetZ(pose.RestScale, v));

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => OnInitPoseClicked());
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => OnResetLayersClicked());
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => OnBakePoseClicked());
        }

        private void RegisterRestPoseField(FloatField field, Action<BonePoseData, float> setter)
        {
            field?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                var beforeSnapshots = CaptureSnapshots(targets);
                foreach (var (_, _, pose) in targets)
                {
                    setter(pose, evt.newValue);
                    pose.SetDirty();
                }
                var afterSnapshots = CaptureSnapshots(targets);
                RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "ボーンポーズ変更");
                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void RegisterRestRotField(FloatField field, int axis)
        {
            field?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                var beforeSnapshots = CaptureSnapshots(targets);
                foreach (var (_, _, pose) in targets)
                {
                    Vector3 euler = IsQuatValid(pose.RestRotation)
                        ? pose.RestRotation.eulerAngles
                        : Vector3.zero;

                    if (axis == 0) euler.x = evt.newValue;
                    else if (axis == 1) euler.y = evt.newValue;
                    else euler.z = evt.newValue;

                    pose.RestRotation = Quaternion.Euler(euler);
                    pose.SetDirty();
                }
                var afterSnapshots = CaptureSnapshots(targets);
                RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "ボーン回転変更");

                // スライダ同期
                _isSyncingPoseUI = true;
                try
                {
                    var slider = axis == 0 ? _restRotSliderX : (axis == 1 ? _restRotSliderY : _restRotSliderZ);
                    float normalized = NormalizeAngle(evt.newValue);
                    slider?.SetValueWithoutNotify(normalized);
                }
                finally
                {
                    _isSyncingPoseUI = false;
                }

                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void RegisterRestRotSlider(Slider slider, int axis)
        {
            slider?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                // ドラッグ開始時にスナップショットを取得（1ドラッグ1記録）
                if (_sliderDragBeforeSnapshots.Count == 0)
                {
                    foreach (var (idx, _, pose) in targets)
                        _sliderDragBeforeSnapshots[idx] = pose.CreateSnapshot();
                }

                foreach (var (_, _, pose) in targets)
                {
                    Vector3 euler = IsQuatValid(pose.RestRotation)
                        ? pose.RestRotation.eulerAngles
                        : Vector3.zero;

                    if (axis == 0) euler.x = evt.newValue;
                    else if (axis == 1) euler.y = evt.newValue;
                    else euler.z = evt.newValue;

                    pose.RestRotation = Quaternion.Euler(euler);
                    pose.SetDirty();
                }

                // FloatField同期
                _isSyncingPoseUI = true;
                try
                {
                    var floatField = axis == 0 ? _restRotX : (axis == 1 ? _restRotY : _restRotZ);
                    floatField?.SetValueWithoutNotify((float)System.Math.Round(evt.newValue, 4));
                }
                finally
                {
                    _isSyncingPoseUI = false;
                }

                UpdateBonePosePanel();
                NotifyModelChanged();
            });

            // ドラッグ完了時にUndo記録をコミット
            slider?.RegisterCallback<PointerCaptureOutEvent>(_ => CommitSliderDragUndo("ボーン回転変更"));
        }

        /// <summary>
        /// 角度を -180～180 の範囲に正規化
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private void OnPoseActiveChanged(ChangeEvent<bool> evt)
        {
            if (_isSyncingPoseUI) return;
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                beforeSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            foreach (var (_, ctx) in boneContexts)
            {
                if (evt.newValue)
                {
                    if (ctx.BonePoseData == null)
                        ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                    ctx.BonePoseData.SetDirty();
                }
                else
                {
                    if (ctx.BonePoseData != null)
                    {
                        ctx.BonePoseData.IsActive = false;
                        ctx.BonePoseData.SetDirty();
                    }
                }
            }

            var afterSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                afterSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            RecordMultiBonePoseUndoRaw(beforeSnapshots, afterSnapshots,
                evt.newValue ? "ボーンポーズ有効化" : "ボーンポーズ無効化");
            UpdateBonePosePanel();
            NotifyModelChanged();
        }

        private void OnInitPoseClicked()
        {
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                beforeSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            foreach (var (_, ctx) in boneContexts)
            {
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
            }

            var afterSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                afterSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            RecordMultiBonePoseUndoRaw(beforeSnapshots, afterSnapshots, "ボーンポーズ初期化");
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("BonePoseData初期化完了");
        }

        private void OnResetLayersClicked()
        {
            var targets = GetSelectedBonePoseDatas();
            if (targets.Count == 0) return;

            var beforeSnapshots = CaptureSnapshots(targets);
            foreach (var (_, _, pose) in targets)
                pose.ClearAllLayers();
            var afterSnapshots = CaptureSnapshots(targets);
            RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "全レイヤークリア");
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("全レイヤーをクリア");
        }

        private void OnBakePoseClicked()
        {
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            // BonePoseDataを持つもののみ対象
            var targets = new List<(int idx, MeshContext ctx)>();
            foreach (var (idx, ctx) in boneContexts)
            {
                if (ctx.BonePoseData != null)
                    targets.Add((idx, ctx));
            }
            if (targets.Count == 0) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var (idx, ctx) in targets)
            {
                var beforePose = ctx.BonePoseData.CreateSnapshot();
                Matrix4x4 oldBindPose = ctx.BindPose;

                ctx.BonePoseData.BakeToBindPose(ctx.WorldMatrix);
                ctx.BindPose = ctx.WorldMatrix.inverse;

                var afterPose = ctx.BonePoseData.CreateSnapshot();
                Matrix4x4 newBindPose = ctx.BindPose;

                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = beforePose,
                    NewSnapshot = afterPose,
                    OldBindPose = oldBindPose,
                    NewBindPose = newBindPose
                });
            }

            var undoController = ToolCtx?.UndoController;
            if (undoController != null)
            {
                undoController.MeshListStack.Record(record, "BindPoseにベイク");
                undoController.FocusMeshList();
            }

            UpdateBonePosePanel();
            NotifyModelChanged();
            Log($"BindPoseにベイク完了 ({targets.Count}件)");
        }

        /// <summary>
        /// BonePoseパネルの表示を更新（複数選択対応・Unity-style混合値）
        /// </summary>
        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;
            if (_currentTab != TabType.Bone) return;

            _isSyncingPoseUI = true;
            try
            {
                var boneContexts = GetSelectedBoneContexts();

                if (boneContexts.Count == 0)
                {
                    // 選択なし
                    SetPoseFieldsEmpty();
                    return;
                }

                // 全選択ボーンのBonePoseDataを収集
                var poses = new List<BonePoseData>();
                foreach (var (_, ctx) in boneContexts)
                {
                    if (ctx.BonePoseData != null)
                        poses.Add(ctx.BonePoseData);
                }

                bool allHavePose = poses.Count == boneContexts.Count;
                bool noneHavePose = poses.Count == 0;

                // Active トグル
                if (allHavePose)
                {
                    bool firstActive = poses[0].IsActive;
                    bool allSame = poses.TrueForAll(p => p.IsActive == firstActive);
                    _poseActiveToggle?.SetValueWithoutNotify(allSame ? firstActive : false);
                    SetMixedValue(_poseActiveToggle, !allSame);
                }
                else if (noneHavePose)
                {
                    _poseActiveToggle?.SetValueWithoutNotify(false);
                    SetMixedValue(_poseActiveToggle, false);
                }
                else
                {
                    _poseActiveToggle?.SetValueWithoutNotify(false);
                    SetMixedValue(_poseActiveToggle, true);
                }
                _poseActiveToggle?.SetEnabled(boneContexts.Count > 0);

                // RestPose フィールド
                if (allHavePose && poses.Count > 0)
                {
                    SetMixedFloatField(_restPosX, poses, p => p.RestPosition.x, true);
                    SetMixedFloatField(_restPosY, poses, p => p.RestPosition.y, true);
                    SetMixedFloatField(_restPosZ, poses, p => p.RestPosition.z, true);

                    SetMixedRotField(_restRotX, _restRotSliderX, poses, 0, true);
                    SetMixedRotField(_restRotY, _restRotSliderY, poses, 1, true);
                    SetMixedRotField(_restRotZ, _restRotSliderZ, poses, 2, true);

                    SetMixedFloatField(_restSclX, poses, p => p.RestScale.x, true);
                    SetMixedFloatField(_restSclY, poses, p => p.RestScale.y, true);
                    SetMixedFloatField(_restSclZ, poses, p => p.RestScale.z, true);
                }
                else
                {
                    SetFloatField(_restPosX, 0, false); SetFloatField(_restPosY, 0, false); SetFloatField(_restPosZ, 0, false);
                    SetFloatField(_restRotX, 0, false); SetFloatField(_restRotY, 0, false); SetFloatField(_restRotZ, 0, false);
                    SetSlider(_restRotSliderX, 0, false); SetSlider(_restRotSliderY, 0, false); SetSlider(_restRotSliderZ, 0, false);
                    SetFloatField(_restSclX, 1, false); SetFloatField(_restSclY, 1, false); SetFloatField(_restSclZ, 1, false);
                }

                // レイヤー一覧（単一選択のみ）
                BonePoseData singlePose = (boneContexts.Count == 1 && allHavePose) ? poses[0] : null;
                UpdateLayersList(singlePose);

                // 合成結果（単一選択のみ）
                if (singlePose != null)
                {
                    Vector3 pos = singlePose.Position;
                    Vector3 rot = IsQuatValid(singlePose.Rotation)
                        ? singlePose.Rotation.eulerAngles
                        : Vector3.zero;
                    if (_poseResultPos != null)
                        _poseResultPos.text = $"Pos: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})";
                    if (_poseResultRot != null)
                        _poseResultRot.text = $"Rot: ({rot.x:F1}, {rot.y:F1}, {rot.z:F1})";
                }
                else
                {
                    if (_poseResultPos != null) _poseResultPos.text = boneContexts.Count > 1 ? "Pos: (複数選択)" : "Pos: -";
                    if (_poseResultRot != null) _poseResultRot.text = boneContexts.Count > 1 ? "Rot: (複数選択)" : "Rot: -";
                }

                // Initボタン
                _btnInitPose?.SetEnabled(false);
                if (_btnInitPose != null)
                    _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(allHavePose && poses.Any(p => p.LayerCount > 0));

                // BindPose（単一選択のみ値表示）
                if (boneContexts.Count == 1)
                {
                    var ctx = boneContexts[0].ctx;
                    Matrix4x4 bp = ctx.BindPose;
                    Vector3 bpPos = (Vector3)bp.GetColumn(3);
                    Vector3 bpRot = IsQuatValid(bp.rotation)
                        ? bp.rotation.eulerAngles
                        : Vector3.zero;
                    Vector3 bpScl = bp.lossyScale;

                    if (_bindposePos != null)
                        _bindposePos.text = $"Pos: ({bpPos.x:F3}, {bpPos.y:F3}, {bpPos.z:F3})";
                    if (_bindposeRot != null)
                        _bindposeRot.text = $"Rot: ({bpRot.x:F1}, {bpRot.y:F1}, {bpRot.z:F1})";
                    if (_bindposeScl != null)
                        _bindposeScl.text = $"Scl: ({bpScl.x:F3}, {bpScl.y:F3}, {bpScl.z:F3})";
                }
                else
                {
                    if (_bindposePos != null) _bindposePos.text = boneContexts.Count > 1 ? "Pos: (複数選択)" : "Pos: -";
                    if (_bindposeRot != null) _bindposeRot.text = boneContexts.Count > 1 ? "Rot: (複数選択)" : "Rot: -";
                    if (_bindposeScl != null) _bindposeScl.text = boneContexts.Count > 1 ? "Scl: (複数選択)" : "Scl: -";
                }

                _btnBakePose?.SetEnabled(allHavePose);
            }
            finally
            {
                _isSyncingPoseUI = false;
            }
        }

        private void SetPoseFieldsEmpty()
        {
            _poseActiveToggle?.SetValueWithoutNotify(false);
            _poseActiveToggle?.SetEnabled(false);
            SetMixedValue(_poseActiveToggle, false);

            SetFloatField(_restPosX, 0, false); SetFloatField(_restPosY, 0, false); SetFloatField(_restPosZ, 0, false);
            SetFloatField(_restRotX, 0, false); SetFloatField(_restRotY, 0, false); SetFloatField(_restRotZ, 0, false);
            SetSlider(_restRotSliderX, 0, false); SetSlider(_restRotSliderY, 0, false); SetSlider(_restRotSliderZ, 0, false);
            SetFloatField(_restSclX, 1, false); SetFloatField(_restSclY, 1, false); SetFloatField(_restSclZ, 1, false);

            UpdateLayersList(null);

            if (_poseResultPos != null) _poseResultPos.text = "Pos: -";
            if (_poseResultRot != null) _poseResultRot.text = "Rot: -";
            _btnInitPose?.SetEnabled(false);
            if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
            _btnResetLayers?.SetEnabled(false);

            if (_bindposePos != null) _bindposePos.text = "Pos: -";
            if (_bindposeRot != null) _bindposeRot.text = "Rot: -";
            if (_bindposeScl != null) _bindposeScl.text = "Scl: -";
            _btnBakePose?.SetEnabled(false);
        }

        /// <summary>Unity-style: 値が全一致→表示、不一致→showMixedValue</summary>
        private void SetMixedFloatField(FloatField field, List<BonePoseData> poses,
            Func<BonePoseData, float> getter, bool enabled)
        {
            if (field == null) return;
            float first = getter(poses[0]);
            bool allSame = poses.TrueForAll(p => Mathf.Abs(getter(p) - first) < 0.0001f);
            field.SetValueWithoutNotify(allSame ? (float)System.Math.Round(first, 4) : 0f);
            field.showMixedValue = !allSame;
            field.SetEnabled(enabled);
        }

        /// <summary>Unity-style: 回転フィールドとスライダーの混合値処理</summary>
        private void SetMixedRotField(FloatField field, Slider slider, List<BonePoseData> poses,
            int axis, bool enabled)
        {
            if (field == null) return;
            float first = GetEulerAxis(poses[0], axis);
            bool allSame = poses.TrueForAll(p => Mathf.Abs(GetEulerAxis(p, axis) - first) < 0.01f);
            float val = allSame ? first : 0f;
            field.SetValueWithoutNotify((float)System.Math.Round(val, 4));
            field.showMixedValue = !allSame;
            field.SetEnabled(enabled);
            if (slider != null)
            {
                slider.SetValueWithoutNotify(allSame ? NormalizeAngle(val) : 0f);
                slider.SetEnabled(enabled && allSame);
            }
        }

        private static float GetEulerAxis(BonePoseData pose, int axis)
        {
            Vector3 euler = IsQuatValid(pose.RestRotation)
                ? pose.RestRotation.eulerAngles
                : Vector3.zero;
            return axis == 0 ? euler.x : (axis == 1 ? euler.y : euler.z);
        }

        private static void SetMixedValue(Toggle toggle, bool mixed)
        {
            if (toggle == null) return;
            toggle.showMixedValue = mixed;
        }

        private void UpdateLayersList(BonePoseData pose)
        {
            if (_poseLayersContainer == null) return;

            // 動的に追加したレイヤー行を削除（Labelは残す）
            var toRemove = new List<VisualElement>();
            foreach (var child in _poseLayersContainer.Children())
            {
                if (child.ClassListContains("pose-layer-row"))
                    toRemove.Add(child);
            }
            foreach (var el in toRemove)
                _poseLayersContainer.Remove(el);

            bool hasLayers = pose != null && pose.LayerCount > 0;
            if (_poseNoLayersLabel != null)
                _poseNoLayersLabel.style.display = hasLayers ? DisplayStyle.None : DisplayStyle.Flex;

            if (!hasLayers) return;

            foreach (var layer in pose.Layers)
            {
                var row = new VisualElement();
                row.AddToClassList("pose-layer-row");
                if (!layer.Enabled) row.AddToClassList("pose-layer-disabled");

                var nameLabel = new Label(layer.Name);
                nameLabel.AddToClassList("pose-layer-name");

                var euler = IsQuatValid(layer.DeltaRotation)
                    ? layer.DeltaRotation.eulerAngles
                    : Vector3.zero;
                string deltaInfo = $"dP({layer.DeltaPosition.x:F2},{layer.DeltaPosition.y:F2},{layer.DeltaPosition.z:F2}) " +
                                   $"dR({euler.x:F1},{euler.y:F1},{euler.z:F1})";
                var infoLabel = new Label(deltaInfo);
                infoLabel.AddToClassList("pose-layer-info");

                var weightLabel = new Label($"w={layer.Weight:F2}");
                weightLabel.AddToClassList("pose-layer-weight");

                row.Add(nameLabel);
                row.Add(infoLabel);
                row.Add(weightLabel);
                _poseLayersContainer.Add(row);
            }
        }

        // ================================================================
        // BonePose ヘルパー
        // ================================================================

        private MeshContext GetSelectedMeshContext()
        {
            if (_selectedAdapters.Count != 1) return null;
            return _selectedAdapters[0].Entry.Context;
        }

        private BonePoseData GetSelectedBonePoseData()
        {
            return GetSelectedMeshContext()?.BonePoseData;
        }

        private int GetSelectedMasterIndex()
        {
            if (_selectedAdapters.Count != 1) return -1;
            return _selectedAdapters[0].MasterIndex;
        }

        /// <summary>選択中の全ボーンの(masterIndex, MeshContext)リスト</summary>
        private List<(int idx, MeshContext ctx)> GetSelectedBoneContexts()
        {
            var result = new List<(int, MeshContext)>();
            foreach (var adapter in _selectedAdapters)
            {
                var ctx = adapter.Entry.Context;
                if (ctx != null)
                    result.Add((adapter.MasterIndex, ctx));
            }
            return result;
        }

        /// <summary>選択中の全ボーンの(masterIndex, MeshContext, BonePoseData)リスト（BonePoseData有りのみ）</summary>
        private List<(int idx, MeshContext ctx, BonePoseData pose)> GetSelectedBonePoseDatas()
        {
            var result = new List<(int, MeshContext, BonePoseData)>();
            foreach (var adapter in _selectedAdapters)
            {
                var ctx = adapter.Entry.Context;
                if (ctx?.BonePoseData != null)
                    result.Add((adapter.MasterIndex, ctx, ctx.BonePoseData));
            }
            return result;
        }

        /// <summary>スナップショット一括取得</summary>
        private Dictionary<int, BonePoseDataSnapshot> CaptureSnapshots(
            List<(int idx, MeshContext ctx, BonePoseData pose)> targets)
        {
            var dict = new Dictionary<int, BonePoseDataSnapshot>();
            foreach (var (idx, _, pose) in targets)
                dict[idx] = pose.CreateSnapshot();
            return dict;
        }

        /// <summary>
        /// 複数ボーンのBonePose変更をUndoスタックに記録
        /// </summary>
        private void RecordMultiBonePoseUndo(
            List<(int idx, MeshContext ctx, BonePoseData pose)> targets,
            Dictionary<int, BonePoseDataSnapshot> before,
            Dictionary<int, BonePoseDataSnapshot> after,
            string description)
        {
            var undoController = ToolCtx?.UndoController;
            if (undoController == null) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var (idx, _, _) in targets)
            {
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = before.TryGetValue(idx, out var b) ? b : (BonePoseDataSnapshot?)null,
                    NewSnapshot = after.TryGetValue(idx, out var a) ? a : (BonePoseDataSnapshot?)null,
                });
            }
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        /// <summary>
        /// 複数ボーンのBonePose変更をUndoスタックに記録（nullable snapshot辞書版）
        /// </summary>
        private void RecordMultiBonePoseUndoRaw(
            Dictionary<int, BonePoseDataSnapshot?> before,
            Dictionary<int, BonePoseDataSnapshot?> after,
            string description)
        {
            var undoController = ToolCtx?.UndoController;
            if (undoController == null) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var kvp in before)
            {
                after.TryGetValue(kvp.Key, out var afterVal);
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = kvp.Key,
                    OldSnapshot = kvp.Value,
                    NewSnapshot = afterVal,
                });
            }
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        /// <summary>
        /// スライダードラッグ完了時にUndo記録をコミット（複数ボーン対応）
        /// </summary>
        private void CommitSliderDragUndo(string description)
        {
            if (_sliderDragBeforeSnapshots.Count == 0) return;

            var targets = GetSelectedBonePoseDatas();
            var afterSnapshots = CaptureSnapshots(targets);

            var undoController = ToolCtx?.UndoController;
            if (undoController != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var kvp in _sliderDragBeforeSnapshots)
                {
                    afterSnapshots.TryGetValue(kvp.Key, out var afterVal);
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = kvp.Key,
                        OldSnapshot = kvp.Value,
                        NewSnapshot = afterVal,
                    });
                }
                undoController.MeshListStack.Record(record, description);
                undoController.FocusMeshList();
            }

            _sliderDragBeforeSnapshots.Clear();
        }

        private static void SetFloatField(FloatField field, float value, bool enabled)
        {
            if (field == null) return;
            field.SetValueWithoutNotify((float)System.Math.Round(value, 4));
            field.showMixedValue = false;
            field.SetEnabled(enabled);
        }

        private static void SetSlider(Slider slider, float value, bool enabled)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(value);
            slider.SetEnabled(enabled);
        }

        private static Vector3 SetX(Vector3 v, float x) => new Vector3(x, v.y, v.z);
        private static Vector3 SetY(Vector3 v, float y) => new Vector3(v.x, y, v.z);
        private static Vector3 SetZ(Vector3 v, float z) => new Vector3(v.x, v.y, z);

        private static bool IsQuatValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0);
        }

        private static Vector3 SafeEuler(Quaternion q)
        {
            return IsQuatValid(q) ? q.eulerAngles : Vector3.zero;
        }
    }
}
