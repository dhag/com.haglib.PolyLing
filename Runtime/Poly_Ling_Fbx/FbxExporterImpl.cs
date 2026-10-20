// FbxExporterImpl.cs
// ============================================================
// IFbxExporter の Autodesk FBX SDK 実装（モデル書き出し）
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md（段 2）。分離の形は PLFbxBridge.cs 冒頭。
//
// ■ 流れ
//   1. HierarchyBuilder（Runtime）で ModelContext → GameObject 階層 ＋ UnityEngine.Mesh。
//      設定は glTF 書き出し（GltfExporterImpl）と同じ。ミラー枝・関節・可視補完・モーフの規則は
//      HierarchyBuilder が正本で、ここでは書き写さない。
//   2. 階層をたどって FbxScene を組む。値は Unity のまま（左手系・Y 上・Z 前）で入れ、
//      単位 cm（位置 ×100）・軸 DirectX と宣言する。最後に FbxAxisSystem.MayaYUp.DeepConvertScene で
//      右手系へ変換する（Unity の FBX Exporter com.unity.formats.fbx 5.1.3 と同じ手順）。
//   3. FbxExporter で書く（既定はバイナリ。テクスチャは埋め込める）。
//
// ■ ノードの姿勢
//   ボーンはバインドポーズで置く：ワールド = SMR のワールド · bindpose⁻¹。
//   それ以外のノードは親の書き出し上のワールド · ローカル TRS。
//   FBX のローカル = 親のワールド⁻¹ · 自分のワールド。回転は Unity のオイラー角（ZXY 順）で、
//   ノードの回転順を ZXY・RotationActive にする（DeepConvertScene が回転順を見るため）。
//   継承は RSrs（Unity と同じ）。
//
// ■ 制御点
//   Unity のメッシュは（頂点・UV・法線）の組ごとに分かれている。FBX の制御点は
//   「位置・ボーンウェイト・全ブレンドシェイプの差分」がすべて一致する Unity 頂点を 1 つにまとめる。
//   位置だけでまとめると、同じ位置の別頂点（唇の上下など）のウェイトやモーフが潰れる。
//   UV・法線は面の頂点ごと（eByPolygonVertex）に持たせる。
//
// ■ 面
//   HierarchyBuilder の Unity メッシュは三角形化済み。その元の MeshObject
//   （HierarchyBuildResult.SourceMeshObjectOf）から多角形を組み直して書く（SourcePolygons.cs）。
//   組み直せないメッシュは三角形のまま書き、理由を警告に出す。
//
// ■ 材質・テクスチャ
//   材質は MaterialData から Lambert を作る（Diffuse = BaseColor、透明度 = 1 − α、
//   Emissive = EmissionColor、BumpFactor = NormalScale）。
//   テクスチャは ModelTextureTransfer.Collect の集め方で集め、Entry.Uses で材質へつなぐ。
//   Base → Diffuse、Normal → NormalMap、Emission → Emissive。画像は「ファイル名_textures」へ書き、
//   FBX には絶対パスと相対パスの両方を入れる。EmbedTextures かつバイナリなら FBX の中へも埋め込む。
//
// ■ アニメーション（ExportMotion。段 3）
//   ModelMotionSampler（BVH 書き出しと共通）で、モーションをモデルへ当てた標本を先に取る。
//   関節（仮想骨格のノード）は ContextIndex で HierarchyBuilder のボーン（BoneTransformByIndex）へ対応づける。
//   実体の無い仮想関節（半身モデルの仮想ミラー）は FBX のノードが無いので曲線を書かない（数を結果に出す）。
//   各フレームの関節のワールド = 移動(バインド位置 + (標本の位置 − レスト位置))·回転(標本の回転差分 · バインド回転)·バインド尺度。
//   関節でないノードは親のワールド · 書き出し上のローカル（動かない）。
//   曲線は関節のノードの LclTranslation / LclRotation に、毎フレーム線形キーで書く（回転は ZXY のオイラー角。
//   同じ回転の 2 通りの解のうち前のフレームに近い方）。
//   表情は標本の重み（表情を頂点モーフへ展開して足したもの）を、HierarchyBuildResult.MorphShapes で
//   ブレンドシェイプのチャンネルへつなぎ、DeformPercent（0〜100）に書く。
//   全部組んでから DeepConvertScene を掛ける（曲線も右手系へ変換される。FBX Exporter と同じ順）。
//
// ■ 一時オブジェクトの後始末
//   階層・Mesh は書き出し用の使い捨て。finally で必ず破棄する。FbxManager も finally で破棄する。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Autodesk.Fbx;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Fbx;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Materials;
using Poly_Ling.Motion;
using Poly_Ling.Serialization;

namespace Poly_Ling.FbxImpl
{
    public class FbxExporterImpl : IFbxExporter
    {
        /// <summary>Unity の 1 m を FBX の cm にする倍率（FBX Exporter の UnitScaleFactor と同じ）。</summary>
        private const double UnitScale = 100.0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            PLFbxBridge.RegisterExporter(new FbxExporterImpl());
        }

        public bool IsAvailable => true;

        public FbxExportResult Export(ModelContext model, string outputPath, FbxExportSettings settings)
            => ExportCore(model, outputPath, settings, null, null);

