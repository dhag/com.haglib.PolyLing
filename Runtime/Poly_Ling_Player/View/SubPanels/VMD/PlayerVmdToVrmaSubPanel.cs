// PlayerVmdToVrmaSubPanel.cs
// ============================================================
// VMD ＋ モデル → PolyLing モーション（.plmotion.json）＋ VRM アニメーション（.vrma）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Player/View/SubPanels/VMD/ に配置。
//
// 【経路（PolyLing_VRMA整備計画.md 2.5）】
//   VMD はマッスルを持たないので、モデルへ当てながらマッスル＋二次骨＋表情を焼く（VmdMotionBake）。
//   焼いたものを .plmotion.json に保存し、「VRMA も書く」がオンなら同じ内容を .vrma へ書く
//   （ExportVmdToMotionCommand → VrmaCodec）。VRMA への出口は「.plmotion → .vrma」1 本で、
//   ここもその出口を通る。モデルに Humanoid 割り当てが要る。
//   .vrma は .plmotion.json と同じフォルダ・同じ名前で置く（x.plmotion.json → x.vrma）。
//
// 【PlayerVMDTestSubPanel との違い】
//   あちらは VMD をモデルへ適用して 1 フレームずつ確認するための道具。
//   こちらは区間を通しでサンプリングして書き出すだけで、フレームスライダも骨・モーフの一覧も持たない。
//
// 【パスと PLSandbox】
//   PLSandbox の 1 回許可は解決した時点で消える（PLSandbox.cs:224-226）ため、
//   コマンドを送る直前に VMD のパスへ付け直す。書き出し先は毎回 AskSavePath を通す。
//
//   T ポーズ補正は腕 8 本に固定（UnityClipApplier.IsArmAlignBone）。ミラー枝の反対側は載らない。
//   詳細は VmdVrmAnimationExport.cs / VmdMotionBake.cs 冒頭を見ること。
// ============================================================

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.VMD;

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

        // UI 自動操作の ID は "vmdToVrma.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("vmdPath", Description = "読み込む VMD のパス（ダイアログの初期値として使う）")]
        private TextField  _vmdPathField;
        [UiControl("model", Safety = UiSafety.ReadOnly, Description = "対象モデル")]
        private Label      _modelLabel;
        [UiControl("vmdInfo", Safety = UiSafety.ReadOnly, Description = "読み込んだ VMD の情報")]
        private Label      _vmdInfoLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label      _statusLabel;
        [UiControl("save", Safety = UiSafety.UserOnly, Description = "PolyLing モーション（と VRMA）を保存する（保存ダイアログを開く）")]
        private Button     _btnSave;
        [UiControl("clear", Safety = UiSafety.Destructive, Description = "読み込んだ VMD を外す")]
        private Button     _btnClear;
        [UiControl("open", Safety = UiSafety.UserOnly, Description = "VMD を開く（ファイル選択ダイアログを開く）")]
        private Button     _btnOpen;
        [UiControl("browseVmd", Safety = UiSafety.UserOnly, Description = "VMD の [...]（ファイル選択ダイアログを開く）")]
        private Button     _btnBrowseVmd;
        [UiControl("browseIkTraceDir", Safety = UiSafety.UserOnly, Description = "IK トレースの出力先の [...]（フォルダ選択ダイアログを開く）")]
        private Button     _btnBrowseIkTraceDir;
        [UiControl("writeVrma", Description = "PolyLing モーションと同じ名前で VRMA も書く（既定オン）")]
        private Toggle     _vrmaToggle;
        [UiControl("solveIk", Description = "IK を解いてから採取する")]
        private Toggle     _ikToggle;
        [UiControl("diagnostics", Description = "切り分けログを出す")]
        private Toggle     _diagToggle;
        [UiControl("ikTraceDir", Description = "IK トレースの出力先（ダイアログの初期値として使う）")]
        private TextField  _ikTraceDirField;
        [UiControl("ignoreAngleLimits", Description = "角度制限を無視する")]
        private Toggle     _ignoreLimitToggle;
        [UiControl("kneePreBend", Description = "ひざを事前に曲げる")]
        private Toggle     _kneePreBendToggle;
        [UiControl("alignLegs", Description = "T ポーズ合わせを脚（太もも・すね）にも掛ける。切は腕だけ。再生（統合モーションテスト）でも同じ設定にすること")]
        private Toggle     _alignLegsToggle;
        [UiControl("fps", Description = "採取のフレームレート")]
        private FloatField _fpsField;
        [UiControl("startFrame", Description = "採取の開始時刻（秒）")]
        private FloatField _startField;
        [UiControl("endFrame", Description = "採取の終了時刻（秒）")]
        private FloatField _endField;

        private const string VmdPathKey     = "VmdToVrma.Vmd.Path";
        private const string IkTraceDirKey  = "VmdToVrma.IkTrace.Dir";
        // 統合モーションテストの「脚も T ポーズに合わせる」と同じキー（焼き込みと再生をそろえる）
        private const string AlignLegsKey   = "MotionClip.AlignLegs";

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

            root.Add(SecLabel("VMD→モーション・VRMA書き出し"));

            var note = new Label(
                "モデルへ VMD を適用しながら、マッスル・二次骨・表情の PolyLing モーションを書き出します。" +
                "Humanoid 割り当てが要ります。\n" +
                "「VRMA も書く」がオンなら、同じ内容を同じ名前の .vrma にも書きます（二次骨は VRMA に載りません）。\n" +
                "T ポーズ補正は腕（肩・上腕・前腕・手首）だけに掛かります。");
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
            root.Add(PlayerIoUiKit.PathRow(_vmdPathField, OnBrowseVmd, out _btnBrowseVmd));
            _vmdPathField.SetValueWithoutNotify(RecentPaths.Get(VmdPathKey));

            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 3;
            var btnOpen = PlayerIoUiKit.OpenButton("開く", OnBrowseVmd);
            _btnOpen = btnOpen;
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

            // ── 設定 ─────────────────────────────────────────────────
            root.Add(SecLabel("設定"));

            var row1 = new VisualElement();
            row1.style.flexDirection = FlexDirection.Row;
            row1.style.marginBottom  = 2;
            row1.Add(NumField("FPS", out _fpsField, 30f));
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
            root.Add(PlayerIoUiKit.PathRow(_ikTraceDirField, OnBrowseIkTraceDir, out _btnBrowseIkTraceDir));
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

            // T ポーズ合わせの範囲。記憶は統合モーションテストのチェックと同じキー（焼き込みと再生をそろえるため）。
            _alignLegsToggle = new Toggle("脚も T ポーズに合わせる（切は腕だけ）")
            {
                value = RecentPaths.Get(AlignLegsKey) == "1",
            };
            _alignLegsToggle.style.fontSize     = 10;
            _alignLegsToggle.style.marginBottom = 2;
            _alignLegsToggle.tooltip = "焼き込みと再生で同じ設定にしてください。";
            _alignLegsToggle.RegisterValueChangedCallback(e => RecentPaths.Set(AlignLegsKey, e.newValue ? "1" : "0"));
            root.Add(_alignLegsToggle);

            _diagToggle = new Toggle("切り分けログを出す") { value = false };
            _diagToggle.style.fontSize     = 10;
            _diagToggle.style.marginBottom = 2;
            _diagToggle.tooltip = "開始時とフレーム 2 点だけログを出します。";
            root.Add(_diagToggle);

            _vrmaToggle = new Toggle("VRMA も書く（同じ名前の .vrma）") { value = true };
            _vrmaToggle.style.fontSize     = 10;
            _vrmaToggle.style.marginBottom = 4;
            root.Add(_vrmaToggle);

            _btnSave = new Button(OnSave) { text = "保存" };
            _btnSave.style.height       = 24;
            _btnSave.style.marginBottom = 2;
            root.Add(_btnSave);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(PlayerIoUiKit.StatusColor);
            _statusLabel.style.marginTop  = 4;
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
            var model = Model;

            if (_modelLabel != null)
                _modelLabel.text = model == null ? "(モデル未選択)" : $"Model: {model.Name}";

            if (_vmdInfoLabel != null)
            {
                _vmdInfoLabel.text = _vmd == null
                    ? "(未読込)"
                    : $"VMD: {_vmd.ModelName}\n" +
                      $"Length: {_maxTime:F2}s  (最終フレーム {_vmd.MaxFrameNumber} @ 30fps)\n" +
                      $"Bone keys: {_vmd.TotalBoneFrameCount}   Morph keys: {_vmd.TotalMorphFrameCount}";
            }

            bool ikOn = _ikToggle != null && _ikToggle.value;
            if (_ikTraceDirField   != null) _ikTraceDirField.SetEnabled(ikOn);
            if (_ignoreLimitToggle != null) _ignoreLimitToggle.SetEnabled(ikOn);
            if (_kneePreBendToggle != null) _kneePreBendToggle.SetEnabled(ikOn);

            if (_btnClear != null) _btnClear.SetEnabled(_vmd != null);
            if (_btnSave  != null) _btnSave.SetEnabled(_vmd != null && model != null && SendCommand != null);
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

        /// <summary>x.plmotion.json → 同じフォルダの x.vrma。</summary>
        public static string VrmaPathFor(string motionPath)
        {
            string dir  = Path.GetDirectoryName(motionPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(motionPath);   // x.plmotion
            const string suffix = ".plmotion";
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - suffix.Length);
            return Path.Combine(dir, name + ".vrma");
        }

        private void OnSave()
        {
            if (Model == null)       { SetStatus("モデルがありません"); return; }
            if (_vmd == null || string.IsNullOrEmpty(_vmdPath))
                                     { SetStatus("VMD を読み込んでください"); return; }
            if (SendCommand == null) { SetStatus("コマンドの発行口がありません"); return; }

            string defName = Path.GetFileNameWithoutExtension(_vmdPath) + ".plmotion.json";
            string outPath = SaveDest.AskSavePath(
                "PolyLing モーションの書き出し", SaveDest.Keys.Motion, "", defName, "json");
            if (string.IsNullOrEmpty(outPath)) return;

            bool   writeVrma = _vrmaToggle?.value ?? true;
            string vrmaPath  = writeVrma ? VrmaPathFor(outPath) : string.Empty;

            // VMD と出力先はこのパネルのダイアログで利用者が選んだもの。
            // 1 回許可は解決時に消えるので、送る直前に付け直す。
            PLSandbox.AllowOnceFromDialog(_vmdPath);
            PLSandbox.AllowOnceFromDialog(outPath);
            if (writeVrma) PLSandbox.AllowOnceFromDialog(vrmaPath);

            // IK トレース先も同じ規則。IK が off なら送らない。
            string ikTraceDir = (_ikToggle?.value ?? true)
                ? (_ikTraceDirField?.value?.Trim() ?? string.Empty)
                : string.Empty;
            if (!string.IsNullOrEmpty(ikTraceDir))
                PLSandbox.AllowOnceFromDialog(ikTraceDir);

            var since = DateTime.Now.AddSeconds(-2);

            SendCommand(new Poly_Ling.Data.ExportVmdToMotionCommand(
                GetModelIndex?.Invoke() ?? 0,
                outPath,
                _vmdPath,
                vrmaPath,
                _fpsField?.value   ?? 30f,
                _startField?.value ?? 0f,
                _endField?.value   ?? 0f,
                _ikToggle?.value   ?? true,
                _diagToggle?.value ?? false,
                ikTraceDir,
                _ignoreLimitToggle?.value ?? false,
                _kneePreBendToggle?.value ?? false,
                _alignLegsToggle?.value ?? false));

            // Dispatch は同期なので、書き出し結果をここで確かめる。
            bool okMotion = File.Exists(outPath) && File.GetLastWriteTime(outPath) >= since;
            bool okVrma   = !writeVrma || (File.Exists(vrmaPath) && File.GetLastWriteTime(vrmaPath) >= since);
            SetStatus(okMotion && okVrma
                ? $"保存しました: {Path.GetFileName(outPath)}" + (writeVrma ? $" / {Path.GetFileName(vrmaPath)}" : "")
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
