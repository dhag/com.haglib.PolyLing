// Assets/Editor/Poly_Ling_/Core/Data/MeshCategory.cs
// メッシュオブジェクトのカテゴリ定義
// MeshTypeをグループ化してアクセスするための列挙型

namespace Poly_Ling.Data
{
    /// <summary>
    /// メッシュオブジェクトのカテゴリ
    /// MeshTypeの単一タイプまたは複合グループを表す
    /// </summary>
    public enum MeshCategory
    {
        /// <summary>通常のメッシュのみ (MeshType.Mesh)</summary>
        Mesh,

        /// <summary>ベイクされたミラーメッシュのみ (MeshType.BakedMirror)</summary>
        BakedMirror,

        /// <summary>描画可能メッシュ (Mesh + BakedMirror)</summary>
        Drawable,

        /// <summary>ボーンのみ (MeshType.Bone)</summary>
        Bone,

        /// <summary>モーフのみ (MeshType.Morph)</summary>
        Morph,

        /// <summary>剛体のみ (MeshType.RigidBody)</summary>
        RigidBody,

        /// <summary>剛体ジョイントのみ (MeshType.RigidBodyJoint)</summary>
        RigidBodyJoint,

        /// <summary>ヘルパーのみ (MeshType.Helper)</summary>
        Helper,

        /// <summary>グループのみ (MeshType.Group)</summary>
        Group,

        /// <summary>全てのオブジェクト</summary>
        All
    }
}
