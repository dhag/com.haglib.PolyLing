// SelectionClipboardOps.cs
// 選択部分のコピー・貼り付け（Ctrl+C / Ctrl+X / Ctrl+V / Shift+D の実体）。
// Undo とモデルの更新通知は呼び出し側（PlayerCommandDispatcher.Clipboard.cs）が持つ。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【写す範囲】
//   SelectionDuplicateOps.Extract と同じ（選択の頂点・辺・線分・面。辺は両端の頂点だけを写す）。
//
// 【座標】
//   写したときに、頂点の位置と法線をモデル空間（バインド姿勢のワールド。VertexToBindWorldMatrix）
//   へ移して持つ。貼り付け先では、貼り付け先オブジェクトの格納空間（BindWorldToVertexMatrix）へ戻す。
//   同じオブジェクトへ貼れば元と同じ位置に重なる。
//
// 【頂点 ID・面 ID】
//   写さない（0 = 未付与）。AssignMissingIds も呼ばない。
//
// 【別のモデルへ貼るとき】
//   ボーンウェイトは外す（ボーン番号はモデルの MeshContextList の番号なので、別のモデルでは別物を指す）。
//   マテリアル番号は 0 にする（マテリアルのスロットもモデルごと）。
//
// 【既存オブジェクトへ貼る（AppendInto）】
//   末尾へ頂点と面を足す。ボーンウェイトは「同じモデル かつ 貼り付け先がスキンド」のときだけ残す。
//   ミラーの相手（MirrorBranchOps.CollectMirrorPeers）があれば、ミラー側にも同じ番号で鏡像を足す。
//   鏡像の作り方は MirrorBranchOps.BuildMirroredMeshObject と同じ
//   （位置 MirrorPoint・法線 MirrorNormal・面は逆順・マテリアルに MirrorMaterialOffset）。
//   貼った部分だけが選ばれた状態にする（貼り付け先の選択を、写したときの選択の形で置き換える）。
//
// 【新しいオブジェクトとして貼る（AddAsNewObjects）】
//   写した部分 1 つ（元のオブジェクト 1 つ）ごとに 1 つ作る。
//   同じモデル … 写したときの元のオブジェクトの属性（SelectionDuplicateOps.Duplicate と同じ複製）を使う。
//                 親・モーフの親・ミラー元モーフは、写したときの実体を今の番号へ引き直す（消えていれば外す）。
//   別のモデル … 新しい空のオブジェクト（MeshListOps.AddNewMesh と同じ形）。親なし・変換は単位。
//   名前は「元の名前_paste」（重なるときは GenerateUniqueMeshName が番号を付ける）。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    /// <summary>貼り付け先。</summary>
    public enum SelectionPasteDestination
    {
        /// <summary>編集対象のオブジェクトの末尾へ足す。</summary>
        ActiveObject = 0,

        /// <summary>新しいオブジェクトを作って貼る。</summary>
        NewObject = 1,
    }

    /// <summary>コピーした部分 1 つ（元のオブジェクト 1 つぶん）。</summary>
    public sealed class SelectionClipboardPart
    {
        /// <summary>元のオブジェクトの名前。</summary>
        public string SourceName;

        /// <summary>元のオブジェクト（その場で複製の貼り付け先）。</summary>
        public MeshContext SourceContext;

        /// <summary>元のオブジェクトの属性の写し（同じモデルへ新規オブジェクトとして貼るときに使う）。</summary>
        public MeshContext Template;

        /// <summary>写したときの親・モーフの親・ミラー元モーフ（実体）。貼るときに今の番号へ引き直す。</summary>
        public MeshContext ParentRef;
        public MeshContext MorphParentRef;
        public MeshContext MirrorOfMorphRef;

        /// <summary>頂点（モデル空間。ID は 0）。</summary>
        public List<Vertex> Vertices;

        /// <summary>面（頂点番号はこの部分の中の番号。ID は 0）。</summary>
        public List<Face> Faces;

        /// <summary>写したときの選択（この部分の中の番号）。</summary>
        public SelectionSnapshot Selection;
    }

    /// <summary>選択部分のクリップボード。プロジェクトに 1 つ（ProjectContext.SelectionClipboard）。保存しない。</summary>
    public sealed class SelectionClipboard
    {
        /// <summary>写し元のモデル。</summary>
        public ModelContext SourceModel;

        public List<SelectionClipboardPart> Parts = new List<SelectionClipboardPart>();

        public int VertexCount => Parts.Sum(p => p.Vertices.Count);
        public int FaceCount   => Parts.Sum(p => p.Faces.Count);
    }

    /// <summary>貼り付けの結果。</summary>
    public struct SelectionPasteResult
    {
        public int Vertices;
        public int Faces;

        /// <summary>外したボーンウェイトの頂点数。</summary>
        public int WeightsRemoved;

        /// <summary>スキンドの貼り付け先へ、ウェイトを持たずに入った頂点数。</summary>
        public int VerticesWithoutWeight;

        /// <summary>鏡像を足したミラー側の数。</summary>
        public int MirrorsUpdated;
    }

    public static class SelectionClipboardOps
    {
        // ================================================================
        // 写す
        // ================================================================

        /// <summary>
        /// 指定オブジェクトの選択部分を写す。写すものが 1 つも無ければ null を返し、reason に理由を入れる。
        /// 元のオブジェクトは書き換えない。
        /// </summary>
        public static SelectionClipboard Capture(ModelContext model, IEnumerable<int> masterIndices, out string reason)
        {
            reason = "";
            if (model == null) { reason = "モデルがありません"; return null; }

            model.ComputeWorldMatrices();

            var clip = new SelectionClipboard { SourceModel = model };
            foreach (int idx in masterIndices.Distinct().OrderBy(i => i))
            {
                var src = model.GetMeshContext(idx);
                if (src?.MeshObject == null || src.Type == MeshType.Bone) continue;
                if (!SelectionDuplicateOps.HasSelection(src)) continue;

                var ex = SelectionDuplicateOps.Extract(src.MeshObject, src.Selection, false, out _);
                if (ex == null) continue;

                var template = SelectionDuplicateOps.Duplicate(src, false, src.Name, out _, assignMissingIds: false);
                if (template == null) continue;

                // 位置と法線をモデル空間へ。
                Matrix4x4 m = src.VertexToBindWorldMatrix;
                Matrix4x4 nm = NormalMatrix(m);
                foreach (var v in ex.Vertices)
                {
                    v.Id = 0;
                    v.Position = m.MultiplyPoint3x4(v.Position);
                    TransformNormals(v, nm);
                }
                foreach (var f in ex.Faces) f.Id = 0;

                var list = model.MeshContextList;
                clip.Parts.Add(new SelectionClipboardPart
                {
                    SourceName       = src.Name,
                    SourceContext    = src,
                    Template         = template,
                    ParentRef        = SafeGet(list, src.HierarchyParentIndex),
                    MorphParentRef   = SafeGet(list, src.MorphParentIndex),
                    MirrorOfMorphRef = SafeGet(list, src.MirrorOfMorphIndex),
                    Vertices         = ex.Vertices,
                    Faces            = ex.Faces,
                    Selection        = MapSelection(src.Selection, ex),
                });
            }

            if (clip.Parts.Count == 0)
            {
                reason = "選択中の描画オブジェクトに、頂点・辺・線分・面の選択がありません";
                return null;
            }
            return clip;
        }

        // ================================================================
        // 既存オブジェクトへ貼る
        // ================================================================

        /// <summary>
        /// targetIndex のオブジェクトの末尾へ parts を足し、ミラー側にも鏡像を足す。
        /// 貼り付け先の選択は貼った部分だけになる。Undo は呼び出し側が前後のスナップショットで取る。
        /// </summary>
        public static bool AppendInto(
            ModelContext model, int targetIndex, SelectionClipboard clip,
            IList<SelectionClipboardPart> parts, out SelectionPasteResult result, out string reason)
        {
            result = default;
            reason = "";

            var tgt = model?.GetMeshContext(targetIndex);
            var mo  = tgt?.MeshObject;
            if (mo == null) { reason = $"貼り付け先のオブジェクトがありません（masterIndex {targetIndex}）"; return false; }
            if (tgt.Type == MeshType.Bone) { reason = "ボーンには貼り付けられません"; return false; }
            if (MirrorBranchOps.IsMirrorSideContext(tgt))
            { reason = "ミラー側のオブジェクトには貼り付けられません。実体側へ貼ってください"; return false; }
            if (parts == null || parts.Count == 0) { reason = "貼るものがありません"; return false; }

            bool sameModel   = clip != null && clip.SourceModel == model;
            bool keepWeights = sameModel && tgt.IsSkinned;

            // ミラー側への伝播計画。位相を変える前に取る。
            var mirrorPlan = MirrorBranchOps.CaptureMirrorRebuildPlan(model, new[] { targetIndex });

            model.ComputeWorldMatrices();
            Matrix4x4 m  = tgt.BindWorldToVertexMatrix;
            Matrix4x4 nm = NormalMatrix(m);

            int vStart = mo.VertexCount;
            int fStart = mo.FaceCount;
            var newSel = new SelectionSnapshot
            {
                Vertices = new HashSet<int>(), Edges = new HashSet<VertexPair>(),
                Faces    = new HashSet<int>(), Lines = new HashSet<int>(),
            };

            foreach (var part in parts)
            {
                int vb = mo.VertexCount;
                int fb = mo.FaceCount;

                foreach (var src in part.Vertices)
                {
                    var v = src.Clone();
                    v.Id = 0;
                    v.Position = m.MultiplyPoint3x4(src.Position);
                    TransformNormals(v, nm);
                    if (!keepWeights)
                    {
                        if (v.BoneWeight.HasValue || v.MirrorBoneWeight.HasValue) result.WeightsRemoved++;
                        v.BoneWeight = null;
                        v.MirrorBoneWeight = null;
                    }
                    else if (!v.BoneWeight.HasValue)
                    {
                        result.VerticesWithoutWeight++;
                    }
                    mo.AddVertexRaw(v);
                }

                foreach (var src in part.Faces)
                {
                    var f = src.Clone();
                    f.Id = 0;
                    for (int k = 0; k < f.VertexIndices.Count; k++) f.VertexIndices[k] += vb;
                    if (!sameModel) f.MaterialIndex = 0;
                    mo.AddFaceRaw(f);
                }

                OffsetSelection(part.Selection, vb, fb, newSel);
            }

            mo.InvalidatePositionCache();

            int vEnd = mo.VertexCount;
            int fEnd = mo.FaceCount;
            result.Vertices = vEnd - vStart;
            result.Faces    = fEnd - fStart;

            // ミラー側にも同じ番号で鏡像を足す。
            result.MirrorsUpdated = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (realIdx != targetIndex) return false;
                AppendMirrored(tgt, mo, mirrorMo, vStart, vEnd, fStart, fEnd);
                return true;
            });

            tgt.Selection?.RestoreFromSnapshot(newSel);
            return true;
        }

        /// <summary>実体側の [vStart, vEnd) の頂点と [fStart, fEnd) の面の鏡像を、ミラー側の末尾へ足す。</summary>
        private static void AppendMirrored(
            MeshContext realCtx, MeshObject realMo, MeshObject mirrorMo,
            int vStart, int vEnd, int fStart, int fEnd)
        {
            int   axis      = MirrorBranchOps.ResolveMirrorAxis(realCtx);
            float dist      = realCtx.MirrorDistance;
            int   matOffset = realCtx.MirrorMaterialOffset;

            for (int i = vStart; i < vEnd; i++)
            {
                var rv = realMo.Vertices[i];
                var mv = rv.Clone();
                mv.Id = 0;
                mv.Position = MirrorBranchOps.MirrorPoint(axis, dist, rv.Position);
                for (int k = 0; k < mv.Normals.Count; k++)
                    mv.Normals[k] = MirrorBranchOps.MirrorNormal(axis, mv.Normals[k]);
                mv.BoneWeight       = rv.HasMirrorBoneWeight ? rv.MirrorBoneWeight : rv.BoneWeight;
                mv.MirrorBoneWeight = null;
                mirrorMo.AddVertexRaw(mv);
            }

            for (int f = fStart; f < fEnd; f++)
            {
                var rf = realMo.Faces[f];
                var mf = new Face { Id = 0, MaterialIndex = rf.MaterialIndex + matOffset, Flags = rf.Flags };
                for (int i = rf.VertexIndices.Count - 1; i >= 0; i--) mf.VertexIndices.Add(rf.VertexIndices[i]);
                for (int i = rf.UVIndices.Count - 1; i >= 0; i--)     mf.UVIndices.Add(rf.UVIndices[i]);
                for (int i = rf.NormalIndices.Count - 1; i >= 0; i--) mf.NormalIndices.Add(rf.NormalIndices[i]);
                mirrorMo.AddFaceRaw(mf);
            }
        }

        // ================================================================
        // 新しいオブジェクトとして貼る
        // ================================================================

        /// <summary>
        /// 写した部分ごとに新しいオブジェクトを作ってモデルへ足す。足したもの（番号と実体）を返す。
        /// オブジェクトの選択は呼び出し側が決める。Undo も呼び出し側が持つ。
        /// </summary>
        public static List<(int Index, MeshContext Context)> AddAsNewObjects(
            ModelContext model, SelectionClipboard clip, out SelectionPasteResult result, out string reason)
        {
            result = default;
            reason = "";
            var added = new List<(int, MeshContext)>();
            if (model == null) { reason = "モデルがありません"; return added; }
            if (clip == null || clip.Parts.Count == 0) { reason = "貼るものがありません"; return added; }

            bool sameModel = clip.SourceModel == model;

            foreach (var part in clip.Parts)
            {
                string name = model.GenerateUniqueMeshName((part.SourceName ?? "Mesh") + "_paste");

                MeshContext mc;
                if (sameModel && part.Template != null)
                {
                    mc = MeshContextCloneOps.Clone(part.Template, MeshContextCloneKind.NewObject, name);
                    var list = model.MeshContextList;
                    int parent = IndexOf(list, part.ParentRef);
                    mc.HierarchyParentIndex = parent;
                    if (parent < 0) mc.Depth = 0;
                    mc.MorphParentIndex   = IndexOf(list, part.MorphParentRef);
                    mc.MirrorOfMorphIndex = IndexOf(list, part.MirrorOfMorphRef);
                }
                else
                {
                    mc = new MeshContext { MeshObject = new MeshObject(name) };
                }

                var mo = mc.MeshObject;
                mo.Vertices = new List<Vertex>(part.Vertices.Count);
                foreach (var src in part.Vertices)
                {
                    var v = src.Clone();
                    v.Id = 0;
                    if (!sameModel)
                    {
                        if (v.BoneWeight.HasValue || v.MirrorBoneWeight.HasValue) result.WeightsRemoved++;
                        v.BoneWeight = null;
                        v.MirrorBoneWeight = null;
                    }
                    mo.Vertices.Add(v);
                }
                mo.Faces = new List<Face>(part.Faces.Count);
                foreach (var src in part.Faces)
                {
                    var f = src.Clone();
                    f.Id = 0;
                    if (!sameModel) f.MaterialIndex = 0;
                    mo.Faces.Add(f);
                }
                mo.RebuildIdSets();

                int idx = model.Add(mc);
                added.Add((idx, mc));

                result.Vertices += mo.VertexCount;
                result.Faces    += mo.FaceCount;
            }

            // 足したオブジェクトの格納空間へ位置と法線を戻す（親を含むワールドが決まってから）。
            model.ComputeWorldMatrices();
            for (int i = 0; i < added.Count; i++)
            {
                var mc = added[i].Item2;
                var mo = mc.MeshObject;
                Matrix4x4 m  = mc.BindWorldToVertexMatrix;
                Matrix4x4 nm = NormalMatrix(m);
                foreach (var v in mo.Vertices)
                {
                    v.Position = m.MultiplyPoint3x4(v.Position);
                    TransformNormals(v, nm);
                }
                mo.InvalidatePositionCache();
                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();
                mc.UnityMesh = mo.ToUnityMesh(model.MaterialCount);

                var sel = clip.Parts[i].Selection;
                if (sel != null) mc.Selection.RestoreFromSnapshot(sel);
            }

            return added;
        }

        // ================================================================
        // 補助
        // ================================================================

        /// <summary>元の選択を、写した部分の中の番号へ置き換える。</summary>
        private static SelectionSnapshot MapSelection(SelectionState sel, SelectionExtract ex)
        {
            var s = new SelectionSnapshot
            {
                Mode     = sel.Mode,
                Vertices = new HashSet<int>(), Edges = new HashSet<VertexPair>(),
                Faces    = new HashSet<int>(), Lines = new HashSet<int>(),
            };
            foreach (int v in sel.Vertices)
                if (ex.VertexMap.TryGetValue(v, out int nv)) s.Vertices.Add(nv);
            foreach (var e in sel.Edges)
                if (ex.VertexMap.TryGetValue(e.V1, out int a) && ex.VertexMap.TryGetValue(e.V2, out int b))
                    s.Edges.Add(new VertexPair(a, b));
            foreach (int f in sel.Faces)
                if (ex.FaceMap.TryGetValue(f, out int nf)) s.Faces.Add(nf);
            foreach (int f in sel.Lines)
                if (ex.FaceMap.TryGetValue(f, out int nf)) s.Lines.Add(nf);
            return s;
        }

        /// <summary>部分の選択を、頂点 vb・面 fb だけずらして into へ足す。</summary>
        private static void OffsetSelection(SelectionSnapshot s, int vb, int fb, SelectionSnapshot into)
        {
            if (s == null) return;
            foreach (int v in s.Vertices) into.Vertices.Add(v + vb);
            foreach (var e in s.Edges)    into.Edges.Add(new VertexPair(e.V1 + vb, e.V2 + vb));
            foreach (int f in s.Faces)    into.Faces.Add(f + fb);
            foreach (int f in s.Lines)    into.Lines.Add(f + fb);
        }

        /// <summary>法線に掛ける行列（位置の行列の逆転置）。</summary>
        private static Matrix4x4 NormalMatrix(Matrix4x4 m) => m.inverse.transpose;

        private static void TransformNormals(Vertex v, Matrix4x4 nm)
        {
            for (int k = 0; k < v.Normals.Count; k++)
            {
                Vector3 n = nm.MultiplyVector(v.Normals[k]);
                if (n.sqrMagnitude > 1e-20f) v.Normals[k] = n.normalized;
            }
        }

        private static MeshContext SafeGet(IList<MeshContext> list, int index)
            => (list != null && index >= 0 && index < list.Count) ? list[index] : null;

        private static int IndexOf(IList<MeshContext> list, MeshContext mc)
            => (list != null && mc != null) ? list.IndexOf(mc) : -1;
    }
}
