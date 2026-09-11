// PlayerSpringSkinPipeScenarioSubPanel.cs
// 揺れもの（パイプ）→ スキンド化 → VRM の順で通す自動検証。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜフリル版と別のパネルにするか】
//   フリル版（PlayerSpringSkinScenarioSubPanel）は通る。パイプは通らない。
//   フリル版を書き換えて確かめると、通っていた経路まで巻き込む。
//   フリル版はそのまま残し、こちらを独立させて差だけを見る。
//
// 【フリル版との差は 3 つだけ】
//   1. 梯子が閉じた円環ではなく、開いた 1 本のはしご（アホ毛用はしご）。
//      取り込み方は AutoRing ではなく AutoLadder（BeltStackDetector）。
//      鎖の並べ方も Stack（スカート系）ではなく Strand（髪系）。
//   2. 断面プロファイルを CSV ではなくモデル内の描画オブジェクトから読む
//      （アホ毛用パイププロファイル。MQO に入っている 2 頂点面の閉ループ）。
//   3. 生成が CreateFrillCommand ではなく CreatePipeCommand。
//   他（MQO・Humanoid 割当・自動更新・一括スキンド化・VRM）はフリル版と同じ。
//
// 【なぜフリルが通ってパイプが通らないかの候補】
//   フリルは ConnectShared = true（既定）だと FrillMeshGenerator が 1 メッシュに
//   まとめるので MeshObjectAppendOps.Append を通らない
//   （PrimitiveMeshFactory.cs:274-288）。
//   パイプは梯子 1 本ごとに作って必ず Append で連結する
//   （PrimitiveMeshFactory.cs:357-372）。Append は BoneWeight / MirrorBoneWeight を
//   写していなかった。写すように直したが、パイプでの通し確認をしていない。
//   この検証はその通し確認そのもの。
//
// 【ウェイトを塗る契機はスキンド化の直後 1 か所】
//   フリル版と同じ。理由もそちらのクラス冒頭に書いたものと同一。
//     ・MeshFilter のうちに塗ると、塗った頂点だけが SkinningMatrix 経路へ移り、
//       WorldMatrix が単位でなければ飛ぶ。
//     ・一括スキンド化は全頂点へ {自分のボーン, 1.0} を書くので、先に塗っても消える。
//   はしご／パイプのグループへ「自動更新」を立てておき、スキンド化のあとに
//   PlayerCommandDispatcher.RunAutoUpdateGroups が流し直して塗る。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Pipe;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools.SpringBoneRig;

namespace Poly_Ling.Player
{
    public class PlayerSpringSkinPipeScenarioSubPanel : PlayerStagedTestSubPanelBase
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

        private TextField    _mqoPath, _originCsvPath, _ladderName, _profileObjectName, _prefix, _vrmPath;
        private Toggle       _useOriginCsv, _originRotation, _profileClosed, _capEnds, _hideLadder;
        private IntegerField _rungStride, _chainStride;
        private FloatField   _thickness;
        private FloatField   _springStiffness, _springDrag, _springGravity, _springHitRadius;

        // ================================================================
        // 持ち回り
        // ================================================================

        private int    _meshCountBefore;
        private int    _boneCountBefore;
        private int    _ladderIndex   = -1;
        private int    _attachIndex   = -1;
        private int    _profileIndex  = -1;
        private string _ladderGroup   = "";
        private string _pipeGroup     = "";
        private int    _pipeIndex     = -1;
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
        /// 共通部のタイムアウトは全段ぶんの合計なので、どの段で何を待っていたかが残らない。
        /// </summary>
        private int Waited()
        {
            if (_waitStage != CurrentStageName) { _waitStage = CurrentStageName; _waitTicks = 0; }
            return ++_waitTicks;
        }

        /// <summary>1 つの段で待つ上限。200ms × 100 ＝ 20 秒。</summary>
        private const int StageWaitLimit = 100;

        private const string PipeName = "SS_Pipe";

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "揺れもの（パイプ）→スキンド→VRM 自動検証";

        protected override long StageIntervalMs => 200;
        protected override int  MaxRetry        => 900;   // 200ms × 900 ＝ 180 秒

