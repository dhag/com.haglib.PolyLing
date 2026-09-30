// PlayerCommandDispatcher.EditSpace.cs
// コマンドディスパッチャ：作業空間（PanelCommand.EditSpace.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 作業空間の状態（開いている作業空間・退避したビュー）は Viewer が持つので、受け口へ渡すだけ。
// 実体は PolyLingPlayerViewerCore.EditSpace.cs。

using System;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 作業空間のコマンドの受け口。失敗理由を返す（成功なら null）。
        /// data へ戻り値を書き、masterIndices / objectIds へ変えたオブジェクトを入れる。
        /// </summary>
        public Func<PanelCommand, ModelContext, CommandDataBuilder, EditSpaceTouched, string> OnEditSpaceCommand;

        /// <summary>DispatchCore の分担：作業空間。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchEditSpace(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case OpenUvEditSpaceCommand _:
                case OpenProfileEditSpaceCommand _:
                case SetEditSpacePlaneConstraintCommand _:
                case SetEditSpacePlateUnderlayCommand _:
                case ClearEditSpacePlateUnderlayCommand _:
                case ApplyEditSpaceCommand _:
                case CancelEditSpaceCommand _:
                case CloseEditSpaceCommand _:
                case SetEditSpaceBillboardLockCommand _:
                case SetEditSpaceUnderlayCommand _:
                case SetEditSpaceImageRatioCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnEditSpaceCommand == null) { Fail("edit space handler not wired"); return true; }
                    var data    = CommandDataJson.New();
                    var touched = new EditSpaceTouched();
                    string reason = OnEditSpaceCommand.Invoke(cmd, model, data, touched);
                    if (reason != null) { Fail(reason); return true; }
                    ReportData(data.Build(), touched.MasterIndices, touched.ObjectIds);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>作業空間のコマンドが変えたオブジェクト（CommandResult に載せる）。</summary>
    public sealed class EditSpaceTouched
    {
        public int[]   MasterIndices;
        public ulong[] ObjectIds;
    }
}
