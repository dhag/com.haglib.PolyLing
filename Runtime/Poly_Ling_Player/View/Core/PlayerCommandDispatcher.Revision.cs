// PlayerCommandDispatcher.Revision.cs
// モデルの版と、1 回の呼び出しで増えた・消えたオブジェクトの割り出し。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【いつ動くか】
//   DispatchAuthorized の一番外側の呼び出しだけ。入れ子の呼び出しでは動かない。
//   実行前に安定 ID の集合を控え、実行後に比べる。
//
// 【版を進める条件】
//   コマンドが成功し、PLCommand.Writes が None 以外であること。
//   照会コマンド（Writes = None）では進まないので、「版が同じなら中身も同じ」を保てる。
//   Writes = Unspecified は宣言漏れなので、書き込むものとして扱う（進める）。

using System.Collections.Generic;
using System.Reflection;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>実行前のカレントモデルの安定 ID。一番外側の呼び出しだけが控える。</summary>
        private HashSet<ulong> _dispatchEntryObjectIds;

        /// <summary>カレントモデルの安定 ID を集める。モデルが無ければ null。</summary>
        private HashSet<ulong> CaptureObjectIds()
        {
            var model = _getProject()?.CurrentModel;
            if (model == null) return null;

            var ids = new HashSet<ulong>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) ids.Add(mc.ObjectId);
            }
            return ids;
        }

        /// <summary>コマンドがモデルを書き換えるか（PLCommand.Writes）。宣言が無いときは書き換える側に倒す。</summary>
        private static bool WritesModel(PanelCommand cmd)
        {
            if (cmd == null) return false;
            var attr = cmd.GetType().GetCustomAttribute<PLCommandAttribute>(inherit: false);
            return attr == null || attr.Writes != PLWriteScope.None;
        }

        /// <summary>
        /// 一番外側の呼び出しの最後に、版を進めて結果へ差分を入れる。
        /// 控えが無い（モデルが無かった）ときは何もしない。
        /// </summary>
        private void FinishRevision(PanelCommand cmd, CommandResult result)
        {
            var before = _dispatchEntryObjectIds;
            _dispatchEntryObjectIds = null;

            if (result == null || !result.Success) return;

            var model = _getProject()?.CurrentModel;
            if (model == null) return;

            if (WritesModel(cmd)) model.BumpRevision();
            result.ModelRevision = model.Revision;

            if (before == null) return;

            var after = CaptureObjectIds();
            if (after == null) return;

            var created = new List<ulong>();
            foreach (var id in after) if (!before.Contains(id)) created.Add(id);

            var deleted = new List<ulong>();
            foreach (var id in before) if (!after.Contains(id)) deleted.Add(id);

            created.Sort();
            deleted.Sort();

            if (created.Count > 0) result.CreatedObjectIds = created.ToArray();
            if (deleted.Count > 0) result.DeletedObjectIds = deleted.ToArray();
        }
    }
}
