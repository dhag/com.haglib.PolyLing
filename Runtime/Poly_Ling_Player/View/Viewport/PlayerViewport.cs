// PlayerViewport.cs
// 1ビューポートの単位。Camera + RenderTexture + コントローラーを保持する。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    public enum ViewportMode { Perspective, Top, Front, Side }

    /// <summary>
    /// 1ビューポート分の Camera + RenderTexture + コントローラー。
    /// <see cref="PlayerViewportPanel"/> から IMouseEventSource 経由でイベントを受ける。
    /// 毎フレーム <see cref="ApplyCameraTransform"/> を呼ぶこと。
    /// </summary>
    public class PlayerViewport
    {
        // ================================================================
        // 公開プロパティ
        // ================================================================

        public Camera         Cam      { get; private set; }
        public RenderTexture  RT       { get; private set; }
        public ViewportMode   Mode     { get; private set; }
        public bool           IsReady  => Cam != null && RT != null && RT.IsCreated();

        // コントローラー（Viewer が ResetToMesh などを呼ぶために公開）
        public OrbitCameraController Orbit { get; private set; }
        public OrthoViewController   Ortho { get; private set; }

        // ================================================================
        // 内部
        // ================================================================

        private IMouseEventSource _source;
        private GameObject        _camGo;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Initialize(ViewportMode mode, Transform parent)
        {
            Mode = mode;

            // Camera GameObject 生成
            _camGo = new GameObject($"VP_{mode}") { hideFlags = HideFlags.HideAndDontSave };
            if (parent != null) _camGo.transform.SetParent(parent, false);

            Cam = _camGo.AddComponent<Camera>();
            Cam.enabled        = true;  // targetTexture=RT のためスクリーンには映らない
            Cam.clearFlags     = CameraClearFlags.SolidColor;
            Cam.backgroundColor= new Color(0.18f, 0.18f, 0.18f, 1f);
            Cam.cullingMask    = -1;
            Cam.nearClipPlane  = 0.01f;
            Cam.farClipPlane   = 1000f;

            if (mode == ViewportMode.Perspective)
            {
                Cam.orthographic = false;
                Cam.fieldOfView  = 60f;
                Orbit = new OrbitCameraController();
            }
            else
            {
                Cam.orthographic = true;
                var dir = mode == ViewportMode.Top   ? OrthoViewDirection.Top
                        : mode == ViewportMode.Side  ? OrthoViewDirection.Side
                        :                              OrthoViewDirection.Front;
                Ortho = new OrthoViewController(dir);
            }

            // 初期 RT（1×1、後で Resize される）
            CreateRT(1, 1);
        }

        public void Dispose()
        {
            if (_source != null) DisconnectSource(_source);

            ReleaseRT();

            if (_camGo != null)
            {
                Object.Destroy(_camGo);
                _camGo = null;
                Cam    = null;
            }
        }

        // ================================================================
        // IMouseEventSource 接続
        // ================================================================

        public void ConnectSource(IMouseEventSource source)
        {
            if (_source != null) DisconnectSource(_source);
            _source = source;

            if (Orbit != null) Orbit.Connect(source);
            if (Ortho != null) Ortho.Connect(source);
        }

        public void DisconnectSource(IMouseEventSource source)
        {
            if (source == null) return;
            if (Orbit != null) Orbit.Disconnect(source);
            if (Ortho != null) Ortho.Disconnect(source);
            if (_source == source) _source = null;
        }

        // ================================================================
        // RenderTexture リサイズ
        // ================================================================

        public void Resize(int w, int h)
        {
            w = Mathf.Max(1, w);
            h = Mathf.Max(1, h);
            if (RT != null && RT.width == w && RT.height == h) return;
            ReleaseRT();
            CreateRT(w, h);
        }

        // ================================================================
        // カメラ transform 更新
        // ================================================================

        public void ApplyCameraTransform()
        {
            if (Cam == null) return;
            if (Orbit != null) Orbit.ApplyCameraTransform(Cam);
            if (Ortho != null) Ortho.ApplyCameraTransform(Cam);
        }

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        public void ResetToMesh(Bounds bounds)
        {
            Orbit?.ResetToMesh(bounds);
            Ortho?.ResetToMesh(bounds);
        }

        // ================================================================
        // 内部ユーティリティ
        // ================================================================

        private void CreateRT(int w, int h)
        {
            RT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags    = HideFlags.HideAndDontSave,
                antiAliasing = 1,
            };
            RT.Create();
            if (Cam != null) Cam.targetTexture = RT;
        }

        private void ReleaseRT()
        {
            if (RT == null) return;
            if (Cam != null && Cam.targetTexture == RT) Cam.targetTexture = null;
            RT.Release();
            Object.Destroy(RT);
            RT = null;
        }
    }
}
