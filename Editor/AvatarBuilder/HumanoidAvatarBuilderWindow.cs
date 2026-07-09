// HumanoidAvatarBuilderWindow.cs
// ─────────────────────────────────────────────────────────────────────────────
// 独立エディタ拡張：ヒエラルキー/プレファブ上の「SkinnedMesh＋ボーンTransform階層」を持つ
// モデルと、対応表 humanoid.csv（Humanoid名,ボーン名）から Humanoid Avatar(.asset) を生成する。
//
// 【設計方針】
//  ・Unity 標準API のみ（UnityEngine.AvatarBuilder / HumanDescription / HumanTrait）。
//    PolyLing 本体（CSVモデル復元 / MeshContextList / PMX）には一切依存しない＝完全独立。
//  ・AvatarBuilder.BuildHumanAvatar が要求する入力は次の2つだけ：
//      (1) ボーンTransform階層を持つ root GameObject  →  SkeletonBone[]（各Transformの name/局所TRS）
//      (2) Humanoid名 ↔ ボーン名 の対応            →  HumanBone[]
//    モデルがヒエラルキー上に SkinnedMeshRenderer＋ボーンで載っているので、骨格の再構築は不要。
//  ・必須ボーン判定は Unity の HumanTrait を使う（PolyLing の表を持ち込まない）。
//  ・レスト姿勢は「指定モデルの現姿勢」をバインドに使う（T強制しない。A/Tどちらでも構築可。
//    Unity が筋肉空間をその姿勢基準で算出する）。
//
// 【humanoid.csv 形式】（PolyLing ランタイムが書き出す形式）
//    先頭行: #PolyLing_Humanoid,version,1.0  （'#' 始まりはコメント＝スキップ）
//    データ: <Humanoid名>,<値>[,useDefault,minX,minY,minZ,maxX,maxY,maxZ,centerX,centerY,centerZ,axisLength]
//      値がボーン名 → 名前ベース（本拡張が使うのはこれ）
//      値が整数     → indexベース（GameObject からは復元不可のため非対応。警告）
//      3列目以降（任意・加算互換）→ マッスル可動域（度）。useDefault=false の行のみ採用。
//        2列のみの行＝可動域は既定値（従来通り）。
//    引用は標準CSV（カンマ/引用符を含む場合のみ "..." で囲み、内部の " は "" に倍化）。
//
// 置き場所: Assets 以下の "Editor" フォルダ。 メニュー: Tools ▸ Humanoid Avatar 作成
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Poly_Ling.EditorIO;

public class HumanoidAvatarBuilderWindow : EditorWindow
{
    private GameObject _root;                                   // モデルのルート（SkinnedMesh＋ボーン階層）
    private string _csvPath = "";                               // humanoid.csv のフルパス
    private string _savePath = "Assets/NewHumanoidAvatar.asset";// Avatar(.asset) 保存先（プロジェクト相対）
    private bool _addAnimator = true;                           // 生成後、元モデルに Animator を付与/割当
    private Vector2 _scroll;
    private string _log = "";

