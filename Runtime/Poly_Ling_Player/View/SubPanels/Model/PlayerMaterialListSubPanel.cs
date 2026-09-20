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
using Poly_Ling.Materials;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerMaterialListSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        /// <summary>プロジェクトの窓口（操作経路統一計画.md E、M-1）。マテリアルの写しと選択面数を読む。</summary>
        public Func<Poly_Ling.View.IProjectView> GetView;
        /// <summary>ツールの窓口。色・Metallic・Smoothness の操作中プレビューと確定（"materialEdit"。M-2）。</summary>
        public IToolSurface        Surface;
        private const string       Tool = "materialEdit";
        public Action              OnRepaint;

        private Poly_Ling.View.IModelView Model => GetView?.Invoke()?.CurrentModel;

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
        // UI 自動操作の ID は "materialList.<下の Id>"（UiControlAttribute.cs）。
        // マテリアルの行と、選んだマテリアルのパラメータ欄（テクスチャ・スライダー・色・サーフェス）は
        // マテリアルとシェーダーに合わせて作り直すので、固定の項目としては登録しない（Rows）。
        // シェーダーの選択と名前欄はフィールドに持つので登録する。
        [UiControl("count", Safety = UiSafety.ReadOnly, Description = "マテリアル数")]
        private Label         _countLabel;
        [UiControl(Ignore = true, Rows = true)]
        private ScrollView    _list;
        [UiControl("add", Safety = UiSafety.SafeWrite, Description = "新規マテリアルを作成する")]
        private Button        _btnAdd;

        // リスト高さ（下端ドラッグで手動リサイズ）: MeshListSubPanel と同方式
        private float _matListHeight = 180f;
        private const float MatListMinHeight = 60f;
        private const float MatListMaxHeight = 600f;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _paramSection;
        [UiControl(Ignore = true)]
        private VisualElement _applySection;
        [UiControl("applyToSelection", Safety = UiSafety.SafeWrite, Description = "カレントマテリアルを選択面に適用する（面の選択が要る）")]
        private Button        _btnApply;
        [UiControl("selectionInfo", Safety = UiSafety.ReadOnly, Description = "選択面の情報")]
        private Label         _selInfoLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        private const string  TexPathKey = "Material.TexPath";

        [UiControl("shader", Description = "選んだマテリアルのシェーダー（マテリアルを選んだときだけ表示）")]
        private DropdownField _shaderDropdown;
        [UiControl("customShaderName", Description = "シェーダー名（シェーダーが Custom のときだけ表示）")]
        private TextField     _customShaderField;

        // ── 状態 ──────────────────────────────────────────────────────────
        private int _editingSlot = -1;   // パラメータ展開中のスロット番号（-1=非表示）

        // シェーダー候補。ビルドに含まれない種別は Shader.Find が null を返すため、
        // 実際に解決できたものだけを並べる（MaterialDataConverter.GetShader で判定）。
        private static readonly (ShaderType Type, string Label)[] ShaderChoices =
        {
            (ShaderType.URPLit,        "URP / Lit"),
            (ShaderType.URPSimpleLit,  "URP / Simple Lit"),
            (ShaderType.URPUnlit,      "URP / Unlit"),
            (ShaderType.StandardLit,   "Standard (Built-in)"),
            (ShaderType.StandardUnlit, "Unlit / Texture"),
            (ShaderType.MToon,         "VRM / MToon10"),
        };
        private const string CustomShaderLabel = "Custom（名前指定）";

        // ── Build ─────────────────────────────────────────────────────────
        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("マテリアル（質感・色）"));

            _countLabel = new Label("0 slots");
            _countLabel.style.fontSize = 10;
            _countLabel.style.marginBottom = 2;
            root.Add(_countLabel);

            _list = new ScrollView();
            // 高さを height/minHeight/maxHeight とも同値で厳密固定（ドラッグで変更）。
            _list.style.height    = _matListHeight;
            _list.style.minHeight = _matListHeight;
            _list.style.maxHeight = _matListHeight;
            _list.style.marginBottom = 4;
            root.Add(_list);
            AddListResizeHandle(root, _list,
                () => _matListHeight, h => _matListHeight = h, MatListMinHeight, MatListMaxHeight);

            // スロット追加ボタン
            var addBtn = new Button(OnAdd) { text = "+ 新規マテリアルを作成" };
            addBtn.style.marginBottom = 4;
            root.Add(addBtn);
            _btnAdd = addBtn;

            // パラメータ編集エリア（選択時に展開）
            _paramSection = new VisualElement();
            _paramSection.style.display = DisplayStyle.None;
            _paramSection.style.borderTopWidth   = 1;
            _paramSection.style.borderTopColor   = new StyleColor(new Color(0.4f, 0.4f, 0.4f));
            _paramSection.style.paddingTop        = 4;
            _paramSection.style.marginBottom      = 4;
            root.Add(_paramSection);

            // 面適用セクション（常時表示。面未選択時はボタンを無効化してグレーアウト）
            _applySection = new VisualElement();
            _applySection.style.display = DisplayStyle.Flex;
            _selInfoLabel = new Label(); _selInfoLabel.style.fontSize = 10;
            _applySection.Add(_selInfoLabel);
            _btnApply = new Button(OnApplyToSelection) { text = "選択面に適用" };
            _applySection.Add(_btnApply);
            root.Add(_applySection);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);
        }

        // ── 表示同期 ───────────────────────────────────────────────────────
        // パネル表示直後、ハイライト行(CurrentMaterialIndex)と詳細展開(_editingSlot)が
        // 別変数のままだと詳細が出ない。表示時に _editingSlot を現在スロットへ揃える。
        // 折りたたみトグルは Refresh() では触らないため、パネル表示時のみ呼ぶこと。
        public void SyncEditingSlotToCurrent()
        {
            var model = Model;
            if (model == null) { _editingSlot = -1; return; }
            int count = model.MaterialCount;
            if (count <= 0) { _editingSlot = -1; return; }
            int idx = model.CurrentMaterialIndex;
            if (idx < 0 || idx >= count) idx = count - 1;
            _editingSlot = idx;
        }

        // ── Refresh ───────────────────────────────────────────────────────
        public void Refresh()
        {
            var model = Model;
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

            // 面適用セクション（面は編集対象メッシュの選択面。窓口の ActiveMesh は現物を読む）
            int selFaces = model.ActiveMesh?.SelectedFaceCount ?? 0;
            bool hasFace = selFaces > 0;
            // 面未選択でもセクションは表示し、ボタンを無効化(グレーアウト)して存在を示す。
            if (_applySection != null) _applySection.style.display = DisplayStyle.Flex;
            if (_btnApply != null)
            {
                _btnApply.SetEnabled(hasFace);
                _btnApply.text = $"マテリアル [{model.CurrentMaterialIndex}] を選択面に適用";
            }
            if (_selInfoLabel != null)
                _selInfoLabel.text = hasFace ? $"{selFaces} 面選択中" : "面が選択されていません";

            PlayerLayoutRoot.ApplyDarkTheme(_list);
        }

        // ── Row ───────────────────────────────────────────────────────────
        private VisualElement MakeRow(Poly_Ling.View.IModelView model, int index)
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

        private static string MatName(Poly_Ling.View.IModelView model, int index)
        {
            var v = model.GetMaterialSlot(index);
            return v != null && v.HasMaterial ? v.Name : "(None)";
        }

        // ── パラメータ編集エリア ──────────────────────────────────────────
        private void RebuildParamSection(Poly_Ling.View.IModelView model)
        {
            if (_paramSection == null) return;
            _paramSection.Clear();

            if (_editingSlot < 0 || _editingSlot >= model.MaterialCount)
            {
                _paramSection.style.display = DisplayStyle.None;
                return;
            }

            var v = model.GetMaterialSlot(_editingSlot);
            _paramSection.style.display = DisplayStyle.Flex;

            // ヘッダー
            var header = new Label($"■ スロット [{_editingSlot}]  {(v != null && v.HasMaterial ? v.Name : "(None)")}");
            header.style.fontSize = 10;
            header.style.color    = new StyleColor(new Color(0.65f, 0.8f, 1f));
            header.style.marginBottom = 4;
            _paramSection.Add(header);

            if (v == null || !v.HasMaterial)
            {
                var none = new Label("マテリアルが割り当てられていません");
                none.style.fontSize = 10;
                none.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
                _paramSection.Add(none);
                return;
            }

            // ── シェーダー ──
            _paramSection.Add(ParamLabel("シェーダー"));
            _paramSection.Add(MakeShaderRow(v));

            // ── メインカラー ──
            if (v.HasColor)
            {
                _paramSection.Add(ParamLabel("メインカラー"));
                _paramSection.Add(MakeColorRGBARow(v));
            }

            // ── メインテクスチャ ──
            if (v.MainTexProperty != null)
            {
                string propName = v.MainTexProperty;
                var tex = v.MainTexture;
                string texName = v.MainTexName ?? "(None)";

                _paramSection.Add(ParamLabel("メインテクスチャ"));
                var texRow = new VisualElement();
                texRow.style.flexDirection = FlexDirection.Row;
                texRow.style.alignItems    = Align.Center;
                texRow.style.marginBottom  = 4;

                // サムネプレビュー（本体でだけテクスチャが入る。リモートでは名前だけ）
                var thumb = new VisualElement();
                thumb.style.width  = 40;
                thumb.style.height = 40;
                thumb.style.marginRight = 6;
                thumb.style.borderTopWidth  = thumb.style.borderBottomWidth =
                thumb.style.borderLeftWidth = thumb.style.borderRightWidth  = 1;
                thumb.style.borderTopColor  = thumb.style.borderBottomColor =
                thumb.style.borderLeftColor = thumb.style.borderRightColor  =
                    new StyleColor(new Color(0.5f, 0.5f, 0.5f));
                thumb.style.backgroundColor = new StyleColor(new Color(0.15f, 0.15f, 0.15f));
                thumb.style.backgroundSize  = new StyleBackgroundSize(
                    new BackgroundSize(BackgroundSizeType.Contain));
                if (tex != null)
                    thumb.style.backgroundImage = new StyleBackground(tex);
                texRow.Add(thumb);

                var texLabel = new Label(texName);
                texLabel.style.flexGrow = 1;
                texLabel.style.fontSize = 10;
                texLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                texLabel.style.overflow = Overflow.Hidden;
                texLabel.style.textOverflow = TextOverflow.Ellipsis;
                texLabel.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f));

                string capturedProp = propName;
                var browseBtn = new Button(() => OnBrowseTexture(capturedProp, texLabel, thumb)) { text = "..." };
                browseBtn.style.width = 28;
                browseBtn.style.marginRight = 2;

                texRow.Add(browseBtn);
                texRow.Add(texLabel);
                _paramSection.Add(texRow);
            }

            // ── Metallic ──
            if (v.HasMetallic)
                AddScalarSlider("Metallic", v.Metallic, MaterialScalarKind.Metallic);

            // ── Smoothness ──
            if (v.HasSmoothness)
                AddScalarSlider("Smoothness", v.Smoothness, MaterialScalarKind.Smoothness);

            // ── 表面種別（Opaque / Transparent）──
            bool isTransparent = v.IsTransparent;
            _paramSection.Add(ParamLabel("表面種別"));
            var surfaceRow = new VisualElement();
            surfaceRow.style.flexDirection = FlexDirection.Row;
            surfaceRow.style.marginBottom  = 4;

            var opaqueBtn      = new Button(() => OnSurface(false)) { text = "Opaque"      };
            var transparentBtn = new Button(() => OnSurface(true))  { text = "Transparent" };
            StyleSurfaceBtn(opaqueBtn,      !isTransparent);
            StyleSurfaceBtn(transparentBtn, isTransparent);

            surfaceRow.Add(opaqueBtn);
            surfaceRow.Add(transparentBtn);
            _paramSection.Add(surfaceRow);
        }

        /// <summary>
        /// Metallic・Smoothness のスライダー。操作中はツールの窓口の previewScalar で見た目だけ変え、
        /// 離したとき・フォーカスを外したときに commitScalar で確定する（M-2）。
        /// 確定後は窓口が見た目を保存データへ揃えるので、止められたときは保存データの値へ戻す。
        /// </summary>
        private void AddScalarSlider(string title, float initial, MaterialScalarKind kind)
        {
            int slot = _editingSlot;
            var label = ParamLabel($"{title}  {initial:F2}");
            _paramSection.Add(label);
            var slider = new Slider(0f, 1f) { value = initial };
            slider.style.marginBottom = 4;
            bool[] pending = { false };
            slider.RegisterValueChangedCallback(e =>
            {
                Surface?.Invoke(Tool, "previewScalar",
                    ("slot", slot), ("kind", kind), ("value", e.newValue));
                label.text = $"{title}  {e.newValue:F2}";
                pending[0] = true;
            });
            void Commit()
            {
                if (!pending[0]) return;
                pending[0] = false;
                Surface?.Invoke(Tool, "commitScalar",
                    ("slot", slot), ("kind", kind), ("value", slider.value));
                var after = Model?.GetMaterialSlot(slot);
                if (after == null) return;
                float saved = kind == MaterialScalarKind.Metallic ? after.SavedMetallic : after.SavedSmoothness;
                if (!Mathf.Approximately(saved, slider.value))
                {
                    slider.SetValueWithoutNotify(saved);
                    label.text = $"{title}  {saved:F2}";
                }
            }
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => Commit());
            slider.RegisterCallback<FocusOutEvent>(_ => Commit());
            _paramSection.Add(slider);
        }

        // ── シェーダー選択行 ──────────────────────────────────────────────
        private VisualElement MakeShaderRow(Poly_Ling.View.MaterialSlotView v)
        {
            var container = new VisualElement();
            container.style.marginBottom = 4;

            var labels = new List<string>();
            var types  = new List<ShaderType>();
            foreach (var choice in ShaderChoices)
            {
                // ビルドに含まれないシェーダーは Shader.Find が null を返す。並べない。
                if (MaterialDataConverter.GetShader(choice.Type) == null) continue;
                labels.Add(choice.Label);
                types.Add(choice.Type);
            }
            labels.Add(CustomShaderLabel);
            types.Add(ShaderType.Custom);

            // 表示は実材質の shader から求める。matRef.Data.ShaderType と食い違うのは
            // 「記録した種別のシェーダーがビルドに無く ToMaterial がフォールバックした」場合で、
            // ここで Data を書き換えると記録した種別を失う。表示だけ合わせ、Data は触らない。
            var detected = v.DetectedShaderType;
            int sel = types.IndexOf(detected);
            if (sel < 0) sel = types.Count - 1;   // Unknown → Custom 扱い

            _shaderDropdown = new DropdownField(labels, sel);
            _shaderDropdown.style.marginBottom = 2;
            container.Add(_shaderDropdown);

            string shaderName = v.ShaderName ?? "";
            _customShaderField = new TextField("シェーダー名") { isDelayed = true, value = shaderName };
            _customShaderField.style.fontSize = 10;
            _customShaderField.style.display =
                (types[sel] == ShaderType.Custom) ? DisplayStyle.Flex : DisplayStyle.None;
            container.Add(_customShaderField);

            var nowLabel = new Label($"現在: {shaderName}");
            nowLabel.style.fontSize    = 9;
            nowLabel.style.marginBottom = 2;
            container.Add(nowLabel);

            // フィールド(_customShaderField)ではなくローカルを捕捉する。Rebuild でフィールドが
            // 次の要素に差し替わっても、このコールバックは自分が作った要素を操作する。
            var capturedTypes  = types;
            var capturedLabels = labels;
            var customField    = _customShaderField;
            _shaderDropdown.RegisterValueChangedCallback(e =>
            {
                int i = capturedLabels.IndexOf(e.newValue);
                if (i < 0) return;
                var t = capturedTypes[i];
                if (t == ShaderType.Custom)
                {
                    // 名前欄を出すだけ。適用は名前が確定してから。
                    customField.style.display = DisplayStyle.Flex;
                    SetStatus("シェーダー名を入力してください");
                    return;
                }
                ApplyShaderType(t, null);
            });

            customField.RegisterValueChangedCallback(e =>
                ApplyShaderType(ShaderType.Custom, e.newValue));

            // DropdownField / TextField は自前で色を持たないため暗色テーマを掛ける。
            // 全 TextElement を白にするので、灰色にしたいラベルはこの後で塗り直す。
            PlayerLayoutRoot.ApplyDarkTheme(container);
            nowLabel.style.color = new StyleColor(new Color(0.6f, 0.6f, 0.6f));

            return container;
        }

        /// <summary>
        /// マテリアルのシェーダーを差し替える。処理は SetMaterialShaderCommand の受け口
        /// （MaterialEditOps.ApplyShader）が行う（操作経路統一計画.md H-3c）。
        /// </summary>
        private void ApplyShaderType(ShaderType type, string customName)
        {
            if (_editingSlot < 0) return;
            SendCmd(new SetMaterialShaderCommand(
                _getModelIndex?.Invoke() ?? 0, _editingSlot, type, customName ?? ""));
            NotifyAndRefresh($"シェーダー: {Model?.GetMaterialSlot(_editingSlot)?.ShaderName ?? ""}");
        }

        // ── RGBA スライダー行 ─────────────────────────────────────────────
        private VisualElement MakeColorRGBARow(Poly_Ling.View.MaterialSlotView v)
        {
            Color initial = v.Color;
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

            // スライダー操作中は画面上の Material だけを変え、操作を離したときに
            // SetMaterialColorCommand で確定する（永続データと変更扱いはコマンド側が行う。
            // 操作経路統一計画.md H-3）。
            int slot = _editingSlot;
            bool[] pending = { false };
            void CommitColor()
            {
                if (!pending[0]) return;
                pending[0] = false;
                var c = cur[0];
                // 確定は窓口の commitColor（SetMaterialColorCommand を送り、見た目を保存データへ揃える。M-2）。
                Surface?.Invoke(Tool, "commitColor",
                    ("slot", slot), ("r", c.r), ("g", c.g), ("b", c.b), ("a", c.a));

                // 担当者判定で止められた場合、保存データは変わっていない。
                // 表示を保存データの色へ戻し、見た目とデータの食い違いを残さない。
                var saved = Model?.GetMaterialSlot(slot)?.SavedColor ?? initial;
                if (saved != c)
                {
                    cur[0] = saved;
                    preview.style.backgroundColor = new StyleColor(saved);
                }
            }

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
                    Surface?.Invoke(Tool, "previewColor",
                        ("slot", slot), ("r", cur[0].r), ("g", cur[0].g), ("b", cur[0].b), ("a", cur[0].a));
                    preview.style.backgroundColor = new StyleColor(cur[0]);
                    pending[0] = true;
                });
                // 離したとき・フォーカスを外したときに確定する。
                slider.RegisterCallback<PointerCaptureOutEvent>(_ => CommitColor());
                slider.RegisterCallback<FocusOutEvent>(_ => CommitColor());

                row.Add(lbl);
                row.Add(slider);
                sliders.Add(row);
            }

            previewRow.Add(sliders);
            container.Add(previewRow);
            return container;
        }

        // ── テクスチャブラウズ ────────────────────────────────────────────
        // 読み込みと設定は SetMaterialTextureCommand の受け口（MaterialEditOps.ApplyTextureFile）
        // が行う。画面で選んだファイルは作業フォルダの外でも 1 回だけ許可する（操作経路統一計画.md H-3c）。
        private void OnBrowseTexture(string propName, Label displayLabel, VisualElement preview)
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "テクスチャ選択", TexPathKey, null, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) return;

            PLSandbox.AllowOnceFromDialog(path);
            int slot = _editingSlot;
            SendCmd(new SetMaterialTextureCommand(
                _getModelIndex?.Invoke() ?? 0, slot, propName, path));

            // 設定できたかは窓口の写しで確かめる（止められた・読めなかったときは変わらない）。
            var after = Model?.GetMaterialSlot(slot);
            if (after != null && after.MainTexProperty == propName
                && after.MainTexName == Path.GetFileNameWithoutExtension(path))
            {
                displayLabel.text = after.MainTexName;
                if (preview != null && after.MainTexture != null)
                    preview.style.backgroundImage = new StyleBackground(after.MainTexture);
                SetStatus($"テクスチャ設定: {after.MainTexName}");
            }
            else
            {
                SetStatus("テクスチャを設定できませんでした");
            }
        }

        // ── 表面種別 ──────────────────────────────────────────────────────
        // 設定は SetMaterialSurfaceCommand の受け口（MaterialEditOps.ApplySurface）が行う。
        private void OnSurface(bool transparent)
        {
            if (_editingSlot < 0) return;
            SendCmd(new SetMaterialSurfaceCommand(_getModelIndex?.Invoke() ?? 0, _editingSlot, transparent));
            NotifyAndRefresh(transparent ? "Transparent に設定" : "Opaque に設定");
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
            if (Model == null) return;
            // 現在スロットの切り替えと既定マテリアルの自動控えはコマンドで行う（M-3）。
            SendCmd(new SetCurrentMaterialSlotCommand(_getModelIndex?.Invoke() ?? 0, index));

            // 同スロットを再クリックでパラメータエリアをトグル
            _editingSlot = (_editingSlot == index) ? -1 : index;

            NotifyAndRefresh(string.Empty);
        }

        // 追加・削除・面への適用はコマンドだけで行う。本体（ホスト）も SetCommandContext を渡すので
        // （PolyLingPlayerViewerCore.Layout.Panels.cs）、パネル内で直接書き換える経路は持たない
        // （操作経路統一計画.md J）。
        private void OnAdd()
        {
            if (Model == null) return;
            SendCmd(new AddMaterialSlotCommand(_getModelIndex?.Invoke() ?? 0));
            Refresh();
        }

        private void OnRemoveSlot(int index)
        {
            var m = Model; if (m == null || m.MaterialCount <= 1) return;
            if (_editingSlot == index) _editingSlot = -1;
            SendCmd(new RemoveMaterialSlotCommand(_getModelIndex?.Invoke() ?? 0, index));
            Refresh();
        }

        private void OnApplyToSelection()
        {
            var m = Model; if (m == null) return;
            int masterIdx = m.ActiveMeshIndex;
            int selFaces  = m.ActiveMesh?.SelectedFaceCount ?? 0;
            if (masterIdx < 0 || selFaces == 0) return;
            int matIdx = m.CurrentMaterialIndex;
            // 面の索引は空で送る（受け口が対象メッシュの今の選択面を使う）。
            SendCmd(new ApplyMaterialToFacesCommand(
                _getModelIndex?.Invoke() ?? 0, masterIdx, matIdx, System.Array.Empty<int>()));
            NotifyAndRefresh($"[{matIdx}] を {selFaces} 面に適用");
        }

        // ── Helpers ──────────────────────────────────────────────────────
        // 変更フラグ・一覧更新通知・再描画は各コマンドの受け口が行う（M-3）。
        private void NotifyAndRefresh(string status)
        {
            OnRepaint?.Invoke();
            if (!string.IsNullOrEmpty(status)) SetStatus(status);
            Refresh();
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        // リスト(ScrollView)の下端ドラッグリサイズ用ハンドル（MeshListSubPanel と同方式）。
        // 6px バーを PointerDown/Move/Up + CapturePointer でドラッグし、height/minHeight/maxHeight を同値で固定。
        private static void AddListResizeHandle(
            VisualElement container, VisualElement target,
            Func<float> getHeight, Action<float> setHeight,
            float min, float max)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginTop       = 2;
            handle.style.marginBottom    = 4;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            bool  dragging    = false;
            float startY      = 0f;
            float startHeight = 0f;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                dragging    = true;
                startY      = e.position.y;
                startHeight = getHeight();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - startY;
                float h = Mathf.Clamp(startHeight + delta, min, max);
                setHeight(h);
                target.style.height    = h;
                target.style.minHeight = h;
                target.style.maxHeight = h;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                dragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }

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
