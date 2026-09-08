// PlayerVmdToVrmaSubPanel.cs
// ============================================================
// VMD → VRMA 書き出しパネル
// ------------------------------------------------------------
// Runtime/Poly_Ling_Player/View/SubPanels/VMD/ に配置。
//
// 【PlayerVMDTestSubPanel との違い】
//   あちらは VMD をモデルへ適用して 1 フレームずつ確認するための道具。
//   こちらは区間を通しでサンプリングして .vrma を書き出すだけで、
//   フレームスライダも骨・モーフの一覧も持たない。
//
// 【PlayerUnityClipToVrmaSubPanel との違い】
//   あちらはモデルを見ない（マッスルだけで正準骨格へ載せる）。
//   VMD はマッスルを持たないので、こちらは必ずモデルを使う。
//   モデルに Humanoid 割り当てが要る。
//
// 【パスと PLSandbox】
//   PLSandbox の 1 回許可は解決した時点で消える（PLSandbox.cs:224-226）ため、
//   コマンドを送る直前に VMD のパスへ付け直す。
//   書き出し先は毎回 AskSavePath を通す（PlayerMeshSelectionSetSubPanel と同じ規則）。
//
// 【この段階で出ないもの】
//   T ポーズ補正の既定は腕のみ（VmdTPoseAlignScope.ArmsOnly）。体幹と指は無補正。
//   表情・二次骨・ミラー枝の反対側は載らない。
//   詳細は VmdVrmAnimationExport.cs 冒頭を見ること。
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.VMD;
using Poly_Ling.Vrm;

namespace Poly_Ling.Player
{
    public class PlayerVmdToVrmaSubPanel
    {
        // ================================================================
        // 依存
        // ================================================================

        /// <summary>対象モデル。Humanoid 割り当てが要る。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>現在のモデル索引。</summary>
        public Func<int> GetModelIndex;

        /// <summary>コマンドの発行口。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        // ================================================================
        // 状態
        // ================================================================

        private VMDData _vmd;
        private string  _vmdPath;
        private float   _maxTime;

        private TextField  _vmdPathField;
        private TextField  _vrmaPathField;
        private Label      _modelLabel;
        private Label      _vmdInfoLabel;
        private Label      _statusLabel;
        private Label      _availLabel;
        private Button     _btnSave;
        private Button     _btnClear;
        private Toggle     _ikToggle;
        private EnumField  _alignField;
        private Toggle     _diagToggle;
        private TextField  _ikTraceDirField;
        private Toggle     _ignoreLimitToggle;
        private Toggle     _kneePreBendToggle;
        private FloatField _fpsField;
        private FloatField _scaleField;
        private FloatField _startField;
        private FloatField _endField;

        private const string VmdPathKey     = "VmdToVrma.Vmd.Path";
        private const string VrmaPathKey    = "VmdToVrma.Vrma.Path";
        private const string IkTraceDirKey  = "VmdToVrma.IkTrace.Dir";

        private ModelContext Model => GetModel?.Invoke();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("VMD→VRMA書き出し"));

            var note = new Label(
                "モデルへ VMD を適用しながら書き出します。Humanoid 割り当てが要ります。\n" +
                "出るのは Hips の移動と Humanoid ボーンの回転だけです（表情・二次骨は載りません）。\n" +
                "T ポーズ補正は既定で腕（肩・上腕・前腕・手首）のみです。");
            note.style.fontSize     = 10;
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.marginBottom = 4;
            root.Add(note);

            _modelLabel = new Label("(モデル未選択)");
            _modelLabel.style.fontSize     = 10;
            _modelLabel.style.whiteSpace   = WhiteSpace.Normal;
            _modelLabel.style.marginBottom = 4;
            root.Add(_modelLabel);

