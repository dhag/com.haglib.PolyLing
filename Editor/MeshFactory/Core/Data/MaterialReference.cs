// Assets/Editor/MeshFactory/Materials/MaterialReference.cs
// マテリアル参照とパラメータデータを統合管理
// アセットパス + パラメータデータ + ランタイムキャッシュ

using System;
using UnityEngine;
using UnityEditor;

namespace MeshFactory.Materials
{
    /// <summary>
    /// マテリアル参照
    /// - アセットがあればパスで参照
    /// - なければパラメータデータから生成
    /// </summary>
    [Serializable]
    public class MaterialReference
    {
        // ================================================================
        // 永続データ（シリアライズ対象）
        // ================================================================
        
        /// <summary>マテリアルアセットのパス（あれば）</summary>
        public string AssetPath;
        
        /// <summary>パラメータデータ（常に保持）</summary>
        public MaterialData Data;

        // ================================================================
        // ランタイムキャッシュ（シリアライズ対象外）
        // ================================================================
        
        [NonSerialized]
        private Material _cachedMaterial;
        
        [NonSerialized]
        private bool _cacheValid;

        // ================================================================
        // プロパティ
        // ================================================================
        
        /// <summary>
        /// マテリアルインスタンスを取得
        /// アセットがあれば読み込み、なければDataから生成
        /// </summary>
        public Material Material
        {
            get => GetOrCreateMaterial();
            set => SetMaterial(value);
        }
        
        /// <summary>アセットパスが設定されているか</summary>
        public bool HasAssetPath => !string.IsNullOrEmpty(AssetPath);
        
        /// <summary>有効なマテリアルを持っているか</summary>
        public bool IsValid => Data != null || HasAssetPath;
        
        /// <summary>マテリアル名</summary>
        public string Name
        {
            get => Data?.Name ?? "Unknown";
            set { if (Data != null) Data.Name = value; }
        }

        // ================================================================
        // コンストラクタ
        // ================================================================
        
        public MaterialReference()
        {
            Data = new MaterialData();
        }
        
        public MaterialReference(MaterialData data)
        {
            Data = data ?? new MaterialData();
        }
        
        public MaterialReference(Material material)
        {
            SetMaterial(material);
        }
        
        public MaterialReference(string assetPath)
        {
            AssetPath = assetPath;
            Data = new MaterialData();
            
            // アセットからデータを抽出
            if (HasAssetPath)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
                if (mat != null)
                {
                    Data = MaterialDataConverter.FromMaterial(mat);
                    _cachedMaterial = mat;
                    _cacheValid = true;
                }
            }
        }

        // ================================================================
        // マテリアル取得/設定
        // ================================================================
        
        /// <summary>
        /// マテリアルを取得または生成
        /// </summary>
        private Material GetOrCreateMaterial()
        {
            // キャッシュが有効ならそれを返す
            if (_cacheValid && _cachedMaterial != null)
                return _cachedMaterial;
            
            // アセットパスがあればロード
            if (HasAssetPath)
            {
                _cachedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetPath);
                if (_cachedMaterial != null)
                {
                    _cacheValid = true;
                    return _cachedMaterial;
                }
                // ロード失敗 → パスをクリア
                Debug.LogWarning($"[MaterialReference] Failed to load material: {AssetPath}");
                AssetPath = null;
            }
            
            // Dataから生成
            if (Data != null)
            {
                _cachedMaterial = MaterialDataConverter.ToMaterial(Data);
                _cacheValid = true;
                return _cachedMaterial;
            }
            
            return null;
        }
        
        /// <summary>
        /// マテリアルを設定
        /// </summary>
        private void SetMaterial(Material material)
        {
            if (material == null)
            {
                AssetPath = null;
                Data = new MaterialData();
                _cachedMaterial = null;
                _cacheValid = false;
                return;
            }
            
            // アセットパスを取得
            string path = AssetDatabase.GetAssetPath(material);
            if (!string.IsNullOrEmpty(path))
            {
                AssetPath = path;
            }
            else
            {
                AssetPath = null;
            }
            
            // パラメータを抽出
            Data = MaterialDataConverter.FromMaterial(material);
            
            // キャッシュを更新
            _cachedMaterial = material;
            _cacheValid = true;
        }

        // ================================================================
        // キャッシュ管理
        // ================================================================
        
        /// <summary>キャッシュを無効化（再読み込み/再生成させる）</summary>
        public void InvalidateCache()
        {
            _cachedMaterial = null;
            _cacheValid = false;
        }
        
        /// <summary>Dataからキャッシュを再生成</summary>
        public void RefreshFromData()
        {
            if (Data != null)
            {
                _cachedMaterial = MaterialDataConverter.ToMaterial(Data);
                _cacheValid = true;
            }
        }
        
        /// <summary>現在のマテリアルからDataを更新</summary>
        public void RefreshData()
        {
            if (_cachedMaterial != null)
            {
                Data = MaterialDataConverter.FromMaterial(_cachedMaterial);
            }
        }

        // ================================================================
        // アセット保存
        // ================================================================
        
        /// <summary>
        /// マテリアルをアセットとして保存
        /// </summary>
        /// <param name="savePath">保存先パス（Assets/...）</param>
        /// <returns>成功したらtrue</returns>
        public bool SaveAsAsset(string savePath)
        {
            if (string.IsNullOrEmpty(savePath))
                return false;
            
            try
            {
                // マテリアルを取得または生成
                var mat = GetOrCreateMaterial();
                if (mat == null)
                    return false;
                
                // 既存アセットの場合はコピーを作成
                if (HasAssetPath && AssetPath != savePath)
                {
                    mat = new Material(mat);
                }
                
                // 新規オンメモリの場合はそのまま保存
                string existingPath = AssetDatabase.GetAssetPath(mat);
                if (string.IsNullOrEmpty(existingPath))
                {
                    AssetDatabase.CreateAsset(mat, savePath);
                }
                else if (existingPath != savePath)
                {
                    // 別パスにコピー
                    var newMat = new Material(mat);
                    AssetDatabase.CreateAsset(newMat, savePath);
                    mat = newMat;
                }
                
                AssetDatabase.SaveAssets();
                
                // パスを更新
                AssetPath = savePath;
                _cachedMaterial = mat;
                _cacheValid = true;
                
                Debug.Log($"[MaterialReference] Saved: {savePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MaterialReference] Save failed: {e.Message}");
                return false;
            }
        }

        // ================================================================
        // ユーティリティ
        // ================================================================
        
        /// <summary>ディープコピーを作成</summary>
        public MaterialReference Clone()
        {
            return new MaterialReference
            {
                AssetPath = this.AssetPath,
                Data = this.Data?.Clone()
            };
        }
        
        public override string ToString()
        {
            if (HasAssetPath)
                return $"[Asset] {Name} ({AssetPath})";
            return $"[OnMemory] {Name}";
        }
    }
}
