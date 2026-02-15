// MeshTreeAdapter.cs
// MeshContextをITreeItem<T>に適合させるアダプター
// TreeViewDragDropHelperで使用可能にする

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    /// <summary>
    /// MeshContextをITreeItem&lt;T&gt;に適合させるアダプター。
    /// TreeViewDragDropHelperで使用可能。
    /// 
    /// ID管理: インデックスベース（順序変更後はRebuild必要）
    /// </summary>
    public class MeshTreeAdapter : ITreeItem<MeshTreeAdapter>
    {
        // ================================================================
        // 内部参照
        // ================================================================

        private readonly MeshContext _meshContext;
        private readonly ModelContext _modelContext;
        private int _cachedIndex;

        // ================================================================
        // ITreeItem<MeshTreeAdapter> 実装
        // ================================================================

        /// <summary>一意なID（インデックスベース）</summary>
        public int Id => _cachedIndex;

        /// <summary>表示名</summary>
        public string DisplayName => _meshContext?.Name ?? "Untitled";

        /// <summary>親アイテム（ルートならnull）</summary>
        public MeshTreeAdapter Parent { get; set; }

        /// <summary>子アイテムのリスト</summary>
        public List<MeshTreeAdapter> Children { get; } = new List<MeshTreeAdapter>();

        // ================================================================
        // 追加プロパティ（表示・操作用）
        // ================================================================

        /// <summary>元のMeshContext</summary>
        public MeshContext MeshContext => _meshContext;

        /// <summary>元のModelContext</summary>
        public ModelContext ModelContext => _modelContext;

        /// <summary>頂点数</summary>
        public int VertexCount => _meshContext?.MeshObject?.VertexCount ?? 0;

        /// <summary>面数</summary>
        public int FaceCount => _meshContext?.MeshObject?.FaceCount ?? 0;

        /// <summary>ミラータイプ（0=なし, 1=X, 2=Y, 3=Z）</summary>
        public int MirrorType => _meshContext?.MirrorType ?? 0;

        /// <summary>ベイクされたミラーか</summary>
        public bool IsBakedMirror => _meshContext?.IsBakedMirror ?? false;

        /// <summary>ベイクミラーの元インデックス</summary>
        public int BakedMirrorSourceIndex => _meshContext?.BakedMirrorSourceIndex ?? -1;

        /// <summary>TreeViewでの展開状態</summary>
        public bool IsExpanded { get; set; } = true;

        /// <summary>選択状態（TreeViewの選択と同期用）</summary>
        public bool IsSelected { get; set; }

        // ================================================================
        // 将来拡張用（現在は未使用）
        // ================================================================

        /// <summary>可視性（MeshContextに連動）</summary>
        public bool IsVisible
        {
            get => _meshContext?.IsVisible ?? true;
            set
            {
                if (_meshContext != null)
                    _meshContext.IsVisible = value;
            }
        }

        /// <summary>ロック状態（MeshContextに連動）</summary>
        public bool IsLocked
        {
            get => _meshContext?.IsLocked ?? false;
            set
            {
                if (_meshContext != null)
                    _meshContext.IsLocked = value;
            }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        /// <summary>
        /// アダプターを作成
        /// </summary>
        /// <param name="meshContext">ラップするMeshContext</param>
        /// <param name="modelContext">親のModelContext</param>
        /// <param name="index">MeshContextList内のインデックス</param>
        public MeshTreeAdapter(MeshContext meshContext, ModelContext modelContext, int index)
        {
            _meshContext = meshContext;
            _modelContext = modelContext;
            _cachedIndex = index;
        }

        // ================================================================
        // インデックス管理
        // ================================================================

        /// <summary>
        /// キャッシュされたインデックスを更新（順序変更後に呼ぶ）
        /// </summary>
        public void UpdateIndex(int newIndex)
        {
            _cachedIndex = newIndex;
        }

        /// <summary>
        /// ModelContextから現在の実際のインデックスを取得
        /// </summary>
        public int GetCurrentIndex()
        {
            if (_modelContext == null || _meshContext == null)
                return -1;
            return _modelContext.MeshContextList.IndexOf(_meshContext);
        }

        // ================================================================
        // 階層ユーティリティ
        // ================================================================

        /// <summary>
        /// ルートからの深さを取得（ルート=0）
        /// </summary>
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

        /// <summary>
        /// この項目がルートか（親がない）
        /// </summary>
        public bool IsRoot => Parent == null;

        /// <summary>
        /// 子を持つか
        /// </summary>
        public bool HasChildren => Children != null && Children.Count > 0;

        // ================================================================
        // 表示用ヘルパー
        // ================================================================

        /// <summary>
        /// ミラータイプの表示文字列を取得
        /// </summary>
        public string GetMirrorTypeDisplay()
        {
            if (IsBakedMirror)
                return "🪞";  // ベイクドミラー

            return MirrorType switch
            {
                1 => "⇆X",
                2 => "⇆Y",
                3 => "⇆Z",
                _ => ""
            };
        }

        /// <summary>
        /// 簡易情報文字列を取得
        /// </summary>
        public string GetInfoString()
        {
            return $"V:{VertexCount} F:{FaceCount}";
        }

        public override string ToString()
        {
            return $"MeshTreeAdapter[{Id}]: {DisplayName} ({GetInfoString()})";
        }
    }
}
