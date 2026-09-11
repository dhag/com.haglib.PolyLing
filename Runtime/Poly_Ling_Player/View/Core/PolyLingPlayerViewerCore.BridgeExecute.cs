// PolyLingPlayerViewerCore.BridgeExecute.cs
// Player ビューアのコア：辺群ブリッジ・穴つなぎの実行（生成先の解決と面の追加）。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Remote;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.EditorBridge;
using Poly_Ling.View;
using Poly_Ling.MeshListV2;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectArray;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        // ================================================================
        // 辺群ブリッジの実行
        // ================================================================

        /// <summary>
        /// 辺群ブリッジを実行する。
        ///
        /// 拾った 2 つの辺群は同じ描画オブジェクトの中にあるので、書き込み先は
        /// そのオブジェクト自身に固定する。両端の頂点はクローンせず既存のものを
        /// そのまま参照する（AppendBridgeInto の reuseA / reuseB を true にする）。
        /// これで拾った辺に直結した面が張られる。
        ///
        /// 頂点の追加・ウェイト補間・UV／法線スロット確保・Undo は穴つなぎと同じ
        /// AppendBridgeInto に任せる。同じ処理を二重に持たない。
        /// </summary>
        private void ExecuteEdgeBridge()
        {
            var panel = _edgeBridgeSubPanel;
            var h     = _edgeBridgeHandler;
            if (panel == null || h == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel?.SetStatus("モデルがありません"); return; }

            if (!h.TryBuildPlan(out var plan, out string planMsg))
            {
                panel?.SetStatus(planMsg);
                return;
            }

            var targetMc = model.GetMeshContext(plan.SrcMeshA);
            if (targetMc?.MeshObject == null)
            {
                panel?.SetStatus("拾った辺のオブジェクトが見つかりません");
                return;
            }

            // UNDO: 変更前スナップショット（穴つなぎの AddToExisting と同じ経路）
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            // 両端とも同じオブジェクトの既存頂点なので reuse。追加されるのは中間頂点だけ。
            int added = AppendBridgeInto(
                targetMc.MeshObject, targetMc.WorldToVertexMatrix, plan,
                reuseA: true, reuseB: true,
                srcA: targetMc.MeshObject, srcB: targetMc.MeshObject);

            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Edge Bridge in {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);

            // 面を張ったら拾いは用済み。頂点番号は変わらないが、境界辺の集合が
            // 変わる（張った辺はもう境界辺ではない）ので拾い直させる。
            h.InvalidateOnTopologyChanged();

            panel?.SetStatus($"面 {plan.Result.Faces.Count} / 追加頂点 {added} → {targetMc.Name}");
            UpdateTopologyToolsOverlay();
        }

        /// <summary>
        /// 穴つなぎを実行する。書き込み先を決めてから、必要な頂点（位置クローン・中間頂点）を
        /// 作って面を張る。書き込み先の既存頂点はそのまま参照するので穴の縁に直結する。
        /// </summary>
        private void ExecuteBridge(PlayerPrimitiveMeshSubPanel panel)
        {
            if (panel == null) return;

            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.SetBridgeStatus("モデルがありません"); return; }

            if (!panel.TryBuildBridgePlan(out var plan, out string planMsg))
            {
                panel.SetBridgeStatus(planMsg);
                return;
            }

            // 行き先は共通の「追加先」に従う。専用トグルは廃止した。
            switch (panel.CurrentAddMode)
            {
                case PrimitiveAddMode.AddToExisting:
                    ExecuteBridgeIntoExisting(model, panel, plan);
                    break;
                case PrimitiveAddMode.NewModel:
                    ExecuteBridgeNewModel(panel, plan);
                    break;
                default:
                    ExecuteBridgeNewObject(model, panel, plan);
                    break;
            }
        }

        /// <summary>
        /// 追加先＝既存の描画オブジェクト。既存頂点をそのまま使う。
        ///
        /// 対象の解決は図形生成の AddToExisting と同じ ResolveAddTargetMeshContext を通す。
        /// 以前はここだけ ModelContext.FirstMeshIndex を直に見ており、
        /// ModelContext.cs の「編集対象は ActiveMeshContext / ActiveMeshIndex を使う」
        /// という規約から外れていた。
        /// </summary>
        private void ExecuteBridgeIntoExisting(
            ModelContext model, PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            var targetMc  = ResolveAddTargetMeshContext(model, panel.CurrentAddTargetIndex);
            int targetIdx = targetMc != null ? model.IndexOf(targetMc) : -1;
            if (targetMc?.MeshObject == null || targetIdx < 0)
            {
                panel.SetBridgeStatus("追加先の描画オブジェクトがありません");
                return;
            }

            // UNDO: 変更前スナップショット（図形生成の AddToExisting と同じ経路）
            MeshObjectSnapshot before = null;
            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetMeshObject(targetMc.MeshObject, targetMc.UnityMesh);
                _editOps.UndoController.MeshUndoContext.ParentModelContext = model;
                before = _editOps.UndoController.CaptureMeshObjectSnapshot();
            }

            // 書き込み先自身の座標系へ落とす。スキンドなら頂点はワールド空間なので恒等。
            int added = AppendBridgeInto(
                targetMc.MeshObject, targetMc.WorldToVertexMatrix, plan,
                plan.SrcMeshA == targetIdx, plan.SrcMeshB == targetIdx,
                model.GetMeshContext(plan.SrcMeshA)?.MeshObject,
                model.GetMeshContext(plan.SrcMeshB)?.MeshObject);

            var newUnityMesh = targetMc.MeshObject.ToUnityMesh();
            newUnityMesh.name      = targetMc.Name;
            newUnityMesh.hideFlags = HideFlags.HideAndDontSave;
            // Object.Destroy は edit mode では破棄しない。ReplaceUnityMesh は
            // MeshContext.DestroyMesh 経由で isPlaying を見て使い分ける。
            targetMc.ReplaceUnityMesh(newUnityMesh);

            if (_editOps?.UndoController != null && before != null)
            {
                var after = _editOps.UndoController.CaptureMeshObjectSnapshot();
                _editOps.UndoController.RecordTopologyChange(
                    before, after, $"Bridge into {targetMc.Name}");
            }

            model.ComputeWorldMatrices();
            PrimitiveMeshFinalize(model);
            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} / 追加頂点 {added} → {targetMc.Name}");
        }

        /// <summary>
        /// 追加先＝新しい描画オブジェクト。両側とも位置クローンになる。
        /// ResolveBridgeParent が決めた親候補の子として作る。
        /// </summary>
        private void ExecuteBridgeNewObject(
            ModelContext model, PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            int parentIdx = ResolveBridgeParent(model, plan.SrcMeshA, plan.SrcMeshB);
            var parentMc  = parentIdx >= 0 ? model.GetMeshContext(parentIdx) : null;

            // 挿入で索引がずれるので、元メッシュは実体で控える。
            // 計画の SrcMeshA/B は計画を立てた時点の索引で、挿入後は別の要素を指す。
            var srcCtxA = model.GetMeshContext(plan.SrcMeshA);
            var srcCtxB = model.GetMeshContext(plan.SrcMeshB);

            // 頂点をどの空間へ格納するかは「生成物自身」が決める。親ではない。
            //
            //   生成物はウェイトを引き継ぐのでスキンドになる。スキンドの頂点は
            //   ワールド（バインド）空間へ格納するのが約束なので、変換を掛けない。
            //
            //   親を基準にすると誤る。スキンド後の親はボーンで、ボーンは頂点を
            //   持たないため WorldToVertexMatrix は WorldMatrixInverse を返す。
            //   それを掛けると、ボーンのワールド位置ぶん頂点がずれる。
            bool producesSkinned = (srcCtxA?.IsSkinned ?? false) || (srcCtxB?.IsSkinned ?? false);

            Matrix4x4 worldToLocal = producesSkinned
                ? Matrix4x4.identity
                : (parentMc != null ? parentMc.WorldToVertexMatrix : Matrix4x4.identity);

            var mo = new MeshObject(panel.BridgeMeshName);

            AppendBridgeInto(
                mo, worldToLocal, plan, false, false,
                srcCtxA?.MeshObject, srcCtxB?.MeshObject);

            var piece = new ObjectArrayPiece
            {
                Mesh          = mo,
                Name          = panel.BridgeMeshName,
                RelativeDepth = 0,
                CopyIndex     = 0,
            };

            var oldSelected = model.CaptureAllSelectedIndices();

            var added = ObjectArrayInserter.InsertAsChildren(
                model, new List<ObjectArrayPiece> { piece }, parentIdx, model.GenerateUniqueMeshName);

            if (added.Count == 0) { panel.SetBridgeStatus("生成できませんでした"); return; }

            model.ComputeWorldMatrices();

            model.ClearMeshSelection();
            foreach (var e in added) model.AddToMeshSelection(e.Index);
            var newSelected = model.CaptureAllSelectedIndices();

            if (_editOps?.UndoController != null)
            {
                _editOps.UndoController.SetModelContext(model);
                _editOps.UndoController.RecordMeshContextsAdd(added, oldSelected, newSelected);
            }

            PrimitiveMeshFinalize(model);

            // 両端の元メッシュがどちらもミラー実体側なら、生成物にもミラーを付ける。
            // ブリッジだけミラーが無いと、左右で構成が食い違ったまま残る。
            // 送るのは UI の ⇆ と同じコマンド。ここで直に EnableMirror を呼ばない。
            // 索引ではなく実体で持ち回る。コマンドはキュー経由で後から走るため、
            // その間に挿入・削除が挟まると索引はずれ、別の要素にミラーが付く。
            var generatedCtx = added[0].MeshContext;
            if (ShouldMirrorGeneratedBridge(model, srcCtxA, srcCtxB))
            {
                int idxNow = model.IndexOf(generatedCtx);
                if (idxNow >= 0)
                {
                    _panelContext?.SendCommand(new SetMirrorEnabledCommand(
                        ActiveProject?.CurrentModelIndex ?? 0, new[] { idxNow }, true));
                    panel.SetBridgeStatus(
                        $"面 {plan.Result.Faces.Count} → {generatedCtx.Name}（ミラーを付与）");
                    return;
                }
            }

            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} → {generatedCtx.Name}");
        }

        /// <summary>
        /// 生成したブリッジにミラーを付けるべきか。
        /// 穴A・穴Bの元メッシュが両方ともミラーペアの実体側のときだけ true。
        /// 片側だけの場合は左右の対応が決まらないので付けない。
        /// </summary>
        private static bool ShouldMirrorGeneratedBridge(
            ModelContext model, MeshContext mcA, MeshContext mcB)
        {
            if (model?.MirrorPairs == null) return false;
            if (mcA == null || mcB == null) return false;

            bool realA = false, realB = false;
            foreach (var pair in model.MirrorPairs)
            {
                if (pair?.Real == null) continue;
                if (ReferenceEquals(pair.Real, mcA)) realA = true;
                if (ReferenceEquals(pair.Real, mcB)) realB = true;
            }
            return realA && realB;
        }

        /// <summary>
        /// 新規オブジェクトのぶら下げ先。親候補は穴A物体と穴B物体の 2 つ。
        ///
        ///   同一オブジェクト        → それ自身
        ///   子孫関係がある          → ルートに近い側
        ///   子孫関係が無い          → 穴A物体
        ///
        /// 以前は子孫関係が無いとき -1（ルート直下）を返しており、
        /// 生成物がどちらの物体にも付かなかった。
        /// </summary>
        private static int ResolveBridgeParent(ModelContext model, int a, int b)
        {
            if (model == null) return -1;
            if (a < 0) return b;
            if (b < 0) return a;
            if (a == b) return a;

            if (IsBridgeAncestor(model, a, b)) return a;
            if (IsBridgeAncestor(model, b, a)) return b;

            // スキンド済みモデルでは 2 つの穴物体がどちらもボーンの子なので、
            // 互いに祖先にならない。どちらが親側かはボーンの鎖で決まるので、
            // それぞれの親ボーンの祖先関係を見て親側の穴物体を返す。
            //
            // ボーン自身を親にはしない。描画オブジェクトの並びの中に置きたいのと、
            // スキンドは頂点の変換に WorldMatrix を使わない（SkinningMatrix を使う）ため、
            // メッシュの子にしても描画には影響しないため。
            int byBone = ResolveBridgeParentByBoneChain(model, a, b);
            if (byBone >= 0) return byBone;

            return a;   // 決まらないときは穴A物体の子にする
        }

        /// <summary>
        /// 2 つの穴物体それぞれの親ボーンを引き、ボーンの祖先関係で親側を選ぶ。
        /// 決まらなければ -1。
        /// </summary>
        private static int ResolveBridgeParentByBoneChain(ModelContext model, int a, int b)
        {
            int boneA = BoneParentOf(model, a);
            int boneB = BoneParentOf(model, b);
            if (boneA < 0 || boneB < 0 || boneA == boneB) return -1;

            if (IsBridgeAncestor(model, boneA, boneB)) return a;
            if (IsBridgeAncestor(model, boneB, boneA)) return b;
            return -1;
        }

        /// <summary>描画オブジェクトが付いているボーンの索引。ボーンでなければ -1。</summary>
        private static int BoneParentOf(ModelContext model, int meshIndex)
        {
            var mc = model.GetMeshContext(meshIndex);
            if (mc == null) return -1;

            int p = mc.HierarchyParentIndex;
            if (p < 0 || p >= model.MeshContextCount) return -1;
            return model.GetMeshContext(p)?.Type == MeshType.Bone ? p : -1;
        }

        /// <summary>
        /// 追加先＝新しいモデル。両側とも位置クローンになる。
        ///
        /// 生成物は元モデルの頂点位置から作るだけで、既存頂点を参照しない。
        /// そのため他図形と同じ PlaceGeneratedMesh / PrimitiveMeshCreateNewModel 経路へ
        /// 流せる。新モデルには親が無いのでルート直下に置かれる。
        /// 座標はワールド空間のまま渡す（worldToLocal に単位行列を使う）。
        /// </summary>
        private void ExecuteBridgeNewModel(
            PlayerPrimitiveMeshSubPanel panel,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan)
        {
            var model = ActiveProject?.CurrentModel;
            if (model == null) { panel.SetBridgeStatus("モデルがありません"); return; }

            var mo = new MeshObject(panel.BridgeMeshName);

            AppendBridgeInto(
                mo, Matrix4x4.identity, plan, false, false,
                model.GetMeshContext(plan.SrcMeshA)?.MeshObject,
                model.GetMeshContext(plan.SrcMeshB)?.MeshObject);

            if (mo.FaceCount == 0) { panel.SetBridgeStatus("生成できませんでした"); return; }

            var pl = PrimitivePlacement.Default;
            pl.AddMode                = PrimitiveAddMode.NewModel;
            pl.MergeDuplicateVertices = false;
            // 失敗理由を捨てない。捨てると生成できていないのに
            // 「新しいモデル」と表示されてしまう。
            string placeReason = PlaceGeneratedMesh(
                mo, panel.BridgeMeshName, pl, Vector3.zero, Vector3.one, out _);
            if (placeReason != null) { panel.SetBridgeStatus(placeReason); return; }

            panel.SetBridgeStatus($"面 {plan.Result.Faces.Count} → 新しいモデル");
        }

        /// <summary>ancestor が descendant の祖先か。HierarchyParentIndex を辿る。</summary>
        private static bool IsBridgeAncestor(ModelContext model, int ancestor, int descendant)
        {
            int cur   = descendant;
            int guard = 0;
            while (cur >= 0 && guard++ < 4096)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) return false;
                int p = mc.HierarchyParentIndex;
                if (p == ancestor) return true;
                cur = p;
            }
            return false;
        }

        /// <summary>
        /// 計画にしたがって dst へ頂点と面を足す。戻り値は追加した頂点数。
        /// reuseA / reuseB が true の側は既存頂点インデックスをそのまま使う（穴の縁に直結）。
        /// false の側は位置クローンを作る。中間頂点は常に新規。
        /// </summary>
        private static int AppendBridgeInto(
            MeshObject dst, Matrix4x4 worldToLocal,
            PlayerPrimitiveMeshSubPanel.BridgePlan plan,
            bool reuseA, bool reuseB,
            MeshObject srcA, MeshObject srcB)
        {
            var r = plan.Result;
            int addedCount = 0;

            // 符号化ID → dst の頂点インデックス
            var map = new int[r.InterBase + r.Inter.Count];

            for (int k = 0; k < plan.LoopA.Count; k++)
            {
                if (reuseA) { map[k] = plan.LoopA[k]; continue; }
                map[k] = AddBridgeClone(
                    dst, worldToLocal.MultiplyPoint3x4(plan.WorldA[k]), srcA, plan.LoopA[k]);
                addedCount++;
            }

            for (int k = 0; k < plan.LoopB.Count; k++)
            {
                int id = r.ACount + k;
                if (reuseB) { map[id] = plan.LoopB[k]; continue; }
                map[id] = AddBridgeClone(
                    dst, worldToLocal.MultiplyPoint3x4(plan.WorldB[k]), srcB, plan.LoopB[k]);
                addedCount++;
            }

            // 中間頂点。位置は分割比で内分し、ウェイトも同じ比で補間する。
            for (int k = 0; k < r.Inter.Count; k++)
            {
                var ip = r.Inter[k];
                Vector3 world = Vector3.Lerp(plan.WorldA[ip.AIdx], plan.WorldB[ip.BIdx], ip.T);

                var v = new Poly_Ling.Data.Vertex(worldToLocal.MultiplyPoint3x4(world));

                v.BoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                    BridgeSourceWeight(srcA, plan.LoopA, ip.AIdx, false),
                    BridgeSourceWeight(srcB, plan.LoopB, ip.BIdx, false), ip.T);

                v.MirrorBoneWeight = Poly_Ling.UI.SkinWeightOps.LerpNullable(
                    BridgeSourceWeight(srcA, plan.LoopA, ip.AIdx, true),
                    BridgeSourceWeight(srcB, plan.LoopB, ip.BIdx, true), ip.T);

                map[r.InterBase + k] = dst.AddVertex(v);
                addedCount++;
            }

            foreach (var f in r.Faces)
            {
                var face = new Face();
                for (int i = 0; i < f.Length; i++) face.VertexIndices.Add(map[f[i]]);

                Vector3 n = BridgeFaceNormal(dst, face.VertexIndices);

                // UV / 法線スロットは同一インデックスで確保する（スロット不変条件）。
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    int slot = dst.Vertices[face.VertexIndices[i]].GetOrAddUVNormal(Vector2.zero, n);
                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }

                face.MaterialIndex = 0;
                dst.AddFace(face);
            }

            // ブリッジは別メッシュ間でも張れる。相手のウェイトを引き継いだ結果、
            // 転送先が初めてウェイトを持つことがあるため種別を確認し直す。
            dst.RecomputeSkinKind();

            return addedCount;
        }

        /// <summary>元メッシュの頂点位置クローンを dst へ足す。ウェイトは引き継ぐ。</summary>
        private static int AddBridgeClone(
            MeshObject dst, Vector3 localPos, MeshObject src, int srcVertexIndex)
        {
            var v = new Poly_Ling.Data.Vertex(localPos);

            if (src != null && srcVertexIndex >= 0 && srcVertexIndex < src.Vertices.Count)
            {
                var sv = src.Vertices[srcVertexIndex];
                v.BoneWeight       = sv.BoneWeight;
                v.MirrorBoneWeight = sv.MirrorBoneWeight;
            }

            return dst.AddVertex(v);
        }

        /// <summary>ループ上の頂点のウェイトを取る。取れないときは null。</summary>
        private static BoneWeight? BridgeSourceWeight(
            MeshObject src, List<int> loop, int loopIndex, bool mirror)
        {
            if (src == null || loop == null) return null;
            if (loopIndex < 0 || loopIndex >= loop.Count) return null;

            int vi = loop[loopIndex];
            if (vi < 0 || vi >= src.Vertices.Count) return null;

            return mirror ? src.Vertices[vi].MirrorBoneWeight : src.Vertices[vi].BoneWeight;
        }

        /// <summary>Newell 法の面法線。退化時は Vector3.up を返す。</summary>
        private static Vector3 BridgeFaceNormal(MeshObject mo, List<int> indices)
        {
            Vector3 n = Vector3.zero;
            int c = indices.Count;

            for (int i = 0; i < c; i++)
            {
                Vector3 p0 = mo.Vertices[indices[i]].Position;
                Vector3 p1 = mo.Vertices[indices[(i + 1) % c]].Position;
                n.x += (p0.y - p1.y) * (p0.z + p1.z);
                n.y += (p0.z - p1.z) * (p0.x + p1.x);
                n.z += (p0.x - p1.x) * (p0.y + p1.y);
            }

            return n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
        }
    }
}
