// PlayerCommandDispatcher.MeshArrays.cs
// 頂点座標と面の配列から描画オブジェクトを作る（addMeshFromArrays）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【形】
//   配列は getRawData の結果と同じ平坦な形（positions / faceSizes / faceVertices）。
//   座標と面の並びは PolyLing のまま受ける（外部ツールの座標系への変換は呼ぶ側）。
// 【置き方】
//   組み立てたメッシュを AddGeneratedMeshCommand と同じ実処理（OnAddGeneratedMesh）へ渡す。
//   新しい描画オブジェクトとして原点に置き、姿勢は頂点に入っているものとする。
//   作った描画オブジェクトの masterIndex と安定 ID は実処理が ReportTargets で報告する。
// 【UV と法線】
//   UV は頂点ごとに (0,0) を 1 つ持たせ、法線は面から計算し直す（RecalculateNormals）。

using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        private void RunAddMeshFromArrays(AddMeshFromArraysCommand cmd)
        {
            if (OnAddGeneratedMesh == null) { Fail("add generated mesh handler not wired"); return; }

            string name = string.IsNullOrWhiteSpace(cmd.MeshName) ? "Mesh" : cmd.MeshName.Trim();
            if (!TryBuildMeshFromArrays(name, cmd.Positions, cmd.FaceSizes, cmd.FaceVertices,
                                        out MeshObject mesh, out string reason))
            {
                _pendingResult = CommandResult.Fail(reason, CommandErrorCodes.InvalidArgs);
                return;
            }

            var placement = PrimitivePlacement.Default;
            placement.AddMode                = PrimitiveAddMode.NewObject;
            placement.KeepAsGroup            = false;
            placement.MergeDuplicateVertices = false;
            placement.WorldPosition          = Vector3.zero;

            string addReason = OnAddGeneratedMesh.Invoke(
                new AddGeneratedMeshCommand(cmd.ModelIndex, mesh, name, placement, poseAlreadyBaked: true));
            if (addReason != null) Fail(addReason);
        }

        /// <summary>
        /// 配列からメッシュを組み立てる。数が合わない・番号が範囲外・値が有限でないときは false と理由。
        /// </summary>
        internal static bool TryBuildMeshFromArrays(string name, float[] positions, int[] faceSizes, int[] faceVertices,
                                                    out MeshObject mesh, out string reason)
        {
            mesh = null;
            reason = null;
            positions    = positions    ?? System.Array.Empty<float>();
            faceSizes    = faceSizes    ?? System.Array.Empty<int>();
            faceVertices = faceVertices ?? System.Array.Empty<int>();

            if (positions.Length == 0 || positions.Length % 3 != 0)
            { reason = $"positions の数（{positions.Length}）が 3 の倍数ではありません"; return false; }
            for (int i = 0; i < positions.Length; i++)
                if (float.IsNaN(positions[i]) || float.IsInfinity(positions[i]))
                { reason = $"positions[{i}] が有限の値ではありません"; return false; }

            int vertexCount = positions.Length / 3;
            long cornerCount = 0;
            for (int f = 0; f < faceSizes.Length; f++)
            {
                if (faceSizes[f] < 3) { reason = $"faceSizes[{f}]（{faceSizes[f]}）が 3 未満です"; return false; }
                cornerCount += faceSizes[f];
            }
            if (cornerCount != faceVertices.Length)
            { reason = $"faceSizes の合計（{cornerCount}）と faceVertices の数（{faceVertices.Length}）が違います"; return false; }
            for (int i = 0; i < faceVertices.Length; i++)
                if (faceVertices[i] < 0 || faceVertices[i] >= vertexCount)
                { reason = $"faceVertices[{i}]（{faceVertices[i]}）が頂点の範囲（0〜{vertexCount - 1}）の外です"; return false; }

            var mo = new MeshObject { Name = name };
            for (int v = 0; v < vertexCount; v++)
                mo.AddVertex(new Vector3(positions[v * 3], positions[v * 3 + 1], positions[v * 3 + 2]), Vector2.zero);

            int offset = 0;
            for (int f = 0; f < faceSizes.Length; f++)
            {
                var face = new Face();
                for (int j = 0; j < faceSizes[f]; j++)
                {
                    face.VertexIndices.Add(faceVertices[offset + j]);
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }
                offset += faceSizes[f];
                mo.AddFace(face);
            }

            mo.RecalculateNormals();
            mesh = mo;
            return true;
        }
    }
}
