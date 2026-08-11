// ObjectPoseWedgeShape.cs
// オブジェクト姿勢を表す「くさび」の形状定義と、形状からの姿勢復元。
// Runtime/Poly_Ling_Main/Tools/ObjectPose/ に配置
//
// 【形状】
//   MeshSceneRenderer.BoneShapeVertices（ボーン線表示のくさび）と同じ比率を基にした
//   6頂点の八面体。根 = ローカル原点、先端 = +X、環は x = 0.5 の断面。
//
// 【前側(+Z)だけ広げている理由】
//   元のボーン形状は環が ±Z = 0.4 / ±Y = 0.2 で、X軸まわり180°回転に対して
//   自己対称になる。それだとロール（軸まわりの回転）が 180° の不定性を持ち、
//   取り込み時に姿勢を一意へ戻せない。
//   前側 +Z を後側 -Z より広くして対称性を壊すことで、
//   「軸 = 根→先端」「前 = 環の中で軸から最も遠い点」の2本で姿勢が確定する。
//   頂点の並び順に依存しないので、外部ツールを経由して戻ってきても読める。
//
// 【大きさ】
//   全長（根→先端）を Length とし、Length / UnitTipX を内部単位の倍率にする。
//   呼び出し側はさらにオブジェクトの拡大率平均を掛ける。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Tools.ObjectPose
{
    public static class ObjectPoseWedgeShape
    {
        // ================================================================
        // 形状定数（内部単位）
        // ================================================================

        /// <summary>先端の X 位置。全長の基準。</summary>
        public const float UnitTipX = 2.5f;

        /// <summary>環（断面）の X 位置。</summary>
        public const float UnitRingX = 0.5f;

        /// <summary>前側（+Z）の張り出し。後側より広い＝姿勢の目印。</summary>
        public const float UnitFrontZ = 0.45f;

        /// <summary>後側（-Z）の張り出し。</summary>
        public const float UnitBackZ = 0.25f;

        /// <summary>左右（±Y）の張り出し。</summary>
        public const float UnitSideY = 0.2f;

        /// <summary>くさび1個あたりの頂点数。</summary>
        public const int VertexCount = 6;

        // 頂点の並び（生成時の順序。読み取りは順序に依存しない）
        public const int IdxRoot  = 0;
        public const int IdxTip   = 1;
        public const int IdxPosY  = 2;
        public const int IdxPosZ  = 3;   // 前（広い）
        public const int IdxNegY  = 4;
        public const int IdxNegZ  = 5;   // 後（狭い）

        private static readonly Vector3[] UnitVertices =
        {
            new Vector3(0f,        0f,          0f),          // 0 根
            new Vector3(UnitTipX,  0f,          0f),          // 1 先端
            new Vector3(UnitRingX,  UnitSideY,  0f),          // 2 +Y
            new Vector3(UnitRingX,  0f,   UnitFrontZ),        // 3 +Z（前・広い）
            new Vector3(UnitRingX, -UnitSideY,  0f),          // 4 -Y
            new Vector3(UnitRingX,  0f,  -UnitBackZ),         // 5 -Z（後・狭い）
        };

        // 環を軸まわりに一周する順序（+Y → +Z → -Y → -Z）
        private static readonly int[] RingCycle = { IdxPosY, IdxPosZ, IdxNegY, IdxNegZ };

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// くさび1個を MeshObject として作る。頂点は place を通した座標で書く
        /// （呼び出し側がワールド行列を渡せば、そのままワールド座標になる）。
        /// </summary>
        /// <param name="name">オブジェクト名</param>
        /// <param name="place">根の位置と姿勢（拡大は含めない）</param>
        /// <param name="size">全長（根→先端）</param>
        public static MeshObject Build(string name, Matrix4x4 place, float size)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "Wedge" : name);

            float k = size / UnitTipX;

            var pos = new Vector3[VertexCount];
            for (int i = 0; i < VertexCount; i++)
                pos[i] = place.MultiplyPoint3x4(UnitVertices[i] * k);

            for (int i = 0; i < VertexCount; i++)
                mo.AddVertex(new Vertex(pos[i]));

            // 根側: (根, 次, 現) の順で外向き。先端側: (先端, 現, 次) の順で外向き。
            // 面法線は cross(p1-p0, p2-p0)（CubeMeshGenerator と同じ右ねじ規則）。
            for (int i = 0; i < RingCycle.Length; i++)
            {
                int cur  = RingCycle[i];
                int next = RingCycle[(i + 1) % RingCycle.Length];

                AddTriangle(mo, pos, IdxRoot, next, cur);
                AddTriangle(mo, pos, IdxTip,  cur,  next);
            }

            return mo;
        }

        private static void AddTriangle(MeshObject mo, Vector3[] pos, int a, int b, int c)
        {
            Vector3 n = NormalHelper.CalculateFaceNormal(pos[a], pos[b], pos[c]);

            int sa = mo.Vertices[a].GetOrAddUVNormal(new Vector2(0f,   0f),   n);
            int sb = mo.Vertices[b].GetOrAddUVNormal(new Vector2(1f,   0f),   n);
            int sc = mo.Vertices[c].GetOrAddUVNormal(new Vector2(0.5f, 1f),   n);

            mo.AddFace(Face.CreateTriangle(a, b, c, sa, sb, sc, sa, sb, sc));
        }

        // ================================================================
        // 読み取り（形状 → 姿勢）
        // ================================================================

        /// <summary>
        /// くさびの形状から根の位置と姿勢を復元する。頂点の並び順には依存しない。
        ///   1) 最も離れた2点が「根」と「先端」。環寄りの側が根。
        ///   2) 軸 = 根 → 先端
        ///   3) 前 = 環のうち軸から最も遠い点の径方向（+Z が最も広いため）
        ///   4) 上 = cross(前, 軸)
        /// </summary>
        /// <returns>くさびとして読めたら true</returns>
        public static bool TryReadPose(MeshObject mo, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (mo == null || mo.Vertices == null || mo.Vertices.Count < VertexCount) return false;

            // 外部ツールを経由すると面ごとに頂点が割れていることがあるので位置で重複を潰す。
            var pts = new List<Vector3>(VertexCount);
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                bool dup = false;
                for (int i = 0; i < pts.Count; i++)
                    if ((pts[i] - v.Position).sqrMagnitude < 1e-10f) { dup = true; break; }
                if (!dup) pts.Add(v.Position);
            }
            if (pts.Count < VertexCount) return false;

            // 1) 最遠の2点
            int ia = 0, ib = 1;
            float bestD = -1f;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    float d = (pts[i] - pts[j]).sqrMagnitude;
                    if (d > bestD) { bestD = d; ia = i; ib = j; }
                }
            if (bestD < 1e-12f) return false;

            // 2) 残り（環）の重心に近い方が根
            Vector3 ringCenter = Vector3.zero;
            int ringCount = 0;
            for (int k = 0; k < pts.Count; k++)
            {
                if (k == ia || k == ib) continue;
                ringCenter += pts[k];
                ringCount++;
            }
            if (ringCount < 4) return false;
            ringCenter /= ringCount;

            Vector3 root = pts[ia], tip = pts[ib];
            if ((ringCenter - tip).sqrMagnitude < (ringCenter - root).sqrMagnitude)
            {
                var t = root; root = tip; tip = t;
            }

            Vector3 axis = tip - root;
            float len = axis.magnitude;
            if (len < 1e-6f) return false;
            axis /= len;

            // 3) 環のうち軸から最も遠い点 = 前（+Z）
            Vector3 front = Vector3.zero;
            float bestR = -1f;
            for (int k = 0; k < pts.Count; k++)
            {
                if (k == ia || k == ib) continue;
                Vector3 d = pts[k] - root;
                Vector3 r = d - axis * Vector3.Dot(d, axis);
                float m = r.sqrMagnitude;
                if (m > bestR) { bestR = m; front = r; }
            }
            if (bestR < 1e-12f) return false;
            front.Normalize();

            // 4) 上 = cross(前, 軸)（Unity では cross(z, x) = y）
            Vector3 up = Vector3.Cross(front, axis);
            if (up.sqrMagnitude < 1e-12f) return false;
            up.Normalize();

            position = root;
            rotation = Quaternion.LookRotation(front, up);
            return true;
        }

        // ================================================================
        // 行列ユーティリティ
        // ================================================================

        /// <summary>行列から回転だけを取り出す（拡大を含んでいてもよい）。</summary>
        public static Quaternion RotationOf(Matrix4x4 m)
        {
            Vector3 c1 = new Vector3(m.m01, m.m11, m.m21);   // up
            Vector3 c2 = new Vector3(m.m02, m.m12, m.m22);   // forward
            if (c1.sqrMagnitude < 1e-12f || c2.sqrMagnitude < 1e-12f) return Quaternion.identity;
            return Quaternion.LookRotation(c2.normalized, c1.normalized);
        }

        /// <summary>行列の位置成分。</summary>
        public static Vector3 PositionOf(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);

        /// <summary>3軸の拡大率の平均。くさびの大きさに掛ける。</summary>
        public static float AverageScaleOf(Matrix4x4 m)
        {
            float sx = new Vector3(m.m00, m.m10, m.m20).magnitude;
            float sy = new Vector3(m.m01, m.m11, m.m21).magnitude;
            float sz = new Vector3(m.m02, m.m12, m.m22).magnitude;
            return (sx + sy + sz) / 3f;
        }
    }
}
