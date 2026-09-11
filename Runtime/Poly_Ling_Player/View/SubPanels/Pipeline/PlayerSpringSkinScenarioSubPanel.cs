// PlayerSpringSkinScenarioSubPanel.cs
// 揺れもの → スキンド化 → VRM の順で通す自動検証。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるか】
//   ボーンが 1 本も無い MeshFilter 系のモデルに対して、
//     1. はしごから揺れボーンを先に作る（このときウェイトは塗らない）
//     2. そのはしごからフリルを作る
//     3. そのあと一括スキンド化する
//   という順が最後まで通り、スキンド化のあとにウェイトが自動で塗り直されることを見る。
//
// 【なぜこの順を通す必要があるか】
//   人の手順としてはこちらが自然（揺らしたい所を先に組んでから、最後に
//   スキンドへ落とす）。一方で塗りには制約がある。
//
//   ・描画は per-vertex で欄を選ぶ。ウェイトを持つ頂点はボーンの欄、持たない頂点は
//     メッシュ自身の欄を引く（UnifiedBufferManager_Build.cs:411-427）。
//     MeshFilter のはしごを塗ると、塗った頂点だけが SkinningMatrix 経路へ移り、
//     WorldMatrix が単位でなければ飛ぶ。
//   ・一括スキンド化は全メッシュの全頂点へ {自分のボーン, 1.0} を書く
//     （MeshFilterToSkinnedConverter.cs の Phase 4）。先に塗っても消える。
//
//   よって塗る契機はスキンド化の直後 1 か所に集約してある。
//   はしごのグループへ「自動更新」を立てておくと、スキンド化のあとに
//   PlayerCommandDispatcher.RunAutoUpdateGroups が流し直して塗る。
//   フリルのグループも同じ流れで、はしごのウェイトを引き継ぐ。
//
// 【取り付け先】
//   はしごの階層親をそのまま使う。まだボーンでなくてよい。
//   スキンド化のとき、そのメッシュのボーン配下へ付け替わる
//   （MeshFilterToSkinnedConverter の Phase 4a）。
//
// 【断面プロファイルは CSV から読む】
//   梯子は描画オブジェクトから取り込むので、CSV はプロファイル側。
//   ProfilePointsCsvIO.LoadPair は 4 列（XA,YA,XB,YB）も読めるが、
//   ここでは A だけを使う。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Frill;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools.SpringBoneRig;

namespace Poly_Ling.Player
{
    public class PlayerSpringSkinScenarioSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ProjectContext>  GetProject;
        public Func<ModelContext>    GetModel;
        public Func<int>             GetModelIndex;
        public Action<PanelCommand>  SendCommand;

        protected int ModelIndex => GetModelIndex?.Invoke() ?? 0;

        // ================================================================
        // 入力
        // ================================================================

        // UI 自動操作の ID は "springSkinScenario.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ）は基底クラス側で登録する。
        // 入力の 3 ファイルは実行時に PLSandbox が作業フォルダの下だけに限る。
        // VRM の書き出し先はこのパネルがそのまま書き込むので、外から変えるときは関門を通す。
        [UiControl("mqoPath", Description = "読み込む MQO のパス")]
        private TextField    _mqoPath;
        [UiControl("originCsvPath", Description = "原点 CSV のパス")]
        private TextField    _originCsvPath;
        [UiControl("profileCsvPath", Description = "断面プロファイル CSV のパス")]
        private TextField    _profileCsvPath;
        [UiControl("ladderName", Description = "梯子オブジェクト名")]
        private TextField    _ladderName;
        [UiControl("bonePrefix", Description = "ボーン名の接頭辞")]
        private TextField    _prefix;
        [UiControl("vrmPath", Getter = nameof(GetVrmPathForAutomation), Setter = nameof(SetVrmPathByAutomation),
                   Description = "VRM の書き出し先。作業フォルダからの相対パスで指定する")]
        private TextField    _vrmPath;
        [UiControl("useOriginCsv", Description = "オブジェクトローカル姿勢を別ファイルから読む")]
        private Toggle       _useOriginCsv;
        [UiControl("originRotation", Description = "原点 CSV の回転列も使う")]
        private Toggle       _originRotation;
        [UiControl("hideLadder", Description = "VRM へ書き出す前にはしごを隠す")]
        private Toggle       _hideLadder;
        [UiControl("rungStride", Description = "段ストライド")]
        private IntegerField _rungStride;
        [UiControl("chainStride", Description = "本ストライド")]
        private IntegerField _chainStride;
        [UiControl("frillHeight", Description = "フリルの高さ倍率")]
        private FloatField   _frillHeight;
        [UiControl("spring.stiffness", Description = "揺れ方のかたさ")]
        private FloatField   _springStiffness;
        [UiControl("spring.drag", Description = "揺れ方の抵抗")]
        private FloatField   _springDrag;
        [UiControl("spring.gravityPower", Description = "揺れ方の重力の強さ")]
        private FloatField   _springGravity;
        [UiControl("spring.hitRadius", Description = "揺れ方の当たり半径")]
        private FloatField   _springHitRadius;

        private string GetVrmPathForAutomation() => _vrmPath?.value ?? "";

