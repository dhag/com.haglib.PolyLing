using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using Poly_Ling.VMD;

namespace Poly_Ling.VMD.Serialization
{
    // ================================================================
    // VmdMotionSerializer
    // ----------------------------------------------------------------
    // VMDData（VMD読み込み結果・右手系・スパースキー）から
    // MotionDTO（Unity左手系・純POCO）を生成し、JSON 入出力する。
    //
    // 役割分担:
    //   - 座標系変換(右手系VMD→左手系Unity)・Vector/Quaternion→float[] 変換は
    //     “この生成側(Unity依存)” に閉じる。MotionDTO 自体は純POCO のまま。
    //   - キーはスパースのまま運ぶ（補間しない／密化しない）。キー間補間は消費側の責務。
    //
    // 移植メモ:
    //   他言語(Python/JS)で同じ JSON を作る場合、必要なのは
    //   (1) VMDバイナリのキー読み出し、(2) 右手系→左手系変換、(3) 本DTO構造への詰め替え。
    //   ここの ToMotionDTO がその参照実装になる。
    // ================================================================
    public static class VmdMotionSerializer
    {
        // VMD の標準フレームレート。
        private const float VmdFps = 30f;

        // ------------------------------------------------------------
        // VMDData -> MotionDTO （ベイク: 座標変換のみ、補間はしない）
        // ------------------------------------------------------------
        public static MotionDTO ToMotionDTO(VMDData vmd)
        {
            var dto = new MotionDTO
            {
                fps = VmdFps,
                space = "local"
            };

            if (vmd == null)
                return dto;

            // --- ボーン ---
            // VMDData.BoneFramesByName は読み込み時にフレーム昇順へソート済み。
            foreach (var kv in vmd.BoneFramesByName)
            {
                var frames = kv.Value;
                if (frames == null || frames.Count == 0)
                    continue;

                var track = new MotionBoneTrackDTO { name = kv.Key };

                // 位置キーは「そのボーンが位置を一度でも使う」場合のみ全キーを出す。
                // （多くの VMD ボーンは位置が常にゼロ。線形補間を壊さないため、
                //   使うボーンは全キー出力、使わないボーンは pos を空にする。）
                bool usesPos = frames.Any(fr => fr.Position != Vector3.zero);

                foreach (var fr in frames)
                {
                    int f = (int)fr.FrameNumber;

                    // 回転: 右手系VMD -> 左手系Unity
                    Quaternion uq = CoordinateConverter.ToUnityRotation(fr.Rotation);
                    track.rot.Add(new RotKeyDTO
                    {
                        f = f,
                        v = new[] { uq.x, uq.y, uq.z, uq.w }
                    });

                    if (usesPos)
                    {
                        // 位置: 右手系VMD -> 左手系Unity
                        Vector3 up = CoordinateConverter.ToUnityPosition(fr.Position);
                        track.pos.Add(new PosKeyDTO
                        {
                            f = f,
                            v = new[] { up.x, up.y, up.z }
                        });
                    }
                }

                dto.bones.Add(track);
            }

            // --- モーフ ---
            foreach (var kv in vmd.MorphFramesByName)
            {
                var frames = kv.Value;
                if (frames == null || frames.Count == 0)
                    continue;

                var track = new MotionMorphTrackDTO { name = kv.Key };
                foreach (var fr in frames)
                {
                    track.w.Add(new WeightKeyDTO
                    {
                        f = (int)fr.FrameNumber,
                        v = fr.Weight
                    });
                }
                dto.morphs.Add(track);
            }

            return dto;
        }

        // ------------------------------------------------------------
        // JSON 入出力
        // ------------------------------------------------------------
        public static string ToJson(MotionDTO dto, bool indented = true)
        {
            return JsonConvert.SerializeObject(
                dto,
                indented ? Formatting.Indented : Formatting.None);
        }

        public static MotionDTO FromJson(string json)
        {
            return JsonConvert.DeserializeObject<MotionDTO>(json);
        }

        // VMDData から直接 JSON 文字列へ。
        public static string ToJson(VMDData vmd, bool indented = true)
        {
            return ToJson(ToMotionDTO(vmd), indented);
        }

        // ------------------------------------------------------------
        // ファイル入出力
        // ------------------------------------------------------------
        public static void SaveJson(MotionDTO dto, string path)
        {
            File.WriteAllText(path, ToJson(dto));
        }

        public static void SaveJson(VMDData vmd, string path)
        {
            File.WriteAllText(path, ToJson(vmd));
        }

        public static MotionDTO LoadJson(string path)
        {
            return FromJson(File.ReadAllText(path));
        }
    }
}
