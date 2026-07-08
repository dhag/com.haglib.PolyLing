// Editor/UnityClip/AnimationClipImportWindow.cs
// ============================================================
// UnityClipDTO JSON → AnimationClip アセット 生成エディタ拡張
// ------------------------------------------------------------
//   AnimationClipExtractWindow の逆写像。JSON を UnityClipDTO に
//   復元し、Transform カーブを AnimationClip に流し込んで .anim
//   アセットとして保存する。
//
//   対応範囲（AnimationClipToDto の逆に対応）:
//     - Transform カーブのみ:
//         m_LocalPosition.{x,y,z} / m_LocalRotation.{x,y,z,w} / m_LocalScale.{x,y,z}
//     - 各キーは key.t（秒）を時刻とする線形サンプル（接線既定）。
//     - 座標系変換なし（Unity 左手系のまま）。
//     - clipType=="Humanoid" かつ bones 空 → 空クリップ＋警告。
// ============================================================

using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.UnityClip.Editor
{
    public class AnimationClipImportWindow : EditorWindow
    {
        private string _jsonPath = "";

        [MenuItem("PolyLing/UnityClip/UnityClipDTO JSON → AnimationClip")]
        public static void Open()
        {
            GetWindow<AnimationClipImportWindow>(true, "AnimationClip Import", true);
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UnityClipDTO JSON → AnimationClip", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                _jsonPath = EditorGUILayout.TextField("JSONファイル", _jsonPath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string sel = EditorUtility.OpenFilePanel("UnityClipDTO JSON を選択", _jsonPath, "json");
                    if (!string.IsNullOrEmpty(sel)) _jsonPath = sel;
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_jsonPath)))
            {
                if (GUILayout.Button("AnimationClipとして保存", GUILayout.Height(28)))
                {
                    ImportJson();
                }
            }
        }

        // ================================================================
        // 復元 → 生成 → 保存
        // ================================================================

        private void ImportJson()
        {
            if (!File.Exists(_jsonPath))
            {
                EditorUtility.DisplayDialog("エラー", "ファイルが存在しません:\n" + _jsonPath, "OK");
                return;
            }

            string json = File.ReadAllText(_jsonPath);

            UnityClipDTO dto;
            try
            {
                dto = JsonConvert.DeserializeObject<UnityClipDTO>(json);
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("エラー", "JSON の復元に失敗しました:\n" + e.Message, "OK");
                return;
            }
            if (dto == null)
            {
                EditorUtility.DisplayDialog("エラー", "JSON の復元に失敗しました（null）。", "OK");
                return;
            }

            var clip = BuildClip(dto);

            string defaultName = string.IsNullOrEmpty(dto.name) ? "clip" : dto.name;
            string savePath = EditorUtility.SaveFilePanelInProject(
                "AnimationClip を保存", defaultName, "anim", "保存先を選択");
            if (string.IsNullOrEmpty(savePath)) return;

            AssetDatabase.CreateAsset(clip, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(savePath);
            if (saved != null)
            {
                UnityEditor.Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
            }
            Debug.Log($"[AnimationClipImportWindow] 保存完了: {savePath}");
        }

        // ================================================================
        // UnityClipDTO → AnimationClip
        // ================================================================

        private static AnimationClip BuildClip(UnityClipDTO dto)
        {
            float fps = dto.frameRate > 0f ? dto.frameRate : 30f;

            var clip = new AnimationClip
            {
                name = string.IsNullOrEmpty(dto.name) ? "clip" : dto.name,
                frameRate = fps
            };

            bool hasBones   = dto.bones   != null && dto.bones.Count   > 0;
            bool hasMuscles = dto.muscles != null && dto.muscles.Count > 0;

            if (dto.clipType == "Humanoid" && !hasBones && !hasMuscles)
            {
                Debug.LogWarning(
                    "[AnimationClipImportWindow] Humanoid クリップ（bones・muscles 空）です。" +
                    "カーブが無いため空クリップを生成します。");
            }

            // 二次骨（Transform カーブ）
            if (hasBones)
            {
                foreach (var track in dto.bones)
                {
                    if (track == null || track.keys == null || track.keys.Count == 0) continue;
                    BuildTrackCurves(clip, track, fps);
                }
            }

            // Humanoid マッスル/ルート（Animator バインディング）を復元。
            //   export（AnimationClipToDto: typeof(Animator) を GetEditorCurve）と対称。
            //   track.name = propertyName（マッスル軸名・RootT/RootQ 等）。
            if (hasMuscles)
            {
                foreach (var mt in dto.muscles)
                {
                    if (mt == null || string.IsNullOrEmpty(mt.name)) continue;
                    var curve = BuildMuscleCurve(mt);
                    if (curve == null || curve.length == 0) continue;

                    var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), mt.name);
                    AnimationUtility.SetEditorCurve(clip, binding, curve);
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = dto.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        // ================================================================
        // 1 トラック分のカーブ生成
        //   pos/rot/scl それぞれ、少なくとも1キーで値を持つ成分のみ生成。
        // ================================================================

        private static void BuildTrackCurves(AnimationClip clip, UnityBoneTrackDTO track, float fps)
        {
            AnimationCurve px = null, py = null, pz = null;
            AnimationCurve rx = null, ry = null, rz = null, rw = null;
            AnimationCurve sx = null, sy = null, sz = null;

            foreach (var key in track.keys)
            {
                if (key == null) continue;
                float t = key.t;

                if (key.pos != null && key.pos.Length >= 3)
                {
                    AddKey(ref px, t, key.pos[0]);
                    AddKey(ref py, t, key.pos[1]);
                    AddKey(ref pz, t, key.pos[2]);
                }
                if (key.rot != null && key.rot.Length >= 4)
                {
                    AddKey(ref rx, t, key.rot[0]);
                    AddKey(ref ry, t, key.rot[1]);
                    AddKey(ref rz, t, key.rot[2]);
                    AddKey(ref rw, t, key.rot[3]);
                }
                if (key.scl != null && key.scl.Length >= 3)
                {
                    AddKey(ref sx, t, key.scl[0]);
                    AddKey(ref sy, t, key.scl[1]);
                    AddKey(ref sz, t, key.scl[2]);
                }
            }

            SetCurve(clip, track.path, "m_LocalPosition.x", px);
            SetCurve(clip, track.path, "m_LocalPosition.y", py);
            SetCurve(clip, track.path, "m_LocalPosition.z", pz);

            SetCurve(clip, track.path, "m_LocalRotation.x", rx);
            SetCurve(clip, track.path, "m_LocalRotation.y", ry);
            SetCurve(clip, track.path, "m_LocalRotation.z", rz);
            SetCurve(clip, track.path, "m_LocalRotation.w", rw);

            SetCurve(clip, track.path, "m_LocalScale.x", sx);
            SetCurve(clip, track.path, "m_LocalScale.y", sy);
            SetCurve(clip, track.path, "m_LocalScale.z", sz);
        }

        private static void AddKey(ref AnimationCurve c, float t, float v)
        {
            if (c == null) c = new AnimationCurve();
            c.AddKey(new Keyframe(t, v));
        }

        // muscle トラック（{t,v} 疎キー列）→ AnimationCurve
        private static AnimationCurve BuildMuscleCurve(UnityMuscleTrackDTO mt)
        {
            if (mt?.w == null || mt.w.Count == 0) return null;
            var c = new AnimationCurve();
            foreach (var k in mt.w)
            {
                if (k == null) continue;
                c.AddKey(new Keyframe(k.t, k.v));
            }
            return c;
        }

        private static void SetCurve(AnimationClip clip, string path, string prop, AnimationCurve c)
        {
            if (c == null || c.length == 0) return;
            clip.SetCurve(path, typeof(Transform), prop, c);
        }
    }
}
