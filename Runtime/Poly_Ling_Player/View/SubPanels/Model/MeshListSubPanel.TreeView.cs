// MeshListSubPanel.TreeView.cs
// オブジェクトリストのサブパネル：ツリービューの構築（タブ・行の生成とバインド・行内ボタン・ミラー欄・インデント）。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;
using PlayerIoUiKit        = Poly_Ling.Player.PlayerIoUiKit;
using PlayerUiPrefs        = Poly_Ling.Player.PlayerUiPrefs;
using ObjectMoveSettings   = Poly_Ling.Tools.ObjectMoveSettings;
using ParameterLimits      = Poly_Ling.Core.ParameterLimits;
using RecentPaths          = Poly_Ling.Core.RecentPaths;
using PartsDictionaryPath  = Poly_Ling.Core.PartsDictionaryPath;
using MeshRenameCsvHelper  = Poly_Ling.UI.MeshRenameCsvHelper;

namespace Poly_Ling.MeshListV2
{
    public partial class MeshListSubPanel
    {
        // ================================================================
        // SetupTreeView（エディタ版と同一）
        // ================================================================

        private void SetupTreeView()
        {
            if (_treeView == null) return;
            _treeView.fixedItemHeight    = 20;
            _treeView.horizontalScrollingEnabled = true;
            SetupScrollerStability();
            _treeView.makeItem           = MakeTreeItem;
            _treeView.bindItem           = BindTreeItem;
            _treeView.selectionType      = SelectionType.Multiple;
            _treeView.selectionChanged   += OnSelectionChanged;
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
        }

        // ================================================================
        // 横スクロールバーのちらつき対策
        // ================================================================

        // 縦操作中は横スクロールバーの出入りを止める。
        // 既定の Auto は内容幅の変化で出たり消えたりし、そのたびに
        // 表示領域の高さが変わってリストが揺れる。
        private ScrollView _treeScroll;
        private bool _hScrollerLocked;
        private IVisualElementScheduledItem _hScrollerUnlock;

        private void SetupScrollerStability()
        {
            _treeScroll = _treeView?.Q<ScrollView>();
            if (_treeScroll == null) return;

            var vs = _treeScroll.verticalScroller;
            if (vs != null)
            {
                vs.RegisterCallback<PointerDownEvent>(_ => LockHorizontalScroller(), TrickleDown.TrickleDown);
                vs.RegisterCallback<PointerUpEvent>(_ => UnlockHorizontalScroller(), TrickleDown.TrickleDown);
                vs.RegisterCallback<PointerCaptureOutEvent>(_ => UnlockHorizontalScroller());
            }

            // ホイールは終端イベントが無いので、途切れてから解除する。
            _treeScroll.RegisterCallback<WheelEvent>(_ =>
            {
                LockHorizontalScroller();
                _hScrollerUnlock?.Pause();
                _hScrollerUnlock = _treeScroll.schedule.Execute(UnlockHorizontalScroller).StartingIn(200);
            }, TrickleDown.TrickleDown);
        }

        private void LockHorizontalScroller()
        {
            if (_hScrollerLocked || _treeScroll == null) return;
            var hs = _treeScroll.horizontalScroller;
            bool shown = hs != null && hs.resolvedStyle.display == DisplayStyle.Flex;
            _treeScroll.horizontalScrollerVisibility =
                shown ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Hidden;
            _hScrollerLocked = true;
        }

        private void UnlockHorizontalScroller()
        {
            if (!_hScrollerLocked || _treeScroll == null) return;
            _treeScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _hScrollerLocked = false;
        }

        // ================================================================
        // タブ切り替え（エディタ版と同一）
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            if (_currentTab == TabType.Morph && tab != TabType.Morph) SendEndMorphPreview();
            _currentTab = tab;
            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone,     tab == TabType.Bone);
            SetTabActive(_tabMorph,    tab == TabType.Morph);
            SetTabActive(_tabRigidBody, tab == TabType.RigidBody);
            SetTabActive(_tabJoint,    tab == TabType.Joint);

