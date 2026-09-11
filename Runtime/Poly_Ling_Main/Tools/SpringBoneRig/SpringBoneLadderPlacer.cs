// Runtime/Poly_Ling_Main/Tools/SpringBoneRig/SpringBoneLadderPlacer.cs
// ============================================================
// はしご（梯子状ベルト）から揺れもの用のボーン鎖を作る
// ============================================================
//
// 【SpringBoneChainPlacer との違い】
//   あちらは数値（半径・高さ・折れ線）から鎖を置く。こちらは既にあるメッシュの
//   はしごから置く。位置がメッシュの頂点そのものに載るので、頂点とボーンの
//   対応が決まり、そのままウェイトになる。
//
// 【取り込み】
//   BeltAcquire.AcquireStrips を使う。BeltAcquire.Acquire は点列（位置）へ
//   落としてしまい、どの頂点だったかが消えるのでこちらでは使わない。
//
// ============================================================
// 【方針：節点の間引き】← ここが設計の中心。あとで迷ったらここを読むこと
// ============================================================
//
//   ボーンをはしごの段・本と 1 対 1 で置くと数が増えすぎる。そこで
//   「はしごはそのまま、ボーンを置くための粗い骨組みを別に組む」形にした。
//   はしご自身を間引かないのは、ウェイトの塗り先として全頂点が要るため。
//   間引くのはボーンの側だけで、頂点は 1 つ残らず必ずどれかの鎖へ付く。
//
//   〔格子〕
//     Strand / Stack のどちらも、いったん次の格子へ落としてから間引く。
//     こうすると間引きの実装が並べ方ごとに分かれない。
//
//       線 j … 鎖の候補 1 本ぶん（＝「はしご○本にひとつ」の「本」）
//       段 p … その線に沿った節点の位置（＝「段○段にひとつ」の「段」）
//
//     Strand … 線 j = strip 番号、段 p = rung 番号。
//              節点の位置は Left[p] と Right[p] の中点、付く頂点は 2 個。
//     Stack  … 線 j = 列番号、段 p = 縦断の位置。
//              rows[0].Left[j] → rows[0].Right[j] → rows[1].Right[j] → … と辿る
//              （BeltStackExpander の作りにより段の境目のレールは上下で共有され、
//              　同じ頂点が続けて出るので直前と同じなら足さない）。
//              節点の位置はレール頂点そのもの、付く頂点は 1 個。
//
//   〔束〕
//     本方向の間引きは束の中だけで行う。束をまたいで数えない。
//     Strand … strips 全部で 1 束。crossRows = false のとき GroupId は
//              はしご 1 本ごとに振られるため、GroupId で束ねると 1 本ずつに
//              なって本方向の間引きが一切効かなくなる。
//     Stack  … 段グループ 1 つが 1 束。列がそのまま束の中の線になる。
//
//   〔段ストライド Ns〕
//     節点を段 0, Ns, 2Ns, … に置く。末尾の段は必ず含める。
//     含めないと鎖の先が動かず、短い線が鎖 1 本ぶん（節点 2 個）に満たなくなる。
//     割り切れないときは最後の 1 区間だけ短くなる。それでよい。
//
//     塗りは、段 p を挟む 2 つの節点 stops[a] ≦ p ≦ stops[a+1] を取り、
//       t = (p - stops[a]) / (stops[a+1] - stops[a])
//     で chain[a] へ (1-t)、chain[a+1] へ t を配る。
//     Ns = 1 では stops が全段になり t = 0 となるので、
//     「その段のボーンへ 1.0」という従来の動作にそのまま一致する。
//
//   〔本ストライド Nc と 2 つの方式〕
//     Merge（まとめる）
//       線 [b·Nc, (b+1)·Nc) を 1 本の鎖に束ねる。節点の位置は束ねた線の平均。
//       束ねた線の頂点は全部その鎖へ付き、横方向の補間は無い。
//       鎖はレール頂点から外れて中間に浮く。板を 1 枚ずつ動かす感じになる。
//     Thin（間引く）
//       線 j = 0, Nc, 2Nc, … だけを鎖にする。末尾の線は必ず鎖にする。
//       間の線 j は、挟む 2 本の鎖で
//         u = (j - keeps[b]) / (keeps[b+1] - keeps[b])
//       の線形補間。鎖はレール頂点に載ったまま。なめらかに曲がる。
//
//   〔混ざるボーン数〕
//     縦 2（t）× 横 2（u）＝ 最大 4。BoneWeight の 4 スロットにちょうど収まる。
//     上位 4 件の切り捨ては起きず、正規化も掛からない純粋な線形補間になる。
//
//   〔Thin で線の長さがそろわないとき〕
//     Nc ≧ 2 のときだけ、束の中の最短の段数にそろえて stops を作る。
//     横に補間する相手と段（スロット番号）の対応を取る必要があるため。
//     そろえたぶんからあふれた段の頂点は、最後の節点へ寄せる。
//     Nc = 1 のときは線ごとに自分の長さのまま扱う。線どうしは独立で、
//     そろえる理由が無い（＝従来と同じ）。
//     Stack は 1 つの束の中で線の長さが必ずそろう（rows.Count + 1）ので、
//     そろえが効いてくるのは Strand で長さの違うはしごが混じったときだけ。
//
//   〔Strand の並び順に関する注意〕
//     BeltStackDetector は開始タグ三角形を面インデックス昇順に見て、
//     そのタグに接する先端三角形も面インデックス昇順に見る。つまり
//     strips の並びは面番号順であって、空間的な隣接順とは限らない。
//     Strand で Nc ≧ 2 を使うときは、はしごの並び順が意図どおりかを
//     人が確かめること。Stack の列番号はベルト上の rung 番号なので順序は正しい。
//
// 【座標系】
//   取り込み元の頂点はそのメッシュの格納空間。ボーンは世界へ置くので
//   source.VertexToWorldMatrix を掛けてから、親のワールド位置との差をローカル位置にする。
//   WorldMatrix ではない。スキンドの頂点は既にワールド（バインド）空間なので、
//   WorldMatrix を掛けると二重に掛かって鎖がメッシュから外れる
//   （MeshContext.cs:489-490 … IsSkinned ? identity : WorldMatrix）。
//   親のワールド位置は頂点ではなくオブジェクトの原点なので、そちらは WorldMatrix のまま。
//
// 【ウェイトを塗る契機】
//   塗ってよいのは取り込み元がスキンド系のときだけ。MeshFilter 系のうちは
//   ボーンだけ作り、塗るのはスキンド化のあとへ回す。
//   描画は per-vertex で欄を選ぶ（ウェイトを持つ頂点はボーンの欄、持たない頂点は
//   メッシュ自身の欄）ため、MeshFilter のはしごを塗ると塗った頂点だけが
//   ローカル座標のまま SkinningMatrix 経路へ移り、WorldMatrix が単位でなければ飛ぶ。
//   塗り直しはオブジェクトグループの自動更新が行う。
//
// 【ウェイト】
//   塗る先は「取り込み元のメッシュ」＝はしご自身。はしごから作ったフリルや
//   パイプは別メッシュで、頂点はここには無い。そちらへ移すのは別の機能。
//   同じ頂点が 2 つ以上の線に現れたときは足し合わせてから正規化する
//   （検出器が consumed で面を潰すため、通常は起きない）。
//
// 【BindPose】
//   親の姿勢が決まらないと入れられないので、全部足したあとに
//   SpringBoneChainPlacer.FixBindPoses を 1 回だけ呼ぶ。
//
// 【ミラー】
//   mirrorMatrix を渡すと、同じ計画をその行列で写した鎖をもう一組作り、
//   実体側とミラー側のボーンへ MirrorBoneIndex を双方向に立てる。
//
//   立てないとミラーは成立しない。MirrorPair.BuildBonePairMap は
//   MirrorBoneIndex からしか対応表を作らず（MirrorPair.cs:170-205）、
//   対応が無い頂点へは MirrorBoneWeight を書かない（同 :243-249）。
//   その状態でミラーを描くと、ミラー側は実体側と同じボーン番号を引くので
//   「右のメッシュが左のボーンで動く」。
//
//   ウェイトを塗るのは実体側だけ。ミラー側メッシュへの反映は
//   MirrorPair.SyncBoneWeights の仕事で、二重に塗ると食い違ったときに
//   どちらが正かが決まらなくなる。
//
// 【冪等（既にある鎖への流し込み）】
//   chainRootIndices に鎖の根元ボーンを渡すと、ボーンを作らずに位置だけ書き換える。
//   ObjectId が保たれるので、揺れ方の設定・選択辞書・はしごのウェイトが切れない。
//
//   鎖は根元から子を 1 つずつ辿って集める。名前は見ない（接頭辞を変えても追える）。
//   前提は「はしごから作った鎖には他のボーンを子としてリンクしない」こと。
//   子のボーンが 2 本以上ある鎖は分岐していて辿れないので、照合に落とす。
//
//   照合（合わなければ流し込まずに新規作成へ落とす）
//     ・鎖の本数が違う
//     ・辿った鎖の長さが plans[c].Worlds.Count（＋tail 1）と違う
//   どちらも「はしごを鎖の本数・段数が変わるほど編集した」ということ。
//   古い鎖は消さない。消すと揺れ方の設定ごと失われるため、残して人へ知らせる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.UI;

