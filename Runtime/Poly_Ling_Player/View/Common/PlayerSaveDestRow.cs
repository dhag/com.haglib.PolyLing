// PlayerSaveDestRow.cs
// 「書き込み先フォルダ」欄 ＋ [...] の 1 行。保存を伴う Player パネルは必ずこれを使う。
//
// 【規約】SaveDest.cs に書いた 4 項目をそのまま UI にしたもの。
//   ・欄にはフォルダだけを置く（ファイル名は置かない）
//   ・[...] は保存ダイアログを出すが、フォルダ部分だけを欄に書く。保存はしない
//   ・保存ボタンは AskSavePath() を呼ぶ。欄のフォルダを初期ディレクトリにして
//     保存ダイアログが開く（「名前を付けて保存」）
//   ・欄の内容は settings（RecentPaths）へ即書き戻す
//
// パネル側がやることは 3 つだけ。
//   1. コンストラクタで用途・キー・拡張子・既定ファイル名を渡す
//   2. Root を自分の UI に足す
//   3. 保存ボタンで AskSavePath() を呼び、返ったフルパスへ書く
//
// Runtime/Poly_Ling_Player/View/Common/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>保存先フォルダ欄（フォルダのみ）＋ [...] の 1 行。</summary>
    public sealed class PlayerSaveDestRow
    {
        private readonly string _key;
        private readonly Func<string> _defaultName;

        // UI 自動操作では、保存をダイアログで確定するパネルが UiNested("saveDest") で取り込む。
        // 欄のフォルダへダイアログなしで書き込むパネル（キャプチャ）は取り込まず、
        // FolderField / BrowseButton を作業フォルダの関門付きで自分で登録する。
        [UiControl(Ignore = true)]
        private readonly VisualElement _root;
        [UiControl("folder", Description = "書き込み先フォルダ（保存ダイアログの初期フォルダ）")]
        private readonly TextField     _folderField;
        [UiControl("browse", Safety = UiSafety.UserOnly, Description = "書き込み先フォルダを選ぶダイアログを開く")]
        private readonly Button        _browseBtn;

        /// <summary>この行の UI。パネルの親要素へ Add する。</summary>
        public VisualElement Root => _root;

        /// <summary>フォルダ欄。UI 自動操作で関門付きの項目として登録するパネルが使う。</summary>
        public TextField FolderField => _folderField;

        /// <summary>フォルダを選ぶ [...] ボタン。</summary>
        public Button BrowseButton => _browseBtn;

        /// <summary>ダイアログのタイトル。モードで変わるパネルは実行時に差し替える。</summary>
        public string DialogTitle { get; set; }

        /// <summary>拡張子（ドット無し。カンマ区切りで複数可）。モードで変わるパネルは実行時に差し替える。</summary>
        public string Extension { get; set; }

        /// <summary>欄が指しているフォルダ。空のこともある。</summary>
        public string Folder => SaveDest.NormalizeFolder(_folderField?.value ?? "");

        /// <summary>settings キー。パネルからステータス表示に使うことがある。</summary>
        public string Key => _key;

        /// <param name="label">欄の上に出す見出し。用途が分かる語にする。</param>
        /// <param name="settingsKey">SaveDest.Keys のいずれか。</param>
        /// <param name="dialogTitle">ダイアログのタイトル。</param>
        /// <param name="extension">拡張子（ドット無し。カンマ区切りで複数可）。</param>
        /// <param name="defaultName">
        /// ダイアログのファイル名欄の初期値を返す関数。
        /// 毎回呼ぶので、モデル名や時刻から作る場合もここへ入れる。null 可。
        /// </param>
        public PlayerSaveDestRow(
            string label,
            string settingsKey,
            string dialogTitle,
            string extension,
            Func<string> defaultName = null)
        {
            _key         = settingsKey;
            DialogTitle  = string.IsNullOrEmpty(dialogTitle) ? "保存先" : dialogTitle;
            Extension    = extension ?? "";
            _defaultName = defaultName;

            _root = new VisualElement();

            _root.Add(PlayerIoUiKit.SectionLabel(
                string.IsNullOrEmpty(label) ? "書き込み先フォルダ" : label));

            _folderField = new TextField();
            _folderField.SetValueWithoutNotify(SaveDest.GetFolder(_key));
            _folderField.RegisterValueChangedCallback(e => SaveDest.SetFolder(_key, e.newValue));

            _root.Add(PlayerIoUiKit.PathRow(_folderField, OnBrowse, out _browseBtn));
        }

        /// <summary>settings から読み直す。パネルの Refresh から呼ぶ。</summary>
        public void Refresh()
        {
            _folderField?.SetValueWithoutNotify(SaveDest.GetFolder(_key));
        }

        /// <summary>欄へフォルダを入れる。ファイルパスを渡してもフォルダ部分だけが入る。</summary>
        public void SetFolder(string value)
        {
            string folder = SaveDest.NormalizeFolder(value);
            _folderField?.SetValueWithoutNotify(folder);
            SaveDest.SetFolder(_key, folder);
        }

        /// <summary>
        /// 保存ボタンから呼ぶ。欄のフォルダを初期ディレクトリにして保存ダイアログを開き、
        /// 確定したフルパスを返す。キャンセル時は空文字。
        /// </summary>
        public string AskSavePath() => AskSavePath(null);

        /// <summary>
        /// 既定ファイル名をその場で差し替えて保存ダイアログを開く。
        /// コンストラクタに渡した関数より、この引数が優先される。
        /// </summary>
        public string AskSavePath(string defaultNameOverride)
        {
            string defName = !string.IsNullOrEmpty(defaultNameOverride)
                ? defaultNameOverride
                : (_defaultName != null ? _defaultName() : "");

            string path = SaveDest.AskSavePath(DialogTitle, _key, Folder, defName, Extension);
            if (!string.IsNullOrEmpty(path)) Refresh();
            return path;
        }

        // [...] は「フォルダを決めるだけ」。ここで保存はしない。
        private void OnBrowse()
        {
            string defName = _defaultName != null ? _defaultName() : "";
            string folder  = SaveDest.PickFolder(DialogTitle, _key, Folder, defName, Extension);
            if (!string.IsNullOrEmpty(folder)) _folderField.SetValueWithoutNotify(folder);
        }
    }
}
