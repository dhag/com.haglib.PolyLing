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
                    result[key] = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
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
                list.Add(inner.Substring(qs + 1, qe - qs - 1));
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
