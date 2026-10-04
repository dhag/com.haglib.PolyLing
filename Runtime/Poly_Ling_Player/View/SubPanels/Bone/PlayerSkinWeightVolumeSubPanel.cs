// PlayerSkinWeightVolumeSubPanel.cs
// スキンW範囲塗りパネル（Player ビルド用）。
// 自ボーン・親ボーン・形（円筒／球）・半径・高さを指定し、範囲内の頂点へ
// 親ボーンと自ボーンのウェイトを直線補間で配分する（SkinWeightVolumePaintCommand）。
// 3D 側は頂点のみ選択（InteractionMode.SkinWeightVolume）。範囲の表示と半径・高さの
// ハンドルは SkinWeightVolumeToolHandler が受け持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    /// <summary>
    /// スキンW範囲塗りパネル。
    ///
    /// ISkinWeightPaintPanel を実装するのは、ウェイトのヒートマップ可視化を流用するため
    /// （PlayerSkinWeightNumericSubPanel と同じ）。ISkinWeightVolumePreview で
    /// 適用前の値を色で見せる。計算は描画側が SkinWeightVolumeOps で行い、
    /// このパネルはモデルを読まない。
    /// </summary>
    public class PlayerSkinWeightVolumeSubPanel : ISkinWeightPaintPanel, ISkinWeightVolumePreview
    {
        // ================================================================
        // ISkinWeightPaintPanel（ウェイト可視化のためだけに実装する）
        // ================================================================

        /// <summary>色で見せるボーン。自ボーン。</summary>
        public int CurrentTargetBone => _selfBone;

        /// <summary>
        /// 常に -1。可視化の対象を「選択中の描画オブジェクト全件」にし、
        /// 適用先（SkinWeightOperations.CollectTargetMeshContexts）と揃える。
        /// </summary>
        public int CurrentTargetMesh => -1;

        public SkinWeightPaintMode CurrentPaintMode    => SkinWeightPaintMode.Replace;
        public float               CurrentBrushRadius  => 0f;
        public float               CurrentStrength     => 0f;
        public FalloffType         CurrentFalloff      => FalloffType.Constant;
        public DistanceMode        CurrentDistanceMode => DistanceMode.Euclidean;
        public float               CurrentWeightValue  => 0f;

        public void NotifyWeightChanged() { }

        // ================================================================
        // ISkinWeightVolumePreview
        // ================================================================

        public bool TryGetVolumePreviewSpec(out SkinWeightVolumeSpec spec)
        {
            spec = CurrentSpec();
            return _preview && _selfBone >= 0;
        }

        /// <summary>現在の入力。</summary>
        public SkinWeightVolumeSpec CurrentSpec() => new SkinWeightVolumeSpec
        {
            SelfBone     = _selfBone,
            ParentBone   = _parentBone,
            Shape        = _shape,
            Radius       = _radius,
            Height       = _height,
            SeparateParentHeight = _separateParent,
            ParentHeight         = _parentHeight,
            SelectedOnly = _selectedOnly,
            AxisMode     = _axisMode,
            ChildBone    = _childBone,
        };

        // ================================================================
        // 外部依存
        // ================================================================

        /// <summary>モデルの窓口（操作経路統一計画.md E）。</summary>
        public Func<Poly_Ling.View.IModelView> GetModel;
        /// <summary>ツールの窓口。読み取り（対象・入力の検査）は "skinWeightVolume" で行う。</summary>
        public IToolSurface Surface;
        private const string Tool = "skinWeightVolume";

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>入力が変わったとき（プレビューの色と範囲の表示を作り直す）。</summary>
        public Action OnSpecChanged;

        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        private void SendCmd(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ================================================================
        // 入力値
        // ================================================================

        private int   _selfBone   = -1;
        /// <summary>-1 は「階層の親」。</summary>
        private int   _parentBone = -1;
        /// <summary>軸の向き。</summary>
        private SkinWeightVolumeAxis _axisMode = SkinWeightVolumeAxis.ParentToSelf;
        /// <summary>-1 は「階層の子」。</summary>
        private int   _childBone  = -1;
        private SkinWeightVolumeShape _shape = SkinWeightVolumeShape.Cylinder;
        private float _radius       = 0f;
        private float _height       = 0f;
        /// <summary>親側の長さを別に指定するか。false なら先端側と同じ。</summary>
        private bool  _separateParent = false;
        private float _parentHeight   = 0f;
        private bool  _selectedOnly = false;
        private bool  _preview      = true;
        private bool  _showVolumeWire  = true;
        private bool  _showSegmentWire = false;

        /// <summary>範囲塗りの範囲（円筒／球）をワイヤで出すか。</summary>
        public bool ShowVolumeWire  => _showVolumeWire;
        /// <summary>区間塗りの範囲（親関節〜自関節の円筒）をワイヤで出すか。</summary>
        public bool ShowSegmentWire => _showSegmentWire;

        // ================================================================
        // UI
        // ================================================================

        // UI 自動操作の ID は "skinWeightVolume.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;

        private readonly List<int> _boneMasterIndices = new List<int>();
        private readonly Dictionary<int, int> _boneParent = new Dictionary<int, int>();

        [UiControl("selfBone", Description = "自ボーン（ウェイトを 50%→100% にするボーン）")]
        private DropdownField _selfDropdown;
        [UiControl("parentBone", Description = "親ボーン（先頭は「階層の親」）")]
        private DropdownField _parentDropdown;
        [UiControl("axisMode", Description = "範囲の軸の向き（親関節→自関節／自関節→子関節）")]
        private DropdownField _axisDropdown;
        [UiControl("childBone", Description = "軸に使う子ボーン（先頭は「階層の子」）。軸が自関節→子関節のときだけ使う")]
        private DropdownField _childDropdown;
        [UiControl("shape", Description = "範囲の形（円筒／球）")]
        private DropdownField _shapeDropdown;
        [UiControl("radius", Description = "半径")]
        private FloatField _radiusField;
        [UiControl("height", Description = "円筒の高さ（自関節から先端まで）")]
        private FloatField _heightField;
        [UiControl("separateParentHeight", Description = "親側（自関節から 0% の点まで）の長さを別に指定する。外すと先端側と同じ")]
        private Toggle _separateParentToggle;
        [UiControl("parentHeight", Description = "親側の長さ（自関節から 0% の点まで）")]
        private FloatField _parentHeightField;
        [UiControl("vertexScope", Description = "対象の頂点（全頂点／選択頂点のみ）")]
        private DropdownField _scopeDropdown;
        [UiControl("preview", Description = "適用後のウェイトを色で表示する")]
        private Toggle _previewToggle;
        [UiControl("showVolumeWire", Description = "範囲塗りの範囲（円筒／球）を黄色のワイヤで表示する")]
        private Toggle _volumeWireToggle;
        [UiControl("showSegmentWire", Description = "区間塗りの範囲（親関節〜自関節の円筒）を水色のワイヤで表示する")]
        private Toggle _segmentWireToggle;

        [UiControl("target", Safety = UiSafety.ReadOnly, Description = "対象")]
        private Label _targetLabel;
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "入力・対象の問題")]
        private Label _warnLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;

        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "範囲内の頂点へ適用する")]
        private Button _applyBtn;

        [UiControl("segmentFill", Safety = UiSafety.SafeWrite, Description = "親関節〜自関節の円筒（半径は上の値）の中の頂点を親ボーン 100% で塗る")]
        private Button _segmentFillBtn;

        private static readonly List<string> ShapeChoices = new List<string> { "円筒", "球" };
        private static readonly List<string> ScopeChoices = new List<string> { "全頂点", "選択頂点のみ" };
        private static readonly List<string> AxisChoices = new List<string> { "親関節→自関節", "自関節→子関節" };
        private const string ParentAuto = "（階層の親）";
        private const string ChildAuto  = "（階層の子）";
        private const string NoneChoice = "（未選択）";

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            AddSectionLabel("スキンW範囲塗り");
            _root.Add(new HelpBox(
                "円筒または球の中の頂点へ、親ボーンと自ボーンのウェイトを配分します。\n" +
                "自関節の手前で自ボーン 0%、自関節で 50%、先端で 100% になるよう直線で変化します。\n" +
                "手前（親側）の長さは先端側と同じです。別にしたいときは「親側を別に指定」を入れます。\n" +
                "範囲外の頂点は変わりません。半径と高さはビューポートのハンドルでも変えられます。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_targetLabel);

            AddSep();

            AddSectionLabel("自ボーン");
            _selfDropdown = new DropdownField(new List<string> { NoneChoice }, 0);
            _selfDropdown.style.color = new StyleColor(Color.white);
            _selfDropdown.RegisterValueChangedCallback(_ =>
            {
                int sel = _selfDropdown.index;
                _selfBone = sel <= 0 ? -1 : _boneMasterIndices[sel - 1];
                InitSizeIfUnset();
                SpecChanged();
            });
            _root.Add(_selfDropdown);

            AddSectionLabel("親ボーン");
            _parentDropdown = new DropdownField(new List<string> { ParentAuto }, 0);
            _parentDropdown.style.color = new StyleColor(Color.white);
            _parentDropdown.RegisterValueChangedCallback(_ =>
            {
                int sel = _parentDropdown.index;
                _parentBone = sel <= 0 ? -1 : _boneMasterIndices[sel - 1];
                SpecChanged();
            });
            _root.Add(_parentDropdown);

            AddSectionLabel("軸の向き");
            _axisDropdown = new DropdownField(AxisChoices, 0);
            _axisDropdown.style.color = new StyleColor(Color.white);
            _axisDropdown.RegisterValueChangedCallback(_ =>
            {
                _axisMode = _axisDropdown.index == 1
                    ? SkinWeightVolumeAxis.SelfToChild : SkinWeightVolumeAxis.ParentToSelf;
                UpdateHeightEnabled();
                SpecChanged();
            });
            _root.Add(_axisDropdown);

            _childDropdown = new DropdownField("子ボーン", new List<string> { ChildAuto }, 0);
            _childDropdown.style.color = new StyleColor(Color.white);
            _childDropdown.RegisterValueChangedCallback(_ =>
            {
                int sel = _childDropdown.index;
                _childBone = sel <= 0 ? -1 : _boneMasterIndices[sel - 1];
                SpecChanged();
            });
            _root.Add(_childDropdown);

            AddSectionLabel("範囲");
            _shapeDropdown = new DropdownField("形", ShapeChoices, 0);
            _shapeDropdown.style.color = new StyleColor(Color.white);
            _shapeDropdown.RegisterValueChangedCallback(_ =>
            {
                _shape = _shapeDropdown.index == 1 ? SkinWeightVolumeShape.Sphere : SkinWeightVolumeShape.Cylinder;
                UpdateHeightEnabled();
                SpecChanged();
            });
            _root.Add(_shapeDropdown);

            _radiusField = new FloatField("半径") { value = _radius };
            _radiusField.RegisterValueChangedCallback(e =>
            {
                _radius = Mathf.Max(0f, e.newValue);
                if (!Mathf.Approximately(_radius, e.newValue)) _radiusField.SetValueWithoutNotify(_radius);
                SpecChanged();
            });
            _root.Add(_radiusField);

            _heightField = new FloatField("高さ") { value = _height };
            _heightField.RegisterValueChangedCallback(e =>
            {
                _height = Mathf.Max(0f, e.newValue);
                if (!Mathf.Approximately(_height, e.newValue)) _heightField.SetValueWithoutNotify(_height);
                SpecChanged();
            });
            _root.Add(_heightField);

            _separateParentToggle = new Toggle("親側を別に指定") { value = _separateParent };
            _separateParentToggle.RegisterValueChangedCallback(e =>
            {
                _separateParent = e.newValue;
                // 入れた直後は今の先端側の長さから始める（範囲が急に変わらないように）。
                if (_separateParent && _parentHeight <= 0f)
                {
                    _parentHeight = CurrentTipLength();
                    _parentHeightField?.SetValueWithoutNotify((float)Math.Round(_parentHeight, 5));
                }
                UpdateHeightEnabled();
                SpecChanged();
            });
            _root.Add(_separateParentToggle);

            _parentHeightField = new FloatField("親側の高さ") { value = _parentHeight };
            _parentHeightField.RegisterValueChangedCallback(e =>
            {
                _parentHeight = Mathf.Max(0f, e.newValue);
                if (!Mathf.Approximately(_parentHeight, e.newValue)) _parentHeightField.SetValueWithoutNotify(_parentHeight);
                SpecChanged();
            });
            _root.Add(_parentHeightField);

            _scopeDropdown = new DropdownField("対象の頂点", ScopeChoices, 0);
            _scopeDropdown.style.color = new StyleColor(Color.white);
            _scopeDropdown.RegisterValueChangedCallback(_ =>
            {
                _selectedOnly = _scopeDropdown.index == 1;
                SpecChanged();
            });
            _root.Add(_scopeDropdown);

            _previewToggle = new Toggle("適用後を色で表示") { value = _preview };
            _previewToggle.RegisterValueChangedCallback(e =>
            {
                _preview = e.newValue;
                SpecChanged();
            });
            _root.Add(_previewToggle);

            _volumeWireToggle = new Toggle("範囲塗りの範囲を表示（黄）") { value = _showVolumeWire };
            _volumeWireToggle.RegisterValueChangedCallback(e =>
            {
                _showVolumeWire = e.newValue;
                SpecChanged();
            });
            _root.Add(_volumeWireToggle);

            _segmentWireToggle = new Toggle("区間塗りの範囲を表示（水色）") { value = _showSegmentWire };
            _segmentWireToggle.RegisterValueChangedCallback(e =>
            {
                _showSegmentWire = e.newValue;
                SpecChanged();
            });
            _root.Add(_segmentWireToggle);

            _warnLabel = InfoLabel();
            _warnLabel.style.whiteSpace = WhiteSpace.Normal;
            _warnLabel.style.color = new StyleColor(new Color(1f, 0.65f, 0.2f));
            _root.Add(_warnLabel);

            var applyBtn = new Button(OnApply) { text = "適用" };
            applyBtn.style.height    = 30;
            applyBtn.style.marginTop = 4;
            _root.Add(applyBtn);
            _applyBtn = applyBtn;

            // 区間塗り：関節と関節の間の中ほどを親ボーン 1 本で埋める（範囲塗りの前に使う）。
            var segBtn = new Button(OnSegmentFill) { text = "親関節〜自関節を親ボーン100%で塗る" };
            segBtn.style.height    = 24;
            segBtn.style.marginTop = 2;
            _root.Add(segBtn);
            _segmentFillBtn = segBtn;

            _statusLabel = InfoLabel();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);

            UpdateHeightEnabled();
            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnApply()
        {
            if (_selfBone < 0) { SetStatus("自ボーンを選んでください。"); return; }

            string err = Surface?.GetString(Tool, "specError") ?? "";
            if (!string.IsNullOrEmpty(err)) { SetStatus(err); return; }

            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            SendCmd(new SkinWeightVolumePaintCommand(
                modelIdx, _selfBone, _parentBone, _shape, _radius, _height, _selectedOnly,
                _separateParent, _parentHeight, _axisMode, _childBone));
            SetStatus("適用しました。");
            OnRepaint?.Invoke();
        }

        /// <summary>ハンドルで半径を変えたとき（SkinWeightVolumeToolHandler から）。</summary>
        public void SetRadiusFromHandle(float v)
        {
            _radius = Mathf.Max(0f, v);
            _radiusField?.SetValueWithoutNotify((float)Math.Round(_radius, 5));
            OnSpecChanged?.Invoke();
        }

        /// <summary>
        /// 区間塗り：親関節〜自関節の円筒の中を親ボーン 100% にする（SkinWeightSegmentFillCommand）。
        /// 半径・対象の頂点・親ボーンは範囲塗りと同じ欄を使う。軸の向き・高さは使わない。
        /// </summary>
        private void OnSegmentFill()
        {
            if (_selfBone < 0) { SetStatus("自ボーン（区間の先の関節）を選んでください。"); return; }
            if (_radius <= 0f) { SetStatus("半径は 0 より大きくしてください。"); return; }

            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            SendCmd(new SkinWeightSegmentFillCommand(
                modelIdx, _selfBone, _parentBone, _radius, _selectedOnly));
            SetStatus("親関節〜自関節を親ボーン 100% で塗りました。");
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// ハンドルで親側の長さを変えたとき（SkinWeightVolumeToolHandler から）。
        /// 親側を別に指定していないときは、先端側（円筒は高さ、球は半径）を変える。
        /// </summary>
        public void SetParentHeightFromHandle(float v)
        {
            if (!_separateParent)
            {
                if (_shape == SkinWeightVolumeShape.Cylinder) SetHeightFromHandle(v);
                else                                          SetRadiusFromHandle(v);
                return;
            }
            _parentHeight = Mathf.Max(0f, v);
            _parentHeightField?.SetValueWithoutNotify((float)Math.Round(_parentHeight, 5));
            OnSpecChanged?.Invoke();
        }

        /// <summary>ハンドルで高さを変えたとき（SkinWeightVolumeToolHandler から）。</summary>
        public void SetHeightFromHandle(float v)
        {
            _height = Mathf.Max(0f, v);
            _heightField?.SetValueWithoutNotify((float)Math.Round(_height, 5));
            OnSpecChanged?.Invoke();
        }

        // ================================================================
        // Refresh
        // ================================================================

        /// <summary>モデルのボーン一覧をドロップダウンへ反映する。</summary>
        public void RefreshBoneList(Poly_Ling.View.IModelView model)
        {
            _boneMasterIndices.Clear();
            _boneParent.Clear();
            var names = new List<string>();

            var bones = model?.BoneList;
            if (bones != null)
            {
                foreach (var entry in bones)
                {
                    string bname = string.IsNullOrEmpty(entry.Name) ? $"Bone_{entry.MasterIndex}" : entry.Name;
                    names.Add($"{bname} [{entry.MasterIndex}]");
                    _boneMasterIndices.Add(entry.MasterIndex);
                    _boneParent[entry.MasterIndex] = entry.HierarchyParentIndex;
                }
            }

            if (_selfBone >= 0 && !_boneMasterIndices.Contains(_selfBone))     _selfBone = -1;
            if (_parentBone >= 0 && !_boneMasterIndices.Contains(_parentBone)) _parentBone = -1;
            if (_childBone >= 0 && !_boneMasterIndices.Contains(_childBone))   _childBone = -1;

            if (_selfDropdown != null)
            {
                var choices = new List<string> { NoneChoice };
                choices.AddRange(names);
                _selfDropdown.choices = choices;
                int i = _selfBone >= 0 ? _boneMasterIndices.IndexOf(_selfBone) + 1 : 0;
                _selfDropdown.SetValueWithoutNotify(choices[i]);
            }
            if (_parentDropdown != null)
            {
                var choices = new List<string> { ParentAuto };
                choices.AddRange(names);
                _parentDropdown.choices = choices;
                int i = _parentBone >= 0 ? _boneMasterIndices.IndexOf(_parentBone) + 1 : 0;
                _parentDropdown.SetValueWithoutNotify(choices[i]);
            }
            if (_childDropdown != null)
            {
                var choices = new List<string> { ChildAuto };
                choices.AddRange(names);
                _childDropdown.choices = choices;
                int i = _childBone >= 0 ? _boneMasterIndices.IndexOf(_childBone) + 1 : 0;
                _childDropdown.SetValueWithoutNotify(choices[i]);
            }
        }

        public void Refresh()
        {
            RefreshBoneList(GetModel?.Invoke());
            RefreshInfo();
        }

        /// <summary>対象と入力の検査結果を表示し直す。</summary>
        public void RefreshInfo()
        {
            var names  = Surface?.Get(Tool, "targetNames", Array.Empty<string>()) ?? Array.Empty<string>();
            var counts = Surface?.Get(Tool, "targetSelectedVertexCounts", Array.Empty<int>()) ?? Array.Empty<int>();

            var parts = new List<string>();
            int selTotal = 0;
            for (int k = 0; k < names.Length; k++)
            {
                int n = k < counts.Length ? counts[k] : 0;
                selTotal += n;
                parts.Add($"{names[k]}({n})");
            }
            if (_targetLabel != null)
                _targetLabel.text = names.Length == 0
                    ? "対象: なし（オブジェクトリストでオブジェクトを選択してください）"
                    : $"対象: {names.Length} 件 — {string.Join(" / ", parts)}　選択頂点 {selTotal}";

            var warns = new List<string>();
            var nonSkinned = Surface?.Get(Tool, "nonSkinnedTargetNames", Array.Empty<string>()) ?? Array.Empty<string>();
            if (nonSkinned.Length > 0)
                warns.Add("スキンドでないため適用できません: " + string.Join(" / ", nonSkinned));
            if (_selfBone >= 0)
            {
                string err = Surface?.GetString(Tool, "specError") ?? "";
                if (!string.IsNullOrEmpty(err)) warns.Add(err);
            }
            if (_selectedOnly && selTotal == 0)
                warns.Add("「選択頂点のみ」ですが頂点が選ばれていません。");
            if (_warnLabel != null) _warnLabel.text = string.Join("\n", warns);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void SpecChanged()
        {
            RefreshInfo();
            OnSpecChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 半径・高さが未設定のとき、親関節〜自関節の長さから初期値を入れる
        /// （高さ＝その長さ、半径＝その 3 割）。一度入れた値は変えない。
        /// </summary>
        private void InitSizeIfUnset()
        {
            if (_selfBone < 0) return;
            if (_radius > 0f && _height > 0f) return;

            float len = Surface?.GetFloat(Tool, "parentLength") ?? 0f;
            if (len <= 0f) return;

            if (_radius <= 0f) { _radius = len * 0.3f; _radiusField?.SetValueWithoutNotify((float)Math.Round(_radius, 5)); }
            if (_height <= 0f) { _height = len;        _heightField?.SetValueWithoutNotify((float)Math.Round(_height, 5)); }
        }

        private void UpdateHeightEnabled()
        {
            _heightField?.SetEnabled(_shape == SkinWeightVolumeShape.Cylinder);
            _parentHeightField?.SetEnabled(_separateParent);
            _childDropdown?.SetEnabled(_axisMode == SkinWeightVolumeAxis.SelfToChild);
        }

        /// <summary>先端側の長さ（円筒は高さ、球は半径）。</summary>
        private float CurrentTipLength()
            => _shape == SkinWeightVolumeShape.Cylinder ? _height : _radius;

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        private void AddSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
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

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color        = new StyleColor(Color.white);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
