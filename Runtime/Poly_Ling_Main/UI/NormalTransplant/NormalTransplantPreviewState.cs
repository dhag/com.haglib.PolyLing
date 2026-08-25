// NormalTransplantPreviewState.cs
// 法線移植 プレビュー状態管理
// UnityEditor非依存
//
// ShrinkPreviewState と同じ役割だが、退避・書き戻しの対象は頂点位置ではなく
// スロット法線（Vertex.Normals[slot]）である。
//
// 【不変条件】
//   本クラスはスロットを増減しない。既存スロットの値だけを書き換える。
//   よって Vertex.UVs.Count == Vertex.Normals.Count と
//   Face.UVIndices[j] == Face.NormalIndices[j] は保たれる。
//
// 【スロットの扱い】
//   移植法線は「ターゲット頂点の位置」から求まるため、同一頂点の全スロットが
//   同じ値を受け取る。適用率 1.0 ではターゲット側のハードエッジ（スロット分離）は
//   同一値に潰れる。適用率が 1.0 未満なら元のスロット値との Slerp なので分離は残る。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;

namespace Poly_Ling.UI
{
    public class NormalTransplantPreviewState
    {
        // ================================================================
        // サンプル（移植元の計算結果）
        // ================================================================

        /// <summary>
        /// ターゲット1個分の移植結果。
        /// LocalNormals はターゲットのローカル空間における頂点ごとの移植法線。
        /// </summary>
        public class TargetSample
        {
            public int MasterIndex;
            public int VertexCount;

            /// <summary>頂点ごとの移植法線（ローカル空間・正規化済み）</summary>
            public Vector3[] LocalNormals;

            /// <summary>移植法線が求まったか</summary>
            public bool[] Resolved;

            /// <summary>プリズムに内包されていた頂点数</summary>
            public int InsideCount;

            /// <summary>移植法線が求まった頂点数（最近傍フォールバックを含む）</summary>
            public int ResolvedCount;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private bool _isActive;
        private List<TargetSample> _samples;

        /// <summary>[ターゲット][頂点][スロット] の元法線</summary>
        private List<Vector3[][]> _backups;

        public bool IsActive => _isActive;

        public IReadOnlyList<TargetSample> Samples => _samples;

        // ================================================================
        // 開始
        // ================================================================

        /// <summary>
        /// プレビューを開始する。ターゲットの全スロット法線を退避する。
        /// 頂点座標も可視状態も変更しない。
        /// </summary>
        public bool Start(ModelContext model, IList<TargetSample> samples)
        {
            if (_isActive) return false;
            if (model == null || samples == null || samples.Count == 0) return false;

            _samples = new List<TargetSample>();
            _backups = new List<Vector3[][]>();

            foreach (var s in samples)
            {
                if (s == null) continue;

                var ctx = model.GetMeshContext(s.MasterIndex);
                var mo = ctx?.MeshObject;
                if (mo == null) continue;

                int vc = mo.VertexCount;
                var backup = new Vector3[vc][];
                for (int vi = 0; vi < vc; vi++)
                {
                    var vertex = mo.Vertices[vi];
                    if (vertex == null || vertex.Normals.Count == 0)
                    {
                        backup[vi] = null;
                        continue;
                    }

                    var slots = new Vector3[vertex.Normals.Count];
                    for (int si = 0; si < slots.Length; si++) slots[si] = vertex.Normals[si];
                    backup[vi] = slots;
                }

                _samples.Add(s);
                _backups.Add(backup);
            }

            if (_samples.Count == 0)
            {
                _samples = null;
                _backups = null;
                return false;
            }

            _isActive = true;
            return true;
        }

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>
        /// 適用率 [0,1] を反映する。常に退避値から作り直すため何度呼んでも同じ結果になる。
        /// 表示同期は行わない（呼び出し側の責務）。
        /// </summary>
        public void Apply(ModelContext model, float strength)
        {
            if (!_isActive || model == null) return;

            float s = Mathf.Clamp01(strength);

            for (int ti = 0; ti < _samples.Count; ti++)
            {
                var sample = _samples[ti];
                var backup = _backups[ti];

                var ctx = model.GetMeshContext(sample.MasterIndex);
                var mo = ctx?.MeshObject;
                if (mo == null) continue;

                int vc = Mathf.Min(backup.Length, mo.VertexCount);
                for (int vi = 0; vi < vc; vi++)
                {
                    var slots = backup[vi];
                    if (slots == null) continue;

                    var vertex = mo.Vertices[vi];
                    if (vertex == null) continue;

                    int count = Mathf.Min(slots.Length, vertex.Normals.Count);

                    bool resolved = vi < sample.Resolved.Length && sample.Resolved[vi];
                    if (!resolved || s <= 0f)
                    {
                        for (int si = 0; si < count; si++) vertex.Normals[si] = slots[si];
                        continue;
                    }

                    Vector3 target = sample.LocalNormals[vi];

                    for (int si = 0; si < count; si++)
                    {
                        Vector3 blended = (s >= 1f)
                            ? target
                            : Vector3.Slerp(slots[si], target, s);

                        vertex.Normals[si] = blended.sqrMagnitude < 1e-12f
                            ? slots[si]
                            : blended.normalized;
                    }
                }
            }
        }

        // ================================================================
        // 復元
        // ================================================================

        /// <summary>退避した法線を書き戻す。プレビューは継続する。表示同期は行わない。</summary>
        public void Restore(ModelContext model)
        {
            if (!_isActive || model == null) return;

            for (int ti = 0; ti < _samples.Count; ti++)
            {
                var sample = _samples[ti];
                var backup = _backups[ti];

                var ctx = model.GetMeshContext(sample.MasterIndex);
                var mo = ctx?.MeshObject;
                if (mo == null) continue;

                int vc = Mathf.Min(backup.Length, mo.VertexCount);
                for (int vi = 0; vi < vc; vi++)
                {
                    var slots = backup[vi];
                    if (slots == null) continue;

                    var vertex = mo.Vertices[vi];
                    if (vertex == null) continue;

                    int count = Mathf.Min(slots.Length, vertex.Normals.Count);
                    for (int si = 0; si < count; si++) vertex.Normals[si] = slots[si];
                }
            }
        }

        // ================================================================
        // 終了
        // ================================================================

        /// <summary>退避した法線を書き戻して状態を捨てる。表示同期は行わない。</summary>
        public void End(ModelContext model)
        {
            if (!_isActive) return;

            Restore(model);

            _samples = null;
            _backups = null;
            _isActive = false;
        }

        // ================================================================
        // 統計（UI表示用）
        // ================================================================

        public int TotalVertexCount()
        {
            if (_samples == null) return 0;
            int n = 0;
            foreach (var s in _samples) n += s.VertexCount;
            return n;
        }

        public int TotalResolvedCount()
        {
            if (_samples == null) return 0;
            int n = 0;
            foreach (var s in _samples) n += s.ResolvedCount;
            return n;
        }

        public int TotalInsideCount()
        {
            if (_samples == null) return 0;
            int n = 0;
            foreach (var s in _samples) n += s.InsideCount;
            return n;
        }
    }
}
