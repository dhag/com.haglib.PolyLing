// StlImporter.cs
// StlDocument → MeshContext。
// Runtime/Poly_Ling_Main/STL/Import/ に配置
//
// 【STL と PolyLing の対応】
//   solid 1 個を MeshContext 1 個にする（バイナリはファイル全体で 1 個）。
//   STL は三角形ごとに頂点を持つので、WeldVertices が ON のときは
//   ファイル上の座標値（変換前の float 3 つ）が完全に一致する頂点を共有する。
//   共有した結果、同じ頂点を 2 回以上指す三角形は面として成り立たないので捨てる。
//
// 【法線】
//   facet の法線は使わず、スムージング角から作る（OBJ で vn が無いときと同じ
//   NormalSmoothingOps 経路）。UV は無いので全コーナー 0 を渡す。
//
// 【材質】
//   STL は材質を持たない。マテリアル参照は作らず、面は材質 0 を指す。
//
// 【ID】
//   頂点 ID・面 ID は付けない（-1 のまま。AssignMissingIds は呼ばない）。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.STL
{
    // ================================================================
    // 結果
    // ================================================================

    public class StlImportResult
    {
        public bool   Success;
        public string ErrorMessage;

        /// <summary>読み込んだメッシュ。</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>元ドキュメント。</summary>
        public StlDocument Document;

        public bool IsBinary;
        public int  TotalVertices;
        public int  TotalFaces;

        /// <summary>頂点の共有で縮退して捨てた三角形の数。</summary>
        public int  DroppedDegenerateFaces;
    }

    // ================================================================
    // インポータ
    // ================================================================

    public static class StlImporter
    {
        public static StlImportResult ImportFile(string filePath, StlImportSettings settings = null)
        {
            var result = new StlImportResult();

            if (string.IsNullOrEmpty(filePath))
            {
                result.ErrorMessage = "ファイルパスが空です";
                return result;
            }
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = $"ファイルが見つかりません: {filePath}";
                return result;
            }

            StlDocument document;
            try
            {
                document = StlParser.ParseFile(filePath);
            }
            catch (Exception e)
            {
                result.ErrorMessage = e.Message;
                Debug.LogError($"[StlImporter] パースに失敗: {e}");
                return result;
            }

            return Import(document, settings);
        }

        public static StlImportResult Import(StlDocument document, StlImportSettings settings = null)
        {
            var result = new StlImportResult();
            settings = settings ?? StlImportSettings.CreateDefault();

            if (document == null)
            {
                result.ErrorMessage = "ドキュメントが null です";
                return result;
            }

            result.Document = document;
            result.IsBinary = document.IsBinary;

            try
            {
                foreach (var solid in document.Solids)
                {
                    var mc = BuildMeshContext(solid, document, settings, result);
                    if (mc != null) result.MeshContexts.Add(mc);
                }

                if (result.MeshContexts.Count == 0)
                {
                    result.ErrorMessage = "三角形がありません";
                    return result;
                }

                result.Success = true;

                Debug.Log($"[StlImporter] 読み込み完了: {(document.IsBinary ? "binary" : "ascii")} " +
                          $"objects={result.MeshContexts.Count} vertices={result.TotalVertices} " +
                          $"faces={result.TotalFaces} dropped={result.DroppedDegenerateFaces}");
            }
            catch (Exception e)
            {
                result.Success      = false;
                result.ErrorMessage = e.Message;
                Debug.LogError($"[StlImporter] 変換に失敗: {e}");
            }

            return result;
        }

        // ================================================================
        // メッシュ生成
        // ================================================================

        private static MeshContext BuildMeshContext(
            StlSolid solid, StlDocument doc, StlImportSettings settings, StlImportResult result)
        {
            if (solid == null || solid.Triangles.Count == 0) return null;

            string name = string.IsNullOrEmpty(solid.Name)
                ? (doc.FileName ?? "Object")
                : solid.Name;

            var mesh = new MeshObject(name) { Type = MeshType.Mesh };

            // ファイル上の座標値 → 頂点索引（WeldVertices が ON のときだけ使う）。
            var indexOf = settings.WeldVertices ? new Dictionary<Vector3Key, int>() : null;

            bool reverseWinding = AxisFlipOps.ReverseWinding(settings.Flip);
            var  faceCornerUVs  = new List<Vector2[]>(solid.Triangles.Count);
            var  corner         = new int[3];

            foreach (var tri in solid.Triangles)
            {
                // 共有すると同じ頂点を 2 回以上指す三角形は、頂点を登録する前に捨てる
                // （登録した後で捨てると、どの面からも参照されない頂点が残る）。
                if (indexOf != null)
                {
                    var k0 = new Vector3Key(tri.V0);
                    var k1 = new Vector3Key(tri.V1);
                    var k2 = new Vector3Key(tri.V2);
                    if (k0.Equals(k1) || k1.Equals(k2) || k2.Equals(k0))
                    {
                        result.DroppedDegenerateFaces++;
                        continue;
                    }
                }

                corner[0] = AddVertex(mesh, tri.V0, settings, indexOf);
                corner[1] = AddVertex(mesh, tri.V1, settings, indexOf);
                corner[2] = AddVertex(mesh, tri.V2, settings, indexOf);

                // 反転軸が奇数個のときは巻き順を反転する（先頭を固定して残りを逆順）。
                int i1 = reverseWinding ? corner[2] : corner[1];
                int i2 = reverseWinding ? corner[1] : corner[2];

                var face = new Face { MaterialIndex = 0 };
                face.VertexIndices.Add(corner[0]);
                face.VertexIndices.Add(i1);
                face.VertexIndices.Add(i2);
                for (int j = 0; j < 3; j++)
                {
                    // スロット番号は ApplyFacetSmoothing が確定させる。
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }

                mesh.Faces.Add(face);
                faceCornerUVs.Add(new Vector2[3]);
                result.TotalFaces++;
            }

            result.TotalVertices += mesh.VertexCount;

            if (mesh.Faces.Count == 0) return null;

            mesh.PreserveNormals = false;
            NormalSmoothingOps.ApplyFacetSmoothing(
                mesh, faceCornerUVs, settings.SmoothingAngle, false, name);

            var originalPositions = new Vector3[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
                originalPositions[i] = mesh.Vertices[i].Position;

            var ctx = new MeshContext
            {
                Name              = name,
                MeshObject        = mesh,
                OriginalPositions = originalPositions,
                IsVisible         = true,
            };

            ctx.UnityMesh = mesh.ToUnityMeshShared(1);

            return ctx;
        }

        /// <summary>頂点を追加する（共有するときは既存の索引を返す）。</summary>
        private static int AddVertex(
            MeshObject mesh, Vector3 raw, StlImportSettings settings,
            Dictionary<Vector3Key, int> indexOf)
        {
            if (indexOf != null)
            {
                var key = new Vector3Key(raw);
                if (indexOf.TryGetValue(key, out int existing)) return existing;

                int added = Append(mesh, raw, settings);
                indexOf[key] = added;
                return added;
            }

            return Append(mesh, raw, settings);
        }

        private static int Append(MeshObject mesh, Vector3 raw, StlImportSettings settings)
        {
            Vector3 yUp = StlAxis.ToYUp(settings.UpAxis, raw);
            Vector3 pos = AxisFlipOps.Position(settings.Flip, yUp, settings.Scale);
            mesh.AddVertexRaw(new Vertex(pos) { Id = -1 });
            return mesh.VertexCount - 1;
        }

        /// <summary>
        /// 座標値の完全一致を判定するキー。
        /// Vector3 の == は近似比較なので辞書のキーには使わず、float のビット列で比べる。
        /// -0 と +0 は同じ位置なので同一視する。
        /// </summary>
        private readonly struct Vector3Key : IEquatable<Vector3Key>
        {
            private readonly int _x, _y, _z;

            public Vector3Key(Vector3 v)
            {
                _x = Bits(v.x);
                _y = Bits(v.y);
                _z = Bits(v.z);
            }

            private static int Bits(float f)
            {
                if (f == 0f) f = 0f;   // -0 を +0 へ
                return BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
            }

            public bool Equals(Vector3Key o) => _x == o._x && _y == o._y && _z == o._z;
            public override bool Equals(object obj) => obj is Vector3Key k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _x;
                    h = h * 397 ^ _y;
                    h = h * 397 ^ _z;
                    return h;
                }
            }
        }
    }
}
