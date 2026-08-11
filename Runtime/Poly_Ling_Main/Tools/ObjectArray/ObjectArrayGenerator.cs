// Runtime/Poly_Ling_Main/Tools/ObjectArray/ObjectArrayGenerator.cs
// 入力オブジェクトリストを N 組ぶん複製し、各組へ歪み（IMeshDeformer）を掛ける。
// ModelContext には一切触らない純粋な生成部で、挿入は ObjectArrayInserter が行う。
//
// 【座標往復】DeformApplier (DeformApplier.cs:177-204) と同じ経路をたどる。
//
//     元メッシュローカル p
//       → sourceContext.LocalToWorld(p)
//       → workAxis.WorldToLocal(...)        ← ここから先が作業軸ローカル
//       → deformer.Evaluate(...) - anchor + OffsetStep·i
//       → workAxis.LocalToWorld(...)
//       → worldToOutputLocal.MultiplyPoint3x4(...)   ← 出力先の空間へ入れる
//
//   最後だけ DeformApplier と違う。あちらは元メッシュへ書き戻すので
//   sourceContext.WorldToLocal だが、こちらは別の親の下へ置くため
//   出力先のワールド逆行列で入れ直す。生成物の BoneTransform は単位に
//   するので、これで見た目の位置がそのまま保たれる。
//
// 【DeformContext は組をまたいで共通】
//   s の範囲は入力リスト全体の AABB から1度だけ作り、全複製で使い回す。
//   組ごとに取り直すと、位置ずらしのぶんだけ範囲が動いて波長が変わってしまう。
//
// 【複製ごとの差】
//   ・位相 … PhaseStepDeg × i。デフォーマが IDeformerPhase を実装していれば効く。
//            未実装なら黙って無視する（歪みを「任意のメソッド」に保つため、
//            必須要件を増やさない）。
//   ・位置 … OffsetStep × i。作業軸ローカルで足す。歪みの後に足すので、
//            ずらしても波形は変わらない。
//
// 【原点を固定（FixOrigin）】
//   上端 a = (0, ctx.SMax, 0)（作業軸ローカル +Y の最大側）での変位
//     anchor = deformer.Evaluate(a) - a
//   を全頂点から引く。上端は動かず、それ以外の位置は上端からの相対になる。
//   引くのは X / Y / Z の全成分。位相は組ごとに変わるので anchor も組ごとに取る。
//   OffsetStep は anchor を引いた後に足すため、位置ステップは従来どおり効く。
//
// 【入力の階層】
//   選んだ部分集合の中だけで相対 Depth を決め直す（親を選ばず子だけ選んでも
//   破綻しないように）。SummaryTreeRoot.BuildHierarchyFromDepth と同じスタック法。
//
// Runtime/Poly_Ling_Main/Tools/ObjectArray/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools.Deformers;

namespace Poly_Ling.Tools.ObjectArray
{
    /// <summary>複製元の1オブジェクト。</summary>
    public sealed class ObjectArraySource
    {
        /// <summary>元の描画オブジェクト。</summary>
        public MeshContext Context;

        /// <summary>MeshContextList 内での位置。</summary>
        public int MasterIndex;

        /// <summary>選んだ部分集合の中で数え直した階層の深さ。最上位が 0。</summary>
        public int RelativeDepth;
    }

    /// <summary>生成された1オブジェクト。</summary>
    public sealed class ObjectArrayPiece
    {
        /// <summary>生成メッシュ。頂点は出力先の空間で入っている。</summary>
        public MeshObject Mesh;

        /// <summary>名前の素。一意化は呼び出し側が行う。</summary>
        public string Name;

        /// <summary>入力リスト内での相対的な深さ。</summary>
        public int RelativeDepth;

        /// <summary>何組目か（0 起点）。</summary>
        public int CopyIndex;
    }

    public static class ObjectArrayGenerator
    {
        // ================================================================
        // 入力の組み立て
        // ================================================================

