using Poly_Ling.Data;
// MeshCategory.cs
// メッシュオブジェクトのカテゴリ定義
// MeshTypeをグループ化してアクセスするための列挙型
//
// 番号はリモートの要求（category）で数として渡るので、値を明示して固定する。
// 4 はモーフ（段 7 で廃止。PolyLing_モーフ整理計画.md 3.12）の欠番。

namespace Poly_Ling.Data
{
    /// <summary>
    /// メッシュオブジェクトのカテゴリ
    /// MeshTypeの単一タイプまたは複合グループを表す
    /// </summary>
    public enum MeshCategory
    {
        /// <summary>通常のメッシュのみ (MeshType.Mesh)</summary>
        Mesh = 0,

        /// <summary>ベイクされたミラーメッシュのみ (MeshType.BakedMirror)</summary>
        BakedMirror = 1,

        /// <summary>描画可能メッシュ (Mesh + BakedMirror)</summary>
        Drawable = 2,

        /// <summary>ボーンのみ (MeshType.Bone)</summary>
        Bone = 3,

        /// <summary>剛体のみ (MeshType.RigidBody)</summary>
        RigidBody = 5,

        /// <summary>剛体ジョイントのみ (MeshType.RigidBodyJoint)</summary>
        RigidBodyJoint = 6,

        /// <summary>ヘルパーのみ (MeshType.Helper)</summary>
        Helper = 7,

        /// <summary>グループのみ (MeshType.Group)</summary>
        Group = 8,

        /// <summary>作業軸のみ (MeshType.WorkAxis)</summary>
        WorkAxis = 9,

        /// <summary>全てのオブジェクト</summary>
        All = 10
    }
}
