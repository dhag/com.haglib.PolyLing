// FbxModelWriter.cs
// ============================================================
// HierarchyBuilder の結果（Transform 階層＋形状の出どころ）→ FBX のノード木
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 7（7.4 書き出しの規則）。
//
// ■ 座標
//   ファイルは右手系・Y 上・前 +Z・単位 cm（GlobalSettings の UnitScaleFactor 1）。
//   Unity の値から S = diag(−1, 1, 1) で写す：点 100·S·p、方向 S·n、行列 S·M·S（移動は ×100）。
//   面の角の並びは反転する（鏡映で表裏が入れ替わる）。FbxImporter（読み込み）の逆。
//
// ■ ノード
//   置く姿勢は段 2 と同じ：ボーンはバインドのワールド（スキンドの描画ノードのワールド · bindpose⁻¹。
//   最初に見つかったもの）、ほかは親の書き出し上のワールド · ローカル TRS。
//   書き出し上のローカル = 親のワールド⁻¹ · ワールド を S で写し、移動・回転・拡大に分ける。
//   回転は FBX 既定の回転順 XYZ（R = Rz·Ry·Rx）のオイラー角。拡大 0 は 1e-12。継承は RSrs。
//
// ■ メッシュ（HierarchyMeshSource）
//   制御点 = Shape の Vertex（＋VertexOffset）。面は Faces の順で、表示中・3 頂点以上の面を多角形のまま。
//   角の UV・法線のスロットの選び方は MeshBridgeDefault.ToUnityMesh と同じ（範囲外はスロット 0、無ければ
//   UV (0,0)・法線 (0,1,0)）。ToUnityMesh が法線を Unity に計算させる条件（PreserveNormals でなく、角の法線が
//   全部 (0,1,0)）のときは法線の層を書かない（読む側が計算する）。
//   材質は面の材質番号を 0〜Materials.Length−1 に丸め、面のある材質だけを番号順に付ける。
//   スキンはボーンごとのクラスタ（HierarchyBuilder.RemapBoneWeight で写したウェイト）とバインドポーズ。
//   クラスタの Transform = ボーンのバインドワールド⁻¹ · メッシュのワールド（メッシュ → ボーン）、
//   TransformLink = ボーンのバインドワールド（どちらもファイル座標）。
//   ブレンドシェイプは Morphs ごとに BlendShapeChannel と Shape（差分。頂点番号の昇順、同じ番号は足す）。
//
// ■ アニメーション（段 3 と同じ標本化）
//   各フレームの関節のワールド = 移動(バインド位置 + (標本の位置 − レスト位置))·回転(標本の回転差分 · バインド回転)·バインド尺度。
//   関節でないノードは親のワールド · 書き出し上のローカル。曲線は関節ノードの Lcl Translation / Lcl Rotation に
//   毎フレームの線形キー（オイラー角は同じ回転の 2 通りのうち前のフレームに近い方）。
//   表情は標本の重みを BlendShapeChannel の DeformPercent（0〜100）へ。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Materials;
using Poly_Ling.Motion;
using Poly_Ling.Ops;
using Poly_Ling.Serialization;

namespace Poly_Ling.Fbx
{
    internal sealed class FbxModelWriter
    {
        /// <summary>Unity の 1 m を FBX の cm にする倍率。</summary>
        private const double UnitScale = 100.0;

        /// <summary>FBX の時間の刻み（1 秒あたり）。</summary>
        private const long TicksPerSecond = 46186158000L;

        // FileId・CreationTime・フッタ ID は対になる値（FbxBinaryWriter.cs 冒頭）。
        private static readonly byte[] FileId =
        {
            0x2a, 0xb1, 0x29, 0xe9, 0xb4, 0x21, 0xcb, 0xc4, 0xbb, 0xc4, 0xbf, 0x2c, 0xaa, 0x2e, 0xfc, 0xf4,
        };
        private const string CreationTime = "2026-10-20 17:55:20:283";
        private const string Creator      = "PolyLing FBX writer";

        /// <summary>キーの重みの既定（ビット列 0x0D050D05 を float として書く）。</summary>
        private static readonly float DefaultWeights = BitConverter.ToSingle(BitConverter.GetBytes(0x0D050D05), 0);

        private readonly ModelContext    _model;
        private readonly string          _texFolder;
        private readonly bool            _embed;
        private readonly FbxExportResult _result;
        private readonly bool            _includeMesh;

        private readonly FbxNode _objects     = new FbxNode("Objects");
        private readonly FbxNode _connections = new FbxNode("Connections");
        private readonly Dictionary<string, int> _typeCount = new Dictionary<string, int>(StringComparer.Ordinal);
        private long _nextId = 1000000;

        private readonly Dictionary<Transform, long>      _idOf    = new Dictionary<Transform, long>();
        private readonly Dictionary<Transform, Matrix4x4> _worldOf = new Dictionary<Transform, Matrix4x4>();
        private readonly Dictionary<Transform, Matrix4x4> _localOf = new Dictionary<Transform, Matrix4x4>();
        private readonly List<Transform> _order = new List<Transform>();
        private readonly Dictionary<Transform, Matrix4x4> _bindWorldOfBone = new Dictionary<Transform, Matrix4x4>();
        private readonly HashSet<string> _usedNodeNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Transform, string> _nameOf = new Dictionary<Transform, string>();

        /// <summary>（描画ノード, ブレンドシェイプの並びの番号）→ BlendShapeChannel の ID。</summary>
        private readonly Dictionary<(Transform, int), long> _channelOf = new Dictionary<(Transform, int), long>();