        public FbxExportResult ExportMotion(ModelContext model, MotionClipDTO clip, string outputPath,
                                            FbxExportSettings settings, FbxMotionExportOptions motion)
        {
            if (model == null) return FbxExportResult.Failed("モデルがありません");
            if (clip == null)  return FbxExportResult.Failed("モーションがありません");
            motion = motion ?? new FbxMotionExportOptions();

            // 標本化は階層を組む前に済ませる（標本化は終わりにポーズを戻す。HierarchyBuilder はレストで組む）。
            if (!ModelMotionSampler.Sample(model, clip, new ModelMotionSampleOptions
                {
                    Fps               = motion.Fps,
                    StartSec          = motion.StartSec,
                    EndSec            = motion.EndSec,
                    ClipPositionScale = motion.ClipPositionScale,
                    AlignLegs         = motion.AlignLegs,
                    SampleExpressions = true,
                }, out var samples, out string reason))
                return FbxExportResult.Failed(reason);

            return ExportCore(model, outputPath, settings, samples, motion);
        }

        private FbxExportResult ExportCore(ModelContext model, string outputPath, FbxExportSettings settings,
                                           ModelMotionSamples samples, FbxMotionExportOptions motion)
        {
            if (model == null)                    return FbxExportResult.Failed("モデルがありません");
            if (string.IsNullOrEmpty(outputPath)) return FbxExportResult.Failed("出力パスが空です");

            settings = settings ?? FbxExportSettings.CreateDefault();

            HierarchyBuildResult built = null;
            FbxManager manager = null;
            try
            {
                // ------------------------------------------------------------
                // 1. ヒエラルキー生成（GltfExporterImpl と同じ設定）
                // ------------------------------------------------------------
                var buildOptions = new HierarchyBuildOptions
                {
                    CreateArmature            = true,
                    UseBindpose               = true,
                    ExportVisibleOnly         = !settings.ExportInvisibleObjects,
                    IncludeInvisibleAncestors = true,
                    ExportMeshOnly            = false,
                    ExportPhysics             = false,
                    TolerantMirrorBranch      = true,
                    ExportMorphTargets        = settings.ExportMorphTargets,
                    DropEmptySubMeshes        = true,
                    RendererMode              = settings.ExportSkinning
                        ? HierarchyRendererMode.Auto
                        : HierarchyRendererMode.ForceMeshFilter,
                };

                var builder = new HierarchyBuilder(buildOptions);
                var result  = new FbxExportResult { OutputPath = outputPath };

                var pre = new HierarchyBuildResult();
                builder.WarnAboutExpectations(model, pre);
                result.Warnings.AddRange(pre.Warnings);

                built = builder.Build(model);
                if (built?.Root == null)
                    return FbxExportResult.Failed("ヒエラルキー生成に失敗しました");
                built.Root.hideFlags = HideFlags.HideAndDontSave;
                result.Warnings.AddRange(built.Warnings);

                string dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string texFolder = string.IsNullOrEmpty(settings.TextureFolder)
                    ? FbxExportSettings.DefaultTextureFolder(outputPath)
                    : settings.TextureFolder;

                // ------------------------------------------------------------
                // 2. FbxScene を組む
                // ------------------------------------------------------------
                manager = FbxManager.Create();
                var ios = FbxIOSettings.Create(manager, Globals.IOSROOT);
                manager.SetIOSettings(ios);

                var scene = FbxScene.Create(manager, "Scene");
                var info  = FbxDocumentInfo.Create(manager, "SceneInfo");
                info.mTitle = model.Name ?? "";
                info.Original_ApplicationName.Set("PolyLing");
                info.LastSaved_ApplicationName.Set("PolyLing");
                scene.SetSceneInfo(info);

                var globals = scene.GetGlobalSettings();
                globals.SetSystemUnit(FbxSystemUnit.cm);
                globals.SetAxisSystem(FbxAxisSystem.DirectX);

                bool includeMesh = motion == null || motion.IncludeMesh;
                var ctx = new SceneBuild(model, scene, texFolder, result, includeMesh);
                ctx.Build(built.Root, built.SourceMeshObjectOf);
                if (samples != null)
                    ctx.AddAnimation(samples, built, string.IsNullOrEmpty(motion?.TakeName) ? "Take 001" : motion.TakeName);

                // Unity（左手系）→ 右手系（Y 上）。全部組んでから最後に掛ける。
                FbxAxisSystem.MayaYUp.DeepConvertScene(scene);

                // ------------------------------------------------------------
                // 3. 書き出し
                // ------------------------------------------------------------
                bool embed = settings.EmbedTextures && !settings.Ascii;
                ios.SetBoolProp(Globals.EXP_FBX_EMBEDDED, embed);

                int format = -1;
                if (settings.Ascii)
                {
                    format = manager.GetIOPluginRegistry().FindWriterIDByDescription("FBX ascii (*.fbx)");
                    if (format < 0) return FbxExportResult.Failed("FBX の ASCII 書き出しが見つかりません");
                }

                var exporter = FbxExporter.Create(manager, "Exporter");
                if (!exporter.Initialize(outputPath, format, ios))
                    return FbxExportResult.Failed("FBX の書き出しを始められません: " + outputPath);
                if (!exporter.Export(scene))
                    return FbxExportResult.Failed("FBX の書き出しに失敗しました: " + outputPath);

                result.Success       = true;
                result.TextureFolder = ctx.WroteTextures ? texFolder : "";

                foreach (string w in result.Warnings)
                    Debug.LogWarning($"[FbxExporter] {w}");
                Debug.Log($"[FbxExporter] Export successful: {result.Summary()} → {outputPath}");
                return result;
            }
            catch (Exception ex)
            {
                // ネイティブ DLL（UnityFbxSdkNative）が無いと型の初期化で例外になる。
                string msg = ex.InnerException?.Message ?? ex.Message;
                Debug.LogError($"[FbxExporter] Export failed: {msg}\n{ex.StackTrace}");
                return FbxExportResult.Failed(msg);
            }
            finally
            {
                manager?.Destroy();
                DestroyHierarchy(built?.Root);
            }
        }

