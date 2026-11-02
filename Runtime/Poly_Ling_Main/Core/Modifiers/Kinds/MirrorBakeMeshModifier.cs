// MirrorBakeMeshModifier.cs
// 処理列の種類「ミラー（実体化）」（面構成を変える）。入力を平面で鏡映した半身を同じメッシュの中に足す。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.6・段 8
//
// 【実処理】MirrorBaker.BakeMirror（bakeMirror コマンドと同じもの）を 1 回呼ぶ。境界はしきい値で決める
//   （平面からの距離が threshold 未満の頂点を元とミラーで 1 つにする）。列のどこにでも置ける。
//   「ミラー（表示）」（mirrorDisplay）とは別物：こちらは結果のメッシュそのものに頂点と面が増える。
//
// 【範囲】全体だけ。全体以外は失敗。
//
// 【ID】BakeMirror は頂点 ID を写さない（MirrorBaker.cs の注記）。ここで両側の頂点に元の頂点の ID を写す
//   （NewToOriginalIndex。配列と同じ「写しは元と同じ ID」の規則）。面は 0。
//   部品 ID・サブ ID は BakeMirror が振ったまま（元側 0・ミラー側 1）。
//
// 【ドラッグ中の追随】IMeshModifierDragFollow。最後の Evaluate のベイク結果（頂点の出自）を控え、
//   MirrorBaker.MapPositions で位置を写す。境界にあった頂点を平面から離すと面構成（統合の組）が変わるが、
//   ドラッグ中は最後の組のまま写し、離したときに枠組みが Evaluate で作り直す。
//
// 【引数の範囲】planeOffset ±10、threshold 0.00001〜0.1 はこの段で決めた。
//   既存の LimitKey "Mirror.Threshold" は下限しか無く（ParameterLimits.cs）引数欄がスライダーを作れないので使わない。

using Poly_Ling.Data;
using Poly_Ling.Symmetry;
using Poly_Ling.Tools;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("mirrorBake", DisplayName = "ミラー（実体化）", Category = "topology", Change = MeshModifierChange.Topology,
                    Description = "入力を平面で鏡映した半身を同じメッシュに足す（MirrorBaker.BakeMirror）。境界の頂点は 1 つにまとめる。範囲は全体だけ")]
    public sealed class MirrorBakeMeshModifier : IMeshModifier, IMeshModifierDragFollow
    {
        public const float PlaneOffsetMax = 10f;
        public const float ThresholdMin   = 0.00001f;
        public const float ThresholdMax   = 0.1f;

        [PLParam(Description = "鏡映の軸（X なら YZ 平面で鏡映）")]
        public SymmetryAxis Axis { get; set; } = SymmetryAxis.X;

        [PLParam(Description = "鏡映の平面の位置（ローカル座標。軸の向きの距離）", Min = -PlaneOffsetMax, Max = PlaneOffsetMax, Step = 0.001)]
        public float PlaneOffset { get; set; } = 0f;

        [PLParam(Description = "平面からの距離がこの値未満の頂点を境界とみなし、元とミラーを 1 つにする",
                 Min = ThresholdMin, Max = ThresholdMax, Step = 0.00001)]
        public float Threshold { get; set; } = 0.0001f;

        [PLParam(Description = "ミラー側の U 座標を反転する")]
        public bool FlipU { get; set; } = false;

        [PLParam(Description = "境界の頂点を平面へ寄せる")]
        public bool ProjectBoundaryToPlane { get; set; } = true;

        /// <summary>最後の Evaluate のベイク結果（ドラッグ中の追随用。引数ではない）。</summary>
        private MirrorBakeResult _bake;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            _bake = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            if (input.Scope != null && !input.Scope.IsAll)
            { error = "ミラー（実体化）の範囲は全体だけ"; return null; }
            if (src.VertexCount == 0) { error = "入力に頂点が無い"; return null; }

            int axis = Axis == SymmetryAxis.Y ? 1 : Axis == SymmetryAxis.Z ? 2 : 0;
            var (baked, result) = MirrorBaker.BakeMirror(src, axis, PlaneOffset, Threshold, FlipU, null, ProjectBoundaryToPlane);
            if (baked == null || result == null) { error = "ミラーの実体化が失敗した"; return null; }

            var map = result.NewToOriginalIndex;
            for (int i = 0; i < baked.Vertices.Count && map != null && i < map.Length; i++)
            {
                int s = map[i];
                if (s >= 0 && s < src.Vertices.Count && src.Vertices[s] != null) baked.Vertices[i].Id = src.Vertices[s].Id;
            }
            foreach (var f in baked.Faces) if (f != null) f.Id = 0;
            baked.Name = src.Name;
            baked.RebuildIdSets();

            _bake = result;
            return baked;
        }

        public bool FollowPositions(Vector3[] src, out Vector3[] dst)
        {
            dst = MirrorBaker.MapPositions(_bake, src);
            return dst != null;
        }
    }
}
