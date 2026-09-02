// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyBuildOptions.cs
// ============================================================
// ModelContext → Unity ヒエラルキー生成のオプション（純POCO）
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// 【役割】
//   HierarchyBuilder.Build に渡す設定値。UnityEditor に依存する項目は
//   ここに入れない（プレファブ化・Avatar 生成・出力先フォルダ・EditorPrefs は
//   すべて Editor 側の関心事であり、生成そのものには要らない）。
//
// 【依存】
//   UnityEngine すら使わない純データ。#if UNITY_EDITOR も含まない。
//
// ============================================================

namespace Poly_Ling.HierarchyIO
{
    /// <summary>
    /// レンダラ種別の決め方。
    ///
    /// Auto            … 従来どおり（ウェイトがあればスキンド）
    /// ForceMeshFilter … ウェイトがあっても MeshFilter+MeshRenderer で出す
    ///
    /// 「スキンド強制」は用意しない。ウェイトが無いメッシュはスキンドにできないため、
    /// その場合は先に MeshFilter → Skinned 変換が必要。
    /// </summary>
    public enum HierarchyRendererMode
    {
        Auto = 0,
        ForceMeshFilter = 1,
    }

    /// <summary>ヒエラルキー生成の設定。</summary>
    public class HierarchyBuildOptions
    {
        /// <summary>ボーン階層（Armature）を生成する。</summary>
        public bool CreateArmature = true;

        /// <summary>MeshContext.BindPose を bindposes に使う。false なら worldToLocalMatrix。</summary>
        public bool UseBindpose = true;

        /// <summary>可視メッシュのみ書き出す。</summary>
        public bool ExportVisibleOnly = true;

        /// <summary>可視ノードの親が不可視なら補完して出力する（Transform のみ）。</summary>
        public bool IncludeInvisibleAncestors = true;

        /// <summary>ボーンを除外しメッシュのみ出力する。</summary>
        public bool ExportMeshOnly = false;

        /// <summary>剛体／JOINT を Unity 物理部品として出力する。</summary>
        public bool ExportPhysics = true;

        /// <summary>ミラー設定漏れを許容する（分岐配下は実体側から鏡像を生成）。</summary>
        public bool TolerantMirrorBranch = true;

        /// <summary>
        /// モーフ MeshContext を UnityMesh のブレンドシェイプとして載せるか。
        /// VRM の表情（VRMC_vrm.expressions）はこれを前提にする。
        /// </summary>
        public bool ExportMorphTargets = false;

        /// <summary>
        /// 面を1つも持たないサブメッシュを取り除くか。
        ///
        /// MeshObject.SubMeshCount は「使用マテリアル index の最大値+1」なので、
        /// モデル全体で 73 マテリアルあるうち index 40 だけを使うメッシュは
        /// サブメッシュ 41 個のうち 40 個が空になる。Unity 上は無害だが、
        /// glTF 化すると空プリミティブになり UniVRM が落ちる
        /// （空配列 → ExportingGltfData.cs:64 が accessor index -1 を返し、
        ///   MeshExportUtil.ToGltfPrimitive の Gltf.accessors[-1] で例外）。
        ///
        /// 既定 false。ヒエラルキー出力の結果を変えないため、
        /// 必要な経路（VRM 出力）だけが true にする。
        /// </summary>
        public bool DropEmptySubMeshes = false;

        /// <summary>レンダラ種別の決め方。</summary>
        public HierarchyRendererMode RendererMode = HierarchyRendererMode.Auto;

        public HierarchyBuildOptions Clone()
        {
            return new HierarchyBuildOptions
            {
                CreateArmature            = this.CreateArmature,
                UseBindpose               = this.UseBindpose,
                ExportVisibleOnly         = this.ExportVisibleOnly,
                IncludeInvisibleAncestors = this.IncludeInvisibleAncestors,
                ExportMeshOnly            = this.ExportMeshOnly,
                ExportPhysics             = this.ExportPhysics,
                TolerantMirrorBranch      = this.TolerantMirrorBranch,
                ExportMorphTargets        = this.ExportMorphTargets,
                DropEmptySubMeshes        = this.DropEmptySubMeshes,
                RendererMode              = this.RendererMode,
            };
        }

        public static HierarchyBuildOptions CreateDefault() => new HierarchyBuildOptions();
    }
}
