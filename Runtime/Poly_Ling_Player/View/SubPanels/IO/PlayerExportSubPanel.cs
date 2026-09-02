// PlayerExportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO / OBJ エクスポート設定パネル（UIToolkit）。
// PlayerImportSubPanel と対称な設計。
// 保存は必ず PlayerIoUiKit.AskSavePath（保存ダイアログ）を通す。パス欄への直接書き出しは行わない。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.OBJ;
using Poly_Ling.Vrm;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示する PMX / MQO / OBJ エクスポート設定 UI。
    /// Build(parent) で UIToolkit 要素を生成し、
    /// OnExportPmx / OnExportMqo コールバックで実行を Viewer に委譲する。
    /// </summary>
    public class PlayerExportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO, OBJ, VRM }

        private Mode _mode;

        // ================================================================
        // 設定
        // ================================================================

        private PMXExportSettings _pmxSettings = PMXExportSettings.CreateFullExport();
        // MQO⇔Unity は X のみ反転（AxisFlip.MqoToUnity, AxisFlipOps.cs:49）。
        // 第2引数は flipZ。PMX は true が正しいが MQO は false。
        // ここを true にするとインポート（MQOImportSettings の既定 FlipZ = false）と
        // 食い違い、読んで書くだけで Z 反転が1回残る。
        private MQOExportSettings _mqoSettings = MQOExportSettings.CreateFromCoordinate(
            0.01f, flipZ: false, flipX: true);

        // OBJ も右手系・+Y 上で MQO と同じ置き方をするため X のみ反転。
        // 単位は決まっていないので等倍を既定にする。
        private ObjExportSettings _objSettings = ObjExportSettings.CreateDefault();

        // VRM 1.0。実装は PolyLing.Vrm10 アセンブリ側にあり、
        // VRM パッケージが無い環境では PLVrm10Bridge.I.IsAvailable が false になる。
        // 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
        private Vrm10ExportSettings _vrmSettings = Vrm10ExportSettings.CreateDefault();

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>PMX Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, PMXExportSettings> OnExportPmx;

        /// <summary>MQO Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, MQOExportSettings> OnExportMqo;

        /// <summary>OBJ Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, ObjExportSettings> OnExportObj;

        /// <summary>VRM Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, Vrm10ExportSettings> OnExportVrm;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private Label         _panelNameLabel;
        private Label         _statusLabel;
        private VisualElement _settingsContainer;
        private TextField     _pathField;

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

            // 出力先パス行（[...] は「エクスポート」と同一処理）
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;

            var browseBtn = new Button(OnExportClicked) { text = "..." };
            browseBtn.style.width       = 28;
            browseBtn.style.marginRight = 2;

            _pathField = new TextField();
            _pathField.style.flexGrow = 1;
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(ExportPathKey(), e.newValue));

            fileRow.Add(browseBtn);
            fileRow.Add(_pathField);
            parent.Add(fileRow);

            // Export ボタン
            var exportBtn = new Button(OnExportClicked) { text = "エクスポート" };
            exportBtn.style.marginTop    = 2;
            exportBtn.style.marginBottom = 4;
            exportBtn.style.height       = 28;
            exportBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(exportBtn);

            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            parent.Add(_statusLabel);

            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);
        }

        public void SetMode(Mode mode)
        {
            _mode = mode;
            if (_panelNameLabel != null)
                _panelNameLabel.text = ModeName(mode) + "エクスポータ";
            _pathField?.SetValueWithoutNotify(RecentPaths.Get(ExportPathKey()));
            RebuildSettings();
        }

        // ================================================================
        // Export 実行
        // ================================================================

        // 保存は必ず保存ダイアログを通す。
        // パス欄の値へ無確認で書き出すと、読み込んだファイルを事故で上書きする。
        // パス欄の値はダイアログの初期フォルダ／初期ファイル名としてだけ使い、
        // 空欄のときは OS の現在フォルダに任せる。
        private void OnExportClicked()
        {
            SetStatus("");

            string name  = ModeName(_mode);
            string ext   = name.ToLowerInvariant();
            string title = "Export " + name;

            string savePath = PlayerIoUiKit.AskSavePath(
                title, ExportPathKey(), _pathField?.value ?? "", "", ext);
            if (string.IsNullOrEmpty(savePath)) return;   // キャンセル

            _pathField.value = savePath;                  // RecentPaths へは値変更コールバックで反映される

            if (_mode == Mode.PMX)
                OnExportPmx?.Invoke(savePath, ClonePmxSettings());
            else if (_mode == Mode.OBJ)
                OnExportObj?.Invoke(savePath, _objSettings.Clone());
            else if (_mode == Mode.VRM)
                OnExportVrm?.Invoke(savePath, _vrmSettings.Clone());
            else
                OnExportMqo?.Invoke(savePath, CloneMqoSettings());
        }

        /// <summary>モード名（表示・保存キー・拡張子の共通元）</summary>
        private static string ModeName(Mode mode)
        {
            switch (mode)
            {
                case Mode.PMX: return "PMX";
                case Mode.OBJ: return "OBJ";
                case Mode.VRM: return "VRM";
                default:       return "MQO";
            }
        }

        /// <summary>エクスポートパスの保存キー（モード別）</summary>
        private string ExportPathKey()
            => "Export." + ModeName(_mode) + ".Path";

        public void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // 設定 UI 構築
        // ================================================================

        private void RebuildSettings()
        {
            if (_settingsContainer == null) return;
            _settingsContainer.Clear();

            if (_mode == Mode.PMX)
                BuildPmxSettings(_settingsContainer);
            else if (_mode == Mode.OBJ)
                BuildObjSettings(_settingsContainer);
            else if (_mode == Mode.VRM)
                BuildVrmSettings(_settingsContainer);
            else
                BuildMqoSettings(_settingsContainer);
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
        }

        // ────────────────────────────────────────────────────────
        // PMX 設定
        // ────────────────────────────────────────────────────────

        private void BuildPmxSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",    () => _pmxSettings.Scale,    v => _pmxSettings.Scale    = v));
            parent.Add(ToggleRow("Flip X",  () => _pmxSettings.FlipX,    v => _pmxSettings.FlipX    = v));
            parent.Add(ToggleRow("Flip Z",  () => _pmxSettings.FlipZ,    v => _pmxSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _pmxSettings.FlipUV_V, v => _pmxSettings.FlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(ToggleRow("材質",   () => _pmxSettings.ExportMaterials, v => _pmxSettings.ExportMaterials = v));
            parent.Add(ToggleRow("ボーン", () => _pmxSettings.ExportBones,     v => _pmxSettings.ExportBones     = v));
            parent.Add(ToggleRow("モーフ", () => _pmxSettings.ExportMorphs,    v => _pmxSettings.ExportMorphs    = v));
            parent.Add(ToggleRow("剛体",   () => _pmxSettings.ExportBodies,    v => _pmxSettings.ExportBodies    = v));
            parent.Add(ToggleRow("ジョイント", () => _pmxSettings.ExportJoints, v => _pmxSettings.ExportJoints  = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力形式"));
            parent.Add(ToggleRow("バイナリ PMX", () => _pmxSettings.OutputBinaryPMX, v => _pmxSettings.OutputBinaryPMX = v));
            parent.Add(ToggleRow("CSV も出力",   () => _pmxSettings.OutputCSV,        v => _pmxSettings.OutputCSV        = v));
        }

        private PMXExportSettings ClonePmxSettings()
        {
            // PMXExportSettings にコピーコンストラクタがないため手動コピー
            return new PMXExportSettings
            {
                ExportMode       = _pmxSettings.ExportMode,
                Scale            = _pmxSettings.Scale,
                FlipX            = _pmxSettings.FlipX,
                FlipZ            = _pmxSettings.FlipZ,
                FlipUV_V         = _pmxSettings.FlipUV_V,
                ExportMaterials  = _pmxSettings.ExportMaterials,
                ExportBones      = _pmxSettings.ExportBones,
                ExportMorphs     = _pmxSettings.ExportMorphs,
                ExportBodies     = _pmxSettings.ExportBodies,
                ExportJoints     = _pmxSettings.ExportJoints,
                OutputBinaryPMX  = _pmxSettings.OutputBinaryPMX,
                OutputCSV        = _pmxSettings.OutputCSV,
                DecimalPrecision = _pmxSettings.DecimalPrecision,
            };
        }

        // ────────────────────────────────────────────────────────
        // MQO 設定
        // ────────────────────────────────────────────────────────

        private void BuildMqoSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",    () => _mqoSettings.Scale,    v => _mqoSettings.Scale    = v));
            parent.Add(ToggleRow("Flip X",  () => _mqoSettings.FlipX,    v => _mqoSettings.FlipX    = v));
            parent.Add(ToggleRow("Flip Z",  () => _mqoSettings.FlipZ,    v => _mqoSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _mqoSettings.FlipUV_V, v => _mqoSettings.FlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(ToggleRow("材質",            () => _mqoSettings.ExportMaterials,       v => _mqoSettings.ExportMaterials       = v));
            parent.Add(ToggleRow("ボーン",          () => _mqoSettings.ExportBones,           v => _mqoSettings.ExportBones           = v));
            parent.Add(ToggleRow("BWを埋め込む",    () => _mqoSettings.EmbedBoneWeightsInMQO, v => _mqoSettings.EmbedBoneWeightsInMQO = v));
            parent.Add(ToggleRow("BakedMirrorをスキップ", () => _mqoSettings.SkipBakedMirror, v => _mqoSettings.SkipBakedMirror       = v));
            parent.Add(ToggleRow("名前ミラー(+)をスキップ", () => _mqoSettings.SkipNamedMirror, v => _mqoSettings.SkipNamedMirror    = v));
        }

        private MQOExportSettings CloneMqoSettings() => _mqoSettings.Clone();

        // ────────────────────────────────────────────────────────
        // OBJ 設定
        //
        // OBJ は階層・ボーン・モーフ・非表示のいずれも持たない。
        // 頂点はワールド座標へ畳んで書き、マテリアルは同名の .mtl を隣に作る。
        // ────────────────────────────────────────────────────────

        private void BuildObjSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",      () => _objSettings.Scale,    v => _objSettings.Scale    = v));
            parent.Add(ToggleRow("Flip X",    () => _objSettings.FlipX,    v => _objSettings.FlipX    = v));
            parent.Add(ToggleRow("Flip Z",    () => _objSettings.FlipZ,    v => _objSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _objSettings.FlipUV_V, v => _objSettings.FlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(ToggleRow("UV",   () => _objSettings.ExportUVs,     v => _objSettings.ExportUVs     = v));
            parent.Add(ToggleRow("法線", () => _objSettings.ExportNormals, v => _objSettings.ExportNormals = v));
            parent.Add(ToggleRow("材質（.mtl も出力）",
                () => _objSettings.ExportMaterials, v => _objSettings.ExportMaterials = v));
            parent.Add(ToggleRow("非表示メッシュも出力",
                () => _objSettings.ExportInvisibleObjects, v => _objSettings.ExportInvisibleObjects = v));
            parent.Add(ToggleRow("非表示面も出力",
                () => _objSettings.ExportHiddenFaces, v => _objSettings.ExportHiddenFaces = v));
            parent.Add(ToggleRow("補助線を l 行で出力",
                () => _objSettings.ExportLines, v => _objSettings.ExportLines = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力形式"));
            parent.Add(ToggleRow("ワールド座標で出力",
                () => _objSettings.ExportVerticesInWorldSpace,
                v => _objSettings.ExportVerticesInWorldSpace = v));
            parent.Add(FloatRow("小数桁数",
                () => _objSettings.DecimalPrecision,
                v => _objSettings.DecimalPrecision = Mathf.Clamp(Mathf.RoundToInt(v), 1, 9)));
        }

        // ────────────────────────────────────────────────────────
        // VRM 1.0 設定
        // ────────────────────────────────────────────────────────

        private void BuildVrmSettings(VisualElement parent)
        {
            if (!PLVrm10Bridge.I.IsAvailable)
            {
                var warn = new Label(
                    "VRM 1.0 エクスポータが利用できません。\n" +
                    "VRM パッケージ (com.vrmc.vrm) を導入し、再生モードで実行してください。");
                warn.style.whiteSpace = WhiteSpace.Normal;
                warn.style.fontSize   = 10;
                warn.style.color      = new StyleColor(new Color(1f, 0.6f, 0.4f));
                parent.Add(warn);
                return;
            }

            parent.Add(SectionLabel("メタ情報（VRM 仕様で必須）"));
            parent.Add(TextRow("モデル名", () => _vrmSettings.Title,   v => _vrmSettings.Title   = v));
            parent.Add(TextRow("バージョン", () => _vrmSettings.Version, v => _vrmSettings.Version = v));
            parent.Add(TextRow("作者", () => FirstAuthor(_vrmSettings), v => SetFirstAuthor(_vrmSettings, v)));
            parent.Add(TextRow("連絡先",
                () => _vrmSettings.ContactInformation, v => _vrmSettings.ContactInformation = v));
            parent.Add(TextRow("ライセンスURL",
                () => _vrmSettings.OtherLicenseUrl, v => _vrmSettings.OtherLicenseUrl = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(FloatRow("Scale", () => _vrmSettings.Scale, v => _vrmSettings.Scale = v));
            // UV・法線のトグルは置かない。VRM 出力は常に両方を含む。
            //   UniVRM の ModelExporter.CreateMesh が法線・UV を常に載せるうえ、
            //   MeshWriter.ExportMeshDivided は VertexBuffer.Normals / TexCoords を
            //   null チェックせずに読むため、外すと出力が落ちる。
            //   切れないものをトグルで見せると「切ったのに出る」ことになるので出さない。
            parent.Add(ToggleRow("スキニング", () => _vrmSettings.ExportSkinning, v => _vrmSettings.ExportSkinning = v));
            parent.Add(ToggleRow("非表示メッシュも出力",
                () => _vrmSettings.ExportInvisibleObjects,
                v => _vrmSettings.ExportInvisibleObjects = v));

            // 必須関節が欠けると VRM ビューアは読み込みを拒否する。
            // 上半身だけ・片側だけのモデルを確認したいときに使う。
            parent.Add(ToggleRow("不足関節を補完",
                () => _vrmSettings.SupplementHumanoid,
                v => _vrmSettings.SupplementHumanoid = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("モーフ・表情・揺れ"));
            parent.Add(ToggleRow("モーフ（ブレンドシェイプ）",
                () => _vrmSettings.ExportMorphTargets,
                v => _vrmSettings.ExportMorphTargets = v));
            parent.Add(ToggleRow("表情（モーフエクスプレッション）",
                () => _vrmSettings.ExportExpressions,
                v => _vrmSettings.ExportExpressions = v));
            parent.Add(ToggleRow("表情名をプリセットへ割当",
                () => _vrmSettings.MapExpressionPresets,
                v => _vrmSettings.MapExpressionPresets = v));
            parent.Add(ToggleRow("スプリングボーン",
                () => _vrmSettings.ExportSpringBones,
                v => _vrmSettings.ExportSpringBones = v));

            parent.Add(Separator());
            var note = new Label(
                "表情はモーフの出力が前提です（モーフを切ると表情も出ません）。\n"
                + "テクスチャ・視線（LookAt）・一人称設定は未対応です。");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.fontSize   = 10;
            note.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            parent.Add(note);
        }

        private static string FirstAuthor(Vrm10ExportSettings s)
            => (s.Authors != null && s.Authors.Count > 0) ? s.Authors[0] : "";

        private static void SetFirstAuthor(Vrm10ExportSettings s, string value)
        {
            if (s.Authors == null) s.Authors = new System.Collections.Generic.List<string>();
            if (s.Authors.Count == 0) s.Authors.Add(value);
            else                      s.Authors[0] = value;
        }

        private static VisualElement TextRow(string label, Func<string> get, Action<string> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;

            var field = new TextField { value = get() ?? "" };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));

            row.Add(lbl);
            row.Add(field);
            return row;
        }

        // ================================================================
        // UIパーツ ヘルパー（PlayerImportSubPanel と共通パターン）
        // ================================================================

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(new Color(0.7f, 0.85f, 1f));
            l.style.fontSize     = 10;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 4;
            v.style.marginBottom    = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static VisualElement ToggleRow(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        private static VisualElement FloatRow(string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;

            var field = new FloatField { value = get() };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));

            row.Add(lbl);
            row.Add(field);
            return row;
        }
    }
}