            bool simpleMode = IsSimpleMode;
            if (_tabHeader != null) _tabHeader.style.display = simpleMode ? DisplayStyle.None : DisplayStyle.Flex;

            if (simpleMode)
            {
                if (_indexInfo     != null) _indexInfo.style.display     = DisplayStyle.None;
                if (_bonePoseSection != null) _bonePoseSection.style.display = DisplayStyle.Flex;
                if (_mainContent   != null) _mainContent.style.display   = DisplayStyle.Flex;
                if (_morphEditor   != null) _morphEditor.style.display   = DisplayStyle.None;
            }
            else
            {
                if (_indexInfo != null)
                    _indexInfo.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                if (_bonePoseSection != null)
                    _bonePoseSection.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                bool isMorph = tab == TabType.Morph;
                if (_mainContent != null) _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
                if (_morphEditor != null) _morphEditor.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _selectedAdapters.Clear();
            bool showMorph = !simpleMode && tab == TabType.Morph;
            if (!showMorph) CreateTreeRoot();
            if (showMorph) RefreshMorphEditor();
            RefreshAllImmediate();
            // 名称一括変更はタブ（メッシュ／ボーン／モーフ…）ごとに独立させる
            ResetRenameState();
            RefreshSelectionDictionary();
            // タブでピック対象（ボーン / 描画メッシュ）が変わる。
            ApplyPickFilter();
            Log($"{tab} タブ");
        }

        private void SetTabActive(Button btn, bool active) => btn?.EnableInClassList("tab-active", active);

        private void OnDetailModeChanged()
        {
            _selectedAdapters.Clear();
            if (IsSimpleMode) SendEndMorphPreview();
            SwitchTab(IsSimpleMode ? TabType.Drawable : _currentTab);
            UpdateBonePosePanel();
            UpdateTransformPanel();
        }

        // ================================================================
        // CreateTreeRoot（エディタ版と同一）
        // ================================================================

        private void CreateTreeRoot()
        {
            var model = CurrentModel;
            if (model == null) return;

            IReadOnlyList<IMeshView> sourceList;
            MeshCategory category;

            if (IsSimpleMode)
            {
                var filtered = model.DrawableList?.Where(v => !v.HasBoneWeight).ToList() ?? new List<IMeshView>();
                sourceList = filtered;
                category   = MeshCategory.Drawable;
            }
            else
            {
                sourceList = _currentTab switch
                {
                    TabType.Drawable => model.DrawableList,
                    TabType.Bone     => model.BoneList,
                    TabType.RigidBody => model.RigidBodyList,
                    TabType.Joint    => model.RigidBodyJointList,
                    _                => null
                };
                category = CurrentCategory;
            }

            if (sourceList == null) return;

            bool   excludeMirror = !(_showMirrorSideToggle?.value ?? false);
            string filter        = _filterField?.value;

            _treeRoot = new SummaryTreeRoot();
            _treeRoot.ModelIndex   = ModelIndex;
            _treeRoot.SendCommand  = cmd =>
            {
                _genBeforeReorder = _applyTreeGeneration;
                _ctx?.SendCommand(cmd);
            };
            _treeRoot.OnChanged    = () =>
            {
                // SendCommand の同期通知で既にツリーを作り直していれば再構築しない。
                bool alreadyRebuilt = _genBeforeReorder >= 0 && _applyTreeGeneration != _genBeforeReorder;
                _genBeforeReorder = -1;
                _isReceiving = true;
                try
                {
                    if (!alreadyRebuilt) RefreshTreeImmediate();
                    SyncTreeViewSelection();
                    UpdateDetailPanel();
                }
                finally { _isReceiving = false; }
            };
            _treeRoot.Build(sourceList, category, excludeMirror, filter);
            SetupDragDrop();
        }

        // ================================================================
        // MakeItem / BindItem（エディタ版と同一）
        // ================================================================

        /// <summary>D&D が行要素からアイテムを引くための目印名。</summary>
        private const string TreeItemName = "pl-tree-item";

