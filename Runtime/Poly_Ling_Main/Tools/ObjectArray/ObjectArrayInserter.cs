// Runtime/Poly_Ling_Main/Tools/ObjectArray/ObjectArrayInserter.cs
// ObjectArrayGenerator が作った生成物を ModelContext へ入れる。
// Undo 記録とビュー更新は呼び出し側（Player の Core）が行う。
//
// 【描画オブジェクトの階層】
//   リスト順 + Depth で決まる（SummaryTreeRoot.BuildHierarchyFromDepth,
//   SummaryTreeRoot.cs:86-114）。ワールド行列は HierarchyParentIndex から
//   組み立てられる（ModelContext.ComputeWorldMatrices, ModelContext.cs:1442-1472）。
//   したがって「出力先の子」にするには
//     ・出力先のサブツリー直後へ挿入し
//     ・Depth = 出力先.Depth + 1 + 相対深さ
//     ・HierarchyParentIndex を親の実インデックスへ
//   の3つを同時に満たす必要がある。
//
// 【挿入で既存の HierarchyParentIndex がずれる】
//   付け替えは ModelContext.Insert (ModelContext.cs:1298) が行う。
//   adjustSelection = true のとき RemapIndexReferences
//   (ModelContext.cs:1229-1271) が ParentIndex / HierarchyParentIndex /
//   MorphParentIndex / BakedMirrorSourceIndex / Humanoid 割当 / T ポーズ退避を
//   まとめて繰り下げるため、ここで先に足してはいけない。
//   （以前ここに事前シフトがあり、Insert の付け替えと二重に掛かって
//     挿入位置以降を親に持つ無関係な描画オブジェクトの親索引が
//     +1 ではなく +2 になり、末尾を親にしていたものは -1 へ飛んでいた。）
//
// 【生成物のトランスフォーム】
//   BoneTransform は単位（UseLocalTransform = false）にする。頂点は
//   ObjectArrayGenerator が出力先のローカル空間で書いているため、
//   親のワールド行列がそのまま自分のワールド行列になり、見た目の位置が保たれる。
//
// Runtime/Poly_Ling_Main/Tools/ObjectArray/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Tools.ObjectArray
{
    public static class ObjectArrayInserter
    {
        // ================================================================
        // 出力先
        // ================================================================

        /// <summary>
        /// 出力先オブジェクト。TargetMasterIndex が -1 か無効なら null（ルート扱い）。
        /// </summary>
        public static MeshContext ResolveTarget(ModelContext model, int targetMasterIndex)
        {
            if (model == null) return null;
            if (targetMasterIndex < 0 || targetMasterIndex >= model.MeshContextCount) return null;
            return model.GetMeshContext(targetMasterIndex);
        }

        /// <summary>
        /// 生成頂点を書き込む空間への変換。ルートなら単位行列。
        /// </summary>
        public static Matrix4x4 GetWorldToOutputLocal(ModelContext model, int targetMasterIndex)
        {
            var target = ResolveTarget(model, targetMasterIndex);
            return target != null ? target.WorldMatrixInverse : Matrix4x4.identity;
        }

        // ================================================================
        // 挿入位置
        // ================================================================

        /// <summary>
        /// 出力先のサブツリー直後の位置を返す。ルート指定・出力先不明ならリスト末尾。
        /// サブツリーの終端は「出力先より後ろで最初に現れる、Depth が出力先以下の
        /// 描画オブジェクト」。ボーン等は Depth の意味が違うので数えない。
        /// </summary>
        public static int ResolveInsertIndex(ModelContext model, int targetMasterIndex)
        {
            if (model == null) return 0;

            var target = ResolveTarget(model, targetMasterIndex);
            if (target == null) return model.MeshContextCount;

            int targetDepth = target.Depth;

            for (int i = targetMasterIndex + 1; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || !IsDrawable(mc)) continue;
                if (mc.Depth <= targetDepth) return i;
            }

            return model.MeshContextCount;
        }

        private static bool IsDrawable(MeshContext mc)
        {
            var t = mc.Type;
            return t == MeshType.Mesh || t == MeshType.BakedMirror || t == MeshType.MirrorSide;
        }

        // ================================================================
        // モード1: 子として生成
        // ================================================================

        /// <summary>
        /// 生成物を出力先の子として挿入する。
        /// 名前の一意化は uniqueName に委ねる（ModelContext.GenerateUniqueMeshName を渡す）。
        /// </summary>
        /// <returns>挿入した (インデックス, MeshContext) の一覧。挿入順。</returns>
        public static List<(int Index, MeshContext MeshContext)> InsertAsChildren(
            ModelContext model,
            IReadOnlyList<ObjectArrayPiece> pieces,
            int targetMasterIndex,
            System.Func<string, string> uniqueName)
        {
            var added = new List<(int, MeshContext)>();
            if (model == null || pieces == null || pieces.Count == 0) return added;

            var target    = ResolveTarget(model, targetMasterIndex);
            int baseDepth = target != null ? target.Depth + 1 : 0;
            int insertAt  = ResolveInsertIndex(model, targetMasterIndex);

            // 入れるものが1つも無ければ何もしない
            // （メッシュを持たない要素は下のループで飛ばすため、先に数える）。
            int validCount = 0;
            foreach (var piece in pieces) if (piece?.Mesh != null) validCount++;
            if (validCount == 0) return added;

            // 相対深さごとに「直近に置いた実インデックス」を覚え、親を解決する。
            // 生成物は入力リスト順（親が先）に並んでいるため、これで足りる。
            var lastAtDepth = new Dictionary<int, int>();

            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (piece?.Mesh == null) continue;

                int index = insertAt + added.Count;
                int depth = baseDepth + piece.RelativeDepth;

                // 組が変わったら深さの記憶を捨てる（前の組の子にぶら下げないため）。
                if (piece.RelativeDepth == 0) lastAtDepth.Clear();

                int parentIndex;
                if (piece.RelativeDepth == 0)
                    parentIndex = (target != null) ? targetMasterIndex : -1;
                else
                    parentIndex = lastAtDepth.TryGetValue(piece.RelativeDepth - 1, out int pi) ? pi : -1;

                var ctx = BuildContextFor(model, piece, uniqueName);
                ctx.Depth = depth;

                model.Insert(index, ctx);

                // Insert のあとに書く。挿入前は自身の親索引が -1 で、
                // Insert の付け替え対象にならないため、ここで確定させる。
                ctx.HierarchyParentIndex = parentIndex;
                ctx.ParentIndex          = parentIndex;

                lastAtDepth[piece.RelativeDepth] = index;
                added.Add((index, ctx));
            }

            return added;
        }

        // ================================================================
        // モード2: 出力先の中へ統合
        // ================================================================

        /// <summary>
        /// 生成物の頂点・面を dst へ連結する。
        /// 連結の仕方は PolyLingPlayerViewerCore.PrimitiveMeshAddToExisting と同じ規則。
        /// 頂点は Clone で丸ごと写し、UV / 法線スロットと NormalIndices もそのまま持ち越す
        /// （RecalculateNormals を掛けると全部フラットに潰れるため掛けない）。
        /// </summary>
        public static void AppendInto(MeshObject dst, IReadOnlyList<ObjectArrayPiece> pieces)
        {
            if (dst == null || pieces == null) return;

            foreach (var piece in pieces)
            {
                var src = piece?.Mesh;
                if (src == null || src.VertexCount == 0) continue;

                int baseIdx = dst.VertexCount;

                for (int v = 0; v < src.VertexCount; v++)
                {
                    var sv = src.Vertices[v];
                    dst.Vertices.Add(sv != null ? sv.Clone() : new Vertex(Vector3.zero));
                }

                for (int f = 0; f < src.FaceCount; f++)
                {
                    var sf = src.Faces[f];
                    if (sf?.VertexIndices == null || sf.VertexIndices.Count < 3) continue;

                    var nf = new Face { MaterialIndex = sf.MaterialIndex };
                    nf.VertexIndices = sf.VertexIndices.ConvertAll(i => i + baseIdx);
                    nf.UVIndices     = new List<int>(sf.UVIndices);
                    nf.NormalIndices = new List<int>(sf.NormalIndices);
                    dst.AddFace(nf);
                }
            }

            dst.InvalidatePositionCache();
        }

        /// <summary>
        /// 生成物を1つの MeshObject へまとめる。
        /// 「中に生成」でルートを指定したときの、新規オブジェクトの中身になる。
        /// </summary>
        public static MeshObject CombineAll(IReadOnlyList<ObjectArrayPiece> pieces, string name)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "ObjectArray" : name);
            AppendInto(mo, pieces);
            return mo;
        }

        // ================================================================
        // 共通
        // ================================================================

        /// <summary>
        /// 生成メッシュから MeshContext を作る。名前は uniqueName で一意化する。
        /// トランスフォームは単位。頂点が出力先の空間で入っているため。
        /// </summary>
        public static MeshContext BuildContextFor(
            ModelContext model, ObjectArrayPiece piece, System.Func<string, string> uniqueName)
        {
            string name = uniqueName != null ? uniqueName(piece.Name) : piece.Name;

            var mo = piece.Mesh;
            mo.Name = name;

            // 複製元から受け継いだ階層情報は捨てる（置き場所はここで決め直すため）。
            mo.ParentIndex           = -1;
            mo.HierarchyParentIndex  = -1;
            mo.Depth                 = 0;

            if (mo.BoneTransform != null)
            {
                mo.BoneTransform.UseLocalTransform = false;
                mo.BoneTransform.Position          = Vector3.zero;
                mo.BoneTransform.Rotation          = Vector3.zero;
                mo.BoneTransform.Scale             = Vector3.one;
            }

            var unityMesh = mo.ToUnityMesh();
            unityMesh.name      = name;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            // MeshContext.Name は MeshObject.Name への委譲（MeshContext.cs:32-36）。
            // MeshObject を先に入れないと Name の代入が捨てられるため順序を守る。
            return new MeshContext
            {
                MeshObject         = mo,
                Name               = name,
                UnityMesh          = unityMesh,
                IsVisible          = true,
                ParentModelContext = model,
            };
        }
    }
}
