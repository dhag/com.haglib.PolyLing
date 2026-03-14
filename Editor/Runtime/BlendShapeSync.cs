// BlendShapeSync.cs
// 複数SkinnedMeshRendererのBlendShapeを統合制御
// CSV辞書で定義されたクリップを一括操作

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Runtime
{
    /// <summary>
    /// BlendShape同期コントローラー
    /// CSV形式: ClipName,MeshName,BlendShapeName,Weight,MeshName,BlendShapeName,Weight,...
    /// </summary>
    [ExecuteAlways]
    public class BlendShapeSync : MonoBehaviour
    {
        // ================================================================
        // 設定
        // ================================================================

        [Tooltip("CSV形式のマッピングデータ")]
        [TextArea(10, 30)]
        public string MappingCSV = "";

        [Tooltip("子オブジェクトから自動検索")]
        public bool AutoFindRenderers = true;

        // ================================================================
        // クリップウェイト
        // ================================================================

        [Serializable]
        public class ClipWeight
        {
            public string ClipName;
            [Range(0f, 100f)]
            public float Weight;
        }

        [SerializeField]
        public List<ClipWeight> Clips = new List<ClipWeight>();

        // ================================================================
        // 内部データ
        // ================================================================

        // ClipName -> List<(MeshName, BlendShapeName, Weight)>
        private Dictionary<string, List<(string meshName, string shapeName, float weight)>> _clipDefinitions;

        // MeshName -> SkinnedMeshRenderer
        private Dictionary<string, SkinnedMeshRenderer> _rendererMap;

        // BlendShapeインデックスキャッシュ: (renderer, shapeName) -> index
        private Dictionary<(SkinnedMeshRenderer, string), int> _indexCache;

        // 前回の値（変更検出用）
        private Dictionary<string, float> _lastValues = new Dictionary<string, float>();

        private bool _initialized = false;

        // ================================================================
        // 初期化
        // ================================================================

        void OnEnable()
        {
            Initialize();
        }

        void Start()
        {
            if (!_initialized) Initialize();
        }

        void OnValidate()
        {
            _initialized = false;
        }

        public void Initialize()
        {
            _clipDefinitions = new Dictionary<string, List<(string, string, float)>>();
            _rendererMap = new Dictionary<string, SkinnedMeshRenderer>();
            _indexCache = new Dictionary<(SkinnedMeshRenderer, string), int>();

            // レンダラーマップ構築
            if (AutoFindRenderers)
            {
                var renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var r in renderers)
                {
                    if (!_rendererMap.ContainsKey(r.name))
                    {
                        _rendererMap[r.name] = r;
                    }
                }
            }

            // CSV解析
            ParseCSV();

            // クリップリスト初期化
            InitializeClips();

            _initialized = true;
        }

        // ================================================================
        // CSV解析
        // ================================================================

        private void ParseCSV()
        {
            if (string.IsNullOrEmpty(MappingCSV)) return;

            var lines = MappingCSV.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 4) continue; // 最低: ClipName,MeshName,ShapeName,Weight

                string clipName = parts[0].Trim();
                var targets = new List<(string meshName, string shapeName, float weight)>();

                // 残りを3つずつ解析
                for (int i = 1; i + 2 < parts.Length; i += 3)
                {
                    string meshName = parts[i].Trim();
                    string shapeName = parts[i + 1].Trim();
                    if (float.TryParse(parts[i + 2].Trim(), out float weight))
                    {
                        targets.Add((meshName, shapeName, weight));
                    }
                }

                if (targets.Count > 0)
                {
                    _clipDefinitions[clipName] = targets;
                }
            }
        }

        // ================================================================
        // クリップリスト初期化
        // ================================================================

        private void InitializeClips()
        {
            // 既存クリップを保持しつつ、足りないものを追加
            var existingClips = new Dictionary<string, ClipWeight>();
            foreach (var clip in Clips)
            {
                if (!string.IsNullOrEmpty(clip.ClipName))
                {
                    existingClips[clip.ClipName] = clip;
                }
            }

            Clips.Clear();

            foreach (var clipName in _clipDefinitions.Keys)
            {
                if (existingClips.TryGetValue(clipName, out var existing))
                {
                    Clips.Add(existing);
                }
                else
                {
                    Clips.Add(new ClipWeight { ClipName = clipName, Weight = 0f });
                }
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        void LateUpdate()
        {
            if (!_initialized) Initialize();

            ApplyAllClips();
        }

        private void ApplyAllClips()
        {
            // まず全BlendShapeを0にリセット
            ResetAllBlendShapes();

            // 各クリップを適用（加算）
            foreach (var clip in Clips)
            {
                if (clip.Weight <= 0f) continue;
                if (!_clipDefinitions.TryGetValue(clip.ClipName, out var targets)) continue;

                foreach (var (meshName, shapeName, weight) in targets)
                {
                    ApplyBlendShape(meshName, shapeName, clip.Weight * weight);
                }
            }
        }

        private void ResetAllBlendShapes()
        {
            foreach (var renderer in _rendererMap.Values)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;

                int count = renderer.sharedMesh.blendShapeCount;
                for (int i = 0; i < count; i++)
                {
                    renderer.SetBlendShapeWeight(i, 0f);
                }
            }
        }

        private void ApplyBlendShape(string meshName, string shapeName, float value)
        {
            if (!_rendererMap.TryGetValue(meshName, out var renderer)) return;
            if (renderer == null || renderer.sharedMesh == null) return;

            // インデックスキャッシュ
            var key = (renderer, shapeName);
            if (!_indexCache.TryGetValue(key, out int index))
            {
                index = renderer.sharedMesh.GetBlendShapeIndex(shapeName);
                _indexCache[key] = index;
            }

            if (index >= 0)
            {
                // 加算（複数クリップが同じBlendShapeを操作する場合）
                float current = renderer.GetBlendShapeWeight(index);
                renderer.SetBlendShapeWeight(index, Mathf.Clamp(current + value, 0f, 100f));
            }
        }

        // ================================================================
        // API
        // ================================================================

        /// <summary>
        /// クリップのウェイトを設定
        /// </summary>
        public void SetClipWeight(string clipName, float weight)
        {
            var clip = Clips.Find(c => c.ClipName == clipName);
            if (clip != null)
            {
                clip.Weight = Mathf.Clamp(weight, 0f, 100f);
            }
        }

        /// <summary>
        /// クリップのウェイトを取得
        /// </summary>
        public float GetClipWeight(string clipName)
        {
            var clip = Clips.Find(c => c.ClipName == clipName);
            return clip?.Weight ?? 0f;
        }

        /// <summary>
        /// 全クリップをリセット
        /// </summary>
        public void ResetAllClips()
        {
            foreach (var clip in Clips)
            {
                clip.Weight = 0f;
            }
        }

        /// <summary>
        /// クリップ名一覧を取得
        /// </summary>
        public string[] GetClipNames()
        {
            var names = new string[Clips.Count];
            for (int i = 0; i < Clips.Count; i++)
            {
                names[i] = Clips[i].ClipName;
            }
            return names;
        }
    }
}
