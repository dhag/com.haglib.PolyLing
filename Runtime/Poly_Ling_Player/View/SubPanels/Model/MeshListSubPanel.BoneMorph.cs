// MeshListSubPanel.BoneMorph.cs
// オブジェクトリストのサブパネル：BonePose・BoneTransform。
// モーフタブ（旧モーフの一覧・変換・セット作成・試し表示）は段 7 で撤去した（形のパネルへ移した）。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;
using PlayerIoUiKit        = Poly_Ling.Player.PlayerIoUiKit;
using PlayerUiPrefs        = Poly_Ling.Player.PlayerUiPrefs;
using ObjectMoveSettings   = Poly_Ling.Tools.ObjectMoveSettings;
using ParameterLimits      = Poly_Ling.Core.ParameterLimits;
using RecentPaths          = Poly_Ling.Core.RecentPaths;
using PartsDictionaryPath  = Poly_Ling.Core.PartsDictionaryPath;
using MeshRenameCsvHelper  = Poly_Ling.UI.MeshRenameCsvHelper;

namespace Poly_Ling.MeshListV2
{
    public partial class MeshListSubPanel
    {
        // ================================================================
        // BonePose（エディタ版と同一）
        // ================================================================

        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = new VisualElement { name = "bone-pose-section" };
            _bonePoseSection.style.marginTop = 4;

            _poseFoldout = new Foldout { text = "ボーンポーズ", value = true, name = "pose-foldout" };

