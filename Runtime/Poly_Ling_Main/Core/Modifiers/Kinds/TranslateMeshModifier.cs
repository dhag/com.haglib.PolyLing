// TranslateMeshModifier.cs
// 処理列の種類「平行移動」。範囲内の頂点を Offset だけ動かす。
// 枠組みの核（段 1）の発見・引数の往復を確かめる最小の種類。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置

using Poly_Ling.Data;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("translate", DisplayName = "平行移動", Category = "deform",
                    Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を、オブジェクトのローカル座標で offset だけ動かす")]
    public sealed class TranslateMeshModifier : IMeshModifier, IMeshModifierPositions
    {
        /// <summary>移動量の各成分の下限・上限（引数欄のスライダーの範囲）。</summary>
        public const float OffsetMin = -10f;
        public const float OffsetMax =  10f;

        [PLParam(Description = "移動量（ローカル座標）", Min = OffsetMin, Max = OffsetMax, Step = 0.01)]
        public Vector3 Offset { get; set; } = Vector3.zero;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            if (input?.Source == null) { error = "入力のメッシュが無い"; return null; }

            var dst   = input.Source.Clone();
            var scope = input.Scope ?? ResolvedMeshModifierScope.CreateAll();
            for (int i = 0; i < dst.Vertices.Count; i++)
                if (scope.ContainsVertex(i)) dst.Vertices[i].Position += Offset;
            return dst;
        }

        public bool EvaluatePositions(MeshModifierInput input, Vector3[] src, Vector3[] dst, out string error)
        {
            error = null;
            if (src == null || dst == null || src.Length != dst.Length) { error = "位置の配列の長さが合わない"; return false; }

            var scope = input?.Scope ?? ResolvedMeshModifierScope.CreateAll();
            for (int i = 0; i < src.Length; i++)
                dst[i] = scope.ContainsVertex(i) ? src[i] + Offset : src[i];
            return true;
        }
    }
}
