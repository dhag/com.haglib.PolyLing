// Assets/Editor/Poly_Ling/Core/Rendering/ShaderColorSettings.cs
// シェーダー用色設定
// 統合シェーダーに渡す色・サイズパラメータを管理

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Core.Rendering
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
        public Color VertexMeshSelected = new Color(0.82f, 0.48f, 0.60f, 0.80f);  // 明るめ小豆色

        [Header("Vertex - State")]
        [Tooltip("ホバー中")]
        public Color VertexHovered = new Color(1f, 0f, 0f, 1f);  // 赤

        [Tooltip("選択された頂点")]
        public Color VertexSelected = new Color(1f, 0.6f, 0f, 1f);  // オレンジ

        [Tooltip("ドラッグ中")]
        public Color VertexDragging = new Color(1f, 0.5f, 0f, 1f);

        [Header("Vertex - Border")]
        public Color VertexBorderDefault = new Color(0.5f, 0.5f, 0.5f, 1f);
        public Color VertexBorderSelected = new Color(1f, 0.6f, 0f, 1f);  // オレンジ
        public Color VertexBorderHovered = new Color(1f, 0f, 0f, 1f);  // 赤

        // ============================================================
        // ライン/エッジ色
        // ============================================================

        [Header("Line - Hierarchy")]
        [Tooltip("非選択モデル/メッシュのエッジ")]
        public Color LineUnselectedMesh = new Color(0.5f, 0.5f, 0.5f, 0.7f);  // グレー

        [Tooltip("選択メッシュの通常エッジ")]
        public Color LineSelectedMesh = new Color(0f, 1f, 0.5f, 0.9f);  // 緑

        [Header("Line - State")]
        [Tooltip("ホバー中")]
        public Color LineHovered = new Color(1f, 0f, 0f, 1f);  // 赤

        [Tooltip("選択エッジ")]
        public Color EdgeSelected = new Color(1f, 0.6f, 0f, 1f);  // オレンジ

        [Header("Line - Special")]
        [Tooltip("補助線（選択メッシュ）")]
        public Color AuxLineSelectedMesh = new Color(1f, 0.3f, 1f, 0.9f);  // マゼンタ

        [Tooltip("補助線（非選択メッシュ）")]
        public Color AuxLineUnselectedMesh = new Color(0.7f, 0.3f, 0.7f, 0.5f);  // 薄いマゼンタ

        // ============================================================
        // 面色
        // ============================================================

        [Header("Face - State")]
        [Tooltip("ホバー面（塗りつぶし）")]
        public Color FaceHoveredFill = new Color(1f, 0f, 0f, 0.2f);  // 赤（薄い）

        [Tooltip("ホバー面（輪郭）")]
        public Color FaceHoveredEdge = new Color(1f, 0f, 0f, 0.8f);  // 赤

        [Tooltip("選択面（塗りつぶし）")]
        public Color FaceSelectedFill = new Color(1f, 0.5f, 0f, 0.15f);  // オレンジ（薄い）

        [Tooltip("選択面（輪郭）")]
        public Color FaceSelectedEdge = new Color(1f, 0.6f, 0f, 0.6f);  // オレンジ

        // ============================================================
        // UI色
        // ============================================================

        [Header("UI")]
        [Tooltip("背景色")]
        public Color Background = new Color(0.15f, 0.15f, 0.18f, 1f);

        [Tooltip("矩形選択（塗りつぶし）")]
        public Color BoxSelectFill = new Color(0.3f, 0.6f, 1f, 0.2f);

        [Tooltip("矩形選択（枠線）")]
        public Color BoxSelectBorder = new Color(0.3f, 0.6f, 1f, 0.8f);

        // ============================================================
        // 軸色
        // ============================================================

        [Header("Axis")]
        public Color AxisX = new Color(1f, 0.3f, 0.3f, 0.7f);
        public Color AxisY = new Color(0.3f, 1f, 0.3f, 0.7f);
        public Color AxisZ = new Color(0.3f, 0.3f, 1f, 0.7f);

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
        public float VertexPointScale = 0.02f;

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
                settings.LineUnselectedMesh = new Color(0.4f, 0.4f, 0.4f, 0.5f);
                settings.Background = new Color(0.1f, 0.1f, 0.12f, 1f);
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
                settings.LineUnselectedMesh = new Color(0.6f, 0.6f, 0.6f, 0.6f);
                settings.Background = new Color(0.8f, 0.8f, 0.82f, 1f);
                return settings;
            }
        }

        // ============================================================
        // ヘルパーメソッド
        // ============================================================

        /// <summary>
        /// アルファ値を適用した色を取得
        /// </summary>
        public Color WithAlpha(Color baseColor, float alpha)
        {
            return new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
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
            mat.SetColor("_ColorUnselectedMesh", LineUnselectedMesh);
            mat.SetColor("_ColorSelectedMesh", LineSelectedMesh);

            // 状態色
            mat.SetColor("_ColorHovered", LineHovered);
            mat.SetColor("_ColorEdgeSelected", EdgeSelected);

            // 特殊色
            mat.SetColor("_ColorAuxLineSelected", AuxLineSelectedMesh);
            mat.SetColor("_ColorAuxLineUnselected", AuxLineUnselectedMesh);

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
