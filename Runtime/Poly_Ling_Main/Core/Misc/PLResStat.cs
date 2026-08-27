// PLResStat.cs
// GPU リソースの破棄漏れを検出するための診断コード。恒久コードではない。
//
// 【A】自前カウンタ
//   Adapter / バッファセット / Mesh / Material の生成・解放を呼び出し側で
//   計上し、生存数（差分）を記録する。発生源の特定に使う。
//
// 【B】Unity 組込みカウンタ（ProfilerRecorder）
//   Unity 自身が数えている実数。こちらの計上漏れがあっても検出できる。
//   カウンタ名は環境で異なりうるため決め打ちせず、開始時に
//   ProfilerRecorderHandle.GetAvailable() で存在するものだけ購読する。
//
// 出力は値が変わったときだけ 1 行。変化がなければ何も出さない。

using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Poly_Ling.Diagnostics
{
    public static class PLResStat
    {
        // ============================================================
        // A: 自前カウンタ
        // ============================================================

        public static int LiveAdapter;    // UnifiedSystemAdapter
        public static int LiveBufSet;     // CreateAllBuffers - ReleaseAllBuffers
        public static int LiveMesh;       // new Mesh - Destroy
        public static int LiveMaterial;   // new Material - Destroy
        public static int LiveCB;         // new ComputeBuffer - Release

        /// <summary>ComputeBuffer の生成を計上する。生成式をこれで包む。</summary>
        public static UnityEngine.ComputeBuffer NewCB(UnityEngine.ComputeBuffer b)
        {
            if (b != null) LiveCB++;
            return b;
        }

        private static string _lastLive;

        /// <summary>自前カウンタを出力する。前回と同じ内容なら出さない。</summary>
        public static void ReportLive(string where)
        {
            if (!PLCamDbg.SwLog) return;
            string s = "LIVE adapter=" + LiveAdapter
                     + " bufSet=" + LiveBufSet
                     + " cb=" + LiveCB
                     + " mesh=" + LiveMesh
                     + " mat=" + LiveMaterial;
            if (s == _lastLive) return;
            _lastLive = s;
            PLCamDbg.Mark(s + " at=" + where);
        }

        // ============================================================
        // B: Unity 組込みカウンタ
        // ============================================================

        private sealed class Rec
        {
            public string Name;
            public ProfilerRecorder Recorder;
        }

        private static readonly List<Rec> _recs = new List<Rec>();
        private static bool _statInit;
        private static string _lastStat;

        // 見たいカウンタ名。存在しないものは黙って飛ばす。
        private static readonly string[] WantedNames =
        {
            "Used Buffers Count",
            "Used Buffers Bytes",
            "Render Textures Count",
            "Render Textures Bytes",
            "Mesh Count",
            "Mesh Memory",
            "Material Count",
            "Texture Count",
            "Texture Memory",
            "Gfx Used Memory",
            "Gfx Reserved Memory",
            "Object Count",
        };

        private static void EnsureStat()
        {
            if (_statInit) return;
            _statInit = true;

            try
            {
                var avail = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(avail);

                var wanted = new HashSet<string>(WantedNames);
                var seen   = new HashSet<string>();

                for (int i = 0; i < avail.Count; i++)
                {
                    var desc = ProfilerRecorderHandle.GetDescription(avail[i]);
                    string nm = desc.Name;
                    if (!wanted.Contains(nm) || seen.Contains(nm)) continue;
                    seen.Add(nm);

                    var r = new ProfilerRecorder(avail[i], 1, ProfilerRecorderOptions.Default);
                    r.Start();
                    _recs.Add(new Rec { Name = nm, Recorder = r });
                }

                PLCamDbg.Mark("STATINIT n=" + _recs.Count);
            }
            catch (Exception e)
            {
                PLCamDbg.Mark("STATINIT failed: " + e.GetType().Name);
                _recs.Clear();
            }
        }

        /// <summary>組込みカウンタを出力する。前回と同じ内容なら出さない。</summary>
        public static void ReportStat()
        {
            if (!PLCamDbg.SwLog) return;
            // Mesh 生存数は自前計上。組込みカウンタが取れない環境でも出す。
            ReportLive("frame");
            EnsureStat();
            if (_recs.Count == 0) return;

            var sb = new System.Text.StringBuilder(256);
            sb.Append("STAT");
            for (int i = 0; i < _recs.Count; i++)
            {
                var r = _recs[i];
                long v = -1;
                try { if (r.Recorder.Valid) v = r.Recorder.LastValue; }
                catch (Exception) { v = -1; }
                // バイト単位のカウンタは MB へ丸める。毎フレームの微小変動で
                // 出力が溢れるのを防ぐため。
                if (v > 0 && IsByteCounter(r.Name)) v /= (1024 * 1024);
                sb.Append(' ').Append(Compact(r.Name)).Append('=').Append(v);
            }
            string s = sb.ToString();
            if (s == _lastStat) return;
            _lastStat = s;
            PLCamDbg.Mark(s);
        }

        private static bool IsByteCounter(string n)
        {
            return n == "Used Buffers Bytes"
                || n == "Render Textures Bytes"
                || n == "Mesh Memory"
                || n == "Texture Memory"
                || n == "Gfx Used Memory"
                || n == "Gfx Reserved Memory";
        }

        private static string Compact(string n)
        {
            switch (n)
            {
                case "Used Buffers Count":     return "bufN";
                case "Used Buffers Bytes":     return "bufMB";
                case "Render Textures Count":  return "rtN";
                case "Render Textures Bytes":  return "rtMB";
                case "Mesh Count":             return "meshN";
                case "Mesh Memory":            return "meshMB";
                case "Material Count":         return "matN";
                case "Texture Count":          return "texN";
                case "Texture Memory":         return "texMB";
                case "Gfx Used Memory":        return "gfxUsedMB";
                case "Gfx Reserved Memory":    return "gfxResMB";
                case "Object Count":           return "objN";
                default:                       return n.Replace(' ', '_');
            }
        }

        /// <summary>A と B をまとめて出す。</summary>
        public static void Report(string where)
        {
            ReportLive(where);
            ReportStat();
        }
    }
}
