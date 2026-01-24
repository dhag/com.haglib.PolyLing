// Assets/Editor/Poly_Ling_/Core/Data/TypedMeshIndices.cs
// タイプ別メッシュインデックスキャッシュ
// MeshContextListをタイプ別にフィルタリングしてキャッシュ

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Model;

namespace Poly_Ling.Data
{
    /// <summary>
    /// タイプ別メッシュインデックスのキャッシュを管理するクラス
    /// ModelContext.MeshContextList をタイプ別にフィルタリングしてキャッシュ
    /// </summary>
    public class TypedMeshIndices
    {
        // ================================================================
        // 内部状態
        // ================================================================

        private readonly ModelContext _owner;
        private bool _isDirty = true;

        // キャッシュ: カテゴリ → (マスターインデックス, MeshContext) のリスト
        private readonly Dictionary<MeshCategory, List<TypedMeshEntry>> _cache
            = new Dictionary<MeshCategory, List<TypedMeshEntry>>();

        // ボーン用のマッピング: マスターインデックス → ボーンリスト内インデックス
        private Dictionary<int, int> _masterToBoneIndex;

        // ボーン用の逆マッピング: ボーンリスト内インデックス → マスターインデックス
        private Dictionary<int, int> _boneToMasterIndex;

        // ================================================================
        // コンストラクタ
        // ================================================================

