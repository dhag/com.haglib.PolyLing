// FileDialogGuard.cs
// ファイルダイアログの再入を1か所で止める門番。
//
// 【なぜ必要か】
//   ファイル選択・保存ダイアログはネイティブのモーダルで、開いている間も
//   エディタのループは回り続ける。そこでウィンドウが Repaint すると
//   UIToolkit のパネルが保留イベントを処理し直し、ダイアログを開かせた
//   クリックがもう一度配送されて Button.clicked が再入する。
//   その結果、閉じても閉じても同じダイアログが出続ける。
//
//   呼び出し側（各パネル）に対策を書くと、ダイアログを出す箇所ぶんだけ
//   同じ判定が増えて必ず漏れる。ダイアログの入口はブリッジ実装の
//   OpenFilePanel / SaveFilePanel / OpenFolderPanel … だけなので、
//   そこを全部この門番でくぐらせる。
//
// 【使い方】
//   ブリッジ実装側:  => FileDialogGuard.Run(() => EditorUtility.SaveFilePanel(...));
//   ウィンドウ側  :  if (FileDialogGuard.IsOpen) return;  // Repaint を止める
//
// Runtime/Poly_Ling_Main/Core/EditorBridge/ に配置（IEditorBridge と同じ場所）

using System;

namespace Poly_Ling.EditorBridge
{
    /// <summary>
    /// ファイルダイアログの多重表示を防ぐ門番。
    /// 開いている間に来た要求は空文字を返して捨てる。
    /// </summary>
    public static class FileDialogGuard
    {
        /// <summary>
        /// ダイアログが開いている間 true。
        /// エディタウィンドウはこれを見て Repaint を止める
        /// （モーダル中に再描画するとクリックが再配送され、二重に開く）。
        /// </summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// ダイアログ表示を門番ごしに実行する。
        /// 既に開いているときは show を呼ばず空文字を返す。
        /// </summary>
        public static string Run(Func<string> show)
        {
            if (show == null) return string.Empty;
            if (IsOpen) return string.Empty;

            IsOpen = true;
            try { return show() ?? string.Empty; }
            finally { IsOpen = false; }
        }
    }
}
