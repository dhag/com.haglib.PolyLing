// Runtime/Poly_Ling_Main/Tools/Deformers/DeformApplier.cs
// デフォーマ適用パイプライン。複数メッシュ横断で選択頂点に IMeshDeformer を適用する。
//
// 【座標往復】RotateTool.UpdatePreview (RotateTool.cs:341,344) と同じ経路で
//   メッシュローカル ⇔ ワールドを往復し、さらに作業軸ローカルへ入れる。
//
//     メッシュローカル p
//       → meshContext.LocalToWorld(p)
//       → workAxis.WorldToLocal(...)      ← ここから先が作業軸ローカル
//       → deformer.Evaluate(...)
//       → workAxis.LocalToWorld(...)
//       → meshContext.WorldToLocal(...)
//
// 【絶対計算】Begin で記録した開始位置から毎回計算し直す。フレーム差分を
//   積み上げないため、パラメータを往復させても誤差が溜まらない。
//   RotateTool.UpdatePreview と同じ方針。
//
// 【Undo】BuildUndoEntries が MeshMoveEntry[] を返すだけにしてある。
//   MultiMeshVertexMoveRecord の生成と Record 呼び出しは呼び出し側が行う
//   （RotateTool.ApplyRotation の RotateTool.cs:389-416 に相当）。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools.Deformers
{
    /// <summary>デフォーマを選択頂点へ適用する。</summary>
    public class DeformApplier
    {
        // ================================================================
        // 状態
        // ================================================================

        private ModelContext    _model;
        private WorkAxisContext _axis;

        // meshContextIndex -> (vertexIndex -> 開始位置。メッシュローカル座標)
        private readonly Dictionary<int, Dictionary<int, Vector3>> _startPositions
            = new Dictionary<int, Dictionary<int, Vector3>>();

        // meshContextIndex -> (vertexIndex -> 重み 0..1)。選択頂点は 1、
        // マグネット影響頂点のみ 1 未満。マグネット未使用なら空。
        private readonly Dictionary<int, Dictionary<int, float>> _weights
            = new Dictionary<int, Dictionary<int, float>>();

        // Prepare へ渡す作業軸ローカルでの s 範囲
        private DeformContext _context;

        /// <summary>Begin 済みか。</summary>
        public bool IsActive { get; private set; }

        /// <summary>対象頂点の総数。</summary>
        public int AffectedCount { get; private set; }

        /// <summary>Prepare に渡される事前計算コンテキスト（UI 表示用）。</summary>
        public DeformContext Context => _context;

        // ================================================================
        // 開始
        // ================================================================

        /// <summary>
        /// 対象頂点を集めて開始位置を記録する。
        /// magnetRadius に 0 以下を渡すとマグネットは無効。
        /// </summary>
        public bool Begin(
            ModelContext model,
            WorkAxisContext axis,
            float magnetRadius = 0f,
            FalloffType falloff = FalloffType.Smooth,
            DistanceMode distanceMode = DistanceMode.Euclidean)
        {
            Reset();

            if (model == null || axis == null) return false;

            _model = model;
            _axis  = axis;

            bool useMagnet = magnetRadius > 0f;

            foreach (int meshIdx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(meshIdx);
                var mo = mc?.MeshObject;
                if (mo == null || !mc.HasSelection) continue;

                var selected = CollectSelectedVertices(mc, mo);
                if (selected.Count == 0) continue;

                var start = new Dictionary<int, Vector3>();
                foreach (int i in selected)
                {
                    if (i >= 0 && i < mo.VertexCount)
                        start[i] = mo.Vertices[i].Position;
                }

                if (useMagnet)
                {
                    // MagnetInfluence は全頂点の元位置配列を要求する。
                    var orig = new Vector3[mo.VertexCount];
                    for (int i = 0; i < mo.VertexCount; i++)
                        orig[i] = mo.Vertices[i].Position;

                    var influenced = MagnetInfluence.Compute(
                        mo, selected, orig, magnetRadius, falloff, distanceMode);

                    if (influenced != null && influenced.Count > 0)
                    {
                        var wmap = new Dictionary<int, float>();
                        foreach (var kv in influenced)
                        {
                            if (kv.Key < 0 || kv.Key >= mo.VertexCount) continue;
                            if (!start.ContainsKey(kv.Key)) start[kv.Key] = orig[kv.Key];
                            wmap[kv.Key] = kv.Value;
                        }
                        if (wmap.Count > 0) _weights[meshIdx] = wmap;
                    }
                }

                if (start.Count > 0)
                {
                    _startPositions[meshIdx] = start;
                    AffectedCount += start.Count;
                }
            }

            if (AffectedCount == 0)
            {
                Reset();
                return false;
            }

            _context = BuildContext();
            IsActive = true;
            return true;
        }

        // ================================================================
        // 適用
        // ================================================================

        /// <summary>
        /// デフォーマを適用する。常に開始位置を基準にした絶対計算。
        /// 何度呼んでも結果は同じで、誤差は蓄積しない。
        /// </summary>
        public bool Apply(IMeshDeformer deformer)
        {
            if (!IsActive || deformer == null || _model == null || _axis == null)
                return false;

            deformer.Prepare(_context);

            // アフィンで表せるデフォーマは、部分適用時に回転成分を Slerp できる。
            // 曲げのような非アフィンは位置 Lerp になり、円弧が弦へ寄る近似になる。
            bool isAffine = deformer.TryGetAffine(out Matrix4x4 affine);
            Quaternion affineRot = isAffine ? affine.rotation : Quaternion.identity;

            foreach (var meshKv in _startPositions)
            {
                var mc = _model.GetMeshContext(meshKv.Key);
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                _weights.TryGetValue(meshKv.Key, out var wmap);

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i < 0 || i >= mo.VertexCount) continue;

                    // メッシュローカル → ワールド → 作業軸ローカル
                    Vector3 world = mc.LocalToWorld(posKv.Value);
                    Vector3 local = _axis.WorldToLocal(world);

                    float w = 1f;
                    if (wmap != null && wmap.TryGetValue(i, out float wt)) w = wt;

                    Vector3 deformed;
                    if (w >= 0.9999f)
                    {
                        deformed = deformer.Evaluate(local);
                    }
                    else if (isAffine)
                    {
                        // 回転を Slerp してから適用する。位置を Lerp すると
                        // 円弧ではなく弦を通り、回転量が縮む。
                        Quaternion rq = Quaternion.Slerp(Quaternion.identity, affineRot, w);
                        deformed = rq * local;
                    }
                    else
                    {
                        // 非アフィンは位置の線形補間で近似する。
                        deformed = Vector3.Lerp(local, deformer.Evaluate(local), w);
                    }

                    // 作業軸ローカル → ワールド → メッシュローカル
                    Vector3 outWorld = _axis.LocalToWorld(deformed);

                    var v = mo.Vertices[i];
                    v.Position = mc.WorldToLocal(outWorld);
                    mo.Vertices[i] = v;
                }

                mo.InvalidatePositionCache();
            }

            return true;
        }

        // ================================================================
        // 巻き戻し
        // ================================================================

        /// <summary>開始位置へ戻す。Begin 状態は維持する。</summary>
        public void Revert()
        {
            if (!IsActive || _model == null) return;

            foreach (var meshKv in _startPositions)
            {
                var mo = _model.GetMeshContext(meshKv.Key)?.MeshObject;
                if (mo == null) continue;

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i < 0 || i >= mo.VertexCount) continue;
                    var v = mo.Vertices[i];
                    v.Position = posKv.Value;
                    mo.Vertices[i] = v;
                }

                mo.InvalidatePositionCache();
            }
        }

        // ================================================================
        // Undo 用エントリ
        // ================================================================

        /// <summary>
        /// 開始位置から動いた頂点の差分を MeshMoveEntry[] にして返す。
        /// MultiMeshVertexMoveRecord の生成と UndoStack への Record は
        /// 呼び出し側が行う（RotateTool.cs:408-417 と同じ責務分割）。
        /// </summary>
        public MeshMoveEntry[] BuildUndoEntries(float threshold = 0.0001f)
        {
            var entries = new List<MeshMoveEntry>();
            if (!IsActive || _model == null) return entries.ToArray();

            foreach (var meshKv in _startPositions)
            {
                var mo = _model.GetMeshContext(meshKv.Key)?.MeshObject;
                if (mo == null) continue;

                var indices = new List<int>();
                var oldPos  = new List<Vector3>();
                var newPos  = new List<Vector3>();

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i < 0 || i >= mo.VertexCount) continue;

                    Vector3 cur = mo.Vertices[i].Position;
                    if (Vector3.Distance(posKv.Value, cur) > threshold)
                    {
                        indices.Add(i);
                        oldPos.Add(posKv.Value);
                        newPos.Add(cur);
                    }
                }

                if (indices.Count > 0)
                {
                    entries.Add(new MeshMoveEntry
                    {
                        MeshContextIndex = meshKv.Key,
                        Indices          = indices.ToArray(),
                        OldPositions     = oldPos.ToArray(),
                        NewPositions     = newPos.ToArray()
                    });
                }
            }

            return entries.ToArray();
        }

        /// <summary>
        /// OriginalPositions を現在位置へ追従させる。確定時に呼ぶ。
        /// RotateTool.ApplyRotation (RotateTool.cs:397-404) と同じ処理。
        /// </summary>
        public void SyncOriginalPositions()
        {
            if (!IsActive || _model == null) return;

            foreach (var meshKv in _startPositions)
            {
                var mc = _model.GetMeshContext(meshKv.Key);
                var mo = mc?.MeshObject;
                if (mo == null || mc.OriginalPositions == null) continue;

                foreach (var posKv in meshKv.Value)
                {
                    int i = posKv.Key;
                    if (i >= 0 && i < mo.VertexCount && i < mc.OriginalPositions.Length)
                        mc.OriginalPositions[i] = mo.Vertices[i].Position;
                }
            }
        }

        // ================================================================
        // 終了
        // ================================================================

        public void Reset()
        {
            _model = null;
            _axis  = null;
            _startPositions.Clear();
            _weights.Clear();
            _context = default;
            AffectedCount = 0;
            IsActive = false;
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// 開始位置を作業軸ローカルへ写して s（= y）の範囲と AABB を求める。
        /// 曲げはこの範囲から曲率 k を決めるため、Begin 時に一度だけ計算する。
        /// 格子変形の「選択フィット」は LocalMin / LocalMax を使う。
        /// </summary>
        private DeformContext BuildContext()
        {
            var ctx = new DeformContext
            {
                SMin = float.MaxValue,
                SMax = float.MinValue,
                LocalMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                LocalMax = new Vector3(float.MinValue, float.MinValue, float.MinValue),
                VertexCount = 0
            };

            foreach (var meshKv in _startPositions)
            {
                var mc = _model.GetMeshContext(meshKv.Key);
                if (mc == null) continue;

                foreach (var posKv in meshKv.Value)
                {
                    Vector3 local = _axis.WorldToLocal(mc.LocalToWorld(posKv.Value));

                    if (local.x < ctx.LocalMin.x) ctx.LocalMin.x = local.x;
                    if (local.y < ctx.LocalMin.y) ctx.LocalMin.y = local.y;
                    if (local.z < ctx.LocalMin.z) ctx.LocalMin.z = local.z;

                    if (local.x > ctx.LocalMax.x) ctx.LocalMax.x = local.x;
                    if (local.y > ctx.LocalMax.y) ctx.LocalMax.y = local.y;
                    if (local.z > ctx.LocalMax.z) ctx.LocalMax.z = local.z;

                    ctx.VertexCount++;
                }
            }

            if (ctx.VertexCount == 0)
            {
                ctx.LocalMin = Vector3.zero;
                ctx.LocalMax = Vector3.zero;
            }

            // s は AABB の y 成分そのもの。二重に持たずここで写す。
            ctx.SMin = ctx.LocalMin.y;
            ctx.SMax = ctx.LocalMax.y;

            return ctx;
        }

        /// <summary>
        /// 選択（頂点 / 辺 / 面 / 線分）から影響頂点を集める。
        /// RotateTool.UpdateAffected (RotateTool.cs:155-182) と同じ規則。
        /// </summary>
        private static HashSet<int> CollectSelectedVertices(MeshContext mc, MeshObject mo)
        {
            var set = new HashSet<int>();
            if (mc == null || mo == null) return set;

            if (mc.SelectedVertices != null)
                foreach (int v in mc.SelectedVertices) set.Add(v);

            if (mc.SelectedEdges != null)
                foreach (var e in mc.SelectedEdges) { set.Add(e.V1); set.Add(e.V2); }

            if (mc.SelectedFaces != null)
                foreach (int fi in mc.SelectedFaces)
                    if (fi >= 0 && fi < mo.FaceCount)
                        foreach (int v in mo.Faces[fi].VertexIndices) set.Add(v);

            if (mc.SelectedLines != null)
                foreach (int li in mc.SelectedLines)
                    if (li >= 0 && li < mo.FaceCount)
                    {
                        var f = mo.Faces[li];
                        if (f.VertexCount == 2)
                        {
                            set.Add(f.VertexIndices[0]);
                            set.Add(f.VertexIndices[1]);
                        }
                    }

            return set;
        }
    }
}
