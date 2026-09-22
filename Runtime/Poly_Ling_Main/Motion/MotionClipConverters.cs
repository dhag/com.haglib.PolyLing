using System.Collections.Generic;
using Poly_Ling.VMD;
using Poly_Ling.VMD.Serialization;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipConverters
    // ----------------------------------------------------------------
    // 旧 DTO（MotionDTO）/ VMDData から MotionClipDTO への「片方向」変換。
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

            // VMD のモーフは PMX のモーフ名そのもの。表情としては MorphExpression の名前で解決する。
            if (src.morphs != null)
            {
                foreach (var mt in src.morphs)
                {
                    if (mt == null) continue;
                    var track = new MotionExpressionTrackDTO { name = mt.name, provider = "generic" };
                    if (mt.w != null)
                        foreach (var k in mt.w)
                            if (k != null) track.keys.Add(new MotionScalarKeyDTO { t = k.f / fps, v = k.v });
                    dst.expressions.Add(track);
                }
            }

            return dst;
        }

        // ------------------------------------------------------------
        // VMDData → MotionClipDTO（既存の軸反転 X・Z 両反転 を再利用）
        // ------------------------------------------------------------
        public static MotionClipDTO FromVMD(VMDData vmd)
        {
            var motion = VmdMotionSerializer.ToMotionDTO(vmd);
            var dst = FromMotionDTO(motion);
            if (vmd != null) dst.name = vmd.ModelName;
            return dst;
        }
    }
}
