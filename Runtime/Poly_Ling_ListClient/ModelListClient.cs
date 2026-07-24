// ModelListClient.cs
// モデルリスト表示クライアント。空 GameObject にアタッチして使う。

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.ListClient
{
    public sealed class ModelListClient : ListClientBase
    {
        protected override string PanelTitle => "Model List";

        protected override void BuildRows(VisualElement listRoot)
        {
            var project = Project;
            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.Models[i];
                var row = MakeRow();

                var idx = MakeCell($"[{i}]", 34);
                if (i == project.CurrentModelIndex)
                    idx.style.color = new Color(0.4f, 0.8f, 1f);
                row.Add(idx);

                var name = MakeCell(model.Name ?? "(no name)", 0, grow: true);
                if (i == project.CurrentModelIndex)
                    name.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(name);

                row.Add(MakeCell($"{model.Count} mesh", 70));
                listRoot.Add(row);
            }
        }
    }
}
