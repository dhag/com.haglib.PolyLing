// BvhDocument.cs
// ============================================================
// BVH（BioVision Hierarchy）の構文だけを扱う（読む・書く）。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。意味づけ（Humanoid・座標変換）は BvhCodec が行う。
// 規則の正本は PolyLing_BVH対応計画.md 2。
//
//   HIERARCHY
//   ROOT 名前 { OFFSET x y z  CHANNELS n …  (JOINT 名前 {…})*  (End Site { OFFSET x y z })? }
//   MOTION
//   Frames: n
//   Frame Time: t
//   値（1 行 1 フレーム。CHANNELS を関節の出現順に連結した並び）
//
//   ROOT は 1 つだけ受ける。値はファイルの座標（右手系）のまま持つ。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public enum BvhChannel { Xposition, Yposition, Zposition, Xrotation, Yrotation, Zrotation }

    /// <summary>関節 1 つ（End Site も 1 つの関節として持つ）。</summary>
    public sealed class BvhJoint
    {
        public string Name;
        /// <summary>親の索引（Joints の添字）。根は -1。</summary>
        public int Parent = -1;
        public bool IsEndSite;
        /// <summary>親から見た位置（ファイルの値）。</summary>
        public Vector3 Offset;
        public BvhChannel[] Channels = Array.Empty<BvhChannel>();
        /// <summary>フレームの値の並びでの先頭の位置。</summary>
        public int ChannelStart;

        public bool HasPosition
        {
            get
            {
                foreach (var c in Channels)
                    if (c == BvhChannel.Xposition || c == BvhChannel.Yposition || c == BvhChannel.Zposition) return true;
                return false;
            }
        }
    }

    public sealed class BvhDocument
    {
        /// <summary>関節。親は必ず子より前にある。</summary>
        public readonly List<BvhJoint> Joints = new List<BvhJoint>();
        public int   ChannelCount;
        public int   FrameCount;
        public float FrameTime;
        /// <summary>FrameCount × ChannelCount の値（フレーム順）。</summary>
        public float[] Values = Array.Empty<float>();
        /// <summary>読込の警告（エラーではないもの）。</summary>
        public readonly List<string> Warnings = new List<string>();

        public float Value(int frame, int channel) => Values[frame * ChannelCount + channel];

        // ================================================================
        // 読む
        // ================================================================

        /// <summary>BVH の文字列を読む。失敗時は null と理由。</summary>
        public static BvhDocument Parse(string text, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(text)) { reason = "内容が空です"; return null; }
            var tok = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var doc = new BvhDocument();
            int i = 0;
            try
            {
                Expect(tok, ref i, "HIERARCHY");
                if (i >= tok.Length || !Eq(tok[i], "ROOT")) throw new FormatException("ROOT がありません");
                i++;
                doc.ParseJoint(tok, ref i, Next(tok, ref i), -1, false);
                if (i < tok.Length && Eq(tok[i], "ROOT")) throw new FormatException("ROOT が 2 つ以上あります（1 つだけ受けます）");

                Expect(tok, ref i, "MOTION");
                // "Frames:" "n"（"Frames" ":" の書き方も受ける）
                string fr = Next(tok, ref i);
                if (!fr.StartsWith("Frames", StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Frames: がありません（\"{fr}\"）");
                if (fr.EndsWith("Frames", StringComparison.OrdinalIgnoreCase) && i < tok.Length && tok[i] == ":") i++;
                int frames = (int)ParseF(Next(tok, ref i));
                Expect(tok, ref i, "Frame");
                string tm = Next(tok, ref i);
                if (!tm.StartsWith("Time", StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Frame Time: がありません（\"{tm}\"）");
                if (tm.Equals("Time", StringComparison.OrdinalIgnoreCase) && i < tok.Length && tok[i] == ":") i++;
                doc.FrameTime = ParseF(Next(tok, ref i));
                if (doc.FrameTime <= 0f) throw new FormatException($"Frame Time が {doc.FrameTime} です");

                int remain = tok.Length - i;
                int avail  = doc.ChannelCount > 0 ? remain / doc.ChannelCount : 0;
                if (frames < 0) frames = 0;
                if (avail < frames)
                {
                    doc.Warnings.Add($"Frames: {frames} に対して値が {avail} フレーム分しかないので、{avail} フレームとして読みます");
                    frames = avail;
                }
                else if (doc.ChannelCount > 0 && remain > frames * doc.ChannelCount)
                    doc.Warnings.Add($"Frames: {frames} より後ろの値 {remain - frames * doc.ChannelCount} 個を読みません");

                doc.FrameCount = frames;
                doc.Values = new float[frames * doc.ChannelCount];
                for (int k = 0; k < doc.Values.Length; k++) doc.Values[k] = ParseF(tok[i + k]);
            }
            catch (FormatException ex)
            {
                reason = ex.Message;
                return null;
            }
            return doc;
        }

        private void ParseJoint(string[] tok, ref int i, string name, int parent, bool endSite)
        {
            int self = Joints.Count;
            var j = new BvhJoint { Name = name, Parent = parent, IsEndSite = endSite, ChannelStart = ChannelCount };
            Joints.Add(j);
            Expect(tok, ref i, "{");
            while (true)
            {
                string t = Next(tok, ref i);
                if (t == "}") return;
                if (Eq(t, "OFFSET"))
                {
                    float x = ParseF(Next(tok, ref i)), y = ParseF(Next(tok, ref i)), z = ParseF(Next(tok, ref i));
                    j.Offset = new Vector3(x, y, z);
                }
                else if (Eq(t, "CHANNELS"))
                {
                    if (endSite) throw new FormatException($"End Site（{name}）に CHANNELS があります");
                    int n = (int)ParseF(Next(tok, ref i));
                    if (n < 0 || n > 6) throw new FormatException($"{name} の CHANNELS の数が {n} です");
                    var chs = new BvhChannel[n];
                    for (int c = 0; c < n; c++)
                    {
                        string cn = Next(tok, ref i);
                        if (!Enum.TryParse(cn, true, out BvhChannel ch)) throw new FormatException($"{name} のチャンネル \"{cn}\" を知りません");
                        chs[c] = ch;
                    }
                    j.Channels = chs;
                    j.ChannelStart = ChannelCount;
                    ChannelCount += n;
                }
                else if (Eq(t, "JOINT"))
                {
                    ParseJoint(tok, ref i, Next(tok, ref i), self, false);
                }
                else if (Eq(t, "End"))
                {
                    Expect(tok, ref i, "Site");
                    ParseJoint(tok, ref i, name + "_End", self, true);
                }
                else throw new FormatException($"{name} の中に読めない語 \"{t}\" があります");
            }
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Next(string[] tok, ref int i)
        {
            if (i >= tok.Length) throw new FormatException("ファイルが途中で終わっています");
            return tok[i++];
        }

        private static void Expect(string[] tok, ref int i, string word)
        {
            string t = Next(tok, ref i);
            if (!Eq(t, word)) throw new FormatException($"\"{word}\" のはずの所が \"{t}\" です");
        }

        private static float ParseF(string s)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                throw new FormatException($"数値ではありません: \"{s}\"");
            return v;
        }

        // ================================================================
        // 書く
        // ================================================================

        /// <summary>BVH の文字列にする。ChannelStart は Joints の並びで振り直す。</summary>
        public string ToText()
        {
            int start = 0;
            foreach (var j in Joints) { j.ChannelStart = start; start += j.Channels.Length; }
            ChannelCount = start;

            var children = new List<int>[Joints.Count];
            for (int k = 0; k < Joints.Count; k++) children[k] = new List<int>();
            int root = -1;
            for (int k = 0; k < Joints.Count; k++)
            {
                if (Joints[k].Parent >= 0) children[Joints[k].Parent].Add(k);
                else if (root < 0) root = k;
            }
            if (root < 0) throw new InvalidOperationException("根の関節がありません");

            var sb = new StringBuilder(1 << 16);
            sb.Append("HIERARCHY\n");
            WriteJoint(sb, root, children, 0);
            sb.Append("MOTION\n");
            sb.Append("Frames: ").Append(FrameCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("Frame Time: ").Append(FrameTime.ToString("0.#########", CultureInfo.InvariantCulture)).Append('\n');
            for (int f = 0; f < FrameCount; f++)
            {
                int o = f * ChannelCount;
                for (int c = 0; c < ChannelCount; c++)
                {
                    if (c > 0) sb.Append(' ');
                    sb.Append(F(Values[o + c]));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private void WriteJoint(StringBuilder sb, int k, List<int>[] children, int depth)
        {
            var j = Joints[k];
            string ind = new string('\t', depth);
            if (j.IsEndSite) sb.Append(ind).Append("End Site\n");
            else sb.Append(ind).Append(depth == 0 ? "ROOT " : "JOINT ").Append(j.Name).Append('\n');
            sb.Append(ind).Append("{\n");
            sb.Append(ind).Append("\tOFFSET ").Append(F(j.Offset.x)).Append(' ').Append(F(j.Offset.y)).Append(' ').Append(F(j.Offset.z)).Append('\n');
            if (!j.IsEndSite)
            {
                sb.Append(ind).Append("\tCHANNELS ").Append(j.Channels.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var c in j.Channels) sb.Append(' ').Append(c.ToString());
                sb.Append('\n');
            }
            foreach (int ch in children[k]) WriteJoint(sb, ch, children, depth + 1);
            sb.Append(ind).Append("}\n");
        }

        private static string F(float v)
        {
            if (Mathf.Abs(v) < 1e-7f) v = 0f;
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
