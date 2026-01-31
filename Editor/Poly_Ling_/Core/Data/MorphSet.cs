// Assets/Editor/Poly_Ling_/Core/Data/MorphSet.cs
// モーフセット（複数メッシュのモーフをグループ化）
// PMXエクスポート時に統合して1つのモーフとして出力

using System;
using System.Collections.Generic;

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
    /// モーフセット
    /// 複数メッシュのモーフを1つの名前でグループ化
    /// </summary>
    [Serializable]
    public class MorphSet
    {
        /// <summary>モーフ名</summary>
        public string Name = "";

        /// <summary>英語名</summary>
        public string NameEnglish = "";

        /// <summary>パネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int Panel = 3;

        /// <summary>モーフタイプ</summary>
        public MorphType Type = MorphType.Vertex;

        /// <summary>所属するモーフメッシュのインデックスリスト</summary>
        public List<int> MeshIndices = new List<int>();

        /// <summary>作成日時</summary>
        public DateTime CreatedAt = DateTime.Now;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public MorphSet()
        {
        }

        public MorphSet(string name, MorphType type = MorphType.Vertex)
        {
            Name = name;
            Type = type;
        }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>有効なセットか</summary>
        public bool IsValid => !string.IsNullOrEmpty(Name) && MeshIndices.Count > 0;

        /// <summary>メッシュ数</summary>
        public int MeshCount => MeshIndices.Count;

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

        /// <summary>メッシュを追加</summary>
        public void AddMesh(int meshIndex)
        {
            if (!MeshIndices.Contains(meshIndex))
            {
                MeshIndices.Add(meshIndex);
            }
        }

        /// <summary>メッシュを削除</summary>
        public bool RemoveMesh(int meshIndex)
        {
            return MeshIndices.Remove(meshIndex);
        }

        /// <summary>メッシュを含むか</summary>
        public bool ContainsMesh(int meshIndex)
        {
            return MeshIndices.Contains(meshIndex);
        }

        /// <summary>
        /// メッシュインデックス調整（メッシュ削除時）
        /// </summary>
        public void AdjustIndicesOnRemove(int removedIndex)
        {
            MeshIndices.Remove(removedIndex);
            for (int i = 0; i < MeshIndices.Count; i++)
            {
                if (MeshIndices[i] > removedIndex)
                {
                    MeshIndices[i]--;
                }
            }
        }

        /// <summary>
        /// メッシュインデックス調整（メッシュ挿入時）
        /// </summary>
        public void AdjustIndicesOnInsert(int insertedIndex)
        {
            for (int i = 0; i < MeshIndices.Count; i++)
            {
                if (MeshIndices[i] >= insertedIndex)
                {
                    MeshIndices[i]++;
                }
            }
        }

        // ================================================================
        // クローン
        // ================================================================

        public MorphSet Clone()
        {
            return new MorphSet
            {
                Name = this.Name,
                NameEnglish = this.NameEnglish,
                Panel = this.Panel,
                Type = this.Type,
                MeshIndices = new List<int>(this.MeshIndices),
                CreatedAt = this.CreatedAt
            };
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            return $"MorphSet[{Name}]: {Type}, {MeshCount} meshes";
        }
    }
}
