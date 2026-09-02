// Runtime/Poly_Ling_Main/UI/ThinPlateMorph/LocalControlPointSelector.cs
// ターゲット頂点ごとに、TPS の制御点として使うビフォー候補点を選ぶ。
//
// 【4 つのモード】
//   EuclideanCount  … ターゲット頂点位置から直線距離で近い順に N 個
//   LinkCount       … ターゲット頂点に最も近い候補点を始点に、リンク距離で近い順に N 個
//   EuclideanRadius … ターゲット頂点位置から直線距離 L 以下
//   LinkRadius      … ターゲット頂点に最も近い候補点を始点に、リンク距離 L 以下
//
// 【リンク距離モードの経路】
// 隣接グラフは候補点だけの誘導部分グラフを使う。両端が候補点である辺だけを
// 残すため、選択が飛び地になっていると到達できず制御点が減る。これは仕様。
// 始点探索も候補点の中から行う。全ビフォー頂点から最近傍を取ると、
// その頂点がグラフのノードでない場合が出るため。
//
// 【重複除去をここで行わない理由】
// ビフォー位置が完全一致する頂点を先に落とすと、誘導部分グラフのノードごと
// 消えて経路が切れる。除去は係数を解く直前に近傍単位で行う（呼び出し側の責務）。
//
// 【スレッド】
// 作業配列を持つためスレッド安全ではない。1 インスタンスを 1 スレッドから使う。
// 内部で UnityEngine の Object 派生には一切触れないため、
// バックグラウンドスレッドから使える。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    /// <summary>
    /// ターゲット頂点ごとに制御点の候補索引を選ぶ。
    /// 索引は候補配列（BeforeWorld / AfterWorld）に対するもの。
    /// </summary>
    public sealed class LocalControlPointSelector
    {
        private readonly Vector3[]                _beforeWorld;
        private readonly PointGrid3D              _grid;
        private readonly LinkDistanceLocalSearch  _link;   // 隣接が無ければ null

        private int[]   _idxBuffer  = new int[64];
        private float[] _d2Buffer   = new float[64];
        private readonly List<int> _linkResult = new List<int>(64);

        /// <summary>候補点の数。</summary>
        public int CandidateCount => _beforeWorld.Length;

        /// <summary>リンク距離モードが使えるかどうか（誘導部分グラフがあるか）。</summary>
        public bool HasGraph => _link != null;

        /// <summary>
        /// 候補点と誘導部分グラフから選択器を作る。
        /// </summary>
        /// <param name="beforeWorld">候補点のワールド座標。重複除去はしていないこと。</param>
        /// <param name="adjacencyStart">誘導部分グラフの CSR 先頭索引。長さは beforeWorld+1。無ければ null。</param>
        /// <param name="adjacencyList">誘導部分グラフの CSR 隣接列。無ければ null。</param>
        public LocalControlPointSelector(
            Vector3[] beforeWorld, int[] adjacencyStart, int[] adjacencyList)
        {
            _beforeWorld = beforeWorld ?? throw new ArgumentNullException(nameof(beforeWorld));
            _grid        = new PointGrid3D(beforeWorld);

            if (adjacencyStart != null && adjacencyList != null &&
                adjacencyStart.Length == beforeWorld.Length + 1)
            {
                _link = new LinkDistanceLocalSearch(adjacencyStart, adjacencyList, beforeWorld);
            }
        }

        /// <summary>
        /// ターゲット頂点 1 つ分の制御点候補を選ぶ。
        /// </summary>
        /// <param name="targetWorld">ターゲット頂点のワールド座標。</param>
        /// <param name="mode">選択モード。Global を渡した場合は 0 個を返す。</param>
        /// <param name="neighborCount">件数モードで選ぶ個数。</param>
        /// <param name="radius">半径モードの距離しきい値。</param>
        /// <param name="maxControlPoints">選ぶ個数の上限。半径モードで効く。</param>
        /// <param name="result">選ばれた候補索引。呼び出し側で使い回してよい。</param>
        /// <returns>選ばれた個数。</returns>
        public int Select(
            Vector3 targetWorld,
            ThinPlateLocalMode mode,
            int neighborCount,
            float radius,
            int maxControlPoints,
            List<int> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();

            if (_beforeWorld.Length == 0) return 0;
            if (maxControlPoints <= 0) return 0;

            switch (mode)
            {
                case ThinPlateLocalMode.EuclideanCount:
                {
                    int k = Math.Min(neighborCount, maxControlPoints);
                    if (k <= 0) return 0;
                    int n = _grid.FindKNearest(targetWorld, k, ref _idxBuffer, ref _d2Buffer);
                    for (int i = 0; i < n; i++) result.Add(_idxBuffer[i]);
                    return n;
                }

                case ThinPlateLocalMode.EuclideanRadius:
                {
                    int n = _grid.FindWithinRadius(
                        targetWorld, radius, maxControlPoints, ref _idxBuffer, ref _d2Buffer);
                    for (int i = 0; i < n; i++) result.Add(_idxBuffer[i]);
                    return n;
                }

                case ThinPlateLocalMode.LinkCount:
                {
                    if (_link == null) return 0;
                    int start = FindNearestCandidate(targetWorld);
                    if (start < 0) return 0;

                    int k = Math.Min(neighborCount, maxControlPoints);
                    if (k <= 0) return 0;

                    int n = _link.SearchNearestCount(start, k, _linkResult);
                    for (int i = 0; i < n; i++) result.Add(_linkResult[i]);
                    return n;
                }

                case ThinPlateLocalMode.LinkRadius:
                {
                    if (_link == null) return 0;
                    int start = FindNearestCandidate(targetWorld);
                    if (start < 0) return 0;

                    int n = _link.SearchWithinDistance(start, radius, maxControlPoints, _linkResult);
                    for (int i = 0; i < n; i++) result.Add(_linkResult[i]);
                    return n;
                }

                default:
                    return 0;
            }
        }

        /// <summary>ターゲット位置に最も近い候補点の索引。候補が無ければ -1。</summary>
        public int FindNearestCandidate(Vector3 targetWorld)
        {
            int n = _grid.FindKNearest(targetWorld, 1, ref _idxBuffer, ref _d2Buffer);
            return n > 0 ? _idxBuffer[0] : -1;
        }
    }
}
