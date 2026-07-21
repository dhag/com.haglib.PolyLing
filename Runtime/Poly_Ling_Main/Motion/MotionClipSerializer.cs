using System.Collections.Generic;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipSerializer
    // ----------------------------------------------------------------
    // MotionClipDTO（純POCO）の JSON 入出力・ファイル入出力を担う。
    // 既存 VmdMotionSerializer / UnityClipSerializer に倣い
    // Unity.Plastic.Newtonsoft.Json を使用する。
    //
    // 読込時に各トラックのキーを秒（t）昇順へソートする
    // （サンプラは昇順を前提とするため）。
    // ================================================================
    public static class MotionClipSerializer
    {
        // ------------------------------------------------------------
        // JSON 入出力
        // ------------------------------------------------------------
        public static string ToJson(MotionClipDTO dto, bool indented = true)
        {
            return JsonConvert.SerializeObject(
                dto,
                indented ? Formatting.Indented : Formatting.None);
        }

        public static MotionClipDTO FromJson(string json)
        {
            var dto = JsonConvert.DeserializeObject<MotionClipDTO>(json);
            SortKeys(dto);
            return dto;
        }

        // ------------------------------------------------------------
        // ファイル入出力
        // ------------------------------------------------------------
        public static void SaveJson(MotionClipDTO dto, string path)
        {
            File.WriteAllText(path, ToJson(dto));
        }

        public static MotionClipDTO LoadJson(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        // ------------------------------------------------------------
        // キーの秒昇順ソート
        // ------------------------------------------------------------
        public static void SortKeys(MotionClipDTO dto)
        {
            if (dto == null) return;

            SortTracks(dto.bones);
            SortTracks(dto.bakedBones);
            if (dto.body != null) SortTrack(dto.body);

            SortScalarTracks(dto.morphs);
            SortScalarTracks(dto.muscles);
        }

        private static void SortTracks(List<MotionTrackDTO> tracks)
        {
            if (tracks == null) return;
            foreach (var t in tracks) SortTrack(t);
        }

        private static void SortTrack(MotionTrackDTO track)
        {
            if (track?.keys == null) return;
            track.keys.Sort((a, b) => a.t.CompareTo(b.t));
        }

        private static void SortScalarTracks(List<MotionScalarTrackDTO> tracks)
        {
            if (tracks == null) return;
            foreach (var t in tracks)
                if (t?.keys != null) t.keys.Sort((a, b) => a.t.CompareTo(b.t));
        }
    }
}