        protected override string NoteText =>
            "MQO 読込（Humanoid 自動割当 / 原点CSV）→ 断面を描画オブジェクトから取り込む\n"
          + "→ はしごから揺れボーン → パイプ生成 → 一括スキンド化 → 自動更新で塗り直し → VRM 保存。\n"
          + "フリル版と同じ MQO・同じ順で、フリルの段だけをパイプに置き換えたものです。";

        // ================================================================
        // 既定のパス・名前
        //
        // フリル版と同じ MQO を使う。違いをフリルかパイプかだけにするため。
        // 梯子と断面は同じ MQO の中に入っているオブジェクトを名前で指す。
        // ================================================================

        private const string DefaultMqoPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\テストデータろぼFろぼK\Tpose\roboF_T5_SK.mqo";

        private const string DefaultOriginCsvPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\テストデータろぼFろぼK\Tpose\ローカル原点MoldelFKDL_5T.csv";

        private const string DefaultVrmPath =
            @"D:\UNITY保存の品\TEO3.0開発中\PolyLing\データとツール\MCPで自動生成\tmpp_pipe.vrm";

        /// <summary>梯子。MQO の「頭」の下にある開いたはしご（四角形 8 枚＋開始／終了三角形）。</summary>
        private const string DefaultLadderName = "アホ毛用はしご";

