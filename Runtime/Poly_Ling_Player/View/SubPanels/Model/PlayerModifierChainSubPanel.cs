// PlayerModifierChainSubPanel.cs
// モディファイアパネル。選択中のオブジェクトの履歴とモディファイアを出す。
// Blender の Modifiers タブと Maya の INPUTS に当たる。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 5 章・段 1
//
// 【対象】
//   編集対象のオブジェクト（IModelView.ActiveMeshIndex）。結果は選べない（元へ回る。段 2）ので、対象は処理列の元。
//   既存のサブディビジョン（subdivideMesh）のスタックの親なら、種類「サブディビジョン」のカードで出す。
//   元を出力に持つ生成スタックは「履歴」のカードで出す（IModelView.ModifierStacksOf）。
//
// 【操作はすべてコマンド】
//   追加 addMeshModifier・設定 setMeshModifierArgs・並べ替え moveMeshModifier・有効 setMeshModifierEnabled・
//   削除 removeMeshModifier・すべて適用 applyMeshModifierChain・元の線の表示 setModifierCageVisible。
//   履歴の設定は setModifierStackStepArgs、履歴の削除は deleteModifierStack。画面から直接モデルを書かない。
//   スライダーを動かしている間は setMeshModifierArgs の preview=true（Undo を積まない）、
//   離したときに preview=false（受け口が Undo 1 件にまとめる）。
//
// 【カードと UI 自動操作の ID】
//   カードは縦に並ぶので、同じ種類の欄が同時に複数ある。選択中のカード（Blender の active modifier）の欄は
//   "modifierChain.arg.<キー>" のような短い ID で、他のカードの欄は "modifierChain.item.<項目 ID>.arg.<キー>" で引く。
//   選択中のカードを変えても作り直さず、ID だけを付け替える（ドラッグ中のスライダーを壊さないため）。
//   足したばかりの項目は選択中になる（Blender と同じ）。
//
// 【読み直し】
//   カードの組（項目・種類・キー）が変わったときだけ作り直し、それ以外は値だけ合わせる。
//   読み直しは選択が変わるたびに走るので、要更新の判定をしない写し（ModifierStacksOf）を使う。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Modifiers;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerModifierChainSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<IProjectView> GetProject;
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        /// <summary>ビューのハンドルで動かしている項目（スタック名・項目・種類）。無ければ Kind が Off。</summary>
        public Func<(string StackName, string ItemId, ModifierGizmoKind Kind)> GetActiveGizmo;

        private IProjectView Project      => GetProject?.Invoke();
        private IModelView   CurrentModel => Project?.CurrentModel;
        private int          ModelIndex   => Project?.CurrentModelIndex ?? 0;

        // ================================================================
        // ボタンや入力欄（UI 自動操作の ID は "modifierChain.<Id>"）
        // ================================================================

        [UiControl("target", Safety = UiSafety.ReadOnly, Description = "対象のオブジェクト")]
        private Label _targetLabel;
        [UiControl("addMenu", Safety = UiSafety.SafeWrite, Description = "モディファイアの追加メニューを開く・閉じる")]
        private Button _btnAddMenu;
        [UiControl("showCage", Description = "元の線（辺と頂点）を表示する。隠している間は元の頂点を選べない")]
        private Toggle _showCageToggle;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "すべてのモディファイアを適用する。結果の形が元の名前のオブジェクトになる")]
        private Button _btnApply;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の注意")]
        private Label _statusLabel;

        [UiControl("sweepFollow", Safety = UiSafety.SafeWrite, Description = "パイプとカーブを、複製せず元の辺や面を読んで作り、元の編集に追随させる")]
        private Toggle _sweepFollow;

        [UiControl(Ignore = true)] private VisualElement _usageBox;
        [UiControl(Ignore = true)] private VisualElement _historyBox;
        [UiControl(Ignore = true)] private VisualElement _menuBox;
        [UiControl(Ignore = true)] private VisualElement _cardsBox;

        /// <summary>追加メニューの種類のボタン。ID は "add.&lt;種類名&gt;"。</summary>
        private readonly UiDynamicControls _menuControls = new UiDynamicControls { IsPossibleId = IsPossibleMenuId };

        /// <summary>カードと履歴の欄。ID はファイル頭の注記。</summary>
        private readonly UiDynamicControls _cardControls = new UiDynamicControls { IsPossibleId = IsPossibleCardId };

        private const string ItemIdHead = "item.";

        /// <summary>カードの固定の欄（選択中のカードの短い ID）。</summary>
        private static readonly HashSet<string> CardFixedIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "select", "fold", "up", "down", "enabled", "remove", "itemState",
            "scope.group", "scope.invert", "scope.fromSelection", "scope.info",
            "detailFold", "detailKey", "detailValue", "detailWrite", "mirrorAsAttribute",
            "grip", "menu", "applyOne", "applyUpTo", "duplicate", "moveFirst", "moveLast", "copyToSelected", "showUpTo",
            "gizmo.axisMove", "gizmo.axisRotate", "gizmo.latticePoints",
        };

        private static bool IsPossibleMenuId(string id)
        {
            const string Head = "add.";
            if (string.IsNullOrEmpty(id) || !id.StartsWith(Head, StringComparison.Ordinal)) return false;
            string kind = id.Substring(Head.Length);
            return kind == "{0}" || kind == "pipe" || kind == "curve" || MeshModifierRegistry.Find(kind) != null;
        }

        /// <summary>
        /// カードを作るまで無い欄の ID か。欄の有無は種類の宣言で決まる（ParamFieldSpec.HasField）。
        /// </summary>
        private static bool IsPossibleCardId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (CardFixedIds.Contains(id)) return true;
            // 履歴カードの「揺れ鎖を作る」（帯を読む履歴を選ぶまで無い）。
            if (id.StartsWith("history.", StringComparison.Ordinal) && id.EndsWith(".chain", StringComparison.Ordinal)) return true;
            // 履歴カードのカーブの「点を増やす」（パイプの履歴を選ぶまで無い）。
            if (id.StartsWith("history.", StringComparison.Ordinal) && id.Contains(".curvePoints.")) return true;
            const string Head = "arg.";
            if (!id.StartsWith(Head, StringComparison.Ordinal)) return false;
            string rest = id.Substring(Head.Length);
            foreach (var info in MeshModifierRegistry.All)
                foreach (var p in info.Params)
                {
                    if (!new ParamFieldSpec { Key = p.Key, Type = p.Type, Attr = p.Attr }.HasField) continue;
                    if (rest == p.Key || rest.StartsWith(p.Key + ".", StringComparison.Ordinal)) return true;
                }
            return false;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        /// <summary>カード 1 枚。</summary>
        private sealed class Card
        {
            public string ItemId = "";
            public VisualElement Root, Body, ArgBox, MenuBox;
            public Button Select, Fold, Up, Down, Remove, Menu, ShowUpTo, MoveFirst, MoveLast;
            public Label Grip;
            public Toggle Enabled;
            public Label State;
            public ParamFieldBuilder Builder;
            public List<ParamFieldSpec> Specs = new List<ParamFieldSpec>();
            public DropdownField ScopeGroup;
            public Toggle ScopeInvert;
            public Button ScopeFromSel;
            public VisualElement ScopeBox;
            public Label ScopeInfo;
            /// <summary>頂点グループの選択肢に「選択から作ったもの」があるか（選択肢の 2 番目）。</summary>
            public bool ScopeHasIds;
            public Foldout DetailFold;
            public Toggle MirrorAsAttribute;
            public DropdownField DetailKey;
            public TextField DetailValue;
            public Button DetailWrite;
            /// <summary>ビューのハンドルで動かすボタン（デフォーマのカードだけ）。</summary>
            public readonly Dictionary<ModifierGizmoKind, Button> GizmoButtons = new Dictionary<ModifierGizmoKind, Button>();
        }

        /// <summary>履歴の項目 1 つ。</summary>
        private sealed class HistoryStep
        {
            public string StackName = "";
            public int StepIndex;
            public string OpenKey = "";
            public VisualElement Body;
            public Button Fold;
            public ParamFieldBuilder Builder;
            public List<ParamFieldSpec> Specs = new List<ParamFieldSpec>();
        }

        private readonly List<Button> _kindButtons = new List<Button>();
        private readonly List<Card> _cards = new List<Card>();
        private readonly List<HistoryStep> _historySteps = new List<HistoryStep>();
        private readonly List<ModifierStackStepView> _items = new List<ModifierStackStepView>();
        private readonly List<ModifierStackView> _histories = new List<ModifierStackView>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _historyOpen = new Dictionary<string, bool>(StringComparer.Ordinal);
        private HashSet<string> _knownItems = new HashSet<string>(StringComparer.Ordinal);
        private string _knownChain = "";

        [UiControl(Ignore = true)] private VisualElement _root;
        [UiControl(Ignore = true)] private VisualElement _dropLine;
        private string _dragItemId;

        private ModifierStackView _chain;
        private int _activeIndex = -1;
        private int _subjectIndex = -1;
        private string _activeItemId = "";
        private string _cardsSig = null;
        private string _historySig = null;

        /// <summary>
        /// 出しているのが既存のサブディビジョン（subdivideMesh）のスタックか。
        /// 項目は種類「サブディビジョン」の宣言で同じ見た目に出し、回数の変更は setModifierStackStepArgs
        /// （項目の作り直し）、削除は ReleaseSubdivisionCommand（子を消す）、すべて適用は applyMeshModifierChain を送る。
        /// 追加・並べ替え・有効・頂点グループ・詳細は使えない。
        /// </summary>
        private bool _isSubdiv;
        private const string SubdivisionAction = "subdivideMesh";
        private const string SubdivisionKind   = "subdivision";

        private static readonly string[] ScopeKeys =
        {
            MeshModifierScope.KeyKind, MeshModifierScope.KeyVertexIds, MeshModifierScope.KeyEdgeVertexIds,
            MeshModifierScope.KeyFaceVertexIds, MeshModifierScope.KeyFaceSizes, MeshModifierScope.KeyPartsSet,
            MeshModifierScope.KeyInvert,
        };

        private const string MirrorBakeKind    = "mirrorBake";
        private const string MirrorDisplayKind = "mirrorDisplay";
        private const string ScopeNoneLabel    = "なし（全体）";
        private const string ScopeIdsLabel     = "選択から作ったもの";

        /// <summary>追加メニューの並び（Blender の Generate・Deform に当たる）。載っていない種類は分類の末尾に足す。</summary>
        private static readonly string[] MenuOrder =
        {
            "array", "bevel", "boolean", "mirrorBake", "thickness", "subdivision",
            "bend", "twist", "wave", "lattice", "rotate", "move", "scale",
        };

        private static readonly Color CardBg       = new Color(0.17f, 0.17f, 0.19f);
        private static readonly Color CardBorder   = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color ActiveBorder = new Color(0.40f, 0.65f, 1f);
        private static readonly Color HeadingColor = new Color(0.65f, 0.8f, 1f);

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);
            _root = root;

            var title = new Label("モディファイア");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 3;
            root.Add(title);

            _targetLabel = new Label();
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _targetLabel.style.fontSize   = 10;
            _targetLabel.style.marginBottom = 4;
            root.Add(_targetLabel);

            _historyBox = new VisualElement();
            root.Add(_historyBox);

            _btnAddMenu = MkBtn("モディファイアを追加", ToggleMenu);
            _btnAddMenu.style.marginBottom = 3;
            root.Add(_btnAddMenu);

            _menuBox = new VisualElement();
            _menuBox.style.display = DisplayStyle.None;
            _menuBox.style.marginBottom = 4;
            root.Add(_menuBox);
            _sweepFollow = new Toggle("元に追随") { tooltip = "複製せず元の辺や面を読み、元の編集に追随させる" };
            BuildMenu();

            _showCageToggle = new Toggle("元の線を表示")
            {
                value = true,
                tooltip = "元の辺と頂点を表示する。隠している間は元の頂点を選べない",
            };
            _showCageToggle.style.marginBottom = 3;
            _showCageToggle.RegisterValueChangedCallback(e => OnShowCage(e.newValue));
            root.Add(_showCageToggle);

            _cardsBox = new VisualElement();
            root.Add(_cardsBox);

            _btnApply = MkBtn("すべて適用", OnApply);
            _btnApply.style.marginTop = 2;
            _btnApply.style.marginBottom = 3;
            root.Add(_btnApply);

            _usageBox = new VisualElement();
            root.Add(_usageBox);

            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            root.Add(_statusLabel);

            Refresh();
        }

        // ── 追加メニュー ───────────────────────────────────────────

        private static string MenuCategoryOf(MeshModifierInfo info)
            => info.Category == "deform" ? "変形" : "生成";

        private void BuildMenu()
        {
            _menuControls.Begin("");
            _menuBox.Clear();
            _kindButtons.Clear();

            var ordered = new List<MeshModifierInfo>();
            foreach (string kind in MenuOrder)
            {
                var info = MeshModifierRegistry.Find(kind);
                if (info != null && !info.HideFromMenu) ordered.Add(info);
            }
            foreach (var info in MeshModifierRegistry.All)
                if (!info.HideFromMenu && !ordered.Contains(info)) ordered.Add(info);

            foreach (string category in new[] { "生成", "変形" })
            {
                _menuBox.Add(SecLabel(category));
                var grid = new VisualElement();
                grid.style.flexDirection = FlexDirection.Row;
                grid.style.flexWrap = Wrap.Wrap;
                grid.style.marginBottom = 2;
                _menuBox.Add(grid);
                foreach (var info in ordered)
                {
                    if (MenuCategoryOf(info) != category) continue;
                    string kind = info.Kind;
                    var b = MkBtn(info.DisplayName, () => OnAdd(kind));
                    b.tooltip = info.Description;
                    b.style.flexGrow = 1;
                    b.style.flexBasis = new Length(45f, LengthUnit.Percent);
                    b.style.minWidth = 0;
                    grid.Add(b);
                    _kindButtons.Add(b);
                    _menuControls.Add($"add.{kind}", b, $"「{info.DisplayName}」を足す。{info.Description}",
                                      UiSafety.SafeWrite, RevealMenu);
                }
            }

            // カーブとパイプ（Maya の Sweep Mesh）。選択が無くても作れる。
            _menuBox.Add(SecLabel("カーブ"));
            var sweepGrid = new VisualElement();
            sweepGrid.style.flexDirection = FlexDirection.Row;
            sweepGrid.style.marginBottom = 2;
            _menuBox.Add(sweepGrid);
            void AddSweep(string id, string text, bool curveOnly, string description)
            {
                var b = MkBtn(text, () => OnSweep(curveOnly));
                b.tooltip = description;
                b.style.flexGrow = 1;
                b.style.flexBasis = new Length(45f, LengthUnit.Percent);
                sweepGrid.Add(b);
                _menuControls.Add($"add.{id}", b, description, UiSafety.SafeWrite, RevealMenu);
            }
            AddSweep("pipe", "パイプ", false, "選択の辺や面からパイプを作る。選択が無ければ直線で作る");
            AddSweep("curve", "カーブ", true, "選択の辺からカーブを作る。選択が無ければ直線で作る");
            _menuBox.Add(_sweepFollow);
        }

        private void OnSweep(bool curveOnly)
        {
            SendCmd(new CreateSweepMeshCommand(ModelIndex, curveOnly, _sweepFollow.value));
            Refresh();
        }

        private bool RevealMenu()
        {
            if (_menuBox.style.display == DisplayStyle.Flex) return false;
            _menuBox.style.display = DisplayStyle.Flex;
            return true;
        }

        private void ToggleMenu()
        {
            bool open = _menuBox.style.display == DisplayStyle.Flex;
            _menuBox.style.display = open ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ================================================================
        // 読み直し
        // ================================================================

        public void Refresh()
        {
            if (_cardsBox == null) return;
            var model = CurrentModel;
            _activeIndex = model?.ActiveMeshIndex ?? -1;

            _chain = null;
            _histories.Clear();
            var stacks = model != null && _activeIndex >= 0
                ? model.ModifierStacksOf(_activeIndex) : Array.Empty<ModifierStackView>();
            foreach (var g in stacks)
            {
                if (g == null) continue;
                if (_chain == null && (g.IsChain || g.IsSubdivision)) _chain = g;
                else if (!g.IsChain && !g.IsSubdivision) _histories.Add(g);
            }
            _isSubdiv = _chain != null && _chain.IsSubdivision;
            _subjectIndex = _chain != null && _chain.ChainSourceIndex >= 0 ? _chain.ChainSourceIndex : _activeIndex;

            RefreshTarget(model);
            RefreshItems();
            RefreshActiveItem();
            RefreshCards();
            RefreshHistory();
            RefreshUsage(model);

            _btnApply.style.display = _chain != null ? DisplayStyle.Flex : DisplayStyle.None;
            _showCageToggle.style.display = _chain != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (_chain != null) _showCageToggle.SetValueWithoutNotify(_chain.CageVisible);
        }

        private void RefreshTarget(IModelView model)
        {
            var active = model != null && _activeIndex >= 0 ? model.GetMesh(_activeIndex) : null;
            if (active == null) _targetLabel.text = "オブジェクトを選んでください";
            else if (_isSubdiv) _targetLabel.text = $"対象：{_chain.ChainSourceName}\nモディファイアを足すには、先にサブディビジョンを適用します";
            else if (_chain != null) _targetLabel.text = $"対象：{_chain.ChainSourceName}";
            else _targetLabel.text = $"対象：{active.Name}";
            // カーブとパイプはオブジェクトが無くても作れるので、メニューはいつでも開ける。
            _btnAddMenu.SetEnabled(true);
            bool canAddKind = active != null && !_isSubdiv;
            foreach (var b in _kindButtons) b.SetEnabled(canAddKind);
        }

        private void RefreshItems()
        {
            _items.Clear();
            if (_chain?.Steps == null) return;
            foreach (var st in _chain.Steps)
            {
                if (st == null || !st.IsExecutable) continue;
                // サブディビジョンのスタックは、つながりの項目（ItemId の入ったもの）だけを出す。
                if (_isSubdiv && (st.Action != SubdivisionAction || string.IsNullOrEmpty(st.ItemId))) continue;
                _items.Add(st);
            }
        }

        /// <summary>足したばかりの項目を選択中にする。選択中の項目が無くなったら先頭にする。</summary>
        private void RefreshActiveItem()
        {
            string chainName = _chain?.Name ?? "";
            string added = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in _items)
            {
                ids.Add(it.ItemId);
                if (chainName == _knownChain && !_knownItems.Contains(it.ItemId)) added = it.ItemId;
            }
            _knownChain = chainName;
            _knownItems = ids;

            if (added != null) _activeItemId = added;
            else if (!ids.Contains(_activeItemId)) _activeItemId = _items.Count > 0 ? _items[0].ItemId : "";
        }

        private ModifierStackStepView FindItem(string itemId)
        {
            foreach (var it in _items) if (it.ItemId == itemId) return it;
            return null;
        }

        private int PositionOf(string itemId)
        {
            for (int i = 0; i < _items.Count; i++) if (_items[i].ItemId == itemId) return i;
            return -1;
        }

        // ================================================================
        // カード
        // ================================================================

        private MeshModifierInfo InfoOf(ModifierStackStepView it)
            => MeshModifierRegistry.Find(_isSubdiv ? SubdivisionKind : it.Action);

        private List<ParamFieldSpec> BuildSpecs(ModifierStackStepView it)
        {
            var specs = new List<ParamFieldSpec>();
            var info = InfoOf(it);
            if (info == null) return specs;
            List<KeyValuePair<string, int>> meshChoices = null;
            foreach (var p in info.Params)
            {
                if (p.Attr.IsMeshRef && meshChoices == null) meshChoices = BuildMeshChoices();
                specs.Add(new ParamFieldSpec
                {
                    Key = p.Key, Type = p.Type, Attr = p.Attr, Value = ValueOf(it, p.Key),
                    MeshChoices = p.Attr.IsMeshRef ? meshChoices : null,
                });
            }
            return specs;
        }

        private void RefreshCards()
        {
            var allSpecs = new List<List<ParamFieldSpec>>();
            var sb = new StringBuilder(_chain?.Name ?? "").Append('#').Append(_isSubdiv);
            foreach (var it in _items)
            {
                var specs = BuildSpecs(it);
                allSpecs.Add(specs);
                sb.Append('\n').Append(ParamFieldBuilder.Signature($"{it.ItemId}|{it.Action}", specs));
            }

            string sig = sb.ToString();
            if (sig != _cardsSig)
            {
                _cardsSig = sig;
                RebuildCards(allSpecs);
            }
            else
            {
                for (int i = 0; i < _cards.Count && i < allSpecs.Count; i++)
                {
                    _cards[i].Specs = allSpecs[i];
                    _cards[i].Builder.UpdateValues(allSpecs[i]);
                }
            }

            foreach (var card in _cards) UpdateCard(card);
            ApplyCardIds();
        }

        private void RebuildCards(List<List<ParamFieldSpec>> allSpecs)
        {
            _cardControls.RemoveUnder(_cardsBox);
            _cardsBox.Clear();
            _cards.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                var card = BuildCard(_items[i], allSpecs[i]);
                _cards.Add(card);
                _cardsBox.Add(card.Root);
            }
            PlayerLayoutRoot.ApplyDarkTheme(_cardsBox);
        }

        private Card BuildCard(ModifierStackStepView it, List<ParamFieldSpec> specs)
        {
            string itemId = it.ItemId;
            var card = new Card { ItemId = itemId, Specs = specs };

            card.Root = new VisualElement();
            card.Root.style.marginBottom = 4;
            card.Root.style.paddingLeft = card.Root.style.paddingRight = 4;
            card.Root.style.paddingTop = card.Root.style.paddingBottom = 3;
            card.Root.style.backgroundColor = new StyleColor(CardBg);
            card.Root.style.borderTopWidth = card.Root.style.borderBottomWidth = card.Root.style.borderRightWidth = 1;
            card.Root.style.borderLeftWidth = 3;
            card.Root.style.borderTopColor = card.Root.style.borderBottomColor = card.Root.style.borderRightColor
                = new StyleColor(CardBorder);
            // カードのどこを押しても選択中にする（作り直さないので、押したスライダーはそのまま動く）。
            card.Root.RegisterCallback<PointerDownEvent>(_ => SetActive(itemId), TrickleDown.TrickleDown);

            // ── 見出し ──
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            card.Root.Add(head);

            card.Fold = MkSmallBtn("▼", () => ToggleCard(itemId), 22);
            head.Add(card.Fold);
            RegCard("fold", card.Fold, "カードを畳む・開く");

            var info = InfoOf(it);
            card.Select = new Button(() => SetActive(itemId)) { text = info != null ? info.DisplayName : it.Action };
            card.Select.tooltip = info?.Description ?? "";
            card.Select.style.flexGrow = 1;
            card.Select.style.flexShrink = 1;
            card.Select.style.minWidth = 0;
            card.Select.style.height = 22;
            card.Select.style.unityTextAlign = TextAnchor.MiddleLeft;
            card.Select.style.borderTopWidth = card.Select.style.borderBottomWidth =
            card.Select.style.borderLeftWidth = card.Select.style.borderRightWidth = 0;
            head.Add(card.Select);
            RegCard("select", card.Select, "このカードを選択中にする");

            card.Enabled = new Toggle("表示") { tooltip = "外すとこのモディファイアを掛けずに表示する（Blender の Realtime）" };
            card.Enabled.labelElement.style.minWidth = 0;
            card.Enabled.labelElement.style.fontSize = 10;
            card.Enabled.style.flexShrink = 0;
            card.Enabled.style.marginLeft = 2;
            card.Enabled.RegisterValueChangedCallback(e => OnEnabled(itemId, e.newValue));
            head.Add(card.Enabled);
            RegCard("enabled", card.Enabled, "処理するか（外すと一時的に無効）");

            card.Up = MkSmallBtn("↑", () => OnUp(itemId), 22);
            head.Add(card.Up);
            RegCard("up", card.Up, "1 つ上へ");
            card.Down = MkSmallBtn("↓", () => OnDown(itemId), 22);
            head.Add(card.Down);
            RegCard("down", card.Down, "1 つ下へ");
            card.Remove = MkSmallBtn("×", () => OnRemove(itemId), 22);
            head.Add(card.Remove);
            RegCard("remove", card.Remove, "このモディファイアを消す（最後の 1 つなら結果も消える）", UiSafety.Destructive);

            if (!_isSubdiv) BuildCardMenu(card, head, itemId);

            card.State = new Label();
            card.State.style.whiteSpace = WhiteSpace.Normal;
            card.State.style.fontSize = 10;
            card.Root.Add(card.State);
            RegCard("itemState", card.State, "失敗の理由", UiSafety.ReadOnly);

            // ── 本体 ──
            card.Body = new VisualElement();
            card.Body.style.marginTop = 2;
            card.Root.Add(card.Body);

            card.ArgBox = new VisualElement();
            card.Body.Add(card.ArgBox);
            card.Builder = new ParamFieldBuilder(_cardControls)
            {
                OnPreview = (k, v) => SendArgs(itemId, new[] { k }, new[] { v }, preview: true),
                OnCommit  = (k, v) => SendArgs(itemId, new[] { k }, new[] { v }, preview: false),
            };
            card.Builder.Build(card.ArgBox, specs);

            if (!_isSubdiv) BuildCardGizmo(card, info, itemId);
            if (!_isSubdiv) BuildCardScope(card, itemId);
            if (!_isSubdiv) BuildCardDetail(card, itemId);
            return card;
        }

        // ── ビューのハンドルで動かすボタン（Maya の変形ハンドル・ラティスの点モードに当たる） ──

        private void BuildCardGizmo(Card card, MeshModifierInfo info, string itemId)
        {
            if (info?.Type == null || !typeof(IDeformerMeshModifier).IsAssignableFrom(info.Type)) return;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 2;
            card.ArgBox.parent.Add(row);

            void Add(ModifierGizmoKind kind, string id, string text, string desc)
            {
                var b = MkBtn(text, () => SendCmd(new ShowModifierGizmoCommand(ModelIndex, kind, _chain?.Name, itemId)));
                b.tooltip = desc;
                b.style.flexGrow = 1;
                row.Add(b);
                card.GizmoButtons[kind] = b;
                RegCard(id, b, desc);
            }

            if (typeof(LatticeMeshModifier).IsAssignableFrom(info.Type))
                Add(ModifierGizmoKind.LatticePoints, "gizmo.latticePoints", "制御点を動かす",
                    "ビューで格子の制御点を選んで動かす（クリック・Ctrl で追加・矩形）。押し直すか Esc で終わる");
            Add(ModifierGizmoKind.AxisMove, "gizmo.axisMove", "作業軸を動かす",
                "ビューのハンドルでこのモディファイアの作業軸を動かす。押し直すか Esc で終わる");
            Add(ModifierGizmoKind.AxisRotate, "gizmo.axisRotate", "作業軸を回す",
                "ビューのハンドルでこのモディファイアの作業軸を回す。押し直すか Esc で終わる");
        }

        private void UpdateCardGizmo(Card card)
        {
            if (card.GizmoButtons.Count == 0) return;
            var active = GetActiveGizmo?.Invoke() ?? default;
            foreach (var kv in card.GizmoButtons)
            {
                bool on = active.Kind == kv.Key && active.ItemId == card.ItemId && active.StackName == _chain?.Name;
                kv.Value.style.backgroundColor = on ? new StyleColor(new Color(0.2f, 0.45f, 0.75f)) : PlayerLayoutRoot.BtnInactiveColor;
            }
        }

        // ── カードのメニューとドラッグ（Blender のカードの ∨ と右端のつまみ） ──

        private void BuildCardMenu(Card card, VisualElement head, string itemId)
        {
            card.Menu = MkSmallBtn("∨", () => ToggleCardMenu(card), 22);
            head.Add(card.Menu);
            RegCard("menu", card.Menu, "このモディファイアのメニューを開く・閉じる");

            card.Grip = new Label("≡") { tooltip = "ドラッグで並べ替える" };
            card.Grip.style.width = 16;
            card.Grip.style.flexShrink = 0;
            card.Grip.style.unityTextAlign = TextAnchor.MiddleCenter;
            head.Add(card.Grip);
            RegCard("grip", card.Grip, "ドラッグで並べ替えるつまみ", UiSafety.ReadOnly);
            card.Grip.RegisterCallback<PointerDownEvent>(e =>
            {
                SetActive(itemId);
                _dragItemId = itemId;
                card.Grip.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            card.Grip.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_dragItemId == itemId && card.Grip.HasPointerCapture(e.pointerId)) ShowDropLine(e.position.y);
            });
            card.Grip.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_dragItemId != itemId) return;
                card.Grip.ReleasePointer(e.pointerId);
                _dragItemId = null;
                ClearDropLine();
                DropCard(itemId, e.position.y);
            });

            card.MenuBox = new VisualElement();
            card.MenuBox.style.display = DisplayStyle.None;
            card.MenuBox.style.marginTop = 2;
            card.MenuBox.style.marginBottom = 2;
            card.Root.Add(card.MenuBox);

            Button Item(string id, string text, string description, Action action, UiSafety safety = UiSafety.SafeWrite)
            {
                var b = MkBtn(text, () => { card.MenuBox.style.display = DisplayStyle.None; action(); });
                b.tooltip = description;
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                card.MenuBox.Add(b);
                RegCard(id, b, description, safety);
                return b;
            }
            Item("applyOne", "適用", "このモディファイアを元の形へ焼き込み、残りは残す", () => OnApplyItem(itemId, false));
            Item("applyUpTo", "ここまで適用", "先頭からこのモディファイアまでを元の形へ焼き込む", () => OnApplyItem(itemId, true));
            Item("duplicate", "複製", "このモディファイアを直下に複製する", () => OnDuplicate(itemId));
            card.MoveFirst = Item("moveFirst", "先頭へ", "このモディファイアを先頭へ動かす", () => OnMoveTo(itemId, true));
            card.MoveLast  = Item("moveLast", "末尾へ", "このモディファイアを末尾へ動かす", () => OnMoveTo(itemId, false));
            Item("copyToSelected", "選択中の他のオブジェクトへコピー", "このモディファイアを、選択中の他のオブジェクトの末尾へコピーする", () => OnCopyToSelected(itemId));
            card.ShowUpTo = Item("showUpTo", "ここまで表示", "結果の表示をこのモディファイアまでの形で止める。もう一度押すと戻す（保存しない）", () => OnShowUpTo(itemId));
        }

        private void ToggleCardMenu(Card card)
        {
            card.MenuBox.style.display = card.MenuBox.style.display == DisplayStyle.Flex ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>y の位置に落としたときの、前に入る項目（null は末尾）。</summary>
        private string DropBefore(string itemId, float y)
        {
            foreach (var c in _cards)
            {
                if (c.ItemId == itemId) continue;
                if (y < c.Root.worldBound.center.y) return c.ItemId;
            }
            return null;
        }

        private void ShowDropLine(float y)
        {
            // カードの間に線を 1 本出す。カードの高さを変えないよう、絶対位置で重ねる。
            if (_dropLine == null)
            {
                _dropLine = new VisualElement { pickingMode = PickingMode.Ignore };
                _dropLine.style.position = Position.Absolute;
                _dropLine.style.left = 0;
                _dropLine.style.right = 0;
                _dropLine.style.height = 3;
                _dropLine.style.backgroundColor = new StyleColor(ActiveBorder);
            }
            if (_dropLine.parent != _cardsBox) _cardsBox.Add(_dropLine);
            _dropLine.BringToFront();

            string before = DropBefore(_dragItemId, y);
            float top = 0f;
            foreach (var c in _cards)
            {
                if (c.ItemId == before) { top = c.Root.layout.yMin - 3f; break; }
                top = c.Root.layout.yMax;
            }
            _dropLine.style.top = top;
            _dropLine.style.display = DisplayStyle.Flex;
        }

        private void ClearDropLine()
        {
            if (_dropLine != null) _dropLine.style.display = DisplayStyle.None;
        }

        private void DropCard(string itemId, float y)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            string before = DropBefore(itemId, y);
            int pos = PositionOf(itemId);
            // 動かない落とし先は送らない（Undo を積まない）。
            string nextNow = pos + 1 < _items.Count ? _items[pos + 1].ItemId : null;
            if (before == nextNow) return;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, itemId, before));
            Refresh();
        }

        // ── カーソルの下のキー（Blender の Add・Apply・Duplicate・Delete と同じキー） ──

        /// <summary>カーソルがパネルの上にあるときだけキーを受ける。受けたら true。</summary>
        public bool HandleKey(KeyDownEvent e, Vector2? pointer)
        {
            if (!pointer.HasValue || _cardsBox == null || _root == null || _root.panel == null || !_root.visible
                || _root.resolvedStyle.display == DisplayStyle.None || !_root.worldBound.Contains(pointer.Value)) return false;
            bool ctrl = e.ctrlKey || e.commandKey;
            if (e.keyCode == KeyCode.A && e.shiftKey && !ctrl && !e.altKey)
            {
                if (!_btnAddMenu.enabledSelf) return false;
                ToggleMenu();
                return true;
            }
            if (string.IsNullOrEmpty(_activeItemId) || FindItem(_activeItemId) == null) return false;
            if (e.keyCode == KeyCode.A && ctrl && !e.shiftKey && !e.altKey)
            {
                if (_isSubdiv) OnApply(); else OnApplyItem(_activeItemId, false);
                return true;
            }
            if (e.keyCode == KeyCode.D && e.shiftKey && !ctrl && !e.altKey && !_isSubdiv)
            {
                OnDuplicate(_activeItemId);
                return true;
            }
            if ((e.keyCode == KeyCode.X || e.keyCode == KeyCode.Delete) && !ctrl && !e.shiftKey && !e.altKey)
            {
                OnRemove(_activeItemId);
                return true;
            }
            return false;
        }

        private void BuildCardScope(Card card, string itemId)
        {
            // 全体にだけ掛かる種類では隠す（UpdateScope）。
            card.ScopeBox = new VisualElement();
            card.Body.Add(card.ScopeBox);

            // 欄の幅が狭いので、見出しを上に置いて名前が読めるようにする。
            card.ScopeBox.Add(SecLabel("頂点グループ"));
            card.ScopeGroup = new DropdownField(new List<string>(), -1)
            {
                tooltip = "効かせる範囲。名前は元メッシュのパーツ選択辞書",
            };
            card.ScopeGroup.RegisterValueChangedCallback(_ => OnScopeGroup(card));
            card.ScopeBox.Add(card.ScopeGroup);
            RegCard("scope.group", card.ScopeGroup, "頂点グループ（なし・選択から作ったもの・元メッシュのパーツ選択辞書の名前）");

            card.ScopeInvert = new Toggle("反転") { tooltip = "頂点グループの外側に効かせる" };
            card.ScopeInvert.RegisterValueChangedCallback(e => OnScopeInvert(card, e.newValue));
            card.ScopeBox.Add(card.ScopeInvert);
            RegCard("scope.invert", card.ScopeInvert, "頂点グループの外側に効かせる");

            card.ScopeFromSel = MkBtn("今の選択から作る", () => OnScopeFromSelection(itemId));
            card.ScopeFromSel.tooltip = "元メッシュの今の選択（頂点・辺・面）を頂点グループにする。頂点 ID が無ければ元メッシュに振る";
            card.ScopeFromSel.style.marginBottom = 2;
            card.ScopeBox.Add(card.ScopeFromSel);
            RegCard("scope.fromSelection", card.ScopeFromSel, "元メッシュの今の選択（頂点・辺・面）を頂点グループにする。頂点 ID が無ければ元メッシュに振る", UiSafety.SafeWrite);

            card.ScopeInfo = new Label();
            card.ScopeInfo.style.whiteSpace = WhiteSpace.Normal;
            card.ScopeInfo.style.fontSize = 10;
            card.ScopeBox.Add(card.ScopeInfo);
            RegCard("scope.info", card.ScopeInfo, "範囲の中身", UiSafety.ReadOnly);
        }

        private void BuildCardDetail(Card card, string itemId)
        {
            card.DetailFold = new Foldout { text = "詳細", value = false };
            card.Body.Add(card.DetailFold);
            RegCard("detailFold", card.DetailFold, "詳細の開閉", UiSafety.SafeWrite);

            string action = FindItem(itemId)?.Action;
            if (action == MirrorBakeKind || action == MirrorDisplayKind)
            {
                card.MirrorAsAttribute = new Toggle("MQO の属性で持つ")
                {
                    tooltip = "結果に面を足さず、MQO の mirror 属性とミラー側のオブジェクトで持つ。列の最後に 1 つだけ置ける",
                };
                card.MirrorAsAttribute.RegisterValueChangedCallback(e => OnMirrorAsAttribute(itemId, e.newValue));
                card.DetailFold.Add(card.MirrorAsAttribute);
                RegCard("mirrorAsAttribute", card.MirrorAsAttribute, "MQO の属性で持つ（切ると結果に面を足す）");
            }

            card.DetailKey = new DropdownField("キー", new List<string>(), -1);
            card.DetailKey.RegisterValueChangedCallback(_ => UpdateDetailValue(card));
            card.DetailFold.Add(card.DetailKey);
            RegCard("detailKey", card.DetailKey, "文字列で書く設定のキー");

            card.DetailValue = new TextField("値");
            card.DetailFold.Add(card.DetailValue);
            RegCard("detailValue", card.DetailValue, "書く値（文字列。種類の宣言で読み直せること）");

            card.DetailWrite = MkBtn("書く", () => OnDetailWrite(card, itemId));
            card.DetailFold.Add(card.DetailWrite);
            RegCard("detailWrite", card.DetailWrite, "文字列の値を書く", UiSafety.SafeWrite);
        }

        private void RegCard<T>(string id, T element, string description, UiSafety safety = UiSafety.SafeWrite)
            where T : VisualElement
            => _cardControls.Add(id, element, description, safety);

        /// <summary>カードの表示を項目の今の状態へ合わせる。</summary>
        private void UpdateCard(Card card)
        {
            var it = FindItem(card.ItemId);
            if (it == null) return;
            int pos = PositionOf(card.ItemId);
            UpdateCardGizmo(card);

            bool collapsed = _collapsed.Contains(CollapseKey(card.ItemId));
            card.Body.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            card.Fold.text = collapsed ? "▶" : "▼";

            // 末尾固定の項目は動かせず、その直前の項目は下へ動かせない。
            bool pinned     = IsPinned(it);
            bool nextPinned = pos + 1 < _items.Count && IsPinned(_items[pos + 1]);
            card.Up.SetEnabled(pos > 0 && !pinned && !_isSubdiv);
            card.Down.SetEnabled(pos < _items.Count - 1 && !pinned && !nextPinned && !_isSubdiv);
            card.MoveFirst?.SetEnabled(pos > 0 && !pinned);
            card.MoveLast?.SetEnabled(pos < _items.Count - 1 && !pinned && !nextPinned);
            if (card.ShowUpTo != null)
                card.ShowUpTo.text = _chain != null && _chain.DisplayUpToItemId == card.ItemId ? "最後まで表示" : "ここまで表示";
            card.Enabled.SetEnabled(!_isSubdiv);
            card.Enabled.SetValueWithoutNotify(it.Enabled);

            bool failed = it.Enabled && (it.State == "Failed" || it.State == "Blocked");
            string hint = failed || _isSubdiv ? null : HintOf(it);
            card.State.style.display = failed || hint != null ? DisplayStyle.Flex : DisplayStyle.None;
            card.State.text = failed
                ? (string.IsNullOrEmpty(it.StateMessage) ? (it.State == "Blocked" ? "前のモディファイアが失敗したので止まっています" : "失敗しました") : it.StateMessage)
                : hint ?? "";
            card.State.style.color = new StyleColor(StateColor(it));

            card.Select.style.backgroundColor = new StyleColor(Color.clear);
            card.Select.style.color = new StyleColor(failed ? StateColor(it) : (it.Enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f)));

            bool active = card.ItemId == _activeItemId;
            card.Root.style.borderLeftColor = new StyleColor(active ? ActiveBorder : CardBorder);

            if (!_isSubdiv)
            {
                UpdateScope(card, it);
                UpdateDetailKeys(card, it);
            }
        }

        /// <summary>今の設定では形が変わらない種類の案内（IMeshModifierHint）。</summary>
        private static string HintOf(ModifierStackStepView it)
        {
            if (it?.Keys == null) return null;
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int k = 0; k < it.Keys.Length && k < it.Values.Length; k++) args[it.Keys[k]] = it.Values[k];
            return MeshModifierRegistry.FromArgs(it.Action, args, out var m, out _, out _) && m is IMeshModifierHint h
                ? h.GetHint() : null;
        }

        private string CollapseKey(string itemId) => $"{_chain?.Name}|{itemId}";

        private void ToggleCard(string itemId)
        {
            string key = CollapseKey(itemId);
            if (!_collapsed.Remove(key)) _collapsed.Add(key);
            foreach (var c in _cards) if (c.ItemId == itemId) UpdateCard(c);
        }

        private bool RevealCard(Card card)
        {
            if (!_collapsed.Remove(CollapseKey(card.ItemId))) return false;
            UpdateCard(card);
            return true;
        }

        /// <summary>選択中のカードを変える。作り直さず、見た目と UI 自動操作の ID だけを変える。</summary>
        private void SetActive(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || itemId == _activeItemId || FindItem(itemId) == null) return;
            _activeItemId = itemId;
            foreach (var c in _cards)
                c.Root.style.borderLeftColor = new StyleColor(c.ItemId == _activeItemId ? ActiveBorder : CardBorder);
            ApplyCardIds();
        }

        private static string StripItemPrefix(string id)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(ItemIdHead, StringComparison.Ordinal)) return id;
            int dot = id.IndexOf('.', ItemIdHead.Length);
            return dot > 0 ? id.Substring(dot + 1) : id;
        }

        /// <summary>カードの欄の ID を、選択中かどうかに合わせて付け替える。</summary>
        private void ApplyCardIds()
        {
            foreach (var card in _cards)
            {
                string prefix = card.ItemId == _activeItemId ? "" : $"{ItemIdHead}{card.ItemId}.";
                var owner = card;
                foreach (var e in _cardControls.Entries)
                {
                    if (e.Element == null || !card.Root.Contains(e.Element)) continue;
                    e.Id = prefix + StripItemPrefix(e.Id);
                    var inMenu = owner.MenuBox != null && owner.MenuBox.Contains(e.Element);
                    e.Reveal = () =>
                    {
                        bool changed = RevealCard(owner);
                        if (inMenu && owner.MenuBox.style.display != DisplayStyle.Flex)
                        {
                            owner.MenuBox.style.display = DisplayStyle.Flex;
                            changed = true;
                        }
                        return changed;
                    };
                }
            }
        }

        /// <summary>
        /// IsMeshRef の欄の選択肢。元メッシュと結果は指せないので外す。
        /// 他のオブジェクトの結果の行も出さない（元を選ぶと受け口がその結果を読む）。
        /// </summary>
        private List<KeyValuePair<string, int>> BuildMeshChoices()
        {
            var list = new List<KeyValuePair<string, int>>();
            var model = CurrentModel;
            if (model?.DrawableList == null) return list;
            var results = new HashSet<int>(model.ModifierResultIndices);
            foreach (var m in model.DrawableList)
            {
                if (m == null || results.Contains(m.MasterIndex)) continue;
                if (_chain != null && m.MasterIndex == _chain.ChainSourceIndex) continue;
                list.Add(new KeyValuePair<string, int>(m.Name, m.MasterIndex));
            }
            return list;
        }

        private static string ValueOf(ModifierStackStepView it, string key)
        {
            if (it?.Keys == null) return "";
            for (int k = 0; k < it.Keys.Length; k++)
                if (it.Keys[k] == key) return k < it.Values.Length ? it.Values[k] ?? "" : "";
            return "";
        }

        // ── 頂点グループ ───────────────────────────────────────────

        private void UpdateScope(Card card, ModifierStackStepView it)
        {
            string kind = ValueOf(it, MeshModifierScope.KeyKind);
            var scopeKind = MeshModifierScopeKind.All;
            if (PanelCommandFactory.TryParseValue(kind, typeof(MeshModifierScopeKind), out object kv, out _))
                scopeKind = (MeshModifierScopeKind)kv;
            string ps0 = ValueOf(it, MeshModifierScope.KeyPartsSet);

            // 全体にだけ掛かる種類は欄を出さない。範囲が入っている古いデータでは解除できるよう出す。
            bool wholeOnly = MeshModifierRegistry.Find(it.Action)?.WholeMeshOnly == true;
            card.ScopeBox.style.display = wholeOnly && scopeKind == MeshModifierScopeKind.All
                ? DisplayStyle.None : DisplayStyle.Flex;

            // 選択肢：なし、（あれば）選択から作ったもの、元メッシュの辞書の名前。見つからない名前も今の値として出す。
            var choices = new List<string> { ScopeNoneLabel };
            card.ScopeHasIds = scopeKind == MeshModifierScopeKind.Ids;
            if (card.ScopeHasIds) choices.Add(ScopeIdsLabel);
            var src = _chain != null ? CurrentModel?.GetMesh(_chain.ChainSourceIndex) : null;
            if (src?.PartsSelectionSets != null)
                foreach (var ps in src.PartsSelectionSets) if (ps != null) choices.Add(ps.Name);
            if (scopeKind == MeshModifierScopeKind.PartsSet && !choices.Contains(ps0)) choices.Add(ps0);
            card.ScopeGroup.choices = choices;
            card.ScopeGroup.SetValueWithoutNotify(scopeKind switch
            {
                MeshModifierScopeKind.Ids      => ScopeIdsLabel,
                MeshModifierScopeKind.PartsSet => ps0,
                _                              => ScopeNoneLabel,
            });

            card.ScopeInvert.SetEnabled(scopeKind != MeshModifierScopeKind.All);
            card.ScopeInvert.SetValueWithoutNotify(ValueOf(it, MeshModifierScope.KeyInvert) == "true");

            card.ScopeInfo.text = card.ScopeHasIds
                ? $"頂点 {CountOf(it, MeshModifierScope.KeyVertexIds)}・辺 {CountOf(it, MeshModifierScope.KeyEdgeVertexIds) / 2}・面 {CountOf(it, MeshModifierScope.KeyFaceSizes)}"
                : "";
        }

        private static int CountOf(ModifierStackStepView it, string key)
        {
            string v = ValueOf(it, key);
            if (string.IsNullOrEmpty(v)) return 0;
            return PanelCommandFactory.TryParseValue(v, typeof(int[]), out object o, out _) ? ((int[])o).Length : 0;
        }

        private void OnScopeGroup(Card card)
        {
            string itemId = card.ItemId;
            if (FindItem(itemId) == null) return;
            int index = card.ScopeGroup.index;
            if (index < 0) return;
            if (index == 0)
            {
                SendArgs(itemId, new[] { MeshModifierScope.KeyKind }, new[] { Fmt(MeshModifierScopeKind.All) }, false);
                return;
            }
            if (card.ScopeHasIds && index == 1) return;   // 選択から作ったもののまま
            if (_chain == null) return;
            SendCmd(new SetMeshModifierVertexGroupCommand(ModelIndex, _chain.Name, itemId, card.ScopeGroup.value));
            Refresh();
        }

        private void OnScopeInvert(Card card, bool on)
        {
            if (FindItem(card.ItemId) == null) return;
            SendArgs(card.ItemId, new[] { MeshModifierScope.KeyInvert }, new[] { on ? "true" : "false" }, false);
        }

        private void OnScopeFromSelection(string itemId)
        {
            if (FindItem(itemId) == null || _chain == null || _isSubdiv) return;
            int sourceIndex = _chain.ChainSourceIndex;
            var sv = CurrentModel?.BuildModifierScopeFromSelection(sourceIndex);
            if (sv == null) { SetStatus("元メッシュの選択を読めません"); Refresh(); return; }
            if (sv.VertexCount + sv.EdgeCount + sv.FaceCount + sv.NoId == 0)
            {
                SetStatus("元メッシュで頂点・辺・面を選んでから押してください");
                Refresh();
                return;
            }
            // モディファイアの結果は流し直すと ID が消えるので振らない（受け口 ExecuteSetMeshModifierVertexGroup と同じ判定）。
            // 履歴の生成物は作り直しが ID を戻すので振れる。
            bool isOutput = _histories.Count == 0
                && new HashSet<int>(CurrentModel.ModifierResultIndices).Contains(sourceIndex);
            SetStatus(sv.NoId > 0 && isOutput
                ? $"頂点 ID の無い要素 {sv.NoId} 件は頂点グループに入りません（モディファイアの結果は流し直すと ID が消えるので振れません）"
                : "");
            SendCmd(new SetMeshModifierVertexGroupCommand(ModelIndex, _chain.Name, itemId));
            Refresh();
        }

        private void OnMirrorAsAttribute(string itemId, bool on)
        {
            if (FindItem(itemId) == null || _chain == null) return;
            if (on && _items.Exists(s => s.ItemId != itemId && IsPinned(s)))
            {
                SetStatus("MQO の属性で持つミラーは 1 つだけ置けます");
                Refresh();
                return;
            }
            SetStatus("");
            SendCmd(new SetMeshModifierMirrorAttributeCommand(ModelIndex, _chain.Name, itemId, on));
            Refresh();
        }

        // ── 詳細（文字列） ─────────────────────────────────────────

        private void UpdateDetailKeys(Card card, ModifierStackStepView it)
        {
            string prev = card.DetailKey.value;
            var keys = new List<string>();
            var info = MeshModifierRegistry.Find(it.Action);
            if (info != null) foreach (var p in info.Params) keys.Add(p.Key);
            keys.AddRange(ScopeKeys);
            card.DetailKey.choices = keys;
            card.DetailKey.SetValueWithoutNotify(keys.Contains(prev) ? prev : (keys.Count > 0 ? keys[0] : ""));
            card.DetailWrite.SetEnabled(keys.Count > 0);
            UpdateDetailValue(card);
            card.MirrorAsAttribute?.SetValueWithoutNotify(it.Action == MirrorDisplayKind);
        }

        private void UpdateDetailValue(Card card)
        {
            card.DetailValue.SetValueWithoutNotify(ValueOf(FindItem(card.ItemId), card.DetailKey.value));
        }

        private void OnDetailWrite(Card card, string itemId)
        {
            string key = card.DetailKey.value;
            if (FindItem(itemId) == null || string.IsNullOrEmpty(key)) { SetStatus("書くキーを選んでください"); return; }
            SendArgs(itemId, new[] { key }, new[] { card.DetailValue.value ?? "" }, false);
        }

        // ================================================================
        // 履歴（Maya の INPUTS に当たる）
        // ================================================================

        private static string StepName(string action)
        {
            var t = PanelCommandFactory.ResolveType(action);
            if (t == null) return action ?? "";
            string name = t.GetCustomAttribute<PLModifierAttribute>(inherit: true)?.DisplayName;
            if (!string.IsNullOrEmpty(name)) return name;

            string d = t.GetCustomAttribute<PLCommandAttribute>(inherit: false)?.Description ?? "";
            int cut = d.Length;
            foreach (char c in new[] { '。', '、', '（', '(' })
            {
                int i = d.IndexOf(c);
                if (i > 0 && i < cut) cut = i;
            }
            d = d.Substring(0, cut).Trim();
            foreach (string tail in new[] { "を作る", "を足す", "を置く", "にする" })
                if (d.EndsWith(tail, StringComparison.Ordinal)) { d = d.Substring(0, d.Length - tail.Length); break; }
            return string.IsNullOrEmpty(d) ? action : d;
        }

        private static List<ParamFieldSpec> BuildHistorySpecs(ModifierStackStepView st)
        {
            var specs = new List<ParamFieldSpec>();
            var type = PanelCommandFactory.ResolveType(st.Action);
            if (type == null || st.Keys == null) return specs;
            for (int k = 0; k < st.Keys.Length; k++)
            {
                if (k >= st.Writable.Length || !st.Writable[k]) continue;
                if (!PanelCommandFactory.TryGetArgParam(type, st.Keys[k], out var argType, out var attr)) continue;
                specs.Add(new ParamFieldSpec
                {
                    Key = st.Keys[k], Type = argType, Attr = attr,
                    Value = k < st.Values.Length ? st.Values[k] ?? "" : "",
                });
            }
            return specs;
        }

        // ── 使用先（Maya の OUTPUTS に当たる。操作統合計画 5 章・段 7）──

        private string _usageSig;

        /// <summary>帯を読む揺れ鎖があるか（履歴カードの「揺れ鎖を作る」を出さない）。</summary>
        private bool _hasSpringUsage;

        private void RefreshUsage(IModelView model)
        {
            int subject = _activeIndex;
            var usages = model != null && subject >= 0 ? model.ModifierUsagesOf(subject) : Array.Empty<ModifierUsageView>();
            var sb = new StringBuilder();
            bool spring = false;
            foreach (var u in usages)
            {
                sb.Append(u.Action).Append('|').Append(u.OutputName).Append('|').Append(u.OutputIndex).Append('\n');
                if (u.Action == SpringAction) spring = true;
            }
            string sig = sb.ToString();
            bool springChanged = spring != _hasSpringUsage;
            _hasSpringUsage = spring;
            if (springChanged)
            {
                // ボタンの有無が変わるのでカードを組み直す。
                _historySig = null;
                RefreshHistory();
            }
            if (sig == _usageSig) return;
            _usageSig = sig;

            _cardControls.RemoveUnder(_usageBox);
            _usageBox.Clear();
            if (usages.Count == 0) return;

            _usageBox.Add(SecLabel("使用先"));
            for (int i = 0; i < usages.Count; i++)
            {
                var u = usages[i];
                int index = u.OutputIndex;
                var b = MkBtn($"{StepName(u.Action)}：{u.OutputName}", () => OnSelectInput(index));
                b.tooltip = u.OutputIsBone ? "ボーンはオブジェクトリストの Bone のタブで選ぶ" : "読んでいる項目の出力を選ぶ";
                b.SetEnabled(index >= 0 && !u.OutputIsBone);
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                b.style.marginBottom = 2;
                _usageBox.Add(b);
                _cardControls.Add($"usage.{i}", b, "このオブジェクトを読んでいる項目の出力を選ぶ", UiSafety.SafeWrite);
            }
            PlayerLayoutRoot.ApplyDarkTheme(_usageBox);
        }

        private void RefreshHistory()
        {
            var sb = new StringBuilder();
            var specsOf = new Dictionary<string, List<ParamFieldSpec>>(StringComparer.Ordinal);
            foreach (var g in _histories)
            {
                sb.Append(g.Name);
                for (int si = 0; si < g.Steps.Length; si++)
                {
                    var st = g.Steps[si];
                    if (st == null || !st.IsExecutable) continue;
                    var specs = BuildHistorySpecs(st);
                    specsOf[$"{g.Name}|{si}"] = specs;
                    sb.Append('\n').Append(ParamFieldBuilder.Signature($"{si}|{st.Action}", specs));
                    foreach (string n in st.InputNames) sb.Append(',').Append(n);
                    foreach (int i in st.InputIndices) sb.Append(',').Append(i);
                    sb.Append(st.OutputEditPending ? "!" : "");
                    // 断面の種類で出す欄が変わる。
                    foreach (var s in specs) if (s.Key == SectionKindKey) sb.Append('=').Append(s.Value);
                }
            }

            string sig = sb.ToString();
            if (sig != _historySig)
            {
                _historySig = sig;
                RebuildHistory(specsOf);
            }
            else
            {
                foreach (var hs in _historySteps)
                    if (specsOf.TryGetValue($"{hs.StackName}|{hs.StepIndex}", out var specs))
                    {
                        hs.Specs = specs;
                        hs.Builder.UpdateValues(specs);
                    }
            }
        }

        private void RebuildHistory(Dictionary<string, List<ParamFieldSpec>> specsOf)
        {
            _cardControls.RemoveUnder(_historyBox);
            _historyBox.Clear();
            _historySteps.Clear();

            for (int h = 0; h < _histories.Count; h++)
            {
                var g = _histories[h];
                string stackName = g.Name;

                var box = new VisualElement();
                box.style.marginBottom = 4;
                box.style.paddingLeft = box.style.paddingRight = 4;
                box.style.paddingTop = box.style.paddingBottom = 3;
                box.style.backgroundColor = new StyleColor(CardBg);
                box.style.borderTopWidth = box.style.borderBottomWidth =
                box.style.borderLeftWidth = box.style.borderRightWidth = 1;
                box.style.borderTopColor = box.style.borderBottomColor =
                box.style.borderLeftColor = box.style.borderRightColor = new StyleColor(CardBorder);
                _historyBox.Add(box);

                var head = new VisualElement();
                head.style.flexDirection = FlexDirection.Row;
                head.style.alignItems = Align.Center;
                head.style.marginBottom = 2;
                box.Add(head);

                string lastName = "";
                foreach (var st in g.Steps)
                    if (st != null && st.IsExecutable) lastName = StepName(st.Action);
                var title = new Label($"履歴：{lastName}");
                title.style.flexGrow = 1;
                title.style.flexShrink = 1;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                head.Add(title);

                var del = MkBtn("履歴を削除", () => OnDeleteHistory(stackName));
                del.style.flexShrink = 0;
                head.Add(del);
                _cardControls.Add($"history.{h}.delete", del,
                    $"履歴「{stackName}」を削除する。出力は残るが、作り直せなくなる", UiSafety.Destructive);

                int ribbonSi = -1, pipeSi = -1;
                for (int si = 0; si < g.Steps.Length; si++)
                {
                    var st = g.Steps[si];
                    if (st == null || !st.IsExecutable) continue;
                    if (st.Action == RibbonAction && ribbonSi < 0) ribbonSi = si;
                    else if (st.Action == PipeAction && pipeSi < 0) pipeSi = si;
                }

                for (int si = 0; si < g.Steps.Length; si++)
                {
                    var st = g.Steps[si];
                    if (st == null || !st.IsExecutable) continue;
                    if (si == ribbonSi && pipeSi >= 0) continue;   // パイプの欄へまとめる
                    if (si == pipeSi)
                    {
                        BuildSweepCard(box, h, stackName, ribbonSi >= 0 ? g.Steps[ribbonSi] : null, ribbonSi,
                                       ribbonSi >= 0 ? specsOf[$"{stackName}|{ribbonSi}"] : null,
                                       st, pipeSi, specsOf[$"{stackName}|{si}"]);
                        continue;
                    }
                    BuildHistoryStep(box, h, stackName, si, st, specsOf[$"{stackName}|{si}"]);
                }

                // 同じカーブから揺れ鎖を作る（カーブの編集がボーンまで追随する。残す独自機能）。
                bool readsBelt = false;
                foreach (var st in g.Steps)
                    if (st != null && st.IsExecutable && typeof(CreateBeltPrimitiveCommand).IsAssignableFrom(PanelCommandFactory.ResolveType(st.Action) ?? typeof(object)))
                        readsBelt = true;
                if (readsBelt && !_hasSpringUsage)
                {
                    var chain = MkBtn("揺れ鎖を作る", () => SendCmdAndRefresh(new AddSpringChainFromHistoryCommand(ModelIndex, stackName)));
                    chain.tooltip = "同じカーブの帯からボーンの鎖を作り、生成物へウェイトと揺れ方を入れて揺れるようにする。カーブを直すとボーンも追随する";
                    chain.style.marginTop = 2;
                    box.Add(chain);
                    _cardControls.Add($"history.{h}.chain", chain, "同じカーブから揺れ鎖を作る", UiSafety.SafeWrite);
                }
            }
            PlayerLayoutRoot.ApplyDarkTheme(_historyBox);
        }

        // ── カーブとパイプの履歴（Maya の Sweep Mesh の入力ノードに当たる。操作統合計画 5 章）──

        private const string RibbonAction   = "edgeRibbonFace";
        private const string PipeAction     = "createPipe";
        private const string SpringAction   = "placeSpringBoneLadderChains";
        private const string SectionKindKey = "params.sectionKind";

        /// <summary>よく使う欄のキーと見出し（並びの順）。断面の種類ごとの欄は SectionFieldKeys。</summary>
        private static readonly KeyValuePair<string, string>[] PipeCommonFields =
        {
            new KeyValuePair<string, string>(SectionKindKey, "断面"),
            new KeyValuePair<string, string>("params.sectionSides", "辺の数"),
            new KeyValuePair<string, string>("params.sectionInner", "内側の半径"),
            new KeyValuePair<string, string>("params.sectionHeight", "高さ"),
            new KeyValuePair<string, string>("params.sectionArcAngle", "角度"),
            new KeyValuePair<string, string>("params.sectionCycles", "周期"),
            new KeyValuePair<string, string>("params.sectionAmplitude", "振幅"),
            new KeyValuePair<string, string>("params.capEnds", "蓋"),
            new KeyValuePair<string, string>("spline.enabled", "分割する"),
            new KeyValuePair<string, string>("spline.segments", "分割"),
        };

        /// <summary>今の断面の種類で出す欄か。種類ごとの欄でないキーは常に出す。</summary>
        private static bool SectionFieldVisible(string key, string kind)
        {
            switch (key)
            {
                case "params.sectionSides":
                    return kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Circle) || kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Star)
                        || kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Arc) || kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Wave);
                case "params.sectionInner":     return kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Star);
                case "params.sectionHeight":    return kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Rectangle);
                case "params.sectionArcAngle":  return kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Arc);
                case "params.sectionCycles":
                case "params.sectionAmplitude": return kind == nameof(Poly_Ling.Pipe.SweepSectionKind.Wave);
                default: return true;
            }
        }

        private static string SectionKindName(string value)
            => PanelCommandFactory.TryParseValue(value, typeof(Poly_Ling.Pipe.SweepSectionKind), out object o, out _)
                ? o.ToString() : nameof(Poly_Ling.Pipe.SweepSectionKind.Custom);

        private void BuildSweepCard(VisualElement box, int h, string stackName,
                                    ModifierStackStepView ribbon, int ribbonSi, List<ParamFieldSpec> ribbonSpecs,
                                    ModifierStackStepView pipe, int pipeSi, List<ParamFieldSpec> pipeSpecs)
        {
            string head = $"history.{h}.";

            // カーブ（入力）。帯だけのパイプは帯の元がそのまま入力。
            var inputStep = ribbon ?? pipe;
            for (int i = 0; i < inputStep.InputIndices.Length; i++)
            {
                int index = inputStep.InputIndices[i];
                var curve = MkBtn($"カーブを編集：{inputStep.InputNames[i]}", () => OnSelectInput(index));
                curve.tooltip = "入力のオブジェクトを選ぶ。頂点を動かすとパイプが追随する";
                curve.SetEnabled(index >= 0);
                curve.style.unityTextAlign = TextAnchor.MiddleLeft;
                curve.style.marginBottom = 2;
                box.Add(curve);
                _cardControls.Add($"{head}curve.{i}", curve, "入力のカーブを選ぶ", UiSafety.SafeWrite);

                var points = MkBtn("点を増やす", () => SendCmdAndRefresh(new SubdivideCurveCommand(ModelIndex, index)));
                points.tooltip = "カーブの線分の中点に点を足す（線分を選んでいればそれだけ）。パイプと揺れ鎖も追随する";
                points.SetEnabled(index >= 0);
                points.style.marginBottom = 2;
                box.Add(points);
                _cardControls.Add($"{head}curvePoints.{i}", points, "カーブの線分の中点に点を足す", UiSafety.SafeWrite);
            }

            if (pipe.OutputEditPending)
            {
                var pending = new Label("出力の編集が未確定です。左ペインの「履歴の一覧」で確定か破棄をしてください");
                pending.style.whiteSpace = WhiteSpace.Normal;
                pending.style.fontSize = 10;
                box.Add(pending);
            }

            // よく使う欄：太さ（帯の幅）と断面をひとまとめに。
            var used = new HashSet<string>(StringComparer.Ordinal);
            if (ribbonSpecs != null)
            {
                var common = new List<ParamFieldSpec>();
                foreach (var s in ribbonSpecs)
                    if (s.Key == "widthWorld") { s.LabelText = "太さ"; common.Add(s); used.Add(s.Key); }
                AddHistoryFields(box, $"{head}{ribbonSi}.", stackName, ribbonSi, common, ribbonSpecs, null);
            }

            string kind = "";
            foreach (var s in pipeSpecs) if (s.Key == SectionKindKey) kind = SectionKindName(s.Value);
            var pipeCommon = new List<ParamFieldSpec>();
            var pipeUsed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in PipeCommonFields)
                foreach (var s in pipeSpecs)
                    if (s.Key == f.Key)
                    {
                        pipeUsed.Add(s.Key);
                        if (!SectionFieldVisible(s.Key, kind)) continue;
                        s.LabelText = f.Value;
                        pipeCommon.Add(s);
                    }
            AddHistoryFields(box, $"{head}{pipeSi}.", stackName, pipeSi, pipeCommon, pipeSpecs, null);

            var edit = MkBtn("断面を編集", () => SendCmdAndRefresh(new OpenModifierStackProfileEditSpaceCommand(ModelIndex, stackName, pipeSi, "pipe")));
            edit.tooltip = "断面の点を作業空間で編集する。反映すると断面の種類はカスタムになる";
            edit.style.marginBottom = 2;
            box.Add(edit);
            _cardControls.Add($"{head}sectionEdit", edit, "断面を作業空間で編集する", UiSafety.SafeWrite);

            // 残りは詳細へ（帯の取り込みなど）。
            var detail = new Foldout { text = "詳細", value = false };
            box.Add(detail);
            bool Reveal() { if (detail.value) return false; detail.value = true; return true; }
            if (ribbonSpecs != null)
            {
                var rest = ribbonSpecs.FindAll(s => !used.Contains(s.Key));
                AddHistoryFields(detail.contentContainer, $"{head}{ribbonSi}.", stackName, ribbonSi, rest, ribbonSpecs, Reveal);
            }
            var pipeRest = pipeSpecs.FindAll(s => !pipeUsed.Contains(s.Key));
            AddHistoryFields(detail.contentContainer, $"{head}{pipeSi}.", stackName, pipeSi, pipeRest, pipeSpecs, Reveal);
        }

        private void AddHistoryFields(VisualElement parent, string prefix, string stackName, int si,
                                      List<ParamFieldSpec> fields, List<ParamFieldSpec> allSpecs, Func<bool> reveal)
        {
            if (fields.Count == 0) return;
            var args = new VisualElement();
            parent.Add(args);
            var builder = new ParamFieldBuilder(_cardControls)
            {
                OnPreview = null,
                OnCommit  = (k, v) => OnHistoryArg(stackName, si, k, v),
            };
            builder.Build(args, fields);
            foreach (var e in _cardControls.Entries)
                if (e.Element != null && args.Contains(e.Element) && !e.Id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    e.Id = prefix + e.Id;
                    if (reveal != null) e.Reveal = reveal;
                }
            _historySteps.Add(new HistoryStep { StackName = stackName, StepIndex = si, Builder = builder, Specs = allSpecs });
        }

        private void SendCmdAndRefresh(PanelCommand cmd)
        {
            SendCmd(cmd);
            Refresh();
        }

        private void BuildHistoryStep(VisualElement box, int h, string stackName, int si,
                                      ModifierStackStepView st, List<ParamFieldSpec> specs)
        {
            string prefix = $"history.{h}.{si}.";
            string openKey = $"{stackName}|{si}";
            var hs = new HistoryStep { StackName = stackName, StepIndex = si, OpenKey = openKey, Specs = specs };
            _historySteps.Add(hs);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            box.Add(row);

            hs.Fold = MkSmallBtn("▼", () => ToggleHistoryStep(hs), 22);
            row.Add(hs.Fold);
            _cardControls.Add(prefix + "fold", hs.Fold, "履歴の項目を畳む・開く", UiSafety.SafeWrite);

            var name = new Label(StepName(st.Action));
            name.style.flexGrow = 1;
            row.Add(name);

            hs.Body = new VisualElement();
            hs.Body.style.marginLeft = 6;
            box.Add(hs.Body);

            for (int i = 0; i < st.InputIndices.Length; i++)
            {
                int index = st.InputIndices[i];
                var input = MkBtn($"入力：{st.InputNames[i]}", () => OnSelectInput(index));
                input.tooltip = "このオブジェクトを選ぶ";
                input.SetEnabled(index >= 0);
                input.style.unityTextAlign = TextAnchor.MiddleLeft;
                input.style.marginBottom = 2;
                hs.Body.Add(input);
                _cardControls.Add($"{prefix}input.{i}", input, "入力のオブジェクトを選ぶ", UiSafety.SafeWrite);
            }

            if (st.OutputEditPending)
            {
                var pending = new Label("出力の編集が未確定です。左ペインの「履歴の一覧」で確定か破棄をしてください");
                pending.style.whiteSpace = WhiteSpace.Normal;
                pending.style.fontSize = 10;
                hs.Body.Add(pending);
            }

            var args = new VisualElement();
            hs.Body.Add(args);
            hs.Builder = new ParamFieldBuilder(_cardControls)
            {
                // 作り直しが重いので、スライダーは離したときだけ送る。
                OnPreview = null,
                OnCommit  = (k, v) => OnHistoryArg(stackName, si, k, v),
            };
            hs.Builder.Build(args, specs);
            foreach (var e in _cardControls.Entries)
                if (e.Element != null && args.Contains(e.Element) && !e.Id.StartsWith(prefix, StringComparison.Ordinal))
                {
                    e.Id = prefix + e.Id;
                    e.Reveal = () => RevealHistoryStep(hs);
                }

            UpdateHistoryStep(hs, st);
        }

        private bool IsHistoryOpen(HistoryStep hs, ModifierStackStepView st)
        {
            if (_historyOpen.TryGetValue(hs.OpenKey, out bool open)) return open;
            // 既定では、対象のオブジェクトを作った項目だけを開く。
            return st != null && Array.IndexOf(st.OutputIndices, _subjectIndex) >= 0;
        }

        private void UpdateHistoryStep(HistoryStep hs, ModifierStackStepView st)
        {
            bool open = IsHistoryOpen(hs, st);
            hs.Body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            hs.Fold.text = open ? "▼" : "▶";
        }

        private ModifierStackStepView HistoryStepView(HistoryStep hs)
        {
            foreach (var g in _histories)
                if (g.Name == hs.StackName && hs.StepIndex < g.Steps.Length) return g.Steps[hs.StepIndex];
            return null;
        }

        private void ToggleHistoryStep(HistoryStep hs)
        {
            var st = HistoryStepView(hs);
            _historyOpen[hs.OpenKey] = !IsHistoryOpen(hs, st);
            UpdateHistoryStep(hs, st);
        }

        private bool RevealHistoryStep(HistoryStep hs)
        {
            var st = HistoryStepView(hs);
            if (IsHistoryOpen(hs, st)) return false;
            _historyOpen[hs.OpenKey] = true;
            UpdateHistoryStep(hs, st);
            return true;
        }

        private void OnHistoryArg(string stackName, int stepIndex, string key, string value)
        {
            SendCmd(new SetModifierStackStepArgsCommand(ModelIndex, stackName, stepIndex, new[] { key }, new[] { value ?? "" }));
            Refresh();
        }

        private void OnSelectInput(int index)
        {
            if (index < 0) return;
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Drawable, new[] { index }));
            Refresh();
        }

        private void OnDeleteHistory(string stackName)
        {
            // 削除すると設定は復元できない。作り直したいなら同じ操作を最初からやり直すことになるので確認を通す。
            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "履歴の削除の確認",
                $"履歴「{stackName}」を削除します。\n\n"
              + "入力と設定の記録が失われ、作り直せなくなります。\n"
              + "（作られたオブジェクトは残ります）",
                "削除", "キャンセル");
            if (!ok) return;
            SendCmd(new DeleteModifierStackCommand(ModelIndex, stackName));
            Refresh();
        }

        // ================================================================
        // 操作
        // ================================================================

        private void SendArgs(string itemId, string[] keys, string[] values, bool preview)
        {
            var it = FindItem(itemId);
            if (it == null || _chain == null) return;
            if (_isSubdiv)
            {
                // サブディビジョンのスタック：回数の確定だけを、既存スタックの引数の書き換え
                // （setModifierStackStepArgs。項目を作り直す）で送る。
                // SubdivideMeshCommand に子を渡す形は、子がスタックの出力なので受け口の保護（ModifierOutputGuard）が拒否する。
                // 子の作り直しは重いので、スライダーを動かしている間は送らない。
                if (preview) return;
                int si = -1;
                for (int i = 0; i < (_chain.Steps?.Length ?? 0); i++)
                    if (ReferenceEquals(_chain.Steps[i], it)) { si = i; break; }
                if (si < 0) { SetStatus("項目が見つかりません"); Refresh(); return; }
                SendCmd(new SetModifierStackStepArgsCommand(ModelIndex, _chain.Name, si, keys, values));
                Refresh();
                return;
            }
            SendCmd(new SetMeshModifierArgsCommand(ModelIndex, _chain.Name, itemId, keys, values, preview));
            Refresh();
        }

        private void OnAdd(string kind)
        {
            _menuBox.style.display = DisplayStyle.None;
            SetStatus("");
            // 足した直後に形が変わる既定値で足す（操作統合計画 段 3）。
            if (_chain != null)
                SendCmd(new AddMeshModifierCommand(ModelIndex, kind, stackName: _chain.Name, useAddDefaults: true));
            else if (_activeIndex >= 0)
                SendCmd(new AddMeshModifierCommand(ModelIndex, kind, stackName: null, sourceMasterIndex: _activeIndex, useAddDefaults: true));
            else { SetStatus("オブジェクトを選んでください"); return; }
            Refresh();
        }

        private void OnUp(string itemId)
        {
            int pos = PositionOf(itemId);
            if (_chain == null || pos <= 0 || _isSubdiv) return;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, itemId, _items[pos - 1].ItemId));
            Refresh();
        }

        private void OnDown(string itemId)
        {
            int pos = PositionOf(itemId);
            if (_chain == null || pos < 0 || pos >= _items.Count - 1 || _isSubdiv) return;
            string before = pos + 2 < _items.Count ? _items[pos + 2].ItemId : null;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, itemId, before));
            Refresh();
        }

        private void OnEnabled(string itemId, bool on)
        {
            var it = FindItem(itemId);
            if (_chain == null || it == null || it.Enabled == on || _isSubdiv) return;
            SendCmd(new SetMeshModifierEnabledCommand(ModelIndex, _chain.Name, itemId, on));
            Refresh();
        }

        private void OnRemove(string itemId)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            if (_isSubdiv)
            {
                // サブディビジョンのスタック：つながりを外し、子を消す。
                SendCmd(new ReleaseSubdivisionCommand(ModelIndex, _chain.ChainResultIndex, deleteChild: true));
                Refresh();
                return;
            }
            SendCmd(new RemoveMeshModifierCommand(ModelIndex, _chain.Name, itemId));
            Refresh();
        }

        private void OnApplyItem(string itemId, bool upTo)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            int pos = PositionOf(itemId);
            SetStatus(!upTo && pos > 0 ? "先頭のモディファイアではないので、前のモディファイアを飛ばして元の形に適用しました。形が変わることがあります" : "");
            SendCmd(new ApplyMeshModifierCommand(ModelIndex, _chain.Name, itemId, upTo));
            Refresh();
        }

        private void OnDuplicate(string itemId)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            SendCmd(new DuplicateMeshModifierCommand(ModelIndex, _chain.Name, itemId));
            Refresh();
        }

        private void OnMoveTo(string itemId, bool first)
        {
            if (_chain == null || FindItem(itemId) == null || _items.Count == 0) return;
            SendCmd(new MoveMeshModifierCommand(ModelIndex, _chain.Name, itemId, first ? _items[0].ItemId : null));
            Refresh();
        }

        private void OnCopyToSelected(string itemId)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            SendCmd(new CopyMeshModifierToObjectsCommand(ModelIndex, _chain.Name, itemId));
            Refresh();
        }

        private void OnShowUpTo(string itemId)
        {
            if (_chain == null || FindItem(itemId) == null) return;
            SendCmd(new SetModifierDisplayUpToCommand(ModelIndex, _chain.Name, _chain.DisplayUpToItemId == itemId ? null : itemId));
            Refresh();
        }

        private void OnShowCage(bool visible)
        {
            if (_chain == null || _chain.CageVisible == visible) return;
            SendCmd(new SetModifierCageVisibleCommand(ModelIndex, _chain.Name, visible));
            Refresh();
        }

        private void OnApply()
        {
            if (_chain == null) return;
            // サブディビジョンも同じ。結果の形が元の名前のオブジェクトになる（操作統合計画 段 2）。
            // 送ると通知で Refresh が走り _chain は null になる。名前は先に控える。
            string name = _chain.Name;
            SendCmd(new ApplyMeshModifierChainCommand(ModelIndex, name));
            Refresh();
        }

        // ================================================================
        // 小物
        // ================================================================

        private void SetStatus(string text) { if (_statusLabel != null) _statusLabel.text = text ?? ""; }

        /// <summary>末尾固定の種類の項目か。</summary>
        private bool IsPinned(ModifierStackStepView it)
            => !_isSubdiv && it != null && (MeshModifierRegistry.Find(it.Action)?.PinnedLast ?? false);

        private static string Fmt(object v)
            => PanelCommandFactory.TryFormatValue(v, out string s) ? s : Convert.ToString(v, CultureInfo.InvariantCulture);

        private static Color StateColor(ModifierStackStepView it)
        {
            if (it == null || !it.Enabled) return Color.white;
            switch (it.State)
            {
                case "Failed":  return new Color(1f, 0.45f, 0.4f);
                case "Blocked": return new Color(1f, 0.78f, 0.35f);
                default:        return Color.white;
            }
        }

        private static Button MkBtn(string t, Action a)
        {
            var b = new Button(a) { text = t };
            b.style.height = 22;
            return b;
        }

        private static Button MkSmallBtn(string t, Action a, float width)
        {
            var b = new Button(a) { text = t };
            b.style.width = width;
            b.style.height = 20;
            b.style.flexShrink = 0;
            b.style.marginLeft = 1;
            b.style.marginRight = 1;
            b.style.paddingLeft = b.style.paddingRight = 0;
            return b;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(HeadingColor);
            l.style.fontSize = 10;
            l.style.marginBottom = 2;
            l.style.marginTop = 2;
            return l;
        }
    }
}
