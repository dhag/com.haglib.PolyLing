// PanelCommand.View.cs
// ビューポートの種別と、カレントビューを切り替える要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【カレントビュー】
//   画面ではポインタが最後に乗ったビューポートがカレントになる。
//   ビューを指定しない操作（サーフェススナップの Current など）はカレントビューのカメラを使う。
//   setCurrentView はこれをコマンドで切り替える。ポインタが別のビューに乗れば、また切り替わる。
//
// 【ビューを持つコマンド】
//   画面座標やカメラを使うコマンド（knifeSimpleCut / edgeExtrude）は、
//   カレントビューに頼らず ViewportKind で対象のビューを持つ。
//
// 【形状ではない】
//   Undo には入れない。モデルにも保存しない。
//
// 【実体は Viewer】
//   受け口は PolyLingPlayerViewerCore.CameraCommands.cs / PolyLingPlayerViewerCore.CurrentView.cs。

namespace Poly_Ling.Data
{
    /// <summary>ビューポートの種別。</summary>
    public enum ViewportKind
    {
        /// <summary>メイン画面（透視ビュー）。</summary>
        Perspective = 0,
        /// <summary>3 面図の上面ビュー。</summary>
        Top = 1,
        /// <summary>3 面図の正面ビュー。</summary>
        Front = 2,
        /// <summary>3 面図の側面ビュー。</summary>
        Side = 3,
    }

    /// <summary>カレントビューを切り替える。</summary>
    [PLCommand(Category = "camera", Writes = PLWriteScope.None,
        Description = "カレントビュー（ビューを指定しない操作が使うビューポート）を切り替える。画面ではポインタが最後に乗ったビューがカレントになるので、ポインタが別のビューに乗るとまた切り替わる。今の値は queryCamera の currentView で読める。")]
    [PLResult("view",     PLResultKind.Text, Description = "切り替えた後のカレントビュー")]
    [PLResult("previous", PLResultKind.Text, Description = "切り替える前のカレントビュー")]
    public sealed class SetCurrentViewCommand : PanelCommand
    {
        [PLParam(Description = "カレントにするビュー。Perspective（メイン画面）/ Top / Front / Side",
                 Required = true)]
        public ViewportKind View { get; }

        public SetCurrentViewCommand(int modelIndex, ViewportKind view = ViewportKind.Perspective)
            : base(modelIndex)
        {
            View = view;
        }
    }
}
