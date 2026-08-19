// ObjExporter.cs
// ModelContext → Wavefront OBJ（＋ MTL）。
// Runtime/Poly_Ling_Main/OBJ/Export/ に配置
//
// 【OBJ に落とすときの制約】
//   ・階層もオブジェクト変換も持たない → ワールド行列を頂点へ畳んで出す
//   ・ボーン・モーフ・剛体・JOINT に対応する記法が無い → 出力しない
//   ・非表示（メッシュ・面）の概念が無い → 既定では出力しない
//   ・v / vt / vn はファイル全体の通し番号。面はその番号を 1 始まりで参照する
//
// 【重複の畳み込み】
//   v / vt / vn は出力桁数まで丸めた文字列をキーにして畳む。
//   「書いた文字列が同じなら同じ番号」になるので、丸めと畳み込みが食い違わない。
//
// 【巻き順】
//   反転軸が奇数個（既定の X のみ反転）だと表裏が入れ替わるため、
//   面の頂点順を反転して書く（先頭を固定して残りを逆順）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Materials;
using Poly_Ling.Ops;

namespace Poly_Ling.OBJ
{
    // ================================================================
    // 結果
    // ================================================================

    public class ObjExportResult
    {
        public bool   Success;
        public string ErrorMessage;

        /// <summary>書き出した OBJ のパス。</summary>
        public string FilePath;

        /// <summary>書き出した MTL のパス（出力しなかった場合は null）。</summary>
        public string MtlPath;

        public int ObjectCount;
        public int VertexCount;
        public int FaceCount;
        public int LineCount;
        public int MaterialCount;
    }

    // ================================================================
    // エクスポータ
    // ================================================================

    public static class ObjExporter
    {
        // ================================================================
        // 公開 API
        // ================================================================

        public static ObjExportResult ExportFile(
            string filePath, ModelContext model, ObjExportSettings settings = null)
        {
            var result = new ObjExportResult();

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

            settings = settings ?? ObjExportSettings.CreateDefault();

            try
            {
                // OBJ は階層を持たないので、ワールド行列を最新化してから畳む。
                if (settings.ExportVerticesInWorldSpace)
                    model.ComputeWorldMatrices();

                var builder = new Builder(model, settings);
                builder.Collect(result);

                if (result.ObjectCount == 0)
                {
                    result.ErrorMessage = "出力できるメッシュがありません";
                    return result;
                }

                string mtlPath = null;
                string mtlName = null;
                if (settings.ExportMaterials && builder.HasMaterials)
                {
                    mtlName = Path.GetFileNameWithoutExtension(filePath) + ".mtl";
                    string dir = Path.GetDirectoryName(filePath);
                    mtlPath = string.IsNullOrEmpty(dir) ? mtlName : Path.Combine(dir, mtlName);
                }

                string objText = builder.BuildObjText(mtlName);
                File.WriteAllText(filePath, objText, new UTF8Encoding(false));

                if (mtlPath != null)
                {
                    File.WriteAllText(mtlPath, builder.BuildMtlText(), new UTF8Encoding(false));
                    result.MtlPath = mtlPath;
                }

                result.Success       = true;
                result.FilePath      = filePath;
                result.MaterialCount = builder.ExportedMaterialCount;

                Debug.Log($"[ObjExporter] 書き出し完了: {filePath}\n" +
                          $"  objects={result.ObjectCount} vertices={result.VertexCount} " +
                          $"faces={result.FaceCount} lines={result.LineCount} " +
                          $"materials={result.MaterialCount} mtl={result.MtlPath ?? "(なし)"}");
            }
            catch (Exception e)
            {
                result.Success      = false;
                result.ErrorMessage = e.Message;
                Debug.LogError($"[ObjExporter] 書き出しに失敗: {e}");
            }

            return result;
        }

        // ================================================================
        // 構築
        // ================================================================

