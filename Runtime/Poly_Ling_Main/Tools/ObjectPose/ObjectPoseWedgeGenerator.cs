// ObjectPoseWedgeGenerator.cs
// メッシュオブジェクトの姿勢（BoneTransform / 原点CSV読込の結果）を
// 表示用のくさびオブジェクト列に起こす。ModelContext は読むだけで書き換えない。
// 挿入は ObjectPoseWedgeInserter が行う。
// Runtime/Poly_Ling_Main/Tools/ObjectPose/ に配置
//
// 【対象】
//   MeshType.Mesh のみ。ボーン / モーフ / 剛体 / グループと、
//   ミラー側（MirrorSide / BakedMirror）はその配下ごと対象外（実体側のみ）。
//
// 【名前】
//   元メッシュ名 + "_bone"。
//   「メッシュからボーンとスキンの生成」はメッシュ自体をスキンド化するため、
//   ボーンが元の名前を継いでメッシュ側に "_skinned" が付く（MeshNameSuffix）。
//   こちらは元メッシュに一切触らないので、逆に生成物側へ "_bone" を付ける。
//   これで元メッシュと名前が衝突しない。
//
// 【くさびを作らないもの（空のオブジェクトにする）】
//   ・ローカル位置が 0,0,0 かつ回転なし のオブジェクト
//     （ただしヒエラルキのルートにあるものは 0,0,0 でもくさびを作る）
//   階層を保つためにノード自体は作る。
//
//   頂点の有無は見ない。@ 付きの関節のように頂点を持たないオブジェクトこそ
//   姿勢の情報を持っているため、これを外すと大半のくさびが消える。
//   「空のオブジェクトは無視する」は取り込み側の話で、姿勢を持たない
//   （＝くさび形状が無い）ノードを飛ばして元の姿勢を残すことを指す。
//
// 【座標】
//   くさびの頂点はワールド座標で書く（グローバル原点として作る）。
//   挿入側が BoneTransform を単位にするので、見た目の位置がそのまま保たれる。
//
// 【大きさ】
//   全長 = WedgeLength × そのオブジェクトのワールド拡大率の平均。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Tools.ObjectPose
{
    /// <summary>生成するくさび1個ぶんの計画。</summary>
    public sealed class ObjectPoseWedgePiece
    {
        /// <summary>オブジェクト名（元メッシュ名そのまま）。</summary>
        public string Name;

        /// <summary>くさびメッシュ。頂点0なら空のオブジェクト。</summary>
        public MeshObject Mesh;

        /// <summary>親の Piece 索引（-1 = コンテナ直下）。</summary>
        public int ParentPieceIndex = -1;

        /// <summary>コンテナからの相対深さ（0 = コンテナ直下）。</summary>
        public int Depth;

        /// <summary>由来の MeshContextList 索引。</summary>
        public int SourceIndex = -1;

        /// <summary>くさび形状を持つか（false = 空のオブジェクト）。</summary>
        public bool HasWedge;
    }

    public static class ObjectPoseWedgeGenerator
    {
        /// <summary>くさびをぶら下げる空オブジェクトの既定名。</summary>
        public const string DefaultContainerName = "ObjectPose";

        /// <summary>くさびの既定の全長。</summary>
        public const float DefaultWedgeLength = 0.1f;

        /// <summary>くさびオブジェクト名に付ける接尾辞。</summary>
        public const string WedgeNameSuffix = "_bone";

        /// <summary>ローカル姿勢を「なし」とみなす許容差。</summary>
        private const float IdentityEpsilon = 1e-5f;

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// くさびの計画を DFS 前順（親が先）で返す。
        /// </summary>
        public static List<ObjectPoseWedgePiece> Generate(ModelContext model, float wedgeLength)
        {
            var pieces = new List<ObjectPoseWedgePiece>();
            if (model == null) return pieces;
            if (wedgeLength <= 0f) wedgeLength = DefaultWedgeLength;

            int count = model.MeshContextCount;
            if (count == 0) return pieces;

            model.ComputeWorldMatrices();

            // ── 対象判定 ─────────────────────────────────────────────
            // 実体側の通常メッシュのみ。ミラー側は自分だけでなく配下も丸ごと外す
            // （ミラー側の下にぶら下がる作業用オブジェクトを拾わないため）。
            var included = new bool[count];
            for (int i = 0; i < count; i++)
                included[i] = IsTargetSelf(model, i) && !HasMirrorSideAncestor(model, i);

            // ── 親子索引（対象内で最も近い祖先へ寄せる）─────────────
            var parentOf = new int[count];
            for (int i = 0; i < count; i++)
            {
                parentOf[i] = -1;
                if (!included[i]) continue;

                int cur = model.GetMeshContext(i)?.HierarchyParentIndex ?? -1;
                int safety = count + 1;
                while (cur >= 0 && cur < count && safety-- > 0)
                {
                    if (included[cur]) { parentOf[i] = cur; break; }
                    cur = model.GetMeshContext(cur)?.HierarchyParentIndex ?? -1;
                }
            }

            var childrenOf = new Dictionary<int, List<int>>();
            var roots = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (!included[i]) continue;
                int p = parentOf[i];
                if (p < 0) { roots.Add(i); continue; }
                if (!childrenOf.TryGetValue(p, out var list))
                {
                    list = new List<int>();
                    childrenOf[p] = list;
                }
                list.Add(i);
            }

            // ── DFS 前順で Piece を並べる ────────────────────────────
            var stack = new Stack<(int Index, int Depth, int ParentPiece)>();
            for (int i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 0, -1));

            while (stack.Count > 0)
            {
                var (index, depth, parentPiece) = stack.Pop();

                var piece = BuildPiece(model, index, wedgeLength, isRoot: depth == 0);
                piece.Depth            = depth;
                piece.ParentPieceIndex = parentPiece;

                int pieceIndex = pieces.Count;
                pieces.Add(piece);

                if (!childrenOf.TryGetValue(index, out var children)) continue;
                for (int c = children.Count - 1; c >= 0; c--)
                    stack.Push((children[c], depth + 1, pieceIndex));
            }

            return pieces;
        }

        // ================================================================
        // 個別生成
        // ================================================================

        private static ObjectPoseWedgePiece BuildPiece(
            ModelContext model, int index, float wedgeLength, bool isRoot)
        {
            var mc = model.GetMeshContext(index);

            var piece = new ObjectPoseWedgePiece
            {
                Name        = ToWedgeName(mc?.Name),
                SourceIndex = index,
                HasWedge    = false,
            };

            if (mc == null)
            {
                piece.Mesh = new MeshObject(piece.Name);
                return piece;
            }

            bool localIsIdentity = IsLocalIdentity(mc);

            // ローカル姿勢が単位のものは空のオブジェクトにする。
            // ただしルートは 0,0,0 でもそのまま作る。
            // 頂点の有無は条件に入れない（関節は頂点0でも姿勢を持つ）。
            if (localIsIdentity && !isRoot)
            {
                piece.Mesh = new MeshObject(piece.Name);
                return piece;
            }

            Matrix4x4 world = mc.WorldMatrix;
            Vector3    pos  = ObjectPoseWedgeShape.PositionOf(world);
            Quaternion rot  = ObjectPoseWedgeShape.RotationOf(world);
            float      size = wedgeLength * ObjectPoseWedgeShape.AverageScaleOf(world);
            if (size <= 0f) size = wedgeLength;

            piece.Mesh     = ObjectPoseWedgeShape.Build(
                piece.Name, Matrix4x4.TRS(pos, rot, Vector3.one), size);
            piece.HasWedge = true;
            return piece;
        }

        // ================================================================
        // 名前
        // ================================================================

        /// <summary>元メッシュ名 → くさびオブジェクト名。</summary>
        public static string ToWedgeName(string meshName)
        {
            if (string.IsNullOrEmpty(meshName)) meshName = "Object";
            return meshName + WedgeNameSuffix;
        }

        /// <summary>
        /// くさびオブジェクト名 → 適用先の元メッシュ名。
        /// 接尾辞が無ければそのまま返す（手で名前を付け替えた場合の逃げ道）。
        /// </summary>
        public static string ToMeshName(string wedgeName)
        {
            if (string.IsNullOrEmpty(wedgeName)) return wedgeName;
            if (wedgeName.Length <= WedgeNameSuffix.Length) return wedgeName;
            if (!wedgeName.EndsWith(WedgeNameSuffix)) return wedgeName;
            return wedgeName.Substring(0, wedgeName.Length - WedgeNameSuffix.Length);
        }

        // ================================================================
        // 判定
        // ================================================================

        /// <summary>そのコンテキスト単体が対象か（実体側の通常メッシュのみ）。</summary>
        private static bool IsTargetSelf(ModelContext model, int index)
        {
            var mc = model.GetMeshContext(index);
            if (mc == null) return false;
            if (mc.Type != MeshType.Mesh) return false;      // ボーン/モーフ/剛体/群/ミラー側を除外
            if (string.IsNullOrEmpty(mc.Name)) return false;
            return true;
        }

        /// <summary>自身または祖先にミラー側（MirrorSide / BakedMirror）がいるか。</summary>
        private static bool HasMirrorSideAncestor(ModelContext model, int index)
        {
            int count  = model.MeshContextCount;
            int cur    = index;
            int safety = count + 1;

            while (cur >= 0 && cur < count && safety-- > 0)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) return false;
                if (MirrorBranchOps.IsMirrorSideContext(mc)) return true;
                cur = mc.HierarchyParentIndex;
            }
            return false;
        }

        /// <summary>ローカル姿勢が「位置 0,0,0・回転なし」か（拡大は見ない）。</summary>
        private static bool IsLocalIdentity(MeshContext mc)
        {
            var bt = mc.BoneTransform;
            if (bt == null || !bt.UseLocalTransform) return true;
            if (bt.Position.sqrMagnitude > IdentityEpsilon * IdentityEpsilon) return false;

            Vector3 r = bt.Rotation;
            return Mathf.Abs(Mathf.DeltaAngle(r.x, 0f)) <= IdentityEpsilon
                && Mathf.Abs(Mathf.DeltaAngle(r.y, 0f)) <= IdentityEpsilon
                && Mathf.Abs(Mathf.DeltaAngle(r.z, 0f)) <= IdentityEpsilon;
        }
    }
}
