// PlayerLayoutRoot.LeftPane.cs
// 左ペイン：上部の常時表示部（状態・Undo・モデル・選択モード）と表示フラグのグリッド。
// 通常ボタンのカテゴリ別 Foldout は PlayerLayoutRoot.LeftPaneButtons.cs。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public partial class PlayerLayoutRoot
    {
        // ================================================================
        // Left ペイン公開要素
        // ================================================================

        public Label         StatusLabel        { get; private set; }
        public Button        UndoBtn            { get; private set; }
        public Button        RedoBtn            { get; private set; }
        public VisualElement ModelListContainer  { get; private set; }
        public DropdownField ModelSelectDropdown { get; private set; }
        public Button        ModelListBtn        { get; private set; }
        public Button        MeshListBtn         { get; private set; }

        // ================================================================
        // ビューポート表示フラグ（面ごと）
        // ================================================================

        /// <summary>
        /// 面ごとの表示トグル配列。[viewportSlot, itemIndex]
        ///
        /// viewportSlot: 0=Perspective、1=Top、2=Front、3=Side
        ///   （PlayerViewportManager の SlotPerspective 等と対応）
        ///
        /// itemIndex 定数は VD_* を参照。
        /// </summary>
        public Toggle[,] ViewportDisplayToggles { get; private set; }

        // itemIndex 定数
        // 「選択Mirror」トグルは廃止。選択メッシュのミラー表示は選択Mesh に従属する
        // （ViewportDisplaySettings.WithMirrorClamped）。
        // 【番号を繰り下げてよい理由】 2026-08-28
        //   非選Mirror の直下に 2 行を挿入したため VD_SEL_WIRE 以降が +2 されている。
        //   これらは全てシンボルで参照されており、表示設定の永続化は
        //   ViewportDisplaySettings.ToBits / FromBits が担う（トグルの並び順とは無関係）。
        //   したがって番号が変わっても保存データは壊れない。
        //   itemLabels / itemDefaults の並びは必ずこの順序に一致させること。
        public const int VD_CULLING      = 0;
        public const int VD_SEL_MESH     = 1;
        public const int VD_UNSEL_MESH   = 2;
        /// <summary>非選択ミラーのマスタ。下の 3 つを一括で落とす。独立（非選Mesh に従属しない）。</summary>
        public const int VD_UNSEL_MIRROR = 3;
        /// <summary>非選択ミラーの面。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_MESH = 4;
        /// <summary>非選択ミラーの辺。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_WIRE = 5;
        /// <summary>非選択ミラーの頂点。VD_UNSEL_MIRROR に従属。</summary>
        public const int VD_UNSEL_MIRROR_VERT = 6;
        public const int VD_SEL_WIRE     = 7;
        public const int VD_UNSEL_WIRE   = 8;
        public const int VD_SEL_VERT     = 9;
        public const int VD_UNSEL_VERT   = 10;
        public const int VD_SEL_BONE     = 11;
        public const int VD_UNSEL_BONE   = 12;
        public const int VD_SEL_MESH_ORIGIN   = 13;
        public const int VD_UNSEL_MESH_ORIGIN = 14;
        public const int VD_MIRROR_MESH_ORIGIN = 15;
        public const int VD_NORMAL       = 16;
        public const int VD_COUNT        = 17;

        /// <summary>左ペイン：ラッソ選択トグル。</summary>
        public Toggle LassoToggle { get; private set; }

        /// <summary>
        /// 左ペイン：性能ログ（CSV）の記録トグル。既定 OFF。
        ///
        /// ON の間だけ PLPerfLog が一定間隔で数値 1 行を CSV へ追記する。
        /// 出力先は Application.persistentDataPath で、開始時にログパネルへ通知される。
        /// 値は PlayerUiPrefs に永続化される。
        /// </summary>
        public Toggle PerfLogToggle { get; private set; }

        /// <summary>
        /// 左ペイン：軌道回転の中心をローカル原点（＝ピボット）にするトグル。既定 ON。
        ///
        /// ON のとき、メインビュー（透視ビューポート）の右ドラッグ回転は
        /// 選択オブジェクトのローカル原点の重心を軸に回る。ローカル原点は
        /// MeshContext.WorldMatrix の平行移動成分であり、PivotOffsetTool が
        /// 言う「ピボット原点」と同じ点。
        ///
        /// ON にした瞬間も視点は一切動かない。回した瞬間に軸が変わるだけである
        /// （Blender の Orbit Around Selection / Maya の Tumble Pivot と同じ扱い）。
        /// 視点を選択へ寄せる操作（Frame Selected 相当）とは別物。
        ///
        /// OrbitCenterToSelectionBtn と排他。釦を押すとここは自動的に OFF になり、
        /// 逆にここを ON に戻すと釦で確定した固定ピボットは解除される。
        /// </summary>
        public Toggle OrbitAroundLocalOriginToggle { get; private set; }

        /// <summary>
        /// 左ペイン：押した時点の選択の重心を軌道回転の中心として固定する押し釦。
        ///
        /// スナップショット動作。押した後に選択を変えても頂点を動かしても
        /// 中心は移動しない。更新したいときは再度押す。
        /// 要素（頂点/辺/面/線分）が未選択のときはローカル原点（ピボット）へ
        /// フォールバックする。押しても視点は動かない。
        /// </summary>
        public Button OrbitCenterToSelectionBtn { get; private set; }

        // 選択モード切替（頂点/辺/面/線分・非排他）。SelectionState.Mode を設定する。
        public Toggle SelModeVertexToggle { get; private set; }
        public Toggle SelModeEdgeToggle   { get; private set; }
        public Toggle SelModeFaceToggle   { get; private set; }
        public Toggle SelModeLineToggle   { get; private set; }

        // 辺／面／線分を選んだとき、その構成頂点も頂点選択へ入れるか（種別ごと）。
        // MoveToolHandler.ExpandLinkedVertices の展開対象を種別単位で切る。
        // OFF にすると「辺だけを選んだのに頂点まで選択色になる」状態を避けられる。
        public Toggle SelExpandEdgeToVertexToggle { get; private set; }
        public Toggle SelExpandFaceToVertexToggle { get; private set; }
        public Toggle SelExpandLineToVertexToggle { get; private set; }
        public Button        MaterialListBtn       { get; private set; }

        /// <summary>左ペイン：現在のタブの全オブジェクトを選択する。処理はメッシュリスト側と同じ。</summary>
        public Button        SelectAllObjectsBtn      { get; private set; }

        // ================================================================
        // Left ペイン
        // ================================================================

        private VisualElement BuildLeftPane()
        {
            var pane = MakePane(200f);
            pane.style.backgroundColor = PaneBg(0.15f);
            pane.style.color           = Col(1f);
            pane.style.flexDirection   = FlexDirection.Column;
            pane.style.overflow        = Overflow.Hidden;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow     = 1;
            scroll.style.paddingTop   = 6;
            scroll.style.paddingLeft  = 6;
            scroll.style.paddingRight = 6;
            pane.Add(scroll);

            StatusLabel = new Label("Status: -");
            StatusLabel.style.marginBottom = 6;
            StatusLabel.style.whiteSpace   = WhiteSpace.Normal;
            scroll.Add(StatusLabel);

            var undoRow = new VisualElement();
            undoRow.style.flexDirection = FlexDirection.Row;
            undoRow.style.marginBottom  = 6;
            UndoBtn = MakeBtn("Undo"); UndoBtn.style.flexGrow = 1; UndoBtn.style.marginRight = 2;
            RedoBtn = MakeBtn("Redo"); RedoBtn.style.flexGrow = 1; RedoBtn.style.marginLeft  = 2;
            undoRow.Add(UndoBtn); undoRow.Add(RedoBtn);
            scroll.Add(undoRow);

            scroll.Add(Separator());
            scroll.Add(Header("Models"));

            ModelSelectDropdown = new DropdownField();
            ModelSelectDropdown.style.marginBottom = 4;
            scroll.Add(ModelSelectDropdown);

            ModelListContainer = new VisualElement();
            scroll.Add(ModelListContainer);

            var listBtnRow = new VisualElement();
            listBtnRow.style.flexDirection = FlexDirection.Column;
            listBtnRow.style.marginTop     = 4;
            ModelListBtn = MakeBtn("モデルリスト");
            MeshListBtn  = MakeBtn("オブジェクトリスト");
            MaterialListBtn = MakeBtn("マテリアル（質感・色）");
            listBtnRow.Add(ModelListBtn);
            listBtnRow.Add(MeshListBtn);
            listBtnRow.Add(MaterialListBtn);
            scroll.Add(listBtnRow);

            scroll.Add(Separator());

            // 選択モード（頂点/辺/面/線分・非排他）— Lasso Select の上に配置。
            scroll.Add(Header("選択モード"));
            var selModeRow = new VisualElement();
            selModeRow.style.flexDirection = FlexDirection.Row;
            selModeRow.style.flexWrap      = Wrap.Wrap;   // 収まらない場合は折り返して見切れを防ぐ
            selModeRow.style.marginBottom  = 4;
            SelModeVertexToggle = new Toggle("頂点") { value = true };
            SelModeEdgeToggle   = new Toggle("辺")   { value = false };
            SelModeFaceToggle   = new Toggle("面")   { value = false };
            SelModeLineToggle   = new Toggle("線分") { value = false };
            foreach (var t in new[] { SelModeVertexToggle, SelModeEdgeToggle, SelModeFaceToggle, SelModeLineToggle })
            {
                t.style.color      = new StyleColor(Color.white);
                t.style.flexGrow   = 0;
                t.style.flexShrink = 0;
                t.style.marginRight = 12;
                // 既定の広い label min-width を解除し、ラベルとチェックの間隔を詰める
                // （これが無いとラベルとチェックが大きく離れ、右が見切れる）。
                if (t.labelElement != null)
                {
                    t.labelElement.style.minWidth    = 0;
                    t.labelElement.style.flexGrow    = 0;
                    t.labelElement.style.marginRight = 3;
                }
                selModeRow.Add(t);
            }
            scroll.Add(selModeRow);

            // 辺／面／線分を選んだとき、その構成頂点も頂点選択へ入れるか（種別ごと）。
            // 既定は 3 つとも ON（従来どおり展開する）。
            scroll.Add(Header("選んだ要素の頂点も選択する"));
            var selExpandRow = new VisualElement();
            selExpandRow.style.flexDirection = FlexDirection.Row;
            selExpandRow.style.flexWrap      = Wrap.Wrap;   // 収まらない場合は折り返して見切れを防ぐ
            selExpandRow.style.marginBottom  = 4;
            SelExpandEdgeToVertexToggle = new Toggle("辺→頂点")   { value = true };
            SelExpandFaceToVertexToggle = new Toggle("面→頂点")   { value = true };
            SelExpandLineToVertexToggle = new Toggle("線分→頂点") { value = true };
            foreach (var t in new[] { SelExpandEdgeToVertexToggle,
                                      SelExpandFaceToVertexToggle,
                                      SelExpandLineToVertexToggle })
            {
                t.style.color      = new StyleColor(Color.white);
                t.style.flexGrow   = 0;
                t.style.flexShrink = 0;
                t.style.marginRight = 12;
                // 選択モードのトグルと同じ詰め方（既定の広い label min-width を解除する）。
                if (t.labelElement != null)
                {
                    t.labelElement.style.minWidth    = 0;
                    t.labelElement.style.flexGrow    = 0;
                    t.labelElement.style.marginRight = 3;
                }
                selExpandRow.Add(t);
            }
            scroll.Add(selExpandRow);

            LassoToggle = new Toggle("Lasso Select") { value = false };
            LassoToggle.style.marginBottom = 4;
            scroll.Add(LassoToggle);

            // 性能ログ（CSV）。長時間の操作で次第に重くなる現象を追うための数値記録。
            // ON の間だけ一定間隔で 1 行ずつ追記する。テキストログ（右ペインの「ログ」）
            // とは別系統で、上限行数で捨てられないため長期の傾きが残る。
            PerfLogToggle = new Toggle("性能ログを記録（CSV）") { value = false };
            PerfLogToggle.style.color        = new StyleColor(Color.white);
            PerfLogToggle.style.marginBottom = 4;
            if (PerfLogToggle.labelElement != null)
            {
                PerfLogToggle.labelElement.style.minWidth    = 0;
                PerfLogToggle.labelElement.style.flexGrow    = 0;
                PerfLogToggle.labelElement.style.marginRight = 3;
            }
            scroll.Add(PerfLogToggle);

            // 軌道回転の中心（既定＝ローカル原点）。Lasso Select の直下に置く。
            OrbitAroundLocalOriginToggle = new Toggle("回転はローカル原点中心") { value = true };
            OrbitAroundLocalOriginToggle.style.color        = new StyleColor(Color.white);
            OrbitAroundLocalOriginToggle.style.marginBottom = 2;
            // 既定の広い label min-width を解除し、ラベルとチェックの間隔を詰める
            // （選択モードのトグル群と同じ処理）。
            if (OrbitAroundLocalOriginToggle.labelElement != null)
            {
                OrbitAroundLocalOriginToggle.labelElement.style.minWidth    = 0;
                OrbitAroundLocalOriginToggle.labelElement.style.flexGrow    = 0;
                OrbitAroundLocalOriginToggle.labelElement.style.marginRight = 3;
            }
            scroll.Add(OrbitAroundLocalOriginToggle);

            // 押した時点の選択重心を回転中心として固定する（スナップショット）。
            OrbitCenterToSelectionBtn = MakeBtn("現在の選択を中心に");
            OrbitCenterToSelectionBtn.style.marginBottom = 4;
            scroll.Add(OrbitCenterToSelectionBtn);

            // 法線の自動計算トグルと手動再計算ボタンは「法線」折りたたみへ移動した。

            // 現在のタブの全オブジェクトを選択する。
            // メッシュリスト内の同名ボタンと同じ処理を呼ぶだけで、判定は増やさない。
            SelectAllObjectsBtn = MakeBtn("すべてのオブジェクトを選択");
            SelectAllObjectsBtn.style.marginBottom = 4;
            scroll.Add(SelectAllObjectsBtn);

            scroll.Add(Separator());

            // 通常ボタン（カテゴリ別 Foldout）。ボタンを増やすときは
            // PlayerLayoutRoot.LeftPaneButtons.cs の BuildLeftPaneToolButtons を編集する。
            BuildLeftPaneToolButtons(scroll);

            scroll.Add(Separator());

            // 中央4画面の仕切り再配置（押下した瞬間に1回だけ実行）
            scroll.Add(Header("画面分割"));
            scroll.Add(BuildSplitModeGrid());

            scroll.Add(Header("Display (P/T/F/S)"));

            // 4ビューポート × VD_COUNT 項目のグリッド
            // 列: P=Perspective(slot0), T=Top(slot1), F=Front(slot2), S=Side(slot3)
            // 行: VD_* 定数の順序と一致させること。
            var vpHeaders  = new string[] { "P", "T", "F", "S" };
            var itemLabels = new string[]
            {
                "カリング",
                "選択Mesh",  "非選Mesh", "ミラー",
                "ミラー面",  "ミラー辺", "ミラー頂点",
                "選択辺",    "非選辺",
                "選択頂点",  "非選頂点",
                "選択Bone",  "非選Bone",
                "選択M原点", "非選M原点", "ミラーM原点",
                "法線",
            };
            // ViewportDisplaySettings.Default と一致させる
            var itemDefaults = new bool[]
            {
                true,  // カリング
                true,  // 選択Mesh
                true,  // 非選Mesh
                true,  // ミラー
                true,  // ミラー面
                true,  // ミラー辺
                true,  // ミラー頂点
                true,  // 選択辺
                true,  // 非選辺
                true,  // 選択頂点
                true,  // 非選頂点
                true,  // 選択Bone
                false, // 非選Bone
                true,  // 選択M原点
                true,  // 非選M原点
                false, // ミラーM原点（実体側と重なるため既定 OFF）
                false, // 法線（線分数が多いため既定 OFF）
            };

            // ヘッダ行
            var vpHeaderRow = new VisualElement();
            vpHeaderRow.style.flexDirection = FlexDirection.Row;
            vpHeaderRow.style.marginBottom  = 1;
            var vpHeaderSpacer = new VisualElement();
            vpHeaderSpacer.style.width = 54;
            vpHeaderRow.Add(vpHeaderSpacer);
            foreach (var h in vpHeaders)
            {
                var lbl = new Label(h);
                lbl.style.width             = 22;
                lbl.style.fontSize          = 9;
                lbl.style.unityTextAlign    = TextAnchor.MiddleCenter;
                vpHeaderRow.Add(lbl);
            }
            scroll.Add(vpHeaderRow);

            // トグル配列確保 [slot, item]
            ViewportDisplayToggles = new Toggle[4, VD_COUNT];
            for (int item = 0; item < VD_COUNT; item++)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.height        = 18;
                row.style.marginBottom  = 1;

                var lbl = new Label(itemLabels[item]);
                lbl.style.width            = 54;
                lbl.style.fontSize         = 9;
                lbl.style.unityTextAlign   = TextAnchor.MiddleLeft;
                row.Add(lbl);

                for (int vp = 0; vp < 4; vp++)
                {
                    var t = new Toggle { value = itemDefaults[item] };
                    t.style.width      = 22;
                    t.style.height     = 18;
                    t.style.minWidth   = 0;
                    t.style.flexShrink = 0;
                    t.style.marginLeft = 0;
                    t.style.marginRight= 0;
                    // パネルアタッチ後に内部 Label を非表示にする
                    // （コンストラクタ直後は内部子要素が未初期化のため Q<Label>() が null を返す）
                    t.RegisterCallback<AttachToPanelEvent>(_ =>
                    {
                        var inner = t.Q<Label>();
                        if (inner != null)
                        {
                            inner.style.display  = DisplayStyle.None;
                            inner.style.minWidth = 0;
                            inner.style.width    = 0;
                        }
                    });
                    ViewportDisplayToggles[vp, item] = t;
                    row.Add(t);
                }
                scroll.Add(row);
            }

            return pane;
        }
    }
}
