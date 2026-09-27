// PolyLingPlayerViewerCore.CameraCommands.cs
// Player ビューアのコア：カメラ系コマンド（fitCameraToModel / fitCameraToSelection /
// resetCamera / setCamera / queryCamera）の受け口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【範囲は GPU の表示位置から取る】
//   ローカル→ワールドの表示位置は GPU が計算した値（TryGetMeshWorldPositions）を読む。
//   CPU で変換し直さない（スキニング・ビルボード等の規則を二重に持たないため）。
//
// 【合わせ方】
//   注視点 = 範囲（バウンディングボックス）の中心、大きさ = 外接球の半径 × FitMargin。
//   メイン画面（透視）… 縦と横の画角の狭い方で球が収まる距離。
//   メイン画面（オルソ）… orthographicSize = 距離 × tan(画角/2) が球を覆う距離。
//   3 面図 … 3 台で共有する「1 画素あたりのワールド高さ」を、最も狭いビューでも収まる値にする。
//   角度は変えない。
//
// 【反映】
//   値を変えたら EnterCameraChanged で描き直す（3 面図は連動するので 1 台ぶんでよい）。
//   カメラ調整パネルの表示も更新する。形状ではないので Undo は残さない。

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>合わせるときの余白（外接球の半径に掛ける）。</summary>
        private const float FitMargin = 1.1f;

        // 起動時の値（OrbitCameraController / OrthoViewSharedState / OrthoViewController の初期値と同じ）。
        private const float DefaultMainRotX     = 20f;
        private const float DefaultMainRotY     = 180f;
        private const float DefaultMainRotZ     = 0f;
        private const float DefaultMainDistance = 3f;
        private const float DefaultFov          = 60f;
        private const float DefaultTriWorldHeightPerPixel = 0.01f;

        /// <summary>カメラ系コマンドの受け口。失敗理由を返す（成功なら null）。</summary>
        private string ExecuteCameraCommand(PanelCommand cmd, ModelContext model, CommandDataBuilder data)
        {
            if (_viewportManager == null) return "ビューがありません";
            switch (cmd)
            {
                case FitCameraToModelCommand c:
                    return FitCamera(model, CollectVisibleDrawables(model), c.Views, data);

                case FitCameraToSelectionCommand c:
                {
                    var sel = new List<int>(model.SelectedDrawableMeshIndices);
                    if (sel.Count == 0) return "描画オブジェクトが選択されていません";
                    return FitCamera(model, sel, c.Views, data);
                }

                case ResetCameraCommand c:
                    ResetCamera(c.Views);
                    return null;

                case SetCameraCommand c:
                    return SetCamera(c);

                case QueryCameraCommand _:
                    return QueryCamera(data);
            }
            return "カメラのコマンドではありません";
        }

        private static bool IncludesMain(CameraViews v) => v == CameraViews.All || v == CameraViews.Main;
        private static bool IncludesTri (CameraViews v) => v == CameraViews.All || v == CameraViews.Tri;

        // ================================================================
        // 合わせる
        // ================================================================

        /// <summary>表示中の描画オブジェクトの番号。</summary>
        private static List<int> CollectVisibleDrawables(ModelContext model)
        {
            var list = new List<int>();
            if (model == null) return list;
            foreach (int i in model.TypedIndices.GetMasterIndices(MeshCategory.Drawable))
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.IsVisible) list.Add(i);
            }
            return list;
        }

        /// <summary>指定の描画オブジェクトの表示位置（GPU）の範囲に合わせる。</summary>
        private string FitCamera(ModelContext model, List<int> indices, CameraViews views, CommandDataBuilder data)
        {
            bool has = false;
            Bounds b = default;
            int objects = 0;
            foreach (int i in indices)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (!_viewportManager.TryGetMeshWorldPositions(model, mc, out var world) || world == null) continue;
                if (world.Length == 0) continue;
                foreach (var p in world)
                {
                    if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                    else b.Encapsulate(p);
                }
                objects++;
            }
            if (!has) return "範囲を求められる描画オブジェクトがありません（非表示のもの・頂点の無いものは表示位置が無いので入れられません）";

            float radius = Mathf.Max(b.extents.magnitude, 1e-4f) * FitMargin;

            if (IncludesMain(views)) FitMain(b.center, radius);
            if (IncludesTri(views))  FitTri(b.center, radius);
            CommitCamera(IncludesMain(views), IncludesTri(views));

            data.Text("target", V3Text(b.center))
                .Num("radius", b.extents.magnitude)
                .Int("objects", objects);
            return null;
        }

        private void FitMain(Vector3 center, float radius)
        {
            var vp = _viewportManager.PerspectiveViewport;
            var orbit = vp?.Orbit;
            if (orbit == null) return;

            float aspect = Aspect(vp.Cam);
            float halfV  = Mathf.Clamp(orbit.Fov, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
            float halfH  = Mathf.Atan(Mathf.Tan(halfV) * aspect);
            float half   = Mathf.Min(halfV, halfH);

            orbit.Target = center;
            if (orbit.Orthographic)
            {
                // orthographicSize = Distance × tan(halfV)。縦 = size、横 = size × aspect が半径を覆う。
                orbit.Distance = radius / (Mathf.Tan(halfV) * Mathf.Min(1f, aspect));
            }
            else
            {
                orbit.Distance = radius / Mathf.Sin(half);
            }
        }

        private void FitTri(Vector3 center, float radius)
        {
            var ortho = _viewportManager.FrontViewport?.Ortho;
            if (ortho == null) return;

            // 共有ズーム：各ビューの orthographicSize = WHPP × 画素高 ÷ 2。
            // 縦横とも半径を覆うには WHPP ≥ 2r ÷ min(画素幅, 画素高)。3 台の最大を取る。
            float whpp = 0f;
            foreach (var vp in new[] { _viewportManager.TopViewport, _viewportManager.FrontViewport, _viewportManager.SideViewport })
            {
                var cam = vp?.Cam;
                if (cam == null) continue;
                float m = Mathf.Min(cam.pixelWidth, cam.pixelHeight);
                if (m < 2f) continue;   // まだ大きさが決まっていない
                whpp = Mathf.Max(whpp, 2f * radius / m);
            }

            ortho.Target = center;
            if (whpp > 0f) ortho.WorldHeightPerPixel = whpp;
        }

        private static float Aspect(Camera cam)
        {
            if (cam == null || cam.pixelHeight <= 0) return 1f;
            return Mathf.Max(1e-3f, (float)cam.pixelWidth / cam.pixelHeight);
        }

        // ================================================================
        // 戻す・設定する
        // ================================================================

        private void ResetCamera(CameraViews views)
        {
            if (IncludesMain(views))
            {
                var orbit = _viewportManager.PerspectiveViewport?.Orbit;
                if (orbit != null)
                {
                    orbit.Target   = Vector3.zero;
                    orbit.RotX     = DefaultMainRotX;
                    orbit.RotY     = DefaultMainRotY;
                    orbit.RotZ     = DefaultMainRotZ;
                    orbit.Distance = DefaultMainDistance;
                    orbit.Fov      = DefaultFov;
                }
                SetMainCameraOrthographic(false);
            }

            if (IncludesTri(views))
            {
                var ortho = _viewportManager.FrontViewport?.Ortho;
                if (ortho != null)
                {
                    ortho.Target              = Vector3.zero;
                    ortho.WorldHeightPerPixel = DefaultTriWorldHeightPerPixel;
                    ortho.RigRotation         = Quaternion.identity;
                    ortho.Perspective         = false;
                    ortho.Fov                 = DefaultFov;
                }
                // 反転はラベル・下絵の差し替えと一緒に行う経路で戻す。
                _setTopFlip  ?.Invoke(false);
                _setFrontFlip?.Invoke(false);
                _setSideFlip ?.Invoke(false);
            }

            CommitCamera(IncludesMain(views), IncludesTri(views));
        }

        private string SetCamera(SetCameraCommand c)
        {
            if (IncludesMain(c.Views))
            {
                var orbit = _viewportManager.PerspectiveViewport?.Orbit;
                if (orbit == null) return "メイン画面のカメラがありません";
                orbit.Target   = c.Target;
                orbit.RotX     = c.RotationX;
                orbit.RotY     = c.RotationY;
                orbit.RotZ     = c.RotationZ;
                orbit.Distance = Mathf.Max(1e-4f, c.Distance);
                orbit.Fov      = Mathf.Clamp(c.Fov, 1f, 179f);
                SetMainCameraOrthographic(c.Orthographic);
            }

            if (IncludesTri(c.Views))
            {
                var front = _viewportManager.FrontViewport;
                var ortho = front?.Ortho;
                if (ortho == null) return "3 面図のカメラがありません";
                ortho.Target      = c.Target;
                ortho.RigRotation = Quaternion.Euler(c.TriRotation);
                float ph = front.Cam != null ? front.Cam.pixelHeight : 0f;
                if (ph >= 2f) ortho.WorldHeightPerPixel = 2f * Mathf.Max(1e-4f, c.TriHalfHeight) / ph;
            }

            CommitCamera(IncludesMain(c.Views), IncludesTri(c.Views));
            return null;
        }

        // ================================================================
        // 照会
        // ================================================================

        private string QueryCamera(CommandDataBuilder data)
        {
            var orbit = _viewportManager.PerspectiveViewport?.Orbit;
            if (orbit != null)
            {
                data.Text("mainTarget", V3Text(orbit.Target))
                    .Num("rotationX", orbit.RotX)
                    .Num("rotationY", orbit.RotY)
                    .Num("rotationZ", orbit.RotZ)
                    .Num("distance", orbit.Distance)
                    .Num("fov", orbit.Fov)
                    .Flag("orthographic", orbit.Orthographic);
            }

            var front = _viewportManager.FrontViewport;
            var ortho = front?.Ortho;
            if (ortho != null)
            {
                float ph = front.Cam != null ? front.Cam.pixelHeight : 0f;
                data.Text("triTarget", V3Text(ortho.Target))
                    .Text("triRotation", V3Text(ortho.RigRotation.eulerAngles))
                    .Num("triHalfHeight", ortho.WorldHeightPerPixel * ph * 0.5f)
                    .Flag("triPerspective", ortho.Perspective);
            }
            return null;
        }

        // ================================================================
        // 反映
        // ================================================================

        /// <summary>変えたカメラを描き直し、カメラ調整パネルの表示を合わせる。</summary>
        private void CommitCamera(bool main, bool tri)
        {
            if (main) _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
            // 3 面図は 3 台連動（ApplyAndDirtyLinkedOrtho）なので 1 台ぶんでよい。
            if (tri)  _viewportManager.EnterCameraChanged(_viewportManager.FrontViewport, CameraChangePhase.Committed);
            _cameraSubPanel?.Refresh();
        }

        private static string V3Text(Vector3 v)
            => string.Format(CultureInfo.InvariantCulture, "{0:G6},{1:G6},{2:G6}", v.x, v.y, v.z);
    }
}
