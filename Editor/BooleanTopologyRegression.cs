// Unity Editor 上で実コードを使う回帰検証。シーンやモデル一覧は変更しない。
// 実行: Tools > PolyLing > Diagnostics > Run Boolean Topology Regression
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.EditorDiagnostics
{
    public static class BooleanTopologyRegression
    {
        private static int passed, failed;

        [MenuItem("Tools/PolyLing/Diagnostics/Run Boolean Topology Regression")]
        public static void Run()
        {
            passed = failed = 0;
            Check("T字とUV/法線", TestSplitEdge);
            Check("両側の分割位置が違う辺", TestCrossedSubdivision);
            Check("近くの孤立頂点を挿入しない", TestUnrelatedVertex);
            Check("同方向の境界辺を接続しない", TestSameDirection);

            foreach (float offset in new[] { 0f, 3f })
            foreach (float threshold in new[] { 1e-5f, 1e-4f })
            foreach (BooleanOpKind op in Enum.GetValues(typeof(BooleanOpKind)))
            {
                float x = offset, t = threshold;
                BooleanOpKind operation = op;
                Check($"球/円柱 {op} x={x} merge={t:G3}", () => TestSphereCylinder(operation, x, t));
            }
            Check("直方体の差", TestCubes);
            Debug.Log($"[BooleanTopologyRegression] PASS={passed} FAIL={failed}");
            if (failed != 0) throw new InvalidOperationException("ブーリアン回帰検証に失敗。各ケースのログを確認すること。");
        }

        private static void Check(string name, Action action)
        {
            try { action(); passed++; Debug.Log("[BooleanTopologyRegression] PASS " + name); }
            catch (Exception e) { failed++; Debug.LogError("[BooleanTopologyRegression] FAIL " + name + "\n" + e); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void RequireClosed(MeshObject mesh)
        {
            var c = BoundaryEdgeOps.AnalyzeTopology(mesh);
            Require(mesh.FaceCount > 0 && c.BoundaryEdges == 0 && c.NonManifoldEdges == 0 && c.InconsistentWindingEdges == 0,
                $"面={mesh.FaceCount} 境界={c.BoundaryEdges} 3面以上={c.NonManifoldEdges} 同方向={c.InconsistentWindingEdges}");
        }

        private static MeshObject Tetrahedron()
        {
            var m = new MeshObject("TjunctionRegression");
            var points = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward,
                new Vector3(0.3f, 0, 0), new Vector3(0.7f, 0, 0) };
            foreach (var p in points)
            {
                var v = new Vertex(p, new Vector2(9, 9), Vector3.back);
                v.UVs.Add(new Vector2(p.x, p.y));
                v.Normals.Add(Vector3.forward);
                m.Vertices.Add(v);
            }
            m.Faces.Add(new Face(0, 2, 1));
            m.Faces.Add(new Face(0, 4, 1, 3));
            m.Faces.Add(new Face(0, 3, 2));
            m.Faces.Add(new Face(1, 2, 3));
            return m;
        }

        private static void TestSplitEdge()
        {
            var m = Tetrahedron();
            var face = m.Faces[0];
            face.UVIndices = new List<int> { 1, 1, 1 };
            face.NormalIndices = new List<int> { 1, 1, 1 };
            int faces = m.FaceCount, vertices = m.VertexCount;
            var r = TJunctionOps.Resolve(m);
            Require(r.Inserted == 1 && r.TouchedFaces == 1, "分割は相手側の面1枚だけであるべき");
            Require(m.FaceCount == faces && m.VertexCount == vertices, "頂点・面を増減してはならない");
            RequireClosed(m);
            int corner = face.VertexIndices.IndexOf(4);
            Require(corner >= 0 && face.UVIndices.Count == face.VertexCount && face.NormalIndices.Count == face.VertexCount, "隅参照の長さ不一致");
            var v = m.Vertices[4];
            Require((v.UVs[face.UVIndices[corner]] - new Vector2(0.3f, 0)).sqrMagnitude < 1e-10f, "追加した隅のUV補間に失敗");
            Require(v.Normals[face.NormalIndices[corner]] == Vector3.forward, "別の面の法線を使っている");
            for (int i = 0; i < face.VertexCount; i++)
                if (face.VertexIndices[i] != 4) Require(face.UVIndices[i] == 1 && face.NormalIndices[i] == 1, "元の隅参照がずれた");
            Require(TJunctionOps.Resolve(m).Inserted == 0, "2回目で結果が変わる");
        }

        private static void TestCrossedSubdivision()
        {
            var m = Tetrahedron();
            m.Faces[0] = new Face(0, 2, 1, 5);
            Require(TJunctionOps.Resolve(m).Inserted == 2, "両側の異なる分割点を共有できていない");
            RequireClosed(m);
        }

        private static void TestUnrelatedVertex()
        {
            var m = Tetrahedron();
            m.Faces[1] = new Face(0, 1, 3);
            m.Vertices.Add(new Vertex(new Vector3(0.5f, 0.00001f, 0)));
            RequireClosed(m);
            Require(TJunctionOps.Resolve(m).Inserted == 0, "正常な共有辺を近くの無関係な頂点で分割した");
            RequireClosed(m);
        }

        private static void TestSameDirection()
        {
            var m = Tetrahedron();
            m.Faces.Clear();
            m.Faces.Add(new Face(0, 1, 2));
            m.Faces.Add(new Face(0, 4, 3));
            Require(TJunctionOps.Resolve(m).Inserted == 0, "同方向の辺を縫い合わせてはならない");
        }

        private static void TestSphereCylinder(BooleanOpKind op, float offset, float threshold)
        {
            var sp = SphereMeshGenerator.SphereParams.Default;
            sp.Radius = 0.5f; sp.LongitudeSegments = 32; sp.LatitudeSegments = 24;
            sp.Pivot = Vector3.zero; sp.CubeSphere = false;
            var cp = CylinderMeshGenerator.CylinderParams.Default;
            cp.RadiusTop = cp.RadiusBottom = 0.2f; cp.Height = 1.5f;
            cp.RadialSegments = 24; cp.HeightSegments = 6;
            cp.CapTop = cp.CapBottom = true; cp.EdgeRadius = 0;
            // 底面を基準に生成してから -0.75 移動し、円柱中心を球中心と同じ高さにする。
            cp.Pivot = new Vector3(0, -0.5f, 0);
            var a = SphereMeshGenerator.Generate(sp);
            var b = CylinderMeshGenerator.Generate(cp);
            MeshMergeHelper.MergeAllVerticesAtSamePosition(a, 0.001f);
            MeshMergeHelper.MergeAllVerticesAtSamePosition(b, 0.001f);
            Require(a.VertexCount == 738 && b.VertexCount == 170, "入力の頂点数が調査条件と異なる");
            RequireClosed(a); RequireClosed(b);
            var result = BooleanOps.Perform(op,
                a, Matrix4x4.Translate(new Vector3(offset, 0, 0)),
                b, Matrix4x4.Translate(new Vector3(offset + 0.1f, -0.75f, 0.05f)),
                Matrix4x4.Translate(new Vector3(-offset, 0, 0)),
                epsilon: 1e-6f, mergeVertices: true, mergeThreshold: threshold);
            Require(result.Success && result.Mesh != null, result.Message);
            RequireClosed(result.Mesh);
            foreach (var face in result.Mesh.Faces)
            {
                Require(face.VertexCount == 3, "出力に未分割の面が残っている");
                Vector3 a0 = result.Mesh.Vertices[face.VertexIndices[0]].Position;
                Vector3 b0 = result.Mesh.Vertices[face.VertexIndices[1]].Position;
                Vector3 c0 = result.Mesh.Vertices[face.VertexIndices[2]].Position;
                double ax = (double)b0.x-a0.x, ay = (double)b0.y-a0.y, az = (double)b0.z-a0.z;
                double bx = (double)c0.x-a0.x, by = (double)c0.y-a0.y, bz = (double)c0.z-a0.z;
                Require(ay*bz-az*by != 0 || az*bx-ax*bz != 0 || ax*by-ay*bx != 0, "面積0の三角形がある");
            }
            Require(TJunctionOps.Resolve(result.Mesh, result.ActualMergeThreshold).Inserted == 0, "BooleanOpsの後処理が完了していない");
            RequireClosed(a); RequireClosed(b);
            Debug.Log($"[BooleanTopologyRegression] actualMergeThreshold={result.ActualMergeThreshold:G6} attempts={result.PostprocessAttempts}");
        }

        private static void TestCubes()
        {
            var p = CubeMeshGenerator.CubeParams.Default;
            p.WidthTop = p.WidthBottom = p.DepthTop = p.DepthBottom = p.Height = 6;
            p.CornerRadius = 0; p.Pivot = Vector3.zero; p.Subdivisions = new Vector3Int(6, 6, 6);
            var a = CubeMeshGenerator.Generate(p);
            p.WidthTop = p.WidthBottom = p.DepthTop = p.DepthBottom = p.Height = 3;
            p.Subdivisions = new Vector3Int(3, 3, 3);
            var b = CubeMeshGenerator.Generate(p);
            MeshMergeHelper.MergeAllVerticesAtSamePosition(a, 0.001f);
            MeshMergeHelper.MergeAllVerticesAtSamePosition(b, 0.001f);
            var result = BooleanOps.Perform(BooleanOpKind.Subtract, a, Matrix4x4.identity,
                b, Matrix4x4.Translate(new Vector3(1.5f, 1.5f, 1.5f)), Matrix4x4.identity,
                epsilon: 1e-6f, mergeVertices: true, mergeThreshold: 1e-4f);
            Require(result.Success && result.Mesh != null, result.Message);
            RequireClosed(result.Mesh);
            foreach (var face in result.Mesh.Faces)
            {
                Require(face.VertexCount == 3, "出力に未分割の面が残っている");
                Vector3 a0 = result.Mesh.Vertices[face.VertexIndices[0]].Position;
                Vector3 b0 = result.Mesh.Vertices[face.VertexIndices[1]].Position;
                Vector3 c0 = result.Mesh.Vertices[face.VertexIndices[2]].Position;
                double ax = (double)b0.x-a0.x, ay = (double)b0.y-a0.y, az = (double)b0.z-a0.z;
                double bx = (double)c0.x-a0.x, by = (double)c0.y-a0.y, bz = (double)c0.z-a0.z;
                Require(ay*bz-az*by != 0 || az*bx-ax*bz != 0 || ax*by-ay*bx != 0, "面積0の三角形がある");
            }
        }
    }
}
