// Runtime/Poly_Ling_Main/Tools/Deformers/TwistDeformer.cs
// ねじりデフォーマ。作業軸ローカルの +Y（= ライン方向 u）まわりに、
// 軸へ射影した距離 s に比例した角度で断面を回す。
//
// 【曲げとの違い】θ(s) の作り方は曲げと同一で、回す軸だけが違う。
//   曲げ : u に垂直な軸まわり（φ=0 ならローカル Z）→ 中心線が円弧になる
//   ねじり: u そのものまわり                        → 断面が軸まわりに回る
//
// 【式】
//   s  = pLocal.y - sOrigin        （軸へ射影した起点からの距離）
//   θ  = kDeg · s                  （kDeg = 単位長さ当たりの角度・度）
//   p' = R_y(θ) · pLocal
//
//   R_y は y 成分を変えないため、頂点は軸方向へ滑らない。
//   s = 0 の断面は動かない。
//
//   曲げと違い r = 1/k を使わないので k → 0 の特異点は無い。
//   s の範囲が取れないとき（選択が軸方向に厚みを持たないとき）だけ恒等写像になる。
//
// 【非アフィン】θ が s の関数なので単一のアフィン行列では表せない。
//   TryGetAffine は常に false。マグネット併用時は位置の線形補間になり、
//   円弧ではなく弦を通る近似になる（DeformApplier のコメント参照）。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class TwistDeformerParams : DeformerParamsBase
    {
        /// <summary>
        /// 選択範囲の全長にわたる合計ねじれ角（度）。
        /// Prepare で s の範囲を見て kDeg = TotalAngleDeg / SRange に換算する。
        /// 曲げの TotalAngleDeg と同じ意味づけ。
        /// </summary>
        public float TotalAngleDeg = 0f;

        /// <summary>
        /// ねじれの起点となる s。既定は false = 選択範囲の下端（SMin）を起点にする。
        /// true にすると作業軸の原点（s = 0）を起点にする。
        /// </summary>
        public bool PivotAtAxisOrigin = false;

        public override IDeformerParams Clone()
            => new TwistDeformerParams
            {
                TotalAngleDeg     = TotalAngleDeg,
                PivotAtAxisOrigin = PivotAtAxisOrigin
            };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<TwistDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(TotalAngleDeg, o.TotalAngleDeg)
                || PivotAtAxisOrigin != o.PivotAtAxisOrigin;
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<TwistDeformerParams>(other, out var o)) return;
            TotalAngleDeg     = o.TotalAngleDeg;
            PivotAtAxisOrigin = o.PivotAtAxisOrigin;
        }

        public override void Reset()
        {
            TotalAngleDeg     = 0f;
            PivotAtAxisOrigin = false;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class TwistDeformer : IMeshDeformer
    {
        public string Name        => "Twist";

        // Name は内部 ID（レジストリの検索キー）。表示だけを日本語にする。
        public string DisplayName => "ねじり";

        private readonly TwistDeformerParams _params = new TwistDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public TwistDeformerParams Settings => _params;

        // Prepare で確定させる値
        private float _kDeg;        // 単位長さ当たりのねじれ角（度）
        private float _sOrigin;     // ねじれの起点となる s
        private bool  _isIdentity;

        public void Prepare(DeformContext ctx)
        {
            _sOrigin = _params.PivotAtAxisOrigin ? 0f : ctx.SMin;

            if (!ctx.HasRange || Mathf.Abs(_params.TotalAngleDeg) < 1e-6f)
            {
                // 軸方向に厚みが無い、または角度ゼロ。恒等写像として扱う。
                _kDeg = 0f;
                _isIdentity = true;
                return;
            }

            _kDeg = _params.TotalAngleDeg / ctx.SRange;
            _isIdentity = false;
        }

        public Vector3 Evaluate(Vector3 pLocal)
        {
            if (_isIdentity) return pLocal;

            float s        = pLocal.y - _sOrigin;
            float thetaDeg = _kDeg * s;

            // +Y まわりの回転。y 成分は不変なので軸方向へは滑らない。
            return Quaternion.AngleAxis(thetaDeg, Vector3.up) * pLocal;
        }

        /// <summary>
        /// θ が s に依存するため単一のアフィン行列では表せない。常に false。
        /// </summary>
        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return false;
        }
    }
}
