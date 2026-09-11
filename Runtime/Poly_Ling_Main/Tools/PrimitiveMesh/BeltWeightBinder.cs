// BeltWeightBinder.cs
// 梯子（基準ベルト）の点列へ、取り込み元メッシュのボーンウェイトを載せる。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【何のためにあるか】
//   はしごへ塗ったウェイト（SpringBoneLadderPlacer）を、そのはしごから作る
//   フリル／パイプの頂点へも引き継ぎたい。生成器が受け取るのは点列だけで、
//   どの頂点だったかは BeltAcquire.Acquire（BeltAcquire.cs:136-144）が
//   位置へ落とした時点で消えている。
//
// 【なぜ位置で突き合わせるか】
//   Acquire は mesh.Vertices[li].Position をそのまま複写する。つまり
//   ベルトの点は取り込み元の頂点の位置そのもの。前処理（向き補正・スプライン）が
//   掛かるのは生成時（PrimitiveMeshFactory.EachPreprocessedBelt）なので、
//   前処理の手前で突き合わせれば位置は一致する。
//
//   頂点索引をコマンドへ載せる案は採らない。索引は編集で動くうえ、
//   点列と索引の二重管理になる（BeltAcquire.cs 冒頭の方針と同じ理由）。
//
// 【丸め】
//   位置は 1e-5 単位へ丸めてから引く。丸めないと、同じ値でも浮動小数の
//   下位ビットの揺れで引けなくなる。刻みは ObjectGroupOps.ComputeSourceDigest
//   （ObjectGroupOps.cs:221-223）と同じにそろえてある。
//
// 【引けなくてもよい】
//   引けない点は null のままにする。ウェイトの無い頂点が出来るだけで、
//   引き継ぎを入れる前と同じ結果になる。黙って別のボーンへ付けるより良い。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>梯子の点列へ取り込み元のウェイトを載せる。</summary>
    public static class BeltWeightBinder
    {
        /// <summary>位置の量子化幅。ダイジェストと同じ刻み。</summary>
        private const float PosEps = 1e-5f;

        /// <summary>丸めた位置のキー。</summary>
        public readonly struct PosKey : System.IEquatable<PosKey>
        {
            private readonly long _x, _y, _z;

            public PosKey(Vector3 p)
            {
                _x = (long)Mathf.Round(p.x / PosEps);
                _y = (long)Mathf.Round(p.y / PosEps);
                _z = (long)Mathf.Round(p.z / PosEps);
            }

            public bool Equals(PosKey o) => _x == o._x && _y == o._y && _z == o._z;
            public override bool Equals(object o) => o is PosKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = h * 31 + _x.GetHashCode();
                    h = h * 31 + _y.GetHashCode();
                    h = h * 31 + _z.GetHashCode();
                    return h;
                }
            }
        }

        /// <summary>
        /// 取り込み元の「位置 → ウェイト」表を作る。
        /// ウェイトを持たない頂点は入れない。同じ位置に複数あるときは先に見つけたものを使う
        /// （位置が同じなら、はしごへの塗りも同じ値になっている）。
        /// </summary>
        public static Dictionary<PosKey, BoneWeight> BuildTable(MeshObject source)
        {
            var table = new Dictionary<PosKey, BoneWeight>();
            if (source == null) return table;

            for (int i = 0; i < source.VertexCount; i++)
            {
                var v = source.Vertices[i];
                if (v?.BoneWeight == null) continue;

                var key = new PosKey(v.Position);
                if (!table.ContainsKey(key)) table[key] = v.BoneWeight.Value;
            }

            return table;
        }

        /// <summary>
        /// 梯子 1 本へウェイトを載せる。戻り値は引けた点の数。
        /// 前処理（BeltShapeOps.Preprocess）より前に呼ぶこと。
        /// </summary>
        public static int Bind(BeltCsvEntry belt, MeshObject source)
        {
            if (belt == null || !belt.HasData || source == null) return 0;
            return Bind(belt, BuildTable(source));
        }

        /// <summary>表を使い回して梯子 1 本へ載せる。</summary>
        public static int Bind(BeltCsvEntry belt, Dictionary<PosKey, BoneWeight> table)
        {
            if (belt == null || !belt.HasData || table == null || table.Count == 0) return 0;

            int n = Mathf.Min(belt.Left.Count, belt.Right.Count);

            var lw = new List<BoneWeight?>(n);
            var rw = new List<BoneWeight?>(n);

            int found = 0;

            for (int i = 0; i < n; i++)
            {
                lw.Add(Lookup(table, belt.Left[i],  ref found));
                rw.Add(Lookup(table, belt.Right[i], ref found));
            }

            // 点列より短い側があっても対で揃える（HasWeights の条件）。
            while (lw.Count < belt.Left.Count)  lw.Add(null);
            while (rw.Count < belt.Right.Count) rw.Add(null);

            belt.LeftWeights  = lw;
            belt.RightWeights = rw;

            return found;
        }

        /// <summary>梯子をまとめて処理する。表は 1 回だけ作る。</summary>
        public static int BindAll(IEnumerable<BeltCsvEntry> belts, MeshObject source)
        {
            if (belts == null || source == null) return 0;

            var table = BuildTable(source);
            if (table.Count == 0) return 0;

            int found = 0;
            foreach (var b in belts) found += Bind(b, table);
            return found;
        }

        private static BoneWeight? Lookup(
            Dictionary<PosKey, BoneWeight> table, Vector3 p, ref int found)
        {
            if (table.TryGetValue(new PosKey(p), out var w))
            {
                found++;
                return w;
            }
            return null;
        }
    }
}
