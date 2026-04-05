// Assets/Editor/HumanoidLimitDumpWindow.cs
// Humanoid dump tool (Unity6 compatible)
//
// 1) HumanTrait default muscle ranges: GetMuscleDefaultMin/Max (+ defaultCenterMuscle=0)
// 2) Avatar HumanDescription bone limits: HumanLimit min/max/center (degrees)
// 3) Probe "center pose" in practice: set muscles=0 via HumanPoseHandler, dump each HumanBodyBones local euler
//
// Notes:
// - Unity does NOT expose a "GetMuscleDefaultCenter" API.
//   Therefore default muscle center is treated as 0 in muscle space.
// - Bone-limit center exists in HumanLimit.center (degree), accessible via Avatar.humanDescription.human[].limit.center.
// - The probed center pose depends on the Animator/Avatar and current scene object (it applies a pose).

#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using Poly_Ling.EditorCore;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class HumanoidLimitDumpWindow : EditorWindow
{
    private enum Tab
    {
        DefaultMuscles = 0,
        AvatarLimits = 1,
        ProbeCenterPose = 2,
        ExportAll = 3,
    }

    private Tab _tab = Tab.DefaultMuscles;
    private Vector2 _scroll;

    // Target selection
    private Animator _animator;
    private Avatar _avatar;

    // Default(muscle) options
    private bool _includeIndex = true;
    private bool _includeDerivedGroup = true; // derive from MuscleName prefix
    private bool _preview = true;
    private int _previewRows = 200;

    // Avatar(bone limit) options
    private bool _avatarOnlyOverrides = false; // show only useDefaultValues==false
    private bool _avatarShowAxisLength = false;

    // Probe options
    private bool _probeUseAnimatorTransform = true; // use Animator.transform as root
    private bool _probeDumpAllBones = true;         // dump all HumanBodyBones except LastBone
    private bool _probeIncludeWorld = false;        // dump world euler too (optional)
    private bool _probeApplyPose = true;            // actually SetHumanPose
    private bool _probeRestoreAfter = true;         // attempt to restore after probing
    //単体コマンド
    [MenuItem("Tools/Utility/Misc/Humanoid Dump Defaults & Avatar Limits")]
    public static void Open()
    {
        GetWindow<HumanoidLimitDumpWindow>("Humanoid Limits Dump");
    }

    private void OnGUI()
    {
        DrawHeader();

        _tab = (Tab)GUILayout.Toolbar((int)_tab, new[]
        {
            "Default Muscles",
            "Avatar HumanLimits",
            "Probe Center Pose",
            "Export",
        });

        EditorGUILayout.Space(6);

        switch (_tab)
        {
            case Tab.DefaultMuscles:
                DrawDefaultMusclesTab();
                break;
            case Tab.AvatarLimits:
                DrawAvatarLimitsTab();
                break;
            case Tab.ProbeCenterPose:
                DrawProbeCenterPoseTab();
                break;
            case Tab.ExportAll:
                DrawExportTab();
                break;
        }

        EditorGUILayout.Space(8);
        DrawFooterSummary();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Humanoid Limits Dumper", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "HumanTrait default(muscle) と Avatar個別(HumanDescription: bone limit) をDumpする。\n" +
            "HumanTrait側には default center API が無いので defaultCenterMuscle=0 を出力する。\n" +
            "角度としての“center”が欲しい場合は Probe Center Pose（muscles=0を適用して各ボーンのLocalEulerをDump）を使う。",
            MessageType.Info);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            _animator = (Animator)EditorGUILayout.ObjectField("Animator (recommended)", _animator, typeof(Animator), true);
            _avatar = (Avatar)EditorGUILayout.ObjectField("Avatar (optional)", _avatar, typeof(Avatar), false);

            EditorGUILayout.HelpBox(
                "優先順位: Animator.avatar → Avatarフィールド。\n" +
                "Probeは Animator が必要（シーン上のHumanoidに適用して測るため）。",
                MessageType.None);
        }
    }

    // -------------------------
    // Tab: Default Muscles
    // -------------------------
    private void DrawDefaultMusclesTab()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("HumanTrait Default Muscle Ranges", EditorStyles.boldLabel);

            _includeIndex = EditorGUILayout.ToggleLeft("CSVにindex列を含める", _includeIndex);
            _includeDerivedGroup = EditorGUILayout.ToggleLeft("CSVに推定group列を含める（MuscleNameから推定）", _includeDerivedGroup);

            _preview = EditorGUILayout.ToggleLeft("プレビュー表示", _preview);
            using (new EditorGUI.DisabledScope(!_preview))
            {
                _previewRows = EditorGUILayout.IntSlider("プレビュー最大行数", _previewRows, 20, 1000);
            }

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("DefaultMuscles CSVをクリップボードへ"))
                {
                    GUIUtility.systemCopyBuffer = BuildDefaultMusclesCsv();
                    Debug.Log("Copied: HumanTrait default muscles CSV");
                }
                if (GUILayout.Button("DefaultMuscles CSVをファイル保存..."))
                {
                    SaveTextAsCsv("HumanTrait_DefaultMuscleRanges", BuildDefaultMusclesCsv());
                }
            }
        }

        if (_preview)
        {
            DrawDefaultMusclesPreview();
        }
    }

    private void DrawDefaultMusclesPreview()
    {
        string[] names = HumanTrait.MuscleName;
        int count = HumanTrait.MuscleCount;
        int rows = Mathf.Min(count, _previewRows);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"Preview (first {rows} rows)", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(440));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (_includeIndex) GUILayout.Label("i", GUILayout.Width(40));
                if (_includeDerivedGroup) GUILayout.Label("group", GUILayout.Width(90));
                GUILayout.Label("name", GUILayout.Width(360));
                GUILayout.Label("defaultMin", GUILayout.Width(90));
                GUILayout.Label("defaultMax", GUILayout.Width(90));
                GUILayout.Label("defaultCenterMuscle", GUILayout.Width(130));
            }
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            for (int i = 0; i < rows; i++)
            {
                float mn = HumanTrait.GetMuscleDefaultMin(i);
                float mx = HumanTrait.GetMuscleDefaultMax(i);
                string group = _includeDerivedGroup ? DeriveMuscleGroup(names[i]) : "";

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_includeIndex) GUILayout.Label(i.ToString(), GUILayout.Width(40));
                    if (_includeDerivedGroup) GUILayout.Label(group, GUILayout.Width(90));
                    GUILayout.Label(names[i], GUILayout.Width(360));
                    GUILayout.Label(mn.ToString("0.###", CultureInfo.InvariantCulture), GUILayout.Width(90));
                    GUILayout.Label(mx.ToString("0.###", CultureInfo.InvariantCulture), GUILayout.Width(90));
                    GUILayout.Label("0", GUILayout.Width(130)); // muscle center is 0 (API not provided otherwise)
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private string BuildDefaultMusclesCsv()
        => EditorHumanoidLimitDump.BuildDefaultMusclesCsv(_includeIndex, _includeDerivedGroup);

    private static string DeriveMuscleGroup(string name)
        => EditorHumanoidLimitDump.DeriveMuscleGroup(name);

    // -------------------------
    // Tab: Avatar Limits (bone limit center exists)
    // -------------------------
    private void DrawAvatarLimitsTab()
    {
        Avatar av = ResolveAvatar();
        bool hasAvatar = av != null;

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Avatar HumanDescription / HumanLimit (bone-based)", EditorStyles.boldLabel);

            _avatarOnlyOverrides = EditorGUILayout.ToggleLeft("上書きのみ表示 (useDefaultValues == false)", _avatarOnlyOverrides);
            _avatarShowAxisLength = EditorGUILayout.ToggleLeft("axisLength列を表示/出力", _avatarShowAxisLength);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!hasAvatar))
                {
                    if (GUILayout.Button("AvatarLimits CSVをクリップボードへ"))
                    {
                        GUIUtility.systemCopyBuffer = BuildAvatarLimitsCsv(av, _avatarOnlyOverrides, _avatarShowAxisLength);
                        Debug.Log("Copied: Avatar human limits CSV");
                    }
                    if (GUILayout.Button("AvatarLimits CSVをファイル保存..."))
                    {
                        SaveTextAsCsv("Avatar_HumanLimits", BuildAvatarLimitsCsv(av, _avatarOnlyOverrides, _avatarShowAxisLength));
                    }
                }
            }

            EditorGUILayout.Space(4);

            if (!hasAvatar)
            {
                EditorGUILayout.HelpBox("AnimatorまたはAvatarを指定する必要がある。", MessageType.Warning);
                return;
            }

            if (!av.isHuman)
            {
                EditorGUILayout.HelpBox("指定されたAvatarはHumanoidではない（Avatar.isHuman == false）。", MessageType.Error);
                return;
            }
        }

        DrawAvatarLimitsPreview(av);
    }

    private void DrawAvatarLimitsPreview(Avatar av)
    {
        HumanDescription hd;
        try
        {
            hd = av.humanDescription;
        }
        catch (Exception e)
        {
            EditorGUILayout.HelpBox("Avatar.humanDescription の取得に失敗した。\n" + e.Message, MessageType.Error);
            return;
        }

        var list = hd.human ?? Array.Empty<HumanBone>();
        if (list.Length == 0)
        {
            EditorGUILayout.HelpBox("HumanDescription.human が空である。", MessageType.Warning);
            return;
        }

        var filtered = _avatarOnlyOverrides
            ? list.Where(h => h.limit.useDefaultValues == false).ToArray()
            : list;

        int rows = Mathf.Min(filtered.Length, _previewRows);

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"Preview (first {rows} rows) / total: {filtered.Length}", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(440));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("humanName", GUILayout.Width(210));
                GUILayout.Label("boneName", GUILayout.Width(210));
                GUILayout.Label("useDefault", GUILayout.Width(80));
                GUILayout.Label("min(deg)", GUILayout.Width(140));
                GUILayout.Label("max(deg)", GUILayout.Width(140));
                GUILayout.Label("center(deg)", GUILayout.Width(140));
                if (_avatarShowAxisLength) GUILayout.Label("axisLength", GUILayout.Width(80));
            }
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            for (int i = 0; i < rows; i++)
            {
                var hb = filtered[i];
                var lim = hb.limit;

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(hb.humanName, GUILayout.Width(210));
                    GUILayout.Label(hb.boneName, GUILayout.Width(210));
                    GUILayout.Label(lim.useDefaultValues ? "true" : "false", GUILayout.Width(80));
                    GUILayout.Label(Vec3ToString(lim.min), GUILayout.Width(140));
                    GUILayout.Label(Vec3ToString(lim.max), GUILayout.Width(140));
                    GUILayout.Label(Vec3ToString(lim.center), GUILayout.Width(140));
                    if (_avatarShowAxisLength) GUILayout.Label(lim.axisLength.ToString("0.###", CultureInfo.InvariantCulture), GUILayout.Width(80));
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private string BuildAvatarLimitsCsv(Avatar av, bool onlyOverrides, bool includeAxisLength)
        => EditorHumanoidLimitDump.BuildAvatarLimitsCsv(av, onlyOverrides, includeAxisLength);

    // -------------------------
    // Tab: Probe Center Pose (practical "default center angles")
    // -------------------------
    private void DrawProbeCenterPoseTab()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Probe Center Pose (muscles=0)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Animator上のHumanoidに対して muscles=0 の HumanPose を適用し、各HumanBodyBonesのLocalEulerをDumpする。\n" +
                "これが実運用上の“center角”になる（UnityがHumanTrait側の角度centerを公開していないため）。",
                MessageType.Info);

            _probeUseAnimatorTransform = EditorGUILayout.ToggleLeft("AnimatorのTransformを対象にする", _probeUseAnimatorTransform);
            _probeDumpAllBones = EditorGUILayout.ToggleLeft("全HumanBodyBonesをDump", _probeDumpAllBones);
            _probeIncludeWorld = EditorGUILayout.ToggleLeft("WorldEulerも出力する", _probeIncludeWorld);
            _probeApplyPose = EditorGUILayout.ToggleLeft("muscles=0を実際に適用する(SetHumanPose)", _probeApplyPose);
            _probeRestoreAfter = EditorGUILayout.ToggleLeft("終了後に姿勢を復元する（可能な範囲）", _probeRestoreAfter);

            EditorGUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_animator == null))
                {
                    if (GUILayout.Button("Probe CSVをクリップボードへ"))
                    {
                        GUIUtility.systemCopyBuffer = BuildProbeCenterPoseCsv();
                        Debug.Log("Copied: probed center pose CSV");
                    }
                    if (GUILayout.Button("Probe CSVをファイル保存..."))
                    {
                        SaveTextAsCsv("Avatar_ProbedCenterPose", BuildProbeCenterPoseCsv());
                    }
                }
            }

            if (_animator == null)
            {
                EditorGUILayout.HelpBox("ProbeにはAnimatorが必要である。", MessageType.Warning);
                return;
            }
            if (_animator.avatar == null || !_animator.avatar.isHuman)
            {
                EditorGUILayout.HelpBox("AnimatorのAvatarがHumanoidである必要がある。", MessageType.Error);
                return;
            }
        }

        // preview
        string csv = BuildProbeCenterPoseCsv(preview: true, previewMaxRows: _previewRows);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Preview (CSV head)", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(320));
            EditorGUILayout.TextArea(csv);
            EditorGUILayout.EndScrollView();
        }
    }

    private string BuildProbeCenterPoseCsv(bool preview = false, int previewMaxRows = 200)
        => EditorHumanoidLimitDump.BuildProbeCenterPoseCsv(
            _animator, _probeUseAnimatorTransform, _probeDumpAllBones,
            _probeIncludeWorld, _probeApplyPose, _probeRestoreAfter,
            preview, previewMaxRows);

    private static Vector3 NormalizeEuler(Vector3 euler)
        => EditorHumanoidLimitDump.NormalizeEuler(euler);

    private static float NormalizeAngle(float a)
        => EditorHumanoidLimitDump.NormalizeAngle(a);

    private static string GetPathRelative(Transform root, Transform t)
        => EditorHumanoidLimitDump.GetPathRelative(root, t);

    // -------------------------
    // Tab: Export
    // -------------------------
    private void DrawExportTab()
    {
        Avatar av = ResolveAvatar();

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("全部クリップボードへ（連結）"))
                {
                    var sb = new StringBuilder();

                    sb.AppendLine("# --- HumanTrait Default Muscles ---");
                    sb.AppendLine(BuildDefaultMusclesCsv().TrimEnd());
                    sb.AppendLine();

                    sb.AppendLine("# --- Avatar HumanLimits ---");
                    sb.AppendLine(av != null ? BuildAvatarLimitsCsv(av, _avatarOnlyOverrides, _avatarShowAxisLength).TrimEnd()
                                             : "error,avatar is null\n");
                    sb.AppendLine();

                    sb.AppendLine("# --- Probed Center Pose (muscles=0) ---");
                    sb.AppendLine(_animator != null ? BuildProbeCenterPoseCsv().TrimEnd()
                                                    : "error,animator is null\n");

                    GUIUtility.systemCopyBuffer = sb.ToString();
                    Debug.Log("Copied: combined dump text");
                }

                if (GUILayout.Button("フォルダにまとめて保存..."))
                {
                    var folder = EditorUtility.OpenFolderPanel("Select export folder", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        File.WriteAllText(Path.Combine(folder, "HumanTrait_DefaultMuscleRanges.csv"), BuildDefaultMusclesCsv(), Encoding.UTF8);

                        if (av != null && av.isHuman)
                            File.WriteAllText(Path.Combine(folder, "Avatar_HumanLimits.csv"), BuildAvatarLimitsCsv(av, _avatarOnlyOverrides, _avatarShowAxisLength), Encoding.UTF8);
                        else
                            File.WriteAllText(Path.Combine(folder, "Avatar_HumanLimits.ERROR.txt"), "Avatar not set or not humanoid.\n", Encoding.UTF8);

                        if (_animator != null && _animator.avatar != null && _animator.avatar.isHuman)
                            File.WriteAllText(Path.Combine(folder, "Avatar_ProbedCenterPose.csv"), BuildProbeCenterPoseCsv(), Encoding.UTF8);
                        else
                            File.WriteAllText(Path.Combine(folder, "Avatar_ProbedCenterPose.ERROR.txt"), "Animator not set or not humanoid.\n", Encoding.UTF8);

                        Debug.Log($"Exported to folder: {folder}");
                        AssetDatabase.Refresh();
                    }
                }
            }
        }
    }

    // -------------------------
    // Utilities
    // -------------------------
    private Avatar ResolveAvatar()
    {
        if (_animator != null && _animator.avatar != null) return _animator.avatar;
        if (_avatar != null) return _avatar;
        return null;
    }

    private void SaveTextAsCsv(string defaultName, string csvText)
        => EditorHumanoidLimitDump.SaveTextAsCsv(defaultName, csvText);

    private static string Vec3ToString(Vector3 v)
        => EditorHumanoidLimitDump.Vec3ToString(v);

    private static void AppendVec3Csv(StringBuilder sb, Vector3 v)
        => EditorHumanoidLimitDump.AppendVec3Csv(sb, v);

    private void DrawFooterSummary()
    {
        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField("Quick Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"HumanTrait.MuscleCount: {HumanTrait.MuscleCount}");

            Avatar av = ResolveAvatar();
            if (av == null)
            {
                EditorGUILayout.LabelField("Avatar: (none)");
            }
            else
            {
                EditorGUILayout.LabelField($"Avatar: {av.name}");
                EditorGUILayout.LabelField($"isHuman: {av.isHuman}");
                EditorGUILayout.LabelField($"isValid: {av.isValid}");
            }

            if (_animator == null)
                EditorGUILayout.LabelField("Animator: (none)");
            else
                EditorGUILayout.LabelField($"Animator: {_animator.name} (avatar: {(_animator.avatar ? _animator.avatar.name : "null")})");
        }
    }
}
#endif
