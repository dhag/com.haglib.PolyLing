// FbxImporter.cs
// ============================================================
// FBX → ModelContext の材料（ufbx。plufbx.dll 経由）
// ------------------------------------------------------------
// 規則の正本は本ファイル冒頭と PolyLing_FBX対応計画.md 段 4。
// 形は glTF 読み込み（Runtime/Poly_Ling_Vrm10/VrmToPolyLingConverter.cs）に合わせる。
//
// ============================================================
// 座標
// ============================================================
//
//   ufbx が右手系・Y 上・前 +Z・単位 m へ直す（plufbx_load_memory）。
//   ここで X 反転して Unity の左手系へ：点 p → S·p、行列 M → S·M·S（S = diag(−1, 1, 1)）。
//   面の角の並びは反転する（鏡映で表裏が入れ替わるため。glTF の読み込みと同じ）。
//   UV はそのまま（FBX も Unity も原点が左下）。
//   FBX 書き出し（FbxModelWriter：Unity → X 反転・右手系・Y 上）の逆になる。
//
// ============================================================
// ノードの分類
// ============================================================
//
//   入れ物    … シーンの根から続く、メッシュ・属性・スケルトン・明示のバインドのどれも持たないノード。
//               読み飛ばす（子はワールド行列で置くので形は変わらない）。
//               書き出しが作る「モデル名」「Armature」の 2 段、Blender の「Armature」がこれに当たる。
//   ボーン    … 入れ物でもシーンの根でもなく、メッシュを持たないノードと、スキンクラスタの関節ノード。
//               DFS 順。親がボーンならローカル、そうでなければバインド時のワールドを BoneTransform に置く。
//               BindPose はワールドの逆（glTF の読み込みと同じ）。
//   描画オブジェクト … メッシュを持つノード 1 つにつき 1 つ（メッシュを共有するインスタンスも 1 つずつ）。
//
// ============================================================
// バインド姿勢
// ============================================================
//
//   ノードのバインド時のワールド bw は、
//     スキンクラスタのリンク行列（bind_to_world）→ バインドポーズ（FbxPose）→ 親の bw · 親から見た今の変換
//   の順に決める（plufbx の explicit_bind と BindWorld）。ボーンと静的な描画オブジェクトはこの bw に置く。
//
// ============================================================
// 頂点と面
// ============================================================
//
//   FBX の制御点 1 つを Vertex 1 つにする。面は FBX の多角形のまま（三角形化しない）。
//   角ごとの UV と法線は Vertex.GetOrAddUVNormal で頂点のスロットへ入れ、面の UVIndices / NormalIndices に置く。
//   スキンのあるメッシュは、頂点を「先頭クラスタのボーンの bw · geometry_to_bone」でワールドへベイクする
//   （親なし・変換なし。glTF の読み込みと同じ）。各クラスタで同じ行列にならないときは警告を出す。
//   スキンの無いメッシュは geometry_to_node を頂点へベイクし、ノードの bw を BoneTransform に置く。
//   ウェイトは重い順に 4 つまで取り、和で割り直す。ウェイトの無い制御点は先頭クラスタのボーンへ 1 で付ける（数を警告）。
//
// ============================================================
// モーフと表情
// ============================================================
//
//   ブレンドシェイプのチャンネルごとに、重み 1 の形（target_shape）の差分をモーフにする
//   （基底メッシュの複製へ差分を足す。PMX・glTF と同じ形）。
//   表情名はチャンネル名。チャンネル名が「メッシュのノード名_」で始まるときはそれを外す
//   （FBX 書き出しはブレンドシェイプ名をモーフの MeshContext 名「基底名_モーフ名」にしている：HierarchyMorphBuilder）。
//   同じ表情名のモーフを 1 つの Vertex 表情にまとめる。モーフの MeshContext 名は「基底名_表情名」。
//
// ============================================================
// 材質
// ============================================================
//
//   FBX の Lambert / Phong の値から URP Lit の MaterialData を作る（FBX 書き出しの逆）。
//     基本色 = Diffuse · DiffuseFactor、α = 1 − TransparencyFactor · 平均(TransparentColor)（α < 1 なら透明）、
//     発光 = Emissive · EmissiveFactor、法線の強さ = BumpFactor。
//   テクスチャは Diffuse → BaseMap、NormalMap → 法線マップ、Emissive → 発光マップ。
//   ST は FbxTexture の Scaling / Translation の値そのまま（書き出しの SetScale / SetTranslation の逆）。
//   Bump（高さ）のテクスチャは法線マップとして扱えないので使わない（警告）。
//   画像は埋め込みを優先し、無ければ外部ファイル（解決済み → 絶対 → FBX からの相対）を読む。
//   PNG / JPEG だけを「ファイル名_textures」へ書き出して使う（glTF の読み込みと同じ）。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Materials;

