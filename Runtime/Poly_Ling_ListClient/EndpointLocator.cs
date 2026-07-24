// EndpointLocator.cs
// サーバが公開する endpoint.json を探索し、接続先(host, port)を取得する。
// 保存先(サーバ側): Application.persistentDataPath/PolyLing/endpoint.json
//   = %LocalLow%/HagiharaLab/PolyLing/PolyLing/endpoint.json (company=HagiharaLab, product=PolyLing 時)
// クライアントプロジェクトの company/product が異なる場合に備え、
// persistentDataPath 経由が空振りしたら固定の LocalLow パスもフォールバックで探索する。

using System;
using System.IO;
using UnityEngine;

namespace Poly_Ling.ListClient
{
    public static class EndpointLocator
    {
        /// <summary>相対配置: &lt;dir&gt;/PolyLing/endpoint.json</summary>
        private const string SubDir   = "PolyLing";
        private const string FileName = "endpoint.json";

        /// <summary>
        /// endpoint.json を探索して host/port を返す。見つからなければ false。
        /// </summary>
        public static bool TryLocate(out string host, out int port, out string foundPath)
        {
            host = null;
            port = 0;
            foundPath = null;

            foreach (var path in CandidatePaths())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string json = File.ReadAllText(path);
                    string h = ExtractString(json, "host");
                    int    p = ExtractInt(json, "port");
                    if (!string.IsNullOrEmpty(h) && p > 0)
                    {
                        host = h;
                        port = p;
                        foundPath = path;
                        return true;
                    }
                }
                catch
                {
                    // 読み取り失敗は次候補へ
                }
            }
            return false;
        }

        // ================================================================
        // 探索候補
        // ================================================================

        private static System.Collections.Generic.IEnumerable<string> CandidatePaths()
        {
            // 1) 自プロジェクトの persistentDataPath 直下（company/product が一致する場合）
            yield return Path.Combine(Application.persistentDataPath, SubDir, FileName);

            // 2) 固定 LocalLow パス（Windows）。company/product 不一致でもサーバ書込先に一致させる。
            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                yield return Path.Combine(
                    userProfile, "AppData", "LocalLow",
                    "HagiharaLab", "PolyLing", "PolyLing", FileName);
            }
        }

        // ================================================================
        // 極小 JSON 抽出（endpoint.json は既知フォーマットのため軽量抽出で足りる）
        // ================================================================

        private static string ExtractString(string json, string key)
        {
            int vs = ValueStart(json, key);
            if (vs < 0 || vs >= json.Length || json[vs] != '"') return null;
            int ve = json.IndexOf('"', vs + 1);
            if (ve < 0) return null;
            return json.Substring(vs + 1, ve - vs - 1);
        }

        private static int ExtractInt(string json, string key)
        {
            int vs = ValueStart(json, key);
            if (vs < 0) return 0;
            int e = vs;
            while (e < json.Length && (char.IsDigit(json[e]) || json[e] == '-')) e++;
            if (e == vs) return 0;
            return int.TryParse(json.Substring(vs, e - vs), out int v) ? v : 0;
        }

        private static int ValueStart(string json, string key)
        {
            string s = "\"" + key + "\"";
            int i = json.IndexOf(s, StringComparison.Ordinal);
            if (i < 0) return -1;
            int c = json.IndexOf(':', i + s.Length);
            if (c < 0) return -1;
            int vs = c + 1;
            while (vs < json.Length && (json[vs] == ' ' || json[vs] == '\t')) vs++;
            return vs;
        }
    }
}
