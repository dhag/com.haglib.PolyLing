// Editor/MeshFactory/Core/Rendering/RenderingTypes.cs
// レンダリング関連の共通型定義

namespace MeshFactory.Rendering
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

    /// <summary>
    /// ヒットテスト結果
    /// </summary>
    public struct GPUHitTestResult
    {
        public int NearestVertexIndex;
        public float NearestVertexDistance;
        public float NearestVertexDepth;

        public int NearestLineIndex;
        public float NearestLineDistance;
        public float NearestLineDepth;

        public int[] HitFaceIndices;
        public float[] HitFaceDepths;

        public bool HasVertexHit(float radius) => NearestVertexIndex >= 0 && NearestVertexDistance < radius;
        public bool HasLineHit(float distance) => NearestLineIndex >= 0 && NearestLineDistance < distance;
        public bool HasFaceHit => HitFaceIndices != null && HitFaceIndices.Length > 0;

        /// <summary>
        /// 最も手前（深度が小さい）の面インデックスを取得
        /// </summary>
        public int GetNearestFaceIndex()
        {
            if (HitFaceIndices == null || HitFaceIndices.Length == 0)
                return -1;

            if (HitFaceDepths == null || HitFaceDepths.Length != HitFaceIndices.Length)
                return HitFaceIndices[0];

            int nearestIndex = 0;
            float nearestDepth = HitFaceDepths[0];

            for (int i = 1; i < HitFaceDepths.Length; i++)
            {
                if (HitFaceDepths[i] < nearestDepth)
                {
                    nearestDepth = HitFaceDepths[i];
                    nearestIndex = i;
                }
            }

            return HitFaceIndices[nearestIndex];
        }
    }
}
