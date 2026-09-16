// MeshObject.Removal.cs
// MeshObject：頂点・面を消す唯一の入口。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何のためにあるか】
//   頂点・面の削除は各ツール・各 Ops が自前で Vertices.RemoveAt を呼び、
//   面の頂点索引も各所で詰め直していた。索引を詰める処理が散っていると、
//   選択状態やパーツ選択辞書といった「索引で要素を指しているもの」を
//   一緒に付け替える場所が作れない。入口をここ 1 本にする。
//
// 【使い方】
//   RemoveVertices / RemoveFaces は「旧索引 → 新索引」の表を返す（-1 = 消えた）。
//   呼び出し側は自前の RemoveAt と索引シフトを持たないこと。
//
// 【辞書への波及】
//   付け替えの後に VerticesRemoved / FacesRemoved を出す。MeshContext がこれを
//   購読して、選択状態とパーツ選択辞書を同じ表で付け替える
//   （MeshContext.MeshObject の setter で購読する）。
//   MeshObject 自身が持つ法線再計算の除外セットは、ここで付け替える。

using System;
using System.Collections.Generic;
using Poly_Ling.Selection;

namespace Poly_Ling.Data
{
    public partial class MeshObject
    {
        /// <summary>頂点を消した直後に出る。引数は旧索引→新索引の表（-1 = 消えた）。</summary>
        public event Action<int[]> VerticesRemoved;

        /// <summary>面を消した直後に出る。引数は旧索引→新索引の表（-1 = 消えた）。</summary>
        public event Action<int[]> FacesRemoved;

        // ================================================================
        // 法線再計算 除外セットの控え（Undo 用）
        //
        //   除外セットも索引で要素を指しているので、削除のたびに付け替える。
        //   付け替える直前に 1 度だけ複製を控え、Undo 記録がこれを引き取る
        //   （MeshUndoStack）。引き取られなければ次の操作で上書きされる。
        // ================================================================

        private List<PartsSelectionSet> _pendingNormalExcludeBackup;

        /// <summary>除外セットを書き換える直前に、まだ控えが無ければ控える。</summary>
        private void CaptureNormalExcludeBackupIfNeeded()
        {
            if (_pendingNormalExcludeBackup != null) return;
            if (NormalRecalcExcludeList == null || NormalRecalcExcludeList.Count == 0) return;

            _pendingNormalExcludeBackup = CloneNormalExcludeList();
        }

        /// <summary>控えを取り出して手放す。控えが無ければ null。</summary>
        public List<PartsSelectionSet> TakePendingNormalExcludeBackup()
        {
            var b = _pendingNormalExcludeBackup;
            _pendingNormalExcludeBackup = null;
            return b;
        }

        /// <summary>今の除外セットの複製を返す。Undo 記録の「操作後」用。</summary>
        public List<PartsSelectionSet> CloneNormalExcludeList()
        {
            if (NormalRecalcExcludeList == null) return null;
            var list = new List<PartsSelectionSet>(NormalRecalcExcludeList.Count);
            foreach (var s in NormalRecalcExcludeList) list.Add(s?.Clone());
            return list;
        }

        /// <summary>控えを除外セットへ書き戻す。</summary>
        public void RestoreNormalExcludeList(List<PartsSelectionSet> sets)
        {
            if (sets == null) return;
            var list = new List<PartsSelectionSet>(sets.Count);
            foreach (var s in sets) list.Add(s?.Clone());
            NormalRecalcExcludeList = list;
        }

