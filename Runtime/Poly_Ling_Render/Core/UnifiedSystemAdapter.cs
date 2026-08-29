// Assets/Editor/Poly_Ling/Core/UnifiedSystemAdapter.cs
// 統合システムアダプター
// SimpleMeshFactoryとUnifiedMeshSystemを接続するブリッジクラス

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
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
    public partial class UnifiedSystemAdapter : IDisposable
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

        // _lastKnownViewport は UpdateHoverOnly(Vector2, Rect) 専用の記録だった。
        // その経路ごと撤去したため読み手が無くなり、フィールドも削除した（2026-08-28）。

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
        /// 吸着用ヒットテスト（メッシュ選択を無視）の有効/無効。既定 false。
        /// true の間だけ追加のディスパッチと読み戻しが走るため、
        /// 必要なツールが有効な間だけ true にすること。
        /// </summary>
        public bool EnableSnapHitTest
        {
            get => _unifiedSystem?.EnableSnapHitTest ?? false;
            set { if (_unifiedSystem != null) _unifiedSystem.EnableSnapHitTest = value; }
        }

        /// <summary>
        /// 吸着用ヒットテストのホバー頂点（グローバルインデックス）。未ヒットは -1。
        /// 非選択メッシュの頂点も返り得る。通常のホバー表示には反映されない。
        /// </summary>
        public int SnapHoverVertexIndex => _unifiedSystem?.SnapHoveredVertexIndex ?? -1;

        /// <summary>
        /// ホバー状態を全てクリアする。
        /// マウスが表示エリア外に出た場合に呼び出す。
        /// </summary>
        public void ClearMouseHover()
        {
            if (!_isInitialized)
                return;
            _unifiedSystem?.ClearAllHover();
        }

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

            Poly_Ling.Diagnostics.PLResStat.LiveAdapter++;
            Poly_Ling.Diagnostics.PLResStat.Report("Adapter.Initialize");

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
            RequestNormal();
        }

        /// <summary>
        /// 位置変更を通知
        /// </summary>
        public void NotifyTransformChanged()
        {
            _unifiedSystem.NotifyTransformChanged();
            RequestNormal();
        }

        /// <summary>
        /// 選択変更を通知
        /// </summary>
        public void NotifySelectionChanged()
        {
            _unifiedSystem.NotifySelectionChanged();
            RequestNormal();
        }

        // ============================================================
        // フレーム更新
        // ============================================================

        /// <summary>
        /// フレーム更新（毎フレーム呼ばれる）
        /// 
        /// ★★★ 禁忌（絶対厳守） ★★★
        /// この関数はAllowHitTest=false時に早期リターンする。
        /// TransformDragging中にこのガードを迂回・削除してはならない。
        /// BeginFrame/ProcessUpdates/ExecuteUpdates/EndFrame の全パイプラインが
        /// 毎ドラッグフレームで実行されると、全メッシュのGPU転送・スクリーン座標計算・
        /// ヒットテストが毎フレーム走り、1FPS以下に落ちる。
        ///
        /// ドラッグ中のリアルタイム表示更新が必要な場合:
        /// - トポロジ変更を伴わない表示専用入口を使用すること
        ///   → UnifiedMeshSystem.ProcessTransformUpdate()
        ///     （_bufferManager.UpdatePositions: Array.Copy + SetData のみ）
        /// - ホバーチェック無効化はUpdateModeProfile.AllowHitTest=falseで制御
        ///   （TransformDraggingプロファイルで既にfalse）
        /// - この関数を経由してはならない
        ///
        /// 過去の障害: RequestNormal迂回 + AllowMeshRebuild=true → 1FPS
        /// ★★★★★★★★★★★★★★★★★★★★
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

            // ★禁忌ガード: このreturnを削除・迂回してはならない
            if (!_currentProfile.AllowHitTest)
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
        /// フレーム更新（Camera オブジェクト直接版）。
        /// 正射影カメラ（Top / Front ビューポート）に対応するため、
        /// camera.worldToCameraMatrix / camera.projectionMatrix を使用する。
        /// fov=0 で Perspective 行列を構築する旧パスは正射影で縮退するため使用しない。
        /// </summary>
        public void UpdateFrame(Camera camera, Rect viewport, Vector2 mousePosition)
        {
            if (!_isInitialized || camera == null)
                return;

            if (!_currentProfile.AllowHitTest)
                return;

            _unifiedSystem.BeginFrame();

            Vector3 camPos    = camera.transform.position;
            Vector3 camTarget = camPos + camera.transform.forward;

            _unifiedSystem.UpdateCameraFromMatrix(
                camera.worldToCameraMatrix,
                camera.projectionMatrix,
                camPos,
                camTarget,
                viewport);

            _unifiedSystem.UpdateMousePosition(mousePosition);

            DirtyLevel level = _unifiedSystem.ProcessUpdates();
            _unifiedSystem.ExecuteUpdates(level);

            _unifiedSystem.EndFrame();
        }

        // ============================================================
        // 軽量ホバー更新（撤去済み） 2026-08-28
        // ============================================================
        //
        // 【UpdateHoverOnly(Vector2, Rect) を撤去した理由】
        //   呼出元 0 件。旧 Editor 側の PolyLing_Input.UpdateHoverOnMouseMove から
        //   呼ばれる前提のコードで、Player 経路からは一度も呼ばれていない。
        //   本体は UnifiedMeshSystem.ProcessHoverOnly → ProcessMouseUpdate(cpuOnly: true)
        //   を呼ぶ CPU ヒットテスト専用パスであり、そちらも同時に撤去した。
        //
        //   現在のホバーの正規入口は UpdateFrame(Camera, Rect, Vector2)。
        //   PlayerViewportManager.NotifyPointerHover がポインタ移動ごとに呼ぶ。
        //   GPU ヒットテストを削って軽量化したくなった場合でも、
        //   CPU ヒットテストへ戻す経路をここに復活させてはならない。

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
        ///
        /// 【展開範囲の権威】
        ///   メッシュごとの展開開始位置・頂点数は BufferManager が構築時に記録した
        ///   値 (TryGetExpandedRange) だけを使う。ここで数え直してはならない。
        ///   数え方が BuildExpandedVertexMapping と 1 か所でも違うと、
        ///   別メッシュのワールド座標を UnityMesh へ書き込む。
        ///
        /// 【旧 Mesh の破棄】
        ///   本メソッドは Graphics.DrawMesh 提出と同一フレーム内で走り得る唯一の
        ///   差し替え経路なので、破棄は MeshContext の退避キューへ回し、
        ///   次回の呼び出し先頭でまとめて解放する（下の FlushRetiredMeshes）。
        /// </summary>
        public void WritebackTransformedVertices()
        {
            // 前回積んだ旧 Mesh をここで解放する。前フレームの描画は完了している。
            Poly_Ling.Data.MeshContext.FlushRetiredMeshes();

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

            {
                // [CamDbg] GPU から読み戻した頂点座標に NaN / Inf が無いかを検査する。
                int __bad = 0, __firstBad = -1;
                int __n = totalExpandedCount < expandedPositions.Length ? totalExpandedCount : expandedPositions.Length;
                for (int __i = 0; __i < __n; __i++)
                {
                    var __p = expandedPositions[__i];
                    if (float.IsNaN(__p.x) || float.IsNaN(__p.y) || float.IsNaN(__p.z)
                     || float.IsInfinity(__p.x) || float.IsInfinity(__p.y) || float.IsInfinity(__p.z))
                    {
                        __bad++;
                        if (__firstBad < 0) __firstBad = __i;
                    }
                }
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("WB n=" + __n + " bad=" + __bad + " first=" + __firstBad);
            }

            // 各MeshContextのUnityMeshを更新
            for (int ctxIdx = 0; ctxIdx < meshContextList.Count; ctxIdx++)
            {
                var ctx = meshContextList[ctxIdx];
                if (ctx?.MeshObject == null)
                    continue;

                // バッファに載っていないメッシュはスキップする。
                //
                // 【なぜ型で判定しないか】
                //   ボーン・モーフに加えて不可視メッシュ・空メッシュも載らないため、
                //   型による判定では足りない。実際のマッピング
                //   （ContextToUnifiedMeshIndex）を唯一の根拠にする。
                //   載せる条件は UnifiedBufferManager.ShouldIncludeInBuffers。
                int unifiedIdx = bufferManager.ContextToUnifiedMeshIndex(ctxIdx);
                if (unifiedIdx < 0)
                    continue;

                var meshObject = ctx.MeshObject;

                // 展開範囲は構築時に記録された値だけを使う。ここで数え直さないこと
                // （数え方が BuildExpandedVertexMapping と割れると別メッシュを書く）。
                if (!bufferManager.TryGetExpandedRange(unifiedIdx, out int expandedOffset, out int expandedVertexCount))
                    continue;

                // 展開頂点 0 は「全頂点が孤立頂点」のメッシュ（点群・補助線のみ）。
                // UnityMesh 側にも対応する頂点が無いので書き戻す対象が無い。
                // 表示・選択は基本頂点バッファ側（点描画）が担当する。
                if (expandedVertexCount == 0)
                    continue;

                // 境界チェック: expandedPositions配列の範囲外アクセスを防ぐ
                if (expandedOffset < 0 || expandedOffset + expandedVertexCount > expandedPositions.Length)
                {
                    WarnWritebackOnce(
                        $"展開範囲がバッファ外を指している mesh='{ctx.Name}' " +
                        $"offset={expandedOffset} count={expandedVertexCount} bufferSize={expandedPositions.Length}");
                    continue;
                }

                var unityMesh = ctx.UnityMesh;

                // UnityMeshが存在し、頂点数が一致する場合は位置のみ更新（通常経路）
                if (unityMesh != null && unityMesh.vertexCount == expandedVertexCount)
                {
                    CopyExpandedPositions(unityMesh, expandedPositions, expandedOffset, expandedVertexCount);
                    continue;
                }

                // ここから先は「UnityMesh が無い」か「MeshObject の位相と食い違っている」場合。
                //
                // 【どういうときに来るか】
                //   ・新規メッシュに面を張った直後（UnityMesh が空 Mesh のまま）
                //   ・位相 Undo / Redo。MeshObjectSnapshot.ApplyTo は MeshObject を
                //     差し替えるだけで UnityMesh を触らない。UnityMesh を作り直すのは
                //     PlayerViewportManager.RebuildSelectedUnityMeshes だが、対象は
                //     SelectedDrawableMeshIndices に入っているメッシュだけなので、
                //     非選択メッシュはここが唯一の復旧経路になる。
                //   ・選択外のメッシュを書き換えるツール
                //
                // 【重要】ここで meshObject.Vertices[].Position に GPU のワールド座標を
                // 書き戻してはならない。Vertices はローカル座標であり、ワールド座標を
                // 焼き込むと描画時に WorldMatrix が二重適用され、メッシュがずれたまま
                // 保存されてデータが永続的に壊れる。
                // 上の分岐と同じく、UnityMesh 側にだけ展開済みワールド座標を入れる。
                var regenerated = meshObject.ToUnityMesh();

                if (regenerated == null)
                {
                    WarnWritebackOnce($"ToUnityMesh が null を返した mesh='{ctx.Name}'");
                    continue;
                }

                if (regenerated.vertexCount != expandedVertexCount)
                {
                    // 展開規則は MeshExpansion に一本化してあるので、ここは
                    // 「バッファ構築後に MeshObject が書き換わった」ことを意味する。
                    // 呼び出し側が再構築（EnterTopologyChanged）を通していない。
                    WarnWritebackOnce(
                        $"再生成した Mesh の頂点数が展開頂点数と一致しない mesh='{ctx.Name}' " +
                        $"regenerated={regenerated.vertexCount} expected={expandedVertexCount}。" +
                        "バッファ構築後に MeshObject が変更されている。呼び出し側の再構築漏れを疑うこと。");

                    // 座標を書けないまま表示すると原点基準のローカル座標が描かれる。
                    // 作りかけを表に出さず、その場で捨てる。
                    // ここで作った Mesh は一度も DrawMesh へ提出していないので、
                    // 退避キューを経由せず即時破棄してよい。
                    Poly_Ling.Data.MeshContext.DestroyMesh(regenerated);
                    continue;
                }

                CopyExpandedPositions(regenerated, expandedPositions, expandedOffset, expandedVertexCount);

                // 旧 Mesh は退避キューへ（同一フレーム内で破棄しない）。
                ctx.ReplaceUnityMeshDeferred(regenerated);
            }
        }

        /// <summary>
        /// 展開済みワールド座標を Mesh へコピーする。NativeArray 経由で GC を抑える。
        /// </summary>
        private static void CopyExpandedPositions(
            Mesh mesh, Vector3[] expandedPositions, int offset, int count)
        {
            var nativeArray = new Unity.Collections.NativeArray<Vector3>(
                count,
                Unity.Collections.Allocator.Temp,
                Unity.Collections.NativeArrayOptions.UninitializedMemory);

            Unity.Collections.NativeArray<Vector3>.Copy(expandedPositions, offset, nativeArray, 0, count);

            mesh.SetVertices(nativeArray);
            mesh.RecalculateBounds();

            nativeArray.Dispose();
        }

        /// <summary>
        /// 書き戻しの不整合を報告する。毎フレーム走る経路なので同じ内容は 1 回だけ出す。
        /// ここが出たら呼び出し側の再構築漏れ。握り潰さずに原因を直すこと。
        /// </summary>
        private readonly HashSet<string> _writebackWarned = new HashSet<string>();

        private void WarnWritebackOnce(string message)
        {
            if (!_writebackWarned.Add(message)) return;
            Debug.LogWarning($"[WritebackTransformedVertices] {message}");
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

                if (ctx.Type == MeshType.Bone || ctx.Type == MeshType.Morph)
                    continue;

                int unifiedMeshIdx = bufferManager.ContextToUnifiedMeshIndex(ctxIdx);
                if (unifiedMeshIdx < 0)
                    continue;

                var meshInfo = meshInfos[unifiedMeshIdx];
                int vertexCount = (int)meshInfo.VertexCount;

                if (vertexCount == 0)
                    continue;

                var meshObject = ctx.MeshObject;
                if (meshObject.VertexCount != vertexCount)
                {
                    WarnWritebackOnce(
                        $"[Fallback] MeshObject の頂点数がバッファと一致しない mesh='{ctx.Name}' " +
                        $"meshObject={meshObject.VertexCount} buffer={vertexCount}。" +
                        "バッファ構築後に MeshObject が変更されている。");
                    continue;
                }

                // 【重要】meshObject.Vertices[].Position はローカル座標。
                // GPU の worldPositions を書き戻すと描画時に WorldMatrix が二重適用され、
                // データが永続的に壊れる。Vertices は変更せず、変換行列を適用した座標を
                // UnityMesh 側にだけ入れる。行列の選択規則は
                // UnifiedBufferManager.UpdateTransformMatrices と同一にする。
                // 対象の型は明示する。ウェイトの有無の判定だけ MeshContext.IsSkinned へ寄せる。
                bool usesWorldMatrixDirect = ctx.Type == MeshType.Mesh && !ctx.IsSkinned;
                Matrix4x4 xform = usesWorldMatrixDirect ? ctx.WorldMatrix : ctx.SkinningMatrix;

                // 【ToUnityMesh(xform) を使わない理由】
                //   行列版は面駆動で (頂点, UVスロット, 法線スロット) の組で名寄せする
                //   別順序であり、ToUnityMesh() の展開順序（MeshExpansion）と
                //   頂点数も並びも一致しない。ここで行列版の Mesh を作ると、
                //   以後 通常経路の「UnityMesh.vertexCount == 展開頂点数」判定が
                //   永久に偽になり、毎回この再生成を通り続ける。
                //   引数なし版で作り、座標だけ後から変換して入れる。
                var regenerated = meshObject.ToUnityMesh();
                if (regenerated == null)
                {
                    WarnWritebackOnce($"[Fallback] ToUnityMesh が null を返した mesh='{ctx.Name}'");
                    continue;
                }

                ApplyTransformToMeshVertices(regenerated, xform);

                // 旧 Mesh は退避キューへ（同一フレーム内で破棄しない）。
                ctx.ReplaceUnityMeshDeferred(regenerated);
            }
        }

        /// <summary>
        /// Mesh の頂点へ変換行列を適用する。展開順序は変えない。
        /// フォールバック経路が「ToUnityMesh() の並びのまま座標だけワールド化」を
        /// 行うために使う。法線は RecalculateNormals せず、そのまま残す
        /// （ToUnityMesh() が MeshObject の法線を入れている）。
        /// </summary>
        private static void ApplyTransformToMeshVertices(Mesh mesh, Matrix4x4 xform)
        {
            if (mesh == null || mesh.vertexCount == 0) return;

            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = xform.MultiplyPoint3x4(verts[i]);

            mesh.vertices = verts;
            mesh.RecalculateBounds();
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
        /// 描画準備（ワイヤーフレーム・頂点メッシュの構築とキューイング）
        ///
        /// ★★★ 禁忌（絶対厳守） ★★★
        /// AllowMeshRebuild=true のプロファイルを TransformDragging中に適用してはならない。
        /// UpdateWireframeMesh / UpdatePointMesh は全ライン・全頂点を走査して
        /// Mesh頂点・カラー・インデックスを毎回再構築する重い処理であり、
        /// 毎ドラッグフレームで実行すると1FPS以下に落ちる。
        ///
        /// ドラッグ中のワイヤーフレーム更新が必要な場合:
        /// - トポロジ（インデックス・カラー・フラグ）は不変のまま
        ///   頂点位置のみを差し替える軽量パスを使用すること
        ///   → UnifiedMeshSystem.ProcessTransformUpdateSelectedOnly()
        ///     （選択メッシュの _bufferManager.UpdatePositions のみ）
        /// - この関数のrebuildMeshパスを経由してはならない
        ///
        /// 過去の障害: AllowMeshRebuild=true → 1FPS（ワイヤー全再構築が毎フレーム実行）
        /// ★★★★★★★★★★★★★★★★★★★★
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
            float alpha = 1f,
            int cullingSlot = 0,
            bool showFaceOverlay = true)
        {
            if (!_isInitialized)
            {
                return;
            }

            bool rebuildMesh = _currentProfile.AllowMeshRebuild;

            bool lightweightPositionUpdate = !rebuildMesh
                && _currentMode == UpdateMode.TransformDragging
                && RealtimeTransformUpdate;

            if (lightweightPositionUpdate)
            {
                _unifiedSystem.ProcessTransformUpdateSelectedOnly();

                var bm = BufferManager;
                bm?.DispatchTransformVertices(useWorldTransform: true, transformNormals: false, readbackToCPU: true);

                if (showWireframe)
                    _renderer.UpdateWireframePositionsOnly();
                if (showVertices)
                    _renderer.UpdatePointPositionsOnly(camera, pointSize);
                // Phase 2c: 面塗り overlay は頂点位置に連動するため、軽量更新時も再構築が必要。
                // ただし wireframe の UpdateWireframePositionsOnly 相当の軽量経路は現状未実装のため、
                // やむをえずフル再構築する（ドラッグ中は毎フレーム走る点、許容する）。
                if (showFaceOverlay)
                    _renderer.UpdateFaceOverlayMeshSelected();
            }

            if (showWireframe)
            {
                if (rebuildMesh)
                {
                    _renderer.UpdateWireframeMeshSelected(alpha);
                    _renderer.UpdateWireframeMeshUnselected(0.4f);
                }
                _renderer.QueueWireframe(showUnselectedWireframe, cullingSlot);
            }

            if (showVertices)
            {
                if (rebuildMesh)
                {
                    _renderer.UpdatePointMeshSelected(camera, pointSize, alpha);
                    _renderer.UpdatePointMeshUnselected(camera, pointSize, 0.4f);
                }
                _renderer.QueuePoints(showUnselectedVertices, cullingSlot);
            }

            // Phase 2c: 面塗り overlay
            if (showFaceOverlay)
            {
                if (rebuildMesh)
                {
                    _renderer.UpdateFaceOverlayMeshSelected();
                }
                _renderer.QueueFaceOverlay(cullingSlot);
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 指定 slot のキューに入っているメッシュを指定カメラに描画する。
        /// 計算処理は一切禁止。全ての準備は PrepareDrawing で完了させておくこと。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void DrawQueued(Camera camera, int slot)
        {
            if (!_isInitialized) return;
            _renderer.DrawQueued(camera, slot);
        }

        /// <summary>
        /// 【重大規約違反: 旧 API】slot 非対応版。slot=0 で動作する。Phase 1 で削除予定。
        /// </summary>
        [System.Obsolete("Use DrawQueued(camera, slot) instead.")]
        public void DrawQueued(Camera camera)
        {
            if (!_isInitialized) return;
            _renderer.DrawQueued(camera, 0);
        }


        /// <summary>
        /// 指定 slot の描画後クリーンアップ。
        /// </summary>
        public void CleanupQueued(int slot)
        {
            if (!_isInitialized) return;
            _renderer.CleanupQueued(slot);
        }

        /// <summary>
        /// 【重大規約違反: 旧 API】全 slot をクリア。Phase 1 で削除予定。
        /// </summary>
        [System.Obsolete("Use CleanupQueued(slot) instead.")]
        public void CleanupQueued()
        {
            if (!_isInitialized) return;
#pragma warning disable CS0618
            _renderer.CleanupQueued();
#pragma warning restore CS0618
        }

        /// <summary>
        /// プレイヤービルド用: カメラ情報からGPUカリングを実行して per-slot カリングバッファを更新する。
        /// DrawQueued の前に呼ぶこと。
        /// </summary>
        /// <param name="readback">
        /// スクリーン座標を CPU へ同期読み戻しするか。既定 false。
        ///
        /// 本メソッドの後続（FaceVisibility / LineVisibility / ApplyMirrorCull）は
        /// すべて GPU 内で完結し、CPU は結果を読まない。したがって表示用カリングの
        /// ために呼ぶときは false のままでよい。
        ///
        /// true にするのは「そのあと CPU が GetScreenPositions() を読む」ときだけ。
        /// 現状の該当箇所は PlayerViewportManager.PresentAll 末尾の
        /// アクティブ slot 最終確定 1 か所のみ（矩形選択・投げ縄選択が読む）。
        ///
        /// _screenPositions は slot ごとに分かれていない単一配列なので、
        /// 1 回の PresentAll で true にしてよい呼び出しは 1 つだけ。
        /// 複数 slot で true にすると最後の 1 回の値しか残らない。
        /// </param>
        public void DispatchCullingForDisplay(
            Camera camera, bool backfaceCulling = true, int slot = 0, bool readback = false)
        {
            if (!_isInitialized || camera == null) return;
            var bm = _unifiedSystem?.BufferManager;
            if (bm == null || !bm.GpuComputeAvailable) return;

            Matrix4x4 vp = camera.projectionMatrix * camera.worldToCameraMatrix;
            var viewport = new Rect(0f, 0f, camera.pixelWidth, camera.pixelHeight);

            // 対象スロットのカリングバッファのみクリアする。
            // DispatchClearBuffersGPU は呼ばない（ヒットテストバッファのクリアは不要、
            // かつ呼ぶと他スロットの slot 0 自動クリアが発生する可能性があるため）。
            // [CamDbg] cull=0 のとき表示用カリングを丸ごと止める。診断専用。
            if (!Poly_Ling.Diagnostics.PLCamDbg.SwCullDisplay) return;
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C1 clearCulled slot=" + slot);
            bm.DispatchClearCulledBuffersGPU(slot);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C2 screenPos slot=" + slot);
            bm.ComputeScreenPositionsGPU(vp, viewport, slot, "cullDisplay", readback);

            if (backfaceCulling)
            {
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C3 faceVis slot=" + slot);
                bm.DispatchFaceVisibilityGPU(slot);
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C4 lineVis slot=" + slot);
                bm.DispatchLineVisibilityGPU(slot);
            }
            else
            {
                if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C3b clearCulledFlags slot=" + slot);
                bm.ClearCulledFlagsGPU(slot);
            }

            // 永続ミラーの最終カリング（両分岐後に適用し、上書きを受けないようにする）。
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C5 applyMirrorCull slot=" + slot);
            bm.DispatchApplyMirrorCullGPU(slot);
            if (Poly_Ling.Diagnostics.PLCamDbg.SwLog) Poly_Ling.Diagnostics.PLCamDbg.Mark("C6 cullDone slot=" + slot);
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
        /// 指定メッシュのローカル頂点が「表面の面に属さない」(背面カリング対象) かを取得。
        ///
        /// IsVertexCulled (_vertexFlags & FLAG_CULLED) は画面外判定専用で、
        /// 背面カリングの結果は反映されていない。本メソッドは GPU 側
        /// _VertexCulledBuffer を ReadBackVertexCulled で CPU に読み戻した
        /// _vertexCulledCache を参照する。矩形・投げ縄選択の CPU ループから使う。
        ///
        /// 呼出前に必ず ReadBackVertexCulled(slot) を呼ぶこと。
        /// </summary>
        public bool IsVertexBackfaceCulled(int meshIndex, int localVertexIndex)
        {
            var bufferManager = _unifiedSystem?.BufferManager;
            if (!_isInitialized || bufferManager == null)
                return false;

            int globalIndex = bufferManager.LocalToGlobalVertexIndex(meshIndex, localVertexIndex);
            if (globalIndex < 0)
                return false;

            var vertexCulled = bufferManager.VertexCulled;
            if (vertexCulled == null || globalIndex >= vertexCulled.Length)
                return false;

            return vertexCulled[globalIndex] != 0u;
        }

        /// <summary>
        /// GPUの頂点フラグをCPUに読み戻す。
        ///
        /// 呼出し元は MoveToolHandler.OnLeftDragEnd 内の矩形/投げ縄選択確定直前
        /// のワンショットのみ。ドラッグ中は UpdateMode が TransformDragging 等で
        /// AllowVertexFlagsReadback=false になっているが、本メソッドは矩形選択
        /// 確定時に CPU カリング判定を行うための必須処理のためガードを設けない。
        /// </summary>
        public void ReadBackVertexFlags()
        {
            _unifiedSystem?.BufferManager?.ReadBackVertexFlags();
        }

        /// <summary>
        /// 指定スロットの GPU 頂点カリングバッファを CPU キャッシュに読み戻す。
        /// 矩形選択・投げ縄選択の直前に呼ぶこと。IsVertexBackfaceCulled はこの
        /// キャッシュを参照する。
        ///
        /// ReadBackVertexFlags と同じ理由 (ワンショット呼出し) でガードは設けない。
        /// </summary>
        public void ReadBackVertexCulled(int slot = 0)
        {
            _unifiedSystem?.BufferManager?.ReadBackVertexCulled(slot);
        }

        /// <summary>
        /// 指定メッシュ(unified)のローカル面が背面カリング(または非表示)対象かを取得。
        /// 呼出前に ReadBackFaceCulled(slot) を呼ぶこと。GPU 側 _FaceCulledBuffer 由来。
        /// </summary>
        public bool IsFaceBackfaceCulled(int meshIndex, int localFaceIndex)
        {
            var bufferManager = _unifiedSystem?.BufferManager;
            if (!_isInitialized || bufferManager == null)
                return false;

            int globalIndex = bufferManager.LocalToGlobalFaceIndex(meshIndex, localFaceIndex);
            if (globalIndex < 0)
                return false;

            var faceCulled = bufferManager.FaceCulled;
            if (faceCulled == null || globalIndex >= faceCulled.Length)
                return false;

            return faceCulled[globalIndex] != 0u;
        }

        /// <summary>
        /// 指定スロットの GPU 面カリングバッファを CPU キャッシュに読み戻す。
        /// </summary>
        public void ReadBackFaceCulled(int slot = 0)
        {
            _unifiedSystem?.BufferManager?.ReadBackFaceCulled(slot);
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
                Poly_Ling.Diagnostics.PLResStat.LiveAdapter--;
                Poly_Ling.Diagnostics.PLResStat.Report("Adapter.Dispose");
                if (disposing)
                {
                    // 退避キューに残った旧 Mesh をここで解放する。
                    // アダプター破棄時点で描画提出は既に消化されている。
                    Poly_Ling.Data.MeshContext.FlushRetiredMeshes();

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