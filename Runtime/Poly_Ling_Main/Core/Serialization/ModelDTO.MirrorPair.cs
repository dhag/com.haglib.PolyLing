// ModelDTO.MirrorPair.cs
// DTO：ミラーペア。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置（ModelDTO.cs と同じ名前空間。ModelDTO.cs から分割）
// 段 7 でモーフ基準データ（MorphBaseDataDTO。.mfproj の meshContexts[].morphBaseData）を廃止し、
// 残ったミラーペアだけをここへ移した（PolyLing_モーフ整理計画.md 3.10）。

using System;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// ミラーペア情報のシリアライズ用構造
    /// Real側とMirror側のメッシュインデックスペアを保持
    /// </summary>
    [Serializable]
    public class MirrorPairDTO
    {
        /// <summary>Real側メッシュインデックス（MeshContextList内）</summary>
        public int realIndex;

        /// <summary>Mirror側メッシュインデックス（MeshContextList内）</summary>
        public int mirrorIndex;

        /// <summary>ミラー軸（0=X, 1=Y, 2=Z）</summary>
        public int axis = 0;
    }
}
