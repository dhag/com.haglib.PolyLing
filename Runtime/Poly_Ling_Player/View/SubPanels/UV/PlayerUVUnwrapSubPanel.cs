// PlayerUVUnwrapSubPanel.cs
// UV展開サブパネル（Player ビルド用）。
// 「投影」タブ：UVUnwrapPanel（投影展開）
// 「LSCM」タブ：LscmUnwrapPanel（選択エッジをシームとしたLSCM展開）
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.UI.Lscm;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public class PlayerUVUnwrapSubPanel
    {
        public Func<ModelContext>   GetModel;
        public Action<PanelCommand> SendCommand;
        public Action               OnRepaint;

        private enum Tab { Projection, Lscm }
        private Tab _tab = Tab.Projection;

        // 投影展開パラメータ
        private ProjectionType _projection = ProjectionType.PlanarXY;
        private float _scale   = 1f;
        private float _offsetU = 0f;
        private float _offsetV = 0f;
        private readonly Button[] _projBtns = new Button[6];

        // LSCM パラメータ
        private bool _includeBoundaryAsSeam = true;
        private int  _maxIterations         = 3000;

        // UI
        private Label         _warningLabel;
        private Label         _targetInfo;
        private Label         _statusLabel;
        private Button        _tabProjBtn, _tabLscmBtn;
        private VisualElement _projContent, _lscmContent;
        private Label         _seamInfo;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            _warningLabel = new Label();
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_warningLabel);

            _targetInfo = new Label();
            _targetInfo.style.color = new StyleColor(Color.white);
            _targetInfo.style.fontSize     = 10;
            _targetInfo.style.marginBottom = 4;
            root.Add(_targetInfo);

            // タブ行
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom  = 4;
            _tabProjBtn = new Button(() => SwitchTab(Tab.Projection)) { text = "投影展開" };
            _tabProjBtn.style.flexGrow = 1;
            _tabLscmBtn = new Button(() => SwitchTab(Tab.Lscm))       { text = "LSCM展開" };
            _tabLscmBtn.style.flexGrow = 1;
            tabRow.Add(_tabProjBtn); tabRow.Add(_tabLscmBtn);
            root.Add(tabRow);

            // 投影展開コンテンツ
            _projContent = new VisualElement();
            root.Add(_projContent);
            BuildProjectionContent(_projContent);

            // LSCM コンテンツ
            _lscmContent = new VisualElement();
            _lscmContent.style.display = DisplayStyle.None;
            root.Add(_lscmContent);
            BuildLscmContent(_lscmContent);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(Color.white);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            UpdateTabColors();
        }

        private void BuildProjectionContent(VisualElement root)
        {
            root.Add(SecLabel("投影モード"));
            string[] labels   = { "PlanarXY", "PlanarXZ", "PlanarYZ", "Box", "Cylindrical", "Spherical" };
            var projTypes = new[]
            {
                ProjectionType.PlanarXY, ProjectionType.PlanarXZ, ProjectionType.PlanarYZ,
                ProjectionType.Box, ProjectionType.Cylindrical, ProjectionType.Spherical
            };
            var row1 = new VisualElement(); row1.style.flexDirection = FlexDirection.Row; row1.style.marginBottom = 2;
            var row2 = new VisualElement(); row2.style.flexDirection = FlexDirection.Row; row2.style.marginBottom = 4;
            for (int i = 0; i < 6; i++)
            {
                int ci = i;
                var b  = new Button(() => { _projection = projTypes[ci]; UpdateProjBtns(); }) { text = labels[i] };
                b.style.flexGrow = 1; b.style.height = 22; b.style.fontSize = 9;
                _projBtns[i] = b;
                (i < 3 ? row1 : row2).Add(b);
            }
            root.Add(row1); root.Add(row2);
            UpdateProjBtns();

            root.Add(SecLabel("パラメータ"));
            root.Add(MkSliderRow("スケール",     0.01f, 10f, _scale,   v => _scale   = v));
            root.Add(MkSliderRow("オフセット U", -2f,   2f,  _offsetU, v => _offsetU = v));
            root.Add(MkSliderRow("オフセット V", -2f,   2f,  _offsetV, v => _offsetV = v));

            var btn = new Button(OnApplyProjection) { text = "UV展開を実行" };
            btn.style.height = 28; btn.style.marginTop = 6; btn.style.fontSize = 11;
            root.Add(btn);
        }

        private void BuildLscmContent(VisualElement root)
        {
            root.Add(SecLabel("LSCM UV展開"));
            root.Add(new HelpBox("選択エッジをSeamとしてLSCM展開します。\nエッジ未選択時はバウンダリのみ使用します。", HelpBoxMessageType.Info));

            _seamInfo = new Label();
            _seamInfo.style.color = new StyleColor(Color.white);
            _seamInfo.style.fontSize     = 10;
            _seamInfo.style.marginTop    = 4;
            _seamInfo.style.marginBottom = 4;
            root.Add(_seamInfo);

            var boundaryToggle = new Toggle("バウンダリをSeamに含める") { value = _includeBoundaryAsSeam };
            boundaryToggle.style.color = new StyleColor(Color.white);
            boundaryToggle.RegisterValueChangedCallback(e => _includeBoundaryAsSeam = e.newValue);
            root.Add(boundaryToggle);

            var maxIterRow = new VisualElement();
            maxIterRow.style.flexDirection = FlexDirection.Row;
            maxIterRow.style.marginTop     = 3;
            maxIterRow.style.marginBottom  = 4;
            var maxIterLbl = new Label("最大反復数");
            maxIterLbl.style.color = new StyleColor(Color.white);
            maxIterLbl.style.width = 80; maxIterLbl.style.fontSize = 10;
            maxIterLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var maxIterField = new IntegerField { value = _maxIterations };
            maxIterField.style.color = new StyleColor(Color.black);
            maxIterField.style.flexGrow = 1;
            maxIterField.RegisterValueChangedCallback(e => _maxIterations = Mathf.Clamp(e.newValue, 100, 50000));
            maxIterRow.Add(maxIterLbl); maxIterRow.Add(maxIterField);
            root.Add(maxIterRow);

            var btn = new Button(OnApplyLscm) { text = "LSCM展開を実行" };
            btn.style.height = 28; btn.style.marginTop = 4; btn.style.fontSize = 11;
            root.Add(btn);
        }

        public void Refresh()
        {
            var model = GetModel?.Invoke();
            if (_warningLabel == null) return;

            if (model == null || model.SelectedMeshIndices.Count == 0)
            {
                _warningLabel.text          = model == null ? "モデルがありません" : "メッシュが未選択です";
                _warningLabel.style.display = DisplayStyle.Flex;
                _targetInfo.text            = "";
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            var names = new List<string>();
            foreach (int idx in model.SelectedMeshIndices)
            {
                var mc = model.GetMeshContext(idx); if (mc != null) names.Add(mc.Name ?? $"[{idx}]");
            }
            _targetInfo.text = "対象: " + string.Join(", ", names);

            if (_tab == Tab.Lscm) RefreshSeamInfo(model);
        }

        private void RefreshSeamInfo(ModelContext model)
        {
            if (_seamInfo == null) return;
            int cnt = model.FirstSelectedMeshContext?.SelectedEdges?.Count ?? 0;
            _seamInfo.text = $"Seam（選択エッジ）: {cnt} 辺";
        }

        private void OnApplyProjection()
        {
            var model = GetModel?.Invoke();
            if (model == null || model.SelectedMeshIndices.Count == 0) { SetStatus("メッシュが未選択です"); return; }
            int[] indices = model.SelectedMeshIndices.ToArray();
            SendCommand?.Invoke(new ApplyUvUnwrapCommand(indices[0], indices, _projection, _scale, _offsetU, _offsetV));
            SetStatus($"UV展開を実行しました ({_projection})");
        }

        private void OnApplyLscm()
        {
            var model = GetModel?.Invoke();
            if (model == null || model.SelectedMeshIndices.Count == 0) { SetStatus("メッシュが未選択です"); return; }
            var mc      = model.FirstSelectedMeshContext;
            var meshObj = mc?.MeshObject;
            if (meshObj == null) { SetStatus("メッシュデータがありません"); return; }

            var seamEdges = mc.SelectedEdges ?? new HashSet<VertexPair>();
            var result    = LscmUnwrapOperation.Execute(meshObj, seamEdges, _includeBoundaryAsSeam,
                                                        Mathf.Clamp(_maxIterations, 100, 50000));
            SetStatus(result.StatusMessage);
            if (result.Success) { mc.UnityMesh = meshObj.ToUnityMesh(); OnRepaint?.Invoke(); }
            RefreshSeamInfo(model);
        }

        private void SwitchTab(Tab tab)
        {
            _tab = tab;
            if (_projContent != null) _projContent.style.display = tab == Tab.Projection ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lscmContent != null) _lscmContent.style.display = tab == Tab.Lscm       ? DisplayStyle.Flex : DisplayStyle.None;
            UpdateTabColors();
            if (tab == Tab.Lscm) { var m = GetModel?.Invoke(); if (m != null) RefreshSeamInfo(m); }
        }

        private void UpdateTabColors()
        {
            var active   = new StyleColor(Color.white);
            var inactive = new StyleColor(StyleKeyword.Null);
            if (_tabProjBtn != null) _tabProjBtn.style.backgroundColor = _tab == Tab.Projection ? active : inactive;
            if (_tabLscmBtn != null) _tabLscmBtn.style.backgroundColor = _tab == Tab.Lscm       ? active : inactive;
        }

        private void UpdateProjBtns()
        {
            var active   = new StyleColor(Color.white);
            var inactive = new StyleColor(StyleKeyword.Null);
            var types    = new[] { ProjectionType.PlanarXY, ProjectionType.PlanarXZ, ProjectionType.PlanarYZ, ProjectionType.Box, ProjectionType.Cylindrical, ProjectionType.Spherical };
            for (int i = 0; i < _projBtns.Length; i++)
                if (_projBtns[i] != null) _projBtns[i].style.backgroundColor = (_projection == types[i]) ? active : inactive;
        }

        private void SetStatus(string t) { if (_statusLabel != null) _statusLabel.text = t; }

        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 2; return l; }

        private static VisualElement MkSliderRow(string label, float min, float max, float val, Action<float> onChange)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label); lb.style.width = 80; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            lb.style.color = new StyleColor(Color.white);
            var sl = new Slider(min, max) { value = val }; sl.style.flexGrow = 1;
            sl.style.color = new StyleColor(Color.white);
            var nf = new FloatField { value = val }; nf.style.width = 50;
            nf.style.color = new StyleColor(Color.black);
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); onChange(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); onChange(v); });
            row.Add(lb); row.Add(sl); row.Add(nf);
            return row;
        }
    }
}