        /// <summary>
        /// 頂点を消して索引を詰める。面・線分の頂点索引も詰め直す。
        ///
        /// 消す頂点を使っている面が残っていた場合は、その頂点をコーナーから外す。
        /// 外した結果 2 頂点未満になった面は面ごと消す（RemoveFaces を続けて呼ぶ）。
        /// 面の始末は本来呼び出し側の仕事で、これは取りこぼしの受け皿。
        /// </summary>
        /// <returns>旧索引→新索引の表。消す対象が無ければ null。</returns>
        public int[] RemoveVertices(IReadOnlyCollection<int> indices)
        {
            if (indices == null || indices.Count == 0) return null;

            int n = Vertices.Count;
            var kill = new HashSet<int>();
            foreach (int i in indices)
                if (i >= 0 && i < n) kill.Add(i);
            if (kill.Count == 0) return null;

            var map = new int[n];
            int w = 0;
            for (int i = 0; i < n; i++) map[i] = kill.Contains(i) ? -1 : w++;

            // 面のコーナーを詰める
            List<int> facesToRemove = null;
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                var vidx = f?.VertexIndices;
                if (vidx == null) continue;

                bool dropped = false;
                for (int k = vidx.Count - 1; k >= 0; k--)
                {
                    int ov = vidx[k];
                    int nv = (ov >= 0 && ov < n) ? map[ov] : -1;
                    if (nv < 0)
                    {
                        vidx.RemoveAt(k);
                        if (f.UVIndices     != null && k < f.UVIndices.Count)     f.UVIndices.RemoveAt(k);
                        if (f.NormalIndices != null && k < f.NormalIndices.Count) f.NormalIndices.RemoveAt(k);
                        dropped = true;
                    }
                    else if (nv != ov)
                    {
                        vidx[k] = nv;
                    }
                }
                if (dropped && vidx.Count < 2)
                    (facesToRemove ??= new List<int>()).Add(fi);
            }

            // 頂点を消す（降順）
            for (int i = n - 1; i >= 0; i--)
                if (map[i] < 0) Vertices.RemoveAt(i);

            InvalidatePositionCache();
            RebuildIdSets();

            // 自分が持つ除外セット
            if (NormalRecalcExcludeList != null)
            {
                CaptureNormalExcludeBackupIfNeeded();
                foreach (var set in NormalRecalcExcludeList)
                    PartsIndexRemap.ApplyVertexMap(set, map);
            }

            VerticesRemoved?.Invoke(map);

            // コーナーが減って成立しなくなった面を始末する
            if (facesToRemove != null) RemoveFaces(facesToRemove);

            return map;
        }

        /// <summary>
        /// 面は呼び出し側が新しい索引へ書き換えてある前提で、頂点だけを消す。
        ///
        /// 「複数の旧索引が 1 つの新索引へ落ちる」表（頂点マージ）を扱うための入口。
        /// 面の書き換えまでこちらで行うと二重にシフトしてしまうので、面は触らない。
        /// 表は呼び出し側が作ったものをそのまま通知へ回す（辞書側はこの表で追随する）。
        /// </summary>
        /// <param name="indices">消す頂点の旧索引。</param>
        /// <param name="map">旧索引→新索引の表（-1 = 消えた）。</param>
        public void RemoveVerticesWithMap(IReadOnlyCollection<int> indices, int[] map)
        {
            if (indices == null || indices.Count == 0 || map == null) return;

            var kill = new List<int>();
            foreach (int i in indices)
                if (i >= 0 && i < Vertices.Count) kill.Add(i);
            if (kill.Count == 0) return;

            kill.Sort();
            for (int k = kill.Count - 1; k >= 0; k--)
                Vertices.RemoveAt(kill[k]);

            InvalidatePositionCache();
            RebuildIdSets();

            if (NormalRecalcExcludeList != null)
                foreach (var set in NormalRecalcExcludeList)
                    PartsIndexRemap.ApplyVertexMap(set, map);

            VerticesRemoved?.Invoke(map);
        }

        /// <summary>
        /// 面（2 頂点の線分を含む）を消して索引を詰める。
        /// </summary>
        /// <returns>旧索引→新索引の表。消す対象が無ければ null。</returns>
        public int[] RemoveFaces(IReadOnlyCollection<int> faceIndices)
        {
            if (faceIndices == null || faceIndices.Count == 0) return null;

            int n = Faces.Count;
            var kill = new HashSet<int>();
            foreach (int i in faceIndices)
                if (i >= 0 && i < n) kill.Add(i);
            if (kill.Count == 0) return null;

            var map = new int[n];
            int w = 0;
            for (int i = 0; i < n; i++) map[i] = kill.Contains(i) ? -1 : w++;

            for (int i = n - 1; i >= 0; i--)
                if (map[i] < 0) Faces.RemoveAt(i);

            RebuildIdSets();

            if (NormalRecalcExcludeList != null)
            {
                CaptureNormalExcludeBackupIfNeeded();
                foreach (var set in NormalRecalcExcludeList)
                    PartsIndexRemap.ApplyFaceMap(set, map);
            }

            FacesRemoved?.Invoke(map);

            return map;
        }
    }
}
