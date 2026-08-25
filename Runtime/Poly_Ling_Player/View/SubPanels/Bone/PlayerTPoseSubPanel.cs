// PlayerTPoseSubPanel.cs
// TPosePanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public class PlayerTPoseSubPanel
    {
        public Func<ModelContext>    GetModel;
        public Func<ToolContext>     GetToolContext;
        /// <summary>PanelCommand を送信するコールバック。</summary>
        public Action<PanelCommand> SendCommand;
        /// <summary>モデルインデックスを返すデリゲート。</summary>
        public Func<int>             GetModelIndex;

        private Label         _warningLabel;
        private VisualElement _mainContent;
        private Label         _mappingInfoLabel;
        private Button        _btnApplyTPose;
        private VisualElement _backupSection;
        private Label         _backupStatusLabel;
        private Button        _btnRestore;
        private Toggle        _toggleBake;
        private Button        _btnBake;
        private Label         _noBackupLabel;
        private Label         _statusLabel;
        private Button        _btnSaveOriginCsv;
        private Toggle        _toggleBakeRotToPos;

        // ボーンを持たないモデル（MeshFilter 相当）用のマッピング読み込み
        private VisualElement _csvSection;
        private Label         _csvStatusLabel;

        /// <summary>Humanoidマッピング CSV の最近使ったパス。</summary>
        private const string MappingCsvRecentKey = "TPose.MappingCsv.Path";

        /// <summary>原点CSVの最近使ったパス。ボーンエディタの原点CSV書出・読込と共有する。</summary>
        private const string OriginCsvRecentKey = "BoneEditor.OriginCsv.Path";

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Tポーズ変換"));

            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // ── マッピングCSV（ボーンが無いモデル用）────────────────────
            // スキンド化していない MeshFilter 相当の階層でも、CSV でマッピングを
            // 与えれば Tポーズ化できるようにする。候補はボーンに限らず
            // MeshContextList 全件（索引 = マスター索引）を対象にする。
            _csvSection = new VisualElement();
            _csvSection.style.marginBottom = 6;

            _csvSection.Add(SecLabel("マッピングCSV（ボーンが無いモデルでも可）"));

            var btnLoadCsv = new Button(OnLoadMappingCsv) { text = "CSVを読み込んでマッピング" };
            btnLoadCsv.style.height = 24;
            btnLoadCsv.tooltip =
                "UnityHumanoidName,Alias1,... 形式のCSVを読み込み、モデル内の全オブジェクト名と\n" +
                "名前一致でマッピングする（ボーン・メッシュを問わない）";
            _csvSection.Add(btnLoadCsv);

            _csvStatusLabel = new Label();
            _csvStatusLabel.style.fontSize   = 10;
            _csvStatusLabel.style.color      = new StyleColor(Color.white);
            _csvStatusLabel.style.whiteSpace = WhiteSpace.Normal;
            _csvStatusLabel.style.marginTop  = 2;
            _csvSection.Add(_csvStatusLabel);

            root.Add(_csvSection);
            root.Add(MakeSep());

            _mainContent = new VisualElement();
            _mainContent.style.display = DisplayStyle.None;
            root.Add(_mainContent);

            _mappingInfoLabel = new Label();
            _mappingInfoLabel.style.color       = new StyleColor(Color.white);
            _mappingInfoLabel.style.marginBottom = 8;
            _mainContent.Add(_mappingInfoLabel);

            _btnApplyTPose = new Button(OnApplyTPose) { text = "Tポーズに変換" };
            _btnApplyTPose.style.height       = 28;
            _btnApplyTPose.style.marginBottom = 8;
            _mainContent.Add(_btnApplyTPose);

            _mainContent.Add(MakeSep());

            // バックアップあり
            _backupSection = new VisualElement();
            _backupSection.style.display = DisplayStyle.None;
            _backupStatusLabel = new Label();
            _backupStatusLabel.style.color       = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            _backupStatusLabel.style.marginBottom = 6;
            _backupSection.Add(_backupStatusLabel);
            _btnRestore = new Button(OnRestoreOriginal) { text = "元の姿勢に戻す" };
            _btnRestore.style.marginBottom = 4;
            _backupSection.Add(_btnRestore);
            _toggleBake = new Toggle("元の姿勢にベイク（バックアップを破棄）");
            _toggleBake.style.color = new StyleColor(Color.white);
            _toggleBake.style.marginBottom = 2;
            _toggleBake.RegisterValueChangedCallback(e =>
                { if (_btnBake != null) _btnBake.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None; });
            _backupSection.Add(_toggleBake);
            _btnBake = new Button(OnBake) { text = "Bake" };
            _btnBake.style.display     = DisplayStyle.None;
            _btnBake.style.marginBottom = 4;
            _backupSection.Add(_btnBake);
            _mainContent.Add(_backupSection);

            // バックアップなし
            _noBackupLabel = new Label();
            _noBackupLabel.style.color       = new StyleColor(Color.white);
            _noBackupLabel.style.marginBottom = 6;
            _mainContent.Add(_noBackupLabel);

            // 現在の姿勢を原点CSVとして退避する。
            // Tポーズ変換が階層に書くのは腕の回転なので、回転列は常に付ける。
            _mainContent.Add(MakeSep());

            _toggleBakeRotToPos = new Toggle("回転を位置に変換して保存") { value = true };
            _toggleBakeRotToPos.style.color = new StyleColor(Color.white);
            _toggleBakeRotToPos.style.marginBottom = 2;
            _toggleBakeRotToPos.tooltip =
                "読込後に各オブジェクトのワールド原点が今と同じ場所へ来るローカル位置を書き、\n" +
                "回転は 0 で書く（読込後の階層を計算して逆算する）。\n" +
                "保存時と同じ姿勢のモデルへ読み込む前提。読込側はボーンを適用対象に\n" +
                "しないため、姿勢の違うモデルではボーン配下のオブジェクトは一致しない。\n" +
                "オフのときは BoneTransform の位置・回転をそのまま書く。";
            _mainContent.Add(_toggleBakeRotToPos);

            _btnSaveOriginCsv = new Button(OnSaveOriginCsv) { text = "現在の姿勢を原点CSVに保存" };
            _btnSaveOriginCsv.style.height       = 24;
            _btnSaveOriginCsv.style.marginBottom = 4;
            _btnSaveOriginCsv.tooltip =
                "name,posX,posY,posZ,rotX,rotY,rotZ 形式で書き出す（ボーン行も含む）。\n" +
                "ボーンエディタの「原点CSV読込」で読める書式。モデルは変更しない。";
            _mainContent.Add(_btnSaveOriginCsv);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.color      = new StyleColor(Color.white);
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _mainContent.Add(_statusLabel);
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var model = GetModel?.Invoke();
            if (model == null)
            {
                if (_csvSection != null) _csvSection.style.display = DisplayStyle.None;
                ShowWarning("モデルがありません");
                return;
            }

            // CSV セクションはマッピングの有無に関わらず出す（ここから設定できるようにする）
            if (_csvSection != null) _csvSection.style.display = DisplayStyle.Flex;

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                ShowWarning("Humanoidボーンマッピングが未設定です。\n" +
                            "上のCSV読み込み、または Humanoid Mapping パネルで設定してください。");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            bool hasSkin = TPoseConverter.HasAnySkinWeight(model.MeshContextList);
            _mappingInfoLabel.text = $"マッピング済: {mapping.Count} 件" +
                (hasSkin ? "（スキンド：頂点を焼き込みます）"
                         : "（スキンなし：階層の姿勢だけを変えます）");

            RefreshBackupSection(model);
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnApplyTPose()
        {
            var model = GetModel?.Invoke(); if (model == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;

            // 変換前に何が起きるかを確定させておく。
            // 「押しても反応がない」ときに、どこで止まったかがそのまま残る。
            string diag = TPoseConverter.Diagnose(model.MeshContextList, model.HumanoidMapping);
            Debug.Log("[TPose診断]\n" + diag);

            if (SendCommand != null)
            {
                SendCommand.Invoke(new ApplyTPoseCommand(modelIdx));
                SetStatus(diag);
                Refresh();
                return;
            }
            // フォールバック
            var tc      = GetToolContext?.Invoke();
            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;
            var beforeState    = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
            var oldTPoseBackup = model.TPoseBackup;
            var backup = new TPoseBackup();
            TPoseConverter.ConvertToTPose(model.MeshContextList, mapping, backup);
            model.TPoseBackup = backup;
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, afterState);
            var undo = tc?.UndoController;
            if (undo != null)
            {
                {
                    string __dbgDesc = "Apply T-Pose";
                    var __record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                    PLDiag.UndoRecord("MeshList", __dbgDesc, __record);
                    undo.MeshListStack.Record(__record, __dbgDesc);
                }
            }
            model.IsDirty = true;
            tc?.NotifyTopologyChanged?.Invoke();
            tc?.Repaint?.Invoke();
            SetStatus("Tポーズを適用しました。バックアップを保存しました。");
            Refresh();
        }

        private void OnRestoreOriginal()
        {
            var model = GetModel?.Invoke(); if (model?.TPoseBackup == null) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
            {
                SendCommand.Invoke(new RestoreTPoseCommand(modelIdx));
                SetStatus("元の姿勢に戻しました。");
                Refresh();
                return;
            }
            // フォールバック
            var tc = GetToolContext?.Invoke();
            var beforeState    = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
            var oldTPoseBackup = model.TPoseBackup;
            TPoseConverter.RestoreFromBackup(model.MeshContextList, model.TPoseBackup);
            var afterState = new TPoseBackup();
            TPoseConverter.CaptureBackup(model.MeshContextList, afterState);
            model.TPoseBackup = null;
            var undo = tc?.UndoController;
            if (undo != null)
            {
                {
                    string __dbgDesc = "Restore Original Pose";
                    var __record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, null, "Restore Original Pose");
                    PLDiag.UndoRecord("MeshList", __dbgDesc, __record);
                    undo.MeshListStack.Record(__record, __dbgDesc);
                }
            }
            model.IsDirty = true;
            tc?.NotifyTopologyChanged?.Invoke();
            tc?.Repaint?.Invoke();
            SetStatus("元の姿勢に戻しました。");
            Refresh();
        }

        private void OnBake()
        {
            var model = GetModel?.Invoke(); if (model?.TPoseBackup == null) return;
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("Tポーズ変換", "元の姿勢のバックアップを破棄しますか？\nこの操作は元に戻せません。", "OK", "Cancel");
            if (!ok) return;
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            if (SendCommand != null)
                SendCommand.Invoke(new BakeTPoseCommand(modelIdx));
            else
                model.TPoseBackup = null;
            SetStatus("バックアップを破棄しました。現在の姿勢がベース姿勢になります。");
            Refresh();
        }

        /// <summary>
        /// 現在の姿勢を原点CSVとして書き出す。
        /// 書き出すだけで、モデルのデータは変えない（ワールド行列のキャッシュ更新のみ）。
        ///
        /// 値は「回転を位置に変換して保存」トグルで決まる。
        ///   オン : 読込後にワールド原点が今と同じ場所へ来るローカル位置、回転 = 0
        ///   オフ : BoneTransform.Position / Rotation をそのまま
        ///
        /// 読込側（ApplyObjectOrigins）は MeshType.Bone を適用対象から外すため、
        /// ボーン行は同名の非ボーンメッシュがある場合だけ効く。
        /// </summary>
        private void OnSaveOriginCsv()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルがありません"); return; }

            string path = RecentFileDialog.AskSave(
                "原点CSVの書き出し", OriginCsvRecentKey,
                SanitizeFileName(model.Name) + "_tpose_origin", "csv");
            if (string.IsNullOrEmpty(path)) return;

            bool bakeRotToPos = _toggleBakeRotToPos?.value ?? true;

            string csv = Poly_Ling.Tools.ObjectPose.ObjectOriginCsv.Build(
                model, withRotation: true, includeBones: true,
                bakeRotationToPosition: bakeRotToPos,
                out int count, out int skippedMirror, out int skippedWedge);

            try
            {
                File.WriteAllText(path, csv, new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TPose] 原点CSVの書き出しに失敗: {ex.Message}");
                SetStatus("原点CSVの書き出しに失敗しました");
                return;
            }

            Debug.Log($"[TPose] 現在の姿勢を原点CSVに保存: {count} 件 → {path}" +
                      $"（回転を位置に変換={bakeRotToPos} / " +
                      $"除外: ミラー {skippedMirror} 件・姿勢くさび {skippedWedge} 件）");
            SetStatus($"原点CSVに保存しました: {count} 件" +
                      $"（{(bakeRotToPos ? "回転を位置に変換" : "位置・回転をそのまま")}" +
                      $" / 除外: ミラー {skippedMirror} 件・姿勢くさび {skippedWedge} 件）");
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "model";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>
        /// マッピングCSVを読み込み、モデル全体の名前と突き合わせて適用する。
        ///
        /// 候補はボーンに限らず MeshContextList 全件で、名前をその索引位置に置く
        /// （索引 = マスター索引）。HumanoidBoneMapping が期待する索引規約と一致し、
        /// スキンド化していない MeshFilter 相当の階層でもそのまま骨格として扱える。
        /// 名前の無い位置は空文字にしておく。FindBoneByAliases は空文字にヒットしない。
        /// </summary>
        private void OnLoadMappingCsv()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetCsvStatus("モデルがありません"); return; }

            string path = RecentFileDialog.AskLoad(
                "Humanoidマッピング CSV の読み込み", MappingCsvRecentKey, "csv");
            if (string.IsNullOrEmpty(path)) return;

            List<string> csvLines;
            try
            {
                csvLines = new List<string>(File.ReadAllLines(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerTPoseSubPanel] CSV 読み込みに失敗: {ex.Message}");
                SetCsvStatus("CSV の読み込みに失敗しました");
                return;
            }

            // 索引 = マスター索引 になるよう、全コンテキストぶんの名前リストを作る
            var names = new List<string>(model.MeshContextCount);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                names.Add(mc != null && !string.IsNullOrEmpty(mc.Name) ? mc.Name : "");
            }

            var mapping = new HumanoidBoneMapping();
            int count = mapping.LoadFromCSV(csvLines, names);

            if (count == 0)
            {
                SetCsvStatus("マッピングできませんでした。1列目が Humanoid 名（Hips / LeftUpperArm など）の\n" +
                             "CSVかどうか、別名列がモデルのオブジェクト名と一致するかを確認してください。");
                return;
            }

            // 腕が取れないと Tポーズ変換は何もできないので、その場で伝える
            bool hasLeft  = mapping.GetArmBoneIndices(true,  out _, out _);
            bool hasRight = mapping.GetArmBoneIndices(false, out _, out _);

            SendCommand?.Invoke(new ApplyHumanoidMappingCommand(
                GetModelIndex?.Invoke() ?? 0, mapping.Clone()));

            string armInfo = (hasLeft && hasRight) ? ""
                : $"（腕の解決: 左={(hasLeft ? "OK" : "不足")} / 右={(hasRight ? "OK" : "不足")}）";
            SetCsvStatus($"マッピングを適用しました: {count} 件 {armInfo}");

            if (!hasLeft && !hasRight)
                Debug.LogWarning("[PlayerTPoseSubPanel] 左右とも腕が解決していません。" +
                                 "Tポーズ変換は何も行いません。読み込んだCSVが" +
                                 "Humanoidマッピング用か確認してください。");

            Refresh();
        }

        private void SetCsvStatus(string s) { if (_csvStatusLabel != null) _csvStatusLabel.text = s; }

        // ── Helpers ──────────────────────────────────────────────────────
        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            if (_mainContent != null) _mainContent.style.display = DisplayStyle.None;
        }

        private void RefreshBackupSection(ModelContext model)
        {
            if (model.TPoseBackup != null)
            {
                _backupSection.style.display = DisplayStyle.Flex;
                _backupStatusLabel.text      = "✓ 元の姿勢のバックアップあり（復元可能）";
                _noBackupLabel.style.display = DisplayStyle.None;
                if (_toggleBake != null) { _toggleBake.value = false; }
                if (_btnBake    != null) _btnBake.style.display = DisplayStyle.None;
            }
            else
            {
                _backupSection.style.display = DisplayStyle.None;
                _noBackupLabel.text          = "バックアップがありません";
                _noBackupLabel.style.display = DisplayStyle.Flex;
            }
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(Color.white);
            sep.style.marginTop       = 4; sep.style.marginBottom = 6;
            return sep;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 3; return l;
        }
    }
}
