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

    // ====================================================================
    // 骨の列に沿った関節ぼかし（関節の太さで幅を決める／関節から関節まで滑らか）
    // ====================================================================
    //
    // 【なぜ要るか】
    //   範囲塗り（SkinWeightVolumeOps.ApplyToMesh）は親と自分の 2 本だけを扱い、範囲内を上書きする。
    //   胴体のように関節が 3 つ以上並ぶところへ順に掛けると、後の塗りは関節の直後から親 100% で
    //   始まるため、前の塗りの 50% と食い違い、関節の位置でウェイトが飛ぶ。
    //   また半径・幅を手で決める必要があり、関節の太さに応じた幅にできない。
    //
    // 【配分】
    //   骨の列 B0..Bn-1 の関節（BindPose⁻¹ の原点）J0..Jn-1 を折れ線にする。
    //   骨 Bk は Jk〜Jk+1 の区間を受け持つ（B0 は J1 より手前すべて、Bn-1 は Jn-1 より先すべて）。
    //   関節 Jj（j≥1）の前後 hj の範囲では Bj-1 → Bj を直線で移す（Jj で 50%）。
    //   hj = 係数 × 関節の太さ rj。隣り合う区間の短い方の半分を上限にする（隣の関節のぼかしと重ならない）。
    //   関節から関節まで（FullSmooth）は幅を使わず、関節 Jk で Bk 100%、次の関節 Jk+1 へ向かって
    //   Bk+1 へ直線で移す（区間の中に 100% の平らな所を作らない）。J0 より手前は B0、最後の関節より先は Bn-1。
    //   範囲内の頂点の他ボーンのウェイトは消える。対象は選択中の描画オブジェクトの全頂点。
    //
    // 【関節の太さ】
    //   Jj を通り、前後の区間の向きの平均に直角な薄い輪切り（|s| ≤ 輪切り比 × 短い方の区間）に入る
    //   対象頂点のうち、軸から最も遠いものの距離を rj とする。
    //
    // 【頂点の位置づけ】
    //   各頂点を最も近い区間へ投影し、列の始点からの道のり u を求める。
    //   端の区間だけは区間の外へも伸ばす（中の区間は区間内へ寄せる）。
    //
    // 【座標】範囲塗りと同じくバインド空間（頂点の格納値と BindPose⁻¹ の原点）。

    /// <summary>骨の列に沿った関節ぼかしの計算結果（バインド空間）。</summary>
    public sealed class SkinWeightChainFrame
    {
        public int[]     Bones;
        public Vector3[] Joints;
        /// <summary>区間 k（Jk→Jk+1）の単位方向。</summary>
        public Vector3[] Dirs;
        /// <summary>区間 k の長さ。</summary>
        public float[]   Lengths;
        /// <summary>関節 j までの道のり（J0 が 0）。</summary>
        public float[]   Cum;
        /// <summary>関節 j の太さ（半径）。j=0 は使わない。測らなかったときは 0。</summary>
        public float[]   Radii;
        /// <summary>関節 j のぼかしの片側の幅。j=0 は使わない。FullSmooth では使わない（0）。</summary>
        public float[]   HalfWidths;
        /// <summary>関節から関節まで滑らか（関節で 100%、次の関節へ直線）。</summary>
        public bool      FullSmooth;
    }

    public static class SkinWeightChainOps
    {
        private const float Epsilon = 1e-6f;

        /// <summary>
        /// ボーン名（オブジェクト名、無ければ Humanoid 名）から masterIndex を引く。見つからなければ -1。
        /// </summary>
        public static int ResolveBone(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return -1;
            string n = name.Trim();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.Type == MeshType.Bone && mc.Name == n) return i;
            }
            int h = model.HumanoidMapping != null ? model.HumanoidMapping.Get(n) : -1;
            if (h >= 0)
            {
                var hm = model.GetMeshContext(h);
                if (hm != null && hm.Type == MeshType.Bone) return h;
            }
            return -1;
        }

        /// <summary>名前の並びをボーン索引の並びにする。1 つでも引けなければ false。</summary>
        public static bool ResolveBones(ModelContext model, string[] names, out int[] bones, out string error)
        {
            bones = null;
            error = null;
            if (names == null || names.Length == 0) { error = "ボーンの名前がありません。"; return false; }
            bones = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                bones[i] = ResolveBone(model, names[i]);
                if (bones[i] < 0) { error = $"ボーン「{names[i]}」が見つかりません。"; return false; }
            }
            return true;
        }

        /// <summary>
        /// 計算用の形を作る。fullSmooth が false なら targets の頂点から関節の太さを測って幅を決める。
        /// </summary>
        public static bool TryBuildFrame(
            ModelContext model, int[] bones, List<MeshContext> targets,
            bool fullSmooth, float widthFactor, float sliceRatio,
            out SkinWeightChainFrame frame, out string error)
        {
            frame = null;
            error = null;
            if (model == null) { error = "モデルがありません。"; return false; }
            if (bones == null || bones.Length < 2) { error = "骨は 2 本以上並べてください。"; return false; }
            if (!fullSmooth && !(widthFactor > Epsilon)) { error = "幅の係数は 0 より大きくしてください。"; return false; }
            if (!(sliceRatio > Epsilon)) { error = "輪切りの比は 0 より大きくしてください。"; return false; }

            int n = bones.Length;
            var f = new SkinWeightChainFrame
            {
                Bones      = (int[])bones.Clone(),
                Joints     = new Vector3[n],
                Dirs       = new Vector3[n - 1],
                Lengths    = new float[n - 1],
                Cum        = new float[n],
                Radii      = new float[n],
                HalfWidths = new float[n],
                FullSmooth = fullSmooth,
            };

            for (int i = 0; i < n; i++)
            {
                var b = model.GetMeshContext(bones[i]);
                if (b == null || b.Type != MeshType.Bone) { error = $"{bones[i]} 番はボーンではありません。"; return false; }
                f.Joints[i] = SkinWeightVolumeOps.BindJointPosition(b);
            }
            for (int k = 0; k < n - 1; k++)
            {
                Vector3 d = f.Joints[k + 1] - f.Joints[k];
                float   l = d.magnitude;
                if (l <= Epsilon) { error = $"関節 {k} と {k + 1} が同じ位置にあります。"; return false; }
                f.Dirs[k]    = d / l;
                f.Lengths[k] = l;
                f.Cum[k + 1] = f.Cum[k] + l;
            }

            for (int j = 1; j < n; j++)
            {
                float limit = (j < n - 1)
                    ? 0.5f * Mathf.Min(f.Lengths[j - 1], f.Lengths[j])
                    : 0.5f * f.Lengths[j - 1];

                if (fullSmooth) continue;

                Vector3 axis = (j < n - 1) ? (f.Dirs[j - 1] + f.Dirs[j]) : f.Dirs[j - 1];
                if (axis.sqrMagnitude <= Epsilon) axis = f.Dirs[j - 1];
                axis.Normalize();
                float slab = sliceRatio * ((j < n - 1) ? Mathf.Min(f.Lengths[j - 1], f.Lengths[j]) : f.Lengths[j - 1]);

                float r = MeasureRadius(targets, f.Joints[j], axis, slab);
                if (r <= Epsilon)
                {
                    error = $"関節 {j}（{model.GetMeshContext(bones[j])?.Name}）の輪切りに頂点がありません。";
                    return false;
                }
                f.Radii[j]      = r;
                f.HalfWidths[j] = Mathf.Min(widthFactor * r, limit);
            }

            frame = f;
            return true;
        }

        /// <summary>関節を通る輪切り（|s| ≤ slab）に入る頂点の、軸からの最大距離。無ければ 0。</summary>
        public static float MeasureRadius(List<MeshContext> targets, Vector3 joint, Vector3 axis, float slab)
        {
            float best = 0f;
            if (targets == null) return 0f;
            foreach (var mc in targets)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                for (int vi = 0; vi < mo.VertexCount; vi++)
                {
                    Vector3 d = mo.Vertices[vi].Position - joint;
                    float   s = Vector3.Dot(d, axis);
                    if (s < -slab || s > slab) continue;
                    float r = (d - axis * s).magnitude;
                    if (r > best) best = r;
                }
            }
            return best;
        }

        /// <summary>頂点の、列の始点からの道のり。</summary>
        public static float PathCoordinate(SkinWeightChainFrame f, Vector3 x)
        {
            int   segs  = f.Lengths.Length;
            float bestD = float.MaxValue;
            float bestU = 0f;
            for (int k = 0; k < segs; k++)
            {
                Vector3 d   = x - f.Joints[k];
                float   t   = Vector3.Dot(d, f.Dirs[k]);
                float   tc  = Mathf.Clamp(t, 0f, f.Lengths[k]);
                float   dist = (d - f.Dirs[k] * tc).sqrMagnitude;
                if (dist < bestD)
                {
                    bestD = dist;
                    float tu = t;
                    if (k > 0 && tu < 0f) tu = 0f;
                    if (k < segs - 1 && tu > f.Lengths[k]) tu = f.Lengths[k];
                    bestU = f.Cum[k] + tu;
                }
            }
            return bestU;
        }

        /// <summary>道のり u での BoneWeight。</summary>
        public static BoneWeight WeightAt(SkinWeightChainFrame f, float u)
        {
            int n = f.Bones.Length;
            if (f.FullSmooth)
            {
                // 関節 Jk で骨 Bk が 100%、次の関節 Jk+1 へ向かって Bk+1 へ直線で移す（区間の中に 100% の平らな所を作らない）。
                if (u <= 0f) return Pack(f.Bones[0], 1f, 0, 0f);
                for (int k = 0; k < n - 1; k++)
                {
                    if (u > f.Cum[k + 1]) continue;
                    float w = Mathf.Clamp01((u - f.Cum[k]) / f.Lengths[k]);
                    return Pack(f.Bones[k + 1], w, f.Bones[k], 1f - w);
                }
                return Pack(f.Bones[n - 1], 1f, 0, 0f);
            }
            for (int j = 1; j < n; j++)
            {
                float h = f.HalfWidths[j];
                if (h <= Epsilon) continue;
                float a = f.Cum[j] - h;
                float b = f.Cum[j] + h;
                if (u < a || u > b) continue;
                float w = Mathf.Clamp01((u - a) / (b - a));
                return Pack(f.Bones[j], w, f.Bones[j - 1], 1f - w);
            }
            int owner = 0;
            for (int j = 1; j < n; j++) if (u >= f.Cum[j]) owner = j;
            return Pack(f.Bones[owner], 1f, 0, 0f);
        }

        private static BoneWeight Pack(int b0, float w0, int b1, float w1)
        {
            var slots = new (int idx, float w)[4] { (b0, w0), (b1, w1), (0, 0f), (0, 0f) };
            return SkinWeightOps.SortBoneWeight(SkinWeightOps.Pack(slots));
        }

        /// <summary>1 メッシュの全頂点へ適用する。Undo・同期は呼び出し側。</summary>
        public static int ApplyToMesh(MeshContext mc, SkinWeightChainFrame f)
        {
            var mo = mc?.MeshObject;
            if (mo == null || mo.SkinKind != SkinKind.Skinned) return 0;
            int count = 0;
            for (int vi = 0; vi < mo.VertexCount; vi++)
            {
                var v = mo.Vertices[vi];
                v.BoneWeight = WeightAt(f, PathCoordinate(f, v.Position));
                count++;
            }
            if (count > 0) mo.RecomputeSkinKind();
            return count;
        }

        // ================================================================
        // 既存モデルの関節ぼかしを測る（ゼロちゃんなどの手本を調べる）
        // ================================================================

        /// <summary>測定結果。</summary>
        public struct BlendMeasure
        {
            /// <summary>関節の太さ（輪切りの最大距離）。</summary>
            public float Radius;
            /// <summary>混ざっている頂点の、関節からの軸方向の最小・最大。</summary>
            public float SMin, SMax;
            /// <summary>混ざっている頂点の数。</summary>
            public int   BlendCount;
            /// <summary>輪切りに入った頂点の数。</summary>
            public int   SliceCount;
            /// <summary>軸に使った区間の長さ（axisFrom の関節〜関節）。</summary>
            public float BoneLength;
        }

        /// <summary>
        /// 関節 joint（軸は axisFrom の関節→joint の向き）について、側 A のボーン群と側 B のボーン群の
        /// ウェイトの合計が 0.99 以上の頂点を関係する頂点とし、
        /// ・輪切り（|s| ≤ sliceRatio × ボーンの長さ）に入る関係頂点の軸からの最大距離
        /// ・B の合計が (eps, 1-eps) に入る関係頂点の s の最小・最大
        /// を返す。対象は全メッシュのうちスキンドのもの。
        /// </summary>
        public static bool Measure(
            ModelContext model, int joint, int axisFrom, int[] sideA, int[] sideB, float sliceRatio,
            out BlendMeasure m, out string error)
        {
            m = default;
            error = null;
            var jb = model?.GetMeshContext(joint);
            var ab = model?.GetMeshContext(axisFrom);
            if (jb == null || ab == null) { error = "ボーンが見つかりません。"; return false; }
            Vector3 J = SkinWeightVolumeOps.BindJointPosition(jb);
            Vector3 P = SkinWeightVolumeOps.BindJointPosition(ab);
            float   L = (J - P).magnitude;
            if (L <= Epsilon) { error = "関節が同じ位置にあります。"; return false; }
            Vector3 axis = (J - P) / L;
            float slab = sliceRatio * L;
            const float eps = 0.01f;

            m.BoneLength = L;
            m.SMin = float.MaxValue;
            m.SMax = float.MinValue;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                // SkinKind では絞らない：ウェイトを持つ頂点はバインド空間（ワールド）にある前提で読む。
                if (mo == null || mc.Type == MeshType.Bone) continue;
                for (int vi = 0; vi < mo.VertexCount; vi++)
                {
                    var v = mo.Vertices[vi];
                    if (!v.HasBoneWeight) continue;
                    var bw = v.BoneWeight.Value;
                    float wa = 0f, wb = 0f;
                    foreach (int b in sideA) wa += SkinWeightVolumeOps.BoneWeightOf(bw, b);
                    foreach (int b in sideB) wb += SkinWeightVolumeOps.BoneWeightOf(bw, b);
                    if (wa + wb < 0.99f) continue;

                    Vector3 d = v.Position - J;
                    float   s = Vector3.Dot(d, axis);
                    if (s >= -slab && s <= slab)
                    {
                        float r = (d - axis * s).magnitude;
                        if (r > m.Radius) m.Radius = r;
                        m.SliceCount++;
                    }
                    if (wb > eps && wb < 1f - eps)
                    {
                        m.BlendCount++;
                        if (s < m.SMin) m.SMin = s;
                        if (s > m.SMax) m.SMax = s;
                    }
                }
            }
            if (m.BlendCount == 0) { m.SMin = 0f; m.SMax = 0f; }
            return true;
        }
    }
}
