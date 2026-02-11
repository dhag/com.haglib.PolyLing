// Assets/Editor/Poly_Ling/Model/ProjectContext.cs
// プロジェクトのランタイムコンテキスト
// 複数モデルの管理とカレントモデルの追跡
// v1.0: 初期バージョン
// v1.1: Phase 3 - Undo用コールバック追加

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;

namespace Poly_Ling.Model
{
    /// <summary>
    /// プロジェクト全体のランタイムコンテキスト
    /// 複数モデルの管理とカレントモデルの追跡
    /// </summary>
    public class ProjectContext
    {
        // ================================================================
        // プロジェクト情報
        // ================================================================

        /// <summary>プロジェクト名</summary>
        public string Name { get; set; } = "Untitled";

        /// <summary>ファイルパス（保存済みの場合）</summary>
        public string FilePath { get; set; }

        /// <summary>変更フラグ</summary>
        public bool IsDirty { get; set; }

        /// <summary>作成日時</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>更新日時</summary>
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        // ================================================================
        // モデルリスト
        // ================================================================

        /// <summary>モデルコンテキストリスト</summary>
        public List<ModelContext> Models { get; set; } = new List<ModelContext>();

        /// <summary>モデル数</summary>
        public int ModelCount => Models?.Count ?? 0;

        // ================================================================
        // カレントモデル（シリアライズしない）
        // ================================================================

        private int _currentModelIndex = 0;

        /// <summary>現在選択中のモデルインデックス</summary>
        public int CurrentModelIndex
        {
            get => _currentModelIndex;
            set
            {
                if (value >= 0 && value < ModelCount)
                {
                    _currentModelIndex = value;
                    OnCurrentModelChanged?.Invoke(_currentModelIndex);
                }
                else if (ModelCount == 0)
                {
                    _currentModelIndex = -1;
                }
            }
        }

        /// <summary>現在選択中のモデルコンテキスト</summary>
        public ModelContext CurrentModel =>
            (_currentModelIndex >= 0 && _currentModelIndex < ModelCount)
                ? Models[_currentModelIndex] : null;

        /// <summary>有効なモデルが選択されているか</summary>
        public bool HasValidModel => CurrentModel != null;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>カレントモデル変更時のコールバック</summary>
        public Action<int> OnCurrentModelChanged;

        /// <summary>モデルリスト変更時のコールバック</summary>
        public Action OnModelsChanged;

        // ================================================================
        // Phase 3追加: Undo用コールバック
        // ================================================================

        /// <summary>カメラ復元要求（Undo/Redo時）</summary>
        public Action<CameraSnapshot> OnCameraRestoreRequested;

        /// <summary>選択状態復元要求（Undo/Redo時）</summary>
        public Action<MeshSelectionSnapshot> OnSelectionRestoreRequested;

        /// <summary>UndoController参照更新要求</summary>
        public Action OnRefreshUndoControllerRequested;

        /// <summary>メッシュ選択インデックス変更要求</summary>
        public Action<int> OnMeshSelectionRequested;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public ProjectContext()
        {
        }

        public ProjectContext(string name)
        {
            Name = name;
        }

        /// <summary>
        /// デフォルトモデル付きで作成
        /// </summary>
        public static ProjectContext CreateWithDefaultModel(string projectName = "Untitled", string modelName = "Model")
        {
            var project = new ProjectContext(projectName);
            project.AddModel(new ModelContext(modelName));
            return project;
        }

        // ================================================================
        // モデル操作
        // ================================================================

        /// <summary>モデルを追加</summary>
        /// <returns>追加されたインデックス</returns>
        public int AddModel(ModelContext model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            Models.Add(model);
            IsDirty = true;
            MarkModified();

            // 最初のモデルならカレントに設定
            if (Models.Count == 1)
            {
                _currentModelIndex = 0;
            }

            OnModelsChanged?.Invoke();
            return Models.Count - 1;
        }

