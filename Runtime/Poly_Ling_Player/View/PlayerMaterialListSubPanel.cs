// PlayerMaterialListSubPanel.cs
// MaterialListPanel の Player 版サブパネル。
// ObjectField は Runtime 非対応のため TextField（名前表示のみ）で代替。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerMaterialListSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>       GetModel;
        public Func<ToolContext>        GetToolContext;
        public Action                   OnRepaint;

        // ── UI ────────────────────────────────────────────────────────────
        private Label         _countLabel;
        private Label         _currentLabel;
        private ScrollView    _list;
        private Toggle        _autoDefaultToggle;
        private VisualElement _applySection;
        private Button        _btnApply;
        private Label         _selInfoLabel;
        private Label         _statusLabel;

        // ── Build ─────────────────────────────────────────────────────────
        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("マテリアルリスト"));

            var infoRow = new VisualElement();
            infoRow.style.flexDirection = FlexDirection.Row;
            infoRow.style.marginBottom  = 2;
            _countLabel   = new Label("0 slots"); _countLabel.style.flexGrow = 1; _countLabel.style.fontSize = 10;
            _currentLabel = new Label();          _currentLabel.style.fontSize = 10;
            _currentLabel.style.color = new StyleColor(new Color(0.6f, 0.8f, 0.6f));
            infoRow.Add(_countLabel); infoRow.Add(_currentLabel);
            root.Add(infoRow);

            _list = new ScrollView();
            _list.style.minHeight = 60;
            _list.style.maxHeight = 180;
            _list.style.marginBottom = 4;
            root.Add(_list);

            // Add button
            var addBtn = new Button(OnAdd) { text = "+ スロット追加" };
            addBtn.style.marginBottom = 4;
            root.Add(addBtn);

            // AutoDefault toggle
            _autoDefaultToggle = new Toggle("AutoDefault") { value = false };
            _autoDefaultToggle.style.marginBottom = 4;
            _autoDefaultToggle.RegisterValueChangedCallback(e =>
            {
                var m = GetModel?.Invoke(); if (m == null) return;
                var tc = GetToolContext?.Invoke();
                var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
                m.AutoSetDefaultMaterials = e.newValue;
                RecordChange(before, "AutoDefault Materials");
            });
            root.Add(_autoDefaultToggle);

            var setDefaultBtn = new Button(OnSetDefault) { text = "現在をデフォルトに設定" };
            setDefaultBtn.style.marginBottom = 4;
            root.Add(setDefaultBtn);

            // Apply to selection section
            _applySection = new VisualElement();
            _applySection.style.display = DisplayStyle.None;
            _selInfoLabel = new Label(); _selInfoLabel.style.fontSize = 10;
            _selInfoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _applySection.Add(_selInfoLabel);
            _btnApply = new Button(OnApplyToSelection) { text = "Apply to Selection" };
            _applySection.Add(_btnApply);
            root.Add(_applySection);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        // ── Refresh ───────────────────────────────────────────────────────
        public void Refresh()
        {
            var model = GetModel?.Invoke();
            if (_list == null) return;
            _list.Clear();

            if (model == null)
            {
                if (_countLabel != null) _countLabel.text = "0 slots";
                return;
            }

            int count = model.MaterialCount;
            if (_countLabel != null) _countLabel.text = $"{count} slots";
            if (_currentLabel != null) _currentLabel.text = $"Current: [{model.CurrentMaterialIndex}]";
            if (_autoDefaultToggle != null) _autoDefaultToggle.SetValueWithoutNotify(model.AutoSetDefaultMaterials);

            for (int i = 0; i < count; i++)
                _list.Add(MakeRow(model, i));

            // Apply section: 面選択があれば表示
            var tc = GetToolContext?.Invoke();
            var sel = tc?.SelectionState;
            bool hasFace = sel != null && sel.Faces.Count > 0;
            if (_applySection != null) _applySection.style.display = hasFace ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasFace)
            {
                if (_selInfoLabel != null) _selInfoLabel.text = $"{sel.Faces.Count} 面選択中";
                if (_btnApply != null) _btnApply.text = $"Apply [{model.CurrentMaterialIndex}] to Selection";
            }
        }

        // ── Row ───────────────────────────────────────────────────────────
        private VisualElement MakeRow(ModelContext model, int index)
        {
            bool isCurrent = (model.CurrentMaterialIndex == index);
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            if (isCurrent)
                row.style.backgroundColor = new StyleColor(new Color(0.2f, 0.35f, 0.5f));

            int ci = index;
            var selBtn = new Button(() => { OnSelectSlot(ci); }) { text = isCurrent ? "●" : "○" };
            selBtn.style.width = 22; selBtn.style.height = 20;

            var matLabel = new Label($"[{index}] {MatName(model, index)}");
            matLabel.style.flexGrow = 1; matLabel.style.fontSize = 10;
            matLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

            var delBtn = new Button(() => { OnRemoveSlot(ci); }) { text = "×" };
            delBtn.style.width = 22; delBtn.style.height = 20;
            delBtn.SetEnabled(model.MaterialCount > 1);

            row.Add(selBtn); row.Add(matLabel); row.Add(delBtn);
            return row;
        }

        private static string MatName(ModelContext model, int index)
        {
            var mat = model.GetMaterial(index);
            return mat != null ? mat.name : "(None)";
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSelectSlot(int index)
        {
            var m = GetModel?.Invoke(); if (m == null) return;
            m.CurrentMaterialIndex = index;
            AutoUpdateDefault(m);
            NotifyAndRefresh("マテリアルスロット選択");
        }

        private void OnAdd()
        {
            var m = GetModel?.Invoke(); if (m == null) return;
            var tc = GetToolContext?.Invoke();
            var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
            m.AddMaterial(null);
            m.CurrentMaterialIndex = m.MaterialCount - 1;
            RecordChange(before, "Add Material Slot");
            AutoUpdateDefault(m);
            NotifyAndRefresh("スロット追加");
        }

        private void OnRemoveSlot(int index)
        {
            var m = GetModel?.Invoke(); if (m == null || m.MaterialCount <= 1) return;
            var tc = GetToolContext?.Invoke();
            var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
            var mc = m.FirstSelectedMeshContext;
            if (mc?.MeshObject != null)
                foreach (var face in mc.MeshObject.Faces)
                {
                    if (face.MaterialIndex == index)   face.MaterialIndex = 0;
                    else if (face.MaterialIndex > index) face.MaterialIndex--;
                }
            m.RemoveMaterialAt(index);
            if (m.CurrentMaterialIndex >= m.MaterialCount)
                m.CurrentMaterialIndex = m.MaterialCount - 1;
            RecordChange(before, $"Remove Material Slot [{index}]");
            tc?.SyncMesh?.Invoke();
            NotifyAndRefresh("スロット削除");
        }

        private void OnSetDefault()
        {
            var m = GetModel?.Invoke(); if (m == null || m.MaterialCount == 0) return;
            var tc = GetToolContext?.Invoke();
            var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
            m.DefaultMaterials = new List<Material>(m.Materials);
            m.DefaultCurrentMaterialIndex = m.CurrentMaterialIndex;
            RecordChange(before, "Set Default Materials");
            SetStatus("デフォルト設定済み");
            Refresh();
        }

        private void OnApplyToSelection()
        {
            var m = GetModel?.Invoke(); if (m == null) return;
            var tc = GetToolContext?.Invoke();
            var sel = tc?.SelectionState;
            var mc = m.FirstSelectedMeshContext;
            if (mc?.MeshObject == null || sel == null || sel.Faces.Count == 0) return;
            int matIdx = m.CurrentMaterialIndex;
            var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
            bool changed = false;
            foreach (int fi in sel.Faces)
                if (fi >= 0 && fi < mc.MeshObject.FaceCount)
                { mc.MeshObject.Faces[fi].MaterialIndex = matIdx; changed = true; }
            if (changed)
            {
                tc?.SyncMesh?.Invoke();
                RecordChange(before, $"Apply Material [{matIdx}]");
                NotifyAndRefresh($"[{matIdx}] を {sel.Faces.Count} 面に適用");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void RecordChange(MeshObjectSnapshot before, string desc)
        {
            var tc = GetToolContext?.Invoke();
            if (before == null || tc?.UndoController == null) return;
            var after = tc.UndoController.CaptureMeshObjectSnapshot();
            tc.UndoController.RecordTopologyChange(before, after, desc);
        }

        private void AutoUpdateDefault(ModelContext m)
        {
            if (m == null || !m.AutoSetDefaultMaterials || m.MaterialCount == 0) return;
            m.DefaultMaterials = new List<Material>(m.Materials);
            m.DefaultCurrentMaterialIndex = m.CurrentMaterialIndex;
        }

        private void NotifyAndRefresh(string status)
        {
            var m  = GetModel?.Invoke();
            var tc = GetToolContext?.Invoke();
            if (m != null) { m.IsDirty = true; m.OnListChanged?.Invoke(); }
            tc?.SyncMesh?.Invoke(); tc?.Repaint?.Invoke();
            SetStatus(status); Refresh();
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginTop = 2; l.style.marginBottom = 3;
            return l;
        }
    }
}
