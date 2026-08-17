using System.IO;
using Newtonsoft.Json;

namespace Poly_Ling.UnityClip
{
    // ================================================================
    // UnityClipSerializer
    // ----------------------------------------------------------------
    // UnityClipDTO（純POCO）の JSON 入出力・ファイル入出力を担う。
    //
    // 役割分担:
    //   - AnimationClip -> UnityClipDTO への抽出は Editor 側（AnimationClipToDto）。
    //     UnityEditor.AnimationUtility が必要なため Runtime には置けない。
    //   - 本クラスは DTO <-> JSON <-> ファイル のみ。UnityEngine/UnityEditor に依存せず
    //     Runtime で JSON ファイル読み込みができる。
    //
    // ■ Newtonsoft について（恒久メモ）
    //   Newtonsoft.Json（com.unity.nuget.newtonsoft-json）を使用。
    //   エディタ専用の Unity.Plastic.Newtonsoft.Json はプレイヤービルドに
    //   含まれず解決できないため使わない。
    // ================================================================
    public static class UnityClipSerializer
    {
        // ------------------------------------------------------------
        // JSON 入出力
        // ------------------------------------------------------------
        public static string ToJson(UnityClipDTO dto, bool indented = true)
        {
            return JsonConvert.SerializeObject(
                dto,
                indented ? Formatting.Indented : Formatting.None);
        }

        public static UnityClipDTO FromJson(string json)
        {
            return JsonConvert.DeserializeObject<UnityClipDTO>(json);
        }

        // ------------------------------------------------------------
        // ファイル入出力
        // ------------------------------------------------------------
        public static void SaveJson(UnityClipDTO dto, string path)
        {
            File.WriteAllText(path, ToJson(dto));
        }

        public static UnityClipDTO LoadJson(string path)
        {
            return FromJson(File.ReadAllText(path));
        }
    }
}
