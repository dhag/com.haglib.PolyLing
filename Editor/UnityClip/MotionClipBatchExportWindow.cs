// Editor/UnityClip/MotionClipBatchExportWindow.cs
// ============================================================
// クリップ一括変換（AnimationClip → PolyLingMotion v2 .plmotion.json）
// ------------------------------------------------------------
// ・入力: Assets 配下のフォルダ。サブフォルダを含むすべての AnimationClip
//         （FBX 等に含まれるクリップも対象。"__preview__" で始まるものは除く）。
// ・出力: 書き込み先フォルダ（SaveDest.Keys.Motion で記憶）。入力フォルダからの
//         相対サブフォルダ構成を再現し、<クリップ名>.plmotion.json で書く。
//         同じ出力先で名前が重なったときは <アセットファイル名>_<クリップ名> にする。
// ・変換: AnimationClipToMotionClip。書き出しは MotionClipSerializer.Save（検証してから書く）。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorTools;
using Poly_Ling.Motion;

namespace Poly_Ling.UnityClip.Editor
{
    public class MotionClipBatchExportWindow : EditorWindow
    {
        private string _inDir  = "";   // Assets 相対（"Assets/..."）
        private string _outDir = "";

        [MenuItem("PolyLing/UnityClip/クリップ一括変換（→ PolyLingMotion）")]
        public static void Open()
        {
            var w = GetWindow<MotionClipBatchExportWindow>(true, "クリップ一括変換（→ PolyLingMotion）", true);
            w.minSize = new Vector2(460, 200);
            // Project ウインドウで選んでいるフォルダを入力の初期値にする
            if (string.IsNullOrEmpty(w._inDir) && UnityEditor.Selection.activeObject != null)
            {
                string p = AssetDatabase.GetAssetPath(UnityEditor.Selection.activeObject);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) w._inDir = p;
            }
            w.Show();
        }

        private void OnEnable()
        {
            _outDir = EditorSaveDestField.Load(SaveDest.Keys.Motion);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("AnimationClip → PolyLingMotion v2 一括変換", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            _inDir = EditorGUILayout.TextField("入力フォルダ (Assets)", _inDir);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string start = string.IsNullOrEmpty(_inDir) ? Application.dataPath : ToAbsolute(_inDir);
                string picked = EditorUtility.OpenFolderPanel("入力フォルダ（Assets 配下）", start, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    string rel = ToAssetsRelative(picked);
                    if (rel == null) EditorUtility.DisplayDialog("エラー", "Assets 配下のフォルダを選んでください:\n" + picked, "OK");
                    else { _inDir = rel; GUI.FocusControl(null); }
                }
            }
            EditorGUILayout.EndHorizontal();

            _outDir = EditorSaveDestField.Draw(
                "書き込み先フォルダ", _outDir, SaveDest.Keys.Motion,
                "PolyLingMotion の書き込み先", "motion.plmotion.json", "json");

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "入力フォルダ下（サブフォルダ含む）のすべての AnimationClip を .plmotion.json（v2）で書き出します。\n" +
                "Transform カーブ → bones（path）、Animator カーブ → muscles。接線とラップモードを保持します。\n" +
                "サブフォルダ構成は書き込み先に再現します。同名ファイルは上書きします。",
                MessageType.Info);

