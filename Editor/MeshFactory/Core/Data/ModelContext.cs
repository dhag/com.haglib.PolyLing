// Assets/Editor/MeshFactory/Model/ModelContext.cs
// ランタイム用モデルコンテキスト
// ModelDataのランタイム版 - SimpleMeshFactory内のモデルデータを一元管理
// v1.3: MeshListUndoContext統合（複数選択、Undoコールバック対応）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Symmetry;
using MeshFactory.UndoSystem;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Model
{
    /// <summary>
    /// モデル全体のランタイムコンテキスト
    /// SimpleMeshFactory内のデータを一元管理
    /// Undo用コンテキストとしても使用（旧MeshListUndoContextを統合）
    /// </summary>
    public class ModelContext
    {
        // ================================================================
        // モデル情報
        // ================================================================

        /// <summary>モデル名</summary>
        public string Name { get; set; } = "Untitled";

        /// <summary>ファイルパス（保存済みの場合）</summary>
        public string FilePath { get; set; }

        /// <summary>変更フラグ</summary>
        public bool IsDirty { get; set; }

        // ================================================================
        // メッシュリスト
        // ================================================================

        /// <summary>メッシュコンテキストリスト</summary>
        public List<MeshContext> MeshContextList { get; set; } = new List<MeshContext>();

        /// <summary>メッシュ数</summary>
        public int Count => MeshContextList?.Count ?? 0;

        /// <summary>メッシュ数（後方互換）</summary>
        public int MeshContextCount => Count;

        // ================================================================
        // 選択状態（複数選択対応）
        // ================================================================

        /// <summary>選択中のメッシュインデックス（複数選択対応）</summary>
        public HashSet<int> SelectedIndices { get; set; } = new HashSet<int>();

        /// <summary>主選択インデックス（編集対象・最小インデックス）</summary>
        public int PrimarySelectedIndex => SelectedIndices.Count > 0 ? SelectedIndices.Min() : -1;

        /// <summary>選択があるか</summary>
        public bool HasSelection => SelectedIndices.Count > 0;

        /// <summary>複数選択されているか</summary>
        public bool IsMultiSelected => SelectedIndices.Count > 1;

        /// <summary>選択中のメッシュインデックス（後方互換・単一選択用）</summary>
        public int SelectedIndex
        {
            get => PrimarySelectedIndex;
            set
            {
                SelectedIndices.Clear();
                if (value >= 0 && value < Count)
                    SelectedIndices.Add(value);
            }
        }

        /// <summary>現在選択中のメッシュコンテキスト（主選択）</summary>
        public MeshContext CurrentMeshContext =>
            (PrimarySelectedIndex >= 0 && PrimarySelectedIndex < Count)
                ? MeshContextList[PrimarySelectedIndex] : null;

        /// <summary>有効なメッシュコンテキストが選択されているか</summary>
        public bool HasValidSelection => CurrentMeshContext != null;

        // ================================================================
        // Undoコールバック（旧MeshListUndoContextから統合）
        // ================================================================

        /// <summary>Undo/Redo実行後のコールバック（UI更新等）</summary>
        public Action OnListChanged;

        /// <summary>カメラ状態復元リクエスト時のコールバック（MeshSelectionChangeRecord用）</summary>
        public Action<CameraSnapshot> OnCameraRestoreRequested;
        
        /// <summary>MeshListStackへのフォーカス切り替えリクエスト</summary>
        public Action OnFocusMeshListRequested;

        // ================================================================
        // WorkPlane
        // ================================================================

        /// <summary>作業平面</summary>
        public WorkPlane WorkPlane { get; set; }

        // ================================================================
        // 対称設定
        // ================================================================

        /// <summary>対称モード設定</summary>
        public SymmetrySettings SymmetrySettings { get; } = new SymmetrySettings();

        // ================================================================
        // コンストラクタ
        // ================================================================

        public ModelContext()
        {
        }

        public ModelContext(string name)
        {
            Name = name;
        }

        // ================================================================
        // 選択操作（複数選択対応・旧MeshListUndoContextから統合）
        // ================================================================

        /// <summary>選択をクリア</summary>
        public void ClearSelection()
        {
            SelectedIndices.Clear();
        }

        /// <summary>単一選択（既存選択をクリアして選択）</summary>
        public void Select(int index)
        {
            SelectedIndices.Clear();
            if (index >= 0 && index < Count)
                SelectedIndices.Add(index);
        }

        /// <summary>選択を追加</summary>
        public void AddToSelection(int index)
        {
            if (index >= 0 && index < Count)
                SelectedIndices.Add(index);
        }

        /// <summary>選択を解除</summary>
        public void RemoveFromSelection(int index)
        {
            SelectedIndices.Remove(index);
        }

        /// <summary>選択をトグル</summary>
        public void ToggleSelection(int index)
        {
            if (SelectedIndices.Contains(index))
                SelectedIndices.Remove(index);
            else if (index >= 0 && index < Count)
                SelectedIndices.Add(index);
        }

        /// <summary>範囲選択（from から to まで）</summary>
        public void SelectRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < Count)
                    SelectedIndices.Add(i);
            }
        }

        /// <summary>全選択</summary>
        public void SelectAll()
        {
            SelectedIndices.Clear();
            for (int i = 0; i < Count; i++)
                SelectedIndices.Add(i);
        }

        /// <summary>選択されているか</summary>
        public bool IsSelected(int index)
        {
            return SelectedIndices.Contains(index);
        }

        /// <summary>選択インデックスを検証して無効なものを除去</summary>
        public void ValidateSelection()
        {
            SelectedIndices.RemoveWhere(i => i < 0 || i >= Count);
        }

        // ================================================================
        // メッシュリスト操作
        // ================================================================

        /// <summary>メッシュを追加</summary>
        /// <returns>追加されたインデックス</returns>
        public int Add(MeshContext meshContext)
        {
            if (meshContext == null)
                throw new ArgumentNullException(nameof(meshContext));

            MeshContextList.Add(meshContext);
            IsDirty = true;
            return MeshContextList.Count - 1;
        }

        /// <summary>メッシュを挿入</summary>
        public void Insert(int index, MeshContext meshContext)
        {
            if (meshContext == null)
                throw new ArgumentNullException(nameof(meshContext));
            if (index < 0 || index > MeshContextList.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            MeshContextList.Insert(index, meshContext);
            IsDirty = true;

            // 選択インデックス調整（挿入位置以降は+1）
            var adjusted = new HashSet<int>();
            foreach (var i in SelectedIndices)
            {
                adjusted.Add(i >= index ? i + 1 : i);
            }
            SelectedIndices = adjusted;
        }

        /// <summary>メッシュを削除</summary>
        /// <returns>削除成功したか</returns>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return false;

            MeshContextList.RemoveAt(index);
            IsDirty = true;

            // 選択インデックス調整
            var adjusted = new HashSet<int>();
            foreach (var i in SelectedIndices)
            {
                if (i < index)
                    adjusted.Add(i);
                else if (i > index)
                    adjusted.Add(i - 1);
                // i == index の場合は削除されるので追加しない
            }
            SelectedIndices = adjusted;
            ValidateSelection();

            return true;
        }

        /// <summary>メッシュを移動（順序変更）</summary>
        /// <returns>移動成功したか</returns>
        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= MeshContextList.Count)
                return false;
            if (toIndex < 0 || toIndex >= MeshContextList.Count)
                return false;
            if (fromIndex == toIndex)
                return false;

            var meshContext = MeshContextList[fromIndex];
            MeshContextList.RemoveAt(fromIndex);
            MeshContextList.Insert(toIndex, meshContext);

            // 選択インデックス調整
            var adjusted = new HashSet<int>();
            foreach (var i in SelectedIndices)
            {
                if (i == fromIndex)
                {
                    adjusted.Add(toIndex);
                }
                else if (fromIndex < i && toIndex >= i)
                {
                    adjusted.Add(i - 1);
                }
                else if (fromIndex > i && toIndex <= i)
                {
                    adjusted.Add(i + 1);
                }
                else
                {
                    adjusted.Add(i);
                }
            }
            SelectedIndices = adjusted;

            IsDirty = true;
            return true;
        }

        /// <summary>インデックスでメッシュコンテキストを取得</summary>
        public MeshContext GetMeshContext(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return null;
            return MeshContextList[index];
        }

        /// <summary>メッシュコンテキストのインデックスを取得</summary>
        public int IndexOf(MeshContext meshContext)
        {
            return MeshContextList.IndexOf(meshContext);
        }

        // ================================================================
        // 全体操作
        // ================================================================

        /// <summary>全メッシュをクリア</summary>
        /// <param name="destroyMeshes">Unity Meshリソースを破棄するか</param>
        public void Clear(bool destroyMeshes = true)
        {
            if (destroyMeshes)
            {
                foreach (var meshContext in MeshContextList)
                {
                    if (meshContext.UnityMesh != null)
                        UnityEngine.Object.DestroyImmediate(meshContext.UnityMesh);
                }
            }

            MeshContextList.Clear();
            SelectedIndices.Clear();
            IsDirty = true;
        }

        /// <summary>新規モデルとしてリセット</summary>
        public void Reset(string name = "Untitled")
        {
            Clear();
            Name = name;
            FilePath = null;
            IsDirty = false;
            WorkPlane?.Reset();
            SymmetrySettings?.Reset();
        }

        // ================================================================
        // 複製
        // ================================================================

        /// <summary>指定メッシュコンテキストを複製</summary>
        /// <returns>複製されたメッシュコンテキストのインデックス、失敗時は-1</returns>
        /// <remarks>MeshContext.Clone()が必要。Phase 2以降で実装</remarks>
        public int Duplicate(int index)
        {
            // TODO: MeshContext.Clone()を実装後に有効化
            throw new NotImplementedException("MeshContext.Clone() is required");
        }

        // ================================================================
        // バウンディングボックス
        // ================================================================

        /// <summary>全メッシュのバウンディングボックスを計算</summary>
        public Bounds CalculateBounds()
        {
            if (MeshContextList.Count == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds? combinedBounds = null;

            foreach (var meshContext in MeshContextList)
            {
                if (meshContext.Data == null)
                    continue;

                var meshContextBounds = meshContext.Data.CalculateBounds();

                if (!combinedBounds.HasValue)
                {
                    combinedBounds = meshContextBounds;
                }
                else
                {
                    var bounds = combinedBounds.Value;
                    bounds.Encapsulate(meshContextBounds);
                    combinedBounds = bounds;
                }
            }

            return combinedBounds ?? new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>現在選択中のメッシュのバウンディングボックス</summary>
        public Bounds CalculateCurrentBounds()
        {
            if (CurrentMeshContext?.Data == null)
                return new Bounds(Vector3.zero, Vector3.one);

            return CurrentMeshContext.Data.CalculateBounds();
        }
    }
}
