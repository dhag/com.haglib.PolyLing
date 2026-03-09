// PolyLing_CommandHandlers_Model.cs
// モデルブレンドハンドラ
// DispatchPanelCommand から呼ばれる private メソッド群

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;

public partial class PolyLing
{
    // ================================================================
    // モデルブレンド
    // ================================================================

    private void HandleCreateBlendClone(CreateBlendCloneCommand cmd)
    {
        if (_project == null) return;
        var src = _project.GetModel(cmd.ModelIndex);
        if (src == null) return;

        string uniqueName = _project.GenerateUniqueModelName(
            string.IsNullOrEmpty(cmd.CloneNameBase)
                ? src.Name + "_blend"
                : cmd.CloneNameBase);

        var clone = DeepCloneModelContext(src, uniqueName);
        if (clone == null) return;
        _project.AddModel(clone);
    }

    private void HandleApplyModelBlend(ApplyModelBlendCommand cmd)
    {
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, cmd.RecalcNormals, cmd.BlendBones);
    }

    private void HandlePreviewModelBlend(PreviewModelBlendCommand cmd)
    {
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, recalcNormals: false, blendBones: cmd.BlendBones);
    }

    private void ExecuteBlend(int sourceModelIndex, int cloneModelIndex,
        float[] weights, bool[] meshEnabled, bool recalcNormals, bool blendBones)
    {
        if (_project == null) return;
        var cloneModel = _project.GetModel(cloneModelIndex);
        if (cloneModel == null) return;

        // ウェイト正規化
        float total = 0f;
        foreach (var w in weights) total += w;
        float[] nw = new float[weights.Length];
        if (total > 0f)
            for (int i = 0; i < weights.Length; i++) nw[i] = weights[i] / total;
        else
        {
            float eq = 1f / weights.Length;
            for (int i = 0; i < weights.Length; i++) nw[i] = eq;
        }

        // Step 1: ターゲット（clone）のフィルタ済みメッシュリスト
        // MirrorSide 除外・VertexCount==0 除外
        // drawableIdx = DrawableMeshes 上の元インデックス（meshEnabled の添字）を保持
        var cloneDrawables = cloneModel.DrawableMeshes;
        var targetEntries = new List<(int drawableIdx, TypedMeshEntry entry)>();
        for (int di = 0; di < cloneDrawables.Count; di++)
        {
            var e = cloneDrawables[di];
            if (e.Type == MeshType.MirrorSide) continue;
            if (e.Type == MeshType.BakedMirror) continue;
            if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
            targetEntries.Add((di, e));
        }

        // ターゲット各メッシュの展開前頂点数（MeshObject.VertexCount）と
        // 展開後頂点数（UnityMesh.vertexCount）を記録
        // ※展開後頂点数は UnityMesh が null の場合は展開前と同じとみなす
        var targetVertCountRaw      = targetEntries.Select(t => t.entry.MeshObject.VertexCount).ToArray();
        var targetVertCountExpanded = targetEntries.Select(t =>
            t.entry.Context.UnityMesh != null ? t.entry.Context.UnityMesh.vertexCount : t.entry.MeshObject.VertexCount
        ).ToArray();

        // Step 2: 各ソースモデルのフィルタ済みメッシュリスト（同条件）
        // モデルインデックス → フィルタ済みエントリリスト + 展開後頂点数リスト
        var srcFilteredMap  = new Dictionary<int, List<TypedMeshEntry>>();
        var srcExpCountsMap = new Dictionary<int, List<int>>();
        for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
        {
            if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
            var m = _project.GetModel(modelIdx);
            if (m == null) continue;
            var srcDrawables = m.DrawableMeshes;
            var filtered  = new List<TypedMeshEntry>();
            var expCounts = new List<int>();
            for (int di = 0; di < srcDrawables.Count; di++)
            {
                var e = srcDrawables[di];
                if (e.Type == MeshType.MirrorSide) continue;
                if (e.Type == MeshType.BakedMirror) continue;
                if ((e.MeshObject?.VertexCount ?? 0) == 0) continue;
                filtered.Add(e);
                int ec = e.Context.UnityMesh != null ? e.Context.UnityMesh.vertexCount : e.MeshObject.VertexCount;
                expCounts.Add(ec);
            }
            srcFilteredMap[modelIdx]  = filtered;
            srcExpCountsMap[modelIdx] = expCounts;
        }
        // ソースモデルごとのマッチングカーソル（先頭から順に頂点数一致で対応付け）
        var srcCursors = new Dictionary<int, int>();
        foreach (var key in srcFilteredMap.Keys) srcCursors[key] = 0;

        // Step 3 & 4: メッシュ対応表でブレンド計算
        for (int k = 0; k < targetEntries.Count; k++)
        {
            // meshEnabled は DrawableMeshes 上のインデックス基準
            int drawableIdx = targetEntries[k].drawableIdx;
            if (drawableIdx < meshEnabled.Length && !meshEnabled[drawableIdx]) continue;

            var targetEntry = targetEntries[k].entry;

            var targetMesh = targetEntry.MeshObject;
            int rawCount   = targetVertCountRaw[k];
            int expCount   = targetVertCountExpanded[k];

            // 孤立頂点を除外するセット（target の Vertices インデックス基準）
            var nonIsolated = BuildBlendNonIsolatedSet(targetMesh);

            Debug.Log($"[Blend] k={k} name={targetEntry.Context.Name} rawCount={rawCount} expCount={expCount} nonIsolated={nonIsolated.Count}");
            foreach (var kv2 in srcFilteredMap)
            {
                var sc = srcExpCountsMap[kv2.Key];
                string kVal = k < sc.Count ? sc[k].ToString() : "OOB";
                Debug.Log($"[Blend]   srcModel[{kv2.Key}] srcList.Count={kv2.Value.Count} sc[k]={kVal}");
            }

            var blended = new Vector3[rawCount];

            bool targetIsExpanded = targetMesh.IsExpanded;
            if (targetIsExpanded)
            {
                foreach (var kv in srcFilteredMap)
                {
                    float w = nw[kv.Key];
                    var srcList = kv.Value;
                    var srcExpCounts = srcExpCountsMap[kv.Key];
                    int cursor = srcCursors[kv.Key];
                    int matchSi = -1;
                    for (int si = cursor; si < srcExpCounts.Count; si++)
                    {
                        if (srcExpCounts[si] == expCount) { matchSi = si; break; }
                    }
                    if (matchSi < 0) continue;
                    srcCursors[kv.Key] = matchSi + 1;
                    var srcMesh = srcList[matchSi].MeshObject;
                    bool srcIsExpanded = srcMesh.IsExpanded;
                    var srcInvMap = srcIsExpanded ? null : srcMesh.BuildInverseExpansionMap();

                    for (int vi = 0; vi < rawCount; vi++)
                    {
                        if (!nonIsolated.Contains(vi)) continue;
                        Vector3 srcPos;
                        if (srcIsExpanded)
                        {
                            if (vi >= srcMesh.Vertices.Count) continue;
                            srcPos = srcMesh.Vertices[vi].Position;
                        }
                        else
                        {
                            if (!srcInvMap.TryGetValue(vi, out var r)) continue;
                            srcPos = srcMesh.Vertices[r.vIdx].Position;
                        }
                        blended[vi] += srcPos * w;
                    }
                }
            }
            else
            {
                foreach (var kv in srcFilteredMap)
                {
                    float w = nw[kv.Key];
                    var srcList = kv.Value;
                    var srcExpCounts2 = srcExpCountsMap[kv.Key];
                    int cursor2 = srcCursors[kv.Key];
                    int matchSi2 = -1;
                    for (int si = cursor2; si < srcExpCounts2.Count; si++)
                    {
                        if (srcExpCounts2[si] == expCount) { matchSi2 = si; break; }
                    }
                    if (matchSi2 < 0) continue;
                    srcCursors[kv.Key] = matchSi2 + 1;
                    var srcMesh = srcList[matchSi2].MeshObject;
                    bool srcIsExpanded = srcMesh.IsExpanded;
                    var srcExpMap = srcIsExpanded ? targetMesh.BuildExpansionMap() : null;

                    for (int vi = 0; vi < rawCount; vi++)
                    {
                        if (!nonIsolated.Contains(vi)) continue;
                        Vector3 srcPos;
                        if (srcIsExpanded)
                        {
                            if (!srcExpMap.TryGetValue((vi, 0), out int srcEi)) continue;
                            if (srcEi >= srcMesh.Vertices.Count) continue;
                            srcPos = srcMesh.Vertices[srcEi].Position;
                        }
                        else
                        {
                            if (vi >= srcMesh.Vertices.Count) continue;
                            srcPos = srcMesh.Vertices[vi].Position;
                        }
                        blended[vi] += srcPos * w;
                    }
                }
            }

            // 書き戻し（孤立頂点は blended[vi]==Vector3.zero のまま → 元位置を維持するため書き戻さない）
            for (int vi = 0; vi < rawCount; vi++)
            {
                if (!nonIsolated.Contains(vi)) continue;
                targetMesh.Vertices[vi].Position = blended[vi];
            }

            if (recalcNormals)
                targetMesh.RecalculateSmoothNormals();

            _toolContext?.SyncMeshContextPositionsOnly?.Invoke(targetEntry.Context);
        }

        // Step 5: ミラー同期（Real ブレンド後にミラー側へ反映）
        // MirrorPairs経由（PMX・再インポート済みMQO）
        var syncedRealContexts = new HashSet<MeshContext>();
        foreach (var pair in cloneModel.MirrorPairs)
        {
            if (!pair.IsValid) continue;
            pair.SyncPositions();
            if (recalcNormals) pair.SyncNormals();
            _toolContext?.SyncMeshContextPositionsOnly?.Invoke(pair.Real);
            _toolContext?.SyncMeshContextPositionsOnly?.Invoke(pair.Mirror);
            syncedRealContexts.Add(pair.Real);
        }

        // フォールバック: MirrorPairsに含まれないMirrorSideをName+"+"で直接同期（MQO既存インポート対応）
        foreach (var (_, targetEntry) in targetEntries)
        {
            var realCtx = targetEntry.Context;
            if (syncedRealContexts.Contains(realCtx)) continue;
            string mirrorName = realCtx.Name + "+";
            var axis = realCtx.GetMirrorSymmetryAxis();
            var realMo = realCtx.MeshObject;

            for (int i = 0; i < cloneModel.MeshContextCount; i++)
            {
                var mc = cloneModel.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.MirrorSide) continue;
                if (mc.Name != mirrorName) continue;
                if (mc.MeshObject == null || mc.MeshObject.VertexCount != realMo.VertexCount) continue;

                for (int vi = 0; vi < realMo.VertexCount; vi++)
                {
                    var p = realMo.Vertices[vi].Position;
                    mc.MeshObject.Vertices[vi].Position = axis switch
                    {
                        Poly_Ling.Symmetry.SymmetryAxis.X => new Vector3(-p.x, p.y, p.z),
                        Poly_Ling.Symmetry.SymmetryAxis.Y => new Vector3(p.x, -p.y, p.z),
                        Poly_Ling.Symmetry.SymmetryAxis.Z => new Vector3(p.x, p.y, -p.z),
                        _ => new Vector3(-p.x, p.y, p.z),
                    };
                }
                _toolContext?.SyncMeshContextPositionsOnly?.Invoke(mc);
                break;
            }
        }

        // Step 6: ボーンブレンド（名前照合・位置補間 → WorldMatrix/BindPose 再計算）
        if (blendBones && cloneModel.BoneCount > 0)
        {
            // クローンのボーンコンテキストを 名前 → インデックス でマップ
            var cloneBoneByName = new Dictionary<string, MeshContext>();
            for (int i = 0; i < cloneModel.MeshContextCount; i++)
            {
                var mc = cloneModel.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (!string.IsNullOrEmpty(mc.Name))
                    cloneBoneByName[mc.Name] = mc;
            }

            // ソースモデルのボーン名 → Position マップ（ウェイト > 0 かつボーンありのみ）
            var srcBoneMaps = new Dictionary<int, Dictionary<string, Vector3>>();
            for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length || nw[modelIdx] <= 0f) continue;
                var srcM = _project.GetModel(modelIdx);
                if (srcM == null || srcM.BoneCount == 0) continue;
                var bmap = new Dictionary<string, Vector3>();
                for (int i = 0; i < srcM.MeshContextCount; i++)
                {
                    var mc = srcM.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    if (!string.IsNullOrEmpty(mc.Name) && mc.BoneTransform != null)
                        bmap[mc.Name] = mc.BoneTransform.Position;
                }
                if (bmap.Count > 0)
                    srcBoneMaps[modelIdx] = bmap;
            }

            // 各クローンボーンの位置を加重平均でブレンド
            foreach (var kv in cloneBoneByName)
            {
                var cloneBoneCtx = kv.Value;
                if (cloneBoneCtx.BoneTransform == null) continue;

                Vector3 blendedPos = Vector3.zero;
                float totalW = 0f;
                foreach (var srcKv in srcBoneMaps)
                {
                    if (!srcKv.Value.TryGetValue(kv.Key, out Vector3 srcPos)) continue;
                    float w = nw[srcKv.Key];
                    blendedPos += srcPos * w;
                    totalW += w;
                }
                if (totalW > 0f)
                    cloneBoneCtx.BoneTransform.Position = blendedPos / totalW;
            }

            // WorldMatrix と BindPose を再計算
            cloneModel.ComputeWorldAndBindPoses();

            // GPU バッファに通知（トポロジ変更扱いでフルリビルド）
            _toolContext?.NotifyTopologyChanged?.Invoke();
        }
    }

    /// <summary>
    /// いずれかの Face に参照されている頂点インデックスのセットを返す（孤立頂点除外用）
    /// </summary>
    private static HashSet<int> BuildBlendNonIsolatedSet(MeshObject mo)
    {
        var set = new HashSet<int>();
        foreach (var face in mo.Faces)
            foreach (int vi in face.VertexIndices)
                set.Add(vi);
        return set;
    }

    // ================================================================
    // ModelContext ディープコピー
    // ================================================================

    /// <summary>
    /// ModelContext をディープコピーする（BindPose 等 DTO 非保存フィールドを含む）
    /// </summary>
    private static ModelContext DeepCloneModelContext(ModelContext src, string newName)
    {
        var dst = new ModelContext { Name = newName };

        for (int i = 0; i < src.MeshContextCount; i++)
        {
            var s = src.GetMeshContext(i);
            if (s == null) continue;

            var meshObj = s.MeshObject?.Clone();
            if (meshObj == null) continue;

            var d = new MeshContext
            {
                Name                   = s.Name,
                MeshObject             = meshObj,
                UnityMesh              = meshObj.ToUnityMesh(),
                OriginalPositions      = (Vector3[])meshObj.Positions.Clone(),
                BoneTransform          = CloneBoneTransform(s.BoneTransform),
                // 階層
                ParentIndex            = s.ParentIndex,
                Depth                  = s.Depth,
                HierarchyParentIndex   = s.HierarchyParentIndex,
                // 表示
                IsVisible              = s.IsVisible,
                IsLocked               = s.IsLocked,
                IsFolding              = s.IsFolding,
                // ミラー
                MirrorType             = s.MirrorType,
                MirrorAxis             = s.MirrorAxis,
                MirrorDistance         = s.MirrorDistance,
                MirrorMaterialOffset   = s.MirrorMaterialOffset,
                // ベイクミラー
                BakedMirrorSourceIndex = s.BakedMirrorSourceIndex,
                HasBakedMirrorChild    = s.HasBakedMirrorChild,
                // モーフ
                MorphParentIndex       = s.MorphParentIndex,
                // BindPose（DTOに保存されないため直接コピー必須）
                BindPose               = s.BindPose,
                // BonePoseData / MorphBaseData
                BonePoseData           = s.BonePoseData?.Clone(),
                MorphBaseData          = s.MorphBaseData?.Clone(),
            };

            dst.Add(d);
        }

        if (src.MaterialReferences != null)
            foreach (var m in src.MaterialReferences)
                dst.MaterialReferences.Add(m);
        dst.CurrentMaterialIndex = src.CurrentMaterialIndex;

        if (src.DefaultMaterialReferences != null)
            foreach (var m in src.DefaultMaterialReferences)
                dst.DefaultMaterialReferences.Add(m);
        dst.DefaultCurrentMaterialIndex = src.DefaultCurrentMaterialIndex;
        dst.AutoSetDefaultMaterials     = src.AutoSetDefaultMaterials;

        if (src.MirrorPairs != null)
        {
            foreach (var sp in src.MirrorPairs)
            {
                int ri = src.IndexOf(sp.Real);
                int mi = src.IndexOf(sp.Mirror);
                if (ri < 0 || mi < 0 || ri >= dst.Count || mi >= dst.Count) continue;
                var pair = new MirrorPair
                {
                    Real   = dst.GetMeshContext(ri),
                    Mirror = dst.GetMeshContext(mi),
                    Axis   = sp.Axis,
                };
                if (pair.Build())
                    dst.MirrorPairs.Add(pair);
            }
        }

        return dst;
    }

    private static BoneTransform CloneBoneTransform(BoneTransform src)
    {
        if (src == null) return new BoneTransform();
        var dst = new BoneTransform();
        dst.CopyFrom(src);
        return dst;
    }
}
