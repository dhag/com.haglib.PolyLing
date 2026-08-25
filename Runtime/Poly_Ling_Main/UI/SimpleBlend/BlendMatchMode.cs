// BlendMatchMode.cs
// メッシュブレンドの「ターゲット頂点 → ソース頂点」対応付け方式と、その一致集計・解決器。
// UnityEditor非依存。

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    /// <summary>
    /// メッシュブレンドで、ターゲット頂点に対応するソース頂点をどう決めるか。
    ///
    /// いずれの方式も「対応が取れなかった頂点は動かさない（ブレンド前の位置を保つ）」。
    /// 何件対応が取れたかは <see cref="BlendMatchStats"/> で呼び出し側へ返す。
    /// </summary>
    public enum BlendMatchMode
    {
        /// <summary>
        /// 頂点インデックス直結。ターゲットの i 番にソースの i 番を対応させる。
        /// 両者の Vertices の並びが同一である場合にのみ正しい。
        /// </summary>
        Index = 0,

        /// <summary>
        /// 頂点ID照合。Vertex.Id が一致する頂点同士を対応させる。
        /// 未設定ID（MeshObject.IsUnsetId）は対応対象から外れる。
        /// </summary>
        VertexId = 1,

        /// <summary>
        /// 展開インデックス経由。MeshObject.IsTriangulated の差を吸収する。
        ///
        /// IsTriangulated == true のメッシュ（PMX 由来）は Vertices が既に
        /// UV 展開後の並びで、false のメッシュ（MQO 由来）は展開前の並びである。
        /// 生インデックスの空間が違うため、片方だけが三角形化済みのときは
        /// BuildExpansionMap / BuildInverseExpansionMap を挟まないと対応が取れない。
        /// 両者の IsTriangulated が同じときは <see cref="Index"/> と同じ結果になる。
        /// </summary>
        Expanded = 2,
    }

    /// <summary>
    /// ブレンドの対応付け結果。UI に「何件のうち何件が対応したか」を出すために使う。
    /// 対応が取れない頂点が黙って据え置かれると、ソースを選び間違えていても
    /// 「効いていない」という見え方しかせず原因が分からない。
    /// </summary>
    public struct BlendMatchStats
    {
        /// <summary>ブレンド対象となった頂点数（孤立頂点除外・選択絞り込み後）。</summary>
        public int TargetVertexCount;

        /// <summary>そのうちソース側の対応が取れた頂点数。</summary>
        public int MatchedVertexCount;

        /// <summary>対応が取れなかった頂点数。</summary>
        public int UnmatchedVertexCount => TargetVertexCount - MatchedVertexCount;

        /// <summary>対応率 [0,1]。対象 0 件のときは 0。</summary>
        public float MatchRatio
            => TargetVertexCount > 0 ? (float)MatchedVertexCount / TargetVertexCount : 0f;

        public void Add(BlendMatchStats other)
        {
            TargetVertexCount  += other.TargetVertexCount;
            MatchedVertexCount += other.MatchedVertexCount;
        }
    }

    /// <summary>
    /// ブレンドのソース1件（解決済み）。
    ///
    /// ソースは宛先と別モデルに属してよい。頂点位置の混ぜ合わせはワールド空間で
    /// 行うため（BlendPreviewState.BlendVertices）、モデルが違っても
    /// MeshContext から VertexToWorldMatrix を引ければ同じ経路で扱える。
    /// </summary>
    public struct BlendSourceEntry
    {
        public MeshContext Context;
        public float       Weight;

        public BlendSourceEntry(MeshContext context, float weight)
        {
            Context = context;
            Weight  = weight;
        }

        public bool IsUsable => Context?.MeshObject != null && Weight > 0f;
    }

    /// <summary>
    /// ターゲット頂点インデックス → ソース頂点インデックスの解決器。
    ///
    /// 方式ごとに必要な辞書を構築時に 1 回だけ作る。頂点ループの中で
    /// 辞書を作り直すと、スライダー1目盛りごとに全メッシュ分の辞書構築が走る。
    /// </summary>
    public sealed class BlendVertexResolver
    {
        private readonly MeshObject _src;
        private readonly int        _srcCount;

        /// <summary>ターゲットの i 番がそのままソースの i 番に対応するか。</summary>
        private readonly bool _sameIndexSpace;

        /// <summary>VertexId 方式: ソースの Vertex.Id → ソース頂点インデックス。</summary>
        private readonly Dictionary<int, int> _srcIdMap;

        /// <summary>Expanded 方式・ターゲットのみ三角形化済み: 展開後 → (ソース生index, uvSub)。</summary>
        private readonly Dictionary<int, (int vIdx, int uvIdx)> _srcInvExpMap;

        /// <summary>Expanded 方式・ソースのみ三角形化済み: (ターゲット生index, uvSub) → 展開後。</summary>
        private readonly Dictionary<(int vIdx, int uvIdx), int> _dstExpMap;

        private readonly MeshObject _dst;

        public BlendMatchMode Mode { get; }

        public BlendVertexResolver(MeshObject dst, MeshObject src, BlendMatchMode mode)
        {
            _dst      = dst;
            _src      = src;
            _srcCount = src?.VertexCount ?? 0;
            Mode      = mode;

            switch (mode)
            {
                case BlendMatchMode.VertexId:
                    _srcIdMap = BuildVertexIdMap(src);
                    break;

                case BlendMatchMode.Expanded:
                {
                    // IsTriangulated == true は Vertices が UV 展開後の並び、
                    // false は展開前の並び。同型なら生インデックスがそのまま通る。
                    // 分岐規則は PolyLingCore_Commands.ExecuteBlend と同一。
                    bool dstTri = dst != null && dst.IsTriangulated;
                    bool srcTri = src != null && src.IsTriangulated;

                    if (dstTri == srcTri)
                        _sameIndexSpace = true;
                    else if (dstTri)
                        _srcInvExpMap = src.BuildInverseExpansionMap();
                    else
                        _dstExpMap = dst.BuildExpansionMap();
                    break;
                }

                default:   // BlendMatchMode.Index
                    _sameIndexSpace = true;
                    break;
            }
        }

        /// <summary>
        /// 対応するソース頂点インデックスを返す。対応が無ければ false。
        /// false のとき呼び出し側はその頂点を動かしてはならない。
        /// </summary>
        public bool TryResolve(int dstIndex, out int srcIndex)
        {
            srcIndex = -1;

            if (_sameIndexSpace)
            {
                if (dstIndex < 0 || dstIndex >= _srcCount) return false;
                srcIndex = dstIndex;
                return true;
            }

            if (_srcIdMap != null)
            {
                if (_dst == null || dstIndex < 0 || dstIndex >= _dst.VertexCount) return false;
                int id = _dst.Vertices[dstIndex].Id;
                if (MeshObject.IsUnsetId(id)) return false;
                if (!_srcIdMap.TryGetValue(id, out int si)) return false;
                if (si < 0 || si >= _srcCount) return false;
                srcIndex = si;
                return true;
            }

            if (_srcInvExpMap != null)
            {
                if (!_srcInvExpMap.TryGetValue(dstIndex, out var r)) return false;
                if (r.vIdx < 0 || r.vIdx >= _srcCount) return false;
                srcIndex = r.vIdx;
                return true;
            }

            if (_dstExpMap != null)
            {
                if (!_dstExpMap.TryGetValue((dstIndex, 0), out int si)) return false;
                if (si < 0 || si >= _srcCount) return false;
                srcIndex = si;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 頂点ID → 頂点インデックスの辞書を作る。
        ///
        /// 未設定ID（0 以下。MeshObject.IsUnsetId 参照）はキーに入れない。
        /// 入れてしまうと、ID 未設定の頂点が多数あるメッシュで先頭 1 個だけが
        /// 辞書に載り、残りが黙って無視されたまま「一致した」ように振る舞う。
        /// 重複IDも先勝ちなので、UI 側は InspectVertexIds で事前に件数を出すこと。
        /// </summary>
        public static Dictionary<int, int> BuildVertexIdMap(MeshObject mo)
        {
            var map = new Dictionary<int, int>();
            if (mo == null) return map;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) continue;
                if (!map.ContainsKey(id)) map[id] = i;
            }
            return map;
        }

        /// <summary>
        /// 頂点IDの状態を数える。VertexId 方式が使えるかを UI に出すために使う。
        /// unset は未設定ID の頂点数、duplicate は「同じIDの2件目以降」の頂点数。
        /// </summary>
        public static (int unset, int duplicate) InspectVertexIds(MeshObject mo)
        {
            if (mo == null) return (0, 0);
            int unset = 0, dup = 0;
            var seen = new HashSet<int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                int id = mo.Vertices[i].Id;
                if (MeshObject.IsUnsetId(id)) { unset++; continue; }
                if (!seen.Add(id)) dup++;
            }
            return (unset, dup);
        }
    }
}