        // ================================================================
        // シーン組み立て
        // ================================================================

        private sealed class SceneBuild
        {
            private readonly ModelContext    _model;
            private readonly FbxScene        _scene;
            private readonly string          _texFolder;
            private readonly FbxExportResult _result;
            private readonly bool            _includeMesh;

            private readonly Dictionary<Transform, FbxNode>   _nodeOf  = new Dictionary<Transform, FbxNode>();
            private readonly Dictionary<Transform, Matrix4x4> _worldOf = new Dictionary<Transform, Matrix4x4>();
            /// <summary>書き出し上のローカル（親のワールド⁻¹ · 自分のワールド）。モーションの無いノードはこのまま。</summary>
            private readonly Dictionary<Transform, Matrix4x4> _localOf = new Dictionary<Transform, Matrix4x4>();
            /// <summary>ノードを作った順（親が子より前）。</summary>
            private readonly List<Transform> _order = new List<Transform>();
            /// <summary>（レンダラの Transform, Unity のブレンドシェイプ番号）→ FBX のチャンネル。</summary>
            private readonly Dictionary<(Transform, int), FbxBlendShapeChannel> _channelOf
                = new Dictionary<(Transform, int), FbxBlendShapeChannel>();
            private readonly Dictionary<Transform, Matrix4x4> _bindWorldOfBone = new Dictionary<Transform, Matrix4x4>();
            private readonly HashSet<string> _usedNodeNames = new HashSet<string>(StringComparer.Ordinal);

            private readonly Dictionary<Material, MaterialReference> _refOfMaterial = new Dictionary<Material, MaterialReference>();
            private readonly Dictionary<object, FbxSurfaceMaterial>   _fbxMaterialOf = new Dictionary<object, FbxSurfaceMaterial>();
            private readonly HashSet<string> _usedMaterialNames = new HashSet<string>(StringComparer.Ordinal);

            private readonly Dictionary<(MaterialReference, ModelTextureTransfer.TextureSlot), ModelTextureTransfer.Entry> _textureOf
                = new Dictionary<(MaterialReference, ModelTextureTransfer.TextureSlot), ModelTextureTransfer.Entry>();
            private readonly Dictionary<ModelTextureTransfer.Entry, string> _writtenTexture
                = new Dictionary<ModelTextureTransfer.Entry, string>();

            public bool WroteTextures => _writtenTexture.Count > 0;

            public SceneBuild(ModelContext model, FbxScene scene, string texFolder, FbxExportResult result, bool includeMesh)
            {
                _model       = model;
                _scene       = scene;
                _texFolder   = texFolder;
                _result      = result;
                _includeMesh = includeMesh;

                if (model.MaterialReferences != null)
                    foreach (var r in model.MaterialReferences)
                    {
                        // HierarchyBuilder.BuildMaterials が同じ getter で取得済み（キャッシュ）なので同じ実体になる。
                        var m = r?.Material;
                        if (m != null && !_refOfMaterial.ContainsKey(m)) _refOfMaterial[m] = r;
                    }

                foreach (var e in ModelTextureTransfer.Collect(model))
                    foreach (var u in e.Uses)
                        if (!_textureOf.ContainsKey((u.Material, u.Slot))) _textureOf[(u.Material, u.Slot)] = e;
            }

            private IReadOnlyDictionary<Transform, MeshObject> _sourceOf;

            public void Build(GameObject root, IReadOnlyDictionary<Transform, MeshObject> sourceOf)
            {
                _sourceOf = sourceOf;
                // ボーンのバインド時のワールド（SMR のワールド · bindpose⁻¹）。最初に見つかった SMR を使う。
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var bones = smr.bones;
                    var binds = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
                    if (bones == null || binds == null || binds.Length != bones.Length) continue;
                    var meshWorld = smr.transform.localToWorldMatrix;
                    for (int i = 0; i < bones.Length; i++)
                        if (bones[i] != null && !_bindWorldOfBone.ContainsKey(bones[i]))
                            _bindWorldOfBone[bones[i]] = meshWorld * binds[i].inverse;
                }

                // 1 巡目：ノードと姿勢（クラスタはノードのグローバルを読むので先に全部置く）
                CreateNode(root.transform, _scene.GetRootNode(), Matrix4x4.identity);

                // 2 巡目：メッシュ・スキン・ブレンドシェイプ（骨格だけのときは書かない）
                if (_includeMesh)
                    foreach (var t in _order)
                    {
                        var go = t.gameObject;
                        if (go.TryGetComponent<SkinnedMeshRenderer>(out var smr) && smr.sharedMesh != null)
                            ExportMesh(t, _nodeOf[t], smr.sharedMesh, smr.sharedMaterials, smr);
                        else if (go.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null)
                        {
                            var mr = go.GetComponent<MeshRenderer>();
                            ExportMesh(t, _nodeOf[t], mf.sharedMesh, mr != null ? mr.sharedMaterials : null, null);
                        }
                    }

                _result.NodeCount     = _nodeOf.Count;
                _result.BoneCount     = _bindWorldOfBone.Count;
                _result.MaterialCount = _fbxMaterialOf.Count;
                _result.TextureCount  = _writtenTexture.Count;
            }

