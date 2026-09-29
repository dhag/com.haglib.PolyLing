// PlayerCommandDispatcher.Camera.cs
// コマンドディスパッチャ：カメラ（PanelCommand.Camera.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// カメラは Viewer 側が持つので、受け口へ渡すだけ。実体は PolyLingPlayerViewerCore.CameraCommands.cs。
// モデルが無くてもカメラはあるので、合わせる 2 本以外はモデルの有無を見ない。

using System;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>カメラ系コマンドの受け口。失敗理由を返す（成功なら null）。data へ戻り値を書く。</summary>
        public Func<PanelCommand, ModelContext, CommandDataBuilder, string> OnCameraCommand;

        /// <summary>DispatchCore の分担：カメラ。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchCamera(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            bool needsModel;
            switch (cmd)
            {
                case FitCameraToModelCommand _:
                case FitCameraToSelectionCommand _:
                    needsModel = true;
                    break;
                case ResetCameraCommand _:
                case SetCameraCommand _:
                case QueryCameraCommand _:
                case SetCurrentViewCommand _:
                    needsModel = false;
                    break;
                default:
                    return false;
            }

            if (needsModel && model == null) { Fail("no current model"); return true; }
            if (OnCameraCommand == null) { Fail("camera handler not wired"); return true; }

            var data = CommandDataJson.New();
            string reason = OnCameraCommand.Invoke(cmd, model, data);
            if (reason != null) { Fail(reason); return true; }
            ReportData(data.Build());
            return true;
        }
    }
}