        private VisualElement MakeTreeItem()
        {
            var c = new VisualElement { name = TreeItemName };
            c.style.flexDirection = FlexDirection.Row;
            c.style.flexGrow = 1; c.style.alignItems = Align.Center;
            c.style.paddingLeft = 2; c.style.paddingRight = 4;

            var nameLabel = new Label { name = "name" };
            nameLabel.style.color = new StyleColor(Color.white);
            nameLabel.style.flexGrow = 1; nameLabel.style.flexShrink = 0;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft; nameLabel.style.marginRight = 4;
            c.Add(nameLabel);

            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80; infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new StyleColor(Color.white);
            infoLabel.style.fontSize = 11; infoLabel.style.marginRight = 4;
            c.Add(infoLabel);

            // 担当者バッジ（名前の直後・情報ラベルの手前）
            var editorBadge = new Label { name = "editor-badge" };
            editorBadge.style.flexShrink = 0;
            editorBadge.style.fontSize = 10;
            editorBadge.style.marginRight = 4;
            editorBadge.style.paddingLeft = 4; editorBadge.style.paddingRight = 4;
            editorBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            editorBadge.style.display = DisplayStyle.None;
            c.Insert(1, editorBadge);

            // 行内ボタンを押した瞬間の Ctrl 状態を控える。
            // Clickable はクリックを消費するので行の選択は起きない。Ctrl のときだけ
            // ボタンの機能を無視して選択の反転に振り替える（下の RunRowButton）。
            // MakeTreeItem は行要素の生成時に1回だけ呼ばれるので、ここで登録する
            // （BindTreeItem で登録するとハンドラが行の再利用のたびに積み重なる）。
            c.RegisterCallback<PointerDownEvent>(
                e => _rowCtrlDown = e.ctrlKey || e.commandKey, TrickleDown.TrickleDown);

            var attr = new VisualElement(); attr.style.flexDirection = FlexDirection.Row; attr.style.flexShrink = 0;
            var editorBtn = MkAttrBtn("editor-btn", "\u25CB", "編集者の取得／解放");
            attr.Add(editorBtn);
            var visBtn = MkAttrBtn("vis-btn", "◉", "可視性切り替え");
            var lockBtn = MkAttrBtn("lock-btn", "■", "ロック切り替え");
            var symBtn  = MkAttrBtn("sym-btn", "⇆", "対称切り替え");
            attr.Add(visBtn); attr.Add(lockBtn); attr.Add(symBtn);
            c.Add(attr);

            c.userData = new TreeItemCache
            {
                NameLabel = nameLabel, InfoLabel = infoLabel,
                VisBtn = visBtn, LockBtn = lockBtn, SymBtn = symBtn,
                EditorBadge = editorBadge, EditorBtn = editorBtn,
            };
            return c;
        }

