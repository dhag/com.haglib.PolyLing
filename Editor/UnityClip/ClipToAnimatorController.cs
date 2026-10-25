// Editor/UnityClip/ClipToAnimatorController.cs
// ============================================================
// AnimationClip → 同名の AnimatorController（.controller）
// ------------------------------------------------------------
// ■ 入力
//   ・AnimationClip 1 本（FBX 内のクリップも可）、またはフォルダ（サブフォルダを含む全クリップ）。
//   ・"__preview__" で始まるクリップ（FBX のプレビュー用）は対象外。
//
// ■ 出力
//   ・クリップと同じフォルダの <クリップ名>.controller。
//   ・Base Layer 1 枚、ステート 1 つ（名前はクリップ名、Motion はそのクリップ）を既定ステートにする。
//     遷移・パラメータは持たない。
//   ・ファイル名に使えない文字は '_' に置き換える。
//   ・同名の .controller が既にあれば作らずに飛ばし、警告を出す。
//
// ■ ランタイム
//   AnimatorController の生成は UnityEditor.Animations の API のため Editor 専用。
// ============================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Poly_Ling.UnityClip.Editor
{
    public static class ClipToAnimatorController
    {
        public const string PreviewPrefix = "__preview__";

        public sealed class Result
        {
            public string ClipName;
            public string ClipPath;
            public string OutputPath;
            public bool   Created;
            public bool   Skipped;    // 同名 .controller が既にあった
            public string Error;      // 失敗時のみ
        }

        // ------------------------------------------------------------
        // 入力からクリップを集める
        // ------------------------------------------------------------
        public static List<AnimationClip> CollectClips(UnityEngine.Object input)
        {
            var list = new List<AnimationClip>();
            if (input == null) return list;

            if (input is AnimationClip single)
            {
                if (!IsPreview(single)) list.Add(single);
                return list;
            }

            string folder = AssetDatabase.GetAssetPath(input);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return list;

            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files)
            {
                if (f.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)) continue;
                string p = f.Replace('\\', '/');
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    if (o is AnimationClip ac && !IsPreview(ac)) list.Add(ac);
                }
            }
            return list;
        }

        public static bool IsPreview(AnimationClip clip)
            => clip.name.StartsWith(PreviewPrefix, System.StringComparison.Ordinal);

        public static bool IsValidInput(UnityEngine.Object input)
        {
            if (input == null) return false;
            if (input is AnimationClip) return true;
            string p = AssetDatabase.GetAssetPath(input);
            return !string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p);
        }

        // ------------------------------------------------------------
        // 1 本ぶん作る
        // ------------------------------------------------------------
        public static Result Create(AnimationClip clip)
        {
            var r = new Result
            {
                ClipName = clip.name,
                ClipPath = AssetDatabase.GetAssetPath(clip),
            };

            if (string.IsNullOrEmpty(r.ClipPath))
            {
                r.Error = "アセットとして保存されていないクリップ";
                return r;
            }

            string dir = Path.GetDirectoryName(r.ClipPath).Replace('\\', '/');
            r.OutputPath = dir + "/" + SanitizeFileName(clip.name) + ".controller";

            if (File.Exists(r.OutputPath))
            {
                r.Skipped = true;
                Debug.LogWarning($"[ClipToAnimatorController] 既に存在するため飛ばしました: {r.OutputPath}（クリップ {r.ClipPath} / {r.ClipName}）");
                return r;
            }

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(r.OutputPath);
            if (ctrl == null)
            {
                r.Error = "AnimatorController を作成できませんでした";
                return r;
            }

            var sm    = ctrl.layers[0].stateMachine;
            var state = sm.AddState(clip.name);
            state.motion    = clip;
            sm.defaultState = state;

            EditorUtility.SetDirty(ctrl);
            r.Created = true;
            return r;
        }

        public static List<Result> CreateAll(List<AnimationClip> clips)
        {
            var results = new List<Result>();
            foreach (var c in clips) results.Add(Create(c));
            AssetDatabase.SaveAssets();
            return results;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(System.Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            return sb.ToString();
        }

        public static string FormatResults(List<Result> results, out int skipped)
        {
            int created = 0, failed = 0;
            skipped = 0;
            var sb = new StringBuilder();
            foreach (var r in results)
            {
                if (r.Created)      { created++; sb.AppendLine($"作成: {r.OutputPath}"); }
                else if (r.Skipped) { skipped++; sb.AppendLine($"飛ばし（既存）: {r.OutputPath}"); }
                else                { failed++;  sb.AppendLine($"失敗: {r.ClipPath} / {r.ClipName} — {r.Error}"); }
            }
            sb.Insert(0, $"クリップ {results.Count} 本 — 作成 {created} / 飛ばし {skipped} / 失敗 {failed}\n");
            return sb.ToString();
        }
    }

    // ================================================================
    // 窓
    // ================================================================
    public class ClipToAnimatorControllerWindow : EditorWindow
    {
        private UnityEngine.Object _input;
        private string  _report = "";
        private int     _skipped;
        private Vector2 _scroll;

        [MenuItem("PolyLing/UnityClip/クリップ → AnimatorController")]
        public static void Open()
        {
            var w = GetWindow<ClipToAnimatorControllerWindow>(false, "クリップ → Controller", true);
            w.minSize = new Vector2(480, 300);
            w.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("AnimationClip → 同名 AnimatorController", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            _input = EditorGUILayout.ObjectField("クリップまたはフォルダ", _input, typeof(UnityEngine.Object), false);

            bool valid = ClipToAnimatorController.IsValidInput(_input);
            if (_input != null && !valid)
                EditorGUILayout.HelpBox("AnimationClip（FBX 内のクリップも可）、またはフォルダを指定してください。", MessageType.Warning);

            EditorGUILayout.HelpBox(
                "クリップと同じ場所に <クリップ名>.controller を作ります（ステート 1 つ、既定ステート、遷移なし）。\n" +
                "同名の .controller が既にある場合は作らずに飛ばします。",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(!valid))
            {
                if (GUILayout.Button("作成", GUILayout.Height(30)))
                {
                    var clips   = ClipToAnimatorController.CollectClips(_input);
                    var results = ClipToAnimatorController.CreateAll(clips);
                    _report = ClipToAnimatorController.FormatResults(results, out _skipped);
                    Debug.Log("[ClipToAnimatorController] " + _report);
                }
            }

            if (_skipped > 0)
                EditorGUILayout.HelpBox($"同名の .controller が既にあったため {_skipped} 本を飛ばしました。", MessageType.Warning);

            if (!string.IsNullOrEmpty(_report))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();
            }
        }
    }
}
