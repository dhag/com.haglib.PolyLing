// CanvasPointerAxis.cs
// 独立キャンバス（UV・2D押し出し・回転体・ベルト断面）に、ビューポートと同じ縦横制限
// （PointerAxisConstraint）を組み込む部品。縦横の判定は PointerAxisConstraint だけが行う。
// 計画書: 計画と規約/PolyLing_縦横制限計画.md
// Runtime/Poly_Ling_Player/View/Common/ に配置
//
// 【使い方】キャンバス側は次の 4 か所だけを書く。
//   ・点を動かし始めたとき Begin(基準点)。基準点はドラッグを始めたキャンバスローカル座標。
//   ・点を動かすときの座標を Apply(実座標, Shift) に通す。
//   ・離したとき End()。
//   ・replay に「操作座標で点の移動をやり直す」処理を渡す（Shift・Esc だけが変わったとき呼ばれる）。
// パン・アンカー・回転と拡大縮小のハンドル・範囲選択では Begin を呼ばない（制限しない）。
//
// 【キー】Shift と Esc は UI ルートで先取りして受ける（フォーカスがキャンバスに無くても届く）。
//   反応するのは最後に Apply を呼んだキャンバスで、点を動かしている間だけ。
//   Esc はその Shift の押下が終わるまで制限を外す（PointerAxisConstraint.SuppressUntilShiftUp。
//   ビューポートで Esc で終わらない操作と同じ。キャンバスには取り消しが無いので操作は続き、
//   補正量は引き継ぐので点は跳ばない）。
// 【解除】End、キャプチャの喪失。
// 【確認】キャンバスの name を NamePrefix + id にし、uiCanvasPointer が名前で探す。

using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public sealed class CanvasPointerAxis
    {
        /// <summary>キャンバスの name の接頭辞。uiCanvasPointer の canvas 引数は接頭辞を除いた部分。</summary>
        public const string NamePrefix = "polylingCanvas.";

        private static readonly ConditionalWeakTable<VisualElement, CanvasPointerAxis> s_byElement
            = new ConditionalWeakTable<VisualElement, CanvasPointerAxis>();

        /// <summary>Shift・Esc に反応するキャンバス。最後に Apply を呼んだもの。</summary>
        private static CanvasPointerAxis s_keyTarget;

        private readonly VisualElement   _canvas;
        private readonly Action<Vector2> _replay;

        /// <summary>このキャンバスの縦横制限。</summary>
        public PointerAxisConstraint Constraint { get; } = new PointerAxisConstraint();

        /// <summary>キャンバスの種類（uiCanvasPointer の canvas 引数）。</summary>
        public string Id { get; }

        /// <summary>UV キャンバスのように Mouse 系イベントで入力を受けるか（再現入力の種類を決める）。</summary>
        public bool UsesMouseEvents { get; }

        /// <summary>再現入力の報告に添える、キャンバスの状態の説明（選択点の位置など）。無ければ null。</summary>
        public Func<string> Describe;

        private AxisConstraintUse _use = AxisConstraintUse.None;
        private Vector2 _anchor;
        private Vector2 _lastRaw;
        private bool    _shift;

        /// <summary>点を動かしている間 true（Begin から End まで）。</summary>
        public bool Active => _use != AxisConstraintUse.None;

        /// <param name="canvas">入力を受けるキャンバス。name を NamePrefix + id にする。</param>
        /// <param name="id">キャンバスの種類（uv / p2d / rev / belt）。</param>
        /// <param name="replay">操作座標（キャンバスローカル）で点の移動をやり直す処理。</param>
        /// <param name="usesMouseEvents">Mouse 系イベントで入力を受けるキャンバスなら true。</param>
        public CanvasPointerAxis(VisualElement canvas, string id, Action<Vector2> replay, bool usesMouseEvents = false)
        {
            _canvas         = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _replay         = replay;
            Id              = id ?? "";
            UsesMouseEvents = usesMouseEvents;

            canvas.name = NamePrefix + Id;
            s_byElement.Remove(canvas);
            s_byElement.Add(canvas, this);

            canvas.RegisterCallback<AttachToPanelEvent>(e =>
            {
                e.destinationPanel.visualTree.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
                e.destinationPanel.visualTree.RegisterCallback<KeyUpEvent>(OnRootKeyUp, TrickleDown.TrickleDown);
            });
            canvas.RegisterCallback<DetachFromPanelEvent>(e =>
            {
                e.originPanel.visualTree.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
                e.originPanel.visualTree.UnregisterCallback<KeyUpEvent>(OnRootKeyUp, TrickleDown.TrickleDown);
                End();
                if (s_keyTarget == this) s_keyTarget = null;
            });
            // キャプチャの喪失：制限を解除する（キャンバスのドラッグ自体の扱いは各キャンバスのまま）。
            canvas.RegisterCallback<PointerCaptureOutEvent>(_ => End());
            canvas.RegisterCallback<MouseCaptureOutEvent>(_ => End());
        }

        /// <summary>要素に組み込んだ部品を返す（uiCanvasPointer 用）。</summary>
        public static bool TryGet(VisualElement canvas, out CanvasPointerAxis axis)
        {
            axis = null;
            return canvas != null && s_byElement.TryGetValue(canvas, out axis);
        }

        /// <summary>点を動かし始める。基準点はドラッグを始めたキャンバスローカル座標。</summary>
        public void Begin(Vector2 anchor, bool shift)
        {
            Constraint.Reset();
            _use         = AxisConstraintUse.ObjectMove;
            _anchor      = anchor;
            _lastRaw     = anchor;
            _shift       = shift;
            s_keyTarget  = this;
        }

        /// <summary>
        /// 実座標（キャンバスローカル）を操作座標へ補正する。動かしていない間は実座標のまま。
        /// Esc の抑止（Shift を離すまで外す）は PointerAxisConstraint が持つ（ビューポートと同じ）。
        /// </summary>
        public Vector2 Apply(Vector2 raw, bool shift)
        {
            s_keyTarget = this;
            _lastRaw = raw;
            _shift   = shift;
            return Constraint.Apply(raw, _use, _anchor, shift, out _);
        }

        /// <summary>点を動かし終えた（離した・キャプチャを失った）。</summary>
        public void End()
        {
            _use = AxisConstraintUse.None;
            Constraint.Reset();
        }

        // ================================================================
        // キー（UI ルートで先取り）
        // ================================================================

        private void OnRootKeyDown(KeyDownEvent evt)
        {
            if (s_keyTarget != this || !Active) return;
            if (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift)
            {
                if (_shift) return;   // キーリピート
                Replay(true);
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                // キャンバスには取り消しが無いので操作は続く。ビューポートの続く操作と同じく、
                // その Shift の押下が終わるまで制限を外し、補正量を引き継ぐ。
                if (Constraint.Suppressed) return;
                Constraint.SuppressUntilShiftUp();
                Replay(_shift);
                evt.StopPropagation();
            }
        }

        private void OnRootKeyUp(KeyUpEvent evt)
        {
            if (s_keyTarget != this || !Active) return;
            if (evt.keyCode == KeyCode.LeftShift || evt.keyCode == KeyCode.RightShift)
            {
                if (!_shift) return;
                Replay(false);
            }
        }

        /// <summary>最後の実座標で補正し直し、点の移動をやり直す（Shift・Esc だけが変わったとき）。</summary>
        private void Replay(bool shift)
        {
            var op = Apply(_lastRaw, shift);
            _replay?.Invoke(op);
            _canvas.MarkDirtyRepaint();
        }
    }
}
