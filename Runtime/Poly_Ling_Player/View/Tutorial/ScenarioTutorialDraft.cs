// ScenarioTutorialDraft.cs
// シナリオからチュートリアルの原稿の下書き（MD）を作る。exportScenarioTutorialDraft の本体。
// Runtime/Poly_Ling_Player/View/Tutorial/ に配置
//
// 【正本】PolyLing_チュートリアル計画.md 3.4・4.2・段 5
//
// 【何を書くか】
//   項目ごとの理由（Purpose）、画面の操作（差し込みと撮影の指定）、経路の有無。呼ばれたシナリオも展開する。
//   そのまま buildScenarioTutorial の原稿にできる。LLM はこれを読者向けの文章に直す。
//   画面の文字は書かない（差し込みが読む）。ここでは流さないので、値も読まない。
//
// 【本文に出さないもの】
//   コマンド名、引数名、項目 ID。項目 ID は差し込みと撮影の指定の中にだけ現れる。
//   経路の有無は読者に見えない HTML のコメントで書く。LLM 向けの細目は結果の gaps で返す。
//
// 【同じシナリオを 2 度呼ぶ】
//   差し込みと撮影は最初の通過で読むので、2 度目は展開せずコメントで示す。
//
// 【置き換えを次の手順へまとめる】
//   人の操作への置き換えの項目のボタンが、次の項目の経路にもあるときは、次の手順の中に書く
//   （空のモデルの項目と読み込みの項目は、画面では読み込み 1 回で済む）。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public sealed class ScenarioTutorialDraft
    {
        public string Markdown = "";
        public string Error;
        public int ItemCount, RoutedCount, ManualCount, NoRouteCount;
        public readonly List<string> Gaps = new List<string>();

        private sealed class Entry
        {
            public string Scenario = "";
            public ModifierStackStep Step;
            public UiRoute Route;
            public PLUiManualAttribute Manual;
            public Type CommandType;
            public string Ref = "";
            /// <summary>地の文（呼び出しの見出し、2 度目の呼び出し）。Step が null のとき。</summary>
            public string Plain;
        }

        public static ScenarioTutorialDraft Build(ModifierStack root)
        {
            var draft   = new ScenarioTutorialDraft();
            var entries = new List<Entry>();
            var seen    = new HashSet<string>(StringComparer.Ordinal);
            UiRoute prev = null;
            draft.Flatten(root, root.Name ?? "", root.Name ?? "", new List<string>(), seen, entries, ref prev);
            if (draft.Error != null) return draft;

            var sb = new StringBuilder();
            sb.Append("# ").Append(root.Name ?? "").Append("\n\n");
            if (!string.IsNullOrEmpty(root.Goal)) sb.Append(root.Goal).Append("\n\n");
            AppendBullets(sb, "始める前に", root.Preconditions);

            sb.Append("## 手順\n\n");
            draft.WriteSteps(sb, entries);

            AppendBullets(sb, "できあがりの確かめ方", root.SuccessCriteria);
            draft.Markdown = sb.ToString();
            return draft;
        }

        private void Flatten(
            ModifierStack group, string name, string rootName, List<string> stack, HashSet<string> seen,
            List<Entry> entries, ref UiRoute prev)
        {
            if (group?.Steps == null) return;
            stack.Add(name);
            foreach (var step in group.Steps)
            {
                if (step == null) continue;
                string id = string.Equals(name, rootName, StringComparison.Ordinal)
                    ? step.ScenarioItemId : $"{name}:{step.ScenarioItemId}";

                if (step.IsScenarioRef)
                {
                    string refName = step.RefName ?? "";
                    var child = ScenarioLibrary.Get(refName);
                    if (child == null) { Error = $"{id}: 呼ぶシナリオがありません: {refName}"; return; }
                    if (stack.Contains(refName)) { Error = $"{id}: 呼び出しが循環しています: {refName}"; return; }

                    if (!string.IsNullOrEmpty(step.Purpose)) entries.Add(new Entry { Plain = step.Purpose + "\n\n" });
                    if (!seen.Add(refName))
                    {
                        entries.Add(new Entry { Plain = "<!-- 上と同じシナリオをもう一度呼ぶ。画面の操作は最初の通過で書いた -->\n\n" });
                        continue;
                    }
                    if (!string.IsNullOrEmpty(child.Goal)) entries.Add(new Entry { Plain = child.Goal + "\n\n" });
                    Flatten(child, refName, rootName, stack, seen, entries, ref prev);
                    if (Error != null) return;
                    continue;
                }

                var e = new Entry { Scenario = name, Step = step, Ref = id };
                if (step.IsExecutable)
                {
                    e.CommandType = PanelCommandFactory.ResolveType(step.Action);
                    e.Route  = UiRouteCatalog.Choose(e.CommandType, prev);
                    e.Manual = e.CommandType?.GetCustomAttribute<PLUiManualAttribute>(inherit: false);
                    if (e.Route != null) prev = e.Route;
                }
                entries.Add(e);
            }
            stack.RemoveAt(stack.Count - 1);
        }

        private void WriteSteps(StringBuilder sb, List<Entry> entries)
        {
            int number = 0;
            var merged = new List<Entry>();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Step == null) { sb.Append(e.Plain); continue; }

                if (!e.Step.IsExecutable)
                {
                    string head = e.Step.Kind == ModifierStackStepKind.Observe ? "確かめる"
                                : e.Step.Kind == ModifierStackStepKind.Instruction ? "指示"
                                : "注意";
                    sb.Append("> ").Append(head).Append("：").Append(e.Step.Purpose ?? "").Append("\n\n");
                    continue;
                }

                ItemCount++;
                bool routed = e.Route != null && e.Route.Items.Length > 0;

                // 置き換えの項目は、ボタンが次の項目の経路にもあれば次の手順へまとめる。
                if (!routed && e.Manual != null)
                {
                    ManualCount++;
                    if (OverlapsNext(entries, i, e.Manual))
                    {
                        merged.Add(e);
                        continue;
                    }
                }

                number++;
                string purpose = string.IsNullOrEmpty(e.Step.Purpose) ? "（理由が書かれていない）" : e.Step.Purpose;
                sb.Append("### ").Append(number).Append(". ").Append(purpose).Append("\n\n");
                foreach (var m in merged)
                    if (!string.IsNullOrEmpty(m.Step.Purpose)) sb.Append(m.Step.Purpose).Append("\n\n");

                if (routed) WriteRouted(sb, e, number, merged);
                else if (e.Manual != null) WriteManual(sb, e, number);
                else
                {
                    NoRouteCount++;
                    Gaps.Add($"{e.Ref}: {e.Step.Action} に画面の経路の宣言がない");
                    sb.Append("<!-- 経路: なし。画面の操作の宣言が無いので、手で書くか宣言を足す -->\n\n");
                }
                merged.Clear();
            }
        }

        private static bool OverlapsNext(List<Entry> entries, int index, PLUiManualAttribute manual)
        {
            for (int k = index + 1; k < entries.Count; k++)
            {
                var n = entries[k];
                if (n.Step == null || !n.Step.IsExecutable) continue;
                if (n.Route == null) return false;
                foreach (var a in manual.Items)
                    if (Array.IndexOf(n.Route.Items, a) >= 0 && a.Contains(".") && !a.Contains(".fold.")) return true;
                return false;
            }
            return false;
        }

        private void WriteRouted(StringBuilder sb, Entry e, int number, List<Entry> merged)
        {
            RoutedCount++;
            var route = e.Route;
            sb.Append("<!-- 経路: あり（").Append(CommentText(route.Name)).Append("） -->\n");
            if (!string.IsNullOrEmpty(route.Note))
                sb.Append("<!-- 補足: ").Append(CommentText(route.Note)).Append(" -->\n");
            foreach (var m in merged)
                sb.Append("<!-- 前の項目を人の操作に置き換えてここへまとめた: ").Append(CommentText(m.Manual.Note)).Append(" -->\n");
            sb.Append('\n');

            sb.Append("[[route:").Append(e.Ref).Append("]] を開きます。\n\n");
            sb.Append("![手順 ").Append(number).Append(" の操作の場所](shot:").Append(ShotRef(e)).Append("?when=before)\n\n");

            var missing = e.CommandType != null ? PanelCommandFactoryAudit.UndeclaredUiArgs(e.CommandType) : new List<string>();
            foreach (var m in merged) sb.Append("[[args:").Append(m.Ref).Append("]]\n\n");
            if (missing.Count == 0)
                sb.Append("次のように設定します。\n\n[[args:").Append(e.Ref).Append("]]\n\n");
            else
            {
                Gaps.Add($"{e.Ref}: 入力欄の宣言が足りない: {string.Join(", ", missing)}");
                sb.Append("<!-- 入力欄の宣言が足りない。手で書くか宣言を足す -->\n\n");
            }

            if (!string.IsNullOrEmpty(route.Run))
                sb.Append("[[run:").Append(e.Ref).Append("]] を押します。\n\n");
            else
            {
                Gaps.Add($"{e.Ref}: 実行のボタンの宣言がない");
                sb.Append("<!-- 実行のボタンの宣言が無い。手で書くか宣言を足す -->\n\n");
            }

            sb.Append("![手順 ").Append(number).Append(" の結果](shot:").Append(ShotRef(e)).Append("?when=after)\n\n");
        }

        private static void WriteManual(StringBuilder sb, Entry e, int number)
        {
            sb.Append("<!-- 経路: 人の操作への置き換え（").Append(CommentText(e.Manual.Name)).Append("） -->\n\n");
            if (!string.IsNullOrEmpty(e.Manual.Note)) sb.Append(e.Manual.Note).Append("\n\n");
            sb.Append("[[route:").Append(e.Ref).Append("]]\n\n");
            sb.Append("[[args:").Append(e.Ref).Append("]]\n\n");
            sb.Append("![手順 ").Append(number).Append(" の結果](shot:").Append(ShotRef(e)).Append("?when=after&target=Window)\n\n");
        }

        /// <summary>shot: の項目の指定。シナリオ名は URL の形に直す（括弧や空白を含んでよい）。</summary>
        private static string ShotRef(Entry e)
        {
            int colon = e.Ref.LastIndexOf(':');
            return colon < 0 ? e.Ref : Uri.EscapeDataString(e.Ref.Substring(0, colon)) + e.Ref.Substring(colon);
        }

        private static string CommentText(string s) => (s ?? "").Replace("--", "－");

        private static void AppendBullets(StringBuilder sb, string title, List<string> lines)
        {
            if (lines == null || lines.Count == 0) return;
            sb.Append("## ").Append(title).Append("\n\n");
            foreach (var l in lines) sb.Append("- ").Append(l).Append('\n');
            sb.Append('\n');
        }
    }
}
