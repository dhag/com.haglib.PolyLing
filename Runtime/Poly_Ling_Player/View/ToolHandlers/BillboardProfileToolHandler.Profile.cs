// BillboardProfileToolHandler.Profile.cs
// 線分群の編集：サブモード Profile（A：回転体／2D押し出しのキャンバスの操作を踏襲）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【操作の優先順】（Profile2D.Canvas.cs と同じ）
//   1. ハンドルをドラッグ … その点のずれを変える（拘束は残り、解き直される）
//   2. 点をドラッグ     … 選択中の点をまとめて動かす（掴んだ点が未選択なら選び直す）
//      クリック：選択（Shift 追加、Ctrl 除外）
//   3. 弦をクリック     … その位置に点を挿入
//   4. 空いた所をドラッグ … 矩形選択（Shift 追加、Ctrl 除外）。空いた所のクリックは選択解除
//   Delete / Backspace … 選択中の点を消す（2 点未満になる群は群ごと消す）
// 【確定】ドラッグ中はオーバーレイで仮の形を見せ、離したときに群ごとに SetLineGroupPoints を 1 回送る。
// 【座標】点の表示位置は GPU の値。ハンドルの表示位置は点 + DisplayWorldMatrix で移したずれ。
//   画面 → ローカルは TryScreenToLocal（DisplayWorldMatrixInverse）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class BillboardProfileToolHandler
    {
        /// <summary>選択中の点（群の番号, 群の中の点の番号）。</summary>
        private readonly HashSet<(int G, int P)> _selected = new HashSet<(int, int)>();
        public IReadOnlyCollection<(int G, int P)> SelectedPoints => _selected;

        private enum DragKind { None, Points, Handle, Marquee }
        private DragKind _drag = DragKind.None;
        private Vector2 _dragStartImgui;
        private Vector3 _dragStartLocal;
        private Vector3 _dragDeltaLocal;
        private (int G, int P, bool IsOut) _dragHandle;
        private Vector3 _dragHandleOffset;        // ドラッグ中のずれ（ローカル）
        private Rect? _marquee;                   // IMGUI 座標

        /// <summary>ドラッグ中の点の移動量（ローカル）。点ドラッグ中だけ意味を持つ。</summary>
        public Vector3? PointDragDelta => _drag == DragKind.Points ? _dragDeltaLocal : (Vector3?)null;
        /// <summary>ドラッグ中のハンドル（群, 点, 出か）とずれ。</summary>
        public (int G, int P, bool IsOut, Vector3 Offset)? HandleDrag
            => _drag == DragKind.Handle ? (_dragHandle.G, _dragHandle.P, _dragHandle.IsOut, _dragHandleOffset) : ((int, int, bool, Vector3)?)null;
        public Rect? Marquee => _marquee;

        // ================================================================
        // クリック
        // ================================================================

        private void ProfileClick(Vector2 screenPos, ModifierKeys mods)
        {
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);

            var pt = HitPoint(model, mc, ctx, imgui);
            if (pt.HasValue)
            {
                ApplySelect(pt.Value, mods);
                OnChanged?.Invoke();
                return;
            }

            var seg = HitChord(model, mc, ctx, imgui);
            if (seg.HasValue && TryScreenToLocal(mc, ctx, imgui, out var local))
            {
                InsertPoint(mc, seg.Value.G, seg.Value.P + 1, local);
                return;
            }

            if (!mods.Shift && !mods.Ctrl) { _selected.Clear(); OnChanged?.Invoke(); }
        }

        private void ApplySelect((int G, int P) key, ModifierKeys mods)
        {
            if (mods.Ctrl) { _selected.Remove(key); return; }
            if (!mods.Shift) _selected.Clear();
            _selected.Add(key);
        }

        // ================================================================
        // ドラッグ
        // ================================================================

        private void ProfileDragBegin(Vector2 screenPos, ModifierKeys mods)
        {
            _drag = DragKind.None;
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);
            _dragStartImgui = imgui;

            var hd = HitHandle(model, mc, ctx, imgui);
            if (hd.HasValue)
            {
                var g = mc.MeshObject.LineGroups[hd.Value.G];
                var h = g.PointHandles[hd.Value.P];
                _dragHandle = hd.Value;
                _dragHandleOffset = hd.Value.IsOut ? h.OutOffset : h.InOffset;
                _drag = DragKind.Handle;
                return;
            }

            var pt = HitPoint(model, mc, ctx, imgui);
            if (pt.HasValue && TryScreenToLocal(mc, ctx, imgui, out _dragStartLocal))
            {
                if (!_selected.Contains(pt.Value)) ApplySelect(pt.Value, mods);
                _dragDeltaLocal = Vector3.zero;
                _drag = DragKind.Points;
                OnChanged?.Invoke();
                return;
            }

            _drag = DragKind.Marquee;
            _marquee = new Rect(imgui, Vector2.zero);
            OnChanged?.Invoke();
        }

        private void ProfileDrag(Vector2 screenPos, ModifierKeys mods)
        {
            if (_drag == DragKind.None) return;
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);

            switch (_drag)
            {
                case DragKind.Points:
                    if (TryScreenToLocal(mc, ctx, imgui, out var cur)) _dragDeltaLocal = cur - _dragStartLocal;
                    break;
                case DragKind.Handle:
                    if (TryScreenToLocal(mc, ctx, imgui, out var hp))
                    {
                        var g = mc.MeshObject.LineGroups[_dragHandle.G];
                        var p = mc.MeshObject.Vertices[g.Order[_dragHandle.P]].Position;
                        _dragHandleOffset = hp - p;
                        _dragHandleOffset.z = 0f;
                    }
                    break;
                case DragKind.Marquee:
                    var min = Vector2.Min(_dragStartImgui, imgui);
                    var max = Vector2.Max(_dragStartImgui, imgui);
                    _marquee = new Rect(min, max - min);
                    break;
            }
            OnChanged?.Invoke();
        }

        private void ProfileDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var kind = _drag;
            _drag = DragKind.None;
            if (!TryGetTarget(out var model, out var mc, out var ctx)) { _marquee = null; return; }
            var mo = mc.MeshObject;

            switch (kind)
            {
                case DragKind.Points:
                {
                    if (_dragDeltaLocal.sqrMagnitude < 1e-12f) break;
                    var byGroup = new Dictionary<int, List<int>>();
                    foreach (var (g, p) in _selected)
                    {
                        if (!byGroup.TryGetValue(g, out var l)) byGroup[g] = l = new List<int>();
                        l.Add(p);
                    }
                    // 共有頂点を二重に動かさないよう、動かす頂点を先に集める。
                    var moveVerts = new HashSet<int>();
                    foreach (var kv in byGroup)
                        foreach (int p in kv.Value) moveVerts.Add(mo.LineGroups[kv.Key].Order[p]);

                    foreach (var kv in byGroup)
                    {
                        var g = mo.LineGroups[kv.Key];
                        var pts = new List<Vector3>(g.Order.Count);
                        foreach (int vi in g.Order)
                        {
                            var q = mo.Vertices[vi].Position;
                            pts.Add(moveVerts.Contains(vi) ? q + _dragDeltaLocal : q);
                        }
                        SendSetPoints(mc, kv.Key, pts, g.Closed, null);
                    }
                    break;
                }
                case DragKind.Handle:
                {
                    var g = mo.LineGroups[_dragHandle.G];
                    var offs = new List<LinePointHandle>(g.PointHandles.Count);
                    foreach (var h in g.PointHandles) offs.Add(h.Clone());
                    if (_dragHandle.IsOut) offs[_dragHandle.P].OutOffset = _dragHandleOffset;
                    else                   offs[_dragHandle.P].InOffset  = _dragHandleOffset;
                    var pts = new List<Vector3>(g.Order.Count);
                    foreach (int vi in g.Order) pts.Add(mo.Vertices[vi].Position);
                    SendSetPoints(mc, _dragHandle.G, pts, g.Closed, offs);
                    break;
                }
                case DragKind.Marquee:
                {
                    if (_marquee.HasValue) SelectInRect(model, mc, ctx, _marquee.Value, mods);
                    break;
                }
            }
            _marquee = null;
            OnChanged?.Invoke();
        }

        // ================================================================
        // 削除・挿入
        // ================================================================

        /// <summary>選択中の点を消す。2 点未満になる群は群ごと消す。</summary>
        [Poly_Ling.Data.PLToolAction(Description = "選択中の点を消す（Profile）")]
        public bool DeleteSelectedPoints()
        {
            if (Mode != SubMode.Profile || _selected.Count == 0) return false;
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return false;
            var mo = mc.MeshObject;

            var byGroup = new SortedDictionary<int, HashSet<int>>();
            foreach (var (g, p) in _selected)
            {
                if (!byGroup.TryGetValue(g, out var s)) byGroup[g] = s = new HashSet<int>();
                s.Add(p);
            }
            _selected.Clear();

            // 群の番号がずれないよう、後ろの群から処理する。
            var keys = new List<int>(byGroup.Keys);
            keys.Reverse();
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;
            foreach (int gi in keys)
            {
                if (mo.LineGroups == null || gi >= mo.LineGroups.Count) continue;
                var g = mo.LineGroups[gi];
                var pts = new List<Vector3>();
                var hs  = new List<LinePointHandle>();
                for (int k = 0; k < g.Order.Count; k++)
                {
                    if (byGroup[gi].Contains(k)) continue;
                    pts.Add(mo.Vertices[g.Order[k]].Position);
                    if (g.HasHandles) hs.Add(g.PointHandles[k].Clone());
                }
                if (pts.Count < 2)
                    Dispatch?.Invoke(new DeleteLineGroupCommand(modelIndex, _targetMaster, gi, true));
                else
                    SendSetPoints(mc, gi, pts, g.Closed && pts.Count >= 3, g.HasHandles ? hs : null);
            }
            SetStatus("選択した点を消しました");
            OnChanged?.Invoke();
            return true;
        }

        private void InsertPoint(MeshContext mc, int gi, int at, Vector3 local)
        {
            var mo = mc.MeshObject;
            var g = mo.LineGroups[gi];
            var pts = new List<Vector3>(g.Order.Count + 1);
            var hs  = new List<LinePointHandle>();
            for (int k = 0; k < g.Order.Count; k++)
            {
                if (k == at) { pts.Add(local); if (g.HasHandles) hs.Add(LinePointHandle.CreateDefault()); }
                pts.Add(mo.Vertices[g.Order[k]].Position);
                if (g.HasHandles) hs.Add(g.PointHandles[k].Clone());
            }
            if (at >= g.Order.Count) { pts.Add(local); if (g.HasHandles) hs.Add(LinePointHandle.CreateDefault()); }
            SendSetPoints(mc, gi, pts, g.Closed, g.HasHandles ? hs : null);
            _selected.Clear();
            _selected.Add((gi, Mathf.Min(at, pts.Count - 1)));
            SetStatus("点を挿入しました");
            OnChanged?.Invoke();
        }

        /// <summary>
        /// 選択中の点を滑らかにする（入り・出とも接線、長さは弦の 1/3）。ハンドルの無い群にはハンドルを作る。
        /// </summary>
        [Poly_Ling.Data.PLToolAction(Description = "選択中の点を滑らかにする（Profile）")]
        public bool SmoothSelectedPoints()
        {
            if (_selected.Count == 0) return false;
            if (!TryGetTarget(out _, out _, out _)) return false;
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;
            foreach (var (g, p) in _selected)
            {
                Dispatch?.Invoke(new SetLineHandleConstraintCommand(modelIndex, _targetMaster, g, p, false,
                    HandleDirection.Tangent, HandleLength.ChordRatio, 1f / 3f));
                Dispatch?.Invoke(new SetLineHandleConstraintCommand(modelIndex, _targetMaster, g, p, true,
                    HandleDirection.Tangent, HandleLength.ChordRatio, 1f / 3f));
            }
            SetStatus("選択した点を滑らかにしました");
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>選択中の点のハンドル拘束を設定する（入りか出か）。</summary>
        /// <summary>
        /// 選択中の点へハンドルの拘束を設定する（窓口の操作用。拘束を値ごとに受け取る）。
        /// 中身は SetSelectedConstraint と同じ。
        /// </summary>
        [Poly_Ling.Data.PLToolAction(Description = "選択中の点へハンドルの拘束を設定する（isOut: 出の側か、direction, length, ratio, lengthGroupId）")]
        public bool SetSelectedConstraintValues(bool isOut, HandleDirection direction, HandleLength length,
                                                float ratio, int lengthGroupId)
            => SetSelectedConstraint(isOut, new HandleConstraint
            {
                Direction     = direction,
                Length        = length,
                Ratio         = ratio,
                LengthGroupId = lengthGroupId,
            });

        /// <summary>選択中の点の数。</summary>
        [Poly_Ling.Data.PLToolState(Description = "選択中の点の数（Profile）")]
        public int SelectedPointCount => SelectedPoints.Count;

        public bool SetSelectedConstraint(bool isOut, HandleConstraint c)
        {
            if (_selected.Count == 0) return false;
            if (!TryGetTarget(out _, out _, out _)) return false;
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;
            foreach (var (g, p) in _selected)
                Dispatch?.Invoke(new SetLineHandleConstraintCommand(modelIndex, _targetMaster, g, p, isOut,
                    c.Direction, c.Length, c.Ratio, c.LengthGroupId));
            OnChanged?.Invoke();
            return true;
        }

        private void SendSetPoints(MeshContext mc, int gi, List<Vector3> pts, bool closed, List<LinePointHandle> hs)
        {
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;
            var flat = new float[pts.Count * 3];
            for (int i = 0; i < pts.Count; i++) { flat[i * 3] = pts[i].x; flat[i * 3 + 1] = pts[i].y; flat[i * 3 + 2] = pts[i].z; }
            float[] hflat = null;
            if (hs != null && hs.Count == pts.Count)
            {
                hflat = new float[hs.Count * 6];
                for (int i = 0; i < hs.Count; i++)
                {
                    hflat[i * 6]     = hs[i].InOffset.x;  hflat[i * 6 + 1] = hs[i].InOffset.y;  hflat[i * 6 + 2] = hs[i].InOffset.z;
                    hflat[i * 6 + 3] = hs[i].OutOffset.x; hflat[i * 6 + 4] = hs[i].OutOffset.y; hflat[i * 6 + 5] = hs[i].OutOffset.z;
                }
            }
            Dispatch?.Invoke(new SetLineGroupPointsCommand(modelIndex, _targetMaster, gi, flat, closed, hflat));
        }

        // ================================================================
        // 当たり判定（表示位置：点は GPU、ハンドルは点 + DisplayWorldMatrix で移したずれ）
        // ================================================================

        private bool TryGetTarget(out ModelContext model, out MeshContext mc, out ToolContext ctx)
        {
            model = GetProject?.Invoke()?.CurrentModel;
            ctx = GetToolContext?.Invoke();
            mc = null;
            if (model == null || ctx == null) return false;
            int master = model.ActiveMeshIndex;
            mc = model.GetMeshContext(master);
            if (mc?.MeshObject == null || mc.Type != MeshType.Mesh) { SetStatus("描画オブジェクトを 1 つ選んでください"); return false; }
            if (master != _targetMaster) { _selected.Clear(); ResetChain(); _targetMaster = master; }
            return true;
        }

        private Vector2? ScreenOf(ModelContext model, MeshContext mc, ToolContext ctx, int vi)
        {
            var w = GetVertexWorld?.Invoke(model, mc, vi);
            return w.HasValue ? ctx.WorldToScreen(w.Value) : (Vector2?)null;
        }

        private (int G, int P)? HitPoint(ModelContext model, MeshContext mc, ToolContext ctx, Vector2 imgui)
        {
            var mo = mc.MeshObject;
            if (mo.LineGroups == null) return null;
            float best = PickRadius;
            (int, int)? found = null;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var g = mo.LineGroups[gi];
                if (g?.Order == null) continue;
                for (int p = 0; p < g.Order.Count; p++)
                {
                    var s = ScreenOf(model, mc, ctx, g.Order[p]);
                    if (!s.HasValue) continue;
                    float d = Vector2.Distance(imgui, s.Value);
                    if (d < best) { best = d; found = (gi, p); }
                }
            }
            return found;
        }

        private (int G, int P, bool IsOut)? HitHandle(ModelContext model, MeshContext mc, ToolContext ctx, Vector2 imgui)
        {
            var mo = mc.MeshObject;
            if (mo.LineGroups == null) return null;
            Matrix4x4 dm = mc.DisplayWorldMatrix;
            float best = PickRadius;
            (int, int, bool)? found = null;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var g = mo.LineGroups[gi];
                if (g == null || !g.HasHandles) continue;
                for (int p = 0; p < g.Order.Count; p++)
                {
                    var w = GetVertexWorld?.Invoke(model, mc, g.Order[p]);
                    if (!w.HasValue) continue;
                    var h = g.PointHandles[p];
                    for (int s = 0; s < 2; s++)
                    {
                        Vector3 off = s == 0 ? h.InOffset : h.OutOffset;
                        if (off.sqrMagnitude < 1e-12f) continue;
                        float d = Vector2.Distance(imgui, ctx.WorldToScreen(w.Value + dm.MultiplyVector(off)));
                        if (d < best) { best = d; found = (gi, p, s == 1); }
                    }
                }
            }
            return found;
        }

        /// <summary>弦（隣り合う点を結ぶ画面上の線分）への当たり。(群, 区間の始点) を返す。</summary>
        private (int G, int P)? HitChord(ModelContext model, MeshContext mc, ToolContext ctx, Vector2 imgui)
        {
            var mo = mc.MeshObject;
            if (mo.LineGroups == null) return null;
            float best = PickRadius;
            (int, int)? found = null;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var g = mo.LineGroups[gi];
                if (g?.Order == null) continue;
                int n = g.Order.Count;
                int segs = g.Closed ? n : n - 1;
                for (int k = 0; k < segs; k++)
                {
                    var a = ScreenOf(model, mc, ctx, g.Order[k]);
                    var b = ScreenOf(model, mc, ctx, g.Order[(k + 1) % n]);
                    if (!a.HasValue || !b.HasValue) continue;
                    float d = DistToSegment(imgui, a.Value, b.Value);
                    if (d < best) { best = d; found = (gi, k); }
                }
            }
            return found;
        }

        private void SelectInRect(ModelContext model, MeshContext mc, ToolContext ctx, Rect r, ModifierKeys mods)
        {
            if (!mods.Shift && !mods.Ctrl) _selected.Clear();
            var mo = mc.MeshObject;
            if (mo.LineGroups == null) return;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var g = mo.LineGroups[gi];
                if (g?.Order == null) continue;
                for (int p = 0; p < g.Order.Count; p++)
                {
                    var s = ScreenOf(model, mc, ctx, g.Order[p]);
                    if (!s.HasValue || !r.Contains(s.Value)) continue;
                    if (mods.Ctrl) _selected.Remove((gi, p)); else _selected.Add((gi, p));
                }
            }
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
