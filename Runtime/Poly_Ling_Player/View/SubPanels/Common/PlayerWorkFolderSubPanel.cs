// PlayerWorkFolderSubPanel.cs
// 作業フォルダ（PLSandbox の根）を選ぶサブパネル。
//
// 【コマンドを通さない理由】
//   作業フォルダはコマンド経由のファイル入出力を閉じ込める境界そのもの。
//   リモートから根を書き換えられると境界の意味が消えるので、
//   意図的にコマンド化していない。モデルにも属さないので Undo も所有権判定も無い。
//
// 【ダイアログで選んだフォルダの扱い】
//   RecentFileDialog.AskFolder が確定パスを RecentPaths へ書き戻し、
//   PLSandbox.AllowOnceFromDialog にも渡す。ここではその戻り値を
//   PLSandbox.SetWorkFolder へ入れて根として保存する。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Common/ に配置
// （TempMirrorSettings と同じ「セッション共通の設定物」の置き場）

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>作業フォルダの表示と選択。</summary>
    public class PlayerWorkFolderSubPanel
    {
        /// <summary>RecentFileDialog / PLSandbox が使う保存キー。</summary>
        private const string RecentKey = PLSandbox.WorkFolderKey;

        // UI 自動操作の ID は "workFolder.<下の Id>"（UiControlAttribute.cs）。
        // 作業フォルダは、外部から読み書きできる範囲そのものを決める設定なので、
        // 変える操作は利用者のダイアログを通す（UserOnly）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("path", Safety = UiSafety.ReadOnly, Description = "今の作業フォルダ")]
        private Label         _pathLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label         _statusLabel;
        [UiControl("clear", Safety = UiSafety.UserOnly, Description = "作業フォルダの指定を解除する")]
        private Button        _clearBtn;
        [UiControl("select", Safety = UiSafety.UserOnly, Description = "作業フォルダを選ぶ（フォルダ選択ダイアログを開く）")]
        private Button        _selectBtn;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            var header = new Label("作業フォルダ");
            header.style.marginTop    = 4;
            header.style.marginBottom = 3;
            _root.Add(header);

            var desc = new Label(
                "コマンド経由のファイル入出力は、このフォルダの下だけに限られます。\n" +
                "ダイアログで選んだファイルはこの制限の対象外です。");
            desc.style.fontSize   = 10;
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.color      = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            desc.style.marginBottom = 4;
            _root.Add(desc);

            _pathLabel = new Label();
            _pathLabel.style.fontSize     = 11;
            _pathLabel.style.whiteSpace   = WhiteSpace.Normal;
            _pathLabel.style.marginBottom = 4;
            _root.Add(_pathLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;

            var selectBtn = new Button(OnSelect) { text = "選択…" };
            _selectBtn = selectBtn;
            selectBtn.style.flexGrow    = 1;
            selectBtn.style.marginRight = 2;

            _clearBtn = new Button(OnClear) { text = "解除" };
            _clearBtn.style.flexGrow = 1;

            btnRow.Add(selectBtn);
            btnRow.Add(_clearBtn);
            _root.Add(btnRow);

            _statusLabel = new Label("");
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _root.Add(_statusLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
            Refresh();
        }

        /// <summary>現在の作業フォルダを読み直して表示を組み直す。</summary>
        public void Refresh()
        {
            if (_pathLabel == null) return;

            string folder = PLSandbox.WorkFolder;
            if (string.IsNullOrEmpty(folder))
            {
                _pathLabel.text        = "未設定";
                _pathLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.3f));
            }
            else
            {
                _pathLabel.text        = folder;
                _pathLabel.style.color = new StyleColor(Color.white);
            }

            if (_clearBtn != null)
                _clearBtn.SetEnabled(!string.IsNullOrEmpty(folder));
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnSelect()
        {
            SetStatus("");

            string picked = RecentFileDialog.AskFolder("作業フォルダを選ぶ", RecentKey);
            if (string.IsNullOrEmpty(picked)) return;   // キャンセル

            if (!PLSandbox.SetWorkFolder(picked, out string reason))
                SetStatus(reason);

            Refresh();
        }

        private void OnClear()
        {
            PLSandbox.ClearWorkFolder();
            SetStatus("解除しました。コマンド経由のファイル入出力はできません。");
            Refresh();
        }

        private void SetStatus(string text)
        {
            if (_statusLabel != null) _statusLabel.text = text ?? "";
        }
    }
}
