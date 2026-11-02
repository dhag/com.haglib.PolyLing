// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/LatticeDeformer.cs
// 格子変形（Metasequoia の「格子」に相当）を IMeshDeformer として提供する。
//
// 【役割分担】
//   LatticeGrid              … 格子の形（セル数・基準範囲・Base/Current 制御点）
//   ILatticeInterpolator     … 制御点から頂点位置を求める数学
//   LatticeDeformer（本体）  … 上記 2 つを DeformApplier のパイプラインへ橋渡しする
//
//   座標往復（メッシュローカル ⇔ ワールド ⇔ 作業軸ローカル）、開始位置の記録、
//   絶対計算、Revert、Undo エントリ生成はすべて DeformApplier が持っている。
//   本クラスはそこへ「作業軸ローカルでの写像」を1つ提供するだけである。
//
// 【DeformerRegistry へ登録しない】
//   PlayerDeformSubPanel はスライダと数値でパラメータを編集する前提であり、
//   格子点の選択・移動を表現できない。格子変形は専用の操作層から
//   DeformApplier.Apply(latticeDeformer) を直接呼んで使う。
//   処理列の種類は別に LatticeMeshModifier（Core/Modifiers/Kinds/）がある。
//   引数から格子を組むのは Configure の 1 か所（種類とコマンドの受け口が共用する）。
//
// 【Prepare で格子を作り直さないこと】
//   「格子配置」と「格子変形」は別の作業である。Prepare は変形のたびに
//   呼ばれるため、ここで対象頂点の AABB へ格子を合わせ直すと、制御点を
//   動かすたびに基準格子まで動いて変形が発散する。選択フィットは
//   Placement 状態で <see cref="FitToSelection"/> を明示的に呼ぶこと。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>制御格子による自由変形。</summary>
    public class LatticeDeformer : IMeshDeformer
    {
        // ================================================================
        // IMeshDeformer
        // ================================================================

        public string Name => "Lattice";
        public string DisplayName => "Lattice";

        /// <summary>
        /// スライダ用パラメータは持たない。格子の状態は <see cref="Grid"/> にある。
        /// IMeshDeformer は「持たないデフォーマは null を返してよい」としている。
        /// </summary>
        public IDeformerParams Params => null;

        // ================================================================
        // 格子
        // ================================================================

        /// <summary>格子データ。常に非 null。操作層はこれを直接編集する。</summary>
        public LatticeGrid Grid { get; } = new LatticeGrid();

        private ILatticeInterpolator _interpolator = new TrilinearLatticeInterpolator();

        /// <summary>
        /// 補間方式。既定は三線形補間。null を設定しても既定へ戻すだけで、
        /// Evaluate が素通しになることはない。
        /// </summary>
        public ILatticeInterpolator Interpolator
        {
            get => _interpolator;
            set => _interpolator = value ?? new TrilinearLatticeInterpolator();
        }

        /// <summary>格子が生成済みで、変形を計算できる状態か。</summary>
        public bool IsReady => Grid.IsBuilt;

        // ================================================================
        // 格子配置（Placement 状態で呼ぶ）
        // ================================================================

        /// <summary>
        /// 選択フィット。対象頂点の作業軸ローカル AABB へ格子を合わせ、制御点を作り直す。
        /// DeformContext は DeformApplier.Begin が組み立てたものを渡す。
        /// 対象頂点が 0 個のときは何もせず false を返す。
        /// </summary>
        public bool FitToSelection(DeformContext ctx)
        {
            if (ctx.VertexCount <= 0) return false;

            Grid.FitTo(ctx.LocalMin, ctx.LocalMax);
            _interpolator.Bind(Grid);
            return true;
        }

        /// <summary>
        /// セル数を変更して制御点を作り直す。変形中の変更は仕様上禁止のため、
        /// 呼び出し側が Placement 状態でのみ呼ぶこと。
        /// </summary>
        /// <returns>セル数が変化したら true。</returns>
        public bool SetCells(int x, int y, int z)
        {
            if (!Grid.SetCells(x, y, z)) return false;

            Grid.Rebuild();
            _interpolator.Bind(Grid);
            return true;
        }

        /// <summary>
        /// 格子の中心と大きさを設定して制御点を作り直す。格子全体の移動・拡大縮小に使う。
        /// 制御点が等間隔で作り直されるため、呼び出し側が Placement 状態でのみ呼ぶこと。
        /// </summary>
        public void SetBounds(Vector3 center, Vector3 size)
        {
            Grid.SetCenterSize(center, size);
            Grid.Rebuild();
            _interpolator.Bind(Grid);
        }

        /// <summary>
        /// 引数から格子を組む（共用の組み立て。処理列の種類 LatticeMeshModifier と
        /// 格子変形コマンドの受け口 LatticeToolHandler.ExecuteFromCommand が呼ぶ。モディファイア枠組み計画 段 7b）。
        /// セル数（MinCells〜MaxCells に丸める）→ 中心と大きさ → 制御点を等間隔で作り直す →
        /// 動かした点の検査と、基準位置からのずれで Current を書く。
        /// pointIndices の索引は index = ix + iy*(CellsX+1) + iz*(CellsX+1)*(CellsY+1)、
        /// pointOffsets は同じ並びで x,y,z の 3 個ずつ。どちらも null・空なら無変形。
        /// 失敗したら false と理由（格子は組んだ直後の無変形のまま）。
        /// </summary>
        public bool Configure(int cellsX, int cellsY, int cellsZ, Vector3 center, Vector3 size,
                              int[] pointIndices, float[] pointOffsets, out string error)
        {
            error = null;
            Grid.SetCells(cellsX, cellsY, cellsZ);
            Grid.SetCenterSize(center, size);
            Grid.Rebuild();
            _interpolator.Bind(Grid);

            int n = pointIndices?.Length ?? 0;
            int m = pointOffsets?.Length ?? 0;
            if (m != n * 3)
            { error = $"PointOffsets の長さは PointIndices の 3 倍にしてください（{n * 3} 個）"; return false; }

            int pointCount = Grid.ControlPointCount;
            for (int k = 0; k < n; k++)
            {
                int pi = pointIndices[k];
                if (pi < 0 || pi >= pointCount)
                { error = $"PointIndices[{k}] = {pi} が範囲外です（0〜{pointCount - 1}）"; return false; }
            }

            for (int k = 0; k < n; k++)
            {
                int pi = pointIndices[k];
                var d = new Vector3(pointOffsets[k * 3], pointOffsets[k * 3 + 1], pointOffsets[k * 3 + 2]);
                Grid.SetCurrent(pi, Grid.GetBase(pi) + d);
            }
            return true;
        }

        /// <summary>
        /// 制御点だけを基準位置へ戻す。範囲とセル数は保つ。
        /// 「変形をリセットして格子はそのまま」の操作に使う。
        /// </summary>
        public void ResetDeformation()
        {
            Grid.ResetCurrent();
        }

        // ================================================================
        // 変形
        // ================================================================

        /// <summary>
        /// 補間器と格子を結び直すだけ。基準格子はここで作らない
        /// （理由はファイル先頭の注記を参照）。
        /// </summary>
        public void Prepare(DeformContext ctx)
        {
            _interpolator.Bind(Grid);
        }

        /// <summary>
        /// 作業軸ローカル座標を格子で変形する。未構築なら入力をそのまま返す。
        /// </summary>
        public Vector3 Evaluate(Vector3 pLocal)
        {
            return _interpolator.Evaluate(pLocal);
        }

        /// <summary>
        /// 格子変形はアフィンではない。制御点ごとに異なる移動を与えられるため、
        /// 全頂点に共通の行列 M で書けるのは「全制御点を同じだけ動かした」等の
        /// 特殊な場合に限られる。判定を入れても得られるのは
        /// マグネット部分適用時の回転 Slerp だけなので、常に false を返す。
        /// </summary>
        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return false;
        }
    }
}
