// PlayerEditSpaceBar.cs
// 作業空間バー。右ペイン中区画（RightPanelKind.EditSpace）に常駐し、
// 下区画でツールを切り替えても消えない（設計方針 5.1）。
// Runtime/Poly_Ling_Player/View/SubPanels/EditSpace/ に配置
//
// 【操作はコマンドで】
//   反映・取消し・終了・ロック切替はすべて SendCommand で送る（MCP からも同じ経路）。
//   バーは作業空間の状態を読んで表示するだけ。

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.EditSpace;

namespace Poly_Ling.Player
{
    public class PlayerEditSpaceBar
    {
        // ── コールバック ──────────────────────────────────────────────────
        /// <summary>開いている作業空間。無ければ null。</summary>
        public Func<EditSpaceSession> GetSession;

        /// <summary>コマンドを送り、失敗理由を返す（成功なら null）。</summary>
        public Func<PanelCommand, string> SendCommand;

        /// <summary>コマンドのモデル番号。</summary>
        public Func<int> GetModelIndex;

        /// <summary>開いている作業空間の作業板スロット（断面系の下絵）。無ければ null。</summary>
        public Func<UnderlayPlateSlotData> GetPlate;

        /// <summary>作業板スロットの画像の縦横（画素）。読めなければ null。</summary>
        public Func<UnderlayPlateSlotData, Vector2Int?> GetPlateImageSize;

        /// <summary>ダイアログで選んだパスを 1 回だけ許可する（作業フォルダの関門）。</summary>
        public Func<string, string> AllowPath;

