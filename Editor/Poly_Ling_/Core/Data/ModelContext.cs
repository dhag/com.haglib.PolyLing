// Assets/Editor/Poly_Ling/Model/ModelContext.cs
// ランタイム用モデルコンテキスト
// ModelDataのランタイム版 - SimpleMeshFactory内のモデルデータを一元管理
// v1.3: MeshListUndoContext統合（複数選択、Undoコールバック対応）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
////using MeshContext = MeshContext;

namespace Poly_Ling.Model
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
        // タイプ別メッシュインデックス
        // ================================================================

        /// <summary>タイプ別インデックスキャッシュ（遅延初期化）</summary>
        private TypedMeshIndices _typedIndices;

        /// <summary>
        /// タイプ別メッシュインデックスへのアクセス
        /// カテゴリ別のフィルタリングとボーンインデックス変換を提供
        /// </summary>
        public TypedMeshIndices TypedIndices
        {
            get
            {
                if (_typedIndices == null)
                    _typedIndices = new TypedMeshIndices(this);
                return _typedIndices;
            }
        }

        /// <summary>タイプ別インデックスキャッシュを無効化（リスト変更時に呼ぶ）</summary>
        public void InvalidateTypedIndices()
        {
            _typedIndices?.Invalidate();
        }

        // ================================================================
        // タイプ別アクセス（ショートカット）
        // ================================================================

        /// <summary>描画可能メッシュ（Mesh + BakedMirror）</summary>
        public IReadOnlyList<TypedMeshEntry> DrawableMeshes => TypedIndices.GetEntries(MeshCategory.Drawable);

        /// <summary>通常メッシュのみ</summary>
        public IReadOnlyList<TypedMeshEntry> Meshes => TypedIndices.GetEntries(MeshCategory.Mesh);

        /// <summary>ボーンリスト</summary>
        public IReadOnlyList<TypedMeshEntry> Bones => TypedIndices.GetEntries(MeshCategory.Bone);

        /// <summary>モーフリスト</summary>
        public IReadOnlyList<TypedMeshEntry> Morphs => TypedIndices.GetEntries(MeshCategory.Morph);

        /// <summary>剛体リスト</summary>
        public IReadOnlyList<TypedMeshEntry> RigidBodies => TypedIndices.GetEntries(MeshCategory.RigidBody);

        /// <summary>剛体ジョイントリスト</summary>
        public IReadOnlyList<TypedMeshEntry> RigidBodyJoints => TypedIndices.GetEntries(MeshCategory.RigidBodyJoint);

        /// <summary>ヘルパーリスト</summary>
        public IReadOnlyList<TypedMeshEntry> Helpers => TypedIndices.GetEntries(MeshCategory.Helper);

        /// <summary>グループリスト</summary>
        public IReadOnlyList<TypedMeshEntry> Groups => TypedIndices.GetEntries(MeshCategory.Group);

        /// <summary>ボーン数</summary>
        public int BoneCount => TypedIndices.BoneCount;

        /// <summary>描画可能メッシュ数</summary>
        public int DrawableCount => TypedIndices.DrawableCount;

        /// <summary>ボーンがあるか</summary>
        public bool HasBones => TypedIndices.HasBones;

        // ================================================================
        // 選択状態（複数選択対応）
        // ================================================================

        /// <summary>選択中のメッシュインデックス（複数選択対応）</summary>
        public HashSet<int> SelectedMeshContextIndices { get; set; } = new HashSet<int>();

        /// <summary>主選択インデックス（編集対象・最小インデックス）</summary>
        public int PrimarySelectedMeshContextIndex => SelectedMeshContextIndices.Count > 0 ? SelectedMeshContextIndices.Min() : -1;

        /// <summary>選択があるか</summary>
        public bool HasSelection => SelectedMeshContextIndices.Count > 0;

        /// <summary>複数選択されているか</summary>
        public bool IsMultiSelected => SelectedMeshContextIndices.Count > 1;

        /// <summary>選択中のメッシュインデックス（後方互換・単一選択用）</summary>
        public int SelectedMeshContextIndex
        {
            get => PrimarySelectedMeshContextIndex;
            set
            {
                SelectedMeshContextIndices.Clear();
                if (value >= 0 && value < Count)
                    SelectedMeshContextIndices.Add(value);
            }
        }

        /// <summary>現在選択中のメッシュコンテキスト（主選択）</summary>
        public MeshContext CurrentMeshContext =>
            (PrimarySelectedMeshContextIndex >= 0 && PrimarySelectedMeshContextIndex < Count)
                ? MeshContextList[PrimarySelectedMeshContextIndex] : null;

        /// <summary>有効なメッシュコンテキストが選択されているか</summary>
        public bool HasValidMeshContextSelection => CurrentMeshContext != null;

        // ================================================================
        // Undoコールバック（旧MeshListUndoContextから統合）
        // ================================================================

        /// <summary>Undo/Redo実行後のコールバック（UI更新等）</summary>
        public Action OnListChanged;

        /// <summary>カメラ状態復元リクエスト時のコールバック（MeshSelectionChangeRecord用）</summary>
        public Action<CameraSnapshot> OnCameraRestoreRequested;
        
        /// <summary>MeshListStackへのフォーカス切り替えリクエスト</summary>
        public Action OnFocusMeshListRequested;

        /// <summary>メッシュリスト順序変更後のCG再構築リクエスト</summary>
        public Action OnReorderCompleted;

        /// <summary>VertexEditStackクリアリクエスト（メッシュ順序変更で古い記録が無効になるため）</summary>
        public Action OnVertexEditStackClearRequested;

        // ================================================================
        // WorkPlaneContext
        // ================================================================

        /// <summary>作業平面</summary>
        public WorkPlaneContext WorkPlane { get; set; }
        
        // ================================================================
        // Materials（モデル単位で実データを保持）
        // MaterialReference によるパラメータ＋キャッシュ管理
        // ================================================================
        
        // v1.2: 空リストで初期化（インポート時に自動追加されないように）
        private List<MaterialReference> _materialRefs = new List<MaterialReference>();
        private int _currentMaterialIndex = 0;
        
        /// <summary>
        /// マテリアル参照リスト（新API）
        /// パラメータデータ＋アセットパス＋キャッシュを管理
        /// </summary>
        public List<MaterialReference> MaterialReferences
        {
            get => _materialRefs;
            set => _materialRefs = value ?? new List<MaterialReference>();
        }
        
        /// <summary>
        /// マテリアルリスト（後方互換API）
        /// 内部的にはMaterialReferenceから取得/設定
        /// </summary>
        public List<Material> Materials
        {
            get => _materialRefs.Select(r => r?.Material).ToList();
            set
            {
                if (value == null || value.Count == 0)
                {
                    _materialRefs = new List<MaterialReference>();
                }
                else
                {
                    _materialRefs = value.Select(m => new MaterialReference(m)).ToList();
                }
            }
        }
        
        /// <summary>
        /// 現在選択中のマテリアルインデックス
        /// </summary>
        public int CurrentMaterialIndex
        {
            get => _currentMaterialIndex;
            set => _currentMaterialIndex = value;
        }
        
        // ================================================================
        // デフォルトマテリアル設定
        // ================================================================
        
        private List<MaterialReference> _defaultMaterialRefs = new List<MaterialReference> { new MaterialReference() };
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアル参照リスト</summary>
        public List<MaterialReference> DefaultMaterialReferences
        {
            get => _defaultMaterialRefs;
            set => _defaultMaterialRefs = value ?? new List<MaterialReference> { new MaterialReference() };
        }
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアルリスト（後方互換API）</summary>
        public List<Material> DefaultMaterials
        {
            get => _defaultMaterialRefs.Select(r => r?.Material).ToList();
            set
            {
                if (value == null || value.Count == 0)
                {
                    _defaultMaterialRefs = new List<MaterialReference> { new MaterialReference() };
                }
                else
                {
                    _defaultMaterialRefs = value.Select(m => new MaterialReference(m)).ToList();
                }
            }
        }
        
        /// <summary>新規メッシュ作成時に適用されるデフォルトカレントマテリアルインデックス</summary>
        public int DefaultCurrentMaterialIndex { get; set; } = 0;
        
        /// <summary>マテリアル変更時に自動でデフォルトに設定するか</summary>
        public bool AutoSetDefaultMaterials { get; set; } = true;
        
        // ================================================================
        // マテリアル操作ヘルパーメソッド
        // ================================================================
        
        /// <summary>マテリアル数</summary>
        public int MaterialCount => _materialRefs.Count;
        
        /// <summary>インデックスでマテリアルを取得</summary>
        public Material GetMaterial(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return null;
            return _materialRefs[index]?.Material;
        }
        
        /// <summary>インデックスでマテリアルを設定</summary>
        public void SetMaterial(int index, Material mat)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return;
            _materialRefs[index] = new MaterialReference(mat);
        }
        
        /// <summary>マテリアルを追加</summary>
        public void AddMaterial(Material mat)
        {
            _materialRefs.Add(new MaterialReference(mat));
        }
        
        /// <summary>インデックスでマテリアルを削除</summary>
        public void RemoveMaterialAt(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return;
            if (_materialRefs.Count <= 1)
                return; // 最低1つは残す
            
            _materialRefs.RemoveAt(index);
            
            // CurrentMaterialIndexを調整
            if (_currentMaterialIndex >= _materialRefs.Count)
            {
                _currentMaterialIndex = _materialRefs.Count - 1;
            }
        }
        
        /// <summary>全マテリアルをクリア（1つのnullマテリアルにリセット）</summary>
        public void ClearMaterials()
        {
            _materialRefs.Clear();
            _materialRefs.Add(new MaterialReference());
            _currentMaterialIndex = 0;
        }
        
        /// <summary>インデックスでマテリアル参照を取得</summary>
        public MaterialReference GetMaterialReference(int index)
        {
            if (index < 0 || index >= _materialRefs.Count)
                return null;
            return _materialRefs[index];
        }
        
        /// <summary>マテリアルリストを一括設定</summary>
        public void SetMaterials(IList<Material> materials)
        {
            if (materials == null || materials.Count == 0)
            {
                _materialRefs = new List<MaterialReference> { new MaterialReference() };
            }
            else
            {
                _materialRefs = materials.Select(m => new MaterialReference(m)).ToList();
            }
        }
        
        /// <summary>オンメモリマテリアル（アセット未保存）があるか</summary>
        public bool HasOnMemoryMaterials()
        {
            return _materialRefs.Any(r => r != null && !r.HasAssetPath && r.Material != null);
        }
        
        /// <summary>オンメモリマテリアルをアセットとして保存</summary>
        /// <param name="saveDir">保存先ディレクトリ（Assets/...）</param>
        /// <returns>保存したマテリアル数</returns>
        public int SaveOnMemoryMaterialsAsAssets(string saveDir)
        {
            if (string.IsNullOrEmpty(saveDir))
                return 0;
            
            // ディレクトリを作成
            if (!System.IO.Directory.Exists(saveDir))
            {
                System.IO.Directory.CreateDirectory(saveDir);
                AssetDatabase.Refresh();
            }
            
            int savedCount = 0;
            for (int i = 0; i < _materialRefs.Count; i++)
            {
                var matRef = _materialRefs[i];
                if (matRef == null || matRef.HasAssetPath || matRef.Material == null)
                    continue;
                
                // ファイル名を生成
                string matName = matRef.Name ?? $"Material_{i}";
                // 無効な文字を置換
                foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                {
                    matName = matName.Replace(c, '_');
                }
                
                string savePath = $"{saveDir}/{matName}.mat";
                
                // 重複チェック
                int counter = 1;
                while (AssetDatabase.LoadAssetAtPath<Material>(savePath) != null)
                {
                    savePath = $"{saveDir}/{matName}_{counter}.mat";
                    counter++;
                }
                
                // 保存
                if (matRef.SaveAsAsset(savePath))
                {
                    savedCount++;
                }
            }
            
            return savedCount;
        }

        // ================================================================
        // 対称設定
        // ================================================================

        /// <summary>対称モード設定</summary>
        public SymmetrySettings SymmetrySettings { get; } = new SymmetrySettings();

        // ================================================================
        // Humanoidボーンマッピング
        // ================================================================

        /// <summary>Humanoid Avatar用のボーンマッピング</summary>
        private HumanoidBoneMapping _humanoidMapping = new HumanoidBoneMapping();

        /// <summary>Humanoidボーンマッピング</summary>
        public HumanoidBoneMapping HumanoidMapping => _humanoidMapping;

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
            SelectedMeshContextIndices.Clear();
        }

        /// <summary>単一選択（既存選択をクリアして選択）</summary>
        public void Select(int index)
        {
            SelectedMeshContextIndices.Clear();
            if (index >= 0 && index < Count)
                SelectedMeshContextIndices.Add(index);
        }

        /// <summary>選択を追加</summary>
        public void AddToSelection(int index)
        {
            if (index >= 0 && index < Count)
                SelectedMeshContextIndices.Add(index);
        }

        /// <summary>選択を解除</summary>
        public void RemoveFromSelection(int index)
        {
            SelectedMeshContextIndices.Remove(index);
        }

        /// <summary>選択をトグル</summary>
        public void ToggleSelection(int index)
        {
            if (SelectedMeshContextIndices.Contains(index))
                SelectedMeshContextIndices.Remove(index);
            else if (index >= 0 && index < Count)
                SelectedMeshContextIndices.Add(index);
        }

        /// <summary>範囲選択（from から to まで）</summary>
        public void SelectRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < Count)
                    SelectedMeshContextIndices.Add(i);
            }
        }

        /// <summary>全選択</summary>
        public void SelectAll()
        {
            SelectedMeshContextIndices.Clear();
            for (int i = 0; i < Count; i++)
                SelectedMeshContextIndices.Add(i);
        }

        /// <summary>選択されているか</summary>
        public bool IsSelected(int index)
        {
            return SelectedMeshContextIndices.Contains(index);
        }

        /// <summary>選択インデックスを検証して無効なものを除去</summary>
        public void ValidateSelection()
        {
            SelectedMeshContextIndices.RemoveWhere(i => i < 0 || i >= Count);
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
            InvalidateTypedIndices();
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
            InvalidateTypedIndices();
            IsDirty = true;

            // 選択インデックス調整（挿入位置以降は+1）
            var adjusted = new HashSet<int>();
            foreach (var i in SelectedMeshContextIndices)
            {
                adjusted.Add(i >= index ? i + 1 : i);
            }
            SelectedMeshContextIndices = adjusted;
        }

        /// <summary>メッシュを削除</summary>
        /// <returns>削除成功したか</returns>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return false;

            MeshContextList.RemoveAt(index);
            InvalidateTypedIndices();
            IsDirty = true;

            // 選択インデックス調整
            var adjusted = new HashSet<int>();
            foreach (var i in SelectedMeshContextIndices)
            {
                if (i < index)
                    adjusted.Add(i);
                else if (i > index)
                    adjusted.Add(i - 1);
                // i == index の場合は削除されるので追加しない
            }
            SelectedMeshContextIndices = adjusted;
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
            foreach (var i in SelectedMeshContextIndices)
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
            SelectedMeshContextIndices = adjusted;

            InvalidateTypedIndices();
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
            SelectedMeshContextIndices.Clear();
            InvalidateTypedIndices();
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
            _humanoidMapping?.ClearAll();
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
                if (meshContext.MeshObject == null)
                    continue;

                var meshContextBounds = meshContext.MeshObject.CalculateBounds();

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
            if (CurrentMeshContext?.MeshObject == null)
                return new Bounds(Vector3.zero, Vector3.one);

            return CurrentMeshContext.MeshObject.CalculateBounds();
        }

        // ================================================================
        // ワールド変換行列計算
        // ================================================================

        /// <summary>
        /// 全MeshContextのワールド変換行列を計算
        /// HierarchyParentIndexに基づいて親子関係を解決し、
        /// 累積変換行列をWorldMatrixに設定する
        /// </summary>
        public void ComputeWorldMatrices()
        {
            if (MeshContextList == null || MeshContextList.Count == 0)
                return;

            // トポロジカルソートして親から順に処理
            var sortedIndices = TopologicalSortByHierarchy();

            foreach (int index in sortedIndices)
            {
                var ctx = MeshContextList[index];
                if (ctx == null) continue;

                Matrix4x4 localMatrix = ctx.LocalMatrix;
                int parentIndex = ctx.HierarchyParentIndex;

                if (parentIndex >= 0 && parentIndex < MeshContextList.Count)
                {
                    // 親がある場合：親のワールド行列 × 自身のローカル行列
                    var parent = MeshContextList[parentIndex];
                    ctx.WorldMatrix = parent.WorldMatrix * localMatrix;
                }
                else
                {
                    // ルートの場合：ローカル行列 = ワールド行列
                    ctx.WorldMatrix = localMatrix;
                }

                // 逆行列をキャッシュ
                ctx.WorldMatrixInverse = ctx.WorldMatrix.inverse;
            }
        }

        /// <summary>
        /// HierarchyParentIndexに基づいてトポロジカルソート
        /// 親が先に来るようにインデックスを並べ替える
        /// </summary>
        private List<int> TopologicalSortByHierarchy()
        {
            int count = MeshContextList.Count;
            var result = new List<int>(count);
            var visited = new bool[count];
            var inProgress = new bool[count];

            for (int i = 0; i < count; i++)
            {
                if (!visited[i])
                {
                    TopologicalSortVisit(i, visited, inProgress, result);
                }
            }

            return result;
        }

        private void TopologicalSortVisit(int index, bool[] visited, bool[] inProgress, List<int> result)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return;

            if (inProgress[index])
            {
                // 循環参照を検出（警告を出して無視）
                Debug.LogWarning($"[ModelContext] Circular hierarchy detected at index {index}");
                return;
            }

            if (visited[index])
                return;

            inProgress[index] = true;

            // 親を先に処理
            var ctx = MeshContextList[index];
            if (ctx != null)
            {
                int parentIndex = ctx.HierarchyParentIndex;
                if (parentIndex >= 0 && parentIndex < MeshContextList.Count && parentIndex != index)
                {
                    TopologicalSortVisit(parentIndex, visited, inProgress, result);
                }
            }

            inProgress[index] = false;
            visited[index] = true;
            result.Add(index);
        }

        /// <summary>
        /// 指定インデックスのMeshContextのワールド行列のみを再計算
        /// 親の行列は既に計算済みである前提
        /// </summary>
        public void ComputeWorldMatrix(int index)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return;

            var ctx = MeshContextList[index];
            if (ctx == null) return;

            Matrix4x4 localMatrix = ctx.LocalMatrix;
            int parentIndex = ctx.HierarchyParentIndex;

            if (parentIndex >= 0 && parentIndex < MeshContextList.Count)
            {
                var parent = MeshContextList[parentIndex];
                ctx.WorldMatrix = parent.WorldMatrix * localMatrix;
            }
            else
            {
                ctx.WorldMatrix = localMatrix;
            }

            ctx.WorldMatrixInverse = ctx.WorldMatrix.inverse;
        }
    }
}
