// Assets/Editor/UndoSystem/MeshEditor/MeshUndoRecords.cs
// メッシュエディタ専用のUndo記録クラス群
// MeshData（Vertex/Face）ベースに対応
// Phase6: マルチマテリアル対応版
// DefaultMaterials対応版

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Selection;

namespace MeshFactory.UndoSystem
{
    // ============================================================
    // コンテキスト
    // ============================================================

    /// <summary>
    /// メッシュ編集コンテキスト
    /// Undo/Redo操作の対象となるデータを保持
    /// MeshDataベースの新構造
    /// </summary>
    public class MeshEditContext
    {
        // === メッシュデータ（新構造） ===

        /// <summary>メッシュデータ本体</summary>
        public MeshData MeshData;

        // === 選択状態 ===

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> SelectedVertices;

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> SelectedFaces;

        // === 元データ参照（リセット用） ===

        /// <summary>元の頂点位置（リセット用）</summary>
        public Vector3[] OriginalPositions;

        // === Unity UnityMesh 参照 ===

        /// <summary>実際のMeshオブジェクト</summary>
        public Mesh TargetMesh;

        // === WorkPlane参照（選択連動Undo用） ===

        /// <summary>WorkPlane参照（選択と連動してUndo/Redoするため）</summary>
        public WorkPlane WorkPlane;

        // === 拡張選択システム用 ===

        /// <summary>Undo/Redo時に復元すべきSelectionSnapshot</summary>
        public SelectionSnapshot CurrentSelectionSnapshot;

        // === マテリアル（マルチマテリアル対応） ===

        /// <summary>マテリアルリスト（MeshContext.Materialsと同期）</summary>
        public List<Material> Materials = new List<Material> { null };

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int CurrentMaterialIndex = 0;

        // === デフォルトマテリアル（グローバル設定） ===

        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアルリスト</summary>
        public List<Material> DefaultMaterials = new List<Material> { null };

        /// <summary>新規メッシュ作成時に適用されるデフォルトカレントマテリアルインデックス</summary>
        public int DefaultCurrentMaterialIndex = 0;

        /// <summary>マテリアル変更時に自動でデフォルトに設定するか</summary>
        public bool AutoSetDefaultMaterials = true;

        // === 後方互換プロパティ ===

        /// <summary>頂点位置リスト（後方互換）</summary>
        public List<Vector3> Vertices
        {
            get => MeshData?.Vertices.Select(v => v.Position).ToList() ?? new List<Vector3>();
            set
            {
                if (MeshData == null) return;
                for (int i = 0; i < value.Count && i < MeshData.Vertices.Count; i++)
                {
                    MeshData.Vertices[i].Position = value[i];
                }
            }
        }

        /// <summary>元の頂点位置（後方互換）</summary>
        public Vector3[] OriginalVertices
        {
            get => OriginalPositions;
            set => OriginalPositions = value;
        }

        // === コンストラクタ ===

        public MeshEditContext()
        {
            MeshData = new MeshData();
            SelectedVertices = new HashSet<int>();
            SelectedFaces = new HashSet<int>();
            Materials = new List<Material> { null };
            CurrentMaterialIndex = 0;
            DefaultMaterials = new List<Material> { null };
            DefaultCurrentMaterialIndex = 0;
            AutoSetDefaultMaterials = true;
        }

        // === メッシュ読み込み/適用 ===

        /// <summary>
        /// Meshからデータを読み込む
        /// </summary>
        public void LoadFromMesh(Mesh mesh, bool mergeVertices = true)
        {
            if (mesh == null) return;

            TargetMesh = mesh;
            MeshData = new MeshData();
            MeshData.FromUnityMesh(mesh, mergeVertices);

            // 元の位置を保存
            OriginalPositions = MeshData.Vertices.Select(v => v.Position).ToArray();

            // 選択クリア
            SelectedVertices.Clear();
            SelectedFaces.Clear();
        }

        /// <summary>
        /// データをMeshに適用
        /// </summary>
        public void ApplyToMesh()
        {
            if (TargetMesh == null || MeshData == null) return;

            // MeshDataをUnity Meshに変換して適用
            var newMesh = MeshData.ToUnityMesh();

            TargetMesh.Clear();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.triangles = newMesh.triangles;
            TargetMesh.uv = newMesh.uv;
            TargetMesh.normals = newMesh.normals;
            TargetMesh.RecalculateBounds();

            // 一時メッシュを破棄
            Object.DestroyImmediate(newMesh);
        }

        /// <summary>
        /// 頂点位置のみをMeshに適用（高速）
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (TargetMesh == null || MeshData == null) return;

            // Unity Meshに変換して頂点位置を更新
            var newMesh = MeshData.ToUnityMesh();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.RecalculateNormals();
            TargetMesh.RecalculateBounds();

            Object.DestroyImmediate(newMesh);
        }

        // === 頂点操作ヘルパー ===

        /// <summary>
        /// 頂点位置を取得
        /// </summary>
        public Vector3 GetVertexPosition(int index)
        {
            if (MeshData == null || index < 0 || index >= MeshData.VertexCount)
                return Vector3.zero;
            return MeshData.Vertices[index].Position;
        }