        private readonly Dictionary<Material, MaterialReference> _refOfMaterial = new Dictionary<Material, MaterialReference>();
        private readonly Dictionary<object, long> _materialIdOf = new Dictionary<object, long>();
        private readonly HashSet<string> _usedMaterialNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<(MaterialReference, ModelTextureTransfer.TextureSlot), ModelTextureTransfer.Entry> _textureOf
            = new Dictionary<(MaterialReference, ModelTextureTransfer.TextureSlot), ModelTextureTransfer.Entry>();
        private readonly Dictionary<ModelTextureTransfer.Entry, (string full, long videoId)> _writtenTexture
            = new Dictionary<ModelTextureTransfer.Entry, (string, long)>();

        private HierarchyBuildResult _built;

        // テイク
        private string _takeName;
        private long   _takeStop;
        private float  _fps = 30f;

        public bool WroteTextures => _writtenTexture.Count > 0;

        public FbxModelWriter(ModelContext model, string texFolder, bool embed, FbxExportResult result, bool includeMesh)
        {
            _model       = model;
            _texFolder   = texFolder;
            _embed       = embed;
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

        // ================================================================
        // 組み立て
        // ================================================================

        public void Build(HierarchyBuildResult built)
        {
            _built = built;
            var root = built.Root.transform;

            // ボーンのバインド時のワールド（スキンドの描画ノードのワールド · bindpose⁻¹）。最初に見つかったもの。
            foreach (var t in Dfs(root))
            {
                if (!built.MeshSourceOf.TryGetValue(t, out var src) || !src.IsSkinned) continue;
                if (src.Bones == null || src.Bindposes == null || src.Bones.Length != src.Bindposes.Length) continue;
                var meshWorld = t.localToWorldMatrix;
                for (int i = 0; i < src.Bones.Length; i++)
                    if (src.Bones[i] != null && !_bindWorldOfBone.ContainsKey(src.Bones[i]))
                        _bindWorldOfBone[src.Bones[i]] = meshWorld * src.Bindposes[i].inverse;
            }

            // 1 巡目：ノードと姿勢（クラスタはノードのワールドを使うので先に全部置く）
            CreateNode(root, 0L, Matrix4x4.identity);

            // 2 巡目：メッシュ・スキン・ブレンドシェイプ
            if (_includeMesh)
                foreach (var t in _order)
                    if (built.MeshSourceOf.TryGetValue(t, out var src) && HasDrawableFace(src.Shape))
                        WriteMesh(t, src);

            _result.NodeCount     = _idOf.Count;
            _result.BoneCount     = _bindWorldOfBone.Count;
            _result.MaterialCount = _materialIdOf.Count;
            _result.TextureCount  = _writtenTexture.Count;
        }

        private static IEnumerable<Transform> Dfs(Transform t)
        {
            yield return t;
            for (int i = 0; i < t.childCount; i++)
                foreach (var c in Dfs(t.GetChild(i))) yield return c;
        }

        /// <summary>最上位のノードの並び（FbxBinaryWriter / FbxAsciiWriter へ渡す）。</summary>
        public List<FbxNode> Document(bool ascii)
        {
            var top = new List<FbxNode>();

            // FBXHeaderExtension
            var hx = new FbxNode("FBXHeaderExtension");
            hx.Add("FBXHeaderVersion", 1003);
            hx.Add("FBXVersion", FbxBinaryWriter.Version);
            hx.Add("EncryptionType", 0);
            var ts = hx.Add("CreationTimeStamp");
            ts.Add("Version", 1000);
            ts.Add("Year", 2026); ts.Add("Month", 10); ts.Add("Day", 20);
            ts.Add("Hour", 17); ts.Add("Minute", 55); ts.Add("Second", 20); ts.Add("Millisecond", 283);
            hx.Add("Creator", Creator);
            var si = hx.Add("SceneInfo", FbxNode.ObjectName("GlobalInfo", "SceneInfo"), "UserData");
            si.Add("Type", "UserData");
            si.Add("Version", 100);
            var md = si.Add("MetaData");
            md.Add("Version", 100);
            md.Add("Title", _model.Name ?? "");
            md.Add("Subject", ""); md.Add("Author", ""); md.Add("Keywords", "");
            md.Add("Revision", ""); md.Add("Comment", "");
            var sp = si.Add("Properties70");
            FbxProps.KString(sp, "Original|ApplicationVendor", "");
            FbxProps.KString(sp, "Original|ApplicationName", "PolyLing");
            FbxProps.KString(sp, "LastSaved|ApplicationVendor", "");
            FbxProps.KString(sp, "LastSaved|ApplicationName", "PolyLing");
            top.Add(hx);

            if (!ascii) top.Add(new FbxNode("FileId", (object)FileId));
            top.Add(new FbxNode("CreationTime", CreationTime));
            top.Add(new FbxNode("Creator", Creator));

            // GlobalSettings
            var gs = new FbxNode("GlobalSettings");
            gs.Add("Version", 1000);
            var gp = gs.Add("Properties70");
            FbxProps.Int(gp, "UpAxis", 1);
            FbxProps.Int(gp, "UpAxisSign", 1);
            FbxProps.Int(gp, "FrontAxis", 2);
            FbxProps.Int(gp, "FrontAxisSign", 1);
            FbxProps.Int(gp, "CoordAxis", 0);
            FbxProps.Int(gp, "CoordAxisSign", 1);
            FbxProps.Int(gp, "OriginalUpAxis", 1);
            FbxProps.Int(gp, "OriginalUpAxisSign", 1);
            FbxProps.Double(gp, "UnitScaleFactor", 1.0);
            FbxProps.Double(gp, "OriginalUnitScaleFactor", 1.0);
            FbxProps.ColorRGB(gp, "AmbientColor", 0, 0, 0);
            FbxProps.KString(gp, "DefaultCamera", "Producer Perspective");
            FbxProps.Enum(gp, "TimeMode", TimeModeOf(_fps));
            FbxProps.Enum(gp, "TimeProtocol", 2);
            FbxProps.Enum(gp, "SnapOnFrameMode", 0);
            FbxProps.KTime(gp, "TimeSpanStart", 0L);
            FbxProps.KTime(gp, "TimeSpanStop", _takeName != null ? _takeStop : TicksPerSecond);
            FbxProps.Double(gp, "CustomFrameRate", _fps);
            top.Add(gs);

            // Documents / References
            var docs = new FbxNode("Documents");
            docs.Add("Count", 1);
            var doc = docs.Add("Document", NewId(), "Scene", "Scene");
            var dp = doc.Add("Properties70");
            dp.Add("P", "SourceObject", "object", "", "");
            FbxProps.KString(dp, "ActiveAnimStackName", _takeName ?? "");
            doc.Add("RootNode", 0L);
            top.Add(docs);
            top.Add(new FbxNode("References"));

            // Definitions
            var defs = new FbxNode("Definitions");
            defs.Add("Version", 100);
            int total = 1;
            foreach (var kv in _typeCount) total += kv.Value;
            defs.Add("Count", total);
            defs.Add("ObjectType", "GlobalSettings").Add("Count", 1);
            foreach (var kv in _typeCount)
                defs.Add("ObjectType", kv.Key).Add("Count", kv.Value);
            top.Add(defs);

            top.Add(_objects);
            top.Add(_connections);

            // Takes
            var takes = new FbxNode("Takes");
            takes.Add("Current", _takeName ?? "");
            if (_takeName != null)
            {
                var take = takes.Add("Take", _takeName);
                take.Add("FileName", _takeName + ".tak");
                take.Add("LocalTime", 0L, _takeStop);
                take.Add("ReferenceTime", 0L, _takeStop);
            }
            top.Add(takes);

            return top;
        }

        // ================================================================
        // 共通
        // ================================================================

        private long NewId() => _nextId++;

        private FbxNode AddObject(string type, params object[] props)
        {
            _typeCount.TryGetValue(type, out int c);
            _typeCount[type] = c + 1;
            return _objects.Add(type, props).Block();
        }

        private void ConnectOO(long child, long parent) => _connections.Add("C", "OO", child, parent);
        private void ConnectOP(long child, long parent, string prop) => _connections.Add("C", "OP", child, parent, prop);

        /// <summary>Unity の行列 → ファイル座標の行列（S·M·S、移動 ×100）を FBX の 16 個（列ごと）にする。</summary>
        private static double[] FileMatrix(Matrix4x4 m)
        {
            var a = new double[16];
            for (int c = 0; c < 4; c++)
                for (int r = 0; r < 4; r++)
                {
                    double v = m[r, c];
                    if ((r == 0) != (c == 0) && r < 3 && c < 3) v = -v;     // S·M·S：x 行と x 列の非対角を反転
                    if (c == 3 && r < 3) v = (r == 0 ? -v : v) * UnitScale;  // 移動は S·t ×100
                    a[c * 4 + r] = v;
                }
            return a;
        }

        private static double NonZero(float v) => v == 0f ? 1e-12 : v;

        /// <summary>
        /// Unity の回転（ローカル）→ ファイル座標の XYZ 順オイラー角（度）。S·R·S は四元数 (x, −y, −z, w)。
        /// prev があれば同じ回転の 2 通りのうち prev に近い方を 360° 単位でそろえて返す。
        /// </summary>
        private static Vector3 FileEuler(Quaternion q, Vector3? prev)
        {
            double x = q.x, y = -q.y, z = -q.z, w = q.w;
            double n = Math.Sqrt(x * x + y * y + z * z + w * w);
            if (n > 0) { x /= n; y /= n; z /= n; w /= n; }

            double r00 = 1 - 2 * (y * y + z * z);
            double r01 = 2 * (x * y - z * w);
            double r10 = 2 * (x * y + z * w);
            double r11 = 1 - 2 * (x * x + z * z);
            double r20 = 2 * (x * z - y * w);
            double r21 = 2 * (y * z + x * w);
            double r22 = 1 - 2 * (x * x + y * y);

            // R = Rz(γ)·Ry(β)·Rx(α)。β は cos β = |(r00, r10)| から atan2 で求める（asin は ±90° 付近で精度が落ちる）。
            double a, b, g;
            double cb = Math.Sqrt(r00 * r00 + r10 * r10);
            b = Math.Atan2(-r20, cb);
            if (cb > 1e-9)
            {
                a = Math.Atan2(r21, r22);
                g = Math.Atan2(r10, r00);
            }
            else
            {
                // β = ±90°：γ = 0 として α に寄せる
                g = 0;
                a = r20 < 0 ? Math.Atan2(r01, r11) : Math.Atan2(-r01, r11);
            }

            const double Deg = 180.0 / Math.PI;
            var e = new Vector3((float)(a * Deg), (float)(b * Deg), (float)(g * Deg));
            if (prev == null) return e;

            Vector3 p = prev.Value;
            Vector3 alt = new Vector3(e.x + 180f, 180f - e.y, e.z + 180f);
            e   = new Vector3(Near(e.x, p.x),   Near(e.y, p.y),   Near(e.z, p.z));
            alt = new Vector3(Near(alt.x, p.x), Near(alt.y, p.y), Near(alt.z, p.z));
            return (e - p).sqrMagnitude <= (alt - p).sqrMagnitude ? e : alt;
        }

        private static float Near(float a, float prev) => a + 360f * Mathf.Round((prev - a) / 360f);

        private static long Ticks(double sec) => (long)Math.Round(sec * TicksPerSecond);

        /// <summary>fps に一致する FBX の時間モード。無ければ eCustom（14。CustomFrameRate を使う）。</summary>
        private static int TimeModeOf(float fps)
        {
            int[] fpsOf  = { 120, 100, 60, 50, 48, 30, 25, 24, 1000, 96, 72 };
            int[] modeOf = {   1,   2,  3,  4,  5,  6, 10, 11,   12, 15, 16 };
            for (int i = 0; i < fpsOf.Length; i++)
                if (Math.Abs(fps - fpsOf[i]) < 1e-3) return modeOf[i];
            return 14;
        }

        private static bool HasDrawableFace(MeshObject mo)
        {
            if (mo?.Faces == null || mo.Vertices == null || mo.Vertices.Count == 0) return false;
            foreach (var f in mo.Faces)
                if (f != null && f.VertexCount >= 3 && !f.IsHidden) return true;
            return false;
        }

        // ================================================================
        // ノード
        // ================================================================

        private void CreateNode(Transform t, long parentId, Matrix4x4 parentWorld)
        {
            var local = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
            bool isBone = _bindWorldOfBone.TryGetValue(t, out var bind);
            var world = isBone ? bind : parentWorld * local;
            var exportLocal = parentWorld.inverse * world;

            bool hasMesh = _includeMesh && _built.MeshSourceOf.TryGetValue(t, out var src) && HasDrawableFace(src.Shape);
            string type = isBone ? "LimbNode" : hasMesh ? "Mesh" : "Null";

            string name = UniqueNodeName(t.name);
            long id = NewId();
            var model = AddObject("Model", id, FbxNode.ObjectName(name, "Model"), type);
            model.Add("Version", 232);
            var p70 = model.Add("Properties70");
            FbxProps.Enum(p70, "InheritType", 1);
            if (isBone) FbxProps.Int(p70, "DefaultAttributeIndex", 0);

            Vector3    p = exportLocal.GetColumn(3);
            Vector3    s = exportLocal.lossyScale;
            Vector3    e = FileEuler(exportLocal.rotation, null);
            FbxProps.Lcl(p70, "Lcl Translation", -p.x * UnitScale, p.y * UnitScale, p.z * UnitScale);
            FbxProps.Lcl(p70, "Lcl Rotation", e.x, e.y, e.z);
            // 尺度 0 は子の尺度が壊れる DCC があるので 1e-12 にする。
            FbxProps.Lcl(p70, "Lcl Scaling", NonZero(s.x), NonZero(s.y), NonZero(s.z));
            model.Add("Culling", "CullingOff");
            ConnectOO(id, parentId);

            if (isBone)
            {
                long attr = NewId();
                var na = AddObject("NodeAttribute", attr, FbxNode.ObjectName(name, "NodeAttribute"), "LimbNode");
                FbxProps.Double(na.Add("Properties70"), "Size", UnitScale);
                na.Add("TypeFlags", "Skeleton");
                ConnectOO(attr, id);
            }

            _idOf[t]    = id;
            _nameOf[t]  = name;
            _worldOf[t] = world;
            _localOf[t] = exportLocal;
            _order.Add(t);

            for (int i = 0; i < t.childCount; i++)
                CreateNode(t.GetChild(i), id, world);
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

        // ================================================================
        // メッシュ
        // ================================================================

        private void WriteMesh(Transform t, HierarchyMeshSource src)
        {
            var mo    = src.Shape;
            var verts = mo.Vertices;
            int vCount = verts.Count;
            string name = _nameOf[t];
            long modelId = _idOf[t];

            // ── 面（表示中・3 頂点以上。頂点番号が範囲外の面は書かない）──
            var faces = new List<Face>();
            int badFaces = 0;
            foreach (var f in mo.Faces)
            {
                if (f == null || f.VertexCount < 3 || f.IsHidden) continue;
                bool ok = true;
                foreach (int vi in f.VertexIndices) if (vi < 0 || vi >= vCount) { ok = false; break; }
                if (!ok) { badFaces++; continue; }
                faces.Add(f);
            }
            if (badFaces > 0)
                _result.Warnings.Add($"\"{name}\" の頂点番号が範囲外の面 {badFaces} 個を書きません。");

            // ── 制御点 ──
            var off = src.VertexOffset;
            var cp = new double[vCount * 3];
            for (int v = 0; v < vCount; v++)
            {
                var pos = MirrorBranchOps.SameSenseFlip(src.SameSenseAxis, verts[v].Position + off);
                cp[v * 3]     = -pos.x * UnitScale;
                cp[v * 3 + 1] =  pos.y * UnitScale;
                cp[v * 3 + 2] =  pos.z * UnitScale;
            }

            // ── 角（並びは反転）・UV・法線・材質 ──
            int subCount = Math.Max(1, src.Materials?.Length ?? 1);
            var pvi = new List<int>();
            var cornerNormal = new List<Vector3>();
            var uvDirect = new List<double>();
            var uvBase = new int[vCount];
            for (int v = 0; v < vCount; v++)
            {
                uvBase[v] = uvDirect.Count / 2;
                foreach (var uv in verts[v].UVs) { uvDirect.Add(uv.x); uvDirect.Add(uv.y); }
            }
            int zeroUv = -1;
            var uvIndex = new List<int>();
            var faceSlot = new int[faces.Count];
            var usedSlots = new SortedSet<int>();
            bool allUp = true;

            for (int fi = 0; fi < faces.Count; fi++)
            {
                var f = faces[fi];
                int n = f.VertexCount;
                for (int k = n - 1; k >= 0; k--)
                {
                    int vi = f.VertexIndices[k];
                    var vx = verts[vi];

                    pvi.Add(k == 0 ? ~vi : vi);

                    // UV（ToUnityMesh と同じ選び方）
                    int uvSub = k < f.UVIndices.Count ? f.UVIndices[k] : 0;
                    if (uvSub < 0 || uvSub >= vx.UVs.Count) uvSub = vx.UVs.Count > 0 ? 0 : -1;
                    if (uvSub >= 0) uvIndex.Add(uvBase[vi] + uvSub);
                    else
                    {
                        if (zeroUv < 0) { zeroUv = uvDirect.Count / 2; uvDirect.Add(0); uvDirect.Add(0); }
                        uvIndex.Add(zeroUv);
                    }

                    // 法線（ToUnityMesh と同じ選び方）
                    int nSub = k < f.NormalIndices.Count ? f.NormalIndices[k] : 0;
                    Vector3 nn;
                    if (nSub >= 0 && nSub < vx.Normals.Count) nn = vx.Normals[nSub];
                    else if (vx.Normals.Count > 0)           nn = vx.Normals[0];
                    else                                     nn = Vector3.up;
                    nn = nn.normalized;
                    if (nn != Vector3.up) allUp = false;
                    cornerNormal.Add(MirrorBranchOps.SameSenseFlip(src.SameSenseAxis, nn));
                }

                int slot = Mathf.Clamp(f.MaterialIndex, 0, subCount - 1);
                faceSlot[fi] = slot;
                usedSlots.Add(slot);
            }

            long geoId = NewId();
            var geo = AddObject("Geometry", geoId, FbxNode.ObjectName(name, "Geometry"), "Mesh");
            geo.Add("Properties70");
            geo.Add("GeometryVersion", 124);
            geo.Add("Vertices", (object)cp);
            geo.Add("PolygonVertexIndex", (object)pvi.ToArray());

            var layer = new FbxNode("Layer", 0);
            layer.Add("Version", 100);

            // 法線（ToUnityMesh が計算させる条件のときは書かない）
            bool writeNormals = mo.PreserveNormals || !allUp;
            if (writeNormals)
            {
                var nrm = new double[cornerNormal.Count * 3];
                for (int i = 0; i < cornerNormal.Count; i++)
                {
                    nrm[i * 3]     = -cornerNormal[i].x;
                    nrm[i * 3 + 1] =  cornerNormal[i].y;
                    nrm[i * 3 + 2] =  cornerNormal[i].z;
                }
                var en = geo.Add("LayerElementNormal", 0);
                en.Add("Version", 101);
                en.Add("Name", "");
                en.Add("MappingInformationType", "ByPolygonVertex");
                en.Add("ReferenceInformationType", "Direct");
                en.Add("Normals", (object)nrm);
                var le = layer.Add("LayerElement");
                le.Add("Type", "LayerElementNormal");
                le.Add("TypedIndex", 0);
            }

            // UV
            {
                var eu = geo.Add("LayerElementUV", 0);
                eu.Add("Version", 101);
                eu.Add("Name", "UVSet0");
                eu.Add("MappingInformationType", "ByPolygonVertex");
                eu.Add("ReferenceInformationType", "IndexToDirect");
                eu.Add("UV", (object)uvDirect.ToArray());
                eu.Add("UVIndex", (object)uvIndex.ToArray());
                var le = layer.Add("LayerElement");
                le.Add("Type", "LayerElementUV");
                le.Add("TypedIndex", 0);
            }

            // 材質（面のある材質だけを番号順に付ける）
            var localOfSlot = new Dictionary<int, int>();
            foreach (int slot in usedSlots)
            {
                localOfSlot[slot] = localOfSlot.Count;
                Material m = src.Materials != null && slot < src.Materials.Length ? src.Materials[slot] : null;
                ConnectOO(GetMaterial(m), modelId);
            }
            {
                var em = geo.Add("LayerElementMaterial", 0);
                em.Add("Version", 101);
                em.Add("Name", "");
                if (localOfSlot.Count <= 1)
                {
                    em.Add("MappingInformationType", "AllSame");
                    em.Add("ReferenceInformationType", "IndexToDirect");
                    em.Add("Materials", (object)new[] { 0 });
                }
                else
                {
                    var mats = new int[faces.Count];
                    for (int fi = 0; fi < faces.Count; fi++) mats[fi] = localOfSlot[faceSlot[fi]];
                    em.Add("MappingInformationType", "ByPolygon");
                    em.Add("ReferenceInformationType", "IndexToDirect");
                    em.Add("Materials", (object)mats);
                }
                var le = layer.Add("LayerElement");
                le.Add("Type", "LayerElementMaterial");
                le.Add("TypedIndex", 0);
            }

            geo.Add(layer);
            ConnectOO(geoId, modelId);

            // ── スキン ──
            if (src.IsSkinned) WriteSkin(t, src, geoId, name);

            // ── ブレンドシェイプ ──
            if (src.Morphs.Count > 0) WriteBlendShapes(t, src, geoId, name, vCount);

            _result.MeshCount++;
            _result.ControlPointCount += vCount;
            _result.PolygonCount      += faces.Count;
        }

        private void WriteSkin(Transform t, HierarchyMeshSource src, long geoId, string name)
        {
            var bones = src.Bones;
            if (bones == null || bones.Length == 0) return;

            var indexes = new List<int>[bones.Length];
            var weights = new List<double>[bones.Length];
            for (int i = 0; i < bones.Length; i++) { indexes[i] = new List<int>(); weights[i] = new List<double>(); }

            bool changed = false;
            int dropped = 0;
            var verts = src.Shape.Vertices;
            for (int v = 0; v < verts.Count; v++)
            {
                var w = HierarchyBuilder.RemapBoneWeight(verts[v].BoneWeight ?? default, src.BoneSlotOf, ref changed, ref dropped);
                Add(w.boneIndex0, w.weight0, v);
                Add(w.boneIndex1, w.weight1, v);
                Add(w.boneIndex2, w.weight2, v);
                Add(w.boneIndex3, w.weight3, v);
            }
            if (dropped > 0) _result.Warnings.Add(HierarchyBuilder.DroppedWeightsMessage(name, dropped));

            void Add(int bone, float weight, int cp)
            {
                if (weight <= 0f || bone < 0 || bone >= bones.Length) return;
                indexes[bone].Add(cp);
                weights[bone].Add(weight);
            }

            long skinId = NewId();
            var skin = AddObject("Deformer", skinId, FbxNode.ObjectName(name + "_Skin", "Deformer"), "Skin");
            skin.Add("Version", 101);
            skin.Add("Link_DeformAcuracy", 50.0);
            ConnectOO(skinId, geoId);

            var meshGlobal = FileMatrix(_worldOf[t]);

            long poseId = NewId();
            var pose = AddObject("Pose", poseId, FbxNode.ObjectName(name + "_BindPose", "Pose"), "BindPose");
            pose.Add("Type", "BindPose");
            pose.Add("Version", 100);
            var poseNodes = new List<FbxNode>();

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !_idOf.TryGetValue(bones[i], out long boneId)) continue;
                var boneGlobal = FileMatrix(_worldOf[bones[i]]);
                // ファイルの Transform はメッシュ → ボーン（ufbx の mesh_node_to_bone、ufbx.c 14050 行）。
                // FileMatrix は相似変換なので FileMatrix(B⁻¹·M) = FileMatrix(B)⁻¹·FileMatrix(M)。
                var meshToBone = FileMatrix(_worldOf[bones[i]].inverse * _worldOf[t]);

                long cid = NewId();
                var c = AddObject("Deformer", cid, FbxNode.ObjectName(_nameOf[bones[i]] + "_Cluster", "SubDeformer"), "Cluster");
                c.Add("Version", 100);
                c.Add("UserData", "", "");
                c.Add("Indexes", (object)indexes[i].ToArray());
                c.Add("Weights", (object)weights[i].ToArray());
                c.Add("Transform", (object)meshToBone);
                c.Add("TransformLink", (object)boneGlobal);
                ConnectOO(cid, skinId);
                ConnectOO(boneId, cid);

                var pn = new FbxNode("PoseNode");
                pn.Add("Node", boneId);
                pn.Add("Matrix", (object)boneGlobal);
                poseNodes.Add(pn);
            }
            {
                var pn = new FbxNode("PoseNode");
                pn.Add("Node", _idOf[t]);
                pn.Add("Matrix", (object)meshGlobal);
                poseNodes.Add(pn);
            }
            pose.Add("NbPoseNodes", poseNodes.Count);
            foreach (var pn in poseNodes) pose.Add(pn);

            _result.SkinnedMeshCount++;
        }

