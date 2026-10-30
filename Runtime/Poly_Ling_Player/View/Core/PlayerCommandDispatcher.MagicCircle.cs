// PlayerCommandDispatcher.MagicCircle.cs
// コマンドディスパッチャ：魔法陣（空の親オブジェクト＋部品の子）。
// 空の親の下へ部品を子として作る処理（ExecuteEmptyParentAssembly）は鉄塔も使う。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【空の親】
//   パーツIDによる分解（PartsIdSplitInserter）と同じく、頂点 0・面 0 の MeshObject を持つ
//   描画オブジェクトにする。MeshContext.HierarchyParentIndex は MeshObject への委譲なので、
//   MeshObject を持たせないと親子づけが効かない。オブジェクトリストに出て、選んで動かせる。
//
// 【部品】
//   部品は図形生成コマンドとして Viewer の受け口（OnCreatePrimitiveMesh）へそのまま流す。
//   子はリストの末尾に足されるので、親（先に足した）の索引はずれない。
//   子の姿勢は単位のまま（部品の配置は MagicCircleElementCodec が原点・単位へ上書き済み）なので、
//   子のワールド姿勢は親の姿勢そのものになる。
//
// 【オブジェクトグループ】
//   部品 1 つが項目 1 つ、出力先はその子。作り直し（RebuildObjectGroupCommand）は
//   項目ごとに子の中身だけを入れ替える（PrimitiveMeshReplaceExisting）。親子・名前は残る。
//
// 【Undo】
//   サブディビジョンと同じく、オブジェクト一覧のスナップショットとグループの追加を 1 件にまとめる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：魔法陣。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchMagicCircle(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            if (!(cmd is CreateMagicCircleCommand c)) return false;

            int modelIndex = c.ModelIndex;
            string why = EnsureModelForAssembly(ref project, ref model, ref modelIndex);
            if (why != null) { Fail(why); return true; }

            string reason = ExecuteEmptyParentAssembly(
                project, model, modelIndex, c.Name, "MagicCircle",
                c.Position, c.Rotation, c.Scale, c.Elements, "魔法陣", out _);
            if (reason != null) Fail(reason);
            return true;
        }

        /// <summary>
        /// 部品だけの図形生成と同じく、モデルが無い状態から作れるようにする。
        /// 親を先にモデルへ足すので、部品の生成（EnsureProject を通る）より前に用意する。
        /// 戻り値は失敗理由。用意できたら null。
        /// </summary>
        private string EnsureModelForAssembly(ref ProjectContext project, ref ModelContext model, ref int modelIndex)
        {
            if (model != null && project != null) return null;

            if (OnEnsureProjectForCreate == null) return "ensure project handler not wired";
            project = OnEnsureProjectForCreate.Invoke();
            model   = project?.CurrentModel;
            if (model == null) return "モデルを用意できませんでした";
            modelIndex = project.CurrentModelIndex;
            return null;
        }

        /// <summary>
        /// 空の親を 1 つ作り、elements の部品を順にその子として作る。部品の並びは 1 件のグループになる。
        /// 戻り値は失敗理由（途中で落ちたときも、そこまでの変更は Undo に積む）。成功時は null。
        /// </summary>
        private string ExecuteEmptyParentAssembly(
            ProjectContext project, ModelContext model, int modelIndex,
            string requestedName, string defaultName,
            Vector3 position, Vector3 rotation, float scale,
            string[] elements, string undoLabel,
            out MeshContext parent)
        {
            parent = null;
            if (OnCreatePrimitiveMesh == null) return "primitive mesh handler not wired";
            if (elements == null || elements.Length == 0) return "部品が 1 つもありません";

            // 先に全部品を組んでおく。途中で組めないものがあれば、モデルに触る前に止める。
            var parts = new List<CreatePrimitiveMeshCommand>(elements.Length);
            for (int i = 0; i < elements.Length; i++)
            {
                if (!MagicCircleElementCodec.TryDecode(elements[i], modelIndex, out var ec, out string err))
                    return $"部品 {i + 1}: {err}";
                parts.Add(ec);
            }

            _undoController?.SetModelContext(model);
            var listBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ObjectGroupCount;

            string fail = null;
            Poly_Ling.Data.ObjectGroup group = null;

            _undoController?.SuspendRecording();
            try
            {
                // ── 親（空のオブジェクト）
                string name = model.GenerateUniqueMeshName(string.IsNullOrEmpty(requestedName) ? defaultName : requestedName);
                var mo = new MeshObject(name);
                var unityMesh = mo.ToUnityMesh();
                unityMesh.name      = name;
                unityMesh.hideFlags = HideFlags.HideAndDontSave;

                parent = new MeshContext
                {
                    MeshObject = mo,
                    Name       = name,
                    UnityMesh  = unityMesh,
                    IsVisible  = true,
                };
                parent.ParentModelContext = model;

                float s = scale > 0f ? scale : 1f;
                if (parent.BoneTransform != null)
                {
                    parent.BoneTransform.UseLocalTransform = true;
                    parent.BoneTransform.Position = position;
                    parent.BoneTransform.Rotation = rotation;
                    parent.BoneTransform.Scale    = new Vector3(s, s, s);
                }

                model.Add(parent);
                parent.Depth                = 0;
                parent.HierarchyParentIndex = -1;
                parent.ParentIndex          = -1;

                // ── 部品（子）
                for (int i = 0; i < parts.Count; i++)
                {
                    var ec = parts[i];
                    var child = CreateChildPart(model, parent, ec, out string r);
                    if (child == null) { fail = $"部品 {i + 1}（{ec.ShapeName}）: {r}"; break; }

                    var outIds = new List<ulong> { child.ObjectId };
                    if (group == null)
                        group = ObjectGroupOps.Capture(
                            project, modelIndex, ec, outIds, model.GenerateUniqueObjectGroupName(name));
                    else
                        ObjectGroupOps.AppendStep(project, modelIndex, group, ec, outIds);
                }

                if (group != null)
                {
                    model.AddObjectGroup(group);
                    // 作った直後の入力・出力を控える（作り直し整備計画 段 9。CaptureObjectGroup を通らないため）。
                    ModifierEvaluator.PrimeGroup(model, group);
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            // 途中で落ちても記録は積む。そこまでの変更を Undo で戻せるようにする。
            RecordSubdivisionUndo(model, listBefore, groupsBefore, null, undoLabel);

            model.ComputeWorldMatrices();
            model.IsDirty = true;

            int parentIndex = model.MeshContextList.IndexOf(parent);
            if (parentIndex >= 0)
            {
                model.SelectMeshContextExclusive(parentIndex);
                model.SelectMesh(parentIndex);
            }

            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.ListStructure);

            if (parentIndex >= 0)
                ReportTargets(new[] { parentIndex }, parent.ObjectId != 0UL ? new[] { parent.ObjectId } : null);

            return fail;
        }

        /// <summary>
        /// 部品 1 つを図形生成の受け口へ流し、できた子を parent の子にする。
        /// 作れなかったときは null を返し、why に理由。
        /// </summary>
        private MeshContext CreateChildPart(
            ModelContext model, MeshContext parent, CreatePrimitiveMeshCommand ec, out string why)
        {
            why = null;
            var before = SnapshotObjectIds();

            string r = OnCreatePrimitiveMesh.Invoke(ec);
            if (r != null) { why = r; return null; }

            var child = NewestCreated(model, before);
            if (child == null) { why = "作った子が見つかりません"; return null; }

            int pi = model.MeshContextList.IndexOf(parent);
            child.HierarchyParentIndex = pi;
            child.ParentIndex          = pi;
            child.Depth                = parent.Depth + 1;
            return child;
        }

        /// <summary>
        /// 実行前に控えた ObjectId に無いもののうち、最も新しいもの。
        /// ObjectId は単調増加なので最大値が最後に作られたもの（CaptureObjectGroup と同じ考え方）。
        /// </summary>
        private static MeshContext NewestCreated(ModelContext model, HashSet<ulong> before)
        {
            MeshContext newest = null;
            if (model?.MeshContextList == null) return null;

            foreach (var mc in model.MeshContextList)
            {
                if (mc == null || mc.ObjectId == 0UL) continue;
                if (before != null && before.Contains(mc.ObjectId)) continue;
                if (newest == null || mc.ObjectId > newest.ObjectId) newest = mc;
            }
            return newest;
        }
    }
}
