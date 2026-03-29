// PlayerViewportManager.cs
// 3つの PlayerViewport（Perspective / Top / Front）を管理し、
// MeshSceneRenderer の描画呼び出しを各カメラに対して行う。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Tools;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    /// <summary>
    /// Perspective / Top / Front の3ビューポートを管理する。
    /// Viewer の Update / LateUpdate から対応メソッドを呼ぶこと。
    /// </summary>
    public class PlayerViewportManager
    {
        // ================================================================
        // ビューポート公開
        // ================================================================

        public PlayerViewport PerspectiveViewport { get; private set; }
        public PlayerViewport TopViewport         { get; private set; }
        public PlayerViewport FrontViewport       { get; private set; }
        public PlayerViewport SideViewport        { get; private set; }

        // ================================================================
        // 内部
        // ================================================================

        private MeshSceneRenderer _renderer;

        // AxisGizmo 用
        private MoveToolHandler   _moveToolHandler;
        private PlayerToolContext _toolCtx = new PlayerToolContext();

        // LateUpdate で UpdateFrame を呼ぶための最後のカメラ参照とマウス位置。
        // NotifyCameraChanged / NotifyPointerHover で更新される。
        private Camera  _lastCamera;
        private Vector2 _lastMousePos;
        private bool    _lastParamsValid;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Initialize(Transform parent, MeshSceneRenderer renderer)
        {
            _renderer = renderer;

            PerspectiveViewport = new PlayerViewport();
            TopViewport         = new PlayerViewport();
            FrontViewport       = new PlayerViewport();
            SideViewport        = new PlayerViewport();

            PerspectiveViewport.Initialize(ViewportMode.Perspective, parent);
            TopViewport        .Initialize(ViewportMode.Top,         parent);
            FrontViewport      .Initialize(ViewportMode.Front,       parent);
            SideViewport       .Initialize(ViewportMode.Side,        parent);
        }

        public void Dispose()
        {
            PerspectiveViewport?.Dispose();
            TopViewport        ?.Dispose();
            FrontViewport      ?.Dispose();
            SideViewport       ?.Dispose();
            PerspectiveViewport = null;
            TopViewport         = null;
            FrontViewport       = null;
            SideViewport        = null;
        }

        public void RegisterMoveToolHandler(MoveToolHandler handler)
        {
            _moveToolHandler = handler;
        }

        public ToolContext GetCurrentToolContext(PlayerViewport vp = null)
        {
            var target = vp ?? PerspectiveViewport;
            var cam = target?.Cam;
            if (cam == null) return null;
            _toolCtx.UpdateFromViewport(target);
            return _toolCtx.ToToolContext(cam);
        }

        // ================================================================
        // 毎フレーム更新（Update から呼ぶ）
        // ================================================================

        public void Update()
        {
            PerspectiveViewport?.ApplyCameraTransform();
            TopViewport        ?.ApplyCameraTransform();
            FrontViewport      ?.ApplyCameraTransform();
            SideViewport       ?.ApplyCameraTransform();
        }

        // ================================================================
        // 描画（LateUpdate から呼ぶ）
        // ================================================================

        public void LateUpdate(ProjectContext project)
        {
            if (_renderer == null) return;

            // 毎フレーム RequestNormal + UpdateFrame を呼ぶ。
            // Editor の ViewportCore.Draw() が毎 Repaint UpdateFrame を呼ぶのと同等。
            // dirty チェックにより変化がなければ ProcessUpdates は即返る。
            // TransformDragging / CameraDragging 中は RequestNormal が内部でブロックされる。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapter = _renderer.GetAdapter(0);
                if (adapter != null && adapter.IsInitialized)
                {
                    var rect = new Rect(0, 0, _lastCamera.pixelWidth, _lastCamera.pixelHeight);
                    adapter.RequestNormal();
                    adapter.UpdateFrame(_lastCamera, rect, _lastMousePos);
                }
            }

            DrawViewport(project, PerspectiveViewport);
            DrawViewport(project, TopViewport);
            DrawViewport(project, FrontViewport);
            DrawViewport(project, SideViewport);

            // 全ビューポート描画後、_screenPositions をアクティブビューポート用に復元する。
            // DrawViewport で各カメラの DispatchCullingForDisplay を呼ぶと _screenPositions が
            // 最後のビューポートのカメラで上書きされる。
            // 矩形選択の CommitBoxSelect は _screenPositions を参照するため、
            // アクティブビューポート（_lastCamera）の座標に戻す必要がある。
            if (_lastParamsValid && _lastCamera != null)
            {
                var adapterRestore = _renderer?.GetAdapter(0);
                if (adapterRestore != null && adapterRestore.IsInitialized)
                    adapterRestore.DispatchCullingForDisplay(
                        _lastCamera, adapterRestore.BackfaceCullingEnabled);
            }
        }

        // ================================================================
        // ホバー・カメラ更新（イベント駆動）
        // ================================================================

        /// <summary>
        /// カメラパラメータが確定したときに呼ぶ。
        /// 対象ビューポートの全アダプターに UpdateFrame を1回実行する。
        ///
        /// 【いつ呼ぶか】
        ///   - OrbitCameraController.OnCameraChanged（ドラッグ終了・ResetToMesh後）
        ///   - OrthoViewController.OnCameraChanged（パン・ズーム終了後）
        ///
        /// 【なぜ毎フレームでないか】
        ///   UpdateFrame はGPUヒットテストパイプライン全体を走らせる重い処理。
        ///   パラメータが変化したタイミングだけで十分。
        /// </summary>
        public void NotifyCameraChanged(PlayerViewport vp)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;

            // モデルインデックス0固定（現状シングルモデル）
            // 将来マルチモデル対応する場合はループに変える。
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // ビューポートのピクセルサイズをRectで渡す（座標変換に使われる）
            var rect = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);

            // ホバー位置はカメラ変更時には不明なのでパネル中央を渡す（影響小）
            var dummyMouse = new Vector2(rect.width * 0.5f, rect.height * 0.5f);

            // パラメータを保存（LateUpdate の UpdateFrame で使い回す）
            _lastCamera  = cam;
            if (!_lastParamsValid) _lastMousePos = dummyMouse;
            _lastParamsValid = true;

            _toolCtx.UpdateFromViewport(vp);

            adapter.RequestNormal();
            adapter.UpdateFrame(cam, rect, _lastMousePos);
        }

        /// <summary>
        /// マウスが指定ビューポートのRenderTexture内を移動したときに呼ぶ。
        /// UpdateFrame を実行してカメラパラメータ設定 + GPU ヒットテストを一括実行する。
        ///
        /// 【いつ呼ぶか】
        ///   PlayerViewportPanel.OnPointerHover（UIToolkit PointerMoveEvent）。
        ///   UIToolkit はそのパネル内にポインターがあるときだけイベントを発火するので、
        ///   ボタン等の別パネル上では自然に発火しない → RenderTexture 内限定を保証。
        ///
        /// 【UpdateHoverOnly を使わない理由】
        ///   UpdateHoverOnly（cpuOnly=true）は CPU 版ヒットテストのみを実行し、
        ///   GPU の頂点フラグバッファには書き込まない。
        ///   そのためホバーハイライトが描画に反映されない。
        ///   UpdateFrame はフルパイプラインを実行しホバー色も GPU バッファに書き込む。
        ///   Editor 側も Repaint のたびに UpdateFrame を呼んでいる（同様に遅さを許容）。
        ///
        /// 【座標系について】
        ///   panelLocalPos は UIToolkit のパネルローカル座標（Y=0が上）。
        ///   UpdateFrame 内部の SetHitTestInput が期待する座標系と一致する。
        /// </summary>
        public void NotifyPointerHover(PlayerViewport vp, Vector2 panelLocalPos)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            var rect = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);

            // パラメータを保存（LateUpdate の UpdateFrame で使い回す）
            _lastCamera      = cam;
            _lastMousePos    = panelLocalPos;
            _lastParamsValid = true;

            // ポインター移動時は RequestNormal → UpdateFrame でフルパイプライン実行。
            _toolCtx.UpdateFromViewport(vp);
            if (_moveToolHandler != null)
                _moveToolHandler.UpdateHover(panelLocalPos, _toolCtx.ToToolContext(cam));

            adapter.RequestNormal();
            adapter.UpdateFrame(cam, rect, panelLocalPos);
        }

        /// <summary>
        /// 矩形選択確定前に背面カリングフラグをGPU→CPUへ読み戻す。
        ///
        /// 【いつ呼ぶか】
        ///   MoveToolHandler.OnLeftDragEnd で矩形選択が確定する直前。
        ///   ReadBack後に IsVertexVisible() で背面頂点を除外できる。
        /// </summary>
        public void ReadBackVertexFlags()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ReadBackVertexFlags();
        }

        public void ClearMouseHover()
        {
            _renderer?.GetAdapter(0)?.ClearMouseHover();
        }

        /// <summary>
        /// 選択確定・操作確定後にGPUバッファ更新を1回要求する。
        ///
        /// 【いつ呼ぶか】
        ///   - 矩形選択確定後
        ///   - クリック選択確定後
        ///   - 頂点移動ドラッグ終了後
        ///
        /// 【ワンショット方式】
        ///   RequestNormal() で Normal モードに昇格 → 次の PrepareDrawing で
        ///   全フラグ再計算 → ConsumeNormalMode() で Idle に自動降格。
        ///   これにより「何もしていない時」は重い処理が走らない。
        /// </summary>
        public void RequestNormal()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.RequestNormal();
        }

        /// <summary>
        /// 頂点ドラッグ開始を通知する。
        /// アダプターを TransformDragging モードに切り替える。
        ///
        /// TransformDragging 中は UpdateFrame のガードが有効になり、
        /// ヒットテスト・GPU可視性計算・メッシュ再構築がスキップされる。
        /// 位置更新は軽量パス（ProcessTransformUpdate）で行う。
        /// </summary>
        public void EnterTransformDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterTransformDragging();
        }

        /// <summary>
        /// 頂点ドラッグ終了を通知する。
        /// アダプターを Normal モード（1フレーム）→ Idle に戻す。
        /// </summary>
        public void ExitTransformDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitTransformDragging();
        }

        /// <summary>
        /// カメラ姿勢変更開始を通知する（オービット・パン）。
        /// アダプターを CameraDragging モードに切り替える。
        /// CameraDragging 中は AllowUnselectedOverlay=false になり、
        /// 非選択メッシュの頂点・辺描画が抑止される。
        /// </summary>
        public void EnterCameraDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterCameraDragging();
        }

        /// <summary>
        /// カメラ姿勢変更終了を通知する。
        /// アダプターを Normal モード（1フレーム）→ Idle に戻す。
        /// </summary>
        public void ExitCameraDragging()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitCameraDragging();
        }

        /// <summary>
        /// 矩形選択ドラッグ開始を通知する。
        /// ホバー・ヒットテストが不要なため CameraDragging モードで代用する。
        ///
        /// 【なぜ CameraDragging か】
        ///   矩形選択専用モードは存在しないが、CameraDragging は
        ///   AllowHitTest=false / AllowMeshRebuild=false のプロファイルで
        ///   「重い処理を全スキップ」を意味するため意図に合致する。
        /// </summary>
        public void EnterBoxSelecting()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.EnterCameraDragging();
        }

        /// <summary>
        /// 矩形選択ドラッグ終了を通知する。
        /// CameraDragging を終了して Normal モードに戻す。
        /// その後 ReadBackVertexFlags() + RequestNormal() を呼ぶこと。
        /// </summary>
        public void ExitBoxSelecting()
        {
            var adapter = _renderer?.GetAdapter(0);
            adapter?.ExitCameraDragging();
        }

        // ================================================================
        // 矩形選択用 GPU データ取得（MoveToolHandler コールバック用）
        // ================================================================

        /// <summary>
        /// GPU が計算したスクリーン座標配列（全頂点グローバルインデックス）を返す。
        /// ReadBackVertexFlags() の後に呼ぶこと。
        ///
        /// MoveToolHandler.GetScreenPositions コールバックに設定して使う。
        /// </summary>
        public Vector2[] GetScreenPositions()
        {
            return _renderer?.GetAdapter(0)?.BufferManager?.GetScreenPositions();
        }

        /// <summary>
        /// 指定コンテキストインデックスのメッシュの頂点グローバルオフセットを返す。
        /// GetScreenPositions() の配列インデックス計算に使う。
        ///
        /// MoveToolHandler.GetVertexOffset コールバックに設定して使う。
        /// </summary>
        public int GetVertexOffset(int meshContextIndex)
        {
            return _renderer?.GetAdapter(0)?.GetVertexOffset(meshContextIndex) ?? 0;
        }

        /// <summary>
        /// グローバル頂点インデックスが背面カリングで可視かどうかを返す。
        /// ReadBackVertexFlags() の後に有効。
        ///
        /// 内部で IsVertexCulled(meshIndex, localIndex) を使う。
        /// グローバルインデックスからメッシュ・ローカルインデックスへの変換を行う。
        ///
        /// MoveToolHandler.IsVertexVisible コールバックに設定して使う。
        /// </summary>
        public bool IsVertexVisible(int globalVertexIndex)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return true;
            if (!adapter.BackfaceCullingEnabled) return true;

            // グローバルインデックス → メッシュインデックス + ローカルインデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertexIndex, out int meshIdx, out int localIdx) == true)
            {
                return !adapter.IsVertexCulled(meshIdx, localIdx);
            }
            return true;
        }

        /// <summary>
        /// GPU ホバー結果を PlayerHoverElement（種別付き）に変換して返す。
        /// SelectionState.Mode に応じた要素種別を返す。
        ///
        /// 優先順位: 頂点 > 辺/補助線分 > 面
        /// （Mode で無効な種別はスキップ）
        /// </summary>
        public PlayerHoverElement GetHoverElement(
            Poly_Ling.Selection.MeshSelectMode mode,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHoverElement.None;

            // ---- 頂点 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Vertex))
            {
                int gv = adapter.HoverVertexIndex;
                if (gv >= 0 && adapter.BufferManager?.GlobalToLocalVertexIndex(
                        gv, out int vm, out int vl) == true)
                {
                    int ctxV = UnifiedToContextIndex(adapter, model, vm);
                    if (ctxV >= 0)
                        return new PlayerHoverElement
                        {
                            Kind        = PlayerHoverKind.Vertex,
                            MeshIndex   = ctxV,
                            VertexIndex = vl,
                        };
                }
            }

            // ---- 辺 / 補助線分 ----
            bool wantEdge = mode.Has(Poly_Ling.Selection.MeshSelectMode.Edge);
            bool wantLine = mode.Has(Poly_Ling.Selection.MeshSelectMode.Line);
            if (wantEdge || wantLine)
            {
                int gl = adapter.HoverLineIndex;
                if (gl >= 0 && adapter.BufferManager?.GlobalToLocalLineIndex(
                        gl, out int lm, out int ll) == true)
                {
                    int ctxL = UnifiedToContextIndex(adapter, model, lm);
                    if (ctxL >= 0)
                    {
                        // IsAuxLine 判定
                        var lines = adapter.BufferManager.Lines;
                        bool isAux = gl < lines.Length && lines[gl].IsAuxLine;

                        if (isAux && wantLine)
                        {
                            // 補助線分。FaceIndex はグローバル面インデックス
                            // → ローカル面インデックスに変換
                            uint gfi = gl < lines.Length ? lines[gl].FaceIndex : uint.MaxValue;
                            int localFaceIdx = -1;
                            if (gfi != uint.MaxValue)
                                adapter.BufferManager.GlobalToLocalFaceIndex(
                                    (int)gfi, out _, out localFaceIdx);
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Line,
                                MeshIndex = ctxL,
                                FaceIndex = localFaceIdx,
                            };
                        }
                        else if (!isAux && wantEdge)
                        {
                            // 辺。V1/V2 をローカルインデックスに変換
                            var meshInfos = adapter.BufferManager.MeshInfos;
                            uint vStart = lm < meshInfos.Length
                                ? meshInfos[lm].VertexStart : 0u;
                            uint gv1 = lines[gl].V1;
                            uint gv2 = lines[gl].V2;
                            return new PlayerHoverElement
                            {
                                Kind      = PlayerHoverKind.Edge,
                                MeshIndex = ctxL,
                                EdgeV1    = (int)(gv1 - vStart),
                                EdgeV2    = (int)(gv2 - vStart),
                            };
                        }
                    }
                }
            }

            // ---- 面 ----
            if (mode.Has(Poly_Ling.Selection.MeshSelectMode.Face))
            {
                int gf = adapter.HoverFaceIndex;
                if (gf >= 0 && adapter.BufferManager?.GlobalToLocalFaceIndex(
                        gf, out int fm, out int fl) == true)
                {
                    int ctxF = UnifiedToContextIndex(adapter, model, fm);
                    if (ctxF >= 0)
                        return new PlayerHoverElement
                        {
                            Kind      = PlayerHoverKind.Face,
                            MeshIndex = ctxF,
                            FaceIndex = fl,
                        };
                }
            }

            return PlayerHoverElement.None;
        }

        // unified メッシュインデックス → context インデックス 変換ヘルパー
        private static int UnifiedToContextIndex(
            Poly_Ling.Core.UnifiedSystemAdapter adapter,
            Poly_Ling.Context.ModelContext model,
            int unifiedIdx)
        {
            if (model == null) return -1;
            for (int ci = 0; ci < model.Count; ci++)
                if (adapter.ContextToUnifiedMeshIndex(ci) == unifiedIdx)
                    return ci;
            return -1;
        }

        public Vector2[] GetHoverFaceScreenPts(
            PlayerViewport vp, Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return null;
            int gf = adapter.HoverFaceIndex;
            if (gf < 0) return null;
            if (adapter.BufferManager?.GlobalToLocalFaceIndex(gf, out int mi, out int lf) != true) return null;
            int ci = UnifiedToContextIndex(adapter, model, mi);
            if (ci < 0) return null;
            var mc = model.GetMeshContext(ci);
            if (mc?.MeshObject == null || lf < 0 || lf >= mc.MeshObject.FaceCount) return null;
            var face = mc.MeshObject.Faces[lf];
            if (face.VertexCount < 3) return null;
            var cam = vp?.Cam;
            if (cam == null) return null;
            var pts = new Vector2[face.VertexCount];
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vi = face.VertexIndices[i];
                if (vi < 0 || vi >= mc.MeshObject.VertexCount) return null;
                Vector3 sp = cam.WorldToScreenPoint(mc.MeshObject.Vertices[vi].Position);
                if (sp.z < 0) return null;
                pts[i] = new Vector2(sp.x, sp.y);
            }
            return pts;
        }

        /// <summary>
        /// GPU ホバー結果から PlayerHitResult を生成する。
        /// マウスダウン時に PlayerVertexInteractor.GetHoverHit コールバックから呼ばれる。
        ///
        /// UpdateFrame（ポインター移動時）が GPU ヒットテストを完了済みの前提。
        /// CPU による最近傍探索は行わない。
        /// </summary>
        public PlayerHitResult GetHoverHit()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized)
                return PlayerHitResult.Miss;

            // グローバルホバー頂点インデックス
            int globalVertex = adapter.HoverVertexIndex;
            if (globalVertex < 0)
                return PlayerHitResult.Miss;

            // グローバル → メッシュコンテキストインデックス + ローカル頂点インデックス
            if (adapter.BufferManager?.GlobalToLocalVertexIndex(
                    globalVertex, out int meshIdx, out int localIdx) == true)
            {
                return new PlayerHitResult
                {
                    HasHit      = true,
                    MeshIndex   = meshIdx,
                    VertexIndex = localIdx,
                };
            }
            return PlayerHitResult.Miss;
        }

        // ================================================================
        // スキンド変換
        // ================================================================

        /// <summary>
        /// スキンドメッシュのワールド変換をGPUバッファに反映する。
        /// model.ComputeWorldMatrices() を呼んだ後に呼ぶこと。
        /// Viewer.Update() で毎フレーム呼ぶことで矩形選択・ホバーの
        /// スクリーン座標がスキンドポーズに追従する。
        /// </summary>
        public void UpdateTransform()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;
            adapter.UpdateTransform(useWorldTransform: true);
            adapter.WritebackTransformedVertices();
        }

        // ================================================================
        // MeshSceneRenderer 委譲
        // ================================================================

        /// <summary>
        /// 展開済み UnityMesh（UV分割で頂点数 > MeshObject.VertexCount）の
        /// 頂点座標を MeshObject.Vertices から直接更新する。
        ///
        /// GPU バッファの _expandedToOriginal マッピングと同じロジックを CPU で再現し、
        /// 展開後 Unity 頂点 → 元頂点インデックスを解決して position をコピーする。
        /// </summary>
        public void UpdateExpandedUnityMesh(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            if (mc?.MeshObject == null || mc.UnityMesh == null || model == null) return;

            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;

            // contextIndex を解決
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            if (ctxIdx < 0) return;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return;

            var bm = adapter.BufferManager;
            if (bm == null) return;

            var meshInfos = bm.MeshInfos;
            if (meshInfos == null || unifiedIdx >= meshInfos.Length) return;

            var positions = bm.Positions; // CPU側 _positions 配列
            if (positions == null) return;

            uint vertexStart = meshInfos[unifiedIdx].VertexStart;
            int meshVertCount = mc.MeshObject.VertexCount;

            // ToUnityMesh/ToUnityMeshShared と同じ展開順序でUnityMesh頂点を更新する。
            // 孤立頂点を除外し、頂点順 → UV順 で展開。
            var mo = mc.MeshObject;
            var nonIsolated = new System.Collections.Generic.HashSet<int>();
            foreach (var face in mo.Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden) continue;
                foreach (int vi in face.VertexIndices) nonIsolated.Add(vi);
            }

            int unityIdx = 0;
            int totalUnity = mc.UnityMesh.vertexCount;
            var unityVerts = new UnityEngine.Vector3[totalUnity];

            for (int vIdx = 0; vIdx < mo.Vertices.Count && unityIdx < totalUnity; vIdx++)
            {
                if (!nonIsolated.Contains(vIdx)) continue;
                var vertex = mo.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                UnityEngine.Vector3 pos = vertex.Position;
                for (int uvIdx = 0; uvIdx < uvCount && unityIdx < totalUnity; uvIdx++, unityIdx++)
                    unityVerts[unityIdx] = pos;
            }

            mc.UnityMesh.vertices = unityVerts;
            mc.UnityMesh.RecalculateBounds();
        }

        public void RebuildAdapter(int mi, ModelContext model)
            => _renderer?.RebuildAdapter(mi, model);

        public void ClearScene()
            => _renderer?.ClearScene();

        /// <summary>
        /// CPU の MeshObject 位置を GPU バッファに同期する。
        ///
        /// 【呼び出しタイミング】
        ///   MoveToolHandler.OnSyncMeshPositions（頂点ドラッグ中の毎フレーム位置更新）。
        ///
        /// 【処理順序】
        ///   1. MeshObject.Positions → GPU _positionsBuffer（UpdatePositions）
        ///   2. NotifyTransformChanged で次フレームのワイヤー/頂点メッシュ再構築を予約
        /// </summary>
        public void SyncMeshPositionsAndTransform(
            Poly_Ling.Data.MeshContext mc,
            Poly_Ling.Context.ModelContext model)
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null || !adapter.IsInitialized) return;
            if (mc?.MeshObject == null || model == null) return;

            var bm = adapter.BufferManager;
            if (bm == null) return;

            // MeshContext → contextIndex → unifiedMeshIndex
            int ctxIdx = -1;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (ReferenceEquals(model.GetMeshContext(i), mc)) { ctxIdx = i; break; }
            }
            if (ctxIdx < 0) return;

            int unifiedIdx = adapter.ContextToUnifiedMeshIndex(ctxIdx);
            if (unifiedIdx < 0) return;

            // ① CPU MeshObject.Positions → GPU _positionsBuffer
            bm.UpdatePositions(mc.MeshObject, unifiedIdx);

            // ② 次フレームのワイヤー/頂点メッシュ再構築を予約
            adapter.NotifyTransformChanged();
        }

        /// <summary>
        /// 頂点移動後に呼ぶ。次フレームのGPUバッファ更新を予約する。
        /// </summary>
        public void NotifyTransformChanged()
        {
            var adapter = _renderer?.GetAdapter(0);
            if (adapter == null) return;
            adapter.NotifyTransformChanged();
        }

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        public void ResetToMesh(Bounds bounds)
        {
            PerspectiveViewport?.ResetToMesh(bounds);
            TopViewport        ?.ResetToMesh(bounds);
            FrontViewport      ?.ResetToMesh(bounds);
            SideViewport       ?.ResetToMesh(bounds);
        }

        // ================================================================
        // 内部
        // ================================================================

        private void DrawViewport(ProjectContext project, PlayerViewport vp)
        {
            if (vp == null || !vp.IsReady) return;
            var cam     = vp.Cam;
            var adapter = _renderer?.GetAdapter(0);

            // 各ビューポートのカメラで背面カリングを再計算する。
            // DrawWireframeAndVertices の AllowGpuVisibility パスは Normal モード時のみ実行され
            // ConsumeNormalMode() で Idle に降格するため、2番目以降のビューポートでは
            // AllowGpuVisibility が走らず最初のカメラのカリング結果が流用される。
            // DispatchCullingForDisplay を常に呼ぶことで各カメラのカリングを保証する。
            if (adapter != null && adapter.IsInitialized
                && adapter.BackfaceCullingEnabled)
                adapter.DispatchCullingForDisplay(cam, true);

            _renderer.DrawMeshes(project, cam);
            // project を渡すことで AllowSelectionSync 時に SyncSelectionFromModel が
            // 呼ばれ VertexSelected 等のフラグが正しく反映される。
            _renderer.DrawWireframeAndVertices(cam, project);
            _renderer.DrawBones(project, cam);
        }
    }
}
