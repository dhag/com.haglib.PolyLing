// MagicCircleMeshGenerators.cs
// 魔法陣の部品になる図形の生成ロジック（Runtime / Editor 共有）
//   ・リング（同心円を任意の本数）            MagicRingMeshGenerator
//   ・星座の円周配置（選んだ星座を円周上へ）    ZodiacCircleMeshGenerator
//   ・オブジェクトの円周配置（指定物を円周上へ） RadialPlaceMeshGenerator
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【平面と向き】
//   どれも XY 面に置く（表は紋章図形・星座マークと同じ側）。全体の位置・向きは
//   魔法陣の親（空のオブジェクト）の姿勢が受け持つ。
//
// 【角度の測り方】
//   角度はすべて「正面ビュー（+Z 側から見た画面）で真上を 0、時計回りを正」とする。
//   正面ビューの画面右はワールド -X なので、画面上の角度 a の位置はワールドで
//   (-sin a, cos a) × 半径。
//
// 【円周上の向き（RadialFacing）】
//   外向き … 置く物の上（+Y）を円の外へ向ける。ワールドでは Z 軸まわりに a だけ回すのと同じ
//            （Quaternion.AngleAxis(a, forward) は (0,1,0) を (-sin a, cos a, 0) へ写す）。
//   内向き … 上を中心へ向ける（外向きから 180° 回す）。上下反転して並べたいときに使う。
//   正立   … 回さない。どの位置でも上は +Y のまま。
//   回転は面内の回転だけなので、字形が鏡に映ったように裏返ることはない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PlaceObject;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>円周上に置く物の向き。</summary>
    public enum RadialFacing
    {
        /// <summary>上を円の外へ向ける。</summary>
        Outward = 0,
        /// <summary>上を円の中心へ向ける（外向きの上下反転）。</summary>
        Inward  = 1,
        /// <summary>回さない（上は常に +Y）。</summary>
        Upright = 2,
    }

    /// <summary>星座の並べ方。</summary>
    public enum ZodiacCircleSpacing
    {
        /// <summary>円を 12 等分した定位置に置く。選ばなかった星座の位置は空く。</summary>
        Slots = 0,
        /// <summary>選んだ星座だけで円を等分する。</summary>
        Even  = 1,
    }

    /// <summary>円周配置の共通計算。</summary>
    public static class RadialLayout
    {
        /// <summary>角度の下限・上限（度）。</summary>
        public const float AngleMin = -360f;
        public const float AngleMax = 360f;

        /// <summary>画面上の角度（真上 0・時計回り正、度）の位置。ワールドの XY 面。</summary>
        public static Vector3 Position(float radius, float angleDeg)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a) * radius, Mathf.Cos(a) * radius, 0f);
        }

        /// <summary>置く物の X 軸・Y 軸（ワールド）。Z 軸は常に +Z。</summary>
        public static void Axes(float angleDeg, RadialFacing facing, out Vector3 x, out Vector3 y)
        {
            float deg = facing == RadialFacing.Upright ? 0f
                      : facing == RadialFacing.Inward  ? angleDeg + 180f
                      : angleDeg;
            var q = Quaternion.AngleAxis(deg, Vector3.forward);
            x = q * Vector3.right;
            y = q * Vector3.up;
        }

        /// <summary>
        /// 1 個を円周上へ複製する。法線は写さない（呼び出し側が最後に RecalculateNormals する）。
        /// </summary>
        public static void AppendAt(
            MeshObject dst, MeshObject src, float radius, float angleDeg,
            RadialFacing facing, float scale, int partsId)
        {
            if (dst == null || src == null || src.VertexCount == 0) return;
            Axes(angleDeg, facing, out var x, out var y);
            PlaceObjectMeshGenerator.AppendInstance(
                dst, src, Position(radius, angleDeg), x, y, Vector3.forward, scale, true, partsId, partsId);
        }
    }

    // ====================================================================
    // リング
    // ====================================================================

    public static class MagicRingMeshGenerator
    {
        [Serializable]
        public struct MagicRingParams : IEquatable<MagicRingParams>
        {
            public const float RadiusMin    = 0.01f;
            public const float RadiusMax    = 20f;
            public const float WidthMin     = 0.001f;
            public const float WidthMax     = 2f;
            public const int   CountMin     = 1;
            public const int   CountMax     = 32;
            public const float SpacingMin   = -5f;
            public const float SpacingMax   = 5f;
            public const int   SegmentsMin  = 8;
            public const int   SegmentsMax  = 512;
            public const float ThicknessMin = 0f;
            public const float ThicknessMax = 1f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "MagicRingRadius", Description = "1 本目のリングの半径（線の中心）", Min = RadiusMin, Max = RadiusMax)]
            public float Radius;

            [PLParam(TextKey = "MagicRingWidth", Description = "リングの線の太さ", Min = WidthMin, Max = WidthMax)]
            public float Width;

            [PLParam(TextKey = "MagicRingCount", Description = "リングの本数", Min = CountMin, Max = CountMax, Step = 1)]
            public int Count;

            [PLParam(TextKey = "MagicRingSpacing", Description = "2 本目以降の半径の増分。負で内側へ並べる", Min = SpacingMin, Max = SpacingMax)]
            public float Spacing;

            [PLParam(TextKey = "Segments", Description = "円周の分割数", Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
            public int Segments;

            [PLParam(TextKey = "Thickness", Description = "押し出しの厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;

            public static MagicRingParams Default => new MagicRingParams
            {
                MeshName  = "MagicRing",
                Radius    = 1f,
                Width     = 0.03f,
                Count     = 1,
                Spacing   = 0.25f,
                Segments  = 128,
                Thickness = 0.02f,
            };

            public bool Equals(MagicRingParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(Radius, o.Radius) &&
                Mathf.Approximately(Width, o.Width) &&
                Count == o.Count &&
                Mathf.Approximately(Spacing, o.Spacing) &&
                Segments == o.Segments &&
                Mathf.Approximately(Thickness, o.Thickness);

            public override bool Equals(object obj) => obj is MagicRingParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>
        /// 同心のリングを Count 本作る。i 本目の半径は Radius + i × Spacing。
        /// 内側の縁が中心を越えるリングは穴なしの円板になる。半径が 0 以下になるリングは作らない。
        /// パーツ ID はリングごとに 0 から振る。
        /// </summary>
        public static MeshObject Generate(MagicRingParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "MagicRing" : p.MeshName;
            int count = Mathf.Clamp(p.Count, MagicRingParams.CountMin, MagicRingParams.CountMax);
            int seg   = Mathf.Clamp(p.Segments, MagicRingParams.SegmentsMin, MagicRingParams.SegmentsMax);
            float w   = Mathf.Clamp(p.Width, MagicRingParams.WidthMin, MagicRingParams.WidthMax);

            var gp = new Profile2DGenerateParams
            {
                Scale         = 1f,
                Offset        = Vector2.zero,
                FlipY         = false,
                Thickness     = Mathf.Clamp(p.Thickness, MagicRingParams.ThicknessMin, MagicRingParams.ThicknessMax),
                SegmentsFront = 0,
                SegmentsBack  = 0,
                EdgeSizeFront = 0.01f,
                EdgeSizeBack  = 0.01f,
                EdgeInward    = false,
                SymmetryMode  = false,
            };

            MeshObject result = null;
            int partsId = 0;

            for (int i = 0; i < count; i++)
            {
                float r = p.Radius + i * p.Spacing;
                if (r <= 0f) continue;

                float outer = r + w * 0.5f;
                float inner = r - w * 0.5f;

                // 文字・紋章図形と同じく、穴も含めて全ループ CCW・穴フラグ付きで渡す。
                var loops = new List<Loop> { Circle(outer, seg, false) };
                if (inner > 1e-5f) loops.Add(Circle(inner, seg, true));

                var one = Profile2DExtrudeMeshGenerator.Generate(loops, name, gp);
                if (one == null || one.VertexCount == 0) continue;

                PartsIdOps.SetPartsId(one, partsId++);
                if (result == null) result = one;
                else MeshObjectAppendOps.Append(result, one, copyNormals: true);
            }

            return result;
        }

        private static Loop Circle(float r, int seg, bool hole)
        {
            var loop = new Loop { IsHole = hole };
            for (int k = 0; k < seg; k++)
            {
                float a = 2f * Mathf.PI * k / seg;
                loop.Points.Add(new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), 0f));
            }
            return loop;
        }
    }

    // ====================================================================
    // 星座の円周配置
    // ====================================================================

    public static class ZodiacCircleMeshGenerator
    {
        /// <summary>12 星座すべてを選んだときの選択値。</summary>
        public const int AllSigns = 0xFFF;

        [Serializable]
        public struct ZodiacCircleParams : IEquatable<ZodiacCircleParams>
        {
            public const float RadiusMin = 0.01f;
            public const float RadiusMax = 20f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "ZodiacCircleSigns",
                     Description = "置く星座の選択。ビット 0=牡羊座 1=牡牛座 2=双子座 3=蟹座 4=獅子座 5=乙女座 6=天秤座 7=蠍座 8=射手座 9=山羊座 10=水瓶座 11=魚座。4095 で全部",
                     Min = 0, Max = AllSigns, Step = 1)]
            public int SignMask;

            [PLParam(TextKey = "ZodiacCircleSpacing", Description = "並べ方。Slots = 12 等分の定位置（選ばなかった位置は空く） / Even = 選んだ星座だけで等分")]
            public ZodiacCircleSpacing Spacing;

            [PLParam(TextKey = "Radius", Description = "マークの中心を置く円の半径", Min = RadiusMin, Max = RadiusMax)]
            public float Radius;

            [PLParam(TextKey = "RadialStartAngle", Description = "1 個目（牡羊座の定位置）の角度。正面から見て真上が 0、時計回りが正（度）",
                     Min = RadialLayout.AngleMin, Max = RadialLayout.AngleMax)]
            public float StartAngle;

            [PLParam(TextKey = "RadialCounterClockwise", Description = "反時計回りに並べる")]
            public bool CounterClockwise;

            [PLParam(TextKey = "RadialFacing", Description = "向き。Outward = 上を外へ / Inward = 上を中心へ（上下反転） / Upright = 回さない")]
            public RadialFacing Facing;

            [PLParam(TextKey = "Size", Description = "マークの大きさ（中心線の外接矩形の長辺）",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.SizeMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.SizeMax)]
            public float Size;

            [PLParam(TextKey = "ZodiacStrokeWidth", Description = "線の太さ。大きさに対する比",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMax)]
            public float StrokeWidth;

            [PLParam(TextKey = "ZodiacRoundCaps", Description = "線端を丸くする。false で平らに切る")]
            public bool RoundCaps;

            [PLParam(TextKey = "Segments", Description = "曲線の分割数（円弧 90° あたり）",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMax, Step = 1)]
            public int Segments;

            [PLParam(TextKey = "Thickness", Description = "押し出しの厚み。0 で板",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.ThicknessMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.ThicknessMax)]
            public float Thickness;

            [PLParam(TextKey = "ZodiacRing", Description = "マークごとに周囲の円を描く")]
            public bool SignRing;

            [PLParam(TextKey = "ZodiacRingRadius", Description = "マークごとの円の半径。大きさに対する比",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMax)]
            public float SignRingRadius;

            [PLParam(TextKey = "ZodiacRingWidth", Description = "マークごとの円の線の太さ。大きさに対する比",
                     Min = ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMin, Max = ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMax)]
            public float SignRingWidth;

            public static ZodiacCircleParams Default => new ZodiacCircleParams
            {
                MeshName         = "ZodiacCircle",
                SignMask         = AllSigns,
                Spacing          = ZodiacCircleSpacing.Slots,
                Radius           = 1.125f,
                StartAngle       = 0f,
                CounterClockwise = false,
                Facing           = RadialFacing.Outward,
                Size             = 0.15f,
                StrokeWidth      = 0.1f,
                RoundCaps        = true,
                Segments         = 6,
                Thickness        = 0.02f,
                SignRing         = false,
                SignRingRadius   = 0.75f,
                SignRingWidth    = 0.06f,
            };

            public bool Equals(ZodiacCircleParams o) =>
                MeshName == o.MeshName && SignMask == o.SignMask && Spacing == o.Spacing &&
                Mathf.Approximately(Radius, o.Radius) && Mathf.Approximately(StartAngle, o.StartAngle) &&
                CounterClockwise == o.CounterClockwise && Facing == o.Facing &&
                Mathf.Approximately(Size, o.Size) && Mathf.Approximately(StrokeWidth, o.StrokeWidth) &&
                RoundCaps == o.RoundCaps && Segments == o.Segments &&
                Mathf.Approximately(Thickness, o.Thickness) && SignRing == o.SignRing &&
                Mathf.Approximately(SignRingRadius, o.SignRingRadius) &&
                Mathf.Approximately(SignRingWidth, o.SignRingWidth);

            public override bool Equals(object obj) => obj is ZodiacCircleParams p && Equals(p);
            public override int GetHashCode() => (MeshName?.GetHashCode() ?? 0) * 31 + SignMask;
        }

        /// <summary>星座が選ばれているか。</summary>
        public static bool Has(int mask, ZodiacSign sign) => (mask & (1 << (int)sign)) != 0;

        /// <summary>選ばれている星座の数。</summary>
        public static int CountSelected(int mask)
        {
            int n = 0;
            for (int i = 0; i < 12; i++) if ((mask & (1 << i)) != 0) n++;
            return n;
        }

        /// <summary>
        /// 選んだ星座を円周上へ並べる。マーク 1 個（周囲の円を含む）がパーツ 1 つ。
        /// 1 つも選ばれていなければ null。
        /// </summary>
        public static MeshObject Generate(ZodiacCircleParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "ZodiacCircle" : p.MeshName;
            int mask = p.SignMask & AllSigns;
            int n = CountSelected(mask);
            if (n == 0) return null;

            var mo = new MeshObject(name);
            float dir = p.CounterClockwise ? -1f : 1f;
            int k = 0;
            int partsId = 0;

            for (int i = 0; i < 12; i++)
            {
                if ((mask & (1 << i)) == 0) continue;

                float step = p.Spacing == ZodiacCircleSpacing.Even ? 360f * k / n : 30f * i;
                float angle = p.StartAngle + dir * step;
                k++;

                var sp = ZodiacSignMeshGenerator.ZodiacSignParams.Default;
                sp.MeshName    = name;
                sp.Sign        = (ZodiacSign)i;
                sp.Size        = p.Size;
                sp.StrokeWidth = p.StrokeWidth;
                sp.RoundCaps   = p.RoundCaps;
                sp.Segments    = p.Segments;
                sp.Thickness   = p.Thickness;
                sp.Ring        = p.SignRing;
                sp.RingRadius  = p.SignRingRadius;
                sp.RingWidth   = p.SignRingWidth;
                sp.Pivot       = Vector3.zero;

                var one = ZodiacSignMeshGenerator.Generate(sp);
                if (one == null || one.VertexCount == 0) continue;

                RadialLayout.AppendAt(mo, one, p.Radius, angle, p.Facing, 1f, partsId++);
            }

            if (mo.VertexCount == 0) return null;
            mo.RecalculateNormals();
            return mo;
        }
    }

    // ====================================================================
    // オブジェクトの円周配置
    // ====================================================================

    public static class RadialPlaceMeshGenerator
    {
        [Serializable]
        public struct RadialPlaceParams : IEquatable<RadialPlaceParams>
        {
            public const int   CountMin  = 1;
            public const int   CountMax  = 128;
            public const float RadiusMin = 0f;
            public const float RadiusMax = 20f;
            public const float ScaleMin  = 0.01f;
            public const float ScaleMax  = 10f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "RadialCount", Description = "円周上に置く個数（円を等分する）", Min = CountMin, Max = CountMax, Step = 1)]
            public int Count;

            [PLParam(TextKey = "Radius", Description = "置く円の半径", Min = RadiusMin, Max = RadiusMax)]
            public float Radius;

            [PLParam(TextKey = "RadialStartAngle", Description = "1 個目の角度。正面から見て真上が 0、時計回りが正（度）",
                     Min = RadialLayout.AngleMin, Max = RadialLayout.AngleMax)]
            public float StartAngle;

            [PLParam(TextKey = "RadialCounterClockwise", Description = "反時計回りに並べる")]
            public bool CounterClockwise;

            [PLParam(TextKey = "RadialFacing", Description = "向き。Outward = 配置元の +Y を外へ / Inward = 中心へ（上下反転） / Upright = 回さない")]
            public RadialFacing Facing;

            [PLParam(TextKey = "Scale", Description = "配置元に掛ける倍率", Min = ScaleMin, Max = ScaleMax)]
            public float Scale;

            [PLParam(TextKey = "RadialCenterSources", Description = "配置元を外接箱の中心で置く。false なら配置元の原点で置く")]
            public bool CenterSources;

            [PLParam(TextKey = "PlaceMode", Description = "配置元が複数のときの割り当て方式（結合 / 順番 / 抽選）")]
            public PlaceSourceMode Mode;

            [PLParam(TextKey = "PlaceSeed", Description = "抽選の乱数シード。割り当て方式が Random のときだけ使う")]
            public int RandomSeed;

            [PLParam(TextKey = "PlaceIncludeChildren", Description = "配置元の子孫も一緒に配置する")]
            public bool IncludeChildren;

            public static RadialPlaceParams Default => new RadialPlaceParams
            {
                MeshName         = "RadialPlace",
                Count            = 8,
                Radius           = 1f,
                StartAngle       = 0f,
                CounterClockwise = false,
                Facing           = RadialFacing.Outward,
                Scale            = 1f,
                CenterSources    = true,
                Mode             = PlaceSourceMode.Sequence,
                RandomSeed       = 0,
                IncludeChildren  = true,
            };

            public bool Equals(RadialPlaceParams o) =>
                MeshName == o.MeshName && Count == o.Count &&
                Mathf.Approximately(Radius, o.Radius) && Mathf.Approximately(StartAngle, o.StartAngle) &&
                CounterClockwise == o.CounterClockwise && Facing == o.Facing &&
                Mathf.Approximately(Scale, o.Scale) && CenterSources == o.CenterSources &&
                Mode == o.Mode && RandomSeed == o.RandomSeed && IncludeChildren == o.IncludeChildren;

            public override bool Equals(object obj) => obj is RadialPlaceParams p && Equals(p);
            public override int GetHashCode() => (MeshName?.GetHashCode() ?? 0) * 31 + Count;
        }

        /// <summary>
        /// 配置元を円周上へ等分に並べる。置いた 1 個がパーツ 1 つ。サブ ID は配置元の値のまま。
        /// 配置元が無いときは頂点 0 のメッシュ。
        /// </summary>
        public static MeshObject Generate(RadialPlaceParams p, List<MeshObject> sources)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "RadialPlace" : p.MeshName);
            if (sources == null || sources.Count == 0) return mo;

            var srcs = new List<MeshObject>(sources.Count);
            foreach (var s in sources)
                if (s != null && s.VertexCount > 0) srcs.Add(p.CenterSources ? Centered(s) : s);
            if (srcs.Count == 0) return mo;

            MeshObject combined = p.Mode == PlaceSourceMode.Combine
                ? MeshObjectAppendOps.Combine(srcs, mo.Name)
                : null;
            if (combined != null && p.CenterSources) combined = Centered(combined);

            var rnd = p.Mode == PlaceSourceMode.Random ? new System.Random(p.RandomSeed) : null;
            int seq = 0;

            int n = Mathf.Clamp(p.Count, RadialPlaceParams.CountMin, RadialPlaceParams.CountMax);
            float dir = p.CounterClockwise ? -1f : 1f;
            float scale = p.Scale <= 0f ? 1f : p.Scale;

            for (int k = 0; k < n; k++)
            {
                MeshObject src;
                switch (p.Mode)
                {
                    case PlaceSourceMode.Sequence:
                        src = srcs[seq];
                        seq = (seq + 1) % srcs.Count;
                        break;
                    case PlaceSourceMode.Random:
                        src = srcs[rnd.Next(srcs.Count)];
                        break;
                    default:
                        src = combined;
                        break;
                }

                float angle = p.StartAngle + dir * 360f * k / n;
                RadialLayout.AppendAt(mo, src, Mathf.Max(0f, p.Radius), angle, p.Facing, scale, k);
            }

            mo.RecalculateNormals();
            return mo;
        }

        /// <summary>外接箱の中心が原点に来るよう平行移動した複製。</summary>
        private static MeshObject Centered(MeshObject src)
        {
            var b = new Bounds(src.Vertices[0].Position, Vector3.zero);
            for (int i = 1; i < src.VertexCount; i++) b.Encapsulate(src.Vertices[i].Position);

            var c = src.Clone();
            foreach (var v in c.Vertices) v.Position -= b.center;
            return c;
        }
    }
}
