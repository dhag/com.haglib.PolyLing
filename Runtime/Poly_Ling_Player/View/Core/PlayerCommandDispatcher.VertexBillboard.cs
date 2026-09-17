// PlayerCommandDispatcher.VertexBillboard.cs
// コマンドディスパッチャ：頂点へ藤壺（CreateVertexBillboardPlaceCommand）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【グループとして残すとき】
//   辞書名が空なら選択頂点を辞書へ保存し、その名前を控えたコマンドで実行してグループに残す。
//   選択頂点のままでは作り直しのときに同じ頂点を読めないため。
//   辞書の追加・生成・グループの追加を Undo 1 件にまとめる（辺から帯面と同じ形）。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：頂点へ藤壺。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchVertexBillboard(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case CreateVertexBillboardPlaceCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnCreateVertexBillboardPlace == null) { Fail("vertex billboard place handler not wired"); return true; }

                    int vbFallback = c.Placement.AddMode == PrimitiveAddMode.AddToExisting
                        ? c.Placement.AddTargetIndex
                        : -1;

                    // 辞書を作らない経路（グループに残さない／辞書名が指定済み）。
                    if (!c.Placement.KeepAsGroup || !string.IsNullOrEmpty(c.VertexSetName))
                    {
                        var vbBeforeIds = c.Placement.KeepAsGroup ? SnapshotObjectIds() : null;

                        string vbReason = OnCreateVertexBillboardPlace.Invoke(c);
                        if (vbReason != null) { Fail(vbReason); return true; }

                        if (c.Placement.KeepAsGroup)
                            CaptureObjectGroup(c, vbBeforeIds, vbFallback);
                        return true;
                    }

                    if (project == null) { Fail("no current project"); return true; }

                    if (!PlayerCommandTargets.MatchesSelectedDrawables(model, c.MasterIndices, out string vbSelReason))
                    { Fail(vbSelReason); return true; }

                    _undoController?.SetModelContext(model);
                    var vbListBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
                    int vbGroupsBefore = model.ObjectGroupCount;

                    string vbFail = null;
                    bool   vbSetFailed = false;

                    _undoController?.SuspendRecording();
                    try
                    {
                        if (!TryCreateVertexSelectionSets(model, c.MasterIndices, out string vbSetName, out string vbSetReason))
                        {
                            vbFail = vbSetReason;
                            vbSetFailed = true;   // 何も足していない
                        }
                        else
                        {
                            var vbCmd      = c.WithVertexSetName(vbSetName);
                            var vbBeforeId = SnapshotObjectIds();

                            vbFail = OnCreateVertexBillboardPlace.Invoke(vbCmd);
                            if (vbFail == null)
                                CaptureObjectGroup(vbCmd, vbBeforeId, vbFallback);
                        }
                    }
                    finally
                    {
                        _undoController?.ResumeRecording();
                    }

                    if (vbSetFailed) { Fail(vbFail); return true; }

                    RecordEdgeOperationUndo(model, vbListBefore, vbGroupsBefore, "頂点へ藤壺");

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);

                    if (vbFail != null) { Fail(vbFail); return true; }
                    return true;
                }
            }
            return false;
        }
    }
}
