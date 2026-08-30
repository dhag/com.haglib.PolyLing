// Original CSG.JS library by Evan Wallace (http://madebyevan.com), under the MIT license.
// GitHub: https://github.com/evanw/csg.js/
//
// C++ port by Tomasz Dabrowski (http://28byteslater.com), under the MIT license.
// GitHub: https://github.com/dabroz/csgjs-cpp/
//
// C# port by Karl Henkel (parabox.co), under MIT license.
// GitHub: https://github.com/karl-/pb_CSG
//
// Constructive Solid Geometry (CSG) is a modeling technique that uses Boolean
// operations like union and intersection to combine 3D solids. This library
// implements CSG operations on meshes elegantly and concisely using BSP trees,
// and is meant to serve as an easily understandable implementation of the
// algorithm. All edge cases involving overlapping coplanar polygons in both
// solids are correctly handled.
//
// PolyLing 改変:
//   - namespace を Parabox.CSG -> Poly_Ling.CSG。
//   - 入口を GameObject 版から Model 版へ変更（GameObject 依存を削除）。
// 詳細は同フォルダの LICENSE.txt を参照。

using UnityEngine;
using System.Collections.Generic;

namespace Poly_Ling.CSG
{
    /// <summary>
    /// Base class for CSG operations. Contains Model level methods for Subtraction, Intersection, and Union
    /// operations. The Models passed to these functions will not be modified.
    /// </summary>
    public static class CSG
    {
        public enum BooleanOp
        {
            Intersection,
            Union,
            Subtraction
        }

        public const float k_DefaultEpsilon = 0.00001f;
        static float s_Epsilon = k_DefaultEpsilon;

        /// <summary>
        /// Tolerance used by <see cref="Plane.SplitPolygon"/> to determine whether planes are coincident.
        /// </summary>
        /// <remarks>
        /// Plane のコンストラクタは法線を正規化しないため（元コードのまま）、
        /// ここで比較される値は三角形の面積に比例する。したがって実効的な
        /// 許容量はメッシュの大きさに依存する。
        /// </remarks>
        public static float epsilon
        {
            get => s_Epsilon;
            set => s_Epsilon = value;
        }

        /// <summary>
        /// Performs a boolean operation on two Models.
        /// </summary>
        /// <returns>A new Model.</returns>
        public static Model Perform(BooleanOp op, Model lhs, Model rhs)
        {
            switch (op)
            {
                case BooleanOp.Intersection:
                    return Intersect(lhs, rhs);
                case BooleanOp.Union:
                    return Union(lhs, rhs);
                case BooleanOp.Subtraction:
                    return Subtract(lhs, rhs);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Returns a new Model by merging @lhs with @rhs.
        /// </summary>
        public static Model Union(Model lhs, Model rhs)
        {
            Node a = new Node(lhs.ToPolygons());
            Node b = new Node(rhs.ToPolygons());

            List<Polygon> polygons = Node.Union(a, b).AllPolygons();

            return new Model(polygons);
        }

        /// <summary>
        /// Returns a new Model by subtracting @rhs from @lhs.
        /// </summary>
        public static Model Subtract(Model lhs, Model rhs)
        {
            Node a = new Node(lhs.ToPolygons());
            Node b = new Node(rhs.ToPolygons());

            List<Polygon> polygons = Node.Subtract(a, b).AllPolygons();

            return new Model(polygons);
        }

        /// <summary>
        /// Returns a new Model by intersecting @lhs with @rhs.
        /// </summary>
        public static Model Intersect(Model lhs, Model rhs)
        {
            Node a = new Node(lhs.ToPolygons());
            Node b = new Node(rhs.ToPolygons());

            List<Polygon> polygons = Node.Intersect(a, b).AllPolygons();

            return new Model(polygons);
        }
    }
}
