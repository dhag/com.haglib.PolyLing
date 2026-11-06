// DeformerMeshModifier.cs
// デフォーマ（IMeshDeformer）を処理列の種類として包む部品。位置だけの種類になる。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 6
//
// 【作り】
//   この抽象の基底が、作業軸・処理範囲・Prepare・位置の口を受け持つ。
//   種類ごとのクラス（DeformerMeshModifiers.cs）は PLMeshModifier 属性・PLParam の引数・
//   デフォーマの Params へ写す WriteParams だけを書く。
//   デフォーマごとに引数が違い、IDeformerParams は宣言（範囲・説明）を持たないので、
//   1 つの種類に引数でデフォーマを選ばせる形にはしない。
//   登録表は抽象型を飛ばし、継いだプロパティも拾う（MeshModifierRegistry.Inspect）。
//
// 【DeformApplier から外したもの】
//   モデル・選択（→ 処理範囲）・作業軸（→ 引数 axisOrigin・axisRotation）・
//   GPU のワールド座標とバインド表示（→ オブジェクトのローカル座標で計算。元メッシュは
//   スキンド化していない：段 4 の決定）・マグネット・Undo（→ 処理列のコマンド）。
//
// 【DeformContext】範囲内の頂点（全体なら全頂点）を作業軸ローカルへ写して作る。
//   Evaluate も位置の口も同じ計算。位置の口では渡された今の位置から作るので、ドラッグ中も追随する。
//
// 【包む漏れ】DeformerRegistry に登録されたデフォーマで、包む種類が無いものは
//   queryMeshModifierAudit の「種類の自己試験」が失敗として出す（格子は DeformerRegistry に未登録なので対象外。
//   格子の種類は LatticeMeshModifier としてこの基底を継ぐ。段 7b）。

using System;
using Poly_Ling.Data;
using Poly_Ling.Tools.Deformers;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    /// <summary>デフォーマを包んだ種類の共通の口（検査が包むデフォーマ名を引く）。</summary>
    public interface IDeformerMeshModifier
    {
        /// <summary>包んでいるデフォーマの Name（DeformerRegistry の検索キー）。</summary>
        string GetDeformerName();

        /// <summary>作業軸の原点（オブジェクトのローカル座標）。ビューのハンドルが読む（操作統合計画 段 8）。</summary>
        Vector3 AxisOrigin { get; }

        /// <summary>作業軸の向き（オイラー角・度。オブジェクトのローカル座標）。</summary>
        Vector3 AxisRotation { get; }
    }

    public abstract class DeformerMeshModifier<TDeformer> : IMeshModifier, IMeshModifierPositions, IDeformerMeshModifier
        where TDeformer : IMeshDeformer, new()
    {
        /// <summary>作業軸の原点の各成分の下限・上限（translate の offset と同じ）。</summary>
        public const float AxisOriginMin = -10f;
        public const float AxisOriginMax =  10f;

        [PLParam(Description = "作業軸の原点（オブジェクトのローカル座標）", Min = AxisOriginMin, Max = AxisOriginMax, Step = 0.01)]
        public Vector3 AxisOrigin { get; set; } = Vector3.zero;

        [PLParam(Description = "作業軸の向き（オイラー角・度。オブジェクトのローカル座標。+Y がライン方向）", Min = -180.0, Max = 180.0, Step = 1)]
        public Vector3 AxisRotation { get; set; } = Vector3.zero;

        /// <summary>
        /// 追加メニューの既定値に使う（操作統合計画 段 3）。作業軸の原点を入力の中心へ置く（Maya の変形ハンドルと同じ）。
        /// alignLine=true なら、入力が +Y 方向に厚みを持たないとき、ライン（+Y）をいちばん長い向きへ回す
        /// （Y に沿って変形する種類が平らなものに効かないため）。入力の外接箱の大きさを返す。
        /// </summary>
        protected Vector3 FitAxisToInput(MeshObject input, bool alignLine = true)
        {
            if (!MeshModifierAddDefaultOps.TryBounds(input, out var center, out var size)) return Vector3.zero;
            AxisOrigin = center;
            float longest = MeshModifierAddDefaultOps.Longest(size);
            if (alignLine && size.y <= longest * 1e-4f)
                AxisRotation = size.x >= size.z ? new Vector3(0f, 0f, -90f) : new Vector3(90f, 0f, 0f);
            return size;
        }

        /// <summary>引数をデフォーマの Params へ写す。</summary>
        protected abstract void WriteParams(TDeformer deformer);

        /// <summary>
        /// 引数をデフォーマへ写す。写せない引数（格子の点の索引の範囲外など）は false と理由を返す。
        /// 既定は WriteParams を呼んで true。検査の要る種類だけ上書きする（段 7b。LatticeMeshModifier）。
        /// </summary>
        protected virtual bool TryWriteParams(TDeformer deformer, out string error)
        {
            WriteParams(deformer);
            error = null;
            return true;
        }

        public string GetDeformerName() => new TDeformer().Name;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            if (input?.Source == null) { error = "入力のメッシュが無い"; return null; }

            var dst = input.Source.Clone();
            int n = dst.Vertices.Count;
            var src = new Vector3[n];
            for (int i = 0; i < n; i++) src[i] = dst.Vertices[i].Position;
            var outPos = (Vector3[])src.Clone();
            if (!EvaluatePositions(input, src, outPos, out error)) return null;
            for (int i = 0; i < n; i++) dst.Vertices[i].Position = outPos[i];
            dst.InvalidatePositionCache();
            return dst;
        }

        public bool EvaluatePositions(MeshModifierInput input, Vector3[] src, Vector3[] dst, out string error)
        {
            error = null;
            if (src == null || dst == null || src.Length != dst.Length) { error = "位置の配列の長さが合わない"; return false; }
            var scope = input?.Scope ?? ResolvedMeshModifierScope.CreateAll();

            var deformer = new TDeformer();
            if (!TryWriteParams(deformer, out error)) return false;

            var rot = Quaternion.Euler(AxisRotation);
            var inv = Quaternion.Inverse(rot);
            Vector3 origin = AxisOrigin;

            // 範囲内の頂点を作業軸ローカルへ写して DeformContext を作る（DeformApplier.BuildContext と同じ規則）
            var ctx = new DeformContext
            {
                LocalMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
                LocalMax = new Vector3(float.MinValue, float.MinValue, float.MinValue),
            };
            for (int i = 0; i < src.Length; i++)
            {
                if (!scope.ContainsVertex(i)) continue;
                Vector3 l = inv * (src[i] - origin);
                ctx.LocalMin = Vector3.Min(ctx.LocalMin, l);
                ctx.LocalMax = Vector3.Max(ctx.LocalMax, l);
                ctx.VertexCount++;
            }
            if (ctx.VertexCount == 0) { ctx.LocalMin = Vector3.zero; ctx.LocalMax = Vector3.zero; }
            ctx.SMin = ctx.LocalMin.y;
            ctx.SMax = ctx.LocalMax.y;

            try { deformer.Prepare(ctx); }
            catch (Exception ex) { error = ex.Message; return false; }

            for (int i = 0; i < src.Length; i++)
            {
                if (!scope.ContainsVertex(i)) { dst[i] = src[i]; continue; }
                Vector3 l = inv * (src[i] - origin);
                dst[i] = origin + rot * deformer.Evaluate(l);
            }
            return true;
        }
    }
}
