// VertexBillboardPlaceOps.cs
// 頂点へ藤壺：頂点それぞれの位置へ配置元オブジェクトを、カメラに向けて複製する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【位置】呼び出し側が GPU から読んだワールド座標をそのまま使う。行列は掛けない。
//
// 【向き（ビルボード）】カメラの視線 forward と上 up（ワールド）から作る。
//   up は forward に直交化する。toward = -forward（カメラ側）。
//   TowardCamera : Z = toward, Y = up       … 配置元の +Z（とげ）がカメラ側を向く
//   ScreenUp     : Z = up,     Y = forward  … 配置元の +Z（とげ）が画面の上を向く
//   X = Cross(Y, Z)。PlaceObjectMeshGenerator の Y = Cross(Z, X) と同じ系になる。
//   全頂点で同じフレームを使う（平行投影のビルボード）。
//
// 【倍率】一律（配置元のローカル座標 × Scale）。
//
// 【割り当て】PlaceSourceMode の規則に合わせる。
//   Combine  : 全頂点に結合メッシュ
//   Sequence : 頂点の並び順に巡回
//   Random   : シード固定の乱数で抽選
//
// 【パーツID】置いた 1 個につき 1 つ（PartsIdCounter）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PlaceObject;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.PlaceObject
{
    /// <summary>頂点へ藤壺で、配置元の +Z をどちらへ向けるか。</summary>
    public enum BillboardZDirection
    {
        /// <summary>+Z をカメラ側（手前）へ向ける。</summary>
        TowardCamera = 0,

        /// <summary>+Z を画面の上方向へ向ける。</summary>
        ScreenUp = 1,
    }
}

namespace Poly_Ling.Ops
{
    public static class VertexBillboardPlaceOps
    {
        private const float Eps = 1e-8f;

        /// <summary>
        /// カメラの視線と上方向からビルボードのフレームを作る。
        /// 視線が 0、または上方向が視線と平行で直交化できないときは false。
        /// </summary>
        public static bool TryBuildFrame(
            Vector3 viewDirection, Vector3 viewUp, BillboardZDirection zDirection,
            out Vector3 x, out Vector3 y, out Vector3 z)
        {
            x = Vector3.right;
            y = Vector3.up;
            z = Vector3.forward;

            if (viewDirection.sqrMagnitude <= Eps) return false;
            Vector3 forward = viewDirection.normalized;

            Vector3 up = viewUp - forward * Vector3.Dot(viewUp, forward);
            if (up.sqrMagnitude <= Eps) return false;
            up.Normalize();

            if (zDirection == BillboardZDirection.ScreenUp)
            {
                z = up;
                y = forward;
            }
            else
            {
                z = -forward;
                y = up;
            }

            x = Vector3.Cross(y, z);
            if (x.sqrMagnitude <= Eps) return false;
            x.Normalize();
            return true;
        }

        /// <summary>
        /// positions の各位置へ配置元を複製し、1 つの MeshObject（ワールド座標）にして返す。
        /// sources が空、または位置が無いときは頂点 0 のメッシュを返す。
        /// </summary>
        public static MeshObject Build(
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<MeshObject> sources,
            PlaceSourceMode mode,
            int randomSeed,
            float scale,
            Vector3 x, Vector3 y, Vector3 z,
            string meshName)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "VertexBillboardPlace" : meshName);
            if (positions == null || positions.Count == 0) return mo;
            if (sources == null || sources.Count == 0) return mo;

            float s = scale <= 0f ? 1f : scale;

            MeshObject combined = mode == PlaceSourceMode.Combine
                ? Poly_Ling.Ops.MeshObjectAppendOps.Combine(new List<MeshObject>(sources), mo.Name)
                : null;

            var rnd = mode == PlaceSourceMode.Random ? new System.Random(randomSeed) : null;
            int seq = 0;

            var partsIds = new PartsIdCounter();

            for (int i = 0; i < positions.Count; i++)
            {
                MeshObject src;
                switch (mode)
                {
                    case PlaceSourceMode.Sequence:
                        src = sources[seq];
                        seq = (seq + 1) % sources.Count;
                        break;
                    case PlaceSourceMode.Random:
                        src = sources[rnd.Next(sources.Count)];
                        break;
                    default:
                        src = combined;
                        break;
                }

                if (src == null || src.VertexCount == 0) continue;

                PlaceObjectMeshGenerator.AppendInstance(
                    mo, src, positions[i], x, y, z, s, true, partsIds.Take());
            }

            mo.RecalculateNormals();
            return mo;
        }
    }
}
