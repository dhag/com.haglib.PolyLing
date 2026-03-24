// PlayerViewportManager.cs
// 3つの PlayerViewport（Perspective / Top / Front）を管理し、
// MeshSceneRenderer の描画呼び出しを各カメラに対して行う。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;

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

        // ================================================================
        // 内部
        // ================================================================

        private MeshSceneRenderer _renderer;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Initialize(Transform parent, MeshSceneRenderer renderer)
        {
            _renderer = renderer;

            PerspectiveViewport = new PlayerViewport();
            TopViewport         = new PlayerViewport();
            FrontViewport       = new PlayerViewport();

            PerspectiveViewport.Initialize(ViewportMode.Perspective, parent);
            TopViewport        .Initialize(ViewportMode.Top,         parent);
            FrontViewport      .Initialize(ViewportMode.Front,       parent);
        }

        public void Dispose()
        {
            PerspectiveViewport?.Dispose();
            TopViewport        ?.Dispose();
            FrontViewport      ?.Dispose();
            PerspectiveViewport = null;
            TopViewport         = null;
            FrontViewport       = null;
        }

        // ================================================================
        // 毎フレーム更新（Update から呼ぶ）
        // ================================================================

        public void Update()
        {
            PerspectiveViewport?.ApplyCameraTransform();
            TopViewport        ?.ApplyCameraTransform();
            FrontViewport      ?.ApplyCameraTransform();
        }

        // ================================================================
        // 描画（LateUpdate から呼ぶ）
        // ================================================================

        public void LateUpdate(ProjectContext project)
        {
            if (_renderer == null) return;

            DrawViewport(project, PerspectiveViewport);
            DrawViewport(project, TopViewport);
            DrawViewport(project, FrontViewport);
        }

        // ================================================================
        // MeshSceneRenderer 委譲
        // ================================================================

        public void RebuildAdapter(int mi, ModelContext model)
            => _renderer?.RebuildAdapter(mi, model);

        public void ClearScene()
            => _renderer?.ClearScene();

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        public void ResetToMesh(Bounds bounds)
        {
            PerspectiveViewport?.ResetToMesh(bounds);
            TopViewport        ?.ResetToMesh(bounds);
            FrontViewport      ?.ResetToMesh(bounds);
        }

        // ================================================================
        // 内部
        // ================================================================

        private void DrawViewport(ProjectContext project, PlayerViewport vp)
        {
            if (vp == null || !vp.IsReady) return;
            var cam = vp.Cam;

            // RT に描画してから元に戻す
            _renderer.DrawMeshes(project, cam);
            _renderer.DrawWireframeAndVertices(cam);
            _renderer.DrawBones(project, cam);
        }
    }
}
