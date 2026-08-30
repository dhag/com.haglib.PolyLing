// Original CSG.JS library by Evan Wallace (http://madebyevan.com), under the MIT license.
// GitHub: https://github.com/evanw/csg.js/
//
// C++ port by Tomasz Dabrowski (http://28byteslater.com), under the MIT license.
// GitHub: https://github.com/dabroz/csgjs-cpp/
//
// C# port by Karl Henkel (parabox.co), under MIT license.
// GitHub: https://github.com/karl-/pb_CSG
//
// PolyLing 改変:
//   - namespace を Parabox.CSG -> Poly_Ling.CSG。
//   - material (UnityEngine.Material) を materialIndex (int) に変更。
//     PolyLing の MeshObject は Face.MaterialIndex（サブメッシュ番号）で
//     マテリアルを表すため、Material インスタンスは保持しない。
//   - Flip() の頂点法線反転を修正。元コードは
//         for (...) vertices[i].Flip();
//     だが Vertex は struct、vertices は List<Vertex> のため、
//     インデクサの戻り値（一時コピー）を書き換えるだけで
//     リストの要素は変化しない（法線・接線が反転しない）。
//     書き戻す形に修正した。
// 詳細は同フォルダの LICENSE.txt を参照。

using System.Collections.Generic;

namespace Poly_Ling.CSG
{
    /// <summary>
    /// Represents a polygon face with an arbitrary number of vertices.
    /// </summary>
    sealed class Polygon
    {
        public List<Vertex> vertices;
        public Plane plane;

        /// <summary>
        /// この面が属するサブメッシュ番号（PolyLing の Face.MaterialIndex と同値）。
        /// </summary>
        public int materialIndex;

        public Polygon(List<Vertex> list, int materialIndex)
        {
            vertices = list;
            plane = new Plane(list[0].position, list[1].position, list[2].position);
            this.materialIndex = materialIndex;
        }

        public void Flip()
        {
            vertices.Reverse();

            // Vertex は struct。List のインデクサは値を返すため、
            // 反転した値を必ず書き戻すこと。
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                v.Flip();
                vertices[i] = v;
            }

            plane.Flip();
        }

        public override string ToString()
        {
            return $"[{vertices.Count}] {plane.normal} mat={materialIndex}";
        }
    }
}
