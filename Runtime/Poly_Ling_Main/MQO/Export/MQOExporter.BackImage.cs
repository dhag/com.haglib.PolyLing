// MQOExporter.BackImage.cs
// MQO エクスポート：モデルの下絵（UnderlayData）を BackImage チャンクへ詰める。
// Runtime/Poly_Ling_Main/MQO/Export/ に配置
//
// 2 隅の座標は頂点と同じ座標変換（ConvertPosition：倍率・軸反転）を通す。
// パートと軸の対応は MQOBackImageMapping（読み込みと共通）。
// Persp / Ortho は MQO に対応するものが無いので書かない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.MQO
{
    public static partial class MQOExporter
    {
        /// <summary>
        /// 出力するメッシュが属するモデルの下絵を document.BackImages へ詰める。
        /// モデルに属していないメッシュだけのとき（部分エクスポートの一時リスト等）は何もしない。
        /// </summary>
        private static void AppendBackImages(MQODocument document, IList<MeshContext> meshContexts, MQOExportSettings settings)
        {
            if (document == null || meshContexts == null) return;

            Poly_Ling.Context.ModelContext model = null;
            foreach (var mc in meshContexts)
            {
                if (mc?.ParentModelContext == null) continue;
                model = mc.ParentModelContext;
                break;
            }

            var u = model?.Underlay;
            if (u == null || u.IsEmpty) return;

            foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
            {
                string part = MQOBackImageMapping.PartOf(dir);
                if (part == null) continue;

                var s = u.Get(dir);
                if (s == null || s.IsEmpty) continue;

                Vector2 a = MQOBackImageMapping.FromMqo3D(dir, ConvertPosition(s.Corner0, settings));
                Vector2 b = MQOBackImageMapping.FromMqo3D(dir, ConvertPosition(s.Corner1, settings));
                document.BackImages.Add(new MQOBackImage
                {
                    Part = part,
                    Path = s.FilePath,
                    X0 = a.x, Y0 = a.y,
                    X1 = b.x, Y1 = b.y,
                });
            }
        }
    }
}
