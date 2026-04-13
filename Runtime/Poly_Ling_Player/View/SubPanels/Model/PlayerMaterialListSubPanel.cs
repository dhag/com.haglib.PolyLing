// PlayerMaterialListSubPanel.cs
// MaterialListPanel の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    public class PlayerMaterialListSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Action              OnRepaint;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        private void SendCmd(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ── UI ────────────────────────────────────────────────────────────
        private Label         _countLabel;
        private ScrollView    _list;
        private VisualElement _paramSection;
        private VisualElement _applySection;
        private Button        _btnApply;
        private Label         _selInfoLabel;
        private Label         _statusLabel;

        // ── 状態 ──────────────────────────────────────────────────────────
        private int _editingSlot = -1;   // パラメータ展開中のスロット番号（-1=非表示）

        // ── Build ─────────────────────────────────────────────────────────
        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("マテリアルリスト"));

            _countLabel = new Label("0 slots");
            _countLabel.style.fontSize = 10;
            _countLabel.style.marginBottom = 2;
            root.Add(_countLabel);

            _list = new ScrollView();
            _list.style.minHeight = 60;
            _list.style.maxHeight = 180;
            _list.style.marginBottom = 4;
            root.Add(_list);

            // スロット追加ボタン
            var addBtn = new Button(OnAdd) { text = "+ スロット追加" };
            addBtn.style.marginBottom = 4;
            root.Add(addBtn);

            // パラメータ編集エリア（選択時に展開）
            _paramSection = new VisualElement();
            _paramSection.style.display = DisplayStyle.None;
            _paramSection.style.borderTopWidth   = 1;
            _paramSection.style.borderTopColor   = new StyleColor(new Color(0.4f, 0.4f, 0.4f));
            _paramSection.style.paddingTop        = 4;
            _paramSection.style.marginBottom      = 4;
            root.Add(_paramSection);

            // 面適用セクション（面選択時のみ表示）
            _applySection = new VisualElement();
            _applySection.style.display = DisplayStyle.None;
            _selInfoLabel = new Label(); _selInfoLabel.style.fontSize = 10;
            _applySection.Add(_selInfoLabel);
            _btnApply = new Button(OnApplyToSelection) { text = "選択面に適用" };
            _applySection.Add(_btnApply);
            root.Add(_applySection);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(Color.white);
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
                if (_paramSection != null) _paramSection.style.display = DisplayStyle.None;
                return;
            }

            int count = model.MaterialCount;
            if (_countLabel != null) _countLabel.text = $"{count} slots";

            // _editingSlot が範囲外になった場合は閉じる
            if (_editingSlot >= count) _editingSlot = -1;

            for (int i = 0; i < count; i++)
                _list.Add(MakeRow(model, i));

            // パラメータエリア
            RebuildParamSection(model);

            // 面適用セクション
            var tc  = GetToolContext?.Invoke();
            var sel = tc?.SelectionState;
            bool hasFace = sel != null && sel.Faces.Count > 0;
            if (_applySection != null) _applySection.style.display = hasFace ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasFace)
            {
                if (_selInfoLabel != null) _selInfoLabel.text = $"{sel.Faces.Count} 面選択中";
                if (_btnApply    != null) _btnApply.text = $"マテリアル [{model.CurrentMaterialIndex}] を選択面に適用";
            }

            PlayerLayoutRoot.ApplyDarkTheme(_list);
        }

        // ── Row ───────────────────────────────────────────────────────────
        private VisualElement MakeRow(ModelContext model, int index)
        {
            bool isCurrent = (model.CurrentMaterialIndex == index);

            var row = new VisualElement();
            row.style.flexDirection   = FlexDirection.Row;
            row.style.height          = 22;
            row.style.alignItems      = Align.Center;
            row.style.marginBottom    = 2;
            row.style.paddingLeft     = 4;
            row.style.paddingRight    = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            row.style.backgroundColor = isCurrent
                ? new StyleColor(new Color(0.2f, 0.45f, 0.8f))
                : new StyleColor(new Color(0.18f, 0.18f, 0.18f));

            int ci = index;
            row.RegisterCallback<ClickEvent>(_ => OnSelectSlot(ci));

            var nameLabel = new Label($"[{index}]  {MatName(model, index)}");
            nameLabel.style.flexGrow          = 1;
            nameLabel.style.fontSize          = 11;
            nameLabel.style.unityTextAlign    = TextAnchor.MiddleLeft;
            nameLabel.style.overflow          = Overflow.Hidden;
            nameLabel.style.textOverflow      = TextOverflow.Ellipsis;
            nameLabel.style.color = isCurrent
                ? new StyleColor(Color.white)
                : new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            if (isCurrent) nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(nameLabel);

            var delBtn = new Button(() => OnRemoveSlot(ci)) { text = "×" };
            delBtn.style.width  = 22; delBtn.style.height = 18;
            delBtn.style.paddingLeft = delBtn.style.paddingRight =
            delBtn.style.paddingTop  = delBtn.style.paddingBottom = 0;
            delBtn.style.fontSize = 11;
            delBtn.style.color = new StyleColor(new Color(0.9f, 0.4f, 0.4f));
            delBtn.style.borderTopWidth = delBtn.style.borderBottomWidth =
            delBtn.style.borderLeftWidth = delBtn.style.borderRightWidth = 0;
            delBtn.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            delBtn.SetEnabled(model.MaterialCount > 1);
            row.Add(delBtn);

            return row;
        }

        private static string MatName(ModelContext model, int index)
        {
            var mat = model.GetMaterial(index);
            return mat != null ? mat.name : "(None)";
        }

        // ── パラメータ編集エリア ──────────────────────────────────────────
        private void RebuildParamSection(ModelContext model)
        {
            if (_paramSection == null) return;
            _paramSection.Clear();

            if (_editingSlot < 0 || _editingSlot >= model.MaterialCount)
            {
                _paramSection.style.display = DisplayStyle.None;
                return;
            }

            var mat = model.GetMaterial(_editingSlot);
            _paramSection.style.display = DisplayStyle.Flex;

            // ヘッダー
            var header = new Label($"■ スロット [{_editingSlot}]  {(mat != null ? mat.name : "(None)")}");
            header.style.fontSize = 10;
            header.style.color    = new StyleColor(new Color(0.65f, 0.8f, 1f));
            header.style.marginBottom = 4;
            _paramSection.Add(header);

            if (mat == null)
            {
                var none = new Label("マテリアルが割り当てられていません");
                none.style.fontSize = 10;
                none.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                _paramSection.Add(none);
                return;
            }

            // ── メインカラー ──
            if (mat.HasProperty("_BaseColor") || mat.HasProperty("_Color"))
            {
                Color current = mat.HasProperty("_BaseColor")
                    ? mat.GetColor("_BaseColor")
                    : mat.GetColor("_Color");

                _paramSection.Add(ParamLabel("メインカラー"));
                _paramSection.Add(MakeColorRGBARow(mat, current));
            }

            // ── メインテクスチャ ──
            if (mat.HasProperty("_BaseMap") || mat.HasProperty("_MainTex"))
            {
                string propName = mat.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                var tex = mat.GetTexture(propName) as Texture2D;
                string texName = tex != null ? tex.name : "(None)";

                _paramSection.Add(ParamLabel("メインテクスチャ"));
                var texRow = new VisualElement();
                texRow.style.flexDirection = FlexDirection.Row;
                texRow.style.marginBottom  = 4;

                var texLabel = new Label(texName);
                texLabel.style.flexGrow = 1;
                texLabel.style.fontSize = 10;
                texLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                texLabel.style.overflow = Overflow.Hidden;
                texLabel.style.textOverflow = TextOverflow.Ellipsis;
                texLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));

                string capturedProp = propName;
                var browseBtn = new Button(() => OnBrowseTexture(mat, capturedProp, texLabel)) { text = "..." };
                browseBtn.style.width = 28;

                texRow.Add(texLabel);
                texRow.Add(browseBtn);
                _paramSection.Add(texRow);
            }

            // ── Metallic ──
            if (mat.HasProperty("_Metallic"))
            {
                float val = mat.GetFloat("_Metallic");
                _paramSection.Add(ParamLabel($"Metallic  {val:F2}"));
                var slider = new Slider(0f, 1f) { value = val };
                slider.style.marginBottom = 4;
                var labelRef = (Label)_paramSection[_paramSection.childCount - 1]; // 直前のParamLabel
                slider.RegisterValueChangedCallback(e =>
                {
                    mat.SetFloat("_Metallic", e.newValue);
                    labelRef.text = $"Metallic  {e.newValue:F2}";
                    MarkDirty();
                });
                _paramSection.Add(slider);
            }

            // ── Smoothness ──
            string smoothProp = mat.HasProperty("_Smoothness") ? "_Smoothness"
                              : mat.HasProperty("_Glossiness") ? "_Glossiness"
                              : null;
            if (smoothProp != null)
            {
                float val = mat.GetFloat(smoothProp);
                _paramSection.Add(ParamLabel($"Smoothness  {val:F2}"));
                var slider = new Slider(0f, 1f) { value = val };
                slider.style.marginBottom = 4;
                var labelRef = (Label)_paramSection[_paramSection.childCount - 1];
                string capturedProp = smoothProp;
                slider.RegisterValueChangedCallback(e =>
                {
                    mat.SetFloat(capturedProp, e.newValue);
                    labelRef.text = $"Smoothness  {e.newValue:F2}";
                    MarkDirty();
                });
                _paramSection.Add(slider);
            }

            // ── 表面種別（Opaque / Transparent）──
            bool isTransparent = IsTransparent(mat);
            _paramSection.Add(ParamLabel("表面種別"));
            var surfaceRow = new VisualElement();
            surfaceRow.style.flexDirection = FlexDirection.Row;
            surfaceRow.style.marginBottom  = 4;

            var opaqueBtn      = new Button(() => SetSurfaceOpaque(mat))      { text = "Opaque"      };
            var transparentBtn = new Button(() => SetSurfaceTransparent(mat)) { text = "Transparent" };
            StyleSurfaceBtn(opaqueBtn,      !isTransparent);
            StyleSurfaceBtn(transparentBtn, isTransparent);

            surfaceRow.Add(opaqueBtn);
            surfaceRow.Add(transparentBtn);
            _paramSection.Add(surfaceRow);
        }

        // ── RGBA スライダー行 ─────────────────────────────────────────────
        private VisualElement MakeColorRGBARow(Material mat, Color initial)
        {
            var container = new VisualElement();
            container.style.marginBottom = 4;

            // カラープレビュー + チャンネル別スライダー
            var previewRow = new VisualElement();
            previewRow.style.flexDirection = FlexDirection.Row;
            previewRow.style.marginBottom  = 2;

            var preview = new VisualElement();
            preview.style.width           = 32;
            preview.style.height          = 32;
            preview.style.marginRight     = 6;
            preview.style.borderTopWidth  = preview.style.borderBottomWidth =
            preview.style.borderLeftWidth = preview.style.borderRightWidth  = 1;
            preview.style.borderTopColor  = preview.style.borderBottomColor =
            preview.style.borderLeftColor = preview.style.borderRightColor  =
                new StyleColor(new Color(0.5f, 0.5f, 0.5f));
            preview.style.backgroundColor = new StyleColor(initial);
            previewRow.Add(preview);

            // 現在のカラーを保持（クロージャで共有）
            Color[] cur = { initial };

            var sliders = new VisualElement();
            sliders.style.flexGrow = 1;

            string[] chNames  = { "R", "G", "B", "A" };
            Color[]  chColors = {
                new Color(0.9f, 0.3f, 0.3f),
                new Color(0.3f, 0.8f, 0.3f),
                new Color(0.4f, 0.6f, 1.0f),
                new Color(0.7f, 0.7f, 0.7f),
            };
            float[] initVals = { initial.r, initial.g, initial.b, initial.a };

            for (int ch = 0; ch < 4; ch++)
            {
                int capturedCh = ch;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.marginBottom  = 1;

                var lbl = new Label(chNames[ch]);
                lbl.style.width    = 10;
                lbl.style.fontSize = 9;
                lbl.style.color    = new StyleColor(chColors[ch]);

                var slider = new Slider(0f, 1f) { value = initVals[ch] };
                slider.style.flexGrow     = 1;
                slider.style.marginLeft   = 2;
                slider.style.marginRight  = 2;
                slider.RegisterValueChangedCallback(e =>
                {
                    switch (capturedCh)
                    {
                        case 0: cur[0].r = e.newValue; break;
                        case 1: cur[0].g = e.newValue; break;
                        case 2: cur[0].b = e.newValue; break;
                        case 3: cur[0].a = e.newValue; break;
                    }
                    SetMaterialColor(mat, cur[0]);
                    preview.style.backgroundColor = new StyleColor(cur[0]);
                    MarkDirty();
                });

                row.Add(lbl);
                row.Add(slider);
                sliders.Add(row);
            }

            previewRow.Add(sliders);
            container.Add(previewRow);
            return container;
        }

        // ── テクスチャブラウズ ────────────────────────────────────────────
        private void OnBrowseTexture(Material mat, string propName, Label displayLabel)
        {
            string dir = Application.dataPath;
            string path = PLEditorBridge.I.OpenFilePanel("テクスチャ選択", dir, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;

            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    tex.name = Path.GetFileNameWithoutExtension(path);
                    mat.SetTexture(propName, tex);
                    displayLabel.text = tex.name;
                    MarkDirty();
                    SetStatus($"テクスチャ設定: {tex.name}");
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    SetStatus("テクスチャの読み込みに失敗しました");
                }
            }
            catch (Exception e)
            {
                SetStatus($"読み込みエラー: {e.Message}");
            }
        }

        // ── 表面種別 ──────────────────────────────────────────────────────
        private static bool IsTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) return true;
            if (mat.HasProperty("_Mode")    && mat.GetFloat("_Mode")    > 1.5f) return true;
            return false;
        }

        private void SetSurfaceOpaque(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0);
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 0);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 1);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            MarkDirty();
            NotifyAndRefresh("Opaque に設定");
        }

        private void SetSurfaceTransparent(Material mat)
        {
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0);
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 3);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            MarkDirty();
            NotifyAndRefresh("Transparent に設定");
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
        }

        private static void StyleSurfaceBtn(Button btn, bool active)
        {
            btn.style.flexGrow        = 1;
            btn.style.backgroundColor = active
                ? new StyleColor(new Color(0.2f, 0.45f, 0.8f))
                : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            btn.style.color = new StyleColor(Color.white);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSelectSlot(int index)
        {
            var m = GetModel?.Invoke(); if (m == null) return;
            m.CurrentMaterialIndex = index;

            // 同スロットを再クリックでパラメータエリアをトグル
            _editingSlot = (_editingSlot == index) ? -1 : index;

            AutoUpdateDefault(m);
            NotifyAndRefresh(string.Empty);
        }

        private void OnAdd()
        {
            var m = GetModel?.Invoke(); if (m == null) return;
            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            if (_panelContext != null)
            {
                SendCmd(new AddMaterialSlotCommand(modelIdx));
                Refresh();
            }
            else
            {
                var tc = GetToolContext?.Invoke();
                var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
                m.AddMaterial(null);
                m.CurrentMaterialIndex = m.MaterialCount - 1;
                RecordChange(before, "Add Material Slot");
                AutoUpdateDefault(m);
                NotifyAndRefresh("スロット追加");
            }
        }

        private void OnRemoveSlot(int index)
        {
            var m = GetModel?.Invoke(); if (m == null || m.MaterialCount <= 1) return;
            int modelIdx = _getModelIndex?.Invoke() ?? 0;
            if (_panelContext != null)
            {
                if (_editingSlot == index) _editingSlot = -1;
                SendCmd(new RemoveMaterialSlotCommand(modelIdx, index));
                Refresh();
            }
            else
            {
                var tc = GetToolContext?.Invoke();
                var before = tc?.UndoController?.CaptureMeshObjectSnapshot();
                var mc = m.FirstDrawableMeshContext;
                if (mc?.MeshObject != null)
                    foreach (var face in mc.MeshObject.Faces)
                    {
                        if (face.MaterialIndex == index)        face.MaterialIndex = 0;
                        else if (face.MaterialIndex > index)    face.MaterialIndex--;
                    }
                m.RemoveMaterialAt(index);
                if (m.CurrentMaterialIndex >= m.MaterialCount)
                    m.CurrentMaterialIndex = m.MaterialCount - 1;
                if (_editingSlot == index) _editingSlot = -1;
                RecordChange(before, $"Remove Material Slot [{index}]");
                tc?.SyncMesh?.Invoke();
                NotifyAndRefresh("スロット削除");
            }
        }

        private void OnApplyToSelection()
        {
            var m  = GetModel?.Invoke();     if (m == null) return;
            var tc = GetToolContext?.Invoke();
            var sel = tc?.SelectionState;
            var mc = m.FirstDrawableMeshContext;
            if (mc?.MeshObject == null || sel == null || sel.Faces.Count == 0) return;
            int matIdx   = m.CurrentMaterialIndex;
            int modelIdx = _getModelIndex?.Invoke() ?? 0;

            if (_panelContext != null)
            {
                int masterIdx = m.IndexOf(mc);
                SendCmd(new ApplyMaterialToFacesCommand(
                    modelIdx, masterIdx, matIdx, sel.Faces.ToArray()));
                NotifyAndRefresh($"[{matIdx}] を {sel.Faces.Count} 面に適用");
            }
            else
            {
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

        private void MarkDirty()
        {
            var m  = GetModel?.Invoke();
            var tc = GetToolContext?.Invoke();
            if (m != null) m.IsDirty = true;
            tc?.SyncMesh?.Invoke();
            tc?.Repaint?.Invoke();
        }

        private void NotifyAndRefresh(string status)
        {
            var m  = GetModel?.Invoke();
            var tc = GetToolContext?.Invoke();
            if (m != null) { m.IsDirty = true; m.OnListChanged?.Invoke(); }
            tc?.SyncMesh?.Invoke(); tc?.Repaint?.Invoke();
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
            Refresh();
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label ParamLabel(string t)
        {
            var l = new Label(t);
            l.style.fontSize    = 10;
            l.style.marginBottom = 1;
            l.style.color       = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            return l;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color      = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize   = 10;
            l.style.marginTop  = 2;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
