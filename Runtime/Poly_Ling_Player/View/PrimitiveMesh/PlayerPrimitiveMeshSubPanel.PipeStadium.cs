// PlayerPrimitiveMeshSubPanel.PipeStadium.cs
// 図形生成サブパネル：パイプ接続用小判型（手のひらのもと）（高度な図形）。
// 長さ X と奥行き Z は指定ではなく、円の個数・半径・矩形部の幅から決まるので、
// 値を読み取り専用の行として出す。
// 「手のひらにする」を入れると A〜D の 4 段になり、高さ Y と Y の分割数の代わりに
// 区間ごとの高さ・分割数と D の幅を指定する。行は display の切り替えで見せ分ける。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
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

        private static PipeStadiumMeshGenerator.PipeStadiumParams DefaultPipeStadiumParams()
        {
            var p = PipeStadiumMeshGenerator.PipeStadiumParams.Default;
            p.Pivot = DefaultPivotBottom;
            return p;
        }

        private PipeStadiumMeshGenerator.PipeStadiumParams _pipeStadiumP = DefaultPipeStadiumParams();

        /// <summary>長さ X / 奥行き Z の表示欄。パラメータを触るたびに書き直す。</summary>
        private Label _pipeStadiumSizeLabel;

        /// <summary>単段のときだけ出す行。</summary>
        private readonly List<VisualElement> _pipeStadiumSingleRows = new List<VisualElement>();
        /// <summary>手のひらモードのときだけ出す行。</summary>
        private readonly List<VisualElement> _pipeStadiumPalmRows = new List<VisualElement>();

        // ================================================================
        // UI
        // ================================================================

        private void BuildPipeStadiumUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("PipeStadium")));
            c.Add(NF(() => _pipeStadiumP.MeshName, v => _pipeStadiumP.MeshName = v));

            c.Add(GearHint(T("PipeStadiumHint")));

            c.Add(IR(T("PipeStadiumCircleCount"),
                PipeStadiumMeshGenerator.CircleCountMin, PipeStadiumMeshGenerator.CircleCountMax,
                () => _pipeStadiumP.CircleCount,
                v => { _pipeStadiumP.CircleCount = v; RefreshPipeStadiumSize(); D(); }));

            c.Add(SR(T("PipeStadiumRadius"),
                PipeStadiumMeshGenerator.PipeStadiumParams.RadiusMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.RadiusMax,
                () => _pipeStadiumP.Radius,
                v => { _pipeStadiumP.Radius = v; RefreshPipeStadiumSize(); D(); }));

            c.Add(SR(T("PipeStadiumGapWidth"),
                PipeStadiumMeshGenerator.PipeStadiumParams.GapWidthMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.GapWidthMax,
                () => _pipeStadiumP.GapWidth,
                v => { _pipeStadiumP.GapWidth = v; RefreshPipeStadiumSize(); D(); }));

            var heightRow = SR(T("PipeStadiumHeight"),
                PipeStadiumMeshGenerator.PipeStadiumParams.HeightMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.HeightMax,
                () => _pipeStadiumP.Height,
                v => { _pipeStadiumP.Height = v; RefreshPipeStadiumSize(); D(); });
            c.Add(heightRow);
            _pipeStadiumSingleRows.Add(heightRow);

            // ── 手のひら ──
            c.Add(TR(T("PipeStadiumPalm"),
                () => _pipeStadiumP.Palm,
                v => { _pipeStadiumP.Palm = v; RefreshPipeStadiumMode(); RefreshPipeStadiumSize(); D(); }));
            c.Add(GearHint(T("PipeStadiumPalmHint")));
            AddPipeStadiumPalmRow(c, GearHint(T("PipeStadiumThumbHint")));

            AddPipeStadiumPalmRow(c, IR(T("PipeStadiumThumbLeft"),
                PipeStadiumMeshGenerator.ThumbCountMin, PipeStadiumMeshGenerator.ThumbCountMax,
                () => _pipeStadiumP.ThumbLeft,
                v => { _pipeStadiumP.ThumbLeft = v; RefreshPipeStadiumSize(); D(); }));
            AddPipeStadiumPalmRow(c, IR(T("PipeStadiumThumbRight"),
                PipeStadiumMeshGenerator.ThumbCountMin, PipeStadiumMeshGenerator.ThumbCountMax,
                () => _pipeStadiumP.ThumbRight,
                v => { _pipeStadiumP.ThumbRight = v; RefreshPipeStadiumSize(); D(); }));

            AddPipeStadiumPalmRow(c, SR(T("PipeStadiumGapWidthD"),
                PipeStadiumMeshGenerator.PipeStadiumParams.GapWidthPalmMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.GapWidthMax,
                () => _pipeStadiumP.GapWidthD,
                v => { _pipeStadiumP.GapWidthD = v; RefreshPipeStadiumSize(); D(); }));
            AddPipeStadiumPalmRow(c, SR(T("PipeStadiumHeightAB"),
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMax,
                () => _pipeStadiumP.HeightAB,
                v => { _pipeStadiumP.HeightAB = v; RefreshPipeStadiumSize(); D(); }));
            AddPipeStadiumPalmRow(c, SR(T("PipeStadiumHeightBC"),
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMax,
                () => _pipeStadiumP.HeightBC,
                v => { _pipeStadiumP.HeightBC = v; RefreshPipeStadiumSize(); D(); }));
            AddPipeStadiumPalmRow(c, SR(T("PipeStadiumHeightCD"),
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMin,
                PipeStadiumMeshGenerator.PipeStadiumParams.PalmHeightMax,
                () => _pipeStadiumP.HeightCD,
                v => { _pipeStadiumP.HeightCD = v; RefreshPipeStadiumSize(); D(); }));

            _pipeStadiumSizeLabel = GearHint(string.Empty);
            c.Add(_pipeStadiumSizeLabel);
            RefreshPipeStadiumSize();

            // ── 上下のフタ ──
            c.Add(SL(T("PipeStadiumCap")));
            c.Add(GearHint(T("PipeStadiumCapHint")));
            c.Add(PipeStadiumCapDD(T("PipeStadiumCapTop"),
                () => _pipeStadiumP.CapTopMode,    v => { _pipeStadiumP.CapTopMode    = v; D(); }));
            c.Add(PipeStadiumCapDD(T("PipeStadiumCapBottom"),
                () => _pipeStadiumP.CapBottomMode, v => { _pipeStadiumP.CapBottomMode = v; D(); }));

            // ── 分割数 ──
            c.Add(SL(T("Segments")));
            c.Add(GearHint(T("PipeStadiumSegmentsHint")));
            c.Add(IR(T("PipeStadiumRadialSegments"),
                PipeStadiumMeshGenerator.RadialSegmentsMin, PipeStadiumMeshGenerator.RadialSegmentsMax,
                () => _pipeStadiumP.RadialSegments, v => { _pipeStadiumP.RadialSegments = v; D(); }));
            c.Add(IR(T("PipeStadiumGapSegments"),
                PipeStadiumMeshGenerator.GapSegmentsMin, PipeStadiumMeshGenerator.GapSegmentsMax,
                () => _pipeStadiumP.GapSegments, v => { _pipeStadiumP.GapSegments = v; D(); }));
            var heightSegRow = IR(T("PipeStadiumHeightSegments"),
                PipeStadiumMeshGenerator.HeightSegmentsMin, PipeStadiumMeshGenerator.HeightSegmentsMax,
                () => _pipeStadiumP.HeightSegments, v => { _pipeStadiumP.HeightSegments = v; D(); });
            c.Add(heightSegRow);
            _pipeStadiumSingleRows.Add(heightSegRow);

            AddPipeStadiumPalmRow(c, IR(T("PipeStadiumSegmentsAB"),
                PipeStadiumMeshGenerator.PalmSegmentsMin, PipeStadiumMeshGenerator.PalmSegmentsMax,
                () => _pipeStadiumP.SegmentsAB, v => { _pipeStadiumP.SegmentsAB = v; D(); }));
            AddPipeStadiumPalmRow(c, IR(T("PipeStadiumSegmentsBC"),
                PipeStadiumMeshGenerator.PalmSegmentsMin, PipeStadiumMeshGenerator.PalmSegmentsMax,
                () => _pipeStadiumP.SegmentsBC, v => { _pipeStadiumP.SegmentsBC = v; D(); }));
            AddPipeStadiumPalmRow(c, IR(T("PipeStadiumSegmentsCD"),
                PipeStadiumMeshGenerator.PalmSegmentsMin, PipeStadiumMeshGenerator.PalmSegmentsMax,
                () => _pipeStadiumP.SegmentsCD, v => { _pipeStadiumP.SegmentsCD = v; D(); }));

            c.Add(IR(T("PipeStadiumRadialRings"),
                PipeStadiumMeshGenerator.RadialRingsMin, PipeStadiumMeshGenerator.RadialRingsMax,
                () => _pipeStadiumP.RadialRings, v => { _pipeStadiumP.RadialRings = v; D(); }));

            // ── 面の向き ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("FlipFaces"),
                () => _pipeStadiumP.FlipFaces, v => { _pipeStadiumP.FlipFaces = v; D(); }));

            BuildPivotXYZ(c,
                () => _pipeStadiumP.Pivot, v => { _pipeStadiumP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

            RefreshPipeStadiumMode();
        }

        /// <summary>手のひらモードのときだけ出す行を足す。</summary>
        private void AddPipeStadiumPalmRow(VisualElement c, VisualElement row)
        {
            c.Add(row);
            _pipeStadiumPalmRows.Add(row);
        }

        /// <summary>単段用・手のひら用の行の表示切替。</summary>
        private void RefreshPipeStadiumMode()
        {
            bool palm = _pipeStadiumP.Palm;
            foreach (var r in _pipeStadiumSingleRows)
                if (r != null) r.style.display = palm ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (var r in _pipeStadiumPalmRows)
                if (r != null) r.style.display = palm ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>フタの張り方のドロップダウン（すべてふさぐ / すべて抜く / 円の部分だけ抜く）。</summary>
        private VisualElement PipeStadiumCapDD(
            string label,
            System.Func<PipeStadiumCapMode> get, System.Action<PipeStadiumCapMode> set)
        {
            var dd = new DropdownField(
                new List<string> { T("PipeStadiumCapFull"), T("PipeStadiumCapNone"), T("PipeStadiumCapHole") },
                (int)get());
            dd.label = label;
            dd.style.marginBottom = 2;
            dd.RegisterValueChangedCallback(_ => set((PipeStadiumCapMode)dd.index));
            return dd;
        }

        /// <summary>長さ X / 奥行き Z の表示を今のパラメータで書き直す。</summary>
        private void RefreshPipeStadiumSize()
        {
            if (_pipeStadiumSizeLabel == null) return;
            float len = PipeStadiumMeshGenerator.LengthOf(_pipeStadiumP);
            float dep = PipeStadiumMeshGenerator.DepthOf(_pipeStadiumP);
            float hgt = PipeStadiumMeshGenerator.TotalHeightOf(_pipeStadiumP);
            string head = T("PipeStadiumSize");
            if (!_pipeStadiumP.Palm)
            {
                _pipeStadiumSizeLabel.text = head + $" X = {len:0.###} / Z = {dep:0.###}";
            }
            else
            {
                float lenB = PipeStadiumMeshGenerator.LengthOfB(_pipeStadiumP);
                float lenD = PipeStadiumMeshGenerator.LengthOfD(_pipeStadiumP);
                _pipeStadiumSizeLabel.text = head +
                    $" A: X = {len:0.###} / B: X = {lenB:0.###} / D: X = {lenD:0.###}" +
                    $" / Z = {dep:0.###} / Y = {hgt:0.###}";
            }
        }

        private MeshObject GeneratePipeStadiumMesh()
            => PipeStadiumMeshGenerator.Generate(_pipeStadiumP);
    }
}
