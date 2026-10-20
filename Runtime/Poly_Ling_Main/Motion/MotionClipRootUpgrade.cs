// MotionClipRootUpgrade.cs
// 旧表現（muscles に "RootT.x"～"RootQ.w" の 7 本を置いたもの）の PolyLing モーションを、
// Root を body トラックに置く version 3 の表現へ書き換える。
// Runtime/Poly_Ling_Main/Motion/ に配置
//
// ■ 使いどころ
//   読み込み時の自動変換はしない（MotionClipSerializer は旧表現をエラーにする）。
//   手元の旧ファイルを一度だけ書き換えるための道具で、コマンド convertMotionRootToBody から呼ぶ。
//
// ■ 変換
//   RootT.x/y/z → body の pos[0..2]、RootQ.x/y/z/w → body の rot[0..3]。
//   キー時刻は 7 本の和集合。その時刻に成分のキーがあれば値と接線（tan）をそのまま写す。
//   キーの無い成分は MotionCurveMath.EvaluateScalar で値を取り、その時刻のチャンネルは接線なし（線形）にする。
//   ラップモードは RootT.y（無ければ最初に見つかった Root）のものを写す。
//   body が既にあるファイルは書き換えない（どちらを正とするか決められない）。

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    public static class MotionClipRootUpgrade
    {
        private static readonly string[] PosNames = { UnityClipRootMotion.NameTx, UnityClipRootMotion.NameTy, UnityClipRootMotion.NameTz };
        private static readonly string[] RotNames = { UnityClipRootMotion.NameQx, UnityClipRootMotion.NameQy, UnityClipRootMotion.NameQz, UnityClipRootMotion.NameQw };

        /// <summary>
        /// jo の muscles にある Root の 7 本を body トラックへ移す。
        /// 移したら true。移すものが無い・移せないときは false（note に理由）。
        /// </summary>
        public static bool TryMoveRootCurvesToBody(JObject jo, out string note)
        {
            note = "";
            if (!(jo?["muscles"] is JArray muscles)) { note = "muscles がありません"; return false; }

            var found = new Dictionary<string, MotionScalarTrackDTO>(StringComparer.Ordinal);
            var keep  = new JArray();
            foreach (var tok in muscles)
            {
                string name = (tok as JObject)?.Value<string>("name");
                if (name != null && UnityClipRootMotion.IsRootCurveName(name))
                    found[name] = tok.ToObject<MotionScalarTrackDTO>();
                else
                    keep.Add(tok);
            }
            if (found.Count == 0) { note = "Root の旧表現はありません"; return false; }
            if (jo["body"] != null && jo["body"].Type != JTokenType.Null)
            { note = "body が既にあるため書き換えません"; return false; }

            var pos = new MotionScalarTrackDTO[3];
            var rot = new MotionScalarTrackDTO[4];
            for (int c = 0; c < 3; c++) found.TryGetValue(PosNames[c], out pos[c]);
            for (int c = 0; c < 4; c++) found.TryGetValue(RotNames[c], out rot[c]);
            bool hasPos = Array.Exists(pos, t => t?.keys != null && t.keys.Count > 0);
            bool hasRot = Array.TrueForAll(rot, t => t?.keys != null && t.keys.Count > 0);

            var times = new SortedSet<float>();
            foreach (var t in found.Values)
                if (t?.keys != null) foreach (var k in t.keys) if (k != null) times.Add(k.t);

            MotionScalarTrackDTO wrapSrc = pos[1] ?? (hasPos ? Array.Find(pos, t => t != null) : null) ?? rot[0];
            var body = new MotionTrackDTO
            {
                id           = MotionClipDTO.BodyTrackId,
                targetKind   = MotionClipDTO.TargetBody,
                preWrapMode  = wrapSrc?.preWrapMode,
                postWrapMode = wrapSrc?.postWrapMode,
            };

            foreach (float t in times)
            {
                var key = new MotionKeyDTO { t = t };
                if (hasPos) Fill(pos, t, 0f, out key.pos, out key.posTan);
                if (hasRot) Fill(rot, t, 0f, out key.rot, out key.rotTan);
                body.keys.Add(key);
            }
            MotionCurveMath.EnsureQuaternionContinuity(body);

            jo["muscles"] = keep;
            var settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
            jo["body"] = JObject.FromObject(body, JsonSerializer.Create(settings));
            note = $"Root {found.Count} 本を body（{body.keys.Count} キー）へ移しました"
                 + (hasRot ? "" : "（RootQ が 4 本そろわないため回転は移していません）");
            return true;
        }

        // 成分ごとに、その時刻のキーがあれば値と接線、無ければ評価値（接線はチャンネルごと捨てる）。
        private static void Fill(MotionScalarTrackDTO[] comps, float t, float missing,
                                 out float[] v, out MotionTangentDTO[] tan)
        {
            v   = new float[comps.Length];
            tan = new MotionTangentDTO[comps.Length];
            bool allKeyed = true, anyTan = false;
            for (int c = 0; c < comps.Length; c++)
            {
                var tr = comps[c];
                var k  = tr?.keys?.Find(x => x != null && x.t == t);
                if (k != null) { v[c] = k.v; tan[c] = k.tan; anyTan |= k.tan != null; }
                else
                {
                    allKeyed = false;
                    v[c] = tr != null ? MotionCurveMath.EvaluateScalar(tr, t, missing) : missing;
                }
            }
            if (!allKeyed || !anyTan) tan = null;
        }
    }
}
