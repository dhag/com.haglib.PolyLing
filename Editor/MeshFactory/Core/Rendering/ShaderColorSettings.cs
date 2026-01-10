// Assets/Editor/MeshFactory/Core/Rendering/ShaderColorSettings.cs
// シェーダー用色設定
// 統合シェーダーに渡す色・サイズパラメータを管理

using System;
using UnityEngine;

namespace MeshFactory.Core.Rendering
{
    /// <summary>
    /// シェーダー用色設定
    /// </summary>
    [Serializable]
    public class ShaderColorSettings
    {
        // ============================================================
        // 頂点色
        // ============================================================

        [Header("Vertex - Hierarchy")]
        [Tooltip("非選択モデルの頂点")]
        public Color VertexInactiveModel = new Color(0.5f, 0.5f, 0.5f, 0.3f);

        [Tooltip("デフォルト（選択モデル内、非選択メッシュ）")]
        public Color VertexDefault = new Color(0.8f, 0.8f, 0.8f, 0.5f);

        [Tooltip("アクティブメッシュの頂点")]
        public Color VertexActive = new Color(1f, 1f, 1f, 0.6f);

        [Tooltip("選択メッシュの頂点")]
        public Color VertexMeshSelected = new Color(1f, 1f, 1f, 0.7f);

        [Header("Vertex - State")]
        [Tooltip("ホバー中")]
        public Color VertexHovered = new Color(0f, 1f, 1f, 1f);

        [Tooltip("選択された頂点")]
        public Color VertexSelected = new Color(1f, 0.8f, 0f, 1f);

        [Tooltip("ドラッグ中")]
        public Color VertexDragging = new Color(1f, 0.5f, 0f, 1f);

        [Header("Vertex - Border")]
        public Color VertexBorderDefault = new Color(0.5f, 0.5f, 0.5f, 1f);
        public Color VertexBorderSelected = new Color(1f, 0f, 0f, 1f);
        public Color VertexBorderHovered = new Color(0f, 0.8f, 0.8f, 1f);

        // ============================================================
        // ライン/エッジ色
        // ============================================================

        [Header("Line - Hierarchy")]
        [Tooltip("非選択モデルのエッジ")]
        public Color LineInactiveModel = new Color(0.3f, 0.3f, 0.3f, 0.3f);

        [Tooltip("デフォルトエッジ")]
        public Color LineDefault = new Color(0f, 0.5f, 0.3f, 0.6f);

        [Tooltip("アクティブメッシュのエッジ")]
        public Color LineActive = new Color(0f, 0.7f, 0.4f, 0.8f);

        [Tooltip("選択メッシュのエッジ")]
        public Color LineMeshSelected = new Color(0f, 0.8f, 0.5f, 0.9f);

        [Header("Line - State")]
        [Tooltip("ホバー中")]
        public Color LineHovered = new Color(0f, 1f, 1f, 1f);

        [Tooltip("選択エッジ")]
        public Color EdgeSelected = new Color(1f, 0.5f, 0f, 1f);

        [Tooltip("ドラッグ中")]
        public Color LineDragging = new Color(1f, 0.3f, 0f, 1f);

        [Header("Line - Special")]
        [Tooltip("補助線")]
        public Color AuxLine = new Color(0.8f, 0.6f, 0f, 0.5f);

        [Tooltip("補助線（選択）")]
        public Color AuxLineSelected = new Color(1f, 0.8f, 0f, 1f);

        [Tooltip("境界エッジ")]
        public Color BoundaryEdge = new Color(0f, 1f, 0.5f, 1f);

        // ============================================================
        // 面色
        // ============================================================

        [Header("Face")]
        [Tooltip("選択面")]
        public Color FaceSelected = new Color(1f, 0.5f, 0f, 0.3f);

        [Tooltip("ホバー面")]
        public Color FaceHovered = new Color(0f, 1f, 1f, 0.2f);

        // ============================================================
        // ミラー
        // ============================================================

        [Header("Mirror")]
        [Range(0f, 1f)]
        public float MirrorAlpha = 0.4f;

        // ============================================================
        // サイズ
        // ============================================================

        [Header("Sizes")]
        public float VertexSizeDefault = 5f;
        public float VertexSizeActive = 6f;
        public float VertexSizeSelected = 8f;
        public float VertexSizeHovered = 7f;

