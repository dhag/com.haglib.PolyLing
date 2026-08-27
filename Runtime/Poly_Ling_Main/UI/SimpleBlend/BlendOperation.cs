// BlendOperation.cs
// メッシュブレンド 確定処理（バックアップ / 新規オブジェクト作成 + Undo記録）
// UnityEditor非依存。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Commands;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UI
{
    public static class BlendOperation
    {
        /// <summary>
        /// プレビュー内容を確定する。
        ///
        /// createNewObject = false … 宛先に書き込み、ブレンド前の形状を持つ
        ///   バックアップメッシュを作る。
        /// createNewObject = true  … 宛先を複製し、複製側へ書き込む。
        ///   元がそのまま残るのでバックアップメッシュは作らない。
        ///
        /// Undo は 2 系統に分かれる。頂点変更は RecordTopologyChangeCommand
        /// （vertex/topology スタック）、メッシュ追加は RecordMeshContextsAdd
        /// （MeshList スタック）。追加を記録しないと Undo しても増えたメッシュが残る。
        /// </summary>
        /// <returns>書き込み先となった MeshContext の索引。失敗時は -1。</returns>
        public static int ApplyBlend(
            ModelContext model,
            BlendPreviewState preview,
            IReadOnlyList<BlendSourceEntry> sources,
            bool recalculateNormals,
            bool selectedVertsOnly,
            BlendMatchMode matchMode,
            bool createNewObject,
            ToolContext toolCtx)
        {
            if (model == null || preview == null || !preview.IsActive) return -1;
            if (sources == null || sources.Count == 0) return -1;

            int destIndex = preview.DestIndex;
            var destCtx   = model.GetMeshContext(destIndex);
            if (destCtx?.MeshObject == null) return -1;

            var backup       = preview.Backup;
            var normalBackup = preview.NormalBackup;
            if (backup == null) return -1;

            var undo        = toolCtx?.UndoController;
            var oldSelected = model.CaptureAllSelectedIndices();
            var addedCtxs   = new List<(int Index, MeshContext MeshContext)>();

            // ── プレビュー結果を捨て、ブレンド前の状態に戻す。
            //    ここが Undo の「before」であり、複製元でもなければならない。
            var destMo = destCtx.MeshObject;
            int restoreCount = Mathf.Min(backup.Length, destMo.VertexCount);
            for (int i = 0; i < restoreCount; i++) destMo.Vertices[i].Position = backup[i];
            BlendPreviewState.RestoreNormals(destMo, normalBackup);

            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) existingNames.Add(mc.Name);
            }

            MeshContext        writeCtx;
            int                writeIndex;
            MeshObjectSnapshot before = null;

            if (createNewObject)
            {
                // ── 複製を作り、そちらへ書き込む
                string cloneName = GenerateUniqueName(destCtx.Name + "_blend", existingNames);
                writeCtx  = CloneContext(destCtx, cloneName);
                writeIndex = model.Add(writeCtx);
                addedCtxs.Add((writeIndex, writeCtx));
            }
            else
            {
                // ── 宛先へ書き込み、ブレンド前の形状をバックアップとして残す
                string backupName = GenerateUniqueName(destCtx.Name + "_backup", existingNames);
                var backupCtx  = CloneContext(destCtx, backupName);
                int backupIdx  = model.Add(backupCtx);
                addedCtxs.Add((backupIdx, backupCtx));

                writeCtx   = destCtx;
                writeIndex = destIndex;

                if (undo != null)
                {
                    undo.SetMeshObjectFor(writeCtx);
                    undo.MeshUndoContext.ParentModelContext = model;
                    before = undo.CaptureMeshObjectSnapshot();
                }
            }

            // ── ブレンド確定
            var writeMo     = writeCtx.MeshObject;
            var nonIsolated = BlendPreviewState.BuildNonIsolatedSet(writeMo);
            var verts       = selectedVertsOnly ? writeCtx.SelectedVertices : null;

            BlendPreviewState.BlendVertices(
                writeCtx, backup, sources, nonIsolated, verts, matchMode, null);

            if (recalculateNormals) writeMo.RecalculateSmoothNormals();

            // ── Undo after（既存メッシュを書き換えたときのみ）
            if (!createNewObject && undo != null && before != null)
            {
                var after = undo.CaptureMeshObjectSnapshot();
                toolCtx?.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    undo, before, after, "Mesh Blend"));
                undo.ClearTargetMeshContext();
            }

            // ── 追加したメッシュを MeshList スタックへ記録
            if (undo != null && addedCtxs.Count > 0)
            {
                var newSelected = model.CaptureAllSelectedIndices();
                undo.RecordMeshContextsAdd(addedCtxs, oldSelected, newSelected);
            }

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(writeCtx);
            SyncMirrorSide(model, writeCtx, toolCtx, recalculateNormals, null);

            // 可視状態復元（書き込み先は表示のまま残す）
            preview.RestoreVisibility(model, writeIndex);

            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return writeIndex;
        }

        /// <summary>
        /// MeshContext を複製する。
        ///
        /// BindPose / WorldMatrix / BonePoseData / MirrorGeometryDerived は
        /// MeshContext 側の実体で、MeshObject.Clone() では移らない
        /// （MeshContext.cs:466,481,486,493、BonePoseData は :423）。
        /// 引き継がないと、スキンドメッシュの複製を表示したときに
        /// SkinningMatrix = WorldMatrix × BindPose が単位行列基準になり形が飛ぶ。
        /// </summary>
        public static MeshContext CloneContext(MeshContext src, string newName)
        {
            var mo = src.MeshObject.Clone();
            mo.Name = newName;

            var ctx = new MeshContext
            {
                MeshObject = mo,
                Name       = newName,
                Type       = src.Type,
                IsVisible  = false,
                IsLocked   = src.IsLocked,
            };

            ctx.BindPose              = src.BindPose;
            ctx.WorldMatrix           = src.WorldMatrix;
            ctx.WorldMatrixInverse    = src.WorldMatrixInverse;
            ctx.MirrorGeometryDerived = src.MirrorGeometryDerived;
            // BonePoseData は可変オブジェクト。参照を共有すると、
            // あとで元メッシュをポーズさせたとき複製も一緒に動く。
            ctx.BonePoseData          = src.BonePoseData?.Clone();

            ctx.UnityMesh = mo.ToUnityMeshShared();
            if (ctx.UnityMesh != null)
                ctx.UnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

            return ctx;
        }

        /// <summary>
        /// 実体側の編集結果をミラー側へ写す。
        ///
        /// MirrorPair を持つ組は VertexMap 経由で同期する（MirrorPair.cs:274）。
        /// 名前 "+" と頂点数の一致だけを根拠にインデックス恒等で写す従来の方法は、
        /// MirrorPair が持つ対応表と食い違いうるため、ペアが取れないときの
        /// フォールバックに下げる。順序は PolyLingCore_Commands.ExecuteBlend と同じ。
        /// </summary>
        public static void SyncMirrorSide(
            ModelContext model, MeshContext ctx, ToolContext toolCtx,
            bool syncNormalsToo = false, Action<MeshContext> syncNormals = null)
        {
            if (model == null || ctx?.MeshObject == null) return;

            // ① MirrorPair 経由
            var pair = model.GetMirrorPair(ctx);
            if (pair != null && pair.Real == ctx && pair.IsValid && pair.Mirror?.MeshObject != null)
            {
                pair.SyncPositions();
                if (syncNormalsToo) pair.SyncNormals();
                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(pair.Mirror);
                if (syncNormalsToo) syncNormals?.Invoke(pair.Mirror);
                return;
            }

            // ② フォールバック: 名前 + "+" の MirrorSide をインデックス恒等で写す
            string mirrorName = ctx.Name + "+";
            var    axis       = ctx.GetMirrorSymmetryAxis();
            var    mo         = ctx.MeshObject;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                if (mc.Name != mirrorName) continue;
                if (mc.MeshObject == null || mc.MeshObject.VertexCount != mo.VertexCount) continue;

                var mirrorMo = mc.MeshObject;
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var pos = mo.Vertices[v].Position;
                    mirrorMo.Vertices[v].Position = axis switch
                    {
                        SymmetryAxis.Y => new Vector3( pos.x, -pos.y,  pos.z),
                        SymmetryAxis.Z => new Vector3( pos.x,  pos.y, -pos.z),
                        _              => new Vector3(-pos.x,  pos.y,  pos.z),
                    };
                }
                toolCtx?.SyncMeshContextPositionsOnly?.Invoke(mc);
                break;
            }
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
