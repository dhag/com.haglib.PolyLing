// ヒエラルキー上のGameObjectからUnity Humanoid Avatarを作成するパネル
// HumanoidBoneMapping.csvで対応付け

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Localization;

namespace Poly_Ling.MISC
{
    /// <summary>
    /// PMX Avatar作成パネル
    /// </summary>
    public class AvatarCreatorPanel : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Avatar Creator", ["ja"] = "アバター作成" },
            ["RootObject"] = new() { ["en"] = "Root Object", ["ja"] = "ルートオブジェクト" },
            ["SelectFromHierarchy"] = new() { ["en"] = "Select root GameObject from Hierarchy", ["ja"] = "ヒエラルキーからルートGameObjectを選択" },
            ["MappingFile"] = new() { ["en"] = "Bone Mapping CSV", ["ja"] = "ボーン対応表CSV" },
            ["DragDropMapping"] = new() { ["en"] = "Drag & Drop Mapping CSV here", ["ja"] = "対応表CSVをここにドロップ" },
            ["Preview"] = new() { ["en"] = "Bone Mapping Preview", ["ja"] = "ボーンマッピング確認" },
            ["MappedBones"] = new() { ["en"] = "Mapped", ["ja"] = "マッピング済" },
            ["UnmappedRequired"] = new() { ["en"] = "Missing Required", ["ja"] = "必須が未設定" },
            ["Create"] = new() { ["en"] = "Create Avatar", ["ja"] = "アバター作成" },
            ["CreateSuccess"] = new() { ["en"] = "Avatar Created!", ["ja"] = "アバター作成完了！" },
            ["CreateFailed"] = new() { ["en"] = "Creation Failed: {0}", ["ja"] = "作成失敗: {0}" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // 必須ボーン
        // ================================================================

        private static readonly HashSet<string> RequiredBones = new()
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "LeftUpperArm", "LeftLowerArm", "LeftHand",
            "RightUpperArm", "RightLowerArm", "RightHand",
            "LeftUpperLeg", "LeftLowerLeg", "LeftFoot",
            "RightUpperLeg", "RightLowerLeg", "RightFoot"
        };

        // ================================================================
        // フィールド
        // ================================================================

        private GameObject _rootObject;
        private string _mappingFilePath = "";
        private Dictionary<string, string> _boneMapping; // Unity名 → PMX名
        private Vector2 _scrollPosition;
        private bool _foldPreview = true;

        // 結果
        private Avatar _lastCreatedAvatar;

        // ================================================================
        // Open
        // ================================================================

