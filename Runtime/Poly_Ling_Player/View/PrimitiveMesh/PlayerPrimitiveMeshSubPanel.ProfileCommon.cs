// PlayerPrimitiveMeshSubPanel.ProfileCommon.cs
// 図形生成サブパネル：2D プロファイル編集の共通部品
// （回転体・2D押し出し・ベルト断面で共用するリサイズ・アンカー・変換・下絵）。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.NohMask;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Core;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // プロファイル編集キャンバス高さ（下端ドラッグで手動リサイズ）
        private float _profileHeight = 260f;
        private bool  _profileResizeDragging;
        private float _profileResizeStartY;
        private float _profileResizeStartHeight;
        private const float ProfileMinHeight = 120f;
        private const float ProfileMaxHeight = 900f;

        /// <summary>Painter2D でポリゴン近似の塗りつぶし円を描く</summary>
        private static void RevFillCircle(Painter2D p2d, Vector2 center, float radius, int n)
        {
            p2d.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float a  = i * Mathf.PI * 2f / n;
                var   pt = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                if (i == 0) p2d.MoveTo(pt); else p2d.LineTo(pt);
            }
            p2d.ClosePath();
            p2d.Fill();
        }

        // ================================================================
        // 回転/拡大縮小アンカー・変換（共通ビルダー＋回転体側）
        // ================================================================

        /// <summary>アンカーX/Y行（スライダー＋テキスト）を作る。</summary>
        private VisualElement BuildAnchorRow(string label, float min, float max, float val,
            out Slider slider, out FloatField field, Func<bool> suppressed, Action<float> onChange)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label + ":"); lb.style.width = 16; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            var sl = new Slider(min, max) { value = Mathf.Clamp(val, min, max) }; sl.style.flexGrow = 1; sl.style.marginRight = 3;
            var ff = new FloatField { value = val }; ff.style.width = 52;
            sl.RegisterValueChangedCallback(e => { if (!suppressed()) onChange(e.newValue); });
            ff.RegisterValueChangedCallback(e => { if (!suppressed()) onChange(e.newValue); });
            row.Add(lb); row.Add(sl); row.Add(ff);
            slider = sl; field = ff; return row;
        }

        /// <summary>2フィールド行（例: 移動 X/Y、スケール X/Y）。</summary>
        private VisualElement BuildTf2(string label, float v1, float v2, out FloatField f1, out FloatField f2)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label); lb.style.width = 70; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            f1 = new FloatField { value = v1 }; f1.style.flexGrow = 1;
            f2 = new FloatField { value = v2 }; f2.style.flexGrow = 1;
            row.Add(lb); row.Add(f1); row.Add(f2); return row;
        }

        /// <summary>1フィールド行（例: 回転）。</summary>
        private VisualElement BuildTf1(string label, float v, out FloatField f)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            var lb = new Label(label); lb.style.width = 70; lb.style.fontSize = 10; lb.style.unityTextAlign = TextAnchor.MiddleLeft;
            f = new FloatField { value = v }; f.style.flexGrow = 1;
            row.Add(lb); row.Add(f); return row;
        }

        /// <summary>
        /// アンカー a 基準の2D変換（移動/スケール(スケール軸フレーム)/回転、重み wt）。
        /// 3キャンバス（回転体/Profile2D/UV と同一数式）で共有。
        /// </summary>
        private static Vector2 Xform2D(Vector2 p, Vector2 a,
            float mx, float my, float sx, float sy, float saCos, float saSin, float deg, float wt)
        {
            float sxw = 1f + (sx - 1f) * wt, syw = 1f + (sy - 1f) * wt;
            float degw = deg * wt * Mathf.Deg2Rad;
            float cw = Mathf.Cos(degw), sw = Mathf.Sin(degw);
            Vector2 d = p - a;
            // スケール軸フレームへ回転(-φ) → 重み付きスケール → 戻す(+φ)
            float rx =  d.x * saCos + d.y * saSin;
            float ry = -d.x * saSin + d.y * saCos;
            rx *= sxw; ry *= syw;
            d = new Vector2(rx * saCos - ry * saSin, rx * saSin + ry * saCos);
            // 重み付き全体回転
            d = new Vector2(d.x * cw - d.y * sw, d.x * sw + d.y * cw);
            return a + d + new Vector2(mx, my) * wt;
        }

        // ================================================================
        // 下絵ヘルパー（Rev/P2d 共用）
        // ================================================================

        /// <summary>下絵セクションUIを構築する共通メソッド</summary>
        private void BuildBgSection(VisualElement c, string sectionLabel,
            Func<string> getPath, Action<string> setPath,
            Func<float> getAlpha, Action<float> setAlpha,
            Func<bool>  getMode,  Action<bool>  setMode,
            Func<float> getScale, Action<float> setScale,
            Func<Vector2> getOrigin, Action<Vector2> setOrigin,
            Func<Texture2D> getTex,
            Action onLoad, Action onClear,
            out Slider scaleSlider, out Label sizeLabel)
        {
            // 見出しをフォールドのタイトルにして中身を折り畳む（既定は閉じる）。
            // 回転体・2D押し出し・断面エディタの全てで同じ見え方にする。
            var bgFold = new Foldout { text = sectionLabel, value = false };
            bgFold.style.marginBottom = 4;
            var bgHost = c;
            c = bgFold.contentContainer;
            bgHost.Add(bgFold);

            // パス入力（loadPMX デザインに統一：[...] 左＋TextField＋RecentPaths）
            string bgKey  = "Primitive.Bg." + sectionLabel;
            string bgInit = string.IsNullOrEmpty(getPath()) ? RecentPaths.Get(bgKey) : getPath();
            var pathField = new TextField();
            pathField.SetValueWithoutNotify(bgInit);
            if (!string.IsNullOrEmpty(bgInit)) setPath(bgInit);
            pathField.RegisterValueChangedCallback(e => { setPath(e.newValue); RecentPaths.Set(bgKey, e.newValue); });
            // PMX読込と同じ操作感：[...] も「読込」も必ずダイアログを出す。
            void LoadBgImage()
            {
                string path = PlayerIoUiKit.AskLoadPath("Select Image", bgKey, getPath(), "png,jpg,jpeg");
                if (string.IsNullOrEmpty(path)) return;
                pathField.value = path;
                onLoad();
            }

            c.Add(PlayerIoUiKit.PathRow(pathField, LoadBgImage));

            // Load / Clear ボタン行
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 3;
            SB(row, T("BgLoad"),  LoadBgImage);
            SB(row, T("BgClear"), onClear);
            c.Add(row);

            // アルファスライダー
            c.Add(SR(T("BgAlpha"), 0f, 1f, getAlpha, v => setAlpha(v)));

            // ── 下絵操作サブモード ─────────────────────────────────────
            // 通常時は「下絵を調整」ボタン、サブモード中は「下絵調整中」＋「決定」
            // と調整パネル（スケール／原点プリセット／画素数）を表示する。
            var enterBtn = new Button { text = "下絵を調整" };
            enterBtn.style.marginBottom = 2;

            var adjustPanel = new VisualElement();
            adjustPanel.style.marginBottom = 4;

            // 「下絵調整中」＋「決定」
            var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
            var adjustingLbl = new Label("下絵調整中"); adjustingLbl.style.flexGrow = 1; adjustingLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var doneBtn = new Button { text = "決定" }; doneBtn.style.width = 60;
            headRow.Add(adjustingLbl); headRow.Add(doneBtn);
            adjustPanel.Add(headRow);

            // スケールスライダー（0.1–10）
            var sclSlider = new Slider("スケール", 0.1f, 10f) { value = Mathf.Clamp(getScale(), 0.1f, 10f) };
            sclSlider.style.marginBottom = 2;
            sclSlider.RegisterValueChangedCallback(e => setScale(Mathf.Clamp(e.newValue, 0.1f, 10f)));
            adjustPanel.Add(sclSlider);

            // 原点プリセット（画像px基準・Y下向き）
            var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
            SB(presetRow, "原点:中心", () => { var t = getTex(); if (t != null) setOrigin(new Vector2(t.width * 0.5f, t.height * 0.5f)); });
            SB(presetRow, "左上",     () => { if (getTex() != null) setOrigin(Vector2.zero); });
            SB(presetRow, "左下",     () => { var t = getTex(); if (t != null) setOrigin(new Vector2(0f, t.height)); });
            adjustPanel.Add(presetRow);

            // 画素数
            var sizeLbl = new Label("サイズ: -");
            adjustPanel.Add(sizeLbl);

            c.Add(enterBtn);
            c.Add(adjustPanel);

            void RefreshModeUI()
            {
                bool on = getMode();
                enterBtn.style.display    = on ? DisplayStyle.None : DisplayStyle.Flex;
                adjustPanel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            enterBtn.clicked += () => { setMode(true);  RefreshModeUI(); };
            doneBtn.clicked  += () => { setMode(false); RefreshModeUI(); };
            RefreshModeUI();

            scaleSlider = sclSlider;
            sizeLabel   = sizeLbl;
        }

        /// <summary>画素数ラベルを更新する。</summary>
        private static void SetBgSizeLabel(Label lbl, Texture2D tex)
        {
            if (lbl == null) return;
            lbl.text = tex != null ? $"サイズ: {tex.width} × {tex.height} px" : "サイズ: -";
        }

        /// <summary>テクスチャをファイルパスから読み込んでBgElに設定</summary>
        private static void LoadBgTexture(string path, ref Texture2D tex, VisualElement bgEl)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                if (tex == null)
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    Debug.LogWarning($"[BgImage] LoadImage failed: {path}");
                    return;
                }
                bgEl.style.backgroundImage = new StyleBackground(tex);
                bgEl.style.display         = DisplayStyle.Flex;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[BgImage] Load failed: {ex.Message}");
            }
        }

        /// <summary>プロファイル編集キャンバス直下に縦リサイズハンドルを追加する（3Dプレビューと同方式）。</summary>
        private void AddProfileResizeHandle(VisualElement container, VisualElement canvas, Action refresh)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginBottom    = 4;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                _profileResizeDragging    = true;
                _profileResizeStartY      = e.position.y;
                _profileResizeStartHeight = _profileHeight;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_profileResizeDragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - _profileResizeStartY;
                _profileHeight = Mathf.Clamp(_profileResizeStartHeight + delta, ProfileMinHeight, ProfileMaxHeight);
                canvas.style.height = _profileHeight;
                refresh?.Invoke();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                _profileResizeDragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }
    }
}
