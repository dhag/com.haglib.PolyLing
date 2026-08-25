// PlayerPrimitiveMeshSubPanel.PlaceObject.cs
// 図形生成サブパネル：オブジェクト配置（高度な図形）。
//
// 【配置元の子孫】チェックしたオブジェクトをルートとみなし、その子孫も配置元に加える。
//   結合はせず、子孫を1つずつ別の配置元として並べる（GetSubtreeMeshList）。
//   これで「rung ごとに巡回／抽選」が子孫に対して効く。
//   面を持たないオブジェクト（グループ用の空オブジェクト等）は数に入れない。
//
// 【配置スケール】方式を2つ持つ。X/Y/Z 連動の1値。
//   rung 長に比例: 大きさ = rung 長 × 倍率。梯子の幅に比例する（従来の挙動）。
//   一律サイズ    : 大きさ = サイズ。梯子の幅に関係なく一定。
//
// 【間引き】rung 方向と段方向を独立に何個おきかで指定する。
//   rung 間引き: rung 番号 i が i % RungStride == RungOffset の rung だけ配置する。
//   段 間引き  : 段番号 r が r % RowStride == RowOffset の段だけ配置する。
//     段番号は「上下方向にも探索」でレール辺を跨いで得た BeltSnapshot.RowIndex。
//     この探索が OFF のときは全梯子が RowIndex = 0 になるため、段間引きは効かない。
//
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

        // 上下（左右レール側）への段展開。既定 OFF は従来の検出結果と同じ。
        // 段間引きはここで振られる RowIndex を使うため、段間引きを使うときは ON にする。
        private BeltStackOption _placeStack = new BeltStackOption { Enabled = false };

        private Label         _placeInfoLabel;
        private VisualElement _placeSeedRow;   // Random のときだけ表示する

        private Label _placeScaleLabel;        // 方式で「倍率」／「サイズ」を切り替える
        private Label _placeScaleHint;

        private const float PlaceScaleMin = 0.1f;
        private const float PlaceScaleMax = 10f;

        // ================================================================
        // UI
        // ================================================================

        private void BuildPlaceObjectUI(VisualElement c)
        {
            c.Add(ShapeTitle(T("PlaceObject")));
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

            // ── 配置スケール（方式＋数値。X/Y/Z 連動） ──
            BuildPlaceScaleUI(c);

            // ── 間引き（rung 方向・段方向） ──
            BuildPlaceThinUI(c);

            // ── 基準はしご（取り込み〜向きまでを1つのフォールドにまとめる） ──
            c.Add(PlayerIoUiKit.Divider());
            var baseFold = new Foldout { text = T("FrillBase"), value = true };
            baseFold.style.marginBottom = 4;
            var bc = baseFold.contentContainer;
            c.Add(baseFold);

            var hint = new Label(T("PlaceBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            bc.Add(hint);

            // ── 上下方向への探索（取り込み・自動検索の両方に効く） ──
            // 段間引きの段番号はこの探索で決まるので、段間引きを使うときは ON にする。
            bc.Add(TR(T("BeltStackSearch"), () => _placeStack.Enabled,
                v => { _placeStack.Enabled = v; D(); }));

            var stackHint = new Label(T("BeltStackSearchHint"));
            stackHint.style.fontSize     = 10;
            stackHint.style.whiteSpace   = WhiteSpace.Normal;
            stackHint.style.marginBottom = 2;
            bc.Add(stackHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_placeBelts, _placeStack.Enabled);
                RefreshPlaceInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(bc, _placeAutoPick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            bc.Add(autoHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_placeBelts, _placeAutoPick.Current, _placeStack.Enabled);
                RefreshPlaceInfo();
            }));

            _placeInfoLabel = new Label(PlaceInfoText());
            _placeInfoLabel.style.fontSize   = 10;
            _placeInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _placeInfoLabel.style.marginTop  = 2;
            bc.Add(_placeInfoLabel);

            // ── はしごCSV ──
            BuildBeltCsvUI(bc, _placeBelts,
                "Primitive.PlaceObject.BeltCsv", "place_belt.csv", RefreshPlaceInfo);

            // ── はしごの向き ──
            BuildBeltOrientUI(bc, _placeOrient);

            // ── Z ロール（配置フレームの Z 軸まわり、90°単位） ──
            // 配置側のパラメータなので基準はしごのフォールドの外に出す。
            BuildPlaceRollUI(c);

            BuildBeltSplineUI(c, _placeSpline);
        }

        /// <summary>
        /// 配置スケール。方式のドロップダウンと数値行を作る。
        /// 数値の意味が方式で変わるため、行ラベルとヒント文を方式に合わせて書き換える。
        /// SR() はラベルを後から差し替えられないので、この行だけ手で組む。
        /// </summary>
        private void BuildPlaceScaleUI(VisualElement c)
        {
            c.Add(SL(T("PlaceScaleMode")));

            var modeChoices = new List<string>
            {
                T("PlaceScaleModeRung"), T("PlaceScaleModeUniform"),
            };
            var modeDd = new DropdownField(modeChoices, (int)_placeP.ScaleMode);
            modeDd.RegisterValueChangedCallback(_ =>
            {
                _placeP.ScaleMode = (PlaceScaleMode)modeDd.index;
                RefreshPlaceScaleVis();
                D();
            });
            c.Add(modeDd);

            _placeScaleHint = new Label();
            _placeScaleHint.style.fontSize     = 10;
            _placeScaleHint.style.whiteSpace   = WhiteSpace.Normal;
            _placeScaleHint.style.marginBottom = 2;
            c.Add(_placeScaleHint);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            _placeScaleLabel = ML(string.Empty);
            row.Add(_placeScaleLabel);

            float cur = _placeP.Scale <= 0f ? 1f : _placeP.Scale;

            var sl = new Slider(PlaceScaleMin, PlaceScaleMax) { value = cur };
            sl.style.flexGrow = 1;
            var nf = new FloatField { value = cur };
            nf.style.width = 42;

            sl.RegisterValueChangedCallback(e =>
            {
                nf.SetValueWithoutNotify(Mathf.Round(e.newValue * 1000f) / 1000f);
                _placeP.Scale = e.newValue;
                D();
            });
            nf.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp(e.newValue, PlaceScaleMin, PlaceScaleMax);
                sl.SetValueWithoutNotify(v);
                _placeP.Scale = v;
                D();
            });

            row.Add(sl);
            row.Add(nf);
            c.Add(row);

            RefreshPlaceScaleVis();
        }

        /// <summary>スケール数値行のラベルとヒント文を、現在の方式へ合わせる。</summary>
        private void RefreshPlaceScaleVis()
        {
            bool uniform = _placeP.ScaleMode == PlaceScaleMode.Uniform;

            if (_placeScaleLabel != null)
                _placeScaleLabel.text = uniform ? T("PlaceScaleUniformLabel") : T("PlaceScaleRungLabel");

            if (_placeScaleHint != null)
                _placeScaleHint.text = uniform ? T("PlaceScaleHintUniform") : T("PlaceScaleHintRung");
        }

        /// <summary>
        /// 間引き。rung 方向と段方向を独立に指定する。
        /// 間隔 1・開始位置 0 が全数配置（既定）。
        /// </summary>
        private void BuildPlaceThinUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());

            var fold = new Foldout { text = T("PlaceThin"), value = false };
            fold.style.marginBottom = 4;
            var fc = fold.contentContainer;
            c.Add(fold);

            var hint = new Label(T("PlaceThinHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            fc.Add(hint);

            fc.Add(IR(T("PlaceRungStride"), 1, 10,
                () => Mathf.Max(1, _placeP.RungStride),
                v  => { _placeP.RungStride = Mathf.Max(1, v); RefreshPlaceInfo(); D(); }));

            fc.Add(IR(T("PlaceRungOffset"), 0, 9,
                () => Mathf.Max(0, _placeP.RungOffset),
                v  => { _placeP.RungOffset = Mathf.Max(0, v); RefreshPlaceInfo(); D(); }));

            fc.Add(IR(T("PlaceRowStride"), 1, 10,
                () => Mathf.Max(1, _placeP.RowStride),
                v  => { _placeP.RowStride = Mathf.Max(1, v); RefreshPlaceInfo(); D(); }));

            fc.Add(IR(T("PlaceRowOffset"), 0, 9,
                () => Mathf.Max(0, _placeP.RowOffset),
                v  => { _placeP.RowOffset = Mathf.Max(0, v); RefreshPlaceInfo(); D(); }));
        }

        /// <summary>
        /// Z 軸まわりのロール（90°単位）。段数 0〜3 のスライダと度数表示。
        /// SliderInt に刻み幅が無いので段数で持ち、表示だけ度数へ直す。
        /// </summary>
        private void BuildPlaceRollUI(VisualElement c)
        {
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("PlaceRoll")));

            var hint = new Label(T("PlaceRollHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;

            var slider = new SliderInt(0, 3) { value = Mathf.Clamp(_placeP.RollSteps, 0, 3) };
            slider.style.flexGrow = 1;

            var degLabel = new Label($"{Mathf.Clamp(_placeP.RollSteps, 0, 3) * 90}°");
            degLabel.style.width          = 40;
            degLabel.style.unityTextAlign = TextAnchor.MiddleRight;

            slider.RegisterValueChangedCallback(e =>
            {
                int step = Mathf.Clamp(e.newValue, 0, 3);
                _placeP.RollSteps = step;
                degLabel.text = $"{step * 90}°";
                D();
            });

            row.Add(slider);
            row.Add(degLabel);
            c.Add(row);
        }

        private void RefreshPlaceInfo()
        {
            RefreshCreateButtonState();
            if (_placeInfoLabel != null) _placeInfoLabel.text = PlaceInfoText();
        }

        /// <summary>
        /// 基準はしごの概要に、rung 長の実測値と間引き後の配置個数を足した文を返す。
        /// スケール方式を選ぶときの判断材料として rung 長の幅を出す。
        /// rung 長の統計は全段（間引き前）、配置個数は間引き後の数。
        /// </summary>
        private string PlaceInfoText()
        {
            string head = BeltsInfoText(_placeBelts);
            if (_placeBelts == null || _placeBelts.Count == 0) return head;

            int rungStride = Mathf.Max(1, _placeP.RungStride);
            int rungOffset = ((_placeP.RungOffset % rungStride) + rungStride) % rungStride;
            int rowStride  = Mathf.Max(1, _placeP.RowStride);
            int rowOffset  = ((_placeP.RowOffset % rowStride) + rowStride) % rowStride;

            float min = float.MaxValue, max = 0f, sum = 0f;
            int   lenCount = 0, placed = 0, rows = 0;

            foreach (var belt in _placeBelts)
            {
                if (belt == null || !belt.HasData) continue;

                // 生成と同じ順序（向き補正 → 段間引き → スプライン分割）で数える。
                var oriented = ApplyBeltOrient(belt, _placeOrient);

                int  row    = Mathf.Max(0, oriented.RowIndex);
                bool useRow = (row % rowStride) == rowOffset;
                if (useRow) rows++;

                var b = ApplyBeltSpline(oriented, _placeSpline);

                int n = Mathf.Min(b.Left.Count, b.Right.Count);
                for (int i = 0; i < n; i++)
                {
                    float len = (b.Right[i] - b.Left[i]).magnitude;
                    if (len <= 1e-6f) continue;

                    min = Mathf.Min(min, len);
                    max = Mathf.Max(max, len);
                    sum += len;
                    lenCount++;

                    if (useRow && (i % rungStride) == rungOffset) placed++;
                }
            }

            if (lenCount == 0) return head;

            string lenText = T("PlaceRungLenInfo",
                min.ToString("0.###"), max.ToString("0.###"), (sum / lenCount).ToString("0.###"));

            return head + "\n" + lenText + "\n" + T("PlaceCountInfo", placed, rows);
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
        /// 間引きは段（RowIndex）→ rung（rung 番号）の順に効かせ、置かない rung は割り当てを進めない。
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

            // 間引き。間隔は 1 以上へ、開始位置は間隔で割った余りへ丸める。
            int rungStride = Mathf.Max(1, _placeP.RungStride);
            int rungOffset = ((_placeP.RungOffset % rungStride) + rungStride) % rungStride;
            int rowStride  = Mathf.Max(1, _placeP.RowStride);
            int rowOffset  = ((_placeP.RowOffset % rowStride) + rowStride) % rowStride;

            foreach (var belt in _placeBelts)
            {
                if (belt == null || !belt.HasData) continue;

                // 段間引き。段番号は上下探索で振られた RowIndex（未展開なら全段 0）。
                // 向き補正（左右入れ替え）は RowIndex を反転させるので、補正後の番号で判定する。
                // スプライン分割は rung 数を変えるだけなので、段を通した後に掛ける。
                var oriented = ApplyBeltOrient(belt, _placeOrient);

                int row = Mathf.Max(0, oriented.RowIndex);
                if ((row % rowStride) != rowOffset) continue;

                var b = ApplyBeltSpline(oriented, _placeSpline);

                int n = Mathf.Min(b.Left.Count, b.Right.Count);
                var perRung = new MeshObject[n];
                for (int i = 0; i < n; i++)
                {
                    // rung 間引き。置かない rung は null のままにする
                    // （PlaceObjectMeshGenerator は null の rung を飛ばす）。
                    // 巡回位置・抽選は実際に置く rung だけで進める。
                    if ((i % rungStride) != rungOffset) continue;

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
                    perRung, _placeP.MeshName, userScale, _placeP.RollSteps, _placeP.ScaleMode);
                AppendMesh(mo, part);
            }
            return mo;
        }
    }
}