        private void WriteBlendShapes(Transform t, HierarchyMeshSource src, long geoId, string name, int vCount)
        {
            long bsId = NewId();
            AddObject("Deformer", bsId, FbxNode.ObjectName(name + "_BlendShape", "Deformer"), "BlendShape")
                .Add("Version", 100);
            ConnectOO(bsId, geoId);

            foreach (var slot in src.Morphs)
            {
                // 差分（頂点番号の昇順。同じ番号は足す。範囲外は捨てる）
                var sum = new SortedDictionary<int, Vector3>();
                var offsets = _model.GetMeshContext(slot.MorphContextIndex)?.GetMorphOffsets();
                if (offsets != null)
                    foreach (var (vi, o) in offsets)
                    {
                        if (vi < 0 || vi >= vCount) continue;
                        Vector3 d = src.MirrorDelta ? MirrorBranchOps.MirrorNormal(src.MirrorAxis, o) : o;
                        d = MirrorBranchOps.SameSenseFlip(src.SameSenseAxis, d);
                        sum[vi] = sum.TryGetValue(vi, out var acc) ? acc + d : d;
                    }

                var idx = new int[sum.Count];
                var dv  = new double[sum.Count * 3];
                int k = 0;
                foreach (var kv in sum)
                {
                    idx[k] = kv.Key;
                    dv[k * 3]     = -kv.Value.x * UnitScale;
                    dv[k * 3 + 1] =  kv.Value.y * UnitScale;
                    dv[k * 3 + 2] =  kv.Value.z * UnitScale;
                    k++;
                }

                long chId = NewId();
                var ch = AddObject("Deformer", chId, FbxNode.ObjectName(slot.ShapeName, "SubDeformer"), "BlendShapeChannel");
                ch.Add("Version", 100);
                ch.Add("DeformPercent", 0.0);
                ch.Add("FullWeights", (object)new[] { 100.0 });
                ConnectOO(chId, bsId);
                _channelOf[(t, slot.ShapeIndex)] = chId;

                long shId = NewId();
                var sh = AddObject("Geometry", shId, FbxNode.ObjectName(slot.ShapeName, "Geometry"), "Shape");
                sh.Add("Version", 100);
                sh.Add("Indexes", (object)idx);
                sh.Add("Vertices", (object)dv);
                ConnectOO(shId, chId);
            }
            _result.BlendShapeCount += src.Morphs.Count;
        }

