// Editor/Poly_Ling/Core/Rendering/RenderingTypes.cs
// レンダリング関連の共通型定義

namespace Poly_Ling.Rendering
{
    /// <summary>
    /// 可視性情報を提供するインターフェース
    /// </summary>
    public interface IVisibilityProvider
    {
        bool IsVertexVisible(int index);
        bool IsLineVisible(int index);
        bool IsFaceVisible(int index);

        // バッチ取得（パフォーマンス用）
        float[] GetVertexVisibility();
        float[] GetLineVisibility();
        float[] GetFaceVisibility();
    }
}
