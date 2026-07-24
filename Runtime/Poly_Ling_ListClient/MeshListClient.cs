// MeshListClient.cs
// メッシュリスト表示クライアント。空 GameObject にアタッチして使う。
// 選択中モデルの MeshContextList を表示する。頂点数/面数は Summary 由来
// (描画メッシュ本体は取得しない)。

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.ListClient
{
    public sealed class MeshListClient : ListClientBase
    {
        protected override string PanelTitle => "Mesh List";
        protected override bool UsesModelSelector => true;

        protected override void BuildRows(VisualElement listRoot)
        {
            var project = Project;
            if (SelectedModelIndex < 0 || SelectedModelIndex >= project.ModelCount) return;

            var model = project.Models[SelectedModelIndex];
            var list  = model.MeshContextList;
            if (list == null || list.Count == 0)
            {
                listRoot.Add(new Label("(メッシュなし)"));
                return;
            }

            // ヘッダ行
            var head = MakeRow();
            head.style.unityFontStyleAndWeight = FontStyle.Bold;
            head.Add(MakeCell("#", 34));
            head.Add(MakeCell("Name", 0, grow: true));
            head.Add(MakeCell("Type", 64));
            head.Add(MakeCell("V", 60));
            head.Add(MakeCell("F", 60));
            head.Add(MakeCell("状態", 44));
            listRoot.Add(head);

            for (int si = 0; si < list.Count; si++)
            {
                var mc = list[si];
                if (mc == null) continue;

                var row = MakeRow();
                row.Add(MakeCell($"{si}", 34));

                var name = MakeCell(mc.Name ?? "(no name)", 0, grow: true);
                if (!mc.IsVisible) name.style.color = new Color(1, 1, 1, 0.4f);
                row.Add(name);

                row.Add(MakeCell(mc.Type.ToString(), 64));

                // 頂点数/面数は Summary 由来(Counts)。未取得時は "-"。
                int vc = 0, fc = 0;
                bool hasCount = Counts.TryGetValue((SelectedModelIndex, si), out var c);
                if (hasCount) { vc = c.vc; fc = c.fc; }
                row.Add(MakeCell(hasCount ? vc.ToString() : "-", 60));
                row.Add(MakeCell(hasCount ? fc.ToString() : "-", 60));

                string flags = (mc.IsLocked ? "L" : "") + (mc.IsVisible ? "" : "H");
                row.Add(MakeCell(flags, 44));

                listRoot.Add(row);
            }
        }
    }
}