        // ================================================================
        // 材質・テクスチャ
        // ================================================================

        private long GetMaterial(Material unityMat)
        {
            MaterialReference matRef = null;
            if (unityMat != null) _refOfMaterial.TryGetValue(unityMat, out matRef);
            object key = (object)matRef ?? (object)unityMat ?? "Default";
            if (_materialIdOf.TryGetValue(key, out long existing)) return existing;

            var d = matRef?.Data;
            string name = UniqueMaterialName(d?.Name ?? (unityMat != null ? unityMat.name : "Default"));

            long id = NewId();
            var mat = AddObject("Material", id, FbxNode.ObjectName(name, "Material"), "");
            mat.Add("Version", 102);
            mat.Add("ShadingModel", "lambert");
            mat.Add("MultiLayer", 0);
            var p = mat.Add("Properties70");

            float[] bc = d?.BaseColor;
            double r = bc != null && bc.Length > 0 ? bc[0] : 0.8;
            double g = bc != null && bc.Length > 1 ? bc[1] : 0.8;
            double b = bc != null && bc.Length > 2 ? bc[2] : 0.8;
            double a = bc != null && bc.Length > 3 ? bc[3] : 1.0;
            FbxProps.Color(p, "DiffuseColor", r, g, b);
            FbxProps.Number(p, "DiffuseFactor", 1.0);
            FbxProps.Color(p, "AmbientColor", 0, 0, 0);
            FbxProps.Color(p, "TransparentColor", 1, 1, 1);
            FbxProps.Number(p, "TransparencyFactor", 1.0 - a);
            if (d != null && d.EmissionEnabled && d.EmissionColor != null && d.EmissionColor.Length >= 3)
            {
                FbxProps.Color(p, "EmissiveColor", d.EmissionColor[0], d.EmissionColor[1], d.EmissionColor[2]);
                FbxProps.Number(p, "EmissiveFactor", 1.0);
            }
            else
            {
                FbxProps.Color(p, "EmissiveColor", 0, 0, 0);
                FbxProps.Number(p, "EmissiveFactor", 1.0);   // Lambert の既定値（段 2 の書き出しと同じ）
            }
            FbxProps.Double(p, "BumpFactor", d != null ? d.NormalScale : 1.0);

            if (matRef != null)
            {
                ConnectTexture(id, "DiffuseColor",  matRef, ModelTextureTransfer.TextureSlot.Base,     d.BaseMapST);
                ConnectTexture(id, "NormalMap",     matRef, ModelTextureTransfer.TextureSlot.Normal,   d.NormalMapST);
                ConnectTexture(id, "EmissiveColor", matRef, ModelTextureTransfer.TextureSlot.Emission, d.EmissionMapST);
            }

            _materialIdOf[key] = id;
            return id;
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

        private void ConnectTexture(long materialId, string propName, MaterialReference matRef,
                                    ModelTextureTransfer.TextureSlot slot, float[] st)
        {
            if (!_textureOf.TryGetValue((matRef, slot), out var entry)) return;

            string rel = Path.GetFileName(_texFolder) + "/" + entry.FileName;
            if (!_writtenTexture.TryGetValue(entry, out var written))
            {
                Directory.CreateDirectory(_texFolder);
                string full = Path.GetFullPath(Path.Combine(_texFolder, entry.FileName));
                File.WriteAllBytes(full, entry.Data);

                long vid = NewId();
                string vname = Path.GetFileNameWithoutExtension(entry.FileName);
                var video = AddObject("Video", vid, FbxNode.ObjectName(vname, "Video"), "Clip");
                video.Add("Type", "Clip");
                FbxProps.KString(video.Add("Properties70"), "Path", full);
                video.Add("UseMipMap", 0);
                video.Add("Filename", full);
                video.Add("RelativeFilename", rel);
                if (_embed) video.Add("Content", (object)entry.Data);

                written = (full, vid);
                _writtenTexture[entry] = written;
            }

            string tname = Path.GetFileNameWithoutExtension(entry.FileName) + "_" + propName;
            long tid = NewId();
            var tex = AddObject("Texture", tid, FbxNode.ObjectName(tname, "Texture"), "");
            tex.Add("Type", "TextureVideoClip");
            tex.Add("Version", 202);
            tex.Add("TextureName", FbxNode.ObjectName(tname, "Texture"));
            var tp = tex.Add("Properties70");
            FbxProps.Bool(tp, "UseMaterial", true);
            FbxProps.Enum(tp, "WrapModeU", 0);   // 繰り返し
            FbxProps.Enum(tp, "WrapModeV", 0);
            double sx = 1, sy = 1, tx = 0, ty = 0;
            if (st != null && st.Length >= 4) { sx = st[0]; sy = st[1]; tx = st[2]; ty = st[3]; }
            FbxProps.Vector(tp, "Translation", tx, ty, 0);
            FbxProps.Vector(tp, "Scaling", sx, sy, 1);
            tex.Add("Media", FbxNode.ObjectName(Path.GetFileNameWithoutExtension(entry.FileName), "Video"));
            tex.Add("FileName", written.full);
            tex.Add("RelativeFilename", rel);
            tex.Add("ModelUVTranslation", 0.0, 0.0);
            tex.Add("ModelUVScaling", 1.0, 1.0);
            tex.Add("Texture_Alpha_Source", "None");
            tex.Add("Cropping", 0, 0, 0, 0);

            ConnectOO(written.videoId, tid);
            ConnectOP(tid, materialId, propName);
        }

        // ================================================================
        // アニメーション（ファイル冒頭【アニメーション】）
        // ================================================================

        public void AddAnimation(ModelMotionSamples s, string takeName)
        {
            _result.HasMotion               = true;
            _result.FrameCount              = s.FrameCount;
            _result.DurationSec             = s.DurationSec;
            _result.MatchedTrackCount       = s.MatchedTrackCount;
            _result.RootMotion              = s.RootMotion;
            _result.ExpressionTrackCount    = s.ExpressionTrackCount;
            _result.ExpressionResolvedCount = s.ExpressionResolvedCount;

            // ── 関節 → ノード（仮想骨格のノードの ContextIndex = HierarchyBuilder のボーン索引）──
            var jointOf = new Dictionary<Transform, int>();
            for (int j = 0; j < s.JointNode.Count; j++)
            {
                int ci = s.Skeleton.Nodes[s.JointNode[j]].ContextIndex;
                if (ci >= 0 && _built.BoneTransformByIndex.TryGetValue(ci, out var tf) && tf != null && _idOf.ContainsKey(tf))
                    jointOf[tf] = j;
                else
                    _result.SkippedVirtualJointCount++;
            }
            _result.AnimatedNodeCount = jointOf.Count;

            // ── テイク ──
            _fps      = s.Fps;
            _takeName = takeName;
            _takeStop = Ticks(s.DurationSec);

            long stackId = NewId();
            var stack = AddObject("AnimationStack", stackId, FbxNode.ObjectName(takeName, "AnimStack"), "");
            var sp = stack.Add("Properties70");
            FbxProps.KTime(sp, "LocalStart", 0L);
            FbxProps.KTime(sp, "LocalStop", _takeStop);
            FbxProps.KTime(sp, "ReferenceStart", 0L);
            FbxProps.KTime(sp, "ReferenceStop", _takeStop);

            long layerId = NewId();
            AddObject("AnimationLayer", layerId, FbxNode.ObjectName("Base Layer", "AnimLayer"), "");
            ConnectOO(layerId, stackId);

            int nF = s.FrameCount;
            var times = new long[nF];
            for (int f = 0; f < nF; f++) times[f] = Ticks(f / (double)s.Fps);

            // ── フレームごとに全ノードのワールドを組み、関節のローカルを値にする ──
            var tv = new Dictionary<Transform, float[][]>();   // [6][nF]：移動 X/Y/Z・回転 X/Y/Z
            foreach (var t in jointOf.Keys)
            {
                var a = new float[6][];
                for (int c = 0; c < 6; c++) a[c] = new float[nF];
                tv[t] = a;
            }

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

                    if (!tv.TryGetValue(t, out var a)) continue;
                    var local = parentW.inverse * w;
                    Vector3 p = local.GetColumn(3);
                    Vector3 e = FileEuler(local.rotation, prevEuler.TryGetValue(t, out var pe) ? pe : (Vector3?)null);
                    prevEuler[t] = e;
                    a[0][f] = (float)(-p.x * UnitScale);
                    a[1][f] = (float)( p.y * UnitScale);
                    a[2][f] = (float)( p.z * UnitScale);
                    a[3][f] = e.x;
                    a[4][f] = e.y;
                    a[5][f] = e.z;
                }
            }

