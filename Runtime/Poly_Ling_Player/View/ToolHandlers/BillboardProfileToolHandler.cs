// BillboardProfileToolHandler.cs
// ビルボード上の 2D プロファイル編集（線分群）のツールハンドラ。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【対象】選択中の描画オブジェクト 1 つ（ModelContext.ActiveMeshContext）。
// 【編集面】対象のローカル XY 平面（Z = 0）。ビルボードならカメラに正対する。
//   画面の点 → ローカル座標は、光線を DisplayWorldMatrixInverse でローカルへ移して
//   Z = 0 と交わらせる（書き戻し方向なので CPU の行列を使う）。
// 【表示位置】既存の点は GPU の値（GetVertexWorld）。当たり判定もこれで行う。
// 【確定】モデルは線分群コマンドでだけ変える（CreateLineGroup / SetLineGroupPoints）。
//
// 【サブモード】
//   Line（B：面の追加の線分モードを踏襲）…実装済み
//     クリックで点を置き、線分群を伸ばす。始点をクリックすると閉じる。
//     Escape / 右クリック（FinishChain）で描画を終える。
//     既存の群の頂点から描き始めたとき：既定は新しい群を作り、その頂点を始点として共有して親にする。
//     ExtendExisting が ON で、その頂点が開いた群の終点なら、その群を伸ばす。
//   Profile（A：回転体／2D押し出しの操作を踏襲）… BillboardProfileToolHandler.Profile.cs
//   Freeform（C：パワーポイント風の自由曲線）… BillboardProfileToolHandler.Freeform.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    [Poly_Ling.Data.PLTool("billboardProfile", Description = "線分群の編集（BillboardProfileToolHandler）")]
    public partial class BillboardProfileToolHandler : IPlayerToolHandler
    {
        public enum SubMode { Line = 0, Profile = 1, Freeform = 2 }

        // ── 結線 ──
        public Func<ProjectContext> GetProject;
        public Func<ToolContext>    GetToolContext;
        /// <summary>頂点の GPU ワールド座標（CPU で計算し直さない）。</summary>
        public Func<ModelContext, MeshContext, int, Vector3?> GetVertexWorld;
        /// <summary>コマンドを送る。</summary>
        public Action<PanelCommand> Dispatch;
        /// <summary>状態が変わった（パネル・オーバーレイの更新）。</summary>
        public Action OnChanged;

        // ── 設定 ──
        [Poly_Ling.Data.PLToolParam(Description = "BillboardProfileToolHandler.Mode")]
        public SubMode Mode = SubMode.Line;
        /// <summary>開いた群の終点から描き始めたとき、その群を伸ばすか（既定 OFF = 新しい群）。</summary>
        [Poly_Ling.Data.PLToolParam(Description = "開いた群の終点から描き始めたとき、その群を伸ばすか（既定 OFF = 新しい群）")]
        public bool ExtendExisting;
        /// <summary>既存の点に吸着する半径（px）。</summary>
        [Poly_Ling.Data.PLToolParam(Description = "既存の点に吸着する半径（px）")]
        public float PickRadius = 10f;

        // ── 描画中の折れ線 ──
        private int _targetMaster = -1;
        private int _chainGroup   = -1;         // 描いている群（まだ無ければ -1）
        private readonly List<Vector3> _chainPoints = new List<Vector3>(); // ローカル座標
        private int _pendingStartVertex = -1;   // 始点に使う既存頂点（群を作る前）
        private Vector2? _hoverScreen;          // IMGUI 座標

        [Poly_Ling.Data.PLToolState(Description = "BillboardProfileToolHandler.Status")]
        public string Status { get; private set; } = "";

        /// <summary>描画中の点（ローカル座標）。</summary>
        public IReadOnlyList<Vector3> ChainPoints => _chainPoints;
        [Poly_Ling.Data.PLToolState(Description = "描画中の点の数")]
        public int ChainPointCount => _chainPoints.Count;
        [Poly_Ling.Data.PLToolState(Description = "描画中の対象オブジェクト")]
        public int TargetMasterIndex => _targetMaster;
        /// <summary>描いている群の番号（まだ作っていなければ -1）。</summary>
        [Poly_Ling.Data.PLToolState(Description = "描いている群の番号（まだ作っていなければ -1）")]
        public int ChainGroupIndex => _chainGroup;
        public Vector2? HoverScreen => _hoverScreen;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (Mode == SubMode.Profile) { ProfileClick(screenPos, mods); return; }
            if (Mode == SubMode.Freeform) { FreeformClick(screenPos, mods); return; }
            if (Mode != SubMode.Line) return;
            var ctx = GetToolContext?.Invoke();
            var model = GetProject?.Invoke()?.CurrentModel;
            if (ctx == null || model == null) return;

            var imgui = ToImgui(screenPos, ctx);
            int master = model.ActiveMeshIndex;
            var mc = model.GetMeshContext(master);
            if (mc?.MeshObject == null || mc.Type != MeshType.Mesh) { SetStatus("描画オブジェクトを 1 つ選んでください"); return; }

            // 対象が変わったら描き直し
            if (master != _targetMaster) { ResetChain(); _targetMaster = master; }

            // 既存の点への吸着（GPU の表示位置で判定）
            int snapVertex = PickVertex(model, mc, imgui, ctx);
            Vector3 local;
            if (snapVertex >= 0) local = mc.MeshObject.Vertices[snapVertex].Position;
            else if (!TryScreenToLocal(mc, ctx, imgui, out local)) { SetStatus("編集面と交わりません"); return; }

            if (_chainPoints.Count == 0)
            {
                StartChain(mc, snapVertex, local);
                return;
            }

            // 始点をクリック → 閉じる（3 点以上）
            if (_chainPoints.Count >= 3 && IsFirstPoint(mc, snapVertex, imgui, ctx))
            {
                Commit(closed: true);
                ResetChain();
                SetStatus("閉じました");
                return;
            }

            _chainPoints.Add(local);
            Commit(closed: false);
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (Mode == SubMode.Profile) ProfileDragBegin(screenPos, mods);
            else if (Mode == SubMode.Freeform) FreeformDragBegin(screenPos);
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (Mode == SubMode.Profile) ProfileDrag(screenPos, mods);
            else if (Mode == SubMode.Freeform) FreeformDrag(screenPos);
        }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (Mode == SubMode.Profile) ProfileDragEnd(screenPos, mods);
            else if (Mode == SubMode.Freeform) FreeformDragEnd(screenPos);
        }

        /// <summary>ポインタ移動。仮の線分の表示用に位置を覚える。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _hoverScreen = ToImgui(screenPos, ctx);
            if (_chainPoints.Count > 0 || _ffPoints.Count > 0) OnChanged?.Invoke();
        }

        /// <summary>描画を終える（Escape / 右クリック）。描いた分は確定済み。自由曲線はここで確定する。</summary>
        [Poly_Ling.Data.PLToolAction(Description = "描いている線分群を終える（Escape と同じ）")]
        public bool FinishChain()
        {
            if (_ffPoints.Count > 0) return FreeformFinish(false);
            if (_chainPoints.Count == 0) return false;
            bool had = _chainGroup >= 0;
            ResetChain();
            SetStatus(had ? "描画を終えました" : "始点を取り消しました");
            return true;
        }

        // ================================================================
        // 描画中の処理
        // ================================================================

        private void StartChain(MeshContext mc, int snapVertex, Vector3 local)
        {
            var mo = mc.MeshObject;
            _chainPoints.Clear();
            _chainGroup = -1;
            _pendingStartVertex = -1;

            // 開いた群の終点から始め、伸ばす指定なら、その群の続きとして描く。
            if (snapVertex >= 0 && ExtendExisting && mo.LineGroups != null)
            {
                for (int gi = 0; gi < mo.LineGroups.Count; gi++)
                {
                    var g = mo.LineGroups[gi];
                    if (g?.Order == null || g.Closed || g.Order.Count < 2 || g.EndVertex != snapVertex) continue;
                    _chainGroup = gi;
                    foreach (int vi in g.Order) _chainPoints.Add(mo.Vertices[vi].Position);
                    SetStatus($"線分群 {g.Name} を伸ばします");
                    OnChanged?.Invoke();
                    return;
                }
            }

            _pendingStartVertex = snapVertex;
            _chainPoints.Add(local);
            SetStatus("始点を置きました");
            OnChanged?.Invoke();
        }

        private void Commit(bool closed)
        {
            var model = GetProject?.Invoke()?.CurrentModel;
            var mc = model?.GetMeshContext(_targetMaster);
            if (mc?.MeshObject == null) return;
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;

            var flat = new float[_chainPoints.Count * 3];
            for (int i = 0; i < _chainPoints.Count; i++)
            { flat[i * 3] = _chainPoints[i].x; flat[i * 3 + 1] = _chainPoints[i].y; flat[i * 3 + 2] = _chainPoints[i].z; }

            if (_chainGroup < 0)
            {
                int before = mc.MeshObject.LineGroups?.Count ?? 0;
                Dispatch?.Invoke(new CreateLineGroupCommand(
                    modelIndex, _targetMaster, flat, closed, "", null, _pendingStartVertex));
                int after = mc.MeshObject.LineGroups?.Count ?? 0;
                if (after == before + 1) _chainGroup = after - 1;
                else { SetStatus("線分群を作れませんでした"); ResetChain(); return; }
            }
            else
            {
                Dispatch?.Invoke(new SetLineGroupPointsCommand(
                    modelIndex, _targetMaster, _chainGroup, flat, closed));
            }
            SetStatus($"点 {_chainPoints.Count} 個");
            OnChanged?.Invoke();
        }

        private void ResetChain()
        {
            _chainPoints.Clear();
            _chainGroup = -1;
            _pendingStartVertex = -1;
            OnChanged?.Invoke();
        }

        // ================================================================
        // 座標と当たり判定
        // ================================================================

        /// <summary>画面（IMGUI）→ 対象のローカル XY 平面（Z = 0）。</summary>
        public static bool TryScreenToLocal(MeshContext mc, ToolContext ctx, Vector2 imgui, out Vector3 local)
        {
            local = Vector3.zero;
            if (ctx?.ScreenPosToRay == null) return false;
            Ray ray = ctx.ScreenPosToRay(imgui);
            Matrix4x4 inv = mc.DisplayWorldMatrixInverse;
            Vector3 o = inv.MultiplyPoint3x4(ray.origin);
            Vector3 d = inv.MultiplyVector(ray.direction);
            if (Mathf.Abs(d.z) < 1e-8f) return false;
            float t = -o.z / d.z;
            if (t < 0f) return false;
            local = o + d * t;
            local.z = 0f;
            return true;
        }

        /// <summary>線分群の点のうち、画面で最も近いもの（PickRadius 以内）。無ければ -1。</summary>
        private int PickVertex(ModelContext model, MeshContext mc, Vector2 imgui, ToolContext ctx)
        {
            var mo = mc.MeshObject;
            if (mo?.LineGroups == null || GetVertexWorld == null) return -1;
            float best = PickRadius;
            int found = -1;
            var seen = new HashSet<int>();
            foreach (var g in mo.LineGroups)
            {
                if (g?.Order == null) continue;
                foreach (int vi in g.Order)
                {
                    if (!seen.Add(vi)) continue;
                    var w = GetVertexWorld(model, mc, vi);
                    if (!w.HasValue) continue;
                    float d = Vector2.Distance(imgui, ctx.WorldToScreen(w.Value));
                    if (d < best) { best = d; found = vi; }
                }
            }
            return found;
        }

        /// <summary>描いている折れ線の最初の点をクリックしたか。</summary>
        private bool IsFirstPoint(MeshContext mc, int snapVertex, Vector2 imgui, ToolContext ctx)
        {
            var mo = mc.MeshObject;
            if (_chainGroup >= 0 && mo.LineGroups != null && _chainGroup < mo.LineGroups.Count)
                return snapVertex >= 0 && snapVertex == mo.LineGroups[_chainGroup].StartVertex;
            return false;
        }

        private static Vector2 ToImgui(Vector2 screenPos, ToolContext ctx)
            => new Vector2(screenPos.x, ctx.PreviewRect.height - screenPos.y);

        private void SetStatus(string s) { Status = s; }
    }
}
