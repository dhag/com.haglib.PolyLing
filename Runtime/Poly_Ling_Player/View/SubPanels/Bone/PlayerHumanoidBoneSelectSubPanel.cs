// PlayerHumanoidBoneSelectSubPanel.cs
// ヒューマノイドボーン選択（右ペイン上区画の常駐リスト）。
// 体・頭・左手・右手の図の上に Humanoid ボーンの丸印を置き、丸印を押してボーンを選ぶ。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置
//
// 【割当の正本】
//   どの Humanoid ボーンがどのボーン（master 索引）かは IModelView.HumanoidBoneIndexMap
//   （ModelContext.HumanoidMapping の写し）で引く。ボーン個別の HumanBodyBone は
//   保存・読込時にしか同期しないので使わない（HumanoidBoneMapping.cs 冒頭の規約）。
//
// 【選択】
//   SelectMeshCommand（分類＝ボーン）だけを送る。Undo・表示の更新はその経路に任せる。
//   押す＝その 1 本に置き換え／Shift＋押す＝足す／Ctrl＋押す＝足し引き。
//
// 【左右】
//   図は正面から見た絵。キャラクター自身の右（Right〜）は画面の左に置く。
//   手の図は T ポーズで前から見た形（左手は手首が画面の左、右手は左右反転）。
//
// 【背景と丸印の位置の差し替え】
//   図ごとに背景画像（PNG/JPG）と位置表（CSV）を指定できる。パスは RecentPaths に残る。
//   位置表は 1 行 1 ボーン「ボーン名,x,y」。x・y は図の左上を 0、右下を 1 とする割合。
//   ボーン名は HumanoidBoneMapping.AllHumanoidBones の表記。# で始まる行は無視する。
//   位置表を指定すると、その図の丸印は位置表に書いたボーンだけになる。
//   背景画像を指定すると、図の縦横比は画像に合わせ、既定の絵は描かない。
//   「位置表の書き出し」で今の位置を CSV に書き出せるので、それを直して使う。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    public class PlayerHumanoidBoneSelectSubPanel
    {
        /// <summary>モデルの窓口（操作経路統一計画.md E）。</summary>
        public Func<Poly_Ling.View.IModelView> GetModel;
        public Action<PanelCommand> SendCommand;
        public Func<int>            GetModelIndex;

        // ================================================================
        // 図の種類
        // ================================================================

        private enum Chart { Body = 0, Head = 1, LeftHand = 2, RightHand = 3 }

        private static readonly string[] ChartKeys   = { "Body", "Head", "LeftHand", "RightHand" };
        private static readonly string[] ChartLabels = { "体", "頭", "左手", "右手" };

        /// <summary>既定の図の縦横比（高さ÷幅）。背景画像を指定したときは画像に合わせる。</summary>
        private static readonly float[] DefaultAspect = { 1.45f, 1.0f, 0.8f, 0.8f };

        /// <summary>図の幅（px）。</summary>
        private const float CanvasWidth = 230f;

        // ================================================================
        // UI
        // ================================================================

        // UI 自動操作の ID は "humanoidBoneSelect.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("tab.body", Safety = UiSafety.SafeWrite, Description = "体の図に切り替える")]
        private Button _tabBody;
        [UiControl("tab.head", Safety = UiSafety.SafeWrite, Description = "頭の図に切り替える")]
        private Button _tabHead;
        [UiControl("tab.leftHand", Safety = UiSafety.SafeWrite, Description = "左手の図に切り替える")]
        private Button _tabLeftHand;
        [UiControl("tab.rightHand", Safety = UiSafety.SafeWrite, Description = "右手の図に切り替える")]
        private Button _tabRightHand;

        /// <summary>図（背景＋丸印）。丸印は割当に合わせて作り直す行として扱う。</summary>
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _canvas;

        [UiControl("hover", Safety = UiSafety.ReadOnly, Description = "丸印に乗せたボーンの名前と割当先")]
        private Label _hoverLabel;
        [UiControl("state", Safety = UiSafety.ReadOnly, Description = "割当の状態（割当なしのときの案内を含む）")]
        private Label _stateLabel;

        [UiControl("bgPath", Description = "今の図の背景画像のパス（空なら既定の絵）")]
        private TextField _bgPathField;
        [UiControl("browseBg", Safety = UiSafety.UserOnly, Description = "背景画像を選ぶダイアログを開く")]
        private Button _btnBrowseBg;
        [UiControl("layoutPath", Description = "今の図の位置表（CSV）のパス（空なら既定の位置）")]
        private TextField _layoutPathField;
        [UiControl("browseLayout", Safety = UiSafety.UserOnly, Description = "位置表を選ぶダイアログを開く")]
        private Button _btnBrowseLayout;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "欄のパスで背景と位置表を読み直す")]
        private Button _btnApply;
        [UiControl("reset", Safety = UiSafety.SafeWrite, Description = "今の図を既定の絵と位置に戻す（パスを空にする）")]
        private Button _btnReset;
        [UiControl("exportLayout", Safety = UiSafety.UserOnly, Description = "今の図の位置表を CSV に書き出す（保存ダイアログを開く）")]
        private Button _btnExportLayout;
        [UiControl("customState", Safety = UiSafety.ReadOnly, Description = "背景・位置表の読み込み結果")]
        private Label _customStateLabel;

        private readonly Button[] _tabs = new Button[4];
        private Chart _chart = Chart.Body;

        // 図ごとの差し替え（読めたものだけ入る）
        private readonly Texture2D[] _bgTex = new Texture2D[4];
        private readonly List<(string bone, Vector2 pos)>[] _customLayout = new List<(string, Vector2)>[4];
        private readonly string[] _customError = new string[4];

        /// <summary>今の図の丸印（ボーン名 → 要素）。</summary>
        private readonly Dictionary<string, VisualElement> _markers = new Dictionary<string, VisualElement>();

        private const string TabKey = "HumanoidBoneSelect.Tab";
        private static string BgKey(Chart c)     => "HumanoidBoneSelect.Bg." + ChartKeys[(int)c];
        private static string LayoutKey(Chart c) => "HumanoidBoneSelect.Layout." + ChartKeys[(int)c];

        private Poly_Ling.View.IModelView Model => GetModel?.Invoke();

        // ── 色 ────────────────────────────────────────────────
        private static readonly Color CanvasBg      = new Color(0.33f, 0.33f, 0.33f);
        private static readonly Color BodyFill      = new Color(0.17f, 0.27f, 0.17f);
        private static readonly Color BodyEdge      = new Color(0.20f, 0.85f, 0.20f);
        private static readonly Color MarkAssigned  = new Color(0.20f, 0.95f, 0.20f);
        private static readonly Color MarkSelected  = new Color(1.00f, 0.62f, 0.10f);
        private static readonly Color MarkMissing   = new Color(0.62f, 0.62f, 0.62f);

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("ヒューマノイドボーン選択"));

            // タブ
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom  = 4;
            _tabBody      = MakeTab(Chart.Body);
            _tabHead      = MakeTab(Chart.Head);
            _tabLeftHand  = MakeTab(Chart.LeftHand);
            _tabRightHand = MakeTab(Chart.RightHand);
            _tabs[0] = _tabBody; _tabs[1] = _tabHead; _tabs[2] = _tabLeftHand; _tabs[3] = _tabRightHand;
            foreach (var t in _tabs) tabRow.Add(t);
            root.Add(tabRow);

            // 図
            var canvasHolder = new VisualElement();
            canvasHolder.style.alignItems = Align.Center;
            _canvas = new VisualElement();
            _canvas.style.width           = CanvasWidth;
            _canvas.style.backgroundColor = CanvasBg;
            _canvas.style.position        = Position.Relative;
            _canvas.style.overflow        = Overflow.Hidden;
            _canvas.generateVisualContent += DrawDefaultFigure;
            canvasHolder.Add(_canvas);
            root.Add(canvasHolder);

            _hoverLabel = new Label(" ");
            _hoverLabel.style.marginTop  = 2;
            _hoverLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_hoverLabel);

            _stateLabel = new Label();
            _stateLabel.style.fontSize   = 10;
            _stateLabel.style.whiteSpace = WhiteSpace.Normal;
            _stateLabel.style.marginBottom = 4;
            root.Add(_stateLabel);

            // 差し替え
            var fold = new Foldout { text = "背景と丸印の位置", value = false };
            fold.Add(SecLabel("背景画像（PNG/JPG）"));
            _bgPathField = new TextField();
            fold.Add(PlayerIoUiKit.PathRow(_bgPathField, OnBrowseBg, out _btnBrowseBg));
            fold.Add(SecLabel("位置表（CSV：ボーン名,x,y　x・y は 0〜1）"));
            _layoutPathField = new TextField();
            fold.Add(PlayerIoUiKit.PathRow(_layoutPathField, OnBrowseLayout, out _btnBrowseLayout));

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _btnApply        = new Button(OnApplyCustom)  { text = "読み直す" };
            _btnReset        = new Button(OnResetCustom)  { text = "既定に戻す" };
            _btnExportLayout = new Button(OnExportLayout) { text = "位置表の書き出し" };
            foreach (var b in new[] { _btnApply, _btnReset, _btnExportLayout })
            {
                b.style.flexGrow = 1; b.style.flexBasis = 0; b.style.minWidth = 0;
                btnRow.Add(b);
            }
            fold.Add(btnRow);

            _customStateLabel = new Label();
            _customStateLabel.style.fontSize   = 10;
            _customStateLabel.style.whiteSpace = WhiteSpace.Normal;
            fold.Add(_customStateLabel);
            root.Add(fold);

            // 起動時：全図の差し替えを読み込み、前回の図を開く
            for (int i = 0; i < 4; i++) LoadCustom((Chart)i);
            string savedTab = RecentPaths.Get(TabKey);
            int tabIdx = Array.IndexOf(ChartKeys, savedTab);
            SelectChart(tabIdx >= 0 ? (Chart)tabIdx : Chart.Body);
        }

        private Button MakeTab(Chart c)
        {
            var b = new Button(() => SelectChart(c)) { text = ChartLabels[(int)c] };
            b.style.flexGrow = 1; b.style.flexBasis = 0; b.style.minWidth = 0;
            b.style.marginLeft = b.style.marginRight = 1;
            return b;
        }

        // ================================================================
        // 図の切り替え・再構築
        // ================================================================

        private void SelectChart(Chart c)
        {
            _chart = c;
            RecentPaths.Set(TabKey, ChartKeys[(int)c]);
            for (int i = 0; i < 4; i++)
                _tabs[i].style.backgroundColor = (i == (int)c) ? PlayerLayoutRoot.BtnActiveColor : PlayerLayoutRoot.BtnInactiveColor;

            _bgPathField.SetValueWithoutNotify(RecentPaths.Get(BgKey(c)));
            _layoutPathField.SetValueWithoutNotify(RecentPaths.Get(LayoutKey(c)));

            RebuildCanvas();
            Refresh();
        }

        /// <summary>背景・大きさ・丸印を今の図で組み直す。</summary>
        private void RebuildCanvas()
        {
            int ci = (int)_chart;
            var tex = _bgTex[ci];

            float aspect = (tex != null && tex.width > 0) ? (float)tex.height / tex.width : DefaultAspect[ci];
            _canvas.style.height = CanvasWidth * aspect;

            if (tex != null)
            {
                _canvas.style.backgroundImage = new StyleBackground(tex);
                _canvas.style.backgroundSize  = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                _canvas.style.backgroundImage = StyleKeyword.None;
            }

            _canvas.Clear();
            _markers.Clear();
            foreach (var (bone, pos) in CurrentLayout())
            {
                var m = MakeMarker(bone);
                m.style.left = pos.x * CanvasWidth - MarkerSize * 0.5f;
                m.style.top  = pos.y * CanvasWidth * aspect - MarkerSize * 0.5f;
                _canvas.Add(m);
                _markers[bone] = m;
            }
            _canvas.MarkDirtyRepaint();
            UpdateCustomStateLabel();
        }

        private List<(string bone, Vector2 pos)> CurrentLayout()
            => _customLayout[(int)_chart] ?? DefaultLayout(_chart);

        // ================================================================
        // 丸印
        // ================================================================

        private const float MarkerSize = 13f;

        private VisualElement MakeMarker(string bone)
        {
            var m = new VisualElement();
            m.style.position = Position.Absolute;
            m.style.width  = MarkerSize;
            m.style.height = MarkerSize;
            float r = MarkerSize * 0.5f;
            m.style.borderTopLeftRadius = m.style.borderTopRightRadius =
            m.style.borderBottomLeftRadius = m.style.borderBottomRightRadius = r;
            m.style.borderTopWidth = m.style.borderBottomWidth =
            m.style.borderLeftWidth = m.style.borderRightWidth = 2;

            m.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                OnMarkerClicked(bone, e.shiftKey, e.ctrlKey || e.commandKey);
                e.StopPropagation();
            });
            m.RegisterCallback<PointerEnterEvent>(_ => ShowHover(bone));
            m.RegisterCallback<PointerLeaveEvent>(_ => _hoverLabel.text = " ");
            return m;
        }

        private static void SetMarkerColor(VisualElement m, Color border, Color fill, float opacity)
        {
            m.style.borderTopColor = m.style.borderBottomColor =
            m.style.borderLeftColor = m.style.borderRightColor = border;
            m.style.backgroundColor = fill;
            m.style.opacity = opacity;
        }

        private void ShowHover(string bone)
        {
            var map = Model?.HumanoidBoneIndexMap;
            if (map != null && map.TryGetValue(bone, out int idx))
                _hoverLabel.text = $"{bone}  →  {BoneName(idx)}";
            else
                _hoverLabel.text = $"{bone}  （未割当）";
        }

        private string BoneName(int masterIndex)
        {
            var list = Model?.BoneList;
            if (list != null)
                foreach (var v in list)
                    if (v != null && v.MasterIndex == masterIndex) return v.Name;
            return $"#{masterIndex}";
        }

        private void OnMarkerClicked(string bone, bool add, bool toggle)
        {
            var model = Model;
            var map   = model?.HumanoidBoneIndexMap;
            if (map == null || !map.TryGetValue(bone, out int idx) || idx < 0) return;

            var next = new List<int>();
            if (add || toggle)
            {
                var cur = model.SelectedBoneIndices;
                if (cur != null) next.AddRange(cur);
                if (next.Contains(idx)) { if (toggle) next.Remove(idx); }
                else next.Add(idx);
            }
            else
            {
                next.Add(idx);
            }

            SendCommand?.Invoke(new SelectMeshCommand(GetModelIndex?.Invoke() ?? 0, MeshCategory.Bone, next.ToArray()));
        }

        // ================================================================
        // 更新（割当・選択の変化）
        // ================================================================

        public void Refresh()
        {
            if (_canvas == null) return;
            // 全体の配色（ApplyDarkTheme）が構築後にボタン色を上書きするので、ここで塗り直す。
            for (int i = 0; i < 4; i++)
                _tabs[i].style.backgroundColor = (i == (int)_chart) ? PlayerLayoutRoot.BtnActiveColor : PlayerLayoutRoot.BtnInactiveColor;
            var model = Model;
            var map   = model?.HumanoidBoneIndexMap;
            var sel   = new HashSet<int>(model?.SelectedBoneIndices ?? Array.Empty<int>());

            int assigned = 0;
            foreach (var kv in _markers)
            {
                if (map != null && map.TryGetValue(kv.Key, out int idx) && idx >= 0)
                {
                    assigned++;
                    if (sel.Contains(idx)) SetMarkerColor(kv.Value, Color.white, MarkSelected, 1f);
                    else                   SetMarkerColor(kv.Value, MarkAssigned, BodyFill, 1f);
                }
                else
                {
                    SetMarkerColor(kv.Value, MarkMissing, Color.clear, 0.6f);
                }
            }

            if (model == null)
                _stateLabel.text = "モデルがありません。";
            else if (map == null || map.Count == 0)
                _stateLabel.text = "Humanoid の割当がありません（左ペイン「ボーン・モーフ」→「アバター用ヒューマンマッピング」で割り当てる）。";
            else
                _stateLabel.text = $"この図：割当済み {assigned} / {_markers.Count}（灰色は未割当）。Shift＋押すで追加、Ctrl＋押すで足し引き。";
        }

        // ================================================================
        // 既定の絵
        // ================================================================

        /// <summary>背景画像が無いときだけ、既定の位置をつないだ人形を描く。</summary>
        private void DrawDefaultFigure(MeshGenerationContext ctx)
        {
            int ci = (int)_chart;
            if (_bgTex[ci] != null) return;
            var rect = _canvas.contentRect;
            float w = rect.width, h = rect.height;
            if (w <= 0 || h <= 0) return;

            var pos = new Dictionary<string, Vector2>();
            foreach (var (b, p) in DefaultLayout(_chart)) pos[b] = new Vector2(p.x * w, p.y * h);

            var p2 = ctx.painter2D;
            p2.lineCap  = LineCap.Round;
            p2.lineJoin = LineJoin.Round;

            switch (_chart)
            {
                case Chart.Body:
                {
                    // 頭
                    Disc(p2, pos["Head"] + new Vector2(0, -0.035f * h), 0.085f * w);
                    // 胴と首
                    float t = 0.17f * w;
                    Chain(p2, pos, t, "Hips", "Spine", "Chest", "UpperChest", "Neck");
                    // 腕
                    float a = 0.065f * w;
                    Chain(p2, pos, a, "UpperChest", "RightShoulder", "RightUpperArm", "RightLowerArm", "RightHand");
                    Chain(p2, pos, a, "UpperChest", "LeftShoulder",  "LeftUpperArm",  "LeftLowerArm",  "LeftHand");
                    // 脚
                    float l = 0.085f * w;
                    Chain(p2, pos, l, "Hips", "RightUpperLeg", "RightLowerLeg", "RightFoot", "RightToes");
                    Chain(p2, pos, l, "Hips", "LeftUpperLeg",  "LeftLowerLeg",  "LeftFoot",  "LeftToes");
                    break;
                }
                case Chart.Head:
                {
                    Chain(p2, pos, 0.28f * w, "Neck", "Jaw");
                    Disc(p2, new Vector2(0.5f * w, 0.47f * h), 0.33f * w);
                    break;
                }
                case Chart.LeftHand:
                case Chart.RightHand:
                {
                    string s = _chart == Chart.LeftHand ? "Left " : "Right ";
                    string hand = _chart == Chart.LeftHand ? "LeftHand" : "RightHand";
                    // 手のひら（手首・親指の付け根・各指の付け根を結んだ面を太い縁で丸める）
                    Palm(p2, pos, 0.12f * w, hand, s + "Thumb Proximal", s + "Index Proximal",
                         s + "Middle Proximal", s + "Ring Proximal", s + "Little Proximal");
                    // 指（親指は手首から、他の指は付け根から）
                    float fw = 0.065f * w;
                    Chain(p2, pos, fw, hand, s + "Thumb Proximal", s + "Thumb Intermediate", s + "Thumb Distal");
                    foreach (var f in new[] { "Index", "Middle", "Ring", "Little" })
                        Chain(p2, pos, fw, s + f + " Proximal", s + f + " Intermediate", s + f + " Distal");
                    break;
                }
            }
        }

        /// <summary>点列を太い線でつなぐ（縁取り→塗りの 2 回描き）。</summary>
        private static void Chain(Painter2D p2, Dictionary<string, Vector2> pos, float width, params string[] bones)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                p2.strokeColor = pass == 0 ? BodyEdge : BodyFill;
                p2.lineWidth   = pass == 0 ? width + 3f : width;
                p2.BeginPath();
                bool first = true;
                foreach (var b in bones)
                {
                    if (!pos.TryGetValue(b, out var p)) continue;
                    if (first) { p2.MoveTo(p); first = false; }
                    else p2.LineTo(p);
                }
                p2.Stroke();
            }
        }

        /// <summary>点列を閉じた面として描く。縁を太く描いてから塗るので角が丸くなる。</summary>
        private static void Palm(Painter2D p2, Dictionary<string, Vector2> pos, float edge, params string[] bones)
        {
            var pts = new List<Vector2>();
            foreach (var b in bones) if (pos.TryGetValue(b, out var p)) pts.Add(p);
            if (pts.Count < 3) return;

            for (int pass = 0; pass < 2; pass++)
            {
                p2.BeginPath();
                p2.MoveTo(pts[0]);
                for (int i = 1; i < pts.Count; i++) p2.LineTo(pts[i]);
                p2.ClosePath();
                if (pass == 1)
                {
                    p2.fillColor = BodyFill;
                    p2.Fill();
                }
                p2.strokeColor = pass == 0 ? BodyEdge : BodyFill;
                p2.lineWidth   = pass == 0 ? edge + 3f : edge;
                p2.Stroke();
            }
        }

        private static void Disc(Painter2D p2, Vector2 c, float r)
        {
            p2.BeginPath();
            p2.Arc(c, r, 0f, 360f);
            p2.fillColor = BodyFill;
            p2.Fill();
            p2.strokeColor = BodyEdge;
            p2.lineWidth   = 1.5f;
            p2.Stroke();
        }

        // ================================================================
        // 既定の位置（図の左上 0、右下 1。キャラクターの右は画面の左）
        // ================================================================

        private static List<(string, Vector2)> DefaultLayout(Chart c)
        {
            var l = new List<(string, Vector2)>();
            void A(string b, float x, float y) => l.Add((b, new Vector2(x, y)));

            switch (c)
            {
                case Chart.Body:
                    A("Head", 0.50f, 0.115f);
                    A("Neck", 0.50f, 0.175f);
                    A("UpperChest", 0.50f, 0.235f);
                    A("Chest", 0.50f, 0.300f);
                    A("Spine", 0.50f, 0.370f);
                    A("Hips", 0.50f, 0.440f);
                    A("RightShoulder", 0.42f, 0.215f);
                    A("LeftShoulder",  0.58f, 0.215f);
                    A("RightUpperArm", 0.33f, 0.235f);
                    A("LeftUpperArm",  0.67f, 0.235f);
                    A("RightLowerArm", 0.25f, 0.355f);
                    A("LeftLowerArm",  0.75f, 0.355f);
                    A("RightHand", 0.18f, 0.470f);
                    A("LeftHand",  0.82f, 0.470f);
                    A("RightUpperLeg", 0.42f, 0.475f);
                    A("LeftUpperLeg",  0.58f, 0.475f);
                    A("RightLowerLeg", 0.41f, 0.690f);
                    A("LeftLowerLeg",  0.59f, 0.690f);
                    A("RightFoot", 0.41f, 0.900f);
                    A("LeftFoot",  0.59f, 0.900f);
                    A("RightToes", 0.38f, 0.955f);
                    A("LeftToes",  0.62f, 0.955f);
                    break;

                case Chart.Head:
                    A("RightEye", 0.37f, 0.42f);
                    A("LeftEye",  0.63f, 0.42f);
                    A("Head", 0.50f, 0.58f);
                    A("Jaw",  0.50f, 0.74f);
                    A("Neck", 0.50f, 0.93f);
                    break;

                case Chart.LeftHand:
                case Chart.RightHand:
                {
                    bool left = c == Chart.LeftHand;
                    string s = left ? "Left " : "Right ";
                    // 左手の座標で書き、右手は x を反転する
                    void H(string b, float x, float y) => A(b, left ? x : 1f - x, y);
                    H(left ? "LeftHand" : "RightHand", 0.08f, 0.55f);
                    H(s + "Thumb Proximal",     0.26f, 0.38f);
                    H(s + "Thumb Intermediate", 0.36f, 0.24f);
                    H(s + "Thumb Distal",       0.45f, 0.12f);
                    float[] fy   = { 0.36f, 0.50f, 0.64f, 0.78f };
                    float[] fLen = { 1.00f, 1.05f, 0.97f, 0.82f };
                    string[] fn  = { "Index", "Middle", "Ring", "Little" };
                    for (int i = 0; i < 4; i++)
                    {
                        float x0 = 0.52f, step = 0.14f * fLen[i];
                        H(s + fn[i] + " Proximal",     x0,            fy[i]);
                        H(s + fn[i] + " Intermediate", x0 + step,     fy[i]);
                        H(s + fn[i] + " Distal",       x0 + step * 2, fy[i]);
                    }
                    break;
                }
            }
            return l;
        }

        // ================================================================
        // 背景・位置表の差し替え
        // ================================================================

        private void OnBrowseBg()
        {
            string cur = _bgPathField.value;
            string dir = string.IsNullOrEmpty(cur) ? "" : Path.GetDirectoryName(cur);
            string path = PLEditorBridge.I.OpenFilePanel("背景画像を選ぶ", dir, "png,jpg,jpeg");
            if (string.IsNullOrEmpty(path)) return;
            _bgPathField.value = path;
            OnApplyCustom();
        }

        private void OnBrowseLayout()
        {
            string cur = _layoutPathField.value;
            string dir = string.IsNullOrEmpty(cur) ? "" : Path.GetDirectoryName(cur);
            string path = PLEditorBridge.I.OpenFilePanel("位置表を選ぶ", dir, "csv");
            if (string.IsNullOrEmpty(path)) return;
            _layoutPathField.value = path;
            OnApplyCustom();
        }

        private void OnApplyCustom()
        {
            RecentPaths.Set(BgKey(_chart),     _bgPathField.value ?? "");
            RecentPaths.Set(LayoutKey(_chart), _layoutPathField.value ?? "");
            LoadCustom(_chart);
            RebuildCanvas();
            Refresh();
        }

        private void OnResetCustom()
        {
            _bgPathField.SetValueWithoutNotify("");
            _layoutPathField.SetValueWithoutNotify("");
            OnApplyCustom();
        }

        private void OnExportLayout()
        {
            string path = PLEditorBridge.I.SaveFilePanel(
                "位置表の書き出し", "", "HumanoidBoneLayout_" + ChartKeys[(int)_chart], "csv");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine("# ボーン名,x,y（図の左上 0・右下 1。キャラクターの右は画面の左）");
            foreach (var (bone, pos) in CurrentLayout())
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.###},{2:0.###}", bone, pos.x, pos.y));
            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                _customStateLabel.text = $"書き出しました: {path}";
            }
            catch (Exception e)
            {
                _customStateLabel.text = $"書き出しに失敗しました: {e.Message}";
            }
        }

        /// <summary>図 c の背景画像と位置表を、保存済みのパスから読み直す。</summary>
        private void LoadCustom(Chart c)
        {
            int ci = (int)c;
            var errors = new List<string>();

            if (_bgTex[ci] != null) { UnityEngine.Object.Destroy(_bgTex[ci]); _bgTex[ci] = null; }
            string bg = RecentPaths.Get(BgKey(c));
            if (!string.IsNullOrEmpty(bg))
            {
                _bgTex[ci] = LoadTexture(bg, out string err);
                if (err != null) errors.Add(err);
            }

            _customLayout[ci] = null;
            string lay = RecentPaths.Get(LayoutKey(c));
            if (!string.IsNullOrEmpty(lay))
            {
                _customLayout[ci] = LoadLayout(lay, out string err);
                if (err != null) errors.Add(err);
            }

            _customError[ci] = errors.Count > 0 ? string.Join("\n", errors) : null;
        }

        /// <summary>下絵（UnderlayConfig.Load）と同じ読み方。</summary>
        private static Texture2D LoadTexture(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) { error = $"画像ファイルがありません: {path}"; return null; }
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(tex);
                    error = $"画像として読めません: {path}";
                    return null;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);
                return tex;
            }
            catch (Exception e)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                error = $"画像の読み込みに失敗しました: {e.Message}";
                return null;
            }
        }

        private static List<(string, Vector2)> LoadLayout(string path, out string error)
        {
            error = null;
            if (!File.Exists(path)) { error = $"位置表がありません: {path}"; return null; }
            try
            {
                var list = new List<(string, Vector2)>();
                var bad  = new List<string>();
                int lineNo = 0;
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    lineNo++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var cells = line.Split(',');
                    if (cells.Length < 3
                        || !HumanoidBoneMapping.IsHumanoidBoneName(cells[0].Trim())
                        || !float.TryParse(cells[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        || !float.TryParse(cells[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                    {
                        bad.Add(lineNo.ToString());
                        continue;
                    }
                    list.Add((cells[0].Trim(), new Vector2(x, y)));
                }
                if (bad.Count > 0) error = $"位置表の読めない行: {string.Join(", ", bad)} 行目";
                if (list.Count == 0) { error = (error ?? "") + $"\n位置表に有効な行がありません: {path}"; return null; }
                return list;
            }
            catch (Exception e)
            {
                error = $"位置表の読み込みに失敗しました: {e.Message}";
                return null;
            }
        }

        private void UpdateCustomStateLabel()
        {
            int ci = (int)_chart;
            string bg  = _bgTex[ci] != null ? $"背景：{_bgTex[ci].name}（{_bgTex[ci].width}×{_bgTex[ci].height}）" : "背景：既定の絵";
            string lay = _customLayout[ci] != null ? $"位置：位置表（{_customLayout[ci].Count} 件）" : "位置：既定";
            _customStateLabel.text = $"{bg}／{lay}" + (_customError[ci] != null ? "\n" + _customError[ci] : "");
        }

        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
