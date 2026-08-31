// PolyLingToVrmLibConverter.cs
// ============================================================
// ModelContext → VrmLib.Model 変換
// ============================================================
//
// 【分離規約】規約は Poly_Ling.Vrm.IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属し、VRM パッケージへの依存はここに閉じる。
//
// 【なぜ GameObject を作らないか】
//   UniVRM の Vrm10Exporter.Export(root, model, converter, option, meta) は、
//   マテリアル・メッシュ・ノード・スキンをすべて VrmLib.Model から取る。
//   GameObject ヒエラルキーを要求するのは便利関数 Export(settings, go, ...) の側だけ。
//   root は ExportVrm が非 null を要求するためだけに空の GameObject を1つ渡す。
//
// ============================================================
// 出力構造は HierarchyExportWindow.Export と同じ規則にそろえる
// ============================================================
//
//   あちらが GameObject を作る所を、こちらは VrmLib.Node に置き換えているだけで、
//   判定は同じ Runtime 側 Ops（MeshHierarchyOps / MirrorBranchOps / MirrorNameOps）を使う。
//   規則を写し取ると必ずずれるので、判定は必ず Ops を呼ぶこと。
//
//   ノードは「ボーンだけ」ではなく、出力対象の全 MeshContext に対して作る。
//   MeshFilter 経路のモデルはボーンを1本も持たず、ボーンだけをノード化すると
//   VRMC_vrm.humanoid.humanBones が空になって VRM として成立しないため。
//   HumanoidBoneMapping は MeshContextList の index を指すので、
//   割当先がボーンとは限らない。
//
// ============================================================
// ミラー分岐（左右対称モデルで右半身を作る仕組み）
// ============================================================
//
//   ・分岐の起点は MeshContext.IsMirrorBranchRoot。
//     AnalyzeMirrorBranches がその配下に所属側（SideReal / SideMirror）を付ける。
//
//   ・頂点を持つメッシュ（左目・左親指…）はインポータが MirrorSide コンテキストを
//     作るので、そのコンテキスト自身がミラー枝に出る。
//
//   ・頂点ゼロの関節（左腕・左ひじ・左手首…）はミラー側コンテキストが存在しない
//     （CreateDerivedMirrorContext は頂点ゼロで null を返す）。
//     よって実体側と同じ index でミラー枝にももう1つノードを作る。
//     これをやらないと右腕・右ひじ・右手首・右足・右ひざ・右足首 が生まれず、
//     VRM の必須 humanBones を満たせない。
//
//   ・走査は index の昇順で行う。TryResolveMirrorParent は
//     「その時点でミラー枝に生成済みか」を問う述語を取るため、親が先に来ている
//     必要がある。HierarchyExportWindow も同じ前提（Depth 由来の index 順）で動く。
//
// ============================================================
// 頂点の空間（SkinKind による分岐）
// ============================================================
//
//   ・SkinKind.MeshFilter … 頂点はローカル空間。ノードに TRS を入れて配置する。
//   ・SkinKind.Skinned    … 頂点はワールド（バインド）空間。
//                           メッシュは単位変換のノードへ載せ、Skin を張る。
//   スキンド側ではミラー分岐を使わない（HierarchyExportWindow も同じ）。
//
// ============================================================
// glTF の単一インデックス制約
// ============================================================
//
//   PolyLing の Face は VertexIndices / UVIndices / NormalIndices を別々に持つが、
//   glTF は 1 頂点 = 1 組の属性しか持てない。よって (頂点index, UVスロット) の組で
//   頂点を展開する。PMXExporter.AppendExpandedVertices と同じ考え方。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UniGLTF;
using VrmLib;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    /// <summary>1メッシュ分の展開結果。</summary>
    internal class ExpandedMesh
    {
        public List<Vector3> Positions = new List<Vector3>();
        public List<Vector3> Normals   = new List<Vector3>();
        public List<Vector2> UVs       = new List<Vector2>();
        public List<UShort4> Joints    = new List<UShort4>();
        public List<Vector4> Weights   = new List<Vector4>();
        public List<int>     Indices   = new List<int>();

        public List<VrmLib.Submesh> Submeshes = new List<VrmLib.Submesh>();

        public bool HasSkinning;
    }

    /// <summary>変換結果の付帯情報。</summary>
    public class ConvertReport
    {
        /// <summary>Humanoid に割り当てたボーン数。</summary>
        public int HumanoidBoneCount;

        /// <summary>非表示のためメッシュを載せなかった数。</summary>
        public int SkippedInvisible;

        /// <summary>ミラー枝に生成したノード数。</summary>
        public int MirrorBranchNodes;

        /// <summary>VRM 1.0 の必須 humanBones のうち割り当てられなかったもの。</summary>
        public List<string> MissingRequiredBones = new List<string>();

        /// <summary>Humanoid 割当で解決できなかったエントリ（診断用）。</summary>
        public List<string> UnresolvedHumanoid = new List<string>();

        /// <summary>配置情報がどこに入っているかの実測（診断用）。</summary>
        public List<string> PlacementDiagnostics = new List<string>();
    }

    public static class PolyLingToVrmLibConverter
    {
        // ================================================================
        // エントリ
        // ================================================================

        public static VrmLib.Model Convert(
            ModelContext model, Vrm10ExportSettings settings, INativeArrayManager arrayManager,
            out ConvertReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            settings = settings ?? Vrm10ExportSettings.CreateDefault();
            report = new ConvertReport();

            var vrmModel = new VrmLib.Model(VrmLib.Coordinates.Unity)
            {
                AssetGenerator = "PolyLing",
            };

            var root = new VrmLib.Node("__root__");

            // ----------------------------------------------------------------
            // 下準備（判定はすべて Runtime 側 Ops に委ねる）
            // ----------------------------------------------------------------
            var parentIndices = MeshHierarchyOps.BuildParentIndicesFromDepth(model);
            var peers         = MirrorPeerIndex.Build(model);
            var plan          = MirrorBranchOps.BuildMirrorBranchPlan(
                                    model, parentIndices, MirrorBranchTolerance.Tolerant, peers);

            // ----------------------------------------------------------------
            // マテリアル（メッシュ生成より先に必要）
            // ----------------------------------------------------------------
            var materials = model.Materials;
            if (materials != null)
            {
                for (int i = 0; i < materials.Count; i++)
                    vrmModel.Materials.Add(materials[i]);
            }
            if (vrmModel.Materials.Count == 0)
            {
                var fallback = new Material(Shader.Find("Universal Render Pipeline/Lit")
                                            ?? Shader.Find("Standard"));
                fallback.name = "PolyLing_Default";
                vrmModel.Materials.Add(fallback);
            }

            // ----------------------------------------------------------------
            // ボーン（スキンド経路でのみ使う）
            // ----------------------------------------------------------------
            var boneOrder = new List<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject != null && mc.Type == MeshType.Bone) boneOrder.Add(i);
            }

            var jointIndexOf = new Dictionary<int, int>();
            for (int j = 0; j < boneOrder.Count; j++) jointIndexOf[boneOrder[j]] = j;

            // ----------------------------------------------------------------
            // ノード生成（index 昇順。親が先に来る前提）
            // ----------------------------------------------------------------
            var realNodeOf   = new Dictionary<int, VrmLib.Node>();
            var mirrorNodeOf = new Dictionary<int, VrmLib.Node>();
            var usedNames    = new HashSet<string>();

            VrmLib.Skin sharedSkin = null;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.ExcludeFromExport) continue;

                switch (mc.Type)
                {
                    case MeshType.Morph:
                    case MeshType.RigidBody:
                    case MeshType.RigidBodyJoint:
                        continue;
                }

                bool isJoint  = mc.MeshObject.VertexCount == 0;
                bool skinned  = settings.ExportSkinning && mc.IsSkinned && boneOrder.Count > 0;

                // ミラー分岐はスキンドでは扱わない（HierarchyExportWindow と同じ）。
                // out 変数を && の右辺で宣言すると、短絡で TryGetValue が呼ばれない経路が
                // でき、未代入になる（CS0165）。宣言と初期化を先に済ませる。
                int side = MirrorBranchOps.SideReal;
                bool inBranch = !skinned
                             && plan.Side != null
                             && plan.Side.TryGetValue(i, out side);
                if (!inBranch) side = MirrorBranchOps.SideReal;

                bool emitMirror = !skinned && plan.EmitsMirror(i);

                // 枝の中の関節は両側へ複製する。ここが右腕・右ひじを生む唯一の経路。
                bool makeReal   = !inBranch || isJoint || side == MirrorBranchOps.SideReal;
                bool makeMirror = (inBranch && isJoint) || emitMirror;
                bool generateMirrorShape = emitMirror && plan.GeneratesMirrorShape(i);

                if (makeReal)
                {
                    var node = CreateNode(
                        model, mc, i, mirror: false, parentIndices, peers,
                        realNodeOf, mirrorNodeOf, usedNames, root, settings);
                    realNodeOf[i] = node;

                    if (!isJoint)
                        AttachMesh(model, mc, i, node, root, vrmModel, settings, report,
                                   skinned, boneOrder, jointIndexOf, realNodeOf,
                                   mirroredObject: null, ref sharedSkin, arrayManager);
                }

                if (makeMirror)
                {
                    var node = CreateNode(
                        model, mc, i, mirror: true, parentIndices, peers,
                        realNodeOf, mirrorNodeOf, usedNames, root, settings);
                    mirrorNodeOf[i] = node;
                    report.MirrorBranchNodes++;

                    if (!isJoint)
                    {
                        var mirroredObject = BuildMirrorMeshObject(
                            model, mc, i, peers, generateMirrorShape);

                        AttachMesh(model, mc, i, node, root, vrmModel, settings, report,
                                   skinned: false, boneOrder, jointIndexOf, realNodeOf,
                                   mirroredObject, ref sharedSkin, arrayManager);
                    }
                }
            }

            // ----------------------------------------------------------------
            // Armature（skin.skeleton の指し先）
            //   __root__ は Model.SetRoot が Nodes から外す疑似ノードなので、
            //   Skin.Root に入れても ExportNodes の IndexOf が -1 になる。
            // ----------------------------------------------------------------
            if (sharedSkin != null)
            {
                var armature = new VrmLib.Node("Armature");
                root.Add(armature);
                foreach (int b in boneOrder)
                {
                    if (!realNodeOf.TryGetValue(b, out var bn)) continue;
                    // Node.Add は既存の親から自動で外す（Node.cs:216-224）。
                    if (bn.Parent == root) armature.Add(bn);
                }
                sharedSkin.Root = armature;
            }

            // ----------------------------------------------------------------
            // Humanoid 割当
            // ----------------------------------------------------------------
            var assignedBones = AssignHumanoidBones(model, realNodeOf, mirrorNodeOf, report);
            report.HumanoidBoneCount    = assignedBones.Count;
            report.MissingRequiredBones = CollectMissingRequiredBones(assignedBones);

            CollectPlacementDiagnostics(model, report);

            // ================================================================
            // ワールド行列のキャッシュを確定させる（必須）
            // ================================================================
            //
            // VrmLib.Node はローカル TRS とは別に、ワールド行列 m_matrix を
            // キャッシュで持つ。LocalTranslationWithoutUpdate などの
            // 「WithoutUpdate」フィールドへ直接書くと、このキャッシュは更新されない。
            //
            // そして Vrm10Exporter が呼ぶ Model.ConvertCoordinate は、
            // 内部の ReverseAxisAndFlipTriangle が全ノードに対して
            //     n.SetMatrix(reverser.ReverseMatrix(n.Matrix), false)
            // を行う（Model.cs:491-495）。n.Matrix はこのキャッシュそのものなので、
            // 更新されていなければ単位行列が読まれ、SetMatrix がローカル TRS を
            // 単位で上書きする。結果、設定した姿勢が全部消える。
            //
            // ここで一度だけワールド行列を組み直しておけば、
            // ConvertCoordinate は正しい値を読んで正しく反転する。
            root.CalcWorldMatrix();

            vrmModel.SetRoot(root);
            return vrmModel;
        }

        // ================================================================
        // 配置情報の実測（診断用）
        // ================================================================

        /// <summary>
        /// MeshFilter 経路では、オブジェクトの親子関係と Transform が配置を担う
        /// （頂点はパーツローカル）。その配置がどのフィールドに入っているかを実測する。
        ///
        /// BoneTransform / LocalMatrix / WorldMatrix のどれが実値を持つかが分かれば、
        /// ノードの TRS をどこから作るべきかが確定する。
        /// </summary>
        private static void CollectPlacementDiagnostics(ModelContext model, ConvertReport report)
        {
            int total = 0;
            int useLocal = 0;
            int btNonZeroPos = 0;
            int btNonOneScale = 0;
            int localNonIdentity = 0;
            int worldNonIdentity = 0;

            var samples = new List<string>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Morph ||
                    mc.Type == MeshType.RigidBody ||
                    mc.Type == MeshType.RigidBodyJoint) continue;

                total++;

                var bt = mc.BoneTransform;
                bool ul = bt != null && bt.UseLocalTransform;
                Vector3 bp = bt != null ? bt.Position : Vector3.zero;
                Vector3 br = bt != null ? bt.Rotation : Vector3.zero;
                Vector3 bs = bt != null ? bt.Scale    : Vector3.one;

                if (ul) useLocal++;
                if (bp.sqrMagnitude > 1e-10f) btNonZeroPos++;
                if ((bs - Vector3.one).sqrMagnitude > 1e-10f) btNonOneScale++;

                Vector3 lp = mc.LocalMatrix.GetColumn(3);
                Vector3 wp = mc.WorldMatrix.GetColumn(3);
                if (lp.sqrMagnitude > 1e-10f) localNonIdentity++;
                if (wp.sqrMagnitude > 1e-10f) worldNonIdentity++;

                if (samples.Count < 20 && mc.MeshObject.VertexCount > 0)
                {
                    Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                    Vector3 hi = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                    foreach (var v in mc.MeshObject.Vertices)
                    {
                        lo = Vector3.Min(lo, v.Position);
                        hi = Vector3.Max(hi, v.Position);
                    }
                    Vector3 c = (lo + hi) * 0.5f;

                    samples.Add(
                        $"[{i}]\"{mc.Name}\" type={mc.Type} hp={mc.HierarchyParentIndex} depth={mc.Depth} " +
                        $"btUseLocal={ul} btPos={F(bp)} btRot={F(br)} btScl={F(bs)} " +
                        $"localPos={F(lp)} worldPos={F(wp)} vtxCenter={F(c)}");
                }
            }

            report.PlacementDiagnostics.Add(
                $"総数={total} btUseLocal={useLocal} bt位置非ゼロ={btNonZeroPos} btスケール非1={btNonOneScale} " +
                $"localMatrix平行移動非ゼロ={localNonIdentity} worldMatrix平行移動非ゼロ={worldNonIdentity}");

            report.PlacementDiagnostics.AddRange(samples);
        }

        private static string F(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

        // ================================================================
        // ノード生成
        // ================================================================

        private static VrmLib.Node CreateNode(
            ModelContext model, MeshContext mc, int index, bool mirror,
            int[] parentIndices, MirrorPeerIndex peers,
            Dictionary<int, VrmLib.Node> realNodeOf,
            Dictionary<int, VrmLib.Node> mirrorNodeOf,
            HashSet<string> usedNames, VrmLib.Node root,
            Vrm10ExportSettings settings)
        {
            string rawName = string.IsNullOrEmpty(mc.Name) ? $"Node_{index}" : mc.Name;

            // ミラー側の関節は元と同名になるので別名にする。
            // 左右対応で解決できれば「左腕 → 右腕」、できなければ接尾辞 "+"。
            // ミラー側コンテキストは元から別名なので触らない。
            if (mirror && !MirrorBranchOps.IsMirrorSideContext(mc))
                rawName = MirrorNameOps.MakeMirrorName(
                    rawName, MirrorBranchOps.MirrorBranchSuffix, usedNames.Contains);

            string name = MakeUniqueName(rawName, usedNames);
            var node = new VrmLib.Node(name);

            // ---- 親の解決 ----
            VrmLib.Node parentNode = root;

            int hp = (parentIndices != null && index < parentIndices.Length)
                ? parentIndices[index]
                : mc.HierarchyParentIndex;

            if (MirrorBranchOps.TryResolveMirrorParent(
                    peers, hp, mirror,
                    idx => mirrorNodeOf.ContainsKey(idx),
                    out int parentIndex, out bool parentIsMirrorSide))
            {
                if (parentIsMirrorSide)
                {
                    if (mirrorNodeOf.TryGetValue(parentIndex, out var mn)) parentNode = mn;
                }
                else if (realNodeOf.TryGetValue(parentIndex, out var rn))
                {
                    parentNode = rn;
                }
            }

            parentNode.Add(node);

            bool hasParent = parentNode != root;

            // ================================================================
            // 姿勢
            // ================================================================
            //
            // 【配置は WorldMatrix が持つ】
            //   実測（配置診断）では、168 コンテキスト中 138 の WorldMatrix が
            //   非ゼロの平行移動を持ち、BoneTransform 側は 52 しか値を持たなかった。
            //   MeshFilter 経路ではオブジェクトの親子関係と変換が配置を担うので、
            //   実際に埋まっている WorldMatrix を正本にする。
            //   BoneTransform を使うと残りの配置が落ちて、全パーツが親の原点へ集まる。
            //
            //   ローカル変換 = 親の WorldMatrix の逆 × 自身の WorldMatrix。
            //   逆行列は MeshContext.WorldMatrixInverse を使わずその場で作る。
            //   あちらは ComputeWorldMatrices が更新する保存値で、
            //   直近で呼ばれていなければ古い値のまま残るため。
            //
            // 【ミラー枝は「枝での実効ワールド」で揃える】
            //   ミラー枝には由来の違う2種類のノードが混ざる。
            //     ・ミラー側コンテキスト（インポータ製）… WorldMatrix が既に共役 S·H·S
            //       （ModelContext.cs:1752-1764）。
            //     ・複製した頂点ゼロの関節           … 実体側と同じコンテキストなので
            //                                        WorldMatrix は反転前のまま。
            //   親子でこの2種類が混ざると、片方だけ反転済みの状態で差分を取ることになり、
            //   鏡像1回分の平行移動が残る（実測: てのひら+ に -2×0.364 のずれ）。
            //
            //   そこで BranchWorld() で「枝での実効ワールド」に揃えてから相対を取る。
            //   反転前のものだけ共役 S·W·S を掛ける。S は反射なので S⁻¹ = S。
            //   これで親子とも同じ空間になり、ローカルを別途鏡像化する必要がなくなる
            //   （MirrorLocalTRS は使わない。あれはローカル位置に 2d-p を掛けるため、
            //     ミラー距離が 0 でない枝で平行移動が余分に入る）。

            Matrix4x4 parentWorld = Matrix4x4.identity;
            if (hasParent && parentIndex >= 0 && parentIndex < model.MeshContextCount)
            {
                var pmc = model.GetMeshContext(parentIndex);
                if (pmc != null) parentWorld = BranchWorld(pmc, parentIsMirrorSide);
            }

            Matrix4x4 local = parentWorld.inverse * BranchWorld(mc, mirror);

            Vector3 pos = local.GetColumn(3);
            Vector3 rot = SafeRotation(local).eulerAngles;
            Vector3 scl = local.lossyScale;

            node.LocalTranslationWithoutUpdate = pos * settings.Scale;
            node.LocalRotationWithoutUpdate    = Quaternion.Euler(rot);
            node.LocalScalingWithoutUpdate     = scl;

            return node;
        }

        /// <summary>
        /// ミラー枝での実効ワールド行列を返す。
        ///
        /// ミラー側コンテキストの WorldMatrix は既に共役 S·H·S なのでそのまま。
        /// 実体側から複製したノードは反転前なので、ここで共役を掛けて枝の空間へそろえる。
        /// mirror が false（実体側）なら何もしない。
        /// </summary>
        private static Matrix4x4 BranchWorld(MeshContext mc, bool mirror)
        {
            if (mc == null) return Matrix4x4.identity;
            if (!mirror) return mc.WorldMatrix;
            if (MirrorBranchOps.IsMirrorSideContext(mc)) return mc.WorldMatrix;

            var s = MirrorBranchOps.MirrorMatrix(
                MirrorBranchOps.ResolveMirrorAxis(mc), mc.MirrorDistance);

            // S は反射なので S⁻¹ = S。共役は S·W·S。
            return s * mc.WorldMatrix * s;
        }

        private static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Node" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }

        // ================================================================
        // ミラー枝のメッシュ形状
        // ================================================================

        /// <summary>
        /// ミラー枝に載せる MeshObject を作る。
        ///   ・生成が要る場合（実体側からの複製）… BuildMirroredMeshObject で鏡像化する。
        ///     距離は 0 を渡す。ノード側が共役ワールド S·W·S を持つので、
        ///     頂点に掛けるのは距離を含まない原点まわりの反射だけでよい。
        ///     厳密に一致するのはミラー距離が 0 のときで、0 でない枝では
        ///     親空間に 2d ぶんの平行移動が残る。HierarchyExportWindow も同じ性質。
        ///   ・ミラー側コンテキストの場合 … 形状は既に反転済み。ピボットが
        ///     「実体側ピボットの鏡像」へ動くぶんだけ頂点を平行移動する。
        ///     （HierarchyExportWindow.BuildMirrorSideMesh と同じ補正）
        /// </summary>
        private static MeshObject BuildMirrorMeshObject(
            ModelContext model, MeshContext mc, int index,
            MirrorPeerIndex peers, bool generateMirrorShape)
        {
            if (mc?.MeshObject == null) return null;

            if (generateMirrorShape)
            {
                return MirrorBranchOps.BuildMirroredMeshObject(
                    mc.MeshObject,
                    MirrorBranchOps.ResolveMirrorAxis(mc),
                    0f,                             // 距離はピボット側が吸収する
                    mc.MirrorMaterialOffset,
                    mc.Name);
            }

            MeshContext realPeer = null;
            if (peers != null && peers.TryGetReal(index, out int realIdx))
                realPeer = model.GetMeshContext(realIdx);

            var axisSource = realPeer ?? mc;

            var wm = mc.WorldMatrix;
            var w  = new Vector3(wm.m03, wm.m13, wm.m23);

            Vector3 pivot = w;
            if (realPeer != null)
            {
                var rwm = realPeer.WorldMatrix;
                pivot = new Vector3(rwm.m03, rwm.m13, rwm.m23);
            }

            Vector3 mirrored = MirrorBranchOps.MirrorPoint(
                MirrorBranchOps.ResolveMirrorAxis(axisSource), axisSource.MirrorDistance, pivot);

            Vector3 offset = w - mirrored;
            if (offset.sqrMagnitude < 1e-12f) return null;   // 補正不要。元の形状をそのまま使う

            var clone = mc.MeshObject.Clone();
            for (int i = 0; i < clone.Vertices.Count; i++)
                clone.Vertices[i].Position += offset;
            clone.InvalidatePositionCache();
            return clone;
        }

        // ================================================================
        // メッシュを載せる
        // ================================================================

        private static void AttachMesh(
            ModelContext model, MeshContext mc, int index,
            VrmLib.Node node, VrmLib.Node root, VrmLib.Model vrmModel,
            Vrm10ExportSettings settings, ConvertReport report,
            bool skinned, List<int> boneOrder, Dictionary<int, int> jointIndexOf,
            Dictionary<int, VrmLib.Node> realNodeOf,
            MeshObject mirroredObject, ref VrmLib.Skin sharedSkin,
            INativeArrayManager arrayManager)
        {
            if (!mc.IsVisible && !settings.ExportInvisibleObjects)
            {
                report.SkippedInvisible++;
                return;
            }
            if (mc.ExcludeFromExport) return;

            var source = mirroredObject ?? mc.MeshObject;
            if (source == null || source.VertexCount == 0) return;

            var expanded = ExpandMesh(source, settings, jointIndexOf,
                                      vrmModel.Materials.Count, skinned);
            if (expanded == null || expanded.Positions.Count == 0 || expanded.Indices.Count == 0)
                return;

            var meshGroup = BuildMeshGroup(mc, expanded, settings, arrayManager);

            if (skinned && expanded.HasSkinning)
            {
                if (sharedSkin == null)
                {
                    sharedSkin = new VrmLib.Skin();
                    foreach (int b in boneOrder)
                        if (realNodeOf.TryGetValue(b, out var bn)) sharedSkin.Joints.Add(bn);

                    sharedSkin.InverseMatrices =
                        BuildInverseBindMatrices(model, boneOrder, settings, arrayManager);

                    vrmModel.Skins.Add(sharedSkin);
                }
                meshGroup.Skin = sharedSkin;

                // 頂点はバインド空間なので、メッシュを載せるノードのワールド変換は
                // 単位でなければならない。glTF 2.0 仕様は
                //   "Only the joint transforms are applied to the skinned mesh;
                //    the transform of the skinned mesh node MUST be ignored."
                // と定めるが、単位にしておけば無視する実装でもしない実装でも同じ結果になる。
                var skinnedNode = new VrmLib.Node(node.Name + "_mesh")
                {
                    MeshGroup = meshGroup,
                };
                root.Add(skinnedNode);
            }
            else
            {
                node.MeshGroup = meshGroup;
            }

            vrmModel.MeshGroups.Add(meshGroup);
        }

        // ================================================================
        // メッシュ展開
        // ================================================================

        private static ExpandedMesh ExpandMesh(
            MeshObject mo, Vrm10ExportSettings settings,
            Dictionary<int, int> jointIndexOf, int materialCount, bool skinned)
        {
            var result = new ExpandedMesh();
            var map = new Dictionary<(int v, int uv), int>();

            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in mo.Faces)
            {
                if (face == null || face.VertexIndices.Count < 3) continue;

                int mat = Mathf.Clamp(face.MaterialIndex, 0, Mathf.Max(0, materialCount - 1));
                if (!facesByMaterial.TryGetValue(mat, out var list))
                {
                    list = new List<Face>();
                    facesByMaterial[mat] = list;
                }
                list.Add(face);
            }

            var matKeys = new List<int>(facesByMaterial.Keys);
            matKeys.Sort();

            foreach (int mat in matKeys)
            {
                int offset = result.Indices.Count;

                foreach (var face in facesByMaterial[mat])
                {
                    for (int i = 0; i < face.VertexIndices.Count - 2; i++)
                    {
                        int a = GetOrAdd(mo, result, map, settings, jointIndexOf, skinned,
                                         face.VertexIndices[0],     SlotAt(face.UVIndices, 0));
                        int b = GetOrAdd(mo, result, map, settings, jointIndexOf, skinned,
                                         face.VertexIndices[i + 1], SlotAt(face.UVIndices, i + 1));
                        int c = GetOrAdd(mo, result, map, settings, jointIndexOf, skinned,
                                         face.VertexIndices[i + 2], SlotAt(face.UVIndices, i + 2));
                        if (a < 0 || b < 0 || c < 0) continue;

                        result.Indices.Add(a);
                        result.Indices.Add(b);
                        result.Indices.Add(c);
                    }
                }

                int drawCount = result.Indices.Count - offset;
                if (drawCount > 0)
                    result.Submeshes.Add(new VrmLib.Submesh(offset, drawCount, mat));
            }

            return result;
        }

        private static int SlotAt(List<int> slots, int i)
            => (slots != null && i < slots.Count) ? slots[i] : 0;

        private static int GetOrAdd(
            MeshObject mo, ExpandedMesh dst, Dictionary<(int, int), int> map,
            Vrm10ExportSettings settings, Dictionary<int, int> jointIndexOf, bool skinned,
            int vertexIndex, int uvSlot)
        {
            if (vertexIndex < 0 || vertexIndex >= mo.VertexCount) return -1;

            var key = (vertexIndex, uvSlot);
            if (map.TryGetValue(key, out int existing)) return existing;

            var v = mo.Vertices[vertexIndex];

            int newIndex = dst.Positions.Count;
            dst.Positions.Add(v.Position * settings.Scale);

            if (settings.ExportNormals)
            {
                Vector3 n = (v.Normals != null && uvSlot < v.Normals.Count)
                    ? v.Normals[uvSlot]
                    : ((v.Normals != null && v.Normals.Count > 0) ? v.Normals[0] : Vector3.up);
                dst.Normals.Add(n);
            }

            if (settings.ExportUVs)
            {
                Vector2 uv = (v.UVs != null && uvSlot < v.UVs.Count)
                    ? v.UVs[uvSlot]
                    : ((v.UVs != null && v.UVs.Count > 0) ? v.UVs[0] : Vector2.zero);
                dst.UVs.Add(uv);
            }

            if (skinned)
            {
                if (v.HasBoneWeight)
                {
                    var bw = v.BoneWeight.Value;
                    dst.Joints.Add(new UShort4(
                        (ushort)MapJoint(jointIndexOf, bw.boneIndex0),
                        (ushort)MapJoint(jointIndexOf, bw.boneIndex1),
                        (ushort)MapJoint(jointIndexOf, bw.boneIndex2),
                        (ushort)MapJoint(jointIndexOf, bw.boneIndex3)));
                    dst.Weights.Add(new Vector4(bw.weight0, bw.weight1, bw.weight2, bw.weight3));
                    dst.HasSkinning = true;
                }
                else
                {
                    // ウェイトを持たない頂点。glTF は属性の欠落を許さないので枠は埋める。
                    dst.Joints.Add(new UShort4(0, 0, 0, 0));
                    dst.Weights.Add(new Vector4(1f, 0f, 0f, 0f));
                }
            }

            map[key] = newIndex;
            return newIndex;
        }

        private static int MapJoint(Dictionary<int, int> jointIndexOf, int masterIndex)
            => jointIndexOf.TryGetValue(masterIndex, out int j) ? j : 0;

        // ================================================================
        // BufferAccessor 組み立て
        // ================================================================

        private static VrmLib.MeshGroup BuildMeshGroup(
            MeshContext mc, ExpandedMesh src, Vrm10ExportSettings settings,
            INativeArrayManager arrayManager)
        {
            var mesh = new VrmLib.Mesh
            {
                VertexBuffer = new VrmLib.VertexBuffer(),
            };

            mesh.VertexBuffer.Add(
                VrmLib.VertexBuffer.PositionKey,
                MakeAccessor(arrayManager, src.Positions.ToArray(), AccessorVectorType.VEC3));

            if (settings.ExportNormals && src.Normals.Count == src.Positions.Count)
            {
                mesh.VertexBuffer.Add(
                    VrmLib.VertexBuffer.NormalKey,
                    MakeAccessor(arrayManager, src.Normals.ToArray(), AccessorVectorType.VEC3));
            }

            if (settings.ExportUVs && src.UVs.Count == src.Positions.Count)
            {
                mesh.VertexBuffer.Add(
                    VrmLib.VertexBuffer.TexCoordKey,
                    MakeAccessor(arrayManager, src.UVs.ToArray(), AccessorVectorType.VEC2));
            }

            if (src.HasSkinning && src.Joints.Count == src.Positions.Count)
            {
                mesh.VertexBuffer.Add(
                    VrmLib.VertexBuffer.JointKey,
                    MakeJointAccessor(arrayManager, src.Joints.ToArray()));
                mesh.VertexBuffer.Add(
                    VrmLib.VertexBuffer.WeightKey,
                    MakeAccessor(arrayManager, src.Weights.ToArray(), AccessorVectorType.VEC4));
            }

            mesh.IndexBuffer = MakeIndexAccessor(arrayManager, src.Indices.ToArray());

            foreach (var sub in src.Submeshes)
                mesh.Submeshes.Add(sub);

            var group = new VrmLib.MeshGroup(string.IsNullOrEmpty(mc.Name) ? "Mesh" : mc.Name);
            group.Meshes.Add(mesh);
            return group;
        }

        /// <summary>
        /// inverseBindMatrices を PolyLing の BindPose から作る。
        /// HierarchyExportWindow.AttachSkinnedMesh が bindposes に BindPose を入れているのと同じ。
        /// </summary>
        private static BufferAccessor BuildInverseBindMatrices(
            ModelContext model, List<int> boneOrder, Vrm10ExportSettings settings,
            INativeArrayManager arrayManager)
        {
            var matrices = new Matrix4x4[boneOrder.Count];
            for (int i = 0; i < boneOrder.Count; i++)
            {
                var mc = model.GetMeshContext(boneOrder[i]);
                var m = mc != null ? mc.BindPose : Matrix4x4.identity;

                if (!Mathf.Approximately(settings.Scale, 1f))
                {
                    Vector4 col = m.GetColumn(3);
                    m.SetColumn(3, new Vector4(col.x * settings.Scale,
                                               col.y * settings.Scale,
                                               col.z * settings.Scale,
                                               col.w));
                }
                matrices[i] = m;
            }

            var accessor = new BufferAccessor(
                arrayManager,
                arrayManager.CreateNativeArray<byte>(0),
                AccessorValueType.FLOAT, AccessorVectorType.MAT4, 0);
            accessor.Assign(matrices);
            return accessor;
        }

        private static BufferAccessor MakeAccessor<T>(
            INativeArrayManager arrayManager, T[] values, AccessorVectorType type) where T : struct
        {
            var accessor = new BufferAccessor(
                arrayManager,
                arrayManager.CreateNativeArray<byte>(0),
                AccessorValueType.FLOAT, type, 0);
            accessor.Assign(values);
            return accessor;
        }

        private static BufferAccessor MakeJointAccessor(
            INativeArrayManager arrayManager, UShort4[] values)
        {
            var accessor = new BufferAccessor(
                arrayManager,
                arrayManager.CreateNativeArray<byte>(0),
                AccessorValueType.UNSIGNED_SHORT, AccessorVectorType.VEC4, 0);
            accessor.Assign(values);
            return accessor;
        }

        private static BufferAccessor MakeIndexAccessor(
            INativeArrayManager arrayManager, int[] indices)
        {
            var accessor = new BufferAccessor(
                arrayManager,
                arrayManager.CreateNativeArray<byte>(0),
                AccessorValueType.UNSIGNED_INT, AccessorVectorType.SCALAR, 0);
            accessor.Assign(indices);
            return accessor;
        }

        // ================================================================
        // Humanoid
        // ================================================================

        /// <summary>
        /// Humanoid を割り当てる。
        ///
        /// 割当先は2系統ある。
        ///   ・実体側ノード … 通常の割当。ミラー側コンテキスト（右目・右親指など）は
        ///     実体側ノードを持たずミラー枝にだけ出るので、そちらも引く。
        ///   ・ミラー枝ノード … 頂点ゼロの関節（左腕など）は同じ index で両側に出る。
        ///     割当名を SwapHumanoidLeftRight で左右反転してミラー枝ノードへ入れる。
        ///     これをやらないと rightUpperArm 以下が永久に埋まらない。
        /// </summary>
        private static HashSet<VrmLib.HumanoidBones> AssignHumanoidBones(
            ModelContext model,
            Dictionary<int, VrmLib.Node> realNodeOf,
            Dictionary<int, VrmLib.Node> mirrorNodeOf,
            ConvertReport report)
        {
            var assigned = new HashSet<VrmLib.HumanoidBones>();

            var mapping = model.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return assigned;

            // 1) そのままの割当
            foreach (var kv in mapping.BoneIndexMap)
            {
                VrmLib.Node node = null;
                if (!realNodeOf.TryGetValue(kv.Value, out node))
                    mirrorNodeOf.TryGetValue(kv.Value, out node);

                if (node == null)
                {
                    report.UnresolvedHumanoid.Add($"{kv.Key}→[{kv.Value}] ノード無し");
                    continue;
                }
                if (!TryParseHumanoidBone(kv.Key, out var bone))
                {
                    report.UnresolvedHumanoid.Add($"{kv.Key}→[{kv.Value}] 名前解決失敗");
                    continue;
                }
                if (!assigned.Add(bone))
                {
                    report.UnresolvedHumanoid.Add($"{kv.Key}→[{kv.Value}] 既に割当済み");
                    continue;
                }

                node.HumanoidBone = bone;
            }

            // 2) 両側へ複製された関節の反対側
            foreach (var kv in mapping.BoneIndexMap)
            {
                if (!realNodeOf.ContainsKey(kv.Value)) continue;
                if (!mirrorNodeOf.TryGetValue(kv.Value, out var mirrorNode)) continue;

                string swapped = MirrorNameOps.SwapHumanoidLeftRight(kv.Key);
                if (string.IsNullOrEmpty(swapped)) continue;
                if (!TryParseHumanoidBone(swapped, out var bone)) continue;
                if (!assigned.Add(bone)) continue;

                mirrorNode.HumanoidBone = bone;
            }

            return assigned;
        }

        /// <summary>
        /// PolyLing の Humanoid 名を VrmLib.HumanoidBones に変換する。
        ///
        /// 【名前の形式】
        ///   PolyLing の Humanoid 名は Unity の HumanTrait.BoneName 形式が正本。
        ///   AvatarBuildCore.cs:14 に「map/limits の humanName は HumanTrait.BoneName 形式
        ///   （指はスペース付き）」と明記されており、AllHumanoidBones も
        ///   体幹・四肢は "LeftUpperArm"、指だけ "Left Thumb Proximal" と空白入りになっている。
        ///   enum メンバ名だと思って直に Enum.TryParse すると指が全滅する。
        ///   よって空白を落としてから解決する。
        ///
        /// 【親指の段ずれ】
        ///   Unity: ThumbProximal / ThumbIntermediate / ThumbDistal
        ///   VRM  : thumbMetacarpal / thumbProximal   / thumbDistal
        ///   そのまま解決すると1段ずれるので、UniVRM の ModelExporter:50-53 と
        ///   同じ規則で先に潰す。
        /// </summary>
        private static bool TryParseHumanoidBone(string name, out VrmLib.HumanoidBones bone)
        {
            bone = default;
            if (string.IsNullOrEmpty(name)) return false;

            // "Left Thumb Proximal" → "LeftThumbProximal"
            string compact = name.Replace(" ", string.Empty);

            switch (compact)
            {
                case "LeftThumbProximal":      bone = VrmLib.HumanoidBones.leftThumbMetacarpal;  return true;
                case "LeftThumbIntermediate":  bone = VrmLib.HumanoidBones.leftThumbProximal;    return true;
                case "RightThumbProximal":     bone = VrmLib.HumanoidBones.rightThumbMetacarpal; return true;
                case "RightThumbIntermediate": bone = VrmLib.HumanoidBones.rightThumbProximal;   return true;
            }

            if (!Enum.TryParse(compact, ignoreCase: true, result: out bone)) return false;
            return bone != VrmLib.HumanoidBones.unknown;
        }

        // ================================================================
        // 必須ボーンの検査
        // ================================================================

        /// <summary>
        /// VRM 1.0 が必須とする humanBones。
        ///
        /// 【正典は VrmLib 側】一覧を自前で持たない。
        ///   VrmLib.HumanoidBones の各メンバに付いた BoneRequiredAttribute が正典で、
        ///   VrmLib.Model.CheckVrmHumanoid() も同じ属性から必須集合を導いている。
        /// </summary>
        private static VrmLib.HumanoidBones[] _requiredBonesCache;

        private static VrmLib.HumanoidBones[] GetRequiredHumanoidBones()
        {
            if (_requiredBonesCache != null) return _requiredBonesCache;

            var list = new List<VrmLib.HumanoidBones>();
            var type = typeof(VrmLib.HumanoidBones);

            foreach (VrmLib.HumanoidBones bone in Enum.GetValues(type))
            {
                var field = type.GetField(bone.ToString());
                if (field == null) continue;

                var attrs = field.GetCustomAttributes(typeof(VrmLib.BoneRequiredAttribute), false);
                if (attrs != null && attrs.Length > 0) list.Add(bone);
            }

            _requiredBonesCache = list.ToArray();
            return _requiredBonesCache;
        }

        private static List<string> CollectMissingRequiredBones(HashSet<VrmLib.HumanoidBones> assigned)
        {
            var missing = new List<string>();
            foreach (var b in GetRequiredHumanoidBones())
                if (!assigned.Contains(b)) missing.Add(b.ToString());
            return missing;
        }

        // ================================================================
        // 行列ヘルパー
        // ================================================================

        private static Quaternion SafeRotation(Matrix4x4 m)
        {
            var q = m.rotation;
            if (float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w))
                return Quaternion.identity;
            return q;
        }
    }
}
