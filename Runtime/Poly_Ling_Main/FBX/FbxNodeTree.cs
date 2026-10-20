// FbxNodeTree.cs
// ============================================================
// FBX のノード木（書き出し用）
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 7。
//
// FBX ファイルは「名前・プロパティの列・子ノード」の木。バイナリ（FbxBinaryWriter）と
// ASCII（FbxAsciiWriter）は同じ木を書く。
//
// ■ プロパティの型（C# の型 → FBX の型記号）
//   short Y / bool C / int I / float F / double D / long L / string S / byte[] R
//   float[] f / double[] d / long[] l / int[] i / bool[] b
//   オブジェクト名「名前\0\x01クラス」はバイナリではそのまま、ASCII では「クラス::名前」にする。
// ============================================================

using System.Collections.Generic;

namespace Poly_Ling.Fbx
{
    public sealed class FbxNode
    {
        public readonly string Name;
        public readonly List<object> Props = new List<object>();
        public List<FbxNode> Children;

        public FbxNode(string name, params object[] props)
        {
            Name = name;
            if (props != null) Props.AddRange(props);
        }

        /// <summary>子ノードを足して返す。</summary>
        public FbxNode Add(string name, params object[] props)
        {
            var c = new FbxNode(name, props);
            (Children ?? (Children = new List<FbxNode>())).Add(c);
            return c;
        }

        /// <summary>作った子ノードを足して返す。</summary>
        public FbxNode Add(FbxNode child)
        {
            (Children ?? (Children = new List<FbxNode>())).Add(child);
            return child;
        }

        /// <summary>
        /// 子が無くても入れ子の区切り（バイナリの空レコード、ASCII の { }）を書く印を付けて返す。
        /// Objects の下のオブジェクトはすべてこれを付ける（FBX SDK の書くファイルと同じ）。区切りが無いと、
        /// 子を持たないオブジェクト（AnimationLayer など）を FBX SDK が読まず、Unity の取り込みでテイクが消える
        /// （2026-10-20、FBX SDK の書いたファイルから区切りだけを外して確かめた）。
        /// </summary>
        public FbxNode Block()
        {
            if (Children == null) Children = new List<FbxNode>();
            return this;
        }

        /// <summary>入れ子の区切りを書くか（子がある、区切りの印がある、プロパティが無い）。</summary>
        public bool HasBlock => Children != null || Props.Count == 0;

        /// <summary>オブジェクト名「名前\0\x01クラス」。</summary>
        public static string ObjectName(string name, string cls) => (name ?? "") + "\u0000\u0001" + cls;
    }

    /// <summary>Properties70 の P 行を組む。</summary>
    internal static class FbxProps
    {
        public static void Int(FbxNode p70, string name, int v)
            => p70.Add("P", name, "int", "Integer", "", v);

        public static void Enum(FbxNode p70, string name, int v)
            => p70.Add("P", name, "enum", "", "", v);

        public static void Bool(FbxNode p70, string name, bool v)
            => p70.Add("P", name, "bool", "", "", v ? 1 : 0);

        public static void Double(FbxNode p70, string name, double v)
            => p70.Add("P", name, "double", "Number", "", v);

        public static void Number(FbxNode p70, string name, double v, bool animatable = true)
            => p70.Add("P", name, "Number", "", animatable ? "A" : "", v);

        public static void ColorRGB(FbxNode p70, string name, double r, double g, double b)
            => p70.Add("P", name, "ColorRGB", "Color", "", r, g, b);

        public static void Color(FbxNode p70, string name, double r, double g, double b)
            => p70.Add("P", name, "Color", "", "A", r, g, b);

        public static void Vector3D(FbxNode p70, string name, double x, double y, double z)
            => p70.Add("P", name, "Vector3D", "Vector", "", x, y, z);

        public static void Vector(FbxNode p70, string name, double x, double y, double z)
            => p70.Add("P", name, "Vector", "", "A", x, y, z);

        public static void Lcl(FbxNode p70, string name, double x, double y, double z)
            => p70.Add("P", name, name, "", "A", x, y, z);

        public static void KString(FbxNode p70, string name, string v)
            => p70.Add("P", name, "KString", "", "", v ?? "");

        public static void KTime(FbxNode p70, string name, long v)
            => p70.Add("P", name, "KTime", "Time", "", v);
    }
}
