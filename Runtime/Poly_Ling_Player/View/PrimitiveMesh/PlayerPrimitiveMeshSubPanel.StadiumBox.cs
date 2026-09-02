// PlayerPrimitiveMeshSubPanel.StadiumBox.cs
// 図形生成サブパネル：小判型（両側面が半円筒の直方体）。
// 「上下も丸める」を入れると上下も半円筒になり、四隅は 1/4 球でつながる。
// 「上下も丸める」が OFF のときだけ、上下のフタの有無を指定できる。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private static StadiumBoxMeshGenerator.StadiumBoxParams DefaultStadiumBoxParams()
        {
            var p = StadiumBoxMeshGenerator.StadiumBoxParams.Default;
            p.Pivot = DefaultPivotBottom;
            return p;
        }

        private StadiumBoxMeshGenerator.StadiumBoxParams _stadiumP = DefaultStadiumBoxParams();

        /// <summary>フタ指定の行。「上下も丸める」ON のときは隠す。</summary>
        private VisualElement _stadiumCapTopRow;
        private VisualElement _stadiumCapBottomRow;
        private VisualElement _stadiumCapHint;

        // ================================================================
        // UI
        // ================================================================

        private void BuildStadiumBoxUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("StadiumBox")));
            c.Add(NF(() => _stadiumP.MeshName, v => _stadiumP.MeshName = v));

            c.Add(GearHint(T("StadiumBoxHint")));

            c.Add(SR(T("StadiumLength"), StadiumBoxMeshGenerator.StadiumBoxParams.LengthMin, StadiumBoxMeshGenerator.StadiumBoxParams.LengthMax,
                () => _stadiumP.Length, v => { _stadiumP.Length = v; D(); }));
            c.Add(SR(T("StadiumHeight"), StadiumBoxMeshGenerator.StadiumBoxParams.HeightMin, StadiumBoxMeshGenerator.StadiumBoxParams.HeightMax,
                () => _stadiumP.Height, v => { _stadiumP.Height = v; D(); }));
            c.Add(SR(T("StadiumDepth"), StadiumBoxMeshGenerator.StadiumBoxParams.DepthMin, StadiumBoxMeshGenerator.StadiumBoxParams.DepthMax,
                () => _stadiumP.Depth, v => { _stadiumP.Depth = v; D(); }));

            c.Add(TR(T("StadiumRoundTopBottom"),
                () => _stadiumP.RoundTopBottom,
                v => { _stadiumP.RoundTopBottom = v; D(); RefreshStadiumCapVis(); }));
            c.Add(GearHint(T("StadiumRoundTopBottomHint")));

            // ── 上下のフタ ──
            _stadiumCapTopRow = TR(T("CapTop"),
                () => _stadiumP.CapTop,    v => { _stadiumP.CapTop    = v; D(); });
            c.Add(_stadiumCapTopRow);

            _stadiumCapBottomRow = TR(T("CapBottom"),
                () => _stadiumP.CapBottom, v => { _stadiumP.CapBottom = v; D(); });
            c.Add(_stadiumCapBottomRow);

            _stadiumCapHint = GearHint(T("StadiumCapHint"));
            c.Add(_stadiumCapHint);

            RefreshStadiumCapVis();

            // ── 分割数 ──
            c.Add(SL(T("Segments")));
            c.Add(GearHint(T("StadiumSegmentsHint")));
            c.Add(IR(T("StadiumCapSegments"), StadiumBoxMeshGenerator.CapSegmentsMin, StadiumBoxMeshGenerator.CapSegmentsMax,
                () => _stadiumP.CapSegments, v => { _stadiumP.CapSegments = v; D(); }));
            c.Add(IR(T("StadiumLengthSegments"), StadiumBoxMeshGenerator.LineSegmentsMin, StadiumBoxMeshGenerator.LineSegmentsMax,
                () => _stadiumP.LengthSegments, v => { _stadiumP.LengthSegments = v; D(); }));
            c.Add(IR(T("StadiumHeightSegments"), StadiumBoxMeshGenerator.LineSegmentsMin, StadiumBoxMeshGenerator.LineSegmentsMax,
                () => _stadiumP.HeightSegments, v => { _stadiumP.HeightSegments = v; D(); }));

            // ── 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FlipFaces"),
                () => _stadiumP.FlipFaces, v => { _stadiumP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _stadiumP.Pivot, v => { _stadiumP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);
        }

        /// <summary>フタ指定行の表示切替（「上下も丸める」OFF のときだけ出す）。</summary>
        private void RefreshStadiumCapVis()
        {
            var d = _stadiumP.RoundTopBottom ? DisplayStyle.None : DisplayStyle.Flex;
            if (_stadiumCapTopRow    != null) _stadiumCapTopRow.style.display    = d;
            if (_stadiumCapBottomRow != null) _stadiumCapBottomRow.style.display = d;
            if (_stadiumCapHint      != null) _stadiumCapHint.style.display      = d;
        }

        private MeshObject GenerateStadiumBoxMesh()
            => StadiumBoxMeshGenerator.Generate(_stadiumP);
    }
}
