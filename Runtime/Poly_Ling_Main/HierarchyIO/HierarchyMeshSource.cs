// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyMeshSource.cs
// ============================================================
// 描画ノード 1 つぶんの「形状の出どころ」
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// 【何のためか】
//   Unity メッシュは三角形しか持たず、（頂点・UV・法線）の組ごとに頂点が分かれる。
//   多角形・制御点・疎なモーフ差分を持つ形式（FBX）は、Unity メッシュではなく
//   ここに記録した MeshObject から直接書く。
//   どの MeshObject を使うか・ミラー側をどれだけずらすか・材質・ボーンの並び・
//   モーフの名前は HierarchyBuilder が決める（書き出し側へ規則を書き写さない）。
//
// 【中身の約束】
//   ・Shape … その GameObject の形状を作る MeshObject。座標は GameObject のローカル。
//             実体側は mc.MeshObject、許容モードの生成ミラーは MirrorBranchOps.BuildMirroredMeshObject の結果
//             （反転と巻き順の反転済み）、ミラー側コンテキストは mc.MeshObject（反転済み）。
//   ・VertexOffset … 全頂点に足す平行移動（ミラー側コンテキストのピボット差。それ以外は 0）。
//   ・Materials … 面の材質番号（0〜Materials.Length−1 に丸める）→ 材質。空サブメッシュを詰める前。
//   ・Bones / Bindposes / BoneSlotOf … スキンドのみ。BoneSlotOf は MeshContext 索引 → Bones の位置。
//     Vertex.BoneWeight の索引は HierarchyBuilder.RemapBoneWeight で写す。
//   ・Morphs … 載せるモーフ（名前・並びは Unity メッシュのブレンドシェイプと同じ規則）。
//     差分は MeshContext.GetMorphOffsets の疎な差分。MirrorDelta のときは
//     MirrorBranchOps.MirrorNormal(MirrorAxis, d) で反転する。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>描画ノード 1 つぶんの形状の出どころ（HierarchyBuildResult.MeshSourceOf）。</summary>
    public sealed class HierarchyMeshSource
    {
        /// <summary>形状を作る MeshObject（GameObject のローカル座標）。</summary>
        public MeshObject Shape;

        /// <summary>全頂点に足す平行移動。</summary>
        public Vector3 VertexOffset;

        /// <summary>SkinnedMeshRenderer として出すノードか。</summary>
        public bool IsSkinned;

        /// <summary>材質番号 → 材質（空サブメッシュを詰める前）。</summary>
        public Material[] Materials;

        /// <summary>スキンドのボーン（SkinnedMeshRenderer.bones と同じ並び）。</summary>
        public Transform[] Bones;

        /// <summary>Bones と同じ並びのバインドポーズ。</summary>
        public Matrix4x4[] Bindposes;

        /// <summary>MeshContext 索引 → Bones の位置。</summary>
        public Dictionary<int, int> BoneSlotOf;

        /// <summary>モーフの差分を軸反転するか（許容モードの生成ミラーのみ true）。</summary>
        public bool MirrorDelta;

        /// <summary>反転軸（1=X / 2=Y / 4=Z）。MirrorDelta が false なら未使用。</summary>
        public int MirrorAxis = 1;

        /// <summary>載せるモーフ（ShapeIndex は並びの番号）。</summary>
        public readonly List<MorphShapeSlot> Morphs = new List<MorphShapeSlot>();
    }
}
