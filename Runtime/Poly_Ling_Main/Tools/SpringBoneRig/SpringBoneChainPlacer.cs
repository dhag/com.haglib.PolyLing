// Runtime/Poly_Ling_Main/Tools/SpringBoneRig/SpringBoneChainPlacer.cs
// ============================================================
// 揺れもの用のボーン鎖を配置する
// ============================================================
//
// 【何をするか / しないか】
//   するのは「ボーンを作って親子につなぐ」ことだけ。
//   メッシュは作らない。ウェイトも塗らない。揺れ方も付けない。
//   3 つを 1 つの関数に混ぜると、片方だけやり直したいときに全部作り直しになる
//   （SpringBoneTestRigBuilder.AddSkirtMesh が頂点・ウェイト・面・マテリアルを
//     一度にやっていて、実際にそうなっていた）。
//
// 【配置の型】
//   Single     … 折れ線（プロファイル）に沿って 1 本
//   Cylinder   … 取り付け先のまわりに等間隔で N 本。上下の半径を変えれば円錐
//   Revolution … 折れ線を軸まわりに N 方向へ回して N 本
//
//   Cylinder は「上下 2 点だけのプロファイルを持つ Revolution」と同じだが、
//   スカートで実際に要るのはほぼこれなので、半径と高さだけで指定できる
//   入口を別に用意する。
//
// 【座標系】
//   ボーンのローカル位置は BoneTransform.Position。親からの相対で入れる。
//   Unity は左手系。プロファイルは XY 平面（X が半径方向、Y が高さ）で受け、
//   これを角度ぶん回して XZ 平面上へ展開する。
//
// 【BindPose】
//   親の姿勢が決まらないと入れられないので、全部足したあとに
//   ComputeWorldMatrices() を 1 回だけ回してから入れ直す。
//   SpringBoneTestRigBuilder.FixBindPoses と同じ手順。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.SpringBoneRig
{
    /// <summary>鎖の並べ方。</summary>
    public enum SpringBoneChainLayout
    {
        /// <summary>折れ線に沿って 1 本。</summary>
        Single = 0,

        /// <summary>取り付け先のまわりに等間隔で N 本。上下の半径で円錐にもなる。</summary>
        Cylinder = 1,

        /// <summary>折れ線を軸まわりに N 方向へ回して N 本。</summary>
        Revolution = 2,
    }

    /// <summary>配置の結果。</summary>
    public sealed class SpringBoneChainPlaceResult
    {
        /// <summary>作ったボーンの索引。鎖ごとに 1 リスト。先頭が鎖の先頭。</summary>
        public readonly List<List<int>> Chains = new List<List<int>>();

        /// <summary>作ったボーンの総数。</summary>
        public int BoneCount
        {
            get { int n = 0; foreach (var c in Chains) n += c.Count; return n; }
        }

        public string Message = "";
    }

    /// <summary>揺れもの用のボーン鎖を置く。</summary>
    public static class SpringBoneChainPlacer
    {
        /// <summary>
        /// 鎖を置く。
        /// </summary>
        /// <param name="attachIndex">
        /// 親にするボーンの索引。-1 で親を付けない。
        /// </param>
        /// <param name="originIndex">
        /// 位置の基準にするボーンの索引。-1 でワールド原点。親にはしない。
        ///
        /// 親と分けている理由。親子関係を変えてもワールド位置は変わらないのが
        /// 当たり前なので、あとから親を付け替えても位置は直せない。
        /// 作る時点で正しい場所へ置くために、基準だけを別に受ける。
        /// </param>
        /// <param name="profile">
        /// 折れ線。X が取り付け先からの水平距離、Y が高さ（下向きが負）。
        /// Single / Revolution で使う。Cylinder では使わない。
        /// 先頭が鎖の先頭、末尾が鎖の先。2 点以上必要。
        /// </param>
        public static SpringBoneChainPlaceResult Place(
            ModelContext model,
            SpringBoneChainLayout layout,
            int attachIndex,
            string namePrefix,
            int originIndex,
            int chainCount,
            int segments,
            float topRadius,
            float bottomRadius,
            float height,
            float startAngleDeg,
            IReadOnlyList<Vector2> profile,
            bool addTailBone,
            float tailLength)
        {
            var result = new SpringBoneChainPlaceResult();

            if (model == null)
            {
                result.Message = "モデルがありません。";
                return result;
            }

            if (string.IsNullOrEmpty(namePrefix)) namePrefix = "Spring";

            // 位置の基準。親とは別に解決する。
            Vector3 originWorld = Vector3.zero;
            if (originIndex >= 0 && originIndex < model.MeshContextCount)
            {
                var omc = model.GetMeshContext(originIndex);
                if (omc == null)
                {
                    result.Message = "位置の基準が見つかりません。";
                    return result;
                }
                originWorld = Origin(omc.WorldMatrix);
            }

            // 親。範囲外は親なし扱いにする。
            Vector3 parentWorldRoot = Vector3.zero;
            if (attachIndex >= 0 && attachIndex < model.MeshContextCount)
            {
                var amc = model.GetMeshContext(attachIndex);
                if (amc == null)
                {
                    result.Message = "親にするボーンが見つかりません。";
                    return result;
                }
                parentWorldRoot = Origin(amc.WorldMatrix);
            }
            else
            {
                attachIndex = -1;
            }

            // 折れ線を決める。
            List<Vector3> line = BuildProfile(layout, profile, segments, topRadius, bottomRadius, height);
            if (line == null || line.Count < 2)
            {
                result.Message = "折れ線が 2 点未満です。";
                return result;
            }

            int count = layout == SpringBoneChainLayout.Single ? 1 : Mathf.Max(1, chainCount);

            for (int c = 0; c < count; c++)
            {
                float deg = startAngleDeg + (count > 1 ? 360f * c / count : 0f);
                float rad = deg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                // 折れ線を角度ぶん回して、取り付け先を基準にワールド位置へ展開する。
                var worlds = new List<Vector3>(line.Count);
                foreach (var p in line)
                {
                    // p.x を半径方向、p.z を接線方向として回す。p.y はそのまま高さ。
                    float x = p.x * cos - p.z * sin;
                    float z = p.x * sin + p.z * cos;
                    worlds.Add(originWorld + new Vector3(x, p.y, z));
                }

                string chainName = count > 1
                    ? $"{namePrefix}_{c:00}"
                    : namePrefix;

                var indices = new List<int>(worlds.Count);
                // 鎖の先頭のローカル位置は「親のワールド位置」からの差にする。
                // 親が無ければワールド原点からの差になる。
                int parent = attachIndex;
                Vector3 parentWorld = parentWorldRoot;

                for (int i = 0; i < worlds.Count; i++)
                {
                    string boneName = i == 0
                        ? $"{chainName}_top"
                        : $"{chainName}_{i}";

                    int added = AddBone(model, boneName, parent, worlds[i] - parentWorld);
                    indices.Add(added);

                    parent = added;
                    parentWorld = worlds[i];
                }

                // 鎖の先は tail 扱いで揺れないので、必要なら 1 本足す。
                if (addTailBone && worlds.Count >= 2)
                {
                    Vector3 dir = worlds[worlds.Count - 1] - worlds[worlds.Count - 2];
                    if (dir.sqrMagnitude <= 1e-10f) dir = new Vector3(0f, -1f, 0f);
                    dir = dir.normalized;

                    float len = tailLength > 0f ? tailLength : 0.05f;
                    int tail = AddBone(model, $"{chainName}_end", parent, dir * len);
                    indices.Add(tail);
                }

                result.Chains.Add(indices);
            }

            // 親の姿勢が確定してから BindPose を入れ直す。
            FixBindPoses(model, result.Chains);

            result.Message =
                $"鎖 {result.Chains.Count} 本 / ボーン {result.BoneCount} 本を作りました。";
            return result;
        }

        // ================================================================
        // 折れ線の組み立て
        // ================================================================

        /// <summary>
        /// 配置の型ごとに、鎖 1 本ぶんの折れ線（取り付け先からの相対）を作る。
        /// 戻り値の先頭が鎖の先頭。
        /// </summary>
        private static List<Vector3> BuildProfile(
            SpringBoneChainLayout layout,
            IReadOnlyList<Vector2> profile,
            int segments, float topRadius, float bottomRadius, float height)
        {
            if (layout == SpringBoneChainLayout.Cylinder)
            {
                // 上の半径から下の半径へ、高さを段数で割って下ろす。
                // 上下の半径を変えると円錐になる。蓋は作らない（ボーンなので面が無い）。
                int n = Mathf.Max(1, segments);
                var list = new List<Vector3>(n + 1);
                for (int i = 0; i <= n; i++)
                {
                    float t = (float)i / n;
                    float r = Mathf.Lerp(topRadius, bottomRadius, t);
                    list.Add(new Vector3(r, -height * t, 0f));
                }
                return list;
            }

            // Single / Revolution は渡された折れ線をそのまま使う。
            if (profile == null) return null;

            // Vector2(X=水平距離, Y=高さ) を XZ 平面へ展開できる形に直す。
            var copy = new List<Vector3>(profile.Count);
            foreach (var p in profile) copy.Add(new Vector3(p.x, p.y, 0f));
            return copy;
        }

        // ================================================================
        // ボーン 1 本
        // ================================================================

        /// <summary>
        /// 親の姿勢が確定してから BindPose を入れ直す。
        /// 鎖を足したあとに 1 回だけ呼ぶ。SpringBoneTestRigBuilder.FixBindPoses と同じ手順。
        /// </summary>
        internal static void FixBindPoses(ModelContext model, IReadOnlyList<List<int>> chains)
        {
            if (model == null || chains == null) return;

            model.ComputeWorldMatrices();

            foreach (var chain in chains)
            {
                if (chain == null) continue;
                foreach (int i in chain)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null) mc.BindPose = mc.WorldMatrix.inverse;
                }
            }
        }

        /// <summary>
        /// ボーンを 1 本足す。作り方は SpringBoneTestRigBuilder.AddBone にそろえる。
        /// BindPose は呼び出し側が全部足したあとに入れ直す。
        /// </summary>
        internal static int AddBone(
            ModelContext model, string name, int parentIndex, Vector3 localPos)
        {
            var bt = new BoneTransform
            {
                Position          = localPos,
                Rotation          = Vector3.zero,
                Scale             = Vector3.one,
                UseLocalTransform = true,
                HasBoneTransform  = true,
            };

            var mo = new MeshObject(name)
            {
                Type                 = MeshType.Bone,
                HierarchyParentIndex = parentIndex,
                BoneTransform        = bt,
            };

            var mc = new MeshContext
            {
                MeshObject           = mo,
                Name                 = name,
                Type                 = MeshType.Bone,
                IsVisible            = true,
                BindPose             = Matrix4x4.identity,
                BoneTransform        = bt,
                HierarchyParentIndex = parentIndex,
                BonePoseData         = new BonePoseData { IsActive = true },
            };

            return model.Add(mc);
        }

        /// <summary>
        /// 接頭辞で作った鎖を集め直す。名前の規則は Place が決めたものと同じ
        /// （prefix_NN_top / prefix_NN_i / prefix_NN_end、1 本なら prefix_top …）。
        ///
        /// コマンドは戻り値でボーン索引を返さないので、作った直後に
        /// 揺れ方を掛けたい呼び出し側はここで引き直す。
        /// </summary>
        public static List<List<int>> CollectByPrefix(ModelContext model, string prefix)
        {
            var result = new List<List<int>>();
            if (model == null || string.IsNullOrEmpty(prefix)) return result;

            var childrenOf = Poly_Ling.Ops.MeshHierarchyOps.BuildChildrenTable(model);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (string.IsNullOrEmpty(mc.Name)) continue;
                if (!mc.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!mc.Name.EndsWith("_top", StringComparison.Ordinal)) continue;

                var chain = new List<int>();
                int cur = i;
                var seen = new HashSet<int>();
                while (cur >= 0 && seen.Add(cur))
                {
                    chain.Add(cur);

                    int next = -1;
                    if (childrenOf.TryGetValue(cur, out var kids))
                        foreach (int k in kids)
                        {
                            var kmc = model.GetMeshContext(k);
                            if (kmc == null || kmc.Type != MeshType.Bone) continue;
                            if (string.IsNullOrEmpty(kmc.Name)) continue;
                            if (!kmc.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                            next = k;
                            break;
                        }
                    cur = next;
                }

                result.Add(chain);
            }

            return result;
        }

        private static Vector3 Origin(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);
    }
}
