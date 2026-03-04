// PMXFaceMetaWriter.cs
// メッシュごとの面情報（頂点インデックス・頂点ID・UVサブインデックス）を
// .plmface.csv として保存する。
// フォーマット:
//   MESH,{objectName}
//   FACE,{face_id},{v0_localIdx},{v0_id},{v0_uvSub},{v1_localIdx},{v1_id},{v1_uvSub},{v2_localIdx},{v2_id},{v2_uvSub}
// 3頂点超の面は三角形単位で展開する。
using System.Collections.Generic;
using System.IO;
using System.Text;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    public static class PMXFaceMetaWriter
    {
        public const string Extension = ".plmface.csv";

        /// <summary>
        /// MeshContextリストからフェースメタCSVを出力する。
        /// </summary>
        /// <param name="meshContexts">対象メッシュコンテキストリスト（ボーン・モーフ除く）</param>
        /// <param name="pmxOutputPath">PMX出力パス（拡張子を .plmface.csv に変換）</param>
        public static void Save(IEnumerable<MeshContext> meshContexts, string pmxOutputPath)
        {
            string outPath = Path.ChangeExtension(pmxOutputPath, null) + Extension;
            var sb = new StringBuilder();

            foreach (var ctx in meshContexts)
            {
                if (ctx?.MeshObject == null) continue;
                if (ctx.Type == MeshType.Morph) continue;
                if (ctx.ExcludeFromExport) continue;

                var mo = ctx.MeshObject;
                if (mo.Faces.Count == 0) continue;

                sb.Append("MESH,");
                sb.AppendLine(EscapeField(ctx.Name ?? "Unnamed"));

                foreach (var face in mo.Faces)
                {
                    int vCount = face.VertexIndices.Count;
                    if (vCount < 3) continue;

                    // 三角形に展開（fan triangulation）
                    for (int i = 0; i < vCount - 2; i++)
                    {
                        int[] corners = { 0, i + 1, i + 2 };
                        sb.Append("FACE,");
                        sb.Append(face.Id);
                        foreach (int ci in corners)
                        {
                            int localIdx = ci < face.VertexIndices.Count ? face.VertexIndices[ci] : 0;
                            int vertId   = (localIdx < mo.Vertices.Count) ? mo.Vertices[localIdx].Id : 0;
                            int uvSub    = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                            sb.Append(',');
                            sb.Append(localIdx);
                            sb.Append(',');
                            sb.Append(vertId);
                            sb.Append(',');
                            sb.Append(uvSub);
                        }
                        sb.AppendLine();
                    }
                }
            }

            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
        }

        private static string EscapeField(string s)
        {
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
