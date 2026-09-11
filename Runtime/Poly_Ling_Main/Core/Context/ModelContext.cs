// Assets/Editor/Poly_Ling/Model/ModelContext.cs
// ランタイム用モデルコンテキスト
// ModelDataのランタイム版 - SimpleMeshFactory内のモデルデータを一元管理
// v1.3: MeshListUndoContext統合（複数選択、Undoコールバック対応）
//
// 【分割先】このファイルから次へ分けてある。
//   ModelContext.Data.cs       ModelContext：モデル単位の付帯データ（モーフ式・選択辞書・オブジェクトグループ・データストア・
//   ModelContext.Highlight.cs  ModelContext：揺れもの編集の強調表示と Humanoid ボーンマッピング。
//   ModelContext.MeshList.cs   ModelContext：メッシュリスト操作・全体操作・複製・バウンディングボックス・ワールド変換行列。
//   ModelContext.Selection.cs  ModelContext：選択操作（カテゴリ対応）と、リスト構造変更に伴う索引参照の付け替え。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
////using MeshContext = MeshContext;

namespace Poly_Ling.Context
{
    /// <summary>
    /// モデル全体のランタイムコンテキスト
    /// SimpleMeshFactory内のデータを一元管理
    /// Undo用コンテキストとしても使用（旧MeshListUndoContextを統合）
    /// </summary>
    public partial class ModelContext
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
        /// PMX のモデル情報（名前・英語名・コメント）。null = PMX 由来でない。
        /// ModelContext.Name はファイル名由来で上書きされるため、
        /// PMX が持っていた表示名とコメントはここで保持する。
        /// </summary>
        public Poly_Ling.Data.PmxModelInfoData PmxModelInfo { get; set; }

        /// <summary>
        /// 各ボーンのPMXワールド位置（インポート時の初期位置）
        /// MikuMikuFlexの「ローカル位置」に相当。CCDIKSolverで使用。
        /// </summary>
        public Vector3[] BoneWorldPositions { get; set; }

        /// <summary>
        /// Tポーズ変換前の姿勢バックアップ（CSV保存対象）
        /// </summary>
        public TPoseBackup TPoseBackup { get; set; }

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
        public List<int> SelectedDrawableMeshIndices { get; set; } = new List<int>();


        /// <summary>選択中のボーンインデックス（Bone タイプ）- 選択順序を保持</summary>
        public List<int> SelectedBoneIndices { get; set; } = new List<int>();

        /// <summary>選択中の頂点モーフインデックス（Morph タイプ）- 選択順序を保持</summary>
        public List<int> SelectedMorphIndices { get; set; } = new List<int>();



        /// <summary>メッシュが選択されているか</summary>
        public bool HasMeshSelection => SelectedDrawableMeshIndices.Count > 0;

        /// <summary>ボーンが選択されているか</summary>
        public bool HasBoneSelection => SelectedBoneIndices.Count > 0;

        /// <summary>頂点モーフが選択されているか</summary>
        public bool HasMorphSelection => SelectedMorphIndices.Count > 0;

        /// <summary>メッシュが複数選択されているか</summary>
        public bool IsMeshMultiSelected => SelectedDrawableMeshIndices.Count > 1;

        /// <summary>ボーンが複数選択されているか</summary>
        public bool IsBoneMultiSelected => SelectedBoneIndices.Count > 1;

        /// <summary>頂点モーフが複数選択されているか</summary>
        public bool IsMorphMultiSelected => SelectedMorphIndices.Count > 1;

        /// <summary>メッシュ選択リストの先頭インデックス</summary>
        public int FirstMeshIndex => SelectedDrawableMeshIndices.Count > 0 ? SelectedDrawableMeshIndices[0] : -1;

        /// <summary>ボーン選択リストの先頭インデックス</summary>
        public int FirstBoneIndex => SelectedBoneIndices.Count > 0 ? SelectedBoneIndices[0] : -1;

        /// <summary>モーフ選択リストの先頭インデックス</summary>
        public int FirstMorphIndex => SelectedMorphIndices.Count > 0 ? SelectedMorphIndices[0] : -1;

        // ================================================================
        // カテゴリ別選択操作
        // ================================================================