        /// <summary>
        /// マスターインデックスの集合から複製元リストを作る。
        /// リスト順に並べ直し、選んだ部分集合の中だけで相対 Depth を決める。
        /// 描画オブジェクト以外（ボーン・モーフ等）と無効なインデックスは捨てる。
        /// </summary>
        public static List<ObjectArraySource> BuildSources(
            ModelContext model, IReadOnlyList<int> masterIndices)
        {
            var list = new List<ObjectArraySource>();
            if (model == null || masterIndices == null) return list;

            // 重複を落としてリスト順にそろえる。
            var ordered = new List<int>();
            var seen    = new HashSet<int>();
            foreach (int i in masterIndices)
                if (i >= 0 && i < model.MeshContextCount && seen.Add(i)) ordered.Add(i);
            ordered.Sort();

            // 親候補スタックには元の Depth を積む。相対深さはスタックの高さ。
            var depthStack = new Stack<int>();

            foreach (int idx in ordered)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (!IsDrawable(mc)) continue;

                int depth = mc.Depth;
                while (depthStack.Count > 0 && depthStack.Peek() >= depth) depthStack.Pop();

                list.Add(new ObjectArraySource
                {
                    Context       = mc,
                    MasterIndex   = idx,
                    RelativeDepth = depthStack.Count,
                });

                depthStack.Push(depth);
            }

            return list;
        }

        /// <summary>描画オブジェクトか（TypedMeshIndices の Drawable と同じ判定）。</summary>
        private static bool IsDrawable(MeshContext mc)
        {
            var t = mc.Type;
            return t == MeshType.Mesh || t == MeshType.BakedMirror || t == MeshType.MirrorSide;
        }

        // ================================================================
        // 事前計算コンテキスト
        // ================================================================

