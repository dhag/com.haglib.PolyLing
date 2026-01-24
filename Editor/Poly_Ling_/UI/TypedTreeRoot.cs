// TypedTreeRoot.cs
// タイプ別リストをITreeRoot<T>に適合させるアダプター
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
    /// タイプ別リスト（DrawableMeshes/Bones/Morphs）を
    /// ITreeRoot&lt;TypedTreeAdapter&gt;に適合させるアダプター。
    /// </summary>
    public class TypedTreeRoot : ITreeRoot<TypedTreeAdapter>
    {
        // ================================================================
        // 内部参照
        // ================================================================

        private readonly ModelContext _modelContext;
        private readonly ToolContext _toolContext;
        private readonly MeshCategory _category;

        private List<TypedTreeAdapter> _rootItems = new List<TypedTreeAdapter>();
        private Dictionary<MeshContext, TypedTreeAdapter> _adapterMap = new Dictionary<MeshContext, TypedTreeAdapter>();

        // D&D用スナップショット
        private List<MeshContext> _preOrderedList;
        private Dictionary<MeshContext, MeshContext> _preParentMap;
        private MeshContext _preSelectedMeshContext;

        // ================================================================
        // ITreeRoot<TypedTreeAdapter> 実装
        // ================================================================

        public List<TypedTreeAdapter> RootItems => _rootItems;

        public void OnTreeChanged()
        {
            var undoController = _toolContext?.UndoController;
            int groupId = -1;

            if (undoController != null)
            {
                groupId = undoController.MeshListStack.BeginGroup("メッシュ順序変更");
            }

            try
            {
                SyncToModelContext();

                if (_preOrderedList != null && undoController != null)
                {
                    RecordReorderChange();
                }

                _preOrderedList = null;
                _preParentMap = null;
                _preSelectedMeshContext = null;

                OnChanged?.Invoke();

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

        // ================================================================
        // コールバック・プロパティ
        // ================================================================

        public Action OnChanged { get; set; }
        public ModelContext ModelContext => _modelContext;
        public ToolContext ToolContext => _toolContext;
        public MeshCategory Category => _category;
        public int TotalCount => _adapterMap.Count;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public TypedTreeRoot(ModelContext modelContext, ToolContext toolContext, MeshCategory category)
        {
            _modelContext = modelContext ?? throw new ArgumentNullException(nameof(modelContext));
            _toolContext = toolContext;
            _category = category;

            Rebuild();
        }

        // ================================================================
        // ツリー構築
        // ================================================================

        /// <summary>
        /// タイプ別リストからツリーを再構築
        /// </summary>
        public void Rebuild()
        {
            _rootItems.Clear();
            _adapterMap.Clear();

            if (_modelContext == null) return;

            // タイプ別リストを取得
            var entries = _category switch
            {
                MeshCategory.Drawable => _modelContext.DrawableMeshes,
                MeshCategory.Bone => _modelContext.Bones,
                MeshCategory.Morph => _modelContext.Morphs,
                _ => _modelContext.TypedIndices.GetEntries(_category)
            };

            if (entries == null) return;

            // アダプターを作成
            var allAdapters = new List<TypedTreeAdapter>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var adapter = new TypedTreeAdapter(entry, _modelContext, i);
                allAdapters.Add(adapter);
                _adapterMap[entry.Context] = adapter;
            }

            // 親子関係を設定
            // 注意: 親がフィルタ外の場合はルートとして扱う
            foreach (var adapter in allAdapters)
            {
                int parentMasterIndex = adapter.MeshContext?.HierarchyParentIndex ?? -1;

                if (parentMasterIndex >= 0 && parentMasterIndex < _modelContext.MeshContextCount)
                {
                    var parentContext = _modelContext.GetMeshContext(parentMasterIndex);
                    if (parentContext != null && _adapterMap.TryGetValue(parentContext, out var parentAdapter))
                    {
                        adapter.Parent = parentAdapter;
                        parentAdapter.Children.Add(adapter);
                    }
                    else
                    {
                        _rootItems.Add(adapter);
                    }
                }
                else
                {
                    _rootItems.Add(adapter);
                }
            }

            SyncSelectionFromModelContext();
        }

        // ================================================================
        // D&Dスナップショット
        // ================================================================

        public void SavePreChangeSnapshot()
        {
            if (_modelContext == null) return;

            _preOrderedList = new List<MeshContext>(_modelContext.MeshContextList);

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

            _preSelectedMeshContext = _modelContext.CurrentMeshContext;
        }

        private void RecordReorderChange()
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null || _modelContext == null) return;

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

            var newSelectedMeshContext = _modelContext.CurrentMeshContext;

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
        // ModelContextへの同期
        // ================================================================

        private void SyncToModelContext()
        {
            if (_modelContext == null) return;

            // フラットリストを取得
            var flatList = TreeViewHelper.Flatten(_rootItems);

            // このカテゴリに含まれるMeshContextのセット
            var categoryContexts = new HashSet<MeshContext>(flatList.Select(a => a.MeshContext));

            // マスターリストから現在のカテゴリ以外を取得
            var otherContexts = _modelContext.MeshContextList
                .Where(mc => !categoryContexts.Contains(mc))
                .ToList();

            // 選択状態を保持
            var selectedContexts = _modelContext.SelectedMeshContextIndices
                .Where(i => i >= 0 && i < _modelContext.MeshContextCount)
                .Select(i => _modelContext.GetMeshContext(i))
                .Where(mc => mc != null)
                .ToList();

            // 新しい順序でMeshContextリストを構築
            // 注意: D&Dで順序変更されたのはこのカテゴリのみ
            // 他のカテゴリは元の位置を維持
            var newOrder = new List<MeshContext>();
            var flatContexts = flatList.Select(a => a.MeshContext).ToList();

            // マスターリストの順序を再構築
            // カテゴリ内の相対順序を維持しつつ、元の位置に挿入
            int categoryIdx = 0;
            foreach (var mc in _modelContext.MeshContextList)
            {
                if (categoryContexts.Contains(mc))
                {
                    // このカテゴリの要素：新しい順序で追加
                    if (categoryIdx < flatContexts.Count)
                    {
                        var newCtx = flatContexts[categoryIdx];
                        if (!newOrder.Contains(newCtx))
                        {
                            newOrder.Add(newCtx);
                        }
                        categoryIdx++;
                    }
                }
                else
                {
                    // 他のカテゴリ：そのまま追加
                    newOrder.Add(mc);
                }
            }

            // 残りを追加
            while (categoryIdx < flatContexts.Count)
            {
                var ctx = flatContexts[categoryIdx];
                if (!newOrder.Contains(ctx))
                {
                    newOrder.Add(ctx);
                }
                categoryIdx++;
            }

            // リストを更新
            _modelContext.MeshContextList.Clear();
            _modelContext.MeshContextList.AddRange(newOrder);

            // インデックスと親参照を更新
            for (int i = 0; i < flatList.Count; i++)
            {
                var adapter = flatList[i];
                adapter.UpdateTypedIndex(i);

                // 親インデックスを設定（マスターリストのインデックス）
                int parentIndex = -1;
                if (adapter.Parent != null)
                {
                    parentIndex = newOrder.IndexOf(adapter.Parent.MeshContext);
                }
                if (adapter.MeshContext != null)
                {
                    adapter.MeshContext.HierarchyParentIndex = parentIndex;
                }
            }

            // 選択インデックスを復元
            _modelContext.SelectedMeshContextIndices.Clear();
            foreach (var mc in selectedContexts)
            {
                int newIndex = _modelContext.MeshContextList.IndexOf(mc);
                if (newIndex >= 0)
                {
                    _modelContext.SelectedMeshContextIndices.Add(newIndex);
                }
            }
        }

        // ================================================================
        // 選択状態の同期
        // ================================================================

        public void SyncSelectionFromModelContext()
        {
            foreach (var adapter in _adapterMap.Values)
            {
                adapter.IsSelected = false;
            }

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

        public void SyncSelectionToModelContext()
        {
            if (_modelContext == null) return;

            _modelContext.SelectedMeshContextIndices.Clear();

            foreach (var adapter in _adapterMap.Values)
            {
                if (adapter.IsSelected)
                {
                    int index = adapter.GetCurrentMasterIndex();
                    if (index >= 0)
                    {
                        _modelContext.SelectedMeshContextIndices.Add(index);
                    }
                }
            }
        }

        public void SelectSingle(TypedTreeAdapter adapter)
        {
            foreach (var a in _adapterMap.Values)
            {
                a.IsSelected = (a == adapter);
            }
            SyncSelectionToModelContext();
        }

        public void SelectMultiple(IEnumerable<TypedTreeAdapter> adapters)
        {
            var set = new HashSet<TypedTreeAdapter>(adapters);
            foreach (var a in _adapterMap.Values)
            {
                a.IsSelected = set.Contains(a);
            }
            SyncSelectionToModelContext();
        }

        // ================================================================
        // アダプター検索
        // ================================================================

        public TypedTreeAdapter GetAdapter(MeshContext meshContext)
        {
            if (meshContext == null) return null;
            _adapterMap.TryGetValue(meshContext, out var adapter);
            return adapter;
        }

        public TypedTreeAdapter GetAdapterByMasterIndex(int masterIndex)
        {
            if (_modelContext == null || masterIndex < 0 || masterIndex >= _modelContext.MeshContextCount)
                return null;
            var meshContext = _modelContext.GetMeshContext(masterIndex);
            return GetAdapter(meshContext);
        }

        public TypedTreeAdapter FindById(int id)
        {
            return TreeViewHelper.FindById(_rootItems, id);
        }

        public List<TypedTreeAdapter> GetAllAdapters()
        {
            return TreeViewHelper.Flatten(_rootItems);
        }

        public List<TypedTreeAdapter> GetSelectedAdapters()
        {
            return _adapterMap.Values.Where(a => a.IsSelected).ToList();
        }

        public int GenerateNextId()
        {
            return TreeViewHelper.GetMaxId(_rootItems) + 1;
        }
    }
}
