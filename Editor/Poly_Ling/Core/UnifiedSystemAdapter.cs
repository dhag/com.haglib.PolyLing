// Assets/Editor/Poly_Ling/Core/UnifiedSystemAdapter.cs
// 統合システムアダプター
// SimpleMeshFactoryとUnifiedMeshSystemを接続するブリッジクラス

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Rendering;
using Poly_Ling.Core.Rendering;

namespace Poly_Ling.Core
{
    /// <summary>
    /// 統合システムアダプター
    /// 既存のSimpleMeshFactoryとUnifiedMeshSystemを段階的に統合
    /// </summary>
    public class UnifiedSystemAdapter : IDisposable
    {
        // ============================================================
        // コンポーネント
        // ============================================================

        private UnifiedMeshSystem _unifiedSystem;
        private UnifiedRenderer _renderer;

        // ============================================================
        // 外部参照
        // ============================================================

        private ModelContext _modelContext;
        private SelectionState _selectionState;
        private SymmetrySettings _symmetrySettings;

        // ============================================================
        // 状態
        // ============================================================

        private bool _isInitialized = false;
        private bool _disposed = false;
        private bool _useUnifiedRendering = false; // 統合レンダリング使用フラグ
        private bool _skipHitTest = true;  // ヒットテストをスキップ（カメラ操作中など）
        private bool _skipVertexFlagsReadback = true;  // 頂点フラグ読み戻しをスキップ
        private bool _skipGpuVisibilityCompute = false;  // GPU可視性計算をスキップ
        private bool _skipUnselectedWireframe = false;  // 非選択ワイヤーフレーム描画をスキップ
        private bool _skipUnselectedVertices = false;   // 非選択頂点描画をスキップ
        // 【重要】カメラ操作中はtrueにすること。falseのままだと毎フレームメッシュ再構築が走る
        private bool _skipMeshRebuild = false;          // メッシュ再構築をスキップ（カメラ操作中）

        // クワッドメッシュ（頂点描画用）
        private Mesh _quadMesh;

        // ============================================================
        // プロパティ
        // ============================================================

        public bool IsInitialized => _isInitialized;
        public bool UseUnifiedRendering
        {
            get => _useUnifiedRendering;
            set => _useUnifiedRendering = value;
        }

        /// <summary>
        /// ヒットテストをスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipHitTest
        {
            get => _skipHitTest;
            set => _skipHitTest = value;
        }

        /// <summary>
        /// 頂点フラグ読み戻しをスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipVertexFlagsReadback
        {
            get => _skipVertexFlagsReadback;
            set => _skipVertexFlagsReadback = value;
        }

        /// <summary>
        /// GPU可視性計算をスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipGpuVisibilityCompute
        {
            get => _skipGpuVisibilityCompute;
            set => _skipGpuVisibilityCompute = value;
        }

        /// <summary>
        /// 非選択ワイヤーフレーム描画をスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipUnselectedWireframe
        {
            get => _skipUnselectedWireframe;
            set => _skipUnselectedWireframe = value;
        }

        /// <summary>
        /// 非選択頂点描画をスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipUnselectedVertices
        {
            get => _skipUnselectedVertices;
            set => _skipUnselectedVertices = value;
        }

        /// <summary>
        /// メッシュ再構築をスキップするか（カメラ操作中など）
        /// </summary>
        public bool SkipMeshRebuild
        {
            get => _skipMeshRebuild;
            set => _skipMeshRebuild = value;
        }

        /// <summary>
        /// 背面カリングを有効にするか
        /// </summary>
        public bool BackfaceCullingEnabled
        {
            get => _renderer?.BackfaceCullingEnabled ?? true;
            set
            {
                if (_renderer != null) _renderer.BackfaceCullingEnabled = value;
                if (_unifiedSystem != null) _unifiedSystem.BackfaceCullingEnabled = value;
            }
        }

        public UnifiedMeshSystem UnifiedSystem => _unifiedSystem;
        public UnifiedRenderer Renderer => _renderer;
        public UnifiedBufferManager BufferManager => _unifiedSystem?.BufferManager;

        // 既存システムとの互換用（グローバルインデックス）
        public int HoverVertexIndex => _unifiedSystem?.HoveredVertexIndex ?? -1;
        public int HoverLineIndex => _unifiedSystem?.HoveredLineIndex ?? -1;
        public int HoverFaceIndex => _unifiedSystem?.HoveredFaceIndex ?? -1;

