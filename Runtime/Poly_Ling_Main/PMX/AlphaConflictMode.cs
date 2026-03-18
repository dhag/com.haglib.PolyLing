// Runtime/Poly_Ling_Main/PMX/AlphaConflictMode.cs
// Editor/Poly_Ling_Main/PMX/PMXImportSettings.cs から分離

namespace Poly_Ling.PMX
{
    /// <summary>
    /// 材質不透明度(Diffuse.a &lt; 1.0)とテクスチャが両方ある場合の動作
    /// PMX/MQO共通で使用
    /// </summary>
    public enum AlphaConflictMode
    {
        /// <summary>Transparent（材質不透明度を優先、半透明ブレンディング）</summary>
        PreferTransparent,
        /// <summary>AlphaClip（テクスチャアルファを優先、アルファテスト）</summary>
        PreferAlphaClip,
    }
}
