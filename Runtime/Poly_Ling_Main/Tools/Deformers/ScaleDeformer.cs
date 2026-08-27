// Runtime/Poly_Ling_Main/Tools/Deformers/ScaleDeformer.cs
// 拡大縮小デフォーマ。作業軸ローカルの X / Y / Z 方向へ独立に伸縮する。
//
// 【式】p' = (sx·x, sy·y, sz·z)
//   中心は常に作業軸ローカル原点。ワールド上の任意点を中心にしたいときは
//   WorkAxisContext.Origin を動かす（回転デフォーマと同じ約束）。
//
//   軸ごとに倍率を変えられるので、1軸だけ潰す・伸ばすといった使い方ができる。
//   3軸を同じ値にすれば等倍拡大になる。
//
// 【下限】倍率 0 は面積・体積が消えて元へ戻せなくなるため MinScale で止める。
//
// 【TryGetAffine が false な理由】
//   スケールもアフィンだが、DeformApplier のアフィン分岐は affine.rotation しか
//   補間せずスケール成分を捨てる（DeformApplier.cs:189-195）。true を返すと
//   マグネットの部分適用で全く変形しない。非アフィン扱いにすると位置 Lerp
//   （同 :196-200）へ流れ、成分ごとの線形写像であるスケールでは
//   Lerp(p, S·p, w) = ((1-w)I + wS)·p となって厳密解になる。
//   したがって false を返すのが正しい。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class ScaleDeformerParams : DeformerParamsBase
    {
        /// <summary>倍率の下限。0 以下だと潰れて戻せなくなる。</summary>
        public const float MinScale = 0.01f;

        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private float _scaleZ = 1f;

        /// <summary>作業軸ローカル X 方向の倍率。</summary>
        public float ScaleX
        {
            get => _scaleX;
            set => _scaleX = Mathf.Max(MinScale, value);
        }

        /// <summary>作業軸ローカル Y 方向の倍率。</summary>
        public float ScaleY
        {
            get => _scaleY;
            set => _scaleY = Mathf.Max(MinScale, value);
        }

        /// <summary>作業軸ローカル Z 方向の倍率。</summary>
        public float ScaleZ
        {
            get => _scaleZ;
            set => _scaleZ = Mathf.Max(MinScale, value);
        }

        public Vector3 Scale
        {
            get => new Vector3(_scaleX, _scaleY, _scaleZ);
            set { ScaleX = value.x; ScaleY = value.y; ScaleZ = value.z; }
        }

        public override IDeformerParams Clone()
            => new ScaleDeformerParams { ScaleX = _scaleX, ScaleY = _scaleY, ScaleZ = _scaleZ };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<ScaleDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(_scaleX, o._scaleX)
                || !Mathf.Approximately(_scaleY, o._scaleY)
                || !Mathf.Approximately(_scaleZ, o._scaleZ);
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<ScaleDeformerParams>(other, out var o)) return;
            _scaleX = o._scaleX; _scaleY = o._scaleY; _scaleZ = o._scaleZ;
        }

        public override void Reset()
        {
            _scaleX = _scaleY = _scaleZ = 1f;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class ScaleDeformer : IMeshDeformer
    {
        public string Name        => "Scale";

        // Name は内部 ID（レジストリの検索キー）。表示だけを日本語にする。
        public string DisplayName => "拡大縮小";

        private readonly ScaleDeformerParams _params = new ScaleDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public ScaleDeformerParams Settings => _params;

        // Prepare で確定させる倍率。Evaluate 中に毎回組み立てない。
        private Vector3 _scale = Vector3.one;

        public void Prepare(DeformContext ctx)
        {
            // 倍率は s に依存しないので ctx は使わない。
            _scale = _params.Scale;
        }

        public Vector3 Evaluate(Vector3 pLocal)
            => new Vector3(pLocal.x * _scale.x, pLocal.y * _scale.y, pLocal.z * _scale.z);

        /// <summary>
        /// アフィンではあるが false を返す。理由はファイル冒頭のコメントを参照。
        /// </summary>
        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.identity;
            return false;
        }
    }
}
