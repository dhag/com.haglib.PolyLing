// HierarchyReparentOps.cs
// 親を付け替えたとき、ワールド姿勢を保つようにローカル姿勢を組み直す。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【なぜ要るか】
//   MeshListOps.ReorderMeshes は HierarchyParentIndex を書き換えるだけで、
//   BoneTransform には触れていなかった。一方 ModelContext.ComputeWorldMatrices は
//     h = 親のワールド × 自身の LocalMatrix
//   と積む（ModelContext.cs:1746-1748）ので、親が付いた瞬間に自身のローカル値が
//   親の座標系で再解釈され、ワールド位置が親のぶんだけ飛ぶ。
//
//   Unity の Transform.SetParent(parent, worldPositionStays: true) が行う
//   「ローカルを付け替え先の座標系で組み直す」処理が抜けていた。
//
// 【組み直しの式】
//   新ローカル = inverse(新親のワールド) × 旧親のワールド × 旧ローカル
//   旧親が無いとき（ルートだった）は旧親のワールドを単位行列として扱う。
//
// 【親から子へ順に処理する理由】
//   親のローカルを直すと子のワールドが動く。子を先に直すと、そのあと親を直した
//   ぶんだけ子がずれる。トポロジカル順（親が先）に回す。
//
// 【触らないもの】
//   ・BonePoseData … 姿勢とは別レイヤ。LocalMatrix = base × pose なので base 側だけ直す。
//   ・生成ミラー側（MirrorGeometryDerived）… 自前の姿勢を持たず実体側から引き写す
//     （ModelContext.cs:1729 の SyncDerivedMirrorTransforms）。
//
// 【剪断が出る場合】
//   行列から TRS を戻すには、回転が直交で剪断が無いことが要る。
//   親子で非一様スケールと回転が混ざると剪断が出て厳密には戻せない。
//   そのときは姿勢を変えずに警告だけ返す（黙って近似すると原因追跡ができない）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>親の付け替えでワールド姿勢を保つ。</summary>
    public static class HierarchyReparentOps
    {
        /// <summary>剪断の判定に使う許容。</summary>
        private const float ShearEpsilon = 1e-3f;

        /// <summary>
        /// 付け替え前のワールド行列を控える。
        /// 親を書き換える「前」に呼ぶこと。
        /// </summary>
        public static Dictionary<MeshContext, Matrix4x4> CaptureWorld(ModelContext model)
        {
            var map = new Dictionary<MeshContext, Matrix4x4>();
            if (model == null) return map;

            model.ComputeWorldMatrices();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                map[mc] = mc.WorldMatrix;
            }
            return map;
        }

        /// <summary>
        /// 控えたワールド行列に合わせてローカル姿勢を組み直す。
        /// 親を書き換えた「後」に呼ぶこと。
        /// </summary>
        /// <param name="model">対象モデル。</param>
        /// <param name="beforeWorld">CaptureWorld の戻り値。</param>
        /// <param name="targets">組み直す対象。null なら全件。</param>
        /// <param name="warnings">剪断などで組み直せなかったものの説明。</param>
        /// <returns>実際に組み直した数。</returns>
        public static int RestoreWorld(
            ModelContext model,
            Dictionary<MeshContext, Matrix4x4> beforeWorld,
            ICollection<MeshContext> targets,
            List<string> warnings)
        {
            if (model == null || beforeWorld == null) return 0;

            int changed = 0;

            // 親から子へ。親のローカルを直すと子のワールドが動くので順序が要る。
            var order = model.TopologicalSortByHierarchy();

            // 親のワールドを持ち回る。対象ごとに ComputeWorldMatrices を呼ぶと
            // 部位数の 2 乗で効いてくるので、1 パスで積み上げる。
            // 添字は MeshContextList の位置。
            var world = new Matrix4x4[model.MeshContextCount];
            for (int i = 0; i < world.Length; i++) world[i] = Matrix4x4.identity;

            foreach (int index in order)
            {
                var mc = model.GetMeshContext(index);
                if (mc == null) continue;

                int pi = mc.HierarchyParentIndex;
                Matrix4x4 parentWorld = (pi >= 0 && pi < world.Length)
                    ? world[pi]
                    : Matrix4x4.identity;

                // 対象外・生成ミラーは姿勢を変えず、現在のローカルで積むだけ。
                //
                // out var を || の中で使うと、短絡で代入されない経路が出るため
                // コンパイラが「未代入」と判断する。取り出しは条件から分ける。
                bool hasWant = beforeWorld.TryGetValue(mc, out Matrix4x4 wantWorld);
                bool skip = !hasWant
                         || mc.MirrorGeometryDerived
                         || (targets != null && !targets.Contains(mc));

                if (skip)
                {
                    world[index] = parentWorld * mc.LocalMatrix;
                    continue;
                }

                Matrix4x4 wantLocal = parentWorld.inverse * wantWorld;

                // ポーズが乗っているときは base だけを直す。
                if (mc.BonePoseData != null && mc.BonePoseData.IsActive)
                    wantLocal = wantLocal * mc.BonePoseData.LocalMatrix.inverse;

                if (!TryDecompose(wantLocal, out var pos, out var rotEuler, out var scale)
                    || mc.BoneTransform == null)
                {
                    if (mc.BoneTransform != null)
                        warnings?.Add($"{mc.Name}: 剪断が出るため姿勢を組み直せません（親子で回転と非一様拡大が混ざっています）");

                    // 組み直せなかったものは現在のローカルのまま積む。
                    world[index] = parentWorld * mc.LocalMatrix;
                    continue;
                }

                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position = pos;
                mc.BoneTransform.Rotation = rotEuler;
                mc.BoneTransform.Scale    = scale;
                changed++;

                // 直したローカルで積む。子はこれを親として見る。
                world[index] = parentWorld * mc.LocalMatrix;
            }

            model.ComputeWorldMatrices();
            return changed;
        }

        /// <summary>
        /// 行列を TRS へ分解する。剪断が入っていたら false。
        ///
        /// 各軸の基底ベクトルの長さを拡大とし、正規化した基底から回転を作る。
        /// 正規化後の基底が直交していなければ剪断なので分解できない。
        /// </summary>
        public static bool TryDecompose(
            Matrix4x4 m, out Vector3 position, out Vector3 rotationEuler, out Vector3 scale)
        {
            position      = m.GetColumn(3);
            rotationEuler = Vector3.zero;
            scale         = Vector3.one;

            Vector3 ax = m.GetColumn(0);
            Vector3 ay = m.GetColumn(1);
            Vector3 az = m.GetColumn(2);

            float sx = ax.magnitude, sy = ay.magnitude, sz = az.magnitude;
            if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f) return false;

            Vector3 nx = ax / sx, ny = ay / sy, nz = az / sz;

            if (Mathf.Abs(Vector3.Dot(nx, ny)) > ShearEpsilon) return false;
            if (Mathf.Abs(Vector3.Dot(ny, nz)) > ShearEpsilon) return false;
            if (Mathf.Abs(Vector3.Dot(nz, nx)) > ShearEpsilon) return false;

            // 左手系の反転（det < 0）は X をひっくり返して表す。
            if (Vector3.Dot(Vector3.Cross(nx, ny), nz) < 0f)
            {
                nx = -nx;
                sx = -sx;
            }

            rotationEuler = Quaternion.LookRotation(nz, ny).eulerAngles;
            scale         = new Vector3(sx, sy, sz);
            return true;
        }
    }
}