            _poseActiveToggle = new Toggle("アクティブ") { name = "pose-active-toggle" };
            _poseActiveToggle.style.color = new StyleColor(Color.white);
            _poseActiveToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                SendCmd(new SetBonePoseActiveCommand(ModelIndex, SelIndices(), e.newValue));
            });
            _poseFoldout.Add(_poseActiveToggle);

            _poseFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_poseFoldout, out _restPosX, out _restPosY, out _restPosZ, "rest-pos");
            RegRestTF(_restPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegRestTF(_restPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegRestTF(_restPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _poseFoldout.Add(SectionHeader("回転"));
            AddRotFields(_poseFoldout,
                out _restRotX, out _restRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _restRotY, out _restRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _restRotZ, out _restRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: true);

            _poseFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_poseFoldout, out _restSclX, out _restSclY, out _restSclZ, "rest-scl");
            RegRestTF(_restSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegRestTF(_restSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegRestTF(_restSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _poseFoldout.Add(_poseResultPos); _poseFoldout.Add(_poseResultRot);

            _poseLayersContainer = new VisualElement { name = "pose-layers-container" };
            _poseNoLayersLabel = new Label("(レイヤーなし)") { name = "pose-no-layers-label" };
            _poseNoLayersLabel.style.color = new StyleColor(Color.white);
            _poseLayersContainer.Add(_poseNoLayersLabel);
            _poseFoldout.Add(_poseLayersContainer);

            var poseRow = new VisualElement(); poseRow.style.flexDirection = FlexDirection.Row; poseRow.style.marginTop = 4;
            _btnInitPose     = MakeSmallBtn("初期化", "btn-init-pose");
            _btnResetLayers  = MakeSmallBtn("レイヤーリセット", "btn-reset-layers");
            poseRow.Add(_btnInitPose); poseRow.Add(_btnResetLayers);
            _poseFoldout.Add(poseRow);
            _bonePoseSection.Add(_poseFoldout);

            _bindposeFoldout = new Foldout { text = "バインドポーズ", value = false, name = "bindpose-foldout" };
            _bindposeFoldout.Add(_bindposePos); _bindposeFoldout.Add(_bindposeRot); _bindposeFoldout.Add(_bindposeScl);
            _btnBakePose = MakeSmallBtn("ポーズベイク", "btn-bake-pose");
            _bindposeFoldout.Add(_btnBakePose);
            _bonePoseSection.Add(_bindposeFoldout);

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new InitBonePoseCommand(ModelIndex, i)); });
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new ResetBonePoseLayersCommand(ModelIndex, i)); });
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new BakePoseToBindPoseCommand(ModelIndex, i)); });

            _mainContent?.Add(_bonePoseSection);
        }

        private void RegRestTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegRestRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingPoseUI = false; }
            });
        }

        private void RegRestRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingPoseUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "ボーン回転変更")));
        }

        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;

            if (IsSimpleMode)
            {
                bool show = _selectedAdapters.Any(a => a.MeshView.BonePose.HasPose);
                _bonePoseSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) return;
            }

            if (_currentTab != TabType.Bone) return;
            _isSyncingPoseUI = true;
            try
            {
                if (_selectedAdapters.Count == 0) { SetPoseEmpty(); return; }
                var poses = _selectedAdapters.Select(a => a.MeshView.BonePose).Where(bp => bp.HasPose).ToList();
                bool all  = poses.Count == _selectedAdapters.Count;
                bool none = poses.Count == 0;

                if (all) { bool f = poses[0].IsActive; bool same = poses.TrueForAll(p => p.IsActive == f); _poseActiveToggle?.SetValueWithoutNotify(same ? f : false); SMV(_poseActiveToggle, !same); }
                else { _poseActiveToggle?.SetValueWithoutNotify(false); SMV(_poseActiveToggle, !none); }
                _poseActiveToggle?.SetEnabled(true);

                if (all && poses.Count > 0)
                {
                    var views = _selectedAdapters.Select(a => a.MeshView).ToList();
                    MixFT(_restPosX, views, v => v.LocalPosition.x); MixFT(_restPosY, views, v => v.LocalPosition.y); MixFT(_restPosZ, views, v => v.LocalPosition.z);
                    MixRTF(_restRotX, _restRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_restRotY, _restRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_restRotZ, _restRotSliderZ, views, v => v.LocalRotationEuler.z);
                    MixFT(_restSclX, views, v => v.LocalScale.x); MixFT(_restSclY, views, v => v.LocalScale.y); MixFT(_restSclZ, views, v => v.LocalScale.z);
                }
                else
                {
                    SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
                    SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
                    SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
                    SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
                }

                var single = (_selectedAdapters.Count == 1 && all) ? poses[0] : null;
                UpdateLayers(single);
                if (single != null)
                {
                    SL(_poseResultPos, $"Pos: ({single.ResultPosition.x:F3}, {single.ResultPosition.y:F3}, {single.ResultPosition.z:F3})");
                    SL(_poseResultRot, $"Rot: ({single.ResultRotationEuler.x:F1}, {single.ResultRotationEuler.y:F1}, {single.ResultRotationEuler.z:F1})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_poseResultPos, $"Pos: {m}"); SL(_poseResultRot, $"Rot: {m}"); }

                _btnInitPose?.SetEnabled(false); if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(all && poses.Any(p => p.LayerCount > 0));

                if (_selectedAdapters.Count == 1 && all)
                {
                    var bp = poses[0];
                    SL(_bindposePos, $"Pos: ({bp.BindPosePosition.x:F3}, {bp.BindPosePosition.y:F3}, {bp.BindPosePosition.z:F3})");
                    SL(_bindposeRot, $"Rot: ({bp.BindPoseRotationEuler.x:F1}, {bp.BindPoseRotationEuler.y:F1}, {bp.BindPoseRotationEuler.z:F1})");
                    SL(_bindposeScl, $"Scl: ({bp.BindPoseScale.x:F3}, {bp.BindPoseScale.y:F3}, {bp.BindPoseScale.z:F3})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_bindposePos, $"Pos: {m}"); SL(_bindposeRot, $"Rot: {m}"); SL(_bindposeScl, $"Scl: {m}"); }
                _btnBakePose?.SetEnabled(all);
            }
            finally { _isSyncingPoseUI = false; }
        }

        private void SetPoseEmpty()
        {
            _poseActiveToggle?.SetValueWithoutNotify(false); _poseActiveToggle?.SetEnabled(false); SMV(_poseActiveToggle, false);
            SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
            SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
            SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
            SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
            UpdateLayers(null);
            SL(_poseResultPos,"Pos: -"); SL(_poseResultRot,"Rot: -");
            if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
            _btnResetLayers?.SetEnabled(false);
            SL(_bindposePos,"Pos: -"); SL(_bindposeRot,"Rot: -"); SL(_bindposeScl,"Scl: -");
            _btnBakePose?.SetEnabled(false);
        }

        private void UpdateLayers(IBonePoseView pose)
        {
            if (_poseLayersContainer == null) return;
            var rm = _poseLayersContainer.Children().Where(c => c.ClassListContains("pose-layer-row")).ToList();
            foreach (var e in rm) _poseLayersContainer.Remove(e);
            bool has = pose != null && pose.LayerCount > 0;
            if (_poseNoLayersLabel != null) _poseNoLayersLabel.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            if (has)
            {
                var row = new VisualElement(); row.AddToClassList("pose-layer-row");
                row.Add(new Label($"({pose.LayerCount} layers)") { style = { fontSize = 11 } });
                _poseLayersContainer.Add(row);
            }
        }

        // ================================================================
        // BoneTransform（エディタ版と同一）
        // ================================================================

        private void BindTransformUI(VisualElement root)
        {
            _transformFoldout = new Foldout { text = "トランスフォーム", value = false, name = "transform-foldout" };
            _transformFoldout.style.marginTop  = 4;
            _transformFoldout.style.display    = DisplayStyle.None;

            _transformFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_transformFoldout, out _localPosX, out _localPosY, out _localPosZ, "local-pos");
            RegTF(_localPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_localPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_localPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _transformFoldout.Add(SectionHeader("回転"));
            AddRotFields(_transformFoldout,
                out _localRotX, out _localRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _localRotY, out _localRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _localRotZ, out _localRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: false);

            _transformFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_transformFoldout, out _localSclX, out _localSclY, out _localSclZ, "local-scl");
            RegTF(_localSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_localSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_localSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _mainContent?.Add(_transformFoldout);
        }

        private void RegTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegTRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingTransformUI = false; }
            });
        }

        private void RegTRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingTransformUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "トランスフォーム回転変更")));
        }

        private void UpdateTransformPanel()
        {
            if (_transformFoldout == null) return;
            if (!IsSimpleMode) { _transformFoldout.style.display = DisplayStyle.None; return; }
            bool show = _selectedAdapters.Any(a => !a.MeshView.BonePose.HasPose);
            _transformFoldout.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _isSyncingTransformUI = true;
            try
            {
                var views = _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MeshView).ToList();
                MixFT(_localPosX, views, v => v.LocalPosition.x); MixFT(_localPosY, views, v => v.LocalPosition.y); MixFT(_localPosZ, views, v => v.LocalPosition.z);
                MixRTF(_localRotX, _localRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_localRotY, _localRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_localRotZ, _localRotSliderZ, views, v => v.LocalRotationEuler.z);
                MixFT(_localSclX, views, v => v.LocalScale.x); MixFT(_localSclY, views, v => v.LocalScale.y); MixFT(_localSclZ, views, v => v.LocalScale.z);
            }
            finally { _isSyncingTransformUI = false; }
        }
    }
}
