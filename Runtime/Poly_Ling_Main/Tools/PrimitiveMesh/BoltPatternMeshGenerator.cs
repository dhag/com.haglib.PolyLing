// BoltPatternMeshGenerator.cs
// ネジ配置：配置元オブジェクト（ネジなど）を円周上（PCD）または長方形の周上へ複製する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【位置】
//   ローカル空間で、原点を中心に配置面（XY / XZ / YZ）上へ並べる。
//   中心の位置・全体の回転は PrimitivePlacement（配置位置・配置の回転）が受け持つ。
//   頂点を読まないので、何もない空中に置ける。
//
//   円     : 半径 PcdRadius の円周上に Count 個を等角度で並べる。1 個目は StartAngle（度）。
//   長方形 : 幅 Width・高さ Height の長方形の周上に並べる。
//            幅方向の辺に CountX 個、高さ方向の辺に CountY 個（どちらも両端の角を含む）を等分に置く。
//            総数は 2×(CountX + CountY) − 4（常に偶数）。4 個 = (2,2)、6 個 = (3,2)、8 個 = (3,3)。
//            並びは左下の角から反時計回り。
//
// 【配置面】
//   XY 面で組んだ点を GearDiskBuilder.ApplyOrientation と同じ回転で倒す
//   （XZ: X 軸まわり +90°、YZ: Y 軸まわり +90°）。機構部品の「向き」と同じ値を選べば、
//   同じ向きで作った部品の面の上に並ぶ。
//
// 【配置元】
//   回転させずに各位置へ平行移動して複製する（倍率 Scale を掛ける）。
//   ネジの軸の向きは配置元のまま。複数のときの割り当ては藤壺と同じ PlaceSourceMode。
//
// 【パーツID】置いた 1 個につき 1 つ（PartsIdCounter）。サブIDは配置元の値のまま。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PlaceObject;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ネジ配置の並べ方。</summary>
    public enum BoltPatternLayout
    {
        /// <summary>円周上（PCD）。</summary>
        Circle    = 0,
        /// <summary>長方形の周上。</summary>
        Rectangle = 1,
    }

    public static class BoltPatternMeshGenerator
    {
        [Serializable]
        public struct BoltPatternParams : IEquatable<BoltPatternParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            public const int   CircleCountMin = 1,      CircleCountMax = 128;
            public const int   SideCountMin   = 2,      SideCountMax   = 64;
            public const float RadiusMin      = 0.001f, RadiusMax      = 10f;
            public const float SizeMin        = 0.001f, SizeMax        = 20f;
            public const float AngleMin       = -180f,  AngleMax       = 180f;
            public const float ScaleMin       = 0.01f,  ScaleMax       = 10f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "BoltLayout", Description = "並べ方。Circle = 円周上（PCD） / Rectangle = 長方形の周上")]
            public BoltPatternLayout Layout;

            [PLParam(TextKey = "BoltCount", Description = "円周上に置く個数",
                     Min = CircleCountMin, Max = CircleCountMax, Step = 1)]
            public int Count;

            [PLParam(TextKey = "BoltPcdRadius", Description = "円の半径（PCD の半径）",
                     Min = RadiusMin, Max = RadiusMax)]
            public float PcdRadius;

            [PLParam(TextKey = "BoltStartAngle", Description = "円周上の 1 個目の角度（度）。配置面の第 1 軸から測る",
                     Min = AngleMin, Max = AngleMax)]
            public float StartAngle;

            [PLParam(TextKey = "BoltRectWidth", Description = "長方形の幅（配置面の第 1 軸方向）",
                     Min = SizeMin, Max = SizeMax)]
            public float Width;

            [PLParam(TextKey = "BoltRectHeight", Description = "長方形の高さ（配置面の第 2 軸方向）",
                     Min = SizeMin, Max = SizeMax)]
            public float Height;

            [PLParam(TextKey = "BoltCountX", Description = "幅方向の辺に置く個数（両端の角を含む）",
                     Min = SideCountMin, Max = SideCountMax, Step = 1)]
            public int CountX;

            [PLParam(TextKey = "BoltCountY", Description = "高さ方向の辺に置く個数（両端の角を含む）",
                     Min = SideCountMin, Max = SideCountMax, Step = 1)]
            public int CountY;

            [PLParam(TextKey = "Orientation", Description = "並べる面（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;

            [PLParam(TextKey = "BoltScale", Description = "配置元に掛ける倍率", Min = ScaleMin, Max = ScaleMax)]
            public float Scale;

            [PLParam(TextKey = "PlaceMode", Description = "配置元が複数のときの割り当て方式（結合 / 順番 / 抽選）")]
            public PlaceSourceMode Mode;

            [PLParam(TextKey = "PlaceSeed", Description = "抽選の乱数シード。割り当て方式が Random のときだけ使う")]
            public int RandomSeed;

            [PLParam(TextKey = "PlaceIncludeChildren", Description = "配置元の子孫も一緒に配置する")]
            public bool IncludeChildren;

            public static BoltPatternParams Default => new BoltPatternParams
            {
                MeshName        = "BoltPattern",
                Layout          = BoltPatternLayout.Circle,
                Count           = 4,
                PcdRadius       = 0.5f,
                StartAngle      = 0f,
                Width           = 1f,
                Height          = 0.6f,
                CountX          = 2,
                CountY          = 2,
                Orientation     = PlaneOrientation.XY,
                Scale           = 1f,
                Mode            = PlaceSourceMode.Combine,
                RandomSeed      = 0,
                IncludeChildren = true,
            };

            public bool Equals(BoltPatternParams o)
                => MeshName == o.MeshName && Layout == o.Layout && Count == o.Count
                && Mathf.Approximately(PcdRadius, o.PcdRadius)
                && Mathf.Approximately(StartAngle, o.StartAngle)
                && Mathf.Approximately(Width, o.Width)
                && Mathf.Approximately(Height, o.Height)
                && CountX == o.CountX && CountY == o.CountY
                && Orientation == o.Orientation
                && Mathf.Approximately(Scale, o.Scale)
                && Mode == o.Mode && RandomSeed == o.RandomSeed
                && IncludeChildren == o.IncludeChildren;

            public override bool Equals(object obj) => obj is BoltPatternParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>今の諸元で置く個数。</summary>
        public static int PlacementCount(BoltPatternParams p)
        {
            if (p.Layout == BoltPatternLayout.Rectangle)
            {
                int nx = Mathf.Clamp(p.CountX, BoltPatternParams.SideCountMin, BoltPatternParams.SideCountMax);
                int ny = Mathf.Clamp(p.CountY, BoltPatternParams.SideCountMin, BoltPatternParams.SideCountMax);
                return 2 * (nx + ny) - 4;
            }
            return Mathf.Clamp(p.Count, BoltPatternParams.CircleCountMin, BoltPatternParams.CircleCountMax);
        }

        /// <summary>配置位置（ローカル、配置面へ倒した後）を並び順で返す。</summary>
        public static List<Vector3> Positions(BoltPatternParams p)
        {
            var flat = new List<Vector2>();

            if (p.Layout == BoltPatternLayout.Rectangle)
            {
                int nx = Mathf.Clamp(p.CountX, BoltPatternParams.SideCountMin, BoltPatternParams.SideCountMax);
                int ny = Mathf.Clamp(p.CountY, BoltPatternParams.SideCountMin, BoltPatternParams.SideCountMax);
                float w = Mathf.Max(0f, p.Width);
                float h = Mathf.Max(0f, p.Height);
                float x0 = -w * 0.5f, y0 = -h * 0.5f;

                float X(int i) => x0 + w * i / (nx - 1);
                float Y(int j) => y0 + h * j / (ny - 1);

                // 左下の角から反時計回り。角は 1 回だけ入れる。
                for (int i = 0; i < nx; i++)       flat.Add(new Vector2(X(i), y0));      // 下辺
                for (int j = 1; j < ny; j++)       flat.Add(new Vector2(x0 + w, Y(j)));  // 右辺
                for (int i = nx - 2; i >= 0; i--)  flat.Add(new Vector2(X(i), y0 + h));  // 上辺
                for (int j = ny - 2; j >= 1; j--)  flat.Add(new Vector2(x0, Y(j)));      // 左辺
            }
            else
            {
                int n = Mathf.Clamp(p.Count, BoltPatternParams.CircleCountMin, BoltPatternParams.CircleCountMax);
                float r = Mathf.Max(0f, p.PcdRadius);
                for (int i = 0; i < n; i++)
                {
                    float a = (p.StartAngle + 360f * i / n) * Mathf.Deg2Rad;
                    flat.Add(new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a)));
                }
            }

            // GearDiskBuilder.ApplyOrientation と同じ回転で配置面へ倒す。
            Quaternion q = p.Orientation == PlaneOrientation.XZ ? Quaternion.Euler(90f, 0f, 0f)
                         : p.Orientation == PlaneOrientation.YZ ? Quaternion.Euler(0f, 90f, 0f)
                         : Quaternion.identity;

            var result = new List<Vector3>(flat.Count);
            foreach (var f in flat) result.Add(q * new Vector3(f.x, f.y, 0f));
            return result;
        }

        /// <summary>
        /// 配置元を各位置へ複製した MeshObject を返す。配置元が無いときは頂点 0 のメッシュ。
        /// </summary>
        public static MeshObject Generate(BoltPatternParams p, List<MeshObject> sources)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "BoltPattern" : p.MeshName);
            if (sources == null || sources.Count == 0) return mo;

            float s = p.Scale <= 0f ? 1f : p.Scale;

            MeshObject combined = p.Mode == PlaceSourceMode.Combine
                ? Poly_Ling.Ops.MeshObjectAppendOps.Combine(sources, mo.Name)
                : null;

            var rnd = p.Mode == PlaceSourceMode.Random ? new System.Random(p.RandomSeed) : null;
            int seq = 0;

            var partsIds = new PartsIdCounter();

            foreach (var pos in Positions(p))
            {
                MeshObject src;
                switch (p.Mode)
                {
                    case PlaceSourceMode.Sequence:
                        src = sources[seq];
                        seq = (seq + 1) % sources.Count;
                        break;
                    case PlaceSourceMode.Random:
                        src = sources[rnd.Next(sources.Count)];
                        break;
                    default:
                        src = combined;
                        break;
                }

                if (src == null || src.VertexCount == 0) continue;

                // 回転させない（配置元の向きのまま平行移動）。
                PlaceObjectMeshGenerator.AppendInstance(
                    mo, src, pos, Vector3.right, Vector3.up, Vector3.forward, s, true, partsIds.Take());
            }

            mo.RecalculateNormals();
            return mo;
        }
    }
}
