// ScenarioTutorialManuscript.cs
// チュートリアルの原稿（MD）の解釈。画像の指定を取り出し、残りの文字はそのまま持つ。
// Runtime/Poly_Ling_Player/View/Tutorial/ に配置
//
// 【正本】PolyLing_チュートリアル計画.md 3.3
//
// 【変えないもの】
//   http(s) のリンク、既に data: のリンク、コードブロックの中（フェンス・字下げ）とインラインコードの中。
//   原稿は文字を足さずに持ち、画像と差し込み（[[route:]]・[[args:]]・[[run:]]）の所だけを差し替えるので、
//   それ以外の文字は 1 文字も変わらない。
//
// 【字下げのコードブロック】
//   リストの続きの字下げと区別する。リストの中の字下げはコードブロックとして扱わない。
//
// 【断る】
//   読めない指定は黙って落とさず、問題として全部返す（綴り違いの指定が無視されると、違う画像ができる）。
//   ここは文字だけを見る。項目やパネルの存在は呼ぶ側（Viewer）が確かめる。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Poly_Ling.Player
{
    public enum TutorialShotWhen { Before, After }

    /// <summary>矢印の指定。Default は省略で、when と項目の種類から決まる。</summary>
    public enum TutorialCursorKind { Default, None, Route, Run, Control, Position, Vertex }

    /// <summary>解決できなかった指定 1 つ。</summary>
    public readonly struct TutorialProblem
    {
        /// <summary>項目 ID か画像のパス。</summary>
        public readonly string Item;
        public readonly string Reason;

        public TutorialProblem(string item, string reason)
        {
            Item   = item ?? "";
            Reason = reason ?? "";
        }
    }

    /// <summary>原稿の shot: 1 つ。</summary>
    public sealed class TutorialShot
    {
        /// <summary>原稿での順番。1 始まり。画像の名前に使う。</summary>
        public int Order;

        public string Alt = "";

        /// <summary>原稿に書かれた項目の指定（e1 や シナリオ名:e1）。問題の報告に使う。</summary>
        public string ItemRef = "";

        /// <summary>シナリオ名。省略なら空（呼ぶ側が流すシナリオの名前を入れる）。</summary>
        public string Scenario = "";

        public string ItemId = "";
        public TutorialShotWhen When = TutorialShotWhen.After;

        /// <summary>撮影範囲。省略なら空。</summary>
        public string Target = "";

        /// <summary>画面に収めるオブジェクトの名前。省略なら空。</summary>
        public string Fit = "";

        public TutorialCursorKind Cursor = TutorialCursorKind.Default;
        public string CursorControl = "";
        public float  CursorX, CursorY;
        public string CursorObject = "";
        public int    CursorVertex;

        /// <summary>省略なら -1。</summary>
        public int MaxLongEdge = -1;

        /// <summary>省略なら空。</summary>
        public string Format = "";

        // ── 呼ぶ側が解決したもの ─────────────────────────────
        public string PanelId = "";
        public string CaptureTarget = "";
        public UiRoute Route;
        public string ImagePath = "";
    }

    /// <summary>原稿の普通の画像 1 つ。</summary>
    public sealed class TutorialImage
    {
        public string Alt = "";
        /// <summary>原稿に書かれたパス。問題の報告に使う。</summary>
        public string Dest = "";
        /// <summary>解釈したパス（まだ PLSandbox を通していない）。</summary>
        public string Path = "";
        /// <summary>タイトルの部分（前の空白を含む）。無ければ空。出力へそのまま写す。</summary>
        public string TitleSuffix = "";
        /// <summary>解釈の時点で問題に入れた。呼ぶ側はファイルを探さない。</summary>
        public bool Invalid;
        /// <summary>PLSandbox を通した実パス。呼ぶ側が入れる。</summary>
        public string FullPath = "";
    }

    public enum TutorialInsertKind { Route, Args, Run }

    /// <summary>原稿の差し込み 1 つ（[[route:e4]]・[[args:e4]]・[[run:e4]]）。</summary>
    public sealed class TutorialInsert
    {
        public TutorialInsertKind Kind;
        /// <summary>原稿に書かれた項目の指定（e4 や シナリオ名:e4）。問題の報告に使う。</summary>
        public string ItemRef = "";
        /// <summary>シナリオ名。省略なら空（呼ぶ側が流すシナリオの名前を入れる）。</summary>
        public string Scenario = "";
        public string ItemId = "";
        /// <summary>置き換わる文字。流しの中で呼ぶ側が入れる。null はまだ読んでいない。</summary>
        public string Text;
    }

    /// <summary>原稿のひと続き。文字か、撮影か、普通の画像か、差し込みのどれか。</summary>
    public sealed class TutorialSegment
    {
        public string         Text;
        public TutorialShot   Shot;
        public TutorialImage  Image;
        public TutorialInsert Insert;
    }

    public sealed class ScenarioTutorialManuscript
    {
        public readonly List<TutorialSegment> Segments = new List<TutorialSegment>();
        public readonly List<TutorialShot>    Shots    = new List<TutorialShot>();
        public readonly List<TutorialImage>   Images   = new List<TutorialImage>();
        public readonly List<TutorialInsert>  Inserts  = new List<TutorialInsert>();
        public readonly List<TutorialProblem> Problems = new List<TutorialProblem>();

        private static readonly Regex ImageRx =
            new Regex(@"!\[([^\]\r\n]*)\]\(([^)\r\n]*)\)", RegexOptions.CultureInvariant);

        private static readonly Regex InsertRx =
            new Regex(@"\[\[(route|args|run):([^\]\r\n]*)\]\]", RegexOptions.CultureInvariant);

        private static readonly Regex ListMarkerRx =
            new Regex(@"^ {0,3}([-*+]|\d+[.)])\s", RegexOptions.CultureInvariant);

        private static readonly Regex FenceRx =
            new Regex(@"^ {0,3}(`{3,}|~{3,})(.*)$", RegexOptions.CultureInvariant);

        /// <summary>2 文字以上のスキーム。1 文字は Windows のドライブ名なので当てない。</summary>
        private static readonly Regex SchemeRx =
            new Regex(@"^[a-zA-Z][a-zA-Z0-9+.\-]+:", RegexOptions.CultureInvariant);

        private static readonly Regex TitleRx =
            new Regex(@"^(.*?)(\s+""[^""]*"")$", RegexOptions.CultureInvariant);

        // ================================================================
        // 解釈
        // ================================================================

        public static ScenarioTutorialManuscript Parse(string text)
        {
            var m = new ScenarioTutorialManuscript();
            text = text ?? "";

            var literal = new StringBuilder();
            bool inFence = false, prevBlank = true, inList = false, inIndented = false;
            char fenceChar = '\0';
            int  fenceLen  = 0;

            int pos = 0;
            while (pos < text.Length)
            {
                int nl  = text.IndexOf('\n', pos);
                int end = nl < 0 ? text.Length : nl + 1;
                string line = text.Substring(pos, end - pos);
                pos = end;

                string content = line.TrimEnd('\r', '\n');

                // ── フェンス ──
                var fence = FenceRx.Match(content);
                if (inFence)
                {
                    if (fence.Success && fence.Groups[1].Value[0] == fenceChar
                        && fence.Groups[1].Length >= fenceLen && fence.Groups[2].Value.Trim().Length == 0)
                        inFence = false;
                    literal.Append(line);
                    continue;
                }
                if (fence.Success)
                {
                    inFence   = true;
                    fenceChar = fence.Groups[1].Value[0];
                    fenceLen  = fence.Groups[1].Length;
                    literal.Append(line);
                    prevBlank = false;
                    inIndented = false;
                    continue;
                }

                // ── 字下げのコードブロック ──
                if (content.Trim().Length == 0)
                {
                    prevBlank = true;
                    literal.Append(line);
                    continue;
                }

                bool indented = content.StartsWith("    ", StringComparison.Ordinal) || content[0] == '\t';
                if (ListMarkerRx.IsMatch(content)) inList = true;
                else if (!indented && prevBlank)   inList = false;

                if (indented && !inList && (prevBlank || inIndented))
                {
                    inIndented = true;
                    prevBlank  = false;
                    literal.Append(line);
                    continue;
                }
                inIndented = false;
                prevBlank  = false;

                m.ParseLine(line, literal);
            }

            if (literal.Length > 0) m.Segments.Add(new TutorialSegment { Text = literal.ToString() });
            return m;
        }

        private void ParseLine(string line, StringBuilder literal)
        {
            var codeSpans = InlineCodeSpans(line);

            // 画像と差し込みを行の順に並べる。画像の説明の中の差し込みは画像の側で扱う（重なりは飛ばす）。
            var matches = new List<Match>();
            foreach (Match x in ImageRx.Matches(line))  matches.Add(x);
            foreach (Match x in InsertRx.Matches(line)) matches.Add(x);
            matches.Sort((a, b) => a.Index.CompareTo(b.Index));

            int cursor = 0;
            foreach (var match in matches)
            {
                if (match.Index < cursor) continue;
                if (IsInside(codeSpans, match.Index)) continue;
                if (match.Index > 0 && line[match.Index - 1] == '\\') continue;

                var seg = new TutorialSegment();
                if (match.Value.StartsWith("[[", StringComparison.Ordinal))
                {
                    seg.Insert = ParseInsert(match.Groups[1].Value, match.Groups[2].Value.Trim());
                    Inserts.Add(seg.Insert);
                }
                else
                {
                    string alt  = match.Groups[1].Value;
                    string dest = match.Groups[2].Value.Trim();
                    if (IsLeftAlone(dest)) continue;

                    if (dest.StartsWith("shot:", StringComparison.Ordinal))
                    {
                        seg.Shot = ParseShot(alt, dest);
                        Shots.Add(seg.Shot);
                    }
                    else
                    {
                        seg.Image = ParseImage(alt, dest);
                        Images.Add(seg.Image);
                    }
                }

                literal.Append(line, cursor, match.Index - cursor);
                cursor = match.Index + match.Length;

                if (literal.Length > 0)
                {
                    Segments.Add(new TutorialSegment { Text = literal.ToString() });
                    literal.Clear();
                }
                Segments.Add(seg);
            }
            literal.Append(line, cursor, line.Length - cursor);
        }

        private static bool IsLeftAlone(string dest)
            => dest.StartsWith("http://",  StringComparison.OrdinalIgnoreCase)
            || dest.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || dest.StartsWith("data:",    StringComparison.OrdinalIgnoreCase);

        /// <summary>行のインラインコードの範囲。開始と終了の組み。</summary>
        private static List<(int Start, int End)> InlineCodeSpans(string line)
        {
            var spans = new List<(int, int)>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '\\') { i += 2; continue; }
                if (line[i] != '`')  { i++; continue; }

                int n = 0;
                while (i + n < line.Length && line[i + n] == '`') n++;

                int close = -1;
                for (int j = i + n; j < line.Length; j++)
                {
                    if (line[j] != '`') continue;
                    int k = 0;
                    while (j + k < line.Length && line[j + k] == '`') k++;
                    if (k == n) { close = j; break; }
                    j += k - 1;
                }

                if (close < 0) { i += n; continue; }
                spans.Add((i, close + n));
                i = close + n;
            }
            return spans;
        }

        private static bool IsInside(List<(int Start, int End)> spans, int index)
        {
            foreach (var s in spans)
                if (index >= s.Start && index < s.End) return true;
            return false;
        }

        // ================================================================
        // 差し込み
        // ================================================================

        private TutorialInsert ParseInsert(string kind, string itemRef)
        {
            var insert = new TutorialInsert
            {
                ItemRef = itemRef,
                Kind = kind == "route" ? TutorialInsertKind.Route
                     : kind == "args"  ? TutorialInsertKind.Args
                     : TutorialInsertKind.Run,
            };

            int colon = itemRef.LastIndexOf(':');
            if (colon >= 0)
            {
                insert.Scenario = itemRef.Substring(0, colon);
                insert.ItemId   = itemRef.Substring(colon + 1);
            }
            else insert.ItemId = itemRef;

            if (insert.ItemId.Length == 0)
                Problems.Add(new TutorialProblem(itemRef, $"差し込みの項目 ID がありません: [[{kind}:{itemRef}]]"));
            if (colon >= 0 && insert.Scenario.Length == 0)
                Problems.Add(new TutorialProblem(itemRef, $"差し込みのシナリオ名が空です: [[{kind}:{itemRef}]]"));
            return insert;
        }

        // ================================================================
        // shot:
        // ================================================================

        private TutorialShot ParseShot(string alt, string dest)
        {
            var shot = new TutorialShot { Alt = alt, Order = Shots.Count + 1 };

            string body = dest.Substring("shot:".Length);
            int q = body.IndexOf('?');
            string refText = Uri.UnescapeDataString(q < 0 ? body : body.Substring(0, q)).Trim();
            string query   = q < 0 ? "" : body.Substring(q + 1);

            shot.ItemRef = refText;
            int colon = refText.LastIndexOf(':');
            if (colon >= 0)
            {
                shot.Scenario = refText.Substring(0, colon);
                shot.ItemId   = refText.Substring(colon + 1);
            }
            else shot.ItemId = refText;

            if (shot.ItemId.Length == 0) Problem(shot, $"項目 ID がありません: {dest}");
            if (colon >= 0 && shot.Scenario.Length == 0) Problem(shot, $"シナリオ名が空です: {dest}");

            foreach (var part in query.Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string key   = eq < 0 ? part : part.Substring(0, eq);
                string value = eq < 0 ? ""   : Uri.UnescapeDataString(part.Substring(eq + 1));

                switch (key)
                {
                    case "when":
                        if (value == "before")     shot.When = TutorialShotWhen.Before;
                        else if (value == "after") shot.When = TutorialShotWhen.After;
                        else Problem(shot, $"when は before か after です: {value}");
                        break;
                    case "target":
                        shot.Target = value;
                        if (value.Length == 0) Problem(shot, "target が空です");
                        break;
                    case "fit":
                        shot.Fit = value;
                        if (value.Length == 0) Problem(shot, "fit が空です");
                        break;
                    case "cursor":
                        ParseCursor(shot, value);
                        break;
                    case "maxLongEdge":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int edge) && edge >= 0)
                            shot.MaxLongEdge = edge;
                        else Problem(shot, $"maxLongEdge は 0 以上の整数です: {value}");
                        break;
                    case "format":
                        shot.Format = value;
                        if (!IsKnownFormat(value)) Problem(shot, $"format は png か jpeg です: {value}");
                        break;
                    default:
                        Problem(shot, $"知らない指定です: {key}（when / target / fit / cursor / maxLongEdge / format）");
                        break;
                }
            }
            return shot;
        }

        public static bool IsKnownFormat(string format)
        {
            string f = (format ?? "").Trim().ToLowerInvariant();
            return f == "png" || f == "jpeg" || f == "jpg";
        }

        private void ParseCursor(TutorialShot shot, string value)
        {
            if (value == "none")  { shot.Cursor = TutorialCursorKind.None;  return; }
            if (value == "route") { shot.Cursor = TutorialCursorKind.Route; return; }
            if (value == "run")   { shot.Cursor = TutorialCursorKind.Run;   return; }

            if (value.StartsWith("control:", StringComparison.Ordinal))
            {
                shot.Cursor        = TutorialCursorKind.Control;
                shot.CursorControl = value.Substring("control:".Length);
                if (shot.CursorControl.Length == 0) Problem(shot, "cursor=control: の ID が空です");
                return;
            }

            if (value.StartsWith("position:", StringComparison.Ordinal))
            {
                shot.Cursor = TutorialCursorKind.Position;
                var p = value.Substring("position:".Length).Split(',');
                if (p.Length == 2
                    && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && x >= 0f && x <= 1f && y >= 0f && y <= 1f)
                {
                    shot.CursorX = x;
                    shot.CursorY = y;
                }
                else Problem(shot, $"cursor=position は 0〜1 の x,y です: {value}");
                return;
            }

            if (value.StartsWith("vertex:", StringComparison.Ordinal))
            {
                shot.Cursor = TutorialCursorKind.Vertex;
                string v = value.Substring("vertex:".Length);
                int comma = v.LastIndexOf(',');
                if (comma > 0 && int.TryParse(v.Substring(comma + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int vertex) && vertex >= 0)
                {
                    shot.CursorObject = v.Substring(0, comma);
                    shot.CursorVertex = vertex;
                }
                else Problem(shot, $"cursor=vertex はオブジェクト名,頂点番号です: {value}");
                return;
            }

            Problem(shot, $"知らない cursor です: {value}（none / route / run / control:ID / position:x,y / vertex:オブジェクト名,頂点番号）");
        }

        private void Problem(TutorialShot shot, string reason)
            => Problems.Add(new TutorialProblem(shot.ItemRef, reason));

        // ================================================================
        // 普通の画像
        // ================================================================

        private TutorialImage ParseImage(string alt, string dest)
        {
            var image = new TutorialImage { Alt = alt, Dest = dest };
            int problemsBefore = Problems.Count;

            string path = dest;
            var title = TitleRx.Match(path);
            if (title.Success)
            {
                path = title.Groups[1].Value;
                image.TitleSuffix = title.Groups[2].Value;
            }
            if (path.Length >= 2 && path[0] == '<' && path[path.Length - 1] == '>')
                path = path.Substring(1, path.Length - 2);

            if (path.Length == 0)
                Problems.Add(new TutorialProblem(dest, "画像のパスが空です"));
            else if (SchemeRx.IsMatch(path))
                Problems.Add(new TutorialProblem(dest, "埋め込めないリンクです（ファイルのパス・http(s)・data: だけを扱います）"));
            else if (path[0] == '#')
                Problems.Add(new TutorialProblem(dest, "画像のパスではありません"));
            else if (MimeOf(path) == null)
                Problems.Add(new TutorialProblem(dest, "画像の形式が分かりません（.png / .jpg / .jpeg / .gif / .webp / .svg）"));

            image.Invalid = problemsBefore != Problems.Count;
            image.Path = Uri.UnescapeDataString(path);
            return image;
        }

        /// <summary>拡張子から MIME を返す。埋め込まない形式は null。</summary>
        public static string MimeOf(string path)
        {
            switch ((System.IO.Path.GetExtension(path) ?? "").ToLowerInvariant())
            {
                case ".png":  return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".gif":  return "image/gif";
                case ".webp": return "image/webp";
                case ".svg":  return "image/svg+xml";
                default:      return null;
            }
        }
    }
}
