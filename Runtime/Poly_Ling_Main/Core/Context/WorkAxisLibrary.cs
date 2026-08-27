// WorkAxisLibrary.cs
// 名前付きの作業軸を溜めておく辞書。
//
// 「今の作業軸をこの名前で登録」「登録済みを呼び出す」を成立させるための入れ物で、
// 保持するのは WorkAxisContext の値そのもの（Origin / Rotation / Length）だけ。
// WorkAxisContext の参照は持たない。呼び出しは値のコピーになる。
//
// 【永続化】
//   プロジェクトファイルには入らない。セッション内の保持のみで、
//   残したいときは CSV へ書き出す（WorkAxisLibraryCsvIO）。
//
// 【名前】
//   前後の空白を落として一意キーにする。同名の登録は上書き。
//   並び順は登録順を保つ（Keys の見た目が毎回変わると選びにくいため）。
//
// Runtime/Poly_Ling_Main/Core/Context/ に配置

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Context
{
    /// <summary>辞書に入る1件ぶんの作業軸。</summary>
    public struct WorkAxisEntry
    {
        public Vector3    Origin;
        public Quaternion Rotation;
        public float      Length;

        public static WorkAxisEntry FromContext(WorkAxisContext wa)
            => new WorkAxisEntry
            {
                Origin   = wa.Origin,
                Rotation = wa.Rotation,
                Length   = wa.Length,
            };

        /// <summary>作業軸へ値を書き戻す。表示フラグ（IsVisible）は変えない。</summary>
        public void ApplyTo(WorkAxisContext wa)
        {
            if (wa == null) return;
            wa.Origin   = Origin;
            wa.Rotation = Rotation;
            wa.Length   = Length;
        }
    }

    /// <summary>名前付き作業軸の辞書。</summary>
    public class WorkAxisLibrary
    {
        // 登録順を保つため、キー列は別に持つ。
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, WorkAxisEntry> _map =
            new Dictionary<string, WorkAxisEntry>();

        public int Count => _order.Count;

        /// <summary>登録順のキー一覧。</summary>
        public IReadOnlyList<string> Names => _order;

        /// <summary>名前を正規化する。前後の空白を落とすだけ。</summary>
        public static string Normalize(string name)
            => string.IsNullOrEmpty(name) ? "" : name.Trim();

        /// <summary>登録または上書き。名前が空なら何もせず false。</summary>
        public bool Set(string name, WorkAxisEntry entry)
        {
            string key = Normalize(name);
            if (key.Length == 0) return false;

            if (!_map.ContainsKey(key)) _order.Add(key);
            _map[key] = entry;
            return true;
        }

        public bool TryGet(string name, out WorkAxisEntry entry)
            => _map.TryGetValue(Normalize(name), out entry);

        public bool Contains(string name) => _map.ContainsKey(Normalize(name));

        public bool Remove(string name)
        {
            string key = Normalize(name);
            if (!_map.Remove(key)) return false;
            _order.Remove(key);
            return true;
        }

        public void Clear()
        {
            _order.Clear();
            _map.Clear();
        }
    }
}