        /// <summary>
        /// 指定メッシュのローカル頂点ホバーインデックスを取得
        /// </summary>
        public int GetLocalHoverVertexIndex(int meshIndex)
        {
            int globalIndex = HoverVertexIndex;
            if (globalIndex < 0) return -1;

            if (BufferManager?.GlobalToLocalVertexIndex(globalIndex, out int meshIdx, out int localIdx) == true)
            {
                if (meshIdx == meshIndex)
                    return localIdx;
            }
            return -1;
        }

        /// <summary>
        /// 指定メッシュのローカル線分ホバーインデックスを取得
        /// グローバルLineIndexを返す（EdgeCacheはグローバルリスト）
        /// ただし指定メッシュの線分でない場合は-1
        /// </summary>
        public int GetLocalHoverLineIndex(int meshIndex)
        {
            int globalIndex = HoverLineIndex;
            if (globalIndex < 0) return -1;

            if (BufferManager?.GlobalToLocalLineIndex(globalIndex, out int meshIdx, out int localIdx) == true)
            {
                if (meshIdx == meshIndex)
                    return globalIndex;  // EdgeCacheはグローバルリストなのでグローバルインデックスを返す
            }
            return -1;
        }

        /// <summary>
        /// 指定メッシュのローカル面ホバーインデックスを取得
        /// </summary>
        public int GetLocalHoverFaceIndex(int meshIndex)
        {
            int globalIndex = HoverFaceIndex;
            if (globalIndex < 0) return -1;

            if (BufferManager?.GlobalToLocalFaceIndex(globalIndex, out int meshIdx, out int localIdx) == true)
            {
                if (meshIdx == meshIndex)
                    return localIdx;
            }
            return -1;
        }

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UnifiedSystemAdapter()
        {
            _unifiedSystem = new UnifiedMeshSystem();
            _renderer = new UnifiedRenderer(_unifiedSystem.BufferManager);
        }

        // ============================================================
        // 初期化
        // ============================================================

        /// <summary>
        /// アダプターを初期化
        /// </summary>
        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            _unifiedSystem.Initialize();

            if (!_renderer.Initialize())
            {
                Debug.LogWarning("[UnifiedSystemAdapter] Failed to initialize renderer");
                return false;
            }

            // クワッドメッシュを作成（頂点描画用）
            CreateQuadMesh();

            _isInitialized = true;
            return true;
        }

        /// <summary>
        /// クワッドメッシュを作成
        /// </summary>
        private void CreateQuadMesh()
        {
            _quadMesh = new Mesh();
            _quadMesh.name = "UnifiedPointQuad";

            // 単位正方形（中心原点）
            _quadMesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };

