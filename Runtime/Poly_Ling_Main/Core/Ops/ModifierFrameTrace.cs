// ModifierFrameTrace.cs
// 正方向の生成で「どの頂点がどの枠（段・置いた 1 個・先端）から来たか」を控える。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何のためにあるか】（PolyLing_作り直し整備計画.md 2.5・3.4・段 6）
//   逆方向（出力の編集 → 入力）は、枠ごとに「正方向で作った頂点」と「編集後の頂点」を
//   突き合わせて相似変換を求める（ModifierInverse）。対応はパーツ ID や頂点の並びの前提に頼らず、
//   生成器自身がここへ記録した範囲で取る。厚み付けのように部品を作り直す処理を通ると、
//   その部品の控えは全体へ届かない（逆方向はそれを「対応が取れない」として断る）。
//
// 【記録する所】
//   ・PlaceObjectMeshGenerator.AppendInstance（藤壺・頂点藤壺・ネジ配置・円周配置・デッキ）
//   ・PipeMeshGenerator.Generate（輪と先端の要）
//   ・MeshObjectAppendOps.Append（部品から全体へ番号をずらす）
//   ・PrimitiveMeshFactory の梯子の繰り返し（どの梯子の何段目か。BeginBelt）
//   ・入力の頂点から作った頂点（RecordSource。段 14）：PipeBeltExtractor.AppendBelt、
//     EdgeRibbonFaceOps.BuildInto、SurfacePasteOps.Build
//   ・フリルの段（RecordVerticesOfBelt。段 15）：FrillMeshGenerator.Generate
//   ・厚み付けで部品を作り直したとき（Expanded。段 15）：BeltShapeOps.ApplySolidify
//
// 【有効な間】Begin から Dispose まで。それ以外では何もしない（通常の生成に影響しない）。
// 同じスレッドで入れ子にはしない（正方向の作り直しを 1 回控えるためだけに使う）。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>枠の種類。</summary>
    public enum ModifierFrameKind
    {
        /// <summary>梯子の 1 段（パイプの輪・藤壺の 1 個）、または置いた 1 個。</summary>
        Instance = 0,
        /// <summary>パイプの根元の要（梯子の始点）。</summary>
        StartApex = 1,
        /// <summary>パイプの毛先の要（梯子の終点）。</summary>
        EndApex = 2,
        /// <summary>
        /// 入力の頂点 1 個から作った頂点（段 14）。枠の頂点の平均（生成器の空間）が、その入力の頂点の位置に当たる。
        /// パイプの梯子化の始点・レール・終点、帯の左右、貼り付けた頂点。
        /// </summary>
        Source = 3,
    }

    public sealed class ModifierFrameTrace
    {
        /// <summary>枠 1 つ。</summary>
        public sealed class Frame
        {
            public ModifierFrameKind Kind;
            /// <summary>何本目の梯子か（PrimitiveMeshFactory が渡した元の並びでの番号）。梯子を使わない生成は -1。</summary>
            public int Belt = -1;
            /// <summary>枠の番号。梯子では前処理後の段の番号、それ以外は置いた順の番号。</summary>
            public int Key = -1;
            /// <summary>今の持ち主のメッシュでの頂点番号。</summary>
            public readonly List<int> Vertices = new List<int>();
            /// <summary>今の持ち主（部品 → 全体へ連結されると付け替わる）。</summary>
            public MeshObject Owner;
            /// <summary>置いた位置と倍率（AppendInstance が渡したもの。生成器が点を置いた空間）。</summary>
            public bool HasAnchor;
            public UnityEngine.Vector3 Anchor;
            public float AnchorScale = 1f;
            /// <summary>Kind=Source の元のメッシュ（生成器が読んだ MeshObject そのもの）と頂点番号。</summary>
            public MeshObject SourceMesh;
            public int SourceVertex = -1;
        }

        /// <summary>梯子 1 本ぶんの前処理の控え。</summary>
        public sealed class BeltInfo
        {
            /// <summary>元の並びでの梯子の番号。</summary>
            public int RawIndex;
            /// <summary>前処理の対応（BeltShapeOps.PreprocessTraced）。</summary>
            public Poly_Ling.PrimitiveMesh.BeltRungTrace Rungs;
        }

        public readonly List<Frame> Frames = new List<Frame>();
        public readonly Dictionary<int, BeltInfo> Belts = new Dictionary<int, BeltInfo>();

        /// <summary>今生成している梯子の番号（BeginBelt で立てる）。</summary>
        public int CurrentBelt = -1;

        [ThreadStatic] private static ModifierFrameTrace _current;

        /// <summary>今控えているもの。無ければ null。</summary>
        public static ModifierFrameTrace Current => _current;

        /// <summary>控えを立てる。戻り値を Dispose すると外れる。</summary>
        public static IDisposable Begin(ModifierFrameTrace trace)
        {
            var prev = _current;
            _current = trace;
            return new Scope(prev);
        }

        private sealed class Scope : IDisposable
        {
            private readonly ModifierFrameTrace _prev;
            private bool _done;
            public Scope(ModifierFrameTrace prev) { _prev = prev; }
            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _current = _prev;
            }
        }

        /// <summary>これから生成する梯子を控える。控えていなければ何もしない。</summary>
        public static void BeginBelt(int rawIndex, Poly_Ling.PrimitiveMesh.BeltRungTrace rungs)
        {
            var t = _current;
            if (t == null) return;
            t.CurrentBelt = rawIndex;
            t.Belts[rawIndex] = new BeltInfo { RawIndex = rawIndex, Rungs = rungs };
        }

        /// <summary>dst の連続した範囲を枠として控える。</summary>
        public static void RecordRange(MeshObject dst, int start, int count, int key,
                                       ModifierFrameKind kind = ModifierFrameKind.Instance)
        {
            var t = _current;
            if (t == null || dst == null || count <= 0) return;
            var f = new Frame { Kind = kind, Belt = t.CurrentBelt, Key = key, Owner = dst };
            for (int i = 0; i < count; i++) f.Vertices.Add(start + i);
            t.Frames.Add(f);
        }

        /// <summary>置いた 1 個（AppendInstance）を、置いた位置・倍率とともに控える。</summary>
        public static void RecordInstance(MeshObject dst, int start, int count, int key,
                                          UnityEngine.Vector3 anchor, float scale)
        {
            var t = _current;
            if (t == null || dst == null || count <= 0) return;
            var f = new Frame
            {
                Kind = ModifierFrameKind.Instance, Belt = t.CurrentBelt, Key = key, Owner = dst,
                HasAnchor = true, Anchor = anchor, AnchorScale = scale,
            };
            for (int i = 0; i < count; i++) f.Vertices.Add(start + i);
            t.Frames.Add(f);
        }

        /// <summary>dst の頂点の並びを枠として控える。</summary>
        public static void RecordVertices(MeshObject dst, IEnumerable<int> vertices, int key,
                                          ModifierFrameKind kind = ModifierFrameKind.Instance)
        {
            var t = _current;
            if (t == null || dst == null || vertices == null) return;
            var f = new Frame { Kind = kind, Belt = t.CurrentBelt, Key = key, Owner = dst };
            f.Vertices.AddRange(vertices);
            if (f.Vertices.Count > 0) t.Frames.Add(f);
        }

        /// <summary>
        /// dst の頂点の並びを、入力 srcMesh の頂点 srcVertex から作ったものとして控える（段 14）。
        /// 並びの平均（生成器の空間）がその入力の頂点に当たること。
        /// </summary>
        public static void RecordSource(MeshObject dst, IEnumerable<int> vertices, MeshObject srcMesh, int srcVertex)
        {
            var t = _current;
            if (t == null || dst == null || vertices == null || srcMesh == null || srcVertex < 0) return;
            var f = new Frame
            {
                Kind = ModifierFrameKind.Source, Belt = t.CurrentBelt, Key = srcVertex, Owner = dst,
                SourceMesh = srcMesh, SourceVertex = srcVertex,
            };
            f.Vertices.AddRange(vertices);
            if (f.Vertices.Count > 0) t.Frames.Add(f);
        }

        /// <summary>dst の頂点の並びを、梯子 belt の枠として控える（今の梯子を見ない版。段 15 のフリル）。</summary>
        public static void RecordVerticesOfBelt(MeshObject dst, IEnumerable<int> vertices, int belt, int key)
        {
            var t = _current;
            if (t == null || dst == null || vertices == null) return;
            var f = new Frame { Kind = ModifierFrameKind.Instance, Belt = belt, Key = key, Owner = dst };
            f.Vertices.AddRange(vertices);
            if (f.Vertices.Count > 0) t.Frames.Add(f);
        }

        /// <summary>
        /// src から dst を作り直したこと（厚み付け。段 15）を控えへ写す。map は src の頂点番号 → dst の頂点番号の並び
        /// （null は対応なし）。src を持ち主とする枠を dst へ付け替え、枠の頂点を対応する頂点の並びへ広げる。
        /// Source の枠（頂点の平均が入力の頂点に当たる）は広げると平均が変わるので、厚み付けを通さないこと。
        /// </summary>
        public static void Expanded(MeshObject src, MeshObject dst, int[][] map)
        {
            var t = _current;
            if (t == null || src == null || dst == null || map == null || ReferenceEquals(src, dst)) return;
            foreach (var f in t.Frames)
            {
                if (!ReferenceEquals(f.Owner, src)) continue;
                var seen = new HashSet<int>();
                var nv = new List<int>(f.Vertices.Count * 2);
                foreach (int v in f.Vertices)
                {
                    if (v < 0 || v >= map.Length || map[v] == null) continue;
                    foreach (int m in map[v]) if (seen.Add(m)) nv.Add(m);
                }
                f.Owner = dst;
                f.Vertices.Clear();
                f.Vertices.AddRange(nv);
            }
        }

        /// <summary>src の頂点が dst の offset 番目から連結されたことを控えへ写す。</summary>
        public static void Moved(MeshObject src, MeshObject dst, int offset)
        {
            var t = _current;
            if (t == null || src == null || dst == null || ReferenceEquals(src, dst)) return;
            foreach (var f in t.Frames)
            {
                if (!ReferenceEquals(f.Owner, src)) continue;
                f.Owner = dst;
                for (int i = 0; i < f.Vertices.Count; i++) f.Vertices[i] += offset;
            }
        }

        /// <summary>
        /// mo の頂点を詰め直したこと（重複頂点の結合）を控えへ写す。map は元の番号 → 新しい番号。
        /// 結合で 1 つになった頂点は枠の中で 1 つにまとめる。
        /// </summary>
        public static void Remapped(MeshObject mo, int[] map)
        {
            var t = _current;
            if (t == null || mo == null || map == null) return;
            foreach (var f in t.Frames)
            {
                if (!ReferenceEquals(f.Owner, mo)) continue;
                var seen = new HashSet<int>();
                var nv = new List<int>(f.Vertices.Count);
                foreach (int v in f.Vertices)
                {
                    if (v < 0 || v >= map.Length) continue;
                    int m = map[v];
                    if (m >= 0 && seen.Add(m)) nv.Add(m);
                }
                f.Vertices.Clear();
                f.Vertices.AddRange(nv);
            }
        }

        /// <summary>
        /// 生成器が点を置いた空間（梯子の点・配置位置の空間）から、今の頂点の空間への行列。
        /// 枠を持つメッシュへ全体の変換（ピボット・回転と拡大の焼き込み・置き場所・段 5 の M）を
        /// 掛けるたびに左から掛け合わせる（Transformed）。
        /// </summary>
        public UnityEngine.Matrix4x4 ToOutput = UnityEngine.Matrix4x4.identity;

        /// <summary>mo の全頂点へ行列 m を掛けたことを控える（mo が枠を持つときだけ）。</summary>
        public static void Transformed(MeshObject mo, UnityEngine.Matrix4x4 m)
        {
            var t = _current;
            if (t == null || mo == null) return;
            foreach (var f in t.Frames)
            {
                if (!ReferenceEquals(f.Owner, mo)) continue;
                t.ToOutput = m * t.ToOutput;
                return;
            }
        }

        /// <summary>mo を持ち主とする枠。</summary>
        public List<Frame> FramesOf(MeshObject mo)
        {
            var list = new List<Frame>();
            foreach (var f in Frames) if (ReferenceEquals(f.Owner, mo)) list.Add(f);
            return list;
        }
    }
}
