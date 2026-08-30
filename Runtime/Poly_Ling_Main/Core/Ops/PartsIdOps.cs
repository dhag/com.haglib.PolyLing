// PartsIdOps.cs
// 部品ID（Vertex.PartsId）とサブID（Vertex.SubId）の採番を1箇所へ集めたもの。
//
// 【採番の約束】
//   ・部品IDは 1 つのメッシュ（MeshObject）の中で 0 から始まる通し番号。
//     図形を1つ作れば 1 つ、既存オブジェクトへ足せば「そのメッシュの最大値 + 1」から続ける。
//   ・サブIDは部品IDごとのローカル通し番号で、頂点の並び順の先頭から 0,1,2… と振る。
//     同じ部品IDの頂点が 10 個なら 0〜9 になる。
//   ・どちらも一意性は保証しない（同じ値を多数の頂点が共有する）。
//     Vertex.Id（頂点ID）とは別物で、こちらは触らない。
//
// 【0 の扱い】
//   MeshObject.cs の定義では 0 は「未設定」だが、この採番では先頭の部品が 0 を使う。
//   値だけで未設定と先頭部品を見分けることはできない。
//
// 【配置】 Runtime/Poly_Ling_Main/Core/Ops/

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>部品ID / サブIDの採番。</summary>
    public static class PartsIdOps
    {
        /// <summary>
        /// メッシュ内の部品IDの最大値。頂点が 1 つも無ければ -1 を返す。
        /// 「次に使える部品ID」は この値 + 1。
        /// </summary>
        public static int MaxPartsId(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null) return -1;

            int max = -1;
            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                if (v.PartsId > max) max = v.PartsId;
            }
            return max;
        }

        /// <summary>次に使える部品ID（最大値 + 1）。空メッシュなら 0。</summary>
        public static int NextPartsId(MeshObject mo) => MaxPartsId(mo) + 1;

        /// <summary>全頂点の部品IDを同じ値にする。</summary>
        public static void SetPartsId(MeshObject mo, int partsId)
        {
            if (mo == null || mo.Vertices == null) return;

            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                v.PartsId = partsId;
            }
        }

        /// <summary>
        /// 全頂点の部品IDへ offset を足す。
        /// 生成物が内部で複数の部品に分かれている（フリル・パイプ・藤壺）ときに、
        /// 内部の構成を保ったまま追加先メッシュの空き番号へ寄せるために使う。
        /// </summary>
        public static void OffsetPartsId(MeshObject mo, int offset)
        {
            if (mo == null || mo.Vertices == null || offset == 0) return;

            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                v.PartsId += offset;
            }
        }

        /// <summary>
        /// 指定範囲の頂点の部品IDを同じ値にする。fromIndex は含み、toIndexExclusive は含まない。
        /// </summary>
        public static void SetPartsIdRange(MeshObject mo, int fromIndex, int toIndexExclusive, int partsId)
        {
            if (mo == null || mo.Vertices == null) return;

            int from = fromIndex < 0 ? 0 : fromIndex;
            int to   = toIndexExclusive > mo.Vertices.Count ? mo.Vertices.Count : toIndexExclusive;

            for (int i = from; i < to; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                v.PartsId = partsId;
            }
        }

        /// <summary>
        /// 部品IDごとに、頂点の並び順の先頭から 0,1,2… とサブIDを振り直す。
        ///
        /// 厚み付けや重複頂点の結合で頂点数が変わった後の、確定した頂点列に対して呼ぶこと。
        /// 部品IDが飛んでいても（間の部品が消えていても）各IDは 0 から始まる。
        /// </summary>
        public static void AssignSubIdByPartsId(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null) return;

            var next = new Dictionary<int, int>();

            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;

                int n = next.TryGetValue(v.PartsId, out int cur) ? cur : 0;
                v.SubId = n;
                next[v.PartsId] = n + 1;
            }
        }

        /// <summary>
        /// fromIndex 以降に増えた頂点を 1 つの新しい部品として扱う。
        /// 部品IDは fromIndex より前の頂点が持つ最大値 + 1、サブIDはその中で 0 から。
        /// 既存頂点（fromIndex より前）は一切書き換えない。
        ///
        /// 頂点が増えていなければ何もしない。
        /// 新規頂点が末尾へ追加される操作でだけ使えることに注意する。
        /// </summary>
        public static void AssignNewVertices(MeshObject mo, int fromIndex)
        {
            if (mo == null || mo.Vertices == null) return;
            if (fromIndex < 0 || fromIndex >= mo.Vertices.Count) return;

            // 既存側の最大値だけを見る（新規側はこれから書くので数えない）。
            int max = -1;
            for (int i = 0; i < fromIndex; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                if (v.PartsId > max) max = v.PartsId;
            }

            int partsId = max + 1;
            int sub     = 0;

            for (int i = fromIndex; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                v.PartsId = partsId;
                v.SubId   = sub;
                sub++;
            }
        }
    }
}
