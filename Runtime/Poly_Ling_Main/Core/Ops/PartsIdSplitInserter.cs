// PartsIdSplitInserter.cs
// PartsIdSplitOps が切り出したメッシュを ModelContext へ入れ、
// 空のオブジェクトを親にして子として並べる。
// Undo 記録とビュー更新は呼び出し側（Player の Core）が行う。
//
// 【空のオブジェクト】
//   MeshContext.HierarchyParentIndex は MeshObject への委譲で、MeshObject が
//   null だと -1 を返す（MeshContext.cs:436-440）。親子づけを効かせるため、
//   親には頂点 0・面 0 の MeshObject を持たせる。
//
// 【階層のつけ方】
//   描画オブジェクトの階層はリスト順 + Depth + HierarchyParentIndex の 3 点で
//   決まる（ObjectArrayInserter.cs:5-13）。ここでは
//     ・元オブジェクトのサブツリー直後へ、親 → 子の順で挿入し
//     ・親の Depth = 元と同じ（＝元の兄弟）、子の Depth = 親 + 1
//     ・親の HierarchyParentIndex = 元の親、子の HierarchyParentIndex = 親
//   の 3 つを満たす。
//
// 【階層を最後に書く理由】
//   ModelContext.Insert は挿入のたびに既存の索引参照を繰り下げる
//   （ObjectArrayInserter.cs:15-25 の注記）。挿入の途中で書くと繰り下げが
//   二重に掛かる。全部挿し終わってから書く。
//   元オブジェクトの HierarchyParentIndex も Insert が繰り下げるので、
//   親へ写す値は挿入後に読む。
//
// 【姿勢】
//   親に元オブジェクトの BoneTransform を写し、子は単位にする。
//   頂点は元のローカル座標のまま複製されているので、これで分解前と同じ
//   ワールド位置に出る。
//
// 【パーツIDを残す】
//   ObjectArrayInserter.BuildContextFor は生成物のパーツIDを 0 へ潰すが
//   （ObjectArrayInserter.cs:244-246）、ここでは潰さない。
//   どのパーツから切り出したかを頂点側に残しておく。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools.ObjectArray;

namespace Poly_Ling.Ops
{
    /// <summary>分解結果をモデルへ入れる。</summary>
    public static class PartsIdSplitInserter
    {
        /// <summary>
        /// 空のオブジェクトを 1 つ作り、切り出したメッシュをその子として挿入する。
        /// </summary>
        /// <returns>
        /// 挿入した (索引, MeshContext) の一覧。先頭が空の親、以降がパーツIDの昇順。
        /// 何も入れられなかったときは空。
        /// </returns>
        public static List<(int Index, MeshContext MeshContext)> Insert(
            ModelContext model, int sourceMasterIndex, IReadOnlyList<PartsIdSplitPiece> pieces)
        {
            var added = new List<(int Index, MeshContext MeshContext)>();
            if (model == null || pieces == null || pieces.Count == 0) return added;

            var src = model.GetMeshContext(sourceMasterIndex);
            if (src == null) return added;

            string baseName = string.IsNullOrEmpty(src.Name) ? "Mesh" : src.Name;
            int    srcDepth = src.Depth;
            int    insertAt = ObjectArrayInserter.ResolveInsertIndex(model, sourceMasterIndex);

            // ── 親（空のオブジェクト） ──────────────────────────────────
            var emptyCtx = BuildContext(
                model, new MeshObject(), model.GenerateUniqueMeshName(baseName + "_Parts"));
            emptyCtx.BoneTransform?.CopyFrom(src.BoneTransform);

            model.Insert(insertAt, emptyCtx);
            added.Add((insertAt, emptyCtx));

            // ── 子 ──────────────────────────────────────────────────────
            for (int i = 0; i < pieces.Count; i++)
            {
                var mo = pieces[i].Mesh;
                if (mo == null) continue;

                int    index = insertAt + added.Count;
                string name  = model.GenerateUniqueMeshName(ChildName(baseName, pieces[i].PartsId));

                var ctx = BuildContext(model, mo, name);
                model.Insert(index, ctx);
                added.Add((index, ctx));
            }

            // ── 階層（全部挿し終わってから書く） ────────────────────────
            emptyCtx.Depth                = srcDepth;
            emptyCtx.HierarchyParentIndex = src.HierarchyParentIndex;
            emptyCtx.ParentIndex          = src.HierarchyParentIndex;

            for (int i = 1; i < added.Count; i++)
            {
                var ctx = added[i].MeshContext;
                ctx.Depth                = srcDepth + 1;
                ctx.HierarchyParentIndex = insertAt;
                ctx.ParentIndex          = insertAt;
            }

            return added;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>子オブジェクトの名前。予約値だけ番号ではなく語で書く。</summary>
        private static string ChildName(string baseName, int partsId)
            => partsId == PartsIdOps.UnweightedPartsId
                ? baseName + "_NoWeight"
                : baseName + "_" + partsId;

        /// <summary>
        /// 描画オブジェクトを 1 つ組む。階層と姿勢はここでは決めず、呼び出し側が後で書く。
        /// </summary>
        private static MeshContext BuildContext(ModelContext model, MeshObject mo, string name)
        {
            mo.Name = name;

            // 置き場所はこのあと決め直すので、受け継いだ階層情報は捨てる。
            mo.ParentIndex          = -1;
            mo.HierarchyParentIndex = -1;
            mo.Depth                = 0;

            if (mo.BoneTransform != null)
            {
                mo.BoneTransform.UseLocalTransform = false;
                mo.BoneTransform.Position          = Vector3.zero;
                mo.BoneTransform.Rotation          = Vector3.zero;
                mo.BoneTransform.Scale             = Vector3.one;
            }

            // 頂点 0 のときは空の Mesh がそのまま返る（MeshBridgeDefault.cs:50-51）。
            var unityMesh = mo.ToUnityMesh();
            unityMesh.name      = name;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshContext.Name は MeshObject.Name への委譲（MeshContext.cs:45-53）。
            // MeshObject を先に入れないと Name の代入が捨てられるため順序を守る。
            return new MeshContext
            {
                MeshObject         = mo,
                Name               = name,
                UnityMesh          = unityMesh,
                IsVisible          = true,
                ParentModelContext = model,
            };
        }
    }
}
