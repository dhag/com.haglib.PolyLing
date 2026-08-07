// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/LatticeGrid.cs
// 格子変形（Metasequoia の「格子」に相当）の格子データ。
//
// 【責務】格子の形だけを持つ。メッシュも補間式も知らない。
//   ・セル数 CellsX / CellsY / CellsZ（制御点数は各軸 Cells + 1）
//   ・基準格子の範囲 BaseMin / BaseMax
//   ・BaseControlPoints  … Deform 開始時に固定する制御点
//   ・CurrentControlPoints … 現在の制御点。編集されるのはこちらだけ
//
// 【座標系】すべて「作業軸ローカル座標」。DeformApplier が
//   メッシュローカル → ワールド → 作業軸ローカル の往復を行うため、
//   本クラスはメッシュの WorldMatrix も作業軸の姿勢も参照しない。
//   格子フレーム（仕様書 §9 の LatticeFrame）は作業軸そのものである。
//
// 【Base と Current を必ず分けること】
//   「格子配置」は BaseMin / BaseMax / Cells を決める作業であり、メッシュを
//   変形しない。「格子変形」は Base から Current がどれだけ動いたかで
//   メッシュを変形する作業である。両者を同じ配列で扱うと、配置操作が
//   そのまま変形になってしまう。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/Lattice/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>
    /// 直方体状の制御格子。制御点は X が最内周、次に Y、Z の順に並ぶ
    /// （<see cref="PointIndex"/> 参照）。
    /// </summary>
    public class LatticeGrid
    {
        // ================================================================
        // 定数
        // ================================================================

        /// <summary>ゼロ厚み軸に与える最小の厚み。0 除算を避けるために使う。</summary>
        public const float MinThickness = 1e-4f;

        /// <summary>セル数の下限。0 セルでは制御点が 1 枚になり補間できない。</summary>
        public const int MinCells = 1;

        /// <summary>セル数の上限。制御点総数の暴発を防ぐ。</summary>
        public const int MaxCells = 32;

        // ================================================================
        // セル数
        // ================================================================

        private int _cellsX = 2;
        private int _cellsY = 2;
        private int _cellsZ = 2;

        /// <summary>X 方向のセル数。制御点数は CellsX + 1。</summary>
        public int CellsX => _cellsX;
        public int CellsY => _cellsY;
        public int CellsZ => _cellsZ;

        /// <summary>各軸の制御点数。</summary>
        public int PointCountX => _cellsX + 1;
        public int PointCountY => _cellsY + 1;
        public int PointCountZ => _cellsZ + 1;

        /// <summary>制御点の総数。</summary>
        public int ControlPointCount => PointCountX * PointCountY * PointCountZ;

        // ================================================================
        // 基準範囲（作業軸ローカル）
        // ================================================================

        /// <summary>基準格子の最小側。Rebuild の入力。</summary>
        public Vector3 BaseMin { get; private set; } = new Vector3(-0.5f, -0.5f, -0.5f);

        /// <summary>基準格子の最大側。Rebuild の入力。</summary>
        public Vector3 BaseMax { get; private set; } = new Vector3(0.5f, 0.5f, 0.5f);

        /// <summary>基準格子の各軸の長さ。MinThickness 以上であることが SetBounds で保証される。</summary>
        public Vector3 BaseSize => BaseMax - BaseMin;

        /// <summary>基準格子の中心（作業軸ローカル）。</summary>
        public Vector3 BaseCenter => (BaseMin + BaseMax) * 0.5f;

        // ================================================================
        // 制御点
        // ================================================================

        /// <summary>基準制御点。Rebuild 以外では書き換えないこと。</summary>
        public Vector3[] BaseControlPoints { get; private set; }

        /// <summary>現在の制御点。編集対象はこちらだけ。</summary>
        public Vector3[] CurrentControlPoints { get; private set; }

        /// <summary>制御点が生成済みか。</summary>
        public bool IsBuilt =>
            BaseControlPoints != null &&
            CurrentControlPoints != null &&
            BaseControlPoints.Length == ControlPointCount &&
            CurrentControlPoints.Length == ControlPointCount;

        // ================================================================
        // インデックス
        // ================================================================

        /// <summary>格子座標 (ix, iy, iz) から制御点インデックスへ。範囲検査はしない。</summary>
        public int PointIndex(int ix, int iy, int iz)
            => ix + PointCountX * (iy + PointCountY * iz);

        /// <summary>制御点インデックスから格子座標へ。</summary>
        public void PointCoord(int index, out int ix, out int iy, out int iz)
        {
            int px = PointCountX;
            int py = PointCountY;
            ix = index % px;
            iy = (index / px) % py;
            iz = index / (px * py);
        }

        /// <summary>インデックスが有効か。</summary>
        public bool IsValidIndex(int index)
            => IsBuilt && index >= 0 && index < CurrentControlPoints.Length;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// セル数を設定する。値は MinCells〜MaxCells に丸める。
        /// 制御点の配置が変わるため、呼び出し後は必ず <see cref="Rebuild"/> が要る
        /// （変形中の変更は仕様上禁止。呼び出し側が Placement 状態でのみ呼ぶこと）。
        /// </summary>
        /// <returns>値が変化したら true。</returns>
        public bool SetCells(int x, int y, int z)
        {
            int nx = Mathf.Clamp(x, MinCells, MaxCells);
            int ny = Mathf.Clamp(y, MinCells, MaxCells);
            int nz = Mathf.Clamp(z, MinCells, MaxCells);

            if (nx == _cellsX && ny == _cellsY && nz == _cellsZ) return false;

            _cellsX = nx;
            _cellsY = ny;
            _cellsZ = nz;
            return true;
        }

        /// <summary>
        /// 基準範囲を設定する。ゼロ厚みの軸には MinThickness を与えて 0 除算を避ける。
        /// 「選択フィット」は対象頂点の AABB をそのまま渡せばよい。
        /// 制御点は作り直さないため、続けて <see cref="Rebuild"/> を呼ぶこと。
        /// </summary>
        public void SetBounds(Vector3 min, Vector3 max)
        {
            // min / max が入れ替わって渡されても成立するようにそろえる。
            Vector3 lo = new Vector3(
                Mathf.Min(min.x, max.x),
                Mathf.Min(min.y, max.y),
                Mathf.Min(min.z, max.z));
            Vector3 hi = new Vector3(
                Mathf.Max(min.x, max.x),
                Mathf.Max(min.y, max.y),
                Mathf.Max(min.z, max.z));

            ExpandIfThin(ref lo.x, ref hi.x);
            ExpandIfThin(ref lo.y, ref hi.y);
            ExpandIfThin(ref lo.z, ref hi.z);

            BaseMin = lo;
            BaseMax = hi;
        }

        /// <summary>
        /// 中心と大きさで基準範囲を設定する。負の大きさは絶対値として扱う。
        /// 制御点は作り直さないため、続けて <see cref="Rebuild"/> を呼ぶこと。
        /// </summary>
        public void SetCenterSize(Vector3 center, Vector3 size)
        {
            Vector3 half = new Vector3(
                Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * 0.5f;
            SetBounds(center - half, center + half);
        }

        private static void ExpandIfThin(ref float lo, ref float hi)
        {
            if (hi - lo >= MinThickness) return;
            float c = (lo + hi) * 0.5f;
            lo = c - MinThickness * 0.5f;
            hi = c + MinThickness * 0.5f;
        }

        /// <summary>
        /// 現在のセル数と基準範囲から制御点を作り直す。
        /// Base と Current の両方を等間隔配置で初期化するため、
        /// 直後は無変形（Evaluate が入力をそのまま返す状態）になる。
        /// </summary>
        public void Rebuild()
        {
            int n = ControlPointCount;
            var pts = new Vector3[n];

            Vector3 size = BaseSize;
            int px = PointCountX, py = PointCountY, pz = PointCountZ;

            for (int iz = 0; iz < pz; iz++)
            {
                float w = (float)iz / _cellsZ;
                for (int iy = 0; iy < py; iy++)
                {
                    float v = (float)iy / _cellsY;
                    for (int ix = 0; ix < px; ix++)
                    {
                        float u = (float)ix / _cellsX;
                        pts[PointIndex(ix, iy, iz)] = new Vector3(
                            BaseMin.x + size.x * u,
                            BaseMin.y + size.y * v,
                            BaseMin.z + size.z * w);
                    }
                }
            }

            BaseControlPoints = pts;
            CurrentControlPoints = (Vector3[])pts.Clone();
        }

        /// <summary>
        /// 基準範囲を設定して制御点を作り直す。選択フィット用の一括呼び出し。
        /// </summary>
        public void FitTo(Vector3 min, Vector3 max)
        {
            SetBounds(min, max);
            Rebuild();
        }

        // ================================================================
        // 編集
        // ================================================================

        /// <summary>現在の制御点を取得する。未構築・範囲外は Vector3.zero。</summary>
        public Vector3 GetCurrent(int index)
            => IsValidIndex(index) ? CurrentControlPoints[index] : Vector3.zero;

        /// <summary>基準制御点を取得する。未構築・範囲外は Vector3.zero。</summary>
        public Vector3 GetBase(int index)
            => (IsBuilt && index >= 0 && index < BaseControlPoints.Length)
                ? BaseControlPoints[index]
                : Vector3.zero;

        /// <summary>現在の制御点を設定する。未構築・範囲外は無視する。</summary>
        public void SetCurrent(int index, Vector3 p)
        {
            if (!IsValidIndex(index)) return;
            CurrentControlPoints[index] = p;
        }

        /// <summary>現在の制御点を基準位置へ戻す。格子の形だけを初期化し、範囲とセル数は保つ。</summary>
        public void ResetCurrent()
        {
            if (!IsBuilt) return;
            System.Array.Copy(BaseControlPoints, CurrentControlPoints, BaseControlPoints.Length);
        }

        /// <summary>
        /// 無変形か（Current がすべて Base と一致するか）。
        /// 変形していない状態で Apply したときに頂点を触らないための判定に使う。
        /// </summary>
        public bool IsIdentity(float tolerance = 1e-6f)
        {
            if (!IsBuilt) return true;

            float sqrTol = tolerance * tolerance;
            for (int i = 0; i < CurrentControlPoints.Length; i++)
            {
                if ((CurrentControlPoints[i] - BaseControlPoints[i]).sqrMagnitude > sqrTol)
                    return false;
            }
            return true;
        }

        // ================================================================
        // 複製
        // ================================================================

        /// <summary>他の格子から状態をまるごとコピーする。</summary>
        public void CopyFrom(LatticeGrid other)
        {
            if (other == null) return;

            _cellsX = other._cellsX;
            _cellsY = other._cellsY;
            _cellsZ = other._cellsZ;
            BaseMin = other.BaseMin;
            BaseMax = other.BaseMax;

            BaseControlPoints = other.BaseControlPoints != null
                ? (Vector3[])other.BaseControlPoints.Clone()
                : null;
            CurrentControlPoints = other.CurrentControlPoints != null
                ? (Vector3[])other.CurrentControlPoints.Clone()
                : null;
        }

        /// <summary>複製を作る。</summary>
        public LatticeGrid Clone()
        {
            var g = new LatticeGrid();
            g.CopyFrom(this);
            return g;
        }
    }
}
