// NormalEditPreviewState.cs
// 法線編集のプレビュー状態。法線移植の NormalTransplantPreviewState と同じ役割。
// UnityEditor非依存
//
// 【方式】
//   開始時に対象メッシュのスロット（頂点ごとの UV・法線の並び）と、面ごとの
//   UV／法線の添字を丸ごと控える。パラメータが変わるたびに「控えへ戻す → 同じ
//   NormalEditOps.Execute を掛ける」をやり直すので、どの順で値を動かしても
//   同じ値なら同じ結果になる（0.2 → 0.8 → 0.2 で最初と最後が一致する）。
//   スロットを増やす操作（分離・選択外の保護）も控えへ戻せば元のスロット構成に戻る。
//
// 【生成ミラー】
//   生成ミラーは実体側から作り直す（MirrorBranchOps.RebakeDerivedMirrorNormals）ので
//   控えない。実体側を戻してから作り直せばミラー側も戻る。
//
// 【確定】
//   プレビューは確定しない。決定時は End で元に戻してから NormalEditCommand を送り、
//   コマンド側で同じ計算をやり直す（1 件の Undo になる。法線移植と同じ）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.UI
{
    public class NormalEditPreviewState
    {
        private sealed class Backup
        {
            public MeshContext Context;
            public List<Vector2>[] UVs;
            public List<Vector3>[] Normals;
            public List<int>[] FaceUV;
            public List<int>[] FaceNormal;
            public bool PreserveNormals;
            public int SlotCount;
        }

        private readonly List<Backup> _backups = new List<Backup>();

        public bool IsActive { get; private set; }

        /// <summary>プレビュー中の対象。</summary>
        public IReadOnlyList<MeshContext> Targets
        {
            get
            {
                var list = new List<MeshContext>(_backups.Count);
                foreach (var b in _backups) list.Add(b.Context);
                return list;
            }
        }

        /// <summary>
        /// プレビューを始める。対象のスロットと面の添字を控える。
        /// </summary>
        public bool Start(IList<MeshContext> targets)
        {
            if (IsActive || targets == null || targets.Count == 0) return false;

            _backups.Clear();
            foreach (var mc in targets)
            {
                var mo = mc?.MeshObject;
                if (mo == null) continue;

                var b = new Backup
                {
                    Context         = mc,
                    UVs             = new List<Vector2>[mo.Vertices.Count],
                    Normals         = new List<Vector3>[mo.Vertices.Count],
                    FaceUV          = new List<int>[mo.Faces.Count],
                    FaceNormal      = new List<int>[mo.Faces.Count],
                    PreserveNormals = mo.PreserveNormals,
                    SlotCount       = NormalEditOps.SlotCount(mo),
                };
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    var vx = mo.Vertices[v];
                    b.UVs[v]     = vx != null ? new List<Vector2>(vx.UVs) : null;
                    b.Normals[v] = vx != null ? new List<Vector3>(vx.Normals) : null;
                }
                for (int f = 0; f < mo.Faces.Count; f++)
                {
                    var face = mo.Faces[f];
                    b.FaceUV[f]     = face != null ? new List<int>(face.UVIndices) : null;
                    b.FaceNormal[f] = face != null ? new List<int>(face.NormalIndices) : null;
                }
                _backups.Add(b);
            }

            IsActive = _backups.Count > 0;
            return IsActive;
        }

        /// <summary>
        /// 控えへ戻してから cmd を掛け直す。表示同期は行わない（呼び出し側の責務）。
        /// </summary>
        /// <param name="slotCountChanged">開始時からスロット数が変わったオブジェクトがあるか。</param>
        public void Apply(ModelContext model, NormalEditCommand cmd, out bool slotCountChanged)
        {
            slotCountChanged = false;
            if (!IsActive || cmd == null) return;

            Restore(model);

            if (cmd.Operation == NormalEditCommand.Op.AverageAcrossObjects)
            {
                var list = new List<MeshContext>();
                foreach (var b in _backups) list.Add(b.Context);
                NormalEditOps.ExecuteAcrossObjects(list, cmd);
            }
            else
            {
                foreach (var b in _backups) NormalEditOps.Execute(b.Context, cmd);
            }

            foreach (var b in _backups)
                if (NormalEditOps.SlotCount(b.Context.MeshObject) != b.SlotCount) slotCountChanged = true;

            if (model != null)
                MirrorBranchOps.RebakeDerivedMirrorNormals(model.MeshContextList, model.MaterialCount);
        }

        /// <summary>
        /// 控えへ戻してから、ここまでのブラシ中心の列を掛け直す（ブラシのなぞり中の表示）。
        /// 確定は NormalBrushStrokeCommand で行う。表示同期は行わない。
        /// </summary>
        public void ApplyBrush(
            ModelContext model, IReadOnlyList<Vector3> centersWorld, float radius, float strength,
            NormalBrushMode mode, Vector3 directionWorld, bool mirrorX)
        {
            if (!IsActive) return;
            Restore(model);
            foreach (var b in _backups)
                NormalBrushOps.ApplyStroke(b.Context, centersWorld, radius, strength, mode, directionWorld, mirrorX);
            if (model != null)
                MirrorBranchOps.RebakeDerivedMirrorNormals(model.MeshContextList, model.MaterialCount);
        }

        /// <summary>控えた状態へ戻す。プレビューは続く。表示同期は行わない。</summary>
        public void Restore(ModelContext model)
        {
            if (!IsActive) return;

            foreach (var b in _backups)
            {
                var mo = b.Context?.MeshObject;
                if (mo == null) continue;

                int vc = Mathf.Min(mo.Vertices.Count, b.UVs.Length);
                for (int v = 0; v < vc; v++)
                {
                    var vx = mo.Vertices[v];
                    if (vx == null || b.UVs[v] == null) continue;
                    vx.UVs.Clear();     vx.UVs.AddRange(b.UVs[v]);
                    vx.Normals.Clear(); vx.Normals.AddRange(b.Normals[v]);
                }
                int fc = Mathf.Min(mo.Faces.Count, b.FaceUV.Length);
                for (int f = 0; f < fc; f++)
                {
                    var face = mo.Faces[f];
                    if (face == null || b.FaceUV[f] == null) continue;
                    face.UVIndices.Clear();     face.UVIndices.AddRange(b.FaceUV[f]);
                    face.NormalIndices.Clear(); face.NormalIndices.AddRange(b.FaceNormal[f]);
                }
                mo.PreserveNormals = b.PreserveNormals;
            }

            if (model != null)
                MirrorBranchOps.RebakeDerivedMirrorNormals(model.MeshContextList, model.MaterialCount);
        }

        /// <summary>控えた状態へ戻して終える。表示同期は行わない。</summary>
        public void End(ModelContext model)
        {
            if (!IsActive) return;
            Restore(model);
            _backups.Clear();
            IsActive = false;
        }

        /// <summary>今の状態が開始時とスロット数で食い違うか（表示を作り直す必要があるか）。</summary>
        public bool SlotCountDiffersFromStart()
        {
            foreach (var b in _backups)
                if (b.Context?.MeshObject != null
                    && NormalEditOps.SlotCount(b.Context.MeshObject) != b.SlotCount) return true;
            return false;
        }
    }
}
