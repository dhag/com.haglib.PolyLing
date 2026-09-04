// InvoluteRackMeshGenerator.cs
// インボリュートラック（平ラック）のメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【形状】
//   ピッチ線を Y=0 に置き、歯は +Y へ向く。長さは X、幅（歯幅）は Z。
//   歯面は直線なので、折れ点だけを並べた正確な断面を作り、Z 方向へまっすぐ押し出す。
//   標本数を増やす必要がない。
//
// 【かみ合い】
//   同じモジュール・同じ圧力角のインボリュート平歯車とかみ合う。
//   ピッチ線が歯車のピッチ円に接する位置に置く。
//
// 【全長】
//   ちょうど 歯数 × ピッチ。両端は歯溝の中心で切れるので、指定した歯数ぶんの
//   完全な歯が並ぶ。X 方向の位置ずらしは図形生成パネルの配置で行う
//   （ここで持つと本体ごと動くだけで、歯と本体の関係は変わらない）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class InvoluteRackMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct InvoluteRackParams : System.IEquatable<InvoluteRackParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 1;
            public const int ToothCountMax = 200;

            /// <summary>モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>歯幅（Z 方向）の下限・上限</summary>
            public const float FaceWidthMin = 0f;
            public const float FaceWidthMax = 3f;

            /// <summary>歯底から本体の底までの肉厚の下限・上限</summary>
            public const float BodyHeightMin = 0.01f;
            public const float BodyHeightMax = 5f;

            /// <summary>歯末のたけ係数・歯元のたけ係数の下限・上限</summary>
            public const float ToothDepthCoefMin = 0.1f;
            public const float ToothDepthCoefMax = 2f;

            /// <summary>バックラッシの下限・上限</summary>
            public const float BacklashMin = 0f;
            public const float BacklashMax = 0.2f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 基本諸元 ──
            /// <summary>歯数</summary>
            [PLParam(TextKey = "RackToothCount", Description = "歯数。全長は 歯数 × ピッチ になる",
                     Min = ToothCountMin, Max = ToothCountMax, Step = 1)]
            public int ToothCount;
            /// <summary>モジュール m</summary>
            [PLParam(TextKey = "InvModule", Description = "モジュール（歯の大きさ）", Min = ModuleMin, Max = ModuleMax)]
            public float Module;
            /// <summary>圧力角 α（度）</summary>
            [PLParam(TextKey = "InvPressureAngle", Description = "圧力角（度）。歯面の傾き",
                     Min = PressureAngleMin, Max = PressureAngleMax)]
            public float PressureAngleDeg;
            /// <summary>歯幅（Z 方向）</summary>
            [PLParam(TextKey = "RackFaceWidth", Description = "歯幅。0 で板", Min = FaceWidthMin, Max = FaceWidthMax)]
            public float FaceWidth;
            /// <summary>歯底から本体の底までの肉厚</summary>
            [PLParam(TextKey = "RackBodyHeight", Description = "歯底から本体の底までの肉厚",
                     Min = BodyHeightMin, Max = BodyHeightMax)]
            public float BodyHeight;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ線から歯先までの高さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ線から歯底までの深さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            /// <summary>ピッチ線上のバックラッシ</summary>
            [PLParam(TextKey = "InvBacklash", Description = "バックラッシ", Min = BacklashMin, Max = BacklashMax)]
            public float Backlash;

            // ── 配置 ──
            /// <summary>断面を置く平面</summary>
            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static InvoluteRackParams Default => new InvoluteRackParams
            {
                MeshName         = "InvoluteRack",
                ToothCount       = 12,
                Module           = 0.1f,
                PressureAngleDeg = 20f,
                FaceWidth        = 0.2f,
                BodyHeight       = 0.2f,
                AddendumCoef     = 1f,
                DedendumCoef     = 1.25f,
                Backlash         = 0f,
                Orientation      = PlaneOrientation.XY,
                FlipFaces        = false,
                Pivot            = Vector3.zero,
            };

            public bool Equals(InvoluteRackParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(Module,           o.Module)           &&
                Mathf.Approximately(PressureAngleDeg, o.PressureAngleDeg) &&
                Mathf.Approximately(FaceWidth,        o.FaceWidth)        &&
                Mathf.Approximately(BodyHeight,       o.BodyHeight)       &&
                Mathf.Approximately(AddendumCoef,     o.AddendumCoef)     &&
                Mathf.Approximately(DedendumCoef,     o.DedendumCoef)     &&
                Mathf.Approximately(Backlash,         o.Backlash)         &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is InvoluteRackParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 共有断面への受け渡し
        // ================================================================

        private static bool TryGetRack(InvoluteRackParams p, out RackToothSection.RackData g)
        {
            g = default;

            if (p.FaceWidth < 0f) return false;

            float alpha = p.PressureAngleDeg * Mathf.Deg2Rad;

            var input = new RackToothSection.RackInput
            {
                ToothCount              = p.ToothCount,
                TransverseModule        = p.Module,
                RadialModule            = p.Module,
                TransversePressureAngle = alpha,
                Backlash                = p.Backlash,
                AddendumCoef            = p.AddendumCoef,
                DedendumCoef            = p.DedendumCoef,
                BodyHeight              = p.BodyHeight,
            };

            return RackToothSection.TryGetRackData(input, out g);
        }

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct RackInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float Pitch;
            public float Length;
            public float Addendum;
            public float Dedendum;
            public float TotalHeight;
            public float ToothThicknessPitchLine;
            public float TipWidth;
            public float RootWidth;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static RackInfo GetInfo(InvoluteRackParams p)
        {
            var info = new RackInfo { Valid = false };

            if (!TryGetRack(p, out RackToothSection.RackData g))
                return info;

            info.Valid                   = true;
            info.Pitch                   = g.pitch;
            info.Length                  = g.length;
            info.Addendum                = g.addendum;
            info.Dedendum                = g.dedendum;
            info.TotalHeight             = g.tipY - g.bottomY;
            info.ToothThicknessPitchLine = 2f * g.pitchHalfThickness;
            info.TipWidth                = 2f * g.tipHalfThickness;
            info.RootWidth               = 2f * g.rootHalfThickness;

            return info;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// ラックメッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(InvoluteRackParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "InvoluteRack" : p.MeshName;

            if (!TryGetRack(p, out RackToothSection.RackData g))
                return new MeshObject(name);

            List<Vector2> top = RackToothSection.BuildExactTopProfile(g);

            Vector2[] loop = RackToothSection.CloseSection(top, g.bottomY);
            if (loop == null || loop.Length < 3) return new MeshObject(name);

            // 断面は Z 方向に変わらないので、前後 2 枚で足りる。
            float width = Mathf.Max(0f, p.FaceWidth);

            var sections = new List<GearLoftSection>(2);

            if (width <= 1e-6f)
            {
                sections.Add(new GearLoftSection(0f, loop));
            }
            else
            {
                sections.Add(new GearLoftSection(-0.5f * width, loop));
                sections.Add(new GearLoftSection(+0.5f * width, loop));
            }

            return GearLoftBuilder.Build(
                name,
                sections,
                GearLoftCapMode.Triangulate,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }
    }
}