        public float LineSizeDefault = 1f;
        public float LineSizeActive = 1.5f;
        public float LineSizeSelected = 2f;
        public float LineSizeHovered = 2f;

        public float BorderWidth = 0.15f;

        // ============================================================
        // プリセット
        // ============================================================

        /// <summary>
        /// デフォルト設定
        /// </summary>
        public static ShaderColorSettings Default => new ShaderColorSettings();

        /// <summary>
        /// ダークテーマ
        /// </summary>
        public static ShaderColorSettings Dark
        {
            get
            {
                var settings = new ShaderColorSettings();
                settings.VertexInactiveModel = new Color(0.4f, 0.4f, 0.4f, 0.3f);
                settings.VertexDefault = new Color(0.6f, 0.6f, 0.6f, 0.5f);
                settings.LineInactiveModel = new Color(0.2f, 0.2f, 0.2f, 0.3f);
                settings.LineDefault = new Color(0.3f, 0.5f, 0.4f, 0.6f);
                return settings;
            }
        }

        /// <summary>
        /// ライトテーマ
        /// </summary>
        public static ShaderColorSettings Light
        {
            get
            {
                var settings = new ShaderColorSettings();
                settings.VertexInactiveModel = new Color(0.6f, 0.6f, 0.6f, 0.4f);
                settings.VertexDefault = new Color(0.9f, 0.9f, 0.9f, 0.6f);
                settings.LineInactiveModel = new Color(0.4f, 0.4f, 0.4f, 0.4f);
                settings.LineDefault = new Color(0.2f, 0.6f, 0.4f, 0.7f);
                return settings;
            }
        }

        // ============================================================
        // マテリアル設定
        // ============================================================

        /// <summary>
        /// 頂点シェーダーマテリアルに設定を適用
        /// </summary>
        public void ApplyToPointMaterial(Material mat)
        {
            if (mat == null) return;

            // 階層色
            mat.SetColor("_ColorInactiveModel", VertexInactiveModel);
            mat.SetColor("_ColorDefault", VertexDefault);
            mat.SetColor("_ColorActive", VertexActive);
            mat.SetColor("_ColorMeshSelected", VertexMeshSelected);

            // 状態色
            mat.SetColor("_ColorHovered", VertexHovered);
            mat.SetColor("_ColorSelected", VertexSelected);
            mat.SetColor("_ColorDragging", VertexDragging);

            // ボーダー色
            mat.SetColor("_BorderColorDefault", VertexBorderDefault);
            mat.SetColor("_BorderColorSelected", VertexBorderSelected);
            mat.SetColor("_BorderColorHovered", VertexBorderHovered);

            // サイズ
            mat.SetFloat("_SizeDefault", VertexSizeDefault);
            mat.SetFloat("_SizeActive", VertexSizeActive);
            mat.SetFloat("_SizeSelected", VertexSizeSelected);
            mat.SetFloat("_SizeHovered", VertexSizeHovered);

            mat.SetFloat("_BorderWidth", BorderWidth);
            mat.SetFloat("_MirrorAlpha", MirrorAlpha);
        }

        /// <summary>
        /// ラインシェーダーマテリアルに設定を適用
        /// </summary>
        public void ApplyToLineMaterial(Material mat)
        {
            if (mat == null) return;

            // 階層色
            mat.SetColor("_ColorInactiveModel", LineInactiveModel);
            mat.SetColor("_ColorDefault", LineDefault);
            mat.SetColor("_ColorActive", LineActive);
            mat.SetColor("_ColorMeshSelected", LineMeshSelected);

            // 状態色
            mat.SetColor("_ColorHovered", LineHovered);
            mat.SetColor("_ColorEdgeSelected", EdgeSelected);
            mat.SetColor("_ColorDragging", LineDragging);

            // 特殊色
            mat.SetColor("_ColorAuxLine", AuxLine);
            mat.SetColor("_ColorAuxLineSelected", AuxLineSelected);
            mat.SetColor("_ColorBoundary", BoundaryEdge);

            mat.SetFloat("_MirrorAlpha", MirrorAlpha);
        }

        /// <summary>
        /// コンピュートシェーダーに設定を適用（将来用）
        /// </summary>
        public void ApplyToComputeShader(ComputeShader cs, int kernelIndex)
        {
            if (cs == null) return;

            // 必要に応じて実装
        }
    }
}
