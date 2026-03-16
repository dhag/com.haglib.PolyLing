// ViewportCore.cs
// 再利用可能な3Dビューポートコンポーネント（EditorWindow非依存）
// メッシュ描画、ワイヤー/頂点、カメラ制御、2Dオーバーレイを提供
// 単体パネル・3面図・本体ビューポートいずれにも組み込み可能

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Core;
using Poly_Ling.Core.Rendering;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.MeshListV2
{
    // ================================================================
    // コールバックに渡すイベント情報
    // ================================================================

    public struct ViewportEvent
    {
        public Rect Rect;
        public Vector3 CameraPos;
        public Vector3 CameraTarget;
        public float CameraDistance;
        public float CameraFOV;
        public float RotX, RotY, RotZ;
        public Camera Camera;
        public ModelContext Model;
    }

    // ================================================================
    // ViewportCore
    // ================================================================

    public class ViewportCore : IDisposable
    {
        // ================================================================
        // レンダリング
        // ================================================================

        private PreviewRenderUtility _preview;
        private UnifiedSystemAdapter _adapter;
        private SelectionState _selectionState;
        private Material _defaultMaterial;
        private Material _polygonMaterial;

        // ================================================================
        // モデル追跡
        // ================================================================

        private ModelContext _currentModel;
        private MeshContext _trackedMeshCtx;

        // ================================================================
        // カメラ
        // ================================================================

        public float RotX { get; set; } = 15f;
        public float RotY { get; set; } = -30f;
        public float RotZ { get; set; }
        public float Distance { get; set; } = 3f;
        public Vector3 Target { get; set; } = Vector3.zero;
        public bool IsOrtho { get; set; } = false;

        // カメラ操作の内部状態
        private bool _isDragging;
        private bool _isPanning;

        // ================================================================
        // 表示設定
        // ================================================================

        public bool ShowMesh { get; set; } = true;
        public bool ShowWireframe { get; set; } = true;
        public bool ShowVertices { get; set; } = false;
        public bool ShowUnselectedWireframe { get; set; } = true;
        public bool ShowUnselectedVertices { get; set; } = false;
        public bool ShowSelectedMeshOnly { get; set; } = false;
        public bool BackfaceCulling { get; set; } = true;
        public bool ShowVertexIndices { get; set; } = false;
        public bool ShowBones { get; set; } = true;
        public bool ShowFocusPoint { get; set; } = true;
        public bool ShowUnselectedBones { get; set; } = false;

        // ================================================================
        // コールバック（用途依存の処理を外部から注入）
        // ================================================================

        /// <summary>入力処理（選択、ツール等。nullなら入力なし）</summary>
        public Action<ViewportEvent> OnHandleInput;

        /// <summary>追加オーバーレイ描画（ギズモ、矩形選択等。nullなら追加描画なし）</summary>
        public Action<ViewportEvent> OnDrawOverlay;

        /// <summary>再描画要求（所有者のRepaintを呼ぶ）</summary>
        public Action RequestRepaint;

        /// <summary>表示用行列取得（LocalTransform/WorldTransform対応）。nullならIdentity返却</summary>
        public Func<int, Matrix4x4> GetDisplayMatrixDelegate;

        /// <summary>カスタムメッシュ描画。trueを返すと通常描画をスキップ</summary>
        public Func<PreviewRenderUtility, MeshContext, Mesh, int, Matrix4x4, bool> CustomDrawMesh;

        /// <summary>プレビューキャプチャフック（RemoteServer等）</summary>
        public Action<Texture> OnCapture;

        // ================================================================
        // 公開プロパティ
        // ================================================================

        public ModelContext CurrentModel => _currentModel;
        public UnifiedSystemAdapter Adapter => _adapter;
        public Camera Camera => _preview?.camera;
        public float FOV => _preview?.cameraFieldOfView ?? 30f;

        // ================================================================
        // 初期化・クリーンアップ
        // ================================================================

        public bool Init(ModelContext model)
        {
            InitPreview();

            if (model == null) return true;

            _adapter = new UnifiedSystemAdapter();
            if (!_adapter.Initialize())
            {
                Debug.LogError("[ViewportCore] Failed to initialize UnifiedSystemAdapter");
                _adapter?.Dispose();
                _adapter = null;
                return false;
            }

            _adapter.SetModelContext(model);

            _trackedMeshCtx = model.FirstSelectedMeshContext;
            _selectionState = _trackedMeshCtx?.Selection ?? new SelectionState();
            _adapter.SetSelectionState(_selectionState);

            var sym = model.SymmetrySettings;
            if (sym != null) _adapter.SetSymmetrySettings(sym);

            _adapter.UseUnifiedRendering = true;
            _adapter.RequestNormal();

            _currentModel = model;
            return true;
        }

        public void Dispose()
        {
            _adapter?.Dispose();
            _adapter = null;
            _selectionState = null;
            _trackedMeshCtx = null;
            _currentModel = null;

            if (_preview != null) { _preview.Cleanup(); _preview = null; }
            if (_defaultMaterial != null) { UnityEngine.Object.DestroyImmediate(_defaultMaterial); _defaultMaterial = null; }
            if (_polygonMaterial != null) { UnityEngine.Object.DestroyImmediate(_polygonMaterial); _polygonMaterial = null; }
        }

        private void InitPreview()
        {
            if (_preview != null) return;
            _preview = new PreviewRenderUtility();
            _preview.cameraFieldOfView = 30f;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 200f;
        }

        // ================================================================
        // モデル追跡
        // ================================================================

        /// <summary>モデルが変わった場合にアダプターを再構築</summary>
        public void SetModel(ModelContext model)
        {
            if (model == _currentModel) return;

            _adapter?.Dispose();
            _adapter = null;
            _selectionState = null;
            _trackedMeshCtx = null;
            _currentModel = null;

            if (model != null)
                Init(model);
        }

        /// <summary>選択メッシュ変更時にSelectionStateを差し替え</summary>
        public void SyncSelectionState()
        {
            if (_adapter == null || _currentModel == null) return;
            var meshCtx = _currentModel.FirstSelectedMeshContext;
            if (meshCtx != _trackedMeshCtx)
            {
                _trackedMeshCtx = meshCtx;
                _selectionState = meshCtx?.Selection ?? new SelectionState();
                _adapter.SetSelectionState(_selectionState);
                _adapter.RequestNormal();
            }
        }

        /// <summary>トポロジ変更通知</summary>
        public void NotifyTopologyChanged()
        {
            if (_adapter != null && _currentModel != null)
            {
                _adapter.SetModelContext(_currentModel);
                _adapter.NotifyTopologyChanged();
            }
        }

        /// <summary>属性/選択変更通知</summary>
        public void RequestNormal()
        {
            _adapter?.RequestNormal();
        }

        // ================================================================
        // メイン描画ループ（IMGUIContainerから呼ぶ）
        // ================================================================

        public void Draw(Rect rect)
        {
            if (_preview == null) return;

            var model = _currentModel;

            // カメラ入力
            HandleCameraInput(rect);

            // 外部入力処理
            if (OnHandleInput != null && model != null)
            {
                var evt = MakeEvent(rect, model);
                OnHandleInput(evt);
            }

            if (Event.current.type != EventType.Repaint) return;
            if (model == null) return;

            model.ComputeWorldMatrices();

            // カメラセットアップ
            Quaternion rot = Quaternion.Euler(RotX, RotY, RotZ);
            Vector3 camPos = Target + rot * new Vector3(0, 0, -Distance);
            Quaternion lookRot = Quaternion.LookRotation(Target - camPos, Vector3.up);
            Quaternion rollRot = Quaternion.AngleAxis(RotZ, Vector3.forward);

            _preview.BeginPreview(rect, GUIStyle.none);

            var bgColors = _adapter?.ColorSettings ?? ShaderColorSettings.Default;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = bgColors.Background;
            _preview.camera.transform.position = camPos;
            _preview.camera.transform.rotation = lookRot * rollRot;
            _preview.camera.orthographic = IsOrtho;

            // UnifiedSystem フレーム更新
            if (_adapter != null)
            {
                Vector2 mousePos = Event.current.mousePosition;
                _adapter.UpdateFrame(camPos, Target, _preview.cameraFieldOfView, rect, mousePos, RotZ);
            }

            // メッシュ描画
            if (ShowMesh)
                DrawMeshes(model);

            // ワイヤフレーム・頂点描画
            if (_adapter != null)
                DrawUnified(model);

            _preview.camera.Render();
            _adapter?.CleanupQueued();

            Texture result = _preview.EndPreview();
            GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
            OnCapture?.Invoke(result);

            // ================================================================
            // 2Dオーバーレイ
            // ================================================================
            var cam = _preview.camera;

            if (ShowWireframe && _adapter != null)
            {
                var meshCtx = model.FirstSelectedMeshContext;
                if (meshCtx?.MeshObject != null)
                {
                    DrawHoveredFace(rect, meshCtx, cam);
                    DrawSelectedFaces(rect, meshCtx, cam);
                }
            }

            if (ShowVertexIndices && _adapter != null)
                DrawVertexIndices(rect, model, cam);

            if (ShowBones)
                DrawBones(rect, model, cam);

            DrawSymmetryPlane(rect, model, cam);

            if (ShowFocusPoint)
                DrawFocusPointMarker(rect, cam);

            DrawOriginMarker(rect, cam);

            // 外部追加オーバーレイ
            if (OnDrawOverlay != null)
            {
                var evt = MakeEvent(rect, model);
                OnDrawOverlay(evt);
            }
        }

        // ================================================================
        // カメラ操作
        // ================================================================

        private void HandleCameraInput(Rect rect)
        {
            // OnHandleInputが設定されている場合、カメラ操作は中ボタンPanのみ
            // 左ボタン(選択)、右ボタン(回転)、スクロール(ズーム)はOnHandleInput側が処理
            if (OnHandleInput != null)
            {
                HandleMiddleButtonPan(rect);
                return;
            }

            // スタンドアロンモード: 全カメラ操作を自前で処理
            HandleFullCameraInput(rect);
        }

        /// <summary>中ボタンPanのみ（OnHandleInput併用時）</summary>
        private void HandleMiddleButtonPan(Rect rect)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_isPanning) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 2 && rect.Contains(e.mousePosition))
                    {
                        _isPanning = true;
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_isPanning)
                    {
                        Quaternion rot = Quaternion.Euler(RotX, RotY, 0);
                        Vector3 right = rot * Vector3.right;
                        Vector3 up = rot * Vector3.up;
                        float scale = Distance * 0.002f;
                        Target -= right * e.delta.x * scale;
                        Target += up * e.delta.y * scale;
                        _adapter?.RequestNormal();
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;
                case EventType.MouseUp:
                    if (_isPanning)
                    {
                        _isPanning = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.MouseMove:
                    RequestRepaint?.Invoke();
                    break;
            }
        }

        /// <summary>全カメラ操作（スタンドアロンモード）</summary>
        private void HandleFullCameraInput(Rect rect)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition) && !_isDragging && !_isPanning) return;

            int id = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.ScrollWheel:
                    Distance *= 1f + e.delta.y * 0.05f;
                    Distance = Mathf.Clamp(Distance, 0.05f, 100f);
                    _adapter?.RequestNormal();
                    e.Use();
                    RequestRepaint?.Invoke();
                    break;

                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition))
                    {
                        if (e.button == 0 || e.button == 1)
                        {
                            _isDragging = true;
                            GUIUtility.hotControl = id;
                            e.Use();
                        }
                        if (e.button == 2)
                        {
                            _isPanning = true;
                            GUIUtility.hotControl = id;
                            e.Use();
                        }
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        RotY += e.delta.x * 0.5f;
                        RotX += e.delta.y * 0.5f;
                        RotX = Mathf.Clamp(RotX, -89f, 89f);
                        _adapter?.RequestNormal();
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    if (_isPanning)
                    {
                        Quaternion rot = Quaternion.Euler(RotX, RotY, 0);
                        Vector3 right = rot * Vector3.right;
                        Vector3 up = rot * Vector3.up;
                        float scale = Distance * 0.002f;
                        Target -= right * e.delta.x * scale;
                        Target += up * e.delta.y * scale;
                        _adapter?.RequestNormal();
                        e.Use();
                        RequestRepaint?.Invoke();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDragging || _isPanning)
                    {
                        _isDragging = false;
                        _isPanning = false;
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;

                case EventType.MouseMove:
                    RequestRepaint?.Invoke();
                    break;
            }
        }

        // ================================================================
        // メッシュ描画
        // ================================================================

        private void DrawMeshes(ModelContext model)
        {
            var drawables = model.DrawableMeshes;
            if (drawables == null) return;

            Material defMat = GetDefaultMaterial();

            if (ShowSelectedMeshOnly)
            {
                if (model.SelectedMeshIndices.Count > 0)
                {
                    foreach (int idx in model.SelectedMeshIndices)
                    {
                        if (idx < 0 || idx >= model.MeshContextCount) continue;
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;
                        DrawSingleMesh(ctx, model, defMat, idx);
                    }
                }
            }
            else
            {
                for (int i = 0; i < drawables.Count; i++)
                {
                    var ctx = drawables[i].Context;
                    if (ctx?.UnityMesh == null || !ctx.IsVisible) continue;

                    int masterIdx = drawables[i].MasterIndex;
                    DrawSingleMesh(ctx, model, defMat, masterIdx);
                }
            }
        }

        private void DrawSingleMesh(MeshContext ctx, ModelContext model, Material defMat, int masterIdx)
        {
            var mesh = ctx.UnityMesh;
            Matrix4x4 displayMatrix = GetDisplayMatrixDelegate?.Invoke(masterIdx) ?? Matrix4x4.identity;

            if (CustomDrawMesh != null && CustomDrawMesh(_preview, ctx, mesh, masterIdx, displayMatrix))
                return;

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                Material mat = (sub < model.MaterialCount) ? model.GetMaterial(sub) : null;
                if (mat == null) mat = defMat;
                _preview.DrawMesh(mesh, displayMatrix, mat, sub);
            }
        }

        // ================================================================
        // Unified描画（ワイヤフレーム・頂点）
        // ================================================================

        private void DrawUnified(ModelContext model)
        {
            if (!ShowWireframe && !ShowVertices) return;

            _adapter.BackfaceCullingEnabled = BackfaceCulling;

            var profile = _adapter.CurrentProfile;

            int selectedIdx = -1;
            if (model.SelectedMeshIndices.Count == 1)
                selectedIdx = _adapter.ContextToUnifiedMeshIndex(model.SelectedMeshIndices[0]);

            if (profile.AllowSelectionSync)
            {
                var bufMgr = _adapter.BufferManager;
                if (bufMgr != null)
                {
                    bufMgr.SyncSelectionFromModel(model);
                    bufMgr.SetActiveMesh(0, selectedIdx);
                    bufMgr.UpdateAllSelectionFlags();
                }
            }

            if (profile.AllowGpuVisibility)
            {
                var bufMgr = _adapter.BufferManager;
                if (bufMgr != null)
                {
                    var cam = _preview.camera;
                    var viewport = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);
                    bufMgr.DispatchClearBuffersGPU();
                    bufMgr.ComputeScreenPositionsGPU(cam.projectionMatrix * cam.worldToCameraMatrix, viewport);
                    bufMgr.DispatchFaceVisibilityGPU();
                    bufMgr.DispatchLineVisibilityGPU();
                }
            }

            int meshIdxForDraw = (model.SelectedMeshIndices.Count > 1) ? -1 : selectedIdx;
            var pointSize = ShaderColorSettings.Default.VertexPointScale;

            _adapter.PrepareDrawing(
                _preview.camera,
                ShowWireframe, ShowVertices,
                ShowUnselectedWireframe && profile.AllowUnselectedOverlay,
                ShowUnselectedVertices && profile.AllowUnselectedOverlay,
                meshIdxForDraw, pointSize);

            _adapter.ConsumeNormalMode();
            _adapter.DrawQueued(_preview);
        }

        // ================================================================
        // ホバー面オーバーレイ
        // ================================================================

        private void DrawHoveredFace(Rect rect, MeshContext meshCtx, Camera cam)
        {
            var sys = _adapter?.UnifiedSystem;
            if (sys == null) return;
            if (!sys.GetHoveredFaceLocal(out int hovMeshIdx, out int localFace)) return;

            int selectedMaster = _currentModel.SelectedMeshIndices.Count > 0
                ? _currentModel.SelectedMeshIndices[0] : -1;
            int selectedUnified = _adapter.ContextToUnifiedMeshIndex(selectedMaster);
            if (hovMeshIdx != selectedUnified) return;

            var meshObj = meshCtx.MeshObject;
            if (localFace < 0 || localFace >= meshObj.FaceCount) return;
            var face = meshObj.Faces[localFace];
            if (face.VertexCount < 3) return;

            Matrix4x4 dm = GetDisplayMatrixDelegate?.Invoke(selectedMaster) ?? Matrix4x4.identity;
            bool useIdentity = (dm == Matrix4x4.identity);

            var pts = new Vector2[face.VertexCount];
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vi = face.VertexIndices[i];
                if (vi < 0 || vi >= meshObj.VertexCount) return;
                Vector3 pos = meshObj.Vertices[vi].Position;
                if (!useIdentity) pos = dm.MultiplyPoint3x4(pos);
                pts[i] = WorldToGUI(pos, cam, rect);
            }

            var colors = _adapter.ColorSettings ?? ShaderColorSettings.Default;
            DrawFilledPolygon(pts, colors.FaceHoveredFill);

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = colors.FaceHoveredEdge;
            for (int i = 0; i < face.VertexCount; i++)
                UnityEditor_Handles.DrawAAPolyLine(2f, pts[i], pts[(i + 1) % face.VertexCount]);
            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // 選択面オーバーレイ
        // ================================================================

        private void DrawSelectedFaces(Rect rect, MeshContext meshCtx, Camera cam)
        {
            if (meshCtx.SelectedFaces.Count == 0) return;
            var meshObj = meshCtx.MeshObject;
            var colors = _adapter?.ColorSettings ?? ShaderColorSettings.Default;

            int masterIdx = _currentModel.SelectedMeshIndices.Count > 0
                ? _currentModel.SelectedMeshIndices[0] : -1;
            Matrix4x4 dm = GetDisplayMatrixDelegate?.Invoke(masterIdx) ?? Matrix4x4.identity;
            bool useIdentity = (dm == Matrix4x4.identity);

            foreach (int fi in meshCtx.SelectedFaces)
            {
                if (fi < 0 || fi >= meshObj.FaceCount) continue;
                var face = meshObj.Faces[fi];
                if (face.VertexCount < 3) continue;

                var pts = new Vector2[face.VertexCount];
                bool valid = true;
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int vi = face.VertexIndices[i];
                    if (vi < 0 || vi >= meshObj.VertexCount) { valid = false; break; }
                    Vector3 pos = meshObj.Vertices[vi].Position;
                    if (!useIdentity) pos = dm.MultiplyPoint3x4(pos);
                    pts[i] = WorldToGUI(pos, cam, rect);
                }
                if (!valid) continue;

                DrawFilledPolygon(pts, colors.FaceSelectedFill);
                UnityEditor_Handles.BeginGUI();
                UnityEditor_Handles.color = colors.FaceSelectedEdge;
                for (int i = 0; i < face.VertexCount; i++)
                    UnityEditor_Handles.DrawAAPolyLine(2f, pts[i], pts[(i + 1) % face.VertexCount]);
                UnityEditor_Handles.EndGUI();
            }
        }

        // ================================================================
        // 頂点インデックス表示
        // ================================================================

        private void DrawVertexIndices(Rect rect, ModelContext model, Camera cam)
        {
            _adapter.ReadBackVertexFlags();

            foreach (int meshIdx in model.SelectedMeshIndices)
            {
                if (meshIdx < 0 || meshIdx >= model.MeshContextCount) continue;
                var ctx = model.GetMeshContext(meshIdx);
                if (ctx?.MeshObject == null || !ctx.IsVisible) continue;

                var meshObj = ctx.MeshObject;

                Vector3[] displayPos = null;
                int vertOffset = 0;
                var bufMgr = _adapter.BufferManager;
                if (bufMgr != null)
                {
                    displayPos = bufMgr.GetDisplayPositions();
                    int uIdx = _adapter.ContextToUnifiedMeshIndex(meshIdx);
                    if (uIdx >= 0 && bufMgr.MeshInfos != null)
                        vertOffset = (int)bufMgr.MeshInfos[uIdx].VertexStart;
                }

                bool isLead = (model.SelectedMeshIndices.Count <= 1) || (model.SelectedMeshIndices[0] == meshIdx);
                var style = isLead ? EditorStyles.miniLabel : EditorStyles.whiteMiniLabel;

                for (int i = 0; i < meshObj.VertexCount; i++)
                {
                    if (BackfaceCulling && _adapter.IsVertexCulled(meshIdx, i)) continue;

                    Vector3 worldPos;
                    if (displayPos != null && (vertOffset + i) < displayPos.Length)
                    {
                        worldPos = displayPos[vertOffset + i];
                    }
                    else
                    {
                        Matrix4x4 dm = GetDisplayMatrixDelegate?.Invoke(meshIdx) ?? Matrix4x4.identity;
                        worldPos = (dm == Matrix4x4.identity)
                            ? meshObj.Vertices[i].Position
                            : dm.MultiplyPoint3x4(meshObj.Vertices[i].Position);
                    }

                    Vector2 sp = WorldToGUI(worldPos, cam, rect);
                    if (!rect.Contains(sp)) continue;
                    GUI.Label(new Rect(sp.x + 6, sp.y - 8, 40, 16), i.ToString(), style);
                }
            }
        }

        // ================================================================
        // ボーン描画
        // ================================================================

        private static readonly Vector3[] _boneShape = {
            new Vector3(0.5f, 0f, -0.4f), new Vector3(2.5f, 0f, 0f),
            new Vector3(0.5f, 0.2f, 0f), new Vector3(0.5f, 0f, 0.4f),
            new Vector3(0.5f, -0.2f, 0f), new Vector3(0f, 0f, 0f),
        };
        private const float _boneScale = 0.04f;
        private static readonly int[,] _boneEdges = {
            {0,1},{0,2},{0,4},{0,5},{1,2},{1,3},{1,4},{2,3},{2,5},{3,4},{3,5},{4,5}
        };

        private void DrawBones(Rect rect, ModelContext model, Camera cam)
        {
            var list = model.MeshContextList;
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                if (ctx == null || ctx.Type != MeshType.Bone) continue;

                bool isSel = model.SelectedBoneIndices.Contains(i);
                Color col = isSel ? new Color(1f, 0.6f, 0.1f, 0.9f) : new Color(0.2f, 0.8f, 1f, 0.8f);

                if (!ExtractRotation(ctx.WorldMatrix, out Vector3 pos, out Quaternion boneRot)) continue;

                var sv = new Vector2[_boneShape.Length];
                for (int v = 0; v < _boneShape.Length; v++)
                    sv[v] = WorldToGUI(pos + boneRot * (_boneShape[v] * _boneScale), cam, rect);

                UnityEditor_Handles.BeginGUI();
                UnityEditor_Handles.color = col;
                int ec = _boneEdges.GetLength(0);
                for (int e = 0; e < ec; e++)
                    UnityEditor_Handles.DrawAAPolyLine(1.5f, sv[_boneEdges[e, 0]], sv[_boneEdges[e, 1]]);
                UnityEditor_Handles.EndGUI();

                // デバッグ軸
                float al = 0.05f;
                Vector2 so = WorldToGUI(pos, cam, rect);
                UnityEditor_Handles.BeginGUI();
                UnityEditor_Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
                UnityEditor_Handles.DrawAAPolyLine(2f, so, WorldToGUI(pos + boneRot * Vector3.right * al, cam, rect));
                UnityEditor_Handles.color = new Color(0.2f, 1f, 0.2f, 0.9f);
                UnityEditor_Handles.DrawAAPolyLine(2f, so, WorldToGUI(pos + boneRot * Vector3.up * al, cam, rect));
                UnityEditor_Handles.color = new Color(0.2f, 0.2f, 1f, 0.9f);
                UnityEditor_Handles.DrawAAPolyLine(2f, so, WorldToGUI(pos + boneRot * Vector3.forward * al, cam, rect));
                UnityEditor_Handles.EndGUI();
            }
        }

        // ================================================================
        // 対称平面
        // ================================================================

        private void DrawSymmetryPlane(Rect rect, ModelContext model, Camera cam)
        {
            var sym = model.SymmetrySettings;
            if (sym == null || !sym.IsEnabled || !sym.ShowSymmetryPlane) return;

            var meshCtx = model.FirstSelectedMeshContext;
            Bounds bounds = meshCtx?.MeshObject != null ? meshCtx.MeshObject.CalculateBounds() : new Bounds(Vector3.zero, Vector3.one);

            Color planeColor = sym.GetAxisColor();
            Vector3 planePoint = sym.GetPlanePoint();
            float planeSize = Mathf.Max(bounds.size.magnitude, 0.5f) * 0.6f;

            Vector3 a1, a2;
            switch (sym.Axis)
            {
                case SymmetryAxis.X: a1 = Vector3.up; a2 = Vector3.forward; break;
                case SymmetryAxis.Y: a1 = Vector3.right; a2 = Vector3.forward; break;
                default:             a1 = Vector3.right; a2 = Vector3.up; break;
            }

            var sc = new Vector2[4];
            sc[0] = WorldToGUI(planePoint + (-a1 - a2) * planeSize * 0.5f, cam, rect);
            sc[1] = WorldToGUI(planePoint + ( a1 - a2) * planeSize * 0.5f, cam, rect);
            sc[2] = WorldToGUI(planePoint + ( a1 + a2) * planeSize * 0.5f, cam, rect);
            sc[3] = WorldToGUI(planePoint + (-a1 + a2) * planeSize * 0.5f, cam, rect);

            DrawFilledPolygon(sc, new Color(planeColor.r, planeColor.g, planeColor.b, 0.15f));

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(planeColor.r, planeColor.g, planeColor.b, 0.6f);
            for (int i = 0; i < 4; i++) UnityEditor_Handles.DrawAAPolyLine(2f, sc[i], sc[(i + 1) % 4]);
            UnityEditor_Handles.EndGUI();

            Color lc = new Color(planeColor.r, planeColor.g, planeColor.b, 0.5f);
            DrawDottedLine(WorldToGUI(planePoint - a1 * planeSize * 0.4f, cam, rect),
                          WorldToGUI(planePoint + a1 * planeSize * 0.4f, cam, rect), lc);
            DrawDottedLine(WorldToGUI(planePoint - a2 * planeSize * 0.4f, cam, rect),
                          WorldToGUI(planePoint + a2 * planeSize * 0.4f, cam, rect), lc);

            Vector2 center = WorldToGUI(planePoint, cam, rect);
            if (rect.Contains(center))
            {
                UnityEditor_Handles.BeginGUI();
                EditorGUI.DrawRect(new Rect(center.x - 3, center.y - 3, 6, 6), planeColor);
                UnityEditor_Handles.EndGUI();
            }
        }

        // ================================================================
        // 注目点マーカー
        // ================================================================

        private void DrawFocusPointMarker(Rect rect, Camera cam)
        {
            Vector2 sp = WorldToGUI(Target, cam, rect);
            if (!rect.Contains(sp)) return;

            Color marker = new Color(1f, 0.8f, 0f, 0.9f);
            Color outline = new Color(0f, 0f, 0f, 0.6f);
            float sz = 12f;

            UnityEditor_Handles.BeginGUI();
            EditorGUI.DrawRect(new Rect(sp.x - sz - 1, sp.y - 2, sz * 2 + 2, 5), outline);
            EditorGUI.DrawRect(new Rect(sp.x - sz, sp.y - 1, sz * 2, 3), marker);
            EditorGUI.DrawRect(new Rect(sp.x - 2, sp.y - sz - 1, 5, sz * 2 + 2), outline);
            EditorGUI.DrawRect(new Rect(sp.x - 1, sp.y - sz, 3, sz * 2), marker);
            EditorGUI.DrawRect(new Rect(sp.x - 3, sp.y - 3, 7, 7), outline);
            EditorGUI.DrawRect(new Rect(sp.x - 2, sp.y - 2, 5, 5), marker);
            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // 原点マーカー
        // ================================================================

        private void DrawOriginMarker(Rect rect, Camera cam)
        {
            float axisLen = 0.2f;

            // メッシュフィルター（非スキンド）選択時はそのWorldMatrix原点をピボットとする。
            // スキンドメッシュはボーン群で変形されるため単一ピボットが存在せずVector3.zeroを使用。
            Vector3 pivot = Vector3.zero;
            var model = _currentModel;
            if (model != null && model.SelectedMeshIndices.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                int count = 0;
                foreach (int idx in model.SelectedMeshIndices)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx == null) continue;
                    if (ctx.MeshObject?.IsSkinned ?? false) continue; // スキンドはスキップ
                    sum += (Vector3)ctx.WorldMatrix.GetColumn(3);
                    count++;
                }
                if (count > 0) pivot = sum / count;
            }

            Vector2 o = WorldToGUI(pivot, cam, rect);
            if (!rect.Contains(o)) return;

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            UnityEditor_Handles.DrawAAPolyLine(2f, o, WorldToGUI(pivot + Vector3.right * axisLen, cam, rect));
            UnityEditor_Handles.color = new Color(0.2f, 1f, 0.2f, 0.8f);
            UnityEditor_Handles.DrawAAPolyLine(2f, o, WorldToGUI(pivot + Vector3.up * axisLen, cam, rect));
            UnityEditor_Handles.color = new Color(0.3f, 0.3f, 1f, 0.8f);
            UnityEditor_Handles.DrawAAPolyLine(2f, o, WorldToGUI(pivot + Vector3.forward * axisLen, cam, rect));
            EditorGUI.DrawRect(new Rect(o.x - 3, o.y - 3, 6, 6), new Color(1, 1, 1, 0.7f));
            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // GLヘルパー
        // ================================================================

        private void DrawFilledPolygon(Vector2[] points, Color color)
        {
            if (points == null || points.Length < 3 || Event.current.type != EventType.Repaint) return;
            GL.PushMatrix();
            GetPolygonMaterial().SetPass(0);
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            for (int i = 1; i < points.Length - 1; i++)
            {
                GL.Vertex3(points[0].x, points[0].y, 0);
                GL.Vertex3(points[i].x, points[i].y, 0);
                GL.Vertex3(points[i + 1].x, points[i + 1].y, 0);
            }
            for (int i = 1; i < points.Length - 1; i++)
            {
                GL.Vertex3(points[0].x, points[0].y, 0);
                GL.Vertex3(points[i + 1].x, points[i + 1].y, 0);
                GL.Vertex3(points[i].x, points[i].y, 0);
            }
            GL.End();
            GL.PopMatrix();
        }

        private Material GetPolygonMaterial()
        {
            if (_polygonMaterial == null)
            {
                Shader s = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("UI/Default");
                _polygonMaterial = new Material(s);
                _polygonMaterial.hideFlags = HideFlags.HideAndDontSave;
                _polygonMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _polygonMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _polygonMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _polygonMaterial.SetInt("_ZWrite", 0);
            }
            return _polygonMaterial;
        }

        private static void DrawDottedLine(Vector2 from, Vector2 to, Color color)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = color;
            Vector2 dir = to - from;
            float len = dir.magnitude;
            if (len < 1f) { UnityEditor_Handles.EndGUI(); return; }
            dir /= len;
            float pos = 0f;
            while (pos < len)
            {
                float end = Mathf.Min(pos + 4f, len);
                UnityEditor_Handles.DrawAAPolyLine(2f, from + dir * pos, from + dir * end);
                pos += 7f;
            }
            UnityEditor_Handles.EndGUI();
        }

        private static Vector2 WorldToGUI(Vector3 world, Camera cam, Rect rect)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0) return new Vector2(-1000, -1000);
            return new Vector2(sp.x, rect.height - sp.y);
        }

        private static bool ExtractRotation(Matrix4x4 m, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(m.m03, m.m13, m.m23);
            Vector3 c0 = new Vector3(m.m00, m.m10, m.m20);
            Vector3 c1 = new Vector3(m.m01, m.m11, m.m21);
            Vector3 c2 = new Vector3(m.m02, m.m12, m.m22);
            float sx = c0.magnitude, sy = c1.magnitude, sz = c2.magnitude;
            if (sx < 0.0001f || sy < 0.0001f || sz < 0.0001f) { rot = Quaternion.identity; return false; }
            var r = Matrix4x4.identity;
            r.SetColumn(0, c0 / sx); r.SetColumn(1, c1 / sy); r.SetColumn(2, c2 / sz);
            rot = r.rotation;
            return true;
        }

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial != null) return _defaultMaterial;
            Shader s = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Unlit/Color");
            if (s != null)
            {
                _defaultMaterial = new Material(s);
                _defaultMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f));
                _defaultMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f));
            }
            return _defaultMaterial;
        }

        private ViewportEvent MakeEvent(Rect rect, ModelContext model)
        {
            Quaternion rot = Quaternion.Euler(RotX, RotY, RotZ);
            Vector3 camPos = Target + rot * new Vector3(0, 0, -Distance);
            return new ViewportEvent
            {
                Rect = rect,
                CameraPos = camPos,
                CameraTarget = Target,
                CameraDistance = Distance,
                CameraFOV = _preview?.cameraFieldOfView ?? 30f,
                RotX = RotX, RotY = RotY, RotZ = RotZ,
                Camera = _preview?.camera,
                Model = model
            };
        }
    }
}
