// PlayerSpringBoneTestSubPanel.cs
// スプリングボーン検証パネル。ボタン 1 回で
//   PMX 読込 → ダミー揺れもの生成 → Humanoid 自動割当 → T ポーズ化 → VRM 書き出し
// までを流し、各段の直後に検査して結果を表に出す。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ要るか】
//   SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders を書き込む
//   オーサリング UI が無く、CSV で持つモデルしか揺れデータを持たない。
//   VRM 出力（VRMC_springBone）を検証する手段が無いので、
//   既存モデルへその場でダミーを足して出力まで通せるようにする。
//
// 【なぜ実コマンドを送るか】
//   PlayerPipelineTestSubPanel.cs:7-11 と同じ理由。Ops を直接叩くと
//   ディスパッチャ側の欠陥が検査を素通りする。パネルが押されたときに
//   飛ぶのと同じ PanelCommand を送る。
//
// 【段の区切り】
//   コマンドはキュー経由で処理されるため、送信直後の状態は当てにならない。
//   1 段ごとに間を空けてから検査する。MonoBehaviour.Update は使わない。
//   UIToolkit の schedule で、段が終わるたびに次の段を予約する。
//   PMX 読込のように完了までの時間が読めない段は Retry を返して待つ。
//
// 【パスの既定値】
//   インポート／エクスポートのパネルと同じ RecentPaths のキーを読む。
//     Import.PMX.Path / Export.VRM.Path
//   同じキーへ書き戻すので、通常のパネルと履歴を共有する。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;          // RecentPaths
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools.SpringBoneTest;

namespace Poly_Ling.Player
{
    /// <summary>揺れもの検証。人間の操作は形状を選んで「実行」を押すだけ。</summary>
    public class PlayerSpringBoneTestSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        /// <summary>現在のモデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>現在のモデル索引。</summary>
        public Func<int> GetModelIndex;

        /// <summary>コマンド送信。パネルが押されたときと同じ経路へ流す。</summary>
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
        // UI
        // ================================================================

        private VisualElement _root;
        private TextField     _pmxPathField;
        private TextField     _vrmPathField;
        private Toggle        _doImport;
        private Toggle        _doExport;
        private EnumField     _shapeField;
        private Toggle        _applyMapping;
        private Toggle        _applyTPose;
        private Label         _status;
        private VisualElement _resultTable;

        private FloatField   _stiffnessTop, _stiffnessTip, _drag, _gravity, _hitRadius;
        private Toggle       _autoSkirtHeight;
        private FloatField   _skirtLift, _ponytailBack;
        private IntegerField _strands, _segments;

        // ================================================================
        // 実行状態
        // ================================================================

        /// <summary>段の結果。Retry は同じ段をもう一度呼ぶ。</summary>
        private enum StageResult { Ok, Fail, Retry }

        private readonly List<Func<StageResult>> _stages = new List<Func<StageResult>>();
        private readonly List<(string name, bool ok, string detail)> _log =
            new List<(string, bool, string)>();

        private int  _stageIndex;
        private int  _retryCount;
        private bool _running;

        /// <summary>段の間に空けるミリ秒。コマンドキューが捌けるのを待つ。</summary>
        private const long StageIntervalMs = 120;

        /// <summary>同じ段を待ち直す上限。120ms × 100 ＝ 12 秒で諦める。</summary>
        private const int MaxRetry = 100;

        /// <summary>読込前のメッシュ数。読込完了の判定に使う。</summary>
        private int _meshCountBeforeImport;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = parent;
            _root.Clear();

            var title = new Label("スプリングボーン検証");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            _root.Add(title);

            var note = new Label(
                "PMX 読込 → ダミー揺れもの生成 → Humanoid 割当 → T ポーズ → VRM 書出 を通しで流します。\n"
                + "揺れデータのオーサリング UI が無いため、検証用のダミー装備を生成します。\n"
                + "生成物は接頭辞で識別し、再実行時は作り直します。");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            _root.Add(note);

            // ── 入出力 ────────────────────────────────────────────────
            _doImport = new Toggle("PMX を読み込む") { value = true };
            _root.Add(_doImport);

            _pmxPathField = new TextField("PMX パス");
            _pmxPathField.SetValueWithoutNotify(RecentPaths.Get(PmxPathKey));
            _pmxPathField.RegisterValueChangedCallback(e => RecentPaths.Set(PmxPathKey, e.newValue));
            _root.Add(_pmxPathField);

