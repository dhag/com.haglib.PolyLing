// VrmExpressionNames.cs
// ============================================================
// 表情名 → VRM 1.0 の表情名（preset 名か custom 名）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
//
// .vrm 書き出し（Vrm10SceneAssembler）と .vrma 書き出し（VrmaCodec）が共用する唯一の表。
// UniVRM に依存しない（preset 名は文字列で持つ。綴りは VRMC_vrm 1.0 の expressions.preset の名前）。
// ============================================================

using System;
using System.Collections.Generic;

namespace Poly_Ling.Motion
{
    public static class VrmExpressionNames
    {
        /// <summary>VRM 1.0 の preset 表情名（仕様の綴り）。</summary>
        public static readonly string[] Presets =
        {
            "happy", "angry", "sad", "relaxed", "surprised",
            "aa", "ih", "ou", "ee", "oh",
            "blink", "blinkLeft", "blinkRight",
            "lookUp", "lookDown", "lookLeft", "lookRight",
            "neutral",
        };

        private static Dictionary<string, string> _presetByLower;

        /// <summary>name が preset 名なら（大文字小文字を問わず）仕様の綴りを返す。</summary>
        public static bool TryGetPreset(string name, out string preset)
        {
            preset = null;
            if (string.IsNullOrEmpty(name)) return false;
            if (_presetByLower == null)
            {
                var d = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in Presets) d[p.ToLowerInvariant()] = p;
                _presetByLower = d;
            }
            return _presetByLower.TryGetValue(name.Trim().ToLowerInvariant(), out preset);
        }

        /// <summary>視線の preset（VRMA には書けない。視線は lookAt で表す）。</summary>
        public static bool IsLookPreset(string preset)
            => preset == "lookUp" || preset == "lookDown" || preset == "lookLeft" || preset == "lookRight";

        /// <summary>PMX の標準モーフ名 → VRM preset 名。</summary>
        public static readonly IReadOnlyDictionary<string, string> PresetByPmxName =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "あ",           "aa" },
                { "い",           "ih" },
                { "う",           "ou" },
                { "え",           "ee" },
                { "お",           "oh" },
                { "まばたき",     "blink" },
                { "ウィンク",     "blinkLeft" },
                { "ウィンク右",   "blinkRight" },
                { "笑い",         "happy" },
                { "怒り",         "angry" },
                { "困る",         "sad" },
                { "リラックス",   "relaxed" },
                { "驚き",         "surprised" },
                { "上",           "lookUp" },
                { "下",           "lookDown" },
                { "左",           "lookLeft" },
                { "右",           "lookRight" },
                { "Neutral",      "neutral" },
            };

        /// <summary>
        /// 表情名から VRM の preset を決める。英語名が preset 名ならそれ、
        /// そうでなければ PMX の標準モーフ名で引く。決まらなければ false（custom 扱い）。
        /// </summary>
        public static bool TryResolvePreset(string name, string nameEnglish, out string preset)
        {
            if (TryGetPreset(nameEnglish, out preset)) return true;
            if (TryGetPreset(name, out preset)) return true;
            if (!string.IsNullOrEmpty(name) && PresetByPmxName.TryGetValue(name.Trim(), out preset)) return true;
            preset = null;
            return false;
        }
    }
}
