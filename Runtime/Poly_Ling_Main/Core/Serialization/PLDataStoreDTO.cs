// PLDataStoreDTO.cs
// PLDataStore のシリアライズ用データ構造。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【形は ObjectGroupDTO に合わせる】
//   JsonUtility が扱える形（配列は List、Dictionary は使えない、
//   入れ子の配列は持てない）に落とす。
//
// 【ループ群を平たい 3 本で持つ】
//   ループごとの頂点列は長さが違う入れ子配列になる。
//   SelectionSetDTO の識別子の控えと同じく、平行配列にして入れ子を避ける。
//     loopStarts    … ループ i が loopVertices の何番目から始まるか。単調増加
//     loopVertices  … 全ループの頂点を連結したもの
//     loopCentroids … 重心の x,y,z を 3 個ずつ。長さ = loopStarts.Count * 3
//   ループ i の頂点は loopStarts[i] から loopStarts[i+1]（最後は末尾）まで。
//   PanelCommand.cs の BeltStarts と同じ区切り方。
//
// 【ObjectId は文字列で持つ】
//   ulong は JsonUtility が扱えない。10 進の文字列にして往復させる
//   （ObjectGroupDTO と同じ）。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Serialization
{
    /// <summary>PLDataEntry のシリアライズ用。</summary>
    [Serializable]
    public class PLDataEntryDTO
    {
        public string name        = "";
        public string kind        = "None";
        public string createdAt   = "";
        public string source      = "";
        public int    masterIndex = -1;

        /// <summary>対象の ObjectId（10 進文字列）。"0" = なし。</summary>
        public string objectId = "0";

        /// <summary>kind が IndexSet のときの中身。それ以外は null。</summary>
        public SelectionSetDTO indexSet;

        /// <summary>ループ i が loopVertices の何番目から始まるか。単調増加。</summary>
        public List<int> loopStarts = new List<int>();

        /// <summary>全ループの頂点を連結したもの。</summary>
        public List<int> loopVertices = new List<int>();

        /// <summary>重心の x,y,z を 3 個ずつ。長さは loopStarts の 3 倍。</summary>
        public List<float> loopCentroids = new List<float>();

        /// <summary>値の名前。以下 4 本は同じ長さ。</summary>
        public List<string> valueKeys = new List<string>();

        /// <summary>数値。valueIsText が true の位置は 0。</summary>
        public List<double> valueNumbers = new List<double>();

        /// <summary>文字列。valueIsText が false の位置は空。</summary>
        public List<string> valueTexts = new List<string>();

        /// <summary>文字列として持っているか。</summary>
        public List<bool> valueIsText = new List<bool>();

        // ================================================================
        // 変換
        // ================================================================

        public static PLDataEntryDTO From(PLDataEntry e)
        {
            if (e == null) return null;

            var dto = new PLDataEntryDTO
            {
                name        = e.Name ?? "",
                kind        = e.Kind.ToString(),
                createdAt   = e.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
                source      = e.Source ?? "",
                masterIndex = e.MasterIndex,
                objectId    = e.ObjectId.ToString(CultureInfo.InvariantCulture),
            };

            if (e.Kind == PLDataKind.IndexSet && e.IndexSet != null)
                dto.indexSet = SelectionSetDTO.FromSelectionSet(e.IndexSet);

            if (e.Kind == PLDataKind.LoopSet && e.Loops != null)
            {
                foreach (var loop in e.Loops)
                {
                    if (loop == null) continue;

                    dto.loopStarts.Add(dto.loopVertices.Count);
                    if (loop.Vertices != null) dto.loopVertices.AddRange(loop.Vertices);

                    dto.loopCentroids.Add(loop.Centroid.x);
                    dto.loopCentroids.Add(loop.Centroid.y);
                    dto.loopCentroids.Add(loop.Centroid.z);
                }
            }

            if (e.Kind == PLDataKind.ValueSet && e.Values != null)
            {
                foreach (var v in e.Values)
                {
                    dto.valueKeys.Add(v.Key ?? "");
                    dto.valueNumbers.Add(v.IsText ? 0.0 : v.Number);
                    dto.valueTexts.Add(v.IsText ? (v.Text ?? "") : "");
                    dto.valueIsText.Add(v.IsText);
                }
            }

            return dto;
        }

        public PLDataEntry ToEntry()
        {
            var e = new PLDataEntry
            {
                Name        = name ?? "",
                Source      = source ?? "",
                MasterIndex = masterIndex,
            };

            if (!string.IsNullOrEmpty(kind) && Enum.TryParse<PLDataKind>(kind, out var k))
                e.Kind = k;

            if (!string.IsNullOrEmpty(createdAt) &&
                DateTime.TryParse(createdAt, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out var dt))
                e.CreatedAt = dt;

            if (!string.IsNullOrEmpty(objectId) &&
                ulong.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var oid))
                e.ObjectId = oid;

            switch (e.Kind)
            {
                case PLDataKind.IndexSet:
                    e.IndexSet = indexSet?.ToSelectionSet() ?? new PartsSelectionSet(e.Name);
                    break;

                case PLDataKind.LoopSet:
                    e.Loops = BuildLoops();
                    break;

                case PLDataKind.ValueSet:
                    e.Values = BuildValues();
                    break;
            }

            return e;
        }

        /// <summary>平たい 3 本からループ群を組み直す。欠けた欄は落とす。</summary>
        private List<PLDataLoop> BuildLoops()
        {
            var loops = new List<PLDataLoop>();
            if (loopStarts == null || loopVertices == null) return loops;

            int n = loopStarts.Count;
            for (int i = 0; i < n; i++)
            {
                int from = loopStarts[i];
                int to   = (i + 1 < n) ? loopStarts[i + 1] : loopVertices.Count;

                if (from < 0) from = 0;
                if (to > loopVertices.Count) to = loopVertices.Count;
                if (to < from) to = from;

                var loop = new PLDataLoop();
                for (int k = from; k < to; k++) loop.Vertices.Add(loopVertices[k]);

                if (loopCentroids != null && (i * 3 + 2) < loopCentroids.Count)
                {
                    loop.Centroid = new Vector3(
                        loopCentroids[i * 3], loopCentroids[i * 3 + 1], loopCentroids[i * 3 + 2]);
                }

                loops.Add(loop);
            }

            return loops;
        }

        /// <summary>4 本の平行配列から値の一覧を組み直す。そろっているぶんだけ読む。</summary>
        private List<PLDataValue> BuildValues()
        {
            var values = new List<PLDataValue>();
            if (valueKeys == null || valueNumbers == null ||
                valueTexts == null || valueIsText == null) return values;

            int n = valueKeys.Count;
            if (valueNumbers.Count < n) n = valueNumbers.Count;
            if (valueTexts.Count   < n) n = valueTexts.Count;
            if (valueIsText.Count  < n) n = valueIsText.Count;

            for (int i = 0; i < n; i++)
            {
                values.Add(valueIsText[i]
                    ? PLDataValue.Str(valueKeys[i], valueTexts[i])
                    : PLDataValue.Num(valueKeys[i], valueNumbers[i]));
            }

            return values;
        }
    }
}