            _doExport = new Toggle("VRM を書き出す") { value = true };
            _doExport.style.marginTop = 4;
            _root.Add(_doExport);

            _vrmPathField = new TextField("VRM パス");
            _vrmPathField.SetValueWithoutNotify(RecentPaths.Get(VrmPathKey));
            _vrmPathField.RegisterValueChangedCallback(e => RecentPaths.Set(VrmPathKey, e.newValue));
            _vrmPathField.style.marginBottom = 4;
            _root.Add(_vrmPathField);

            // ── 形状 ──────────────────────────────────────────────────
            _shapeField = new EnumField("形状", SpringBoneTestRigShape.Skirt);
            _root.Add(_shapeField);

            // ── パラメータ ────────────────────────────────────────────
            var fo = new Foldout { text = "パラメータ", value = false };

            var defaults = new SpringBoneTestRigParams();

            _strands      = new IntegerField("チェーン本数（Skirt）") { value = defaults.Strands };
            _segments     = new IntegerField("1本あたりの段数")       { value = defaults.SegmentsPerStrand };

            // PMX の「センター」は腰の高さとは限らない（ひざより下のこともある）。
            // 既定では股関節の高さに合わせる。
            _autoSkirtHeight = new Toggle("腰高さを股関節に合わせる") { value = defaults.AutoSkirtHeight };
            _skirtLift    = new FloatField("腰高さの補正[m]")         { value = defaults.SkirtLift };
            _ponytailBack = new FloatField("ポニテを後ろへ[m]")       { value = defaults.PonytailBack };
            _stiffnessTop = new FloatField("stiffness 根元")          { value = defaults.StiffnessTop };
            _stiffnessTip = new FloatField("stiffness 末端")          { value = defaults.StiffnessTip };
            _drag         = new FloatField("drag")                    { value = defaults.Drag };
            _gravity      = new FloatField("gravityPower")            { value = defaults.GravityPower };
            _hitRadius    = new FloatField("hitRadius")               { value = defaults.HitRadius };

            fo.Add(_strands); fo.Add(_segments);
            fo.Add(_autoSkirtHeight); fo.Add(_skirtLift); fo.Add(_ponytailBack);
            fo.Add(_stiffnessTop); fo.Add(_stiffnessTip);
            fo.Add(_drag); fo.Add(_gravity); fo.Add(_hitRadius);
            _root.Add(fo);

            // ── 後段 ──────────────────────────────────────────────────
            _applyMapping = new Toggle("Humanoid 自動割当を実行") { value = true };
            _applyTPose   = new Toggle("T ポーズ化を実行")        { value = true };
            _root.Add(_applyMapping);
            _root.Add(_applyTPose);

            var runBtn = new Button(OnRun) { text = "実行" };
            runBtn.style.marginTop = 6;
            runBtn.style.marginBottom = 4;
            runBtn.style.height = 28;
            runBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _root.Add(runBtn);

            _status = new Label("");
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginBottom = 4;
            _root.Add(_status);

