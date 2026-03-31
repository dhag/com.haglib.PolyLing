// PrimitivePreviewViewport.cs
// 図形生成プレビュー用カメラ。
// Camera + RenderTexture + OrbitCameraController を保持し、
// Graphics.DrawMesh でプレビューメッシュを描画する。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    public class PrimitivePreviewViewport
    {
        public Camera              Cam   { get; private set; }
        public RenderTexture       RT    { get; private set; }
        public OrbitCameraController Orbit { get; private set; }

        public bool IsReady => Cam != null && RT != null && RT.IsCreated();

        private GameObject _camGo;
        private Material   _solidMat;
        private Material   _wireMat;
        private Mesh       _solidMesh;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        public void Initialize(Transform parent)
        {
            _camGo = new GameObject("PrimitivePreview") { hideFlags = HideFlags.HideAndDontSave };
            if (parent != null) _camGo.transform.SetParent(parent, false);

            Cam = _camGo.AddComponent<Camera>();
            Cam.enabled         = false;
            Cam.clearFlags      = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);
            Cam.cullingMask     = ~0;
            Cam.nearClipPlane   = 0.01f;
            Cam.farClipPlane    = 1000f;
            Cam.fieldOfView     = 40f;

            Orbit = new OrbitCameraController();
            Orbit.ResetToMesh(new Bounds(Vector3.zero, Vector3.one));

            CreateRT(256, 256);
            BuildMaterials();
        }

        public void Dispose()
        {
            ReleaseRT();
            DestroyMesh(ref _solidMesh);
            DestroyMat(ref _solidMat);
            DestroyMat(ref _wireMat);
            if (_camGo != null) { Object.Destroy(_camGo); _camGo = null; Cam = null; }
        }

        // ================================================================
        // メッシュ更新
        // ================================================================

        public void SetMesh(Data.MeshObject meshObj)
        {
            DestroyMesh(ref _solidMesh);
            if (meshObj == null || meshObj.VertexCount == 0) return;

            _solidMesh = meshObj.ToUnityMesh();
            _solidMesh.hideFlags = HideFlags.HideAndDontSave;
            Orbit.ResetToMesh(_solidMesh.bounds);
        }

        // ================================================================
        // RT リサイズ
        // ================================================================

        public void Resize(int w, int h)
        {
            w = Mathf.Max(1, w); h = Mathf.Max(1, h);
            if (RT != null && RT.width == w && RT.height == h) return;
            ReleaseRT();
            CreateRT(w, h);
        }

        // ================================================================
        // Tick（毎フレーム呼ぶ）
        // ================================================================

        public void Tick(Mesh wireMesh)
        {
            if (!IsReady) return;
            Orbit.ApplyCameraTransform(Cam);
            if (_solidMesh != null && _solidMat != null)
                Graphics.DrawMesh(_solidMesh, Matrix4x4.identity, _solidMat, 0, Cam);
            if (wireMesh != null && _wireMat != null)
                Graphics.DrawMesh(wireMesh,   Matrix4x4.identity, _wireMat,  0, Cam);
            Cam.targetTexture = RT;
            Cam.Render();
        }

        // ================================================================
        // 内部
        // ================================================================

        private void CreateRT(int w, int h)
        {
            RT = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                { hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            RT.Create();
            if (Cam != null) Cam.targetTexture = RT;
        }

        private void ReleaseRT()
        {
            if (RT == null) return;
            if (Cam != null && Cam.targetTexture == RT) Cam.targetTexture = null;
            RT.Release(); Object.Destroy(RT); RT = null;
        }

        private void BuildMaterials()
        {
            var solidShader = Shader.Find("Universal Render Pipeline/Lit")
                           ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                           ?? Shader.Find("Standard")
                           ?? Shader.Find("Unlit/Color");
            if (solidShader != null)
            {
                _solidMat = new Material(solidShader) { hideFlags = HideFlags.HideAndDontSave };
                _solidMat.SetColor("_BaseColor", new Color(0.72f, 0.72f, 0.75f));
                _solidMat.SetColor("_Color",     new Color(0.72f, 0.72f, 0.75f));
            }

            var wireShader = Shader.Find("Hidden/Internal-Colored")
                          ?? Shader.Find("Unlit/Color");
            if (wireShader != null)
            {
                _wireMat = new Material(wireShader) { hideFlags = HideFlags.HideAndDontSave };
                _wireMat.SetColor("_Color", new Color(0.1f, 0.1f, 0.12f, 1f));
                _wireMat.SetInt("_ZTest",  (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                _wireMat.SetInt("_ZWrite", 0);
            }
        }

        private static void DestroyMesh(ref Mesh m)
            { if (m != null) { Object.Destroy(m); m = null; } }
        private static void DestroyMat(ref Material m)
            { if (m != null) { Object.Destroy(m); m = null; } }
    }
}
