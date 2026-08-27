// Runtime/Poly_Ling_Main/Tools/Deformers/MoveDeformer.cs
// 移動デフォーマ。作業軸ローカルの X / Y / Z 方向へ平行移動する。
//
// 【式】p' = pLocal + (dx, dy, dz)
//   全頂点で同じ差分を足すだけなので、頂点間の距離も角度も保存される。
//   s に依存しないため Prepare で確定させる値は無い。
//
// 【ワールド移動との違い】差分は作業軸ローカルで与える。作業軸を傾けておけば
//   その傾いた方向へ真っ直ぐ動かせる。ワールド軸に沿った移動が欲しいときは
//   作業軸の回転を identity にすればよい。
//
// 【TryGetAffine が false な理由】
//   平行移動は当然アフィンだが、DeformApplier のアフィン分岐は
//   affine.rotation しか補間せず平行移動成分を捨てる（DeformApplier.cs:189-195）。
//   true を返すとマグネットの部分適用で全く動かなくなる。
//   非アフィン扱いにすると位置 Lerp（同 :196-200）へ流れ、平行移動では
//   これが厳密解になる。したがって false を返すのが正しい。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    public class MoveDeformerParams : DeformerParamsBase
    {
        /// <summary>作業軸ローカルの移動量。</summary>
        public float OffsetX = 0f;
        public float OffsetY = 0f;
        public float OffsetZ = 0f;

        public Vector3 Offset
        {
            get => new Vector3(OffsetX, OffsetY, OffsetZ);
            set { OffsetX = value.x; OffsetY = value.y; OffsetZ = value.z; }
        }

        public override IDeformerParams Clone()
            => new MoveDeformerParams { OffsetX = OffsetX, OffsetY = OffsetY, OffsetZ = OffsetZ };

        public override bool IsDifferentFrom(IDeformerParams other)
        {
            if (!IsSameType<MoveDeformerParams>(other, out var o)) return true;
            return !Mathf.Approximately(OffsetX, o.OffsetX)
                || !Mathf.Approximately(OffsetY, o.OffsetY)
                || !Mathf.Approximately(OffsetZ, o.OffsetZ);
        }

        public override void CopyFrom(IDeformerParams other)
        {
            if (!IsSameType<MoveDeformerParams>(other, out var o)) return;
            OffsetX = o.OffsetX; OffsetY = o.OffsetY; OffsetZ = o.OffsetZ;
        }

        public override void Reset()
        {
            OffsetX = OffsetY = OffsetZ = 0f;
        }
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    public class MoveDeformer : IMeshDeformer
    {
        public string Name        => "Move";

        // Name は内部 ID（レジストリの検索キー）。表示だけを日本語にする。
        public string DisplayName => "移動";

        private readonly MoveDeformerParams _params = new MoveDeformerParams();
        public IDeformerParams Params => _params;

        /// <summary>型付きアクセサ。UI / ハンドラから使う。</summary>
        public MoveDeformerParams Settings => _params;

        // Prepare で確定させる差分。Evaluate 中に毎回組み立てない。
        private Vector3 _offset = Vector3.zero;

        public void Prepare(DeformContext ctx)
        {
            // 移動量は s に依存しないので ctx は使わない。
            _offset = _params.Offset;
        }

        public Vector3 Evaluate(Vector3 pLocal)
            => pLocal + _offset;

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
