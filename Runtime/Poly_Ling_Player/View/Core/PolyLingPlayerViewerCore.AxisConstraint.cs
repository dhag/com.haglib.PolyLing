// PolyLingPlayerViewerCore.AxisConstraint.cs
// Shift による縦横制限（PointerAxisConstraint）を全ビューポートに配線する。
// 計画書: 計画と規約/PolyLing_縦横制限計画.md
//
// 【配線】
//   ・1 つの PointerAxisConstraint を 4 つのビューポートパネルで共有する。
//   ・使い方と基準点は、その時点の左ボタンのハンドラ（IPointerAxisClient）に聞く。
//     ツール名では決めない。
//   ・ツール切替（SetInteractionMode）で解除する。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        private readonly PointerAxisConstraint _axisConstraint = new PointerAxisConstraint();

        /// <summary>全ビューポート共通の縦横制限（ハンドラの吸着判定から参照する）。</summary>
        public PointerAxisConstraint AxisConstraint => _axisConstraint;

        /// <summary>
        /// 現在のハンドラに縦横制限の使い方と基準点を問い合わせる。
        /// （ボーン編集から ObjectMoveToolHandler へ直接つないでいた経路は段7 で撤去したので、
        ///   問い合わせ先は現在のハンドラだけ。）
        /// </summary>
        private (AxisConstraintUse use, Vector2 anchor) QueryAxisConstraintOfCurrentHandler()
        {
            if (_vertexInteractor?.CurrentHandler is IPointerAxisClient client)
            {
                var use = client.QueryAxisConstraint(out var anchor);
                return (use, anchor);
            }
            return (AxisConstraintUse.None, Vector2.zero);
        }

        /// <summary>パネルへ縦横制限を刺す。</summary>
        private void ConnectAxisConstraint(PlayerViewportPanel panel)
        {
            if (panel == null) return;
            panel.AxisConstraint      = _axisConstraint;
            panel.QueryAxisConstraint = QueryAxisConstraintOfCurrentHandler;
        }

        /// <summary>縦横制限を解除する（ツール切替）。</summary>
        private void ResetAxisConstraint() => _axisConstraint.Reset();

        // ================================================================
        // uiViewportPointer（入力の再現）
        // ================================================================

        private CommandResult ExecuteUiViewportPointer(UiViewportPointerCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            PlayerViewportPanel panel;
            switch (cmd.View)
            {
                case Poly_Ling.Data.ViewportKind.Top:   panel = _layoutRoot?.TopPanel;   break;
                case Poly_Ling.Data.ViewportKind.Front: panel = _layoutRoot?.FrontPanel; break;
                case Poly_Ling.Data.ViewportKind.Side:  panel = _layoutRoot?.SidePanel;  break;
                default:                                panel = _layoutRoot?.PerspectivePanel; break;
            }
            if (panel == null) return CommandResult.Fail($"ビュー {cmd.View} がありません");

            var report = new System.Text.StringBuilder();
            bool shift = false;
            var steps = cmd.Steps.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < steps.Length; i++)
            {
                var tok = steps[i].Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;
                string kind = tok[0];
                var local = Vector2.zero;
                if (kind == "down" || kind == "move" || kind == "up")
                {
                    if (tok.Length < 3
                        || !float.TryParse(tok[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out local.x)
                        || !float.TryParse(tok[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out local.y))
                        return CommandResult.Fail($"段 {i}: 座標が読めません '{steps[i]}'");
                }
                else if (kind == "shiftDown") shift = true;
                else if (kind == "shiftUp")   shift = false;
                else if (kind != "escape")    return CommandResult.Fail($"段 {i}: 不明な入力 '{kind}'");

                var mods = new ModifierKeys { Shift = shift };
                Vector2 op;
                try { op = panel.SimulateInput(kind, local, mods); }
                catch (System.Exception e) { return CommandResult.Fail($"段 {i}: {e.Message}"); }

                var c = _axisConstraint;
                report.Append(System.FormattableString.Invariant(
                    $"{i}: {steps[i].Trim()} -> op=({op.x:0.0},{op.y:0.0}) use={c.Use} shift={c.Shift} lock={c.Lock} anchor=({c.Anchor.x:0.0},{c.Anchor.y:0.0}) carry=({c.Carry.x:0.0},{c.Carry.y:0.0})"));
                report.Append('\n');
            }

            return CommandResult.Ok(data: CommandDataJson.New()
                .Num ("width",  panel.resolvedStyle.width)
                .Num ("height", panel.resolvedStyle.height)
                .Text("report", report.ToString())
                .Build());
        }

        // ================================================================
        // uiCanvasPointer（独立キャンバスへの入力の再現）
        // ================================================================

        /// <summary>
        /// 独立キャンバスへ、実際の入力と同じ UI Toolkit のイベントを送る。
        /// キャンバスは CanvasPointerAxis が付けた name（NamePrefix + canvas）で探す。
        /// Mouse 系で受けるキャンバス（UV）には Mouse 系、ほかには Pointer 系のイベントを送る。
        /// </summary>
        private CommandResult ExecuteUiCanvasPointer(UiCanvasPointerCommand cmd)
        {
            if (cmd == null) return CommandResult.Fail("コマンドが null");
            if (_uiRoot == null) return CommandResult.Fail("UI がまだありません");

            var el = _uiRoot.Q<UnityEngine.UIElements.VisualElement>(CanvasPointerAxis.NamePrefix + cmd.Canvas);
            if (el == null || el.panel == null || el.worldBound.width < 1f || el.worldBound.height < 1f)
                return CommandResult.Fail($"キャンバス {cmd.Canvas} が画面に出ていません（uv / p2d / rev / belt。先にそのパネルを開く）");
            if (!CanvasPointerAxis.TryGet(el, out var axis))
                return CommandResult.Fail($"キャンバス {cmd.Canvas} に縦横制限が組み込まれていません");

            var report = new System.Text.StringBuilder();
            bool shift = false;
            bool down  = false;
            var steps = cmd.Steps.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < steps.Length; i++)
            {
                var tok = steps[i].Trim().Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length == 0) continue;
                string kind = tok[0];
                var local = Vector2.zero;
                try
                {
                    switch (kind)
                    {
                        case "down":
                        case "move":
                        case "up":
                            if (tok.Length < 3
                                || !float.TryParse(tok[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out local.x)
                                || !float.TryParse(tok[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out local.y))
                                return CommandResult.Fail($"段 {i}: 座標が読めません '{steps[i]}'");
                            var type = kind == "down" ? EventType.MouseDown
                                     : kind == "up"   ? EventType.MouseUp
                                     : down           ? EventType.MouseDrag
                                     :                  EventType.MouseMove;
                            SendCanvasPointer(el, axis.UsesMouseEvents, type, local, shift);
                            if (kind == "down") down = true;
                            if (kind == "up")   down = false;
                            break;
                        case "shiftDown": shift = true;  SendCanvasKey(el, true,  KeyCode.LeftShift, true);  break;
                        case "shiftUp":   shift = false; SendCanvasKey(el, false, KeyCode.LeftShift, false); break;
                        case "escape":    SendCanvasKey(el, true, KeyCode.Escape, shift); break;
                        default: return CommandResult.Fail($"段 {i}: 不明な入力 '{kind}'");
                    }
                }
                catch (System.Exception e) { return CommandResult.Fail($"段 {i}: {e.Message}"); }

                var c  = axis.Constraint;
                var op = c.LastOutput;
                string state = axis.Describe?.Invoke() ?? "";
                report.Append(System.FormattableString.Invariant(
                    $"{i}: {steps[i].Trim()} -> op=({op.x:0.0},{op.y:0.0}) active={axis.Active} use={c.Use} shift={c.Shift} lock={c.Lock} anchor=({c.Anchor.x:0.0},{c.Anchor.y:0.0}) carry=({c.Carry.x:0.0},{c.Carry.y:0.0}) {state}"));
                report.Append('\n');
            }

            return CommandResult.Ok(data: CommandDataJson.New()
                .Num ("width",  el.resolvedStyle.width)
                .Num ("height", el.resolvedStyle.height)
                .Text("report", report.ToString())
                .Build());
        }

        /// <summary>キャンバスローカルの位置へ左ボタンのイベントを送る。</summary>
        private static void SendCanvasPointer(UnityEngine.UIElements.VisualElement el, bool mouseEvents,
                                              EventType type, Vector2 local, bool shift)
        {
            var sys = new Event
            {
                type          = type,
                mousePosition = el.LocalToWorld(local),
                button        = 0,
                clickCount    = type == EventType.MouseDown ? 1 : 0,
                modifiers     = shift ? EventModifiers.Shift : EventModifiers.None,
            };
            UnityEngine.UIElements.EventBase evt;
            if (mouseEvents)
            {
                evt = type == EventType.MouseDown ? UnityEngine.UIElements.MouseDownEvent.GetPooled(sys)
                    : type == EventType.MouseUp   ? (UnityEngine.UIElements.EventBase)UnityEngine.UIElements.MouseUpEvent.GetPooled(sys)
                    :                               UnityEngine.UIElements.MouseMoveEvent.GetPooled(sys);
            }
            else
            {
                evt = type == EventType.MouseDown ? UnityEngine.UIElements.PointerDownEvent.GetPooled(sys)
                    : type == EventType.MouseUp   ? (UnityEngine.UIElements.EventBase)UnityEngine.UIElements.PointerUpEvent.GetPooled(sys)
                    :                               UnityEngine.UIElements.PointerMoveEvent.GetPooled(sys);
            }
            using (evt) el.SendEvent(evt);
        }

        /// <summary>キーの押下・解除を送る（UI ルートの TrickleDown を通る）。</summary>
        private static void SendCanvasKey(UnityEngine.UIElements.VisualElement el, bool keyDown, KeyCode key, bool shift)
        {
            var mods = shift ? EventModifiers.Shift : EventModifiers.None;
            UnityEngine.UIElements.EventBase evt = keyDown
                ? UnityEngine.UIElements.KeyDownEvent.GetPooled('\0', key, mods)
                : (UnityEngine.UIElements.EventBase)UnityEngine.UIElements.KeyUpEvent.GetPooled('\0', key, mods);
            using (evt) el.SendEvent(evt);
        }
    }
}
