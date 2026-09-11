// MeshListSubPanel.Selection.cs
// オブジェクトリストのサブパネル：選択・D&D・ボタンイベント・選択辞書・名称一括変更。
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
        // 選択（エディタ版と同一）
        // ================================================================

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (_isReceiving || _ctx == null) return;
            _selectedAdapters.Clear();
            // 除外するのは生成ミラーだけ。ミラー側でもスキンド変換後の独立メッシュは
            // 自分のウェイトを保存するため、選択できないとウェイトを塗れない。
            foreach (var item in selection)
                if (item is SummaryTreeAdapter a && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);

            _isReceiving = true;
            try
            {
                var indices = _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
                // 現在のモデルの選択と同じなら送らない。
                // 送ると PlayerCommandDispatcher が EnterTopologyChanged を呼び、
                // GPU バッファの全再構築が走る。
                if (!SameAsCurrentSelection(indices))
                    SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices));
            }
            finally { _isReceiving = false; }

            UpdateDetailPanel();
            UpdateBonePosePanel();
            UpdateTransformPanel();
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            // _isReceiving: OnViewChanged 経由で TreeView をプログラム的に展開/折りたたみ
            // した場合にここへ再入する。Undo/Redo 連鎖記録を防ぐためスキップする。
            if (_isReceiving) return;
            var a = _treeRoot?.FindById(args.id);
            if (a == null) return;
            bool newExpanded = _treeView.IsExpanded(args.id);
            // UI 側アダプタ状態を更新
            a.IsExpanded = newExpanded;
            // データモデル (MeshContext.IsFolding) にも反映 + Undo 記録
            // IsFolding は IsExpanded の反転値 (folding = true で折りたたみ)
            if (a.MasterIndex >= 0)
                SendCmd(new SetMeshFoldingCommand(ModelIndex, a.MasterIndex, !newExpanded));
        }

        /// <summary>
        /// 左ペインのボタンから「すべてのオブジェクトを選択」を実行する。
        /// リスト内のボタン（btn-select-all）と同じ処理へ委ねるだけで、判定は持たない。
        /// </summary>
        public void SelectAllObjectsFromExternal() => SelectAllObjects();

        /// <summary>
        /// 現在のタブのリストにある全オブジェクトを選択する。
        /// ツリーの展開状態は見ないため、折りたたまれている子も対象になる。
        /// ミラー側 / ベイク済みミラーは選択対象外。
        /// </summary>
        private void SelectAllObjects()
        {
            var model = CurrentModel;
            if (model == null) return;

            IReadOnlyList<IMeshView> list = CurrentCategory switch
            {
                MeshCategory.Drawable       => model.DrawableList,
                MeshCategory.Bone           => model.BoneList,
                MeshCategory.RigidBody      => model.RigidBodyList,
                MeshCategory.RigidBodyJoint => model.RigidBodyJointList,
                _                           => null,
            };
            if (list == null) return;

            var indices = new List<int>();
            foreach (var v in list)
            {
                if (v == null) continue;
                // 生成ミラーだけ外す（OnSelectionChanged と同じ判定）。
                if ((v.IsBakedMirror || v.IsMirrorSide) && v.MirrorGeometryDerived) continue;
                if (v.MasterIndex < 0) continue;
                indices.Add(v.MasterIndex);
            }
            if (indices.Count == 0) return;

            var arr = indices.ToArray();

            // 同じ選択を送り直すと GPU 側が丸ごと作り直されるだけなので送らない。
            if (SameAsCurrentSelection(arr)) return;

            SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, arr));
        }

        /// <summary>
        /// indices が現在のモデルの選択と同一か（順序は問わない）。
        /// </summary>
        private bool SameAsCurrentSelection(int[] indices)
        {
            if (CurrentModel == null) return false;
            int[] cur = CurrentCategory switch
            {
                MeshCategory.Drawable => CurrentModel.SelectedDrawableIndices,
                MeshCategory.Bone     => CurrentModel.SelectedBoneIndices,
                MeshCategory.Morph    => CurrentModel.SelectedMorphIndices,
                _                     => null,
            };
            if (cur == null || indices == null) return false;
            if (cur.Length != indices.Length) return false;
            if (indices.Length == 0) return true;

            var set = new HashSet<int>(cur);
            foreach (int i in indices)
                if (!set.Contains(i)) return false;
            return true;
        }

        /// <summary>直前に同期したツリー内 id。展開・スクロールを「変わったときだけ」に絞る判定に使う。</summary>
        private readonly List<int> _lastSyncedSelIds = new List<int>();

        /// <summary>
        /// モデル側の選択をツリーの選択へ反映する。
        ///
        /// 【id と index を取り違えないこと】
        ///   TreeView は BaseVerticalCollectionView から
        ///   SetSelectionWithoutNotify(IEnumerable&lt;int&gt;) を継承しており、これは
        ///   「今表示されている行の index」を取る。SummaryTreeAdapter.Id は
        ///   TreeViewItemData に渡した item id であって index ではない。
        ///   ここへ id を渡すと、折り畳みやフィルタで行数がずれた瞬間に
        ///   まったく別の行が選ばれる（すべて展開・フィルタなしのときだけ
        ///   id と index が一致するので、症状が出たり出なかったりする）。
        ///   id で指定するときは必ず SetSelectionByIdWithoutNotify を使う。
        ///   モーフ側 (SyncMorphSel) は ListView なので index で正しい。
        /// </summary>
        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || CurrentModel == null)
            {
                PLDiag.SelList($"sync skip tree={_treeView != null} root={_treeRoot != null} model={CurrentModel != null}");
                return;
            }

            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
            };
            if (sel == null)
            {
                PLDiag.SelList($"sync clear tab={_currentTab}");
                _treeView.ClearSelection();
                _lastSyncedSelIds.Clear();
                return;
            }

            var ids     = new List<int>();
            var missing = new List<int>();
            foreach (var idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a == null) { missing.Add(idx); continue; }
                ids.Add(a.Id);
            }

            bool changed = !SameIntList(ids, _lastSyncedSelIds);

            PLDiag.SelList(
                $"sync tab={_currentTab} simple={IsSimpleMode} rows={_treeRoot.TotalCount} " +
                $"master=[{string.Join(",", sel)}] ids=[{string.Join(",", ids)}] " +
                $"missing=[{string.Join(",", missing)}] changed={changed}");

            // ここは OnViewChanged の中からも呼ばれる。無条件に false へ戻すと
            // 外側の受信中フラグを途中で落としてしまうため、元の値へ戻す。
            bool prevReceiving = _isReceiving;
            _isReceiving = true;
            try
            {
                // 折り畳みを開くのは「選択が変わった」ときだけ。
                // 属性変更などの通知でも同期は走るので、無条件に開くと
                // 利用者が閉じた枝を勝手に開き直してしまう。
                if (changed)
                    foreach (int id in ids) ExpandAncestorsIfCollapsed(id);

                _treeView.SetSelectionByIdWithoutNotify(ids);
            }
            finally { _isReceiving = prevReceiving; }

            // 選択行が表示範囲の外だと、選ばれていること自体が見えない。
            // 先頭の 1 件までスクロールする（複数選択でも基準を 1 つに決める）。
            if (changed && ids.Count > 0)
            {
                int firstId = ids[0];
                _root?.schedule.Execute(() =>
                {
                    try { _treeView?.ScrollToItemById(firstId); }
                    catch (System.Exception) { /* 行が消えている場合は何もしない */ }
                });
            }

            _lastSyncedSelIds.Clear();
            _lastSyncedSelIds.AddRange(ids);

            RebuildSelectedAdaptersFromCurrentModel();
        }

        private static bool SameIntList(List<int> a, List<int> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ================================================================
        // D&D（エディタ版と同一）
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();
            if (_treeView == null || _treeRoot == null) return;
            _dragDropHelper = new TreeViewDragDropHelper<SummaryTreeAdapter>(
                _treeView, _treeRoot, new SummaryDragValidator(), new MeshListRowResolver());
            _dragDropHelper.Setup();
        }

        /// <summary>
        /// 行要素 → アダプタ / 内容要素 の解決器。
        /// 行の並び順から index を数える方法は仮想化リストでずれるため、
        /// BindTreeItem が書き込んだ値を直接読む。
        /// </summary>
        private class MeshListRowResolver : ITreeRowResolver<SummaryTreeAdapter>
        {
            public SummaryTreeAdapter ResolveItem(VisualElement rowElement)
                => (ResolveContent(rowElement)?.userData as TreeItemCache)?.Adapter;

            public VisualElement ResolveContent(VisualElement rowElement)
            {
                if (rowElement == null) return null;
                if (rowElement.userData is TreeItemCache) return rowElement;
                return rowElement.Q<VisualElement>(TreeItemName);
            }
        }

        private void CleanupDragDrop() { _dragDropHelper?.Cleanup(); _dragDropHelper = null; }

        // ================================================================
        // ボタンイベント（エディタ版と同一ロジック、DisplayDialog除去）
        // ================================================================

        private void RegisterButtonEvents()
        {
            _tabDrawable?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone    ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph   ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));
            _tabRigidBody?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.RigidBody));
            _tabJoint   ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Joint));

            Q<Button>("btn-add")      ?.RegisterCallback<ClickEvent>(_ => OnAdd());
            Q<Button>("btn-up")       ?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            Q<Button>("btn-down")     ?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));
            Q<Button>("btn-outdent")  ?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            Q<Button>("btn-indent")   ?.RegisterCallback<ClickEvent>(_ => IndentSelected());
            Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            Q<Button>("btn-delete")   ?.RegisterCallback<ClickEvent>(_ => DeleteSelected());
            Q<Button>("btn-select-all")?.RegisterCallback<ClickEvent>(_ => SelectAllObjects());

            Q<Button>("btn-show")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(true));
            Q<Button>("btn-hide")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(false));

            Q<Button>("btn-lock")      ?.RegisterCallback<ClickEvent>(_ => SetSelectedLock(true));
            Q<Button>("btn-unlock")    ?.RegisterCallback<ClickEvent>(_ => SetSelectedLock(false));
            Q<Button>("btn-mirror-on") ?.RegisterCallback<ClickEvent>(_ => SetSelectedMirror(1));
            Q<Button>("btn-mirror-off")?.RegisterCallback<ClickEvent>(_ => SetSelectedMirror(0));

            Q<Button>("btn-seldic-apply")?.RegisterCallback<ClickEvent>(_ => ApplySelectionDictionary(false));
            Q<Button>("btn-seldic-add")  ?.RegisterCallback<ClickEvent>(_ => ApplySelectionDictionary(true));

            Q<Button>("btn-rename-template")?.RegisterCallback<ClickEvent>(_ => OnRenameSaveTemplate());
            Q<Button>("btn-rename-load")    ?.RegisterCallback<ClickEvent>(_ => OnRenameLoad());
            Q<Button>("btn-rename-apply")   ?.RegisterCallback<ClickEvent>(_ => OnRenameApply());

            _indentSlider?.RegisterValueChangedCallback(e =>
            {
                _treeIndentWidth = Mathf.Round(e.newValue);
                if (_indentValueLabel != null) _indentValueLabel.text = $"{(int)_treeIndentWidth}px";
                ApplyIndentWidthToVisibleRows();
            });

            _detailModeToggle?.RegisterValueChangedCallback(_ => OnDetailModeChanged());
            _showInfoToggle?.RegisterValueChangedCallback(_ => RefreshTree());
            _showMirrorSideToggle?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _filterField?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _meshNameField?.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    ApplyMeshName();
            });
            _meshNameField?.RegisterCallback<FocusOutEvent>(_ => ApplyMeshName());
        }

        private void ApplyMeshName()
        {
            if (_isReceiving || _ctx == null) return;
            if (_selectedAdapters.Count == 1 && _meshNameField != null)
            {
                var newName = _meshNameField.value;
                if (!string.IsNullOrEmpty(newName) && newName != _selectedAdapters[0].MeshView.Name)
                    SendCmd(new RenameMeshCommand(ModelIndex, _selectedAdapters[0].MasterIndex, newName));
            }
        }

        private void OnAdd() => SendCmd(new AddMeshCommand(ModelIndex));

        // ================================================================
        // 選択辞書（オブジェクト選択辞書）
        // ================================================================

        /// <summary>
        /// 辞書名の一覧をドロップダウンへ反映する。並びは
        /// ModelContext.MeshSelectionSets と同じで、位置がそのまま SetIndex になる。
        /// </summary>
        private void RefreshSelectionDictionary()
        {
            if (_selDicDropdown == null) return;

            string prev = _selDicDropdown.value;
            _selDicNames.Clear();
            var names = CurrentModel?.MeshSelectionSetNames;
            if (names != null)
                foreach (var n in names) _selDicNames.Add(n ?? "");

            _selDicDropdown.choices = _selDicNames;
            if (_selDicNames.Count == 0)
            {
                _selDicDropdown.SetValueWithoutNotify("");
                _selDicDropdown.index = -1;
            }
            else
            {
                int idx = prev != null ? _selDicNames.IndexOf(prev) : -1;
                _selDicDropdown.index = idx >= 0 ? idx : 0;
            }

            bool has = _selDicNames.Count > 0;
            _btnSelDicApply?.SetEnabled(has);
            _btnSelDicAdd?.SetEnabled(has);
        }

        /// <summary>辞書を選択へ適用する。addToExisting=true なら現在の選択へ追加。</summary>
        private void ApplySelectionDictionary(bool addToExisting)
        {
            if (_selDicDropdown == null) return;
            int idx = _selDicDropdown.index;
            if (idx < 0 || idx >= _selDicNames.Count) { Log("選択辞書が選ばれていません"); return; }
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, idx, addToExisting));
            Log(addToExisting ? $"辞書を選択に追加: {_selDicNames[idx]}" : $"辞書を選択に適用: {_selDicNames[idx]}");
        }

        // ================================================================
        // 名称一括変更
        // ================================================================

        /// <summary>タブごとに辞書を分けるためのキー要素。</summary>
        private string RenameCategoryKey() => IsSimpleMode ? "mesh" : _currentTab switch
        {
            TabType.Bone      => "bone",
            TabType.Morph     => "morph",
            TabType.RigidBody => "rigidbody",
            TabType.Joint     => "joint",
            _                 => "mesh",
        };

        private string RenamePathKey()         => $"MeshRename.{RenameCategoryKey()}.CsvPath";
        private string RenameDefaultFileName() => $"rename_{RenameCategoryKey()}.csv";

        /// <summary>
        /// 対応表 CSV のパス。手入力があればそれを使い、無ければ
        /// partsDictionary/rename_<カテゴリ>.csv を既定にする。
        /// </summary>
        private string ResolveRenamePath()
        {
            string saved = RecentPaths.Get(RenamePathKey());
            if (!string.IsNullOrEmpty(saved)) return saved;
            return System.IO.Path.Combine(PartsDictionaryPath.Resolve(), RenameDefaultFileName());
        }

        /// <summary>現在のタブが対象にするビュー一覧。</summary>
        private IReadOnlyList<IMeshView> RenameSourceList()
        {
            var model = CurrentModel;
            if (model == null) return null;
            if (IsSimpleMode) return model.DrawableList;
            return _currentTab switch
            {
                TabType.Drawable  => model.DrawableList,
                TabType.Bone      => model.BoneList,
                TabType.Morph     => model.MorphList,
                TabType.RigidBody => model.RigidBodyList,
                TabType.Joint     => model.RigidBodyJointList,
                _                 => null,
            };
        }

        /// <summary>[...] は読込用。書き出し先は雛形書出ボタン側で保存ダイアログを出す。</summary>
        private void OnRenameBrowse()
        {
            string cur = _renamePathField?.value ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveRenamePath();
            string path = PlayerIoUiKit.AskLoadPath("名称一括変更 対応表の読込", RenamePathKey(), cur, "csv");
            if (!string.IsNullOrEmpty(path)) _renamePathField.value = path;
        }

        /// <summary>現在のタブの名前を「旧名,新名（同じ）」で書き出す。</summary>
        private void OnRenameSaveTemplate()
        {
            var source = RenameSourceList();
            if (source == null || source.Count == 0) { RenameStatus("対象がありません"); return; }

            // パス欄は読込用。書き出しは毎回ダイアログを出す。
            // 書き込み先はフォルダだけを覚え、ファイル名は毎回この既定から始める。
            // 読込パスを初期値にすると、読んだ対応表をそのまま上書きする事故になる。
            string path = Poly_Ling.Core.SaveDest.AskSavePath(
                "名称一括変更 対応表の書き出し", Poly_Ling.Core.SaveDest.Keys.Dictionary, "",
                RenameDefaultFileName(), "csv");
            if (string.IsNullOrEmpty(path)) return;

            _renamePathField.value = path;

            // 既定の受け渡しフォルダを使う場合は作成しておく
            PartsDictionaryPath.ResolveForWrite();

            var names = new List<string>(source.Count);
            foreach (var v in source)
                if (v != null && !string.IsNullOrEmpty(v.Name)) names.Add(v.Name);

            int written = MeshRenameCsvHelper.SaveTemplate(names, path);
            RenameStatus(written >= 0
                ? $"雛形を書き出しました: {written} 行 → {path}"
                : "雛形の書き出しに失敗しました（ログを参照）");
        }

        /// <summary>
        /// 対応表を読み込み、現在のタブの名前と突き合わせて適用対象を決める。
        /// 実際の重複回避は適用時に受け側で行う。
        /// </summary>
        private void OnRenameLoad()
        {
            _renameTargetIndices = null;
            _renameTargetNames   = null;

            // 読込は必ずダイアログを通す。パス欄の値（無ければ既定の受け渡しファイル）は
            // 初期フォルダ／初期ファイル名としてだけ使う。
            string cur = _renamePathField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(cur)) cur = ResolveRenamePath();

            string path = PlayerIoUiKit.AskLoadPath("名称一括変更 対応表の読込", RenamePathKey(), cur, "csv");
            if (string.IsNullOrEmpty(path)) { UpdateRenameButtonStates(); return; }

            _renamePathField.value = path;

            var pairs = MeshRenameCsvHelper.LoadPairs(path);
            if (pairs == null) { RenameStatus("読込に失敗しました（ログを参照）"); UpdateRenameButtonStates(); return; }
            if (pairs.Count == 0) { RenameStatus("有効な行がありません"); UpdateRenameButtonStates(); return; }

            var source = RenameSourceList();
            if (source == null) { RenameStatus("対象がありません"); UpdateRenameButtonStates(); return; }

            var indices  = new List<int>();
            var newNames = new List<string>();
            var used     = new HashSet<int>();
            int unmatched = 0, skipped = 0;

            foreach (var pair in pairs)
            {
                bool hit = false;
                foreach (var v in source)
                {
                    if (v == null || v.Name != pair.OldName) continue;
                    hit = true;
                    if (!used.Add(v.MasterIndex)) { skipped++; continue; }
                    indices.Add(v.MasterIndex);
                    newNames.Add(pair.NewName);
                }
                if (!hit) unmatched++;
            }

            _renameTargetIndices = indices.ToArray();
            _renameTargetNames   = newNames.ToArray();

            string msg = $"対象 {indices.Count} 件 / 未一致 {unmatched} 件";
            if (skipped > 0) msg += $" / 重複指定 {skipped} 件";
            RenameStatus(msg);
            UpdateRenameButtonStates();
        }

        private void OnRenameApply()
        {
            if (_renameTargetIndices == null || _renameTargetIndices.Length == 0)
            {
                RenameStatus("先に読込を実行してください");
                return;
            }
            SendCmd(new RenameMeshesCommand(ModelIndex, _renameTargetIndices, _renameTargetNames));
            RenameStatus($"適用しました: {_renameTargetIndices.Length} 件（重複名は自動回避）");
        }

        /// <summary>タブ切り替え時など、読込済みの対応表を捨ててパスを引き直す。</summary>
        private void ResetRenameState()
        {
            _renameTargetIndices = null;
            _renameTargetNames   = null;
            _renamePathField?.SetValueWithoutNotify(ResolveRenamePath());
            RenameStatus("");
            UpdateRenameButtonStates();
        }

        private void UpdateRenameButtonStates()
        {
            bool hasTarget = _renameTargetIndices != null && _renameTargetIndices.Length > 0;
            _btnRenameApply?.SetEnabled(hasTarget);
        }

        private void RenameStatus(string m) { if (_renameStatusLabel != null) _renameStatusLabel.text = m; }

        private void MoveSelected(int dir)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            if (TreeViewHelper.MoveItems(_selectedAdapters, _treeRoot.RootItems, dir))
                _treeRoot.OnTreeChanged();
        }

        private void OutdentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Outdent(_selectedAdapters[0], _treeRoot.RootItems))
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void IndentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Indent(_selectedAdapters[0], _treeRoot.RootItems))
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void DuplicateSelected()
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DuplicateMeshesCommand(ModelIndex, SelIndices()));
        }

        private void DeleteSelected()
        {
            // Player: EditorUtility.DisplayDialog なし、即実行
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DeleteMeshesCommand(ModelIndex,
                _selectedAdapters.OrderByDescending(a => a.MasterIndex).Select(a => a.MasterIndex).ToArray()));
            _selectedAdapters.Clear();
        }

        private void SetSelectedVisibility(bool visible)
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new SetBatchVisibilityCommand(ModelIndex, SelIndices(), visible));
        }

        /// <summary>選択されている行すべてのロックを設定する。</summary>
        private void SetSelectedLock(bool locked)
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new SetBatchLockCommand(ModelIndex, SelIndices(), locked));
        }

        /// <summary>
        /// 選択されている行すべてのミラーを設定する。
        /// ロック対象（ミラー側・PMX 由来）は除外する。
        /// </summary>
        private void SetSelectedMirror(int mirrorType)
        {
            // ミラー側メッシュが実在する行は対象から外す。ペアを解体させないため。
            var targets = _selectedAdapters
                .Where(a => !HasLiveMirrorPeer(a))
                .Where(a => mirrorType == 0 ? a.MirrorType != 0 : a.MirrorType == 0)
                .Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
            if (targets.Length == 0) { Log("ミラーを変更できる行が選択されていません"); return; }
            SendCmd(new SetMirrorEnabledCommand(ModelIndex, targets, mirrorType != 0));
        }

        private void MoveToEdge(bool toTop)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            var item = _selectedAdapters[0];
            var siblings = item.Parent?.Children ?? _treeRoot.RootItems;
            int pos = siblings.IndexOf(item);
            if (toTop && pos > 0) { siblings.Remove(item); siblings.Insert(0, item); _treeRoot.OnTreeChanged(); }
            else if (!toTop && pos < siblings.Count - 1) { siblings.Remove(item); siblings.Add(item); _treeRoot.OnTreeChanged(); }
        }
    }
}