            // ------------------------------------------------------------
            // ノード
            // ------------------------------------------------------------

            private void CreateNode(Transform t, FbxNode parentNode, Matrix4x4 parentWorld)
            {
                var local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
                var world = _bindWorldOfBone.TryGetValue(t, out var bind) ? bind : parentWorld * local;
                var exportLocal = parentWorld.inverse * world;

                var node = FbxNode.Create(_scene, UniqueNodeName(t.name));
                node.SetTransformationInheritType(FbxTransform.EInheritType.eInheritRSrs);
                node.SetRotationOrder(FbxNode.EPivotSet.eSourcePivot, FbxEuler.EOrder.eOrderZXY);
                node.SetRotationActive(true);

                Vector3    p = exportLocal.GetColumn(3);
                Quaternion r = exportLocal.rotation;
                Vector3    s = exportLocal.lossyScale;
                Vector3    e = r.eulerAngles;
                node.LclTranslation.Set(new FbxDouble3(p.x * UnitScale, p.y * UnitScale, p.z * UnitScale));
                node.LclRotation.Set(new FbxDouble3(e.x, e.y, e.z));
                // 尺度 0 は子の尺度が壊れる DCC があるので 1e-12 にする（FBX Exporter と同じ）。
                node.LclScaling.Set(new FbxDouble3(NonZero(s.x), NonZero(s.y), NonZero(s.z)));

                if (_bindWorldOfBone.ContainsKey(t))
                {
                    var skel = FbxSkeleton.Create(_scene, node.GetName() + "_Skeleton");
                    bool parentIsBone = t.parent != null && _bindWorldOfBone.ContainsKey(t.parent);
                    skel.SetSkeletonType(parentIsBone ? FbxSkeleton.EType.eLimbNode : FbxSkeleton.EType.eRoot);
                    skel.Size.Set(UnitScale);
                    node.SetNodeAttribute(skel);
                }

                parentNode.AddChild(node);
                _nodeOf[t]  = node;
                _worldOf[t] = world;
                _localOf[t] = exportLocal;
                _order.Add(t);

                for (int i = 0; i < t.childCount; i++)
                    CreateNode(t.GetChild(i), node, world);
            }

            private string UniqueNodeName(string name)
            {
                string n = string.IsNullOrEmpty(name) ? "Node" : name;
                if (_usedNodeNames.Add(n)) return n;
                for (int i = 1; ; i++)
                {
                    string c = $"{n}_{i}";
                    if (_usedNodeNames.Add(c)) return c;
                }
            }

            private static double NonZero(float v) => v == 0f ? 1e-12 : v;

            // ------------------------------------------------------------
            // メッシュ
            // ------------------------------------------------------------

