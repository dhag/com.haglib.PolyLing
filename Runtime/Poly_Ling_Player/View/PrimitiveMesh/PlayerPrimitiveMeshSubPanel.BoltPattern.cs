// PlayerPrimitiveMeshSubPanel.BoltPattern.cs
// 図形生成サブパネル：ネジ配置（機構部品B）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置
//
// 【生成経路】
//   パネルからメッシュを直接作らない。BuildCreateCommand（.Command.cs）で
//   CreateBoltPatternCommand を組み、PrimitiveMeshFactory が作る。プレビューも同じ経路。
//   中心は配置位置、全体の向きは配置の回転（共通の配置欄）で決める。

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.PlaceObject;
using Poly_Ling.PrimitiveMesh;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        private BoltPatternMeshGenerator.BoltPatternParams _boltP =
            BoltPatternMeshGenerator.BoltPatternParams.Default;

        /// <summary>配置元（ネジ）。複数可。</summary>
        private MeshSourceMultiPick _boltSrcPick = new MeshSourceMultiPick();

        private VisualElement _boltCircleBox;
        private VisualElement _boltRectBox;

        [UiControl("boltPattern.info", Safety = UiSafety.ReadOnly, Description = "ネジ配置の個数")]
        private Label _boltInfo;

        private void BuildBoltPatternUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("BoltPattern")));
            c.Add(NF(() => _boltP.MeshName, v => _boltP.MeshName = v));
            c.Add(GearHint(T("BoltPatternHint")));

            // ── 配置元（複数選択可） ──
            BuildMeshSourceMultiRow(c, _boltSrcPick, T("PlaceSource"));

            c.Add(TR(T("PlaceIncludeChildren"),
                () => _boltP.IncludeChildren,
                v => { _boltP.IncludeChildren = v; D(); }));

            c.Add(DD(T("PlaceMode"),
                new List<string> { T("BoltModeCombine"), T("BoltModeSequence"), T("BoltModeRandom") },
                () => (int)_boltP.Mode,
                i => { _boltP.Mode = (PlaceSourceMode)i; D(); }));

            c.Add(IR(T("PlaceSeed"), 0, 9999,
                () => _boltP.RandomSeed,
                v => { _boltP.RandomSeed = v; D(); }));

            c.Add(SR(T("BoltScale"),
                BoltPatternMeshGenerator.BoltPatternParams.ScaleMin,
                BoltPatternMeshGenerator.BoltPatternParams.ScaleMax,
                () => _boltP.Scale, v => { _boltP.Scale = v; D(); }));

            // ── 並べ方 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(DD(T("BoltLayout"),
                new List<string> { T("BoltLayoutCircle"), T("BoltLayoutRect") },
                () => (int)_boltP.Layout,
                i => { _boltP.Layout = (BoltPatternLayout)i; RefreshBoltLayoutVisibility(); DB(); }));

            // 円（PCD）
            _boltCircleBox = new VisualElement();
            _boltCircleBox.Add(IR(T("BoltCount"),
                BoltPatternMeshGenerator.BoltPatternParams.CircleCountMin,
                BoltPatternMeshGenerator.BoltPatternParams.CircleCountMax,
                () => _boltP.Count, v => { _boltP.Count = v; DB(); }));
            _boltCircleBox.Add(SR(T("BoltPcdRadius"),
                BoltPatternMeshGenerator.BoltPatternParams.RadiusMin,
                BoltPatternMeshGenerator.BoltPatternParams.RadiusMax,
                () => _boltP.PcdRadius, v => { _boltP.PcdRadius = v; D(); }));
            _boltCircleBox.Add(SR(T("BoltStartAngle"),
                BoltPatternMeshGenerator.BoltPatternParams.AngleMin,
                BoltPatternMeshGenerator.BoltPatternParams.AngleMax,
                () => _boltP.StartAngle, v => { _boltP.StartAngle = v; D(); }));
            c.Add(_boltCircleBox);

            // 長方形
            _boltRectBox = new VisualElement();
            _boltRectBox.Add(SR(T("BoltRectWidth"),
                BoltPatternMeshGenerator.BoltPatternParams.SizeMin,
                BoltPatternMeshGenerator.BoltPatternParams.SizeMax,
                () => _boltP.Width, v => { _boltP.Width = v; D(); }));
            _boltRectBox.Add(SR(T("BoltRectHeight"),
                BoltPatternMeshGenerator.BoltPatternParams.SizeMin,
                BoltPatternMeshGenerator.BoltPatternParams.SizeMax,
                () => _boltP.Height, v => { _boltP.Height = v; D(); }));
            _boltRectBox.Add(IR(T("BoltCountX"),
                BoltPatternMeshGenerator.BoltPatternParams.SideCountMin,
                BoltPatternMeshGenerator.BoltPatternParams.SideCountMax,
                () => _boltP.CountX, v => { _boltP.CountX = v; DB(); }));
            _boltRectBox.Add(IR(T("BoltCountY"),
                BoltPatternMeshGenerator.BoltPatternParams.SideCountMin,
                BoltPatternMeshGenerator.BoltPatternParams.SideCountMax,
                () => _boltP.CountY, v => { _boltP.CountY = v; DB(); }));
            c.Add(_boltRectBox);

            c.Add(OrientationDD(() => _boltP.Orientation, v => { _boltP.Orientation = v; D(); }));

            _boltInfo = GearHint(string.Empty);
            c.Add(_boltInfo);

            RefreshBoltLayoutVisibility();
            RefreshBoltInfo();
        }

        /// <summary>個数が変わる操作用の D()。個数の表示も書き直す。</summary>
        private void DB()
        {
            D();
            RefreshBoltInfo();
        }

        private void RefreshBoltLayoutVisibility()
        {
            bool rect = _boltP.Layout == BoltPatternLayout.Rectangle;
            if (_boltCircleBox != null)
                _boltCircleBox.style.display = rect ? DisplayStyle.None : DisplayStyle.Flex;
            if (_boltRectBox != null)
                _boltRectBox.style.display = rect ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshBoltInfo()
        {
            if (_boltInfo == null) return;
            _boltInfo.text = T("BoltInfo", BoltPatternMeshGenerator.PlacementCount(_boltP));
        }
    }
}