        private Button MkAttrBtn(string name, string icon, string tip)
        {
            var b = new Button { name = name, text = icon, tooltip = tip };
            b.style.width = 24; b.style.height = 18;
            b.style.marginLeft = 1; b.style.marginRight = 1;
            b.style.paddingLeft = 0; b.style.paddingRight = 0; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.fontSize = 12;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            return b;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<SummaryTreeAdapter>(index);
            if (adapter == null) return;
            var cache = element.userData as TreeItemCache;
            if (cache == null) return;
            cache.Adapter = adapter;

            if (cache.NameLabel != null)
            {
                if (adapter.IsMirrorSide)
                {
                    // 選択できる行を薄く描かない。薄さは「選べない（生成ミラー）」の印。
                    cache.NameLabel.text = $"\u25C7 {adapter.DisplayName}";
                    cache.NameLabel.style.opacity = adapter.IsSelectionBlocked ? 0.4f : 1f;
                }
                else if (adapter.IsRealSide)
                { cache.NameLabel.text = $"\u21C6 {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else if (adapter.HasBakedMirrorChild)
                { cache.NameLabel.text = $"\u21C6B {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else
                { cache.NameLabel.text = adapter.DisplayName; cache.NameLabel.style.opacity = 1f; }
            }

            if (cache.InfoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                cache.InfoLabel.text = showInfo
                    ? (_currentTab == TabType.Bone ? $"Bone:{adapter.MeshView.BoneIndex}" : adapter.GetInfoString())
                    : "";
                cache.InfoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            BindEditor(cache, adapter);
            ApplyIndentWidth(element);

            BindAttrBtn(cache.VisBtn, adapter, adapter.IsVisible, "◉", "−",
                () => ToggleVisibilityFromRow(adapter));
            BindAttrBtn(cache.LockBtn, adapter, adapter.IsLocked, "■", "□",
                () => ToggleLockFromRow(adapter));

            if (cache.SymBtn != null)
            {
                bool show = _currentTab == TabType.Drawable;
                cache.SymBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show) BindMirrorBtn(cache.SymBtn, adapter);
            }
        }

        // ================================================================
        // 行内ボタン
        //
        // 【役割の分離】
        //   行内のボタン  … 押した行の反転値を対象全件へ揃える。
        //                   押した行が選択に含まれれば選択全件、含まれなければその行だけ。
        //                   どちらの場合も選択そのものは変えない。
        //   ボタン行の一括 … 選択されている行すべてに、決まった値を設定する。
        //
        // 【Ctrl の例外】
        //   Ctrl を押しながら行内ボタンを押した場合、ボタンの機能は実行せず、
        //   その行の選択だけを反転する。行のどこを押しても Ctrl の意味が同じになる。
        // ================================================================

        /// <summary>
        /// 行内ボタンの共通入口。Ctrl 押下時は action を捨てて選択の反転に振り替える。
        /// action が null のボタン（ロック中）は、クリックを消費するだけで何もしない。
        /// </summary>
        private void RunRowButton(SummaryTreeAdapter adapter, Action action)
        {
            if (adapter == null) return;
            if (_rowCtrlDown) { FlipRowSelection(adapter); return; }
            action?.Invoke();
        }

        /// <summary>
        /// その行の選択状態だけを反転する。他の行の選択はそのまま残す。
        /// TreeView 側の表示は Attributes/Selection 通知後の SyncTreeViewSelection が追従する。
        /// </summary>
        private void FlipRowSelection(SummaryTreeAdapter adapter)
        {
            if (adapter == null || adapter.MasterIndex < 0) return;

            var indices = new List<int>(SelIndices());
            if (!indices.Remove(adapter.MasterIndex))
                indices.Add(adapter.MasterIndex);

            SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices.ToArray()));
        }

        /// <summary>
        /// 行内ボタンの適用対象を決める。
        ///   押した行が選択に含まれる → 選択されている行すべて
        ///   含まれない             → 押した行だけ
        /// exclude を渡すと、その条件に合う行を対象から外す（ミラーのロック行など）。
        /// </summary>
        private int[] RowTargets(SummaryTreeAdapter adapter, Func<SummaryTreeAdapter, bool> exclude = null)
        {
            if (adapter == null || adapter.MasterIndex < 0) return Array.Empty<int>();

            bool inSelection = _selectedAdapters.Any(a => a.MasterIndex == adapter.MasterIndex);
            if (!inSelection)
                return (exclude != null && exclude(adapter))
                    ? Array.Empty<int>()
                    : new[] { adapter.MasterIndex };

            return _selectedAdapters
                .Where(a => a.MasterIndex >= 0 && (exclude == null || !exclude(a)))
                .Select(a => a.MasterIndex)
                .ToArray();
        }

