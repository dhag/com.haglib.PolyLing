// MotionLivePacket.cs
// ライブ受信（UDP）で送るマッスル 1 フレーム分のパケットの読み書き。
// 送信側（mocopi 受信アプリ）と受信側（PolyLing の motionLive 窓口）が同じこのクラスを使う。
//
// ■ 形式（リトルエンディアン、1 フレーム = 1 パケット）
//   0   char[4]      マジック "PLMS"
//   4   u16          バージョン（1）
//   6   u16          フラグ（bit0: Root 有効）
//   8   u32          連番
//   12  f64          時刻 [秒]（送信側の経過時間）
//   20  u16          マッスル数 N（HumanTrait.MuscleName の並び）
//   22  u16          予約（0）
//   24  f32×3        RootT（HumanPose.bodyPosition）
//   36  f32×4        RootQ（HumanPose.bodyRotation）
//   52  u8×⌈N/8⌉     有効マスク（bit i = 1 ならマッスル i は計測値。バイト内は下位ビットから）
//   ..  f32×N        マッスル値（無効のものは 0）

using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Motion
{
    /// <summary>ライブ受信の 1 フレーム。</summary>
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

    public static class MotionLivePacket
    {
        public const  ushort Version    = 1;
        public const  ushort FlagRoot   = 1;
        public const  int    HeaderSize = 52;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PLMS");

        public static int MaskSize(int muscleCount) => (muscleCount + 7) / 8;
        public static int SizeOf(int muscleCount) => HeaderSize + MaskSize(muscleCount) + muscleCount * 4;

        /// <summary>フレームをバイト列にする。</summary>
        public static byte[] Write(MotionLiveFrame f)
        {
            int n = f.Muscles.Length;
            var ms = new MemoryStream(SizeOf(n));
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(f.HasRoot ? FlagRoot : (ushort)0);
                w.Write(f.Seq);
                w.Write(f.Time);
                w.Write((ushort)n);
                w.Write((ushort)0);
                w.Write(f.RootT.x); w.Write(f.RootT.y); w.Write(f.RootT.z);
                w.Write(f.RootQ.x); w.Write(f.RootQ.y); w.Write(f.RootQ.z); w.Write(f.RootQ.w);

                var mask = new byte[MaskSize(n)];
                for (int i = 0; i < n; i++)
                    if (i < f.Valid.Length && f.Valid[i]) mask[i >> 3] |= (byte)(1 << (i & 7));
                w.Write(mask);

                for (int i = 0; i < n; i++)
                    w.Write(i < f.Valid.Length && f.Valid[i] ? f.Muscles[i] : 0f);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// バイト列を読む。形式違い・マッスル数が expectedMuscleCount と違うものは false（error に理由）。
        /// into の中身を書き換える。
        /// </summary>
        public static bool TryRead(byte[] data, int length, int expectedMuscleCount, MotionLiveFrame into, out string error)
        {
            error = "";
            if (data == null || length < HeaderSize) { error = "短すぎる"; return false; }
            for (int i = 0; i < 4; i++)
                if (data[i] != Magic[i]) { error = "マジック不一致"; return false; }

            using (var r = new BinaryReader(new MemoryStream(data, 0, length, false)))
            {
                r.ReadBytes(4);
                ushort ver = r.ReadUInt16();
                if (ver != Version) { error = $"バージョン不一致（{ver}）"; return false; }
                ushort flags = r.ReadUInt16();
                uint   seq   = r.ReadUInt32();
                double time  = r.ReadDouble();
                int    n     = r.ReadUInt16();
                r.ReadUInt16();
                if (n != expectedMuscleCount) { error = $"マッスル数不一致（{n}）"; return false; }
                if (length < SizeOf(n)) { error = "長さ不足"; return false; }

                into.Seq     = seq;
                into.Time    = time;
                into.HasRoot = (flags & FlagRoot) != 0;
                into.RootT   = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                into.RootQ   = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

                into.EnsureCount(n);
                byte[] mask = r.ReadBytes(MaskSize(n));
                for (int i = 0; i < n; i++)
                {
                    into.Valid[i]   = (mask[i >> 3] & (1 << (i & 7))) != 0;
                    into.Muscles[i] = r.ReadSingle();
                }
            }
            return true;
        }
    }
}
