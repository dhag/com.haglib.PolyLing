// MeshListSubPanel.Refresh.cs
// オブジェクトリストのサブパネル：表示変更への追従・ツリー更新・詳細パネル。
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
        // OnViewChanged（エディタ版と同一、EditorApplication.delayCall を schedule.Execute に）
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                switch (kind)
                {
                    case ChangeKind.Selection:
                        if (_currentTab != TabType.Morph) SyncTreeViewSelection(); else SyncMorphSel();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.Attributes:
                        if (_currentTab != TabType.Morph)
                        {
                            // MeshContext.IsFolding の変化 (Undo/Redo 等) を TreeView 展開状態に反映
                            if (_treeRoot != null)
                                SyncExpandedFromData(_treeRoot.RootItems);
                            RefreshAllAdapterViews();
                            _treeView?.RefreshItems();
                            SyncTreeViewSelection();
                        }
                        else RefreshMorphEditor();
                        // Bone タブは BonePoseData 等が変化した可能性があるため
                        // _selectedAdapters のビューを最新スナップショットで更新する
                        if (_currentTab == TabType.Bone) RefreshSelectedAdapterViews();
                        // 辞書の追加・削除・改名は Attributes で通知される
                        RefreshSelectionDictionary();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.ListStructure:
                    case ChangeKind.ModelSwitch:
                    default:
                        // スキンドメッシュの自動設定
                        if (_detailModeToggle != null)
                        {
                            var model = view?.CurrentModel;
                            bool hasSkinned = model?.DrawableList?.Any(v => v.HasBoneWeight) ?? false;
                            _detailModeToggle.SetValueWithoutNotify(hasSkinned);
                            OnDetailModeChanged();
                        }
                        if (_currentTab != TabType.Morph) { CreateTreeRoot(); RefreshAllImmediate(); SyncTreeViewSelection(); }
                        if (_currentTab == TabType.Morph) RefreshMorphEditor();
                        RefreshSelectionDictionary();
                        UpdateRenameButtonStates();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                }
            }
            finally { _root?.schedule.Execute(() => _isReceiving = false); }
        }

        // ================================================================
        // 更新（RefreshTree の delayCall → schedule.Execute）
        // ================================================================

        /// <summary>
        /// 初期高さ = min(基準の4倍, 現在の展開状態で見える行がすべて収まる高さ)。
        /// 下端ドラッグで手動調整された後は何もしない。
        /// </summary>
        private void ApplyAutoTreeHeight()
        {
            if (_treeView == null || _treeRoot == null) return;
            if (_treeHeightUserAdjusted) return;

            int visibleRows = CountVisibleRows(_treeRoot.RootItems);
            if (visibleRows <= 0) return;

            float rowH   = _treeView.fixedItemHeight > 0f ? _treeView.fixedItemHeight : 20f;
            float needed = visibleRows * rowH + 4f;                 // 4f = 上下の枠ぶん
            float h      = Mathf.Max(TreeMinHeight,
                                     Mathf.Min(TreeBaseHeight * TreeInitialScale, needed));
            if (Mathf.Approximately(h, _treeHeight)) return;

            _treeHeight = h;
            // TreeView は height を無視するため min/max も同値にする。
            _treeView.style.height    = h;
            _treeView.style.minHeight = h;
            _treeView.style.maxHeight = h;
        }

        /// <summary>展開状態を加味した可視行数。畳まれた子は数えない。</summary>
        private int CountVisibleRows(List<SummaryTreeAdapter> items)
        {
            if (items == null) return 0;
            int n = 0;
            foreach (var it in items)
            {
                if (it == null) continue;
                n++;
                if (it.Children != null && it.Children.Count > 0 && _treeView.IsExpanded(it.Id))
                    n += CountVisibleRows(it.Children);
            }
            return n;
        }

        private void RefreshAllImmediate() { RefreshTreeImmediate(); UpdateHeader(); UpdateDetailPanel(); }

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null || _refreshScheduled) return;
            _refreshScheduled = true;
            _root?.schedule.Execute(() => { _refreshScheduled = false; ApplyTreeToView(); });
        }

        private void RefreshTreeImmediate()
        {
            if (_treeView == null || _treeRoot == null) return;
            _refreshScheduled = false;
            ApplyTreeToView();
        }

        private void ApplyTreeToView()
        {
            if (_treeView == null || _treeRoot == null) return;

            // Rebuild() の前後でスクロール位置を保持する。
            // 保持処理を入れないと、▲▼移動 / Indent / Outdent / D&D のたびに
            // リストが先頭まで戻ってしまう。
            var scroll = _treeView.Q<ScrollView>();
            Vector2 keep = scroll != null ? scroll.scrollOffset : Vector2.zero;

            // 表示の作り直しは「操作」ではない。Rebuild() は selectionChanged を、
            // RestoreExpanded() は itemExpandedChanged を発火させるため、囲わないと
            // OnSelectionChanged が SelectMeshCommand を送ってしまい、
            // GPU バッファの全再構築が走る。選択の再同期は SyncTreeViewSelection が行う。
            bool prevReceiving = _isReceiving;
            _isReceiving = true;
            try
            {
                _treeView.SetRootItems(TreeViewHelper.BuildTreeData(_treeRoot.RootItems));
                _treeView.Rebuild();
                RestoreExpanded(_treeRoot.RootItems);
            }
            finally { _isReceiving = prevReceiving; }

            _applyTreeGeneration++;
            ApplyAutoTreeHeight();

            if (scroll != null && (keep.x > 0f || keep.y > 0f))
            {
                scroll.scrollOffset = keep;
                // Rebuild 直後はコンテンツ高がまだ確定しておらず、代入値が
                // クランプされることがある。レイアウト確定後に再度セットする。
                _root?.schedule.Execute(() =>
                {
                    var s = _treeView?.Q<ScrollView>();
                    if (s != null) s.scrollOffset = keep;
                });
            }
        }

        /// <summary>
        /// この行を表示するために本当に必要な祖先だけを開く。
        /// 既に開いている枝には触らない。
        ///
        /// データ側 (MeshContext.IsFolding) にも同じ操作を送る。送らないと、
        /// 次の属性通知で SyncExpandedFromData が IsFolding を見て閉じ直してしまう。
        /// 呼び出し元で _isReceiving を立てているため、この送信で OnViewChanged へ
        /// 再入しても弾かれる。
        /// </summary>
        private void ExpandAncestorsIfCollapsed(int id)
        {
            if (_treeView == null || _treeRoot == null) return;
            var item = _treeRoot.FindById(id);
            if (item == null) return;

            for (var p = item.Parent; p != null; p = p.Parent)
            {
                if (_treeView.IsExpanded(p.Id)) continue;   // 開いている枝は触らない

                p.IsExpanded = true;
                _treeView.ExpandItem(p.Id, false);
                if (p.MasterIndex >= 0)
                    SendCmd(new SetMeshFoldingCommand(ModelIndex, p.MasterIndex, false));

                PLDiag.SelList($"expand ancestor id={p.Id} master={p.MasterIndex} name={p.DisplayName}");
            }
        }

        private void RestoreExpanded(List<SummaryTreeAdapter> items)
        {
            foreach (var i in items)
            {
                if (i.IsExpanded) _treeView.ExpandItem(i.Id, false);
                if (i.HasChildren) RestoreExpanded(i.Children);
            }
        }

        /// <summary>
        /// MeshContext.IsFolding (データ側) の最新値を SummaryTreeAdapter.IsExpanded (UI 側)
        /// および TreeView の展開状態に反映する。
        /// Undo/Redo 経由で IsFolding が書き換わった際に呼び、UI を追従させる。
        /// </summary>
        private void SyncExpandedFromData(List<SummaryTreeAdapter> items)
        {
            if (items == null || _treeView == null) return;
            foreach (var i in items)
            {
                bool shouldExpand = !i.MeshView.IsFolding;
                if (i.IsExpanded != shouldExpand)
                {
                    i.IsExpanded = shouldExpand;
                    if (shouldExpand) _treeView.ExpandItem(i.Id, false);
                    else              _treeView.CollapseItem(i.Id, false);
                }
                if (i.HasChildren) SyncExpandedFromData(i.Children);
            }
        }

        private void UpdateHeader()
        {
            if (_countLabel == null) return;
            if (IsSimpleMode) { _countLabel.text = $"メッシュ+ボーン: {_treeRoot?.TotalCount ?? 0}"; return; }
            string label = _currentTab switch { TabType.Drawable => "メッシュ", TabType.Bone => "ボーン", TabType.RigidBody => "剛体", TabType.Joint => "Joint", _ => "モーフ" };
            _countLabel.text = $"{label}: {_treeRoot?.TotalCount ?? 0}";
        }

        // ================================================================
        // 詳細パネル（エディタ版と同一）
        // ================================================================

        /// <summary>
        /// 詳細欄のミラーモード表示を更新する。
        /// mixed=true は選択内で値が揃っていないことを示す。
        /// </summary>
        private void SetMirrorMode(int mirrorType, bool enabled, bool mixed = false)
        {
            if (_mirrorModeDropdown == null) return;
            int idx = MirrorViewUtil.ClampType(mirrorType);
            _mirrorModeDropdown.SetValueWithoutNotify(MirrorModeChoices[idx]);
            _mirrorModeDropdown.showMixedValue = mixed;
            _mirrorModeDropdown.SetEnabled(enabled);
        }

        private void UpdateDetailPanel()
        {
            if (_currentTab == TabType.Morph) return;
            if (_selectedAdapters.Count == 0)
            {
                _meshNameField?.SetValueWithoutNotify("");
                SL(_vertexCountLabel, "頂点: -"); SL(_faceCountLabel, "面: -");
                SL(_triCountLabel, "三角形: -"); SL(_quadCountLabel, "四角形: -"); SL(_ngonCountLabel, "多角形: -");
                SL(_boneIndexLabel, "ボーンIdx: -"); SL(_masterIndexLabel, "マスターIdx: -");
                _ignorePoseToggle?.SetValueWithoutNotify(false);
                _preserveNormalsToggle?.SetValueWithoutNotify(false);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(false);
                _mirrorBranchRootToggle?.SetEnabled(false);
                SetMirrorMode(0, false);
                _detailFoldout?.SetEnabled(false);
                return;
            }
            _detailFoldout?.SetEnabled(true);
            if (_selectedAdapters.Count == 1)
            {
                var s = _selectedAdapters[0].MeshView;
                _meshNameField?.SetValueWithoutNotify(s.Name); _meshNameField?.SetEnabled(true);
                SL(_vertexCountLabel, $"頂点: {s.VertexCount}"); SL(_faceCountLabel, $"面: {s.FaceCount}");
                SL(_triCountLabel, $"三角形: {s.TriCount}"); SL(_quadCountLabel, $"四角形: {s.QuadCount}"); SL(_ngonCountLabel, $"多角形: {s.NgonCount}");
                SL(_boneIndexLabel, $"ボーンIdx: {s.BoneIndex}"); SL(_masterIndexLabel, $"マスターIdx: {s.MasterIndex}");
                _ignorePoseToggle?.SetValueWithoutNotify(s.IgnorePoseInArmature);
                _ignorePoseToggle?.SetEnabled(true);
                _preserveNormalsToggle?.SetValueWithoutNotify(s.PreserveNormals);
                _preserveNormalsToggle?.SetEnabled(true);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(s.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetEnabled(true);
                // ミラー側と PMX 由来のミラーは変更させない
                SetMirrorMode(s.MirrorType, !IsMirrorLocked(_selectedAdapters[0]));
            }
            else
            {
                _meshNameField?.SetValueWithoutNotify($"({_selectedAdapters.Count}個選択)"); _meshNameField?.SetEnabled(false);
                SL(_vertexCountLabel, $"頂点: {_selectedAdapters.Sum(a => a.VertexCount)} (合計)");
                SL(_faceCountLabel,   $"面: {_selectedAdapters.Sum(a => a.FaceCount)} (合計)");
                // 複数選択: 全て同値なら表示、異なればfalse表示
                bool allSame = _selectedAdapters.All(a => a.MeshView.IgnorePoseInArmature == _selectedAdapters[0].MeshView.IgnorePoseInArmature);
                _ignorePoseToggle?.SetValueWithoutNotify(allSame && _selectedAdapters[0].MeshView.IgnorePoseInArmature);
                _ignorePoseToggle?.SetEnabled(true);
                bool pnAllSame = _selectedAdapters.All(a => a.MeshView.PreserveNormals == _selectedAdapters[0].MeshView.PreserveNormals);
                _preserveNormalsToggle?.SetValueWithoutNotify(pnAllSame && _selectedAdapters[0].MeshView.PreserveNormals);
                _preserveNormalsToggle?.SetEnabled(true);
                bool mbAllSame = _selectedAdapters.All(a => a.MeshView.IsMirrorBranchRoot == _selectedAdapters[0].MeshView.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetValueWithoutNotify(mbAllSame && _selectedAdapters[0].MeshView.IsMirrorBranchRoot);
                _mirrorBranchRootToggle?.SetEnabled(true);
                bool mtAllSame = _selectedAdapters.All(a => a.MirrorType == _selectedAdapters[0].MirrorType);
                bool anyEditable = _selectedAdapters.Any(a => !IsMirrorLocked(a));
                SetMirrorMode(_selectedAdapters[0].MirrorType, anyEditable, mixed: !mtAllSame);
            }
        }
    }
}
