// TypedTreeAdapter.cs
// TypedMeshEntryをITreeItem<T>に適合させるアダプター
// TreeViewDragDropHelperで使用可能にする

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Model;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    /// <summary>
    /// TypedMeshEntryをITreeItem&lt;T&gt;に適合させるアダプター。
    /// TreeViewDragDropHelperで使用可能。
    /// </summary>
    public class TypedTreeAdapter : ITreeItem<TypedTreeAdapter>
    {
        // ================================================================
        // 内部参照
        // ================================================================

        private readonly TypedMeshEntry _entry;
        private readonly ModelContext _modelContext;
        private int _typedIndex;

        // ================================================================
        // ITreeItem<TypedTreeAdapter> 実装
        // ================================================================

        /// <summary>一意なID（タイプ別リスト内のインデックス）</summary>
        public int Id => _typedIndex;

        /// <summary>表示名</summary>
        public string DisplayName => _entry.Name ?? "Untitled";

        /// <summary>親アイテム（ルートならnull）</summary>
        public TypedTreeAdapter Parent { get; set; }

        /// <summary>子アイテムのリスト</summary>
        public List<TypedTreeAdapter> Children { get; } = new List<TypedTreeAdapter>();

        // ================================================================
        // 追加プロパティ
        // ================================================================

        /// <summary>元のTypedMeshEntry</summary>
        public TypedMeshEntry Entry => _entry;

        /// <summary>MeshContext</summary>
        public MeshContext MeshContext => _entry.Context;

        /// <summary>ModelContext</summary>
        public ModelContext ModelContext => _modelContext;

        /// <summary>マスターリストでのインデックス</summary>
        public int MasterIndex => _entry.MasterIndex;

        /// <summary>タイプ別リスト内のインデックス</summary>
        public int TypedIndex => _typedIndex;

        /// <summary>頂点数</summary>
        public int VertexCount => _entry.MeshObject?.VertexCount ?? 0;

        /// <summary>面数</summary>
        public int FaceCount => _entry.MeshObject?.FaceCount ?? 0;

        /// <summary>ミラータイプ</summary>
        public int MirrorType => MeshContext?.MirrorType ?? 0;

        /// <summary>ベイクされたミラーか</summary>
        public bool IsBakedMirror => MeshContext?.IsBakedMirror ?? false;

        /// <summary>展開状態（MeshContext.IsFoldingと連動）</summary>
        public bool IsExpanded
        {
            get => !(MeshContext?.IsFolding ?? false);
            set
            {
                if (MeshContext != null)
                {
                    MeshContext.IsFolding = !value;
                }
            }
        }

        /// <summary>選択状態</summary>
        public bool IsSelected { get; set; }

        /// <summary>可視性</summary>
        public bool IsVisible
        {
            get => MeshContext?.IsVisible ?? true;
            set { if (MeshContext != null) MeshContext.IsVisible = value; }
        }

        /// <summary>ロック状態</summary>
        public bool IsLocked
        {
            get => MeshContext?.IsLocked ?? false;
            set { if (MeshContext != null) MeshContext.IsLocked = value; }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        public TypedTreeAdapter(TypedMeshEntry entry, ModelContext modelContext, int typedIndex)
        {
            _entry = entry;
            _modelContext = modelContext;
            _typedIndex = typedIndex;
        }

        // ================================================================
        // インデックス管理
        // ================================================================

        public void UpdateTypedIndex(int newIndex)
        {
            _typedIndex = newIndex;
        }

        public int GetCurrentMasterIndex()
        {
            if (_modelContext == null || MeshContext == null)
                return -1;
            return _modelContext.MeshContextList.IndexOf(MeshContext);
        }

        // ================================================================
        // 階層ユーティリティ
        // ================================================================

        public int GetDepth()
        {
            int depth = 0;
            var current = Parent;
            while (current != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }

        public bool IsRoot => Parent == null;
        public bool HasChildren => Children != null && Children.Count > 0;

        // ================================================================
        // 表示ヘルパー
        // ================================================================

        public string GetMirrorTypeDisplay()
        {
            if (IsBakedMirror) return "🪞";
            return MirrorType switch
            {
                1 => "⇆X",
                2 => "⇆Y",
                3 => "⇆Z",
                _ => ""
            };
        }

        public string GetInfoString()
        {
            return $"V:{VertexCount} F:{FaceCount}";
        }

        public override string ToString()
        {
            return $"TypedTreeAdapter[T:{_typedIndex} M:{MasterIndex}]: {DisplayName}";
        }
    }
}