            _resultTable = new VisualElement();
            _root.Add(_resultTable);
        }

        public void Refresh()
        {
            // 実行中は触らない（段の途中で書き換えると結果が読めなくなる）。
            if (_running) return;

            _pmxPathField?.SetValueWithoutNotify(RecentPaths.Get(PmxPathKey));
            _vrmPathField?.SetValueWithoutNotify(RecentPaths.Get(VrmPathKey));
        }

        // ================================================================
        // 実行
        // ================================================================

        private void OnRun()
        {
            if (_running) { SetStatus("実行中です。"); return; }

            if (SendCommand == null || GetModel == null)
            {
                SetStatus("配線が足りません（SendCommand / GetModel）。");
                return;
            }

            bool doImport = _doImport.value;
            bool doExport = _doExport.value;

            if (doImport)
            {
                if (ImportPmx == null) { SetStatus("配線が足りません（ImportPmx）。"); return; }
                string path = _pmxPathField.value;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SetStatus("PMX パスが正しくありません。");
                    return;
                }
            }
            else if (GetModel() == null)
            {
                SetStatus("モデルが読み込まれていません。");
                return;
            }

            if (doExport)
            {
                if (ExportVrm == null) { SetStatus("配線が足りません（ExportVrm）。"); return; }
                if (string.IsNullOrEmpty(_vrmPathField.value))
                {
                    SetStatus("VRM パスが空です。");
                    return;
                }
            }

            _log.Clear();
            _resultTable.Clear();
            _stages.Clear();
            _stageIndex = 0;
            _retryCount = 0;
            _running = true;

            if (doImport)
            {
                _stages.Add(StageImportPmx);
                _stages.Add(StageWaitImport);
            }
            _stages.Add(StageBuildRig);
            if (_applyMapping.value) _stages.Add(StageAutoMap);
            if (_applyTPose.value)   _stages.Add(StageTPose);
            _stages.Add(StageVerify);
            if (doExport) _stages.Add(StageExportVrm);

            SetStatus("実行中…");
            ScheduleNext();
        }

        /// <summary>次の段を予約する。Update は使わない（規約）。</summary>
        private void ScheduleNext()
        {
            _root.schedule.Execute(RunStage).StartingIn(StageIntervalMs);
        }

        private void RunStage()
        {
            if (_stageIndex >= _stages.Count)
            {
                _running = false;
                RenderTable();
                SetStatus("完了しました。");
                return;
            }

            var stage = _stages[_stageIndex];

            StageResult r;
            try
            {
                r = stage();
            }
            catch (Exception e)
            {
                _log.Add(("例外", false, e.Message));
                Debug.LogException(e);
                Stop("例外で停止しました: " + e.Message);
                return;
            }

            switch (r)
            {
                case StageResult.Retry:
                    if (++_retryCount > MaxRetry)
                    {
                        _log.Add(("待機", false, "時間内に完了しませんでした"));
                        Stop("待機がタイムアウトしました。");
                        return;
                    }
                    ScheduleNext();
                    return;

                case StageResult.Fail:
                    Stop("失敗で停止しました。");
                    return;

                default:
                    _stageIndex++;
                    _retryCount = 0;
                    ScheduleNext();
                    return;
            }
        }

        private void Stop(string message)
        {
            _running = false;
            RenderTable();
            SetStatus(message);
        }

        // ================================================================
        // 各段
        // ================================================================

        private StageResult StageImportPmx()
        {
            var model = GetModel();
            _meshCountBeforeImport = model?.MeshContextCount ?? -1;

            ImportPmx(_pmxPathField.value);
            _log.Add(("PMX 読込", true, Path.GetFileName(_pmxPathField.value)));
            return StageResult.Ok;
        }

        /// <summary>
        /// 読込の完了を待つ。ファイルサイズ次第で時間が読めないので、
        /// メッシュ数が変わるまで同じ段を繰り返す。
        /// </summary>
        private StageResult StageWaitImport()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount == 0) return StageResult.Retry;
            if (model.MeshContextCount == _meshCountBeforeImport) return StageResult.Retry;

            _log.Add(("読込完了", true, $"{model.MeshContextCount} コンテキスト"));
            return StageResult.Ok;
        }

        private StageResult StageBuildRig()
        {
            var model = GetModel();
            if (model == null) { _log.Add(("装備生成", false, "モデルがありません")); return StageResult.Fail; }

            var prms = new SpringBoneTestRigParams
            {
                Shape             = (SpringBoneTestRigShape)_shapeField.value,
                Strands           = Mathf.Max(1, _strands.value),
                SegmentsPerStrand = Mathf.Max(1, _segments.value),
                AutoSkirtHeight   = _autoSkirtHeight.value,
                SkirtLift         = _skirtLift.value,
                PonytailBack      = _ponytailBack.value,
                StiffnessTop      = _stiffnessTop.value,
                StiffnessTip      = _stiffnessTip.value,
                Drag              = _drag.value,
                GravityPower      = _gravity.value,
                HitRadius         = _hitRadius.value,
            };

            SendCommand(new BuildSpringBoneTestRigCommand(
                GetModelIndex?.Invoke() ?? 0, prms, clearExisting: true));

            _log.Add(("装備生成", true, prms.Shape.ToString()));
            return StageResult.Ok;
        }

        private StageResult StageAutoMap()
        {
            var model = GetModel();
            if (model == null) { _log.Add(("Humanoid 割当", false, "モデルがありません")); return StageResult.Fail; }

            var names = new List<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                names.Add(mc != null && mc.Type == MeshType.Bone ? (mc.Name ?? "") : "");
            }

            var mapping = new HumanoidBoneMapping();
            int count = mapping.AutoMapFromEmbeddedCSV(names);

            if (count == 0)
            {
                _log.Add(("Humanoid 割当", false, "一致するボーン名がありません"));
                return StageResult.Fail;
            }

            SendCommand(new ApplyHumanoidMappingCommand(
                GetModelIndex?.Invoke() ?? 0, mapping.Clone()));

            _log.Add(("Humanoid 割当", true, $"{count} ボーン"));
            return StageResult.Ok;
        }

        private StageResult StageTPose()
        {
            var model = GetModel();
            if (model == null) { _log.Add(("T ポーズ", false, "モデルがありません")); return StageResult.Fail; }

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty)
            {
                // 割当コマンドがまだ処理されていない可能性があるので待ち直す。
                return StageResult.Retry;
            }

            SendCommand(new ApplyTPoseCommand(GetModelIndex?.Invoke() ?? 0));
            _log.Add(("T ポーズ", true, ""));
            return StageResult.Ok;
        }

        /// <summary>
        /// 揺れデータが実際に載ったかを数える。
        /// VRM 出力側（Vrm10SceneAssembler）が拾うのと同じ場所を見る。
        /// </summary>
        private StageResult StageVerify()
        {
            var model = GetModel();
            if (model == null) { _log.Add(("検査", false, "モデルがありません")); return StageResult.Fail; }

            int chains = 0, joints = 0, colliders = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var mo = mc.MeshObject;
                if (mo == null) continue;

                if (mo.SpringBoneChainRoot != null) chains++;
                if (mo.SpringBoneJoint != null) joints++;
                if (mo.SpringBoneColliders != null) colliders += mo.SpringBoneColliders.Count;
            }

            // 装備生成コマンドがまだ処理されていないことがあるので、
            // 何も無い間は待ち直す。
            if (chains == 0 && joints == 0) return StageResult.Retry;

            int groups = model.SpringBoneColliderGroupNames?.Count ?? 0;
            int mapped = model.HumanoidMapping?.Count ?? 0;

            _log.Add(("検査: チェーン",   chains    > 0, chains.ToString()));
            _log.Add(("検査: ジョイント", joints    > 0, joints.ToString()));
            _log.Add(("検査: コライダー", colliders > 0, colliders.ToString()));
            _log.Add(("検査: グループ",   groups    > 0, groups.ToString()));
            _log.Add(("検査: Humanoid",   mapped    > 0, mapped.ToString()));

            return StageResult.Ok;
        }

        private StageResult StageExportVrm()
        {
            string path = _vrmPathField.value;

            var settings = Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            var result = ExportVrm(path, settings);

            if (result == null || !result.Success)
            {
                _log.Add(("VRM 書出", false, result?.ErrorMessage ?? "戻り値がありません"));
                return StageResult.Fail;
            }

            _log.Add(("VRM 書出", true, Path.GetFileName(path)));
            _log.Add(("結果: ブレンドシェイプ", result.MorphTargetCount > 0, result.MorphTargetCount.ToString()));
            _log.Add(("結果: 表情",             result.ExpressionCount  > 0, result.ExpressionCount.ToString()));
            _log.Add(("結果: 揺れチェーン",     result.SpringCount      > 0, result.SpringCount.ToString()));
            _log.Add(("結果: コライダー",       result.SpringBoneColliderCount > 0,
                                                result.SpringBoneColliderCount.ToString()));

            if (!string.IsNullOrEmpty(result.Warning))
                _log.Add(("警告", false, result.Warning));

            return StageResult.Ok;
        }

        // ================================================================
        // 表示
        // ================================================================

        private void RenderTable()
        {
            _resultTable.Clear();

            foreach (var (name, ok, detail) in _log)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1;

                var mark = new Label(ok ? "OK" : "NG");
                mark.style.width = 28;
                mark.style.color = ok ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.4f);
                mark.style.unityFontStyleAndWeight = FontStyle.Bold;

                var label = new Label(name);
                label.style.width = 150;

                var value = new Label(detail);
                value.style.flexGrow = 1;
                value.style.whiteSpace = WhiteSpace.Normal;

                row.Add(mark); row.Add(label); row.Add(value);
                _resultTable.Add(row);
            }
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
        }
    }
}
