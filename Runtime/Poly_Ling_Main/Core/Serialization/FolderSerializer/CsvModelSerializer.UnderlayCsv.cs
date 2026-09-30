// CsvModelSerializer.UnderlayCsv.cs
// CSV モデル入出力：underlay.csv（下絵：モデルレベル）。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置
//
// 1 行 1 方向。画像の無い方向は書かない。1 つも無ければファイルを作らない（あれば消す）。
// 作業板スロットは underlay_plates.csv（このファイルの下半分）。
// 方向は名前で持つ（UnderlayData.NameOf）。JSON 側は ModelDTO.Underlay.cs。

using System;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization.FolderSerializer
{
    public static partial class CsvModelSerializer
    {
        private const string UnderlayCsvName = "underlay.csv";

        private static void WriteOrDeleteUnderlayCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, UnderlayCsvName);

            // 作業板スロットは別のファイル（underlay_plates.csv）。このファイルの形式は変えない。
            WriteOrDeleteUnderlayPlatesCsv(folderPath, model);

            var u = model.Underlay;
            if (u == null || u.HasNoDirectionImages)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_Underlay,version,1.0");
            sb.AppendLine("#direction,filePath,topLeftX,topLeftY,scaleOriginX,scaleOriginY,scaleX,scaleY,corner0X,corner0Y,corner0Z,corner1X,corner1Y,corner1Z,viewRelative,contrast,intensity");
            foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
            {
                var s = u.Get(dir);
                if (s == null || s.IsEmpty) continue;
                sb.Append(UnderlayData.NameOf(dir)).Append(',')
                  .Append(Esc(s.FilePath)).Append(',')
                  .Append(Fl(s.TopLeft.x)).Append(',').Append(Fl(s.TopLeft.y)).Append(',')
                  .Append(Fl(s.ScaleOrigin.x)).Append(',').Append(Fl(s.ScaleOrigin.y)).Append(',')
                  .Append(Fl(s.Scale.x)).Append(',').Append(Fl(s.Scale.y)).Append(',')
                  .Append(Fl(s.Corner0.x)).Append(',').Append(Fl(s.Corner0.y)).Append(',').Append(Fl(s.Corner0.z)).Append(',')
                  .Append(Fl(s.Corner1.x)).Append(',').Append(Fl(s.Corner1.y)).Append(',').Append(Fl(s.Corner1.z)).Append(',')
                  .Append(s.ViewRelative ? "1" : "0").Append(',')
                  .Append(Fl(s.Contrast)).Append(',').Append(Fl(s.Intensity))
                  .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void ReadUnderlayCsv(string path, ModelContext model)
        {
            var u = new UnderlayData();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2) continue;
                if (!UnderlayData.TryParse(cols[0], out var dir)) continue;
                if (string.IsNullOrEmpty(cols[1])) continue;

                var s = u.Get(dir);
                s.FilePath    = cols[1];
                s.TopLeft     = new Vector2(PFl(cols, 2),     PFl(cols, 3));
                s.ScaleOrigin = new Vector2(PFl(cols, 4),     PFl(cols, 5));
                s.Scale       = new Vector2(PFl(cols, 6, 1f), PFl(cols, 7, 1f));
                s.Corner0     = new Vector3(PFl(cols, 8),  PFl(cols, 9),  PFl(cols, 10));
                s.Corner1     = new Vector3(PFl(cols, 11), PFl(cols, 12), PFl(cols, 13));
                // 15 列目が無い行は以前の画素基準（表示時に今の形式へ直す）。
                s.ViewRelative = cols.Length > 14 && cols[14].Trim() == "1";
                // 16・17 列目（コントラスト・明るさ）が無い行は 1（元画像のまま）。
                s.Contrast     = Mathf.Clamp01(PFl(cols, 15, 1f));
                s.Intensity    = Mathf.Clamp01(PFl(cols, 16, 1f));
            }

            model.Underlay = u.IsEmpty ? null : u;
        }

        // ================================================================
        // underlay_plates.csv（作業板スロット。PolyLing_UV_Billboard_Design.md 8.3・8.4）
        //   1 行 1 スロット。対象は代理オブジェクトの安定 ID（10 進）。2 隅は代理のローカル XY。
        //   スロットが 1 つも無ければファイルを作らない（あれば消す）。
        // ================================================================

        private const string UnderlayPlatesCsvName = "underlay_plates.csv";

        private static void WriteOrDeleteUnderlayPlatesCsv(string folderPath, ModelContext model)
        {
            string path = Path.Combine(folderPath, UnderlayPlatesCsvName);

            var u = model.Underlay;
            int count = 0;
            if (u != null) foreach (var p in u.Plates) if (p != null && !p.IsEmpty) count++;
            if (count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing_UnderlayPlates,version,1.0");
            sb.AppendLine("#objectId,filePath,corner0X,corner0Y,corner1X,corner1Y,contrast,intensity");
            foreach (var p in u.Plates)
            {
                if (p == null || p.IsEmpty) continue;
                sb.Append(p.ObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                  .Append(Esc(p.FilePath)).Append(',')
                  .Append(Fl(p.Corner0.x)).Append(',').Append(Fl(p.Corner0.y)).Append(',')
                  .Append(Fl(p.Corner1.x)).Append(',').Append(Fl(p.Corner1.y)).Append(',')
                  .Append(Fl(p.Contrast)).Append(',').Append(Fl(p.Intensity))
                  .AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>作業板スロットを読み、モデルの下絵へ足す（方向スロットの読込の後に呼ぶ）。</summary>
        private static void ReadUnderlayPlatesCsv(string path, ModelContext model)
        {
            var u = model.Underlay ?? new UnderlayData();

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length < 2 || string.IsNullOrEmpty(cols[1])) continue;
                if (!ulong.TryParse(cols[0].Trim(), System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out ulong id)) continue;

                u.SetPlate(new UnderlayPlateSlotData
                {
                    ObjectId  = id,
                    FilePath  = cols[1],
                    Corner0   = new Vector2(PFl(cols, 2), PFl(cols, 3)),
                    Corner1   = new Vector2(PFl(cols, 4), PFl(cols, 5)),
                    Contrast  = Mathf.Clamp01(PFl(cols, 6, 1f)),
                    Intensity = Mathf.Clamp01(PFl(cols, 7, 1f)),
                });
            }

            model.Underlay = u.IsEmpty ? null : u;
        }
    }
}
