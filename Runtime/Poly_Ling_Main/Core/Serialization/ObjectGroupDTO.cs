// ObjectGroupDTO.cs
// オブジェクトグループのシリアライズ用データ構造。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【形は MorphExpressionDTO に合わせる】
//   JsonUtility が扱える形（配列は List、Dictionary は使えない）に落とす。
//   Args と MeshRefIds はどちらも辞書なので、キーと値の対を並べた List にする。
//
// 【並びを固定する】
//   Dictionary の列挙順は保証されないので、書き出す前にキー順へ並べ替える
//   （ObjectGroup.SortedArgs / SortedMeshRefIds）。並べ替えないと、
//   中身が同じでも保存のたびにファイルの差分が出る。
//
// 【ObjectId は文字列で持つ】
//   ulong は JsonUtility が扱えない。10 進の文字列にして往復させる。

using System;
using System.Collections.Generic;
using System.Globalization;
using Poly_Ling.Data;

namespace Poly_Ling.Serialization
{
    /// <summary>キーと値の対（Args 用）。</summary>
    [Serializable]
    public class ObjectGroupArgDTO
    {
        public string key = "";
        public string value = "";
    }

    /// <summary>キーと ObjectId 列の対（MeshRefIds 用）。</summary>
    [Serializable]
    public class ObjectGroupMeshRefDTO
    {
        public string key = "";

        /// <summary>ObjectId の 10 進文字列。並びは Args の索引配列と 1 対 1。</summary>
        public List<string> objectIds = new List<string>();
    }

    /// <summary>オブジェクトグループのシリアライズ用。</summary>
    [Serializable]
    public class ObjectGroupDTO
    {
        public string name = "";
        public string action = "";

        public List<ObjectGroupArgDTO>     args     = new List<ObjectGroupArgDTO>();
        public List<ObjectGroupMeshRefDTO> meshRefs = new List<ObjectGroupMeshRefDTO>();

        /// <summary>出力先の ObjectId（10 進文字列）。"0" = なし。</summary>
        public string outputObjectId = "0";

        /// <summary>退避の ObjectId（10 進文字列）。"0" = なし。</summary>
        public string stashObjectId = "0";

        public string sourceDigest = "";
        public bool   autoUpdate;

        /// <summary>作成日時（ISO 8601 形式）。</summary>
        public string createdAt = "";

        // ================================================================
        // 変換
        // ================================================================

        public static ObjectGroupDTO FromObjectGroup(ObjectGroup g)
        {
            if (g == null) return null;

            var dto = new ObjectGroupDTO
            {
                name           = g.Name ?? "",
                action         = g.Action ?? "",
                outputObjectId = g.OutputObjectId.ToString(CultureInfo.InvariantCulture),
                stashObjectId  = g.StashObjectId.ToString(CultureInfo.InvariantCulture),
                sourceDigest   = g.SourceDigest ?? "",
                autoUpdate     = g.AutoUpdate,
                createdAt      = g.CreatedAt.ToString("o"),
            };

            foreach (var kv in g.SortedArgs())
                dto.args.Add(new ObjectGroupArgDTO { key = kv.Key, value = kv.Value ?? "" });

            foreach (var kv in g.SortedMeshRefIds())
            {
                var e = new ObjectGroupMeshRefDTO { key = kv.Key };
                if (kv.Value != null)
                    foreach (ulong id in kv.Value)
                        e.objectIds.Add(id.ToString(CultureInfo.InvariantCulture));
                dto.meshRefs.Add(e);
            }

            return dto;
        }

        public ObjectGroup ToObjectGroup()
        {
            var g = new ObjectGroup(name ?? "")
            {
                Action         = action ?? "",
                OutputObjectId = ParseId(outputObjectId),
                StashObjectId  = ParseId(stashObjectId),
                SourceDigest   = sourceDigest ?? "",
                AutoUpdate     = autoUpdate,
            };

            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(
                    createdAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var t))
            {
                g.CreatedAt = t;
            }

            if (args != null)
                foreach (var a in args)
                    if (a != null && !string.IsNullOrEmpty(a.key)) g.SetArg(a.key, a.value);

            if (meshRefs != null)
            {
                foreach (var r in meshRefs)
                {
                    if (r == null || string.IsNullOrEmpty(r.key)) continue;
                    var ids = new List<ulong>();
                    if (r.objectIds != null)
                        foreach (var s in r.objectIds) ids.Add(ParseId(s));
                    g.SetMeshRefIds(r.key, ids);
                }
            }

            return g;
        }

        /// <summary>
        /// ObjectId の文字列を戻す。読めなければ 0（＝参照なし）。
        /// 0 を返すのは黙って別のオブジェクトを指すより安全なため。
        /// </summary>
        private static ulong ParseId(string s)
            => (!string.IsNullOrEmpty(s) &&
                ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v))
                ? v : 0UL;
    }
}
