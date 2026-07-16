// MeshSceneRenderer.cs
// ProjectContextのメッシュ・ボーン・ワイヤーフレームをシーンに描画するクラス。
// Runtime/Editor両方から使用可能なplain C#クラス（IDisposable）。
// Graphics.DrawMesh ベース（MonoBehaviour不要）。
// エディタ側ViewportCoreも将来このクラスに委譲する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Core;
using Poly_Ling.Core.Rendering;

namespace Poly_Ling.Core
{
    /// <summary>
    /// ProjectContextのメッシュ・ワイヤーフレーム・ボーンをGraphics.DrawMeshで描画する。
    /// LateUpdate相当のタイミングで Draw*() を呼ぶこと。
    /// </summary>
    public class MeshSceneRenderer : IDisposable
    {
        // ================================================================
        // 描画フラグ
        // ================================================================

        public bool ShowSelectedMesh          { get; set; } = true;
        public bool ShowUnselectedMesh        { get; set; } = true;
        public bool ShowSelectedVertices      { get; set; } = true;
        public bool ShowUnselectedVertices    { get; set; } = true;
        public bool ShowSelectedWireframe     { get; set; } = true;
        public bool ShowUnselectedWireframe   { get; set; } = true;
        public bool ShowSelectedBone          { get; set; } = true;
        public bool ShowUnselectedBone        { get; set; } = false;
        public bool ShowSelectedMirror        { get; set; } = true;
        public bool ShowUnselectedMirror      { get; set; } = true;
        public bool BackfaceCullingEnabled    { get; set; } = true;

        // ================================================================
        // 内部状態
        // ================================================================

        // 外部から注入する SelectionState（Player用）
        private SelectionState _selectionState;

        // DrawWireframeAndVertices で PrepareDrawing に渡す selectedMeshIndex（モデルごと）。
        // model.FirstMeshIndex を adapter のコンテキストインデックスに変換した値。
        // RebuildAdapter / UpdateSelectedDrawableMesh で更新する。
        private readonly List<int>                  _selectedMeshIndexForDraw = new List<int>();

        private readonly List<UnifiedSystemAdapter> _adapters       = new List<UnifiedSystemAdapter>();
        private readonly Dictionary<(int, int), Mesh> _boneMeshCache= new Dictionary<(int, int), Mesh>();

        private Material _defaultMaterial;
        private Material _boneMaterial;

        // 診断用: 材質 null ログを1回だけ出すためのフラグ
        private static bool _matDbgLogged = false;
        // Phase 2c-2: 選択/非選択で alpha を変えるため 2 種類保持する。
        // シェーダは Poly_Ling/Bone3D_Overlay (ZTest Always)。
        private Material _boneOverlayMaterialSelected;
        private Material _boneOverlayMaterialUnselected;
        private bool     _disposed;

        // ================================================================
        // ボーン形状定数
        // ================================================================

        private static readonly Vector3[] BoneShapeVertices =
        {
            new Vector3(0.5f,  0f,   -0.4f),
            new Vector3(2.5f,  0f,    0f),
            new Vector3(0.5f,  0.2f,  0f),
            new Vector3(0.5f,  0f,    0.4f),
            new Vector3(0.5f, -0.2f,  0f),
            new Vector3(0f,    0f,    0f),
        };
        private static readonly int[,] BoneShapeEdges =
        {
            {0,1},{0,2},{0,4},{0,5},
            {1,2},{1,3},{1,4},
            {2,3},{2,5},
            {3,4},{3,5},
            {4,5},
        };
        private const float BoneShapeScale              = 0.04f;
        private static readonly Color BoneWireColor     = new Color(0.2f, 0.8f, 1.0f, 0.8f);
        private static readonly Color BoneWireSelColor  = new Color(1.0f, 0.6f, 0.1f, 0.9f);

        // ================================================================
        // Adapter構築
        // ================================================================

        /// <summary>
        /// 選択状態を設定する。RebuildAdapter より前に呼ぶこと。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void SetSelectionState(SelectionState selectionState)
        {
            _selectionState = selectionState;
            // 既存アダプターにも反映。
            // SetSelectionState の後に UpdateAllSelectionFlags を呼ばないと
            // GPU の MeshSelected ビットが古いままになりワイヤー・頂点が描画されない。
            foreach (var adapter in _adapters)
            {
                if (adapter == null) continue;
                adapter.SetSelectionState(_selectionState ?? new SelectionState());
                adapter.BufferManager?.UpdateAllSelectionFlags();
                adapter.RequestNormal();
            }
        }

