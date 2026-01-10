// Assets/Editor/MeshFactory/Data/MeshContext.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
// Phase 1: 選択状態をMeshContextに統合
// Phase Morph: モーフ基準データ対応
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;
using MeshFactory.Model;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;
using MeshFactory.Rendering;
using MeshFactory.Symmetry;

namespace MeshFactory.Data
{
    // ================================================================
    // メッシュコンテキスト
    //   MeshObjectにUnityMeshなどを加えたもの。
    // ================================================================
    public class MeshContext
    {
        public string Name
        {
            get => MeshObject?.Name ?? "Untitled";
            set { if (MeshObject != null) MeshObject.Name = value; }
        }
        public Mesh UnityMesh;                      // Unity UnityMesh（表示用）
        public MeshObject MeshObject;               // メッシュオブジェクト
        public Vector3[] OriginalPositions;         // 元の頂点位置（リセット用）

        // ================================================================
        // 選択状態（Phase 1追加）
        // メッシュ切り替え時に選択を保持するため、各MeshContextが選択を持つ
        // ================================================================

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> SelectedVertices { get; set; } = new HashSet<int>();

        /// <summary>選択中のエッジ（頂点ペア）</summary>
        public HashSet<VertexPair> SelectedEdges { get; set; } = new HashSet<VertexPair>();

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> SelectedFaces { get; set; } = new HashSet<int>();

        /// <summary>選択中の線分インデックス（2頂点Face）</summary>
        public HashSet<int> SelectedLines { get; set; } = new HashSet<int>();

        /// <summary>選択モード</summary>
        public MeshSelectMode SelectMode { get; set; } = MeshSelectMode.Vertex;

        // ================================================================
        // 選択セット（永続的な名前付き選択）
        // ================================================================

        /// <summary>保存された選択セットのリスト</summary>
        public List<SelectionSet> SelectionSets { get; set; } = new List<SelectionSet>();

        /// <summary>
        /// 現在の選択を名前付きセットとして保存
        /// </summary>
        public SelectionSet SaveCurrentSelectionAsSet(string name)
        {
            var set = SelectionSet.FromCurrentSelection(
                name,
                SelectedVertices,
                SelectedEdges,
                SelectedFaces,
                SelectedLines,
                SelectMode
            );
            SelectionSets.Add(set);
            return set;
        }

        /// <summary>
        /// 選択セットから選択を復元（置き換え）
        /// </summary>
        public void LoadSelectionSet(SelectionSet set)
        {
            if (set == null) return;

            SelectedVertices = new HashSet<int>(set.Vertices);
            SelectedEdges = new HashSet<VertexPair>(set.Edges);
            SelectedFaces = new HashSet<int>(set.Faces);
            SelectedLines = new HashSet<int>(set.Lines);
            SelectMode = set.Mode;
        }

        /// <summary>
        /// 選択セットを現在の選択に追加（Union）
        /// </summary>
        public void AddSelectionSet(SelectionSet set)
        {
            if (set == null) return;

            SelectedVertices.UnionWith(set.Vertices);
            SelectedEdges.UnionWith(set.Edges);
            SelectedFaces.UnionWith(set.Faces);
            SelectedLines.UnionWith(set.Lines);
        }

        /// <summary>
        /// 選択セットを現在の選択から除外（Subtract）
        /// </summary>
        public void SubtractSelectionSet(SelectionSet set)
        {
            if (set == null) return;

            SelectedVertices.ExceptWith(set.Vertices);
            SelectedEdges.ExceptWith(set.Edges);
            SelectedFaces.ExceptWith(set.Faces);
            SelectedLines.ExceptWith(set.Lines);
        }

        /// <summary>
        /// 選択セットを削除
        /// </summary>
        public bool RemoveSelectionSet(SelectionSet set)
        {
            return SelectionSets.Remove(set);
        }

        /// <summary>
        /// 選択セットを名前で検索
        /// </summary>
        public SelectionSet FindSelectionSetByName(string name)
        {
            return SelectionSets.FirstOrDefault(s => s.Name == name);
        }