        /// <summary>
        /// 収集と本文生成。v / vt / vn を全体で一意化しつつ、
        /// オブジェクトごとの面（索引の 3 つ組）を組み立てる。
        /// </summary>
        private sealed class Builder
        {
            private readonly ModelContext      _model;
            private readonly ObjExportSettings _settings;
            private readonly string            _format;

            private readonly List<string> _positionLines = new List<string>();
            private readonly List<string> _uvLines       = new List<string>();
            private readonly List<string> _normalLines   = new List<string>();

            private readonly Dictionary<string, int> _positionIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _uvIndex       = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _normalIndex   = new Dictionary<string, int>(StringComparer.Ordinal);

            private readonly List<ObjectEntry> _objects = new List<ObjectEntry>();

            // マテリアル索引 → OBJ 用に一意化した名前
            private readonly Dictionary<int, string> _materialNames = new Dictionary<int, string>();
            private readonly HashSet<string>         _usedMaterialNames = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<int>            _usedMaterials = new HashSet<int>();

            public bool HasMaterials => _model.MaterialReferences != null &&
                                        _model.MaterialReferences.Count > 0 &&
                                        _usedMaterials.Count > 0;

            public int ExportedMaterialCount => _usedMaterials.Count;

            public Builder(ModelContext model, ObjExportSettings settings)
            {
                _model    = model;
                _settings = settings;
                _format   = "F" + Mathf.Clamp(settings.DecimalPrecision, 1, 9).ToString(CultureInfo.InvariantCulture);
            }

            // ── 1 オブジェクト分 ────────────────────────────────────

            private sealed class ObjectEntry
            {
                public string          Name;
                public List<FaceEntry> Faces = new List<FaceEntry>();
                public List<int[]>     Lines = new List<int[]>();   // 1始まりの頂点番号列
            }

            private sealed class FaceEntry
            {
                public int     MaterialIndex;   // -1 = 無指定
                public int[]   V;               // 1始まり
                public int[]   VT;              // 1始まり。0 = なし
                public int[]   VN;              // 1始まり。0 = なし
            }

            // ================================================================
            // 収集
            // ================================================================

            public void Collect(ObjExportResult result)
            {
                var list = _model.MeshContextList;
                if (list == null) return;

                bool reverseWinding = AxisFlipOps.ReverseWinding(_settings.Flip);

                foreach (var ctx in list)
                {
                    if (!IsExportTarget(ctx)) continue;

                    var mesh = ctx.MeshObject;
                    var entry = new ObjectEntry { Name = SanitizeName(ctx.Name ?? mesh.Name ?? "object") };

                    Matrix4x4 toWorld = _settings.ExportVerticesInWorldSpace
                        ? ctx.WorldMatrix
                        : Matrix4x4.identity;
                    bool bakeWorld = !toWorld.isIdentity;

                    // 法線は逆転置行列で移す（非一様スケールで向きが崩れないように）。
                    Matrix4x4 toWorldNormal = bakeWorld ? toWorld.inverse.transpose : Matrix4x4.identity;

                    foreach (var face in mesh.Faces)
                    {
                        if (face == null || face.VertexIndices == null) continue;
                        if (face.IsHidden && !_settings.ExportHiddenFaces) continue;

                        if (face.VertexCount < 3)
                        {
                            if (!_settings.ExportLines || face.VertexCount < 2) continue;

                            var lineIdx = new int[face.VertexCount];
                            bool ok = true;
                            for (int j = 0; j < face.VertexCount; j++)
                            {
                                int vi = face.VertexIndices[j];
                                if (vi < 0 || vi >= mesh.Vertices.Count) { ok = false; break; }
                                lineIdx[j] = AddPosition(mesh.Vertices[vi].Position, toWorld, bakeWorld);
                            }
                            if (ok) entry.Lines.Add(lineIdx);
                            continue;
                        }

                        var fe = BuildFace(mesh, face, toWorld, bakeWorld, toWorldNormal, reverseWinding);
                        if (fe == null) continue;

                        entry.Faces.Add(fe);

                        // 範囲外を指す面は usemtl を書けないので使用済みに数えない
                        // （数えると中身の無い .mtl を書いてしまう）。
                        var refs = _model.MaterialReferences;
                        if (fe.MaterialIndex >= 0 && refs != null && fe.MaterialIndex < refs.Count)
                            _usedMaterials.Add(fe.MaterialIndex);
                    }

                    if (entry.Faces.Count == 0 && entry.Lines.Count == 0) continue;

                    _objects.Add(entry);
                    result.FaceCount += entry.Faces.Count;
                    result.LineCount += entry.Lines.Count;
                }

                result.ObjectCount = _objects.Count;
                result.VertexCount = _positionLines.Count;
            }

