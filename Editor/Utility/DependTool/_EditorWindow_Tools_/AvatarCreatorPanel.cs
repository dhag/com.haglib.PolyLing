// AvatarCreatorPanel.cs
// Humanoid Avatar 作成パネル V2 — UIElements版
// EditorWindow 直接継承のまま（ToolContext/PanelContext 不使用）

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Localization;

namespace Poly_Ling.MISC
{
    public class AvatarCreatorPanel : EditorWindow
    {
        // ================================================================
        // UXML/USS パス
        // ================================================================

        private const string UxmlPackagePath =
            "Packages/com.haglib.polyling/Editor/Utility/DependTool/_EditorWindow_Tools_/AvatarCreatorPanel.uxml";
        private const string UxmlAssetsPath =
            "Assets/Editor/Utility/DependTool/_EditorWindow_Tools_/AvatarCreatorPanel.uxml";
        private const string UssPackagePath =
            "Packages/com.haglib.polyling/Editor/Utility/DependTool/_EditorWindow_Tools_/AvatarCreatorPanel.uss";
        private const string UssAssetsPath =
            "Assets/Editor/Utility/DependTool/_EditorWindow_Tools_/AvatarCreatorPanel.uss";

        // ================================================================
        // ローカライズ
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]          = new() { ["en"] = "Avatar Creator",             ["ja"] = "アバター作成" },
            ["RootObject"]           = new() { ["en"] = "Root Object",                ["ja"] = "ルートオブジェクト" },
            ["SelectFromHierarchy"]  = new() { ["en"] = "Select root from Hierarchy", ["ja"] = "ヒエラルキーからルートを選択" },
            ["MappingFile"]          = new() { ["en"] = "Bone Mapping CSV",           ["ja"] = "ボーン対応表CSV" },
            ["FuzzyMatch"]           = new() { ["en"] = "Fuzzy Match",                ["ja"] = "あいまい検索" },
            ["Preview"]              = new() { ["en"] = "Bone Mapping Preview",       ["ja"] = "ボーンマッピング確認" },
            ["MappedBones"]          = new() { ["en"] = "Mapped",                     ["ja"] = "マッピング済" },
            ["UnmappedRequired"]     = new() { ["en"] = "Missing Required Bones:",    ["ja"] = "必須ボーンが未設定:" },
            ["Create"]               = new() { ["en"] = "Create Avatar",              ["ja"] = "アバター作成" },
            ["CreateSuccess"]        = new() { ["en"] = "Avatar Created!",            ["ja"] = "アバター作成完了！" },
            ["CreateFailed"]         = new() { ["en"] = "Creation Failed: {0}",       ["ja"] = "作成失敗: {0}" },
            ["MissingRequiredWarning"] = new()
            {
                ["en"] = "Required bones missing:\n{0}\n\nContinue anyway?",
                ["ja"] = "必須ボーンが見つかりません:\n{0}\n\nこのまま続行しますか？"
            },
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
        private Dictionary<string, List<string>> _boneMapping;
        private bool _fuzzyMatch = true;
        private Avatar _lastCreatedAvatar;