            foreach (var kv in tv)
            {
                long nodeId = _idOf[kv.Key];
                AddCurveNode(layerId, nodeId, "T", "Lcl Translation", times, kv.Value[0], kv.Value[1], kv.Value[2]);
                AddCurveNode(layerId, nodeId, "R", "Lcl Rotation",    times, kv.Value[3], kv.Value[4], kv.Value[5]);
            }

            // ── 表情 → ブレンドシェイプの重み（DeformPercent は 0〜100）──
            foreach (var slot in _built.MorphShapes)
            {
                if (!s.MorphWeights.TryGetValue(slot.MorphContextIndex, out var weights)) continue;
                if (!_channelOf.TryGetValue((slot.Renderer, slot.ShapeIndex), out long chId)) continue;
                var v = new float[nF];
                for (int f = 0; f < nF; f++) v[f] = weights[f] * 100f;

                long cnId = NewId();
                var cn = AddObject("AnimationCurveNode", cnId, FbxNode.ObjectName("DeformPercent", "AnimCurveNode"), "");
                FbxProps.Number(cn.Add("Properties70"), "d|DeformPercent", v.Length > 0 ? v[0] : 0);
                ConnectOO(cnId, layerId);
                ConnectOP(cnId, chId, "DeformPercent");
                AddCurve(cnId, "d|DeformPercent", times, v);
                _result.AnimatedBlendShapeCount++;
            }
        }

