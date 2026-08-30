// ObjectOriginDiag.cs
// 原点CSV適用（ApplyObjectOrigins）の分岐をオブジェクトごとに記録する診断用の入れ物。
// Runtime/Poly_Ling_Main/Core/Diagnostics/ に配置
//
// 【何のためにあるか】
//   「読み込んだら飛んだ」という結果だけでは、どの分岐が誤ったのか決まらない。
//   位置の差ではなく、通った分岐そのものを記録することで、結果と原因を1対1に対応させる。
//
// 【使い方】
//   Enabled = true にしてから原点CSVを適用し、Report() を読む。
//   既定は false で、そのときは Begin/Record が即座に戻るだけなので通常動作に影響しない。
//   検証パネルが実行中だけ立てる。人が触るスイッチは用意しない。

using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Diagnostics
{
    /// <summary>原点CSV適用の分岐記録。既定は無効。</summary>
    public static class ObjectOriginDiag
    {
        /// <summary>記録するか。検証パネルが実行中だけ true にする。</summary>
        public static bool Enabled = false;

        /// <summary>1オブジェクトぶんの記録。</summary>
        public sealed class Entry
        {
            /// <summary>MeshContext の索引。</summary>
            public int Index;

            /// <summary>名前。</summary>
            public string Name = "";

            /// <summary>種別（Mesh / Bone / MirrorSide / BakedMirror …）。</summary>
            public string Type = "";

            /// <summary>頂点数。</summary>
            public int VertexCount;

            /// <summary>実値の階層親索引。</summary>
            public int HierarchyParentIndex = -1;

            /// <summary>祖先の索引を親から順に並べたもの。</summary>
            public List<int> Ancestors = new List<int>();

            /// <summary>CSV に名前の行があったか。</summary>
            public bool InCsv;

            /// <summary>CSV の値（InCsv のときだけ意味を持つ）。</summary>
            public Vector3 CsvPosition;

            /// <summary>適用先（targets）に入ったか。</summary>
            public bool IsTarget;

            /// <summary>再局所化の候補（relocalize）に入ったか。</summary>
            public bool InRelocalize;

            /// <summary>適用前のワールド頂点位置を控えたか。</summary>
            public bool HasStartWorld;

            /// <summary>適用前後のワールド行列。</summary>
            public Matrix4x4 WorldBefore = Matrix4x4.identity;
            public Matrix4x4 WorldAfter  = Matrix4x4.identity;

            /// <summary>ワールド行列の比較で「変化なし」と判定してスキップしたか。</summary>
            public bool SkippedByMatrixCompare;

            /// <summary>再局所化を実行したか。</summary>
            public bool Relocalized;

            /// <summary>適用前後の BoneTransform。</summary>
            public Vector3 PosBefore, PosAfter;
            public bool    UseLocalBefore, UseLocalAfter;

            /// <summary>適用前後で、頂点のワールド位置がどれだけ動いたか。</summary>
            public float   MaxWorldDelta;
            public Vector3 WorldDeltaOfMax;
        }

        private static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private static string _note = "";

        /// <summary>記録を捨てて次の適用に備える。</summary>
        public static void Begin(string note)
        {
            if (!Enabled) return;
            _entries.Clear();
            _note = note ?? "";
        }

        /// <summary>索引の記録を取り出す。無ければ作る。無効時は null。</summary>
        public static Entry Get(int index)
        {
            if (!Enabled) return null;
            if (!_entries.TryGetValue(index, out var e))
            {
                e = new Entry { Index = index };
                _entries[index] = e;
            }
            return e;
        }

        /// <summary>記録の一覧（索引の昇順）。</summary>
        public static List<Entry> Entries
        {
            get
            {
                var list = new List<Entry>(_entries.Values);
                list.Sort((a, b) => a.Index.CompareTo(b.Index));
                return list;
            }
        }

        /// <summary>記録されている件数。</summary>
        public static int Count => _entries.Count;

        /// <summary>控えの見出し。</summary>
        public static string Note => _note;

        /// <summary>行列を1行で書く。</summary>
        public static string Format(Matrix4x4 m)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < 4; r++)
            {
                sb.Append('[');
                for (int c = 0; c < 4; c++)
                {
                    if (c > 0) sb.Append(' ');
                    sb.Append(m[r, c].ToString("F6"));
                }
                sb.Append(']');
            }
            return sb.ToString();
        }
    }
}
