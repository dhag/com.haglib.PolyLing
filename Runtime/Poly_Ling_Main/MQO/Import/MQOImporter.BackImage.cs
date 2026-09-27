// MQOImporter.BackImage.cs
// MQO インポート：下絵（BackImage チャンク）をモデルの下絵（UnderlayData）へ移す。
// Runtime/Poly_Ling_Main/MQO/Import/ に配置
//
// 2 隅の座標は頂点と同じ座標変換（ConvertPosition：倍率・軸反転）を通す。
// パートと軸の対応は MQOBackImageMapping にまとめてある。

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MQO
{
    public static partial class MQOImporter
    {
        /// <summary>下絵をモデルの下絵へ変換する。1 つも無ければ null。</summary>
        private static UnderlayData ConvertBackImages(MQODocument document, MQOImportSettings settings)
        {
            if (document?.BackImages == null || document.BackImages.Count == 0) return null;

            var data = new UnderlayData();
            foreach (var bi in document.BackImages)
            {
                if (bi == null || string.IsNullOrEmpty(bi.Path)) continue;
                if (!MQOBackImageMapping.TryDirectionOf(bi.Part, out var dir))
                {
                    Debug.LogWarning($"[MQOImporter] 下絵のパート名が分かりません: {bi.Part}");
                    continue;
                }

                var s = data.Get(dir);
                s.FilePath = MQOBackImageMapping.ResolvePath(bi.Path, settings?.BaseDir);
                s.Corner0  = ConvertPosition(MQOBackImageMapping.ToMqo3D(dir, bi.X0, bi.Y0), settings);
                s.Corner1  = ConvertPosition(MQOBackImageMapping.ToMqo3D(dir, bi.X1, bi.Y1), settings);
            }
            return data.IsEmpty ? null : data;
        }
    }
}
