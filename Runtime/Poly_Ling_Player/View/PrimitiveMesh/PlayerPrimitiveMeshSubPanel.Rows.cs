// PlayerPrimitiveMeshSubPanel.Rows.cs
// 図形生成サブパネル：諸元 UI の行ヘルパ（見出し・スライダ行・トグル行・ピボット）。
// 図形ファイルはこの行ヘルパだけで諸元 UI を組む。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // ピボットUIヘルパー
        // ================================================================

        /// <summary>
        /// 現在のパラメータで生成したメッシュから「重心を原点へ寄せるためのピボット増分」を返す。
        ///
        /// 全ジェネレータは「ピボット p で頂点を -p * S だけ平行移動する（S = 形状サイズ）」規約のため、
        /// 現在の重心 c に対して Δp = c / S を足せば重心が原点へ来る。S は生成メッシュの AABB サイズで測る。
        /// 姿勢（回転・スケール）は焼き込まずに測るので、姿勢を変えても結果は変わらない。
        /// 生成できない図形（穴つなぎ・歪み複製など）では null を返す。
        /// </summary>
        private Vector3? PivotDeltaToCentroid()
        {
            var mo = Generate(true, false);
            if (mo == null || mo.Vertices == null || mo.Vertices.Count == 0) return null;

            Vector3 min = mo.Vertices[0].Position, max = min, sum = Vector3.zero;
            foreach (var v in mo.Vertices)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
                sum += v.Position;
            }
            Vector3 c = sum / mo.Vertices.Count;
            Vector3 s = max - min;

            return new Vector3(
                Mathf.Abs(s.x) > 1e-6f ? c.x / s.x : 0f,
                Mathf.Abs(s.y) > 1e-6f ? c.y / s.y : 0f,
                Mathf.Abs(s.z) > 1e-6f ? c.z / s.z : 0f);
        }

        /// <summary>
        /// 見出し付きの折り畳みセクションを作り、中身を入れるコンテナを返す。
        /// 3つの2Dプロファイル編集（回転体・2D押し出し・はしご断面）で共用する。
        /// </summary>
        private static VisualElement FoldSection(VisualElement parent, string title, bool open)
        {
            var f = new Foldout { text = title, value = open };
            f.style.marginBottom = 4;
            parent.Add(f);
            return f.contentContainer;
        }

        private void BuildPivotY(VisualElement c,
            Func<float> getY, Action<float> setY,
            Vector3 bottom, Vector3 center, Vector3 top,
            out Action sync, out VisualElement content)
        {
            var fold = new Foldout { text = T("PivotOffset"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            content = f;
            f.Add(SR(T("PivotY"), PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax, getY, setY, out var ySl, out var yNf));
            void SyncY() { float v = getY(); ySl.SetValueWithoutNotify(v); yNf.SetValueWithoutNotify((float)Math.Round(v, 3)); }
            sync = SyncY;
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => { setY(bottom.y); SyncY(); });
            SB(row, T("Center"), () => { setY(center.y); SyncY(); });
            SB(row, T("Top"),    () => { setY(top.y);    SyncY(); });
            SB(row, T("PivotCentroid"), () =>
            {
                var d = PivotDeltaToCentroid();
                if (!d.HasValue) return;
                setY(getY() + d.Value.y); SyncY();
            });
            f.Add(row);
            c.Add(fold);
        }

        private void BuildPivotXYZ(VisualElement c,
            Func<Vector3> get, Action<Vector3> set,
            float min, float max,
            Vector3 bottom, Vector3 center, Vector3 top,
            out VisualElement content)
        {
            var fold = new Foldout { text = T("PivotOffset"), value = false };
            fold.style.marginBottom = 4;
            var f = fold.contentContainer;
            content = f;
            f.Add(SR(T("PivotX"), min, max, () => get().x, v => { var p = get(); set(new Vector3(v, p.y, p.z)); }, out var xSl, out var xNf));
            f.Add(SR(T("PivotY"), min, max, () => get().y, v => { var p = get(); set(new Vector3(p.x, v, p.z)); }, out var ySl, out var yNf));
            f.Add(SR(T("PivotZ"), min, max, () => get().z, v => { var p = get(); set(new Vector3(p.x, p.y, v)); }, out var zSl, out var zNf));
            void SyncXYZ()
            {
                var p = get();
                xSl.SetValueWithoutNotify(p.x); xNf.SetValueWithoutNotify((float)Math.Round(p.x, 3));
                ySl.SetValueWithoutNotify(p.y); yNf.SetValueWithoutNotify((float)Math.Round(p.y, 3));
                zSl.SetValueWithoutNotify(p.z); zNf.SetValueWithoutNotify((float)Math.Round(p.z, 3));
            }
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 4;
            SB(row, T("Bottom"), () => { set(bottom); SyncXYZ(); });
            SB(row, T("Center"), () => { set(center); SyncXYZ(); });
            SB(row, T("Top"),    () => { set(top);    SyncXYZ(); });
            SB(row, T("PivotCentroid"), () =>
            {
                var d = PivotDeltaToCentroid();
                if (!d.HasValue) return;
                set(get() + d.Value); SyncXYZ();
            });
            f.Add(row);
            c.Add(fold);
        }

        /// <summary>
        /// 図形名の見出し。上に太い区切り線を引き、他のセクション見出し（SL）より
        /// 大きく太くして、ここから個別パラメータが始まることを分かるようにする。
        /// </summary>
        private static VisualElement ShapeTitle(string t)
        {
            var box = new VisualElement();
            box.style.marginTop    = 8;
            box.style.marginBottom = 4;

            var rule = new VisualElement();
            rule.style.height          = 2;
            rule.style.marginBottom    = 4;
            rule.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.30f));
            box.Add(rule);

            var l = new Label(t);
            l.style.fontSize = 14;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(new Color(0.75f, 0.88f, 1f));
            box.Add(l);

            return box;
        }

        private static Label SL(string t, bool bold = false)
        {
            var l = new Label(t);
            l.style.marginTop = bold ? 2 : 5; l.style.marginBottom = 2;
            l.style.color = bold
                ? new StyleColor(new Color(0.9f, 0.9f, 0.9f))
                : new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = bold ? 11 : 10;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
        }

        // out 版: 生成したスライダ/数値欄を呼び出し側へ返す（プリセットボタンからの同期用）。
        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set,
            out Slider slOut, out FloatField nfOut)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new Slider(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() }; nf.style.width = 42;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3)); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { float v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf);
            slOut = sl; nfOut = nf;
            return row;
        }

        private static VisualElement IR(string label, int min, int max, Func<int> get, Action<int> set)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            row.Add(ML(label));
            var sl = new SliderInt(min, max) { value = get() }; sl.style.flexGrow = 1;
            var nf = new IntegerField { value = get() }; nf.style.width = 36;
            sl.RegisterValueChangedCallback(e => { nf.SetValueWithoutNotify(e.newValue); set(e.newValue); });
            nf.RegisterValueChangedCallback(e => { int v = Mathf.Clamp(e.newValue, min, max); sl.SetValueWithoutNotify(v); set(v); });
            row.Add(sl); row.Add(nf); return row;
        }

        private static VisualElement TR(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() }; t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => set(e.newValue)); return t;
        }

        /// <summary>
        /// FloatField 3 連の行。参照を保持しない従来版。
        /// 実装は <see cref="V3FRef"/> に一本化してある（outFields = null）。
        /// </summary>
        private static VisualElement V3F(
            string lx, string ly, string lz,
            Func<float> gx, Action<float> sx,
            Func<float> gy, Action<float> sy,
            Func<float> gz, Action<float> sz)
            => V3FRef(lx, ly, lz, gx, sx, gy, sy, gz, sz, null);

        /// <summary>
        /// <see cref="V3F"/> と同一構造で、生成した FloatField を <paramref name="outFields"/>
        /// (長さ3) に保持する版。外部から <see cref="RefreshTrsFields"/> で値を書き戻すために使う。
        /// </summary>
        private static VisualElement V3FRef(
            string lx, string ly, string lz,
            Func<float> gx, Action<float> sx,
            Func<float> gy, Action<float> sy,
            Func<float> gz, Action<float> sz,
            FloatField[] outFields)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            void AddFF(int slot, string lbl, Func<float> g, Action<float> s)
            {
                var sub = new VisualElement(); sub.style.flexDirection = FlexDirection.Row; sub.style.flexGrow = 1;
                var l = new Label(lbl); l.style.width = 14; l.style.unityTextAlign = TextAnchor.MiddleLeft;
                var f = new FloatField { value = g() }; f.style.flexGrow = 1;
                f.RegisterValueChangedCallback(e => s(e.newValue));
                sub.Add(l); sub.Add(f); row.Add(sub);
                if (outFields != null && slot >= 0 && slot < outFields.Length) outFields[slot] = f;
            }
            AddFF(0, lx, gx, sx); AddFF(1, ly, gy, sy); AddFF(2, lz, gz, sz);
            return row;
        }

        private static Label ML(string t)
        {
            var l = new Label(t); l.style.width = 80;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.style.fontSize = 10; return l;
        }

        private static void SB(VisualElement p, string t, Action onClick)
        {
            var b = new Button(onClick) { text = t }; b.style.flexGrow = 1; b.style.marginRight = 2;
            b.style.height = 18; b.style.fontSize = 9; p.Add(b);
        }
    }
}
