// PlayerPrimitiveMeshSubPanel.Text.cs
// 図形生成サブパネル：文字列（高度な図形）。
// 外部アプリが書き出した .plgly を読み、2D ループへ変換して
// Profile2DExtrudeMeshGenerator で三角化・厚み付けする。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.GlyphText;
using Poly_Ling.Profile2DExtrude;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private TextMeshParams _textP = TextMeshParams.Default;

        private List<PlyFontLibrary.Entry> _textFonts = new List<PlyFontLibrary.Entry>();
        private DropdownField _textFontDrop;
        private Label _textInfoLabel;
        private SolidifyUI _textSolidUI;

        /// <summary>直近の生成でフォントに存在せず飛ばした文字数。</summary>
        private int _textMissing;

        // ================================================================
        // UI
        // ================================================================

        private void BuildTextUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Text")));
            c.Add(NF(() => _textP.MeshName, v => _textP.MeshName = v));

            // ── フォント ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TextFont")));

            RefreshTextFontEntries();

            _textFontDrop = new DropdownField(TextFontChoices(), TextFontIndex());
            _textFontDrop.RegisterValueChangedCallback(_ =>
            {
                int idx = _textFontDrop.index;
                _textP.FontFamily = (idx >= 0 && idx < _textFonts.Count)
                    ? _textFonts[idx].FamilyName
                    : "";
                D();
                RefreshTextInfo();
            });
            c.Add(_textFontDrop);

            c.Add(PlayerIoUiKit.WideBtn(T("TextFontReload"), () =>
            {
                PlyFontLibrary.Clear();
                RefreshTextFontEntries();
                if (_textFontDrop != null)
                {
                    _textFontDrop.choices = TextFontChoices();
                    _textFontDrop.index = TextFontIndex();
                }
                D();
                RefreshTextInfo();
            }));

            var dirHint = new Label(T("TextFontDir") + "\n" + PlyFontLibrary.RootDir);
            dirHint.style.fontSize = 10;
            dirHint.style.whiteSpace = WhiteSpace.Normal;
            dirHint.style.marginBottom = 2;
            c.Add(dirHint);

            _textInfoLabel = new Label(TextInfoText());
            _textInfoLabel.style.fontSize = 10;
            _textInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _textInfoLabel.style.marginBottom = 2;
            c.Add(_textInfoLabel);

            // ── 文字列 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("TextString")));

            var edit = new TextField { value = _textP.Text ?? "", multiline = true };
            edit.style.height = 60;
            edit.style.whiteSpace = WhiteSpace.Normal;
            edit.style.marginBottom = 2;
            edit.RegisterValueChangedCallback(e => { _textP.Text = e.newValue; D(); });
            c.Add(edit);

            var editHint = new Label(T("TextStringHint"));
            editHint.style.fontSize = 10;
            editHint.style.whiteSpace = WhiteSpace.Normal;
            editHint.style.marginBottom = 2;
            c.Add(editHint);

            // ── 形状 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(IR(T("Segments"), 1, 20, () => _textP.Segment, v => { _textP.Segment = v; D(); }));
            c.Add(SR(T("Size"), 0.01f, 10f, () => _textP.Size, v => { _textP.Size = v; D(); }));
            c.Add(SR(T("TextLetterSpacing"), -0.5f, 1f, () => _textP.LetterSpacing,
                v => { _textP.LetterSpacing = v; D(); }));
            c.Add(SR(T("TextLineSpacing"), 0.5f, 3f, () => _textP.LineSpacing,
                v => { _textP.LineSpacing = v; D(); }));

            // ── 厚み付け ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), 0f, 0.5f, () => _textP.Thickness,
                v => { _textP.Thickness = v; D(); RefreshTextSolidVis(); }));

            var textSolid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), 0, 16, () => _textP.SegmentsFront,
                               v => { _textP.SegmentsFront = v; D(); RefreshTextSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), 0.001f, 0.25f, () => _textP.EdgeSizeFront,
                               v => { _textP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), 0, 16, () => _textP.SegmentsBack,
                               v => { _textP.SegmentsBack = v; D(); RefreshTextSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), 0.001f, 0.25f, () => _textP.EdgeSizeBack,
                               v => { _textP.EdgeSizeBack = v; D(); }),
                Inward    = TR(T("EdgeInward"), () => _textP.EdgeInward,
                               v => { _textP.EdgeInward = v; D(); }),
            };
            _textSolidUI = textSolid;
            c.Add(textSolid.EdgeLabel); c.Add(textSolid.FrontSeg); c.Add(textSolid.FrontSize);
            c.Add(textSolid.BackSeg);   c.Add(textSolid.BackSize); c.Add(textSolid.Inward);
            RefreshTextSolidVis();
        }

        // ================================================================
        // フォント一覧
        // ================================================================

        /// <summary>fonts.txt を読み直し、選択中ファミリが無ければ先頭へ寄せる。</summary>
        private void RefreshTextFontEntries()
        {
            _textFonts = PlyFontLibrary.LoadList();

            if (_textFonts.Count == 0)
            {
                _textP.FontFamily = "";
                return;
            }

            for (int i = 0; i < _textFonts.Count; i++)
            {
                if (_textFonts[i].FamilyName == _textP.FontFamily)
                    return;
            }
            _textP.FontFamily = _textFonts[0].FamilyName;
        }

        private List<string> TextFontChoices()
        {
            var list = new List<string>();
            if (_textFonts.Count == 0)
            {
                list.Add(T("TextNoFont"));
                return list;
            }
            for (int i = 0; i < _textFonts.Count; i++)
                list.Add(_textFonts[i].FamilyName);
            return list;
        }

        private int TextFontIndex()
        {
            if (_textFonts.Count == 0) return 0;
            for (int i = 0; i < _textFonts.Count; i++)
            {
                if (_textFonts[i].FamilyName == _textP.FontFamily)
                    return i;
            }
            return 0;
        }

        private string TextInfoText()
        {
            var font = PlyFontLibrary.Open(_textP.FontFamily);
            if (font == null) return T("TextNoFont");

            string s = $"em {font.UnitsPerEm:0}  asc {font.Ascent:0}  desc {font.Descent:0}  "
                     + $"{T("TextGlyphs")} {font.GlyphCount}";
            if (_textMissing > 0)
                s += $"\n{T("TextMissing")} {_textMissing}";
            return s;
        }

        private void RefreshTextInfo()
        {
            RefreshCreateButtonState();
            if (_textInfoLabel != null) _textInfoLabel.text = TextInfoText();
        }

        private void RefreshTextSolidVis()
            => UpdateSolidifyVis(_textSolidUI, _textP.Thickness, _textP.SegmentsFront, _textP.SegmentsBack);

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// .plgly のグリフから 2D ループを作り、平面／厚み付けまで一括生成する。
        /// フォント未選択・文字列が空・全文字が未収録なら null。
        /// </summary>
        private MeshObject GenerateTextMesh()
        {
            var font = PlyFontLibrary.Open(_textP.FontFamily);
            if (font == null)
            {
                _textMissing = 0;
                RefreshTextInfo();
                return null;
            }

            var loops = TextOutlineBuilder.Build(font, _textP.Text ?? "",
                new TextLayoutParams
                {
                    Segment       = _textP.Segment,
                    LetterSpacing = _textP.LetterSpacing,
                    LineSpacing   = _textP.LineSpacing,
                },
                out int missing);

            _textMissing = missing;
            RefreshTextInfo();

            if (loops.Count == 0) return null;

            return Profile2DExtrudeMeshGenerator.Generate(loops, _textP.MeshName,
                new Profile2DGenerateParams
                {
                    Scale         = _textP.Size,
                    Offset        = Vector2.zero,
                    FlipY         = false,
                    Thickness     = _textP.Thickness,
                    SegmentsFront = _textP.SegmentsFront,
                    SegmentsBack  = _textP.SegmentsBack,
                    EdgeSizeFront = _textP.EdgeSizeFront,
                    EdgeSizeBack  = _textP.EdgeSizeBack,
                    EdgeInward    = _textP.EdgeInward,
                    SymmetryMode  = false,
                });
        }
    }
}
