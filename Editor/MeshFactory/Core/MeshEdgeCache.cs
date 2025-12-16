// Assets/Editor/MeshFactory/Core/MeshEdgeCache.cs
// メッシュのエッジ抽出キャッシュ
// Phase 2: GPUバッファ生成用のエッジデータを提供

using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    /// <summary>
    /// GPU描画用の線分データ
    /// </summary>
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LineData
    {
        public int V1;           // 始点の頂点インデックス
        public int V2;           // 終点の頂点インデックス
        public int FaceIndex;    // 属する面のインデックス（カリング用）
        public int LineType;     // 0=通常エッジ, 1=補助線(2頂点Face)

        public static int SizeInBytes => sizeof(int) * 4;
    }

    /// <summary>
    /// メッシュのエッジ抽出キャッシュ
    /// </summary>
    public class MeshEdgeCache
    {
        // ================================================================
        // 出力データ
        // ================================================================

        /// <summary>全線分データ（GPUバッファ転送用）</summary>
        public List<LineData> Lines { get; private set; } = new List<LineData>();

        /// <summary>通常エッジのみ（重複排除済み、従来描画用）</summary>
        public List<(int v1, int v2)> UniqueEdges { get; private set; } = new List<(int, int)>();

        /// <summary>補助線のみ（従来描画用）</summary>
        public List<(int v1, int v2)> AuxLines { get; private set; } = new List<(int, int)>();

        /// <summary>線分数</summary>
        public int LineCount => Lines.Count;

        /// <summary>通常エッジ数</summary>
        public int EdgeCount => UniqueEdges.Count;

        /// <summary>補助線数</summary>
        public int AuxLineCount => AuxLines.Count;

        // ================================================================
        // キャッシュ管理
        // ================================================================

        private MeshData _cachedMeshData;

        /// <summary>
        /// メッシュデータからエッジを抽出
        /// </summary>
        /// <param name="meshData">メッシュデータ</param>
        /// <param name="force">強制更新</param>
        public void Update(MeshData meshData, bool force = false)
        {
            if (meshData == null)
            {
                Clear();
                return;
            }

            // 同じメッシュインスタンスなら更新不要
            if (!force && _cachedMeshData == meshData && Lines.Count > 0)
                return;

            _cachedMeshData = meshData;
            
            Lines.Clear();
            UniqueEdges.Clear();
            AuxLines.Clear();

            var edgeSet = new HashSet<(int, int)>();

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];

                if (face.VertexCount == 2)
                {
                    // 補助線
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];

                    Lines.Add(new LineData
                    {
                        V1 = v1,
                        V2 = v2,
                        FaceIndex = faceIdx,
                        LineType = 1  // 補助線
                    });

                    AuxLines.Add((v1, v2));
                }
                else if (face.VertexCount >= 3)
                {
                    // 通常エッジ
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int v1 = face.VertexIndices[i];
                        int v2 = face.VertexIndices[(i + 1) % face.VertexCount];

                        // GPU用：面ごとに全エッジを追加（カリングで必要）
                        Lines.Add(new LineData
                        {
                            V1 = v1,
                            V2 = v2,
                            FaceIndex = faceIdx,
                            LineType = 0  // 通常エッジ
                        });

                        // 従来描画用：重複排除
                        int a = v1, b = v2;
                        if (a > b) (a, b) = (b, a);
                        if (edgeSet.Add((a, b)))
                        {
                            UniqueEdges.Add((a, b));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// キャッシュを無効化
        /// </summary>
        public void Invalidate()
        {
            _cachedMeshData = null;
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void Clear()
        {
            Lines.Clear();
            UniqueEdges.Clear();
            AuxLines.Clear();
            _cachedMeshData = null;
        }
    }
}
