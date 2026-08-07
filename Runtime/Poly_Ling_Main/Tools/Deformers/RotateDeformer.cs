// Runtime/Poly_Ling_Main/Tools/Deformers/RotateDeformer.cs
// 回転デフォーマ。作業軸の原点まわりに固定角で回す。
//
// 全頂点で同じ R を使うため、頂点間距離・角度・形状が保存される。
// 曲げと違い R が s に依存しないので TryGetAffine が true を返し、
// 部分適用時は呼び出し側が Slerp できる。
//
// 座標系: 作業軸ローカル空間（+Y = ライン方向）。回転中心は常にローカル原点。
//   ワールド上の任意点を中心にしたい場合は WorkAxisContext.Origin を動かす。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class RotateDeformerParams : DeformerParamsBase
    {
        /// <summary>作業軸ローカルのオイラー角（度）。</summary>
        public float AngleX = 0f;
        public float AngleY = 0f;
        public float AngleZ = 0f;

        public Vector3 Euler
        {
            get => new Vector3(AngleX, AngleY, AngleZ);
            set { AngleX = value.x; AngleY = value.y; AngleZ = value.z; }
        }

        public override IDeformerParams Clone()
            => new RotateDeformerParams { AngleX = AngleX, AngleY = AngleY, AngleZ = AngleZ };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<RotateDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(AngleX, o.AngleX)
                || !Mathf.Approximately(AngleY, o.AngleY)
                || !Mathf.Approximately(AngleZ, o.AngleZ);
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<RotateDeformerParams>(other, out var o)) return;
            AngleX = o.AngleX; AngleY = o.AngleY; AngleZ = o.AngleZ;
        }

        public override void Reset()
        {
            AngleX = AngleY = AngleZ = 0f;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class RotateDeformer : IMeshDeformer
    {
        public string Name        => "Rotate";
        public string DisplayName => "Rotate";

        private readonly RotateDeformerParams _params = new RotateDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public RotateDeformerParams Settings => _params;

        // Prepare で確定させる回転。Evaluate 中に毎回作り直さない。
        private Quaternion _rotation = Quaternion.identity;

        public void Prepare(DeformContext ctx)
        {
            // 回転は s に依存しないので ctx は使わない。
            _rotation = Quaternion.Euler(_params.AngleX, _params.AngleY, _params.AngleZ);
        }

        public Vector3 Evaluate(Vector3 pLocal)
            => _rotation * pLocal;

        public bool TryGetAffine(out Matrix4x4 matrix)
        {
            matrix = Matrix4x4.Rotate(_rotation);
            return true;
        }
    }
}