namespace Poly_Ling.Tools.SpringBoneRig
{
    /// <summary>はしごから鎖を作るときの並べ方。</summary>
    public enum SpringBoneLadderMode
    {
        /// <summary>髪系。はしご 1 本ごとに独立した鎖を rung 方向へ 1 本。</summary>
        Strand = 0,

        /// <summary>スカート系。段グループを縦断する鎖を列ごとに 1 本。</summary>
        Stack = 1,
    }

    /// <summary>本方向（鎖の本数）を減らすときのやり方。</summary>
    public enum SpringBoneLadderBundleMode
    {
        /// <summary>間引く。Nc 本目ごとに鎖を置き、間の線は左右の鎖で補間する。</summary>
        Thin = 0,

        /// <summary>まとめる。Nc 本を 1 本の鎖に束ね、平均位置へ置く。横の補間は無い。</summary>
        Merge = 1,
    }

    /// <summary>配置の結果。</summary>
    public sealed class SpringBoneLadderPlaceResult
    {
        /// <summary>作ったボーンの索引。鎖ごとに 1 リスト。先頭が鎖の先頭。</summary>
        public readonly List<List<int>> Chains = new List<List<int>>();

        /// <summary>
        /// 鎖 c の節点 s のボーンに載っている、取り込み元メッシュの頂点索引。
        /// 添字は Chains と同じ。節点に載っていない頂点（間引きで飛ばした段や線）は
        /// ここには出ない。鎖の先の tail は空配列。
        /// </summary>
        public readonly List<List<int[]>> SourceVertices = new List<List<int[]>>();

        /// <summary>作ったボーンの総数。</summary>
        public int BoneCount
        {
            get { int n = 0; foreach (var c in Chains) n += c.Count; return n; }
        }

        /// <summary>ウェイトを書き換えた頂点数。</summary>
        public int WeightedVertexCount;

        /// <summary>
        /// ミラー側の鎖。添字は Chains と同じ（実体側 c 本目の相方が MirrorChains[c]）。
        /// ミラーを作らなかったときは空。
        /// </summary>
        public readonly List<List<int>> MirrorChains = new List<List<int>>();

        /// <summary>既にある鎖へ位置を流し込んだ（ボーンを作っていない）。</summary>
        public bool Updated;

        /// <summary>
        /// 流し込もうとしたが照合に落ちたので新しく作った。
        /// 古い鎖はモデルに残っている。
        /// </summary>
        public bool Recreated;

        public bool   Ok;
        public string Message = "";
    }

    /// <summary>はしごから揺れもの用のボーン鎖を置く。</summary>
    public static class SpringBoneLadderPlacer
    {
        // ================================================================
        // 格子
        // ================================================================

        /// <summary>格子の節点 1 個。位置は取り込み元メッシュのローカル。</summary>
        private struct GridNode
        {
            public Vector3 Local;
            public int[]   Verts;
        }

