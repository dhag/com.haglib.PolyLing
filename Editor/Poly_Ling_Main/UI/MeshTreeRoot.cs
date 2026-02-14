// MeshTreeRoot.cs
// ModelContextをITreeRoot<T>に適合させるアダプター
// TreeViewDragDropHelperのルートとして使用

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    /// <summary>
    /// ModelContextをITreeRoot&lt;T&gt;に適合させるアダプター。
    /// TreeViewDragDropHelperのルートとして使用。
    /// 
    /// 責務:
    /// - MeshContextList ↔ MeshTreeAdapter変換
    /// - 親子関係の管理（HierarchyParentIndex ↔ Parent/Children）
    /// - 変更通知とModelContextへの同期
    /// </summary>
    public class MeshTreeRoot : ITreeRoot<MeshTreeAdapter>
    {
        // ================================================================
        // 内部参照
        // ================================================================

        private readonly ModelContext _modelContext;
        private readonly ToolContext _toolContext;
        private List<MeshTreeAdapter> _rootItems = new List<MeshTreeAdapter>();
        private Dictionary<MeshContext, MeshTreeAdapter> _adapterMap = new Dictionary<MeshContext, MeshTreeAdapter>();

        // ================================================================
        // ITreeRoot<MeshTreeAdapter> 実装
        // ================================================================

        /// <summary>ルートレベルのアイテムリスト</summary>
        public List<MeshTreeAdapter> RootItems => _rootItems;

        // === D&D用スナップショット（オブジェクト参照ベース）===
        private List<MeshContext> _preOrderedList;
        private Dictionary<MeshContext, MeshContext> _preParentMap;
        private MeshContext _preSelectedMeshContext;

        /// <summary>
        /// ツリー構造が変更された時に呼ばれる（D&D完了時等）
        /// </summary>
        public void OnTreeChanged()
        {
            var undoController = _toolContext?.UndoController;
            int groupId = -1;
            
            // BeginGroupでD&D操作全体を1つのグループにまとめる
            if (undoController != null)
            {
                groupId = undoController.MeshListStack.BeginGroup("メッシュ順序変更");
            }
            
            try
            {
                // 1. ModelContextに同期（これでリストの順序が更新される）
                SyncToModelContext();

                // 2. Undo記録（変更前のスナップショットがある場合）
                if (_preOrderedList != null && undoController != null)
                {
                    RecordReorderChange();
                }

                // スナップショットをクリア
                _preOrderedList = null;
                _preParentMap = null;
                _preSelectedMeshContext = null;

                // 3. 変更通知
                UnityEngine.Debug.Log($"[MeshTreeRoot.OnTreeChanged] OnChanged is {(OnChanged != null ? "set" : "null")}");
                OnChanged?.Invoke();
                UnityEngine.Debug.Log($"[MeshTreeRoot.OnTreeChanged] OnChanged invoked");

                // 4. モデルの変更フラグを立てる
                if (_modelContext != null)
                    _modelContext.IsDirty = true;
            }
            finally
            {
                if (undoController != null && groupId >= 0)
                {
                    undoController.MeshListStack.EndGroup();
                }
            }
        }

        /// <summary>
        /// D&D開始前に呼び出してスナップショットを保存
        /// </summary>
        public void SavePreChangeSnapshot()
        {
            if (_modelContext == null) return;
            
            // MeshContextのオブジェクト参照リストを保存
            _preOrderedList = new List<MeshContext>(_modelContext.MeshContextList);
            
            // 親マップを保存（MeshContext → 親MeshContext）
            _preParentMap = new Dictionary<MeshContext, MeshContext>();
            foreach (var mc in _modelContext.MeshContextList)
            {
                MeshContext parent = null;
                if (mc.HierarchyParentIndex >= 0 && mc.HierarchyParentIndex < _modelContext.MeshContextCount)
                {
                    parent = _modelContext.GetMeshContext(mc.HierarchyParentIndex);
                }
                _preParentMap[mc] = parent;
            }
            
            // 選択中のMeshContextを保存
            _preSelectedMeshContext = _modelContext.FirstSelectedMeshContext;
        }

        /// <summary>
        /// 順序変更をUndoスタックに記録
        /// </summary>
        private void RecordReorderChange()
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null || _modelContext == null) return;

            // 現在の状態を取得（SyncToModelContext後）
            var newOrderedList = new List<MeshContext>(_modelContext.MeshContextList);
            
            var newParentMap = new Dictionary<MeshContext, MeshContext>();
            foreach (var mc in _modelContext.MeshContextList)
            {
                MeshContext parent = null;
                if (mc.HierarchyParentIndex >= 0 && mc.HierarchyParentIndex < _modelContext.MeshContextCount)
                {
                    parent = _modelContext.GetMeshContext(mc.HierarchyParentIndex);
                }
                newParentMap[mc] = parent;
            }
            
            var newSelectedMeshContext = _modelContext.FirstSelectedMeshContext;

            // 順序が同じなら記録しない
            if (ListsEqual(_preOrderedList, newOrderedList) && MapsEqual(_preParentMap, newParentMap))
                return;

            var record = new MeshReorderChangeRecord
            {
                OldOrderedList = _preOrderedList,
                NewOrderedList = newOrderedList,
                OldParentMap = _preParentMap,
                NewParentMap = newParentMap,
                OldSelectedMeshContext = _preSelectedMeshContext,
                NewSelectedMeshContext = newSelectedMeshContext
            };

            undoController.MeshListStack.Record(record, "メッシュ順序変更");
            undoController.FocusMeshList();
        }

        private bool ListsEqual(List<MeshContext> a, List<MeshContext> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i])) return false;
            }
            return true;
        }

        private bool MapsEqual(Dictionary<MeshContext, MeshContext> a, Dictionary<MeshContext, MeshContext> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var val) || !ReferenceEquals(kvp.Value, val))
                    return false;
            }
            return true;
        }

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>ツリー変更時のコールバック（UIリフレッシュ用）</summary>
        public Action OnChanged { get; set; }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>元のModelContext</summary>
        public ModelContext ModelContext => _modelContext;

        /// <summary>元のToolContext</summary>
        public ToolContext ToolContext => _toolContext;

        /// <summary>全アダプター数</summary>
        public int TotalCount => _adapterMap.Count;

        // ================================================================
        // コンストラクタ
        // ================================================================

        /// <summary>
        /// MeshTreeRootを作成
        /// </summary>
        /// <param name="modelContext">ラップするModelContext</param>
        /// <param name="toolContext">ToolContext（Undo等に使用、nullでも可）</param>
        public MeshTreeRoot(ModelContext modelContext, ToolContext toolContext = null)
        {
            _modelContext = modelContext ?? throw new ArgumentNullException(nameof(modelContext));
            _toolContext = toolContext;

            // 初期構築
            RebuildFromModelContext();
        }

        // ================================================================
        // ツリー構造の構築
        // ================================================================

        /// <summary>
        /// ModelContextからツリー構造を再構築
        /// </summary>
        public void RebuildFromModelContext()
        {
            _rootItems.Clear();
            _adapterMap.Clear();

            if (_modelContext?.MeshContextList == null)
                return;

            // 1. 全MeshContextをアダプターでラップ
            var allAdapters = new List<MeshTreeAdapter>();
            for (int i = 0; i < _modelContext.MeshContextCount; i++)
            {
                var meshContext = _modelContext.GetMeshContext(i);
                if (meshContext == null) continue;

                var adapter = new MeshTreeAdapter(meshContext, _modelContext, i);
                allAdapters.Add(adapter);
                _adapterMap[meshContext] = adapter;
            }

            // 2. 親子関係を設定（HierarchyParentIndexに基づく）
            foreach (var adapter in allAdapters)
            {
                int parentIndex = adapter.MeshContext.HierarchyParentIndex;

                if (parentIndex >= 0 && parentIndex < allAdapters.Count)
                {
                    var parentMeshContext = _modelContext.GetMeshContext(parentIndex);
                    if (parentMeshContext != null && _adapterMap.TryGetValue(parentMeshContext, out var parentAdapter))
                    {
                        adapter.Parent = parentAdapter;
                        parentAdapter.Children.Add(adapter);
                    }
                    else
                    {
                        // 親が見つからない → ルートに
                        _rootItems.Add(adapter);
                    }
                }
                else
                {
                    // 親インデックスが無効 → ルート
                    _rootItems.Add(adapter);
                }
            }

            // 3. 選択状態を同期
            SyncSelectionFromModelContext();
        }

        /// <summary>
        /// 親参照を再構築（Children → Parent）
        /// </summary>
        public void RebuildParentReferences()
        {
            TreeViewHelper.RebuildParentReferences(_rootItems);
        }

        // ================================================================
        // ModelContextへの同期
        // ================================================================

        /// <summary>
        /// ツリー変更をModelContextに同期
        /// </summary>
        private void SyncToModelContext()
        {
            if (_modelContext == null) return;

            // 1. フラットリストを取得（深さ優先）
            var flatList = TreeViewHelper.Flatten(_rootItems);

            // 2. MeshContextListの順序を更新
            var newOrder = flatList.Select(a => a.MeshContext).ToList();

            // 選択インデックスを保持
            var selectedMeshContexts = _modelContext.SelectedMeshContextIndices
                .Where(i => i >= 0 && i < _modelContext.MeshContextCount)
                .Select(i => _modelContext.GetMeshContext(i))
                .Where(mc => mc != null)
                .ToList();

            // リストを更新
            _modelContext.MeshContextList.Clear();
            _modelContext.MeshContextList.AddRange(newOrder);

            // 3. インデックスと親参照を更新
            for (int i = 0; i < flatList.Count; i++)
            {
                var adapter = flatList[i];
                adapter.UpdateIndex(i);

                // 親インデックスを設定
                int parentIndex = -1;
                if (adapter.Parent != null)
                {
                    parentIndex = flatList.IndexOf(adapter.Parent);
                }
                adapter.MeshContext.HierarchyParentIndex = parentIndex;
            }

            // 4. 選択インデックスを復元 (v2.0: 選択されていたカテゴリのみクリア)
            var oldIndices = string.Join(",", _modelContext.SelectedMeshContextIndices);
            
            // 選択されていたアイテムのカテゴリを収集
            var selectedCategories = new HashSet<MeshType>();
            foreach (var mc in selectedMeshContexts)
            {
                if (mc != null)
                    selectedCategories.Add(mc.Type);
            }
            
            // 選択されていたカテゴリのみクリア
            foreach (var type in selectedCategories)
            {
                switch (type)
                {
                    case MeshType.Bone:
                        _modelContext.ClearBoneSelection();
                        break;
                    case MeshType.Morph:
                        _modelContext.ClearMorphSelection();
                        break;
                    default:
                        _modelContext.ClearMeshSelection();
                        break;
                }
            }
            
            // 選択を復元
            foreach (var mc in selectedMeshContexts)
            {
                int newIndex = _modelContext.MeshContextList.IndexOf(mc);
                if (newIndex >= 0)
                {
                    _modelContext.AddToSelectionByType(newIndex);
                }
            }
            var newIndices = string.Join(",", _modelContext.SelectedMeshContextIndices);
            UnityEngine.Debug.Log($"[SyncToModelContext] 選択インデックス更新: {oldIndices} -> {newIndices}");
        }

        // ================================================================
        // 選択状態の同期
        // ================================================================

        /// <summary>
        /// ModelContextの選択状態をアダプターに同期
        /// </summary>
        public void SyncSelectionFromModelContext()
        {
            // 全てのアダプターの選択をクリア
            foreach (var adapter in _adapterMap.Values)
            {
                adapter.IsSelected = false;
            }

            // 選択されているものをマーク
            if (_modelContext != null)
            {
                foreach (int index in _modelContext.SelectedMeshContextIndices)
                {
                    if (index >= 0 && index < _modelContext.MeshContextCount)
                    {
                        var meshContext = _modelContext.GetMeshContext(index);
                        if (meshContext != null && _adapterMap.TryGetValue(meshContext, out var adapter))
                        {
                            adapter.IsSelected = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// アダプターの選択状態をModelContextに同期
        /// </summary>
        public void SyncSelectionToModelContext()
        {
            if (_modelContext == null) return;

            // v2.0: 選択されたアイテムのカテゴリのみクリアして追加
            // まず選択されたアイテムのカテゴリを収集
            var selectedCategories = new HashSet<MeshType>();
            var selectedIndices = new List<int>();
            
            foreach (var adapter in _adapterMap.Values)
            {
                if (adapter.IsSelected)
                {
                    int index = adapter.GetCurrentIndex();
                    if (index >= 0)
                    {
                        selectedIndices.Add(index);
                        var meshContext = _modelContext.GetMeshContext(index);
                        if (meshContext != null)
                            selectedCategories.Add(meshContext.Type);
                    }
                }
            }

            // 選択に含まれるカテゴリのみクリア
            foreach (var type in selectedCategories)
            {
                switch (type)
                {
                    case MeshType.Bone:
                        _modelContext.ClearBoneSelection();
                        break;
                    case MeshType.Morph:
                        _modelContext.ClearMorphSelection();
                        break;
                    default:
                        _modelContext.ClearMeshSelection();
                        break;
                }
            }

            // 選択されたアイテムを追加
            foreach (var index in selectedIndices)
            {
                _modelContext.AddToSelectionByType(index);
            }
        }

        /// <summary>
        /// 選択をセット（単一）
        /// </summary>
        public void SelectSingle(MeshTreeAdapter adapter)
        {
            foreach (var a in _adapterMap.Values)
            {
                a.IsSelected = (a == adapter);
            }
            SyncSelectionToModelContext();
        }

        /// <summary>
        /// 選択をセット（複数）
        /// </summary>
        public void SelectMultiple(IEnumerable<MeshTreeAdapter> adapters)
        {
            var set = new HashSet<MeshTreeAdapter>(adapters);
            foreach (var a in _adapterMap.Values)
            {
                a.IsSelected = set.Contains(a);
            }
            SyncSelectionToModelContext();
        }

        // ================================================================
        // アダプター検索
        // ================================================================

        /// <summary>
        /// MeshContextからアダプターを取得
        /// </summary>
        public MeshTreeAdapter GetAdapter(MeshContext meshContext)
        {
            if (meshContext == null) return null;
            _adapterMap.TryGetValue(meshContext, out var adapter);
            return adapter;
        }

        /// <summary>
        /// インデックスからアダプターを取得
        /// </summary>
        public MeshTreeAdapter GetAdapterByIndex(int index)
        {
            if (_modelContext == null || index < 0 || index >= _modelContext.MeshContextCount)
                return null;

            var meshContext = _modelContext.GetMeshContext(index);
            return GetAdapter(meshContext);
        }

        /// <summary>
        /// IDからアダプターを検索
        /// </summary>
        public MeshTreeAdapter FindById(int id)
        {
            return TreeViewHelper.FindById(_rootItems, id);
        }

        /// <summary>
        /// 全アダプターをフラットリストで取得
        /// </summary>
        public List<MeshTreeAdapter> GetAllAdapters()
        {
            return TreeViewHelper.Flatten(_rootItems);
        }

        /// <summary>
        /// 選択中のアダプターを取得
        /// </summary>
        public List<MeshTreeAdapter> GetSelectedAdapters()
        {
            return _adapterMap.Values.Where(a => a.IsSelected).ToList();
        }

        // ================================================================
        // アイテム操作
        // ================================================================

        /// <summary>
        /// 新しいアダプターを追加（ルートに）
        /// </summary>
        public MeshTreeAdapter AddItem(MeshContext meshContext)
        {
            if (meshContext == null || _modelContext == null)
                return null;

            // ModelContextに追加
            if (!_modelContext.MeshContextList.Contains(meshContext))
            {
                _modelContext.MeshContextList.Add(meshContext);
            }

            int index = _modelContext.MeshContextList.IndexOf(meshContext);
            var adapter = new MeshTreeAdapter(meshContext, _modelContext, index);

            _adapterMap[meshContext] = adapter;
            _rootItems.Add(adapter);

            return adapter;
        }

        /// <summary>
        /// アダプターを削除
        /// </summary>
        public bool RemoveItem(MeshTreeAdapter adapter)
        {
            if (adapter == null) return false;

            // 子も再帰的に削除
            var toRemove = new List<MeshTreeAdapter>();
            CollectDescendants(adapter, toRemove);
            toRemove.Add(adapter);

            foreach (var item in toRemove)
            {
                // 親から削除
                if (item.Parent != null)
                {
                    item.Parent.Children.Remove(item);
                }
                else
                {
                    _rootItems.Remove(item);
                }

                // マップから削除
                _adapterMap.Remove(item.MeshContext);

                // ModelContextから削除
                _modelContext?.MeshContextList.Remove(item.MeshContext);
            }

            return true;
        }

        private void CollectDescendants(MeshTreeAdapter item, List<MeshTreeAdapter> result)
        {
            foreach (var child in item.Children)
            {
                CollectDescendants(child, result);
                result.Add(child);
            }
        }

        // ================================================================
        // 次のID生成
        // ================================================================

        /// <summary>
        /// 次の利用可能なIDを生成
        /// </summary>
        public int GenerateNextId()
        {
            return TreeViewHelper.GetMaxId(_rootItems) + 1;
        }
    }
}
