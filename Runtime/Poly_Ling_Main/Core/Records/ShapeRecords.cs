// ShapeRecords.cs
// 形（名前付きの頂点位置）の Undo 記録：切り替えと、一覧の変更（追加・削除・属性）。
// Runtime/Poly_Ling_Main/Core/Records/ に配置
//
// 一覧の履歴（MeshListStack）に積む。Undo は記録順に戻るので、形を足して切り替えた
// ときは「追加」「切り替え」の 2 件を積めば、切り替え→追加の順に戻る。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.UndoSystem
{
    /// <summary>今の形の切り替えの記録。</summary>
    public class ShapeSwitchRecord : MeshListUndoRecord
    {
        /// <summary>切り替え前の今の形。</summary>
        public int From;
        /// <summary>切り替え後の今の形。</summary>
        public int To;
        /// <summary>切り替え前の「直前の形」。</summary>
        public int PreviousBefore;
        /// <summary>切り替え後の「直前の形」。</summary>
        public int PreviousAfter;

        public override void Undo(ModelContext ctx)
        {
            if (ctx == null) return;
            ShapeOps.SwitchModel(ctx, From);
            ctx.PreviousShapeId = PreviousBefore;
            ctx.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            if (ctx == null) return;
            ShapeOps.SwitchModel(ctx, To);
            ctx.PreviousShapeId = PreviousAfter;
            ctx.OnListChanged?.Invoke();
        }
    }

    /// <summary>
    /// 形の一覧の変更の記録（追加・削除・属性）。一覧は前後を丸ごと控える。
    /// 頂点の控えは、変えた形 1 つぶんだけを前後で持つ。
    /// </summary>
    public class ShapeListRecord : MeshListUndoRecord
    {
        /// <summary>変更前の一覧（複製）。</summary>
        public List<ShapeDef> Before;
        /// <summary>変更後の一覧（複製）。</summary>
        public List<ShapeDef> After;

        /// <summary>頂点の控えを変えた形の番号。0 なら控えは変えていない。</summary>
        public int ShapeId;
        /// <summary>変更前の、その形の控え。</summary>
        public List<ShapeEntryRef> EntriesBefore;
        /// <summary>変更後の、その形の控え。</summary>
        public List<ShapeEntryRef> EntriesAfter;

        /// <summary>変更前後の、その形の UV の控え（UV の形。null は空と同じ）。</summary>
        public List<ShapeUVEntryRef> UVEntriesBefore;
        public List<ShapeUVEntryRef> UVEntriesAfter;

        /// <summary>変更前後の「直前の形」。</summary>
        public int PreviousBefore;
        public int PreviousAfter;

        public static List<ShapeDef> CloneList(List<ShapeDef> src)
        {
            return src == null
                ? new List<ShapeDef>()
                : src.Where(s => s != null).Select(s => s.Clone()).ToList();
        }

        private void Apply(ModelContext ctx, List<ShapeDef> defs, List<ShapeEntryRef> entries,
                           List<ShapeUVEntryRef> uvEntries, int previous)
        {
            if (ctx == null) return;
            ctx.Shapes = CloneList(defs);
            if (ShapeId != ShapeOps.BaseShapeId)
            {
                ShapeOps.RemoveEntries(ctx, ShapeId);
                ShapeOps.ApplyEntries(entries, ShapeId);
                ShapeOps.ApplyUVEntries(uvEntries, ShapeId);
            }
            ctx.PreviousShapeId = previous;
            ctx.IsDirty = true;
            ctx.OnListChanged?.Invoke();
        }

        public override void Undo(ModelContext ctx) => Apply(ctx, Before, EntriesBefore, UVEntriesBefore, PreviousBefore);
        public override void Redo(ModelContext ctx) => Apply(ctx, After, EntriesAfter, UVEntriesAfter, PreviousAfter);
    }

    /// <summary>
    /// モデルの形まわりの丸ごとの控え：形の一覧（読み込み元を含む）、基本の形の読み込み元、今の形・直前の形、
    /// 全描画オブジェクトの頂点位置と形の控え。MQO の読み込み（形を作る・切り替える・読むを 1 件にまとめる）で使う。
    /// </summary>
    public class ShapeModelState
    {
        public List<ShapeDef> Shapes;
        public ShapeSource    BaseSource;
        public int Current;
        public int Previous;

        public class MeshState
        {
            public MeshUndoTarget Target;
            public UnityEngine.Vector3[] Positions;
            public ShapeSnapshot Shapes;
            /// <summary>UV スロット 0（UV を持たない頂点は has が false）。</summary>
            public UnityEngine.Vector2[] UV0;
            public bool[] HasUV0;
            /// <summary>形の UV の控え（頂点番号順。持たない頂点は null。全頂点が持たなければ配列ごと null）。</summary>
            public List<ShapeUV>[] UVShapes;
        }
        public List<MeshState> Meshes = new List<MeshState>();

        public static ShapeModelState Capture(ModelContext model)
        {
            var s = new ShapeModelState
            {
                Shapes     = ShapeListRecord.CloneList(model.Shapes),
                BaseSource = model.BaseShapeSource?.Clone(),
                Current    = model.CurrentShapeId,
                Previous   = model.PreviousShapeId,
            };
            if (model.MeshContextList != null)
                foreach (var mc in model.MeshContextList)
                {
                    var mo = mc?.MeshObject;
                    if (mo == null || mo.VertexCount == 0) continue;
                    var pos = new UnityEngine.Vector3[mo.VertexCount];
                    var uv0 = new UnityEngine.Vector2[mo.VertexCount];
                    var has = new bool[mo.VertexCount];
                    List<ShapeUV>[] uvs = null;
                    for (int i = 0; i < pos.Length; i++)
                    {
                        var v = mo.Vertices[i];
                        pos[i] = v.Position;
                        if (ShapeOps.HasUV0(v)) { has[i] = true; uv0[i] = v.UVs[0]; }
                        if (v.UVShapes != null)
                        {
                            if (uvs == null) uvs = new List<ShapeUV>[pos.Length];
                            uvs[i] = new List<ShapeUV>(v.UVShapes);
                        }
                    }
                    s.Meshes.Add(new MeshState
                    {
                        Target = MeshUndoTarget.Of(mc, model),
                        Positions = pos,
                        Shapes = ShapeOps.CaptureShapes(mo),
                        UV0 = uv0,
                        HasUV0 = has,
                        UVShapes = uvs,
                    });
                }
            return s;
        }

        /// <summary>控えた状態へ戻し、表示用メッシュを作り直す。</summary>
        public void Restore(ModelContext model)
        {
            if (model == null) return;
            model.Shapes          = ShapeListRecord.CloneList(Shapes);
            model.BaseShapeSource = BaseSource?.Clone();
            model.CurrentShapeId  = Current;
            model.PreviousShapeId = Previous;
            foreach (var m in Meshes)
            {
                var mc = m.Target.Resolve();
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                int n = System.Math.Min(mo.VertexCount, m.Positions.Length);
                for (int i = 0; i < n; i++)
                {
                    var v = mo.Vertices[i];
                    v.Position = m.Positions[i];
                    if (m.HasUV0 != null && i < m.HasUV0.Length && m.HasUV0[i] && ShapeOps.HasUV0(v)) v.UVs[0] = m.UV0[i];
                    var us = m.UVShapes != null && i < m.UVShapes.Length ? m.UVShapes[i] : null;
                    v.UVShapes = us != null ? new List<ShapeUV>(us) : null;
                }
                ShapeOps.RestoreShapes(mo, m.Shapes);
                mo.InvalidatePositionCache();
                mc.ReplaceUnityMesh(mo.ToUnityMesh());
            }
            model.IsDirty = true;
        }
    }

    /// <summary>形まわりを丸ごと前後で控える記録（ShapeModelState）。</summary>
    public class ShapeStateRecord : MeshListUndoRecord
    {
        public ShapeModelState Before;
        public ShapeModelState After;

        public override void Undo(ModelContext ctx)
        {
            Before?.Restore(ctx);
            ctx?.OnListChanged?.Invoke();
        }

        public override void Redo(ModelContext ctx)
        {
            After?.Restore(ctx);
            ctx?.OnListChanged?.Invoke();
        }
    }
}
