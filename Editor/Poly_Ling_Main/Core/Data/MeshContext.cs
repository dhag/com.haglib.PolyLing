// Assets/Editor/Poly_Ling/Data/MeshContext.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
// Phase 1: 選択状態をMeshContextに統合
// Phase Morph: モーフ基準データ対応
// Phase BonePose: BonePoseData対応（BindPose相互変換）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Transforms;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Rendering;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
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
        // 選択状態 — SelectionState が Single Source of Truth
        // 全ての選択アクセスは Selection プロパティ経由
        // ================================================================

        /// <summary>選択状態（Single Source of Truth）</summary>
        public SelectionState Selection { get; } = new SelectionState();

        /// <summary>選択中の頂点インデックス（Selection.Verticesへのリダイレクト）</summary>
        public HashSet<int> SelectedVertices
        {
            get => Selection.Vertices;
            set { Selection.Vertices.Clear(); if (value != null) Selection.Vertices.UnionWith(value); }
        }

        /// <summary>選択中のエッジ（Selection.Edgesへのリダイレクト）</summary>
        public HashSet<VertexPair> SelectedEdges
        {
            get => Selection.Edges;
            set { Selection.Edges.Clear(); if (value != null) Selection.Edges.UnionWith(value); }
        }

        /// <summary>選択中の面インデックス（Selection.Facesへのリダイレクト）</summary>
        public HashSet<int> SelectedFaces
        {
            get => Selection.Faces;
            set { Selection.Faces.Clear(); if (value != null) Selection.Faces.UnionWith(value); }
        }

        /// <summary>選択中の線分インデックス（Selection.Linesへのリダイレクト）</summary>
        public HashSet<int> SelectedLines
        {
            get => Selection.Lines;
            set { Selection.Lines.Clear(); if (value != null) Selection.Lines.UnionWith(value); }
        }

        /// <summary>選択モード（Selection.Modeへのリダイレクト）</summary>
        public MeshSelectMode SelectMode
        {
            get => Selection.Mode;
            set => Selection.Mode = value;
        }

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
                Vertices = new HashSet<int>(Selection.Vertices),
                Edges = new HashSet<VertexPair>(Selection.Edges),
                Faces = new HashSet<int>(Selection.Faces),
                Lines = new HashSet<int>(Selection.Lines),
                Mode = Selection.Mode
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
            Selection.ClearAll();
        }

        /// <summary>
        /// 選択があるか
        /// </summary>
        public bool HasSelection => Selection.HasAnySelection;



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

        /// <summary>
        /// ボーンポーズデータ（エディット＆ランタイムポーズ）
        /// BindPoseと相互変換可能
        /// null = BonePoseData未使用（BoneTransformにフォールバック）
        /// </summary>
        public BonePoseData BonePoseData { get; set; }

        // ================================================================
        // 変換行列（ワールド座標変換用）
        // ================================================================

        /// <summary>
        /// ローカル変換行列
        /// BonePoseDataが有効ならそちら優先、なければBoneTransformにフォールバック
        /// </summary>
        public Matrix4x4 LocalMatrix
        {
            get
            {
                // ベース: BoneTransformのローカル変換（親子関係の基礎）
                Matrix4x4 baseMatrix;
                if (BoneTransform == null || !BoneTransform.UseLocalTransform)
                    baseMatrix = Matrix4x4.identity;
                else
                    baseMatrix = BoneTransform.TransformMatrix;

                // BonePoseDataが有効ならデルタを乗算
                if (BonePoseData != null && BonePoseData.IsActive)
                    return baseMatrix * BonePoseData.LocalMatrix;

                return baseMatrix;
            }
        }

        /// <summary>
        /// ワールド変換行列（親子関係を考慮した累積行列）
        /// ComputeWorldMatrices()で計算される
        /// </summary>
        public Matrix4x4 WorldMatrix { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// ワールド変換行列の逆行列（キャッシュ）
        /// </summary>
        public Matrix4x4 WorldMatrixInverse { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// バインドポーズ行列（スキンドメッシュ用）
        /// インポート時のボーンのワールド位置の逆行列
        /// SkinningMatrix = WorldMatrix × BindPose
        /// </summary>
        public Matrix4x4 BindPose { get; set; } = Matrix4x4.identity;

        /// <summary>
        /// ★★★ PMXインポート時のモデル空間でのローカル軸回転（ワールド累積） ★★★
        /// VMDモーション適用時にローカル軸空間変換 (R⁻¹ * Q * R) で使用する。
        /// BoneTransform.RotationQuaternionは親からの相対回転であり、
        /// VMD変換にはこのワールド空間での累積回転が必要。削除禁止。
        /// </summary>
        public Quaternion BoneModelRotation { get; set; } = Quaternion.identity;

        // ================================================================
        // IKデータ（PMXインポート時に設定）
        // ================================================================

        /// <summary>このボーンがIKボーンか</summary>
        public bool IsIK { get; set; }

        /// <summary>IKターゲット（エフェクタ）のMeshContextListインデックス</summary>
        public int IKTargetIndex { get; set; } = -1;

        /// <summary>IKループ回数</summary>
        public int IKLoopCount { get; set; }

        /// <summary>IK1回あたりの制限角度（ラジアン）</summary>
        public float IKLimitAngle { get; set; }

        /// <summary>IKリンクチェーン</summary>
        public List<IKLinkInfo> IKLinks { get; set; }

        /// <summary>
        /// スキニング行列を取得（WorldMatrix × BindPose）
        /// </summary>
        public Matrix4x4 SkinningMatrix => WorldMatrix * BindPose;

        /// <summary>
        /// ローカル座標をワールド座標に変換
        /// </summary>
        public Vector3 LocalToWorld(Vector3 localPos)
        {
            return WorldMatrix.MultiplyPoint3x4(localPos);
        }

        /// <summary>
        /// ワールド座標をローカル座標に変換
        /// </summary>
        public Vector3 WorldToLocal(Vector3 worldPos)
        {
            return WorldMatrixInverse.MultiplyPoint3x4(worldPos);
        }

        /// <summary>
        /// ローカル方向をワールド方向に変換（法線等）
        /// </summary>
        public Vector3 LocalToWorldDirection(Vector3 localDir)
        {
            return WorldMatrix.MultiplyVector(localDir).normalized;
        }

        /// <summary>
        /// ワールド方向をローカル方向に変換
        /// </summary>
        public Vector3 WorldToLocalDirection(Vector3 worldDir)
        {
            return WorldMatrixInverse.MultiplyVector(worldDir).normalized;
        }

        // ================================================================
        // オブジェクト属性（MQOからインポート）
        // ================================================================

        /// <summary>メッシュの種類（MeshObject.Typeに委譲）</summary>
        public MeshType Type
        {
            get => MeshObject?.Type ?? MeshType.Mesh;
            set { if (MeshObject != null) MeshObject.Type = value; }
        }

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

        /// <summary>折りたたみ状態（MQO互換）</summary>
        public bool IsFolding { get; set; } = false;

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
        /// モーフ親メッシュのマスターインデックス
        /// このモーフが適用されるベースメッシュを明示的に指定
        /// -1 = 未指定（名前規則ベースで検索）
        /// </summary>
        public int MorphParentIndex { get; set; } = -1;

        /// <summary>
        /// モーフ基準データを設定（現在のメッシュ状態を基準として保存）
        /// </summary>
        /// <param name="morphName">モーフ名</param>
        public void SetAsMorph(string morphName, MeshObject baseMeshObject = null)
        {
            if (MeshObject == null || MeshObject.VertexCount == 0)
                return;

            // baseMeshObjectが指定された場合、親メッシュの位置をBasePositionsに使用
            // PMXインポータではbaseMeshObject=null（呼び出し時点でMeshObjectが親のクローン）
            MorphBaseData = MorphBaseData.FromMeshObject(baseMeshObject ?? MeshObject, morphName);
        }

        /// <summary>
        /// モーフ基準データをクリア（通常メッシュに戻す）
        /// </summary>
        public void ClearMorphData()
        {
            MorphBaseData = null;
            MorphParentIndex = -1;
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

        /// <summary>
        /// UVモーフ差分を取得（エクスポート用）
        /// </summary>
        /// <returns>変化のあるUVとその差分のリスト</returns>
        public List<(int VertexIndex, Vector2 Offset)> GetUVMorphOffsets(float threshold = 0.0001f)
        {
            if (!IsMorph || MeshObject == null || MorphBaseData == null)
                return new List<(int, Vector2)>();

            return MorphBaseData.GetSparseUVOffsets(MeshObject, threshold);
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

        /// <summary>
        /// ミラー側マテリアルのオフセット
        /// ミラー側マテリアルインデックス = 実体側インデックス + MirrorMaterialOffset
        /// </summary>
        public int MirrorMaterialOffset { get; set; } = 0;

        /// <summary>ミラーが有効か</summary>
        public bool IsMirrored => MirrorType > 0;

        /// <summary>ミラー軸をSymmetryAxisに変換</summary>
        public Poly_Ling.Symmetry.SymmetryAxis GetMirrorSymmetryAxis()
        {
            switch (MirrorAxis)
            {
                case 1: return Poly_Ling.Symmetry.SymmetryAxis.X;
                case 2: return Poly_Ling.Symmetry.SymmetryAxis.Y;
                case 4: return Poly_Ling.Symmetry.SymmetryAxis.Z;
                default: return Poly_Ling.Symmetry.SymmetryAxis.X;
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

        // ================================================================
        // ベイクミラー（実体化されたミラーメッシュ）
        // ================================================================

        /// <summary>
        /// ベイク元メッシュのインデックス
        /// -1 = ベイクされたミラーではない（通常メッシュまたはミラー属性を持つメッシュ）
        /// 0以上 = このメッシュはベイクされたミラーで、指定インデックスのメッシュが元
        /// </summary>
        public int BakedMirrorSourceIndex { get; set; } = -1;

        /// <summary>ベイクされたミラーメッシュかどうか</summary>
        public bool IsBakedMirror => BakedMirrorSourceIndex >= 0;

        /// <summary>
        /// ベイクミラーの元メッシュかどうか
        /// （MirrorType > 0 かつ BakedMirrorSourceIndex == -1）
        /// </summary>
        public bool HasBakedMirrorChild { get; set; } = false;

        public MeshContext()
        {
            BoneTransform = new BoneTransform();
        }

        // ================================================================
        // マテリアル（ModelContext への委譲）
        // ================================================================
        // マテリアルはModelContextで一元管理
        // MeshContextはMaterialOwner経由でアクセス

        /// <summary>親ModelContextへの参照（マテリアル取得用）- 必須</summary>
        internal Poly_Ling.Model.ModelContext ParentModelContext { get; set; }

        /// <summary>マテリアルリスト（ModelContextに委譲）</summary>
        public List<Material> Materials
        {
            get
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。ModelContext.Add()で追加してください。");
                    return new List<Material> { null };
                }
                return ParentModelContext.Materials;
            }
            set
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。");
                    return;
                }
                ParentModelContext.Materials = value;
            }
        }

        /// <summary>現在選択中のマテリアルインデックス（ModelContextに委譲）</summary>
        public int CurrentMaterialIndex
        {
            get
            {
                if (ParentModelContext == null) return 0;
                return ParentModelContext.CurrentMaterialIndex;
            }
            set
            {
                if (ParentModelContext == null)
                {
                    Debug.LogError("[MeshContext] MaterialOwnerが設定されていません。");
                    return;
                }
                ParentModelContext.CurrentMaterialIndex = value;
            }
        }

        /// <summary>サブメッシュ数</summary>
        public int SubMeshCount => ParentModelContext?.Materials?.Count ?? 1;

        /// <summary>現在選択中のマテリアルを取得</summary>
        public Material GetCurrentMaterial()
        {
            if (ParentModelContext == null) return null;
            var mats = ParentModelContext.Materials;
            int idx = ParentModelContext.CurrentMaterialIndex;
            if (idx >= 0 && idx < mats.Count)
                return mats[idx];
            return null;
        }

        /// <summary>指定スロットのマテリアルを取得</summary>
        public Material GetMaterial(int index)
        {
            if (ParentModelContext == null) return null;
            var mats = ParentModelContext.Materials;
            if (index >= 0 && index < mats.Count)
                return mats[index];
            return null;
        }

        // ================================================================
        // メッシュ操作メソッド（UndoRecord から直接呼び出される）
        // ================================================================

        /// <summary>頂点数</summary>
        public int VertexCount => MeshObject?.VertexCount ?? 0;

        /// <summary>面数</summary>
        public int FaceCount => MeshObject?.FaceCount ?? 0;

        /// <summary>頂点位置を取得</summary>
        public Vector3 GetVertexPosition(int index)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return Vector3.zero;
            return MeshObject.Vertices[index].Position;
        }

        /// <summary>頂点位置を設定</summary>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (MeshObject == null || index < 0 || index >= MeshObject.VertexCount)
                return;
            MeshObject.Vertices[index].Position = position;
            MeshObject.InvalidatePositionCache();
        }

        /// <summary>全頂点位置を配列で取得（Clone）</summary>
        public Vector3[] GetAllPositions()
        {
            if (MeshObject == null) return new Vector3[0];
            return (Vector3[])MeshObject.Positions.Clone();
        }

        /// <summary>全頂点位置を配列で設定</summary>
        public void SetAllPositions(Vector3[] positions)
        {
            if (MeshObject == null) return;
            MeshObject.SetPositions(positions);
        }

        /// <summary>
        /// MeshObjectの全データをUnityMeshに適用
        /// </summary>
        public void ApplyToMesh()
        {
            if (UnityMesh == null || MeshObject == null) return;

            var newMesh = MeshObject.ToUnityMeshShared();

            UnityMesh.Clear();
            UnityMesh.vertices = newMesh.vertices;
            UnityMesh.triangles = newMesh.triangles;
            UnityMesh.uv = newMesh.uv;
            UnityMesh.normals = newMesh.normals;
            UnityMesh.RecalculateBounds();

            UnityEngine.Object.DestroyImmediate(newMesh);
        }

        /// <summary>
        /// 頂点位置のみをUnityMeshに適用（高速）
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (UnityMesh == null || MeshObject == null) return;

            var newMesh = MeshObject.ToUnityMeshShared();
            UnityMesh.vertices = newMesh.vertices;
            UnityMesh.RecalculateNormals();
            UnityMesh.RecalculateBounds();

            UnityEngine.Object.DestroyImmediate(newMesh);
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

    /// <summary>
    /// IKリンク情報（CCDIKのチェーン要素）
    /// </summary>
    public class IKLinkInfo
    {
        /// <summary>リンクボーンのMeshContextListインデックス</summary>
        public int BoneIndex { get; set; }

        /// <summary>角度制限あり</summary>
        public bool HasLimit { get; set; }

        /// <summary>角度制限下限（ラジアン）</summary>
        public Vector3 LimitMin { get; set; }

        /// <summary>角度制限上限（ラジアン）</summary>
        public Vector3 LimitMax { get; set; }
    }
}
