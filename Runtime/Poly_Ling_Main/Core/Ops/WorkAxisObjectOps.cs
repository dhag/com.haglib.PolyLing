// WorkAxisObjectOps.cs
// 作業軸オブジェクト（MeshType.WorkAxis）の生成と、旧データからの移行。
//
// 【正典】
//   軸値（原点・回転・長さ）は MeshContext.WorkAxis が持つ。ModelContext は
//   軸値を持たず、使う 1 本の解決だけを ModelContext.ResolveWorkAxisObject で行う。
//
// 【空のオブジェクト】
//   MeshContext.HierarchyParentIndex などは MeshObject への委譲なので、
//   頂点 0・面 0 の MeshObject を必ず持たせる（PartsIdSplitInserter.cs:6-9 と同じ理由）。
//
// 【表示】
//   表示するかどうかは MeshContext.IsVisible を使う。WorkAxisContext.IsVisible は
//   旧データの移行でだけ読み、以後は参照しない。
//
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>作業軸オブジェクトの生成・移行。</summary>
    public static class WorkAxisObjectOps
    {
        /// <summary>作業軸オブジェクトの既定名。</summary>
        public const string DefaultName = "作業軸";

        /// <summary>
        /// 作業軸オブジェクトを 1 つ組む。モデルへの挿入は呼び出し側が行う。
        /// value を省くと既定値の軸になる。value は複製して持つ。
        /// </summary>
        public static MeshContext BuildContext(ModelContext model, string name, WorkAxisContext value = null)
        {
            string finalName = string.IsNullOrEmpty(name) ? DefaultName : name;
            if (model != null) finalName = model.GenerateUniqueMeshName(finalName);

            var mo = new MeshObject
            {
                Name                = finalName,
                Type                = MeshType.WorkAxis,
                ParentIndex         = -1,
                HierarchyParentIndex = -1,
                Depth               = 0,
            };

            if (mo.BoneTransform != null)
            {
                mo.BoneTransform.UseLocalTransform = false;
                mo.BoneTransform.Position          = Vector3.zero;
                mo.BoneTransform.Rotation          = Vector3.zero;
                mo.BoneTransform.Scale             = Vector3.one;
            }

            // 頂点 0 のときは空の Mesh がそのまま返る（MeshBridgeDefault.cs:50-51）。
            var unityMesh = mo.ToUnityMesh();
            unityMesh.name      = finalName;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshObject を先に入れないと Name の代入が捨てられる（MeshContext.cs:40-48）。
            var ctx = new MeshContext
            {
                MeshObject         = mo,
                Name               = finalName,
                UnityMesh          = unityMesh,
                IsVisible          = value?.IsVisible ?? true,
                ParentModelContext = model,
            };
            ctx.WorkAxis = value != null ? new WorkAxisContext(value) : new WorkAxisContext();
            return ctx;
        }

        /// <summary>
        /// 作業軸オブジェクトをモデルの末尾へ足し、アクティブにする。
        /// </summary>
        /// <returns>挿入した索引。model が null なら -1。</returns>
        public static int Append(ModelContext model, string name, WorkAxisContext value = null)
        {
            if (model == null) return -1;

            var ctx = BuildContext(model, name, value);
            int index = model.MeshContextList.Count;
            model.Insert(index, ctx);
            model.ActiveWorkAxisObjectId = ctx.ObjectId;
            return index;
        }

        /// <summary>
        /// 旧データ（モデル直下に 1 本だけ持っていた作業軸）を作業軸オブジェクトへ移す。
        /// すでに作業軸オブジェクトがあるモデルには何もしない。
        /// </summary>
        /// <returns>移行したら true。</returns>
        public static bool MigrateLegacy(ModelContext model, WorkAxisContext legacy)
        {
            if (model == null || legacy == null) return false;
            if (model.ResolveWorkAxisObject() != null) return false;

            return Append(model, DefaultName, legacy) >= 0;
        }
    }
}
