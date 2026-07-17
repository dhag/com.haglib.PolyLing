// PlayerUnderlaySubPanel.cs
// 「下絵」（3D背面に敷く参照画像）の方向別設定パネル（UIToolkit・右ペイン）。
// 8方向スロット（Persp/Ortho/Top/Bottom/Front/Back/Left/Right）ごとに
// ファイル・左上位置・拡大縮小の原点・2Dスケールを設定する。
// 値変更時は onChanged を呼び、ViewerCore がビューポートへ再適用する。
// Runtime/Poly_Ling_Player/View/SubPanels/Underlay/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerUnderlaySubPanel
    {
        private readonly UnderlayConfig _config;
        private readonly Action         _onChanged;  // 値変更時に呼ぶ（再適用要求）

        private DropdownField _dirDropdown;
        private Label         _fileLabel;
        private TextField     _pathField;
        private Label         _sizeLabel;      // 画像の縦横画素数
        private Slider        _scaleSlider;    // XY同時スケール
        private FloatField    _tlX, _tlY;      // 左上位置
        private FloatField    _orgX, _orgY;    // 拡大縮小の原点
        private FloatField    _sclX, _sclY;    // 2Dスケール

        private const float ScaleMin = 0.1f;
        private const float ScaleMax = 10f;
        private const string PathKey = "Underlay.Path";

        private bool _suppress;  // フィールド→設定 反映の一時抑止（ロード時）

        private static readonly List<string> DirNames = new List<string>
        {
            "Persp(透視)", "Ortho", "Top", "Bottom", "Front", "Back", "Left", "Right",
        };

        public PlayerUnderlaySubPanel(UnderlayConfig config, Action onChanged)
        {
            _config    = config;
            _onChanged = onChanged;
        }

        private UnderlayDirection CurrentDir =>
            (UnderlayDirection)Mathf.Clamp(_dirDropdown?.index ?? 0, 0, 7);

        /// <summary>
        /// 指定方向が現在選択中なら、フィールドをスロット値へ再読込する。
        /// ビューポートの左ドラッグでオフセットが変化した際のライブ更新用。
        /// </summary>
        public void RefreshFields(UnderlayDirection dir)
        {
            if (_dirDropdown == null) return;
            if (dir == CurrentDir) LoadSlotToFields();
        }

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();

            parent.Add(PlayerIoUiKit.Title("下絵（3D背面）"));

            // 方向選択
            _dirDropdown = new DropdownField("方向", DirNames, 0);
            _dirDropdown.style.marginBottom = 4;
            _dirDropdown.RegisterValueChangedCallback(_ => LoadSlotToFields());
            parent.Add(_dirDropdown);

            // ファイル読込 / クリア（loadPMX デザインに統一）
            parent.Add(PlayerIoUiKit.SectionLabel("画像ファイル"));
            _pathField = new TextField();
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(PathKey, e.newValue));
            parent.Add(PlayerIoUiKit.PathRow(_pathField, OnBrowseFile));
            _pathField.SetValueWithoutNotify(RecentPaths.Get(PathKey));

            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;
            var loadBtn = PlayerIoUiKit.OpenButton("開く", () => LoadFromPath(_pathField.value));
            loadBtn.style.flexGrow = 1; loadBtn.style.marginRight = 2;
            var clearBtn = new Button(OnClearFile) { text = "クリア" };
            clearBtn.style.width = 60;
            fileRow.Add(loadBtn); fileRow.Add(clearBtn);
            parent.Add(fileRow);

            _fileLabel = new Label("(未設定)");
            _fileLabel.style.marginBottom = 2;
            _fileLabel.style.whiteSpace   = WhiteSpace.Normal;
            parent.Add(_fileLabel);

            // 画像の縦横画素数（原点設定の目安用）
            _sizeLabel = new Label("サイズ: -");
            _sizeLabel.style.marginBottom = 6;
            parent.Add(_sizeLabel);

            AddXYRow(parent, "左上位置",   out _tlX,  out _tlY);
            AddXYRow(parent, "原点",       out _orgX, out _orgY);

            // 原点プリセット（画像画素サイズ基準。要素ローカルpx／Y下向き）
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            var lblP = new Label("原点プリセット");
            lblP.style.width          = 72;
            lblP.style.unityTextAlign = TextAnchor.MiddleLeft;
            var btnCenter = new Button(() => ApplyOriginPreset(OriginAnchor.Center))    { text = "中心" };
            var btnTL     = new Button(() => ApplyOriginPreset(OriginAnchor.TopLeft))    { text = "左上" };
            var btnBL     = new Button(() => ApplyOriginPreset(OriginAnchor.BottomLeft)) { text = "左下" };
            btnCenter.style.flexGrow = 1; btnCenter.style.marginRight = 2;
            btnTL.style.flexGrow     = 1; btnTL.style.marginRight     = 2;
            btnBL.style.flexGrow     = 1;
            presetRow.Add(lblP); presetRow.Add(btnCenter); presetRow.Add(btnTL); presetRow.Add(btnBL);
            parent.Add(presetRow);

            // XY同時スケールスライダー（0.1–10倍）。
            // スライダー → テキストへ反映（テキストからの通知は受けない）。
            _scaleSlider = new Slider("スケール(XY)", ScaleMin, ScaleMax) { value = 1f };
            _scaleSlider.style.marginBottom = 2;
            _scaleSlider.RegisterValueChangedCallback(evt =>
            {
                float v = Mathf.Clamp(evt.newValue, ScaleMin, ScaleMax);
                _sclX.SetValueWithoutNotify(v);
                _sclY.SetValueWithoutNotify(v);
                var s = _config.Get(CurrentDir);
                s.Scale = new Vector2(v, v);
                _onChanged?.Invoke();
            });
            parent.Add(_scaleSlider);

            AddXYRow(parent, "2Dスケール", out _sclX, out _sclY);

            LoadSlotToFields();
        }

        private void AddXYRow(VisualElement parent, string label, out FloatField fx, out FloatField fy)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 72;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;

            fx = new FloatField(); fx.style.flexGrow = 1; fx.style.marginRight = 2;
            fy = new FloatField(); fy.style.flexGrow = 1;

            fx.RegisterValueChangedCallback(_ => WriteFieldsToSlot());
            fy.RegisterValueChangedCallback(_ => WriteFieldsToSlot());

            row.Add(lbl); row.Add(fx); row.Add(fy);
            parent.Add(row);
        }

        /// <summary>現在方向のスロット値をフィールドへ読み込む。</summary>
        private void LoadSlotToFields()
        {
            var s = _config.Get(CurrentDir);
            _suppress = true;
            _tlX.value  = s.TopLeft.x;     _tlY.value  = s.TopLeft.y;
            _orgX.value = s.ScaleOrigin.x; _orgY.value = s.ScaleOrigin.y;
            _sclX.value = s.Scale.x;       _sclY.value = s.Scale.y;
            // スライダーはテキストへ通知せず現在スケール（X基準）へ同期。
            _scaleSlider?.SetValueWithoutNotify(Mathf.Clamp(s.Scale.x, ScaleMin, ScaleMax));
            _fileLabel.text = s.HasImage
                ? (string.IsNullOrEmpty(s.FilePath) ? "(読込済)" : Path.GetFileName(s.FilePath))
                : "(未設定)";
            _pathField?.SetValueWithoutNotify(s.FilePath ?? "");
            UpdateSizeLabel(s);
            _suppress = false;
        }

        /// <summary>画像の縦横画素数ラベルを更新する。</summary>
        private void UpdateSizeLabel(UnderlaySlot s)
        {
            if (_sizeLabel == null) return;
            _sizeLabel.text = (s != null && s.HasImage)
                ? $"サイズ: {s.Texture.width} × {s.Texture.height} px"
                : "サイズ: -";
        }

        private enum OriginAnchor { Center, TopLeft, BottomLeft }

        /// <summary>
        /// 原点を画像画素サイズ基準のプリセットへ設定する（要素ローカルpx／Y下向き）。
        /// 画像未設定時は何もしない。
        /// </summary>
        private void ApplyOriginPreset(OriginAnchor anchor)
        {
            var s = _config.Get(CurrentDir);
            if (!s.HasImage) return;

            float w = s.Texture.width;
            float h = s.Texture.height;
            Vector2 origin;
            switch (anchor)
            {
                case OriginAnchor.Center:     origin = new Vector2(w * 0.5f, h * 0.5f); break;
                case OriginAnchor.TopLeft:    origin = new Vector2(0f, 0f);             break;
                case OriginAnchor.BottomLeft: origin = new Vector2(0f, h);              break;
                default:                      origin = Vector2.zero;                    break;
            }

            s.ScaleOrigin = origin;
            _orgX.SetValueWithoutNotify(origin.x);
            _orgY.SetValueWithoutNotify(origin.y);
            _onChanged?.Invoke();
        }

        /// <summary>フィールド値を現在方向のスロットへ書き込み、再適用を要求する。</summary>
        private void WriteFieldsToSlot()
        {
            if (_suppress) return;
            var s = _config.Get(CurrentDir);
            s.TopLeft     = new Vector2(_tlX.value,  _tlY.value);
            s.ScaleOrigin = new Vector2(_orgX.value, _orgY.value);
            s.Scale       = new Vector2(_sclX.value, _sclY.value);
            _onChanged?.Invoke();
        }

        private void OnBrowseFile()
        {
            string dir  = string.IsNullOrEmpty(_pathField.value)
                ? Application.dataPath : Path.GetDirectoryName(_pathField.value);
            string path = PLEditorBridge.I.OpenFilePanel("下絵画像を選択", dir, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;
            _pathField.value = path;
            LoadFromPath(path);
        }

        private void LoadFromPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                byte[] data = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2);
                if (!tex.LoadImage(data))
                {
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);

                var s = _config.Get(CurrentDir);
                if (s.Texture != null) UnityEngine.Object.Destroy(s.Texture);
                s.Texture  = tex;
                s.FilePath = path;
                _fileLabel.text = Path.GetFileName(path);
                UpdateSizeLabel(s);
                _onChanged?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Underlay] 画像読込に失敗: {e.Message}");
            }
        }

        private void OnClearFile()
        {
            var s = _config.Get(CurrentDir);
            if (s.Texture != null) UnityEngine.Object.Destroy(s.Texture);
            s.Texture  = null;
            s.FilePath = string.Empty;
            _fileLabel.text = "(未設定)";
            UpdateSizeLabel(s);
            _onChanged?.Invoke();
        }
    }
}
