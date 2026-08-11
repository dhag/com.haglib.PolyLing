// PlayerPrimitiveMeshSubPanel.PlaceObject.cs
// 図形生成サブパネル：オブジェクト接地（高度な図形）。
//
// 【配置元の子孫】チェックしたオブジェクトをルートとみなし、その子孫も配置元に加える。
//   結合はせず、子孫を1つずつ別の配置元として並べる（GetSubtreeMeshList）。
//   これで「rung ごとに巡回／抽選」が子孫に対して効く。
//   面を持たないオブジェクト（グループ用の空オブジェクト等）は数に入れない。
//
// 【配置スケール】rung 長による等倍へさらに掛ける倍率。X/Y/Z 連動の1値。
// 基準ベルトの取り込み・自動検索は PlayerPrimitiveMeshSubPanel.BeltProfile.cs の共通部を使う。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
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

        private MeshSourceMultiPick _placeSrcPick  = new MeshSourceMultiPick();  // 配置するオブジェクト（複数可）
        private MeshSourcePick      _placeAutoPick = new MeshSourcePick();       // 自動検索の対象

        private BeltSplineOption _placeSpline = new BeltSplineOption();
        private BeltOrientOption _placeOrient = new BeltOrientOption();

        private Label         _placeInfoLabel;
        private VisualElement _placeSeedRow;   // Random のときだけ表示する

        // ================================================================
        // UI
        // ================================================================

        private void BuildPlaceObjectUI(VisualElement c)
        {
            c.Add(SL(T("PlaceObject")));
            c.Add(NF(() => _placeP.MeshName, v => _placeP.MeshName = v));

            // ── 配置元オブジェクト（複数選択可） ──
            BuildMeshSourceMultiRow(c, _placeSrcPick, T("PlaceSource"));

            // チェックしたオブジェクトをルートとみなし、その子孫も一緒に配置する。
            c.Add(TR(T("PlaceIncludeChildren"),
                () => _placeP.IncludeChildren,
                v => { _placeP.IncludeChildren = v; D(); }));

            var childHint = new Label(T("PlaceIncludeChildrenHint"));
            childHint.style.fontSize     = 10;
            childHint.style.whiteSpace   = WhiteSpace.Normal;
            childHint.style.marginBottom = 2;
            c.Add(childHint);

            // ── 割り当て方式 ──
            c.Add(SL(T("PlaceMode")));
            var modeChoices = new List<string>
            {
                T("PlaceModeCombine"), T("PlaceModeSequence"), T("PlaceModeRandom"),
            };
            var modeDd = new DropdownField(modeChoices, (int)_placeP.Mode);
            modeDd.RegisterValueChangedCallback(_ =>
            {
                _placeP.Mode = (PlaceSourceMode)modeDd.index;
                RefreshPlaceSeedVis();
                D();
            });
            c.Add(modeDd);

            _placeSeedRow = new VisualElement();
            _placeSeedRow.style.flexDirection = FlexDirection.Row;
            _placeSeedRow.style.marginBottom  = 3;
            _placeSeedRow.Add(ML(T("PlaceSeed")));
            var seedField = new IntegerField { value = _placeP.RandomSeed };
            seedField.style.flexGrow = 1;
            seedField.RegisterValueChangedCallback(ev => { _placeP.RandomSeed = ev.newValue; D(); });
            _placeSeedRow.Add(seedField);
            c.Add(_placeSeedRow);
            RefreshPlaceSeedVis();

            // ── 配置スケール（X/Y/Z 連動。rung 長による等倍へさらに掛ける） ──
            c.Add(SR(T("PlaceScale"), 0.1f, 10f,
                () => _placeP.Scale <= 0f ? 1f : _placeP.Scale,
                v  => { _placeP.Scale = v; D(); }));

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

            // ── 梯子CSV ──
            BuildBeltCsvUI(c, _placeBelts,
                "Primitive.PlaceObject.BeltCsv", "place_belt.csv", RefreshPlaceInfo);

            // ── 梯子の向き ──
            BuildBeltOrientUI(c, _placeOrient);

            BuildBeltSplineUI(c, _placeSpline);
        }

        private void RefreshPlaceInfo()
        {
            if (_placeInfoLabel != null) _placeInfoLabel.text = BeltsInfoText(_placeBelts);
        }

        /// <summary>乱数シード欄は Random のときだけ表示する。</summary>
        private void RefreshPlaceSeedVis()
        {
            if (_placeSeedRow == null) return;
            _placeSeedRow.style.display =
                (_placeP.Mode == PlaceSourceMode.Random) ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 各基準ベルトの rung 中心へ配置元オブジェクトを複製する。未取込・未選択なら空メッシュ。
        /// Combine は全 rung へ結合メッシュ、Sequence/Random は rung ごとに選択リストから1つを割り当てる。
        /// </summary>
        private MeshObject GeneratePlaceObjectMesh()
        {
            var mo   = new MeshObject(_placeP.MeshName);
            var srcs = _placeSrcPick.CurrentList(_placeP.IncludeChildren, GetSubtreeMeshList);
            if (srcs.Count == 0) return mo;

            float userScale = _placeP.Scale <= 0f ? 1f : _placeP.Scale;

            // Combine は全 rung 共通の1メッシュ。連結は生成ごとに1回だけ行う。
            MeshObject combined = (_placeP.Mode == PlaceSourceMode.Combine)
                ? CombineMeshes(srcs, _placeP.MeshName)
                : null;

            // Random は生成開始時に1個だけ作り、ベルト→rung の固定順で引く。
            // 同じシード・同じ入力なら同一結果になる。
            var rnd = (_placeP.Mode == PlaceSourceMode.Random)
                ? new System.Random(_placeP.RandomSeed)
                : null;

            // Sequence の巡回位置はベルトをまたいで連続させる。
            int seqIndex = 0;

            foreach (var belt in _placeBelts)
            {
                if (belt == null || !belt.HasData) continue;
                var b = ApplyBeltSpline(ApplyBeltOrient(belt, _placeOrient), _placeSpline);

                int n = Mathf.Min(b.Left.Count, b.Right.Count);
                var perRung = new MeshObject[n];
                for (int i = 0; i < n; i++)
                {
                    switch (_placeP.Mode)
                    {
                        case PlaceSourceMode.Sequence:
                            perRung[i] = srcs[seqIndex];
                            seqIndex   = (seqIndex + 1) % srcs.Count;
                            break;
                        case PlaceSourceMode.Random:
                            perRung[i] = srcs[rnd.Next(srcs.Count)];
                            break;
                        default:
                            perRung[i] = combined;
                            break;
                    }
                }

                var part = PlaceObjectMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    perRung, _placeP.MeshName, userScale);
                AppendMesh(mo, part);
            }
            return mo;
        }
    }
}
