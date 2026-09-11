// PolyLingCommandGateway.cs
// 実行中の PolyLing パネルへ PanelCommand を渡すための静的な受け口。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ要るか】
//   MCP（名前付きパイプ）側のサーバ PolyLingEditorControlServer は、
//   PolyLing ウィンドウが開いていなくても Play 制御を受け付ける必要があるため、
//   モデル・ToolContext・パネルに一切依存しない設計になっている
//   （PolyLingEditorControlServer.cs の冒頭注記）。
//   そのままではコマンドを実行する経路が無い。
//
//   一方 RemoteServerCore（WebSocket 側）は
//   PolyLingPlayerServer.Initialize 経由で DispatchCommand を受け取るが、
//   その初期化は RemoteMode.Server のときしか走らない。
//   MCP を WebSocket サーバの起動状態に縛らないため、ここで受け取る。
//
// 【誰が設定するか】
//   PolyLingPlayerViewerCore が _commandDispatcher を作った直後に設定し、
//   Dispose で null に戻す。RemoteMode には依存しない。
//
// 【スレッド】
//   Dispatch はパネル側の実装であり、メインスレッド専用。
//   呼び出し側がメインスレッドへ回してから Invoke すること。
//   ここではスレッドの付け替えを行わない（どのメインスレッドへ回すかは
//   呼び出し側の文脈に属するため）。

using System;

namespace Poly_Ling.Data
{
    /// <summary>実行中のパネルへ PanelCommand を渡す静的な受け口。</summary>
    public static class PolyLingCommandGateway
    {
        /// <summary>
        /// コマンドの実行先。PolyLingPlayerViewerCore が設定し、Dispose で null に戻す。
        /// </summary>
        public static Func<PanelCommand, CommandResult> Dispatch;

        /// <summary>実行できる状態か。パネルが開いていなければ false。</summary>
        public static bool IsReady => Dispatch != null;

        /// <summary>
        /// コマンドを実行する。メインスレッドから呼ぶこと。
        /// 未登録・null・例外はすべて CommandResult.Fail で返し、外へ投げない。
        /// </summary>
        public static CommandResult Invoke(PanelCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");

            var d = Dispatch;
            if (d == null) return CommandResult.Fail("PolyLing のパネルが開いていません");

            try
            {
                return d(cmd) ?? CommandResult.Fail("ディスパッチャが結果を返しませんでした");
            }
            catch (Exception ex)
            {
                return CommandResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
