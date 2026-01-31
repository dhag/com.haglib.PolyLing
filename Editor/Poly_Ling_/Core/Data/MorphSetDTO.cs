// Assets/Editor/Poly_Ling_/Core/Data/MorphSetDTO.cs
// モーフセットのシリアライズ用データ構造

using System;
using System.Collections.Generic;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// モーフセットのシリアライズ用
    /// </summary>
    [Serializable]
    public class MorphSetDTO
    {
        /// <summary>モーフ名</summary>
        public string name = "";

        /// <summary>英語名</summary>
        public string nameEnglish = "";

        /// <summary>パネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int panel = 3;

        /// <summary>モーフタイプ（1=頂点, 3=UV, ...）</summary>
        public int type = 1;

        /// <summary>メッシュインデックスリスト</summary>
        public List<int> meshIndices = new List<int>();

        /// <summary>作成日時（ISO 8601形式）</summary>
        public string createdAt;

        // ================================================================
        // 変換
        // ================================================================

        /// <summary>
        /// MorphSetからDTOを作成
        /// </summary>
        public static MorphSetDTO FromMorphSet(Data.MorphSet set)
        {
            if (set == null) return null;

            return new MorphSetDTO
            {
                name = set.Name ?? "",
                nameEnglish = set.NameEnglish ?? "",
                panel = set.Panel,
                type = (int)set.Type,
                meshIndices = set.MeshIndices != null 
                    ? new List<int>(set.MeshIndices) 
                    : new List<int>(),
                createdAt = set.CreatedAt.ToString("o")
            };
        }

        /// <summary>
        /// DTOからMorphSetを作成
        /// </summary>
        public Data.MorphSet ToMorphSet()
        {
            var set = new Data.MorphSet
            {
                Name = name ?? "",
                NameEnglish = nameEnglish ?? "",
                Panel = panel,
                Type = (Data.MorphType)type,
                MeshIndices = meshIndices != null 
                    ? new List<int>(meshIndices) 
                    : new List<int>()
            };

            if (!string.IsNullOrEmpty(createdAt) && DateTime.TryParse(createdAt, out var dt))
            {
                set.CreatedAt = dt;
            }

            return set;
        }
    }
}
