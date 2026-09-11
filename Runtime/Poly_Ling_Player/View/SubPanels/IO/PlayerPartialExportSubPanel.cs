// PlayerPartialExportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO 部分エクスポートパネル（UIToolkit）。
// PMXPartialExportOps / MQOPartialMatchHelper+MQOPartialExportOps を内部保持する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Ops;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerPartialExportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO }
        private Mode _mode;

        // ================================================================
        // ロジック層
        // ================================================================

        private readonly PMXPartialExportOps   _pmxOps    = new PMXPartialExportOps();
        private readonly MQOPartialMatchHelper _mqoHelper = new MQOPartialMatchHelper();
        private readonly MQOPartialExportOps   _mqoOps    = new MQOPartialExportOps();

        // PMX 部分エクスポートオプション
        private string _pmxRefPath         = "";
        private PMXDocument _pmxDocument;
        private List<MeshMaterialMapping> _pmxMappings = new List<MeshMaterialMapping>();
        private float _pmxScale              = 10f;
        private bool  _pmxFlipX              = true;
        private bool  _pmxFlipZ              = true;
        private bool  _pmxFlipUV_V           = true;
        private bool  _pmxReplacePositions   = true;
        private bool  _pmxReplaceNormals     = true;
        private bool  _pmxReplaceUVs         = true;
        private bool  _pmxReplaceBoneWeights = true;
        private bool  _pmxOutputCSV          = false;

        // MQO 部分エクスポートオプション
        private string _mqoRefPath       = "";
        private float  _mqoExportScale   = 0.01f;
        private bool   _mqoFlipX         = true;
        private bool   _mqoFlipZ         = false;
        private bool   _mqoSkipBaked     = true;
        private bool   _mqoSkipNamed     = true;
        private bool   _mqoWritePos      = true;
        private bool   _mqoWriteUV       = false;
        private bool   _mqoWriteBW       = false;

        // ================================================================
        // コールバック
        // ================================================================

        public Action<string> OnStatusChanged;

        // ================================================================
        // モデルコンテキスト
        // ================================================================

        private ModelContext _model;

        public void SetModel(ModelContext model)
        {
            _model = model;
            _pmxMappings.Clear();
            _mqoHelper.BuildModelList(model, _mqoSkipBaked, _mqoSkipNamed);
        }

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        // UI 自動操作の ID は "<partialExportPmx / partialExportMqo>.<下の Id>"（UiControlAttribute.cs）。
        // 1 つのセクションをモードで切り替えるので、モードごとに別パネルとして登録する。
        // 設定行は _uiDynamic、一覧の上のボタンは _uiDynamicList（一覧だけ作り直すことがあるため別）。
        // 一覧の行（対応表のチェック）はデータ行。保存は必ずダイアログを通す。
        [UiControl("title", Safety = UiSafety.ReadOnly, Description = "部分エクスポータの名前（モード）")]
        private Label         _panelNameLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        [UiControl(Ignore = true)]
        private VisualElement _settingsContainer;
        [UiControl(Ignore = true)]
        private VisualElement _listContainer;
        [UiNested("saveDest")]
        private PlayerSaveDestRow _saveDest;
        [UiControl("run", Safety = UiSafety.UserOnly, Description = "エクスポートする（保存ダイアログを開く）")]
        private Button        _exportBtn;

        /// <summary>モードごとの設定行。</summary>
        private readonly UiDynamicControls _uiDynamic     = new UiDynamicControls();
        /// <summary>一覧の上のボタンと、一覧のデータ行。</summary>
        private readonly UiDynamicControls _uiDynamicList = new UiDynamicControls();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            _panelNameLabel = new Label("");
            _panelNameLabel.style.fontSize = 12;
            _panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panelNameLabel.style.marginBottom = 4;
            parent.Add(_panelNameLabel);

            // 書き込み先フォルダ行。[...] はフォルダを決めるだけで、保存はしない。
            _saveDest = new PlayerSaveDestRow(
                "書き込み先フォルダ", SaveDest.Keys.PartialExport, "Export PMX", "pmx", DefaultOutName);
            parent.Add(_saveDest.Root);

            var exportBtn = new Button(OnExportClicked) { text = "エクスポート" };
            exportBtn.style.marginTop    = 2;
            exportBtn.style.marginBottom = 4;
            exportBtn.style.height       = 28;
            exportBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(exportBtn);
            _exportBtn = exportBtn;

            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            parent.Add(_statusLabel);

            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);

            _listContainer = new VisualElement();
            parent.Add(_listContainer);
        }

        public void SetMode(Mode mode)
        {
            _mode = mode;
            if (_panelNameLabel != null)
                _panelNameLabel.text = mode == Mode.PMX ? "PMX部分エクスポート" : "MQO部分エクスポート";

            if (_saveDest != null)
            {
                _saveDest.DialogTitle = mode == Mode.PMX ? "Export PMX" : "Export MQO";
                _saveDest.Extension   = mode == Mode.PMX ? "pmx" : "mqo";
                _saveDest.Refresh();
            }
            RestoreRefForMode();
            RebuildAll();
        }

        /// <summary>
        /// 保存ダイアログのファイル名欄の初期値。参照元の名前を土台にする。
        /// これは「初期値」であって書き込み先ではない。書き込み先は必ずダイアログで確定する。
        /// </summary>
        private string DefaultOutName()
        {
            string refPath = _mode == Mode.PMX ? _pmxRefPath : _mqoRefPath;
            if (string.IsNullOrEmpty(refPath)) return "";

            string stem = Path.GetFileNameWithoutExtension(refPath);
            if (string.IsNullOrEmpty(stem)) return "";

            return _mode == Mode.PMX ? stem + "_modified.pmx" : stem + "_partial.mqo";
        }

        /// <summary>現在モードの参照元パスを RecentPaths から復元・読込（未設定かつ実在時のみ）</summary>
        private void RestoreRefForMode()
        {
            if (_mode == Mode.PMX)
            {
                if (string.IsNullOrEmpty(_pmxRefPath))
                {
                    string p = RecentPaths.Get("PartialExport.PMX.Ref");
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        _pmxRefPath = p;
                        LoadPMXRef(p);
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(_mqoRefPath))
                {
                    string p = RecentPaths.Get("PartialExport.MQO.Ref");
                    if (!string.IsNullOrEmpty(p) && File.Exists(p))
                    {
                        _mqoRefPath = p;
                        LoadMQORef(p);
                    }
                }
            }
        }

        // ================================================================
        // Export 実行
        // ================================================================

        private void OnExportClicked()
        {
            if (_model == null) { SetStatus("モデルが未設定です"); return; }

            try
            {
                if (_mode == Mode.PMX)
                    ExecutePMXExport();
                else
                    ExecuteMQOExport();
            }
            catch (Exception ex)
            {
                SetStatus($"失敗: {ex.Message}");
                Debug.LogError($"[PlayerPartialExport] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ExecutePMXExport()
        {
            if (_pmxDocument == null) { SetStatus("リファレンスPMXを選択してください"); return; }
            if (!_pmxMappings.Any(m => m.Selected && m.IsMatched))
            { SetStatus("エクスポートするメッシュを選択してください"); return; }

            // 保存先は必ずここで確定する（「名前を付けて保存」）。
            string savePath = _saveDest?.AskSavePath() ?? "";
            if (string.IsNullOrEmpty(savePath)) return;   // キャンセル

            int transferred = _pmxOps.ExecuteExport(
                _pmxMappings, _pmxDocument,
                _pmxScale, new AxisFlip(_pmxFlipX, _pmxFlipZ), _pmxFlipUV_V,
                _pmxReplacePositions, _pmxReplaceNormals, _pmxReplaceUVs, _pmxReplaceBoneWeights);

            PMXWriter.Save(_pmxDocument, savePath);
            if (_pmxOutputCSV)
                PMXCSVWriter.Save(_pmxDocument, Path.ChangeExtension(savePath, ".csv"));

            SetStatus($"完了: {transferred}verts → {Path.GetFileName(savePath)}");
            // PMXを再ロードして編集内容をリセット
            LoadPMXRef(_pmxRefPath);
        }

        private void ExecuteMQOExport()
        {
            if (_mqoHelper.MQODocument == null) { SetStatus("リファレンスMQOを選択してください"); return; }

            var selectedModels = _mqoHelper.SelectedModelMeshes;
            var selectedMQOs   = _mqoHelper.SelectedMQOObjects;
            if (selectedModels.Count == 0 || selectedMQOs.Count == 0)
            { SetStatus("エクスポートするメッシュを選択してください"); return; }

            // 保存先は必ずここで確定する（「名前を付けて保存」）。
            string savePath = _saveDest?.AskSavePath() ?? "";
            if (string.IsNullOrEmpty(savePath)) return;   // キャンセル

            int transferred = _mqoOps.ExecuteExport(
                selectedMQOs, selectedModels,
                _mqoHelper.MQODocument,
                _mqoExportScale, new AxisFlip(_mqoFlipX, _mqoFlipZ),
                _mqoWritePos, _mqoWriteUV, _mqoWriteBW);

            var writeResult = MQODocumentIO.WriteDocumentToFile(_mqoHelper.MQODocument, savePath);
            if (!writeResult.Success) throw new Exception(writeResult.ErrorMessage);

            SetStatus($"完了: {transferred}verts → {Path.GetFileName(savePath)}");
            _mqoHelper.LoadMQO(_mqoRefPath, visibleOnly: false);
        }

        // ================================================================
        // UI 再構築
        // ================================================================

        private void RebuildAll()
        {
            RebuildSettings();
            RebuildList();
        }

        private void RebuildSettings()
        {
            if (_settingsContainer == null) return;
            _settingsContainer.Clear();
            _uiDynamic.Begin(ModeGroup(_mode));
            if (_mode == Mode.PMX)
                BuildPmxSettings(_settingsContainer);
            else
                BuildMqoSettings(_settingsContainer);
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
        }

        private void RebuildList()
        {
            if (_listContainer == null) return;
            _listContainer.Clear();
            // 一覧は設定と別に作り直す（参照元の読込・全選択など）ので、別の置き場へ登録する。
            _uiDynamicList.Begin(ModeGroup(_mode));
            if (_mode == Mode.PMX)
                BuildPmxList(_listContainer);
            else
                BuildMqoList(_listContainer);
        }

        /// <summary>UI 自動操作の動的な項目の組（モードごとの設定行・一覧）。</summary>
        public static string ModeGroup(Mode mode) => mode == Mode.PMX ? "pmx" : "mqo";

        // ================================================================
        // PMX 設定 UI
        // ================================================================

        private void BuildPmxSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("リファレンスPMX"));
            parent.Add(FilePickRow("reference", _pmxRefPath, "pmx", "Select PMX", path =>
            {
                _pmxRefPath = path;
                LoadPMXRef(path);
                RebuildList();
            }, "PartialExport.PMX.Ref"));

            parent.Add(Separator());
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("scale",     "Scale",    () => _pmxScale,    v => _pmxScale    = v));
            parent.Add(ToggleRow("flipX",    "Flip X",  () => _pmxFlipX,    v => _pmxFlipX    = v));
            parent.Add(ToggleRow("flipZ",    "Flip Z",  () => _pmxFlipZ,    v => _pmxFlipZ    = v));
            parent.Add(ToggleRow("flipUvV",  "Flip UV V", () => _pmxFlipUV_V, v => _pmxFlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("書き出し対象"));
            parent.Add(ToggleRow("replace.positions",   "座標",           () => _pmxReplacePositions,   v => _pmxReplacePositions   = v));
            parent.Add(ToggleRow("replace.normals",     "法線",           () => _pmxReplaceNormals,     v => _pmxReplaceNormals     = v));
            parent.Add(ToggleRow("replace.uvs",         "UV",             () => _pmxReplaceUVs,         v => _pmxReplaceUVs         = v));
            parent.Add(ToggleRow("replace.boneWeights", "ボーンウェイト", () => _pmxReplaceBoneWeights, v => _pmxReplaceBoneWeights = v));
            parent.Add(ToggleRow("csv",                 "CSVも出力",      () => _pmxOutputCSV,          v => _pmxOutputCSV          = v));
        }

        private void BuildPmxList(VisualElement parent)
        {
            if (_pmxMappings.Count == 0) return;

            parent.Add(Separator());

            // 選択ボタン行
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 2;
            AddSmallBtn("list.selectAll",   btnRow, "全選択",     () => { foreach (var m in _pmxMappings) m.Selected = true;          RebuildList(); }, "対応表をすべて選ぶ");
            AddSmallBtn("list.selectNone",  btnRow, "全解除",     () => { foreach (var m in _pmxMappings) m.Selected = false;         RebuildList(); }, "対応表の選択をすべて外す");
            AddSmallBtn("list.matchedOnly", btnRow, "一致のみ",   () => { foreach (var m in _pmxMappings) m.Selected = m.IsMatched;   RebuildList(); }, "一致した行だけを選ぶ");
            parent.Add(btnRow);

            // ヘッダー行
            var header = new VisualElement(); header.style.flexDirection = FlexDirection.Row;
            header.Add(SpacerLabel(20, ""));
            header.Add(SpacerLabel(120, "モデルメッシュ", 9));
            header.Add(SpacerLabel(45,  "V", 9));
            header.Add(SpacerLabel(120, "PMX材質", 9));
            header.Add(SpacerLabel(45,  "V", 9));
            header.Add(SpacerLabel(20,  "", 9));
            parent.Add(header);

            foreach (var mapping in _pmxMappings)
            {
                var m   = mapping;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1;

                if (!m.IsMatched && !string.IsNullOrEmpty(m.PMXMaterialName))
                    row.style.backgroundColor = new StyleColor(Color.white);

                var tog = new Toggle { value = m.Selected };
                tog.style.width = 20;
                tog.RegisterValueChangedCallback(ev => m.Selected = ev.newValue);

                row.Add(tog);
                row.Add(SpacerLabel(120, m.MeshName, 9));
                row.Add(SpacerLabel(45,  m.MeshExpandedVertexCount.ToString(), 9));
                row.Add(SpacerLabel(120, m.PMXMaterialName ?? "(none)", 9));
                row.Add(SpacerLabel(45,  m.PMXVertexCount.ToString(), 9));

                if (!string.IsNullOrEmpty(m.PMXMaterialName))
                {
                    var icon = new Label(m.IsMatched ? "✓" : "×");
                    icon.style.color = m.IsMatched
                        ? new StyleColor(new Color(0.3f, 1f, 0.3f))
                        : new StyleColor(new Color(1f, 0.3f, 0.3f));
                    icon.style.width   = 20;
                    icon.style.fontSize = 10;
                    row.Add(icon);
                }

                // 行ごとのチェックはメッシュ・材質の対応表なので固定の ID を付けない（データ行）。
                parent.Add(_uiDynamicList.AddRows(row));
            }
        }

        private void LoadPMXRef(string path)
        {
            _pmxDocument  = null;
            _pmxMappings.Clear();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                _pmxDocument = PMXReader.Load(path);
                _pmxMappings = _pmxOps.BuildMappings(_model, _pmxDocument);
                SetStatus($"PMX: {_pmxDocument.Materials.Count}材質 / {_pmxDocument.Vertices.Count}頂点");
            }
            catch (Exception ex)
            {
                SetStatus($"PMX読込失敗: {ex.Message}");
            }
        }

        // ================================================================
        // MQO 設定 UI
        // ================================================================

        private void BuildMqoSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("リファレンスMQO"));
            parent.Add(FilePickRow("reference", _mqoRefPath, "mqo", "Select MQO", path =>
            {
                _mqoRefPath = path;
                LoadMQORef(path);
                RebuildList();
            }, "PartialExport.MQO.Ref"));

            parent.Add(Separator());
            parent.Add(SectionLabel("オプション"));
            parent.Add(FloatRow("scale", "Export Scale", () => _mqoExportScale, v => _mqoExportScale = v));
            parent.Add(ToggleRow("flipX", "Flip X",       () => _mqoFlipX,       v => _mqoFlipX       = v));
            parent.Add(ToggleRow("flipZ", "Flip Z",       () => _mqoFlipZ,       v => _mqoFlipZ       = v));

            bool prevBaked = _mqoSkipBaked;
            parent.Add(ToggleRow("skipBakedMirror", "BakedMirrorをスキップ", () => _mqoSkipBaked, v =>
            {
                _mqoSkipBaked = v;
                _mqoHelper.BuildModelList(_model, _mqoSkipBaked, _mqoSkipNamed);
                if (_mqoHelper.MQODocument != null) _mqoHelper.AutoMatch();
                RebuildList();
            }));

            bool prevNamed = _mqoSkipNamed;
            parent.Add(ToggleRow("skipNamedMirror", "名前ミラー(+)をスキップ", () => _mqoSkipNamed, v =>
            {
                _mqoSkipNamed = v;
                _mqoHelper.BuildModelList(_model, _mqoSkipBaked, _mqoSkipNamed);
                if (_mqoHelper.MQODocument != null) _mqoHelper.AutoMatch();
                RebuildList();
            }));

            parent.Add(Separator());
            parent.Add(SectionLabel("WriteBack"));
            var wbRow = new VisualElement(); wbRow.style.flexDirection = FlexDirection.Row;
            wbRow.Add(ToggleCompact("writeBack.positions",   "位置",           () => _mqoWritePos, v => _mqoWritePos = v));
            wbRow.Add(ToggleCompact("writeBack.uvs",         "UV",             () => _mqoWriteUV,  v => _mqoWriteUV  = v));
            wbRow.Add(ToggleCompact("writeBack.boneWeights", "BoneWeight",     () => _mqoWriteBW,  v => _mqoWriteBW  = v));
            parent.Add(wbRow);
        }

        private void BuildMqoList(VisualElement parent)
        {
            if (_mqoHelper.MQODocument == null && _mqoHelper.MQOObjects.Count == 0) return;

            parent.Add(Separator());

            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 2;
            AddSmallBtn("list.autoMatch",  btnRow, "Auto", () => { _mqoHelper.AutoMatch(); RebuildList(); }, "自動で対応を取り直す");
            AddSmallBtn("list.selectAll",  btnRow, "全",   () => { foreach (var e in _mqoHelper.ModelMeshes) e.Selected = true;  foreach (var e in _mqoHelper.MQOObjects) e.Selected = true;  RebuildList(); }, "両方の一覧をすべて選ぶ");
            AddSmallBtn("list.selectNone", btnRow, "無",   () => { foreach (var e in _mqoHelper.ModelMeshes) e.Selected = false; foreach (var e in _mqoHelper.MQOObjects) e.Selected = false; RebuildList(); }, "両方の一覧の選択をすべて外す");
            parent.Add(btnRow);

            var cols = new VisualElement(); cols.style.flexDirection = FlexDirection.Row;

            // 左：モデルメッシュ
            var left = new VisualElement(); left.style.flexGrow = 1; left.style.marginRight = 2;
            left.Add(ColHeader("モデルメッシュ"));
            foreach (var e in _mqoHelper.ModelMeshes)
            {
                var entry = e;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                var lbl = new Label($"{entry.Name} ({entry.TotalExpandedVertexCount})"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9;
                row.Add(tog); row.Add(lbl); left.Add(row);
            }
            cols.Add(left);

            // 右：MQOオブジェクト
            var right = new VisualElement(); right.style.flexGrow = 1;
            right.Add(ColHeader("MQOオブジェクト"));
            foreach (var e in _mqoHelper.MQOObjects)
            {
                var entry = e;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                string mirrorMark = entry.IsMirrored ? "⇆" : "";
                var lbl = new Label($"{entry.Name}{mirrorMark} ({entry.ExpandedVertexCountWithMirror})"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9;
                row.Add(tog); row.Add(lbl); right.Add(row);
            }
            cols.Add(right);

            // 左右の一覧のチェックはメッシュ・オブジェクトごとの行なので固定の ID を付けない（データ行）。
            parent.Add(_uiDynamicList.AddRows(cols));
        }

        private void LoadMQORef(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            _mqoHelper.LoadMQO(path, visibleOnly: false);
            if (_mqoHelper.ModelMeshes.Count == 0)
                _mqoHelper.BuildModelList(_model, _mqoSkipBaked, _mqoSkipNamed);
            if (_mqoHelper.MQODocument != null)
            {
                _mqoHelper.AutoMatch();
                SetStatus($"MQO: {_mqoHelper.MQOObjects.Count}オブジェクト");
            }
        }

        // ================================================================
        // UIパーツ ヘルパー
        // ================================================================

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            OnStatusChanged?.Invoke(msg);
        }

        /// <summary>
        /// ファイルパス表示 + ブラウズボタン行。
        /// UI 自動操作では "<id>.path"（表示）と "<id>.browse"（ダイアログ）を登録する。
        /// </summary>
        private VisualElement FilePickRow(
            string id, string currentPath, string ext, string title, Action<string> onPicked, string recentKey = null)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lbl = new Label(string.IsNullOrEmpty(currentPath) ? "(未選択)" : Path.GetFileName(currentPath));
            lbl.style.flexGrow = 1; lbl.style.fontSize = 9;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.overflow = Overflow.Hidden;
            var btn = new Button(() =>
            {
                string path = PlayerIoUiKit.AskLoadPath(
                    title,
                    string.IsNullOrEmpty(recentKey) ? "PartialExport.Ref." + ext : recentKey,
                    currentPath, ext);
                if (!string.IsNullOrEmpty(path))
                {
                    lbl.text = Path.GetFileName(path);
                    onPicked(path);
                }
            }) { text = "..." };
            btn.style.width = 28;
            row.Add(lbl); row.Add(btn);
            _uiDynamic.Add(id + ".path", lbl, title + "（選んだファイル名）", UiSafety.ReadOnly);
            _uiDynamic.Add(id + ".browse", btn, title + "（ファイル選択ダイアログを開き、選んだら読み込む）", UiSafety.UserOnly);
            return row;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.marginTop = 6; l.style.marginBottom = 2;
            l.style.color     = new StyleColor(new Color(0.7f, 0.85f, 1f));
            l.style.fontSize  = 10;
            return l;
        }

        private static Label ColHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 9; l.style.color = new StyleColor(Color.white);
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private VisualElement ToggleRow(string id, string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return _uiDynamic.Add(id, t, label);
        }

        private VisualElement ToggleCompact(string id, string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginRight = 4; t.style.fontSize = 9;
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return _uiDynamic.Add(id, t, label);
        }

        private VisualElement FloatRow(string id, string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lbl = new Label(label); lbl.style.width = 90; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var field = new FloatField { value = get() }; field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(lbl); row.Add(_uiDynamic.Add(id, field, label));
            return row;
        }

        private static Label SpacerLabel(float width, string text, int fontSize = 10)
        {
            var l = new Label(text);
            l.style.width          = width;
            l.style.fontSize       = fontSize;
            l.style.overflow       = Overflow.Hidden;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        /// <summary>一覧の上の小ボタン。一覧は設定と別に作り直すので _uiDynamicList へ登録する。</summary>
        private void AddSmallBtn(string id, VisualElement parent, string text, Action onClick, string description)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1; b.style.marginRight = 2; b.style.height = 18; b.style.fontSize = 9;
            parent.Add(_uiDynamicList.Add(id, b, description, UiSafety.SafeWrite));
        }
    }
}
