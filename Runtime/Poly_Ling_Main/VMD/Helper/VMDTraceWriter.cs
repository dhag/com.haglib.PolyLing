// VMDTraceWriter.cs
// VMD 適用のフレーム単位トレースを CSV へ書き出すユーティリティ。
//
// 列仕様（24 列）:
//   frame,bone,stage,
//   srcPosX,srcPosY,srcPosZ,srcQx,srcQy,srcQz,srcQw,
//   cnvPosX,cnvPosY,cnvPosZ,cnvQx,cnvQy,cnvQz,cnvQw,
//   axisX,axisY,axisZ,angleDeg,
//   worldPosX,worldPosY,worldPosZ
//
//   src* = その stage の入力、cnv* = その stage の出力。
//   stage = vmd_raw / after_flip / after_scale / after_rest / applied。
//   vmd_raw は src == cnv（生値をそのまま出す）。
//   axis/angleDeg は cnvQ を軸角に直したもの（w >= 0 に正規化するので angle は 0..180）。
//   world* は ComputeWorldMatrices() 後の値が必要なため、1 フレーム分を
//   バッファへ溜め、FlushFrame() で埋めてから書き出す。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>トレース 1 行分の保持データ（world は Flush 時に埋める）</summary>
    internal struct VMDTraceRow
    {
        public float Frame;
        public string Bone;
        public string Stage;
        public Vector3 SrcPos;
        public Quaternion SrcRot;
        public Vector3 CnvPos;
        public Quaternion CnvRot;
    }

    /// <summary>
    /// vmd_trace.csv のライタ。Open で新規作成（ヘッダ書き直し）、以降は同一ストリームへ追記する。
    /// </summary>
    public class VMDTraceWriter : IDisposable
    {
        private const string Header =
            "frame,bone,stage," +
            "srcPosX,srcPosY,srcPosZ,srcQx,srcQy,srcQz,srcQw," +
            "cnvPosX,cnvPosY,cnvPosZ,cnvQx,cnvQy,cnvQz,cnvQw," +
            "axisX,axisY,axisZ,angleDeg," +
            "worldPosX,worldPosY,worldPosZ";

        private StreamWriter _writer;
        private readonly List<VMDTraceRow> _pending = new List<VMDTraceRow>();

        /// <summary>出力先フルパス（Open 済みのとき有効）</summary>
        public string FilePath { get; private set; }

        /// <summary>ストリームが開いているか</summary>
        public bool IsOpen => _writer != null;

        /// <summary>バッファ済み行数</summary>
        public int PendingCount => _pending.Count;

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

        /// <summary>ストリームを閉じる。未フラッシュのバッファは破棄する。</summary>
        public void Close()
        {
            _pending.Clear();
            if (_writer != null)
            {
                try { _writer.Flush(); _writer.Dispose(); }
                catch (Exception ex) { Debug.LogError($"[VMDTraceWriter] Close failed: {ex.Message}"); }
                _writer = null;
            }
            FilePath = null;
        }

        public void Dispose() => Close();

        // ================================================================
        // 行の追加とフラッシュ
        // ================================================================

        /// <summary>1 stage 分の行をバッファへ積む。</summary>
        public void Add(float frame, string bone, string stage,
                        Vector3 srcPos, Quaternion srcRot,
                        Vector3 cnvPos, Quaternion cnvRot)
        {
            if (_writer == null) return;

            _pending.Add(new VMDTraceRow
            {
                Frame  = frame,
                Bone   = bone,
                Stage  = stage,
                SrcPos = srcPos,
                SrcRot = srcRot,
                CnvPos = cnvPos,
                CnvRot = cnvRot,
            });
        }

        /// <summary>
        /// バッファ済みの行を書き出す。world 列は worldResolver（ボーン名 → ワールド位置）で埋める。
        /// ComputeWorldMatrices() の後に呼ぶこと。
        /// </summary>
        public void FlushFrame(Func<string, Vector3> worldResolver)
        {
            if (_writer == null) { _pending.Clear(); return; }

            var sb = new StringBuilder();
            foreach (var row in _pending)
            {
                Vector3 world = worldResolver != null ? worldResolver(row.Bone) : Vector3.zero;

                Quaternion q = Canonical(row.CnvRot);
                q.ToAngleAxis(out float angleDeg, out Vector3 axis);
                if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) axis = Vector3.zero;

                sb.Length = 0;
                sb.Append(F(row.Frame)).Append(',');
                sb.Append(Escape(row.Bone)).Append(',');
                sb.Append(row.Stage).Append(',');
                AppendVector(sb, row.SrcPos);
                AppendQuaternion(sb, row.SrcRot);
                AppendVector(sb, row.CnvPos);
                AppendQuaternion(sb, row.CnvRot);
                AppendVector(sb, axis);
                sb.Append(F(angleDeg)).Append(',');
                sb.Append(F(world.x)).Append(',');
                sb.Append(F(world.y)).Append(',');
                sb.Append(F(world.z));

                _writer.WriteLine(sb.ToString());
            }
            _pending.Clear();
        }

        // ================================================================
        // 内部ユーティリティ
        // ================================================================

        /// <summary>w >= 0 の側へ揃える。回転は変わらず、軸角の angle が 0..180 に収まる。</summary>
        private static Quaternion Canonical(Quaternion q)
        {
            return q.w < 0f ? new Quaternion(-q.x, -q.y, -q.z, -q.w) : q;
        }

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
