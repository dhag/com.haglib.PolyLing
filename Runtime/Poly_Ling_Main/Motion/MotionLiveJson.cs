// MotionLiveJson.cs
// ライブ受信のマッスル 1 フレーム分の JSON の組み立てと読み取り。
// 送信側（mocopi 受信アプリ）と受信側（PolyLing の motionLive 窓口）、転送先の画面（モーションスタジオ）が
// 同じ形を使う。WebSocket では Duplex 封筒（Text フレーム {type,id,items:[{type:3,data:"…"}]}）に包んで送る。
//
// ■ 形
//   {"type":"muscles","seq":連番,"time":秒,
//    "muscles":{"マッスル名":値, …},      ← HumanTrait.MuscleName の名前。計測値のものだけを入れる
//    "rootT":[x,y,z],                     ← HumanPose.bodyPosition（無いこともある）
//    "rootQ":[x,y,z,w]}                   ← HumanPose.bodyRotation（無いこともある）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Poly_Ling.Motion
{
    /// <summary>ライブ受信の 1 フレーム。Muscles / Valid は HumanTrait.MuscleName の並び。</summary>
    public sealed class MotionLiveFrame
    {
        public uint       Seq;
        public double     Time;
        public bool       HasRoot;
        public Vector3    RootT;
        public Quaternion RootQ = Quaternion.identity;
        public float[]    Muscles = Array.Empty<float>();
        public bool[]     Valid   = Array.Empty<bool>();

        /// <summary>マッスル数を合わせる（足りなければ作り直す）。</summary>
        public void EnsureCount(int n)
        {
            if (Muscles.Length != n) Muscles = new float[n];
            if (Valid.Length   != n) Valid   = new bool[n];
        }
    }

    public static class MotionLiveJson
    {
        public const string TypeMuscles = "muscles";

        /// <summary>フレームを JSON にする。names は HumanTrait.MuscleName。</summary>
        public static string Build(MotionLiveFrame f, string[] names)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(4096);
            sb.Append("{\"type\":\"").Append(TypeMuscles).Append("\",\"seq\":").Append(f.Seq.ToString(inv))
              .Append(",\"time\":").Append(f.Time.ToString("R", inv))
              .Append(",\"muscles\":{");
            bool first = true;
            for (int i = 0; i < f.Muscles.Length && names != null && i < names.Length; i++)
            {
                if (i >= f.Valid.Length || !f.Valid[i]) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(names[i]).Append("\":").Append(f.Muscles[i].ToString("R", inv));
            }
            sb.Append('}');
            if (f.HasRoot)
            {
                sb.Append(",\"rootT\":[").Append(f.RootT.x.ToString("R", inv)).Append(',')
                  .Append(f.RootT.y.ToString("R", inv)).Append(',').Append(f.RootT.z.ToString("R", inv)).Append(']');
                sb.Append(",\"rootQ\":[").Append(f.RootQ.x.ToString("R", inv)).Append(',')
                  .Append(f.RootQ.y.ToString("R", inv)).Append(',').Append(f.RootQ.z.ToString("R", inv)).Append(',')
                  .Append(f.RootQ.w.ToString("R", inv)).Append(']');
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// JSON を読む。type が muscles でないもの・形が違うものは false（error に理由）。
        /// indexOf はマッスル名 → 番号（HumanTrait.MuscleName の添字）、count はマッスル数。
        /// 知らない名前のマッスルは無視する。
        /// </summary>
        public static bool TryParse(string json, IReadOnlyDictionary<string, int> indexOf, int count,
                                    MotionLiveFrame into, out string error)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { error = "JSON でない: " + e.Message; return false; }
            return TryParse(o, indexOf, count, into, out error);
        }

        /// <summary>読み込み済みの JSON から読む（type で振り分けた後に使う）。</summary>
        public static bool TryParse(JObject o, IReadOnlyDictionary<string, int> indexOf, int count,
                                    MotionLiveFrame into, out string error)
        {
            error = "";
            if ((string)o["type"] != TypeMuscles) { error = "type が muscles でない"; return false; }
            if (!(o["muscles"] is JObject ms)) { error = "muscles が無い"; return false; }

            try
            {
                into.Seq  = o["seq"]  != null ? (uint)o["seq"]  : 0u;
                into.Time = o["time"] != null ? (double)o["time"] : 0.0;

                into.EnsureCount(count);
                Array.Clear(into.Muscles, 0, count);
                Array.Clear(into.Valid, 0, count);
                foreach (var p in ms.Properties())
                {
                    if (!indexOf.TryGetValue(p.Name, out int i) || i < 0 || i >= count) continue;
                    into.Muscles[i] = (float)p.Value;
                    into.Valid[i]   = true;
                }

                var t = o["rootT"] as JArray;
                var q = o["rootQ"] as JArray;
                into.HasRoot = t != null && t.Count == 3 && q != null && q.Count == 4;
                if (into.HasRoot)
                {
                    into.RootT = new Vector3((float)t[0], (float)t[1], (float)t[2]);
                    into.RootQ = new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
                }
            }
            catch (Exception e)
            {
                error = "値の形が違う: " + e.Message;
                return false;
            }
            return true;
        }

        /// <summary>HumanTrait.MuscleName から「名前 → 番号」を作る。</summary>
        public static Dictionary<string, int> BuildIndex(string[] names)
        {
            var d = new Dictionary<string, int>(names?.Length ?? 0);
            if (names != null)
                for (int i = 0; i < names.Length; i++) d[names[i]] = i;
            return d;
        }
    }
}
