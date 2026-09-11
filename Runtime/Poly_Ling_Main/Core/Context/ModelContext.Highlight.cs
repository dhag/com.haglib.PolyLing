// ModelContext.Highlight.cs
// ModelContext：揺れもの編集の強調表示と Humanoid ボーンマッピング。
// Runtime/Poly_Ling_Main/Core/Context/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.Symmetry;
using Poly_Ling.UndoSystem;
using Poly_Ling.Materials;

namespace Poly_Ling.Context
{
    public partial class ModelContext
    {
        // ================================================================
        // 揺れもの編集の強調表示（表示専用。保存しない）
        //   揺れもの編集パネルが「いまどの鎖のどのノードを触っているか」を
        //   3D 画面へ伝えるためだけの一時データ。
        //
        //   【保存しない理由】
        //     モデルの中身ではなく、パネルを開いている間だけの見え方の指定。
        //     CSV/JSON どちらにも書き出さないこと（規約4 の対称性の対象外）。
        //
        //   【誰が読むか】
        //     MeshSceneRenderer.PrepareBones / SubmitBones。
        //     中身を変えたら、ビューポートへ作り直しを促すこと
        //     （PlayerViewportManager.MarkAllSlotsDirty）。
        // ================================================================

        /// <summary>強調表示する鎖のメンバー（masterIndex）。空＝強調なし。</summary>
        public List<int> SpringBoneHighlightIndices { get; set; } = new List<int>();

        /// <summary>いま編集しているノード（masterIndex）。-1＝なし。</summary>
        public int SpringBoneHighlightActiveIndex { get; set; } = -1;

        /// <summary>強調表示を消す。</summary>
        public void ClearSpringBoneHighlight()
        {
            SpringBoneHighlightIndices?.Clear();
            SpringBoneHighlightActiveIndex = -1;
        }

        /// <summary>
        /// 指定MeshContextが属するMirrorPairを取得（実体側・ミラー側どちらでも検索）
        /// </summary>
        public MirrorPair GetMirrorPair(MeshContext meshContext)
        {
            if (meshContext == null || MirrorPairs == null) return null;
            for (int i = 0; i < MirrorPairs.Count; i++)
            {
                var pair = MirrorPairs[i];
                if (pair.Real == meshContext || pair.Mirror == meshContext)
                    return pair;
            }
            return null;
        }

        /// <summary>
        /// 指定MeshContextがミラー側（編集不可側）かどうか
        /// MirrorPair方式と旧BakedMirror方式の両方をチェック
        /// </summary>
        public bool IsMirrorSide(MeshContext meshContext)
        {
            if (meshContext == null) return false;

            // 旧BakedMirror方式
            if (meshContext.IsBakedMirror) return true;

            // MirrorPair方式
            if (MirrorPairs != null)
            {
                for (int i = 0; i < MirrorPairs.Count; i++)
                {
                    if (MirrorPairs[i].Mirror == meshContext)
                        return true;
                }
            }

            // ミラー側モーフ。Real 側から自動同期される派生物なので、
            // メッシュのミラー側と同じく編集不可側として扱う
            // （規約は MorphMirrorPolicy.cs を正典とする）。
            // 親はモーフではないため IsMirrorSideMorph 側で再帰は止まる。
            if (IsMirrorSideMorph(meshContext)) return true;

            return false;
        }

