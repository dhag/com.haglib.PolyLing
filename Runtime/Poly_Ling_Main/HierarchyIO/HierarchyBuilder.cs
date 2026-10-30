// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyBuilder.cs
// ============================================================
// ModelContext → Unity GameObject 階層
// ============================================================
//
// 【この規約の位置づけ】
//   ヒエラルキー生成の Editor / Runtime 分離についての規約は、本ファイルを正典とする。
//   HierarchyBuildOptions / HierarchyBuildResult / HierarchyPhysicsBuilder /
//   HumanoidTransformMap はここを参照し、規約を書き写さない。
//
// ============================================================
// 1. ここに UnityEditor の関心事を持ち込まない
// ============================================================
//
//   本ファイルは PolyLing.Runtime に属する。#if UNITY_EDITOR は書かない。
//   Editor でしか成立しない処理は 2 系統に分けて外へ出す。
//
//     ・Editor API そのもの（Undo / AssetDatabase）
//         → PLEditorBridge（IEditorBridge）経由で呼ぶ。既存規約どおり。
//           Player では EditorBridgeNull が素の AddComponent などへ落とす。
//
//     ・Editor にしか存在しない「決定」
//         （プレファブ化するか／どのアセットパスへ保存するか／
//           Avatar を生成するか／出力先フォルダ／EditorPrefs）
//         → 本クラスは一切知らない。呼び出し側に残す。
//           メッシュの共有アセット化だけは生成の途中に挟まるため、
//           Build の引数 persistMesh（Mesh → Mesh）で受け取る。
//           null なら何もしない＝Player でもそのまま動く。
//
// ============================================================
// 2. ログを出さない
// ============================================================
//
//   警告・補足は HierarchyBuildResult に溜めるだけにする。
//   コンソールへ出すかダイアログに出すかは呼び出し側が決める。
//
// ============================================================
// 3. Undo のグループ化は呼び出し側
// ============================================================
//
//   Undo.SetCurrentGroupName / CollapseUndoOperations は「1 操作としてまとめる」
//   という Editor の編集体験の話であり、生成の一部ではない。
//   個々の生成物の Undo 登録だけを PLEditorBridge 経由で行う。
//
// ============================================================
// 出力構造
// ============================================================
//
//   <ModelName>                 ← ルート GameObject
//     Armature                  ← ボーン階層ルート（ボーンが存在する場合のみ）
//       <BoneName> ...          ← ボーン Transform ツリー（WorldMatrix で配置）
//     <MeshName> ...            ← SkinnedMeshRenderer または MeshFilter+MeshRenderer
//       スキニング: MeshObject.SkinKind==Skinned → SkinnedMeshRenderer
//                  （BindPose を bindposes に設定、ボーン Transform を bones に設定）
//       それ以外 → MeshFilter + MeshRenderer（WorldMatrix で配置）
//
// 【移植元】
//   Editor/HierarchyIO/HierarchyExportWindow.cs の Export ほか。
//   判定・姿勢計算のロジックは移設時に変更していない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>ModelContext を Unity GameObject 階層へ書き出す。</summary>
    public class HierarchyBuilder
    {
        // ================================================================
        // 名前の接尾辞
        // ================================================================

        /// <summary>
        /// ミラー分岐のミラー側 GameObject に付ける接尾辞。
        /// 規則は MirrorBranchOps を正本とする。
        /// </summary>
        public const string MirrorBranchSuffix = MirrorBranchOps.MirrorBranchSuffix;

        /// <summary>メッシュ GameObject 名がボーン名と衝突した時に付ける接尾辞。</summary>
        public const string MeshNameSuffix = "_skinned";

        // ================================================================
        // 状態（Build 単位）
        // ================================================================

        private readonly HierarchyBuildOptions _opt;
        private readonly Func<Mesh, Mesh> _persistMesh;

        private HierarchyBuildResult _result;

        /// <summary>
        /// 向きに F（ミラー軸まわり 180°）を掛けたミラー側 Transform
        /// （MirrorBranchSameSense の枝。MirrorBranchOps の「同じ向き」の説明）。
        /// 子のローカルを決めるとき、親がこれに入っているかを見る。
        /// </summary>
        private readonly Dictionary<Transform, int> _sameSenseMirror = new Dictionary<Transform, int>();

        /// <param name="options">生成設定。null なら既定値。</param>
        /// <param name="persistMesh">
        /// 生成した Mesh を共有アセット化するフック。null なら素通し。
        /// Editor 拡張がプレファブ保存時にだけ渡す。
        /// </param>
        public HierarchyBuilder(HierarchyBuildOptions options, Func<Mesh, Mesh> persistMesh = null)
        {
            _opt = options ?? HierarchyBuildOptions.CreateDefault();
            _persistMesh = persistMesh;
        }

        // ================================================================
        // 事前警告
        // ================================================================

        /// <summary>
        /// 「そのつもりで出したのに出ない」典型パターンを、出力の前に明示する。
        ///   ・ウェイト皆無なのにスキンドを期待している
        ///   ・MirrorType は立っているのにミラー側コンテキストが無い（＝右側は出ない）
        ///   ・ミラー側はあるのに分岐ルートが未設定（＝枝が展開されない）
        /// いずれも気付かないと無言で片側だけが出る。
        ///
        /// Humanoid 割当が空という警告はここには含めない。Avatar 生成は Editor の
        /// 関心事であり、生成そのものには関係しないため（規約 1）。
        /// </summary>
        public void WarnAboutExpectations(ModelContext model, HierarchyBuildResult result)
        {
            if (model == null || result == null) return;

            bool anyWeight     = false;
            bool anyMirrorType = false;
            bool anyMirrorSide = false;
            bool anyBranchRoot = false;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                if (mc.IsSkinned)                            anyWeight = true;
                if (mc.MirrorType > 0)                       anyMirrorType = true;
                if (MirrorBranchOps.IsMirrorSideContext(mc)) anyMirrorSide = true;
                if (mc.IsMirrorBranchRoot)                   anyBranchRoot = true;
            }

            if (!anyWeight)
            {
                result.Warn(
                    "ボーンウェイトを持つメッシュがありません。全て MeshFilter で出力します。\n"
                    + "スキンド版が必要なら、先に「MeshFilter → Skinned 変換」を実行してください。");
            }

            // 許容モードで分岐ルートが在れば、ミラー側メッシュが無くても枝から生成される。
            //   その場合この警告は事実に反するので出さない。
            bool coveredByBranch = _opt.TolerantMirrorBranch && anyBranchRoot;

            if (anyMirrorType && !anyMirrorSide && !coveredByBranch)
            {
                result.Warn(
                    "ミラーが有効（MirrorType>0）なのに、ミラー側メッシュがモデル内に存在しません。\n"
                    + "プロジェクトファイルの読込ではミラー側は生成されないため、このままでは"
                    + "反対側は出力されません。メッシュ一覧でミラーを一度 OFF→ON するか、"
                    + "ミラー分岐ルートを設定して「ミラー設定漏れを許容」を有効にしてください。");
            }

            if (!anyBranchRoot && (anyMirrorSide || anyMirrorType))
            {
                result.Warn(
                    "ミラー分岐ルートが1つも設定されていません。\n"
                    + "枝が展開されないため、関節の複製と左右の Humanoid 補完は行われません。");
            }
        }

        // ================================================================
        // 本体
        // ================================================================

        /// <summary>ModelContext を Unity ヒエラルキーに書き出す。</summary>
        public HierarchyBuildResult Build(ModelContext model)
        {
            var result = new HierarchyBuildResult();
            _result = result;
            _sameSenseMirror.Clear();

            if (model == null) return result;

            // ── ルート ────────────────────────────────────────────────
            var rootGo = new GameObject(string.IsNullOrEmpty(model.Name) ? "Model" : model.Name);
            PLEditorBridge.I.RegisterCreatedObjectUndo(rootGo, "Create Root");

            // ── ボーン Transform ツリーを構築 ─────────────────────────
            // boneTransformMap[ctxIndex] = MeshContextList インデックス ctxIndex の Transform
            Transform armatureRoot = null;
            var boneTransformMap = new Dictionary<int, Transform>();

            if (_opt.CreateArmature && !_opt.ExportMeshOnly)
            {
                bool hasBones = false;
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc?.Type == MeshType.Bone) { hasBones = true; break; }
                }

                if (hasBones)
                {
                    var armatureGo = new GameObject("Armature");
                    PLEditorBridge.I.RegisterCreatedObjectUndo(armatureGo, "Create Armature");
                    armatureGo.transform.SetParent(rootGo.transform, worldPositionStays: false);
                    armatureRoot = armatureGo.transform;

                    // 1パス目: 全ボーンの Transform を生成
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneGo = new GameObject(mc.Name ?? $"Bone_{i}");
                        PLEditorBridge.I.RegisterCreatedObjectUndo(boneGo, "Create Bone");
                        boneTransformMap[i] = boneGo.transform;
                    }

                    // 2パス目: 親子関係設定（HierarchyParentIndex）
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneTf = boneTransformMap[i];
                        int parentIdx = mc.HierarchyParentIndex;

                        if (parentIdx >= 0 && boneTransformMap.TryGetValue(parentIdx, out var parentTf))
                            boneTf.SetParent(parentTf, worldPositionStays: false);
                        else
                            boneTf.SetParent(armatureRoot, worldPositionStays: false);
                    }

                    // 3パス目: ワールド位置設定（親子確定後に WorldMatrix で配置）
                    //   親を先に置いてから子を置く。並び順で置くと、親より前に並ぶ子
                    //   （Robot Kyle の「右ひじ」8 番の親が「右腕捩」59 番）は、後から親を
                    //   動かしたときに一緒に動いてずれる（ひじから先が抜けた）。
                    var placedBones = new HashSet<int>();
                    for (int i = 0; i < model.MeshContextCount; i++)
                        PlaceBoneWorld(model, boneTransformMap, placedBones, i);
                }
            }

            // ── メッシュ書き出し ──────────────────────────────────────
            // メッシュ名がボーン名と衝突すると Humanoid 割当先の Transform 名が
            // 一意でなくなり AvatarBuilder が失敗する。衝突時のみ接尾辞を付ける。
            var usedMeshNames = CollectHierarchyNames(rootGo);

            // 静的メッシュは Depth から補正した親インデックスに従って親子にする。
            //   モデルの HierarchyParentIndex は、旧 MQO インポータの不具合により
            //   ミラー実体（例: ゆびA1）が親になる箇所でグループまで巻き戻っていることがある。
            //   ここではモデルを変更せず、Depth から解決し直した配列を使う。
            //   WorldMatrix も同じ index で累積されるため、GO 側は BoneTransform（ローカル）
            //   を設定すれば同じワールド位置になる。
            //   スキンドメッシュは BindPose 前提のためルート直下のまま。
            var parentIndices = MeshHierarchyOps.BuildParentIndicesFromDepth(model);

            // 実体側 ↔ ミラー側の index 対応表（ミラー枝の親解決・姿勢の鏡像化に使う）
            var mirrorPeers = MirrorPeerIndex.Build(model);

            var meshTransformByIndex   = new Dictionary<int, Transform>();
            var mirrorTransformByIndex = new Dictionary<int, Transform>();

            // 出力対象の索引集合。可視フィルタと親補完をここで一度に解決する。
            //   親が出力されないと CreateMeshGameObject の親解決（下の parentIndices 参照）が
            //   空振りしてルート直下へ平坦化されるため、可視ノードの祖先は補完する。

            // ミラー分岐の解析（分岐ルート配下を実体側／ミラー側に振り分ける）。
            //   可視性には依存しないので補完より前に出せる。ミラー相方の補完判定に使うため
            //   「ミラー解析 → 不可視補完 → 生成」の順に固定する。
            //   許容モードでは、ミラー側コンテキストを持たない実体側ノードにも
            //   ミラー枝のノードを作る（形状はその場で鏡像化する。モデルは変えない）。
            var branchPlan = MirrorBranchOps.BuildMirrorBranchPlan(
                model, parentIndices,
                _opt.TolerantMirrorBranch
                    ? MirrorBranchTolerance.Tolerant
                    : MirrorBranchTolerance.Strict,
                mirrorPeers);
            var branchSide = branchPlan.Side;

            int generatedMirrorCount = branchPlan.CollectGeneratedMirrors().Count;
            if (generatedMirrorCount > 0)
                result.Note(
                    $"ミラー設定の無い枝内オブジェクト {generatedMirrorCount} 件から"
                    + "ミラー側を生成して出力します（許容モード）。");

            var exportTargets = BuildExportTargets(model, parentIndices, mirrorPeers, branchSide);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                // 実体メッシュに加えてミラー側も出力する。
                //   MirrorSide / BakedMirror は実頂点を持つ（PMXエクスポートと同じ扱い）。
                //   ボーン・モーフ・剛体・JOINT は除外。
                if (mc.Type != MeshType.Mesh &&
                    mc.Type != MeshType.MirrorSide &&
                    mc.Type != MeshType.BakedMirror) continue;
                if (mc.MeshObject == null) continue;
                if (!exportTargets.Contains(i)) continue;

                bool isSkinned = _opt.RendererMode != HierarchyRendererMode.ForceMeshFilter
                              && mc.IsSkinned && boneTransformMap.Count > 0
                              && !result.SupplementedIndices.Contains(i);

                // 頂点を持たないノードは関節（グループ）として扱い、空の GameObject にする。
                // 補完で追加した不可視ノードも同じ扱い（Transform のみ・レンダラなし）。
                bool isJoint = mc.MeshObject.Vertices.Count == 0
                            || result.SupplementedIndices.Contains(i);

                Mesh unityMesh = null;
                if (!isJoint && _opt.CreateUnityMeshes)
                {
                    // Unityメッシュはブリッジ経由（MeshObject.ToUnityMesh）で毎回生成する。
                    //   mc.UnityMesh は表示用に WorldMatrix を焼き込んだメッシュのため
                    //   （UnifiedSystemAdapter が ToUnityMesh(xform) で作る）、
                    //   これを使うと GameObject の Transform と二重に位置が適用される。
                    //
                    //   行列版を単位行列で呼ぶ。引数なし版は頂点の名寄せキーに
                    //   法線サブindexを含まないため、法線が分岐する頂点で
                    //   三角形の参照が引けず面が欠落する（穴が開く）。
                    unityMesh = mc.MeshObject.ToUnityMesh(Matrix4x4.identity);
                    if (unityMesh == null) continue;
                }

                // 分岐内での所属側。ミラー分岐はスキンドでは扱わない。
                int  side     = 0;
                bool inBranch = !isSkinned && branchSide.TryGetValue(i, out side);
                if (!inBranch) side = 0;

                // 関節は両側に複製する。メッシュは所属側のみ。
                //   ただし補完で追加したミラー側コンテキストは「ミラー枝の親の受け皿」なので
                //   isJoint でもミラー側だけに出す（実体側へ空ノードを増やさない）。
                //   分岐の外のミラー側コンテキスト（右目など）は受け皿ではない。
                //   Humanoid 割当先として補完されたものは通常どおり実体側に出す
                //   （ミラー枝に出ないので、ここで止めると階層から消える）。
                bool supplementedMirrorSide =
                    result.SupplementedIndices.Contains(i) && MirrorBranchOps.IsMirrorSideContext(mc)
                    && inBranch;

                // ミラー枝に出すかは出力計画が決める。
                //   ・ミラー側コンテキスト（従来）
                //   ・許容モードで、ミラー相方を持たない実体側ノード
                // 関節の両側複製は計画の外なので、ここで OR する（従来どおり）。
                bool emitMirror = !isSkinned && branchPlan.EmitsMirror(i);

                bool makeNormal = !supplementedMirrorSide && (!inBranch || isJoint || side == 0);
                bool makeMirror = (inBranch && isJoint) || emitMirror;

                // ミラー枝の形状を実体側から作る必要があるか（＝ミラー設定漏れの救済）。
                bool generateMirrorShape = emitMirror && branchPlan.GeneratesMirrorShape(i);

                if (makeNormal)
                    CreateMeshGameObject(
                        model, mc, i, unityMesh, isSkinned, isJoint, mirror: false,
                        rootGo, armatureRoot, boneTransformMap,
                        meshTransformByIndex, mirrorTransformByIndex, usedMeshNames,
                        parentIndices, mirrorPeers, generateMirrorShape: false);

                if (makeMirror)
                    CreateMeshGameObject(
                        model, mc, i, unityMesh, isSkinned, isJoint, mirror: true,
                        rootGo, armatureRoot, boneTransformMap,
                        meshTransformByIndex, mirrorTransformByIndex, usedMeshNames,
                        parentIndices, mirrorPeers, generateMirrorShape);
            }

            // ── 描画オブジェクトを親に持つボーン ──────────────────────
            //   ボーンの親（HierarchyParentIndex）が描画オブジェクトのとき、
            //   2 パス目ではボーンの表に親が無いので Armature 直下に置いてある。
            //   モデルの親子どおり、その描画オブジェクトの下へ移す（ワールド姿勢は保つ）。
            //   親がミラー枝に複製されていれば、ボーンの枝もミラー側へ複製する。
            if (boneTransformMap.Count > 0)
                AttachBonesToDrawableParents(
                    model, boneTransformMap, meshTransformByIndex, mirrorTransformByIndex, usedMeshNames);

            // ── 剛体/JOINT 書き出し ──────────────────────────────────
            if (_opt.ExportPhysics && !_opt.ExportMeshOnly)
                HierarchyPhysicsBuilder.Build(model, rootGo, boneTransformMap, result);

            // ── 索引→Transform 表を確定 ──────────────────────────────
            //
            // 【規約】索引は常に「割当対象ノード（＝実体側）の MeshContext 索引」。
            //   RealTransformByIndex[i]   … 索引 i のノードの実体側 GameObject
            //   MirrorTransformByIndex[i] … 索引 i のノードのミラー側 GameObject
            //
            //   ミラー側 GameObject の作られ方は 2 通りあり、由来ノードの索引が
            //   一致するとは限らない。両方をこの規約へ寄せてから格納する。
            //     A: 頂点ゼロの関節（左肩・左腕 …）
            //        ミラー側 MeshContext が存在しない（CreateDerivedMirrorContext は
            //        頂点ゼロで null を返す）ため、CreateMeshGameObject が実体側と
            //        同じ索引でミラー枝にも GameObject を作る。索引は一致する。
            //     B: 頂点を持つメッシュ（左人指１・左つま先・左目 …）
            //        ミラーはインポータが作った別の MeshContext（MirrorSide）で、
            //        GameObject はその相方自身の索引で登録される。索引が一致しない。
            //
            //   B を実体側索引へ寄せずに mirrorTransformByIndex をそのまま流すと、
            //   消費側は「ミラー枝が無い」と誤認して左右補完を黙って諦める。
            //   規約は表の側で満たす（消費側ごとに引き直さない）。
            foreach (var kv in boneTransformMap)     result.RealTransformByIndex[kv.Key] = kv.Value;
            foreach (var kv in meshTransformByIndex) result.RealTransformByIndex[kv.Key] = kv.Value;

            foreach (var kv in boneTransformMap)     result.BoneTransformByIndex[kv.Key] = kv.Value;

            // A: 由来ノードの索引がそのまま実体側索引。
            foreach (var kv in mirrorTransformByIndex) result.MirrorTransformByIndex[kv.Key] = kv.Value;

            // B: ミラー側 MeshContext の GameObject を、実体側相方の索引へ登録する。
            //    ミラー分岐ルート配下でないノード（目など）のミラー側は
            //    branchSide に載らず makeNormal 側へ回るため meshTransformByIndex に
            //    入る。どちらの表からも拾う。
            //    A で既に埋まっている索引は上書きしない（関節の複製が正）。
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (result.MirrorTransformByIndex.ContainsKey(i)) continue;
                if (!mirrorPeers.TryGetMirror(i, out int peerIndex)) continue;

                if (mirrorTransformByIndex.TryGetValue(peerIndex, out var peerTf) ||
                    meshTransformByIndex.TryGetValue(peerIndex, out peerTf))
                {
                    if (peerTf != null) result.MirrorTransformByIndex[i] = peerTf;
                }
            }

            // モーフの取りこぼしは黙って 0 件になると原因が追えないので、
            // どのモーフがなぜ載らなかったかを警告として残す。
            if (_opt.ExportMorphTargets) ReportUnattachedMorphs(model, result);

            result.BoneCount                 = boneTransformMap.Count;
            result.ExportedNodeCount         = meshTransformByIndex.Count + mirrorTransformByIndex.Count;
            result.SupplementedAncestorCount = result.SupplementedIndices.Count;
            result.Root                      = rootGo;

            return result;
        }

        // ================================================================
        // メッシュ／関節の GameObject
        // ================================================================

        /// <summary>
        /// メッシュ／関節の GameObject を1つ生成する。
        /// mirror=true のときはミラー側の枝に配置し、関節のローカル姿勢を鏡像化する。
        /// </summary>
        /// <param name="generateMirrorShape">
        /// ミラー枝に出す形状を実体側から作るか（許容モードでミラー設定漏れを救済する場合）。
        /// false のときは mc 自身が既に鏡像済みのミラー側コンテキストである前提。
        /// </param>
        private void CreateMeshGameObject(
            ModelContext model, MeshContext mc, int index, Mesh unityMesh,
            bool isSkinned, bool isJoint, bool mirror,
            GameObject rootGo, Transform armatureRoot,
            Dictionary<int, Transform> boneTransformMap,
            Dictionary<int, Transform> meshTransformByIndex,
            Dictionary<int, Transform> mirrorTransformByIndex,
            HashSet<string> usedMeshNames,
            int[] parentIndices,
            MirrorPeerIndex peers,
            bool generateMirrorShape)
        {
            string rawName = string.IsNullOrEmpty(mc.Name) ? $"Mesh_{index}" : mc.Name;

            // ミラー側の関節は元と同名になるため別名にする。
            //   まず左右対応で解決（左腕 → 右腕）。左右を持たない名前（センター等）と
            //   既に使われている名前だけ従来の接尾辞（"+"）へ落とす。
            // ミラー側メッシュ（MirrorSide / BakedMirror）は元から実体側と別名なので触らない。
            if (mirror && !MirrorBranchOps.IsMirrorSideContext(mc))
                rawName = MirrorNameOps.MakeMirrorName(
                    rawName, MirrorBranchSuffix, usedMeshNames.Contains);

            string goName = usedMeshNames.Contains(rawName)
                ? MakeUniqueName(rawName + MeshNameSuffix, usedMeshNames)
                : MakeUniqueName(rawName, usedMeshNames);

            var go = new GameObject(goName);
            PLEditorBridge.I.RegisterCreatedObjectUndo(go, "Create Mesh GameObject");

            // 親の解決（同じ側を優先し、無ければ実体側 → ボーン → ルートの順）
            Transform parentTf = rootGo.transform;
            if (!isSkinned)
            {
                int hp = (parentIndices != null && index < parentIndices.Length)
                    ? parentIndices[index]
                    : mc.HierarchyParentIndex;
                // 階層親がミラー側相方を持つ場合はそちらを親にする。
                //   例）ゆびB1+ の階層親は実体側 ゆびA1 だが、ミラー枝では ゆびA1+ にぶら下げる。
                //   相方の無い関節（両側に複製される）は階層親そのものをミラー枝で引く。
                if (MirrorBranchOps.TryResolveMirrorParent(
                        peers, hp, mirror,
                        idx => mirrorTransformByIndex.ContainsKey(idx),
                        out int parentIndex, out bool parentIsMirrorSide))
                {
                    if (parentIsMirrorSide)
                    {
                        if (mirrorTransformByIndex.TryGetValue(parentIndex, out var mTf))
                            parentTf = mTf;
                    }
                    else if (meshTransformByIndex.TryGetValue(parentIndex, out var nTf))
                        parentTf = nTf;
                    else if (boneTransformMap.TryGetValue(parentIndex, out var bTf))
                        parentTf = bTf;
                }
            }

            go.transform.SetParent(parentTf, worldPositionStays: false);

            if (mirror) mirrorTransformByIndex[index] = go.transform;
            else        meshTransformByIndex[index]   = go.transform;

            bool hasMeshParent = parentTf != rootGo.transform;

            // ミラー側は自分の BoneTransform が既に反転済みの値を持つ（原点CSVで設定される）。
            // そのまま鏡像化に通すと二重反転になるため、実体側相方の値を使う。
            MeshContext realPeer = null;
            if (mirror && MirrorBranchOps.IsMirrorSideContext(mc) &&
                peers != null && peers.TryGetReal(index, out int realIdx))
                realPeer = model.GetMeshContext(realIdx);

            MeshContext trsSource = realPeer ?? mc;

            // 同じ向き（MirrorBranchSameSense）の枝のミラー側か。非スキンドのミラー枝だけが対象。
            //   parentAxis … 親が F 付きならその軸、selfAxis … 自分に F を掛けるならその軸（無ければ 0）。
            int parentAxis = 0, selfAxis = 0;
            if (mirror && !isSkinned)
            {
                _sameSenseMirror.TryGetValue(parentTf, out parentAxis);
                if (MirrorBranchOps.IsInSameSenseBranch(model, parentIndices, index))
                    selfAxis = MirrorBranchOps.NormalizeMirrorAxis(trsSource.MirrorAxis);
            }

            if (isJoint)
            {
                // 関節はレンダラを持たない。姿勢のみ設定する。
                ApplyJointTransform(go, mc, trsSource, hasMeshParent, mirror);
                ApplySameSenseTransform(go.transform, parentAxis, selfAxis);
                return;
            }

            if (isSkinned)
            {
                AttachSkinnedMesh(go, mc, unityMesh, model, boneTransformMap, armatureRoot, index);
                return;
            }

            // ミラー側は姿勢を鏡像化するため、頂点も新しいピボット基準に直す。
            //   ・ミラー側コンテキスト … 形状は既に反転済み。ピボット差だけ平行移動する。
            //   ・許容モードの生成    … 実体側から鏡像を作る（反転と巻き順の反転を伴う）。
            Mesh meshForGo = unityMesh;

            // ブレンドシェイプの差分は「meshForGo を作った MeshObject」から作る必要がある。
            // 実体側とミラー枝では面の巻き順が違うため、ここを取り違えると並びがずれる。
            MeshObject shapeSource = mc.MeshObject;
            bool mirrorDelta = false;
            Vector3 vertexOffset = Vector3.zero;

            if (mirror)
            {
                bool failed;
                if (generateMirrorShape)
                {
                    var mirroredObject = BuildMirroredShape(mc);
                    meshForGo = mirroredObject != null && _opt.CreateUnityMeshes
                        ? BuildGeneratedMirrorMesh(mirroredObject, unityMesh, mc, goName)
                        : null;
                    failed = mirroredObject == null || (_opt.CreateUnityMeshes && meshForGo == null);
                    if (!failed)
                    {
                        shapeSource = mirroredObject;
                        mirrorDelta = true;
                    }
                }
                else
                {
                    // ミラー側コンテキストは形状が既に反転済みで、
                    // BuildMirrorSideMesh の補正は全頂点一律の平行移動なので差分に影響しない。
                    vertexOffset = MirrorSideVertexOffset(mc, realPeer);
                    meshForGo = _opt.CreateUnityMeshes ? BuildMirrorSideMesh(unityMesh, vertexOffset) : null;
                    failed = _opt.CreateUnityMeshes && meshForGo == null;
                }

                if (failed)
                {
                    _result.Warn($"ミラー側メッシュを生成できませんでした: \"{mc.Name}\"");
                    meshForGo    = unityMesh;
                    shapeSource  = mc.MeshObject;
                    mirrorDelta  = false;
                    vertexOffset = Vector3.zero;
                }
            }

            // F を掛けるメッシュは自分専用にする（実体側と同じ Unity メッシュを使い回す経路があるため）。
            //   ブレンドシェイプは AttachStaticMesh が載せ直すので、写しからは外しておく。
            if (selfAxis != 0 && meshForGo != null && ReferenceEquals(meshForGo, unityMesh))
            {
                var own = UnityEngine.Object.Instantiate(unityMesh);
                own.name = string.IsNullOrEmpty(goName) ? unityMesh.name : goName;
                own.ClearBlendShapes();
                meshForGo = own;
            }

            AttachStaticMesh(go, mc, meshForGo, model, hasMeshParent, mirror, trsSource,
                             index, shapeSource, mirrorDelta, vertexOffset, selfAxis);
            ApplySameSenseTransform(go.transform, parentAxis, selfAxis);
        }

        /// <summary>
        /// 鏡像化を済ませたローカル姿勢へ F を掛け、F 付きなら表に登録する
        /// （MirrorBranchOps の「同じ向き」の説明）。
        /// </summary>
        private void ApplySameSenseTransform(Transform t, int parentAxis, int selfAxis)
        {
            if (t == null || (parentAxis == 0 && selfAxis == 0)) return;

            var pos = t.localPosition;
            var rot = t.localRotation;
            MirrorBranchOps.ApplySameSenseFlip(parentAxis, selfAxis, ref pos, ref rot);
            t.localPosition = pos;
            t.localRotation = rot;

            if (selfAxis != 0) _sameSenseMirror[t] = selfAxis;
        }

        /// <summary>
        /// Unity メッシュの頂点・法線・接線・ブレンドシェイプ差分に F を掛ける。
        /// F は回転なので巻き順と接線の w はそのまま。
        /// </summary>
        private static void ApplySameSenseToMesh(Mesh mesh, int axis)
        {
            if (mesh == null || axis == 0) return;

            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = MirrorBranchOps.SameSenseFlip(axis, v[i]);
            mesh.vertices = v;

            var n = mesh.normals;
            if (n != null && n.Length == v.Length)
            {
                for (int i = 0; i < n.Length; i++) n[i] = MirrorBranchOps.SameSenseFlip(axis, n[i]);
                mesh.normals = n;
            }

            var t = mesh.tangents;
            if (t != null && t.Length == v.Length)
            {
                for (int i = 0; i < t.Length; i++)
                {
                    var d = MirrorBranchOps.SameSenseFlip(axis, new Vector3(t[i].x, t[i].y, t[i].z));
                    t[i] = new Vector4(d.x, d.y, d.z, t[i].w);
                }
                mesh.tangents = t;
            }

            int shapeCount = mesh.blendShapeCount;
            if (shapeCount > 0)
            {
                int vc = mesh.vertexCount;
                var names   = new string[shapeCount];
                var weights = new List<float>[shapeCount];
                var dvs     = new List<Vector3[]>[shapeCount];
                var dns     = new List<Vector3[]>[shapeCount];
                var dts     = new List<Vector3[]>[shapeCount];
                for (int s = 0; s < shapeCount; s++)
                {
                    names[s]   = mesh.GetBlendShapeName(s);
                    weights[s] = new List<float>();
                    dvs[s] = new List<Vector3[]>(); dns[s] = new List<Vector3[]>(); dts[s] = new List<Vector3[]>();
                    int frames = mesh.GetBlendShapeFrameCount(s);
                    for (int f = 0; f < frames; f++)
                    {
                        var dv = new Vector3[vc]; var dn = new Vector3[vc]; var dt = new Vector3[vc];
                        mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                        for (int i = 0; i < vc; i++)
                        {
                            dv[i] = MirrorBranchOps.SameSenseFlip(axis, dv[i]);
                            dn[i] = MirrorBranchOps.SameSenseFlip(axis, dn[i]);
                            dt[i] = MirrorBranchOps.SameSenseFlip(axis, dt[i]);
                        }
                        weights[s].Add(mesh.GetBlendShapeFrameWeight(s, f));
                        dvs[s].Add(dv); dns[s].Add(dn); dts[s].Add(dt);
                    }
                }
                mesh.ClearBlendShapes();
                for (int s = 0; s < shapeCount; s++)
                    for (int f = 0; f < weights[s].Count; f++)
                        mesh.AddBlendShapeFrame(names[s], weights[s][f], dvs[s][f], dns[s][f], dts[s][f]);
            }

            mesh.RecalculateBounds();
        }

        // ================================================================
        // ミラー枝のメッシュ
        // ================================================================

        /// <summary>
        /// ミラー設定を持たない実体側ノードから、ミラー枝用のメッシュを新しく作る。
        ///
        /// ピボットは ApplyJointTransform / AttachStaticMesh が MirrorLocalTRS で
        /// 鏡像化する。ローカル姿勢は L' = S_d · L · S_0 の形になるため
        /// （S_d はミラー面 x=d での反射、S_0 は原点まわりの反射）、
        /// 頂点に掛けるべきなのは距離を含まない S_0 の側になる。
        /// よって BuildMirroredMeshObject には距離 0 を渡す。
        ///
        /// 実体側の UnityMesh をそのまま使い回すと巻き順が裏返ったままになるため、
        /// MeshObject 段階で鏡像化してから Unity メッシュへ変換する。
        /// </summary>
        /// <remarks>
        /// 鏡像化した MeshObject は BuildMirroredShape、Unity メッシュへの変換はこの関数。
        /// ブレンドシェイプの差分は、その MeshObject を元にクローンして作らなければ並びがずれる
        /// （HierarchyMorphBuilder.cs 冒頭を参照）。
        /// </remarks>
        private static Mesh BuildGeneratedMirrorMesh(
            MeshObject mirroredObj, Mesh source, MeshContext mc, string goName)
        {
            if (mirroredObj == null) return null;

            // 行列版を単位行列で呼ぶ理由は実体側と同じ（法線が分岐する頂点の欠落回避）。
            var mesh = mirroredObj.ToUnityMesh(Matrix4x4.identity);
            if (mesh == null) return null;

            // アセット名は GameObject 名（既に左右入替済み）に合わせる。
            mesh.name = string.IsNullOrEmpty(goName)
                ? (source != null ? source.name : mc.Name) + MirrorBranchSuffix
                : goName;

            return mesh;
        }

        /// <summary>
        /// ミラー枝用の形状（鏡像化と巻き順の反転を済ませた MeshObject）を作る。
        /// 距離 0 の理由は BuildGeneratedMirrorMesh の説明のとおり。作れなければ null。
        /// </summary>
        private static MeshObject BuildMirroredShape(MeshContext mc)
        {
            if (mc?.MeshObject == null) return null;

            return MirrorBranchOps.BuildMirroredMeshObject(
                mc.MeshObject,
                MirrorBranchOps.ResolveMirrorAxis(mc),
                0f,                            // 距離はピボット側が吸収する
                mc.MirrorMaterialOffset,
                mc.Name);
        }

        /// <summary>
        /// ミラー側 GameObject 用のメッシュを生成する。
        ///
        /// PolyLing の MirrorSide 頂点は「実体側と同じ親（＝実体側のピボット）」を基準にした
        /// ローカル座標で保持されている。ミラー側の枝ではピボットを鏡像位置へ動かすため、
        /// その差分だけ頂点を平行移動しないと位置がずれる。
        ///
        /// ミラー側 GO のピボットは「実体側ピボットの鏡像」に置かれる。
        /// mc のワールド原点を W、実体側相方のワールド原点を R、ミラー軸の面を x = d とすると、
        /// 鏡像ピボットは mirror(R) で、頂点に必要な補正は W - mirror(R)。
        ///
        /// 原点CSV未適用（MirrorSide の原点が実体側と同一）なら W == R となり
        /// 補正量は 2(W - d) で従来と一致する。相方が取れない場合も同式にフォールバックする。
        /// 形状自体は MirrorSide が既に反転済みのため、反転や巻き順の変更は行わない。
        /// 補正量は MirrorSideVertexOffset が求める（MeshObject から直接書く書き出しも同じ量を使う）。
        /// </summary>
        private static Mesh BuildMirrorSideMesh(Mesh source, Vector3 offset)
        {
            if (source == null) return null;

            // Vector3 の == は誤差を許すので成分ごとに比べる（0 は MirrorSideVertexOffset が丸めた値）。
            if (offset.x == 0f && offset.y == 0f && offset.z == 0f) return source;

            var mesh = UnityEngine.Object.Instantiate(source);
            // 「左腕+」ではなく「右腕」にする。左右対応が付かない名前だけ接尾辞へ落ちる。
            mesh.name = MirrorNameOps.MakeMirrorName(source.name, MirrorBranchSuffix, null);

            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] += offset;
            mesh.vertices = verts;

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// ミラー側コンテキストの頂点に足す補正量 W − mirror(R)（BuildMirrorSideMesh の説明）。
        /// 長さの 2 乗が 1e-12 未満なら 0（補正しない）。
        /// </summary>
        private static Vector3 MirrorSideVertexOffset(MeshContext mc, MeshContext realPeer)
        {
            var wm = mc.WorldMatrix;
            var w  = new Vector3(wm.m03, wm.m13, wm.m23);

            // ミラー軸・距離は実体側が正本
            var axisSource = realPeer ?? mc;
            float d = axisSource.MirrorDistance;

            Vector3 pivot = w;
            if (realPeer != null)
            {
                var rwm = realPeer.WorldMatrix;
                pivot = new Vector3(rwm.m03, rwm.m13, rwm.m23);
            }

            Vector3 mirrored;
            switch (axisSource.MirrorAxis)
            {
                case 2:  mirrored = new Vector3(pivot.x, 2f * d - pivot.y, pivot.z); break;   // Y
                case 4:  mirrored = new Vector3(pivot.x, pivot.y, 2f * d - pivot.z); break;   // Z
                default: mirrored = new Vector3(2f * d - pivot.x, pivot.y, pivot.z); break;   // X
            }

            Vector3 offset = w - mirrored;

            return offset.sqrMagnitude < 1e-12f ? Vector3.zero : offset;
        }

        /// <summary>
        /// 親が描画オブジェクトのボーンを、その描画オブジェクトの GameObject の下へ移す。
        /// 親がミラー枝にも出ていれば、ボーンとその子孫のボーンをミラー側へ複製する。
        /// 複製のローカル姿勢は実体側のローカルを鏡映 S で共役にしたもの（S·L·S）。
        /// 複製は mirrorTransformByIndex に実体側ボーンの索引で登録する
        /// （Humanoid の左右補完がミラー側を引けるように）。
        /// </summary>
        private void AttachBonesToDrawableParents(
            ModelContext model,
            Dictionary<int, Transform> boneTransformMap,
            Dictionary<int, Transform> meshTransformByIndex,
            Dictionary<int, Transform> mirrorTransformByIndex,
            HashSet<string> usedMeshNames)
        {
            var boneKids = new Dictionary<int, List<int>>();
            foreach (var kv in boneTransformMap)
            {
                int p = model.GetMeshContext(kv.Key)?.HierarchyParentIndex ?? -1;
                if (p < 0 || !boneTransformMap.ContainsKey(p)) continue;
                if (!boneKids.TryGetValue(p, out var list)) boneKids[p] = list = new List<int>();
                list.Add(kv.Key);
            }

            foreach (var kv in boneTransformMap)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc == null) continue;
                int p = mc.HierarchyParentIndex;
                if (p < 0 || boneTransformMap.ContainsKey(p)) continue;
                if (!meshTransformByIndex.TryGetValue(p, out var realParent) || realParent == null) continue;

                kv.Value.SetParent(realParent, worldPositionStays: true);

                if (mirrorTransformByIndex.TryGetValue(p, out var mirrorParent) && mirrorParent != null)
                {
                    int axis = model.GetMeshContext(p)?.MirrorAxis ?? 1;
                    CloneBoneToMirror(kv.Key, kv.Value, mirrorParent, axis,
                                      boneTransformMap, boneKids, mirrorTransformByIndex, usedMeshNames);
                }
            }
        }

        private void CloneBoneToMirror(
            int boneIndex, Transform src, Transform mirrorParent, int mirrorAxis,
            Dictionary<int, Transform> boneTransformMap,
            Dictionary<int, List<int>> boneKids,
            Dictionary<int, Transform> mirrorTransformByIndex,
            HashSet<string> usedMeshNames)
        {
            string name = MirrorNameOps.MakeMirrorName(src.name, MirrorBranchSuffix, usedMeshNames.Contains);
            var go = new GameObject(MakeUniqueName(name, usedMeshNames));
            PLEditorBridge.I.RegisterCreatedObjectUndo(go, "Create Mirror Bone");
            go.transform.SetParent(mirrorParent, worldPositionStays: false);

            // 入れ子のローカルなので鏡映面は親の原点を通る（距離 0）。
            var pos = src.localPosition;
            var rot = src.localEulerAngles;
            MirrorBranchOps.MirrorLocalTRS(mirrorAxis, 0f, ref pos, ref rot);
            go.transform.localPosition    = pos;
            go.transform.localEulerAngles = rot;
            go.transform.localScale       = src.localScale;

            // 同じ向きの枝の中なら、ボーンも親と同じ F を掛ける（枝の中の子と同じ規則）。
            if (_sameSenseMirror.TryGetValue(mirrorParent, out int sameSenseAxis))
                ApplySameSenseTransform(go.transform, sameSenseAxis, sameSenseAxis);

            mirrorTransformByIndex[boneIndex] = go.transform;

            if (!boneKids.TryGetValue(boneIndex, out var kids)) return;
            foreach (int k in kids)
                if (boneTransformMap.TryGetValue(k, out var kTf) && kTf != null)
                    CloneBoneToMirror(k, kTf, go.transform, mirrorAxis,
                                      boneTransformMap, boneKids, mirrorTransformByIndex, usedMeshNames);
        }

        /// <summary>
        /// 関節ノードの Transform を設定する。mirror=true ではピボットを鏡像化する。
        /// trsSource は鏡像化の元にする姿勢の持ち主（MirrorSide の場合は実体側相方）。
        /// </summary>
        private static void ApplyJointTransform(
            GameObject go, MeshContext mc, MeshContext trsSource, bool hasMeshParent, bool mirror)
        {
            var src = trsSource ?? mc;

            if (hasMeshParent)
            {
                // ミラー側は回転中心を反対側へ持っていく必要があるため、
                // ローカル姿勢を鏡像化する。配下メッシュの頂点は
                // BuildMirrorSideMesh 側でこのピボット基準に合わせる。
                var bt  = src.BoneTransform;
                var pos = bt.Position;
                var rot = bt.Rotation;

                if (mirror) MirrorBranchOps.MirrorLocalTRS(src, ref pos, ref rot);

                go.transform.localPosition    = pos;
                go.transform.localEulerAngles = rot;
                go.transform.localScale       = bt.Scale;
                return;
            }

            // 親を持たない場合はワールド指定。
            //   分岐ルートがモデルのルート（HierarchyParentIndex == -1）でも
            //   成立させる必要があるため、ミラー側はここでも鏡像化する。
            //   親が無いときのローカル基準はモデル原点そのものなので、
            //   ワールド値に同じ規則（MirrorLocalTRS）を適用すれば
            //   親を持つ場合と同じ結果になる。
            //   飛ばすとミラー側ルートが実体側と同じ位置に出て、
            //   配下のミラー枝ごと実体側へ重なる。
            var wm   = mc.WorldMatrix;
            var wPos = new Vector3(wm.m03, wm.m13, wm.m23);
            var wRot = wm.rotation.eulerAngles;
            var wScl = wm.lossyScale;

            if (mirror) MirrorBranchOps.MirrorLocalTRS(src, ref wPos, ref wRot);

            go.transform.position   = wPos;
            go.transform.rotation   = Quaternion.Euler(wRot);
            go.transform.localScale = wScl;
        }

        // ================================================================
        // 出力対象の解決
        // ================================================================

        /// <summary>
        /// 出力対象の索引集合を作る。
        ///
        /// ・「可視メッシュのみ」が OFF なら全メッシュ系ノードが対象。
        /// ・ON のときは可視ノードを起点に parentIndices を親方向へたどり、
        ///   途中の不可視ノードを補完して対象へ加える（SupplementedIndices に印を付ける）。
        ///   補完しないと、その子は親 Transform を引けずルート直下へ平坦化される。
        /// ・補完したノードは Transform のみで出力する（隠した形状は出さない）。
        /// ・ミラー枝は親をミラー相方で解決する（MirrorBranchOps.TryResolveMirrorParent）。
        ///   相方が不可視で出力されないと実体側の親へフォールバックしてピボットが
        ///   実体側基準に落ちるため、親として使う実体側ノードのミラー相方も補完する。
        /// </summary>
        private HashSet<int> BuildExportTargets(
            ModelContext model, int[] parentIndices,
            MirrorPeerIndex peers, Dictionary<int, int> branchSide)
        {
            _result.SupplementedIndices.Clear();

            var targets = new HashSet<int>();
            if (model == null) return targets;

            // 候補（メッシュ系ノード）と可視ノードを拾う。
            var candidates = new HashSet<int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.MeshObject == null) continue;
                if (mc.Type != MeshType.Mesh &&
                    mc.Type != MeshType.MirrorSide &&
                    mc.Type != MeshType.BakedMirror) continue;

                candidates.Add(i);
                if (!_opt.ExportVisibleOnly || mc.IsVisible) targets.Add(i);
            }

            if (!_opt.ExportVisibleOnly) return targets;

            int skipped = candidates.Count - targets.Count;

            if (_opt.IncludeInvisibleAncestors)
            {
                // Humanoid の割当先は関節そのものなので、非表示でも Transform として要る。
                // 欠けると Avatar の必須関節が階層に無くなり Avatar を作れない。
                // 不可視の親と同じく補完扱い（Transform のみ）で対象へ加え、
                // 下の祖先たどりの起点にも含める。
                var mapping = model.HumanoidMapping;
                if (mapping != null && !mapping.IsEmpty)
                {
                    foreach (var kv in mapping.BoneIndexMap)
                    {
                        int mi = kv.Value;
                        if (!candidates.Contains(mi) || targets.Contains(mi)) continue;
                        targets.Add(mi);
                        _result.SupplementedIndices.Add(mi);
                    }
                }

                // 可視ノードそれぞれから親をたどる。既に対象なら打ち切ってよい。
                var seeds = new List<int>(targets);
                foreach (int seed in seeds)
                {
                    int cur   = ResolveParentIndex(model, parentIndices, seed);
                    int guard = 0;

                    while (cur >= 0 && guard++ < 4096)
                    {
                        if (targets.Contains(cur)) break;
                        if (!candidates.Contains(cur))
                        {
                            // ボーン等はこのループの対象外。親方向の探索も打ち切る。
                            break;
                        }

                        targets.Add(cur);
                        _result.SupplementedIndices.Add(cur);

                        cur = ResolveParentIndex(model, parentIndices, cur);
                    }
                }

                // ── 第2段階: ミラー相方の補完 ────────────────────────
                //   親として使われる実体側ノードのミラー相方が不可視で欠けると、
                //   TryResolveMirrorParent が実体側へフォールバックしてミラー枝が崩れる。
                var parentsInUse = new HashSet<int>();
                foreach (int idx in targets)
                {
                    int pi = ResolveParentIndex(model, parentIndices, idx);
                    if (pi >= 0) parentsInUse.Add(pi);
                }

                foreach (int pi in parentsInUse)
                {
                    if (peers == null) break;
                    if (!peers.TryGetMirror(pi, out int mirrorIdx)) continue;
                    if (targets.Contains(mirrorIdx)) continue;
                    if (!candidates.Contains(mirrorIdx)) continue;

                    // ミラー枝の中にあるものだけを補完する。
                    if (branchSide == null ||
                        !branchSide.TryGetValue(mirrorIdx, out int side) ||
                        side != MirrorBranchOps.SideMirror) continue;

                    targets.Add(mirrorIdx);
                    _result.SupplementedIndices.Add(mirrorIdx);
                }

                skipped -= _result.SupplementedIndices.Count;

                if (_result.SupplementedIndices.Count > 0)
                {
                    var names = new List<string>();
                    foreach (int idx in _result.SupplementedIndices)
                        names.Add(model.GetMeshContext(idx)?.Name ?? $"[{idx}]");

                    _result.Note(
                        $"不可視の親を {_result.SupplementedIndices.Count} 件補完しました"
                        + "（Transform のみ）: " + string.Join(", ", names));
                }
            }

            _result.SkippedInvisibleCount = skipped < 0 ? 0 : skipped;
            return targets;
        }

        /// <summary>
        /// ボーン index の Transform を WorldMatrix の位置・回転へ置く。親ボーンが未配置なら先に親を置く。
        /// 親子は 2 パス目で HierarchyParentIndex に従って組んだものと同じ。
        /// placed に入れてから親をたどるので、親子が輪になっていても止まる。
        /// </summary>
        private static void PlaceBoneWorld(
            ModelContext model, Dictionary<int, Transform> boneTransformMap, HashSet<int> placed, int index)
        {
            if (!boneTransformMap.TryGetValue(index, out var boneTf)) return;
            if (!placed.Add(index)) return;

            var mc = model.GetMeshContext(index);
            if (mc == null) return;

            int parentIdx = mc.HierarchyParentIndex;
            if (parentIdx >= 0 && boneTransformMap.ContainsKey(parentIdx))
                PlaceBoneWorld(model, boneTransformMap, placed, parentIdx);

            var wm = mc.WorldMatrix;
            boneTf.position   = new Vector3(wm.m03, wm.m13, wm.m23);
            boneTf.rotation   = wm.rotation;
            boneTf.localScale = Vector3.one;
        }

        /// <summary>Depth 補正済みの親インデックスを引く。範囲外なら HierarchyParentIndex。</summary>
        private static int ResolveParentIndex(ModelContext model, int[] parentIndices, int index)
        {
            if (parentIndices != null && index >= 0 && index < parentIndices.Length)
                return parentIndices[index];

            return model.GetMeshContext(index)?.HierarchyParentIndex ?? -1;
        }

        // ================================================================
        // SkinnedMeshRenderer アタッチ
        // ================================================================

        private void AttachSkinnedMesh(
            GameObject go,
            MeshContext mc,
            Mesh unityMesh,
            ModelContext model,
            Dictionary<int, Transform> boneTransformMap,
            Transform armatureRoot,
            int index)
        {
            // BoneWeight の bone0-3 は MeshContextList の索引を指す
            // （PMXImporter.ApplyBoneWeightIndexOffset が「既存 MeshContext 数」を
            //   足していることが根拠。ボーン数ではない）。
            // 一方 SkinnedMeshRenderer.bones はボーンだけを詰めた配列なので、
            // 両者が一致するのは「ボーンがリスト先頭に連続して並ぶ」ときだけ。
            // PMX 読込直後はそうなっているが、あとからボーンを足すと崩れる。
            // ここで索引 → bones 配列の位置の対応表を作り、下で頂点ウェイトを詰め替える。
            var boneList  = new List<Transform>();
            var bindposes = new List<Matrix4x4>();
            var boneSlotOf = new Dictionary<int, int>();

            for (int bi = 0; bi < model.MeshContextCount; bi++)
            {
                var bmc = model.GetMeshContext(bi);
                if (bmc == null || bmc.Type != MeshType.Bone) continue;
                if (!boneTransformMap.TryGetValue(bi, out var boneTf)) continue;

                boneSlotOf[bi] = boneList.Count;
                boneList.Add(boneTf);
                bindposes.Add(_opt.UseBindpose ? bmc.BindPose : boneTf.worldToLocalMatrix);
            }

            var materials = BuildMaterials(mc, model);

            // 形状の出どころ。スキンドはミラー枝を通らないので差分の反転は不要。
            var source = new HierarchyMeshSource
            {
                Shape       = mc.MeshObject,
                IsSkinned   = true,
                Materials   = (Material[])materials.Clone(),
                Bones       = boneList.ToArray(),
                Bindposes   = bindposes.ToArray(),
                BoneSlotOf  = boneSlotOf,
                MirrorDelta = false,
                MirrorAxis  = 1,
            };
            _result.MeshSourceOf[go.transform] = source;

            if (!_opt.CreateUnityMeshes)
            {
                AppendMorphPlan(model, index, source, go.transform);
                return;
            }

            var smr = PLEditorBridge.I.AddComponent<SkinnedMeshRenderer>(go);

            // bindposes はメッシュ複製側に設定（共有メッシュを汚さない）。
            var mesh = UnityEngine.Object.Instantiate(unityMesh);
            mesh.name = unityMesh.name;
            mesh.bindposes = bindposes.ToArray();

            RemapBoneWeights(mesh, boneSlotOf, mc.Name);

            // マテリアル配列は空サブメッシュ除去と対で扱う（index が連動するため）。
            if (_opt.DropEmptySubMeshes) CompactSubMeshes(mesh, ref materials);

            // ブレンドシェイプはアセット化より前に載せる（アセットへ焼き込むため）。
            AppendMorphTargets(model, index, source, mesh, go.transform);

            // 共有アセット化するかどうかは呼び出し側の決定（規約 1）。
            mesh = PersistMesh(mesh);

            smr.sharedMesh = mesh;
            smr.bones      = boneList.ToArray();
            smr.rootBone   = armatureRoot;

            smr.sharedMaterials = materials;
        }

        // ================================================================
        // 静的メッシュ（MeshFilter + MeshRenderer）アタッチ
        // ================================================================

        private void AttachStaticMesh(
            GameObject go, MeshContext mc, Mesh unityMesh, ModelContext model,
            bool hasMeshParent, bool mirror = false, MeshContext trsSource = null,
            int index = -1, MeshObject shapeSource = null, bool mirrorDelta = false,
            Vector3 vertexOffset = default, int sameSenseAxis = 0)
        {
            var src = trsSource ?? mc;

            var materials = BuildMaterials(mc, model);

            // 形状の出どころ（頂点は vertexOffset を足した位置。BuildMirrorSideMesh と同じ量）。
            var source = new HierarchyMeshSource
            {
                Shape        = shapeSource ?? mc.MeshObject,
                VertexOffset = vertexOffset,
                IsSkinned    = false,
                Materials    = (Material[])materials.Clone(),
                MirrorDelta  = mirrorDelta,
                MirrorAxis   = MirrorBranchOps.ResolveMirrorAxis(mc),
                SameSenseAxis = sameSenseAxis,
            };
            _result.MeshSourceOf[go.transform] = source;

            MeshRenderer mr = null;
            if (_opt.CreateUnityMeshes)
            {
                var mf = PLEditorBridge.I.AddComponent<MeshFilter>(go);
                mr = PLEditorBridge.I.AddComponent<MeshRenderer>(go);

                // マテリアル配列は空サブメッシュ除去と対で扱う（index が連動するため）。
                if (_opt.DropEmptySubMeshes) CompactSubMeshes(unityMesh, ref materials);

                // ブレンドシェイプはアセット化より前に載せる（アセットへ焼き込むため）。
                AppendMorphTargets(model, index, source, unityMesh, go.transform);

                // 同じ向きの枝のミラー側は、向きに掛けた F の分だけ形状を逆に回す（差分も含む）。
                ApplySameSenseToMesh(unityMesh, sameSenseAxis);

                // 共有アセット化するかどうかは呼び出し側の決定（規約 1）。
                mf.sharedMesh = PersistMesh(unityMesh);
            }
            else
            {
                AppendMorphPlan(model, index, source, go.transform);
            }

            if (hasMeshParent)
            {
                // 親側で累積されるのでローカル値を設定する。
                //   ミラー側はピボットを鏡像化する（頂点は BuildMirrorSideMesh で補正済み）。
                //   MirrorSide は自分の値が既に反転済みなので実体側相方の値を使う。
                var bt  = src.BoneTransform;
                var pos = bt.Position;
                var rot = bt.Rotation;

                if (mirror) MirrorBranchOps.MirrorLocalTRS(src, ref pos, ref rot);

                go.transform.localPosition    = pos;
                go.transform.localEulerAngles = rot;
                go.transform.localScale       = bt.Scale;
            }
            else
            {
                // 親を持たない場合はワールド指定。
                var wm   = mc.WorldMatrix;
                var wPos = new Vector3(wm.m03, wm.m13, wm.m23);
                var wRot = wm.rotation.eulerAngles;

                // 実体側ノードを複製して作ったミラー枝は、WorldMatrix が実体側のままなので
                // ここで鏡像化しないと反対側へ行かない（分岐ルートがモデルのルート直下の
                // ときに露見する）。ミラー側コンテキストは WorldMatrix が既に鏡像
                // （ModelContext.ApplyMirrorConjugate の S·H·S）なので触らない。
                // ApplyJointTransform の親なし分岐と同じ規則。
                if (mirror && !MirrorBranchOps.IsMirrorSideContext(mc))
                    MirrorBranchOps.MirrorLocalTRS(src, ref wPos, ref wRot);

                go.transform.position   = wPos;
                go.transform.rotation   = Quaternion.Euler(wRot);
                go.transform.localScale = wm.lossyScale;
            }

            if (mr != null) mr.sharedMaterials = materials;
        }

        /// <summary>
        /// 頂点ウェイトのボーン索引を、SkinnedMeshRenderer.bones の位置へ詰め替える。
        ///
        /// ボーンがリスト先頭に連続して並ぶ通常のモデルでは対応表が恒等になるので、
        /// 何も変わらない。ボーンをあとから足したモデルでのみ効く。
        /// 対応表に無い索引（出力されなかったボーンを指すウェイト）は
        /// 重み 0 に落とし、件数を警告に残す。放置すると範囲外の joint index が
        /// そのまま glTF に載り、ビューアがメッシュごと描画しなくなる。
        /// </summary>
        private void RemapBoneWeights(Mesh mesh, Dictionary<int, int> boneSlotOf, string meshName)
        {
            if (mesh == null || boneSlotOf == null) return;

            var weights = mesh.boneWeights;
            if (weights == null || weights.Length == 0) return;

            bool changed = false;
            int dropped = 0;

            for (int i = 0; i < weights.Length; i++)
                weights[i] = RemapBoneWeight(weights[i], boneSlotOf, ref changed, ref dropped);

            if (changed) mesh.boneWeights = weights;

            if (dropped > 0)
                _result.Warn(DroppedWeightsMessage(meshName, dropped));
        }

        /// <summary>
        /// 1 頂点ぶんのウェイトのボーン索引（MeshContext 索引）を bones 配列の位置へ写す。
        /// 規則は RemapBoneWeights のとおり（MeshObject から直接書く書き出しも同じ規則で写す）。
        /// </summary>
        public static BoneWeight RemapBoneWeight(
            BoneWeight w, Dictionary<int, int> boneSlotOf, ref bool changed, ref int dropped)
        {
            // BoneWeight の weight0-3 / boneIndex0-3 はプロパティなので
            // ref で渡せない（CS0206）。ローカルへ出してから書き戻す。
            float w0 = w.weight0, w1 = w.weight1, w2 = w.weight2, w3 = w.weight3;

            int i0 = MapSlot(w.boneIndex0, ref w0, boneSlotOf, ref changed, ref dropped);
            int i1 = MapSlot(w.boneIndex1, ref w1, boneSlotOf, ref changed, ref dropped);
            int i2 = MapSlot(w.boneIndex2, ref w2, boneSlotOf, ref changed, ref dropped);
            int i3 = MapSlot(w.boneIndex3, ref w3, boneSlotOf, ref changed, ref dropped);

            w.boneIndex0 = i0; w.weight0 = w0;
            w.boneIndex1 = i1; w.weight1 = w1;
            w.boneIndex2 = i2; w.weight2 = w2;
            w.boneIndex3 = i3; w.weight3 = w3;
            return w;
        }

        /// <summary>出力されなかったボーンを指すウェイトを 0 にしたときの警告文。</summary>
        public static string DroppedWeightsMessage(string meshName, int dropped)
            => $"メッシュ \"{meshName}\" のウェイト {dropped} 件が、出力されなかったボーンを"
             + "指していたため重み 0 にしました。";

        /// <summary>索引を bones 配列の位置へ写す。重み 0 のスロットは触らない。</summary>
        private static int MapSlot(
            int index, ref float weight, Dictionary<int, int> boneSlotOf,
            ref bool changed, ref int dropped)
        {
            if (weight <= 0f) return 0;

            if (boneSlotOf.TryGetValue(index, out int slot))
            {
                if (slot != index) changed = true;
                return slot;
            }

            weight = 0f;
            changed = true;
            dropped++;
            return 0;
        }

        /// <summary>
        /// モーフ MeshContext をブレンドシェイプとして載せ、結果を result に記録する。
        /// ExportMorphTargets が false のときは何もしない。
        /// </summary>
        private void AppendMorphTargets(
            ModelContext model, int index, HierarchyMeshSource source, Mesh mesh,
            Transform rendererTransform)
        {
            if (!_opt.ExportMorphTargets) return;
            if (index < 0 || model == null || source?.Shape == null || mesh == null) return;

            var slots = HierarchyMorphBuilder.Apply(
                model, index, source.Shape, mesh, rendererTransform,
                source.MirrorDelta, source.MirrorAxis, _result.Warnings);

            if (slots != null && slots.Count > 0)
            {
                _result.MorphShapes.AddRange(slots);
                source.Morphs.AddRange(slots);
            }
        }

        /// <summary>
        /// Unity メッシュを作らないとき（CreateUnityMeshes = false）に、
        /// 載せるモーフの名前と並びだけを決めて記録する（HierarchyMorphBuilder.Plan）。
        /// </summary>
        private void AppendMorphPlan(
            ModelContext model, int index, HierarchyMeshSource source, Transform rendererTransform)
        {
            if (!_opt.ExportMorphTargets) return;
            if (index < 0 || model == null || source?.Shape == null) return;

            var slots = HierarchyMorphBuilder.Plan(model, index, source.Shape, rendererTransform);

            if (slots != null && slots.Count > 0)
            {
                _result.MorphShapes.AddRange(slots);
                source.Morphs.AddRange(slots);
            }
        }

        /// <summary>共有アセット化フックを通す。未指定なら素通し。</summary>
        private Mesh PersistMesh(Mesh mesh)
        {
            if (_persistMesh == null || mesh == null) return mesh;
            return _persistMesh(mesh) ?? mesh;
        }

        // ================================================================
        // モーフの取りこぼし検査
        // ================================================================

        /// <summary>
        /// ブレンドシェイプにできなかったモーフを理由つきで警告に残す。
        /// 親が特定できない場合と、親が出力対象外だった場合を分ける。
        /// </summary>
        private static void ReportUnattachedMorphs(ModelContext model, HierarchyBuildResult result)
        {
            if (model == null || result == null) return;

            var attached = new HashSet<int>();
            foreach (var slot in result.MorphShapes) attached.Add(slot.MorphContextIndex);

            var noBase          = new List<string>();
            var baseNotExported = new List<string>();
            int total = 0;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !mc.IsMorph) continue;

                total++;
                if (attached.Contains(i)) continue;

                string name = string.IsNullOrEmpty(mc.Name) ? "[" + i + "]" : mc.Name;
                int baseIndex = MorphPreviewState.FindBaseMeshIndex(model, mc);

                if (baseIndex < 0)
                {
                    noBase.Add(name);
                }
                else
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    baseNotExported.Add(name + " → " + (baseCtx?.Name ?? ("[" + baseIndex + "]")));
                }
            }

            if (total == 0) return;

            if (noBase.Count > 0)
                result.Warn(
                    "親メッシュを特定できないモーフ " + noBase.Count + " 件をブレンドシェイプにできませんでした: "
                    + Join(noBase));

            if (baseNotExported.Count > 0)
                result.Warn(
                    "親メッシュが出力対象外のモーフ " + baseNotExported.Count + " 件をブレンドシェイプにできませんでした: "
                    + Join(baseNotExported));
        }

        /// <summary>警告文が延々と伸びないよう先頭 20 件までにする。</summary>
        private static string Join(List<string> names)
        {
            const int Max = 20;
            if (names.Count <= Max) return string.Join(", ", names);
            return string.Join(", ", names.GetRange(0, Max)) + $", ...ほか {names.Count - Max} 件";
        }

        // ================================================================
        // 空サブメッシュの除去
        // ================================================================

        /// <summary>
        /// 面を1つも持たないサブメッシュを取り除き、マテリアル配列も同じ順で詰める。
        ///
        /// MeshObject.SubMeshCount は「使用マテリアル index の最大値+1」なので
        /// （MeshObject.cs:1237-1245）、モデル全体のマテリアル数が多いほど
        /// 空サブメッシュが増える。Unity 上は無害だが、glTF 化すると
        /// 頂点ゼロのプリミティブになり UniVRM が落ちる：
        ///   ExportingGltfData.cs:64  … 空配列に対して accessor index -1 を返す
        ///   MeshExportUtil.cs        … その -1 で Gltf.accessors[-1] を引いて例外
        /// ModelExporter.CreateMesh には空サブメッシュを飛ばす処理が無いため、
        /// 渡す前にこちら側で潰しておく。
        ///
        /// 呼ばれるのは DropEmptySubMeshes が true のときだけ。
        /// ヒエラルキー出力（Editor 拡張）は従来どおり空サブメッシュを保つ。
        /// </summary>
        private static void CompactSubMeshes(Mesh mesh, ref Material[] materials)
        {
            if (mesh == null) return;

            int count = mesh.subMeshCount;
            if (count <= 1) return;

            var keep = new List<int>(count);
            for (int i = 0; i < count; i++)
                if (mesh.GetIndexCount(i) > 0) keep.Add(i);

            // 空が無いなら触らない。全部空なら潰さない（呼び出し先が弾く）。
            if (keep.Count == count || keep.Count == 0) return;

            var triangles = new List<int[]>(keep.Count);
            foreach (int i in keep) triangles.Add(mesh.GetTriangles(i));

            mesh.subMeshCount = keep.Count;
            for (int j = 0; j < keep.Count; j++)
                mesh.SetTriangles(triangles[j], j);

            if (materials != null)
            {
                var compacted = new Material[keep.Count];
                for (int j = 0; j < keep.Count; j++)
                    compacted[j] = keep[j] < materials.Length ? materials[keep[j]] : null;
                materials = compacted;
            }
        }

        // ================================================================
        // マテリアル配列生成
        // ================================================================

        private static Material[] BuildMaterials(MeshContext mc, ModelContext model)
        {
            int subMeshCount = Mathf.Max(1, mc.MeshObject?.SubMeshCount ?? 1);
            var mats = new Material[subMeshCount];

            var matRefs = model?.MaterialReferences;
            var defaultMat = PLEditorBridge.I.GetBuiltinDefaultMaterial();

            for (int m = 0; m < subMeshCount; m++)
            {
                Material mat = null;
                if (matRefs != null && m < matRefs.Count)
                    mat = matRefs[m]?.Material;
                mats[m] = mat != null ? mat : defaultMat;
            }
            return mats;
        }

        // ================================================================
        // 名前の一意化
        // ================================================================

        /// <summary>root 配下に存在する GameObject 名を収集する。</summary>
        public static HashSet<string> CollectHierarchyNames(GameObject root)
        {
            var set = new HashSet<string>();
            if (root == null) return set;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                set.Add(t.name);

            return set;
        }

        /// <summary>used に含まれない名前を返し、使用済みとして登録する。</summary>
        public static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Object" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }
    }
}