        /// <summary>
        /// ユニークな選択セット名を生成
        /// </summary>
        public string GenerateUniqueSelectionSetName(string baseName = "SelectionSet")
        {
            var existingNames = new HashSet<string>(SelectionSets.Select(s => s.Name));

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

        // ================================================================
        // 選択スナップショット（Save/Load用）
        // ================================================================

        /// <summary>
        /// 現在の選択状態のスナップショットを作成
        /// </summary>
        public MeshSelectionSnapshot CaptureSelection()
        {
            return new MeshSelectionSnapshot
            {
                Vertices = new HashSet<int>(SelectedVertices),
                Edges = new HashSet<VertexPair>(SelectedEdges),
                Faces = new HashSet<int>(SelectedFaces),
                Lines = new HashSet<int>(SelectedLines),
                Mode = SelectMode
            };
        }

        /// <summary>
        /// スナップショットから選択状態を復元
        /// </summary>
        public void RestoreSelection(MeshSelectionSnapshot snapshot)
        {
            if (snapshot == null)
            {
                ClearSelection();
                return;
            }

            SelectedVertices = new HashSet<int>(snapshot.Vertices ?? new HashSet<int>());
            SelectedEdges = new HashSet<VertexPair>(snapshot.Edges ?? new HashSet<VertexPair>());
            SelectedFaces = new HashSet<int>(snapshot.Faces ?? new HashSet<int>());
            SelectedLines = new HashSet<int>(snapshot.Lines ?? new HashSet<int>());
            SelectMode = snapshot.Mode;
        }

        /// <summary>
        /// 選択状態をクリア
        /// </summary>
        public void ClearSelection()
        {
            SelectedVertices.Clear();
            SelectedEdges.Clear();
            SelectedFaces.Clear();
            SelectedLines.Clear();
        }

        /// <summary>
        /// 選択があるか
        /// </summary>
        public bool HasSelection =>
            SelectedVertices.Count > 0 ||
            SelectedEdges.Count > 0 ||
            SelectedFaces.Count > 0 ||
            SelectedLines.Count > 0;

        /// <summary>
        /// SelectionStateから選択をコピー（カレント選択→MeshContext）
        /// </summary>
        public void SaveSelectionFrom(SelectionState state)
        {
            if (state == null)
            {
                ClearSelection();
                return;
            }

            SelectedVertices = new HashSet<int>(state.Vertices);
            SelectedEdges = new HashSet<VertexPair>(state.Edges);
            SelectedFaces = new HashSet<int>(state.Faces);
            SelectedLines = new HashSet<int>(state.Lines);
            SelectMode = state.Mode;
        }

        /// <summary>
        /// SelectionStateへ選択をコピー（MeshContext→カレント選択）
        /// </summary>
        public void LoadSelectionTo(SelectionState state)
        {
            if (state == null) return;

            // 各選択をクリア
            state.Vertices.Clear();
            state.Edges.Clear();
            state.Faces.Clear();
            state.Lines.Clear();
            state.Mode = SelectMode;

            foreach (var v in SelectedVertices)
                state.Vertices.Add(v);
            foreach (var e in SelectedEdges)
                state.Edges.Add(e);
            foreach (var f in SelectedFaces)
                state.Faces.Add(f);
            foreach (var l in SelectedLines)
                state.Lines.Add(l);
        }

        // ================================================================
        // 階層・トランスフォーム（MeshObjectへの参照）
        // 階層には２種類ある。
        // メッシュの編集用階層：モデリングアプリと同様の親子関係
        // ゲームオブジェクト階層：ボーン等に利用する親子関係
        // ================================================================

        /// <summary>親メッシュのインデックス（-1=ルート）</summary>
        public int ParentIndex
        {
            get => MeshObject?.ParentIndex ?? -1;
            set { if (MeshObject != null) MeshObject.ParentIndex = value; }
        }

        /// <summary>階層深度（MQO互換）</summary>
        public int Depth
        {
            get => MeshObject?.Depth ?? 0;
            set { if (MeshObject != null) MeshObject.Depth = value; }
        }

        /// <summary>ゲームオブジェクト階層の親（将来用）</summary>
        public int HierarchyParentIndex
        {
            get => MeshObject?.HierarchyParentIndex ?? -1;
            set { if (MeshObject != null) MeshObject.HierarchyParentIndex = value; }
        }

        /// <summary>エクスポート設定</summary>
        public BoneTransform BoneTransform
        {
            get => MeshObject?.BoneTransform;
            set { if (MeshObject != null) MeshObject.BoneTransform = value ?? new BoneTransform(); }
        }

        // ================================================================
        // オブジェクト属性（MQOからインポート）
        // ================================================================

        /// <summary>メッシュの種類</summary>
        public MeshType Type { get; set; } = MeshType.Mesh;

        // ----------------------------------------------------------------
        // 親子関係について
        // ----------------------------------------------------------------
        // MQOでは「depth」値で親子関係を表現する（リスト順序に依存）。
        // しかしdepth値だけでは削除・順序変更で親子関係が破綻する。
        //
        // 【設計方針】
        // - Depth: MQOとの互換用。表示インデント等に使用。
        // - ParentIndex: 実際の親子関係。削除・移動時はこちらを更新。
        //
        // 【運用ルール】
        // - インポート時: MQOのdepthからParentIndexを計算して設定
        // - 削除時: 子のParentIndexを親の親に付け替える
        // - 順序変更時: ParentIndexを新しいインデックスに更新
        // - エクスポート時: ParentIndexからdepthを再計算
        // ----------------------------------------------------------------

        /// <summary>可視状態</summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>編集禁止（ロック）</summary>
        public bool IsLocked { get; set; } = false;

        // ================================================================
        // モーフ基準データ（Phase: Morph対応）
        // ================================================================
        // 
        // 【設計思想】
        // 通常のモーフ形式：相対移動量を保存（ベース位置 + オフセット）
        // 本システム：絶対位置を保存（編集しやすく、紛失しにくい）
        // 
        // メッシュ頂点（MeshObject.Vertices）: モーフ**適用後**の位置
        // MorphBaseData: モーフ**適用前**の基準位置
        // 
        // エクスポート時に差分を計算して相対移動量として出力
        // ----------------------------------------------------------------

        /// <summary>
        /// モーフ基準データ（モーフ前の位置を保持）
        /// nullの場合、このメッシュはモーフではない
        /// </summary>
        public MorphBaseData MorphBaseData { get; set; }

        /// <summary>
        /// モーフメッシュかどうか
        /// MorphBaseDataが有効な場合true
        /// </summary>
        public bool IsMorph => MorphBaseData != null && MorphBaseData.IsValid;

        /// <summary>
        /// モーフ名（MorphBaseDataから取得、後方互換）
        /// </summary>
        public string MorphName
        {
            get => MorphBaseData?.MorphName ?? "";
            set
            {
                if (MorphBaseData != null)
                    MorphBaseData.MorphName = value;
            }
        }

        /// <summary>
        /// モーフパネル（PMX: 0=眉, 1=目, 2=口, 3=その他）
        /// </summary>
        public int MorphPanel
        {
            get => MorphBaseData?.Panel ?? 3;
            set
            {
                if (MorphBaseData != null)
                    MorphBaseData.Panel = value;
            }
        }

        /// <summary>
        /// モーフ基準データを設定（現在のメッシュ状態を基準として保存）
        /// </summary>
        /// <param name="morphName">モーフ名</param>
        public void SetAsMorph(string morphName)
        {
            if (MeshObject == null || MeshObject.VertexCount == 0)
                return;

            MorphBaseData = MorphBaseData.FromMeshObject(MeshObject, morphName);
        }

        /// <summary>
        /// モーフ基準データをクリア（通常メッシュに戻す）
        /// </summary>
        public void ClearMorphData()
        {
            MorphBaseData = null;
        }

        /// <summary>
        /// モーフをリセット（基準位置に戻す）
        /// </summary>
        public void ResetToMorphBase()
        {
            if (!IsMorph || MeshObject == null)
                return;

            MorphBaseData.ApplyBaseToMeshObject(MeshObject);
        }

        /// <summary>
        /// モーフ差分を取得（エクスポート用）
        /// </summary>
        /// <returns>変化のある頂点とその差分のリスト</returns>
        public List<(int VertexIndex, Vector3 Offset)> GetMorphOffsets(float threshold = 0.0001f)
        {
            if (!IsMorph || MeshObject == null)
                return new List<(int, Vector3)>();

            return MorphBaseData.GetSparseOffsets(MeshObject, threshold);
        }

        // ================================================================
        // エクスポート制御フラグ（Phase: Morph対応）
        // ================================================================

        /// <summary>
        /// モデルエクスポート時にこのメッシュを除外するか
        /// true: エクスポートしない（作業用メッシュ、モーフ専用メッシュ等）
        /// false: 通常通りエクスポート（デフォルト）
        /// </summary>
        public bool ExcludeFromExport { get; set; } = false;

        // ================================================================
        // ミラー設定（MQOからインポート）
        // ================================================================

        /// <summary>ミラータイプ (0:なし, 1:分離, 2:結合)</summary>
        public int MirrorType { get; set; } = 0;

        /// <summary>ミラー軸 (1:X, 2:Y, 4:Z)</summary>
        public int MirrorAxis { get; set; } = 1;

        /// <summary>ミラー距離</summary>
        public float MirrorDistance { get; set; } = 0f;

        /// <summary>ミラーが有効か</summary>
        public bool IsMirrored => MirrorType > 0;

        /// <summary>ミラー軸をSymmetryAxisに変換</summary>
        public MeshFactory.Symmetry.SymmetryAxis GetMirrorSymmetryAxis()
        {
            switch (MirrorAxis)
            {
                case 1: return MeshFactory.Symmetry.SymmetryAxis.X;
                case 2: return MeshFactory.Symmetry.SymmetryAxis.Y;
                case 4: return MeshFactory.Symmetry.SymmetryAxis.Z;
                default: return MeshFactory.Symmetry.SymmetryAxis.X;
            }
        }

        // ================================================================
        // ミラーメッシュキャッシュ
        // ================================================================

        /// <summary>ミラー表示用メッシュキャッシュ（遅延初期化）</summary>
        private SymmetryMeshCache _symmetryCache;

        /// <summary>ミラーメッシュキャッシュを取得（遅延初期化）</summary>
        public SymmetryMeshCache SymmetryCache
        {
            get
            {
                if (_symmetryCache == null)
                    _symmetryCache = new SymmetryMeshCache();
                return _symmetryCache;
            }
        }

        /// <summary>ミラーキャッシュを無効化（トポロジー変更時に呼ぶ）</summary>
        public void InvalidateSymmetryCache()
        {
            _symmetryCache?.Invalidate();
        }

        /// <summary>ミラーキャッシュをクリア（リソース解放）</summary>
        public void ClearSymmetryCache()
        {
            _symmetryCache?.Clear();
            _symmetryCache = null;
        }

        public MeshContext()
        {
            BoneTransform = new BoneTransform();
        }

        // ================================================================
        // マテリアル（後方互換用 - ModelContext への委譲）
        // ================================================================
        // Phase 1: Materials は ModelContext に集約されたが、
        // 外部ファイル（MQOImporter等）との互換性のため委譲プロパティを維持

        /// <summary>親ModelContextへの参照（マテリアル取得用）</summary>
        internal MeshFactory.Model.ModelContext MaterialOwner { get; set; }

        /// <summary>フォールバック用マテリアルリスト（MaterialOwnerがない場合）</summary>
        private List<Material> _fallbackMaterials = new List<Material> { null };
        private int _fallbackMaterialIndex = 0;

        /// <summary>マテリアルリスト（後方互換）</summary>
        public List<Material> Materials
        {
            get => MaterialOwner?.Materials ?? _fallbackMaterials;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.Materials = value;
                else
                    _fallbackMaterials = value ?? new List<Material> { null };
            }
        }

