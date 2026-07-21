using System.Collections.Generic;
using Poly_Ling.VMD;
using Poly_Ling.VMD.Serialization;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipConverters
    // ----------------------------------------------------------------
    // 旧 DTO / VMDData から新 MotionClipDTO への「片方向」変換。
    // すべて統一 Unity 空間・float 秒の新形式へ入力する。
    //
    // ■ 座標系（恒久メモ）
    //   VMD 系は VmdMotionSerializer.ToMotionDTO（ToUnityRotation / ToUnityPosition）で
    //   既に Unity 化された値を受け取る。ここで新たな座標変換は足さない。
    //   sourceRest はいずれの経路でも空で生成する（外部 UnityBone CSV v2 を優先）。
    // ================================================================
    public static class MotionClipConverters
    {
        // ------------------------------------------------------------
        // MotionDTO（VMD 系・rot/pos 別チャンネル）→ MotionClipDTO
        //   t = f / fps。bones は targetKind="boneName"。
        // ------------------------------------------------------------
        public static MotionClipDTO FromMotionDTO(MotionDTO src)
        {
            var dst = new MotionClipDTO { name = null, space = "local" };
            if (src == null) return dst;

            float fps = src.fps > 0f ? src.fps : 30f;
            dst.frameRate = fps;

            if (src.bones != null)
            {
                foreach (var bt in src.bones)
                {
                    if (bt == null) continue;
                    var track = new MotionTrackDTO { id = bt.name, targetKind = "boneName" };

                    // rot / pos は別列。フレーム番号でマージする。
                    var rotByFrame = new Dictionary<int, float[]>();
                    if (bt.rot != null)
                        foreach (var k in bt.rot)
                            if (k != null && k.v != null) rotByFrame[k.f] = k.v;

                    var posByFrame = new Dictionary<int, float[]>();
                    if (bt.pos != null)
                        foreach (var k in bt.pos)
                            if (k != null && k.v != null) posByFrame[k.f] = k.v;

                    var frames = new SortedSet<int>();
                    foreach (var f in rotByFrame.Keys) frames.Add(f);
                    foreach (var f in posByFrame.Keys) frames.Add(f);

                    foreach (int f in frames)
                    {
                        var key = new MotionKeyDTO { t = f / fps };
                        if (rotByFrame.TryGetValue(f, out var rv)) key.rot = rv;
                        if (posByFrame.TryGetValue(f, out var pv)) key.pos = pv;
                        track.keys.Add(key);
                    }
                    dst.bones.Add(track);
                }
            }

            if (src.morphs != null)
            {
                foreach (var mt in src.morphs)
                {
                    if (mt == null) continue;
                    var track = new MotionScalarTrackDTO { name = mt.name };
                    if (mt.w != null)
                        foreach (var k in mt.w)
                            if (k != null) track.keys.Add(new MotionScalarKeyDTO { t = k.f / fps, v = k.v });
                    dst.morphs.Add(track);
                }
            }

            return dst;
        }

        // ------------------------------------------------------------
        // UnityClipDTO（Unity クリップ系）→ MotionClipDTO
        //   t はそのまま。bones は targetKind="path"、bakedBones/body は "humanoid"。
        // ------------------------------------------------------------
        public static MotionClipDTO FromUnityClipDTO(UnityClipDTO src)
        {
            var dst = new MotionClipDTO { space = "local" };
            if (src == null) return dst;

            dst.name      = src.name;
            dst.frameRate = src.frameRate > 0f ? src.frameRate : 30f;
            dst.loop      = src.loop;

            // 二次骨（path）
            if (src.bones != null)
                foreach (var t in src.bones)
                    if (t != null)
                        dst.bones.Add(new MotionTrackDTO { id = t.path, targetKind = "path", keys = CopyKeys(t.keys) });

            // baked 本体ボーン（humanoid 正準名）
            if (src.bakedBones != null)
                foreach (var t in src.bakedBones)
                    if (t != null)
                        dst.bakedBones.Add(new MotionTrackDTO { id = t.path, targetKind = "humanoid", keys = CopyKeys(t.keys) });

            // マッスル
            if (src.muscles != null)
            {
                foreach (var m in src.muscles)
                {
                    if (m == null) continue;
                    var track = new MotionScalarTrackDTO { name = m.name };
                    if (m.w != null)
                        foreach (var k in m.w)
                            if (k != null) track.keys.Add(new MotionScalarKeyDTO { t = k.t, v = k.v });
                    dst.muscles.Add(track);
                }
            }

            // ルート（body）
            if (src.body != null && src.body.keys != null && src.body.keys.Count > 0)
            {
                var body = new MotionTrackDTO { id = "body", targetKind = "humanoid" };
                foreach (var k in src.body.keys)
                    if (k != null) body.keys.Add(new MotionKeyDTO { t = k.t, pos = k.pos, rot = k.rot });
                dst.body = body;
            }

            return dst;
        }

        // ------------------------------------------------------------
        // VMDData → MotionClipDTO（既存の右手系→左手系変換を再利用）
        // ------------------------------------------------------------
        public static MotionClipDTO FromVMD(VMDData vmd)
        {
            var motion = VmdMotionSerializer.ToMotionDTO(vmd);
            var dst = FromMotionDTO(motion);
            if (vmd != null) dst.name = vmd.ModelName;
            return dst;
        }

        // ------------------------------------------------------------
        // ヘルパ: UnityBoneKeyDTO 列 → MotionKeyDTO 列（値はそのまま）
        // ------------------------------------------------------------
        private static List<MotionKeyDTO> CopyKeys(List<UnityBoneKeyDTO> src)
        {
            var outp = new List<MotionKeyDTO>();
            if (src == null) return outp;
            foreach (var k in src)
                if (k != null)
                    outp.Add(new MotionKeyDTO { t = k.t, pos = k.pos, rot = k.rot, scl = k.scl });
            return outp;
        }
    }
}
