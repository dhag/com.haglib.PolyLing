// DisplaySettings.cs
// 表示まわりの見た目パラメータを外部ファイル(CSV)で管理する。
// 保存先: Application.persistentDataPath/PolyLing/DisplaySettings.csv
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
    public static class DisplaySettings
    {
        // ================================================================
        // キー定数
        // ================================================================

        /// <summary>非選択メッシュの頂点サイズ（スクリーンピクセル）</summary>
        public const string KeyVertexScreenSizeUnselected = "Vertex.ScreenSize.Unselected";

        /// <summary>選択メッシュの頂点サイズ（スクリーンピクセル）</summary>
        public const string KeyVertexScreenSizeSelected = "Vertex.ScreenSize.Selected";

        /// <summary>法線表示の線分長（ワールド単位）</summary>
        public const string KeyNormalLength = "Normal.Length";

        /// <summary>Top/Front/Side ビューの orthographicSize 下限（拡大限界）</summary>
        public const string KeyCameraOrthoSizeMin = "Camera.OrthoSizeMin";

        /// <summary>Perspective ビュー（オルソ切替含む）の注視点距離 下限（拡大限界）</summary>
        public const string KeyCameraZoomDistanceMin = "Camera.ZoomDistanceMin";

        /// <summary>粗動倍率: Shift 押下中の視点操作（回転・パン・ズーム）の速度倍率</summary>
        public const string KeyCameraSpeedCoarse = "Camera.SpeedCoarse";

        /// <summary>微動倍率: Ctrl 押下中の視点操作（回転・パン・ズーム）の速度倍率</summary>
        public const string KeyCameraSpeedFine = "Camera.SpeedFine";

        // ================================================================
        // 既定値テーブル（key, 既定値, コメント）。ここが唯一の定義元。
        // ================================================================

        private static readonly (string Key, float Default, string Comment)[] Defaults =
            new (string, float, string)[]
        {
            // --- Vertex（頂点表示） ---
            (KeyVertexScreenSizeUnselected, 8f, "頂点サイズ: 非選択メッシュ（スクリーンピクセル）"),
            (KeyVertexScreenSizeSelected,   8f, "頂点サイズ: 選択メッシュ（スクリーンピクセル）"),

            // --- Normal（法線表示） ---
            (KeyNormalLength,           0.03f,  "法線表示: 線分の長さ（ワールド単位・固定長）"),

            // --- Camera（カメラ） ---
            (KeyCameraOrthoSizeMin,     0.001f, "拡大限界: Top/Front/Side の orthographicSize 下限（小さいほど拡大できる）"),
            (KeyCameraZoomDistanceMin,  0.001f, "拡大限界: Perspective の注視点距離 下限（小さいほど拡大できる）"),
            (KeyCameraSpeedCoarse,      4f,     "粗動倍率: Shift+視点操作（回転・パン・ズーム）の速度倍率"),
            (KeyCameraSpeedFine,        0.2f,   "微動倍率: Ctrl+視点操作（回転・パン・ズーム）の速度倍率"),
        };

        // ================================================================
        // 内部状態
        // ================================================================

        private static Dictionary<string, float> _values;          // key -> 値
        private static List<string> _unknownLines;                 // 既定外の "key,value" 行（保持用）
        private static readonly object _lock = new object();

        private static string Dir        => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FilePath   => Path.Combine(Dir, "DisplaySettings.csv");
        private static string BackupPath => Path.Combine(Dir, "DisplaySettings.bak.csv");

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

        /// <summary>
        /// float値を設定してCSVへ即書き戻す（UI→CSV の逆経路。既知キーのみ受理）。
        /// </summary>
        public static void SetF(string key, float value)
        {
            EnsureLoaded();
            if (!IsKnownKey(key)) return; // 未知キーは無視（キー体系を保つ）
            lock (_lock)
            {
                _values[key] = value;
                Write();
            }
        }

        /// <summary>保存先CSVの絶対パス（表示・手動バックアップ用）。</summary>
        public static string GetFilePath() => FilePath;

        /// <summary>CSVを同フォルダに DisplaySettings.bak.csv として複製する。</summary>
        public static bool Backup()
        {
            try
            {
                EnsureLoaded();
                lock (_lock) { Write(); } // 最新状態を確実にファイル化してから複製
                if (!File.Exists(FilePath)) return false;
                File.Copy(FilePath, BackupPath, overwrite: true);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DisplaySettings] バックアップ失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>bakファイルを本体へ戻して再読込する。</summary>
        public static bool Restore()
        {
            try
            {
                if (!File.Exists(BackupPath)) return false;
                File.Copy(BackupPath, FilePath, overwrite: true);
                Reload();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DisplaySettings] 復元失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 全値をコード既定(Defaults)へ総入れ替えしてCSVを書き直す。
        /// 既定外の保持行(_unknownLines)は残す。
        /// </summary>
        public static void ResetToDefaults()
        {
            lock (_lock)
            {
                EnsureLoaded();
                var values = new Dictionary<string, float>();
                foreach (var d in Defaults) values[d.Key] = d.Default;
                _values = values;
                Write();
            }
        }

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
                Debug.LogWarning($"[DisplaySettings] 読込失敗: {e.Message}");
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
                sb.AppendLine("# PolyLing 表示設定");
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
                Debug.LogWarning($"[DisplaySettings] 書込失敗: {e.Message}");
            }
        }
    }
}
