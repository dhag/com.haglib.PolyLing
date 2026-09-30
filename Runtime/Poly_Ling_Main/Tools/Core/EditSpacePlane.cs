// EditSpacePlane.cs
// 作業空間の平面制約で、ツールが変形の計算に使う平面の情報。
// Runtime/Poly_Ling_Main/Tools/Core/ に配置
//
// 【何のためか】
//   作業空間（PolyLing_UV_Billboard_Design.md 5.2）では、代理メッシュの頂点を
//   ローカル XY 面の中だけで動かす。移動・回転・拡大縮小は「3D で自由に変形してから
//   Z を捨てる」のではなく、計算の段階から平面の軸を使う。
//   本構造体は、その平面の表示上の姿勢と単位換算をツールへ渡す。
//
// 【既存の「作業平面」（WorkPlaneContext）とは別物】
//   WorkPlaneContext は面の追加などで点を置く平面（モデルごとに保存される設定）。
//   本構造体は作業空間が開いている間だけ存在する、代理メッシュの平面。
//
// 【誰が作るか】
//   Player では PolyLingPlayerViewerCore が、作業空間が開いていて操作対象が
//   代理メッシュだけのときに作り、ToolContext.GetEditSpacePlane から返す。
//   それ以外（通常の編集）は null で、ツールは従来どおりワールド軸で動く。

using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>作業空間の平面。原点・姿勢は表示上のもの（ビルボードを含む DisplayWorldMatrix 由来）。</summary>
    public readonly struct EditSpacePlane
    {
        /// <summary>平面の原点（ワールド）。代理メッシュのローカル原点の表示位置。</summary>
        public readonly Vector3 Origin;

        /// <summary>平面の姿勢（ワールド）。ローカル X・Y が平面の軸、ローカル Z が法線。</summary>
        public readonly Quaternion Rotation;

        /// <summary>
        /// 平面の X 方向の、表示単位 1 あたりのローカル長さ（UV なら作業倍率 sU）。
        /// 数値入力の X とマグネット半径はこの値で割って表示する。
        /// </summary>
        public readonly float UnitScale;

        /// <summary>
        /// 平面の Y 方向の、表示単位 1 あたりのローカル長さ（UV なら作業倍率 sV）。
        /// 数値入力の Y はこの値で割って表示する。画像の縦横比で表示する UV のとき UnitScale と違う。
        /// </summary>
        public readonly float UnitScaleY;

        public EditSpacePlane(Vector3 origin, Quaternion rotation, Vector2 unitScale)
        {
            Origin     = origin;
            Rotation   = rotation;
            UnitScale  = unitScale.x > 0f ? unitScale.x : 1f;
            UnitScaleY = unitScale.y > 0f ? unitScale.y : 1f;
        }

        /// <summary>平面の X 軸（ワールド）。</summary>
        public Vector3 AxisX  => Rotation * Vector3.right;

        /// <summary>平面の Y 軸（ワールド）。</summary>
        public Vector3 AxisY  => Rotation * Vector3.up;

        /// <summary>平面の法線（ワールド。ローカル Z）。</summary>
        public Vector3 Normal => Rotation * Vector3.forward;

        // ================================================================
        // 代理のローカル座標での写像
        // ================================================================
        //   平面内の操作（法線まわりの回転、平面の X・Y だけの伸縮、平面内の移動）は、
        //   ワールドで組んでも代理のローカル Z を保つ写像になる。ところが頂点ごとに
        //   「ローカル → ワールド → 操作 → ローカル」と往復すると、行列の丸めで
        //   ローカル Z に 1e-8 程度の値が乗る。
        //   そこで写像そのものを代理のローカル座標へ移し（M⁻¹·Op·M）、
        //   Z の行を恒等にした形で頂点へ掛ける。Z の行は操作が平面を保つことから
        //   決まる値で、計算結果の Z を捨てているのではない。
        //   頂点のローカル Z が 0 なら、結果の Z も厳密に 0 のまま。

        /// <summary>
        /// ワールドの平面内の操作 worldOp を、頂点の行列 vertexMatrix のローカル座標での写像にする。
        /// 結果の Z 行は恒等（z' = z）。
        /// </summary>
        public static Matrix4x4 ToLocalPlanar(Matrix4x4 worldOp, Matrix4x4 vertexMatrix)
        {
            var m = vertexMatrix.inverse * worldOp * vertexMatrix;
            m.m20 = 0f; m.m21 = 0f; m.m22 = 1f; m.m23 = 0f;
            return m;
        }

        /// <summary>
        /// 平面内のワールド移動量を、頂点の行列 vertexMatrix のローカル移動量にする。Z 成分は 0。
        /// </summary>
        public static Vector3 ToLocalPlanarDelta(Vector3 worldDelta, Matrix4x4 vertexMatrixInverse)
        {
            var d = vertexMatrixInverse.MultiplyVector(worldDelta);
            d.z = 0f;
            return d;
        }
    }
}
