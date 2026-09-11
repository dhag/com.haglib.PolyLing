// Assets/Editor/Poly_Ling/Data/MeshContext.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshObject（Vertex/Face）ベース対応版
// DefaultMaterials対応版
// Phase 1: 選択状態をMeshContextに統合
// Phase Morph: モーフ基準データ対応
// Phase BonePose: BonePoseData対応（BindPose相互変換）
//
// 【分割先】このファイルから次へ分けてある。
//   MeshContext.Attributes.cs  MeshContext：オブジェクト属性・協働編集・モーフ基準とミラー適用・エクスポート制御・ミラー設定と
//   MeshContext.Transform.cs   MeshContext：階層・トランスフォーム・変換行列・IK データ・頂点単位のローカル→ワールド変換。
//   MeshSelectionSnapshot.cs   MeshContext 用の選択スナップショット（MeshContext.cs から分離）。
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
    public partial class MeshContext
    {
        /// <summary>
        /// 名前。実体は MeshObject.Name。
        ///
        /// 【MeshObject が未設定のときの代入を捨てない】
        ///   以前は setter が「MeshObject が null なら何もしない」だったため、
        ///     new MeshContext { Name = ..., MeshObject = ... }
        ///   と書くと（オブジェクト初期化子は書いた順に走るので）名前の代入が
        ///   黙って捨てられていた。この書き方はコード内に 16 箇所あり、
        ///   図形生成の一意名も捨てられて同名の描画オブジェクトが並んでいた
        ///   （PolyLingPlayerViewerCore.BuildPrimitiveMeshContext）。
        ///   呼び出し側の順序に依存させないため、未設定のときは保留し、
        ///   MeshObject が入った時点で反映する。
        /// </summary>
        public string Name
        {
            get => MeshObject?.Name ?? (_pendingName ?? "Untitled");
            set
            {
                if (MeshObject != null) MeshObject.Name = value;
                else                    _pendingName = value;
            }
        }

        /// <summary>MeshObject 未設定のときに受けた名前。MeshObject 代入時に流し込む。</summary>
        private string _pendingName;

        public Mesh UnityMesh;                      // Unity UnityMesh（表示用）

        /// <summary>
        /// メッシュオブジェクト。
        ///
        /// Name / Type / Depth / ParentIndex / HierarchyParentIndex / BoneTransform /
        /// SkinKind / MirrorBoneIndex / IgnorePoseInArmature / PreserveNormals /
        /// NormalRecalcExcludeList / IsMirrorBranchRoot はこちらへ委譲している。
        /// 代入の時点で保留していた名前を流し込む（上の Name の注記を参照）。
        /// </summary>
        public MeshObject MeshObject
        {
            get => _meshObject;
            set
            {
                _meshObject = value;
                if (_meshObject != null && _pendingName != null)
                {
                    _meshObject.Name = _pendingName;
                    _pendingName = null;
                }
            }
        }

        private MeshObject _meshObject;

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
    }

    // IKLinkInfo は IKData.cs へ移設済み（namespace Poly_Ling.Data 不変のため
    // 既存参照は無改修で解決される）。
}
