// Assets/Editor/Poly_Ling/Core/Rendering/UnifiedRenderer.cs
// 統合レンダラー
// UnifiedBufferManagerのデータをMesh生成+DrawMesh方式で描画
//
// 【パイプライン分離】
// 選択メッシュと非選択メッシュを別々のMeshに構築する。
// - 選択メッシュ: オーバーレイ描画（ZTest Always）。ドラッグ中は位置のみ軽量更新。
// - 非選択メッシュ: 通常描画（ZTest LEqual）。ドラッグ中は更新不要（位置不変）。
// これにより:
// - シェーダーのFLAG_MESH_SELECTEDチェック＋99999飛ばしが不要
// - TransformDragging中のGPU転送量が選択メッシュ分のみに削減

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Core.Rendering
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
        // Phase 2c: 面塗り overlay（選択面/ホバー面）
        private Shader _faceOverlayShader;
        private ComputeShader _computeShader;

        private Material _wireframeMaterial;
        private Material _pointMaterial;

        private Material _wireframeOverlayMaterial;
        private Material _pointOverlayMaterial;
        // Phase 2c: 面塗り overlay マテリアル
        private Material _faceOverlayMaterial;

        // ============================================================
        // メッシュ（選択/非選択分離）
        // ============================================================

        private Mesh _wireframeMeshSelected;
        private Mesh _wireframeMeshUnselected;
        private Mesh _pointMeshSelected;
        private Mesh _pointMeshUnselected;
        // Phase 2c: 面塗り overlay mesh（選択メッシュのみ、非選択メッシュの面は塗らない）
        private Mesh _faceOverlayMeshSelected;

        // 描画キュー（per-slot）
        // Phase 1: slot (viewport) 毎に独立したキューを保持し、OnRenderObject() が
        // slot 指定で DrawQueued(cam, slot) を呼ぶ構造にする。
        // Queue 構築は PrepareDrawing 系 (event 駆動) で行い、
        // DrawQueued は Graphics.DrawMesh 提出のみを行う。
        // UnifiedBufferManager.CullingSlotCount と一致（4）。
        private List<Mesh>[]                  _pendingMeshesBySlot;
        private List<Material>[]              _pendingMaterialsBySlot;
        private List<MaterialPropertyBlock>[] _pendingPropertyBlocksBySlot;

        // per-slot MPB キャッシュ（Graphics.DrawMesh 呼び出し時にキャプチャされる）
        private MaterialPropertyBlock[] _wireframeMPBs;
        private MaterialPropertyBlock[] _wireframeOverlayMPBs;
        private MaterialPropertyBlock[] _pointMPBs;
        private MaterialPropertyBlock[] _pointOverlayMPBs;
        // Phase 2c: 面塗り overlay MPB（per-slot）
        private MaterialPropertyBlock[] _faceOverlayMPBs;

        // =====================================================================
        // 【重要】メッシュ構築用キャッシュリスト - 毎フレームnewしないこと！
        // =====================================================================
        // UpdatePointMesh/UpdateWireframeMesh内で毎フレーム new List<>() すると
        // 大量のGCが発生し、カメラ操作やマウス移動が重くなる。
        // 必ずこれらのキャッシュを Clear() して再利用すること。
        // =====================================================================
        private List<Vector3> _cachedVertices = new List<Vector3>();
        private List<Color> _cachedColors = new List<Color>();
        private List<Vector2> _cachedUVs = new List<Vector2>();
        private List<Vector2> _cachedUVs2 = new List<Vector2>();
        private List<int> _cachedIndices = new List<int>();

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
            _pointShader = Shader.Find("Poly_Ling/Point3D");
            _wireframeShader = Shader.Find("Poly_Ling/Wireframe3D");
            _pointOverlayShader = Shader.Find("Poly_Ling/Point3D_Overlay");
            _wireframeOverlayShader = Shader.Find("Poly_Ling/Wireframe3D_Overlay");
            // Phase 2c: 面塗り overlay
            _faceOverlayShader = Shader.Find("Poly_Ling/Face3D_Overlay");

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
            }
            else
            {
                Debug.LogWarning("[UnifiedRenderer] Point overlay shader not found!");
            }
            if (_wireframeOverlayShader != null)
            {
                _wireframeOverlayMaterial = new Material(_wireframeOverlayShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            else
            {
                Debug.LogWarning("[UnifiedRenderer] Wireframe overlay shader not found!");
            }
            // Phase 2c: 面塗り overlay マテリアル
            if (_faceOverlayShader != null)
            {
                _faceOverlayMaterial = new Material(_faceOverlayShader) { hideFlags = HideFlags.HideAndDontSave };
            }
            else
            {
                Debug.LogWarning("[UnifiedRenderer] Face overlay shader not found!");
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

            // per-slot MaterialPropertyBlock 初期化
            int N = UnifiedBufferManager.CullingSlotCount;
            _wireframeMPBs        = new MaterialPropertyBlock[N];
            _wireframeOverlayMPBs = new MaterialPropertyBlock[N];
            _pointMPBs            = new MaterialPropertyBlock[N];
            _pointOverlayMPBs     = new MaterialPropertyBlock[N];
            // Phase 2c: 面塗り overlay の per-slot MPB
            _faceOverlayMPBs      = new MaterialPropertyBlock[N];
            for (int s = 0; s < N; s++)
            {
                _wireframeMPBs[s]        = new MaterialPropertyBlock();
                _wireframeOverlayMPBs[s] = new MaterialPropertyBlock();
                _pointMPBs[s]            = new MaterialPropertyBlock();
                _pointOverlayMPBs[s]     = new MaterialPropertyBlock();
                _faceOverlayMPBs[s]      = new MaterialPropertyBlock();
            }

            // per-slot 描画キュー初期化
            _pendingMeshesBySlot         = new List<Mesh>[N];
            _pendingMaterialsBySlot      = new List<Material>[N];
            _pendingPropertyBlocksBySlot = new List<MaterialPropertyBlock>[N];
            for (int s = 0; s < N; s++)
            {
                _pendingMeshesBySlot[s]         = new List<Mesh>();
                _pendingMaterialsBySlot[s]      = new List<Material>();
                _pendingPropertyBlocksBySlot[s] = new List<MaterialPropertyBlock>();
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
        // メッシュ構築（選択/非選択分離）
        // ============================================================

        /// <summary>
        /// 選択メッシュのワイヤーフレームを構築
        /// </summary>
        public void UpdateWireframeMeshSelected(
            float selectedAlpha = 1f)
        {
            BuildWireframeMesh(
                ref _wireframeMeshSelected,
                "UnifiedWireframeMesh_Selected",
                forSelected: true,
                selectedAlpha);
        }

        /// <summary>
        /// 非選択メッシュのワイヤーフレームを構築
        /// </summary>
        public void UpdateWireframeMeshUnselected(
            float unselectedAlpha = 0.4f)
        {
            BuildWireframeMesh(
                ref _wireframeMeshUnselected,
                "UnifiedWireframeMesh_Unselected",
                forSelected: false,
                unselectedAlpha);
        }

        /// <summary>
        /// ワイヤーフレームメッシュ構築の共通処理
        /// </summary>
        /// <param name="mesh">対象Mesh参照</param>
        /// <param name="meshName">Meshの名前</param>
        /// <param name="forSelected">true: 選択メッシュの辺のみ, false: 非選択メッシュの辺のみ</param>
        /// <param name="alpha">アルファ値</param>
        private void BuildWireframeMesh(
            ref Mesh mesh,
            string meshName,
            bool forSelected,
            float alpha)
        {
            if (_bufferManager == null)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            int lineCount = _bufferManager.TotalLineCount;
            int vertexCount = _bufferManager.TotalVertexCount;

            if (lineCount <= 0)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            // メッシュ初期化
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = meshName;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                mesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                mesh.Clear();
            }

            // キャッシュリストをクリアして再利用
            _cachedVertices.Clear();
            _cachedColors.Clear();
            _cachedUVs.Clear();
            _cachedIndices.Clear();

            var positions = _bufferManager.GetDisplayPositions();
            var lines = _bufferManager.Lines;
            var lineFlags = _bufferManager.LineFlags;

            for (int i = 0; i < lineCount; i++)
            {
                uint flags = lineFlags != null && i < lineFlags.Length ? lineFlags[i] : 0;
                bool isMeshSelected = (flags & (uint)SelectionFlags.MeshSelected) != 0;

                // 選択/非選択フィルタ
                if (forSelected != isMeshSelected) continue;

                var line = lines[i];
                int v1 = (int)line.V1;
                int v2 = (int)line.V2;

                if (v1 < 0 || v1 >= vertexCount || v2 < 0 || v2 >= vertexCount)
                    continue;

                Vector3 p1 = positions[v1];
                Vector3 p2 = positions[v2];

                // 色決定
                Color lineColor;
                if (forSelected)
                {
                    // 選択メッシュ: ホバー/エッジ選択/補助線/通常
                    bool isHovered = (flags & (uint)SelectionFlags.Hovered) != 0;
                    bool isEdgeSelected = (flags & (uint)SelectionFlags.EdgeSelected) != 0;

                    if (isHovered)
                        lineColor = _colorSettings.WithAlpha(_colorSettings.LineHovered, alpha);
                    else if (isEdgeSelected)
                        lineColor = _colorSettings.WithAlpha(_colorSettings.EdgeSelected, alpha);
                    else if (line.IsAuxLine)
                        lineColor = _colorSettings.WithAlpha(_colorSettings.AuxLineSelectedMesh, alpha);
                    else
                        lineColor = _colorSettings.WithAlpha(_colorSettings.LineSelectedMesh, alpha);
                }
                else
                {
                    // 非選択メッシュ: 補助線/通常のみ
                    if (line.IsAuxLine)
                        lineColor = _colorSettings.WithAlpha(_colorSettings.AuxLineUnselectedMesh, alpha);
                    else
                        lineColor = _colorSettings.WithAlpha(_colorSettings.LineUnselectedMesh, alpha);
                }

                int baseIdx = _cachedVertices.Count;
                _cachedVertices.Add(p1);
                _cachedVertices.Add(p2);
                _cachedColors.Add(lineColor);
                _cachedColors.Add(lineColor);
                // 元のラインインデックスをUVに格納（シェーダーでフラグバッファ参照用）
                _cachedUVs.Add(new Vector2(i, 0));
                _cachedUVs.Add(new Vector2(i, 0));
                _cachedIndices.Add(baseIdx);
                _cachedIndices.Add(baseIdx + 1);
            }

            mesh.SetVertices(_cachedVertices);
            mesh.SetColors(_cachedColors);
            mesh.SetUVs(0, _cachedUVs);
            mesh.SetIndices(_cachedIndices, MeshTopology.Lines, 0);
        }

        // ============================================================
        // Phase 2c: 面塗り overlay メッシュ構築（選択面・ホバー面）
        //
        // 三角形ファンで面を三角形化し、uv.x に globalFaceIndex を埋め込む。
        // シェーダ側で _FaceFlagsBuffer[uv.x] を読み FLAG_FACE_SELECTED / FLAG_HOVERED
        // に応じた色を塗る。カリング判定も _FaceCulledBuffer[uv.x] で行う。
        // 画面サイズ変更・視点移動・頂点移動すべて GPU 投影パイプラインに委ねるため
        // 自動追従する。
        // ============================================================

        /// <summary>
        /// 選択メッシュの面塗り overlay メッシュを構築する。
        /// 選択面・ホバー面の塗りつぶし表示に使用。
        /// </summary>
        public void UpdateFaceOverlayMeshSelected()
        {
            BuildFaceOverlayMesh(
                ref _faceOverlayMeshSelected,
                "UnifiedFaceOverlayMesh_Selected",
                forSelected: true);
        }

        /// <summary>
        /// 面塗り overlay mesh 構築の共通処理。
        /// </summary>
        private void BuildFaceOverlayMesh(
            ref Mesh mesh,
            string meshName,
            bool forSelected)
        {
            if (_bufferManager == null)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            int faceCount = _bufferManager.TotalFaceCount;
            int vertexCount = _bufferManager.TotalVertexCount;

            if (faceCount <= 0)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            // メッシュ初期化
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = meshName;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                mesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                mesh.Clear();
            }

            _cachedVertices.Clear();
            _cachedColors.Clear();
            _cachedUVs.Clear();
            _cachedIndices.Clear();

            var positions = _bufferManager.GetDisplayPositions();
            var faces = _bufferManager.Faces;
            var faceFlags = _bufferManager.FaceFlags;
            var indexBuffer = _bufferManager.Indices;

            if (positions == null || faces == null || indexBuffer == null) return;

            for (int fi = 0; fi < faceCount; fi++)
            {
                uint flags = faceFlags != null && fi < faceFlags.Length ? faceFlags[fi] : 0;
                bool isMeshSelected = (flags & (uint)SelectionFlags.MeshSelected) != 0;

                // 選択/非選択フィルタ
                if (forSelected != isMeshSelected) continue;

                var face = faces[fi];
                uint triCount = face.VertexCount >= 2u ? face.VertexCount - 2u : 0u;
                if (triCount == 0) continue;

                // 三角形ファンを直接 _IndexBuffer から読み出して組み立てる。
                // 各三角形の 3 頂点それぞれに uv.x = faceIndex を仕込む。
                for (uint t = 0; t < triCount; t++)
                {
                    uint i0 = indexBuffer[face.IndexStart + t * 3 + 0];
                    uint i1 = indexBuffer[face.IndexStart + t * 3 + 1];
                    uint i2 = indexBuffer[face.IndexStart + t * 3 + 2];

                    if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) continue;

                    int baseIdx = _cachedVertices.Count;
                    _cachedVertices.Add(positions[i0]);
                    _cachedVertices.Add(positions[i1]);
                    _cachedVertices.Add(positions[i2]);

                    // color は使用しない（シェーダで _FaceFlagsBuffer 参照）
                    _cachedColors.Add(Color.white);
                    _cachedColors.Add(Color.white);
                    _cachedColors.Add(Color.white);

                    // uv.x に globalFaceIndex を埋め込む
                    var uvFace = new Vector2(fi, 0);
                    _cachedUVs.Add(uvFace);
                    _cachedUVs.Add(uvFace);
                    _cachedUVs.Add(uvFace);

                    _cachedIndices.Add(baseIdx);
                    _cachedIndices.Add(baseIdx + 1);
                    _cachedIndices.Add(baseIdx + 2);
                }
            }

            mesh.SetVertices(_cachedVertices);
            mesh.SetColors(_cachedColors);
            mesh.SetUVs(0, _cachedUVs);
            mesh.SetIndices(_cachedIndices, MeshTopology.Triangles, 0);
        }

        /// <summary>
        /// 面塗り overlay を描画キューに追加する。
        /// </summary>
        public void QueueFaceOverlay(int cullingSlot = 0)
        {
            if (_faceOverlayMaterial == null) return;
            if (_faceOverlayMeshSelected == null || _faceOverlayMeshSelected.vertexCount == 0) return;

            bool hasFlagsBuffer = _bufferManager?.FaceFlagsBuffer != null;
            var faceCulledBuf = _bufferManager?.GetFaceCulledBuffer(cullingSlot);
            int slot = (_faceOverlayMPBs != null && cullingSlot < _faceOverlayMPBs.Length) ? cullingSlot : 0;

            MaterialPropertyBlock mpb = null;
            if (hasFlagsBuffer)
            {
                _faceOverlayMaterial.SetBuffer("_FaceFlagsBuffer", _bufferManager.FaceFlagsBuffer);
                _faceOverlayMaterial.SetInt("_UseFaceFlagsBuffer", 1);
                _faceOverlayMaterial.SetColor("_FaceHoveredFill",  _colorSettings.FaceHoveredFill);
                _faceOverlayMaterial.SetColor("_FaceSelectedFill", _colorSettings.FaceSelectedFill);

                // _FaceCulledBuffer と _EnableBackfaceCulling は per-slot → MPB でキャプチャ
                mpb = _faceOverlayMPBs?[slot];
                mpb?.SetBuffer("_FaceCulledBuffer", faceCulledBuf ?? _bufferManager.FaceFlagsBuffer);
                mpb?.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
            }
            else
            {
                _faceOverlayMaterial.SetInt("_UseFaceFlagsBuffer", 0);
                _faceOverlayMaterial.SetInt("_EnableBackfaceCulling", 0);
            }

            _pendingMeshesBySlot[slot].Add(_faceOverlayMeshSelected);
            _pendingMaterialsBySlot[slot].Add(_faceOverlayMaterial);
            _pendingPropertyBlocksBySlot[slot].Add(mpb);
        }

        /// <summary>
        /// 選択メッシュの頂点メッシュを構築
        /// </summary>
        public void UpdatePointMeshSelected(
            Camera camera,
            float pointSize,
            float selectedAlpha = 1f)
        {
            BuildPointMesh(
                ref _pointMeshSelected,
                "UnifiedPointMesh_Selected",
                camera,
                forSelected: true,
                pointSize,
                selectedAlpha);
        }

        /// <summary>
        /// 非選択メッシュの頂点メッシュを構築
        /// </summary>
        public void UpdatePointMeshUnselected(
            Camera camera,
            float pointSize,
            float unselectedAlpha = 0.4f)
        {
            BuildPointMesh(
                ref _pointMeshUnselected,
                "UnifiedPointMesh_Unselected",
                camera,
                forSelected: false,
                pointSize,
                unselectedAlpha);
        }

        /// <summary>
        /// 頂点メッシュ構築の共通処理
        /// </summary>
        /// <param name="mesh">対象Mesh参照</param>
        /// <param name="meshName">Meshの名前</param>
        /// <param name="camera">カメラ</param>
        /// <param name="forSelected">true: 選択メッシュの頂点のみ, false: 非選択メッシュの頂点のみ</param>
        /// <param name="pointSize">頂点サイズ</param>
        /// <param name="alpha">アルファ値</param>
        private void BuildPointMesh(
            ref Mesh mesh,
            string meshName,
            Camera camera,
            bool forSelected,
            float pointSize,
            float alpha)
        {
            if (_bufferManager == null || camera == null)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            int totalVertexCount = _bufferManager.TotalVertexCount;
            int meshCount = _bufferManager.MeshCount;

            if (totalVertexCount <= 0)
            {
                if (mesh != null) mesh.Clear();
                return;
            }

            // メッシュ初期化
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = meshName;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                mesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                mesh.Clear();
            }

            // キャッシュリストをクリアして再利用
            _cachedVertices.Clear();
            _cachedColors.Clear();
            _cachedUVs.Clear();
            _cachedUVs2.Clear();
            _cachedIndices.Clear();

            var positions = _bufferManager.GetDisplayPositions();
            var vertexFlags = _bufferManager.VertexFlags;
            var meshInfos = _bufferManager.MeshInfos;

            Vector3 camRight = camera.transform.right;
            Vector3 camUp = camera.transform.up;

            bool useScreenSpace = _pointMaterial != null && _pointMaterial.GetInt("_UseScreenSpace") > 0;
            float size = useScreenSpace ? 0f : pointSize;
            float halfSize = size * 0.5f;

            for (int meshIdx = 0; meshIdx < meshCount; meshIdx++)
            {
                var meshInfo = meshInfos[meshIdx];
                int vertStart = (int)meshInfo.VertexStart;
                int vertEnd = vertStart + (int)meshInfo.VertexCount;

                for (int i = vertStart; i < vertEnd; i++)
                {
                    if (i >= totalVertexCount) break;

                    uint flags = vertexFlags != null && i < vertexFlags.Length ? vertexFlags[i] : 0;
                    bool isMeshSelected = (flags & (uint)SelectionFlags.MeshSelected) != 0;

                    // 選択/非選択フィルタ
                    if (forSelected != isMeshSelected) continue;

                    // 選択状態（色のエンコード）
                    float selectState;
                    if (forSelected)
                    {
                        bool isHovered = (flags & (uint)SelectionFlags.Hovered) != 0;
                        bool isVertexSelected = (flags & (uint)SelectionFlags.VertexSelected) != 0;

                        if (isHovered)
                            selectState = 0f;   // ホバー
                        else if (isVertexSelected)
                            selectState = 1f;   // 選択
                        else
                            selectState = 0.5f; // 通常
                    }
                    else
                    {
                        selectState = 0.5f; // 非選択メッシュは常に通常状態
                    }

                    Vector3 center = positions[i];
                    Color col = new Color(1, 1, 1, selectState * alpha);

                    Vector3 offset1 = (-camRight - camUp) * halfSize;
                    Vector3 offset2 = (camRight - camUp) * halfSize;
                    Vector3 offset3 = (-camRight + camUp) * halfSize;
                    Vector3 offset4 = (camRight + camUp) * halfSize;

                    int baseIdx = _cachedVertices.Count;

                    _cachedVertices.Add(center + offset1);
                    _cachedVertices.Add(center + offset2);
                    _cachedVertices.Add(center + offset3);
                    _cachedVertices.Add(center + offset4);

                    _cachedColors.Add(col);
                    _cachedColors.Add(col);
                    _cachedColors.Add(col);
                    _cachedColors.Add(col);

                    _cachedUVs.Add(new Vector2(0, 0));
                    _cachedUVs.Add(new Vector2(1, 0));
                    _cachedUVs.Add(new Vector2(0, 1));
                    _cachedUVs.Add(new Vector2(1, 1));

                    // 元のバッファインデックスをUV2に格納（シェーダーでフラグバッファ参照用）
                    _cachedUVs2.Add(new Vector2(i, 0));
                    _cachedUVs2.Add(new Vector2(i, 0));
                    _cachedUVs2.Add(new Vector2(i, 0));
                    _cachedUVs2.Add(new Vector2(i, 0));

                    _cachedIndices.Add(baseIdx);
                    _cachedIndices.Add(baseIdx + 1);
                    _cachedIndices.Add(baseIdx + 2);
                    _cachedIndices.Add(baseIdx + 2);
                    _cachedIndices.Add(baseIdx + 1);
                    _cachedIndices.Add(baseIdx + 3);
                }
            }

            mesh.SetVertices(_cachedVertices);
            mesh.SetColors(_cachedColors);
            mesh.SetUVs(0, _cachedUVs);
            mesh.SetUVs(1, _cachedUVs2);
            mesh.SetTriangles(_cachedIndices, 0);
        }

        // ============================================================
        // 軽量位置更新（TransformDragging中専用）
        // 選択メッシュのみ更新。非選択メッシュはドラッグ中位置不変のため更新不要。
        // トポロジ（インデックス・カラー・UV）は不変のまま頂点位置のみ差し替え
        // ============================================================

        /// <summary>
        /// 選択メッシュのワイヤーフレーム頂点位置のみを更新（軽量パス）。
        /// TransformDragging中に使用。非選択メッシュは更新不要。
        /// </summary>
        public void UpdateWireframePositionsOnly()
        {
            if (_bufferManager == null || _wireframeMeshSelected == null) return;
            if (_wireframeMeshSelected.vertexCount == 0) return;

            var positions = _bufferManager.GetDisplayPositions();
            var lines = _bufferManager.Lines;
            var lineFlags = _bufferManager.LineFlags;
            int lineCount = _bufferManager.TotalLineCount;
            int vertexCount = _bufferManager.TotalVertexCount;

            _cachedVertices.Clear();

            for (int i = 0; i < lineCount; i++)
            {
                uint flags = lineFlags != null && i < lineFlags.Length ? lineFlags[i] : 0;
                if ((flags & (uint)SelectionFlags.MeshSelected) == 0) continue;

                var line = lines[i];
                int v1 = (int)line.V1;
                int v2 = (int)line.V2;

                if (v1 < 0 || v1 >= vertexCount || v2 < 0 || v2 >= vertexCount)
                    continue;

                _cachedVertices.Add(positions[v1]);
                _cachedVertices.Add(positions[v2]);
            }

            // 頂点数が前回のフル構築と一致する場合のみ更新（トポロジ不変の保証）
            if (_cachedVertices.Count == _wireframeMeshSelected.vertexCount)
            {
                _wireframeMeshSelected.SetVertices(_cachedVertices);
            }
        }

        /// <summary>
        /// 選択メッシュの頂点メッシュ（ポイントビルボード）の位置のみを更新（軽量パス）。
        /// TransformDragging中に使用。非選択メッシュは更新不要。
        /// </summary>
        public void UpdatePointPositionsOnly(Camera camera, float pointSize)
        {
            if (_bufferManager == null || _pointMeshSelected == null || camera == null) return;
            if (_pointMeshSelected.vertexCount == 0) return;

            var positions = _bufferManager.GetDisplayPositions();
            var vertexFlags = _bufferManager.VertexFlags;
            var meshInfos = _bufferManager.MeshInfos;
            int meshCount = _bufferManager.MeshCount;
            int totalVertexCount = _bufferManager.TotalVertexCount;

            Vector3 camRight = camera.transform.right;
            Vector3 camUp = camera.transform.up;

            bool useScreenSpace = _pointMaterial != null && _pointMaterial.GetInt("_UseScreenSpace") > 0;
            float size = useScreenSpace ? 0f : pointSize;
            float halfSize = size * 0.5f;

            Vector3 offset1 = (-camRight - camUp) * halfSize;
            Vector3 offset2 = ( camRight - camUp) * halfSize;
            Vector3 offset3 = (-camRight + camUp) * halfSize;
            Vector3 offset4 = ( camRight + camUp) * halfSize;

            _cachedVertices.Clear();

            for (int meshIdx = 0; meshIdx < meshCount; meshIdx++)
            {
                var meshInfo = meshInfos[meshIdx];
                int vertStart = (int)meshInfo.VertexStart;
                int vertEnd = vertStart + (int)meshInfo.VertexCount;

                for (int i = vertStart; i < vertEnd; i++)
                {
                    if (i >= totalVertexCount) break;

                    uint flags = vertexFlags != null && i < vertexFlags.Length ? vertexFlags[i] : 0;
                    if ((flags & (uint)SelectionFlags.MeshSelected) == 0) continue;

                    Vector3 center = positions[i];
                    _cachedVertices.Add(center + offset1);
                    _cachedVertices.Add(center + offset2);
                    _cachedVertices.Add(center + offset3);
                    _cachedVertices.Add(center + offset4);
                }
            }

            // 頂点数が前回のフル構築と一致する場合のみ更新（トポロジ不変の保証）
            if (_cachedVertices.Count == _pointMeshSelected.vertexCount)
            {
                _pointMeshSelected.SetVertices(_cachedVertices);
            }
        }

        // ============================================================
        // 描画キュー
        // ============================================================

        /// <summary>
        /// ワイヤーフレームを描画キューに追加
        /// 選択メッシュ → オーバーレイ描画（ZTest Always）
        /// 非選択メッシュ → 通常描画（ZTest LEqual）
        /// </summary>
        /// <param name="showUnselected">非選択メッシュを表示するか</param>
        public void QueueWireframe(bool showUnselected = true, int cullingSlot = 0)
        {
            bool hasBuffer = _bufferManager?.LineFlagsBuffer != null;
            var lineCulledBuf = _bufferManager?.GetLineCulledBuffer(cullingSlot);
            int slot = (_wireframeMPBs != null && cullingSlot < _wireframeMPBs.Length) ? cullingSlot : 0;

            // 非選択メッシュ → 通常描画
            if (showUnselected && _wireframeMeshUnselected != null && _wireframeMaterial != null
                && _wireframeMeshUnselected.vertexCount > 0)
            {
                MaterialPropertyBlock mpb = null;
                if (hasBuffer)
                {
                    _wireframeMaterial.SetBuffer("_LineFlagsBuffer",   _bufferManager.LineFlagsBuffer);
                    _wireframeMaterial.SetInt("_UseLineFlagsBuffer",    1);

                    // _LineCulledBuffer と _EnableBackfaceCulling は per-slot → MPB でキャプチャ
                    mpb = _wireframeMPBs?[slot];
                    mpb?.SetBuffer("_LineCulledBuffer", lineCulledBuf ?? _bufferManager.LineFlagsBuffer);
                    mpb?.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                }
                else
                {
                    _wireframeMaterial.SetInt("_UseLineFlagsBuffer",    0);
                    _wireframeMaterial.SetInt("_EnableBackfaceCulling", 0);
                }
                _pendingMeshesBySlot[slot].Add(_wireframeMeshUnselected);
                _pendingMaterialsBySlot[slot].Add(_wireframeMaterial);
                _pendingPropertyBlocksBySlot[slot].Add(mpb);
            }

            // 選択メッシュ → オーバーレイ描画
            if (_wireframeMeshSelected != null && _wireframeOverlayMaterial != null
                && _wireframeMeshSelected.vertexCount > 0)
            {
                MaterialPropertyBlock mpb = null;
                if (hasBuffer)
                {
                    _wireframeOverlayMaterial.SetBuffer("_LineFlagsBuffer",   _bufferManager.LineFlagsBuffer);
                    _wireframeOverlayMaterial.SetInt("_UseLineFlagsBuffer",    1);

                    mpb = _wireframeOverlayMPBs?[slot];
                    mpb?.SetBuffer("_LineCulledBuffer", lineCulledBuf ?? _bufferManager.LineFlagsBuffer);
                    mpb?.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                }
                _pendingMeshesBySlot[slot].Add(_wireframeMeshSelected);
                _pendingMaterialsBySlot[slot].Add(_wireframeOverlayMaterial);
                _pendingPropertyBlocksBySlot[slot].Add(mpb);
            }
        }

        /// <summary>
        /// 頂点を描画キューに追加
        /// </summary>
        public void QueuePoints(bool showUnselected = true, int cullingSlot = 0)
        {
            ApplyPointColorSettings(_pointMaterial,        isOverlay: false);
            ApplyPointColorSettings(_pointOverlayMaterial, isOverlay: true);

            bool hasBuffer   = _bufferManager?.VertexFlagsBuffer != null;
            var vtxCulledBuf = _bufferManager?.GetVertexCulledBuffer(cullingSlot);
            int slot = (_pointMPBs != null && cullingSlot < _pointMPBs.Length) ? cullingSlot : 0;

            // 非選択メッシュ → 通常描画
            if (showUnselected && _pointMeshUnselected != null && _pointMaterial != null
                && _pointMeshUnselected.vertexCount > 0)
            {
                MaterialPropertyBlock mpb = null;
                if (hasBuffer)
                {
                    _pointMaterial.SetBuffer("_VertexFlagsBuffer",  _bufferManager.VertexFlagsBuffer);
                    _pointMaterial.SetInt("_UseVertexFlagsBuffer",   1);

                    mpb = _pointMPBs?[slot];
                    mpb?.SetBuffer("_VertexCulledBuffer", vtxCulledBuf ?? _bufferManager.VertexFlagsBuffer);
                    mpb?.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                }
                else
                {
                    _pointMaterial.SetInt("_UseVertexFlagsBuffer",  0);
                    _pointMaterial.SetInt("_EnableBackfaceCulling", 0);
                }
                _pendingMeshesBySlot[slot].Add(_pointMeshUnselected);
                _pendingMaterialsBySlot[slot].Add(_pointMaterial);
                _pendingPropertyBlocksBySlot[slot].Add(mpb);
            }

            // 選択メッシュ → オーバーレイ描画
            if (_pointMeshSelected != null && _pointOverlayMaterial != null
                && _pointMeshSelected.vertexCount > 0)
            {
                MaterialPropertyBlock mpb = null;
                if (hasBuffer)
                {
                    _pointOverlayMaterial.SetBuffer("_VertexFlagsBuffer",  _bufferManager.VertexFlagsBuffer);
                    _pointOverlayMaterial.SetInt("_UseVertexFlagsBuffer",   1);

                    mpb = _pointOverlayMPBs?[slot];
                    mpb?.SetBuffer("_VertexCulledBuffer", vtxCulledBuf ?? _bufferManager.VertexFlagsBuffer);
                    mpb?.SetInt("_EnableBackfaceCulling", BackfaceCullingEnabled ? 1 : 0);
                }
                _pendingMeshesBySlot[slot].Add(_pointMeshSelected);
                _pendingMaterialsBySlot[slot].Add(_pointOverlayMaterial);
                _pendingPropertyBlocksBySlot[slot].Add(mpb);
            }
        }


        /// <summary>
        /// ShaderColorSettingsを頂点マテリアルに適用
        /// </summary>
        private void ApplyPointColorSettings(Material mat, bool isOverlay = false)
        {
            if (mat == null) return;
            
            mat.SetColor("_ColorSelected", _colorSettings.VertexSelected);
            mat.SetColor("_BorderColorSelected", _colorSettings.VertexBorderSelected);
            mat.SetColor("_ColorHovered", _colorSettings.VertexHovered);
            mat.SetColor("_BorderColorHovered", _colorSettings.VertexBorderHovered);
            // オーバーレイ（選択メッシュ）の通常頂点には VertexMeshSelected を使う。
            // 非選択メッシュマテリアルは VertexDefault をそのまま使う。
            mat.SetColor("_ColorDefault",       isOverlay ? _colorSettings.VertexMeshSelected : _colorSettings.VertexDefault);
            mat.SetColor("_BorderColorDefault", _colorSettings.VertexBorderDefault);
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 提出のみを行う ★★★
        /// 指定 slot のキューに入っているメッシュを指定カメラに描画する。
        /// OnRenderObject() 経路から毎フレーム呼ばれる想定。
        /// 計算処理（Mesh 再構築、バッファ更新、Dispatch 等）は一切禁止。
        /// 全ての準備は PrepareDrawing / Queue* 系で完了させておくこと。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public void DrawQueued(Camera camera, int slot)
        {
            if (camera == null) return;
            if (_pendingMeshesBySlot == null || slot < 0 || slot >= _pendingMeshesBySlot.Length) return;

            var meshes    = _pendingMeshesBySlot[slot];
            var materials = _pendingMaterialsBySlot[slot];
            var mpbs      = _pendingPropertyBlocksBySlot[slot];
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh     = meshes[i];
                var material = materials[i];
                var mpb      = i < mpbs.Count ? mpbs[i] : null;
                if (mesh != null && material != null)
                {
                    // MPB が設定されている場合は per-draw-call プロパティを適用
                    // （MaterialPropertyBlock の値は DrawMesh 呼び出し時点でキャプチャされる）
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, camera, 0, mpb);
                }
            }
        }

        /// <summary>
        /// 【重大規約違反: 旧 API】slot 非対応版 DrawQueued。
        /// 呼び出された場合は slot=0 で動作する。Phase 1 で削除予定。
        /// 新規コードは DrawQueued(camera, slot) を使うこと。
        /// </summary>
        [System.Obsolete("Use DrawQueued(camera, slot) instead. slot 0 is assumed here.")]
        public void DrawQueued(Camera camera) => DrawQueued(camera, 0);


        /// <summary>
        /// 指定 slot の描画後クリーンアップ。
        /// Phase 1: 通常は PrepareDrawing で slot を再構築する前にクリアするので、
        /// 呼び出しは Prepare の冒頭から行われる。
        /// </summary>
        public void CleanupQueued(int slot)
        {
            if (_pendingMeshesBySlot == null || slot < 0 || slot >= _pendingMeshesBySlot.Length) return;
            _pendingMeshesBySlot[slot].Clear();
            _pendingMaterialsBySlot[slot].Clear();
            _pendingPropertyBlocksBySlot[slot].Clear();
        }

        /// <summary>
        /// 【重大規約違反: 旧 API】slot 非対応版 CleanupQueued。
        /// 全 slot をクリアする。Phase 1 で削除予定。
        /// </summary>
        [System.Obsolete("Use CleanupQueued(slot) instead.")]
        public void CleanupQueued()
        {
            if (_pendingMeshesBySlot == null) return;
            for (int s = 0; s < _pendingMeshesBySlot.Length; s++)
            {
                _pendingMeshesBySlot[s].Clear();
                _pendingMaterialsBySlot[s].Clear();
                _pendingPropertyBlocksBySlot[s].Clear();
            }
        }

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
                    if (_wireframeMeshSelected != null)
                        UnityEngine.Object.DestroyImmediate(_wireframeMeshSelected);
                    if (_wireframeMeshUnselected != null)
                        UnityEngine.Object.DestroyImmediate(_wireframeMeshUnselected);
                    if (_pointMeshSelected != null)
                        UnityEngine.Object.DestroyImmediate(_pointMeshSelected);
                    if (_pointMeshUnselected != null)
                        UnityEngine.Object.DestroyImmediate(_pointMeshUnselected);

                    // Dispose 時は全 slot をクリアする（旧 API 相当）
#pragma warning disable CS0618
                    CleanupQueued();
#pragma warning restore CS0618
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
