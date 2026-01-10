// Assets/Editor/MeshFactory/Core/Buffers/UnifiedBufferManager_Build.cs
// 統合バッファ管理クラス - データ構築
// MeshObject/MeshContextからバッファデータを構築

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;
using MeshFactory.Selection;

namespace MeshFactory.Core
{
    public partial class UnifiedBufferManager
    {
        // ============================================================
        // Level 5: トポロジー再構築
        // ============================================================

        /// <summary>
        /// 単一モデルからバッファを構築
        /// </summary>
        public void BuildFromModel(ModelContext model, int modelIndex = 0)
        {
            if (model == null)
            {
                ClearData();
                return;
            }

            var meshContexts = model.MeshContextList;
            BuildFromMeshContexts(meshContexts, modelIndex);
        }

        /// <summary>
        /// MeshContextリストからバッファを構築
        /// </summary>
        public void BuildFromMeshContexts(List<MeshContext> meshContexts, int modelIndex = 0)
        {
            if (!_isInitialized)
                Initialize();

            ClearData();

            if (meshContexts == null || meshContexts.Count == 0)
                return;

            // 必要な容量を計算
            int totalVerts = 0;
            int totalLines = 0;
            int totalFaces = 0;
            int totalIndices = 0;
            int meshCount = 0;

            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;
                var mo = mc.MeshObject;

                totalVerts += mo.VertexCount;
                totalFaces += mo.FaceCount;
                meshCount++;

                // ライン数を計算（エッジ + 補助線）
                foreach (var face in mo.Faces)
                {
                    if (face.VertexCount == 2)
                    {
                        totalLines++; // 補助線
                    }
                    else if (face.VertexCount >= 3)
                    {
                        totalLines += face.VertexCount; // エッジ
                        totalIndices += (face.VertexCount - 2) * 3; // 三角形化
                    }
                }
            }

            Debug.Log($"[BuildFromMeshContexts] meshCount={meshCount}, totalVerts={totalVerts}, totalLines={totalLines}, totalFaces={totalFaces}, _meshInfos.Length={_meshInfos?.Length ?? 0}");
            EnsureCapacity(totalVerts, totalLines, totalFaces, totalIndices, meshCount);

            // データ構築
            uint vertexOffset = 0;
            uint lineOffset = 0;
            uint faceOffset = 0;
            uint indexOffset = 0;

            for (int meshIdx = 0; meshIdx < meshContexts.Count; meshIdx++)
            {
                var meshContext = meshContexts[meshIdx];
                if (meshContext?.MeshObject == null)
                    continue;

                var meshObject = meshContext.MeshObject;

                // MeshInfo作成
                _meshInfos[_meshCount] = new MeshInfo
                {
                    VertexStart = vertexOffset,
                    VertexCount = (uint)meshObject.VertexCount,
                    LineStart = lineOffset,
                    LineCount = 0, // 後で更新
                    FaceStart = faceOffset,
                    FaceCount = (uint)meshObject.FaceCount,
                    IndexStart = indexOffset,
                    IndexCount = 0, // 後で更新
                    Flags = 0,
                    ModelIndex = (uint)modelIndex
                };

                // 頂点データ構築
                BuildVertexData(meshObject, meshContext, modelIndex, meshIdx, ref vertexOffset);

                // ライン/エッジデータ構築（faceOffsetをグローバルFaceIndexのベースとして渡す）
                uint lineCount = BuildLineData(meshObject, meshContext, modelIndex, meshIdx, vertexOffset - (uint)meshObject.VertexCount, faceOffset, ref lineOffset);

                // 面データ構築
                uint indexCount = BuildFaceData(meshObject, meshContext, modelIndex, meshIdx, vertexOffset - (uint)meshObject.VertexCount, ref faceOffset, ref indexOffset);

                // MeshInfo更新
                _meshInfos[_meshCount].LineCount = lineCount;
                _meshInfos[_meshCount].IndexCount = indexCount;

                _meshCount++;
            }

            _totalVertexCount = (int)vertexOffset;
            _totalLineCount = (int)lineOffset;
            _totalFaceCount = (int)faceOffset;
            _totalIndexCount = (int)indexOffset;
            _modelCount = 1;

            // ModelInfo作成
            _modelInfos[0] = new ModelInfo
            {
                MeshStart = 0,
                MeshCount = (uint)_meshCount,
                VertexStart = 0,
                VertexCount = (uint)_totalVertexCount,
                Flags = 0
            };

            // GPUにアップロード
            UploadAllBuffers();
        }

