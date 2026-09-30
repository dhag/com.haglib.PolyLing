// ModelDTO.Underlay.cs
// DTO：下絵（UnderlayData）。JSON 保存用のフィールド型。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【方向は名前で持つ】
//   並び順に依存させない。読めない名前の行は捨てる。
// 【画像の無いスロットは書かない】
//   下絵が 1 つも無ければ DTO 自体を null にする（ModelContext.Underlay == null と同じ）。
// 【作業板スロット】
//   方向スロット（slots）とは別の一覧（plates）に書く。slots の読み書きは変えていない。
//   plates が無いのは以前のデータ。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Serialization
{
    /// <summary>下絵 DTO。</summary>
    [Serializable]
    public class UnderlayDTO
    {
        public List<UnderlaySlotDTO> slots = new List<UnderlaySlotDTO>();

        /// <summary>作業板スロット（方向スロットとは別の一覧。無い＝以前のデータ）。</summary>
        public List<UnderlayPlateDTO> plates = new List<UnderlayPlateDTO>();

        /// <summary>POCO → DTO。null・空は null。</summary>
        public static UnderlayDTO From(UnderlayData data)
        {
            if (data == null || data.IsEmpty) return null;
            var dto = new UnderlayDTO();
            foreach (var p in data.Plates)
            {
                if (p == null || p.IsEmpty) continue;
                dto.plates.Add(new UnderlayPlateDTO
                {
                    objectId = p.ObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    filePath = p.FilePath,
                    corner0  = new[] { p.Corner0.x, p.Corner0.y },
                    corner1  = new[] { p.Corner1.x, p.Corner1.y },
                    adjust   = new[] { p.Contrast, p.Intensity },
                });
            }
            foreach (UnderlayDirection dir in Enum.GetValues(typeof(UnderlayDirection)))
            {
                var s = data.Get(dir);
                if (s == null || s.IsEmpty) continue;
                dto.slots.Add(new UnderlaySlotDTO
                {
                    direction   = UnderlayData.NameOf(dir),
                    filePath    = s.FilePath,
                    topLeft     = new[] { s.TopLeft.x, s.TopLeft.y },
                    scaleOrigin = new[] { s.ScaleOrigin.x, s.ScaleOrigin.y },
                    scale       = new[] { s.Scale.x, s.Scale.y },
                    viewRelative = s.ViewRelative,
                    corner0     = new[] { s.Corner0.x, s.Corner0.y, s.Corner0.z },
                    corner1     = new[] { s.Corner1.x, s.Corner1.y, s.Corner1.z },
                    adjust      = new[] { s.Contrast, s.Intensity },
                });
            }
            return dto.slots.Count > 0 || dto.plates.Count > 0 ? dto : null;
        }

        /// <summary>DTO → POCO。null・空は null。</summary>
        public static UnderlayData ToData(UnderlayDTO dto)
        {
            if (dto == null) return null;
            var data = new UnderlayData();
            if (dto.plates != null)
            {
                foreach (var p in dto.plates)
                {
                    if (p == null || string.IsNullOrEmpty(p.filePath)) continue;
                    if (!ulong.TryParse(p.objectId ?? "", System.Globalization.NumberStyles.Integer,
                                        System.Globalization.CultureInfo.InvariantCulture, out ulong id)) continue;
                    var adjP = V2(p.adjust, Vector2.one);
                    data.SetPlate(new UnderlayPlateSlotData
                    {
                        ObjectId  = id,
                        FilePath  = p.filePath,
                        Corner0   = V2(p.corner0, Vector2.zero),
                        Corner1   = V2(p.corner1, Vector2.zero),
                        Contrast  = Mathf.Clamp01(adjP.x),
                        Intensity = Mathf.Clamp01(adjP.y),
                    });
                }
            }
            if (dto.slots == null) return data.IsEmpty ? null : data;
            foreach (var d in dto.slots)
            {
                if (d == null || string.IsNullOrEmpty(d.filePath)) continue;
                if (!UnderlayData.TryParse(d.direction, out var dir)) continue;
                var s = data.Get(dir);
                s.FilePath    = d.filePath;
                s.TopLeft     = V2(d.topLeft, Vector2.zero);
                s.ScaleOrigin = V2(d.scaleOrigin, Vector2.zero);
                s.Scale       = V2(d.scale, Vector2.one);
                s.ViewRelative = d.viewRelative;
                s.Corner0     = V3(d.corner0);
                s.Corner1     = V3(d.corner1);
                var adj       = V2(d.adjust, Vector2.one);
                s.Contrast    = Mathf.Clamp01(adj.x);
                s.Intensity   = Mathf.Clamp01(adj.y);
            }
            return data.IsEmpty ? null : data;
        }

        private static Vector2 V2(float[] a, Vector2 fallback)
            => (a != null && a.Length >= 2) ? new Vector2(a[0], a[1]) : fallback;

        private static Vector3 V3(float[] a)
            => (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }

    /// <summary>作業板スロット 1 件分の DTO。</summary>
    [Serializable]
    public class UnderlayPlateDTO
    {
        /// <summary>代理オブジェクトの安定 ID（10 進の文字列。桁落ちを避ける）。</summary>
        public string  objectId;
        public string  filePath;
        /// <summary>画像の 1 隅（代理のローカル XY）。</summary>
        public float[] corner0;
        /// <summary>向かい合う隅（代理のローカル XY）。</summary>
        public float[] corner1;
        /// <summary>表示調整 { コントラスト, 明るさ }（0〜1）。</summary>
        public float[] adjust;
    }

    /// <summary>下絵 1 方向分の DTO。</summary>
    [Serializable]
    public class UnderlaySlotDTO
    {
        public string  direction;
        public string  filePath;
        public float[] topLeft;
        public float[] scaleOrigin;
        public float[] scale;
        /// <summary>画面基準の値がビュー中央・ビューの高さ基準か。無い（false）なら以前の画素基準。</summary>
        public bool    viewRelative;
        public float[] corner0;
        public float[] corner1;
        /// <summary>表示調整 { コントラスト, 明るさ }（0〜1）。無い（以前のデータ）なら { 1, 1 }。</summary>
        public float[] adjust;
    }
}
