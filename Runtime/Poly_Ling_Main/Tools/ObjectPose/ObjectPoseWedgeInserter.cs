// ObjectPoseWedgeInserter.cs
// ObjectPoseWedgeGenerator が作った計画を ModelContext へ入れる。
// Undo 記録とビュー更新は呼び出し側が行う。
// Runtime/Poly_Ling_Main/Tools/ObjectPose/ に配置
//
// 【リスト末尾へ追加する理由】
//   ObjectArrayInserter は出力先の直後へ挿し込むため、挿入位置以降を指していた
//   HierarchyParentIndex を ModelContext.Insert の付け替え
//   （ModelContext.RemapIndexReferences）に頼ってずらすことになる。
//   ここは常に末尾へ足すので既存の索引が動かず、
//   元モデルの階層に一切触れずに済む。
//
// 【トランスフォーム】
//   コンテナもくさびも BoneTransform は単位（UseLocalTransform = false）。
//   くさびの頂点はワールド座標で入っているため、これで見た目の位置が保たれる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.ObjectPose
{
    public static class ObjectPoseWedgeInserter
    {
        /// <summary>
        /// コンテナ（空オブジェクト）とその配下のくさびをモデル末尾へ追加する。
        /// 戻り値の先頭がコンテナ。
        /// </summary>
        public static List<(int Index, MeshContext MeshContext)> Insert(
            ModelContext model,
            IReadOnlyList<ObjectPoseWedgePiece> pieces,
            string containerBaseName)
        {
            var added = new List<(int, MeshContext)>();
            if (model == null || pieces == null || pieces.Count == 0) return added;

            if (string.IsNullOrEmpty(containerBaseName))
                containerBaseName = ObjectPoseWedgeGenerator.DefaultContainerName;

            // コンテナ名だけは一意化する（取り込み時に見つけられるようにするため）。
            // くさび自身は「元メッシュ名 + _bone」で、元メッシュとは衝突しない。
            string containerName = model.GenerateUniqueMeshName(containerBaseName);

            var container = BuildContext(model, new MeshObject(containerName), containerName);
            container.Depth                = 0;
            container.HierarchyParentIndex = -1;
            container.ParentIndex          = -1;

            int containerIndex = model.Add(container);
            added.Add((containerIndex, container));

            var indexOfPiece = new int[pieces.Count];
            for (int i = 0; i < pieces.Count; i++) indexOfPiece[i] = -1;

            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;

                var mo = piece.Mesh ?? new MeshObject(piece.Name);
                var ctx = BuildContext(model, mo, piece.Name);

                int parentIndex = (piece.ParentPieceIndex >= 0 && piece.ParentPieceIndex < i)
                    ? indexOfPiece[piece.ParentPieceIndex]
                    : containerIndex;
                if (parentIndex < 0) parentIndex = containerIndex;

                ctx.Depth = piece.Depth + 1;

                int index = model.Add(ctx);

                // Add のあとに書く。Add 自体は階層参照を触らない。
                ctx.HierarchyParentIndex = parentIndex;
                ctx.ParentIndex          = parentIndex;

                indexOfPiece[i] = index;
                added.Add((index, ctx));
            }

            model.ComputeWorldMatrices();
            return added;
        }

        // ================================================================
        // 共通
        // ================================================================

        private static MeshContext BuildContext(ModelContext model, MeshObject mo, string name)
        {
            mo.Name                = name;
            mo.ParentIndex         = -1;
            mo.HierarchyParentIndex = -1;
            mo.Depth               = 0;

            if (mo.BoneTransform == null) mo.BoneTransform = new BoneTransform();
            mo.BoneTransform.UseLocalTransform = false;
            mo.BoneTransform.Position          = Vector3.zero;
            mo.BoneTransform.Rotation          = Vector3.zero;
            mo.BoneTransform.Scale             = Vector3.one;

            Mesh unityMesh;
            if (mo.VertexCount > 0)
            {
                unityMesh = mo.ToUnityMesh();
            }
            else
            {
                unityMesh = new Mesh();
            }
            unityMesh.name      = name;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshContext.Name は MeshObject.Name への委譲なので MeshObject を先に入れる。
            return new MeshContext
            {
                MeshObject         = mo,
                Name               = name,
                UnityMesh          = unityMesh,
                IsVisible          = true,
                OriginalPositions  = new Vector3[0],
                ParentModelContext = model,
            };
        }
    }
}
