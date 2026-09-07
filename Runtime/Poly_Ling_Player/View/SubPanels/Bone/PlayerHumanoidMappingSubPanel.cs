// PlayerHumanoidMappingSubPanel.cs
// HumanoidMappingPanelV2 の Player 版サブパネル。
// DnD 除去。EditorUtility.OpenFilePanel → PLEditorBridge.I.OpenFilePanel に置換。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerHumanoidMappingSubPanel
    {
        public Func<ModelContext>  GetModel;
        public Func<ToolContext>   GetToolContext;
        public Action<PanelCommand> SendCommand;
        public Func<int>            GetModelIndex;

        private Label       _warningLabel;
        private Label       _modelMappingLabel;
        private Toggle      _scopeToggle;
        private TextField   _csvPathField;
        private Label       _csvHintLabel;
        private Button      _btnAutoMap, _btnLoadCsv, _btnApply, _btnClear;
        private Label       _mappedCountLabel;
        private VisualElement _previewContent;
        private Label       _previewEmptyLabel;
        private VisualElement _mappingDetailContainer;
        private Label       _statusLabel;

        private string             _csvFilePath   = "";
        private const string       CsvPathKey     = "HumanoidMapping.CsvPath";
        private HumanoidBoneMapping _previewMapping = null;

        /// <summary>
        /// 割当候補にボーン以外のコンテキストも含めるか。
        /// 通常はボーンのみ（false）。ボーンを持たない MeshFilter ツリーを
        /// そのまま骨格として扱いたい実験用途のときだけ有効にする。
        /// </summary>
        private bool _includeNonBoneContexts = false;

        // ── Avatar リターゲット設定8項目の入力欄 ──────────────────────
        //   Avatar 生成（Editor のプレファブ書き出し）だけが使う値。
        //   Humanoid 割当と同じく Avatar の入力なので、この画面へ置く。
        private Slider     _upperArmTwist, _lowerArmTwist, _upperLegTwist, _lowerLegTwist;
        private Slider     _armStretch, _legStretch;
        private FloatField _feetSpacing;
        private Toggle     _hasTranslationDoF;
        private Label      _retargetStateLabel;

        /// <summary>直前に欄へ読み込んだモデル。変わったら読み直す。</summary>
        private ModelContext _retargetLoadedModel;

        private ModelContext Model => GetModel?.Invoke();

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Humanoidボーンマッピング"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_warningLabel);

            // 現在のモデルが既にマッピングを持っているかどうか。
            // 下の「プレビュー」は本パネル内の一時バッファの状態であり、
            // モデル本体のマッピングとは別物なので、ここで別に出す。
            _modelMappingLabel = new Label();
            _modelMappingLabel.style.whiteSpace   = WhiteSpace.Normal;
            _modelMappingLabel.style.marginBottom = 4;
            root.Add(_modelMappingLabel);

            // CSV ファイル行
            root.Add(SecLabel("CSV ファイル"));
            _csvPathField = new TextField();
            _csvPathField.RegisterValueChangedCallback(e => { _csvFilePath = e.newValue; RecentPaths.Set(CsvPathKey, e.newValue); });
            root.Add(PlayerIoUiKit.PathRow(_csvPathField, OnBrowseCSV));
            _csvFilePath = RecentPaths.Get(CsvPathKey);
            _csvPathField.SetValueWithoutNotify(_csvFilePath);

            _csvHintLabel = new Label("CSVを[...]で選択してください。");
            _csvHintLabel.style.fontSize    = 10;
            _csvHintLabel.style.color       = new StyleColor(Color.white);
            _csvHintLabel.style.marginBottom = 4;
            root.Add(_csvHintLabel);

            // 割当候補の範囲
            _scopeToggle = new Toggle("ボーン以外も候補に含める (MeshFilter用)")
            {
                value = _includeNonBoneContexts
            };
            _scopeToggle.style.fontSize     = 10;
            _scopeToggle.style.marginBottom = 2;
            _scopeToggle.RegisterValueChangedCallback(e => _includeNonBoneContexts = e.newValue);
            root.Add(_scopeToggle);

            var scopeHint = new Label("ボーン数に応じて自動設定（ボーン0本でオン）。手動で変えてもツール再選択で戻る。");
            scopeHint.style.fontSize     = 9;
            scopeHint.style.whiteSpace   = WhiteSpace.Normal;
            scopeHint.style.marginBottom = 4;
            scopeHint.style.color        = new StyleColor(PlayerIoUiKit.StatusColor);
            root.Add(scopeHint);

            // AutoMap / Load CSV ボタン
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 6;
            _btnAutoMap = new Button(OnAutoMap)  { text = "Auto Map (PMX)" }; _btnAutoMap.style.flexGrow = 1; _btnAutoMap.style.marginRight = 4;
            _btnLoadCsv = new Button(OnBrowseCSV) { text = "CSVから読み込み" }; _btnLoadCsv.style.flexGrow = 1;
            btnRow.Add(_btnAutoMap); btnRow.Add(_btnLoadCsv);
            root.Add(btnRow);

            root.Add(MakeSep());

            // プレビュー
            root.Add(SecLabel("プレビュー"));
            _previewEmptyLabel = new Label("プレビューなし（Auto Map か CSVから読み込み で作成）");
            _previewEmptyLabel.style.color      = new StyleColor(Color.white);
            _previewEmptyLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_previewEmptyLabel);

            _previewContent = new VisualElement();
            _previewContent.style.display = DisplayStyle.None;
            _mappedCountLabel = new Label(); _mappedCountLabel.style.marginBottom = 4;
            _previewContent.Add(_mappedCountLabel);
            _mappingDetailContainer = new VisualElement();
            _previewContent.Add(_mappingDetailContainer);
            root.Add(_previewContent);

            // Apply / Clear ボタン
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginTop = 6; applyRow.style.marginBottom = 4;
            _btnApply = new Button(OnApply) { text = "Apply" }; _btnApply.style.flexGrow = 1; _btnApply.style.marginRight = 4;
            _btnClear = new Button(OnClear) { text = "Clear" }; _btnClear.style.flexGrow = 1;
            applyRow.Add(_btnApply); applyRow.Add(_btnClear);
            root.Add(applyRow);

            root.Add(MakeSep());
            BuildRetargetSection(root);

            _statusLabel = new Label(); _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new StyleColor(PlayerIoUiKit.StatusColor);
            root.Add(_statusLabel);

            // 構築直後にも状態表示と候補範囲の自動設定を通す。
            Refresh();
        }

        // ── Avatar リターゲット設定 ──────────────────────────────────
        private void BuildRetargetSection(VisualElement root)
        {
            var fo = new Foldout { text = "Avatar リターゲット設定", value = false };

            _retargetStateLabel = new Label();
            _retargetStateLabel.style.fontSize   = 9;
            _retargetStateLabel.style.whiteSpace = WhiteSpace.Normal;
            _retargetStateLabel.style.color      = new StyleColor(PlayerIoUiKit.StatusColor);
            fo.Add(_retargetStateLabel);

            fo.Add(RetargetHint(
                "Avatar を作るときの動きの馴染ませ方です。プレファブ書き出しのときだけ使います。\n"
              + "VRM 出力と Player の表示には影響しません。"));

            var d = new AvatarRetargetData();

            _upperArmTwist = RetargetSlider("上腕のねじれ配分", d.UpperArmTwist);
            _lowerArmTwist = RetargetSlider("前腕のねじれ配分", d.LowerArmTwist);
            _upperLegTwist = RetargetSlider("大腿のねじれ配分", d.UpperLegTwist);
            _lowerLegTwist = RetargetSlider("下腿のねじれ配分", d.LowerLegTwist);
            fo.Add(_upperArmTwist); fo.Add(_lowerArmTwist);
            fo.Add(_upperLegTwist); fo.Add(_lowerLegTwist);
            fo.Add(RetargetHint(
                "ねじりを、親のボーンと捩り用ボーンへどの割合で配るかです。既定 0.5。\n"
              + "捩り用ボーンが無いモデルでは見た目が変わりません。"));

            _armStretch = RetargetSlider("腕の伸び代", d.ArmStretch);
            _legStretch = RetargetSlider("脚の伸び代", d.LegStretch);
            fo.Add(_armStretch); fo.Add(_legStretch);
            fo.Add(RetargetHint(
                "体格の違うモーションを当てたとき、手足をどれだけ伸ばしてよいかです。既定 0.05。\n"
              + "大きくすると届くようになりますが、腕や脚が伸びて見えます。"));

            _feetSpacing = new FloatField("両足の間隔の補正") { value = d.FeetSpacing };
            fo.Add(_feetSpacing);
            fo.Add(RetargetHint("足が内股・がに股にずれるときに調整します。既定 0。"));

            _hasTranslationDoF = new Toggle("移動の自由度を持たせる") { value = d.HasTranslationDoF };
            fo.Add(_hasTranslationDoF);
            fo.Add(RetargetHint(
                "関節の位置そのものを動かすモーションを扱うときだけ入れます。既定はオフ。"));

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;

            var btnLoad  = new Button(OnLoadRetarget)  { text = "モデルから読み込む" };
            var btnApply = new Button(OnApplyRetarget) { text = "モデルへ書き込む" };
            var btnClear = new Button(OnClearRetarget) { text = "既定に戻す" };
            btnLoad.style.flexGrow = 1; btnLoad.style.marginRight = 2;
            btnApply.style.flexGrow = 1; btnApply.style.marginRight = 2;
            btnClear.style.flexGrow = 1;
            row.Add(btnLoad); row.Add(btnApply); row.Add(btnClear);
            fo.Add(row);

            root.Add(fo);
        }

        private static Slider RetargetSlider(string label, float value)
        {
            var s = new Slider(label, 0f, 1f) { value = value };
            s.showInputField = true;
            return s;
        }

        private static Label RetargetHint(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            return l;
        }

        /// <summary>
        /// モデルの値を欄へ入れる。
        /// Refresh からは「モデルが変わったとき」だけ呼ぶ。毎回入れると
        /// 入力中のスライダーが戻ってしまう。
        /// </summary>
        private void LoadRetargetFields(ModelContext model)
        {
            var a = AvatarRetargetOps.GetRetargetOrNew(model);

            _upperArmTwist?.SetValueWithoutNotify(a.UpperArmTwist);
            _lowerArmTwist?.SetValueWithoutNotify(a.LowerArmTwist);
            _upperLegTwist?.SetValueWithoutNotify(a.UpperLegTwist);
            _lowerLegTwist?.SetValueWithoutNotify(a.LowerLegTwist);
            _armStretch?.SetValueWithoutNotify(a.ArmStretch);
            _legStretch?.SetValueWithoutNotify(a.LegStretch);
            _feetSpacing?.SetValueWithoutNotify(a.FeetSpacing);
            _hasTranslationDoF?.SetValueWithoutNotify(a.HasTranslationDoF);
        }

        private void OnLoadRetarget()
        {
            var model = Model;
            if (model == null) { SetStatus("モデルがありません。"); return; }

            LoadRetargetFields(model);
            SetStatus("モデルのリターゲット設定を読み込みました。");
        }

        private void OnApplyRetarget()
        {
            if (Model == null) { SetStatus("モデルがありません。"); return; }

            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            SendCommand?.Invoke(new SetAvatarRetargetCommand(
                modelIdx,
                _upperArmTwist.value, _lowerArmTwist.value,
                _upperLegTwist.value, _lowerLegTwist.value,
                _armStretch.value, _legStretch.value,
                _feetSpacing.value, _hasTranslationDoF.value));

            SetStatus("リターゲット設定をモデルへ書き込みました。");
            Refresh();
        }

        private void OnClearRetarget()
        {
            if (Model == null) { SetStatus("モデルがありません。"); return; }

            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            SendCommand?.Invoke(new ClearAvatarRetargetCommand(modelIdx));

            SetStatus("リターゲット設定を未設定へ戻しました。");
            Refresh();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            // hint text
            if (_csvHintLabel != null)
            {
                bool hasFile = !string.IsNullOrEmpty(_csvFilePath);
                _csvHintLabel.text          = hasFile ? "" : "CSVを[...]で選択してください。";
                _csvHintLabel.style.display = hasFile ? DisplayStyle.None : DisplayStyle.Flex;
            }
            var model = Model;
            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                UpdateModelMappingLabel(null);
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;
            UpdateModelMappingLabel(model);

            // リターゲット設定は、モデルが変わったときだけ欄へ読み直す。
            if (!ReferenceEquals(_retargetLoadedModel, model))
            {
                _retargetLoadedModel = model;
                LoadRetargetFields(model);
            }
            if (_retargetStateLabel != null)
            {
                _retargetStateLabel.text = (model.AvatarRetarget != null)
                    ? "設定済み。Avatar 生成でこの値を使います。"
                    : "未設定。Avatar 生成は Unity の既定値を使います。";
            }
            SyncScopeToggleToBoneCount(model);
            UpdatePreviewUI();
        }

        // ── Operations ───────────────────────────────────────────────────
        // 「CSVから読み込み」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowseCSV()
        {
            string path = PlayerIoUiKit.AskLoadPath("Select Bone Mapping CSV", CsvPathKey, _csvFilePath, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                _csvFilePath = path;
                _csvPathField?.SetValueWithoutNotify(_csvFilePath);
                RecentPaths.Set(CsvPathKey, _csvFilePath);
                if (_csvHintLabel != null)
                {
                    _csvHintLabel.text          = "";
                    _csvHintLabel.style.display = DisplayStyle.None;
                }
                LoadCSVMapping();
            }
        }

        private void OnAutoMap()
        {
            var boneNames = GetBoneNames();
            _previewMapping = new HumanoidBoneMapping();
            int count = _previewMapping.AutoMapFromEmbeddedCSV(boneNames);
            int candidates = 0;
            foreach (var n in boneNames) if (!string.IsNullOrEmpty(n)) candidates++;
            UnityEngine.Debug.Log(
                $"[PlayerHumanoidMappingSubPanel] Auto-mapped {count} bones " +
                $"(候補 {candidates} / ボーン以外も含める={_includeNonBoneContexts})");
            if (count == 0)
                SetStatus(candidates == 0
                    ? "割当候補がありません。ボーンが無いモデルでは「ボーン以外も候補に含める」を有効にすること。"
                    : "一致するボーン名がありませんでした。");
            UpdatePreviewUI();
        }

        private void OnApply()
        {
            if (_previewMapping == null || Model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                ApplyHumanoidMappingCommand.SplitMapping(_previewMapping, out var hmNames, out var hmIdx);
                SendCommand.Invoke(new ApplyHumanoidMappingCommand(modelIdx, hmNames, hmIdx));
                SetStatus($"適用しました ({_previewMapping.Count} ボーン)");
                Refresh();
                return;
            }
            // フォールバック
            var tc     = GetToolContext?.Invoke();
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.CopyFrom(_previewMapping);
            var after  = Model.HumanoidMapping.Clone();
            var undo   = tc?.UndoController;
            if (undo != null)
            {
                var __rec = new HumanoidMappingChangedRecord(before, after, "Apply Humanoid Mapping");
                string __dbgDesc = "Apply Humanoid Mapping";
                PLDiag.UndoRecord("MeshList", __dbgDesc, __rec);
                undo.MeshListStack.Record(__rec, __dbgDesc);
            }
            Model.IsDirty = true;
            SetStatus($"適用しました ({_previewMapping.Count} ボーン)");
            Refresh();
        }

        private void OnClear()
        {
            if (Model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new ClearHumanoidMappingCommand(modelIdx));
                _previewMapping = null;
                SetStatus("マッピングをクリアしました");
                UpdateModelMappingLabel(Model);
                UpdatePreviewUI();
                return;
            }
            // フォールバック
            var tc     = GetToolContext?.Invoke();
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.ClearAll();
            var after  = Model.HumanoidMapping.Clone();
            var undo   = tc?.UndoController;
            if (undo != null)
            {
                var __rec = new HumanoidMappingChangedRecord(before, after, "Clear Humanoid Mapping");
                string __dbgDesc = "Clear Humanoid Mapping";
                PLDiag.UndoRecord("MeshList", __dbgDesc, __rec);
                undo.MeshListStack.Record(__rec, __dbgDesc);
            }
            Model.IsDirty   = true;
            _previewMapping = null;
            SetStatus("マッピングをクリアしました");
            UpdateModelMappingLabel(Model);
            UpdatePreviewUI();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void LoadCSVMapping()
        {
            if (string.IsNullOrEmpty(_csvFilePath) || !File.Exists(_csvFilePath))
            {
                SetStatus("CSVファイルが見つかりません"); return;
            }
            try
            {
                var csvLines = new List<string>(File.ReadAllLines(_csvFilePath, Encoding.UTF8));
                _previewMapping = new HumanoidBoneMapping();
                int count = _previewMapping.LoadFromCSV(csvLines, GetBoneNames());
                SetStatus($"CSV 読込み: {count} ボーン");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PlayerHumanoidMappingSubPanel] CSV load failed: {ex.Message}");
                _previewMapping = null;
                SetStatus("CSV 読込みに失敗しました");
            }
            UpdatePreviewUI();
        }

        /// <summary>
        /// 割当候補の名前リストを返す。
        ///
        /// _includeNonBoneContexts = false（既定）:
        ///   従来どおりボーンのみを詰めたリスト。位置が MeshContextList の索引と
        ///   一致するのはボーンが先頭に連続して並ぶ場合に限られる。
        ///
        /// _includeNonBoneContexts = true:
        ///   長さ MeshContextCount で、各名前を自分の索引位置に置いたリスト。
        ///   HumanoidBoneMapping が期待する「索引 = MeshContextList の索引」を満たす。
        ///   候補外の位置は空文字。FindBoneByAliases は完全一致・部分一致とも
        ///   空文字にはヒットしない。
        /// </summary>
        private List<string> GetBoneNames()
        {
            var names = new List<string>();
            var model = Model;
            if (model == null) return names;

            if (!_includeNonBoneContexts)
            {
                foreach (var entry in model.Bones)
                {
                    var mc = model.GetMeshContext(entry.MasterIndex);
                    if (mc != null && !string.IsNullOrEmpty(mc.Name)) names.Add(mc.Name);
                }
                return names;
            }

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                names.Add(mc != null && !string.IsNullOrEmpty(mc.Name) ? mc.Name : "");
            }
            return names;
        }

        /// <summary>
        /// モデル本体（ModelContext.HumanoidMapping）の割当状態を出す。
        /// この Dict は保存対象で、humanoid.csv と bone.csv の humanBodyBone 列に
        /// 書き出され、読込時に再構築される（CsvModelSerializer / ModelSerializer）。
        /// </summary>
        private void UpdateModelMappingLabel(ModelContext model)
        {
            if (_modelMappingLabel == null) return;

            if (model == null)
            {
                _modelMappingLabel.text  = "";
                _modelMappingLabel.style.display = DisplayStyle.None;
                return;
            }
            _modelMappingLabel.style.display = DisplayStyle.Flex;

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                _modelMappingLabel.text  = "このモデル: マッピング未設定";
                _modelMappingLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.4f));
                return;
            }

            int missing = mapping.GetMissingRequiredBones().Count;
            _modelMappingLabel.text = missing == 0
                ? $"このモデル: マッピング設定済み {mapping.Count} ボーン（必須すべて割当済み。Avatar作成可）"
                : $"このモデル: マッピング設定済み {mapping.Count} ボーン（必須未割当 {missing} 件）";
            _modelMappingLabel.style.color = new StyleColor(
                missing == 0 ? new Color(0.5f, 0.9f, 0.5f) : new Color(1f, 0.85f, 0.4f));
        }

        /// <summary>
        /// 「ボーン以外も候補に含める」をボーン数から決める。
        /// ボーンが 1 本も無ければオン、あればオフ。Refresh のたびに無条件で上書きする。
        /// </summary>
        private void SyncScopeToggleToBoneCount(ModelContext model)
        {
            bool auto = (model != null && model.BoneCount == 0);
            _includeNonBoneContexts = auto;
            _scopeToggle?.SetValueWithoutNotify(auto);
        }

        private void UpdatePreviewUI()
        {
            if (_previewContent == null) return;
            if (_previewMapping == null || _previewMapping.Count == 0)
            {
                _previewContent.style.display  = DisplayStyle.None;
                if (_previewEmptyLabel != null) _previewEmptyLabel.style.display = DisplayStyle.Flex;
                return;
            }
            if (_previewEmptyLabel != null) _previewEmptyLabel.style.display = DisplayStyle.None;
            _previewContent.style.display = DisplayStyle.Flex;
            if (_mappedCountLabel != null) _mappedCountLabel.text = $"マッピング済: {_previewMapping.Count} ボーン";

            // 詳細（最大15件）
            _mappingDetailContainer?.Clear();
            int shown = 0;
            foreach (var kvp in _previewMapping.BoneIndexMap)
            {
                if (shown++ >= 15) { var more = new Label("  ...他"); more.style.fontSize = 9; more.style.color = new StyleColor(Color.white); _mappingDetailContainer?.Add(more); break; }
                var lbl = new Label($"  {kvp.Key}: [{kvp.Value}]");
                _mappingDetailContainer?.Add(lbl);
            }
            PlayerLayoutRoot.ApplyDarkTheme(_mappingDetailContainer);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static VisualElement MakeSep() { var s = new VisualElement(); s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white); s.style.marginTop = 4; s.style.marginBottom = 6; return s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
