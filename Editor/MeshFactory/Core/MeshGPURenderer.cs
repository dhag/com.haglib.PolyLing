// Editor/MeshFactory/Core/MeshGPURenderer.cs
// IMGUI対応版 GPU描画レンダラー
// v2.5: 頂点・線分・面の深度対応追加、ホバーデバッグ機能追加

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
        public float NearestVertexDepth;       // 頂点の深度
        
        public int NearestLineIndex;
        public float NearestLineDistance;
        public float NearestLineDepth;         // 線分の深度（補間）
        
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
        private ComputeBuffer _vertexHitDepthBuffer;     // 頂点の深度バッファ
        private ComputeBuffer _lineHitDistanceBuffer;
        private ComputeBuffer _lineHitDepthBuffer;       // 線分の深度バッファ
        private ComputeBuffer _faceHitBuffer;
        private ComputeBuffer _faceHitDepthBuffer;  // 面の深度バッファ
        
        // CPU側読み取り用配列
        private float[] _vertexHitDistanceData;
        private float[] _vertexHitDepthData;             // 頂点の深度データ
        private float[] _lineHitDistanceData;
        private float[] _lineHitDepthData;               // 線分の深度データ
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
        private MeshObject _cachedMeshObject;

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

        public void UpdateBuffers(MeshObject meshObject, MeshEdgeCache edgeCache)
        {
            if (!_initialized || meshObject == null || edgeCache == null) return;
            
            // キャッシュが有効な場合はスキップ
            if (_cachedMeshObject == meshObject && _vertexCount == meshObject.VertexCount) return;

            _cachedMeshObject = meshObject;
            ReleaseBuffers();
            
            _vertexCount = meshObject.VertexCount;
            _faceCount = meshObject.FaceCount;
            
            if (_vertexCount == 0) return;

            // エッジキャッシュを更新
            edgeCache.Update(meshObject, force: true);
            _lineCount = edgeCache.LineCount;

            // 頂点位置バッファ
            var positions = new Vector3[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = meshObject.Vertices[i].Position;
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
            BuildFaceBuffers(meshObject);

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
                _vertexHitDepthBuffer = new ComputeBuffer(_vertexCount, sizeof(float));
                _vertexHitDepthData = new float[_vertexCount];
                
                if (_lineCount > 0)
                {
                    _lineHitDistanceBuffer = new ComputeBuffer(_lineCount, sizeof(float));
                    _lineHitDistanceData = new float[_lineCount];
                    _lineHitDepthBuffer = new ComputeBuffer(_lineCount, sizeof(float));
                    _lineHitDepthData = new float[_lineCount];
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

        private void BuildFaceBuffers(MeshObject meshObject)
        {
            if (_faceCount == 0) return;
            
            var indices = new List<int>();
            var offsets = new int[_faceCount];
            var counts = new int[_faceCount];
            
            int offset = 0;
            for (int f = 0; f < _faceCount; f++)
            {
                var face = meshObject.Faces[f];
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
            //この計算は必須。必須必須必須必須必須
            float ratioY = mousePos.y / rect.height;
            float adjustedMouseY = tabHeight + ratioY * (rect.height - tabHeight);
            Vector2 adjustedMousePos = new Vector2(mousePos.x, adjustedMouseY);
            
            _computeShader.SetVector("_MousePosition", new Vector4(adjustedMousePos.x, adjustedMousePos.y, 0, 0));

            //臨時。 検証用にキャッシュ
            _cachedAdjustedMousePos = adjustedMousePos;


            //これではだめ。
            //_computeShader.SetVector("_MousePosition", new Vector4(mousePos.x, mousePos.y, 0, 0));

            int threadGroups(int count) => Mathf.CeilToInt(count / 64f);

            // 1. 頂点ヒット距離計算
            SetBuffer(_kernelVertexHit, "_ScreenPositionBuffer", _screenPositionBuffer);
            SetBuffer(_kernelVertexHit, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            SetBuffer(_kernelVertexHit, "_VertexHitDistanceBuffer", _vertexHitDistanceBuffer);
            SetBuffer(_kernelVertexHit, "_VertexHitDepthBuffer", _vertexHitDepthBuffer);
            _computeShader.Dispatch(_kernelVertexHit, threadGroups(_vertexCount), 1, 1);//コンピュートシェーダによる頂点ヒットテスト

            // 2. 線分ヒット距離計算
            if (_lineCount > 0 && _lineHitDistanceBuffer != null)
            {
                SetBuffer(_kernelLineHit, "_ScreenPositionBuffer", _screenPositionBuffer);
                SetBuffer(_kernelLineHit, "_LineBuffer", _lineBuffer);
                SetBuffer(_kernelLineHit, "_LineVisibilityBuffer", _lineVisibilityBuffer);
                SetBuffer(_kernelLineHit, "_LineHitDistanceBuffer", _lineHitDistanceBuffer);
                SetBuffer(_kernelLineHit, "_LineHitDepthBuffer", _lineHitDepthBuffer);
                _computeShader.Dispatch(_kernelLineHit, threadGroups(_lineCount), 1, 1);//コンピュートシェーダによる線分ヒットテスト
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
                _computeShader.Dispatch(_kernelFaceHit, threadGroups(_faceCount), 1, 1);//コンピュートシェーダによる面ヒットテスト
            }

            return ReadHitTestResults();
        }

        private GPUHitTestResult ReadHitTestResults()
        {
            var result = new GPUHitTestResult
            {
                NearestVertexIndex = -1,
                NearestVertexDistance = float.MaxValue,
                NearestVertexDepth = float.MaxValue,
                NearestLineIndex = -1,
                NearestLineDistance = float.MaxValue,
                NearestLineDepth = float.MaxValue
            };

            // 距離がほぼ同じとみなす閾値（ピクセル）
            const float distanceEpsilon = 0.5f;

            // 頂点ヒット（深度も読み込み）
            if (_vertexHitDistanceBuffer != null && _vertexHitDistanceData != null)
            {
                _vertexHitDistanceBuffer.GetData(_vertexHitDistanceData);
                
                if (_vertexHitDepthBuffer != null && _vertexHitDepthData != null)
                {
                    _vertexHitDepthBuffer.GetData(_vertexHitDepthData);
                }
                
                for (int i = 0; i < _vertexHitDistanceData.Length; i++)
                {
                    float dist = _vertexHitDistanceData[i];
                    float depth = (_vertexHitDepthData != null && i < _vertexHitDepthData.Length) 
                        ? _vertexHitDepthData[i] : float.MaxValue;
                    
                    // 距離が近い場合（深度も考慮）
                    if (dist < result.NearestVertexDistance)
                    {
                        result.NearestVertexDistance = dist;
                        result.NearestVertexDepth = depth;
                        result.NearestVertexIndex = i;
                    }
                    // 距離がほぼ同じ場合は深度で比較
                    else if (Mathf.Abs(dist - result.NearestVertexDistance) <= distanceEpsilon && 
                             depth < result.NearestVertexDepth)
                    {
                        result.NearestVertexDistance = dist;
                        result.NearestVertexDepth = depth;
                        result.NearestVertexIndex = i;
                    }
                }
            }

            // 線分ヒット（深度も読み込み）
            if (_lineHitDistanceBuffer != null && _lineHitDistanceData != null)
            {
                _lineHitDistanceBuffer.GetData(_lineHitDistanceData);
                
                if (_lineHitDepthBuffer != null && _lineHitDepthData != null)
                {
                    _lineHitDepthBuffer.GetData(_lineHitDepthData);
                }
                
                for (int i = 0; i < _lineHitDistanceData.Length; i++)
                {
                    float dist = _lineHitDistanceData[i];
                    float depth = (_lineHitDepthData != null && i < _lineHitDepthData.Length) 
                        ? _lineHitDepthData[i] : float.MaxValue;
                    
                    // 距離が近い場合（深度も考慮）
                    if (dist < result.NearestLineDistance)
                    {
                        result.NearestLineDistance = dist;
                        result.NearestLineDepth = depth;
                        result.NearestLineIndex = i;
                    }
                    // 距離がほぼ同じ場合は深度で比較
                    else if (Mathf.Abs(dist - result.NearestLineDistance) <= distanceEpsilon && 
                             depth < result.NearestLineDepth)
                    {
                        result.NearestLineDistance = dist;
                        result.NearestLineDepth = depth;
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
        /// ホバーデバッグを有効化（線分インデックス検証用）
        /// </summary>
        public bool EnableHoverDebug { get; set; } = true;  // デバッグ中は有効
        
        // デバッグ用：最後のマウス位置
        private Vector2 _lastMousePosForDebug;
        
        // 前回のホバー状態（変化検出用）
        private int _prevHoverVertexIndex = -1;
        private int _prevHoverLineIndex = -1;
        private int _prevHoverFaceIndex = -1;
        
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
            int newHoverVertex = -1;
            int newHoverLine = -1;
            int newHoverFace = -1;
            
            // 頂点ホバー（最優先）
            if (hitResult.HasVertexHit(vertexRadius))
            {
                newHoverVertex = hitResult.NearestVertexIndex;
            }
            // 線分ホバー
            else if (hitResult.HasLineHit(lineDistance))
            {
                newHoverLine = hitResult.NearestLineIndex;
            }
            // 面ホバー（最も優先度が低い）
            else if (hitResult.HasFaceHit)
            {
                // 深度を考慮して最も手前の面を選択
                newHoverFace = hitResult.GetNearestFaceIndex();
            }
            /*こっちはもう問題解消されたが、いつでも確認できるよう残しておく
            // デバッグ：状態変化をログ出力
            
            if (EnableHoverDebug)
            {
                if (newHoverVertex != _prevHoverVertexIndex)
                {
                    if (newHoverVertex >= 0)
                        Debug.Log($"[HoverDebug] Vertex HOVER: idx={newHoverVertex}, dist={hitResult.NearestVertexDistance:F2}, depth={hitResult.NearestVertexDepth:F4}");
                    else if (_prevHoverVertexIndex >= 0)
                        Debug.Log($"[HoverDebug] Vertex UNHOVER: was idx={_prevHoverVertexIndex}");
                }
                
                if (newHoverLine != _prevHoverLineIndex)
                {
                    if (newHoverLine >= 0)
                        Debug.Log($"[HoverDebug] Line HOVER: idx={newHoverLine}, dist={hitResult.NearestLineDistance:F2}, depth={hitResult.NearestLineDepth:F4}");
                    else if (_prevHoverLineIndex >= 0)
                        Debug.Log($"[HoverDebug] Line UNHOVER: was idx={_prevHoverLineIndex}");
                }
                
                if (newHoverFace != _prevHoverFaceIndex)
                {
                    if (newHoverFace >= 0)
                        Debug.Log($"[HoverDebug] Face HOVER: idx={newHoverFace}");
                    else if (_prevHoverFaceIndex >= 0)
                        Debug.Log($"[HoverDebug] Face UNHOVER: was idx={_prevHoverFaceIndex}");
                }
                /*
                // ヒットテスト結果の詳細（ホバー対象がない場合も）
                if (newHoverVertex < 0 && newHoverLine < 0 && newHoverFace < 0)
                {
                    if (_prevHoverVertexIndex >= 0 || _prevHoverLineIndex >= 0 || _prevHoverFaceIndex >= 0)
                    {
                        Debug.Log($"[HoverDebug] NO HOVER - NearestVertex: idx={hitResult.NearestVertexIndex}, dist={hitResult.NearestVertexDistance:F2} (threshold={vertexRadius}), NearestLine: idx={hitResult.NearestLineIndex}, dist={hitResult.NearestLineDistance:F2} (threshold={lineDistance})");
                    }
                }
                
            }
            */

            _prevHoverVertexIndex = newHoverVertex;
            _prevHoverLineIndex = newHoverLine;
            _prevHoverFaceIndex = newHoverFace;
            
            _hoverVertexIndex = newHoverVertex;
            _hoverLineIndex = newHoverLine;
            _hoverFaceIndex = newHoverFace;
        }

        /// <summary>
        /// ホバー状態を更新（デバッグ検証付き）
        /// 線分インデックスの稀な間違い：未解決　のデバッグ用
        /// </summary>
        public void UpdateHoverState(GPUHitTestResult hitResult, float vertexRadius, float lineDistance, 
            Vector2 mousePos, MeshEdgeCache edgeCache)
        {
            _lastMousePosForDebug = mousePos;
            
            // 通常の更新
            UpdateHoverState(hitResult, vertexRadius, lineDistance);

            // [HoverDebug]デバッグ検証
            if (EnableHoverDebug && _hoverLineIndex >= 0 && edgeCache != null)
            {
                ValidateLineHover(hitResult, mousePos, edgeCache);
            }
        }


        // ヒットテスト用の変換済みマウス座標（検証用）
        private Vector2 _cachedAdjustedMousePos;
        /// <summary>
        /// 線分ホバーの検証（デバッグ用）
        /// GPUの結果とCPU再計算を比較
        /// </summary>
        private void ValidateLineHover(GPUHitTestResult hitResult, Vector2 mousePos, MeshEdgeCache edgeCache)
        {
            int lineIndex = hitResult.NearestLineIndex;
            float gpuDistance = hitResult.NearestLineDistance;
            
            if (lineIndex < 0 || lineIndex >= edgeCache.LineCount)
            {
                Debug.LogError($"[HoverDebug] Invalid line index: {lineIndex}, LineCount: {edgeCache.LineCount}");
                return;
            }
            
            var lineData = edgeCache.Lines[lineIndex];
            int v1 = lineData.V1;
            int v2 = lineData.V2;
            
            // GPUバッファからスクリーン座標を読み取り
            if (_screenPositionData == null || _screenPositionData.Length != _vertexCount)
            {
                _screenPositionData = new Vector4[_vertexCount];
            }
            _screenPositionBuffer?.GetData(_screenPositionData);
            
            if (v1 < 0 || v1 >= _vertexCount || v2 < 0 || v2 >= _vertexCount)
            {
                Debug.LogError($"[HoverDebug] Invalid vertex indices: v1={v1}, v2={v2}, VertexCount={_vertexCount}");
                return;
            }
            
            Vector4 p1Screen = _screenPositionData[v1];
            Vector4 p2Screen = _screenPositionData[v2];

            // CPU側で距離を再計算。この式誤り。
            //float cpuDistance = DistanceToLineSegmentCPU(mousePos, 
            //     new Vector2(p1Screen.x, p1Screen.y), 
            //    new Vector2(p2Screen.x, p2Screen.y));
            // CPU側で距離を再計算。この式正しい。
            // mousePos を adjustedRect 座標系に変換（DispatchHitTestと同じ変換）
            // CPU側で距離を再計算（変換済みマウス座標を使用）
            float cpuDistance = DistanceToLineSegmentCPU(_cachedAdjustedMousePos,
                new Vector2(p1Screen.x, p1Screen.y),
                new Vector2(p2Screen.x, p2Screen.y));

            // 比較
            float distanceError = Mathf.Abs(gpuDistance - cpuDistance);
            const float errorThreshold = 5f;  // ピクセル
            
            if (distanceError > errorThreshold)
            {
                Debug.LogWarning($"[HoverDebug] Line hover mismatch!\n" +
                    $"  LineIndex: {lineIndex} (v1={v1}, v2={v2}, FaceIndex={lineData.FaceIndex}, LineType={lineData.LineType})\n" +
                    $"  GPU Distance: {gpuDistance:F2}, CPU Distance: {cpuDistance:F2}, Error: {distanceError:F2}\n" +
                    $"  MousePos: ({mousePos.x:F1}, {mousePos.y:F1})\n" +
                    $"  P1 Screen: ({p1Screen.x:F1}, {p1Screen.y:F1})\n" +
                    $"  P2 Screen: ({p2Screen.x:F1}, {p2Screen.y:F1})");
            }
            
            // 距離が閾値外なのにホバー判定された場合
            const float lineHitThreshold = 10f;  // ホバー判定閾値の2倍
            if (cpuDistance > lineHitThreshold)
            {
                Debug.LogError($"[HoverDebug] Line hover distance too large!\n" +
                    $"  LineIndex: {lineIndex} (v1={v1}, v2={v2})\n" +
                    $"  CPU Distance: {cpuDistance:F2} (threshold: {lineHitThreshold})\n" +
                    $"  This suggests index mismatch between GPU and CPU data!");
                    
                // 近い線分を探す（デバッグ用）
                FindNearestLineCPU(mousePos, edgeCache, lineHitThreshold);
            }
        }
        
        /// <summary>
        /// CPU側で最も近い線分を探す（デバッグ用）
        /// </summary>
        private void FindNearestLineCPU(Vector2 mousePos, MeshEdgeCache edgeCache, float threshold)
        {
            if (_screenPositionData == null) return;
            
            float minDist = float.MaxValue;
            int nearestIndex = -1;
            
            for (int i = 0; i < edgeCache.LineCount; i++)
            {
                var line = edgeCache.Lines[i];
                if (line.V1 < 0 || line.V1 >= _vertexCount || 
                    line.V2 < 0 || line.V2 >= _vertexCount)
                    continue;
                
                Vector4 p1 = _screenPositionData[line.V1];
                Vector4 p2 = _screenPositionData[line.V2];
                
                // 無効な座標はスキップ
                if (p1.w < 0.5f || p2.w < 0.5f) continue;
                
                float dist = DistanceToLineSegmentCPU(mousePos, 
                    new Vector2(p1.x, p1.y), 
                    new Vector2(p2.x, p2.y));
                
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestIndex = i;
                }
            }
            
            if (nearestIndex >= 0 && minDist < threshold)
            {
                var nearestLine = edgeCache.Lines[nearestIndex];
                Debug.Log($"[HoverDebug] CPU found nearest line:\n" +
                    $"  Index: {nearestIndex} (v1={nearestLine.V1}, v2={nearestLine.V2})\n" +
                    $"  Distance: {minDist:F2}");
            }
        }
        
        /// <summary>
        /// 点と線分の最短距離（CPU計算）
        /// </summary>
        private float DistanceToLineSegmentCPU(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float lenSq = line.sqrMagnitude;
            
            if (lenSq < 0.000001f)
                return Vector2.Distance(point, lineStart);
            
            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / lenSq);
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }
        
        // スクリーン座標データ（デバッグ用）
        private Vector4[] _screenPositionData;

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
            // デバッグ: ホバーインデックスを確認
            //Debug.Log($"[DrawDebug] DrawPoints: _hoverVertexIndex={_hoverVertexIndex}");
            //_pointMaterial.SetInt("_HoverVertexIndex", _hoverVertexIndex);
            _pointMaterial.SetFloat("_HoverVertexIndex", _hoverVertexIndex);

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
            // デバッグ: ホバーインデックスを確認  
            //Debug.Log($"[DrawDebug] DrawLines: _hoverLineIndex={_hoverLineIndex}");
            //_lineMaterial.SetInt("_HoverLineIndex", _hoverLineIndex);
            _lineMaterial.SetFloat("_HoverLineIndex", _hoverLineIndex);

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
        public void DrawHoverFace(Vector2 windowSize, MeshObject meshObject, Color? hoverColor = null)
        {
            if (!_initialized || _hoverFaceIndex < 0 || meshObject == null) return;
            if (_hoverFaceIndex >= meshObject.FaceCount) return;

            // スクリーン座標を読み戻し
            var screenPositions = new Vector4[_vertexCount];
            _screenPositionBuffer.GetData(screenPositions);

            var face = meshObject.Faces[_hoverFaceIndex];
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
            _vertexHitDepthBuffer?.Release();
            _lineHitDistanceBuffer?.Release();
            _lineHitDepthBuffer?.Release();
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
            _vertexHitDepthBuffer = null;
            _lineHitDistanceBuffer = null;
            _lineHitDepthBuffer = null;
            _faceHitBuffer = null;
            _faceHitDepthBuffer = null;
            
            _vertexHitDistanceData = null;
            _vertexHitDepthData = null;
            _lineHitDistanceData = null;
            _lineHitDepthData = null;
            _faceHitData = null;
            _faceHitDepthData = null;
            
            _vertexCount = 0;
            _faceCount = 0;
            _lineCount = 0;
            _cachedMeshObject = null;
        }

        public void InvalidateBuffers()
        {
            _cachedMeshObject = null;
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
