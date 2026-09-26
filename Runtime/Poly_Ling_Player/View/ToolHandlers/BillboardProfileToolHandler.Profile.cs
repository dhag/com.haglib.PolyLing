// BillboardProfileToolHandler.Profile.cs
// 線分群の編集：サブモード Profile（A：回転体／2D押し出しのキャンバスの操作を踏襲）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【操作の優先順】（Profile2D.Canvas.cs と同じ）
//   1. ハンドルをドラッグ … その点のずれを変える（拘束は残り、解き直される）
//   2. 点をドラッグ     … 選択中の点をまとめて動かす（掴んだ点が未選択なら選び直す）
//      クリック：選択（Shift 追加、Ctrl 除外）
//   3. 弦の上で押す     … 押した瞬間に挿入待ちの点を作り、そのまま動かせる。離したとき 1 回で確定
//      （IPlayerPressHandler。動かさずに離せば押した位置に入る）
//   4. 空いた所をドラッグ … 矩形選択（Shift 追加、Ctrl 除外）。空いた所のクリックは選択解除
//   Delete / Backspace … 選択中の点を消す（2 点未満になる群は群ごと消す）
// 【確定】ドラッグ中はオーバーレイで仮の形を見せ、離したときに群ごとに SetLineGroupPoints を 1 回送る。
// 【座標】点の表示位置は GPU の値。ハンドルの表示位置は点 + DisplayWorldMatrix で移したずれ。
//   画面 → ローカルは TryScreenToLocal（DisplayWorldMatrixInverse）。
// 【候補】ポインタ移動で決め（ResolveProfileHover）、面追加と同じ描画経路で見せる。
//   点・直線の弦は GPU ホバー（頂点・線分）。ハンドルと曲線の弦（非表示の弦）は
//   重ね描きにしか無いので画面距離で調べる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class BillboardProfileToolHandler : IPlayerPressHandler
    {
        /// <summary>選択中の点（群の番号, 群の中の点の番号）。</summary>
        private readonly HashSet<(int G, int P)> _selected = new HashSet<(int, int)>();
        public IReadOnlyCollection<(int G, int P)> SelectedPoints => _selected;

        private enum DragKind { None, Points, Handle, Marquee, Insert }
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

        // ── 候補（ポインタ移動で決める。クリック・ドラッグ開始も同じ規則で決め直す）──
        public enum ProfileHoverKind { None, Point, Chord, Handle }
        private struct ProfileHover
        {
            public ProfileHoverKind Kind;
            public int G, P;           // Point：群と点 / Chord：群と区間の始点 / Handle：群と点
            public int Vertex;         // Point：頂点番号
            public bool IsOut;         // Handle：出の側か
            public Vector3 InsertLocal; // Chord：挿入位置（ローカル）
        }
        private ProfileHover _ph;

        public ProfileHoverKind ProfileHoverType => _ph.Kind;
        /// <summary>選択候補の頂点（Point のとき）。</summary>
        public int ProfileHoverVertex => _ph.Kind == ProfileHoverKind.Point ? _ph.Vertex : -1;
        /// <summary>挿入候補の区間（群, 区間の始点）と挿入位置（ローカル）。Chord のとき。</summary>
        public (int G, int P, Vector3 Local)? ProfileHoverChord
            => _ph.Kind == ProfileHoverKind.Chord ? (_ph.G, _ph.P, _ph.InsertLocal) : ((int, int, Vector3)?)null;
        /// <summary>候補のハンドル（群, 点, 出か）。Handle のとき。</summary>
        public (int G, int P, bool IsOut)? ProfileHoverHandle
            => _ph.Kind == ProfileHoverKind.Handle ? (_ph.G, _ph.P, _ph.IsOut) : ((int, int, bool)?)null;

        // ── 押した瞬間の挿入（弦の上で押す → 挿入待ちの点を作り、そのまま動かして離したら確定）──
        private bool    _piActive;         // 挿入待ちの点がある
        private int     _piG, _piAt;       // 群と挿入位置（この添字の前へ入れる）
        private Vector3 _piLocal;          // 挿入待ちの点の現在位置（ローカル）
        private Vector3 _piBase;           // 押したときの挿入位置（ローカル）
        private Vector3 _piPointerStart;   // 押したときのポインタ（編集面上のローカル）
        private bool    _pressConsumed;    // 押下で処理済み（続くクリックでは何もしない）

        /// <summary>挿入待ちの点（群, 挿入位置, ローカル座標）。無ければ null。</summary>
        public (int G, int At, Vector3 Local)? PendingInsert
            => _piActive ? (_piG, _piAt, _piLocal) : ((int, int, Vector3)?)null;

        public void OnLeftButtonDown(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _pressConsumed = false;
            _piActive = false;
            if (Mode != SubMode.Profile) return;
            if (!TryGetTarget(out _, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);
            ResolveProfileHover(imgui);
            if (_ph.Kind != ProfileHoverKind.Chord) return;
            if (!TryScreenToLocal(mc, ctx, imgui, out _piPointerStart)) return;

            _piActive = true;
            _piG = _ph.G;
            _piAt = _ph.P + 1;
            _piBase = _piLocal = _ph.InsertLocal;
            _ph = default;
            _selected.Clear();
            SetStatus("点を挿入します（そのまま動かせます）");
            OnChanged?.Invoke();
        }

        public void OnLeftPressMove(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_piActive) return;
            MovePendingInsert(screenPos);
        }

        /// <summary>動かさずに離した：押した位置で確定する（続くクリックでは挿入しない）。</summary>
        public void OnLeftPressCancel(Vector2 screenPos, ModifierKeys mods)
        {
            if (!_piActive) return;
            CommitPendingInsert();
            _pressConsumed = true;
        }

        private void MovePendingInsert(Vector2 screenPos)
        {
            if (!TryGetTarget(out _, out var mc, out var ctx)) return;
            if (!TryScreenToLocal(mc, ctx, ToImgui(screenPos, ctx), out var cur)) return;
            _piLocal = _piBase + (cur - _piPointerStart);
            _piLocal.z = 0f;
            OnChanged?.Invoke();
        }

        private void CommitPendingInsert()
        {
            if (!_piActive) return;
            _piActive = false;
            if (!TryGetTarget(out _, out var mc, out _)) return;
            var mo = mc.MeshObject;
            if (mo.LineGroups == null || _piG >= mo.LineGroups.Count) return;
            InsertPoint(mc, _piG, _piAt, _piLocal);
        }

        /// <summary>
        /// 候補を決める。優先順はクリック・ドラッグと同じ（ハンドル → 点 → 弦）。
        /// ハンドルと曲線の弦はオーバーレイにしか無いので画面距離、点と直線の弦は GPU ホバー。
        /// </summary>
        private void ResolveProfileHover(Vector2 imgui)
        {
            _ph = default;
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return;
            var mo = mc.MeshObject;
            if (mo.LineGroups == null || mo.LineGroups.Count == 0) return;

            var hd = HitHandle(model, mc, ctx, imgui);
            if (hd.HasValue)
            {
                _ph = new ProfileHover { Kind = ProfileHoverKind.Handle, G = hd.Value.G, P = hd.Value.P, IsOut = hd.Value.IsOut };
                return;
            }

            var e = GetHoverElement != null
                ? GetHoverElement(Poly_Ling.Selection.MeshSelectMode.Vertex | Poly_Ling.Selection.MeshSelectMode.Line)
                : PlayerHoverElement.None;
            bool onTarget = e.MeshIndex >= 0 && e.MeshIndex == model.ActiveMeshIndex;

            if (onTarget && e.Kind == PlayerHoverKind.Vertex && FindPoint(mo, e.VertexIndex, out int pg, out int pp))
            {
                _ph = new ProfileHover { Kind = ProfileHoverKind.Point, G = pg, P = pp, Vertex = e.VertexIndex };
                return;
            }

            // 直線の弦：2 頂点の面に当たっていれば、その区間
            if (onTarget && e.Kind == PlayerHoverKind.Line && e.FaceIndex >= 0 && e.FaceIndex < mo.FaceCount)
            {
                var f = mo.Faces[e.FaceIndex];
                if (f?.VertexIndices != null && f.VertexIndices.Count == 2
                    && FindSpan(mo, f.VertexIndices[0], f.VertexIndices[1], out int sg, out int sp)
                    && TryChordInsert(model, mc, ctx, imgui, sg, sp, out var local))
                {
                    _ph = new ProfileHover { Kind = ProfileHoverKind.Chord, G = sg, P = sp, InsertLocal = local };
                    return;
                }
            }

            // 曲線の弦（ハンドルを持つ群の弦は非表示で GPU に当たらない）：重ね描きの曲線への画面距離
            float best = PickRadius;
            int bg = -1, bp = -1;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var g = mo.LineGroups[gi];
                if (g?.Order == null || !g.HasHandles) continue;
                int segs = g.Closed ? g.Order.Count : g.Order.Count - 1;
                for (int k = 0; k < segs; k++)
                {
                    var line = ChordScreen(model, mc, ctx, gi, k);
                    if (line == null) continue;
                    for (int i = 1; i < line.Count; i++)
                    {
                        float d = DistToSegment(imgui, line[i - 1], line[i]);
                        if (d < best) { best = d; bg = gi; bp = k; }
                    }
                }
            }
            if (bg >= 0 && TryChordInsert(model, mc, ctx, imgui, bg, bp, out var cl))
                _ph = new ProfileHover { Kind = ProfileHoverKind.Chord, G = bg, P = bp, InsertLocal = cl };
        }

        /// <summary>頂点を持つ最初の (群, 点)。</summary>
        private static bool FindPoint(MeshObject mo, int vi, out int g, out int p)
        {
            g = p = -1;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var order = mo.LineGroups[gi]?.Order;
                if (order == null) continue;
                int k = order.IndexOf(vi);
                if (k >= 0) { g = gi; p = k; return true; }
            }
            return false;
        }

        /// <summary>2 頂点の組に当たる (群, 区間の始点)。ハンドルを持たない群だけ（持つ群の弦は非表示）。</summary>
        private static bool FindSpan(MeshObject mo, int a, int b, out int g, out int p)
        {
            g = p = -1;
            for (int gi = 0; gi < mo.LineGroups.Count; gi++)
            {
                var grp = mo.LineGroups[gi];
                if (grp?.Order == null || grp.HasHandles) continue;
                int n = grp.Order.Count;
                int segs = grp.Closed ? n : n - 1;
                for (int k = 0; k < segs; k++)
                {
                    int u = grp.Order[k], v = grp.Order[(k + 1) % n];
                    if ((u == a && v == b) || (u == b && v == a)) { g = gi; p = k; return true; }
                }
            }
            return false;
        }

        /// <summary>
        /// 区間の画面上の折れ線（Y=0 上）。点は GPU の値、ハンドルは DisplayWorldMatrix で移したずれ。
        /// 曲線は重ね描き（UpdateLineCurveOverlay）と同じ分割数。
        /// </summary>
        public List<Vector2> ChordScreen(ModelContext model, MeshContext mc, ToolContext ctx, int gi, int k)
        {
            var mo = mc?.MeshObject;
            if (mo?.LineGroups == null || gi < 0 || gi >= mo.LineGroups.Count) return null;
            var g = mo.LineGroups[gi];
            int n = g.Order.Count;
            if (k < 0 || k >= n) return null;
            int kb = (k + 1) % n;
            var wa = GetVertexWorld?.Invoke(model, mc, g.Order[k]);
            var wb = GetVertexWorld?.Invoke(model, mc, g.Order[kb]);
            if (!wa.HasValue || !wb.HasValue) return null;

            var result = new List<Vector2>();
            Vector3 outA = Vector3.zero, inB = Vector3.zero;
            if (g.HasHandles)
            {
                Matrix4x4 dm = mc.DisplayWorldMatrix;
                outA = dm.MultiplyVector(g.PointHandles[k]?.OutOffset ?? Vector3.zero);
                inB  = dm.MultiplyVector(g.PointHandles[kb]?.InOffset ?? Vector3.zero);
            }
            bool straight = outA.sqrMagnitude < 1e-12f && inB.sqrMagnitude < 1e-12f;
            int steps = straight ? 1 : Poly_Ling.Ops.LineCurveSampler.DefaultSegmentsPerSpan * 2;
            for (int s = 0; s <= steps; s++)
                result.Add(ctx.WorldToScreen(Poly_Ling.Ops.LineCurveSampler.Bezier(
                    wa.Value, wa.Value + outA, wb.Value + inB, wb.Value, (float)s / steps)));
            return result;
        }

        /// <summary>区間の画面上の折れ線でポインタに最も近い所を、編集面のローカル座標にする。</summary>
        private bool TryChordInsert(ModelContext model, MeshContext mc, ToolContext ctx, Vector2 imgui,
                                    int gi, int k, out Vector3 local)
        {
            local = Vector3.zero;
            var line = ChordScreen(model, mc, ctx, gi, k);
            if (line == null || line.Count < 2) return false;
            float best = float.MaxValue;
            Vector2 foot = line[0];
            for (int i = 1; i < line.Count; i++)
            {
                Vector2 q = ClosestOnSegment(imgui, line[i - 1], line[i]);
                float d = Vector2.Distance(imgui, q);
                if (d < best) { best = d; foot = q; }
            }
            return TryScreenToLocal(mc, ctx, foot, out local);
        }

        private static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        // ================================================================
        // クリック
        // ================================================================

        private void ProfileClick(Vector2 screenPos, ModifierKeys mods)
        {
            if (_pressConsumed) { _pressConsumed = false; return; }   // 押下で挿入済み
            if (!TryGetTarget(out var model, out var mc, out var ctx)) return;
            ResolveProfileHover(ToImgui(screenPos, ctx));

            switch (_ph.Kind)
            {
                case ProfileHoverKind.Point:
                    ApplySelect((_ph.G, _ph.P), mods);
                    OnChanged?.Invoke();
                    return;
                case ProfileHoverKind.Chord:
                    InsertPoint(mc, _ph.G, _ph.P + 1, _ph.InsertLocal);
                    _ph = default;
                    return;
                case ProfileHoverKind.Handle:
                    return;   // ハンドルはドラッグで動かす。クリックでは何もしない。
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

            // 弦の上で押していれば、挿入待ちの点をそのまま動かす。
            if (_piActive) { _drag = DragKind.Insert; return; }

            ResolveProfileHover(imgui);

            if (_ph.Kind == ProfileHoverKind.Handle)
            {
                var g = mc.MeshObject.LineGroups[_ph.G];
                var h = g.PointHandles[_ph.P];
                _dragHandle = (_ph.G, _ph.P, _ph.IsOut);
                _dragHandleOffset = _ph.IsOut ? h.OutOffset : h.InOffset;
                _drag = DragKind.Handle;
                _ph = default;
                return;
            }

            if (_ph.Kind == ProfileHoverKind.Point && TryScreenToLocal(mc, ctx, imgui, out _dragStartLocal))
            {
                var key = (_ph.G, _ph.P);
                if (!_selected.Contains(key)) ApplySelect(key, mods);
                _dragDeltaLocal = Vector3.zero;
                _drag = DragKind.Points;
                _ph = default;
                OnChanged?.Invoke();
                return;
            }

            _ph = default;
            _drag = DragKind.Marquee;
            _marquee = new Rect(imgui, Vector2.zero);
            OnChanged?.Invoke();
        }

        private void ProfileDrag(Vector2 screenPos, ModifierKeys mods)
        {
            if (_drag == DragKind.None) return;
            if (_drag == DragKind.Insert) { MovePendingInsert(screenPos); return; }
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
            if (kind == DragKind.Insert)
            {
                MovePendingInsert(screenPos);
                CommitPendingInsert();
                _marquee = null;
                OnChanged?.Invoke();
                return;
            }
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

        /// <summary>ハンドルへの当たり。ハンドルは重ね描きにしか無いので画面距離で調べる。</summary>
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