        private void AddCurveNode(long layerId, long nodeId, string shortName, string prop,
                                  long[] times, float[] x, float[] y, float[] z)
        {
            long cnId = NewId();
            var cn = AddObject("AnimationCurveNode", cnId, FbxNode.ObjectName(shortName, "AnimCurveNode"), "");
            var p = cn.Add("Properties70");
            FbxProps.Number(p, "d|X", x.Length > 0 ? x[0] : 0);
            FbxProps.Number(p, "d|Y", y.Length > 0 ? y[0] : 0);
            FbxProps.Number(p, "d|Z", z.Length > 0 ? z[0] : 0);
            ConnectOO(cnId, layerId);
            ConnectOP(cnId, nodeId, prop);
            AddCurve(cnId, "d|X", times, x);
            AddCurve(cnId, "d|Y", times, y);
            AddCurve(cnId, "d|Z", times, z);
        }

        /// <summary>
        /// 線形キーの曲線。キーの属性は FBX SDK 2020.3.4 が線形キーに書くものと同じ：
        /// KeyAttrFlags 0x104（線形 0x4。ufbx.c の UFBXI_KEY_INTERPOLATION_LINEAR ＋ 接線の自動 0x100）、
        /// KeyAttrDataFloat（傾き 0, 0、重みの既定 0x0D050D05 のビット列、0）。
        /// </summary>
        private void AddCurve(long curveNodeId, string channel, long[] times, float[] values)
        {
            long id = NewId();
            var c = AddObject("AnimationCurve", id, FbxNode.ObjectName("", "AnimCurve"), "");
            c.Add("Default", values.Length > 0 ? (double)values[0] : 0.0);
            c.Add("KeyVer", 4009);
            c.Add("KeyTime", (object)times);
            c.Add("KeyValueFloat", (object)values);
            c.Add("KeyAttrFlags", (object)new[] { 0x104 });
            c.Add("KeyAttrDataFloat", (object)new[] { 0f, 0f, DefaultWeights, 0f });
            c.Add("KeyAttrRefCount", (object)new[] { times.Length });
            ConnectOP(id, curveNodeId, channel);
        }
    }
}