namespace Poly_Ling.Fbx
{
    public static class FbxImporter
    {
        public static FbxImportResult Import(string filePath, FbxImportSettings settings)
        {
            if (string.IsNullOrEmpty(filePath)) return FbxImportResult.Failed("ファイルパスが空です");
            if (!File.Exists(filePath))         return FbxImportResult.Failed($"ファイルが見つかりません: {filePath}");

            settings = settings ?? FbxImportSettings.CreateDefault();

            using (var scene = UfbxScene.Load(filePath, out string error))
            {
                if (scene == null) return FbxImportResult.Failed(error);
                try
                {
                    var r = new Converter(scene, filePath, settings).Convert();
                    foreach (string w in r.Warnings) Debug.LogWarning($"[FbxImporter] {w}");
                    Debug.Log($"[FbxImporter] {r.Summary()} ← {filePath}");
                    return r;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FbxImporter] {ex}");
                    return FbxImportResult.Failed(ex.Message);
                }
            }
        }

        // ================================================================
        // 変換
        // ================================================================

        private sealed class Converter
        {
            private const int MaxInfluences = 4;
            private const float BakeTolerance = 1e-4f;

            private readonly UfbxScene         _scene;
            private readonly string            _filePath;
            private readonly FbxImportSettings _settings;
            private readonly FbxImportResult   _result = new FbxImportResult();

            // ノードの分類とバインド姿勢（FbxNodeTable。モーション読み込みと共通）
            private FbxNodeTable _t;
            private UfbxScene.NodeInfo[] _nodes      => _t.Nodes;
            private bool[]               _container  => _t.Container;
            private Matrix4x4[]          _bindWorld  => _t.BindWorld;     // ファイル座標
            private HashSet<int>         _jointNodes => _t.JointNodes;

            private readonly Dictionary<int, int> _boneCtxByNode = new Dictionary<int, int>();
            private readonly Dictionary<int, int> _meshCtxByNode = new Dictionary<int, int>();

            // 材質（シーンの材質索引 → MaterialReferences の索引）
            private readonly Dictionary<int, int> _materialIndexOf = new Dictionary<int, int>();
            private readonly HashSet<string> _materialNames = new HashSet<string>();
            private int _defaultMaterialIndex = -1;

            // テクスチャ（画像の識別子 → 書き出したファイル / バイト列）
            private readonly Dictionary<int, string> _texturePath  = new Dictionary<int, string>();
            private readonly Dictionary<string, byte[]> _bytesOfPath = new Dictionary<string, byte[]>();
            private readonly Dictionary<string, string> _pathOfHash  = new Dictionary<string, string>();
            private readonly HashSet<string> _textureFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private string _textureFolder;

            private sealed class PendingMorph
            {
                public MeshContext BaseCtx;
                public string      ExpressionName;
                public Vector3[]   Deltas;
            }
            private readonly List<PendingMorph> _pendingMorphs = new List<PendingMorph>();

