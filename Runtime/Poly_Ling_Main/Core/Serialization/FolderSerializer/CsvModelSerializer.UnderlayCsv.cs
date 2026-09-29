// CsvModelSerializer.UnderlayCsv.cs
// CSV モデル入出力：underlay.csv（下絵：モデルレベル）。
// Runtime/Poly_Ling_Main/Core/Serialization/FolderSerializer/ に配置
//
// 1 行 1 方向。画像の無い方向は書かない。1 つも無ければファイルを作らない（あれば消す）。
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

            var u = model.Underlay;
            if (u == null || u.IsEmpty)
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
    }
}
