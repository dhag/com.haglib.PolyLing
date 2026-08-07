// Runtime/Poly_Ling_Main/Tools/Deformers/IMeshDeformer.cs
// 頂点変形（デフォーマ）の共通インターフェース。
//
// 【なぜ行列ではないか】
//   回転は全頂点で同じ R を使うアフィン変換 p' = R(p-c) + c だが、
//   曲げは R = R(θ(s)) と R 自体が頂点位置 s の関数になる。
//   さらに円弧写像には sin / cos が入るため、線形変換でも多項式変換でもない。
//   したがって共通の原始的な単位は「行列 M」ではなく「写像 F: p → p'」であり、
//   回転は F がアフィンである特殊ケースにすぎない。
//
// 【座標系の約束（全デフォーマ共通・厳守）】
//   Evaluate は「作業軸ローカル空間」で受け取り、同じ空間で返す。
//     ・+Y  = ライン方向 u。曲げの位置パラメータは s = pLocal.y
//     ・+X  = たわみの既定方向（BendPlaneAngleDeg = 0 のとき）
//     ・原点 = WorkAxisContext.Origin
//   ワールド／メッシュローカルとの往復は DeformApplier が行う。
//   デフォーマ側は軸の向きも対象メッシュも一切知らなくてよい。
//
// Runtime/Poly_Ling_Main/Tools/Deformers/ に配置

using UnityEngine;

namespace Poly_Ling.Tools.Deformers
{
    // ================================================================
    // パラメータ
    // ================================================================

    /// <summary>
    /// デフォーマのパラメータ。契約は IToolSettings と同形にそろえてある
    /// （Clone / IsDifferentFrom / CopyFrom）。将来 Undo やプリセットへ
    /// つなぐときに IToolSettings と同じ扱いができる。
    /// </summary>
    public interface IDeformerParams
    {
        /// <summary>複製を作成（スナップショット用）。</summary>
        IDeformerParams Clone();

        /// <summary>他と異なるか。</summary>
        bool IsDifferentFrom(IDeformerParams other);

        /// <summary>他からコピー。</summary>
        void CopyFrom(IDeformerParams other);

        /// <summary>既定値へ戻す。</summary>
        void Reset();
    }

    /// <summary>IDeformerParams 実装用の基底（型チェック補助のみ）。</summary>
    public abstract class DeformerParamsBase : IDeformerParams
    {
        public abstract IDeformerParams Clone();
        public abstract bool IsDifferentFrom(IDeformerParams other);
        public abstract void CopyFrom(IDeformerParams other);
        public abstract void Reset();

        protected bool IsSameType<T>(IDeformerParams other, out T typed) where T : class, IDeformerParams
        {
            typed = other as T;
            return typed != null;
        }
    }

    // ================================================================
    // 事前計算コンテキスト
    // ================================================================

    /// <summary>
    /// Prepare に渡す情報。対象頂点を作業軸ローカルへ変換したあとの
    /// 統計値を持つ。デフォーマはここから s の範囲などを読む。
    ///
    /// 座標はすべて作業軸ローカル空間。
    /// </summary>
    public struct DeformContext
    {
        /// <summary>対象頂点の s（= pLocal.y）の最小値。</summary>
        public float SMin;

        /// <summary>対象頂点の s（= pLocal.y）の最大値。</summary>
        public float SMax;

        /// <summary>対象頂点数。0 のとき SMin / SMax / LocalMin / LocalMax は未定義。</summary>
        public int VertexCount;

        /// <summary>
        /// 対象頂点の作業軸ローカル AABB の最小側。格子変形の「選択フィット」が
        /// 基準格子の範囲を決めるのに使う。SMin は LocalMin.y と同じ値になる。
        /// </summary>
        public Vector3 LocalMin;

        /// <summary>対象頂点の作業軸ローカル AABB の最大側。</summary>
        public Vector3 LocalMax;

        /// <summary>s の範囲。0 以下になり得るので割る前に必ず確認すること。</summary>
        public float SRange => SMax - SMin;

        /// <summary>有効な範囲を持つか（ゼロ割り回避用）。</summary>
        public bool HasRange => VertexCount > 0 && SRange > 1e-6f;
    }

    // ================================================================
    // デフォーマ
    // ================================================================

    /// <summary>
    /// 頂点変形の写像。作業軸ローカル空間で完結する純粋な関数として実装する。
    /// </summary>
    public interface IMeshDeformer
    {
        /// <summary>内部識別子。DeformerRegistry の検索キー。</summary>
        string Name { get; }

        /// <summary>表示名（英語。ローカライズは呼び出し側）。</summary>
        string DisplayName { get; }

        /// <summary>パラメータ。持たないデフォーマは null を返してよい。</summary>
        IDeformerParams Params { get; }

        /// <summary>
        /// 変形の前に一度だけ呼ぶ。s の範囲から曲率を決める等の事前計算を行う。
        /// Evaluate の前に必ず呼ばれることを実装は前提にしてよい。
        /// </summary>
        void Prepare(DeformContext ctx);

        /// <summary>
        /// 作業軸ローカル座標を受け取り、変形後の作業軸ローカル座標を返す。
        /// 副作用を持たせないこと（同じ入力に同じ出力を返すこと）。
        /// </summary>
        Vector3 Evaluate(Vector3 pLocal);

        /// <summary>
        /// 変形がアフィンで表せる場合に true を返し、その行列を出力する。
        /// 部分適用（マグネット等）で重み補間するとき、
        /// true なら回転成分を Slerp できる。false のときは呼び出し側が
        /// 位置を Lerp する近似になる。
        /// </summary>
        bool TryGetAffine(out Matrix4x4 matrix);
    }
}