            private static readonly Matrix4x4 FlipX = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));

            public Converter(UfbxScene scene, string filePath, FbxImportSettings settings)
            {
                _scene    = scene;
                _filePath = filePath;
                _settings = settings;
            }

            public FbxImportResult Convert()
            {
                _result.NodeCount          = _scene.NodeCount;
                _result.OriginalAxisUp     = _scene.OriginalAxisUp;
                _result.OriginalUnitMeters = _scene.OriginalUnitMeters;

                ReadNodes();
                PrepareTextureFolder();
                BuildMaterials();
                BuildBones();
                BuildMeshes();
                if (_result.MaterialReferences.Count == 0) DefaultMaterialIndex();
                BuildMorphs();
                FinalizeUnityMeshes();

                SkinKindOps.RecomputeAll(_result.MeshContexts);

                _result.MaterialCount = _result.MaterialReferences.Count;
                _result.TextureCount  = _pathOfHash.Count;
                _result.TextureFolder = _pathOfHash.Count > 0 ? _textureFolder : "";
                _result.Success = true;
                return _result;
            }

            // ------------------------------------------------------------
            // 共通
            // ------------------------------------------------------------

            private static Matrix4x4 ToUnity(Matrix4x4 m) => FlipX * m * FlipX;
            private static Vector3 ToUnity(Vector3 p) => new Vector3(-p.x, p.y, p.z);

            private MeshContext Ctx(int i) => (i >= 0 && i < _result.MeshContexts.Count) ? _result.MeshContexts[i] : null;

            private static bool IsIdentity(Matrix4x4 m)
            {
                var id = Matrix4x4.identity;
                for (int i = 0; i < 16; i++)
                    if (Mathf.Abs(m[i] - id[i]) > 1e-6f) return false;
                return true;
            }

            private static float MaxDiff(Matrix4x4 a, Matrix4x4 b)
            {
                float d = 0f;
                for (int i = 0; i < 16; i++) d = Mathf.Max(d, Mathf.Abs(a[i] - b[i]));
                return d;
            }

            private static void Decompose(Matrix4x4 m, out Vector3 pos, out Quaternion rot, out Vector3 scale)
            {
                pos   = m.GetColumn(3);
                rot   = m.rotation;
                scale = m.lossyScale;
            }

            private static string MakeUnique(string baseName, HashSet<string> used)
            {
                if (used.Add(baseName)) return baseName;
                for (int n = 1; ; n++)
                {
                    string c = $"{baseName}_{n}";
                    if (used.Add(c)) return c;
                }
            }

            private bool IsTopLevel(int n) => _t.IsTopLevel(n);
            private bool IsBone(int n)     => _t.IsBone(n);
            private IEnumerable<int> Traverse() => _t.Traverse();

            // ------------------------------------------------------------
            // ノード
            // ------------------------------------------------------------

            private void ReadNodes()
            {
                _t = FbxNodeTable.Read(_scene);
                _result.SkippedContainers.AddRange(_t.ContainerNames);
            }

            // ------------------------------------------------------------
            // テクスチャ
            // ------------------------------------------------------------

            private void PrepareTextureFolder()
            {
                _textureFolder = !string.IsNullOrEmpty(_settings.TextureFolder)
                    ? _settings.TextureFolder
                    : FbxImportSettings.DefaultTextureFolder(_filePath);
            }

            /// <summary>シーンのテクスチャ索引 → 書き出したファイル。書き出しは画像ごとに初回だけ。</summary>
            private string ResolveTexture(int tex)
            {
                if (tex < 0 || tex >= _scene.TextureCount || !_settings.ExtractTextures) return null;

                _scene.TextureInfo(tex, out int contentSize, out int video, out _);
                int key = video >= 0 ? video : (1 << 30) + tex;
                if (_texturePath.TryGetValue(key, out var cached)) return cached;

                string resolved = _scene.TextureString(tex, 1);
                string absolute = _scene.TextureString(tex, 2);
                string relative = _scene.TextureString(tex, 3);

                byte[] bytes = null;
                if (contentSize > 0)
                {
                    bytes = _scene.TextureContent(tex, contentSize);
                }
                else
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(_filePath)) ?? "";
                    foreach (string cand in new[]
                             {
                                 resolved, absolute,
                                 string.IsNullOrEmpty(relative) ? null : Path.Combine(dir, relative),
                             })
                    {
                        if (string.IsNullOrEmpty(cand)) continue;
                        try { if (File.Exists(cand)) { bytes = File.ReadAllBytes(cand); break; } }
                        catch (Exception) { /* パスとして読めないものは次へ */ }
                    }
                }

                string label = !string.IsNullOrEmpty(relative) ? relative : !string.IsNullOrEmpty(absolute) ? absolute : _scene.TextureString(tex, 0);
                if (bytes == null || bytes.Length == 0)
                {
                    _result.Warnings.Add($"テクスチャ \"{label}\" の画像が埋め込みにも外部ファイルにもありません。");
                    _texturePath[key] = null;
                    return null;
                }

                string ext = IsPng(bytes) ? ".png" : IsJpeg(bytes) ? ".jpg" : null;
                if (ext == null)
                {
                    _result.Warnings.Add($"テクスチャ \"{label}\" は PNG / JPEG ではないので使いません。");
                    _texturePath[key] = null;
                    return null;
                }

                // 同じ画像（バイト列が同じ）は 1 つのファイルにまとめる。
                // FBX SDK はテクスチャごとに Video を作るので、Video の索引だけでは同じ画像がまとまらない。
                string hash = HashOf(bytes);
                if (_pathOfHash.TryGetValue(hash, out string same))
                {
                    _texturePath[key] = same;
                    return same;
                }

                string slashed  = label.Replace('\\', '/');
                string baseName = Path.GetFileNameWithoutExtension(slashed.Substring(slashed.LastIndexOf('/') + 1));
                if (string.IsNullOrEmpty(baseName)) baseName = "texture";
                foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
                string fileName = baseName + ext;
                if (!_textureFileNames.Add(fileName))
                {
                    fileName = $"{_texturePath.Count:D3}_{baseName}{ext}";
                    _textureFileNames.Add(fileName);
                }

                Directory.CreateDirectory(_textureFolder);
                string path = Path.Combine(_textureFolder, fileName);
                File.WriteAllBytes(path, bytes);

                _texturePath[key]   = path;
                _bytesOfPath[path]  = bytes;
                _pathOfHash[hash]   = path;
                return path;
            }

            private static string HashOf(byte[] bytes)
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(bytes));
            }

            private static bool IsPng(byte[] b)  => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
            private static bool IsJpeg(byte[] b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8;

            private Texture2D CreateTexture(string path, bool linear)
            {
                if (string.IsNullOrEmpty(path) || !_bytesOfPath.TryGetValue(path, out var bytes)) return null;
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);
                return tex;
            }

            // ------------------------------------------------------------
            // 材質
            // ------------------------------------------------------------

            private void BuildMaterials()
            {
                if (!_settings.ImportMaterials) return;

                for (int i = 0; i < _scene.MaterialCount; i++)
                {
                    string raw  = _scene.MaterialName(i);
                    string name = MakeUnique(string.IsNullOrEmpty(raw) ? $"Material_{i}" : raw, _materialNames);
                    _scene.MaterialInfo(i, out var v, out var tex);

                    var data = new MaterialData { Name = name, ShaderType = ShaderType.URPLit };
                    float df = v[3];
                    float transparency = v[7] * (v[4] + v[5] + v[6]) / 3f;
                    float alpha = Mathf.Clamp01(1f - transparency);
                    data.SetBaseColor(new Color(v[0] * df, v[1] * df, v[2] * df, alpha));
                    if (alpha < 1f - 1e-4f)
                    {
                        data.Surface          = SurfaceType.Transparent;
                        data.AlphaClipEnabled = false;
                    }

                    float ef = v[11];
                    var emission = new Color(v[8] * ef, v[9] * ef, v[10] * ef, 1f);
                    bool hasEmission = emission.r > 0f || emission.g > 0f || emission.b > 0f;
                    if (hasEmission) data.SetEmissionColor(emission);

                    var texProps = new List<(string prop, string path, bool linear)>();

                    string basePath = ResolveTextureWithST(tex[0], out var baseST);
                    if (basePath != null)
                    {
                        data.SourceTexturePath = basePath;
                        data.BaseMapPath       = basePath;
                        data.BaseMapST         = baseST;
                        texProps.Add(("_BaseMap", basePath, false));
                    }

                    string normalPath = ResolveTextureWithST(tex[1], out var normalST);
                    if (normalPath != null)
                    {
                        data.NormalMapPath     = normalPath;
                        data.SourceBumpMapPath = normalPath;
                        data.NormalMapST       = normalST;
                        data.NormalScale       = v[12];
                        texProps.Add(("_BumpMap", normalPath, true));
                    }
                    else if (tex[2] >= 0)
                    {
                        _result.Warnings.Add($"材質 \"{name}\" の Bump（高さ）テクスチャは法線マップではないので使いません。");
                    }

                    string emissionPath = ResolveTextureWithST(tex[3], out var emissionST);
                    if (emissionPath != null)
                    {
                        data.EmissionMapPath = emissionPath;
                        data.EmissionMapST   = emissionST;
                        texProps.Add(("_EmissionMap", emissionPath, false));
                        hasEmission = true;
                    }
                    data.EmissionEnabled = hasEmission;

                    var matRef = new MaterialReference(data);
                    var mat = MaterialDataConverter.ToMaterial(data);
                    if (mat != null)
                    {
                        var owned = new List<Texture2D>();
                        foreach (var (prop, path, linear) in texProps)
                        {
                            if (!mat.HasProperty(prop)) continue;
                            var t = CreateTexture(path, linear);
                            if (t == null) continue;
                            mat.SetTexture(prop, t);
                            owned.Add(t);
                            // URP Lit の基本色は _BaseMap。_MainTex も持つシェーダーでは両方に付ける（glTF の読み込みと同じ）。
                            if (prop == "_BaseMap" && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", t);
                        }
                        matRef.AttachRuntimeMaterial(mat, owned);
                    }

                    _materialIndexOf[i] = _result.MaterialReferences.Count;
                    _result.MaterialReferences.Add(matRef);
                }
            }

            private string ResolveTextureWithST(int tex, out float[] st)
            {
                st = new[] { 1f, 1f, 0f, 0f };
                if (tex < 0) return null;
                string p = ResolveTexture(tex);
                if (p == null) return null;
                _scene.TextureInfo(tex, out _, out _, out var s);
                st = new[] { s[0], s[1], s[2], s[3] };
                return p;
            }

            private int DefaultMaterialIndex()
            {
                if (_defaultMaterialIndex >= 0) return _defaultMaterialIndex;
                string name = MakeUnique("Default", _materialNames);
                _result.MaterialReferences.Add(new MaterialReference(new MaterialData { Name = name, ShaderType = ShaderType.URPLit }));
                _defaultMaterialIndex = _result.MaterialReferences.Count - 1;
                return _defaultMaterialIndex;
            }

            private int MaterialIndexOf(int node, int slot)
            {
                int sceneMat = _settings.ImportMaterials ? _scene.NodeMaterial(node, slot) : -1;
                return sceneMat >= 0 && _materialIndexOf.TryGetValue(sceneMat, out int idx) ? idx : DefaultMaterialIndex();
            }

            // ------------------------------------------------------------
            // ボーン
            // ------------------------------------------------------------

            private void BuildBones()
            {
                foreach (int n in Traverse())
                {
                    if (!IsBone(n)) continue;
                    var nd = _nodes[n];

                    bool parentIsBone = !IsTopLevel(n) && IsBone(nd.Parent);
                    Matrix4x4 world = ToUnity(_bindWorld[n]);
                    Matrix4x4 local = parentIsBone ? ToUnity(_bindWorld[nd.Parent]).inverse * world : world;
                    Decompose(local, out var pos, out var rot, out var scale);

                    int parentCtx = parentIsBone && _boneCtxByNode.TryGetValue(nd.Parent, out int pc) ? pc : -1;
                    string name = string.IsNullOrEmpty(nd.Name) ? $"Node_{n}" : nd.Name;

                    var mo = new MeshObject(name) { Type = MeshType.Bone, HierarchyParentIndex = parentCtx };
                    var bt = new BoneTransform
                    {
                        Position          = pos,
                        Rotation          = rot.eulerAngles,
                        Scale             = scale,
                        UseLocalTransform = true,
                        HasBoneTransform  = true,
                    };
                    mo.BoneTransform = bt;

                    var mc = new MeshContext
                    {
                        MeshObject    = mo,
                        Name          = name,
                        Type          = MeshType.Bone,
                        IsVisible     = true,
                        BindPose      = world.inverse,
                        BoneTransform = bt,
                    };
                    mc.HierarchyParentIndex = parentCtx;
                    mc.BonePoseData = new BonePoseData { IsActive = true };

                    _boneCtxByNode[n] = _result.MeshContexts.Count;
                    _result.MeshContexts.Add(mc);
                }

                _result.BoneCount = _boneCtxByNode.Count;

                foreach (var kv in _boneCtxByNode)
                {
                    var mc = Ctx(kv.Value);
                    int depth = 0, cur = mc.HierarchyParentIndex, safety = 1024;
                    while (cur >= 0 && safety-- > 0) { depth++; cur = Ctx(cur)?.HierarchyParentIndex ?? -1; }
                    mc.Depth = depth;
                }
            }

            // ------------------------------------------------------------
            // 描画オブジェクト
            // ------------------------------------------------------------

            private void BuildMeshes()
            {
                foreach (int n in Traverse())
                {
                    var nd = _nodes[n];
                    if (nd.Mesh < 0 || nd.IsRoot) continue;

                    int mi   = nd.Mesh;
                    var info = _scene.Mesh(mi);
                    if (info.VertexCount == 0 || info.FaceCount == 0) continue;

                    string name = string.IsNullOrEmpty(nd.Name) ? $"Mesh_{n}" : nd.Name;
                    if (_boneCtxByNode.ContainsKey(n)) name += "_mesh";

                    bool skinned = info.SkinDeformerCount > 0 && info.ClusterCount > 0;
                    if (info.SkinDeformerCount > 1)
                        _result.Warnings.Add($"\"{name}\" のスキン変形が {info.SkinDeformerCount} 個あります。先頭だけ使います。");

                    // ベイク行列（ファイル座標）とクラスタ → ボーンの MeshContext
                    Matrix4x4 bake = nd.GeometryToNode;
                    int[] clusterToCtx = null;
                    if (skinned)
                    {
                        clusterToCtx = new int[info.ClusterCount];
                        bool first = true;
                        float worst = 0f;
                        for (int c = 0; c < info.ClusterCount; c++)
                        {
                            int bone = _scene.MeshCluster(mi, c, out var geoToBone, out _);
                            clusterToCtx[c] = bone >= 0 && _boneCtxByNode.TryGetValue(bone, out int bi) ? bi : -1;
                            if (bone < 0) continue;
                            var m = _bindWorld[bone] * geoToBone;
                            if (first) { bake = m; first = false; }
                            else worst = Mathf.Max(worst, MaxDiff(m, bake));
                        }
                        if (worst > BakeTolerance)
                            _result.Warnings.Add($"\"{name}\" のクラスタごとのバインド行列が一致しません（最大差 {worst:G4}）。先頭のクラスタで置きます。");
                    }
                    Matrix4x4 bakeU    = ToUnity(bake);
                    Matrix4x4 normalU  = bakeU.inverse.transpose;

                    // 制御点
                    var mo = new MeshObject(name);
                    float[] pos = _scene.MeshPositions(mi, info.VertexCount);
                    for (int v = 0; v < info.VertexCount; v++)
                    {
                        var p = new Vector3(pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]);
                        mo.Vertices.Add(new Vertex(bakeU.MultiplyPoint3x4(ToUnity(p))));
                    }

                    // ウェイト
                    if (skinned) ApplyWeights(mi, info, mo, clusterToCtx, name);

                    // 面（角の並びは反転）
                    _scene.MeshFaces(mi, info.FaceCount, out var sizes, out var mats);
                    _scene.MeshCorners(mi, info.IndexCount, out var vidx, out var nrm, out var uv);
                    int begin = 0, dropped = 0;
                    bool allTriangles = true;
                    for (int f = 0; f < info.FaceCount; f++)
                    {
                        int size = sizes[f];
                        if (size < 3) { dropped++; begin += size; continue; }
                        if (size != 3) allTriangles = false;

                        var face = new Face { MaterialIndex = MaterialIndexOf(n, mats[f]) };
                        for (int k = size - 1; k >= 0; k--)
                        {
                            int i = begin + k;
                            int v = vidx[i];
                            Vector3 nn = normalU.MultiplyVector(ToUnity(new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]))).normalized;
                            int slot = SlotOf(mo.Vertices[v], new Vector2(uv[i * 2], uv[i * 2 + 1]), nn);
                            face.VertexIndices.Add(v);
                            face.UVIndices.Add(slot);
                            face.NormalIndices.Add(slot);
                        }
                        mo.Faces.Add(face);
                        begin += size;
                    }
                    if (dropped > 0)
                        _result.Warnings.Add($"\"{name}\" の角が 3 未満の面（点・線）{dropped} 個を読み込みません。");
                    mo.IsTriangulated = allTriangles;
                    if (!info.HasNormals) mo.RecalculateSmoothNormals();

                    var mc = new MeshContext { MeshObject = mo, Name = name, IsVisible = true };
                    if (skinned)
                    {
                        mc.HierarchyParentIndex = -1;
                        mc.Depth = 0;
                        _result.SkinnedMeshCount++;
                    }
                    else
                    {
                        ResolveStaticParent(n, mc);
                    }

                    _meshCtxByNode[n] = _result.MeshContexts.Count;
                    _result.MeshContexts.Add(mc);
                    _result.MeshCount++;
                    _result.VertexCount += mo.VertexCount;
                    _result.FaceCount   += mo.FaceCount;

                    if (_settings.ImportMorphs && info.BlendChannelCount > 0)
                        CollectMorphs(mi, info, nd.Name, mc, bakeU);
                }
            }

            /// <summary>
            /// 角の UV・法線の組を頂点のスロットへ入れる。値が完全に同じ組だけを共有する
            /// （Vertex.GetOrAddUVNormal は 1e-4 の許容でまとめるので、近い UV が 1 つに潰れる）。
            /// </summary>
            private static int SlotOf(Vertex v, Vector2 uv, Vector3 n)
            {
                int count = Mathf.Min(v.UVs.Count, v.Normals.Count);
                for (int s = 0; s < count; s++)
                    if (v.UVs[s].x.Equals(uv.x) && v.UVs[s].y.Equals(uv.y) &&
                        v.Normals[s].x.Equals(n.x) && v.Normals[s].y.Equals(n.y) && v.Normals[s].z.Equals(n.z))
                        return s;
                return v.AddUVNormalSlot(uv, n);
            }

            private void ApplyWeights(int mi, UfbxScene.MeshInfo info, MeshObject mo, int[] clusterToCtx, string name)
            {
                int over = _scene.MeshSkinWeights(mi, info.VertexCount, MaxInfluences, out var cl, out var w);
                if (over > 0)
                    _result.Warnings.Add($"\"{name}\" の制御点 {over} 個はウェイトが {MaxInfluences} 個を超えます。重い順に {MaxInfluences} 個を使います。");

                int fallback = -1;
                foreach (int c in clusterToCtx) if (c >= 0) { fallback = c; break; }

                int unweighted = 0, unmapped = 0;
                var idx = new int[MaxInfluences];
                var wt  = new float[MaxInfluences];
                for (int v = 0; v < info.VertexCount; v++)
                {
                    float sum = 0f;
                    for (int k = 0; k < MaxInfluences; k++)
                    {
                        int c = cl[v * MaxInfluences + k];
                        float x = w[v * MaxInfluences + k];
                        int ctx = c >= 0 && c < clusterToCtx.Length ? clusterToCtx[c] : -1;
                        if (ctx < 0 && x > 0f) { unmapped++; x = 0f; }
                        idx[k] = ctx < 0 ? 0 : ctx;
                        wt[k]  = x;
                        sum   += x;
                    }
                    if (sum <= 0f)
                    {
                        unweighted++;
                        idx[0] = Mathf.Max(0, fallback); wt[0] = 1f;
                        for (int k = 1; k < MaxInfluences; k++) { idx[k] = 0; wt[k] = 0f; }
                        sum = 1f;
                    }
                    mo.Vertices[v].BoneWeight = new BoneWeight
                    {
                        boneIndex0 = idx[0], boneIndex1 = idx[1], boneIndex2 = idx[2], boneIndex3 = idx[3],
                        weight0 = wt[0] / sum, weight1 = wt[1] / sum, weight2 = wt[2] / sum, weight3 = wt[3] / sum,
                    };
                }
                if (unweighted > 0)
                    _result.Warnings.Add($"\"{name}\" のウェイトの無い制御点 {unweighted} 個を先頭クラスタのボーンへ付けます。");
                if (unmapped > 0)
                    _result.Warnings.Add($"\"{name}\" のボーンにならないクラスタを指すウェイト {unmapped} 個を捨てます。");
            }

            /// <summary>スキンの無い描画オブジェクト：バインド時のワールドから親とローカル変換を決める。</summary>
            private void ResolveStaticParent(int n, MeshContext mc)
            {
                var nd = _nodes[n];
                int parentCtx = -1, depth = 0;
                Matrix4x4 local;

                if (_boneCtxByNode.TryGetValue(n, out int selfBone))
                {
                    parentCtx = selfBone;
                    local = Matrix4x4.identity;
                }
                else if (IsTopLevel(n))
                {
                    local = ToUnity(_bindWorld[n]);
                }
                else
                {
                    int p = nd.Parent;
                    local = ToUnity(_bindWorld[p]).inverse * ToUnity(_bindWorld[n]);
                    if (_meshCtxByNode.TryGetValue(p, out int pm) && !_boneCtxByNode.ContainsKey(p))
                    {
                        parentCtx = pm;
                        depth = (Ctx(pm)?.Depth ?? 0) + 1;
                    }
                    else if (_boneCtxByNode.TryGetValue(p, out int pb))
                    {
                        parentCtx = pb;
                    }
                }

                Decompose(local, out var pos, out var rot, out var scale);
                mc.BoneTransform.Position = pos;
                mc.BoneTransform.Rotation = rot.eulerAngles;
                mc.BoneTransform.Scale    = scale;
                mc.BoneTransform.UseLocalTransform = !IsIdentity(local);
                mc.HierarchyParentIndex = parentCtx;
                mc.Depth = depth;
            }

            // ------------------------------------------------------------
            // モーフと表情
            // ------------------------------------------------------------

            private void CollectMorphs(int mi, UfbxScene.MeshInfo info, string nodeName, MeshContext baseCtx, Matrix4x4 bakeU)
            {
                string prefix = string.IsNullOrEmpty(nodeName) ? null : nodeName + "_";
                for (int k = 0; k < info.BlendChannelCount; k++)
                {
                    string channel = _scene.MeshBlendName(mi, k);
                    string expr = prefix != null && channel.StartsWith(prefix, StringComparison.Ordinal) && channel.Length > prefix.Length
                        ? channel.Substring(prefix.Length)
                        : (string.IsNullOrEmpty(channel) ? $"Morph_{k}" : channel);

                    _scene.MeshBlendOffsets(mi, k, out var vidx, out var d3);
                    var deltas = new Vector3[info.VertexCount];
                    for (int i = 0; i < vidx.Length; i++)
                    {
                        int v = vidx[i];
                        if (v < 0 || v >= deltas.Length) continue;
                        deltas[v] = bakeU.MultiplyVector(ToUnity(new Vector3(d3[i * 3], d3[i * 3 + 1], d3[i * 3 + 2])));
                    }
                    _pendingMorphs.Add(new PendingMorph { BaseCtx = baseCtx, ExpressionName = expr, Deltas = deltas });
                }
            }

            private void BuildMorphs()
            {
                var usedNames = new HashSet<string>();
                foreach (var mc in _result.MeshContexts)
                    if (!string.IsNullOrEmpty(mc?.Name)) usedNames.Add(mc.Name);

                var exprByName = new Dictionary<string, MorphExpression>();
                foreach (var pm in _pendingMorphs)
                {
                    var baseMc = pm.BaseCtx;
                    var morphMc = new MeshContext
                    {
                        MeshObject        = baseMc.MeshObject.Clone(),
                        Type              = MeshType.Morph,
                        IsVisible         = false,
                        ExcludeFromExport = true,
                    };
                    morphMc.MeshObject.Type = MeshType.Morph;

                    string name = MakeUnique($"{baseMc.Name}_{pm.ExpressionName}", usedNames);
                    morphMc.MeshObject.Name = name;
                    morphMc.Name = name;

                    // 基準位置は差分を足す前に取る（PMX・glTF の読み込みと同じ順）。
                    morphMc.SetAsMorph(pm.ExpressionName);
                    if (morphMc.MorphBaseData != null) morphMc.MorphBaseData.BaseMeshName = baseMc.Name;

                    var verts = morphMc.MeshObject.Vertices;
                    for (int i = 0; i < verts.Count && i < pm.Deltas.Length; i++)
                        verts[i].Position += pm.Deltas[i];

                    int index = _result.MeshContexts.Count;
                    _result.MeshContexts.Add(morphMc);
                    _result.MorphCount++;

                    if (!exprByName.TryGetValue(pm.ExpressionName, out var me))
                    {
                        me = new MorphExpression(pm.ExpressionName, MorphType.Vertex);
                        exprByName[pm.ExpressionName] = me;
                        _result.MorphExpressions.Add(me);
                    }
                    me.AddMesh(index, 1f);
                }
                _result.ExpressionCount = _result.MorphExpressions.Count;
            }

            private void FinalizeUnityMeshes()
            {
                int matCount = Mathf.Max(1, _result.MaterialReferences.Count);
                foreach (var mc in _result.MeshContexts)
                {
                    if (mc?.MeshObject == null || mc.Type == MeshType.Bone) continue;
                    if (mc.Type == MeshType.Morph)
                    {
                        mc.UnityMesh = mc.MeshObject.ToUnityMeshShared(matCount);
                        mc.UnityMesh.name = mc.MeshObject.Name;
                        mc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    }
                    else
                    {
                        mc.UnityMesh = mc.MeshObject.ToUnityMesh(matCount);
                        mc.UnityMesh.name = mc.Name;
                    }
                }
            }
        }
    }
}
