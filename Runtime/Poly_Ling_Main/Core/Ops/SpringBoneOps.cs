// Runtime/Poly_Ling_Main/Core/Ops/SpringBoneOps.cs
// ============================================================
// 揺れもの（VRM SpringBone）のオーサリング処理
// ============================================================
//
// 【なぜ要るか】
//   SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders を書き込む
//   経路が BuildSpringBoneTestRigCommand（検証用ダミー装備の一括生成）しか
//   無かった。既存のボーンへ揺れを付ける・外す・直す手段がないため、
//   CSV で持つモデル以外は揺れデータを持てなかった。
//
// 【付帯先はボーンに限らない】
//   格納規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   揺れデータだけは例外で、階層に載るノードなら付けられる（IsCarrier 参照）。
//   VRM の joint / collider はノード索引を指すだけで、スキン関節である必要が
//   ないため（UniVRM の ModelExporter は階層の全 Transform をノードにする）。
//
// 【チェーンの形はここでは持たない】
//   ジョイントの集合・順序は保持せず、階層と SpringBoneJoint の有無から
//   導出する（SpringBoneChainData.cs の規約）。本 Ops も同じ規則で辿る。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。UnityEngine の型のみ使う。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>階層を辿って鎖を集めるときの辿り方。</summary>
    public enum SpringBoneChainWalk
    {
        /// <summary>
        /// 第 1 子だけを辿って一本道にする。分岐したらそこで止める。
        /// VRM の 1 チェーンは分岐できないため、既定はこちら。
        /// </summary>
        FirstChild = 0,

        /// <summary>分岐も含めて子孫を全部集める。枝ごとに別チェーンとして出る。</summary>
        AllDescendants = 1,
    }

    /// <summary>検査で見つけた不具合 1 件。</summary>
    public sealed class SpringBoneIssue
    {
        /// <summary>対象の MeshContextList 索引。モデル全体の指摘は -1。</summary>
        public int MasterIndex = -1;

        /// <summary>人へ出す文。</summary>
        public string Message = "";

        /// <summary>出力が落ちる（＝直さないと VRM に出ない）なら true。</summary>
        public bool IsError;
    }

    /// <summary>揺れもののオーサリング処理。</summary>
    public static class SpringBoneOps
    {
        /// <summary>
        /// 末端ボーンの既定の長さ[m]。
        /// UniVRM の VRM0 → VRM1 移行が 7cm を足す（MigrationVrmSpringBone）。
        /// VRChat PhysBone の Endpoint Position は 2〜5cm が実務値。
        /// 揺れの見た目が変わらない範囲で短い方を既定にする。
        /// </summary>
        public const float DefaultTailLength = 0.05f;

        /// <summary>末端ボーンの既定の接尾辞。</summary>
        public const string DefaultTailSuffix = "_end";

        // ================================================================
        // 付帯先になれるか
        // ================================================================

        /// <summary>
        /// 揺れデータを付けられるノードか。
        ///
        /// 【ボーン】
        ///   常に付けられる。Armature の下に Transform が作られる。
        ///
        /// 【描画オブジェクト】
        ///   非スキンドのものだけ。スキンドは HierarchyBuilder が
        ///   ルート直下へ置く（親を解決するのは !isSkinned の枝だけ）ため、
        ///   親子の鎖にならず、揺らしても意味がない。
        ///
        /// 【それ以外】
        ///   モーフ・剛体・JOINT・グループは GameObject を持たないので不可。
        /// </summary>
        public static bool IsCarrier(MeshContext mc)
        {
            if (mc?.MeshObject == null) return false;
            if (mc.Type == MeshType.Bone) return true;
            if (mc.Type == MeshType.Mesh) return !mc.IsSkinned;
            return false;
        }

        /// <summary>索引が付帯先になれるか。</summary>
        public static bool IsCarrier(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return false;
            return IsCarrier(model.GetMeshContext(index));
        }

        // ================================================================
        // モデルレベル：コライダーグループ
        // ================================================================

        /// <summary>
        /// グループ名を確保して索引を返す。同名があればその索引を返す。
        /// 名前が空なら通し番号で作る。
        /// </summary>
        public static int EnsureGroup(ModelContext model, string name)
        {
            if (model == null) return -1;
            if (model.SpringBoneColliderGroupNames == null)
                model.SpringBoneColliderGroupNames = new List<string>();

            var names = model.SpringBoneColliderGroupNames;

            if (string.IsNullOrEmpty(name))
                name = "ColliderGroup_" + names.Count.ToString();

            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.Ordinal)) return i;

            names.Add(name);
            return names.Count - 1;
        }

        /// <summary>グループ名を変える。索引は動かないので参照は影響を受けない。</summary>
        public static bool RenameGroup(ModelContext model, int groupIndex, string newName)
        {
            var names = model?.SpringBoneColliderGroupNames;
            if (names == null) return false;
            if (groupIndex < 0 || groupIndex >= names.Count) return false;
            if (string.IsNullOrEmpty(newName)) return false;

            names[groupIndex] = newName;
            return true;
        }

        /// <summary>
        /// グループを消して、全ノードの参照索引を詰め直す。
        ///
        /// 【なぜ詰め直しが要るか】
        ///   所属は名前ではなく SpringBoneColliderGroupNames への索引で持つ
        ///   （SpringBoneChainData.cs / SpringBoneColliderData.cs の規約）。
        ///   名前リストから 1 件抜くと、後ろの索引が全部 1 つずれる。
        ///   放置すると、チェーンもコライダーも別のグループを指し始める。
        ///
        /// 【消したグループにしか属さないコライダーは残す】
        ///   所属が空になるだけで、コライダー自体は付帯ボーンの持ち物。
        ///   黙って消すと、グループを作り直せば戻せるはずのものが失われる。
        ///   （生成物の後始末である SpringBoneTestRigBuilder.RemoveGenerated は
        ///     消す側だが、あれは自分で作ったものを畳む処理で用途が違う。）
        /// </summary>
        public static bool DeleteGroup(ModelContext model, int groupIndex)
        {
            var names = model?.SpringBoneColliderGroupNames;
            if (names == null) return false;
            if (groupIndex < 0 || groupIndex >= names.Count) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mo = model.GetMeshContext(i)?.MeshObject;
                if (mo == null) continue;

                if (mo.SpringBoneColliders != null)
                    foreach (var c in mo.SpringBoneColliders)
                        RemoveAndShift(c?.SpringBoneGroupIndices, groupIndex);

                RemoveAndShift(mo.SpringBoneChainRoot?.SpringBoneColliderGroupIndices, groupIndex);
            }

            names.RemoveAt(groupIndex);
            return true;
        }

        /// <summary>索引リストから removed を抜き、それより大きい索引を 1 つ詰める。</summary>
        private static void RemoveAndShift(List<int> indices, int removed)
        {
            if (indices == null) return;

            for (int i = indices.Count - 1; i >= 0; i--)
            {
                if (indices[i] == removed)      indices.RemoveAt(i);
                else if (indices[i] >  removed) indices[i] = indices[i] - 1;
            }
        }

        // ================================================================
        // ノード付帯：チェーンルート
        // ================================================================

        /// <summary>
        /// 指定ノードを揺れチェーンの起点にする。
        ///
        /// 【ジョイントを同時に付ける理由】
        ///   VRM 出力側は、ルートに SpringBoneJoint が無いチェーンを
        ///   警告付きで捨てる（Vrm10SceneAssembler が joint 表を引けないため）。
        ///   「起点にした」のに出力されない状態を作らない。
        /// </summary>
        public static bool SetChainRoot(
            ModelContext model, int index,
            string chainName, string centerBoneName, IReadOnlyList<int> groupIndices,
            out string reason)
        {
            reason = "";

            if (!IsCarrier(model, index))
            {
                reason = "揺れデータを付けられないオブジェクトです（ボーンか非スキンドの描画オブジェクトのみ）。";
                return false;
            }

            var mc = model.GetMeshContext(index);
            var mo = mc.MeshObject;

            var groups = new List<int>();
            int groupCount = model.SpringBoneColliderGroupNames?.Count ?? 0;
            if (groupIndices != null)
                foreach (int g in groupIndices)
                    if (g >= 0 && g < groupCount && !groups.Contains(g)) groups.Add(g);

            mo.SpringBoneChainRoot = new SpringBoneChainData
            {
                Name = string.IsNullOrEmpty(chainName) ? (mc.Name ?? "") : chainName,
                CenterBoneName = centerBoneName ?? "",
                SpringBoneColliderGroupIndices = groups,
            };

            // ルートにジョイントが無いチェーンは出力されない。既定値で補う。
            if (mo.SpringBoneJoint == null)
                mo.SpringBoneJoint = new SpringBoneJointData();

            return true;
        }

        /// <summary>チェーンの起点指定を外す。ジョイントは残す。</summary>
        public static int ClearChainRoot(ModelContext model, IEnumerable<int> indices)
        {
            if (model == null || indices == null) return 0;

            int done = 0;
            foreach (int i in indices)
            {
                if (i < 0 || i >= model.MeshContextCount) continue;
                var mo = model.GetMeshContext(i)?.MeshObject;
                if (mo?.SpringBoneChainRoot == null) continue;

                mo.SpringBoneChainRoot = null;
                done++;
            }
            return done;
        }

        // ================================================================
        // ノード付帯：ジョイント
        // ================================================================

        /// <summary>
        /// 選んだノードへジョイントを付ける（既にあれば値を上書きする）。
        /// 付帯先になれないものは黙って飛ばし、件数だけ返す。
        /// </summary>
        public static int SetJoint(
            ModelContext model, IEnumerable<int> indices,
            float hitRadius, float stiffnessForce, float gravityPower,
            Vector3 gravityDir, float dragForce)
        {
            return SetJoint(model, indices,
                hitRadius, stiffnessForce, gravityPower, gravityDir, dragForce,
                SpringBoneAngleLimitType.None, Quaternion.identity, Mathf.PI, 0f);
        }

        /// <summary>
        /// 角度制限（VRMC_springBone_limit）まで含めてジョイントを付ける。
        ///
        /// 【値の丸め】
        ///   Pitch は 0〜π、Yaw は 0〜π/2 に丸める。VRM の仕様が
        ///   それを超える値を上限として解釈するため、保存側でそろえておく。
        ///   LimitRotation は長さ 0 のときだけ無回転に直す。
        /// </summary>
        public static int SetJoint(
            ModelContext model, IEnumerable<int> indices,
            float hitRadius, float stiffnessForce, float gravityPower,
            Vector3 gravityDir, float dragForce,
            SpringBoneAngleLimitType angleLimitType, Quaternion limitRotation,
            float pitch, float yaw)
        {
            if (model == null || indices == null) return 0;

            Quaternion rot = limitRotation;
            if (rot.x * rot.x + rot.y * rot.y + rot.z * rot.z + rot.w * rot.w < 1e-12f)
                rot = Quaternion.identity;

            int done = 0;
            foreach (int i in indices)
            {
                if (!IsCarrier(model, i)) continue;

                model.GetMeshContext(i).MeshObject.SpringBoneJoint = new SpringBoneJointData
                {
                    HitRadius      = Mathf.Max(0f, hitRadius),
                    StiffnessForce = Mathf.Max(0f, stiffnessForce),
                    GravityPower   = gravityPower,
                    GravityDir     = gravityDir.sqrMagnitude > 1e-12f ? gravityDir : new Vector3(0f, -1f, 0f),
                    DragForce      = Mathf.Clamp01(dragForce),
                    AngleLimitType = angleLimitType,
                    LimitRotation  = rot,
                    Pitch          = Mathf.Clamp(pitch, 0f, Mathf.PI),
                    Yaw            = Mathf.Clamp(yaw,   0f, Mathf.PI * 0.5f),
                };
                done++;
            }
            return done;
        }

        /// <summary>
        /// ジョイントを外す。チェーンルートが付いたままのノードから外すと
        /// そのチェーンが出力されなくなるので、ルート指定も一緒に外す。
        /// </summary>
        public static int ClearJoint(ModelContext model, IEnumerable<int> indices)
        {
            if (model == null || indices == null) return 0;

            int done = 0;
            foreach (int i in indices)
            {
                if (i < 0 || i >= model.MeshContextCount) continue;
                var mo = model.GetMeshContext(i)?.MeshObject;
                if (mo?.SpringBoneJoint == null) continue;

                mo.SpringBoneJoint = null;
                mo.SpringBoneChainRoot = null;
                done++;
            }
            return done;
        }

        // ================================================================
        // 階層を辿る
        // ================================================================

        /// <summary>
        /// 起点から階層を辿って鎖を集める。戻り値の先頭は必ず起点。
        /// 付帯先になれない枝は辿らない（そこで打ち切る）。
        /// </summary>
        public static List<int> CollectChain(
            ModelContext model, int rootIndex, SpringBoneChainWalk walk)
        {
            var result = new List<int>();
            if (!IsCarrier(model, rootIndex)) return result;

            var childrenOf = MeshHierarchyOps.BuildChildrenTable(model);
            var visited = new HashSet<int>();

            if (walk == SpringBoneChainWalk.FirstChild)
            {
                int cur = rootIndex;
                while (cur >= 0 && visited.Add(cur))
                {
                    result.Add(cur);

                    int next = -1;
                    if (childrenOf.TryGetValue(cur, out var kids))
                    {
                        foreach (int c in kids)
                        {
                            if (!IsCarrier(model, c)) continue;
                            next = c;
                            break;   // 第 1 子だけ。分岐は追わない
                        }
                    }
                    cur = next;
                }
                return result;
            }

            // 子孫を全部
            var stack = new Stack<int>();
            stack.Push(rootIndex);
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                if (!visited.Add(cur)) continue;
                result.Add(cur);

                if (!childrenOf.TryGetValue(cur, out var kids)) continue;

                // 元の並び順で出したいので逆順に積む
                for (int k = kids.Count - 1; k >= 0; k--)
                    if (IsCarrier(model, kids[k])) stack.Push(kids[k]);
            }

            result.Sort();
            return result;
        }

        /// <summary>
        /// 描画オブジェクトの頂点に効いているボーンを集める。
        ///
        /// 【対象頂点】
        ///   頂点選択があればその頂点だけ、無ければ全頂点。
        ///   「スカートのボーンをまとめて選ぶ」用途で、面を選んでから
        ///   絞り込めるようにするための規則。
        /// </summary>
        public static List<int> CollectBonesByVertexWeight(
            ModelContext model, IEnumerable<int> meshIndices, float minWeight)
        {
            var result = new List<int>();
            if (model == null || meshIndices == null) return result;

            var found = new HashSet<int>();

            foreach (int mi in meshIndices)
            {
                if (mi < 0 || mi >= model.MeshContextCount) continue;

                var mc = model.GetMeshContext(mi);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                HashSet<int> targetVerts = null;
                if (mc.SelectedVertices != null && mc.SelectedVertices.Count > 0)
                    targetVerts = mc.SelectedVertices;

                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    if (targetVerts != null && !targetVerts.Contains(v)) continue;

                    var vert = mo.Vertices[v];
                    if (vert == null || !vert.HasBoneWeight) continue;

                    var bw = vert.BoneWeight.Value;
                    Take(model, found, bw.boneIndex0, bw.weight0, minWeight);
                    Take(model, found, bw.boneIndex1, bw.weight1, minWeight);
                    Take(model, found, bw.boneIndex2, bw.weight2, minWeight);
                    Take(model, found, bw.boneIndex3, bw.weight3, minWeight);
                }
            }

            result.AddRange(found);
            result.Sort();
            return result;
        }

        private static void Take(
            ModelContext model, HashSet<int> found, int boneIndex, float weight, float minWeight)
        {
            if (weight < minWeight) return;
            if (boneIndex < 0 || boneIndex >= model.MeshContextCount) return;

            var mc = model.GetMeshContext(boneIndex);
            if (mc == null || mc.Type != MeshType.Bone) return;

            found.Add(boneIndex);
        }

        // ================================================================
        // 末端ボーン
        // ================================================================

        /// <summary>
        /// 末端に子ボーンを 1 本足す。
        ///
        /// 【なぜ要るか】
        ///   VRM 1.0 の鎖は末端の joint を tail として扱い、これ自体は揺れない
        ///   （UniVRM の FastSpringBoneBuffer は joints.Length - 1 までしか回さない）。
        ///   さらにボーン軸を「次の joint の親からのローカル位置」から取るので、
        ///   子の無いボーンで鎖を終えると軸が定まらず、実質 1 段短くなる。
        ///   UniVRM の VRM0 → VRM1 移行が末端へノードを足しているのと同じ手当て。
        ///
        /// 【向き】
        ///   親 → 自分のワールド方向を伸ばす。親が取れない・長さが 0 のときは
        ///   自分のローカル位置の向き、それも 0 なら真下。
        ///
        /// 【ボーンだけを相手にする】
        ///   非ボーンのノードに子ボーンを足すと、HierarchyBuilder のボーン親解決
        ///   （boneTransformMap を引く 2 パス目）が空振りして Armature 直下へ
        ///   逃げ、鎖が切れる。非ボーンの末端はオブジェクト階層側で足すこと。
        /// </summary>
        public static int AddTailBone(
            ModelContext model, int index, float length, string nameSuffix, bool addJoint,
            out string reason)
        {
            reason = "";

            if (model == null || index < 0 || index >= model.MeshContextCount)
            {
                reason = "対象がありません。";
                return -1;
            }

            var mc = model.GetMeshContext(index);
            if (mc == null || mc.Type != MeshType.Bone)
            {
                reason = "末端ボーンを足せるのはボーンだけです。";
                return -1;
            }

            var childrenOf = MeshHierarchyOps.BuildChildrenTable(model);
            if (childrenOf.TryGetValue(index, out var kids))
            {
                foreach (int c in kids)
                {
                    var cmc = model.GetMeshContext(c);
                    if (cmc != null && cmc.Type == MeshType.Bone)
                    {
                        reason = $"\"{mc.Name}\" は末端ではありません（子ボーンがあります）。";
                        return -1;
                    }
                }
            }

            if (length <= 0f) length = DefaultTailLength;
            if (string.IsNullOrEmpty(nameSuffix)) nameSuffix = DefaultTailSuffix;

            // 向きを決める。
            Vector3 selfWorld = Origin(mc.WorldMatrix);
            Vector3 dir = Vector3.zero;

            int parentIndex = mc.HierarchyParentIndex;
            if (parentIndex >= 0 && parentIndex < model.MeshContextCount)
            {
                var pmc = model.GetMeshContext(parentIndex);
                if (pmc != null) dir = selfWorld - Origin(pmc.WorldMatrix);
            }

            if (dir.sqrMagnitude <= 1e-10f && mc.BoneTransform != null)
                dir = mc.BoneTransform.Position;

            dir = dir.sqrMagnitude <= 1e-10f ? new Vector3(0f, -1f, 0f) : dir.normalized;

            Vector3 localStep = dir * length;

            string name = (mc.Name ?? "bone") + nameSuffix;

            var bt = new BoneTransform
            {
                Position          = localStep,
                Rotation          = Vector3.zero,
                Scale             = Vector3.one,
                UseLocalTransform = true,
                HasBoneTransform  = true,
            };

            // 構築の仕方は SpringBoneTestRigBuilder.AddBone にそろえる。
            //   MeshObject を先に入れてから Name を入れる（MeshContext.Name の規約）。
            var tailMo = new MeshObject(name)
            {
                Type                 = MeshType.Bone,
                HierarchyParentIndex = index,
                BoneTransform        = bt,
            };

            if (addJoint)
            {
                // 末端 joint の値は使われない（tail 扱い）が、
                // 無いと鎖がここで終わらず 1 段短くなる。根元の値を写す。
                var src = mc.MeshObject?.SpringBoneJoint;
                tailMo.SpringBoneJoint = src != null ? src.Clone() : new SpringBoneJointData();
            }

            var tailMc = new MeshContext
            {
                MeshObject           = tailMo,
                Name                 = name,
                Type                 = MeshType.Bone,
                IsVisible            = true,
                BindPose             = Matrix4x4.identity,
                BoneTransform        = bt,
                HierarchyParentIndex = index,
                BonePoseData         = new BonePoseData { IsActive = true },
            };

            int added = model.Add(tailMc);

            // 親の姿勢が確定してからでないと BindPose を入れられない。
            model.ComputeWorldMatrices();
            var addedMc = model.GetMeshContext(added);
            if (addedMc != null) addedMc.BindPose = addedMc.WorldMatrix.inverse;

            return added;
        }

        private static Vector3 Origin(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);

        // ================================================================
        // 検査
        // ================================================================

        /// <summary>
        /// 揺れデータの不具合を集める。パネルの警告表示に使う。
        ///
        /// 見る観点は VRM 出力側・UniVRM ランタイム側が実際に落とす条件にそろえる。
        ///   1. ルートにジョイントが無い          → チェーンごと出力されない
        ///   2. 鎖のジョイントが 1 個しかない      → 末端は tail 扱いなので何も揺れない
        ///   3. 末端の 1 つ手前と末端が同じ位置    → ボーン軸が定まらない
        ///   4. 分岐している                       → 経路ごとに別チェーンへ割れる
        ///   5. 1 ノードが複数のチェーンに属する   → どちらの鎖として動くか決まらない
        ///   6. 付帯先になれないノードに付いている → 出力されない
        /// </summary>
        public static List<SpringBoneIssue> Validate(ModelContext model)
        {
            var issues = new List<SpringBoneIssue>();
            if (model == null) return issues;

            var childrenOf = MeshHierarchyOps.BuildChildrenTable(model);

            // どのチェーンに属したかを数える（重複所属の検出用）。
            var chainOwnerCount = new Dictionary<int, int>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                bool hasJoint = mo.SpringBoneJoint != null;
                bool hasChain = mo.SpringBoneChainRoot != null;
                bool hasCollider = mo.SpringBoneColliders != null && mo.SpringBoneColliders.Count > 0;

                if (!hasJoint && !hasChain && !hasCollider) continue;

                if (!IsCarrier(mc))
                {
                    issues.Add(new SpringBoneIssue
                    {
                        MasterIndex = i,
                        IsError = true,
                        Message = $"\"{mc.Name}\" は揺れデータを持てません"
                                + "（ボーンか非スキンドの描画オブジェクトのみ）。出力されません。",
                    });
                    continue;
                }

                if (!hasChain) continue;

                if (!hasJoint)
                {
                    issues.Add(new SpringBoneIssue
                    {
                        MasterIndex = i,
                        IsError = true,
                        Message = $"チェーン \"{ChainName(mo, mc)}\" のルート \"{mc.Name}\" に"
                                + "ジョイントがありません。出力されません。",
                    });
                    continue;
                }

                var members = CollectJointMembers(model, childrenOf, i);
                foreach (int m in members)
                    chainOwnerCount[m] = (chainOwnerCount.TryGetValue(m, out int n) ? n : 0) + 1;

                if (members.Count < 2)
                {
                    issues.Add(new SpringBoneIssue
                    {
                        MasterIndex = i,
                        IsError = true,
                        Message = $"チェーン \"{ChainName(mo, mc)}\" のジョイントが 1 個だけです。"
                                + "末端は tail 扱いで揺れないため、末端ボーンを足してください。",
                    });
                }

                int branchCount = CountBranches(model, childrenOf, i);
                if (branchCount > 1)
                {
                    issues.Add(new SpringBoneIssue
                    {
                        MasterIndex = i,
                        IsError = false,
                        Message = $"チェーン \"{ChainName(mo, mc)}\" が {branchCount} 本に分岐しています。"
                                + "VRM のチェーンは分岐できないため、経路ごとに別チェーンとして出ます。",
                    });
                }

                foreach (int m in members)
                {
                    var jmc = model.GetMeshContext(m);
                    if (jmc == null) continue;
                    if (m == i) continue;

                    if (jmc.BoneTransform != null &&
                        jmc.BoneTransform.Position.sqrMagnitude <= 1e-10f)
                    {
                        issues.Add(new SpringBoneIssue
                        {
                            MasterIndex = m,
                            IsError = true,
                            Message = $"\"{jmc.Name}\" が親と同じ位置にあります。"
                                    + "ボーン軸が定まらないため、その段は揺れません。",
                        });
                    }
                }
            }

            foreach (var kv in chainOwnerCount)
            {
                if (kv.Value < 2) continue;
                var mc = model.GetMeshContext(kv.Key);
                issues.Add(new SpringBoneIssue
                {
                    MasterIndex = kv.Key,
                    IsError = false,
                    Message = $"\"{mc?.Name}\" が {kv.Value} 本のチェーンに属しています。"
                            + "どちらの鎖として動くかが決まりません。",
                });
            }

            return issues;
        }

        /// <summary>ルートから、ジョイントを持つ子孫だけを辿って集める（ルートを含む）。</summary>
        public static List<int> CollectJointMembers(
            ModelContext model, Dictionary<int, List<int>> childrenOf, int rootIndex)
        {
            var result = new List<int>();
            if (model == null) return result;

            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(rootIndex);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                if (!visited.Add(cur)) continue;

                var mo = model.GetMeshContext(cur)?.MeshObject;
                if (mo?.SpringBoneJoint == null) continue;

                result.Add(cur);

                if (childrenOf != null && childrenOf.TryGetValue(cur, out var kids))
                    for (int k = kids.Count - 1; k >= 0; k--)
                        stack.Push(kids[k]);
            }

            result.Sort();
            return result;
        }

        /// <summary>ルートから葉までの経路が何本になるかを数える。</summary>
        private static int CountBranches(
            ModelContext model, Dictionary<int, List<int>> childrenOf, int rootIndex)
        {
            int leaves = 0;
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(rootIndex);

            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                if (!visited.Add(cur)) continue;

                int next = 0;
                if (childrenOf != null && childrenOf.TryGetValue(cur, out var kids))
                {
                    foreach (int c in kids)
                    {
                        var mo = model.GetMeshContext(c)?.MeshObject;
                        if (mo?.SpringBoneJoint == null) continue;
                        stack.Push(c);
                        next++;
                    }
                }

                if (next == 0) leaves++;
            }

            return leaves;
        }

        /// <summary>チェーン名。空ならノード名で代用する（VRM 出力側と同じ規則）。</summary>
        public static string ChainName(MeshObject mo, MeshContext mc)
        {
            string n = mo?.SpringBoneChainRoot?.Name;
            if (!string.IsNullOrEmpty(n)) return n;
            return string.IsNullOrEmpty(mc?.Name) ? "Spring" : mc.Name;
        }
    }
}
