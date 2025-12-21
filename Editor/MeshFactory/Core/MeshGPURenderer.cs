// Editor/MeshFactory/Core/MeshGPURenderer.cs
// IMGUI対応版 GPU描画レンダラー
// v2.4: 面ホバーの深度対応追加

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    /// <summary>
    /// 可視性情報を提供するインターフェース
    /// 将来GPU一本化時に差し替え可能
    /// </summary>
    public interface IVisibilityProvider
    {
        bool IsVertexVisible(int index);
        bool IsLineVisible(int index);
        bool IsFaceVisible(int index);
        
        // バッチ取得（パフォーマンス用）
        float[] GetVertexVisibility();
        float[] GetLineVisibility();
        float[] GetFaceVisibility();
    }

    /// <summary>
    /// GPUヒットテスト結果
    /// </summary>
    public struct GPUHitTestResult
    {
        public int NearestVertexIndex;
        public float NearestVertexDistance;
        
        public int NearestLineIndex;
        public float NearestLineDistance;
        
        public int[] HitFaceIndices;
        public float[] HitFaceDepths;  // 各ヒット面の深度
        
        public bool HasVertexHit(float radius) => NearestVertexIndex >= 0 && NearestVertexDistance < radius;
        public bool HasLineHit(float distance) => NearestLineIndex >= 0 && NearestLineDistance < distance;
        public bool HasFaceHit => HitFaceIndices != null && HitFaceIndices.Length > 0;
        
        /// <summary>
        /// 最も手前（深度が小さい）の面インデックスを取得
        /// </summary>
        public int GetNearestFaceIndex()
        {
            if (HitFaceIndices == null || HitFaceIndices.Length == 0)
                return -1;
            
            if (HitFaceDepths == null || HitFaceDepths.Length != HitFaceIndices.Length)
                return HitFaceIndices[0];  // 深度情報がない場合は最初の面
            
            int nearestIndex = 0;
            float nearestDepth = HitFaceDepths[0];
            
            for (int i = 1; i < HitFaceDepths.Length; i++)
            {
                if (HitFaceDepths[i] < nearestDepth)
                {
                    nearestDepth = HitFaceDepths[i];
                    nearestIndex = i;
                }
            }
            
            return HitFaceIndices[nearestIndex];
        }
    }

    public class MeshGPURenderer : IDisposable, IVisibilityProvider
    {
        // シェーダー・マテリアル
        private ComputeShader _computeShader;
        private int _kernelClear, _kernelScreenPos, _kernelFaceVisibility, _kernelLineVisibility;
        private int _kernelVertexHit, _kernelLineHit, _kernelFaceHit;
        private Material _pointMaterial, _lineMaterial;
        
        // バッファ（既存）
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
        
        // ヒットテスト用バッファ
        private ComputeBuffer _vertexHitDistanceBuffer;
        private ComputeBuffer _lineHitDistanceBuffer;
        private ComputeBuffer _faceHitBuffer;
        private ComputeBuffer _faceHitDepthBuffer;  // 面の深度バッファ
        
        // CPU側読み取り用配列
        private float[] _vertexHitDistanceData;
        private float[] _lineHitDistanceData;
        private uint[] _faceHitData;
        private float[] _faceHitDepthData;  // 面の深度データ
        
        // 可視性キャッシュ（CPU読み戻し用）
        private float[] _vertexVisibilityCache;
        private float[] _lineVisibilityCache;
        private float[] _faceVisibilityCache;
        private bool _visibilityCacheDirty = true;
        
        private uint[] _selectionData;
        private uint[] _lineSelectionData;
        private bool _initialized;
        private bool _hitTestAvailable;
        private int _vertexCount, _faceCount, _lineCount;
        private MeshData _cachedMeshData;

        // ホバー状態
        private int _hoverVertexIndex = -1;
        private int _hoverLineIndex = -1;
        private int _hoverFaceIndex = -1;

        public bool IsAvailable => _initialized && SystemInfo.supportsComputeShaders;
        public bool HitTestAvailable => _hitTestAvailable;
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

            // ヒットテスト用カーネルの初期化（オプション）
            try
            {
                _kernelVertexHit = _computeShader.FindKernel("ComputeVertexHitTest");
                _kernelLineHit = _computeShader.FindKernel("ComputeLineHitTest");
                _kernelFaceHit = _computeShader.FindKernel("ComputeFaceHitTest");
                _hitTestAvailable = true;
            }
            catch (Exception)
            {
                _hitTestAvailable = false;
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

            // ヒットテスト用バッファ
            if (_hitTestAvailable)
            {
                _vertexHitDistanceBuffer = new ComputeBuffer(_vertexCount, sizeof(float));
                _vertexHitDistanceData = new float[_vertexCount];
                
                if (_lineCount > 0)
                {
                    _lineHitDistanceBuffer = new ComputeBuffer(_lineCount, sizeof(float));
                    _lineHitDistanceData = new float[_lineCount];
                }
                
                if (_faceCount > 0)
                {
                    _faceHitBuffer = new ComputeBuffer(_faceCount, sizeof(uint));
                    _faceHitData = new uint[_faceCount];
                    _faceHitDepthBuffer = new ComputeBuffer(_faceCount, sizeof(float));
                    _faceHitDepthData = new float[_faceCount];
                }
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

        /// <summary>
        /// ホバー状態を設定
        /// </summary>
        /// <param name="vertexIndex">ホバー中の頂点インデックス（-1で無効）</param>
        /// <param name="lineIndex">ホバー中の線分インデックス（-1で無効）</param>
        public void SetHoverState(int vertexIndex, int lineIndex, int faceIndex = -1)
        {
            _hoverVertexIndex = vertexIndex;
            _hoverLineIndex = lineIndex;
            _hoverFaceIndex = faceIndex;
        }

        /// <summary>
        /// ホバー状態をクリア
        /// </summary>
        public void ClearHoverState()
        {
            _hoverVertexIndex = -1;
            _hoverLineIndex = -1;
            _hoverFaceIndex = -1;
        }

        // DispatchCompute パラメータキャッシュ（DispatchHitTest用）
        private Matrix4x4 _cachedMVP;
        private Rect _cachedPreviewRect;
        private Vector2 _cachedWindowSize;
        private bool _computeParamsCached = false;

        public void DispatchCompute(Matrix4x4 mvp, Rect previewRect, Vector2 windowSize)
        {
            if (!_initialized || _vertexCount == 0) return;

            // パラメータをキャッシュ（DispatchHitTest用）
            _cachedMVP = mvp;
            _cachedPreviewRect = previewRect;
            _cachedWindowSize = windowSize;
            _computeParamsCached = true;

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
                SetAllVisible();
            }
            
            // 可視性キャッシュを無効化（次回取得時に読み戻し）
            _visibilityCacheDirty = true;
        }

        // ================================================================
        // IVisibilityProvider 実装
        // ================================================================

        /// <summary>
        /// 可視性キャッシュを更新（GPUバッファから読み戻し）
        /// </summary>
        private void UpdateVisibilityCache()
        {
            if (!_visibilityCacheDirty || !_initialized) return;

            // 頂点可視性
            if (_vertexVisibilityBuffer != null && _vertexCount > 0)
            {
                if (_vertexVisibilityCache == null || _vertexVisibilityCache.Length != _vertexCount)
                    _vertexVisibilityCache = new float[_vertexCount];
                _vertexVisibilityBuffer.GetData(_vertexVisibilityCache);
            }

            // 線分可視性
            if (_lineVisibilityBuffer != null && _lineCount > 0)
            {
                if (_lineVisibilityCache == null || _lineVisibilityCache.Length != _lineCount)
                    _lineVisibilityCache = new float[_lineCount];
                _lineVisibilityBuffer.GetData(_lineVisibilityCache);
            }

            // 面可視性
            if (_faceVisibilityBuffer != null && _faceCount > 0)
            {
                if (_faceVisibilityCache == null || _faceVisibilityCache.Length != _faceCount)
                    _faceVisibilityCache = new float[_faceCount];
                _faceVisibilityBuffer.GetData(_faceVisibilityCache);
            }

            _visibilityCacheDirty = false;
        }

        /// <summary>
        /// 頂点が可視か
        /// </summary>
        public bool IsVertexVisible(int index)
        {
            if (!CullingEnabled) return true;
            UpdateVisibilityCache();
            if (_vertexVisibilityCache == null || index < 0 || index >= _vertexVisibilityCache.Length)
                return true; // 範囲外は可視として扱う
            return _vertexVisibilityCache[index] >= 0.5f;
        }

        /// <summary>
        /// 線分が可視か
        /// </summary>
        public bool IsLineVisible(int index)
        {
            if (!CullingEnabled) return true;
            UpdateVisibilityCache();
            if (_lineVisibilityCache == null || index < 0 || index >= _lineVisibilityCache.Length)
                return true;
            return _lineVisibilityCache[index] >= 0.5f;
        }

        /// <summary>
        /// 面が可視か
        /// </summary>
        public bool IsFaceVisible(int index)
        {
            if (!CullingEnabled) return true;
            UpdateVisibilityCache();
            if (_faceVisibilityCache == null || index < 0 || index >= _faceVisibilityCache.Length)
                return true;
            return _faceVisibilityCache[index] >= 0.5f;
        }

        /// <summary>
        /// 頂点可視性配列を取得（コピー）
        /// </summary>
        public float[] GetVertexVisibility()
        {
            if (!CullingEnabled || !_initialized)
                return null;
            UpdateVisibilityCache();
            return _vertexVisibilityCache != null ? (float[])_vertexVisibilityCache.Clone() : null;
        }

        /// <summary>
        /// 線分可視性配列を取得（コピー）
        /// </summary>
        public float[] GetLineVisibility()
        {
            if (!CullingEnabled || !_initialized)
                return null;
            UpdateVisibilityCache();
            return _lineVisibilityCache != null ? (float[])_lineVisibilityCache.Clone() : null;
        }

        /// <summary>
        /// 面可視性配列を取得（コピー）
        /// </summary>
        public float[] GetFaceVisibility()
        {
            if (!CullingEnabled || !_initialized)
                return null;
            UpdateVisibilityCache();
            return _faceVisibilityCache != null ? (float[])_faceVisibilityCache.Clone() : null;
        }

        /// <summary>
        /// GPUでヒットテストを実行
        /// 内部で可視性計算も行う（前回のDispatchComputeのパラメータを使用）
        /// 
        /// 【座標系】
        /// GPU描画は adjustedRect を使用：
        ///   adjustedRect.y = rect.y + tabHeight
        ///   adjustedRect.height = rect.height - tabHeight
        /// 
        /// このメソッドは内部でマウス座標を変換します。
        /// 呼び出し側は rect 座標系のマウス座標をそのまま渡してください。
        /// 
        /// tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y
        /// 
        /// 【注意】事前に一度はDispatchComputeが呼ばれている必要があります。
        /// </summary>
        public GPUHitTestResult DispatchHitTest(Vector2 mousePos, Rect rect, float tabHeight)
        {
            var result = new GPUHitTestResult
            {
                NearestVertexIndex = -1,
                NearestVertexDistance = float.MaxValue,
                NearestLineIndex = -1,
                NearestLineDistance = float.MaxValue,
                HitFaceIndices = null
            };

            if (!_hitTestAvailable || !_initialized || _vertexCount == 0)
                return result;

            // キャッシュされたパラメータがない場合は警告
            if (!_computeParamsCached)
            {
                Debug.LogWarning("[MeshGPURenderer] DispatchHitTest called before DispatchCompute. Visibility may be incorrect.");
            }
            else
            {
                // キャッシュされたパラメータで可視性を再計算
                Rect adjustedRect = new Rect(rect.x, rect.y + tabHeight, rect.width, rect.height - tabHeight);
                DispatchCompute(_cachedMVP, adjustedRect, _cachedWindowSize);
            }

            // マウス座標を adjustedRect 座標系に変換
            float ratioY = mousePos.y / rect.height;
            float adjustedMouseY = tabHeight + ratioY * (rect.height - tabHeight);
            Vector2 adjustedMousePos = new Vector2(mousePos.x, adjustedMouseY);

            _computeShader.SetVector("_MousePosition", new Vector4(adjustedMousePos.x, adjustedMousePos.y, 0, 0));

            int threadGroups(int count) => Mathf.CeilToInt(count / 64f);

            // 1. 頂点ヒット距離計算
            SetBuffer(_kernelVertexHit, "_ScreenPositionBuffer", _screenPositionBuffer);
            SetBuffer(_kernelVertexHit, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            SetBuffer(_kernelVertexHit, "_VertexHitDistanceBuffer", _vertexHitDistanceBuffer);
            _computeShader.Dispatch(_kernelVertexHit, threadGroups(_vertexCount), 1, 1);

            // 2. 線分ヒット距離計算
            if (_lineCount > 0 && _lineHitDistanceBuffer != null)
            {
                SetBuffer(_kernelLineHit, "_ScreenPositionBuffer", _screenPositionBuffer);
                SetBuffer(_kernelLineHit, "_LineBuffer", _lineBuffer);
                SetBuffer(_kernelLineHit, "_LineVisibilityBuffer", _lineVisibilityBuffer);
                SetBuffer(_kernelLineHit, "_LineHitDistanceBuffer", _lineHitDistanceBuffer);
                _computeShader.Dispatch(_kernelLineHit, threadGroups(_lineCount), 1, 1);
            }

            // 3. 面ヒットテスト
            if (_faceCount > 0 && _faceHitBuffer != null)
            {
                SetBuffer(_kernelFaceHit, "_ScreenPositionBuffer", _screenPositionBuffer);
                SetBuffer(_kernelFaceHit, "_FaceVertexIndexBuffer", _faceVertexIndexBuffer);
                SetBuffer(_kernelFaceHit, "_FaceVertexOffsetBuffer", _faceVertexOffsetBuffer);
                SetBuffer(_kernelFaceHit, "_FaceVertexCountBuffer", _faceVertexCountBuffer);
                SetBuffer(_kernelFaceHit, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
                SetBuffer(_kernelFaceHit, "_FaceHitBuffer", _faceHitBuffer);
                SetBuffer(_kernelFaceHit, "_FaceHitDepthBuffer", _faceHitDepthBuffer);
                _computeShader.Dispatch(_kernelFaceHit, threadGroups(_faceCount), 1, 1);
            }

            return ReadHitTestResults();
        }

        private GPUHitTestResult ReadHitTestResults()
        {
            var result = new GPUHitTestResult
            {
                NearestVertexIndex = -1,
                NearestVertexDistance = float.MaxValue,
                NearestLineIndex = -1,
                NearestLineDistance = float.MaxValue
            };

            if (_vertexHitDistanceBuffer != null && _vertexHitDistanceData != null)
            {
                _vertexHitDistanceBuffer.GetData(_vertexHitDistanceData);
                
                for (int i = 0; i < _vertexHitDistanceData.Length; i++)
                {
                    if (_vertexHitDistanceData[i] < result.NearestVertexDistance)
                    {
                        result.NearestVertexDistance = _vertexHitDistanceData[i];
                        result.NearestVertexIndex = i;
                    }
                }
            }

            if (_lineHitDistanceBuffer != null && _lineHitDistanceData != null)
            {
                _lineHitDistanceBuffer.GetData(_lineHitDistanceData);
                
                for (int i = 0; i < _lineHitDistanceData.Length; i++)
                {
                    if (_lineHitDistanceData[i] < result.NearestLineDistance)
                    {
                        result.NearestLineDistance = _lineHitDistanceData[i];
                        result.NearestLineIndex = i;
                    }
                }
            }

            if (_faceHitBuffer != null && _faceHitData != null)
            {
                _faceHitBuffer.GetData(_faceHitData);
                
                // 深度も読み込み
                if (_faceHitDepthBuffer != null && _faceHitDepthData != null)
                {
                    _faceHitDepthBuffer.GetData(_faceHitDepthData);
                }
                
                var hitFaces = new List<int>();
                var hitDepths = new List<float>();
                for (int i = 0; i < _faceHitData.Length; i++)
                {
                    if (_faceHitData[i] != 0)
                    {
                        hitFaces.Add(i);
                        if (_faceHitDepthData != null && i < _faceHitDepthData.Length)
                        {
                            hitDepths.Add(_faceHitDepthData[i]);
                        }
                    }
                }
                result.HitFaceIndices = hitFaces.ToArray();
                result.HitFaceDepths = hitDepths.ToArray();
            }

            return result;
        }

        public float[] GetVertexHitDistances()
        {
            if (_vertexHitDistanceData == null) return null;
            return (float[])_vertexHitDistanceData.Clone();
        }

        public float[] GetLineHitDistances()
        {
            if (_lineHitDistanceData == null) return null;
            return (float[])_lineHitDistanceData.Clone();
        }

        public uint[] GetFaceHitData()
        {
            if (_faceHitData == null) return null;
            return (uint[])_faceHitData.Clone();
        }

        /// <summary>
        /// ホバー状態を更新
        /// ヒットテスト結果に基づいて、ホバー中の頂点/線分/面を設定
        /// 優先順位: 頂点 > 線分 > 面
        /// </summary>
        /// <param name="hitResult">DispatchHitTestの結果</param>
        /// <param name="vertexRadius">頂点ヒット判定の半径（ピクセル）</param>
        /// <param name="lineDistance">線分ヒット判定の距離（ピクセル）</param>
        public void UpdateHoverState(GPUHitTestResult hitResult, float vertexRadius = 10f, float lineDistance = 5f)
        {
            // 頂点ホバー（最優先）
            if (hitResult.HasVertexHit(vertexRadius))
            {
                _hoverVertexIndex = hitResult.NearestVertexIndex;
                _hoverLineIndex = -1;
                _hoverFaceIndex = -1;
            }
            // 線分ホバー
            else if (hitResult.HasLineHit(lineDistance))
            {
                _hoverVertexIndex = -1;
                _hoverLineIndex = hitResult.NearestLineIndex;
                _hoverFaceIndex = -1;
            }
            // 面ホバー（最も優先度が低い）
            else if (hitResult.HasFaceHit)
            {
                _hoverVertexIndex = -1;
                _hoverLineIndex = -1;
                // 深度を考慮して最も手前の面を選択
                _hoverFaceIndex = hitResult.GetNearestFaceIndex();
            }
            else
            {
                _hoverVertexIndex = -1;
                _hoverLineIndex = -1;
                _hoverFaceIndex = -1;
            }
        }

        /// <summary>
        /// 現在のホバー面インデックス
        /// </summary>
        public int HoverFaceIndex => _hoverFaceIndex;

        private void SetAllVisible()
        {
            // 頂点可視性
            if (_vertexVisibilityData == null || _vertexVisibilityData.Length != _vertexCount)
                _vertexVisibilityData = new float[_vertexCount];

            for (int i = 0; i < _vertexCount; i++)
                _vertexVisibilityData[i] = 1.0f;

            _vertexVisibilityBuffer?.SetData(_vertexVisibilityData);

            // 線分可視性
            if (_lineVisibilityData == null || _lineVisibilityData.Length != _lineCount)
                _lineVisibilityData = new float[_lineCount];

            for (int i = 0; i < _lineCount; i++)
                _lineVisibilityData[i] = 1.0f;

            _lineVisibilityBuffer?.SetData(_lineVisibilityData);

            // 面可視性（カリング無効時のヒットテスト用）
            if (_faceCount > 0)
            {
                if (_faceVisibilityData == null || _faceVisibilityData.Length != _faceCount)
                    _faceVisibilityData = new float[_faceCount];

                for (int i = 0; i < _faceCount; i++)
                    _faceVisibilityData[i] = 1.0f;

                _faceVisibilityBuffer?.SetData(_faceVisibilityData);
            }
        }

        private float[] _vertexVisibilityData;
        private float[] _lineVisibilityData;
        private float[] _faceVisibilityData;

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
            
            _pointMaterial.SetBuffer("_ScreenPositionBuffer", _screenPositionBuffer);
            _pointMaterial.SetBuffer("_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            _pointMaterial.SetBuffer("_SelectionBuffer", _selectionBuffer);
            _pointMaterial.SetVector("_MeshFactoryScreenSize", windowSize);
            _pointMaterial.SetVector("_PreviewRect", new Vector4(previewRect.x, previewRect.y, previewRect.width, previewRect.height));
            _pointMaterial.SetVector("_GUIOffset", guiOffset);
            _pointMaterial.SetFloat("_PointSize", pointSize);
            _pointMaterial.SetFloat("_Alpha", alpha);
            _pointMaterial.SetInt("_HoverVertexIndex", _hoverVertexIndex);
            
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, windowSize.x, windowSize.y, 0);
            
            _pointMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, _vertexCount);
            
            GL.PopMatrix();
        }

        public void DrawLines(Rect previewRect, Vector2 windowSize, Vector2 guiOffset, float lineWidth = 2f, float alpha = 1f, Color? edgeColor = null)
        {
            if (!_initialized || _lineCount == 0 || _lineMaterial == null) return;
            
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
            _lineMaterial.SetColor("_SelectedEdgeColor", new Color(0f, 1f, 1f, 1f));
            _lineMaterial.SetInt("_HoverLineIndex", _hoverLineIndex);
            
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, windowSize.x, windowSize.y, 0);
            
            _lineMaterial.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6, _lineCount);
            
            GL.PopMatrix();
        }

        // GL描画用マテリアル（面ホバー用）
        private static Material _glMaterial;

        /// <summary>
        /// ホバー中の面を半透明で描画
        /// DrawPoints/DrawLinesの前に呼び出すこと（下に描画されるように）
        /// </summary>
        public void DrawHoverFace(Vector2 windowSize, MeshData meshData, Color? hoverColor = null)
        {
            if (!_initialized || _hoverFaceIndex < 0 || meshData == null) return;
            if (_hoverFaceIndex >= meshData.FaceCount) return;

            // スクリーン座標を読み戻し
            var screenPositions = new Vector4[_vertexCount];
            _screenPositionBuffer.GetData(screenPositions);

            var face = meshData.Faces[_hoverFaceIndex];
            if (face.VertexCount < 3) return;

            // GL描画用マテリアルの初期化
            if (_glMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                _glMaterial = new Material(shader);
                _glMaterial.hideFlags = HideFlags.HideAndDontSave;
                _glMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _glMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _glMaterial.SetInt("_Cull", (int)CullMode.Off);
                _glMaterial.SetInt("_ZWrite", 0);
            }

            Color color = hoverColor ?? new Color(0f, 0.8f, 1f, 0.3f);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, windowSize.x, windowSize.y, 0);
            _glMaterial.SetPass(0);

            // 半透明塗りつぶし
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);

            // 三角形分割（ファンで描画）
            var indices = face.VertexIndices;
            Vector4 p0 = screenPositions[indices[0]];
            
            for (int i = 1; i < indices.Count - 1; i++)
            {
                Vector4 p1 = screenPositions[indices[i]];
                Vector4 p2 = screenPositions[indices[i + 1]];

                // 全頂点が有効な場合のみ描画
                if (p0.w > 0.5f && p1.w > 0.5f && p2.w > 0.5f)
                {
                    GL.Vertex3(p0.x, p0.y, 0);
                    GL.Vertex3(p1.x, p1.y, 0);
                    GL.Vertex3(p2.x, p2.y, 0);
                }
            }

            GL.End();
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
            
            _vertexHitDistanceBuffer?.Release();
            _lineHitDistanceBuffer?.Release();
            _faceHitBuffer?.Release();
            _faceHitDepthBuffer?.Release();
            
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
            
            _vertexHitDistanceBuffer = null;
            _lineHitDistanceBuffer = null;
            _faceHitBuffer = null;
            _faceHitDepthBuffer = null;
            
            _vertexHitDistanceData = null;
            _lineHitDistanceData = null;
            _faceHitData = null;
            _faceHitDepthData = null;
            
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
