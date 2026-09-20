// BillboardProfileToolHandler.Freeform.cs
// 線分群の編集：サブモード Freeform（C：パワーポイントの自由曲線に寄せた描き方）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【操作】
//   クリック           … 角の点を置く（ハンドルなし）
//   押したままドラッグ … 手描きの線。離したら画面上の許容量（SimplifyTolerancePx）で点を間引き
//                        （Ramer–Douglas–Peucker）、残した点を滑らかな点にする
//                        （入り・出とも接線、長さは弦の 1/3）
//   始点の近くでクリック／離す（3 点以上）… 閉じて確定
//   ダブルクリック・Enter・Escape・右クリック … 開いたまま確定
// 【確定】描いている間はモデルを変えず、確定のときに CreateLineGroup を 1 回送る（Undo 1 件）。
//   描いている間の点は頂点がまだ無いので、表示はローカル座標を DisplayWorldMatrix で移す。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class BillboardProfileToolHandler
    {
        /// <summary>手描きの線を間引く許容量（px）。</summary>
        [Poly_Ling.Data.PLToolParam(Description = "手描きの線を間引く許容量（px）")]
        public float SimplifyTolerancePx = 4f;
        /// <summary>ダブルクリックとみなす間隔（秒）と距離（px）。</summary>
        [Poly_Ling.Data.PLToolParam(Description = "ダブルクリックとみなす間隔（秒）")]
        public float DoubleClickSeconds = 0.35f;
        [Poly_Ling.Data.PLToolParam(Description = "ダブルクリックとみなす距離（px）")]
        public float DoubleClickPx = 6f;

        private readonly List<Vector3> _ffPoints = new List<Vector3>();   // 確定前の点（ローカル）
        private readonly List<bool>    _ffSmooth = new List<bool>();      // 滑らかな点か
        private readonly List<Vector3> _ffStroke = new List<Vector3>();   // ドラッグ中の手描き（ローカル）
        private readonly List<Vector2> _ffStrokeScreen = new List<Vector2>();
        private float   _ffLastClickTime = -10f;
        private Vector2 _ffLastClickPos;

        /// <summary>確定前の点（ローカル）と、ドラッグ中の手描き（ローカル）。表示用。</summary>
        public IReadOnlyList<Vector3> FreeformPoints => _ffPoints;
        public IReadOnlyList<Vector3> FreeformStroke => _ffStroke;

        private void FreeformClick(Vector2 screenPos, ModifierKeys mods)
        {
            if (!TryGetTarget(out _, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);

            // ダブルクリックで確定
            float now = Time.realtimeSinceStartup;
            bool dbl = now - _ffLastClickTime < DoubleClickSeconds
                       && Vector2.Distance(imgui, _ffLastClickPos) < DoubleClickPx;
            _ffLastClickTime = now;
            _ffLastClickPos  = imgui;
            if (dbl && _ffPoints.Count >= 2) { FreeformFinish(false); return; }

            if (_ffPoints.Count >= 3 && NearFirst(mc, ctx, imgui)) { FreeformFinish(true); return; }

            if (!TryScreenToLocal(mc, ctx, imgui, out var local)) { SetStatus("編集面と交わりません"); return; }
            // 直前と同じ位置の点（ダブルクリックの 1 回目など）は足さない
            if (_ffPoints.Count > 0 && (_ffPoints[_ffPoints.Count - 1] - local).sqrMagnitude < 1e-10f) return;
            _ffPoints.Add(local);
            _ffSmooth.Add(false);
            SetStatus($"点 {_ffPoints.Count} 個（Enter・ダブルクリック・Escape で確定、始点で閉じる）");
            OnChanged?.Invoke();
        }

        private void FreeformDragBegin(Vector2 screenPos)
        {
            _ffStroke.Clear();
            _ffStrokeScreen.Clear();
            if (!TryGetTarget(out _, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);
            if (!TryScreenToLocal(mc, ctx, imgui, out var local)) return;
            _ffStroke.Add(local);
            _ffStrokeScreen.Add(imgui);
            OnChanged?.Invoke();
        }

        private void FreeformDrag(Vector2 screenPos)
        {
            if (_ffStroke.Count == 0) return;
            if (!TryGetTarget(out _, out var mc, out var ctx)) return;
            var imgui = ToImgui(screenPos, ctx);
            if (Vector2.Distance(imgui, _ffStrokeScreen[_ffStrokeScreen.Count - 1]) < 2f) return;
            if (!TryScreenToLocal(mc, ctx, imgui, out var local)) return;
            _ffStroke.Add(local);
            _ffStrokeScreen.Add(imgui);
            OnChanged?.Invoke();
        }

        private void FreeformDragEnd(Vector2 screenPos)
        {
            if (_ffStroke.Count == 0) return;
            if (!TryGetTarget(out _, out var mc, out var ctx)) { _ffStroke.Clear(); return; }

            var keep = Simplify(_ffStrokeScreen, SimplifyTolerancePx);
            bool closeAtEnd = _ffPoints.Count + keep.Count >= 3
                              && NearFirstScreen(mc, ctx, _ffStrokeScreen[_ffStrokeScreen.Count - 1]);

            foreach (int k in keep)
            {
                var p = _ffStroke[k];
                if (_ffPoints.Count > 0 && (_ffPoints[_ffPoints.Count - 1] - p).sqrMagnitude < 1e-10f)
                {
                    // 前の点と同じ（手描きを前の点から始めた）なら、その点を滑らかにする
                    _ffSmooth[_ffSmooth.Count - 1] = true;
                    continue;
                }
                _ffPoints.Add(p);
                _ffSmooth.Add(true);
            }
            // 手描きの両端は角の点のままにしない（流れがつながるよう滑らか）。閉じるときは最後の点を捨てる。
            if (closeAtEnd && _ffPoints.Count >= 4) { _ffPoints.RemoveAt(_ffPoints.Count - 1); _ffSmooth.RemoveAt(_ffSmooth.Count - 1); }

            _ffStroke.Clear();
            _ffStrokeScreen.Clear();
            if (closeAtEnd) { FreeformFinish(true); return; }
            SetStatus($"点 {_ffPoints.Count} 個（Enter・ダブルクリック・Escape で確定、始点で閉じる）");
            OnChanged?.Invoke();
        }

        /// <summary>確定して CreateLineGroup を 1 回送る。2 点未満なら捨てる。</summary>
        public bool FreeformFinish(bool closed)
        {
            if (_ffPoints.Count == 0) return false;
            if (_ffPoints.Count < 2 || !TryGetTarget(out _, out var mc, out _))
            {
                FreeformReset();
                SetStatus("点が足りないので捨てました");
                OnChanged?.Invoke();
                return true;
            }

            int n = _ffPoints.Count;
            var flat  = new float[n * 3];
            var offs  = new float[n * 6];
            var cons  = new int[n * 6];
            var ratio = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                flat[i * 3] = _ffPoints[i].x; flat[i * 3 + 1] = _ffPoints[i].y; flat[i * 3 + 2] = _ffPoints[i].z;
                if (_ffSmooth[i])
                {
                    cons[i * 6]     = (int)HandleDirection.Tangent; cons[i * 6 + 1] = (int)HandleLength.ChordRatio;
                    cons[i * 6 + 3] = (int)HandleDirection.Tangent; cons[i * 6 + 4] = (int)HandleLength.ChordRatio;
                    ratio[i * 2] = 1f / 3f; ratio[i * 2 + 1] = 1f / 3f;
                }
            }
            bool anySmooth = _ffSmooth.Contains(true);
            int modelIndex = GetProject?.Invoke()?.CurrentModelIndex ?? 0;
            Dispatch?.Invoke(new CreateLineGroupCommand(
                modelIndex, _targetMaster, flat, closed && n >= 3, "",
                anySmooth ? offs : null, -1,
                anySmooth ? cons : null, anySmooth ? ratio : null));

            FreeformReset();
            SetStatus(closed ? "閉じて確定しました" : "確定しました");
            OnChanged?.Invoke();
            return true;
        }

        private void FreeformReset()
        {
            _ffPoints.Clear();
            _ffSmooth.Clear();
            _ffStroke.Clear();
            _ffStrokeScreen.Clear();
        }

        private bool NearFirst(Poly_Ling.Data.MeshContext mc, Poly_Ling.Tools.ToolContext ctx, Vector2 imgui)
            => NearFirstScreen(mc, ctx, imgui);

        /// <summary>確定前の最初の点の近く（PickRadius 以内）か。表示位置はローカルを DisplayWorldMatrix で移す。</summary>
        private bool NearFirstScreen(Poly_Ling.Data.MeshContext mc, Poly_Ling.Tools.ToolContext ctx, Vector2 imgui)
        {
            if (_ffPoints.Count == 0) return false;
            var w = mc.DisplayWorldMatrix.MultiplyPoint3x4(_ffPoints[0]);
            return Vector2.Distance(imgui, ctx.WorldToScreen(w)) < PickRadius;
        }

        /// <summary>Ramer–Douglas–Peucker。残す点の添字（両端を含む、昇順）。</summary>
        private static List<int> Simplify(List<Vector2> pts, float tol)
        {
            var keep = new List<int>();
            if (pts.Count == 0) return keep;
            if (pts.Count == 1) { keep.Add(0); return keep; }
            var mark = new bool[pts.Count];
            mark[0] = mark[pts.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, pts.Count - 1));
            while (stack.Count > 0)
            {
                var (a, b) = stack.Pop();
                float best = -1f; int bi = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = DistToSegment(pts[i], pts[a], pts[b]);
                    if (d > best) { best = d; bi = i; }
                }
                if (bi >= 0 && best > tol)
                {
                    mark[bi] = true;
                    stack.Push((a, bi));
                    stack.Push((bi, b));
                }
            }
            for (int i = 0; i < mark.Length; i++) if (mark[i]) keep.Add(i);
            return keep;
        }
    }
}
