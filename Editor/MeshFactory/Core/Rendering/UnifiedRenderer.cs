// Assets/Editor/MeshFactory/Core/Rendering/UnifiedRenderer.cs
// 統合レンダラー
// UnifiedBufferManagerのデータをMesh生成+DrawMesh方式で描画

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using MeshFactory.Core;

namespace MeshFactory.Core.Rendering
{
    /// <summary>
    /// 統合レンダラー
    /// 旧システム(MeshGPURenderer_3D)と同じMesh生成方式で描画
    /// </summary>
    public class UnifiedRenderer : IDisposable
    {
        // ============================================================
        // シェーダー・マテリアル
        // ============================================================

        private Shader _pointShader;
        private Shader _wireframeShader;
        private Shader _pointOverlayShader;
        private Shader _wireframeOverlayShader;
        private ComputeShader _computeShader;

        private Material _wireframeMaterial;
        private Material _pointMaterial;
        private Material _wireframeOverlayMaterial;
        private Material _pointOverlayMaterial;

        // ============================================================
        // メッシュ
        // ============================================================

        private Mesh _wireframeMesh;
        private Mesh _pointMesh;

        // 描画キュー
        private List<Mesh> _pendingMeshes = new List<Mesh>();
        private List<Material> _pendingMaterials = new List<Material>();

        // ============================================================
        // 設定
        // ============================================================

        private ShaderColorSettings _colorSettings;

        // ============================================================
        // バッファ参照
        // ============================================================

        private UnifiedBufferManager _bufferManager;

        // ============================================================
        // 状態
        // ============================================================

        private bool _isInitialized = false;
        private bool _disposed = false;

        // カーネルインデックス
        private int _kernelClear;
        private int _kernelScreenPos;
        private int _kernelCulling;
        private int _kernelVertexHit;
        private int _kernelLineHit;
        private int _kernelFaceVisibility;
        private int _kernelFaceHit;
        private int _kernelUpdateHover;

        // ============================================================
        // プロパティ
        // ============================================================

        public bool IsInitialized => _isInitialized;
        public ShaderColorSettings ColorSettings => _colorSettings;
        
        /// <summary>
        /// 背面カリングを有効にするか
        /// </summary>
        public bool BackfaceCullingEnabled { get; set; } = true;

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UnifiedRenderer(UnifiedBufferManager bufferManager)
        {
            _bufferManager = bufferManager;
            _colorSettings = ShaderColorSettings.Default;
        }

        // ============================================================
        // 初期化
        // ============================================================

