// Original CSG.JS library by Evan Wallace (http://madebyevan.com), under the MIT license.
// GitHub: https://github.com/evanw/csg.js/
//
// C++ port by Tomasz Dabrowski (http://28byteslater.com), under the MIT license.
// GitHub: https://github.com/dabroz/csgjs-cpp/
//
// C# port by Karl Henkel (parabox.co), under MIT license.
// GitHub: https://github.com/karl-/pb_CSG
//
// PolyLing 改変: namespace のみ Parabox.CSG -> Poly_Ling.CSG。
// 詳細は同フォルダの LICENSE.txt を参照。

namespace Poly_Ling.CSG
{
    /// <summary>
    /// Mesh attributes bitmask.
    /// </summary>
    [System.Flags]
    public enum VertexAttributes
    {
        /// <summary>
        /// Vertex positions.
        /// </summary>
        Position = 0x1,
        /// <summary>
        /// First UV channel.
        /// </summary>
        Texture0 = 0x2,
        /// <summary>
        /// Second UV channel. Commonly called UV2 or Lightmap UVs in Unity terms.
        /// </summary>
        Texture1 = 0x4,
        /// <summary>
        /// Second UV channel. Commonly called UV2 or Lightmap UVs in Unity terms.
        /// </summary>
        Lightmap = 0x4,
        /// <summary>
        /// Third UV channel.
        /// </summary>
        Texture2 = 0x8,
        /// <summary>
        /// Vertex UV4.
        /// </summary>
        Texture3 = 0x10,
        /// <summary>
        /// Vertex colors.
        /// </summary>
        Color = 0x20,
        /// <summary>
        /// Vertex normals.
        /// </summary>
        Normal = 0x40,
        /// <summary>
        /// Vertex tangents.
        /// </summary>
        Tangent = 0x80,
        /// <summary>
        /// All stored mesh attributes.
        /// </summary>
        All = 0xFF
    };
}
