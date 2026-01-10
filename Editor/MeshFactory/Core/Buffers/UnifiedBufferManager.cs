// Assets/Editor/MeshFactory/Core/Buffers/UnifiedBufferManager.cs
// 統合バッファ管理クラス
// 全モデル・全メッシュのデータを統合管理

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;

namespace MeshFactory.Core
{
    /// <summary>
    /// 統合バッファ管理クラス
    /// 複数モデル・複数メッシュを1つのバッファセットで管理
    /// </summary>
    public partial class UnifiedBufferManager : IDisposable
    {
        // ============================================================
        // 定数
        // ============================================================

        private const int DEFAULT_VERTEX_CAPACITY = 65536;
        private const int DEFAULT_LINE_CAPACITY = 131072;
        private const int DEFAULT_FACE_CAPACITY = 65536;
        private const int DEFAULT_INDEX_CAPACITY = 262144;

        // ============================================================
        // バッファ（Level 5: Topology）
        // ============================================================

        // 頂点インデックス（面の構成）
        private ComputeBuffer _indexBuffer;
        private uint[] _indices;

        // ライン/エッジ
        private ComputeBuffer _lineBuffer;
        private UnifiedLine[] _lines;

        // 面情報
        private ComputeBuffer _faceBuffer;
        private UnifiedFace[] _faces;

        // メッシュ情報
        private ComputeBuffer _meshInfoBuffer;
        private MeshInfo[] _meshInfos;

        // モデル情報
        private ComputeBuffer _modelInfoBuffer;
        private ModelInfo[] _modelInfos;

        // ============================================================
        // バッファ（Level 4: Transform）
        // ============================================================

        // 頂点位置
        private ComputeBuffer _positionBuffer;
        private Vector3[] _positions;

        // 法線
        private ComputeBuffer _normalBuffer;
        private Vector3[] _normals;

        // UV
        private ComputeBuffer _uvBuffer;
        private Vector2[] _uvs;

        // バウンディングボックス
        private ComputeBuffer _boundsBuffer;
        private AABB[] _bounds;

        // ミラー頂点位置
        private ComputeBuffer _mirrorPositionBuffer;
        private Vector3[] _mirrorPositions;

        // ============================================================
        // バッファ（Level 3: Selection）
        // ============================================================

        // 頂点フラグ
        private ComputeBuffer _vertexFlagsBuffer;
        private uint[] _vertexFlags;

        // ラインフラグ
        private ComputeBuffer _lineFlagsBuffer;
        private uint[] _lineFlags;

        // 面フラグ
        private ComputeBuffer _faceFlagsBuffer;
        private uint[] _faceFlags;

        // ============================================================
        // バッファ（Level 2: Camera）
        // ============================================================

        // カメラ情報
        private ComputeBuffer _cameraBuffer;
        private CameraInfo[] _cameraInfo;

        // スクリーン座標
        private ComputeBuffer _screenPosBuffer;
        private Vector2[] _screenPositions;

        // カリング結果
        private ComputeBuffer _cullingBuffer;
        private uint[] _cullingResults;

        // ============================================================
        // バッファ（Level 1: Mouse）
        // ============================================================

        // ヒットテスト入力
        private ComputeBuffer _hitTestInputBuffer;
        private HitTestInput[] _hitTestInput;

        // ヒット距離（頂点）
        private ComputeBuffer _hitVertexDistBuffer;
        private float[] _hitVertexDistances;

        // ヒット距離（ライン）
        private ComputeBuffer _hitLineDistBuffer;
        private float[] _hitLineDistances;

        // ヒット結果（面）
        private ComputeBuffer _faceHitBuffer;
        private float[] _faceHitResults;

        // ヒット深度（面）
        private ComputeBuffer _faceHitDepthBuffer;
        private float[] _faceHitDepths;

        // ============================================================
        // カウント・オフセット
        // ============================================================

        private int _totalVertexCount;
        private int _totalLineCount;
        private int _totalFaceCount;
        private int _totalIndexCount;
        private int _meshCount;
        private int _modelCount;

        // 容量
        private int _vertexCapacity;
        private int _lineCapacity;
        private int _faceCapacity;
        private int _indexCapacity;

        // ============================================================
        // GPU計算
        // ============================================================

        private ComputeShader _computeShader;
        private int _kernelClear;
        private int _kernelClearFace;
        private int _kernelScreenPos;
        private int _kernelCulling;
        private int _kernelVertexHit;
        private int _kernelLineHit;
        private int _kernelFaceVisibility;
        private int _kernelLineVisibility;
        private int _kernelFaceHit;
        private int _kernelUpdateHover;
        private bool _gpuComputeAvailable = false;

