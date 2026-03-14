using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public class ConsoleCopyWindow : EditorWindow
{
    // 検索キーワード。空なら全件対象
    private string _keyword = "";

    // 本文の大文字小文字を無視するか
    private bool _ignoreCase = true;

    // スタックトレースを含めるか
    private bool _includeStackTrace = true;

    // ファイルパスと行番号も付けるか
    private bool _includeFileAndLine = false;

    // mode値も付けるか（内部調査用）
    private bool _includeMode = false;

    // エディタ再起動後も値を残すためのキー
    private const string PrefKeyword = "ConsoleCopyWindow.Keyword";
    private const string PrefIgnoreCase = "ConsoleCopyWindow.IgnoreCase";
    private const string PrefIncludeStackTrace = "ConsoleCopyWindow.IncludeStackTrace";
    private const string PrefIncludeFileAndLine = "ConsoleCopyWindow.IncludeFileAndLine";
    private const string PrefIncludeMode = "ConsoleCopyWindow.IncludeMode";
    //単体コマンド
    [MenuItem("Tools/Utility/Misc/Console Copy Window")]
    public static void Open()
    {
        var window = GetWindow<ConsoleCopyWindow>("Console Copy");
        window.minSize = new Vector2(420, 180);
        window.Show();
    }

    private void OnEnable()
    {
        _keyword = EditorPrefs.GetString(PrefKeyword, "");
        _ignoreCase = EditorPrefs.GetBool(PrefIgnoreCase, true);
        _includeStackTrace = EditorPrefs.GetBool(PrefIncludeStackTrace, true);
        _includeFileAndLine = EditorPrefs.GetBool(PrefIncludeFileAndLine, false);
        _includeMode = EditorPrefs.GetBool(PrefIncludeMode, false);
    }

    private void OnDisable()
    {
        EditorPrefs.SetString(PrefKeyword, _keyword);
        EditorPrefs.SetBool(PrefIgnoreCase, _ignoreCase);
        EditorPrefs.SetBool(PrefIncludeStackTrace, _includeStackTrace);
        EditorPrefs.SetBool(PrefIncludeFileAndLine, _includeFileAndLine);
        EditorPrefs.SetBool(PrefIncludeMode, _includeMode);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Console 出力コピー", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("キーワードで絞り込み（空なら全件）");
        _keyword = EditorGUILayout.TextField(_keyword);

        _ignoreCase = EditorGUILayout.ToggleLeft("大文字小文字を無視", _ignoreCase);
        _includeStackTrace = EditorGUILayout.ToggleLeft("スタックトレースを含める", _includeStackTrace);
        _includeFileAndLine = EditorGUILayout.ToggleLeft("file / line を含める", _includeFileAndLine);
        _includeMode = EditorGUILayout.ToggleLeft("mode 値を含める（調査用）", _includeMode);

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("コピー実行", GUILayout.Height(32)))
            {
                CopyLogs(
                    _keyword,
                    _ignoreCase,
                    _includeStackTrace,
                    _includeFileAndLine,
                    _includeMode
                );
            }

            if (GUILayout.Button("本文だけコピー", GUILayout.Height(32)))
            {
                CopyLogs(
                    _keyword,
                    _ignoreCase,
                    includeStackTrace: false,
                    includeFileAndLine: false,
                    includeMode: false
                );
            }
        }

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "現状は Unity Console の内部ログを読み出してコピーする。\n" +
            "検索欄や Log/Warning/Error トグルの状態そのものを読むわけではない。\n" +
            "必要ならキーワードで絞り込む方式として使う。",
            MessageType.Info
        );
    }
    //単体コマンド
    [MenuItem("Tools/Utility/Misc/Copy Console Logs/Message Only")]
    public static void CopyMessageOnlyMenu()
    {
        CopyLogs("", true, false, false, false);
    }


    //単体コマンド
    [MenuItem("Tools/Utility/Misc/Copy Console Logs/Message + StackTrace")]
    public static void CopyMessageAndStackMenu()
    {
        CopyLogs("", true, true, false, false);
    }

    private static void CopyLogs(
        string keyword,
        bool ignoreCase,
        bool includeStackTrace,
        bool includeFileAndLine,
        bool includeMode)
    {
        try
        {
            Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
            Type logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor.dll");

            if (logEntriesType == null || logEntryType == null)
            {
                Debug.LogError("UnityEditor.LogEntries または UnityEditor.LogEntry が見つからない。");
                return;
            }

            MethodInfo getCount = logEntriesType.GetMethod(
                "GetCount",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo getEntryInternal = logEntriesType.GetMethod(
                "GetEntryInternal",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo startGettingEntries = logEntriesType.GetMethod(
                "StartGettingEntries",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo endGettingEntries = logEntriesType.GetMethod(
                "EndGettingEntries",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (getCount == null || getEntryInternal == null)
            {
                Debug.LogError("LogEntries の必要メソッド取得に失敗した。");
                return;
            }

            FieldInfo messageField = logEntryType.GetField(
                "message",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            FieldInfo fileField = logEntryType.GetField(
                "file",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            FieldInfo lineField = logEntryType.GetField(
                "line",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            FieldInfo modeField = logEntryType.GetField(
                "mode",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (messageField == null)
            {
                Debug.LogError("LogEntry.message が見つからない。");
                return;
            }

            int count = (int)getCount.Invoke(null, null);
            object logEntry = Activator.CreateInstance(logEntryType);

            StringBuilder sb = new StringBuilder();

            startGettingEntries?.Invoke(null, null);

            for (int i = 0; i < count; i++)
            {
                bool ok = (bool)getEntryInternal.Invoke(null, new object[] { i, logEntry });
                if (!ok) continue;

                string message = messageField.GetValue(logEntry) as string ?? "";
                if (string.IsNullOrEmpty(message)) continue;

                if (!PassKeyword(message, keyword, ignoreCase))
                    continue;

                if (includeMode && modeField != null)
                {
                    object modeObj = modeField.GetValue(logEntry);
                    sb.AppendLine($"[mode:{modeObj}]");
                }

                sb.AppendLine(message);

                if (includeFileAndLine)
                {
                    string file = fileField?.GetValue(logEntry) as string ?? "";
                    int line = 0;

                    if (lineField != null)
                    {
                        object lineObj = lineField.GetValue(logEntry);
                        if (lineObj is int intLine)
                            line = intLine;
                    }

                    if (!string.IsNullOrEmpty(file))
                    {
                        sb.AppendLine($"    at {file}:{line}");
                    }
                }

                if (includeStackTrace)
                {
                    // Unity 6 系では message に本文＋スタックがまとまっている場合がある。
                    // 追加の stackTrace フィールドが見えていないため、ここでは別取得はしない。
                    // 必要なら後で callstack 系内部バッファを掘る。
                }

                sb.AppendLine();
            }

            endGettingEntries?.Invoke(null, null);

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"Console logs copied to clipboard. Count={count}, CopiedChars={sb.Length}");
        }
        catch (Exception ex)
        {
            Debug.LogError("CopyLogs 失敗: " + ex);
        }
    }

    private static bool PassKeyword(string text, string keyword, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(keyword))
            return true;

        if (string.IsNullOrEmpty(text))
            return false;

        return ignoreCase
            ? text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
            : text.Contains(keyword);
    }
}