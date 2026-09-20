// LineGroup.cs
// 線分群 1 本分（開始点から終了点までの順序付き頂点列）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何を表すか】
//   Order が開始点 → 終了点の順に並んだ頂点索引。隣り合う 2 要素が 1 本の線分に当たる。
//   MeshObject.LineGroups が本型のリストを持ち、1 メッシュが複数の線分群を保持する。
//   2 頂点 Face（補助線）とは独立した入れ物で、Face の有無とは同期しない。
//
// 【索引と控えの二本立て】
//   Order は頂点索引なので、頂点を消して索引が詰まると指す先がずれる。
//   MeshObject.RemoveVertices / RemoveVerticesWithMap が旧索引→新索引の表で
//   付け替える（MeshObject.Removal.cs）。それを通らない経路に備えて、
//   OrderVertexIds に Vertex.Id の控えを Order と同順・同数で持つ
//   （PartsSelectionSet.VertexIds と同じ役割）。
//   控えが 0（未設定）の要素は引き直しの対象外。
//
// 【親】
//   ParentVertex は開始点の親となる同一メッシュ内の頂点索引（-1 = 親なし）。
//   別の線分群の途中の点を指すことで、線分群の集まりが枝分かれの木になる。
//   ParentVertexId はその頂点 ID の控え（0 = 控え無し）。
//
// 【ハンドル】
//   PointHandles は Order と同順・同数の点ごとの方向ハンドルと拘束（LineHandle.cs）。
//   空なら折れ線。LengthGroups はこの群の中だけで共有する長さ。
//   Order の付け替え（LineGroupOps）では OrderVertexIds と同じ規則で一緒に付け替える。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 線分群 1 本分。開始点から終了点までの順序付き頂点列。
    /// </summary>
    [Serializable]
    public class LineGroup
    {
        /// <summary>線分群の名前（UI 表示・識別用）。</summary>
        public string Name = "LineGroup";

        /// <summary>開始点 → 終了点の順に並んだ頂点索引。</summary>
        public List<int> Order = new List<int>();

        /// <summary>
        /// Order と同順・同数の頂点 ID 控え（0 = 控え無し）。
        /// 索引が詰められたあとの引き直し用。
        /// </summary>
        public List<int> OrderVertexIds = new List<int>();

        /// <summary>閉環なら true。開いた鎖なら false。</summary>
        public bool Closed = false;

        /// <summary>開始点の親となる同一メッシュ内の頂点索引（-1 = 親なし）。</summary>
        public int ParentVertex = -1;

        /// <summary>ParentVertex の頂点 ID 控え（0 = 控え無し）。</summary>
        public int ParentVertexId = 0;

        /// <summary>
        /// Order と同順・同数の点ごとのハンドルと拘束（LineHandle.cs）。
        /// 空なら折れ線（ハンドル無し）。空でなければ Order と同数に保つ。
        /// </summary>
        public List<LinePointHandle> PointHandles = new List<LinePointHandle>();

        /// <summary>長さの組（この群の中だけで共有する長さ）。</summary>
        public List<LineLengthGroup> LengthGroups = new List<LineLengthGroup>();

        /// <summary>ハンドルを持つか（PointHandles が Order と同数）。</summary>
        public bool HasHandles => PointHandles != null && Order != null
                                  && PointHandles.Count > 0 && PointHandles.Count == Order.Count;

        /// <summary>構成点の数。</summary>
        public int Count => Order?.Count ?? 0;

        /// <summary>開始点の頂点索引（空なら -1）。</summary>
        public int StartVertex => (Order != null && Order.Count > 0) ? Order[0] : -1;

        /// <summary>終了点の頂点索引（空なら -1）。</summary>
        public int EndVertex => (Order != null && Order.Count > 0) ? Order[Order.Count - 1] : -1;

        public LineGroup() { }

        public LineGroup(string name)
        {
            Name = name;
        }

        /// <summary>ディープコピー。</summary>
        public LineGroup Clone()
        {
            var copy = new LineGroup(Name)
            {
                Closed         = this.Closed,
                ParentVertex   = this.ParentVertex,
                ParentVertexId = this.ParentVertexId,
            };
            copy.Order          = (Order          != null) ? new List<int>(Order)          : new List<int>();
            copy.OrderVertexIds = (OrderVertexIds != null) ? new List<int>(OrderVertexIds) : new List<int>();
            copy.PointHandles   = new List<LinePointHandle>();
            if (PointHandles != null)
                foreach (var h in PointHandles) copy.PointHandles.Add(h?.Clone() ?? LinePointHandle.CreateDefault());
            copy.LengthGroups   = new List<LineLengthGroup>();
            if (LengthGroups != null)
                foreach (var lg in LengthGroups) if (lg != null) copy.LengthGroups.Add(lg.Clone());
            return copy;
        }
    }
}
