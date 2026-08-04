// Editor/AvatarBuilder/ManualHumanoidAvatarWindow.cs
// ============================================================
// Humanoid Avatar を手動で組み立てるエディタウインドウ。
//
// 【役割】
//   ・空の状態から始め、HumanTrait.BoneCount 全ボーンの Transform 割り当てと
//     可動域（HumanLimit）、リターゲット設定8項目をすべて手作業で指定する。
//   ・既存 Avatar を投げ込むと、そこから読み取れる情報（humanName→boneName、
//     HumanLimit、リターゲット設定8項目）を自動で流し込む。
//   ・生成・保存は AvatarBuildCore.BuildAndSaveAvatar に委譲する。
//
// 【注意】
//   ・Avatar の内部データは HumanDescription 以外を公開していないため、
//     既存 Avatar から取得できるのは HumanDescription の内容のみである。
//   ・boneName は文字列であるため、Character Root 階層内の同名 Transform を
//     検索して解決する。同名が複数ある場合は AvatarBuildCore が先勝ちで採用する。
// ============================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.EditorIO;

public class ManualHumanoidAvatarWindow : EditorWindow
{
    private GameObject characterRoot;
    private Avatar sourceAvatar;

    private Transform[] mappedTransforms;
    private HumanLimit[] humanLimits;
    private string[] sourceBoneNames;
    private string[] sourceSkeletonNames = new string[0];
    private bool showSkeletonList = true;
    private AvatarRetargetSettings settings = AvatarRetargetSettings.Default;

    private Vector2 scroll;
    private string log = "";

    [MenuItem("PolyLing/Avatar/Avatarを手動作成")]
    public static void Open()
    {
        ManualHumanoidAvatarWindow window = GetWindow<ManualHumanoidAvatarWindow>();
        window.titleContent = new GUIContent("Avatar手動作成");
        window.minSize = new Vector2(560f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        EnsureArrays();
    }

    private void EnsureArrays()
    {
        int count = HumanTrait.BoneCount;

        if (mappedTransforms == null || mappedTransforms.Length != count)
            mappedTransforms = new Transform[count];

        if (humanLimits == null || humanLimits.Length != count)
        {
            humanLimits = new HumanLimit[count];
            for (int i = 0; i < count; i++)
                humanLimits[i] = CreateDefaultLimit();
        }

        if (sourceBoneNames == null || sourceBoneNames.Length != count)
        {
            sourceBoneNames = new string[count];
            for (int i = 0; i < count; i++)
                sourceBoneNames[i] = "";
        }
    }

    private void OnGUI()
    {
        EnsureArrays();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSourceFields();
        EditorGUILayout.Space(6f);
        DrawToolbar();
        DrawMappings();
        DrawSkeletonList();
        DrawRetargetSettings();
        DrawBuildButton();
        DrawLog();

        EditorGUILayout.EndScrollView();
    }

    // ── 入力元 ──────────────────────────────────────────────────────────────
    private void DrawSourceFields()
    {
        EditorGUILayout.LabelField("Humanoid Avatar 手動作成", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        GameObject newRoot = (GameObject)EditorGUILayout.ObjectField(
            "Character Root", characterRoot, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            characterRoot = newRoot;
            ResolveTransformsFromSourceAvatar();
        }

        EditorGUI.BeginChangeCheck();
        Avatar newAvatar = (Avatar)EditorGUILayout.ObjectField(
            "既存Avatar（任意）", sourceAvatar, typeof(Avatar), false);
        if (EditorGUI.EndChangeCheck())
        {
            sourceAvatar = newAvatar;
            LoadFromSourceAvatar();
        }

        EditorGUILayout.HelpBox(
            "Character Root は必須である。AvatarBuilder.BuildHumanAvatar が GameObject 階層を要求する。\n" +
            "既存Avatar を入れると、そこから読み取れる情報を自動で流し込む。空欄なら全て手入力になる。",
            MessageType.None);
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("既存Avatarから再取得", EditorStyles.toolbarButton))
                LoadFromSourceAvatar();

            if (GUILayout.Button("割り当てをクリア", EditorStyles.toolbarButton))
            {
                for (int i = 0; i < mappedTransforms.Length; i++)
                    mappedTransforms[i] = null;
            }

            if (GUILayout.Button("可動域を既定へ戻す", EditorStyles.toolbarButton))
            {
                for (int i = 0; i < humanLimits.Length; i++)
                    humanLimits[i] = CreateDefaultLimit();
            }

            GUILayout.FlexibleSpace();
        }
    }