        /// <summary>
        /// レンダラーを初期化
        /// </summary>
        public bool Initialize()
        {
            if (_isInitialized)
                return true;

            // シェーダーロード
            _pointShader = Shader.Find("MeshFactory/Point3D");
            _wireframeShader = Shader.Find("MeshFactory/Wireframe3D");
            _pointOverlayShader = Shader.Find("MeshFactory/Point3D_Overlay");
            _wireframeOverlayShader = Shader.Find("MeshFactory/Wireframe3D_Overlay");

            // デバッグ: シェーダーロード状況
            //Debug.Log($"[UnifiedRenderer] Shader load status: Point3D={_pointShader != null}, Wireframe3D={_wireframeShader != null}, Point3D_Overlay={_pointOverlayShader != null}, Wireframe3D_Overlay={_wireframeOverlayShader != null}");

            if (_pointShader == null || _wireframeShader == null)
            {
                Debug.LogError("[UnifiedRenderer] Failed to load shaders");
                return false;
            }

            // マテリアル作成
            _wireframeMaterial = new Material(_wireframeShader) { hideFlags = HideFlags.HideAndDontSave };
            _pointMaterial = new Material(_pointShader) { hideFlags = HideFlags.HideAndDontSave };

            // 可視性バッファを使わない設定
            _wireframeMaterial.SetInt("_UseVisibilityBuffer", 0);
            _pointMaterial.SetInt("_UseVisibilityBuffer", 0);

            // オーバーレイマテリアル作成（選択要素をデプス無視で描画）
            if (_pointOverlayShader != null)
            {
                _pointOverlayMaterial = new Material(_pointOverlayShader) { hideFlags = HideFlags.HideAndDontSave };
                Debug.Log("[UnifiedRenderer] Point overlay material created");
            }
            else
            {
                Debug.LogWarning("[UnifiedRenderer] Point overlay shader not found!");
            }
            if (_wireframeOverlayShader != null)
            {
                _wireframeOverlayMaterial = new Material(_wireframeOverlayShader) { hideFlags = HideFlags.HideAndDontSave };
                Debug.Log("[UnifiedRenderer] Wireframe overlay material created");
            }
            else
            {
                Debug.LogWarning("[UnifiedRenderer] Wireframe overlay shader not found!");
            }

            // コンピュートシェーダーロード（オプション）
            _computeShader = Resources.Load<ComputeShader>("UnifiedCompute");
            if (_computeShader != null)
            {
                _kernelClear = _computeShader.FindKernel("ClearBuffers");
                _kernelScreenPos = _computeShader.FindKernel("ComputeScreenPositions");
                _kernelCulling = _computeShader.FindKernel("ComputeCulling");
                _kernelVertexHit = _computeShader.FindKernel("ComputeVertexHitTest");
                _kernelLineHit = _computeShader.FindKernel("ComputeLineHitTest");
                _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
                _kernelFaceHit = _computeShader.FindKernel("ComputeFaceHitTest");
                _kernelUpdateHover = _computeShader.FindKernel("UpdateHoverFlags");
            }

            _isInitialized = true;
            return true;
        }

        /// <summary>
        /// 色設定を変更
        /// </summary>
        public void SetColorSettings(ShaderColorSettings settings)
        {
            _colorSettings = settings ?? ShaderColorSettings.Default;
        }

        // ============================================================
        // メッシュ構築
        // ============================================================

        /// <summary>
        /// ワイヤーフレームメッシュを構築（後方互換用オーバーロード）
        /// </summary>
        public void UpdateWireframeMesh(float alpha = 1f)
        {
            // 全メッシュ描画（後方互換）
            UpdateWireframeMesh(-1, true, alpha, alpha);
        }

