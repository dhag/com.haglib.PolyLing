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
        public bool BackfaceCullingEnabled    { get; set; } = true;

        // ================================================================
        // 内部状態
        // ================================================================

        // 外部から注入する SelectionState（Player用）
        private SelectionState _selectionState;

        // DrawWireframeAndVertices で PrepareDrawing に渡す selectedMeshIndex（モデルごと）。
        // model.FirstDrawableMeshIndex を adapter のコンテキストインデックスに変換した値。
        // RebuildAdapter / UpdateSelectedDrawableMesh で更新する。
        private readonly List<int>                  _selectedMeshIndexForDraw = new List<int>();

        private readonly List<UnifiedSystemAdapter> _adapters       = new List<UnifiedSystemAdapter>();
        private readonly Dictionary<(int, int), Mesh> _boneMeshCache= new Dictionary<(int, int), Mesh>();

        private Material _defaultMaterial;
        private Material _boneMaterial;
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
        public void UpdateSelectedDrawableMesh(int mi, ModelContext model)
        {
            while (_selectedMeshIndexForDraw.Count <= mi)
                _selectedMeshIndexForDraw.Add(-1);

            // PrepareDrawing の selectedMeshIndex は adapter の unifiedMeshIndex を期待する。
            // SelectedDrawableMeshIndices[0]（MeshContextList インデックス）を変換する。
            int ctxIdx = model.FirstDrawableMeshIndex;
            if (ctxIdx < 0 || mi >= _adapters.Count || _adapters[mi] == null)
            {
                _selectedMeshIndexForDraw[mi] = -1;
                return;
            }
            int unifiedIdx = _adapters[mi].BufferManager?.ContextToUnifiedMeshIndex(ctxIdx) ?? -1;
            _selectedMeshIndexForDraw[mi] = unifiedIdx;
        }

        /// <summary>選択変更をGPUバッファに通知する。</summary>
        public void NotifySelectionChanged()
        {
            foreach (var adapter in _adapters)
                adapter?.NotifySelectionChanged();
        }

        /// <summary>
        /// モデルのメッシュ受信完了後にAdapterを再構築する。
        /// </summary>
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
            int firstCtxIdx = model.FirstDrawableMeshIndex;
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
        // 描画（LateUpdateから呼ぶ）
        // ================================================================

        public void DrawMeshes(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;

            var model = project.CurrentModel;
            if (model == null) return;

            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            var selDrawable = model.SelectedDrawableMeshIndices;

            for (int i = 0; i < drawables.Count; i++)
            {
                bool isSel = selDrawable.Contains(drawables[i].MasterIndex);
                if ( isSel && !ShowSelectedMesh)   continue;
                if (!isSel && !ShowUnselectedMesh) continue;

                var ctx = drawables[i].Context;
                if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;

                var mesh = ctx.UnityMesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                    if (mat == null) mat = GetDefaultMaterial();
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

        /// <param name="project">
        /// ProjectContext を渡すと選択状態（VertexSelected 等）を GPU に正しく反映する。
        /// null の場合は選択フラグ更新をスキップする。
        /// </param>
        public void DrawWireframeAndVertices(Camera cam, ProjectContext project = null, int cullingSlot = 0)
        {
            if (cam == null) return;
            float pointSize = ShaderColorSettings.Default.VertexPointScale;

            for (int mi = 0; mi < _adapters.Count; mi++)
            {
                var adapter = _adapters[mi];
                if (adapter == null || !adapter.IsInitialized) continue;

                adapter.CleanupQueued();
                adapter.BackfaceCullingEnabled = BackfaceCullingEnabled;

                var profile = adapter.CurrentProfile;

                int selIdx = (mi < _selectedMeshIndexForDraw.Count)
                    ? _selectedMeshIndexForDraw[mi] : -1;

                // ---- AllowSelectionSync ----
                if (profile.AllowSelectionSync && project != null && mi < project.ModelCount)
                {
                    var bufMgr = adapter.BufferManager;
                    if (bufMgr != null)
                    {
                        var model = project.Models[mi];
                        bool needSwap = model.SelectedMeshIndices.Count == 0
                                     && model.SelectedDrawableMeshIndices.Count > 0;
                        if (needSwap)
                            foreach (var idx in model.SelectedDrawableMeshIndices)
                                model.SelectedMeshIndices.Add(idx);

                        bufMgr.SyncSelectionFromModel(model);
                        if (selIdx >= 0) bufMgr.SetActiveMesh(0, selIdx);
                        bufMgr.UpdateAllSelectionFlags();

                        if (needSwap) model.SelectedMeshIndices.Clear();
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
                        bufMgr.DispatchClearBuffersGPU();
                        bufMgr.DispatchClearCulledBuffersGPU(cullingSlot);
                        bufMgr.ComputeScreenPositionsGPU(
                            cam.projectionMatrix * cam.worldToCameraMatrix, viewport, cullingSlot);
                        bufMgr.DispatchFaceVisibilityGPU(cullingSlot);
                        bufMgr.DispatchLineVisibilityGPU(cullingSlot);
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
                adapter.DrawQueued(cam);
            }
        }

        public void DrawBones(ProjectContext project, Camera cam)
        {
            if (project == null || cam == null) return;
            if (!ShowSelectedBone && !ShowUnselectedBone) return;

            var mat = GetBoneMaterial();
            if (mat == null) return;

            for (int mi = 0; mi < project.ModelCount; mi++)
            {
                var model = project.Models[mi];
                // model.SelectedBoneIndices（MeshContextList インデックス）で
                // 選択ボーンを判定する。
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

                    if (boneMesh != null)
                        Graphics.DrawMesh(boneMesh, Matrix4x4.identity, mat, 0, cam);
                }
            }
        }

        // ================================================================
        // シーンクリア
        // ================================================================

        public void ClearScene()
        {
            foreach (var adapter in _adapters)
            {
                adapter?.CleanupQueued();
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
            ClearScene();
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
            if (_boneMaterial != null) return _boneMaterial;
            var shader = Shader.Find("Poly_Ling/Wireframe3D");
            if (shader == null) return null;
            _boneMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _boneMaterial.SetInt("_UseLineFlagsBuffer",    0);
            _boneMaterial.SetInt("_EnableBackfaceCulling", 0);
            return _boneMaterial;
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
