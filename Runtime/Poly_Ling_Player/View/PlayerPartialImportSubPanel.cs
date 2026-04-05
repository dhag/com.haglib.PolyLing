// PlayerPartialImportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO 部分インポートパネル（UIToolkit）。
// PMXPartialImportOps / MQOPartialMatchHelper+MQOPartialImportOps を内部保持し
// Viewer が持つ ProjectContext からモデルを受け取って動作する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.EditorBridge;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class PlayerPartialImportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO }
        private Mode _mode;

        // ================================================================
        // ロジック層
        // ================================================================

        private readonly PMXPartialImportOps   _pmxOps     = new PMXPartialImportOps();
        private readonly MQOPartialMatchHelper _mqoHelper  = new MQOPartialMatchHelper();
        private readonly MQOPartialImportOps   _mqoOps     = new MQOPartialImportOps();

        // PMX ファイル読み込みオプション（PMXImporter に渡す）
        private float _pmxScale  = 0.1f;
        private bool  _pmxFlipZ  = false;

        // MQO ファイルオプション
        private float      _mqoScale         = 0.01f;
        private bool       _mqoFlipZ         = true;
        private bool       _mqoFlipUV_V      = true;
        private bool       _mqoSkipNamedMirror = true;
        private bool       _mqoBakeMirror    = true;
        private bool       _mqoRecalcNormals = true;
        private NormalMode _mqoNormalMode    = NormalMode.Smooth;
        private float      _mqoSmoothingAngle = 60f;

        // インポート対象（PMX）
        private bool _pmxImportPos      = true;
        private bool _pmxImportUV       = false;
        private bool _pmxImportBW       = false;
        private bool _pmxImportFace     = false;
        private bool _pmxImportMaterial = false;

        // インポート対象（MQO）
        private bool _mqoImportPos      = true;
        private bool _mqoImportVtxId    = false;
        private bool _mqoImportMesh     = false;
        private bool _mqoImportMaterial = false;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>インポート完了 / 失敗時のステータス通知。</summary>
        public Action<string> OnStatusChanged;

        /// <summary>
        /// インポート実行後に Viewer 側で GPU バッファ再構築等を行うコールバック。
        /// topologyChanged=true の場合はトポロジー変更扱い。
        /// </summary>
        public Action<bool> OnImportDone;

        // ================================================================
        // モデルコンテキスト（Viewer から毎フレーム渡す必要はなく、パネル表示時にセットする）
        // ================================================================

        private ModelContext _model;
        private MeshUndoController _undoController;

        public void SetModel(ModelContext model, MeshUndoController undoController = null)
        {
            _model          = model;
            _undoController = undoController;
            _pmxOps.BuildModelList(model);
            _mqoHelper.BuildModelList(model, skipBakedMirror: true, skipNamedMirror: _mqoSkipNamedMirror, pairMirrors: true);
        }

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private Label         _panelNameLabel;
        private Label         _statusLabel;
        private TextField     _filePathField;
        private VisualElement _settingsContainer;
        private VisualElement _listContainer;

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
            _panelNameLabel.style.color = new StyleColor(Color.white);
            parent.Add(_panelNameLabel);

            // ── ファイル行
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;

            _filePathField = new TextField();
            _filePathField.style.flexGrow    = 1;
            _filePathField.style.marginRight = 2;

            var browseBtn = new Button(OnBrowse) { text = "..." };
            browseBtn.style.width = 28;
            browseBtn.style.color = new StyleColor(Color.white);

            fileRow.Add(_filePathField);
            fileRow.Add(browseBtn);
            parent.Add(fileRow);

            var importBtn = new Button(OnImportClicked) { text = "リロード" };
            importBtn.style.marginTop    = 2;
            importBtn.style.marginBottom = 4;
            importBtn.style.height       = 28;
            importBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            importBtn.style.color = new StyleColor(Color.white);
            parent.Add(importBtn);

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
                _panelNameLabel.text = mode == Mode.PMX ? "PMX部分インポート" : "MQO部分インポート";
            if (_filePathField != null) _filePathField.value = "";
            RebuildAll();
        }

        // ================================================================
        // ファイルブラウズ
        // ================================================================

        private void OnBrowse()
        {
            string ext   = _mode == Mode.PMX ? "pmx" : "mqo";
            string title = _mode == Mode.PMX ? "Select PMX" : "Select MQO";
            string dir   = string.IsNullOrEmpty(_filePathField?.value)
                ? Application.dataPath
                : Path.GetDirectoryName(_filePathField.value);

            string path = PLEditorBridge.I.OpenFilePanel(title, dir, ext);
            if (string.IsNullOrEmpty(path)) return;

            _filePathField.value = path;
            LoadFileAndMatch(path);
        }

        // ================================================================
        // ファイル読み込みとマッチング
        // ================================================================

        private void LoadFileAndMatch(string path)
        {
            if (_mode == Mode.PMX)
            {
                var settings = new PMXImportSettings
                {
                    ImportMode            = PMXImportMode.NewModel,
                    ImportTarget          = PMXImportTarget.Mesh,
                    ImportMaterials       = false,
                    FlipZ                 = _pmxFlipZ,
                    Scale                 = _pmxScale,
                    RecalculateNormals    = false,
                    DetectNamedMirror     = true,
                    UseObjectNameGrouping = true
                };
                var result = PMXImporter.ImportFile(path, settings);
                if (result == null || !result.Success)
                {
                    SetStatus($"読込失敗: {result?.ErrorMessage}");
                    return;
                }
                _pmxOps.LoadPMXResult(result);
                if (_pmxOps.ModelMeshes.Count == 0) _pmxOps.BuildModelList(_model);
                _pmxOps.AutoMatch();
            }
            else
            {
                _mqoHelper.LoadMQO(path, _mqoFlipZ, visibleOnly: true);
                if (_mqoHelper.ModelMeshes.Count == 0)
                    _mqoHelper.BuildModelList(_model, true, _mqoSkipNamedMirror, pairMirrors: true);
                if (_mqoHelper.MQODocument != null) _mqoHelper.AutoMatch();
            }

            RebuildList();
            SetStatus("");
        }

        // ================================================================
        // Import 実行
        // ================================================================

        private void OnImportClicked()
        {
            string path = _filePathField?.value ?? "";
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                LoadFileAndMatch(path);

            if (_model == null) { SetStatus("モデルが未設定です"); return; }

            try
            {
                var results          = new List<string>();
                bool topologyChanged = false;

                var beforeSnapshot = MultiMeshVertexSnapshot.Capture(_model);

                if (_mode == Mode.PMX)
                    topologyChanged = ExecutePMXImport(results);
                else
                    topologyChanged = ExecuteMQOImport(results);

                // Undo 記録
                if (_undoController != null)
                {
                    var afterSnapshot = MultiMeshVertexSnapshot.Capture(_model);
                    var label = _mode == Mode.PMX ? "PMX Partial Import" : "MQO Partial Import";
                    var record = new MultiMeshVertexSnapshotRecord(beforeSnapshot, afterSnapshot, label);
                    _undoController.MeshListStack.Record(record, label);
                }

                string resultMsg = string.Join(", ", results);
                SetStatus($"完了: {resultMsg}");
                OnImportDone?.Invoke(topologyChanged);
            }
            catch (Exception ex)
            {
                SetStatus($"失敗: {ex.Message}");
                Debug.LogError($"[PlayerPartialImport] {ex.Message}\n{ex.StackTrace}");
            }
        }

        private bool ExecutePMXImport(List<string> results)
        {
            bool topologyChanged = false;
            var selectedModels = _pmxOps.SelectedModelMeshes;
            var selectedPMXs   = _pmxOps.SelectedPMXMeshes;

            if (selectedModels.Count > 0 && selectedPMXs.Count > 0)
            {
                if (_pmxImportFace)
                {
                    int count = _pmxOps.ExecuteFaceStructureImport(selectedModels, selectedPMXs);
                    results.Add($"Faces:{count}meshes");
                    topologyChanged = true;
                }
                int vertCount = _pmxOps.ExecuteVertexAttributeImport(
                    selectedModels, selectedPMXs, _pmxImportPos, _pmxImportUV, _pmxImportBW);
                if (vertCount > 0)
                {
                    var attrs = new List<string>();
                    if (_pmxImportPos) attrs.Add("Pos");
                    if (_pmxImportUV)  attrs.Add("UV");
                    if (_pmxImportBW)  attrs.Add("BW");
                    results.Add($"{string.Join("+", attrs)}:{vertCount}verts");
                }
            }

            if (_pmxImportMaterial && _pmxOps.PMXImportResult != null)
            {
                int count = _pmxOps.ExecuteMaterialImport(_model);
                results.Add($"Mat:{count}");
            }

            return topologyChanged;
        }

        private bool ExecuteMQOImport(List<string> results)
        {
            bool topologyChanged = false;
            var selectedModels = _mqoHelper.SelectedModelMeshes;
            var selectedMQOs   = _mqoHelper.SelectedMQOObjects;

            if (_mqoImportMesh && selectedModels.Count > 0 && selectedMQOs.Count > 0)
            {
                int count = _mqoOps.ExecuteMeshStructureImport(
                    selectedModels, selectedMQOs,
                    alsoImportPosition: _mqoImportPos,
                    importScale: _mqoScale, flipZ: _mqoFlipZ, flipUV_V: _mqoFlipUV_V,
                    bakeMirror: _mqoBakeMirror,
                    recalcNormals: _mqoRecalcNormals, normalMode: _mqoNormalMode,
                    smoothingAngle: _mqoSmoothingAngle);
                results.Add($"Structure:{count}meshes");
                if (_mqoImportPos) results.Add("Pos:included");
                topologyChanged = true;
            }
            else if (_mqoImportPos && selectedModels.Count > 0 && selectedMQOs.Count > 0)
            {
                int count = _mqoOps.ExecuteVertexPositionImport(
                    selectedModels, selectedMQOs, _mqoScale, _mqoFlipZ);
                results.Add($"Pos:{count}verts");
                if (_mqoRecalcNormals)
                {
                    foreach (var e in selectedModels)
                    {
                        var mo = e.Context?.MeshObject;
                        if (mo != null) _mqoOps.RecalculateNormals(mo, _mqoNormalMode, _mqoSmoothingAngle);
                    }
                    results.Add("Normals:recalc");
                }
            }

            if (_mqoImportVtxId && selectedModels.Count > 0 && selectedMQOs.Count > 0)
            {
                int count = _mqoOps.ExecuteVertexIdImport(selectedModels, selectedMQOs);
                if (count > 0) results.Add($"VtxID:{count}");
            }

            if (_mqoImportMaterial && _mqoHelper.MQODocument != null)
            {
                int count = _mqoOps.ExecuteMaterialImport(_model, _mqoHelper.MQODocument);
                results.Add($"Mat:{count}");
            }

            return topologyChanged;
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

            if (_mode == Mode.PMX)
                BuildPmxSettings(_settingsContainer);
            else
                BuildMqoSettings(_settingsContainer);
        }

        private void RebuildList()
        {
            if (_listContainer == null) return;
            _listContainer.Clear();

            if (_mode == Mode.PMX)
                BuildPmxList(_listContainer);
            else
                BuildMqoList(_listContainer);
        }

        // ================================================================
        // PMX 設定 UI
        // ================================================================

        private void BuildPmxSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("インポート対象"));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            row.Add(ToggleCompact("頂点位置", () => _pmxImportPos,      v => _pmxImportPos      = v));
            row.Add(ToggleCompact("UV",       () => _pmxImportUV,       v => _pmxImportUV       = v));
            row.Add(ToggleCompact("BW",       () => _pmxImportBW,       v => _pmxImportBW       = v));
            row.Add(ToggleCompact("面構成",   () => _pmxImportFace,     v => _pmxImportFace     = v));
            row.Add(ToggleCompact("材質",     () => _pmxImportMaterial, v => { _pmxImportMaterial = v; }));
            parent.Add(row);

            parent.Add(Separator());
            parent.Add(SectionLabel("オプション"));
            parent.Add(FloatRow("Scale", () => _pmxScale, v => _pmxScale = v));
            parent.Add(ToggleRow("Flip Z", () => _pmxFlipZ, v => { _pmxFlipZ = v; }));
        }

        private void BuildPmxList(VisualElement parent)
        {
            if (_pmxOps.PMXImportResult == null && _pmxOps.PMXMeshes.Count == 0) return;

            parent.Add(Separator());

            // Auto/全/無 ボタン行
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 2;
            AddSmallBtn(btnRow, "Auto", () => { _pmxOps.AutoMatch(); RebuildList(); });
            AddSmallBtn(btnRow, "全",   () => { foreach (var e in _pmxOps.ModelMeshes) e.Selected = true;  foreach (var e in _pmxOps.PMXMeshes) e.Selected = true;  RebuildList(); });
            AddSmallBtn(btnRow, "無",   () => { foreach (var e in _pmxOps.ModelMeshes) e.Selected = false; foreach (var e in _pmxOps.PMXMeshes) e.Selected = false; RebuildList(); });
            parent.Add(btnRow);

            var cols = new VisualElement(); cols.style.flexDirection = FlexDirection.Row;

            // 左：PMXメッシュ
            var left = new VisualElement(); left.style.flexGrow = 1; left.style.marginRight = 2;
            left.Add(ColHeader("PMXメッシュ"));
            foreach (var e in _pmxOps.PMXMeshes)
            {
                var entry = e; // capture
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                var lbl = new Label($"{entry.Name} ({entry.VertexCount})"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9; lbl.style.color = new StyleColor(Color.white);
                row.Add(tog); row.Add(lbl); left.Add(row);
            }
            cols.Add(left);

            // 右：モデルメッシュ
            var right = new VisualElement(); right.style.flexGrow = 1;
            right.Add(ColHeader("モデルメッシュ"));
            foreach (var e in _pmxOps.ModelMeshes)
            {
                var entry = e;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                var lbl = new Label($"{entry.Name} ({entry.ExpandedVertexCount})"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9; lbl.style.color = new StyleColor(Color.white);
                row.Add(tog); row.Add(lbl); right.Add(row);
            }
            cols.Add(right);

            parent.Add(cols);
        }

        // ================================================================
        // MQO 設定 UI
        // ================================================================

        private void BuildMqoSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("インポート対象"));
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap;
            row.Add(ToggleCompact("頂点位置", () => _mqoImportPos,      v => _mqoImportPos      = v));
            row.Add(ToggleCompact("頂点ID",   () => _mqoImportVtxId,    v => _mqoImportVtxId    = v));
            row.Add(ToggleCompact("メッシュ構造", () => _mqoImportMesh, v => { _mqoImportMesh   = v; RebuildSettings(); }));
            row.Add(ToggleCompact("材質",     () => _mqoImportMaterial, v => _mqoImportMaterial = v));
            parent.Add(row);

            parent.Add(Separator());
            parent.Add(SectionLabel("オプション"));
            parent.Add(FloatRow("Scale",    () => _mqoScale,    v => _mqoScale    = v));
            parent.Add(ToggleRow("Flip Z",  () => _mqoFlipZ,    v => _mqoFlipZ    = v));

            if (_mqoImportMesh)
            {
                parent.Add(ToggleRow("Flip UV V",  () => _mqoFlipUV_V,   v => _mqoFlipUV_V   = v));
                parent.Add(ToggleRow("ミラーベイク", () => _mqoBakeMirror, v => _mqoBakeMirror = v));
            }

            if (_mqoImportPos || _mqoImportMesh)
            {
                parent.Add(ToggleRow("法線再計算", () => _mqoRecalcNormals, v => { _mqoRecalcNormals = v; RebuildSettings(); }));
                if (_mqoRecalcNormals)
                {
                    parent.Add(EnumRow("法線モード",
                        new[] { "FaceNormal", "Smooth", "Unity" },
                        () => (int)_mqoNormalMode,
                        v  => { _mqoNormalMode = (NormalMode)v; RebuildSettings(); }));
                    if (_mqoNormalMode == NormalMode.Smooth)
                        parent.Add(SliderRow("スムージング角度", 0f, 180f, () => _mqoSmoothingAngle, v => _mqoSmoothingAngle = v));
                }
            }

            bool prevSkip = _mqoSkipNamedMirror;
            parent.Add(ToggleRow("名前ミラー(+)をスキップ", () => _mqoSkipNamedMirror, v =>
            {
                _mqoSkipNamedMirror = v;
                _mqoHelper.BuildModelList(_model, true, _mqoSkipNamedMirror, pairMirrors: true);
                if (_mqoHelper.MQODocument != null) _mqoHelper.AutoMatch();
                RebuildList();
            }));
        }

        private void BuildMqoList(VisualElement parent)
        {
            if (_mqoHelper.MQODocument == null && _mqoHelper.MQOObjects.Count == 0) return;

            parent.Add(Separator());

            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 2;
            AddSmallBtn(btnRow, "Auto", () => { _mqoHelper.AutoMatch(); RebuildList(); });
            AddSmallBtn(btnRow, "全",   () => { foreach (var e in _mqoHelper.ModelMeshes) e.Selected = true;  foreach (var e in _mqoHelper.MQOObjects) e.Selected = true;  RebuildList(); });
            AddSmallBtn(btnRow, "無",   () => { foreach (var e in _mqoHelper.ModelMeshes) e.Selected = false; foreach (var e in _mqoHelper.MQOObjects) e.Selected = false; RebuildList(); });
            parent.Add(btnRow);

            var cols = new VisualElement(); cols.style.flexDirection = FlexDirection.Row;

            // 左：MQOオブジェクト
            var left = new VisualElement(); left.style.flexGrow = 1; left.style.marginRight = 2;
            left.Add(ColHeader("MQOオブジェクト"));
            foreach (var e in _mqoHelper.MQOObjects)
            {
                var entry = e;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                string mirrorMark = entry.IsMirrored ? "⟲" : "";
                var lbl = new Label($"{entry.Name}{mirrorMark} ({entry.ExpandedVertexCount})"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9; lbl.style.color = new StyleColor(Color.white);
                row.Add(tog); row.Add(lbl); left.Add(row);
            }
            cols.Add(left);

            // 右：モデルメッシュ
            var right = new VisualElement(); right.style.flexGrow = 1;
            right.Add(ColHeader("モデルメッシュ"));
            foreach (var e in _mqoHelper.ModelMeshes)
            {
                var entry = e;
                var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
                var tog = new Toggle { value = entry.Selected };
                tog.style.marginRight = 2;
                tog.RegisterValueChangedCallback(ev => { entry.Selected = ev.newValue; });
                string peerMark = entry.BakedMirrorPeer != null ? $"(+{entry.BakedMirrorPeer.Name})" : "";
                var lbl = new Label($"{entry.Name}{peerMark} [{entry.TotalExpandedVertexCount}]"); lbl.style.flexGrow = 1; lbl.style.fontSize = 9; lbl.style.color = new StyleColor(Color.white);
                row.Add(tog); row.Add(lbl); right.Add(row);
            }
            cols.Add(right);

            parent.Add(cols);
        }

        // ================================================================
        // UIパーツ ヘルパー
        // ================================================================

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            OnStatusChanged?.Invoke(msg);
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
            l.style.fontSize = 9;
            l.style.color    = new StyleColor(Color.white);
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

        private static VisualElement ToggleRow(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        private static VisualElement ToggleCompact(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginRight = 4; t.style.fontSize = 9;
            t.style.color = new StyleColor(Color.white);
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        private static VisualElement FloatRow(string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lbl = new Label(label); lbl.style.width = 80; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var field = new FloatField { value = get() }; field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(lbl); row.Add(field);
            return row;
        }

        private static VisualElement SliderRow(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lbl = new Label(label); lbl.style.width = 80; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var slider = new Slider(min, max) { value = get() }; slider.style.flexGrow = 1;
            slider.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(lbl); row.Add(slider);
            return row;
        }

        private static VisualElement EnumRow(string label, string[] choices, Func<int> get, Action<int> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lbl = new Label(label); lbl.style.width = 80; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var dd = new DropdownField(new List<string>(choices), get()); dd.style.flexGrow = 1;
            dd.RegisterValueChangedCallback(e => set(dd.index));
            row.Add(lbl); row.Add(dd);
            return row;
        }

        private static void AddSmallBtn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1; b.style.marginRight = 2; b.style.height = 18; b.style.fontSize = 9;
            b.style.color = new StyleColor(Color.white);
            parent.Add(b);
        }
    }
}
