// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/TrilinearLatticeInterpolator.cs
// セル単位の三線形補間による格子変形。格子変形の初版実装。
//
// 【方式】
//   頂点を基準格子の正規化座標 (u,v,w) へ移し、属するセルを求め、
//   そのセルの 8 隅の制御点だけを重み付きで足す。
//
//     P' = Σ C[隅] * (x 方向の重み)(y 方向の重み)(z 方向の重み)
//
//   1 頂点が参照する制御点は常に 8 個で、格子を細かくするほど変形は局所化する。
//   Metasequoia が内部でこの式を使っていると断定するものではない。実機比較で
//   合わなければ Bernstein FFD / B-spline FFD を別実装として足し、
//   ILatticeInterpolator ごと差し替える。
//
// 【格子外の頂点】初版は u,v,w を 0〜1 に clamp する。clamp すると格子の外側に
//   ある頂点は、格子の面・辺・隅の変形に貼り付いて動く。外挿はしない。
//   この判断はこのクラスの中だけに閉じており、UI 側は知らなくてよい。
//
// 【重みのキャッシュについて】
//   IMeshDeformer.Evaluate は頂点インデックスを受け取らないため、頂点ごとの
//   セル・重みを持つ表は作れない。代わりに 1 頂点あたりの計算量を定数に
//   抑えてある（除算 3 回・floor 3 回・積和 8 項）。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>セル単位の三線形補間。</summary>
    public sealed class TrilinearLatticeInterpolator : ILatticeInterpolator
    {
        public string Name => "Trilinear";
        public string DisplayName => "Trilinear";

        // ================================================================
        // Bind で確定させる値
        // ================================================================

        private LatticeGrid _grid;

        private int _cellsX, _cellsY, _cellsZ;
        private int _pointCountX, _pointCountY;

        private Vector3 _min;

        // 基準範囲の逆数。SetBounds が MinThickness を保証するので 0 除算は起きないが、
        // 念のため Bind 側でも 0 を弾く。
        private Vector3 _invSize;

        public bool IsBound => _grid != null && _grid.IsBuilt;

        // ================================================================
        // Bind
        // ================================================================

        public void Bind(LatticeGrid grid)
        {
            _grid = null;

            if (grid == null || !grid.IsBuilt) return;

            _cellsX = grid.CellsX;
            _cellsY = grid.CellsY;
            _cellsZ = grid.CellsZ;
            _pointCountX = grid.PointCountX;
            _pointCountY = grid.PointCountY;

            _min = grid.BaseMin;

            Vector3 size = grid.BaseSize;
            _invSize = new Vector3(
                size.x > 0f ? 1f / size.x : 0f,
                size.y > 0f ? 1f / size.y : 0f,
                size.z > 0f ? 1f / size.z : 0f);

            _grid = grid;
        }

        // ================================================================
        // Evaluate
        // ================================================================

        public Vector3 Evaluate(Vector3 pLocal)
        {
            if (_grid == null) return pLocal;

            var cp = _grid.CurrentControlPoints;
            if (cp == null) return pLocal;

            // ── 正規化座標 ────────────────────────────────────────────
            float u = Mathf.Clamp01((pLocal.x - _min.x) * _invSize.x);
            float v = Mathf.Clamp01((pLocal.y - _min.y) * _invSize.y);
            float w = Mathf.Clamp01((pLocal.z - _min.z) * _invSize.z);

            // ── 所属セルとセル内座標 ──────────────────────────────────
            // u=1 のとき ix は cells を指すので最後のセルへ落とす。
            // このとき a = 1 となり、セルの上端に一致する。
            SplitCell(u, _cellsX, out int ix, out float a);
            SplitCell(v, _cellsY, out int iy, out float b);
            SplitCell(w, _cellsZ, out int iz, out float c);

            // ── 8 隅の重み ────────────────────────────────────────────
            float ia = 1f - a, ib = 1f - b, ic = 1f - c;

            float w000 = ia * ib * ic;
            float w100 = a * ib * ic;
            float w010 = ia * b * ic;
            float w110 = a * b * ic;
            float w001 = ia * ib * c;
            float w101 = a * ib * c;
            float w011 = ia * b * c;
            float w111 = a * b * c;

            // ── 8 隅の制御点インデックス ──────────────────────────────
            int strideY = _pointCountX;
            int strideZ = _pointCountX * _pointCountY;

            int i000 = ix + strideY * iy + strideZ * iz;
            int i100 = i000 + 1;
            int i010 = i000 + strideY;
            int i110 = i010 + 1;
            int i001 = i000 + strideZ;
            int i101 = i001 + 1;
            int i011 = i001 + strideY;
            int i111 = i011 + 1;

            // ix / iy / iz はセル添字なので +1 側も必ず配列内に入る。
            // 万一 Bind 後に格子が作り直された場合に備えて長さだけ見る。
            if (i111 >= cp.Length) return pLocal;

            return cp[i000] * w000
                 + cp[i100] * w100
                 + cp[i010] * w010
                 + cp[i110] * w110
                 + cp[i001] * w001
                 + cp[i101] * w101
                 + cp[i011] * w011
                 + cp[i111] * w111;
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 正規化座標 t（0〜1）を、セル添字とセル内の局所座標へ分ける。
        /// t = 1 は最後のセルの上端（局所座標 1）として扱う。
        /// </summary>
        private static void SplitCell(float t, int cells, out int index, out float local)
        {
            float s = t * cells;
            int i = Mathf.FloorToInt(s);

            if (i < 0) i = 0;
            else if (i > cells - 1) i = cells - 1;

            index = i;
            local = s - i;
        }
    }
}