        /// <summary>格子の線 1 本（＝鎖の候補 1 本ぶん）。</summary>
        private sealed class GridLine
        {
            public readonly List<GridNode> Nodes = new List<GridNode>();
            public int Count => Nodes.Count;
        }

        /// <summary>鎖 1 本ぶんの下ごしらえ。位置は世界、頂点は取り込み元メッシュのもの。</summary>
        private sealed class ChainPlan
        {
            public readonly List<Vector3> Worlds = new List<Vector3>();
            public readonly List<int[]>   Verts  = new List<int[]>();
        }

        /// <summary>頂点 1 個に配るウェイトの 1 件。鎖はまだ作っていないので添字で控える。</summary>
        private struct WeightRef
        {
            public int   Chain;
            public int   Slot;
            public float W;
        }

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// はしごから鎖を置く。
        /// </summary>
        /// <param name="source">取り込み元の描画オブジェクト。選択辞書を引くため MeshContext で受ける。</param>
        /// <param name="method">取り込み方。Baked は受け付けない（点列しか無く頂点が引けないため）。</param>
        /// <param name="setName">SelectionSet のときの辞書名。</param>
        /// <param name="mode">並べ方。Stack のときだけ段の横断を行う。</param>
        /// <param name="attachIndex">親にするボーンの索引。-1 で親を付けない。</param>
        /// <param name="reverseChain">鎖の根元と先を入れ替える。</param>
        /// <param name="paintWeights">取り込み元メッシュへウェイトを塗るか。</param>
        /// <param name="rungStride">段ストライド Ns。1 で間引かない。</param>
        /// <param name="chainStride">本ストライド Nc。1 で間引かない。</param>
        /// <param name="bundleMode">本方向のやり方。Thin=間引く / Merge=まとめる。</param>
        /// <param name="chainRootIndices">
        /// 既にある鎖の根元ボーンの索引。渡すとボーンを作らず位置だけ書き換える。
        /// ミラーも作る指定のときは「実体側 N 本 → ミラー側 N 本」の順で渡す。
        /// 照合に落ちたときは新しく作る（古い鎖は残る）。
        /// </param>
        /// <param name="mirrorMatrix">
        /// ミラー側の鎖を作るときの写し行列（MirrorBranchOps.MirrorMatrix）。
        /// null でミラーを作らない。
        /// </param>
        public static SpringBoneLadderPlaceResult Place(
            ModelContext model,
            MeshContext source,
            BeltAcquireMethod method,
            string setName,
            SpringBoneLadderMode mode,
            int attachIndex,
            string namePrefix,
            bool reverseChain,
            bool addTailBone,
            float tailLength,
            bool paintWeights,
            int rungStride = 1,
            int chainStride = 1,
            SpringBoneLadderBundleMode bundleMode = SpringBoneLadderBundleMode.Thin,
            IReadOnlyList<int> chainRootIndices = null,
            Matrix4x4? mirrorMatrix = null)
        {
            var result = new SpringBoneLadderPlaceResult();

            if (model == null)
            {
                result.Message = "モデルがありません。";
                return result;
            }

            var mesh = source?.MeshObject;
            if (mesh == null)
            {
                result.Message = "取り込み元のメッシュがありません。";
                return result;
            }

            if (string.IsNullOrEmpty(namePrefix)) namePrefix = "Spring";

            // ── 塗ってよいか ──
            //
            // 【MeshFilter 系のうちは塗らない】
            //   描画は per-vertex で欄を選ぶ。ウェイトを持つ頂点はボーンの欄
            //   （SkinningMatrix）、持たない頂点はメッシュ自身の欄（WorldMatrix）を引く
            //   （UnifiedBufferManager_Build.cs:411-427 / _Update.cs:1825-1830）。
            //   MeshFilter のはしごを塗ると、塗った頂点だけがローカル座標のまま
            //   SkinningMatrix 経路へ移り、WorldMatrix が単位でなければ飛ぶ。
            //
            //   ボーンは先に作ってよい。塗るのはスキンド化のあとで、
            //   オブジェクトグループの自動更新が流し直す
            //   （PlayerCommandDispatcher.RunAutoUpdateGroups）。
            bool canPaint = paintWeights && source.IsSkinned;

            string paintNote = (paintWeights && !source.IsSkinned)
                ? " 取り込み元が MeshFilter 系なのでウェイトは塗っていません（スキンド化のときに塗ります）。"
                : "";

            // ── 取り込み ──
            bool crossRows = mode == SpringBoneLadderMode.Stack;
            var picked = BeltAcquire.AcquireStrips(source, method, crossRows, setName);
            if (!picked.Ok)
            {
                result.Message = picked.Message;
                return result;
            }

            // ── 格子 ──
            var bundles = BuildBundles(mesh, picked.Strips, mode);

            // 反転は間引きより先に掛ける。あとから掛けると「末尾を必ず節点にする」
            // 手当てが鎖の根元側に付いてしまう。
            if (reverseChain)
                foreach (var bundle in bundles)
                    foreach (var line in bundle)
                        line.Nodes.Reverse();

            // ── 間引き ──
            // スキンドの頂点は既にワールド（バインド）空間。WorldMatrix を掛けると
            // 二重に掛かる。判定は MeshContext.VertexToWorldMatrix に集約してある。
            Matrix4x4 toWorld = source.VertexToWorldMatrix;

            var plans   = new List<ChainPlan>();
            var weights = new Dictionary<int, List<WeightRef>>();

            foreach (var bundle in bundles)
                ReduceBundle(bundle, rungStride, chainStride, bundleMode, toWorld, plans, weights);

            if (plans.Count == 0)
            {
                result.Message = $"鎖を作れる梯子がありません（{picked.Message}）";
                return result;
            }

            // ── 既にある鎖へ流し込む ──
            string recreateWhy = null;

            if (chainRootIndices != null && chainRootIndices.Count > 0)
            {
                if (UpdateChains(model, chainRootIndices, plans, addTailBone, tailLength,
                                 mirrorMatrix,
                                 out var upChains, out var upVerts, out var upMirror, out string why))
                {
                    result.Chains.AddRange(upChains);
                    result.SourceVertices.AddRange(upVerts);
                    result.MirrorChains.AddRange(upMirror);

                    if (canPaint)
                        result.WeightedVertexCount = PaintWeights(mesh, result.Chains, weights);

                    result.Ok      = true;
                    result.Updated = true;
                    result.Message =
                        $"鎖 {result.Chains.Count} 本 / ボーン {result.BoneCount} 本の位置を更新しました。"
                        + (canPaint ? $" ウェイトを {result.WeightedVertexCount} 頂点へ塗りました。" : "")
                        + paintNote
                        + $"（{picked.Message}）";
                    return result;
                }

                recreateWhy = why;
            }

            // ── 親 ──
            //
            // 【まだボーンでないメッシュを親にしてよい】
            //   MeshFilter 系のモデルにはボーンが 1 本も無い。取り付け先が
            //   これからボーンになるメッシュでも、位置の基準として使えるので通す。
            //   スキンド化のとき、そのメッシュのボーン配下へ付け替わる
            //   （MeshFilterToSkinnedConverter の Phase 4a）。
            Vector3 parentWorldRoot = Vector3.zero;
            if (attachIndex >= 0 && attachIndex < model.MeshContextCount)
            {
                var amc = model.GetMeshContext(attachIndex);
                if (amc == null)
                {
                    result.Message = "親にするボーンが見つかりません。";
                    return result;
                }
                parentWorldRoot = Origin(amc.WorldMatrix);
            }
            else
            {
                attachIndex = -1;
            }

            // ── ボーン（実体側）──
            BuildChains(model, plans, attachIndex, parentWorldRoot, namePrefix,
                        addTailBone, tailLength, Matrix4x4.identity,
                        result.Chains, result.SourceVertices);

            SpringBoneChainPlacer.FixBindPoses(model, result.Chains);

            // ── ボーン（ミラー側）──
            if (mirrorMatrix.HasValue)
            {
                int mAttach = MirrorPeer(model, attachIndex);

                Vector3 mParentWorld = Vector3.zero;
                if (mAttach >= 0 && mAttach < model.MeshContextCount)
                {
                    var mamc = model.GetMeshContext(mAttach);
                    if (mamc != null) mParentWorld = Origin(mamc.WorldMatrix);
                }

                // ウェイトの参照先は実体側の鎖だけ。ミラー側の頂点対応は捨てる。
                var mirrorVerts = new List<List<int[]>>();

                BuildChains(model, plans, mAttach, mParentWorld, namePrefix + MirrorSuffix,
                            addTailBone, tailLength, mirrorMatrix.Value,
                            result.MirrorChains, mirrorVerts);

                SpringBoneChainPlacer.FixBindPoses(model, result.MirrorChains);
                LinkMirrorBones(model, result.Chains, result.MirrorChains);
            }

            // ── ウェイト ──
            if (canPaint)
                result.WeightedVertexCount = PaintWeights(mesh, result.Chains, weights);

            result.Ok        = true;
            result.Recreated = recreateWhy != null;
            result.Message =
                $"鎖 {result.Chains.Count} 本 / ボーン {result.BoneCount} 本を作りました。"
                + (result.MirrorChains.Count > 0
                    ? $" ミラー側にも鎖 {result.MirrorChains.Count} 本を作り、左右のボーンを対にしました。"
                    : "")
                + (canPaint ? $" ウェイトを {result.WeightedVertexCount} 頂点へ塗りました。" : "")
                + paintNote
                + (recreateWhy != null
                    ? $" 既にある鎖へは流し込めなかったので作り直しました（{recreateWhy}）。古い鎖は残っています。"
                    : "")
                + $"（{picked.Message}）";
            return result;
        }

