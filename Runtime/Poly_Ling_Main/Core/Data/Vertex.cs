// Vertex.cs
// 頂点クラス（MeshObject.cs から分離）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（MeshObject.cs と同じ名前空間。MeshObject.cs から分割）

using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{
    // ============================================================
    // Vertex クラス
    // ============================================================

    /// <summary>
    /// 頂点データ
    /// 位置と、複数のUV/法線を保持（シーム・ハードエッジ対応）
    /// </summary>
    [Serializable]
    public class Vertex
    {
        /// <summary>
        /// 頂点ID（トポロジー追跡・外部連携・モーフ用）
        ///
        /// 【一意性の範囲】
        ///   一意が保証されるのは 1 つの MeshObject の中だけ。GenerateVertexId が
        ///   参照する使用中IDセット（_usedVertexIds）はインスタンスごとに持つため、
        ///   モデル全体では重複しうるし、重複してよい。
        ///   首と胴のつなぎ目のようにオブジェクトをまたいで同じ頂点を指す用途や、
        ///   モデルをまたいだモーフの指標として意図的に共有する。
        ///   「モデル内で重複している」ことを異常として扱わないこと。
        /// </summary>
        public int Id = 0;

        /// <summary>
        /// 部品ID。1 つのオブジェクトの中に複数の部品があるとき、
        /// どの部品に属するかを表すグループ番号。
        ///
        /// 0 = 未設定。一意性は保証しない（同じ値を多数の頂点が共有する）。
        /// 自動採番はしない。値の意味付けは利用側の責務。
        /// </summary>
        public int PartsId = 0;

        /// <summary>
        /// サブID。部品ローカルなインデックスを表すグループ番号。
        ///
        /// 0 = 未設定。一意性は保証しない。自動採番はしない。
        /// </summary>
        public int SubId = 0;

        /// <summary>頂点位置</summary>
        public Vector3 Position;

        /// <summary>UV座標リスト（面から UVIndices で参照）</summary>
        public List<Vector2> UVs = new List<Vector2>();

        /// <summary>法線リスト（面から NormalIndices で参照）</summary>
        public List<Vector3> Normals = new List<Vector3>();

        /// <summary>頂点フラグ</summary>
        public VertexFlags Flags = VertexFlags.None;

        /// <summary>
        /// ボーンウェイト（スキニング用）
        /// boneIndex = _meshContextList のインデックス
        /// null = スキニングなし
        /// </summary>
        public BoneWeight? BoneWeight = null;

        /// <summary>
        /// ミラー側ボーンウェイト（ミラーオブジェクト用）
        /// ミラー展開時に使用される
        /// null = ミラーウェイトなし（実体側と同じか、ミラーなし）
        /// </summary>
        public BoneWeight? MirrorBoneWeight = null;

        /// <summary>
        /// オプション位置リスト（制御点位置など）。
        /// 用途は未定。null = 保持なし（頂点数が多いため既定は null）。
        /// 座標系は Position と同じくローカル空間。
        /// </summary>
        public List<Vector3> ControlPoints = null;

        /// <summary>スキニングデータを持つか</summary>
        public bool HasBoneWeight => BoneWeight.HasValue;

        /// <summary>ミラー側スキニングデータを持つか</summary>
        public bool HasMirrorBoneWeight => MirrorBoneWeight.HasValue;

        /// <summary>オプション位置リストを1件以上持つか。</summary>
        public bool HasControlPoints => ControlPoints != null && ControlPoints.Count > 0;

        /// <summary>
        /// オプション位置リストを確保して返す。null なら生成する。
        /// 読み取りだけの用途では呼ばないこと（空リストが残る）。
        /// </summary>
        public List<Vector3> EnsureControlPoints()
        {
            if (ControlPoints == null) ControlPoints = new List<Vector3>();
            return ControlPoints;
        }

        // === コンストラクタ ===

        public Vertex()
        {
            Position = Vector3.zero;
        }

        public Vertex(Vector3 position)
        {
            Position = position;
        }

        public Vertex(Vector3 position, Vector2 uv)
        {
            Position = position;
            UVs.Add(uv);
        }

        public Vertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Position = position;
            UVs.Add(uv);
            Normals.Add(normal);
        }

        /// <summary>
        /// ID指定付きコンストラクタ
        /// </summary>
        public Vertex(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        // === フラグ操作 ===

        /// <summary>フラグが設定されているか</summary>
        public bool HasFlag(VertexFlags flag) => (Flags & flag) != 0;

        /// <summary>フラグを設定</summary>
        public void SetFlag(VertexFlags flag) => Flags |= flag;

        /// <summary>フラグをクリア</summary>
        public void ClearFlag(VertexFlags flag) => Flags &= ~flag;

        /// <summary>フラグをトグル</summary>
        public void ToggleFlag(VertexFlags flag) => Flags ^= flag;

        /// <summary>ミラー平面上か</summary>
        public bool IsOnMirrorPlane => HasFlag(VertexFlags.OnMirrorPlane);

        /// <summary>ミラー生成された頂点か</summary>
        public bool IsMirrorGenerated => HasFlag(VertexFlags.MirrorGenerated);

        /// <summary>ロックされているか</summary>
        public bool IsLocked => HasFlag(VertexFlags.Locked);

        /// <summary>補助点か</summary>
        public bool IsAuxiliary => HasFlag(VertexFlags.Auxiliary);

        // === ユーティリティ ===

        /// <summary>
        /// UVを追加し、インデックスを返す
        /// </summary>
        public int AddUV(Vector2 uv)
        {
            UVs.Add(uv);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線を追加し、インデックスを返す
        /// </summary>
        public int AddNormal(Vector3 normal)
        {
            Normals.Add(normal);
            return Normals.Count - 1;
        }

        /// <summary>
        /// 同一UVが既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddUV(Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < UVs.Count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance)
                    return i;
            }
            return AddUV(uv);
        }

        /// <summary>
        /// 同一法線が既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddNormal(Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < Normals.Count; i++)
            {
                if (Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }
            return AddNormal(normal);
        }

        /// <summary>
        /// UVと法線を1組として検索し、無ければ両方に追加して共通の添字を返す。
        ///
        /// 【不変条件】UVs.Count == Normals.Count、および面の
        /// UVIndices[j] == NormalIndices[j] を保つための唯一の追加口。
        /// UV と法線を別々に追加すると両者の添字がずれ、展開index空間
        /// （BuildExpansionMap / GPU展開バッファ / PMXエクスポート）と食い違う。
        /// </summary>
        public int GetOrAddUVNormal(Vector2 uv, Vector3 normal, float tolerance = 0.0001f)
        {
            EnsureNormalSlots();

            int count = Mathf.Min(UVs.Count, Normals.Count);
            for (int i = 0; i < count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance &&
                    Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }

            UVs.Add(uv);
            Normals.Add(normal);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線スロット数を UV スロット数へ揃える。
        /// 不足分は先頭法線（無ければ Vector3.up）で埋め、超過分は切り捨てる。
        /// UVスロットが無い頂点は判断材料が無いため何もしない。
        /// </summary>
        public void EnsureNormalSlots()
        {
            if (UVs.Count == 0) return;

            Vector3 fill = Normals.Count > 0 ? Normals[0] : Vector3.up;
            while (Normals.Count < UVs.Count) Normals.Add(fill);
            while (Normals.Count > UVs.Count) Normals.RemoveAt(Normals.Count - 1);
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public Vertex Clone()
        {
            var clone = new Vertex(this.Position);
            clone.Id = this.Id;
            clone.PartsId = this.PartsId;
            clone.SubId = this.SubId;
            clone.UVs = new List<Vector2>(this.UVs);
            clone.Normals = new List<Vector3>(this.Normals);
            clone.Flags = this.Flags;
            clone.BoneWeight = this.BoneWeight;
            clone.MirrorBoneWeight = this.MirrorBoneWeight;
            // オプション位置リストは null なら null のまま複製する
            clone.ControlPoints = this.ControlPoints != null
                ? new List<Vector3>(this.ControlPoints)
                : null;
            return clone;
        }

        /// <summary>
        /// ディープコピー（新しいIDを割り当て）
        /// </summary>
        public Vertex CloneWithNewId(int newId)
        {
            var clone = Clone();
            clone.Id = newId;
            return clone;
        }
    }
}