        /// <summary>現在選択中のマテリアルインデックス（後方互換）</summary>
        public int CurrentMaterialIndex
        {
            get => MaterialOwner?.CurrentMaterialIndex ?? _fallbackMaterialIndex;
            set
            {
                if (MaterialOwner != null)
                    MaterialOwner.CurrentMaterialIndex = value;
                else
                    _fallbackMaterialIndex = value;
            }
        }

        /// <summary>サブメッシュ数（後方互換）</summary>
        public int SubMeshCount => Materials?.Count ?? 1;

        /// <summary>現在選択中のマテリアルを取得（後方互換）</summary>
        public Material GetCurrentMaterial()
        {
            var mats = Materials;
            int idx = CurrentMaterialIndex;
            if (idx >= 0 && idx < mats.Count)
                return mats[idx];
            return null;
        }

        /// <summary>指定スロットのマテリアルを取得（後方互換）</summary>
        public Material GetMaterial(int index)
        {
            var mats = Materials;
            if (index >= 0 && index < mats.Count)
                return mats[index];
            return null;
        }
    }

    // ================================================================
    // MeshContext用選択スナップショット
    // ================================================================

    /// <summary>
    /// MeshContext用の選択状態スナップショット
    /// メッシュ切り替え時の保存/復元、Undo/Redo、シリアライズに使用
    /// </summary>
    [Serializable]
    public class MeshSelectionSnapshot
    {
        /// <summary>選択モード</summary>
        public MeshSelectMode Mode;

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> Vertices;