        /// <summary>断面。MQO に入っている 2 頂点面 5 本の閉ループ。</summary>
        private const string DefaultProfileObjectName = "アホ毛用パイププロファイル";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("入力ファイル"));

            _mqoPath        = T("MQO", DefaultMqoPath);
            _useOriginCsv   = TG("オブジェクトローカル姿勢を別ファイルから読む", true);
            _originCsvPath  = T("原点CSV", DefaultOriginCsvPath);
            _originRotation = TG("原点CSV の回転列も使う", true);

            root.Add(_mqoPath);
            root.Add(_useOriginCsv);
            root.Add(_originCsvPath);
            root.Add(_originRotation);
            root.Add(Hint("いずれも作業フォルダの下だけを読めます（PLSandbox）。"));

            root.Add(Sec("はしご"));
            _ladderName  = T("梯子オブジェクト名", DefaultLadderName);
            _prefix      = T("ボーン名の接頭辞", "SpringAhoge");
            _rungStride  = I("段ストライド", 1);
            _chainStride = I("本ストライド", 1);
            _hideLadder  = TG("VRM へ書き出す前にはしごを隠す", true);
            root.Add(_ladderName); root.Add(_prefix);
            root.Add(_rungStride); root.Add(_chainStride);
            root.Add(_hideLadder);
            root.Add(Hint("開いた 1 本のはしごなので、取り込みは「はしごを自動検索」、"
                        + "鎖の並べ方は髪系（はしご 1 本＝鎖 1 本）です。"));
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

            root.Add(Sec("パイプ"));
            _profileObjectName = T("断面オブジェクト名", DefaultProfileObjectName);
            _profileClosed     = TG("断面を閉ループとして扱う（筒にする）", true);
            _capEnds           = TG("開いた梯子の両端に蓋を張る", true);
            _thickness         = F("厚み（0 で厚み付けなし）", 0f);
            root.Add(_profileObjectName); root.Add(_profileClosed);
            root.Add(_capEnds); root.Add(_thickness);
            root.Add(Hint("断面は CSV ではなくモデル内の描画オブジェクトから読みます。"
                        + "2 頂点の面（補助線）だけを拾い、長辺が 1 になるよう正規化します。"));

            root.Add(Sec("書き出し"));
            _vrmPath = T("VRM の書き出し先", DefaultVrmPath);
            root.Add(_vrmPath);
            root.Add(Hint("書き出し先のフォルダが無いときは作られません。先にフォルダを用意しておくこと。"
                        + "フリル版と同じ名前にすると上書きになるので、既定は別名にしてあります。"));
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

            // 走らせる前に入り口を全部確かめる。
            // ディスパッチャの Fail はコマンドの結果へ書くだけでコンソールへ出さない
            // （PlayerCommandDispatcher.cs:523）ので、関門で弾かれても
            // この検証からは「待ち切れなかった」としか見えない。
            if (!Poly_Ling.Core.PLSandbox.HasWorkFolder)
            {
                SetStatus("作業フォルダが未設定です。左ペインの「その他 → 作業フォルダ」で "
                        + @"D:\UNITY保存の品\TEO3.0開発中\PolyLing を選んでください。");
                return false;
            }

            if (!CheckRead("MQO", _mqoPath.value)) return false;

            if (_useOriginCsv.value && !string.IsNullOrWhiteSpace(_originCsvPath.value)
                && !CheckRead("原点CSV", _originCsvPath.value)) return false;

            if (string.IsNullOrWhiteSpace(_ladderName.value))
            { SetStatus("梯子オブジェクト名を入れてください。"); return false; }

            if (string.IsNullOrWhiteSpace(_profileObjectName.value))
            { SetStatus("断面オブジェクト名を入れてください。"); return false; }

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

        /// <summary>入力ファイルを 1 本確かめる。関門を通るか、実在するかの両方を見る。</summary>
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
            _ladderIndex  = -1;
            _attachIndex  = -1;
            _profileIndex = -1;
            _ladderGroup  = "";
            _pipeGroup    = "";
            _pipeIndex    = -1;
            _profile      = new List<Vector2>();
            _lastSeenMeshCount = -1;
            _settleCount       = 0;
            _waitStage         = "";
            _waitTicks         = 0;

            // 走っている間だけコンソールを拾う。読込やスキンド化が落ちたときの
            // 実際の理由はコンソールにしか出ない。
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

            // 自動更新の失敗は警告で出る（RunAutoUpdateGroups）。この印のものだけ拾う。
            if (type == LogType.Warning && condition != null && condition.Contains("[ObjectGroup]"))
                _captured.Add(condition);
        }

        /// <summary>コンソールにエラーが出ていたら、その場で段を落とす。</summary>
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
            stages.Add(("0. 入り口を確かめる",                      StageCheckInputs));
            stages.Add(("1. MQO を読み込む",                       StageImportMqo));
            stages.Add(("2. 読み込み完了を待つ",                   StageWaitMqo));
            stages.Add(("3. 断面を描画オブジェクトから取り込む",   StageImportProfile));
            stages.Add(("3b. Humanoid の割当を確かめる",           StageCheckHumanoid));
            stages.Add(("4. 梯子オブジェクトと取り付け先を決める", StageResolveLadder));
            stages.Add(("5. はしごから揺れボーンを作る",           StagePlaceSpringBones));
            stages.Add(("6. ボーンが出来たか検査",                 StageVerifyBones));
            stages.Add(("7. はしごのグループへ自動更新を立てる",   StageArmLadderGroup));
            stages.Add(("7b. 鎖へ揺れ方を付ける",                  StageApplySpring));
            stages.Add(("8. はしごからパイプを作る",               StageCreatePipe));
            stages.Add(("9. パイプが出来たか検査",                 StageVerifyPipe));
            stages.Add(("10. パイプのグループへ自動更新を立てる",  StageArmPipeGroup));
            stages.Add(("11. 一括スキンド化",                      StageConvertToSkinned));
            stages.Add(("12. スキンド化の結果を検査",              StageVerifySkinned));
            stages.Add(("13. 自動更新で塗り直されたか検査",        StageVerifyRepaint));
            stages.Add(("13b. はしごを隠す",                       StageHideLadder));
            stages.Add(("14. VRM へ書き出す",                      StageExportVrm));
        }

        // ================================================================
        // 段 0: 入り口
        // ================================================================

        private StageResult StageCheckInputs()
        {
            string root = Poly_Ling.Core.PLSandbox.WorkFolder ?? "（未設定）";

            Poly_Ling.Core.PLSandbox.TryResolveRead(_mqoPath.value.Trim(), out string mqoFull, out _);

            long mqoBytes = (mqoFull != null && System.IO.File.Exists(mqoFull))
                ? new System.IO.FileInfo(mqoFull).Length : 0;

            return Ok(
                $"作業フォルダ={root} / MQO={mqoFull}（{mqoBytes} bytes）/ "
              + $"梯子=「{_ladderName.value}」/ 断面=「{_profileObjectName.value}」",
                "左ペイン「その他 → 作業フォルダ」で、この検証データを含むフォルダを選ぶ",
                "読込はすべて作業フォルダの関門（PLSandbox）を通る。弾かれた理由はコマンドの結果に"
              + "入るだけでコンソールには出ないので、走る前にここで同じ関門を通して実経路を記録する。"
              + "梯子も断面もこの MQO の中のオブジェクトなので、別ファイルは要らない。");
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
              + "この時点でボーンが 0 本なのが、一括スキンド化の前提。");
        }

        // ================================================================
        // 段 3: 断面（描画オブジェクトから）
        // ================================================================

        /// <summary>
        /// 断面プロファイルをモデル内の描画オブジェクトから読む。
        /// 図形生成パネルの「取り込み(メッシュ→断面)」と同じ経路
        /// （PlayerBeltGroupTestSubPanelBase.StageImportProfile と同じ手順）。
        /// </summary>
        private StageResult StageImportProfile()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            string name = _profileObjectName.value.Trim();
            _profileIndex = FindMeshByName(model, name);
            if (_profileIndex < 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"断面オブジェクト「{name}」が見つからない",
                    "オブジェクトリストで名前を確かめる",
                    "断面は CSV ではなくモデル内の描画オブジェクトから読む。"
                  + "MQO に入っていない、または名前が違う。");
            }

            var mo = model.GetMeshContext(_profileIndex)?.MeshObject;
            if (mo == null) return StageResult.Retry;

            var lineFaces = LineProfileExtractor.CollectLineFaceIndices(mo);
            if (lineFaces.Count == 0)
                return Ng($"「{name}」に 2 頂点ラインが 1 本も無い", null,
                    "取り込みは「頂点 2 個だけの面」を線とみなして拾う。"
                  + "三角形や四角形の面しか無いオブジェクトからは断面を作れない。");

            bool closed = _profileClosed.value;

            List<Vector2> raw = null;
            if (closed)
            {
                var loops = LineProfileExtractor.ExtractLoops(mo, lineFaces);
                if (loops != null)
                {
                    foreach (var lp in loops)
                    {
                        if (lp?.Points == null || lp.Points.Count < 3) continue;
                        if (raw == null || lp.Points.Count > raw.Count) raw = lp.Points;
                    }
                }
            }
            else
            {
                raw = LineProfileExtractor.ExtractPolyline(mo, lineFaces);
            }

            if (raw == null || raw.Count < 2)
                return Ng("線をつなげて断面にできなかった", null,
                    closed
                        ? "閉ループとして読むので、線が輪になっている必要がある。"
                        : "開いた折れ線として読むので、線が一本につながっている必要がある。");

            _profile = LineProfileExtractor.NormalizeToUnitSpan(raw);
            if (_profile == null || _profile.Count < 2)
                return Ng("断面がつぶれている（全点が同じ位置）", null, null);

            return Ok(
                $"「{name}」（索引 {_profileIndex}）から線 {lineFaces.Count} 本 → "
              + $"{(closed ? "ループ" : "折れ線")} {raw.Count} 点 → 正規化 {_profile.Count} 点",
                "図形生成パネル → 断面プロファイル欄 → 「取り込み(メッシュ→断面)」"
              + "（取り込み元はオブジェクト一覧で選択中のもの）",
                "断面座標は rung 長で正規化された系にある。描画オブジェクトから読んだ点列は"
              + "モデルのローカル座標そのままなので、そのままでは寸法が合わない。"
              + "長辺が 1 になるよう等方スケールし、AABB の最小角を原点へ寄せている"
              + "（LineProfileExtractor.NormalizeToUnitSpan）。Z は捨てて XY だけ使う。");
        }

        // ================================================================
        // 段 3b: Humanoid の割当
        // ================================================================

        private StageResult StageCheckHumanoid()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            // 割当の正本はモデルが持つ集中表現。MeshObject.HumanBodyBone を数えないこと
            // （ApplyHumanoidMappingCommand の受け口は model.HumanoidMapping.CopyFrom を
            //   呼ぶだけで per-bone 側へは書かない。PlayerCommandDispatcher.cs:3899-3919）。
            var mapping = model.HumanoidMapping;
            int mapped = mapping?.BoneIndexMap?.Count ?? 0;

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
                "必須が欠けたまま進むと VRM は人型として通らない。"
              + "読込時の自動割当は、ボーンが 1 本も無いモデルでは描画メッシュの名前を候補にする。");
        }

        // ================================================================
        // 段 4: 梯子と取り付け先
        // ================================================================

        private StageResult StageResolveLadder()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            string name = _ladderName.value.Trim();
            _ladderIndex = FindMeshByName(model, name);
            if (_ladderIndex < 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{name}」が見つからない",
                    "オブジェクトリストで名前を確かめる",
                    "梯子の取り込み元はこのオブジェクト。名前が違うと以後の段が全部立たない。");
            }

            var lc = model.GetMeshContext(_ladderIndex);
            if (lc?.MeshObject == null || lc.MeshObject.FaceCount == 0)
                return Ng($"「{name}」に面が無い", null,
                    "はしごは四角形が一列に並んだ帯。面が無いと検出できない。");

            _attachIndex = lc.HierarchyParentIndex;
            string attachName = (_attachIndex >= 0 && _attachIndex < model.MeshContextCount)
                ? model.GetMeshContext(_attachIndex)?.Name
                : "（モデル直下）";

            var attachCtx = _attachIndex >= 0 ? model.GetMeshContext(_attachIndex) : null;
            string attachKind = attachCtx == null ? "なし" : attachCtx.Type.ToString();

            // ミラーの状態を出しておく。この MQO の「アホ毛用はしご」は mirror 指定つき。
            // ミラー側は派生ジオメトリなので、取り込みや塗りの対象になるかどうかで
            // 結果が変わりうる。断定はせず、事実として記録だけ残す。
            string mirror = $"ミラー={(lc.IsMirrored ? $"あり（型 {lc.MirrorType}）" : "なし")}"
                          + $" / 焼き込みミラー={(lc.IsBakedMirror ? "あり" : "なし")}"
                          + $" / 派生ジオメトリ={(lc.MirrorGeometryDerived ? "はい" : "いいえ")}";

            return Ok(
                $"梯子=「{lc.Name}」（索引 {_ladderIndex} / 頂点 {lc.MeshObject.VertexCount} / "
              + $"面 {lc.MeshObject.FaceCount}）／取り付け先=「{attachName}」"
              + $"（索引 {_attachIndex} / 種別 {attachKind}）／{mirror}",
                "図形生成パネル → 揺れもの（はしご）→ 取り込み元を選び、"
              + "「取り付け先を取り込み元の親から決める」を入れる",
                "取り付け先は名前で探さず階層をそのまま読む。この時点では親はまだメッシュでよい。"
              + "スキンド化のとき、そのメッシュのボーン配下へ付け替わる。"
              + "ミラーの状態を並べているのは、フリル版の梯子（閉じた円環）と条件が違うため。"
              + "パイプが通らないときに、まずここの差を疑えるようにしてある。");
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
                BeltAcquireMethod.AutoLadder,
                SpringBoneLadderMode.Strand,
                _attachIndex,
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringAhoge" : _prefix.value.Trim(),
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
              + "（はしごを自動検索 / 髪系 / ウェイトを塗る=指定あり / グループとして残す）",
                "図形生成パネル → 揺れもの（はしご）→ 生成",
                "フリル版との差はここ 2 つ。取り込みが円環の自動検索ではなくはしごの自動検索"
              + "（開始タグ三角形が起点。BeltStackDetector）、並べ方がスカート系ではなく髪系"
              + "（はしご 1 本＝鎖 1 本）。"
              + "「塗る」を指定していても、取り込み元が MeshFilter 系のうちは塗られない。");
        }

        private StageResult StageVerifyBones()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            int bones = CountBones(model);
            if (bones <= _boneCountBefore)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"ボーンが増えないまま待ち切った（{bones} 本のまま）",
                    "図形生成パネル → 揺れもの（はしご）→ 取り込み元を選んで「はしごを自動検索」",
                    "はしごの自動検索は開始タグ三角形を起点にする（BeltStackDetector）。"
                  + "起点になる三角形（辺を他面と共有せず、共有する頂点がちょうど 1 個）が"
                  + "無いと 1 本も検出されず、鎖が作られない。");
            }

            _ladderGroup = FindGroupByAction(model, "placeSpringBoneLadderChains");
            if (string.IsNullOrEmpty(_ladderGroup))
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng("はしごのグループが出来ていない", null,
                    "「グループとして残す」を指定して送っている。作られないなら、"
                  + "生成そのものが途中で落ちている。");
            }

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
        /// placeSpringBoneLadderChains は揺れ方を付けないので、コマンドを直接並べる
        /// 経路では自分で送る必要がある（PanelCommand.SpringBoneLadder.cs:30）。
        /// </summary>
        private StageResult StageApplySpring()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            string prefix = string.IsNullOrWhiteSpace(_prefix.value) ? "SpringAhoge" : _prefix.value.Trim();

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

            // 段ごとに同じ値を入れる。根元→先へ変化させたいならパネル側の taper を使うこと。
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
        // 段 8-10: パイプ
        // ================================================================

        private StageResult StageCreatePipe()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            _meshCountBefore = model.MeshContextCount;

            // 梯子の点列をここで取り込む。
            // 生成そのものは点列を使う。取り込み方（acquireMethod）は作り直し用の控えで、
            // 初回の生成では読まれない（PanelCommand.cs:5616）。空で送ると 0 頂点の
            // パイプが出来て、次の段が待ち続ける。
            var src = model.GetMeshContext(_ladderIndex);

            var acquired = BeltAcquire.Acquire(src, BeltAcquireMethod.AutoLadder, false, "");
            if (!acquired.Ok || acquired.Belts.Count == 0)
                return Ng($"はしごを取り込めない（{acquired.Message}）",
                    "図形生成パネル → パイプ → 取り込み元を選び「はしごを自動検索」→ 取り込み",
                    "はしごの自動検索は開始タグ三角形が起点。縦走査は四角形の対辺を辿り、"
                  + "終了三角形に当たって止まる（BeltStackDetector）。"
                  + "取れないなら、四角形が一列に並んでいるか、起点の三角形があるかを疑う。");

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var beltLeft, out var beltRight, out var beltStarts,
                out var beltClosed, out var beltFlip, out var beltHeight);

            var pp = PipeParams.Default;
            pp.MeshName           = PipeName;
            pp.CapEnds            = _capEnds.value;
            pp.Thickness          = Mathf.Clamp(_thickness.value,
                                                PipeParams.ThicknessMin, PipeParams.ThicknessMax);
            pp.InheritBeltWeights = true;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;

            SendCommand(new CreatePipeCommand(
                ModelIndex,
                pp,
                _profile.ToArray(), _profileClosed.value,
                beltLeft, beltRight, beltStarts,
                beltClosed, beltFlip, beltHeight,
                BeltOrientOptions.Default,
                BeltSplineOptions.Default,
                pl,
                beltSourceIndex: _ladderIndex,
                acquireMethod: BeltAcquireMethod.AutoLadder,
                acquireCrossRows: false));

            return Ok(
                $"CreatePipeCommand を送った（梯子={_ladderName.value} / 取り込んだはしご {acquired.Belts.Count} 本 / "
              + $"断面 {_profile.Count} 点（{(_profileClosed.value ? "閉ループ" : "折れ線")}）/ "
              + $"蓋={(_capEnds.value ? "張る" : "張らない")} / 厚み {pp.Thickness:0.###} / "
              + "はしごのウェイトを引き継ぐ=する / グループとして残す）",
                "図形生成パネル → パイプ → 取り込み元を選び「はしごを自動検索」→ 断面を取り込む "
              + "→ 「はしごのウェイトを引き継ぐ」を入れて生成",
                "パイプははしご 1 本ごとに作って MeshObjectAppendOps.Append で連結する"
              + "（PrimitiveMeshFactory.cs:357-372）。フリルは ConnectShared のとき 1 メッシュに"
              + "まとめるので Append を通らない。ここが両者の分かれ目。"
              + "引き継ぎはこの段では空振りする（はしごにまだウェイトが無い）。"
              + "スキンド化のあとに自動更新で入る。");
        }

        private StageResult StageVerifyPipe()
        {
            var err = FailedByConsole(); if (err.HasValue) return err.Value;

            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Retry;

            if (model.MeshContextCount <= _meshCountBefore)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng("パイプのオブジェクトが増えないまま待ち切った",
                    "図形生成パネル → パイプ → 生成",
                    "生成が落ちている。断面の点数・はしごの取り込み・置き方（新規オブジェクト）を疑う。");
            }

            _pipeIndex = FindMeshByName(model, PipeName);
            if (_pipeIndex < 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{PipeName}」が見つからない", null,
                    "名前は PipeParams.MeshName で指定している。別名になっているなら"
                  + "置き方（AddMode）が新規オブジェクトになっていない。");
            }

            var mo = model.GetMeshContext(_pipeIndex)?.MeshObject;
            if (mo == null || mo.FaceCount == 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{PipeName}」に面が無い（頂点 {mo?.VertexCount ?? 0}）", null,
                    "0 頂点 0 面のパイプが出来ている。送った点列が空だったということ。"
                  + "段 8 で取り込んだはしごの本数と、断面の点数を見ること。");
            }

            _pipeGroup = FindGroupByAction(model, "createPipe");
            if (string.IsNullOrEmpty(_pipeGroup))
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng("パイプのグループが出来ていない", null,
                    "「グループとして残す」を指定して送っている。グループが無いと"
                  + "スキンド化のあとの塗り直しが流れない。");
            }

            _meshCountBefore = model.MeshContextCount;

            return Ok(
                $"「{PipeName}」が出来た（索引 {_pipeIndex} / 頂点 {mo.VertexCount} / 面 {mo.FaceCount}）／"
              + $"グループ「{_pipeGroup}」が出来た",
                null,
                "パイプは別メッシュなので、はしごへ塗ったウェイトは自動では乗らない。"
              + "生成時に位置で突き合わせて引き継ぐ仕組み（BeltWeightBinder）に乗せてある。");
        }

        private StageResult StageArmPipeGroup()
        {
            SendCommand(new SetObjectGroupAutoUpdateCommand(ModelIndex, _pipeGroup, true));

            return Ok(
                $"グループ「{_pipeGroup}」の自動更新を立てた",
                "オブジェクトグループパネル → グループを選ぶ → 自動更新を入れる",
                "スキンド化のあと、はしごのグループが塗ったウェイトを、"
              + "このグループが作り直しで引き継ぐ。並びは ObjectGroups の順なので、"
              + "はしご→パイプの順に流れる。");
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
                    "変換コマンドが実行されていない。ディスパッチャの Fail はコマンドの結果へ"
                  + "書くだけでコンソールへ出さない（PlayerCommandDispatcher.cs:523）。");
            }

            // 変換でメッシュ名に接尾辞が付くことがある（MeshNameSuffix）ので、両方で探す。
            _ladderIndex = FindByNameOrSkinned(model, _ladderName.value.Trim());
            _pipeIndex   = FindByNameOrSkinned(model, PipeName);

            if (_ladderIndex < 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{_ladderName.value}」を引き直せない。" + DumpModel(model),
                    "オブジェクトリストで梯子の名前を確かめる",
                    "元の名前と接尾辞つきの両方で探している。どちらでも無いなら、"
                  + "別の名前へ変わっているか、消えている。");
            }

            var lc = model.GetMeshContext(_ladderIndex);
            if (lc == null || !lc.IsSkinned)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"「{lc?.Name}」がスキンド系にならない。" + DumpModel(model), null,
                    "変換の対象から外れている。CollectMeshEntries は Type が Mesh か"
                  + "ミラー側のものだけを拾う（MeshFilterToSkinnedConverter.cs:105）。");
            }

            int rootIndex = FindFirstBoneByPrefix(model,
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringAhoge" : _prefix.value.Trim());

            string parentKind = "（鎖が見つからない）";
            if (rootIndex >= 0)
            {
                int p = model.GetMeshContext(rootIndex).HierarchyParentIndex;
                parentKind = (p >= 0 && p < model.MeshContextCount)
                    ? $"{model.GetMeshContext(p)?.Name}（{model.GetMeshContext(p)?.Type}）"
                    : "モデル直下";

                var pc = (p >= 0 && p < model.MeshContextCount) ? model.GetMeshContext(p) : null;
                if (pc != null && pc.Type != MeshType.Bone)
                    return Ng($"鎖の親がボーンになっていない（{pc.Name} / {pc.Type}）", null,
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
                string.IsNullOrWhiteSpace(_prefix.value) ? "SpringAhoge" : _prefix.value.Trim());

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
                  + "鎖のボーンへ塗り替わる。自動更新が立っているか・要更新か・"
                  + "ソースを引けているかを上の並びで見ること。");
            }

            var pm = _pipeIndex >= 0 ? model.GetMeshContext(_pipeIndex)?.MeshObject : null;
            int pipeOnChain = CountWeightedTo(pm, chainBones);
            int pipeVerts   = pm?.VertexCount ?? 0;

            if (pm == null)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;
                return Ng($"パイプ（{PipeName}）を引き直せない。" + DumpModel(model), null,
                    "スキンド化で名前に接尾辞が付いたか、消えている。");
            }

            // ここが本題。パイプの頂点が 1 つも鎖を指していなければ、
            // 引き継ぎか連結のどちらかで落ちている。フリルとの差は連結（Append）。
            if (pipeOnChain == 0)
            {
                if (Waited() < StageWaitLimit) return StageResult.Retry;

                return Ng(
                    $"パイプの頂点が 1 つも鎖のボーンを指していない"
                  + $"（パイプの頂点 {pipeVerts} / 鎖のボーン {chainBones.Count} / "
                  + $"はしごは {ladderOnChain} 頂点が鎖を指している）" + DumpGroups(),
                    "オブジェクトグループパネルで、パイプのグループが「要更新」でなくなっているかを見る",
                    "はしご側は塗れているので、鎖もウェイトも出来ている。落ちているのは"
                  + "パイプ側の引き継ぎ。疑う順は 3 つ。"
                  + "(1) 生成時の位置の突き合わせ（BeltWeightBinder）が引けているか。"
                  + "(2) はしご 1 本ごとの部品を連結する MeshObjectAppendOps.Append が"
                  + "BoneWeight / MirrorBoneWeight を写しているか。"
                  + "(3) 厚み付け・蓋の頂点が複製元のウェイトを受け取っているか"
                  + "（FaceGroupSolidifier.CloneShellVertex / CloneBandVertex）。");
            }

            return Ok(
                $"はしごの {ladderOnChain} 頂点が鎖のボーンを指している"
              + $"（鎖のボーン {chainBones.Count} 本）／"
              + $"パイプの {pipeOnChain}/{pipeVerts} 頂点が鎖のボーンを指している",
                "オブジェクトグループパネルで、両方のグループが「要更新」でなくなっているのを確かめる",
                "間引きなし（段・本とも 1）では、はしごの頂点は 1 ボーン 1.0 のままになる。"
              + "だから『2 本以上のボーンを持つか』では見られない。"
              + "『鎖のボーンを指しているか』で見るのが正しい。"
              + "パイプは線分の両端のウェイトを断面 x で混ぜて受け取る。"
              + "分母（パイプの全頂点）に届いていないときは、蓋や厚みの頂点が"
              + "引き継げていない可能性がある。");
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
        ///   ここで隠すと、はしごは VRM に入らない。パイプだけが残る。
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
              + "はしごは形を決めるための下地なので、隠せば VRM にはパイプだけが入る。"
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
              + "書き出した VRM の JOINTS_0 / WEIGHTS_0 を読み、鎖のボーンを指す頂点数を"
              + "数えるのが最終確認になる。");
        }

        // ================================================================
        // 小物（フリル版と同じ実装）
        // ================================================================

        /// <summary>待ち切れなかったときに出すモデルの状態。</summary>
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

            string name     = _ladderName.value.Trim();
            string suffix   = Poly_Ling.Ops.MeshFilterToSkinnedConverter.MeshNameSuffix;
            string suffixed = name + suffix;

            return $"［全 {model.MeshContextCount} / メッシュ {meshes}（うちスキンド {skinned}）"
                 + $" / ボーン {bones}（変換前 {_boneCountBefore}）/ その他 {others}］"
                 + $"メッシュ「{name}」={FindMeshByName(model, name)}"
                 + $" メッシュ「{suffixed}」={FindMeshByName(model, suffixed)}"
                 + $" メッシュ「{PipeName}」={FindMeshByName(model, PipeName)}"
                 + $" メッシュ「{PipeName}{suffix}」={FindMeshByName(model, PipeName + suffix)}";
        }

        /// <summary>
        /// 名前で描画メッシュを引く。見つからなければ「名前＋スキンド化の接尾辞」でも引く。
        /// ボーンを拾わないこと（ボーンはメッシュと同名で作られ、先頭へ挿される）。
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

        /// <summary>オブジェクトグループの実態を並べる。自動更新が流れなかったときの判定材料。</summary>
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

        /// <summary>指定したボーンの集合を指している頂点の数。重みが 0 のスロットは数えない。</summary>
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
    }
}
