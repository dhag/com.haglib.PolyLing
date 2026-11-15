// Runtime/Poly_Ling_Main/HierarchyIO/HierarchyMorphBuilder.cs
// ============================================================
// 形・モーフ → UnityEngine.Mesh のブレンドシェイプ
// ============================================================
//
// 【分離規約】規約は HierarchyBuilder.cs 冒頭のコメントを正典とする。
//
// ============================================================
// 載せるもの（PolyLing_モーフ整理計画.md 3.9・段 5）
// ============================================================
//
//   描画オブジェクトごとに、その描画オブジェクトで基本の形との差分がある形（ShapeOps.GetSparseOffsets）を
//   ブレンドシェイプにする。割り当ての鍵は MorphTargetKey。ブレンドシェイプ名は形の名前。
//   差分の無い形は載せない（どのオブジェクトにも差分が無い形は HierarchyBuilder.ReportUnattachedMorphs が警告する）。
//   差分は書き出しの写し（ShapeExportView。今の形が基本の形）で取るので、今の形に関係なく
//   基本の形をベースに出る。
//
// ============================================================
// 差分を「もう一度 ToUnityMesh して引く」理由
// ============================================================
//
//   ブレンドシェイプの deltaVertices は UnityMesh の頂点並びに一致していなければ
//   ならない。ところが HierarchyBuilder が使う MeshObject.ToUnityMesh(Matrix4x4) の
//   展開は面駆動で、名寄せキーが (頂点index, UVサブindex, 法線サブindex)、
//   さらに face.IsHidden を飛ばす（MeshBridgeDefault.cs:195-260）。
//   これは MeshExpansion の展開規則（頂点順・孤立除外・IsHidden 無視）とは別物なので、
//   MeshObject.BuildExpansionMap() を引いて写すと並びがずれる。
//
//   展開順序を自前で書き写すのは禁止されている（MeshExpansion.cs 冒頭の規則）。
//   そこで「同じ MeshObject の位置だけをずらしたクローン」を作り、
//   同じ ToUnityMesh を通してから引き算する。
//   展開は面・UVスロット・法線スロットだけで決まり、頂点位置に依存しないため、
//   クローンは基準メッシュと必ず同じ頂点数・同じ並びになる。
//   一致しなかった場合は載せずに警告する（黙って壊れた形状を出さない）。
//
//   引き算の基準も unityMesh.vertices ではなく shapeSource から作り直す。
//   ミラー側コンテキストの unityMesh は BuildMirrorSideMesh が全頂点へ
//   一律の平行移動を掛けた後の頂点を持つため、そのまま基準にすると
//   その平行移動ぶんが差分に混ざる。基準も同じ ToUnityMesh から取れば相殺される。
//
// ============================================================
// ミラー枝
// ============================================================
//
//   差分の元にする MeshObject は「その GameObject の形状を作った MeshObject」と
//   同一でなければならない。ミラー枝では実体側と面の巻き順が違うため、
//   実体側の MeshObject でクローンを作ると並びがずれる。
//   よって呼び出し側が shapeSource を渡す。
//     ・実体側                    … mc.MeshObject、差分はそのまま
//     ・許容モードの生成ミラー    … BuildMirroredMeshObject の結果、
//                                   差分は軸成分を反転（差分なので距離は使わない）
//     ・ミラー側コンテキスト      … mc.MeshObject（形状は既に反転済み）、差分はそのまま
//       （BuildMirrorSideMesh の平行移動は全頂点一律なので差分に影響しない）
//   差分そのものは常にモデルの baseIndex の描画オブジェクト（反転前）から取る。
//   形の控えはそのオブジェクトの頂点が持つ。
//
// ============================================================
// 法線（3.13。段 5 で決めた）
// ============================================================
//
//   ブレンドシェイプに法線の差分は持たせない。PolyLing の法線は全部の形で共有なので差分は常に 0。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.HierarchyIO
{
    /// <summary>ブレンドシェイプ1本ぶんの登録結果。</summary>
    public struct MorphShapeSlot
    {
        /// <summary>載せた相手（形の番号）。</summary>
        public MorphTargetKey Key;

        /// <summary>載せた差分（頂点番号順。反転前。MeshObject の頂点番号）。FBX はこれを書く。</summary>
        public List<(int VertexIndex, Vector3 Offset)> Offsets;

        /// <summary>ブレンドシェイプを載せたレンダラの Transform。</summary>
        public Transform Renderer;

        /// <summary>その Mesh 内でのブレンドシェイプ index。</summary>
        public int ShapeIndex;

        /// <summary>登録に使ったブレンドシェイプ名。</summary>
        public string ShapeName;
    }

    /// <summary>形・モーフ MeshContext から UnityMesh のブレンドシェイプを作る。</summary>
    public static class HierarchyMorphBuilder
    {
        /// <summary>ブレンドシェイプのフレーム重み。Unity の慣習にそろえて 100 固定。</summary>
        private const float FrameWeight = 100f;

        /// <summary>載せる候補 1 つ。</summary>
        private struct Target
        {
            public MorphTargetKey Key;
            public string Name;
            public List<(int VertexIndex, Vector3 Offset)> Offsets;
        }

        /// <summary>
        /// baseIndex のメッシュに載せる形を unityMesh へブレンドシェイプとして載せる。
        /// </summary>
        /// <param name="shapeSource">unityMesh を作った MeshObject（ミラー枝では鏡像化済みのもの）。</param>
        /// <param name="mirrorDelta">差分を軸反転するか（許容モードの生成ミラーのみ true）。</param>
        /// <param name="mirrorAxis">反転軸（1=X / 2=Y / 4=Z）。mirrorDelta が false なら未使用。</param>
        /// <returns>載せたブレンドシェイプの一覧。</returns>
        public static List<MorphShapeSlot> Apply(
            ModelContext model, int baseIndex,
            MeshObject shapeSource, Mesh unityMesh, Transform rendererTransform,
            bool mirrorDelta, int mirrorAxis,
            List<string> warnings)
        {
            var slots = new List<MorphShapeSlot>();
            if (model == null || shapeSource == null || unityMesh == null) return slots;

            var targets = CollectTargets(model, baseIndex);
            if (targets.Count == 0) return slots;

            // 引き算の基準。unityMesh そのものではなく shapeSource から作り直す（冒頭参照）。
            var baseVerts = BuildBaseVertices(shapeSource);
            if (baseVerts == null || baseVerts.Length == 0) return slots;

            if (baseVerts.Length != unityMesh.vertexCount)
            {
                warnings?.Add(
                    $"メッシュ \"{shapeSource.Name}\" の展開頂点数が一致しないため"
                    + $"ブレンドシェイプを作れませんでした"
                    + $"（Unity {unityMesh.vertexCount} / 再展開 {baseVerts.Length}）。");
                return slots;
            }

            var usedNames = new HashSet<string>();
            for (int i = 0; i < unityMesh.blendShapeCount; i++)
                usedNames.Add(unityMesh.GetBlendShapeName(i));

            foreach (var t in targets)
            {
                // 候補は差分のある形だけ（CollectTargets）。
                var delta = BuildDelta(shapeSource, baseVerts, t.Offsets, mirrorDelta, mirrorAxis, t.Name, warnings);
                if (delta == null) continue;

                string shapeName = HierarchyBuilder.MakeUniqueName(t.Name, usedNames);

                int shapeIndex = unityMesh.blendShapeCount;
                unityMesh.AddBlendShapeFrame(shapeName, FrameWeight, delta, null, null);

                slots.Add(new MorphShapeSlot
                {
                    Key        = t.Key,
                    Offsets    = t.Offsets,
                    Renderer   = rendererTransform,
                    ShapeIndex = shapeIndex,
                    ShapeName  = shapeName,
                });
            }

            return slots;
        }

        /// <summary>
        /// Unity メッシュを作らずに、載せる形の名前と並びだけを決める
        /// （HierarchyBuildOptions.CreateUnityMeshes = false。MeshObject から直接書く書き出し用）。
        /// 名前・並び・飛ばす条件は Apply と同じ：
        ///   ・形状に表示中の 3 頂点以上の面が無い（Apply では展開頂点 0）なら何も載せない。
        ///   ・差分があるのに 1 つも頂点番号の範囲に入らないものは載せない（Apply の BuildDelta が null）。
        /// Apply だけにある「展開頂点数の不一致」は Unity メッシュへ展開し直すことで起きるもので、ここには無い。
        /// ShapeIndex は並びの番号。
        /// </summary>
        public static List<MorphShapeSlot> Plan(
            ModelContext model, int baseIndex, MeshObject shapeSource, Transform rendererTransform)
        {
            var slots = new List<MorphShapeSlot>();
            if (model == null || shapeSource == null) return slots;

            var targets = CollectTargets(model, baseIndex);
            if (targets.Count == 0) return slots;

            if (!HasDrawableFace(shapeSource)) return slots;

            int vertexCount = shapeSource.Vertices?.Count ?? 0;
            var usedNames = new HashSet<string>();

            foreach (var t in targets)
            {
                if (t.Offsets != null && t.Offsets.Count > 0)
                {
                    bool any = false;
                    foreach (var (vertexIndex, _) in t.Offsets)
                        if (vertexIndex >= 0 && vertexIndex < vertexCount) { any = true; break; }
                    if (!any) continue;
                }

                slots.Add(new MorphShapeSlot
                {
                    Key        = t.Key,
                    Offsets    = t.Offsets,
                    Renderer   = rendererTransform,
                    ShapeIndex = slots.Count,
                    ShapeName  = HierarchyBuilder.MakeUniqueName(t.Name, usedNames),
                });
            }

            return slots;
        }

        /// <summary>
        /// baseIndex の描画オブジェクトに載せる候補。形の並びの順。
        /// </summary>
        private static List<Target> CollectTargets(ModelContext model, int baseIndex)
        {
            var list = new List<Target>();

            var baseCtx = model.GetMeshContext(baseIndex);
            var mo = baseCtx?.MeshObject;
            if (mo != null && mo.VertexCount > 0 && model.Shapes != null)
            {
                int current = model.CurrentShapeId;
                foreach (var shape in model.Shapes)
                {
                    // UV の形はブレンドシェイプに出さない（ShapeData.cs の冒頭注記）。
                    if (shape == null || shape.IsUV) continue;
                    var off = ShapeOps.GetSparseOffsets(mo, current, shape.Id);
                    if (off.Count == 0) continue;
                    list.Add(new Target
                    {
                        Key     = MorphTargetKey.Shape(shape.Id),
                        Name    = string.IsNullOrEmpty(shape.Name) ? $"Shape_{shape.Id}" : shape.Name,
                        Offsets = off,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// ToUnityMesh が面を積む条件（3 頂点以上・表示中。MeshBridgeDefault.ToUnityMesh）を満たす面があるか。
        /// </summary>
        private static bool HasDrawableFace(MeshObject mo)
        {
            if (mo?.Faces == null || mo.Vertices == null || mo.Vertices.Count == 0) return false;
            foreach (var f in mo.Faces)
                if (f != null && f.VertexCount >= 3 && !f.IsHidden) return true;
            return false;
        }

        /// <summary>
        /// 差分の基準になる展開頂点を作る。unityMesh を作ったのと同じ
        /// ToUnityMesh(Matrix4x4.identity) を通すことで並びを一致させる。
        /// </summary>
        private static Vector3[] BuildBaseVertices(MeshObject shapeSource)
        {
            Mesh baseMesh = null;
            try
            {
                baseMesh = shapeSource.ToUnityMesh(Matrix4x4.identity);
                return baseMesh != null ? baseMesh.vertices : null;
            }
            finally
            {
                DestroyMesh(baseMesh);
            }
        }

        private static void DestroyMesh(Mesh mesh)
        {
            if (mesh == null) return;
            if (Application.isPlaying) Object.Destroy(mesh);
            else                       Object.DestroyImmediate(mesh);
        }

        /// <summary>
        /// 疎差分から deltaVertices を作る。
        /// 展開後の並びは「位置をずらしたクローンを同じ ToUnityMesh へ通す」ことで得る。
        /// </summary>
        private static Vector3[] BuildDelta(
            MeshObject shapeSource, Vector3[] baseVerts,
            List<(int VertexIndex, Vector3 Offset)> offsets,
            bool mirrorDelta, int mirrorAxis,
            string morphName, List<string> warnings)
        {
            var clone = shapeSource.Clone();
            if (clone?.Vertices == null) return null;

            bool any = false;
            foreach (var (vertexIndex, offset) in offsets)
            {
                if (vertexIndex < 0 || vertexIndex >= clone.Vertices.Count) continue;

                // 差分の鏡像化は軸成分の符号反転だけでよい（距離は差分に効かない）。
                // 規則は MirrorBranchOps.MirrorNormal と同一。
                Vector3 d = mirrorDelta
                    ? MirrorBranchOps.MirrorNormal(mirrorAxis, offset)
                    : offset;

                clone.Vertices[vertexIndex].Position += d;
                any = true;
            }
            if (!any) return null;

            clone.InvalidatePositionCache();

            Mesh morphed = null;
            try
            {
                morphed = clone.ToUnityMesh(Matrix4x4.identity);
                if (morphed == null) return null;

                var morphVerts = morphed.vertices;
                if (morphVerts == null || morphVerts.Length != baseVerts.Length)
                {
                    warnings?.Add(
                        $"モーフ \"{morphName}\" の展開頂点数が基準メッシュと一致しないため"
                        + $"ブレンドシェイプを作れませんでした"
                        + $"（基準 {baseVerts.Length} / モーフ {morphVerts?.Length ?? 0}）。");
                    return null;
                }

                var delta = new Vector3[baseVerts.Length];
                for (int i = 0; i < delta.Length; i++)
                    delta[i] = morphVerts[i] - baseVerts[i];

                return delta;
            }
            finally
            {
                DestroyMesh(morphed);
            }
        }
    }
}
