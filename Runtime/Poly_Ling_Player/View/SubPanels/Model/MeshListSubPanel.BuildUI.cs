// MeshListSubPanel.BuildUI.cs
// オブジェクトリストのサブパネル：UI 構築（ビューポート操作モード・姿勢調整・名称一括変更の UI）。
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
        // UI構築（エディタ版はUXMLだが、ここではコードで同等構造を構築）
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft = 4; root.style.paddingRight  = 4;
            root.style.paddingTop  = 4; root.style.paddingBottom = 4;

            // ── パネル名
            var panelNameLabel = new Label("オブジェクトリスト");
            panelNameLabel.style.color = new StyleColor(Color.white);
            panelNameLabel.style.fontSize = 12;
            panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panelNameLabel.style.marginBottom = 3;
            root.Add(panelNameLabel);

            // ── ビューポート操作（3択）
            root.Add(BuildViewportOpModeRow());

            // ── スキンドメッシュ（= 詳細モード）トグル
            _detailModeToggle = new Toggle("スキンドメッシュ") { value = false, name = "detail-mode-toggle" };
            _detailModeToggle.style.color = new StyleColor(Color.white);
            _detailModeToggle.style.marginBottom = 3;
            root.Add(_detailModeToggle);

            // ── タブヘッダー（詳細モード時のみ表示）
            _tabHeader = new VisualElement { name = "tab-header" };
            _tabHeader.style.flexDirection = FlexDirection.Row;
            _tabHeader.style.marginBottom  = 3;
            _tabDrawable = MakeTabBtn("Mesh",  "tab-drawable");
            _tabBone     = MakeTabBtn("Bone",  "tab-bone");
            _tabMorph    = MakeTabBtn("Morph", "tab-morph");
            _tabRigidBody = MakeTabBtn("剛体",  "tab-rigidbody");
            _tabJoint    = MakeTabBtn("Joint", "tab-joint");
            _tabHeader.Add(_tabDrawable); _tabHeader.Add(_tabBone); _tabHeader.Add(_tabMorph);
            _tabHeader.Add(_tabRigidBody); _tabHeader.Add(_tabJoint);
            root.Add(_tabHeader);

            // ── カウント・フィルター行
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems    = Align.Center;
            topRow.style.marginBottom  = 3;

            _countLabel = new Label { name = "count-label" };
            _countLabel.style.color = new StyleColor(Color.white);
            topRow.Add(_countLabel);

            _showInfoToggle = new Toggle("情報を表示") { name = "show-info-toggle", value = true };
            _showInfoToggle.style.color = new StyleColor(Color.white);
            _showInfoToggle.tooltip = "情報表示"; _showInfoToggle.style.marginLeft = 4;
            topRow.Add(_showInfoToggle);

            _showMirrorSideToggle = new Toggle("ミラーも表示") { name = "show-mirror-toggle", value = false };
            _showMirrorSideToggle.style.color = new StyleColor(Color.white);
            _showMirrorSideToggle.tooltip = "ミラー側表示"; _showMirrorSideToggle.style.marginLeft = 2;
            topRow.Add(_showMirrorSideToggle);
            root.Add(topRow);

            // ── インデント幅
            var indentRow = new VisualElement();
            indentRow.style.flexDirection = FlexDirection.Row;
            indentRow.style.alignItems    = Align.Center;
            indentRow.style.marginBottom  = 3;

            var indentLabel = new Label("インデント:");
            indentLabel.style.color    = new StyleColor(Color.white);
            indentLabel.style.fontSize = 10;
            indentRow.Add(indentLabel);

            _indentSlider = new Slider(TreeIndentMin, TreeIndentMax)
            { name = "indent-slider", value = _treeIndentWidth };
            _indentSlider.style.flexGrow   = 1;
            _indentSlider.style.marginLeft = 4;
            _indentSlider.style.color      = new StyleColor(Color.white);
            indentRow.Add(_indentSlider);

            _indentValueLabel = new Label($"{(int)_treeIndentWidth}px") { name = "indent-value-label" };
            _indentValueLabel.style.color    = new StyleColor(Color.white);
            _indentValueLabel.style.fontSize = 10;
            _indentValueLabel.style.width    = 30;
            _indentValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            indentRow.Add(_indentValueLabel);
            root.Add(indentRow);

            // ── すべてのオブジェクトを選択
            var selectAllRow = new VisualElement();
            selectAllRow.style.flexDirection = FlexDirection.Row;
            selectAllRow.style.marginBottom  = 3;
            selectAllRow.Add(_btnSelectAllObjects = MakeSmallBtn("すべてのオブジェクトを選択", "btn-select-all",
                                          "リストにある全オブジェクトを選択する"));
            root.Add(selectAllRow);

            var filterLabel = new Label("フィルタ:");
            filterLabel.style.color    = new StyleColor(Color.white);
            filterLabel.style.fontSize = 10;
            filterLabel.style.marginTop = 2;
            root.Add(filterLabel);

            _filterField = new TextField { name = "filter-field" };
            _filterField.style.marginBottom = 3;
            root.Add(_filterField);

            // ── メインコンテンツ（ツリー + 詳細 + BonePose + Transform）
            _mainContent = new VisualElement { name = "main-content" };
            // flexGrow=1 だと親高いっぱいに伸び、内部 flexShrink でツリーの明示 height が縮み効かない。
            // モーフ側コンテナ(_morphEditor)と同じ自然高にして、ツリーの明示 height をそのまま効かせる。
            _mainContent.style.flexGrow = 0;

            _treeView = new TreeView { name = "mesh-tree" };
            _treeView.style.flexGrow  = 0;
            // TreeView は style.height を無視し minHeight/maxHeight で高さが決まるため、
            // 3つとも _treeHeight にして高さを厳密固定する（ドラッグ時も同様に設定）。
            _treeView.style.height    = _treeHeight;
            _treeView.style.minHeight = _treeHeight;
            _treeView.style.maxHeight = _treeHeight;
            _mainContent.Add(_treeView);
            AddListResizeHandle(_mainContent, _treeView,
                () => _treeHeight, h => { _treeHeight = h; _treeHeightUserAdjusted = true; }, TreeMinHeight);

            // 操作ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.flexWrap     = Wrap.Wrap;
            btnRow.style.marginTop    = 3;
            btnRow.Add(_btnAdd       = MakeSmallBtn("+",   "btn-add"));
            btnRow.Add(_btnMoveUp    = MakeSmallBtn("▲",  "btn-up"));
            btnRow.Add(_btnMoveDown  = MakeSmallBtn("▼",  "btn-down"));
            btnRow.Add(_btnOutdent   = MakeSmallBtn("←",  "btn-outdent"));
            btnRow.Add(_btnIndent    = MakeSmallBtn("→",  "btn-indent"));
            btnRow.Add(_btnDuplicate = MakeSmallBtn("Dup", "btn-duplicate"));
            btnRow.Add(_btnDelete    = MakeSmallBtn("Del", "btn-delete"));
            // 一括操作。対象は「選択されている行すべて」。
            // 行内のボタンは押した行 1 件だけなので、押す場所で対象が分かれる。
            btnRow.Add(_btnShow      = MakeSmallBtn("◉",  "btn-show",        "選択を可視にする"));
            btnRow.Add(_btnHide      = MakeSmallBtn("−",   "btn-hide",        "選択を不可視にする"));
            btnRow.Add(_btnLock      = MakeSmallBtn("■",  "btn-lock",        "選択をロックする"));
            btnRow.Add(_btnUnlock    = MakeSmallBtn("□",  "btn-unlock",      "選択のロックを解除する"));
            btnRow.Add(_btnMirrorOn  = MakeSmallBtn("⇆",   "btn-mirror-on",   "選択のミラーを有効にする"));
            btnRow.Add(_btnMirrorOff = MakeSmallBtn("⇆×",   "btn-mirror-off",  "選択のミラーを無効にする"));
            _mainContent.Add(btnRow);

            // ── 選択辞書（オブジェクト選択辞書）からの読み込み
            var selDicRow = new VisualElement();
            selDicRow.style.flexDirection = FlexDirection.Row;
            selDicRow.style.alignItems    = Align.Center;
            selDicRow.style.marginTop     = 2;
            selDicRow.style.marginBottom  = 2;

            var selDicLabel = new Label("選択辞書:");
            selDicLabel.style.color    = new StyleColor(Color.white);
            selDicLabel.style.fontSize = 10;
            selDicRow.Add(selDicLabel);

            _selDicDropdown = new DropdownField { name = "seldic-dropdown" };
            _selDicDropdown.style.flexGrow   = 1;
            _selDicDropdown.style.marginLeft = 4;
            _selDicDropdown.style.marginRight = 2;
            selDicRow.Add(_selDicDropdown);

            _btnSelDicApply = MakeSmallBtn("適用", "btn-seldic-apply");
            _btnSelDicAdd   = MakeSmallBtn("追加", "btn-seldic-add");
            selDicRow.Add(_btnSelDicApply); selDicRow.Add(_btnSelDicAdd);
            _mainContent.Add(selDicRow);

            // ── 名称一括変更（旧名→新名 CSV）
            _renameFoldout = new Foldout { text = "名称一括変更", value = false, name = "rename-foldout" };
            _renameFoldout.style.marginTop = 2;
            BuildRenameSection(_renameFoldout.contentContainer);
            _mainContent.Add(_renameFoldout);

            // 詳細Foldout
            _detailFoldout = new Foldout { text = "詳細", value = true, name = "detail-foldout" };
            _detailFoldout.style.marginTop = 4;
            BuildDetailFoldout(_detailFoldout.contentContainer);
            _mainContent.Add(_detailFoldout);

            // indexInfo（ボーンタブ用）
            _indexInfo = new VisualElement { name = "index-info" };
            _boneIndexLabel   = MakeInfoLabel("bone-index-label");
            _masterIndexLabel = MakeInfoLabel("master-index-label");
            _indexInfo.Add(_boneIndexLabel); _indexInfo.Add(_masterIndexLabel);
            _mainContent.Add(_indexInfo);

            root.Add(_mainContent);

            // ── モーフエディタ（詳細モード+Morphタブ時のみ表示）
            _morphEditor = new VisualElement { name = "morph-editor" };
            _morphEditor.style.display = DisplayStyle.None;
            BuildMorphEditor(_morphEditor);
            root.Add(_morphEditor);

            // ── 姿勢調整（常に表示。ビューポート操作モードに関わらず出す）
            root.Add(BuildObjectPoseSection());

            // ── ステータス
            _statusLabel = new Label("") { name = "status-label" };
            _statusLabel.style.color = new StyleColor(Color.white);
            root.Add(_statusLabel);
        }

        // ================================================================
        // ビューポート操作モード（3択）
        // ================================================================

        private VisualElement BuildViewportOpModeRow()
        {
            _viewportOpMode = (ViewportOpMode)PlayerUiPrefs.GetInt(
                ViewportOpModeKey, (int)ViewportOpMode.ObjectPose);

            var box = new VisualElement();
            box.style.marginBottom = 3;

            var lbl = new Label("ビューポート操作:");
            lbl.style.color    = new StyleColor(Color.white);
            lbl.style.fontSize = 10;
            box.Add(lbl);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            Button MakeOpBtn(string text, string tip, ViewportOpMode mode)
            {
                var b = new Button(() => SetViewportOpMode(mode)) { text = text, tooltip = tip };
                b.style.flexGrow      = 1;
                b.style.height        = 20;
                b.style.fontSize      = 9;
                b.style.marginRight   = 2;
                b.style.paddingLeft   = 2;
                b.style.paddingRight  = 2;
                row.Add(b);
                return b;
            }

            _btnOpNone   = MakeOpBtn("操作なし", "3D 操作を受け付けない（視点操作だけ）",
                                     ViewportOpMode.None);
            _btnOpSelect = MakeOpBtn("要素選択", "頂点・辺・面の選択だけを行う（移動ギズモは出さない）",
                                     ViewportOpMode.SelectElem);
            _btnOpPose   = MakeOpBtn("姿勢調整", "オブジェクト原点を選び、姿勢を調整する（描画オブジェクトの姿勢と同じ）",
                                     ViewportOpMode.ObjectPose);
            _btnOpPose.style.marginRight = 0;

            box.Add(row);
            UpdateViewportOpModeButtons();
            return box;
        }

        private void SetViewportOpMode(ViewportOpMode mode)
        {
            _viewportOpMode = mode;
            PlayerUiPrefs.SetInt(ViewportOpModeKey, (int)mode);
            UpdateViewportOpModeButtons();
            OnViewportOpModeChanged?.Invoke(mode);
        }

        private void UpdateViewportOpModeButtons()
        {
            void Style(Button b, bool active)
            {
                if (b == null) return;
                b.style.backgroundColor = new StyleColor(
                    active ? new Color(0.25f, 0.45f, 0.7f) : new Color(0.2f, 0.2f, 0.2f));
                b.style.color = new StyleColor(Color.white);
            }
            Style(_btnOpNone,   _viewportOpMode == ViewportOpMode.None);
            Style(_btnOpSelect, _viewportOpMode == ViewportOpMode.SelectElem);
            Style(_btnOpPose,   _viewportOpMode == ViewportOpMode.ObjectPose);
        }

        // ================================================================
        // 姿勢調整（ObjectMoveSettings と双方向同期）
        // ================================================================

        /// <summary>
        /// 「描画オブジェクトの姿勢」タブと同じ操作チェックを、オブジェクトリストにも置く。
        /// 実体は ObjectMoveTool の共有 ObjectMoveSettings 1 個なので、
        /// どちらのパネルで変えても同じ設定を触る。
        /// 数値での位置・回転・スケールは既存の「トランスフォーム」に集約してあるため、
        /// ここには置かない（同じ値の入力欄を 2 か所に作らない）。
        /// </summary>
        private VisualElement BuildObjectPoseSection()
        {
            var box = new VisualElement { name = "object-pose-section" };
            box.Add(Separator());
            box.Add(SectionHeader("姿勢調整"));

            _toggleOriginOnly        = new Toggle("原点だけ移動") { value = false };
            _toggleMoveWithChildren  = new Toggle("子を一緒に移動") { value = true };
            _toggleShowMoveGizmo     = new Toggle("移動ギズモを表示") { value = true };
            _toggleShowRotationGizmo = new Toggle("回転ギズモを表示") { value = false };

            _toggleOriginOnly.style.color        = new StyleColor(Color.white);
            _toggleMoveWithChildren.style.color  = new StyleColor(Color.white);
            _toggleShowMoveGizmo.style.color     = new StyleColor(Color.white);
            _toggleShowRotationGizmo.style.color = new StyleColor(Color.white);

            _toggleShowMoveGizmo.tooltip =
                "OFF にすると矢印と中央ハンドルを消し、当たり判定も止める"
                + "（オブジェクト原点をクリックで選びやすくする）";
            _toggleShowRotationGizmo.tooltip =
                "OFF にすると回転リングを消し、当たり判定も止める";

            _toggleOriginOnly.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.OriginOnly = e.newValue;

                // 「原点だけ移動」を ON にしたときだけ「子を一緒に移動」を OFF にする。
                // OFF にしたときは連動しない。
                if (e.newValue)
                {
                    if (s != null) s.MoveWithChildren = false;
                    _suppressMoveSettings = true;
                    try { _toggleMoveWithChildren?.SetValueWithoutNotify(false); }
                    finally { _suppressMoveSettings = false; }
                }
            });
            _toggleMoveWithChildren.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.MoveWithChildren = e.newValue;
            });
            _toggleShowMoveGizmo.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.AllowMoveGizmo = e.newValue;
                OnGizmoRefresh?.Invoke();
            });
            _toggleShowRotationGizmo.RegisterValueChangedCallback(e =>
            {
                if (_suppressMoveSettings) return;
                var s = GetObjectMoveSettings?.Invoke();
                if (s != null) s.AllowRotationGizmo = e.newValue;
                OnGizmoRefresh?.Invoke();
            });

            box.Add(_toggleOriginOnly);
            box.Add(_toggleMoveWithChildren);
            box.Add(_toggleShowMoveGizmo);
            box.Add(_toggleShowRotationGizmo);
            box.Add(BuildQuickOffsetRow());
            return box;
        }

        /// <summary>
        /// 選択対象のローカル姿勢を決め打ちの量だけ動かすボタン行。
        /// 「描画オブジェクトの姿勢」タブと同じ並び・同じ動きにする。
        /// </summary>
        private VisualElement BuildQuickOffsetRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;

            Button Make(string text, string tip, Action onClick)
            {
                var b = new Button(onClick) { text = text, tooltip = tip };
                b.style.flexGrow     = 1;
                b.style.height       = 20;
                b.style.fontSize     = 9;
                b.style.marginRight  = 2;
                b.style.paddingLeft  = 2;
                b.style.paddingRight = 2;
                row.Add(b);
                return b;
            }

            _btnQuickRotZPlus = Make("Z90度回転", "選択対象のローカル Z 回転に +90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, 90f, "Z+90度回転"));
            _btnQuickMoveYPlus = Make("Y0.1移動", "選択対象のローカル Y 位置に +0.1 を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.PositionY, 0.1f, "Y+0.1移動"));
            var last = Make("Z-90度回転", "選択対象のローカル Z 回転に −90 度を足す",
                 () => OffsetTransform(SetBoneTransformValueCommand.Field.RotationZ, -90f, "Z-90度回転"));
            last.style.marginRight = 0;
            _btnQuickRotZMinus = last;

            return row;
        }

        /// <summary>
        /// ローカル姿勢の 1 軸を相対で動かす。
        ///
        /// 「原点だけ移動」の自頂点再ローカル化・スキン固定の BindPose 追従・Undo は
        /// PlayerCommandDispatcher の Begin → Set → End 経路が持っている。
        /// ここでは現在値に差分を足した絶対値を求めて既存経路へ渡すだけにする
        /// （後処理を書き写さない）。
        ///
        /// SetBoneTransformValueCommand は配列全員へ同じ値を代入するため、
        /// 現在値がオブジェクトごとに違う相対操作では 1 件ずつ送る必要がある。
        /// 対象はポーズを持たないもの（＝トランスフォーム欄と同じ集合）。
        /// </summary>
        private void OffsetTransform(
            SetBoneTransformValueCommand.Field field, float delta, string undoLabel)
        {
            if (_ctx == null) return;

            var targets = _selectedAdapters
                .Where(a => !a.MeshView.BonePose.HasPose && a.MasterIndex >= 0)
                .ToList();
            if (targets.Count == 0) return;

            var indices = targets.Select(a => a.MasterIndex).ToArray();
            var s = GetObjectMoveSettings?.Invoke();

            SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, indices)
            {
                Mode       = s?.MoveMode ?? Poly_Ling.Tools.BoneMoveMode.BoneOnlyRebind,
                OriginOnly = s?.OriginOnly ?? false,
            });

            bool isRotation =
                field == SetBoneTransformValueCommand.Field.RotationX ||
                field == SetBoneTransformValueCommand.Field.RotationY ||
                field == SetBoneTransformValueCommand.Field.RotationZ;

            foreach (var a in targets)
            {
                var v = a.MeshView;
                float cur;
                switch (field)
                {
                    case SetBoneTransformValueCommand.Field.PositionX: cur = v.LocalPosition.x; break;
                    case SetBoneTransformValueCommand.Field.PositionY: cur = v.LocalPosition.y; break;
                    case SetBoneTransformValueCommand.Field.PositionZ: cur = v.LocalPosition.z; break;
                    case SetBoneTransformValueCommand.Field.RotationX: cur = v.LocalRotationEuler.x; break;
                    case SetBoneTransformValueCommand.Field.RotationY: cur = v.LocalRotationEuler.y; break;
                    case SetBoneTransformValueCommand.Field.RotationZ: cur = v.LocalRotationEuler.z; break;
                    default: continue;
                }

                float next = cur + delta;
                // 回転は押すたびに 360 を超えて伸びていくので毎回畳む。
                if (isRotation) next = NormAngle(next);

                SendCmd(new SetBoneTransformValueCommand(
                    ModelIndex, new[] { a.MasterIndex }, field, next));
            }

            SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, undoLabel));
            UpdateTransformPanel();
        }

        /// <summary>ObjectMoveSettings の現在値をチェックへ映す。パネル表示時に呼ぶ。</summary>
        public void SyncObjectPoseToggles()
        {
            var s = GetObjectMoveSettings?.Invoke();
            if (s == null) return;
            _suppressMoveSettings = true;
            try
            {
                _toggleOriginOnly?.SetValueWithoutNotify(s.OriginOnly);
                _toggleMoveWithChildren?.SetValueWithoutNotify(s.MoveWithChildren);
                _toggleShowMoveGizmo?.SetValueWithoutNotify(s.AllowMoveGizmo);
                _toggleShowRotationGizmo?.SetValueWithoutNotify(s.AllowRotationGizmo);
            }
            finally { _suppressMoveSettings = false; }
        }

        /// <summary>
        /// このパネルが担当する間の ObjectMoveTool のピック対象を決める。
        /// ボーンタブならボーン、それ以外は描画メッシュ（スキンドは除く）。
        /// </summary>
        public void ApplyPickFilter()
        {
            var s = GetObjectMoveSettings?.Invoke();
            if (s == null) return;
            bool boneTab = !IsSimpleMode && _currentTab == TabType.Bone;
            s.PickBones         = boneTab;
            s.PickMeshesNoSkin  = !boneTab;
            s.PickMeshesSkinned = false;
        }

        private void BuildDetailFoldout(VisualElement c)
        {
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.alignItems    = Align.Center;
            nameRow.style.marginBottom  = 3;

            var nameLabel = new Label("名前:");
            nameLabel.style.color    = new StyleColor(Color.white);
            nameLabel.style.fontSize = 10;
            nameLabel.style.width    = 34;
            nameRow.Add(nameLabel);

            _meshNameField = new TextField { name = "mesh-name-field" };
            _meshNameField.style.flexGrow = 1;
            nameRow.Add(_meshNameField);

            var applyBtn = new Button(() => ApplyMeshName()) { text = "変更" };
            applyBtn.style.width       = 36;
            applyBtn.style.height      = 18;
            applyBtn.style.fontSize    = 9;
            applyBtn.style.marginLeft  = 2;
            applyBtn.style.paddingTop  = 0;
            applyBtn.style.paddingBottom = 0;
            nameRow.Add(applyBtn);
            _btnApplyMeshName = applyBtn;

            c.Add(nameRow);
            _vertexCountLabel = MakeInfoLabel("vertex-count-label"); c.Add(_vertexCountLabel);
            _faceCountLabel   = MakeInfoLabel("face-count-label");   c.Add(_faceCountLabel);
            _triCountLabel    = MakeInfoLabel("tri-count-label");     c.Add(_triCountLabel);
            _quadCountLabel   = MakeInfoLabel("quad-count-label");    c.Add(_quadCountLabel);
            _ngonCountLabel   = MakeInfoLabel("ngon-count-label");    c.Add(_ngonCountLabel);

            _ignorePoseToggle = new Toggle("姿勢無視(アーマチャ)") { name = "ignore-pose-toggle" };
            _ignorePoseToggle.style.color        = new StyleColor(Color.white);
            _ignorePoseToggle.style.marginTop    = 4;
            _ignorePoseToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetIgnorePoseCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_ignorePoseToggle);

            _mirrorModeDropdown = new DropdownField { name = "mirror-mode-dropdown" };
            _mirrorModeDropdown.choices = MirrorModeChoices;
            _mirrorModeDropdown.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                int mode = MirrorModeChoices.IndexOf(e.newValue);
                if (mode < 0) return;
                var indices = _selectedAdapters
                    .Where(a => !IsMirrorLocked(a))
                    .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
                if (indices.Length == 0) return;

                // 「なし」への切り替えはミラー側メッシュの始末を伴う。
                // 「なし」からの切り替えも同様に生成が要る。
                // 分離(1) ↔ 結合(2) は MQO の属性が変わるだけなので属性コマンドで足りる。
                //
                // 生成・始末の対象は、ミラー側メッシュをまだ持たない行だけに絞る。
                // 実在するペアをここで解体させない。
                // 判定は行ごとに行う。以前は _selectedAdapters[0].MirrorType を
                // 全行の分岐に使っており、先頭行の状態で対象全部が引きずられていた。
                var enableTargets = _selectedAdapters
                    .Where(a => !HasLiveMirrorPeer(a))
                    .Where(a => mode == 0 ? a.MirrorType != 0 : a.MirrorType == 0)
                    .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

                if (enableTargets.Length > 0)
                    SendCmd(new SetMirrorEnabledCommand(ModelIndex, enableTargets, mode != 0));
                if (mode != 0)
                    SendCmd(new SetBatchMirrorTypeCommand(ModelIndex, indices, mode));
            });
            c.Add(LabeledRow("ミラー", _mirrorModeDropdown));

            // ── ミラー分岐ルート ──────────────────────────────────────
            //   エクスポート／スキンド変換で、このノードを含む配下を実体側と
            //   ミラー側の2本の枝に分割する起点。ボーン生成より前に決めるメッシュ
            //   属性なので、ボーンエディタではなくここで設定する。
            _mirrorBranchRootToggle = new Toggle("ミラー分岐ルート") { name = "mirror-branch-root-toggle" };
            _mirrorBranchRootToggle.style.color     = new StyleColor(Color.white);
            _mirrorBranchRootToggle.style.marginTop = 2;
            _mirrorBranchRootToggle.tooltip =
                "このオブジェクトを含む配下を、実体側とミラー側の2本の枝に分割する。\n"
                + "半身モデルの反対側を生成する起点。\n"
                + "枝の中のオブジェクトは、ミラー設定の有無に関わらずボーンがミラー化される。";
            _mirrorBranchRootToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetMirrorBranchRootCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_mirrorBranchRootToggle);

            _preserveNormalsToggle = new Toggle("法線を保持(再計算しない)") { name = "preserve-normals-toggle" };
            _preserveNormalsToggle.style.color     = new StyleColor(Color.white);
            _preserveNormalsToggle.style.marginTop = 2;
            _preserveNormalsToggle.RegisterValueChangedCallback(e =>
            {
                if (_isReceiving || _ctx == null) return;
                var indices = SelIndices();
                if (indices.Length > 0)
                    SendCmd(new SetPreserveNormalsCommand(ModelIndex, indices, e.newValue));
            });
            c.Add(_preserveNormalsToggle);
        }

        // ================================================================
        // 名称一括変更（旧名→新名 CSV）
        // ================================================================

        private void BuildRenameSection(VisualElement c)
        {
            var hint = new Label("CSV は「旧名,新名」の2列。'#' 始まりはコメント。");
            hint.style.color      = new StyleColor(Color.white);
            hint.style.fontSize   = 9;
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            _renamePathField = new TextField { name = "rename-path-field" };
            _renamePathField.RegisterValueChangedCallback(e => RecentPaths.Set(RenamePathKey(), e.newValue));
            c.Add(PlayerIoUiKit.PathRow(_renamePathField, OnRenameBrowse, out _btnRenameBrowse));
            _renamePathField.SetValueWithoutNotify(ResolveRenamePath());

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;
            _btnRenameTemplate = MakeSmallBtn("雛形書出", "btn-rename-template");
            _btnRenameLoad     = MakeSmallBtn("読込",     "btn-rename-load");
            _btnRenameApply    = MakeSmallBtn("適用",     "btn-rename-apply");
            _btnRenameTemplate.style.flexGrow = 1;
            _btnRenameLoad.style.flexGrow     = 1;
            _btnRenameApply.style.flexGrow    = 1;
            row.Add(_btnRenameTemplate); row.Add(_btnRenameLoad); row.Add(_btnRenameApply);
            c.Add(row);

            _renameStatusLabel = PlayerIoUiKit.StatusLabel();
            c.Add(_renameStatusLabel);

            UpdateRenameButtonStates();
        }

        private void BuildMorphEditor(VisualElement parent)
        {
            // カウント・フィルター
            var topRow = new VisualElement(); topRow.style.flexDirection = FlexDirection.Row;
            _morphCountLabel = new Label("モーフ: 0") { name = "morph-count-label" };
            _morphCountLabel.style.color    = new StyleColor(Color.white);
            _morphCountLabel.style.fontSize = 11;
            topRow.Add(_morphCountLabel);
            parent.Add(topRow);

            _morphFilterField = new TextField(); _morphFilterField.style.marginBottom = 3;
            parent.Add(_morphFilterField);

            // リスト
            _morphListView = new ListView(_morphFilteredData, 20, MorphMake, MorphBind);
            _morphListView.style.flexGrow  = 0; _morphListView.style.height = _morphListHeight; _morphListView.style.minHeight = _morphListHeight; _morphListView.style.maxHeight = _morphListHeight;
            _morphListView.selectionType   = SelectionType.Multiple;
            _morphListView.selectionChanged += OnMorphSel;
            parent.Add(_morphListView);
            AddListResizeHandle(parent, _morphListView,
                () => _morphListHeight, h => _morphListHeight = h, MorphListMinHeight);

            // テストウェイト
            // レンジの実体は ParameterLimits（persistentDataPath の CSV）にある。
            // 同じキーを ApplyMorphPreviewCommand.Weight の PLParam(LimitKey) が指す。
            var wRow = new VisualElement(); wRow.style.flexDirection = FlexDirection.Row; wRow.style.marginTop = 4; wRow.style.alignItems = Align.Center;
            _morphTestWeight = new Slider(MorphWeightMin, MorphWeightMax); _morphTestWeight.style.flexGrow = 1; wRow.Add(_morphTestWeight);
            _morphTestWeight.style.color = new StyleColor(Color.white);
            parent.Add(wRow);

            // 選択操作ボタン
            var selRow = new VisualElement(); selRow.style.flexDirection = FlexDirection.Row; selRow.style.marginTop = 3;
            selRow.Add(_btnMorphTestSelectAll   = MakeSmallBtn("全選択",   "btn-morph-test-select-all"));
            selRow.Add(_btnMorphTestDeselectAll = MakeSmallBtn("全解除",   "btn-morph-test-deselect-all"));
            selRow.Add(_btnMorphTestReset       = MakeSmallBtn("リセット", "btn-morph-test-reset"));
            parent.Add(selRow);

            parent.Add(Separator());

            // モーフ変換
            parent.Add(SectionHeader("メッシュ→モーフ"));

            _morphSourceMeshPopupContainer = new VisualElement { name = "morph-source-mesh-container" };
            parent.Add(LabeledRow("元メッシュ", _morphSourceMeshPopupContainer));

            _morphParentPopupContainer = new VisualElement { name = "morph-parent-container" };
            parent.Add(LabeledRow("親", _morphParentPopupContainer));

            _morphPanelPopupContainer = new VisualElement { name = "morph-panel-container" };
            parent.Add(LabeledRow("パネル", _morphPanelPopupContainer));

            _morphNameField = new TextField(); _morphNameField.name = "morph-name-field";
            parent.Add(LabeledRow("名前", _morphNameField));

            var convRow = new VisualElement(); convRow.style.flexDirection = FlexDirection.Row;
            _btnMeshToMorph = MakeSmallBtn("Mesh→Morph", "btn-mesh-to-morph");
            _btnMorphToMesh = MakeSmallBtn("Morph→Mesh", "btn-morph-to-mesh");
            convRow.Add(_btnMeshToMorph); convRow.Add(_btnMorphToMesh);
            parent.Add(convRow);

            parent.Add(Separator());

            // モーフセット作成
            parent.Add(SectionHeader("モーフセット作成"));
            _morphSetNameField = new TextField(); _morphSetNameField.name = "morph-set-name-field";
            parent.Add(LabeledRow("セット名", _morphSetNameField));

            _morphSetTypePopupContainer = new VisualElement { name = "morph-set-type-container" };
            parent.Add(LabeledRow("種別", _morphSetTypePopupContainer));

            _btnCreateMorphSet = MakeSmallBtn("セット作成", "btn-create-morph-set");
            parent.Add(_btnCreateMorphSet);

            _morphStatusLabel = new Label(""); _morphStatusLabel.style.fontSize = 10; _morphStatusLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.4f)); _morphStatusLabel.style.marginTop = 3;
            _morphStatusLabel.style.color = new StyleColor(Color.white);
            parent.Add(_morphStatusLabel);
        }
    }
}