        /// <summary>
        /// 可視性。押した行の反転値を対象全件へ揃える。
        /// 「見えている行の目を押したら、選択全部の目を閉じる」という動きになる。
        /// </summary>
        private void ToggleVisibilityFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return;
            var targets = RowTargets(adapter);
            if (targets.Length == 0) return;
            SendCmd(new SetBatchVisibilityCommand(ModelIndex, targets, !adapter.IsVisible));
        }

        /// <summary>ロック。押した行の反転値を対象全件へ揃える。</summary>
        private void ToggleLockFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return;
            var targets = RowTargets(adapter);
            if (targets.Length == 0) return;
            SendCmd(new SetBatchLockCommand(ModelIndex, targets, !adapter.IsLocked));
        }

        /// <summary>
        /// ミラーの有無。押した行の反転値を対象全件へ揃える。
        /// 0 → 1（分離）、1 または 2 → 0。結合(2) はここでは作らない。
        /// ロック行（ミラー側・PMX 由来）は対象から外す。
        /// </summary>
        private void SetMirrorFromRow(SummaryTreeAdapter adapter)
        {
            if (adapter == null || HasLiveMirrorPeer(adapter)) return;
            // ミラー側メッシュが実在する行は対象から外す。ペアを解体させないため。
            var targets = RowTargets(adapter, HasLiveMirrorPeer);
            if (targets.Length == 0) return;
            // 属性を書くだけでなく、ミラー側メッシュの生成・始末まで行う。
            SendCmd(new SetMirrorEnabledCommand(ModelIndex, targets, adapter.MirrorType == 0));
        }

        // ================================================================
        // ミラー欄の表示
        // ================================================================

        /// <summary>
        /// ミラーボタンの表示と操作を組み立てる。
        ///
        /// このボタンは「ミラーの有無」だけを扱うフラグとする。
        ///   ⇆    : MirrorType が 1（分離）または 2（結合）
        ///   空欄 : MirrorType が 0（なし）
        ///   🪞   : ミラー側そのもの。実体側の従属なので操作させない
        ///
        /// 押すと 0 → 1、1 または 2 → 0。結合(2) にするのは詳細欄のモード選択で行う。
        /// モード・軸・ミラー側メッシュの有無はツールチップで確認できる。
        ///
        /// 【操作できる範囲】
        /// ミラー側メッシュが実在する行（MirrorPair の実体側 / ベイクミラーの実体側 /
        /// ミラー側そのもの）はロックする。これらは MirrorType を変えても
        /// ミラー実体との対応が変わらず、属性だけが実体と食い違うため。
        /// 操作できるのは「MQO 由来の実体なしミラー」＝ミラー側メッシュを持たない行だけ。
        /// </summary>
        private void BindMirrorBtn(Button btn, SummaryTreeAdapter adapter)
        {
            if (btn == null || adapter == null) return;

            bool locked = IsMirrorLocked(adapter);
            bool on     = adapter.MirrorType > 0;

            // OFF のときもラベルを出す。空文字だと「押せるボタンがそこにある」ことが
            // 見えず、ミラーを掛ける入口が存在しないように見えてしまう。
            // 状態の区別は不透明度で付ける（ON=不透明 / OFF=薄い / ロック=中間）。
            btn.text = adapter.IsMirrorSide ? "\u25C7" : "\u21C6";
            btn.style.opacity = locked ? 0.5f : (on ? 1f : 0.28f);
            btn.tooltip = adapter.IsMirrorSide
                ? "ミラー側メッシュ。実体側の従属なのでここでは変更できません。"
                : MirrorTooltip(adapter);

            // ロック中も SetEnabled(false) にはしない。無効な要素はクリックを処理しないため、
            // イベントが行へ渡って選択が差し替わってしまう。
            // 有効なままクリックだけ消費し、何もしないようにする。
            btn.SetEnabled(true);
            btn.clickable = new Clickable(
                () => RunRowButton(adapter, locked ? (Action)null : () => SetMirrorFromRow(adapter)));
        }

        /// <summary>
        /// ミラーの変更をロックする行か。
        ///
        /// ミラー側そのもの（IsMirrorSide）だけをロックする。実体側の従属で、
        /// ここで切り替えても実体側との対応が変わらないため。
        ///
        /// スキニング済みメッシュは特別扱いしない。
        ///   ・分岐の中にあるメッシュは、変換時に反対側ボーンが作られ
        ///     MirrorBoneWeight を持つ。ミラーを掛ければ PMX 型ミラーが
        ///     正しいボーンに紐づいて生成される（CreateDerivedMirrorContext）。
        ///   ・分岐の外にあるメッシュは反対側ボーンが存在しない。
        ///     実体側と同じボーンで動く鏡像になるが、これは中心線上の
        ///     オブジェクトを鏡像化する MQO 系ミラーと同じ結果であり、
        ///     分岐を張らなかったという指定どおりの挙動。
        /// </summary>
        private bool IsMirrorLocked(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return true;
            return adapter.IsMirrorSide;
        }

        /// <summary>
        /// ミラーの有無を切り替える操作（SetMirrorEnabledCommand）の対象から外す行か。
        ///
        /// ミラー側メッシュが実在する行を外す。実体側で切ると DisableMirror が
        /// ミラー側を Mesh へ降格させ MirrorPairs から外す（＝ペアの解体）。
        /// ミラー側はウェイトを持つ独立メッシュなので、選択・編集の途中で
        /// ペアが解体されてはならない。ミラーの有無を扱えるのは、ミラー側メッシュを
        /// まだ持たない行だけ。
        ///
        /// 選択できるかどうかとは別の判定であり、混ぜないこと。
        /// 選択の可否は SummaryTreeAdapter.IsSelectionBlocked。
        /// </summary>
        private static bool HasLiveMirrorPeer(SummaryTreeAdapter adapter)
        {
            if (adapter == null) return true;
            return adapter.IsMirrorSide || adapter.IsBakedMirror
                || adapter.IsRealSide  || adapter.HasBakedMirrorChild;
        }

        private string MirrorTooltip(SummaryTreeAdapter adapter)
        {
            string entity =
                adapter.IsRealSide          ? "ミラー側メッシュあり（ペア同期）" :
                adapter.HasBakedMirrorChild ? "ミラー側メッシュあり（ベイク済み）" :
                                              "ミラー側メッシュなし";
            string howto = IsMirrorLocked(adapter)
                ? "ミラー側メッシュ。実体側の従属なので変更できません"
                : "クリックでミラーの有無を切り替え（結合は詳細欄で設定）";
            return $"ミラー: {MirrorViewUtil.TypeName(adapter.MirrorType)}"
                 + $" / 軸: {MirrorViewUtil.AxisLetter(adapter.MirrorAxis)}"
                 + $"\n{entity}"
                 + $"\n{howto}";
        }

        // ================================================================
        // インデント幅
        // ================================================================

        /// <summary>
        /// 行のインデントを現在値に合わせる。
        ///
        /// TreeView 既定のインデント段組みは、段数と要素幅の対応が実装依存で、
        /// 要素ごとに幅を上書きしても段数を保てない。そこで既定の段組みは
        /// 幅0＋非表示にして無効化し、インデントは自分が生成した行要素
        /// （MakeTreeItem の pl-tree-item）の marginLeft で与える。
        /// この要素は自前で作っているので、必ず反映される。
        ///
        /// 深さは SummaryTreeAdapter.GetDepth()（Parent 連鎖）から取る。
        /// フィルタで親が落ちた場合もツリー実体に従うため、表示とずれない。
        ///
        /// なお既定の段組みを消したぶん、開閉トグルの矢印は深さによらず
        /// 左端に揃う。名前だけが深さぶん右へずれる。
        /// </summary>
        private void ApplyIndentWidth(VisualElement rowContent)
        {
            if (rowContent == null) return;
            var adapter = (rowContent.userData as TreeItemCache)?.Adapter;
            if (adapter == null) return;

            // 既定の段組みを無効化する。祖先を遡って最初に見つかった階層で打ち切る
            // （兄弟行は子孫に含まれないので、他行を巻き込むことはない）。
            var e = rowContent.parent;
            for (int up = 0; e != null && up < 4; up++, e = e.parent)
            {
                bool found = false;
                e.Query(className: BaseTreeView.itemIndentUssClassName)
                 .ForEach(x => { x.style.width = 0f; x.style.display = DisplayStyle.None; found = true; });
                if (found) break;
            }

            rowContent.style.marginLeft = adapter.GetDepth() * _treeIndentWidth;
        }

        /// <summary>表示中の全行のインデントを更新する（スライダー操作時）。</summary>
        private void ApplyIndentWidthToVisibleRows()
        {
            if (_treeView == null) return;
            _treeView.Query(name: TreeItemName).ForEach(ApplyIndentWidth);
        }

        /// <summary>
        /// 担当者バッジと取得／解放ボタンを行に反映する。
        ///
        /// 表示規則:
        ///   担当者なし → バッジ非表示 / ボタン「✋」（押すと取得）
        ///   自分が担当 → バッジ着色   / ボタン「✔」（押すと解放）
        ///   他人が担当 → バッジ着色   / ボタン無効・行を淡色化
        /// </summary>
        private void BindEditor(TreeItemCache cache, SummaryTreeAdapter adapter)
        {
            string editor = adapter.EditorName ?? "";
            bool hasEditor = editor.Length > 0;
            bool isMine    = hasEditor && editor == LocalUserName;
            bool isOthers  = hasEditor && !isMine;

            if (cache.EditorBadge != null)
            {
                cache.EditorBadge.style.display = hasEditor ? DisplayStyle.Flex : DisplayStyle.None;
                if (hasEditor)
                {
                    cache.EditorBadge.text = editor;
                    var col = EditorColor(editor);
                    cache.EditorBadge.style.color = new StyleColor(col);
                    cache.EditorBadge.style.backgroundColor =
                        new StyleColor(new Color(col.r, col.g, col.b, 0.15f));
                }
            }

            // 他人の担当は編集できないので行を淡くして触れないことを示す
            if (cache.NameLabel != null && isOthers)
                cache.NameLabel.style.opacity = 0.55f;

            if (cache.EditorBtn == null) return;

            bool canOperate = !string.IsNullOrEmpty(LocalUserName) && !isOthers;
            // SetEnabled(false) にはしない。無効な要素はクリックを処理しないため、
            // イベントが行へ渡って選択が差し替わってしまう。
            // 有効なままクリックを消費し、下のハンドラが canOperate で弾く。
            cache.EditorBtn.SetEnabled(true);
            cache.EditorBtn.tooltip = isOthers
                ? $"{editor} が編集中です"
                : (isMine ? "編集者を解放する" : "自分を編集者に設定する");
            cache.EditorBtn.style.opacity = canOperate ? 1f : 0.35f;

            // 取得なら自分の名前、解放なら空文字を送る。
            // ObjectIds を添えることでサーバ側がリスト構造のズレを検出できる。
            string icon = isMine ? "\u25CF" : "\u25CB";
            string next = isMine ? "" : LocalUserName;
            BindAttrBtn(cache.EditorBtn, adapter, true, icon, icon,
                () =>
                {
                    if (!canOperate) return;
                    SendCmd(new SetObjectEditorCommand(
                        ModelIndex, new[] { adapter.MasterIndex }, next,
                        new[] { adapter.ObjectId }));
                });
        }

        /// <summary>選択中のオブジェクトをまとめて取得／解放する（パネルの一括操作用）。</summary>
        public void ClaimSelected(bool claim)
        {
            if (string.IsNullOrEmpty(LocalUserName)) return;
            var indices = SelIndices();
            if (indices.Length == 0) return;

            var ids = new ulong[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                var a = _selectedAdapters.FirstOrDefault(x => x.MasterIndex == indices[i]);
                ids[i] = a?.ObjectId ?? 0UL;
            }

            SendCmd(new SetObjectEditorCommand(
                ModelIndex, indices, claim ? LocalUserName : "", ids));
        }

        /// <summary>
        /// 行内ボタンの見た目とクリック処理を設定する。
        /// クリックは必ず RunRowButton を通し、Ctrl 押下時は選択の反転に振り替える。
        /// </summary>
        private void BindAttrBtn(Button btn, SummaryTreeAdapter adapter,
                                 bool active, string onIcon, string offIcon, Action click)
        {
            if (btn == null) return;
            btn.text = active ? onIcon : offIcon;
            btn.style.opacity = active ? 1f : 0.3f;
            btn.clickable = new Clickable(() => RunRowButton(adapter, click));
        }
    }
}
