// MeshSignature.cs
// メッシュの「変わったか」を見るための署名。評価器（ModifierEvaluator）が入力ごとに取る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【2 本に分ける】
//   Topology  … 頂点数・面構成・材質・UV。位置は含まない。
//   Positions … 頂点の位置（作業中オフセット WorkingPositions を足したもの）と頂点 ID。
//   Matrix    … 行列（モデル空間で読む入力の VertexToBindWorldMatrix。段 10）。
//   面構成が変わったか・位置だけ変わったかで、処理器の手間が違う（サブディビジョンは
//   前者で作り直し、後者はステンシルを掛けるだけ）。
//
// 【刻み】位置は 1e-5 単位へ丸めてから混ぜる（ObjectGroupOps.ComputeSourceDigest と同じ）。
//   丸めないと、同じ操作でも浮動小数の下位ビットが揺れて「変わった」と出る。
//
// 【毎フレーム呼ばないこと】全頂点・全面を走査する。

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class MeshSignature
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime  = 1099511628211UL;

        /// <summary>
        /// 面構成・UV・材質の署名。頂点位置は含まない。
        /// minFaceVertices 未満の頂点数の面は混ぜない（サブディビジョンは 3：線分を扱わない）。
        /// </summary>
        public static ulong Topology(MeshObject mo, int minFaceVertices = 1)
        {
            if (mo == null) return 0UL;

            ulong h = Offset;
            void Mix(int v) { unchecked { h ^= (uint)v; h *= Prime; } }

            Mix(mo.VertexCount);
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var f = mo.Faces[fi];
                if (f == null || f.VertexCount < minFaceVertices) continue;
                Mix(f.VertexCount);
                Mix(f.MaterialIndex);
                for (int i = 0; i < f.VertexCount; i++)
                {
                    int vi = f.VertexIndices[i];
                    Mix(vi);
                    int slot = (f.UVIndices != null && i < f.UVIndices.Count) ? f.UVIndices[i] : 0;
                    Mix(slot);
                    if (vi >= 0 && vi < mo.VertexCount)
                    {
                        var uvs = mo.Vertices[vi].UVs;
                        if (uvs != null && slot >= 0 && slot < uvs.Count)
                        {
                            Mix(BitConverter.SingleToInt32Bits(uvs[slot].x));
                            Mix(BitConverter.SingleToInt32Bits(uvs[slot].y));
                        }
                    }
                }
            }
            return h;
        }

        /// <summary>
        /// 行列の署名（段 10）。モデル空間で読む入力の VertexToBindWorldMatrix に使う。
        /// 位置と同じく 1e-5 単位へ丸めてから混ぜる。
        /// </summary>
        public static ulong Matrix(Matrix4x4 m)
        {
            ulong h = Offset;
            for (int i = 0; i < 16; i++)
            {
                long v = Mathf.RoundToInt(m[i] * 100000f);
                unchecked
                {
                    for (int b = 0; b < 8; b++)
                    {
                        h ^= (byte)(v >> (b * 8));
                        h *= Prime;
                    }
                }
            }
            return h;
        }

        /// <summary>頂点位置（作業中オフセット込み）と頂点 ID の署名。</summary>
        public static ulong Positions(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0UL;

            ulong h = Offset;
            void Mix(long v)
            {
                unchecked
                {
                    for (int b = 0; b < 8; b++)
                    {
                        h ^= (byte)(v >> (b * 8));
                        h *= Prime;
                    }
                }
            }

            var wp = mc.WorkingPositions;
            Mix(mo.VertexCount);
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) { Mix(-2); continue; }
                Mix(v.Id);
                Vector3 p = v.Position;
                if (wp != null && i < wp.Length) p += wp[i];
                Mix(Mathf.RoundToInt(p.x * 100000f));
                Mix(Mathf.RoundToInt(p.y * 100000f));
                Mix(Mathf.RoundToInt(p.z * 100000f));
            }
            return h;
        }
    }
}
