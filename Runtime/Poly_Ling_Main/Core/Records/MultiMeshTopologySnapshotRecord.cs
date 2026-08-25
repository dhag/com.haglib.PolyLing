// MultiMeshTopologySnapshotRecord.cs
// 複数メッシュの位相変更（頂点・面の増減を含む）の Undo/Redo 記録。
// Runtime/Poly_Ling_Main/Core/Records/ に配置
//
// 【なぜ MeshObjectSnapshot を使わないか】
//   MeshUndoContext.MeshObject は ParentModelContext.FirstSelectedMeshContext へ
//   解決される（MeshUndoContext.cs:54,64）。複数メッシュを同時に書き換える操作が
//   これを経由すると、先頭メッシュだけが復元される。そのため本レコードは
//   MeshContextIndex をキーに持ち、Undo/Redo で ModelContext から自分で解決する。
//   （MultiMeshVertexSnapshotRecord と同じ方式。あちらは座標のみ、こちらは位相ごと）
//
// 【UnityMesh】既存の単一メッシュ位相 Undo（MeshSnapshotRecord）と同じく触らない。
//   描画側は Undo 適用後の RebuildAdapter で MeshObject から作り直される。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// 複数メッシュの MeshObject スナップショット（触ったメッシュだけを持つ）。
    /// </summary>
    public class MultiMeshTopologySnapshot
    {
        /// <summary>MeshContext インデックス → MeshObject の複製</summary>
        public Dictionary<int, MeshObject> Meshes = new Dictionary<int, MeshObject>();

        /// <summary>
        /// MeshContext インデックス → 捕獲時の MeshContext 実体。
        ///
        /// 索引だけで戻すと、捕獲と復元の間にリストの並べ替え・挿入・削除が挟まった
        /// 場合に別メッシュへ書き込む。実体で引き当てられるならそちらを優先する。
        /// </summary>
        public Dictionary<int, MeshContext> Contexts = new Dictionary<int, MeshContext>();

        /// <summary>指定インデックスのメッシュを1つ取り込む。</summary>
        public void CaptureMesh(ModelContext model, int meshContextIndex)
        {
            var mc = model?.GetMeshContext(meshContextIndex);
            if (mc?.MeshObject == null) return;
            Meshes[meshContextIndex]   = mc.MeshObject.Clone();
            Contexts[meshContextIndex] = mc;
        }

        /// <summary>保持しているメッシュを ModelContext へ戻す。</summary>
        public void RestoreTo(ModelContext model)
        {
            if (model == null) return;

            foreach (var kv in Meshes)
            {
                if (kv.Value == null) continue;

                // 実体優先。モデルから消えている場合だけ索引へ落とす。
                MeshContext mc = null;
                if (Contexts.TryGetValue(kv.Key, out var captured)
                    && captured != null
                    && model.MeshContextList.Contains(captured))
                {
                    mc = captured;
                }
                else
                {
                    mc = model.GetMeshContext(kv.Key);
                }
                if (mc == null) continue;

                mc.MeshObject = kv.Value.Clone();
                mc.MeshObject.InvalidatePositionCache();
            }
        }
    }

    /// <summary>
    /// 複数メッシュの位相変更の Undo/Redo 記録。MeshListStack へ記録する。
    /// </summary>
    public class MultiMeshTopologySnapshotRecord : IUndoRecord<ModelContext>
    {
        private readonly MultiMeshTopologySnapshot _before;
        private readonly MultiMeshTopologySnapshot _after;

        public UndoOperationInfo Info { get; set; }

        public MultiMeshTopologySnapshotRecord(
            MultiMeshTopologySnapshot before,
            MultiMeshTopologySnapshot after,
            string description = "Topology Change")
        {
            _before = before;
            _after  = after;
            Info    = new UndoOperationInfo(description, "MultiMeshTopology");
        }

        public void Undo(ModelContext context)
        {
            _before?.RestoreTo(context);
            context?.OnListChanged?.Invoke();
        }

        public void Redo(ModelContext context)
        {
            _after?.RestoreTo(context);
            context?.OnListChanged?.Invoke();
        }
    }
}
