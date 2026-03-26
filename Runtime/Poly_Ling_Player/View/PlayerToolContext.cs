// PlayerToolContext.cs
// AxisGizmo が必要とする ToolContext プロパティの Player 向け軽量実装。
// UnityEngine.Camera から計算して AxisGizmo に渡す。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// AxisGizmo が使う ToolContext プロパティのみを実装した Player 向けコンテキスト。
    /// ToolContext を継承せずスタンドアローンで実装する。
    /// </summary>
    public class PlayerToolContext
    {
        // AxisGizmo が参照するプロパティ
        public Vector3    CameraPosition  { get; set; }
        public Vector3    CameraTarget    { get; set; }
        public float      CameraDistance  { get; set; }
        public Rect       PreviewRect     { get; set; }
        public Matrix4x4  DisplayMatrix   => Matrix4x4.identity; // Player は常に identity

        // ================================================================
        // Camera から毎フレーム更新する
        // ================================================================

        /// <summary>
        /// PlayerViewport.Cam と OrbitCameraController からパラメータを更新する。
        /// </summary>
        public void UpdateFromViewport(PlayerViewport vp)
        {
            if (vp?.Cam == null) return;
            var cam = vp.Cam;
            CameraPosition = cam.transform.position;
            CameraTarget   = vp.Orbit?.Target ?? vp.Ortho?.Target ?? Vector3.zero;
            CameraDistance = Vector3.Distance(CameraPosition, CameraTarget);
            PreviewRect    = new Rect(0, 0, cam.pixelWidth, cam.pixelHeight);
        }

        // ================================================================
        // AxisGizmo が必要とする関数デリゲートの代替メソッド
        // ================================================================

        /// <summary>
        /// ワールド座標 → プレビュースクリーン座標（Y=0 が上、IMGUI 系）。
        /// AxisGizmo.Draw / FindAxisAtScreenPos / ComputeAxisDelta で使われる。
        /// </summary>
        public Vector2 WorldToScreenPos(Vector3 worldPos, Rect previewRect,
                                        Vector3 camPos, Vector3 lookAt)
        {
            if (_cam == null) return Vector2.zero;
            // Camera.WorldToScreenPoint は Y=0 が下
            Vector3 sp = _cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0) return new Vector2(-10000, -10000);
            // IMGUI 系（Y=0 が上）に変換
            return new Vector2(sp.x, previewRect.height - sp.y);
        }

        /// <summary>
        /// スクリーンデルタ → ワールドデルタ。
        /// CalcWorldDelta 相当。
        /// </summary>
        public Vector3 ScreenDeltaToWorldDelta(Vector2 screenDelta,
                                               Vector3 camPos, Vector3 target,
                                               float camDist, Rect previewRect)
        {
            if (_cam == null) return Vector3.zero;
            float scale = camDist / previewRect.height
                        * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f;
            return  _cam.transform.right * screenDelta.x * scale
                  + _cam.transform.up    * screenDelta.y * scale;
        }

        // ================================================================
        // ToolContext ラッパー（AxisGizmo に渡す）
        // ================================================================

        private Camera _cam;

        /// <summary>ToolContext 互換ラッパーを生成して返す。</summary>
        public ToolContext ToToolContext(Camera cam)
        {
            _cam = cam;
            var ctx = new ToolContext();
            ctx.CameraPosition  = CameraPosition;
            ctx.CameraTarget    = CameraTarget;
            ctx.CameraDistance  = CameraDistance;
            ctx.PreviewRect     = PreviewRect;
            ctx.DisplayMatrix   = DisplayMatrix;
            ctx.WorldToScreenPos = (wp, rect, cp, lt) =>
                WorldToScreenPos(wp, rect, cp, lt);
            ctx.ScreenDeltaToWorldDelta = (sd, cp, ct, cd, rect) =>
                ScreenDeltaToWorldDelta(sd, cp, ct, cd, rect);
            return ctx;
        }
    }
}
