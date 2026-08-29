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
using UnityEngine;
using Poly_Ling.UndoSystem;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;
using Poly_Ling.Selection;
using Poly_Ling.Context;
using Poly_Ling.MeshBridge;
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

        /// <summary>
        /// 旧 Mesh を破棄するか。既定 true。
        ///
        /// 【経緯】
        /// 全差し替え箇所で破棄を有効にしたところ、矩形選択が一部頂点で効かなくなった。
        /// 当時は UnifiedSystemAdapter.WritebackTransformedVertices の 2 箇所を
        /// 直接代入へ戻す（＝破棄しない＝リークを容認する）ことで回避していた。
        /// 現在はその 2 箇所も ReplaceUnityMesh を通し、破棄は下の退避キューで
        /// 1 フレーム遅らせている。リークは無い。
        ///
        /// 再び選択や表示に異常が出た場合は、まずこれを false にして切り分けること。
        /// </summary>
        public static bool DestroyReplacedUnityMesh = true;

        // ================================================================
        // Mesh の退避キュー（遅延破棄）
        //
        // 【なぜ即時破棄しないか】
        //   Graphics.DrawMesh は「そのフレームの描画に使う」提出であり、
        //   実際に読まれるのはレンダースレッドがコマンドを処理するとき。
        //   提出後・描画前に同じフレーム内で Mesh を破棄すると、
        //   レンダラーから見れば解放済みオブジェクトの参照になる。
        //
        //   WritebackTransformedVertices は DrawMesh 提出と同一フレーム内で
        //   走り得る唯一の差し替え経路なので、ここだけは破棄を次フレームへ回す。
        //
        // 【フラッシュ地点】
        //   UnifiedSystemAdapter.WritebackTransformedVertices の先頭
        //   MeshSceneRenderer.RebuildAdapter の入口
        //   UnifiedSystemAdapter.Dispose
        //   いずれも「前フレームの描画は終わっている」地点。
        //
        // 【注意】キューに積んだ Mesh を再び使ってはならない。
        //   ReplaceUnityMesh / RetireUnityMesh を通った時点で参照は捨てること。
        // ================================================================
        private static readonly List<Mesh> _retiredMeshes = new List<Mesh>();

        /// <summary>退避キューに積まれている Mesh の数。診断用。</summary>
        public static int RetiredMeshCount => _retiredMeshes.Count;

        /// <summary>
        /// Mesh を退避キューへ積む。破棄は次の FlushRetiredMeshes まで遅らせる。
        /// null / 既にキューにあるものは無視する。
        /// </summary>
        public static void RetireUnityMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (!DestroyReplacedUnityMesh) return;

            // 同一フレームに同じ Mesh が二重に積まれると二重破棄になる。
            for (int i = 0; i < _retiredMeshes.Count; i++)
                if (ReferenceEquals(_retiredMeshes[i], mesh)) return;

            _retiredMeshes.Add(mesh);
        }

        /// <summary>
        /// 退避キューの Mesh をまとめて破棄する。
        /// 「前フレームの描画が終わっている」と言える地点でのみ呼ぶこと。
        /// </summary>
        public static void FlushRetiredMeshes()
        {
            if (_retiredMeshes.Count == 0) return;

            for (int i = 0; i < _retiredMeshes.Count; i++)
                DestroyMesh(_retiredMeshes[i]);

            _retiredMeshes.Clear();
        }

        /// <summary>
        /// UnityMesh を差し替え、旧 Mesh を退避キューへ積む（遅延破棄）。
        ///
        /// 描画提出と同一フレーム内で走り得る経路
        /// （UnifiedSystemAdapter.WritebackTransformedVertices）はこちらを使う。
        /// それ以外の個別操作は ReplaceUnityMesh（即時破棄）でよい。
        /// </summary>
        public void ReplaceUnityMeshDeferred(Mesh newMesh)
        {
            var old = UnityMesh;
            UnityMesh = newMesh;

            if (old == null || ReferenceEquals(old, newMesh)) return;
            RetireUnityMesh(old);
        }

        /// <summary>
        /// UnityMesh を差し替える。
        ///
        /// 【必ずこれを使うこと】
        /// Mesh はネイティブオブジェクトで、C# 参照を捨てても GC では解放されない。
        /// ctx.UnityMesh = mo.ToUnityMesh() のように直接代入すると旧 Mesh が
        /// 到達不能なまま常駐し、Undo / ミラー再構築 / 変換のたびに積み上がる
        /// （長時間の編集で目に見えて重くなる原因）。
        ///
        /// 旧 Mesh の破棄は DestroyReplacedUnityMesh で切り替える。
        /// false のときは差し替えるだけで、変更前と同じ挙動になる。
        /// 同一インスタンスの再代入では破棄しない。
        /// </summary>
        public void ReplaceUnityMesh(Mesh newMesh)
        {
            var old = UnityMesh;
            UnityMesh = newMesh;

            if (!DestroyReplacedUnityMesh) return;
            if (old == null || ReferenceEquals(old, newMesh)) return;
            DestroyMesh(old);
        }

        /// <summary>再生中か否かで Destroy / DestroyImmediate を使い分ける。</summary>
        public static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            Poly_Ling.Diagnostics.PLResStat.LiveMesh--;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else                       UnityEngine.Object.DestroyImmediate(mesh);
        }

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
        public List<PartsSelectionSet> PartsSelectionSetList { get; set; } = new List<PartsSelectionSet>();

        /// <summary>
        /// 現在の選択を名前付きセットとして保存
        /// </summary>
        public PartsSelectionSet SaveCurrentSelectionAsSet(string name)
        {
            var set = PartsSelectionSet.FromCurrentSelection(
                name,
                SelectedVertices,
                SelectedEdges,
                SelectedFaces,
                SelectedLines,
                SelectMode
            );
            PartsSelectionSetList.Add(set);
            return set;
        }

        /// <summary>
        /// 選択セットから選択を復元（置き換え）
        /// </summary>
        public void LoadSelectionSet(PartsSelectionSet set)
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
        public void AddSelectionSet(PartsSelectionSet set)
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
        public void SubtractSelectionSet(PartsSelectionSet set)
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
        public bool RemoveSelectionSet(PartsSelectionSet set)
        {
            return PartsSelectionSetList.Remove(set);
        }

        /// <summary>
        /// 選択セットを名前で検索
        /// </summary>
        public PartsSelectionSet FindSelectionSetByName(string name)
        {
            return PartsSelectionSetList.FirstOrDefault(s => s.Name == name);
        }

        /// <summary>
        /// ユニークな選択セット名を生成
        /// </summary>
        public string GenerateUniqueSelectionSetName(string baseName = "SelectionSet")
        {
            var existingNames = new HashSet<string>(PartsSelectionSetList.Select(s => s.Name));

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
        /// <summary>
        /// 親の MeshContextList 索引。HierarchyParentIndex と同じ場所を指す。
        ///
        /// 【なぜ委譲か】
        ///   この 2 つは同じ「親」を表しており、値が食い違ってよい場面が無い。
        ///   実際、親を書く箇所（MeshHierarchyOps / ObjectArrayInserter /
        ///   ObjectPoseWedgeInserter / MeshFilterToSkinnedConverter）は
        ///   すべて両方へ同じ値を入れている。
        ///   別々の入れ物にしておくと、片方だけ書く箇所が 1 つ生まれるだけで
        ///   食い違いが保存され、あとから読む側が壊れる
        ///   （実測: 並べ替えが HierarchyParentIndex だけを書き、
        ///    ParentIndex にブリッジ挿入前の古い値が残っていた）。
        ///   入れ物を 1 つにして、食い違いを作れなくする。
        /// </summary>
        public int ParentIndex
        {
            get => HierarchyParentIndex;
            set => HierarchyParentIndex = value;
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

        /// <summary>
        /// スキンドか（頂点がボーンウェイトを持つ描画オブジェクトか）。
        ///
        /// 【何を分けるための判定か】
        ///   頂点の座標系が違う。
        ///     非スキンド … 頂点はローカル空間。ワールドへ出すには WorldMatrix を掛ける
        ///     スキンド   … 頂点はワールド（バインド）空間。描画は
        ///                  SkinningMatrix = WorldMatrix × BindPose を通し、静止時は単位。
        ///                  ここへ WorldMatrix を掛けると二重に効いて位置が飛ぶ
        ///
        ///   スキンド変換後のメッシュは親がボーンになるため WorldMatrix は
        ///   ボーンのワールド行列になる。「メッシュ自身の姿勢」ではないので、
        ///   頂点へ掛けてはいけない。
        ///
        /// 【この判定が答えるのは「ウェイトを持つか」だけ】
        ///   どの行列を使うかまでは決めない。用途によって意味が違うため。
        ///   例: UnifiedBufferManager.UpdateTransformMatrices の行列表は
        ///   ボーンの欄に SkinningMatrix を要求する（スキンド頂点の boneIndex が
        ///   その欄を引くため）。ここに「頂点の座標系」の判定を持ち込むと、
        ///   ボーンの欄が WorldMatrix になってスキンド頂点が全部飛ぶ。
        ///   型で対象を絞るのは各所の責任とし、ここはウェイトの有無だけを答える。
        ///
        ///   ボーンは頂点を持たないので、結果は常に false。
        ///
        /// 【O(1) である】
        ///   MeshObject.SkinKind の明示状態を読むだけ。以前は全頂点を走査していたため、
        ///   カメラ操作・ドラッグ・行列アップロードのたびに
        ///   全 MeshContext × 全頂点ぶんの走査が走っていた。
        ///   実頂点にウェイトが入っているかを知りたい場合はここではなく
        ///   MeshObject.AnyVertexHasBoneWeight() を呼ぶこと。
        /// </summary>
        public bool IsSkinned => MeshObject?.IsSkinnedKind ?? false;

        /// <summary>
        /// 描画オブジェクトの種別（MeshObject に委譲）。MeshObject が無いときは MeshFilter。
        /// 設定は MeshObject.SetSkinKind と同じく明示操作。
        /// </summary>
        public SkinKind SkinKind
        {
            get => MeshObject?.SkinKind ?? SkinKind.MeshFilter;
            set { if (MeshObject != null) MeshObject.SetSkinKind(value); }
        }

        /// <summary>
        /// 頂点をワールドへ出すために掛ける行列。
        /// スキンドは頂点が既にワールド（バインド）空間なので単位行列。
        /// </summary>
        public Matrix4x4 VertexToWorldMatrix
            => IsSkinned ? Matrix4x4.identity : WorldMatrix;

        /// <summary>
        /// ワールド座標を、このオブジェクトの頂点格納空間へ落とす行列。
        /// VertexToWorldMatrix の逆。
        /// </summary>
        public Matrix4x4 WorldToVertexMatrix
            => IsSkinned ? Matrix4x4.identity : WorldMatrixInverse;

        /// <summary>
        /// 左右で対になるボーンの MeshContextList 索引（-1 = 対なし）。
        /// スキンド変換が確定させた値。ウェイトからの推定に使ってはならない。
        /// </summary>
        public int MirrorBoneIndex
        {
            get => MeshObject?.MirrorBoneIndex ?? -1;
            set { if (MeshObject != null) MeshObject.MirrorBoneIndex = value; }
        }

        /// <summary>
        /// アーマチャ生成時にボーンを生成しない（MeshObject に委譲）
        /// </summary>
        public bool IsMirrorBranchRoot
        {
            get => MeshObject?.IsMirrorBranchRoot ?? false;
            set { if (MeshObject != null) MeshObject.IsMirrorBranchRoot = value; }
        }

        public bool IgnorePoseInArmature
        {
            get => MeshObject?.IgnorePoseInArmature ?? false;
            set { if (MeshObject != null) MeshObject.IgnorePoseInArmature = value; }
        }

        /// <summary>
        /// 頂点法線を保持する（自動再計算を行わない）。実体は MeshObject 側。
        /// </summary>
        public bool PreserveNormals
        {
            get => MeshObject?.PreserveNormals ?? false;
            set { if (MeshObject != null) MeshObject.PreserveNormals = value; }
        }

        /// <summary>
        /// 法線の自動再計算から除外するセット一覧。実体は MeshObject 側。
        /// </summary>
        public List<PartsSelectionSet> NormalRecalcExcludeList
        {
            get => MeshObject?.NormalRecalcExcludeList;
            set
            {
                if (MeshObject != null)
                    MeshObject.NormalRecalcExcludeList = value ?? new List<PartsSelectionSet>();
            }
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
        /// <summary>
        /// ミラー側の形状が、実体側の頂点を素直に鏡像化して「生成された」ものか。
        ///
        /// true のとき v_M = S·v_R が成り立ち、実効ワールドは S·H·S で求まる
        /// （ComputeWorldMatrices が適用する）。姿勢は実体側と共有する。
        ///
        /// false のミラー（PMX のように、ファイル内に実在する独立メッシュを
        /// MirrorSide/BakedMirror に再タイプしただけのもの）は自前の正しい頂点を
        /// 持つため、共役を掛けてはならない。
        /// </summary>
        public bool MirrorGeometryDerived { get; set; } = false;

        /// <summary>
        /// ミラーを解消したときに切り離したミラー側の ObjectId（0 = なし）。
        ///
        /// PMX 系（MirrorGeometryDerived = false）のミラー側はボーンウェイトなど
        /// 実体側から復元できない情報を持つため、解消時に破棄せず独立メッシュとして残す。
        /// 再びミラー化するときに相手を引き当てるための参照がこれ。
        /// インデックスは並べ替え・追加・削除でずれるため、位置非依存の ObjectId を持つ。
        ///
        /// MQO 系（同 true）のミラー側は実体側から再生成できるので解消時に破棄する。
        /// この値は使わない。
        /// </summary>
        public ulong DetachedMirrorObjectId { get; set; } = 0;

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
        /// モーフ等の一時的な位置オーバーライド用バッファ。
        /// null = 無効（GPU へは MeshObject.Positions を使用）。
        /// 非null = GPU _positionBuffer への書き込みにこちらを優先する。
        /// Vertices[i].Position（頂点移動結果）は変更しないため競合しない。
        /// </summary>
        public Vector3[] WorkingPositions { get; set; } = null;

        /// <summary>
        /// ★★★ PMXインポート時のモデル空間でのローカル軸回転（ワールド累積） ★★★
        /// VMDモーション適用時にローカル軸空間変換 (R⁻¹ * Q * R) で使用する。
        /// BoneTransform.RotationQuaternionは親からの相対回転であり、
        /// VMD変換にはこのワールド空間での累積回転が必要。削除禁止。
        /// </summary>
        public Quaternion BoneModelRotation { get; set; } = Quaternion.identity;

        // ================================================================
        // IKデータ（MeshObject.IKData へ委譲）
        // ================================================================
        //
        // 【設計方針】
        //   IKデータの実体は MeshObject.IKData（純POCO）に統一した。
        //   MeshContext は後方互換のため薄い委譲プロパティのみを公開する
        //   （Type / BoneTransform / ParentIndex と同一パターン）。
        //   これにより既存の全呼び出し元（PMX/MQO/CSV/Remote/CCDIK 等）は
        //   無改修のまま、データ実体だけが MeshObject 側へ移動する。
        //
        // 【不変条件】
        //   MeshObject.IKData != null  ⇔  このボーンはIKボーン。
        //   非IKボーンでは IKData を生成しない（意味とメモリの明確化）。
        //
        // 【遅延生成の安全性】
        //   全インポータ/デシリアライザは IsIK を true にしてからスカラーを
        //   設定する。さらに各スカラー setter は「デフォルト値かつ未生成」の
        //   場合に生成をスキップするため、非IKボーンに IKData が漏れ生成され
        //   ない（Remote の全ボーン一括読み込みでもデフォルト値のため安全）。
        // ----------------------------------------------------------------

        /// <summary>IKDataを遅延生成（MeshObjectが存在する場合のみ）。</summary>
        private void EnsureIKData()
        {
            if (MeshObject != null && MeshObject.IKData == null)
                MeshObject.IKData = new IKData();
        }

        /// <summary>このボーンがIKボーンか（MeshObject.IKData へ委譲）。</summary>
        public bool IsIK
        {
            get => MeshObject?.IKData?.IsIK ?? false;
            set
            {
                // 非IK化要求で未生成なら、IKDataを作らずに済ませる
                if (!value && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.IsIK = value;
            }
        }

        /// <summary>IKターゲット（エフェクタ）のMeshContextListインデックス。</summary>
        public int IKTargetIndex
        {
            get => MeshObject?.IKData?.TargetIndex ?? -1;
            set
            {
                if (value == -1 && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.TargetIndex = value;
            }
        }

        /// <summary>IKループ回数。</summary>
        public int IKLoopCount
        {
            get => MeshObject?.IKData?.LoopCount ?? 0;
            set
            {
                if (value == 0 && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.LoopCount = value;
            }
        }

        /// <summary>IK1回あたりの制限角度（ラジアン）。</summary>
        public float IKLimitAngle
        {
            get => MeshObject?.IKData?.LimitAngle ?? 0f;
            set
            {
                if (value == 0f && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.LimitAngle = value;
            }
        }

        /// <summary>
        /// IKリンクチェーン（実体は MeshObject.IKData.Links）。
        /// getter は生成済みIKDataでは常に非null（空リスト）を返す。
        /// </summary>
        public List<IKLinkInfo> IKLinks
        {
            get => MeshObject?.IKData?.Links;
            set
            {
                if (value == null && (MeshObject == null || MeshObject.IKData == null)) return;
                EnsureIKData();
                if (MeshObject?.IKData != null) MeshObject.IKData.Links = value;
            }
        }

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

        // ================================================================
        // 頂点単位のローカル→ワールド変換（描画側と同一規則）
        // ================================================================
        //
        // WorldMatrix は「メッシュ 1 個につき行列 1 個」を前提にしている。
        // スキンドメッシュではこの前提が成立しない。GPU は頂点ごとに次の規則で
        // 行列を選ぶため、ツール・オーバーレイ側も同じ規則に従う必要がある。
        //
        //   UnifiedBufferManager_Build.cs:344-363
        //     BoneWeight あり → _boneIndices = 頂点の boneIndex（ボーンの context 索引）
        //     BoneWeight なし → _boneIndices = メッシュ自身の context 索引
        //   UnifiedCompute.compute:911-918
        //     skinMatrix = Σ _TransformMatrixBuffer[boneIds.k] * weights.k
        //   UnifiedBufferManager_Update.cs:1513-1515
        //     ボーン／スキンドメッシュ → SkinningMatrix、非スキンドメッシュ → WorldMatrix
        //
        // 規則の定義はこの 1 箇所だけに置く。ToolContext.ActiveVertexMatrix は
        // ここへ委譲する。
        // ================================================================

        // ================================================================
        // 【禁止事項】GPU 由来の座標を扱うときの拗らせ
        // ================================================================
        // 以下は実際に発生させた失敗である。繰り返さないこと。
        //
        // 1. 調べずに CPU 側で独自計算しない。
        //    GPU が _worldPositionBuffer にワールド座標を出しているのに、
        //    同じ規則を CPU で書き直すと、規則が食い違ったときに表示だけがずれる。
        //    まず GPU の値を使う経路を探すこと。
        //
        // 2.「今は呼ばれていないからできない」と決めつけない。
        //    呼び出し箇所が無いことは、呼び出しを足せない理由にならない。
        //    足せるかどうかを調べてから結論を出すこと。
        //
        // 3. カメラもモデルも動いていないのに読み戻しを毎フレーム呼ばない。
        //    WritebackTransformedVertices / GetWorldPositions は同期 GetData を伴う。
        //    ワールド座標が変わる契機（頂点移動・ボーン移動・再構築）でのみ更新し、
        //    ホバーのようにトポロジ・視点・頂点位置のいずれも変わらない操作では呼ばない。
        // ================================================================

        // 【このメソッドは上記 1 に該当する CPU 独自計算である】
        // GPU が _worldPositionBuffer に出した値を使う経路
        // （UnifiedBufferManager.GetWorldPositions + LocalToGlobalVertexIndex）へ
        // 置き換えるべき対象。新規の呼び出しを増やさないこと。

        /// <summary>
        /// 指定頂点に GPU が実際に適用する変換行列を返す。
        /// BoneWeight を持たない頂点、および解決できない場合は WorldMatrix を返す。
        /// </summary>
        public Matrix4x4 VertexMatrix(int vertexIndex)
        {
            var mo = MeshObject;
            if (mo == null || vertexIndex < 0 || vertexIndex >= mo.Vertices.Count)
                return WorldMatrix;

            var vtx = mo.Vertices[vertexIndex];
            if (vtx == null || !vtx.HasBoneWeight)
                return WorldMatrix;

            var list = ParentModelContext?.MeshContextList;
            if (list == null || list.Count == 0)
                return WorldMatrix;

            var bw = vtx.BoneWeight.Value;
            Matrix4x4 acc = new Matrix4x4();
            float total = 0f;

            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex0, bw.weight0);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex1, bw.weight1);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex2, bw.weight2);
            total += AccumulateBoneMatrix(ref acc, list, bw.boneIndex3, bw.weight3);

            if (total <= 0f)
                return WorldMatrix;

            return acc;
        }

        /// <summary>
        /// acc に list[boneIndex].SkinningMatrix を weight 倍して加算する。
        /// 加算できたときだけ weight を返す（範囲外・weight 0 は 0）。
        /// </summary>
        private static float AccumulateBoneMatrix(
            ref Matrix4x4 acc, List<MeshContext> list, int boneIndex, float weight)
        {
            if (weight == 0f) return 0f;
            if (boneIndex < 0 || boneIndex >= list.Count) return 0f;

            var boneCtx = list[boneIndex];
            if (boneCtx == null) return 0f;

            Matrix4x4 m = boneCtx.SkinningMatrix;
            acc.m00 += m.m00 * weight; acc.m01 += m.m01 * weight; acc.m02 += m.m02 * weight; acc.m03 += m.m03 * weight;
            acc.m10 += m.m10 * weight; acc.m11 += m.m11 * weight; acc.m12 += m.m12 * weight; acc.m13 += m.m13 * weight;
            acc.m20 += m.m20 * weight; acc.m21 += m.m21 * weight; acc.m22 += m.m22 * weight; acc.m23 += m.m23 * weight;
            acc.m30 += m.m30 * weight; acc.m31 += m.m31 * weight; acc.m32 += m.m32 * weight; acc.m33 += m.m33 * weight;
            return weight;
        }

        /// <summary>ローカル座標をワールド座標に変換（頂点単位・描画側と同一規則）</summary>
        public Vector3 LocalToWorld(int vertexIndex, Vector3 localPos)
        {
            return VertexMatrix(vertexIndex).MultiplyPoint3x4(localPos);
        }

        /// <summary>ワールド座標をローカル座標に変換（頂点単位・描画側と同一規則）</summary>
        public Vector3 WorldToLocal(int vertexIndex, Vector3 worldPos)
        {
            return VertexMatrix(vertexIndex).inverse.MultiplyPoint3x4(worldPos);
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

        // ================================================================
        // 協働編集（グループワーク）用の識別と担当
        // ----------------------------------------------------------------
        // ObjectId  : リスト内の位置（MasterIndex）に依存しない安定識別子。
        //             追加・削除・並べ替えを跨いで同一オブジェクトを指す。
        //             0 は「未割当」。ObjectIdAllocator.EnsureIds で遅延割当される。
        //             複製（Duplicate）は別オブジェクトなので新IDを振る。
        //             Undo/Redo のスナップショット復元は同一オブジェクトなのでIDを保つ。
        // EditorName: 現在の編集者名。空文字は「担当者なし（誰でも編集可）」。
        //             サーバはこの値と register 済みユーザー名を突き合わせて
        //             リモートコマンドの可否を判定する（RemoteOwnership）。
        //             手動 claim / release のみで変化し、切断では解放しない。
        //             プロジェクト保存に含まれる。
        // ================================================================

        /// <summary>位置非依存の安定オブジェクトID（0=未割当）</summary>
        public ulong ObjectId { get; set; } = 0;

        /// <summary>現在の編集者名（空文字＝担当者なし）</summary>
        public string EditorName { get; set; } = "";

        /// <summary>編集者が設定されているか</summary>
        public bool HasEditor => !string.IsNullOrEmpty(EditorName);

        /// <summary>
        /// 指定ユーザーがこのオブジェクトを編集できるか。
        /// 担当者なし、または自分が担当者のときに true。
        /// </summary>
        public bool IsEditableBy(string userName)
        {
            if (string.IsNullOrEmpty(EditorName)) return true;
            return string.Equals(EditorName, userName, StringComparison.Ordinal);
        }

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
        /// モーフに関わるメッシュか。
        /// true のとき法線再計算でUVスロットを増やしてはならない。
        /// 親子で UVs.Count が食い違うと展開index空間がずれ、
        /// PMXエクスポート時のモーフ頂点参照が崩れるため。
        /// </summary>
        public bool IsMorphRelated(Poly_Ling.Context.ModelContext model)
        {
            if (IsMorph) return true;
            if (Type == MeshType.Morph) return true;
            if (model == null) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || ReferenceEquals(mc, this)) continue;
                if (mc.MorphParentIndex < 0) continue;
                if (ReferenceEquals(model.GetMeshContext(mc.MorphParentIndex), this)) return true;
            }
            return false;
        }

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

        /// <summary>PMX材質名リスト（PMXインポート時に設定、空メッシュエクスポート用）</summary>
        public List<string> PMXMaterialNames { get; set; } = new List<string>();

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

        /// <summary>Memo欄のIsMirrorフラグ由来のミラーか（PMXインポート時に設定）</summary>
        public bool IsMirrorFromMemo { get; set; } = false;

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
        public Poly_Ling.Context.ModelContext ParentModelContext { get; set; }

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

            // 生Unity Mesh 操作は MeshBridge に集約（一時Mesh生成・コピー・破棄を内包）。
            PLMeshBridge.I.RebuildMeshInPlace(UnityMesh, MeshObject);
        }

        /// <summary>
        /// 頂点位置のみをUnityMeshに適用（高速）
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (UnityMesh == null || MeshObject == null) return;

            PLMeshBridge.I.ApplyVertexPositionsInPlace(UnityMesh, MeshObject);
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

    // IKLinkInfo は IKData.cs へ移設済み（namespace Poly_Ling.Data 不変のため
    // 既存参照は無改修で解決される）。
}
