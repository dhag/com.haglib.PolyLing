// MirrorNameOps.cs
// ミラー生成物の命名規則。Editor / Runtime 共有。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【なぜ要るか】
//   従来のミラー命名は MirrorBranchOps.MirrorBranchSuffix（"+"）を足すだけで、
//   「左腕」のミラーが「左腕+」になっていた。衝突回避としては正しいが、
//   左右の意味論をどこにも持っていないため、生成された右半身が
//   ヒエラルキー上ずっと「左」を名乗り続ける。
//
// 【方針】
//   1) 左右対応辞書で解決できるならそれを使う（左腕 → 右腕）。
//   2) 解決できない名前（センター・上半身など左右を持たないもの）だけ
//      従来どおり接尾辞にフォールバックする（センター → センター+）。
//   これで「意味のある名前は意味どおりに、それ以外は衝突回避だけ」になる。
//
// 【対応させる表記】
//   日本語  : 左 ⇔ 右
//   英語語頭: Left ⇔ Right / left ⇔ right
//   接頭辞  : L_ ⇔ R_
//   接尾辞  : _L ⇔ _R / .L ⇔ .R / -L ⇔ -R
//   Humanoid: LeftUpperArm ⇔ RightUpperArm / Left Thumb Proximal ⇔ Right …

using System;
using System.Collections.Generic;

namespace Poly_Ling.Ops
{
    public static class MirrorNameOps
    {
        // ================================================================
        // オブジェクト名の左右入れ替え
        // ================================================================

        /// <summary>
        /// オブジェクト名の左右を入れ替える。左右を持たない名前は null。
        /// </summary>
        public static string SwapLeftRight(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // 語頭・語中の日本語「左」「右」。最初の1文字だけを対象にする
            // （「左手首の左端」のような名前で全置換すると意味が壊れるため）。
            int jp = IndexOfAny(name, '左', '右');
            if (jp >= 0)
            {
                char c = name[jp] == '左' ? '右' : '左';
                return name.Substring(0, jp) + c + name.Substring(jp + 1);
            }

            foreach (var (a, b) in AffixPairs)
            {
                if (name.StartsWith(a, StringComparison.Ordinal))
                    return b + name.Substring(a.Length);
                if (name.StartsWith(b, StringComparison.Ordinal))
                    return a + name.Substring(b.Length);
            }

            foreach (var (a, b) in SuffixPairs)
            {
                if (name.EndsWith(a, StringComparison.Ordinal))
                    return name.Substring(0, name.Length - a.Length) + b;
                if (name.EndsWith(b, StringComparison.Ordinal))
                    return name.Substring(0, name.Length - b.Length) + a;
            }

            return null;
        }

        /// <summary>
        /// ミラー側に付ける名前を決める。
        /// 左右対応で解決できればそれを、できなければ接尾辞版を返す。
        /// nameExists には「その名前が既に使われているか」を渡す（null 可）。
        /// 左右入れ替えた名前が既に居る場合は衝突するので接尾辞へ落とす。
        /// </summary>
        public static string MakeMirrorName(
            string sourceName, string fallbackSuffix, Func<string, bool> nameExists = null)
        {
            string swapped = SwapLeftRight(sourceName);

            if (!string.IsNullOrEmpty(swapped) &&
                (nameExists == null || !nameExists(swapped)))
                return swapped;

            return (sourceName ?? string.Empty) + fallbackSuffix;
        }

        // ================================================================
        // Humanoid 名の左右入れ替え
        // ================================================================

        /// <summary>
        /// Humanoid ボーン名の左右を入れ替える。
        /// HumanBodyBones 列挙形（"LeftUpperArm"）と
        /// HumanTrait.BoneName 形（"Left Thumb Proximal"）の双方を扱う。
        /// 左右を持たない名前は null。
        /// </summary>
        public static string SwapHumanoidLeftRight(string humanName)
        {
            if (string.IsNullOrEmpty(humanName)) return null;

            if (humanName.StartsWith("Left", StringComparison.Ordinal))
                return "Right" + humanName.Substring(4);

            if (humanName.StartsWith("Right", StringComparison.Ordinal))
                return "Left" + humanName.Substring(5);

            return null;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static readonly (string, string)[] AffixPairs =
        {
            ("Left",  "Right"),
            ("left",  "right"),
            ("L_",    "R_"),
            ("l_",    "r_"),
        };

        private static readonly (string, string)[] SuffixPairs =
        {
            ("_L", "_R"), ("_l", "_r"),
            (".L", ".R"), (".l", ".r"),
            ("-L", "-R"), ("-l", "-r"),
            ("Left", "Right"), ("left", "right"),
        };

        private static int IndexOfAny(string s, char a, char b)
        {
            for (int i = 0; i < s.Length; i++)
                if (s[i] == a || s[i] == b) return i;
            return -1;
        }
    }
}
