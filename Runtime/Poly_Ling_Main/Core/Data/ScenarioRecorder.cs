// ScenarioRecorder.cs
// 実行したコマンドを、手本（シナリオ）の下書きとして控える。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何を減らすか】
//   検証パネルを手本へ写すとき、パネルの C# を読んで
//   「どのコマンドをどの値で送ったか」を組み立て直していた。
//   値の多くは C# の中で計算され、フィールドで段をまたいで持ち回されるので、
//   読むだけでは追いにくい。実際に流して、送られたものをそのまま控える。
//
// 【どこで控えるか】
//   コマンド … PlayerCommandDispatcher.Dispatch の一番外側。
//             パネルの SendCommand も MCP も UI も同じ入口を通る。
//             入れ子（実処理の中で撃たれるもの）は外側 1 本に含まれるので控えない。
//   段の文言 … PlayerStagedTestSubPanelBase の Ok / Ng。
//             「UI でやるなら」と「なぜ」を Note にして、
//             その段で控えたコマンドの前へ置く。UI の操作説明は流すときにやることが無いので、
//             止まる段（Instruction）にはしない。
//
// 【控えないもの】
//   手本コマンド・UI 自動操作・コマンド定義の検査。手順ではなく道具の操作なので。
//   判定はディスパッチャが行う（どの口で捌いたかを知っているのはあちら）。
//   runScenario / continueScenario で流した段も、中身は入れ子なので控えない。
//
// 【戻り値は控えない】
//   段（ObjectGroupStep）に戻り値の欄が無い。失敗した段だけ Note で残す。
//
// 【索引も焼いたまま入る】
//   索引を名前からの照会に置き換えるべきかは、queryScenarioAudit が
//   IsMeshRef の印で指摘する。ここでは値を変えない。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>実行したコマンドを手本の下書きとして控える。</summary>
    public static class ScenarioRecorder
    {
        /// <summary>控えている段。null なら記録していない。</summary>
        private static List<ObjectGroupStep> _steps;

        /// <summary>今の検証段が始まったときの段数。-1 は印なし。</summary>
        private static int _stageMark = -1;

        /// <summary>記録中か。</summary>
        public static bool IsRecording => _steps != null;

        /// <summary>控えた段の数。</summary>
        public static int StepCount => _steps?.Count ?? 0;

        // ================================================================
        // 開始・終了
        // ================================================================

        /// <summary>記録を始める。記録中なら失敗。</summary>
        public static bool Start(out string error)
        {
            error = null;
            if (_steps != null)
            {
                error = "既に記録中です。stopScenarioRecording で止めてから始めてください";
                return false;
            }
            _steps     = new List<ObjectGroupStep>();
            _stageMark = -1;
            return true;
        }

        /// <summary>控えたものを捨てて記録をやめる。</summary>
        public static void Discard()
        {
            _steps     = null;
            _stageMark = -1;
        }

        /// <summary>
        /// 記録を止め、手本として登録する。
        /// 登録に失敗したときは記録を続ける（名前を変えて止め直せる）。
        /// </summary>
        public static bool Stop(string name, string goal, bool overwrite, out int steps, out string error)
        {
            steps = 0;
            error = null;

            if (_steps == null)            { error = "記録していません";     return false; }
            if (string.IsNullOrEmpty(name)) { error = "手本の名前が空です"; return false; }

            var g = new ObjectGroup(name);
            g.Steps.Clear();   // コンストラクタが空の段を 1 つ足すため
            foreach (var s in _steps) g.AddStep(s);

            g.Goal = goal ?? "";
            g.Provenance = new ObjectGroupProvenance
            {
                ParentName    = "",
                ChangeSummary = "startScenarioRecording から stopScenarioRecording までに実行したコマンドを記録した",
                CreatedBy     = "",
            };

            if (!ScenarioLibrary.Register(g, overwrite, out error)) return false;

            steps = g.StepCount;
            Discard();
            return true;
        }

        // ================================================================
        // コマンド
        // ================================================================

        /// <summary>
        /// 実行したコマンドを 1 段として控える。記録していなければ何もしない。
        /// 呼ぶのは Dispatch の一番外側だけ。
        /// </summary>
        public static void RecordCommand(PanelCommand cmd, CommandResult result)
        {
            if (_steps == null || cmd == null) return;

            Type t = cmd.GetType();

            var step = new ObjectGroupStep
            {
                Kind   = ObjectGroupStepKind.Command,
                Action = PanelCommandFactory.ActionOf(t),
            };
            foreach (var kv in PanelCommandFactory.ToArgs(cmd))
                step.SetArg(kv.Key, kv.Value);

            _steps.Add(step);

            // 文字列の引数へ直せない型を持つコマンドは、撃ち直しても同じにならない。
            if (!PanelCommandFactory.TryBuildToolJson(t, out _, out string why))
                _steps.Add(NoteStep($"直前の段 {step.Action} は文字列の引数に直せない型を含むので、撃ち直しても同じにならない（{why}）"));

            if (result != null && !result.Success)
                _steps.Add(NoteStep($"直前の段 {step.Action} は記録したとき失敗した: {result.Reason}"));
        }

        // ================================================================
        // 検証パネルの段
        // ================================================================

        /// <summary>検証パネルの段が始まった。ここから後に控えたものがその段のもの。</summary>
        public static void BeginStage()
        {
            if (_steps == null) return;
            _stageMark = _steps.Count;
        }

        /// <summary>
        /// 検証パネルの段が Ok / Ng を返した。
        /// 「UI でやるなら」を Instruction、「なぜ」を Note にして段の頭へ置き、
        /// その段で控えたコマンドの目的に段名を入れる。
        /// </summary>
        public static void AnnotateStage(string stageName, string did, string ui, string why, bool failed)
        {
            if (_steps == null) return;

            string stage = stageName ?? "";
            int at = (_stageMark >= 0 && _stageMark <= _steps.Count) ? _stageMark : _steps.Count;

            for (int i = at; i < _steps.Count; i++)
                if (_steps[i].IsExecutable && string.IsNullOrEmpty(_steps[i].Purpose))
                    _steps[i].Purpose = stage;

            var head = new List<ObjectGroupStep>();
            if (failed)
                head.Add(NoteStep($"{stage}: 記録したときこの段で失敗した（{did}）"));
            if (!string.IsNullOrWhiteSpace(ui))
                head.Add(NoteStep($"{stage}: UI でやるなら {ui}"));
            if (!string.IsNullOrWhiteSpace(why))
                head.Add(NoteStep($"{stage}: {why}"));

            _steps.InsertRange(at, head);

            // 同じ段の文言を 2 回入れない。
            _stageMark = -1;
        }

        private static ObjectGroupStep NoteStep(string text)
            => new ObjectGroupStep { Kind = ObjectGroupStepKind.Note, Purpose = text ?? "" };
    }
}
