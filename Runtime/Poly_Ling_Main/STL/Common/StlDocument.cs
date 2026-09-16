// StlDocument.cs
// STL（ASCII / バイナリ）の読み込み結果と、上方向の変換規則。
// Runtime/Poly_Ling_Main/STL/Common/ に配置
//
// 【STL の中身】
//   三角形の並びだけを持つ。頂点の共有・UV・材質・階層は無い。
//   facet の法線は頂点順（外から見て反時計回り＝右手系）と重複する情報で、
//   0 ベクトルを書く書き手もあるため、読み込み側では使わない。
//
// 【上方向】
//   STL は上方向を規格で決めていない。Y 上と Z 上の 2 通りを選べるようにする。
//   Z 上（正面 = -Y）から Y 上（正面 = +Z）へは X 軸まわり -90° の回転
//     (x, y, z) → (x, z, -y)
//   で揃う。純粋な回転なので手系は変わらず、巻き順の反転は不要。
//   手系の変換（右手系 → Unity 左手系）は、この後に AxisFlip で別に行う。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.STL
{
    /// <summary>STL ファイルの上方向。</summary>
    public enum StlUpAxis
    {
        /// <summary>+Y が上（OBJ / メタセコイアと同じ置き方）。</summary>
        Y = 0,

        /// <summary>+Z が上・正面 -Y（3D プリンタ・CAD 系）。</summary>
        Z = 1,
    }

    /// <summary>上方向の変換。</summary>
    public static class StlAxis
    {
        /// <summary>ファイルの座標（上方向 up）を Y 上の右手系へ直す。</summary>
        public static Vector3 ToYUp(StlUpAxis up, Vector3 p)
        {
            if (up == StlUpAxis.Z) return new Vector3(p.x, p.z, -p.y);
            return p;
        }

        /// <summary>Y 上の右手系の座標をファイルの座標（上方向 up）へ直す。ToYUp の逆。</summary>
        public static Vector3 FromYUp(StlUpAxis up, Vector3 p)
        {
            if (up == StlUpAxis.Z) return new Vector3(p.x, -p.z, p.y);
            return p;
        }
    }

    /// <summary>三角形 1 枚。座標はファイルに書かれた値のまま。</summary>
    public struct StlTriangle
    {
        public Vector3 Normal;
        public Vector3 V0;
        public Vector3 V1;
        public Vector3 V2;
    }

    /// <summary>solid 1 個（ASCII の solid〜endsolid。バイナリは全体で 1 個）。</summary>
    public class StlSolid
    {
        public string Name;
        public List<StlTriangle> Triangles { get; } = new List<StlTriangle>();
    }

    /// <summary>STL ファイル全体。</summary>
    public class StlDocument
    {
        /// <summary>拡張子を除いたファイル名。</summary>
        public string FileName;

        /// <summary>バイナリ形式だったか。</summary>
        public bool IsBinary;

        public List<StlSolid> Solids { get; } = new List<StlSolid>();

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (var s in Solids) n += s.Triangles.Count;
                return n;
            }
        }
    }
}