        /// <summary>
        /// いまの指定で鎖とボーンが何本になるかだけを数える。モデルは変えない。
        /// 実際の配置と同じ手順を通すので、数え方が本体と食い違わない。
        /// </summary>
        public static void CountPlan(
            MeshObject mesh,
            IReadOnlyList<BeltAutoStrip> strips,
            SpringBoneLadderMode mode,
            int rungStride,
            int chainStride,
            SpringBoneLadderBundleMode bundleMode,
            out int chains,
            out int bones)
        {
            chains = 0;
            bones  = 0;
            if (mesh == null) return;

            var bundles = BuildBundles(mesh, strips, mode);

            var plans   = new List<ChainPlan>();
            var weights = new Dictionary<int, List<WeightRef>>();

            foreach (var bundle in bundles)
                ReduceBundle(bundle, rungStride, chainStride, bundleMode,
                             Matrix4x4.identity, plans, weights);

            chains = plans.Count;
            foreach (var p in plans) bones += p.Worlds.Count;
        }

        // ================================================================
        // 格子の組み立て
        // ================================================================

        private static List<List<GridLine>> BuildBundles(
            MeshObject mesh, IReadOnlyList<BeltAutoStrip> strips, SpringBoneLadderMode mode)
        {
            return mode == SpringBoneLadderMode.Stack
                ? BuildStackBundles(mesh, strips)
                : BuildStrandBundles(mesh, strips);
        }

        /// <summary>
        /// 髪系。はしご 1 本が線 1 本。節点は rung の中点で、その rung の
        /// 左右レール頂点 2 個が付く。strips 全部で 1 束にする。
        /// </summary>
        private static List<List<GridLine>> BuildStrandBundles(
            MeshObject mesh, IReadOnlyList<BeltAutoStrip> strips)
        {
            var bundles = new List<List<GridLine>>();
            if (strips == null) return bundles;

            var bundle = new List<GridLine>();

            foreach (var st in strips)
            {
                if (st == null || st.RungCount < 2) continue;

                var line = new GridLine();

                for (int i = 0; i < st.RungCount; i++)
                {
                    int li = st.Left[i];
                    int ri = st.Right[i];
                    if (!InRange(mesh, li) || !InRange(mesh, ri)) continue;

                    Vector3 l = mesh.Vertices[li].Position;
                    Vector3 r = mesh.Vertices[ri].Position;

                    line.Nodes.Add(new GridNode
                    {
                        Local = (l + r) * 0.5f,
                        Verts = new[] { li, ri },
                    });
                }

                if (line.Count >= 2) bundle.Add(line);
            }

            if (bundle.Count > 0) bundles.Add(bundle);
            return bundles;
        }

