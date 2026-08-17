// VMDIKTraceWriter.cs
// CCD IK の反復トレースを CSV へ書き出すユーティリティ。
//
// 列仕様（24 列）:
//   frame,ikBone,iteration,link,
//   effWorldX,effWorldY,effWorldZ,tgtWorldX,tgtWorldY,tgtWorldZ,dist,
//   rotAxisX,rotAxisY,rotAxisZ,rotAngleDeg,
//   beforeQx,beforeQy,beforeQz,beforeQw,afterQx,afterQy,afterQz,afterQw,clamped
//
//   1 行 = CCD の 1 反復 × 1 リンク。
//   dist   = そのリンクを回す「前」の eff ↔ tgt 距離。
//   SKIP（回転角過小 / 回転軸過小）の回も rotAngleDeg=0・rotAxis=(0,0,0) で 1 行出す。
//     出さないと収束が止まった位置が読めないため。
//   clamped = 角度制限で実際にクォータニオンが変化したときのみ 1。
//   iteration = -1 / link = "(final)" の行は、ループと最適値復元が終わった後の
//     最終残差（distEnd）。手順書 §3-3 の判定はこの行で行う。

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// vmd_ik.csv のライタ。Open で新規作成（ヘッダ書き直し）、以降は同一ストリームへ追記する。
    /// </summary>
    public class VMDIKTraceWriter : IDisposable
    {
        private const string Header =
            "frame,ikBone,iteration,link," +
            "effWorldX,effWorldY,effWorldZ,tgtWorldX,tgtWorldY,tgtWorldZ,dist," +
            "rotAxisX,rotAxisY,rotAxisZ,rotAngleDeg," +
            "beforeQx,beforeQy,beforeQz,beforeQw," +
            "afterQx,afterQy,afterQz,afterQw,clamped";

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
                catch (Exception ex) { Debug.LogError($"[VMDIKTraceWriter] Close failed: {ex.Message}"); }
                _writer = null;
            }
            FilePath = null;
        }

        public void Dispose() => Close();

        // ================================================================
        // 書き出し
        // ================================================================

        /// <summary>1 反復 × 1 リンク分の行を書く。</summary>
        public void Write(float frame, string ikBone, int iteration, string link,
                          Vector3 effWorld, Vector3 tgtWorld, float dist,
                          Vector3 rotAxis, float rotAngleDeg,
                          Quaternion before, Quaternion after, bool clamped)
        {
            if (_writer == null) return;

            _sb.Length = 0;
            _sb.Append(F(frame)).Append(',');
            _sb.Append(Escape(ikBone)).Append(',');
            _sb.Append(iteration.ToString(CultureInfo.InvariantCulture)).Append(',');
            _sb.Append(Escape(link)).Append(',');
            AppendVector(_sb, effWorld);
            AppendVector(_sb, tgtWorld);
            _sb.Append(F(dist)).Append(',');
            AppendVector(_sb, rotAxis);
            _sb.Append(F(rotAngleDeg)).Append(',');
            AppendQuaternion(_sb, before);
            AppendQuaternion(_sb, after);
            _sb.Append(clamped ? '1' : '0');

            _writer.WriteLine(_sb.ToString());
        }

        // ================================================================
        // 内部ユーティリティ
        // ================================================================

        private static void AppendVector(StringBuilder sb, Vector3 v)
        {
            sb.Append(F(v.x)).Append(',');
            sb.Append(F(v.y)).Append(',');
            sb.Append(F(v.z)).Append(',');
        }

        private static void AppendQuaternion(StringBuilder sb, Quaternion q)
        {
            sb.Append(F(q.x)).Append(',');
            sb.Append(F(q.y)).Append(',');
            sb.Append(F(q.z)).Append(',');
            sb.Append(F(q.w)).Append(',');
        }

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
