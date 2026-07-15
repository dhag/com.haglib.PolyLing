// PlayerUiPrefs.cs
// 左ペイン等の UI 状態を端末ローカルに保持する共有ヘルパー。
// 保存先は PTFS 表示と同じ RecentPaths（ファイル永続ストア）に一本化する。
// Runtime/Poly_Ling_Player/Core/ に配置

using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>
    /// UI 状態の永続化（Bool/Int）。RecentPaths を型付きでラップする。
    /// 以後の左ペイン UI 状態はここ経由に統一する（ad-hoc な直 PlayerPrefs をやめる）。
    /// </summary>
    public static class PlayerUiPrefs
    {
        public static bool GetBool(string key, bool def)
        {
            string s = RecentPaths.Get(key, "");
            return string.IsNullOrEmpty(s) ? def : (s == "1");
        }

        public static void SetBool(string key, bool value)
            => RecentPaths.Set(key, value ? "1" : "0");

        public static int GetInt(string key, int def)
        {
            string s = RecentPaths.Get(key, "");
            return int.TryParse(s, out int v) ? v : def;
        }

        public static void SetInt(string key, int value)
            => RecentPaths.Set(key, value.ToString());
    }
}
