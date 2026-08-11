// Runtime/Poly_Ling_Main/Tools/Deformers/WaveDeformer.cs
// 波デフォーマ。作業軸ローカルの +Y（= ライン方向 u）に沿った位置 s の
// サイン関数で、+X 方向と +Z 方向へ横に振る。
//
// 【式】
//   t  = (pLocal.y - sOrigin) / SRange     （選択範囲を 0..1 に正規化）
//   x' = pLocal.x + Ax · sin(2π·Cx·t + φx)
//   z' = pLocal.z + Az · sin(2π·Cz·t + φz)
//   y' = pLocal.y                          （軸方向へは動かさない）
//
//   Cx / Cz は「選択範囲の全長で何周期ぶん波打つか」。曲げ・ねじりの
//   TotalAngleDeg が「範囲の全長にわたる合計角」なのと同じ意味づけにしてある。
//   波長を絶対長で持つと対象の大きさを変えるたびに指定し直しになるため。
//
//   X と Z は独立に振れる。両方を同じ周期・位相差 90 度にすると螺旋になる。
//
// 【非アフィン】振幅が s の関数なので単一のアフィン行列では表せない。
//   TryGetAffine は常に false。
//
// 【範囲が取れないとき】選択が軸方向に厚みを持たないと t が定義できない。
//   その場合は恒等写像として扱う（曲げ・ねじりと同じ扱い）。
//
// 【位相】IDeformerPhase を実装する。ObjectArrayGenerator が複製ごとに
//   位相を進めて、同じ形の繰り返しにならないようにする。
//   PhaseDeg は φx / φz の両方へ同じだけ足す共通オフセット。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class WaveDeformerParams : DeformerParamsBase
    {
        /// <summary>+X 方向の振幅。作業軸ローカルの長さ。</summary>
        public float AmplitudeX = 0f;

        /// <summary>+X 方向の周期数。選択範囲の全長で何周ぶん波打つか。</summary>
        public float CyclesX = 1f;

        /// <summary>+X 方向の位相（度）。</summary>
        public float PhaseXDeg = 0f;

        /// <summary>+Z 方向の振幅。0 なら Z へは振らない。</summary>
        public float AmplitudeZ = 0f;

        /// <summary>+Z 方向の周期数。</summary>
        public float CyclesZ = 1f;

        /// <summary>+Z 方向の位相（度）。</summary>
        public float PhaseZDeg = 0f;

        /// <summary>
        /// 波の起点となる s。既定は false = 選択範囲の下端（SMin）を起点にする。
        /// true にすると作業軸の原点（s = 0）を起点にする。曲げ・ねじりと同じ規約。
        /// </summary>
        public bool PivotAtAxisOrigin = false;

        public override IDeformerParams Clone()
            => new WaveDeformerParams
            {
                AmplitudeX        = AmplitudeX,
                CyclesX           = CyclesX,
                PhaseXDeg         = PhaseXDeg,
                AmplitudeZ        = AmplitudeZ,
                CyclesZ           = CyclesZ,
                PhaseZDeg         = PhaseZDeg,
                PivotAtAxisOrigin = PivotAtAxisOrigin,
            };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<WaveDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(AmplitudeX, o.AmplitudeX)
                || !Mathf.Approximately(CyclesX,    o.CyclesX)
                || !Mathf.Approximately(PhaseXDeg,  o.PhaseXDeg)
                || !Mathf.Approximately(AmplitudeZ, o.AmplitudeZ)
                || !Mathf.Approximately(CyclesZ,    o.CyclesZ)
                || !Mathf.Approximately(PhaseZDeg,  o.PhaseZDeg)
                || PivotAtAxisOrigin != o.PivotAtAxisOrigin;
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<WaveDeformerParams>(other, out var o)) return;
            AmplitudeX        = o.AmplitudeX;
            CyclesX           = o.CyclesX;
            PhaseXDeg         = o.PhaseXDeg;
            AmplitudeZ        = o.AmplitudeZ;
            CyclesZ           = o.CyclesZ;
            PhaseZDeg         = o.PhaseZDeg;
            PivotAtAxisOrigin = o.PivotAtAxisOrigin;
        }

        public override void Reset()
        {
            AmplitudeX        = 0f;
            CyclesX           = 1f;
            PhaseXDeg         = 0f;
            AmplitudeZ        = 0f;
            CyclesZ           = 1f;
            PhaseZDeg         = 0f;
            PivotAtAxisOrigin = false;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class WaveDeformer : IMeshDeformer, IDeformerPhase
    {
        public string Name        => "Wave";
        public string DisplayName => "Wave";

        private readonly WaveDeformerParams _params = new WaveDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public WaveDeformerParams Settings => _params;

        /// <summary>φx / φz の両方へ足す共通位相（度）。複製ごとの差に使う。</summary>
        public float PhaseDeg { get; set; } = 0f;

        // Prepare で確定させる値
        private float _sOrigin;
        private float _invRange;     // 1 / SRange
        private float _radX, _radZ;  // 位相（ラジアン）
        private bool  _isIdentity;

        public void Prepare(DeformContext ctx)
        {
            _sOrigin = _params.PivotAtAxisOrigin ? 0f : ctx.SMin;

            bool noAmplitude = Mathf.Abs(_params.AmplitudeX) < 1e-8f
                            && Mathf.Abs(_params.AmplitudeZ) < 1e-8f;

            if (!ctx.HasRange || noAmplitude)
            {
                // 範囲が取れない、または振幅ゼロ。恒等写像として扱う。
                _invRange   = 0f;
                _isIdentity = true;
                return;
            }

            _invRange   = 1f / ctx.SRange;
            _radX       = (_params.PhaseXDeg + PhaseDeg) * Mathf.Deg2Rad;
            _radZ       = (_params.PhaseZDeg + PhaseDeg) * Mathf.Deg2Rad;
            _isIdentity = false;
        }

        public Vector3 Evaluate(Vector3 pLocal)
        {
            if (_isIdentity) return pLocal;

            float t = (pLocal.y - _sOrigin) * _invRange;

            float dx = _params.AmplitudeX
                     * Mathf.Sin(2f * Mathf.PI * _params.CyclesX * t + _radX);
            float dz = _params.AmplitudeZ
                     * Mathf.Sin(2f * Mathf.PI * _params.CyclesZ * t + _radZ);

            return new Vector3(pLocal.x + dx, pLocal.y, pLocal.z + dz);
        }

        /// <summary>
        /// 変位が s の関数なので単一のアフィン行列では表せない。常に false。
        /// </summary>
        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return false;
        }
    }
}
