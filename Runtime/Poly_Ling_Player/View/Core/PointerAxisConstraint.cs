// PointerAxisConstraint.cs
// Shift 押下中、操作座標を画面の横または縦にそろえる共通部品。
// Unity の入力取得・メッシュ処理には依存しない（Vector2 のみ使用）。
// 座標はすべて同じビューポート内のピクセル座標（Y=0 下）で受け渡す。
// 計画書: 計画と規約/PolyLing_縦横制限計画.md
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>縦横制限の使い方。ハンドラが現在の操作状態から返す。</summary>
    public enum AxisConstraintUse
    {
        /// <summary>制限しない。</summary>
        None,
        /// <summary>点を指定する操作。基準点は確定済みの始点。Shift を離すと実座標へ戻る。</summary>
        PointSpec,
        /// <summary>対象を動かす操作。途中で Shift を押したらその時点の操作位置が基準。離しても補正量を引き継ぐ。</summary>
        ObjectMove,
    }

    /// <summary>固定方向。</summary>
    public enum AxisLockDir { Undecided, Horizontal, Vertical }

    /// <summary>
    /// 縦横制限の状態と補正。1 つのインスタンスを全ビューポートで共有する。
    /// </summary>
    public sealed class PointerAxisConstraint
    {
        /// <summary>方向を決めるのに要る、基準点からの変位（ピクセル）。調整はここだけで行う。</summary>
        public const float DirectionThresholdPx = 4f;

        /// <summary>吸着候補が制限方向上にあるとみなす、制限線からの距離（ピクセル）。</summary>
        public const float CandidateTolerancePx = 1f;

        public AxisConstraintUse Use    { get; private set; } = AxisConstraintUse.None;
        public bool              Shift  { get; private set; }
        public AxisLockDir       Lock   { get; private set; } = AxisLockDir.Undecided;
        public Vector2           Anchor { get; private set; }

        /// <summary>対象を動かす操作で、Shift 解除後に引き継ぐ補正量（操作座標 − 実座標）。</summary>
        public Vector2 Carry { get; private set; }

        /// <summary>直前の補正後座標。</summary>
        public Vector2 LastOutput { get; private set; }
        private bool _hasLast;

        /// <summary>制限が今かかっている（方向が決まっている）か。</summary>
        public bool IsLocked => Shift && Lock != AxisLockDir.Undecided && Use != AxisConstraintUse.None;

        /// <summary>
        /// Esc で外した後、その Shift の押下が終わるまで Shift を押していない扱いにしているか。
        /// Shift を離した（shift=false で Apply した）ときに解ける。Reset では解けない
        /// （操作を取り消してもまだ Shift を押しているので）。
        /// </summary>
        public bool Suppressed { get; private set; }

        /// <summary>
        /// Esc：その Shift の押下が終わるまで制限を外す。続いている「対象を動かす」操作では
        /// Shift を離したときと同じく補正量を引き継ぐので、対象は跳ばない。
        /// </summary>
        public void SuppressUntilShiftUp() => Suppressed = true;

        /// <summary>
        /// 実座標を補正する。
        /// </summary>
        /// <param name="raw">補正前のポインター位置（Y=0 下）。</param>
        /// <param name="use">現在の操作で使える制限の種類。</param>
        /// <param name="anchor">ハンドラが示す基準点（Y=0 下）。PointSpec は始点、ObjectMove は操作開始位置。</param>
        /// <param name="shift">Shift 押下中か。</param>
        /// <param name="delta">前回の補正後座標からの差分。初回は 0。</param>
        public Vector2 Apply(Vector2 raw, AxisConstraintUse use, Vector2 anchor, bool shift, out Vector2 delta)
        {
            // Esc の抑止は Shift を離すまで。抑止中は Shift を押していない扱い。
            if (!shift) Suppressed = false;
            if (Suppressed) shift = false;

            Vector2 output;

            if (use == AxisConstraintUse.None)
            {
                // 操作が無い・制限できない状態。引き継ぎ量も捨てる。
                Use = AxisConstraintUse.None;
                Lock = AxisLockDir.Undecided;
                Carry = Vector2.zero;
                Shift = shift;
                output = raw;
            }
            else if (use == AxisConstraintUse.PointSpec)
            {
                bool changedUse = Use != AxisConstraintUse.PointSpec;
                Use = AxisConstraintUse.PointSpec;
                Carry = Vector2.zero;
                if (changedUse || anchor != Anchor) Lock = AxisLockDir.Undecided;
                Anchor = anchor;
                Shift = shift;
                if (!shift) Lock = AxisLockDir.Undecided;
                output = shift ? Constrain(raw) : raw;
            }
            else // ObjectMove
            {
                bool starting = Use != AxisConstraintUse.ObjectMove;
                if (starting)
                {
                    Use = AxisConstraintUse.ObjectMove;
                    Carry = Vector2.zero;
                    Lock = AxisLockDir.Undecided;
                    // 開始時点で Shift が押されていれば、操作開始位置を基準にする。
                    Anchor = anchor;
                    Shift = shift;
                }
                else if (shift && !Shift)
                {
                    // 途中で押した：その時点の操作位置を基準にする。
                    Anchor = _hasLast ? LastOutput : raw + Carry;
                    Lock = AxisLockDir.Undecided;
                    Shift = true;
                }
                else if (!shift && Shift)
                {
                    // 離した：補正量を引き継ぐ（対象が跳ばないように）。
                    if (_hasLast) Carry = LastOutput - raw;
                    Lock = AxisLockDir.Undecided;
                    Shift = false;
                }

                // 固定中は、引き継ぎ量を含めた操作位置を制限する。
                output = Shift ? Constrain(raw + Carry) : raw + Carry;
            }

            delta = _hasLast ? output - LastOutput : Vector2.zero;
            LastOutput = output;
            _hasLast = true;
            return output;
        }

        /// <summary>
        /// 吸着候補が制限方向を崩さないか。固定していないときは常に true。
        /// </summary>
        public bool AcceptsCandidate(Vector2 candidate)
        {
            if (!IsLocked) return true;
            float perp = Lock == AxisLockDir.Horizontal
                ? Mathf.Abs(candidate.y - Anchor.y)
                : Mathf.Abs(candidate.x - Anchor.x);
            return perp <= CandidateTolerancePx;
        }

        /// <summary>状態を解除する（取り消された操作・ツール切替・操作終了・フォーカス喪失）。Esc の抑止は残す。</summary>
        public void Reset()
        {
            Use = AxisConstraintUse.None;
            Lock = AxisLockDir.Undecided;
            Carry = Vector2.zero;
            Shift = false;
            _hasLast = false;
        }

        private Vector2 Constrain(Vector2 p)
        {
            if (Lock == AxisLockDir.Undecided)
            {
                Vector2 d = p - Anchor;
                if (Mathf.Abs(d.x) < DirectionThresholdPx && Mathf.Abs(d.y) < DirectionThresholdPx)
                    return p;   // 未決定の間は実座標のまま（クリック・追加選択を変えない）
                Lock = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? AxisLockDir.Horizontal : AxisLockDir.Vertical;
            }
            return Lock == AxisLockDir.Horizontal
                ? new Vector2(p.x, Anchor.y)
                : new Vector2(Anchor.x, p.y);
        }
    }
}