        /// <summary>
        /// ワイヤーフレームメッシュを構築（選択/非選択フィルタリング対応）
        /// </summary>
        /// <param name="selectedMeshIndex">選択メッシュインデックス（-1で全選択扱い）</param>
        /// <param name="showUnselected">非選択メッシュを表示するか</param>
        /// <param name="selectedAlpha">選択メッシュのアルファ値</param>
        /// <param name="unselectedAlpha">非選択メッシュのアルファ値</param>
        public void UpdateWireframeMesh(
            int selectedMeshIndex,
            bool showUnselected,
            float selectedAlpha = 1f,
            float unselectedAlpha = 0.4f)
        {
            if (_bufferManager == null)
            {
                if (_wireframeMesh != null) _wireframeMesh.Clear();
                return;
            }

            int lineCount = _bufferManager.TotalLineCount;
            int vertCount = _bufferManager.TotalVertexCount;
            int meshCount = _bufferManager.MeshCount;
            
            if (lineCount <= 0)
            {
                if (_wireframeMesh != null) _wireframeMesh.Clear();
                return;
            }

            // メッシュ初期化
            if (_wireframeMesh == null)
            {
                _wireframeMesh = new Mesh();
                _wireframeMesh.name = "UnifiedWireframeMesh";
                _wireframeMesh.hideFlags = HideFlags.HideAndDontSave;
                _wireframeMesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                _wireframeMesh.Clear();
            }

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();  // 元のラインインデックス格納用
            var indices = new List<int>();

            var positions = _bufferManager.Positions;
            var lines = _bufferManager.Lines;
            var lineFlags = _bufferManager.LineFlags;
            int vertexCount = _bufferManager.TotalVertexCount;

            for (int i = 0; i < lineCount; i++)
            {
                var line = lines[i];
                int v1 = (int)line.V1;
                int v2 = (int)line.V2;

                if (v1 < 0 || v1 >= vertexCount || v2 < 0 || v2 >= vertexCount)
                    continue;

                // メッシュインデックスでフィルタリング
                int lineMeshIndex = (int)line.MeshIndex;
                bool isSelected = (selectedMeshIndex < 0) || (lineMeshIndex == selectedMeshIndex);

                // 非選択メッシュはshowUnselected=falseならスキップ
                if (!isSelected && !showUnselected)
                    continue;

                // フラグから状態を決定
                uint flags = lineFlags != null && i < lineFlags.Length ? lineFlags[i] : 0;
                bool isHovered = (flags & (uint)SelectionFlags.Hovered) != 0;
                bool isEdgeSelected = (flags & (uint)SelectionFlags.EdgeSelected) != 0;
                // カリングはシェーダーで行う

                Vector3 p1 = positions[v1];
                Vector3 p2 = positions[v2];

                float alpha = isSelected ? selectedAlpha : unselectedAlpha;

                // 色設定
                Color normalColor = isSelected
                    ? new Color(0f, 1f, 0.5f, 0.9f * alpha)   // 緑
                    : new Color(0.5f, 0.5f, 0.5f, 0.7f * alpha); // グレー
                Color selectedColor = new Color(0f, 1f, 1f, 1f * alpha);
                Color auxColor = isSelected
                    ? new Color(1f, 0.3f, 1f, 0.9f * alpha)
                    : new Color(0.7f, 0.3f, 0.7f, 0.5f * alpha);
                Color hoverColor = new Color(1f, 1f, 0f, 1f * alpha);

                Color lineColor;
                if (isHovered)
                    lineColor = hoverColor;
                else if (isEdgeSelected)
                    lineColor = selectedColor;
                else if (line.IsAuxLine)
                    lineColor = auxColor;
                else
                    lineColor = normalColor;

                int baseIdx = vertices.Count;
                vertices.Add(p1);
                vertices.Add(p2);
                colors.Add(lineColor);
                colors.Add(lineColor);
                // 元のラインインデックスをUVに格納（シェーダーでフラグバッファ参照用）
                uvs.Add(new Vector2(i, 0));
                uvs.Add(new Vector2(i, 0));
                indices.Add(baseIdx);
                indices.Add(baseIdx + 1);
            }

            _wireframeMesh.SetVertices(vertices);
            _wireframeMesh.SetColors(colors);
            _wireframeMesh.SetUVs(0, uvs);  // UV0に元のラインインデックス
            _wireframeMesh.SetIndices(indices, MeshTopology.Lines, 0);
        }
        /*
        /// <summary>
        /// 頂点メッシュを構築（後方互換用オーバーロード）
        /// </summary>
        public void UpdatePointMesh(Camera camera, float pointSize = 0.02f, float alpha = 1f)
        {
            // 全メッシュ描画（後方互換）
            UpdatePointMesh(camera, -1, true, pointSize, alpha, alpha);
        }
        */
        /// <summary>
        /// 頂点メッシュを構築（選択/非選択フィルタリング対応、ビルボード方式）
        /// </summary>
        /// <param name="camera">カメラ</param>
        /// <param name="selectedMeshIndex">選択メッシュインデックス（-1で全選択扱い）</param>
        /// <param name="showUnselected">非選択メッシュを表示するか</param>
        /// <param name="pointSize">頂点サイズ</param>
        /// <param name="selectedAlpha">選択メッシュのアルファ値</param>
        /// <param name="unselectedAlpha">非選択メッシュのアルファ値</param>
        public void UpdatePointMesh(
            Camera camera,
            int selectedMeshIndex,
            bool showUnselected,
            float pointSize ,
            float selectedAlpha = 1f,
            float unselectedAlpha = 0.4f)
        {
            if (_bufferManager == null || camera == null)
            {
                if (_pointMesh != null) _pointMesh.Clear();
                return;
            }

            int totalVertexCount = _bufferManager.TotalVertexCount;
            int meshCount = _bufferManager.MeshCount;
            
            if (totalVertexCount <= 0)
            {
                if (_pointMesh != null) _pointMesh.Clear();
                return;
            }

            // メッシュ初期化
            if (_pointMesh == null)
            {
                _pointMesh = new Mesh();
                _pointMesh.name = "UnifiedPointMesh";
                _pointMesh.hideFlags = HideFlags.HideAndDontSave;
                _pointMesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                _pointMesh.Clear();
            }

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var uvs2 = new List<Vector2>();  // 元のバッファインデックス格納用
            var indices = new List<int>();

            var positions = _bufferManager.Positions;
            var vertexFlags = _bufferManager.VertexFlags;
            var meshInfos = _bufferManager.MeshInfos;

            Vector3 camRight = camera.transform.right;
            Vector3 camUp = camera.transform.up;

            // メッシュごとに処理
            for (int meshIdx = 0; meshIdx < meshCount; meshIdx++)
            {
                bool isSelected = (meshIdx == selectedMeshIndex) || (selectedMeshIndex < 0);

                // 非選択メッシュはshowUnselected=falseならスキップ
                if (!isSelected && !showUnselected)
                    continue;

                var meshInfo = meshInfos[meshIdx];
                float alpha = isSelected ? selectedAlpha : unselectedAlpha;
                float size = isSelected ? pointSize : pointSize * 0.5f;
                float halfSize = size * 0.5f;

                // このメッシュの頂点範囲をループ
                int vertStart = (int)meshInfo.VertexStart;
                int vertEnd = vertStart + (int)meshInfo.VertexCount;

                for (int i = vertStart; i < vertEnd; i++)
                {
                    if (i >= totalVertexCount) break;

                    // フラグから選択状態を決定
                    uint flags = vertexFlags != null && i < vertexFlags.Length ? vertexFlags[i] : 0;
                    bool isHovered = (flags & (uint)SelectionFlags.Hovered) != 0;
                    bool isVertexSelected = (flags & (uint)SelectionFlags.VertexSelected) != 0;
                    // カリングはシェーダーで行う

                    Vector3 center = positions[i];
                    
                    float selectState;
                    if (isHovered)
                        selectState = 0f;  // ホバー
                    else if (isVertexSelected)
                        selectState = 1f;  // 選択
                    else
                        selectState = 0.5f;  // 通常

                    // 非選択メッシュの頂点は薄く表示
                    if (!isSelected)
                        selectState = 0.5f;  // 常に通常色（薄い）

                    Color col = new Color(1, 1, 1, selectState * alpha);

                    Vector3 offset1 = (-camRight - camUp) * halfSize;
                    Vector3 offset2 = (camRight - camUp) * halfSize;
                    Vector3 offset3 = (-camRight + camUp) * halfSize;
                    Vector3 offset4 = (camRight + camUp) * halfSize;

                    int baseIdx = vertices.Count;

                    vertices.Add(center + offset1);
                    vertices.Add(center + offset2);
                    vertices.Add(center + offset3);
                    vertices.Add(center + offset4);

                    colors.Add(col);
                    colors.Add(col);
                    colors.Add(col);
                    colors.Add(col);

                    uvs.Add(new Vector2(0, 0));
                    uvs.Add(new Vector2(1, 0));
                    uvs.Add(new Vector2(0, 1));
                    uvs.Add(new Vector2(1, 1));

                    // 元のバッファインデックスをUV2に格納（シェーダーでフラグバッファ参照用）
                    uvs2.Add(new Vector2(i, 0));
                    uvs2.Add(new Vector2(i, 0));
                    uvs2.Add(new Vector2(i, 0));
                    uvs2.Add(new Vector2(i, 0));

                    indices.Add(baseIdx);
                    indices.Add(baseIdx + 1);
                    indices.Add(baseIdx + 2);
                    indices.Add(baseIdx + 2);
                    indices.Add(baseIdx + 1);
                    indices.Add(baseIdx + 3);
                }
            }

            _pointMesh.SetVertices(vertices);
            _pointMesh.SetColors(colors);
            _pointMesh.SetUVs(0, uvs);
            _pointMesh.SetUVs(1, uvs2);  // UV2に元のバッファインデックス
            _pointMesh.SetTriangles(indices, 0);
        }

