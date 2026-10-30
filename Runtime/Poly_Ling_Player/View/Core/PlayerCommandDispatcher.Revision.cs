// PlayerCommandDispatcher.Revision.cs
// モデルの版と、1 回の呼び出しで増えた・消えたオブジェクトの割り出し。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【いつ動くか】
//   DispatchAuthorized の一番外側の呼び出しだけ。入れ子の呼び出しでは動かない。
//   実行前に安定 ID の集合を控え、実行後に比べる。
//
// 【控えはモデルごと】（作り直し整備計画 段 9）
//   プロジェクトの全モデルについて、モデルの参照と ID の集合の組を控え、同じモデル同士で比べる。
//   以前はカレントモデルだけを実行前・実行後に取っていたので、カレントを替えるコマンド
//   （シナリオ先頭の createEmptyModel など）では、前のモデルの全 ID が消えた扱いになっていた。
//   新しく増えたモデルの ID は created、無くなったモデルの ID は deleted に入れる。
//
// 【版を進める条件】
//   コマンドが成功し、PLCommand.Writes が None 以外であること。
//   照会コマンド（Writes = None）では進まないので、「版が同じなら中身も同じ」を保てる。
//   Writes = Unspecified は宣言漏れなので、書き込むものとして扱う（進める）。
//   版はカレントモデルのもの。

using System.Collections.Generic;
using System.Reflection;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>実行前の、モデルごとの安定 ID。一番外側の呼び出しだけが控える。</summary>
        private Dictionary<ModelContext, HashSet<ulong>> _dispatchEntryObjectIds;

        /// <summary>プロジェクトの全モデルの安定 ID をモデルごとに集める。プロジェクトが無ければ null。</summary>
        private Dictionary<ModelContext, HashSet<ulong>> CaptureObjectIds()
        {
            var project = _getProject();
            if (project == null) return null;

            var map = new Dictionary<ModelContext, HashSet<ulong>>();
            for (int m = 0; m < project.ModelCount; m++)
            {
                var model = project.GetModel(m);
                if (model == null || map.ContainsKey(model)) continue;
                map[model] = CaptureObjectIds(model);
            }
            return map;
        }

        /// <summary>モデル 1 つの安定 ID を集める。</summary>
        private static HashSet<ulong> CaptureObjectIds(ModelContext model)
        {
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
        /// 控えが無い（プロジェクトが無かった）ときは差分を入れない。
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
            var deleted = new List<ulong>();

            foreach (var kv in after)
            {
                if (before.TryGetValue(kv.Key, out var prev))
                {
                    foreach (var id in kv.Value) if (!prev.Contains(id)) created.Add(id);
                    foreach (var id in prev) if (!kv.Value.Contains(id)) deleted.Add(id);
                }
                else
                {
                    // 新しく増えたモデル
                    created.AddRange(kv.Value);
                }
            }
            foreach (var kv in before)
                if (!after.ContainsKey(kv.Key)) deleted.AddRange(kv.Value);   // 無くなったモデル

            created.Sort();
            deleted.Sort();

            if (created.Count > 0) result.CreatedObjectIds = created.ToArray();
            if (deleted.Count > 0) result.DeletedObjectIds = deleted.ToArray();
        }
    }
}
