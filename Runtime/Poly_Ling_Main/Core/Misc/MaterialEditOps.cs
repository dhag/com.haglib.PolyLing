// MaterialEditOps.cs
// 材質スロットの編集（色・シェーダー・不透明／半透明・テクスチャ）の共通処理。
// 操作経路統一計画.md H-3a。
//
// 【何のために要るか】
//   これらの処理は材質一覧パネル（PlayerMaterialListSubPanel）の中にだけあり、
//   パネルが Unity の Material と MaterialData を直接書き換えていた。
//   コマンド（ディスパッチャ）から同じ処理を呼べるよう、ここへ移す。
//   パネルはスライダー操作中の見た目の更新にだけ Material 側の関数を使い、
//   確定はコマンドで行う。
//
// 【Material と MaterialData】
//   保存に乗るのは MaterialReference.Data。起きている Material はキャッシュで、
//   Data を書いても作り直されないので、両方を書く。

using System.IO;
using UnityEngine;

namespace Poly_Ling.Materials
{
    /// <summary>実数の材質パラメータの種類。SetMaterialScalarCommand が使う。</summary>
    public enum MaterialScalarKind
    {
        Metallic   = 0,
        Smoothness = 1,
    }

    public static class MaterialEditOps
    {
        // ================================================================
        // Material 側（見た目）
        // ================================================================

