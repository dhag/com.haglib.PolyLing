// PlayerBoneEditorSubPanel.cs
// ボーン・描画メッシュ統合 TRS エディタ（旧 BoneEditorSubPanel + ObjectMoveTRSPanel の統合）
// SubPanelScope でボーンのみ / 描画メッシュのみ / 両方 を切り替え可能。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
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
        // スコープ
        // ================================================================

        private enum SubPanelScope { BonesOnly, MeshesOnly, Both }
        private SubPanelScope _scope = SubPanelScope.BonesOnly;

        // ================================================================
        // コールバック
        // ================================================================

        public Func<ModelContext>        GetModel;
        public Func<MeshUndoController>  GetUndoController;
        public Action                    OnRepaint;
        public Action<Vector3>           OnFocusCamera;
        public Func<int>                 GetModelIndex;

        /// <summary>
        /// ObjectMoveTool の共有設定インスタンスを返すコールバック。
        /// 設定: MoveWithChildren / PickBones / PickMeshesNoSkin / PickMeshesSkinned。
        /// サブパネルのチェックボックスとこの設定を双方向で同期する。
        /// </summary>
        public Func<Poly_Ling.Tools.ObjectMoveSettings> GetObjectMoveSettings;

        // PanelContext 経由でコマンドを送信
        private PanelContext _panelContext;

        public void SetContext(PanelContext ctx) => _panelContext = ctx;

        private void SendCommand(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ================================================================
        // UI 要素
        // ================================================================

        // スコープタブ
        private Button _tabBones, _tabMeshes;
        private VisualElement _moveOptionsSection;

        // 共通
        private Label         _warningLabel;
        private Label         _selectionCountLabel;

        // ── ボーン専用 ──────────────────────────────────────────────
        private VisualElement _boneSection;
        private DropdownField _boneDropdown;
        private bool          _suppressBoneDropdown;

        private Label         _boneNameLabel;
        private IntegerField  _masterIndexField;
        private Label         _boneIndexLabel;
        private DropdownField _parentBoneDropdown;
        private List<int>     _parentChoiceMasters = new List<int>();
        private bool          _suppressBoneEdit;
        private Label         _worldPosLabel;

        private Toggle        _bonePoseActiveToggle;
        private Button        _btnInitPose;
        private Button        _btnResetLayers;
        private Button        _btnBakePose;
        private Button        _btnFreezePose;
        private VisualElement _bonePoseSection;

        private Button        _btnReset;
        private Button        _btnFocus;

        // ── 共通 TRS ────────────────────────────────────────────────
        private FloatField _posX, _posY, _posZ;
        private FloatField _rotX, _rotY, _rotZ;
        private Slider     _rotSliderX, _rotSliderY, _rotSliderZ;
        private FloatField _sclX, _sclY, _sclZ;
        private bool       _suppressTRS;

        // IgnorePose（描画メッシュ含む場合）
        private Toggle        _ignorePoseToggle;
        private VisualElement _ignorePoseRow;

        // ── ObjectMoveSettings 連動チェックボックス ───────────────
        // BoneInputHandler 廃止に伴い、ObjectMoveTool のピック対象を
        // ここから操作する。GetObjectMoveSettings() 経由で同一インスタンスを共有。
        private Toggle        _toggleMoveWithChildren;
        private Toggle        _toggleModeA;
        private Toggle        _toggleModeB;
        private Toggle        _toggleModeC;
        private bool          _suppressMoveSettings;

        private Label _statusLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            // ── スコープタブ ─────────────────────────────────────────
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom  = 6;
            _tabBones  = MakeScopeTab("ボーン",               () => SetScope(SubPanelScope.BonesOnly));
            _tabMeshes = MakeScopeTab("スキンドでないメッシュ", () => SetScope(SubPanelScope.MeshesOnly));
            _tabBones.style.flexGrow = _tabMeshes.style.flexGrow = 1;
            tabRow.Add(_tabBones); tabRow.Add(_tabMeshes);
            root.Add(tabRow);
            UpdateTabHighlight();

            // ── ピック対象・挙動オプション ───────────────────────────
            // ObjectMoveTool の ObjectMoveSettings と同期するチェックボックス 4 個。
            // BoneInputHandler 廃止後、ボーンエディタ表示中のピック対象を
            // このサブパネルから操作する。
            _moveOptionsSection = new VisualElement();
            _moveOptionsSection.style.marginBottom = 6;

            _toggleMoveWithChildren  = new Toggle("子を一緒に移動") { value = true };
            _toggleModeA             = new Toggle("ボーンだけ動かす（スキン固定）") { value = true };
            _toggleModeB             = new Toggle("スキンごと動かして確定（焼き込み）") { value = false };
            _toggleModeC             = new Toggle("ポーズ（一時）") { value = false };
            _toggleMoveWithChildren.style.color  = new StyleColor(Color.white);
            _toggleModeA.style.color             = new StyleColor(Color.white);
            _toggleModeB.style.color             = new StyleColor(Color.white);
            _toggleModeC.style.color             = new StyleColor(Color.white);

            _toggleMoveWithChildren.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveWithChildren = e.newValue;
            });
            _toggleModeA.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeA.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind;
                _toggleModeB.SetValueWithoutNotify(false);
                _toggleModeC.SetValueWithoutNotify(false);
                Refresh();
            });
            _toggleModeB.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeB.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.SkinBakeRebind;
                _toggleModeA.SetValueWithoutNotify(false);
                _toggleModeC.SetValueWithoutNotify(false);
                Refresh();
            });
            _toggleModeC.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                if (!e.newValue) { _toggleModeC.SetValueWithoutNotify(true); return; }
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveMode = Poly_Ling.Tools.BoneMoveMode.PoseLayer;
                _toggleModeA.SetValueWithoutNotify(false);
                _toggleModeB.SetValueWithoutNotify(false);
                Refresh();
            });

            _moveOptionsSection.Add(_toggleMoveWithChildren);
            _moveOptionsSection.Add(MakeSecLabel("移動モード"));
            _moveOptionsSection.Add(_toggleModeA);
            _moveOptionsSection.Add(_toggleModeB);
            _moveOptionsSection.Add(_toggleModeC);
            root.Add(_moveOptionsSection);

            // ── 警告・選択カウント ───────────────────────────────────
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

            // ── ボーン専用セクション ─────────────────────────────────
            _boneSection = new VisualElement();

            var dropLabel = new Label("ボーン選択:");
            dropLabel.style.color    = new StyleColor(Color.white);
            dropLabel.style.fontSize = 10;
            _boneSection.Add(dropLabel);

            _boneDropdown = new DropdownField();
            _boneDropdown.style.marginBottom = 4;
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
            _boneSection.Add(_boneDropdown);

            // リセット / フォーカス
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;
            _btnReset = new Button(OnResetPose) { text = "ポーズリセット" };
            _btnReset.style.flexGrow    = 1;
            _btnReset.style.marginRight = 4;
            _btnReset.style.height      = 22;
            _btnFocus = new Button(OnFocusBone) { text = "フォーカス" };
            _btnFocus.style.flexGrow = 1;
            _btnFocus.style.height   = 22;
            btnRow.Add(_btnFocus);
            _boneSection.Add(btnRow);

            // ボーンポーズ
            _bonePoseSection = new VisualElement();
            _bonePoseSection.Add(MakeSep());
            _bonePoseSection.Add(MakeSecLabel("ボーンポーズ"));
            _bonePoseSection.Add(_btnReset);

            _bonePoseActiveToggle = new Toggle("ポーズ有効") { value = false };
            _bonePoseActiveToggle.style.marginBottom = 4;
            _bonePoseActiveToggle.RegisterValueChangedCallback(e =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new SetBonePoseActiveCommand(
                    GetModelIndex?.Invoke() ?? 0, model.SelectedBoneIndices.ToArray(), e.newValue));
            });
            _bonePoseSection.Add(_bonePoseActiveToggle);

            var poseRow = new VisualElement();
            poseRow.style.flexDirection = FlexDirection.Row;
            poseRow.style.marginBottom  = 4;
            _btnInitPose    = MakeSmallBtn("初期化");
            _btnResetLayers = MakeSmallBtn("レイヤークリア");
            _btnBakePose    = MakeSmallBtn("BindPoseへベイク");
            _btnInitPose.style.flexGrow     = 1; _btnInitPose.style.marginRight    = 2;
            _btnResetLayers.style.flexGrow  = 1; _btnResetLayers.style.marginRight = 2;
            _btnBakePose.style.flexGrow     = 1;
            _btnInitPose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new InitBonePoseCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            _btnResetLayers.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new ResetBonePoseLayersCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            _btnBakePose.clicked += () =>
            {
                var model = GetModel?.Invoke();
                if (model == null || !model.HasBoneSelection) return;
                SendCommand(new BakePoseToBindPoseCommand(GetModelIndex?.Invoke() ?? 0,
                    model.SelectedBoneIndices.ToArray()));
            };
            poseRow.Add(_btnInitPose); poseRow.Add(_btnResetLayers); poseRow.Add(_btnBakePose);
            _bonePoseSection.Add(poseRow);

            _btnFreezePose = new Button(() =>
            {
                var model = GetModel?.Invoke();
                if (model == null) return;
                SendCommand(new FreezeCurrentPoseCommand(GetModelIndex?.Invoke() ?? 0));
            }) { text = "この姿勢で確定（焼き込み）" };
            _btnFreezePose.style.height    = 22;
            _btnFreezePose.style.marginTop = 4;
            _bonePoseSection.Add(_btnFreezePose);

            _boneSection.Add(_bonePoseSection);

            // ボーン詳細情報
            _boneSection.Add(MakeSep());
            AddRow(_boneSection, "ボーン名",    out _boneNameLabel);
            AddIntRow(_boneSection, "マスターIdx", out _masterIndexField, OnMasterIndexChanged);
            AddRow(_boneSection, "ボーンIdx",   out _boneIndexLabel);
            AddDropdownRow(_boneSection, "親ボーン", out _parentBoneDropdown, OnParentBoneChanged);
            _boneSection.Add(MakeSecLabel("ワールド座標"));
            AddRow(_boneSection, "位置",        out _worldPosLabel);

            root.Add(_boneSection);

            // ── TRS ─────────────────────────────────────────────────
            root.Add(MakeSep());
            root.Add(MakeSecLabel("位置"));
            AddXYZFields(root, "pos", out _posX, out _posY, out _posZ);
            RegTF(_posX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_posY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_posZ, SetBoneTransformValueCommand.Field.PositionZ);

            root.Add(MakeSecLabel("回転 (°)"));
            AddXYZFields(root, "rot", out _rotX, out _rotY, out _rotZ);
            RegTF(_rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegTF(_rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegTF(_rotZ, SetBoneTransformValueCommand.Field.RotationZ);
            _rotSliderX = MakeRotSlider(); root.Add(_rotSliderX);
            _rotSliderY = MakeRotSlider(); root.Add(_rotSliderY);
            _rotSliderZ = MakeRotSlider(); root.Add(_rotSliderZ);
            RegRotSlider(_rotSliderX, _rotX, SetBoneTransformValueCommand.Field.RotationX);
            RegRotSlider(_rotSliderY, _rotY, SetBoneTransformValueCommand.Field.RotationY);
            RegRotSlider(_rotSliderZ, _rotZ, SetBoneTransformValueCommand.Field.RotationZ);

            root.Add(MakeSecLabel("スケール"));
            AddXYZFields(root, "scl", out _sclX, out _sclY, out _sclZ);
            RegTF(_sclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_sclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_sclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            // IgnorePose
            _ignorePoseRow = new VisualElement();
            _ignorePoseRow.Add(MakeSep());
            _ignorePoseToggle = new Toggle("姿勢無視(アーマチャ)");
            _ignorePoseToggle.style.color = new StyleColor(Color.white);
            _ignorePoseToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var indices = GetTargetIndices();
                if (indices.Length > 0)
                    SendCommand(new SetIgnorePoseCommand(GetModelIndex?.Invoke() ?? 0, indices, e.newValue));
            });
            _ignorePoseRow.Add(_ignorePoseToggle);
            root.Add(_ignorePoseRow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 6;
            root.Add(_statusLabel);
        }

        // ================================================================
        // スコープ切り替え
        // ================================================================

        private void SetScope(SubPanelScope scope)
        {
            _scope = scope;
            UpdateTabHighlight();
            Refresh();
        }

        private void UpdateTabHighlight()
        {
            void Style(Button b, bool active)
            {
                if (b == null) return;
                b.style.backgroundColor = new StyleColor(
                    active ? new Color(0.25f, 0.45f, 0.7f) : new Color(0.2f, 0.2f, 0.2f));
                b.style.color = new StyleColor(Color.white);
            }
            Style(_tabBones,  _scope == SubPanelScope.BonesOnly);
            Style(_tabMeshes, _scope == SubPanelScope.MeshesOnly);
        }

        // ================================================================
        // 対象インデックス取得（MirrorSide 除外）
        // ================================================================

        private int[] GetTargetIndices()
        {
            var model = GetModel?.Invoke();
            if (model == null) return Array.Empty<int>();

            IEnumerable<int> raw;
            switch (_scope)
            {
                case SubPanelScope.BonesOnly:
                    raw = model.SelectedBoneIndices;
                    break;
                case SubPanelScope.MeshesOnly:
                    raw = model.SelectedDrawableMeshIndices;
                    break;
                default:
                    raw = model.SelectedBoneIndices
                        .Concat(model.SelectedDrawableMeshIndices
                            .Where(i => !model.SelectedBoneIndices.Contains(i)));
                    break;
            }

            return raw.Where(i =>
            {
                var mc = model.GetMeshContext(i);
                return mc != null && mc.Type != MeshType.MirrorSide;
            }).ToArray();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            // ObjectMoveSettings -> チェックボックス同期
            // 他経路 (Editor 拡張 UI 等) で値が変わっている可能性があるので
            // Refresh のたびに読み直す。_suppressMoveSettings で循環更新を防止。
            var moveSettings = GetObjectMoveSettings?.Invoke();
            if (moveSettings != null)
            {
                _suppressMoveSettings = true;
                try
                {
                    _toggleMoveWithChildren?.SetValueWithoutNotify(moveSettings.MoveWithChildren);
                    _toggleModeA?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind);
                    _toggleModeB?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.SkinBakeRebind);
                    _toggleModeC?.SetValueWithoutNotify(moveSettings.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer);
                }
                finally { _suppressMoveSettings = false; }
            }

            // スコープからピックフィルタを駆動（チェックボックス廃止に伴い一本化）
            if (moveSettings != null)
            {
                bool boneScope = _scope == SubPanelScope.BonesOnly;
                moveSettings.PickBones         = boneScope;
                moveSettings.PickMeshesNoSkin  = !boneScope;
                moveSettings.PickMeshesSkinned = false;
            }

            var model = GetModel?.Invoke();

            bool showBoneSection = _scope != SubPanelScope.MeshesOnly;
            bool showIgnorePose  = _scope != SubPanelScope.BonesOnly;

            if (_boneSection    != null) _boneSection.style.display    = showBoneSection ? DisplayStyle.Flex : DisplayStyle.None;
            if (_ignorePoseRow  != null) _ignorePoseRow.style.display  = showIgnorePose  ? DisplayStyle.Flex : DisplayStyle.None;

            // 「ボーンポーズ」セクションはモードC（ポーズ一時）専用。A/Bでは常に非表示（モデル有無に依存しない）。
            bool poseModeC = (GetObjectMoveSettings?.Invoke())?.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer;
            if (_bonePoseSection != null)
                _bonePoseSection.style.display = poseModeC ? DisplayStyle.Flex : DisplayStyle.None;

            // ボーン専用の移動オプション（子を一緒に移動・A/B/Cモード）はメッシュスコープでは非表示
            if (_moveOptionsSection != null)
                _moveOptionsSection.style.display = (_scope == SubPanelScope.MeshesOnly) ? DisplayStyle.None : DisplayStyle.Flex;

            if (model == null)
            {
                SetWarning("モデルがありません");
                SetTRSEnabled(false);
                return;
            }

            if (showBoneSection)
                RefreshBoneSection(model);

            var indices = GetTargetIndices();

            if (indices.Length == 0)
            {
                SetWarning("");
                _selectionCountLabel.text = ScopeEmptyMessage();
                SetTRSEnabled(false);
                _statusLabel.text = StatusText(model);
                return;
            }

            SetWarning("");
            _selectionCountLabel.text = $"{indices.Length} 項目選択中";
            SetTRSEnabled(true);

            // TRS 同期
            _suppressTRS = true;
            var mc0 = model.GetMeshContext(indices[0]);
            var bt0 = mc0?.BoneTransform;
            var mvs = GetObjectMoveSettings?.Invoke();
            bool poseMode = mvs != null && mvs.MoveMode == Poly_Ling.Tools.BoneMoveMode.PoseLayer;
            if (poseMode)
            {
                // モードC: ポーズ層 "Manual" の差分を表示（0＝ポーズ無し）
                var layer = mc0?.BonePoseData?.GetLayer("Manual");
                Vector3 pRot = layer != null ? NormEuler180(layer.DeltaRotation.eulerAngles) : Vector3.zero;
                Vector3 pPos = layer != null ? layer.DeltaPosition : Vector3.zero;
                SF(_posX, pPos.x); SF(_posY, pPos.y); SF(_posZ, pPos.z);
                SF(_rotX, pRot.x); SF(_rotY, pRot.y); SF(_rotZ, pRot.z);
                SS(_rotSliderX, pRot.x); SS(_rotSliderY, pRot.y); SS(_rotSliderZ, pRot.z);
                SF(_sclX, bt0?.Scale.x ?? 1f); SF(_sclY, bt0?.Scale.y ?? 1f); SF(_sclZ, bt0?.Scale.z ?? 1f);
            }
            else if (bt0 != null)
            {
                SF(_posX, bt0.Position.x); SF(_posY, bt0.Position.y); SF(_posZ, bt0.Position.z);
                SF(_rotX, bt0.Rotation.x); SF(_rotY, bt0.Rotation.y); SF(_rotZ, bt0.Rotation.z);
                SS(_rotSliderX, bt0.Rotation.x); SS(_rotSliderY, bt0.Rotation.y); SS(_rotSliderZ, bt0.Rotation.z);
                SF(_sclX, bt0.Scale.x);    SF(_sclY, bt0.Scale.y);    SF(_sclZ, bt0.Scale.z);
            }
            else
            {
                SF(_posX,0); SF(_posY,0); SF(_posZ,0);
                SF(_rotX,0); SF(_rotY,0); SF(_rotZ,0);
                SS(_rotSliderX,0); SS(_rotSliderY,0); SS(_rotSliderZ,0);
                SF(_sclX,1); SF(_sclY,1); SF(_sclZ,1);
            }

            if (_ignorePoseToggle != null && showIgnorePose)
                _ignorePoseToggle.SetValueWithoutNotify(
                    model.GetMeshContext(indices[0])?.IgnorePoseInArmature ?? false);

            _suppressTRS = false;
            _statusLabel.text = StatusText(model);
        }

        private void RefreshBoneSection(ModelContext model)
        {
            if (_boneDropdown != null)
            {
                _suppressBoneDropdown = true;
                var bones = model.Bones;
                var choices = new List<string>();
                if (bones != null) foreach (var e in bones) choices.Add(e.Name);
                _boneDropdown.choices = choices;

                int dropIdx = -1;
                if (model.HasBoneSelection && bones != null)
                {
                    int dropFirst = model.SelectedBoneIndices[0];
                    for (int i = 0; i < bones.Count; i++)
                        if (bones[i].MasterIndex == dropFirst) { dropIdx = i; break; }
                }
                _boneDropdown.index = dropIdx;
                _suppressBoneDropdown = false;
            }

            bool hasBone = model.HasBoneSelection;
            _btnReset?.SetEnabled(hasBone);
            _btnFocus?.SetEnabled(hasBone);

            if (!hasBone)
            {
                if (_boneNameLabel != null) _boneNameLabel.text = "";
                return;
            }

            int first = model.SelectedBoneIndices[0];
            var ctx   = model.GetMeshContext(first);

            if (_bonePoseActiveToggle != null)
                _bonePoseActiveToggle.SetValueWithoutNotify(ctx?.BonePoseData?.IsActive ?? false);

            if (_boneNameLabel    != null) _boneNameLabel.text    = ctx?.Name ?? "(no name)";
            _suppressBoneEdit = true;
            if (_masterIndexField != null) _masterIndexField.SetValueWithoutNotify(first);
            int boneIdx = model.TypedIndices?.MasterToBoneIndex(first) ?? -1;
            if (_boneIndexLabel   != null) _boneIndexLabel.text   = boneIdx >= 0 ? boneIdx.ToString() : "-";
            RefreshParentDropdown(model, first);
            _suppressBoneEdit = false;
            if (_worldPosLabel != null && ctx != null)
            {
                var wm = ctx.WorldMatrix;
                _worldPosLabel.text = $"({wm.m03:F4}, {wm.m13:F4}, {wm.m23:F4})";
            }
        }

        // ================================================================
        // ボーン並べ替え / 親変更（既存 ReorderMeshesCommand を発行）
        // ================================================================

        /// <summary>親ボーン Dropdown の選択肢と現在値を更新する。</summary>
        private void RefreshParentDropdown(ModelContext model, int targetMaster)
        {
            if (_parentBoneDropdown == null) return;

            _parentChoiceMasters.Clear();
            var choices = new List<string> { "(なし)" };
            _parentChoiceMasters.Add(-1);

            foreach (var e in model.Bones)
            {
                int m = e.MasterIndex;
                if (m == targetMaster) continue;                    // 自身は親にできない
                if (IsDescendant(model, targetMaster, m)) continue; // 子孫は親にできない（循環防止）
                var c = model.GetMeshContext(m);
                choices.Add($"{c?.Name ?? "-"} [{m}]");
                _parentChoiceMasters.Add(m);
            }

            _parentBoneDropdown.choices = choices;

            int curParent = model.GetMeshContext(targetMaster)?.HierarchyParentIndex ?? -1;
            int sel = _parentChoiceMasters.IndexOf(curParent);
            if (sel < 0) sel = 0;
            _parentBoneDropdown.SetValueWithoutNotify(choices[sel]);
        }

        private void OnMasterIndexChanged(ChangeEvent<int> evt)
        {
            if (_suppressBoneEdit) return;
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            MoveBone(model, model.SelectedBoneIndices[0], newMaster: evt.newValue, newParentMaster: null);
        }

        private void OnParentBoneChanged(ChangeEvent<string> evt)
        {
            if (_suppressBoneEdit) return;
            var model = GetModel?.Invoke();
            if (model == null || !model.HasBoneSelection) return;
            int idx = _parentBoneDropdown.index;
            if (idx < 0 || idx >= _parentChoiceMasters.Count) return;
            MoveBone(model, model.SelectedBoneIndices[0], newMaster: null, newParentMaster: _parentChoiceMasters[idx]);
        }

        /// <summary>nodeMaster が ancestorMaster の子孫かどうか（HierarchyParentIndex を上に辿る）。</summary>
        private static bool IsDescendant(ModelContext model, int ancestorMaster, int nodeMaster)
        {
            int cur = model.GetMeshContext(nodeMaster)?.HierarchyParentIndex ?? -1;
            int guard = 0;
            while (cur >= 0 && guard++ < 4096)
            {
                if (cur == ancestorMaster) return true;
                cur = model.GetMeshContext(cur)?.HierarchyParentIndex ?? -1;
            }
            return false;
        }

        /// <summary>
        /// 対象ボーン（＋子孫サブツリー）を移動して ReorderMeshesCommand を発行する。
        /// newMaster 指定時: 親を維持しつつ親の範囲内でマスターIdx位置へ（親は超えない）。
        /// newParentMaster 指定時: 新親の子末尾へ。各パネル反映は Dispatch 側の通知に委ねる。
        /// </summary>
        private void MoveBone(ModelContext model, int target, int? newMaster, int? newParentMaster)
        {
            // 現在のボーン順（master index）と depth / parent
            var order = new List<int>();
            foreach (var e in model.Bones) order.Add(e.MasterIndex);
            int n = order.Count;
            if (n == 0) return;

            var depth  = new Dictionary<int, int>(n);
            var parent = new Dictionary<int, int>(n);
            foreach (var m in order)
            {
                var c = model.GetMeshContext(m);
                depth[m]  = c?.Depth ?? 0;
                parent[m] = c?.HierarchyParentIndex ?? -1;
            }

            int tPos = order.IndexOf(target);
            if (tPos < 0) return;
            int tDepth = depth[target];

            // 対象サブツリー範囲: tPos から subEnd の手前まで（tDepth より深い連続範囲）
            int subEnd = tPos + 1;
            while (subEnd < n && depth[order[subEnd]] > tDepth) subEnd++;
            var subtree = order.GetRange(tPos, subEnd - tPos);

            // 対象サブツリーを除いた残り
            var rest = new List<int>(order);
            rest.RemoveRange(tPos, subEnd - tPos);

            int appliedParent = parent[target];
            int appliedDepth  = tDepth;
            int insertPos;

            if (newParentMaster.HasValue)
            {
                int np = newParentMaster.Value;
                if (np == target || IsDescendant(model, target, np)) { Refresh(); return; }
                appliedParent = np;
                appliedDepth  = np >= 0
                    ? ((depth.TryGetValue(np, out var dp) ? dp : (model.GetMeshContext(np)?.Depth ?? 0)) + 1)
                    : 0;

                if (np < 0)
                {
                    insertPos = rest.Count;
                }
                else
                {
                    int npPos = rest.IndexOf(np);
                    if (npPos < 0) { Refresh(); return; }
                    int e = npPos + 1;
                    int npDepth = depth[np];
                    while (e < rest.Count && depth[rest[e]] > npDepth) e++;
                    insertPos = e;
                }
            }
            else
            {
                // マスターIdx変更: 親（維持）の範囲内にクランプ（親は超えない）
                int p = parent[target];
                int lo, hi;
                if (p < 0) { lo = 0; hi = rest.Count; }
                else
                {
                    int pPos = rest.IndexOf(p);
                    if (pPos < 0) { Refresh(); return; }
                    lo = pPos + 1;
                    int e = pPos + 1;
                    int pDepth = depth[p];
                    while (e < rest.Count && depth[rest[e]] > pDepth) e++;
                    hi = e;
                }
                // newMaster を rest 上の挿入位置に変換（newMaster 未満の master 数）
                int want = 0;
                foreach (var m in rest) { if (m < newMaster.Value) want++; else break; }
                insertPos = Mathf.Clamp(want, lo, hi);
            }

            int depthDelta = appliedDepth - tDepth;

            var newOrder = new List<int>(rest);
            newOrder.InsertRange(insertPos, subtree);

            var entries = new ReorderMeshesCommand.ReorderEntry[newOrder.Count];
            for (int i = 0; i < newOrder.Count; i++)
            {
                int m   = newOrder[i];
                int d   = depth[m];
                int par = parent[m];
                if (m == target)
                {
                    d   = appliedDepth;
                    par = appliedParent;
                }
                else if (subtree.Contains(m))
                {
                    d = depth[m] + depthDelta;   // 子孫は相対深さ維持、親は不変
                }
                entries[i] = new ReorderMeshesCommand.ReorderEntry
                {
                    MasterIndex          = m,
                    NewDepth             = d,
                    NewParentMasterIndex = par,
                };
            }

            int modelIndex = GetModelIndex?.Invoke() ?? 0;
            SendCommand(new ReorderMeshesCommand(modelIndex, MeshCategory.Bone, entries));
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
                if (ctx.BonePoseData == null) { ctx.BonePoseData = new BonePoseData(); ctx.BonePoseData.IsActive = true; }
                beforeSnapshots[idx] = ctx.BonePoseData.CreateSnapshot();
                contexts.Add((idx, ctx));
            }
            if (contexts.Count == 0) return;

            foreach (var (_, ctx) in contexts) { ctx.BonePoseData.ClearAllLayers(); ctx.BonePoseData.SetDirty(); }

            var undo = GetUndoController?.Invoke();
            if (undo != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var (idx, ctx) in contexts)
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = idx,
                        OldSnapshot = beforeSnapshots.TryGetValue(idx, out var b) ? b : (BonePoseDataSnapshot?)null,
                        NewSnapshot = ctx.BonePoseData.CreateSnapshot(),
                    });
                {
                    string __dbgDesc = "ボーンポーズリセット";
                    UnityEngine.Debug.Log("[UndoDbg] MeshList.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                    undo.MeshListStack.Record(record, __dbgDesc);
                }
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
            var ctx = model.GetMeshContext(model.SelectedBoneIndices[0]);
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
                var indices = GetTargetIndices();
                if (indices.Length == 0) return;
                int modelIdx = GetModelIndex?.Invoke() ?? 0;
                SendCommand(new BeginBoneTransformSliderDragCommand(modelIdx, indices) { Mode = GetObjectMoveSettings?.Invoke()?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind });
                SendCommand(new SetBoneTransformValueCommand(modelIdx, indices, field, e.newValue));
                SendCommand(new EndBoneTransformSliderDragCommand(modelIdx, "TRS変更"));
                OnRepaint?.Invoke();
            });
        }

        private bool _trsDragOpen;

        private void OpenTrsDrag()
        {
            if (_trsDragOpen) return;
            var indices = GetTargetIndices();
            if (indices.Length == 0) return;
            SendCommand(new BeginBoneTransformSliderDragCommand(GetModelIndex?.Invoke() ?? 0, indices)
                { Mode = GetObjectMoveSettings?.Invoke()?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind });
            _trsDragOpen = true;
        }

        private void CloseTrsDrag(string desc)
        {
            if (!_trsDragOpen) return;
            _trsDragOpen = false;
            SendCommand(new EndBoneTransformSliderDragCommand(GetModelIndex?.Invoke() ?? 0, desc));
        }

        private static Vector3 NormEuler180(Vector3 e)
            => new Vector3(NormAngle180(e.x), NormAngle180(e.y), NormAngle180(e.z));

        private static float NormAngle180(float a)
        {
            a %= 360f;
            if (a > 180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        private void RegRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field field)
        {
            // UIToolkit の Slider は内部ドラッガーが bubble 段階の PointerDown を消費するため、
            // capture 段階(TrickleDown)で拾う。さらに値変更時にも遅延オープンして確実に Begin を送る。
            s.RegisterCallback<PointerDownEvent>(_ => OpenTrsDrag(), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerUpEvent>(_ => CloseTrsDrag("回転変更"), TrickleDown.TrickleDown);
            s.RegisterCallback<PointerCaptureOutEvent>(_ => CloseTrsDrag("回転変更"));
            s.RegisterValueChangedCallback(e =>
            {
                if (_suppressTRS) return;
                var indices = GetTargetIndices();
                if (indices.Length == 0) return;
                OpenTrsDrag();
                SendCommand(new SetBoneTransformValueCommand(GetModelIndex?.Invoke() ?? 0, indices, field, e.newValue));
                if (f != null) { _suppressTRS = true; SF(f, e.newValue); _suppressTRS = false; }
                OnRepaint?.Invoke();
            });
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void SetTRSEnabled(bool enabled)
        {
            _posX?.SetEnabled(enabled); _posY?.SetEnabled(enabled); _posZ?.SetEnabled(enabled);
            _rotX?.SetEnabled(enabled); _rotY?.SetEnabled(enabled); _rotZ?.SetEnabled(enabled);
            _rotSliderX?.SetEnabled(enabled); _rotSliderY?.SetEnabled(enabled); _rotSliderZ?.SetEnabled(enabled);
            _sclX?.SetEnabled(enabled); _sclY?.SetEnabled(enabled); _sclZ?.SetEnabled(enabled);
            _ignorePoseToggle?.SetEnabled(enabled);
        }

        private void SetWarning(string text)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = text;
            _warningLabel.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private string ScopeEmptyMessage() => _scope switch
        {
            SubPanelScope.BonesOnly  => "未選択 — ボーンを選択してください",
            SubPanelScope.MeshesOnly => "未選択 — 描画メッシュを選択してください",
            _                        => "未選択 — オブジェクトを選択してください",
        };

        private string StatusText(ModelContext model) => _scope switch
        {
            SubPanelScope.BonesOnly  => $"Bones: {model.BoneCount}  Selected: {model.SelectedBoneIndices.Count}",
            SubPanelScope.MeshesOnly => $"Meshes: {model.DrawableCount}  Selected: {model.SelectedDrawableMeshIndices.Count}",
            _                        => $"Bones: {model.BoneCount}  Meshes: {model.DrawableCount}",
        };

        private static void AddXYZFields(VisualElement parent, string prefix,
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

        private static FloatField MakeFloatField(string name, string label)
        {
            var f = new FloatField(label) { name = name };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.style.color       = new StyleColor(Color.black);
            return f;
        }

        private static Slider MakeRotSlider()
        {
            var s = new Slider(-180f, 180f);
            s.style.marginBottom = 2;
            return s;
        }

        private static void SF(FloatField f, float v)
        { if (f != null) f.SetValueWithoutNotify((float)Math.Round(v, 4)); }

        private static void SS(Slider s, float v)
        { if (s != null) s.SetValueWithoutNotify(Mathf.Clamp(v, -180f, 180f)); }

        private static Button MakeScopeTab(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height        = 22;
            b.style.fontSize      = 10;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            b.style.marginRight   = 1;
            return b;
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

        private void AddIntRow(VisualElement parent, string labelText, out IntegerField field,
                               EventCallback<ChangeEvent<int>> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var f = new IntegerField();
            f.style.flexGrow = 1;
            f.style.fontSize = 10;
            f.RegisterValueChangedCallback(onChange);
            row.Add(key); row.Add(f);
            parent.Add(row);
            field = f;
        }

        private void AddDropdownRow(VisualElement parent, string labelText, out DropdownField field,
                                    EventCallback<ChangeEvent<string>> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var key = new Label(labelText + ": ");
            key.style.width    = 76;
            key.style.color    = new StyleColor(Color.white);
            key.style.fontSize = 10;
            var d = new DropdownField();
            d.style.flexGrow = 1;
            d.style.fontSize = 10;
            d.RegisterValueChangedCallback(onChange);
            row.Add(key); row.Add(d);
            parent.Add(row);
            field = d;
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
