// Editor/HierarchyIO/RemoteHierarchyReceive.cs
// ============================================================
// リモートから受け取ったプロジェクト束の扱い（共有部品）
// ============================================================
//
// 【使う窓】
//   HierarchyExportClientWindow  … サーバからの push を待ち受ける
//   HierarchyRemoteExportWindow  … クライアントから要求して受け取る
//
// 【持つもの】
//   - 書き出してよいかの判定（自分のエディタの状態）
//   - 受信ファイルの書き込み先（既定値）
//   - PLRF 束の展開
//   - 接続先が複数あるときの選択ボタン（IMGUI）
// ============================================================

#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Player;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorIO
{
    public static class RemoteHierarchyReceive
    {
        /// <summary>受信ファイルの書き込み先の既定値。</summary>
        public static string DefaultDestRoot()
        {
            return Path.Combine(Application.persistentDataPath, "PolyLing", "RemoteHierarchy");
        }

        /// <summary>
        /// いま書き出してよいか。不可なら理由を返す（可なら reason は空）。
        ///
        /// Play モード中に書き出すと、生成した GameObject は Play 終了で破棄され、
        /// プレファブ／メッシュ .asset の生成も想定外の結果になる。
        /// コンパイル中・アセット更新中も AssetDatabase 操作が不安定なため同じ扱いにする。
        /// </summary>
        public static bool CanExportNow(out string reason)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
            {
                reason = "Play モード実行中のため書き出しをスキップしました。";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                reason = "スクリプトコンパイル中のため書き出しをスキップしました。";
                return false;
            }

            if (EditorApplication.isUpdating)
            {
                reason = "アセットデータベース更新中のため書き出しをスキップしました。";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>
        /// PLRF 束を destRoot の下へ展開する。成功したら展開先フォルダとファイル数を返す。
        /// destRoot が空なら既定値を使う。
        /// </summary>
        public static bool Expand(byte[] data, string destRoot,
            out string folderPath, out int fileCount, out string error)
        {
            folderPath = "";
            fileCount  = 0;

            if (!RemoteFileBundle.IsBundle(data))
            {
                error = "PLRF ではありません";
                return false;
            }

            if (string.IsNullOrEmpty(destRoot)) destRoot = DefaultDestRoot();

            try { Directory.CreateDirectory(destRoot); }
            catch (Exception ex)
            {
                error = "保存先を作成できません: " + ex.Message;
                return false;
            }

            return RemoteFileBundle.Deserialize(
                data, destRoot, out folderPath, out byte _, out fileCount, out error);
        }

        /// <summary>
        /// 接続先が複数あるとき、選択ボタンと「一覧を取り直す」を描く。選択肢が無ければ何も描かない。
        /// </summary>
        public static void DrawServerChoices(RemoteServerConnector connector)
        {
            var choices = connector?.Choices;
            if (choices == null || choices.Count == 0) return;

            EditorGUILayout.LabelField("接続先を選択", EditorStyles.miniBoldLabel);
            foreach (var info in choices)
            {
                string label = info.IsMaster ? info.Label + "  [master]" : info.Label;
                if (GUILayout.Button(label)) { connector.Choose(info); GUIUtility.ExitGUI(); }
            }
            if (GUILayout.Button("一覧を取り直す", GUILayout.Width(120))) { connector.Begin(); GUIUtility.ExitGUI(); }
        }
    }
}

#endif
