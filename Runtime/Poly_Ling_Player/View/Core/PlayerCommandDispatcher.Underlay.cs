// PlayerCommandDispatcher.Underlay.cs
// コマンドディスパッチャ：下絵（PanelCommand.Underlay.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 画像の読み込みと表示は Viewer 側（UnderlayConfig）が持つので、受け口へ渡すだけ。
// 実体は PolyLingPlayerViewerCore.Underlay.cs。

using System;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>下絵の設定。失敗理由を返す（成功なら null）。data へ戻り値を書く。</summary>
        public Func<SetUnderlayCommand, ModelContext, CommandDataBuilder, string> OnSetUnderlay;

        /// <summary>下絵の削除。失敗理由を返す（成功なら null）。data へ戻り値を書く。</summary>
        public Func<ClearUnderlayCommand, ModelContext, CommandDataBuilder, string> OnClearUnderlay;

        /// <summary>下絵の照会。失敗理由を返す（成功なら null）。data へ戻り値を書く。</summary>
        public Func<QueryUnderlayCommand, ModelContext, CommandDataBuilder, string> OnQueryUnderlay;

        /// <summary>DispatchCore の分担：下絵。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchUnderlay(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case SetUnderlayCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSetUnderlay == null) { Fail("set underlay handler not wired"); return true; }
                    var data = CommandDataJson.New();
                    string reason = OnSetUnderlay.Invoke(c, model, data);
                    if (reason != null) { Fail(reason); return true; }
                    ReportData(data.Build());
                    return true;
                }

                case ClearUnderlayCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnClearUnderlay == null) { Fail("clear underlay handler not wired"); return true; }
                    var data = CommandDataJson.New();
                    string reason = OnClearUnderlay.Invoke(c, model, data);
                    if (reason != null) { Fail(reason); return true; }
                    ReportData(data.Build());
                    return true;
                }

                case QueryUnderlayCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnQueryUnderlay == null) { Fail("query underlay handler not wired"); return true; }
                    var data = CommandDataJson.New();
                    string reason = OnQueryUnderlay.Invoke(c, model, data);
                    if (reason != null) { Fail(reason); return true; }
                    ReportData(data.Build());
                    return true;
                }
            }
            return false;
        }
    }
}