        // GPU出力バッファ（float4: xy=screen, z=depth, w=valid）
        private ComputeBuffer _screenPosBuffer4;
        private Vector4[] _screenPositions4;
        private ComputeBuffer _mirrorScreenPosBuffer4;
        private Vector4[] _mirrorScreenPositions4;

        // ============================================================
        // 状態
        // ============================================================

        private bool _isInitialized = false;
        private bool _disposed = false;

        // 依存コンポーネント
        private FlagManager _flagManager;
        private UpdateManager _updateManager;

        // ============================================================
        // プロパティ
        // ============================================================

        public bool IsInitialized => _isInitialized;
        public bool GpuComputeAvailable => _gpuComputeAvailable;
        public int TotalVertexCount => _totalVertexCount;
        public int TotalLineCount => _totalLineCount;
        public int TotalFaceCount => _totalFaceCount;
        public int MeshCount => _meshCount;
        public int ModelCount => _modelCount;

        // バッファアクセス
        public ComputeBuffer PositionBuffer => _positionBuffer;
        public ComputeBuffer NormalBuffer => _normalBuffer;
        public ComputeBuffer UVBuffer => _uvBuffer;
        public ComputeBuffer IndexBuffer => _indexBuffer;
        public ComputeBuffer LineBuffer => _lineBuffer;
        public ComputeBuffer FaceBuffer => _faceBuffer;
        public ComputeBuffer VertexFlagsBuffer => _vertexFlagsBuffer;
        public ComputeBuffer LineFlagsBuffer => _lineFlagsBuffer;
        public ComputeBuffer FaceFlagsBuffer => _faceFlagsBuffer;
        public ComputeBuffer MeshInfoBuffer => _meshInfoBuffer;
        public ComputeBuffer ModelInfoBuffer => _modelInfoBuffer;
        public ComputeBuffer CameraBuffer => _cameraBuffer;
        public ComputeBuffer ScreenPosBuffer => _screenPosBuffer;
        public ComputeBuffer FaceHitBuffer => _faceHitBuffer;
        public ComputeBuffer FaceHitDepthBuffer => _faceHitDepthBuffer;

        // CPU配列アクセス
        public Vector3[] Positions => _positions;
        public uint[] VertexFlags => _vertexFlags;
        public uint[] LineFlags => _lineFlags;
        public UnifiedLine[] Lines => _lines;
        public MeshInfo[] MeshInfos => _meshInfos;
        public float[] FaceHitResults => _faceHitResults;
        public float[] FaceHitDepths => _faceHitDepths;
        public uint[] CullingResults => _cullingResults;

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UnifiedBufferManager(
            FlagManager flagManager = null,
            UpdateManager updateManager = null)
        {
            _flagManager = flagManager ?? new FlagManager();
            _updateManager = updateManager;

            _vertexCapacity = DEFAULT_VERTEX_CAPACITY;
            _lineCapacity = DEFAULT_LINE_CAPACITY;
            _faceCapacity = DEFAULT_FACE_CAPACITY;
            _indexCapacity = DEFAULT_INDEX_CAPACITY;
        }

        // ============================================================
        // 初期化
        // ============================================================

        /// <summary>
        /// バッファを初期化
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
                return;

            // CPU配列初期化
            _positions = new Vector3[_vertexCapacity];
            _normals = new Vector3[_vertexCapacity];
            _uvs = new Vector2[_vertexCapacity];
            _vertexFlags = new uint[_vertexCapacity];
            _mirrorPositions = new Vector3[_vertexCapacity];

            _lines = new UnifiedLine[_lineCapacity];
            _lineFlags = new uint[_lineCapacity];

            _faces = new UnifiedFace[_faceCapacity];
            _faceFlags = new uint[_faceCapacity];

            _indices = new uint[_indexCapacity];

            _meshInfos = new MeshInfo[256];
            _modelInfos = new ModelInfo[16];

            _cameraInfo = new CameraInfo[1];
            _screenPositions = new Vector2[_vertexCapacity];
            _cullingResults = new uint[_vertexCapacity];

            _hitTestInput = new HitTestInput[1];
            _hitVertexDistances = new float[_vertexCapacity];
            _hitLineDistances = new float[_lineCapacity];
            _faceHitResults = new float[_faceCapacity];
            _faceHitDepths = new float[_faceCapacity];

            _bounds = new AABB[256];

            // float4スクリーン座標（GPU用）
            _screenPositions4 = new Vector4[_vertexCapacity];
            _mirrorScreenPositions4 = new Vector4[_vertexCapacity];

            // GPUバッファ作成
            CreateAllBuffers();

