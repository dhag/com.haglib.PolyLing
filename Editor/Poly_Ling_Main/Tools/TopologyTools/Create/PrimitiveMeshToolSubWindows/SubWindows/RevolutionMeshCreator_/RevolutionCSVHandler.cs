// RevolutionCSVHandler.cs
// EditorUtility ファイルダイアログラッパー。
// CSV 読み書きコアは Runtime の RevolutionCSVIO に委譲。

using static Poly_Ling.Revolution.RevolutionTexts;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Revolution
{
    /// <summary>
    /// Editor 専用：ファイルダイアログを開いて CSV の読み書きを行う。
    /// 実際の入出力ロジックは RevolutionCSVIO が担当。
    /// </summary>
    public static class RevolutionCSVHandler
    {
        public static CSVLoadResult LoadFromCSVWithDialog(RevolutionParams currentParams)
        {
            string path = EditorUtility.OpenFilePanel(T("CSVLoadTitle"), "", "csv");
            if (string.IsNullOrEmpty(path))
                return new CSVLoadResult { Success = false, ErrorMessage = T("Cancelled") };

            var result = RevolutionCSVIO.Load(path, currentParams);

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                EditorUtility.DisplayDialog(T("Error"), T("CSVLoadError", result.ErrorMessage), T("OK"));

            return result;
        }

        public static bool SaveToCSVWithDialog(List<Vector2> profile, RevolutionParams p)
        {
            string path = EditorUtility.SaveFilePanel(T("CSVSaveTitle"), "", "profile.csv", "csv");
            if (string.IsNullOrEmpty(path)) return false;

            bool ok = RevolutionCSVIO.Save(path, profile, p);
            if (!ok)
                EditorUtility.DisplayDialog(T("Error"), T("CSVSaveError", ""), T("OK"));
            return ok;
        }
    }
}
