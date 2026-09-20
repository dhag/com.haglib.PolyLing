// PanelCommandRouter.cs
// パネルが発行する PanelCommand を、サーバの command プロトコル（action と params）へ変換して送信する。
//
// 【変換は汎用】
//   action 名は PanelCommandFactory.ActionOf、引数は PanelCommandFactory.ToArgs で作る。
//   サーバは PanelCommandFactory.Create で同じ規則のまま組み立て直す
//   （RemoteServerCore.BuildPanelCommand）。コマンド種別ごとの手書きの変換表は持たない
//   （操作経路統一計画.md A）。
//
// 【objectIds】
//   サーバの担当者判定は、主モデルの書き込み先（RemoteOwnership.TryCollectWriteTargets の
//   primary と同じ並び）と封筒の objectIds を照合し、リスト構造のズレを検出する。
//   クライアントも同じ関数で書き込み先を集め、その並びで安定 ID を添える。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Player;
using Poly_Ling.Remote;

namespace Poly_Ling.ListClient
{
    public sealed class PanelCommandRouter
    {
        private readonly PolyLingPlayerClient _client;

        /// <summary>
        /// (modelIndex, masterIndex) → 安定ObjectId を返す解決子。
        /// 未設定（null）なら objectIds を添えない（サーバは照合をスキップする）。
        /// </summary>
        public System.Func<int, int, ulong> ResolveObjectId;

        /// <summary>
        /// 受信済みの ProjectContext を返す。書き込み先を集めるのに使う。
        /// 未設定（null）なら objectIds を添えない。
        /// </summary>
        public System.Func<ProjectContext> GetProject;

        public PanelCommandRouter(PolyLingPlayerClient client)
        {
            _client = client;
        }

        public void Send(PanelCommand cmd)
        {
            if (_client == null || cmd == null || !_client.IsConnected) return;

            string action = PanelCommandFactory.ActionOf(cmd.GetType());
            if (string.IsNullOrEmpty(action)) return;

            var args = new Dictionary<string, string>(PanelCommandFactory.ToArgs(cmd));

            string ids = ObjectIdCsv(cmd);
            if (!string.IsNullOrEmpty(ids)) args["objectIds"] = ids;

            _client.SendCommand(action, cmd.ModelIndex, args);
        }

        /// <summary>
        /// サーバの判定と同じ規則で主モデルの書き込み先を集め、その並びの安定 ID を CSV にする。
        /// 集められない（Targets 以外、-1 を含む、プロジェクト未受信など）ときは空。
        /// </summary>
        private string ObjectIdCsv(PanelCommand cmd)
        {
            if (ResolveObjectId == null || GetProject == null) return "";
            var project = GetProject();
            if (project == null) return "";
            if (RemoteOwnership.WriteScopeOf(cmd) != PLWriteScope.Targets) return "";
            if (!RemoteOwnership.TryCollectWriteTargets(project, cmd, out var primary, out _)) return "";
            if (primary.Count == 0) return "";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < primary.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(ResolveObjectId(cmd.ModelIndex, primary[i]));
            }
            return sb.ToString();
        }
    }
}