        // ============================================================
        // 描画キュー
        // ============================================================

        /// <summary>
        /// ワイヤーフレームを描画キューに追加
        /// </summary>
        public void QueueWireframe()
        {
            // フラグバッファを通常マテリアルに設定
            bool hasBuffer = _bufferManager?.LineFlagsBuffer != null;
            if (hasBuffer && _wireframeMaterial != null)
            {
                _wireframeMaterial.SetBuffer("_LineFlagsBuffer", _bufferManager.LineFlagsBuffer);
                _wireframeMaterial.SetInt("_UseLineFlagsBuffer", 1);
                _wireframeMaterial.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
            }
            else if (_wireframeMaterial != null)
            {
                _wireframeMaterial.SetInt("_UseLineFlagsBuffer", 0);
                _wireframeMaterial.SetInt("_EnableBackfaceCulling", 0);
            }
            
            // 通常描画（デプステストあり、選択メッシュ非表示）
            if (_wireframeMesh != null && _wireframeMaterial != null && _wireframeMesh.vertexCount > 0)
            {
                var meshCopy = UnityEngine.Object.Instantiate(_wireframeMesh);
                meshCopy.hideFlags = HideFlags.HideAndDontSave;
                _pendingMeshes.Add(meshCopy);
                _pendingMaterials.Add(_wireframeMaterial);
            }
            
            // オーバーレイ描画（デプステストなし、選択メッシュのみ表示）
            if (hasBuffer && _wireframeMesh != null && _wireframeOverlayMaterial != null && _wireframeMesh.vertexCount > 0)
            {
                _wireframeOverlayMaterial.SetBuffer("_LineFlagsBuffer", _bufferManager.LineFlagsBuffer);
                _wireframeOverlayMaterial.SetInt("_UseLineFlagsBuffer", 1);
                _wireframeOverlayMaterial.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                
                var overlayMeshCopy = UnityEngine.Object.Instantiate(_wireframeMesh);
                overlayMeshCopy.hideFlags = HideFlags.HideAndDontSave;
                _pendingMeshes.Add(overlayMeshCopy);
                _pendingMaterials.Add(_wireframeOverlayMaterial);
            }
        }

