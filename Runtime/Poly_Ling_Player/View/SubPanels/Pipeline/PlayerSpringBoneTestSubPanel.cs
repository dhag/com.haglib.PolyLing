// PlayerSpringBoneTestSubPanel.cs
// スプリングボーン検証。ボタン 1 回で
//   PMX 読込 → ダミー揺れもの生成 → Humanoid 自動割当 → T ポーズ化
//   → 揺れデータの検査とレポート書出 → VRM 書出
// までを流す。段ランナーと 3 行ログは PlayerStagedTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【何を確かめるための検証か】
//   揺れデータ（SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders）が
//   モデルに載り、それが VRM の VRMC_springBone として出るところまでを機械的に見る。
//   載せる操作そのものは「ボーン・モーフ →『揺れもの編集』」でも手でできる。
//   ここでは検証用のダミー装備を一括生成して、出力までの経路だけを通す。
//
// 【なぜ実コマンドを送るか】
//   PlayerStagedTestSubPanelBase.cs 冒頭と同じ理由。Ops を直接叩くと
//   ディスパッチャ側の欠陥が検査を素通りする。パネルが押されたときに
//   飛ぶのと同じ PanelCommand を送る。
//
// 【検証専用の型を使わない】
//   以前は形状の選択に SpringBoneTestRigShape を、既定値に
//   SpringBoneTestRigParams を使っていた。どちらも
//   BuildSpringBoneTestRigCommand からしか届かない型で、そのコマンドを
//   送るパネルは 1 つも無い。つまり人が触れない経路の値を検証に
//   使っていたことになるので、この画面の中だけで完結する形へ直した。
//   形状はこのファイルの RigShape、既定値はこのファイルの const が正典。
//
// 【段の間隔】
//   PMX 読込はクラウドストレージ上のファイルだと実体化から始まるため、
//   MQO を読む他の検証より 1 段あたりの待ちを長く取る。待ち直しの上限
//   （MaxRetry）は移行前の値をそのまま引き継ぐ。
//
// 【パスの既定値】
//   インポート／エクスポートのパネルと同じ RecentPaths のキーを読む。
//     Import.PMX.Path / Export.VRM.Path
//   同じキーへ書き戻すので、通常のパネルと履歴を共有する。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;          // RecentPaths
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    /// <summary>揺れもの検証。人の操作は形状を選んで「実行」を押すだけ。</summary>
    public class PlayerSpringBoneTestSubPanel : PlayerStagedTestSubPanelBase
    {
        // ================================================================
        // 生成する装備の形（この画面だけの選択肢）
        // ================================================================

        /// <summary>検証で作るダミー装備の形。</summary>
        public enum RigShape
        {
            /// <summary>腰まわりに放射状の鎖と、脚のカプセル 2 本。</summary>
            Skirt = 0,
            /// <summary>頭の後ろに鎖 1 本と、頭の球 1 個。</summary>
            Ponytail = 1,
        }

        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;

        /// <summary>PMX を読み込む。実際の import 経路（ImportPmxCommand）へ流す。</summary>
        public Action<string> ImportPmx;

        /// <summary>VRM を書き出す。エクスポートパネルと同じ経路へ流す。</summary>
        public Func<string, Poly_Ling.Vrm.Vrm10ExportSettings, Poly_Ling.Vrm.Vrm10ExportResult> ExportVrm;

        // ================================================================
        // RecentPaths のキー（IO パネルと共有する）
        // ================================================================

        private const string PmxPathKey = "Import.PMX.Path";
        private const string VrmPathKey = "Export.VRM.Path";

        // ================================================================
        // VRM(UniVRM) のジョイント既定値
        // ================================================================
        //
        // VRM10SpringBoneJoint のフィールド初期値。調整済みの値が無い形状は
        // ここから始める。PolyLing の SpringBoneJointData の既定と同じ値。

        private const float VrmDefaultStiffness    = 1.0f;
        private const float VrmDefaultDrag         = 0.4f;
        private const float VrmDefaultGravityPower = 0f;
        private const float VrmDefaultHitRadius    = 0.02f;

        // ================================================================
        // 生成する装備の既定値
        // ================================================================
        //
        // 値は、以前まで使っていた SpringBoneTestRigParams の初期値をそのまま
        // 引き写したもの。あちらは人が触れない経路の型なので参照をやめ、
        // この画面の中に置いた。数値を変えたときの影響はこの画面だけに閉じる。

        private const string DefaultRigPrefix = "SBTest";

        private const int   DefaultStrands           = 12;
        private const int   DefaultSegmentsPerStrand = 5;
        private const bool  DefaultAutoSkirtHeight   = true;
        private const float DefaultSkirtLift         = 0f;

        private const int   DefaultPonytailSegments  = 6;
        private const float DefaultPonytailBack      = 0.50f;

        // スカートの揺れ方。末端のかたさ 8 は VRM の通常の目安（0〜4）の外側で、
        // そう決めた根拠は残っていない。引き写しであることを明記しておく。
        private const float DefaultSkirtStiffnessTop = 1.0f;
        private const float DefaultSkirtStiffnessTip = 8.0f;
        private const float DefaultSkirtDrag         = 0.15f;
        private const float DefaultSkirtGravityPower = 0.15f;
        private const float DefaultSkirtHitRadius    = 0.04f;

        // ================================================================
        // 段の刻み
        // ================================================================

        /// <summary>
        /// 段の間隔。既定（120ms）では PMX 読込の完了を待ち切れない。
        /// クラウドストレージ上のファイルは、開く前にローカルへ実体化される。
        /// </summary>
        protected override long StageIntervalMs => 1200;

        /// <summary>
        /// 待ち直しの上限。移行前の値（100）をそのまま引き継ぐ。
        /// 1200ms × 100 ＝ 120 秒。
        /// </summary>
        protected override int MaxRetry => 100;

        // ================================================================
        // UI
        // ================================================================

        private TextField    _pmxPathField;
        private TextField    _vrmPathField;
        private Toggle       _doImport;
        private Toggle       _doExport;
        private EnumField    _shapeField;
        private Toggle       _applyMapping;
        private Toggle       _applyTPose;

        // 揺れ方の値は形状ごとに別に持つ。
        //   スカートは脚に当てながら重く垂らす、ポニーテールは頭の後ろで軽く振れる、と
        //   要る値が違う。1 組しか無いと形状を切り替えるたびに入れ直しになる。
        private FloatField _skStiffTop, _skStiffTip, _skDrag, _skGravity, _skHitRadius;
        private FloatField _ptStiffTop, _ptStiffTip, _ptDrag, _ptGravity, _ptHitRadius;
        private Toggle       _autoSkirtHeight;
        private FloatField   _skirtLift, _ponytailBack;
        private IntegerField _strands, _segments, _ponytailSegments;

        /// <summary>
        /// 形状ごとの入力欄のまとまり。選んだ形状の側だけを出す。
        /// 両方を常に出していたため、Skirt でポニテの欄が、Ponytail で
        /// スカートの欄が並び、どれが効くのか判らない状態になっていた。
        /// </summary>
        private VisualElement _skirtParams, _ponytailParams;

        // ================================================================
        // 実行中に持ち回る値
        // ================================================================

        /// <summary>読込前のメッシュ数。読込完了の判定に使う。</summary>
        private int _meshCountBeforeImport;

        private string _reportPath = "";

        private int _chains, _joints, _colliders, _groups, _mapped;
        private Poly_Ling.Vrm.Vrm10ExportResult _exportResult;

        // ================================================================
        // 基底が要求するもの
        // ================================================================

        protected override string TitleText => "スプリングボーン検証";

        protected override string NoteText =>
            "PMX 読込 → ダミー揺れもの生成 → Humanoid 自動割当 → T ポーズ化\n"
          + "→ 揺れデータの検査とレポート書出 → VRM 書出 を通しで流します。\n"
          + "揺れデータを手で付けるときは「ボーン・モーフ →『揺れもの編集』」を使います。\n"
          + "段ごとに、手でやるならどこを押すかと、その段が要る理由をログに出します。";

        protected override void BuildOptionsUI(VisualElement root)
        {
            // ── 入出力 ────────────────────────────────────────────────
            root.Add(Sec("入出力"));

            _doImport = new Toggle("PMX を読み込む") { value = true };
            root.Add(_doImport);

            _pmxPathField = new TextField("PMX パス");
            _pmxPathField.style.fontSize = 10;
            _pmxPathField.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _pmxPathField.RegisterValueChangedCallback(e => SafeSet(PmxPathKey, e.newValue));
            root.Add(_pmxPathField);

            root.Add(Hint(
                "読み込みを外すと、いま開いているモデルへそのまま揺れものを足します。"
              + "クラウドストレージ上のファイルは実体化に時間が掛かるため、段の間隔を長めに取っています。"));

            _doExport = new Toggle("VRM を書き出す") { value = true };
            root.Add(_doExport);

            _vrmPathField = new TextField("VRM パス");
            _vrmPathField.style.fontSize = 10;
            _vrmPathField.SetValueWithoutNotify(SafeGet(VrmPathKey));
            _vrmPathField.RegisterValueChangedCallback(e => SafeSet(VrmPathKey, e.newValue));
            root.Add(_vrmPathField);

            // ── 形状 ──────────────────────────────────────────────────
            root.Add(Sec("生成する装備"));

            _shapeField = new EnumField("形状", RigShape.Skirt);
            root.Add(_shapeField);
            root.Add(Hint(
                "Skirt は腰まわりに放射状のチェーンと脚カプセル 2 本、"
              + "Ponytail は頭の後ろに 1 本のチェーンと球コライダー 1 個を作ります。"));

            var fo = new Foldout { text = "パラメータ", value = false };

            // ── 形状ごとの欄（選んだ側だけ出す）────────────────────
            _skirtParams = new VisualElement();
            _strands  = I("鎖の本数",         DefaultStrands);
            _segments = I("1 本あたりの段数", DefaultSegmentsPerStrand);
            _autoSkirtHeight = new Toggle("腰高さを股関節に合わせる") { value = DefaultAutoSkirtHeight };
            _skirtLift = F("腰高さの補正[m]", DefaultSkirtLift);
            _skirtParams.Add(Sec("スカートの形"));
            _skirtParams.Add(_strands);
            _skirtParams.Add(_segments);
            _skirtParams.Add(_autoSkirtHeight);
            _skirtParams.Add(_skirtLift);
            _skirtParams.Add(Hint(
                "腰のまわりに「チェーンの本数」だけ鎖を放射状に生やし、"
              + "1 本を「1 本あたりの段数」に分けます。\n"
              + "PMX の「センター」は腰の高さにあるとは限らず、ひざより下のこともあるため、"
              + "既定では股関節の高さに合わせます。"));
            fo.Add(_skirtParams);

            _ponytailParams = new VisualElement();
            _ponytailSegments = I("段数",              DefaultPonytailSegments);
            _ponytailBack     = F("頭から後ろへ[m]",   DefaultPonytailBack);
            _ponytailParams.Add(Sec("ポニーテールの形"));
            _ponytailParams.Add(_ponytailSegments);
            _ponytailParams.Add(_ponytailBack);
            _ponytailParams.Add(Hint(
                "頭の後ろに鎖を 1 本だけ生やします。本数の指定はありません。\n"
              + "PMX のモデルは Z マイナス方向を向くので、後ろは +Z です。"
              + "頭に密着させると髪と重なって見えないため、既定で 0.5m 離しています。"
              + "頭の球コライダーも同じだけ後ろへずれます。"));
            fo.Add(_ponytailParams);

            // ── 揺れ方（形状ごとに別の値）──────────────────────────
            //
            // スカート側の既定は、これまでコードに入っていた値をそのまま使う。
            // ポニーテール側は調整済みの値が無いので VRM(UniVRM) の既定から始める。
            _skirtParams.Add(Sec("スカートの揺れ方"));
            _skStiffTop   = F("かたさ 根元",  DefaultSkirtStiffnessTop);
            _skStiffTip   = F("かたさ 末端",  DefaultSkirtStiffnessTip);
            _skDrag       = F("減衰",         DefaultSkirtDrag);
            _skGravity    = F("重力の強さ",   DefaultSkirtGravityPower);
            _skHitRadius  = F("当たり半径",   DefaultSkirtHitRadius);
            _skirtParams.Add(_skStiffTop);
            _skirtParams.Add(_skStiffTip);
            _skirtParams.Add(_skDrag);
            _skirtParams.Add(_skGravity);
            _skirtParams.Add(_skHitRadius);
            _skirtParams.Add(Hint(
                "この 5 つは、これまでコードに入っていた値をそのまま引き写したものです。"
              + "末端のかたさ 8 は VRM の通常の目安（0〜4）の外側で、"
              + "そう決めた根拠はコードに残っていません。"));

            _ponytailParams.Add(Sec("ポニーテールの揺れ方"));
            _ptStiffTop  = F("かたさ 根元",  VrmDefaultStiffness);
            _ptStiffTip  = F("かたさ 末端",  VrmDefaultStiffness);
            _ptDrag      = F("減衰",         VrmDefaultDrag);
            _ptGravity   = F("重力の強さ",   VrmDefaultGravityPower);
            _ptHitRadius = F("当たり半径",   VrmDefaultHitRadius);
            _ponytailParams.Add(_ptStiffTop);
            _ponytailParams.Add(_ptStiffTip);
            _ponytailParams.Add(_ptDrag);
            _ponytailParams.Add(_ptGravity);
            _ponytailParams.Add(_ptHitRadius);
            _ponytailParams.Add(Hint(
                "ポニーテール向けに調整した値は無いので、VRM(UniVRM) の既定から始めます。"
              + "鎖は最初から下を向いているので、重力を 0 にしても垂れたままです。"));

            // ── 4 つの値の意味（形状によらず同じ）────────────────────
            fo.Add(Sec("値の意味"));
            fo.Add(Hint(
                "かたさ＝初期姿勢へ戻そうとする速さ。大きいほど硬く、小さいほどふにゃふにゃ。"
              + "根元から末端へ線形に変化させます。VRM の通常の目安は 0〜4。"));
            fo.Add(Hint(
                "減衰＝前フレームの移動をどれだけ捨てるか。空気抵抗ではありません。"
              + "0 で捨てず揺れ続け、1 で慣性を捨てます。範囲は 0〜1。VRM の既定は 0.4。"));
            fo.Add(Hint(
                "重力の強さ＝重力の向きへ足す量。加速度[m/s²]ではないので 9.8 ではありません。"
              + "VRM の既定は 0、通常の目安は 0〜2。垂れないときだけ少しずつ上げます。"));
            fo.Add(Hint(
                "当たり半径＝ジョイント側の球の半径。コライダーの半径に足して衝突を見ます。"
              + "太いほど体から離れて早く止まります。VRM の既定は 0.02、通常の目安は 0〜0.5。"));

            root.Add(fo);

            // 形状に合わせて出し分ける。
            _shapeField.RegisterValueChangedCallback(_ => SyncShapeParams());
            SyncShapeParams();

            // ── 仕上げ ────────────────────────────────────────────────
            root.Add(Sec("仕上げ"));

            _applyMapping = new Toggle("Humanoid 自動割当を実行") { value = true };
            root.Add(_applyMapping);

            _applyTPose = new Toggle("T ポーズ化を実行") { value = true };
            root.Add(_applyTPose);
        }

        /// <summary>選んだ形状で実際に読まれる欄だけを出す。</summary>
        private void SyncShapeParams()
        {
            if (_shapeField == null) return;

            bool skirt = (RigShape)_shapeField.value == RigShape.Skirt;

            if (_skirtParams != null)
                _skirtParams.style.display = skirt ? DisplayStyle.Flex : DisplayStyle.None;
            if (_ponytailParams != null)
                _ponytailParams.style.display = skirt ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public override void Refresh()
        {
            base.Refresh();
            if (IsRunning) return;

            _pmxPathField?.SetValueWithoutNotify(SafeGet(PmxPathKey));
            _vrmPathField?.SetValueWithoutNotify(SafeGet(VrmPathKey));
        }

        protected override bool CanRun()
        {
            if (SendCommand == null || GetModel == null)
            { SetStatus("配線が足りません（SendCommand / GetModel）。"); return false; }

            if (_doImport.value)
            {
                if (ImportPmx == null) { SetStatus("配線が足りません（ImportPmx）。"); return false; }

                string path = _pmxPathField.value;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                { SetStatus("PMX パスが正しくありません。"); return false; }
            }
            else if (GetModel() == null)
            {
                SetStatus("モデルが読み込まれていません。");
                return false;
            }

            if (_doExport.value)
            {
                if (ExportVrm == null) { SetStatus("配線が足りません（ExportVrm）。"); return false; }
                if (string.IsNullOrEmpty(_vrmPathField.value))
                { SetStatus("VRM パスが空です。"); return false; }
            }

            return true;
        }

        protected override void ResetRunState()
        {
            _meshCountBeforeImport = GetModel()?.MeshContextCount ?? -1;

            _chains = _joints = _colliders = _groups = _mapped = 0;
            _exportResult = null;

            _reportPath = Path.Combine(
                Application.persistentDataPath, "PolyLing", "SpringBoneTest",
                DateTime.Now.ToString("yyyyMMdd_HHmmss"), "report.txt");
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            if (_doImport.value)
            {
                stages.Add(("1. PMX を読み込む",       StageImportPmx));
                stages.Add(("2. 読み込みの完了を待つ", StageWaitImport));
            }

            // 取り付け先（腰・頭）を Humanoid の割当から引くので、段 3 より前に置く。
            if (_applyMapping.value) stages.Add(("3-0. Humanoid を自動割当する", StageAutoMap));

            // 前回の生成物を先に消す。消さないと実行のたびに積み上がり、
            // 接頭辞で引き直す CollectByPrefix が前回の鎖まで拾う。
            stages.Add(("3-00. 前回の生成物を消す", StageRigCleanup));

            stages.Add(("3-1. マテリアルとメッシュを作る",         StageRigMaterialAndMesh));
            stages.Add(("3-2. 鎖になるボーンを作る",               StageRigBones));
            stages.Add(("3-3. 鎖のボーンに揺れ方を入れる",         StageRigJoints));
            stages.Add(("3-4. 鎖を既存のボーンにつなぐ",           StageRigAttach));
            stages.Add(("3-5. 鎖ごとの設定を入れる",               StageRigChainRoots));
            stages.Add(("3-6. 当たり判定を作る",                   StageRigColliders));
            stages.Add(("3-7. ウェイトを塗る",                     StageRigWeights));

            if (_applyTPose.value)   stages.Add(("4. T ポーズ化する",          StageTPose));

            stages.Add(("5. 揺れデータを検査してレポートを書く", StageVerify));

            if (_doExport.value) stages.Add(("6. VRM を書き出す", StageExportVrm));
        }

        protected override void OnFinished(bool aborted)
        {
            if (aborted) WriteReport("段の途中で停止した");
        }

        // ================================================================
        // 各段
        // ================================================================

        private StageResult StageImportPmx()
        {
            var model = GetModel();
            _meshCountBeforeImport = model?.MeshContextCount ?? -1;

            ImportPmx(_pmxPathField.value);

            return Ok(
                $"「{Path.GetFileName(_pmxPathField.value)}」の読み込みを要求した",
                "ファイル →「PMX 読込」",
                "以降の段は、このモデルのボーンへ揺れものを足していく。"
              + "読み込みは非同期なので、この段では要求を出すだけで完了を待たない。");
        }

        /// <summary>読み込みの完了を待つ。件数が増えるまで同じ段を繰り返す。</summary>
        private StageResult StageWaitImport()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;
            if (model.MeshContextCount == _meshCountBeforeImport) return StageResult.Retry;

            return Ok(
                $"モデル「{model.Name}」に MeshContext が {model.MeshContextCount} 件できた",
                null,
                "読み込みはファイルの大きさで時間が変わる。件数が増えるまで待ち直している。"
              + "クラウドストレージ上のファイルは、開く前にローカルへ実体化される分だけ余計に掛かる。"
              + "ここで止まるなら、そもそも import が走っていない。");
        }

        // ================================================================
        // 3-1 〜 3-7 揺れものの装備を作る
        // ================================================================
        //
        // 【なぜ小さく分けるか】
        //   1 つのコマンドで全部作ると、人が同じことを手でやる手順が出てこない。
        //   人がパネルから押せるコマンドだけを、押す順に並べる。
        //
        // 【使っているコマンド】
        //   3-1 CreateCylinderCommand / AddMaterialSlotCommand / SetMaterialColorCommand
        //   3-2 PlaceSpringBoneChainsCommand
        //   3-3 SetSpringBoneJointCommand
        //   3-4 SetBoneParentCommand
        //   3-5 AddSpringBoneColliderGroupCommand / SetSpringBoneChainRootCommand
        //   3-6 AddSpringBoneColliderCommand
        //   3-7 ConvertToSkinnedCommand / SelectElementsCommand / SetSkinWeightNumericCommand
        //   すべてパネルから押せるもの。専用の一括生成は使わない。

        /// <summary>装備の名前の接頭辞。</summary>
        private string RigPrefix => DefaultRigPrefix;

        /// <summary>この実行で作った鎖。3-2 で作り、以降の段が使う。</summary>
        private List<List<int>> _rigChains = new List<List<int>>();

        /// <summary>3-1 で作ったメッシュの masterIndex。</summary>
        private int _rigMeshIndex = -1;

        /// <summary>3-4 の取り付け先ボーン。</summary>
        private int _rigAttachIndex = -1;

        /// <summary>3-5 で作った当たり判定のまとまりの索引。</summary>
        private int _rigGroupIndex = -1;

        private bool RigIsSkirt => (RigShape)_shapeField.value == RigShape.Skirt;

        // ── 3-1 ──────────────────────────────────────────────────────
        private StageResult StageRigMaterialAndMesh()
        {
            var model = GetModel();
            if (model == null)
                return Ng("モデルがありません", null, "前の段の読み込みが終わっていない。");

            _rigChains.Clear();
            _rigMeshIndex = -1;
            _rigGroupIndex = -1;

            _rigAttachIndex = FindRigAttachBone(model);
            if (_rigAttachIndex < 0)
                return Ng(RigIsSkirt ? "腰にあたるボーンが見つかりません" : "頭にあたるボーンが見つかりません",
                    "アバター用ヒューマンマッピングで Hips / Head を割り当てる",
                    "鎖の取り付け先が決まらないと、どこから垂らすかが決まらない。");

            Vector3 at = BoneWorld(model, _rigAttachIndex);

            // 六角柱にする。裏から見たときに面の向きが分かるように、
            // 円周の分割を 6 にして稜線を出す。
            var cyl = CylinderMeshGenerator.CylinderParams.Default;
            cyl.MeshName       = RigPrefix + (RigIsSkirt ? "_SkirtMesh" : "_TailMesh");
            cyl.RadialSegments = 6;
            cyl.HeightSegments = Mathf.Max(1, _segments.value);
            cyl.CapTop         = false;
            cyl.CapBottom      = false;
            // ApplyPivotOffset は 頂点 -= pivot × サイズ。
            // 円柱は中心が原点なので、+0.5 で 0.5×高さ ぶん下がり、上端が原点になる。
            // ボーンも取り付け先から下へ伸ばすので、これで両者がそろう。
            cyl.Pivot          = new Vector3(0f, 0.5f, 0f);

            if (RigIsSkirt)
            {
                cyl.RadiusTop    = 0.12f;
                cyl.RadiusBottom = 0.45f;
                cyl.Height       = 0.60f;
            }
            else
            {
                cyl.RadiusTop    = 0.06f;
                cyl.RadiusBottom = 0.06f;
                cyl.Height       = 0.12f * Mathf.Max(1, _ponytailSegments.value);
            }

            var placement = new PrimitivePlacement
            {
                WorldPosition          = RigOrigin(model),
                PlaceRotation          = Vector3.zero,
                PlaceScale             = Vector3.one,
                BakeRotation           = true,
                BakeScale              = true,
                AddMode                = PrimitiveAddMode.NewObject,
                AddTargetIndex         = -1,
                MaterialIndex          = -1,
                MergeDuplicateVertices = true,
            };

            int before = model.MeshContextCount;
            SendCommand(new CreateCylinderCommand(GetModelIndex?.Invoke() ?? 0, cyl, placement));

            var m2 = GetModel();
            if (m2 == null || m2.MeshContextCount == before) return StageResult.Retry;

            _rigMeshIndex = FindByName(m2, cyl.MeshName);
            if (_rigMeshIndex < 0) return StageResult.Retry;

            // 専用マテリアルを足して色を付ける。既存のマテリアルを流用すると、
            // それが切り抜き付きのテクスチャだった場合に帯が丸ごと消えることがある。
            SendCommand(new AddMaterialSlotCommand(GetModelIndex?.Invoke() ?? 0));

            // 足したスロットは末尾に付く。色は RGBA の float 配列で渡す。
            int slot = (GetModel()?.MaterialCount ?? 1) - 1;
            if (slot < 0) slot = 0;
            SendCommand(new SetMaterialColorCommand(
                GetModelIndex?.Invoke() ?? 0, slot, new[] { 0.85f, 0.35f, 0.55f, 1f }));

            return Ok(
                $"六角柱メッシュ「{cyl.MeshName}」を作り、マテリアルスロット {slot} を足した",
                "図形生成 →「基本図形（3D連携）」→ 円柱 → 円周の分割を 6 にして生成。"
              + "そのあとマテリアルパネルでスロットを足して色を付ける",
                "円周を 6 にすると稜線が出るので、裏から見たときに面の向きが分かる。"
              + "上下の蓋は要らないので外している。");
        }

        // ── 3-2 ──────────────────────────────────────────────────────
        private StageResult StageRigBones()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルがありません", null, "前の段が走っていない。");

            int chains = RigIsSkirt ? Mathf.Max(1, _strands.value) : 1;
            int segs   = RigIsSkirt ? Mathf.Max(1, _segments.value)
                                    : Mathf.Max(1, _ponytailSegments.value);

            // 位置は取り付け先を基準にし、親はまだ付けない。
            // 親子関係を変えてもワールド位置は変わらないので、
            // ここで正しい場所に置いておかないと 3-4 では直せない。
            SendCommand(new PlaceSpringBoneChainsCommand(
                GetModelIndex?.Invoke() ?? 0,
                SpringBoneChainLayout.Cylinder,
                -1,                 // 親はまだ付けない（3-4 の担当）
                RigPrefix,
                _rigAttachIndex,    // 位置の基準
                chains, segs,
                RigIsSkirt ? 0.12f : 0.06f,
                RigIsSkirt ? 0.45f : 0.06f,
                RigIsSkirt ? 0.60f : 0.12f * segs,
                0f, null, true, 0.05f));

            _rigChains = SpringBoneChainPlacer.CollectByPrefix(GetModel(), RigPrefix);
            if (_rigChains.Count == 0) return StageResult.Retry;

            // ポニーテールは頭の後ろへ出す。PMX のモデルは Z マイナス方向を向くので、
            // 後ろは +Z。鎖の先頭だけを動かせば、以下の段はぶら下がったまま付いてくる。
            if (!RigIsSkirt && Mathf.Abs(_ponytailBack.value) > 1e-6f)
            {
                var tops = new List<int>();
                foreach (var ch in _rigChains) if (ch.Count > 0) tops.Add(ch[0]);

                SendCommand(new SetBoneTransformValueCommand(
                    GetModelIndex?.Invoke() ?? 0, tops.ToArray(),
                    SetBoneTransformValueCommand.Field.PositionZ, _ponytailBack.value));
            }

            int total = 0;
            foreach (var ch in _rigChains) total += ch.Count;

            return Ok(
                $"鎖 {_rigChains.Count} 本 / ボーン {total} 本を作った（末端の 1 本を含む）",
                "図形生成 →「揺れものボーン（3D連携）」→ 揺れボーン 円筒 → 本数と段数を入れて生成",
                "鎖のいちばん先のボーンは、1 つ手前の向きを決めるためだけに使われ、自分は揺れない。"
              + "そのぶん 1 本多く作っている。");
        }

        // ── 3-3 ──────────────────────────────────────────────────────
        private StageResult StageRigJoints()
        {
            if (_rigChains.Count == 0) return Ng("鎖がありません", null, "3-2 が走っていない。");

            int maxLen = 0;
            foreach (var ch in _rigChains) maxLen = Mathf.Max(maxLen, ch.Count);

            bool skirt = RigIsSkirt;
            float stiffTop = skirt ? _skStiffTop.value  : _ptStiffTop.value;
            float stiffTip = skirt ? _skStiffTip.value  : _ptStiffTip.value;
            float drag     = skirt ? _skDrag.value      : _ptDrag.value;
            float grav     = skirt ? _skGravity.value   : _ptGravity.value;
            float hit      = skirt ? _skHitRadius.value : _ptHitRadius.value;

            int sent = 0;
            for (int step = 0; step < maxLen; step++)
            {
                var atStep = new List<int>();
                foreach (var ch in _rigChains) if (step < ch.Count) atStep.Add(ch[step]);
                if (atStep.Count == 0) continue;

                float u = maxLen > 1 ? (float)step / (maxLen - 1) : 0f;

                SendCommand(new SetSpringBoneJointCommand(
                    GetModelIndex?.Invoke() ?? 0, atStep.ToArray(),
                    hit, Mathf.Lerp(stiffTop, stiffTip, u), grav,
                    new Vector3(0f, -1f, 0f), drag));
                sent++;
            }

            return Ok(
                $"段ごとに {sent} 回に分けて揺れ方を入れた"
              + $"（かたさ {Fmt(stiffTop)} → {Fmt(stiffTip)} / 減衰 {Fmt(drag)} / "
              + $"重力 {Fmt(grav)} / 当たり半径 {Fmt(hit)}）",
                "揺れもの編集 →「ジョイント」に値を入れて「選択へ適用」。"
              + "根元と先で変えるときは「根元→末端に補間して適用」",
                "同じ段のボーンは同じ値でよいので、段ごとにまとめて送っている。"
              + "かたさだけ根元から先へ補間している。");
        }

        // ── 3-4 ──────────────────────────────────────────────────────
        private StageResult StageRigAttach()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルがありません", null, "前の段が走っていない。");
            if (_rigChains.Count == 0) return Ng("鎖がありません", null, "3-2 が走っていない。");

            var tops = new List<int>();
            foreach (var ch in _rigChains) if (ch.Count > 0) tops.Add(ch[0]);

            SendCommand(new SetBoneParentCommand(
                GetModelIndex?.Invoke() ?? 0, tops.ToArray(), _rigAttachIndex));

            string attachName = model.GetMeshContext(_rigAttachIndex)?.Name ?? "?";

            return Ok(
                $"鎖の先頭 {tops.Count} 本を「{attachName}」の子にした",
                "メッシュリストで鎖の先頭を選び、ボーンエディタで親を変える",
                "取り付け先が動いたときに鎖もついていくようにする。"
              + "ワールド位置は保つので見た目は変わらない。");
        }

        // ── 3-5 ──────────────────────────────────────────────────────
        private StageResult StageRigChainRoots()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルがありません", null, "前の段が走っていない。");
            if (_rigChains.Count == 0) return Ng("鎖がありません", null, "3-2 が走っていない。");

            // 当たり判定のまとまりを 1 つ作る。中身は 3-6 で入れる。
            string groupName = RigPrefix + (RigIsSkirt ? "_legs" : "_head");
            SendCommand(new AddSpringBoneColliderGroupCommand(GetModelIndex?.Invoke() ?? 0, groupName));

            var names = GetModel()?.SpringBoneColliderGroupNames;
            _rigGroupIndex = names != null ? names.IndexOf(groupName) : -1;
            if (_rigGroupIndex < 0) return StageResult.Retry;

            for (int i = 0; i < _rigChains.Count; i++)
            {
                string chainName = _rigChains.Count > 1
                    ? $"{RigPrefix}_{i:00}" : RigPrefix;

                SendCommand(new SetSpringBoneChainRootCommand(
                    GetModelIndex?.Invoke() ?? 0, _rigChains[i][0],
                    chainName, "", new[] { _rigGroupIndex }));
            }

            return Ok(
                $"当たり判定のまとまり「{groupName}」を作り、鎖 {_rigChains.Count} 本の先頭に設定した",
                "揺れもの編集 →「コライダーグループ」で追加 →「チェーン」で先頭のボーンを選んで「起点にする」",
                "鎖の先頭にだけ設定を持たせる。鎖に含まれるボーンは、階層と揺れ方の有無から導き出される。");
        }

        // ── 3-6 ──────────────────────────────────────────────────────
        private StageResult StageRigColliders()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルがありません", null, "前の段が走っていない。");
            if (_rigGroupIndex < 0) return Ng("当たり判定のまとまりがありません", null, "3-5 が走っていない。");

            int mi = GetModelIndex?.Invoke() ?? 0;
            int made = 0;

            if (RigIsSkirt)
            {
                // 脚 2 本ぶんのカプセル。腰ボーンのローカル座標で置く。
                foreach (float x in new[] { -0.09f, 0.09f })
                {
                    SendCommand(new AddSpringBoneColliderCommand(
                        mi, _rigAttachIndex,
                        SpringBoneColliderShape.Capsule,
                        new Vector3(x, 0f, 0f), 0.07f,
                        new Vector3(x, -0.7f, 0f), Vector3.up,
                        new[] { _rigGroupIndex }));
                    made++;
                }
            }
            else
            {
                SendCommand(new AddSpringBoneColliderCommand(
                    mi, _rigAttachIndex,
                    SpringBoneColliderShape.Sphere,
                    new Vector3(0f, 0f, 0f), 0.12f,
                    Vector3.zero, Vector3.up,
                    new[] { _rigGroupIndex }));
                made++;
            }

            return Ok(
                RigIsSkirt
                    ? $"腰ボーンに脚のカプセル {made} 本を足した"
                    : $"頭ボーンに球 {made} 個を足した",
                "揺れもの編集 →「コライダーグループ」でまとまりを選び、ボーンを選んで当たり判定を足す",
                "当たり判定が無いと揺れものが体を突き抜ける。"
              + "当たり判定はまとまりに属し、鎖はまとまりを指す。");
        }

        // ── 3-7 ──────────────────────────────────────────────────────
        private StageResult StageRigWeights()
        {
            var model = GetModel();
            if (model == null) return Ng("モデルがありません", null, "前の段が走っていない。");
            if (_rigMeshIndex < 0) return Ng("メッシュがありません", null, "3-1 が走っていない。");
            if (_rigChains.Count == 0) return Ng("鎖がありません", null, "3-2 が走っていない。");

            int mi = GetModelIndex?.Invoke() ?? 0;

            var mc = model.GetMeshContext(_rigMeshIndex);
            var mo = mc?.MeshObject;
            if (mo == null) return Ng("メッシュが取れません", null, "3-1 の生成が終わっていない。");

            // まずスキンドにする。頂点がワールド（バインド）空間へ焼き直される。
            if (!mc.IsSkinned)
            {
                SendCommand(new ConvertToSkinnedCommand(
                    mi, new[] { _rigMeshIndex }, _rigChains[0][0]));
                return StageResult.Retry;
            }

            // 頂点を高さで段に振り分け、その段のボーンへ 1.0 で塗る。
            // まとめて塗るコマンドが無いので、段ごとに
            // 「頂点を選ぶ → 数値で入れる」を繰り返す。
            int maxLen = 0;
            foreach (var ch in _rigChains) maxLen = Mathf.Max(maxLen, ch.Count);

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int v = 0; v < mo.Vertices.Count; v++)
            {
                float y = mo.Vertices[v].Position.y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float span = Mathf.Max(1e-6f, maxY - minY);

            int painted = 0;
            for (int step = 0; step < maxLen; step++)
            {
                var verts = new List<int>();
                var meshes = new List<int>();

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    // 上端が段 0。下へ行くほど段が進む。
                    float t = (maxY - mo.Vertices[v].Position.y) / span;
                    int band = Mathf.Clamp(Mathf.RoundToInt(t * (maxLen - 1)), 0, maxLen - 1);
                    if (band != step) continue;

                    verts.Add(v);
                    meshes.Add(_rigMeshIndex);
                }

                if (verts.Count == 0) continue;

                // その段のボーンのうち先頭のものへ寄せる。
                // 鎖が複数あるときは、鎖ごとに分けずまとめて 1 本へ塗る。
                int bone = -1;
                foreach (var ch in _rigChains)
                    if (step < ch.Count) { bone = ch[step]; break; }
                if (bone < 0) continue;

                var empty = System.Array.Empty<int>();
                SendCommand(new SelectElementsCommand(
                    mi, new[] { _rigMeshIndex },
                    verts.ToArray(), meshes.ToArray(),
                    empty, empty,
                    empty, empty,
                    empty, empty));

                SendCommand(new SetSkinWeightNumericCommand(
                    mi, new[] { bone, -1, -1, -1 }, new[] { 1f, 0f, 0f, 0f }));

                painted++;
            }

            SendCommand(new NormalizeAllSkinWeightsCommand(mi));

            return Ok(
                $"{painted} 段ぶんのウェイトを塗り、正規化した",
                "スキンW数値設定パネル。頂点を選んでボーンとウェイトを入れる",
                "まとめて塗るコマンドが無いので、段ごとに「頂点を選ぶ → 数値で入れる」を繰り返している。"
              + "合計が 1 でない頂点は原点方向へ寄るので、最後に正規化する。");
        }

        // ── 小物 ──────────────────────────────────────────────────────

        // ── 3-00 ─────────────────────────────────────────────────────
        private StageResult StageRigCleanup()
        {
            var model = GetModel();
            if (model == null)
                return Ng("モデルがありません", null, "前の段の読み込みが終わっていない。");

            var doomed = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                string name = model.GetMeshContext(i)?.Name;
                if (!string.IsNullOrEmpty(name) &&
                    name.StartsWith(RigPrefix, System.StringComparison.Ordinal))
                    doomed.Add(i);
            }

            // 当たり判定のまとまりは名前で残るが、索引が動くと参照が壊れるので
            // ここでは触らない。3-5 が同じ名前を作り直したときに使い回される。

            if (doomed.Count == 0)
                return Ok("消すものはなかった", null,
                    "初回の実行、または前回の生成物が既に消えている。");

            SendCommand(new DeleteMeshesCommand(GetModelIndex?.Invoke() ?? 0, doomed.ToArray()));

            var m2 = GetModel();
            for (int i = 0; i < (m2?.MeshContextCount ?? 0); i++)
            {
                string name = m2.GetMeshContext(i)?.Name;
                if (!string.IsNullOrEmpty(name) &&
                    name.StartsWith(RigPrefix, System.StringComparison.Ordinal))
                    return StageResult.Retry;
            }

            return Ok(
                $"前回の生成物 {doomed.Count} 件を消した（名前が「{RigPrefix}」で始まるもの）",
                "メッシュリストで選んで削除",
                "消さずに実行すると、同じ名前の鎖が積み上がる。"
              + "接頭辞で引き直す処理が前回の鎖まで拾い、揺れ方を掛け直してしまう。");
        }

        /// <summary>装備を置く基準のワールド位置。メッシュとボーンで同じものを使う。</summary>
        private Vector3 RigOrigin(ModelContext model)
        {
            Vector3 at = BoneWorld(model, _rigAttachIndex);
            if (RigIsSkirt) return at;
            return at + new Vector3(0f, 0f, _ponytailBack.value);
        }

        /// <summary>取り付け先のボーンを探す。スカートは腰、ポニーテールは頭。</summary>
        private int FindRigAttachBone(ModelContext model)
        {
            // 【名前を先に見る理由】
            //   PMX の Humanoid 自動割当は Hips に「センター」を当てることがある。
            //   センターは腰とは限らず、モデルによって膝や足元の高さにある。
            //   スカートの取り付け先としては「下半身」の方が確実なので、
            //   名前での一致を先に試し、無いときだけ Humanoid の割当へ落とす。
            string[] names = RigIsSkirt
                ? new[] { "下半身", "腰", "Hips" }
                : new[] { "頭", "Head" };

            foreach (string n in names)
            {
                int i = FindByName(model, n);
                if (i >= 0 && model.GetMeshContext(i)?.Type == MeshType.Bone) return i;
            }

            var mapping = model.HumanoidMapping;
            string bone = RigIsSkirt ? "Hips" : "Head";

            if (mapping != null && !mapping.IsEmpty)
            {
                int idx = mapping.Get(bone);
                if (idx >= 0 && idx < model.MeshContextCount &&
                    model.GetMeshContext(idx)?.Type == MeshType.Bone)
                    return idx;
            }
            return -1;
        }

        private static int FindByName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (model.GetMeshContext(i)?.Name == name) return i;
            return -1;
        }

        private static Vector3 BoneWorld(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return Vector3.zero;
            var m = model.GetMeshContext(index).WorldMatrix;
            return new Vector3(m.m03, m.m13, m.m23);
        }

        private StageResult StageAutoMap()
        {
            var model = GetModel();
            if (model == null)
                return Ng("モデルがありません", null, "前の段が走っていない。");

            var names = new List<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                names.Add(mc != null && mc.Type == MeshType.Bone ? (mc.Name ?? "") : "");
            }

            var mapping = new HumanoidBoneMapping();
            int count = mapping.AutoMapFromEmbeddedCSV(names);

            if (count == 0)
                return Ng("一致するボーン名がありません",
                    "アバター用ヒューマンマッピング →「自動割当」",
                    "内蔵の対応表は PMX の標準ボーン名を前提にしている。"
                  + "独自の命名のモデルは手で割り当てる必要がある。");

            ApplyHumanoidMappingCommand.SplitMapping(mapping, out var hmNames, out var hmIdx);
            SendCommand(new ApplyHumanoidMappingCommand(
                GetModelIndex?.Invoke() ?? 0, hmNames, hmIdx));

            return Ok(
                $"{count} 本のボーンを Humanoid へ割り当てた",
                "アバター用ヒューマンマッピング →「自動割当」",
                "VRM 1.0 は humanoid が必須。割当が 0 件だと最後の書き出しで落ちる。"
              + "揺れチェーンの取付先（腰・頭）もこの割当から引いている。");
        }

        private StageResult StageTPose()
        {
            var model = GetModel();
            if (model == null)
                return Ng("モデルがありません", null, "前の段が走っていない。");

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                // 割当コマンドがまだ処理されていないので待ち直す。
                return StageResult.Retry;
            }

            SendCommand(new ApplyTPoseCommand(GetModelIndex?.Invoke() ?? 0));

            return Ok(
                "ApplyTPoseCommand を送った",
                "T ポーズ変換 →「T ポーズ化」",
                "VRM 1.0 のビューアは T ポーズを基準に姿勢を解釈する。"
              + "元の姿勢はバックアップされるので、あとで戻せる。");
        }

        /// <summary>
        /// 揺れデータが実際に載ったかを数える。
        /// VRM 出力側（Vrm10SceneAssembler）が拾うのと同じ場所を見る。
        /// </summary>
        private StageResult StageVerify()
        {
            var model = GetModel();
            if (model == null)
                return Ng("モデルがありません", null, "前の段が走っていない。");

            int chains = 0, joints = 0, colliders = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mo = model.GetMeshContext(i)?.MeshObject;
                if (mo == null) continue;

                if (mo.SpringBoneChainRoot != null) chains++;
                if (mo.SpringBoneJoint != null) joints++;
                if (mo.SpringBoneColliders != null) colliders += mo.SpringBoneColliders.Count;
            }

            // 装備生成コマンドがまだ処理されていないことがあるので、
            // 何も無い間は待ち直す。
            if (chains == 0 && joints == 0) return StageResult.Retry;

            _chains    = chains;
            _joints    = joints;
            _colliders = colliders;
            _groups    = model.SpringBoneColliderGroupNames?.Count ?? 0;
            _mapped    = model.HumanoidMapping?.Count ?? 0;

            WriteReport(null);

            if (colliders == 0)
                return Ng(
                    $"チェーン {chains} / ジョイント {joints} / コライダー 0 / "
                  + $"グループ {_groups} / Humanoid {_mapped}",
                    "揺れもの編集 →「コライダーグループ」でグループを作り、ボーンにコライダーを足す",
                    "コライダーが無いと揺れものが体を突き抜ける。"
                  + "生成物のコライダーは取付先ボーン（腰・頭）に付くので、"
                  + "取付先が解決できていないとここが 0 になる。");

            return Ok(
                $"チェーン {chains} / ジョイント {joints} / コライダー {colliders} / "
              + $"グループ {_groups} / Humanoid {_mapped}。レポート: {_reportPath}",
                "揺れもの編集 →「検査」に同じ観点の一覧が出る",
                "数えている場所は VRM 出力側が拾う場所と同じ。"
              + "ジョイントがチェーン数と同じだけしか無いなら、鎖が 1 段で終わっている。"
              + "末端は tail 扱いで揺れないので、2 段以上ないと動かない。");
        }

        private StageResult StageExportVrm()
        {
            string path = _vrmPathField.value;

            var settings = Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            settings.SupplementHumanoid = true;

            var result = ExportVrm(path, settings);
            _exportResult = result;

            if (result == null || !result.Success)
                return Ng("VRM 書出に失敗: " + (result?.ErrorMessage ?? "戻り値がありません"),
                    "エクスポートパネル → VRM",
                    "Humanoid の割当が 0 件だと VRM にできない。段 4 の割当件数を見る。");

            if (result.HumanoidBoneCount == 0)
                return Ng("Humanoid ボーンが 0 件で出力された", null,
                    "VRM 1.0 は humanoid が必須。0 件のファイルはビューアが読めない。");

            SafeSet(VrmPathKey, path);
            WriteReport(null);

            if (result.SpringCount == 0)
                return Ng(
                    $"「{Path.GetFileName(path)}」へ書き出したが、揺れチェーンが 0 本だった",
                    "エクスポートパネル → VRM",
                    "モデルにはチェーンが載っている（段 6）のに 0 本なら、出力側で落ちている。"
                  + "ルートにジョイントが無いチェーン、出力ノードが無いノードは警告付きで捨てられる。"
                  + "揺れもの編集の「検査」で同じ条件を先に潰せる。");

            return Ok(
                $"「{Path.GetFileName(path)}」へ書き出した。"
              + $"揺れチェーン {result.SpringCount} / コライダー {result.SpringBoneColliderCount} / "
              + $"Humanoid ボーン {result.HumanoidBoneCount}"
              + $"（うちダミー補完 {result.SupplementedJointCount}） / "
              + $"ブレンドシェイプ {result.MorphTargetCount} / 表情 {result.ExpressionCount}"
              + (string.IsNullOrEmpty(result.Warning) ? "" : $" / 警告: {result.Warning}"),
                "エクスポートパネル → VRM → 保存先を選ぶ",
                "ここまで通れば VRMC_springBone が出ている。"
              + "段 6 のチェーン数と揺れチェーン数が食い違うときは、分岐が経路ごとに"
              + "別チェーンへ割れているか、ルートにジョイントが無いチェーンが捨てられている。");
        }

        // ================================================================
        // レポート
        // ================================================================

        /// <summary>
        /// そこまでの内容を書き出す。中断時も呼ぶので
        /// 「レポートが出ない」という状態を作らない。
        /// </summary>
        private void WriteReport(string abortReason)
        {
            if (string.IsNullOrEmpty(_reportPath)) return;

            var sb = new StringBuilder();
            sb.AppendLine("# PolyLing スプリングボーン検証レポート"
                + (abortReason != null ? "（中断）" : ""));
            sb.AppendLine("日時: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("PMX: " + (_pmxPathField?.value ?? ""));
            sb.AppendLine("VRM: " + (_vrmPathField?.value ?? ""));
            if (abortReason != null) sb.AppendLine("中断理由: " + abortReason);
            sb.AppendLine();

            sb.AppendLine("## 実行ログ");
            foreach (var l in PlainLog) sb.Append(l);
            sb.AppendLine();

            var model = GetModel?.Invoke();
            sb.AppendLine("## モデル側の揺れデータ");
            sb.AppendLine("モデル: " + (model?.Name ?? "<null>"));
            sb.AppendLine($"MeshContext 数: {model?.MeshContextCount ?? 0}");
            sb.AppendLine($"チェーン: {_chains}");
            sb.AppendLine($"ジョイント: {_joints}");
            sb.AppendLine($"コライダー: {_colliders}");
            sb.AppendLine($"コライダーグループ: {_groups}");
            sb.AppendLine($"Humanoid 割当: {_mapped}");
            sb.AppendLine();

            sb.AppendLine("## VRM 出力");
            if (_exportResult == null)
            {
                sb.AppendLine("未実行");
            }
            else
            {
                sb.AppendLine($"成否: {(_exportResult.Success ? "成功" : "失敗")}");
                sb.AppendLine($"揺れチェーン: {_exportResult.SpringCount}");
                sb.AppendLine($"揺れコライダー: {_exportResult.SpringBoneColliderCount}");
                sb.AppendLine($"Humanoid ボーン: {_exportResult.HumanoidBoneCount}"
                    + $"（ダミー補完 {_exportResult.SupplementedJointCount}）");
                sb.AppendLine($"ブレンドシェイプ: {_exportResult.MorphTargetCount}");
                sb.AppendLine($"表情: {_exportResult.ExpressionCount}");
                if (!string.IsNullOrEmpty(_exportResult.Warning))
                    sb.AppendLine("警告: " + _exportResult.Warning);
                if (!string.IsNullOrEmpty(_exportResult.ErrorMessage))
                    sb.AppendLine("エラー: " + _exportResult.ErrorMessage);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_reportPath));
                File.WriteAllText(_reportPath, sb.ToString(), new UTF8Encoding(true));
                Debug.Log("[SpringBoneTest] レポート: " + _reportPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpringBoneTest] レポートを書けませんでした: " + e.Message);
            }
        }

        // ================================================================
        // 小物
        // ================================================================

        private static string SafeGet(string key)
        {
            try { return RecentPaths.Get(key); }
            catch { return ""; }
        }

        private static void SafeSet(string key, string value)
        {
            try { RecentPaths.Set(key, value); }
            catch { }
        }
    }
}
