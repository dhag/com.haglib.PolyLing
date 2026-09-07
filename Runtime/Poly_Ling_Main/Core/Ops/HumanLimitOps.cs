// Runtime/Poly_Ling_Main/Core/Ops/HumanLimitOps.cs
// ============================================================
// Humanoid マッスル可動域（HumanLimitData）のオーサリング処理
// ============================================================
//
// 【なぜ要るか】
//   MeshObject.HumanLimit を書き込む経路が、既存 Avatar から取り込む
//   HierarchyImportWindow しか無かった。PolyLing の中で可動域を作る・直す・
//   既定へ戻す手段が画面にもコマンドにも無く、格納だけがある状態だった。
//
// 【付帯先はボーンだけ】
//   格納規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
//   可動域は Unity Humanoid のマッスル軸に対応する量なので、
//   揺れデータと違い MeshType.Bone 以外には付けない。
//
// 【単位】
//   本 Ops が受け取る Min / Max / Center はラジアン。HumanLimitData の
//   規約（HumanLimitData.cs「単位・座標系」）に合わせてある。
//   度で見せるのは画面側の仕事で、変換はコマンドのディスパッチャで行う。
//
// 【既定へ戻す＝null】
//   HumanLimit == null が「Unity 既定を使う」の正本表現。
//   ClearLimit は null を書き戻す。UseDefaultValues == true のデータは
//   既存 Avatar からの取り込みで入ってくるので、読む側は両方を既定として扱う
//   （UnityClipApplier の可動端差し替えも !UseDefaultValues を条件にしている）。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。UnityEngine の型のみ使う。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>Humanoid マッスル可動域の付け外し。</summary>
    public static class HumanLimitOps
    {
        // ================================================================
        // 付帯先の判定
        // ================================================================

        /// <summary>可動域を付けられるか。ボーンだけが対象。</summary>
        public static bool IsCarrier(MeshContext mc)
        {
            if (mc?.MeshObject == null) return false;
            return mc.Type == MeshType.Bone;
        }

        /// <summary>索引が付帯先になれるか。</summary>
        public static bool IsCarrier(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return false;
            return IsCarrier(model.GetMeshContext(index));
        }

        // ================================================================
        // 書き込み
        // ================================================================

        /// <summary>
        /// 選んだボーンへ可動域を書き込む（既にあれば上書きする）。
        /// min / max / center はラジアン。axisLength は角度ではないので変換しない。
        /// 書けた本数を返す。
        /// </summary>
        public static int SetLimit(
            ModelContext model, IEnumerable<int> indices,
            Vector3 minRad, Vector3 maxRad, Vector3 centerRad, float axisLength)
        {
            if (model == null || indices == null) return 0;

            int done = 0;
            foreach (int i in indices)
            {
                if (!IsCarrier(model, i)) continue;

                model.GetMeshContext(i).MeshObject.HumanLimit = new HumanLimitData
                {
                    Min              = minRad,
                    Max              = maxRad,
                    Center           = centerRad,
                    AxisLength       = Mathf.Max(0f, axisLength),
                    UseDefaultValues = false,
                };
                done++;
            }
            return done;
        }

        /// <summary>
        /// 可動域を外して Unity 既定へ戻す。外せた本数を返す。
        /// 元から持たないボーンは数えない。
        /// </summary>
        public static int ClearLimit(ModelContext model, IEnumerable<int> indices)
        {
            if (model == null || indices == null) return 0;

            int done = 0;
            foreach (int i in indices)
            {
                if (!IsCarrier(model, i)) continue;

                var mo = model.GetMeshContext(i).MeshObject;
                if (mo.HumanLimit == null) continue;

                mo.HumanLimit = null;
                done++;
            }
            return done;
        }

        // ================================================================
        // 読み出し
        // ================================================================

        /// <summary>
        /// 可動域を持ち、かつ既定を外しているか。
        /// 画面表示と UnityClipApplier の差し替え条件をそろえるための共通判定。
        /// </summary>
        public static bool HasCustomLimit(ModelContext model, int index)
        {
            if (!IsCarrier(model, index)) return false;
            var hl = model.GetMeshContext(index).MeshObject.HumanLimit;
            return hl != null && !hl.UseDefaultValues;
        }
    }
}