        /// <summary>
        /// 頂点を描画キューに追加
        /// </summary>
        public void QueuePoints()
        {
            // フラグバッファは常に設定（MeshSelected非表示に必要）
            bool hasBuffer = _bufferManager?.VertexFlagsBuffer != null;
            if (hasBuffer && _pointMaterial != null)
            {
                _pointMaterial.SetBuffer("_VertexFlagsBuffer", _bufferManager.VertexFlagsBuffer);
                _pointMaterial.SetInt("_UseVertexFlagsBuffer", 1);
                _pointMaterial.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
            }
            else if (_pointMaterial != null)
            {
                _pointMaterial.SetInt("_UseVertexFlagsBuffer", 0);
                _pointMaterial.SetInt("_EnableBackfaceCulling", 0);
            }
            
            // 通常描画（デプステストあり、選択メッシュ非表示）
            if (_pointMesh != null && _pointMaterial != null && _pointMesh.vertexCount > 0)
            {
                var meshCopy = UnityEngine.Object.Instantiate(_pointMesh);
                meshCopy.hideFlags = HideFlags.HideAndDontSave;
                _pendingMeshes.Add(meshCopy);
                _pendingMaterials.Add(_pointMaterial);
            }
            
            // オーバーレイ描画（デプステストなし、選択メッシュのみ表示）
            if (hasBuffer && _pointMesh != null && _pointOverlayMaterial != null && _pointMesh.vertexCount > 0)
            {
                _pointOverlayMaterial.SetBuffer("_VertexFlagsBuffer", _bufferManager.VertexFlagsBuffer);
                _pointOverlayMaterial.SetInt("_UseVertexFlagsBuffer", 1);
                _pointOverlayMaterial.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                //Debug.Log($"[QueuePoints] Overlay: BackfaceCullingEnabled={BackfaceCullingEnabled}");
                
                var overlayMeshCopy = UnityEngine.Object.Instantiate(_pointMesh);
                overlayMeshCopy.hideFlags = HideFlags.HideAndDontSave;
                _pendingMeshes.Add(overlayMeshCopy);
                _pendingMaterials.Add(_pointOverlayMaterial);
            }
        }

