// StlExporter.cs
// ModelContext → STL（バイナリ / ASCII）。
// Runtime/Poly_Ling_Main/STL/Export/ に配置
//
// 【STL に落とすときの制約】
//   ・三角形しか持てない → Face.Triangulate（表示用メッシュ化と同じ扇形分割）で分ける
//   ・階層もオブジェクト変換も持たない → ワールド行列を頂点へ畳んで出す（ObjExporter と同じ）
//   ・UV・材質・ボーン・モーフ・非表示の概念が無い → 出力しない
//   ・補助線（3 頂点未満の面）は出力しない
//
// 【まとまり】
//   バイナリは solid を 1 個しか持てないので全オブジェクトを 1 個にまとめる。
//   ASCII はオブジェクトごとに solid〜endsolid を書く。
//
// 【ヘッダ】
//   バイナリのヘッダを "solid" で始めると ASCII と誤認する読み手があるため避ける。
//
// 【巻き順と法線】
//   反転軸が奇数個（既定の X のみ反転）だと表裏が入れ替わるため、頂点順を反転して書く。
//   上方向の変換は回転なので巻き順に影響しない。
//   facet の法線は、書き出す座標と頂点順から (v1 - v0) × (v2 - v0) で求める。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.STL
{
    // ================================================================
    // 結果
    // ================================================================

    public class StlExportResult
    {
        public bool   Success;
        public string ErrorMessage;

        /// <summary>書き出した STL のパス。</summary>
        public string FilePath;

        public bool IsBinary;
        public int  ObjectCount;
        public int  TriangleCount;
    }

    // ================================================================
    // エクスポータ
    // ================================================================

    public static class StlExporter
    {
        private const int HeaderSize = 80;

        public static StlExportResult ExportFile(
            string filePath, ModelContext model, StlExportSettings settings = null)
        {
            var result = new StlExportResult();

            if (string.IsNullOrEmpty(filePath))
            {
                result.ErrorMessage = "ファイルパスが空です";
                return result;
            }
            if (model == null)
            {
                result.ErrorMessage = "モデルがありません";
                return result;
            }

            settings = settings ?? StlExportSettings.CreateDefault();
            result.IsBinary = settings.Binary;

            try
            {
                // STL は階層を持たないので、ワールド行列を最新化してから畳む（ObjExporter と同じ）。
                if (settings.ExportVerticesInWorldSpace)
                    model.ComputeWorldMatrices();

                var solids = Collect(model, settings);

                int triCount = 0;
                foreach (var s in solids) triCount += s.Triangles.Count;

                if (solids.Count == 0 || triCount == 0)
                {
                    result.ErrorMessage = "出力できるメッシュがありません";
                    return result;
                }

                if (settings.Binary)
                    WriteBinary(filePath, solids, triCount);
                else
                    WriteAscii(filePath, solids, settings);

                result.Success       = true;
                result.FilePath      = filePath;
                result.ObjectCount   = solids.Count;
                result.TriangleCount = triCount;

                Debug.Log($"[StlExporter] 書き出し完了: {filePath}\n" +
                          $"  format={(settings.Binary ? "binary" : "ascii")} " +
                          $"objects={result.ObjectCount} triangles={result.TriangleCount}");
            }
            catch (Exception e)
            {
                result.Success      = false;
                result.ErrorMessage = e.Message;
                Debug.LogError($"[StlExporter] 書き出しに失敗: {e}");
            }

            return result;
        }

        // ================================================================
        // 収集
        // ================================================================

        private static List<StlSolid> Collect(ModelContext model, StlExportSettings settings)
        {
            var solids = new List<StlSolid>();
            var list   = model.MeshContextList;
            if (list == null) return solids;

            bool reverseWinding = AxisFlipOps.ReverseWinding(settings.Flip);

            foreach (var ctx in list)
            {
                if (!IsExportTarget(ctx, settings)) continue;

                var mesh  = ctx.MeshObject;
                var solid = new StlSolid { Name = SanitizeName(ctx.Name ?? mesh.Name ?? "object") };

                Matrix4x4 toWorld = settings.ExportVerticesInWorldSpace
                    ? ctx.WorldMatrix
                    : Matrix4x4.identity;
                bool bakeWorld = !toWorld.isIdentity;

                foreach (var face in mesh.Faces)
                {
                    if (face == null || face.VertexIndices == null) continue;
                    if (face.VertexCount < 3) continue;
                    if (face.IsHidden && !settings.ExportHiddenFaces) continue;

                    foreach (var tri in face.Triangulate())
                    {
                        if (tri.VertexIndices.Count < 3) continue;

                        int a = tri.VertexIndices[0];
                        int b = tri.VertexIndices[1];
                        int c = tri.VertexIndices[2];
                        if (!InRange(mesh, a) || !InRange(mesh, b) || !InRange(mesh, c)) continue;

                        Vector3 p0 = Transform(mesh.Vertices[a].Position, toWorld, bakeWorld, settings);
                        Vector3 p1 = Transform(mesh.Vertices[b].Position, toWorld, bakeWorld, settings);
                        Vector3 p2 = Transform(mesh.Vertices[c].Position, toWorld, bakeWorld, settings);

                        if (reverseWinding)
                        {
                            Vector3 t = p1; p1 = p2; p2 = t;
                        }

                        Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                        n = n.sqrMagnitude > 0f ? n.normalized : Vector3.zero;

                        solid.Triangles.Add(new StlTriangle { Normal = n, V0 = p0, V1 = p1, V2 = p2 });
                    }
                }

                if (solid.Triangles.Count == 0) continue;
                solids.Add(solid);
            }

            return solids;
        }

        private static bool IsExportTarget(MeshContext ctx, StlExportSettings settings)
        {
            if (ctx?.MeshObject == null) return false;

            // 実頂点を持つのはこの 3 種だけ（ObjExporter.IsExportTarget と同じ扱い）。
            if (ctx.Type != MeshType.Mesh &&
                ctx.Type != MeshType.MirrorSide &&
                ctx.Type != MeshType.BakedMirror) return false;

            if (!ctx.IsVisible && !settings.ExportInvisibleObjects) return false;

            return ctx.MeshObject.Faces != null && ctx.MeshObject.Faces.Count > 0;
        }

        private static bool InRange(MeshObject mesh, int vi) => vi >= 0 && vi < mesh.Vertices.Count;

        private static Vector3 Transform(Vector3 local, Matrix4x4 toWorld, bool bakeWorld, StlExportSettings settings)
        {
            Vector3 p = bakeWorld ? toWorld.MultiplyPoint3x4(local) : local;
            p = AxisFlipOps.Position(settings.Flip, p, settings.Scale);
            return StlAxis.FromYUp(settings.UpAxis, p);
        }

        // ================================================================
        // バイナリ
        // ================================================================

        private static void WriteBinary(string filePath, List<StlSolid> solids, int triCount)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                var header = new byte[HeaderSize];
                byte[] text = Encoding.ASCII.GetBytes("PolyLing binary STL");
                Array.Copy(text, header, Math.Min(text.Length, HeaderSize));
                bw.Write(header);

                bw.Write((uint)triCount);

                foreach (var solid in solids)
                {
                    foreach (var t in solid.Triangles)
                    {
                        WriteVector(bw, t.Normal);
                        WriteVector(bw, t.V0);
                        WriteVector(bw, t.V1);
                        WriteVector(bw, t.V2);
                        bw.Write((ushort)0);
                    }
                }
            }
        }

        private static void WriteVector(BinaryWriter bw, Vector3 v)
        {
            // BinaryWriter はリトルエンディアンで書く（STL の規定と同じ）。
            bw.Write(Z(v.x));
            bw.Write(Z(v.y));
            bw.Write(Z(v.z));
        }

        // ================================================================
        // ASCII
        // ================================================================

        private static void WriteAscii(string filePath, List<StlSolid> solids, StlExportSettings settings)
        {
            string fmt = "e" + Mathf.Clamp(settings.DecimalPrecision, 1, 9).ToString(CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            foreach (var solid in solids)
            {
                sb.Append("solid ").Append(solid.Name).Append('\n');

                foreach (var t in solid.Triangles)
                {
                    sb.Append("  facet normal ").Append(V(t.Normal, fmt)).Append('\n');
                    sb.Append("    outer loop\n");
                    sb.Append("      vertex ").Append(V(t.V0, fmt)).Append('\n');
                    sb.Append("      vertex ").Append(V(t.V1, fmt)).Append('\n');
                    sb.Append("      vertex ").Append(V(t.V2, fmt)).Append('\n');
                    sb.Append("    endloop\n");
                    sb.Append("  endfacet\n");
                }

                sb.Append("endsolid ").Append(solid.Name).Append('\n');
            }

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
        }

        private static string V(Vector3 v, string fmt)
        {
            return Z(v.x).ToString(fmt, CultureInfo.InvariantCulture) + " " +
                   Z(v.y).ToString(fmt, CultureInfo.InvariantCulture) + " " +
                   Z(v.z).ToString(fmt, CultureInfo.InvariantCulture);
        }

        /// <summary>-0 を 0 に潰す。</summary>
        private static float Z(float f) => f == 0f ? 0f : f;

        /// <summary>solid 名に使えない文字（空白・改行）を潰す。</summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "object";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(char.IsWhiteSpace(c) ? '_' : c);

            string s = sb.ToString();
            return s.Length > 0 ? s : "object";
        }
    }
}