        /// <summary>
        /// 指定モデルインデックスの UnifiedSystemAdapter を取得する。
        ///
        /// 【用途】
        ///   PlayerViewportManager がホバー更新・カメラ更新・矩形選択の
        ///   ReadBackVertexFlags を呼ぶために使う。
        ///   アダプターは RebuildAdapter() 後にのみ存在する。
        ///   存在しない場合（未ロード・ClearScene後）は null を返す。
        /// </summary>
        public UnifiedSystemAdapter GetAdapter(int mi)
        {
            if (mi < 0 || mi >= _adapters.Count) return null;
            return _adapters[mi];
        }

        /// <summary>
        /// 全アダプター数（ビューポートマネージャーがループ走査する際に使う）。
        /// </summary>
        public int AdapterCount => _adapters.Count;

        /// <summary>
        /// 選択描画メッシュが変わったときに呼ぶ。
        /// _selectedMeshIndexForDraw を更新し、PrepareDrawing に正しい index が渡るようにする。
        /// Viewer が model.SelectedDrawableMeshIndices を変更した後に呼ぶこと。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void UpdateSelectedDrawableMesh(int mi, ModelContext model)
        {
            while (_selectedMeshIndexForDraw.Count <= mi)
                _selectedMeshIndexForDraw.Add(-1);

            // PrepareDrawing の selectedMeshIndex は adapter の unifiedMeshIndex を期待する。
            // SelectedDrawableMeshIndices[0]（MeshContextList インデックス）を変換する。
            int ctxIdx = model.FirstMeshIndex;
            if (ctxIdx < 0 || mi >= _adapters.Count || _adapters[mi] == null)
            {
                _selectedMeshIndexForDraw[mi] = -1;
                return;
            }
            int unifiedIdx = _adapters[mi].BufferManager?.ContextToUnifiedMeshIndex(ctxIdx) ?? -1;
            _selectedMeshIndexForDraw[mi] = unifiedIdx;

            // ActiveMeshIndex と選択フラグを即時更新する。
            // RebuildAdapter は SelectMesh より先に呼ばれるため、ここで再設定が必要。
            var bm = _adapters[mi].BufferManager;
            if (bm != null && unifiedIdx >= 0)
            {
                bm.SyncSelectionFromModel(model);
                bm.SetActiveMesh(0, unifiedIdx);
                bm.UpdateAllSelectionFlags();
            }
        }

        /// <summary>選択変更をGPUバッファに通知する。</summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void NotifySelectionChanged()
        {
            foreach (var adapter in _adapters)
                adapter?.NotifySelectionChanged();
        }

