// Editor/HierarchyIO/RemoteHierarchyReceive.cs
// ============================================================
// Pull 方式で取得したプロジェクト束を扱う共有部品。
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
        public static string DefaultDestRoot()
        {
            return Path.Combine(
                Application.persistentDataPath,
                "PolyLing",
                "RemoteHierarchy");
        }

        public static bool CanExportNow(out string reason)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isPlaying)
            {
                reason = "Playモード実行中のため書き出しできません。";
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                reason = "スクリプトコンパイル中のため書き出しできません。";
                return false;
            }

            if (EditorApplication.isUpdating)
            {
                reason = "アセットデータベース更新中のため書き出しできません。";
                return false;
            }

            reason = "";
            return true;
        }

        public static bool Expand(
            byte[] data,
            string destinationRoot,
            out string folderPath,
            out int fileCount,
            out string error)
        {
            folderPath = "";
            fileCount = 0;

            if (!RemoteFileBundle.IsBundle(data))
            {
                error = "受信データがPLRF形式ではありません。";
                return false;
            }

            if (string.IsNullOrEmpty(destinationRoot))
                destinationRoot = DefaultDestRoot();

            try
            {
                Directory.CreateDirectory(destinationRoot);
            }
            catch (Exception ex)
            {
                error = "保存先を作成できません: " + ex.Message;
                return false;
            }

            return RemoteFileBundle.Deserialize(
                data,
                destinationRoot,
                out folderPath,
                out byte _,
                out fileCount,
                out error);
        }

        public static void DrawServerChoices(
            RemoteServerConnector connector)
        {
            var choices = connector?.Choices;
            if (choices == null || choices.Count == 0)
                return;

            EditorGUILayout.LabelField(
                "接続先を選択", EditorStyles.miniBoldLabel);

            foreach (var info in choices)
            {
                string label = info.IsMaster
                    ? info.Label + "  [master]"
                    : info.Label;

                if (GUILayout.Button(label))
                {
                    connector.Choose(info);
                    GUIUtility.ExitGUI();
                }
            }

            if (GUILayout.Button("一覧を取り直す", GUILayout.Width(120)))
            {
                connector.Begin();
                GUIUtility.ExitGUI();
            }
        }
    }
}

#endif
