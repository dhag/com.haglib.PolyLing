// SkinWeightVolumeToolHandler.cs
// スキンW範囲塗りのビューポート側（範囲の重ね表示・半径／高さのハンドル）と、
// パネル向けの読み取り（対象・入力の検査結果）を受け持つ（ツールの窓口 "skinWeightVolume"）。
//
// 【入力経路】ビューポート入力は MoveToolHandler が受け、そのフック
//   (GizmoHitTestOverride / OnDragStartExtra / OnToolDragExtra / OnToolDragEndExtra)
//   から GizmoHitTest / BeginGizmoDrag / GizmoDrag / EndGizmoDrag を呼ぶ。
//   PrimitivePlaceToolHandler と同じ構成。頂点のクリック選択と、何も掴んでいない位置からの
//   矩形／投げ縄選択は MoveToolHandler がそのまま行う。
//
// 【座標】計算はバインド空間（SkinWeightVolumeOps）。表示は、バインドポーズ表示なら
//   そのまま、現在ポーズ表示なら親側を親ボーン、自関節より先を自ボーンの
//   SkinningMatrix（= WorldMatrix × BindPose）で写す。スキンド頂点の描画と同じ行列。
//
// データは変えない（書き込みは SkinWeightVolumePaintCommand）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    [PLTool("skinWeightVolume", Description = "スキンW範囲塗りの読み取り（対象・入力の検査）とビューポートの範囲表示")]
    public sealed class SkinWeightVolumeToolHandler
    {
        /// <summary>ハンドルの種類。</summary>
        private enum HandleKind { None, Radius, Height, ParentHeight }

        /// <summary>ハンドルを掴める距離（ピクセル）。</summary>
        private const float HandlePickPixels = 10f;

        /// <summary>円を描く分割数。</summary>
        private const int CircleSegments = 32;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ModelContext> GetModel;
        public Func<ToolContext>  GetToolContext;
        public Func<float>        GetPanelHeight;

        /// <summary>パネルの現在の入力。</summary>
        public Func<SkinWeightVolumeSpec> GetSpec;
        /// <summary>ハンドルで半径を変えたとき。</summary>
        public Action<float> SetRadius;
        /// <summary>ハンドルで高さを変えたとき。</summary>
        public Action<float> SetHeight;
        /// <summary>ハンドルで親側の長さを変えたとき（親側を別に指定していなければ先端側が変わる）。</summary>
        public Action<float> SetParentHeight;
        /// <summary>重ね表示を描き直す（Viewer の UpdateTopologyToolsOverlay）。</summary>
        public Action OnRefreshOverlay;

        // ================================================================
        // 状態（ツールの窓口）
        // ================================================================

        [PLToolState(Description = "対象オブジェクト（適用先と同じ「選択中の描画オブジェクト全件」）の名前")]
        public string[] TargetNames
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return Array.Empty<string>();
                var list = new List<string>();
                foreach (var mc in SkinWeightOperations.CollectTargetMeshContexts(model))
                    list.Add(string.IsNullOrEmpty(mc?.Name) ? "?" : mc.Name);
                return list.ToArray();
            }
        }

        [PLToolState(Description = "対象オブジェクトごとの選択頂点数（TargetNames と同じ並び）")]
        public int[] TargetSelectedVertexCounts
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return Array.Empty<int>();
                var list = new List<int>();
                foreach (var mc in SkinWeightOperations.CollectTargetMeshContexts(model))
                    list.Add(mc?.SelectedVertices?.Count ?? 0);
                return list.ToArray();
            }
        }

        [PLToolState(Description = "対象のうちスキンドでないオブジェクトの名前（適用できない）")]
        public string[] NonSkinnedTargetNames
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return Array.Empty<string>();
                return SkinWeightVolumeOps.FindNonSkinned(
                    SkinWeightOperations.CollectTargetMeshContexts(model)).ToArray();
            }
        }

        [PLToolState(Description = "現在の入力の問題（無ければ空）")]
        public string SpecError
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null) return "モデルがありません。";
                if (GetSpec == null) return "入力がありません。";
                return SkinWeightVolumeOps.TryBuildFrame(model, GetSpec(), out _, out string err) ? "" : err;
            }
        }

        [PLToolState(Description = "使う親ボーンの masterIndex（入力が不正なら -1）")]
        public int ResolvedParentBone
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null || GetSpec == null) return -1;
                return SkinWeightVolumeOps.TryBuildFrame(model, GetSpec(), out var f, out _) ? f.ParentBone : -1;
            }
        }

        [PLToolState(Description = "親関節から自関節までの長さ（ボーンの指定が不正なら 0。半径・高さは問わない）")]
        public float ParentLength
        {
            get
            {
                var model = GetModel?.Invoke();
                if (model == null || GetSpec == null) return 0f;
                // 半径・高さの初期値を決めるために使うので、それらが未設定でも長さを返す。
                var spec = GetSpec();
                spec.Radius       = 1f;
                spec.Height       = 1f;
                spec.ParentHeight = 1f;
                return SkinWeightVolumeOps.TryBuildFrame(model, spec, out var f, out _) ? f.BoneLength : 0f;
            }
        }

        // ================================================================
        // ハンドル
        // ================================================================

        private HandleKind _hover   = HandleKind.None;
        private HandleKind _pending = HandleKind.None;
        private Vector2    _pendingScreen;

        private HandleKind _drag = HandleKind.None;
        private Vector2    _dragStartImgui;
        private Vector3    _dragAnchor;   // 表示空間のハンドル位置
        private Vector3    _dragDir;      // 表示空間の伸びる向き（単位）
        private float      _dragStartValue;

        /// <summary>ホバー更新（RegisterActiveToolHandler 用）。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            var hit = HitHandle(screenPos, ctx);
            if (hit != _hover)
            {
                _hover = hit;
                OnRefreshOverlay?.Invoke();
            }
        }

        /// <summary>ハンドルのヒットテスト（MoveToolHandler.GizmoHitTestOverride 用）。</summary>
        public bool GizmoHitTest(Vector2 screenPos, ToolContext ctx)
        {
            _pending       = HitHandle(screenPos, ctx);
            _pendingScreen = screenPos;
            return _pending != HandleKind.None;
        }

        /// <summary>ドラッグ開始（OnDragStartExtra 用）。ハンドルを掴んでいれば true。</summary>
        public bool BeginGizmoDrag()
        {
            _drag = HandleKind.None;
            var kind = _pending;
            _pending = HandleKind.None;
            if (kind == HandleKind.None) return false;

            var ctx = GetToolContext?.Invoke();
            if (!TryGetDisplay(ctx, out var g)) return false;

            var spec = GetSpec();
            if (kind == HandleKind.Radius)
            {
                _dragAnchor     = g.RadiusHandle;
                _dragDir        = (g.RadiusHandle - g.JointDisplay).normalized;
                _dragStartValue = spec.Radius;
            }
            else if (kind == HandleKind.Height)
            {
                _dragAnchor     = g.TipDisplay;
                _dragDir        = (g.TipDisplay - g.JointDisplay).normalized;
                _dragStartValue = spec.Height;
            }
            else
            {
                _dragAnchor     = g.ParentDisplay;
                _dragDir        = (g.ParentDisplay - g.JointDisplay).normalized;
                _dragStartValue = g.Frame.ParentLength;
            }
            if (_dragDir.sqrMagnitude < 1e-12f) return false;

            _dragStartImgui = ToImgui(_pendingScreen);
            _drag = kind;
            return true;
        }

        /// <summary>ドラッグ中（OnToolDragExtra 用）。</summary>
        public void GizmoDrag(Vector2 screenPos)
        {
            if (_drag == HandleKind.None) return;
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            // ハンドルの向きを画面へ写し、マウスの移動量をその向きの長さへ直す。
            Vector2 a  = ctx.WorldToScreen(_dragAnchor + _dragDir) - ctx.WorldToScreen(_dragAnchor);
            float   aa = a.sqrMagnitude;
            if (aa < 1e-6f) return;

            Vector2 delta = ToImgui(screenPos) - _dragStartImgui;
            float   value = Mathf.Max(1e-4f, _dragStartValue + Vector2.Dot(delta, a) / aa);

            if      (_drag == HandleKind.Radius) SetRadius?.Invoke(value);
            else if (_drag == HandleKind.Height) SetHeight?.Invoke(value);
            else                                 SetParentHeight?.Invoke(value);
            OnRefreshOverlay?.Invoke();
        }

        /// <summary>ドラッグ終了（OnToolDragEndExtra 用）。</summary>
        public void EndGizmoDrag()
        {
            _drag    = HandleKind.None;
            _pending = HandleKind.None;
            OnRefreshOverlay?.Invoke();
        }

        private HandleKind HitHandle(Vector2 screenPos, ToolContext ctx)
        {
            if (!TryGetDisplay(ctx, out var g)) return HandleKind.None;

            Vector2 m = ToImgui(screenPos);
            float best = HandlePickPixels * HandlePickPixels;
            var kind = HandleKind.None;

            float dr = (ctx.WorldToScreen(g.RadiusHandle) - m).sqrMagnitude;
            if (dr <= best) { best = dr; kind = HandleKind.Radius; }

            if (g.Shape == SkinWeightVolumeShape.Cylinder)
            {
                float dh = (ctx.WorldToScreen(g.TipDisplay) - m).sqrMagnitude;
                if (dh <= best) { best = dh; kind = HandleKind.Height; }
            }

            float dp = (ctx.WorldToScreen(g.ParentDisplay) - m).sqrMagnitude;
            if (dp <= best) { kind = HandleKind.ParentHeight; }
            return kind;
        }

        // ================================================================
        // 重ね表示
        // ================================================================

        /// <summary>表示空間へ写した形状。</summary>
        private struct DisplayGeometry
        {
            public SkinWeightVolumeShape Shape;
            public SkinWeightVolumeFrame Frame;
            public Matrix4x4 ParentM;
            public Matrix4x4 SelfM;
            public Vector3 ParentDisplay, JointDisplay, TipDisplay, RadiusHandle;
            /// <summary>軸に垂直な単位ベクトル 2 本（バインド空間）。</summary>
            public Vector3 U, V;
        }

        private bool TryGetDisplay(ToolContext ctx, out DisplayGeometry g)
        {
            g = default;
            if (ctx == null || GetSpec == null) return false;
            var model = GetModel?.Invoke();
            if (model == null) return false;
            if (!SkinWeightVolumeOps.TryBuildFrame(model, GetSpec(), out var f, out _)) return false;

            var parent = model.GetMeshContext(f.ParentBone);
            var self   = model.GetMeshContext(f.SelfBone);
            if (parent == null || self == null) return false;

            bool bind = ctx.ShowBindPose;
            g.Shape   = f.Shape;
            g.Frame   = f;
            g.ParentM = bind ? Matrix4x4.identity : parent.SkinningMatrix;
            g.SelfM   = bind ? Matrix4x4.identity : self.SkinningMatrix;

            Vector3 helper = Mathf.Abs(f.Axis.y) < 0.9f ? Vector3.up : Vector3.right;
            g.U = Vector3.Cross(f.Axis, helper).normalized;
            g.V = Vector3.Cross(f.Axis, g.U).normalized;

            g.ParentDisplay = g.ParentM.MultiplyPoint3x4(f.Parent);
            g.JointDisplay  = g.SelfM.MultiplyPoint3x4(f.Joint);
            g.TipDisplay    = g.SelfM.MultiplyPoint3x4(f.Tip);
            g.RadiusHandle  = g.SelfM.MultiplyPoint3x4(f.Joint + g.U * f.Radius);
            return true;
        }

        /// <summary>
        /// 重ね表示の線と点（スクリーン、Y=0 上）を作る。入力が不正なら false。
        /// </summary>
        public bool TryBuildOverlay(
            ToolContext ctx,
            out List<(Vector2 a, Vector2 b, Color col)> lines,
            out List<(Vector2 p, Color col, float halfSize)> points)
        {
            lines  = new List<(Vector2, Vector2, Color)>();
            points = new List<(Vector2, Color, float)>();
            // 重ね表示を作り直す契機（入力・姿勢・表示モードの変化）で 3D ワイヤも作り直す。
            // 入力が不正で重ね表示が出ないときも、古いワイヤを残さないよう先に立てる。
            _wireDirty = true;
            if (!TryGetDisplay(ctx, out var g)) return false;

            float h = ctx.PreviewRect.height;
            Vector2 S(Vector3 world)
            {
                var sp = ctx.WorldToScreen(world);
                return new Vector2(sp.x, h - sp.y);
            }

            var f       = g.Frame;
            var shell   = new Color(0.4f, 0.85f, 1f, 0.9f);
            var axisCol = new Color(1f, 1f, 1f, 0.8f);
            var c0      = new Color(0.2f, 0.4f, 1f);   // 0%
            var c50     = new Color(0.2f, 1f, 0.3f);   // 50%
            var c100    = new Color(1f, 0.25f, 0.2f);  // 100%
            var hot     = new Color(1f, 0.95f, 0.2f);

            // 軸（親関節 → 自関節 → 先端）
            lines.Add((S(g.ParentDisplay), S(g.JointDisplay), axisCol));
            lines.Add((S(g.JointDisplay),  S(g.TipDisplay),   axisCol));

            // 範囲の形（断面の円・母線・球の大円）は 3D のワイヤ（SubmitWire）で描く。
            // 重ね表示に残すのは軸・基準点・ハンドルだけ。

            // 基準点
            points.Add((S(g.ParentDisplay), c0,   4f));
            points.Add((S(g.JointDisplay),  c50,  4f));
            points.Add((S(g.TipDisplay),    c100, 4f));

            // ハンドル
            bool radiusHot = _drag == HandleKind.Radius || (_drag == HandleKind.None && _hover == HandleKind.Radius);
            points.Add((S(g.RadiusHandle), radiusHot ? hot : shell, radiusHot ? 7f : 5f));
            if (f.Shape == SkinWeightVolumeShape.Cylinder)
            {
                bool heightHot = _drag == HandleKind.Height || (_drag == HandleKind.None && _hover == HandleKind.Height);
                points.Add((S(g.TipDisplay), heightHot ? hot : c100, heightHot ? 7f : 5f));
            }
            bool parentHot = _drag == HandleKind.ParentHeight || (_drag == HandleKind.None && _hover == HandleKind.ParentHeight);
            points.Add((S(g.ParentDisplay), parentHot ? hot : c0, parentHot ? 7f : 5f));
            return true;
        }

        // ================================================================
        // 3D ワイヤ（図形作成の黄色ワイヤと同じ描き方。PlayerPrimitiveMeshSubPanel.Preview.cs）
        // ================================================================
        //
        // 線メッシュを作り、各ビューポートのカメラの描画直前に Graphics.RenderMesh で渡す。
        // 頂点は表示空間（重ね表示と同じ規則：親側は親ボーン、自関節より先は自ボーンの
        // SkinningMatrix で写す）で作るので、行列は単位。
        // 作り直すのは _wireDirty のときだけ（重ね表示を作り直す契機で立つ）。

        /// <summary>ワイヤを出す状態か（スキンW範囲塗りの操作モード中）。</summary>
        public Func<bool> IsWireActive;
        /// <summary>メインのビューポートのカメラか。</summary>
        public Func<Camera, bool> IsViewportCamera;
        /// <summary>範囲塗りの範囲を出すか。</summary>
        public Func<bool> ShowVolumeWire;
        /// <summary>区間塗りの範囲を出すか。</summary>
        public Func<bool> ShowSegmentWire;

        /// <summary>円周の分割数。</summary>
        private const int WireRadialSegments = 16;
        /// <summary>長さ方向の輪の間隔（目安）。</summary>
        private const float WireRingSpacing = 0.03f;
        /// <summary>球の緯線・経線の本数。</summary>
        private const int WireSphereRings = 7;
        private const int WireSphereMeridians = 8;

        private Mesh     _volumeWire;
        private Mesh     _segmentWire;
        private Material _volumeWireMat;
        private Material _segmentWireMat;
        private bool     _wireDirty = true;
        private bool     _wireHooked;

        /// <summary>ワイヤの描画を始める（Viewer が配線後に 1 回呼ぶ）。</summary>
        public void EnableWire()
        {
            if (_wireHooked) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            _wireHooked = true;
        }

        /// <summary>ワイヤを作り直す（入力が変わったとき）。</summary>
        public void InvalidateWire() => _wireDirty = true;

        public void Dispose()
        {
            if (_wireHooked)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
                _wireHooked = false;
            }
            DestroyMesh(ref _volumeWire);
            DestroyMesh(ref _segmentWire);
            if (_volumeWireMat  != null) { UnityEngine.Object.Destroy(_volumeWireMat);  _volumeWireMat  = null; }
            if (_segmentWireMat != null) { UnityEngine.Object.Destroy(_segmentWireMat); _segmentWireMat = null; }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext rc, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game) return;
            if (IsWireActive == null || !IsWireActive()) return;
            if (IsViewportCamera == null || !IsViewportCamera(cam)) return;

            if (_wireDirty) RebuildWire();

            bool showVol = ShowVolumeWire?.Invoke() ?? true;
            bool showSeg = ShowSegmentWire?.Invoke() ?? false;

            if (showVol && _volumeWire != null)
                Submit(cam, _volumeWire, ref _volumeWireMat, new Color(1f, 0.92f, 0.2f, 1f));
            if (showSeg && _segmentWire != null)
                Submit(cam, _segmentWire, ref _segmentWireMat, new Color(0.3f, 0.9f, 1f, 1f));
        }

        private static void Submit(Camera cam, Mesh mesh, ref Material mat, Color col)
        {
            if (mat == null)
            {
                var sh = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Unlit/Color");
                if (sh == null) return;
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetColor("_Color", col);
                // 常に手前に描く（モデルに埋まっていても範囲が見えるように）。
                mat.SetInt("_ZTest",  (int)CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
            }
            Graphics.RenderMesh(
                Poly_Ling.Core.PLRenderMeshHelper.Make(
                    mat, cam, Poly_Ling.Core.PLRenderMeshHelper.WorldBoundsOf(mesh, Matrix4x4.identity)),
                mesh, 0, Matrix4x4.identity);
        }

        private void RebuildWire()
        {
            _wireDirty = false;
            DestroyMesh(ref _volumeWire);
            DestroyMesh(ref _segmentWire);

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            // 範囲塗り
            if (TryGetDisplay(ctx, out var g))
            {
                var verts = new List<Vector3>();
                var idx   = new List<int>();
                var f     = g.Frame;
                if (f.Shape == SkinWeightVolumeShape.Cylinder)
                {
                    // 親側（親ボーンで写す）と先側（自ボーンで写す）を自関節で分けて作る。
                    AddWireCylinder(verts, idx, g.ParentM, f.Joint, f.Axis, g.U, g.V, f.Radius, -f.ParentLength, 0f);
                    AddWireCylinder(verts, idx, g.SelfM,   f.Joint, f.Axis, g.U, g.V, f.Radius, 0f, f.TipLength);
                }
                else
                {
                    AddWireSphere(verts, idx, g.SelfM, f.Joint, f.Axis, g.U, g.V, f.Radius);
                }
                _volumeWire = MakeLineMesh(verts, idx);
            }

            // 区間塗り（親関節〜自関節。全体が親ボーンの範囲なので親ボーンで写す）
            var model = GetModel?.Invoke();
            if (model != null && GetSpec != null)
            {
                var spec = GetSpec();
                if (SkinWeightVolumeOps.TryBuildSegment(model, spec.SelfBone, spec.ParentBone, spec.Radius,
                        out var sf, out _))
                {
                    var parent = model.GetMeshContext(sf.ParentBone);
                    Matrix4x4 pm = (ctx.ShowBindPose || parent == null) ? Matrix4x4.identity : parent.SkinningMatrix;
                    Vector3 helper = Mathf.Abs(sf.Axis.y) < 0.9f ? Vector3.up : Vector3.right;
                    Vector3 u = Vector3.Cross(sf.Axis, helper).normalized;
                    Vector3 v = Vector3.Cross(sf.Axis, u).normalized;

                    var verts = new List<Vector3>();
                    var idx   = new List<int>();
                    AddWireCylinder(verts, idx, pm, sf.Parent, sf.Axis, u, v, sf.Radius, 0f, sf.BoneLength);
                    _segmentWire = MakeLineMesh(verts, idx);
                }
            }
        }

        /// <summary>
        /// 円筒のワイヤを足す。軸上の位置 sStart〜sEnd（center からの距離）に輪を
        /// WireRingSpacing 間隔で置き、輪どうしを母線でつなぐ。m で表示空間へ写す。
        /// </summary>
        private static void AddWireCylinder(
            List<Vector3> verts, List<int> idx, Matrix4x4 m,
            Vector3 center, Vector3 axis, Vector3 u, Vector3 v, float radius,
            float sStart, float sEnd)
        {
            float len = sEnd - sStart;
            if (len <= 1e-6f || radius <= 1e-6f) return;

            int rings = Mathf.Max(2, Mathf.CeilToInt(len / WireRingSpacing) + 1);
            int n     = WireRadialSegments;
            int baseIx = verts.Count;

            for (int r = 0; r < rings; r++)
            {
                float s = Mathf.Lerp(sStart, sEnd, (float)r / (rings - 1));
                Vector3 c = center + axis * s;
                for (int i = 0; i < n; i++)
                {
                    float t = i * Mathf.PI * 2f / n;
                    verts.Add(m.MultiplyPoint3x4(c + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                int ro = baseIx + r * n;
                for (int i = 0; i < n; i++)
                {
                    // 輪
                    idx.Add(ro + i); idx.Add(ro + (i + 1) % n);
                    // 母線
                    if (r + 1 < rings) { idx.Add(ro + i); idx.Add(ro + n + i); }
                }
            }
        }

        /// <summary>球のワイヤを足す（軸に垂直な緯線と、軸を通る経線）。</summary>
        private static void AddWireSphere(
            List<Vector3> verts, List<int> idx, Matrix4x4 m,
            Vector3 center, Vector3 axis, Vector3 u, Vector3 v, float radius)
        {
            if (radius <= 1e-6f) return;
            int n = WireRadialSegments * 2;

            // 緯線
            for (int r = 1; r <= WireSphereRings; r++)
            {
                float phi = Mathf.PI * r / (WireSphereRings + 1);   // 0..π（両極を除く）
                float h   = Mathf.Cos(phi) * radius;
                float rr  = Mathf.Sin(phi) * radius;
                int b = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    float t = i * Mathf.PI * 2f / n;
                    verts.Add(m.MultiplyPoint3x4(center + axis * h + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * rr));
                }
                for (int i = 0; i < n; i++) { idx.Add(b + i); idx.Add(b + (i + 1) % n); }
            }

            // 経線（極から極までの半円）
            for (int k = 0; k < WireSphereMeridians; k++)
            {
                float   t   = k * Mathf.PI * 2f / WireSphereMeridians;
                Vector3 dir = u * Mathf.Cos(t) + v * Mathf.Sin(t);
                int b = verts.Count;
                int segs = WireRadialSegments;
                for (int i = 0; i <= segs; i++)
                {
                    float phi = Mathf.PI * i / segs;
                    verts.Add(m.MultiplyPoint3x4(center + (axis * Mathf.Cos(phi) + dir * Mathf.Sin(phi)) * radius));
                }
                for (int i = 0; i < segs; i++) { idx.Add(b + i); idx.Add(b + i + 1); }
            }
        }

        private static Mesh MakeLineMesh(List<Vector3> verts, List<int> idx)
        {
            if (verts.Count == 0 || idx.Count == 0) return null;
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetIndices(idx, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void DestroyMesh(ref Mesh mesh)
        {
            if (mesh != null) { UnityEngine.Object.Destroy(mesh); mesh = null; }
        }

        private static void AddCircle(
            List<(Vector2, Vector2, Color)> lines, Func<Vector3, Vector2> toScreen,
            Matrix4x4 m, Vector3 center, Vector3 u, Vector3 v, float radius, Color col)
        {
            Vector2 prev = default;
            for (int i = 0; i <= CircleSegments; i++)
            {
                float   t  = i * Mathf.PI * 2f / CircleSegments;
                Vector3 pt = center + (u * Mathf.Cos(t) + v * Mathf.Sin(t)) * radius;
                Vector2 sp = toScreen(m.MultiplyPoint3x4(pt));
                if (i > 0) lines.Add((prev, sp, col));
                prev = sp;
            }
        }

        /// <summary>スクリーン系（Y 下）→ ctx 系（Y 上）。</summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
