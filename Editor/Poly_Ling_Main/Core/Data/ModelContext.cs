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

        /// <summary>
        /// インポート元ドキュメント（PMXDocument等）
        /// エクスポート時に物理データ等のパススルーに使用
        /// </summary>
        public object SourceDocument { get; set; }

        /// <summary>
        /// 各ボーンのPMXワールド位置（インポート時の初期位置）
        /// MikuMikuFlexの「ローカル位置」に相当。CCDIKSolverで使用。
        /// </summary>
        public Vector3[] BoneWorldPositions { get; set; }

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
        // 選択状態（カテゴリ別・複数選択対応）
        // v2.0: 選択メッシュ/選択ボーン/選択頂点モーフに分離
        // ================================================================

        /// <summary>選択カテゴリ</summary>
        public enum SelectionCategory { Mesh, Bone, Morph }

        /// <summary>現在アクティブな選択カテゴリ</summary>
        public SelectionCategory ActiveCategory { get; private set; } = SelectionCategory.Mesh;

        /// <summary>選択中のメッシュインデックス（Mesh, BakedMirror タイプ）- 選択順序を保持</summary>
        public List<int> SelectedMeshIndices { get; set; } = new List<int>();

        /// <summary>選択中のボーンインデックス（Bone タイプ）- 選択順序を保持</summary>
        public List<int> SelectedBoneIndices { get; set; } = new List<int>();

        /// <summary>選択中の頂点モーフインデックス（Morph タイプ）- 選択順序を保持</summary>
        public List<int> SelectedMorphIndices { get; set; } = new List<int>();



        /// <summary>メッシュが選択されているか</summary>
        public bool HasMeshSelection => SelectedMeshIndices.Count > 0;

        /// <summary>ボーンが選択されているか</summary>
        public bool HasBoneSelection => SelectedBoneIndices.Count > 0;

        /// <summary>頂点モーフが選択されているか</summary>
        public bool HasMorphSelection => SelectedMorphIndices.Count > 0;

        /// <summary>メッシュが複数選択されているか</summary>
        public bool IsMeshMultiSelected => SelectedMeshIndices.Count > 1;

        /// <summary>ボーンが複数選択されているか</summary>
        public bool IsBoneMultiSelected => SelectedBoneIndices.Count > 1;

        /// <summary>頂点モーフが複数選択されているか</summary>
        public bool IsMorphMultiSelected => SelectedMorphIndices.Count > 1;

        /// <summary>メッシュ選択リストの先頭インデックス</summary>
        public int FirstMeshIndex => SelectedMeshIndices.Count > 0 ? SelectedMeshIndices[0] : -1;

        /// <summary>ボーン選択リストの先頭インデックス</summary>
        public int FirstBoneIndex => SelectedBoneIndices.Count > 0 ? SelectedBoneIndices[0] : -1;

        /// <summary>モーフ選択リストの先頭インデックス</summary>
        public int FirstMorphIndex => SelectedMorphIndices.Count > 0 ? SelectedMorphIndices[0] : -1;

        // ================================================================
        // カテゴリ別選択操作
        // ================================================================

        /// <summary>Drawableメッシュを選択（単一選択・Mesh/BakedMirrorカテゴリ専用）</summary>
        public void SelectDrawable(int index)
        {
            ClearMeshSelection();
            if (index >= 0 && index < Count)
            {
                SelectedMeshIndices.Add(index);
                ActiveCategory = SelectionCategory.Mesh;
            }
        }

        /// <summary>ボーンを選択（単一選択）</summary>
        public void SelectBone(int index)
        {
            ClearBoneSelection();
            if (index >= 0 && index < Count)
            {
                SelectedBoneIndices.Add(index);
                ActiveCategory = SelectionCategory.Bone;
            }
        }

        /// <summary>頂点モーフを選択（単一選択）</summary>
        public void SelectMorph(int index)
        {
            ClearMorphSelection();
            if (index >= 0 && index < Count)
            {
                SelectedMorphIndices.Add(index);
                ActiveCategory = SelectionCategory.Morph;
            }
        }

        /// <summary>メッシュ選択に追加（重複防止・順序保持）</summary>
        public void AddToMeshSelection(int index)
        {
            if (index >= 0 && index < Count && !SelectedMeshIndices.Contains(index))
            {
                SelectedMeshIndices.Add(index);
                ActiveCategory = SelectionCategory.Mesh;
            }
        }

        /// <summary>メッシュ選択をトグル（Ctrl+クリック用）</summary>
        public void ToggleMeshSelection(int index)
        {
            if (index < 0 || index >= Count) return;
            
            if (SelectedMeshIndices.Contains(index))
            {
                SelectedMeshIndices.Remove(index);
            }
            else
            {
                SelectedMeshIndices.Add(index);
            }
            ActiveCategory = SelectionCategory.Mesh;
        }

        /// <summary>メッシュ範囲選択（Shift+クリック用）</summary>
        public void SelectMeshRange(int fromIndex, int toIndex)
        {
            int start = Mathf.Min(fromIndex, toIndex);
            int end = Mathf.Max(fromIndex, toIndex);
            start = Mathf.Max(0, start);
            end = Mathf.Min(Count - 1, end);
            
            for (int i = start; i <= end; i++)
            {
                if (!SelectedMeshIndices.Contains(i))
                    SelectedMeshIndices.Add(i);
            }
            ActiveCategory = SelectionCategory.Mesh;
        }

        /// <summary>メッシュ選択から除去</summary>
        public void RemoveFromMeshSelection(int index)
        {
            SelectedMeshIndices.Remove(index);
        }

        /// <summary>ボーン選択に追加（重複防止・順序保持）</summary>
        public void AddToBoneSelection(int index)
        {
            if (index >= 0 && index < Count && !SelectedBoneIndices.Contains(index))
            {
                SelectedBoneIndices.Add(index);
                ActiveCategory = SelectionCategory.Bone;
            }
        }

        /// <summary>頂点モーフ選択に追加（重複防止・順序保持）</summary>
        public void AddToMorphSelection(int index)
        {
            if (index >= 0 && index < Count && !SelectedMorphIndices.Contains(index))
            {
                SelectedMorphIndices.Add(index);
                ActiveCategory = SelectionCategory.Morph;
            }
        }

        /// <summary>メッシュ選択をクリア</summary>
        public void ClearMeshSelection() => SelectedMeshIndices.Clear();

        /// <summary>ボーン選択をクリア</summary>
        public void ClearBoneSelection() => SelectedBoneIndices.Clear();

        /// <summary>頂点モーフ選択をクリア</summary>
        public void ClearMorphSelection() => SelectedMorphIndices.Clear();

        // ================================================================
        // 後方互換プロパティ（全カテゴリ統合ビュー）
        // ================================================================

        /// <summary>
        /// 選択中の全インデックス（読み取り専用）
        /// 設定にはSelect/SelectMesh/SelectBone/SelectMorph等を使用
        /// </summary>
        public HashSet<int> SelectedMeshContextIndices
        {
            get
            {
                var all = new HashSet<int>(SelectedMeshIndices);
                foreach (var i in SelectedBoneIndices)
                    all.Add(i);
                foreach (var i in SelectedMorphIndices)
                    all.Add(i);
                return all;
            }
        }

        /// <summary>ActiveCategoryの選択インデックスリスト</summary>
        public List<int> ActiveSelectedIndices
        {
            get
            {
                return ActiveCategory switch
                {
                    SelectionCategory.Mesh => SelectedMeshIndices,
                    SelectionCategory.Bone => SelectedBoneIndices,
                    SelectionCategory.Morph => SelectedMorphIndices,
                    _ => new List<int>()
                };
            }
        }

        /// <summary>選択があるか</summary>
        public bool HasSelection => HasMeshSelection || HasBoneSelection || HasMorphSelection;

        /// <summary>複数選択されているか</summary>
        public bool IsMultiSelected => SelectedMeshContextIndices.Count > 1;

        /// <summary>選択中の全MeshContext（ActiveCategory準拠）</summary>
        public List<MeshContext> SelectedMeshContexts
        {
            get
            {
                var result = new List<MeshContext>();
                foreach (int idx in ActiveSelectedIndices)
                {
                    if (idx >= 0 && idx < Count)
                        result.Add(MeshContextList[idx]);
                }
                return result;
            }
        }

        /// <summary>選択リストの先頭MeshContext（便宜アクセサ・特別扱いではない）</summary>
        public MeshContext FirstSelectedMeshContext
        {
            get
            {
                var indices = ActiveSelectedIndices;
                if (indices.Count == 0) return null;
                int idx = indices[0];
                return (idx >= 0 && idx < Count) ? MeshContextList[idx] : null;
            }
        }

        /// <summary>
        /// ActiveCategoryに依存せず、常にSelectedMeshIndicesから描画メッシュを返す。
        /// SelectBoneでActiveCategoryがBoneに変わっても描画メッシュ参照を維持する。
        /// 用途: SkinWeightPaintTool/Panel等、ボーン選択中も描画メッシュへの参照が必要な場面。
        /// </summary>
        public MeshContext FirstSelectedDrawableMeshContext
        {
            get
            {
                if (SelectedMeshIndices.Count == 0) return null;
                int idx = SelectedMeshIndices[0];
                return (idx >= 0 && idx < Count) ? MeshContextList[idx] : null;
            }
        }

        /// <summary>選択リストの先頭インデックス（便宜アクセサ）</summary>
        public int FirstSelectedIndex
        {
            get
            {
                var indices = ActiveSelectedIndices;
                return indices.Count > 0 ? indices[0] : -1;
            }
        }

        /// <summary>有効なメッシュコンテキストが選択されているか</summary>
        public bool HasValidMeshContextSelection => ActiveSelectedIndices.Count > 0 && ActiveSelectedIndices[0] >= 0 && ActiveSelectedIndices[0] < Count;

        // ================================================================
        // カテゴリ別選択ヘルパー
        // ================================================================

        /// <summary>タイプに基づいて適切なカテゴリに選択を追加（重複防止）</summary>
        public void AddToSelectionByType(int index)
        {
            if (index < 0 || index >= Count) return;
            var meshContext = MeshContextList[index];
            if (meshContext == null) return;

            switch (meshContext.Type)
            {
                case MeshType.Bone:
                    if (!SelectedBoneIndices.Contains(index))
                        SelectedBoneIndices.Add(index);
                    ActiveCategory = SelectionCategory.Bone;
                    break;
                case MeshType.Morph:
                    if (!SelectedMorphIndices.Contains(index))
                        SelectedMorphIndices.Add(index);
                    ActiveCategory = SelectionCategory.Morph;
                    break;
                default:
                    // Mesh, BakedMirror, Helper, Group, RigidBody, RigidBodyJoint等
                    if (!SelectedMeshIndices.Contains(index))
                        SelectedMeshIndices.Add(index);
                    ActiveCategory = SelectionCategory.Mesh;
                    break;
            }
        }

        /// <summary>
        /// タイプに基づいて同一カテゴリのみをクリアして選択（他カテゴリは維持）
        /// メインパネルでの選択操作用
        /// </summary>
        public void SelectByTypeExclusive(int index)
        {
            if (index < 0 || index >= Count) return;
            var meshContext = MeshContextList[index];
            if (meshContext == null) return;

            switch (meshContext.Type)
            {
                case MeshType.Bone:
                    ClearBoneSelection();
                    SelectedBoneIndices.Add(index);
                    ActiveCategory = SelectionCategory.Bone;
                    break;
                case MeshType.Morph:
                    ClearMorphSelection();
                    SelectedMorphIndices.Add(index);
                    ActiveCategory = SelectionCategory.Morph;
                    break;
                default:
                    // Mesh, BakedMirror, Helper, Group, RigidBody, RigidBodyJoint等
                    ClearMeshSelection();
                    SelectedMeshIndices.Add(index);
                    ActiveCategory = SelectionCategory.Mesh;
                    break;
            }
        }

        /// <summary>タイプに基づいて適切なカテゴリから選択を解除</summary>
        public void RemoveFromSelectionByType(int index)
        {
            // 全カテゴリから削除（タイプが変わっている可能性があるため）
            SelectedMeshIndices.Remove(index);
            SelectedBoneIndices.Remove(index);
            SelectedMorphIndices.Remove(index);
        }

        /// <summary>指定インデックスがどのカテゴリで選択されているか確認</summary>
        public bool IsSelectedInAnyCategory(int index)
        {
            return SelectedMeshIndices.Contains(index) ||
                   SelectedBoneIndices.Contains(index) ||
                   SelectedMorphIndices.Contains(index);
        }

        /// <summary>全カテゴリの選択をクリア</summary>
        public void ClearAllCategorySelection()
        {
            SelectedMeshIndices.Clear();
            SelectedBoneIndices.Clear();
            SelectedMorphIndices.Clear();
        }

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
        // MorphExpressions（モーフグループ管理）
        // ================================================================

        /// <summary>モーフエクスプレッションリスト</summary>
        public List<MorphExpression> MorphExpressions { get; set; } = new List<MorphExpression>();

        /// <summary>モーフエクスプレッション数</summary>
        public int MorphExpressionCount => MorphExpressions?.Count ?? 0;

        /// <summary>モーフエクスプレッションがあるか</summary>
        public bool HasMorphExpressions => MorphExpressionCount > 0;

        /// <summary>モーフエクスプレッションを追加</summary>
        public MorphExpression AddMorphExpression(string name, MorphType type = MorphType.Vertex)
        {
            var set = new MorphExpression(name, type);
            MorphExpressions.Add(set);
            return set;
        }

        /// <summary>モーフエクスプレッションを削除</summary>
        public bool RemoveMorphExpression(MorphExpression set)
        {
            return MorphExpressions.Remove(set);
        }

        /// <summary>名前でモーフエクスプレッションを検索</summary>
        public MorphExpression FindMorphExpressionByName(string name)
        {
            return MorphExpressions.Find(s => s.Name == name);
        }

        /// <summary>メッシュインデックスでモーフエクスプレッションを検索</summary>
        public MorphExpression FindMorphExpressionByMesh(int meshIndex)
        {
            return MorphExpressions.Find(s => s.ContainsMesh(meshIndex));
        }

        /// <summary>一意なモーフエクスプレッション名を生成</summary>
        public string GenerateUniqueMorphExpressionName(string baseName = "Morph")
        {
            string name = baseName;
            int counter = 1;

            while (FindMorphExpressionByName(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }

            return name;
        }
        
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
        // 選択操作（カテゴリ対応・旧MeshListUndoContextから統合）
        // ================================================================

        /// <summary>全選択をクリア（後方互換）</summary>
        public void ClearSelection()
        {
            ClearAllCategorySelection();
        }

        /// <summary>単一選択（既存選択をクリアして選択・タイプ自動判定）</summary>
        public void Select(int index)
        {
            ClearAllCategorySelection();
            if (index >= 0 && index < Count)
                AddToSelectionByType(index);
        }

        /// <summary>選択を追加（タイプ自動判定）</summary>
        public void AddToSelection(int index)
        {
            if (index >= 0 && index < Count)
                AddToSelectionByType(index);
        }

        /// <summary>選択を解除（全カテゴリ）</summary>
        public void RemoveFromSelection(int index)
        {
            RemoveFromSelectionByType(index);
        }

        /// <summary>選択をトグル（タイプ自動判定）</summary>
        public void ToggleSelection(int index)
        {
            if (IsSelectedInAnyCategory(index))
                RemoveFromSelectionByType(index);
            else if (index >= 0 && index < Count)
                AddToSelectionByType(index);
        }

        /// <summary>範囲選択（from から to まで・タイプ自動判定）</summary>
        public void SelectRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < Count)
                    AddToSelectionByType(i);
            }
        }

        /// <summary>全選択（タイプ自動判定）</summary>
        public void SelectAll()
        {
            ClearAllCategorySelection();
            for (int i = 0; i < Count; i++)
                AddToSelectionByType(i);
        }

        /// <summary>インデックスセットから選択状態を復元（Undo用）</summary>
        public void RestoreSelectionFromIndices(IEnumerable<int> indices)
        {
            ClearAllCategorySelection();
            if (indices == null) return;
            foreach (var index in indices)
            {
                if (index >= 0 && index < Count)
                    AddToSelectionByType(index);
            }
        }

        /// <summary>選択されているか（全カテゴリ）</summary>
        public bool IsSelected(int index)
        {
            return IsSelectedInAnyCategory(index);
        }

        /// <summary>選択インデックスを検証して無効なものを除去（全カテゴリ）</summary>
        public void ValidateSelection()
        {
            SelectedMeshIndices.RemoveAll(i => i < 0 || i >= Count);
            SelectedBoneIndices.RemoveAll(i => i < 0 || i >= Count);
            SelectedMorphIndices.RemoveAll(i => i < 0 || i >= Count);
        }

        /// <summary>全カテゴリの選択インデックスをフラットリストとして取得（Undo記録用）</summary>
        public List<int> CaptureAllSelectedIndices()
        {
            var indices = new List<int>(SelectedMeshIndices.Count + SelectedBoneIndices.Count + SelectedMorphIndices.Count);
            indices.AddRange(SelectedMeshIndices);
            indices.AddRange(SelectedBoneIndices);
            indices.AddRange(SelectedMorphIndices);
            return indices;
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

            meshContext.ParentModelContext = this;
            MeshContextList.Add(meshContext);
            InvalidateTypedIndices();
            IsDirty = true;
            return MeshContextList.Count - 1;
        }

        /// <summary>メッシュを挿入</summary>
        /// <param name="adjustSelection">選択インデックスを調整するか（Undo/Redo時はfalse）</param>
        public void Insert(int index, MeshContext meshContext, bool adjustSelection = true)
        {
            if (meshContext == null)
                throw new ArgumentNullException(nameof(meshContext));
            if (index < 0 || index > MeshContextList.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            meshContext.ParentModelContext = this;
            MeshContextList.Insert(index, meshContext);
            InvalidateTypedIndices();
            IsDirty = true;

            if (adjustSelection)
            {
                // 選択インデックス調整（挿入位置以降は+1）- 各カテゴリ個別に
                SelectedMeshIndices = AdjustIndicesForInsert(SelectedMeshIndices, index);
                SelectedBoneIndices = AdjustIndicesForInsert(SelectedBoneIndices, index);
                SelectedMorphIndices = AdjustIndicesForInsert(SelectedMorphIndices, index);
            }

            // モーフエクスプレッションのインデックス調整（常に実行）
            foreach (var set in MorphExpressions)
            {
                set.AdjustIndicesOnInsert(index);
            }
        }

        /// <summary>挿入時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForInsert(List<int> indices, int insertIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
            {
                adjusted.Add(i >= insertIndex ? i + 1 : i);
            }
            return adjusted;
        }

        /// <summary>メッシュを削除</summary>
        /// <param name="adjustSelection">選択インデックスを調整するか（Undo/Redo時はfalse）</param>
        /// <returns>削除成功したか</returns>
        public bool RemoveAt(int index, bool adjustSelection = true)
        {
            if (index < 0 || index >= MeshContextList.Count)
                return false;

            MeshContextList.RemoveAt(index);
            InvalidateTypedIndices();
            IsDirty = true;

            if (adjustSelection)
            {
                // 選択インデックス調整 - 各カテゴリ個別に
                SelectedMeshIndices = AdjustIndicesForRemove(SelectedMeshIndices, index);
                SelectedBoneIndices = AdjustIndicesForRemove(SelectedBoneIndices, index);
                SelectedMorphIndices = AdjustIndicesForRemove(SelectedMorphIndices, index);

                ValidateSelection();
            }

            // モーフエクスプレッションのインデックス調整（常に実行）
            foreach (var set in MorphExpressions)
            {
                set.AdjustIndicesOnRemove(index);
            }

            return true;
        }

        /// <summary>削除時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForRemove(List<int> indices, int removeIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
            {
                if (i < removeIndex)
                    adjusted.Add(i);
                else if (i > removeIndex)
                    adjusted.Add(i - 1);
                // i == removeIndex の場合は削除されるので追加しない
            }
            return adjusted;
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

            // 選択インデックス調整 - 各カテゴリ個別に
            SelectedMeshIndices = AdjustIndicesForMove(SelectedMeshIndices, fromIndex, toIndex);
            SelectedBoneIndices = AdjustIndicesForMove(SelectedBoneIndices, fromIndex, toIndex);
            SelectedMorphIndices = AdjustIndicesForMove(SelectedMorphIndices, fromIndex, toIndex);

            InvalidateTypedIndices();
            IsDirty = true;
            return true;
        }

        /// <summary>移動時のインデックス調整ヘルパー</summary>
        private static List<int> AdjustIndicesForMove(List<int> indices, int fromIndex, int toIndex)
        {
            var adjusted = new List<int>(indices.Count);
            foreach (var i in indices)
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
            return adjusted;
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
            ClearAllCategorySelection();
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

        /// <summary>選択中の全メッシュのバウンディングボックス</summary>
        public Bounds CalculateCurrentBounds()
        {
            Bounds? combined = null;
            foreach (var mc in SelectedMeshContexts)
            {
                if (mc?.MeshObject == null) continue;
                var b = mc.MeshObject.CalculateBounds();
                combined = combined.HasValue
                    ? new Bounds(
                        (combined.Value.center + b.center) * 0.5f,
                        Vector3.Max(combined.Value.max, b.max) - Vector3.Min(combined.Value.min, b.min))
                    : b;
            }
            return combined ?? new Bounds(Vector3.zero, Vector3.one);
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
        /// 全ボーンのBindPoseを計算（WorldMatrix.inverse）
        /// ComputeWorldMatrices()の後に呼ぶこと
        /// </summary>
        public void ComputeBindPoses()
        {
            if (MeshContextList == null) return;

            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var ctx = MeshContextList[i];
                if (ctx == null || ctx.Type != MeshType.Bone) continue;

                ctx.BindPose = ctx.WorldMatrix.inverse;
            }
        }

        /// <summary>
        /// ワールド行列とBindPoseを一括計算
        /// </summary>
        public void ComputeWorldAndBindPoses()
        {
            ComputeWorldMatrices();
            ComputeBindPoses();
        }

        /// <summary>
        /// MeshContextリストからワールド行列を計算（静的メソッド・インポート時用）
        /// HierarchyParentIndexとBoneTransformに基づいて親→子の順で計算
        /// </summary>
        public static Dictionary<int, Matrix4x4> CalculateWorldMatrices(List<MeshContext> meshContexts)
        {
            var worldMatrices = new Dictionary<int, Matrix4x4>();

            int maxIterations = meshContexts.Count;
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                bool anyAdded = false;

                for (int i = 0; i < meshContexts.Count; i++)
                {
                    if (worldMatrices.ContainsKey(i))
                        continue;

                    var ctx = meshContexts[i];
                    if (ctx?.BoneTransform == null)
                        continue;

                    int parentIndex = ctx.HierarchyParentIndex;
                    Matrix4x4 parentWorld;

                    if (parentIndex < 0)
                    {
                        parentWorld = Matrix4x4.identity;
                    }
                    else if (worldMatrices.TryGetValue(parentIndex, out parentWorld))
                    {
                        // 親が計算済み
                    }
                    else
                    {
                        continue;
                    }

                    Matrix4x4 localMatrix = Matrix4x4.TRS(
                        ctx.BoneTransform.Position,
                        Quaternion.Euler(ctx.BoneTransform.Rotation),
                        ctx.BoneTransform.Scale
                    );

                    worldMatrices[i] = parentWorld * localMatrix;
                    anyAdded = true;
                }

                if (!anyAdded)
                    break;
            }

            return worldMatrices;
        }

        /// <summary>
        /// MeshContextリストのBindPoseを一括計算（静的メソッド・インポート時用）
        /// CalculateWorldMatrices + BindPose = inverse を一括実行
        /// </summary>
        public static void ComputeBindPosesFromList(List<MeshContext> meshContexts)
        {
            var worldMatrices = CalculateWorldMatrices(meshContexts);
            foreach (var kv in worldMatrices)
            {
                meshContexts[kv.Key].BindPose = kv.Value.inverse;
            }
        }

        /// <summary>
        /// HierarchyParentIndexに基づいてトポロジカルソート
        /// 親が先に来るようにインデックスを並べ替える
        /// </summary>
        public List<int> TopologicalSortByHierarchy()
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