        //[MenuItem("Poly_Ling/Tools/Avatar Creator...")]
        public static void ShowWindow()
        {
            var window = GetWindow<AvatarCreatorPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.Space(5);

            // ルートオブジェクト選択
            EditorGUILayout.LabelField(T("RootObject"), EditorStyles.boldLabel);
            _rootObject = (GameObject)EditorGUILayout.ObjectField(_rootObject, typeof(GameObject), true);

            if (_rootObject == null)
            {
                EditorGUILayout.HelpBox(T("SelectFromHierarchy"), MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // マッピングCSV
            EditorGUILayout.LabelField(T("MappingFile"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _mappingFilePath = EditorGUILayout.TextField(_mappingFilePath);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_mappingFilePath)
                        ? Application.dataPath
                        : Path.GetDirectoryName(_mappingFilePath);

                    string path = EditorUtility.OpenFilePanel("Select Bone Mapping CSV", dir, "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _mappingFilePath = path;
                        LoadMapping();
                    }
                }
            }

            DrawDropArea(T("DragDropMapping"), path =>
            {
                _mappingFilePath = path;
                LoadMapping();
            });

            EditorGUILayout.Space(10);

            // プレビュー
            DrawPreviewSection();

            EditorGUILayout.Space(10);

            // 作成ボタン
            DrawCreateButton();

            EditorGUILayout.EndScrollView();
        }

        private void DrawDropArea(string message, Action<string> onDrop)
        {
            var dropArea = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, message, EditorStyles.helpBox);

            var evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                switch (evt.type)
                {
                    case EventType.DragUpdated:
                        if (DragAndDrop.paths.Length > 0 && DragAndDrop.paths[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        {
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                            evt.Use();
                        }
                        break;

                    case EventType.DragPerform:
                        if (DragAndDrop.paths.Length > 0)
                        {
                            string path = DragAndDrop.paths[0];
                            if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                            {
                                DragAndDrop.AcceptDrag();
                                onDrop(path);
                                evt.Use();
                            }
                        }
                        break;
                }
            }
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void DrawPreviewSection()
        {
            if (_rootObject == null || _boneMapping == null) return;

            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            EditorGUI.indentLevel++;

            var foundBones = FindBones();
            int mappedCount = 0;
            var missingRequired = new List<string>();

            foreach (var kvp in _boneMapping)
            {
                string unityName = kvp.Key;
                string pmxName = kvp.Value;

                if (string.IsNullOrEmpty(pmxName)) continue;

                bool found = foundBones.ContainsKey(unityName);
                if (found) mappedCount++;

                if (!found && RequiredBones.Contains(unityName))
                {
                    missingRequired.Add($"{unityName} → {pmxName}");
                }
            }

            EditorGUILayout.LabelField(T("MappedBones"), $"{mappedCount} / {_boneMapping.Count}");

            if (missingRequired.Count > 0)
            {
                EditorGUILayout.Space(3);
                GUI.color = new Color(1f, 0.7f, 0.5f);
                EditorGUILayout.LabelField(T("UnmappedRequired"), EditorStyles.boldLabel);
                foreach (var bone in missingRequired)
                {
                    EditorGUILayout.LabelField($"  ✗ {bone}", EditorStyles.miniLabel);
                }
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.green;
                EditorGUILayout.LabelField("✓ All required bones found!", EditorStyles.boldLabel);
                GUI.color = Color.white;
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // 作成ボタン
        // ================================================================

        private void DrawCreateButton()
        {
            bool canCreate = _rootObject != null && _boneMapping != null;

            EditorGUI.BeginDisabledGroup(!canCreate);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            if (GUILayout.Button(T("Create"), buttonStyle))
            {
                ExecuteCreate();
            }

            EditorGUI.EndDisabledGroup();

            // 結果表示
            if (_lastCreatedAvatar != null)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(T("CreateSuccess"), MessageType.Info);
                EditorGUILayout.ObjectField("Avatar", _lastCreatedAvatar, typeof(Avatar), false);
            }
        }

        // ================================================================
        // ファイル読み込み
        // ================================================================

        private void LoadMapping()
        {
            try
            {
                _boneMapping = new Dictionary<string, string>();
                var lines = File.ReadAllLines(_mappingFilePath, Encoding.UTF8);

                bool isHeader = true;
                foreach (var line in lines)
                {
                    if (isHeader)
                    {
                        isHeader = false;
                        continue;
                    }

                    var parts = line.Split(',');
                    if (parts.Length >= 2)
                    {
                        string unityName = parts[0].Trim();
                        string pmxName = parts[1].Trim();

                        if (!string.IsNullOrEmpty(unityName))
                        {
                            _boneMapping[unityName] = pmxName;
                        }
                    }
                }

                Debug.Log($"[AvatarCreator] Loaded mapping: {_boneMapping.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarCreator] Failed to load mapping: {ex.Message}");
                _boneMapping = null;
            }
            Repaint();
        }

        // ================================================================
        // ボーン検索
        // ================================================================

        private Dictionary<string, Transform> FindBones()
        {
            var result = new Dictionary<string, Transform>();
            if (_rootObject == null || _boneMapping == null) return result;

            // PMX名 → Transform のマップを作成
            var allTransforms = _rootObject.GetComponentsInChildren<Transform>(true);
            var nameToTransform = new Dictionary<string, Transform>();

            foreach (var t in allTransforms)
            {
                // 同名がある場合は最初のものを使用
                if (!nameToTransform.ContainsKey(t.name))
                {
                    nameToTransform[t.name] = t;
                }
            }

            // Unity名 → Transform に変換
            foreach (var kvp in _boneMapping)
            {
                string unityName = kvp.Key;
                string pmxName = kvp.Value;

                if (!string.IsNullOrEmpty(pmxName) && nameToTransform.TryGetValue(pmxName, out var transform))
                {
                    result[unityName] = transform;
                }
            }

            return result;
        }

        // ================================================================
        // Avatar作成
        // ================================================================

        private void ExecuteCreate()
        {
            // 保存先選択
            string defaultName = _rootObject.name + "_Avatar.asset";
            string savePath = EditorUtility.SaveFilePanelInProject("Save Avatar", defaultName, "asset", "Save Avatar Asset");

            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                var foundBones = FindBones();

                // デバッグ: 見つかったボーンを出力
                Debug.Log($"[AvatarCreator] Found {foundBones.Count} bones:");
                foreach (var kvp in foundBones)
                {
                    Debug.Log($"  {kvp.Key} → {kvp.Value.name} (path: {GetTransformPath(kvp.Value)})");
                }

                // 必須ボーンのチェック
                var missingRequired = new List<string>();
                foreach (var required in RequiredBones)
                {
                    if (!foundBones.ContainsKey(required))
                    {
                        missingRequired.Add(required);
                    }
                }

                if (missingRequired.Count > 0)
                {
                    Debug.LogError($"[AvatarCreator] Missing required bones: {string.Join(", ", missingRequired)}");
                }

                // HumanBone配列を作成
                var humanBones = new List<HumanBone>();

                foreach (var kvp in foundBones)
                {
                    string unityName = kvp.Key;
                    Transform boneTransform = kvp.Value;

                    var humanBone = new HumanBone
                    {
                        humanName = unityName,
                        boneName = boneTransform.name,
                        limit = new HumanLimit { useDefaultValues = true }
                    };

                    humanBones.Add(humanBone);
                    Debug.Log($"[AvatarCreator] HumanBone: {unityName} = {boneTransform.name}");
                }

                // SkeletonBone配列を作成（全Transform）
                var allTransforms = _rootObject.GetComponentsInChildren<Transform>(true);
                var skeletonBones = new List<SkeletonBone>();

                Debug.Log($"[AvatarCreator] Building skeleton from {allTransforms.Length} transforms");

                foreach (var t in allTransforms)
                {
                    var skeletonBone = new SkeletonBone
                    {
                        name = t.name,
                        position = t.localPosition,
                        rotation = t.localRotation,
                        scale = t.localScale
                    };
                    skeletonBones.Add(skeletonBone);
                }

                // HumanDescription作成
                var humanDescription = new HumanDescription
                {
                    human = humanBones.ToArray(),
                    skeleton = skeletonBones.ToArray(),
                    upperArmTwist = 0.5f,
                    lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f,
                    lowerLegTwist = 0.5f,
                    armStretch = 0.05f,
                    legStretch = 0.05f,
                    feetSpacing = 0f,
                    hasTranslationDoF = false
                };

                Debug.Log($"[AvatarCreator] HumanDescription: {humanBones.Count} humanBones, {skeletonBones.Count} skeletonBones");

                // Avatar作成
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(_rootObject, humanDescription);

                Debug.Log($"[AvatarCreator] Avatar built: isNull={avatar == null}, isHuman={avatar?.isHuman}, isValid={avatar?.isValid}");

                if (avatar == null)
                {
                    throw new Exception("AvatarBuilder.BuildHumanAvatar returned null");
                }

                if (!avatar.isValid)
                {
                    // 無効なAvatarでも一応保存して確認できるようにする
                    Debug.LogWarning("[AvatarCreator] Avatar is not valid, but will save anyway for inspection");
                }

                if (!avatar.isHuman)
                {
                    Debug.LogWarning("[AvatarCreator] Avatar is not humanoid");
                }

                avatar.name = Path.GetFileNameWithoutExtension(savePath);

                // アセット保存
                AssetDatabase.CreateAsset(avatar, savePath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                _lastCreatedAvatar = avatar;

                Debug.Log($"[AvatarCreator] Avatar saved: {savePath}");
                Debug.Log($"[AvatarCreator] isHuman: {avatar.isHuman}, isValid: {avatar.isValid}");

                // 作成したアセットを選択
                UnityEditor.Selection.activeObject = avatar;
                EditorGUIUtility.PingObject(avatar);

                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarCreator] Failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog(T("WindowTitle"), T("CreateFailed", ex.Message), "OK");
            }
        }

        private string GetTransformPath(Transform t)
        {
            if (t.parent == null) return t.name;
            return GetTransformPath(t.parent) + "/" + t.name;
        }
    }
}