        /// <summary>モデルを挿入</summary>
        public void InsertModel(int index, ModelContext model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            if (index < 0 || index > Models.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            Models.Insert(index, model);
            IsDirty = true;
            MarkModified();

            // カレントインデックス調整
            if (_currentModelIndex >= index)
            {
                _currentModelIndex++;
            }

            OnModelsChanged?.Invoke();
        }

        /// <summary>モデルを削除</summary>
        /// <returns>削除成功したか</returns>
        public bool RemoveModelAt(int index)
        {
            if (index < 0 || index >= Models.Count)
                return false;

            // モデルのリソース解放
            var model = Models[index];
            model.Clear(true);

            Models.RemoveAt(index);
            IsDirty = true;
            MarkModified();

            // カレントインデックス調整
            if (_currentModelIndex >= Models.Count)
            {
                _currentModelIndex = Models.Count - 1;
            }
            else if (_currentModelIndex > index)
            {
                _currentModelIndex--;
            }

            OnModelsChanged?.Invoke();
            return true;
        }

        /// <summary>モデルを取得</summary>
        public ModelContext GetModel(int index)
        {
            if (index < 0 || index >= Models.Count)
                return null;
            return Models[index];
        }

        /// <summary>モデルのインデックスを取得</summary>
        public int IndexOf(ModelContext model)
        {
            return Models.IndexOf(model);
        }

        /// <summary>モデルを移動（順序変更）</summary>
        public bool MoveModel(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= Models.Count)
                return false;
            if (toIndex < 0 || toIndex >= Models.Count)
                return false;
            if (fromIndex == toIndex)
                return false;

            var model = Models[fromIndex];
            Models.RemoveAt(fromIndex);
            Models.Insert(toIndex, model);

            // カレントインデックス調整
            if (_currentModelIndex == fromIndex)
            {
                _currentModelIndex = toIndex;
            }
            else if (fromIndex < _currentModelIndex && toIndex >= _currentModelIndex)
            {
                _currentModelIndex--;
            }
            else if (fromIndex > _currentModelIndex && toIndex <= _currentModelIndex)
            {
                _currentModelIndex++;
            }

            IsDirty = true;
            MarkModified();
            OnModelsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 新規モデルを作成してカレントに設定
        /// </summary>
        /// <param name="name">モデル名（nullの場合は自動生成）</param>
        /// <returns>作成されたModelContext</returns>
        public ModelContext CreateNewModel(string name = null)
        {
            // 名前の自動生成
            if (string.IsNullOrEmpty(name))
            {
                name = $"Model {Models.Count + 1}";
            }

            var model = new ModelContext(name);
            int index = AddModel(model);
            
            // 新しいモデルをカレントに設定
            CurrentModelIndex = index;
            
            //Debug.Log($"[ProjectContext] Created new model '{name}' at index {index}");
            return model;
        }

        /// <summary>
        /// 指定インデックスのモデルを選択（カレントに設定）
        /// </summary>
        /// <param name="index">モデルインデックス</param>
        /// <returns>選択成功したか</returns>
        public bool SelectModel(int index)
        {
            if (index < 0 || index >= Models.Count)
            {
                Debug.LogWarning($"[ProjectContext] Invalid model index: {index} (count: {Models.Count})");
                return false;
            }

            if (_currentModelIndex == index)
            {
                return true; // 既に選択中
            }

            int oldIndex = _currentModelIndex;
            CurrentModelIndex = index;
            Debug.Log($"[ProjectContext] Model selection changed: {oldIndex} -> {index} ({CurrentModel?.Name})");
            return true;
        }

        /// <summary>
        /// 指定モデルを選択（カレントに設定）
        /// </summary>
        /// <param name="model">選択するモデル</param>
        /// <returns>選択成功したか</returns>
        public bool SelectModel(ModelContext model)
        {
            int index = IndexOf(model);
            if (index < 0)
            {
                Debug.LogWarning($"[ProjectContext] Model not found: {model?.Name}");
                return false;
            }
            return SelectModel(index);
        }

        // ================================================================
        // 全体操作
        // ================================================================

        /// <summary>全モデルをクリア</summary>
        /// <param name="destroyResources">リソースを破棄するか</param>
        public void Clear(bool destroyResources = true)
        {
            if (destroyResources)
            {
                foreach (var model in Models)
                {
                    model.Clear(true);
                }
            }

            Models.Clear();
            _currentModelIndex = -1;
            IsDirty = true;
            MarkModified();
            OnModelsChanged?.Invoke();
        }

        /// <summary>新規プロジェクトとしてリセット</summary>
        public void Reset(string name = "Untitled")
        {
            Clear();
            Name = name;
            FilePath = null;
            IsDirty = false;
            CreatedAt = DateTime.Now;
            ModifiedAt = DateTime.Now;
        }

        /// <summary>更新日時を現在時刻に設定</summary>
        public void MarkModified()
        {
            ModifiedAt = DateTime.Now;
        }

        // ================================================================
        // バウンディングボックス
        // ================================================================

        /// <summary>全モデルのバウンディングボックスを計算</summary>
        public Bounds CalculateBounds()
        {
            if (Models.Count == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            Bounds? combinedBounds = null;

            foreach (var model in Models)
            {
                var modelBounds = model.CalculateBounds();

                if (!combinedBounds.HasValue)
                {
                    combinedBounds = modelBounds;
                }
                else
                {
                    var bounds = combinedBounds.Value;
                    bounds.Encapsulate(modelBounds);
                    combinedBounds = bounds;
                }
            }

            return combinedBounds ?? new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>現在選択中のモデルのバウンディングボックス</summary>
        public Bounds CalculateCurrentModelBounds()
        {
            if (CurrentModel == null)
                return new Bounds(Vector3.zero, Vector3.one);

            return CurrentModel.CalculateBounds();
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>ユニークなモデル名を生成</summary>
        public string GenerateUniqueModelName(string baseName = "Model")
        {
            var existingNames = new HashSet<string>(Models.Select(m => m.Name));
            
            if (!existingNames.Contains(baseName))
                return baseName;

            int suffix = 1;
            string newName;
            do
            {
                newName = $"{baseName}_{suffix}";
                suffix++;
            } while (existingNames.Contains(newName));

            return newName;
        }
    }
}
