// ShrinkOperation.cs
// シュリンカー 停止パラメータ算出 と 確定処理（バックアップ作成 + Undo記録）
// UnityEditor非依存
//
// 【ワールド座標の取得について】
// 本ファイルは自前でスキニング／ワールド変換を計算しない。
// getWorldPositions デリゲート経由で GPU が計算した値
// （PlayerViewportManager.TryGetMeshWorldPositions →
//   UnifiedBufferManager.GetDisplayPositions）を受け取る。
// MeshContext.VertexMatrix 等の CPU 独自計算を新たに呼び出さないこと。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class ShrinkOperation
    {
        // ================================================================
        // 停止パラメータ算出
        // ================================================================

        /// <summary>
        /// コライダー群を一様グリッドに積み、ビフォー→アフターの各線分について
        /// 停止パラメータ [0,1] を求める。失敗時は null を返し error に理由を入れる。
        /// </summary>
        public static float[] ComputeStopParams(
            ModelContext model,
            int beforeIndex, int afterIndex,
            IList<int> colliderIndices,
            float surfaceOffset, bool frontFaceOnly,
            Func<MeshContext, Vector3[]> getWorldPositions,
            out string error)
        {
            error = null;

            if (model == null) { error = "モデルがありません"; return null; }
            if (getWorldPositions == null) { error = "ワールド座標の取得経路が未配線です"; return null; }

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx  = model.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null) { error = "ビフォーオブジェクトが不正です"; return null; }
            if (afterCtx?.MeshObject == null)  { error = "アフターオブジェクトが不正です"; return null; }
            if (beforeIndex == afterIndex)     { error = "ビフォーとアフターが同一です"; return null; }

            var beforeWorld = getWorldPositions(beforeCtx);
            var afterWorld  = getWorldPositions(afterCtx);
            if (beforeWorld == null) { error = "ビフォーのワールド座標を取得できません"; return null; }
            if (afterWorld  == null) { error = "アフターのワールド座標を取得できません"; return null; }

            var solver = new ShrinkCollisionSolver();

            if (colliderIndices != null)
            {
                foreach (int ci in colliderIndices)
                {
                    if (ci == beforeIndex || ci == afterIndex) continue;
                    var cctx = model.GetMeshContext(ci);
                    if (cctx?.MeshObject == null) continue;
                    var cw = getWorldPositions(cctx);
                    if (cw == null) continue;
                    solver.AddMesh(cctx.MeshObject, cw);
                }
            }

            solver.Build();

            if (solver.TriangleCount == 0)
                error = "衝突対象の三角形が0件です（全頂点がアフターまで移動します）";

            return solver.ComputeStopParams(beforeWorld, afterWorld, surfaceOffset, frontFaceOnly);
        }

        /// <summary>
        /// 判定方式で分岐する版。面方式では frontFaceOnly は使わない
        /// （距離判定に面の向きの概念が無いため）。
        /// </summary>
        public static float[] ComputeStopParams(
            ModelContext model,
            int beforeIndex, int afterIndex,
            IList<int> colliderIndices,
            float surfaceOffset, bool frontFaceOnly,
            ShrinkCollisionMode mode, int maxPasses,
            Func<MeshContext, Vector3[]> getWorldPositions,
            out string error)
        {
            if (mode == ShrinkCollisionMode.FacePair)
            {
                return ComputeStopParamsByFace(
                    model, beforeIndex, afterIndex, colliderIndices,
                    surfaceOffset, maxPasses, getWorldPositions,
                    out error, out _);
            }

            return ComputeStopParams(
                model, beforeIndex, afterIndex, colliderIndices,
                surfaceOffset, frontFaceOnly, getWorldPositions, out error);
        }

        /// <summary>
        /// 面方式。ビフォー面を三角形に割り、移動する三角形とコライダー三角形の
        /// 接触時刻を保守的前進法で求め、面単位でまとめてから頂点へ配る。
        /// 失敗時は null を返し error に理由を入れる。
        /// </summary>
        public static float[] ComputeStopParamsByFace(
            ModelContext model,
            int beforeIndex, int afterIndex,
            IList<int> colliderIndices,
            float surfaceOffset, int maxPasses,
            Func<MeshContext, Vector3[]> getWorldPositions,
            out string error,
            out ShrinkFaceStats stats)
        {
            error = null;
            stats = default;

            if (model == null) { error = "モデルがありません"; return null; }
            if (getWorldPositions == null) { error = "ワールド座標の取得経路が未配線です"; return null; }

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx  = model.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null) { error = "ビフォーオブジェクトが不正です"; return null; }
            if (afterCtx?.MeshObject == null)  { error = "アフターオブジェクトが不正です"; return null; }
            if (beforeIndex == afterIndex)     { error = "ビフォーとアフターが同一です"; return null; }

            var beforeWorld = getWorldPositions(beforeCtx);
            var afterWorld  = getWorldPositions(afterCtx);
            if (beforeWorld == null) { error = "ビフォーのワールド座標を取得できません"; return null; }
            if (afterWorld  == null) { error = "アフターのワールド座標を取得できません"; return null; }

            var solver = new ShrinkFaceCollisionSolver();

            if (colliderIndices != null)
            {
                foreach (int ci in colliderIndices)
                {
                    if (ci == beforeIndex || ci == afterIndex) continue;
                    var cctx = model.GetMeshContext(ci);
                    if (cctx?.MeshObject == null) continue;
                    var cw = getWorldPositions(cctx);
                    if (cw == null) continue;
                    solver.AddColliderMesh(cctx.MeshObject, cw);
                }
            }

            if (solver.ColliderTriangleCount == 0)
                error = "衝突対象の三角形が0件です（全頂点がアフターまで移動します）";

            var result = solver.ComputeStopParams(
                beforeCtx.MeshObject, beforeWorld, afterWorld, surfaceOffset, maxPasses);

            if (result == null)
            {
                if (string.IsNullOrEmpty(error)) error = "停止パラメータを算出できません";
                return null;
            }

            stats = new ShrinkFaceStats
            {
                FaceCount             = solver.FaceCount,
                StoppedFaceCount      = solver.StoppedFaceCount,
                UsedPasses            = solver.UsedPasses,
                HitPassLimit          = solver.HitPassLimit,
                ColliderTriangleCount = solver.ColliderTriangleCount,
            };

            return result;
        }

        // ================================================================
        // 確定
        // ================================================================

        /// <summary>
        /// プレビュー中の状態を確定する。
        /// 呼び出し前に ShrinkPreviewState.Apply でスライダー値を反映しておくこと。
        /// </summary>
        /// <param name="colliderIndices">
        /// 衝突対象の MasterIndex。確定後は結果だけが見えるよう、両モードとも非表示にする。
        /// </param>
        /// <param name="createNewObject">
        /// true : シュリンク結果を新規オブジェクトとして追加し、ビフォーは元形状のまま非表示にする（既定）。
        /// false: ビフォーを上書きし、元形状を <名前>_backup として非表示で追加する。
        /// </param>
        /// <returns>追加したメッシュ数（0 または 1）</returns>
        public static int Apply(
            ModelContext model,
            ShrinkPreviewState preview,
            IList<int> colliderIndices,
            bool createNewObject,
            bool recalculateNormals,
            ToolContext toolCtx)
        {
            if (model == null || preview == null || !preview.IsActive) return 0;

            var ctx = model.GetMeshContext(preview.BeforeIndex);
            if (ctx?.MeshObject == null) return 0;

            var backup = preview.Backup;
            if (backup == null) return 0;

            return createNewObject
                ? ApplyAsNewObject(model, preview, ctx, backup, colliderIndices, recalculateNormals, toolCtx)
                : ApplyOverwrite(model, preview, ctx, backup, colliderIndices, recalculateNormals, toolCtx);
        }

        // ----------------------------------------------------------------
        // 新規オブジェクトモード（既定）
        // ----------------------------------------------------------------

        private static int ApplyAsNewObject(
            ModelContext model, ShrinkPreviewState preview, MeshContext ctx,
            Vector3[] backup, IList<int> colliderIndices,
            bool recalculateNormals, ToolContext toolCtx)
        {
            // この時点で ctx.MeshObject はシュリンク結果の座標を保持している。
            // 変換系（BindPose / BoneTransform / Depth / Mirror 設定）を落とさないよう、
            // 既存のディープコピー経路を使う（MeshListRecords.cs:1108）。
            var newCtx = MeshFilterToSkinnedRecord.CloneMeshContext(ctx);
            if (newCtx?.MeshObject == null) return 0;

            var existingNames = CollectNames(model);
            string newName = GenerateUniqueName(ctx.Name + "_shrink", existingNames);
            newCtx.Name            = newName;
            newCtx.MeshObject.Name = newName;
            if (newCtx.UnityMesh != null) newCtx.UnityMesh.name = newName;
            newCtx.Type      = ctx.Type;
            newCtx.IsVisible = true;
            newCtx.ParentModelContext = model;

            if (recalculateNormals) newCtx.MeshObject.RecalculateSmoothNormals();

            // ビフォーを元形状へ戻す（新規モードではビフォーを変更しない）
            var mo = ctx.MeshObject;
            int count = Mathf.Min(backup.Length, mo.VertexCount);
            for (int i = 0; i < count; i++) mo.Vertices[i].Position = backup[i];
            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
            BlendOperation.SyncMirrorSide(model, ctx, toolCtx);

            // プレビュー中に変更した可視状態を戻したうえで、
            // ビフォー／アフター／衝突対象を非表示にする（結果だけを残す）
            preview.RestoreVisibility(model);
            ctx.IsVisible = false;
            var afterCtx = model.GetMeshContext(preview.AfterIndex);
            if (afterCtx != null) afterCtx.IsVisible = false;
            HideColliders(model, colliderIndices, protectIndex: -1);

            var undo   = toolCtx?.UndoController;
            var oldSel = model.CaptureAllSelectedIndices();
            int insertIndex = model.Add(newCtx);
            var newSel = model.CaptureAllSelectedIndices();
            undo?.RecordMeshContextAdd(newCtx, insertIndex, oldSel, newSel);

            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return 1;
        }

        // ----------------------------------------------------------------
        // 上書きモード
        // ----------------------------------------------------------------

        private static int ApplyOverwrite(
            ModelContext model, ShrinkPreviewState preview, MeshContext ctx,
            Vector3[] backup, IList<int> colliderIndices,
            bool recalculateNormals, ToolContext toolCtx)
        {
            var mo = ctx.MeshObject;
            var existingNames = CollectNames(model);

            var undo   = toolCtx?.UndoController;
            var before = undo?.CaptureMeshObjectSnapshot();

            // バックアップメッシュ（シュリンク前の形状）
            var backupMo = mo.Clone();
            for (int i = 0; i < backup.Length && i < backupMo.VertexCount; i++)
                backupMo.Vertices[i].Position = backup[i];

            string backupName = GenerateUniqueName(ctx.Name + "_backup", existingNames);
            backupMo.Name = backupName;

            var backupCtx = new MeshContext
            {
                MeshObject = backupMo,
                Name       = backupName,
                Type       = ctx.Type,
                IsVisible  = false,
            };
            backupCtx.UnityMesh = backupMo.ToUnityMeshShared();
            if (backupCtx.UnityMesh != null)
                backupCtx.UnityMesh.hideFlags = HideFlags.HideAndDontSave;

            model.Add(backupCtx);

            if (recalculateNormals) mo.RecalculateSmoothNormals();

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
            BlendOperation.SyncMirrorSide(model, ctx, toolCtx);

            // プレビュー中に変更した可視状態を戻したうえで、衝突対象を非表示にする。
            // このモードではビフォー自身が結果なので、ビフォーは非表示にしない。
            preview.RestoreVisibility(model);
            HideColliders(model, colliderIndices, protectIndex: preview.BeforeIndex);

            if (undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                toolCtx?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Shrink"));
            }

            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return 1;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>
        /// 衝突対象を非表示にする。protectIndex に一致するものは対象外（結果メッシュの保護）。
        /// </summary>
        private static void HideColliders(ModelContext model, IList<int> colliderIndices, int protectIndex)
        {
            if (model == null || colliderIndices == null) return;
            foreach (int ci in colliderIndices)
            {
                if (ci == protectIndex) continue;
                var mc = model.GetMeshContext(ci);
                if (mc != null) mc.IsVisible = false;
            }
        }

        private static HashSet<string> CollectNames(ModelContext model)
        {
            var names = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) names.Add(mc.Name);
            }
            return names;
        }

        private static string GenerateUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName)) return baseName;
            for (int n = 1; n < 10000; n++)
            {
                string name = $"{baseName}_{n}";
                if (!existingNames.Contains(name)) return name;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
