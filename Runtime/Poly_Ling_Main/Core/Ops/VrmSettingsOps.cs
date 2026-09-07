// Runtime/Poly_Ling_Main/Core/Ops/VrmSettingsOps.cs
// ============================================================
// VRM 1.0 のモデルレベル設定と一人称指定のオーサリング処理
// ============================================================
//
// 【なぜ要るか】
//   VRM メタ情報は出力パネルが持つ Vrm10ExportSettings にしか入らず、
//   プロジェクトに残らなかった。視線（lookAt）と一人称（firstPerson）は
//   PolyLing 側に値そのものが無く、UniVRM の既定だけが出ていた。
//
// 【付帯先】
//   メタ情報と視線はモデルに 1 つずつ（ModelContext）。
//   一人称の扱いは描画オブジェクトごと（MeshObject.VrmFirstPerson）。
//   ボーンには一人称の意味がないので付けない（IsFirstPersonCarrier 参照）。
//
// 【null＝未設定】
//   ModelContext.VrmMeta / VrmLookAt は null が「未設定」。
//   未設定のまま出力すると VRM 側の既定が載る。値を作るのは本 Ops だけ。
//
// 【依存】
//   #if UNITY_EDITOR を含まない純ロジック。UnityEngine の型のみ使う。
//
// ============================================================

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>VRM のモデルレベル設定と一人称指定の付け外し。</summary>
    public static class VrmSettingsOps
    {
        // ================================================================
        // メタ情報
        // ================================================================

        /// <summary>メタ情報を書き込む（未設定なら作る）。書けたら true。</summary>
        public static bool SetMeta(ModelContext model, VrmMetaData meta)
        {
            if (model == null || meta == null) return false;
            model.VrmMeta = meta.Clone();
            return true;
        }

        /// <summary>メタ情報を未設定へ戻す。元から無ければ false。</summary>
        public static bool ClearMeta(ModelContext model)
        {
            if (model?.VrmMeta == null) return false;
            model.VrmMeta = null;
            return true;
        }

        /// <summary>
        /// 編集の出発点になるメタ情報。未設定ならモデル名を入れた新品を返す。
        /// 返り値はコピーなので、書き換えても model には影響しない。
        /// </summary>
        public static VrmMetaData GetMetaOrNew(ModelContext model)
        {
            if (model == null) return new VrmMetaData();
            if (model.VrmMeta != null) return model.VrmMeta.Clone();

            return new VrmMetaData { Name = model.Name ?? "" };
        }

        // ================================================================
        // 視線
        // ================================================================

        /// <summary>視線設定を書き込む（未設定なら作る）。書けたら true。</summary>
        public static bool SetLookAt(ModelContext model, VrmLookAtData lookAt)
        {
            if (model == null || lookAt == null) return false;
            model.VrmLookAt = lookAt.Clone();
            return true;
        }

        /// <summary>視線設定を未設定へ戻す。元から無ければ false。</summary>
        public static bool ClearLookAt(ModelContext model)
        {
            if (model?.VrmLookAt == null) return false;
            model.VrmLookAt = null;
            return true;
        }

        /// <summary>
        /// 編集の出発点になる視線設定。未設定なら VRM の既定値で作る。
        /// 返り値はコピー。
        /// </summary>
        public static VrmLookAtData GetLookAtOrNew(ModelContext model)
        {
            if (model?.VrmLookAt != null) return model.VrmLookAt.Clone();
            return new VrmLookAtData();
        }

        // ================================================================
        // 一人称カメラでの扱い（per-mesh）
        // ================================================================

        /// <summary>
        /// 一人称の指定を持てるか。描画オブジェクトだけが対象で、
        /// ボーン・モーフ・剛体などは対象外。
        /// </summary>
        public static bool IsFirstPersonCarrier(MeshContext mc)
        {
            if (mc?.MeshObject == null) return false;

            switch (mc.Type)
            {
                case MeshType.Mesh:
                case MeshType.BakedMirror:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>索引が一人称の指定を持てるか。</summary>
        public static bool IsFirstPersonCarrier(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return false;
            return IsFirstPersonCarrier(model.GetMeshContext(index));
        }

        /// <summary>
        /// 一人称の扱いを書き込む。書けた本数を返す。
        /// Auto を渡すと「指定なし」に戻る（保存にも出力にも出なくなる）。
        /// </summary>
        public static int SetFirstPerson(
            ModelContext model, IEnumerable<int> indices, VrmFirstPersonType type)
        {
            if (model == null || indices == null) return 0;

            int done = 0;
            foreach (int i in indices)
            {
                if (!IsFirstPersonCarrier(model, i)) continue;

                model.GetMeshContext(i).MeshObject.VrmFirstPerson = type;
                done++;
            }
            return done;
        }
    }
}
