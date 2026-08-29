// Assets/Editor/Poly_Ling/Core/Buffers/UnifiedBufferManager.cs
// 統合バッファ管理クラス
// 全モデル・全メッシュのデータを統合管理

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.Core
{
    /// <summary>
    /// uint4構造体（GPUバッファ転送用）
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct UInt4
    {
        public uint x, y, z, w;

        public UInt4(uint x, uint y, uint z, uint w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static readonly int Stride = sizeof(uint) * 4;
    }

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
        // MeshContext → UnifiedMeshIndex マッピング
        // ============================================================
        
        // MeshContextsのインデックス → UnifiedSystem内メッシュインデックス
        private Dictionary<int, int> _contextToUnifiedMeshIndex = new Dictionary<int, int>();
        
        // UnifiedSystem内メッシュインデックス → MeshContextsのインデックス（逆引き）
        private Dictionary<int, int> _unifiedToContextMeshIndex = new Dictionary<int, int>();
        
        /// <summary>
        /// MeshContextsのインデックスをUnifiedSystem内メッシュインデックスに変換
        /// </summary>
        public int ContextToUnifiedMeshIndex(int contextIndex)
        {
            if (_contextToUnifiedMeshIndex.TryGetValue(contextIndex, out int unifiedIndex))
                return unifiedIndex;
            return -1;
        }
        
        /// <summary>
        /// UnifiedSystem内メッシュインデックスをMeshContextsのインデックスに変換
        /// </summary>
        public int UnifiedToContextMeshIndex(int unifiedIndex)
        {
            if (_unifiedToContextMeshIndex.TryGetValue(unifiedIndex, out int contextIndex))
                return contextIndex;
            return -1;
        }

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

        // 頂点位置（ローカル座標）
        private ComputeBuffer _positionBuffer;
        private Vector3[] _positions;

        // ワールド座標変換後の頂点位置（GPU計算出力）
        private ComputeBuffer _worldPositionBuffer;
        private Vector3[] _worldPositions;

        // UV展開済み頂点（Unity Mesh用）
        private ComputeBuffer _expandedToOriginalBuffer;  // 展開後idx → 元idx マッピング
        private ComputeBuffer _expandedPositionBuffer;    // 展開後の頂点位置
        private ComputeBuffer _expandedNormalBuffer;      // 展開後の法線
        private uint[] _expandedToOriginal;               // CPU側マッピングデータ
        private int _totalExpandedVertexCount;            // 展開後の総頂点数

        // ------------------------------------------------------------
        // メッシュごとの UV 展開範囲（CPU 専用。GPU には送らない）
        //
        // 【なぜ必要か】
        //   書き戻し (UnifiedSystemAdapter.WritebackTransformedVertices) は以前、
        //   自前で MeshContextList を走査して展開頂点数とオフセットを数え直していた。
        //   数え方が BuildExpandedVertexMapping と 1 か所でも違うと、別メッシュの
        //   ワールド座標を UnityMesh へ書き込む。数える主体を構築側の 1 つに寄せ、
        //   書き戻しは読むだけにする。
        //
        // 【添字】unified メッシュ index（_meshInfos と同じ並び）。
        //   MeshContextList の index ではない。変換は ContextToUnifiedMeshIndex。
        //
        // 【MeshInfo に入れない理由】
        //   MeshInfo は UnifiedCompute.compute の struct MeshInfo とレイアウトを
        //   共有している。フィールドを足すと HLSL 側も同時に直す必要があり、
        //   GPU はこの値を使わないので割に合わない。
        // ------------------------------------------------------------
        private uint[] _meshExpandedStart;                // 展開バッファ内の開始位置
        private uint[] _meshExpandedCount;                // 展開後頂点数

        // 変換行列（メッシュごと）
        private ComputeBuffer _transformMatrixBuffer;
        private Matrix4x4[] _transformMatrices;

        // 頂点→メッシュインデックス（各頂点がどのメッシュに属するか）
        private ComputeBuffer _vertexMeshIndexBuffer;
        private uint[] _vertexMeshIndices;

        // ボーンウェイト（スキンメッシュ用、通常メッシュは (1,0,0,0)）
        private ComputeBuffer _boneWeightsBuffer;
        private Vector4[] _boneWeights;

        // ボーンインデックス（スキンメッシュ用、通常メッシュは (meshIndex,0,0,0)）
        private ComputeBuffer _boneIndicesBuffer;
        private UInt4[] _boneIndices;

        // ミラー用ボーンウェイト（MirrorBoneWeightがあればそれ、なければBoneWeight）
        private ComputeBuffer _mirrorBoneWeightsBuffer;
        private Vector4[] _mirrorBoneWeights;

        // ミラー用ボーンインデックス
        private ComputeBuffer _mirrorBoneIndicesBuffer;
        private UInt4[] _mirrorBoneIndices;

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

        // スキニング済みミラー頂点位置（GPU計算結果）
        private ComputeBuffer _skinnedMirrorPositionBuffer;
        private Vector3[] _skinnedMirrorPositions;

        // ============================================================
        // バッファ（Level 3: Selection）
        // ============================================================

        // 頂点フラグ
        private ComputeBuffer _vertexFlagsBuffer;
        private uint[] _vertexFlags;

        // 頂点カリング結果の CPU キャッシュ (slot 別)
        // GPU 側 _FaceCulledBuffer (per-slot) を ReadBack したもの。
        private uint[] _faceCulledCache;

        // GPU 側 _VertexCulledBuffer (per-slot) を ReadBack したもの。
        // 矩形/投げ縄選択の CPU ループで「表面の面に属さない頂点」を除外するために使う。
        // _vertexFlags に混ぜると CPU 側からの SetData で消失するため、独立した配列として保持。
        private uint[] _vertexCulledCache;

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
        //
        // 【_screenPosBuffer / _cullingBuffer / _cullingResults を撤去した理由】
        //   _screenPosBuffer(float2) は ComputeScreenPositions が SetData するだけで、
        //   ComputeShader.SetBuffer に渡している箇所が 0 件だった。GPU が実際に読むのは
        //   _screenPosBuffer4(float4) と per-slot の _slotScreenPosBufs。
        //   _cullingBuffer は確保と解放しかしておらず SetData も SetBuffer も 0 件。
        //   _cullingResults は CPU 版 ComputeScreenPositions が埋めるだけで、
        //   公開プロパティ CullingResults の呼出元が 0 件だった。
        //   背面カリングの CPU キャッシュは _vertexCulledCache / _faceCulledCache が担う。
        private Vector2[] _screenPositions;

        // ================================================================
        // per-slot カリングバッファ（CullingSlotCount 個）
        // ================================================================
        //
        // 【slot の割り当て】 2026-08-28
        //   0〜3 : ビューポート表示用。PlayerViewportManager の
        //          SlotPerspective / SlotTop / SlotFront / SlotSide と 1:1。
        //          そのビューポートのカメラで計算した結果を保持し、
        //          描画シェーダー（MeshFactoryWireframe3D 等）が読む。
        //   4    : ヒットテスト用スクラッチ（HitTestSlot）。
        //          UnifiedMeshSystem.ProcessMouseUpdate が
        //          「今ポインタが乗っているビューポート」のカメラで毎回上書きする。
        //
        // 【分離した理由】
        //   以前はヒットテストも slot 0 を使っていた。slot 0 は Perspective
        //   ビューの表示用でもあるため、Top ビュー上でマウスを動かすと
        //   Top のカメラで計算したカリングが Perspective の表示用バッファへ
        //   書き込まれ、Perspective の表示が壊れた。
        //   さらにホバー経路は DispatchApplyMirrorCullGPU を呼ばないので、
        //   永続ミラーの表示トグルも slot 0 では反映されなかった。
        //
        //   従来はこの汚染を、描画準備のたびに slot 0 を Perspective カメラで
        //   計算し直すことで結果的に打ち消していた。その再計算を効率化で
        //   省いた結果、汚染が残るようになった。用途を分ければ構造的に解決する。
        //
        // 【守ること】
        //   ・表示用の slot へヒットテスト経路から書き込まない。
        //   ・ヒットテスト用の slot を描画シェーダーへ渡さない。
        //   ・ビューポートを増やすときは CullingSlotCount を増やし、
        //     HitTestSlot を末尾（= CullingSlotCount - 1）に保つ。

        /// <summary>ビューポート表示用 slot の本数。slot 番号 0〜3。</summary>
        public const int ViewportSlotCount = 4;

        /// <summary>ヒットテスト専用の slot 番号。表示用とは絶対に共用しない。</summary>
        public const int HitTestSlot = ViewportSlotCount;

        /// <summary>確保する slot バッファの総数（表示用 + ヒットテスト用）。</summary>
        public const int CullingSlotCount = ViewportSlotCount + 1;

        private ComputeBuffer[] _slotScreenPosBufs;     // float4 × vertexCount
        private ComputeBuffer[] _slotVertexCulledBufs;  // uint   × vertexCount
        private ComputeBuffer[] _slotLineCulledBufs;    // uint   × lineCount
        private ComputeBuffer[] _slotFaceCulledBufs;    // uint   × faceCount

        // ClearCulledFlagsGPU 用 zeros キャッシュ（GC allocation 回避）
        private uint[] _zeroVertexCache;
        private uint[] _zeroLineCache;
        private uint[] _zeroFaceCache;

        // ============================================================
        // バッファ（Level 1: Mouse）
        // ============================================================

        // ヒットテスト入力
        private ComputeBuffer _hitTestInputBuffer;
        private HitTestInput[] _hitTestInput;

        // ヒット距離（頂点）
        private ComputeBuffer _hitVertexDistBuffer;
        private float[] _hitVertexDistances;

        // ヒット距離（頂点・吸着用）
        // メッシュ選択を無視するヒットテストの出力。面追加ツールが
        // 非選択オブジェクトの頂点へ位置を合わせるために使う。
        // 通常のホバー結果（_hitVertexDistances）とは独立。
        private ComputeBuffer _snapHitVertexDistBuffer;
        private float[] _snapHitVertexDistances;

        // ヒット距離（ライン）
        private ComputeBuffer _hitLineDistBuffer;
        private float[] _hitLineDistances;

        // ヒット結果（面）
        private ComputeBuffer _faceHitBuffer;
        // 面ヒット結果の CPU キャッシュ。x = ヒット（0 or 1）、y = 深度。
        // GPU 側 _FaceHitBuffer（float2）と 1:1。旧 _faceHitResults(float[]) と
        // _faceHitDepths(float[]) を統合したもの。分けていた頃は 1 ホバーごとに
        // GetData が 2 回走っていた（同期読み戻しの回数がそのまま停止回数になる）。
        private Vector2[] _faceHit;

        // ヒット深度（面）

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
        private int _kernelVertexSnapHit;
        private int _kernelLineHit;
        private int _kernelFaceVisibility;
        private int _kernelLineVisibility;
        private int _kernelFaceHit;
        private int _kernelUpdateHover;
        private int _kernelClearCulled;
        private int _kernelClearFaceCulled;
        private int _kernelApplyMirrorCull;
        private bool _gpuComputeAvailable = false;

        // GPU 出力の受け先（float4: xy=screen, z=depth, w=valid）。
        // ComputeScreenPositionsGPU が readback: true のときだけ GetData で埋める。
        private Vector4[] _screenPositions4;

        // 【_screenPosBuffer4 / _mirrorScreenPosBuffer4 / _mirrorScreenPositions4 を撤去した理由】
        //   2026-08-28
        //
        //   _screenPosBuffer4:
        //     ComputeShader へ渡していたのは DispatchClearBuffersGPU の
        //     _kernelClear（_ScreenPositionBuffer）1 か所だけで、書いた値の読み手が
        //     0 件だった。ヒットテストも表示用カリングも per-slot バッファ
        //     _slotScreenPosBufs[slot] を読む。slot バッファは Initialize で
        //     CullingSlotCount(=4) 本を必ず確保するため、
        //     旧コードの「GetSlotScreenPosBuffer(slot) ?? _screenPosBuffer4」の
        //     フォールバック側には一度も到達していなかった。
        //
        //   _mirrorScreenPosBuffer4 / _mirrorScreenPositions4:
        //     ClearBuffers と ComputeScreenPositions が書き込むだけで、
        //     .compute / .shader / .hlsl のどこにも読み取りが 0 件だった。
        //     ミラー有効時は全頂点ぶんの行列積・除算・書き込みを毎回捨てていた。
        //     シェーダー側のミラー分岐と _UseMirror も同時に撤去している。
        //     ミラー頂点のワールド座標 _mirrorPositionBuffer は
        //     TransformVertices が使うので残っている。

        // ============================================================
        // 状態
        // ============================================================

        private bool _isInitialized = false;
        private bool _disposed = false;

        // 診断用: 生存中の UnifiedBufferManager インスタンス数
        private static int _liveCount = 0;

        /// <summary>
        /// 生存中の UnifiedBufferManager インスタンス数。
        /// PLPerfLog が長期ログの 1 列として読む。増え続ける場合は Dispose 漏れ。
        /// </summary>
        public static int LiveCount => _liveCount;

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
        public ComputeBuffer WorldPositionBuffer => _worldPositionBuffer;
        // ExpandedPositionBuffer / ExpandedNormalBuffer は呼出元 0 件のため撤去した。
        // 展開バッファは DispatchExpandVertices と GetExpandedPositions が内部で扱う。
        public int TotalExpandedVertexCount => _totalExpandedVertexCount;

        /// <summary>
        /// 指定 unified メッシュの UV 展開範囲を返す。載っていなければ false。
        ///
        /// 値の生成は BuildExpandedVertexMapping ただ 1 か所。呼び出し側で
        /// 展開頂点数を数え直さないこと（数え方が割れると別メッシュの座標を書く）。
        /// </summary>
        public bool TryGetExpandedRange(int unifiedMeshIndex, out int start, out int count)
        {
            start = 0;
            count = 0;
            if (_meshExpandedStart == null || _meshExpandedCount == null) return false;
            if (unifiedMeshIndex < 0 || unifiedMeshIndex >= _meshCount) return false;
            if (unifiedMeshIndex >= _meshExpandedStart.Length) return false;

            start = (int)_meshExpandedStart[unifiedMeshIndex];
            count = (int)_meshExpandedCount[unifiedMeshIndex];
            return true;
        }
        public ComputeBuffer TransformMatrixBuffer => _transformMatrixBuffer;
        public ComputeBuffer VertexMeshIndexBuffer => _vertexMeshIndexBuffer;
        public ComputeBuffer BoneWeightsBuffer => _boneWeightsBuffer;
        public ComputeBuffer BoneIndicesBuffer => _boneIndicesBuffer;
        public ComputeBuffer MirrorBoneWeightsBuffer => _mirrorBoneWeightsBuffer;
        public ComputeBuffer MirrorBoneIndicesBuffer => _mirrorBoneIndicesBuffer;
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
        // FaceHitBuffer / FaceHitDepthBuffer は呼出元 0 件のため撤去した（2026-08-28）。
        // 面ヒット結果を読むのは FindNearestFaceFromGPU だけで、内部の _faceHit を使う。

        /// <summary>スロット指定スクリーン座標バッファ</summary>
        public ComputeBuffer GetSlotScreenPosBuffer(int slot)
            => (_slotScreenPosBufs != null && slot >= 0 && slot < CullingSlotCount)
               ? _slotScreenPosBufs[slot] : null;

        /// <summary>スロット指定頂点カリングバッファ</summary>
        public ComputeBuffer GetVertexCulledBuffer(int slot)
            => (_slotVertexCulledBufs != null && slot >= 0 && slot < CullingSlotCount)
               ? _slotVertexCulledBufs[slot] : null;

        /// <summary>スロット指定辺カリングバッファ</summary>
        public ComputeBuffer GetLineCulledBuffer(int slot)
            => (_slotLineCulledBufs != null && slot >= 0 && slot < CullingSlotCount)
               ? _slotLineCulledBufs[slot] : null;

        /// <summary>スロット指定面カリングバッファ</summary>
        public ComputeBuffer GetFaceCulledBuffer(int slot)
            => (_slotFaceCulledBufs != null && slot >= 0 && slot < CullingSlotCount)
               ? _slotFaceCulledBufs[slot] : null;

        // CPU配列アクセス
        public Vector3[] Positions => _positions;
        
        /// <summary>
        /// 描画に使用する位置配列を取得
        /// UseWorldPositions=trueの場合はワールド座標、falseの場合はローカル座標を返す
        /// </summary>
        public Vector3[] GetDisplayPositions()
        {
            if (UseWorldPositions && _worldPositions != null && _worldPositions.Length >= _totalVertexCount)
            {
                return _worldPositions;
            }
            return _positions;
        }

        /// <summary>
        /// GPU が ComputeScreenPositions で計算したスクリーン座標配列を返す。
        ///
        /// 【用途】
        ///   矩形選択（CommitBoxSelect）で頂点のスクリーン座標と矩形の交差判定に使う。
        ///   ReadBackVertexFlags() の後に呼ぶことで背面カリング情報と組み合わせられる。
        ///
        /// 【配列インデックス】
        ///   グローバル頂点インデックス。メッシュのローカルインデックスではない。
        ///   GetVertexOffset(meshIndex) で得たオフセットを足して参照すること。
        ///
        /// 【座標系】
        ///   ComputeScreenPositions / ComputeScreenPositionsGPU と同じ座標系。
        ///   UpdateFrame に渡した viewport・カメラパラメータに対応する。
        /// </summary>
        public Vector2[] GetScreenPositions() => _screenPositions;
        
        /// <summary>
        /// ワールド座標を描画に使用するか
        /// </summary>
        public bool UseWorldPositions { get; set; } = false;
        
        public uint[] VertexFlags => _vertexFlags;

        /// <summary>
        /// 頂点カリング結果の CPU キャッシュ。ReadBackVertexCulled(slot) で埋める。
        /// 各 uint は 0=可視 / 非 0=カリング済み (表面の面に属さない)。
        /// </summary>
        public uint[] VertexCulled => _vertexCulledCache;

        /// <summary>面カリング結果の CPU キャッシュ。ReadBackFaceCulled(slot) で埋める。</summary>
        public uint[] FaceCulled => _faceCulledCache;
        public uint[] LineFlags => _lineFlags;
        public UnifiedLine[] Lines => _lines;
        public MeshInfo[] MeshInfos => _meshInfos;
        // FaceHitResults / FaceHitDepths も呼出元 0 件のため撤去した（2026-08-28）。
        // Phase 2c: 面塗り overlay の CPU mesh 構築で参照。
        public UnifiedFace[] Faces => _faces;
        public uint[] FaceFlags => _faceFlags;
        public uint[] Indices => _indices;

        // ------------------------------------------------------------
        // 法線表示の CPU mesh 構築で参照するスキニングデータ。
        //
        // 【用途】
        //   MeshSceneRenderer.PrepareNormals が、頂点ごとのスキニング行列を
        //   UnifiedCompute.compute の TransformVertices カーネルと同じ式で
        //   組み立て、ローカル法線をワールドへ回すために使う。
        //     skinMatrix = Σ TransformMatrices[BoneIndices[i][k]] * BoneWeights[i][k]
        //   非スキン頂点は BoneWeights=(1,0,0,0) / BoneIndices.x=contextIndex が
        //   入っているため、同じ式で行列 1 個の参照に帰着する。
        //
        // 【配列インデックス】
        //   BoneWeights / BoneIndices はグローバル頂点インデックス。
        //   TransformMatrices は MeshContextList のインデックス
        //   （UpdateTransformMatrices が MeshContextList 順に格納する）。
        // ------------------------------------------------------------

        /// <summary>メッシュコンテキスト順の変換行列（読み取り専用参照）。</summary>
        public Matrix4x4[] TransformMatrices => _transformMatrices;

        /// <summary>頂点ごとのボーンウェイト（読み取り専用参照）。</summary>
        public Vector4[] BoneWeights => _boneWeights;

        /// <summary>頂点ごとのボーンインデックス（読み取り専用参照）。</summary>
        public UInt4[] BoneIndices => _boneIndices;

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

            System.Threading.Interlocked.Increment(ref _liveCount);
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
            _worldPositions = new Vector3[_vertexCapacity];
            _normals = new Vector3[_vertexCapacity];
            _uvs = new Vector2[_vertexCapacity];
            _vertexFlags = new uint[_vertexCapacity];
            _mirrorPositions = new Vector3[_vertexCapacity];
            _vertexMeshIndices = new uint[_vertexCapacity];
            _boneWeights = new Vector4[_vertexCapacity];
            _boneIndices = new UInt4[_vertexCapacity];
            _mirrorBoneWeights = new Vector4[_vertexCapacity];
            _mirrorBoneIndices = new UInt4[_vertexCapacity];

            _lines = new UnifiedLine[_lineCapacity];
            _lineFlags = new uint[_lineCapacity];

            _faces = new UnifiedFace[_faceCapacity];
            _faceFlags = new uint[_faceCapacity];

            _indices = new uint[_indexCapacity];

            _meshInfos = new MeshInfo[256];
            _meshExpandedStart = new uint[256];
            _meshExpandedCount = new uint[256];
            _modelInfos = new ModelInfo[16];
            _transformMatrices = new Matrix4x4[256];

            _cameraInfo = new CameraInfo[1];
            _screenPositions = new Vector2[_vertexCapacity];

            _hitTestInput = new HitTestInput[1];
            _hitVertexDistances = new float[_vertexCapacity];
            _snapHitVertexDistances = new float[_vertexCapacity];
            _hitLineDistances = new float[_lineCapacity];
            _faceHit = new Vector2[_faceCapacity];

            _bounds = new AABB[256];

            // float4スクリーン座標（GPU用）
            _screenPositions4 = new Vector4[_vertexCapacity];

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
                _kernelVertexSnapHit = _computeShader.FindKernel("ComputeVertexSnapHitTest");
                _kernelLineHit = _computeShader.FindKernel("ComputeLineHitTest");
                _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
                _kernelLineVisibility = _computeShader.FindKernel("ComputeLineVisibility");
                _kernelFaceHit = _computeShader.FindKernel("ComputeFaceHitTest");
                _kernelUpdateHover    = _computeShader.FindKernel("UpdateHoverFlags");
                _kernelClearCulled    = _computeShader.FindKernel("ClearCulledBuffers");
                _kernelClearFaceCulled= _computeShader.FindKernel("ClearFaceCulledBuffers");
                _kernelApplyMirrorCull= _computeShader.FindKernel("ApplyMirrorCull");
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
            Poly_Ling.Diagnostics.PLResStat.LiveBufSet++;
            Poly_Ling.Diagnostics.PLResStat.Report("CreateAllBuffers");

            // Level 5: Topology
            _positionBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 3));
            _normalBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 3));
            _uvBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 2));
            _indexBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_indexCapacity, sizeof(uint)));
            _lineBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_lineCapacity, UnifiedLine.Stride));
            _faceBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_faceCapacity, UnifiedFace.Stride));
            _meshInfoBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_meshInfos.Length, MeshInfo.Stride));
            _modelInfoBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(16, ModelInfo.Stride));

            // Level 4: Transform
            _boundsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_meshInfos.Length, AABB.Stride));
            _mirrorPositionBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 3));
            _skinnedMirrorPositionBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 3));
            _worldPositionBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 3));
            _transformMatrixBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(Mathf.Max(1, _meshInfos.Length), sizeof(float) * 16));
            _vertexMeshIndexBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(uint)));
            _boneWeightsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 4));
            _boneIndicesBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, UInt4.Stride));
            _mirrorBoneWeightsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 4));
            _mirrorBoneIndicesBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, UInt4.Stride));

            // Level 3: Selection
            _vertexFlagsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(uint)));
            _lineFlagsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_lineCapacity, sizeof(uint)));
            _faceFlagsBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_faceCapacity, sizeof(uint)));

            // Level 2: Camera
            _cameraBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(1, CameraInfo.Stride));
            // スクリーン座標の実体は per-slot バッファ（下の _slotScreenPosBufs）だけ。
            // 旧 _screenPosBuffer4 / _mirrorScreenPosBuffer4 は撤去した（理由は宣言部）。

            // Level 1: Mouse
            _hitTestInputBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(1, HitTestInput.Stride));
            _hitVertexDistBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float)));
            _snapHitVertexDistBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float)));
            _hitLineDistBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_lineCapacity, sizeof(float)));
            // float2（x=ヒット, y=深度）。旧 2 本を 1 本に統合。stride は 8。
            _faceHitBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_faceCapacity, sizeof(float) * 2));

            // per-slot カリングバッファ
            _slotScreenPosBufs    = new ComputeBuffer[CullingSlotCount];
            _slotVertexCulledBufs = new ComputeBuffer[CullingSlotCount];
            _slotLineCulledBufs   = new ComputeBuffer[CullingSlotCount];
            _slotFaceCulledBufs   = new ComputeBuffer[CullingSlotCount];
            for (int s = 0; s < CullingSlotCount; s++)
            {
                _slotScreenPosBufs[s]    = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(float) * 4));
                _slotVertexCulledBufs[s] = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_vertexCapacity, sizeof(uint)));
                _slotLineCulledBufs[s]   = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_lineCapacity,   sizeof(uint)));
                _slotFaceCulledBufs[s]   = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_faceCapacity,   sizeof(uint)));
            }
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
                // 展開範囲は _meshInfos と同じ添字で引く。必ず同じ長さに保つこと。
                Array.Resize(ref _meshExpandedStart, newSize);
                Array.Resize(ref _meshExpandedCount, newSize);
                
                // GPUバッファも再作成
                if (_meshInfoBuffer != null) Poly_Ling.Diagnostics.PLResStat.LiveCB--;
                _meshInfoBuffer?.Release();
                _meshInfoBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(newSize, MeshInfo.Stride));
            }

            if (needsRebuild)
            {
                ResizeBuffers();
            }
        }

        // UV展開バッファ用の容量
        private int _expandedVertexCapacity = 0;

        /// <summary>
        /// UV展開用バッファの容量を確保
        /// </summary>
        public void EnsureExpandedCapacity(int expandedVertexCount)
        {
            if (expandedVertexCount <= _expandedVertexCapacity)
                return;

            _expandedVertexCapacity = Mathf.NextPowerOfTwo(expandedVertexCount);

            // CPU配列
            Array.Resize(ref _expandedToOriginal, _expandedVertexCapacity);

            // GPUバッファ再作成
            ReleaseBuffer(ref _expandedToOriginalBuffer);
            ReleaseBuffer(ref _expandedPositionBuffer);
            ReleaseBuffer(ref _expandedNormalBuffer);

            _expandedToOriginalBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_expandedVertexCapacity, sizeof(uint)));
            _expandedPositionBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_expandedVertexCapacity, sizeof(float) * 3));
            _expandedNormalBuffer = Poly_Ling.Diagnostics.PLResStat.NewCB(new ComputeBuffer(_expandedVertexCapacity, sizeof(float) * 3));

            //Debug.Log($"[EnsureExpandedCapacity] Resized to {_expandedVertexCapacity} (requested: {expandedVertexCount})");
        }

        /// <summary>
        /// バッファサイズを変更
        /// </summary>
        private void ResizeBuffers()
        {
            // CPU配列リサイズ
            Array.Resize(ref _positions, _vertexCapacity);
            Array.Resize(ref _worldPositions, _vertexCapacity);
            Array.Resize(ref _normals, _vertexCapacity);
            Array.Resize(ref _uvs, _vertexCapacity);
            Array.Resize(ref _vertexFlags, _vertexCapacity);
            Array.Resize(ref _mirrorPositions, _vertexCapacity);
            Array.Resize(ref _vertexMeshIndices, _vertexCapacity);
            Array.Resize(ref _boneWeights, _vertexCapacity);
            Array.Resize(ref _boneIndices, _vertexCapacity);
            Array.Resize(ref _mirrorBoneWeights, _vertexCapacity);
            Array.Resize(ref _mirrorBoneIndices, _vertexCapacity);
            Array.Resize(ref _screenPositions, _vertexCapacity);
            Array.Resize(ref _screenPositions4, _vertexCapacity);
            Array.Resize(ref _hitVertexDistances, _vertexCapacity);
            Array.Resize(ref _snapHitVertexDistances, _vertexCapacity);

            Array.Resize(ref _lines, _lineCapacity);
            Array.Resize(ref _lineFlags, _lineCapacity);
            Array.Resize(ref _hitLineDistances, _lineCapacity);

            Array.Resize(ref _faces, _faceCapacity);
            Array.Resize(ref _faceFlags, _faceCapacity);
            Array.Resize(ref _faceHit, _faceCapacity);

            Array.Resize(ref _indices, _indexCapacity);

            // zeros キャッシュ（ClearCulledFlagsGPU 用）を再確保
            // Array.Resize は拡張時に新規要素をゼロ初期化するため追記分は常にゼロ
            Array.Resize(ref _zeroVertexCache, _vertexCapacity);
            Array.Resize(ref _zeroLineCache,   _lineCapacity);
            Array.Resize(ref _zeroFaceCache,   _faceCapacity);

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
            Poly_Ling.Diagnostics.PLResStat.LiveBufSet--;
            Poly_Ling.Diagnostics.PLResStat.Report("ReleaseAllBuffers");

            ReleaseBuffer(ref _positionBuffer);
            ReleaseBuffer(ref _worldPositionBuffer);
            ReleaseBuffer(ref _expandedToOriginalBuffer);
            ReleaseBuffer(ref _expandedPositionBuffer);
            ReleaseBuffer(ref _expandedNormalBuffer);
            ReleaseBuffer(ref _transformMatrixBuffer);
            ReleaseBuffer(ref _vertexMeshIndexBuffer);
            ReleaseBuffer(ref _boneWeightsBuffer);
            ReleaseBuffer(ref _boneIndicesBuffer);
            ReleaseBuffer(ref _mirrorBoneWeightsBuffer);
            ReleaseBuffer(ref _mirrorBoneIndicesBuffer);
            ReleaseBuffer(ref _normalBuffer);
            ReleaseBuffer(ref _uvBuffer);
            ReleaseBuffer(ref _indexBuffer);
            ReleaseBuffer(ref _lineBuffer);
            ReleaseBuffer(ref _faceBuffer);
            ReleaseBuffer(ref _meshInfoBuffer);
            ReleaseBuffer(ref _modelInfoBuffer);
            ReleaseBuffer(ref _boundsBuffer);
            ReleaseBuffer(ref _mirrorPositionBuffer);
            ReleaseBuffer(ref _skinnedMirrorPositionBuffer);
            ReleaseBuffer(ref _vertexFlagsBuffer);
            ReleaseBuffer(ref _lineFlagsBuffer);
            ReleaseBuffer(ref _faceFlagsBuffer);
            ReleaseBuffer(ref _cameraBuffer);
            ReleaseBuffer(ref _hitTestInputBuffer);
            ReleaseBuffer(ref _hitVertexDistBuffer);
            ReleaseBuffer(ref _snapHitVertexDistBuffer);
            ReleaseBuffer(ref _hitLineDistBuffer);
            ReleaseBuffer(ref _faceHitBuffer);

            // per-slot カリングバッファ
            if (_slotScreenPosBufs != null)
                for (int s = 0; s < _slotScreenPosBufs.Length; s++)
                    ReleaseBuffer(ref _slotScreenPosBufs[s]);
            if (_slotVertexCulledBufs != null)
                for (int s = 0; s < _slotVertexCulledBufs.Length; s++)
                    ReleaseBuffer(ref _slotVertexCulledBufs[s]);
            if (_slotLineCulledBufs != null)
                for (int s = 0; s < _slotLineCulledBufs.Length; s++)
                    ReleaseBuffer(ref _slotLineCulledBufs[s]);
            if (_slotFaceCulledBufs != null)
                for (int s = 0; s < _slotFaceCulledBufs.Length; s++)
                    ReleaseBuffer(ref _slotFaceCulledBufs[s]);
        }

        private void ReleaseBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                Poly_Ling.Diagnostics.PLResStat.LiveCB--;
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

            // 展開範囲も無効化する。_meshCount = 0 で TryGetExpandedRange は
            // false を返すが、古い値が残っていると再構築の途中で
            // 読まれたときに前のモデルの範囲を返してしまう。
            _totalExpandedVertexCount = 0;
            if (_meshExpandedStart != null) Array.Clear(_meshExpandedStart, 0, _meshExpandedStart.Length);
            if (_meshExpandedCount != null) Array.Clear(_meshExpandedCount, 0, _meshExpandedCount.Length);
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
                    _meshExpandedStart = null;
                    _meshExpandedCount = null;
                    _modelInfos = null;
                }

                System.Threading.Interlocked.Decrement(ref _liveCount);

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
