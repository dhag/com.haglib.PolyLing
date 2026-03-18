// TextureAtlasFrameController.cs
// テクスチャアトラスのフレーム切り替えコントローラー
// UV Offset/Scaleでパラパラ漫画的なアニメーションを実現

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Runtime
{
    /// <summary>
    ///パラパラ漫画テクスチャ
    /// テクスチャアトラスフレームコントローラー
    /// 異なるサイズの区画を持つアトラステクスチャのフレーム切り替え
    /// </summary>
    [ExecuteAlways]
    public class TextureAtlasFrameController : MonoBehaviour
    {
        // ================================================================
        // フレーム定義
        // ================================================================

        [Serializable]
        public class FrameData
        {
            public string Name;
            public Vector2 Offset;
            public Vector2 Scale = Vector2.one;

            public FrameData() { }

            public FrameData(string name, float offsetX, float offsetY, float scaleX, float scaleY)
            {
                Name = name;
                Offset = new Vector2(offsetX, offsetY);
                Scale = new Vector2(scaleX, scaleY);
            }
        }

        [SerializeField]
        public List<FrameData> Frames = new List<FrameData>();

        // ================================================================
        // 設定
        // ================================================================

        [Tooltip("CSV形式のフレーム定義（Name,OffsetX,OffsetY,ScaleX,ScaleY）")]
        [TextArea(5, 15)]
        public string FrameCSV = "";

        [Tooltip("現在のフレームインデックス")]
        [Range(0, 100)]
        public int CurrentFrame = 0;

        [Tooltip("フレーム間を補間する（0-1の小数値で中間表示）")]
        public bool Interpolate = false;

        [Tooltip("補間用のフレーム位置（0.0 = frame0, 1.0 = frame1, ...）")]
        [Range(0f, 100f)]
        public float FramePosition = 0f;

        [Tooltip("対象のテクスチャプロパティ名")]
        public string TexturePropertyName = "_MainTex";

        [Tooltip("子オブジェクトのRendererも含める")]
        public bool IncludeChildren = true;

        [Tooltip("対象マテリアルのインデックス（-1で全て）")]
        public int MaterialIndex = -1;

        // ================================================================
        // 内部データ
        // ================================================================

        private Renderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private bool _initialized = false;
        private int _lastFrame = -1;
        private float _lastPosition = -1f;

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
            // Editorでの即時反映
            if (gameObject.activeInHierarchy)
            {
                Initialize();
                ApplyFrame();
            }
        }

        public void Initialize()
        {
            // CSV解析
            ParseCSV();

            // Renderer検索
            if (IncludeChildren)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                var r = GetComponent<Renderer>();
                _renderers = r != null ? new Renderer[] { r } : new Renderer[0];
            }

            _propertyBlock = new MaterialPropertyBlock();
            _initialized = true;
            _lastFrame = -1;
            _lastPosition = -1f;
        }

        // ================================================================
        // CSV解析
        // ================================================================

        private void ParseCSV()
        {
            if (string.IsNullOrEmpty(FrameCSV))
                return;

            var newFrames = new List<FrameData>();
            var lines = FrameCSV.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 5)
                    continue;

                string name = parts[0].Trim();
                if (float.TryParse(parts[1].Trim(), out float offsetX) &&
                    float.TryParse(parts[2].Trim(), out float offsetY) &&
                    float.TryParse(parts[3].Trim(), out float scaleX) &&
                    float.TryParse(parts[4].Trim(), out float scaleY))
                {
                    newFrames.Add(new FrameData(name, offsetX, offsetY, scaleX, scaleY));
                }
            }

            if (newFrames.Count > 0)
            {
                Frames = newFrames;
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        void LateUpdate()
        {
            if (!_initialized) Initialize();

            // 変更検出
            if (Interpolate)
            {
                if (Mathf.Abs(_lastPosition - FramePosition) > 0.001f)
                {
                    ApplyFrame();
                    _lastPosition = FramePosition;
                }
            }
            else
            {
                if (_lastFrame != CurrentFrame)
                {
                    ApplyFrame();
                    _lastFrame = CurrentFrame;
                }
            }
        }

        private void ApplyFrame()
        {
            if (Frames.Count == 0 || _renderers == null)
                return;

            Vector2 offset;
            Vector2 scale;

            if (Interpolate && Frames.Count > 1)
            {
                // 補間モード
                float clampedPos = Mathf.Clamp(FramePosition, 0f, Frames.Count - 1);
                int frameA = Mathf.FloorToInt(clampedPos);
                int frameB = Mathf.Min(frameA + 1, Frames.Count - 1);
                float t = clampedPos - frameA;

                var a = Frames[frameA];
                var b = Frames[frameB];

                offset = Vector2.Lerp(a.Offset, b.Offset, t);
                scale = Vector2.Lerp(a.Scale, b.Scale, t);
            }
            else
            {
                // 離散モード
                int idx = Mathf.Clamp(CurrentFrame, 0, Frames.Count - 1);
                var frame = Frames[idx];
                offset = frame.Offset;
                scale = frame.Scale;
            }

            // 全Rendererに適用
            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;

                ApplyToRenderer(renderer, offset, scale);
            }
        }

        private void ApplyToRenderer(Renderer renderer, Vector2 offset, Vector2 scale)
        {
            // MaterialPropertyBlockを使用（マテリアルインスタンスを作らない）
            var materials = renderer.sharedMaterials;

            for (int i = 0; i < materials.Length; i++)
            {
                if (MaterialIndex >= 0 && i != MaterialIndex)
                    continue;

                var mat = materials[i];
                if (mat == null)
                    continue;

                // テクスチャプロパティが存在するか確認
                if (!mat.HasProperty(TexturePropertyName))
                    continue;

                renderer.GetPropertyBlock(_propertyBlock, i);
                
                // ST = (ScaleX, ScaleY, OffsetX, OffsetY)
                Vector4 st = new Vector4(scale.x, scale.y, offset.x, offset.y);
                _propertyBlock.SetVector(TexturePropertyName + "_ST", st);
                
                renderer.SetPropertyBlock(_propertyBlock, i);
            }
        }

        // ================================================================
        // API
        // ================================================================

        /// <summary>
        /// フレームを設定（離散）
        /// </summary>
        public void SetFrame(int frame)
        {
            CurrentFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, Frames.Count - 1));
            ApplyFrame();
        }

        /// <summary>
        /// フレームを名前で設定
        /// </summary>
        public void SetFrameByName(string frameName)
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                if (Frames[i].Name == frameName)
                {
                    SetFrame(i);
                    return;
                }
            }
        }

        /// <summary>
        /// フレーム位置を設定（補間用）
        /// </summary>
        public void SetFramePosition(float position)
        {
            FramePosition = Mathf.Clamp(position, 0f, Mathf.Max(0f, Frames.Count - 1));
            Interpolate = true;
            ApplyFrame();
        }

        /// <summary>
        /// 正規化された位置で設定（0-1）
        /// </summary>
        public void SetNormalizedPosition(float normalized)
        {
            if (Frames.Count <= 1)
            {
                SetFramePosition(0f);
                return;
            }
            SetFramePosition(normalized * (Frames.Count - 1));
        }

        /// <summary>
        /// フレーム数を取得
        /// </summary>
        public int FrameCount => Frames.Count;

        /// <summary>
        /// フレーム名一覧を取得
        /// </summary>
        public string[] GetFrameNames()
        {
            var names = new string[Frames.Count];
            for (int i = 0; i < Frames.Count; i++)
            {
                names[i] = Frames[i].Name;
            }
            return names;
        }

        // ================================================================
        // ヘルパー：アトラス生成
        // ================================================================

        /// <summary>
        /// 均等分割アトラス用のフレームを自動生成
        /// </summary>
        /// <param name="cols">列数</param>
        /// <param name="rows">行数</param>
        /// <param name="startFrame">開始フレーム名</param>
        public void GenerateUniformGrid(int cols, int rows, string prefix = "frame_")
        {
            Frames.Clear();
            float scaleX = 1f / cols;
            float scaleY = 1f / rows;

            int index = 0;
            // 左上から右下へ
            for (int y = rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < cols; x++)
                {
                    float offsetX = x * scaleX;
                    float offsetY = y * scaleY;
                    Frames.Add(new FrameData($"{prefix}{index}", offsetX, offsetY, scaleX, scaleY));
                    index++;
                }
            }
        }

        /// <summary>
        /// CSVをFramesから生成
        /// </summary>
        public string GenerateCSV()
        {
            var lines = new List<string>();
            lines.Add("# Name,OffsetX,OffsetY,ScaleX,ScaleY");

            foreach (var frame in Frames)
            {
                lines.Add($"{frame.Name},{frame.Offset.x},{frame.Offset.y},{frame.Scale.x},{frame.Scale.y}");
            }

            return string.Join("\n", lines);
        }
    }
}
