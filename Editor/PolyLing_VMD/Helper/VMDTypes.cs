// VMDTypes.cs
// VMD読み込み用の基本型定義
// HagLib.Helper から移植・Unity対応版

using System;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// 文字列エンコーディング種別
    /// </summary>
    public enum EncodeType
    {
        UTF16LE = 0,
        UTF8 = 1,
        SJIS = 999
    }

    /// <summary>
    /// RGB色構造体（PMX/VMD用）
    /// </summary>
    public struct Color3
    {
        public float Red;
        public float Green;
        public float Blue;

        public Color3(float value)
        {
            Red = Green = Blue = value;
        }

        public Color3(float red, float green, float blue)
        {
            Red = red;
            Green = green;
            Blue = blue;
        }

        public Color3(Vector3 value)
        {
            Red = value.x;
            Green = value.y;
            Blue = value.z;
        }

        // Unity Color への変換
        public static implicit operator Color(Color3 val)
        {
            return new Color(val.Red, val.Green, val.Blue, 1f);
        }

        public static implicit operator Vector3(Color3 val)
        {
            return new Vector3(val.Red, val.Green, val.Blue);
        }

        public static implicit operator Color3(Color val)
        {
            return new Color3(val.r, val.g, val.b);
        }
    }

    /// <summary>
    /// RGBA色構造体（PMX/VMD用）
    /// </summary>
    public struct Color4
    {
        public float Red;
        public float Green;
        public float Blue;
        public float Alpha;

        public Color4(float value)
        {
            Alpha = Red = Green = Blue = value;
        }

        public Color4(float red, float green, float blue, float alpha)
        {
            Red = red;
            Green = green;
            Blue = blue;
            Alpha = alpha;
        }

        public Color4(Vector4 value)
        {
            Red = value.x;
            Green = value.y;
            Blue = value.z;
            Alpha = value.w;
        }

        // Unity Color への変換
        public static implicit operator Color(Color4 val)
        {
            return new Color(val.Red, val.Green, val.Blue, val.Alpha);
        }

        public static implicit operator Vector4(Color4 val)
        {
            return new Vector4(val.Red, val.Green, val.Blue, val.Alpha);
        }

        public static implicit operator Color4(Color val)
        {
            return new Color4(val.r, val.g, val.b, val.a);
        }
    }
}
