// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyBuildResult.cs
// ============================================================
// ヒエラルキー生成の結果
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// 【ログを出さない理由】
//   警告をここで Debug.LogWarning すると、ログ方針（接頭辞・重要度・出す／出さない）が
//   Runtime に固定される。生成側は溜めるだけにして、コンソールへ出すか
//   ダイアログに出すかは呼び出し側（Editor 拡張／Player UI）が決める。
//
// 【索引→Transform 表の規約】
//   索引は常に「割当対象ノード（＝実体側）の MeshContext 索引」。
//     RealTransformByIndex[i]   … 索引 i のノードの実体側 GameObject
//     MirrorTransformByIndex[i] … 索引 i のノードのミラー側 GameObject
//   ミラー側 GameObject の作られ方は 2 通りあり由来ノードの索引が一致するとは
//   限らないが、Build の末尾で両方をこの規約へ寄せてある。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>ヒエラルキー生成1回ぶんの結果。</summary>
    public class HierarchyBuildResult
    {
        /// <summary>生成したルート GameObject。失敗時は null。</summary>
        public GameObject Root;

        /// <summary>ボーンの索引 → Transform。</summary>
        public readonly Dictionary<int, Transform> BoneTransformByIndex = new Dictionary<int, Transform>();

        /// <summary>実体側ノードの索引 → Transform（ボーンとメッシュの両方）。</summary>
        public readonly Dictionary<int, Transform> RealTransformByIndex = new Dictionary<int, Transform>();

        /// <summary>ミラー側ノードの索引 → Transform。索引は実体側に寄せてある。</summary>
        public readonly Dictionary<int, Transform> MirrorTransformByIndex = new Dictionary<int, Transform>();

        /// <summary>可視ノードの親として補完した不可視ノードの索引。</summary>
        public readonly HashSet<int> SupplementedIndices = new HashSet<int>();

        /// <summary>
        /// 載せたブレンドシェイプの一覧（ExportMorphTargets が true のときのみ）。
        /// 同じモーフが実体側とミラー枝の両方に載ることがあるため、
        /// モーフ索引ごとに複数入りうる。VRM の表情バインドがこれを引く。
        /// </summary>
        public readonly List<MorphShapeSlot> MorphShapes = new List<MorphShapeSlot>();

        /// <summary>出力した GameObject 数（メッシュ・関節）。</summary>
        public int ExportedNodeCount;

        /// <summary>不可視のまま出力対象から外したノード数。</summary>
        public int SkippedInvisibleCount;

        /// <summary>可視ノードの親として補完した不可視ノード数。</summary>
        public int SupplementedAncestorCount;

        /// <summary>出力したボーン数。</summary>
        public int BoneCount;

        /// <summary>載せたブレンドシェイプの総数。</summary>
        public int MorphShapeCount => MorphShapes.Count;

        /// <summary>警告（呼び出し側がログ・ダイアログへ流す）。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>補足（警告ではない）。</summary>
        public readonly List<string> Notes = new List<string>();

        public void Warn(string message) => Warnings.Add(message);
        public void Note(string message) => Notes.Add(message);
    }
}
