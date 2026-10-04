// NormalEditOps.Pipeline.cs
// 法線編集の実行手順。コマンド（PlayerCommandDispatcher）とパネルのプレビュー
// （NormalEditPreviewState）が同じ Execute を通るので、プレビューと確定の結果は一致する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. 対象コーナーを集める（CollectTargetCorners：面 → 頂点・辺 → 全体）
//   2. 「手動の法線編集からも守る」除外セットのコーナーを外す
//   3. ProtectUnselected なら、対象外と共有しているスロットを対象側だけ分ける
//   4. 操作を実行する
//   5. Blend < 1 なら、操作の前の値と結果を球面補間して書き直す
//
// 【座標空間】
//   Target / Direction は既定でオブジェクトのローカル座標。WorldSpace のときは
//   ObjectToWorld（非スキンド＝WorldMatrix、スキンド＝単位。PolyLing_姿勢の規約.md 3 章）
//   でローカルへ直す。法線の変換は行列の逆転置（N）を使い、戻しは Mᵀ を使う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static partial class NormalEditOps
    {
        // ================================================================
        // 座標空間
        // ================================================================

        /// <summary>
        /// 頂点座標の空間からワールドへの行列。非スキンドは WorldMatrix、
        /// スキンドは頂点がバインド（ワールド）空間にあるので単位。
        /// </summary>
        public static Matrix4x4 ObjectToWorld(MeshContext mc)
        {
            if (mc == null || mc.SkinKind == SkinKind.Skinned) return Matrix4x4.identity;
            return mc.WorldMatrix;
        }

        /// <summary>ローカル法線 → ワールド法線（逆転置で変換して正規化）。</summary>
        private static Vector3 NormalToWorld(Matrix4x4 m, Vector3 n)
        {
            Vector3 w = m.inverse.transpose.MultiplyVector(n);
            return w.sqrMagnitude < 1e-12f ? n : w.normalized;
        }

        /// <summary>ワールド法線 → ローカル法線（Mᵀ で戻して正規化）。</summary>
        private static Vector3 NormalToLocal(Matrix4x4 m, Vector3 w)
        {
            Vector3 n = m.transpose.MultiplyVector(w);
            return n.sqrMagnitude < 1e-12f ? w : n.normalized;
        }

        // ================================================================
        // 手動編集からの保護
        // ================================================================

        /// <summary>
        /// 「手動の法線編集からも守る」除外セットが指すコーナー（面, コーナー番号）。
        /// 判定は自動再計算の除外と同じ：面が入っているか、頂点が入っているか（辺は両端頂点）。
        /// </summary>
        public static HashSet<(int Face, int Corner)> CollectManualProtectedCorners(MeshObject mesh)
        {
            var result = new HashSet<(int, int)>();
            var list = mesh?.NormalRecalcExcludeList;
            if (list == null || list.Count == 0) return result;

            var verts = new HashSet<int>();
            var faces = new HashSet<int>();
            foreach (var set in list)
            {
                if (set == null || !set.ProtectManualNormalEdit) continue;
                foreach (int v in set.Vertices) verts.Add(v);
                foreach (var e in set.Edges) { verts.Add(e.V1); verts.Add(e.V2); }
                foreach (int f in set.Faces) faces.Add(f);
            }
            if (verts.Count == 0 && faces.Count == 0) return result;

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                bool faceIn = faces.Contains(fi);
                for (int j = 0; j < face.VertexCount; j++)
                {
                    if (faceIn || verts.Contains(face.VertexIndices[j]))
                        result.Add((fi, j));
                }
            }
            return result;
        }

        /// <summary>
        /// 選択から編集対象のコーナーを集め、手動編集から守るコーナーを外す。
        /// </summary>
        /// <param name="protectedCount">守るために外したコーナー数。</param>
        public static List<FaceCorner> CollectEditCorners(MeshContext mc, out int protectedCount)
        {
            protectedCount = 0;
            var mo = mc?.MeshObject;
            if (mo == null) return new List<FaceCorner>();

            var sel = mc.Selection;
            var corners = CollectTargetCorners(mo, sel?.Faces, sel?.Vertices, sel?.Edges);

            var guard = CollectManualProtectedCorners(mo);
            if (guard.Count == 0) return corners;

            var kept = new List<FaceCorner>(corners.Count);
            foreach (var fc in corners)
            {
                if (guard.Contains((fc.Face, fc.Corner))) protectedCount++;
                else kept.Add(fc);
            }
            return kept;
        }

        // ================================================================
        // 選択外の保護（共有スロットの分離）
        // ================================================================

        /// <summary>
        /// 対象コーナーが使うスロットのうち、対象外のコーナーも使っているものを
        /// 対象側だけ新しいスロットへ分ける。値（UV・法線）は元のスロットと同じで始まる。
        /// 同じ（頂点, スロット）を使う対象コーナーは同じ新スロットへまとめる。
        /// </summary>
        /// <returns>新しく作ったスロット数。</returns>
        public static int IsolateTargetSlots(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null || corners.Count == 0) return 0;

            var isTarget = new HashSet<(int, int)>();
            foreach (var fc in corners) isTarget.Add((fc.Face, fc.Corner));

            // 対象外のコーナーが使っている（頂点, スロット）
            var usedByOthers = new HashSet<(int, int)>();
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                for (int j = 0; j < face.VertexCount; j++)
                {
                    if (isTarget.Contains((fi, j))) continue;
                    int slot = SlotOf(mesh, new FaceCorner(fi, j), out int vi);
                    if (slot >= 0) usedByOthers.Add((vi, slot));
                }
            }
            if (usedByOthers.Count == 0) return 0;

            var remap = new Dictionary<(int, int), int>();
            int created = 0;
            foreach (var fc in corners)
            {
                int slot = SlotOf(mesh, fc, out int vi);
                if (slot < 0 || !usedByOthers.Contains((vi, slot))) continue;

                if (!remap.TryGetValue((vi, slot), out int ns))
                {
                    var v = mesh.Vertices[vi];
                    ns = v.AddUVNormalSlot(v.UVs[slot], v.Normals[slot]);
                    remap[(vi, slot)] = ns;
                    created++;
                }

                var face = mesh.Faces[fc.Face];
                face.UVIndices[fc.Corner] = ns;
                while (face.NormalIndices.Count <= fc.Corner) face.NormalIndices.Add(0);
                face.NormalIndices[fc.Corner] = ns;
            }

            if (created > 0)
            {
                NormalSmoothingOps.NormalizeSlotCounts(mesh);
                NormalSmoothingOps.ValidateSlotInvariant(mesh, mesh.Name);
            }
            return created;
        }

        // ================================================================
        // 追加の操作
        // ================================================================

        /// <summary>
        /// 方向を保持して分離。対象コーナーのスロットを別の面と共有していれば、
        /// 今の値（UV・法線）のまま面ごとに別スロットへ分ける。方向は変えない。
        /// 同じスロットを使う面が全部対象なら、先頭の面は元のスロットに残す。
        /// </summary>
        public static int SplitKeepDirection(MeshObject mesh, IReadOnlyList<FaceCorner> corners)
        {
            if (mesh == null || corners == null || corners.Count == 0) return 0;

            var isTargetFace = new HashSet<(int, int)>();   // (面, 頂点)
            foreach (var fc in corners)
            {
                int vi = VertexOf(mesh, fc);
                if (vi >= 0) isTargetFace.Add((fc.Face, vi));
            }

            // （頂点, スロット）→ 使っている面（出現順）
            var users = new Dictionary<(int, int), List<int>>();
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                for (int j = 0; j < face.VertexCount; j++)
                {
                    int slot = SlotOf(mesh, new FaceCorner(fi, j), out int vi);
                    if (slot < 0) continue;
                    if (!users.TryGetValue((vi, slot), out var list))
                    {
                        list = new List<int>();
                        users[(vi, slot)] = list;
                    }
                    if (!list.Contains(fi)) list.Add(fi);
                }
            }

            int changed = 0;
            foreach (var fc in corners)
            {
                int slot = SlotOf(mesh, fc, out int vi);
                if (slot < 0) continue;
                var list = users[(vi, slot)];
                if (list.Count <= 1) continue;

                // 使い手が全部対象なら、先頭の面は元のスロットに残す
                bool allTargets = true;
                foreach (int f in list)
                    if (!isTargetFace.Contains((f, vi))) { allTargets = false; break; }
                if (allTargets && list[0] == fc.Face) continue;

                var v = mesh.Vertices[vi];
                int ns = v.AddUVNormalSlot(v.UVs[slot], v.Normals[slot]);
                var face = mesh.Faces[fc.Face];
                face.UVIndices[fc.Corner] = ns;
                while (face.NormalIndices.Count <= fc.Corner) face.NormalIndices.Add(0);
                face.NormalIndices[fc.Corner] = ns;
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        /// <summary>
        /// 軸回りに回す。axisLocal はローカル空間の軸。worldSpace のときは軸をワールドで解釈し、
        /// 法線をワールドへ出して回してから戻す（拡大縮小を含む行列でも向きが正しくなる）。
        /// 共有スロットは 1 回だけ回す。
        /// </summary>
        public static int RotateAxisAngle(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners,
            Vector3 axis, float degrees, bool worldSpace, Matrix4x4 objectToWorld)
        {
            if (mesh == null || corners == null) return 0;
            if (axis.sqrMagnitude < 1e-12f || Mathf.Approximately(degrees, 0f)) return 0;

            Quaternion q = Quaternion.AngleAxis(degrees, axis.normalized);

            int changed = 0;
            foreach (var fc in UniqueSlotCorners(mesh, corners))
            {
                if (!TryReadNormal(mesh, fc, out var n)) continue;

                Vector3 r = worldSpace
                    ? NormalToLocal(objectToWorld, q * NormalToWorld(objectToWorld, n))
                    : q * n;

                WriteNormal(mesh, fc, r);
                changed++;
            }

            return Finish(mesh, changed, mesh.Name);
        }

        // ================================================================
        // 強度（元の法線との混合）
        // ================================================================

        /// <summary>
        /// 操作の前の値 before（CaptureCornerNormals）と今の値を blend で球面補間して書き直す。
        /// 対象コーナーだけを、（頂点, スロット）で重複を除いて 1 回ずつ書く。
        /// </summary>
        public static void ApplyBlend(
            MeshObject mesh, IReadOnlyList<FaceCorner> corners, Vector3[][] before, float blend)
        {
            if (mesh == null || corners == null || before == null) return;
            blend = Mathf.Clamp01(blend);
            if (blend >= 1f) return;

            int changed = 0;
            foreach (var fc in UniqueSlotCorners(mesh, corners))
            {
                if (fc.Face >= before.Length) continue;
                var prev = before[fc.Face];
                if (fc.Corner >= prev.Length) continue;

                Vector3 b = prev[fc.Corner];
                if (b.sqrMagnitude < 1e-12f) continue;
                if (!TryReadNormal(mesh, fc, out var a)) continue;

                Vector3 mixed = Vector3.Slerp(b.normalized, a.normalized, blend);
                WriteNormal(mesh, fc, mixed);
                changed++;
            }
            Finish(mesh, changed, mesh.Name);
        }

        // ================================================================
        // 実行（単一オブジェクト）
        // ================================================================

        /// <summary>
        /// 1 オブジェクトに法線編集を掛ける。対象・保護・分離・強度の手順はファイル冒頭のとおり。
        /// AverageAcrossObjects は複数オブジェクトを同時に見るので ExecuteAcrossObjects を使う。
        /// </summary>
        /// <returns>処理したコーナー数（変更量ではない。変更量は呼び出し側が前後比較で測る）。</returns>
        public static int Execute(MeshContext mc, NormalEditCommand c)
        {
            var mo = mc?.MeshObject;
            if (mo == null || c == null) return 0;

            var op = c.Operation;

            if (op == NormalEditCommand.Op.RecalcByAngle)
            {
                // 除外セット（自動の再計算から守る）は RecalcByAngle 内で保たれる
                RecalcByAngle(mo, c.AngleDeg, c.WeightMode);
                return mo.FaceCount;
            }
            if (op == NormalEditCommand.Op.AverageAcrossObjects) return 0;

            var corners = CollectEditCorners(mc, out _);
            if (corners.Count == 0) return 0;

            Matrix4x4 m = ObjectToWorld(mc);

            bool useBlend = !NormalEditCommand.IgnoresBlend(op) && c.Blend < 1f;
            var before = useBlend ? CaptureCornerNormals(mo) : null;

            // Break / SplitKeepDirection は対象コーナーのスロットだけを付け替えるので分離は要らない
            if (c.ProtectUnselected
                && op != NormalEditCommand.Op.Break
                && op != NormalEditCommand.Op.SplitKeepDirection)
            {
                IsolateTargetSlots(mo, corners);
            }

            Vector3 target = c.WorldSpace ? m.inverse.MultiplyPoint3x4(c.Target) : c.Target;
            Vector3 dir    = c.WorldSpace ? NormalToLocal(m, c.Direction) : c.Direction;

            int processed;
            switch (op)
            {
                case NormalEditCommand.Op.SetFromFaces:
                    processed = SetFromFaces(mo, corners); break;
                case NormalEditCommand.Op.AverageFromFaces:
                    processed = AverageFromFaces(mo, corners, c.WeightMode); break;
                case NormalEditCommand.Op.Unify:
                    processed = Unify(mo, corners, c.WeightMode); break;
                case NormalEditCommand.Op.Break:
                    processed = Break(mo, corners); break;
                case NormalEditCommand.Op.SplitKeepDirection:
                    processed = SplitKeepDirection(mo, corners); break;
                case NormalEditCommand.Op.AverageAll:
                    processed = AverageAll(mo, corners); break;
                case NormalEditCommand.Op.Smooth:
                    processed = Smooth(mo, corners, c.Strength); break;
                case NormalEditCommand.Op.Sphereize:
                {
                    Vector3 center = c.UseSelectionCenter ? CenterOf(mo, corners) : target;
                    processed = Sphereize(mo, corners, center);
                    break;
                }
                case NormalEditCommand.Op.PointToTarget:
                    processed = PointToTarget(mo, corners, target, c.AlignVectors); break;
                case NormalEditCommand.Op.AlignToAxis:
                {
                    Vector3 axisDir = c.Axis switch
                    {
                        0 => Vector3.right,
                        1 => Vector3.up,
                        _ => Vector3.forward,
                    };
                    if (c.Negative) axisDir = -axisDir;
                    if (c.WorldSpace) axisDir = NormalToLocal(m, axisDir);
                    processed = SetDirection(mo, corners, axisDir);
                    break;
                }
                case NormalEditCommand.Op.AlignToVector:
                    processed = SetDirection(mo, corners, dir); break;
                case NormalEditCommand.Op.RotateAxisAngle:
                    processed = RotateAxisAngle(mo, corners, c.Direction, c.RotateDeg, c.WorldSpace, m); break;
                case NormalEditCommand.Op.FlattenOnAxis:
                    processed = FlattenOnAxis(mo, corners, c.Axis); break;
                case NormalEditCommand.Op.MirrorFlattenSeamX:
                    processed = FlattenMirrorSeamX(mo, corners, c.MirrorThreshold); break;
                case NormalEditCommand.Op.Flip:
                    processed = Flip(mo, corners); break;
                default:
                    processed = 0; break;
            }

            if (useBlend && processed > 0) ApplyBlend(mo, corners, before, c.Blend);

            return processed;
        }

        // ================================================================
        // 実行（オブジェクト間の継ぎ目）
        // ================================================================

        private struct SeamEntry
        {
            public int Mesh;
            public FaceCorner Corner;
            public Vector3 WorldPos;
            public Vector3 WorldNormal;
        }

        /// <summary>
        /// AverageAcrossObjects。各オブジェクトの対象コーナー（選択の規則と手動保護に従う）を
        /// ワールドへ出し、SeamDistance 以内に重なる点の集まりのうち 2 つ以上のオブジェクトに
        /// またがるものだけ、法線をワールドで平均して戻す。Blend と ProtectUnselected も効く。
        /// </summary>
        /// <returns>書き換えたスロット数。</returns>
        public static int ExecuteAcrossObjects(IList<MeshContext> targets, NormalEditCommand c)
        {
            if (targets == null || targets.Count < 2 || c == null) return 0;

            float dist = Mathf.Max(c.SeamDistance, 1e-7f);
            var entries = new List<SeamEntry>();
            var mats = new Matrix4x4[targets.Count];

            for (int mi = 0; mi < targets.Count; mi++)
            {
                var mc = targets[mi];
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                mats[mi] = ObjectToWorld(mc);
                var corners = CollectEditCorners(mc, out _);
                if (corners.Count == 0) continue;
                if (c.ProtectUnselected) IsolateTargetSlots(mo, corners);

                foreach (var fc in UniqueSlotCorners(mo, corners))
                {
                    int vi = VertexOf(mo, fc);
                    if (vi < 0 || !TryReadNormal(mo, fc, out var n)) continue;
                    entries.Add(new SeamEntry
                    {
                        Mesh        = mi,
                        Corner      = fc,
                        WorldPos    = mats[mi].MultiplyPoint3x4(mo.Vertices[vi].Position),
                        WorldNormal = NormalToWorld(mats[mi], n),
                    });
                }
            }
            if (entries.Count < 2) return 0;

            // 格子で近傍を引き、距離 dist 以内を同じ集まりにする（Union-Find）
            var parent = new int[entries.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(
                Mathf.FloorToInt(p.x / dist), Mathf.FloorToInt(p.y / dist), Mathf.FloorToInt(p.z / dist));

            for (int i = 0; i < entries.Count; i++)
            {
                var cell = Cell(entries[i].WorldPos);
                if (!grid.TryGetValue(cell, out var list)) { list = new List<int>(); grid[cell] = list; }
                list.Add(i);
            }

            float dist2 = dist * dist;
            for (int i = 0; i < entries.Count; i++)
            {
                var cell = Cell(entries[i].WorldPos);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!grid.TryGetValue(cell + new Vector3Int(dx, dy, dz), out var list)) continue;
                    foreach (int k in list)
                    {
                        if (k <= i) continue;
                        if ((entries[k].WorldPos - entries[i].WorldPos).sqrMagnitude > dist2) continue;
                        int ri = Find(parent, i), rk = Find(parent, k);
                        if (ri != rk) parent[rk] = ri;
                    }
                }
            }

            var clusters = new Dictionary<int, List<int>>();
            for (int i = 0; i < entries.Count; i++)
            {
                int r = Find(parent, i);
                if (!clusters.TryGetValue(r, out var list)) { list = new List<int>(); clusters[r] = list; }
                list.Add(i);
            }

            float blend = NormalEditCommand.IgnoresBlend(c.Operation) ? 1f : Mathf.Clamp01(c.Blend);
            int written = 0;
            var touched = new HashSet<int>();

            foreach (var list in clusters.Values)
            {
                if (list.Count < 2) continue;

                int firstMesh = entries[list[0]].Mesh;
                bool spans = false;
                Vector3 sum = Vector3.zero;
                foreach (int i in list)
                {
                    if (entries[i].Mesh != firstMesh) spans = true;
                    sum += entries[i].WorldNormal;
                }
                if (!spans || sum.sqrMagnitude < 1e-12f) continue;
                Vector3 avg = sum.normalized;

                foreach (int i in list)
                {
                    var e  = entries[i];
                    var mo = targets[e.Mesh].MeshObject;
                    Vector3 w = blend >= 1f ? avg : Vector3.Slerp(e.WorldNormal, avg, blend);
                    WriteNormal(mo, e.Corner, NormalToLocal(mats[e.Mesh], w));
                    touched.Add(e.Mesh);
                    written++;
                }
            }

            foreach (int mi in touched)
                Finish(targets[mi].MeshObject, 1, targets[mi].MeshObject.Name);

            return written;
        }

        // ================================================================
        // 範囲の分類（法線表示の色分け用）
        // ================================================================

        /// <summary>スロットの分類。法線表示の色分けに使う。数値が大きいほど優先して表示する。</summary>
        public enum SlotScope : byte
        {
            /// <summary>どれにも当たらない。</summary>
            Other = 0,
            /// <summary>編集の対象。</summary>
            Target = 1,
            /// <summary>対象と対象外の両方が使っている（選択外の保護が無いと選択外も変わる）。</summary>
            SharedInfluence = 2,
            /// <summary>除外セットで自動の再計算から守られている。</summary>
            ProtectedAuto = 3,
            /// <summary>除外セットで手動の法線編集からも守られている。</summary>
            ProtectedManual = 4,
        }

        /// <summary>
        /// オブジェクトの全スロットを分類して [頂点][スロット] で返す。
        /// 対象は今の選択（CollectEditCorners と同じ規則）で決める。
        /// </summary>
        public static SlotScope[][] ClassifySlots(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return new SlotScope[0][];

            var result = new SlotScope[mo.Vertices.Count][];
            for (int v = 0; v < mo.Vertices.Count; v++)
                result[v] = new SlotScope[mo.Vertices[v]?.Normals.Count ?? 0];

            void Raise(int vi, int slot, SlotScope s)
            {
                if (vi < 0 || vi >= result.Length) return;
                if (slot < 0 || slot >= result[vi].Length) return;
                if ((byte)s > (byte)result[vi][slot]) result[vi][slot] = s;
            }

            // 除外セット（自動のみ / 手動も）
            var autoCorners = new HashSet<(int, int)>();
            var list = mo.NormalRecalcExcludeList;
            if (list != null)
            {
                var verts = new HashSet<int>();
                var faces = new HashSet<int>();
                foreach (var set in list)
                {
                    if (set == null) continue;
                    foreach (int v in set.Vertices) verts.Add(v);
                    foreach (var e in set.Edges) { verts.Add(e.V1); verts.Add(e.V2); }
                    foreach (int f in set.Faces) faces.Add(f);
                }
                for (int fi = 0; fi < mo.Faces.Count; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face == null || face.VertexCount < 3) continue;
                    for (int j = 0; j < face.VertexCount; j++)
                        if (faces.Contains(fi) || verts.Contains(face.VertexIndices[j]))
                            autoCorners.Add((fi, j));
                }
            }
            var manual = CollectManualProtectedCorners(mo);

            var targets = CollectEditCorners(mc, out _);
            var isTarget = new HashSet<(int, int)>();
            foreach (var fc in targets) isTarget.Add((fc.Face, fc.Corner));

            var targetSlots = new HashSet<(int, int)>();
            foreach (var fc in targets)
            {
                int slot = SlotOf(mo, fc, out int vi);
                if (slot >= 0) { targetSlots.Add((vi, slot)); Raise(vi, slot, SlotScope.Target); }
            }

            for (int fi = 0; fi < mo.Faces.Count; fi++)
            {
                var face = mo.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;
                for (int j = 0; j < face.VertexCount; j++)
                {
                    var fc = new FaceCorner(fi, j);
                    int slot = SlotOf(mo, fc, out int vi);
                    if (slot < 0) continue;

                    if (manual.Contains((fi, j)))           Raise(vi, slot, SlotScope.ProtectedManual);
                    else if (autoCorners.Contains((fi, j))) Raise(vi, slot, SlotScope.ProtectedAuto);

                    if (!isTarget.Contains((fi, j)) && targetSlots.Contains((vi, slot)))
                        Raise(vi, slot, SlotScope.SharedInfluence);
                }
            }
            return result;
        }

        /// <summary>
        /// 法線表示を範囲で色分けするか。法線編集パネルのチェックが書き、
        /// MeshSceneRenderer.BuildNormalLineMesh が読む（SkinWeightPaintTool.ActivePanel と同じ持ち方）。
        /// </summary>
        public static bool ScopeColoringEnabled { get; set; }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }
    }
}
