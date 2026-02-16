// Assets/Editor/Poly_Ling_/Core/Data/MorphExpressionDTO.cs
// モーフセットのシリアライズ用データ構造

using System;
using System.Collections.Generic;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// モーフメッシュエントリのシリアライズ用
    /// </summary>
    [Serializable]
    public class MorphMeshEntryDTO
    {
        public int meshIndex;
        public float weight = 1f;
    }

    /// <summary>
    /// モーフセットのシリアライズ用
    /// </summary>
    [Serializable]
    public class MorphExpressionDTO
    {
        /// <summary>モーフ名</summary>
        public string name = "";

        /// <summary>英語名</summary>
        public string nameEnglish = "";

        /// <summary>パネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int panel = 3;

        /// <summary>モーフタイプ（1=頂点, 3=UV, ...）</summary>
        public int type = 1;

        /// <summary>メッシュエントリリスト（インデックス＋ウェイト）</summary>
        public List<MorphMeshEntryDTO> meshEntries;

        /// <summary>後方互換用：旧形式のインデックスのみリスト（読み込み時のみ使用）</summary>
        public List<int> meshIndices;

        /// <summary>作成日時（ISO 8601形式）</summary>
        public string createdAt;

        // ================================================================
        // 変換
        // ================================================================

        /// <summary>
        /// MorphExpressionからDTOを作成
        /// </summary>
        public static MorphExpressionDTO FromMorphExpression(Data.MorphExpression set)
        {
            if (set == null) return null;

            var entries = new List<MorphMeshEntryDTO>();
            if (set.MeshEntries != null)
            {
                foreach (var e in set.MeshEntries)
                {
                    entries.Add(new MorphMeshEntryDTO { meshIndex = e.MeshIndex, weight = e.Weight });
                }
            }

            return new MorphExpressionDTO
            {
                name = set.Name ?? "",
                nameEnglish = set.NameEnglish ?? "",
                panel = set.Panel,
                type = (int)set.Type,
                meshEntries = entries,
                meshIndices = null,  // 新形式ではmeshEntriesのみ使用
                createdAt = set.CreatedAt.ToString("o")
            };
        }

        /// <summary>
        /// DTOからMorphExpressionを作成
        /// </summary>
        public Data.MorphExpression ToMorphExpression()
        {
            var set = new Data.MorphExpression
            {
                Name = name ?? "",
                NameEnglish = nameEnglish ?? "",
                Panel = panel,
                Type = (Data.MorphType)type,
                MeshEntries = new List<Data.MorphMeshEntry>()
            };

            // 新形式（meshEntries）があればそちらを使用
            if (meshEntries != null && meshEntries.Count > 0)
            {
                foreach (var e in meshEntries)
                {
                    set.MeshEntries.Add(new Data.MorphMeshEntry(e.meshIndex, e.weight));
                }
            }
            // 後方互換：旧形式（meshIndices）からweight=1.0で復元
            else if (meshIndices != null && meshIndices.Count > 0)
            {
                foreach (var idx in meshIndices)
                {
                    set.MeshEntries.Add(new Data.MorphMeshEntry(idx, 1f));
                }
            }

            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(createdAt, out var dt))
            {
                set.CreatedAt = dt;
            }

            return set;
        }
    }
}
