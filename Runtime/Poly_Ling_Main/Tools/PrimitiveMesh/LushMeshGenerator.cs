// LushMeshGenerator.cs
// まつげ等（旧 NCSHAGLIB EPMXToolFor_LushMaker）。梯子の段ごとに原型を置く、配置系（藤壺）の変種。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【旧ツールの手順】（PolyLing_作り直し整備計画.md 3.5・段 7）
//   梯子をスプラインで補間して段を増やす（GetSplineBeltPair → ここでは BeltShapeOps.PreprocessTraced）
//   → 段ごとの基準三角形（底辺＝段、頂点＝次の段の中点。最後は終点）（CreateBaseTriangles）
//   → 回転を始点から終点へ線形補間（RotationsGetMatrixBsub）
//   → 原型を枠に置く（CreateAndAddVertex1：倍率＝段の長さ×10、位置＝中点か端）
//   参照面への貼り付けは別の項目（createSurfacePaste）にした。
//
// 【枠】（旧 GetAxis）
//   中点 m＝(左＋右)/2、dv0＝normalize(頂点 − m)、dv2＝normalize(右 − m)
//   vUp＝normalize(dv0 × dv2)、vScreen＝normalize(右 − 左)、vRay＝normalize(vScreen × vUp)
//   枠の反転（旧 rotatePipe）は vScreen と vUp の符号を変える（vRay はそのまま）
//   原型の点 P は q（回転）で回してから  X→vRay、Y→−vScreen、Z→−vUp に載せる
//
// 【回転】角度（度）を X→Y→Z の順にローカル軸で回す（旧 RotationsLocalMatrix）。q＝qX·qY·qZ。
//   旧ツールの回転軸の初期化（ResetLocalMatrix）はローカル変数を作るだけで軸を戻さず、
//   回転が段・梯子をまたいで積み重なっていた。ここでは段ごとに軸を戻す（意図どおり）。
//   回転の向きは旧 MatrixHelper.RotationAxis（行ベクトル）と Unity の AngleAxis で数値が同じ。
//
// 【頂点 ID】旧ツールは連番を振ったが、PolyLing では自動で振らない（頂点 ID の自動付与は禁止）。
//   置いた 1 個＝パーツ 1 つ（PartsIdCounter）、サブ ID は原型のまま（藤壺と同じ）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.PlaceObject
{
    /// <summary>まつげの枠の決め方。</summary>
    public enum LushFrameMode
    {
        /// <summary>底辺＝段、頂点＝次の段の中点（最後は終点）。旧ツールと同じ。</summary>
        NextRungMidpoint = 0,
        /// <summary>梯子の面の法線（藤壺と同じ）を上向きに使う。</summary>
        BeltNormal = 1,
    }

    /// <summary>原型を置く位置。</summary>
    public enum LushAnchor
    {
        /// <summary>段の中点（旧 posMode 0）。</summary>
        Center = 0,
        /// <summary>段の左（旧 posMode 1）。</summary>
        Left = 1,
        /// <summary>段の右（旧 posMode 2）。</summary>
        Right = 2,
    }

    /// <summary>回転の補間。</summary>
    public enum LushRotationInterp
    {
        /// <summary>段 i で 始点＋(終点−始点)×i/n。終点には届かない（旧ツールと同じ）。</summary>
        Linear = 0,
        /// <summary>段 i で 始点＋(終点−始点)×i/(n−1)。最後の段が終点。</summary>
        LinearInclusive = 1,
        /// <summary>全段で始点。</summary>
        Constant = 2,
    }

    /// <summary>まつげのパラメータ。</summary>
    public struct LushParams
    {
        public const float ScaleMin = 0.0001f;
        public const float ScaleMax = 1000f;

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        [PLParam(TextKey = "LushFrameMode", Description = "枠の決め方（次の段の中点＝旧ツール / 梯子の面の法線）")]
        public LushFrameMode FrameMode;

        [PLParam(TextKey = "LushAnchor", Description = "原型を置く位置（段の中点 / 左 / 右）")]
        public LushAnchor Anchor;

        [PLParam(TextKey = "LushRotationStart", Description = "最初の段の回転（度。X→Y→Z の順にローカル軸で回す）")]
        public Vector3 RotationStart;

        [PLParam(TextKey = "LushRotationEnd", Description = "最後の段へ向かう回転（度）")]
        public Vector3 RotationEnd;

        [PLParam(TextKey = "LushRotationInterp", Description = "回転の補間（i/n＝旧ツール / i/(n-1)＝最後の段が終点 / 一定）")]
        public LushRotationInterp RotationInterp;

        [PLParam(TextKey = "PlaceScaleMode", Description = "倍率の決め方（段の長さに比例 / 一定）")]
        public PlaceScaleMode ScaleMode;

        [PLParam(TextKey = "Scale", Description = "倍率。段の長さに比例のときは段の長さへの掛け率（旧ツールは 10）", Min = ScaleMin, Max = ScaleMax)]
        public float Scale;

        [PLParam(TextKey = "LushLengthPlaneNormal", Description = "段の長さをこの法線に直交する面で測る。0 なら 3D の長さ（旧ツールは Z を無視＝(0,0,1)）")]
        public Vector3 LengthPlaneNormal;

        [PLParam(TextKey = "LushFlipFrame", Description = "枠を反転する（旧ツールの rotatePipe）")]
        public bool FlipFrame;

        [PLParam(TextKey = "LushReverseOrder", Description = "逆順に置く（パーツ ID の並びが逆になる）")]
        public bool ReverseOrder;

        [PLParam(TextKey = "PlaceIncludeChildren", Description = "原型の子孫も一緒に置く")]
        public bool IncludeChildren;

        /// <summary>
        /// 取り込み元のはしごのボーンウェイトを、置いた原型へ引き継ぐ（段 I）。
        /// 原型 1 個の全頂点に、その段の左右の点のウェイトを置く位置（中点＝半々 / 左 / 右）で混ぜた 1 つの値を入れる。
        /// </summary>
        [PLParam(TextKey = "BeltInheritWeights",
                 Description = "取り込み元のはしごのボーンウェイトを置いた原型へ引き継ぐ（原型ごとに段の左右を置く位置で混ぜた 1 つの値）")]
        public bool InheritBeltWeights;

        public static LushParams Default => new LushParams
        {
            MeshName          = "Lush",
            FrameMode         = LushFrameMode.NextRungMidpoint,
            Anchor            = LushAnchor.Center,
            RotationStart     = Vector3.zero,
            RotationEnd       = Vector3.zero,
            RotationInterp    = LushRotationInterp.Linear,
            ScaleMode         = PlaceScaleMode.RungLength,
            Scale             = 1f,
            LengthPlaneNormal = Vector3.zero,
            FlipFrame         = false,
            ReverseOrder      = false,
            IncludeChildren   = true,
            InheritBeltWeights = false,
        };
    }

    public static class LushMeshGenerator
    {
        /// <summary>段 1 つの枠（回転の前）。</summary>
        public struct Frame
        {
            public Vector3 Center;
            /// <summary>原型の X・Y・Z を載せる軸（vRay・−vScreen・−vUp）。</summary>
            public Vector3 X, Y, Z;
            public float   Scale;
        }

        /// <summary>
        /// 段 k の枠。作れない（段の長さ 0・頂点が段の上）なら false。
        /// 逆方向（ModifierInverse.InvertLush）も同じ計算を使う。
        /// </summary>
        public static bool TryComputeFrame(
            IReadOnlyList<Vector3> left, IReadOnlyList<Vector3> right, bool closed, bool flipWinding,
            Vector3? endPoint, int k, LushParams p, out Frame frame)
        {
            frame = default;
            int n = Mathf.Min(left.Count, right.Count);
            if (k < 0 || k >= n || n < 1) return false;

            Vector3 v1 = left[k], v2 = right[k];
            Vector3 m  = (v1 + v2) * 0.5f;
            Vector3 side = v2 - v1;
            if (side.sqrMagnitude <= 1e-12f) return false;

            Vector3 vScreen = side.normalized;
            Vector3 vUp;

            if (p.FrameMode == LushFrameMode.BeltNormal)
            {
                if (n < 2) return false;
                int segments = closed ? n : n - 1;
                // 梯子の面の法線 N は (右×前) の向き。旧ツールの vUp（前×右）はその逆向き。
                vUp = -PlaceObjectMeshGenerator.RungNormal(left, right, closed, flipWinding, segments, k);
                vUp -= vScreen * Vector3.Dot(vUp, vScreen);
                if (vUp.sqrMagnitude <= 1e-12f) return false;
                vUp.Normalize();
            }
            else
            {
                Vector3 apex;
                if (k < n - 1)          apex = (left[k + 1] + right[k + 1]) * 0.5f;
                else if (closed)        apex = (left[0] + right[0]) * 0.5f;
                else if (endPoint.HasValue) apex = endPoint.Value;
                else if (n >= 2)        apex = m + (m - (left[k - 1] + right[k - 1]) * 0.5f);
                else return false;

                Vector3 dv0 = apex - m;
                if (dv0.sqrMagnitude <= 1e-12f) return false;
                vUp = Vector3.Cross(dv0.normalized, (v2 - m).normalized);
                if (vUp.sqrMagnitude <= 1e-12f) return false;
                vUp.Normalize();
            }

            Vector3 vRay = Vector3.Cross(vScreen, vUp).normalized;
            if (p.FlipFrame) { vScreen = -vScreen; vUp = -vUp; }

            float len = side.magnitude;
            if (p.LengthPlaneNormal.sqrMagnitude > 1e-12f)
            {
                Vector3 pn = p.LengthPlaneNormal.normalized;
                len = (side - pn * Vector3.Dot(side, pn)).magnitude;
            }
            float userScale = p.Scale <= 0f ? 1f : p.Scale;
            float scale = p.ScaleMode == PlaceScaleMode.Uniform ? userScale : len * userScale;

            Vector3 center = p.Anchor == LushAnchor.Left ? v1 : p.Anchor == LushAnchor.Right ? v2 : m;

            frame = new Frame { Center = center, X = vRay, Y = -vScreen, Z = -vUp, Scale = scale };
            return true;
        }

        /// <summary>段 k（全 n 段）の補間の位置 t。</summary>
        public static float InterpT(int k, int n, LushRotationInterp mode)
        {
            switch (mode)
            {
                case LushRotationInterp.Constant:        return 0f;
                case LushRotationInterp.LinearInclusive: return n > 1 ? (float)k / (n - 1) : 0f;
                default:                                 return n > 0 ? (float)k / n : 0f;
            }
        }

        /// <summary>角度（度）から回転。X→Y→Z の順にローカル軸で回す。</summary>
        public static Quaternion EulerLocalXYZ(Vector3 deg)
            => Quaternion.AngleAxis(deg.x, Vector3.right)
             * Quaternion.AngleAxis(deg.y, Vector3.up)
             * Quaternion.AngleAxis(deg.z, Vector3.forward);

        /// <summary>段 k（全 n 段）の角度（度）。</summary>
        public static Vector3 AnglesAt(int k, int n, LushParams p)
        {
            float t = InterpT(k, n, p.RotationInterp);
            return p.RotationStart + (p.RotationEnd - p.RotationStart) * t;
        }

        /// <summary>
        /// 前処理を済ませた梯子 1 本ぶんの原型を dst へ置く。置いた段だけパーツ ID を進める。
        /// 段の番号を枠の番号として控える（ModifierFrameTrace。呼び出し側が BeginBelt を立てる）。
        /// </summary>
        public static void AppendBelt(
            MeshObject dst, BeltCsvEntry belt, MeshObject proto, LushParams p, PartsIdCounter partsIds)
        {
            if (dst == null || belt == null || !belt.HasData || proto == null || proto.VertexCount == 0) return;
            int n = Mathf.Min(belt.Left.Count, belt.Right.Count);

            for (int idx = 0; idx < n; idx++)
            {
                int k = p.ReverseOrder ? n - 1 - idx : idx;
                if (!TryComputeFrame(belt.Left, belt.Right, belt.Closed, belt.FlipWinding, belt.EndPoint, k, p, out var f))
                    continue;

                Quaternion q = EulerLocalXYZ(AnglesAt(k, n, p));
                // 回転した原型の点を枠へ載せる＝枠の軸を q の列で混ぜる。
                Vector3 cx = q * Vector3.right, cy = q * Vector3.up, cz = q * Vector3.forward;
                Vector3 x = f.X * cx.x + f.Y * cx.y + f.Z * cx.z;
                Vector3 y = f.X * cy.x + f.Y * cy.y + f.Z * cy.z;
                Vector3 z = f.X * cz.x + f.Y * cz.y + f.Z * cz.z;

                bool assign = partsIds != null;
                int  pid    = assign ? partsIds.Take() : 0;

                // はしごのウェイト（載っていれば。段 I）：置く位置で段の左右を混ぜた 1 つの値。
                BoneWeight? w = null;
                if (belt.HasWeights)
                {
                    float t = p.Anchor == LushAnchor.Left ? 0f : p.Anchor == LushAnchor.Right ? 1f : 0.5f;
                    w = Poly_Ling.UI.SkinWeightOps.LerpNullable(belt.LeftWeights[k], belt.RightWeights[k], t);
                }
                PlaceObjectMeshGenerator.AppendInstance(dst, proto, f.Center, x, y, z, f.Scale, assign, pid, k, w);
            }
        }
    }
}