        /// <summary>
        /// モデルのメッシュ受信完了後にAdapterを再構築する。
        /// </summary>
        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。",
            error: false)]
        public void RebuildAdapter(int mi, ModelContext model)
        {
            while (_adapters.Count <= mi) _adapters.Add(null);
            _adapters[mi]?.Dispose();
            _adapters[mi] = null;

            bool hasAny = false;
            foreach (var mc in model.MeshContextList)
                if (mc?.MeshObject != null && mc.MeshObject.VertexCount > 0) { hasAny = true; break; }
            if (!hasAny) return;

            var adapter = new UnifiedSystemAdapter();
            if (!adapter.Initialize())
            {
                Debug.LogWarning($"[MeshSceneRenderer] Adapter初期化失敗 [{mi}]");
                adapter.Dispose();
                return;
            }

            adapter.SetSelectionState(_selectionState ?? new SelectionState());
            adapter.SetSymmetrySettings(new SymmetrySettings());
            adapter.SetModelContext(model);

            // SetActiveMesh 用に先頭 Drawable のコンテキストインデックスを求める。
            // 選択状態の初期設定（SelectDrawableMesh / SelectBone）は
            // Viewer（PolyLingPlayerViewer）がフェッチ完了後に行う。
            // ここではレンダラー内部の GPU バッファ初期化のみ行う。
            int firstCtxIdx = model.FirstMeshIndex;
            if (firstCtxIdx < 0)
            {
                // SelectedDrawableMeshIndices が未設定の場合は
                // DrawableMeshes から頂点数 > 0 の先頭を探す（フォールバック）
                var drawables = model.DrawableMeshes;
                if (drawables != null)
                    foreach (var entry in drawables)
                    {
                        var ctx = entry.Context;
                        if (ctx?.MeshObject != null && ctx.MeshObject.VertexCount > 0 && ctx.IsVisible)
                        { firstCtxIdx = entry.MasterIndex; break; }
                    }
            }

            int firstUnified = (firstCtxIdx >= 0)
                ? (adapter.BufferManager?.ContextToUnifiedMeshIndex(firstCtxIdx) ?? 0) : 0;
            adapter.BufferManager?.SetActiveMesh(0, firstUnified);
            adapter.BufferManager?.UpdateAllSelectionFlags();

            // WorldMatrix を使って初期表示位置を確定する。
            // MeshFilter は WorldMatrix を直接使用、スキンドは SkinningMatrix を使用。
            // （ComputeMeshFilterBindPoses は呼ばない: BindPose=WorldMatrix.inverse にすると
            //   SkinningMatrix=identity になり全メッシュがローカル原点に表示されてしまうため）
            adapter.UpdateTransform(useWorldTransform: true);
            adapter.WritebackTransformedVertices();

            _adapters[mi] = adapter;

            adapter.RequestNormal();
        }

        // ================================================================
        // 描画（Phase 1: Prepare / Submit 分離）
        //
        // ・Prepare*: event 駆動で呼ぶ。計算・CPU Mesh 再構築・ComputeBuffer 更新・
        //            Dispatch 等を含む。毎フレーム呼ぶのは禁止。
        // ・Submit*:  OnRenderObject() から毎フレーム呼ぶ。Graphics.DrawMesh 提出のみ。
        //            計算処理は一切禁止（厳守）。
        // ================================================================

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（バッファ更新、フラグ計算、マテリアル設定等）は一切禁止。
        /// 全ての準備は事前に event 駆動で済ませておくこと。
        /// 面本体の Graphics.DrawMesh 提出のみを担当する。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitMeshes(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;

            var model = project.CurrentModel;
            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            var selDrawable = model.SelectedDrawableMeshIndices;

            for (int i = 0; i < drawables.Count; i++)
            {
                var ctx = drawables[i].Context;
                if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;

                bool isSel = selDrawable.Contains(drawables[i].MasterIndex);
                bool isMirror = ctx.Type == MeshType.BakedMirror || ctx.Type == MeshType.MirrorSide;
                if (isMirror)
                {
                    if ( isSel && !ShowSelectedMirror)   continue;
                    if (!isSel && !ShowUnselectedMirror) continue;
                }
                else
                {
                    if ( isSel && !ShowSelectedMesh)   continue;
                    if (!isSel && !ShowUnselectedMesh) continue;
                }

                var mesh = ctx.UnityMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                    if (mat == null)
                    {
                        // 診断: 描画時に材質が null になる原因を1回だけ記録。
                        if (!_matDbgLogged)
                        {
                            _matDbgLogged = true;
                            Debug.Log($"[MatDbg] MaterialCount={model.MaterialCount} subMeshCount={mesh.subMeshCount} sub={sub} mesh=\"{ctx.Name}\"");
                        }
                        mat = GetDefaultMaterial();
                    }
                    if (mat == null) continue;

                    if (mat.HasProperty("_Cull"))
                    {
                        var matRef = (sub < model.MaterialCount) ? model.GetMaterialReference(sub) : null;
                        bool isMaterialDoubleSide = matRef != null
                            && matRef.Data.CullMode == Poly_Ling.Materials.CullModeType.Off;
                        float cullValue = (!BackfaceCullingEnabled || isMaterialDoubleSide) ? 0f : 2f;
                        mat.SetFloat("_Cull", cullValue);
                    }

                    Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, cam, sub);
                }
            }
        }

        /// <summary>
        /// 【旧 API】面本体を描画する。Phase 1 暫定として残置。
        /// 内部は SubmitMeshes へ委譲。新規コードは SubmitMeshes を直接呼ぶこと。
        /// </summary>
        public void DrawMeshes(ProjectContext project, Camera cam) => SubmitMeshes(project, cam);

        /// <summary>
        /// 【event 駆動で呼ぶ】指定 slot の辺・頂点描画に必要な計算を行う。
        /// CPU Mesh 再構築 / ComputeBuffer 更新 / Dispatch / Queue 登録を含む重い処理。
        /// Phase 1: カメラ操作・選択変更・トポロジ変更イベント等から呼び出される想定。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// <param name="project">
        /// ProjectContext を渡すと選択状態（VertexSelected 等）を GPU に正しく反映する。
        /// null の場合は選択フラグ更新をスキップする。
        /// </param>
        /// </summary>
        public void PrepareWireframeAndVertices(Camera cam, ProjectContext project = null, int cullingSlot = 0)
        {
            if (cam == null) return;
            float pointSize = ShaderColorSettings.Default.VertexPointScale;

            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;

                adapter.CleanupQueued(cullingSlot);
                adapter.BackfaceCullingEnabled = BackfaceCullingEnabled;

                var profile = adapter.CurrentProfile;

                int selIdx = (mi < _selectedMeshIndexForDraw.Count)
                    ? _selectedMeshIndexForDraw[mi] : -1;

                // ---- AllowSelectedDrawableMeshSync ----
                if (profile.AllowSelectedDrawableMeshSync && project != null && mi < project.ModelCount)
                {
                    var bufMgr = adapter.BufferManager;
                    if (bufMgr != null)
                    {
                        var model = project.Models[mi];
                        bufMgr.SyncSelectionFromModel(model);
                        if (selIdx >= 0) bufMgr.SetActiveMesh(0, selIdx);
                        bufMgr.UpdateAllSelectionFlags();
                    }
                }

                // ---- AllowGpuVisibility ----
                // Normal モード（ワンショット）のときのみ実行。
                // Player では DispatchCullingForDisplay が per-slot で呼ばれるため
                // ここでは slot 0 固定（Editor 単一ビューポート用）。
                if (profile.AllowGpuVisibility)
                {
                    var bufMgr = adapter.BufferManager;
                    if (bufMgr != null)
                    {
                        var viewport = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);
                        bufMgr.SetMirrorDisplay(cullingSlot, ShowSelectedMirror, ShowUnselectedMirror);
                        bufMgr.DispatchClearBuffersGPU();
                        bufMgr.DispatchClearCulledBuffersGPU(cullingSlot);
                        bufMgr.ComputeScreenPositionsGPU(
                            cam.projectionMatrix * cam.worldToCameraMatrix, viewport, cullingSlot);
                        bufMgr.DispatchFaceVisibilityGPU(cullingSlot);
                        bufMgr.DispatchLineVisibilityGPU(cullingSlot);
                        bufMgr.DispatchApplyMirrorCullGPU(cullingSlot);
                    }
                }

                adapter.PrepareDrawing(
                    cam,
                    showWireframe:           ShowSelectedWireframe,
                    showVertices:            ShowSelectedVertices,
                    showUnselectedWireframe: ShowUnselectedWireframe && profile.AllowUnselectedOverlay,
                    showUnselectedVertices:  ShowUnselectedVertices && profile.AllowUnselectedOverlay,
                    selectedMeshIndex:       selIdx,
                    pointSize:               pointSize,
                    cullingSlot:             cullingSlot);
                adapter.ConsumeNormalMode();
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理は一切禁止。全ての準備は PrepareWireframeAndVertices で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitWireframeAndVertices(Camera cam, int cullingSlot = 0)
        {
            if (cam == null) return;
            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;
                adapter.DrawQueued(cam, cullingSlot);
            }
        }

        /// <summary>
        /// 【旧 API】辺・頂点を描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawWireframeAndVertices(Camera cam, ProjectContext project = null, int cullingSlot = 0)
        {
            PrepareWireframeAndVertices(cam, project, cullingSlot);
            SubmitWireframeAndVertices(cam, cullingSlot);
        }

        /// <summary>
        /// 【event 駆動で呼ぶ】ボーン描画用のラインメッシュを事前構築・更新する。
        /// 各ボーンの pos/rot/col を抽出し、_boneMeshCache を再構築。
        /// Phase 1: ボーンポーズ変更・ボーン選択変更・モデルロード時に呼び出す想定。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// </summary>
        public void PrepareBones(ProjectContext project)
        {
            if (project == null) return;
            if (!ShowSelectedBone && !ShowUnselectedBone) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var selBones = model.SelectedBoneIndices;

                for (int ci = 0; ci < model.MeshContextCount; ci++)
                {
                    var ctx = model.GetMeshContext(ci);
                    if (ctx == null || ctx.Type != MeshType.Bone) continue;

                    bool isSel = selBones.Contains(ci);
                    if ( isSel && !ShowSelectedBone)   continue;
                    if (!isSel && !ShowUnselectedBone) continue;

                    if (!ExtractBoneTransform(ctx.WorldMatrix, out Vector3 pos, out Quaternion rot)) continue;

                    Color col = isSel ? BoneWireSelColor : BoneWireColor;
                    var key = (mi, ci);
                    if (!_boneMeshCache.TryGetValue(key, out var boneMesh) || boneMesh == null)
                    {
                        boneMesh = BuildBoneLineMesh(pos, rot, col);
                        _boneMeshCache[key] = boneMesh;
                    }
                    else
                    {
                        UpdateBoneLineMesh(boneMesh, pos, rot, col);
                    }
                }
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（BuildBoneLineMesh / UpdateBoneLineMesh / ExtractBoneTransform 等）は
        /// 一切禁止。全ての準備は PrepareBones で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitBones(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;
            if (!ShowSelectedBone && !ShowUnselectedBone) return;

            // Phase 2c-2: 選択/非選択で別マテリアル（global alpha が異なる）。
            var matSel   = GetBoneOverlayMaterial(isSelected: true);
            var matUnsel = GetBoneOverlayMaterial(isSelected: false);
            if (matSel == null && matUnsel == null) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                var selBones = model.SelectedBoneIndices;

                for (int ci = 0; ci < model.MeshContextCount; ci++)
                {
                    var ctx = model.GetMeshContext(ci);
                    if (ctx == null || ctx.Type != MeshType.Bone) continue;

                    bool isSel = selBones.Contains(ci);
                    if ( isSel && !ShowSelectedBone)   continue;
                    if (!isSel && !ShowUnselectedBone) continue;

                    var chosenMat = isSel ? matSel : matUnsel;
                    if (chosenMat == null) continue;

                    var key = (mi, ci);
                    if (_boneMeshCache.TryGetValue(key, out var boneMesh) && boneMesh != null)
                        Graphics.DrawMesh(boneMesh, Matrix4x4.identity, chosenMat, 0, cam);
                }
            }
        }

        /// <summary>
        /// 【旧 API】ボーンを描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawBones(ProjectContext project, Camera cam)
        {
            PrepareBones(project);
            SubmitBones(project, cam);
        }

        // ================================================================
        // スキンウェイト可視化描画
        // ================================================================

        /// <summary>
        /// スキンウェイトペイントモード時にウェイトをヒートマップカラーで描画する。
        /// DrawMeshes の直後に呼ぶこと。
        /// </summary>
        /// <summary>
        /// 【event 駆動で呼ぶ】ウェイトヒートマップ用の頂点カラーを事前計算する。
        /// ApplyVisualizationColors は Mesh.colors への書き込みを含む重い処理。
        /// Phase 1: スキンウェイトパネル操作・選択ボーン変更・ターゲットメッシュ変更時に呼び出す。
        /// Submit と分離されているため、毎フレーム呼ぶのは禁止。
        /// </summary>
        public void PrepareWeightVisualization(ProjectContext project)
        {
            if (!Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive) return;
            if (project == null) return;

            var model = project.CurrentModel;
            if (model == null) return;

            int targetBone = Poly_Ling.Tools.SkinWeightPaintTool.VisualizationTargetBone;

            int targetMeshIdx = Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel?.CurrentTargetMesh ?? -1;
            var masterIndices = targetMeshIdx >= 0
                ? new System.Collections.Generic.List<int> { targetMeshIdx }
                : new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);

            foreach (int masterIdx in masterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                if (ctx?.UnityMesh == null || ctx.MeshObject == null || !ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                Poly_Ling.Tools.SkinWeightPaintTool.ApplyVisualizationColors(mesh, ctx.MeshObject, targetBone);
            }
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 計算処理（ApplyVisualizationColors 等）は一切禁止。
        /// 全ての準備は PrepareWeightVisualization で完了させておくこと。
        /// OnRenderObject() から毎フレーム呼ばれる想定。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void SubmitWeightVisualization(ProjectContext project, Camera cam)
        {
            if (!Poly_Ling.Tools.SkinWeightPaintTool.IsVisualizationActive) return;
            if (project == null || cam == null) return;

            var visMat = Poly_Ling.Tools.SkinWeightPaintTool.GetVisualizationMaterial();
            if (visMat == null) return;

            var model = project.CurrentModel;
            if (model == null) return;

            int targetMeshIdx = Poly_Ling.Tools.SkinWeightPaintTool.ActivePanel?.CurrentTargetMesh ?? -1;
            var masterIndices = targetMeshIdx >= 0
                ? new System.Collections.Generic.List<int> { targetMeshIdx }
                : new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);

            foreach (int masterIdx in masterIndices)
            {
                var ctx = model.GetMeshContext(masterIdx);
                if (ctx?.UnityMesh == null || ctx.MeshObject == null || !ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                // 通常描画 SubmitMeshes と同じく identity で描画する。
                // 頂点はワールド化済み（スキンドメッシュ）/GPU compute 側で処理されるため、
                // ここで ctx.WorldMatrix を掛けると二重変換になりずれる。
                var displayMatrix = Matrix4x4.identity;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    Graphics.DrawMesh(mesh, displayMatrix, visMat, 0, cam, sub);
            }
        }

        /// <summary>
        /// 【旧 API】ウェイト可視化を描画する。Phase 1 暫定として残置。
        /// 内部は Prepare + Submit を連続呼びする。新規コードは分離して呼ぶこと。
        /// </summary>
        public void DrawWeightVisualization(ProjectContext project, Camera cam)
        {
            PrepareWeightVisualization(project);
            SubmitWeightVisualization(project, cam);
        }

        // ================================================================
        // シーンクリア
        // ================================================================

        [System.Obsolete(
            "【規約違反入口】6つの Enter* 正規入口 (PlayerViewportManager 上の " +
            "EnterProjectChanged / EnterTopologyChanged / EnterCameraChanged / " +
            "EnterVerticesMoved / EnterHoverChanged / EnterDisplaySettingsChanged) " +
            "経由で呼ぶこと。本 API を Player 配下の Core / Dispatcher / RemoteFlow から " +
            "直接呼ぶことは禁止。",
            error: false)]
        public void ClearScene()
        {
            foreach (var adapter in _adapters)
            {
                // ClearScene では全 slot を対象にクリア（Dispose 前処理）
#pragma warning disable CS0618
                adapter?.CleanupQueued();
#pragma warning restore CS0618
                adapter?.Dispose();
            }
            _adapters.Clear();
            _selectedMeshIndexForDraw.Clear();

            foreach (var mesh in _boneMeshCache.Values)
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            _boneMeshCache.Clear();

            if (_boneMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneMaterial);
                _boneMaterial = null;
            }
            // Phase 2c-2: ボーン overlay 用マテリアルも破棄
            if (_boneOverlayMaterialSelected != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneOverlayMaterialSelected);
                _boneOverlayMaterialSelected = null;
            }
            if (_boneOverlayMaterialUnselected != null)
            {
                UnityEngine.Object.DestroyImmediate(_boneOverlayMaterialUnselected);
                _boneOverlayMaterialUnselected = null;
            }
            if (_defaultMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_defaultMaterial);
                _defaultMaterial = null;
            }
        }

        // ================================================================
        // オービットターゲット初期化ヘルパー
        // ================================================================

        /// <summary>最初のDrawableメッシュのboundsを返す。カメラ初期位置計算に使用。</summary>
        public bool TryGetInitialBounds(ProjectContext project, out Bounds bounds)
        {
            bounds = default;
            if (project == null) return false;
            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var drawables = project.Models[mi].DrawableMeshes;
                if (drawables == null) continue;
                foreach (var entry in drawables)
                {
                    var mesh = entry.Context?.UnityMesh;
                    if (mesh != null) { bounds = mesh.bounds; return true; }
                }
            }
            return false;
        }

        // ================================================================
        // Dispose
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
#pragma warning disable CS0618
            ClearScene();
