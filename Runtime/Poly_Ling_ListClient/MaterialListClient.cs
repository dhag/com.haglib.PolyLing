// MaterialListClient.cs
// マテリアルリスト表示クライアント。空 GameObject にアタッチして使う。
// 選択中モデルの MaterialReferences を表示する。

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.ListClient
{
    public sealed class MaterialListClient : ListClientBase
    {
        protected override string PanelTitle => "Material List";
        protected override bool UsesModelSelector => true;

        protected override void BuildRows(VisualElement listRoot)
        {
            var project = Project;
            if (SelectedModelIndex < 0 || SelectedModelIndex >= project.ModelCount) return;

            var model = project.Models[SelectedModelIndex];
            var refs  = model.MaterialReferences;
            if (refs == null || refs.Count == 0)
            {
                listRoot.Add(new Label("(マテリアルなし)"));
                return;
            }

            for (int i = 0; i < refs.Count; i++)
            {
                var mref = refs[i];
                var row = MakeRow();

                row.Add(MakeCell($"[{i}]", 34));

                // カラースウォッチ
                var swatch = new VisualElement();
                swatch.style.width = 14; swatch.style.height = 14;
                swatch.style.flexShrink = 0;
                swatch.style.marginRight = 6;
                swatch.style.borderTopWidth = 1; swatch.style.borderBottomWidth = 1;
                swatch.style.borderLeftWidth = 1; swatch.style.borderRightWidth = 1;
                swatch.style.borderTopColor = new Color(0, 0, 0, 0.4f);
                swatch.style.borderBottomColor = new Color(0, 0, 0, 0.4f);
                swatch.style.borderLeftColor = new Color(0, 0, 0, 0.4f);
                swatch.style.borderRightColor = new Color(0, 0, 0, 0.4f);
                swatch.style.backgroundColor = mref?.Data != null ? mref.Data.GetBaseColor() : Color.gray;
                row.Add(swatch);

                row.Add(MakeCell(mref?.Name ?? "(no name)", 0, grow: true));
                listRoot.Add(row);
            }
        }
    }
}
