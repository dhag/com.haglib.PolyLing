// MorphExpression.cs
// 表情（PolyLing_モーフ整理計画.md 3.5）。
//
// 【中身】表情は「形の名前 × 重み」の並び（GroupChildren）と VRM の付帯データだけを持つ。
//   ・グループの表情（Type == Group）：子の名前 × 重み。子は形の名前（ShapeBlendOps.Resolve で解決）。
//   ・頂点の表情（Type == Vertex 等）：同じ名前の形を指す（形が正本）。VRM の付帯データを持つためだけに残す。
//   名前・英語名・パネルは表情の表示名。形の名前・英語名・パネルは形（ShapeDef）が持つ。
//   段 7 でオブジェクト番号で隠しオブジェクトを指す欄（MeshEntries・IsSymmetric）は廃止した。
// 型の名前に Morph を残しているのは、PMX のモーフ（グループ・頂点…の種類）に対応する表情の型として、
// 保存形式（morphExpressions）とリモートの語彙で使われているため。

using System;
using System.Collections.Generic;
using System.Linq;

namespace Poly_Ling.Data
{
    /// <summary>
    /// モーフの種類（PMX 仕様準拠の番号）。表情の種類として使う。
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

    /// <summary>グループの表情の子への参照（形の名前 × 重み）。</summary>
    [Serializable]
    public struct MorphGroupChild
    {
        /// <summary>子の名前（形の名前）。</summary>
        public string Name;

        /// <summary>重み。</summary>
        public float Weight;

        public MorphGroupChild(string name, float weight)
        {
            Name = name;
            Weight = weight;
        }
    }

    /// <summary>表情。</summary>
    [Serializable]
    public class MorphExpression
    {
        /// <summary>表情の名前</summary>
        public string Name = "";

        /// <summary>英語名</summary>
        public string NameEnglish = "";

        /// <summary>パネル（PMX: 0=眉, 1=目, 2=口, 3=その他）</summary>
        public int Panel = 3;

        /// <summary>種類</summary>
        public MorphType Type = MorphType.Vertex;

        /// <summary>作成日時</summary>
        public DateTime CreatedAt = DateTime.Now;

        /// <summary>
        /// グループの子（Type == MorphType.Group のとき）。形の名前 × 重み。
        /// 参照は名前で持つ（番号は並び替えで壊れる）。
        /// </summary>
        public List<MorphGroupChild> GroupChildren = new List<MorphGroupChild>();

        /// <summary>
        /// VRM 1.0 表情の付帯データ（null = VRM 固有の値なし）。
        /// IsBinary / Override / 材質色バインド / UV バインドを保持する。
        /// PolyLing は評価しないが、読み書きで失ってはいけない値。
        /// 規約は VrmExpressionData.cs 冒頭を正典とする。
        /// </summary>
        public VrmExpressionData Vrm = null;

        public MorphExpression()
        {
        }

        public MorphExpression(string name, MorphType type = MorphType.Vertex)
        {
            Name = name;
            Type = type;
        }

        /// <summary>頂点の表情か（同じ名前の形を指す）</summary>
        public bool IsVertexMorph => Type == MorphType.Vertex;

        /// <summary>UV の表情か（同じ名前の UV の形を指す）</summary>
        public bool IsUVMorph => Type == MorphType.UV;

        public MorphExpression Clone()
        {
            return new MorphExpression
            {
                Name = this.Name,
                NameEnglish = this.NameEnglish,
                Panel = this.Panel,
                Type = this.Type,
                GroupChildren = this.GroupChildren.Select(c => new MorphGroupChild(c.Name, c.Weight)).ToList(),
                Vrm = this.Vrm?.Clone(),
                CreatedAt = this.CreatedAt
            };
        }

        public override string ToString()
        {
            return $"MorphExpression[{Name}]: {Type}, {GroupChildren.Count} children";
        }
    }
}