            private bool IsExportTarget(MeshContext ctx)
            {
                if (ctx?.MeshObject == null) return false;

                // 実頂点を持つのはこの 3 種だけ（PMX エクスポートと同じ扱い）。
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.MirrorSide &&
                    ctx.Type != MeshType.BakedMirror) return false;

                if (!ctx.IsVisible && !_settings.ExportInvisibleObjects) return false;

                return ctx.MeshObject.Faces != null && ctx.MeshObject.Faces.Count > 0;
            }

            private FaceEntry BuildFace(
                MeshObject mesh, Face face,
                Matrix4x4 toWorld, bool bakeWorld, Matrix4x4 toWorldNormal,
                bool reverseWinding)
            {
                int n = face.VertexCount;

                var order = new List<int>(n);
                for (int j = 0; j < n; j++) order.Add(j);
                if (reverseWinding && n >= 3) order.Reverse(1, n - 1);

                var fe = new FaceEntry
                {
                    MaterialIndex = face.MaterialIndex,
                    V  = new int[n],
                    VT = new int[n],
                    VN = new int[n],
                };

                // 法線スロットが無いコーナー用の面法線（必要になったときだけ計算する）。
                Vector3 faceNormal = Vector3.zero;
                bool    faceNormalReady = false;

                for (int k = 0; k < n; k++)
                {
                    int j  = order[k];
                    int vi = face.VertexIndices[j];
                    if (vi < 0 || vi >= mesh.Vertices.Count) return null;

                    var vertex = mesh.Vertices[vi];

                    fe.V[k] = AddPosition(vertex.Position, toWorld, bakeWorld);

                    int slot = (j < face.UVIndices.Count) ? face.UVIndices[j] : -1;

                    if (_settings.ExportUVs)
                    {
                        Vector2 uv = (slot >= 0 && slot < vertex.UVs.Count)
                            ? vertex.UVs[slot]
                            : (vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero);
                        fe.VT[k] = AddUV(uv);
                    }

                    if (_settings.ExportNormals)
                    {
                        Vector3 nrm;
                        if (slot >= 0 && slot < vertex.Normals.Count)
                        {
                            nrm = vertex.Normals[slot];
                        }
                        else
                        {
                            if (!faceNormalReady)
                            {
                                faceNormal = NormalSmoothingOps.CalculateFaceNormalNewell(mesh, face);
                                faceNormalReady = true;
                            }
                            nrm = faceNormal;
                        }

                        fe.VN[k] = AddNormal(nrm, toWorldNormal, bakeWorld);
                    }
                }

                return fe;
            }

            // ================================================================
            // v / vt / vn の登録（重複は畳む）
            // ================================================================

            private int AddPosition(Vector3 local, Matrix4x4 toWorld, bool bakeWorld)
            {
                Vector3 p = bakeWorld ? toWorld.MultiplyPoint3x4(local) : local;
                p = AxisFlipOps.Position(_settings.Flip, p, _settings.Scale);

                string body = F(p.x) + " " + F(p.y) + " " + F(p.z);
                return Intern(_positionIndex, _positionLines, body);
            }

            private int AddUV(Vector2 uv)
            {
                if (_settings.FlipUV_V) uv = new Vector2(uv.x, 1f - uv.y);

                string body = F(uv.x) + " " + F(uv.y);
                return Intern(_uvIndex, _uvLines, body);
            }

