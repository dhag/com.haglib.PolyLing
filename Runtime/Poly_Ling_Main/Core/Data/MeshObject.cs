// Assets/Editor/MeshObject.cs
// 頂点ベースのメッシュデータ構造
// - Vertex: 位置 + 複数UV + 複数法線 + フラグ
// - Face: N角形対応（三角形、四角形、Nゴン）+ マテリアルインデックス + フラグ
// - MeshObject: Unity UnityMesh との相互変換（サブメッシュ対応）
// v1.2: VertexFlags/FaceFlags 追加


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
    // ============================================================
    // フラグ定義
    // ============================================================

    /// <summary>
    /// 頂点フラグ（永続的な属性）
    /// </summary>
    [Flags]
    public enum VertexFlags : byte
    {
        /// <summary>フラグなし</summary>
        None = 0,

        /// <summary>ミラー平面上（中央頂点）</summary>
        OnMirrorPlane = 1 << 0,

        /// <summary>ミラー操作で生成された頂点</summary>
        MirrorGenerated = 1 << 1,

        /// <summary>編集ロック</summary>
        Locked = 1 << 2,

        /// <summary>補助点（表示用・非メッシュ）</summary>
        Auxiliary = 1 << 3,

        // 将来の拡張用に 4-7 を予約
    }

    /// <summary>
    /// 面フラグ（永続的な属性）
    /// </summary>
    [Flags]
    public enum FaceFlags : byte
    {
        /// <summary>フラグなし</summary>
        None = 0,

        /// <summary>ミラー操作で生成された面</summary>
        MirrorGenerated = 1 << 0,

        /// <summary>補助線/補助面</summary>
        Auxiliary = 1 << 1,

        /// <summary>非表示</summary>
        Hidden = 1 << 2,

        // 将来の拡張用に 3-7 を予約
    }

    // ============================================================
    // Vertex クラス
    // ============================================================

    /// <summary>
    /// 頂点データ
    /// 位置と、複数のUV/法線を保持（シーム・ハードエッジ対応）
    /// </summary>
    [Serializable]
    public class Vertex
    {
        /// <summary>
        /// 頂点ID（トポロジー追跡・外部連携・モーフ用）
        /// MeshObjectが管理する一意の識別子
        /// </summary>
        public int Id = 0;

        /// <summary>頂点位置</summary>
        public Vector3 Position;

        /// <summary>UV座標リスト（面から UVIndices で参照）</summary>
        public List<Vector2> UVs = new List<Vector2>();

        /// <summary>法線リスト（面から NormalIndices で参照）</summary>
        public List<Vector3> Normals = new List<Vector3>();

        /// <summary>頂点フラグ</summary>
        public VertexFlags Flags = VertexFlags.None;

        /// <summary>
        /// ボーンウェイト（スキニング用）
        /// boneIndex = _meshContextList のインデックス
        /// null = スキニングなし
        /// </summary>
        public BoneWeight? BoneWeight = null;

        /// <summary>
        /// ミラー側ボーンウェイト（ミラーオブジェクト用）
        /// ミラー展開時に使用される
        /// null = ミラーウェイトなし（実体側と同じか、ミラーなし）
        /// </summary>
        public BoneWeight? MirrorBoneWeight = null;

        /// <summary>スキニングデータを持つか</summary>
        public bool HasBoneWeight => BoneWeight.HasValue;

        /// <summary>ミラー側スキニングデータを持つか</summary>
        public bool HasMirrorBoneWeight => MirrorBoneWeight.HasValue;

        // === コンストラクタ ===

        public Vertex()
        {
            Position = Vector3.zero;
        }

        public Vertex(Vector3 position)
        {
            Position = position;
        }

        public Vertex(Vector3 position, Vector2 uv)
        {
            Position = position;
            UVs.Add(uv);
        }

        public Vertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Position = position;
            UVs.Add(uv);
            Normals.Add(normal);
        }

        /// <summary>
        /// ID指定付きコンストラクタ
        /// </summary>
        public Vertex(int id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        // === フラグ操作 ===

        /// <summary>フラグが設定されているか</summary>
        public bool HasFlag(VertexFlags flag) => (Flags & flag) != 0;

        /// <summary>フラグを設定</summary>
        public void SetFlag(VertexFlags flag) => Flags |= flag;

        /// <summary>フラグをクリア</summary>
        public void ClearFlag(VertexFlags flag) => Flags &= ~flag;

        /// <summary>フラグをトグル</summary>
        public void ToggleFlag(VertexFlags flag) => Flags ^= flag;

        /// <summary>ミラー平面上か</summary>
        public bool IsOnMirrorPlane => HasFlag(VertexFlags.OnMirrorPlane);

        /// <summary>ミラー生成された頂点か</summary>
        public bool IsMirrorGenerated => HasFlag(VertexFlags.MirrorGenerated);

        /// <summary>ロックされているか</summary>
        public bool IsLocked => HasFlag(VertexFlags.Locked);

        /// <summary>補助点か</summary>
        public bool IsAuxiliary => HasFlag(VertexFlags.Auxiliary);

        // === ユーティリティ ===

        /// <summary>
        /// UVを追加し、インデックスを返す
        /// </summary>
        public int AddUV(Vector2 uv)
        {
            UVs.Add(uv);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線を追加し、インデックスを返す
        /// </summary>
        public int AddNormal(Vector3 normal)
        {
            Normals.Add(normal);
            return Normals.Count - 1;
        }

        /// <summary>
        /// 同一UVが既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddUV(Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < UVs.Count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance)
                    return i;
            }
            return AddUV(uv);
        }

        /// <summary>
        /// 同一法線が既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddNormal(Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < Normals.Count; i++)
            {
                if (Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }
            return AddNormal(normal);
        }

        /// <summary>
        /// UVと法線を1組として検索し、無ければ両方に追加して共通の添字を返す。
        ///
        /// 【不変条件】UVs.Count == Normals.Count、および面の
        /// UVIndices[j] == NormalIndices[j] を保つための唯一の追加口。
        /// UV と法線を別々に追加すると両者の添字がずれ、展開index空間
        /// （BuildExpansionMap / GPU展開バッファ / PMXエクスポート）と食い違う。
        /// </summary>
        public int GetOrAddUVNormal(Vector2 uv, Vector3 normal, float tolerance = 0.0001f)
        {
            EnsureNormalSlots();

            int count = Mathf.Min(UVs.Count, Normals.Count);
            for (int i = 0; i < count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance &&
                    Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }

            UVs.Add(uv);
            Normals.Add(normal);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線スロット数を UV スロット数へ揃える。
        /// 不足分は先頭法線（無ければ Vector3.up）で埋め、超過分は切り捨てる。
        /// UVスロットが無い頂点は判断材料が無いため何もしない。
        /// </summary>
        public void EnsureNormalSlots()
        {
            if (UVs.Count == 0) return;

            Vector3 fill = Normals.Count > 0 ? Normals[0] : Vector3.up;
            while (Normals.Count < UVs.Count) Normals.Add(fill);
            while (Normals.Count > UVs.Count) Normals.RemoveAt(Normals.Count - 1);
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public Vertex Clone()
        {
            var clone = new Vertex(this.Position);
            clone.Id = this.Id;
            clone.UVs = new List<Vector2>(this.UVs);
            clone.Normals = new List<Vector3>(this.Normals);
            clone.Flags = this.Flags;
            clone.BoneWeight = this.BoneWeight;
            clone.MirrorBoneWeight = this.MirrorBoneWeight;
            return clone;
        }

        /// <summary>
        /// ディープコピー（新しいIDを割り当て）
        /// </summary>
        public Vertex CloneWithNewId(int newId)
        {
            var clone = Clone();
            clone.Id = newId;
            return clone;
        }
    }

    // ============================================================
    // Face クラス
    // ============================================================

    /// <summary>
    /// 面データ（N角形対応）
    /// 頂点インデックスと、各頂点のUV/法線サブインデックス、マテリアルインデックスを保持
    /// </summary>
    [Serializable]
    public class Face
    {
        /// <summary>
        /// 面ID（トポロジー追跡・外部連携・モーフ用）
        /// MeshObjectが管理する一意の識別子
        /// </summary>
        public int Id = 0;

        /// <summary>頂点インデックスリスト（Vertex配列への参照）</summary>
        public List<int> VertexIndices = new List<int>();

        /// <summary>各頂点のUVサブインデックス（Vertex.UVs[n]への参照）</summary>
        public List<int> UVIndices = new List<int>();

        /// <summary>各頂点の法線サブインデックス（Vertex.Normals[n]への参照）</summary>
        public List<int> NormalIndices = new List<int>();

        /// <summary>マテリアルインデックス（MeshUndoContext.Materialsへの参照）</summary>
        public int MaterialIndex = 0;

        /// <summary>面フラグ</summary>
        public FaceFlags Flags = FaceFlags.None;

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => VertexIndices.Count;

        /// <summary>三角形数（扇形分割時）</summary>
        public int TriangleCount => VertexCount >= 3 ? VertexCount - 2 : 0;

        /// <summary>三角形か</summary>
        public bool IsTriangle => VertexCount == 3;

        /// <summary>四角形か</summary>
        public bool IsQuad => VertexCount == 4;

        /// <summary>有効な面か（3頂点以上）</summary>
        public bool IsValid => VertexCount >= 3;

        // === フラグ操作 ===

        /// <summary>フラグが設定されているか</summary>
        public bool HasFlag(FaceFlags flag) => (Flags & flag) != 0;

        /// <summary>フラグを設定</summary>
        public void SetFlag(FaceFlags flag) => Flags |= flag;

        /// <summary>フラグをクリア</summary>
        public void ClearFlag(FaceFlags flag) => Flags &= ~flag;

        /// <summary>フラグをトグル</summary>
        public void ToggleFlag(FaceFlags flag) => Flags ^= flag;

        /// <summary>ミラー生成された面か</summary>
        public bool IsMirrorGenerated => HasFlag(FaceFlags.MirrorGenerated);

        /// <summary>補助線/面か</summary>
        public bool IsAuxiliary => HasFlag(FaceFlags.Auxiliary);

        /// <summary>非表示か</summary>
        public bool IsHidden => HasFlag(FaceFlags.Hidden);

        // === コンストラクタ ===

        public Face() { }

        /// <summary>
        /// 三角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2 });
            UVIndices.AddRange(new[] { 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 四角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int v3, int materialIndex = 0)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2, v3 });
            UVIndices.AddRange(new[] { 0, 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
            MaterialIndex = materialIndex;
        }

        /// <summary>
        /// 完全指定で三角形を作成
        /// </summary>
        public static Face CreateTriangle(
            int v0, int v1, int v2,
            int uv0, int uv1, int uv2,
            int n0, int n1, int n2,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2 },
                UVIndices = new List<int> { uv0, uv1, uv2 },
                NormalIndices = new List<int> { n0, n1, n2 },
                MaterialIndex = materialIndex
            };
        }

        /// <summary>
        /// 完全指定で四角形を作成
        /// </summary>
        public static Face CreateQuad(
            int v0, int v1, int v2, int v3,
            int uv0, int uv1, int uv2, int uv3,
            int n0, int n1, int n2, int n3,
            int materialIndex = 0)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2, v3 },
                UVIndices = new List<int> { uv0, uv1, uv2, uv3 },
                NormalIndices = new List<int> { n0, n1, n2, n3 },
                MaterialIndex = materialIndex
            };
        }

        // === 三角形分解 ===

        /// <summary>
        /// 三角形インデックスに分解（扇形分割）
        /// </summary>
        /// <returns>三角形数 × 3 のインデックス配列</returns>
        public int[] ToTriangleIndices()
        {
            if (VertexCount < 3)
                return Array.Empty<int>();

            if (IsTriangle)
                return VertexIndices.ToArray();

            // 扇形分割: v0 を中心に (v0, v1, v2), (v0, v2, v3), ... 
            var result = new List<int>();
            for (int i = 1; i < VertexCount - 1; i++)
            {
                result.Add(VertexIndices[0]);
                result.Add(VertexIndices[i]);
                result.Add(VertexIndices[i + 1]);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 三角形に分解してFaceリストを返す（MaterialIndex, Flags引き継ぎ）
        /// </summary>
        public List<Face> Triangulate()
        {
            var result = new List<Face>();

            if (VertexCount < 3)
                return result;

            if (IsTriangle)
            {
                result.Add(Clone());
                return result;
            }

            // 扇形分割（MaterialIndex, Flagsを引き継ぐ）
            for (int i = 1; i < VertexCount - 1; i++)
            {
                var tri = Face.CreateTriangle(
                    VertexIndices[0], VertexIndices[i], VertexIndices[i + 1],
                    UVIndices.Count > 0 ? UVIndices[0] : 0,
                    UVIndices.Count > i ? UVIndices[i] : 0,
                    UVIndices.Count > i + 1 ? UVIndices[i + 1] : 0,
                    NormalIndices.Count > 0 ? NormalIndices[0] : 0,
                    NormalIndices.Count > i ? NormalIndices[i] : 0,
                    NormalIndices.Count > i + 1 ? NormalIndices[i + 1] : 0,
                    MaterialIndex);
                tri.Flags = this.Flags;
                result.Add(tri);
            }
            return result;
        }

        /// <summary>
        /// 面を反転（頂点順序を逆にする）
        /// </summary>
        public void Flip()
        {
            VertexIndices.Reverse();
            UVIndices.Reverse();
            NormalIndices.Reverse();
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public Face Clone()
        {
            return new Face
            {
                Id = Id,
                VertexIndices = new List<int>(VertexIndices),
                UVIndices = new List<int>(UVIndices),
                NormalIndices = new List<int>(NormalIndices),
                MaterialIndex = MaterialIndex,
                Flags = Flags
            };
        }

        /// <summary>
        /// ディープコピー（新しいIDを割り当て）
        /// </summary>
        public Face CloneWithNewId(int newId)
        {
            var clone = Clone();
            clone.Id = newId;
            return clone;
        }
    }

    // ============================================================
    // MeshType 定義（MeshContext.MeshTypeと統一）
    // ============================================================

    /// <summary>
    /// メッシュの種類
    /// </summary>
    public enum MeshType
    {
        /// <summary>通常のメッシュ</summary>
        Mesh = 0,
        /// <summary>ボーン</summary>
        Bone = 1,
        /// <summaryモーフオブジェクト</summary>
        Morph = 2,
        /// <summary>剛体オブジェクト</summary>
        RigidBody = 3,
        /// <summary>剛体ジョイントオブジェクト</summary>
        RigidBodyJoint = 4,
        /// <summary>ヘルパーオブジェクト</summary>
        Helper = 5,
        /// <summary>グループ</summary>
        Group = 6,
        /// <summary>ベイクされたミラーメッシュ</summary>
        BakedMirror = 7,
        /// <summary>MirrorPairのミラー側（サーフェス描画のみ、頂点・辺・ヒットテスト対象外）</summary>
        MirrorSide = 8
    }

    // ============================================================
    // MeshObject クラス
    // ============================================================

    /// <summary>
    /// メッシュデータ本体
    /// Vertex/Faceリストを管理し、Unity Meshとの相互変換を提供
    /// </summary>
    [Serializable]
    public class MeshObject
    {
        // ================================================================
        // ID管理
        // ================================================================

        /// <summary>ID生成用の乱数ジェネレータ</summary>
        [NonSerialized]
        private static readonly System.Random _idRandom = new System.Random();

        /// <summary>頂点用の使用中ID（重複防止）</summary>
        [NonSerialized]
        private HashSet<int> _usedVertexIds = new HashSet<int>();

        /// <summary>面用の使用中ID（重複防止）</summary>
        [NonSerialized]
        private HashSet<int> _usedFaceIds = new HashSet<int>();

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

        // ================================================================
        // 基本プロパティ
        // ================================================================

        /// <summary>メッシュ名</summary>
        public string Name = "Mesh";

        /// <summary>メッシュの種類</summary>
        public MeshType Type { get; set; } = MeshType.Mesh;

        /// <summary>頂点リスト</summary>
        public List<Vertex> Vertices = new List<Vertex>();

        /// <summary>面リスト</summary>
        public List<Face> Faces = new List<Face>();

        // ================================================================
        // Position 配列キャッシュ（ハイブリッドSoA）
        // ================================================================
        //
        // 【目的】
        // Vertices[i].Position への個別アクセスを介さずに、
        // Position配列をバルクで取得・設定できるようにする。
        // GPU転送、Undoスナップショット、ネットワーク同期で使用。
        //
        // 【使い分け】
        // - 読み取り: Positions プロパティ（キャッシュ自動構築）
        // - 書き込み: SetPositions()（Vertex.Positionに書き戻し）
        // - 無効化: InvalidatePositionCache()（Vertex.Positionを直接変更した後）
        //
        // 【注意】
        // Vertices[i].Position を直接変更した場合は
        // InvalidatePositionCache() を呼ぶこと。
        // AddVertex/RemoveVertex 等のトポロジー変更時は自動で無効化される。
        //

        [NonSerialized]
        private Vector3[] _positionCache;

        [NonSerialized]
        private bool _positionCacheDirty = true;

        /// <summary>
        /// Position配列を取得（キャッシュ付き）
        /// Vertices[i].Position を変更した場合は InvalidatePositionCache() が必要
        /// </summary>
        public Vector3[] Positions
        {
            get
            {
                if (_positionCacheDirty || _positionCache == null || _positionCache.Length != Vertices.Count)
                {
                    RebuildPositionCache();
                }
                return _positionCache;
            }
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

        /// <summary>
        /// 頂点が三角形化済みか（PMX形式）
        /// true: 各頂点が1つのUV/法線を持ち、すべての面が三角形 (展開済み、PMX互換)
        /// false: 頂点が複数のUV/法線を持つ可能性あり、四角形等も含みうる (MQO形式)
        ///
        /// ※従来は "IsExpanded" と呼称されていたが、TreeView の展開/折りたたみ
        /// (MeshContext.IsFolding / SummaryTreeAdapter.IsExpanded) と紛らわしいため改名。
        /// 実体としては "三角形化 + 頂点展開" が同時に行われる PMX 化処理のフラグ。
        /// </summary>
        public bool IsTriangulated { get; set; } = false;




        // ================================================================
        // 階層・トランスフォーム情報
        // ================================================================

        /// <summary>
        /// 親メッシュのインデックス（-1=ルート）
        /// メッシュ編集時のグループ化・表示用
        /// </summary>
        public int ParentIndex { get; set; } = -1;

        /// <summary>
        /// 階層深度（MQO互換、インポート/エクスポート時のみ使用）
        /// 通常はParentIndexから計算
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// ゲームオブジェクト階層の親インデックス（-1=ルート）
        /// Unityエクスポート時のTransform親子関係用（将来用）
        /// </summary>
        public int HierarchyParentIndex { get; set; } = -1;

        /// <summary>
        /// アーマチャ生成時にボーンを生成しない。
        /// 子ボーンのワールド位置計算にはこのメッシュの姿勢を考慮する。
        /// このメッシュの頂点ウェイトは最寄りの親ボーンに割り当てられる。
        /// </summary>
        public bool IgnorePoseInArmature { get; set; } = false;

        /// <summary>
        /// 頂点法線を保持する（自動再計算を行わない）。
        /// true の場合、頂点移動 / Undo/Redo / ミラー更新時に
        /// Mesh.RecalculateNormals() を呼ばず、MeshObject が保持する法線をそのまま使う。
        /// 髪の房など、隣接オブジェクト間で法線を揃えたメッシュに使用する。
        ///
        /// 【既定 true】自動再計算は既定で行わない。
        /// 左ペインの「法線自動計算」チェック（既定 OFF）が選択メッシュの本フラグを
        /// 反転して書き込む。保存済みプロジェクトは保存値が読み込まれる
        /// （CsvMeshSerializer / ModelSerializer）ため、既定値が効くのは
        /// 新規生成メッシュと、値を持たない経路のみ。
        /// </summary>
        public bool PreserveNormals { get; set; } = true;

        /// <summary>
        /// 法線の自動再計算から除外するセット一覧（パーツ選択辞書と同じ構造）。
        /// リストに載っているセットが指す要素は、RecalculateNormals /
        /// RecalculateSmoothNormals の直前に法線を退避し、計算後に書き戻す。
        ///   - 頂点集合: その頂点を参照する全ての面コーナー
        ///   - 面集合  : その面の全コーナー
        ///   - 辺集合  : 両端頂点として扱う
        ///   - 線集合  : 対象外
        /// </summary>
        public List<Poly_Ling.Selection.PartsSelectionSet> NormalRecalcExcludeList { get; set; }
            = new List<Poly_Ling.Selection.PartsSelectionSet>();

        /// <summary>
        /// ミラー分岐のルートか。
        /// true の場合、ヒエラルキーエクスポート時にこのノード配下を
        /// 実体側とミラー側（MirrorSide を祖先に持つノード）の2本の枝に分割する。
        /// 非スキンドメッシュ専用。
        /// </summary>
        public bool IsMirrorBranchRoot { get; set; } = false;

        /// <summary>
        /// エクスポート時のローカルトランスフォーム
        /// </summary>
        public BoneTransform BoneTransform { get; set; } = new BoneTransform();

        /// <summary>
        /// ミラー実体化（in-place ベイク）の状態。null = 未実体化。
        /// 対称面をまたぐ処理のために一時的に反対側を生やしている間だけ保持し、
        /// 解除で null に戻す。MeshObject に持たせているのは、Undo のスナップショットが
        /// MeshObject 単位で取られるため（Clone で引き継ぐ）。
        /// </summary>
        public Poly_Ling.Tools.MirrorBakeResult MirrorBakeState { get; set; } = null;

        // ================================================================
        // 付帯データ（IK / 剛体 / JOINT / SpringBone）— 純POCOデータ契約
        // ================================================================
        //
        // ================================================================
        // 【ボーン付帯データ格納規約（厳守）】※本ブロックを正典とする
        // ----------------------------------------------------------------
        //   1. per-bone POCO 統一
        //      ボーン付帯データ（IK / 剛体 / JOINT / SpringBone / Humanoid割当）は
        //      Type == MeshType.Bone の MeshObject に per-bone POCO として持つ。
        //      null = 当該属性を持たない。#if UNITY_EDITOR を含めない。
        //
        //   2. 参照は name主・index従
        //      ボーン間参照は付帯先/相手の MeshObject.Name を一次キーとする。
        //      index は実行時キャッシュであり、並べ替え・I/O で無効化されうるため
        //      永続化の基準に用いない。IK の TargetIndex/BoneIndex も本規約に従い
        //      name を一次キー、index をキャッシュへ降格する。
        //
        //   3. モデルレベルは「モデル固有」と「派生ビュー」のみ
        //      ModelContext に置いてよいのは、真にモデル固有のもの
        //      （例: SpringBoneColliderGroupNames）と、per-bone から再構築できる
        //      派生ビュー／キャッシュ（例: HumanoidMapping の name→index Dict）のみ。
        //      派生ビューは per-bone を正として導出する。
        //      Humanoid割当は per-bone を正とし、同一 Humanoidボーンを複数ボーンが
        //      主張しない一意性を不変条件として維持する。
        //
        //   4. 永続化は CSV/JSON 対称
        //      両経路で同じ付帯データを読み書きする（片方のみの実装を残さない）。
        //      座標系変換は I/O 境界のみで行い、POCO は生値（Unity左手系）を保持する。
        //
        //   【運用（IK / Humanoid の派生ビューと同期タイミング）】
        //      per-bone を永続 canonical とする一方、実行時は集約ビューを working
        //      として使う（consumer は集約ビューを読む）。両者の同期は
        //      「保存・読込の境界のみ」で行い、編集中のリアルタイム同期はしない。
        //        - IK       : per-bone(EffectorBoneName/IKLink) ⇔ 集約(IKData.Links)。
        //                     import 時に Sync 済み（ImportCommands）。
        //        - Humanoid : per-bone(MeshObject.HumanBodyBone) ⇔ Dict(HumanoidMapping)。
        //                     割当は UI 経由で Dict を編集し、per-bone へは保存時 Sync、
        //                     読込時 Rebuild のみ（import は Dict を確立しないため無同期）。
        //      同期の実体は IKChainResolver / HumanoidMappingResolver（境界で呼ぶ）。
        // ================================================================
        //
        // 【設計方針】
        //   IK・剛体・JOINT・SpringBone を MeshObject に統一して持たせる。これにより
        //   クロス言語移植（Python/JavaScript）と Unityヒエラルキー
        //   エクスポートのための単一データ契約が MeshObject 上で完結する。
        //   いずれも #if UNITY_EDITOR を含まない POCO（IKData/RigidBodyData/
        //   JointData/SpringBone*）であり、null = 当該属性を持たないことを表す。
        //
        // 【役割の判別は Type(MeshType) を流用】
        //   - 剛体     : Type == MeshType.RigidBody       かつ RigidBodyData != null
        //   - JOINT    : Type == MeshType.RigidBodyJoint  かつ JointData    != null
        //   - IKボーン : Type == MeshType.Bone            かつ IKData       != null
        //
        // 【MeshContext との関係】
        //   従来 MeshContext が保持していた IK フィールド（IsIK 等）の実体は
        //   本 IKData に移設済み。MeshContext 側は後方互換のための薄い委譲
        //   プロパティのみを公開する（Type / BoneTransform と同一パターン）。
        // ----------------------------------------------------------------

        /// <summary>
        /// IKデータ（IKボーンのみ非null）。
        /// 非null ⇔ このボーンはIKボーン。
        /// </summary>
        public IKData IKData { get; set; } = null;

        /// <summary>
        /// IKリンクデータ（IKチェーンのリンクボーンのみ非null）。
        /// 非null ⇔ このボーンはIKリンク。所属チェーン・順序は IKルートの
        /// EffectorBoneName から階層(HierarchyParentIndex)で導出する（IKChainResolver）。
        /// ※#4a: 追加のみ。現段階の源泉は IKData.Links。
        /// </summary>
        public IKLinkData IKLink { get; set; } = null;

        /// <summary>
        /// 剛体データ（Type == MeshType.RigidBody のとき非null）。
        /// 頂点/面は持たず、形状はギズモとして利用時に生成する。
        /// </summary>
        public RigidBodyData RigidBodyData { get; set; } = null;

        /// <summary>
        /// JOINTデータ（Type == MeshType.RigidBodyJoint のとき非null）。
        /// 接続剛体A/Bを名前で参照する（index従）。
        /// </summary>
        public JointData JointData { get; set; } = null;

        // ------------------------------------------------------------
        // スプリングボーン付帯データ（Type == MeshType.Bone のボーンに付く）
        //   VRM SpringBone(VRMC_springBone) 由来。物理演算(RigidBody/Joint)とは別物。
        //   - コライダー : SpringBoneColliders（1ボーンに複数可。null/空=なし）
        //   - ジョイント : SpringBoneJoint（揺れチェーンメンバー。非null=揺れjoint）
        //   - チェーンルート : SpringBoneChainRoot（チェーン起点ボーンのみ。非null=ルート）
        //   チェーンの形状・順序はボーン階層(HierarchyParentIndex)＋SpringBoneJoint有無
        //   から導出する（明示的な順序リストは持たない）。
        // ------------------------------------------------------------

        /// <summary>スプリングボーン・コライダー（付帯ボーンに複数可。null/空=なし）。</summary>
        public List<SpringBoneColliderData> SpringBoneColliders { get; set; } = null;

        /// <summary>スプリングボーン・ジョイント（非null=揺れチェーンのメンバー）。</summary>
        public SpringBoneJointData SpringBoneJoint { get; set; } = null;

        /// <summary>スプリングボーン・チェーンルート（非null=このボーンがチェーン起点）。</summary>
        public SpringBoneChainData SpringBoneChainRoot { get; set; } = null;

        // ------------------------------------------------------------
        // Humanoid 割当（Type == MeshType.Bone のボーンに付く）
        //   規約: MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        //   このボーンが対応する Unity Humanoid 名（例 "LeftUpperArm"）。空=非割当。
        //   モデルレベルの HumanoidMapping（name→index Dict）は本欄からの派生ビュー。
        //   ※#5a: 追加のみ。現段階の源泉は ModelContext.HumanoidMapping。
        //     相互同期は HumanoidMappingResolver で行う（併存・非破壊）。
        // ------------------------------------------------------------

        /// <summary>Unity Humanoid 割当名（空=非割当）。</summary>
        public string HumanBodyBone { get; set; } = "";

        /// <summary>
        /// Humanoid マッスル可動域（null=Unity 既定を使う）。
        /// 3マッスル軸は Min/Max/Center の Vector3 成分で表現する。
        /// ※5d-1: 格納のみ。consumer 差し替えは 5d-2。
        /// </summary>
        public HumanLimitData HumanLimit { get; set; } = null;

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => Vertices.Count;

        /// <summary>面数</summary>
        public int FaceCount => Faces.Count;

        /// <summary>三角形数（全面の合計）</summary>
        public int TriangleCount => Faces.Sum(f => f.TriangleCount);

        /// <summary>サブメッシュ数（使用されているマテリアルインデックスの最大値+1）</summary>
        public int SubMeshCount
        {
            get
            {
                if (Faces.Count == 0) return 1;
                int maxMatIndex = Faces.Max(f => f.MaterialIndex);
                return maxMatIndex + 1;
            }
        }

        /// <summary>スキンドメッシュか（1つ以上の頂点がBoneWeightを持つ）</summary>
        public bool HasBoneWeight => Vertices.Any(v => v.HasBoneWeight);

        // === コンストラクタ ===

        public MeshObject() { }

        public MeshObject(string name)
        {
            Name = name;
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

        // ================================================================
        // Unity Mesh 変換（頂点共有版）
        // ================================================================

        /// <summary>
        /// Unity Meshに変換（頂点共有版）
        /// (頂点インデックス, UVサブインデックス, 法線サブインデックス) の組み合わせで頂点を共有
        /// MQO読み込み時の CreateFaceAndModifyVertex 方式に対応
        /// </summary>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMeshShared(int materialCount = -1)
        {
            return PLMeshBridge.I.ToUnityMeshShared(this, materialCount);
        }

        /// <summary>
        /// Unity Meshに変換（頂点共有版・座標変換付き）
        /// </summary>
        /// <param name="transform">頂点に適用する変換行列</param>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMeshShared(Matrix4x4 transform, int materialCount = -1)
        {
            return PLMeshBridge.I.ToUnityMeshShared(this, transform, materialCount);
        }

        /// <summary>
        /// Unity Meshから読み込み
        /// </summary>
        /// <param name="mesh">読み込み元のMesh</param>
        /// <param name="mergeVertices">同一位置の頂点を統合するか</param>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices = true)
        {
            FromUnityMesh(mesh, mergeVertices, false);
        }

        /// <summary>
        /// Unity MeshからMeshObjectを構築
        /// </summary>
        /// <param name="mesh">ソースメッシュ</param>
        /// <param name="mergeVertices">同一位置の頂点を統合するか</param>
        /// <param name="includeBoneWeights">BoneWeight情報を読み込むか（スキンドメッシュ用）</param>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices, bool includeBoneWeights)
        {
            PLMeshBridge.I.FromUnityMesh(this, mesh, mergeVertices, includeBoneWeights);
        }

        /// <summary>
        /// 三角形だけを既存の Unity Mesh へ張り直す。面の表示/非表示切替に使う。
        /// 展開頂点数が変わっている場合は何もせず false を返す。
        /// </summary>
        public bool ApplyTrianglesToUnityMesh(Mesh mesh, int materialCount = -1)
        {
            return PLMeshBridge.I.ApplyTrianglesInPlace(mesh, this, materialCount);
        }

        /// <summary>
        /// 法線だけを既存の Unity Mesh へ反映する。展開頂点数が変わっている場合は
        /// 何もせず false を返す（呼び出し側でメッシュを作り直すこと）。
        /// </summary>
        public bool ApplyNormalsToUnityMesh(Mesh mesh)
        {
            return PLMeshBridge.I.ApplyNormalsInPlace(mesh, this);
        }
        // === ユーティリティ ===

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Faces.Clear();
            _positionCacheDirty = true;
        }

        /// <summary>
        /// 全ての面の法線を自動計算（フラット）。UVスロットを分割する既定動作。
        /// </summary>
        public void RecalculateNormals()
        {
            RecalculateNormals(splitSlots: true);
        }

        /// <summary>
        /// 全ての面の法線を自動計算（フラット）。
        ///
        /// splitSlots = true:
        ///   面コーナーの (UV値, 面法線) の一意な組ごとに UV/法線スロットを割り当て直す。
        ///   ハードエッジを正しく表現できるが、UVスロット数（＝展開頂点数）が増える。
        /// splitSlots = false:
        ///   既存の UV スロット数を維持し、各スロットへ面法線を書き込む。
        ///   同じスロットを共有する面が複数ある場合は面順で最後の面法線が残る。
        ///   モーフ関連メッシュ（親子で展開index空間を一致させる必要がある）向け。
        ///
        /// いずれも UVs.Count == Normals.Count と UVIndices[j] == NormalIndices[j] を保つ。
        /// </summary>
        public void RecalculateNormals(bool splitSlots)
        {
            // 除外セットの法線を退避（計算後に書き戻す）
            var normalBackup = CaptureNormalRecalcExcluded();

            var faceNormals = ComputeFaceNormals();

            if (splitSlots)
                RebuildSlotsBySplit(faceNormals);
            else
                WriteFaceNormalsIntoExistingSlots(faceNormals);

            // 除外セットの法線を復帰
            RestoreNormalRecalcExcluded(normalBackup);
        }

        /// <summary>面ごとの面法線。3頂点未満の面は Vector3.up。</summary>
        private Vector3[] ComputeFaceNormals()
        {
            var faceNormals = new Vector3[Faces.Count];
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                if (face.VertexCount < 3)
                {
                    faceNormals[fi] = Vector3.up;
                    continue;
                }
                faceNormals[fi] = NormalHelper.CalculateFaceNormal(
                    Vertices[face.VertexIndices[0]].Position,
                    Vertices[face.VertexIndices[1]].Position,
                    Vertices[face.VertexIndices[2]].Position);
            }
            return faceNormals;
        }

        /// <summary>
        /// (UV値, 面法線) の一意な組ごとにスロットを作り直す。
        /// 3頂点未満の面（補助線）は既存法線をそのまま持ち込む。
        /// 面から参照されない頂点は既存スロットを維持する。
        /// </summary>
        private void RebuildSlotsBySplit(Vector3[] faceNormals)
        {
            int vertCount = Vertices.Count;
            var oldUVs     = new List<Vector2>[vertCount];
            var oldNormals = new List<Vector3>[vertCount];
            var newUVs     = new List<Vector2>[vertCount];
            var newNormals = new List<Vector3>[vertCount];

            for (int vi = 0; vi < vertCount; vi++)
            {
                oldUVs[vi]     = new List<Vector2>(Vertices[vi].UVs);
                oldNormals[vi] = new List<Vector3>(Vertices[vi].Normals);
                newUVs[vi]     = new List<Vector2>();
                newNormals[vi] = new List<Vector3>();
            }

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= vertCount)
                    {
                        face.UVIndices[j] = 0;
                        face.NormalIndices.Add(0);
                        continue;
                    }

                    int oldSlot = face.UVIndices[j];

                    Vector2 uv = Vector2.zero;
                    var ou = oldUVs[vIdx];
                    if (oldSlot >= 0 && oldSlot < ou.Count) uv = ou[oldSlot];
                    else if (ou.Count > 0) uv = ou[0];

                    Vector3 n;
                    if (face.VertexCount >= 3)
                    {
                        n = faceNormals[fi];
                    }
                    else
                    {
                        var on = oldNormals[vIdx];
                        n = (oldSlot >= 0 && oldSlot < on.Count) ? on[oldSlot]
                          : (on.Count > 0 ? on[0] : Vector3.up);
                    }

                    int slot = FindOrAddSlot(newUVs[vIdx], newNormals[vIdx], uv, n);
                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);
                }
            }

            for (int vi = 0; vi < vertCount; vi++)
            {
                var vertex = Vertices[vi];
                if (newUVs[vi].Count == 0)
                {
                    vertex.EnsureNormalSlots();
                    continue;
                }
                vertex.UVs     = newUVs[vi];
                vertex.Normals = newNormals[vi];
            }
        }

        /// <summary>(UV値, 法線) の組を検索し、無ければ両リストへ追加して添字を返す。</summary>
        private static int FindOrAddSlot(
            List<Vector2> uvs, List<Vector3> normals, Vector2 uv, Vector3 normal)
        {
            int count = Mathf.Min(uvs.Count, normals.Count);
            for (int i = 0; i < count; i++)
            {
                if (Vector2.Distance(uvs[i], uv) < 0.0001f &&
                    Vector3.Distance(normals[i], normal) < 0.0001f)
                    return i;
            }
            uvs.Add(uv);
            normals.Add(normal);
            return uvs.Count - 1;
        }

        /// <summary>
        /// 既存のUVスロット数を維持したまま面法線を書き込む。
        /// 同一スロットを共有する面が複数ある場合は面順で最後の面法線が残る。
        /// </summary>
        private void WriteFaceNormalsIntoExistingSlots(Vector3[] faceNormals)
        {
            foreach (var vertex in Vertices)
                vertex.EnsureNormalSlots();

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= Vertices.Count)
                    {
                        face.UVIndices[j] = 0;
                        face.NormalIndices.Add(0);
                        continue;
                    }

                    var vertex = Vertices[vIdx];
                    int slot = face.UVIndices[j];
                    if (slot < 0 || slot >= vertex.Normals.Count) slot = 0;

                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);

                    if (face.VertexCount >= 3 && slot < vertex.Normals.Count)
                        vertex.Normals[slot] = faceNormals[fi];
                }
            }
        }

        /// <summary>
        /// スムーズ法線を計算（同一頂点の法線を平均化）。
        /// UVスロット数は変えず、全スロットへ同じ平滑法線を書き込む。
        /// UVs.Count == Normals.Count と UVIndices[j] == NormalIndices[j] を保つ。
        /// </summary>
        public void RecalculateSmoothNormals()
        {
            // 除外セットの法線を退避（計算後に書き戻す）
            var normalBackup = CaptureNormalRecalcExcluded();

            // 頂点ごとに面法線を積算
            var accum = new Vector3[Vertices.Count];
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = NormalHelper.CalculateFaceNormal(v0, v1, v2);

                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < accum.Length)
                        accum[vIdx] += faceNormal;
                }
            }

            // 全スロットへ書き込む（スロット数は維持）
            for (int vi = 0; vi < Vertices.Count; vi++)
            {
                var vertex = Vertices[vi];
                vertex.EnsureNormalSlots();
                if (vertex.Normals.Count == 0)
                    continue;

                Vector3 n = accum[vi].sqrMagnitude > 1e-12f
                    ? accum[vi].normalized
                    : vertex.Normals[0];

                for (int slot = 0; slot < vertex.Normals.Count; slot++)
                    vertex.Normals[slot] = n;
            }

            // 面の法線インデックスをUVサブindexへ合わせる
            foreach (var face in Faces)
            {
                int corners = face.VertexIndices.Count;

                while (face.UVIndices.Count < corners) face.UVIndices.Add(0);
                while (face.UVIndices.Count > corners) face.UVIndices.RemoveAt(face.UVIndices.Count - 1);
                face.NormalIndices.Clear();

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    int slotCount = (vIdx >= 0 && vIdx < Vertices.Count)
                        ? Vertices[vIdx].Normals.Count : 0;

                    int slot = face.UVIndices[j];
                    if (slot < 0 || slot >= slotCount) slot = 0;

                    face.UVIndices[j] = slot;
                    face.NormalIndices.Add(slot);
                }
            }

            // 除外セットの法線を復帰
            RestoreNormalRecalcExcluded(normalBackup);
        }

        // ================================================================
        // 法線の自動再計算 除外セット
        // ================================================================

        /// <summary>法線退避エントリ（面index / コーナーindex / 法線）。</summary>
        private struct NormalBackupEntry
        {
            public int FaceIndex;
            public int Corner;
            public Vector3 Normal;
        }

        /// <summary>除外セットが空でないか。</summary>
        public bool HasNormalRecalcExclude
        {
            get
            {
                if (NormalRecalcExcludeList == null) return false;
                foreach (var set in NormalRecalcExcludeList)
                {
                    if (set == null) continue;
                    if (set.Vertices.Count > 0 || set.Edges.Count > 0 || set.Faces.Count > 0)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// 除外セットを「頂点集合（辺は両端頂点）」と「面集合」に分解する。
        /// </summary>
        private void CollectNormalRecalcExcludeSets(out HashSet<int> verts, out HashSet<int> faces)
        {
            verts = new HashSet<int>();
            faces = new HashSet<int>();
            if (NormalRecalcExcludeList == null) return;

            foreach (var set in NormalRecalcExcludeList)
            {
                if (set == null) continue;

                foreach (int vi in set.Vertices)
                    if (vi >= 0 && vi < Vertices.Count) verts.Add(vi);

                foreach (var e in set.Edges)
                {
                    if (e.V1 >= 0 && e.V1 < Vertices.Count) verts.Add(e.V1);
                    if (e.V2 >= 0 && e.V2 < Vertices.Count) verts.Add(e.V2);
                }

                foreach (int fi in set.Faces)
                    if (fi >= 0 && fi < Faces.Count) faces.Add(fi);
            }
        }

        /// <summary>
        /// 除外セットが指す頂点インデックス集合。面集合はその構成頂点へ展開する。
        /// Unity Mesh 側は法線を頂点単位でしか持てないため、その復帰用。
        /// </summary>
        public HashSet<int> GetNormalRecalcExcludedVertexIndices()
        {
            CollectNormalRecalcExcludeSets(out var verts, out var faces);
            foreach (int fi in faces)
            {
                foreach (int vi in Faces[fi].VertexIndices)
                    if (vi >= 0 && vi < Vertices.Count) verts.Add(vi);
            }
            return verts;
        }

        /// <summary>
        /// 除外セットが指すコーナーの法線を退避する。対象が無ければ null。
        /// </summary>
        private List<NormalBackupEntry> CaptureNormalRecalcExcluded()
        {
            if (NormalRecalcExcludeList == null || NormalRecalcExcludeList.Count == 0) return null;

            CollectNormalRecalcExcludeSets(out var excludedVerts, out var excludedFaces);
            if (excludedVerts.Count == 0 && excludedFaces.Count == 0) return null;

            var backup = new List<NormalBackupEntry>();
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                bool faceExcluded = excludedFaces.Contains(fi);
                int corners = Mathf.Min(face.VertexIndices.Count, face.NormalIndices.Count);

                for (int j = 0; j < corners; j++)
                {
                    int vIdx = face.VertexIndices[j];
                    if (vIdx < 0 || vIdx >= Vertices.Count) continue;
                    if (!faceExcluded && !excludedVerts.Contains(vIdx)) continue;

                    var normals = Vertices[vIdx].Normals;
                    int nIdx = face.NormalIndices[j];
                    if (nIdx < 0 || nIdx >= normals.Count) continue;

                    backup.Add(new NormalBackupEntry
                    {
                        FaceIndex = fi,
                        Corner    = j,
                        Normal    = normals[nIdx]
                    });
                }
            }
            return backup.Count > 0 ? backup : null;
        }

        /// <summary>
        /// 退避した法線を書き戻す。
        ///
        /// UVs.Count == Normals.Count / UVIndices[j] == NormalIndices[j] の不変条件下では、
        /// コーナーの法線スロットは UVサブindex と同一なので、そのスロットへ値を書くだけでよい。
        /// 同一スロットを除外コーナーと非除外コーナーが共有する場合は退避値が優先される。
        /// </summary>
        private void RestoreNormalRecalcExcluded(List<NormalBackupEntry> backup)
        {
            if (backup == null || backup.Count == 0) return;

            foreach (var entry in backup)
            {
                if (entry.FaceIndex < 0 || entry.FaceIndex >= Faces.Count) continue;
                var face = Faces[entry.FaceIndex];
                if (entry.Corner < 0 || entry.Corner >= face.VertexIndices.Count) continue;
                if (entry.Corner >= face.UVIndices.Count) continue;

                int vIdx = face.VertexIndices[entry.Corner];
                if (vIdx < 0 || vIdx >= Vertices.Count) continue;

                var vertex = Vertices[vIdx];
                int slot = face.UVIndices[entry.Corner];
                if (slot < 0 || slot >= vertex.Normals.Count) continue;

                vertex.Normals[slot] = entry.Normal;
                if (entry.Corner < face.NormalIndices.Count)
                    face.NormalIndices[entry.Corner] = slot;
            }
        }

        /// <summary>
        /// UV/法線スロットの不変条件を検証する。
        /// </summary>
        public bool ValidateUVNormalSlots(out string message)
        {
            var sb = new System.Text.StringBuilder();
            int vertexErrors = 0;
            int faceErrors = 0;

            for (int vi = 0; vi < Vertices.Count; vi++)
            {
                var vertex = Vertices[vi];
                if (vertex.UVs.Count != vertex.Normals.Count)
                {
                    if (vertexErrors < 5)
                        sb.AppendLine($"vertex[{vi}] UVs={vertex.UVs.Count} Normals={vertex.Normals.Count}");
                    vertexErrors++;
                }
            }

            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var face = Faces[fi];
                bool bad = face.UVIndices.Count != face.NormalIndices.Count;

                if (!bad)
                {
                    for (int j = 0; j < face.UVIndices.Count; j++)
                    {
                        if (face.UVIndices[j] != face.NormalIndices[j]) { bad = true; break; }
                    }
                }

                if (bad)
                {
                    if (faceErrors < 5)
                        sb.AppendLine($"face[{fi}] UVIndices/NormalIndices mismatch");
                    faceErrors++;
                }
            }

            if (vertexErrors == 0 && faceErrors == 0)
            {
                message = $"[{Name}] OK";
                return true;
            }

            message = $"[{Name}] vertexErrors={vertexErrors} faceErrors={faceErrors}\n{sb}";
            return false;
        }

        /// <summary>
        /// スプリングボーン・コライダーリストのディープコピー（null は null のまま）。
        /// </summary>
        private static List<SpringBoneColliderData> CloneSpringBoneColliders(List<SpringBoneColliderData> src)
        {
            if (src == null) return null;
            var dst = new List<SpringBoneColliderData>(src.Count);
            foreach (var c in src)
                dst.Add(c?.Clone());
            return dst;
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public MeshObject Clone()
        {
            var copy = new MeshObject(Name);
            copy.Type = this.Type;
            copy.IsTriangulated = this.IsTriangulated;
            copy.Vertices = Vertices.Select(v => v.Clone()).ToList();
            copy.Faces = Faces.Select(f => f.Clone()).ToList();
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;
            copy.IgnorePoseInArmature = this.IgnorePoseInArmature;
            copy.IsMirrorBranchRoot   = this.IsMirrorBranchRoot;
            copy.PreserveNormals      = this.PreserveNormals;
            copy.MirrorBakeState      = this.MirrorBakeState?.Clone();
            copy.NormalRecalcExcludeList = this.NormalRecalcExcludeList?.Select(s => s.Clone()).ToList()
                                           ?? new List<Poly_Ling.Selection.PartsSelectionSet>();

            if(this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

            // 付帯データ（IK/剛体/JOINT）をディープコピー（nullはnullのまま）
            copy.IKData = this.IKData?.Clone();
            copy.IKLink = this.IKLink?.Clone();
            copy.RigidBodyData = this.RigidBodyData?.Clone();
            copy.JointData = this.JointData?.Clone();

            // スプリングボーン付帯データをディープコピー（nullはnullのまま）
            copy.SpringBoneColliders = CloneSpringBoneColliders(this.SpringBoneColliders);
            copy.SpringBoneJoint = this.SpringBoneJoint?.Clone();
            copy.SpringBoneChainRoot = this.SpringBoneChainRoot?.Clone();
            copy.HumanBodyBone = this.HumanBodyBone;
            copy.HumanLimit = this.HumanLimit?.Clone();

            // ID管理セットを再構築
            copy.RebuildIdSets();

            return copy;
        }

        /// <summary>
        /// ディープコピー（頂点・面に新しいIDを割り当て）
        /// </summary>
        public MeshObject CloneWithNewIds()
        {
            var copy = new MeshObject(Name);
            copy.Type = this.Type;
            copy.IsTriangulated = this.IsTriangulated;
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;
            copy.IgnorePoseInArmature = this.IgnorePoseInArmature;
            copy.IsMirrorBranchRoot   = this.IsMirrorBranchRoot;
            copy.PreserveNormals      = this.PreserveNormals;
            copy.MirrorBakeState      = this.MirrorBakeState?.Clone();
            copy.NormalRecalcExcludeList = this.NormalRecalcExcludeList?.Select(s => s.Clone()).ToList()
                                           ?? new List<Poly_Ling.Selection.PartsSelectionSet>();

            if (this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

            // 付帯データ（IK/剛体/JOINT）をディープコピー。
            // 内部のボーン/剛体参照は index/name のいずれもオブジェクトIDとは
            // 独立のため、頂点・面のID再割り当てとは無関係にそのまま複製する。
            copy.IKData = this.IKData?.Clone();
            copy.IKLink = this.IKLink?.Clone();
            copy.RigidBodyData = this.RigidBodyData?.Clone();
            copy.JointData = this.JointData?.Clone();

            // スプリングボーン付帯データをディープコピー（頂点/面IDとは独立）。
            copy.SpringBoneColliders = CloneSpringBoneColliders(this.SpringBoneColliders);
            copy.SpringBoneJoint = this.SpringBoneJoint?.Clone();
            copy.SpringBoneChainRoot = this.SpringBoneChainRoot?.Clone();
            copy.HumanBodyBone = this.HumanBodyBone;
            copy.HumanLimit = this.HumanLimit?.Clone();

            // 頂点をコピー（新しいID）
            foreach (var v in Vertices)
            {
                var newV = v.Clone();
                newV.Id = copy.GenerateVertexId();
                copy.Vertices.Add(newV);
            }

            // 面をコピー（新しいID）
            foreach (var f in Faces)
            {
                var newF = f.Clone();
                newF.Id = copy.GenerateFaceId();
                copy.Faces.Add(newF);
            }

            return copy;
        }


        /// <summary>
        /// バウンディングボックスを計算
        /// </summary>
        public Bounds CalculateBounds()
        {
            if (Vertices.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 min = Vertices[0].Position;
            Vector3 max = Vertices[0].Position;

            foreach (var vertex in Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>
        /// マテリアル使用状況を取得
        /// </summary>
        /// <returns>Key: MaterialIndex, Value: 使用面数</returns>
        public Dictionary<int, int> GetMaterialUsage()
        {
            var usage = new Dictionary<int, int>();
            foreach (var face in Faces)
            {
                if (!usage.ContainsKey(face.MaterialIndex))
                    usage[face.MaterialIndex] = 0;
                usage[face.MaterialIndex]++;
            }
            return usage;
        }

        /// <summary>
        /// 指定マテリアルインデックスの面を取得
        /// </summary>
        public IEnumerable<int> GetFacesByMaterial(int materialIndex)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].MaterialIndex == materialIndex)
                    yield return i;
            }
        }

        /// <summary>
        /// 選択した面のマテリアルインデックスを変更
        /// </summary>
        public void SetFacesMaterial(IEnumerable<int> faceIndices, int materialIndex)
        {
            foreach (int idx in faceIndices)
            {
                if (idx >= 0 && idx < Faces.Count)
                {
                    Faces[idx].MaterialIndex = materialIndex;
                }
            }
        }

        /// <summary>
        /// 指定フラグを持つ頂点を取得
        /// </summary>
        public IEnumerable<int> GetVerticesByFlag(VertexFlags flag)
        {
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (Vertices[i].HasFlag(flag))
                    yield return i;
            }
        }

        /// <summary>
        /// 指定フラグを持つ面を取得
        /// </summary>
        public IEnumerable<int> GetFacesByFlag(FaceFlags flag)
        {
            for (int i = 0; i < Faces.Count; i++)
            {
                if (Faces[i].HasFlag(flag))
                    yield return i;
            }
        }

        /// <summary>
        /// 全頂点のフラグをクリア
        /// </summary>
        public void ClearAllVertexFlags()
        {
            foreach (var v in Vertices)
                v.Flags = VertexFlags.None;
        }

        /// <summary>
        /// 全面のフラグをクリア
        /// </summary>
        public void ClearAllFaceFlags()
        {
            foreach (var f in Faces)
                f.Flags = FaceFlags.None;
        }

        /// <summary>
        /// 全頂点のボーンウェイトをクリア
        /// </summary>
        public void ClearAllBoneWeights()
        {
            foreach (var v in Vertices)
                v.BoneWeight = null;
        }

        /// <summary>
        /// デバッグ情報
        /// </summary>
        public string GetDebugInfo()
        {
            int triCount = Faces.Where(f => f.IsTriangle).Count();
            int quadCount = Faces.Where(f => f.IsQuad).Count();
            int nGonCount = Faces.Count - triCount - quadCount;
            int subMeshCount = SubMeshCount;

            return $"[{Name}] Vertices: {VertexCount}, Faces: {FaceCount} " +
                   $"(Tri: {triCount}, Quad: {quadCount}, NGon: {nGonCount}), SubMeshes: {subMeshCount}";
        }

        /// <summary>
        /// UV展開前後の頂点インデックス対応辞書を構築する。
        /// ToUnityMesh / AppendExpandedVertices と同じ展開順序。
        /// key: (rawVertexIndex, uvSubIndex) → value: 展開後インデックス
        /// </summary>
        public Dictionary<(int vIdx, int uvIdx), int> BuildExpansionMap()
        {
            // face.IsHidden は見ない（MeshBridgeDefault.ToUnityMesh と同じ規則。
            // 面の非表示で展開頂点数が変わると展開index空間が食い違う）。
            var nonIsolated = new HashSet<int>();
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3) continue;
                foreach (int vi in face.VertexIndices) nonIsolated.Add(vi);
            }

            var map = new Dictionary<(int vIdx, int uvIdx), int>();
            int expandedIdx = 0;

            for (int vIdx = 0; vIdx < Vertices.Count; vIdx++)
            {
                if (!nonIsolated.Contains(vIdx)) continue;
                var vertex = Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    map[(vIdx, uvIdx)] = expandedIdx++;
                }
            }

            return map;
        }

        /// <summary>
        /// UV展開後インデックス → (rawVertexIndex, uvSubIndex) の逆引き辞書を構築する。
        /// </summary>
        public Dictionary<int, (int vIdx, int uvIdx)> BuildInverseExpansionMap()
        {
            // face.IsHidden は見ない（MeshBridgeDefault.ToUnityMesh と同じ規則。
            // 面の非表示で展開頂点数が変わると展開index空間が食い違う）。
            var nonIsolated = new HashSet<int>();
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3) continue;
                foreach (int vi in face.VertexIndices) nonIsolated.Add(vi);
            }

            var map = new Dictionary<int, (int vIdx, int uvIdx)>();
            int expandedIdx = 0;

            for (int vIdx = 0; vIdx < Vertices.Count; vIdx++)
            {
                if (!nonIsolated.Contains(vIdx)) continue;
                var vertex = Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    map[expandedIdx++] = (vIdx, uvIdx);
                }
            }

            return map;
        }
    }

    // ============================================================
    // ヘルパークラス
    // ============================================================

    /// <summary>
    /// Vector3 比較用（Dictionary キー用）
    /// </summary>
    internal class Vector3Comparer : IEqualityComparer<Vector3>
    {
        private const float Tolerance = 0.00001f;

        public bool Equals(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b) < Tolerance;
        }

        public int GetHashCode(Vector3 v)
        {
            // 精度を落としてハッシュ化（近い値が同じハッシュになるように）
            int x = Mathf.RoundToInt(v.x * 10000);
            int y = Mathf.RoundToInt(v.y * 10000);
            int z = Mathf.RoundToInt(v.z * 10000);
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }
    }
}