        /// <summary>選択中のエッジ（頂点ペア）</summary>
        public HashSet<VertexPair> Edges;

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> Faces;

        /// <summary>選択中の線分インデックス</summary>
        public HashSet<int> Lines;

        /// <summary>デフォルトコンストラクタ</summary>
        public MeshSelectionSnapshot()
        {
            Mode = MeshSelectMode.Vertex;
            Vertices = new HashSet<int>();
            Edges = new HashSet<VertexPair>();
            Faces = new HashSet<int>();
            Lines = new HashSet<int>();
        }

        /// <summary>クローンを作成</summary>
        public MeshSelectionSnapshot Clone()
        {
            return new MeshSelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(this.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(this.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(this.Lines ?? new HashSet<int>())
            };
        }

        /// <summary>差異があるか判定</summary>
        public bool IsDifferentFrom(MeshSelectionSnapshot other)
        {
            if (other == null) return true;
            if (Mode != other.Mode) return true;
            if (!SetEquals(Vertices, other.Vertices)) return true;
            if (!SetEquals(Edges, other.Edges)) return true;
            if (!SetEquals(Faces, other.Faces)) return true;
            if (!SetEquals(Lines, other.Lines)) return true;
            return false;
        }

        private static bool SetEquals<T>(HashSet<T> a, HashSet<T> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.SetEquals(b);
        }

        /// <summary>選択があるか</summary>
        public bool HasSelection =>
            (Vertices?.Count ?? 0) > 0 ||
            (Edges?.Count ?? 0) > 0 ||
            (Faces?.Count ?? 0) > 0 ||
            (Lines?.Count ?? 0) > 0;

        /// <summary>全てクリア</summary>
        public void Clear()
        {
            Vertices?.Clear();
            Edges?.Clear();
            Faces?.Clear();
            Lines?.Clear();
        }

        /// <summary>
        /// SelectionSnapshotから変換（既存システムとの互換）
        /// </summary>
        public static MeshSelectionSnapshot FromSelectionSnapshot(SelectionSnapshot snapshot)
        {
            if (snapshot == null) return new MeshSelectionSnapshot();

            return new MeshSelectionSnapshot
            {
                Mode = snapshot.Mode,
                Vertices = new HashSet<int>(snapshot.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(snapshot.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(snapshot.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(snapshot.Lines ?? new HashSet<int>())
            };
        }

        /// <summary>
        /// SelectionSnapshotへ変換（既存システムとの互換）
        /// </summary>
        public SelectionSnapshot ToSelectionSnapshot()
        {
            return new SelectionSnapshot
            {
                Mode = this.Mode,
                Vertices = new HashSet<int>(this.Vertices ?? new HashSet<int>()),
                Edges = new HashSet<VertexPair>(this.Edges ?? new HashSet<VertexPair>()),
                Faces = new HashSet<int>(this.Faces ?? new HashSet<int>()),
                Lines = new HashSet<int>(this.Lines ?? new HashSet<int>())
            };
        }
    }
}
