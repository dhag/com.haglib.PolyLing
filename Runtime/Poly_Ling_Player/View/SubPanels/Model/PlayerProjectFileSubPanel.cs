// PlayerProjectFileSubPanel.cs
// プレイビュー右ペイン用 プロジェクト保存 / 読込パネル（UIToolkit）。
// .mfproj（JSON）とCSV形式の両方に対応する。
// CSV側はプロジェクトファイル（任意名の .csv）を指定し、モデルフォルダは同じディレクトリ直下に置く。
//
// 【保存と読込を別インスタンスに分ける理由】
//   同一パネルに「開く」と「保存」を並べていたため、押し間違いで
//   編集中のデータを失う / 上書きする事故が起きやすかった。
//   Mode を指定して 2 つ生成し、左ペインのボタンと右ペインのセクションも
//   保存用 / 読込用で別々にすることで、誤操作の経路自体を無くす。
//
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示するプロジェクト保存 / 読込 UI。
    /// Mode によって保存側 / 読込側のどちらかだけを構築する。
    /// 各操作の実行は Viewer 側コールバックに委譲する。
    /// </summary>
    public class PlayerProjectFileSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum PanelMode
        {
            /// <summary>保存専用（名前を付けて保存）。</summary>
            Save,
            /// <summary>読込専用（開く）。</summary>
            Load,
        }

        /// <summary>Build() より前に設定すること。</summary>
        public PanelMode Mode = PanelMode.Save;

        // ================================================================
        // コールバック
        //   Mode に応じて使う組が決まる。使わない側は結線されない。
        // ================================================================

        /// <summary>.mfproj 開く（指定パスから読込）。Load のみ。</summary>
        public Action<string> OnLoad;

        /// <summary>.mfproj 保存（ダイアログで確定したパスへ保存）。Save のみ。</summary>
        public Action<string> OnSave;

        /// <summary>CSVプロジェクトファイル 開く（merge=true で既存モデルにマージ）。Load のみ。</summary>
        public Action<string, bool> OnLoadCsv;

        /// <summary>CSVプロジェクトファイル 保存（ダイアログで確定したパスへ保存）。Save のみ。</summary>
        public Action<string> OnSaveCsv;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        // UI 自動操作の ID は "<projectSave / projectLoad>.<下の Id>"（UiControlAttribute.cs）。
        // このクラスは保存（Save）と読込（Load）の 2 か所で使い、モードで作る欄が違う（作らない側は未構築）。
        // 保存・読込はどれもダイアログを通す。
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label     _statusLabel;
        [UiControl("json.path", Description = ".mfproj のパス（読込。ダイアログの初期パスとして使う）")]
        private TextField _jsonPathField;
        [UiControl("csv.path", Description = "プロジェクト CSV のパス（読込。ダイアログの初期パスとして使う）")]
        private TextField _csvPathField;
        [UiControl("csv.merge", Description = "追加マージ（オフは現在のプロジェクトを置き換える）")]
        private Toggle    _csvMergeToggle;
        [UiControl("csv.saveAs", Safety = UiSafety.UserOnly, Description = "プロジェクト CSV を名前を付けて保存する（保存ダイアログを開く）")]
        private Button    _btnSaveAsCsv;
        [UiControl("csv.open", Safety = UiSafety.UserOnly, Description = "プロジェクト CSV を開く（ファイル選択ダイアログを開く）")]
        private Button    _btnOpenCsv;
        [UiControl("csv.browse", Safety = UiSafety.UserOnly, Description = "プロジェクト CSV の [...]（ファイル選択ダイアログを開く）")]
        private Button    _btnBrowseCsv;
        [UiControl("json.saveAs", Safety = UiSafety.UserOnly, Description = ".mfproj を名前を付けて保存する（保存ダイアログを開く）")]
        private Button    _btnSaveAsJson;
        [UiControl("json.open", Safety = UiSafety.UserOnly, Description = ".mfproj を開く（ファイル選択ダイアログを開く）")]
        private Button    _btnOpenJson;
        [UiControl("json.browse", Safety = UiSafety.UserOnly, Description = ".mfproj の [...]（ファイル選択ダイアログを開く）")]
        private Button    _btnBrowseJson;

        // 保存側の書き込み先。CSV と .mfproj で 1 つを共有する。
        // 「保存先フォルダ」は 1 つで足りるので、欄を 2 本置くと
        // どちらが効いているのか分からなくなる。
        [UiNested("saveDest", Optional = true)]
        private PlayerSaveDestRow _saveDest;

        // 読込側のパス欄のキー。保存側は SaveDest.Keys.Project を使うので共有しない。
        // 以前は保存側と読込側で同じキーを共有していたが、その結果
        // 「最後に読み込んだファイル」が保存先の初期値になり、上書き事故の経路になっていた。
        private const string JsonPathKey = "Project.JsonPath";
        private const string CsvPathKey  = "Project.CsvPath";

        private bool IsSave => Mode == PanelMode.Save;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            var title = new Label(IsSave ? "プロジェクト保存" : "プロジェクト読込");
            title.style.color = new StyleColor(IsSave
                ? new Color(0.65f, 0.9f, 0.65f)    // 保存 = 緑系
                : new Color(1f, 0.8f, 0.5f));      // 読込 = 橙系
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            parent.Add(title);

            parent.Add(Caution(IsSave
                ? "既存ファイルを指定すると上書きされます。"
                : "読込は現在編集中のプロジェクトを置き換えます。"));

            // ── 書き込み先フォルダ（保存側のみ）──────────────────────
            // [...] はフォルダを決めるだけで、保存はしない。
            // ファイル名は「名前を付けて保存」のダイアログで毎回決める。
            if (IsSave)
            {
                _saveDest = new PlayerSaveDestRow(
                    "書き込み先フォルダ", SaveDest.Keys.Project, "プロジェクトの保存先", "mfproj",
                    () => "Project");
                parent.Add(_saveDest.Root);
                parent.Add(Divider());
            }

            // ── CSV セクション ────────────────────────────────────────
            parent.Add(SectionLabel("CSV (プロジェクトファイル)"));

            if (IsSave)
            {
                parent.Add(_btnSaveAsCsv = MakeWideBtn("名前を付けて保存", OnSaveAsCsvFile));
            }
            else
            {
                _csvPathField = new TextField();
                _csvPathField.tooltip = "プロジェクトCSVのファイルパス（任意名）。モデルフォルダは同じディレクトリ直下に置かれる。";
                _csvPathField.RegisterValueChangedCallback(e => RecentPaths.Set(CsvPathKey, e.newValue));
                parent.Add(MakePathRow(_csvPathField, OnOpenCsv, out _btnBrowseCsv));
                _csvPathField.SetValueWithoutNotify(RecentPaths.Get(CsvPathKey));

                _csvMergeToggle = new Toggle("追加マージ");
                _csvMergeToggle.tooltip = "指定ファイルと同じフォルダからメッシュを追加（名前重複時は置き換え）。"
                                        + "OFF のときは現在のプロジェクトを置き換える。";
                _csvMergeToggle.style.marginBottom = 2;
                parent.Add(_csvMergeToggle);

                parent.Add(_btnOpenCsv = MakeWideBtn("開く", OnOpenCsv));
            }

            // ── 区切り線 ──────────────────────────────────────────────
            parent.Add(Divider());

            // ── .mfproj(JSON) セクション ──────────────────────────────
            parent.Add(SectionLabel(".mfproj (JSON)"));

            if (IsSave)
            {
                parent.Add(_btnSaveAsJson = MakeWideBtn("名前を付けて保存", OnSaveAsJson));
            }
            else
            {
                _jsonPathField = new TextField();
                _jsonPathField.RegisterValueChangedCallback(e => RecentPaths.Set(JsonPathKey, e.newValue));
                parent.Add(MakePathRow(_jsonPathField, OnOpenJson, out _btnBrowseJson));
                _jsonPathField.SetValueWithoutNotify(RecentPaths.Get(JsonPathKey));

                parent.Add(_btnOpenJson = MakeWideBtn("開く", OnOpenJson));
            }

            // ── ステータス ───────────────────────────────────────────
            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.marginTop  = 4;
            parent.Add(_statusLabel);
        }

        /// <summary>
        /// パス欄を RecentPaths の最新値へ同期する。
        /// パネル表示時に呼ぶことで、もう一方のパネル（保存 ⇔ 読込）で
        /// 変更されたパスがこちらにも反映される。
        /// </summary>
        public void Refresh()
        {
            _saveDest?.Refresh();
            _jsonPathField?.SetValueWithoutNotify(RecentPaths.Get(JsonPathKey));
            _csvPathField?.SetValueWithoutNotify(RecentPaths.Get(CsvPathKey));
        }

        // ================================================================
        // open（「開く」＝ ダイアログで確定してから読込む）
        //
        // パス欄の値をそのまま読み込むと、意図しないファイルを開く事故が起きる。
        // 読込は必ずダイアログを通し、パス欄の値は初期フォルダ／初期ファイル名
        // としてだけ使う（保存側の AskSavePath と同じ考え方）。
        // ================================================================

        private void OnOpenJson()
        {
            string path = PlayerIoUiKit.AskLoadPath("プロジェクトを開く", JsonPathKey, _jsonPathField.value, "mfproj");
            if (string.IsNullOrEmpty(path)) return;
            _jsonPathField.value = path;
            OnLoad?.Invoke(path);
        }

        private void OnOpenCsv()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "プロジェクトCSVを開く", CsvPathKey, _csvPathField.value,
                CsvProjectSerializer.ProjectFileExtension);
            if (string.IsNullOrEmpty(path)) return;
            _csvPathField.value = path;
            OnLoadCsv?.Invoke(path, _csvMergeToggle != null && _csvMergeToggle.value);
        }

        // ================================================================
        // save（「名前を付けて保存」）— Save モード専用
        //
        // 書き込み先フォルダ欄が持つのはフォルダだけ。ファイル名は毎回
        // 保存ダイアログで決める。パス欄の値へ無確認で書き出す経路
        // （旧「上書き保存」）は保存事故の原因になるため廃止した。
        // ================================================================

        private void OnSaveAsJson()
        {
            if (_saveDest == null) return;

            _saveDest.DialogTitle = "プロジェクトの保存先";
            _saveDest.Extension   = "mfproj";

            string path = _saveDest.AskSavePath("Project");
            if (string.IsNullOrEmpty(path)) return;
            OnSave?.Invoke(path);
        }

        private void OnSaveAsCsvFile()
        {
            if (_saveDest == null) return;

            _saveDest.DialogTitle = "プロジェクトCSVの保存先";
            _saveDest.Extension   = CsvProjectSerializer.ProjectFileExtension;

            string path = _saveDest.AskSavePath("Project");
            if (string.IsNullOrEmpty(path)) return;
            OnSaveCsv?.Invoke(path);
        }

        // ================================================================
        // ステータス表示
        // ================================================================

        public void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        /// <summary>[...]（左）＋パス用 TextField（右）の行。</summary>
        private static VisualElement MakePathRow(TextField field, Action onBrowse, out Button browseButton)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var browse = new Button(onBrowse) { text = "..." };
            browse.style.width       = 28;
            browse.style.marginRight = 2;

            field.style.flexGrow = 1;

            row.Add(browse);
            row.Add(field);
            browseButton = browse;
            return row;
        }

        /// <summary>幅いっぱいのボタン（縦積み用）。</summary>
        private static Button MakeWideBtn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height       = 26;
            b.style.marginBottom = 2;
            return b;
        }

        private static VisualElement Divider()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 6;
            v.style.marginBottom    = 6;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.color        = new StyleColor(new Color(0.6f, 0.8f, 1f));
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label Caution(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.color        = new StyleColor(new Color(0.8f, 0.8f, 0.8f));
            l.style.marginBottom = 4;
            return l;
        }
    }
}
