// PlayerPrimitiveMeshSubPanel.PlaceObject.cs
// 図形生成サブパネル：オブジェクト接地（高度な図形）。
// 基準ベルトの取り込み・自動検索は PlayerPrimitiveMeshSubPanel.BeltProfile.cs の共通部を使う。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PlaceObject;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private PlaceObjectParams  _placeP     = PlaceObjectParams.Default;
        private List<BeltSnapshot> _placeBelts = new List<BeltSnapshot>();

        private MeshSourcePick _placeSrcPick  = new MeshSourcePick();  // 配置するオブジェクト
        private MeshSourcePick _placeAutoPick = new MeshSourcePick();  // 自動検索の対象

        private BeltSplineOption _placeSpline = new BeltSplineOption();

        private Label _placeInfoLabel;

        // ================================================================
        // UI
        // ================================================================

        private void BuildPlaceObjectUI(VisualElement c)
        {
            c.Add(SL(T("PlaceObject")));
            c.Add(NF(() => _placeP.MeshName, v => _placeP.MeshName = v));

            // ── 配置元オブジェクト ──
            BuildMeshSourceRow(c, _placeSrcPick, T("PlaceSource"));

            // ── 基準ベルト（手動取り込み） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("FrillBase")));

            var hint = new Label(T("PlaceBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            c.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_placeBelts);
                RefreshPlaceInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(c, _placeAutoPick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            c.Add(autoHint);

            c.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_placeBelts, _placeAutoPick.Current);
                RefreshPlaceInfo();
            }));

            _placeInfoLabel = new Label(BeltsInfoText(_placeBelts));
            _placeInfoLabel.style.fontSize   = 10;
            _placeInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _placeInfoLabel.style.marginTop  = 2;
            c.Add(_placeInfoLabel);

            BuildBeltSplineUI(c, _placeSpline);
        }

        private void RefreshPlaceInfo()
        {
            if (_placeInfoLabel != null) _placeInfoLabel.text = BeltsInfoText(_placeBelts);
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>各基準ベルトの rung 中心へ配置元オブジェクトを複製する。未取込・未選択なら空メッシュ。</summary>
        private MeshObject GeneratePlaceObjectMesh()
        {
            var mo  = new MeshObject(_placeP.MeshName);
            var src = _placeSrcPick.Current;
            if (src == null) return mo;

            foreach (var belt in _placeBelts)
            {
                if (belt == null || !belt.HasData) continue;
                var b = ApplyBeltSpline(belt, _placeSpline);
                var part = PlaceObjectMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    src, _placeP.MeshName);
                AppendMesh(mo, part);
            }
            return mo;
        }
    }
}