            // ComputeShaderロード
            InitializeComputeShader();

            _isInitialized = true;
        }

        /// <summary>
        /// ComputeShaderを初期化
        /// </summary>
        private void InitializeComputeShader()
        {
            _computeShader = Resources.Load<ComputeShader>("UnifiedCompute");
            if (_computeShader == null)
            {
                Debug.LogWarning("[UnifiedBufferManager] ComputeShader not found, using CPU fallback");
                _gpuComputeAvailable = false;
                return;
            }

            try
            {
                _kernelClear = _computeShader.FindKernel("ClearBuffers");
                _kernelClearFace = _computeShader.FindKernel("ClearFaceBuffers");
                _kernelScreenPos = _computeShader.FindKernel("ComputeScreenPositions");
                _kernelCulling = _computeShader.FindKernel("ComputeCulling");
                _kernelVertexHit = _computeShader.FindKernel("ComputeVertexHitTest");
                _kernelLineHit = _computeShader.FindKernel("ComputeLineHitTest");
                _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
                _kernelLineVisibility = _computeShader.FindKernel("ComputeLineVisibility");
                _kernelFaceHit = _computeShader.FindKernel("ComputeFaceHitTest");
                _kernelUpdateHover = _computeShader.FindKernel("UpdateHoverFlags");
                _gpuComputeAvailable = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[UnifiedBufferManager] ComputeShader kernel error: {e.Message}");
                _gpuComputeAvailable = false;
            }
        }