        /// <summary>
        /// 頂点位置を設定
        /// </summary>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (MeshData == null || index < 0 || index >= MeshData.VertexCount)
                return;
            MeshData.Vertices[index].Position = position;
        }

        /// <summary>
        /// 全頂点位置を配列で取得
        /// </summary>
        public Vector3[] GetAllPositions()
        {
            if (MeshData == null) return new Vector3[0];
            return MeshData.Vertices.Select(v => v.Position).ToArray();
        }

        /// <summary>
        /// 全頂点位置を配列で設定
        /// </summary>
        public void SetAllPositions(Vector3[] positions)
        {
            if (MeshData == null) return;
            for (int i = 0; i < positions.Length && i < MeshData.VertexCount; i++)
            {
                MeshData.Vertices[i].Position = positions[i];
            }
        }

        /// <summary>
        /// 頂点数を取得
        /// </summary>
        public int VertexCount => MeshData?.VertexCount ?? 0;

        /// <summary>
        /// 面数を取得
        /// </summary>
        public int FaceCount => MeshData?.FaceCount ?? 0;
    }

    // ============================================================
    // Undo記録の基底
    // ============================================================

    /// <summary>
    /// メッシュ編集用Undo記録の基底クラス
    /// </summary>
    public abstract class MeshUndoRecord : IUndoRecord<MeshEditContext>
    {
        public UndoOperationInfo Info { get; set; }
        public abstract void Undo(MeshEditContext context);
        public abstract void Redo(MeshEditContext context);
    }

    // ============================================================
    // 具体的なUndo記録クラス
    // ============================================================

    /// <summary>
    /// 頂点移動記録（軽量）
    /// Vertexインデックスと位置のみを保存
    /// </summary>
    public class VertexMoveRecord : MeshUndoRecord
    {
        public int[] Indices;
        public Vector3[] OldPositions;
        public Vector3[] NewPositions;

        public VertexMoveRecord(int[] indices, Vector3[] oldPositions, Vector3[] newPositions)
        {
            Indices = indices;
            OldPositions = oldPositions;
            NewPositions = newPositions;
        }

        public override void Undo(MeshEditContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
            {
                ctx.SetVertexPosition(Indices[i], OldPositions[i]);
            }
            ctx.ApplyVertexPositionsToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            for (int i = 0; i < Indices.Length; i++)
            {
                ctx.SetVertexPosition(Indices[i], NewPositions[i]);
            }
            ctx.ApplyVertexPositionsToMesh();
        }
    }

    /// <summary>
    /// 頂点グループ移動記録（グループ化された頂点用）
    /// 新構造ではVertexがグループに相当
    /// </summary>
    public class VertexGroupMoveRecord : MeshUndoRecord
    {
        public List<int>[] Groups;  // グループごとの頂点インデックス
        public Vector3[] OldOffsets;
        public Vector3[] NewOffsets;
        public Vector3[] OriginalPositions;  // 元の頂点位置

        public VertexGroupMoveRecord(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalPositions)
        {
            Groups = groups;
            OldOffsets = oldOffsets;
            NewOffsets = newOffsets;
            OriginalPositions = originalPositions;
        }

        public override void Undo(MeshEditContext ctx)
        {
            ApplyOffsets(ctx, OldOffsets);
        }

        public override void Redo(MeshEditContext ctx)
        {
            ApplyOffsets(ctx, NewOffsets);
        }

        private void ApplyOffsets(MeshEditContext ctx, Vector3[] offsets)
        {
            for (int g = 0; g < Groups.Length; g++)
            {
                foreach (int vi in Groups[g])
                {
                    if (vi < ctx.VertexCount && vi < OriginalPositions.Length)
                    {
                        ctx.SetVertexPosition(vi, OriginalPositions[vi] + offsets[g]);
                    }
                }
            }
            ctx.ApplyVertexPositionsToMesh();
        }
    }

    /// <summary>
    /// 選択状態変更記録（WorkPlane原点連動対応）
    /// </summary>
    public class SelectionChangeRecord : MeshUndoRecord
    {
        public HashSet<int> OldSelectedVertices;
        public HashSet<int> NewSelectedVertices;
        public HashSet<int> OldSelectedFaces;
        public HashSet<int> NewSelectedFaces;

        // WorkPlane連動（AutoUpdate有効時のみ使用）
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        public SelectionChangeRecord(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            OldSelectedVertices = new HashSet<int>(oldVertices ?? new HashSet<int>());
            NewSelectedVertices = new HashSet<int>(newVertices ?? new HashSet<int>());
            OldSelectedFaces = new HashSet<int>(oldFaces ?? new HashSet<int>());
            NewSelectedFaces = new HashSet<int>(newFaces ?? new HashSet<int>());
            OldWorkPlaneSnapshot = null;
            NewWorkPlaneSnapshot = null;
        }

        /// <summary>
        /// WorkPlane連動付きコンストラクタ
        /// </summary>
        public SelectionChangeRecord(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
            : this(oldVertices, newVertices, oldFaces, newFaces)
        {
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public override void Undo(MeshEditContext ctx)
        {
            ctx.SelectedVertices = new HashSet<int>(OldSelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(OldSelectedFaces);

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public override void Redo(MeshEditContext ctx)
        {
            ctx.SelectedVertices = new HashSet<int>(NewSelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(NewSelectedFaces);

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }

    /// <summary>
    /// メッシュ全体のスナップショット記録（トポロジー変更用）
    /// MeshDataベースの新構造対応
    /// 
    /// SelectionState参照を保持し、Edge/Line選択もUndo対象に
    /// </summary>
    public class MeshSnapshotRecord : MeshUndoRecord
    {
        public MeshDataSnapshot Before;
        public MeshDataSnapshot After;

        // ================================================================
        // 拡張選択復元用のSelectionState参照
        // ================================================================
        //
        // 【目的】
        // Undo/Redo時にExtendedSelection（Edge/Line選択）を復元するため
        // 
        // 【注意】
        // - SimpleMeshFactoryの_selectionStateと同じインスタンスを指す
        // - SimpleMeshFactoryが破棄されると無効になるが、
        //   通常はエディタウィンドウと同じライフサイクル
        // - nullの場合は従来動作（Edge/Line選択は復元されない）
        //
        private SelectionState _selectionStateRef;

        /// <summary>
        /// コンストラクタ（従来版・後方互換）
        /// </summary>
        public MeshSnapshotRecord(MeshDataSnapshot before, MeshDataSnapshot after)
        {
            Before = before;
            After = after;
            _selectionStateRef = null;
        }

        /// <summary>
        /// コンストラクタ（拡張選択対応版）
        /// 
        /// 【フェーズ1追加】
        /// </summary>
        /// <param name="before">変更前スナップショット</param>
        /// <param name="after">変更後スナップショット</param>
        /// <param name="selectionState">
        /// 拡張選択状態への参照。Undo/Redo時の復元先。
        /// トポロジー変更ツール（ベベル、押し出し等）は必ずこれを渡すこと。
        /// </param>
        public MeshSnapshotRecord(
            MeshDataSnapshot before,
            MeshDataSnapshot after,
            SelectionState selectionState)
        {
            Before = before;
            After = after;
            _selectionStateRef = selectionState;
        }

        public override void Undo(MeshEditContext ctx)
        {
            // 【フェーズ1変更】拡張選択対応版ApplyToを呼び出し
            Before?.ApplyTo(ctx, _selectionStateRef);
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            // 【フェーズ1変更】拡張選択対応版ApplyToを呼び出し
            After?.ApplyTo(ctx, _selectionStateRef);
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// MeshDataのスナップショット
    /// マルチマテリアル対応版
    /// </summary>
    /// <summary>
    /// MeshDataのスナップショット
    /// マルチマテリアル対応版
    /// 
    /// 【フェーズ1】ExtendedSelection対応
    /// トポロジー変更時にEdge/Line選択も保存・復元可能に
    /// </summary>
    public class MeshDataSnapshot
    {
        public MeshData MeshData;
        public HashSet<int> SelectedVertices;
        public HashSet<int> SelectedFaces;

        // マテリアル（マルチマテリアル対応）
        public List<Material> Materials;
        public int CurrentMaterialIndex;

        // デフォルトマテリアル
        public List<Material> DefaultMaterials;
        public int DefaultCurrentMaterialIndex;
        public bool AutoSetDefaultMaterials;

        // ================================================================
        // 【フェーズ1追加】拡張選択状態（Edge/Line含む）
        // ================================================================
        //
        // 【目的】
        // トポロジー変更（ベベル、押し出し等）時に、Edge/Line選択も
        // 一緒にUndo/Redoされるようにする。
        //
        // 【背景】
        // 従来、SelectedVertices/SelectedFacesのみ保存していたため、
        // Edge選択（HashSet<VertexPair>）やLine選択（HashSet<int>）は
        // Undoで復元されなかった。
        //
        // 【SelectionSnapshotの内容】（SelectionState.csで定義）
        // - Mode: MeshSelectMode（フラグ）
        // - Vertices: HashSet<int>
        // - Edges: HashSet<VertexPair>  ← 新たに保存対象
        // - Faces: HashSet<int>
        // - Lines: HashSet<int>         ← 新たに保存対象
        //
        public SelectionSnapshot ExtendedSelection;

        /// <summary>
        /// コンテキストからスナップショットを作成（従来版・後方互換）
        /// </summary>
        public static MeshDataSnapshot Capture(MeshEditContext ctx)
        {
            return new MeshDataSnapshot
            {
                MeshData = ctx.MeshData?.Clone(),
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(ctx.SelectedFaces),
                Materials = ctx.Materials != null ? new List<Material>(ctx.Materials) : new List<Material> { null },
                CurrentMaterialIndex = ctx.CurrentMaterialIndex,
                DefaultMaterials = ctx.DefaultMaterials != null ? new List<Material>(ctx.DefaultMaterials) : new List<Material> { null },
                DefaultCurrentMaterialIndex = ctx.DefaultCurrentMaterialIndex,
                AutoSetDefaultMaterials = ctx.AutoSetDefaultMaterials
                // ExtendedSelection は null（後方互換）
            };
        }

        /// <summary>
        /// コンテキストからスナップショットを作成（拡張選択対応版）
        /// 
        /// </summary>
        /// <param name="ctx">メッシュ編集コンテキスト</param>
        /// <param name="selectionState">
        /// 拡張選択状態（Edge/Line含む）。
        /// 
        /// 【重要】トポロジー変更ツールは必ずこれを渡すこと！
        /// - EdgeBevelTool
        /// - EdgeExtrudeTool  
        /// - FaceExtrudeTool
        /// - KnifeTool
        /// - AddFaceTool
        /// 等
        /// 
        /// nullを渡すと従来通りの動作（Edge/Line選択は保存されない）
        /// </param>
        /// <returns>スナップショット</returns>
        public static MeshDataSnapshot Capture(MeshEditContext ctx, SelectionState selectionState)
        {
            var snapshot = new MeshDataSnapshot
            {
                MeshData = ctx.MeshData?.Clone(),
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(ctx.SelectedFaces),
                Materials = ctx.Materials != null ? new List<Material>(ctx.Materials) : new List<Material> { null },
                CurrentMaterialIndex = ctx.CurrentMaterialIndex,
                DefaultMaterials = ctx.DefaultMaterials != null ? new List<Material>(ctx.DefaultMaterials) : new List<Material> { null },
                DefaultCurrentMaterialIndex = ctx.DefaultCurrentMaterialIndex,
                AutoSetDefaultMaterials = ctx.AutoSetDefaultMaterials
            };

            // 【フェーズ1追加】拡張選択を保存
            // SelectionStateが渡された場合のみ保存。
            // これによりEdge/Line選択もUndo対象になる。
            if (selectionState != null)
            {
                snapshot.ExtendedSelection = selectionState.CreateSnapshot();
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットをコンテキストに適用（従来版・後方互換）
        /// </summary>
        public void ApplyTo(MeshEditContext ctx)
        {
            ctx.MeshData = MeshData?.Clone();
            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(SelectedFaces);

            // マテリアル復元
            if (Materials != null)
            {
                ctx.Materials = new List<Material>(Materials);
                ctx.CurrentMaterialIndex = CurrentMaterialIndex;
            }

            // デフォルトマテリアル復元
            if (DefaultMaterials != null)
            {
                ctx.DefaultMaterials = new List<Material>(DefaultMaterials);
            }
            ctx.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
            ctx.AutoSetDefaultMaterials = AutoSetDefaultMaterials;

            // ExtendedSelection は復元しない（後方互換）
        }

        /// <summary>
        /// スナップショットをコンテキストに適用（拡張選択対応版）
        /// 
        /// 【フェーズ1追加】
        /// </summary>
        /// <param name="ctx">メッシュ編集コンテキスト</param>
        /// <param name="selectionState">
        /// 拡張選択状態の復元先。
        /// 
        /// 【重要】SimpleMeshFactory側で_selectionStateを渡すこと！
        /// Undo/Redo実行時にEdge/Line選択を正しく復元するために必要。
        /// 
        /// nullを渡すと拡張選択は復元されない（従来動作）
        /// </param>
        public void ApplyTo(MeshEditContext ctx, SelectionState selectionState)
        {
            // === 既存処理（メッシュデータ、レガシー選択、マテリアル） ===
            ctx.MeshData = MeshData?.Clone();
            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(SelectedFaces);

            if (Materials != null)
            {
                ctx.Materials = new List<Material>(Materials);
                ctx.CurrentMaterialIndex = CurrentMaterialIndex;
            }

            if (DefaultMaterials != null)
            {
                ctx.DefaultMaterials = new List<Material>(DefaultMaterials);
            }
            ctx.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
            ctx.AutoSetDefaultMaterials = AutoSetDefaultMaterials;

            // ================================================================
            // 【フェーズ1追加】拡張選択の復元
            // ================================================================
            //
            // ExtendedSelectionが保存されている場合のみ復元。
            // これにより：
            // - ベベルUndo時 → 元のEdge選択が復元
            // - 押し出しUndo時 → 元のFace選択が復元
            // 等が可能になる。
            //
            // 【注意】
            // - ExtendedSelectionがnull（従来のスナップショット）の場合はスキップ
            // - selectionStateがnullの場合もスキップ（呼び出し側の責任）
            //
            if (ExtendedSelection != null && selectionState != null)
            {
                selectionState.RestoreFromSnapshot(ExtendedSelection);
            }
        }
    }

    // ============================================================
    // 後方互換: MeshSnapshot（旧形式）
    // ============================================================

    /// <summary>
    /// メッシュ状態のスナップショット（後方互換）
    /// </summary>
    public class MeshSnapshot
    {
        public Vector3[] Vertices;
        public int[] Triangles;
        public Vector2[] UVs;
        public Vector3[] Normals;
        public HashSet<int> SelectedVertices;
        public HashSet<int> SelectedFaces;

        /// <summary>
        /// コンテキストからスナップショットを作成
        /// </summary>
        public static MeshSnapshot Capture(MeshEditContext ctx)
        {
            // MeshDataから Unity 形式のデータを取得
            if (ctx.MeshData == null)
            {
                return new MeshSnapshot
                {
                    Vertices = new Vector3[0],
                    Triangles = new int[0],
                    UVs = new Vector2[0],
                    Normals = new Vector3[0],
                    SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                    SelectedFaces = new HashSet<int>(ctx.SelectedFaces)
                };
            }

            var mesh = ctx.MeshData.ToUnityMesh();
            var snapshot = new MeshSnapshot
            {
                Vertices = mesh.vertices,
                Triangles = mesh.triangles,
                UVs = mesh.uv,
                Normals = mesh.normals,
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(ctx.SelectedFaces)
            };
            Object.DestroyImmediate(mesh);
            return snapshot;
        }

        /// <summary>
        /// スナップショットをコンテキストに適用（後方互換用）
        /// </summary>
        public void ApplyTo(MeshEditContext ctx)
        {
            // 一時的なMeshを作成してMeshDataに変換
            var tempMesh = new Mesh
            {
                vertices = Vertices,
                triangles = Triangles,
                uv = UVs,
                normals = Normals
            };

            ctx.MeshData = new MeshData();
            ctx.MeshData.FromUnityMesh(tempMesh, true);
            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);
            ctx.SelectedFaces = new HashSet<int>(SelectedFaces);

            Object.DestroyImmediate(tempMesh);
        }
    }

    // ============================================================
    // 頂点UV/法線変更記録
    // ============================================================

    /// <summary>
    /// 頂点UV変更記録
    /// </summary>
    public class VertexUVChangeRecord : MeshUndoRecord
    {
        public int VertexIndex;
        public int UVIndex;
        public Vector2 OldUV;
        public Vector2 NewUV;

        public VertexUVChangeRecord(int vertexIndex, int uvIndex, Vector2 oldUV, Vector2 newUV)
        {
            VertexIndex = vertexIndex;
            UVIndex = uvIndex;
            OldUV = oldUV;
            NewUV = newUV;
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null && VertexIndex < ctx.MeshData.VertexCount)
            {
                var vertex = ctx.MeshData.Vertices[VertexIndex];
                if (UVIndex < vertex.UVs.Count)
                    vertex.UVs[UVIndex] = OldUV;
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null && VertexIndex < ctx.MeshData.VertexCount)
            {
                var vertex = ctx.MeshData.Vertices[VertexIndex];
                if (UVIndex < vertex.UVs.Count)
                    vertex.UVs[UVIndex] = NewUV;
            }
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// 面追加記録
    /// </summary>
    public class FaceAddRecord : MeshUndoRecord
    {
        public Face AddedFace;
        public int FaceIndex;

        public FaceAddRecord(Face face, int index)
        {
            AddedFace = face.Clone();
            FaceIndex = index;
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null && FaceIndex < ctx.MeshData.FaceCount)
            {
                ctx.MeshData.Faces.RemoveAt(FaceIndex);
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null)
            {
                ctx.MeshData.Faces.Insert(FaceIndex, AddedFace.Clone());
            }
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// 面削除記録
    /// </summary>
    public class FaceDeleteRecord : MeshUndoRecord
    {
        public Face DeletedFace;
        public int FaceIndex;

        public FaceDeleteRecord(Face face, int index)
        {
            DeletedFace = face.Clone();
            FaceIndex = index;
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null)
            {
                ctx.MeshData.Faces.Insert(FaceIndex, DeletedFace.Clone());
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null && FaceIndex < ctx.MeshData.FaceCount)
            {
                ctx.MeshData.Faces.RemoveAt(FaceIndex);
            }
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// 頂点追加記録
    /// </summary>
    public class VertexAddRecord : MeshUndoRecord
    {
        public Vertex AddedVertex;
        public int VertexIndex;

        public VertexAddRecord(Vertex vertex, int index)
        {
            AddedVertex = vertex.Clone();
            VertexIndex = index;
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null && VertexIndex < ctx.MeshData.VertexCount)
            {
                ctx.MeshData.Vertices.RemoveAt(VertexIndex);
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexRemoval(ctx.MeshData, VertexIndex);
            }
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData != null)
            {
                ctx.MeshData.Vertices.Insert(VertexIndex, AddedVertex.Clone());
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexInsertion(ctx.MeshData, VertexIndex);
            }
            ctx.ApplyToMesh();
        }

        private void AdjustFaceIndicesAfterVertexRemoval(MeshData meshData, int removedIndex)
        {
            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] > removedIndex)
                        face.VertexIndices[i]--;
                }
            }
        }

        private void AdjustFaceIndicesAfterVertexInsertion(MeshData meshData, int insertedIndex)
        {
            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] >= insertedIndex)
                        face.VertexIndices[i]++;
                }
            }
        }
    }

    // ============================================================
    // エディタ状態（カメラ、表示、モード統合）
    // ============================================================

    // ============================================================
    // エディタ状態（カメラ、表示、モード統合）
    // IToolSettings対応版
    // ============================================================

    /// <summary>
    /// エディタ状態コンテキスト（カメラ、表示設定、編集モード）
    /// </summary>
    public class EditorStateContext
    {
        // カメラ
        public float RotationX = 20f;
        public float RotationY = 0f;
        public float CameraDistance = 2f;
        public Vector3 CameraTarget = Vector3.zero;

        // 表示設定
        public bool ShowWireframe = true;
        public bool ShowVertices = true;

        // 編集モード
        public bool VertexEditMode = false;

        // 現在のツール
        public string CurrentToolName = "Select";

        // メッシュ作成設定
        public bool AddToCurrentMesh = true;
        public bool AutoMergeOnCreate = true;
        public float AutoMergeThreshold = 0.001f;

        // ツール設定
        //ナイフツールの固有設定（既存）
        //public KnifeProperty knifeProperty = new KnifeProperty();

        // 汎用ツール設定ストレージ（新規追加）
        public ToolSettingsStorage ToolSettings = new ToolSettingsStorage();

        // WorkPlane参照（カメラ連動Undo用）
        public WorkPlane WorkPlane;

        // === Foldout状態 ===
        /// <summary>Foldout開閉をUndo記録するか</summary>
        public bool RecordFoldoutChanges = false;
        
        /// <summary>Foldout状態辞書</summary>
        public Dictionary<string, bool> FoldoutStates = new Dictionary<string, bool>();
        
        /// <summary>Foldout状態を取得（未設定ならデフォルト値を返す）</summary>
        public bool GetFoldout(string key, bool defaultValue = true)
        {
            return FoldoutStates.TryGetValue(key, out var value) ? value : defaultValue;
        }
        
        /// <summary>Foldout状態を設定</summary>
        public void SetFoldout(string key, bool value)
        {
            FoldoutStates[key] = value;
        }

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public EditorStateSnapshot Capture()
        {
            var snapshot = new EditorStateSnapshot
            {
                RotationX = RotationX,
                RotationY = RotationY,
                CameraDistance = CameraDistance,
                CameraTarget = CameraTarget,
                ShowWireframe = ShowWireframe,
                ShowVertices = ShowVertices,
                VertexEditMode = VertexEditMode,
                CurrentToolName = CurrentToolName,
                AddToCurrentMesh = AddToCurrentMesh,
                AutoMergeOnCreate = AutoMergeOnCreate,
                AutoMergeThreshold = AutoMergeThreshold,
                RecordFoldoutChanges = RecordFoldoutChanges,
                FoldoutStates = new Dictionary<string, bool>(FoldoutStates),
            };
            /*
            // KnifeProperty（既存）
            snapshot.knifeProperty = new KnifeProperty();
            snapshot.knifeProperty.Mode = knifeProperty.Mode;
            snapshot.knifeProperty.EdgeSelect = knifeProperty.EdgeSelect;
            snapshot.knifeProperty.ChainMode = knifeProperty.ChainMode;
            */
            // ToolSettings（新規）
            snapshot.ToolSettings = ToolSettings?.Clone();

            return snapshot;
        }

        /// <summary>
        /// スナップショットから復元
        /// </summary>
        public void ApplySnapshot(EditorStateSnapshot snapshot)
        {
            RotationX = snapshot.RotationX;
            RotationY = snapshot.RotationY;
            CameraDistance = snapshot.CameraDistance;
            CameraTarget = snapshot.CameraTarget;
            ShowWireframe = snapshot.ShowWireframe;
            ShowVertices = snapshot.ShowVertices;
            VertexEditMode = snapshot.VertexEditMode;
            CurrentToolName = snapshot.CurrentToolName;
            AddToCurrentMesh = snapshot.AddToCurrentMesh;
            AutoMergeOnCreate = snapshot.AutoMergeOnCreate;
            AutoMergeThreshold = snapshot.AutoMergeThreshold;
            RecordFoldoutChanges = snapshot.RecordFoldoutChanges;
            
            // FoldoutStates復元
            FoldoutStates.Clear();
            if (snapshot.FoldoutStates != null)
            {
                foreach (var kvp in snapshot.FoldoutStates)
                {
                    FoldoutStates[kvp.Key] = kvp.Value;
                }
            }

            // KnifeProperty（既存）
            /*knifeProperty.Mode = snapshot.knifeProperty.Mode;
            knifeProperty.EdgeSelect = snapshot.knifeProperty.EdgeSelect;
            knifeProperty.ChainMode = snapshot.knifeProperty.ChainMode;
            */
            // ToolSettings（新規）
            if (snapshot.ToolSettings != null)
            {
                if (ToolSettings == null)
                    ToolSettings = new ToolSettingsStorage();
                ToolSettings.CopyFrom(snapshot.ToolSettings);
            }
        }
    }

    /// <summary>
    /// エディタ状態のスナップショット
    /// </summary>
    public struct EditorStateSnapshot
    {
        public float RotationX, RotationY, CameraDistance;
        public Vector3 CameraTarget;
        public bool ShowWireframe, ShowVertices, VertexEditMode;
        public string CurrentToolName;

        // メッシュ作成設定
        public bool AddToCurrentMesh;
        public bool AutoMergeOnCreate;
        public float AutoMergeThreshold;

        // ナイフツール設定（既存）
        //public KnifeProperty knifeProperty;

        // 汎用ツール設定（新規追加）
        public ToolSettingsStorage ToolSettings;
        
        // Foldout状態
        public bool RecordFoldoutChanges;
        public Dictionary<string, bool> FoldoutStates;

        public bool IsDifferentFrom(EditorStateSnapshot other)
        {
            // 基本プロパティ
            if (!Mathf.Approximately(RotationX, other.RotationX) ||
                !Mathf.Approximately(RotationY, other.RotationY) ||
                !Mathf.Approximately(CameraDistance, other.CameraDistance) ||
                Vector3.Distance(CameraTarget, other.CameraTarget) > 0.0001f ||
                ShowWireframe != other.ShowWireframe ||
                ShowVertices != other.ShowVertices ||
                VertexEditMode != other.VertexEditMode ||
                CurrentToolName != other.CurrentToolName ||
                AddToCurrentMesh != other.AddToCurrentMesh ||
                AutoMergeOnCreate != other.AutoMergeOnCreate ||
                !Mathf.Approximately(AutoMergeThreshold, other.AutoMergeThreshold) ||
                RecordFoldoutChanges != other.RecordFoldoutChanges)
            {
                return true;
            }
            
            // Foldout状態の比較
            if (!AreFoldoutStatesEqual(FoldoutStates, other.FoldoutStates))
            {
                return true;
            }
            /*
            // KnifeProperty（既存）
            if (knifeProperty != null && other.knifeProperty != null)
            {
                if (knifeProperty.Mode != other.knifeProperty.Mode ||
                    knifeProperty.EdgeSelect != other.knifeProperty.EdgeSelect ||
                    knifeProperty.ChainMode != other.knifeProperty.ChainMode)
                {
                    return true;
                }
            }
            */
            // ToolSettings（新規）
            if (ToolSettings != null && other.ToolSettings != null)
            {
                if (ToolSettings.IsDifferentFrom(other.ToolSettings))
                    return true;
            }
            else if ((ToolSettings != null && ToolSettings.Count > 0) ||
                     (other.ToolSettings != null && other.ToolSettings.Count > 0))
            {
                // 片方だけ設定がある場合は差異あり
                return true;
            }

            return false;
        }
        
        private static bool AreFoldoutStatesEqual(Dictionary<string, bool> a, Dictionary<string, bool> b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            
            foreach (var kvp in a)
            {
                if (!b.TryGetValue(kvp.Key, out var bValue) || kvp.Value != bValue)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// エディタ状態変更記録（WorkPlane連動対応）
    /// </summary>
    public class EditorStateChangeRecord : IUndoRecord<EditorStateContext>
    {
        public UndoOperationInfo Info { get; set; }
        public EditorStateSnapshot Before;
        public EditorStateSnapshot After;

        // WorkPlane連動（カメラ変更時の軸更新用）
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        public EditorStateChangeRecord(EditorStateSnapshot before, EditorStateSnapshot after)
        {
            Before = before;
            After = after;
            OldWorkPlaneSnapshot = null;
            NewWorkPlaneSnapshot = null;
        }

        /// <summary>
        /// WorkPlane連動付きコンストラクタ
        /// </summary>
        public EditorStateChangeRecord(
            EditorStateSnapshot before,
            EditorStateSnapshot after,
            WorkPlaneSnapshot? oldWorkPlane,
            WorkPlaneSnapshot? newWorkPlane)
            : this(before, after)
        {
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public void Undo(EditorStateContext ctx)
        {
            ctx.ApplySnapshot(Before);

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public void Redo(EditorStateContext ctx)
        {
            ctx.ApplySnapshot(After);

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }

    // ============================================================
    // 後方互換用（既存コードとの互換性維持）
    // ============================================================

    /// <summary>
    /// 表示設定コンテキスト（後方互換用エイリアス）
    /// </summary>
    public class ViewContext : EditorStateContext { }

    /// <summary>
    /// 表示設定変更記録（後方互換用）
    /// </summary>
    public class ViewChangeRecord : IUndoRecord<EditorStateContext>
    {
        public UndoOperationInfo Info { get; set; }

        public float OldRotationX, NewRotationX;
        public float OldRotationY, NewRotationY;
        public float OldCameraDistance, NewCameraDistance;
        public Vector3 OldCameraTarget, NewCameraTarget;

        public void Undo(EditorStateContext ctx)
        {
            ctx.RotationX = OldRotationX;
            ctx.RotationY = OldRotationY;
            ctx.CameraDistance = OldCameraDistance;
            ctx.CameraTarget = OldCameraTarget;
        }

        public void Redo(EditorStateContext ctx)
        {
            ctx.RotationX = NewRotationX;
            ctx.RotationY = NewRotationY;
            ctx.CameraDistance = NewCameraDistance;
            ctx.CameraTarget = NewCameraTarget;
        }
    }

    // ============================================================
    // 面追加操作記録（頂点と面をまとめて1つの操作として扱う）
    // ============================================================

    /// <summary>
    /// 面追加操作記録
    /// 新規頂点と面の追加を1つの操作としてまとめる
    /// </summary>
    public class AddFaceOperationRecord : MeshUndoRecord
    {
        /// <summary>追加した頂点のリスト（インデックス, 頂点データ）</summary>
        public List<(int Index, Vertex Vertex)> AddedVertices = new List<(int, Vertex)>();

        /// <summary>追加した面（nullの場合は頂点のみ追加）</summary>
        public Face AddedFace;

        /// <summary>面のインデックス（-1の場合は面なし）</summary>
        public int FaceIndex;

        public AddFaceOperationRecord(Face face, int faceIndex, List<(int Index, Vertex Vertex)> addedVertices)
        {
            AddedFace = face?.Clone();
            FaceIndex = faceIndex;

            // 頂点をクローン
            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData == null) return;

            // 面を削除
            if (AddedFace != null && FaceIndex >= 0 && FaceIndex < ctx.MeshData.FaceCount)
            {
                ctx.MeshData.Faces.RemoveAt(FaceIndex);
            }

            // 頂点を削除（逆順で削除してインデックスを維持）
            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < ctx.MeshData.VertexCount)
                {
                    ctx.MeshData.Vertices.RemoveAt(idx);
                    // 面のインデックスを調整
                    AdjustFaceIndicesAfterVertexRemoval(ctx.MeshData, idx);
                }
            }

            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData == null) return;

            // 頂点を追加（昇順で追加、範囲外なら末尾に追加）
            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (idx, vtx) in sortedVertices)
            {
                if (idx >= ctx.MeshData.Vertices.Count)
                {
                    // インデックスが範囲外なら末尾に追加
                    ctx.MeshData.Vertices.Add(vtx.Clone());
                }
                else
                {
                    ctx.MeshData.Vertices.Insert(idx, vtx.Clone());
                }
                // 面のインデックスを調整
                AdjustFaceIndicesAfterVertexInsertion(ctx.MeshData, idx);
            }

            // 面を追加（範囲外なら末尾に追加）
            if (AddedFace != null && FaceIndex >= 0)
            {
                if (FaceIndex >= ctx.MeshData.Faces.Count)
                {
                    ctx.MeshData.Faces.Add(AddedFace.Clone());
                }
                else
                {
                    ctx.MeshData.Faces.Insert(FaceIndex, AddedFace.Clone());
                }
            }

            ctx.ApplyToMesh();
        }

        private void AdjustFaceIndicesAfterVertexRemoval(MeshData meshData, int removedIndex)
        {
            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] > removedIndex)
                        face.VertexIndices[i]--;
                }
            }
        }

        private void AdjustFaceIndicesAfterVertexInsertion(MeshData meshData, int insertedIndex)
        {
            foreach (var face in meshData.Faces)
            {
                for (int i = 0; i < face.VertexIndices.Count; i++)
                {
                    if (face.VertexIndices[i] >= insertedIndex)
                        face.VertexIndices[i]++;
                }
            }
        }
    }

    // ============================================================
    // KnifeCutOperationRecord - ナイフ切断操作
    // ============================================================
    //ナイフツールの固有設定---------------

    /// <summary>
    /// ナイフ切断操作の記録
    /// </summary>
    public class KnifeCutOperationRecord : MeshUndoRecord
    {
        /// <summary>切断された面のインデックス</summary>
        public int OriginalFaceIndex;

        /// <summary>元の面データ</summary>
        public Face OriginalFace;

        /// <summary>分割後の面1（元の位置に配置）</summary>
        public Face NewFace1;

        /// <summary>分割後の面2のインデックス</summary>
        public int NewFace2Index;

        /// <summary>分割後の面2</summary>
        public Face NewFace2;

        /// <summary>追加された頂点リスト（インデックスと頂点データ）</summary>
        public List<(int Index, Vertex Vertex)> AddedVertices = new List<(int, Vertex)>();

        public KnifeCutOperationRecord(
            int originalFaceIndex,
            Face originalFace,
            Face newFace1,
            int newFace2Index,
            Face newFace2,
            List<(int Index, Vertex Vertex)> addedVertices)
        {
            OriginalFaceIndex = originalFaceIndex;
            OriginalFace = originalFace?.Clone();
            NewFace1 = newFace1?.Clone();
            NewFace2Index = newFace2Index;
            NewFace2 = newFace2?.Clone();

            foreach (var (idx, vtx) in addedVertices)
            {
                AddedVertices.Add((idx, vtx.Clone()));
            }
        }

        public override void Undo(MeshEditContext ctx)
        {
            if (ctx.MeshData == null) return;

            // 分割後の面2を削除（末尾から）
            if (NewFace2Index >= 0 && NewFace2Index < ctx.MeshData.FaceCount)
            {
                ctx.MeshData.Faces.RemoveAt(NewFace2Index);
            }

            // 元の面を復元
            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshData.FaceCount && OriginalFace != null)
            {
                ctx.MeshData.Faces[OriginalFaceIndex] = OriginalFace.Clone();
            }

            // 追加された頂点を削除（逆順で、末尾から）
            var sortedVertices = AddedVertices.OrderByDescending(v => v.Index).ToList();
            foreach (var (idx, _) in sortedVertices)
            {
                if (idx < ctx.MeshData.VertexCount)
                {
                    ctx.MeshData.Vertices.RemoveAt(idx);
                }
            }

            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            if (ctx.MeshData == null) return;

            // 頂点を追加（末尾に追加）
            var sortedVertices = AddedVertices.OrderBy(v => v.Index).ToList();
            foreach (var (_, vtx) in sortedVertices)
            {
                ctx.MeshData.Vertices.Add(vtx.Clone());
            }

            // 面を更新
            if (OriginalFaceIndex >= 0 && OriginalFaceIndex < ctx.MeshData.FaceCount && NewFace1 != null)
            {
                ctx.MeshData.Faces[OriginalFaceIndex] = NewFace1.Clone();
            }

            // 面2を追加（末尾に）
            if (NewFace2 != null)
            {
                ctx.MeshData.Faces.Add(NewFace2.Clone());
            }

            ctx.ApplyToMesh();
        }
    }

    // ============================================================
    // 拡張選択変更記録（Edge/Line対応）
    // ============================================================

    /// <summary>
    /// 拡張選択変更記録（Edge/Face/Line全モード対応）
    /// SelectionSnapshotを使用して全選択状態を保存
    /// </summary>
    public class ExtendedSelectionChangeRecord : MeshUndoRecord
    {
        // 新選択システムのスナップショット
        public MeshFactory.Selection.SelectionSnapshot OldSnapshot;
        public MeshFactory.Selection.SelectionSnapshot NewSnapshot;

        // レガシー互換用（_selectedVertices との同期用）
        public HashSet<int> OldLegacyVertices;
        public HashSet<int> NewLegacyVertices;

        // WorkPlane連動
        public WorkPlaneSnapshot? OldWorkPlaneSnapshot;
        public WorkPlaneSnapshot? NewWorkPlaneSnapshot;

        public ExtendedSelectionChangeRecord(
            MeshFactory.Selection.SelectionSnapshot oldSnapshot,
            MeshFactory.Selection.SelectionSnapshot newSnapshot,
            HashSet<int> oldLegacyVertices = null,
            HashSet<int> newLegacyVertices = null,
            WorkPlaneSnapshot? oldWorkPlane = null,
            WorkPlaneSnapshot? newWorkPlane = null)
        {
            OldSnapshot = oldSnapshot?.Clone();
            NewSnapshot = newSnapshot?.Clone();
            OldLegacyVertices = oldLegacyVertices != null ? new HashSet<int>(oldLegacyVertices) : new HashSet<int>();
            NewLegacyVertices = newLegacyVertices != null ? new HashSet<int>(newLegacyVertices) : new HashSet<int>();
            OldWorkPlaneSnapshot = oldWorkPlane;
            NewWorkPlaneSnapshot = newWorkPlane;
        }

        public override void Undo(MeshEditContext ctx)
        {
            // レガシー選択を復元（MeshEditContext用）
            ctx.SelectedVertices = new HashSet<int>(OldLegacyVertices);

            // 拡張選択スナップショットを設定
            ctx.CurrentSelectionSnapshot = OldSnapshot?.Clone();

            // WorkPlane連動復元
            if (OldWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(OldWorkPlaneSnapshot.Value);
            }
        }

        public override void Redo(MeshEditContext ctx)
        {
            // レガシー選択を復元（MeshEditContext用）
            ctx.SelectedVertices = new HashSet<int>(NewLegacyVertices);

            // 拡張選択スナップショットを設定
            ctx.CurrentSelectionSnapshot = NewSnapshot?.Clone();

            // WorkPlane連動復元
            if (NewWorkPlaneSnapshot.HasValue && ctx.WorkPlane != null)
            {
                ctx.WorkPlane.ApplySnapshot(NewWorkPlaneSnapshot.Value);
            }
        }
    }

    // ============================================================
    // メッシュリスト操作用
    // ============================================================

    /// <summary>
    /// メッシュリスト操作用
   
    /// </summary>
    public class MeshListContext
    {
        /// <summary>メッシュコンテキストリスト（SimpleMeshFactoryから参照を受け取る）</summary>
        public List<SimpleMeshFactory.MeshContext> MeshContextList;

        /// <summary>現在の選択インデックス</summary>
        public int SelectedIndex;

        /// <summary>Undo/Redo実行後のコールバック（UI更新等）</summary>
        public System.Action OnListChanged;
    }

    /// <summary>
    /// MeshContextの完全なスナップショット
    /// </summary>
    public class MeshContextSnapshot
    {
        public string Name;
        public MeshData Data;                    // Clone
        public List<Material> Materials;
        public int CurrentMaterialIndex;
        public ExportSettings ExportSettings;
        public Vector3[] OriginalPositions;

        /// <summary>
        /// MeshContextからスナップショットを作成
        /// </summary>
        public static MeshContextSnapshot Capture(SimpleMeshFactory.MeshContext ｍeshContext)
        {
            if (ｍeshContext == null) return null;

            return new MeshContextSnapshot
            {
                Name = ｍeshContext.Name,
                Data = ｍeshContext.Data?.Clone(),
                Materials = ｍeshContext.Materials != null ? new List<Material>(ｍeshContext.Materials) : new List<Material>(),
                CurrentMaterialIndex = ｍeshContext.CurrentMaterialIndex,
                ExportSettings = ｍeshContext.ExportSettings != null ? new ExportSettings(ｍeshContext.ExportSettings) : null,
                OriginalPositions = ｍeshContext.OriginalPositions != null 
                    ? (Vector3[])ｍeshContext.OriginalPositions.Clone() 
                    : null
            };
        }

        /// <summary>
        /// スナップショットからMeshContextを復元
        /// </summary>
        public SimpleMeshFactory.MeshContext ToMeshContext()
        {
            var meshContext = new SimpleMeshFactory.MeshContext
            {
                Name = Name,
                Data = Data?.Clone(),
                Materials = Materials != null ? new List<Material>(Materials) : new List<Material>(),
                CurrentMaterialIndex = CurrentMaterialIndex,
                ExportSettings = ExportSettings != null ? new ExportSettings(ExportSettings) : null,
                OriginalPositions = OriginalPositions != null 
                    ? (Vector3[])OriginalPositions.Clone() 
                    : null
            };

            // Unity Meshを再生成
            if (meshContext.Data != null)
            {
                meshContext.UnityMesh = meshContext.Data.ToUnityMesh();
                meshContext.UnityMesh.name = Name;
                meshContext.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            return meshContext;
        }
    }

    /// <summary>
    /// メッシュリスト用Undo記録の基底クラス
    /// </summary>
    public abstract class MeshListUndoRecord : IUndoRecord<MeshListContext>
    {
        public UndoOperationInfo Info { get; set; }
        public abstract void Undo(MeshListContext context);
        public abstract void Redo(MeshListContext context);
    }

    /// <summary>
    /// メッシュリスト変更記録
    /// 追加、削除、挿入、順序変更に対応
    /// </summary>
    public class MeshListChangeRecord : MeshListUndoRecord
    {
        /// <summary>削除されたエントリ（インデックス + スナップショット）</summary>
        public List<(int Index, MeshContextSnapshot Snapshot)> RemovedMeshContexts = new List<(int, MeshContextSnapshot)>();

        /// <summary>追加されたエントリ（インデックス + スナップショット）</summary>
        public List<(int Index, MeshContextSnapshot Snapshot)> AddedMeshContexts = new List<(int, MeshContextSnapshot)>();

        /// <summary>変更前の選択インデックス</summary>
        public int OldSelectedIndex;

        /// <summary>変更後の選択インデックス</summary>
        public int NewSelectedIndex;

        public override void Undo(MeshListContext ctx)
        {
            // 1. AddedMeshContextsを降順で削除（大きいインデックスから）
            var sortedAdded = AddedMeshContexts.OrderByDescending(e => e.Index).ToList();
            foreach (var (index, _) in sortedAdded)
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    // Meshを破棄
                    var meshContext = ctx.MeshContextList[index];
                    if (meshContext.UnityMesh != null)
                    {
                        Object.DestroyImmediate(meshContext.UnityMesh);
                    }
                    ctx.MeshContextList.RemoveAt(index);
                }
            }

            // 2. RemovedMeshContextsを昇順で挿入（小さいインデックスから）
            var sortedRemoved = RemovedMeshContexts.OrderBy(e => e.Index).ToList();
            foreach (var (index, snapshot) in sortedRemoved)
            {
                var meshContext = snapshot.ToMeshContext();
                int insertIndex = Mathf.Clamp(index, 0, ctx.MeshContextList.Count);
                ctx.MeshContextList.Insert(insertIndex, meshContext);
            }

            // 3. 選択インデックスを復元
            ctx.SelectedIndex = Mathf.Clamp(OldSelectedIndex, -1, ctx.MeshContextList.Count - 1);

            // 4. コールバック
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(MeshListContext ctx)
        {
            // 1. RemovedMeshContextsを降順で削除（大きいインデックスから）
            var sortedRemoved = RemovedMeshContexts.OrderByDescending(e => e.Index).ToList();
            foreach (var (index, _) in sortedRemoved)
            {
                if (index >= 0 && index < ctx.MeshContextList.Count)
                {
                    // Meshを破棄
                    var meshContext = ctx.MeshContextList[index];
                    if (meshContext.UnityMesh != null)
                    {
                        Object.DestroyImmediate(meshContext.UnityMesh);
                    }
                    ctx.MeshContextList.RemoveAt(index);
                }
            }

            // 2. AddedMeshContextsを昇順で挿入（小さいインデックスから）
            var sortedAdded = AddedMeshContexts.OrderBy(e => e.Index).ToList();
            foreach (var (index, snapshot) in sortedAdded)
            {
                var meshContext = snapshot.ToMeshContext();
                int insertIndex = Mathf.Clamp(index, 0, ctx.MeshContextList.Count);
                ctx.MeshContextList.Insert(insertIndex, meshContext);
            }

            // 3. 選択インデックスを復元
            ctx.SelectedIndex = Mathf.Clamp(NewSelectedIndex, -1, ctx.MeshContextList.Count - 1);

            // 4. コールバック
            ctx.OnListChanged?.Invoke();
        }
    }
}
