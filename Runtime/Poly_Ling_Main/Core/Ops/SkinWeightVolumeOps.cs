// SkinWeightVolumeOps.cs
// 立体範囲（円筒・球）によるスキンウェイトの一括配分（スキンW範囲塗り）。
//
// 【配分】
//   自関節 J の手前（親側へ長さ Hp）で自ボーン 0%、J で 50%、先端（J から先へ長さ H）で 100% とし、
//   2 区間をそれぞれ直線補間する。自ボーンへ w、親ボーンへ 1-w を書く。
//   範囲内の頂点の他ボーンのウェイトは消える。範囲外の頂点は変更しない。
//   Hp は既定で H と等しい（関節の前後に同じだけ広げる）。SeparateParentHeight を立てると
//   ParentHeight を別に使う。球では H の代わりに半径 R を使う。
//   以前は Hp が常に親関節までの長さ（|J-P|）で、親側の範囲を絞れなかった。
//
// 【座標】
//   頂点はスキンド頂点の格納値（バインド空間）をそのまま使う。
//   関節位置は各ボーンの BindPose の逆行列の原点を使う。
//   SkinningMatrix = WorldMatrix × BindPose（PolyLing_姿勢の規約.md §2）なので、
//   格納値の空間で関節と一緒に動く点は BindPose⁻¹ の原点である。
//   この選び方なら表示モード（現在ポーズ／バインドポーズ）に依らず同じ結果になり、
//   GPU の表示位置を読み戻す必要も無い。
//   非スキンドの描画オブジェクトは頂点がローカル空間なので対象にしない（呼び出し側で弾く）。
//
// 【範囲】
//   円筒 … 軸は親関節 P→J の方向（AxisMode = SelfToChild なら J→子関節 C の方向）。
//          J の手前 Hp から J の先 H まで（有限）。半径 R。
//   球   … 中心 J、半径 R。100% の基準は J から軸方向へ R 進んだ点、0% の基準は J から Hp 戻った点。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Ops
{
    /// <summary>立体範囲の形。</summary>
    public enum SkinWeightVolumeShape
    {
        /// <summary>円筒（親関節〜自関節〜先端）。</summary>
        Cylinder = 0,
        /// <summary>球（中心は自関節）。</summary>
        Sphere   = 1,
    }

    /// <summary>
    /// 範囲の軸の向き。
    /// 股関節のように親関節→自関節の向きが部位の伸びる向きと違う関節では、
    /// 自関節→子関節の向きを使う。
    /// </summary>
    public enum SkinWeightVolumeAxis
    {
        /// <summary>親関節→自関節。</summary>
        ParentToSelf = 0,
        /// <summary>自関節→子関節。</summary>
        SelfToChild  = 1,
    }

    /// <summary>立体範囲塗りの入力。</summary>
    public struct SkinWeightVolumeSpec
    {
        /// <summary>軸の向き。既定は親関節→自関節。</summary>
        public SkinWeightVolumeAxis AxisMode;
        /// <summary>子ボーンの masterIndex。負値なら自ボーンの階層の子（ボーンが 1 本だけのとき）。SelfToChild のときだけ使う。</summary>
        public int   ChildBone;
        /// <summary>自ボーンの masterIndex。</summary>
        public int   SelfBone;
        /// <summary>親ボーンの masterIndex。負値なら自ボーンの階層の親。</summary>
        public int   ParentBone;
        public SkinWeightVolumeShape Shape;
        /// <summary>半径。</summary>
        public float Radius;
        /// <summary>円筒の高さ（自関節から先端までの長さ）。球では使わない。</summary>
        public float Height;
        /// <summary>
        /// 親側の長さを別に指定するか。false なら親側は先端側と同じ長さ
        /// （円筒は Height、球は Radius）。
        /// </summary>
        public bool  SeparateParentHeight;
        /// <summary>親側の長さ（自関節から 0% の点まで）。SeparateParentHeight のときだけ使う。</summary>
        public float ParentHeight;
        /// <summary>true なら選択頂点だけ、false なら全頂点を対象にする。</summary>
        public bool  SelectedOnly;
    }

    /// <summary>計算に使う形状（バインド空間）。TryBuildFrame で作る。</summary>
    public struct SkinWeightVolumeFrame
    {
        /// <summary>0% の基準点（自関節から親側へ ParentLength 戻った点）。</summary>
        public Vector3 Parent;
        /// <summary>自関節の位置。</summary>
        public Vector3 Joint;
        /// <summary>軸の単位方向（親側→先端側）。親関節→自関節か、自関節→子関節。</summary>
        public Vector3 Axis;
        /// <summary>自関節から 0% の基準点までの長さ（親側の範囲）。</summary>
        public float   ParentLength;
        /// <summary>親関節から自関節までの長さ（ボーンの長さ。範囲には使わない）。</summary>
        public float   BoneLength;
        /// <summary>自関節から 100% の基準点までの長さ。</summary>
        public float   TipLength;
        public float   Radius;
        public SkinWeightVolumeShape Shape;
        /// <summary>解決済みの自ボーン masterIndex。</summary>
        public int     SelfBone;
        /// <summary>解決済みの親ボーン masterIndex。</summary>
        public int     ParentBone;
        /// <summary>軸に使った子ボーン masterIndex。親関節→自関節の軸なら -1。</summary>
        public int     ChildBone;

        /// <summary>先端（100% の基準点）。</summary>
        public Vector3 Tip => Joint + Axis * TipLength;
    }

    public static class SkinWeightVolumeOps
    {
        /// <summary>長さ 0 とみなす閾値。</summary>
        private const float Epsilon = 1e-6f;

        // ================================================================
        // 形状の組み立て
        // ================================================================

        /// <summary>ボーンの関節位置（バインド空間）。BindPose⁻¹ の原点。</summary>
        public static Vector3 BindJointPosition(MeshContext bone)
            => bone.BindPose.inverse.MultiplyPoint3x4(Vector3.zero);

        /// <summary>
        /// 入力から計算用の形状を作る。入力が不正なら false と理由を返す。
        /// </summary>
        public static bool TryBuildFrame(
            ModelContext model, SkinWeightVolumeSpec spec,
            out SkinWeightVolumeFrame frame, out string error)
        {
            frame = default;
            error = null;

            if (model == null) { error = "モデルがありません。"; return false; }

            var self = model.GetMeshContext(spec.SelfBone);
            if (self == null || self.Type != MeshType.Bone)
            { error = "自ボーンが指定されていないか、ボーンではありません。"; return false; }

            int parentIdx = spec.ParentBone >= 0 ? spec.ParentBone : self.HierarchyParentIndex;
            var parent = parentIdx >= 0 ? model.GetMeshContext(parentIdx) : null;
            if (parent == null || parent.Type != MeshType.Bone)
            { error = "親ボーンが見つからないか、ボーンではありません。"; return false; }
            if (parentIdx == spec.SelfBone)
            { error = "親ボーンと自ボーンが同じです。"; return false; }

            if (!(spec.Radius > Epsilon)) { error = "半径は 0 より大きくしてください。"; return false; }
            if (spec.Shape == SkinWeightVolumeShape.Cylinder && !(spec.Height > Epsilon))
            { error = "高さは 0 より大きくしてください。"; return false; }
            if (spec.SeparateParentHeight && !(spec.ParentHeight > Epsilon))
            { error = "親側の高さは 0 より大きくしてください。"; return false; }

            Vector3 p = BindJointPosition(parent);
            Vector3 j = BindJointPosition(self);
            float   l = (j - p).magnitude;
            if (l <= Epsilon) { error = "親関節と自関節が同じ位置にあります。"; return false; }

            Vector3 axis    = (j - p) / l;
            int     childIx = -1;
            if (spec.AxisMode == SkinWeightVolumeAxis.SelfToChild)
            {
                if (!TryResolveChild(model, spec.SelfBone, spec.ChildBone, out childIx, out error))
                    return false;
                Vector3 c  = BindJointPosition(model.GetMeshContext(childIx));
                float   lc = (c - j).magnitude;
                if (lc <= Epsilon) { error = "自関節と子関節が同じ位置にあります。"; return false; }
                axis = (c - j) / lc;
            }
            float   tip  = spec.Shape == SkinWeightVolumeShape.Cylinder ? spec.Height : spec.Radius;
            float   back = spec.SeparateParentHeight ? spec.ParentHeight : tip;

            frame = new SkinWeightVolumeFrame
            {
                Parent       = j - axis * back,
                Joint        = j,
                Axis         = axis,
                ParentLength = back,
                BoneLength   = l,
                TipLength    = tip,
                Radius       = spec.Radius,
                Shape        = spec.Shape,
                SelfBone     = spec.SelfBone,
                ParentBone   = parentIdx,
                ChildBone    = childIx,
            };
            return true;
        }

        /// <summary>
        /// 軸に使う子ボーンを決める。指定があればそれ（自ボーンとは別のボーンであること）、
        /// 無ければ階層の子のボーン。子のボーンが無い・複数あるときは失敗にする
        /// （複数あると向きが決まらないので、指定してもらう）。
        /// </summary>
        public static bool TryResolveChild(
            ModelContext model, int selfBone, int childBone, out int child, out string error)
        {
            child = -1;
            error = null;

            if (childBone >= 0)
            {
                var cm = model.GetMeshContext(childBone);
                if (cm == null || cm.Type != MeshType.Bone)
                { error = "子ボーンが見つからないか、ボーンではありません。"; return false; }
                if (childBone == selfBone)
                { error = "子ボーンと自ボーンが同じです。"; return false; }
                child = childBone;
                return true;
            }

            int found = 0;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (mc.HierarchyParentIndex != selfBone) continue;
                if (found == 0) child = i;
                found++;
            }
            if (found == 0) { error = "自ボーンに子のボーンがありません。"; child = -1; return false; }
            if (found > 1)  { error = "自ボーンに子のボーンが複数あります。子ボーンを指定してください。"; child = -1; return false; }
            return true;
        }

        // ================================================================
        // 1 点の判定
        // ================================================================

        /// <summary>
        /// 位置 x が範囲内なら true と自ボーンのウェイト w を返す。範囲外なら false。
        /// </summary>
        public static bool Evaluate(in SkinWeightVolumeFrame f, Vector3 x, out float w)
        {
            w = 0f;
            Vector3 d = x - f.Joint;
            float   s = Vector3.Dot(d, f.Axis);

            if (f.Shape == SkinWeightVolumeShape.Sphere)
            {
                if (d.sqrMagnitude > f.Radius * f.Radius) return false;
            }
            else
            {
                if (s < -f.ParentLength || s > f.TipLength) return false;
                Vector3 radial = d - f.Axis * s;
                if (radial.sqrMagnitude > f.Radius * f.Radius) return false;
            }

            w = WeightAt(f, s);
            return true;
        }

        /// <summary>軸方向の位置 s（自関節を 0）での自ボーンのウェイト。</summary>
        public static float WeightAt(in SkinWeightVolumeFrame f, float s)
            => s <= 0f
                ? Mathf.Clamp01(0.5f + 0.5f * s / f.ParentLength)
                : Mathf.Clamp01(0.5f + 0.5f * s / f.TipLength);

        // ================================================================
        // 対象
        // ================================================================

        /// <summary>
        /// 対象メッシュのうち非スキンドのものの名前。空なら全件スキンド。
        /// </summary>
        public static List<string> FindNonSkinned(List<MeshContext> targets)
        {
            var list = new List<string>();
            foreach (var mc in targets)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (mo.SkinKind != SkinKind.Skinned)
                    list.Add(string.IsNullOrEmpty(mc.Name) ? "?" : mc.Name);
            }
            return list;
        }

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>
        /// 1 メッシュへ適用する。Undo・同期は呼び出し側（PlayerCommandDispatcher）。
        /// </summary>
        /// <returns>書き換えた頂点数</returns>
        public static int ApplyToMesh(MeshContext mc, in SkinWeightVolumeFrame f, bool selectedOnly)
        {
            var mo = mc?.MeshObject;
            if (mo == null || mo.SkinKind != SkinKind.Skinned) return 0;

            int count = 0;
            if (selectedOnly)
            {
                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) return 0;
                foreach (int vi in sel)
                    if (ApplyVertex(mo, vi, f)) count++;
            }
            else
            {
                for (int vi = 0; vi < mo.VertexCount; vi++)
                    if (ApplyVertex(mo, vi, f)) count++;
            }

            if (count > 0) mo.RecomputeSkinKind();
            return count;
        }

        // ================================================================
        // 区間塗り（親関節〜指定関節を親ボーン 100%）
        // ================================================================
        //
        // 【何をするか】
        //   親関節 P から指定関節 J までの円筒（軸 P→J、長さ |J-P|、半径 R）の中の頂点を、
        //   親ボーン 1 本だけ・ウェイト 1.0 にする（他のスロットは消す）。
        //   関節の前後のぼかしは範囲塗り（ApplyToMesh）で後から入れる。
        //   範囲塗りは範囲の外を変えないので、関節と関節の間の中ほどはこれで決める。
        //
        // 【座標】範囲塗りと同じくバインド空間（頂点の格納値と BindPose⁻¹ の原点）。

        /// <summary>
        /// 区間塗りの形状を作る。jointBone は区間の先の関節（指定関節）、
        /// parentBone は塗るボーン（-1 で jointBone の階層の親）。
        /// 返す frame は Parent=P・Joint=J・Axis=P→J・BoneLength=|J-P|・Radius を持つ。
        /// </summary>
        public static bool TryBuildSegment(
            ModelContext model, int jointBone, int parentBone, float radius,
            out SkinWeightVolumeFrame frame, out string error)
        {
            frame = default;
            error = null;

            if (model == null) { error = "モデルがありません。"; return false; }

            var joint = model.GetMeshContext(jointBone);
            if (joint == null || joint.Type != MeshType.Bone)
            { error = "関節（ボーン）が指定されていないか、ボーンではありません。"; return false; }

            int parentIdx = parentBone >= 0 ? parentBone : joint.HierarchyParentIndex;
            var parent = parentIdx >= 0 ? model.GetMeshContext(parentIdx) : null;
            if (parent == null || parent.Type != MeshType.Bone)
            { error = "親ボーンが見つからないか、ボーンではありません。"; return false; }
            if (parentIdx == jointBone)
            { error = "親ボーンと関節が同じです。"; return false; }

            if (!(radius > Epsilon)) { error = "半径は 0 より大きくしてください。"; return false; }

            Vector3 p = BindJointPosition(parent);
            Vector3 j = BindJointPosition(joint);
            float   l = (j - p).magnitude;
            if (l <= Epsilon) { error = "親関節と関節が同じ位置にあります。"; return false; }

            frame = new SkinWeightVolumeFrame
            {
                Parent       = p,
                Joint        = j,
                Axis         = (j - p) / l,
                ParentLength = l,
                BoneLength   = l,
                TipLength    = 0f,
                Radius       = radius,
                Shape        = SkinWeightVolumeShape.Cylinder,
                SelfBone     = jointBone,
                ParentBone   = parentIdx,
                ChildBone    = -1,
            };
            return true;
        }

        /// <summary>位置 x が親関節〜関節の円筒の中か（両端を含む）。</summary>
        public static bool InSegment(in SkinWeightVolumeFrame f, Vector3 x)
        {
            Vector3 d = x - f.Parent;
            float   s = Vector3.Dot(d, f.Axis);
            if (s < 0f || s > f.BoneLength) return false;
            Vector3 radial = d - f.Axis * s;
            return radial.sqrMagnitude <= f.Radius * f.Radius;
        }

        /// <summary>
        /// 1 メッシュの区間内の頂点を親ボーン 100%（1 本だけ）にする。
        /// Undo・同期は呼び出し側（PlayerCommandDispatcher）。
        /// </summary>
        /// <returns>書き換えた頂点数</returns>
        public static int ApplySegmentFillToMesh(MeshContext mc, in SkinWeightVolumeFrame f, bool selectedOnly)
        {
            var mo = mc?.MeshObject;
            if (mo == null || mo.SkinKind != SkinKind.Skinned) return 0;

            var slots = new (int idx, float w)[4]
            {
                (f.ParentBone, 1f),
                (0, 0f),
                (0, 0f),
                (0, 0f),
            };
            BoneWeight bw = SkinWeightOps.SortBoneWeight(SkinWeightOps.Pack(slots));

            // in 引数はローカル関数から参照できないので写す。
            var frame = f;
            int count = 0;
            void Apply(int vi)
            {
                if (vi < 0 || vi >= mo.VertexCount) return;
                var vertex = mo.Vertices[vi];
                if (!InSegment(frame, vertex.Position)) return;
                vertex.BoneWeight = bw;
                count++;
            }

            if (selectedOnly)
            {
                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) return 0;
                foreach (int vi in sel) Apply(vi);
            }
            else
            {
                for (int vi = 0; vi < mo.VertexCount; vi++) Apply(vi);
            }

            if (count > 0) mo.RecomputeSkinKind();
            return count;
        }

        private static bool ApplyVertex(MeshObject mo, int vi, in SkinWeightVolumeFrame f)
        {
            if (vi < 0 || vi >= mo.VertexCount) return false;
            var vertex = mo.Vertices[vi];
            if (!Evaluate(f, vertex.Position, out float w)) return false;
            vertex.BoneWeight = MakeWeight(f, w);
            return true;
        }

        /// <summary>自ボーン w・親ボーン 1-w の BoneWeight。</summary>
        public static BoneWeight MakeWeight(in SkinWeightVolumeFrame f, float w)
        {
            var slots = new (int idx, float w)[4]
            {
                (f.SelfBone,   w),
                (f.ParentBone, 1f - w),
                (0, 0f),
                (0, 0f),
            };
            return SkinWeightOps.SortBoneWeight(SkinWeightOps.Pack(slots));
        }

        // ================================================================
        // プレビュー
        // ================================================================

        /// <summary>
        /// 適用後の自ボーンのウェイトを頂点ごとに返す（データは変えない）。
        /// 範囲外・対象外の頂点は現在の自ボーンのウェイト。
        /// </summary>
        public static float[] ComputePreview(MeshContext mc, in SkinWeightVolumeFrame f, bool selectedOnly)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return null;

            var result = new float[mo.VertexCount];
            for (int vi = 0; vi < mo.VertexCount; vi++)
            {
                var v = mo.Vertices[vi];
                result[vi] = v.HasBoneWeight ? BoneWeightOf(v.BoneWeight.Value, f.SelfBone) : 0f;
            }

            if (mo.SkinKind != SkinKind.Skinned) return result;

            if (selectedOnly)
            {
                var sel = mc.SelectedVertices;
                if (sel == null) return result;
                foreach (int vi in sel)
                {
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    if (Evaluate(f, mo.Vertices[vi].Position, out float w)) result[vi] = w;
                }
            }
            else
            {
                for (int vi = 0; vi < mo.VertexCount; vi++)
                    if (Evaluate(f, mo.Vertices[vi].Position, out float w)) result[vi] = w;
            }
            return result;
        }

        /// <summary>BoneWeight のうち指定ボーンの合計。</summary>
        public static float BoneWeightOf(BoneWeight bw, int bone)
        {
            float w = 0f;
            if (bw.boneIndex0 == bone) w += bw.weight0;
            if (bw.boneIndex1 == bone) w += bw.weight1;
            if (bw.boneIndex2 == bone) w += bw.weight2;
            if (bw.boneIndex3 == bone) w += bw.weight3;
            return w;
        }
    }
}