    [MenuItem("PolyLing/Avatar/humanoid.csv → Humanoid Avatar")]
    public static void Open() => GetWindow<HumanoidAvatarBuilderWindow>("Humanoid Avatar 作成");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("モデル（SkinnedMesh＋ボーンTransform階層を持つ root）", EditorStyles.boldLabel);
        _root = (GameObject)EditorGUILayout.ObjectField("Model Root", _root, typeof(GameObject), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("対応表 humanoid.csv（Humanoid名,ボーン名）", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(_csvPath) ? "(未選択)" : _csvPath,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("選択", GUILayout.Width(60)))
            {
                string p = EditorUtility.OpenFilePanel("humanoid.csv を選択", "", "csv");
                if (!string.IsNullOrEmpty(p)) _csvPath = p;
            }
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            _savePath = EditorGUILayout.TextField("保存先(.asset)", _savePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string p = EditorUtility.SaveFilePanelInProject("Avatar 保存先", "NewHumanoidAvatar", "asset", "");
                if (!string.IsNullOrEmpty(p)) _savePath = p;
            }
        }
        _addAnimator = EditorGUILayout.Toggle("Animator を付与/割当（シーン上のモデルへ）", _addAnimator);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_root == null || string.IsNullOrEmpty(_csvPath)))
        {
            if (GUILayout.Button("Avatar を生成", GUILayout.Height(28))) Build();
        }

        if (!string.IsNullOrEmpty(_log))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ログ", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(140));
            EditorGUILayout.HelpBox(_log, MessageType.None);
            EditorGUILayout.EndScrollView();
        }
    }

    // ── 生成本体 ────────────────────────────────────────────────────────────
    private void Build()
    {
        _log = "";
        if (_root == null) { Log("モデル root が未指定。"); return; }
        if (!File.Exists(_csvPath)) { Log("humanoid.csv が見つからない: " + _csvPath); return; }

        // 1) 対応表 CSV を解析（Humanoid名 → ボーン名）
        if (!ParseHumanoidCsv(_csvPath, out var map, out var limits, out var csvWarn)) return;
        foreach (var w in csvWarn) Log(w);
        if (map.Count == 0) { Log("対応表が空（名前ベースの行が無い）。"); return; }

        // プレファブ資産が指定された場合はシーンへ一時インスタンス化して構築する
        bool isAsset = EditorUtility.IsPersistent(_root);
        GameObject root = _root;
        bool temp = false;
        if (isAsset)
        {
            root = PrefabUtility.InstantiatePrefab(_root) as GameObject;
            if (root == null) root = Instantiate(_root);
            temp = true;
        }

        try
        {
            // 手順2〜7（HumanBone/必須判定/SkeletonBone/HumanDescription/生成/保存）は
            // 共有コアへ集約（HierarchyExportWindow と同一処理・重複排除）。
            var avatar = AvatarBuildCore.BuildAndSaveAvatar(root, map, limits, _savePath, Log);
            if (avatar == null) return;

            // 任意：シーン上のモデルへ Animator を付与し Avatar を割当（資産選択時はスキップ）
            if (_addAnimator)
            {
                if (isAsset)
                {
                    Log("※ プレファブ資産が指定されたため Animator 付与はスキップ（Avatar は保存済み）。");
                }
                else
                {
                    var anim = _root.GetComponent<Animator>();
                    if (anim == null) anim = Undo.AddComponent<Animator>(_root);
                    anim.avatar = avatar;
                    EditorUtility.SetDirty(_root);
                }
            }

            Log("完了。");
            EditorGUIUtility.PingObject(avatar);
        }
        finally
        {
            if (temp && root != null) DestroyImmediate(root);   // 一時インスタンスを後片付け
        }
    }

    // ── humanoid.csv 解析（名前ベースのみ採用） ─────────────────────────────
    //   3列目以降があれば可動域（度）として limits に格納（案(i)加算互換）。
    //   列: humanName,bone,useDefault,minX,minY,minZ,maxX,maxY,maxZ,centerX,centerY,centerZ,axisLength
    private bool ParseHumanoidCsv(string path, out Dictionary<string, string> map,
                                  out Dictionary<string, HumanLimit> limits, out List<string> warn)
    {
        map = new Dictionary<string, string>();
        limits = new Dictionary<string, HumanLimit>();
        warn = new List<string>();
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (Exception e) { Log("CSV 読込失敗: " + e.Message); return false; }

        bool indexBasedSeen = false;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;  // 空行・#ヘッダをスキップ
            var cols = SplitCsv(line);
            if (cols.Count < 2) continue;

            string humanName = Unesc(cols[0]);
            string value = cols[1].Trim();

            // 値が非負整数なら indexベース → GameObjectからは復元不可なので採用しない
            if (int.TryParse(value, out int idx) && idx >= 0) { indexBasedSeen = true; continue; }

            string boneName = Unesc(value);
            if (humanName.Length == 0 || boneName.Length == 0) continue;
            map[humanName] = boneName;   // 後勝ち（重複Humanoid名は通常無い）

            // 可動域（度）: useDefault=false かつ 13列以上のときのみ採用
            if (cols.Count >= 13 &&
                bool.TryParse(cols[2].Trim(), out bool useDefault) && !useDefault)
            {
                limits[humanName] = new HumanLimit
                {
                    useDefaultValues = false,
                    min    = new Vector3(Pf(cols[3]),  Pf(cols[4]),  Pf(cols[5])),
                    max    = new Vector3(Pf(cols[6]),  Pf(cols[7]),  Pf(cols[8])),
                    center = new Vector3(Pf(cols[9]),  Pf(cols[10]), Pf(cols[11])),
                    axisLength = Pf(cols[12])
                };
            }
        }

        if (indexBasedSeen)
            warn.Add("※ humanoid.csv に index 形式の行があります。GameObject からは復元不可のため、" +
                     "名前ベースで書き出した humanoid.csv を使用してください（該当行は無視）。");
        return true;
    }

    // 度の数値をパース（InvariantCulture）。失敗は0。
    private static float Pf(string s)
    {
        return float.TryParse((s ?? "").Trim(), System.Globalization.NumberStyles.Float,
                              System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    // 引用対応のCSV分割（カンマ区切り。引用符内のカンマは保護。フィールドは生のまま返し Unesc で復元）
    private static List<string> SplitCsv(string line)
    {
        var outp = new List<string>();
        var sb = new StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"') { q = !q; sb.Append(c); }
            else if (c == ',' && !q) { outp.Add(sb.ToString()); sb.Length = 0; }
            else sb.Append(c);
        }
        outp.Add(sb.ToString());
        return outp;
    }

    // CSVフィールドの復元（外側の "..." を外し、内部の "" を " に戻す）
    private static string Unesc(string s)
    {
        if (s == null) return "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
            s = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
        return s;
    }

    private void Log(string m)
    {
        _log += m + "\n";
        Debug.Log("[HumanoidAvatarBuilder] " + m);
        Repaint();
    }
}
