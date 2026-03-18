// SummaryTreeAdapter.cs
// IMeshViewをITreeItem<T>に適合させるアダプター
// TypedTreeAdapterの代替。MeshContext/ModelContextへの依存なし。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.View;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.MeshListV2
{
    public class SummaryTreeAdapter : ITreeItem<SummaryTreeAdapter>
    {
        private IMeshView _view;
        private int _id;

        // ITreeItem実装
        public int Id => _id;
        public string DisplayName => _view.Name;
        public SummaryTreeAdapter Parent { get; set; }
        public List<SummaryTreeAdapter> Children { get; } = new List<SummaryTreeAdapter>();

        // データアクセス（統一インタフェース経由）
        public IMeshView MeshView => _view;
        public int MasterIndex => _view.MasterIndex;
        public int VertexCount => _view.VertexCount;
        public int FaceCount => _view.FaceCount;
        public bool IsVisible => _view.IsVisible;
        public bool IsLocked => _view.IsLocked;
        public int MirrorType => _view.MirrorType;
        public bool IsBakedMirror => _view.IsBakedMirror;
        public bool IsMirrorSide => _view.IsMirrorSide;
        public bool IsRealSide => _view.IsRealSide;
        public bool HasBakedMirrorChild => _view.HasBakedMirrorChild;
        public int Depth => _view.Depth;
        public int HierarchyParentIndex => _view.HierarchyParentIndex;

        // 展開状態（パネル側で保持）
        public bool IsExpanded { get; set; }

        public SummaryTreeAdapter(IMeshView view, int id)
        {
            _view = view;
            _id = id;
            IsExpanded = !view.IsFolding;
        }

        public void UpdateView(IMeshView view) => _view = view;
        public void UpdateId(int id) => _id = id;

        // 階層
        public int GetDepth()
        {
            int depth = 0;
            var current = Parent;
            while (current != null) { depth++; current = current.Parent; }
            return depth;
        }

        public bool IsRoot => Parent == null;
        public bool HasChildren => Children != null && Children.Count > 0;

        // 表示
        public string GetMirrorTypeDisplay() => _view.MirrorTypeDisplay;
        public string GetInfoString() => _view.InfoString;
        public bool HasMirrorIcon => _view.HasMirrorIcon;

        public override string ToString() => $"SummaryTreeAdapter[{_id} M:{MasterIndex}]: {DisplayName}";
    }
}