            // ── 入力 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("VMD"));
            _vmdPathField = new TextField();
            _vmdPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VmdPathKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_vmdPathField, OnBrowseVmd));
            _vmdPathField.SetValueWithoutNotify(RecentPaths.Get(VmdPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseVmd);
            btnOpen.style.flexGrow = 1; btnOpen.style.marginRight = 2;
            _btnClear = new Button(Clear) { text = "クリア" };
            _btnClear.style.width = 64;
            opRow.Add(btnOpen); opRow.Add(_btnClear);
            root.Add(opRow);

            _vmdInfoLabel = new Label("(未読込)");
            _vmdInfoLabel.style.fontSize     = 10;
            _vmdInfoLabel.style.whiteSpace   = WhiteSpace.Normal;
            _vmdInfoLabel.style.marginBottom = 4;
            root.Add(_vmdInfoLabel);

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
            row1.Add(NumField("倍率", out _scaleField, 1f));
            root.Add(row1);

            var row2 = new VisualElement();
            row2.style.flexDirection = FlexDirection.Row;
            row2.style.marginBottom  = 4;
            row2.Add(NumField("Start", out _startField, 0f));
            row2.Add(NumField("End", out _endField, 0f));
            root.Add(row2);

            _ikToggle = new Toggle("IK を解いてから採取する") { value = true };
            _ikToggle.style.fontSize     = 10;
            _ikToggle.style.marginBottom = 2;
            _ikToggle.tooltip = "CCDIKSolver を通します。";
            _ikToggle.RegisterValueChangedCallback(e => RefreshAll());
            root.Add(_ikToggle);

            // IK の残差 CSV の出力先。空なら出力しない。
            root.Add(PlayerIoUiKit.SectionLabel("IK トレース出力先（空で出力しない）"));
            _ikTraceDirField = new TextField();
            _ikTraceDirField.RegisterValueChangedCallback(
                e => RecentPaths.Set(IkTraceDirKey, e.newValue));
            root.Add(PlayerIoUiKit.PathRow(_ikTraceDirField, OnBrowseIkTraceDir));
            _ikTraceDirField.SetValueWithoutNotify(RecentPaths.Get(IkTraceDirKey));

            _ignoreLimitToggle = new Toggle("角度制限を無視") { value = false };
            _ignoreLimitToggle.style.fontSize     = 10;
            _ignoreLimitToggle.style.marginBottom = 2;
            _ignoreLimitToggle.tooltip =
                "切り分け用。収束が止まる原因が角度制限かどうかを見ます。";
            root.Add(_ignoreLimitToggle);

            _kneePreBendToggle = new Toggle("ひざを事前に曲げる") { value = false };
            _kneePreBendToggle.style.fontSize     = 10;
            _kneePreBendToggle.style.marginBottom = 2;
            _kneePreBendToggle.tooltip =
                "角度制限を持つリンクを解く前に微小量だけ曲げます。角度制限を無視すると効きません。";
            root.Add(_kneePreBendToggle);

            _alignField = new EnumField("T ポーズ補正", VmdTPoseAlignScope.ArmsOnly);
            _alignField.style.fontSize     = 10;
            _alignField.style.marginBottom = 2;
            _alignField.tooltip =
                "None: 補正しない / ArmsOnly: 肩・上腕・前腕・手首の 8 本だけ / All: Humanoid 全ボーン（比較用）";
            root.Add(_alignField);

            _diagToggle = new Toggle("切り分けログを出す") { value = false };
            _diagToggle.style.fontSize     = 10;
            _diagToggle.style.marginBottom = 4;
            _diagToggle.tooltip = "開始時とフレーム 2 点だけログを出します。";
            root.Add(_diagToggle);

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
            var  model     = Model;

            if (_availLabel != null)
                _availLabel.text = available
                    ? string.Empty
                    : "VRM パッケージ (com.vrmc.vrm) が無いため書き出せません";

            if (_modelLabel != null)
            {
                _modelLabel.text = model == null
                    ? "(モデル未選択)"
                    : $"Model: {model.Name}";
            }

            if (_vmdInfoLabel != null)
            {
                if (_vmd == null)
                {
                    _vmdInfoLabel.text = "(未読込)";
                }
                else
                {
                    _vmdInfoLabel.text =
                        $"VMD: {_vmd.ModelName}\n" +
                        $"Length: {_maxTime:F2}s  (最終フレーム {_vmd.MaxFrameNumber} @ 30fps)\n" +
                        $"Bone keys: {_vmd.TotalBoneFrameCount}   Morph keys: {_vmd.TotalMorphFrameCount}";
                }
            }

            bool ikOn = _ikToggle != null && _ikToggle.value;
            if (_ikTraceDirField   != null) _ikTraceDirField.SetEnabled(ikOn);
            if (_ignoreLimitToggle != null) _ignoreLimitToggle.SetEnabled(ikOn);
            if (_kneePreBendToggle != null) _kneePreBendToggle.SetEnabled(ikOn);

            if (_btnClear != null) _btnClear.SetEnabled(_vmd != null);
            if (_btnSave != null)
                _btnSave.SetEnabled(available && _vmd != null && model != null && SendCommand != null);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnBrowseVmd()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "VMD を開く", VmdPathKey, _vmdPathField.value, "vmd");
            if (string.IsNullOrEmpty(path)) return;
            _vmdPathField.value = path;
            LoadVmd(path);
        }

        private void LoadVmd(string path)
        {
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))
            { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            try
            {
                _vmd     = VMDData.LoadFromFile(path);
                _vmdPath = path;
                _maxTime = VmdVrmAnimationExport.ComputeMaxTime(_vmd);

                // 読み込んだ VMD の実値を欄へ入れる。0 を番兵として利用者に入力させない。
                _fpsField?.SetValueWithoutNotify(VmdVrmAnimationExport.VmdFps);
                _startField?.SetValueWithoutNotify(0f);
                _endField?.SetValueWithoutNotify(_maxTime);

                SetStatus($"読込み完了: {Path.GetFileName(path)}");
                RefreshAll();
            }
            catch (Exception ex)
            {
                SetStatus($"読込み失敗: {ex.Message}");
                UnityEngine.Debug.LogError($"[PlayerVmdToVrmaSubPanel] {ex}");
            }
        }

        private void Clear()
        {
            _vmd = null; _vmdPath = null; _maxTime = 0f;
            _fpsField?.SetValueWithoutNotify(VmdVrmAnimationExport.VmdFps);
            _startField?.SetValueWithoutNotify(0f);
            _endField?.SetValueWithoutNotify(0f);
            SetStatus("クリアしました");
            RefreshAll();
        }

        private void OnBrowseIkTraceDir()
        {
            string path = PlayerIoUiKit.AskFolderPath(
                "IK トレースの出力先", IkTraceDirKey, _ikTraceDirField.value);
            if (string.IsNullOrEmpty(path)) return;
            _ikTraceDirField.value = path;
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
            // 書き込み先はフォルダだけを覚える。ファイル名は毎回この既定から始める。
            // 読み込んだ VMD の名前は「初期値」であって書き込み先ではない。
            string defName = !string.IsNullOrEmpty(_vmdPath)
                ? Path.GetFileNameWithoutExtension(_vmdPath)
                : "motion";
            return SaveDest.AskSavePath(
                "VRM アニメーションの書き出し", SaveDest.Keys.Vrma, "", defName, "vrma");
        }

        private void OnSave()
        {
            if (Model == null)       { SetStatus("モデルがありません"); return; }
            if (_vmd == null || string.IsNullOrEmpty(_vmdPath))
                                     { SetStatus("VMD を読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }
            if (!PLVrmAnimationBridge.I.IsAvailable)
                                     { SetStatus("VRM パッケージが無いため書き出せません"); return; }

            string outPath = AskVrmaSavePath();
            if (string.IsNullOrEmpty(outPath)) return;
            _vrmaPathField.value = outPath;

            // VMD はこのパネルのダイアログで利用者が選んだもの。
            // 1 回許可は解決時に消えるので、送る直前に付け直す。
            PLSandbox.AllowOnceFromDialog(_vmdPath);

            // IK トレース先も同じ規則。IK が off なら送らない。
            string ikTraceDir = (_ikToggle?.value ?? true)
                ? (_ikTraceDirField?.value?.Trim() ?? string.Empty)
                : string.Empty;
            if (!string.IsNullOrEmpty(ikTraceDir))
                PLSandbox.AllowOnceFromDialog(ikTraceDir);

            var since = DateTime.Now.AddSeconds(-2);

            SendCommand(new Poly_Ling.Data.ExportVmdToVrmaCommand(
                GetModelIndex?.Invoke() ?? 0,
                outPath,
                _vmdPath,
                _fpsField?.value   ?? 30f,
                _startField?.value ?? 0f,
                _endField?.value   ?? 0f,
                _scaleField?.value  ?? 1f,
                _ikToggle?.value   ?? true,
                (_alignField?.value is VmdTPoseAlignScope sc) ? sc : VmdTPoseAlignScope.ArmsOnly,
                _diagToggle?.value ?? false,
                ikTraceDir,
                _ignoreLimitToggle?.value ?? false,
                _kneePreBendToggle?.value ?? false));

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