        public static Color GetColor(Material mat)
        {
            if (mat == null) return Color.white;
            if (mat.HasProperty("_BaseColor")) return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))     return mat.GetColor("_Color");
            return Color.white;
        }

        public static void SetColor(Material mat, Color color)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     color);
        }

        public static Texture GetMainTexture(Material mat)
        {
            if (mat == null) return null;
            if (mat.HasProperty("_BaseMap"))
            {
                var t = mat.GetTexture("_BaseMap");
                if (t != null) return t;
            }
            if (mat.HasProperty("_MainTex"))
            {
                var t = mat.GetTexture("_MainTex");
                if (t != null) return t;
            }
            return null;
        }

        public static void SetMainTexture(Material mat, Texture tex)
        {
            if (mat == null) return;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        public static bool IsTransparent(Material mat)
        {
            if (mat == null) return false;
            if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f) return true;
            if (mat.HasProperty("_Mode")    && mat.GetFloat("_Mode")    > 1.5f) return true;
            return false;
        }

        /// <summary>Opaque を材質へ書く（Data は触らない）。</summary>
        public static void WriteSurfaceOpaque(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0);
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 0);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 1);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
        }

        /// <summary>Transparent を材質へ書く（Data は触らない）。</summary>
        public static void WriteSurfaceTransparent(Material mat)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0);
            if (mat.HasProperty("_Mode"))    mat.SetFloat("_Mode", 3);
            if (mat.HasProperty("_ZWrite"))  mat.SetFloat("_ZWrite", 0);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
        }

        // ================================================================
        // Data 側（保存）
        // ================================================================

        /// <summary>Data が無ければ作って返す（保存対象は MaterialReference.Data 側）。</summary>
        public static MaterialData EnsureData(MaterialReference matRef)
        {
            if (matRef == null) return null;
            if (matRef.Data == null) matRef.Data = new MaterialData();
            return matRef.Data;
        }

        /// <summary>
        /// 表面種別を Data 側にも反映する。WriteSurface* が材質へ書いた内容と一致させる。
        /// AlphaClip は両方とも _AlphaClip=0 を書くので false 固定。
        /// _Blend は Transparent 側だけが 0（Alpha）を書くので、Opaque では触らない。
        /// </summary>
        public static void SyncSurfaceToData(MaterialData d, bool transparent)
        {
            if (d == null) return;
            d.Surface          = transparent ? SurfaceType.Transparent : SurfaceType.Opaque;
            d.AlphaClipEnabled = false;
            if (transparent) d.BlendMode = BlendModeType.Alpha;
        }

        // ================================================================
        // 確定操作（Material と Data の両方）。コマンドの受け口が呼ぶ。
        // ================================================================

        /// <summary>色を設定する。</summary>
        public static void ApplyColor(MaterialReference matRef, Color color)
        {
            if (matRef == null) return;
            EnsureData(matRef).SetBaseColor(color);
            SetColor(matRef.Material, color);
        }

        /// <summary>
        /// 実数の材質パラメータ（Metallic・Smoothness）を材質へ書く（Data は触らない）。
        /// Smoothness はシェーダーにより _Smoothness か _Glossiness。
        /// </summary>
        public static void SetScalar(Material mat, MaterialScalarKind kind, float value)
        {
            if (mat == null) return;
            switch (kind)
            {
                case MaterialScalarKind.Metallic:
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", value);
                    break;
                case MaterialScalarKind.Smoothness:
                    if (mat.HasProperty("_Smoothness"))      mat.SetFloat("_Smoothness", value);
                    else if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", value);
                    break;
            }
        }

        /// <summary>実数の材質パラメータ（Metallic・Smoothness）を設定する。</summary>
        public static void ApplyScalar(MaterialReference matRef, MaterialScalarKind kind, float value)
        {
            if (matRef == null) return;
            var d = EnsureData(matRef);
            if (kind == MaterialScalarKind.Metallic) d.Metallic   = value;
            else                                     d.Smoothness = value;
            SetScalar(matRef.Material, kind, value);
        }

        /// <summary>不透明／半透明を設定する。</summary>
        public static void ApplySurface(MaterialReference matRef, bool transparent)
        {
            if (matRef == null) return;
            var mat = matRef.Material;
            if (transparent) WriteSurfaceTransparent(mat);
            else             WriteSurfaceOpaque(mat);
            SyncSurfaceToData(EnsureData(matRef), transparent);
        }

        /// <summary>
        /// シェーダーを差し替える。色・メインテクスチャ・不透明／半透明を引き継ぐ。
        ///
        /// キャッシュ材質の shader を直接差し替える。MaterialDataConverter の
        /// FromMaterial / ToMaterial 経由で作り直さないのは、Player では
        /// PLEditorBridge が EditorBridgeNull で LoadAssetAtPath / GetAssetPath が null を返し、
        /// テクスチャを落とすため。InvalidateCache も呼ばない（旧材質を解放しない）。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        public static string ApplyShader(MaterialReference matRef, ShaderType type, string customName)
        {
            if (matRef == null) return "材質スロットがありません";
            var mat = matRef.Material;
            if (mat == null) return "マテリアルが割り当てられていません";

            Shader shader = (type == ShaderType.Custom)
                ? (string.IsNullOrEmpty(customName) ? null : Shader.Find(customName))
                : MaterialDataConverter.GetShader(type);

            if (shader == null)
                return type == ShaderType.Custom
                    ? $"シェーダーが見つかりません: {customName}"
                    : $"シェーダーが見つかりません: {type}（ビルドに含まれていない）";

            // 切替前の状態を Unity オブジェクトのまま退避する（パス経由にしない）。
            Color   keepColor      = GetColor(mat);
            Texture keepTex        = GetMainTexture(mat);
            bool    wasTransparent = IsTransparent(mat);

            mat.shader = shader;

            SetColor(mat, keepColor);
            SetMainTexture(mat, keepTex);

            // 新シェーダーのブレンド状態を確定させる。キーワードと renderQueue だけでは
            // URP の半透明は成立しない（MaterialDataConverter.ApplySurfaceSettings）。
            if (wasTransparent) WriteSurfaceTransparent(mat);
            else                WriteSurfaceOpaque(mat);

            var d = EnsureData(matRef);
            d.ShaderType = type;
            d.ShaderName = shader.name;
            d.SetBaseColor(keepColor);
            SyncSurfaceToData(d, wasTransparent);
            return null;
        }

        /// <summary>
        /// 画像ファイルを読み、材質のテクスチャ欄へ設定する。
        /// 外部ファイルから読んだテクスチャなので、Data には SourceTexturePath（絶対パス欄）を書き、
        /// BaseMapPath（AssetDatabase パス欄）は無効にする。保存時に CopyTextureFile が
        /// textures フォルダへ複製し、読込時に ApplyTextureFromFolder がファイル名で復元する。
        /// </summary>
        /// <param name="fullPath">解決済みの絶対パス（サンドボックスの判定は呼び出し側）</param>
        /// <returns>失敗理由。成功時は null。</returns>
        public static string ApplyTextureFile(MaterialReference matRef, string propName, string fullPath, out Texture2D loaded)
        {
            loaded = null;
            if (matRef == null) return "材質スロットがありません";
            var mat = matRef.Material;
            if (mat == null) return "マテリアルが割り当てられていません";
            if (string.IsNullOrEmpty(propName) || !mat.HasProperty(propName))
                return $"テクスチャ欄がありません: {propName}";
            if (!File.Exists(fullPath)) return $"ファイルがありません: {fullPath}";

            var tex = new Texture2D(2, 2);
            if (!tex.LoadImage(File.ReadAllBytes(fullPath)))
            {
                Object.DestroyImmediate(tex);
                return "テクスチャの読み込みに失敗しました";
            }
            tex.name = Path.GetFileNameWithoutExtension(fullPath);
            mat.SetTexture(propName, tex);

            var d = EnsureData(matRef);
            d.SourceTexturePath = fullPath;
            d.BaseMapPath       = null;
            loaded = tex;
            return null;
        }
    }
}