#pragma warning restore CS0618
        }

        // ================================================================
        // マテリアルヘルパー
        // ================================================================

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Standard")
                      ?? Shader.Find("Unlit/Color");
            if (shader == null) return null;
            _defaultMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _defaultMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
            _defaultMaterial.SetColor("_Color",     new Color(0.7f, 0.7f, 0.7f));
            return _defaultMaterial;
        }

        private Material GetBoneMaterial()
        {
            // Phase 2c-2 以降の新規コードは GetBoneOverlayMaterial(isSelected) を使うこと。
            // 本メソッドは後方互換のため残置。
            return GetBoneOverlayMaterial(isSelected: true);
        }

        /// <summary>
        /// Phase 2c-2: ボーン overlay 用マテリアル（ZTest Always、常に最前面）。
        /// 選択/非選択で global alpha を切り替えて保持する。
        /// </summary>
        private Material GetBoneOverlayMaterial(bool isSelected)
        {
            if (isSelected)
            {
                if (_boneOverlayMaterialSelected != null) return _boneOverlayMaterialSelected;
                var shader = Shader.Find("Poly_Ling/Bone3D_Overlay");
                if (shader == null) return null;
                _boneOverlayMaterialSelected = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // 選択ボーンは不透明
                _boneOverlayMaterialSelected.SetFloat("_GlobalAlpha", 1.0f);
                return _boneOverlayMaterialSelected;
            }
            else
            {
                if (_boneOverlayMaterialUnselected != null) return _boneOverlayMaterialUnselected;
                var shader = Shader.Find("Poly_Ling/Bone3D_Overlay");
                if (shader == null) return null;
                _boneOverlayMaterialUnselected = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // 非選択ボーンはボディと干渉しないよう半透明化
                _boneOverlayMaterialUnselected.SetFloat("_GlobalAlpha", 0.5f);
                return _boneOverlayMaterialUnselected;
            }
        }

        // ================================================================
        // ボーンメッシュ構築
        // ================================================================

        private static Mesh BuildBoneLineMesh(Vector3 pos, Quaternion rot, Color col)
        {
            int ec = BoneShapeEdges.GetLength(0);
            var verts   = new Vector3[ec * 2];
            var colors  = new Color[ec * 2];
            var uvs     = new Vector2[ec * 2];
            var indices = new int[ec * 2];

            for (int i = 0; i < ec; i++)
            {
                verts[i*2]   = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,0]] * BoneShapeScale);
                verts[i*2+1] = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,1]] * BoneShapeScale);
                colors[i*2] = colors[i*2+1] = col;
                uvs[i*2] = uvs[i*2+1] = Vector2.zero;
                indices[i*2] = i*2; indices[i*2+1] = i*2+1;
            }

            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            return mesh;
        }

        private static void UpdateBoneLineMesh(Mesh mesh, Vector3 pos, Quaternion rot, Color col)
        {
            int ec = BoneShapeEdges.GetLength(0);
            var verts  = new Vector3[ec * 2];
            var colors = new Color[ec * 2];
            for (int i = 0; i < ec; i++)
            {
                verts[i*2]   = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,0]] * BoneShapeScale);
                verts[i*2+1] = pos + rot * (BoneShapeVertices[BoneShapeEdges[i,1]] * BoneShapeScale);
                colors[i*2] = colors[i*2+1] = col;
            }
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
        }

        private static bool ExtractBoneTransform(Matrix4x4 m, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(m.m03, m.m13, m.m23);
            Vector3 c0 = new Vector3(m.m00, m.m10, m.m20);
            Vector3 c1 = new Vector3(m.m01, m.m11, m.m21);
            Vector3 c2 = new Vector3(m.m02, m.m12, m.m22);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sx < 0.0001f || sy < 0.0001f || sz < 0.0001f) { rot = Quaternion.identity; return false; }
            var r = Matrix4x4.identity;
            r.SetColumn(0, c0/sx); r.SetColumn(1, c1/sy); r.SetColumn(2, c2/sz);
            rot = r.rotation;
            return true;
        }
    }
}
