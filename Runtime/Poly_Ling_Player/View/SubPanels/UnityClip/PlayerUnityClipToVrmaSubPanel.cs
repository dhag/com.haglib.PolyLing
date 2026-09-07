// PlayerUnityClipToVrmaSubPanel.cs
// ============================================================
// Unity クリップ → VRMA 変換パネル（モデル非依存）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Player/View/SubPanels/UnityClip/ に配置。
//
// 【PlayerUnityClipTestSubPanel との違い】
//   あちらはモデルへクリップを適用して確認するための道具で、
//   Humanoid 割り当て済みのモデルが要る。
//   こちらは T ポーズ基準の正準骨格へ載せるだけなので、モデルを一切見ない。
//   したがって GetModel も OnFrameApplied も持たない。
//
// 【操作】
//   [開く] で保存済みの Unity クリップ JSON を指定 → [保存] で .vrma を書き出す。
//
// 【パスと PLSandbox】
//   PLSandbox の 1 回許可は解決した時点で消える（PLSandbox.cs:224-226）ため、
//   コマンドを送る直前にクリップのパスへ付け直す。
//   書き出し先は毎回 AskSavePath を通す（PlayerMeshSelectionSetSubPanel と同じ規則）。
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.UnityClip;
using Poly_Ling.Vrm;

namespace Poly_Ling.Player
{
    public class PlayerUnityClipToVrmaSubPanel
    {
        // ================================================================
        // 依存
        // ================================================================

        /// <summary>コマンドの発行口。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>現在のモデル索引。コマンドの基底が要求するだけで、変換には使わない。</summary>
        public Func<int> GetModelIndex;

        // ================================================================
        // 状態
        // ================================================================

        private UnityClipDTO _clip;
        private string       _clipPath;
        private float        _maxTime;

        private TextField  _clipPathField;
        private TextField  _vrmaPathField;
        private Label      _clipInfoLabel;
        private Label      _statusLabel;
        private Label      _availLabel;
        private Button     _btnSave;
        private Button     _btnClear;
        private FloatField _fpsField;
        private FloatField _startField;
        private FloatField _endField;
        private FloatField _boneLenField;

        private const string ClipPathKey = "UnityClipToVrma.Clip.Path";
        private const string VrmaPathKey = "UnityClipToVrma.Vrma.Path";

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Unityクリップ→VRMA変換"));

            var note = new Label(
                "T ポーズ基準で変換します。モデルは使いません。\n" +
                "出るのは Humanoid ボーンの回転だけです（表情・二次骨・移動は載りません）。");
            note.style.fontSize   = 10;
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 4;
            root.Add(note);

            // ── 入力 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("Unity Clip (JSON)"));
            _clipPathField = new TextField();
            _clipPathField.RegisterValueChangedCallback(e => RecentPaths.Set(ClipPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_clipPathField, OnBrowseClip));
            _clipPathField.SetValueWithoutNotify(RecentPaths.Get(ClipPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseClip);
            btnOpen.style.flexGrow = 1; btnOpen.style.marginRight = 2;
            _btnClear = new Button(Clear) { text = "クリア" };
            _btnClear.style.width = 64;
            opRow.Add(btnOpen); opRow.Add(_btnClear);
            root.Add(opRow);

            _clipInfoLabel = new Label("(未読込)");
            _clipInfoLabel.style.fontSize     = 10;
            _clipInfoLabel.style.whiteSpace   = WhiteSpace.Normal;
            _clipInfoLabel.style.marginBottom = 4;
            root.Add(_clipInfoLabel);

            // ── 出力 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("書き出し先 (.vrma)"));
            _vrmaPathField = new TextField();
            _vrmaPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VrmaPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_vrmaPathField, OnBrowseVrma));
            _vrmaPathField.SetValueWithoutNotify(RecentPaths.Get(VrmaPathKey));

            // ── 設定 ─────────────────────────────────────────────────
            root.Add(SecLabel("設定"));

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom  = 2;
            row1.Add(NumField("FPS", out _fpsField, 30f));
            row1.Add(NumField("骨長", out _boneLenField, 0.1f));
            root.Add(row1);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginBottom  = 4;
            row2.Add(NumField("Start", out _startField, 0f));
            row2.Add(NumField("End", out _endField, 0f));
            root.Add(row2);

            _btnSave = new Button(OnSave) { text = "保存" };
            _btnSave.style.height       = 24;
            _btnSave.style.marginBottom = 2;
            root.Add(_btnSave);

