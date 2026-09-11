// ModelContext.Selection.cs
// ModelContext：選択操作（カテゴリ対応）と、リスト構造変更に伴う索引参照の付け替え。
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
        // 選択操作（カテゴリ対応・旧MeshListUndoContextから統合）
        // ================================================================

        /// <summary>全選択をクリア（後方互換）</summary>
        public void ClearSelection()
        {
            ClearAllCategorySelection();
        }

        /// <summary>単一選択（既存選択をクリアして選択・タイプ自動判定）</summary>
        public void Select(int index)
        {
            ClearAllCategorySelection();
            if (index >= 0 && index < Count)
                AddMeshContextToSelection(index);
        }

        /// <summary>選択を追加（タイプ自動判定）</summary>
        public void AddToSelection(int index)
        {
            if (index >= 0 && index < Count)
                AddMeshContextToSelection(index);
        }

        /// <summary>選択を解除（全カテゴリ）</summary>
        public void RemoveFromSelection(int index)
        {
            RemoveFromSelectionByCategory(index);
        }

        /// <summary>選択をトグル（タイプ自動判定）</summary>
        public void ToggleMeshContextSelection(int index)
        {
            if (IsSelectedInAnyCategory(index))
                RemoveFromSelectionByCategory(index);
            else if (index >= 0 && index < Count)
                AddMeshContextToSelection(index);
        }

        /// <summary>範囲選択（from から to まで・タイプ自動判定）</summary>
        public void SelectRange(int from, int to)
        {
            int min = Mathf.Min(from, to);
            int max = Mathf.Max(from, to);
            for (int i = min; i <= max; i++)
            {
                if (i >= 0 && i < Count)
                    AddMeshContextToSelection(i);
            }
        }

        /// <summary>全選択（タイプ自動判定）</summary>
        public void SelectAll()
        {
            ClearAllCategorySelection();
            for (int i = 0; i < Count; i++)
                AddMeshContextToSelection(i);
        }

        /// <summary>インデックスセットから選択状態を復元（Undo用）</summary>
        public void RestoreSelectionFromIndices(IEnumerable<int> indices)
        {
            ClearAllCategorySelection();
            if (indices == null) return;
            foreach (var index in indices)
            {
                if (index >= 0 && index < Count)
                    AddMeshContextToSelection(index);
            }
        }

        /// <summary>選択されているか（全カテゴリ）</summary>
        public bool IsSelected(int index)
        {
            return IsSelectedInAnyCategory(index);
        }

        /// <summary>選択インデックスを検証して無効なものを除去（全カテゴリ）</summary>
        public void ValidateSelection()
        {
            SelectedDrawableMeshIndices.RemoveAll(i => i < 0 || i >= Count);
            SelectedBoneIndices.RemoveAll(i => i < 0 || i >= Count);
            SelectedMorphIndices.RemoveAll(i => i < 0 || i >= Count);
        }

        /// <summary>全カテゴリの選択インデックスをフラットリストとして取得（Undo記録用）</summary>
        public List<int> CaptureAllSelectedIndices()
        {
            var indices = new List<int>(SelectedDrawableMeshIndices.Count + SelectedBoneIndices.Count + SelectedMorphIndices.Count);
            indices.AddRange(SelectedDrawableMeshIndices);
            indices.AddRange(SelectedBoneIndices);
            indices.AddRange(SelectedMorphIndices);
            return indices;
        }

        // ================================================================
        // リスト構造変更に伴う索引参照の付け替え
        //
        // 【なぜ要るか】
        //   MeshContext は他要素を「MeshContextList の索引」で指す。
        //     ParentIndex / HierarchyParentIndex … 階層
        //     MorphParentIndex                   … モーフの所属
        //     BakedMirrorSourceIndex             … ミラーの実体側
        //   さらに ModelContext 側にも索引参照がある。
        //     HumanoidMapping (name→index)       … Avatar 割当
        //     TPoseBackup      (index→姿勢)      … T ポーズ退避
        //   Insert / RemoveAt / Move はこれらを一切詰め直していなかったため、
        //   ミラーの付け外し（EnableMirror/DisableMirror が Insert/RemoveAt を呼ぶ）
        //   のたびに階層と Avatar 割当が静かに壊れていた。
        //
        // 【表の意味】
        //   map[old] = new。new が -1 なら「その要素は消えた」。
        //   親系フィールドは -1 のとき祖先へ繰り上げる（BuildRemoveMap が解決済み）。
        // ================================================================

        /// <summary>挿入用の付け替え表を作る。newCount は挿入後の件数。</summary>
        private static int[] BuildInsertMap(int oldCount, int insertIndex)
        {
            var map = new int[oldCount];
            for (int i = 0; i < oldCount; i++)
                map[i] = i >= insertIndex ? i + 1 : i;
            return map;
        }

        /// <summary>
        /// 削除用の付け替え表を作る（削除前に呼ぶこと）。
        /// 削除された要素は -1 ではなく「削除要素の親」へ繰り上げる。
        /// 子を根へ吹き飛ばさないため。祖先も削除されていれば更に上へたどる。
        /// </summary>
        private int[] BuildRemoveMap(int removeIndex)
        {
            int oldCount = MeshContextList.Count;
            var map = new int[oldCount];

            for (int i = 0; i < oldCount; i++)
                map[i] = i < removeIndex ? i : (i == removeIndex ? -1 : i - 1);

            // 削除要素の親へ繰り上げる（自己参照・循環はカウンタで打ち切る）。
            int cur = MeshContextList[removeIndex]?.HierarchyParentIndex ?? -1;
            int safety = oldCount + 1;
            while (cur >= 0 && cur < oldCount && cur == removeIndex && safety-- > 0)
                cur = MeshContextList[cur]?.HierarchyParentIndex ?? -1;

            map[removeIndex] = (cur >= 0 && cur < oldCount) ? map[cur] : -1;
            return map;
        }

        /// <summary>移動用の付け替え表を作る。count は移動後（＝移動前）の件数。</summary>
        private static int[] BuildMoveMap(int count, int fromIndex, int toIndex)
        {
            var map = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (i == fromIndex)                       map[i] = toIndex;
                else if (fromIndex < toIndex)             map[i] = (i > fromIndex && i <= toIndex) ? i - 1 : i;
                else                                      map[i] = (i >= toIndex && i < fromIndex) ? i + 1 : i;
            }
            return map;
        }

        /// <summary>
        /// 並べ替え（MeshContextList をまるごと差し替える操作）のあとに索引参照を付け替える。
        ///
        /// oldOrder は差し替え前の並び。実体の参照一致で新しい位置を引くので、
        /// 「どの要素がどこへ動いたか」を呼び出し側が計算しなくてよい。
        /// 新リストに居ない実体は -1 へ落ちる。
        ///
        /// 付け替える中身は Insert / RemoveAt / Move と同じ RemapIndexReferences。
        /// 索引参照フィールドの列挙をここに複製しないこと。
        /// </summary>
        public void RemapIndexReferencesAfterReorder(IReadOnlyList<MeshContext> oldOrder)
        {
            if (oldOrder == null || oldOrder.Count == 0) return;
            if (MeshContextList == null) return;

            var newIndexOf = new Dictionary<MeshContext, int>(MeshContextList.Count);
            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var mc = MeshContextList[i];
                if (mc != null) newIndexOf[mc] = i;
            }

            var map = new int[oldOrder.Count];
            for (int i = 0; i < oldOrder.Count; i++)
            {
                var mc = oldOrder[i];
                map[i] = (mc != null && newIndexOf.TryGetValue(mc, out int ni)) ? ni : -1;
            }

            RemapIndexReferences(map);
        }

        /// <summary>索引で他要素を指している全参照を付け替える。</summary>
        private void RemapIndexReferences(int[] map)
        {
            if (map == null) return;

            int Map(int old)
            {
                if (old < 0 || old >= map.Length) return -1;
                return map[old];
            }

            // 1) メッシュ側の索引参照
            for (int i = 0; i < MeshContextList.Count; i++)
            {
                var mc = MeshContextList[i];
                if (mc == null) continue;

                // ParentIndex は HierarchyParentIndex と同じ入れ物なので 1 回だけ写す。
                // 2 回書くと +1 が 2 度掛かり、親が 1 つ先の要素（多くは自分自身）を指す。
                if (mc.HierarchyParentIndex >= 0) mc.HierarchyParentIndex = Map(mc.HierarchyParentIndex);
                if (mc.MorphParentIndex     >= 0) mc.MorphParentIndex     = Map(mc.MorphParentIndex);

                // MirrorOf モーフの参照先。切れると鏡像モーフが導出できなくなるので付け替える。
                if (mc.MirrorOfMorphIndex   >= 0) mc.MirrorOfMorphIndex   = Map(mc.MirrorOfMorphIndex);

                // 左右対のボーン索引。スキンド変換が確定させた値なので、
                // 索引が動いたら必ず付け替える（切らない）。
                if (mc.MirrorBoneIndex      >= 0) mc.MirrorBoneIndex      = Map(mc.MirrorBoneIndex);

                // ミラー元が消えたらミラーとしての意味を失うので関係ごと切る。
                if (mc.BakedMirrorSourceIndex >= 0)
                    mc.BakedMirrorSourceIndex = Map(mc.BakedMirrorSourceIndex);
            }

            // 2) Humanoid 割当（実行時 working 表現）
            //    per-bone の HumanBodyBone が canonical だが、保存時に
            //    SyncPerBoneFromMapping がこの Dict を per-bone へ書き戻すため、
            //    ここが stale だと canonical まで壊れる。
            if (_humanoidMapping != null && !_humanoidMapping.IsEmpty)
            {
                var remapped = new Dictionary<string, int>();
                foreach (var kv in _humanoidMapping.BoneIndexMap)
                {
                    int ni = Map(kv.Value);
                    if (ni >= 0 && ni < MeshContextList.Count) remapped[kv.Key] = ni;
                }
                _humanoidMapping.FromDictionary(remapped);
            }

            // 3) T ポーズ退避（索引をキーに持つ Dictionary 群）
            TPoseBackup?.RemapIndices(Map);
        }
    }
}
