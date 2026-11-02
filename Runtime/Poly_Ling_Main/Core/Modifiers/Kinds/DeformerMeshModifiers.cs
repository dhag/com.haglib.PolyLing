// DeformerMeshModifiers.cs
// DeformerRegistry に登録された全デフォーマを処理列の種類にしたもの。
// 基底 DeformerMeshModifier<T> を継ぎ、引数と WriteParams だけを書く。
// 引数のキーと範囲は ApplyXxxDeformCommand（PanelCommand.Deform.cs）にそろえる。
// 写し方は DeformToolHandler.WriteParams と同じ（曲げはカメラ追従を切る）。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【範囲の出どころ】角度 ±360 と倍率の下限 0.01 は既存のコマンド・デフォーマから。
//   移動 ±10・倍率の上限 10・波の振幅 ±10・周期数 0〜20 はこの段で決めた値（コードに出どころは無い）。

using Poly_Ling.Data;
using Poly_Ling.Tools.Deformers;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("rotate", DisplayName = "回転", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸まわりに一様に回す（RotateDeformer）")]
    public sealed class RotateMeshModifier : DeformerMeshModifier<RotateDeformer>
    {
        [PLParam(Description = "作業軸 X まわりの回転角（度）", Min = -360.0, Max = 360.0, Step = 1)]
        public float AngleX { get; set; }

        [PLParam(Description = "作業軸 Y まわりの回転角（度）", Min = -360.0, Max = 360.0, Step = 1)]
        public float AngleY { get; set; }

        [PLParam(Description = "作業軸 Z まわりの回転角（度）", Min = -360.0, Max = 360.0, Step = 1)]
        public float AngleZ { get; set; }

        protected override void WriteParams(RotateDeformer d)
        {
            var p = (RotateDeformerParams)d.Params;
            p.AngleX = AngleX; p.AngleY = AngleY; p.AngleZ = AngleZ;
        }
    }

    [PLMeshModifier("move", DisplayName = "移動（作業軸）", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸のローカル座標で動かす（MoveDeformer）")]
    public sealed class MoveMeshModifier : DeformerMeshModifier<MoveDeformer>
    {
        [PLParam(Description = "作業軸 X 方向の移動量", Min = -10.0, Max = 10.0, Step = 0.01)]
        public float OffsetX { get; set; }

        [PLParam(Description = "作業軸 Y 方向の移動量", Min = -10.0, Max = 10.0, Step = 0.01)]
        public float OffsetY { get; set; }

        [PLParam(Description = "作業軸 Z 方向の移動量", Min = -10.0, Max = 10.0, Step = 0.01)]
        public float OffsetZ { get; set; }

        protected override void WriteParams(MoveDeformer d)
        {
            var p = (MoveDeformerParams)d.Params;
            p.OffsetX = OffsetX; p.OffsetY = OffsetY; p.OffsetZ = OffsetZ;
        }
    }

    [PLMeshModifier("scale", DisplayName = "拡大縮小", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸のローカル座標で拡大縮小する（ScaleDeformer）")]
    public sealed class ScaleMeshModifier : DeformerMeshModifier<ScaleDeformer>
    {
        [PLParam(Description = "作業軸 X 方向の倍率", Min = ScaleDeformerParams.MinScale, Max = 10.0, Step = 0.01)]
        public float ScaleX { get; set; } = 1f;

        [PLParam(Description = "作業軸 Y 方向の倍率", Min = ScaleDeformerParams.MinScale, Max = 10.0, Step = 0.01)]
        public float ScaleY { get; set; } = 1f;

        [PLParam(Description = "作業軸 Z 方向の倍率", Min = ScaleDeformerParams.MinScale, Max = 10.0, Step = 0.01)]
        public float ScaleZ { get; set; } = 1f;

        protected override void WriteParams(ScaleDeformer d)
        {
            var p = (ScaleDeformerParams)d.Params;
            p.ScaleX = ScaleX; p.ScaleY = ScaleY; p.ScaleZ = ScaleZ;
        }
    }

    [PLMeshModifier("bend", DisplayName = "曲げ", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸のライン（+Y）に沿って円弧に曲げる（BendDeformer）")]
    public sealed class BendMeshModifier : DeformerMeshModifier<BendDeformer>
    {
        [PLParam(Description = "曲げる合計角（度）。範囲の全長で曲げる", Min = -360.0, Max = 360.0, Step = 1)]
        public float TotalAngleDeg { get; set; }

        [PLParam(Description = "たわみ方向（度）。作業軸の +X を 0 度として Y まわりに回した角", Min = -360.0, Max = 360.0, Step = 1)]
        public float BendPlaneAngleDeg { get; set; }

        [PLParam(Description = "起点を作業軸の原点にする。切ると範囲の下端が起点")]
        public bool PivotAtAxisOrigin { get; set; }

        protected override void WriteParams(BendDeformer d)
        {
            var p = (BendDeformerParams)d.Params;
            // カメラ追従を切る（DeformToolHandler.WriteParams と同じ）。処理列はカメラを知らない。
            p.UseCameraBendPlane = false;
            p.TotalAngleDeg      = TotalAngleDeg;
            p.BendPlaneAngleDeg  = BendPlaneAngleDeg;
            p.PivotAtAxisOrigin  = PivotAtAxisOrigin;
        }
    }

    [PLMeshModifier("twist", DisplayName = "ねじり", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸のライン（+Y）まわりにねじる（TwistDeformer）")]
    public sealed class TwistMeshModifier : DeformerMeshModifier<TwistDeformer>
    {
        [PLParam(Description = "ねじる合計角（度）。範囲の全長でねじる", Min = -360.0, Max = 360.0, Step = 1)]
        public float TotalAngleDeg { get; set; }

        [PLParam(Description = "起点を作業軸の原点にする。切ると範囲の下端が起点")]
        public bool PivotAtAxisOrigin { get; set; }

        protected override void WriteParams(TwistDeformer d)
        {
            var p = (TwistDeformerParams)d.Params;
            p.TotalAngleDeg     = TotalAngleDeg;
            p.PivotAtAxisOrigin = PivotAtAxisOrigin;
        }
    }

    [PLMeshModifier("wave", DisplayName = "波", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸のライン（+Y）に沿って X・Z へ波打たせる（WaveDeformer）")]
    public sealed class WaveMeshModifier : DeformerMeshModifier<WaveDeformer>
    {
        [PLParam(Description = "X 方向の振幅（作業軸ローカルの長さ）", Min = -10.0, Max = 10.0, Step = 0.01)]
        public float AmplitudeX { get; set; }

        [PLParam(Description = "X 方向の周期数。範囲の全長で何周期か", Min = 0.0, Max = 20.0, Step = 0.1)]
        public float CyclesX { get; set; } = 1f;

        [PLParam(Description = "X 方向の位相（度）", Min = -360.0, Max = 360.0, Step = 1)]
        public float PhaseXDeg { get; set; }

        [PLParam(Description = "Z 方向の振幅。0 なら Z へは振らない", Min = -10.0, Max = 10.0, Step = 0.01)]
        public float AmplitudeZ { get; set; }

        [PLParam(Description = "Z 方向の周期数", Min = 0.0, Max = 20.0, Step = 0.1)]
        public float CyclesZ { get; set; } = 1f;

        [PLParam(Description = "Z 方向の位相（度）", Min = -360.0, Max = 360.0, Step = 1)]
        public float PhaseZDeg { get; set; }

        [PLParam(Description = "起点を作業軸の原点にする。切ると範囲の下端が起点")]
        public bool PivotAtAxisOrigin { get; set; }

        protected override void WriteParams(WaveDeformer d)
        {
            var p = (WaveDeformerParams)d.Params;
            p.AmplitudeX = AmplitudeX; p.CyclesX = CyclesX; p.PhaseXDeg = PhaseXDeg;
            p.AmplitudeZ = AmplitudeZ; p.CyclesZ = CyclesZ; p.PhaseZDeg = PhaseZDeg;
            p.PivotAtAxisOrigin = PivotAtAxisOrigin;
        }
    }
}