            _quadMesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };

            _quadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            _quadMesh.RecalculateNormals();
        }

        // ============================================================
        // 参照設定
        // ============================================================

        /// <summary>
        /// モデルコンテキストを設定
        /// </summary>
        public void SetModelContext(ModelContext modelContext)
        {
            _modelContext = modelContext;

            _unifiedSystem.SetModel(modelContext);

            // 即座にトポロジー更新を実行（遅延せずにバッファを構築）
            _unifiedSystem.ExecuteUpdates(DirtyLevel.Topology);
        }

        /// <summary>
        /// 選択状態を設定
        /// </summary>
        public void SetSelectionState(SelectionState selectionState)
        {
            _selectionState = selectionState;
            _unifiedSystem.SetSelectionState(selectionState);
        }

        /// <summary>
        /// 対称設定を設定
        /// </summary>
        public void SetSymmetrySettings(SymmetrySettings settings)
        {
            _symmetrySettings = settings;
            _unifiedSystem.SetSymmetrySettings(settings);
        }

        /// <summary>
        /// アクティブメッシュを設定
        /// meshIndexはMeshContexts配列のインデックス（ボーン含む）
        /// 内部でUnifiedMeshインデックスに変換される
        /// </summary>
        public void SetActiveMesh(int modelIndex, int contextIndex)
        {
            // MeshContextインデックス → UnifiedMeshインデックスに変換
            int unifiedMeshIndex = BufferManager?.ContextToUnifiedMeshIndex(contextIndex) ?? contextIndex;
            _unifiedSystem.SetActiveMesh(modelIndex, unifiedMeshIndex);
        }

        /// <summary>
        /// MeshContextインデックスをUnifiedMeshインデックスに変換
        /// </summary>
        public int ContextToUnifiedMeshIndex(int contextIndex)
        {
            return BufferManager?.ContextToUnifiedMeshIndex(contextIndex) ?? -1;
        }

        // ============================================================
        // 更新通知
        // ============================================================

        /// <summary>
        /// トポロジー変更を通知
        /// </summary>
        public void NotifyTopologyChanged()
        {
            _unifiedSystem.NotifyTopologyChanged();
            // 即座にトポロジー更新を実行
            _unifiedSystem.ExecuteUpdates(DirtyLevel.Topology);
        }

        /// <summary>
        /// 位置変更を通知
        /// </summary>
        public void NotifyTransformChanged()
        {
            _unifiedSystem.NotifyTransformChanged();
        }

        /// <summary>
        /// 選択変更を通知
        /// </summary>
        public void NotifySelectionChanged()
        {
            _unifiedSystem.NotifySelectionChanged();
        }

        // ============================================================
        // フレーム更新
        // ============================================================

        /// <summary>
        /// フレーム更新
        /// </summary>
        public void UpdateFrame(
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            float fov,
            Rect viewport,
            Vector2 mousePosition,
            float rotationZ = 0f)
        {
            if (!_isInitialized)
                return;

            // カメラドラッグ中は全てスキップ（描画はシェーダーがカメラ行列で変換）
            if (_skipHitTest)
                return;

            _unifiedSystem.BeginFrame();

            // カメラ更新
            _unifiedSystem.UpdateCamera(cameraPosition, cameraTarget, fov, viewport, rotationZ);

            // マウス更新
            _unifiedSystem.UpdateMousePosition(mousePosition);

            // 更新実行
            DirtyLevel level = _unifiedSystem.ProcessUpdates();
            _unifiedSystem.ExecuteUpdates(level);

            _unifiedSystem.EndFrame();
        }

        /// <summary>
        /// 変換行列を更新してGPUで頂点変換を実行
        /// </summary>
        /// <param name="useWorldTransform">ワールド変換を使用するか</param>
        public void UpdateTransform(bool useWorldTransform)
        {
            if (!_isInitialized || _modelContext == null)
                return;

            var bufferManager = _unifiedSystem?.BufferManager;
            if (bufferManager == null)
                return;

            // 変換行列をGPUにアップロード
            bufferManager.UpdateTransformMatrices(_modelContext.MeshContextList, useWorldTransform);

            // TransformVerticesカーネルを実行
            // ReadBackは必要（ワイヤフレーム・頂点描画がGetDisplayPositions()を使うため）
            bufferManager.DispatchTransformVertices(useWorldTransform, false, readbackToCPU: true);

            // UV展開済み頂点を生成（面シェーダ描画用）
            bufferManager.DispatchExpandVertices(transformNormals: false);
        }

        /// <summary>
        /// GPU変換後の頂点をUnityMeshに設定
        /// 展開済み頂点バッファから一度ReadBackして各メッシュに配分
        /// </summary>
        public void WritebackTransformedVertices()
        {
            if (!_isInitialized || _modelContext == null)
                return;

            var bufferManager = _unifiedSystem?.BufferManager;
            if (bufferManager == null)
                return;

            var meshContextList = _modelContext.MeshContextList;
            if (meshContextList == null)
                return;

            int totalExpandedCount = bufferManager.TotalExpandedVertexCount;
            if (totalExpandedCount == 0)
            {
                // 展開バッファが未構築の場合はフォールバック
                WritebackTransformedVerticesFallback();
                return;
            }

            // 展開済み頂点をGPUから一度だけReadBack
            var expandedPositions = bufferManager.GetExpandedPositions();
            if (expandedPositions == null || expandedPositions.Length == 0)
            {
                WritebackTransformedVerticesFallback();
                return;
            }

            // 展開済み頂点のオフセットを追跡
            int expandedOffset = 0;

            // 各MeshContextのUnityMeshを更新
            for (int ctxIdx = 0; ctxIdx < meshContextList.Count; ctxIdx++)
            {
                var ctx = meshContextList[ctxIdx];
                if (ctx?.MeshObject == null)
                    continue;

                // ボーンはスキップ
                if (ctx.Type == MeshType.Bone)
                    continue;

                var meshObject = ctx.MeshObject;

                // このメッシュの展開後頂点数を計算
                int expandedVertexCount = 0;
                foreach (var vertex in meshObject.Vertices)
                {
                    int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                    expandedVertexCount += uvCount;
                }

                if (expandedVertexCount == 0)
                    continue;

                // 境界チェック: expandedPositions配列の範囲外アクセスを防ぐ
                if (expandedOffset + expandedVertexCount > expandedPositions.Length)
                {
                    // バッファサイズ不整合 - フォールバック処理へ
                    Debug.LogWarning($"[WritebackTransformedVertices] Buffer size mismatch for mesh '{ctx.Name}': " +
                        $"offset={expandedOffset}, count={expandedVertexCount}, bufferSize={expandedPositions.Length}. " +
                        $"Skipping this mesh.");
                    expandedOffset += expandedVertexCount;
                    continue;
                }

                // UnityMeshが存在し、頂点数が一致する場合は位置のみ更新
                var unityMesh = ctx.UnityMesh;
                if (unityMesh != null && unityMesh.vertexCount == expandedVertexCount)
                {
                    // NativeArrayを使ってコピー（GC削減）
                    var nativeArray = new Unity.Collections.NativeArray<Vector3>(
                        expandedVertexCount,
                        Unity.Collections.Allocator.Temp,
                        Unity.Collections.NativeArrayOptions.UninitializedMemory);

                    Unity.Collections.NativeArray<Vector3>.Copy(expandedPositions, expandedOffset, nativeArray, 0, expandedVertexCount);

                    unityMesh.SetVertices(nativeArray);
                    unityMesh.RecalculateBounds();

                    nativeArray.Dispose();
                }
                else
                {
                    // UnityMeshが存在しないか頂点数が違う場合は再生成
                    var meshInfos = bufferManager.MeshInfos;
                    int unifiedMeshIdx = bufferManager.ContextToUnifiedMeshIndex(ctxIdx);
                    if (unifiedMeshIdx >= 0 && meshInfos != null)
                    {
                        var meshInfo = meshInfos[unifiedMeshIdx];
                        int vertexStart = (int)meshInfo.VertexStart;
                        var worldPositions = bufferManager.GetWorldPositions();
                        if (worldPositions != null)
                        {
                            for (int i = 0; i < meshObject.VertexCount && (vertexStart + i) < worldPositions.Length; i++)
                            {
                                meshObject.Vertices[i].Position = worldPositions[vertexStart + i];
                            }
                        }
                    }
                    ctx.UnityMesh = meshObject.ToUnityMesh();
                }

                expandedOffset += expandedVertexCount;
            }
        }

        /// <summary>
        /// フォールバック: 従来のCPU経由の書き戻し
        /// </summary>
        private void WritebackTransformedVerticesFallback()
        {
            var bufferManager = _unifiedSystem?.BufferManager;
            if (bufferManager == null)
                return;

            var meshContextList = _modelContext.MeshContextList;
            if (meshContextList == null)
                return;

            // GPU変換後の頂点座標を取得（CPU ReadBack）
            var worldPositions = bufferManager.GetWorldPositions();
            if (worldPositions == null || worldPositions.Length == 0)
                return;

            var meshInfos = bufferManager.MeshInfos;
            if (meshInfos == null)
                return;

            // 各MeshContextの頂点を書き戻す
            for (int ctxIdx = 0; ctxIdx < meshContextList.Count; ctxIdx++)
            {
                var ctx = meshContextList[ctxIdx];
                if (ctx?.MeshObject == null)
                    continue;

                if (ctx.Type == MeshType.Bone)
                    continue;

                int unifiedMeshIdx = bufferManager.ContextToUnifiedMeshIndex(ctxIdx);
                if (unifiedMeshIdx < 0)
                    continue;

                var meshInfo = meshInfos[unifiedMeshIdx];
                int vertexStart = (int)meshInfo.VertexStart;
                int vertexCount = (int)meshInfo.VertexCount;

                if (vertexCount == 0)
                    continue;

                var meshObject = ctx.MeshObject;
                if (meshObject.VertexCount != vertexCount)
                    continue;

                // MeshObject.Verticesに書き戻し
                for (int i = 0; i < vertexCount; i++)
                {
                    int globalIdx = vertexStart + i;
                    if (globalIdx < worldPositions.Length)
                    {
                        meshObject.Vertices[i].Position = worldPositions[globalIdx];
                    }
                }

                // UnityMeshを再生成
                ctx.UnityMesh = meshObject.ToUnityMesh();
            }
        }

        // ============================================================
        // 描画
        // ============================================================
        /*
        /// <summary>
        /// メッシュを構築してキューに追加（後方互換用オーバーロード）
        /// </summary>
        public void PrepareDrawing(Camera camera, bool showWireframe, bool showVertices, float pointSize = 0.02f, float alpha = 1f)
        {
            // 全メッシュ描画（後方互換）
            PrepareDrawing(camera, showWireframe, showVertices, true, true, -1, pointSize, alpha);
        }
        */
        /// <summary>
        /// メッシュを構築してキューに追加（選択/非選択フィルタリング対応）
        /// </summary>
        /// <param name="camera">カメラ</param>
        /// <param name="showWireframe">ワイヤーフレームを表示するか</param>
        /// <param name="showVertices">頂点を表示するか</param>
        /// <param name="showUnselectedWireframe">非選択メッシュのワイヤーフレームを表示するか</param>
        /// <param name="showUnselectedVertices">非選択メッシュの頂点を表示するか</param>
        /// <param name="selectedMeshIndex">選択メッシュインデックス（-1で全選択扱い）</param>
        /// <param name="pointSize">頂点サイズ</param>
        /// <param name="alpha">アルファ値</param>
        public void PrepareDrawing(
            Camera camera,
            bool showWireframe,
            bool showVertices,
            bool showUnselectedWireframe,
            bool showUnselectedVertices,
            int selectedMeshIndex,
            float pointSize,
            float alpha = 1f)
        {
            if (!_isInitialized)
            {
                return;
            }

            // カメラ操作中はメッシュ再構築をスキップ（キャッシュされたメッシュを使用）
            bool rebuildMesh = !_skipMeshRebuild;

            // メッシュ構築
            if (showWireframe)
            {
                if (rebuildMesh)
                {
                    // UpdateWireframeMesh(selectedMeshIndex, showUnselected, cam, lineWidthPx, selectedAlpha, unselectedAlpha)
                    _renderer.UpdateWireframeMesh(
                        selectedMeshIndex,
                        showUnselectedWireframe,
                        camera,
                        1.0f,       // lineWidthPx
                        alpha,      // selectedAlpha
                        0.4f);      // unselectedAlpha
                }
                // v2.1: 非選択表示フラグを渡す
                _renderer.QueueWireframe(showUnselectedWireframe);
            }

            if (showVertices)
            {
                if (rebuildMesh)
                {
                    // UpdatePointMesh(camera, selectedMeshIndex, showUnselected, pointSize, selectedAlpha, unselectedAlpha)
                    _renderer.UpdatePointMesh(
                        camera,
                        selectedMeshIndex,
                        showUnselectedVertices,
                        pointSize,
                        alpha,      // selectedAlpha
                        0.4f);      // unselectedAlpha
                }
                // v2.1: 非選択表示フラグを渡す
                _renderer.QueuePoints(showUnselectedVertices);
            }
        }

        /// <summary>
        /// キューに入っているメッシュを描画
        /// </summary>
        public void DrawQueued(PreviewRenderUtility preview)
        {
            if (!_isInitialized)
                return;

            _renderer.DrawQueued(preview);
        }

        /// <summary>
        /// 描画後のクリーンアップ
        /// </summary>
        public void CleanupQueued()
        {
            if (!_isInitialized)
                return;

            _renderer.CleanupQueued();
        }

        /// <summary>
        /// 旧API互換（非推奨）
        /// </summary>
        [Obsolete("Use PrepareDrawing + DrawQueued + CleanupQueued instead")]
        public void Draw(Matrix4x4 modelMatrix, Camera camera = null)
        {
            // 旧APIは機能しない
        }

        // ============================================================
        // ヒットテスト
        // ============================================================

        /// <summary>
        /// v2.1: 指定メッシュの頂点オフセット（グローバルインデックス）を取得
        /// </summary>
        public int GetVertexOffset(int meshIndex)
        {
            if (!_isInitialized || BufferManager == null)
                return 0;
                
            int unifiedMeshIndex = ContextToUnifiedMeshIndex(meshIndex);
            if (unifiedMeshIndex < 0)
                return 0;
                
            var meshInfos = BufferManager.MeshInfos;
            if (meshInfos == null || unifiedMeshIndex >= meshInfos.Length)
                return 0;
                
            return (int)meshInfos[unifiedMeshIndex].VertexStart;
        }

        /// <summary>
        /// 頂点ヒットテスト
        /// </summary>
        public int FindNearestVertex(Vector2 screenPos, float radius)
        {
            if (!_isInitialized)
                return -1;

            int globalIndex = _unifiedSystem.FindVertexAtScreenPos(screenPos, radius);

            if (globalIndex >= 0)
            {
                // グローバルインデックスをローカルに変換
                if (_unifiedSystem.GlobalToLocal(globalIndex, out int meshIndex, out int localIndex))
                {
                    // アクティブメッシュの頂点のみ返す（既存動作と互換）
                    if (meshIndex == _unifiedSystem.ActiveMeshIndex)
                    {
                        return localIndex;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// ラインヒットテスト
        /// </summary>
        public int FindNearestLine(Vector2 screenPos, float radius)
        {
            if (!_isInitialized)
                return -1;

            int globalIndex = _unifiedSystem.FindLineAtScreenPos(screenPos, radius);

            // TODO: グローバル→ローカル変換
            return globalIndex;
        }

        /// <summary>
        /// ホバー頂点のローカルインデックスを取得
        /// </summary>
        public int GetHoveredVertexLocal()
        {
            if (!_isInitialized)
                return -1;

            if (_unifiedSystem.GetHoveredVertexLocal(out int meshIndex, out int localIndex))
            {
                if (meshIndex == _unifiedSystem.ActiveMeshIndex)
                {
                    return localIndex;
                }
            }

            return -1;
        }

        // ============================================================
        // 色設定
        // ============================================================

        /// <summary>
        /// 色設定を取得
        /// </summary>
        public ShaderColorSettings ColorSettings => _renderer?.ColorSettings;

        /// <summary>
        /// 色設定を変更
        /// </summary>
        public void SetColorSettings(ShaderColorSettings settings)
        {
            _renderer?.SetColorSettings(settings);
        }

        // ============================================================
        // カリング
        // ============================================================

        /// <summary>
        /// 指定メッシュのローカル頂点がカリング（背面）されているかを取得
        /// </summary>
        /// <param name="meshIndex">メッシュインデックス</param>
        /// <param name="localVertexIndex">ローカル頂点インデックス</param>
        /// <returns>カリングされている場合true</returns>
        public bool IsVertexCulled(int meshIndex, int localVertexIndex)
        {
            var bufferManager = _unifiedSystem?.BufferManager;
            if (!_isInitialized || bufferManager == null)
                return false;

            int globalIndex = bufferManager.LocalToGlobalVertexIndex(meshIndex, localVertexIndex);
            if (globalIndex < 0)
                return false;

            var vertexFlags = bufferManager.VertexFlags;
            if (vertexFlags == null || globalIndex >= vertexFlags.Length)
                return false;

            return (vertexFlags[globalIndex] & (uint)SelectionFlags.Culled) != 0;
        }

        /// <summary>
        /// GPUの頂点フラグをCPUに読み戻す
        /// </summary>
        public void ReadBackVertexFlags()
        {
            if (_skipVertexFlagsReadback)
                return;
            _unifiedSystem?.BufferManager?.ReadBackVertexFlags();
        }

        // ============================================================
        // デバッグ
        // ============================================================

        /// <summary>
        /// 統計情報をログ出力
        /// </summary>
        public void LogStatus()
        {
            Debug.Log($"[UnifiedSystemAdapter] Initialized={_isInitialized}, UseUnifiedRendering={_useUnifiedRendering}");
            _unifiedSystem?.LogStatus();
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
                    _renderer?.Dispose();
                    _unifiedSystem?.Dispose();

                    if (_quadMesh != null)
                    {
                        UnityEngine.Object.DestroyImmediate(_quadMesh);
                        _quadMesh = null;
                    }
                }

                _disposed = true;
                _isInitialized = false;
            }
        }

        ~UnifiedSystemAdapter()
        {
            Dispose(false);
        }
    }
}