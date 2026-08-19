// ObjImporter.cs
// ObjDocument → MeshContext / MaterialReference。
// Runtime/Poly_Ling_Main/OBJ/Import/ に配置
//
// 【OBJ と PolyLing の対応】
//   OBJ の索引はファイル全体の通し番号なので、オブジェクトへ分けるときは
//   「そのオブジェクトが使う頂点だけ」を集めてローカル索引へ詰め直す。
//   OBJ の f は頂点・UV・法線を独立に参照する形式で、これは PolyLing の
//   スロット（Vertex.UVs / Vertex.Normals と Face.UVIndices / NormalIndices）と
//   同じ考え方にあたる。スロットは必ず GetOrAddUVNormal 経由で追加し、
//   UVs.Count == Normals.Count / UVIndices[j] == NormalIndices[j] を保つ。
//
// 【法線】
//   vn があるファイルはそのまま使い、PreserveNormals = true にして
//   頂点移動時の自動再計算から守る。vn が無い（または使わない）場合は
//   スムージング角から作り直す（MQO 読込と同じ NormalSmoothingOps 経路）。
//
// 【座標系】
//   OBJ は右手系・+Y 上。Unity へは X のみ反転で揃い、反転軸が奇数個なので
//   面の巻き順も反転する。

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Materials;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.OBJ
{
    // ================================================================
    // 結果
    // ================================================================

    public class ObjImportResult
    {
        public bool   Success;
        public string ErrorMessage;

        /// <summary>読み込んだメッシュ。</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>読み込んだマテリアル参照。</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>元ドキュメント。</summary>
        public ObjDocument Document;

        public int TotalVertices;
        public int TotalFaces;
        public int TotalLines;

        public int MaterialCount => MaterialReferences.Count;
    }

    // ================================================================
    // インポータ
    // ================================================================

    public static class ObjImporter
    {
        // ================================================================
        // 公開 API
        // ================================================================

        public static ObjImportResult ImportFile(string filePath, ObjImportSettings settings = null)
        {
            var result = new ObjImportResult();

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

            settings = settings ?? ObjImportSettings.CreateDefault();
            if (string.IsNullOrEmpty(settings.BaseDir))
                settings.BaseDir = Path.GetDirectoryName(filePath);

            ObjDocument document;
            try
            {
                document = ObjParser.ParseFile(filePath);
            }
            catch (Exception e)
            {
                result.ErrorMessage = e.Message;
                Debug.LogError($"[ObjImporter] パースに失敗: {e}");
                return result;
            }

            if (settings.ImportMaterials)
                LoadMaterialLibraries(document, settings);

            return Import(document, settings);
        }

        public static ObjImportResult Import(ObjDocument document, ObjImportSettings settings = null)
        {
            var result = new ObjImportResult();
            settings = settings ?? ObjImportSettings.CreateDefault();

            if (document == null)
            {
                result.ErrorMessage = "ドキュメントが null です";
                return result;
            }

            result.Document = document;

            try
            {
                Convert(document, settings, result);
                result.Success = true;
            }
            catch (Exception e)
            {
                result.Success      = false;
                result.ErrorMessage = e.Message;
                Debug.LogError($"[ObjImporter] 変換に失敗: {e}");
            }

            return result;
        }

        // ================================================================
        // MTL 読み込み
        // ================================================================

        /// <summary>
        /// mtllib で指定された MTL を読み、document.Materials へ反映する。
        /// usemtl が先に現れて名前だけの器ができている場合は、その器を埋める。
        /// </summary>
        private static void LoadMaterialLibraries(ObjDocument document, ObjImportSettings settings)
        {
            if (document.MtlLibs.Count == 0) return;

            foreach (string lib in document.MtlLibs)
            {
                bool loaded = false;

                // mtllib 行はそのまま 1 個のファイル名として扱うのが基本。
                foreach (string candidate in EnumerateMtlCandidates(lib, settings.BaseDir))
                {
                    if (!File.Exists(candidate)) continue;

                    var mats = MtlParser.ParseFile(candidate);
                    if (mats.Count > 0)
                    {
                        MergeMaterials(document, mats);
                        loaded = true;
                    }
                }

                if (!loaded)
                    Debug.LogWarning($"[ObjImporter] MTL が見つかりません: {lib}");
            }
        }

        /// <summary>
        /// mtllib の値からファイル候補を列挙する。
        /// 空白入りのファイル名と、空白区切りで複数指定した場合の両方に備える。
        /// </summary>
        private static IEnumerable<string> EnumerateMtlCandidates(string lib, string baseDir)
        {
            if (string.IsNullOrEmpty(lib)) yield break;

            yield return ResolvePath(lib, baseDir);

            if (lib.IndexOf(' ') < 0) yield break;

            foreach (string part in lib.Split(' '))
            {
                if (part.Length == 0) continue;
                yield return ResolvePath(part, baseDir);
            }
        }

        /// <summary>同名マテリアルがあれば上書き、無ければ追加する。</summary>
        private static void MergeMaterials(ObjDocument document, List<ObjMaterial> materials)
        {
            foreach (var m in materials)
            {
                if (m == null) continue;

                int idx = document.IndexOfMaterial(m.Name);
                if (idx >= 0) document.Materials[idx] = m;
                else          document.Materials.Add(m);
            }
        }

        // ================================================================
        // 変換
        // ================================================================

        private static void Convert(ObjDocument doc, ObjImportSettings settings, ObjImportResult result)
        {
            // ── マテリアル ───────────────────────────────────────────
            if (settings.ImportMaterials)
            {
                foreach (var m in doc.Materials)
                    result.MaterialReferences.Add(ConvertMaterial(m, settings));
            }

            // ── 分割単位の決定 ───────────────────────────────────────
            ObjGroupingMode mode = ResolveGroupingMode(doc, settings.Grouping);

            var order   = new List<string>();
            var buckets = new Dictionary<string, List<ObjFace>>(StringComparer.Ordinal);

            foreach (var face in doc.Faces)
            {
                if (face == null || face.CornerCount < 2) continue;
                if (face.IsLine && !settings.ImportLines) continue;

                string key = BuildKey(face, doc, mode);
                if (!buckets.TryGetValue(key, out var list))
                {
                    list = new List<ObjFace>();
                    buckets[key] = list;
                    order.Add(key);
                }
                list.Add(face);
            }

            // 面がまったく無いファイルでも、頂点だけのオブジェクトとして拾う。
            if (order.Count == 0 && doc.Positions.Count > 0 && !settings.SkipEmptyObjects)
            {
                string key = doc.FileName ?? "Object";
                order.Add(key);
                buckets[key] = new List<ObjFace>();
            }

            // ── マテリアル数（サブメッシュ数） ──────────────────────
            int materialCount = result.MaterialReferences.Count;
            if (materialCount <= 0) materialCount = 1;

            // ── メッシュ生成 ────────────────────────────────────────
            foreach (string key in order)
            {
                var mc = BuildMeshContext(key, buckets[key], doc, settings, materialCount, result);
                if (mc == null) continue;
                result.MeshContexts.Add(mc);
            }

            Debug.Log($"[ObjImporter] 読み込み完了: objects={result.MeshContexts.Count} " +
                      $"vertices={result.TotalVertices} faces={result.TotalFaces} " +
                      $"lines={result.TotalLines} materials={result.MaterialCount}");
        }

        /// <summary>o / g の有無を見て、実際に使う分割単位を決める。</summary>
        private static ObjGroupingMode ResolveGroupingMode(ObjDocument doc, ObjGroupingMode requested)
        {
            switch (requested)
            {
                case ObjGroupingMode.Object:
                    if (doc.HasObjectNames) return ObjGroupingMode.Object;
                    return doc.HasGroupNames ? ObjGroupingMode.Group : ObjGroupingMode.Single;

                case ObjGroupingMode.Group:
                    if (doc.HasGroupNames) return ObjGroupingMode.Group;
                    return doc.HasObjectNames ? ObjGroupingMode.Object : ObjGroupingMode.Single;

                default:
                    return requested;
            }
        }

        private static string BuildKey(ObjFace face, ObjDocument doc, ObjGroupingMode mode)
        {
            switch (mode)
            {
                case ObjGroupingMode.Object:
                    return face.ObjectName ?? face.GroupName ?? doc.FileName ?? "Object";

                case ObjGroupingMode.Group:
                    return face.GroupName ?? face.ObjectName ?? doc.FileName ?? "Object";

                case ObjGroupingMode.Material:
                {
                    if (face.MaterialIndex >= 0 && face.MaterialIndex < doc.Materials.Count)
                        return doc.Materials[face.MaterialIndex]?.Name ?? "no_material";
                    return "no_material";
                }

                default:
                    return doc.FileName ?? "Object";
            }
        }

        // ================================================================
        // メッシュ生成
        // ================================================================

        private static MeshContext BuildMeshContext(
            string name,
            List<ObjFace> faces,
            ObjDocument doc,
            ObjImportSettings settings,
            int materialCount,
            ObjImportResult result)
        {
            if (faces == null) return null;
            if (faces.Count == 0 && settings.SkipEmptyObjects) return null;

            // ── 使用頂点をローカル索引へ詰め直す ────────────────────
            var localOf = new Dictionary<int, int>();
            var globals = new List<int>();

            if (faces.Count == 0)
            {
                // 面も線も無いファイル（点群など）。全頂点を孤立頂点として取り込む。
                for (int g = 0; g < doc.Positions.Count; g++)
                {
                    localOf[g] = globals.Count;
                    globals.Add(g);
                }
            }
            else
            {
                foreach (var face in faces)
                {
                    foreach (var corner in face.Corners)
                    {
                        if (corner.V < 0 || corner.V >= doc.Positions.Count) continue;
                        if (localOf.ContainsKey(corner.V)) continue;

                        localOf[corner.V] = globals.Count;
                        globals.Add(corner.V);
                    }
                }
            }

            if (globals.Count == 0 && settings.SkipEmptyObjects) return null;

            var mesh = new MeshObject(name) { Type = MeshType.Mesh };

            foreach (int g in globals)
            {
                Vector3 pos = AxisFlipOps.Position(settings.Flip, doc.Positions[g], settings.Scale);
                mesh.AddVertexRaw(new Vertex(pos) { Id = -1 });
                result.TotalVertices++;
            }

            // ── 面 ──────────────────────────────────────────────────
            bool useFileNormals = settings.UseFileNormals && doc.Normals.Count > 0;
            bool reverseWinding = AxisFlipOps.ReverseWinding(settings.Flip);

            // 法線を作り直す経路では、コーナー UV を面と同じ並びで渡す
            // （NormalSmoothingOps.Rebuild の契約。対象外の面には null を入れる）。
            List<Vector2[]> faceCornerUVs = useFileNormals ? null : new List<Vector2[]>();

            foreach (var objFace in faces)
            {
                var corners = BuildCornerList(objFace, localOf, doc, settings, reverseWinding);
                if (corners.Count < 2) continue;

                var face = new Face
                {
                    MaterialIndex = ResolveMaterialIndex(objFace, materialCount),
                };

                for (int j = 0; j < corners.Count; j++)
                {
                    face.VertexIndices.Add(corners[j].Local);
                    // スロット番号はこの後で確定させる。ここでは仮に 0 を入れておく。
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }

                mesh.Faces.Add(face);

                bool isLine = objFace.IsLine || corners.Count < 3;
                if (isLine)
                {
                    // 補助線はスロットを使わない。法線再計算の対象外でもある。
                    faceCornerUVs?.Add(null);
                    result.TotalLines++;
                    continue;
                }

                if (useFileNormals)
                {
                    AssignSlotsFromFile(mesh, face, corners);
                }
                else
                {
                    var uvs = new Vector2[corners.Count];
                    for (int j = 0; j < corners.Count; j++) uvs[j] = corners[j].UV;
                    faceCornerUVs.Add(uvs);
                }

                result.TotalFaces++;
            }

            // ── 法線 ────────────────────────────────────────────────
            if (useFileNormals)
            {
                // OBJ に書かれた法線を正本とする。頂点移動時の自動再計算で
                // 消えないよう維持フラグを立てる。
                mesh.PreserveNormals = true;
                NormalSmoothingOps.NormalizeSlotCounts(mesh);
                NormalSmoothingOps.ValidateSlotInvariant(mesh, name);
            }
            else
            {
                mesh.PreserveNormals = false;
                NormalSmoothingOps.ApplyFacetSmoothing(
                    mesh, faceCornerUVs, settings.SmoothingAngle, false, name);
            }

            // ── MeshContext ─────────────────────────────────────────
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

            ctx.UnityMesh = mesh.ToUnityMeshShared(materialCount);

            return ctx;
        }

        /// <summary>面の 1 コーナー分の解決結果。</summary>
        private struct ResolvedCorner
        {
            public int     Local;
            public Vector2 UV;
            public Vector3 Normal;
            public bool    HasNormal;
        }

        /// <summary>
        /// コーナー列をローカル索引・UV・法線へ解決する。
        /// 反転軸が奇数個のときは巻き順を反転する（先頭を固定して残りを逆順）。
        /// </summary>
        private static List<ResolvedCorner> BuildCornerList(
            ObjFace objFace,
            Dictionary<int, int> localOf,
            ObjDocument doc,
            ObjImportSettings settings,
            bool reverseWinding)
        {
            var list = new List<ResolvedCorner>(objFace.CornerCount);

            foreach (var c in objFace.Corners)
            {
                if (!localOf.TryGetValue(c.V, out int local)) continue;

                var rc = new ResolvedCorner { Local = local };

                if (c.VT >= 0 && c.VT < doc.UVs.Count)
                    rc.UV = ConvertUV(doc.UVs[c.VT], settings);

                if (c.VN >= 0 && c.VN < doc.Normals.Count)
                {
                    rc.Normal    = AxisFlipOps.Normal(settings.Flip, doc.Normals[c.VN]);
                    rc.HasNormal = true;
                }

                list.Add(rc);
            }

            if (reverseWinding && list.Count >= 3)
                list.Reverse(1, list.Count - 1);

            return list;
        }

        /// <summary>
        /// ファイルの法線を使ってスロットを確定させる。
        /// 法線を持たないコーナーはその面の面法線で埋める（面は追加済みである必要がある）。
        /// </summary>
        private static void AssignSlotsFromFile(MeshObject mesh, Face face, List<ResolvedCorner> corners)
        {
            Vector3 faceNormal = Vector3.zero;
            bool    needFaceNormal = false;

            for (int j = 0; j < corners.Count; j++)
            {
                if (!corners[j].HasNormal) { needFaceNormal = true; break; }
            }

            if (needFaceNormal)
                faceNormal = NormalSmoothingOps.CalculateFaceNormalNewell(mesh, face);

            for (int j = 0; j < corners.Count; j++)
            {
                var rc = corners[j];
                var vertex = mesh.Vertices[rc.Local];

                Vector3 n = rc.HasNormal ? rc.Normal : faceNormal;
                if (n.sqrMagnitude < 1e-12f) n = Vector3.up;

                int slot = vertex.GetOrAddUVNormal(rc.UV, n);
                face.UVIndices[j]     = slot;
                face.NormalIndices[j] = slot;
            }
        }

        private static int ResolveMaterialIndex(ObjFace face, int materialCount)
        {
            if (face.MaterialIndex < 0) return 0;
            return face.MaterialIndex < materialCount ? face.MaterialIndex : 0;
        }

        private static Vector2 ConvertUV(Vector2 uv, ObjImportSettings settings)
        {
            return settings.FlipUV_V ? new Vector2(uv.x, 1f - uv.y) : uv;
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        /// <summary>
        /// ObjMaterial → MaterialReference。
        /// Data を先に組み立て、テクスチャが取れたときだけ Material を生成して
        /// キャッシュへ付ける（PLEditorBridge のアセット系 API に依存しない）。
        /// </summary>
        private static MaterialReference ConvertMaterial(ObjMaterial m, ObjImportSettings settings)
        {
            var data = new MaterialData
            {
                Name       = m?.Name ?? "material",
                ShaderType = ShaderType.URPLit,
            };

            if (m != null)
            {
                var c = m.Diffuse;
                data.SetBaseColor(new Color(c.r, c.g, c.b, Mathf.Clamp01(m.Alpha)));

                // Ns（0-1000 の鏡面指数）と Smoothness（0-1）の対応は規格に無い。
                // エクスポート側と同じ Ns = Smoothness * 100 の対応で往復させる。
                data.Smoothness = Mathf.Clamp01(m.SpecularExponent / 100f);

                data.Surface = m.Alpha < 1f - 0.001f ? SurfaceType.Transparent : SurfaceType.Opaque;

                if (!string.IsNullOrEmpty(m.DiffuseMapPath))
                    data.SourceTexturePath = ResolvePath(m.DiffuseMapPath, settings.BaseDir);
                if (!string.IsNullOrEmpty(m.AlphaMapPath))
                    data.SourceAlphaMapPath = ResolvePath(m.AlphaMapPath, settings.BaseDir);
                if (!string.IsNullOrEmpty(m.BumpMapPath))
                    data.SourceBumpMapPath = ResolvePath(m.BumpMapPath, settings.BaseDir);
            }

            var matRef = new MaterialReference(data);

            if (settings.ImportTextures && !string.IsNullOrEmpty(data.SourceTexturePath))
            {
                var tex = LoadTexture(data.SourceTexturePath, out bool owned);
                if (tex != null)
                {
                    var mat = MaterialDataConverter.ToMaterial(data);
                    if (mat != null)
                    {
                        SetTexture(mat, tex);
                        // 所有テクスチャ（ファイルから作ったもの）だけを渡す。
                        // アセット由来は共有物なので破棄対象にしない。
                        matRef.AttachRuntimeMaterial(mat, owned ? tex : null);
                    }
                }
            }

            return matRef;
        }

        private static void SetTexture(Material mat, Texture2D tex)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }

        /// <summary>
        /// テクスチャを読む。Assets 内ならアセットとして、そうでなければファイルから。
        /// owned はこの呼び出しで生成した（＝破棄責任がある）かどうか。
        /// </summary>
        private static Texture2D LoadTexture(string fullPath, out bool owned)
        {
            owned = false;
            if (string.IsNullOrEmpty(fullPath)) return null;

            string normalized = fullPath.Replace("\\", "/");

            // 1) Assets 配下ならアセットとして読む
            int assetsIdx = normalized.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            string assetPath = null;
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                assetPath = normalized;
            else if (assetsIdx >= 0)
                assetPath = normalized.Substring(assetsIdx + 1);

            if (!string.IsNullOrEmpty(assetPath))
            {
                var asset = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(assetPath);
                if (asset != null) return asset;
            }

            // 2) ファイルから直接読む
            if (!File.Exists(normalized))
            {
                Debug.LogWarning($"[ObjImporter] テクスチャが見つかりません: {normalized}");
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(normalized);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes))
                {
                    tex.name = Path.GetFileNameWithoutExtension(normalized);
                    owned = true;
                    return tex;
                }

                UnityEngine.Object.DestroyImmediate(tex);
                Debug.LogWarning($"[ObjImporter] テクスチャを解釈できません: {normalized}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ObjImporter] テクスチャを読めません: {normalized} - {e.Message}");
            }

            return null;
        }

        // ================================================================
        // パス
        // ================================================================

        /// <summary>相対パスを baseDir 基準の絶対パスへ直す。</summary>
        private static string ResolvePath(string path, string baseDir)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string normalized = path.Replace("\\", "/");

            if (Path.IsPathRooted(normalized)) return normalized;
            if (string.IsNullOrEmpty(baseDir)) return normalized;

            try
            {
                return Path.GetFullPath(Path.Combine(baseDir, normalized)).Replace("\\", "/");
            }
            catch (ArgumentException)
            {
                return normalized;
            }
        }
    }
}