        /// <summary>
        /// スカート系。段グループ 1 つが 1 束、その中の列 1 つが線 1 本。
        /// 段の境目のレールは上下で共有されるので、同じ頂点を 2 度置かない。
        /// </summary>
        private static List<List<GridLine>> BuildStackBundles(
            MeshObject mesh, IReadOnlyList<BeltAutoStrip> strips)
        {
            var bundles = new List<List<GridLine>>();
            if (strips == null) return bundles;

            foreach (var group in GroupRows(strips))
            {
                // 列数は段でそろっているのが前提だが、部分採用の段が混じると
                // 短くなることがあるので、いちばん短い段に合わせる。
                int columns = int.MaxValue;
                foreach (var row in group) columns = Mathf.Min(columns, row.RungCount);
                if (columns < 2) continue;

                var bundle = new List<GridLine>();

                for (int i = 0; i < columns; i++)
                {
                    var line = new GridLine();
                    int prev = -1;

                    foreach (var row in group)
                    {
                        AppendRail(mesh, line, row.Left[i],  ref prev);
                        AppendRail(mesh, line, row.Right[i], ref prev);
                    }

                    if (line.Count >= 2) bundle.Add(line);
                }

                if (bundle.Count > 0) bundles.Add(bundle);
            }

            return bundles;
        }

        /// <summary>レール頂点を線へ足す。直前と同じ頂点なら足さない。</summary>
        private static void AppendRail(MeshObject mesh, GridLine line, int vi, ref int prev)
        {
            if (!InRange(mesh, vi)) return;
            if (vi == prev) return;

            line.Nodes.Add(new GridNode
            {
                Local = mesh.Vertices[vi].Position,
                Verts = new[] { vi },
            });
            prev = vi;
        }

        /// <summary>
        /// 段グループごとにまとめ、RowIndex の昇順に並べる。
        /// GroupId が振られていない段は 1 段だけのグループとして扱う。
        /// </summary>
        private static List<List<BeltAutoStrip>> GroupRows(IReadOnlyList<BeltAutoStrip> strips)
        {
            var groups = new List<List<BeltAutoStrip>>();
            var byId   = new Dictionary<int, List<BeltAutoStrip>>();

            foreach (var st in strips)
            {
                if (st == null || st.RungCount < 2) continue;

                if (st.GroupId < 0)
                {
                    groups.Add(new List<BeltAutoStrip> { st });
                    continue;
                }

                if (!byId.TryGetValue(st.GroupId, out var list))
                {
                    list = new List<BeltAutoStrip>();
                    byId[st.GroupId] = list;
                    groups.Add(list);
                }
                list.Add(st);
            }

            foreach (var g in groups)
                g.Sort((a, b) => a.RowIndex.CompareTo(b.RowIndex));

            return groups;
        }

        // ================================================================
        // 間引き
        // ================================================================

        /// <summary>
        /// 束 1 つを鎖へ落とす。作った鎖は plans へ、頂点の配り先は weights へ足す。
        /// </summary>
        private static void ReduceBundle(
            List<GridLine> bundle,
            int rungStride,
            int chainStride,
            SpringBoneLadderBundleMode bundleMode,
            Matrix4x4 toWorld,
            List<ChainPlan> plans,
            Dictionary<int, List<WeightRef>> weights)
        {
            if (bundle == null || bundle.Count == 0) return;

            int lineCount = bundle.Count;
            int ns = Mathf.Max(1, rungStride);
            int nc = Mathf.Max(1, chainStride);

            if (bundleMode == SpringBoneLadderBundleMode.Merge)
            {
                ReduceMerge(bundle, lineCount, ns, nc, toWorld, plans, weights);
                return;
            }

            ReduceThin(bundle, lineCount, ns, nc, toWorld, plans, weights);
        }

        /// <summary>まとめる。Nc 本を 1 本の鎖に束ね、節点は平均位置へ置く。</summary>
        private static void ReduceMerge(
            List<GridLine> bundle, int lineCount, int ns, int nc,
            Matrix4x4 toWorld, List<ChainPlan> plans, Dictionary<int, List<WeightRef>> weights)
        {
            for (int b0 = 0; b0 < lineCount; b0 += nc)
            {
                int b1 = Mathf.Min(b0 + nc, lineCount);

                // 束ねる線で段数が違うことがあるので、いちばん短い線に合わせる。
                int steps = int.MaxValue;
                for (int j = b0; j < b1; j++) steps = Mathf.Min(steps, bundle[j].Count);
                if (steps < 2) continue;

                var stops = BuildStops(steps, ns);
                if (stops.Count < 2) continue;

                int chain = plans.Count;
                var plan  = new ChainPlan();

                for (int k = 0; k < stops.Count; k++)
                {
                    int p = stops[k];

                    Vector3 sum = Vector3.zero;
                    var vs = new List<int>();

                    for (int j = b0; j < b1; j++)
                    {
                        sum += bundle[j].Nodes[p].Local;
                        vs.AddRange(bundle[j].Nodes[p].Verts);
                    }

                    plan.Worlds.Add(toWorld.MultiplyPoint3x4(sum / (b1 - b0)));
                    plan.Verts .Add(vs.ToArray());
                }

                plans.Add(plan);

                // 束ねた線の頂点は全部この鎖へ。横の補間は無く、縦だけ補間する。
                for (int j = b0; j < b1; j++)
                {
                    var line = bundle[j];
                    for (int p = 0; p < line.Count; p++)
                    {
                        LocateSpan(stops, p, out int a, out float t);
                        AddWeight(weights, line.Nodes[p].Verts, chain, a,     1f - t);
                        AddWeight(weights, line.Nodes[p].Verts, chain, a + 1, t);
                    }
                }
            }
        }

