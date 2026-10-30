// Assets/Editor/UndoSystem/MeshEditor/Snapshots/MeshObjectSnapshot.cs
// メッシュデータのスナップショット
// トポロジー変更用の完全なメッシュ状態保存

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.UndoSystem
{
    // ============================================================
    // メッシュ全体のスナップショット記録
    // ============================================================

    /// <summary>
    /// メッシュ全体のスナップショット記録（トポロジー変更用）
    /// MeshObjectベースの新構造対応
    /// 
    /// SelectionState参照を保持し、Edge/Line選択もUndo対象に
    /// </summary>
    public class MeshSnapshotRecord : MeshUndoRecord
    {
        public MeshObjectSnapshot Before;
        public MeshObjectSnapshot After;

        private SelectionState _selectionStateRef;

        public MeshSnapshotRecord(MeshObjectSnapshot before, MeshObjectSnapshot after)
        {
            Before = before;
            After = after;
            _selectionStateRef = null;
        }

        public MeshSnapshotRecord(
            MeshObjectSnapshot before,
            MeshObjectSnapshot after,
            SelectionState selectionState)
        {
            Before = before;
            After = after;
            _selectionStateRef = selectionState;
        }

        public override void Undo(MeshUndoContext ctx)
        {
            if (_selectionStateRef != null)
                Before?.ApplyTo(ctx, _selectionStateRef);
            else
                Before?.ApplyTo(ctx);
        }

        public override void Redo(MeshUndoContext ctx)
        {
            if (_selectionStateRef != null)
                After?.ApplyTo(ctx, _selectionStateRef);
            else
                After?.ApplyTo(ctx);
        }
    }

    // ============================================================
    // MeshObjectSnapshot クラス
    // ============================================================

    /// <summary>
    /// MeshObjectのスナップショット
    /// マルチマテリアル対応版
    /// 
    /// 【フェーズ1】ExtendedSelection対応
    /// トポロジー変更時にEdge/Line選択も保存・復元可能に
    /// </summary>
    public class MeshObjectSnapshot
    {
        public MeshObject MeshObject;
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
        /// 捕獲元（書き込み先）。復元先の取り違えを防ぐために持つ。
        ///
        /// MeshUndoContext を経由する ApplyTo は、既定では
        /// ResolvedMeshContext（先頭の選択メッシュ）へ書き戻す。捕獲時と適用時で
        /// 選択が違えば別メッシュへ書き込むため、ここに実体を控えて優先する。
        ///
        /// 参照だけでなくモデルと ObjectId も控え、適用時に今のモデルの一覧から引き直す
        /// （一覧のスナップショットの Undo が描画オブジェクトを複製に置き換えるため。
        /// MeshUndoTarget の冒頭を参照。作り直し整備計画 段 12）。
        /// </summary>
        public MeshUndoTarget Source;

        /// <summary>捕獲元の描画オブジェクト（控えたときの参照）。</summary>
        public MeshContext SourceMeshContext => Source.Context;

        /// <summary>
        /// メッシュデータ・選択・材質を書き戻す。
        ///
        /// 捕獲元が控えてあれば、今のモデルの一覧から引き直してそこへ書く。
        /// 引き直せなければ（オブジェクトが消えた）何も書かない。別のオブジェクトへ書かない。
        /// 控えが無い場合だけ ctx 経由＝適用時の ResolvedMeshContext へ落とす。
        /// 材質は書き込み先のモデルへ戻す（ctx.ParentModelContext は今のモデルで、
        /// 捕獲したモデルと違うことがある）。
        /// 書いた相手は ctx.AppliedMeshContexts へ入れ、後処理が表面メッシュを作り直す。
        /// </summary>
        private bool WriteTo(MeshUndoContext ctx)
        {
            MeshContext target;
            ModelContext model;
            if (Source.IsSet)
            {
                target = Source.Resolve();
                if (target == null)
                {
                    Debug.LogWarning(
                        $"[MeshObjectSnapshot] 書き戻し先が見つかりません（ObjectId={Source.ObjectId}）。書き戻しません。");
                    return false;
                }
                model = target.ParentModelContext ?? Source.Model;
            }
            else
            {
                target = ctx.ResolvedMeshContext;
                model  = ctx.ParentModelContext;
            }

            var clone = MeshObject?.Clone();
            if (target != null) target.MeshObject = clone;
            else                ctx.MeshObject    = clone;
            if (target != null && !ctx.AppliedMeshContexts.Contains(target))
                ctx.AppliedMeshContexts.Add(target);

            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);

            if (model != null)
            {
                if (Materials != null)
                {
                    model.Materials = new List<Material>(Materials);
                    model.CurrentMaterialIndex = CurrentMaterialIndex;
                }
                if (DefaultMaterials != null)
                    model.DefaultMaterials = new List<Material>(DefaultMaterials);
                model.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
                model.AutoSetDefaultMaterials = AutoSetDefaultMaterials;
            }
            else
            {
                if (Materials != null)
                {
                    ctx.Materials = new List<Material>(Materials);
                    ctx.CurrentMaterialIndex = CurrentMaterialIndex;
                }
                if (DefaultMaterials != null)
                    ctx.DefaultMaterials = new List<Material>(DefaultMaterials);
                ctx.DefaultCurrentMaterialIndex = DefaultCurrentMaterialIndex;
                ctx.AutoSetDefaultMaterials = AutoSetDefaultMaterials;
            }
            return true;
        }

        /// <summary>
        /// MeshContext + MeshUndoContext からスナップショットを作成
        /// </summary>
        public static MeshObjectSnapshot Capture(MeshContext mc, MeshUndoContext ctx)
        {
            return new MeshObjectSnapshot
            {
                Source = MeshUndoTarget.Of(mc, ctx.ParentModelContext),
                MeshObject = mc.MeshObject?.Clone(),
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(),
                Materials = ctx.Materials != null ? new List<Material>(ctx.Materials) : new List<Material> { null },
                CurrentMaterialIndex = ctx.CurrentMaterialIndex,
                DefaultMaterials = ctx.DefaultMaterials != null ? new List<Material>(ctx.DefaultMaterials) : new List<Material> { null },
                DefaultCurrentMaterialIndex = ctx.DefaultCurrentMaterialIndex,
                AutoSetDefaultMaterials = ctx.AutoSetDefaultMaterials
            };
        }

        /// <summary>
        /// MeshContext + MeshUndoContext + SelectionState からスナップショットを作成
        /// </summary>
        public static MeshObjectSnapshot Capture(MeshContext mc, MeshUndoContext ctx, SelectionState selectionState)
        {
            var snapshot = Capture(mc, ctx);

            if (selectionState != null)
            {
                snapshot.ExtendedSelection = selectionState.CreateSnapshot();
                snapshot.SelectedFaces = new HashSet<int>(snapshot.ExtendedSelection.Faces);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットを MeshContext + MeshUndoContext に適用
        /// </summary>
        public void ApplyTo(MeshContext mc, MeshUndoContext ctx, SelectionState selectionState)
        {
            // メッシュデータ → MeshContext
            mc.MeshObject = MeshObject?.Clone();

            // 選択・マテリアル → MeshUndoContext
            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);

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

            if (ExtendedSelection != null && selectionState != null)
            {
                selectionState.RestoreFromSnapshot(ExtendedSelection);
            }
        }

        // ================================================================
        // 後方互換: MeshUndoContext のみを取るオーバーロード
        // ================================================================

        /// <summary>
        /// コンテキストからスナップショットを作成（従来版・後方互換）
        /// </summary>
        public static MeshObjectSnapshot Capture(MeshUndoContext ctx)
        {
            return new MeshObjectSnapshot
            {
                Source = MeshUndoTarget.Of(ctx.ResolvedMeshContext, ctx.ParentModelContext),
                MeshObject = ctx.MeshObject?.Clone(),
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(),
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
        public static MeshObjectSnapshot Capture(MeshUndoContext ctx, SelectionState selectionState)
        {
            MeshObjectSnapshot snapshot = new MeshObjectSnapshot
            {
                Source = MeshUndoTarget.Of(ctx.ResolvedMeshContext, ctx.ParentModelContext),
                MeshObject = ctx.MeshObject?.Clone(),
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>(),
                Materials = ctx.Materials != null ? new List<Material>(ctx.Materials) : new List<Material> { null },
                CurrentMaterialIndex = ctx.CurrentMaterialIndex,
                DefaultMaterials = ctx.DefaultMaterials != null ? new List<Material>(ctx.DefaultMaterials) : new List<Material> { null },
                DefaultCurrentMaterialIndex = ctx.DefaultCurrentMaterialIndex,
                AutoSetDefaultMaterials = ctx.AutoSetDefaultMaterials
            };

            // 【フェーズ1追加】拡張選択を保存
            if (selectionState != null)
            {
                snapshot.ExtendedSelection = selectionState.CreateSnapshot();
                // ExtendedSelectionからFacesを取得（後方互換フィールド）
                snapshot.SelectedFaces = new HashSet<int>(snapshot.ExtendedSelection.Faces);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットをコンテキストに適用（従来版・後方互換）
        /// </summary>
        public void ApplyTo(MeshUndoContext ctx)
        {
            // メッシュデータ・選択・材質（書き込み先のモデルへ）
            WriteTo(ctx);

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
        public void ApplyTo(MeshUndoContext ctx, SelectionState selectionState)
        {
            // === 既存処理（メッシュデータ、レガシー選択、マテリアル） ===
            // 書き込み先が見つからなければ選択も戻さない（別のオブジェクトの選択になる）。
            if (!WriteTo(ctx)) return;

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
        public static MeshSnapshot Capture(MeshUndoContext ctx)
        {
            // MeshObjectから Unity 形式のデータを取得
            if (ctx.MeshObject == null)
            {
                return new MeshSnapshot
                {
                    Vertices = new Vector3[0],
                    Triangles = new int[0],
                    UVs = new Vector2[0],
                    Normals = new Vector3[0],
                    SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                    SelectedFaces = new HashSet<int>()
                };
            }

            UnityEngine.Mesh mesh = ctx.MeshObject.ToUnityMeshShared();
            MeshSnapshot snapshot = new MeshSnapshot
            {
                Vertices = mesh.vertices,
                Triangles = mesh.triangles,
                UVs = mesh.uv,
                Normals = mesh.normals,
                SelectedVertices = new HashSet<int>(ctx.SelectedVertices),
                SelectedFaces = new HashSet<int>()
            };
            Object.DestroyImmediate(mesh);
            return snapshot;
        }

        /// <summary>
        /// スナップショットをコンテキストに適用（後方互換用）
        /// </summary>
        public void ApplyTo(MeshUndoContext ctx)
        {
            // 一時的なMeshを作成してMeshObjectに変換
            UnityEngine.Mesh tempMesh = new Mesh
            {
                vertices = Vertices,
                triangles = Triangles,
                uv = UVs,
                normals = Normals
            };

            ctx.MeshObject = new MeshObject();
            ctx.MeshObject.FromUnityMesh(tempMesh, true);
            ctx.SelectedVertices = new HashSet<int>(SelectedVertices);

            Object.DestroyImmediate(tempMesh);
        }
    }
}