            _availLabel = new Label();
            _availLabel.style.fontSize   = 10;
            _availLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_availLabel);

            _statusLabel = new Label();
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            RefreshAll();
        }

        private static VisualElement NumField(string label, out FloatField field, float initial)
        {
            var box = new VisualElement();
            box.style.flexDirection = FlexDirection.Row;
            box.style.flexGrow      = 1;
            box.style.marginRight   = 4;

            var lbl = new Label(label);
            lbl.style.width          = 44;
            lbl.style.fontSize       = 10;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;

            field = new FloatField { value = initial };
            field.style.flexGrow = 1;

            box.Add(lbl);
            box.Add(field);
            return box;
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh() => RefreshAll();

        private void RefreshAll()
        {
            bool available = PLVrmAnimationBridge.I.IsAvailable;

            if (_availLabel != null)
                _availLabel.text = available
                    ? string.Empty
                    : "VRM パッケージ (com.vrmc.vrm) が無いため書き出せません";

            if (_clipInfoLabel != null)
            {
                if (_clip == null)
                {
                    _clipInfoLabel.text = "(未読込)";
                }
                else
                {
                    int muscles = _clip.muscles?.Count ?? 0;
                    int bones   = _clip.bones?.Count ?? 0;
                    _clipInfoLabel.text =
                        $"Clip: {_clip.name}  ({_clip.clipType})\n" +
                        $"Length: {_maxTime:F2}s  (@ {(_clip.frameRate > 0f ? _clip.frameRate : 30f):F0}fps)\n" +
                        $"Muscles: {muscles}   Bone tracks: {bones}（変換には muscles のみ使用）";
                }
            }

            if (_btnClear != null) _btnClear.SetEnabled(_clip != null);
            if (_btnSave != null)
                _btnSave.SetEnabled(available && _clip != null && SendCommand != null);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnBrowseClip()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "Unity クリップを開く", ClipPathKey, _clipPathField.value, "json");
            if (string.IsNullOrEmpty(path)) return;
            _clipPathField.value = path;
            LoadClip(path);
        }

        private void LoadClip(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))
            { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            try
            {
                _clip     = UnityClipSerializer.LoadJson(path);
                _clipPath = path;
                _maxTime  = UnityClipVrmAnimationSource.ComputeMaxTime(_clip);

                // 読み込んだクリップの実値を欄へ入れる。
                // 0 を番兵として利用者に入力させない。
                float fps = (_clip != null && _clip.frameRate > 0f) ? _clip.frameRate : 30f;
                _fpsField?.SetValueWithoutNotify(fps);
                _startField?.SetValueWithoutNotify(0f);
                _endField?.SetValueWithoutNotify(_maxTime);

                int muscles = _clip?.muscles?.Count ?? 0;
                SetStatus(muscles > 0
                    ? $"読込み完了: {Path.GetFileName(path)}"
                    : "読込みましたが muscles がありません。Humanoid クリップを指定してください");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"読込み失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerUnityClipToVrmaSubPanel] {ex}");
            }
        }

        private void Clear()
        {
            _clip = null; _clipPath = null; _maxTime = 0f;
            _fpsField?.SetValueWithoutNotify(30f);
            _startField?.SetValueWithoutNotify(0f);
            _endField?.SetValueWithoutNotify(0f);
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void OnBrowseVrma()
        {
            string path = AskVrmaSavePath();
            if (string.IsNullOrEmpty(path)) return;
            _vrmaPathField.value = path;
        }

        // 書き出しは必ず保存ダイアログを通す。パス欄の値は初期値としてだけ使う。
        private string AskVrmaSavePath()
        {
            string cur     = _vrmaPathField?.value?.Trim() ?? string.Empty;
            string defName = (_clip != null && !string.IsNullOrEmpty(_clip.name)) ? _clip.name : "motion";
            return PlayerIoUiKit.AskSavePath(
                "VRM アニメーションの書き出し", VrmaPathKey, cur, defName, "vrma");
        }

        private void OnSave()
        {
            if (_clip == null || string.IsNullOrEmpty(_clipPath))
            { SetStatus("クリップを読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }
            if (!PLVrmAnimationBridge.I.IsAvailable)
            { SetStatus("VRM パッケージが無いため書き出せません"); return; }

            string outPath = AskVrmaSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _vrmaPathField.value = outPath;

            // クリップはこのパネルのダイアログで利用者が選んだもの。
            // 1 回許可は解決時に消えるので、送る直前に付け直す。
            PLSandbox.AllowOnceFromDialog(_clipPath);

            var since = DateTime.Now.AddSeconds(-2);

            SendCommand(new Poly_Ling.Data.ConvertUnityClipToVrmaCommand(
                GetModelIndex?.Invoke() ?? 0,
                outPath,
                _clipPath,
                _fpsField?.value     ?? 30f,
                _startField?.value   ?? 0f,
                _endField?.value     ?? 0f,
                _boneLenField?.value ?? 0.1f));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool ok = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            SetStatus(ok
                ? $"保存しました: {Path.GetFileName(outPath)}"
                : "保存に失敗しました。理由はコンソールログに出ています");
            RefreshAll();
        }

        // ================================================================
        // 小物
        // ================================================================

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
