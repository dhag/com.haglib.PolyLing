// Face.cs
// 面クラス（MeshObject.cs から分離）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（MeshObject.cs と同じ名前空間。MeshObject.cs から分割）

using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{
    // ============================================================
    // Face クラス
    // ============================================================

    /// <summary>
    /// 面データ（N角形対応）
    /// 頂点インデックスと、各頂点のUV/法線サブインデックス、マテリアルインデックスを保持
    /// </summary>
    [Serializable]
    public class Face
    {
        /// <summary>
        /// 面ID（トポロジー追跡・外部連携・モーフ用）
        /// MeshObjectが管理する一意の識別子
        /// </summary>
        public int Id = 0;

        /// <summary>頂点インデックスリスト（Vertex配列への参照）</summary>
        public List<int> VertexIndices = new List<int>();

        /// <summary>各頂点のUVサブインデックス（Vertex.UVs[n]への参照）</summary>
        public List<int> UVIndices = new List<int>();

        /// <summary>各頂点の法線サブインデックス（Vertex.Normals[n]への参照）</summary>
        public List<int> NormalIndices = new List<int>();

        /// <summary>マテリアルインデックス（MeshUndoContext.Materialsへの参照）</summary>
        public int MaterialIndex = 0;

        /// <summary>面フラグ</summary>
        public FaceFlags Flags = FaceFlags.None;

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => VertexIndices.Count;

        /// <summary>三角形数（扇形分割時）</summary>
        public int TriangleCount => VertexCount >= 3 ? VertexCount - 2 : 0;

        /// <summary>三角形か</summary>
        public bool IsTriangle => VertexCount == 3;

        /// <summary>四角形か</summary>
        public bool IsQuad => VertexCount == 4;

        /// <summary>有効な面か（3頂点以上）</summary>
        public bool IsValid => VertexCount >= 3;

        // === フラグ操作 ===

        /// <summary>フラグが設定されているか</summary>
        public bool HasFlag(FaceFlags flag) => (Flags & flag) != 0;

        /// <summary>フラグを設定</summary>
        public void SetFlag(FaceFlags flag) => Flags |= flag;

        /// <summary>フラグをクリア</summary>
        public void ClearFlag(FaceFlags flag) => Flags &= ~flag;

        /// <summary>フラグをトグル</summary>
        public void ToggleFlag(FaceFlags flag) => Flags ^= flag;

        /// <summary>ミラー生成された面か</summary>
        public bool IsMirrorGenerated => HasFlag(FaceFlags.MirrorGenerated);

        /// <summary>補助線/面か</summary>
        public bool IsAuxiliary => HasFlag(FaceFlags.Auxiliary);

        /// <summary>非表示か</summary>
        public bool IsHidden => HasFlag(FaceFlags.Hidden);

        // === コンストラクタ ===

        public Face() { }

        /// <summary>
        /// 三角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2 });
            UVIndices.AddRange(new[] { 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 四角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int v3, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2, v3 });
            UVIndices.AddRange(new[] { 0, 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 完全指定で三角形を作成
        /// </summary>
        public static Face CreateTriangle(
            int v0, int v1, int v2,
            int uv0, int uv1, int uv2,
            int n0, int n1, int n2,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2 },
                UVIndices = new List<int> { uv0, uv1, uv2 },
                NormalIndices = new List<int> { n0, n1, n2 },
                MaterialIndex = materialIndex
            };
        }

        /// <summary>
        /// 完全指定で四角形を作成
        /// </summary>
        public static Face CreateQuad(
            int v0, int v1, int v2, int v3,
            int uv0, int uv1, int uv2, int uv3,
            int n0, int n1, int n2, int n3,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2, v3 },
                UVIndices = new List<int> { uv0, uv1, uv2, uv3 },
                NormalIndices = new List<int> { n0, n1, n2, n3 },
                MaterialIndex = materialIndex
            };
        }

        // === 三角形分解 ===

        /// <summary>
        /// 三角形インデックスに分解（扇形分割）
        /// </summary>
        /// <returns>三角形数 × 3 のインデックス配列</returns>
        public int[] ToTriangleIndices()
        {
            if (VertexCount < 3)
                return Array.Empty<int>();

            if (IsTriangle)
                return VertexIndices.ToArray();

            // 扇形分割: v0 を中心に (v0, v1, v2), (v0, v2, v3), ... 
            var result = new List<int>();
            for (int i = 1; i < VertexCount - 1; i++)
            {
                result.Add(VertexIndices[0]);
                result.Add(VertexIndices[i]);
                result.Add(VertexIndices[i + 1]);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 三角形に分解してFaceリストを返す（MaterialIndex, Flags引き継ぎ）
        /// </summary>
        public List<Face> Triangulate()
        {
            var result = new List<Face>();

            if (VertexCount < 3)
                return result;

            if (IsTriangle)
            {
                result.Add(Clone());
                return result;
            }

            // 扇形分割（MaterialIndex, Flagsを引き継ぐ）
            for (int i = 1; i < VertexCount - 1; i++)
            {
                var tri = Face.CreateTriangle(
                    VertexIndices[0], VertexIndices[i], VertexIndices[i + 1],
                    UVIndices.Count > 0 ? UVIndices[0] : 0,
                    UVIndices.Count > i ? UVIndices[i] : 0,
                    UVIndices.Count > i + 1 ? UVIndices[i + 1] : 0,
                    NormalIndices.Count > 0 ? NormalIndices[0] : 0,
                    NormalIndices.Count > i ? NormalIndices[i] : 0,
                    NormalIndices.Count > i + 1 ? NormalIndices[i + 1] : 0,
                    MaterialIndex);
                tri.Flags = this.Flags;
                result.Add(tri);
            }
            return result;
        }

        /// <summary>
        /// 面を反転（頂点順序を逆にする）
        /// </summary>
        public void Flip()
        {
            VertexIndices.Reverse();
            UVIndices.Reverse();
            NormalIndices.Reverse();
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public Face Clone()
        {
            return new Face
            {
                Id = Id,
                VertexIndices = new List<int>(VertexIndices),
                UVIndices = new List<int>(UVIndices),
                NormalIndices = new List<int>(NormalIndices),
                MaterialIndex = MaterialIndex,
                Flags = Flags
            };
        }

        /// <summary>
        /// ディープコピー（新しいIDを割り当て）
        /// </summary>
        public Face CloneWithNewId(int newId)
        {
            var clone = Clone();
            clone.Id = newId;
            return clone;
        }
    }
}
