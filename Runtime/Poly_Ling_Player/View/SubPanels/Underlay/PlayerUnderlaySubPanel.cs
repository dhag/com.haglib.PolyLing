// PlayerUnderlaySubPanel.cs
// 「下絵」（3D背面に敷く参照画像）の方向別設定パネル（UIToolkit・右ペイン）。
// 8方向スロット（Persp/Ortho/Top/Bottom/Front/Back/Left/Right）ごとに設定する。
// Runtime/Poly_Ling_Player/View/SubPanels/Underlay/ に配置
//
// 【設定の正本はモデル】
//   値は現在モデルの ModelContext.Underlay にあり、モデルと一緒に保存される。
//   このパネルは値を読んで表示し、変更は setUnderlay / clearUnderlay コマンドで送る
//   （MCP・記録と同じ経路。PanelCommand.Underlay.cs）。
//
// 【置き方は方向で決まる】
//   上下前後左右 … モデル座標の 2 隅（隅1・隅2 の XYZ）。カメラを動かしても位置関係が変わらない。
//   Persp / Ortho … 画面ピクセル基準（左上位置・拡大縮小の原点・2D スケール）。
//   方向に合わない側の欄は隠す。
// Runtime/Poly_Ling_Player/View/SubPanels/Underlay/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerUnderlaySubPanel
    {
        private readonly UnderlayConfig             _config;
        private readonly Func<PanelCommand, string> _send;        // コマンドを送り、失敗理由を返す（成功なら null）
        private readonly Func<int>                  _modelIndex;  // 送るコマンドのモデル番号
        private readonly Func<string, string>       _allowPath;   // ダイアログで選んだパスを 1 回だけ許可する

        // UI 自動操作の ID は "underlay.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("direction", Description = "設定する方向（8 方向のスロット）。切り替えると他の欄がその方向の値に変わる")]
        private DropdownField _dirDropdown;
        [UiControl("file", Safety = UiSafety.ReadOnly, Description = "読み込んだ画像のファイル名。未設定なら (未設定)")]
        private Label         _fileLabel;
        [UiControl("path", Safety = UiSafety.FileOperation, Setter = nameof(SetPathByAutomation),
                   Description = "下絵画像のパス。設定すると画像を読み込む。作業フォルダからの相対パスで指定する")]
        private TextField     _pathField;
        [UiControl("size", Safety = UiSafety.ReadOnly, Description = "画像の縦横画素数")]
        private Label         _sizeLabel;      // 画像の縦横画素数
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直前の操作の結果（失敗したときの理由）")]
        private Label         _statusLabel;

        // ── モデル座標基準（上下前後左右） ──
        [UiControl(Ignore = true)]
        private VisualElement _modelBox;
        [UiControl("corner0.x", Description = "上下前後左右のとき、画像の 1 隅のモデル座標 X")]
        private FloatField    _c0x;
        [UiControl("corner0.y", Description = "上下前後左右のとき、画像の 1 隅のモデル座標 Y")]
        private FloatField    _c0y;
        [UiControl("corner0.z", Description = "上下前後左右のとき、画像の 1 隅のモデル座標 Z")]
        private FloatField    _c0z;
        [UiControl("corner1.x", Description = "上下前後左右のとき、隅1 と向かい合う隅のモデル座標 X")]
        private FloatField    _c1x;
        [UiControl("corner1.y", Description = "上下前後左右のとき、隅1 と向かい合う隅のモデル座標 Y")]
        private FloatField    _c1y;
        [UiControl("corner1.z", Description = "上下前後左右のとき、隅1 と向かい合う隅のモデル座標 Z")]
        private FloatField    _c1z;
        [UiControl("resetPlacement", Safety = UiSafety.SafeWrite,
                   Description = "上下前後左右のとき、置き方を既定（原点中心・1 画素 = MQO の 1 単位）に戻す")]
        private Button        _resetPlacementBtn;

        // ── 画面ピクセル基準（Persp / Ortho） ──
        [UiControl(Ignore = true)]
        private VisualElement _screenBox;
        [UiControl("scale", Description = "Persp / Ortho のとき、X/Y 同時の拡大率。2D スケールの X・Y も同じ値になる")]
        private Slider        _scaleSlider;    // XY同時スケール
        [UiControl("topLeft.x", Description = "Persp / Ortho のとき、ビュー中央から画像の左上までの X（ビューの高さ = 1）")]
        private FloatField    _tlX;
        [UiControl("topLeft.y", Description = "Persp / Ortho のとき、ビュー中央から画像の左上までの Y（ビューの高さ = 1、下向き）")]
        private FloatField    _tlY;            // 左上位置
        [UiControl("origin.x", Description = "Persp / Ortho のとき、拡大縮小の原点の X（画像の画素基準）")]
        private FloatField    _orgX;
        [UiControl("origin.y", Description = "Persp / Ortho のとき、拡大縮小の原点の Y（画像の画素基準・下向き）")]
        private FloatField    _orgY;           // 拡大縮小の原点
        [UiControl("scale2d.x", Description = "Persp / Ortho のとき、2D スケールの X（1 で画像の高さ = ビューの高さ）")]
        private FloatField    _sclX;
        [UiControl("scale2d.y", Description = "Persp / Ortho のとき、2D スケールの Y（1 で画像の高さ = ビューの高さ）")]
        private FloatField    _sclY;           // 2Dスケール

        // ── 表示調整（全方向共通の欄。値は方向ごと） ──
        [UiControl("contrast", Description = "この方向の下絵のコントラスト（0〜1）。1 で元画像、0 で灰色一色")]
        private Slider        _contrastSlider;
        [UiControl("intensity", Description = "この方向の下絵の明るさ（0〜1）。1 で元画像、0 で黒")]
        private Slider        _intensitySlider;

        [UiControl("open", Safety = UiSafety.UserOnly, Description = "画像ファイルを選ぶダイアログを開く")]
        private Button _openBtn;
        [UiControl("browse", Safety = UiSafety.UserOnly, Description = "パス欄の [...]。画像ファイルを選ぶダイアログを開く")]
        private Button _browseBtn;
        [UiControl("clear", Safety = UiSafety.Destructive, Description = "この方向の下絵画像を外す")]
        private Button _clearBtn;
        [UiControl("originPreset.center", Safety = UiSafety.SafeWrite, Description = "Persp / Ortho のとき、原点を画像の中心にする")]
        private Button _originCenterBtn;
        [UiControl("originPreset.topLeft", Safety = UiSafety.SafeWrite, Description = "Persp / Ortho のとき、原点を画像の左上にする")]
        private Button _originTopLeftBtn;
        [UiControl("originPreset.bottomLeft", Safety = UiSafety.SafeWrite, Description = "Persp / Ortho のとき、原点を画像の左下にする")]
        private Button _originBottomLeftBtn;

        private const float ScaleMin = 0.1f;
        private const float ScaleMax = 10f;
        private const string PathKey = "Underlay.Path";

        private bool _suppress;  // フィールド→コマンド 送信の一時抑止（読込時）

        private static readonly List<string> DirNames = new List<string>
        {
            "Persp(透視)", "Ortho", "Top", "Bottom", "Front", "Back", "Left", "Right",
        };

        public PlayerUnderlaySubPanel(UnderlayConfig config, Func<PanelCommand, string> send,
                                      Func<int> modelIndex, Func<string, string> allowPath)
        {
            _config     = config;
            _send       = send;
            _modelIndex = modelIndex;
            _allowPath  = allowPath;
        }

        private UnderlayDirection CurrentDir =>
            (UnderlayDirection)Mathf.Clamp(_dirDropdown?.index ?? 0, 0, 7);

        /// <summary>
        /// 指定方向が現在選択中なら、フィールドを設定値へ再読込する。
        /// ビューポートの左ドラッグで位置が変化した際のライブ更新用。
        /// </summary>
        public void RefreshFields(UnderlayDirection dir)
        {
            if (_dirDropdown == null) return;
            if (dir == CurrentDir) LoadSlotToFields();
        }

        /// <summary>現在の方向の欄を読み直す（モデル切替・コマンドで変わったとき）。</summary>
        public void Refresh()
        {
            if (_dirDropdown == null) return;
            LoadSlotToFields();
        }

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();

            parent.Add(PlayerIoUiKit.Title("下絵（3D背面）"));

            var note = new Label("下絵はモデルと一緒に保存されます。上下前後左右はモデル座標の 2 隅で置き、カメラを動かしてもずれません。");
            note.style.whiteSpace   = WhiteSpace.Normal;
            note.style.fontSize     = 10;
            note.style.marginBottom = 4;
            parent.Add(note);

            // 方向選択
            _dirDropdown = new DropdownField("方向", DirNames, 0);
            _dirDropdown.style.marginBottom = 4;
            _dirDropdown.RegisterValueChangedCallback(_ => LoadSlotToFields());
            parent.Add(_dirDropdown);

            // ファイル読込 / クリア（loadPMX デザインに統一）
            parent.Add(PlayerIoUiKit.SectionLabel("画像ファイル"));
            _pathField = new TextField();
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(PathKey, e.newValue));
            parent.Add(PlayerIoUiKit.PathRow(_pathField, OnBrowseFile, out _browseBtn));
            _pathField.SetValueWithoutNotify(RecentPaths.Get(PathKey));

            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;
            var loadBtn = PlayerIoUiKit.OpenButton("開く", OnBrowseFile);
            loadBtn.style.flexGrow = 1; loadBtn.style.marginRight = 2;
            var clearBtn = new Button(OnClearFile) { text = "クリア" };
            clearBtn.style.width = 60;
            fileRow.Add(loadBtn); fileRow.Add(clearBtn);
            parent.Add(fileRow);
            _openBtn  = loadBtn;
            _clearBtn = clearBtn;

            _fileLabel = new Label("(未設定)");
            _fileLabel.style.marginBottom = 2;
            _fileLabel.style.whiteSpace   = WhiteSpace.Normal;
            parent.Add(_fileLabel);

            // 画像の縦横画素数（原点設定の目安用）
            _sizeLabel = new Label("サイズ: -");
            _sizeLabel.style.marginBottom = 6;
            parent.Add(_sizeLabel);

            // ── 表示調整（コントラスト・明るさ） ──
            _contrastSlider = new Slider("コントラスト", 0f, 1f) { value = 1f, showInputField = true };
            _contrastSlider.style.marginBottom = 2;
            _contrastSlider.RegisterValueChangedCallback(_ => SendAdjust());
            parent.Add(_contrastSlider);

            _intensitySlider = new Slider("明るさ", 0f, 1f) { value = 1f, showInputField = true };
            _intensitySlider.style.marginBottom = 6;
            _intensitySlider.RegisterValueChangedCallback(_ => SendAdjust());
            parent.Add(_intensitySlider);

            // ── モデル座標基準（上下前後左右） ──
            _modelBox = new VisualElement();
            AddXYZRow(_modelBox, "隅1", out _c0x, out _c0y, out _c0z);
            AddXYZRow(_modelBox, "隅2", out _c1x, out _c1y, out _c1z);
            _resetPlacementBtn = new Button(OnResetPlacement) { text = "既定の置き方に戻す" };
            _resetPlacementBtn.style.marginBottom = 4;
            _modelBox.Add(_resetPlacementBtn);
            parent.Add(_modelBox);

            // ── 画面ピクセル基準（Persp / Ortho） ──
            _screenBox = new VisualElement();
            AddXYRow(_screenBox, "左上(中央から/高さ比)", out _tlX,  out _tlY);
            AddXYRow(_screenBox, "原点",     out _orgX, out _orgY);

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
            _originCenterBtn     = btnCenter;
            _originTopLeftBtn    = btnTL;
            _originBottomLeftBtn = btnBL;
            btnCenter.style.flexGrow = 1; btnCenter.style.marginRight = 2;
            btnTL.style.flexGrow     = 1; btnTL.style.marginRight     = 2;
            btnBL.style.flexGrow     = 1;
            presetRow.Add(lblP); presetRow.Add(btnCenter); presetRow.Add(btnTL); presetRow.Add(btnBL);
            _screenBox.Add(presetRow);

            // XY同時スケールスライダー（0.1–10倍）。
            // スライダー → テキストへ反映（テキストからの通知は受けない）。
            _scaleSlider = new Slider("スケール(XY)", ScaleMin, ScaleMax) { value = 1f };
            _scaleSlider.style.marginBottom = 2;
            _scaleSlider.RegisterValueChangedCallback(evt =>
            {
                if (_suppress) return;
                float v = Mathf.Clamp(evt.newValue, ScaleMin, ScaleMax);
                _sclX.SetValueWithoutNotify(v);
                _sclY.SetValueWithoutNotify(v);
                SendPlacement();
            });
            _screenBox.Add(_scaleSlider);

            AddXYRow(_screenBox, "2Dスケール", out _sclX, out _sclY);
            parent.Add(_screenBox);

            _statusLabel = new Label("");
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.6f, 0.4f));
            parent.Add(_statusLabel);

            LoadSlotToFields();
        }

        /// <summary>
        /// UI 自動操作からパス欄を設定する（UiControl "path" の Setter）。
        /// 画像の読み込みは setUnderlay が行う（作業フォルダの関門もそちらで通す）。
        /// 成功で null、失敗で理由。
        /// </summary>
        private string SetPathByAutomation(string value)
        {
            string reason = Send(new SetUnderlayCommand(Index(), CurrentDir, value ?? "", keepPlacement: true));
            if (reason == null) RecentPaths.Set(PathKey, value ?? "");
            return reason;
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

            fx.RegisterValueChangedCallback(_ => SendPlacement());
            fy.RegisterValueChangedCallback(_ => SendPlacement());

            row.Add(lbl); row.Add(fx); row.Add(fy);
            parent.Add(row);
        }

        private void AddXYZRow(VisualElement parent, string label, out FloatField fx, out FloatField fy, out FloatField fz)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 40;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;

            fx = new FloatField(); fx.style.flexGrow = 1; fx.style.marginRight = 2;
            fy = new FloatField(); fy.style.flexGrow = 1; fy.style.marginRight = 2;
            fz = new FloatField(); fz.style.flexGrow = 1;

            fx.RegisterValueChangedCallback(_ => SendPlacement());
            fy.RegisterValueChangedCallback(_ => SendPlacement());
            fz.RegisterValueChangedCallback(_ => SendPlacement());

            row.Add(lbl); row.Add(fx); row.Add(fy); row.Add(fz);
            parent.Add(row);
        }

        /// <summary>現在方向の設定値をフィールドへ読み込み、方向に合う欄だけ見せる。</summary>
        private void LoadSlotToFields()
        {
            var dir = CurrentDir;
            var s   = _config.Peek(dir) ?? new UnderlaySlotData();
            var tex = _config.GetTexture(dir);
            bool anchored = UnderlayData.IsModelAnchored(dir);

            _suppress = true;
            _modelBox.style.display  = anchored ? DisplayStyle.Flex : DisplayStyle.None;
            _screenBox.style.display = anchored ? DisplayStyle.None : DisplayStyle.Flex;

            _c0x.value = s.Corner0.x; _c0y.value = s.Corner0.y; _c0z.value = s.Corner0.z;
            _c1x.value = s.Corner1.x; _c1y.value = s.Corner1.y; _c1z.value = s.Corner1.z;
            _tlX.value  = s.TopLeft.x;     _tlY.value  = s.TopLeft.y;
            _orgX.value = s.ScaleOrigin.x; _orgY.value = s.ScaleOrigin.y;
            _sclX.value = s.Scale.x;       _sclY.value = s.Scale.y;
            // スライダーはテキストへ通知せず現在スケール（X基準）へ同期。
            _scaleSlider?.SetValueWithoutNotify(Mathf.Clamp(s.Scale.x, ScaleMin, ScaleMax));
            _contrastSlider?.SetValueWithoutNotify(s.Contrast);
            _intensitySlider?.SetValueWithoutNotify(s.Intensity);

            if (s.IsEmpty)            _fileLabel.text = "(未設定)";
            else if (tex == null)     _fileLabel.text = $"{Path.GetFileName(s.FilePath)}（読めません）";
            else                      _fileLabel.text = Path.GetFileName(s.FilePath);
            if (!s.IsEmpty) _pathField?.SetValueWithoutNotify(s.FilePath);

            _sizeLabel.text = tex != null ? $"サイズ: {tex.width} × {tex.height} px" : "サイズ: -";
            _suppress = false;
        }

        private enum OriginAnchor { Center, TopLeft, BottomLeft }

        /// <summary>
        /// 原点を画像画素サイズ基準のプリセットへ設定する（要素ローカルpx／Y下向き）。
        /// 画像未設定時は何もしない。
        /// </summary>
        private void ApplyOriginPreset(OriginAnchor anchor)
        {
            var tex = _config.GetTexture(CurrentDir);
            if (tex == null) return;

            float w = tex.width;
            float h = tex.height;
            Vector2 origin;
            switch (anchor)
            {
                case OriginAnchor.Center:     origin = new Vector2(w * 0.5f, h * 0.5f); break;
                case OriginAnchor.TopLeft:    origin = new Vector2(0f, 0f);             break;
                case OriginAnchor.BottomLeft: origin = new Vector2(0f, h);              break;
                default:                      origin = Vector2.zero;                    break;
            }

            _suppress = true;
            _orgX.value = origin.x;
            _orgY.value = origin.y;
            _suppress = false;
            SendPlacement();
        }

        /// <summary>欄の値で現在方向の置き方を送る。画像が無い方向では何もしない。</summary>
        private void SendPlacement()
        {
            if (_suppress) return;
            var dir = CurrentDir;
            var s   = _config.Peek(dir);
            if (s == null || s.IsEmpty) return;

            Send(new SetUnderlayCommand(
                Index(), dir, "", keepPlacement: false,
                new Vector3(_c0x.value, _c0y.value, _c0z.value),
                new Vector3(_c1x.value, _c1y.value, _c1z.value),
                new Vector2(_tlX.value, _tlY.value),
                new Vector2(_orgX.value, _orgY.value),
                _sclX.value, _sclY.value));
        }

        /// <summary>コントラスト・明るさを現在方向へ送る。置き方は変えない。画像が無い方向では何もしない。</summary>
        private void SendAdjust()
        {
            if (_suppress) return;
            var dir = CurrentDir;
            var s   = _config.Peek(dir);
            if (s == null || s.IsEmpty) return;

            Send(new SetUnderlayCommand(Index(), dir, "", keepPlacement: true,
                contrast:  Mathf.Clamp01(_contrastSlider.value),
                intensity: Mathf.Clamp01(_intensitySlider.value)));
        }

        /// <summary>上下前後左右の置き方を既定に戻す（2 隅を同じ点にして送る）。</summary>
        private void OnResetPlacement()
        {
            var dir = CurrentDir;
            var s   = _config.Peek(dir);
            if (s == null || s.IsEmpty) return;
            Send(new SetUnderlayCommand(Index(), dir, "", keepPlacement: false));
        }

        // 「開く」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowseFile()
        {
            string path = PlayerIoUiKit.AskLoadPath(
                "下絵画像を選択", PathKey, _pathField.value, "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;
            _pathField.value = path;
            string allowed = _allowPath != null ? _allowPath(path) : path;
            Send(new SetUnderlayCommand(Index(), CurrentDir, allowed, keepPlacement: true));
        }

        private void OnClearFile()
        {
            Send(new ClearUnderlayCommand(Index(), CurrentDir));
        }

        private int Index() => _modelIndex?.Invoke() ?? 0;

        /// <summary>コマンドを送り、結果を表示して欄を読み直す。失敗理由を返す。</summary>
        private string Send(PanelCommand cmd)
        {
            string reason = _send != null ? _send(cmd) : "コマンドを送れません";
            if (_statusLabel != null) _statusLabel.text = reason ?? "";
            LoadSlotToFields();
            return reason;
        }
    }
}
