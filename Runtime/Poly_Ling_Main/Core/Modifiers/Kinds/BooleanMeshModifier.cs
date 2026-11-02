// BooleanMeshModifier.cs
// 処理列の種類「ブーリアン」（面構成を変える）。入力と参照オブジェクト（相手）に和・差・積を掛ける。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7b
//
// 【実処理】BooleanOps.Perform（純粋。booleanMesh の受け口と同じもの）を 1 回呼ぶ。
//   演算は元メッシュのローカルで行う：入力はそのまま、相手は MeshModifierInput.ReferenceToSource
//   （相手のローカル → 元メッシュのローカル）を掛けて読む。相手や元メッシュのオブジェクトを動かすと
//   この行列が変わり、枠組みが流し直す。
//
// 【範囲】全体だけ。CSG は閉じた立体の内外で決まるので、面の組だけを相手にはできない。全体以外は失敗。
//
// 【ID】結果の頂点で、入力の頂点と同じ位置（結合距離以内。下限 1e-6）のものには入力の頂点の ID を写す
//   （下流の範囲が入力の頂点 ID で当たるように）。それ以外の頂点と面はすべて 0。
//   BooleanOps.Perform は ID を付けない（BooleanOps.cs の注記）。
//
// 【引数の範囲】mergeThreshold は 0〜0.1、epsilon は 0〜0.01 をこの段で決めた。
//   既存の LimitKey "Boolean.MergeThreshold" は下限しか無く（ParameterLimits.cs）引数欄がスライダーを作れないので使わない。
//   epsilon が 0 以下なら BooleanOps.DefaultEpsilon（BooleanMeshCommand の説明と同じ扱い）。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("boolean", DisplayName = "ブーリアン", Category = "topology", Change = MeshModifierChange.Topology,
                    Description = "入力と相手のオブジェクトに和・差・積を掛ける（BooleanOps.Perform）。範囲は全体だけ。相手を動かすと結果が追随する")]
    public sealed class BooleanMeshModifier : IMeshModifier
    {
        public const float MergeThresholdMax = 0.1f;
        public const float EpsilonMax        = 0.01f;

        // Required にしない：処理列パネルの「追加」は引数なしで足し、相手は引数欄で選ぶ。
        // 相手が無いうちは評価が失敗し、理由が項目の状態に出る。
        [PLParam(Description = "相手のオブジェクト。差では削る側", IsMeshRef = true)]
        public int Other { get; set; } = -1;

        [PLParam(Description = "和 / 差 / 積")]
        public BooleanOpKind Op { get; set; } = BooleanOpKind.Subtract;

        [PLParam(Description = "演算後に同じ位置の頂点を結合し、T 字解消と辺の接続検証を行う")]
        public bool MergeVertices { get; set; } = true;

        [PLParam(Description = "同じ位置とみなす距離の上限。接続が壊れるときは小さくして試し直す。入力の頂点 ID を写す距離にも使う",
                 Min = 0.0, Max = MergeThresholdMax, Step = 0.00001)]
        public float MergeThreshold { get; set; } = BooleanOps.DefaultMergeThreshold;

        [PLParam(Description = "平面の同一判定の許容量。0 以下は既定値", Min = 0.0, Max = EpsilonMax, Step = 0.00001)]
        public float Epsilon { get; set; } = BooleanOps.DefaultEpsilon;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            var a = input?.Source;
            if (a == null) { error = "入力のメッシュが無い"; return null; }
            if (input.Scope != null && !input.Scope.IsAll)
            { error = "ブーリアンの範囲は全体だけ（閉じた立体どうしで演算する）"; return null; }

            const string key = "other";
            if (input.References == null || !input.References.TryGetValue(key, out var b) || b == null)
            { error = "相手のオブジェクト（other）が指定されていない"; return null; }
            Matrix4x4 bToA = Matrix4x4.identity;
            if (input.ReferenceToSource != null && input.ReferenceToSource.TryGetValue(key, out var m)) bToA = m;

            float eps = Epsilon > 0f ? Epsilon : BooleanOps.DefaultEpsilon;
            var r = BooleanOps.Perform(Op, a, Matrix4x4.identity, b, bToA, Matrix4x4.identity,
                                       eps, MergeVertices, MergeThreshold, a.Name);
            if (!r.Success || r.Mesh == null) { error = r.Message ?? "ブーリアンが失敗した"; return null; }

            CopyIds(a, r.Mesh, Mathf.Max(r.ActualMergeThreshold, 1e-6f));
            return r.Mesh;
        }

        /// <summary>結果の頂点のうち、入力の頂点から dist 以内のものに一番近い入力の頂点の ID を写す。他と面は 0。</summary>
        private static void CopyIds(MeshObject src, MeshObject dst, float dist)
        {
            float cell = dist * 2f;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Key(Vector3 p) => (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

            for (int i = 0; i < src.Vertices.Count; i++)
            {
                var v = src.Vertices[i];
                if (v == null) continue;
                var k = Key(v.Position);
                if (!grid.TryGetValue(k, out var lst)) { lst = new List<int>(); grid[k] = lst; }
                lst.Add(i);
            }

            float d2 = dist * dist;
            foreach (var v in dst.Vertices)
            {
                if (v == null) continue;
                int id = 0;
                float best = float.MaxValue;
                var (kx, ky, kz) = Key(v.Position);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue((kx + dx, ky + dy, kz + dz), out var lst)) continue;
                    foreach (int i in lst)
                    {
                        float s = (src.Vertices[i].Position - v.Position).sqrMagnitude;
                        if (s <= d2 && s < best) { best = s; id = src.Vertices[i].Id; }
                    }
                }
                v.Id = id;
            }
            foreach (var f in dst.Faces) if (f != null) f.Id = 0;
            dst.RebuildIdSets();
        }
    }
}
