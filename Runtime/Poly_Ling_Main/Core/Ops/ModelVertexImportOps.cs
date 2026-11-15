// ModelVertexImportOps.cs
// 2 つのモデルの間で、オブジェクト 1 組ずつ頂点位置を写す（2 モデル表示のパネル・importModelVertices）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【書き込み先の形】
//   写し先のモデルの「今の形」「基本の形」「名前を指定した形」のどれか（ModelVertexTarget）。
//   今の形を切り替えずに書く：写し先のオブジェクト 1 個だけを、その形の状態へ移して（ShapeOps.SwitchMesh）
//   頂点位置を書き、今の形の状態へ戻す。切り替えは成分の完全一致で控えを作り直すので、
//   書かなかった頂点の控えは往復で変わらない。
//   名前を指定した形は、書いたオブジェクトの頂点だけが変わる（ほかのオブジェクトのその形はそのまま）。
//
// 【基本の形へ書くとき】（ShapeMqoOps と同じ規則）
//   keepShapeOffsets = true … 位置が変わった頂点の形の控え全部へ「新しい位置 − 古い位置」を足す（差分を保つ）。
//   keepShapeOffsets = false … 控えは絶対位置のまま。新しい基本の位置と同じになった控えだけ消す。
//
// 【頂点の対応】
//   既定は頂点番号（VertexMatchMode.Index）で、頂点数が同じ組だけを適合とする。
//   頂点 ID（VertexMatchMode.VertexId）は補助で、対応が 1 件でも取れれば適合とする（未対応の頂点は書かない）。
//   対応表は VertexDataTransferOps.BuildVertexMap（頂点データ転送と同じ）。
//
// 【末尾へ足す（頂点数が合わないとき）】
//   写し元のオブジェクトを別オブジェクトとして複製し、写し先のモデルの一覧の末尾に足す（CloneForAppend）。
//   ボーンウェイトはボーン名で付け直す（頂点データ転送と同じ）。引けない頂点はウェイトを外す。
//   材質は写し先に同じ名前があればそれ、無ければ写し先へ足す。形の控えは持ち込まない
//   （コピーと貼り付けと同じ。計画書 3.14）。親は無し。
//
// 位置は各頂点の Position（今の形での、オブジェクトのローカルの位置）をそのまま写す。
// 法線は再計算しない（形の決まり。計画書 3.13）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>頂点位置の書き込み先の形。</summary>
    public enum ModelVertexTarget
    {
        /// <summary>写し先のモデルの今の形。</summary>
        CurrentShape = 0,
        /// <summary>基本の形。</summary>
        BaseShape = 1,
        /// <summary>名前を指定した形（無ければ作る）。</summary>
        NamedShape = 2,
    }

    public static class ModelVertexImportOps
    {
        /// <summary>
        /// 写し先頂点 → 写し元頂点 の対応表を作り、組が適合するかを返す（冒頭の注記）。
        /// </summary>
        public static bool BuildMap(MeshContext src, MeshContext dst, VertexMatchMode mode,
                                    out Dictionary<int, int> map, out int unmatched)
        {
            map = VertexDataTransferOps.BuildVertexMap(src, dst, mode, out unmatched);
            int sn = src?.MeshObject?.VertexCount ?? 0;
            int dn = dst?.MeshObject?.VertexCount ?? 0;
            if (sn == 0 || dn == 0) return false;
            if (mode == VertexMatchMode.Index) return sn == dn;
            return map.Count > 0;
        }

        /// <summary>
        /// 写し先のオブジェクト 1 個の、形 shapeId の頂点位置を写し元の位置にする。位置が変わった頂点の数を返す。
        /// current は写し先のモデルの今の形の番号。shapeId は位置の形（UV の形は呼び出し側で断る）。
        /// </summary>
        public static int WriteShape(MeshObject dst, MeshObject src, Dictionary<int, int> map,
                                     int current, int shapeId, bool keepShapeOffsets,
                                     out int shifted, out int dropped)
        {
            shifted = 0;
            dropped = 0;
            if (dst == null || src == null || map == null) return 0;

            ShapeOps.SwitchMesh(dst, current, shapeId);

            int written = 0;
            foreach (var kv in map)
            {
                int di = kv.Key, si = kv.Value;
                if (di < 0 || di >= dst.VertexCount || si < 0 || si >= src.VertexCount) continue;
                var v = dst.Vertices[di];
                Vector3 old = v.Position;
                Vector3 p = src.Vertices[si].Position;
                if (ShapeOps.Same(old, p)) continue;
                v.Position = p;
                written++;

                if (shapeId != ShapeOps.BaseShapeId || v.Shapes == null) continue;
                if (keepShapeOffsets)
                {
                    Vector3 delta = p - old;
                    for (int k = 0; k < v.Shapes.Count; k++)
                    {
                        var e = v.Shapes[k];
                        v.Shapes[k] = new ShapePosition(e.ShapeId, e.Position + delta);
                        shifted++;
                    }
                }
                else
                {
                    for (int k = v.Shapes.Count - 1; k >= 0; k--)
                    {
                        if (!ShapeOps.Same(v.Shapes[k].Position, p)) continue;
                        v.RemoveShape(v.Shapes[k].ShapeId);
                        dropped++;
                    }
                }
            }

            ShapeOps.SwitchMesh(dst, shapeId, current);
            dst.InvalidatePositionCache();
            return written;
        }

        /// <summary>
        /// 写し元のオブジェクトを、写し先のモデルへ足す別オブジェクトとして複製する（冒頭の注記）。
        /// 材質を写し先へ足したときは addedMaterials に入れる（呼び出し側が写し先の一覧へ足し済み）。
        /// weightDropped はボーン名で引けずにウェイトを外した頂点の数。
        /// </summary>
        public static MeshContext CloneForAppend(ModelContext srcModel, MeshContext src, ModelContext dstModel,
                                                 out int weightDropped, out int materialsAdded)
        {
            weightDropped = 0;
            materialsAdded = 0;
            if (src?.MeshObject == null || dstModel == null) return null;

            var mc = MeshContextCloneOps.Clone(src, MeshContextCloneKind.NewObject);
            mc.HierarchyParentIndex = -1;
            var mo = mc.MeshObject;
            ShapeOps.ClearShapes(mo);

            // 材質：名前で引き直す。無ければ写し先へ足す。
            var matMap = new Dictionary<int, int>();
            foreach (var face in mo.Faces)
            {
                int mi = face.MaterialIndex;
                if (!matMap.TryGetValue(mi, out int nm))
                {
                    nm = ResolveMaterial(srcModel, mi, dstModel, ref materialsAdded);
                    matMap[mi] = nm;
                }
                face.MaterialIndex = nm;
            }

            // ボーンウェイト：名前で引き直す。引けない頂点は外す。
            var boneRemap = VertexDataTransferOps.BuildBoneRemap(srcModel, dstModel, out _);
            foreach (var v in mo.Vertices)
            {
                if (v.BoneWeight.HasValue)
                {
                    if (VertexDataTransferOps.TryRemapWeight(v.BoneWeight, boneRemap, out var bw)) v.BoneWeight = bw;
                    else { v.BoneWeight = null; weightDropped++; }
                }
                if (v.MirrorBoneWeight.HasValue)
                {
                    v.MirrorBoneWeight = VertexDataTransferOps.TryRemapWeight(v.MirrorBoneWeight, boneRemap, out var mbw)
                        ? mbw : null;
                }
            }
            mo.RecomputeSkinKind();
            mo.InvalidatePositionCache();
            return mc;
        }

        private static int ResolveMaterial(ModelContext srcModel, int srcIndex, ModelContext dstModel, ref int added)
        {
            var srcRefs = srcModel?.MaterialReferences;
            var dstRefs = dstModel.MaterialReferences;
            if (srcRefs == null || srcIndex < 0 || srcIndex >= srcRefs.Count || srcRefs[srcIndex] == null)
                return 0;

            string name = srcRefs[srcIndex].Name;
            for (int i = 0; i < dstRefs.Count; i++)
                if (dstRefs[i] != null && dstRefs[i].Name == name) return i;

            dstRefs.Add(srcRefs[srcIndex].Clone());
            added++;
            return dstRefs.Count - 1;
        }

        /// <summary>
        /// masterIndex のオブジェクトで、形 shapeId が基本の形と位置の違う頂点の数。
        /// 基本の形・範囲外は 0。
        /// </summary>
        public static int CountMovedVertices(ModelContext model, int masterIndex, int shapeId)
        {
            if (model == null || shapeId == ShapeOps.BaseShapeId) return 0;
            var mo = model.GetMeshContext(masterIndex)?.MeshObject;
            if (mo == null) return 0;
            int current = model.CurrentShapeId;
            int n = 0;
            foreach (var v in mo.Vertices)
                if (!ShapeOps.Same(ShapeOps.GetShapePosition(v, current, shapeId), ShapeOps.GetBasePosition(v, current))) n++;
            return n;
        }
    }
}
