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

        /// <summary>メッシュエントリリスト</summary>
        public List<MeshContext> MeshList { get; } = new List<MeshContext>();

        /// <summary>選択中のメッシュインデックス</summary>
        public int SelectedIndex { get; set; } = -1;

        /// <summary>現在選択中のエントリ（便利プロパティ）</summary>
        public MeshContext CurrentEntry =>
            (SelectedIndex >= 0 && SelectedIndex < MeshList.Count)
                ? MeshList[SelectedIndex] : null;

        /// <summary>現在のMeshData（便利プロパティ）</summary>
        public MeshData CurrentMeshData => CurrentEntry?.Data;

        /// <summary>有効なエントリが選択されているか</summary>
        public bool HasValidSelection => CurrentEntry != null;

        /// <summary>メッシュ数</summary>
        public int MeshCount => MeshList.Count;

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
        public int Add(MeshContext entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            MeshList.Add(entry);
            IsDirty = true;
            return MeshList.Count - 1;
        }

        /// <summary>メッシュを挿入</summary>
        public void Insert(int index, MeshContext entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (index < 0 || index > MeshList.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            MeshList.Insert(index, entry);
            IsDirty = true;

            // 選択インデックス調整（挿入位置以降は+1）
            if (SelectedIndex >= index)
                SelectedIndex++;
        }

        /// <summary>メッシュを削除</summary>
        /// <returns>削除成功したか</returns>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= MeshList.Count)
                return false;

            MeshList.RemoveAt(index);
            IsDirty = true;

            // 選択インデックス調整
            if (SelectedIndex >= MeshList.Count)
                SelectedIndex = MeshList.Count - 1;
            else if (SelectedIndex > index)
                SelectedIndex--;

            return true;
        }

        /// <summary>メッシュを移動（順序変更）</summary>
        /// <returns>移動成功したか</returns>
        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= MeshList.Count)
                return false;
            if (toIndex < 0 || toIndex >= MeshList.Count)
                return false;
            if (fromIndex == toIndex)
                return false;

            var entry = MeshList[fromIndex];
            MeshList.RemoveAt(fromIndex);
            MeshList.Insert(toIndex, entry);

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
            if (index < -1 || index >= MeshList.Count)
                return false;

            if (SelectedIndex != index)
            {
                SelectedIndex = index;
                return true;
            }
            return false;
        }

        /// <summary>インデックスでエントリを取得</summary>
        public MeshContext GetEntry(int index)
        {
            if (index < 0 || index >= MeshList.Count)
                return null;
            return MeshList[index];
        }

        /// <summary>エントリのインデックスを取得</summary>
        public int IndexOf(MeshContext entry)
        {
            return MeshList.IndexOf(entry);
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
                foreach (var entry in MeshList)
                {
                    if (entry.UnityMesh != null)
                        UnityEngine.Object.DestroyImmediate(entry.UnityMesh);
                }
            }

            MeshList.Clear();
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

        /// <summary>指定エントリを複製</summary>
        /// <returns>複製されたエントリのインデックス、失敗時は-1</returns>
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
            if (MeshList.Count == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds? combinedBounds = null;

            foreach (var entry in MeshList)
            {
                if (entry.Data == null)
                    continue;

                var entryBounds = entry.Data.CalculateBounds();

                if (!combinedBounds.HasValue)
                {
                    combinedBounds = entryBounds;
                }
                else
                {
                    var bounds = combinedBounds.Value;
                    bounds.Encapsulate(entryBounds);
                    combinedBounds = bounds;
                }
            }

            return combinedBounds ?? new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>現在選択中のメッシュのバウンディングボックス</summary>
        public Bounds CalculateCurrentBounds()
        {
            if (CurrentEntry?.Data == null)
                return new Bounds(Vector3.zero, Vector3.one);

            return CurrentEntry.Data.CalculateBounds();
        }
    }
}