        // ── UI ────────────────────────────────────────────────────────────
        // UI 自動操作の ID は "editSpace.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("title", Safety = UiSafety.ReadOnly, Description = "作業空間の種類と対象（元の描画オブジェクト・対象の面の数）")]
        private Label  _titleLabel;
        [UiControl("state", Safety = UiSafety.ReadOnly, Description = "未反映の変更の有無・平面制約")]
        private Label  _stateLabel;
        [UiControl("locked", Description = "ビルボードのロック（オンで代理を基準ビューへ正対、オフで 3D で回り込める）")]
        private Toggle _lockToggle;
        [UiControl("planeConstraint", Description = "平面制約（オンで移動・回転・拡大縮小を代理のローカル XY 面に限る）。UV は外せない")]
        private Toggle _planeToggle;
        [UiControl("underlay", Description = "下絵（UV なら対象マテリアルのテクスチャ）を作業ビューに重ねる。ロック中だけ出る。保存しない")]
        private Toggle _underlayToggle;
        [UiControl("underlayInfo", Safety = UiSafety.ReadOnly, Description = "下絵に使うテクスチャの名前とマテリアル番号")]
        private Label  _underlayLabel;
        [UiControl("imageRatio", Description = "UV を下絵のテクスチャの縦横比で表示する（画素が正方形に見える。UV の値は変わらない）。未反映の変更があると切り替えない。UV だけ")]
        private Toggle _imageRatioToggle;
        // ── 断面系：作業板スロット（利用者が置く参考画像） ──
        [UiControl(Ignore = true)]
        private VisualElement _plateBox;
        [UiControl("plate.file", Safety = UiSafety.ReadOnly, Description = "断面の作業空間の下絵（作業板スロット）の画像ファイル名と大きさ")]
        private Label _plateFileLabel;
        [UiControl("plate.open", Safety = UiSafety.UserOnly, Description = "下絵の画像ファイルを選ぶダイアログを開く")]
        private Button _plateOpenBtn;
        [UiControl("plate.clear", Safety = UiSafety.Destructive, Description = "下絵（作業板スロット）を外す")]
        private Button _plateClearBtn;
        [UiControl("plate.corner0.x", Description = "下絵の 1 隅の X（代理のローカル座標）")]
        private FloatField _pc0x;
        [UiControl("plate.corner0.y", Description = "下絵の 1 隅の Y（代理のローカル座標）")]
        private FloatField _pc0y;
        [UiControl("plate.corner1.x", Description = "下絵の向かい合う隅の X（代理のローカル座標）")]
        private FloatField _pc1x;
        [UiControl("plate.corner1.y", Description = "下絵の向かい合う隅の Y（代理のローカル座標）")]
        private FloatField _pc1y;
        [UiControl("plate.resetPlacement", Safety = UiSafety.SafeWrite, Description = "下絵の置き方を既定（代理の範囲の高さに合わせて中央）に戻す")]
        private Button _plateResetBtn;
        [UiControl("plate.contrast", Description = "下絵のコントラスト（0〜1）")]
        private Slider _plateContrast;
        [UiControl("plate.intensity", Description = "下絵の明るさ（0〜1）")]
        private Slider _plateIntensity;

        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "代理を元データへ反映する（元の描画オブジェクト側の 1 回の Undo）")]
        private Button _applyBtn;
        [UiControl("cancel", Safety = UiSafety.SafeWrite, Description = "未反映の変更を捨て、代理を元データの状態へ戻す")]
        private Button _cancelBtn;
        [UiControl("close", Safety = UiSafety.SafeWrite, Description = "作業空間を閉じる（未反映の変更があると閉じない）")]
        private Button _closeBtn;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label  _statusLabel;

        private bool _suppress;

        // ── Build ─────────────────────────────────────────────────────────
        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight = 4;
            _root.style.paddingTop  = _root.style.paddingBottom = 2;
            parent.Add(_root);

            _titleLabel = new Label();
            _titleLabel.style.color      = new StyleColor(new Color(0.65f, 0.85f, 1f));
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_titleLabel);

            _stateLabel = new Label();
            _stateLabel.style.color      = new StyleColor(Color.white);
            _stateLabel.style.fontSize   = 10;
            _stateLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_stateLabel);

            _lockToggle = new Toggle("ビルボードをロック") { value = true };
            _lockToggle.style.color = new StyleColor(Color.white);
            _lockToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                Send(new SetEditSpaceBillboardLockCommand(Index(), e.newValue));
            });
            _root.Add(_lockToggle);

            _planeToggle = new Toggle("平面制約") { value = true };
            _planeToggle.style.color = new StyleColor(Color.white);
            _planeToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                Send(new SetEditSpacePlaneConstraintCommand(Index(), e.newValue));
            });
            _root.Add(_planeToggle);

            // 種類固有の行（UV：下絵）。
            _underlayToggle = new Toggle("下絵を表示") { value = true };
            _underlayToggle.style.color = new StyleColor(Color.white);
            _underlayToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                Send(new SetEditSpaceUnderlayCommand(Index(), e.newValue));
            });
            _root.Add(_underlayToggle);

            _imageRatioToggle = new Toggle("画像の縦横比で表示") { value = false };
            _imageRatioToggle.style.color = new StyleColor(Color.white);
            _imageRatioToggle.RegisterValueChangedCallback(e =>
            {
                if (_suppress) return;
                Send(new SetEditSpaceImageRatioCommand(Index(), e.newValue));
            });
            _root.Add(_imageRatioToggle);

            _underlayLabel = new Label();
            _underlayLabel.style.color      = new StyleColor(Color.white);
            _underlayLabel.style.fontSize   = 10;
            _underlayLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_underlayLabel);

            BuildPlateBox();

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop     = 2;
            _applyBtn  = MakeBtn(row, "反映",   () => Send(new ApplyEditSpaceCommand(Index())));
            _cancelBtn = MakeBtn(row, "取消し", () => Send(new CancelEditSpaceCommand(Index())));
            _closeBtn  = MakeBtn(row, "終了",   () => Send(new CloseEditSpaceCommand(Index())));
            _root.Add(row);

            _statusLabel = new Label();
            _statusLabel.style.color      = new StyleColor(new Color(0.9f, 0.85f, 0.4f));
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(_statusLabel);
        }

        /// <summary>断面系の下絵（作業板スロット）の行。UV では隠す。</summary>
        private void BuildPlateBox()
        {
            _plateBox = new VisualElement();
            _plateBox.style.marginTop = 2;
            _root.Add(_plateBox);

            _plateFileLabel = new Label();
            _plateFileLabel.style.color      = new StyleColor(Color.white);
            _plateFileLabel.style.fontSize   = 10;
            _plateFileLabel.style.whiteSpace = WhiteSpace.Normal;
            _plateBox.Add(_plateFileLabel);

            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            _plateOpenBtn  = MakeBtn(fileRow, "下絵の画像…", OnPlateOpen);
            _plateClearBtn = MakeBtn(fileRow, "下絵を外す",   () => Send(new ClearEditSpacePlateUnderlayCommand(Index())));
            _plateBox.Add(fileRow);

            AddXYRow(_plateBox, "隅1", out _pc0x, out _pc0y);
            AddXYRow(_plateBox, "隅2", out _pc1x, out _pc1y);

            var resetRow = new VisualElement();
            resetRow.style.flexDirection = FlexDirection.Row;
            _plateResetBtn = MakeBtn(resetRow, "既定の置き方",
                () => SendPlate(new SetEditSpacePlateUnderlayCommand(Index(), "", keepPlacement: false)));
            _plateBox.Add(resetRow);

            _plateContrast = new Slider("コントラスト", 0f, 1f) { value = 1f, showInputField = true };
            _plateContrast.RegisterValueChangedCallback(_ => SendPlateAdjust());
            _plateBox.Add(_plateContrast);
            _plateIntensity = new Slider("明るさ", 0f, 1f) { value = 1f, showInputField = true };
            _plateIntensity.RegisterValueChangedCallback(_ => SendPlateAdjust());
            _plateBox.Add(_plateIntensity);
        }

        private void AddXYRow(VisualElement parent, string label, out FloatField fx, out FloatField fy)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label(label);
            lbl.style.width = 32;
            lbl.style.color = new StyleColor(Color.white);
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            fx = new FloatField(); fx.style.flexGrow = 1; fx.style.marginRight = 2;
            fy = new FloatField(); fy.style.flexGrow = 1;
            fx.RegisterValueChangedCallback(_ => SendPlatePlacement());
            fy.RegisterValueChangedCallback(_ => SendPlatePlacement());
            row.Add(lbl); row.Add(fx); row.Add(fy);
            parent.Add(row);
        }

        private void OnPlateOpen()
        {
            var cur = GetPlate?.Invoke();
            string path = PlayerIoUiKit.AskLoadPath("下絵画像を選択", "EditSpace.PlatePath",
                                                    cur?.FilePath ?? "", "png,jpg,jpeg,tga,bmp");
            if (string.IsNullOrEmpty(path)) return;
            string allowed = AllowPath != null ? AllowPath(path) : path;
            SendPlate(new SetEditSpacePlateUnderlayCommand(Index(), allowed, keepPlacement: true));
        }

        /// <summary>欄の 2 隅で置き方を送る。画像が無ければ何もしない。</summary>
        private void SendPlatePlacement()
        {
            if (_suppress) return;
            var cur = GetPlate?.Invoke();
            if (cur == null || cur.IsEmpty) return;
            SendPlate(new SetEditSpacePlateUnderlayCommand(Index(), "", keepPlacement: false,
                new Vector2(_pc0x.value, _pc0y.value), new Vector2(_pc1x.value, _pc1y.value)));
        }

        /// <summary>コントラスト・明るさを送る。置き方は変えない。</summary>
        private void SendPlateAdjust()
        {
            if (_suppress) return;
            var cur = GetPlate?.Invoke();
            if (cur == null || cur.IsEmpty) return;
            SendPlate(new SetEditSpacePlateUnderlayCommand(Index(), "", keepPlacement: true,
                contrast: Mathf.Clamp01(_plateContrast.value), intensity: Mathf.Clamp01(_plateIntensity.value)));
        }

        private void SendPlate(PanelCommand cmd) => Send(cmd);

        /// <summary>断面系の下絵の行を読み直す。</summary>
        private void RefreshPlateBox(EditSpaceSession s)
        {
            bool user = s.Adapter?.BackdropSource == EditSpaceBackdropSource.UserImage;
            _plateBox.style.display      = user ? DisplayStyle.Flex : DisplayStyle.None;
            _underlayLabel.style.display = user ? DisplayStyle.None : DisplayStyle.Flex;
            if (!user) return;

            var p = GetPlate?.Invoke();
            _suppress = true;
            if (p == null || p.IsEmpty)
            {
                _plateFileLabel.text = s.Locked ? "下絵：なし" : "下絵：なし（ロック中だけ表示）";
                _pc0x.value = _pc0y.value = _pc1x.value = _pc1y.value = 0f;
                _plateContrast.SetValueWithoutNotify(1f);
                _plateIntensity.SetValueWithoutNotify(1f);
            }
            else
            {
                var size = GetPlateImageSize?.Invoke(p);
                string name = System.IO.Path.GetFileName(p.FilePath);
                string sz   = size.HasValue ? $"{size.Value.x}×{size.Value.y}" : "読めません";
                _plateFileLabel.text = s.Locked ? $"下絵：{name}（{sz}）" : $"下絵：{name}（{sz}）ロック中だけ表示";
                _pc0x.value = p.Corner0.x; _pc0y.value = p.Corner0.y;
                _pc1x.value = p.Corner1.x; _pc1y.value = p.Corner1.y;
                _plateContrast.SetValueWithoutNotify(p.Contrast);
                _plateIntensity.SetValueWithoutNotify(p.Intensity);
            }
            _suppress = false;

            bool has = p != null && !p.IsEmpty;
            _plateClearBtn.SetEnabled(has);
            _plateResetBtn.SetEnabled(has);
            _pc0x.SetEnabled(has); _pc0y.SetEnabled(has); _pc1x.SetEnabled(has); _pc1y.SetEnabled(has);
            _plateContrast.SetEnabled(has);
            _plateIntensity.SetEnabled(has);
        }

        // ── Refresh ───────────────────────────────────────────────────────
        public void Refresh()
        {
            if (_root == null) return;
            var s = GetSession?.Invoke();
            if (s == null)
            {
                _titleLabel.text  = "作業空間は開いていません";
                _stateLabel.text  = "";
                _statusLabel.text = "";
                _underlayLabel.text = "";
                _plateBox.style.display = DisplayStyle.None;
                _imageRatioToggle.style.display = DisplayStyle.None;
                SetEnabled(false);
                return;
            }

            string kind = s.Adapter?.DisplayName ?? "?";
            _titleLabel.text = s.TemporaryProxy
                ? $"作業空間 {kind}：{s.Source?.Name ?? "?"}（面 {s.FaceCount}）"
                : $"作業空間 {kind}：代理 {s.Proxy?.Name ?? "?"}（線分 {s.FaceCount}）";

            if (s.IsBroken)
            {
                _stateLabel.text = "元または代理の描画オブジェクトがモデルにありません。反映できません。終了してください。";
                _stateLabel.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
            }
            else
            {
                string changes = s.HasChanges ? "未反映の変更あり" : "変更なし";
                string plane   = s.Adapter?.PlaneConstraint == EditSpacePlaneConstraint.Required
                    ? "平面制約：必須" : (s.PlaneConstrained ? "平面制約：オン" : "平面制約：オフ");
                _stateLabel.text = $"{changes}　／　{plane}";
                _stateLabel.style.color = new StyleColor(s.HasChanges ? new Color(1f, 0.8f, 0.3f) : Color.white);
            }

            _suppress = true;
            _lockToggle.value     = s.Locked;
            _planeToggle.value    = s.PlaneConstrained;
            _underlayToggle.value = s.ShowUnderlay;
            _imageRatioToggle.value = s.UseImageRatio;
            _suppress = false;

            // 画像の縦横比は UV（下絵がテクスチャ）だけ。
            bool uvKind = s.Adapter?.BackdropSource == EditSpaceBackdropSource.SourceMaterialTexture;
            _imageRatioToggle.style.display = uvKind ? DisplayStyle.Flex : DisplayStyle.None;

            string texName = s.UnderlayTexture != null ? s.UnderlayTexture.name : "なし";
            _underlayLabel.text = s.Locked
                ? $"下絵：{texName}（マテリアル {s.UnderlayMaterialIndex}）　黄枠 = UV 0〜1"
                : $"下絵：{texName}（マテリアル {s.UnderlayMaterialIndex}）　ロック中だけ表示";

            _statusLabel.text = s.LastMessage ?? "";
            SetEnabled(true);
            RefreshPlateBox(s);
            _applyBtn.SetEnabled(!s.IsBroken);
            _cancelBtn.SetEnabled(!s.IsBroken);
            _lockToggle.SetEnabled(!s.IsBroken);
            _planeToggle.SetEnabled(!s.IsBroken && s.Adapter?.PlaneConstraint != EditSpacePlaneConstraint.Required);
            _underlayToggle.SetEnabled(!s.IsBroken);
            // 未反映の変更があると切り替えられない（切替は代理を作り直すため）。
            _imageRatioToggle.SetEnabled(!s.IsBroken && !s.HasChanges);
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private void Send(PanelCommand cmd)
        {
            string reason = SendCommand?.Invoke(cmd);
            var s = GetSession?.Invoke();
            if (reason != null)
            {
                if (s != null) s.LastMessage = reason;
                else if (_statusLabel != null) _statusLabel.text = reason;
            }
            Refresh();
        }

        private int Index() => GetModelIndex?.Invoke() ?? 0;

        private void SetEnabled(bool on)
        {
            _lockToggle?.SetEnabled(on);
            _planeToggle?.SetEnabled(on);
            _underlayToggle?.SetEnabled(on);
            _imageRatioToggle?.SetEnabled(on);
            _applyBtn?.SetEnabled(on);
            _cancelBtn?.SetEnabled(on);
            _closeBtn?.SetEnabled(on);
        }

        private static Button MakeBtn(VisualElement row, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1;
            b.style.height   = 24;
            row.Add(b);
            return b;
        }
    }
}
