// Runtime/Poly_Ling_Main/Core/Data/VertexIdTriple.cs
// ============================================================
// 頂点の識別子（頂点ID / 部品ID / サブID）の純POCOデータ契約
// ============================================================
//
// 【役割】
//   Vertex が持つ Id / PartsId / SubId をまとめて控えるための値。
//   選択部品辞書（PartsSelectionSet）がローカル索引と並べて保持する。
//
// 【なぜ要るか】
//   選択部品辞書は頂点をローカル索引で指す。頂点の挿入・削除で索引がずれると
//   辞書が別の頂点を指す。ずれたときに引き直せるよう、控えを持たせる。
//
// 【引き当てに使うのは Id だけ】
//   Id が一意なのは 1 つの MeshObject の中だけで、オブジェクトをまたいだ重複は
//   正常（MeshObject.cs「一意性の範囲」）。よって引き当ては必ず 1 つの
//   MeshObject の中で行う。
//   PartsId / SubId は同じ値を多数の頂点が共有するため引き当てには使えない。
//   保持と往復だけを行う（将来の拡張用）。
//
// 【未設定】
//   Id の未設定判定は MeshObject.IsUnsetId（0 と -1 の両方が未設定）。
//   PartsId / SubId は 0 が未設定。未設定でも控えること自体は許す
//   （頂点IDを持たないメッシュが PartsId だけ持つ場合があるため）。
//
// 【MQO の同名構造体との関係】
//   VertexIdHelper.MqoVertexIds は同じ 3 項目を持つが、あちらは MQO の
//   COL(PartsID, SubID, ID) 並びに縛られた入出力用。相互変換はしない。
//
// 【依存】
//   UnityEngine の型を使わない。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;

namespace Poly_Ling.Data
{
    /// <summary>頂点の識別子（頂点ID / 部品ID / サブID）。</summary>
    [Serializable]
    public struct VertexIdTriple : IEquatable<VertexIdTriple>
    {
        /// <summary>頂点ID（Vertex.Id）。引き当てに使う唯一の値。</summary>
        public int Id;

        /// <summary>部品ID（Vertex.PartsId）。0=未設定。控えるだけ。</summary>
        public int PartsId;

        /// <summary>サブID（Vertex.SubId）。0=未設定。控えるだけ。</summary>
        public int SubId;

        public VertexIdTriple(int id, int partsId, int subId)
        {
            Id      = id;
            PartsId = partsId;
            SubId   = subId;
        }

        /// <summary>引き当てに使える頂点IDを持つか。</summary>
        public bool HasVertexId => !MeshObject.IsUnsetId(Id);

        /// <summary>3値とも未設定か。</summary>
        public bool IsEmpty => MeshObject.IsUnsetId(Id) && PartsId == 0 && SubId == 0;

        public bool Equals(VertexIdTriple other)
            => Id == other.Id && PartsId == other.PartsId && SubId == other.SubId;

        public override bool Equals(object obj)
            => obj is VertexIdTriple other && Equals(other);

        public override int GetHashCode()
            => (Id * 397 ^ PartsId) * 397 ^ SubId;

        public override string ToString() => $"id={Id} parts={PartsId} sub={SubId}";
    }
}
