// ModelUndoStack.cs
// メッシュリスト用の Undo スタック。汎用スタックにモデル固有の控えを足す。
// Runtime/Poly_Ling_Main/Core/Misc/ に配置
//
// 【何のためにあるか】
//   オブジェクトの削除・挿入・移動でリストの索引が詰まると、頂点ボーンウェイトの
//   参照先ボーン索引も付け替わる（ModelContext.RemapIndexReferences）。
//   書き換え前の値はモデルが預かる（ModelContext.TakePendingBoneWeightBackup）。
//
//   この控えを引き取る場所が要る。MeshListStack.Record の直接呼び出しは
//   20 か所以上あり（MeshListOps の各操作、DeleteSelectionTool、ObjectMoveTool ほか）、
//   呼び出し側に置くと必ず取りこぼす。記録が積まれる 1 か所＝ここで引き取る。
//
// 【記録の種類で扱いが違う】
//   ・追加・削除（MeshListChangeRecord）
//       Undo/Redo は adjustSelection:false で挿し戻すので付け替えが走らない。
//       さらに削除は「消したボーンのウェイトを親へ寄せる」ため逆写像でも戻せない。
//       よって控えを記録へ持たせ、Undo/Redo で書き戻す。
//   ・並べ替え（MeshReorderChangeRecord）ほか
//       Undo/Redo が RemapIndexReferencesAfterReorder を呼ぶので、付け替えが
//       逆向きに走って元へ戻る。控えは不要なので、ここで捨てる。
//       捨てないと次の追加・削除の記録に「操作前」として誤って付く。

using System.Linq;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.UndoSystem
{
    /// <summary>メッシュリスト用の Undo スタック。</summary>
    public sealed class ModelUndoStack : UndoStack<ModelContext>
    {
        public ModelUndoStack(string id, string name, ModelContext context)
            : base(id, name, context) { }

        /// <summary>記録を積むとき、ウェイトの控えを必ず引き取る。</summary>
        protected override void OnRecording(IUndoRecord<ModelContext> record)
        {
            var before = Context?.TakePendingBoneWeightBackup();
            if (before == null || before.Count == 0) return;

            // 付け替えが自動で逆向きに走る記録では控えは不要。引き取って捨てる。
            if (record is not MeshListChangeRecord mlc) return;

            mlc.BeforeBoneWeights = before;
            mlc.AfterBoneWeights  = BoneWeightBackup.CaptureByObjectIds(
                Context, before.Select(e => e.ObjectId));
        }
    }
}
