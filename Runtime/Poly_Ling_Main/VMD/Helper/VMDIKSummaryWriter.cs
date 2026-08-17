// VMDIKSummaryWriter.cs
// IK 解決の 1 フレーム × 1 IK ボーン サマリを CSV へ書き出すユーティリティ。
//
// 列仕様（8 列）:
//   frame,ikBone,iterUsed,distStart,distEnd,distBest,kneeAngleDeg,kneeSignOK
//
//   distStart    = ループ開始前の eff ↔ tgt 距離
//   distBest     = ループ中の最小距離
//   iterUsed     = distBest を出した反復番号（更新が無ければ -1）
//   distEnd      = 最適値復元後の実距離（vmd_ik.csv の "(final)" 行と同値）
//   kneeAngleDeg = 角度制限付きリンクの合成回転をモデル軸へ写し、
//                  RestrictRotation と同じ SplitRotation で分解した X 成分（度）
//   kneeSignOK   = kneeAngleDeg が PMX の制限範囲に入っていれば 1
//
//   角度制限付きリンクを持たない IK ボーン（髪ＩＫ・つま先ＩＫ）は
//   kneeAngleDeg / kneeSignOK を空欄にする。意味の無い 0 を混ぜないため。
//   kneeAngleDeg / kneeSignOK は IgnoreAngleLimits が ON でも計算して出す。

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// vmd_summary.csv のライタ。Open で新規作成（ヘッダ書き直し）、以降は同一ストリームへ追記する。
    /// </summary>
    public class VMDIKSummaryWriter : IDisposable
    {
        private const string Header =
            "frame,ikBone,iterUsed,distStart,distEnd,distBest,kneeAngleDeg,kneeSignOK";

        private StreamWriter _writer;
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>出力先フルパス（Open 済みのとき有効）</summary>
        public string FilePath { get; private set; }

        /// <summary>ストリームが開いているか</summary>
        public bool IsOpen => _writer != null;

        // ================================================================
        // 開閉
        // ================================================================

        /// <summary>
        /// 出力ファイルを新規作成してヘッダを書く。既存ファイルは上書きする。
        /// ボーン名が日本語のため UTF-8 BOM 付き、改行は LF。
        /// </summary>
        public void Open(string filePath)
        {
            Close();

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(stream, new UTF8Encoding(true));
            _writer.NewLine = "\n";
            _writer.AutoFlush = true;
            _writer.WriteLine(Header);

            FilePath = filePath;
        }

        /// <summary>ストリームを閉じる。</summary>
        public void Close()
        {
            if (_writer != null)
            {
                try { _writer.Flush(); _writer.Dispose(); }
                catch (Exception ex) { Debug.LogError($"[VMDIKSummaryWriter] Close failed: {ex.Message}"); }
                _writer = null;
            }
            FilePath = null;
        }

        public void Dispose() => Close();

        // ================================================================
        // 書き出し
        // ================================================================

        /// <summary>
        /// 1 フレーム × 1 IK ボーン分の行を書く。
        /// hasKnee が false のとき kneeAngleDeg / kneeSignOK は空欄になる。
        /// </summary>
        public void Write(float frame, string ikBone, int iterUsed,
                          float distStart, float distEnd, float distBest,
                          bool hasKnee, float kneeAngleDeg, bool kneeSignOK)
        {
            if (_writer == null) return;

            _sb.Length = 0;
            _sb.Append(F(frame)).Append(',');
            _sb.Append(Escape(ikBone)).Append(',');
            _sb.Append(iterUsed.ToString(CultureInfo.InvariantCulture)).Append(',');
            _sb.Append(F(distStart)).Append(',');
            _sb.Append(F(distEnd)).Append(',');
            _sb.Append(F(distBest)).Append(',');
            if (hasKnee)
            {
                _sb.Append(F(kneeAngleDeg)).Append(',');
                _sb.Append(kneeSignOK ? '1' : '0');
            }
            else
            {
                _sb.Append(',');
            }

            _writer.WriteLine(_sb.ToString());
        }

        // ================================================================
        // 内部ユーティリティ
        // ================================================================

        private static string F(float v)
        {
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
