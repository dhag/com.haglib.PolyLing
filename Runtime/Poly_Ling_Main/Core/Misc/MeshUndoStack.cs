// MeshUndoStack.cs
// メッシュ編集用の Undo スタック。汎用スタックにメッシュ固有の控えを足す。
// Runtime/Poly_Ling_Main/Core/Misc/ に配置
//
// 【何のためにあるか】
//   位相を変える操作（頂点・面の削除、ナイフ切断、面追加など）は、索引が
//   詰まるぶんだけパーツ選択辞書（MeshContext.PartsSelectionSetList）も
//   書き換わる。ところが位相系の Undo 記録は頂点と面しか控えていないので、
//   Undo しても辞書だけ戻らない。
//
//   記録クラスは 10 種類以上あり、同じ控えを各クラスへ書くとまた散らかる。
//   記録の積み込みと Undo/Redo の実行はどちらも UndoStack の 1 か所を通るので、
//   そこへ差し込む。汎用の UndoStack<TContext> は中身を知らないままにしたいので、
//   メッシュ用のこの派生クラスに置く（フックは UndoStack の OnRecording /
//   OnUndone / OnRedone）。
//
// 【控えの出どころ】
//   辞書を書き換えるのは MeshContext（MeshObject の VerticesRemoved /
//   FacesRemoved を購読している）。書き換える直前に自分で複製を持つので、
//   ここでは TakePendingPartsSetsBackup() で引き取るだけ。
//   辞書が動いていない操作では null が返り、記録には何も付かない。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem
{
    /// <summary>メッシュ編集用の Undo スタック。</summary>
    public sealed class MeshUndoStack : UndoStack<MeshUndoContext>
    {
        public MeshUndoStack(string id, string name, MeshUndoContext context)
            : base(id, name, context) { }

        /// <summary>
        /// 記録を積むとき、辞書の控えを記録へ移す。
        ///
        /// 1 操作で複数のメッシュの辞書が動くことがある（複数選択への一括処理、
        /// 実体とミラー側の対など）ので、モデル内の全メッシュから引き取る。
        /// 控えを持っているのは実際に書き換わったメッシュだけなので、
        /// 走査は件数ぶんの軽い確認で済む。
        /// </summary>
        protected override void OnRecording(IUndoRecord<MeshUndoContext> record)
        {
            if (record is not MeshUndoRecord mr) return;

            var model = Context?.ParentModelContext;
            if (model == null)
            {
                // モデルが引けないときは、せめて解決先の 1 件だけでも拾う。
                TakeFrom(mr, Context?.ResolvedMeshContext);
                return;
            }

            for (int i = 0; i < model.MeshContextCount; i++)
                TakeFrom(mr, model.GetMeshContext(i));
        }

        /// <summary>1 メッシュから控えを引き取って記録へ足す。</summary>
        private static void TakeFrom(MeshUndoRecord mr, MeshContext mc)
        {
            if (mc == null) return;

            var before   = mc.TakePendingPartsSetsBackup();
            var mo       = mc.MeshObject;
            var nxBefore = mo?.TakePendingNormalExcludeBackup();
            if (before == null && nxBefore == null) return;   // どちらも動いていない

            (mr.PartsSetsBackups ??= new List<MeshUndoRecord.PartsSetsBackup>())
                .Add(new MeshUndoRecord.PartsSetsBackup
                {
                    Owner               = mc,
                    Before              = before,
                    After               = before == null ? null : mc.ClonePartsSets(),
                    NormalExcludeBefore = nxBefore,
                    NormalExcludeAfter  = nxBefore == null ? null : mo.CloneNormalExcludeList()
                });
        }

        /// <summary>Undo の直後に、操作前の辞書・除外セットへ戻す。</summary>
        protected override void OnUndone(IUndoRecord<MeshUndoContext> record)
        {
            if (record is not MeshUndoRecord mr || mr.PartsSetsBackups == null) return;
            foreach (var b in mr.PartsSetsBackups)
            {
                if (b?.Owner == null) continue;
                b.Owner.RestorePartsSets(b.Before);
                b.Owner.MeshObject?.RestoreNormalExcludeList(b.NormalExcludeBefore);
            }
        }

        /// <summary>Redo の直後に、操作後の辞書・除外セットへ戻す。</summary>
        protected override void OnRedone(IUndoRecord<MeshUndoContext> record)
        {
            if (record is not MeshUndoRecord mr || mr.PartsSetsBackups == null) return;
            foreach (var b in mr.PartsSetsBackups)
            {
                if (b?.Owner == null) continue;
                b.Owner.RestorePartsSets(b.After);
                b.Owner.MeshObject?.RestoreNormalExcludeList(b.NormalExcludeAfter);
            }
        }
    }
}
