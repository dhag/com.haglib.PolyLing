// Assets/Editor/MeshFactory/Model/ModelContext.cs
// ランタイム用モデルコンテキスト
// ModelDataのランタイム版 - SimpleMeshFactory内のモデルデータを一元管理

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Symmetry;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Model
{
    /// <summary>
    /// モデル全体のランタイムコンテキスト
    /// SimpleMeshFactory内のデータを一元管理
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
        public List<MeshContext> MeshContextList { get; } = new List<MeshContext>();

        /// <summary>選択中のメッシュインデックス</summary>
        public int SelectedIndex { get; set; } = -1;

        /// <summary>現在選択中のメッシュコンテキスト（便利プロパティ）</summary>
        public MeshContext CurrentMeshContext =>
            (SelectedIndex >= 0 && SelectedIndex < MeshContextList.Count)
                ? MeshContextList[SelectedIndex] : null;

        /// <summary>現在のMeshData（便利プロパティ）</summary>
        public MeshData CurrentMeshData => CurrentMeshContext?.Data;

        /// <summary>有効なメッシュコンテキストが選択されているか</summary>
        public bool HasValidSelection => CurrentMeshContext != null;

        /// <summary>メッシュ数</summary>
        public int MeshContextCount => MeshContextList.Count;

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
            if (SelectedIndex >= index)
                SelectedIndex++;
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
            if (SelectedIndex >= MeshContextList.Count)
                SelectedIndex = MeshContextList.Count - 1;
            else if (SelectedIndex > index)
                SelectedIndex--;

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
            if (SelectedIndex == fromIndex)
            {
                SelectedIndex = toIndex;
            }
            else if (fromIndex < SelectedIndex && toIndex >= SelectedIndex)
            {
                SelectedIndex--;
            }
            else if (fromIndex > SelectedIndex && toIndex <= SelectedIndex)
            {
                SelectedIndex++;
            }

            IsDirty = true;
            return true;
        }

        /// <summary>選択を変更</summary>
        /// <returns>選択変更成功したか</returns>
        public bool Select(int index)
        {
            if (index < -1 || index >= MeshContextList.Count)
                return false;

            if (SelectedIndex != index)
            {
                SelectedIndex = index;
                return true;
            }
            return false;
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
            SelectedIndex = -1;
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
