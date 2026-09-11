// MeshObject.Hierarchy.cs
// MeshObject：階層・トランスフォームと付帯データ（IK／剛体／JOINT）のメソッド。フィールドは MeshObject.cs。
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
        /// ボーンウェイトを持つ頂点が 1 つ以上あるか。O(頂点数)。
        ///
        /// 【種別判定には使わないこと】
        ///   種別は SkinKind（IsSkinnedKind）が答える。こちらは実データの検査用で、
        ///   ミラー対応表の構築可否のように「実際にウェイトが入っているか」を
        ///   知る必要がある場所だけが呼ぶ。プロパティではなくメソッドにしてあるのは、
        ///   走査コストを名前で示すため。
        /// </summary>
        public bool AnyVertexHasBoneWeight()
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                var v = Vertices[i];
                if (v != null && v.HasBoneWeight) return true;
            }
            return false;
        }

        /// <summary>
        /// 実頂点のウェイト有無から SkinKind を求め直す。
        ///
        /// 【一方向】
        ///   ウェイトを持つ頂点があれば Skinned にする。
        ///   0 個でも MeshFilter へは戻さない。戻すのは明示操作だけ（SetSkinKind /
        ///   ClearAllBoneWeights）。頂点削除やトポロジ編集の途中経過で種別が
        ///   勝手に切り替わると、描画行列が入れ替わって形状が飛ぶ。
        /// </summary>
        /// <returns>呼び出しによって SkinKind が変化したら true。</returns>
        public bool RecomputeSkinKind()
        {
            if (SkinKind == SkinKind.Skinned) return false;
            if (!AnyVertexHasBoneWeight())    return false;
            SkinKind = SkinKind.Skinned;
            return true;
        }

        /// <summary>
        /// 種別を明示的に設定する。MeshFilter へ戻す唯一の入口。
        /// ウェイトデータ自体は変更しない（破棄は ClearAllBoneWeights）。
        /// </summary>
        public void SetSkinKind(SkinKind kind)
        {
            SkinKind = kind;
        }

        // === 頂点操作 ===

        /// <summary>
        /// 頂点を追加（ID自動割り当て）
        /// </summary>
        /// <returns>追加された頂点のインデックス</returns>
        public int AddVertex(Vector3 position)
        {
            var vertex = new Vertex(position);
            vertex.Id = GenerateVertexId();
            Vertices.Add(vertex);
            _positionCacheDirty = true;
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV付き、ID自動割り当て）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv)
        {
            var vertex = new Vertex(position, uv);
            vertex.Id = GenerateVertexId();
            Vertices.Add(vertex);
            _positionCacheDirty = true;
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV/法線付き、ID自動割り当て）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            var vertex = new Vertex(position, uv, normal);
            vertex.Id = GenerateVertexId();
            Vertices.Add(vertex);
            _positionCacheDirty = true;
            return Vertices.Count - 1;
        }

        /// <summary>
        /// Vertexオブジェクトを追加（IDが未設定なら自動割り当て）
        /// </summary>
        public int AddVertex(Vertex vertex)
        {
            if (IsUnsetId(vertex.Id))
            {
                vertex.Id = GenerateVertexId();
            }
            else
            {
                RegisterVertexId(vertex.Id);
            }
            Vertices.Add(vertex);
            _positionCacheDirty = true;
            return Vertices.Count - 1;
        }

        /// <summary>
        /// Vertexオブジェクトを追加（ID管理なし、後方互換用）
        /// </summary>
        public int AddVertexRaw(Vertex vertex)
        {
            Vertices.Add(vertex);
            _positionCacheDirty = true;
            return Vertices.Count - 1;
        }

        // === 面操作 ===

        /// <summary>
        /// 三角形を追加（ID自動割り当て）
        /// </summary>
        public int AddTriangle(int v0, int v1, int v2, int materialIndex = 0)
        {
            var face = new Face(v0, v1, v2, materialIndex);
            face.Id = GenerateFaceId();
            Faces.Add(face);
            return Faces.Count - 1;
        }

        /// <summary>
        /// 四角形を追加（ID自動割り当て）
        /// </summary>
        public int AddQuad(int v0, int v1, int v2, int v3, int materialIndex = 0)
        {
            var face = new Face(v0, v1, v2, v3, materialIndex);
            face.Id = GenerateFaceId();
            Faces.Add(face);
            return Faces.Count - 1;
        }

        /// <summary>
        /// Faceオブジェクトを追加（IDが未設定なら自動割り当て）
        /// </summary>
        public int AddFace(Face face)
        {
            if (IsUnsetId(face.Id))
            {
                face.Id = GenerateFaceId();
            }
            else
            {
                RegisterFaceId(face.Id);
            }
            Faces.Add(face);
            return Faces.Count - 1;
        }

        /// <summary>
        /// Faceオブジェクトを追加（ID管理なし、後方互換用）
        /// </summary>
        public int AddFaceRaw(Face face)
        {
            Faces.Add(face);
            return Faces.Count - 1;
        }

        // === Unity Mesh 変換 ===

        /// <summary>
        /// Unity Meshに変換（サブメッシュ対応）
        /// </summary>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMesh(int materialCount = -1)
        {
            // 実装は MeshBridgeDefault に集約（生Unity Mesh API と頂点展開アルゴリズムを一元管理）。
            return PLMeshBridge.I.ToUnityMesh(this, materialCount);
        }

        /// <summary>
        /// Unity Meshに変換（座標変換付き、SkinnedMesh用）
        /// </summary>
        /// <param name="transform">頂点に適用する変換行列</param>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMesh(Matrix4x4 transform, int materialCount = -1)
        {
            return PLMeshBridge.I.ToUnityMesh(this, transform, materialCount);
        }
    }
}
