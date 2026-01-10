// Assets/Editor/MeshFactory/Rendering/UnifiedGPUBuffer.cs
// 統合GPUバッファ管理
// 全メッシュのデータを1つのバッファに統合し、バッファ切替コストを排除

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    /// <summary>
    /// メッシュごとのオフセット情報
    /// </summary>
    public struct MeshBufferInfo
    {
        public int VertexOffset;
        public int VertexCount;
        public int EdgeOffset;
        public int EdgeCount;
        public int FaceOffset;
        public int FaceCount;
        public int FaceIndexOffset;
        public int FaceIndexCount;
        
        // GPU転送用サイズ（32bytes = 8 * int）
        public static int SizeInBytes => sizeof(int) * 8;
    }
    
    /// <summary>
    /// 統合GPUバッファ
    /// 全メッシュのデータを1つのバッファに統合
    /// </summary>
    public class UnifiedGPUBuffer : IDisposable
    {
        // ================================================================
        // 統合バッファ
        // ================================================================
        
        /// <summary>全頂点位置 (Vector3[])</summary>
        public ComputeBuffer PositionBuffer { get; private set; }
        
        /// <summary>全エッジ (int2[] = 頂点インデックスペア)</summary>
        public ComputeBuffer EdgeBuffer { get; private set; }
        
        /// <summary>全面の頂点インデックス (int[])</summary>
        public ComputeBuffer FaceIndexBuffer { get; private set; }
        
        /// <summary>全面の頂点オフセット (int[])</summary>
        public ComputeBuffer FaceOffsetBuffer { get; private set; }
        
        /// <summary>全面の頂点数 (int[])</summary>
        public ComputeBuffer FaceCountBuffer { get; private set; }
        
        /// <summary>メッシュ情報（オフセットテーブル）</summary>
        public ComputeBuffer MeshInfoBuffer { get; private set; }
        
        // ================================================================
        // 統計情報
        // ================================================================
        
        public int TotalVertexCount { get; private set; }
        public int TotalEdgeCount { get; private set; }
        public int TotalFaceCount { get; private set; }
        public int TotalFaceIndexCount { get; private set; }
        public int MeshCount { get; private set; }
        
        public bool IsValid => PositionBuffer != null && TotalVertexCount > 0;
        public bool IsDisposed { get; private set; }
        
        // ================================================================
        // 内部データ
        // ================================================================
        
        private MeshBufferInfo[] _meshInfos;
        private List<MeshContext> _meshList;
        
        // ================================================================
        // 公開メソッド
        // ================================================================
        
        /// <summary>
        /// 全メッシュからバッファを構築
        /// </summary>
        public void RebuildAll(List<MeshContext> meshList, MeshEdgeCache edgeCache)
        {
            if (meshList == null || meshList.Count == 0)
            {
                ReleaseBuffers();
                return;
            }
            
            _meshList = meshList;
            MeshCount = meshList.Count;
            
            // 1. サイズ計算とオフセット決定（TotalVertexCount等が設定される）
            CalculateOffsets(meshList, edgeCache);
            
            // 2. 古いバッファを解放（カウントは保持）
            ReleaseBuffersOnly();
            
            if (TotalVertexCount == 0) return;
            
            // 3. データ収集と転送
            BuildPositionBuffer(meshList);
            BuildEdgeBuffer(meshList, edgeCache);
            BuildFaceBuffers(meshList);
            BuildMeshInfoBuffer();
        }
        
        /// <summary>
        /// 特定メッシュの頂点位置のみ更新（高速パス）
        /// </summary>
        public void UpdateMeshPositions(int meshIndex, MeshObject mesh)
        {
            if (!IsValid || meshIndex < 0 || meshIndex >= MeshCount) return;
            if (mesh == null) return;
            
            var info = _meshInfos[meshIndex];
            if (info.VertexCount != mesh.VertexCount)
            {
                Debug.LogWarning($"[UnifiedGPUBuffer] Vertex count mismatch for mesh {meshIndex}. Full rebuild required.");
                return;
            }
            
            // 該当範囲の頂点データを更新
            var positions = new Vector3[info.VertexCount];
            for (int i = 0; i < info.VertexCount; i++)
            {
                positions[i] = mesh.Vertices[i].Position;
            }
            
            PositionBuffer.SetData(positions, 0, info.VertexOffset, info.VertexCount);
        }
        
        /// <summary>
        /// 特定メッシュのトポロジーを更新
        /// ※ 頂点数/面数が変わる場合は全体再構築が必要
        /// </summary>
        public bool UpdateMeshTopology(int meshIndex, MeshObject mesh, MeshEdgeCache edgeCache)
        {
            if (!IsValid || meshIndex < 0 || meshIndex >= MeshCount) return false;
            if (mesh == null) return false;
            
            var info = _meshInfos[meshIndex];
            
            // サイズが変わった場合は全体再構築が必要
            edgeCache.Update(mesh, force: true);
            if (info.VertexCount != mesh.VertexCount ||
                info.FaceCount != mesh.FaceCount ||
                info.EdgeCount != edgeCache.LineCount)
            {
                return false; // 呼び出し側でRebuildAllを実行
            }
            
            // 頂点位置更新
            UpdateMeshPositions(meshIndex, mesh);
            
            // エッジ更新
            UpdateMeshEdges(meshIndex, edgeCache);
            
            // 面更新
            UpdateMeshFaces(meshIndex, mesh);
            
            return true;
        }
        
        /// <summary>
        /// メッシュ情報を取得
        /// </summary>
        public MeshBufferInfo GetMeshInfo(int meshIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return default;
            return _meshInfos[meshIndex];
        }
        
        // ================================================================
        // インデックス変換メソッド
        // ================================================================
        
        /// <summary>
        /// グローバル頂点インデックスからメッシュインデックスとローカル頂点インデックスを取得
        /// </summary>
        /// <param name="globalVertexIndex">統合バッファ内のグローバル頂点インデックス</param>
        /// <param name="meshIndex">出力: メッシュインデックス</param>
        /// <param name="localVertexIndex">出力: メッシュ内のローカル頂点インデックス</param>
        /// <returns>変換成功ならtrue</returns>
        public bool GlobalToLocalVertex(int globalVertexIndex, out int meshIndex, out int localVertexIndex)
        {
            meshIndex = -1;
            localVertexIndex = -1;
            
            if (_meshInfos == null || globalVertexIndex < 0 || globalVertexIndex >= TotalVertexCount)
                return false;
            
            for (int i = 0; i < _meshInfos.Length; i++)
            {
                var info = _meshInfos[i];
                if (globalVertexIndex >= info.VertexOffset && 
                    globalVertexIndex < info.VertexOffset + info.VertexCount)
                {
                    meshIndex = i;
                    localVertexIndex = globalVertexIndex - info.VertexOffset;
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// グローバルエッジインデックスからメッシュインデックスとローカルエッジインデックスを取得
        /// </summary>
        public bool GlobalToLocalEdge(int globalEdgeIndex, out int meshIndex, out int localEdgeIndex)
        {
            meshIndex = -1;
            localEdgeIndex = -1;
            
            if (_meshInfos == null || globalEdgeIndex < 0 || globalEdgeIndex >= TotalEdgeCount)
                return false;
            
            for (int i = 0; i < _meshInfos.Length; i++)
            {
                var info = _meshInfos[i];
                if (globalEdgeIndex >= info.EdgeOffset && 
                    globalEdgeIndex < info.EdgeOffset + info.EdgeCount)
                {
                    meshIndex = i;
                    localEdgeIndex = globalEdgeIndex - info.EdgeOffset;
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// グローバル面インデックスからメッシュインデックスとローカル面インデックスを取得
        /// </summary>
        public bool GlobalToLocalFace(int globalFaceIndex, out int meshIndex, out int localFaceIndex)
        {
            meshIndex = -1;
            localFaceIndex = -1;
            
            if (_meshInfos == null || globalFaceIndex < 0 || globalFaceIndex >= TotalFaceCount)
                return false;
            
            for (int i = 0; i < _meshInfos.Length; i++)
            {
                var info = _meshInfos[i];
                if (globalFaceIndex >= info.FaceOffset && 
                    globalFaceIndex < info.FaceOffset + info.FaceCount)
                {
                    meshIndex = i;
                    localFaceIndex = globalFaceIndex - info.FaceOffset;
                    return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// ローカル頂点インデックスからグローバル頂点インデックスを取得
        /// </summary>
        public int LocalToGlobalVertex(int meshIndex, int localVertexIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return -1;
            
            var info = _meshInfos[meshIndex];
            if (localVertexIndex < 0 || localVertexIndex >= info.VertexCount)
                return -1;
            
            return info.VertexOffset + localVertexIndex;
        }
        
        /// <summary>
        /// ローカルエッジインデックスからグローバルエッジインデックスを取得
        /// </summary>
        public int LocalToGlobalEdge(int meshIndex, int localEdgeIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return -1;
            
            var info = _meshInfos[meshIndex];
            if (localEdgeIndex < 0 || localEdgeIndex >= info.EdgeCount)
                return -1;
            
            return info.EdgeOffset + localEdgeIndex;
        }
        
        /// <summary>
        /// ローカル面インデックスからグローバル面インデックスを取得
        /// </summary>
        public int LocalToGlobalFace(int meshIndex, int localFaceIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return -1;
            
            var info = _meshInfos[meshIndex];
            if (localFaceIndex < 0 || localFaceIndex >= info.FaceCount)
                return -1;
            
            return info.FaceOffset + localFaceIndex;
        }
        
        /// <summary>
        /// 指定メッシュの頂点オフセットを取得
        /// </summary>
        public int GetVertexOffset(int meshIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return 0;
            return _meshInfos[meshIndex].VertexOffset;
        }
        
        /// <summary>
        /// 指定メッシュのエッジオフセットを取得
        /// </summary>
        public int GetEdgeOffset(int meshIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return 0;
            return _meshInfos[meshIndex].EdgeOffset;
        }
        
        /// <summary>
        /// 指定メッシュの面オフセットを取得
        /// </summary>
        public int GetFaceOffset(int meshIndex)
        {
            if (_meshInfos == null || meshIndex < 0 || meshIndex >= _meshInfos.Length)
                return 0;
            return _meshInfos[meshIndex].FaceOffset;
        }
        
        /// <summary>
        /// リソース解放
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            ReleaseBuffers();
            IsDisposed = true;
        }
        
        // ================================================================
        // 内部メソッド
        // ================================================================
        
        private void CalculateOffsets(List<MeshContext> meshList, MeshEdgeCache edgeCache)
        {
            _meshInfos = new MeshBufferInfo[meshList.Count];
            
            int vertexOffset = 0;
            int edgeOffset = 0;
            int faceOffset = 0;
            int faceIndexOffset = 0;
            
            for (int i = 0; i < meshList.Count; i++)
            {
                var ctx = meshList[i];
                var mesh = ctx?.MeshObject;
                
                if (mesh == null)
                {
                    _meshInfos[i] = default;
                    continue;
                }
                
                edgeCache.Update(mesh);
                
                int vertexCount = mesh.VertexCount;
                int edgeCount = edgeCache.LineCount;
                int faceCount = mesh.FaceCount;
                int faceIndexCount = 0;
                
                // 面の頂点インデックス数を計算
                for (int f = 0; f < faceCount; f++)
                {
                    faceIndexCount += mesh.Faces[f].VertexCount;
                }
                
                _meshInfos[i] = new MeshBufferInfo
                {
                    VertexOffset = vertexOffset,
                    VertexCount = vertexCount,
                    EdgeOffset = edgeOffset,
                    EdgeCount = edgeCount,
                    FaceOffset = faceOffset,
                    FaceCount = faceCount,
                    FaceIndexOffset = faceIndexOffset,
                    FaceIndexCount = faceIndexCount
                };
                
                vertexOffset += vertexCount;
                edgeOffset += edgeCount;
                faceOffset += faceCount;
                faceIndexOffset += faceIndexCount;
            }
            
            TotalVertexCount = vertexOffset;
            TotalEdgeCount = edgeOffset;
            TotalFaceCount = faceOffset;
            TotalFaceIndexCount = faceIndexOffset;
        }
        
        private void BuildPositionBuffer(List<MeshContext> meshList)
        {
            if (TotalVertexCount == 0) return;
            
            var allPositions = new Vector3[TotalVertexCount];
            
            for (int i = 0; i < meshList.Count; i++)
            {
                var mesh = meshList[i]?.MeshObject;
                if (mesh == null) continue;
                
                var info = _meshInfos[i];
                for (int v = 0; v < info.VertexCount; v++)
                {
                    allPositions[info.VertexOffset + v] = mesh.Vertices[v].Position;
                }
            }
            
            PositionBuffer = new ComputeBuffer(TotalVertexCount, sizeof(float) * 3);
            PositionBuffer.SetData(allPositions);
        }
        
        private void BuildEdgeBuffer(List<MeshContext> meshList, MeshEdgeCache edgeCache)
        {
            if (TotalEdgeCount == 0) return;
            
            // LineData: V1, V2, FaceIndex, LineType (4 ints)
            var allEdges = new int[TotalEdgeCount * 4];
            
            for (int i = 0; i < meshList.Count; i++)
            {
                var mesh = meshList[i]?.MeshObject;
                if (mesh == null) continue;
                
                edgeCache.Update(mesh);
                var lines = edgeCache.Lines;
                var info = _meshInfos[i];
                
                for (int e = 0; e < info.EdgeCount; e++)
                {
                    var line = lines[e];
                    int baseIdx = (info.EdgeOffset + e) * 4;
                    // 頂点インデックスにオフセットを加算
                    allEdges[baseIdx] = line.V1 + info.VertexOffset;
                    allEdges[baseIdx + 1] = line.V2 + info.VertexOffset;
                    // FaceIndexにオフセットを加算してグローバル化
                    allEdges[baseIdx + 2] = line.FaceIndex + info.FaceOffset;
                    allEdges[baseIdx + 3] = line.LineType;
                }
            }
            
            EdgeBuffer = new ComputeBuffer(TotalEdgeCount, sizeof(int) * 4);
            EdgeBuffer.SetData(allEdges);
        }
        
        private void BuildFaceBuffers(List<MeshContext> meshList)
        {
            if (TotalFaceCount == 0) return;
            
            var allFaceIndices = new int[TotalFaceIndexCount];
            var allFaceOffsets = new int[TotalFaceCount];
            var allFaceCounts = new int[TotalFaceCount];
            
            for (int i = 0; i < meshList.Count; i++)
            {
                var mesh = meshList[i]?.MeshObject;
                if (mesh == null) continue;
                
                var info = _meshInfos[i];
                int localFaceIndexOffset = 0;
                
                for (int f = 0; f < info.FaceCount; f++)
                {
                    var face = mesh.Faces[f];
                    int globalFaceIdx = info.FaceOffset + f;
                    
                    allFaceOffsets[globalFaceIdx] = info.FaceIndexOffset + localFaceIndexOffset;
                    allFaceCounts[globalFaceIdx] = face.VertexCount;
                    
                    for (int v = 0; v < face.VertexCount; v++)
                    {
                        // 面の頂点インデックスにオフセットを加算
                        allFaceIndices[info.FaceIndexOffset + localFaceIndexOffset + v] = 
                            face.VertexIndices[v] + info.VertexOffset;
                    }
                    
                    localFaceIndexOffset += face.VertexCount;
                }
            }
            
            if (TotalFaceIndexCount > 0)
            {
                FaceIndexBuffer = new ComputeBuffer(TotalFaceIndexCount, sizeof(int), ComputeBufferType.Raw);
                FaceIndexBuffer.SetData(allFaceIndices);
            }
            
            FaceOffsetBuffer = new ComputeBuffer(TotalFaceCount, sizeof(int), ComputeBufferType.Raw);
            FaceOffsetBuffer.SetData(allFaceOffsets);
            
            FaceCountBuffer = new ComputeBuffer(TotalFaceCount, sizeof(int), ComputeBufferType.Raw);
            FaceCountBuffer.SetData(allFaceCounts);
        }
        
        private void BuildMeshInfoBuffer()
        {
            if (MeshCount == 0) return;
            
            // MeshBufferInfo構造体を直接転送
            MeshInfoBuffer = new ComputeBuffer(MeshCount, MeshBufferInfo.SizeInBytes);
            MeshInfoBuffer.SetData(_meshInfos);
        }
        
        private void UpdateMeshEdges(int meshIndex, MeshEdgeCache edgeCache)
        {
            var info = _meshInfos[meshIndex];
            if (info.EdgeCount == 0) return;
            
            var lines = edgeCache.Lines;
            var edgeData = new int[info.EdgeCount * 2];
            
            for (int e = 0; e < info.EdgeCount; e++)
            {
                edgeData[e * 2] = lines[e].V1 + info.VertexOffset;
                edgeData[e * 2 + 1] = lines[e].V2 + info.VertexOffset;
            }
            
            // SetDataのオフセット版を使用
            var tempBuffer = new int[info.EdgeCount * 2];
            Array.Copy(edgeData, tempBuffer, edgeData.Length);
            EdgeBuffer.SetData(tempBuffer, 0, info.EdgeOffset * 2, info.EdgeCount * 2);
        }
        
        private void UpdateMeshFaces(int meshIndex, MeshObject mesh)
        {
            var info = _meshInfos[meshIndex];
            if (info.FaceCount == 0) return;
            
            // 面のインデックスデータを更新
            var faceIndices = new int[info.FaceIndexCount];
            var faceOffsets = new int[info.FaceCount];
            var faceCounts = new int[info.FaceCount];
            
            int localOffset = 0;
            for (int f = 0; f < info.FaceCount; f++)
            {
                var face = mesh.Faces[f];
                faceOffsets[f] = info.FaceIndexOffset + localOffset;
                faceCounts[f] = face.VertexCount;
                
                for (int v = 0; v < face.VertexCount; v++)
                {
                    faceIndices[localOffset + v] = face.VertexIndices[v] + info.VertexOffset;
                }
                localOffset += face.VertexCount;
            }
            
            FaceIndexBuffer.SetData(faceIndices, 0, info.FaceIndexOffset, info.FaceIndexCount);
            FaceOffsetBuffer.SetData(faceOffsets, 0, info.FaceOffset, info.FaceCount);
            FaceCountBuffer.SetData(faceCounts, 0, info.FaceOffset, info.FaceCount);
        }
        
        private void ReleaseBuffers()
        {
            ReleaseBuffersOnly();
            
            TotalVertexCount = 0;
            TotalEdgeCount = 0;
            TotalFaceCount = 0;
            TotalFaceIndexCount = 0;
            MeshCount = 0;
        }
        
        /// <summary>
        /// バッファのみ解放（カウントは保持）
        /// </summary>
        private void ReleaseBuffersOnly()
        {
            PositionBuffer?.Release();
            PositionBuffer = null;
            
            EdgeBuffer?.Release();
            EdgeBuffer = null;
            
            FaceIndexBuffer?.Release();
            FaceIndexBuffer = null;
            
            FaceOffsetBuffer?.Release();
            FaceOffsetBuffer = null;
            
            FaceCountBuffer?.Release();
            FaceCountBuffer = null;
            
            MeshInfoBuffer?.Release();
            MeshInfoBuffer = null;
        }
    }
}
