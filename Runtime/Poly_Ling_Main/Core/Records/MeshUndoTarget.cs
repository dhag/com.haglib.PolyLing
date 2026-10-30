// MeshUndoTarget.cs
// 頂点編集の履歴（MeshUndoStack）に積む記録の「書き込み先」。
// Runtime/Poly_Ling_Main/Core/Records/ に配置
//
// 【何のためにあるか】（作り直し整備計画 段 12）
//   頂点編集の履歴は、書き込み先のオブジェクトが替わるたびに消していた
//   （MeshUndoController.SetMeshObject / SetMeshObjectFor の _vertexEditStack.Clear()）。
//   消すと、別のオブジェクトを続けて編集したあとの Undo が手前の編集を飛ばして
//   一覧の履歴（グループの追加など）へ進む。消すのをやめるには、残った記録が
//   Undo の時点の選択や MeshUndoContext の解決先ではなく、記録したときの相手へ
//   書き戻せなければならない。
//
//   MeshContext の参照だけでは足りない。一覧のスナップショットの Undo・Redo
//   （MeshFilterToSkinnedRecord.RestoreList など）は描画オブジェクトを複製に置き換えるので、
//   参照は置き換え前のオブジェクトを指したまま残る。索引も、一覧の Undo で並びが
//   変わると別のオブジェクトを指す。そこで参照・モデル・ObjectId を控え、
//   Undo・Redo の時点で今のモデルの一覧から引き直す。
//
// 【引き直しの順】
//   1. モデルの一覧に同じ参照がいれば、それ
//   2. ObjectId が 0 でなければ、モデルの一覧から同じ ObjectId のもの
//   3. どちらも無ければ見つからない（書き戻さない。別のオブジェクトへ書かない）

using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.UndoSystem
{
    /// <summary>頂点編集の記録の書き込み先（描画オブジェクト 1 個）。</summary>
    public struct MeshUndoTarget
    {
        /// <summary>記録したときの描画オブジェクト。</summary>
        public MeshContext Context;

        /// <summary>そのオブジェクトがいたモデル。</summary>
        public ModelContext Model;

        /// <summary>そのオブジェクトの安定 ID。0 なら ID で引き直さない。</summary>
        public ulong ObjectId;

        /// <summary>控えがあるか。</summary>
        public bool IsSet => Context != null || (Model != null && ObjectId != 0UL);

        /// <summary>
        /// 描画オブジェクトから控える。モデルはオブジェクトの持ち主を優先し、
        /// 無ければ fallbackModel。
        /// </summary>
        public static MeshUndoTarget Of(MeshContext mc, ModelContext fallbackModel = null)
        {
            if (mc == null) return default;
            return new MeshUndoTarget
            {
                Context  = mc,
                Model    = mc.ParentModelContext ?? fallbackModel,
                ObjectId = mc.ObjectId,
            };
        }

        /// <summary>
        /// モデルと索引から控える（複数メッシュの記録の要素）。
        /// 索引が範囲外なら控えない。
        /// </summary>
        public static MeshUndoTarget OfIndex(ModelContext model, int index)
        {
            var mc = model?.GetMeshContext(index);
            if (mc == null) return default;
            return new MeshUndoTarget { Context = mc, Model = model, ObjectId = mc.ObjectId };
        }

        /// <summary>今のモデルの一覧から引き直す。見つからなければ null。</summary>
        public MeshContext Resolve()
        {
            var list = Model?.MeshContextList;
            if (list == null) return Context;   // モデルが分からないときは参照のまま

            if (Context != null)
            {
                foreach (var mc in list)
                    if (ReferenceEquals(mc, Context)) return mc;
            }
            if (ObjectId != 0UL)
            {
                foreach (var mc in list)
                    if (mc != null && mc.ObjectId == ObjectId) return mc;
            }
            return null;
        }

        /// <summary>
        /// 複数メッシュの記録の要素（索引＋ObjectId）を今の索引へ引き直す。
        /// モデルが無いか ObjectId が 0 なら索引のまま。ObjectId が一覧に無ければ -1。
        /// </summary>
        public static int ResolveIndex(ModelContext model, int index, ulong objectId)
        {
            if (model == null || objectId == 0UL) return index;
            var list = model.MeshContextList;
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].ObjectId == objectId) return i;
            return -1;
        }

        /// <summary>今のモデルの一覧での索引。見つからなければ -1。</summary>
        public int ResolveIndex()
        {
            var mc = Resolve();
            return mc != null && Model != null ? Model.IndexOf(mc) : -1;
        }
    }
}
