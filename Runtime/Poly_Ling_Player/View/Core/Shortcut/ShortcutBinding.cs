// ShortcutBinding.cs
// キーボードショートカットの「キー + 修飾キー」の組。
// ShortcutMap の辞書キーとして使う不変の値型。
//
// Runtime/Poly_Ling_Player/View/Core/Shortcut/ に配置

using System;
using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 1 つのショートカット入力（KeyCode と Ctrl/Shift/Alt の有無）を表す値型。
    /// 辞書キーにするため IEquatable と GetHashCode を実装している。
    /// </summary>
    public readonly struct ShortcutBinding : IEquatable<ShortcutBinding>
    {
        public readonly KeyCode Key;
        public readonly bool    Ctrl;
        public readonly bool    Shift;
        public readonly bool    Alt;

        public ShortcutBinding(KeyCode key, bool ctrl, bool shift, bool alt)
        {
            Key   = key;
            Ctrl  = ctrl;
            Shift = shift;
            Alt   = alt;
        }

        public bool Equals(ShortcutBinding other)
            => Key == other.Key
            && Ctrl == other.Ctrl
            && Shift == other.Shift
            && Alt == other.Alt;

        public override bool Equals(object obj)
            => obj is ShortcutBinding b && Equals(b);

        public override int GetHashCode()
        {
            int h = (int)Key;
            h = (h * 397) ^ (Ctrl  ? 1 : 0);
            h = (h * 397) ^ (Shift ? 1 : 0);
            h = (h * 397) ^ (Alt   ? 1 : 0);
            return h;
        }

        public override string ToString()
        {
            string mods = string.Empty;
            if (Ctrl)  mods += "Ctrl+";
            if (Shift) mods += "Shift+";
            if (Alt)   mods += "Alt+";
            return mods + Key;
        }
    }
}
