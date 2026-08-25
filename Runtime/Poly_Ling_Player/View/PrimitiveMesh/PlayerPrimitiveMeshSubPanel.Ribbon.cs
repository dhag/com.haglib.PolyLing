// PlayerPrimitiveMeshSubPanel.Ribbon.cs
// 図形生成サブパネル：リボン（高度な図形）。
// 蝶結びを梯子（四角形の帯）の集まりとして生成する。厚みや断面は付けない。
// 生成後、フリル／パイプの「自動検索」で基準ベルトとして拾わせて使う。
// 部品はループ / テール / ノットの3種。多重リボンは部品を選んで複数回に分けて
// 生成し、別のツールで結合して作る。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ribbon;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private RibbonBowParams _ribbonP = RibbonBowParams.Default;

        // ================================================================
        // UI
        // ================================================================

        private void BuildRibbonUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("Ribbon")));
            c.Add(NF(() => _ribbonP.MeshName, v => _ribbonP.MeshName = v));

            c.Add(RibbonHint(T("RibbonHint")));

            c.Add(SR(T("RibbonWidth"), 0.01f, 2f,
                () => _ribbonP.RibbonWidth, v => { _ribbonP.RibbonWidth = v; D(); }));

            // ── 部品の取捨 ──
            // 多重リボンはここで部品を切り替えて複数回生成し、あとで結合して作る。
            c.Add(SL(T("RibbonParts")));
            c.Add(RibbonHint(T("RibbonPartsHint")));
            c.Add(TR(T("RibbonBuildLoops"),
                () => _ribbonP.BuildLoops, v => { _ribbonP.BuildLoops = v; D(); RefreshCreateButtonState(); }));
            c.Add(TR(T("RibbonBuildTails"),
                () => _ribbonP.BuildTails, v => { _ribbonP.BuildTails = v; D(); RefreshCreateButtonState(); }));
            c.Add(TR(T("RibbonBuildKnot"),
                () => _ribbonP.BuildKnot,  v => { _ribbonP.BuildKnot  = v; D(); RefreshCreateButtonState(); }));

            // ── ループ ──
            c.Add(SL(T("RibbonLoop")));
            c.Add(TR(T("RibbonLoopFlip"),
                () => _ribbonP.Loop.Topology == RibbonLoopTopology.Flip,
                v =>
                {
                    _ribbonP.Loop.Topology = v ? RibbonLoopTopology.Flip : RibbonLoopTopology.Flat;
                    D();
                }));
            c.Add(RibbonHint(T("RibbonLoopFlipHint")));
            c.Add(SR(T("RibbonLoopWidth"), 0.05f, 5f,
                () => _ribbonP.Loop.Width, v => { _ribbonP.Loop.Width = v; D(); }));
            c.Add(SR(T("RibbonLoopHeight"), 0f, 3f,
                () => _ribbonP.Loop.Height, v => { _ribbonP.Loop.Height = v; D(); }));
            c.Add(SR(T("RibbonLoopSag"), 0f, 1f,
                () => _ribbonP.Loop.Sag, v => { _ribbonP.Loop.Sag = v; D(); }));
            c.Add(SR(T("RibbonLoopTilt"), -90f, 90f,
                () => _ribbonP.Loop.Tilt, v => { _ribbonP.Loop.Tilt = v; D(); }));
            c.Add(RibbonHint(T("RibbonLoopTiltHint")));
            c.Add(SR(T("RibbonLoopDepth"), 0f, 1f,
                () => _ribbonP.Loop.Depth, v => { _ribbonP.Loop.Depth = v; D(); }));
            c.Add(RibbonHint(T("RibbonLoopDepthHint")));
            c.Add(SR(T("RibbonRootGap"), 0f, 1f,
                () => _ribbonP.Loop.RootGap, v => { _ribbonP.Loop.RootGap = v; D(); }));
            c.Add(SR(T("RibbonRootPinch"), 0.05f, 1f,
                () => _ribbonP.Loop.RootPinch, v => { _ribbonP.Loop.RootPinch = v; D(); }));

            // ── テール ──
            c.Add(SL(T("RibbonTail")));
            c.Add(SR(T("RibbonTailLength"), 0.05f, 5f,
                () => _ribbonP.Tail.Length, v => { _ribbonP.Tail.Length = v; D(); }));
            c.Add(SR(T("RibbonTailSpread"), -1f, 1f,
                () => _ribbonP.Tail.Spread, v => { _ribbonP.Tail.Spread = v; D(); }));
            c.Add(SR(T("RibbonTailClose"), 0f, 1f,
                () => _ribbonP.Tail.Close, v => { _ribbonP.Tail.Close = v; D(); }));
            c.Add(SR(T("RibbonTailCloseAt"), 0.05f, 0.95f,
                () => _ribbonP.Tail.CloseAt, v => { _ribbonP.Tail.CloseAt = v; D(); }));
            c.Add(RibbonHint(T("RibbonTailCloseHint")));
            c.Add(SR(T("RibbonTailSag"), 0f, 1f,
                () => _ribbonP.Tail.Sag, v => { _ribbonP.Tail.Sag = v; D(); }));
            c.Add(SR(T("RibbonTailDepth"), 0f, 1f,
                () => _ribbonP.Tail.Depth, v => { _ribbonP.Tail.Depth = v; D(); }));
            c.Add(SR(T("RibbonTailTaper"), 0.05f, 2f,
                () => _ribbonP.Tail.Taper, v => { _ribbonP.Tail.Taper = v; D(); }));

            // ── ノット ──
            c.Add(SL(T("RibbonKnot")));
            c.Add(RibbonHint(T("RibbonKnotHint")));
            c.Add(SR(T("RibbonKnotWidth"), 0.01f, 2f,
                () => _ribbonP.Knot.Width, v => { _ribbonP.Knot.Width = v; D(); }));
            c.Add(SR(T("RibbonKnotHeight"), 0.01f, 2f,
                () => _ribbonP.Knot.Height, v => { _ribbonP.Knot.Height = v; D(); }));
            c.Add(SR(T("RibbonKnotDepth"), 0f, 1f,
                () => _ribbonP.Knot.Depth, v => { _ribbonP.Knot.Depth = v; D(); }));

            // ── 分割数 ──
            c.Add(SL(T("Segments")));
            c.Add(IR(T("RibbonLoopSegs"), 2, 64,
                () => _ribbonP.LoopSegments, v => { _ribbonP.LoopSegments = v; D(); }));
            c.Add(IR(T("RibbonTailSegs"), 1, 64,
                () => _ribbonP.TailSegments, v => { _ribbonP.TailSegments = v; D(); }));
            c.Add(IR(T("RibbonKnotSegs"), 1, 32,
                () => _ribbonP.KnotSegments, v => { _ribbonP.KnotSegments = v; D(); }));

            // ── 梯子タグ ──
            c.Add(SL(T("RibbonTags")));
            c.Add(RibbonHint(T("RibbonTagsHint")));
            c.Add(TR(T("RibbonStartTag"),
                () => _ribbonP.AddStartTag, v => { _ribbonP.AddStartTag = v; D(); }));
            c.Add(TR(T("RibbonStartTip"),
                () => _ribbonP.AddStartTip, v => { _ribbonP.AddStartTip = v; D(); }));
            c.Add(TR(T("RibbonEndTip"),
                () => _ribbonP.AddEndTip, v => { _ribbonP.AddEndTip = v; D(); }));
            c.Add(SR(T("RibbonTipLen"), 0.05f, 2f,
                () => _ribbonP.TipLengthScale, v => { _ribbonP.TipLengthScale = v; D(); }));
            c.Add(SR(T("RibbonTagSize"), 0.05f, 2f,
                () => _ribbonP.TagSizeScale, v => { _ribbonP.TagSizeScale = v; D(); }));

            // ── 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FlipFaces"),
                () => _ribbonP.FlipFaces, v => { _ribbonP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _ribbonP.Pivot, v => { _ribbonP.Pivot = v; D(); },
                -0.5f, 0.5f,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>説明文の小さいラベル。</summary>
        private static Label RibbonHint(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            return l;
        }

        // ================================================================
        // 生成
        // ================================================================

        private MeshObject GenerateRibbonMesh()
            => RibbonBowMeshGenerator.Generate(_ribbonP);
    }
}
