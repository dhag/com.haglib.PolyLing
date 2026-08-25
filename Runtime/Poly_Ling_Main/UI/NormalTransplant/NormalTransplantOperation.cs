// NormalTransplantOperation.cs
// 法線移植 サンプル算出 と 確定処理（Undo記録）
// UnityEditor非依存
//
// 【ワールド座標の取得について】
// 本ファイルは自前でスキニング／ワールド変換を計算しない。
// getWorldPositions デリゲート経由で GPU が計算した値
// （PlayerViewportManager.TryGetMeshWorldPositions →
//   UnifiedBufferManager.GetDisplayPositions）を受け取る。
// MeshContext.VertexMatrix 等の CPU 独自計算を新たに呼び出さないこと。
//
// 【法線の空間変換】
// GPU 側に法線の読み戻し経路は無いため、法線は CPU で扱う。
// スキニング無しを前提とし、オブジェクト単位の MeshContext.WorldMatrix だけを使う。
//   ローカル → ワールド : inverse(WorldMatrix) の転置
//   ワールド → ローカル : WorldMatrix の転置
//     （(A^T)^-1 = (A^-1)^T より、inverse(M)^T の逆行列は M^T）
// MeshContext.LocalToWorldDirection（MeshContext.cs:675）は WorldMatrix.MultiplyVector
// であり逆転置ではないため、ここでは使わない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class NormalTransplantOperation
    {
        // ================================================================
        // 法線行列
        // ================================================================

        /// <summary>ローカル法線 → ワールド法線 の行列（WorldMatrix の逆転置）。</summary>
        public static Matrix4x4 LocalToWorldNormalMatrix(MeshContext ctx)
        {
            if (ctx == null) return Matrix4x4.identity;
            return ctx.WorldMatrix.inverse.transpose;
        }

        /// <summary>ワールド法線 → ローカル法線 の行列（WorldMatrix の転置）。</summary>
        public static Matrix4x4 WorldToLocalNormalMatrix(MeshContext ctx)
        {
            if (ctx == null) return Matrix4x4.identity;
            return ctx.WorldMatrix.transpose;
        }

        // ================================================================
        // サンプル算出
        // ================================================================

        /// <summary>
        /// ビフォー／アフターからプリズム群を作り、各ターゲット頂点の移植法線を求める。
        /// 失敗時は null を返し error に理由を入れる。
        /// </summary>
        public static List<NormalTransplantPreviewState.TargetSample> ComputeSamples(
            ModelContext model,
            int beforeIndex, int afterIndex,
            IList<int> targetIndices,
            NormalPrismSolver.TriangleBlendMode mode,
            bool allowNearest,
            Func<MeshContext, Vector3[]> getWorldPositions,
            out string error)
        {
            error = null;

            if (model == null) { error = "モデルがありません"; return null; }
            if (getWorldPositions == null) { error = "ワールド座標の取得経路が未配線です"; return null; }
            if (targetIndices == null || targetIndices.Count == 0) { error = "ターゲットが選ばれていません"; return null; }

            if (beforeIndex == afterIndex) { error = "ビフォーとアフターが同一です"; return null; }

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx = model.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null) { error = "ビフォーオブジェクトが不正です"; return null; }
            if (afterCtx?.MeshObject == null) { error = "アフターオブジェクトが不正です"; return null; }

            var beforeWorld = getWorldPositions(beforeCtx);
            var afterWorld = getWorldPositions(afterCtx);
            if (beforeWorld == null) { error = "ビフォーのワールド座標を取得できません"; return null; }
            if (afterWorld == null) { error = "アフターのワールド座標を取得できません"; return null; }

            var solver = NormalPrismSolver.Build(
                beforeCtx.MeshObject, beforeWorld, LocalToWorldNormalMatrix(beforeCtx),
                afterCtx.MeshObject, afterWorld, LocalToWorldNormalMatrix(afterCtx),
                out error);
            if (solver == null) return null;

            var result = new List<NormalTransplantPreviewState.TargetSample>();

            foreach (int ti in targetIndices)
            {
                if (ti == beforeIndex || ti == afterIndex) continue;

                var ctx = model.GetMeshContext(ti);
                var mo = ctx?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                var world = getWorldPositions(ctx);
                if (world == null) continue;

                Matrix4x4 toLocal = WorldToLocalNormalMatrix(ctx);

                int vc = Mathf.Min(mo.VertexCount, world.Length);

                var sample = new NormalTransplantPreviewState.TargetSample
                {
                    MasterIndex = ti,
                    VertexCount = mo.VertexCount,
                    LocalNormals = new Vector3[mo.VertexCount],
                    Resolved = new bool[mo.VertexCount],
                };

                for (int vi = 0; vi < vc; vi++)
                {
                    if (!solver.TryEvaluate(world[vi], mode, allowNearest,
                            out Vector3 worldNormal, out bool inside))
                        continue;

                    Vector3 local = toLocal.MultiplyVector(worldNormal);
                    if (local.sqrMagnitude < 1e-12f) continue;

                    sample.LocalNormals[vi] = local.normalized;
                    sample.Resolved[vi] = true;
                    sample.ResolvedCount++;
                    if (inside) sample.InsideCount++;
                }

                result.Add(sample);
            }

            if (result.Count == 0)
            {
                error = "ターゲットの頂点を取得できません";
                return null;
            }

            return result;
        }

        // ================================================================
        // 確定
        // ================================================================

        /// <summary>
        /// プレビュー状態を確定する。
        ///
        /// Undo のスナップショットは「移植前」を撮る必要があるため、内部で一度
        /// 退避値へ戻してから適用率を適用し直す。preview がすでに Apply 済みでも
        /// 二重適用にはならない。
        /// </summary>
        /// <returns>法線を書き換えたメッシュ数</returns>
        public static int Apply(
            ModelContext model,
            NormalTransplantPreviewState preview,
            float strength,
            ToolContext toolCtx)
        {
            if (model == null || preview == null || !preview.IsActive) return 0;

            var samples = preview.Samples;
            if (samples == null || samples.Count == 0) return 0;

            var undo = toolCtx?.UndoController;

            // 対象メッシュを先に確定させておく（適用中にリストが変わらない前提）
            var contexts = new List<MeshContext>(samples.Count);
            foreach (var s in samples)
            {
                var ctx = model.GetMeshContext(s.MasterIndex);
                contexts.Add(ctx?.MeshObject != null ? ctx : null);
            }

            // ── 移植前スナップショット
            preview.Restore(model);

            var beforeSnapshots = new MeshObjectSnapshot[contexts.Count];
            if (undo != null)
            {
                for (int i = 0; i < contexts.Count; i++)
                {
                    var ctx = contexts[i];
                    if (ctx == null) continue;

                    undo.SetMeshObject(ctx.MeshObject, ctx.UnityMesh);
                    undo.MeshUndoContext.ParentModelContext = model;
                    beforeSnapshots[i] = undo.CaptureMeshObjectSnapshot();
                }
            }

            // ── 適用
            preview.Apply(model, strength);

            int changed = 0;

            for (int i = 0; i < contexts.Count; i++)
            {
                var ctx = contexts[i];
                if (ctx == null) continue;

                var mo = ctx.MeshObject;

                // 手で編集した法線は、頂点移動時の自動再計算で消えてしまう
                // （MeshUndoContext.ApplyVertexPositionsToMesh）。維持フラグを立てる。
                mo.PreserveNormals = true;

                NormalSmoothingOps.NormalizeSlotCounts(mo);
                NormalSmoothingOps.ValidateSlotInvariant(mo, mo.Name);

                changed++;

                if (undo != null && beforeSnapshots[i] != null)
                {
                    undo.SetMeshObject(mo, ctx.UnityMesh);
                    undo.MeshUndoContext.ParentModelContext = model;
                    var after = undo.CaptureMeshObjectSnapshot();

                    toolCtx?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                        undo, beforeSnapshots[i], after, "Normal Transplant"));
                }
            }

            if (changed > 0)
            {
                // ミラー側の面は選択できないため、実体側の編集結果を写す。
                // 生成ミラー（MirrorGeometryDerived）のみが対象。
                MirrorBranchOps.RebakeDerivedMirrorNormals(
                    model.MeshContextList, model.MaterialCount);

                model.OnListChanged?.Invoke();
                toolCtx?.Repaint?.Invoke();
            }

            return changed;
        }
    }
}