        /// <summary>
        /// 指定MeshContextが実体側（ミラー元）かどうか
        /// </summary>
        public bool IsRealSide(MeshContext meshContext)
        {
            if (meshContext == null || MirrorPairs == null) return false;
            for (int i = 0; i < MirrorPairs.Count; i++)
            {
                if (MirrorPairs[i].Real == meshContext)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 全ミラーペアの位置を同期する（Real→Mirror）
        /// </summary>
        public void SyncAllMirrorPositions()
        {
            if (MirrorPairs == null) return;
            for (int i = 0; i < MirrorPairs.Count; i++)
            {
                MirrorPairs[i].SyncPositions();
            }
        }

        /// <summary>
        /// 指定モーフがミラー側（＝親がミラー側）のモーフかどうか。
        ///
        /// ミラー側モーフは Real 側から自動同期される派生物なので、
        /// 編集対象・選択対象として扱ってはならない
        /// （規約は MorphMirrorPolicy.cs を正典とする）。
        /// </summary>
        public bool IsMirrorSideMorph(MeshContext morphCtx)
        {
            if (morphCtx == null || !morphCtx.IsMorph) return false;

            int parentIdx = morphCtx.MorphParentIndex;
            if (parentIdx < 0 || parentIdx >= Count) return false;

            return IsMirrorSide(GetMeshContext(parentIdx));
        }

        /// <summary>
        /// Real 側モーフの編集結果を、対応するミラー側モーフへ反映する（Real→Mirror）。
        ///
        /// ②派生ミラー実体ではミラー側モーフが独立した MeshContext として実在するため、
        /// Real 側を編集しただけでは追随しない。本メソッドが左右対応
        /// （MirrorPair.VertexMap）を使ってミラー側を作り直す。
        /// 規約は MorphMirrorPolicy.cs を正典とする。
        ///
        /// 【対象外】
        ///   MorphMirrorPolicy.NoMirror … 片側だけに効くモーフなので同期しない。
        ///   MorphMirrorPolicy.MirrorOf … 鏡像として導出する形式。導出は別途行う。
        ///   MirrorPair を持たない親  … FindMirrorMorph が相手を引けないため対象外。
        ///                               生成ミラー(MirrorGeometryDerived)はこの経路に乗らない。
        /// </summary>
        /// <returns>同期したミラー側モーフの数</returns>
        public int SyncAllMirrorMorphs()
        {
            if (MirrorPairs == null || MirrorPairs.Count == 0) return 0;

            int synced = 0;

            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var realMorph = MeshContextList[i];
                if (realMorph == null || !realMorph.IsMorph) continue;
                if (realMorph.MeshObject == null) continue;

                // 派生物側からは同期しない（Real→Mirror の一方向）
                if (IsMirrorSideMorph(realMorph)) continue;

                // ポリシー上ミラーへ写さないものは触らない
                if (realMorph.MorphMirrorPolicy != MorphMirrorPolicy.FollowParent) continue;

                int parentIdx = realMorph.MorphParentIndex;
                if (parentIdx < 0 || parentIdx >= Count) continue;

                var pair = GetMirrorPair(GetMeshContext(parentIdx));
                if (pair == null || !pair.IsValid) continue;

                var mirrorMorph = FindMirrorMorph(realMorph);
                if (mirrorMorph?.MeshObject == null || mirrorMorph.MorphBaseData == null) continue;

                pair.SyncMorphSymmetric(
                    realMorph.MorphBaseData, mirrorMorph.MorphBaseData,
                    realMorph.MeshObject, mirrorMorph.MeshObject);

                mirrorMorph.MeshObject.InvalidatePositionCache();
                mirrorMorph.ApplyVertexPositionsToMesh();

                synced++;
            }

            return synced;
        }

        /// <summary>
        /// モーフMeshContextのミラー側カウンターパートを検索する。
        /// MorphParentIndex→MirrorPair→同一MorphExpression内のミラー側モーフを返す。
        /// </summary>
        /// <param name="morphCtx">Real側のモーフMeshContext</param>
        /// <returns>Mirror側のモーフMeshContext、見つからない場合null</returns>
        public MeshContext FindMirrorMorph(MeshContext morphCtx)
        {
            if (morphCtx == null || !morphCtx.IsMorph) return null;

            // モーフの親メッシュを取得
            int parentIdx = morphCtx.MorphParentIndex;
            if (parentIdx < 0 || parentIdx >= Count) return null;
            var parentCtx = GetMeshContext(parentIdx);

            // 親メッシュのMirrorPairを検索（Real側であること）
            var pair = GetMirrorPair(parentCtx);
            if (pair == null || pair.Real != parentCtx) return null;

            // Mirror側親メッシュのインデックス
            int mirrorParentIdx = MeshContextList.IndexOf(pair.Mirror);
            if (mirrorParentIdx < 0) return null;

            // このモーフが属するMorphExpressionを検索
            int morphIdx = MeshContextList.IndexOf(morphCtx);
            if (morphIdx < 0) return null;

            foreach (var expr in MorphExpressions)
            {
                bool containsThisMorph = false;
                for (int i = 0; i < expr.MeshEntries.Count; i++)
                {
                    if (expr.MeshEntries[i].MeshIndex == morphIdx)
                    {
                        containsThisMorph = true;
                        break;
                    }
                }
                if (!containsThisMorph) continue;

                // 同じMorphExpression内でMirror側親のモーフを検索
                for (int i = 0; i < expr.MeshEntries.Count; i++)
                {
                    int candidateIdx = expr.MeshEntries[i].MeshIndex;
                    if (candidateIdx == morphIdx) continue;
                    if (candidateIdx < 0 || candidateIdx >= Count) continue;

                    var candidate = GetMeshContext(candidateIdx);
                    if (candidate != null && candidate.IsMorph && candidate.MorphParentIndex == mirrorParentIdx)
                        return candidate;
                }
            }

            return null;
        }

        // ================================================================
        // Humanoidボーンマッピング
        // ================================================================

        /// <summary>Humanoid Avatar用のボーンマッピング</summary>
        private HumanoidBoneMapping _humanoidMapping = new HumanoidBoneMapping();

        /// <summary>Humanoidボーンマッピング</summary>
        public HumanoidBoneMapping HumanoidMapping => _humanoidMapping;
    }
}
