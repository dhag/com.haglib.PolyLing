// PlayerPrimitiveMeshSubPanel.MagicCircle.cs
// 図形生成サブパネル：魔法陣（高度な図形）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【部品】
//   リング・紋章図形・円形の文字・星座の円周配置・オブジェクトの円周配置を、
//   一覧に好きな数だけ並べる。六芒星の中に六芒星を入れるときは、大きさ（と回転）の違う
//   紋章の部品を 2 つ並べる。部品の設定は一覧で選んだものだけを下に出す。
//
// 【生成経路】
//   部品ごとに図形生成コマンドを組み（BuildMagicPartCommand）、文字列へ直して
//   CreateMagicCircleCommand に載せる。空の親を作って部品を子として作るのはディスパッチャ側。
//   プレビューは同じ部品コマンドを PrimitiveMeshFactory に通し、1 つへ連結して見せる。
//
// 【全体の位置・向き】
//   共通の配置欄（位置・回転・拡大）は親の姿勢になる。部品は親の原点に単位の姿勢で置く。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.GlyphText;
using Poly_Ling.PlaceObject;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private enum MagicPartKind { Ring, Emblem, Text, Zodiac, Objects }

        /// <summary>部品の種類ごとの表示文字のキー。MagicPartKind の並びと一致させる。</summary>
        private static readonly string[] MagicPartKindKeys =
            { "MagicCircleKindRing", "MagicCircleKindEmblem", "MagicCircleKindText",
              "MagicCircleKindZodiac", "MagicCircleKindObjects" };

        /// <summary>部品名に付ける英字。MagicPartKind の並びと一致させる。</summary>
        private static readonly string[] MagicPartNameTags =
            { "Ring", "Emblem", "Text", "Zodiac", "Objects" };

        /// <summary>魔法陣の部品 1 つ。種類に応じたパラメータだけを使う。</summary>
        private sealed class MagicPart
        {
            public MagicPartKind Kind;
            public MagicRingMeshGenerator.MagicRingParams       Ring    = MagicRingMeshGenerator.MagicRingParams.Default;
            public EmblemMeshGenerator.EmblemParams              Emblem  = EmblemMeshGenerator.EmblemParams.Default;
            public TextMeshParams                                Text    = DefaultMagicText();
            public ZodiacCircleMeshGenerator.ZodiacCircleParams  Zodiac  = ZodiacCircleMeshGenerator.ZodiacCircleParams.Default;
            public RadialPlaceMeshGenerator.RadialPlaceParams    Objects = RadialPlaceMeshGenerator.RadialPlaceParams.Default;
            public MeshSourceMultiPick                           Pick    = new MeshSourceMultiPick();

            public MagicPart Copy()
            {
                var c = (MagicPart)MemberwiseClone();
                c.Pick = new MeshSourceMultiPick();
                foreach (var l in Pick.SelectedLabels) c.Pick.SelectedLabels.Add(l);
                return c;
            }
        }

        private static TextMeshParams DefaultMagicText()
        {
            var t = TextMeshParams.Default;
            t.Text           = "MAGIC CIRCLE";
            t.Size           = 0.12f;
            t.Thickness      = 0.02f;
            t.CurveMode      = TextCurveMode.DiskRim;
            t.CurveRadius    = 0.97f;
            t.CurveFlip      = true;
            t.CurveFitCircle = true;
            return t;
        }

        private string _mcName = "MagicCircle";
        private readonly List<MagicPart> _mcParts = DefaultMagicParts();
        private int _mcSel;

        /// <summary>一覧の各行の選択ボタン。選択中の強調に使う。</summary>
        [UiControl(Ignore = true)]
        private readonly List<Button> _mcRowButtons = new List<Button>();

        [UiControl("magicCircle.info", Safety = UiSafety.ReadOnly, Description = "魔法陣の部品の状態")]
        private Label _mcInfo;

        /// <summary>
        /// 初期の部品。外側の二重リングの間に 12 星座、その内側に六芒星、さらに内側へ 30° 回した小さい六芒星。
        /// </summary>
        private static List<MagicPart> DefaultMagicParts()
        {
            var ring = new MagicPart { Kind = MagicPartKind.Ring };
            ring.Ring.Count   = 2;
            ring.Ring.Radius  = 1f;
            ring.Ring.Spacing = 0.25f;

            var zodiac = new MagicPart { Kind = MagicPartKind.Zodiac };

            var outer = new MagicPart { Kind = MagicPartKind.Emblem };
            outer.Emblem.Kind      = EmblemKind.Hexagram;
            outer.Emblem.Style     = EmblemStyle.Line;
            outer.Emblem.Size      = 0.95f;
            outer.Emblem.LineWidth = 0.04f;
            outer.Emblem.Thickness = 0.02f;

            var inner = new MagicPart { Kind = MagicPartKind.Emblem };
            inner.Emblem.Kind      = EmblemKind.Hexagram;
            inner.Emblem.Style     = EmblemStyle.Line;
            inner.Emblem.Size      = 0.5f;
            inner.Emblem.Rotation  = 30f;
            inner.Emblem.LineWidth = 0.06f;
            inner.Emblem.Thickness = 0.02f;

            return new List<MagicPart> { ring, zodiac, outer, inner };
        }

        private MagicPart SelectedMagicPart
            => (_mcSel >= 0 && _mcSel < _mcParts.Count) ? _mcParts[_mcSel] : null;

        // ================================================================
        // UI
        // ================================================================

        private void BuildMagicCircleUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("MagicCircle")));
            c.Add(NF(() => _mcName, v => _mcName = v));
            c.Add(GearHint(T("MagicCircleHint")));

            // ── 部品の一覧 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("MagicCircleParts")));

            _mcRowButtons.Clear();
            if (_mcParts.Count == 0) c.Add(GearHint(T("MagicCircleNoPart")));
            // 部品の数だけ並ぶデータ行なので固定の ID を付けられない。
            // 行の入れ物として登録し、検査の対象から外す。
            for (int i = 0; i < _mcParts.Count; i++) c.Add(RowTarget.AddRows(MagicPartRow(i)));

            // 追加ボタン
            var add1 = new VisualElement(); add1.style.flexDirection = FlexDirection.Row; add1.style.marginTop = 2;
            SB(add1, T("MagicCircleKindRing"),   () => AddMagicPart(MagicPartKind.Ring));
            SB(add1, T("MagicCircleKindEmblem"), () => AddMagicPart(MagicPartKind.Emblem));
            SB(add1, T("MagicCircleKindText"),   () => AddMagicPart(MagicPartKind.Text));
            c.Add(add1);
            var add2 = new VisualElement(); add2.style.flexDirection = FlexDirection.Row; add2.style.marginBottom = 4;
            SB(add2, T("MagicCircleKindZodiac"),  () => AddMagicPart(MagicPartKind.Zodiac));
            SB(add2, T("MagicCircleKindObjects"), () => AddMagicPart(MagicPartKind.Objects));
            c.Add(add2);

            // ── 選んだ部品の設定 ──
            var part = SelectedMagicPart;
            if (part != null)
            {
                c.Add(PlayerIoUiKit.Divider());
                c.Add(PlayerIoUiKit.SectionLabel(
                    $"{T("MagicCirclePartSettings").Text}：{_mcSel + 1}. {T(MagicPartKindKeys[(int)part.Kind]).Text}"));

                switch (part.Kind)
                {
                    case MagicPartKind.Ring:    BuildMagicRingPartUI(c, part);    break;
                    case MagicPartKind.Emblem:  BuildMagicEmblemPartUI(c, part);  break;
                    case MagicPartKind.Text:    BuildMagicTextPartUI(c, part);    break;
                    case MagicPartKind.Zodiac:  BuildMagicZodiacPartUI(c, part);  break;
                    case MagicPartKind.Objects: BuildMagicObjectsPartUI(c, part); break;
                }
            }

            _mcInfo = GearHint(string.Empty);
            c.Add(_mcInfo);
            RefreshMagicCircleInfo();
        }

        /// <summary>一覧の 1 行（選択・上へ・下へ・複製・削除）。</summary>
        private VisualElement MagicPartRow(int index)
        {
            var part = _mcParts[index];

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 1;

            var sel = new Button(() => { _mcSel = index; RebuildSettings(); })
            {
                text = $"{index + 1}. {T(MagicPartKindKeys[(int)part.Kind]).Text}  {MagicPartSummary(part)}",
            };
            sel.style.flexGrow = 1;
            sel.style.height   = 20;
            sel.style.fontSize = 10;
            sel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _mcRowButtons.Add(sel);
            row.Add(sel);

            row.Add(SmallRowButton("↑", () => MoveMagicPart(index, -1)));
            row.Add(SmallRowButton("↓", () => MoveMagicPart(index, +1)));
            row.Add(SmallRowButton("⧉", () => DuplicateMagicPart(index)));
            row.Add(SmallRowButton("×", () => RemoveMagicPart(index)));
            return row;
        }

        private static Button SmallRowButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.width    = 22;
            b.style.height   = 20;
            b.style.fontSize = 10;
            b.style.marginLeft = 1;
            return b;
        }

        /// <summary>一覧に出す部品の要約。</summary>
        private static string MagicPartSummary(MagicPart p)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (p.Kind)
            {
                case MagicPartKind.Ring:
                    return string.Format(inv, "r={0:0.###} ×{1}", p.Ring.Radius, p.Ring.Count);
                case MagicPartKind.Emblem:
                    return string.Format(inv, "{0} {1:0.###} {2:0.#}°", p.Emblem.Kind, p.Emblem.Size, p.Emblem.Rotation);
                case MagicPartKind.Text:
                    return string.Format(inv, "r={0:0.###} \"{1}\"", p.Text.CurveRadius, p.Text.Text);
                case MagicPartKind.Zodiac:
                    return string.Format(inv, "r={0:0.###} {1}/12", p.Zodiac.Radius,
                        ZodiacCircleMeshGenerator.CountSelected(p.Zodiac.SignMask));
                case MagicPartKind.Objects:
                    return string.Format(inv, "r={0:0.###} ×{1}", p.Objects.Radius, p.Objects.Count);
            }
            return "";
        }

        /// <summary>選択中の行を強調する。暗色テーマが背景を塗った後で呼ぶ（RebuildSettings 末尾）。</summary>
        private void RefreshMagicCircleListColors()
        {
            for (int i = 0; i < _mcRowButtons.Count; i++)
            {
                _mcRowButtons[i].style.backgroundColor = i == _mcSel
                    ? new StyleColor(new Color(0.25f, 0.45f, 0.65f))
                    : new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            }
        }

        private void AddMagicPart(MagicPartKind kind)
        {
            _mcParts.Add(new MagicPart { Kind = kind });
            _mcSel = _mcParts.Count - 1;
            RebuildSettings();
            D();
        }

        private void MoveMagicPart(int index, int delta)
        {
            int j = index + delta;
            if (index < 0 || index >= _mcParts.Count || j < 0 || j >= _mcParts.Count) return;
            (_mcParts[index], _mcParts[j]) = (_mcParts[j], _mcParts[index]);
            _mcSel = j;
            RebuildSettings();
            D();
        }

        private void DuplicateMagicPart(int index)
        {
            if (index < 0 || index >= _mcParts.Count) return;
            _mcParts.Insert(index + 1, _mcParts[index].Copy());
            _mcSel = index + 1;
            RebuildSettings();
            D();
        }

        private void RemoveMagicPart(int index)
        {
            if (index < 0 || index >= _mcParts.Count) return;
            _mcParts.RemoveAt(index);
            if (_mcSel >= _mcParts.Count) _mcSel = _mcParts.Count - 1;
            RebuildSettings();
            D();
        }

        /// <summary>値の変更。一覧の要約と情報欄も書き直す。</summary>
        private void DMC()
        {
            D();
            if (_mcSel >= 0 && _mcSel < _mcRowButtons.Count && _mcSel < _mcParts.Count)
            {
                var p = _mcParts[_mcSel];
                _mcRowButtons[_mcSel].text =
                    $"{_mcSel + 1}. {T(MagicPartKindKeys[(int)p.Kind]).Text}  {MagicPartSummary(p)}";
            }
            RefreshMagicCircleInfo();
        }

        // ── リング ──────────────────────────────────────────────────

        private void BuildMagicRingPartUI(VisualElement c, MagicPart p)
        {
            c.Add(SR(T("MagicRingRadius"), MagicRingMeshGenerator.MagicRingParams.RadiusMin, 5f,
                () => p.Ring.Radius, v => { p.Ring.Radius = v; DMC(); }));
            c.Add(SR(T("MagicRingWidth"), MagicRingMeshGenerator.MagicRingParams.WidthMin, 0.5f,
                () => p.Ring.Width, v => { p.Ring.Width = v; DMC(); }));
            c.Add(IR(T("MagicRingCount"), MagicRingMeshGenerator.MagicRingParams.CountMin, 16,
                () => p.Ring.Count, v => { p.Ring.Count = v; DMC(); }));
            c.Add(SR(T("MagicRingSpacing"), -1f, 1f,
                () => p.Ring.Spacing, v => { p.Ring.Spacing = v; DMC(); }));
            c.Add(IR(T("Segments"), MagicRingMeshGenerator.MagicRingParams.SegmentsMin, 256,
                () => p.Ring.Segments, v => { p.Ring.Segments = v; DMC(); }));
            c.Add(SR(T("Thickness"), MagicRingMeshGenerator.MagicRingParams.ThicknessMin, 0.2f,
                () => p.Ring.Thickness, v => { p.Ring.Thickness = v; DMC(); }));
        }

        // ── 紋章 ────────────────────────────────────────────────────

        private void BuildMagicEmblemPartUI(VisualElement c, MagicPart p)
        {
            var kinds = new List<string>(EmblemKindKeys.Length);
            foreach (var k in EmblemKindKeys) kinds.Add(T(k));
            c.Add(DD(T("EmblemKind"), kinds,
                () => (int)p.Emblem.Kind,
                i => { p.Emblem.Kind = (EmblemKind)i; RebuildSettings(); DMC(); }));

            var styles = new List<string>(EmblemStyleKeys.Length);
            foreach (var k in EmblemStyleKeys) styles.Add(T(k));
            c.Add(DD(T("EmblemStyle"), styles,
                () => (int)p.Emblem.Style,
                i => { p.Emblem.Style = (EmblemStyle)i; RebuildSettings(); DMC(); }));

            c.Add(SR(T("EmblemSize"), EmblemMeshGenerator.EmblemParams.SizeMin, 5f,
                () => p.Emblem.Size, v => { p.Emblem.Size = v; DMC(); }));
            c.Add(SR(T("EmblemRotation"), EmblemMeshGenerator.EmblemParams.RotationMin,
                EmblemMeshGenerator.EmblemParams.RotationMax,
                () => p.Emblem.Rotation, v => { p.Emblem.Rotation = v; DMC(); }));

            if (p.Emblem.Style == EmblemStyle.Line)
                c.Add(SR(T("EmblemLineWidth"), EmblemMeshGenerator.EmblemParams.LineWidthMin,
                    EmblemMeshGenerator.EmblemParams.LineWidthMax,
                    () => p.Emblem.LineWidth, v => { p.Emblem.LineWidth = v; DMC(); }));

            if (p.Emblem.Kind == EmblemKind.Cross)
                c.Add(SR(T("EmblemCrossArmWidth"), EmblemMeshGenerator.EmblemParams.CrossArmWidthMin,
                    EmblemMeshGenerator.EmblemParams.CrossArmWidthMax,
                    () => p.Emblem.CrossArmWidth, v => { p.Emblem.CrossArmWidth = v; DMC(); }));

            c.Add(SR(T("EmblemCornerRadius"), EmblemMeshGenerator.EmblemParams.CornerRadiusMin,
                EmblemMeshGenerator.EmblemParams.CornerRadiusMax,
                () => p.Emblem.CornerRadius, v => { p.Emblem.CornerRadius = v; DMC(); }));
            c.Add(IR(T("Segments"), EmblemMeshGenerator.EmblemParams.SegmentsMin,
                EmblemMeshGenerator.EmblemParams.SegmentsMax,
                () => p.Emblem.Segments, v => { p.Emblem.Segments = v; DMC(); }));

            c.Add(TR(T("ZodiacRing"),
                () => p.Emblem.Ring, v => { p.Emblem.Ring = v; RebuildSettings(); DMC(); }));
            if (p.Emblem.Ring)
            {
                c.Add(SR(T("ZodiacRingRadius"), EmblemMeshGenerator.EmblemParams.RingRadiusMin,
                    EmblemMeshGenerator.EmblemParams.RingRadiusMax,
                    () => p.Emblem.RingRadius, v => { p.Emblem.RingRadius = v; DMC(); }));
                c.Add(SR(T("ZodiacRingWidth"), EmblemMeshGenerator.EmblemParams.RingWidthMin,
                    EmblemMeshGenerator.EmblemParams.RingWidthMax,
                    () => p.Emblem.RingWidth, v => { p.Emblem.RingWidth = v; DMC(); }));
            }

            c.Add(SR(T("Thickness"), EmblemMeshGenerator.EmblemParams.ThicknessMin, 0.5f,
                () => p.Emblem.Thickness, v => { p.Emblem.Thickness = v; DMC(); }));
        }

        // ── 円形の文字 ──────────────────────────────────────────────

        private void BuildMagicTextPartUI(VisualElement c, MagicPart p)
        {
            var fonts = PlyFontLibrary.LoadList();
            var names = new List<string>(fonts.Count);
            int cur = 0;
            for (int i = 0; i < fonts.Count; i++)
            {
                names.Add(fonts[i].FamilyName);
                if (fonts[i].FamilyName == p.Text.FontFamily) cur = i;
            }
            if (names.Count == 0)
            {
                names.Add(T("TextNoFont"));
                p.Text.FontFamily = "";
            }
            else if (string.IsNullOrEmpty(p.Text.FontFamily) || names.IndexOf(p.Text.FontFamily) < 0)
            {
                p.Text.FontFamily = names[0];
            }

            c.Add(DD(T("TextFont"), names,
                () => cur,
                i =>
                {
                    p.Text.FontFamily = (i >= 0 && i < fonts.Count) ? fonts[i].FamilyName : "";
                    DMC();
                }));

            var edit = new TextField { value = p.Text.Text ?? "", multiline = true };
            edit.style.height = 40;
            edit.style.whiteSpace = WhiteSpace.Normal;
            edit.style.marginBottom = 2;
            edit.RegisterValueChangedCallback(e => { p.Text.Text = e.newValue; DMC(); });
            c.Add(edit);

            c.Add(SR(T("Size"), TextMeshParams.SizeMin, 1f,
                () => p.Text.Size, v => { p.Text.Size = v; DMC(); }));
            c.Add(SR(T("Radius"), TextMeshParams.CurveRadiusMin, 5f,
                () => p.Text.CurveRadius, v => { p.Text.CurveRadius = v; DMC(); }));
            c.Add(TR(T("TextCurveFlip"),
                () => p.Text.CurveFlip, v => { p.Text.CurveFlip = v; DMC(); }));
            c.Add(SR(T("TextCurveStartAngle"), TextMeshParams.CurveStartAngleMin, TextMeshParams.CurveStartAngleMax,
                () => p.Text.CurveStartAngle, v => { p.Text.CurveStartAngle = v; DMC(); }));
            c.Add(TR(T("TextCurveFitCircle"),
                () => p.Text.CurveFitCircle, v => { p.Text.CurveFitCircle = v; DMC(); }));
            c.Add(SR(T("TextLetterSpacing"), TextMeshParams.LetterSpacingMin, TextMeshParams.LetterSpacingMax,
                () => p.Text.LetterSpacing, v => { p.Text.LetterSpacing = v; DMC(); }));
            c.Add(IR(T("Segments"), TextMeshParams.SegmentMin, TextMeshParams.SegmentMax,
                () => p.Text.Segment, v => { p.Text.Segment = v; DMC(); }));
            c.Add(SR(T("Thickness"), TextMeshParams.ThicknessMin, TextMeshParams.ThicknessMax,
                () => p.Text.Thickness, v => { p.Text.Thickness = v; DMC(); }));
        }

        // ── 星座 ────────────────────────────────────────────────────

        private void BuildMagicZodiacPartUI(VisualElement c, MagicPart p)
        {
            c.Add(PlayerIoUiKit.SectionLabel(T("ZodiacCircleSigns")));

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap      = Wrap.Wrap;
            for (int i = 0; i < 12; i++)
            {
                int bit = 1 << i;
                var tog = TR(T(ZodiacSignKeys[i]),
                    () => (p.Zodiac.SignMask & bit) != 0,
                    v =>
                    {
                        if (v) p.Zodiac.SignMask |= bit;
                        else   p.Zodiac.SignMask &= ~bit;
                        DMC();
                    });
                tog.style.width = new StyleLength(new Length(33.3f, LengthUnit.Percent));
                grid.Add(tog);
            }
            c.Add(grid);

            var allRow = new VisualElement(); allRow.style.flexDirection = FlexDirection.Row; allRow.style.marginBottom = 4;
            SB(allRow, T("ZodiacCircleAll"),  () => { p.Zodiac.SignMask = ZodiacCircleMeshGenerator.AllSigns; RebuildSettings(); DMC(); });
            SB(allRow, T("ZodiacCircleNone"), () => { p.Zodiac.SignMask = 0; RebuildSettings(); DMC(); });
            c.Add(allRow);

            c.Add(DD(T("ZodiacCircleSpacing"),
                new List<string> { T("ZodiacCircleSlots"), T("ZodiacCircleEven") },
                () => (int)p.Zodiac.Spacing,
                i => { p.Zodiac.Spacing = (ZodiacCircleSpacing)i; DMC(); }));

            c.Add(SR(T("Radius"), ZodiacCircleMeshGenerator.ZodiacCircleParams.RadiusMin, 5f,
                () => p.Zodiac.Radius, v => { p.Zodiac.Radius = v; DMC(); }));
            BuildRadialAngleRows(c,
                () => p.Zodiac.StartAngle,       v => p.Zodiac.StartAngle = v,
                () => p.Zodiac.CounterClockwise, v => p.Zodiac.CounterClockwise = v,
                () => p.Zodiac.Facing,           v => p.Zodiac.Facing = v);

            c.Add(SR(T("Size"), ZodiacSignMeshGenerator.ZodiacSignParams.SizeMin, 1f,
                () => p.Zodiac.Size, v => { p.Zodiac.Size = v; DMC(); }));
            c.Add(SR(T("ZodiacStrokeWidth"), ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMin,
                ZodiacSignMeshGenerator.ZodiacSignParams.StrokeWidthMax,
                () => p.Zodiac.StrokeWidth, v => { p.Zodiac.StrokeWidth = v; DMC(); }));
            c.Add(TR(T("ZodiacRoundCaps"),
                () => p.Zodiac.RoundCaps, v => { p.Zodiac.RoundCaps = v; DMC(); }));
            c.Add(IR(T("Segments"), ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMin,
                ZodiacSignMeshGenerator.ZodiacSignParams.SegmentsMax,
                () => p.Zodiac.Segments, v => { p.Zodiac.Segments = v; DMC(); }));
            c.Add(SR(T("Thickness"), ZodiacSignMeshGenerator.ZodiacSignParams.ThicknessMin, 0.5f,
                () => p.Zodiac.Thickness, v => { p.Zodiac.Thickness = v; DMC(); }));

            c.Add(TR(T("ZodiacRing"),
                () => p.Zodiac.SignRing, v => { p.Zodiac.SignRing = v; RebuildSettings(); DMC(); }));
            if (p.Zodiac.SignRing)
            {
                c.Add(SR(T("ZodiacRingRadius"), ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMin,
                    ZodiacSignMeshGenerator.ZodiacSignParams.RingRadiusMax,
                    () => p.Zodiac.SignRingRadius, v => { p.Zodiac.SignRingRadius = v; DMC(); }));
                c.Add(SR(T("ZodiacRingWidth"), ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMin,
                    ZodiacSignMeshGenerator.ZodiacSignParams.RingWidthMax,
                    () => p.Zodiac.SignRingWidth, v => { p.Zodiac.SignRingWidth = v; DMC(); }));
            }
        }

        // ── オブジェクト ────────────────────────────────────────────

        private void BuildMagicObjectsPartUI(VisualElement c, MagicPart p)
        {
            BuildMeshSourceMultiRow(c, p.Pick, T("PlaceSource"));

            c.Add(TR(T("PlaceIncludeChildren"),
                () => p.Objects.IncludeChildren, v => { p.Objects.IncludeChildren = v; DMC(); }));
            c.Add(DD(T("PlaceMode"),
                new List<string> { T("BoltModeCombine"), T("BoltModeSequence"), T("BoltModeRandom") },
                () => (int)p.Objects.Mode,
                i => { p.Objects.Mode = (PlaceSourceMode)i; DMC(); }));
            c.Add(IR(T("PlaceSeed"), 0, 9999,
                () => p.Objects.RandomSeed, v => { p.Objects.RandomSeed = v; DMC(); }));

            c.Add(PlayerIoUiKit.Divider());
            c.Add(IR(T("RadialCount"), RadialPlaceMeshGenerator.RadialPlaceParams.CountMin, 64,
                () => p.Objects.Count, v => { p.Objects.Count = v; DMC(); }));
            c.Add(SR(T("Radius"), RadialPlaceMeshGenerator.RadialPlaceParams.RadiusMin, 5f,
                () => p.Objects.Radius, v => { p.Objects.Radius = v; DMC(); }));
            BuildRadialAngleRows(c,
                () => p.Objects.StartAngle,       v => p.Objects.StartAngle = v,
                () => p.Objects.CounterClockwise, v => p.Objects.CounterClockwise = v,
                () => p.Objects.Facing,           v => p.Objects.Facing = v);
            c.Add(SR(T("Scale"), RadialPlaceMeshGenerator.RadialPlaceParams.ScaleMin, 3f,
                () => p.Objects.Scale, v => { p.Objects.Scale = v; DMC(); }));
            c.Add(TR(T("RadialCenterSources"),
                () => p.Objects.CenterSources, v => { p.Objects.CenterSources = v; DMC(); }));
        }

        /// <summary>円周配置の共通行（開始角・反時計回り・向き）。</summary>
        private void BuildRadialAngleRows(VisualElement c,
            System.Func<float> getAngle, System.Action<float> setAngle,
            System.Func<bool> getCcw, System.Action<bool> setCcw,
            System.Func<RadialFacing> getFacing, System.Action<RadialFacing> setFacing)
        {
            c.Add(SR(T("RadialStartAngle"), RadialLayout.AngleMin, RadialLayout.AngleMax,
                getAngle, v => { setAngle(v); DMC(); }));
            c.Add(TR(T("RadialCounterClockwise"), getCcw, v => { setCcw(v); DMC(); }));
            c.Add(DD(T("RadialFacing"),
                new List<string> { T("RadialFacingOutward"), T("RadialFacingInward"), T("RadialFacingUpright") },
                () => (int)getFacing(),
                i => { setFacing((RadialFacing)i); DMC(); }));
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 部品 1 つを図形生成コマンドにする。配置は親の原点・単位の姿勢（MagicCircleElementCodec.ChildPlacement）。
        /// 部品の名前は「親の名前_種類番号」。
        /// </summary>
        private CreatePrimitiveMeshCommand BuildMagicPartCommand(MagicPart p, int index)
        {
            int mi = ModelIndex();
            var pl = MagicCircleElementCodec.ChildPlacement(EffectiveMaterialIndex, _mergeDuplicateVertices);
            string baseName = string.IsNullOrEmpty(_mcName) ? "MagicCircle" : _mcName;
            string name = $"{baseName}_{MagicPartNameTags[(int)p.Kind]}{index + 1}";

            switch (p.Kind)
            {
                case MagicPartKind.Ring:
                {
                    var prm = p.Ring; prm.MeshName = name;
                    return new CreateMagicRingCommand(mi, prm, pl);
                }
                case MagicPartKind.Emblem:
                {
                    var prm = p.Emblem; prm.MeshName = name;
                    prm.Orientation = PlaneOrientation.XY;
                    prm.Pivot       = Vector3.zero;
                    return new CreateEmblemCommand(mi, prm, pl);
                }
                case MagicPartKind.Text:
                {
                    var prm = p.Text; prm.MeshName = name;
                    prm.CurveMode = TextCurveMode.DiskRim;
                    return new CreateTextMeshCommand(mi, prm, pl);
                }
                case MagicPartKind.Zodiac:
                {
                    var prm = p.Zodiac; prm.MeshName = name;
                    return new CreateZodiacCircleCommand(mi, prm, pl);
                }
                case MagicPartKind.Objects:
                {
                    var prm = p.Objects; prm.MeshName = name;
                    return new CreateRadialPlaceCommand(mi, prm, p.Pick.SelectedMasterIndices().ToArray(), pl);
                }
            }
            return null;
        }

        /// <summary>全部品が作れる状態か。作れない部品があれば why にその理由。</summary>
        private bool MagicCircleReady(out string why)
        {
            why = null;
            if (_mcParts.Count == 0) { why = T("MagicCircleNoPart"); return false; }

            for (int i = 0; i < _mcParts.Count; i++)
            {
                var p = _mcParts[i];
                string reason = null;
                switch (p.Kind)
                {
                    case MagicPartKind.Text:
                        if (string.IsNullOrWhiteSpace(p.Text.Text) || PlyFontLibrary.Open(p.Text.FontFamily) == null)
                            reason = T("MagicCircleNeedFont");
                        break;
                    case MagicPartKind.Zodiac:
                        if ((p.Zodiac.SignMask & ZodiacCircleMeshGenerator.AllSigns) == 0)
                            reason = T("MagicCircleNeedSign");
                        break;
                    case MagicPartKind.Objects:
                        if (p.Pick.SelectedMasterIndices().Count == 0)
                            reason = T("MagicCircleNeedSource");
                        break;
                }
                if (reason != null)
                {
                    why = T("MagicCircleNotReady", i + 1, reason);
                    return false;
                }
            }
            return true;
        }

        private void RefreshMagicCircleInfo()
        {
            if (_mcInfo == null) return;
            _mcInfo.text = MagicCircleReady(out string why) ? "" : why;
        }

        /// <summary>プレビュー。部品を 1 つへ連結し、共通の回転・拡大を掛ける。</summary>
        private MeshObject GenerateMagicCirclePreview(bool forPreview, bool applyTransform)
        {
            MeshObject result = null;
            for (int i = 0; i < _mcParts.Count; i++)
            {
                var cmd = BuildMagicPartCommand(_mcParts[i], i);
                if (cmd == null) continue;

                var one = PrimitiveMeshFactory.Build(cmd, forPreview, ResolvePlaceSources, ResolveBeltSource);
                if (one == null || one.VertexCount == 0) continue;

                if (result == null) result = one;
                else Poly_Ling.Ops.MeshObjectAppendOps.Append(result, one, copyNormals: true);
            }

            if (result != null && applyTransform)
                PrimitiveMeshTransform.ApplyRotationScale(result, _rotEuler, _scale);
            return result;
        }

        /// <summary>生成ボタン。部品を文字列へ直して CreateMagicCircleCommand を送る。</summary>
        private void InvokeMagicCircleGenerate()
        {
            if (SendCommand == null) { _statusLabel.text = "配線が足りません（SendCommand）"; return; }
            if (!MagicCircleReady(out string why)) { _statusLabel.text = why; return; }

            var elements = new string[_mcParts.Count];
            for (int i = 0; i < _mcParts.Count; i++)
                elements[i] = MagicCircleElementCodec.Encode(BuildMagicPartCommand(_mcParts[i], i));

            SendCommand(new CreateMagicCircleCommand(
                ModelIndex(), _mcName, _worldPos, _rotEuler, _scale.x, elements));

            _statusLabel.text = T("MagicCircleSent", _mcParts.Count);
            RefreshMeshNameCandidate();
        }
    }
}
