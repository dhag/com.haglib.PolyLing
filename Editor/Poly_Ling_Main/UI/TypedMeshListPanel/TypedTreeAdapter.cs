// TypedTreeAdapter.cs
// UI表示にはMeshSummaryを介してアクセスする

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Model;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    public class TypedTreeAdapter : ITreeItem<TypedTreeAdapter>
    {
        private readonly TypedMeshEntry _entry;
        private readonly ModelContext _modelContext;
        private int _typedIndex;
        private MeshSummary _summary;

        public int Id => _typedIndex;
        public string DisplayName => _summary.Name;
        public TypedTreeAdapter Parent { get; set; }
        public List<TypedTreeAdapter> Children { get; } = new List<TypedTreeAdapter>();

        /// <summary>メタデータサマリ（UI表示用）</summary>
        public ref readonly MeshSummary Summary => ref _summary;

        /// <summary>MeshSummaryを再生成する（属性変更後に呼ぶ）</summary>
        public void RefreshSummary()
        {
            _summary = MeshSummary.FromContext(_entry.Context, _modelContext, _entry.MasterIndex);
        }

        // 直接参照（Bone/Morph partialなど詳細アクセス用）
        public TypedMeshEntry Entry => _entry;
        public MeshContext MeshContext => _entry.Context;
        public ModelContext ModelContext => _modelContext;
        public int MasterIndex => _entry.MasterIndex;
        public int TypedIndex => _typedIndex;

        // Summary経由プロパティ（互換性維持）
        public int VertexCount => _summary.VertexCount;
        public int FaceCount => _summary.FaceCount;
        public int MirrorType => _summary.MirrorType;
        public bool IsBakedMirror => _summary.IsBakedMirror;

        public bool IsExpanded
        {
            get => !(MeshContext?.IsFolding ?? false);
            set { if (MeshContext != null) MeshContext.IsFolding = !value; }
        }
        public bool IsSelected { get; set; }
        public bool IsVisible
        {
            get => MeshContext?.IsVisible ?? true;
            set { if (MeshContext != null) MeshContext.IsVisible = value; }
        }
        public bool IsLocked
        {
            get => MeshContext?.IsLocked ?? false;
            set { if (MeshContext != null) MeshContext.IsLocked = value; }
        }

        public TypedTreeAdapter(TypedMeshEntry entry, ModelContext modelContext, int typedIndex)
        {
            _entry = entry;
            _modelContext = modelContext;
            _typedIndex = typedIndex;
            _summary = MeshSummary.FromContext(entry.Context, modelContext, entry.MasterIndex);
        }

        public void UpdateTypedIndex(int newIndex) { _typedIndex = newIndex; }

        public int GetCurrentMasterIndex()
        {
            if (_modelContext == null || MeshContext == null) return -1;
            return _modelContext.MeshContextList.IndexOf(MeshContext);
        }

        public int GetDepth()
        {
            int depth = 0;
            var current = Parent;
            while (current != null) { depth++; current = current.Parent; }
            return depth;
        }

        public bool IsRoot => Parent == null;
        public bool HasChildren => Children != null && Children.Count > 0;
        public string GetMirrorTypeDisplay() => _summary.MirrorTypeDisplay;
        public string GetInfoString() => _summary.InfoString;

        public override string ToString()
        {
            return $"TypedTreeAdapter[T:{_typedIndex} M:{MasterIndex}]: {DisplayName}";
        }
    }
}
