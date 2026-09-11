// Remote/RemoteProtocol.cs
// リモートパネル通信プロトコル定義
// WebSocket上でJSON形式のメッセージを双方向でやり取りする
//
// クライアント→ホスト:
//   Query:   {"id":"xxx", "type":"query",   "target":"meshList", "fields":["Name","IsVisible",...]}
//   Command: {"id":"xxx", "type":"command", "action":"selectMesh", "params":{"index":2}}
//
// ホスト→クライアント:
//   Response: {"id":"xxx", "type":"response", "success":true, "data":{...}}
//   Push:     {"id":null,  "type":"push",    "event":"meshListChanged", "data":{...}}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Remote
{
    // ================================================================
    // メッセージ型
    // ================================================================

    /// <summary>
    /// クライアントからの受信メッセージ
    /// </summary>
    public class RemoteMessage
    {
        public string Id;       // リクエストID（応答の紐づけ用）
        public string Type;     // "query" | "command"
        public string Target;   // query対象: "meshList", "meshData", "modelInfo"
        public string Action;   // command種別: "selectMesh", "updateAttribute", ...
        public string[] Fields; // 取得フィールド名
        public Dictionary<string, string> Params; // コマンドパラメータ
    }

    /// <summary>
    /// ホストからの送信メッセージ
    /// </summary>
    public class RemoteResponse
    {
        public string Id;       // 対応するリクエストID（pushならnull）
        public string Type;     // "response" | "push"
        public bool Success;
        public string Event;    // push時のイベント名
        public string Data;     // JSONデータ文字列
        public string Error;    // エラーメッセージ
    }

    // ================================================================
    // 軽量JSONビルダー（外部依存なし）
    // ================================================================

    /// <summary>
    /// 簡易JSONビルダー
    /// Unity JsonUtilityでは辞書・動的構造に対応できないため自前実装
    /// </summary>
    public class JsonBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private bool _needsComma;
        private readonly Stack<char> _scopeStack = new Stack<char>();

        public JsonBuilder BeginObject()
        {
            AppendCommaIfNeeded();
            _sb.Append('{');
            _scopeStack.Push('}');
            _needsComma = false;
            return this;
        }

        public JsonBuilder EndObject()
        {
            _sb.Append(_scopeStack.Pop());
            _needsComma = true;
            return this;
        }

        public JsonBuilder BeginArray()
        {
            AppendCommaIfNeeded();
            _sb.Append('[');
            _scopeStack.Push(']');
            _needsComma = false;
            return this;
        }

        public JsonBuilder EndArray()
        {
            _sb.Append(_scopeStack.Pop());
            _needsComma = true;
            return this;
        }

        public JsonBuilder Key(string key)
        {
            AppendCommaIfNeeded();
            _sb.Append('"').Append(EscapeString(key)).Append("\":");
            _needsComma = false;
            return this;
        }

        public JsonBuilder Value(string val)
        {
            AppendCommaIfNeeded();
            if (val == null)
                _sb.Append("null");
            else
                _sb.Append('"').Append(EscapeString(val)).Append('"');
            _needsComma = true;
            return this;
        }

        public JsonBuilder Value(int val)
        {
            AppendCommaIfNeeded();
            _sb.Append(val);
            _needsComma = true;
            return this;
        }

        public JsonBuilder Value(float val)
        {
            AppendCommaIfNeeded();
            _sb.Append(val.ToString("G9", CultureInfo.InvariantCulture));
            _needsComma = true;
            return this;
        }

        public JsonBuilder Value(bool val)
        {
            AppendCommaIfNeeded();
            _sb.Append(val ? "true" : "false");
            _needsComma = true;
            return this;
        }

        /// <summary>既にフォーマット済みのJSON文字列をそのまま挿入</summary>
        public JsonBuilder RawValue(string rawJson)
        {
            AppendCommaIfNeeded();
            _sb.Append(rawJson);
            _needsComma = true;
            return this;
        }

        public JsonBuilder KeyValue(string key, string val) => Key(key).Value(val);
        public JsonBuilder KeyValue(string key, int val) => Key(key).Value(val);
        public JsonBuilder KeyValue(string key, float val) => Key(key).Value(val);
        public JsonBuilder KeyValue(string key, bool val) => Key(key).Value(val);
        public JsonBuilder KeyRaw(string key, string rawJson) => Key(key).RawValue(rawJson);

        public override string ToString() => _sb.ToString();

        private void AppendCommaIfNeeded()
        {
            if (_needsComma) _sb.Append(',');
        }

        private static string EscapeString(string s)
        {
            if (s == null) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    // ================================================================
    // 軽量JSONパーサー（受信メッセージ解析用）
    // ================================================================

    /// <summary>
    /// 最小限のJSONパーサー
    /// </summary>
    public static class JsonParser
    {
        public static RemoteMessage Parse(string json)
        {
            var msg = new RemoteMessage();
            var dict = ParseFlat(json);

            dict.TryGetValue("id", out msg.Id);
            dict.TryGetValue("type", out msg.Type);
            dict.TryGetValue("target", out msg.Target);
            dict.TryGetValue("action", out msg.Action);

            msg.Fields = ParseStringArray(json, "fields");
            msg.Params = ParseSubObject(json, "params");

            return msg;
        }

        private static Dictionary<string, string> ParseFlat(string json)
        {
            var result = new Dictionary<string, string>();
            int i = 0;
            int len = json.Length;

            while (i < len)
            {
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                int colon = json.IndexOf(':', keyEnd + 1);
                if (colon < 0) break;

                int valueStart = colon + 1;
                while (valueStart < len && json[valueStart] == ' ') valueStart++;
                if (valueStart >= len) break;

                char ch = json[valueStart];
                if (ch == '"')
                {
                    int valueEnd = FindClosingQuote(json, valueStart + 1);
                    if (valueEnd < 0) break;
                    result[key] = Unescape(json.Substring(valueStart + 1, valueEnd - valueStart - 1));
                    i = valueEnd + 1;
                }
                else if (ch == '[' || ch == '{')
                {
                    int depth = 1;
                    char close = ch == '[' ? ']' : '}';
                    int j = valueStart + 1;
                    while (j < len && depth > 0)
                    {
                        if (json[j] == ch) depth++;
                        else if (json[j] == close) depth--;
                        else if (json[j] == '"') j = FindClosingQuote(json, j + 1);
                        j++;
                    }
                    i = j;
                }
                else if (ch == 'n' && valueStart + 3 < len && json.Substring(valueStart, 4) == "null")
                {
                    result[key] = null;
                    i = valueStart + 4;
                }
                else
                {
                    int valueEnd = valueStart;
                    while (valueEnd < len && json[valueEnd] != ',' && json[valueEnd] != '}' && json[valueEnd] != ']')
                        valueEnd++;
                    result[key] = json.Substring(valueStart, valueEnd - valueStart).Trim();
                    i = valueEnd;
                }
            }
            return result;
        }

        private static string[] ParseStringArray(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIdx < 0) return null;

            int bracketStart = json.IndexOf('[', keyIdx + searchKey.Length);
            if (bracketStart < 0) return null;
            int bracketEnd = json.IndexOf(']', bracketStart);
            if (bracketEnd < 0) return null;

            string inner = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
            var list = new List<string>();
            int i = 0;
            while (i < inner.Length)
            {
                int qs = inner.IndexOf('"', i);
                if (qs < 0) break;
                int qe = inner.IndexOf('"', qs + 1);
                if (qe < 0) break;
                list.Add(Unescape(inner.Substring(qs + 1, qe - qs - 1)));
                i = qe + 1;
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        private static Dictionary<string, string> ParseSubObject(string json, string key)
        {
            string searchKey = $"\"{key}\"";
            int keyIdx = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIdx < 0) return new Dictionary<string, string>();

            int braceStart = json.IndexOf('{', keyIdx + searchKey.Length);
            if (braceStart < 0) return new Dictionary<string, string>();

            int depth = 1;
            int j = braceStart + 1;
            while (j < json.Length && depth > 0)
            {
                if (json[j] == '{') depth++;
                else if (json[j] == '}') depth--;
                else if (json[j] == '"') j = FindClosingQuote(json, j + 1);
                j++;
            }

            string sub = json.Substring(braceStart, j - braceStart);
            return ParseFlat(sub);
        }

        /// <summary>
        /// JSON 文字列の中身（引用符の内側）をエスケープ規則どおりに戻す。
        /// 対象は \" \\ \/ \b \f \n \r \t \uXXXX。
        ///
        /// 【なぜ要るか】
        ///   送り手は \ を \\ に、" を \" に置き換えて送る。MCP サーバ
        ///   （System.Text.Json）は日本語などの非 ASCII も \uXXXX にして送る。
        ///   戻さずに使うと、日本語のパスが "\u30C7…" という別の文字列になり、
        ///   PLSandbox で作業フォルダの外と判定される。
        ///
        /// 規則に無い並び（\ の後が上記以外、\u の後が 16 進 4 桁でない）は
        /// 変えずにそのまま残す。
        /// サロゲートペア（\uD83D\uDE00 など）は 2 文字をそのまま並べれば成立する。
        /// </summary>
        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;

            var sb = new StringBuilder(s.Length);
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                switch (s[i + 1])
                {
                    case '"':  sb.Append('"');  i += 2; break;
                    case '\\': sb.Append('\\'); i += 2; break;
                    case '/':  sb.Append('/');  i += 2; break;
                    case 'b':  sb.Append('\b'); i += 2; break;
                    case 'f':  sb.Append('\f'); i += 2; break;
                    case 'n':  sb.Append('\n'); i += 2; break;
                    case 'r':  sb.Append('\r'); i += 2; break;
                    case 't':  sb.Append('\t'); i += 2; break;
                    case 'u':
                        if (i + 6 <= s.Length && TryParseHex4(s, i + 2, out int code))
                        {
                            sb.Append((char)code);
                            i += 6;
                        }
                        else
                        {
                            sb.Append(c);
                            i++;
                        }
                        break;
                    default:
                        sb.Append(c);
                        i++;
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>s[start..start+3] を 16 進 4 桁として読む。1 桁でも外れたら false。</summary>
        private static bool TryParseHex4(string s, int start, out int value)
        {
            value = 0;
            for (int k = 0; k < 4; k++)
            {
                char ch = s[start + k];
                int d;
                if      (ch >= '0' && ch <= '9') d = ch - '0';
                else if (ch >= 'a' && ch <= 'f') d = ch - 'a' + 10;
                else if (ch >= 'A' && ch <= 'F') d = ch - 'A' + 10;
                else { value = 0; return false; }
                value = (value << 4) | d;
            }
            return true;
        }

        private static int FindClosingQuote(string s, int from)
        {
            for (int i = from; i < s.Length; i++)
            {
                if (s[i] == '\\') { i++; continue; }
                if (s[i] == '"') return i;
            }
            return s.Length - 1;
        }
    }
}
