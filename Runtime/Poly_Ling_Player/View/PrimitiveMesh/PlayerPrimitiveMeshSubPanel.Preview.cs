// PlayerPrimitiveMeshSubPanel.Preview.cs
// 図形生成サブパネル：パネル内プレビューと、メイン3Dウインドウへの仮表示
// （黄色ワイヤ・姿勢くさび）、および破棄。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // メイン3Dウインドウへのライブワイヤ描画（新サブツール用）
        // 既定 false。既存の図形生成インスタンスは何も変わらない。
        // ================================================================

        /// <summary>true のとき、生成予定形状の黄色ワイヤをメイン3Dウインドウへ描画する。</summary>
        public bool LiveWireInMainViewport { get; set; }

        /// <summary>メイン3Dウインドウ（4ビューポート）のカメラかを判定する。未設定ならライブワイヤは描画しない。</summary>
        public Func<Camera, bool> IsMainViewportCamera;

        /// <summary>
        /// 追加先モードが AddToExisting のときの、追加先 MeshContext のワールド行列。
        /// 未設定なら単位行列として扱う。
        /// </summary>
        public Func<Matrix4x4> GetAddTargetWorldMatrix;

        /// <summary>
        /// くさび仮表示の画面上の全長（ピクセル）。
        /// 軸ギズモの AxisGizmo.ScreenAxisLength（50px）に揃えてある。
        /// カメラから遠いほどワールド長を伸ばし、見かけの大きさを一定に保つ。
        /// </summary>
        private const float WedgeScreenLength = 50f;

        // ================================================================
        // プレビュー
        // ================================================================

        private PrimitivePreviewViewport _preview;

        /// <summary>
        /// Build に渡された右ペインセクション。表示中かどうかの判定にのみ使う。
        /// 複数インスタンスが同時に存在しても、表示中のものだけがプレビューを描画する。
        /// </summary>
        [UiControl(Ignore = true)]
        private VisualElement _sectionEl;

        /// <summary>メイン3Dウインドウへ描く黄色ワイヤ用マテリアル。初回描画時に遅延生成する。</summary>
        private Material _liveWireMat;

        /// <summary>くさび仮表示用マテリアル。色だけ変えた黄色ワイヤと同じ作り。</summary>
        private Material _wedgeMat;

        /// <summary>くさび仮表示の線メッシュ。MeshSceneRenderer と同じ形状で作る。</summary>
        private Mesh _wedgeMesh;

        private Mesh                     _wireMesh;
        private bool                     _dirty = true;
        private double                   _nextGenAllowed;   // 適応スロットル：次回再生成を許可する時刻(realtime秒)

        // プレビューマウス状態
        private bool    _mouseDragging;
        private int     _mouseBtn;
        private Vector2 _mouseDownPos;
        private Vector2 _mousePrevPos;

        // プレビュー高さ（下端ドラッグで手動リサイズ）
        private float _previewHeight = 200f;
        private bool  _resizeDragging;
        private float _resizeStartY;
        private float _resizeStartHeight;
        private const float PreviewMinHeight = 80f;
        private const float PreviewMaxHeight = 1200f;
        private const float DragThreshold = 3f;

        // ================================================================
        // プレビュー再生成 / カメラレンダー / Dispose
        // ================================================================

        /// <summary>
        /// パラメータ変更時 (_dirty=true) にメッシュとワイヤを再構築する。
        /// イベント駆動で呼んでもよいし、カメラレンダー前 (TickPreview) の
        /// 先頭で呼んでもよい。
        /// </summary>
        private void Regenerate()
        {
            if (_preview == null) return;
            if (!_dirty) return;
            // 適応スロットル：直前の生成コストに応じて再生成頻度を間引く。
            // スキップ時は _dirty を維持して後続フレームに回すため、最終値は必ず反映される。
            double t0 = Time.realtimeSinceStartupAsDouble;
            if (t0 < _nextGenAllowed) return;
            _dirty = false;
            try
            {
                var mo = Generate(true);
                _preview.SetMesh(mo);
                DestroyWire();
                if (mo != null) _wireMesh = BuildWire(mo);
            }
            catch { }
            double elapsed = Time.realtimeSinceStartupAsDouble - t0;
            _nextGenAllowed = Time.realtimeSinceStartupAsDouble + System.Math.Max(0.05, elapsed * 3.0);
        }

        /// <summary>
        /// プレビューカメラの描画を 1 回実行する。
        /// RenderPipelineManager.beginCameraRendering コールバックから
        /// (メインカメラ描画の直前に) 呼ばれる。毎フレームポーリングは
        /// 行わず、Unity のカメラレンダーループに寄り添う形で動かす。
        /// </summary>
        private void TickPreview()
        {
            if (_preview == null) return;
            Regenerate();
            _preview.Tick(_wireMesh);
            if (_previewEl != null && _preview.RT != null)
                _previewEl.style.backgroundImage = new StyleBackground(
                    Background.FromRenderTexture(_preview.RT));
        }

        /// <summary>
        /// URP の beginCameraRendering コールバック。
        /// プレビューカメラ自身 (_preview.Cam) および非 Game カメラは除外。
        /// メインカメラ描画の直前にプレビューを更新する。
        /// </summary>
        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            if (cam.cameraType != CameraType.Game) return;
            if (_preview != null && cam == _preview.Cam) return;
            // 非表示のパネルは RT を誰も見ていないので Render しない。
            // 再表示時は _dirty が保持されているため、次の描画で最新値に追いつく。
            if (!IsSectionVisible()) return;

            // 先に再生成して _wireMesh を最新化し、そのうえで提出する。
            // この順序により、SubmitLiveWire は Graphics.DrawMesh 提出のみを行えばよい。
            TickPreview();
            SubmitLiveWire(cam);

            // くさび仮表示は生成予定形状のワイヤとは独立に出す
            // （形状のプレビューが無くても姿勢は見せたいため）。
            SubmitPoseWedge(cam);
        }

        /// <summary>このパネルのセクションが右ペインに表示されているか。</summary>
        private bool IsSectionVisible()
        {
            if (_sectionEl == null) return true;   // 判定材料が無い場合は従来どおり描画する
            return _sectionEl.resolvedStyle.display != DisplayStyle.None;
        }

        /// <summary>
        /// 穴つなぎの種マーカーをビューポートへ出す状態か。
        /// このパネルが表示中で、かつ選んでいる図形が「ブリッジ」のときだけ true。
        /// ブリッジは専用の InteractionMode を持たないため、Viewer 側はこの値で判定する。
        /// </summary>
        public bool BridgeOverlayActive => IsSectionVisible() && _current == ShapeKind.Bridge;

        // ================================================================
        // メイン3Dウインドウへのライブワイヤ提出
        // ================================================================

        /// <summary>
        /// 生成予定形状の黄色ワイヤをメイン3Dウインドウのカメラへ提出する。
        /// ここでは Graphics.DrawMesh 提出のみを行い、メッシュ再構築は行わない
        /// （呼出し前の TickPreview で _wireMesh は最新化済み）。
        /// </summary>
        private void SubmitLiveWire(Camera cam)
        {
            if (!LiveWireInMainViewport) return;
            if (cam == null || _wireMesh == null) return;
            if (IsMainViewportCamera == null || !IsMainViewportCamera(cam)) return;

            EnsureLiveWireMaterial();
            if (_liveWireMat == null) return;

            var __m = LiveWireMatrix();
            Graphics.RenderMesh(
                Poly_Ling.Core.PLRenderMeshHelper.Make(
                    _liveWireMat, cam, Poly_Ling.Core.PLRenderMeshHelper.WorldBoundsOf(_wireMesh, __m)),
                _wireMesh, 0, __m);
        }

        /// <summary>
        /// 生成予定姿勢のくさびをメイン3Dウインドウのカメラへ提出する（仮表示）。
        ///
        /// 形状と大きさは MeshSceneRenderer の原点マーカーと同じものを使う。
        /// 姿勢は「生成後に描画オブジェクトの姿勢（BoneTransform）へ入る分」で、
        /// ベイク ON の回転は頂点へ焼かれて姿勢に残らないため PoseRotation を見る。
        /// </summary>
        private void SubmitPoseWedge(Camera cam)
        {
            if (!LiveWireInMainViewport) return;
            if (cam == null) return;
            if (IsMainViewportCamera == null || !IsMainViewportCamera(cam)) return;
            if (PlaceSettings == null || !PlaceSettings.ShowWedge) return;

            // 姿勢そのものを持たない図形（歪み複製・穴つなぎ）は対象外。
            if (!PoseApplicable) return;

            if (!Poly_Ling.Core.MeshSceneRenderer.ExtractBoneTransform(
                    PoseWedgeMatrix(), out var pos, out var rot))
                return;

            float scale = WedgeWorldScale(cam, pos);
            if (scale <= 0f) return;

            // 色はマテリアル側で持たせる。頂点色は白のまま
            // （Hidden/Internal-Colored は頂点色とマテリアル色の積を出すため）。
            //
            // 大きさがカメラごとに変わるので、毎回 UpdateBoneLineMesh で書き直す。
            // 4 面はそれぞれ別カメラで、同じ 1 個のメッシュを提出直前に
            // そのカメラ向きの寸法へ更新する。
            if (_wedgeMesh == null)
                _wedgeMesh = Poly_Ling.Core.MeshSceneRenderer.BuildBoneLineMesh(
                    pos, rot, Color.white, scale);
            else
                Poly_Ling.Core.MeshSceneRenderer.UpdateBoneLineMesh(
                    _wedgeMesh, pos, rot, Color.white, scale);

            EnsureWedgeMaterial();
            if (_wedgeMat == null) return;

            // 頂点はワールド座標で作ってあるので行列は単位。
            Graphics.RenderMesh(
                Poly_Ling.Core.PLRenderMeshHelper.Make(
                    _wedgeMat, cam,
                    Poly_Ling.Core.PLRenderMeshHelper.WorldBoundsOf(_wedgeMesh, Matrix4x4.identity)),
                _wedgeMesh, 0, Matrix4x4.identity);
        }

        /// <summary>
        /// くさび仮表示へ渡す倍率（BuildBoneLineMesh の scale）。
        ///
        /// 【見かけ一定にする理由】
        ///   軸ギズモ（AxisGizmo.ScreenAxisLength = 50px）も回転リング
        ///   （RotateRingGizmo は CameraDistance 比例）も画面上の大きさが一定で、
        ///   ワールド固定なのは MeshSceneRenderer の原点マーカーだけだった。
        ///   仮表示を後者に合わせると引いたときに見えなくなるため、
        ///   ギズモ側の規則へ揃える。
        ///
        /// 【正投影も同じ式で扱える理由】
        ///   OrbitCameraController が orthographicSize = Distance * tan(fov/2) を
        ///   保っているので、「画面の半分の高さがワールドで何単位か」を基準にすれば
        ///   透視・正投影のどちらでも同じ意味になる。
        ///
        /// 倍率は BoneShapeVertices の先端 y（= ObjectPoseWedgeShape.UnitTipY）で
        /// 全長を割った値。ObjectPoseWedgeShape.Build の k = size / UnitTipY と同じ換算。
        /// </summary>
        private static float WedgeWorldScale(Camera cam, Vector3 worldPos)
        {
            if (cam == null) return 0f;
            int px = cam.pixelHeight;
            if (px <= 0) return 0f;

            float halfH = cam.orthographic
                ? cam.orthographicSize
                : Vector3.Distance(cam.transform.position, worldPos)
                  * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (halfH <= 0f) return 0f;

            float worldPerPixel = (halfH * 2f) / px;
            float length        = WedgeScreenLength * worldPerPixel;
            return length / Poly_Ling.Tools.ObjectPose.ObjectPoseWedgeShape.UnitTipY;
        }

        /// <summary>
        /// くさび仮表示のワールド行列（位置＋姿勢へ残る回転）。
        /// 位置の扱いは LiveWireMatrix と同じ規則。
        /// </summary>
        private Matrix4x4 PoseWedgeMatrix()
        {
            var local = Matrix4x4.TRS(_worldPos, Quaternion.Euler(PoseRotation), Vector3.one);
            if (_addMode != PrimitiveAddMode.AddToExisting) return local;
            var parent = GetAddTargetWorldMatrix?.Invoke() ?? Matrix4x4.identity;
            return parent * local;
        }

        /// <summary>
        /// ライブワイヤの配置行列。回転・スケールは Generate() で頂点へ焼き込み済みのため、
        /// ここで扱うのは生成位置のみ。
        /// <para>
        /// NewObject / NewModel: 生成物は親を持たないルートとして WorldMatrix = Translate(_worldPos)
        /// になる（BuildPrimitiveMeshContext が BoneTransform.Position へ入れる）。
        /// </para>
        /// <para>
        /// AddToExisting: 追加先メッシュのローカル空間で頂点へ加算されるため、
        /// 追加先の WorldMatrix を左から掛ける。
        /// </para>
        /// </summary>
        private Matrix4x4 LiveWireMatrix()
        {
            // 穴つなぎ・点指定図形のプレビュー頂点はワールド空間で作ってあるので、行列は掛けない。
            if (_current == ShapeKind.Bridge || _current == ShapeKind.PointDefined) return Matrix4x4.identity;

            var local = Matrix4x4.Translate(_worldPos);
            if (_addMode != PrimitiveAddMode.AddToExisting) return local;
            var parent = GetAddTargetWorldMatrix?.Invoke() ?? Matrix4x4.identity;
            return parent * local;
        }

        private void EnsureLiveWireMaterial()
        {
            if (_liveWireMat != null) return;

            var sh = Shader.Find("Hidden/Internal-Colored")
                  ?? Shader.Find("Unlit/Color");
            if (sh == null) return;

            _liveWireMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _liveWireMat.SetColor("_Color", new Color(1f, 0.92f, 0.2f, 1f));
            // 常に手前に描く。モデルに埋まっても生成位置が見えるようにする。
            _liveWireMat.SetInt("_ZTest",  (int)CompareFunction.Always);
            _liveWireMat.SetInt("_ZWrite", 0);
        }

        /// <summary>
        /// くさび仮表示用マテリアル。作り方は EnsureLiveWireMaterial と同じで、
        /// 色だけ MeshSceneRenderer のメッシュ原点マーカーに合わせた緑にする。
        /// </summary>
        private void EnsureWedgeMaterial()
        {
            if (_wedgeMat != null) return;

            var sh = Shader.Find("Hidden/Internal-Colored")
                  ?? Shader.Find("Unlit/Color");
            if (sh == null) return;

            _wedgeMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _wedgeMat.SetColor("_Color", new Color(0.4f, 1f, 0.4f, 0.8f));
            _wedgeMat.SetInt("_ZTest",  (int)CompareFunction.Always);
            _wedgeMat.SetInt("_ZWrite", 0);
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _preview?.Dispose();
            _preview = null;
            DestroyWire();
            if (_liveWireMat != null)
            {
                UnityEngine.Object.Destroy(_liveWireMat);
                _liveWireMat = null;
            }
            if (_wedgeMat != null)
            {
                UnityEngine.Object.Destroy(_wedgeMat);
                _wedgeMat = null;
            }
            if (_wedgeMesh != null)
            {
                UnityEngine.Object.Destroy(_wedgeMesh);
                _wedgeMesh = null;
            }
        }

        // ================================================================
        // ワイヤーフレームMesh生成
        // ================================================================

        private static Mesh BuildWire(MeshObject mo)
        {
            var verts = new Vector3[mo.VertexCount];
            for (int i = 0; i < verts.Length; i++) verts[i] = mo.Vertices[i].Position;
            var set = new HashSet<(int,int)>();
            var idx = new List<int>();
            foreach (var f in mo.Faces)
            {
                int n = f.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int a = f.VertexIndices[i], b = f.VertexIndices[(i+1)%n];
                    var e = a < b ? (a,b) : (b,a);
                    if (set.Add(e)) { idx.Add(a); idx.Add(b); }
                }
            }
            if (idx.Count == 0) return null;
            var wire = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            wire.vertices = verts;
            wire.SetIndices(idx.ToArray(), MeshTopology.Lines, 0);
            wire.RecalculateBounds();
            return wire;
        }

        private void DestroyWire()
        {
            if (_wireMesh != null) { UnityEngine.Object.Destroy(_wireMesh); _wireMesh = null; }
        }
    }
}
