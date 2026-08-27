// Runtime/Poly_Ling_Main/Tools/Deformers/BendDeformer.cs
// 曲げデフォーマ。作業軸ローカルの +Y 方向に伸びた形状を円弧状に曲げる。
//
// 【式】+Y をライン方向 u、+X をたわみ方向とする。
//   s = pLocal.y          （ライン方向の位置）
//   d = pLocal.x          （中心線からのたわみ方向オフセット）
//   θ = k · s             （k = 単位長さ当たりの曲げ角。r = 1/k）
//
//   y' = (r - d) · sin θ
//   x' = r - (r - d) · cos θ
//   z' = pLocal.z
//
//   k → 0 のとき y' → s、x' → d + k·s²/2 となり恒等写像へ連続に収束する。
//   ゼロ割りを避けるため |k| が微小なときは下の一次近似へ切り替える。
//
// 【たわみ方向】BendPlaneAngleDeg = φ で、Y まわりに ∓φ の回転を前後に挟む。
//   R_y(-φ) → 上式 → R_y(+φ)。φ=0 で +X 方向へ曲がる。
//
// 【R が一定でないこと】θ が s の関数なので R = R(θ(s)) は頂点ごとに変わる。
//   これが通常の回転との本質的な違いで、TryGetAffine は常に false を返す。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class BendDeformerParams : DeformerParamsBase
    {
        /// <summary>
        /// 選択範囲の全長にわたる合計曲げ角（度）。
        /// Prepare で s の範囲を見て k = rad / SRange に換算する。
        /// メタセコイア Ver4.7 以降の数値指定と同じ意味。
        /// </summary>
        public float TotalAngleDeg = 0f;

        /// <summary>たわみ方向。Y まわりの角（度）。0 で +X 方向へ曲がる。</summary>
        public float BendPlaneAngleDeg = 0f;

        /// <summary>
        /// たわみ方向をカメラの奥行軸から自動で決めるか。既定は true。
        ///
        /// true のとき、曲げの回転軸が視線と一致するよう BendPlaneAngleDeg を
        /// 毎回計算して書き込む。画面上で見たとおりに曲がるので狙いが付けやすい。
        /// 計算はカメラを知っている呼び出し側（DeformToolHandler）が行う。
        /// デフォーマ自身はカメラを参照しない。
        ///
        /// false のときは BendPlaneAngleDeg を手で指定する従来の挙動になる。
        /// </summary>
        public bool UseCameraBendPlane = true;

        /// <summary>
        /// 曲げの起点となる s。既定は false = 選択範囲の下端（SMin）を起点にする。
        /// true にすると作業軸の原点（s = 0）を起点にする。
        /// </summary>
        public bool PivotAtAxisOrigin = false;

        public override IDeformerParams Clone()
            => new BendDeformerParams
            {
                TotalAngleDeg      = TotalAngleDeg,
                BendPlaneAngleDeg  = BendPlaneAngleDeg,
                UseCameraBendPlane = UseCameraBendPlane,
                PivotAtAxisOrigin  = PivotAtAxisOrigin
            };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<BendDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(TotalAngleDeg, o.TotalAngleDeg)
                || !Mathf.Approximately(BendPlaneAngleDeg, o.BendPlaneAngleDeg)
                || UseCameraBendPlane != o.UseCameraBendPlane
                || PivotAtAxisOrigin != o.PivotAtAxisOrigin;
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<BendDeformerParams>(other, out var o)) return;
            TotalAngleDeg      = o.TotalAngleDeg;
            BendPlaneAngleDeg  = o.BendPlaneAngleDeg;
            UseCameraBendPlane = o.UseCameraBendPlane;
            PivotAtAxisOrigin  = o.PivotAtAxisOrigin;
        }

        public override void Reset()
        {
            TotalAngleDeg      = 0f;
            BendPlaneAngleDeg  = 0f;
            UseCameraBendPlane = true;
            PivotAtAxisOrigin  = false;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class BendDeformer : IMeshDeformer
    {
        public string Name        => "Bend";

        // Name は内部 ID（レジストリの検索キー）。表示だけを日本語にする。
        public string DisplayName => "曲げ";

        private readonly BendDeformerParams _params = new BendDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public BendDeformerParams Settings => _params;

        // Prepare で確定させる値
        private float      _k;              // 単位長さ当たりの曲げ角（ラジアン）
        private float      _sOrigin;        // 曲げの起点となる s
        private Quaternion _toBendPlane;    // R_y(-φ)
        private Quaternion _fromBendPlane;  // R_y(+φ)
        private bool       _isIdentity;     // k がほぼ 0 か範囲が無効

        /// <summary>|k| がこれ以下なら円弧式を使わず一次近似へ切り替える。</summary>
        private const float MinCurvature = 1e-5f;

        public void Prepare(DeformContext ctx)
        {
            float phi = _params.BendPlaneAngleDeg;
            _toBendPlane   = Quaternion.Euler(0f, -phi, 0f);
            _fromBendPlane = Quaternion.Euler(0f,  phi, 0f);

            _sOrigin = _params.PivotAtAxisOrigin ? 0f : ctx.SMin;

            float totalRad = _params.TotalAngleDeg * Mathf.Deg2Rad;

            if (!ctx.HasRange || Mathf.Abs(totalRad) < 1e-8f)
            {
                // 範囲が取れない、または角度ゼロ。恒等写像として扱う。
                _k = 0f;
                _isIdentity = true;
                return;
            }

            _k = totalRad / ctx.SRange;
            _isIdentity = Mathf.Abs(_k) < MinCurvature;
        }

        public Vector3 Evaluate(Vector3 pLocal)
        {
            if (_isIdentity) return pLocal;

            // たわみ平面へ回す（以後 +X がたわみ方向）
            Vector3 p = _toBendPlane * pLocal;

            float s = p.y - _sOrigin;
            float d = p.x;
            float z = p.z;

            float theta = _k * s;
            float r     = 1f / _k;

            // y' = (r - d) sinθ,  x' = r - (r - d) cosθ
            float rd = r - d;
            float y2 = rd * Mathf.Sin(theta);
            float x2 = r - rd * Mathf.Cos(theta);

            // 起点を戻す。s は _sOrigin だけずらしてあるため、曲げの起点が
            // 元の位置に残るようにローカル Y へ足し戻す。
            var bent = new Vector3(x2, y2 + _sOrigin, z);

            return _fromBendPlane * bent;
        }

        /// <summary>
        /// 曲げは R = R(θ(s)) と回転が頂点位置に依存するため、
        /// 単一のアフィン行列では表せない。常に false。
        /// </summary>
        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return false;
        }
    }
}
