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
        //     CSV / JSON / バイナリのどれにも書き出さないこと（規約4 の対称性の対象外）。
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

        // ================================================================
        // 当たり判定（SpringBone collider）の表示（表示専用。保存しない）
        //   当たり判定パネルを開いている間だけ、全当たり判定を線で描く。
        //   読むのは MeshSceneRenderer.PrepareSpringBoneColliders /
        //   SubmitSpringBoneColliders。変えたら MarkAllSlotsDirty を促すこと。
        // ================================================================

        /// <summary>当たり判定を 3D 画面へ描くか。</summary>
        public bool SpringBoneColliderDisplay { get; set; } = false;

        /// <summary>強調する当たり判定の付帯先ボーン（masterIndex）。-1＝なし。</summary>
        public int SpringBoneColliderHighlightMaster { get; set; } = -1;

        /// <summary>強調する当たり判定の、そのボーン内での番号。-1＝なし。</summary>
        public int SpringBoneColliderHighlightSlot { get; set; } = -1;

        /// <summary>当たり判定の表示を消す。</summary>
        public void ClearSpringBoneColliderDisplay()
        {
            SpringBoneColliderDisplay = false;
            SpringBoneColliderHighlightMaster = -1;
            SpringBoneColliderHighlightSlot = -1;
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
        /// 全ミラーペアの位置を同期する（Real→Mirror。形の控えも MirrorPair.SyncPositions が作り直す）
        /// </summary>
        public void SyncAllMirrorPositions()
        {
            if (MirrorPairs == null) return;
            for (int i = 0; i < MirrorPairs.Count; i++)
            {
                MirrorPairs[i].SyncPositions();
            }
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