        /// <summary>
        /// TypedMeshIndices を作成
        /// </summary>
        /// <param name="owner">親の ModelContext</param>
        public TypedMeshIndices(ModelContext owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        // ================================================================
        // 公開メソッド
        // ================================================================

        /// <summary>
        /// キャッシュを無効化（追加/削除/タイプ変更時に呼ぶ）
        /// </summary>
        public void Invalidate()
        {
            _isDirty = true;
        }

        /// <summary>
        /// カテゴリ別にフィルタリングされたエントリを取得
        /// </summary>
        /// <param name="category">取得するカテゴリ</param>
        /// <returns>（マスターインデックス, MeshContext）のリスト</returns>
        public IReadOnlyList<TypedMeshEntry> GetEntries(MeshCategory category)
        {
            EnsureCache();

            if (_cache.TryGetValue(category, out var entries))
                return entries;

            return Array.Empty<TypedMeshEntry>();
        }

        /// <summary>
        /// カテゴリ別に MeshContext のみを取得
        /// </summary>
        public IReadOnlyList<MeshContext> GetContexts(MeshCategory category)
        {
            return GetEntries(category).Select(e => e.Context).ToList();
        }

        /// <summary>
        /// カテゴリ別にマスターインデックスのみを取得
        /// </summary>
        public IReadOnlyList<int> GetMasterIndices(MeshCategory category)
        {
            return GetEntries(category).Select(e => e.MasterIndex).ToList();
        }

        /// <summary>
        /// カテゴリの要素数を取得
        /// </summary>
        public int GetCount(MeshCategory category)
        {
            EnsureCache();

            if (_cache.TryGetValue(category, out var entries))
                return entries.Count;

            return 0;
        }

        // ================================================================
        // ボーンインデックス変換
        // ================================================================

        /// <summary>
        /// マスターインデックスからボーンリスト内インデックスに変換
        /// ボーンでない場合は -1 を返す
        /// </summary>
        /// <param name="masterIndex">MeshContextList 内のインデックス</param>
        /// <returns>ボーンリスト内のインデックス、またはボーンでない場合は -1</returns>
        public int MasterToBoneIndex(int masterIndex)
        {
            EnsureCache();

            if (_masterToBoneIndex != null && _masterToBoneIndex.TryGetValue(masterIndex, out int boneIndex))
                return boneIndex;

            return -1;
        }

        /// <summary>
        /// ボーンリスト内インデックスからマスターインデックスに変換
        /// </summary>
        /// <param name="boneIndex">ボーンリスト内のインデックス</param>
        /// <returns>MeshContextList 内のインデックス、または無効な場合は -1</returns>
        public int BoneToMasterIndex(int boneIndex)
        {
            EnsureCache();

            if (_boneToMasterIndex != null && _boneToMasterIndex.TryGetValue(boneIndex, out int masterIndex))
                return masterIndex;

            return -1;
        }

        /// <summary>
        /// BoneWeight のボーンインデックスをマスターインデックスからボーンリストインデックスに変換
        /// </summary>
        /// <param name="weight">変換する BoneWeight</param>
        /// <returns>変換後の BoneWeight</returns>
        public UnityEngine.BoneWeight ConvertBoneWeightToLocal(UnityEngine.BoneWeight weight)
        {
            EnsureCache();

            if (_masterToBoneIndex == null || _masterToBoneIndex.Count == 0)
                return weight;

            return new UnityEngine.BoneWeight
            {
                boneIndex0 = ConvertSingleBoneIndex(weight.boneIndex0),
                boneIndex1 = ConvertSingleBoneIndex(weight.boneIndex1),
                boneIndex2 = ConvertSingleBoneIndex(weight.boneIndex2),
                boneIndex3 = ConvertSingleBoneIndex(weight.boneIndex3),
                weight0 = weight.weight0,
                weight1 = weight.weight1,
                weight2 = weight.weight2,
                weight3 = weight.weight3
            };
        }

        /// <summary>
        /// BoneWeight のボーンインデックスをボーンリストインデックスからマスターインデックスに変換
        /// </summary>
        /// <param name="weight">変換する BoneWeight</param>
        /// <returns>変換後の BoneWeight</returns>
        public UnityEngine.BoneWeight ConvertBoneWeightToMaster(UnityEngine.BoneWeight weight)
        {
            EnsureCache();

            if (_boneToMasterIndex == null || _boneToMasterIndex.Count == 0)
                return weight;

            return new UnityEngine.BoneWeight
            {
                boneIndex0 = ConvertSingleBoneIndexToMaster(weight.boneIndex0),
                boneIndex1 = ConvertSingleBoneIndexToMaster(weight.boneIndex1),
                boneIndex2 = ConvertSingleBoneIndexToMaster(weight.boneIndex2),
                boneIndex3 = ConvertSingleBoneIndexToMaster(weight.boneIndex3),
                weight0 = weight.weight0,
                weight1 = weight.weight1,
                weight2 = weight.weight2,
                weight3 = weight.weight3
            };
        }

        private int ConvertSingleBoneIndex(int masterIndex)
        {
            if (_masterToBoneIndex.TryGetValue(masterIndex, out int boneIndex))
                return boneIndex;
            return 0; // フォールバック: 最初のボーン
        }

        private int ConvertSingleBoneIndexToMaster(int boneIndex)
        {
            if (_boneToMasterIndex.TryGetValue(boneIndex, out int masterIndex))
                return masterIndex;
            return 0; // フォールバック
        }

        // ================================================================
        // ヘルパープロパティ（よく使うカテゴリへのショートカット）
        // ================================================================

        /// <summary>ボーン数</summary>
        public int BoneCount => GetCount(MeshCategory.Bone);

        /// <summary>描画可能メッシュ数</summary>
        public int DrawableCount => GetCount(MeshCategory.Drawable);

        /// <summary>通常メッシュ数</summary>
        public int MeshCount => GetCount(MeshCategory.Mesh);

        /// <summary>ボーンがあるか</summary>
        public bool HasBones => BoneCount > 0;

        /// <summary>描画可能メッシュがあるか</summary>
        public bool HasDrawables => DrawableCount > 0;

        // ================================================================
        // キャッシュ構築
        // ================================================================

        private void EnsureCache()
        {
            if (!_isDirty)
                return;

            RebuildCache();
            _isDirty = false;
        }

        private void RebuildCache()
        {
            _cache.Clear();
            _masterToBoneIndex = new Dictionary<int, int>();
            _boneToMasterIndex = new Dictionary<int, int>();

            // 各カテゴリ用のリストを初期化
            foreach (MeshCategory category in Enum.GetValues(typeof(MeshCategory)))
            {
                _cache[category] = new List<TypedMeshEntry>();
            }

            var list = _owner?.MeshContextList;
            if (list == null)
                return;

            int boneIndex = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                if (ctx == null)
                    continue;

                var entry = new TypedMeshEntry(i, ctx);
                var meshType = ctx.MeshObject?.Type ?? MeshType.Mesh;

                // All カテゴリには全て追加
                _cache[MeshCategory.All].Add(entry);

                // 単一タイプカテゴリに追加
                switch (meshType)
                {
                    case MeshType.Mesh:
                        _cache[MeshCategory.Mesh].Add(entry);
                        _cache[MeshCategory.Drawable].Add(entry);
                        break;

                    case MeshType.BakedMirror:
                        _cache[MeshCategory.BakedMirror].Add(entry);
                        _cache[MeshCategory.Drawable].Add(entry);
                        break;

                    case MeshType.Bone:
                        _cache[MeshCategory.Bone].Add(entry);
                        // ボーンインデックスマッピングを構築
                        _masterToBoneIndex[i] = boneIndex;
                        _boneToMasterIndex[boneIndex] = i;
                        boneIndex++;
                        break;

                    case MeshType.Morph:
                        _cache[MeshCategory.Morph].Add(entry);
                        break;

                    case MeshType.RigidBody:
                        _cache[MeshCategory.RigidBody].Add(entry);
                        break;

                    case MeshType.RigidBodyJoint:
                        _cache[MeshCategory.RigidBodyJoint].Add(entry);
                        break;

                    case MeshType.Helper:
                        _cache[MeshCategory.Helper].Add(entry);
                        break;

                    case MeshType.Group:
                        _cache[MeshCategory.Group].Add(entry);
                        break;
                }
            }
        }
    }

    // ================================================================
    // エントリ構造体
    // ================================================================

    /// <summary>
    /// タイプ別リストのエントリ
    /// マスターリストのインデックスと MeshContext のペア
    /// </summary>
    public readonly struct TypedMeshEntry
    {
        /// <summary>MeshContextList 内のインデックス</summary>
        public readonly int MasterIndex;

        /// <summary>MeshContext への参照</summary>
        public readonly MeshContext Context;

        public TypedMeshEntry(int masterIndex, MeshContext context)
        {
            MasterIndex = masterIndex;
            Context = context;
        }

        /// <summary>MeshObject への便利アクセス</summary>
        public MeshObject MeshObject => Context?.MeshObject;

        /// <summary>名前への便利アクセス</summary>
        public string Name => Context?.Name ?? string.Empty;

        /// <summary>タイプへの便利アクセス</summary>
        public MeshType Type => Context?.MeshObject?.Type ?? MeshType.Mesh;

        public override string ToString()
        {
            return $"[{MasterIndex}] {Name} ({Type})";
        }
    }
}