        /// <summary>
        /// 入力リスト全体の頂点から DeformContext を作る。
        /// 中身は DeformApplier.BuildContext (DeformApplier.cs:342-384) と同じ規則。
        /// </summary>
        public static DeformContext BuildContext(
            IReadOnlyList<ObjectArraySource> sources, WorkAxisContext axis)
        {
            var ctx = new DeformContext
            {
                LocalMin    = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                LocalMax    = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                VertexCount = 0,
            };

            if (sources != null && axis != null)
            {
                foreach (var s in sources)
                {
                    var mo = s?.Context?.MeshObject;
                    if (mo == null) continue;

                    for (int i = 0; i < mo.VertexCount; i++)
                    {
                        Vector3 local = axis.WorldToLocal(s.Context.LocalToWorld(mo.Vertices[i].Position));

                        if (local.x < ctx.LocalMin.x) ctx.LocalMin.x = local.x;
                        if (local.y < ctx.LocalMin.y) ctx.LocalMin.y = local.y;
                        if (local.z < ctx.LocalMin.z) ctx.LocalMin.z = local.z;

                        if (local.x > ctx.LocalMax.x) ctx.LocalMax.x = local.x;
                        if (local.y > ctx.LocalMax.y) ctx.LocalMax.y = local.y;
                        if (local.z > ctx.LocalMax.z) ctx.LocalMax.z = local.z;

                        ctx.VertexCount++;
                    }
                }
            }

            if (ctx.VertexCount == 0)
            {
                ctx.LocalMin = Vector3.zero;
                ctx.LocalMax = Vector3.zero;
            }

            // s は AABB の y 成分そのもの。
            ctx.SMin = ctx.LocalMin.y;
            ctx.SMax = ctx.LocalMax.y;

            return ctx;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 入力リストを p.Count 組ぶん複製し、各組へ歪みを掛けて返す。
        /// 元のオブジェクトには一切触らない。
        /// </summary>
        /// <param name="worldToOutputLocal">
        /// ワールド → 出力先のローカル空間。ルートへ置くなら単位行列、
        /// 出力先オブジェクトの下へ置くならその WorldMatrixInverse。
        /// </param>
        public static List<ObjectArrayPiece> Generate(
            IReadOnlyList<ObjectArraySource> sources,
            WorkAxisContext axis,
            IMeshDeformer deformer,
            ObjectArrayParams p,
            Matrix4x4 worldToOutputLocal)
        {
            var pieces = new List<ObjectArrayPiece>();
            if (sources == null || sources.Count == 0) return pieces;
            if (axis == null || deformer == null || p == null) return pieces;
            if (p.Count < 1) return pieces;

            var ctx = BuildContext(sources, axis);

            // 組ごとの空の親。「中に生成」は全部を1メッシュへ統合するので親に意味が無い。
            bool wrap = p.GroupEachCopy && p.OutputMode == ObjectArrayOutputMode.AsChild;
            int  depthOffset = wrap ? 1 : 0;

            // 位相はデフォーマ自身が持つ値。借りて使い、必ず元へ戻す。
            var phase = deformer as IDeformerPhase;
            float savedPhase = phase?.PhaseDeg ?? 0f;

            try
            {
                for (int copy = 0; copy < p.Count; copy++)
                {
                    if (phase != null) phase.PhaseDeg = savedPhase + p.PhaseStepDeg * copy;
                    deformer.Prepare(ctx);

                    Vector3 offset = p.OffsetStep * copy;

                    // 上端を固定するための補正。組ごとに位相が変わるので毎回取り直す。
                    Vector3 anchor = Vector3.zero;
                    if (p.FixOrigin)
                    {
                        Vector3 top = new Vector3(0f, ctx.SMax, 0f);
                        anchor = deformer.Evaluate(top) - top;
                    }

                    // 空の親を先に積む。頂点0の通常メッシュで表す
                    // （MQO の通常オブジェクトと同じ扱い。MQOImporter.cs:1074）。
                    if (wrap)
                    {
                        string groupName = string.IsNullOrEmpty(p.GroupNameBase)
                            ? "Group"
                            : p.GroupNameBase;
                        groupName = $"{groupName}_{copy + 1}";

                        pieces.Add(new ObjectArrayPiece
                        {
                            Mesh          = new MeshObject(groupName),
                            Name          = groupName,
                            RelativeDepth = 0,
                            CopyIndex     = copy,
                        });
                    }

                    foreach (var s in sources)
                    {
                        var src = s?.Context?.MeshObject;
                        if (src == null) continue;

                        var mo = src.Clone();

                        // Vertex は参照型（MeshObject.cs:78）なので直接書き換える。
                        for (int i = 0; i < mo.VertexCount; i++)
                        {
                            var v = mo.Vertices[i];
                            if (v == null) continue;

                            Vector3 world    = s.Context.LocalToWorld(v.Position);
                            Vector3 local    = axis.WorldToLocal(world);
                            Vector3 deformed = deformer.Evaluate(local) - anchor + offset;
                            Vector3 outWorld = axis.LocalToWorld(deformed);

                            v.Position = worldToOutputLocal.MultiplyPoint3x4(outWorld);
                        }

                        // 法線は再計算しない。DeformApplier.Apply (DeformApplier.cs:206-208) も
                        // 頂点を動かしてキャッシュを捨てるだけで、元メッシュが持っていた
                        // 法線の構成（スムーズ／フラットの分かれ方）をそのまま残す。
                        // ここで RecalculateNormals を掛けると全部フラットに潰れてしまう。
                        mo.InvalidatePositionCache();

                        string baseName = string.IsNullOrEmpty(p.NameBase)
                            ? (s.Context.Name ?? src.Name ?? "Object")
                            : p.NameBase;

                        mo.Name = $"{baseName}_{copy + 1}";

                        pieces.Add(new ObjectArrayPiece
                        {
                            Mesh          = mo,
                            Name          = mo.Name,
                            RelativeDepth = s.RelativeDepth + depthOffset,
                            CopyIndex     = copy,
                        });
                    }
                }
            }
            finally
            {
                if (phase != null) phase.PhaseDeg = savedPhase;
            }

            return pieces;
        }
    }
}
