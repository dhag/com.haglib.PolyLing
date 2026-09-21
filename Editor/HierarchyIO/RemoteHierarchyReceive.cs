// Editor/HierarchyIO/RemoteHierarchyReceive.cs
// ============================================================
// リモートのヒエラルキー書き出しウィンドウの共有部品（書き出し可否の判定・接続先の選択 UI）。
// ============================================================

#if UNITY_EDITOR

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Player;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorIO
{
    public static class RemoteHierarchyReceive
    {
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