        /// <summary>
        /// キューに入っているメッシュをPreviewRenderUtilityで描画
        /// </summary>
        public void DrawQueued(PreviewRenderUtility preview)
        {
            if (preview == null) return;

            for (int i = 0; i < _pendingMeshes.Count; i++)
            {
                var mesh = _pendingMeshes[i];
                var material = _pendingMaterials[i];
                if (mesh != null && material != null)
                {
                    preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
                }
            }
        }

        /// <summary>
        /// 描画後のクリーンアップ
        /// </summary>
        public void CleanupQueued()
        {
            foreach (var mesh in _pendingMeshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            _pendingMeshes.Clear();
            _pendingMaterials.Clear();
        }

        // ============================================================
        // GPU計算（オプション）
        // ============================================================
        /*
        /// <summary>
        /// GPUでスクリーン座標を計算
        /// </summary>
        public void ComputeScreenPositionsGPU(Matrix4x4 viewProjection, Vector4 viewport)
        {
            if (_computeShader == null || _bufferManager == null)
                return;

            int vertexCount = _bufferManager.TotalVertexCount;
            if (vertexCount <= 0)
                return;

            _computeShader.SetMatrix("_ViewProjectionMatrix", viewProjection);
            _computeShader.SetVector("_ViewportParams", viewport);
            _computeShader.SetInt("_VertexCount", vertexCount);
            _computeShader.SetInt("_UseMirror", _bufferManager.MirrorEnabled ? 1 : 0);

            _computeShader.SetBuffer(_kernelScreenPos, "_PositionBuffer", _bufferManager.PositionBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_ScreenPositionBuffer", _bufferManager.ScreenPosBuffer);
            _computeShader.SetBuffer(_kernelScreenPos, "_VertexFlagsBuffer", _bufferManager.VertexFlagsBuffer);

            if (_bufferManager.MirrorEnabled)
            {
                _computeShader.SetBuffer(_kernelScreenPos, "_MirrorPositionBuffer", _bufferManager.MirrorPositionBuffer);
            }

            int threadGroups = Mathf.CeilToInt(vertexCount / 64f);
            _computeShader.Dispatch(_kernelScreenPos, threadGroups, 1, 1);
        }
        */
        // ============================================================
        // IDisposable
        // ============================================================

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_wireframeMaterial != null)
                        UnityEngine.Object.DestroyImmediate(_wireframeMaterial);
                    if (_pointMaterial != null)
                        UnityEngine.Object.DestroyImmediate(_pointMaterial);
                    if (_wireframeOverlayMaterial != null)
                        UnityEngine.Object.DestroyImmediate(_wireframeOverlayMaterial);
                    if (_pointOverlayMaterial != null)
                        UnityEngine.Object.DestroyImmediate(_pointOverlayMaterial);
                    if (_wireframeMesh != null)
                        UnityEngine.Object.DestroyImmediate(_wireframeMesh);
                    if (_pointMesh != null)
                        UnityEngine.Object.DestroyImmediate(_pointMesh);

                    CleanupQueued();
                }

                _disposed = true;
                _isInitialized = false;
            }
        }

        ~UnifiedRenderer()
        {
            Dispose(false);
        }
    }
}
