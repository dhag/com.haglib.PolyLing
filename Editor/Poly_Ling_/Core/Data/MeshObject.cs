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
        BakedMirror = 7
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
        /// 頂点IDを登録（外部からインポート時等に使用）
        /// </summary>
        public void RegisterVertexId(int id)
        {
            EnsureIdSetsInitialized();
            if (id != 0)
                _usedVertexIds.Add(id);
        }

        /// <summary>
        /// 面IDを登録（外部からインポート時等に使用）
        /// </summary>
        public void RegisterFaceId(int id)
        {
            EnsureIdSetsInitialized();
            if (id != 0)
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
                if (v.Id != 0)
                    _usedVertexIds.Add(v.Id);
            }
            foreach (var f in Faces)
            {
                if (f.Id != 0)
                    _usedFaceIds.Add(f.Id);
            }
        }

        /// <summary>
        /// IDが未設定の頂点・面にIDを割り当て
        /// </summary>
        public void AssignMissingIds()
        {
            EnsureIdSetsInitialized();
            foreach (var v in Vertices)
            {
                if (v.Id == 0)
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
                if (f.Id == 0)
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

        /// <summary>
        /// 頂点が展開済みか（PMX形式）
        /// true: 各頂点が1つのUV/法線を持つ（展開済み、PMX互換）
        /// false: 頂点が複数のUV/法線を持つ可能性あり（MQO形式）
        /// </summary>
        public bool IsExpanded { get; set; } = false;




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
        /// エクスポート時のローカルトランスフォーム
        /// </summary>
        public BoneTransform BoneTransform { get; set; } = new BoneTransform();
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
        public bool IsSkinned => Vertices.Any(v => v.HasBoneWeight);

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
            return Vertices.Count - 1;
        }

        /// <summary>
        /// Vertexオブジェクトを追加（IDが0なら自動割り当て）
        /// </summary>
        public int AddVertex(Vertex vertex)
        {
            if (vertex.Id == 0)
            {
                vertex.Id = GenerateVertexId();
            }
            else
            {
                RegisterVertexId(vertex.Id);
            }
            Vertices.Add(vertex);
            return Vertices.Count - 1;
        }

        /// <summary>
        /// Vertexオブジェクトを追加（ID管理なし、後方互換用）
        /// </summary>
        public int AddVertexRaw(Vertex vertex)
        {
            Vertices.Add(vertex);
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
        /// Faceオブジェクトを追加（IDが0なら自動割り当て）
        /// </summary>
        public int AddFace(Face face)
        {
            if (face.Id == 0)
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
            var mesh = new Mesh();
            mesh.name = Name;

            if (Vertices.Count == 0)
                return mesh;

            // サブメッシュ数を計算（materialCount指定時はそれを使用）
            int subMeshCount = materialCount > 0 ? materialCount : SubMeshCount;

            // === FPX仕様: 頂点順 → UV順 で頂点を展開 ===
            // キー: (頂点インデックス, UVサブインデックス)
            // 値:  Unity頂点インデックス
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = IsSkinned;

            // パス1: 頂点順 → UV順 で頂点データを作成
            for (int vIdx = 0; vIdx < Vertices.Count; vIdx++)
            {
                var vertex = Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int unityIdx = unityVerts.Count;
                    vertexMapping[(vIdx, uvIdx)] = unityIdx;

                    // 位置
                    unityVerts.Add(vertex.Position);

                    // UV
                    if (uvIdx < vertex.UVs.Count)
                        unityUVs.Add(vertex.UVs[uvIdx]);
                    else
                        unityUVs.Add(Vector2.zero);

                    // 法線（最初の法線を使用、なければデフォルト）
                    if (vertex.Normals.Count > 0)
                        unityNormals.Add(vertex.Normals[0]);
                    else
                        unityNormals.Add(Vector3.up);

                    // BoneWeight
                    if (hasBoneWeights)
                    {
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                    }
                }
            }

            // パス2: 面の三角形インデックスを構築
            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in Faces)
            {
                // 補助線（2頂点）や非表示面はスキップ
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                // 三角形に分解
                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= Vertices.Count)
                            continue;

                        // UVサブインデックスを取得（なければ0）
                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        var vertex = Vertices[vIdx];
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                        // マッピングからUnity頂点インデックスを取得
                        var key = (vIdx, uvSubIdx);
                        if (vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            subMeshTriangles[subMesh].Add(unityIdx);
                        }
                    }
                }
            }

            // Meshに設定
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            // BoneWeight設定（スキニングデータがある場合）
            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
            {
                mesh.boneWeights = unityBoneWeights.ToArray();
            }

            // サブメッシュ設定
            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(subMeshTriangles[i], i);
            }

            mesh.RecalculateBounds();

            // 法線がない場合は自動計算
            if (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up))
            {
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        /// <summary>
        /// Unity Meshに変換（座標変換付き、SkinnedMesh用）
        /// </summary>
        /// <param name="transform">頂点に適用する変換行列</param>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMesh(Matrix4x4 transform, int materialCount = -1)
        {
            var mesh = new Mesh();
            mesh.name = Name;

            if (Vertices.Count == 0)
                return mesh;

            // サブメッシュ数を計算
            int subMeshCount = materialCount > 0 ? materialCount : SubMeshCount;

            // === 頂点データ（共有版） ===
            // キー: (頂点インデックス, UVサブインデックス, 法線サブインデックス)
            // 値:  Unity頂点インデックス
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = IsSkinned;

            // 法線変換用（逆転置行列）
            Matrix4x4 normalMatrix = transform.inverse.transpose;

            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= Vertices.Count)
                            continue;

                        var vertex = Vertices[vIdx];

                        // UVサブインデックスを取得（なければ0）
                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                        // 法線サブインデックスを取得（なければ0）
                        int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                        if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                            normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                        // キーを作成
                        var key = (vIdx, uvSubIdx, normalSubIdx);

                        // 既存の頂点があるか確認
                        if (!vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            // 新しいUnity頂点を作成
                            unityIdx = unityVerts.Count;
                            vertexMapping[key] = unityIdx;

                            // 位置を変換
                            Vector3 transformedPos = transform.MultiplyPoint3x4(vertex.Position);
                            unityVerts.Add(transformedPos);

                            // UV
                            if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                                unityUVs.Add(vertex.UVs[uvSubIdx]);
                            else if (vertex.UVs.Count > 0)
                                unityUVs.Add(vertex.UVs[0]);
                            else
                                unityUVs.Add(Vector2.zero);

                            // 法線を変換
                            Vector3 normal;
                            if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                                normal = vertex.Normals[normalSubIdx];
                            else if (vertex.Normals.Count > 0)
                                normal = vertex.Normals[0];
                            else
                                normal = Vector3.up;
                            
                            Vector3 transformedNormal = normalMatrix.MultiplyVector(normal).normalized;
                            unityNormals.Add(transformedNormal);

                            // BoneWeight
                            if (hasBoneWeights)
                            {
                                unityBoneWeights.Add(vertex.BoneWeight ?? default);
                            }
                        }

                        // 三角形インデックスを追加
                        subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // Meshに設定
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
            {
                mesh.boneWeights = unityBoneWeights.ToArray();
            }

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(subMeshTriangles[i], i);
            }

            mesh.RecalculateBounds();

            if (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up))
            {
                mesh.RecalculateNormals();
            }

            return mesh;
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
            var mesh = new Mesh();
            mesh.name = Name;

            if (Vertices.Count == 0)
                return mesh;

            // サブメッシュ数を計算
            int subMeshCount = materialCount > 0 ? materialCount : SubMeshCount;

            // === FPX仕様: 頂点順 → UV順 で頂点を展開 ===
            // キー: (頂点インデックス, UVサブインデックス)
            // 値:  Unity頂点インデックス
            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = IsSkinned;

            // パス1: 頂点順 → UV順 で頂点データを作成
            for (int vIdx = 0; vIdx < Vertices.Count; vIdx++)
            {
                var vertex = Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    int unityIdx = unityVerts.Count;
                    vertexMapping[(vIdx, uvIdx)] = unityIdx;

                    // 位置
                    unityVerts.Add(vertex.Position);

                    // UV
                    if (uvIdx < vertex.UVs.Count)
                        unityUVs.Add(vertex.UVs[uvIdx]);
                    else
                        unityUVs.Add(Vector2.zero);

                    // 法線（最初の法線を使用、なければデフォルト）
                    if (vertex.Normals.Count > 0)
                        unityNormals.Add(vertex.Normals[0]);
                    else
                        unityNormals.Add(Vector3.up);

                    // BoneWeight
                    if (hasBoneWeights)
                    {
                        unityBoneWeights.Add(vertex.BoneWeight ?? default);
                    }
                }
            }

            // パス2: 面の三角形インデックスを構築
            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in Faces)
            {
                // 補助線（2頂点）や非表示面はスキップ
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                // 三角形に分解
                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= Vertices.Count)
                            continue;

                        // UVサブインデックスを取得（なければ0）
                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        var vertex = Vertices[vIdx];
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : 0;

                        // マッピングからUnity頂点インデックスを取得
                        var key = (vIdx, uvSubIdx);
                        if (vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            subMeshTriangles[subMesh].Add(unityIdx);
                        }
                    }
                }
            }

            // Meshに設定
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            // BoneWeight設定
            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
            {
                mesh.boneWeights = unityBoneWeights.ToArray();
            }

            // サブメッシュ設定
            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(subMeshTriangles[i], i);
            }

            mesh.RecalculateBounds();

            // 法線がない場合は自動計算
            if (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up))
            {
                mesh.RecalculateNormals();
            }

            return mesh;
        }

        /// <summary>
        /// Unity Meshに変換（頂点共有版・座標変換付き）
        /// </summary>
        /// <param name="transform">頂点に適用する変換行列</param>
        /// <param name="materialCount">マテリアル数（省略時は自動計算）</param>
        public Mesh ToUnityMeshShared(Matrix4x4 transform, int materialCount = -1)
        {
            var mesh = new Mesh();
            mesh.name = Name;

            if (Vertices.Count == 0)
                return mesh;

            // サブメッシュ数を計算
            int subMeshCount = materialCount > 0 ? materialCount : SubMeshCount;

            var vertexMapping = new Dictionary<(int vertexIdx, int uvIdx, int normalIdx), int>();

            var unityVerts = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityNormals = new List<Vector3>();
            var unityBoneWeights = new List<BoneWeight>();
            bool hasBoneWeights = IsSkinned;

            // 法線変換用（逆転置行列）
            Matrix4x4 normalMatrix = transform.inverse.transpose;

            var subMeshTriangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                subMeshTriangles[i] = new List<int>();

            foreach (var face in Faces)
            {
                if (face.VertexCount < 3 || face.IsHidden)
                    continue;

                var triangles = face.Triangulate();
                foreach (var tri in triangles)
                {
                    int subMesh = Mathf.Clamp(tri.MaterialIndex, 0, subMeshCount - 1);

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        if (vIdx < 0 || vIdx >= Vertices.Count)
                            continue;

                        var vertex = Vertices[vIdx];

                        int uvSubIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                        if (uvSubIdx < 0 || uvSubIdx >= vertex.UVs.Count)
                            uvSubIdx = vertex.UVs.Count > 0 ? 0 : -1;

                        int normalSubIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                        if (normalSubIdx < 0 || normalSubIdx >= vertex.Normals.Count)
                            normalSubIdx = vertex.Normals.Count > 0 ? 0 : -1;

                        var key = (vIdx, uvSubIdx, normalSubIdx);

                        if (!vertexMapping.TryGetValue(key, out int unityIdx))
                        {
                            unityIdx = unityVerts.Count;
                            vertexMapping[key] = unityIdx;

                            // 位置を変換
                            Vector3 transformedPos = transform.MultiplyPoint3x4(vertex.Position);
                            unityVerts.Add(transformedPos);

                            // UV
                            if (uvSubIdx >= 0 && uvSubIdx < vertex.UVs.Count)
                                unityUVs.Add(vertex.UVs[uvSubIdx]);
                            else if (vertex.UVs.Count > 0)
                                unityUVs.Add(vertex.UVs[0]);
                            else
                                unityUVs.Add(Vector2.zero);

                            // 法線を変換
                            Vector3 normal;
                            if (normalSubIdx >= 0 && normalSubIdx < vertex.Normals.Count)
                                normal = vertex.Normals[normalSubIdx];
                            else if (vertex.Normals.Count > 0)
                                normal = vertex.Normals[0];
                            else
                                normal = Vector3.up;
                            
                            Vector3 transformedNormal = normalMatrix.MultiplyVector(normal).normalized;
                            unityNormals.Add(transformedNormal);

                            // BoneWeight
                            if (hasBoneWeights)
                            {
                                unityBoneWeights.Add(vertex.BoneWeight ?? default);
                            }
                        }

                        subMeshTriangles[subMesh].Add(unityIdx);
                    }
                }
            }

            // Meshに設定
            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUVs);
            mesh.SetNormals(unityNormals);

            if (hasBoneWeights && unityBoneWeights.Count == unityVerts.Count)
            {
                mesh.boneWeights = unityBoneWeights.ToArray();
            }

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
            {
                mesh.SetTriangles(subMeshTriangles[i], i);
            }

            mesh.RecalculateBounds();

            if (unityNormals.Count == 0 || unityNormals.All(n => n == Vector3.up))
            {
                mesh.RecalculateNormals();
            }

            return mesh;
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
            Clear();
            if (mesh == null) return;

            Name = mesh.name;

            var srcVerts = mesh.vertices;
            var srcUVs = mesh.uv;
            var srcNormals = mesh.normals;
            
            // BoneWeight読み込み（スキンドメッシュの場合）
            BoneWeight[] srcBoneWeights = null;
            if (includeBoneWeights)
            {
                srcBoneWeights = mesh.boneWeights;
            }

            if (mergeVertices)
            {
                // 同一位置の頂点を統合
                // 注意: スキンドメッシュでは同一位置でもBoneWeightが異なる可能性があるため、
                //       includeBoneWeights=trueの場合は統合を無効にすることを推奨
                var positionToIndex = new Dictionary<Vector3, int>(new Vector3Comparer());
                var oldToNewIndex = new int[srcVerts.Length];

                for (int i = 0; i < srcVerts.Length; i++)
                {
                    Vector3 pos = srcVerts[i];

                    // スキンドメッシュの場合、BoneWeightも考慮して統合判定
                    bool shouldMerge = false;
                    int existingIdx = -1;
                    
                    if (positionToIndex.TryGetValue(pos, out int foundIdx))
                    {
                        existingIdx = foundIdx;
                        
                        if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                        {
                            // BoneWeightが異なる場合は統合しない
                            var existingBw = Vertices[existingIdx].BoneWeight;
                            var newBw = srcBoneWeights[i];
                            if (existingBw.HasValue &&
                                existingBw.Value.boneIndex0 == newBw.boneIndex0 &&
                                existingBw.Value.boneIndex1 == newBw.boneIndex1 &&
                                existingBw.Value.boneIndex2 == newBw.boneIndex2 &&
                                existingBw.Value.boneIndex3 == newBw.boneIndex3 &&
                                Mathf.Approximately(existingBw.Value.weight0, newBw.weight0) &&
                                Mathf.Approximately(existingBw.Value.weight1, newBw.weight1) &&
                                Mathf.Approximately(existingBw.Value.weight2, newBw.weight2) &&
                                Mathf.Approximately(existingBw.Value.weight3, newBw.weight3))
                            {
                                shouldMerge = true;
                            }
                        }
                        else
                        {
                            // 通常メッシュ: 位置が同じなら統合
                            shouldMerge = true;
                        }
                    }

                    if (shouldMerge && existingIdx >= 0)
                    {
                        // 既存の頂点を参照
                        oldToNewIndex[i] = existingIdx;

                        // UV/法線を追加（異なる場合のみ）
                        var vertex = Vertices[existingIdx];
                        if (srcUVs != null && i < srcUVs.Length)
                            vertex.GetOrAddUV(srcUVs[i]);
                        if (srcNormals != null && i < srcNormals.Length)
                            vertex.GetOrAddNormal(srcNormals[i]);
                    }
                    else
                    {
                        // 新規頂点
                        var vertex = new Vertex(pos);
                        if (srcUVs != null && i < srcUVs.Length)
                            vertex.UVs.Add(srcUVs[i]);
                        if (srcNormals != null && i < srcNormals.Length)
                            vertex.Normals.Add(srcNormals[i]);
                        
                        // BoneWeight設定
                        if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                        {
                            vertex.BoneWeight = srcBoneWeights[i];
                        }

                        int newIdx = Vertices.Count;
                        Vertices.Add(vertex);
                        positionToIndex[pos] = newIdx;
                        oldToNewIndex[i] = newIdx;
                    }
                }

                // サブメッシュごとに面を作成
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    var srcTriangles = mesh.GetTriangles(subMesh);

                    for (int i = 0; i < srcTriangles.Length; i += 3)
                    {
                        int oldV0 = srcTriangles[i];
                        int oldV1 = srcTriangles[i + 1];
                        int oldV2 = srcTriangles[i + 2];

                        int v0 = oldToNewIndex[oldV0];
                        int v1 = oldToNewIndex[oldV1];
                        int v2 = oldToNewIndex[oldV2];

                        // UV/法線のサブインデックスを検索
                        int uv0 = FindUVIndex(Vertices[v0], srcUVs != null && oldV0 < srcUVs.Length ? srcUVs[oldV0] : Vector2.zero);
                        int uv1 = FindUVIndex(Vertices[v1], srcUVs != null && oldV1 < srcUVs.Length ? srcUVs[oldV1] : Vector2.zero);
                        int uv2 = FindUVIndex(Vertices[v2], srcUVs != null && oldV2 < srcUVs.Length ? srcUVs[oldV2] : Vector2.zero);

                        int n0 = FindNormalIndex(Vertices[v0], srcNormals != null && oldV0 < srcNormals.Length ? srcNormals[oldV0] : Vector3.up);
                        int n1 = FindNormalIndex(Vertices[v1], srcNormals != null && oldV1 < srcNormals.Length ? srcNormals[oldV1] : Vector3.up);
                        int n2 = FindNormalIndex(Vertices[v2], srcNormals != null && oldV2 < srcNormals.Length ? srcNormals[oldV2] : Vector3.up);

                        Faces.Add(Face.CreateTriangle(v0, v1, v2, uv0, uv1, uv2, n0, n1, n2, subMesh));
                    }
                }
            }
            else
            {
                // 頂点を統合しない（Unity Meshをそのまま再現）
                for (int i = 0; i < srcVerts.Length; i++)
                {
                    var vertex = new Vertex(srcVerts[i]);

                    if (srcUVs != null && i < srcUVs.Length)
                        vertex.UVs.Add(srcUVs[i]);
                    else
                        vertex.UVs.Add(Vector2.zero);

                    if (srcNormals != null && i < srcNormals.Length)
                        vertex.Normals.Add(srcNormals[i]);
                    
                    // BoneWeight設定
                    if (includeBoneWeights && srcBoneWeights != null && i < srcBoneWeights.Length)
                    {
                        vertex.BoneWeight = srcBoneWeights[i];
                    }

                    Vertices.Add(vertex);
                }

                // サブメッシュごとに処理
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    var srcTriangles = mesh.GetTriangles(subMesh);

                    for (int i = 0; i < srcTriangles.Length; i += 3)
                    {
                        int v0 = srcTriangles[i];
                        int v1 = srcTriangles[i + 1];
                        int v2 = srcTriangles[i + 2];

                        Faces.Add(Face.CreateTriangle(v0, v1, v2, 0, 0, 0, 0, 0, 0, subMesh));
                    }
                }
            }
        }

        private int FindUVIndex(Vertex vertex, Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                //if (Vector2.Distance(vertex.UVs[i], uv) < tolerance)
                //    return i;
                if (vertex.UVs[i] == uv)  // 完全一致
                    return i;
            }
            return 0;
        }

        private int FindNormalIndex(Vertex vertex, Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.Normals.Count; i++)
            {
                if (Vector3.Distance(vertex.Normals[i], normal) < tolerance)
                    return i;
            }
            return 0;
        }

        // === ユーティリティ ===

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Faces.Clear();
        }

        /// <summary>
        /// 全ての面の法線を自動計算
        /// </summary>
        public void RecalculateNormals()
        {
            // 各頂点の法線をクリア
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            // 各面ごとに法線を計算
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                // 面法線を計算
                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // 各頂点に法線を追加
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    var vertex = Vertices[face.VertexIndices[i]];
                    int nIdx = vertex.GetOrAddNormal(faceNormal);
                    face.NormalIndices.Add(nIdx);
                }
            }
        }

        /// <summary>
        /// スムーズ法線を計算（同一頂点の法線を平均化）
        /// </summary>
        public void RecalculateSmoothNormals()
        {
            // まず面法線を計算
            var faceNormals = new Dictionary<int, List<Vector3>>();

            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                foreach (int vIdx in face.VertexIndices)
                {
                    if (!faceNormals.ContainsKey(vIdx))
                        faceNormals[vIdx] = new List<Vector3>();
                    faceNormals[vIdx].Add(faceNormal);
                }
            }

            // 各頂点に平均法線を設定
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            foreach (var kvp in faceNormals)
            {
                var vertex = Vertices[kvp.Key];
                Vector3 avgNormal = Vector3.zero;
                foreach (var n in kvp.Value)
                    avgNormal += n;
                avgNormal = avgNormal.normalized;
                vertex.AddNormal(avgNormal);
            }

            // 面の法線インデックスを更新
            foreach (var face in Faces)
            {
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    face.NormalIndices.Add(0);
                }
            }
        }

        /// <summary>
        /// ディープコピー（IDも保持）
        /// </summary>
        public MeshObject Clone()
        {
            var copy = new MeshObject(Name);
            copy.Type = this.Type;
            copy.IsExpanded = this.IsExpanded;
            copy.Vertices = Vertices.Select(v => v.Clone()).ToList();
            copy.Faces = Faces.Select(f => f.Clone()).ToList();
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;

            if(this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

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
            copy.IsExpanded = this.IsExpanded;
            copy.ParentIndex = this.ParentIndex;
            copy.Depth = this.Depth;
            copy.HierarchyParentIndex = this.HierarchyParentIndex;

            if (this.BoneTransform != null)
            {
                copy.BoneTransform = new BoneTransform();
                copy.BoneTransform.CopyFrom(this.BoneTransform);
            }

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
