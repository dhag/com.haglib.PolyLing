// MirrorDisplayMeshModifier.cs
// 処理列の種類「ミラー（表示）」（末尾固定・並べ替え不可）。結果オブジェクトの表示ミラーを持つ。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.6・段 8
//
// 【何をするか】メッシュは変えない（恒等。位置だけの種類として流れる）。この項目が有効なあいだ、
//   処理列の結果の MirrorType・MirrorAxis・MirrorDistance をこの引数にし、ミラー側のオブジェクト
//   （結果から派生する別の描画オブジェクト。MirrorType を使う読込・書き出し・VRM と同じ持ち方）を置く。
//   書き換えるのは処理列の受け口（PlayerCommandDispatcher.ModifierChain.cs の SyncChainDisplayMirror）。
//   結果を作り直したときのミラー側の追随は処理器（ModifierChainHandler）が受け持つ。
//
// 【末尾固定】PinnedLast。ミラー側は列の最後の形（結果）から作るので、列の最後に 1 つだけ置ける。
//
// 【引数の範囲】distance ±10 はこの段で決めた。

using Poly_Ling.Data;
using Poly_Ling.Symmetry;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    /// <summary>表示ミラーの種別（MeshContext.MirrorType の 1・2）。</summary>
    public enum MirrorDisplayType
    {
        /// <summary>分離（MirrorType 1）。</summary>
        Separate = 1,
        /// <summary>結合（MirrorType 2）。</summary>
        Merge = 2,
    }

    [PLMeshModifier("mirrorDisplay", DisplayName = "ミラー（表示）", Category = "mirror", Change = MeshModifierChange.PositionsOnly,
                    PinnedLast = true,
                    Description = "結果オブジェクトに表示ミラーを付ける（ミラー側は結果から派生する別オブジェクト）。メッシュは変えない。列の最後に 1 つだけ置け、並べ替えできない")]
    public sealed class MirrorDisplayMeshModifier : IMeshModifier, IMeshModifierPositions
    {
        public const float DistanceMax = 10f;

        [PLParam(Description = "鏡映の軸（X なら YZ 平面で鏡映）")]
        public SymmetryAxis Axis { get; set; } = SymmetryAxis.X;

        [PLParam(Description = "鏡映の平面の距離（ローカル座標）", Min = -DistanceMax, Max = DistanceMax, Step = 0.001)]
        public float Distance { get; set; } = 0f;

        [PLParam(Description = "分離 / 結合（MQO の mirror 属性に書く種別）")]
        public MirrorDisplayType Type { get; set; } = MirrorDisplayType.Separate;

        /// <summary>MeshContext.MirrorAxis の値（1:X, 2:Y, 4:Z）。プロパティにすると引数として拾われるのでメソッドにする。</summary>
        public int GetMirrorAxisValue() => Axis == SymmetryAxis.Y ? 2 : Axis == SymmetryAxis.Z ? 4 : 1;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            if (input?.Source == null) { error = "入力のメッシュが無い"; return null; }
            return input.Source.Clone();
        }

        public bool EvaluatePositions(MeshModifierInput input, Vector3[] src, Vector3[] dst, out string error)
        {
            error = null;
            return true;   // dst は呼ぶ側が src の写しを入れて渡す（恒等）
        }
    }
}