            private int AddNormal(Vector3 local, Matrix4x4 toWorldNormal, bool bakeWorld)
            {
                Vector3 n = bakeWorld ? toWorldNormal.MultiplyVector(local) : local;
                n = AxisFlipOps.Normal(_settings.Flip, n);
                if (n.sqrMagnitude < 1e-12f) n = Vector3.up;

                string body = F(n.x) + " " + F(n.y) + " " + F(n.z);
                return Intern(_normalIndex, _normalLines, body);
            }

            /// <summary>同じ文字列なら同じ番号を返す。番号は 1 始まり。</summary>
            private static int Intern(Dictionary<string, int> index, List<string> lines, string body)
            {
                if (index.TryGetValue(body, out int existing)) return existing;

                lines.Add(body);
                int id = lines.Count;      // 1 始まり
                index[body] = id;
                return id;
            }

            private string F(float v)
            {
                // -0 を 0 に潰す（読み手によっては別の値として扱われるため）。
                if (v == 0f) v = 0f;
                return v.ToString(_format, CultureInfo.InvariantCulture);
            }

            // ================================================================
            // OBJ 本文
            // ================================================================

            public string BuildObjText(string mtlFileName)
            {
                var sb = new StringBuilder();

                sb.Append("# Exported by PolyLing\n");
                sb.Append("# ").Append(_objects.Count).Append(" objects, ")
                  .Append(_positionLines.Count).Append(" vertices\n");

                if (!string.IsNullOrEmpty(mtlFileName))
                    sb.Append("mtllib ").Append(mtlFileName).Append('\n');

                foreach (string s in _positionLines) sb.Append("v ").Append(s).Append('\n');
                foreach (string s in _uvLines)       sb.Append("vt ").Append(s).Append('\n');
                foreach (string s in _normalLines)   sb.Append("vn ").Append(s).Append('\n');

                bool useMaterials = _settings.ExportMaterials && HasMaterials;

                foreach (var entry in _objects)
                {
                    sb.Append("o ").Append(entry.Name).Append('\n');

                    // usemtl の切替回数を減らすため、マテリアルごとにまとめて書く。
                    int currentMaterial = int.MinValue;

                    foreach (var fe in SortByMaterial(entry.Faces))
                    {
                        if (useMaterials && fe.MaterialIndex != currentMaterial)
                        {
                            currentMaterial = fe.MaterialIndex;
                            string name = ResolveMaterialName(currentMaterial);
                            if (name != null) sb.Append("usemtl ").Append(name).Append('\n');
                        }

                        sb.Append('f');
                        for (int k = 0; k < fe.V.Length; k++)
                        {
                            sb.Append(' ').Append(fe.V[k].ToString(CultureInfo.InvariantCulture));

                            bool hasVT = fe.VT[k] > 0;
                            bool hasVN = fe.VN[k] > 0;

                            if (hasVT && hasVN)
                                sb.Append('/').Append(fe.VT[k].ToString(CultureInfo.InvariantCulture))
                                  .Append('/').Append(fe.VN[k].ToString(CultureInfo.InvariantCulture));
                            else if (hasVT)
                                sb.Append('/').Append(fe.VT[k].ToString(CultureInfo.InvariantCulture));
                            else if (hasVN)
                                sb.Append("//").Append(fe.VN[k].ToString(CultureInfo.InvariantCulture));
                        }
                        sb.Append('\n');
                    }

                    foreach (var line in entry.Lines)
                    {
                        sb.Append('l');
                        for (int k = 0; k < line.Length; k++)
                            sb.Append(' ').Append(line[k].ToString(CultureInfo.InvariantCulture));
                        sb.Append('\n');
                    }
                }

                return sb.ToString();
            }

