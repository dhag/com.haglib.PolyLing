// MaterialEditHandler.cs
// マテリアルスロットの色・Metallic・Smoothness の「操作中プレビュー → 確定／取消」を受け持つ
// （操作経路統一計画.md E、M-2）。
//
// 【なぜツールの窓口に載せるか】
//   材質一覧パネルは、スライダー操作中に起きている Material を直接書き換えて見た目を変え、
//   離したときにコマンドで確定していた。パネルがモデル側のオブジェクトを触らないよう、
//   Rotate／Scale と同じく「プレビュー（見た目だけ）・確定（コマンド）・取消（保存データへ戻す）」を
//   ツールの窓口（IToolSurface）の操作として公開する。パネル・MCP・リモートは同じ操作を呼ぶ。
//
// 【プレビューは見た目だけ】
//   保存に乗るのは MaterialReference.Data。プレビューは起きている Material だけを書き、
//   Data・変更フラグ・Undo には触れない。確定は SetMaterialColorCommand／SetMaterialScalarCommand で
//   行い、確定・取消のあとは Material を Data の値へ揃える（担当者判定で止められた場合も
//   見た目と保存データの食い違いを残さない）。

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Materials;

namespace Poly_Ling.Player
{
    [PLTool("materialEdit", Description = "マテリアルスロットの色・Metallic・Smoothness のプレビューと確定")]
    public sealed class MaterialEditHandler
    {
        public Func<ModelContext>   GetModel;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;
        public Action               OnRepaint;

        [PLToolAction(Description = "基本色をプレビューする（見た目だけ。保存データは変えない）")]
        public void PreviewColor(int slot, float r, float g, float b, float a)
        {
            var mat = MaterialOf(slot);
            if (mat == null) return;
            MaterialEditOps.SetColor(mat, new Color(r, g, b, a));
            OnRepaint?.Invoke();
        }

        [PLToolAction(Description = "Metallic / Smoothness をプレビューする（見た目だけ。保存データは変えない）")]
        public void PreviewScalar(int slot, MaterialScalarKind kind, float value)
        {
            var mat = MaterialOf(slot);
            if (mat == null) return;
            MaterialEditOps.SetScalar(mat, kind, value);
            OnRepaint?.Invoke();
        }

        [PLToolAction(Description = "基本色を SetMaterialColorCommand で確定し、見た目を保存データへ揃える")]
        public void CommitColor(int slot, float r, float g, float b, float a)
        {
            SendCommand?.Invoke(new SetMaterialColorCommand(
                GetModelIndex?.Invoke() ?? 0, slot, new[] { r, g, b, a }));
            RevertPreview(slot);
        }

        [PLToolAction(Description = "Metallic / Smoothness を SetMaterialScalarCommand で確定し、見た目を保存データへ揃える")]
        public void CommitScalar(int slot, MaterialScalarKind kind, float value)
        {
            SendCommand?.Invoke(new SetMaterialScalarCommand(
                GetModelIndex?.Invoke() ?? 0, slot, kind, value));
            RevertPreview(slot);
        }

        [PLToolAction(Description = "プレビューを取り消し、見た目を保存データの値へ戻す")]
        public void RevertPreview(int slot)
        {
            var model = GetModel?.Invoke();
            if (model == null || slot < 0 || slot >= model.MaterialCount) return;
            var matRef = model.GetMaterialReference(slot);
            var mat    = model.GetMaterial(slot);
            var d      = matRef?.Data;
            if (mat == null || d == null) return;

            if (mat.HasProperty("_BaseColor") || mat.HasProperty("_Color"))
                MaterialEditOps.SetColor(mat, d.GetBaseColor());
            MaterialEditOps.SetScalar(mat, MaterialScalarKind.Metallic,   d.Metallic);
            MaterialEditOps.SetScalar(mat, MaterialScalarKind.Smoothness, d.Smoothness);
            OnRepaint?.Invoke();
        }

        private Material MaterialOf(int slot)
        {
            var model = GetModel?.Invoke();
            if (model == null || slot < 0 || slot >= model.MaterialCount) return null;
            return model.GetMaterial(slot);
        }
    }
}