            private void ExportMesh(Transform renderer, FbxNode node, Mesh mesh, Material[] materials, SkinnedMeshRenderer smr)
            {
                int vCount = mesh.vertexCount;
                if (vCount == 0) return;

                var verts   = mesh.vertices;
                var normals = mesh.normals;
                var weights = mesh.boneWeights;
                bool hasWeights = smr != null && weights != null && weights.Length == vCount;

                // ブレンドシェイプの差分（フレームごと）
                var frames = new List<(string name, float weight, Vector3[] dPos, Vector3[] dNrm)>();
                for (int bi = 0; bi < mesh.blendShapeCount; bi++)
                {
                    string bsName = mesh.GetBlendShapeName(bi);
                    int fc = mesh.GetBlendShapeFrameCount(bi);
                    for (int fi = 0; fi < fc; fi++)
                    {
                        var dp = new Vector3[vCount];
                        var dn = new Vector3[vCount];
                        mesh.GetBlendShapeFrameVertices(bi, fi, dp, dn, null);
                        frames.Add((fc > 1 ? $"{bsName}_{fi}" : bsName, mesh.GetBlendShapeFrameWeight(bi, fi), dp, dn));
                    }
                }

                // ── 制御点（位置・ウェイト・全差分が一致するものを 1 つに） ──
                var cpOf = new int[vCount];
                var firstOfCp = new List<int>();
                var map = new Dictionary<int, int>(new ControlPointComparer(verts, hasWeights ? weights : null, frames));
                for (int v = 0; v < vCount; v++)
                {
                    if (!map.TryGetValue(v, out int cp))
                    {
                        cp = firstOfCp.Count;
                        map[v] = cp;
                        firstOfCp.Add(v);
                    }
                    cpOf[v] = cp;
                }

                var fbxMesh = FbxMesh.Create(_scene, node.GetName());
                fbxMesh.InitControlPoints(firstOfCp.Count);
                for (int cp = 0; cp < firstOfCp.Count; cp++)
                    fbxMesh.SetControlPointAt(ToFbxPosition(verts[firstOfCp[cp]]), cp);

                // ── 面（サブメッシュ順。面ごとの材質番号 = サブメッシュ番号） ──
                //   元の MeshObject から多角形を組み直せればそれ、できなければ Unity メッシュの三角形。
                var corners = new List<int>();          // 面の頂点ごとの Unity 頂点番号
                var polyMaterial = new List<int>();
                MeshObject source = null;
                _sourceOf?.TryGetValue(renderer, out source);
                if (!SourcePolygons.TryBuild(source, mesh, out var polygons, out string polyReason))
                {
                    if (source != null)
                        _result.Warnings.Add($"\"{renderer.name}\" は元の多角形に組み直せないため三角形で書きます: {polyReason}");
                    polygons = SourcePolygons.Triangles(mesh);
                }
                foreach (var (sub, polyCorners) in polygons)
                {
                    fbxMesh.BeginPolygon();
                    foreach (int uv in polyCorners)
                    {
                        corners.Add(uv);
                        fbxMesh.AddPolygon(cpOf[uv]);
                    }
                    fbxMesh.EndPolygon();
                    polyMaterial.Add(sub);
                }

                var layer = fbxMesh.GetLayer(0);
                if (layer == null) { fbxMesh.CreateLayer(); layer = fbxMesh.GetLayer(0); }

                // ── 法線（面の頂点ごと） ──
                if (normals != null && normals.Length == vCount)
                {
                    var elem = FbxLayerElementNormal.Create(fbxMesh, "Normals");
                    elem.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
                    elem.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                    var arr = elem.GetDirectArray();
                    foreach (int v in corners) arr.Add(ToFbxDirection(normals[v]));
                    layer.SetNormals(elem);
                }

                // ── UV（チャンネルごとに別レイヤー。中身は Unity 頂点、索引は面の頂点ごと） ──
                var uvs = new List<Vector2>();
                int uvLayer = 0;
                for (int ch = 0; ch < 8; ch++)
                {
                    mesh.GetUVs(ch, uvs);
                    if (uvs.Count != vCount) continue;

                    while (fbxMesh.GetLayerCount() <= uvLayer) fbxMesh.CreateLayer();
                    var l = fbxMesh.GetLayer(uvLayer);
                    var elem = FbxLayerElementUV.Create(fbxMesh, "UVSet" + ch);
                    elem.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
                    elem.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
                    var direct = elem.GetDirectArray();
                    foreach (var uv in uvs) direct.Add(new FbxVector2(uv.x, uv.y));
                    var index = elem.GetIndexArray();
                    index.SetCount(corners.Count);
                    for (int i = 0; i < corners.Count; i++) index.SetAt(i, corners[i]);
                    l.SetUVs(elem, FbxLayerElement.EType.eTextureDiffuse);
                    uvLayer++;
                }

                // ── 材質 ──
                int matCount = Math.Max(1, materials?.Length ?? 0);
                for (int m = 0; m < matCount; m++)
                    node.AddMaterial(GetMaterial(materials != null && m < materials.Length ? materials[m] : null));
                {
                    var elem = FbxLayerElementMaterial.Create(fbxMesh, "Material");
                    elem.SetReferenceMode(FbxLayerElement.EReferenceMode.eIndexToDirect);
                    var index = elem.GetIndexArray();
                    if (matCount == 1)
                    {
                        elem.SetMappingMode(FbxLayerElement.EMappingMode.eAllSame);
                        index.Add(0);
                    }
                    else
                    {
                        elem.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygon);
                        foreach (int pm in polyMaterial) index.Add(Math.Min(pm, matCount - 1));
                    }
                    layer.SetMaterials(elem);
                }

                // ── ブレンドシェイプ ──
                if (frames.Count > 0)
                {
                    var bs = FbxBlendShape.Create(_scene, node.GetName() + "_BlendShape");
                    fbxMesh.AddDeformer(bs);
                    int bi = -1;
                    for (int b = 0; b < mesh.blendShapeCount; b++)
                    {
                        string bsName = mesh.GetBlendShapeName(b);
                        var ch = FbxBlendShapeChannel.Create(_scene, bsName);
                        bs.AddBlendShapeChannel(ch);
                        _channelOf[(renderer, b)] = ch;
                        int fc = mesh.GetBlendShapeFrameCount(b);
                        for (int fi = 0; fi < fc; fi++)
                        {
                            bi++;
                            var fr = frames[bi];
                            var shape = FbxShape.Create(_scene, fr.name);
                            ch.AddTargetShape(shape, fr.weight);
                            shape.InitControlPoints(firstOfCp.Count);
                            for (int cp = 0; cp < firstOfCp.Count; cp++)
                            {
                                int v = firstOfCp[cp];
                                shape.SetControlPointAt(ToFbxPosition(verts[v] + fr.dPos[v]), cp);
                            }
                            if (normals != null && normals.Length == vCount)
                            {
                                var en = shape.CreateElementNormal();
                                en.SetMappingMode(FbxLayerElement.EMappingMode.eByPolygonVertex);
                                en.SetReferenceMode(FbxLayerElement.EReferenceMode.eDirect);
                                var dst = en.GetDirectArray();
                                dst.SetCount(corners.Count);
                                for (int i = 0; i < corners.Count; i++)
                                {
                                    int v = corners[i];
                                    dst.SetAt(i, ToFbxDirection(normals[v] + fr.dNrm[v]));
                                }
                            }
                        }
                    }
                    _result.BlendShapeCount += mesh.blendShapeCount;
                }

                node.SetNodeAttribute(fbxMesh);

                // ── スキンとバインドポーズ ──
                if (hasWeights) ExportSkin(node, fbxMesh, smr, weights, firstOfCp);

