// EditorSaveDestField.cs
// Editor 窓（IMGUI）用の「書き込み先フォルダ」欄 ＋ [...]。
// Player 側の PlayerSaveDestRow と同じ規約を IMGUI で実現する。
//
// 【規約】SaveDest.cs 参照。
//   ・欄にはフォルダだけを置く
//   ・[...] は保存ダイアログを出し、フォルダ部分だけを欄に書く。保存はしない
//   ・保存ボタンは SaveDest.AskSavePath を呼ぶ（「名前を付けて保存」）
//   ・欄の内容は settings（RecentPaths）へ書き戻す。EditorPrefs は使わない
//     （Player と Editor で保存先の覚え方が分かれていると、同じ操作なのに
//       窓を替えると保存先が変わる。設定の置き場を 1 つにする）
//
// Editor/Common/ に配置

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.EditorTools
{
    /// <summary>Editor 窓用の保存先フォルダ欄。</summary>
    public static class EditorSaveDestField
    {
        private const float BrowseWidth = 30f;

        /// <summary>
        /// 「ラベル ＋ フォルダ欄 ＋ [...]」を 1 行描く。
        /// 戻り値は確定後のフォルダ。呼び出し側はこれを自分のフィールドへ代入する。
        /// </summary>
        /// <param name="label">欄のラベル。</param>
        /// <param name="folder">現在のフォルダ。</param>
        /// <param name="settingsKey">SaveDest.Keys のいずれか。</param>
        /// <param name="dialogTitle">[...] で開くダイアログのタイトル。</param>
        /// <param name="defaultName">ダイアログのファイル名欄の初期値。</param>
        /// <param name="extension">拡張子（ドット無し）。</param>
        /// <param name="mapPicked">
        /// [...] で選ばれたフォルダを加工する関数。Assets 相対へ直す窓などが使う。
        /// null を返すと採用しない（拒否）。null 可。
        /// </param>
        public static string Draw(
            string label,
            string folder,
            string settingsKey,
            string dialogTitle,
            string defaultName,
            string extension,
            Func<string, string> mapPicked = null)
        {
            string current = folder ?? "";

            EditorGUILayout.BeginHorizontal();
            string edited = EditorGUILayout.TextField(label, current);

            if (GUILayout.Button("...", GUILayout.Width(BrowseWidth)))
            {
                // [...] は「フォルダを決めるだけ」。ここで保存はしない。
                string picked = EditorUtility.SaveFilePanel(
                    string.IsNullOrEmpty(dialogTitle) ? "書き込み先" : dialogTitle,
                    SaveDest.NormalizeFolder(edited),
                    defaultName ?? "",
                    extension ?? "");

                if (!string.IsNullOrEmpty(picked))
                {
                    string pickedFolder = SaveDest.NormalizeFolder(picked);
                    if (mapPicked != null) pickedFolder = mapPicked(pickedFolder);

                    if (!string.IsNullOrEmpty(pickedFolder))
                    {
                        edited = pickedFolder;
                        GUI.FocusControl(null);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(edited, current, StringComparison.Ordinal))
                SaveDest.SetFolder(settingsKey, edited);

            return edited;
        }

        /// <summary>settings に覚えているフォルダ。窓の OnEnable から呼ぶ。</summary>
        public static string Load(string settingsKey, string fallback = "")
            => SaveDest.GetFolder(settingsKey, fallback);
    }
}
