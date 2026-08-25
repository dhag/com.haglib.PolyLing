// BlendPreviewState.cs
// メッシュブレンド プレビュー状態管理・適用ロジック（多ソース対応）
// UnityEditor非依存。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    /// <summary>
    /// 宛先メッシュ 1 件について、ブレンド前の形状を退避しながら
    /// 複数ソースの混ぜ合わせをプレビューする。
    /// </summary>
    public class BlendPreviewState
    {
        private bool _isActive = false;

        private int         _destIndex     = -1;
        private Vector3[]   _backup        = null;
        private Vector3[][] _normalBackup  = null;

        private readonly Dictionary<int, bool> _savedVisibility = new();

        public bool IsActive  => _isActive;
        public int  DestIndex => _destIndex;

        /// <summary>ブレンド前の頂点位置。確定処理が「適用前の状態」を復元するために読む。</summary>
        public Vector3[] Backup => _backup;

        /// <summary>ブレンド前の法線。</summary>
        public Vector3[][] NormalBackup => _normalBackup;

        /// <summary>
        /// プレビューを開始する。
        /// </summary>
        /// <param name="destIndex">書き込み先 MeshContext の索引。</param>
        /// <param name="hideIndices">
        /// プレビュー中に隠すメッシュの索引（同一モデル内のソース）。
        /// 別モデルのソースは呼び出し側で除外しておくこと。索引空間が違う。
        /// </param>
        public void Start(ModelContext model, int destIndex, IReadOnlyList<int> hideIndices)
        {
            if (_isActive) return;

            var destCtx = model?.GetMeshContext(destIndex);
            if (destCtx?.MeshObject == null) return;

            _savedVisibility.Clear();

            var mo = destCtx.MeshObject;
            _backup = new Vector3[mo.VertexCount];
            for (int i = 0; i < mo.VertexCount; i++) _backup[i] = mo.Vertices[i].Position;

            // 法線もプレビュー中に再計算するため退避する。位置だけ戻して法線を
            // 戻さないと、キャンセルしたのに陰影が変わったままになる。
            _normalBackup = CaptureNormals(mo);

            _savedVisibility[destIndex] = destCtx.IsVisible;
            destCtx.IsVisible = true;

            _destIndex = destIndex;
            _isActive  = true;

            // 隠す処理は UpdateHiddenSources に一本化する。ここで別実装を持つと、
            // プレビュー中のソース差し替えと開始時とで規則がずれる。
            UpdateHiddenSources(model, hideIndices);
        }

        /// <summary>
        /// プレビュー中に隠すソースの集合を差分で取り直す。
        /// ソースの差し替え・追加・削除、隠す/隠さないの切替から呼ぶ。
        ///
        /// 【差分でやる理由】
        /// 退避値（_savedVisibility）は「隠す前の可視状態」であり、隠し直しのたびに
        /// 作り直すと、既に隠してある相手から false を拾って元の状態を失う。
        /// 集合から外れたものだけ戻し、新しく入ったものだけ退避して隠す。
        ///
        /// 書き込み先（_destIndex）は Start が強制表示にしているので対象外。
        /// </summary>
        /// <returns>1 件でも可視状態を変えたら true。</returns>
        public bool UpdateHiddenSources(ModelContext model, IReadOnlyList<int> hideIndices)
        {
            if (!_isActive || model == null) return false;

            var want = new HashSet<int>();
            if (hideIndices != null)
                foreach (int idx in hideIndices)
                    if (idx != _destIndex) want.Add(idx);

            bool changed = false;

            // ── 隠す必要が無くなったものを元へ戻す
            var drop = new List<int>();
            foreach (int idx in _savedVisibility.Keys)
            {
                if (idx == _destIndex) continue;
                if (want.Contains(idx)) continue;
                drop.Add(idx);
            }
            foreach (int idx in drop)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx != null && ctx.IsVisible != _savedVisibility[idx])
                {
                    ctx.IsVisible = _savedVisibility[idx];
                    changed = true;
                }
                _savedVisibility.Remove(idx);
            }

            // ── 新しく隠すものを退避してから隠す
            foreach (int idx in want)
            {
                if (_savedVisibility.ContainsKey(idx)) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx == null) continue;
                _savedVisibility[idx] = ctx.IsVisible;
                if (ctx.IsVisible)
                {
                    ctx.IsVisible = false;
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// バックアップ位置を基準に、複数ソースを混ぜ合わせて宛先へ書き込む。
        /// 返り値はソースごとの対応件数（sources と同じ並び）。
        /// </summary>
        public BlendMatchStats[] Apply(
            ModelContext model,
            IReadOnlyList<BlendSourceEntry> sources,
            bool selectedVertsOnly, BlendMatchMode matchMode, bool recalcNormals,
            Action<MeshContext> syncNormals, ToolContext toolCtx)
        {
            int n = sources?.Count ?? 0;
            var stats = new BlendMatchStats[n];
            if (!_isActive) return stats;

            var destCtx = model?.GetMeshContext(_destIndex);
            if (destCtx?.MeshObject == null) return stats;

            var mo          = destCtx.MeshObject;
            var nonIsolated = BuildNonIsolatedSet(mo);
            var verts       = selectedVertsOnly ? destCtx.SelectedVertices : null;

            BlendVertices(destCtx, _backup, sources, nonIsolated, verts, matchMode, stats);

            if (recalcNormals) mo.RecalculateSmoothNormals();

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(destCtx);
            if (recalcNormals) syncNormals?.Invoke(destCtx);

            BlendOperation.SyncMirrorSide(model, destCtx, toolCtx, recalcNormals, syncNormals);

            toolCtx?.Repaint?.Invoke();
            return stats;
        }

        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (!_isActive) return;

            if (model != null && _backup != null)
            {
                var destCtx = model.GetMeshContext(_destIndex);
                if (destCtx?.MeshObject != null)
                {
                    var mo    = destCtx.MeshObject;
                    int count = Mathf.Min(_backup.Length, mo.VertexCount);
                    for (int i = 0; i < count; i++) mo.Vertices[i].Position = _backup[i];

                    bool normalsRestored = RestoreNormals(mo, _normalBackup);

                    toolCtx?.SyncMeshContextPositionsOnly?.Invoke(destCtx);
                    BlendOperation.SyncMirrorSide(model, destCtx, toolCtx, normalsRestored, null);
                }

                foreach (var (idx, visible) in _savedVisibility)
                {
                    var ctx = model.GetMeshContext(idx);
                    if (ctx != null) ctx.IsVisible = visible;
                }
            }

            _savedVisibility.Clear();
            _backup       = null;
            _normalBackup = null;
            _destIndex    = -1;
            _isActive     = false;
            toolCtx?.Repaint?.Invoke();
        }

        /// <summary>可視状態のみ復元する（確定処理から呼ぶ）。</summary>
        public void RestoreVisibility(ModelContext model, int exceptIndex)
        {
            if (model == null) return;
            foreach (var (idx, visible) in _savedVisibility)
            {
                if (idx == exceptIndex) continue;
                var ctx = model.GetMeshContext(idx);
                if (ctx != null) ctx.IsVisible = visible;
            }
        }

        // ================================================================
        // 静的ヘルパー
        // ================================================================

        /// <summary>
        /// バックアップ位置とソース群を加重平均して書き込む。
        ///
        /// 【合成規則】
        ///   result = base × (1 − Σw) + Σ(w_k × src_k)
        ///   base はブレンド前形状。Σw > 1 のときは w_k を Σw で割って正規化し、
        ///   base の係数を 0 にする。ソース 1 本・Σw ≤ 1 のときは
        ///   Lerp(backup, src, w) と完全に一致する。
        ///
        /// 【対応が取れないソースの扱い】
        ///   その頂点で解決できなかったソースの重みは base へ回す。
        ///   0 として捨てると係数の総和が 1 未満になり、頂点が原点方向へ縮む。
        ///
        /// 【座標系】
        ///   混ぜ合わせはワールド空間で行い、宛先の頂点格納空間へ戻す。
        ///   非スキンドの頂点はローカル、スキンドの頂点はバインド空間にあり、
        ///   生の Position をそのまま混ぜると WorldMatrix の違う相手や
        ///   スキンド↔非スキンドの組でずれる。別モデルのソースも同じ理由で
        ///   ここを通す。行列は既存プロパティを読むだけで CPU 側では作らない。
        /// </summary>
        public static void BlendVertices(
            MeshContext dstCtx, Vector3[] backup,
            IReadOnlyList<BlendSourceEntry> sources,
            HashSet<int> nonIsolated, HashSet<int> selectedVerts,
            BlendMatchMode matchMode,
            BlendMatchStats[] statsPerSource)
        {
            if (dstCtx?.MeshObject == null || backup == null || sources == null) return;

            var mo = dstCtx.MeshObject;
            int n  = sources.Count;

            // ── 重み正規化
            float total = 0f;
            for (int k = 0; k < n; k++)
                if (sources[k].IsUsable) total += sources[k].Weight;

            float scale = (total > 1f) ? 1f / total : 1f;
            float baseW = (total > 1f) ? 0f : 1f - total;

            // ── ソースごとの解決器と変換行列を1回だけ作る
            var resolvers = new BlendVertexResolver[n];
            var mats      = new Matrix4x4[n];
            var identity  = new bool[n];
            var weights   = new float[n];
            var srcMeshes = new MeshObject[n];

            for (int k = 0; k < n; k++)
            {
                var e = sources[k];
                if (!e.IsUsable) continue;
                srcMeshes[k] = e.Context.MeshObject;
                resolvers[k] = new BlendVertexResolver(mo, srcMeshes[k], matchMode);
                mats[k]      = dstCtx.WorldToVertexMatrix * e.Context.VertexToWorldMatrix;
                identity[k]  = mats[k].isIdentity;
                weights[k]   = e.Weight * scale;
            }

            int count = Mathf.Min(mo.VertexCount, backup.Length);
            for (int i = 0; i < count; i++)
            {
                if (nonIsolated != null && !nonIsolated.Contains(i)) continue;
                if (selectedVerts != null && !selectedVerts.Contains(i)) continue;

                Vector3 accum   = backup[i] * baseW;
                float   usedW   = baseW;
                bool    matched = false;

                for (int k = 0; k < n; k++)
                {
                    if (resolvers[k] == null) continue;

                    if (statsPerSource != null && k < statsPerSource.Length)
                        statsPerSource[k].TargetVertexCount++;

                    if (!resolvers[k].TryResolve(i, out int si)) continue;

                    if (statsPerSource != null && k < statsPerSource.Length)
                        statsPerSource[k].MatchedVertexCount++;

                    Vector3 srcPos = srcMeshes[k].Vertices[si].Position;
                    if (!identity[k]) srcPos = mats[k].MultiplyPoint3x4(srcPos);

                    accum += srcPos * weights[k];
                    usedW += weights[k];
                    matched = true;
                }

                if (!matched)
                {
                    // どのソースも対応が取れない頂点は動かさない。
                    mo.Vertices[i].Position = backup[i];
                    continue;
                }

                // 対応が取れなかったソース分の重みは元位置へ回す。
                if (usedW < 1f) accum += backup[i] * (1f - usedW);

                mo.Vertices[i].Position = accum;
            }
        }

        public static HashSet<int> BuildNonIsolatedSet(MeshObject mo)
        {
            var set = new HashSet<int>();
            if (mo == null) return set;
            foreach (var face in mo.Faces)
                foreach (int vi in face.VertexIndices)
                    set.Add(vi);
            return set;
        }

        /// <summary>頂点ごとの法線スロットを配列へ退避する。</summary>
        public static Vector3[][] CaptureNormals(MeshObject mo)
        {
            if (mo == null) return Array.Empty<Vector3[]>();
            var result = new Vector3[mo.VertexCount][];
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var normals = mo.Vertices[i].Normals;
                if (normals == null) { result[i] = null; continue; }
                var a = new Vector3[normals.Count];
                for (int j = 0; j < normals.Count; j++) a[j] = normals[j];
                result[i] = a;
            }
            return result;
        }

        /// <summary>
        /// 退避した法線を書き戻す。スロット数は変えない（UV/法線スロット数は
        /// Face.UVIndices[j] == Face.NormalIndices[j] の不変条件に縛られており、
        /// ここで増減させると面側のインデックスと食い違う）。
        /// 1 件でも書き戻したら true。
        /// </summary>
        public static bool RestoreNormals(MeshObject mo, Vector3[][] backup)
        {
            if (mo == null || backup == null) return false;
            bool wrote = false;
            int count = Mathf.Min(mo.VertexCount, backup.Length);
            for (int i = 0; i < count; i++)
            {
                var saved   = backup[i];
                var normals = mo.Vertices[i].Normals;
                if (saved == null || normals == null) continue;
                int n = Mathf.Min(saved.Length, normals.Count);
                for (int j = 0; j < n; j++) normals[j] = saved[j];
                if (n > 0) wrote = true;
            }
            return wrote;
        }
    }
}
