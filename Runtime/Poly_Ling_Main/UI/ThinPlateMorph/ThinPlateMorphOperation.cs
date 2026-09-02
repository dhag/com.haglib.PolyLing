// ThinPlateMorphOperation.cs
// 3D Thin Plate Spline モーフ 制御点収集・変形算出・確定処理（Undo記録）
// UnityEditor非依存
//
// 【対応点】
// ビフォーとアフターは頂点インデックスで対応させる。両者の頂点数が一致していること。
//
// 【空間】
// ビフォー／アフター／ターゲットはそれぞれ別の WorldMatrix を持ちうるため、
// いったんワールド空間へ移して TPS を解き、結果をターゲットのローカルへ戻す。
// 3つが同じ WorldMatrix なら変換は打ち消し合う。
//
// スキニング／ポーズは考慮しない。オブジェクト単位の MeshContext.WorldMatrix だけを使う
// （NormalTransplantOperation.cs:12 と同じ前提）。
//
// 逆行列はキャッシュである MeshContext.WorldMatrixInverse を使わず、その場で
// WorldMatrix.inverse を取る。リモート受信経路（RemoteProgressiveSerializer.cs:387）が
// WorldMatrix だけを書き込み、WorldMatrixInverse を更新しないため。
//
// 【計算量】
// 係数算出は制御点数 n に対して (n+4)^2 の行列を LU 分解する。積和回数は n^3 のオーダー。
// ワープは「ターゲット頂点数 × n」回のカーネル評価。上限は MaxControlPointCount。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Numerics;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public static class ThinPlateMorphOperation
    {
        /// <summary>係数算出に必要な最小制御点数。</summary>
        public const int MinControlPointCount = PLThinPlateSpline3D.MinimumPointCount;

        /// <summary>制御点数の上限。これを超えると LU 分解が現実的な時間で終わらない。</summary>
        public const int MaxControlPointCount = 1000;

        /// <summary>この数を超えたら時間がかかる旨を表示する目安。</summary>
        public const int WarnControlPointCount = 500;

        /// <summary>既定の平滑化係数。</summary>
        public const float DefaultLambda = PLThinPlateSpline3D.DefaultLambda;

        // ----------------------------------------------------------------
        // 局所モード（ターゲット頂点ごとに独立に係数を求める）
        // ----------------------------------------------------------------

        /// <summary>局所モードの既定近傍数。</summary>
        public const int LocalDefaultNeighborCount = 24;

        /// <summary>
        /// 局所モードの制御点数の上限。1 頂点あたり (N+4)^3/3 の積和が要るため、
        /// 全域モードの MaxControlPointCount をそのまま使うと現実的な時間で終わらない。
        /// ターゲット 10,000 頂点での総積和は N=24 で約 1.1e8、N=256 で約 5.9e10。
        /// </summary>
        public const int LocalMaxControlPointCount = 256;

        /// <summary>
        /// 局所モードの既定平滑化係数。
        ///
        /// カーネルは U = r^2 * log(r^2) で、λ は K 行列の対角に加算される。
        /// 全域モードの典型的な制御点間距離 r = 0.5 では |U| = 0.347 なので
        /// λ = 0.001 の寄与は 0.3% にとどまるが、局所モードで近傍が縮むと
        /// |U| も縮むため同じ λ では平滑化が支配的になる
        /// （r = 0.01 で |U| = 9.2e-4、λ = 0.001 と同オーダー）。
        /// λ = 1e-5 は r = 0.02 付近で全域モードと同等の効き（0.003）になる。
        /// 近傍の大きさに応じた自動調整は行わないため、必要なら手で変える。
        /// </summary>
        public const float LocalDefaultLambda = 1.0e-5f;

        // ================================================================
        // 制御点
        // ================================================================

        /// <summary>ワールド空間の制御点対。</summary>
        public sealed class ControlPointSet
        {
            /// <summary>変形前の制御点（ワールド）。</summary>
            public List<Vector3> BeforeWorld { get; }

            /// <summary>変形後の制御点（ワールド）。BeforeWorld と同数・同順。</summary>
            public List<Vector3> AfterWorld { get; }

            /// <summary>重複除去前の候補点数。</summary>
            public int CandidateCount { get; }

            /// <summary>ビフォー位置が既出と完全一致したため除いた点数。</summary>
            public int DuplicateCount => CandidateCount - BeforeWorld.Count;

            /// <summary>実際に使う制御点数。</summary>
            public int Count => BeforeWorld.Count;

            public ControlPointSet(List<Vector3> beforeWorld, List<Vector3> afterWorld, int candidateCount)
            {
                BeforeWorld    = beforeWorld;
                AfterWorld     = afterWorld;
                CandidateCount = candidateCount;
            }
        }

        /// <summary>
        /// 制御点に使う頂点インデックスを返す。
        ///
        /// selectedOnly のとき、ビフォー側とアフター側どちらで選択したかを問わないよう
        /// 両者の選択頂点の和集合を使う。
        /// </summary>
        public static List<int> BuildControlIndices(
            MeshContext beforeCtx, MeshContext afterCtx, bool selectedOnly)
        {
            var result = new List<int>();
            if (beforeCtx?.MeshObject == null) return result;

            int vertexCount = beforeCtx.MeshObject.VertexCount;

            if (!selectedOnly)
            {
                result.Capacity = vertexCount;
                for (int i = 0; i < vertexCount; i++) result.Add(i);
                return result;
            }

            var set = new HashSet<int>();
            AddSelected(set, beforeCtx, vertexCount);
            AddSelected(set, afterCtx, vertexCount);

            result.AddRange(set);
            result.Sort();
            return result;
        }

        private static void AddSelected(HashSet<int> dest, MeshContext ctx, int vertexCount)
        {
            var selected = ctx?.SelectedVertices;
            if (selected == null) return;
            foreach (int i in selected)
            {
                if (i >= 0 && i < vertexCount) dest.Add(i);
            }
        }

        /// <summary>
        /// 制御点数の見積り。重複除去は行わないため、実際の点数はこれ以下になる。
        /// パネルの事前チェック用。
        /// </summary>
        /// <returns>算出できない場合は -1。</returns>
        public static int EstimateControlPointCount(
            ModelContext model, int beforeIndex, int afterIndex, bool selectedOnly)
        {
            var beforeCtx = model?.GetMeshContext(beforeIndex);
            var afterCtx  = model?.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null || afterCtx?.MeshObject == null) return -1;
            if (beforeIndex == afterIndex) return -1;
            if (beforeCtx.MeshObject.VertexCount != afterCtx.MeshObject.VertexCount) return -1;

            return BuildControlIndices(beforeCtx, afterCtx, selectedOnly).Count;
        }

        /// <summary>
        /// ビフォー／アフターからワールド空間の制御点対を作る。
        ///
        /// ビフォー位置が既出と完全一致する点は落とす。同じ位置に相異なる変形先を
        /// 与えることはできず、そのままでは連立方程式が退化するため。
        /// </summary>
        /// <returns>失敗時は null を返し error に理由を入れる。</returns>
        public static ControlPointSet CollectControlPoints(
            ModelContext model, int beforeIndex, int afterIndex, bool selectedOnly,
            out string error)
        {
            error = null;

            if (model == null) { error = "モデルがありません"; return null; }

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx  = model.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null) { error = "ビフォーオブジェクトが不正です"; return null; }
            if (afterCtx?.MeshObject  == null) { error = "アフターオブジェクトが不正です"; return null; }
            if (beforeIndex == afterIndex)     { error = "ビフォーとアフターが同一です"; return null; }

            var beforeMo = beforeCtx.MeshObject;
            var afterMo  = afterCtx.MeshObject;
            if (beforeMo.VertexCount != afterMo.VertexCount)
            {
                error = $"頂点数が一致しません（ビフォー {beforeMo.VertexCount} / アフター {afterMo.VertexCount}）";
                return null;
            }

            var indices = BuildControlIndices(beforeCtx, afterCtx, selectedOnly);
            if (indices.Count == 0)
            {
                error = selectedOnly ? "選択頂点がありません" : "制御点がありません";
                return null;
            }

            Matrix4x4 beforeToWorld = beforeCtx.WorldMatrix;
            Matrix4x4 afterToWorld  = afterCtx.WorldMatrix;

            var beforeWorld = new List<Vector3>(indices.Count);
            var afterWorld  = new List<Vector3>(indices.Count);
            var seen        = new HashSet<Vector3>();

            for (int k = 0; k < indices.Count; k++)
            {
                int vi = indices[k];
                Vector3 bw = beforeToWorld.MultiplyPoint3x4(beforeMo.Vertices[vi].Position);
                if (!seen.Add(bw)) continue;

                beforeWorld.Add(bw);
                afterWorld.Add(afterToWorld.MultiplyPoint3x4(afterMo.Vertices[vi].Position));
            }

            return new ControlPointSet(beforeWorld, afterWorld, indices.Count);
        }

        // ================================================================
        // 変形算出
        // ================================================================

        /// <summary>
        /// TPS を解き、ターゲットの全頂点をワープした結果をターゲットのローカル座標で返す。
        /// </summary>
        /// <returns>失敗時は null を返し error に理由を入れる。</returns>
        public static Vector3[] ComputeWarpedLocalPositions(
            ModelContext model,
            int beforeIndex, int afterIndex, int targetIndex,
            float lambda, bool selectedControlPointsOnly,
            out ControlPointSet controlPoints,
            out string error)
        {
            controlPoints = null;
            error = null;

            if (model == null) { error = "モデルがありません"; return null; }

            var targetCtx = model.GetMeshContext(targetIndex);
            if (targetCtx?.MeshObject == null) { error = "ターゲットオブジェクトが不正です"; return null; }
            if (targetIndex == beforeIndex || targetIndex == afterIndex)
            {
                error = "ターゲットはビフォー／アフターと別のオブジェクトにしてください";
                return null;
            }

            var cp = CollectControlPoints(model, beforeIndex, afterIndex, selectedControlPointsOnly, out error);
            if (cp == null) return null;
            controlPoints = cp;

            if (cp.Count < MinControlPointCount)
            {
                error = $"制御点が足りません（{cp.Count} 点。{MinControlPointCount} 点以上必要）";
                return null;
            }
            if (cp.Count > MaxControlPointCount)
            {
                error = $"制御点が多すぎます（{cp.Count} 点。上限 {MaxControlPointCount} 点）";
                return null;
            }

            var tps = new PLThinPlateSpline3D();
            if (!tps.Solve(cp.BeforeWorld, cp.AfterWorld, lambda))
            {
                error = "TPS係数を算出できません。制御点が同一平面上または同一直線上に並んでいないか確認してください";
                return null;
            }

            var targetMo = targetCtx.MeshObject;
            int vertexCount = targetMo.VertexCount;
            if (vertexCount == 0) { error = "ターゲットに頂点がありません"; return null; }

            Matrix4x4 toWorld = targetCtx.WorldMatrix;
            Matrix4x4 toLocal = toWorld.inverse;

            var result = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 world  = toWorld.MultiplyPoint3x4(targetMo.Vertices[i].Position);
                Vector3 warped = tps.WarpPoint(world);
                result[i] = toLocal.MultiplyPoint3x4(warped);
            }
            return result;
        }

        // ================================================================
        // 局所モードの入力組み立て（メインスレッド）
        // ================================================================

        /// <summary>
        /// 局所 TPS の入力を組み立てる。MeshContext に触れるためメインスレッドで呼ぶこと。
        /// 出来上がった LocalMorphInput は配列だけで構成され、
        /// LocalThinPlateMorphSolver.Solve へそのままワーカースレッドへ渡せる。
        ///
        /// ビフォー位置の重複除去はここでは行わない。誘導部分グラフのノードが
        /// 消えて経路が切れるため、除去は係数を解く直前に近傍単位で行う
        /// （LocalThinPlateMorphSolver 冒頭のコメントを参照）。
        /// </summary>
        /// <returns>失敗時は null を返し error に理由を入れる。</returns>
        public static LocalMorphInput BuildLocalInput(
            ModelContext model,
            int beforeIndex, int afterIndex, int targetIndex,
            bool selectedControlPointsOnly,
            out string error)
        {
            error = null;

            if (model == null) { error = "モデルがありません"; return null; }

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx  = model.GetMeshContext(afterIndex);
            var targetCtx = model.GetMeshContext(targetIndex);

            if (beforeCtx?.MeshObject == null) { error = "ビフォーオブジェクトが不正です"; return null; }
            if (afterCtx?.MeshObject  == null) { error = "アフターオブジェクトが不正です"; return null; }
            if (targetCtx?.MeshObject == null) { error = "ターゲットオブジェクトが不正です"; return null; }
            if (beforeIndex == afterIndex)     { error = "ビフォーとアフターが同一です"; return null; }
            if (targetIndex == beforeIndex || targetIndex == afterIndex)
            {
                error = "ターゲットはビフォー／アフターと別のオブジェクトにしてください";
                return null;
            }

            var beforeMo = beforeCtx.MeshObject;
            var afterMo  = afterCtx.MeshObject;
            var targetMo = targetCtx.MeshObject;

            if (beforeMo.VertexCount != afterMo.VertexCount)
            {
                error = $"頂点数が一致しません（ビフォー {beforeMo.VertexCount} / アフター {afterMo.VertexCount}）";
                return null;
            }

            var indices = BuildControlIndices(beforeCtx, afterCtx, selectedControlPointsOnly);
            if (indices.Count == 0)
            {
                error = selectedControlPointsOnly ? "選択頂点がありません" : "制御点がありません";
                return null;
            }

            int targetCount = targetMo.VertexCount;
            if (targetCount == 0) { error = "ターゲットに頂点がありません"; return null; }

            Matrix4x4 beforeToWorld = beforeCtx.WorldMatrix;
            Matrix4x4 afterToWorld  = afterCtx.WorldMatrix;
            Matrix4x4 targetToWorld = targetCtx.WorldMatrix;

            int candidateCount = indices.Count;
            var beforeWorld = new Vector3[candidateCount];
            var afterWorld  = new Vector3[candidateCount];
            for (int k = 0; k < candidateCount; k++)
            {
                int vi = indices[k];
                beforeWorld[k] = beforeToWorld.MultiplyPoint3x4(beforeMo.Vertices[vi].Position);
                afterWorld[k]  = afterToWorld.MultiplyPoint3x4(afterMo.Vertices[vi].Position);
            }

            BuildInducedAdjacency(beforeMo, indices, out int[] adjStart, out int[] adjList);

            var targetWorld = new Vector3[targetCount];
            for (int i = 0; i < targetCount; i++)
            {
                targetWorld[i] = targetToWorld.MultiplyPoint3x4(targetMo.Vertices[i].Position);
            }

            return new LocalMorphInput
            {
                BeforeWorld    = beforeWorld,
                AfterWorld     = afterWorld,
                AdjacencyStart = adjStart,
                AdjacencyList  = adjList,
                TargetWorld    = targetWorld,
                TargetToLocal  = targetToWorld.inverse,
            };
        }

        /// <summary>
        /// 候補頂点だけを残した誘導部分グラフを CSR 形式で作る。
        /// 両端が候補である辺だけを残すため、選択が飛び地なら分断される。
        /// 辺が 1 本も無い場合（ビフォーに面が無い場合を含む）は両方 null を返す。
        /// </summary>
        public static void BuildInducedAdjacency(
            MeshObject beforeMesh, List<int> candidateIndices,
            out int[] adjacencyStart, out int[] adjacencyList)
        {
            adjacencyStart = null;
            adjacencyList  = null;

            if (beforeMesh == null || candidateIndices == null) return;
            if (beforeMesh.FaceCount == 0) return;

            int vertexCount    = beforeMesh.VertexCount;
            int candidateCount = candidateIndices.Count;
            if (candidateCount == 0) return;

            // 元頂点索引 → 候補内の局所索引
            var localOf = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) localOf[i] = -1;
            for (int k = 0; k < candidateCount; k++)
            {
                int vi = candidateIndices[k];
                if (vi >= 0 && vi < vertexCount) localOf[vi] = k;
            }

            var adjacency = SelectionHelper.BuildVertexAdjacency(beforeMesh);

            // 次数を数える
            var start = new int[candidateCount + 1];
            int edgeCount = 0;
            for (int k = 0; k < candidateCount; k++)
            {
                int vi = candidateIndices[k];
                if (vi < 0 || vi >= vertexCount) continue;
                if (!adjacency.TryGetValue(vi, out var neighbors)) continue;

                int degree = 0;
                foreach (int nb in neighbors)
                {
                    if (nb < 0 || nb >= vertexCount) continue;
                    if (localOf[nb] < 0) continue;
                    degree++;
                }
                start[k + 1] = degree;
                edgeCount   += degree;
            }

            if (edgeCount == 0) return;

            for (int k = 0; k < candidateCount; k++) start[k + 1] += start[k];

            var list   = new int[edgeCount];
            var cursor = new int[candidateCount];
            for (int k = 0; k < candidateCount; k++)
            {
                int vi = candidateIndices[k];
                if (vi < 0 || vi >= vertexCount) continue;
                if (!adjacency.TryGetValue(vi, out var neighbors)) continue;

                foreach (int nb in neighbors)
                {
                    if (nb < 0 || nb >= vertexCount) continue;
                    int local = localOf[nb];
                    if (local < 0) continue;
                    list[start[k] + cursor[k]] = local;
                    cursor[k]++;
                }
            }

            adjacencyStart = start;
            adjacencyList  = list;
        }

        /// <summary>
        /// 局所モードの LU 分解にかかる積和回数の見積り。
        /// ターゲット頂点数 × (制御点数 + 4)^3 / 3。
        /// 半径モードは制御点数が入力依存なので、この見積りは使えない。
        /// </summary>
        public static double EstimateLocalSolveCost(int targetVertexCount, int controlPointCount)
        {
            if (targetVertexCount <= 0 || controlPointCount <= 0) return 0.0;
            double size = controlPointCount + 4.0;
            return targetVertexCount * (size * size * size) / 3.0;
        }

        // ================================================================
        // 確定
        // ================================================================

        /// <summary>
        /// ワープ結果を新規オブジェクトとして追加する。ターゲットは変更しない。
        ///
        /// 複製は BlendOperation.CloneContext を使う。BindPose / WorldMatrix /
        /// BonePoseData を引き継ぎ、ObjectId は複製しないため ModelContext.Add が
        /// 新しい安定IDを振る。
        /// </summary>
        /// <returns>追加した MeshContext の索引。失敗時は -1。</returns>
        public static int ApplyAsNewObject(
            ModelContext model, int targetIndex, Vector3[] localPositions,
            bool recalculateNormals, ToolContext toolCtx)
        {
            if (model == null || localPositions == null) return -1;

            var targetCtx = model.GetMeshContext(targetIndex);
            if (targetCtx?.MeshObject == null) return -1;
            if (localPositions.Length != targetCtx.MeshObject.VertexCount) return -1;

            var existingNames = new HashSet<string>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null) existingNames.Add(mc.Name);
            }
            string newName = GenerateUniqueName(targetCtx.Name + "_tps", existingNames);

            var newCtx = BlendOperation.CloneContext(targetCtx, newName);
            if (newCtx?.MeshObject == null) return -1;

            var mo = newCtx.MeshObject;
            for (int i = 0; i < localPositions.Length; i++) mo.Vertices[i].Position = localPositions[i];
            if (recalculateNormals) mo.RecalculateSmoothNormals();

            // 独立した新規オブジェクトなので、ミラー由来フラグは引き継がない。
            // TPS の結果は左右対称性を保証せず、ミラー側として扱われると破綻する。
            newCtx.MirrorGeometryDerived = false;
            newCtx.IsVisible = true;

            var undo   = toolCtx?.UndoController;
            var oldSelected = model.CaptureAllSelectedIndices();
            int newIndex = model.Add(newCtx);
            var newSelected = model.CaptureAllSelectedIndices();

            undo?.RecordMeshContextsAdd(
                new List<(int Index, MeshContext MeshContext)> { (newIndex, newCtx) },
                oldSelected, newSelected);

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(newCtx);
            toolCtx?.NotifyTopologyChanged?.Invoke();
            model.OnListChanged?.Invoke();
            toolCtx?.Repaint?.Invoke();

            return newIndex;
        }

        private static string GenerateUniqueName(string baseName, HashSet<string> existingNames)
        {
            if (!existingNames.Contains(baseName)) return baseName;
            for (int n = 1; n < 10000; n++)
            {
                string name = $"{baseName}_{n}";
                if (!existingNames.Contains(name)) return name;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }
    }
}
