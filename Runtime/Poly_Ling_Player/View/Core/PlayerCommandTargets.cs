// PlayerCommandTargets.cs
// コマンドが指す対象オブジェクトと、実行時点の選択が一致するかを確かめる。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【なぜ要るか】
//   位相編集系のコマンドは「どのオブジェクトに効くか」を MasterIndices で持つが、
//   実処理（FaceMergeTool ほか）は ModelContext の選択を自分で走査する。
//   コマンドの値で選択を書き換えると、ユーザーの選択が副作用で変わる。
//   そのため書き換えではなく照合にし、一致しなければ受け口が失敗理由を返す。
//
//   照合の規則は全コマンドで同じなので、ハンドラごとに同じ判定を持たない。

using System.Collections.Generic;
using Poly_Ling.Context;

namespace Poly_Ling.Player
{
    /// <summary>コマンドの対象指定と実行時点の選択を照合する。</summary>
    public static class PlayerCommandTargets
    {
        /// <summary>
        /// masterIndices が、実行時点の「選択中の描画オブジェクト」と
        /// 集合として一致するかを確かめる。並び順は問わない。
        /// </summary>
        /// <param name="reason">一致しなかった理由。一致したときは null。</param>
        public static bool MatchesSelectedDrawables(
            ModelContext model, int[] masterIndices, out string reason)
        {
            reason = null;

            if (model == null) { reason = "モデルがありません"; return false; }

            if (masterIndices == null || masterIndices.Length == 0)
            {
                reason = "MasterIndices が空です";
                return false;
            }

            var selected = model.SelectedDrawableMeshIndices;
            if (selected == null || selected.Count == 0)
            {
                reason = "選択中の描画オブジェクトがありません";
                return false;
            }

            var want = new HashSet<int>(masterIndices);
            var have = new HashSet<int>(selected);

            if (want.Count != masterIndices.Length)
            {
                reason = "MasterIndices に重複があります";
                return false;
            }

            if (!want.SetEquals(have))
            {
                reason = "MasterIndices が選択中の描画オブジェクトと一致しません"
                       + $"（指定 [{Join(masterIndices)}] / 選択 [{Join(selected)}]）。"
                       + "先に SelectMeshCommand で選択を合わせてください";
                return false;
            }

            return true;
        }

        /// <summary>
        /// masterIndices が 1 個で、それが編集対象メッシュ（ActiveMeshContext）と
        /// 一致するかを確かめる。単一メッシュにしか効かない実処理のために使う。
        /// </summary>
        /// <param name="reason">一致しなかった理由。一致したときは null。</param>
        public static bool MatchesActiveMesh(
            ModelContext model, int[] masterIndices, out string reason)
        {
            reason = null;

            if (model == null) { reason = "モデルがありません"; return false; }

            var mc = model.ActiveMeshContext;
            if (mc?.MeshObject == null) { reason = "編集対象メッシュがありません"; return false; }

            if (masterIndices == null || masterIndices.Length != 1)
            {
                reason = "MasterIndices は 1 個で指定してください";
                return false;
            }

            int activeMaster = model.IndexOf(mc);
            if (masterIndices[0] != activeMaster)
            {
                reason = $"masterIndex {masterIndices[0]} は編集対象（{activeMaster}）ではありません";
                return false;
            }

            return true;
        }

        private static string Join(IEnumerable<int> values)
        {
            return string.Join(",", values);
        }
    }
}
