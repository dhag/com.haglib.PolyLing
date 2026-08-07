// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/ILatticeInterpolator.cs
// 格子変形の補間方式を差し替えるためのインターフェース。
//
// 【なぜ分けるか】
//   Metasequoia の公開資料からは、格子制御点からメッシュ頂点を求める補間式が
//   確定できない。初版は三線形補間で作るが、実機比較の結果によっては
//   Bernstein FFD / B-spline FFD へ入れ替える。UI 側が補間方式を知らずに
//   済むよう、数学部分だけを独立させる。
//
// 【契約】
//   Bind(grid) を呼んでから Evaluate(pLocal) を呼ぶ。Bind から次の Bind までの
//   間に格子のセル数・基準範囲が変わってはならない。制御点の位置
//   （LatticeGrid.CurrentControlPoints）は Bind 後に動いてよく、Evaluate は
//   常に「呼ばれた時点の CurrentControlPoints」を読む。
//
//   Evaluate は副作用を持たず、同じ入力と同じ制御点に対して同じ出力を返す。
//   変形後の座標を再入力してはならない（誤差と変形が累積する）。
//
// 【座標系】入出力ともに作業軸ローカル座標。LatticeGrid と同じ空間。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>格子制御点から頂点位置を求める写像。</summary>
    public interface ILatticeInterpolator
    {
        /// <summary>内部識別子。</summary>
        string Name { get; }

        /// <summary>表示名（英語。ローカライズは呼び出し側）。</summary>
        string DisplayName { get; }

        /// <summary>
        /// 格子を結び付け、セル探索に必要な値を用意する。
        /// grid が null または未構築のときは以後の Evaluate が素通しになるよう実装すること。
        /// </summary>
        void Bind(LatticeGrid grid);

        /// <summary>Bind 済みで、変形を計算できる状態か。</summary>
        bool IsBound { get; }

        /// <summary>
        /// 作業軸ローカル座標を受け取り、変形後の作業軸ローカル座標を返す。
        /// Bind していないときは pLocal をそのまま返す。
        /// </summary>
        Vector3 Evaluate(Vector3 pLocal);
    }
}
