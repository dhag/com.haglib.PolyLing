// LatticeMeshModifier.cs
// 処理列の種類「格子」（位置だけ）。デフォーマを包む基底 DeformerMeshModifier<LatticeDeformer> を継ぐ。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7b
//
// 【座標】作業軸（基底の axisOrigin・axisRotation。オブジェクトのローカル座標）のローカルで、
//   中心・大きさ・動かした点のずれを持つ。格子ツールの作業軸（ワールド）とは違う。
//   ツールで動かした格子は、格子パネルの「処理列へ取り込む」（LatticeToolHandler.CommitToModifierChain）が
//   この空間へ移して項目を足す。
//
// 【格子の組み立て】LatticeDeformer.Configure（格子変形コマンドの受け口と共用）。
//   長さが合わない・索引が範囲外なら失敗（TryWriteParams）。点が無ければ無変形。
//
// 【引数】点の索引とずれは配列なので、引数欄は作られない。カードの「制御点を動かす」でビューから
//   動かす（ModifierGizmoToolHandler）か、「詳細」の文字列欄で書く。
//   index = ix + iy*(cellsX+1) + iz*(cellsX+1)*(cellsY+1)、ずれは x,y,z の 3 個ずつ。
//   範囲：セル数 1〜32 は LatticeGrid、大きさの下限は LatticeGrid.MinThickness。
//   中心 ±10（作業軸の原点と同じ）・大きさの上限 20 はこの段で決めた値。
//
// DeformerRegistry には登録しない（LatticeDeformer の注記）。

using System;
using Poly_Ling.Data;
using Poly_Ling.Tools.Deformers;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("lattice", DisplayName = "ラティス", Category = "deform", Change = MeshModifierChange.PositionsOnly,
                    Description = "範囲内の頂点を作業軸ローカルの格子で変形する（LatticeDeformer。三線形補間）。制御点はカードの「制御点を動かす」でビューから動かす")]
    public sealed class LatticeMeshModifier : DeformerMeshModifier<LatticeDeformer>, IMeshModifierAddDefaults, IMeshModifierHint
    {
        /// <summary>
        /// 追加メニューの既定値（操作統合計画 段 3）。Maya の Lattice と同じ：入力を囲み（objectCentered）、
        /// スライスは 2,5,2（セル 1,4,1）で、制御点を動かすまで形は変わらない。
        /// </summary>
        public void ApplyAddDefaults(MeshObject input)
        {
            Vector3 size = FitAxisToInput(input, alignLine: false);
            Size = new Vector3(Mathf.Max(size.x, LatticeGrid.MinThickness),
                               Mathf.Max(size.y, LatticeGrid.MinThickness),
                               Mathf.Max(size.z, LatticeGrid.MinThickness));
            CellsX = 1;
            CellsY = 4;
            CellsZ = 1;
        }

        public string GetHint()
            => PointIndices == null || PointIndices.Length == 0 ? "「制御点を動かす」を押して、ビューで点を動かすと形が変わります" : null;

        public const float CenterMin = -10f;
        public const float CenterMax =  10f;
        public const float SizeMax   =  20f;

        [PLParam(Description = "X 方向のセル数", Min = LatticeGrid.MinCells, Max = LatticeGrid.MaxCells, Step = 1)]
        public int CellsX { get; set; } = 2;

        [PLParam(Description = "Y 方向のセル数", Min = LatticeGrid.MinCells, Max = LatticeGrid.MaxCells, Step = 1)]
        public int CellsY { get; set; } = 2;

        [PLParam(Description = "Z 方向のセル数", Min = LatticeGrid.MinCells, Max = LatticeGrid.MaxCells, Step = 1)]
        public int CellsZ { get; set; } = 2;

        [PLParam(Description = "基準格子の中心（作業軸ローカル）", Min = CenterMin, Max = CenterMax, Step = 0.01)]
        public Vector3 Center { get; set; } = Vector3.zero;

        [PLParam(Description = "基準格子の大きさ（作業軸ローカル）。薄すぎる軸は格子側で広げられる",
                 Min = LatticeGrid.MinThickness, Max = SizeMax, Step = 0.01)]
        public Vector3 Size { get; set; } = Vector3.one;

        [PLParam(Description = "動かした制御点の索引。index = ix + iy*(cellsX+1) + iz*(cellsX+1)*(cellsY+1)")]
        public int[] PointIndices { get; set; } = Array.Empty<int>();

        [PLParam(Description = "pointIndices と同じ並びの基準位置からのずれ。x,y,z の順に 3 個ずつ")]
        public float[] PointOffsets { get; set; } = Array.Empty<float>();

        protected override void WriteParams(LatticeDeformer d) => TryWriteParams(d, out _);

        protected override bool TryWriteParams(LatticeDeformer d, out string error)
            => d.Configure(CellsX, CellsY, CellsZ, Center, Size, PointIndices, PointOffsets, out error);
    }
}
