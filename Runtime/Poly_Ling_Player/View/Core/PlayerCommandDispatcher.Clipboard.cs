// PlayerCommandDispatcher.Clipboard.cs
// コマンドディスパッチャ：選択部分のコピー・カット・貼り付け・その場で複製。
// 実体は SelectionClipboardOps（Core/Ops）。ここは対象の照合・Undo・更新通知を持つ。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>DispatchCore の分担：選択部分のクリップボード操作。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchClipboard(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── コピー（モデルは変えない）
                case CopySelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!PlayerCommandTargets.MatchesSelectedDrawables(model, c.MasterIndices, out string cpReason))
                    { Fail(cpReason); return true; }

                    var clip = SelectionClipboardOps.Capture(model, c.MasterIndices, out string capReason);
                    if (clip == null) { Fail(capReason); return true; }
                    project.SelectionClipboard = clip;

                    ReportClipboard(clip);
                    return true;
                }

                // ── カット（コピー → 選択削除。Undo は選択削除の 1 回）
                case CutSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnDeleteSelection == null) { Fail("delete selection handler not wired"); return true; }
                    if (!PlayerCommandTargets.MatchesSelectedDrawables(model, c.MasterIndices, out string ctReason))
                    { Fail(ctReason); return true; }
                    if (DeleteSelectionTool.GetDeletableCount(model) <= 0)
                    { Fail("削除できる要素がありません。頂点・面・線分を選択してください（辺だけの選択はカットできません）"); return true; }

                    var clip = SelectionClipboardOps.Capture(model, c.MasterIndices, out string capReason);
                    if (clip == null) { Fail(capReason); return true; }

                    string delReason = OnDeleteSelection.Invoke(new DeleteSelectionCommand(c.ModelIndex, c.MasterIndices));
                    if (delReason != null) { Fail(delReason); return true; }

                    // 削除まで済んでからクリップボードを置き換える（失敗したら前の内容を残す）。
                    project.SelectionClipboard = clip;
                    ReportClipboard(clip);
                    return true;
                }

                // ── 貼り付け
                case PasteSelectionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var clip = project.SelectionClipboard;
                    if (clip == null || clip.Parts.Count == 0) { Fail("クリップボードが空です。先にコピーかカットをしてください"); return true; }

                    if (c.Destination == SelectionPasteDestination.NewObject)
                        PasteAsNewObjects(project, model, clip);
                    else
                        PasteIntoObject(project, model, clip, c.TargetMasterIndex);
                    return true;
                }

                // ── その場で複製（クリップボードは使わない）
                case DuplicateInPlaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!PlayerCommandTargets.MatchesSelectedDrawables(model, c.MasterIndices, out string dpReason))
                    { Fail(dpReason); return true; }

                    var clip = SelectionClipboardOps.Capture(model, c.MasterIndices, out string capReason);
                    if (clip == null) { Fail(capReason); return true; }

                    var targets = new List<int>();
                    foreach (var part in clip.Parts)
                    {
                        int idx = model.MeshContextList.IndexOf(part.SourceContext);
                        if (idx >= 0) targets.Add(idx);
                    }

                    var before = CaptureTopology(model, targets);

                    int vTotal = 0, fTotal = 0, mirrors = 0;
                    foreach (var part in clip.Parts)
                    {
                        int idx = model.MeshContextList.IndexOf(part.SourceContext);
                        if (!SelectionClipboardOps.AppendInto(model, idx, clip, new[] { part },
                                out var r, out string apReason))
                        {
                            // 途中で失敗したら、それまでに足した分を戻す。
                            before?.RestoreTo(model);
                            _viewportManager.EnterTopologyChanged(project);
                            _notifyPanels(ChangeKind.ListStructure);
                            Fail(apReason);
                            return true;
                        }
                        vTotal += r.Vertices; fTotal += r.Faces; mirrors += r.MirrorsUpdated;
                    }

                    RecordTopology(model, targets, before,
                        $"Duplicate In Place ({clip.Parts.Count} objs / {fTotal} faces / {vTotal} vertices)");
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);

                    ReportData(CommandDataJson.New()
                        .Int("objects",        clip.Parts.Count)
                        .Int("vertices",       vTotal)
                        .Int("faces",          fTotal)
                        .Int("mirrorsUpdated", mirrors)
                        .Build(),
                        targets.ToArray(), targets.Select(i => model.GetMeshContext(i).ObjectId).ToArray());
                    return true;
                }
            }
            return false;
        }

        /// <summary>貼り付け（既存オブジェクトの末尾へ）。</summary>
        private void PasteIntoObject(ProjectContext project, ModelContext model, SelectionClipboard clip, int targetMasterIndex)
        {
            int target = targetMasterIndex >= 0 ? targetMasterIndex : model.ActiveMeshIndex;
            if (model.GetMeshContext(target)?.MeshObject == null)
            { Fail("貼り付け先がありません。描画オブジェクトを選んでください"); return; }

            // 貼った部分だけが選ばれた状態にするため、他の選択中オブジェクトの要素の選択も外す。
            // その選択も Undo で戻るよう、スナップショットの対象に入れる。
            var touched = new List<int> { target };
            foreach (int i in model.SelectedDrawableMeshIndices)
                if (!touched.Contains(i)) touched.Add(i);

            var before = CaptureTopology(model, touched);

            foreach (int i in touched)
                if (i != target) model.GetMeshContext(i)?.Selection?.ClearAll();

            if (!SelectionClipboardOps.AppendInto(model, target, clip, clip.Parts, out var r, out string reason))
            {
                before?.RestoreTo(model);
                Fail(reason);
                return;
            }

            RecordTopology(model, touched, before,
                $"Paste Selection ({r.Faces} faces / {r.Vertices} vertices)");
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            var tgt = model.GetMeshContext(target);
            ReportData(CommandDataJson.New()
                .Int ("vertices",              r.Vertices)
                .Int ("faces",                 r.Faces)
                .Int ("objects",               0)
                .Int ("weightsRemoved",        r.WeightsRemoved)
                .Int ("verticesWithoutWeight", r.VerticesWithoutWeight)
                .Int ("mirrorsUpdated",        r.MirrorsUpdated)
                .Ints("masterIndices",         new[] { target })
                .Build(),
                new[] { target }, new[] { tgt.ObjectId });
        }

        /// <summary>貼り付け（写した部分ごとに新しいオブジェクト）。</summary>
        private void PasteAsNewObjects(ProjectContext project, ModelContext model, SelectionClipboard clip)
        {
            var oldSel = model.CaptureAllSelectedIndices();

            var added = SelectionClipboardOps.AddAsNewObjects(model, clip, out var r, out string reason);
            if (added.Count == 0) { Fail(reason); return; }

            // 作ったオブジェクトを選んだ状態にする。
            model.ClearMeshSelection();
            foreach (var a in added) model.SelectedDrawableMeshIndices.Add(a.Index);

            if (_undoController != null)
            {
                var newSel = model.CaptureAllSelectedIndices();
                _undoController.RecordMeshContextsAdd(added, oldSel, newSel);
            }
            _notifyPanels(ChangeKind.ListStructure);

            var indices = added.Select(a => a.Index).ToList();
            ReportData(CommandDataJson.New()
                .Int ("vertices",              r.Vertices)
                .Int ("faces",                 r.Faces)
                .Int ("objects",               added.Count)
                .Int ("weightsRemoved",        r.WeightsRemoved)
                .Int ("verticesWithoutWeight", 0)
                .Int ("mirrorsUpdated",        0)
                .Ints("masterIndices",         indices)
                .Build(),
                indices.ToArray(), added.Select(a => a.Context.ObjectId).ToArray());
        }

        private void ReportClipboard(SelectionClipboard clip)
        {
            ReportData(CommandDataJson.New()
                .Int("objects",  clip.Parts.Count)
                .Int("vertices", clip.VertexCount)
                .Int("faces",    clip.FaceCount)
                .Build());
        }

        /// <summary>対象とそのミラー側の位相と選択を取る（Undo の前）。Undo が無ければ null。</summary>
        private MultiMeshTopologySnapshot CaptureTopology(ModelContext model, List<int> targets)
        {
            var snap = new MultiMeshTopologySnapshot();
            foreach (int idx in MirrorBranchOps.CollectMirrorCaptureIndices(model, targets))
                snap.CaptureMesh(model, idx);
            return snap;
        }

        /// <summary>前後のスナップショットを 1 件の Undo として積む（DeleteSelectionTool と同じ形）。</summary>
        private void RecordTopology(ModelContext model, List<int> targets, MultiMeshTopologySnapshot before, string desc)
        {
            if (_undoController == null || before == null) return;

            var after = new MultiMeshTopologySnapshot();
            foreach (int idx in MirrorBranchOps.CollectMirrorCaptureIndices(model, targets))
                after.CaptureMesh(model, idx);

            _undoController.SetModelContext(model);
            var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
        }
    }
}