        /// <summary>間引く。Nc 本目ごとに鎖を置き、間の線は左右の鎖で補間する。</summary>
        private static void ReduceThin(
            List<GridLine> bundle, int lineCount, int ns, int nc,
            Matrix4x4 toWorld, List<ChainPlan> plans, Dictionary<int, List<WeightRef>> weights)
        {
            var keeps = BuildStops(lineCount, nc);
            if (keeps.Count == 0) return;

            // 横に補間する相手と段の対応を取るため、Nc ≧ 2 のときだけ段数をそろえる。
            // Nc = 1 のときは線どうしが独立なので、そろえる理由が無い。
            int unified = -1;
            if (nc >= 2)
            {
                unified = int.MaxValue;
                foreach (var line in bundle) unified = Mathf.Min(unified, line.Count);
                if (unified < 2) return;
            }

            var chainOf = new int[keeps.Count];
            var stopsOf = new List<int>[keeps.Count];

            for (int b = 0; b < keeps.Count; b++)
            {
                var line  = bundle[keeps[b]];
                int steps = unified > 0 ? unified : line.Count;
                if (steps < 2) return;

                var stops = BuildStops(steps, ns);
                if (stops.Count < 2) return;

                var plan = new ChainPlan();
                for (int k = 0; k < stops.Count; k++)
                {
                    var node = line.Nodes[stops[k]];
                    plan.Worlds.Add(toWorld.MultiplyPoint3x4(node.Local));
                    plan.Verts .Add(node.Verts);
                }

                chainOf[b] = plans.Count;
                stopsOf[b] = stops;
                plans.Add(plan);
            }

            for (int j = 0; j < lineCount; j++)
            {
                LocateSpan(keeps, j, out int b, out float u);

                var line   = bundle[j];
                var stopsA = stopsOf[b];
                bool two   = u > 0f && b + 1 < keeps.Count;

                for (int p = 0; p < line.Count; p++)
                {
                    LocateSpan(stopsA, p, out int a, out float t);

                    var verts = line.Nodes[p].Verts;

                    AddWeight(weights, verts, chainOf[b], a,     (1f - u) * (1f - t));
                    AddWeight(weights, verts, chainOf[b], a + 1, (1f - u) * t);

                    if (!two) continue;

                    // Nc ≧ 2 のときは段数をそろえてあるので、stops は隣の鎖と同じ。
                    AddWeight(weights, verts, chainOf[b + 1], a,     u * (1f - t));
                    AddWeight(weights, verts, chainOf[b + 1], a + 1, u * t);
                }
            }
        }

        /// <summary>
        /// 0, stride, 2·stride, … を並べ、末尾（count-1）が入っていなければ足す。
        /// stride = 1 なら 0..count-1 がそのまま並ぶ。
        /// </summary>
        private static List<int> BuildStops(int count, int stride)
        {
            var stops = new List<int>();
            if (count <= 0) return stops;
            if (stride < 1) stride = 1;

            for (int p = 0; p < count; p += stride) stops.Add(p);

            if (stops[stops.Count - 1] != count - 1) stops.Add(count - 1);
            return stops;
        }

        /// <summary>
        /// 値 v を挟む区間 stops[a] ≦ v ≦ stops[a+1] と、その中での比 t を取る。
        /// 端より外は端へ寄せる（t = 0 で stops[a] へ 1.0）。
        /// </summary>
        private static void LocateSpan(List<int> stops, int v, out int a, out float t)
        {
            int last = stops.Count - 1;

            if (v <= stops[0])    { a = 0;    t = 0f; return; }
            if (v >= stops[last]) { a = last; t = 0f; return; }

            for (int k = 0; k < last; k++)
            {
                if (v < stops[k] || v > stops[k + 1]) continue;

                int span = stops[k + 1] - stops[k];
                a = k;
                t = span > 0 ? (float)(v - stops[k]) / span : 0f;
                return;
            }

            a = last;
            t = 0f;
        }

        /// <summary>頂点へウェイトを 1 件足す。同じ鎖・同じ節点なら加算する。</summary>
        private static void AddWeight(
            Dictionary<int, List<WeightRef>> weights, int[] verts, int chain, int slot, float w)
        {
            if (verts == null || w <= 0f) return;

            for (int n = 0; n < verts.Length; n++)
            {
                int vi = verts[n];

                if (!weights.TryGetValue(vi, out var list))
                {
                    list = new List<WeightRef>(4);
                    weights[vi] = list;
                }

                bool merged = false;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Chain != chain || list[i].Slot != slot) continue;
                    var e = list[i];
                    e.W += w;
                    list[i] = e;
                    merged = true;
                    break;
                }

