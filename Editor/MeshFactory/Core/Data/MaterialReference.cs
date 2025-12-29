// Assets/Editor/MeshFactory/Data/MaterialReference.cs
// マテリアル参照クラス（アセット参照またはオンメモリ）

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MeshFactory.Data
{
    /// <summary>
    /// マテリアル参照
    /// アセットパス参照またはオンメモリのマテリアルを管理
    /// </summary>
    [Serializable]
    public class MaterialReference
    {
        // ================================================================
        // シリアライズフィールド
        // ================================================================

        /// <summary>アセットパス（nullまたは空ならオンメモリ）</summary>
        [SerializeField]
        private string _assetPath;

        /// <summary>マテリアル名（オンメモリ時の識別用）</summary>
        [SerializeField]
        private string _name;

        // ================================================================
        // 非シリアライズフィールド
        // ================================================================

        /// <summary>キャッシュされたマテリアル</summary>
        [NonSerialized]
        private Material _cachedMaterial;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>アセットパス</summary>
        public string AssetPath
        {
            get => _assetPath;
            set => _assetPath = value;
        }

        /// <summary>マテリアル名</summary>
        public string Name
        {
            get => _name;
            set => _name = value;
        }

        /// <summary>アセット参照か</summary>
        public bool IsAssetReference => !string.IsNullOrEmpty(_assetPath);

        /// <summary>オンメモリか</summary>
        public bool IsOnMemory => !IsAssetReference && _cachedMaterial != null;

        /// <summary>有効なマテリアルを持っているか</summary>
        public bool HasMaterial => Material != null;

        /// <summary>
        /// マテリアルを取得/設定
        /// </summary>
        public Material Material
        {
            get
            {
                // キャッシュがあればそれを返す
                if (_cachedMaterial != null)
                    return _cachedMaterial;

                // アセット参照なら読み込み
                if (IsAssetReference)
                {
#if UNITY_EDITOR
                    _cachedMaterial = AssetDatabase.LoadAssetAtPath<Material>(_assetPath);
#else
                    // ランタイム時はResources.Loadなど別の方法が必要
                    Debug.LogWarning($"[MaterialReference] Cannot load asset at runtime: {_assetPath}");
#endif
                }

                return _cachedMaterial;
            }
            set
            {
                _cachedMaterial = value;

                if (value == null)
                {
                    _assetPath = null;
                    _name = null;
                    return;
                }

                _name = value.name;

#if UNITY_EDITOR
                // アセットパスを取得
                string path = AssetDatabase.GetAssetPath(value);
                _assetPath = !string.IsNullOrEmpty(path) ? path : null;
#else
                _assetPath = null;
#endif
            }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================

        public MaterialReference()
        {
        }

        public MaterialReference(Material material)
        {
            Material = material;
        }

        public MaterialReference(string assetPath)
        {
            _assetPath = assetPath;
            _name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        }

        // ================================================================
        // メソッド
        // ================================================================

        /// <summary>
        /// キャッシュをクリア（アセット再読み込み用）
        /// </summary>
        public void ClearCache()
        {
            if (IsAssetReference)
            {
                _cachedMaterial = null;
            }
        }

        /// <summary>
        /// オンメモリマテリアルをアセットとして保存
        /// </summary>
        /// <param name="directory">保存先ディレクトリ（Assets/...）</param>
        /// <param name="fileName">ファイル名（拡張子なし）</param>
        /// <returns>保存成功したか</returns>
        public bool SaveAsAsset(string directory, string fileName)
        {
#if UNITY_EDITOR
            if (!IsOnMemory)
            {
                Debug.LogWarning("[MaterialReference] Not an on-memory material, cannot save.");
                return false;
            }

            if (_cachedMaterial == null)
            {
                Debug.LogWarning("[MaterialReference] No material to save.");
                return false;
            }

            // ファイル名をサニタイズ
            string safeName = SanitizeFileName(fileName);
            if (string.IsNullOrEmpty(safeName))
            {
                safeName = "Material";
            }

            string path = System.IO.Path.Combine(directory, $"{safeName}.mat");

            // 同名ファイルが存在する場合は番号を付ける
            int suffix = 1;
            while (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            {
                path = System.IO.Path.Combine(directory, $"{safeName}_{suffix}.mat");
                suffix++;
            }

            // マテリアルをコピーして保存（元のオンメモリマテリアルは変更しない）
            Material matCopy = new Material(_cachedMaterial);
            matCopy.name = System.IO.Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(matCopy, path);
            AssetDatabase.SaveAssets();

            // 参照を保存したアセットに切り替え
            _assetPath = path;
            _cachedMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
            _name = _cachedMaterial.name;

            Debug.Log($"[MaterialReference] Saved material as asset: {path}");
            return true;
#else
            Debug.LogWarning("[MaterialReference] Cannot save assets at runtime.");
            return false;
#endif
        }

        /// <summary>
        /// ファイル名をサニタイズ
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            // 不正な文字を除去
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name.Trim();
        }

        // ================================================================
        // 変換
        // ================================================================

        /// <summary>
        /// Material から MaterialReference への暗黙変換
        /// </summary>
        public static implicit operator MaterialReference(Material material)
        {
            return new MaterialReference(material);
        }

        /// <summary>
        /// MaterialReference から Material への暗黙変換
        /// </summary>
        public static implicit operator Material(MaterialReference reference)
        {
            return reference?.Material;
        }

        // ================================================================
        // デバッグ
        // ================================================================

        public override string ToString()
        {
            if (IsAssetReference)
                return $"[Asset] {_assetPath}";
            if (IsOnMemory)
                return $"[Memory] {_name ?? "(unnamed)"}";
            return "[Empty]";
        }
    }
}