        // UIElements 参照
        private ObjectField _rootField;
        private TextField _csvField;
        private Toggle _fuzzyToggle;
        private Foldout _previewFoldout;
        private Label _previewMapped;
        private Label _previewMissingHeader;
        private VisualElement _previewMissingList;
        private Label _previewOkLabel;
        private Button _createBtn;
        private HelpBox _resultBox;
        private ObjectField _avatarField;

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("Tools/Utility/DependTool/Avatar Creator...")]
        public static void ShowWindow()
        {
            var w = GetWindow<AvatarCreatorPanel>();
            w.titleContent = new GUIContent(T("WindowTitle"));
            w.minSize = new Vector2(400, 500);
            w.Show();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = TryLoad<VisualTreeAsset>(UxmlPackagePath, UxmlAssetsPath);
            if (visualTree == null) { root.Add(new Label("UXML not found")); return; }
            visualTree.CloneTree(root);

            var styleSheet = TryLoad<StyleSheet>(UssPackagePath, UssAssetsPath);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);

            _rootField        = root.Q<ObjectField>("root-field");
            _csvField         = root.Q<TextField>("csv-field");
            _fuzzyToggle      = root.Q<Toggle>("fuzzy-toggle");
            _previewFoldout   = root.Q<Foldout>("preview-foldout");
            _previewMapped    = root.Q<Label>("preview-mapped");
            _previewMissingHeader = root.Q<Label>("preview-missing-header");
            _previewMissingList   = root.Q<VisualElement>("preview-missing-list");
            _previewOkLabel   = root.Q<Label>("preview-ok");
            _createBtn        = root.Q<Button>("create-btn");
            _resultBox        = root.Q<HelpBox>("result-box");
            _avatarField      = root.Q<ObjectField>("avatar-field");

            // ラベルテキスト
            SetLabel(root, "label-root",    T("RootObject"));
            SetLabel(root, "label-mapping", T("MappingFile"));
            SetLabel(root, "label-preview", T("Preview"));
            if (_fuzzyToggle  != null) _fuzzyToggle.label  = T("FuzzyMatch");
            if (_createBtn    != null) _createBtn.text      = T("Create");

            // ルートオブジェクト
            if (_rootField != null)
            {
                _rootField.objectType = typeof(GameObject);
                _rootField.allowSceneObjects = true;
                _rootField.RegisterValueChangedCallback(evt =>
                {
                    _rootObject = evt.newValue as GameObject;
                    RefreshPreview();
                    RefreshCreateBtn();
                });
            }

            // CSVフィールド
            if (_csvField != null)
            {
                _csvField.RegisterValueChangedCallback(evt =>
                {
                    _mappingFilePath = evt.newValue;
                });
                // D&D
                _csvField.RegisterCallback<DragUpdatedEvent>(OnCsvDragUpdated);
                _csvField.RegisterCallback<DragPerformEvent>(OnCsvDragPerform);
            }

            root.Q<Button>("btn-browse-csv")?.RegisterCallback<ClickEvent>(_ => BrowseCSV());
            root.Q<Button>("btn-load-csv")?.RegisterCallback<ClickEvent>(_ =>
            {
                LoadMapping(_mappingFilePath);
                RefreshPreview();
                RefreshCreateBtn();
            });

            // あいまい検索
            _fuzzyToggle?.RegisterValueChangedCallback(evt =>
            {
                _fuzzyMatch = evt.newValue;
                RefreshPreview();
            });

            // 作成ボタン
            _createBtn?.RegisterCallback<ClickEvent>(_ => ExecuteCreate());

            // アバターフィールド
            if (_avatarField != null)
            {
                _avatarField.objectType = typeof(Avatar);
                _avatarField.allowSceneObjects = false;
            }

            RefreshAll();
        }

        private static T TryLoad<T>(string pkg, string assets) where T : UnityEngine.Object
        {
            T v = AssetDatabase.LoadAssetAtPath<T>(pkg);
            return v != null ? v : AssetDatabase.LoadAssetAtPath<T>(assets);
        }

        private static void SetLabel(VisualElement root, string name, string text)
        {
            var l = root.Q<Label>(name);
            if (l != null) l.text = text;
        }

        // ================================================================
        // D&D
        // ================================================================

        private void OnCsvDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.paths.Length > 0 &&
                DragAndDrop.paths[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }

        private void OnCsvDragPerform(DragPerformEvent evt)
        {
            if (DragAndDrop.paths.Length == 0) return;
            string path = DragAndDrop.paths[0];
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return;
            DragAndDrop.AcceptDrag();
            _mappingFilePath = path;
            if (_csvField != null) _csvField.SetValueWithoutNotify(path);
            LoadMapping(path);
            RefreshPreview();
            RefreshCreateBtn();
        }

        private void BrowseCSV()
        {
            string dir = string.IsNullOrEmpty(_mappingFilePath)
                ? Application.dataPath : Path.GetDirectoryName(_mappingFilePath);
            string path = EditorUtility.OpenFilePanel("Select Bone Mapping CSV", dir, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _mappingFilePath = path;
            if (_csvField != null) _csvField.SetValueWithoutNotify(path);
            LoadMapping(path);
            RefreshPreview();
            RefreshCreateBtn();
        }

        // ================================================================
        // RefreshAll
        // ================================================================

        private void RefreshAll()
        {
            if (_fuzzyToggle != null) _fuzzyToggle.SetValueWithoutNotify(_fuzzyMatch);
            RefreshPreview();
            RefreshCreateBtn();
            RefreshResult();
        }

        private void RefreshPreview()
        {
            if (_previewFoldout == null) return;
            bool canPreview = _rootObject != null && _boneMapping != null;
            _previewFoldout.style.display = canPreview ? DisplayStyle.Flex : DisplayStyle.None;
            if (!canPreview) return;

            var found = FindBones();
            int mappedCount = 0;
            var missing = new List<string>();

            foreach (var kv in _boneMapping)
            {
                if (found.ContainsKey(kv.Key)) mappedCount++;
                else if (RequiredBones.Contains(kv.Key))
                    missing.Add($"{kv.Key} ({string.Join(", ", kv.Value)})");
            }

            if (_previewMapped != null)
                _previewMapped.text = $"{T("MappedBones")}: {mappedCount} / {_boneMapping.Count}";

            bool hasMissing = missing.Count > 0;
            if (_previewMissingHeader != null)
                _previewMissingHeader.style.display = hasMissing ? DisplayStyle.Flex : DisplayStyle.None;
            if (_previewMissingList != null)
            {
                _previewMissingList.style.display = hasMissing ? DisplayStyle.Flex : DisplayStyle.None;
                _previewMissingList.Clear();
                foreach (var b in missing)
                    _previewMissingList.Add(new Label($"✗ {b}") { style = { fontSize = 10 } });
            }
            if (_previewOkLabel != null)
                _previewOkLabel.style.display = hasMissing ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void RefreshCreateBtn()
        {
            if (_createBtn == null) return;
            _createBtn.SetEnabled(_rootObject != null && _boneMapping != null);
        }

        private void RefreshResult()
        {
            bool hasResult = _lastCreatedAvatar != null;
            if (_resultBox  != null) _resultBox.style.display  = hasResult ? DisplayStyle.Flex : DisplayStyle.None;
            if (_avatarField != null)
            {
                _avatarField.style.display = hasResult ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasResult) _avatarField.SetValueWithoutNotify(_lastCreatedAvatar);
            }
        }

        // ================================================================
        // マッピング読み込み
        // ================================================================

        private void LoadMapping(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                _boneMapping = new Dictionary<string, List<string>>();
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                bool isHeader = true;
                foreach (var line in lines)
                {
                    if (isHeader) { isHeader = false; continue; }
                    if (line.StartsWith("//")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 1) continue;
                    string unityName = parts[0].Trim();
                    if (string.IsNullOrEmpty(unityName)) continue;
                    var aliases = new List<string> { unityName };
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string alias = parts[i].Trim();
                        if (!string.IsNullOrEmpty(alias)) aliases.Add(alias);
                    }
                    _boneMapping[unityName] = aliases;
                }
                Debug.Log($"[AvatarCreator] Loaded mapping: {_boneMapping.Count} entries");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarCreator] Failed to load mapping: {ex.Message}");
                _boneMapping = null;
            }
        }

        // ================================================================
        // ボーン検索
        // ================================================================

        private Dictionary<string, Transform> FindBones()
        {
            var result = new Dictionary<string, Transform>();
            if (_rootObject == null || _boneMapping == null) return result;
            var allTransforms = _rootObject.GetComponentsInChildren<Transform>(true);
            foreach (var kv in _boneMapping)
            {
                var t = FindBoneByAliases(allTransforms, kv.Value);
                if (t != null) result[kv.Key] = t;
            }
            return result;
        }

        private Transform FindBoneByAliases(Transform[] all, List<string> aliases)
        {
            var names = all.Select(t => t.name).ToList();
            int idx = HumanoidBoneMapping.FindBoneByAliases(names, aliases, _fuzzyMatch);
            return idx >= 0 ? all[idx] : null;
        }

        // ================================================================
        // Avatar作成
        // ================================================================

        private void ExecuteCreate()
        {
            string defaultName = _rootObject.name + "_Avatar.asset";
            string savePath = EditorUtility.SaveFilePanelInProject(
                "Save Avatar", defaultName, "asset", "Save Avatar Asset");
            if (string.IsNullOrEmpty(savePath)) return;

            try
            {
                var found = FindBones();

                // 必須ボーンチェック
                var missing = RequiredBones.Where(b => !found.ContainsKey(b)).ToList();
                if (missing.Count > 0)
                {
                    string msg = T("MissingRequiredWarning", string.Join("\n", missing));
                    if (!EditorUtility.DisplayDialog(T("WindowTitle"), msg, "OK", "Cancel")) return;
                }

                _lastCreatedAvatar = BuildAndSaveAvatar(_rootObject, found, savePath);

                if (_lastCreatedAvatar != null)
                {
                    UnityEditor.Selection.activeObject = _lastCreatedAvatar;
                    EditorGUIUtility.PingObject(_lastCreatedAvatar);
                    if (_resultBox != null)
                    {
                        _resultBox.text = T("CreateSuccess");
                        _resultBox.messageType = HelpBoxMessageType.Info;
                    }
                }
                else
                {
                    if (_resultBox != null)
                    {
                        _resultBox.text = T("CreateFailed", "BuildAndSaveAvatar returned null");
                        _resultBox.messageType = HelpBoxMessageType.Error;
                    }
                }

                RefreshResult();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarCreator] Failed: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog(T("WindowTitle"), T("CreateFailed", ex.Message), "OK");
            }
        }

        // ================================================================
        // 静的 API（外部から Avatar 生成）
        // ================================================================

        public static Avatar BuildAndSaveAvatar(
            GameObject rootObject,
            Dictionary<string, Transform> boneMapping,
            string savePath)
        {
            if (rootObject == null || boneMapping == null || boneMapping.Count == 0) return null;

            try
            {
                var allTransforms = rootObject.GetComponentsInChildren<Transform>(true);
                var skeletonBones = allTransforms.Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                }).ToList();

                var skeletonNames = new HashSet<string>(allTransforms.Select(t => t.name));
                var valid = new Dictionary<string, Transform>();
                var skipped = new List<string>();

                foreach (var kv in boneMapping)
                {
                    var tf = kv.Value;
                    if (tf == null) { skipped.Add($"{kv.Key} (null)"); continue; }
                    if (!skeletonNames.Contains(tf.name)) { skipped.Add($"{kv.Key} → {tf.name} (not in hierarchy)"); continue; }
                    if (!tf.IsChildOf(rootObject.transform)) { skipped.Add($"{kv.Key} → {tf.name} (not child of root)"); continue; }
                    valid[kv.Key] = tf;
                }

                ValidateHumanoidHierarchy(valid, skipped);

                if (skipped.Count > 0)
                    Debug.Log($"[AvatarCreator] Skipped {skipped.Count} bones:\n{string.Join("\n", skipped.Select(s => "  - " + s))}");

                var humanBones = valid.Select(kv => new HumanBone
                {
                    humanName = kv.Key,
                    boneName  = kv.Value.name,
                    limit     = new HumanLimit { useDefaultValues = true }
                }).ToList();

                if (humanBones.Count == 0) { Debug.LogError("[AvatarCreator] No valid human bones"); return null; }

                var desc = new HumanDescription
                {
                    human    = humanBones.ToArray(),
                    skeleton = skeletonBones.ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f,
                    feetSpacing = 0f, hasTranslationDoF = false
                };

                var avatar = AvatarBuilder.BuildHumanAvatar(rootObject, desc);
                if (avatar == null) { Debug.LogError("[AvatarCreator] BuildHumanAvatar returned null"); return null; }

                avatar.name = Path.GetFileNameWithoutExtension(savePath);

                string dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                    CreateFolderRecursive(dir);

                AssetDatabase.CreateAsset(avatar, savePath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[AvatarCreator] Saved: {savePath} (isHuman:{avatar.isHuman} isValid:{avatar.isValid})");
                return avatar;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AvatarCreator] BuildAndSaveAvatar failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private static void ValidateHumanoidHierarchy(
            Dictionary<string, Transform> mapping,
            List<string> skipped)
        {
            var requirements = new (string child, string parent)[]
            {
                ("Spine","Hips"),("Chest","Spine"),("UpperChest","Chest"),
                ("Neck","Spine"),("Head","Neck"),
                ("LeftShoulder","Spine"),("LeftUpperArm","Spine"),("LeftLowerArm","LeftUpperArm"),("LeftHand","LeftLowerArm"),
                ("RightShoulder","Spine"),("RightUpperArm","Spine"),("RightLowerArm","RightUpperArm"),("RightHand","RightLowerArm"),
                ("LeftUpperLeg","Hips"),("LeftLowerLeg","LeftUpperLeg"),("LeftFoot","LeftLowerLeg"),("LeftToes","LeftFoot"),
                ("RightUpperLeg","Hips"),("RightLowerLeg","RightUpperLeg"),("RightFoot","RightLowerLeg"),("RightToes","RightFoot"),
            };
            var toRemove = new List<string>();
            foreach (var (child, parent) in requirements)
            {
                if (!mapping.TryGetValue(child, out var ct)) continue;
                if (!mapping.TryGetValue(parent, out var pt)) continue;
                if (!ct.IsChildOf(pt)) { skipped.Add($"{child} not descendant of {parent}"); toRemove.Add(child); }
            }
            foreach (var b in toRemove) mapping.Remove(b);
        }

        private static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
