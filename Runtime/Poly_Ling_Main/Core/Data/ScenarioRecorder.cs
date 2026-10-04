// ScenarioRecorder.cs
// 実行したコマンドを、シナリオの下書きとして控える。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何を減らすか】
//   検証パネルをシナリオへ写すとき、パネルの C# を読んで
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
//             止まる項目（Instruction）にはしない。
//
// 【控えないもの】
//   シナリオコマンド・UI 自動操作・コマンド定義の検査。手順ではなく道具の操作なので。
//   判定はディスパッチャが行う（どの口で捌いたかを知っているのはあちら）。
//   runScenario / continueScenario で流した項目も、中身は入れ子なので控えない。
//
// 【状態】
//   記録なし → 記録中 ⇄ 一時停止中 → 終了（保存待ち）→ 保存か破棄で記録なしへ。
//   一時停止中と終了後は控えない。終了後は再開できない。
//   控えが残っている間は新しく始められない（黙って捨てないため）。
//   保存は一時停止中か終了後だけ。記録中に保存すると、保存の操作の前後で
//   控えの範囲が曖昧になるので止めてからにする。
//
// 【戻り値は控えない】
//   項目（ObjectGroupStep）に戻り値の欄が無い。失敗した項目だけ Note で残す。
//
// 【索引も焼いたまま入る】
//   索引を名前からの照会に置き換えるべきかは、queryScenarioAudit が
//   IsMeshRef の印で指摘する。ここでは値を変えない。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>記録の状態。</summary>
    public enum ScenarioRecordingState
    {
        /// <summary>記録していない。控えも無い。</summary>
        None,
        /// <summary>記録中。実行したコマンドを控える。</summary>
        Recording,
        /// <summary>一時停止中。控えは残り、再開できる。</summary>
        Paused,
        /// <summary>終了した。控えは保存か破棄を待つ。再開はできない。</summary>
        Ended,
    }

    /// <summary>実行したコマンドをシナリオの下書きとして控える。</summary>
    public static class ScenarioRecorder
    {
        /// <summary>控えている項目。null なら控えが無い。</summary>
        private static List<ObjectGroupStep> _steps;

        /// <summary>記録の状態。</summary>
        private static ScenarioRecordingState _state = ScenarioRecordingState.None;

        /// <summary>今の検証段が始まったときの項目数。-1 は印なし。</summary>
        private static int _stageMark = -1;

        /// <summary>記録の状態。</summary>
        public static ScenarioRecordingState State => _state;

        /// <summary>いま控えているか（記録中で、一時停止・終了していない）。</summary>
        public static bool IsRecording => _state == ScenarioRecordingState.Recording;

        /// <summary>控えが残っているか（記録中・一時停止中・終了後）。</summary>
        public static bool HasDraft => _steps != null;

        /// <summary>控えた項目の数。</summary>
        public static int StepCount => _steps?.Count ?? 0;

        // ================================================================
        // 開始・一時停止・再開・終了
        // ================================================================

        /// <summary>記録を始める。控えが残っていれば失敗。</summary>
        public static bool Start(out string error)
        {
            error = null;
            if (_steps != null)
            {
                error = _state == ScenarioRecordingState.Ended
                    ? "保存していない記録があります。saveScenarioRecording で保存するか discardScenarioRecording で破棄してから始めてください"
                    : "既に記録中です。stopScenarioRecording で終了し、保存か破棄をしてから始めてください";
                return false;
            }
            _steps     = new List<ObjectGroupStep>();
            _state     = ScenarioRecordingState.Recording;
            _stageMark = -1;
            return true;
        }

        /// <summary>一時停止する。記録中でなければ失敗。</summary>
        public static bool Pause(out string error)
        {
            error = null;
            if (_state != ScenarioRecordingState.Recording) { error = NotState("記録中"); return false; }
            _state     = ScenarioRecordingState.Paused;
            _stageMark = -1;
            return true;
        }

        /// <summary>再開する。一時停止中でなければ失敗。</summary>
        public static bool Resume(out string error)
        {
            error = null;
            if (_state != ScenarioRecordingState.Paused) { error = NotState("一時停止中"); return false; }
            _state = ScenarioRecordingState.Recording;
            return true;
        }

        /// <summary>終了する。控えは保存か破棄まで残る。記録中・一時停止中でなければ失敗。</summary>
        public static bool End(out string error)
        {
            error = null;
            if (_state != ScenarioRecordingState.Recording && _state != ScenarioRecordingState.Paused)
            { error = NotState("記録中か一時停止中"); return false; }
            _state     = ScenarioRecordingState.Ended;
            _stageMark = -1;
            return true;
        }

        /// <summary>控えたものを捨てて記録をやめる。</summary>
        public static void Discard()
        {
            _steps     = null;
            _state     = ScenarioRecordingState.None;
            _stageMark = -1;
        }

        /// <summary>
        /// 控えをシナリオとして登録する。一時停止中か終了後だけ。
        /// 登録したら控えを消す。失敗したときは控えも状態もそのまま（名前を変えて保存し直せる）。
        /// </summary>
        public static bool Save(string name, string goal, bool overwrite, out int steps, out string error, string folder = null)
        {
            steps = 0;
            error = null;

            if (_steps == null) { error = "保存する記録がありません"; return false; }
            if (_state == ScenarioRecordingState.Recording)
            { error = "記録中は保存できません。pauseScenarioRecording か stopScenarioRecording の後に保存してください"; return false; }
            if (string.IsNullOrEmpty(name)) { error = "シナリオの名前が空です"; return false; }

            var g = new ObjectGroup(name);
            g.Steps.Clear();   // コンストラクタが空の項目を 1 つ足すため
            foreach (var s in _steps) g.AddStep(s);

            g.Goal = goal ?? "";
            g.Provenance = new ObjectGroupProvenance
            {
                ParentName    = "",
                ChangeSummary = "startScenarioRecording から記録したコマンドを saveScenarioRecording で保存した",
                CreatedBy     = "",
            };

            if (!ScenarioLibrary.Register(g, overwrite, out error, folder)) return false;

            steps = g.StepCount;
            Discard();
            return true;
        }

        /// <summary>状態の表示名。</summary>
        public static string StateText(ScenarioRecordingState s)
        {
            switch (s)
            {
                case ScenarioRecordingState.Recording: return "記録中";
                case ScenarioRecordingState.Paused:    return "一時停止中";
                case ScenarioRecordingState.Ended:     return "終了（保存待ち）";
                default:                               return "記録していません";
            }
        }

        /// <summary>状態の識別名（コマンドの戻り値用）。</summary>
        public static string StateId(ScenarioRecordingState s)
        {
            switch (s)
            {
                case ScenarioRecordingState.Recording: return "recording";
                case ScenarioRecordingState.Paused:    return "paused";
                case ScenarioRecordingState.Ended:     return "ended";
                default:                               return "none";
            }
        }

        private static string NotState(string need)
            => $"{need}ではありません（いまは{StateText(_state)}）";

        // ================================================================
        // コマンド
        // ================================================================

        /// <summary>
        /// 実行したコマンドを 1 項目として控える。記録していなければ何もしない。
        /// 呼ぶのは Dispatch の一番外側だけ。
        /// </summary>
        public static void RecordCommand(PanelCommand cmd, CommandResult result)
        {
            if (!IsRecording || cmd == null) return;

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
                _steps.Add(NoteStep($"直前の項目 {step.Action} は文字列の引数に直せない型を含むので、撃ち直しても同じにならない（{why}）"));

            if (result != null && !result.Success)
                _steps.Add(NoteStep($"直前の項目 {step.Action} は記録したとき失敗した: {result.Reason}"));
        }

        // ================================================================
        // 検証パネルの段
        // ================================================================

        /// <summary>検証パネルの段が始まった。ここから後に控えたものがその段のもの。</summary>
        public static void BeginStage()
        {
            if (!IsRecording) return;
            _stageMark = _steps.Count;
        }

        /// <summary>
        /// 検証パネルの段が Ok / Ng を返した。
        /// 「UI でやるなら」を Instruction、「なぜ」を Note にして段の頭へ置き、
        /// その段で控えたコマンドの目的に段名を入れる。
        /// </summary>
        public static void AnnotateStage(string stageName, string did, string ui, string why, bool failed)
        {
            if (!IsRecording) return;

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
