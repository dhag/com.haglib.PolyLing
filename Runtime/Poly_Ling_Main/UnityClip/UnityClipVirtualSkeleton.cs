// UnityClipVirtualSkeleton.cs
// Unity クリップ適用用の「ボーンノード表」を作る。
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
//
// ============================================================
// ■ 何のために要るか
//   UnityClipApplier は従来 model.Bones（MeshType.Bone）だけを対象にしていた。
//   MeshFilter ＋ ミラーのまま骨格を持つモデル（MQO 由来の半身モデル）は
//   MeshType.Bone を 1 件も持たないため、対象が空になり一切適用できなかった。
//
//   さらに半身モデルでは、右半身の関節そのものが存在しない。
//   ミラー側コンテキスト（MirrorSide / BakedMirror）は
//   MirrorBranchOps.CreateDerivedMirrorContext が頂点ゼロで null を返すため、
//   頂点を持つメッシュにしか作られないからである。
//   HierarchyExportWindow はこれを「ミラー枝に関節を複製する」ことで解いている
//   （CreateMeshGameObject(mirror:true) / 名前に "+" を付ける）。
//   ここでは同じ規則で、モデルを改変せずにノード表の上だけで複製する。
//
// ============================================================
// ■ ミラー側の姿勢の解き方（重要・削除禁止）
//
//   ModelContext.ComputeWorldMatrices は、MirrorGeometryDerived なミラー側の
//   実効ワールドを共役 S·H·S で解く。H はミラー側自身の階層ワールドで、
//   SyncDerivedMirrorTransforms によって実体側と同じ BoneTransform・同じ階層親を
//   持たされている（H_M = H_R）。
//
//   よってミラー側を独立に動かすには、H_M 側に差分を入れるしかない。
//   BonePoseData は SyncDerivedMirrorTransforms の同期対象外なので、
//   ここへデルタを入れれば実体側と独立した H_M を作れる。
//
//   仮想鎖は「共役を掛ける前の枠（＝H の枠）」で組む。
//     Ĥ_j = Ĥ_parent · B_j · D̃_j
//       B_j … 実体側のレスト・ローカル（ミラー側は同値）
//       D̃_j … ミラー側デルタ（下記）
//     境界（ミラー枝ルートの親）の Ĥ は実体側の WorldMatrix をそのまま使う。
//
//   D̃ と「右半身自身の枠で見たデルタ D」の関係:
//     実際のワールド      = S·Ĥ_j·S = (S·Ĥ_parent·B_j·S)·(S·D̃_j·S)
//     レストのワールド    = S·Ĥ_parent·B_j·S
//     よって  S·D̃_j·S = D   すなわち  D̃_j = S·D·S
//   S は対合（S² = I）なので往復できる。
//
//   この組み方の利点は、D＝単位のとき Ĥ_j が実体側の階層ワールドと
//   ビット単位で同じ積になること。つまり「デルタ無し ⇒ 現行表示と完全一致」が
//   構造的に保証され、モデルの体幹姿勢に依存しない。
//   （ローカル TRS を鏡像化してから積む書き方＝エクスポータの GameObject 版は、
//     体幹が回っていると S·H_p ≠ H_p·S のぶんだけ現行表示とずれる。
//     エクスポータは静止プレファブを作るので問題にならないが、
//     ここでは現行表示との連続性が要るため共役枠で組む。）
//
//   レスト量（RestL / RestW / レスト位置）は、右半身自身の枠で見た値が要る。
//   これは実体側の値を S で共役したものに一致する:
//     RestL_M = S·RestL_R·S,  RestW_M = S·RestW_R·S,  pos_M = S·pos_R
//   回転の共役はミラー軸以外の 2 成分の符号反転（MirrorBranchOps.MirrorLocalTRS
//   の rot 規則と同一）。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.UnityClip
{
    /// <summary>クリップ適用対象のボーン 1 本。実体・仮想ミラーの両方を表す。</summary>
    public sealed class ClipBoneNode
    {
        /// <summary>ノード名。仮想ミラーは実体名＋"+"（MirrorBranchOps.MirrorBranchSuffix）。</summary>
        public string Name;

        /// <summary>MeshContextList 索引。-1 = 実体を持たない純仮想ノード。</summary>
        public int ContextIndex = -1;

        /// <summary>姿勢・名前の由来となる実体側 MeshContextList 索引。</summary>
        public int SourceContextIndex = -1;

        /// <summary>ミラー側ノードか（純仮想関節・ミラー側コンテキストの両方）。</summary>
        public bool IsMirror;

        /// <summary>仮想鎖での親ノード索引。-1 = 親は実体ノード（境界）。</summary>
        public int ParentNode = -1;

        /// <summary>ParentNode &lt; 0 のときの親 MeshContextList 索引。-1 = ルート。</summary>
        public int ParentContextIndex = -1;

        /// <summary>ミラー軸（1=X / 2=Y / 4=Z）。実体側の設定が正本。</summary>
        public int MirrorAxis = 1;
    }

    /// <summary>
    /// ModelContext からクリップ適用用のノード表を作る。
    /// モデルは一切書き換えない（読み取りのみ）。
    /// </summary>
    public sealed class UnityClipVirtualSkeleton
    {
        /// <summary>ノード列。前半が実体ノード、後半が仮想ミラーノード。</summary>
        public List<ClipBoneNode> Nodes { get; } = new List<ClipBoneNode>();

        /// <summary>ノード名の一覧（Nodes と同じ並び）。あいまい照合のフォールバック用。</summary>
        public List<string> NodeNames { get; } = new List<string>();

        /// <summary>Humanoid 名（空白除去）→ ノード索引。</summary>
        public Dictionary<string, int> HumanoidToNode { get; } = new Dictionary<string, int>();

        /// <summary>MeshContextList 索引 → ノード索引（実体ノードのみ）。</summary>
        private readonly Dictionary<int, int> _nodeOfContext = new Dictionary<int, int>();

        /// <summary>実体側 MeshContextList 索引 → ミラーノード索引。</summary>
        private readonly Dictionary<int, int> _mirrorNodeOfSource = new Dictionary<int, int>();

        /// <summary>MeshType.Bone を持たない＝MeshFilter ツリーを骨格として使うモードか。</summary>
        public bool MeshFilterSkeleton { get; private set; }

        /// <summary>仮想ミラーノード数（純仮想＋ミラー側コンテキスト）。</summary>
        public int MirrorNodeCount { get; private set; }

        /// <summary>実体ノード数。</summary>
        public int RealNodeCount { get; private set; }

        /// <summary>左右名を入れ替えて補完した Humanoid 名の一覧（診断用）。</summary>
        public List<string> SupplementedHumanoidNames { get; } = new List<string>();

        // ================================================================
        // 構築
        // ================================================================

        public static UnityClipVirtualSkeleton Build(ModelContext model)
        {
            var sk = new UnityClipVirtualSkeleton();
            if (model == null || model.MeshContextList == null) return sk;

            sk.BuildRealNodes(model);
            sk.BuildMirrorNodes(model);
            sk.BuildHumanoidMap(model);
            return sk;
        }

        // ── 実体ノード ────────────────────────────────────────────────
        //   MeshType.Bone があればそれだけを使う（従来どおり・回帰なし）。
        //   1 件も無いモデルは MeshFilter ツリーを骨格として扱い、
        //   MeshType.Mesh を実体ノードにする。ミラー側は実体ノードにしない
        //   （ミラーノードとして別枠で入れる）。
        private void BuildRealNodes(ModelContext model)
        {
            var list = model.MeshContextList;

            var bones = model.Bones;
            if (bones != null && bones.Count > 0)
            {
                MeshFilterSkeleton = false;
                foreach (var entry in bones)
                {
                    int mi = entry.MasterIndex;
                    if (mi < 0 || mi >= list.Count) continue;
                    var ctx = list[mi];
                    if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;
                    AddRealNode(ctx, mi);
                }
            }
            else
            {
                MeshFilterSkeleton = true;
                for (int i = 0; i < list.Count; i++)
                {
                    var ctx = list[i];
                    if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;
                    if (ctx.Type != MeshType.Mesh) continue;   // ミラー側は下で入れる
                    AddRealNode(ctx, i);
                }
            }

            RealNodeCount = Nodes.Count;
        }

        private void AddRealNode(MeshContext ctx, int index)
        {
            var node = new ClipBoneNode
            {
                Name               = ctx.Name,
                ContextIndex       = index,
                SourceContextIndex = index,
                IsMirror           = false,
                ParentNode         = -1,
                ParentContextIndex = ctx.HierarchyParentIndex,
                MirrorAxis         = ctx.MirrorAxis
            };
            _nodeOfContext[index] = Nodes.Count;
            Nodes.Add(node);
            NodeNames.Add(node.Name);
        }

        // ── ミラーノード ──────────────────────────────────────────────
        //   (a) ミラー側コンテキスト（MirrorSide / BakedMirror）… 実体を持つ
        //   (b) ミラー分岐内で頂点ゼロの関節       … 純仮想（実体なし）
        //   親の解決は MirrorBranchOps.TryResolveMirrorParent と同じ規則。
        private void BuildMirrorNodes(ModelContext model)
        {
            if (!MeshFilterSkeleton) return;   // スキンド側にミラー枝は無い

            var list  = model.MeshContextList;
            var peers = MirrorPeerIndex.Build(model);

            // ミラー分岐（IsMirrorBranchRoot 配下）。親は HierarchyParentIndex を正本にする
            // （MeshFilterToSkinnedConverter と同じ規約）。
            var branchSide = MirrorBranchOps.AnalyzeMirrorBranches(model, null);

            // 並び順には依存しない。仮想鎖のワールドは適用側が親を先に解く
            // 再帰＋メモ化で求めるため、ここでは並べ替えを行わない。
            var pending = new List<ClipBoneNode>();

            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                if (ctx == null) continue;

                if (MirrorBranchOps.IsMirrorSideContext(ctx))
                {
                    // (a) 実体を持つミラー側。姿勢の正本は実体側相方。
                    if (!peers.TryGetReal(i, out int src) || src < 0 || src >= list.Count) continue;
                    var real = list[src];
                    if (real == null) continue;

                    var node = new ClipBoneNode
                    {
                        Name               = ctx.Name,
                        ContextIndex       = i,
                        SourceContextIndex = src,
                        IsMirror           = true,
                        MirrorAxis         = real.MirrorAxis
                    };
                    _mirrorNodeOfSource[src] = -1;   // 予約（索引は確定後に入れる）
                    pending.Add(node);
                    continue;
                }

                // (b) ミラー枝内の関節（頂点ゼロ）を両側に複製する。
                //     頂点を持つメッシュは所属側のみなので複製しない
                //     （HierarchyExportWindow の makeMirror = isJoint || side==1 と同じ）。
                if (!branchSide.TryGetValue(i, out int side)) continue;
                if (side != MirrorBranchOps.SideReal) continue;
                if ((ctx.MeshObject?.Vertices?.Count ?? 0) != 0) continue;

                var vnode = new ClipBoneNode
                {
                    Name               = ctx.Name + MirrorBranchOps.MirrorBranchSuffix,
                    ContextIndex       = -1,
                    SourceContextIndex = i,
                    IsMirror           = true,
                    MirrorAxis         = ctx.MirrorAxis
                };
                _mirrorNodeOfSource[i] = -1;
                pending.Add(vnode);
            }

            if (pending.Count == 0) return;

            // 索引を確定させてから親を解決する（前方参照があるため 2 段）。
            foreach (var n in pending)
            {
                _mirrorNodeOfSource[n.SourceContextIndex] = Nodes.Count;
                if (n.ContextIndex >= 0) _nodeOfContext[n.ContextIndex] = Nodes.Count;
                Nodes.Add(n);
                NodeNames.Add(n.Name);
            }

            foreach (var n in pending)
            {
                var src = list[n.SourceContextIndex];
                int hp  = src?.HierarchyParentIndex ?? -1;
                if (hp < 0) { n.ParentNode = -1; n.ParentContextIndex = -1; continue; }

                // 階層親のミラーノードがあればそちら、無ければ実体側の階層親。
                if (_mirrorNodeOfSource.TryGetValue(hp, out int pn) && pn >= 0)
                {
                    n.ParentNode         = pn;
                    n.ParentContextIndex = -1;
                }
                else
                {
                    n.ParentNode         = -1;
                    n.ParentContextIndex = hp;
                }
            }

            MirrorNodeCount = pending.Count;
        }

        // ── Humanoid 割当 ─────────────────────────────────────────────
        //   正本は model.HumanoidMapping（per-bone の humanBodyBone から復元されたもの）。
        //   半身モデルでは右半身の関節に実体が無いため右側の割当が欠ける。
        //   HierarchyExportWindow.BuildAvatarMapsFromModel と同じ規則で、
        //   左右名を入れ替えてミラーノードへ補完する。
        private void BuildHumanoidMap(ModelContext model)
        {
            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;

            var assignedContext = new Dictionary<string, int>();

            foreach (var kv in mapping.BoneIndexMap)
            {
                string key = NormalizeHumanoidName(kv.Key);
                if (string.IsNullOrEmpty(key)) continue;
                if (!_nodeOfContext.TryGetValue(kv.Value, out int node)) continue;
                if (HumanoidToNode.ContainsKey(key)) continue;   // 先勝ち
                HumanoidToNode[key] = node;
                assignedContext[key] = kv.Value;
            }

            // 左右補完
            foreach (var kv in new List<KeyValuePair<string, int>>(assignedContext))
            {
                string other = SwapLeftRight(kv.Key);
                if (string.IsNullOrEmpty(other)) continue;
                if (HumanoidToNode.ContainsKey(other)) continue;

                if (!_mirrorNodeOfSource.TryGetValue(kv.Value, out int mn) || mn < 0) continue;

                // 左右反転はミラー軸が X のときだけ意味を持つ
                // （HierarchyExportWindow の軸チェックと同じ）。
                if (Nodes[mn].MirrorAxis != 1) continue;

                HumanoidToNode[other] = mn;
                SupplementedHumanoidNames.Add(other + " → " + Nodes[mn].Name);
            }
        }

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>MeshContextList 索引からノード索引を引く。無ければ -1。</summary>
        public int NodeOfContext(int contextIndex)
            => _nodeOfContext.TryGetValue(contextIndex, out int n) ? n : -1;

        /// <summary>Humanoid 名（空白の有無は問わない）からノード索引を引く。無ければ -1。</summary>
        public int NodeOfHumanoid(string humanoidName)
        {
            string key = NormalizeHumanoidName(humanoidName);
            if (string.IsNullOrEmpty(key)) return -1;
            return HumanoidToNode.TryGetValue(key, out int n) ? n : -1;
        }

        /// <summary>ノードのレスト・ローカル行列（実体側の枠・BonePoseData を含まない）。</summary>
        public Matrix4x4 RestLocalMatrix(ModelContext model, int node)
        {
            var src = SourceContext(model, node);
            var bt  = src?.BoneTransform;
            if (bt == null || !bt.UseLocalTransform) return Matrix4x4.identity;
            return bt.TransformMatrix;
        }

        /// <summary>
        /// ノード自身の枠で見たレスト・ローカル行列。
        /// ミラーノードは鏡映 S による共役 S·B·S を返す（MirrorLocalTRS と同値）。
        /// </summary>
        public Matrix4x4 RestLocalMatrixOwn(ModelContext model, int node)
        {
            var m = RestLocalMatrix(model, node);
            if (node < 0 || node >= Nodes.Count || !Nodes[node].IsMirror) return m;
            var src = SourceContext(model, node);
            var s = MirrorBranchOps.MirrorMatrix(src?.MirrorAxis ?? 1, src?.MirrorDistance ?? 0f);
            return s * m * s;
        }

        /// <summary>ノード自身の枠で見たレスト・ローカル TRS（位置・回転）。</summary>
        public void RestLocalTRSOwn(ModelContext model, int node, out Vector3 pos, out Quaternion rot)
        {
            var src = SourceContext(model, node);
            var bt  = src?.BoneTransform;
            pos = bt != null ? bt.Position : Vector3.zero;
            Vector3 eul = bt != null ? bt.Rotation : Vector3.zero;

            if (src != null && node >= 0 && node < Nodes.Count && Nodes[node].IsMirror)
                MirrorBranchOps.MirrorLocalTRS(src, ref pos, ref eul);

            rot = Quaternion.Euler(eul);
        }

        /// <summary>
        /// ノード自身の枠で見たレスト・ローカル回転。
        /// ミラーノードは実体側を S で共役した値になる。
        /// </summary>
        public Quaternion RestLocalRotation(ModelContext model, int node)
        {
            var src = SourceContext(model, node);
            var bt  = src?.BoneTransform;
            Quaternion q = bt != null ? bt.RotationQuaternion : Quaternion.identity;
            if (node >= 0 && node < Nodes.Count && Nodes[node].IsMirror)
                q = ConjugateMirror(q, Nodes[node].MirrorAxis);
            return q;
        }

        /// <summary>
        /// ルートからノードまでのレスト・ローカル回転の累積（ノード自身の枠）。
        /// 実体側の階層をそのままたどる（Humanoid 割当の無い中間骨も含む）。
        /// </summary>
        public Quaternion RestWorldRotation(ModelContext model, int node)
        {
            if (node < 0 || node >= Nodes.Count) return Quaternion.identity;
            var list = model.MeshContextList;

            var chain = new List<int>();
            int cur   = Nodes[node].SourceContextIndex;
            int guard = 0;
            while (cur >= 0 && cur < list.Count && guard++ < 512)
            {
                chain.Add(cur);
                var c = list[cur];
                if (c == null) break;
                cur = c.HierarchyParentIndex;
            }

            Quaternion w = Quaternion.identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var bt = list[chain[i]]?.BoneTransform;
                w = w * (bt != null ? bt.RotationQuaternion : Quaternion.identity);
            }
            w = QuatNorm(w);

            if (Nodes[node].IsMirror) w = ConjugateMirror(w, Nodes[node].MirrorAxis);
            return w;
        }

        /// <summary>
        /// ノードのレスト・ワールド行列（BonePoseData を含まない累積）。
        /// ミラーノードは共役 S·H·S を掛けた値を返す（＝右半身の実位置）。
        /// </summary>
        public Matrix4x4 RestWorldMatrix(ModelContext model, int node)
        {
            if (node < 0 || node >= Nodes.Count) return Matrix4x4.identity;
            var list = model.MeshContextList;

            var chain = new List<int>();
            int cur   = Nodes[node].SourceContextIndex;
            int guard = 0;
            while (cur >= 0 && cur < list.Count && guard++ < 512)
            {
                chain.Add(cur);
                var c = list[cur];
                if (c == null) break;
                cur = c.HierarchyParentIndex;
            }

            Matrix4x4 w = Matrix4x4.identity;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var bt = list[chain[i]]?.BoneTransform;
                Matrix4x4 l = (bt != null && bt.UseLocalTransform) ? bt.TransformMatrix : Matrix4x4.identity;
                w = w * l;
            }

            if (Nodes[node].IsMirror)
            {
                var src = list[Nodes[node].SourceContextIndex];
                Matrix4x4 s = MirrorBranchOps.MirrorMatrix(
                    src?.MirrorAxis ?? 1, src?.MirrorDistance ?? 0f);
                w = s * w * s;
            }
            return w;
        }

        /// <summary>ノードの姿勢由来となる実体側コンテキスト。</summary>
        public MeshContext SourceContext(ModelContext model, int node)
        {
            if (node < 0 || node >= Nodes.Count) return null;
            int si = Nodes[node].SourceContextIndex;
            var list = model.MeshContextList;
            return (si >= 0 && si < list.Count) ? list[si] : null;
        }

        /// <summary>ノードが書き込み先として持つコンテキスト。純仮想なら null。</summary>
        public MeshContext TargetContext(ModelContext model, int node)
        {
            if (node < 0 || node >= Nodes.Count) return null;
            int ci = Nodes[node].ContextIndex;
            var list = model.MeshContextList;
            return (ci >= 0 && ci < list.Count) ? list[ci] : null;
        }

        // ================================================================
        // ヘルパ
        // ================================================================

        /// <summary>
        /// 鏡映 S による回転の共役 S·R·S。
        /// ミラー軸以外の 2 成分を符号反転する（MirrorLocalTRS の rot 規則と同一）。
        /// </summary>
        public static Quaternion ConjugateMirror(Quaternion q, int mirrorAxis)
        {
            switch (mirrorAxis)
            {
                case 2:  return new Quaternion(-q.x,  q.y, -q.z, q.w);   // Y
                case 4:  return new Quaternion(-q.x, -q.y,  q.z, q.w);   // Z
                default: return new Quaternion( q.x, -q.y, -q.z, q.w);   // X
            }
        }

        /// <summary>Humanoid 名の空白を除いた比較用キー（"Left Thumb Proximal" → "LeftThumbProximal"）。</summary>
        public static string NormalizeHumanoidName(string name)
            => string.IsNullOrEmpty(name) ? name : name.Replace(" ", string.Empty);

        /// <summary>先頭の Left / Right を入れ替える。左右を持たない名前は null。</summary>
        public static string SwapLeftRight(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName)) return null;
            if (normalizedName.StartsWith("Left"))  return "Right" + normalizedName.Substring(4);
            if (normalizedName.StartsWith("Right")) return "Left"  + normalizedName.Substring(5);
            return null;
        }

        private static Quaternion QuatNorm(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n <= 1e-8f) return Quaternion.identity;
            return new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
        }
    }
}
