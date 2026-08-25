// Editor/HierarchyIO/HierarchyExportReport.cs
// ============================================================
// ヒエラルキーエクスポートの結果レポート
// ============================================================
//
// 【目的】
//   これまで警告・エラーはコンソール（Debug.LogWarning/LogError）にしか出ず、
//   ダイアログは「書き出しました」としか言わなかった。結果をここへ溜めて
//   ダイアログにも要約を出す。
//
// 【使い方】
//   ・Info/Warn/Error は呼ぶだけでコンソールにも同じ内容を出す（従来の
//     コンソール出力を落とさないため）。呼び出し側は Debug.Log～ を
//     _report.Warn(...) に置き換えるだけでよい。
//   ・モデル1件ごとに Reset() する。
//   ・BuildDialogText() が返す文字列をそのまま DisplayDialog へ渡す。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Poly_Ling.EditorIO
{
    /// <summary>エクスポート1回ぶんの結果。</summary>
    public class HierarchyExportReport
    {
        private const string LogPrefix = "[HierarchyExport] ";

        /// <summary>ダイアログに並べる警告・エラーの上限。超過分は件数だけ出す。</summary>
        private const int MaxListedLines = 10;

        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _errors   = new List<string>();
        private readonly List<string> _notes    = new List<string>();

        // --- 集計（呼び出し側が加算する） ---

        /// <summary>出力した GameObject 数（メッシュ・関節）。</summary>
        public int ExportedNodeCount;

        /// <summary>不可視のまま出力対象から外したノード数。</summary>
        public int SkippedInvisibleCount;

        /// <summary>可視ノードの親として補完した不可視ノード数。</summary>
        public int SupplementedAncestorCount;

        /// <summary>不足する Humanoid 必須関節としてダミー生成した数。</summary>
        public int SupplementedJointCount;

        /// <summary>出力したボーン数。</summary>
        public int BoneCount;

        /// <summary>Avatar の生成結果（未実行なら null）。</summary>
        public string AvatarResult;

        /// <summary>
        /// 書き出したレポートテキストのパス（未書き出しなら null）。
        /// ダイアログはコンソールの代わりにここを案内する。
        /// </summary>
        public string LogFilePath;

        public int WarningCount => _warnings.Count;
        public int ErrorCount   => _errors.Count;
        public bool HasProblem  => _warnings.Count > 0 || _errors.Count > 0;

        // ================================================================
        // 記録
        // ================================================================

        public void Reset()
        {
            _warnings.Clear();
            _errors.Clear();
            _notes.Clear();

            ExportedNodeCount         = 0;
            SkippedInvisibleCount     = 0;
            SupplementedAncestorCount = 0;
            SupplementedJointCount    = 0;
            BoneCount                 = 0;
            AvatarResult              = null;
            LogFilePath               = null;
        }

        /// <summary>ダイアログには出さず、コンソールにだけ出す。</summary>
        public void Log(string message)
        {
            Debug.Log(LogPrefix + message);
        }

        /// <summary>ダイアログの補足行として出す（警告ではない）。</summary>
        public void Note(string message)
        {
            _notes.Add(message);
            Debug.Log(LogPrefix + message);
        }

        public void Warn(string message)
        {
            _warnings.Add(message);
            Debug.LogWarning(LogPrefix + message);
        }

        public void Error(string message)
        {
            _errors.Add(message);
            Debug.LogError(LogPrefix + message);
        }

        // ================================================================
        // 出力
        // ================================================================

        /// <summary>一括エクスポートの1行サマリ用。</summary>
        public string BuildOneLineSummary()
        {
            if (_errors.Count > 0)   return $"エラー {_errors.Count} / 警告 {_warnings.Count}";
            if (_warnings.Count > 0) return $"警告 {_warnings.Count}";
            return "問題なし";
        }

        /// <summary>ダイアログ本文を組み立てる。</summary>
        public string BuildDialogText(string header)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(header)) sb.AppendLine(header).AppendLine();

            sb.AppendLine($"出力ノード: {ExportedNodeCount}　ボーン: {BoneCount}");

            if (SupplementedAncestorCount > 0)
                sb.AppendLine($"不可視の親を補完: {SupplementedAncestorCount}（Transform のみ）");

            if (SkippedInvisibleCount > 0)
                sb.AppendLine($"不可視でスキップ: {SkippedInvisibleCount}");

            if (SupplementedJointCount > 0)
                sb.AppendLine($"不足する必須関節を補完: {SupplementedJointCount}（ダミー）");

            if (!string.IsNullOrEmpty(AvatarResult))
                sb.AppendLine($"Avatar: {AvatarResult}");

            AppendSection(sb, "エラー", _errors);
            AppendSection(sb, "警告",   _warnings);
            AppendSection(sb, "補足",   _notes);

            if (!HasProblem)
            {
                sb.AppendLine().Append("警告・エラーはありません。");
            }
            else if (!string.IsNullOrEmpty(LogFilePath))
            {
                sb.AppendLine().AppendLine("詳細は次のファイルを確認してください:");
                sb.Append(LogFilePath);
            }
            else
            {
                sb.AppendLine().Append("詳細はコンソールを確認してください。");
            }

            return sb.ToString();
        }

        /// <summary>
        /// エクスポート先へ残すテキスト。ダイアログ用と違い省略しない。
        ///   ・警告・エラー・補足を全件、改行もそのまま出す
        ///   ・コンソールを追わなくても後から原因を追えるようにするのが目的
        /// </summary>
        public string BuildLogText(string header, string modelName)
        {
            var sb = new StringBuilder();

            sb.AppendLine("PolyLing Hierarchy Export Report");
            sb.AppendLine("================================");
            sb.AppendLine($"日時    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"モデル  : {(string.IsNullOrEmpty(modelName) ? "(名称なし)" : modelName)}");
            if (!string.IsNullOrEmpty(header)) sb.AppendLine($"結果    : {header}");
            sb.AppendLine($"集計    : エラー {_errors.Count} / 警告 {_warnings.Count} / 補足 {_notes.Count}");
            sb.AppendLine();

            sb.AppendLine($"出力ノード                  : {ExportedNodeCount}");
            sb.AppendLine($"ボーン                      : {BoneCount}");
            sb.AppendLine($"不可視の親を補完            : {SupplementedAncestorCount}");
            sb.AppendLine($"不可視でスキップ            : {SkippedInvisibleCount}");
            sb.AppendLine($"不足する必須関節を補完      : {SupplementedJointCount}");
            sb.AppendLine($"Avatar                      : {(string.IsNullOrEmpty(AvatarResult) ? "(未実行)" : AvatarResult)}");

            AppendFullSection(sb, "エラー", _errors);
            AppendFullSection(sb, "警告",   _warnings);
            AppendFullSection(sb, "補足",   _notes);

            if (!HasProblem)
            {
                sb.AppendLine();
                sb.AppendLine("警告・エラーはありません。");
            }

            return sb.ToString();
        }

        /// <summary>全件・全行を番号付きで出す（打ち切り・切り詰めをしない）。</summary>
        private static void AppendFullSection(StringBuilder sb, string title, List<string> lines)
        {
            if (lines.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine($"── {title} ({lines.Count}) ──");

            for (int i = 0; i < lines.Count; i++)
            {
                string body = lines[i] ?? "";
                string[] rows = body.Replace("\r\n", "\n").Split('\n');

                sb.AppendLine($"[{i + 1}] {rows[0]}");
                for (int r = 1; r < rows.Length; r++)
                    sb.AppendLine("    " + rows[r]);
            }
        }

        private static void AppendSection(StringBuilder sb, string title, List<string> lines)
        {
            if (lines.Count == 0) return;

            sb.AppendLine().AppendLine($"── {title} ({lines.Count}) ──");

            int shown = Mathf.Min(lines.Count, MaxListedLines);
            for (int i = 0; i < shown; i++)
                sb.AppendLine("・" + FirstLine(lines[i]));

            if (lines.Count > shown)
                sb.AppendLine($"…他 {lines.Count - shown} 件（コンソール参照）");
        }

        /// <summary>ダイアログが縦に伸びないよう、複数行のメッセージは1行目だけ出す。</summary>
        private static string FirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";

            int nl = message.IndexOf('\n');
            if (nl < 0) return message;

            return message.Substring(0, nl).TrimEnd() + " …";
        }
    }
}
