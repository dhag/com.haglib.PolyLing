// PolyLing_CommandHandlers_Merge.cs
// メッシュオブジェクトマージコマンドハンドラ
// DispatchPanelCommand から呼ばれる

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;

public partial class PolyLing
{
    /// <summary>
    /// 選択メッシュオブジェクトをマージする。
    /// CreateNewMesh=true の場合: 新規 MeshContext を作成し、全対象メッシュの頂点を
    ///   基準オブジェクトのローカル空間に変換して格納する。元の MeshContext は削除する。
    /// CreateNewMesh=false の場合: 非基準メッシュの頂点を基準オブジェクトのローカル空間に
    ///   変換して基準 MeshContext の MeshObject に追記する。非基準 MeshContext は削除する。
    /// </summary>
    private void HandleMergeMeshesCommand(MergeMeshesCommand cmd)
    {
        if (_model == null) return;
        if (cmd.MasterIndices == null || cmd.MasterIndices.Length < 2) return;

        // ----------------------------------------------------------------
        // 1. 対象 MeshContext を収集（有効なもののみ）
        // ----------------------------------------------------------------
        var targets = new List<MeshContext>();
        foreach (int mi in cmd.MasterIndices)
        {
            var ctx = _model.GetMeshContext(mi);
            if (ctx?.MeshObject != null)
                targets.Add(ctx);
        }
        if (targets.Count < 2) return;

        var baseCtx = _model.GetMeshContext(cmd.BaseMasterIndex);
        if (baseCtx?.MeshObject == null) return;

        // ----------------------------------------------------------------
        // 2. 基準オブジェクトのローカル→ワールド逆行列を取得
        //    （他メッシュの頂点を基準ローカル空間に変換するために使う）
        // ----------------------------------------------------------------
        Matrix4x4 baseWorldInv = baseCtx.WorldMatrixInverse;

        // ----------------------------------------------------------------
        // 3. マージ先 MeshObject の準備
        // ----------------------------------------------------------------
        MeshContext destCtx;

        if (cmd.CreateNewMesh)
        {
            // 新規 MeshContext: 基準オブジェクトのトランスフォームを引き継ぐ
            destCtx = new MeshContext
            {
                Name            = baseCtx.MeshObject.Name + "_merged",
                MeshObject      = new MeshObject(baseCtx.MeshObject.Name + "_merged"),
                OriginalPositions = new Vector3[0],
            };
            var bt = new BoneTransform();
            bt.CopyFrom(baseCtx.BoneTransform);
            destCtx.BoneTransform = bt;
            destCtx.WorldMatrix        = baseCtx.WorldMatrix;
            destCtx.WorldMatrixInverse = baseCtx.WorldMatrixInverse;
            destCtx.BindPose           = baseCtx.BindPose;
        }
        else
        {
            // 基準 MeshContext に直接追記
            destCtx = baseCtx;
        }

        MeshObject destMesh = destCtx.MeshObject;

        // ----------------------------------------------------------------
        // 4. 各ソースメッシュの頂点・面を destMesh に追記
        //    CreateNewMesh の場合は全メッシュ（基準含む）を追記
        //    CreateNewMesh=false の場合は非基準メッシュのみ追記
        // ----------------------------------------------------------------
        foreach (var srcCtx in targets)
        {
            bool isBase = ReferenceEquals(srcCtx, baseCtx);
            if (!cmd.CreateNewMesh && isBase) continue; // 基準はスキップ（既にdestCtx）

            var srcMesh = srcCtx.MeshObject;
            if (srcMesh == null || srcMesh.VertexCount == 0) continue;

            // 頂点変換行列: src ワールド空間 → base ローカル空間
            // src ローカル → ワールド: srcCtx.WorldMatrix
            // ワールド → base ローカル: baseWorldInv
            Matrix4x4 xform = baseWorldInv * srcCtx.WorldMatrix;

            int vertexOffset = destMesh.VertexCount;

            // 頂点追記
            foreach (var v in srcMesh.Vertices)
            {
                var newV = v.Clone();
                newV.Id       = destMesh.GenerateVertexId();
                newV.Position = xform.MultiplyPoint3x4(v.Position);

                // 法線も回転変換（スケール非均等の場合は逆転置行列が正確だが、
                // ここでは MultiplyVector で近似する）
                if (v.Normals != null)
                {
                    newV.Normals = v.Normals.Select(n => xform.MultiplyVector(n).normalized).ToList();
                }
                destMesh.Vertices.Add(newV);
                destMesh.RegisterVertexId(newV.Id);
            }

            // 面追記（頂点インデックスをオフセット）
            foreach (var f in srcMesh.Faces)
            {
                var newF = f.Clone();
                newF.Id = destMesh.GenerateFaceId();
                newF.VertexIndices = f.VertexIndices.Select(i => i + vertexOffset).ToList();
                destMesh.Faces.Add(newF);
                destMesh.RegisterFaceId(newF.Id);
            }
        }

        // ----------------------------------------------------------------
        // 5. Unity Mesh 再生成
        // ----------------------------------------------------------------
        var unityMesh = destMesh.ToUnityMesh();
        unityMesh.name = destMesh.Name;
        unityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
        destCtx.UnityMesh = unityMesh;
        destCtx.OriginalPositions = (Vector3[])destMesh.Positions.Clone();

        // ----------------------------------------------------------------
        // 6. モデルへの追加・削除（Undo 対応）
        // ----------------------------------------------------------------
        if (cmd.CreateNewMesh)
        {
            // ソースはそのまま残し、新規メッシュを末尾に追加する
            AddMeshContextWithUndo(destCtx);
        }
        else
        {
            // 非基準メッシュを削除
            var nonBaseTargets = targets.Where(t => !ReferenceEquals(t, baseCtx)).ToList();
            var indicesToRemove = nonBaseTargets
                .Select(t => _model.IndexOf(t))
                .Where(i => i >= 0)
                .OrderByDescending(i => i)
                .ToList();

            foreach (int idx in indicesToRemove)
                RemoveMeshContextWithUndo(idx);

            // 基準メッシュのGPU同期
            _toolContext?.SyncMesh?.Invoke();
        }

        _model?.OnListChanged?.Invoke();
    }
}
