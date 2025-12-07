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

        // === Unity Mesh 参照 ===

        /// <summary>実際のMeshオブジェクト</summary>
        public Mesh TargetMesh;

        // === WorkPlane参照（選択連動Undo用） ===

        /// <summary>WorkPlane参照（選択と連動してUndo/Redoするため）</summary>
        public WorkPlane WorkPlane;

        // === 拡張選択システム用 ===

        /// <summary>Undo/Redo時に復元すべきSelectionSnapshot</summary>
        public SelectionSnapshot CurrentSelectionSnapshot;

        // === マテリアル（マルチマテリアル対応） ===

        /// <summary>マテリアルリスト（MeshEntry.Materialsと同期）</summary>
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
    /// </summary>
    public class MeshSnapshotRecord : MeshUndoRecord
    {
        public MeshDataSnapshot Before;
        public MeshDataSnapshot After;

        public MeshSnapshotRecord(MeshDataSnapshot before, MeshDataSnapshot after)
        {
            Before = before;
            After = after;
        }

        public override void Undo(MeshEditContext ctx)
        {
            Before?.ApplyTo(ctx);
            ctx.ApplyToMesh();
        }

        public override void Redo(MeshEditContext ctx)
        {
            After?.ApplyTo(ctx);
            ctx.ApplyToMesh();
        }
    }

    /// <summary>
    /// MeshDataのスナップショット
    /// マルチマテリアル対応版
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

        /// <summary>
        /// コンテキストからスナップショットを作成
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
            };
        }

        /// <summary>
        /// スナップショットをコンテキストに適用
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
    // 頂点UV/法線変更記録（新機能）
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

        // ツール設定
        public KnifeMode KnifeMode = KnifeMode.Cut;
        public bool KnifeEdgeSelect = false;
        public bool KnifeChainMode = false;

        // WorkPlane参照（カメラ連動Undo用）
        public WorkPlane WorkPlane;

        /// <summary>
        /// スナップショットを作成
        /// </summary>
        public EditorStateSnapshot Capture()
        {
            return new EditorStateSnapshot
            {
                RotationX = RotationX,
                RotationY = RotationY,
                CameraDistance = CameraDistance,
                CameraTarget = CameraTarget,
                ShowWireframe = ShowWireframe,
                ShowVertices = ShowVertices,
                VertexEditMode = VertexEditMode,
                CurrentToolName = CurrentToolName,
                KnifeMode = KnifeMode,
                KnifeEdgeSelect = KnifeEdgeSelect,
                KnifeChainMode = KnifeChainMode
            };
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
            KnifeMode = snapshot.KnifeMode;
            KnifeEdgeSelect = snapshot.KnifeEdgeSelect;
            KnifeChainMode = snapshot.KnifeChainMode;
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
        public KnifeMode KnifeMode;
        public bool KnifeEdgeSelect;
        public bool KnifeChainMode;

        public bool IsDifferentFrom(EditorStateSnapshot other)
        {
            return !Mathf.Approximately(RotationX, other.RotationX) ||
                   !Mathf.Approximately(RotationY, other.RotationY) ||
                   !Mathf.Approximately(CameraDistance, other.CameraDistance) ||
                   Vector3.Distance(CameraTarget, other.CameraTarget) > 0.0001f ||
                   ShowWireframe != other.ShowWireframe ||
                   ShowVertices != other.ShowVertices ||
                   VertexEditMode != other.VertexEditMode ||
                   CurrentToolName != other.CurrentToolName ||
                   KnifeMode != other.KnifeMode ||
                   KnifeEdgeSelect != other.KnifeEdgeSelect ||
                   KnifeChainMode != other.KnifeChainMode;
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
}
