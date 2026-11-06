// SubdivisionMeshModifier.cs
// 処理列の種類「サブディビジョン」（面構成を変える）。入力を Catmull-Clark で細分化する。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.6・段 8
//
// 【実処理】SubdivisionOps.TryBuild（subdivideMesh コマンド・SubdivisionSync と同じもの）を 1 回呼ぶ。
//   線分（3 頂点未満の面）と、対象の面に使われない頂点は出力に残らない（TryBuild の規則）。
//
// 【範囲】全体だけ。全体以外は失敗。
//
// 【ID】子の先頭の頂点の点に、親の頂点の ID を写す（TryBuild の vertexPointSources）。
//   辺の点・面の点と面はすべて 0。下流の範囲は親の頂点 ID で角の頂点に当たる。
//
// 【ドラッグ中の追随】IMeshModifierDragFollow。最後の Evaluate のステンシル（SubdivisionStencil）を控え、
//   入力の位置にそれを掛けて出力の位置を求める（SubdivisionSync の SyncDrag と同じ）。
//   法線は直さない。離したときに枠組みが Evaluate で作り直す。
//
// 【既存の subdivideMesh スタック】そのまま動く（SubdivisionSync）。処理列パネルでは、この種類の
//   宣言（表示名・引数 level）で同じ見た目に出す（PlayerModifierChainSubPanel）。

using Poly_Ling.Data;
using Poly_Ling.Ops;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("subdivision", DisplayName = "サブディビジョン", Category = "topology", Change = MeshModifierChange.Topology,
                    WholeMeshOnly = true,
                    Description = "入力を Catmull-Clark で細分化する（SubdivisionOps.TryBuild）。範囲は全体だけ。ドラッグ中はステンシルで追随する")]
    public sealed class SubdivisionMeshModifier : IMeshModifier, IMeshModifierDragFollow
    {
        [PLParam(Description = "細分化の回数", Min = SubdivisionOps.LevelMin, Max = SubdivisionOps.LevelMax, Step = 1)]
        public int Level { get; set; } = 1;

        /// <summary>最後の Evaluate のステンシル（ドラッグ中の追随用。引数ではない）。</summary>
        private SubdivisionStencil _stencil;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            _stencil = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            if (input.Scope != null && !input.Scope.IsAll)
            { error = "サブディビジョンの範囲は全体だけ"; return null; }

            int level = Mathf.Clamp(Level, SubdivisionOps.LevelMin, SubdivisionOps.LevelMax);
            if (!SubdivisionOps.TryBuild(src, level, out var result, out var stencil, out int[] vps, out error))
                return null;

            for (int i = 0; i < vps.Length && i < result.Vertices.Count; i++)
            {
                int s = vps[i];
                if (s >= 0 && s < src.Vertices.Count && src.Vertices[s] != null) result.Vertices[i].Id = src.Vertices[s].Id;
            }
            foreach (var f in result.Faces) if (f != null) f.Id = 0;
            result.RebuildIdSets();

            _stencil = stencil;
            return result;
        }

        public bool FollowPositions(Vector3[] src, out Vector3[] dst)
        {
            dst = null;
            if (_stencil == null || src == null || src.Length != _stencil.InputCount) return false;
            dst = _stencil.Apply(src);
            return dst != null;
        }
    }
}
