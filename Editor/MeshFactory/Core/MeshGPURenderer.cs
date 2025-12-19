// Editor/MeshFactory/Core/MeshGPURenderer.cs
// IMGUI対応版 GPU描画レンダラー

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    public class MeshGPURenderer : IDisposable
    {
        // シェーダー・マテリアル
        private ComputeShader _computeShader;
        private int _kernelClear, _kernelScreenPos, _kernelFaceVisibility, _kernelLineVisibility;
        private Material _pointMaterial, _lineMaterial;
        
        // バッファ
        private ComputeBuffer _positionBuffer;
        private ComputeBuffer _lineBuffer;
        private ComputeBuffer _faceVertexIndexBuffer;
        private ComputeBuffer _faceVertexOffsetBuffer;
        private ComputeBuffer _faceVertexCountBuffer;
        private ComputeBuffer _screenPositionBuffer;
        private ComputeBuffer _vertexVisibilityBuffer;
        private ComputeBuffer _faceVisibilityBuffer;
        private ComputeBuffer _lineVisibilityBuffer;
        private ComputeBuffer _selectionBuffer;
        private ComputeBuffer _lineSelectionBuffer;
        
        private uint[] _selectionData;
        private uint[] _lineSelectionData;
        private bool _initialized;
        private int _vertexCount, _faceCount, _lineCount;
        private MeshData _cachedMeshData;

        public bool IsAvailable => _initialized && SystemInfo.supportsComputeShaders;
        public bool CullingEnabled { get; set; } = true;

        public bool Initialize(ComputeShader computeShader, Shader pointShader, Shader lineShader)
        {
            if (_initialized) return true;
            
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("MeshGPURenderer: Compute shaders not supported");
                return false;
            }
            
            if (computeShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Compute shader is null");
                return false;
            }
            
            if (pointShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Point shader is null");
                return false;
            }
            
            if (lineShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Line shader is null");
                return false;
            }

            _computeShader = computeShader;
            
            try
            {
                _kernelClear = _computeShader.FindKernel("ClearBuffers");
                _kernelScreenPos = _computeShader.FindKernel("ComputeScreenPositions");
                _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
                _kernelLineVisibility = _computeShader.FindKernel("ComputeLineVisibility");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"MeshGPURenderer: Failed to find compute kernels: {e.Message}");
                return false;
            }

            _pointMaterial = new Material(pointShader) { hideFlags = HideFlags.HideAndDontSave };
            _lineMaterial = new Material(lineShader) { hideFlags = HideFlags.HideAndDontSave };
            
            _initialized = true;
            Debug.Log("MeshGPURenderer: Initialized successfully");
            return true;
        }

        public void UpdateBuffers(MeshData meshData, MeshEdgeCache edgeCache)
        {
            if (!_initialized || meshData == null || edgeCache == null) return;
            
            // キャッシュが有効な場合はスキップ
            if (_cachedMeshData == meshData && _vertexCount == meshData.VertexCount) return;

            _cachedMeshData = meshData;
            ReleaseBuffers();
            
            _vertexCount = meshData.VertexCount;
            _faceCount = meshData.FaceCount;
            
            if (_vertexCount == 0) return;

            // エッジキャッシュを更新
            edgeCache.Update(meshData, force: true);
            _lineCount = edgeCache.LineCount;

            // 頂点位置バッファ
            var positions = new Vector3[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = meshData.Vertices[i].Position;
            }
            _positionBuffer = new ComputeBuffer(_vertexCount, sizeof(float) * 3);
            _positionBuffer.SetData(positions);

            // 線分バッファ
            if (_lineCount > 0)
            {
                _lineBuffer = new ComputeBuffer(_lineCount, LineData.SizeInBytes);
                _lineBuffer.SetData(edgeCache.Lines);
            }

            // 面バッファ
            BuildFaceBuffers(meshData);

            // 出力バッファ
            _screenPositionBuffer = new ComputeBuffer(_vertexCount, sizeof(float) * 4);
            _vertexVisibilityBuffer = new ComputeBuffer(_vertexCount, sizeof(float));
            
            if (_faceCount > 0)
            {
                _faceVisibilityBuffer = new ComputeBuffer(_faceCount, sizeof(float));
            }
            
            if (_lineCount > 0)
            {
                _lineVisibilityBuffer = new ComputeBuffer(_lineCount, sizeof(float));
            }
            
            // 選択バッファ
            _selectionBuffer = new ComputeBuffer(_vertexCount, sizeof(uint));
            _selectionData = new uint[_vertexCount];
            
            // 線分選択バッファ
            if (_lineCount > 0)
            {
                _lineSelectionBuffer = new ComputeBuffer(_lineCount, sizeof(uint));
                _lineSelectionData = new uint[_lineCount];
            }
        }

        private void BuildFaceBuffers(MeshData meshData)
        {
            if (_faceCount == 0) return;
            
            var indices = new List<int>();
            var offsets = new int[_faceCount];
            var counts = new int[_faceCount];
            
            int offset = 0;
            for (int f = 0; f < _faceCount; f++)
            {
                var face = meshData.Faces[f];
                offsets[f] = offset;
                counts[f] = face.VertexCount;
                
                foreach (int vIdx in face.VertexIndices)
                {
                    indices.Add(vIdx);
                }
                offset += face.VertexCount;
            }
            
            if (indices.Count > 0)
            {
                _faceVertexIndexBuffer = new ComputeBuffer(indices.Count, sizeof(int));
                _faceVertexIndexBuffer.SetData(indices.ToArray());
            }
            
            _faceVertexOffsetBuffer = new ComputeBuffer(_faceCount, sizeof(int));
            _faceVertexOffsetBuffer.SetData(offsets);
            
            _faceVertexCountBuffer = new ComputeBuffer(_faceCount, sizeof(int));
            _faceVertexCountBuffer.SetData(counts);
        }

        public void UpdateSelection(HashSet<int> selectedVertices)
        {
            if (!_initialized || _selectionBuffer == null || _selectionData == null) return;
            
            Array.Clear(_selectionData, 0, _selectionData.Length);
            
            if (selectedVertices != null)
            {
                foreach (int idx in selectedVertices)
                {
                    if (idx >= 0 && idx < _selectionData.Length)
                    {
                        _selectionData[idx] = 1;
                    }
                }
            }
            
            _selectionBuffer.SetData(_selectionData);
        }

        public void UpdateLineSelection(HashSet<(int, int)> selectedEdges, MeshEdgeCache edgeCache)
        {
            if (!_initialized || _lineSelectionBuffer == null || _lineSelectionData == null) return;
            
            Array.Clear(_lineSelectionData, 0, _lineSelectionData.Length);
            
            if (selectedEdges != null && selectedEdges.Count > 0 && edgeCache != null)
            {
                var lines = edgeCache.Lines;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    // エッジは正規化して比較（小さい方が先）
                    int v1 = line.V1 < line.V2 ? line.V1 : line.V2;
                    int v2 = line.V1 < line.V2 ? line.V2 : line.V1;
                    
                    if (selectedEdges.Contains((v1, v2)))
                    {
                        _lineSelectionData[i] = 1;
                    }
                }
            }
            
            _lineSelectionBuffer.SetData(_lineSelectionData);
        }

        public void DispatchCompute(Matrix4x4 mvp, Rect previewRect, Vector2 windowSize)
        {
            if (!_initialized || _vertexCount == 0) return;

            // 共通パラメータ設定
            _computeShader.SetMatrix("_MATRIX_MVP", mvp);
            _computeShader.SetVector("_ScreenParams", new Vector4(windowSize.x, windowSize.y, 0, 0));
            _computeShader.SetVector("_PreviewRect", new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height));
            _computeShader.SetInt("_VertexCount", _vertexCount);
            _computeShader.SetInt("_FaceCount", _faceCount);
            _computeShader.SetInt("_LineCount", _lineCount);

            int threadGroups(int count) => Mathf.CeilToInt(count / 64f);

            // 1. バッファクリア
            SetBuffer(_kernelClear, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            SetBuffer(_kernelClear, "_ScreenPositionBuffer", _screenPositionBuffer);
            SetBuffer(_kernelClear, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
            SetBuffer(_kernelClear, "_LineVisibilityBuffer", _lineVisibilityBuffer);
            
            int maxCount = Mathf.Max(_vertexCount, Mathf.Max(_faceCount, _lineCount));
            _computeShader.Dispatch(_kernelClear, threadGroups(maxCount), 1, 1);

            // 2. スクリーン座標計算
            SetBuffer(_kernelScreenPos, "_PositionBuffer", _positionBuffer);
            SetBuffer(_kernelScreenPos, "_ScreenPositionBuffer", _screenPositionBuffer);
            _computeShader.Dispatch(_kernelScreenPos, threadGroups(_vertexCount), 1, 1);

            // 3. 面の可視性計算（カリング）
            // 3. 面の可視性計算（カリング）
            if (CullingEnabled && _faceCount > 0)
            {
                SetBuffer(_kernelFaceVisibility, "_ScreenPositionBuffer", _screenPositionBuffer);
                SetBuffer(_kernelFaceVisibility, "_FaceVertexIndexBuffer", _faceVertexIndexBuffer);
                SetBuffer(_kernelFaceVisibility, "_FaceVertexOffsetBuffer", _faceVertexOffsetBuffer);
                SetBuffer(_kernelFaceVisibility, "_FaceVertexCountBuffer", _faceVertexCountBuffer);
                SetBuffer(_kernelFaceVisibility, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
                SetBuffer(_kernelFaceVisibility, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
                _computeShader.Dispatch(_kernelFaceVisibility, threadGroups(_faceCount), 1, 1);

                // 4. 線の可視性計算
                if (_lineCount > 0)
                {
                    SetBuffer(_kernelLineVisibility, "_LineBuffer", _lineBuffer);
                    SetBuffer(_kernelLineVisibility, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
                    SetBuffer(_kernelLineVisibility, "_LineVisibilityBuffer", _lineVisibilityBuffer);
                    _computeShader.Dispatch(_kernelLineVisibility, threadGroups(_lineCount), 1, 1);
                }
            }
            else
            {
                // ★追加: カリングOFF時はすべて可視
                SetAllVisible();
            }
        }
        /// <summary>
        /// カリングOFF時: すべての頂点・線分を可視にする
        /// </summary>
        private void SetAllVisible()
        {
            // 頂点をすべて可視
            if (_vertexVisibilityData == null || _vertexVisibilityData.Length != _vertexCount)
                _vertexVisibilityData = new float[_vertexCount];

            for (int i = 0; i < _vertexCount; i++)
                _vertexVisibilityData[i] = 1.0f;

            _vertexVisibilityBuffer?.SetData(_vertexVisibilityData);

            // 線分をすべて可視
            if (_lineVisibilityData == null || _lineVisibilityData.Length != _lineCount)
                _lineVisibilityData = new float[_lineCount];

            for (int i = 0; i < _lineCount; i++)
                _lineVisibilityData[i] = 1.0f;

            _lineVisibilityBuffer?.SetData(_lineVisibilityData);
        }

        // フィールド追加
        private float[] _vertexVisibilityData;
        private float[] _lineVisibilityData;

        private void SetBuffer(int kernel, string name, ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                _computeShader.SetBuffer(kernel, name, buffer);
            }
        }

        public void DrawPoints(Rect previewRect, Vector2 windowSize, Vector2 guiOffset, float pointSize = 8f, float alpha = 1f)
        {
            if (!_initialized || _vertexCount == 0 || _pointMaterial == null) return;
            
            // マテリアルにバッファを設定
            _pointMaterial.SetBuffer("_ScreenPositionBuffer", _screenPositionBuffer);
            _pointMaterial.SetBuffer("_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            _pointMaterial.SetBuffer("_SelectionBuffer", _selectionBuffer);
            _pointMaterial.SetVector("_MeshFactoryScreenSize", windowSize);
            _pointMaterial.SetVector("_PreviewRect", new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height));
            _pointMaterial.SetVector("_GUIOffset", guiOffset);
            _pointMaterial.SetFloat("_PointSize", pointSize);
            _pointMaterial.SetFloat("_Alpha", alpha);
            
            // IMGUI描画コンテキストで描画
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, windowSize.x, windowSize.y, 0);
            
            _pointMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, _vertexCount);
            
            GL.PopMatrix();
        }

        public void DrawLines(Rect previewRect, Vector2 windowSize, Vector2 guiOffset, float lineWidth = 2f, float alpha = 1f, Color? edgeColor = null)
        {
            if (!_initialized || _lineCount == 0 || _lineMaterial == null) return;
            
            // マテリアルにバッファを設定
            _lineMaterial.SetBuffer("_ScreenPositionBuffer", _screenPositionBuffer);
            _lineMaterial.SetBuffer("_LineBuffer", _lineBuffer);
            _lineMaterial.SetBuffer("_LineVisibilityBuffer", _lineVisibilityBuffer);
            _lineMaterial.SetBuffer("_LineSelectionBuffer", _lineSelectionBuffer);
            _lineMaterial.SetVector("_MeshFactoryScreenSize", windowSize);
            _lineMaterial.SetVector("_PreviewRect", new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height));
            _lineMaterial.SetVector("_GUIOffset", guiOffset);
            _lineMaterial.SetFloat("_LineWidth", lineWidth);
            _lineMaterial.SetFloat("_Alpha", alpha);
            _lineMaterial.SetColor("_EdgeColor", edgeColor ?? new Color(0f, 1f, 0.5f, 0.9f));
            _lineMaterial.SetColor("_SelectedEdgeColor", new Color(0f, 1f, 1f, 1f)); // シアン
            
            // IMGUI描画コンテキストで描画
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, windowSize.x, windowSize.y, 0);
            
            _lineMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, _lineCount);
            
            GL.PopMatrix();
        }

        private void ReleaseBuffers()
        {
            _positionBuffer?.Release();
            _lineBuffer?.Release();
            _faceVertexIndexBuffer?.Release();
            _faceVertexOffsetBuffer?.Release();
            _faceVertexCountBuffer?.Release();
            _screenPositionBuffer?.Release();
            _vertexVisibilityBuffer?.Release();
            _faceVisibilityBuffer?.Release();
            _lineVisibilityBuffer?.Release();
            _selectionBuffer?.Release();
            _lineSelectionBuffer?.Release();
            
            _positionBuffer = null;
            _lineBuffer = null;
            _faceVertexIndexBuffer = null;
            _faceVertexOffsetBuffer = null;
            _faceVertexCountBuffer = null;
            _screenPositionBuffer = null;
            _vertexVisibilityBuffer = null;
            _faceVisibilityBuffer = null;
            _lineVisibilityBuffer = null;
            _selectionBuffer = null;
            _lineSelectionBuffer = null;
            
            _vertexCount = 0;
            _faceCount = 0;
            _lineCount = 0;
            _cachedMeshData = null;
        }

        public void InvalidateBuffers()
        {
            _cachedMeshData = null;
        }

        public void Dispose()
        {
            ReleaseBuffers();
            
            if (_pointMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_pointMaterial);
            }
            if (_lineMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_lineMaterial);
            }
            
            _pointMaterial = null;
            _lineMaterial = null;
            _initialized = false;
        }
    }
}
