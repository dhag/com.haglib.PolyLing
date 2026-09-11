// MeshObject.Ids.cs
// MeshObject：ID 管理・ID による検索・位置配列キャッシュのメソッド。フィールドは MeshObject.cs。
// Runtime/Poly_Ling_Main/Core/Data/ に配置

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
    public partial class MeshObject
    {
        /// <summary>
        /// 新しい頂点IDを生成（GUID的なランダム生成）
        /// </summary>
        public int GenerateVertexId()
        {
            EnsureIdSetsInitialized();
            int id;
            int attempts = 0;
            do
            {
                // 1〜int.MaxValue-1 の範囲でランダム生成
                id = _idRandom.Next(1, int.MaxValue);
                attempts++;
                if (attempts > 1000)
                {
                    // フォールバック: 線形探索
                    id = FindNextAvailableId(_usedVertexIds);
                    break;
                }
            } while (id == 0 || _usedVertexIds.Contains(id));

            _usedVertexIds.Add(id);
            return id;
        }

        /// <summary>
        /// 新しい面IDを生成（GUID的なランダム生成）
        /// </summary>
        public int GenerateFaceId()
        {
            EnsureIdSetsInitialized();
            int id;
            int attempts = 0;
            do
            {
                id = _idRandom.Next(1, int.MaxValue);
                attempts++;
                if (attempts > 1000)
                {
                    id = FindNextAvailableId(_usedFaceIds);
                    break;
                }
            } while (id == 0 || _usedFaceIds.Contains(id));

            _usedFaceIds.Add(id);
            return id;
        }

        /// <summary>
        /// 「ID 未設定」の判定。
        ///
        /// 未設定の表現が経路によって 2 種類ある:
        ///   0  … Vertex/Face の既定値（新規生成・シリアライズ復元の既定）
        ///   -1 … MQO インポートの初期値（MQOImporter が特殊面から ID を拾えなかった場合）
        /// 片方だけを未設定扱いにすると、もう片方が「有効な ID」として辞書のキーに
        /// なり、同値の頂点が大量に潰し合う（先頭 1 個だけが勝つ）事故が起きる。
        /// 正の値だけを有効とみなすこと。
        /// </summary>
        public static bool IsUnsetId(int id) => id <= 0;

        /// <summary>
        /// 頂点IDを登録（外部からインポート時等に使用）
        /// </summary>
        public void RegisterVertexId(int id)
        {
            EnsureIdSetsInitialized();
            if (!IsUnsetId(id))
                _usedVertexIds.Add(id);
        }

        /// <summary>
        /// 面IDを登録（外部からインポート時等に使用）
        /// </summary>
        public void RegisterFaceId(int id)
        {
            EnsureIdSetsInitialized();
            if (!IsUnsetId(id))
                _usedFaceIds.Add(id);
        }

        /// <summary>
        /// 頂点IDを解放（削除時、再利用可能にする場合）
        /// </summary>
        public void ReleaseVertexId(int id)
        {
            EnsureIdSetsInitialized();
            _usedVertexIds.Remove(id);
        }

        /// <summary>
        /// 面IDを解放（削除時、再利用可能にする場合）
        /// </summary>
        public void ReleaseFaceId(int id)
        {
            EnsureIdSetsInitialized();
            _usedFaceIds.Remove(id);
        }

        /// <summary>
        /// 使用中IDセットを現在のVertex/Faceから再構築
        /// </summary>
        public void RebuildIdSets()
        {
            _usedVertexIds = new HashSet<int>();
            _usedFaceIds = new HashSet<int>();

            foreach (var v in Vertices)
            {
                if (!IsUnsetId(v.Id))
                    _usedVertexIds.Add(v.Id);
            }
            foreach (var f in Faces)
            {
                if (!IsUnsetId(f.Id))
                    _usedFaceIds.Add(f.Id);
            }
        }

        /// <summary>
        /// IDが未設定の頂点・面にIDを割り当て。
        /// 0 と -1 の両方を未設定として扱う（IsUnsetId 参照）。
        /// </summary>
        public void AssignMissingIds()
        {
            EnsureIdSetsInitialized();
            foreach (var v in Vertices)
            {
                if (IsUnsetId(v.Id))
                {
                    v.Id = GenerateVertexId();
                }
                else
                {
                    RegisterVertexId(v.Id);
                }
            }
            foreach (var f in Faces)
            {
                if (IsUnsetId(f.Id))
                {
                    f.Id = GenerateFaceId();
                }
                else
                {
                    RegisterFaceId(f.Id);
                }
            }
        }

        private void EnsureIdSetsInitialized()
        {
            if (_usedVertexIds == null)
                _usedVertexIds = new HashSet<int>();
            if (_usedFaceIds == null)
                _usedFaceIds = new HashSet<int>();
        }

        private static int FindNextAvailableId(HashSet<int> usedIds)
        {
            for (int i = 1; i < int.MaxValue; i++)
            {
                if (!usedIds.Contains(i))
                    return i;
            }
            return 1; // 極端な場合のフォールバック
        }

        // ================================================================
        // IDによる検索
        // ================================================================

        /// <summary>
        /// 頂点IDから頂点インデックスを取得（見つからない場合-1）
        /// </summary>
        public int FindVertexIndexById(int id)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i].Id == id)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 面IDから面インデックスを取得（見つからない場合-1）
        /// </summary>
        public int FindFaceIndexById(int id)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].Id == id)
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// 頂点IDから頂点を取得（見つからない場合null）
        /// </summary>
        public Vertex FindVertexById(int id)
        {
            int idx = FindVertexIndexById(id);
            return idx >= 0 ? Vertices[idx] : null;
        }

        /// <summary>
        /// 面IDから面を取得（見つからない場合null）
        /// </summary>
        public Face FindFaceById(int id)
        {
            int idx = FindFaceIndexById(id);
            return idx >= 0 ? Faces[idx] : null;
        }

        /// <summary>
        /// Position配列からVertices[i].Positionに書き戻し
        /// Undo復元、一括位置設定時に使用
        /// </summary>
        public void SetPositions(Vector3[] positions)
        {
            int count = System.Math.Min(positions.Length, Vertices.Count);
            for (int i = 0; i < count; i++)
                Vertices[i].Position = positions[i];
            // キャッシュも同時更新（再構築を避ける）
            if (positions.Length == Vertices.Count)
            {
                _positionCache = (Vector3[])positions.Clone();
                _positionCacheDirty = false;
            }
            else
            {
                _positionCacheDirty = true;
            }
        }

        /// <summary>
        /// Positionキャッシュを無効化
        /// Vertices[i].Positionを直接変更した後に呼ぶこと
        /// </summary>
        public void InvalidatePositionCache()
        {
            _positionCacheDirty = true;
        }

        /// <summary>
        /// Positionキャッシュを再構築
        /// </summary>
        private void RebuildPositionCache()
        {
            int count = Vertices.Count;
            if (_positionCache == null || _positionCache.Length != count)
                _positionCache = new Vector3[count];
            for (int i = 0; i < count; i++)
                _positionCache[i] = Vertices[i].Position;
            _positionCacheDirty = false;
        }
    }
}
