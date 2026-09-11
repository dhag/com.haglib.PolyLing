// Assets/Editor/Poly_Ling_/Core/Data/MorphExpressionDTO.cs
// モーフエクスプレッションのシリアライズ用データ構造

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

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

    /// <summary>材質色バインドのシリアライズ用（VRM 表情）。</summary>
    [Serializable]
    public class VrmMaterialColorBindDTO
    {
        public string materialName = "";
        public int bindType = 0;            // VrmMaterialColorType
        public float[] targetValue;         // [r,g,b,a]
    }

    /// <summary>UV バインドのシリアライズ用（VRM 表情。Unity 側の値）。</summary>
    [Serializable]
    public class VrmTextureTransformBindDTO
    {
        public string materialName = "";
        public float[] scaling;             // [x,y]
        public float[] offset;              // [x,y]
    }

    /// <summary>
    /// VRM 表情の付帯データのシリアライズ用。
    /// 規約は VrmExpressionData.cs 冒頭を正典とする。
    /// 旧 JSON には欄が無く、null のまま＝VRM 固有の値なし。
    /// </summary>
    [Serializable]
    public class VrmExpressionDTO
    {
        public bool isBinary = false;
        public int overrideBlink = 0;       // VrmExpressionOverride
        public int overrideLookAt = 0;      // VrmExpressionOverride
        public int overrideMouth = 0;       // VrmExpressionOverride
        public List<VrmMaterialColorBindDTO> materialColorBinds;
        public List<VrmTextureTransformBindDTO> textureTransformBinds;

        public static VrmExpressionDTO From(VrmExpressionData src)
        {
            if (src == null) return null;

            var dto = new VrmExpressionDTO
            {
                isBinary       = src.IsBinary,
                overrideBlink  = (int)src.OverrideBlink,
                overrideLookAt = (int)src.OverrideLookAt,
                overrideMouth  = (int)src.OverrideMouth,
                materialColorBinds    = new List<VrmMaterialColorBindDTO>(),
                textureTransformBinds = new List<VrmTextureTransformBindDTO>(),
            };

            if (src.MaterialColorBinds != null)
            {
                foreach (var b in src.MaterialColorBinds)
                {
                    if (b == null) continue;
                    var v = b.TargetValue;
                    dto.materialColorBinds.Add(new VrmMaterialColorBindDTO
                    {
                        materialName = b.MaterialName ?? "",
                        bindType     = (int)b.BindType,
                        targetValue  = new[] { v.x, v.y, v.z, v.w },
                    });
                }
            }

            if (src.TextureTransformBinds != null)
            {
                foreach (var b in src.TextureTransformBinds)
                {
                    if (b == null) continue;
                    dto.textureTransformBinds.Add(new VrmTextureTransformBindDTO
                    {
                        materialName = b.MaterialName ?? "",
                        scaling      = new[] { b.Scaling.x, b.Scaling.y },
                        offset       = new[] { b.Offset.x, b.Offset.y },
                    });
                }
            }

            return dto;
        }

        public VrmExpressionData ToData()
        {
            var data = new VrmExpressionData
            {
                IsBinary       = isBinary,
                OverrideBlink  = (VrmExpressionOverride)overrideBlink,
                OverrideLookAt = (VrmExpressionOverride)overrideLookAt,
                OverrideMouth  = (VrmExpressionOverride)overrideMouth,
            };

            if (materialColorBinds != null)
            {
                foreach (var d in materialColorBinds)
                {
                    if (d == null) continue;
                    var t = d.targetValue;
                    data.MaterialColorBinds.Add(new VrmMaterialColorBind
                    {
                        MaterialName = d.materialName ?? "",
                        BindType     = (VrmMaterialColorType)d.bindType,
                        TargetValue  = (t != null && t.Length >= 4)
                            ? new UnityEngine.Vector4(t[0], t[1], t[2], t[3])
                            : UnityEngine.Vector4.zero,
                    });
                }
            }

            if (textureTransformBinds != null)
            {
                foreach (var d in textureTransformBinds)
                {
                    if (d == null) continue;
                    var s = d.scaling;
                    var o = d.offset;
                    data.TextureTransformBinds.Add(new VrmTextureTransformBind
                    {
                        MaterialName = d.materialName ?? "",
                        Scaling      = (s != null && s.Length >= 2)
                            ? new UnityEngine.Vector2(s[0], s[1]) : UnityEngine.Vector2.one,
                        Offset       = (o != null && o.Length >= 2)
                            ? new UnityEngine.Vector2(o[0], o[1]) : UnityEngine.Vector2.zero,
                    });
                }
            }

            return data;
        }
    }

    /// <summary>
    /// モーフエクスプレッションのシリアライズ用
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

        /// <summary>VRM 表情の付帯データ（null = なし）</summary>
        public VrmExpressionDTO vrm;

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
                createdAt = set.CreatedAt.ToString("o"),
                vrm = VrmExpressionDTO.From(set.Vrm)
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

            set.Vrm = vrm?.ToData();

            return set;
        }
    }
}
