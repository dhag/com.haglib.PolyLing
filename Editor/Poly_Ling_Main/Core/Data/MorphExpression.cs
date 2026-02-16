// Assets/Editor/Poly_Ling_/Core/Data/MorphExpression.cs
// モーフセット（複数メッシュのモーフをグループ化）
// PMXエクスポート時に統合して1つのモーフとして出力

using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly_Ling.Data
{
    /// <summary>
    /// モーフタイプ（PMX仕様準拠）
    /// </summary>
    public enum MorphType
    {
        Group = 0,
        Vertex = 1,
        Bone = 2,
        UV = 3,
        UV1 = 4,
        UV2 = 5,
        UV3 = 6,
        UV4 = 7,
        Material = 8,
        Flip = 9,
        Impulse = 10
    }

    /// <summary>
    /// モーフメッシュエントリ（メッシュインデックス＋ウェイト）
    /// </summary>
    [Serializable]
    public struct MorphMeshEntry
    {
        /// <summary>MeshContextリスト内のインデックス</summary>
        public int MeshIndex;

        /// <summary>このメッシュに適用するウェイト（グループモーフのWeight由来）</summary>
        public float Weight;

        public MorphMeshEntry(int meshIndex, float weight = 1f)
        {
            MeshIndex = meshIndex;
            Weight = weight;
        }

        public override string ToString()
        {
            return $"({MeshIndex}, W:{Weight:F2})";
        }
    }

    /// <summary>
    /// モーフセット
    /// 複数メッシュのモーフを1つの名前でグループ化
    /// </summary>
    [Serializable]
    public class MorphExpression
    {
        /// <summary>モーフ名</summary>
        public string Name = "";

        /// <summary>英語名</summary>
        public string NameEnglish = "";

        /// <summary>パネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int Panel = 3;

        /// <summary>モーフタイプ</summary>
        public MorphType Type = MorphType.Vertex;

        /// <summary>所属するモーフメッシュのエントリリスト（インデックス＋ウェイト）</summary>
        public List<MorphMeshEntry> MeshEntries = new List<MorphMeshEntry>();

        /// <summary>
        /// 後方互換用：メッシュインデックスのみのリスト（get/set対応）
        /// </summary>
        public List<int> MeshIndices
        {
            get => MeshEntries.Select(e => e.MeshIndex).ToList();
            set
            {
                MeshEntries.Clear();
                if (value != null)
                {
                    foreach (var idx in value)
                        MeshEntries.Add(new MorphMeshEntry(idx, 1f));
                }
            }
        }

        /// <summary>作成日時</summary>
        public DateTime CreatedAt = DateTime.Now;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public MorphExpression()
        {
        }

        public MorphExpression(string name, MorphType type = MorphType.Vertex)
        {
            Name = name;
            Type = type;
        }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>有効なセットか</summary>
        public bool IsValid => !string.IsNullOrEmpty(Name) && MeshEntries.Count > 0;

        /// <summary>メッシュ数</summary>
        public int MeshCount => MeshEntries.Count;

        /// <summary>頂点モーフか</summary>
        public bool IsVertexMorph => Type == MorphType.Vertex;

        /// <summary>UVモーフか</summary>
        public bool IsUVMorph => Type == MorphType.UV || 
                                  Type == MorphType.UV1 || 
                                  Type == MorphType.UV2 || 
                                  Type == MorphType.UV3 || 
                                  Type == MorphType.UV4;

        // ================================================================
        // 操作
        // ================================================================

        /// <summary>メッシュを追加（weight=1.0）</summary>
        public void AddMesh(int meshIndex)
        {
            AddMesh(meshIndex, 1f);
        }

        /// <summary>メッシュをウェイト付きで追加</summary>
        public void AddMesh(int meshIndex, float weight)
        {
            if (!ContainsMesh(meshIndex))
            {
                MeshEntries.Add(new MorphMeshEntry(meshIndex, weight));
            }
        }

        /// <summary>メッシュを削除</summary>
        public bool RemoveMesh(int meshIndex)
        {
            int idx = MeshEntries.FindIndex(e => e.MeshIndex == meshIndex);
            if (idx >= 0)
            {
                MeshEntries.RemoveAt(idx);
                return true;
            }
            return false;
        }

        /// <summary>メッシュを含むか</summary>
        public bool ContainsMesh(int meshIndex)
        {
            return MeshEntries.Exists(e => e.MeshIndex == meshIndex);
        }

        /// <summary>メッシュインデックスからエントリを取得</summary>
        public MorphMeshEntry? GetEntry(int meshIndex)
        {
            int idx = MeshEntries.FindIndex(e => e.MeshIndex == meshIndex);
            return idx >= 0 ? MeshEntries[idx] : (MorphMeshEntry?)null;
        }

        /// <summary>
        /// メッシュインデックス調整（メッシュ削除時）
        /// </summary>
        public void AdjustIndicesOnRemove(int removedIndex)
        {
            // 該当インデックスのエントリを削除
            MeshEntries.RemoveAll(e => e.MeshIndex == removedIndex);

            // removedIndexより大きいインデックスを-1
            for (int i = 0; i < MeshEntries.Count; i++)
            {
                if (MeshEntries[i].MeshIndex > removedIndex)
                {
                    var entry = MeshEntries[i];
                    entry.MeshIndex--;
                    MeshEntries[i] = entry;
                }
            }
        }

        /// <summary>
        /// メッシュインデックス調整（メッシュ挿入時）
        /// </summary>
        public void AdjustIndicesOnInsert(int insertedIndex)
        {
            for (int i = 0; i < MeshEntries.Count; i++)
            {
                if (MeshEntries[i].MeshIndex >= insertedIndex)
                {
                    var entry = MeshEntries[i];
                    entry.MeshIndex++;
                    MeshEntries[i] = entry;
                }
            }
        }

        // ================================================================
        // クローン
        // ================================================================

        public MorphExpression Clone()
        {
            return new MorphExpression
            {
                Name = this.Name,
                NameEnglish = this.NameEnglish,
                Panel = this.Panel,
                Type = this.Type,
                MeshEntries = this.MeshEntries.Select(e => new MorphMeshEntry(e.MeshIndex, e.Weight)).ToList(),
                CreatedAt = this.CreatedAt
            };
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            return $"MorphExpression[{Name}]: {Type}, {MeshCount} meshes";
        }
    }
}