            /// <summary>面をマテリアル順に並べ替える（元の順序は同一マテリアル内で保つ）。</summary>
            private static List<FaceEntry> SortByMaterial(List<FaceEntry> faces)
            {
                var sorted = new List<FaceEntry>(faces);
                // List.Sort は安定でないため、索引を添えて安定化する。
                var keyed = new List<KeyValuePair<int, FaceEntry>>(sorted.Count);
                for (int i = 0; i < sorted.Count; i++)
                    keyed.Add(new KeyValuePair<int, FaceEntry>(i, sorted[i]));

                keyed.Sort((a, b) =>
                {
                    int c = a.Value.MaterialIndex.CompareTo(b.Value.MaterialIndex);
                    return c != 0 ? c : a.Key.CompareTo(b.Key);
                });

                sorted.Clear();
                foreach (var kv in keyed) sorted.Add(kv.Value);
                return sorted;
            }

            // ================================================================
            // MTL 本文
            // ================================================================

            public string BuildMtlText()
            {
                var sb = new StringBuilder();
                sb.Append("# Exported by PolyLing\n");

                var refs = _model.MaterialReferences;
                if (refs == null) return sb.ToString();

                for (int i = 0; i < refs.Count; i++)
                {
                    if (!_usedMaterials.Contains(i)) continue;

                    string name = ResolveMaterialName(i);
                    if (name == null) continue;

                    var data = refs[i]?.Data;
                    Color kd = data?.GetBaseColor() ?? Color.white;
                    float alpha = Mathf.Clamp01(kd.a);

                    // Ns（0-1000）と Smoothness（0-1）の対応は規格に無い。
                    // インポート側と同じ Ns = Smoothness * 100 で往復させる。
                    float ns = Mathf.Clamp01(data?.Smoothness ?? 0.5f) * 100f;

                    sb.Append("\nnewmtl ").Append(name).Append('\n');
                    sb.Append("Ka 0.000000 0.000000 0.000000\n");
                    sb.Append("Kd ").Append(F(kd.r)).Append(' ').Append(F(kd.g)).Append(' ').Append(F(kd.b)).Append('\n');
                    sb.Append("Ks 0.000000 0.000000 0.000000\n");
                    sb.Append("Ns ").Append(F(ns)).Append('\n');
                    sb.Append("d ").Append(F(alpha)).Append('\n');
                    sb.Append("illum 2\n");

                    string diffuse = TextureFileName(data?.SourceTexturePath ?? data?.BaseMapPath);
                    if (diffuse != null) sb.Append("map_Kd ").Append(diffuse).Append('\n');

                    string bump = TextureFileName(data?.SourceBumpMapPath ?? data?.NormalMapPath);
                    if (bump != null) sb.Append("map_Bump ").Append(bump).Append('\n');
                }

                return sb.ToString();
            }

            /// <summary>
            /// テクスチャのファイル名だけを返す（テクスチャ本体はコピーしない）。
            /// OBJ / MTL と同じフォルダにテクスチャを置く運用を前提とする。
            /// </summary>
            private static string TextureFileName(string path)
            {
                if (string.IsNullOrEmpty(path)) return null;

                try
                {
                    string name = Path.GetFileName(path.Replace("\\", "/"));
                    return string.IsNullOrEmpty(name) ? null : name;
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            // ================================================================
            // マテリアル名
            // ================================================================

            /// <summary>
            /// マテリアル索引に対する OBJ 用の名前を返す。範囲外なら null。
            /// 空白を含む名前は usemtl で切れてしまうため潰し、重複は連番で分ける。
            /// </summary>
            private string ResolveMaterialName(int index)
            {
                if (index < 0) return null;

                var refs = _model.MaterialReferences;
                if (refs == null || index >= refs.Count) return null;

                if (_materialNames.TryGetValue(index, out string cached)) return cached;

                string baseName = SanitizeName(refs[index]?.Data?.Name ?? refs[index]?.Name ?? $"material_{index}");
                string name = baseName;
                int suffix = 1;
                while (_usedMaterialNames.Contains(name))
                    name = baseName + "_" + (suffix++).ToString(CultureInfo.InvariantCulture);

                _usedMaterialNames.Add(name);
                _materialNames[index] = name;
                return name;
            }

            /// <summary>OBJ の名前に使えない文字（空白・改行）を潰す。</summary>
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
}
