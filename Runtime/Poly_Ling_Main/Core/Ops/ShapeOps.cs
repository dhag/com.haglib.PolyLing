// ShapeOps.cs
// 形（名前付きの頂点位置）の切り替え・取り出し・控えの出し入れ・点検。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 中身の決まりは Core/Data/ShapeData.cs の冒頭注記。
// 位置の比較は成分の完全一致で行う（Unity の Vector3 == は近似なので使わない。
// 近似で「基本と同じ」と見なすと、わずかな差の形が消える）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Ops
{
    /// <summary>Undo 用に控える、頂点 1 個ぶんの形の位置。</summary>
    public struct ShapeEntryRef
    {
        /// <summary>頂点のいる描画オブジェクト。</summary>
        public MeshUndoTarget Target;
        /// <summary>頂点番号。</summary>
        public int VertexIndex;
        /// <summary>控えていた位置。</summary>
        public Vector3 Position;
    }

    /// <summary>Undo 用に控える、頂点 1 個ぶんの形の UV（UV スロット 0）。</summary>
    public struct ShapeUVEntryRef
    {
        public MeshUndoTarget Target;
        public int VertexIndex;
        public Vector2 UV;
    }

    /// <summary>
    /// メッシュ 1 個ぶんの形の控えの写し（ShapeOps.CaptureShapes が作る）。
    /// PerVertex が null なら「形を持つ頂点が無かった」。
    /// </summary>
    public class ShapeSnapshot
    {
        /// <summary>頂点番号順の控え。控えを持たない頂点は null。</summary>
        public List<ShapePosition>[] PerVertex;
    }

    /// <summary>形の点検結果（件数）。</summary>
    public struct ShapeAuditResult
    {
        /// <summary>今の形の番号が一覧に無い（0 でない場合）。0 か 1。</summary>
        public int CurrentMissing;
        /// <summary>一覧の番号の重複・0 以下の番号の件数。</summary>
        public int BadIds;
        /// <summary>一覧の名前の重複・空の名前の件数。</summary>
        public int BadNames;
        /// <summary>一覧に無い番号の控えの件数。</summary>
        public int UnknownEntries;
        /// <summary>持ってはいけない控えの件数（今の形が 0 のときの番号 0、0 以外のときの今の形の番号）。</summary>
        public int ForbiddenEntries;
        /// <summary>同じ頂点に同じ番号の控えが複数ある件数。</summary>
        public int DuplicateEntries;
        /// <summary>基本の形と同じ位置の控えの件数（害は無いが要らない）。</summary>
        public int RedundantEntries;
        /// <summary>ミラー側の形の控えが、実体側の控えの鏡映と合わない頂点の数。</summary>
        public int MirrorMismatch;
        /// <summary>モディファイアの出力が持っている形の控えの数（出力は形を持たない決まり）。</summary>
        public int ModifierOutputEntries;

        /// <summary>直すべきものの合計（RedundantEntries は入れない）。</summary>
        public int Issues => CurrentMissing + BadIds + BadNames + UnknownEntries + ForbiddenEntries + DuplicateEntries
                           + MirrorMismatch + ModifierOutputEntries;
    }

    public static class ShapeOps
    {
        /// <summary>基本の形の番号。</summary>
        public const int BaseShapeId = 0;

        /// <summary>成分の完全一致。</summary>
        public static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

        /// <summary>成分の完全一致（UV）。</summary>
        public static bool Same(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;

        // ================================================================
        // UV（UV スロット 0。種類が UV の形。ShapeData.cs の冒頭注記）
        // ================================================================

        /// <summary>頂点が UV スロット 0 を持つか。</summary>
        public static bool HasUV0(Vertex v) => v?.UVs != null && v.UVs.Count > 0;

        /// <summary>基本の形での UV スロット 0。UV を持たない頂点は zero。</summary>
        public static Vector2 GetBaseUV(Vertex v, int current)
        {
            if (!HasUV0(v)) return Vector2.zero;
            if (current == BaseShapeId) return v.UVs[0];
            return v.TryGetUVShape(BaseShapeId, out var b) ? b : v.UVs[0];
        }

        /// <summary>形 shapeId での UV スロット 0。</summary>
        public static Vector2 GetShapeUV(Vertex v, int current, int shapeId)
        {
            if (!HasUV0(v)) return Vector2.zero;
            if (shapeId == current) return v.UVs[0];
            if (shapeId == BaseShapeId) return GetBaseUV(v, current);
            return v.TryGetUVShape(shapeId, out var p) ? p : GetBaseUV(v, current);
        }

        /// <summary>
        /// 形 shapeId の、基本の形からの UV の差分のうち 0 でないもの（頂点番号の昇順）。
        /// PMX の UV モーフの書き出しが使う。
        /// </summary>
        public static List<(int VertexIndex, Vector2 Offset)> GetSparseUVOffsets(MeshObject mo, int current, int shapeId)
        {
            var result = new List<(int, Vector2)>();
            if (mo == null || shapeId == BaseShapeId) return result;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                if (!HasUV0(v)) continue;
                Vector2 d = GetShapeUV(v, current, shapeId) - GetBaseUV(v, current);
                if (d.x != 0f || d.y != 0f) result.Add((i, d));
            }
            return result;
        }

        /// <summary>メッシュに形の UV の控えを持つ頂点が 1 つでもあるか。</summary>
        public static bool HasAnyUVShapes(MeshObject mo)
        {
            if (mo?.Vertices == null) return false;
            for (int i = 0; i < mo.Vertices.Count; i++)
                if (mo.Vertices[i] != null && mo.Vertices[i].UVShapes != null) return true;
            return false;
        }

        /// <summary>メッシュに形の控え（位置か UV）を持つ頂点が 1 つでもあるか。</summary>
        public static bool HasAnyShapeData(MeshObject mo) => HasAnyShapes(mo) || HasAnyUVShapes(mo);

        // ================================================================
        // 取り出し
        // ================================================================

        /// <summary>基本の形での位置。current は今の形の番号。</summary>
        public static Vector3 GetBasePosition(Vertex v, int current)
        {
            if (current == BaseShapeId) return v.Position;
            return v.TryGetShape(BaseShapeId, out var b) ? b : v.Position;
        }

        /// <summary>形 shapeId での位置。current は今の形の番号。</summary>
        public static Vector3 GetShapePosition(Vertex v, int current, int shapeId)
        {
            if (shapeId == current) return v.Position;
            if (shapeId == BaseShapeId) return GetBasePosition(v, current);
            return v.TryGetShape(shapeId, out var p) ? p : GetBasePosition(v, current);
        }

        /// <summary>
        /// 形 shapeId の、基本の形からの差分（頂点番号順）。差の無い頂点は zero。
        /// </summary>
        public static Vector3[] GetOffsets(MeshObject mo, int current, int shapeId)
        {
            int n = mo?.VertexCount ?? 0;
            var result = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                result[i] = GetShapePosition(v, current, shapeId) - GetBasePosition(v, current);
            }
            return result;
        }

        /// <summary>
        /// 形 shapeId の、基本の形からの差分のうち 0 でないもの（頂点番号の昇順）。
        /// 中身は GetOffsets と同じ計算。試し表示・再生・書き出しはこれを使う（PolyLing_モーフ整理計画.md 3.6）。
        /// </summary>
        public static List<(int VertexIndex, Vector3 Offset)> GetSparseOffsets(MeshObject mo, int current, int shapeId)
        {
            var result = new List<(int, Vector3)>();
            if (mo == null || shapeId == BaseShapeId) return result;
            var all = GetOffsets(mo, current, shapeId);
            for (int i = 0; i < all.Length; i++)
                if (all[i].x != 0f || all[i].y != 0f || all[i].z != 0f) result.Add((i, all[i]));
            return result;
        }

        /// <summary>
        /// 形 shapeId で基本の形と位置（UV の形なら UV スロット 0）が違う頂点の数（モデル全体）。
        /// </summary>
        public static int CountMovedVertices(ModelContext model, int shapeId)
        {
            if (model?.MeshContextList == null || shapeId == BaseShapeId) return 0;
            int current = model.CurrentShapeId;
            bool uv = model.FindShapeById(shapeId)?.IsUV == true;
            int count = 0;
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                foreach (var v in mo.Vertices)
                {
                    bool moved = uv
                        ? HasUV0(v) && !Same(GetShapeUV(v, current, shapeId), GetBaseUV(v, current))
                        : !Same(GetShapePosition(v, current, shapeId), GetBasePosition(v, current));
                    if (moved) count++;
                }
            }
            return count;
        }

        // ================================================================
        // 切り替え
        // ================================================================

        /// <summary>
        /// 描画オブジェクト 1 個の頂点を、形 from の状態から形 to の状態へ移す。
        /// </summary>
        public static void SwitchMesh(MeshObject mo, int from, int to)
        {
            if (mo == null || from == to) return;
            var verts = mo.Vertices;

            // 1. from → 基本
            if (from != BaseShapeId)
            {
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    if (v.TryGetShape(BaseShapeId, out var b))
                    {
                        if (!Same(v.Position, b)) v.SetShape(from, v.Position);
                        else v.RemoveShape(from);
                        v.Position = b;
                        v.RemoveShape(BaseShapeId);
                    }
                    else
                    {
                        // 形 from の間に足した頂点。どの形でも今の位置。
                        v.RemoveShape(from);
                    }

                    // UV スロット 0 も同じ規則（ShapeData.cs の冒頭注記）。
                    if (!HasUV0(v)) { v.UVShapes = null; continue; }
                    if (v.TryGetUVShape(BaseShapeId, out var bu))
                    {
                        if (!Same(v.UVs[0], bu)) v.SetUVShape(from, v.UVs[0]);
                        else v.RemoveUVShape(from);
                        v.UVs[0] = bu;
                        v.RemoveUVShape(BaseShapeId);
                    }
                    else
                    {
                        v.RemoveUVShape(from);
                    }
                }
            }

            // 2. 基本 → to
            if (to != BaseShapeId)
            {
                for (int i = 0; i < verts.Count; i++)
                {
                    var v = verts[i];
                    v.SetShape(BaseShapeId, v.Position);
                    if (v.TryGetShape(to, out var p))
                    {
                        v.Position = p;
                        v.RemoveShape(to);
                    }

                    if (!HasUV0(v)) { v.UVShapes = null; continue; }
                    v.SetUVShape(BaseShapeId, v.UVs[0]);
                    if (v.TryGetUVShape(to, out var pu))
                    {
                        v.UVs[0] = pu;
                        v.RemoveUVShape(to);
                    }
                }
            }

            mo.InvalidatePositionCache();
        }

        /// <summary>
        /// モデルの今の形を to へ切り替える。全描画オブジェクトの頂点位置を書き換え、表示用メッシュを作り直す。
        /// PreviousShapeId は触らない（呼び出し側が決める）。
        /// </summary>
        public static void SwitchModel(ModelContext model, int to)
        {
            if (model == null) return;
            int from = model.CurrentShapeId;
            if (from == to) return;

            var list = model.MeshContextList;
            if (list != null)
            {
                foreach (var mc in list)
                {
                    var mo = mc?.MeshObject;
                    if (mo == null || mo.VertexCount == 0) continue;
                    // 試し表示・再生の足し込みは「今の形の位置」への差分なので、切り替えたら捨てる（3.4）。
                    // 持ち主（試し表示・再生）は切り替えの知らせで自分の状態を終える。
                    mc.WorkingPositions = null;
                    SwitchMesh(mo, from, to);
                    mc.ReplaceUnityMesh(mo.ToUnityMesh());
                }
            }
            model.CurrentShapeId = to;
            model.IsDirty = true;
        }

        // ================================================================
        // 控えの出し入れ（形の追加・削除と、その Undo）
        // ================================================================

        /// <summary>
        /// 形 dstId の控えを、形 srcId の位置で作る（基本の形と違う頂点だけ）。
        /// dstId は今の形であってはならない。作った件数を返す。
        /// </summary>
        public static int CopyShape(ModelContext model, int srcId, int dstId)
        {
            if (model?.MeshContextList == null) return 0;
            int current = model.CurrentShapeId;
            if (dstId == current || dstId == BaseShapeId || srcId == dstId) return 0;

            int count = 0;
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                foreach (var v in mo.Vertices)
                {
                    // UV スロット 0（UV の形の写し。位置の形では基本と同じなので控えは作られない）
                    if (HasUV0(v))
                    {
                        var u = GetShapeUV(v, current, srcId);
                        if (Same(u, GetBaseUV(v, current))) v.RemoveUVShape(dstId);
                        else { v.SetUVShape(dstId, u); count++; }
                    }

                    var p = GetShapePosition(v, current, srcId);
                    if (Same(p, GetBasePosition(v, current))) { v.RemoveShape(dstId); continue; }
                    v.SetShape(dstId, p);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 形 dstId の控えを、形 srcId を左右反転した位置で作る（基本の形と違う頂点だけ）。
        /// dstId は今の形・基本の形であってはならない。srcId は基本の形であってはならない。作った件数を返す。
        ///
        /// 【規則】（PolyLing_モーフ整理計画.md 3.7）
        ///   ・ミラーの関係にあるオブジェクト（対の実体側・ミラー側、実体化したミラーとその元、表示ミラー）は
        ///     srcId の写しにする。左右が鏡映で決まっている（ミラー側の形は実体側の鏡映）ので、
        ///     左右を入れ替えても同じ形になる。
        ///   ・それ以外は、同じメッシュの中の対称頂点（ShapeSymmetryOps.BuildSymmetryVertexMap）へ
        ///     差分（形 − 基本の形）を鏡映して写す。対応は基本の形の位置で取る（今の形に入っていても同じ結果）。
        ///     対称面はメッシュのミラー軸・ミラー位置（無ければモデルの対称設定。ShapeSymmetryOps.ResolveAxis）。
        ///   ・相手が見つからない頂点は基本の形のままにする。そのうち srcId で動いていた頂点の数を unmatched に返す
        ///     （反転先が無く、その動きは新しい形に入らない）。
        /// </summary>
        public static int MirrorShape(ModelContext model, int srcId, int dstId, out int unmatched,
                                      float threshold = ShapeSymmetryOps.DefaultSymmetryThreshold)
        {
            unmatched = 0;
            if (model?.MeshContextList == null) return 0;
            int current = model.CurrentShapeId;
            if (dstId == current || dstId == BaseShapeId || srcId == dstId || srcId == BaseShapeId) return 0;

            var list = model.MeshContextList;
            int count = 0;
            for (int mi = 0; mi < list.Count; mi++)
            {
                var mc = list[mi];
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0) continue;

                int n = mo.VertexCount;
                var basePos = new Vector3[n];
                var srcPos  = new Vector3[n];
                bool anyMoved = false;
                for (int i = 0; i < n; i++)
                {
                    var v = mo.Vertices[i];
                    basePos[i] = GetBasePosition(v, current);
                    srcPos[i]  = GetShapePosition(v, current, srcId);
                    if (!Same(srcPos[i], basePos[i])) anyMoved = true;
                }
                if (!anyMoved) continue;

                if (IsMirrorTied(model, mi))
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (Same(srcPos[i], basePos[i])) continue;
                        mo.Vertices[i].SetShape(dstId, srcPos[i]);
                        count++;
                    }
                    continue;
                }

                ShapeSymmetryOps.ResolveAxis(model, mc, out int axis, out float distance);
                var map = ShapeSymmetryOps.BuildSymmetryVertexMap(basePos, axis, distance, threshold);
                for (int d = 0; d < n; d++)
                {
                    int s = (map != null && d < map.Length) ? map[d] : -1;
                    if (s < 0 || s >= n)
                    {
                        if (!Same(srcPos[d], basePos[d])) unmatched++;
                        continue;
                    }
                    if (Same(srcPos[s], basePos[s])) continue;
                    Vector3 p = basePos[d] + MirrorBranchOps.MirrorNormal(axis, srcPos[s] - basePos[s]);
                    if (Same(p, basePos[d])) continue;
                    mo.Vertices[d].SetShape(dstId, p);
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 描画オブジェクトがミラーの関係にあるか（対の実体側・ミラー側、実体化したミラーとその元、表示ミラー）。
        /// MirrorShape が「写しにする」オブジェクトを決めるのに使う。
        /// </summary>
        private static bool IsMirrorTied(ModelContext model, int index)
        {
            var list = model.MeshContextList;
            var mc = list[index];
            if (mc == null) return false;
            if (mc.MirrorType > 0 || mc.IsBakedMirror || mc.HasBakedMirrorChild) return true;
            if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror) return true;
            var pair = model.GetMirrorPair(mc);
            if (pair != null && pair.IsValid) return true;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].BakedMirrorSourceIndex == index) return true;
            return false;
        }

        /// <summary>形 shapeId の控えを全頂点から集める（消さない）。</summary>
        public static List<ShapeEntryRef> CollectEntries(ModelContext model, int shapeId)
        {
            var result = new List<ShapeEntryRef>();
            if (model?.MeshContextList == null) return result;
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                MeshUndoTarget target = default;
                bool targetSet = false;
                for (int i = 0; i < mo.Vertices.Count; i++)
                {
                    if (!mo.Vertices[i].TryGetShape(shapeId, out var p)) continue;
                    if (!targetSet) { target = MeshUndoTarget.Of(mc, model); targetSet = true; }
                    result.Add(new ShapeEntryRef { Target = target, VertexIndex = i, Position = p });
                }
            }
            return result;
        }

        /// <summary>形 shapeId の UV の控えを全頂点から集める（消さない）。</summary>
        public static List<ShapeUVEntryRef> CollectUVEntries(ModelContext model, int shapeId)
        {
            var result = new List<ShapeUVEntryRef>();
            if (model?.MeshContextList == null) return result;
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                MeshUndoTarget target = default;
                bool targetSet = false;
                for (int i = 0; i < mo.Vertices.Count; i++)
                {
                    if (!mo.Vertices[i].TryGetUVShape(shapeId, out var u)) continue;
                    if (!targetSet) { target = MeshUndoTarget.Of(mc, model); targetSet = true; }
                    result.Add(new ShapeUVEntryRef { Target = target, VertexIndex = i, UV = u });
                }
            }
            return result;
        }

        /// <summary>集めておいた UV の控えを形 shapeId として書き戻す。書けた件数を返す。</summary>
        public static int ApplyUVEntries(List<ShapeUVEntryRef> entries, int shapeId)
        {
            if (entries == null) return 0;
            int count = 0;
            foreach (var e in entries)
            {
                var mo = e.Target.Resolve()?.MeshObject;
                if (mo == null || e.VertexIndex < 0 || e.VertexIndex >= mo.Vertices.Count) continue;
                mo.Vertices[e.VertexIndex].SetUVShape(shapeId, e.UV);
                count++;
            }
            return count;
        }

        /// <summary>形 shapeId の控え（位置と UV）を全頂点から消す。消した件数を返す。</summary>
        public static int RemoveEntries(ModelContext model, int shapeId)
        {
            if (model?.MeshContextList == null) return 0;
            int count = 0;
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                foreach (var v in mo.Vertices)
                {
                    if (v.RemoveShape(shapeId)) count++;
                    if (v.RemoveUVShape(shapeId)) count++;
                }
            }
            return count;
        }

        /// <summary>集めておいた控えを形 shapeId として書き戻す。書けた件数を返す。</summary>
        public static int ApplyEntries(List<ShapeEntryRef> entries, int shapeId)
        {
            if (entries == null) return 0;
            int count = 0;
            MeshContext cachedMc = null;
            MeshContext cachedKey = null;
            ulong cachedId = 0UL;
            bool cached = false;
            foreach (var e in entries)
            {
                if (!cached || !ReferenceEquals(cachedKey, e.Target.Context) || cachedId != e.Target.ObjectId)
                {
                    cachedMc = e.Target.Resolve();
                    cachedKey = e.Target.Context;
                    cachedId = e.Target.ObjectId;
                    cached = true;
                }
                var mo = cachedMc?.MeshObject;
                if (mo == null || e.VertexIndex < 0 || e.VertexIndex >= mo.Vertices.Count) continue;
                mo.Vertices[e.VertexIndex].SetShape(shapeId, e.Position);
                count++;
            }
            return count;
        }

        // ================================================================
        // 一括の変換（段 2）
        //
        // 頂点の位置を「メッシュ全体に同じ変換」で書き換える操作は、形の控えにも同じ変換を掛ける。
        // 掛けないと、今の形だけが動いて他の形が元の場所に取り残される。
        // 判定の規則（PolyLing_モーフ整理計画.md 4 章 注意 1）：
        //   ・モデルに残る頂点を一括で書き換える          → TransformShapes / TransformVertexShapes / MapShapes
        //   ・ドラッグ中に開始位置から毎回計算し直す      → 開始時に CaptureShapes、毎回 MapShapesFrom
        //
        // 形の控えは、Position と同じ計算（同じ関数・同じ行列を同じ順で）で出す。
        // 比較は成分の完全一致なので、式が違うと（行列 2 回と合成行列 1 回など）丸めの差が出て、
        // 動かしていない頂点が「形で動いた頂点」になる。行列を渡す口（TransformShapes）は Position も
        // 同じ行列 1 回で出している所でだけ使い、それ以外は Position に使う関数を MapShapes へ渡す。
        // Position を同じ計算で出せない所（GPU の読み戻し）は MapVertexShapesKeepingEqual。
        //   ・Undo／Redo で位置を丸ごと戻す記録          → CaptureShapes を持ち、RestoreShapes で戻す
        //   ・複製で頂点を作る（変換が分かる）            → 複製後に TransformVertexShapes
        //   ・複製で頂点を作る（補間・変形・別モデル）    → ClearShapes（どの形でも同じ位置になる）
        //   ・ミラー側を実体側から作る・取り直す          → MirrorShapesFrom / SyncMirrorShapes
        //   ・モディファイアの出力・一時の写し            → ClearShapes
        //   ・今の形への頂点編集                          → 何もしない
        // ================================================================

        /// <summary>メッシュに形の控えを持つ頂点が 1 つでもあるか。</summary>
        public static bool HasAnyShapes(MeshObject mo)
        {
            if (mo?.Vertices == null) return false;
            for (int i = 0; i < mo.Vertices.Count; i++)
                if (mo.Vertices[i] != null && mo.Vertices[i].Shapes != null) return true;
            return false;
        }

        /// <summary>頂点 1 個の形の控え全部に、位置の写像 f を掛ける。</summary>
        public static void MapVertexShapes(Vertex v, System.Func<Vector3, Vector3> f)
        {
            if (v?.Shapes == null || f == null) return;
            for (int i = 0; i < v.Shapes.Count; i++)
                v.Shapes[i] = new ShapePosition(v.Shapes[i].ShapeId, f(v.Shapes[i].Position));
        }

        /// <summary>頂点 1 個の形の控え全部に、行列 m（点として）を掛ける。</summary>
        public static void TransformVertexShapes(Vertex v, Matrix4x4 m)
        {
            if (v?.Shapes == null) return;
            for (int i = 0; i < v.Shapes.Count; i++)
                v.Shapes[i] = new ShapePosition(v.Shapes[i].ShapeId, m.MultiplyPoint3x4(v.Shapes[i].Position));
        }

        /// <summary>メッシュの全頂点の形の控えに、行列 m（点として）を掛ける。</summary>
        public static void TransformShapes(MeshObject mo, Matrix4x4 m)
        {
            if (mo?.Vertices == null) return;
            for (int i = 0; i < mo.Vertices.Count; i++) TransformVertexShapes(mo.Vertices[i], m);
        }

        /// <summary>メッシュの全頂点の形の控えに、位置の写像 f を掛ける。</summary>
        public static void MapShapes(MeshObject mo, System.Func<Vector3, Vector3> f)
        {
            if (mo?.Vertices == null || f == null) return;
            for (int i = 0; i < mo.Vertices.Count; i++) MapVertexShapes(mo.Vertices[i], f);
        }

        /// <summary>
        /// 形の控えを開始時の控え start へ戻してから、位置の写像 f を掛ける。
        /// ドラッグ中に「開始位置から今の位置」を毎回計算し直す操作用（掛け重ねを避ける）。
        /// f は Position の計算に使うものと同じ関数を渡す。
        /// </summary>
        public static void MapShapesFrom(MeshObject mo, ShapeSnapshot start, System.Func<Vector3, Vector3> f)
        {
            if (mo == null || start == null || f == null) return;
            if (start.PerVertex == null) return;   // 開始時に形が無かった
            RestoreShapes(mo, start);
            MapShapes(mo, f);
        }

        /// <summary>
        /// 頂点 1 個の形の控えを写す。変換前の位置 oldPosition と同じ控えは、変換後の今の位置
        /// （v.Position）にする。違う控えには f を掛ける。
        /// Position を f と同じ計算で出せない操作用（GPU で計算した位置を読み戻す焼き込みなど）。
        /// v.Position を書き換えた後に呼ぶ。
        /// </summary>
        public static void MapVertexShapesKeepingEqual(Vertex v, Vector3 oldPosition, System.Func<Vector3, Vector3> f)
        {
            if (v?.Shapes == null) return;
            for (int i = 0; i < v.Shapes.Count; i++)
            {
                var e = v.Shapes[i];
                Vector3 p = Same(e.Position, oldPosition) ? v.Position : (f != null ? f(e.Position) : e.Position);
                v.Shapes[i] = new ShapePosition(e.ShapeId, p);
            }
        }

        /// <summary>メッシュの形の控えを丸ごと控える（Undo・ドラッグ開始用）。null は返さない。</summary>
        public static ShapeSnapshot CaptureShapes(MeshObject mo)
        {
            var snap = new ShapeSnapshot();
            if (mo?.Vertices == null || !HasAnyShapes(mo)) return snap;
            var per = new List<ShapePosition>[mo.Vertices.Count];
            for (int i = 0; i < per.Length; i++)
            {
                var s = mo.Vertices[i]?.Shapes;
                per[i] = s != null ? new List<ShapePosition>(s) : null;
            }
            snap.PerVertex = per;
            return snap;
        }

        /// <summary>
        /// CaptureShapes の控えを書き戻す。snap が null（控えていない）なら何もしない。
        /// 頂点数が違うときは、重なる範囲だけ戻す。
        /// </summary>
        public static void RestoreShapes(MeshObject mo, ShapeSnapshot snap)
        {
            if (mo?.Vertices == null || snap == null) return;
            var per = snap.PerVertex;
            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                var v = mo.Vertices[i];
                if (v == null) continue;
                var s = (per != null && i < per.Length) ? per[i] : null;
                v.Shapes = s != null ? new List<ShapePosition>(s) : null;
            }
        }

        /// <summary>頂点 1 個の形の控え（位置と UV）を全部落とす（どの形でも今の位置・UV にいる頂点になる）。</summary>
        public static void ClearShapes(Vertex v)
        {
            if (v == null) return;
            v.Shapes = null;
            v.UVShapes = null;
        }

        /// <summary>メッシュの全頂点の形の控え（位置と UV）を落とす（モディファイアの出力・別モデルへ渡す写し用）。</summary>
        public static void ClearShapes(MeshObject mo)
        {
            if (mo?.Vertices == null) return;
            for (int i = 0; i < mo.Vertices.Count; i++)
                ClearShapes(mo.Vertices[i]);
        }

        /// <summary>
        /// ミラー側の頂点 1 個の形の控えを、実体側の頂点の控えを鏡映して作り直す。
        /// mirrorPoint は実体側の位置 → ミラー側の位置（Position に使うものと同じ写像）。
        /// </summary>
        public static void MirrorShapesFrom(Vertex real, Vertex mirror, System.Func<Vector3, Vector3> mirrorPoint)
        {
            if (mirror == null) return;
            if (real?.Shapes == null || mirrorPoint == null) { mirror.Shapes = null; return; }
            var list = new List<ShapePosition>(real.Shapes.Count);
            for (int i = 0; i < real.Shapes.Count; i++)
                list.Add(new ShapePosition(real.Shapes[i].ShapeId, mirrorPoint(real.Shapes[i].Position)));
            mirror.Shapes = list;
        }

        /// <summary>
        /// ミラー側メッシュの形の控えを、実体側から作り直す。
        /// vertexMap は実体側の頂点番号 → ミラー側の頂点番号（null なら同じ番号）。
        /// 実体側もミラー側も形を持たなければ何もしない（ドラッグ中に毎フレーム呼ばれても軽い）。
        /// </summary>
        public static void SyncMirrorShapes(MeshObject real, MeshObject mirror, int[] vertexMap,
                                            System.Func<Vector3, Vector3> mirrorPoint)
        {
            if (real?.Vertices == null || mirror?.Vertices == null || mirrorPoint == null) return;
            if (!HasAnyShapes(real) && !HasAnyShapes(mirror)) return;
            int n = vertexMap != null ? System.Math.Min(vertexMap.Length, real.Vertices.Count) : real.Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                int mi = vertexMap != null ? vertexMap[i] : i;
                if (mi < 0 || mi >= mirror.Vertices.Count) continue;
                MirrorShapesFrom(real.Vertices[i], mirror.Vertices[mi], mirrorPoint);
            }
        }

        // ================================================================
        // 点検
        // ================================================================

        /// <summary>形の一覧と頂点の控えが決まりどおりかを数える。</summary>
        public static ShapeAuditResult Audit(ModelContext model)
        {
            var r = new ShapeAuditResult();
            if (model == null) return r;

            int current = model.CurrentShapeId;
            var ids = new HashSet<int>();
            var names = new HashSet<string>();
            if (model.Shapes != null)
            {
                foreach (var s in model.Shapes)
                {
                    if (s == null || s.Id <= 0 || !ids.Add(s.Id)) r.BadIds++;
                    if (s == null || string.IsNullOrEmpty(s.Name) || !names.Add(s.Name)) r.BadNames++;
                }
            }
            if (current != BaseShapeId && !ids.Contains(current)) r.CurrentMissing = 1;

            if (model.MeshContextList == null) return r;
            var seen = new HashSet<int>();
            foreach (var mc in model.MeshContextList)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                foreach (var v in mo.Vertices)
                {
                    AuditVertexUV(v, current, ids, seen, ref r);
                    if (v.Shapes == null) continue;
                    seen.Clear();
                    Vector3 basePos = GetBasePosition(v, current);
                    foreach (var e in v.Shapes)
                    {
                        if (!seen.Add(e.ShapeId)) r.DuplicateEntries++;

                        bool forbidden = current == BaseShapeId ? e.ShapeId == BaseShapeId : e.ShapeId == current;
                        if (forbidden) { r.ForbiddenEntries++; continue; }
                        if (e.ShapeId == BaseShapeId) continue;   // 今の形が 0 以外のときの基本の控え

                        if (!ids.Contains(e.ShapeId)) r.UnknownEntries++;
                        else if (Same(e.Position, basePos)) r.RedundantEntries++;
                    }
                }
            }

            AuditMirrors(model, ref r);
            AuditModifierOutputs(model, ref r);
            return r;
        }

        /// <summary>
        /// 頂点 1 個の UV の控えを、位置の控えと同じ規則で数える。
        /// UV を持たない頂点が UV の控えを持つのも「持ってはいけない控え」に数える。
        /// </summary>
        private static void AuditVertexUV(Vertex v, int current, HashSet<int> ids, HashSet<int> seen, ref ShapeAuditResult r)
        {
            if (v?.UVShapes == null) return;
            if (!HasUV0(v)) { r.ForbiddenEntries += v.UVShapes.Count; return; }
            seen.Clear();
            Vector2 baseUV = GetBaseUV(v, current);
            foreach (var e in v.UVShapes)
            {
                if (!seen.Add(e.ShapeId)) r.DuplicateEntries++;
                bool forbidden = current == BaseShapeId ? e.ShapeId == BaseShapeId : e.ShapeId == current;
                if (forbidden) { r.ForbiddenEntries++; continue; }
                if (e.ShapeId == BaseShapeId) continue;
                if (!ids.Contains(e.ShapeId)) r.UnknownEntries++;
                else if (Same(e.UV, baseUV)) r.RedundantEntries++;
            }
        }

        /// <summary>ミラーの照合で位置が同じと見なす幅。</summary>
        private const float MirrorTolerance = 1e-4f;

        /// <summary>
        /// ミラー側の形の控えが、実体側の控えの鏡映と合うかを数える（頂点単位）。
        /// 鏡映の写像は Position に使うものと同じ。Position 自体が鏡映と合わない頂点は、
        /// 写像を決められないので数えない（形とは別の不整合）。
        /// </summary>
        private static void AuditMirrors(ModelContext model, ref ShapeAuditResult r)
        {
            var list = model.MeshContextList;
            var done = new HashSet<MeshContext>();

            // 対（MirrorPair）
            for (int i = 0; i < list.Count; i++)
            {
                var real = list[i];
                if (real?.MeshObject == null) continue;
                var pair = model.GetMirrorPair(real);
                if (pair == null || !ReferenceEquals(pair.Real, real) || pair.Mirror?.MeshObject == null || !pair.IsValid) continue;
                if (!done.Add(pair.Mirror)) continue;
                r.MirrorMismatch += CountMirrorMismatch(real.MeshObject, pair.Mirror.MeshObject, pair.VertexMap, pair.MirrorPosition);
            }

            // 実体化したミラー（BakedMirrorSourceIndex）
            for (int i = 0; i < list.Count; i++)
            {
                var mirror = list[i];
                if (mirror?.MeshObject == null || done.Contains(mirror)) continue;
                int src = mirror.BakedMirrorSourceIndex;
                if (src < 0 || src >= list.Count) continue;
                var real = list[src];
                if (real?.MeshObject == null) continue;
                if (real.MeshObject.VertexCount != mirror.MeshObject.VertexCount) continue;
                int axis = real.MirrorAxis;
                float dist = real.MirrorDistance;
                r.MirrorMismatch += CountMirrorMismatch(real.MeshObject, mirror.MeshObject, null,
                    p => MirrorBranchOps.MirrorPoint(axis, dist, p));
            }
        }

        private static int CountMirrorMismatch(MeshObject real, MeshObject mirror, int[] vertexMap,
                                               System.Func<Vector3, Vector3> mirrorPoint)
        {
            int count = 0;
            int n = vertexMap != null ? System.Math.Min(vertexMap.Length, real.Vertices.Count) : real.Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                int mi = vertexMap != null ? vertexMap[i] : i;
                if (mi < 0 || mi >= mirror.Vertices.Count) continue;
                var rv = real.Vertices[i];
                var mv = mirror.Vertices[mi];
                if (rv == null || mv == null) continue;
                if (rv.Shapes == null && mv.Shapes == null) continue;
                if ((mirrorPoint(rv.Position) - mv.Position).magnitude > MirrorTolerance) continue;

                int rc = rv.Shapes?.Count ?? 0;
                int mc = mv.Shapes?.Count ?? 0;
                bool bad = rc != mc;
                for (int k = 0; !bad && k < rc; k++)
                {
                    var e = rv.Shapes[k];
                    if (!mv.TryGetShape(e.ShapeId, out var mp) ||
                        (mirrorPoint(e.Position) - mp).magnitude > MirrorTolerance) bad = true;
                }
                if (bad) count++;
            }
            return count;
        }

        /// <summary>
        /// モディファイアの出力（入力から作り直される描画オブジェクト）が持つ形の控えの数。
        /// 出力は形を持たない決まり（計画書 6 の 6）。
        /// </summary>
        private static void AuditModifierOutputs(ModelContext model, ref ShapeAuditResult r)
        {
            var seen = new HashSet<MeshContext>();
            foreach (var e in ModifierEvaluator.CollectEntries(model))
            {
                if (e?.Outputs == null) continue;
                foreach (var o in e.Outputs)
                {
                    var mo = o?.MeshObject;
                    if (mo == null || !seen.Add(o)) continue;
                    foreach (var v in mo.Vertices)
                    {
                        if (v?.Shapes != null) r.ModifierOutputEntries += v.Shapes.Count;
                        if (v?.UVShapes != null) r.ModifierOutputEntries += v.UVShapes.Count;
                    }
                }
            }
        }
    }
}
