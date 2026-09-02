// PlayerRobotBuildTestSubPanel.Bridge.cs
// ロボ組み立て自動検証の後半の段。仕切り・面削除・穴点数合わせ・穴つなぎ・
// ミラー分岐・スキンド変換・Humanoid 割当・VRM 書き出し。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【面と辺を番号で書かない理由】
//   面番号・頂点番号は生成順に依存する。生成器の実装が変われば番号は変わるので、
//   直に書くとそのたびに検証が壊れる。落とす面も拾う辺も、実データの幾何条件
//   （中心の座標）から選ぶ。番号は実行時に決まる。
//
// 【Python での事前確認】
//   小判型（LengthSegments=3 / CapSegments=3）で次を実測してある。
//     ・蓋なしの上下開口は 12 点の環
//     ・下部開口を X 中央で仕切ると 6 / 6 に割れる（LengthSegments が奇数のとき）
//     ・上だけ蓋つき・HeightSegments=3 で、側面の中段だけを落とすと
//       腕用の穴が左右 8 点ずつ独立して開く
//     ・上蓋の中央長方形を落とすと首用が 14 点
//   中段以外を落とすと下部開口や上蓋の穴とつながって 1 つになるので、
//   帯の選び方は「中段」でなければならない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public partial class PlayerRobotBuildTestSubPanel
    {
        // ================================================================
        // S4 仕切り（センター下部を 2 穴に割る）
        // ================================================================

        /// <summary>
        /// センターの下部開口を X 中央で仕切って 2 つの穴にする。
        ///
        /// 辺ブリッジは「離れた 2 か所の辺群」を要求する（EdgeChainOps.cs:67-74）ので、
        /// 前縁（z が最大）と後縁（z が最小）から x=0 に最も近い辺を 1 本ずつ拾う。
        /// 張られた四角が仕切りになって開口が左右へ割れる。
        /// </summary>
        private StageResult StagePartitionCenter()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int mi = FindMeshIndex(model, "センター");
            var mc = model.GetMeshContext(mi);
            if (mc?.MeshObject == null) return StageResult.Fail;

            var mo = mc.MeshObject;

            // 下部開口の境界辺だけを見る（y が最小の側）。
            var edges = new List<VertexPair>(BoundaryEdgeOps.CollectBoundaryEdges(mo));
            if (edges.Count == 0) return StageResult.Fail;

            float minY = float.MaxValue;
            foreach (var e in edges)
                minY = Mathf.Min(minY, Mathf.Min(mo.Vertices[e.V1].Position.y, mo.Vertices[e.V2].Position.y));

            var lower = new List<VertexPair>();
            foreach (var e in edges)
            {
                float y = (mo.Vertices[e.V1].Position.y + mo.Vertices[e.V2].Position.y) * 0.5f;
                if (Mathf.Abs(y - minY) < 1e-4f) lower.Add(e);
            }
            if (lower.Count < 4) return StageResult.Fail;

            // 前縁 / 後縁を z の符号で分け、それぞれ x=0 に最も近い辺を選ぶ。
            VertexPair? front = null, back = null;
            float frontD = float.MaxValue, backD = float.MaxValue;

            foreach (var e in lower)
            {
                Vector3 a = mo.Vertices[e.V1].Position, b = mo.Vertices[e.V2].Position;
                float cz = (a.z + b.z) * 0.5f;
                float cx = Mathf.Abs((a.x + b.x) * 0.5f);

                if (cz > 0f) { if (cx < frontD) { frontD = cx; front = e; } }
                else         { if (cx < backD)  { backD  = cx; back  = e; } }
            }

            if (front == null || back == null) return StageResult.Fail;

            int holesBefore = HoleCountOf(model, mi);

            SendLogged(new CreateEdgeBridgeCommand(
                ModelIndex(), mi, new[] { front.Value, back.Value },
                autoCorrespondence: true, flipCorrespondence: false, flipFaces: false,
                subdivisions: 0));

            RefreshAfterTopologyChange?.Invoke();

            // 仕切りが効いたなら穴が 1 つ増える。増えていなければ、
            // 面は張られても縁が分断できていない。送信できたことと効いたことは別。
            int holesAfter = HoleCountOf(model, mi);
            NoteTopology(model, mi, "センター");

            if (holesAfter <= holesBefore)
            {
                Debug.LogWarning(
                    $"[RobotBuildTest] 仕切りが効いていません: 穴 {holesBefore} → {holesAfter}");
                return StageResult.Fail;
            }

            return StageResult.Ok;
        }

        // ================================================================
        // S5 面削除（上半身2 に首・腕の穴を開ける）
        // ================================================================

        /// <summary>
        /// 上半身2 の側面の中段（左右の半円部分）と、上蓋の中央長方形を落とす。
        ///
        /// 側面は高さ方向に 3 段あり、中段だけを落とせば腕用の穴が上蓋とも
        /// 下部開口ともつながらずに独立する。最下段だと下部開口と一続きに破れ、
        /// 最上段だと上蓋を落とした穴とつながる。
        ///
        /// 「左右の半円部分」は、直線部の半長さ a = Length/2 - Depth/2 より
        /// 外側（|x| > a）にある面として選ぶ。上蓋の中央長方形は |x| <= a。
        /// </summary>
        private StageResult StageOpenChestHoles()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int mi = FindMeshIndex(model, "上半身2");
            var mc = model.GetMeshContext(mi);
            if (mc?.MeshObject == null) return StageResult.Fail;

            var part = RobotBuildRecipe.Find("上半身2");
            float a = part.Length * 0.5f - part.Depth * 0.5f;

            var mo = mc.MeshObject;

            // 面の中心を集めて、Y でクラスタリングして帯を数える。
            var centers = new Vector3[mo.FaceCount];
            for (int f = 0; f < mo.FaceCount; f++) centers[f] = FaceCenter(mo, f);

            // 上蓋の面（法線が上向き ＝ 中心 Y が最大付近で水平に広がる面）を分ける。
            float maxY = float.MinValue;
            for (int f = 0; f < mo.FaceCount; f++) maxY = Mathf.Max(maxY, centers[f].y);

            var side = new List<int>();
            var cap  = new List<int>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                if (Mathf.Abs(centers[f].y - maxY) < 1e-4f) cap.Add(f);
                else                                        side.Add(f);
            }
            if (side.Count == 0 || cap.Count == 0) return StageResult.Fail;

            // 側面の帯（中心 Y の値）を集めて、中央の帯を選ぶ。
            var bands = new List<float>();
            foreach (int f in side)
            {
                bool found = false;
                for (int i = 0; i < bands.Count; i++)
                    if (Mathf.Abs(bands[i] - centers[f].y) < 1e-4f) { found = true; break; }
                if (!found) bands.Add(centers[f].y);
            }
            bands.Sort();
            if (bands.Count < 3) return StageResult.Fail;

            float midBand = bands[bands.Count / 2];

            var drop = new List<int>();

            // 腕用: 中段の、直線部より外側にある側面。
            foreach (int f in side)
                if (Mathf.Abs(centers[f].y - midBand) < 1e-4f && Mathf.Abs(centers[f].x) > a - 1e-4f)
                    drop.Add(f);

            // 首用: 上蓋の中央長方形。
            foreach (int f in cap)
                if (Mathf.Abs(centers[f].x) <= a + 1e-4f)
                    drop.Add(f);

            if (drop.Count == 0) return StageResult.Fail;

            int facesBefore = mo.FaceCount;

            SendLogged(new DeleteFacesCommand(ModelIndex(), mi, drop.ToArray()));

            RefreshAfterTopologyChange?.Invoke();

            // 消えたかを面数で確かめる。
            // DeleteSelectionTool は選択オブジェクトリストに入っていないメッシュを
            // 対象にしないので、送信が通っても何も消えないことがある。
            int facesAfter = model.GetMeshContext(mi)?.MeshObject?.FaceCount ?? facesBefore;
            NoteTopology(model, mi, "上半身2");

            if (facesAfter != facesBefore - drop.Count)
            {
                Debug.LogWarning(
                    $"[RobotBuildTest] 面削除が効いていません: 面 {facesBefore} → {facesAfter}" +
                    $"（{drop.Count} 枚消える見込み）");
                return StageResult.Fail;
            }

            return StageResult.Ok;
        }

        // ================================================================
        // 穴点数合わせ
        // ================================================================

        /// <summary>
        /// 穴つなぎの前提（2 つの穴の頂点数が同じ）を満たす。
        /// 胴側の穴を基準にして、六角柱側（6 点）をそれに合わせる。
        /// </summary>
        private StageResult StageMatchHoleCounts()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int failed = 0;
            int tried  = 0;

            foreach (var j in Joints())
            {
                int baseMesh   = FindMeshIndex(model, j.BaseName);
                int targetMesh = FindMeshIndex(model, j.TargetName);
                if (baseMesh < 0 || targetMesh < 0)
                {
                    AddNote($"{j.BaseName}↔{j.TargetName}: メッシュが見つからない" +
                            $"（{j.BaseName}={baseMesh} / {j.TargetName}={targetMesh}）");
                    failed++;
                    continue;
                }

                // ここは数が違う穴を揃えるための段。同数を要求する選び方は使えない。
                if (!TryPickNearestHoleSeeds(model, baseMesh, targetMesh,
                                             out int baseVert, out int targetVert, out string why))
                {
                    AddNote($"{j.BaseName}↔{j.TargetName}: 種を選べない（{why}）");
                    Debug.LogWarning($"[RobotBuildTest] 穴点数合わせ {j.BaseName}↔{j.TargetName}: {why}");
                    failed++;
                    continue;
                }

                tried++;

                // 送る前の縁の点数。合わせの前後が分かるようにしておく。
                int beforeB = LoopCountOf(model, baseMesh,   baseVert);
                int beforeT = LoopCountOf(model, targetMesh, targetVert);
                AddNote($"{j.BaseName}↔{j.TargetName}: 合わせ前 {beforeB} / {beforeT} 点");

                SendLogged(new MatchHoleRingCountCommand(
                    ModelIndex(),
                    baseMesh, baseVert, -1,
                    targetMesh, targetVert, -1));

                NoteTopology(model, baseMesh,   j.BaseName);
                NoteTopology(model, targetMesh, j.TargetName);

                // 合ったかを確かめる。合っていないと、次の穴つなぎで
                // 同数の穴ペアが見つからず張れない。
                if (!HoleCountsMatch(model, baseMesh, targetMesh, baseVert, targetVert,
                                     out int cb, out int ct))
                {
                    AddNote($"{j.BaseName}↔{j.TargetName}: 合わず（{cb} / {ct} 点）");
                    Debug.LogWarning(
                        $"[RobotBuildTest] 頂点数が合いませんでした: " +
                        $"{j.BaseName}({cb}) / {j.TargetName}({ct})");
                    failed++;
                }
            }

            RefreshAfterTopologyChange?.Invoke();

            AddNote($"穴点数合わせ: 実行 {tried} 件 / 合わなかった関節 {failed} 件");

            if (failed > 0) return StageResult.Fail;
            return StageResult.Ok;
        }

        /// <summary>
        /// 種が属する穴どうしの頂点数が一致したかを見る。
        /// 穴は種頂点から縁をたどって復元する（ブリッジと同じ引き方）。
        /// </summary>
        private static bool HoleCountsMatch(
            ModelContext model, int meshA, int meshB, int vertA, int vertB,
            out int countA, out int countB)
        {
            countA = LoopCountOf(model, meshA, vertA);
            countB = LoopCountOf(model, meshB, vertB);
            return countA > 0 && countA == countB;
        }

        /// <summary>種頂点が属する縁の頂点数。復元できなければ 0。</summary>
        private static int LoopCountOf(ModelContext model, int meshIndex, int vertex)
        {
            var mo = model?.GetMeshContext(meshIndex)?.MeshObject;
            if (mo == null || vertex < 0) return 0;

            var loop = BridgeLoopOps.OrderBoundaryLoop(mo, vertex, -1, out _);
            return loop?.Count ?? 0;
        }

        // ================================================================
        // S7 穴つなぎ
        // ================================================================

        /// <summary>関節の隙間を面で橋渡しする。</summary>
        private StageResult StageBridgeJoints()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int n = 0;
            foreach (var j in Joints())
            {
                int meshA = FindMeshIndex(model, j.BaseName);
                int meshB = FindMeshIndex(model, j.TargetName);
                if (meshA < 0 || meshB < 0) continue;

                if (!TryPickBridgeSeeds(model, meshA, meshB, out int va, out int vb, out string msg))
                {
                    Debug.LogWarning($"[RobotBuildTest] {j.BaseName}↔{j.TargetName}: {msg}");
                    continue;
                }

                // 対応と面の向きは自動判定にまかせる（autoFlags は既定 true）。
                // 固定値を渡すと、両穴の巻き方向によっては裏返って張られる。
                SendLogged(new CreateHoleBridgeCommand(
                    ModelIndex(), meshA, va, meshB, vb,
                    $"Bridge_{j.BaseName}_{j.TargetName}",
                    PrimitiveAddMode.NewObject, -1,
                    flipCorrespondence: false, flipFaces: false,
                    subdivisions: RobotBuildRecipe.BridgeSubdivisions));
                n++;

                AddNote($"{j.BaseName}↔{j.TargetName}: 種 {va} / {vb}");
            }

            RefreshAfterTopologyChange?.Invoke();

            // 張れなかった組があると、その関節は離れたままになる。
            // 何本を狙って何本張れたかを残す。
            int want = 0;
            foreach (var _ in Joints()) want++;
            AddNote($"穴つなぎ: {n} / {want} 本");

            return n == want ? StageResult.Ok : StageResult.Fail;
        }

        /// <summary>
        /// スキンド変換より前に張る関節の組。胴どうしと左半身。
        ///
        /// 右半身のメッシュはこの時点では無い。SetMirrorBranchRootCommand は
        /// 印を付けるだけで、実体を作るのはスキンド変換
        /// （ConvertMeshFilterToSkinnedCommand の TolerantMirrorBranch）。
        /// 分岐ルート配下のブリッジは変換時に一緒にミラーされるので、
        /// 左を張っておけば右にも出る。
        /// </summary>
        private static IEnumerable<(string BaseName, string TargetName)> Joints()
        {
            // 胴。開口はどちらも 12 点なので穴点数合わせは要らない。
            yield return ("センター", "上半身");
            yield return ("上半身",  "上半身2");

            yield return ("上半身2", "首");
            yield return ("首",     "頭");
            yield return ("上半身2", "左腕");
            yield return ("左腕",   "左ひじ");
            yield return ("左ひじ", "左手首");
            yield return ("センター", "左足");
            yield return ("左足",   "左ひざ");
            yield return ("左ひざ", "左足首");
        }

        /// <summary>
        /// スキンド変換のあとに張る関節の組。分岐ルートをまたぐ右側。
        ///
        /// センター下部は仕切りで左右 2 つの穴になっていて、左だけを変換前に張った。
        /// 上半身2 の側面も左右とも落としてある。どちらも相手（右足・右腕）が
        /// 変換で初めて出来るので、ここで後付けする。
        /// ミラーの対象は分岐ルート配下だけなので、この 2 本は自動では出ない。
        /// </summary>
        private static IEnumerable<(string BaseName, string TargetName)> PostSkinJoints()
        {
            yield return ("センター", "右足");
            yield return ("上半身2", "右腕");
        }

        // ================================================================
        // S8 ミラー分岐ルート
        // ================================================================

        /// <summary>
        /// 腕と足の付け根にミラー分岐ルートを立てる。
        /// これを立てないとスキンド変換で左右のボーン木が作られない。
        /// </summary>
        private StageResult StageMirrorBranchRoots()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            var targets = new List<int>();
            foreach (string name in RobotBuildRecipe.MirrorBranchRoots)
            {
                int idx = FindMeshIndex(model, name);
                if (idx >= 0) targets.Add(idx);
            }
            if (targets.Count == 0) return StageResult.Fail;

            SendLogged(new SetMirrorBranchRootCommand(ModelIndex(), targets.ToArray(), true));
            return StageResult.Ok;
        }

        // ================================================================
        // S9 スキンド変換
        // ================================================================

        private StageResult StageConvertToSkinned()
        {
            SendLogged(new ConvertMeshFilterToSkinnedCommand(ModelIndex()));
            RefreshAfterTopologyChange?.Invoke();
            return StageResult.Ok;
        }

        // ================================================================
        // スキンド変換後のブリッジ（右側の後付け）
        // ================================================================

        /// <summary>
        /// 分岐ルートをまたぐ右側のブリッジを後付けする。
        /// 変換後なので、メッシュ名は接尾辞付きで引く。ボーン名は元のまま。
        /// 生成物の名前は接尾辞を付けずに揃える（ウェイト塗りが同じ規則で引けるように）。
        /// </summary>
        private StageResult StageBridgePostSkinJoints()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int n = 0;
            foreach (var j in PostSkinJoints())
            {
                int meshA = FindMeshIndex(model, SkinnedName(j.BaseName));
                int meshB = FindMeshIndex(model, SkinnedName(j.TargetName));

                if (meshA < 0 || meshB < 0)
                {
                    Debug.LogWarning(
                        $"[RobotBuildTest] 後付けブリッジを飛ばしました: " +
                        $"{SkinnedName(j.BaseName)}({meshA}) / {SkinnedName(j.TargetName)}({meshB})");
                    continue;
                }

                if (!TryPickBridgeSeeds(model, meshA, meshB, out int va, out int vb, out string msg))
                {
                    Debug.LogWarning($"[RobotBuildTest] {j.BaseName}↔{j.TargetName}: {msg}");
                    continue;
                }

                SendLogged(new CreateHoleBridgeCommand(
                    ModelIndex(), meshA, va, meshB, vb,
                    $"Bridge_{j.BaseName}_{j.TargetName}",
                    PrimitiveAddMode.NewObject, -1,
                    flipCorrespondence: false, flipFaces: false,
                    subdivisions: RobotBuildRecipe.BridgeSubdivisions));
                n++;
            }

            RefreshAfterTopologyChange?.Invoke();
            return n > 0 ? StageResult.Ok : StageResult.Fail;
        }

        // ================================================================
        // ブリッジのウェイト塗り（スキンド変換の後）
        // ================================================================

        /// <summary>
        /// ブリッジのメッシュへ、A→B の位置に応じたウェイトを置く。
        ///
        /// 【配分】
        ///   A 端 1.0 : 0.0 から B 端 0.0 : 1.0 まで、輪ごとに直線で配る。
        ///   分割 3（輪 5 本）なら
        ///     1.00:0.00 / 0.75:0.25 / 0.50:0.50 / 0.25:0.75 / 0.00:1.00
        ///   端を 0.5 ずつにすると、隣り合う本体の面と食い違って関節が折れる。
        ///   端は本体と同じ 1.0 にして、中へ向かって渡す。
        ///
        /// 【輪の見分け方】
        ///   頂点の並び順は生成器の実装に依存するので使わない。
        ///   A 側ボーンから B 側ボーンへ向かう軸へ射影し、その値で頂点をまとめる。
        ///   まっすぐな橋なら射影値は輪ごとに等間隔に並ぶ。
        ///
        /// 【なぜスキンド変換の後か】
        ///   ウェイトの相手はボーン。スキンド変換で初めてボーンができる。
        /// </summary>
        private StageResult StagePaintBridgeWeights()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            int mi = ModelIndex();
            int painted = 0;

            // 変換前に張ったものは接尾辞が付く。後付けしたものは付かない。
            // どちらの名前でも引けるようにして、両方を塗る。
            var jobs = new List<(string BaseName, string TargetName)>();
            jobs.AddRange(Joints());
            jobs.AddRange(PostSkinJoints());

            foreach (var j in jobs)
            {
                string plain = $"Bridge_{j.BaseName}_{j.TargetName}";

                int meshIdx = FindMeshIndex(model, SkinnedName(plain));
                if (meshIdx < 0) meshIdx = FindMeshIndex(model, plain);
                int boneA   = FindBoneIndex(model, j.BaseName);
                int boneB   = FindBoneIndex(model, j.TargetName);

                if (meshIdx < 0 || boneA < 0 || boneB < 0)
                {
                    Debug.LogWarning(
                        $"[RobotBuildTest] ウェイト塗りを飛ばしました: " +
                        $"mesh={plain}({meshIdx}) boneA={j.BaseName}({boneA}) boneB={j.TargetName}({boneB})");
                    continue;
                }

                if (PaintOneBridge(model, mi, meshIdx, boneA, boneB)) painted++;
            }

            return painted > 0 ? StageResult.Ok : StageResult.Fail;
        }

        /// <summary>
        /// ブリッジ 1 本を塗る。輪ごとに SetSkinWeightNumericCommand を送る。
        /// 同コマンドは選択頂点にまとめて効くので、同じ配分になる頂点を 1 回で送る。
        /// </summary>
        private bool PaintOneBridge(
            ModelContext model, int modelIndex, int meshIdx, int boneA, int boneB)
        {
            var mc = model.GetMeshContext(meshIdx);
            var mo = mc?.MeshObject;
            if (mo == null || mo.VertexCount == 0) return false;

            var bA = model.GetMeshContext(boneA);
            var bB = model.GetMeshContext(boneB);
            if (bA == null || bB == null) return false;

            // ボーンの原点をブリッジの頂点空間へ落として軸を作る。
            Matrix4x4 toLocal = mc.WorldToVertexMatrix;
            Vector3 pA = toLocal.MultiplyPoint3x4(bA.WorldMatrix.GetColumn(3));
            Vector3 pB = toLocal.MultiplyPoint3x4(bB.WorldMatrix.GetColumn(3));

            Vector3 axis = pB - pA;
            if (axis.sqrMagnitude < 1e-12f) return false;
            axis.Normalize();

            // 射影値を集めて端を出す。橋の実体で正規化するので、
            // ボーン原点が橋の端とずれていても影響しない。
            int n = mo.VertexCount;
            var proj = new float[n];
            float lo = float.MaxValue, hi = float.MinValue;
            for (int v = 0; v < n; v++)
            {
                float d = Vector3.Dot(mo.Vertices[v].Position - pA, axis);
                proj[v] = d;
                if (d < lo) lo = d;
                if (d > hi) hi = d;
            }

            float span = hi - lo;
            if (span < 1e-9f)
            {
                Debug.LogWarning($"[RobotBuildTest] {mc.Name}: 橋の長さがゼロなので塗れません");
                return false;
            }

            // 射影値を輪ごとにまとめる。実測の間隔より十分細かい許容でまとめる。
            const float RingEpsilon = 1e-4f;
            var rings = new List<(float T, List<int> Verts)>();

            for (int v = 0; v < n; v++)
            {
                float t = Mathf.Clamp01((proj[v] - lo) / span);

                int found = -1;
                for (int k = 0; k < rings.Count; k++)
                    if (Mathf.Abs(rings[k].T - t) <= RingEpsilon) { found = k; break; }

                if (found < 0) { rings.Add((t, new List<int> { v })); }
                else           { rings[found].Verts.Add(v); }
            }

            SendLogged(new SelectMeshCommand(modelIndex, MeshCategory.Drawable, new[] { meshIdx }));

            foreach (var ring in rings)
            {
                float wA = 1f - ring.T;
                float wB = ring.T;

                mc.SelectedVertices.Clear();
                foreach (int v in ring.Verts) mc.SelectedVertices.Add(v);

                SendLogged(new SetSkinWeightNumericCommand(
                    modelIndex,
                    new[] { boneA, boneB, -1, -1 },
                    new[] { wA, wB, 0f, 0f }));
            }

            return true;
        }

        /// <summary>
        /// スキンド変換後の名前。接尾辞は変換側の正典
        /// （MeshFilterToSkinnedConverter.MeshNameSuffix）を引く。
        /// 直書きすると、変換側が接尾辞を変えたときに黙って引けなくなる。
        /// </summary>
        private static string SkinnedName(string name)
            => name + MeshFilterToSkinnedConverter.MeshNameSuffix;

        // ================================================================
        // 種の選び方
        // ================================================================

        /// <summary>
        /// 2 つのメッシュの穴から、最短距離の頂点ペアを種として選ぶ。
        /// 穴つなぎ用。両穴の頂点数が同じであることを前提にする。
        ///
        /// 【なぜ自前で最近傍を探さないか】
        ///   種は「両方の穴で対応する位置」でなければならない。A と B を別々に
        ///   最近傍で選ぶと、選んだ 2 点が穴の別の側になることがある。
        ///   BridgeLoopOps.Build は両ループの先頭どうしを対応させるだけなので
        ///   （BridgeLoopOps.cs:205）、そのままねじれになる。
        /// </summary>
        private static bool TryPickBridgeSeeds(
            ModelContext model, int meshA, int meshB,
            out int vertexA, out int vertexB, out string message)
        {
            vertexA = -1; vertexB = -1; message = null;

            var mcA = model?.GetMeshContext(meshA);
            var mcB = model?.GetMeshContext(meshB);
            if (mcA?.MeshObject == null || mcB?.MeshObject == null)
            {
                message = "オブジェクトが見つかりません";
                return false;
            }

            var holesA = BridgeAutoPairOps.CollectHoles(mcA.MeshObject, mcA.VertexToWorldMatrix);
            var holesB = BridgeAutoPairOps.CollectHoles(mcB.MeshObject, mcB.VertexToWorldMatrix);

            var pair = BridgeAutoPairOps.SelectPair(holesA, holesB, sameMesh: meshA == meshB);
            if (!pair.Ok)
            {
                message = pair.Message;
                return false;
            }

            vertexA = pair.VertexA;
            vertexB = pair.VertexB;
            return true;
        }

        /// <summary>
        /// 頂点数を問わずに、いちばん近い穴どうしから種を選ぶ。穴点数合わせ用。
        ///
        /// 【なぜ SelectPair を使えないか】
        ///   BridgeAutoPairOps.SelectPair は「頂点数が同数の穴ペア」しか候補にしない。
        ///   穴点数合わせは数が違う穴を揃えるための前処理なので、
        ///   同数を要求する関数では 1 件も選べない。
        ///
        /// 【限界】
        ///   重心距離だけで穴の組を決めるので、穴が近接して並ぶ形状では
        ///   意図しない組を選ぶことがある。今回の胴のように穴が離れていれば通るが、
        ///   一般には当てにならない。合った・合わなかったは呼出し側が
        ///   HoleCountsMatch で確かめること。
        /// </summary>
        private static bool TryPickNearestHoleSeeds(
            ModelContext model, int meshA, int meshB,
            out int vertexA, out int vertexB, out string message)
        {
            vertexA = -1; vertexB = -1; message = null;

            var mcA = model?.GetMeshContext(meshA);
            var mcB = model?.GetMeshContext(meshB);
            if (mcA?.MeshObject == null || mcB?.MeshObject == null)
            {
                message = "オブジェクトが見つかりません";
                return false;
            }

            var holesA = BridgeAutoPairOps.CollectHoles(mcA.MeshObject, mcA.VertexToWorldMatrix);
            var holesB = BridgeAutoPairOps.CollectHoles(mcB.MeshObject, mcB.VertexToWorldMatrix);

            if (holesA.Count == 0 || holesB.Count == 0)
            {
                message = "穴が見つかりません";
                return false;
            }

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
            {
                message = "穴のペアを決められません";
                return false;
            }

            // その穴どうしで最短の頂点ペア。
            var ha = holesA[bestA];
            var hb = holesB[bestB];
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
            {
                message = "種にできる頂点がありません";
                return false;
            }
            return true;
        }

        // ================================================================
        // 位相の記録
        // ================================================================

        /// <summary>穴の数。境界辺の連結成分を数える。</summary>
        private static int HoleCountOf(ModelContext model, int meshIndex)
        {
            PolyLingPlayerViewerCore.InspectTopology(model, meshIndex, out _, out var holes);
            return holes.Count;
        }

        /// <summary>
        /// 面数と穴の内訳を結果欄へ出す。
        /// 「送れたか」ではなく「どうなったか」を残さないと、
        /// どの段で崩れたのかがあとから追えない。
        /// </summary>
        private void NoteTopology(ModelContext model, int meshIndex, string label)
        {
            if (!PolyLingPlayerViewerCore.InspectTopology(
                    model, meshIndex, out int faces, out var holes))
                return;

            string h = holes.Count == 0 ? "なし" : string.Join(" / ", holes);
            AddNote($"{label}: 面 {faces}  穴 {holes.Count} 個（{h} 点）");
        }

        /// <summary>名前でボーンの索引を引く。見つからなければ -1。</summary>
        private static int FindBoneIndex(ModelContext model, string name)
        {
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && mc.Type == MeshType.Bone && mc.Name == name) return i;
            }
            return -1;
        }

        // ================================================================
        // S10 Humanoid 割当
        // ================================================================

        /// <summary>
        /// 名前から自動割当する。既存のスキン生成自動検証と同じ経路
        /// （HumanoidBoneMapping.AutoMapFromEmbeddedCSV）を通す。
        /// </summary>
        private StageResult StageHumanoidMapping()
        {
            var model = GetModel?.Invoke();
            if (model == null) return StageResult.Fail;

            var names = new List<string>(model.MeshContextCount);
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                names.Add(mc != null && !string.IsNullOrEmpty(mc.Name) ? mc.Name : "");
            }

            var mapping = new HumanoidBoneMapping();
            int mapped = mapping.AutoMapFromEmbeddedCSV(names);
            if (mapped == 0) return StageResult.Fail;

            SendLogged(new ApplyHumanoidMappingCommand(ModelIndex(), mapping.Clone()));
            return StageResult.Ok;
        }

        // ================================================================
        // S11 VRM 書き出し
        // ================================================================

        /// <summary>
        /// VRM を書き出す。必須関節の補完を ON にする。
        /// 上半身だけの系統は脚が無く、そのままでは VRM ビューアが読み込みを拒否する。
        /// </summary>
        private StageResult StageExportVrm()
        {
            if (ExportVrm == null) return StageResult.Fail;

            string path = System.IO.Path.Combine(
                _outputRoot, FolderOf(_variant), "StickRobot.vrm");

            var settings = Poly_Ling.Vrm.Vrm10ExportSettings.CreateDefault();
            settings.SupplementHumanoid = true;
            settings.ExportSkinning     = UsesSkin(_variant);

            var result = ExportVrm(path, settings);
            if (result == null || !result.Success) return StageResult.Fail;

            return StageResult.Ok;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>面の重心（ローカル座標）。</summary>
        private static Vector3 FaceCenter(MeshObject mo, int faceIndex)
        {
            var f = mo.Faces[faceIndex];
            if (f?.VertexIndices == null || f.VertexIndices.Count == 0) return Vector3.zero;

            Vector3 c = Vector3.zero;
            for (int i = 0; i < f.VertexIndices.Count; i++)
                c += mo.Vertices[f.VertexIndices[i]].Position;
            return c / f.VertexIndices.Count;
        }
    }
}
