// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyMorphBuilder.cs
// ============================================================
// モーフ MeshContext → UnityEngine.Mesh のブレンドシェイプ
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// ============================================================
// 差分を「もう一度 ToUnityMesh して引く」理由
// ============================================================
//
//   ブレンドシェイプの deltaVertices は UnityMesh の頂点並びに一致していなければ
//   ならない。ところが HierarchyBuilder が使う MeshObject.ToUnityMesh(Matrix4x4) の
//   展開は面駆動で、名寄せキーが (頂点index, UVサブindex, 法線サブindex)、
//   さらに face.IsHidden を飛ばす（MeshBridgeDefault.cs:195-260）。
//   これは MeshExpansion の展開規則（頂点順・孤立除外・IsHidden 無視）とは別物なので、
//   MeshObject.BuildExpansionMap() を引いて写すと並びがずれる。
//
//   展開順序を自前で書き写すのは禁止されている（MeshExpansion.cs 冒頭の規則）。
//   そこで「同じ MeshObject の位置だけをずらしたクローン」を作り、
//   同じ ToUnityMesh を通してから引き算する。
//   展開は面・UVスロット・法線スロットだけで決まり、頂点位置に依存しないため、
//   クローンは基準メッシュと必ず同じ頂点数・同じ並びになる。
//   一致しなかった場合は載せずに警告する（黙って壊れた形状を出さない）。
//
//   引き算の基準も unityMesh.vertices ではなく shapeSource から作り直す。
//   ミラー側コンテキストの unityMesh は BuildMirrorSideMesh が全頂点へ
//   一律の平行移動を掛けた後の頂点を持つため、そのまま基準にすると
//   その平行移動ぶんが差分に混ざる。基準も同じ ToUnityMesh から取れば相殺される。
//
// ============================================================
// ミラー枝
// ============================================================
//
//   差分の元にする MeshObject は「その GameObject の形状を作った MeshObject」と
//   同一でなければならない。ミラー枝では実体側と面の巻き順が違うため、
//   実体側の MeshObject でクローンを作ると並びがずれる。
//   よって呼び出し側が shapeSource を渡す。
//     ・実体側                    … mc.MeshObject、差分はそのまま
//     ・許容モードの生成ミラー    … BuildMirroredMeshObject の結果、
//                                   差分は軸成分を反転（差分なので距離は使わない）
//     ・ミラー側コンテキスト      … mc.MeshObject（形状は既に反転済み）、差分はそのまま
//       （BuildMirrorSideMesh の平行移動は全頂点一律なので差分に影響しない）
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>ブレンドシェイプ1本ぶんの登録結果。</summary>
    public struct MorphShapeSlot
    {
        /// <summary>モーフ MeshContext のマスター索引。</summary>
        public int MorphContextIndex;

        /// <summary>ブレンドシェイプを載せたレンダラの Transform。</summary>
        public Transform Renderer;

        /// <summary>その Mesh 内でのブレンドシェイプ index。</summary>
        public int ShapeIndex;

        /// <summary>登録に使ったブレンドシェイプ名。</summary>
        public string ShapeName;
    }

    /// <summary>モーフ MeshContext から UnityMesh のブレンドシェイプを作る。</summary>
    public static class HierarchyMorphBuilder
    {
        /// <summary>ブレンドシェイプのフレーム重み。Unity の慣習にそろえて 100 固定。</summary>
        private const float FrameWeight = 100f;

        /// <summary>
        /// baseIndex のメッシュに属するモーフを unityMesh へブレンドシェイプとして載せる。
        /// </summary>
        /// <param name="shapeSource">unityMesh を作った MeshObject（ミラー枝では鏡像化済みのもの）。</param>
        /// <param name="mirrorDelta">差分を軸反転するか（許容モードの生成ミラーのみ true）。</param>
        /// <param name="mirrorAxis">反転軸（1=X / 2=Y / 4=Z）。mirrorDelta が false なら未使用。</param>
        /// <returns>載せたブレンドシェイプの一覧。</returns>
        public static List<MorphShapeSlot> Apply(
            ModelContext model, int baseIndex,
            MeshObject shapeSource, Mesh unityMesh, Transform rendererTransform,
            bool mirrorDelta, int mirrorAxis,
            List<string> warnings)
        {
            var slots = new List<MorphShapeSlot>();
            if (model == null || shapeSource == null || unityMesh == null) return slots;

            var morphIndices = CollectMorphIndices(model, baseIndex);
            if (morphIndices.Count == 0) return slots;

            // 引き算の基準。unityMesh そのものではなく shapeSource から作り直す（冒頭参照）。
            var baseVerts = BuildBaseVertices(shapeSource);
            if (baseVerts == null || baseVerts.Length == 0) return slots;

            if (baseVerts.Length != unityMesh.vertexCount)
            {
                warnings?.Add(
                    $"メッシュ \"{shapeSource.Name}\" の展開頂点数が一致しないため"
                    + $"ブレンドシェイプを作れませんでした"
                    + $"（Unity {unityMesh.vertexCount} / 再展開 {baseVerts.Length}）。");
                return slots;
            }

            var usedNames = new HashSet<string>();
            for (int i = 0; i < unityMesh.blendShapeCount; i++)
                usedNames.Add(unityMesh.GetBlendShapeName(i));

            foreach (int morphIndex in morphIndices)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                if (morphCtx == null) continue;

                var offsets = morphCtx.GetMorphOffsets();
                if (offsets == null || offsets.Count == 0) continue;

                var delta = BuildDelta(
                    shapeSource, baseVerts, offsets, mirrorDelta, mirrorAxis,
                    morphCtx.Name, warnings);
                if (delta == null) continue;

                string shapeName = HierarchyBuilder.MakeUniqueName(
                    string.IsNullOrEmpty(morphCtx.Name) ? $"Morph_{morphIndex}" : morphCtx.Name,
                    usedNames);

                int shapeIndex = unityMesh.blendShapeCount;
                unityMesh.AddBlendShapeFrame(shapeName, FrameWeight, delta, null, null);

                slots.Add(new MorphShapeSlot
                {
                    MorphContextIndex = morphIndex,
                    Renderer          = rendererTransform,
                    ShapeIndex        = shapeIndex,
                    ShapeName         = shapeName,
                });
            }

            return slots;
        }

        /// <summary>
        /// 差分の基準になる展開頂点を作る。unityMesh を作ったのと同じ
        /// ToUnityMesh(Matrix4x4.identity) を通すことで並びを一致させる。
        /// </summary>
        private static Vector3[] BuildBaseVertices(MeshObject shapeSource)
        {
            Mesh baseMesh = null;
            try
            {
                baseMesh = shapeSource.ToUnityMesh(Matrix4x4.identity);
                return baseMesh != null ? baseMesh.vertices : null;
            }
            finally
            {
                DestroyMesh(baseMesh);
            }
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh);
            else                       Object.DestroyImmediate(mesh);
        }

        /// <summary>
        /// baseIndex を親に持つモーフ MeshContext を索引の昇順で集める。
        ///
        /// 【親の特定は MorphParentIndex を直接見てはいけない】
        ///   MorphParentIndex は「-1 = 未指定（名前規則ベースで検索）」という仕様
        ///   （MeshContext.cs:966-970）。PMX 読込では -1 のままのことがあり、
        ///   実際の親は "&lt;親メッシュ名&gt;_&lt;モーフ名&gt;" の命名規則から引く。
        ///   その解決は MorphPreviewState.FindBaseMeshIndex が正典で、
        ///   PMXPartialImportOps.cs:377-380 も同じものを使っている。
        ///   ここで MorphParentIndex を直接比較すると、-1 のモーフを全て取りこぼす。
        /// </summary>
        private static List<int> CollectMorphIndices(ModelContext model, int baseIndex)
        {
            var list = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.IsMorph) continue;
                if (MorphPreviewState.FindBaseMeshIndex(model, mc) != baseIndex) continue;
                list.Add(i);
            }
            return list;
        }

        /// <summary>
        /// 疎差分から deltaVertices を作る。
        /// 展開後の並びは「位置をずらしたクローンを同じ ToUnityMesh へ通す」ことで得る。
        /// </summary>
        private static Vector3[] BuildDelta(
            MeshObject shapeSource, Vector3[] baseVerts,
            List<(int VertexIndex, Vector3 Offset)> offsets,
            bool mirrorDelta, int mirrorAxis,
            string morphName, List<string> warnings)
        {
            var clone = shapeSource.Clone();
            if (clone?.Vertices == null) return null;

            bool any = false;
            foreach (var (vertexIndex, offset) in offsets)
            {
                if (vertexIndex < 0 || vertexIndex >= clone.Vertices.Count) continue;

                // 差分の鏡像化は軸成分の符号反転だけでよい（距離は差分に効かない）。
                // 規則は MirrorBranchOps.MirrorNormal と同一。
                Vector3 d = mirrorDelta
                    ? MirrorBranchOps.MirrorNormal(mirrorAxis, offset)
                    : offset;

                clone.Vertices[vertexIndex].Position += d;
                any = true;
            }
            if (!any) return null;

            clone.InvalidatePositionCache();

            Mesh morphed = null;
            try
            {
                morphed = clone.ToUnityMesh(Matrix4x4.identity);
                if (morphed == null) return null;

                var morphVerts = morphed.vertices;
                if (morphVerts == null || morphVerts.Length != baseVerts.Length)
                {
                    warnings?.Add(
                        $"モーフ \"{morphName}\" の展開頂点数が基準メッシュと一致しないため"
                        + $"ブレンドシェイプを作れませんでした"
                        + $"（基準 {baseVerts.Length} / モーフ {morphVerts?.Length ?? 0}）。");
                    return null;
                }

                var delta = new Vector3[baseVerts.Length];
                for (int i = 0; i < delta.Length; i++)
                    delta[i] = morphVerts[i] - baseVerts[i];

                return delta;
            }
            finally
            {
                DestroyMesh(morphed);
            }
        }
    }
}
