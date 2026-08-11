// MeshFilterToSkinnedConverter.cs
// MeshFilter → Skinned 変換ロジック（Runtime / Editor 共有）。
// EditorGUI 依存なし。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Localization;

namespace Poly_Ling.Ops
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    public static class MeshFilterToSkinnedTexts
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["WindowTitle"]       = new() { ["en"] = "MeshFilter → Skinned",                               ["ja"] = "MeshFilter → Skinned変換" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available",                                 ["ja"] = "モデルがありません" },
            ["NoMeshFound"]       = new() { ["en"] = "No mesh objects found",                               ["ja"] = "メッシュオブジェクトがありません" },
            ["AlreadyHasBones"]   = new() { ["en"] = "Model already has bones",                             ["ja"] = "既にボーンが存在します" },
            ["RootBone"]          = new() { ["en"] = "Root Bone (top mesh)",                               ["ja"] = "ルートボーン (トップメッシュ)" },
            ["Convert"]           = new() { ["en"] = "Convert",                                             ["ja"] = "変換実行" },
            ["ConvertWarning"]    = new() { ["en"] = "This operation cannot be undone.\nProceed?",          ["ja"] = "この操作は元に戻せません。\n変換を実行しますか？" },
            ["ConvertSuccess"]    = new() { ["en"] = "Conversion completed: {0} bones created",            ["ja"] = "変換完了: {0}個のボーンを作成" },
            ["Preview"]           = new() { ["en"] = "Preview",                                             ["ja"] = "プレビュー" },
            ["BoneHierarchy"]     = new() { ["en"] = "Bone Hierarchy",                                      ["ja"] = "ボーン階層" },
            ["BoneAxisSettings"]  = new() { ["en"] = "Bone Axis Settings",                                  ["ja"] = "ボーン軸設定" },
            ["SwapAxisRotated"]   = new() { ["en"] = "Rotated bones: Swap to PMX axis (Y→X)",              ["ja"] = "回転ありボーン: PMX軸に入替 (Y→X)" },
            ["SetAxisIdentity"]   = new() { ["en"] = "Identity bones: Set X=Up, Y=Side (PMX style)",       ["ja"] = "回転なしボーン: X軸上向き・Y軸横向きに設定" },
            ["MirrorSideRow"]     = new() { ["en"] = "mirror (follows source)",                            ["ja"] = "ミラー側 (実体側に従う)" },
        };

        public static string T(string key)                       => L.GetFrom(Texts, key);
        public static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }

    // ================================================================
    // 変換ロジック
    // ================================================================

    public static class MeshFilterToSkinnedConverter
    {
        // ================================================================
        // 公開型
        // ================================================================

        public struct MeshEntry
        {
            public int         Index;
            public MeshContext Context;

            /// <summary>
            /// ミラー側（MirrorSide / BakedMirror）で、かつ実体側相方が解決できたか。
            /// 相方が取れないミラー側は通常メッシュとして扱う（安全側）。
            /// </summary>
            public bool        IsMirrorSide;

            /// <summary>実体側相方の MeshContextList 索引（-1 = なし）。</summary>
            public int         RealPeerIndex;
        }

        /// <summary>生成するボーン1本の計画。</summary>
        private struct BonePlan
        {
            /// <summary>名前・階層親の由来となる MeshContextList 索引（変換前）。</summary>
            public int  SourceIndex;

            /// <summary>ローカル TRS の由来となる索引（ミラー側メッシュなら実体側相方）。</summary>
            public int  TrsSourceIndex;

            /// <summary>ミラー側ボーンか。</summary>
            public bool IsMirror;
        }

        // ================================================================
        // データ収集
        // ================================================================

        /// <summary>
        /// ボーン化・スキン化の対象メッシュを収集する。
        /// 通常メッシュに加えてミラー側（MirrorSide / BakedMirror）も対象に含める。
        /// </summary>
        public static List<MeshEntry> CollectMeshEntries(ModelContext model)
        {
            var result = new List<MeshEntry>();
            if (model == null) return result;

            var peers = MirrorPeerIndex.Build(model);

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;

                bool isMirrorType = MirrorBranchOps.IsMirrorSideContext(ctx);
                if (ctx.Type != MeshType.Mesh && !isMirrorType) continue;

                int realPeer = -1;
                if (isMirrorType && peers.TryGetReal(i, out int r) && r != i) realPeer = r;

                result.Add(new MeshEntry
                {
                    Index         = i,
                    Context       = ctx,
                    IsMirrorSide  = isMirrorType && realPeer >= 0,
                    RealPeerIndex = realPeer
                });
            }
            return result;
        }

        public static int CalculateDepth(int index, ModelContext model)
        {
            int depth = 0, current = index, safety = 100;
            while (safety-- > 0)
            {
                var ctx    = model.MeshContextList[current];
                int parent = ctx.HierarchyParentIndex;
                if (parent < 0 || parent >= model.MeshContextList.Count) break;
                depth++; current = parent;
            }
            return depth;
        }

        // ================================================================
        // 変換実行（Editor / Player 共通）
        // ================================================================

        /// <summary>
        /// MeshFilter → Skinned 変換を実行する。
        /// 成功時に作成したボーン数を返す。失敗時は 0 を返す。
        /// </summary>
        public static int Execute(
            ModelContext model,
            List<MeshEntry> meshEntries,
            bool swapAxisForRotated,
            bool setAxisForIdentity)
        {
            // ================================================================
            // ミラー情報の収集
            //   分岐解析は HierarchyParentIndex を正本にする
            //   （本変換の親子解決は一貫して HierarchyParentIndex を使う）。
            // ================================================================
            var peers      = MirrorPeerIndex.Build(model);
            var branchSide = MirrorBranchOps.AnalyzeMirrorBranches(model, null);

            // 元リストの親子関係を保存（Phase 2 前に参照するため）
            var originalList = new List<MeshContext>(model.MeshContextList);

            // 旧index → エントリ（ミラー側の IgnorePose 判定に使う）
            var entryOfIndex = new Dictionary<int, MeshEntry>();
            foreach (var e in meshEntries) entryOfIndex[e.Index] = e;

            // ミラー側メッシュは実体側相方の IgnorePose 設定に従う。
            // 片側だけ除外されるとボーンの対応が崩れるため。
            bool IsIgnored(MeshEntry e)
            {
                if (e.Context.IgnorePoseInArmature) return true;
                if (e.IsMirrorSide && entryOfIndex.TryGetValue(e.RealPeerIndex, out var realEntry))
                    return realEntry.Context.IgnorePoseInArmature;
                return false;
            }

            int ignoredCount = 0;
            foreach (var e in meshEntries) if (IsIgnored(e)) ignoredCount++;

            // ワールド行列を全メッシュ分保存。
            // BoneTransform.Position は親子設定時は親相対（ローカル）になる
            // （MeshContext.LocalMatrix / ComputeWorldMatrices と同じ意味論）。
            // フラットな TRS では親のワールド変換が抜け落ち、親子ありの子頂点が
            // 原点付近に集中する。親を累積した正しいワールド行列を使う。
            // ルート（親なし）は WorldMatrix = LocalMatrix = TRS(Position,Euler(Rotation),Scale)
            // となり従来のフラット式と一致するため挙動不変。
            model.ComputeWorldMatrices();
            var savedWorldMatrices = new Dictionary<int, Matrix4x4>();
            foreach (var e in meshEntries)
                savedWorldMatrices[e.Index] = e.Context.WorldMatrix;

            // ================================================================
            // ボーン生成計画
            //   realBoneNumOf   : 旧index → 実体側ボーン番号
            //   mirrorBoneNumOf : 旧index → ミラー側ボーン番号
            //     鍵の持ち方は Editor の mirrorTransformByIndex と同じ。
            //     ミラー側メッシュは自分の index、両側に複製された関節は実体側の index。
            //   生成順はリスト順。ミラー側メッシュは実体側の直後に並ぶため
            //   （MQO インポータが実体側の直後に挿入する）、ボーンも隣接する。
            // ================================================================
            var realBoneNumOf   = new Dictionary<int, int>();
            var mirrorBoneNumOf = new Dictionary<int, int>();
            var bonePlans       = new List<BonePlan>();

            foreach (var e in meshEntries)
            {
                if (IsIgnored(e)) continue;

                if (e.IsMirrorSide)
                {
                    // ミラー側メッシュ: ミラー側ボーン1本。TRS の正本は実体側相方。
                    mirrorBoneNumOf[e.Index] = bonePlans.Count;
                    bonePlans.Add(new BonePlan
                    {
                        SourceIndex    = e.Index,
                        TrsSourceIndex = e.RealPeerIndex,
                        IsMirror       = true
                    });
                    continue;
                }

                realBoneNumOf[e.Index] = bonePlans.Count;
                bonePlans.Add(new BonePlan
                {
                    SourceIndex    = e.Index,
                    TrsSourceIndex = e.Index,
                    IsMirror       = false
                });

                // ミラー分岐配下の関節（頂点なし）は両側に複製する。
                bool isJoint = (e.Context.MeshObject?.Vertices.Count ?? 0) == 0;
                if (isJoint &&
                    branchSide.TryGetValue(e.Index, out int side) &&
                    side == MirrorBranchOps.SideReal)
                {
                    mirrorBoneNumOf[e.Index] = bonePlans.Count;
                    bonePlans.Add(new BonePlan
                    {
                        SourceIndex    = e.Index,
                        TrsSourceIndex = e.Index,
                        IsMirror       = true
                    });
                }
            }

            int boneCount = bonePlans.Count;

            // HierarchyParentIndex を上に辿り、ボーンを持つ最初の祖先のボーン番号を返す。
            // mirror=true では MirrorBranchOps の親解決規則（相方 → 同枝 → 実体側）に従う。
            // 見つからない場合は -1。
            int ResolveParentBoneNum(int startMeshIndex, bool mirror)
            {
                int cur = startMeshIndex;
                int safety = 200;
                while (cur >= 0 && cur < originalList.Count && safety-- > 0)
                {
                    if (MirrorBranchOps.TryResolveMirrorParent(
                            peers, cur, mirror,
                            idx => mirrorBoneNumOf.ContainsKey(idx),
                            out int resolved, out bool resolvedIsMirrorSide))
                    {
                        if (resolvedIsMirrorSide)
                        {
                            if (mirrorBoneNumOf.TryGetValue(resolved, out int mb)) return mb;
                        }
                        else if (realBoneNumOf.TryGetValue(resolved, out int rb)) return rb;
                    }
                    cur = originalList[cur].HierarchyParentIndex;
                }
                return -1;
            }

            // 全 meshEntry の effective bone num を Phase 2 前に確定
            var effectiveBoneNumForEntry = new Dictionary<int, int>();
            int lastBoneNum = -1; // リスト順で直前のボーン番号
            foreach (var e in meshEntries)
            {
                if (!IsIgnored(e))
                {
                    int bn = e.IsMirrorSide ? mirrorBoneNumOf[e.Index] : realBoneNumOf[e.Index];
                    effectiveBoneNumForEntry[e.Index] = bn;
                    lastBoneNum = bn;
                }
                else
                {
                    // HierarchyParent を上に辿って最初の非IgnorePose祖先を探す
                    int found = ResolveParentBoneNum(
                        originalList[e.Index].HierarchyParentIndex, e.IsMirrorSide);
                    // 親子関係がない場合はリスト順で直前のボーンを使う
                    if (found < 0) found = lastBoneNum;
                    effectiveBoneNumForEntry[e.Index] = found;
                }
            }

            // 実体側メッシュ → 相方のミラー側メッシュが割り当てられたボーン。
            // PMX と同じ「ミラー元とミラーをワンセット」で持つため、
            // 実体側頂点の MirrorBoneWeight に入れる。
            var mirrorBoneNumForEntry = new Dictionary<int, int>();
            foreach (var e in meshEntries)
            {
                if (e.IsMirrorSide) continue;
                if (!peers.TryGetMirror(e.Index, out int mirrorIndex)) continue;
                if (!effectiveBoneNumForEntry.TryGetValue(mirrorIndex, out int mb)) continue;
                if (mb < 0) continue;
                mirrorBoneNumForEntry[e.Index] = mb;
            }

            // Phase 1: ボーン MeshContext 作成（bonePlans の順）
            var boneContexts = new List<MeshContext>(boneCount);
            for (int i = 0; i < bonePlans.Count; i++)
            {
                var plan   = bonePlans[i];
                var srcCtx = originalList[plan.SourceIndex];
                var trsCtx = originalList[plan.TrsSourceIndex];

                // 有効な親ボーン番号を求める（直接親が IgnorePose ならスキップして辿る）
                int parentBoneNum = ResolveParentBoneNum(srcCtx.HierarchyParentIndex, plan.IsMirror);

                // local TRS を決定（ミラー側は実体側相方の値を鏡像化して使う）
                // 直接の HierarchyParent がボーン対象かどうかで分岐
                bool directParentIsBone = trsCtx.HierarchyParentIndex >= 0 &&
                                          realBoneNumOf.ContainsKey(trsCtx.HierarchyParentIndex);
                Vector3 localPos;
                Vector3 localRot;
                Vector3 localScl;

                if (directParentIsBone || trsCtx.HierarchyParentIndex < 0)
                {
                    // 直接親がボーン（または親なし）: BoneTransform をそのまま使う
                    localPos = trsCtx.BoneTransform.Position;
                    localRot = trsCtx.BoneTransform.Rotation;
                    localScl = trsCtx.BoneTransform.Scale;
                }
                else
                {
                    // 直接親が IgnorePose: ワールド行列から effective 親相対で再計算
                    Matrix4x4 childWorld = savedWorldMatrices[plan.TrsSourceIndex];
                    Matrix4x4 parentWorld = Matrix4x4.identity;
                    if (parentBoneNum >= 0 &&
                        savedWorldMatrices.TryGetValue(bonePlans[parentBoneNum].TrsSourceIndex, out var pw))
                        parentWorld = pw;
                    Matrix4x4 localMat = parentWorld.inverse * childWorld;
                    localPos = new Vector3(localMat.m03, localMat.m13, localMat.m23);
                    Vector3 scaleX = new Vector3(localMat.m00, localMat.m10, localMat.m20);
                    Vector3 scaleY = new Vector3(localMat.m01, localMat.m11, localMat.m21);
                    Vector3 scaleZ = new Vector3(localMat.m02, localMat.m12, localMat.m22);
                    localScl = new Vector3(scaleX.magnitude, scaleY.magnitude, scaleZ.magnitude);
                    Quaternion localRotQ = Quaternion.LookRotation(
                        new Vector3(localMat.m02, localMat.m12, localMat.m22).normalized,
                        new Vector3(localMat.m01, localMat.m11, localMat.m21).normalized);
                    localRot = localRotQ.eulerAngles;
                }

                // ミラー側はローカル姿勢を鏡像化する。軸・距離の正本は TRS 元（実体側相方）。
                if (plan.IsMirror)
                    MirrorBranchOps.MirrorLocalTRS(trsCtx, ref localPos, ref localRot);

                // ミラー側の関節は実体側と同名になるため接尾辞を付ける。
                // ミラー側メッシュは元から実体側と別名なので付けない。
                string boneName = srcCtx.Name;
                if (plan.IsMirror && !MirrorBranchOps.IsMirrorSideContext(srcCtx))
                    boneName += MirrorBranchOps.MirrorBranchSuffix;

                var boneMeshObject = new MeshObject(boneName) { Type = MeshType.Bone };
                boneMeshObject.BoneTransform = new BoneTransform
                {
                    Position          = localPos,
                    Rotation          = localRot,
                    Scale             = localScl,
                    UseLocalTransform = true
                };

                var boneCtx = new MeshContext
                {
                    MeshObject        = boneMeshObject,
                    IsVisible         = true,
                    OriginalPositions = new Vector3[0],
                    UnityMesh         = null
                };
                boneCtx.ParentIndex          = parentBoneNum;
                boneCtx.HierarchyParentIndex = parentBoneNum;
                boneContexts.Add(boneCtx);
            }

            // Phase 1.5: ボーン軸調整（boneContexts のみ、変更なし）
            if (swapAxisForRotated || setAxisForIdentity)
            {
                int n             = boneContexts.Count;
                var savedWorldPos = new Vector3[n];
                var worldMats     = new Matrix4x4[n];
                var computed      = new bool[n];

                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt        = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;
                        if (parentIdx >= 0 && !computed[parentIdx]) continue;
                        Matrix4x4 parentWorld = parentIdx < 0 ? Matrix4x4.identity : worldMats[parentIdx];
                        worldMats[i]     = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        savedWorldPos[i] = new Vector3(worldMats[i].m03, worldMats[i].m13, worldMats[i].m23);
                        computed[i]      = true;
                        anyAdded         = true;
                    }
                    if (!anyAdded) break;
                }

                Quaternion swapYtoX = Quaternion.Euler(0f, 0f, 90f);
                for (int i = 0; i < n; i++)
                {
                    var bt = boneContexts[i].BoneTransform;
                    if (bt == null) continue;
                    bool isIdentity = bt.Rotation == Vector3.zero;
                    if (!isIdentity && swapAxisForRotated)
                        bt.Rotation = (Quaternion.Euler(bt.Rotation) * swapYtoX).eulerAngles;
                    else if (isIdentity && setAxisForIdentity)
                        bt.Rotation = new Vector3(0f, 0f, 90f);
                }

                computed        = new bool[n];
                var newWorldMats = new Matrix4x4[n];
                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt        = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;
                        if (parentIdx >= 0 && !computed[parentIdx]) continue;
                        Matrix4x4 parentWorld = parentIdx < 0 ? Matrix4x4.identity : newWorldMats[parentIdx];
                        bt.Position     = parentWorld.inverse.MultiplyPoint3x4(savedWorldPos[i]);
                        newWorldMats[i] = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        computed[i]     = true;
                        anyAdded        = true;
                    }
                    if (!anyAdded) break;
                }
            }

            // Phase 2: リスト再構築
            var oldList = new List<MeshContext>(model.MeshContextList);
            model.MeshContextList.Clear();
            for (int i = 0; i < boneCount; i++)
            {
                model.MeshContextList.Add(boneContexts[i]);
                boneContexts[i].ParentModelContext = model;
            }
            model.MeshContextList.AddRange(oldList);
            model.InvalidateTypedIndices();

            for (int i = boneCount; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;
                if (ctx.HierarchyParentIndex >= 0) ctx.HierarchyParentIndex += boneCount;
                if (ctx.ParentIndex          >= 0) ctx.ParentIndex          += boneCount;
                // MeshContextList 索引を保持する属性も同じだけずらす。
                if (ctx.MorphParentIndex        >= 0) ctx.MorphParentIndex        += boneCount;
                if (ctx.BakedMirrorSourceIndex  >= 0) ctx.BakedMirrorSourceIndex  += boneCount;
                if (ctx.MeshObject != null)
                {
                    // MirrorBoneWeight も同じボーン索引空間を指すため同じだけずらす。
                    foreach (var vertex in ctx.MeshObject.Vertices)
                    {
                        if (vertex.HasBoneWeight)
                            vertex.BoneWeight = ShiftBoneWeight(vertex.BoneWeight.Value, boneCount);
                        if (vertex.HasMirrorBoneWeight)
                            vertex.MirrorBoneWeight = ShiftBoneWeight(vertex.MirrorBoneWeight.Value, boneCount);
                    }
                }
            }

            // Phase 2b: 選択インデックスの再マップ
            // SelectedDrawableMeshIndices / SelectedBoneIndices / SelectedMorphIndices は
            // MeshContextList 索引を保持する。先頭に boneCount 個挿入した分だけずらさないと
            // 選択が先頭のボーンを指し、FirstDrawableMeshContext がボーンを返す。
            // その結果 SelectionState の束ね替え・選択フラグ更新・頂点移動対象が全て狂う。
            ShiftSelectionIndices(model.SelectedDrawableMeshIndices, boneCount);
            ShiftSelectionIndices(model.SelectedBoneIndices,         boneCount);
            ShiftSelectionIndices(model.SelectedMorphIndices,        boneCount);

            // Phase 2c: メッシュ名の一意化
            // ボーンはメッシュと同名で作られるため、そのままだとヒエラルキー出力時に
            // 同名の GameObject が並び、Humanoid 割当先の Transform 名が一意でなくなって
            // AvatarBuilder が失敗する。衝突したメッシュ側にのみ接尾辞を付ける。
            {
                var usedNames = new HashSet<string>();
                for (int i = 0; i < boneCount; i++)
                    usedNames.Add(model.MeshContextList[i].Name);

                foreach (var entry in meshEntries)
                {
                    var meshCtx = model.MeshContextList[entry.Index + boneCount];
                    if (meshCtx == null) continue;

                    string raw = meshCtx.Name;
                    meshCtx.Name = usedNames.Contains(raw)
                        ? MakeUniqueName(raw + MeshNameSuffix, usedNames)
                        : MakeUniqueName(raw, usedNames);
                }
            }

            // Phase 3: ワールド行列 + BindPose
            model.ComputeWorldAndBindPoses();

            // Phase 4: 頂点ワールド変換 + BoneWeight（全 meshEntries）
            foreach (var entry in meshEntries)
            {
                int oldIndex     = entry.Index;
                int newMeshIndex = oldIndex + boneCount;
                int boneMasterIdx = effectiveBoneNumForEntry[oldIndex]; // -1 の場合は親なし

                var meshCtx = model.MeshContextList[newMeshIndex];
                var meshObj = meshCtx.MeshObject;
                if (meshObj == null) continue;

                // IgnorePose メッシュは有効親ボーン配下に付け替え（-1 ならルートのまま）
                if (boneMasterIdx >= 0)
                {
                    meshCtx.HierarchyParentIndex = boneMasterIdx;
                    meshCtx.ParentIndex          = boneMasterIdx;
                }

                Matrix4x4 originalWorld = savedWorldMatrices[oldIndex];
                if (meshObj.VertexCount > 0)
                    foreach (var vertex in meshObj.Vertices)
                        vertex.Position = originalWorld.MultiplyPoint3x4(vertex.Position);

                meshCtx.BoneTransform.Position          = Vector3.zero;
                meshCtx.BoneTransform.Rotation          = Vector3.zero;
                meshCtx.BoneTransform.Scale             = Vector3.one;
                meshCtx.BoneTransform.UseLocalTransform = false;

                // 生成ミラー（MeshFilter 系）ではなくなったことを明示する。
                //
                // ここまでで、ミラー側メッシュは
                //   ・頂点を originalWorld でワールドへ焼き
                //   ・BoneTransform を単位に潰し
                //   ・自分専用のボーンで動く
                // 状態になり、PMX 系ミラーとまったく同じ持ち方になる。
                //
                // MirrorGeometryDerived を立てたままにすると、
                //   ComputeWorldMatrices        … ワールド行列を S·H·S で解く
                //   SyncDerivedMirrorTransforms … 姿勢を実体側から引き写す
                //   RebakeDerivedMirrorVertices … 頂点を実体側のローカル鏡像で作り直す
                // が生成ミラーとして働き続ける。特に最後のひとつは、
                // オブジェクト姿勢の確定時にワールドへ焼いた頂点を上書きして形を壊す。
                //
                // Type / MirrorPairs / BakedMirrorSourceIndex はそのまま残すので、
                // ミラーとしての関係は保たれる。
                meshCtx.MirrorGeometryDerived = false;

                int assignBone = boneMasterIdx >= 0 ? boneMasterIdx : 0;

                // 実体側メッシュには相方のミラー側ボーンを MirrorBoneWeight として持たせる
                // （PMX と同じ「ミラー元とミラーをワンセット」の保持形）。
                // ボーン対応の正本はミラー側メッシュ頂点の BoneWeight 側であり、
                // 保存/復元時は MirrorPair.Build() の投票で再構築される。
                bool hasMirrorBone = mirrorBoneNumForEntry.TryGetValue(oldIndex, out int mirrorBone);

                foreach (var vertex in meshObj.Vertices)
                {
                    vertex.BoneWeight = new BoneWeight { boneIndex0 = assignBone, weight0 = 1f };
                    if (hasMirrorBone)
                        vertex.MirrorBoneWeight = new BoneWeight { boneIndex0 = mirrorBone, weight0 = 1f };
                }

                meshCtx.UnityMesh      = meshObj.ToUnityMesh();
                meshCtx.UnityMesh.name = meshCtx.Name;
                meshCtx.OriginalPositions = (Vector3[])meshObj.Positions.Clone();
            }

            // Phase 5: 最終ワールド行列
            model.ComputeWorldMatrices();

            // Phase 6: HasBoneTransform フラグ
            foreach (var ctx in model.MeshContextList)
                if (ctx?.BoneTransform != null)
                    ctx.BoneTransform.HasBoneTransform = true;

            Debug.Log($"[MeshFilterToSkinnedConverter] Created {boneCount} bones " +
                      $"(mirror {mirrorBoneNumOf.Count}, ignored {ignoredCount})");
            return boneCount;
        }

        // ボーン名と衝突したメッシュに付ける接尾辞（エディタ拡張のエクスポータと同じ規則）
        private const string MeshNameSuffix = "_skinned";

        /// <summary>ボーンウェイトの参照先ボーン索引を offset 分ずらす。</summary>
        private static BoneWeight ShiftBoneWeight(BoneWeight bw, int offset)
        {
            return new BoneWeight
            {
                boneIndex0 = bw.weight0 > 0 ? bw.boneIndex0 + offset : 0,
                boneIndex1 = bw.weight1 > 0 ? bw.boneIndex1 + offset : 0,
                boneIndex2 = bw.weight2 > 0 ? bw.boneIndex2 + offset : 0,
                boneIndex3 = bw.weight3 > 0 ? bw.boneIndex3 + offset : 0,
                weight0 = bw.weight0, weight1 = bw.weight1,
                weight2 = bw.weight2, weight3 = bw.weight3
            };
        }

        /// <summary>used に含まれない名前を返し、使用済みとして登録する。</summary>
        private static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Mesh" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }

        /// <summary>
        /// MeshContextList 索引を保持する選択リストを offset 分ずらす。
        /// 負値（未選択）はそのまま残す。
        /// </summary>
        private static void ShiftSelectionIndices(List<int> indices, int offset)
        {
            if (indices == null || offset == 0) return;
            for (int i = 0; i < indices.Count; i++)
                if (indices[i] >= 0) indices[i] += offset;
        }
    }
}