        /// <summary>Drawableメッシュを選択（単一選択・Mesh/BakedMirrorカテゴリ専用）</summary>
        public void SelectMesh(int index)
        {
            ClearMeshSelection();
            if (index >= 0 && index < Count)
            {
                SelectedDrawableMeshIndices.Add(index);
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
            if (index >= 0 && index < Count && !SelectedDrawableMeshIndices.Contains(index))
            {
                SelectedDrawableMeshIndices.Add(index);
                ActiveCategory = SelectionCategory.Mesh;
            }
        }

        /// <summary>メッシュ選択をトグル（Ctrl+クリック用）</summary>
        public void ToggleMeshSelection(int index)
        {
            if (index < 0 || index >= Count) return;
            
            if (SelectedDrawableMeshIndices.Contains(index))
            {
                SelectedDrawableMeshIndices.Remove(index);
            }
            else
            {
                SelectedDrawableMeshIndices.Add(index);
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
                if (!SelectedDrawableMeshIndices.Contains(i))
                    SelectedDrawableMeshIndices.Add(i);
            }
            ActiveCategory = SelectionCategory.Mesh;
        }

        /// <summary>メッシュ選択から除去</summary>
        public void RemoveFromMeshSelection(int index)
        {
            SelectedDrawableMeshIndices.Remove(index);
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
        public void ClearMeshSelection() => SelectedDrawableMeshIndices.Clear();

        /// <summary>ボーン選択をクリア</summary>
        public void ClearBoneSelection() => SelectedBoneIndices.Clear();

        /// <summary>頂点モーフ選択をクリア</summary>
        public void ClearMorphSelection() => SelectedMorphIndices.Clear();

        /// <summary>
        /// アクティブ選択カテゴリを設定する。
        /// リモート選択反映（軽量クライアント）で選択リストと併せて復元するために使用。
        /// </summary>
        public void SetActiveCategory(SelectionCategory category) => ActiveCategory = category;

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
                var all = new HashSet<int>(SelectedDrawableMeshIndices);
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
                    SelectionCategory.Mesh => SelectedDrawableMeshIndices,
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
        public MeshContext FirstDrawableMeshContext
        {
            get
            {
                if (SelectedDrawableMeshIndices.Count == 0) return null;
                int idx = SelectedDrawableMeshIndices[0];
                return (idx >= 0 && idx < Count) ? MeshContextList[idx] : null;
            }
        }

        // ================================================================
        // 編集対象メッシュの取得元（一本化）
        // ================================================================
        //
        // FirstSelectedMeshContext は ActiveCategory が Bone/Morph のとき
        // メッシュ以外を指し得る。FirstDrawableMeshContext は常に描画メッシュを返す。
        // メッシュ編集ツール・ハンドラ・オーバーレイは必ず ActiveMeshContext /
        // ActiveMeshIndex を使うこと。MeshObject と行列の取得元がずれると座標がずれる。
        // ================================================================

        /// <summary>編集対象メッシュ（描画メッシュ優先）。</summary>
        public MeshContext ActiveMeshContext => FirstDrawableMeshContext ?? FirstSelectedMeshContext;

        /// <summary>編集対象メッシュの MeshContextList インデックス。未解決は -1。</summary>
        public int ActiveMeshIndex
        {
            get
            {
                if (SelectedDrawableMeshIndices.Count > 0) return SelectedDrawableMeshIndices[0];
                return FirstSelectedIndex;
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
        public void AddMeshContextToSelection(int index)
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
                    if (!SelectedDrawableMeshIndices.Contains(index))
                        SelectedDrawableMeshIndices.Add(index);
                    ActiveCategory = SelectionCategory.Mesh;
                    break;
            }
        }

        /// <summary>
        /// タイプに基づいて同一カテゴリのみをクリアして選択（他カテゴリは維持）
        /// メインパネルでの選択操作用
        /// </summary>
        public void SelectMeshContextExclusive(int index)
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
                    SelectedDrawableMeshIndices.Add(index);
                    ActiveCategory = SelectionCategory.Mesh;
                    break;
            }
        }

        /// <summary>タイプに基づいて適切なカテゴリから選択を解除</summary>
        public void RemoveFromSelectionByCategory(int index)
        {
            if (index < 0 || index >= Count) return;
            var meshContext = MeshContextList[index];
            if (meshContext == null) return;
            switch (meshContext.Type)
            {
                case MeshType.Bone:
                    SelectedBoneIndices.Remove(index);
                    break;
                case MeshType.Morph:
                    SelectedMorphIndices.Remove(index);
                    break;
                default:
                    SelectedDrawableMeshIndices.Remove(index);
                    break;
            }
        }

        /// <summary>指定インデックスがどのカテゴリで選択されているか確認</summary>
        public bool IsSelectedInAnyCategory(int index)
        {
            return SelectedDrawableMeshIndices.Contains(index) ||
                   SelectedBoneIndices.Contains(index) ||
                   SelectedMorphIndices.Contains(index);
        }

        /// <summary>全カテゴリの選択をクリア</summary>
        public void ClearAllCategorySelection()
        {
            SelectedDrawableMeshIndices.Clear();
            SelectedBoneIndices.Clear();
            SelectedMorphIndices.Clear();
            SelectedDrawableMeshIndices.Clear();
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

        /// <summary>選択ボーンのTransform変更通知（UI→UndoHelperへの橋渡し）</summary>
        public Action<BoneTransformSnapshot, BoneTransformSnapshot, string> OnBoneTransformChanged;

        // ================================================================
        // WorkPlaneContext
        // ================================================================

        /// <summary>作業平面</summary>
        public WorkPlaneContext WorkPlane { get; set; }

        // ================================================================
        // WorkAxisContext（作業用ローカル軸）
        // ================================================================

        /// <summary>
        /// 作業用ローカル軸。回転 / 曲げの基準フレーム。Origin はワールド座標。
        /// null にはせず常にインスタンスを持たせる（呼び出し側の null 判定を減らすため）。
        /// </summary>
        public WorkAxisContext WorkAxis { get; set; } = new WorkAxisContext();

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
    }
}