        /// <summary>
        /// 全GPUバッファを作成
        /// </summary>
        private void CreateAllBuffers()
        {
            // Level 5: Topology
            _positionBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float) * 3);
            _normalBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float) * 3);
            _uvBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float) * 2);
            _indexBuffer = new ComputeBuffer(_indexCapacity, sizeof(uint));
            _lineBuffer = new ComputeBuffer(_lineCapacity, UnifiedLine.Stride);
            _faceBuffer = new ComputeBuffer(_faceCapacity, UnifiedFace.Stride);
            _meshInfoBuffer = new ComputeBuffer(_meshInfos.Length, MeshInfo.Stride);
            _modelInfoBuffer = new ComputeBuffer(16, ModelInfo.Stride);

            // Level 4: Transform
            _boundsBuffer = new ComputeBuffer(_meshInfos.Length, AABB.Stride);
            _mirrorPositionBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float) * 3);

            // Level 3: Selection
            _vertexFlagsBuffer = new ComputeBuffer(_vertexCapacity, sizeof(uint));
            _lineFlagsBuffer = new ComputeBuffer(_lineCapacity, sizeof(uint));
            _faceFlagsBuffer = new ComputeBuffer(_faceCapacity, sizeof(uint));

            // Level 2: Camera
            _cameraBuffer = new ComputeBuffer(1, CameraInfo.Stride);
            _screenPosBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float) * 2);
            _screenPosBuffer4 = new ComputeBuffer(_vertexCapacity, sizeof(float) * 4); // GPU用float4
            _mirrorScreenPosBuffer4 = new ComputeBuffer(_vertexCapacity, sizeof(float) * 4); // GPU用float4
            _cullingBuffer = new ComputeBuffer(_vertexCapacity, sizeof(uint));

            // Level 1: Mouse
            _hitTestInputBuffer = new ComputeBuffer(1, HitTestInput.Stride);
            _hitVertexDistBuffer = new ComputeBuffer(_vertexCapacity, sizeof(float));
            _hitLineDistBuffer = new ComputeBuffer(_lineCapacity, sizeof(float));
            _faceHitBuffer = new ComputeBuffer(_faceCapacity, sizeof(float));
            _faceHitDepthBuffer = new ComputeBuffer(_faceCapacity, sizeof(float));
        }

        /// <summary>
        /// 容量を確保（必要に応じて再作成）
        /// </summary>
        public void EnsureCapacity(int vertexCount, int lineCount, int faceCount, int indexCount, int meshCount = 0)
        {
            bool needsRebuild = false;

            if (vertexCount > _vertexCapacity)
            {
                _vertexCapacity = Mathf.NextPowerOfTwo(vertexCount);
                needsRebuild = true;
            }

            if (lineCount > _lineCapacity)
            {
                _lineCapacity = Mathf.NextPowerOfTwo(lineCount);
                needsRebuild = true;
            }

            if (faceCount > _faceCapacity)
            {
                _faceCapacity = Mathf.NextPowerOfTwo(faceCount);
                needsRebuild = true;
            }

            if (indexCount > _indexCapacity)
            {
                _indexCapacity = Mathf.NextPowerOfTwo(indexCount);
                needsRebuild = true;
            }

            // MeshInfos配列のリサイズ
            if (meshCount > 0 && meshCount > _meshInfos.Length)
            {
                int oldSize = _meshInfos.Length;
                int newSize = Mathf.NextPowerOfTwo(meshCount);
                Debug.Log($"[EnsureCapacity] Resizing MeshInfos: {oldSize} -> {newSize} (requested: {meshCount})");
                Array.Resize(ref _meshInfos, newSize);
                
                // GPUバッファも再作成
                _meshInfoBuffer?.Release();
                _meshInfoBuffer = new ComputeBuffer(newSize, MeshInfo.Stride);
            }

            if (needsRebuild)
            {
                ResizeBuffers();
            }
        }

        /// <summary>
        /// バッファサイズを変更
        /// </summary>
        private void ResizeBuffers()
        {
            // CPU配列リサイズ
            Array.Resize(ref _positions, _vertexCapacity);
            Array.Resize(ref _normals, _vertexCapacity);
            Array.Resize(ref _uvs, _vertexCapacity);
            Array.Resize(ref _vertexFlags, _vertexCapacity);
            Array.Resize(ref _mirrorPositions, _vertexCapacity);
            Array.Resize(ref _screenPositions, _vertexCapacity);
            Array.Resize(ref _screenPositions4, _vertexCapacity);
            Array.Resize(ref _mirrorScreenPositions4, _vertexCapacity);
            Array.Resize(ref _cullingResults, _vertexCapacity);
            Array.Resize(ref _hitVertexDistances, _vertexCapacity);

            Array.Resize(ref _lines, _lineCapacity);
            Array.Resize(ref _lineFlags, _lineCapacity);
            Array.Resize(ref _hitLineDistances, _lineCapacity);

            Array.Resize(ref _faces, _faceCapacity);
            Array.Resize(ref _faceFlags, _faceCapacity);
            Array.Resize(ref _faceHitResults, _faceCapacity);
            Array.Resize(ref _faceHitDepths, _faceCapacity);

            Array.Resize(ref _indices, _indexCapacity);

            // GPUバッファ再作成
            ReleaseAllBuffers();
            CreateAllBuffers();
        }

        // ============================================================
        // クリーンアップ
        // ============================================================

        /// <summary>
        /// 全GPUバッファを解放
        /// </summary>
        private void ReleaseAllBuffers()
        {
            ReleaseBuffer(ref _positionBuffer);
            ReleaseBuffer(ref _normalBuffer);
            ReleaseBuffer(ref _uvBuffer);
            ReleaseBuffer(ref _indexBuffer);
            ReleaseBuffer(ref _lineBuffer);
            ReleaseBuffer(ref _faceBuffer);
            ReleaseBuffer(ref _meshInfoBuffer);
            ReleaseBuffer(ref _modelInfoBuffer);
            ReleaseBuffer(ref _boundsBuffer);
            ReleaseBuffer(ref _mirrorPositionBuffer);
            ReleaseBuffer(ref _vertexFlagsBuffer);
            ReleaseBuffer(ref _lineFlagsBuffer);
            ReleaseBuffer(ref _faceFlagsBuffer);
            ReleaseBuffer(ref _cameraBuffer);
            ReleaseBuffer(ref _screenPosBuffer);
            ReleaseBuffer(ref _screenPosBuffer4);
            ReleaseBuffer(ref _mirrorScreenPosBuffer4);
            ReleaseBuffer(ref _cullingBuffer);
            ReleaseBuffer(ref _hitTestInputBuffer);
            ReleaseBuffer(ref _hitVertexDistBuffer);
            ReleaseBuffer(ref _hitLineDistBuffer);
            ReleaseBuffer(ref _faceHitBuffer);
            ReleaseBuffer(ref _faceHitDepthBuffer);
        }

        private void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        /// <summary>
        /// データをクリア（バッファは保持）
        /// </summary>
        public void ClearData()
        {
            _totalVertexCount = 0;
            _totalLineCount = 0;
            _totalFaceCount = 0;
            _totalIndexCount = 0;
            _meshCount = 0;
            _modelCount = 0;
        }

        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    ReleaseAllBuffers();

                    _positions = null;
                    _normals = null;
                    _uvs = null;
                    _vertexFlags = null;
                    _lines = null;
                    _lineFlags = null;
                    _faces = null;
                    _faceFlags = null;
                    _indices = null;
                    _meshInfos = null;
                    _modelInfos = null;
                }

                _disposed = true;
                _isInitialized = false;
            }
        }

        ~UnifiedBufferManager()
        {
            Dispose(false);
        }
    }
}
