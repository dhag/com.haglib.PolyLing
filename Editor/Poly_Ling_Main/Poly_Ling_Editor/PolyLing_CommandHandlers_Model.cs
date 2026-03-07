// PolyLing_CommandHandlers_Model.cs
// モデルブレンドハンドラ
// DispatchPanelCommand から呼ばれる private メソッド群

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
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, cmd.RecalcNormals);
    }

    private void HandlePreviewModelBlend(PreviewModelBlendCommand cmd)
    {
        ExecuteBlend(cmd.ModelIndex, cmd.CloneModelIndex, cmd.Weights, cmd.MeshEnabled, recalcNormals: false);
    }

    private void ExecuteBlend(int sourceModelIndex, int cloneModelIndex,
        float[] weights, bool[] meshEnabled, bool recalcNormals)
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

        var cloneDrawables = cloneModel.DrawableMeshes;
        int drawableCount  = cloneDrawables.Count;

        for (int drawIdx = 0; drawIdx < drawableCount; drawIdx++)
        {
            if (drawIdx < meshEnabled.Length && !meshEnabled[drawIdx]) continue;

            var targetMesh = cloneDrawables[drawIdx].Context?.MeshObject;
            if (targetMesh == null) continue;

            var blended = new Vector3[targetMesh.VertexCount];

            for (int modelIdx = 0; modelIdx < _project.ModelCount; modelIdx++)
            {
                if (modelIdx >= nw.Length) continue;
                float w = nw[modelIdx];
                if (w <= 0f) continue;

                var srcModel     = _project.GetModel(modelIdx);
                var srcDrawables = srcModel?.DrawableMeshes;
                if (srcDrawables == null || drawIdx >= srcDrawables.Count) continue;

                var srcMesh = srcDrawables[drawIdx].Context?.MeshObject;
                if (srcMesh == null) continue;

                int vCount = Mathf.Min(blended.Length, srcMesh.VertexCount);
                for (int vi = 0; vi < vCount; vi++)
                    blended[vi] += srcMesh.Vertices[vi].Position * w;
            }

            for (int vi = 0; vi < blended.Length; vi++)
                targetMesh.Vertices[vi].Position = blended[vi];

            if (recalcNormals)
                targetMesh.RecalculateSmoothNormals();
        }
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