            bool ready = AssetDatabase.IsValidFolder(_inDir) && !string.IsNullOrEmpty(_outDir);
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("一括変換", GUILayout.Height(30))) ExportAll();
            }
        }

        // ================================================================
        // 本体
        // ================================================================
        private void ExportAll()
        {
            try { Directory.CreateDirectory(_outDir); }
            catch (Exception ex) { EditorUtility.DisplayDialog("エラー", "書き込み先フォルダを作れません:\n" + ex.Message, "OK"); return; }

            // 入力フォルダ下のアセットを集め、含まれる AnimationClip を列挙する
            var items = new List<(AnimationClip clip, string assetPath)>();
            var paths = AssetDatabase.FindAssets("", new[] { _inDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !AssetDatabase.IsValidFolder(p))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (string p in paths)
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    if (o is AnimationClip c && !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                        items.Add((c, p));
                }
            }
            if (items.Count == 0)
            {
                EditorUtility.DisplayDialog("クリップ一括変換", "AnimationClip が見つかりません:\n" + _inDir, "OK");
                return;
            }

            int ok = 0, fail = 0, warn = 0, tanCh = 0, smpCh = 0;
            var log = new StringBuilder();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var (clip, assetPath) = items[i];
                    if (EditorUtility.DisplayCancelableProgressBar("クリップ一括変換", $"{clip.name}  ({i + 1}/{items.Count})", (float)i / items.Count))
                    {
                        log.AppendLine("中断しました。");
                        break;
                    }

                    string outPath = MakeOutPath(assetPath, clip.name, used);
                    try
                    {
                        var conv = AnimationClipToMotionClip.Convert(clip);
                        tanCh += conv.TangentChannels; smpCh += conv.SampledChannels;
                        var r = MotionClipSerializer.Save(conv.Dto, outPath);
                        if (r.Dto == null)
                        {
                            fail++;
                            log.AppendLine($"[失敗] {assetPath} : {clip.name}\n{r.FormatIssues(10)}");
                            continue;
                        }
                        ok++;
                        int nw = conv.Notes.Count + r.WarningCount;
                        if (nw > 0)
                        {
                            warn++;
                            log.AppendLine($"[警告] {assetPath} : {clip.name} → {outPath}");
                            foreach (var n in conv.Notes) log.AppendLine("  " + n);
                            if (r.WarningCount > 0) log.AppendLine(r.FormatIssues(10));
                        }
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        log.AppendLine($"[失敗] {assetPath} : {clip.name} : {ex.Message}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            string summary = $"成功 {ok} / 失敗 {fail} / 警告あり {warn}（全 {items.Count} クリップ）\n" +
                             $"接線を保持したチャンネル {tanCh} / 接線なしで書いたチャンネル {smpCh}\n{_outDir}";
            Debug.Log("[MotionClipBatchExport] " + summary + (log.Length > 0 ? "\n" + log : ""));
            EditorUtility.DisplayDialog("クリップ一括変換", summary + (log.Length > 0 ? "\n\n詳細はコンソールを参照してください。" : ""), "OK");
        }

        // 入力フォルダからの相対サブフォルダを再現した出力パス。名前が重なればアセットファイル名を前置する。
        private string MakeOutPath(string assetPath, string clipName, HashSet<string> used)
        {
            string relDir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "";
            string baseIn = _inDir.TrimEnd('/');
            relDir = relDir.Length > baseIn.Length ? relDir.Substring(baseIn.Length).TrimStart('/') : "";
            string dir = string.IsNullOrEmpty(relDir) ? _outDir : Path.Combine(_outDir, relDir);

            string path = Path.Combine(dir, Sanitize(clipName) + ".plmotion.json");
            if (!used.Add(Path.GetFullPath(path)))
            {
                path = Path.Combine(dir, Sanitize(Path.GetFileNameWithoutExtension(assetPath) + "_" + clipName) + ".plmotion.json");
                for (int n = 2; !used.Add(Path.GetFullPath(path)); n++)
                    path = Path.Combine(dir, Sanitize(Path.GetFileNameWithoutExtension(assetPath) + "_" + clipName + "_" + n) + ".plmotion.json");
            }
            return path;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) name = "clip";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static string ToAbsolute(string assetsRel)
            => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", assetsRel));

        // 絶対パス → "Assets/..."。Assets 外なら null。
        private static string ToAssetsRelative(string abs)
        {
            string full = Path.GetFullPath(abs).Replace('\\', '/').TrimEnd('/');
            string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (string.Equals(full, data, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (full.StartsWith(data + "/", StringComparison.OrdinalIgnoreCase)) return "Assets" + full.Substring(data.Length);
            return null;
        }
    }
}
