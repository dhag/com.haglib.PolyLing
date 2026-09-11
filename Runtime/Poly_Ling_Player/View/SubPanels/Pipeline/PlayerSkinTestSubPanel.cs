// PlayerSkinTestSubPanel.cs
// スキン生成自動検証。ボタン 1 回で
//   MQO 読込 → 原点CSV適用 → 変換前の記録 → スキンド変換 → Humanoid 自動割当
//   → 検査とレポート書出 → T ポーズ化 → VRM 書出
// までを流す。段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるための検証か】
//   スキンド変換は「板の集まり」を「ボーン＋スキンドメッシュ」に組み替える。
//   組み替えの前後で見た目が変わらないこと（頂点のワールド位置が保たれること）と、
//   ボーンが実際に生成されて Humanoid に割り当てられることを機械的に見る。
//
// 【原点CSVを先に通す理由】
//   関節位置（BoneTransform.Position）は原点CSVで入る。入れずに変換すると
//   全ボーンが原点に重なり、Unity のアバターが成立しない。
//   検査では「ローカル位置がゼロのボーン」を数えて、この状態を捕まえる。
//
// 【入力欄を置かない理由】
//   MQO は直前の import が、CSV は直前の原点CSV読込が RecentPaths に残している。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>スキン生成自動検証。人の操作は「テスト実行」を押すだけ。</summary>
    public class PlayerSkinTestSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;
        public Action<string>       ImportMqo;

        /// <summary>VRM を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, Poly_Ling.Vrm.Vrm10ExportSettings, Poly_Ling.Vrm.Vrm10ExportResult> ExportVrm;

        // ================================================================
        // 定数
        // ================================================================

        /// <summary>MQO import が最後に使ったパスのキー（PlayerImportSubPanel と同じ規則）。</summary>
        private const string MqoPathKey = "Import.MQO.Path";

        /// <summary>原点CSVが最後に使ったパスのキー（PlayerBoneEditorSubPanel と同じ）。</summary>
        private const string CsvPathKey = "BoneEditor.OriginCsv.Path";

        /// <summary>スキンド変換がメッシュ名へ付ける接尾辞（MeshFilterToSkinnedConverter と同じ）。</summary>
        private const string MeshNameSuffix = "_skinned";

        /// <summary>ワールド位置が動いたと見なす閾値。</summary>
        private const float MoveEpsilon = 1e-4f;

        // ================================================================
        // 状態
        // ================================================================


        private string _mqoPath = "";
        private string _csvPath = "";
        private string _reportPath = "";
        private int    _csvRows;


        /// <summary>変換前の記録。名前をキーにする（索引は変換で動くため）。</summary>
        private sealed class Before
        {
            public string    Name = "";
            public string    Type = "";
            public int       VertexCount;
            public Vector3[] World;
        }

        private readonly List<Before> _before = new List<Before>();
        private int _beforeCount;

        private HumanoidBoneMapping _mapping;
        private int _mapCandidates;
        private int _meshCountBefore;

        /// <summary>実行のたびに保存ダイアログで確定した VRM の書き出し先。</summary>
        private string _vrmPath = "";

        // ================================================================
        // UI
        // ================================================================

        // UI 自動操作の ID は "skinTest.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ・書き込み先）は基底クラス側で登録する。
        [UiControl("inputPaths", Safety = UiSafety.ReadOnly, Description = "使う入力（直前に使ったパス）")]
        private Label     _pathLabel;
        [UiControl("convertToTPose", Description = "T ポーズ化してから書き出す")]
        private Toggle    _doTPose;
        [UiControl("exportVrm", Description = "最後に VRM を書き出す")]
        private Toggle    _doExport;

        protected override string TitleText => "スキン生成自動検証";

        protected override string NoteText =>
            "MQO 読込 → 原点CSV適用 → 変換前の記録 → スキンド変換 → Humanoid 自動割当\n"
          + "→ 検査とレポート書出 → T ポーズ化 → VRM 書出 を通しで流します。\n"
          + "対象は直前に使った MQO と原点CSV です。段ごとに手順と理由をログに出します。";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("入力（直前に使ったパスをそのまま使う）"));
            _pathLabel = new Label("");
            _pathLabel.style.fontSize   = 10;
            _pathLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_pathLabel);
            root.Add(Hint(
                "原点CSV は必須です。関節位置が入っていない状態で変換すると"
              + "全ボーンが原点に重なり、アバターが成立しません。"));

            root.Add(Sec("仕上げ"));
            _doTPose = new Toggle("T ポーズ化してから書き出す") { value = true };
            root.Add(_doTPose);
            root.Add(Hint(
                "VRM 1.0 は T ポーズを前提にします。素材が別の姿勢で作られている場合、"
              + "ここを通さないとビューアで腕の角度がずれます。"));

            _doExport = new Toggle("最後に VRM を書き出す") { value = true };
            root.Add(_doExport);

            root.Add(MakeOutDest("VRM の保存先", "vrm", () => "skin_test.vrm"));
            root.Add(Hint(
                "[...] は書き込み先フォルダを決めるだけです。ファイル名は「実行」を押したときの"
              + "保存ダイアログで決めます。"));

            RefreshPathLabel();
        }

        public override void Refresh()
        {
            base.Refresh();
            if (!IsRunning) RefreshPathLabel();
        }

        private void RefreshPathLabel()
        {
            if (_pathLabel == null) return;
            string mqo = SafeGet(MqoPathKey);
            string csv = SafeGet(CsvPathKey);
            _pathLabel.text =
                "MQO: " + (string.IsNullOrEmpty(mqo) ? "（未設定）" : mqo) + "\n" +
                "CSV: " + (string.IsNullOrEmpty(csv) ? "（未設定）" : csv);
        }

        private static string SafeGet(string key)
        {
            try { return RecentPaths.Get(key); }
            catch { return ""; }
        }

        protected override bool CanRun()
        {
            _mqoPath = SafeGet(MqoPathKey);
            _csvPath = SafeGet(CsvPathKey);

            if (string.IsNullOrEmpty(_mqoPath) || !File.Exists(_mqoPath))
            { SetStatus("直前に読み込んだ MQO が見つかりません。一度 MQO を import してください。"); return false; }

            if (string.IsNullOrEmpty(_csvPath) || !File.Exists(_csvPath))
            { SetStatus("直前に使った原点CSVが見つかりません。関節位置が入らないため実行しません。"); return false; }

            if (ImportMqo == null || SendCommand == null || GetModel == null)
            { SetStatus("配線が足りません（ImportMqo / SendCommand / GetModel）。"); return false; }

            if (_doExport.value)
            {
                if (ExportVrm == null) { SetStatus("配線が足りません（ExportVrm）。"); return false; }

                // 書き出し先は毎回ここで確定する（「名前を付けて保存」）。
                // 以前はエクスポートパネルと同じ履歴キーを読んでいたため、
                // 「実行」がエクスポートパネルで最後に保存した VRM を黙って潰していた。
                _vrmPath = AskOutPath();
                if (string.IsNullOrEmpty(_vrmPath)) return false;   // キャンセル
            }
            return true;
        }

        protected override void ResetRunState()
        {
            _before.Clear();
            _mapping     = null;
            _beforeCount = 0;
            _csvRows     = 0;
            _meshCountBefore = GetModel()?.MeshContextCount ?? 0;

            _reportPath = BuildReportPath("SkinTest");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. MQO を読み込む",               StageImportMqo));
            stages.Add(("2. 読み込みの完了を待つ",         StageWaitImport));
            stages.Add(("3. 原点CSVを適用する",            StageApplyOriginCsv));
            stages.Add(("4. 変換前のワールド位置を控える", StageCaptureBefore));
            stages.Add(("5. ボーンとスキンを生成する",     StageConvertSkinned));
            stages.Add(("6. Humanoid を自動割当する",      StageAutoMapHumanoid));
            stages.Add(("7. 検査してレポートを書く",       StageWriteReport));
            if (_doTPose.value)  stages.Add(("8. T ポーズ化する", StageApplyTPose));
            if (_doExport.value) stages.Add(("9. VRM を書き出す", StageExportVrm));
        }

        protected override void OnFinished(bool aborted)
        {
            if (aborted) WriteAbortReport("段の途中で停止した");
        }

        /// <summary>読み込みの完了を待つ。件数が増えるまで同じ段を繰り返す。</summary>
        private StageResult StageWaitImport()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;
            if (model.MeshContextCount == _meshCountBefore) return StageResult.Retry;

            return Ok(
                $"モデル「{model.Name}」に MeshContext が {model.MeshContextCount} 件できた",
                null,
                "読み込みはファイルの大きさで時間が変わる。件数が増えるまで待ち直している。"
              + "ここで止まるなら、そもそも import が走っていない。");
        }

        /// <summary>T ポーズ化。VRM 1.0 は T ポーズを前提にする。</summary>
        private StageResult StageApplyTPose()
        {
            SendCommand(new ApplyTPoseCommand(GetModelIndex?.Invoke() ?? 0));
            return Ok(
                "ApplyTPoseCommand を送った",
                "T ポーズパネル →「T ポーズ化」",
                "VRM 1.0 のビューアは T ポーズを基準に姿勢を解釈する。"
              + "素材が別の姿勢で作られていると、通さないまま出力したときに腕の角度がずれる。"
              + "元の姿勢はバックアップされるので、あとで戻せる。");
        }

        /// <summary>VRM 書出。スキンド変換済みなのでスキニングが付く。</summary>
        private StageResult StageExportVrm()
        {
            string path = _vrmPath;

            var settings = Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            // 欠けている必須関節はダミーで補う。補わないと VRM 1.0 の必須ボーンが
            // 欠けたままになり、ビューアが読み込みを拒否する。
            settings.SupplementHumanoid = true;

            var result = ExportVrm(path, settings);
            if (result == null || !result.Success)
                return Ng("VRM 書出に失敗: " + (result?.ErrorMessage ?? "戻り値がありません"),
                    "エクスポートパネル → VRM",
                    "Humanoid の割当が 0 件だと VRM にできない。段 6 の割当件数を見る。");

            if (result.HumanoidBoneCount == 0)
                return Ng("Humanoid ボーンが 0 件で出力された", null,
                    "VRM 1.0 は humanoid が必須。0 件のファイルはビューアが読めない。");

            return Ok(
                $"「{Path.GetFileName(path)}」へ書き出した。"
              + $"ノード {result.NodeCount} / メッシュ {result.MeshCount} / 頂点 {result.VertexCount} / "
              + $"Humanoid ボーン {result.HumanoidBoneCount}（うちダミー補完 {result.SupplementedJointCount}） / "
              + $"ブレンドシェイプ {result.MorphTargetCount} / 表情 {result.ExpressionCount}"
              + (string.IsNullOrEmpty(result.Warning) ? "" : $" / 警告: {result.Warning}"),
                "エクスポートパネル → VRM → 保存先を選ぶ",
                "スキンド変換済みなのでスキニング（ボーンウェイト）が付く。"
              + "ダミー補完が多いときは、変換で作られなかった関節があるということ。"
              + "段 7 の「生成されたボーン」の本数と見比べる。");
        }

        /// <summary>
        /// 途中で止まったときに、そこまでの内容と中断理由を書き出す。
        /// 「レポートが出ない」という状態を作らないための保険。
        /// </summary>
        private void WriteAbortReport(string reason)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PolyLing スキン生成自動検証レポート（中断）");
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("MQO: " + _mqoPath);
            sb.AppendLine("中断理由: " + reason);
            sb.AppendLine("実行ログ:");
            foreach (var l in PlainLog) sb.Append(l);
            sb.AppendLine();

            var model = GetModel?.Invoke();
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine($"MeshContext 数: 変換前の記録 {_beforeCount} / 現在 {model?.MeshContextCount ?? 0}");
            sb.AppendLine($"頂点を控えたもの: {_before.Count} 件");
            sb.AppendLine($"Humanoid 割当: {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine();

            if (model != null)
            {
                sb.AppendLine("## 現在の全オブジェクト");
                sb.AppendLine("索引\t名前\t種別\t頂点\t親\tIsSkinned");
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null) continue;
                    sb.AppendLine($"{i}\t{mc.Name}\t{mc.Type}\t{mc.MeshObject?.VertexCount ?? 0}\t" +
                                  $"{mc.HierarchyParentIndex}\t{mc.IsSkinned}");
                }
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
                Debug.Log("[SkinTest] 中断レポート: " + _reportPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SkinTest] 中断レポートも書けなかった: " + e.Message);
            }
        }

        // ================================================================
        // 段
        // ================================================================

        private StageResult StageImportMqo()
        {
            ImportMqo(_mqoPath);
            return Ok(
                $"ImportMqoCommand と同じ経路で「{Path.GetFileName(_mqoPath)}」を読み込んだ",
                "インポートパネル → MQO → ファイルを選ぶ",
                "MQO は板（MeshFilter 系）の集まりで、この時点ではボーンもウェイトも無い。"
              + "関節の位置はこのあと原点CSVで入れ、ボーンはスキンド変換で作る。");
        }

        /// <summary>
        /// 段2: 原点CSVを適用する。UI ボタンと同じコマンド経路へ流す。
        /// 関節位置（BoneTransform.Position）はこれで入る。
        /// </summary>
        private StageResult StageApplyOriginCsv()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            if (!ParseCsv(_csvPath, out var names, out var positions, out string err))
                return Ng($"CSV を読めなかった: {err}",
                    "ボーン編集パネル →「原点CSV読込」",
                    "CSV は 1 行が「名前, x, y, z」。# で始まる行と name, の見出し行は飛ばす。"
                  + "有効な行が 0 なら区切り文字か列数が違う。");

            _csvRows = names.Length;
            SendCommand(new ApplyObjectOriginsCommand(
                GetModelIndex?.Invoke() ?? 0, names, positions));

            return Ok(
                $"CSV {_csvRows} 行を ApplyObjectOriginsCommand で適用した",
                "ボーン編集パネル →「原点CSV読込」でファイルを選ぶ",
                "関節位置はここで入る。入れずに変換すると全ボーンが原点に重なり、"
              + "Unity のアバターが成立しない。段 7 の「ローカル位置がゼロのボーン」で見張る。");
        }

        /// <summary>
        /// 原点CSVを読む。規則は PlayerBoneEditorSubPanel.ImportObjectOriginsCsv と同じ。
        /// 回転列は読まない（この検証では位置だけを対象にする）。
        /// </summary>
        private static bool ParseCsv(
            string path, out string[] names, out Vector3[] positions, out string error)
        {
            names = null; positions = null; error = "";

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { error = e.Message; return false; }

            var ns = new List<string>();
            var ps = new List<Vector3>();

            foreach (string raw in lines)
            {
                string line = raw?.Trim('\uFEFF', ' ', '\t');
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("#")) continue;
                if (line.StartsWith("name,")) continue;

                var cols = line.Split(',');
                if (cols.Length < 4) continue;
                if (!float.TryParse(cols[1], out float x)) continue;
                if (!float.TryParse(cols[2], out float y)) continue;
                if (!float.TryParse(cols[3], out float z)) continue;

                ns.Add(cols[0]);
                ps.Add(new Vector3(x, y, z));
            }

            if (ns.Count == 0) { error = "有効な行がない"; return false; }

            names = ns.ToArray();
            positions = ps.ToArray();
            return true;
        }

        /// <summary>
        /// 段3: 変換前の状態を控える。
        /// 索引はスキンド変換でボーンが挿入されて動くので、名前をキーにする。
        /// </summary>
        private StageResult StageCaptureBefore()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが読み込まれていない", null, null);

            model.ComputeWorldMatrices();

            _beforeCount = model.MeshContextCount;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var wm = mc.WorldMatrix;
                var world = new Vector3[mo.Vertices.Count];
                for (int v = 0; v < mo.Vertices.Count; v++)
                    world[v] = wm.MultiplyPoint3x4(mo.Vertices[v].Position);

                _before.Add(new Before
                {
                    Name        = mc.Name ?? "",
                    Type        = mc.Type.ToString(),
                    VertexCount = mo.Vertices.Count,
                    World       = world,
                });
            }

            return Ok(
                $"MeshContext {_beforeCount} 件のうち {_before.Count} 件について、"
              + "全頂点のワールド位置を控えた",
                "（手作業には対応する操作が無い。検証のための記録）",
                "変換の前後で見た目が変わらないことを確かめるための基準。"
              + "索引はスキンド変換でボーンが挿し込まれて動くので、名前をキーにする。");
        }

        private StageResult StageConvertSkinned()
        {
            SendCommand(new ConvertMeshFilterToSkinnedCommand(GetModelIndex?.Invoke() ?? 0));
            return Ok(
                "ConvertMeshFilterToSkinnedCommand を送った",
                "スキン生成パネル →「ボーンとスキンを生成」",
                "板ごとの原点がボーンになり、板の頂点はワールド（バインド）空間へ移して"
              + "そのボーンへ重み 1 で結ぶ。メッシュ名には _skinned が付く。"
              + "同時にミラー枝の計画が実体化し、右半身のボーンがここで初めて生まれる。");
        }

        /// <summary>
        /// 段4: アバター用 Humanoid オートマップ。
        /// 変換でボーンが生成されるが、ボーン以外も候補に含めておく
        /// （変換が期待どおりに走らなかった場合でも割当の様子が判るようにするため）。
        /// </summary>
        private StageResult StageAutoMapHumanoid()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが無い", null, null);

            var names = new List<string>();
            _mapCandidates = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                string nm = (mc != null && !string.IsNullOrEmpty(mc.Name)) ? mc.Name : "";
                names.Add(nm);
                if (!string.IsNullOrEmpty(nm)) _mapCandidates++;
            }

            _mapping = new HumanoidBoneMapping();
            int mapped = _mapping.AutoMapFromEmbeddedCSV(names);

            if (mapped > 0)
            {
                ApplyHumanoidMappingCommand.SplitMapping(_mapping, out var hmNames, out var hmIdx);
                SendCommand(new ApplyHumanoidMappingCommand(GetModelIndex?.Invoke() ?? 0, hmNames, hmIdx));
            }

            if (mapped == 0)
                return Ng($"候補 {_mapCandidates} 件に対して割当 0 件",
                    "Humanoid 割当パネル →「自動割当」",
                    "自動割当は名前の表（埋め込みCSV）と突き合わせる。0 件なら"
                  + "ボーン名が表のどれにも当たっていない。変換自体が走っていない場合もここで 0 になる。");

            return Ok(
                $"候補 {_mapCandidates} 件 → 割当 {mapped} 件",
                "Humanoid 割当パネル →「自動割当」",
                "候補にはボーン以外も含める。変換が期待どおりに走らなかった場合でも"
              + "割当の様子が判るようにするため。");
        }

        /// <summary>段5: 検査してレポートを書く。</summary>
        private StageResult StageWriteReport()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルが無い", null, null);

            model.ComputeWorldMatrices();

            // 名前 → 変換後の索引（重複名は先着）。
            //
            // スキンド変換の命名は次のとおり（MeshFilterToSkinnedConverter）。
            //   ボーン … 元のオブジェクト名をそのまま継ぐ
            //   メッシュ … 元の名前 + "_skinned"
            // ボーンを除いたうえで、変換前の名前に接尾辞を付けて引く。
            var afterByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (!afterByName.ContainsKey(mc.Name)) afterByName[mc.Name] = i;
            }

            var moved      = new List<(Before b, int idx, float delta, Vector3 d)>();
            var lost       = new List<Before>();
            var countDiff  = new List<Before>();

            foreach (var b in _before)
            {
                int idx;
                if (string.IsNullOrEmpty(b.Name) ||
                    !(afterByName.TryGetValue(b.Name + MeshNameSuffix, out idx) ||
                      afterByName.TryGetValue(b.Name, out idx)))
                { lost.Add(b); continue; }

                var mc = model.GetMeshContext(idx);
                var mo = mc?.MeshObject;
                if (mo == null) { lost.Add(b); continue; }

                if (mo.Vertices.Count != b.VertexCount) { countDiff.Add(b); continue; }

                var wm = mc.WorldMatrix;
                float max = 0f; Vector3 dmax = Vector3.zero;
                for (int v = 0; v < b.VertexCount; v++)
                {
                    Vector3 now = wm.MultiplyPoint3x4(mo.Vertices[v].Position);
                    Vector3 d   = now - b.World[v];
                    float   m   = d.magnitude;
                    if (m > max) { max = m; dmax = d; }
                }
                if (max > MoveEpsilon) moved.Add((b, idx, max, dmax));
            }

            // 種別内訳とウェイトの整合
            var typeCount   = new Dictionary<string, int>();
            var bones       = new List<int>();
            var weightOnly  = new List<int>();   // 種別が Skinned でないのにウェイト有り
            var kindOnly    = new List<int>();   // 種別 Skinned なのにウェイト無し

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                string t = mc.Type.ToString();
                typeCount[t] = typeCount.TryGetValue(t, out int c) ? c + 1 : 1;
                if (mc.Type == MeshType.Bone) bones.Add(i);

                var mo = mc.MeshObject;
                if (mo == null || mo.Vertices == null) continue;

                bool anyWeight = false;
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    if (mo.Vertices[v] != null && mo.Vertices[v].HasBoneWeight) { anyWeight = true; break; }
                }

                if (!mc.IsSkinned && anyWeight) weightOnly.Add(i);
                else if (mc.IsSkinned && !anyWeight && mo.Vertices.Count > 0) kindOnly.Add(i);
            }

            // ボーンのローカル位置がゼロのものを数える。
            // 全ボーンがゼロだと関節が原点に重なり、Unity のアバターは成立しない。
            // 原点CSV を通していれば関節位置が入っているはずなので、ここが多いと
            // 「原点が入っていない状態で変換した」ことになる。
            var zeroBones = new List<int>();
            foreach (int i in bones)
            {
                var bt = model.GetMeshContext(i)?.BoneTransform;
                Vector3 p = bt?.Position ?? Vector3.zero;
                if (p.sqrMagnitude <= 1e-12f) zeroBones.Add(i);
            }

            var sb = new StringBuilder();
            WriteReport(sb, model, moved, lost, countDiff, typeCount, bones, weightOnly, kindOnly, zeroBones);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { return Ng("レポートを書けなかった: " + e.Message, null, null); }

            var missing = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            bool canAvatar = _mapping?.CanCreateAvatar ?? false;

            string detail =
                $"MeshContext {_beforeCount} → {model.MeshContextCount} 件 / 生成ボーン {bones.Count} 本 / "
              + $"ワールド位置が動いた {moved.Count} 件 / 変換後に見つからない {lost.Count} 件 / "
              + $"頂点数が変わった {countDiff.Count} 件 / 種別とウェイトの食い違い {weightOnly.Count + kindOnly.Count} 件 / "
              + $"ローカル位置がゼロのボーン {zeroBones.Count}／{bones.Count} 本。"
              + $"Humanoid 割当 {_mapping?.Count ?? 0} 件（必須の未割当 {missing.Count}／アバター生成可={canAvatar}）。"
              + $"レポート: {_reportPath}";

            bool ok = moved.Count == 0 && lost.Count == 0 && countDiff.Count == 0
                   && bones.Count > 0 && zeroBones.Count < bones.Count && canAvatar;

            if (!ok)
            {
                var head = new StringBuilder();
                int show = Mathf.Min(5, moved.Count);
                for (int i2 = 0; i2 < show; i2++)
                    head.Append($" {moved[i2].b.Name} ずれ={moved[i2].delta:F6}");
                if (moved.Count > show) head.Append($" … 他 {moved.Count - show} 件");

                return Ng(detail + head,
                    null,
                    "ワールド位置が動いた＝変換でバインド空間への移し替えがずれている。"
                  + "見つからない＝名前の付け替え規則（_skinned）から外れた。"
                  + "全ボーンのローカル位置がゼロ＝原点CSVが効いていない。"
                  + "アバター生成不可＝必須ボーンの割当が足りない。"
                  + "どのオブジェクトかはレポートに全件出ている。");
            }

            return Ok(detail,
                "スキン生成のあと、形が崩れていないかとアバターが作れるかを目で見るのと同じ",
                "合格の条件は 6 つ。位置が保たれる／全部見つかる／頂点数が変わらない／"
              + "ボーンが 1 本以上できる／全ボーンが原点に重なっていない／アバターが作れる。");
        }

        // ================================================================
        // レポート本文
        // ================================================================

        private void WriteReport(
            StringBuilder sb, ModelContext model,
            List<(Before b, int idx, float delta, Vector3 d)> moved,
            List<Before> lost, List<Before> countDiff,
            Dictionary<string, int> typeCount, List<int> bones,
            List<int> weightOnly, List<int> kindOnly, List<int> zeroBones)
        {
            sb.AppendLine("# PolyLing スキン生成自動検証レポート");
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("MQO: " + _mqoPath);
            sb.AppendLine("CSV: " + _csvPath + $"（{_csvRows} 行）");
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine($"MeshContext 数: 変換前 {_beforeCount} → 変換後 {model?.MeshContextCount ?? 0}");
            sb.AppendLine();

            sb.AppendLine("## 判定");
            sb.AppendLine($"生成されたボーン: {bones.Count} 本");
            sb.AppendLine($"ワールド位置が動いた: {moved.Count} 件（閾値 {MoveEpsilon}）");
            sb.AppendLine($"変換後に見つからない: {lost.Count} 件");
            sb.AppendLine($"頂点数が変わった: {countDiff.Count} 件");
            sb.AppendLine($"種別が Skinned でないのにウェイト有り: {weightOnly.Count} 件");
            sb.AppendLine($"種別 Skinned なのにウェイト無し: {kindOnly.Count} 件");
            sb.AppendLine($"ローカル位置がゼロのボーン: {zeroBones.Count} / {bones.Count} 本");
            sb.AppendLine($"Humanoid 割当: {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine($"アバター生成可: {(_mapping?.CanCreateAvatar ?? false)}");
            sb.AppendLine();

            sb.AppendLine("## 種別内訳（変換後）");
            foreach (var kv in typeCount) sb.AppendLine($"{kv.Key}\t{kv.Value}");
            sb.AppendLine();

            sb.AppendLine("## 動いたオブジェクト");
            if (moved.Count == 0) sb.AppendLine("なし");
            foreach (var m in moved)
            {
                var mc = model.GetMeshContext(m.idx);
                sb.AppendLine($"- {m.b.Name} 索引={m.idx}");
                sb.AppendLine($"    種別 前={m.b.Type} / 後={(mc != null ? mc.Type.ToString() : "-")}");
                sb.AppendLine($"    親={mc?.HierarchyParentIndex ?? -1} " +
                              $"（{ParentName(model, mc)}）");
                sb.AppendLine($"    最大ずれ={m.delta:F6} ({m.d.x:F6}, {m.d.y:F6}, {m.d.z:F6})");
            }
            sb.AppendLine();

            sb.AppendLine("## 変換後に見つからない");
            if (lost.Count == 0) sb.AppendLine("なし");
            foreach (var b in lost) sb.AppendLine($"- {b.Name} 種別={b.Type} 頂点={b.VertexCount}");
            sb.AppendLine();

            sb.AppendLine("## 頂点数が変わった");
            if (countDiff.Count == 0) sb.AppendLine("なし");
            foreach (var b in countDiff)
            {
                // 上の afterByName と同じ規則で引く（ボーンを除く／接尾辞つき優先）。
                int idx = -1;
                if (!string.IsNullOrEmpty(b.Name))
                {
                    string want = b.Name + MeshNameSuffix;
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var c = model.GetMeshContext(i);
                        if (c == null || c.Type == MeshType.Bone) continue;
                        if (c.Name == want || c.Name == b.Name) { idx = i; break; }
                    }
                }
                int after = (idx >= 0) ? (model.GetMeshContext(idx)?.MeshObject?.VertexCount ?? 0) : 0;
                sb.AppendLine($"- {b.Name} 前={b.VertexCount} 後={after}");
            }
            sb.AppendLine();

            sb.AppendLine("## 種別とウェイトの食い違い");
            if (weightOnly.Count == 0 && kindOnly.Count == 0) sb.AppendLine("なし");
            foreach (int i in weightOnly)
                sb.AppendLine($"- ウェイト有りだが種別が Skinned でない: [{i}] {model.GetMeshContext(i)?.Name}");
            foreach (int i in kindOnly)
                sb.AppendLine($"- 種別 Skinned だがウェイト無し: [{i}] {model.GetMeshContext(i)?.Name}");
            sb.AppendLine();

            sb.AppendLine("## ローカル位置がゼロのボーン");
            sb.AppendLine("（全ボーンがゼロなら関節が原点に重なる。原点CSV が入っていない状態で");
            sb.AppendLine("  変換したときに起きる。Unity のアバターはこの状態では成立しない）");
            if (zeroBones.Count == 0) sb.AppendLine("なし");
            else if (zeroBones.Count == bones.Count) sb.AppendLine("全ボーンがゼロ");
            else
                foreach (int i in zeroBones)
                    sb.AppendLine($"- [{i}] {model.GetMeshContext(i)?.Name}");
            sb.AppendLine();

            sb.AppendLine("## 生成されたボーン");
            if (bones.Count == 0) sb.AppendLine("なし");
            else
            {
                sb.AppendLine("索引\t名前\t親\t親の名前\tローカル位置");
                foreach (int i in bones)
                {
                    var mc = model.GetMeshContext(i);
                    Vector3 p = mc?.BoneTransform?.Position ?? Vector3.zero;
                    sb.AppendLine($"{i}\t{mc?.Name}\t{mc?.HierarchyParentIndex ?? -1}\t" +
                                  $"{ParentName(model, mc)}\t({p.x:F6}, {p.y:F6}, {p.z:F6})");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Humanoid オートマップ");
            sb.AppendLine($"候補 {_mapCandidates} 件（ボーン以外も含める）");
            sb.AppendLine($"割当 {(_mapping?.Count ?? 0)} 件");
            sb.AppendLine();

            sb.AppendLine("### 割当");
            if (_mapping == null || _mapping.Count == 0) sb.AppendLine("なし");
            else
            {
                sb.AppendLine("Humanoid名\t索引\t名前\t種別");
                foreach (var kv in _mapping.BoneIndexMap)
                {
                    var mc = (kv.Value >= 0 && kv.Value < model.MeshContextCount)
                        ? model.GetMeshContext(kv.Value) : null;
                    sb.AppendLine($"{kv.Key}\t{kv.Value}\t{mc?.Name ?? "<範囲外>"}\t" +
                                  $"{(mc != null ? mc.Type.ToString() : "-")}");
                }
            }
            sb.AppendLine();

            sb.AppendLine("### 必須ボーンの未割当");
            var miss = _mapping?.GetMissingRequiredBones() ?? new List<string>();
            if (miss.Count == 0) sb.AppendLine("なし");
            foreach (var m in miss) sb.AppendLine("- " + m);
            sb.AppendLine();

            sb.AppendLine("## 全オブジェクト（変換後）");
            sb.AppendLine("索引\t名前\t種別\t頂点\t親\tIsSkinned");
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                sb.AppendLine($"{i}\t{mc.Name}\t{mc.Type}\t{mc.MeshObject?.VertexCount ?? 0}\t" +
                              $"{mc.HierarchyParentIndex}\t{mc.IsSkinned}");
            }
        }

        private static string ParentName(ModelContext model, MeshContext mc)
        {
            int p = mc?.HierarchyParentIndex ?? -1;
            if (p < 0 || p >= (model?.MeshContextCount ?? 0)) return "ルート";
            return model.GetMeshContext(p)?.Name ?? "?";
        }

    }
}
