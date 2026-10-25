// VoicevoxLipSync.cs
// ============================================================
// VOICEVOX エンジンの audio_query からモーラの時刻を求め、母音ごとの表情トラック（口パク）を作る。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。コマンド createLipSyncFromVoicevox が使う。
//
// ■ 時刻の求め方（VOICEVOX エンジンの合成と同じ並び）
//   先頭の無音（prePhonemeLength）→ 各アクセント句のモーラ（子音長＋母音長）→ 句の後の休止（pause_mora）→
//   末尾の無音（postPhonemeLength）。休止の長さは pauseLength があればそれ、無ければ pause_mora の母音長で、
//   pauseLengthScale を掛ける。すべての長さを speedScale で割る。
//
// ■ キーの打ち方
//   母音 a / i / u / e / o（無声化の大文字も同じ母音）のトラックは、その母音のモーラの母音区間の中央で weight、
//   ほかの区間（ほかの母音・ん・っ・休止・前後の無音）の中央で 0。間は線形。
//   前後の端（開始時刻・終了時刻）にも 0 を置く。0 が続くところの中間のキーは落とす。
//
// ■ クリップへの入れ方（MergeInto）
//   対象の 5 本のトラックについて、開始～終了の範囲にある既存のキーを消してから入れる。
//   新しいトラックは、その母音が 1 回も出ないなら作らない。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Poly_Ling.Motion
{
    public static class VoicevoxLipSync
    {
        /// <summary>母音の並び（トラックの並びと同じ）。</summary>
        public static readonly string[] Vowels = { "a", "i", "u", "e", "o" };

        /// <summary>1 区間（モーラ・休止・無音）。Vowel は "a"～"o" か null（口を閉じる区間）。</summary>
        public sealed class Segment
        {
            public string Text;
            public string Phoneme;   // VOICEVOX の母音の音素（a / I / N / cl / pau など）。無音は "sil"
            public string Vowel;     // a / i / u / e / o / null
            public float  Start;     // 区間の始まり（子音の始まり）
            public float  VowelStart;
            public float  End;
            public float  Center => (VowelStart + End) * 0.5f;
        }

        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// audio_query を呼び、応答の JSON を返す。失敗したら null で error に理由。
        /// 待つ間は呼び出し元（Unity の主スレッド）が止まる。
        /// </summary>
        public static string QueryAudio(string host, string text, int speaker, int timeoutMs, out string error)
        {
            error = null;
            string baseUrl = string.IsNullOrWhiteSpace(host) ? "http://127.0.0.1:50021" : host.Trim().TrimEnd('/');
            string url = $"{baseUrl}/audio_query?text={Uri.EscapeDataString(text ?? "")}&speaker={speaker.ToString(CultureInfo.InvariantCulture)}";
            try
            {
                var task = Task.Run(async () =>
                {
                    using (var req = new HttpRequestMessage(HttpMethod.Post, url))
                    {
                        req.Content = new StringContent("", Encoding.UTF8);
                        using (var res = await _http.SendAsync(req).ConfigureAwait(false))
                        {
                            string body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return (ok: res.IsSuccessStatusCode, code: (int)res.StatusCode, body);
                        }
                    }
                });
                if (!task.Wait(timeoutMs))
                {
                    error = $"VOICEVOX が {timeoutMs} ms 以内に応答しません（{baseUrl}）";
                    return null;
                }
                var r = task.Result;
                if (!r.ok)
                {
                    error = $"VOICEVOX がエラーを返しました（HTTP {r.code}）: {Clip(r.body, 300)}";
                    return null;
                }
                return r.body;
            }
            catch (AggregateException ex)
            {
                var inner = ex.GetBaseException();
                error = $"VOICEVOX に接続できません（{baseUrl}）: {inner.Message}";
                return null;
            }
            catch (Exception ex)
            {
                error = $"VOICEVOX に接続できません（{baseUrl}）: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// audio_query の応答から区間の列を作る。時刻は startTime からの秒。
        /// 先頭と末尾の無音も区間として入れる（Vowel = null、Phoneme = "sil"）。失敗したら null で error に理由。
        /// </summary>
        public static List<Segment> ParseSegments(string json, float startTime, out string error)
        {
            error = null;
            JObject q;
            try { q = JObject.Parse(json); }
            catch (Exception ex) { error = $"応答を JSON として読めません: {ex.Message}"; return null; }

            if (!(q["accent_phrases"] is JArray phrases)) { error = "応答に accent_phrases がありません"; return null; }

            float speed = Num(q["speedScale"], 1f);
            if (speed <= 0f) { error = $"speedScale が {speed} です"; return null; }
            float pre        = Num(q["prePhonemeLength"], 0f);
            float post       = Num(q["postPhonemeLength"], 0f);
            float? pauseLen  = q["pauseLength"] == null || q["pauseLength"].Type == JTokenType.Null ? (float?)null : Num(q["pauseLength"], 0f);
            float pauseScale = Num(q["pauseLengthScale"], 1f);

            var segs = new List<Segment>();
            float t = startTime;

            void AddSilence(float len, string text, string phoneme)
            {
                if (len <= 0f) return;
                segs.Add(new Segment { Text = text, Phoneme = phoneme, Vowel = null, Start = t, VowelStart = t, End = t + len });
                t += len;
            }

            AddSilence(pre / speed, "", "sil");

            foreach (var phTok in phrases)
            {
                if (!(phTok is JObject ph)) continue;
                if (ph["moras"] is JArray moras)
                {
                    foreach (var mTok in moras)
                    {
                        if (!(mTok is JObject m)) continue;
                        string vowelPh = (string)m["vowel"] ?? "";
                        float cl = Num(m["consonant_length"], 0f) / speed;
                        float vl = Num(m["vowel_length"], 0f) / speed;
                        var s = new Segment
                        {
                            Text = (string)m["text"] ?? "",
                            Phoneme = vowelPh,
                            Vowel = ToVowel(vowelPh),
                            Start = t,
                            VowelStart = t + cl,
                            End = t + cl + vl,
                        };
                        segs.Add(s);
                        t = s.End;
                    }
                }
                if (ph["pause_mora"] is JObject pm)
                {
                    float len = (pauseLen ?? Num(pm["vowel_length"], 0f)) * pauseScale / speed;
                    AddSilence(len, (string)pm["text"] ?? "、", "pau");
                }
            }

            AddSilence(post / speed, "", "sil");

            if (segs.Count == 0) { error = "モーラがありません"; return null; }
            return segs;
        }

        /// <summary>
        /// 区間の列から母音ごとのキー列を作る（Vowels と同じ並び。その母音が 1 回も出なければ null）。
        /// </summary>
        public static List<MotionScalarKeyDTO>[] BuildKeys(List<Segment> segs, float weight)
        {
            var result = new List<MotionScalarKeyDTO>[Vowels.Length];
            if (segs == null || segs.Count == 0) return result;
            float t0 = segs[0].Start;
            float t1 = segs[segs.Count - 1].End;

            for (int ch = 0; ch < Vowels.Length; ch++)
            {
                string v = Vowels[ch];
                bool any = false;
                var times  = new List<float> { t0 };
                var values = new List<float> { 0f };
                foreach (var s in segs)
                {
                    float c = s.Center;
                    float val = s.Vowel == v ? weight : 0f;
                    if (val != 0f) any = true;
                    // 同じ時刻に重なるキー（長さ 0 の区間が端にある場合）は後のものを使う
                    if (times.Count > 0 && Math.Abs(times[times.Count - 1] - c) < 1e-6f)
                    { values[values.Count - 1] = val; continue; }
                    times.Add(c);
                    values.Add(val);
                }
                if (Math.Abs(times[times.Count - 1] - t1) < 1e-6f) values[values.Count - 1] = 0f;
                else { times.Add(t1); values.Add(0f); }

                if (!any) continue;

                var keys = new List<MotionScalarKeyDTO>();
                for (int k = 0; k < times.Count; k++)
                {
                    bool keep = k == 0 || k == times.Count - 1
                             || values[k] != 0f || values[k - 1] != 0f || values[k + 1] != 0f;
                    if (keep) keys.Add(new MotionScalarKeyDTO { t = times[k], v = values[k] });
                }
                result[ch] = keys;
            }
            return result;
        }

        /// <summary>
        /// 母音ごとのキー列をクリップの表情トラックへ入れる。names[ch] がトラック名。
        /// 開始～終了（両端を含む）にある同名トラックの既存キーは消す。キーの無い母音で既存トラックがあれば、
        /// 範囲の両端に 0 を置く（範囲の外のキーから補間で口が開かないように）。
        /// 戻り値は書いたトラック名。
        /// </summary>
        public static List<string> MergeInto(MotionClipDTO dto, string[] names, List<MotionScalarKeyDTO>[] keys, float start, float end)
        {
            var written = new List<string>();
            if (dto.expressions == null) dto.expressions = new List<MotionExpressionTrackDTO>();

            for (int ch = 0; ch < Vowels.Length; ch++)
            {
                string name = names[ch];
                if (string.IsNullOrEmpty(name)) continue;
                MotionExpressionTrackDTO track = null;
                foreach (var tr in dto.expressions)
                    if (tr != null && string.Equals(tr.name, name, StringComparison.Ordinal)) { track = tr; break; }

                var add = keys[ch];
                if (track == null)
                {
                    if (add == null) continue;
                    track = new MotionExpressionTrackDTO { name = name, category = "mouth" };
                    dto.expressions.Add(track);
                }
                if (track.keys == null) track.keys = new List<MotionScalarKeyDTO>();
                track.keys.RemoveAll(k => k == null || (k.t >= start - 1e-6f && k.t <= end + 1e-6f));

                if (add != null) track.keys.AddRange(add);
                else
                {
                    track.keys.Add(new MotionScalarKeyDTO { t = start, v = 0f });
                    track.keys.Add(new MotionScalarKeyDTO { t = end,   v = 0f });
                }
                track.keys.Sort((a, b) => a.t.CompareTo(b.t));
                written.Add(name);
            }

            if (dto.duration > 0f && end > dto.duration) dto.duration = end;
            return written;
        }

        /// <summary>区間の列を「モーラの文字(母音の音素)」の並びにする（結果の表示用）。</summary>
        public static string FormatSegments(List<Segment> segs)
        {
            var sb = new StringBuilder();
            foreach (var s in segs)
            {
                if (s.Phoneme == "sil") continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s.Text).Append('(').Append(s.Phoneme).Append(')');
            }
            return sb.ToString();
        }

        private static string ToVowel(string phoneme)
        {
            switch (phoneme)
            {
                case "a": case "A": return "a";
                case "i": case "I": return "i";
                case "u": case "U": return "u";
                case "e": case "E": return "e";
                case "o": case "O": return "o";
                default: return null;   // N / cl / pau
            }
        }

        private static float Num(JToken tok, float fallback)
        {
            if (tok == null || tok.Type == JTokenType.Null) return fallback;
            if (tok.Type == JTokenType.Float || tok.Type == JTokenType.Integer) return (float)(double)tok;
            return fallback;
        }

        private static string Clip(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";
    }
}
