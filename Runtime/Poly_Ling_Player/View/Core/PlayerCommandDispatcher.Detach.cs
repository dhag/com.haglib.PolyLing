// PlayerCommandDispatcher.Detach.cs
// コマンドディスパッチャ：頂点の分離（PanelCommand.Detach.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 実処理（DetachVerticesOps）・Undo・再描画は Viewer が持つので、受け口へ渡すだけ。
// 実体は PolyLingPlayerViewerCore.Detach.cs。

using System;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>頂点の分離の受け口。失敗理由を返す（成功なら null）。data へ戻り値を書く。</summary>
        public Func<DetachVerticesCommand, CommandDataBuilder, EditSpaceTouched, string> OnDetachVertices;

        /// <summary>DispatchCore の分担：頂点の分離。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchDetach(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            if (!(cmd is DetachVerticesCommand c)) return false;
            if (model == null) { Fail("no current model"); return true; }
            if (OnDetachVertices == null) { Fail("detach vertices handler not wired"); return true; }

            var data    = CommandDataJson.New();
            var touched = new EditSpaceTouched();
            string reason = OnDetachVertices.Invoke(c, data, touched);
            if (reason != null) { Fail(reason); return true; }
            ReportData(data.Build(), touched.MasterIndices, touched.ObjectIds);
            return true;
        }
    }
}
