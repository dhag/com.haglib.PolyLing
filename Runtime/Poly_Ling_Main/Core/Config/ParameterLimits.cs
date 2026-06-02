// ParameterLimits.cs
// 各ツールのパラメータ上下限を外部ファイル(CSV)で管理する。
// 保存先: Application.persistentDataPath/PolyLing/ParameterLimits.csv
//   - 1行1項目 "key,value"
//   - 行頭が # の行はコメント、空行は無視
//   - テキストエディタで値を編集可能（改行区切り）
// 起動時(初回アクセス時)に1回だけ読込。
// ファイルが無い / 既定キーが不足している場合のみ、既定値で生成・追記する。
//   既存の値は保持する（ユーザー編集値は上書きしない）。コメント行はコード側から再生成する。
// Editor/Player 両対応（#if UNITY_EDITOR 不使用、毎フレーム処理なし）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Core
{
    public static class ParameterLimits
    {
        // ================================================================
        // 既定値テーブル（key, 既定値, コメント）。ここが唯一の定義元。
        // ================================================================

        private static readonly (string Key, float Default, string Comment)[] Defaults =
            new (string, float, string)[]
        {
            // --- Sculpt（スカルプト） ---
            ("Sculpt.BrushRadius.Min",            0.05f,   "スカルプト: ブラシ半径の下限"),
            ("Sculpt.BrushRadius.Max",            0.1f,    "スカルプト: ブラシ半径の上限"),
            ("Sculpt.Strength.Min",               0.0005f, "スカルプト: 強度の下限"),
            ("Sculpt.Strength.Max",               0.001f,  "スカルプト: 強度の上限"),

            // --- SkinWeightPaint（ウェイトペイント） ---
            ("SkinWeight.BrushRadius.Min",        0.001f,  "ウェイトペイント: ブラシ半径の下限"),
            ("SkinWeight.BrushRadius.Max",        1.0f,    "ウェイトペイント: ブラシ半径の上限"),
            ("SkinWeight.Strength.Min",           0.0f,    "ウェイトペイント: 強度の下限"),
            ("SkinWeight.Strength.Max",           1.0f,    "ウェイトペイント: 強度の上限"),

            // --- Move（移動） ---
            ("Move.ScreenOffsetX.Min",           -100f,    "移動: 画面オフセットX の下限"),
            ("Move.ScreenOffsetX.Max",            100f,    "移動: 画面オフセットX の上限"),
            ("Move.ScreenOffsetY.Min",           -100f,    "移動: 画面オフセットY の下限"),
            ("Move.ScreenOffsetY.Max",            100f,    "移動: 画面オフセットY の上限"),
            ("Move.MagnetRadius.Min",             0.01f,   "移動: マグネット半径の下限"),
            ("Move.MagnetRadius.Max",             1.0f,    "移動: マグネット半径の上限"),

            // --- Scale（スケール） ---
            ("Scale.XYZ.Min",                     0.01f,   "スケール: XYZ一括の下限"),
            ("Scale.XYZ.Max",                     5.0f,    "スケール: XYZ一括の上限"),
            ("Scale.X.Min",                       0.01f,   "スケール: X の下限"),
            ("Scale.X.Max",                       5.0f,    "スケール: X の上限"),
            ("Scale.Y.Min",                       0.01f,   "スケール: Y の下限"),
            ("Scale.Y.Max",                       5.0f,    "スケール: Y の上限"),
            ("Scale.Z.Min",                       0.01f,   "スケール: Z の下限"),
            ("Scale.Z.Max",                       5.0f,    "スケール: Z の上限"),

            // --- EdgeBevel（辺ベベル） ---
            ("EdgeBevel.Amount.Min",              0.001f,  "辺ベベル: 量の下限"),
            ("EdgeBevel.Segments.Min",            1f,      "辺ベベル: 分割数の下限（整数）"),
            ("EdgeBevel.Segments.Max",            10f,     "辺ベベル: 分割数の上限（整数）"),

            // --- FaceExtrude（面押し出し） ---
            ("FaceExtrude.BevelScale.Min",        0.01f,   "面押し出し: ベベルスケールの下限"),
            ("FaceExtrude.BevelScale.Max",        1.0f,    "面押し出し: ベベルスケールの上限"),

            // --- AdvancedSelect（高度な選択） ---
            ("AdvancedSelect.EdgeLoopThreshold.Min", 0.0f, "高度な選択: エッジループ閾値の下限"),
            ("AdvancedSelect.EdgeLoopThreshold.Max", 1.0f, "高度な選択: エッジループ閾値の上限"),
        };

        // ================================================================
        // 内部状態
        // ================================================================

        private static Dictionary<string, float> _values;          // key -> 値
        private static List<string> _unknownLines;                 // 既定外の "key,value" 行（保持用）
        private static readonly object _lock = new object();

        private static string Dir     => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FilePath => Path.Combine(Dir, "ParameterLimits.csv");

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>float値を取得（未登録キーは既定値、それも無ければ0）</summary>
        public static float GetF(string key)
        {
            EnsureLoaded();
            if (_values.TryGetValue(key, out var v)) return v;
            return DefaultOf(key);
        }

        /// <summary>int値を取得（四捨五入）</summary>
        public static int GetI(string key) => Mathf.RoundToInt(GetF(key));

        /// <summary>ファイルを再読込（テキスト編集後に反映したい場合に使用）</summary>
        public static void Reload()
        {
            lock (_lock)
            {
                _values = null;
                _unknownLines = null;
            }
            EnsureLoaded();
        }

        // ================================================================
        // 読込・生成
        // ================================================================

        private static void EnsureLoaded()
        {
            if (_values != null) return;
            lock (_lock)
            {
                if (_values != null) return;
                LoadOrCreate();
            }
        }

        private static float DefaultOf(string key)
        {
            foreach (var d in Defaults)
                if (d.Key == key) return d.Default;
            return 0f;
        }

        private static void LoadOrCreate()
        {
            var values = new Dictionary<string, float>();
            var unknown = new List<string>();
            bool fileExists = false;

            try
            {
                if (File.Exists(FilePath))
                {
                    fileExists = true;
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;

                        int comma = line.IndexOf(',');
                        if (comma <= 0) continue;

                        string key = line.Substring(0, comma).Trim();
                        string valStr = line.Substring(comma + 1).Trim();
                        if (key.Length == 0) continue;

                        if (float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                        {
                            if (IsKnownKey(key)) values[key] = val;
                            else unknown.Add(key + "," + valStr);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ParameterLimits] 読込失敗: {e.Message}");
                values.Clear();
                unknown.Clear();
                fileExists = false;
            }

            // 既定キーで不足分を補う
            bool missing = false;
            foreach (var d in Defaults)
            {
                if (!values.ContainsKey(d.Key))
                {
                    values[d.Key] = d.Default;
                    missing = true;
                }
            }

            _values = values;
            _unknownLines = unknown;

            // ファイルが無い / 既定キーが不足していた場合のみ書き戻す
            if (!fileExists || missing)
                Write();
        }

        private static bool IsKnownKey(string key)
        {
            foreach (var d in Defaults)
                if (d.Key == key) return true;
            return false;
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Dir);

                var sb = new StringBuilder();
                sb.AppendLine("# PolyLing パラメータ上下限設定");
                sb.AppendLine("# 形式: key,value （1行1項目。行頭 # はコメント）");
                sb.AppendLine("# 値を書き換えて保存すると、次回起動時に反映されます。");
                sb.AppendLine();

                foreach (var d in Defaults)
                {
                    float v = _values.TryGetValue(d.Key, out var cur) ? cur : d.Default;
                    if (!string.IsNullOrEmpty(d.Comment))
                        sb.AppendLine("# " + d.Comment);
                    sb.Append(d.Key);
                    sb.Append(',');
                    sb.AppendLine(v.ToString("0.######", CultureInfo.InvariantCulture));
                }

                if (_unknownLines != null && _unknownLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("# --- 以下は既定外のキー（保持） ---");
                    foreach (var l in _unknownLines)
                        sb.AppendLine(l);
                }

                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ParameterLimits] 書込失敗: {e.Message}");
            }
        }
    }
}