    // ── ボーン割り当て ──────────────────────────────────────────────────────
    private void DrawMappings()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Humanoidボーン割り当て", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("* はHumanoidで必須のボーン", EditorStyles.miniLabel);

        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            string humanName = HumanTrait.BoneName[i];
            string label = HumanTrait.RequiredBone(i) ? humanName + "  *" : humanName;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                mappedTransforms[i] = (Transform)EditorGUILayout.ObjectField(
                    label, mappedTransforms[i], typeof(Transform), true);

                if (!string.IsNullOrEmpty(sourceBoneNames[i]))
                {
                    EditorGUILayout.LabelField(
                        "Avatar内のボーン名", sourceBoneNames[i], EditorStyles.miniLabel);
                }

                DrawLimitEditor(i);
            }
        }
    }

    private void DrawLimitEditor(int index)
    {
        HumanLimit limit = humanLimits[index];

        EditorGUI.indentLevel++;
        limit.useDefaultValues = EditorGUILayout.Toggle(
            "Use Default Values", limit.useDefaultValues);

        if (!limit.useDefaultValues)
        {
            limit.axisLength = EditorGUILayout.FloatField("Axis Length", limit.axisLength);
            limit.center = EditorGUILayout.Vector3Field("Center", limit.center);
            limit.min = EditorGUILayout.Vector3Field("Min", limit.min);
            limit.max = EditorGUILayout.Vector3Field("Max", limit.max);
        }

        EditorGUI.indentLevel--;
        humanLimits[index] = limit;
    }

    // ── リターゲット設定 ────────────────────────────────────────────────────
    private void DrawRetargetSettings()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Retargeting設定", EditorStyles.boldLabel);

        EditorGUI.indentLevel++;
        settings.upperArmTwist = EditorGUILayout.Slider(
            "Upper Arm Twist", settings.upperArmTwist, 0f, 1f);
        settings.lowerArmTwist = EditorGUILayout.Slider(
            "Lower Arm Twist", settings.lowerArmTwist, 0f, 1f);
        settings.upperLegTwist = EditorGUILayout.Slider(
            "Upper Leg Twist", settings.upperLegTwist, 0f, 1f);
        settings.lowerLegTwist = EditorGUILayout.Slider(
            "Lower Leg Twist", settings.lowerLegTwist, 0f, 1f);
        settings.armStretch = EditorGUILayout.Slider(
            "Arm Stretch", settings.armStretch, 0f, 1f);
        settings.legStretch = EditorGUILayout.Slider(
            "Leg Stretch", settings.legStretch, 0f, 1f);
        settings.feetSpacing = EditorGUILayout.FloatField(
            "Feet Spacing", settings.feetSpacing);
        settings.hasTranslationDoF = EditorGUILayout.Toggle(
            "Translation DoF", settings.hasTranslationDoF);

        if (GUILayout.Button("既定値へ戻す"))
            settings = AvatarRetargetSettings.Default;

        EditorGUI.indentLevel--;
    }

    // ── 生成 ────────────────────────────────────────────────────────────────
    private void DrawBuildButton()
    {
        EditorGUILayout.Space(8f);

        using (new EditorGUI.DisabledScope(characterRoot == null))
        {
            if (GUILayout.Button("Avatarを生成して保存", GUILayout.Height(30f)))
                Build();
        }
    }

    private void Build()
    {
        log = "";

        if (characterRoot == null)
        {
            Log("Character Root が未指定。");
            return;
        }

        Dictionary<string, string> map = new Dictionary<string, string>();
        Dictionary<string, HumanLimit> limits = new Dictionary<string, HumanLimit>();

        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            Transform mapped = mappedTransforms[i];
            if (mapped == null)
                continue;

            string humanName = HumanTrait.BoneName[i];
            map[humanName] = mapped.name;

            if (!humanLimits[i].useDefaultValues)
                limits[humanName] = humanLimits[i];
        }

        if (map.Count == 0)
        {
            Log("Transform が1つも割り当てられていない。");
            return;
        }

        string defaultName = characterRoot.name + "_Avatar";
        string savePath = EditorUtility.SaveFilePanelInProject(
            "Avatar 保存先", defaultName, "asset", "保存先を指定する。");

        if (string.IsNullOrEmpty(savePath))
            return;

        Avatar avatar = AvatarBuildCore.BuildAndSaveAvatar(
            characterRoot, map, limits, settings, savePath, Log);

        if (avatar == null)
            return;

        Log("完了。");
        Selection.activeObject = avatar;
        EditorGUIUtility.PingObject(avatar);
    }

    // ── 既存Avatarからの読み込み ────────────────────────────────────────────
    private void LoadFromSourceAvatar()
    {
        EnsureArrays();

        if (sourceAvatar == null)
        {
            ClearSourceNames();
            return;
        }

        if (!sourceAvatar.isHuman)
        {
            Log("選択したAvatarはHumanoidではない。");
            return;
        }

        HumanDescription description = sourceAvatar.humanDescription;
        if (description.human == null || description.human.Length == 0)
        {
            Log("このAvatarから HumanDescription.human を取得できない。");
            return;
        }

        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            mappedTransforms[i] = null;
            humanLimits[i] = CreateDefaultLimit();
            sourceBoneNames[i] = "";
        }

        if (description.skeleton != null)
        {
            sourceSkeletonNames = new string[description.skeleton.Length];
            for (int i = 0; i < description.skeleton.Length; i++)
                sourceSkeletonNames[i] = description.skeleton[i].name;
        }
        else
        {
            sourceSkeletonNames = new string[0];
        }

        settings = new AvatarRetargetSettings
        {
            upperArmTwist = description.upperArmTwist,
            lowerArmTwist = description.lowerArmTwist,
            upperLegTwist = description.upperLegTwist,
            lowerLegTwist = description.lowerLegTwist,
            armStretch = description.armStretch,
            legStretch = description.legStretch,
            feetSpacing = description.feetSpacing,
            hasTranslationDoF = description.hasTranslationDoF
        };

        for (int i = 0; i < description.human.Length; i++)
        {
            HumanBone bone = description.human[i];
            int index = FindHumanBoneIndex(bone.humanName);
            if (index < 0)
                continue;

            humanLimits[index] = bone.limit;
            sourceBoneNames[index] = bone.boneName;

            if (characterRoot != null)
                mappedTransforms[index] = FindFirstTransformByName(
                    characterRoot.transform, bone.boneName);
        }

        Repaint();
    }

    /// <summary>Character Root だけが後から指定された場合に Transform 欄を埋め直す。</summary>
    private void ResolveTransformsFromSourceAvatar()
    {
        EnsureArrays();

        if (sourceAvatar == null || characterRoot == null || !sourceAvatar.isHuman)
            return;

        HumanDescription description = sourceAvatar.humanDescription;
        if (description.human == null)
            return;

        for (int i = 0; i < description.human.Length; i++)
        {
            HumanBone bone = description.human[i];
            int index = FindHumanBoneIndex(bone.humanName);
            if (index < 0)
                continue;

            mappedTransforms[index] = FindFirstTransformByName(
                characterRoot.transform, bone.boneName);
        }

        Repaint();
    }

    // ── 既存Avatarのボーン名一覧 ────────────────────────────────────────────
    private void DrawSkeletonList()
    {
        EditorGUILayout.Space(4f);
        showSkeletonList = EditorGUILayout.Foldout(
            showSkeletonList,
            "既存Avatar内のボーン名一覧（skeleton: " + sourceSkeletonNames.Length + "件）",
            true);

        if (!showSkeletonList)
            return;

        if (sourceSkeletonNames.Length == 0)
        {
            EditorGUILayout.HelpBox(
                "既存Avatar が未指定、または skeleton が空である。",
                MessageType.None);
            return;
        }

        EditorGUI.indentLevel++;
        for (int i = 0; i < sourceSkeletonNames.Length; i++)
            EditorGUILayout.LabelField(i.ToString(), sourceSkeletonNames[i]);
        EditorGUI.indentLevel--;
    }

    private void ClearSourceNames()
    {
        for (int i = 0; i < sourceBoneNames.Length; i++)
            sourceBoneNames[i] = "";

        sourceSkeletonNames = new string[0];
        Repaint();
    }

    // ── 補助 ────────────────────────────────────────────────────────────────
    private static int FindHumanBoneIndex(string humanName)
    {
        for (int i = 0; i < HumanTrait.BoneCount; i++)
        {
            if (HumanTrait.BoneName[i] == humanName)
                return i;
        }
        return -1;
    }

    private static Transform FindFirstTransformByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == name)
                return transforms[i];
        }
        return null;
    }

    private static HumanLimit CreateDefaultLimit()
    {
        return new HumanLimit
        {
            useDefaultValues = true,
            axisLength = 0f,
            center = Vector3.zero,
            min = Vector3.zero,
            max = Vector3.zero
        };
    }

    private void DrawLog()
    {
        if (string.IsNullOrEmpty(log))
            return;

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("ログ", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(log, MessageType.None);
    }

    private void Log(string message)
    {
        log += message + "\n";
        Debug.Log("[ManualHumanoidAvatar] " + message);
        Repaint();
    }
}
