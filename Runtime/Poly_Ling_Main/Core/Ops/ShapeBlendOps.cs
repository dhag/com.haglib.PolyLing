// ShapeBlendOps.cs
// 試し表示・モーション再生・書き出しが共通に使う口：名前の解決、差分の取り出し、重み付きの足し込み。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【正本】PolyLing_モーフ整理計画.md 3.5・3.6・段 5・段 7
//
// 【指す相手】形（ShapeDef の番号。MorphTargetKey）。差分は ShapeOps.GetSparseOffsets
//   （今の形がどれでも基本の形との差）。段 7 で旧モーフ（隠しオブジェクト）を指す鍵は撤去した。
//
// 【名前の解決】表情の名前 → 形の名前 の順（Name、無ければ NameEnglish の完全一致）。
//   表情の展開：
//     頂点の表情  … 同じ名前の形があれば形 1 つ（重み 1）。無ければ何も指さない。
//     グループ    … 子（GroupChildren）のうち形であるもの（子の名前 × 重み）。
//     それ以外    … 何も指さない。
//
// 【足し込み】ShapeBlendBuffer。置き場所は描画オブジェクトの WorkingPositions
//   （表示は Position ＋ WorkingPositions）。今の形が基本の形でないと二重に動くので、
//   試し表示と再生は始めるときに基本の形へ切り替える（呼び出し側。3.6）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>ブレンドの相手（形）を指す鍵。</summary>
    public readonly struct MorphTargetKey : IEquatable<MorphTargetKey>
    {
        /// <summary>形の番号（1 以上）。</summary>
        public readonly int ShapeId;

        private MorphTargetKey(int shapeId) { ShapeId = shapeId; }

        public static MorphTargetKey Shape(int shapeId) => new MorphTargetKey(shapeId);

        public bool IsShape => ShapeId > 0;

        public bool Equals(MorphTargetKey o) => ShapeId == o.ShapeId;
        public override bool Equals(object obj) => obj is MorphTargetKey k && Equals(k);
        public override int GetHashCode() => ShapeId;
        public override string ToString() => $"shape:{ShapeId}";
    }

    /// <summary>鍵と重みの組。</summary>
    public struct MorphTargetTerm
    {
        public MorphTargetKey Key;
        public float Weight;
        public MorphTargetTerm(MorphTargetKey key, float weight) { Key = key; Weight = weight; }
    }

    /// <summary>描画オブジェクト 1 つぶんの差分。</summary>
    public struct MorphTargetMeshOffsets
    {
        /// <summary>差分を足す描画オブジェクトの索引。</summary>
        public int MeshIndex;
        /// <summary>0 でない差分（頂点番号の昇順）。</summary>
        public List<(int VertexIndex, Vector3 Offset)> Offsets;
    }

    /// <summary>名前の解決の結果。</summary>
    public enum MorphTargetResolve
    {
        /// <summary>同じ名前の表情も形も無い。</summary>
        None = 0,
        /// <summary>表情として見つかった（指す相手が 0 件のこともある）。</summary>
        Expression = 1,
        /// <summary>形として見つかった。</summary>
        Shape = 2,
    }

    public static class ShapeBlendOps
    {
        // ================================================================
        // 名前の解決
        // ================================================================

        /// <summary>表情を名前で引く（Name、無ければ NameEnglish の完全一致）。</summary>
        public static MorphExpression FindExpression(ModelContext model, string name)
        {
            if (model?.MorphExpressions == null || string.IsNullOrEmpty(name)) return null;
            foreach (var e in model.MorphExpressions)
                if (e != null && string.Equals(e.Name, name, StringComparison.Ordinal)) return e;
            foreach (var e in model.MorphExpressions)
                if (e != null && string.Equals(e.NameEnglish, name, StringComparison.Ordinal)) return e;
            return null;
        }

        /// <summary>形を名前で引く（Name、無ければ NameEnglish の完全一致）。</summary>
        public static ShapeDef FindShape(ModelContext model, string name)
        {
            if (model?.Shapes == null || string.IsNullOrEmpty(name)) return null;
            var s = model.FindShapeByName(name);
            if (s != null) return s;
            foreach (var d in model.Shapes)
                if (d != null && !string.IsNullOrEmpty(d.NameEnglish) && string.Equals(d.NameEnglish, name, StringComparison.Ordinal)) return d;
            return null;
        }

        /// <summary>名前が表情か形に当たるか。</summary>
        public static bool CanResolve(ModelContext model, string name)
            => FindExpression(model, name) != null || FindShape(model, name) != null;

        /// <summary>
        /// 名前（表情 → 形 の順）を指す相手の並びへ展開し、into へ足す。
        /// </summary>
        public static MorphTargetResolve Resolve(ModelContext model, string name, float weight, List<MorphTargetTerm> into)
        {
            var expr = FindExpression(model, name);
            if (expr != null)
            {
                ExpandExpression(model, expr, weight, into);
                return MorphTargetResolve.Expression;
            }
            var shape = FindShape(model, name);
            if (shape != null)
            {
                into?.Add(new MorphTargetTerm(MorphTargetKey.Shape(shape.Id), weight));
                return MorphTargetResolve.Shape;
            }
            return MorphTargetResolve.None;
        }

        /// <summary>表情を指す相手の並びへ展開し、into へ足す（冒頭の規則）。</summary>
        public static void ExpandExpression(ModelContext model, MorphExpression expr, float weight, List<MorphTargetTerm> into)
            => ExpandExpression(model, expr, weight, into, null);

        /// <summary>
        /// 表情を展開する。重みを仮に変えて見る（パネルで重みを動かしている間の試し表示）ときは、
        /// shapeWeights（子の形の名前 → 重み）で上書きする。
        /// </summary>
        public static void ExpandExpression(ModelContext model, MorphExpression expr, float weight, List<MorphTargetTerm> into,
            IReadOnlyDictionary<string, float> shapeWeights)
        {
            if (model == null || expr == null || into == null) return;

            if (expr.Type == MorphType.Vertex)
            {
                var same = model.FindShapeByName(expr.Name);
                if (same != null) into.Add(new MorphTargetTerm(MorphTargetKey.Shape(same.Id), weight));
                return;
            }

            if (expr.Type == MorphType.Group && expr.GroupChildren != null)
                foreach (var c in expr.GroupChildren)
                {
                    var s = model.FindShapeByName(c.Name);
                    if (s == null) continue;
                    float cw = shapeWeights != null && shapeWeights.TryGetValue(s.Name, out float ow) ? ow : c.Weight;
                    into.Add(new MorphTargetTerm(MorphTargetKey.Shape(s.Id), weight * cw));
                }
        }

        // ================================================================
        // 差分
        // ================================================================

        /// <summary>
        /// 鍵が指す形の差分を、描画オブジェクトごとに into へ足す（差分の無い描画オブジェクトは足さない）。
        /// </summary>
        public static void CollectOffsets(ModelContext model, MorphTargetKey key, List<MorphTargetMeshOffsets> into)
        {
            if (model?.MeshContextList == null || into == null || !key.IsShape) return;

            // UV の形は位置の差分を持たない（試し表示・再生・ブレンドシェイプには出ない）。
            if (model.FindShapeById(key.ShapeId)?.IsUV == true) return;
            int current = model.CurrentShapeId;
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var mo = list[i]?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;
                var off = ShapeOps.GetSparseOffsets(mo, current, key.ShapeId);
                if (off.Count > 0) into.Add(new MorphTargetMeshOffsets { MeshIndex = i, Offsets = off });
            }
        }

        /// <summary>描画オブジェクト meshIndex に対する、鍵が指す形の差分（無ければ空）。</summary>
        public static List<(int VertexIndex, Vector3 Offset)> OffsetsFor(ModelContext model, MorphTargetKey key, int meshIndex)
        {
            if (model == null || !key.IsShape) return new List<(int, Vector3)>();
            var mo = model.GetMeshContext(meshIndex)?.MeshObject;
            return ShapeOps.GetSparseOffsets(mo, model.CurrentShapeId, key.ShapeId);
        }
    }

    /// <summary>
    /// 重み付きの足し込み：WorkingPositions に「Σ 重み ×（形 − 基本の形）」を書く。
    /// チャンネル（表情 1 つ・トラック 1 本など）ごとに重みを変えられる。
    /// 差分は AddChannel の時点で取り出して控える。
    /// </summary>
    public sealed class ShapeBlendBuffer
    {
        private struct Entry
        {
            public int MeshIndex;
            public List<(int VertexIndex, Vector3 Offset)> Offsets;
            public float Weight;
            public int Channel;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly HashSet<int> _meshes = new HashSet<int>();
        private readonly List<MorphTargetMeshOffsets> _tmp = new List<MorphTargetMeshOffsets>();

        /// <summary>チャンネルの数。</summary>
        public int ChannelCount { get; private set; }

        /// <summary>差分の組（描画オブジェクト × 相手）の数。</summary>
        public int EntryCount => _entries.Count;

        /// <summary>足し込む描画オブジェクトの索引。</summary>
        public IReadOnlyCollection<int> TouchedMeshes => _meshes;

        /// <summary>控えを捨てる。モデルの WorkingPositions には触らない（先に Clear を呼ぶ）。</summary>
        public void Reset()
        {
            _entries.Clear();
            _meshes.Clear();
            ChannelCount = 0;
        }

        /// <summary>
        /// 相手の並びを 1 つのチャンネルとして足す。足した差分の組の数を entryCount に返す。
        /// 戻り値はチャンネルの番号。
        /// </summary>
        public int AddChannel(ModelContext model, List<MorphTargetTerm> terms, out int entryCount)
        {
            int channel = ChannelCount++;
            entryCount = 0;
            if (model == null || terms == null) return channel;
            foreach (var t in terms)
            {
                _tmp.Clear();
                ShapeBlendOps.CollectOffsets(model, t.Key, _tmp);
                foreach (var m in _tmp)
                {
                    _entries.Add(new Entry { MeshIndex = m.MeshIndex, Offsets = m.Offsets, Weight = t.Weight, Channel = channel });
                    _meshes.Add(m.MeshIndex);
                    entryCount++;
                }
            }
            return channel;
        }

        /// <summary>チャンネルの重みで WorkingPositions を書き直し、表示を更新する。</summary>
        public void Apply(ModelContext model, IReadOnlyList<float> channelWeights, Action<MeshContext> sync)
        {
            if (model == null || _meshes.Count == 0) return;

            foreach (int mi in _meshes)
            {
                var mc = model.GetMeshContext(mi);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (mc.WorkingPositions == null || mc.WorkingPositions.Length != mo.VertexCount)
                    mc.WorkingPositions = new Vector3[mo.VertexCount];
                else
                    Array.Clear(mc.WorkingPositions, 0, mc.WorkingPositions.Length);
            }

            foreach (var e in _entries)
            {
                float cw = channelWeights != null && e.Channel < channelWeights.Count ? channelWeights[e.Channel] : 0f;
                float k = e.Weight * cw;
                if (k == 0f) continue;
                var wp = model.GetMeshContext(e.MeshIndex)?.WorkingPositions;
                if (wp == null) continue;
                foreach (var (vi, off) in e.Offsets)
                    if (vi >= 0 && vi < wp.Length) wp[vi] += off * k;
            }

            if (sync != null)
                foreach (int mi in _meshes)
                {
                    var mc = model.GetMeshContext(mi);
                    if (mc?.MeshObject != null) sync(mc);
                }
        }

        /// <summary>足し込みを消し（WorkingPositions = null）、表示を更新する。控えは残す。</summary>
        public void Clear(ModelContext model, Action<MeshContext> sync)
        {
            if (model == null) return;
            foreach (int mi in _meshes)
            {
                var mc = model.GetMeshContext(mi);
                if (mc == null || mc.WorkingPositions == null) continue;
                mc.WorkingPositions = null;
                if (mc.MeshObject != null) sync?.Invoke(mc);
            }
        }
    }
}
