// Editor/UnityClip/AnimationClipExtractWindow.cs
// ============================================================
// AnimationClip → UnityClipDTO JSON 書き出しエディタ拡張
// ------------------------------------------------------------
//   選択した AnimationClip を既存 AnimationClipToDto.Convert で
//   UnityClipDTO に変換し、JSON ファイルとして保存する。
//   変換ロジックは AnimationClipToDto に委譲（本ウィンドウは
//   UI と直列化・保存のみ）。
//
//   直列化: Newtonsoft.Json（com.unity.nuget.newtonsoft-json）。
//           NullValueHandling.Ignore で未設定フィールドを省く。
// ============================================================

using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.UnityClip.Editor
{
    public class AnimationClipExtractWindow : EditorWindow
    {
        // 入力クリップ
        private AnimationClip _clip;
        // 焼き込み用アバター（Humanoid Animator）。null なら bakedBones は生成しない。
        private Animator _avatar;

        [MenuItem("PolyLing/UnityClip/AnimationClip → UnityClipDTO JSON")]
        public static void Open()
        {
            GetWindow<AnimationClipExtractWindow>(true, "AnimationClip Extract", true);
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("AnimationClip → UnityClipDTO JSON", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _clip = (AnimationClip)EditorGUILayout.ObjectField(
                "AnimationClip", _clip, typeof(AnimationClip), false);

            _avatar = (Animator)EditorGUILayout.ObjectField(
                "Avatar (Humanoid Animator)", _avatar, typeof(Animator), true);
            EditorGUILayout.HelpBox(
                "Avatar を指定すると、本体ボーンのローカル回転を焼き込み bakedBones に格納します。\n" +
                "未指定なら bones（二次骨）＋ muscles（生）のみを出力します。",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_clip == null))
            {
                if (GUILayout.Button("JSONに書き出し", GUILayout.Height(28)))
                {
                    ExportJson();
                }
            }
        }

        // ================================================================
        // 変換 → 保存
        // ================================================================

        private void ExportJson()
        {
            if (_clip == null) return;

            var dto = AnimationClipToDto.Convert(_clip, _avatar);
            if (dto == null)
            {
                EditorUtility.DisplayDialog("エラー", "変換に失敗しました。", "OK");
                return;
            }

            string defaultName = string.IsNullOrEmpty(_clip.name) ? "clip" : _clip.name;
            string savePath = EditorUtility.SaveFilePanel(
                "UnityClipDTO JSON を保存", "", defaultName + ".json", "json");
            if (string.IsNullOrEmpty(savePath)) return;

            var settings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
            string json = JsonConvert.SerializeObject(dto, settings);

            File.WriteAllText(savePath, json, new UTF8Encoding(false));

            AssetDatabase.Refresh();
            Debug.Log($"[AnimationClipExtractWindow] 書き出し完了: {savePath}");
        }
    }
}