                if (!merged) list.Add(new WeightRef { Chain = chain, Slot = slot, W = w });
            }
        }

        // ================================================================
        // ボーンの組み立て
        // ================================================================

        /// <summary>ミラー側の鎖の名前に付ける接尾辞。</summary>
        private const string MirrorSuffix = "_M";

        /// <summary>
        /// 計画からボーン鎖を作る。xform は位置に掛ける写し行列
        /// （実体側は単位、ミラー側はミラー行列）。
        /// BindPose は呼び出し側が全部足したあとに入れ直す。
        /// </summary>
        private static void BuildChains(
            ModelContext model,
            List<ChainPlan> plans,
            int attachIndex,
            Vector3 parentWorldRoot,
            string namePrefix,
            bool addTailBone,
            float tailLength,
            Matrix4x4 xform,
            List<List<int>> outChains,
            List<List<int[]>> outVerts)
        {
            for (int c = 0; c < plans.Count; c++)
            {
                var plan = plans[c];

                string chainName = plans.Count > 1
                    ? $"{namePrefix}_{c:00}"
                    : namePrefix;

                var indices = new List<int>(plan.Worlds.Count);
                var verts   = new List<int[]>(plan.Worlds.Count);

                int     parent      = attachIndex;
                Vector3 parentWorld = parentWorldRoot;

                for (int i = 0; i < plan.Worlds.Count; i++)
                {
                    string boneName = i == 0
                        ? $"{chainName}_top"
                        : $"{chainName}_{i}";

                    Vector3 world = xform.MultiplyPoint3x4(plan.Worlds[i]);

                    int added = SpringBoneChainPlacer.AddBone(
                        model, boneName, parent, world - parentWorld);

                    indices.Add(added);
                    verts.Add(plan.Verts[i]);

                    parent      = added;
                    parentWorld = world;
                }

                // 鎖の先は tail 扱いで揺れないので、必要なら 1 本足す。
                // 足したボーンは節点ではないので、ウェイトの参照先にはならない。
                if (addTailBone && plan.Worlds.Count >= 2)
                {
                    int last = plan.Worlds.Count - 1;

                    Vector3 w1 = xform.MultiplyPoint3x4(plan.Worlds[last]);
                    Vector3 w0 = xform.MultiplyPoint3x4(plan.Worlds[last - 1]);

                    Vector3 dir = w1 - w0;
                    if (dir.sqrMagnitude <= 1e-10f) dir = new Vector3(0f, -1f, 0f);
                    dir = dir.normalized;

                    float len = tailLength > 0f ? tailLength : 0.05f;
                    indices.Add(SpringBoneChainPlacer.AddBone(
                        model, $"{chainName}_end", parent, dir * len));
                    verts.Add(System.Array.Empty<int>());
                }

                outChains.Add(indices);
                outVerts.Add(verts);
            }
        }

        /// <summary>
        /// 実体側とミラー側のボーンへ MirrorBoneIndex を双方向に立てる。
        ///
        /// ここで立てるのは「1 対 1 で作った瞬間の確定値」。
        /// 名前からの推定（MirrorBoneIndexResolver）に頼らないのは、
        /// 鎖の名前が左右を持たないうえ、推定は名前規則が崩れた瞬間に
        /// 黙って外れるため（MirrorBoneIndexResolver.cs:10-18 と同じ理由）。
        /// </summary>
        private static void LinkMirrorBones(
            ModelContext model, List<List<int>> real, List<List<int>> mirror)
        {
            int n = Mathf.Min(real.Count, mirror.Count);

            for (int c = 0; c < n; c++)
            {
                var a = real[c];
                var b = mirror[c];
                if (a == null || b == null) continue;

                int k = Mathf.Min(a.Count, b.Count);
                for (int i = 0; i < k; i++)
                {
                    var ma = model.GetMeshContext(a[i]);
                    var mb = model.GetMeshContext(b[i]);
                    if (ma == null || mb == null) continue;

                    ma.MirrorBoneIndex = b[i];
                    mb.MirrorBoneIndex = a[i];
                }
            }
        }

        /// <summary>
        /// ボーンの左右の相方を引く。無ければ元の索引をそのまま返す
        /// （中心のボーンに取り付けたときは左右で同じ親になる）。
        /// </summary>
        private static int MirrorPeer(ModelContext model, int index)
        {
            if (index < 0 || index >= model.MeshContextCount) return index;

            int peer = model.GetMeshContext(index)?.MirrorBoneIndex ?? -1;
            return (peer >= 0 && peer < model.MeshContextCount) ? peer : index;
        }

        // ================================================================
        // 既にある鎖への流し込み
        // ================================================================

        /// <summary>
        /// 既にある鎖へ位置を流し込む。ボーンは作らず、増やさず、消さない。
        ///
        /// 照合に落ちたら false を返し、why に理由を入れる。そのときモデルは
        /// 1 か所も書き換えていない（先に全部の鎖を辿って照合し、
        /// 全部通ってから書き込む）。途中まで書いて失敗すると、
        /// 作り直しの結果と混ざって原因が追えなくなる。
        /// </summary>
        private static bool UpdateChains(
            ModelContext model,
            IReadOnlyList<int> rootIndices,
            List<ChainPlan> plans,
            bool addTailBone,
            float tailLength,
            Matrix4x4? mirrorMatrix,
            out List<List<int>> chains,
            out List<List<int[]>> sourceVertices,
            out List<List<int>> mirrorChains,
            out string why)
        {
            chains         = null;
            sourceVertices = null;
            mirrorChains   = null;
            why            = "";

            if (model == null) { why = "モデルがありません"; return false; }

            // 控えの本数は「実体だけ N 本」か「実体 N 本＋ミラー N 本」のどちらか。
            bool withMirror;

            if (rootIndices.Count == plans.Count)
            {
                withMirror = false;
            }
            else if (rootIndices.Count == plans.Count * 2)
            {
                if (!mirrorMatrix.HasValue)
                {
                    why = "ミラーぶんの鎖が控えてあるが、ミラーの設定が引けない";
                    return false;
                }
                withMirror = true;
            }
            else
            {
                why = $"鎖の本数が違う（はしごから {plans.Count} 本 / 控えは {rootIndices.Count} 本）";
                return false;
            }

            var childrenOf = Poly_Ling.Ops.MeshHierarchyOps.BuildChildrenTable(model);

            var found = new List<List<int>>(rootIndices.Count);

            for (int c = 0; c < rootIndices.Count; c++)
            {
                int root = rootIndices[c];
                int planIndex = c % plans.Count;

                var rootCtx = (root >= 0 && root < model.MeshContextCount)
                    ? model.GetMeshContext(root) : null;
                if (rootCtx == null || rootCtx.Type != MeshType.Bone)
                {
                    why = $"鎖 {c} の根元ボーンが見つかりません（索引 {root}）";
                    return false;
                }

                if (!TraceChain(model, childrenOf, root, out var chain, out string traceWhy))
                {
                    why = $"鎖 {c}: {traceWhy}";
                    return false;
                }

                int expected = plans[planIndex].Worlds.Count
                             + (addTailBone && plans[planIndex].Worlds.Count >= 2 ? 1 : 0);

                if (chain.Count != expected)
                {
                    why = $"鎖 {c} の長さが違う（はしごから {expected} 個 / 今 {chain.Count} 個）";
                    return false;
                }

                found.Add(chain);
            }

            // ── ここから書き込み。照合は全部通っている ──
            model.ComputeWorldMatrices();

            chains         = new List<List<int>>(plans.Count);
            sourceVertices = new List<List<int[]>>(plans.Count);
            mirrorChains   = new List<List<int>>();

            for (int c = 0; c < found.Count; c++)
            {
                bool isMirror = withMirror && c >= plans.Count;

                var chain  = found[c];
                var plan   = plans[c % plans.Count];
                var verts  = new List<int[]>(chain.Count);

                Matrix4x4 xform = isMirror ? mirrorMatrix.Value : Matrix4x4.identity;

                // 親は既にある鎖のものを使う。コマンドの取り付け先で付け替えると
                // 鎖ごと動いてしまい、位置だけ直す操作にならない。
                int parentIndex = model.GetMeshContext(chain[0])?.HierarchyParentIndex ?? -1;
                var parentCtx   = (parentIndex >= 0 && parentIndex < model.MeshContextCount)
                    ? model.GetMeshContext(parentIndex) : null;

                Vector3 parentWorld = parentCtx != null
                    ? Origin(parentCtx.WorldMatrix)
                    : Vector3.zero;

                for (int i = 0; i < plan.Worlds.Count; i++)
                {
                    Vector3 world = xform.MultiplyPoint3x4(plan.Worlds[i]);

                    var mc = model.GetMeshContext(chain[i]);
                    if (mc?.BoneTransform != null)
                        mc.BoneTransform.Position = world - parentWorld;

                    verts.Add(plan.Verts[i]);
                    parentWorld = world;
                }

                // 鎖の先の tail は節点ではないので、向きと長さだけ入れ直す。
                if (chain.Count > plan.Worlds.Count)
                {
                    int last = plan.Worlds.Count - 1;

                    Vector3 dir = last >= 1
                        ? xform.MultiplyPoint3x4(plan.Worlds[last])
                          - xform.MultiplyPoint3x4(plan.Worlds[last - 1])
                        : new Vector3(0f, -1f, 0f);
                    if (dir.sqrMagnitude <= 1e-10f) dir = new Vector3(0f, -1f, 0f);
                    dir = dir.normalized;

                    float len = tailLength > 0f ? tailLength : 0.05f;

                    var tailCtx = model.GetMeshContext(chain[chain.Count - 1]);
                    if (tailCtx?.BoneTransform != null)
                        tailCtx.BoneTransform.Position = dir * len;

                    verts.Add(System.Array.Empty<int>());
                }

                // ウェイトの配り先は実体側の鎖だけ。ミラー側は
                // MirrorPair.SyncBoneWeights が対応表を通して書く。
                if (isMirror)
                {
                    mirrorChains.Add(chain);
                }
                else
                {
                    chains.Add(chain);
                    sourceVertices.Add(verts);
                }
            }

            SpringBoneChainPlacer.FixBindPoses(model, chains);
            if (mirrorChains.Count > 0)
            {
                SpringBoneChainPlacer.FixBindPoses(model, mirrorChains);

                // 流し込みでは索引が変わらないので、対応は既に立っている。
                // 立て直すのは、片側だけ作り直したときの取りこぼしを防ぐため。
                LinkMirrorBones(model, chains, mirrorChains);
            }

            return true;
        }

        /// <summary>
        /// 根元から子を 1 つずつ辿って鎖を集める。名前は見ない。
        /// 子のボーンが 2 本以上あるところで止めて false を返す
        /// （はしごから作った鎖は分岐しない。分岐しているなら別のボーンが
        /// 　子として付けられており、辿った先が鎖とは限らない）。
        /// </summary>
        private static bool TraceChain(
            ModelContext model, Dictionary<int, List<int>> childrenOf, int root,
            out List<int> chain, out string why)
        {
            chain = new List<int>();
            why   = "";

            var seen = new HashSet<int>();
            int cur  = root;

            while (cur >= 0 && seen.Add(cur))
            {
                chain.Add(cur);

                int next  = -1;
                int kidsN = 0;

                if (childrenOf.TryGetValue(cur, out var kids))
                {
                    foreach (int k in kids)
                    {
                        var kmc = model.GetMeshContext(k);
                        if (kmc == null || kmc.Type != MeshType.Bone) continue;
                        kidsN++;
                        if (next < 0) next = k;
                    }
                }

                if (kidsN > 1)
                {
                    why = "鎖が分岐しています（ボーンの子が 2 本以上）";
                    return false;
                }

                cur = next;
            }

            if (chain.Count < 2)
            {
                why = "鎖が 1 個しかありません";
                return false;
            }

            return true;
        }

        // ================================================================
        // ウェイト
        // ================================================================

        /// <summary>
        /// 控えた配り先を BoneWeight にして書き込む。既存のウェイトは置き換える。
        /// 戻り値は書き換えた頂点数。
        /// 通常は 1 頂点あたり最大 4 件（縦 2 × 横 2）なので切り捨ては起きない。
        /// </summary>
        private static int PaintWeights(
            MeshObject mesh, List<List<int>> chains, Dictionary<int, List<WeightRef>> weights)
        {
            int count = 0;

            foreach (var kv in weights)
            {
                int vi = kv.Key;
                if (!InRange(mesh, vi)) continue;

                var list  = kv.Value;
                var slots = new (int idx, float w)[4];
                for (int i = 0; i < 4; i++) slots[i] = (0, 0f);

                int filled = 0;
                while (filled < 4)
                {
                    int best = -1;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].W > 0f && (best < 0 || list[i].W > list[best].W)) best = i;

                    if (best < 0) break;

                    var got = list[best];

                    var cleared = got;
                    cleared.W = 0f;
                    list[best] = cleared;

                    if (got.Chain < 0 || got.Chain >= chains.Count) continue;

                    var chain = chains[got.Chain];
                    if (got.Slot < 0 || got.Slot >= chain.Count) continue;

                    slots[filled] = (chain[got.Slot], got.W);
                    filled++;
                }

                if (filled == 0) continue;

                mesh.Vertices[vi].BoneWeight =
                    SkinWeightOps.NormalizeBoneWeight(SkinWeightOps.Pack(slots));
                count++;
            }

            // ウェイトを持たなかった頂点にも書き込むため、無 → 有の遷移点。
            // 描画オブジェクトの種別を確定させる。
            if (count > 0) mesh.RecomputeSkinKind();

            return count;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static bool InRange(MeshObject mesh, int vi)
            => vi >= 0 && vi < mesh.VertexCount && mesh.Vertices[vi] != null;

        private static Vector3 Origin(Matrix4x4 m) => new Vector3(m.m03, m.m13, m.m23);
    }
}
