// PLCamDbg.cs
// カメラ操作クラッシュ調査用の診断ロガー。恒久コードではない。
//
// ・Debug.Log はネイティブクラッシュ時に末尾が失われるため使用しない。
//   AutoFlush 付き StreamWriter で 1 行ごとにディスクへ書き出す。
// ・出力先: Application.persistentDataPath/CamDbg.txt（起動ごとに新規作成）
// ・切り分けスイッチは Application.persistentDataPath/CamDbgSwitch.txt から読む。
//   書式（1 行）: hit=1 rebuild=1 readback=1
//   1=許可（通常）／0=強制的に無効化
//   ファイルが無い場合はすべて 1。
//
// 調査完了後にこのファイルと呼び出し側の記述を削除すること。

using System;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Diagnostics
{
    public static class PLCamDbg
    {
        // ============================================================
        // 出力
        // ============================================================

        /// <summary>
        /// ログ出力の総合スイッチ。既定 false（出力しない）。
        ///
        /// 有効にするには CamDbgSwitch.txt に log=1 を書く。
        /// 例:  log=1
        ///      log=1 getdata=0 flush=1
        ///
        /// 計測コード本体は全ファイルに残してあるので、この 1 行だけで
        /// 記録の有無を切り替えられる。ファイル生成も行わない。
        /// </summary>
        public static bool SwLog = false;

        private static bool _enabled = true;
        private static StreamWriter _writer;
        private static int _seq;
        private static string _lastCap;

        private static StreamWriter GetWriter()
        {
            EnsureSwitches();
            if (!SwLog) return null;
            if (!_enabled) return null;
            if (_writer != null) return _writer;

            try
            {
                string path = Path.Combine(Application.persistentDataPath, "CamDbg.txt");
                _writer = new StreamWriter(path, false);
                _writer.AutoFlush = true;
                _writer.WriteLine("=== CamDbg start " + DateTime.Now.ToString("HH:mm:ss.fff") + " ===");
                _writer.WriteLine("path=" + path);
            }
            catch (Exception)
            {
                _enabled = false;
                _writer = null;
            }
            return _writer;
        }

        /// <summary>1 行記録する。通し番号が付くので欠落を検出できる。</summary>
        public static void Mark(string tag)
        {
            if (!SwLog || !_enabled) return;
            var w = GetWriter();
            if (w == null) return;
            try { w.WriteLine((++_seq) + " " + tag); }
            catch (Exception) { _enabled = false; }
        }

        /// <summary>容量情報。前回と同じ内容なら記録しない。</summary>
        public static void Cap(string body)
        {
            if (!SwLog || !_enabled) return;
            if (body == _lastCap) return;
            _lastCap = body;
            Mark("cap " + body);
        }

        // ============================================================
        // 切り分けスイッチ
        // ============================================================

        private static bool _switchesLoaded;

        /// <summary>false にすると AllowHitTest を常に無効化する。</summary>
        public static bool SwHitTest = true;
        /// <summary>false にすると AllowMeshRebuild を常に無効化する。</summary>
        public static bool SwMeshRebuild = true;
        /// <summary>false にすると AllowVertexFlagsReadback を常に無効化する。</summary>
        public static bool SwReadback = true;

        /// <summary>true にするとビューポートカメラを Perspective 1 台だけにする。</summary>
        public static bool SwSingleCamera = false;
        /// <summary>false にすると DispatchCullingForDisplay の GPU 処理を止める。</summary>
        public static bool SwCullDisplay = true;

        /// <summary>true にすると RebuildAdapter で UnifiedSystemAdapter を作らない。</summary>
        public static bool SwNoAdapter = false;

        /// <summary>true にするとワイヤ・頂点オーバーレイの提出を止める。</summary>
        public static bool SwNoWire = false;
        /// <summary>true にすると UpdateTransform / WritebackTransformedVertices を止める。</summary>
        public static bool SwNoXform = false;

        /// <summary>false にすると ComputeBuffer.GetData（同期読み戻し）を一切行わない。</summary>
        public static bool SwGetData = true;

        /// <summary>
        /// getdata=0 と併用する。true にすると、読み戻しの代わりに GL.Flush() だけを呼ぶ。
        ///
        ///   GetData  = コマンドキューのフラッシュ ＋ GPU 完了待ち（メインスレッド停止）
        ///   GL.Flush = フラッシュのみ（待たない）
        ///
        /// どちらが引き金かを分離するための診断スイッチ。
        /// </summary>
        public static bool SwFlushOnly = false;

        /// <summary>
        /// flush=2 のとき true。各読み戻し位置ではフラッシュせず、
        /// 「要フラッシュ」の印だけ立てて、次のフレーム開始時に 1 回だけ実行する。
        /// フラッシュの回数・位置が要因かを分離するための診断スイッチ。
        /// </summary>
        public static bool SwFlushDeferred = false;

        /// <summary>flush=2 用。フラッシュ待ちの印。</summary>
        public static bool FlushPending = false;

        /// <summary>flush=2 用。溜まっていればフラッシュを 1 回だけ実行する。</summary>
        public static void FlushIfPending()
        {
            if (!SwFlushDeferred || !FlushPending) return;
            FlushPending = false;
            Mark("FLUSH once f=" + Frame);
            UnityEngine.GL.Flush();
        }

        /// <summary>
        /// false にすると、ホバー・表示用カリング系（G1〜G6）の同期読み戻しだけを止める。
        ///
        /// 【目的】
        /// AsyncGPUReadback へ置き換えた後の状態を、実装せずに再現する。
        /// 確定操作（矩形選択・書き戻し = G7 以降）は同期のまま残る。
        /// これで落ちなければ非同期化が有効、落ちるなら別の原因。
        /// </summary>
        public static bool SwHotGetData = true;

        // ============================================================
        // GPU 操作の記録（書き込み / 読み戻しのどちらで壊れるかを見るため）
        // ============================================================

        /// <summary>現在のフレーム番号。beginFrameRendering で加算する。</summary>
        public static int Frame;

        /// <summary>Compute シェーダーの Dispatch を記録する。</summary>
        public static void Dsp(string kernel, int slot, UnityEngine.ComputeBuffer outBuf, int threads)
        {
            if (!SwLog) return;
            Mark("DSP f=" + Frame + " k=" + kernel + " slot=" + slot
               + " out=" + (outBuf == null ? 0 : outBuf.GetHashCode())
               + " n=" + threads);
        }

        /// <summary>CPU → GPU の書き込み（SetData）を記録する。</summary>
        public static void Wr(string tag, UnityEngine.ComputeBuffer buf, int count)
        {
            if (!SwLog) return;
            Mark("WR  f=" + Frame + " " + tag
               + " buf=" + (buf == null ? 0 : buf.GetHashCode())
               + " n=" + count);
        }

        /// <summary>GPU → CPU の読み戻し（GetData）を記録する。</summary>
        public static void Rd(string tag, UnityEngine.ComputeBuffer buf, int count, bool after)
        {
            if (!SwLog) return;
            Mark("RD" + (after ? "< " : "> ") + "f=" + Frame + " " + tag
               + " buf=" + (buf == null ? 0 : buf.GetHashCode())
               + " n=" + count);
        }

        public static void EnsureSwitches()
        {
            if (_switchesLoaded) return;
            _switchesLoaded = true;

            try
            {
                string path = Path.Combine(Application.persistentDataPath, "CamDbgSwitch.txt");
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    SwHitTest      = ReadFlag(text, "hit",      true);
                    SwMeshRebuild  = ReadFlag(text, "rebuild",  true);
                    SwReadback     = ReadFlag(text, "readback", true);
                    SwSingleCamera = ReadFlag(text, "cams",     false);
                    SwCullDisplay  = ReadFlag(text, "cull",     true);
                    SwNoAdapter    = ReadFlag(text, "adapter",  false);
                    SwNoWire       = ReadFlag(text, "wire",     false);
                    SwNoXform      = ReadFlag(text, "xform",    false);
                    SwGetData      = ReadFlag(text, "getdata",  true);
                    SwLog           = ReadFlag(text, "log",      false);
                    int __flush     = ReadInt(text, "flush", 0);
                    SwFlushOnly     = (__flush == 1);
                    SwFlushDeferred = (__flush == 2);
                    SwHotGetData   = ReadFlag(text, "hotget",   true);
                }
            }
            catch (Exception)
            {
                // 読めない場合はすべて既定値のまま
            }

            Mark("sw hit=" + (SwHitTest ? 1 : 0)
               + " rebuild=" + (SwMeshRebuild ? 1 : 0)
               + " readback=" + (SwReadback ? 1 : 0)
               + " cams=" + (SwSingleCamera ? 1 : 0)
               + " cull=" + (SwCullDisplay ? 1 : 0)
               + " adapter=" + (SwNoAdapter ? 1 : 0)
               + " wire=" + (SwNoWire ? 1 : 0)
               + " xform=" + (SwNoXform ? 1 : 0)
               + " getdata=" + (SwGetData ? 1 : 0)
               + " hotget=" + (SwHotGetData ? 1 : 0)
               + " flush=" + (SwFlushOnly ? 1 : SwFlushDeferred ? 2 : 0));
        }

        /// <summary>0/1/2 を読む。flush 用。</summary>
        private static int ReadInt(string text, string key, int fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            int i = text.IndexOf(key + "=", StringComparison.Ordinal);
            if (i < 0) return fallback;
            int v = i + key.Length + 1;
            if (v >= text.Length) return fallback;
            char c = text[v];
            if (c >= '0' && c <= '9') return c - '0';
            return fallback;
        }

        private static bool ReadFlag(string text, string key, bool fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            int i = text.IndexOf(key + "=", StringComparison.Ordinal);
            if (i < 0) return fallback;
            int v = i + key.Length + 1;
            if (v >= text.Length) return fallback;
            char c = text[v];
            if (c == '0') return false;
            if (c == '1') return true;
            return fallback;
        }
    }
}