        /// <summary>
        /// UI 自動操作から VRM の書き出し先を設定する。実行がこの場所へ書き込むので、
        /// 作業フォルダの関門（PLSandbox.TryResolveWrite）を通した実経路だけを入れる。
        /// </summary>
        private string SetVrmPathByAutomation(string value)
        {
            if (_vrmPath == null) return "VRM の書き出し先の欄がありません";
            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(value, out string full, out string reason)) return reason;
            _vrmPath.value = full;
            return null;
        }

        // ================================================================
        // 持ち回り
        // ================================================================

        private int    _meshCountBefore;
        private int    _boneCountBefore;
        private int    _ladderIndex   = -1;
        private int    _attachIndex   = -1;
        private string _ladderGroup   = "";
        private string _frillGroup    = "";
        private int    _frillIndex    = -1;
        private List<Vector2> _profile = new List<Vector2>();

        /// <summary>直前に見たオブジェクト数。増え終わったかを見るために持つ。</summary>
        private int _lastSeenMeshCount = -1;

        /// <summary>数が変わらないまま通った回数。</summary>
        private int _settleCount;

        /// <summary>この回数ぶん数が動かなければ「増え終わった」とみなす。</summary>
        private const int SettleTicks = 5;

        /// <summary>待っている段の名前。段が変わると数え直す。</summary>
        private string _waitStage = "";

        /// <summary>同じ段で待った回数。</summary>
        private int _waitTicks;

        /// <summary>
        /// 同じ段で待った回数を返す。段が変わると 0 から数え直す。
        ///
        /// 共通部のタイムアウトは全段ぶんの合計なので、どの段で何を待っていたかが
        /// 残らない。段ごとに区切って数え、待ち切れないときは「何が足りないか」を
        /// 書いて落とす。黙って待ち続けると、次に何を見ればよいか判らない。
        /// </summary>
        private int Waited()
        {
            if (_waitStage != CurrentStageName) { _waitStage = CurrentStageName; _waitTicks = 0; }
            return ++_waitTicks;
        }

        /// <summary>1 つの段で待つ上限。200ms × 100 ＝ 20 秒。</summary>
        private const int StageWaitLimit = 100;

        private const string FrillName = "SS_Frill";

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "揺れもの→スキンド→VRM 自動検証";

        // ── 待ちの上限 ────────────────────────────────────────────────
        //
        // 共通部の既定は 120ms × 60 回 ＝ 7.2 秒（PlayerStagedTestSubPanelBase）。
        // 図形を数個作るだけの検証ならそれで足りるが、この検証は
        // MQO の読込・一括スキンド化・VRM の書き出しという、モデル全体を
        // 舐める処理を待つ。7.2 秒では読込の途中で打ち切られる。
        //
        // 待ち切れなかったときの原因を「前の段が走っていない」に絞るためにも、
        // 素直に足りる長さを取る。待っている間は何も走らない
        // （schedule で次の段を予約するだけで、毎フレーム駆動は無い）。
        protected override long StageIntervalMs => 200;
        protected override int  MaxRetry        => 900;   // 200ms × 900 ＝ 180 秒

        protected override string NoteText =>
            "MQO 読込（Humanoid 自動割当 / 原点CSV）→ 断面CSV 読込 → はしごから揺れボーン\n"
          + "→ フリル生成 → 一括スキンド化 → 自動更新で塗り直し → VRM 保存、までを通しで流します。\n"
          + "ボーンを先に作ってから一括スキンド化する順が通ることの検証です。";

        // ================================================================
        // 既定のパス
        //
        // 人が押すだけで流せるように、検証に使うファイルを最初から入れておく。
        // 欄は残してあるので、別のデータで試すときは書き換えればよい。
        // ================================================================

        private const string DefaultMqoPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\テストデータろぼFろぼK\Tpose\roboF_T5_SK.mqo";

        private const string DefaultOriginCsvPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\テストデータろぼFろぼK\Tpose\ローカル原点MoldelFKDL_5T.csv";

        private const string DefaultProfileCsvPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\線分プロファイルなど。\frill_profile_cos.csv";

        private const string DefaultVrmPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\MCPで自動生成\tmpp.vrm";

        private const string DefaultLadderName = "円筒24C";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("入力ファイル"));

            _mqoPath        = T("MQO", DefaultMqoPath);
            _useOriginCsv   = TG("オブジェクトローカル姿勢を別ファイルから読む", true);
            _originCsvPath  = T("原点CSV", DefaultOriginCsvPath);
            _originRotation = TG("原点CSV の回転列も使う", true);
            _profileCsvPath = T("断面プロファイルCSV", DefaultProfileCsvPath);

            root.Add(_mqoPath);
            root.Add(_useOriginCsv);
            root.Add(_originCsvPath);
            root.Add(_originRotation);
            root.Add(_profileCsvPath);
            root.Add(Hint("いずれも作業フォルダの下だけを読めます（PLSandbox）。"));

            root.Add(Sec("はしご"));
            _ladderName  = T("梯子オブジェクト名", DefaultLadderName);
            _prefix      = T("ボーン名の接頭辞", "SpringLadder");
            _rungStride  = I("段ストライド", 1);
            _chainStride = I("本ストライド", 1);
            _hideLadder  = TG("VRM へ書き出す前にはしごを隠す", true);
            root.Add(_ladderName); root.Add(_prefix);
            root.Add(_rungStride); root.Add(_chainStride);
            root.Add(_hideLadder);
            root.Add(Hint("取り付け先ははしごの階層親をそのまま使います。まだボーンでなくてかまいません。"));
            root.Add(Hint("はしごは形を決めるための下地で、見せるものではありません。"
                        + "隠すと VRM からも外れます（既定では非表示メッシュを書き出さないため）。"));

            root.Add(Sec("揺れ方"));
            _springStiffness = F("かたさ",     1.0f);
            _springDrag      = F("抵抗",       0.4f);
            _springGravity   = F("重力の強さ", 0.0f);
            _springHitRadius = F("当たり半径", 0.02f);
            root.Add(_springStiffness); root.Add(_springDrag);
            root.Add(_springGravity);   root.Add(_springHitRadius);
            root.Add(Hint("鎖を作るコマンドは揺れ方を付けません。付けないと VRM に "
                        + "VRMC_springBone が出ず、ボーンとして動くだけで揺れません。"));

            root.Add(Sec("フリル"));
            _frillHeight = F("高さ倍率", 1.0f);
            root.Add(_frillHeight);

            root.Add(Sec("書き出し"));
            _vrmPath = T("VRM の書き出し先", DefaultVrmPath);
            root.Add(_vrmPath);
            root.Add(Hint("書き出し先のフォルダが無いときは作られません。先にフォルダを用意しておくこと。"));
        }

        private static TextField T(string label, string v)
        {
            var f = new TextField(label) { value = v };
            f.style.fontSize = 11;
            return f;
        }

        private static Toggle TG(string label, bool v)
        {
            var t = new Toggle(label) { value = v };
            t.style.fontSize = 11;
            return t;
        }

        protected override bool CanRun()
        {
            if (GetModel == null || SendCommand == null)
            { SetStatus("パネルが繋がっていません。"); return false; }

            // ── 走らせる前に全部の入り口を確かめる ───────────────────────
            //
            // 【なぜここで見るか】
            //   ディスパッチャの Fail はコマンドの結果へ理由を書くだけで、
            //   コンソールには何も出さない（PlayerCommandDispatcher.cs:523）。
            //   読込が関門で弾かれても、この検証からは「待ち切れなかった」と
            //   しか見えない。走る前に同じ関門を自分で通して、理由を出す。
            if (!Poly_Ling.Core.PLSandbox.HasWorkFolder)
            {
                SetStatus("作業フォルダが未設定です。左ペインの「その他 → 作業フォルダ」で "
                        + @"D:\UNITY保存の品\TEO3.0開発中\PolyLing を選んでください。");
                return false;
            }

            if (!CheckRead("MQO", _mqoPath.value)) return false;

            if (_useOriginCsv.value && !string.IsNullOrWhiteSpace(_originCsvPath.value)
                && !CheckRead("原点CSV", _originCsvPath.value)) return false;

            if (!CheckRead("断面プロファイルCSV", _profileCsvPath.value)) return false;

            if (string.IsNullOrWhiteSpace(_vrmPath.value))
            { SetStatus("VRM の書き出し先を入れてください。"); return false; }

            if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                    _vrmPath.value.Trim(), out string vrmFull, out string vrmWhy))
            { SetStatus($"VRM の書き出し先が通りません: {vrmWhy}"); return false; }

            string vrmDir = System.IO.Path.GetDirectoryName(vrmFull);
            if (!string.IsNullOrEmpty(vrmDir) && !System.IO.Directory.Exists(vrmDir))
            { SetStatus($"VRM の書き出し先フォルダがありません: {vrmDir}"); return false; }

            return true;
        }

        /// <summary>
        /// 入力ファイルを 1 本確かめる。関門を通るか、実在するかの両方を見る。
        /// 通らなければ理由を出して false。
        /// </summary>
        private bool CheckRead(string label, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            { SetStatus($"{label} のパスを入れてください。"); return false; }

            if (!Poly_Ling.Core.PLSandbox.TryResolveRead(path.Trim(), out string full, out string why))
            { SetStatus($"{label} が通りません: {why}"); return false; }

            if (!System.IO.File.Exists(full))
            { SetStatus($"{label} のファイルがありません: {full}"); return false; }

            return true;
        }

        protected override void ResetRunState()
        {
            _meshCountBefore = 0;
            _boneCountBefore = 0;
            _ladderIndex = -1;
            _attachIndex = -1;
            _ladderGroup = "";
            _frillGroup  = "";
            _frillIndex  = -1;
            _profile     = new List<Vector2>();
            _lastSeenMeshCount = -1;
            _settleCount       = 0;
            _waitStage         = "";
            _waitTicks         = 0;

            // コンソールを拾う。
            //
            // 読込やスキンド化が落ちたとき、この検証からは「待ち切れなかった」
            // としか見えない。実際の理由（作業フォルダの関門・パス・書式）は
            // Unity のコンソールにしか出ないので、走っている間だけ拾って
            // このパネルのログへ出す。人がコンソールを開かなくても原因が判る。
            _captured.Clear();
            Application.logMessageReceived -= OnUnityLog;
            Application.logMessageReceived += OnUnityLog;
        }

        protected override void OnFinished(bool aborted)
        {
            Application.logMessageReceived -= OnUnityLog;

            if (_captured.Count > 0)
                Log("コンソール", false, string.Join(" / ", _captured), null,
                    "走っている間に出たエラー。段の失敗より前に出ていることがある。");
        }

        // ================================================================
        // コンソールの取り込み
        // ================================================================

        private readonly List<string> _captured = new List<string>();

        private void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (_captured.Count >= 20) return;

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            { _captured.Add(condition); return; }

            // 自動更新の失敗は警告で出る（RunAutoUpdateGroups）。
            // 警告を全部拾うとノイズに埋もれるので、この印のものだけ拾う。
            if (type == LogType.Warning && condition != null && condition.Contains("[ObjectGroup]"))
                _captured.Add(condition);
        }

        /// <summary>
        /// コンソールにエラーが出ていたら、その場で段を落とす。
        /// 待ち続けてタイムアウトさせると、本当の理由が流れて見えなくなる。
        /// </summary>
        private StageResult? FailedByConsole()
        {
            if (_captured.Count == 0) return null;

            string joined = string.Join(" / ", _captured);
            _captured.Clear();

            return Ng(
                "コンソールにエラーが出た: " + joined,
                null,
                "待ち切れなかったのではなく、前の段が実際に失敗している。"
              + "作業フォルダの関門（PLSandbox）・パス・ファイルの書式を疑うこと。");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("0. 入り口を確かめる",                    StageCheckInputs));
            stages.Add(("1. MQO を読み込む",                     StageImportMqo));
            stages.Add(("2. 読み込み完了を待つ",                 StageWaitMqo));
            stages.Add(("3. 断面プロファイル CSV を読む",        StageLoadProfileCsv));
            stages.Add(("3b. Humanoid の割当を確かめる",         StageCheckHumanoid));
            stages.Add(("4. 梯子オブジェクトと取り付け先を決める", StageResolveLadder));
            stages.Add(("5. はしごから揺れボーンを作る",         StagePlaceSpringBones));
            stages.Add(("6. ボーンが出来たか検査",               StageVerifyBones));
            stages.Add(("7. はしごのグループへ自動更新を立てる", StageArmLadderGroup));
            stages.Add(("7b. 鎖へ揺れ方を付ける",               StageApplySpring));
            stages.Add(("8. はしごからフリルを作る",             StageCreateFrill));
            stages.Add(("9. フリルが出来たか検査",               StageVerifyFrill));
            stages.Add(("10. フリルのグループへ自動更新を立てる", StageArmFrillGroup));
            stages.Add(("11. 一括スキンド化",                    StageConvertToSkinned));
            stages.Add(("12. スキンド化の結果を検査",            StageVerifySkinned));
            stages.Add(("13. 自動更新で塗り直されたか検査",      StageVerifyRepaint));
            stages.Add(("13b. はしごを隠す",                     StageHideLadder));
            stages.Add(("14. VRM へ書き出す",                    StageExportVrm));
        }

        // ================================================================
        // 段 0: 入り口
        // ================================================================

        private StageResult StageCheckInputs()
        {
            string root = Poly_Ling.Core.PLSandbox.WorkFolder ?? "（未設定）";

            Poly_Ling.Core.PLSandbox.TryResolveRead(_mqoPath.value.Trim(), out string mqoFull, out _);
            Poly_Ling.Core.PLSandbox.TryResolveRead(_profileCsvPath.value.Trim(), out string csvFull, out _);

            long mqoBytes = (mqoFull != null && System.IO.File.Exists(mqoFull))
                ? new System.IO.FileInfo(mqoFull).Length : 0;

            return Ok(
                $"作業フォルダ={root} / MQO={mqoFull}（{mqoBytes} bytes）/ 断面CSV={csvFull}",
                "左ペイン「その他 → 作業フォルダ」で、この検証データを含むフォルダを選ぶ",
                "読込はすべて作業フォルダの関門（PLSandbox）を通る。関門で弾かれたときの理由は"
              + "コマンドの結果に入るだけでコンソールには出ないので、走る前にここで同じ関門を通して"
              + "実経路を記録しておく。ここが空や別の場所を指していたら、以降は全部立たない。");
        }

        // ================================================================
        // 段 1-2: MQO
        // ================================================================

        private StageResult StageImportMqo()
        {
            var model = GetModel?.Invoke();
            _meshCountBefore = model?.MeshContextCount ?? 0;

            bool useOrigin = _useOriginCsv.value && !string.IsNullOrWhiteSpace(_originCsvPath.value);

            SendCommand(new ImportMqoFileCommand(
                ModelIndex,
                _mqoPath.value.Trim(),
                null,
                "", "",
                humanoidAutoMap: true,
                applyOriginCsv: useOrigin,
                originCsvPath: useOrigin ? _originCsvPath.value.Trim() : "",
                originCsvIncludeRotation: useOrigin && _originRotation.value));

            return Ok(
                $"ImportMqoFileCommand を送った（Humanoid 自動割当=する / 原点CSV={(useOrigin ? "使う" : "使わない")}）",
                "ファイルパネル → MQO 読込 → 「ボーン名から Humanoid を割り当てる」を入れて読込。"
              + "原点は同じパネルの「原点CSVを適用」",
                "Humanoid の割当が無いと VRM の書き出しで人型として通らない。"
              + "オブジェクトのローカル姿勢は MQO 側に無いので、別ファイル（原点CSV）から入れる。");
        }

        private StageResult StageWaitMqo()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= _meshCountBefore) return StageResult.Retry;

            // 読込は 1 オブジェクトずつ増えることがある。増え終わるまで待つ。
            // 「増えた瞬間」で進むと、まだ半分しか居ないモデルで梯子を探すことになる。
            if (model.MeshContextCount != _lastSeenMeshCount)
            {
                _lastSeenMeshCount = model.MeshContextCount;
                _settleCount       = 0;
                return StageResult.Retry;
            }

            if (++_settleCount < SettleTicks) return StageResult.Retry;

            _meshCountBefore = model.MeshContextCount;
            _boneCountBefore = CountBones(model);

            return Ok(
                $"モデルが出来た（オブジェクト {model.MeshContextCount} / ボーン {_boneCountBefore}）",
                null,
                "コマンドはキュー経由で処理されるので、送った直後のモデルは当てにならない。"
              + "オブジェクトが増え、さらに数が動かなくなるまで同じ段を待ち直している。"
              + "増えた瞬間で進むと、まだ半分しか居ないモデルで梯子を探すことになる。"
              + "この時点でボーンが 0 本なのが、一括スキンド化の前提。");
        }

        // ================================================================
        // 段 3: 断面プロファイル
        // ================================================================

        private StageResult StageLoadProfileCsv()
        {
            var result = ProfilePointsCsvIO.LoadPair(_profileCsvPath.value.Trim(), false);
            if (!result.Success)
                return Ng($"断面プロファイル CSV を読めない（{result.ErrorMessage}）",
                    "図形生成パネル → フリル → 断面プロファイル → 読込",
                    "プロファイルが無いとフリルの面が作れない。パスと書式を確かめること。");

            _profile = result.PointsA ?? new List<Vector2>();
            if (_profile.Count < 2)
                return Ng($"断面プロファイルの点が足りない（{_profile.Count} 点）",
                    null,
                    "フリルは断面を rung 方向へ張るので、点が 2 つ以上要る。");

            return Ok(
                $"断面プロファイルを {_profile.Count} 点読んだ（B 列={(result.HasB ? "あり（使わない）" : "なし")}）",
                "図形生成パネル → フリル → 断面プロファイル → 読込",
                "梯子は描画オブジェクトから取り込むので、CSV はプロファイル側だけ。"
              + "ここで読んだ点はコマンドの ProfileA へそのまま載る。");
        }

        // ================================================================
        // 段 3b: Humanoid の割当
        // ================================================================

        private StageResult StageCheckHumanoid()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            // 割当の正本はモデルが持つ集中表現。
            //
            // 【MeshObject.HumanBodyBone を数えないこと】
            //   ApplyHumanoidMappingCommand の受け口は
            //   model.HumanoidMapping.CopyFrom を呼ぶだけで、
            //   MeshObject.HumanBodyBone には書かない
            //   （PlayerCommandDispatcher.cs:3899-3919）。
            //   per-bone 側へ配るのは HumanoidMappingResolver.ApplyToPerBone で、
            //   別の契機でしか走らない。per-bone を数えると、
            //   割当が入っていても 0 件に見える。
            var mapping = model.HumanoidMapping;
            int mapped = mapping?.BoneIndexMap?.Count ?? 0;

            // 足りない必須ボーンを並べる。VRM はこれが欠けると人型として通らない。
            var missing = new List<string>();
            if (mapping?.BoneIndexMap != null)
            {
                foreach (var req in Poly_Ling.Data.HumanoidBoneMapping.RequiredBones)
                    if (!mapping.BoneIndexMap.ContainsKey(req)) missing.Add(req);
            }

            return Ok(
                mapped > 0
                    ? $"Humanoid の割当が {mapped} 件ある"
                    + (missing.Count > 0
                        ? $"／必須で足りないもの {missing.Count} 件: {string.Join(", ", missing)}"
                        : "／必須はすべて揃っている")
                    : "Humanoid の割当が 1 件も無い",
                "ボーンパネル → Humanoid マッピング → Auto Map",
                "読込時の自動割当は、ボーンが 1 本も無いモデルでは描画メッシュの名前を"
              + "候補にする（PolyLingPlayerViewerCore.cs:9447-9456）。別名表には"
              + "Hips←センター / Spine←上半身 / Chest←上半身2 などが入っている"
              + "（HumanoidBoneMapping.cs:226-260）ので、その名前を持つモデルなら当たる。"
              + "必須が欠けたまま進むと VRM は人型として通らない。");
        }

        // ================================================================
        // 段 4: 梯子と取り付け先
        // ================================================================

        private StageResult StageResolveLadder()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            _ladderIndex = FindMeshByName(model, _ladderName.value.Trim());
            if (_ladderIndex < 0)
                return Ng($"「{_ladderName.value}」が見つからない",
                    "オブジェクトリストで名前を確かめる",
                    "梯子の取り込み元はこのオブジェクト。名前が違うと以後の段が全部立たない。");

            var lc = model.GetMeshContext(_ladderIndex);
            if (lc?.MeshObject == null || lc.MeshObject.FaceCount == 0)
                return Ng($"「{_ladderName.value}」に面が無い", null,
                    "梯子は四角形が一列に並んだ帯。面が無いと検出できない。");

            _attachIndex = lc.HierarchyParentIndex;
            string attachName = (_attachIndex >= 0 && _attachIndex < model.MeshContextCount)
                ? model.GetMeshContext(_attachIndex)?.Name
                : "（モデル直下）";

            var attachCtx = _attachIndex >= 0 ? model.GetMeshContext(_attachIndex) : null;
            string attachKind = attachCtx == null ? "なし" : attachCtx.Type.ToString();

            return Ok(
                $"梯子=「{lc.Name}」（索引 {_ladderIndex} / 面 {lc.MeshObject.FaceCount}） "
              + $"取り付け先=「{attachName}」（索引 {_attachIndex} / 種別 {attachKind}）",
                "図形生成パネル → 揺れもの（はしご）→ 取り込み元を選び、"
              + "「取り付け先を取り込み元の親から決める」を入れる",
                "取り付け先は名前で探さず階層をそのまま読む。名前規則が崩れたモデルでも外れない。"
              + "この時点では親はまだメッシュでよい。スキンド化のとき、"
              + "そのメッシュのボーン配下へ付け替わる。");
        }

        // ================================================================
        // 段 5-7: 揺れボーン
        // ================================================================

        private StageResult StagePlaceSpringBones()
        {
            var model = GetModel?.Invoke();
            _boneCountBefore = CountBones(model);

            SendCommand(new PlaceSpringBoneLadderChainsCommand(
                ModelIndex,
                _ladderIndex,
                BeltAcquireMethod.AutoRing,
                SpringBoneLadderMode.Stack,
                _attachIndex,
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringLadder" : _prefix.value.Trim(),
                setName: "",
                reverseChain: false,
                addTailBone: true,
                tailLength: 0.05f,
                paintWeights: true,
                rungStride: Mathf.Max(1, _rungStride.value),
                chainStride: Mathf.Max(1, _chainStride.value),
                bundleMode: SpringBoneLadderBundleMode.Thin,
                chainRootMasterIndices: null,
                keepAsGroup: true));

            return Ok(
                "PlaceSpringBoneLadderChainsCommand を送った"
              + "（円環を自動検索 / スカート系 / ウェイトを塗る=指定あり / グループとして残す）",
                "図形生成パネル → 揺れもの（はしご）→ 生成",
                "「塗る」を指定していても、取り込み元が MeshFilter 系のうちは塗られない。"
              + "塗ると、塗った頂点だけがボーンの欄を引いて飛ぶため。"
              + "グループとして残しておくと、スキンド化のあとに自動で流し直されて塗られる。");
        }

        private StageResult StageVerifyBones()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            int bones = CountBones(model);
            if (bones <= _boneCountBefore) return StageResult.Retry;

            // はしごのグループを探す。出力は鎖の根元ボーンなので、action で引く。
            _ladderGroup = FindGroupByAction(model, "placeSpringBoneLadderChains");
            if (string.IsNullOrEmpty(_ladderGroup)) return StageResult.Retry;

            var lm = model.GetMeshContext(_ladderIndex)?.MeshObject;
            int painted = CountWeighted(lm);

            if (painted != 0)
                return Ng($"MeshFilter のはしごにウェイトが {painted} 頂点ぶん載っている",
                    null,
                    "この段階で塗られていてはいけない。塗った頂点だけが SkinningMatrix 経路へ移り、"
                  + "WorldMatrix が単位でなければ飛ぶ。塗りはスキンド化のあとに回している。");

            return Ok(
                $"ボーンが {bones - _boneCountBefore} 本増えた（合計 {bones}）／"
              + $"グループ「{_ladderGroup}」が出来た／はしごのウェイトは 0 頂点（正しい）",
                "オブジェクトグループパネルで、出来たグループと出力先を確かめる",
                "出力は鎖の根元ボーン。作り直しではこの ObjectId 列が"
              + "ChainRootMasterIndices へ書き戻され、ボーンを作らず位置だけ流し込む経路に入る。");
        }

        private StageResult StageArmLadderGroup()
        {
            SendCommand(new SetObjectGroupAutoUpdateCommand(ModelIndex, _ladderGroup, true));

            return Ok(
                $"グループ「{_ladderGroup}」の自動更新を立てた",
                "オブジェクトグループパネル → グループを選ぶ → "
              + "「ソースが変わったら自動で作り直す（スキンド化のとき）」を入れる",
                "スキンド化ははしごの頂点をワールドへ焼くのでダイジェストが変わる。"
              + "自動更新が立っていると、変換の直後にこのグループが流れてウェイトが塗られる。");
        }

        // ================================================================
        // 段 7b: 揺れ方
        // ================================================================

        /// <summary>
        /// 鎖へ揺れ方と鎖の先頭を設定する。
        ///
        /// 【なぜ別の段が要るか】
        ///   placeSpringBoneLadderChains は「ボーンを作り、必要ならはしごへ塗る」だけで、
        ///   揺れ方は付けない（PanelCommand.SpringBoneLadder.cs:30）。
        ///   パネルの生成ボタンは続けて ApplySpringToChains を呼ぶので付くが、
        ///   コマンドを直接並べる経路では自分で送る必要がある。
        ///   付けないと VRM に VRMC_springBone が出ず、骨として動くだけで揺れない。
        ///
        /// 【鎖の組み立て】
        ///   名前で拾う。placer が付ける名前は
        ///   {接頭辞}_{鎖番号:00}_top / _1 / _2 … / _end（鎖が 1 本なら番号なし）。
        /// </summary>
        private StageResult StageApplySpring()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            string prefix = string.IsNullOrWhiteSpace(_prefix.value) ? "SpringLadder" : _prefix.value.Trim();

            var chains = CollectChainsByName(model, prefix);
            if (chains.Count == 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"接頭辞「{prefix}」の鎖を見つけられない",
                    "図形生成パネル → 揺れもの（はしご）→ 生成（揺れ方を付ける指定つき）",
                    "名前から鎖を組み立てている。接頭辞か命名規則が変わると拾えない。");
            }

            int maxLen = 0;
            foreach (var ch in chains) maxLen = Mathf.Max(maxLen, ch.Count);

            // 段ごとに同じ値を入れる。根元から先へ変化させたいときはパネル側の
            // 「揺れもの（数値から）」と同じ taper を使うこと。ここでは一定。
            for (int step = 0; step < maxLen; step++)
            {
                var atStep = new List<int>();
                foreach (var ch in chains) if (step < ch.Count) atStep.Add(ch[step]);
                if (atStep.Count == 0) continue;

                SendCommand(new SetSpringBoneJointCommand(
                    ModelIndex, atStep.ToArray(),
                    _springHitRadius.value,
                    _springStiffness.value,
                    _springGravity.value,
                    new Vector3(0f, -1f, 0f),
                    _springDrag.value));
            }

            // 鎖の先頭。これが無いと VRM 側で 1 本の鎖として束ねられない。
            for (int i = 0; i < chains.Count; i++)
            {
                string chainName = chains.Count > 1 ? $"{prefix}_{i:00}" : prefix;
                SendCommand(new SetSpringBoneChainRootCommand(
                    ModelIndex, chains[i][0], chainName, "", System.Array.Empty<int>()));
            }

            return Ok(
                $"鎖 {chains.Count} 本 / 段 {maxLen} へ揺れ方を入れ、鎖の先頭を指定した"
              + $"（かたさ {_springStiffness.value:0.###} / 抵抗 {_springDrag.value:0.###} / "
              + $"重力 {_springGravity.value:0.###} / 当たり半径 {_springHitRadius.value:0.###}）",
                "図形生成パネル → 揺れもの（はしご）→ 揺れ方を付けるを入れて生成",
                "鎖を作るコマンドは揺れ方を付けない。付けないと VRM に VRMC_springBone が"
              + "出ず、ボーンとして動くだけで揺れない。");
        }

        /// <summary>
        /// 名前から鎖を組み立てる。{接頭辞}_{番号:00}_top → _1 → _2 … → _end の順。
        /// 鎖が 1 本のときは番号が付かない（placer の命名と同じ）。
        /// </summary>
        private static List<List<int>> CollectChainsByName(ModelContext model, string prefix)
        {
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!byName.ContainsKey(mc.Name)) byName[mc.Name] = i;
            }

            var chains = new List<List<int>>();

            // 鎖 1 本だけの形も、番号付きの形も同じ手順で拾う。
            var heads = new List<string>();
            if (byName.ContainsKey(prefix + "_top")) heads.Add(prefix);
            for (int c = 0; c < 1000; c++)
            {
                string name = $"{prefix}_{c:00}";
                if (!byName.ContainsKey(name + "_top")) break;
                heads.Add(name);
            }

            foreach (string head in heads)
            {
                var chain = new List<int> { byName[head + "_top"] };

                for (int k = 1; k < 4096; k++)
                {
                    if (!byName.TryGetValue($"{head}_{k}", out int idx)) break;
                    chain.Add(idx);
                }

                if (byName.TryGetValue(head + "_end", out int tail)) chain.Add(tail);

                chains.Add(chain);
            }

            return chains;
        }

        // ================================================================
        // 段 8-10: フリル
        // ================================================================

        private StageResult StageCreateFrill()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            _meshCountBefore = model.MeshContextCount;

            // 梯子の点列をここで取り込む。
            //
            // 【なぜ空で送れないか】
            //   生成そのものは点列を使う。取り込み方（acquireMethod）は
            //   「作り直しのときに同じ手順を掛け直す」ための控えで、
            //   初回の生成では読まれない（PanelCommand.cs:5616）。
            //   空で送ると 0 頂点 0 面のフリルが出来て、次の段が待ち続ける。
            //
            //   手順は ObjectGroupOps.RefreshBelts（:397-417）と同じにそろえる。
            //   ずらすと、作り直したときに初回と違う梯子を読むことになる。
            var src = model.GetMeshContext(_ladderIndex);

            var acquired = BeltAcquire.Acquire(src, BeltAcquireMethod.AutoRing, true, "");
            if (!acquired.Ok || acquired.Belts.Count == 0)
                return Ng($"梯子を取り込めない（{acquired.Message}）",
                    "図形生成パネル → フリル → 取り込み元を選び「円環を自動検索」→ 取り込み",
                    "円筒の側面は一周してつながった帯なので円環として取れるはず。"
                  + "取れないなら、面が四角形で一列に並んでいるかを疑う。");

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var beltLeft, out var beltRight, out var beltStarts,
                out var beltClosed, out var beltFlip, out var beltHeight);

            var prms = FrillParams.Default;
            prms.MeshName           = FrillName;
            prms.HeightScale        = _frillHeight.value;
            prms.InheritBeltWeights = true;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;

            SendCommand(new CreateFrillCommand(
                ModelIndex,
                prms,
                _profile.ToArray(),
                null,
                beltLeft, beltRight, beltStarts,
                beltClosed, beltFlip, beltHeight,
                BeltOrientOptions.Default,
                BeltSplineOptions.Default,
                pl,
                beltSourceIndex: _ladderIndex,
                acquireMethod: BeltAcquireMethod.AutoRing,
                acquireCrossRows: true));

            return Ok(
                $"CreateFrillCommand を送った（梯子={_ladderName.value} / 取り込んだ帯 {acquired.Belts.Count} 本 / "
              + $"段グループ {acquired.GroupCount} / はしごのウェイトを引き継ぐ=する / グループとして残す）",
                "図形生成パネル → フリル → 取り込み元を選び「円環を自動検索」→ "
              + "「はしごのウェイトを引き継ぐ」を入れて生成",
                "点列は今ここで取り込んだものを載せている。取り込み方も一緒に持たせてあるので、"
              + "作り直しのときは同じ手順が掛け直される。"
              + "引き継ぎはこの段では空振りする（はしごにまだウェイトが無い）。"
              + "スキンド化のあとに自動更新で入る。");
        }

        private StageResult StageVerifyFrill()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= _meshCountBefore) return StageResult.Retry;

            _frillIndex = FindMeshByName(model, FrillName);
            if (_frillIndex < 0) return StageResult.Retry;

            var mo = model.GetMeshContext(_frillIndex)?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            _frillGroup = FindGroupByAction(model, "createFrill");
            if (string.IsNullOrEmpty(_frillGroup)) return StageResult.Retry;

            _meshCountBefore = model.MeshContextCount;

            return Ok(
                $"「{FrillName}」が出来た（索引 {_frillIndex} / 頂点 {mo.VertexCount} / 面 {mo.FaceCount}）／"
              + $"グループ「{_frillGroup}」が出来た",
                null,
                "フリルは別メッシュなので、はしごへ塗ったウェイトは自動では乗らない。"
              + "生成時に位置で突き合わせて引き継ぐ仕組みに乗せてある。");
        }

        private StageResult StageArmFrillGroup()
        {
            SendCommand(new SetObjectGroupAutoUpdateCommand(ModelIndex, _frillGroup, true));

            return Ok(
                $"グループ「{_frillGroup}」の自動更新を立てた",
                "オブジェクトグループパネル → グループを選ぶ → 自動更新を入れる",
                "スキンド化のあと、はしごのグループが塗ったウェイトを、"
              + "このグループが作り直しで引き継ぐ。並びは ObjectGroups の順なので、"
              + "はしご→フリルの順に流れる。");
        }

        // ================================================================
        // 段 11-13: スキンド化
        // ================================================================

        private StageResult StageConvertToSkinned()
        {
            var model = GetModel?.Invoke();
            _boneCountBefore = CountBones(model);

            SendCommand(new ConvertMeshFilterToSkinnedCommand(
                ModelIndex, false, false, true));

            return Ok(
                $"ConvertMeshFilterToSkinnedCommand を送った（変換前のボーン {_boneCountBefore} 本）",
                "MeshFilter→Skinned パネル → 変換",
                "既にボーンが居ても止めない。変換はメッシュぶんのボーンを先頭へ挿し、"
              + "既にあるボーンの索引・親・ウェイトをその数だけずらす。"
              + "メッシュを親にしていたボーンは、そのメッシュのボーン配下へ付け替わる。");
        }

        private StageResult StageVerifySkinned()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            int bones = CountBones(model);
            if (bones <= _boneCountBefore)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng("ボーンが増えないまま待ち切った。" + DumpModel(model),
                    "MeshFilter→Skinned パネルで手で変換してみる",
                    "変換コマンドが実行されていない。ディスパッチャの Fail は"
                  + "コマンドの結果へ書くだけでコンソールへ出さない"
                  + "（PlayerCommandDispatcher.cs:523）ので、失敗しても何も見えない。"
                  + "MeshFilterToSkinnedConverter.CollectMeshEntries が 0 件なら"
                  + "「変換できる対象がありません」で止まっている。");
            }

            // 梯子とフリルは索引が動いているうえ、名前も変わりうる。
            //
            // 【なぜ名前が変わるか】
            //   ボーンはメッシュと同名で作られる。そのままだとヒエラルキー出力で
            //   同名の GameObject が並び、Humanoid の割当先が一意でなくなるので、
            //   衝突したメッシュ側へ接尾辞が付く
            //   （MeshFilterToSkinnedConverter の Phase 2c / MeshNameSuffix）。
            //   元の名前だけで探すと、ここから先が全部立たなくなる。
            _ladderIndex = FindByNameOrSkinned(model, _ladderName.value.Trim());
            _frillIndex  = FindByNameOrSkinned(model, FrillName);

            if (_ladderIndex < 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{_ladderName.value}」を引き直せない。" + DumpModel(model),
                    "オブジェクトリストで梯子の名前を確かめる",
                    "変換でメッシュ名に接尾辞が付くことがある"
                  + "（MeshFilterToSkinnedConverter.MeshNameSuffix）。"
                  + "元の名前と接尾辞つきの両方で探しているので、どちらでも無いなら"
                  + "別の名前へ変わっているか、消えている。");
            }

            var lc = model.GetMeshContext(_ladderIndex);
            if (lc == null || !lc.IsSkinned)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{lc?.Name}」がスキンド系にならない。" + DumpModel(model),
                    null,
                    "変換の対象から外れている。CollectMeshEntries は Type が Mesh か"
                  + "ミラー側のものだけを拾う（MeshFilterToSkinnedConverter.cs:105）。");
            }
            // 鎖の根元の親がボーンになっているか。
            int rootIndex = FindFirstBoneByPrefix(model,
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringLadder" : _prefix.value.Trim());

            string parentKind = "（鎖が見つからない）";
            if (rootIndex >= 0)
            {
                int p = model.GetMeshContext(rootIndex).HierarchyParentIndex;
                parentKind = (p >= 0 && p < model.MeshContextCount)
                    ? $"{model.GetMeshContext(p)?.Name}（{model.GetMeshContext(p)?.Type}）"
                    : "モデル直下";
            }

            if (rootIndex >= 0)
            {
                int p = model.GetMeshContext(rootIndex).HierarchyParentIndex;
                var pc = (p >= 0 && p < model.MeshContextCount) ? model.GetMeshContext(p) : null;
                if (pc != null && pc.Type != MeshType.Bone)
                    return Ng($"鎖の親がボーンになっていない（{pc.Name} / {pc.Type}）",
                        null,
                        "メッシュを親にしていたボーンは、変換のときにそのメッシュのボーンへ"
                      + "付け替わるはず。付け替えが働いていない。");
            }

            return Ok(
                $"ボーンが {bones - _boneCountBefore} 本増えた（合計 {bones}）／"
              + $"はしご=「{lc.Name}」がスキンド系になった／鎖の親＝{parentKind}",
                null,
                "変換は全メッシュの全頂点へ {自分のボーン, 1.0} を書く。"
              + "はしごのウェイトもここで一度その形になる。塗り直しは次の段で見る。");
        }

        private StageResult StageVerifyRepaint()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            // 鎖のボーン索引の集合。名前の接頭辞で引く
            // （変換で名前が変わるのはメッシュ側だけ。ボーン名はそのまま）。
            var chainBones = CollectChainBones(model,
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringLadder" : _prefix.value.Trim());

            var lm = model.GetMeshContext(_ladderIndex)?.MeshObject;
            if (lm == null) return StageResult.Retry;

            int ladderOnChain = CountWeightedTo(lm, chainBones);

            if (ladderOnChain == 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;

                return Ng(
                    $"はしごのウェイトが鎖のボーンを指さないまま待ち切った"
                  + $"（鎖のボーン {chainBones.Count} 本 / はしごの頂点 {lm.VertexCount}）"
                  + DumpGroups(),
                    "オブジェクトグループパネルで、はしごのグループが「要更新」でなくなっているかを見る",
                    "変換直後は全頂点が「自分のメッシュのボーン」1.0。自動更新が流れると"
                  + "鎖のボーンへ塗り替わる。上の並びで、自動更新が立っているか・"
                  + "要更新になっているか・ソースを引けているかを見ること。");
            }

            var fm = _frillIndex >= 0 ? model.GetMeshContext(_frillIndex)?.MeshObject : null;
            int frillOnChain = CountWeightedTo(fm, chainBones);

            return Ok(
                $"はしごの {ladderOnChain} 頂点が鎖のボーンを指している"
              + $"（鎖のボーン {chainBones.Count} 本）／"
              + $"フリルの {frillOnChain} 頂点が鎖のボーンを指している",
                "オブジェクトグループパネルで、両方のグループが「要更新」でなくなっているのを確かめる",
                "間引きなし（段・本とも 1）では、はしごの頂点は 1 ボーン 1.0 のままになる。"
              + "だから『2 本以上のボーンを持つか』では見られない。"
              + "『鎖のボーンを指しているか』で見るのが正しい。"
              + "フリルは線分の両端のウェイトを断面 x で混ぜて受け取る。");
        }

        // ================================================================
        // 段 13b: はしごを隠す
        // ================================================================

        /// <summary>
        /// はしごを非表示にする。
        ///
        /// 【なぜ塗り直しの検査より後か】
        ///   段 13 まではしごのウェイトを読む。非表示はモデルの中身を変えないので
        ///   検査結果は変わらないが、隠したものを検査するのは筋が悪い。
        ///   すべて確かめてから隠す。
        ///
        /// 【なぜ VRM の前か】
        ///   VRM の書き出しは既定で非表示メッシュを落とす
        ///   （Vrm10ExportSettings.ExportInvisibleObjects = false →
        ///     Vrm10ExporterImpl.cs:108 で ExportVisibleOnly = true →
        ///     PolyLingToVrmLibConverter.cs:581 が SkippedInvisible として飛ばす）。
        ///   ここで隠すと、はしごは VRM に入らない。フリルだけが残る。
        ///   ボーンは別扱いなので、鎖は隠しても消えない。
        /// </summary>
        private StageResult StageHideLadder()
        {
            if (_hideLadder == null || !_hideLadder.value)
                return Ok("はしごは隠さない（指定なし）", null,
                    "隠さないと VRM にはしごの帯がそのまま入る。"
                  + "形を決めるための下地なので、通常は隠す。");

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            // スキンド化で索引も名前も動くので、ここで引き直す。
            _ladderIndex = FindByNameOrSkinned(model, _ladderName.value.Trim());
            if (_ladderIndex < 0)
                return Ng($"「{_ladderName.value}」を引き直せない。" + DumpModel(model), null,
                    "隠す対象を masterIndex で指すので、引けないと送れない。");

            var lc = model.GetMeshContext(_ladderIndex);

            SendCommand(new SetBatchVisibilityCommand(
                ModelIndex, new[] { _ladderIndex }, false));

            return Ok(
                $"「{lc?.Name}」（索引 {_ladderIndex}）を非表示にした",
                "オブジェクトリストで、はしごの行の目のアイコンを消す",
                "VRM の書き出しは既定で非表示メッシュを落とす"
              + "（Vrm10ExportSettings.ExportInvisibleObjects = false）。"
              + "はしごは形を決めるための下地なので、隠せば VRM にはフリルだけが入る。"
              + "鎖のボーンは非表示の対象ではないので消えない。");
        }

        // ================================================================
        // 段 14: VRM
        // ================================================================

        private StageResult StageExportVrm()
        {
            SendCommand(new ExportVrmFileCommand(ModelIndex, _vrmPath.value.Trim()));

            return Ok(
                $"ExportVrmFileCommand を送った（{_vrmPath.value}）",
                "ファイルパネル → VRM 書き出し",
                "スキンド系になっていて Humanoid の割当があることが前提。"
              + "揺れ方（SpringBoneJoint）はこの検証では付けていないので、"
              + "VRM 側の揺れ設定は空になる。");
        }

        // ================================================================
        // 小物
        // ================================================================

        /// <summary>
        /// 待ち切れなかったときに出すモデルの状態。
        /// 「何が足りないか」を次の一手へつなげるための最小限だけを並べる。
        /// </summary>
        private string DumpModel(ModelContext model)
        {
            if (model == null) return "（モデルが無い）";

            int meshes = 0, skinned = 0, bones = 0, others = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (mc.Type == MeshType.Bone) { bones++; continue; }
                if (mc.Type == MeshType.Mesh)
                {
                    meshes++;
                    if (mc.IsSkinned) skinned++;
                    continue;
                }
                others++;
            }

            string name = _ladderName.value.Trim();
            string suffixed = name + Poly_Ling.Ops.MeshFilterToSkinnedConverter.MeshNameSuffix;

            return $"［全 {model.MeshContextCount} / メッシュ {meshes}（うちスキンド {skinned}）"
                 + $" / ボーン {bones}（変換前 {_boneCountBefore}）/ その他 {others}］"
                 + $"メッシュ「{name}」={FindMeshByName(model, name)}"
                 + $" メッシュ「{suffixed}」={FindMeshByName(model, suffixed)}"
                 + $" メッシュ「{FrillName}」={FindMeshByName(model, FrillName)}"
                 + $" メッシュ「{FrillName}{Poly_Ling.Ops.MeshFilterToSkinnedConverter.MeshNameSuffix}」="
                 + $"{FindMeshByName(model, FrillName + Poly_Ling.Ops.MeshFilterToSkinnedConverter.MeshNameSuffix)}";
        }

        private static int FindByName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.Name == name) return i;
            }
            return -1;
        }

        /// <summary>
        /// 名前で描画メッシュを引く。見つからなければ「名前＋スキンド化の接尾辞」でも引く。
        ///
        /// 【ボーンを拾わないこと】
        ///   ボーンはメッシュと同名で作られ、リストの先頭へ挿される
        ///   （MeshFilterToSkinnedConverter の Phase 2）。名前だけで探すと
        ///   先頭のボーンを掴み、IsSkinned が false のまま止まる。
        ///   衝突したメッシュ側には接尾辞が付く（MeshNameSuffix）。
        /// </summary>
        private static int FindByNameOrSkinned(ModelContext model, string name)
        {
            int i = FindMeshByName(model, name);
            if (i >= 0) return i;

            return FindMeshByName(
                model, name + Poly_Ling.Ops.MeshFilterToSkinnedConverter.MeshNameSuffix);
        }

        /// <summary>名前で描画メッシュ（Type == Mesh）だけを引く。無ければ -1。</summary>
        private static int FindMeshByName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Mesh) continue;
                if (mc.Name == name) return i;
            }
            return -1;
        }

        private static int FindFirstBoneByPrefix(ModelContext model, string prefix)
        {
            if (model == null || string.IsNullOrEmpty(prefix)) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Name.StartsWith(prefix, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static int CountBones(ModelContext model)
        {
            if (model == null) return 0;
            int n = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (model.GetMeshContext(i)?.Type == MeshType.Bone) n++;
            return n;
        }

        /// <summary>action が一致する最後のグループの名前。無ければ空。</summary>
        private static string FindGroupByAction(ModelContext model, string action)
        {
            if (model?.ObjectGroups == null) return "";
            string found = "";
            foreach (var g in model.ObjectGroups)
            {
                if (g == null) continue;
                for (int s = 0; s < g.StepCount; s++)
                    if (string.Equals(g.GetStep(s)?.Action, action, StringComparison.Ordinal))
                    { found = g.Name; break; }
            }
            return found;
        }

        /// <summary>
        /// オブジェクトグループの実態を並べる。自動更新が流れなかったときの
        /// 判定材料（自動更新の可否・要更新かどうか・参照を引けるか）だけを出す。
        /// </summary>
        private string DumpGroups()
        {
            var project = GetProject?.Invoke();
            var model   = GetModel?.Invoke();
            if (model?.ObjectGroups == null) return "／グループ: （無し）";

            var sb = new System.Text.StringBuilder();
            sb.Append($"／グループ {model.ObjectGroups.Count} 件:");

            foreach (var g in model.ObjectGroups)
            {
                if (g == null) { sb.Append(" [null]"); continue; }

                int srcAlive = 0;
                var src = g.SourceObjectIds;
                foreach (ulong id in src)
                    if (Poly_Ling.Ops.ObjectGroupOps.Resolve(project, id) != null) srcAlive++;

                int outAlive = 0;
                var outs = g.OutputObjectIds;
                foreach (ulong id in outs)
                    if (Poly_Ling.Ops.ObjectGroupOps.Resolve(project, id) != null) outAlive++;

                bool stale = Poly_Ling.Ops.ObjectGroupOps.IsStale(project, g);

                sb.Append($" [{g.Name} / action={g.Action} / 自動更新={(g.AutoUpdate ? "有" : "無")}"
                        + $" / 要更新={(stale ? "有" : "無")} / ダイジェスト={(string.IsNullOrEmpty(g.SourceDigest) ? "空" : "有")}"
                        + $" / ソース {srcAlive}/{src.Count} 引けた"
                        + $" / 出力 {outAlive}/{outs.Count} 引けた]");
            }

            return sb.ToString();
        }

        /// <summary>名前が接頭辞で始まるボーンの索引を集める。</summary>
        private static HashSet<int> CollectChainBones(ModelContext model, string prefix)
        {
            var set = new HashSet<int>();
            if (model == null || string.IsNullOrEmpty(prefix)) return set;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (mc.Name.StartsWith(prefix, StringComparison.Ordinal)) set.Add(i);
            }
            return set;
        }

        /// <summary>
        /// 指定したボーンの集合を指している頂点の数。
        /// 重みが 0 のスロットは数えない。
        /// </summary>
        private static int CountWeightedTo(MeshObject mo, HashSet<int> bones)
        {
            if (mo == null || bones == null || bones.Count == 0) return 0;

            int n = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var bw = mo.Vertices[i]?.BoneWeight;
                if (bw == null) continue;

                var w = bw.Value;
                if ((w.weight0 > 0.0001f && bones.Contains(w.boneIndex0)) ||
                    (w.weight1 > 0.0001f && bones.Contains(w.boneIndex1)) ||
                    (w.weight2 > 0.0001f && bones.Contains(w.boneIndex2)) ||
                    (w.weight3 > 0.0001f && bones.Contains(w.boneIndex3)))
                    n++;
            }
            return n;
        }

        private static int CountWeighted(MeshObject mo)
        {
            if (mo == null) return 0;
            int n = 0;
            for (int i = 0; i < mo.VertexCount; i++)
                if (mo.Vertices[i]?.BoneWeight != null) n++;
            return n;
        }

        /// <summary>2 本以上のボーンへ配られた頂点の数。</summary>
        private static int CountMultiBoneWeighted(MeshObject mo)
        {
            if (mo == null) return 0;
            int n = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var bw = mo.Vertices[i]?.BoneWeight;
                if (bw == null) continue;
                if (bw.Value.weight1 > 0.0001f) n++;
            }
            return n;
        }
    }
}
