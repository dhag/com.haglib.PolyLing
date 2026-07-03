// HumanoidAvatarExtractWindow.cs
// ─────────────────────────────────────────────────────────────────────────────
// HumanoidAvatarBuilderWindow の逆写像。
// Humanoid Avatar(.asset) の HumanDescription から Humanoid名↔ボーン名 の対応を取り出し、
// PolyLing 形式の humanoid.csv（名前ベース）として書き出す。
//
// 【設計方針】
//  ・Unity 標準API のみ（Avatar.humanDescription / HumanBone）。PolyLing 本体に非依存。
//  ・出力は順方向 ParseHumanoidCsv/Unesc/SplitCsv が読める書式に合わせる：
//      先頭行: #PolyLing_Humanoid,version,1.0
//      データ: <Humanoid名>,<ボーン名>
//      引用は標準CSV（カンマ/引用符/改行を含む場合のみ "..." で囲み、内部 " は "" に倍化）。
//
// メニュー: PolyLing ▸ IO ▸ Humanoid Avatar → humanoid.csv
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class HumanoidAvatarExtractWindow : EditorWindow
{
    private Avatar _avatar;        // 抽出元 Humanoid Avatar(.asset)
    private Vector2 _scroll;
    private string _log = "";

    [MenuItem("PolyLing/Avatar/Humanoid Avatar → humanoid.csv")]
    public static void Open() => GetWindow<HumanoidAvatarExtractWindow>("Humanoid Avatar 抽出");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Humanoid Avatar(.asset) → humanoid.csv", EditorStyles.boldLabel);
        _avatar = (Avatar)EditorGUILayout.ObjectField("Avatar", _avatar, typeof(Avatar), false);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(_avatar == null))
        {
            if (GUILayout.Button("humanoid.csv に書き出し", GUILayout.Height(28))) Extract();
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

    // ── 抽出本体 ────────────────────────────────────────────────────────────
    private void Extract()
    {
        _log = "";
        if (_avatar == null) { Log("Avatar が未指定。"); return; }
        if (!_avatar.isValid)  { Log("Avatar が無効（isValid=false）。"); return; }
        if (!_avatar.isHuman)  { Log("Humanoid ではない Avatar（isHuman=false）。"); return; }

        HumanBone[] human = _avatar.humanDescription.human;
        if (human == null || human.Length == 0) { Log("HumanBone が空。"); return; }

        var sb = new StringBuilder();
        sb.Append("#PolyLing_Humanoid,version,1.0\n");

        int count = 0;
        foreach (var hb in human)
        {
            if (string.IsNullOrEmpty(hb.humanName)) { Log("humanName 空のエントリをスキップ。"); continue; }
            if (string.IsNullOrEmpty(hb.boneName)) { Log("boneName 空をスキップ: " + hb.humanName); continue; }

            sb.Append(Esc(hb.humanName));
            sb.Append(',');
            sb.Append(Esc(hb.boneName));
            sb.Append('\n');
            count++;
        }

        if (count == 0) { Log("書き出す対応が0件。"); return; }

        string defaultName = string.IsNullOrEmpty(_avatar.name) ? "humanoid" : _avatar.name;
        string savePath = EditorUtility.SaveFilePanel("humanoid.csv を保存", "", defaultName + ".csv", "csv");
        if (string.IsNullOrEmpty(savePath)) return;

        File.WriteAllText(savePath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.Refresh();
        Log($"完了：humanoid.csv 書き出し。件数={count}\n  → {savePath}");
    }

    // ── CSVフィールドのエスケープ（順方向 Unesc の逆） ───────────────────────
    // カンマ / 引用符 / 改行を含む場合のみ "..." で囲み、内部 " は "" に倍化。
    private static string Esc(string s)
    {
        if (s == null) return "";
        bool needQuote = s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
                         || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
        if (!needQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private void Log(string m)
    {
        _log += m + "\n";
        Debug.Log("[HumanoidAvatarExtract] " + m);
        Repaint();
    }
}
