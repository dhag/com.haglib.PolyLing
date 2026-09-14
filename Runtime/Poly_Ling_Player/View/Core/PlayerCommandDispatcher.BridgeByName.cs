// PlayerCommandDispatcher.BridgeByName.cs
// 名前で指した 2 つの描画オブジェクトの穴を橋渡しする処理。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【既存コマンドを呼ぶだけ】
//   点数合わせも橋張りも、既にある matchHoleRingCount / createHoleBridge を
//   再入 Dispatch で呼ぶ。処理を書き写すとパネルと結果が割れる。
//
// 【種の選び方を 2 通り使い分ける】
//   BridgeAutoPairOps.SelectPair は「頂点数が同数の穴ペア」しか候補にしない。
//   点数合わせは数が違う穴を揃えるための前処理なので、そこでは使えない。
//   合わせの種は重心距離が最小の穴ペアから最短の頂点ペアを取る
//   （PlayerRobotBuildTestSubPanel.Bridge.cs:663-675 と同じ判断）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>名前で橋を張るコマンドなら処理して true を返す。</summary>
        private bool DispatchBridgeByName(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            if (!(cmd is BridgeHolesByNameCommand c)) return false;

            if (model == null) { Fail("no current model"); return true; }

            int meshA = FindMeshIndexByName(model, c.BaseName);
            int meshB = FindMeshIndexByName(model, c.TargetName);

            if (meshA < 0 || meshB < 0)
            { Fail($"オブジェクトが見つかりません（{c.BaseName}={meshA} / {c.TargetName}={meshB}）"); return true; }

            // ── 点数合わせ
            if (c.MatchCounts)
            {
                if (!TryPickNearestHoleSeeds(model, meshA, meshB,
                                             out int mvA, out int mvB,
                                             out int countA, out int countB, out string mWhy))
                { Fail($"合わせの種を選べません: {mWhy}"); return true; }

                // 既に同数なら送らない。matchHoleRingCount は
                // 「頂点数は既に一致しています」で失敗を返すため。
                if (countA != countB)
                {
                    var mr = Dispatch(new MatchHoleRingCountCommand(
                        c.ModelIndex, meshA, mvA, -1, meshB, mvB, -1));
                    if (mr != null && !mr.Success)
                    { Fail($"穴の頂点数を合わせられません: {mr.Reason}"); return true; }
                }
            }

            // ── 橋張りの種。ここは同数の穴ペアだけが候補。
            var mcA = model.GetMeshContext(meshA);
            var mcB = model.GetMeshContext(meshB);

            var holesA = BridgeAutoPairOps.CollectHoles(mcA.MeshObject, mcA.VertexToWorldMatrix);
            var holesB = BridgeAutoPairOps.CollectHoles(mcB.MeshObject, mcB.VertexToWorldMatrix);

            var pair = BridgeAutoPairOps.SelectPair(holesA, holesB, sameMesh: meshA == meshB);
            if (!pair.Ok)
            {
                ReportData(CommandDataJson.New()
                    .Flag("ok",          false)
                    .Text("message",     pair.Message ?? "")
                    .Int ("baseIndex",   meshA)
                    .Int ("targetIndex", meshB)
                    .Int ("seedA",       -1)
                    .Int ("seedB",       -1)
                    .Int ("ringA",       0)
                    .Int ("ringB",       0)
                    .Build());
                return true;
            }

            int ringA = holesA[pair.HoleA].Vertices.Count;
            int ringB = holesB[pair.HoleB].Vertices.Count;

            string name = string.IsNullOrEmpty(c.BridgeName)
                ? $"Bridge_{c.BaseName}_{c.TargetName}"
                : c.BridgeName;

            // 対応と面の向きは自動にまかせる。固定値を渡すと、
            // 両穴の巻き方向によっては裏返って張られる。
            var br = Dispatch(new CreateHoleBridgeCommand(
                c.ModelIndex, meshA, pair.VertexA, meshB, pair.VertexB, name,
                PrimitiveAddMode.NewObject, -1,
                flipCorrespondence: false, flipFaces: false,
                subdivisions: c.Subdivisions,
                directionHintA: -1, directionHintB: -1,
                autoFlags: true));

            if (br != null && !br.Success)
            { Fail($"橋を張れません: {br.Reason}"); return true; }

            ReportData(CommandDataJson.New()
                .Flag("ok",          true)
                .Text("message",     "")
                .Int ("baseIndex",   meshA)
                .Int ("targetIndex", meshB)
                .Int ("seedA",       pair.VertexA)
                .Int ("seedB",       pair.VertexB)
                .Int ("ringA",       ringA)
                .Int ("ringB",       ringB)
                .Build());
            return true;
        }

        /// <summary>名前でメッシュ索引を引く。比べるのは Name（EditorName は表示用）。</summary>
        private static int FindMeshIndexByName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < model.MeshContextCount; i++)
                if (model.GetMeshContext(i)?.Name == name) return i;
            return -1;
        }

        /// <summary>
        /// 頂点数を問わずに、いちばん近い穴どうしから種を選ぶ。点数合わせ用。
        /// SelectPair は同数の穴しか候補にしないので、揃える前には使えない。
        /// </summary>
        private static bool TryPickNearestHoleSeeds(
            ModelContext model, int meshA, int meshB,
            out int vertexA, out int vertexB,
            out int countA, out int countB, out string message)
        {
            vertexA = -1; vertexB = -1; countA = 0; countB = 0; message = null;

            var mcA = model?.GetMeshContext(meshA);
            var mcB = model?.GetMeshContext(meshB);
            if (mcA?.MeshObject == null || mcB?.MeshObject == null)
            { message = "オブジェクトが見つかりません"; return false; }

            var holesA = BridgeAutoPairOps.CollectHoles(mcA.MeshObject, mcA.VertexToWorldMatrix);
            var holesB = BridgeAutoPairOps.CollectHoles(mcB.MeshObject, mcB.VertexToWorldMatrix);

            if (holesA.Count == 0 || holesB.Count == 0)
            { message = "穴が見つかりません"; return false; }

            // 重心距離が最小の穴ペア。
            int bestA = -1, bestB = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < holesA.Count; i++)
            {
                for (int k = 0; k < holesB.Count; k++)
                {
                    float d = Vector3.Distance(holesA[i].WorldCentroid, holesB[k].WorldCentroid);
                    if (d >= bestD) continue;
                    bestD = d; bestA = i; bestB = k;
                }
            }
            if (bestA < 0 || bestB < 0)
            { message = "穴のペアを決められません"; return false; }

            // その穴どうしで最短の頂点ペア。
            var ha = holesA[bestA];
            var hb = holesB[bestB];
            countA = ha.Vertices.Count;
            countB = hb.Vertices.Count;
            float bestSq = float.MaxValue;

            for (int p = 0; p < ha.WorldPositions.Count; p++)
            {
                for (int q = 0; q < hb.WorldPositions.Count; q++)
                {
                    float sq = (ha.WorldPositions[p] - hb.WorldPositions[q]).sqrMagnitude;
                    if (sq >= bestSq) continue;
                    bestSq  = sq;
                    vertexA = ha.Vertices[p];
                    vertexB = hb.Vertices[q];
                }
            }

            if (vertexA < 0 || vertexB < 0)
            { message = "種にできる頂点がありません"; return false; }
            return true;
        }
    }
}