                _result.MeshCount++;
                _result.ControlPointCount += firstOfCp.Count;
                _result.PolygonCount      += polyMaterial.Count;
            }

            private void ExportSkin(FbxNode meshNode, FbxMesh fbxMesh, SkinnedMeshRenderer smr,
                                    BoneWeight[] weights, List<int> firstOfCp)
            {
                var bones = smr.bones;
                if (bones == null || bones.Length == 0) return;

                var skin = FbxSkin.Create(_scene, meshNode.GetName() + "_Skin");
                var meshGlobal = meshNode.EvaluateGlobalTransform();
                var clusters = new FbxCluster[bones.Length];

                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null || !_nodeOf.TryGetValue(bones[i], out var boneNode)) continue;
                    var c = FbxCluster.Create(_scene, boneNode.GetName() + "_Cluster");
                    c.SetLink(boneNode);
                    c.SetLinkMode(FbxCluster.ELinkMode.eNormalize);
                    c.SetTransformMatrix(meshGlobal);
                    c.SetTransformLinkMatrix(boneNode.EvaluateGlobalTransform());
                    skin.AddCluster(c);
                    clusters[i] = c;
                }

                for (int cp = 0; cp < firstOfCp.Count; cp++)
                {
                    var w = weights[firstOfCp[cp]];
                    AddWeight(clusters, w.boneIndex0, w.weight0, cp);
                    AddWeight(clusters, w.boneIndex1, w.weight1, cp);
                    AddWeight(clusters, w.boneIndex2, w.weight2, cp);
                    AddWeight(clusters, w.boneIndex3, w.weight3, cp);
                }

                fbxMesh.AddDeformer(skin);

                var pose = FbxPose.Create(_scene, meshNode.GetName() + "_BindPose");
                pose.SetIsBindPose(true);
                for (int i = 0; i < bones.Length; i++)
                    if (clusters[i] != null)
                    {
                        var bn = _nodeOf[bones[i]];
                        pose.Add(bn, new FbxMatrix(bn.EvaluateGlobalTransform()));
                    }
                pose.Add(meshNode, new FbxMatrix(meshGlobal));
                _scene.AddPose(pose);

                _result.SkinnedMeshCount++;
            }

            private static void AddWeight(FbxCluster[] clusters, int bone, float weight, int cp)
            {
                if (weight <= 0f || bone < 0 || bone >= clusters.Length || clusters[bone] == null) return;
                clusters[bone].AddControlPointIndex(cp, weight);
            }

            // ------------------------------------------------------------
            // アニメーション（ファイル冒頭【アニメーション】）
            // ------------------------------------------------------------

            public void AddAnimation(ModelMotionSamples s, HierarchyBuildResult built, string takeName)
            {
                _result.HasMotion               = true;
                _result.FrameCount              = s.FrameCount;
                _result.DurationSec             = s.DurationSec;
                _result.MatchedTrackCount       = s.MatchedTrackCount;
                _result.RootMotion              = s.RootMotion;
                _result.ExpressionTrackCount    = s.ExpressionTrackCount;
                _result.ExpressionResolvedCount = s.ExpressionResolvedCount;

                // ── 関節 → FBX のノード（仮想骨格のノードの ContextIndex = HierarchyBuilder のボーン索引）──
                var jointOf = new Dictionary<Transform, int>();
                for (int j = 0; j < s.JointNode.Count; j++)
                {
                    int ci = s.Skeleton.Nodes[s.JointNode[j]].ContextIndex;
                    if (ci >= 0 && built.BoneTransformByIndex.TryGetValue(ci, out var tf) && tf != null && _nodeOf.ContainsKey(tf))
                        jointOf[tf] = j;
                    else
                        _result.SkippedVirtualJointCount++;
                }
                _result.AnimatedNodeCount = jointOf.Count;

                // ── テイク ──
                var timeMode = TimeModeOf(s.Fps);
                _scene.GetGlobalSettings().SetTimeMode(timeMode);
                var stack = FbxAnimStack.Create(_scene, takeName);
                var layer = FbxAnimLayer.Create(_scene, "Base Layer");
                stack.AddMember(layer);
                stack.SetLocalTimeSpan(new FbxTimeSpan(FbxTime.FromSecondDouble(0), FbxTime.FromSecondDouble(s.DurationSec)));
                _scene.SetCurrentAnimationStack(stack);

                int nF = s.FrameCount;
                var times = new FbxTime[nF];
                for (int f = 0; f < nF; f++) times[f] = FbxTime.FromSecondDouble(f / (double)s.Fps);

                // ── 関節のノードごとの曲線（移動 X/Y/Z・回転 X/Y/Z）──
                var curves = new Dictionary<Transform, FbxAnimCurve[]>();
                foreach (var kv in jointOf)
                {
                    var n = _nodeOf[kv.Key];
                    var c = new FbxAnimCurve[6];
                    c[0] = n.LclTranslation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_X, true);
                    c[1] = n.LclTranslation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_Y, true);
                    c[2] = n.LclTranslation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_Z, true);
                    c[3] = n.LclRotation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_X, true);
                    c[4] = n.LclRotation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_Y, true);
                    c[5] = n.LclRotation.GetCurve(layer, Globals.FBXSDK_CURVENODE_COMPONENT_Z, true);
                    foreach (var cv in c) cv.KeyModifyBegin();
                    curves[kv.Key] = c;
                }

                // ── フレームごとに全ノードのワールドを組み、関節のローカルを鍵にする ──
                var worldF    = new Dictionary<Transform, Matrix4x4>();
                var prevEuler = new Dictionary<Transform, Vector3>();
                for (int f = 0; f < nF; f++)
                {
                    worldF.Clear();
                    foreach (var t in _order)
                    {
                        var parentW = t.parent != null && worldF.TryGetValue(t.parent, out var pw) ? pw : Matrix4x4.identity;
                        Matrix4x4 w;
                        if (jointOf.TryGetValue(t, out int j))
                        {
                            // バインドのワールドに、標本のワールド差分（回転）と位置の動きを足す。
                            var bw = _worldOf[t];
                            Vector3    bp = bw.GetColumn(3);
                            Quaternion br = bw.rotation;
                            Vector3    bs = bw.lossyScale;
                            w = Matrix4x4.TRS(bp + (s.Pos[f, j] - s.RestPos[j]), s.Delta[f, j] * br, bs);
                        }
                        else
                        {
                            w = parentW * _localOf[t];
                        }
                        worldF[t] = w;

                        if (!curves.TryGetValue(t, out var c)) continue;
                        var local = parentW.inverse * w;
                        Vector3 p = local.GetColumn(3);
                        Vector3 e = ContinuousEuler(local.rotation,
                                                    prevEuler.TryGetValue(t, out var pe) ? pe : (Vector3?)null);
                        prevEuler[t] = e;
                        Key(c[0], times[f], p.x * UnitScale);
                        Key(c[1], times[f], p.y * UnitScale);
                        Key(c[2], times[f], p.z * UnitScale);
                        Key(c[3], times[f], e.x);
                        Key(c[4], times[f], e.y);
                        Key(c[5], times[f], e.z);
                    }
                }
                foreach (var c in curves.Values) foreach (var cv in c) cv.KeyModifyEnd();

                // ── 表情 → ブレンドシェイプの重み（DeformPercent は 0〜100）──
                foreach (var slot in built.MorphShapes)
                {
                    if (!s.MorphWeights.TryGetValue(slot.MorphContextIndex, out var weights)) continue;
                    if (!_channelOf.TryGetValue((slot.Renderer, slot.ShapeIndex), out var ch)) continue;
                    var cv = ch.DeformPercent.GetCurve(layer, true);
                    cv.KeyModifyBegin();
                    for (int f = 0; f < nF; f++) Key(cv, times[f], weights[f] * 100f);
                    cv.KeyModifyEnd();
                    _result.AnimatedBlendShapeCount++;
                }
            }

            private static void Key(FbxAnimCurve c, FbxTime t, double v) => Key(c, t, (float)v);

            private static void Key(FbxAnimCurve c, FbxTime t, float v)
            {
                int k = c.KeyAdd(t);
                c.KeySet(k, t, v, FbxAnimCurveDef.EInterpolationType.eInterpolationLinear);
            }

            /// <summary>
            /// Unity のオイラー角（ZXY 順。ノードの回転順と同じ）を、前のフレームに近い解で返す。
            /// 同じ回転を表す 2 通り (x, y, z) と (180 − x, y + 180, z + 180) のうち、
            /// 360° 単位でそろえて前のフレームに近い方を選ぶ（補間で 1 周回らないように）。
            /// </summary>
            private static Vector3 ContinuousEuler(Quaternion q, Vector3? prev)
            {
                Vector3 a = q.eulerAngles;
                if (prev == null) return a;
                Vector3 p = prev.Value;
                Vector3 b = new Vector3(180f - a.x, a.y + 180f, a.z + 180f);
                a = new Vector3(Near(a.x, p.x), Near(a.y, p.y), Near(a.z, p.z));
                b = new Vector3(Near(b.x, p.x), Near(b.y, p.y), Near(b.z, p.z));
                return (a - p).sqrMagnitude <= (b - p).sqrMagnitude ? a : b;
            }

            private static float Near(float a, float prev) => a + 360f * Mathf.Round((prev - a) / 360f);

            /// <summary>fps に合う FBX の時間モード（FBX Exporter と同じ探し方。見つからなければ 30 fps）。</summary>
            private static FbxTime.EMode TimeModeOf(float fps)
            {
                var mode = FbxTime.EMode.eCustom;
                double precision = 1e-6;
                while (mode == FbxTime.EMode.eCustom && precision < 1000)
                {
                    mode = FbxTime.ConvertFrameRateToTimeMode(fps, precision);
                    precision *= 10;
                }
                return mode == FbxTime.EMode.eCustom ? FbxTime.EMode.eFrames30 : mode;
            }

            private static FbxVector4 ToFbxPosition(Vector3 v) => new FbxVector4(v.x * UnitScale, v.y * UnitScale, v.z * UnitScale);
            private static FbxVector4 ToFbxDirection(Vector3 v) => new FbxVector4(v.x, v.y, v.z);

            // ------------------------------------------------------------
            // 材質
            // ------------------------------------------------------------

            private FbxSurfaceMaterial GetMaterial(Material unityMat)
            {
                MaterialReference matRef = null;
                if (unityMat != null) _refOfMaterial.TryGetValue(unityMat, out matRef);
                object key = (object)matRef ?? (object)unityMat ?? "Default";
                if (_fbxMaterialOf.TryGetValue(key, out var existing)) return existing;

                var d = matRef?.Data;
                string name = UniqueMaterialName(d?.Name ?? (unityMat != null ? unityMat.name : "Default"));
                var lambert = FbxSurfaceLambert.Create(_scene, name);

                float[] bc = d?.BaseColor;
                double r = bc != null && bc.Length > 0 ? bc[0] : 0.8;
                double g = bc != null && bc.Length > 1 ? bc[1] : 0.8;
                double b = bc != null && bc.Length > 2 ? bc[2] : 0.8;
                double a = bc != null && bc.Length > 3 ? bc[3] : 1.0;
                lambert.Diffuse.Set(new FbxDouble3(r, g, b));
                lambert.DiffuseFactor.Set(1.0);
                lambert.Ambient.Set(new FbxDouble3(0, 0, 0));
                lambert.TransparentColor.Set(new FbxDouble3(1, 1, 1));
                lambert.TransparencyFactor.Set(1.0 - a);

                if (d != null && d.EmissionEnabled && d.EmissionColor != null && d.EmissionColor.Length >= 3)
                {
                    lambert.Emissive.Set(new FbxDouble3(d.EmissionColor[0], d.EmissionColor[1], d.EmissionColor[2]));
                    lambert.EmissiveFactor.Set(1.0);
                }
                else
                {
                    lambert.Emissive.Set(new FbxDouble3(0, 0, 0));
                }
                lambert.BumpFactor.Set(d != null ? d.NormalScale : 1.0);

                if (matRef != null)
                {
                    ConnectTexture(lambert, FbxSurfaceMaterial.sDiffuse,   matRef, ModelTextureTransfer.TextureSlot.Base,     d.BaseMapST);
                    ConnectTexture(lambert, FbxSurfaceMaterial.sNormalMap, matRef, ModelTextureTransfer.TextureSlot.Normal,   d.NormalMapST);
                    ConnectTexture(lambert, FbxSurfaceMaterial.sEmissive,  matRef, ModelTextureTransfer.TextureSlot.Emission, d.EmissionMapST);
                }

                _fbxMaterialOf[key] = lambert;
                return lambert;
            }

            private string UniqueMaterialName(string name)
            {
                string n = string.IsNullOrEmpty(name) ? "Material" : name;
                if (_usedMaterialNames.Add(n)) return n;
                for (int i = 1; ; i++)
                {
                    string c = $"{n}_{i}";
                    if (_usedMaterialNames.Add(c)) return c;
                }
            }

            private void ConnectTexture(FbxSurfaceMaterial mat, string propName, MaterialReference matRef,
                                        ModelTextureTransfer.TextureSlot slot, float[] st)
            {
                if (!_textureOf.TryGetValue((matRef, slot), out var entry)) return;

                if (!_writtenTexture.TryGetValue(entry, out string fullPath))
                {
                    Directory.CreateDirectory(_texFolder);
                    fullPath = Path.GetFullPath(Path.Combine(_texFolder, entry.FileName));
                    File.WriteAllBytes(fullPath, entry.Data);
                    _writtenTexture[entry] = fullPath;
                }

                var prop = mat.FindProperty(propName);
                if (prop == null || !prop.IsValid()) return;

                var tex = FbxFileTexture.Create(mat, Path.GetFileNameWithoutExtension(entry.FileName) + "_" + propName);
                tex.SetFileName(fullPath);
                tex.SetRelativeFileName(Path.GetFileName(_texFolder) + "/" + entry.FileName);
                tex.SetTextureUse(FbxTexture.ETextureUse.eStandard);
                tex.SetMappingType(FbxTexture.EMappingType.eUV);
                tex.SetWrapMode(FbxTexture.EWrapMode.eRepeat, FbxTexture.EWrapMode.eRepeat);
                if (st != null && st.Length >= 4)
                {
                    tex.SetScale(st[0], st[1]);
                    tex.SetTranslation(st[2], st[3]);
                }
                tex.ConnectDstProperty(prop);
            }
        }

        // ================================================================
        // 制御点の名寄せ（位置・ウェイト・全ブレンドシェイプ差分の完全一致）
        // ================================================================

        private sealed class ControlPointComparer : IEqualityComparer<int>
        {
            private readonly Vector3[]    _pos;
            private readonly BoneWeight[] _w;
            private readonly List<(string name, float weight, Vector3[] dPos, Vector3[] dNrm)> _frames;

            public ControlPointComparer(Vector3[] pos, BoneWeight[] w,
                                        List<(string name, float weight, Vector3[] dPos, Vector3[] dNrm)> frames)
            {
                _pos = pos; _w = w; _frames = frames;
            }

            public bool Equals(int a, int b)
            {
                if (!Same(_pos[a], _pos[b])) return false;
                if (_w != null)
                {
                    var x = _w[a]; var y = _w[b];
                    if (x.boneIndex0 != y.boneIndex0 || x.weight0 != y.weight0 ||
                        x.boneIndex1 != y.boneIndex1 || x.weight1 != y.weight1 ||
                        x.boneIndex2 != y.boneIndex2 || x.weight2 != y.weight2 ||
                        x.boneIndex3 != y.boneIndex3 || x.weight3 != y.weight3) return false;
                }
                foreach (var f in _frames)
                    if (!Same(f.dPos[a], f.dPos[b])) return false;
                return true;
            }

            public int GetHashCode(int v)
            {
                var p = _pos[v];
                int h = p.x.GetHashCode();
                h = h * 31 + p.y.GetHashCode();
                h = h * 31 + p.z.GetHashCode();
                return h;
            }

            // Vector3 の == は誤差を許すので、成分ごとに完全一致で比べる。
            private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        }

        // ================================================================
        // 後始末（GltfExporterImpl と同じ）
        // ================================================================

        /// <summary>書き出し用の階層と Mesh を破棄する（Mesh は GameObject を消しても解放されない）。</summary>
        private static void DestroyHierarchy(GameObject root)
        {
            if (root == null) return;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                DestroyObject(smr.sharedMesh);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                DestroyObject(mf.sharedMesh);
            DestroyObject(root);
        }

        private static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else                       UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