        /// <summary>
        /// 頂点データを構築
        /// </summary>
        private void BuildVertexData(
            MeshObject meshObject,
            MeshContext meshContext,
            int modelIndex,
            int meshIndex,
            ref uint vertexOffset)
        {
            bool isVisible = meshContext?.IsVisible ?? true;
            bool isLocked = meshContext?.IsLocked ?? false;

            for (int v = 0; v < meshObject.VertexCount; v++)
            {
                var vertex = meshObject.Vertices[v];
                uint globalIdx = vertexOffset + (uint)v;

                _positions[globalIdx] = vertex.Position;
                // Normalsリストの最初の要素を使用（なければゼロ）
                _normals[globalIdx] = vertex.Normals.Count > 0 ? vertex.Normals[0] : Vector3.up;
                // UVsリストの最初の要素を使用（なければゼロ）
                _uvs[globalIdx] = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;

                // フラグ計算
                _vertexFlags[globalIdx] = (uint)_flagManager.ComputeVertexFlags(
                    modelIndex, meshIndex, v,
                    isVisible, isLocked, false);
            }

            vertexOffset += (uint)meshObject.VertexCount;
        }

        /// <summary>
        /// ライン/エッジデータを構築
        /// </summary>
        private uint BuildLineData(
            MeshObject meshObject,
            MeshContext meshContext,
            int modelIndex,
            int meshIndex,
            uint vertexBase,
            uint faceBase,
            ref uint lineOffset)
        {
            uint startLineOffset = lineOffset;
            bool isVisible = meshContext?.IsVisible ?? true;
            bool isLocked = meshContext?.IsLocked ?? false;

            // エッジの重複を避けるためのセット
            var processedEdges = new HashSet<VertexPair>();

            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                uint globalFaceIndex = faceBase + (uint)faceIdx;

                if (face.VertexCount == 2)
                {
                    // 補助線
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];

                    _lines[lineOffset] = new UnifiedLine
                    {
                        V1 = vertexBase + (uint)v1,
                        V2 = vertexBase + (uint)v2,
                        Flags = (uint)_flagManager.ComputeLineFlags(
                            modelIndex, meshIndex, v1, v2, faceIdx,
                            true, isVisible, isLocked, false, false),
                        FaceIndex = globalFaceIndex,
                        MeshIndex = (uint)meshIndex,
                        ModelIndex = (uint)modelIndex
                    };
                    _lineFlags[lineOffset] = _lines[lineOffset].Flags;
                    lineOffset++;
                }
                else if (face.VertexCount >= 3)
                {
                    // 面のエッジ
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int v1 = face.VertexIndices[i];
                        int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                        var pair = new VertexPair(v1, v2);

                        // 重複チェック（両面エッジを1回だけ追加）
                        if (processedEdges.Contains(pair))
                            continue;

                        processedEdges.Add(pair);

                        _lines[lineOffset] = new UnifiedLine
                        {
                            V1 = vertexBase + (uint)pair.V1,
                            V2 = vertexBase + (uint)pair.V2,
                            Flags = (uint)_flagManager.ComputeLineFlags(
                                modelIndex, meshIndex, v1, v2, faceIdx,
                                false, isVisible, isLocked, false, false),
                            FaceIndex = globalFaceIndex,
                            MeshIndex = (uint)meshIndex,
                            ModelIndex = (uint)modelIndex
                        };
                        _lineFlags[lineOffset] = _lines[lineOffset].Flags;
                        lineOffset++;
                    }
                }
            }

            return lineOffset - startLineOffset;
        }

        /// <summary>
        /// 面データを構築
        /// </summary>
        private uint BuildFaceData(
            MeshObject meshObject,
            MeshContext meshContext,
            int modelIndex,
            int meshIndex,
            uint vertexBase,
            ref uint faceOffset,
            ref uint indexOffset)
        {
            uint startIndexOffset = indexOffset;
            bool isVisible = meshContext?.IsVisible ?? true;
            bool isLocked = meshContext?.IsLocked ?? false;

            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];

                if (face.VertexCount < 3)
                {
                    faceOffset++;
                    continue; // 補助線はスキップ
                }

                // 面情報
                _faces[faceOffset] = new UnifiedFace
                {
                    IndexStart = indexOffset,
                    VertexCount = (uint)face.VertexCount,
                    Flags = (uint)_flagManager.ComputeFaceFlags(
                        modelIndex, meshIndex, faceIdx,
                        isVisible, isLocked, false),
                    MaterialIndex = (uint)face.MaterialIndex,
                    MeshIndex = (uint)meshIndex,
                    ModelIndex = (uint)modelIndex,
                    Normal = ComputeFaceNormal(meshObject, face)
                };
                _faceFlags[faceOffset] = _faces[faceOffset].Flags;

                // 三角形化してインデックス追加
                for (int i = 1; i < face.VertexCount - 1; i++)
                {
                    _indices[indexOffset++] = vertexBase + (uint)face.VertexIndices[0];
                    _indices[indexOffset++] = vertexBase + (uint)face.VertexIndices[i];
                    _indices[indexOffset++] = vertexBase + (uint)face.VertexIndices[i + 1];
                }

                faceOffset++;
            }

            return indexOffset - startIndexOffset;
        }

        // ============================================================
        // GPUアップロード
        // ============================================================

        /// <summary>
        /// 全バッファをGPUにアップロード
        /// </summary>
        public void UploadAllBuffers()
        {
            if (_totalVertexCount > 0)
            {
                _positionBuffer.SetData(_positions, 0, 0, _totalVertexCount);
                _normalBuffer.SetData(_normals, 0, 0, _totalVertexCount);
                _uvBuffer.SetData(_uvs, 0, 0, _totalVertexCount);
                _vertexFlagsBuffer.SetData(_vertexFlags, 0, 0, _totalVertexCount);
            }

            if (_totalLineCount > 0)
            {
                _lineBuffer.SetData(_lines, 0, 0, _totalLineCount);
                _lineFlagsBuffer.SetData(_lineFlags, 0, 0, _totalLineCount);
            }

            if (_totalFaceCount > 0)
            {
                _faceBuffer.SetData(_faces, 0, 0, _totalFaceCount);
                _faceFlagsBuffer.SetData(_faceFlags, 0, 0, _totalFaceCount);
            }

            if (_totalIndexCount > 0)
            {
                _indexBuffer.SetData(_indices, 0, 0, _totalIndexCount);
            }

            if (_meshCount > 0)
            {
                _meshInfoBuffer.SetData(_meshInfos, 0, 0, _meshCount);
            }

            if (_modelCount > 0)
            {
                _modelInfoBuffer.SetData(_modelInfos, 0, 0, _modelCount);
            }
        }

        // ============================================================
        // Level 4: 位置更新
        // ============================================================

        /// <summary>
        /// 頂点位置のみ更新（トポロジー不変）
        /// </summary>
        public void UpdatePositions(MeshObject meshObject, int meshIndex)
        {
            if (meshObject == null || meshIndex < 0 || meshIndex >= _meshCount)
                return;

            var meshInfo = _meshInfos[meshIndex];
            uint baseOffset = meshInfo.VertexStart;

            for (int v = 0; v < meshObject.VertexCount; v++)
            {
                uint globalIdx = baseOffset + (uint)v;
                if (globalIdx >= _totalVertexCount)
                    break;

                _positions[globalIdx] = meshObject.Vertices[v].Position;
            }

            // GPUにアップロード
            _positionBuffer.SetData(_positions, (int)baseOffset, (int)baseOffset, (int)meshInfo.VertexCount);
        }

        /// <summary>
        /// 全メッシュの位置を更新
        /// </summary>
        public void UpdateAllPositions(List<MeshContext> meshContexts)
        {
            if (meshContexts == null)
                return;

            for (int meshIdx = 0; meshIdx < meshContexts.Count && meshIdx < _meshCount; meshIdx++)
            {
                var mc = meshContexts[meshIdx];
                if (mc?.MeshObject == null)
                    continue;

                var meshInfo = _meshInfos[meshIdx];
                uint baseOffset = meshInfo.VertexStart;
                var meshObject = mc.MeshObject;

                for (int v = 0; v < meshObject.VertexCount; v++)
                {
                    uint globalIdx = baseOffset + (uint)v;
                    if (globalIdx >= _totalVertexCount)
                        break;

                    _positions[globalIdx] = meshObject.Vertices[v].Position;
                }
            }

            // 全頂点をアップロード
            if (_totalVertexCount > 0)
            {
                _positionBuffer.SetData(_positions, 0, 0, _totalVertexCount);
            }
        }

        // ============================================================
        // ヘルパーメソッド
        // ============================================================

        /// <summary>
        /// 面の法線を計算
        /// </summary>
        private Vector3 ComputeFaceNormal(MeshObject meshObject, Face face)
        {
            if (face.VertexCount < 3)
                return Vector3.up;

            var vertices = meshObject.Vertices;
            int i0 = face.VertexIndices[0];
            int i1 = face.VertexIndices[1];
            int i2 = face.VertexIndices[2];

            if (i0 >= vertices.Count || i1 >= vertices.Count || i2 >= vertices.Count)
                return Vector3.up;

            Vector3 v0 = vertices[i0].Position;
            Vector3 v1 = vertices[i1].Position;
            Vector3 v2 = vertices[i2].Position;

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            return normal.magnitude > 0.001f ? normal : Vector3.up;
        }
    }
}
