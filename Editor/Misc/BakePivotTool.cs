// Assets/Editor/PivotTool/BakePivotTool.cs
// MeshFilterの静的メッシュに対して、頂点をずらしてピボットを焼き込むツールである。
// 追加機能：
// ・Hierarchy右クリック（GameObjectメニュー）から起動
// ・SceneViewにリアルタイムで「候補ピボット」のギズモを描画（確定前は何も変更しない）
// ・Cancelで安全に終了（確定前なのでロールバック不要）
//
// 非対象：SkinnedMeshRenderer（壊れる可能性が高い）

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public class BakePivotTool : EditorWindow
{
    private enum PivotMode { Preset, SliderXYZ }

    private enum PivotPreset
    {
        BoundsCenter,
        BoundsBottom,
        BoundsTop,
        BoundsLeft,
        BoundsRight,
        BoundsFront,
        BoundsBack
    }

    // UI state
    private PivotMode mode = PivotMode.SliderXYZ;
    private PivotPreset preset = PivotPreset.BoundsCenter;
    private float sliderX = 0f; // -1..+1
    private float sliderY = 0f;
    private float sliderZ = 0f;

    private bool showGizmo = true;
    private bool allowDragGizmo = true; // つまんで微調整したい場合（SliderXYZ時）
    private float gizmoSize = 0.08f;    // SceneView上の表示サイズ係数

    // Bake options
    private string outputFolder = "Assets/PivotBakedMeshes";
    private bool alsoFixMeshCollider = true;
    private bool recalcBounds = true;
    private bool recalcNormals = false;
    private bool recalcTangents = false;

    // runtime
    private GameObject targetGO;
    private Vector3 lastPivotWorld;
    private bool hasBounds;
    private Bounds cachedBounds;

    // ===== メニュー：通常起動 =====
    [MenuItem("Tools/Pivot Tool/Bake Pivot (Mesh)")]
    public static void OpenFromTools()
    {
        OpenForSelection();
    }

    // ===== メニュー：Hierarchy右クリック（GameObjectメニュー） =====
    // Hierarchy右クリックは実質このGameObjectメニューが出る
    [MenuItem("GameObject/Pivot Tool/Bake Pivot (Mesh)...", false, 49)]
    public static void OpenFromGameObjectMenu()
    {
        OpenForSelection();
    }

    [MenuItem("GameObject/Pivot Tool/Bake Pivot (Mesh)...", true)]
    public static bool ValidateOpenFromGameObjectMenu()
    {
        var go = Selection.activeGameObject;
        return go != null;
    }

    private static void OpenForSelection()
    {
        var w = GetWindow<BakePivotTool>("Bake Pivot (Mesh)");
        w.minSize = new Vector2(540, 360);
        w.RefreshTargetFromSelection();
        w.Show();
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        RefreshTargetFromSelection();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    private void OnSelectionChange()
    {
        // 選択が変わったら追従（初心者が迷子になりにくい）
        RefreshTargetFromSelection();
        Repaint();
        SceneView.RepaintAll();
    }

    private void RefreshTargetFromSelection()
    {
        targetGO = Selection.activeGameObject;
        UpdateBoundsCache();
        UpdatePivotPreview();
    }

    private void UpdateBoundsCache()
    {
        hasBounds = false;
        cachedBounds = new Bounds(Vector3.zero, Vector3.zero);

        if (targetGO == null) return;

        var rends = targetGO.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return;

        cachedBounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) cachedBounds.Encapsulate(rends[i].bounds);
        hasBounds = true;
    }

    private void UpdatePivotPreview()
    {
        if (targetGO == null)
        {
            lastPivotWorld = Vector3.zero;
            return;
        }

        if (!hasBounds || cachedBounds.size == Vector3.zero)
        {
            // Rendererが無い/Boundsが取れない場合はTransform基準
            lastPivotWorld = targetGO.transform.position;
            return;
        }

        Vector3 c = cachedBounds.center;
        Vector3 e = cachedBounds.extents;

        if (mode == PivotMode.Preset)
        {
            lastPivotWorld = preset switch
            {
                PivotPreset.BoundsCenter => c,
                PivotPreset.BoundsBottom => new Vector3(c.x, c.y - e.y, c.z),
                PivotPreset.BoundsTop    => new Vector3(c.x, c.y + e.y, c.z),
                PivotPreset.BoundsLeft   => new Vector3(c.x - e.x, c.y, c.z),
                PivotPreset.BoundsRight  => new Vector3(c.x + e.x, c.y, c.z),
                PivotPreset.BoundsFront  => new Vector3(c.x, c.y, c.z + e.z),
                PivotPreset.BoundsBack   => new Vector3(c.x, c.y, c.z - e.z),
                _ => c
            };
        }
        else
        {
            lastPivotWorld = c + new Vector3(sliderX * e.x, sliderY * e.y, sliderZ * e.z);
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "メッシュの頂点を直接ずらしてピボットを焼き込むツールである。\n" +
            "確定（Bake）するまでデータは一切変更しない。SceneViewの点はプレビューである。\n" +
            "SkinnedMeshRendererは対象外である。",
            MessageType.Info);

        using (var cc = new EditorGUI.ChangeCheckScope())
        {
            targetGO = (GameObject)EditorGUILayout.ObjectField("Target", targetGO, typeof(GameObject), true);

            mode = (PivotMode)EditorGUILayout.EnumPopup("Pivot Mode", mode);

            if (mode == PivotMode.Preset)
            {
                preset = (PivotPreset)EditorGUILayout.EnumPopup("Preset", preset);
            }
            else
            {
                EditorGUILayout.LabelField("Slider (-1=min, 0=center, +1=max)", EditorStyles.boldLabel);
                sliderX = EditorGUILayout.Slider("X", sliderX, -1f, 1f);
                sliderY = EditorGUILayout.Slider("Y", sliderY, -1f, 1f);
                sliderZ = EditorGUILayout.Slider("Z", sliderZ, -1f, 1f);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Center (0,0,0)")) { sliderX = sliderY = sliderZ = 0f; }
                if (GUILayout.Button("X=-1 (Left)")) { sliderX = -1f; }
                if (GUILayout.Button("Y=-1 (Bottom)")) { sliderY = -1f; }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(6);
            showGizmo = EditorGUILayout.ToggleLeft("Show pivot gizmo preview in SceneView", showGizmo);
            allowDragGizmo = EditorGUILayout.ToggleLeft("Allow dragging gizmo (SliderXYZ only)", allowDragGizmo);
            gizmoSize = EditorGUILayout.Slider("Gizmo size", gizmoSize, 0.02f, 0.25f);

            EditorGUILayout.Space(8);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            alsoFixMeshCollider = EditorGUILayout.ToggleLeft("Also update MeshCollider.sharedMesh (if any)", alsoFixMeshCollider);
            recalcBounds = EditorGUILayout.ToggleLeft("Recalculate Bounds", recalcBounds);
            recalcNormals = EditorGUILayout.ToggleLeft("Recalculate Normals (usually unnecessary)", recalcNormals);
            recalcTangents = EditorGUILayout.ToggleLeft("Recalculate Tangents (usually unnecessary)", recalcTangents);

            if (cc.changed)
            {
                // ターゲット変更時はBounds更新
                if (targetGO != null && Selection.activeGameObject != targetGO)
                    Selection.activeGameObject = targetGO;

                UpdateBoundsCache();
                UpdatePivotPreview();
                SceneView.RepaintAll();
            }
        }

        EditorGUILayout.Space(10);

        // 状態表示
        DrawStatus();

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!CanBakeNow(out _)))
        {
            if (GUILayout.Button("Bake (Apply)", GUILayout.Height(34)))
            {
                BakeOnce();
                Close();
            }
        }

        if (GUILayout.Button("Cancel", GUILayout.Height(34)))
        {
            // 確定前は何も変更していないので閉じるだけでキャンセルになる
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawStatus()
    {
        if (targetGO == null)
        {
            EditorGUILayout.HelpBox("Targetが未選択である。", MessageType.Warning);
            return;
        }

        if (!hasBounds)
        {
            EditorGUILayout.HelpBox("Rendererが無い。Bounds基準の計算ができないため、Transform位置を基準にする。", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.LabelField("Bounds Center (World)", cachedBounds.center.ToString("F3"));
            EditorGUILayout.LabelField("Bounds Extents (World)", cachedBounds.extents.ToString("F3"));
        }

        EditorGUILayout.LabelField("Target Pivot (World)", lastPivotWorld.ToString("F3"));

        if (!CanBakeNow(out string reason))
        {
            EditorGUILayout.HelpBox(reason, MessageType.Error);
        }
    }

    private bool CanBakeNow(out string reason)
    {
        reason = "";

        if (targetGO == null) { reason = "Targetが無い。"; return false; }

        if (targetGO.GetComponent<SkinnedMeshRenderer>() != null)
        {
            reason = "SkinnedMeshRendererは対象外である。MeshFilterの静的メッシュを選択すること。";
            return false;
        }

        var mf = targetGO.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            reason = "MeshFilter(sharedMesh) が無い。";
            return false;
        }

        return true;
    }

    // ===== SceneViewギズモ =====
    private void OnSceneGUI(SceneView sv)
    {
        if (!showGizmo) return;
        if (targetGO == null) return;

        // Boundsが変わる（スケール変更など）可能性があるので軽く追従
        UpdateBoundsCache();
        UpdatePivotPreview();

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        // ピボット候補点を表示
        float handleSize = HandleUtility.GetHandleSize(lastPivotWorld) * gizmoSize;

        // 軸線っぽい表示（初心者が点だけだと見失うので）
        Handles.color = new Color(1f, 1f, 0f, 0.9f);
        Handles.DrawWireDisc(lastPivotWorld, sv.camera.transform.forward, handleSize * 0.8f);
        Handles.SphereHandleCap(0, lastPivotWorld, Quaternion.identity, handleSize * 0.25f, EventType.Repaint);

        // SliderXYZのときだけドラッグ可（Presetは意図がブレるので固定）
        if (allowDragGizmo && mode == PivotMode.SliderXYZ && hasBounds && cachedBounds.extents != Vector3.zero)
        {
            EditorGUI.BeginChangeCheck();
            Vector3 newWorld = Handles.PositionHandle(lastPivotWorld, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Move Pivot Preview");

                // newWorldを bounds中心に対する -1..+1 へ戻す
                Vector3 c = cachedBounds.center;
                Vector3 e = cachedBounds.extents;

                Vector3 d = newWorld - c;
                sliderX = SafeDiv(d.x, e.x);
                sliderY = SafeDiv(d.y, e.y);
                sliderZ = SafeDiv(d.z, e.z);

                sliderX = Mathf.Clamp(sliderX, -1f, 1f);
                sliderY = Mathf.Clamp(sliderY, -1f, 1f);
                sliderZ = Mathf.Clamp(sliderZ, -1f, 1f);

                UpdatePivotPreview();
                Repaint();
            }
        }
    }

    private static float SafeDiv(float a, float b)
    {
        if (Mathf.Abs(b) < 1e-8f) return 0f;
        return a / b;
    }

    // ===== 焼き込み本体 =====
    private void BakeOnce()
    {
        if (!CanBakeNow(out string reason))
        {
            EditorUtility.DisplayDialog("Cannot Bake", reason, "OK");
            return;
        }

        var go = targetGO;
        var tr = go.transform;
        var mf = go.GetComponent<MeshFilter>();

        // 目標pivot（ワールド）はプレビュー値を使う
        Vector3 targetPivotWorld = lastPivotWorld;

        // 現在のTransform原点から目標pivotへ
        Vector3 offsetWorld = targetPivotWorld - tr.position;
        if (offsetWorld.sqrMagnitude < 1e-12f)
        {
            EditorUtility.DisplayDialog("No Change", "目標ピボットが現在の位置と同じである。", "OK");
            return;
        }

        // 頂点を動かすローカルオフセット
        Vector3 offsetLocal = tr.InverseTransformVector(offsetWorld);

        Undo.RegisterFullObjectHierarchyUndo(go, "Bake Pivot (Mesh)");
        Undo.RecordObject(mf, "Bake Pivot (Mesh)");
        Undo.RecordObject(tr, "Bake Pivot (Mesh)");

        Mesh src = mf.sharedMesh;
        
        // ★ Bake前のlocalBoundsを保存（重要）
var r = go.GetComponent<Renderer>();
Bounds originalLocalBounds = r != null ? r.localBounds : new Bounds();
        
        
        
        
        Mesh dst = Instantiate(src);
        dst.name = $"{src.name}_PivotBaked_{mode}";

        // 頂点を -offsetLocal へ
        var v = dst.vertices;
        for (int i = 0; i < v.Length; i++) v[i] -= offsetLocal;
        dst.vertices = v;

        if (recalcBounds) dst.RecalculateBounds();
        if (recalcNormals) dst.RecalculateNormals();
        if (recalcTangents) dst.RecalculateTangents();

        EnsureFolder(outputFolder);

        string safeName = MakeSafeFileName(dst.name);
        string path = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/{safeName}.asset");
        AssetDatabase.CreateAsset(dst, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        mf.sharedMesh = dst;

// ★ RendererのBoundsをBake前に戻す（核心）
if (r != null)
{
    r.localBounds = originalLocalBounds;
}

foreach (var col in go.GetComponents<Collider>())
{
    Undo.RecordObject(col, "Fix Collider");

    if (col is BoxCollider bc)
        bc.center -= offsetLocal;

    else if (col is SphereCollider sc)
        sc.center -= offsetLocal;

    else if (col is CapsuleCollider cc)
        cc.center -= offsetLocal;

    else if (col is WheelCollider wc)
        wc.center -= offsetLocal;
}

        if (alsoFixMeshCollider)
        {
            var mc = go.GetComponent<MeshCollider>();
            if (mc != null)
            {
                Undo.RecordObject(mc, "Bake Pivot (Mesh)");
                mc.sharedMesh = null;
                mc.sharedMesh = dst;
            }
        }

        // 見た目を維持するため transform を +offsetWorld
        tr.position += offsetWorld;

        // 確定後に再計算
        UpdateBoundsCache();
        UpdatePivotPreview();
        SceneView.RepaintAll();

        EditorUtility.DisplayDialog("Done",
            $"Pivotを焼き込んだ。\nNew Mesh: {path}\nOffsetWorld: {offsetWorld:F4}", "OK");
    }

    // ===== フォルダ作成 & ファイル名 =====
    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new Exception("Output Folderは 'Assets/...' で指定する必要がある。");

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string MakeSafeFileName(string s)
    {
        foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
        return s;
    }
}