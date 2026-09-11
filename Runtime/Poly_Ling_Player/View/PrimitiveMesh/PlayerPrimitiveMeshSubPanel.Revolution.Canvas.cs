// PlayerPrimitiveMeshSubPanel.Revolution.Canvas.cs
// 図形生成サブパネル：回転体プロファイルのキャンバス（描画・ポインタ操作・
// 選択・アンカー・変換ハンドル）。
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
        // ── キャンバス描画 ────────────────────────────────────────────────

        // プロファイルビュー（ズーム/パン）を反映した座標変換ラッパー。
        private Vector2 RevP2C(Vector2 p, float w, float h)            => RevolutionProfileEditCore.ProfileToCanvas(p, w, h, _revZoom, _revOffset);
        private Vector2 RevC2P(Vector2 c, float w, float h)
            => RevolutionProfileEditCore.CanvasToProfile(c, w, h, _revZoom, _revOffset);
        private int RevFind(List<Vector2> prof, Vector2 c, float w, float h, float md)
            => RevolutionProfileEditCore.FindClosest(prof, c, w, h, md, _revZoom, _revOffset);

        /// <summary>下絵レイヤーにプロファイルビューと同じ変換（中心基準ズーム＋パン）を適用。</summary>
        private void UpdateRevView()
        {
            if (_revViewLayer == null) return;
            _revViewLayer.style.transformOrigin = new TransformOrigin(
                new Length(50, LengthUnit.Percent), new Length(50, LengthUnit.Percent), 0f);
            _revViewLayer.style.scale     = new Scale(new Vector3(_revZoom, _revZoom, 1f));
            _revViewLayer.style.translate = new Translate(
                new Length(_revOffset.x), new Length(_revOffset.y), 0f);
        }

        private void OnDrawProfileCanvas(MeshGenerationContext ctx)
        {            if (_revProfile == null || _revProfile.Count == 0) return;

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            if (w <= 0 || h <= 0) return;

            var p2d = ctx.painter2D;

            // グリッド
            p2d.strokeColor = new Color(0.28f, 0.28f, 0.33f);
            p2d.lineWidth   = 1f;
            p2d.BeginPath();
            for (float x = 0f; x <= RevolutionProfileEditCore.RangeX; x += 0.5f)
            {
                var s = RevP2C(new Vector2(x, -1f), w, h);
                var e = RevP2C(new Vector2(x,  2f), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            for (float y = -1f; y <= 2f; y += 0.5f)
            {
                var s = RevP2C(new Vector2(0f, y), w, h);
                var e = RevP2C(new Vector2(2f, y), w, h);
                p2d.MoveTo(s); p2d.LineTo(e);
            }
            p2d.Stroke();

            // 軸
            p2d.strokeColor = new Color(0.52f, 0.52f, 0.58f);
            p2d.lineWidth   = 1.5f;
            p2d.BeginPath();
            var ay0 = RevP2C(new Vector2(0f, -1f), w, h);
            var ay1 = RevP2C(new Vector2(0f,  2f), w, h);
            p2d.MoveTo(ay0); p2d.LineTo(ay1);
            var ax0 = RevP2C(new Vector2(0f, 0f), w, h);
            var ax1 = RevP2C(new Vector2(2f, 0f), w, h);
            p2d.MoveTo(ax0); p2d.LineTo(ax1);
            p2d.Stroke();

            // プロファイルライン（セグメントごとにホバー判定）
            if (_revProfile.Count >= 2)
            {
                int segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                for (int i = 0; i < segCount; i++)
                {
                    int  j   = (i + 1) % _revProfile.Count;
                    var  a   = RevP2C(_revProfile[i], w, h);
                    var  b   = RevP2C(_revProfile[j], w, h);
                    bool hov = (i == _revHoverEI);
                    p2d.strokeColor = hov ? new Color(0.2f, 0.9f, 0.3f) : new Color(0.2f, 0.75f, 0.85f);
                    p2d.lineWidth   = hov ? 3f : 1.5f;
                    p2d.BeginPath();
                    p2d.MoveTo(a); p2d.LineTo(b);
                    p2d.Stroke();
                }
            }

            // 頂点ドット
            for (int i = 0; i < _revProfile.Count; i++)
            {
                bool sel     = _revSel.Contains(i);
                bool primary = (i == _revSelIdx);
                var  sp      = RevP2C(_revProfile[i], w, h);
                p2d.fillColor = primary ? Color.white
                              : sel     ? new Color(1f, 0.85f, 0.2f)
                              :           new Color(0.2f, 0.75f, 0.85f);
                float r = (sel || primary) ? 5.5f : 3.5f;
                RevFillCircle(p2d, sp, r, 10);
            }

            // マーキー
            if (_revMarquee.Active)
                _revMarquee.Draw(p2d, new Color(1f, 0.85f, 0.2f, 0.9f));

            // アンカー（ギズモ表示OFFで抑止。アンカー設定中は常に表示）
            if (_revShowGizmo || _revAnchor.Mode)
                _revAnchor.Draw(p2d, RevP2C(_revAnchor.Value, w, h));

            // 回転/拡大縮小ハンドル（アンカー設定モード中/ギズモ表示OFFで非表示）
            if (_revShowGizmo && !_revAnchor.Mode)
                _revHandle.Draw(p2d, RevP2C(_revAnchor.Value, w, h));

            // マグネット半径（選択点まわり）
            if (_revMagnet.Enabled && _revSel.Count > 0)
            {
                var centers = new List<Vector2>();
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) centers.Add(RevP2C(_revProfile[i], w, h));
                float cr = Vector2.Distance(RevP2C(Vector2.zero, w, h),
                                            RevP2C(new Vector2(_revMagnet.Radius, 0f), w, h));
                _revMagnet.DrawRadius(p2d, centers, cr);
            }
        }

        private void RefreshRevCanvas() => _revCanvas?.MarkDirtyRepaint();

        private void RefreshRevPointUI()
        {
            if (_revPtRow == null) return;
            if (_revSelIdx >= 0 && _revProfile != null && _revSelIdx < _revProfile.Count)
            {
                var pt = _revProfile[_revSelIdx];
                if (_revPtLabel  != null) _revPtLabel.text = $"Pt {_revSelIdx}  R={pt.x:F3}  Y={pt.y:F3}";
                _revPtXSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.x, 0f,  2f));
                _revPtXField?.SetValueWithoutNotify((float)Math.Round(pt.x, 3));
                _revPtYSlider?.SetValueWithoutNotify(Mathf.Clamp(pt.y, -1f, 2f));
                _revPtYField?.SetValueWithoutNotify((float)Math.Round(pt.y, 3));
                _revPtRow.style.display = DisplayStyle.Flex;
            }
            else
            {
                _revPtRow.style.display = DisplayStyle.None;
            }
            RefreshRevAnchorAuto();
        }

        // ── キャンバスポインターイベント ─────────────────────────────────

        private void OnRevCanvasPointerDown(PointerDownEvent e)
        {
            // 中ボタン＝ビューのパン
            if (e.button == 2)
            {
                _revPanDrag        = true;
                _revPanStart       = e.localPosition;
                _revPanOffsetStart = _revOffset;
                _revCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }
            if (e.button != 0) return;

            // 下絵移動モード
            if (_revBgMode && _revBgTex != null)
            {
                _revBgDrag              = true;
                _revBgDragStart         = e.localPosition;
                _revBgOffsetOnDragStart = _revBgOffset;
                _revCanvas.CapturePointer(e.pointerId);
                e.StopPropagation(); return;
            }

            EnsureRevProfile();

            float w = _revCanvas.resolvedStyle.width;
            float h = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // アンカー設定モード：ドラッグでアンカー移動（点編集/マーキーは行わない）
            if (_revAnchor.Mode)
            {
                _revAnchor.Value = RevC2P(cp, w, h);
                _revAnchor.Manual = true;
                _revAnchorDrag = true;
                RefreshRevAnchorFields();
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 0. ハンドルヒット判定（回転/拡大縮小、点編集より優先。ギズモ表示OFFで無効）
            var revHit = _revShowGizmo
                ? _revHandle.HitTest(cp, RevP2C(_revAnchor.Value, w, h))
                : Canvas2DHandle.HandleType.None;
            if (revHit != Canvas2DHandle.HandleType.None)
            {
                BeginRevHandle(revHit, cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 1. 頂点ヒット判定（優先、15px以内）
            int ptIdx = RevFind(_revProfile, cp, w, h, 15f);
            if (ptIdx >= 0)
            {
                if (e.shiftKey)
                {
                    // Shift+クリック＝トグル（ドラッグ移動しない）
                    if (!_revSel.Add(ptIdx)) _revSel.Remove(ptIdx);
                    _revSelIdx = _revSel.Contains(ptIdx) ? ptIdx : RevPrimary();
                    _revCanvas.CapturePointer(e.pointerId);
                    RefreshRevCanvas(); RefreshRevPointUI();
                    e.StopPropagation(); return;
                }
                if (!_revSel.Contains(ptIdx)) { _revSel.Clear(); _revSel.Add(ptIdx); }
                _revSelIdx = ptIdx;
                RevBegin();
                BeginRevDrag(cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 2. セグメントヒット判定（10px以内）→ 即時挿入＆ドラッグ開始
            int   bestSeg  = -1;
            float bestDist = 10f;
            Vector2 insertProf = Vector2.zero;
            int   segCount = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                int   j  = (i + 1) % _revProfile.Count;
                var   a  = RevP2C(_revProfile[i], w, h);
                var   b  = RevP2C(_revProfile[j], w, h);
                float t  = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                float d  = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                if (d < bestDist)
                {
                    bestDist   = d;
                    bestSeg    = i;
                    // 挿入座標をプロファイル空間で計算
                    insertProf = Vector2.Lerp(_revProfile[i], _revProfile[j], t);
                    insertProf.x = Mathf.Max(0f, insertProf.x);
                }
            }
            if (bestSeg >= 0)
            {
                int insertIdx = bestSeg + 1;
                RevBegin();
                _revProfile.Insert(insertIdx, insertProf);
                _revSel.Clear(); _revSel.Add(insertIdx);
                _revSelIdx  = insertIdx;
                _revHoverEI = -1;
                BeginRevDrag(cp, w, h);
                _revCanvas.CapturePointer(e.pointerId);
                _revP.CurrentPreset = ProfilePreset.Custom;
                D(); RefreshRevCanvas(); RefreshRevPointUI();
                e.StopPropagation(); return;
            }

            // 3. 空領域 → 矩形/投げ縄マーキー選択（Shiftで追加）
            _revMarqueeAdditive = e.shiftKey;
            _revMarquee.Begin(cp, _revLassoMode);
            _revMarqueeDrag = true;
            _revCanvas.CapturePointer(e.pointerId);
            RefreshRevCanvas();
            e.StopPropagation();
        }

        /// <summary>選択集合の代表インデックス（無ければ -1）。</summary>
        private int RevPrimary()
        {
            foreach (var i in _revSel) return i;
            return -1;
        }

        /// <summary>選択点の一括ドラッグ開始（各点の開始位置とカーソル基準を記録）。</summary>
        private void BeginRevDrag(Vector2 cp, float w, float h)
        {
            _revDrag    = true;
            _revDragIdx = _revSelIdx;
            _revDragStart.Clear();
            if (_revProfile != null)
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) _revDragStart[i] = _revProfile[i];
            _revDragStartCursorProf = RevC2P(cp, w, h);

            // マグネット影響点（非選択で半径内）を確定
            _revMagnetStart.Clear(); _revMagnetW.Clear();
            if (_revMagnet.Enabled && _revProfile != null && _revSel.Count > 0)
            {
                var sel = new List<Vector2>();
                foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);
                for (int i = 0; i < _revProfile.Count; i++)
                {
                    if (_revSel.Contains(i)) continue;
                    float wt = _revMagnet.WeightFor(_revProfile[i], sel);
                    if (wt > 0f) { _revMagnetStart[i] = _revProfile[i]; _revMagnetW[i] = wt; }
                }
            }
        }

        /// <summary>マーキー内側の点を選択に反映する。</summary>
        private void ApplyRevMarquee()
        {
            float w = _revCanvas.resolvedStyle.width, h = _revCanvas.resolvedStyle.height;
            if (!_revMarqueeAdditive) _revSel.Clear();
            if (_revProfile != null)
                for (int i = 0; i < _revProfile.Count; i++)
                    if (_revMarquee.Contains(RevP2C(_revProfile[i], w, h))) _revSel.Add(i);
            _revSelIdx = _revSel.Contains(_revSelIdx) ? _revSelIdx : RevPrimary();
            RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>選択（無ければ全点）のプロファイル座標リスト。</summary>
        private List<Vector2> SelectedRevPoints()
        {
            var pts = new List<Vector2>();
            if (_revProfile == null) return pts;
            if (_revSel.Count > 0)
            {
                foreach (var i in _revSel)
                    if (i >= 0 && i < _revProfile.Count) pts.Add(_revProfile[i]);
            }
            else pts.AddRange(_revProfile);
            return pts;
        }

        private void BuildRevAnchorTransformUI(VisualElement pe)
        {
            // 選択点の移動など
            var tfFold = FoldSection(pe, T("SelectionTransform"), false);
            tfFold.Add(BuildTf2("移動 X/Y",   0f, 0f, out _revTfMoveX,  out _revTfMoveY));
            tfFold.Add(BuildTf2("スケール X/Y", 1f, 1f, out _revTfScaleX, out _revTfScaleY));
            tfFold.Add(BuildTf1("スケール軸 (°)", 0f, out _revTfScaleAxis));
            tfFold.Add(BuildTf1("回転 (°)",    0f, out _revTfRot));
            var applyRow = new VisualElement(); applyRow.style.flexDirection = FlexDirection.Row; applyRow.style.marginBottom = 4;
            SB(applyRow, "変換適用", ApplyRevTransform);
            SB(applyRow, "リセット", () =>
            {
                _revTfMoveX.value = 0f; _revTfMoveY.value = 0f;
                _revTfScaleX.value = 1f; _revTfScaleY.value = 1f; _revTfRot.value = 0f;
                _revTfScaleAxis.value = 0f;
            });
            tfFold.Add(applyRow);

            // マグネット（比例編集）
            var magFold = FoldSection(pe, T("Magnet"), false);
            var revMagRow = new VisualElement(); revMagRow.style.flexDirection = FlexDirection.Row; revMagRow.style.marginBottom = 2;
            var revMagToggle = new Toggle("有効") { value = _revMagnet.Enabled }; revMagToggle.style.marginRight = 6;
            revMagToggle.RegisterValueChangedCallback(ev => { _revMagnet.Enabled = ev.newValue; RefreshRevCanvas(); });
            var revFalloff = new EnumField(_revMagnet.Falloff); revFalloff.style.flexGrow = 1;
            revFalloff.RegisterValueChangedCallback(ev => _revMagnet.Falloff = (FalloffType)ev.newValue);
            revMagRow.Add(revMagToggle); revMagRow.Add(revFalloff);
            magFold.Add(revMagRow);
            magFold.Add(BuildAnchorRow("半径", 0.05f, 2f, _revMagnet.Radius, out _revMagnetRadius, out _,
                () => false, v => { _revMagnet.Radius = v; RefreshRevCanvas(); }));

            // アンカー
            var anchorFold = FoldSection(pe, T("AnchorSection"), false);
            _revAnchorEnterBtn = new Button(() => SetRevAnchorMode(true)) { text = "アンカー設定" };
            _revAnchorEnterBtn.style.marginBottom = 2;
            anchorFold.Add(_revAnchorEnterBtn);

            _revAnchorPanel = new VisualElement(); _revAnchorPanel.style.marginBottom = 4;
            {
                var headRow = new VisualElement(); headRow.style.flexDirection = FlexDirection.Row; headRow.style.marginBottom = 2;
                var lbl = new Label("アンカー調整中（キャンバスをドラッグで移動）"); lbl.style.fontSize = 10; lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                var done = new Button(() => SetRevAnchorMode(false)) { text = "決定" }; done.style.width = 60;
                headRow.Add(lbl); headRow.Add(done); _revAnchorPanel.Add(headRow);

                var presetRow = new VisualElement(); presetRow.style.flexDirection = FlexDirection.Row; presetRow.style.marginBottom = 2;
                SB(presetRow, "重心", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.Centroid));
                SB(presetRow, "中心", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.Center));
                SB(presetRow, "左上", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.TopLeft));
                SB(presetRow, "左下", () => ApplyRevAnchorPreset(Canvas2DAnchor.Preset.BottomLeft));
                _revAnchorPanel.Add(presetRow);

                _revAnchorPanel.Add(BuildAnchorRow("X", 0f, 2f, 0f, out _revAnchorXSlider, out _revAnchorXField,
                    () => _revAnchorSuppress, v => SetRevAnchorComponent(true, v)));
                _revAnchorPanel.Add(BuildAnchorRow("Y", -1f, 2f, 0f, out _revAnchorYSlider, out _revAnchorYField,
                    () => _revAnchorSuppress, v => SetRevAnchorComponent(false, v)));
            }
            anchorFold.Add(_revAnchorPanel);
            RefreshRevAnchorModeUI();
            RefreshRevAnchorFields();
        }

        private void SetRevAnchorMode(bool on)
        {
            _revAnchor.Mode = on;
            if (on) RefreshRevAnchorAuto();
            RefreshRevAnchorModeUI();
            RefreshRevCanvas();
        }
        private void RefreshRevAnchorModeUI()
        {
            if (_revAnchorEnterBtn != null) _revAnchorEnterBtn.style.display = _revAnchor.Mode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_revAnchorPanel    != null) _revAnchorPanel.style.display    = _revAnchor.Mode ? DisplayStyle.Flex : DisplayStyle.None;
        }
        private void RefreshRevAnchorFields()
        {
            _revAnchorSuppress = true;
            _revAnchorXSlider?.SetValueWithoutNotify(Mathf.Clamp(_revAnchor.Value.x, 0f, 2f));
            _revAnchorYSlider?.SetValueWithoutNotify(Mathf.Clamp(_revAnchor.Value.y, -1f, 2f));
            _revAnchorXField?.SetValueWithoutNotify(_revAnchor.Value.x);
            _revAnchorYField?.SetValueWithoutNotify(_revAnchor.Value.y);
            _revAnchorSuppress = false;
        }
        private void RefreshRevAnchorAuto()
        {
            if (_revAnchor.Manual) return;
            var pts = SelectedRevPoints();
            if (pts.Count > 0) _revAnchor.SetPreset(pts, Canvas2DAnchor.Preset.Centroid);
            RefreshRevAnchorFields();
        }
        private void SetRevAnchorComponent(bool isX, float v)
        {
            var a = _revAnchor.Value; if (isX) a.x = v; else a.y = v; _revAnchor.Value = a;
            _revAnchor.Manual = true;
            RefreshRevAnchorFields(); RefreshRevCanvas();
        }
        private void ApplyRevAnchorPreset(Canvas2DAnchor.Preset p)
        {
            _revAnchor.SetPreset(SelectedRevPoints(), p);
            RefreshRevAnchorFields(); RefreshRevCanvas();
        }

        private void ApplyRevTransform()
        {
            EnsureRevProfile();
            RevBegin();
            RefreshRevAnchorAuto();
            var a  = _revAnchor.Value;
            float mx = _revTfMoveX?.value ?? 0f, my = _revTfMoveY?.value ?? 0f;
            float sx = _revTfScaleX?.value ?? 1f, sy = _revTfScaleY?.value ?? 1f;
            float deg = _revTfRot?.value ?? 0f;
            float saRad = (_revTfScaleAxis?.value ?? 0f) * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);

            bool useSel = _revSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);
            var orig = new List<Vector2>(_revProfile);

            for (int i = 0; i < orig.Count; i++)
            {
                float wt;
                if (!useSel)                 wt = 1f;                 // 選択なし＝全点フル変形
                else if (_revSel.Contains(i)) wt = 1f;
                else wt = _revMagnet.Enabled ? _revMagnet.WeightFor(orig[i], sel) : 0f;
                if (wt <= 0f) continue;

                var np = Xform2D(orig[i], a, mx, my, sx, sy, saCos, saSin, deg, wt);
                np.x = Mathf.Max(0f, np.x);   // 回転体 R は非負
                _revProfile[i] = np;
            }
            _revP.CurrentPreset = ProfilePreset.Custom;
            RevCommit("変換適用");
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        // ── 回転体：ハンドルドラッグ（回転/拡大縮小） ─────────────────────

        /// <summary>ハンドルドラッグ開始。影響点（選択=1/マグネット=weight、選択なし=全点1）を記録。</summary>
        private void BeginRevHandle(Canvas2DHandle.HandleType type, Vector2 cp, float w, float h)
        {
            _revHandleDrag = true;
            _revHandleType = type;
            _revHandle.Active = type;
            RefreshRevAnchorAuto();
            RevBegin();

            _revHandleAnchorC   = RevP2C(_revAnchor.Value, w, h);
            _revHandlePrevAngle = Canvas2DHandle.AngleDeg(_revHandleAnchorC, cp);
            _revHandleTotalDeg  = 0f;

            _revHandleStart.Clear(); _revHandleW.Clear();
            if (_revProfile == null) return;

            bool useSel = _revSel.Count > 0;
            var sel = new List<Vector2>();
            if (useSel) foreach (var i in _revSel) if (i >= 0 && i < _revProfile.Count) sel.Add(_revProfile[i]);

            for (int i = 0; i < _revProfile.Count; i++)
            {
                float wt;
                if (!useSel)                  wt = 1f;
                else if (_revSel.Contains(i)) wt = 1f;
                else wt = _revMagnet.Enabled ? _revMagnet.WeightFor(_revProfile[i], sel) : 0f;
                if (wt <= 0f) continue;
                _revHandleStart[i] = _revProfile[i];
                _revHandleW[i]     = wt;
            }
        }

        /// <summary>ハンドルドラッグ中：開始スナップショットへ変換を適用（ライブプレビュー）。</summary>
        private void ApplyRevHandle(Vector2 cp, float w, float h)
        {
            if (!_revHandleDrag || _revProfile == null) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (_revHandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(_revHandleAnchorC, cp);
                _revHandleTotalDeg += -Mathf.DeltaAngle(_revHandlePrevAngle, ang); // Y反転補正
                _revHandlePrevAngle = ang;
                deg = _revHandleTotalDeg;
            }
            else
            {
                _revHandle.ScaleFactors(_revHandleType, _revHandleAnchorC, cp, out sx, out sy);
            }

            var a = _revAnchor.Value;
            foreach (var kv in _revHandleStart)
            {
                int i = kv.Key;
                if (i < 0 || i >= _revProfile.Count) continue;
                var np = Xform2D(kv.Value, a, 0f, 0f, sx, sy, 1f, 0f, deg, _revHandleW[i]);
                np.x = Mathf.Max(0f, np.x);
                _revProfile[i] = np;
            }
            _revP.CurrentPreset = ProfilePreset.Custom;
            D(); RefreshRevCanvas(); RefreshRevPointUI();
        }

        /// <summary>ハンドルドラッグ終了：コミット。</summary>
        private void EndRevHandle()
        {
            if (!_revHandleDrag) return;
            _revHandleDrag = false;
            _revHandleType = Canvas2DHandle.HandleType.None;
            _revHandle.Active = Canvas2DHandle.HandleType.None;
            RevCommit("回転/拡大縮小");
        }

        private void OnRevCanvasPointerMove(PointerMoveEvent e)
        {
            float w  = _revCanvas.resolvedStyle.width;
            float h  = _revCanvas.resolvedStyle.height;
            var   cp = new Vector2(e.localPosition.x, e.localPosition.y);

            // 中ボタンパン
            if (_revPanDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revOffset = _revPanOffsetStart + (cp - _revPanStart);
                UpdateRevView(); UpdateRevBgEl(); RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // 下絵移動モード
            if (_revBgDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revBgOffset = _revBgOffsetOnDragStart
                             + (RevC2P(cp, w, h) - RevC2P(_revBgDragStart, w, h));
                UpdateRevBgEl();
                e.StopPropagation(); return;
            }

            // アンカードラッグ
            if (_revAnchorDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revAnchor.Value = RevC2P(cp, w, h);
                RefreshRevAnchorFields(); RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            // ハンドルドラッグ（回転/拡大縮小）
            if (_revHandleDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                ApplyRevHandle(cp, w, h);
                e.StopPropagation(); return;
            }

            // マーキー更新
            if (_revMarqueeDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                _revMarquee.Update(cp);
                RefreshRevCanvas();
                e.StopPropagation(); return;
            }

            if (_revDrag && _revCanvas.HasPointerCapture(e.pointerId))
            {
                if (_revProfile != null && _revDragStart.Count > 0)
                {
                    // 選択点を一括で delta 移動（カーソル追従・グラブ相対を維持）。
                    var delta = RevC2P(cp, w, h) - _revDragStartCursorProf;
                    foreach (var kv in _revDragStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= _revProfile.Count) continue;
                        var np = kv.Value + delta;
                        np.x = Mathf.Max(0f, np.x);
                        _revProfile[idx] = np;
                    }
                    // マグネット: 非選択点を delta×weight で追従
                    foreach (var kv in _revMagnetStart)
                    {
                        int idx = kv.Key;
                        if (idx < 0 || idx >= _revProfile.Count) continue;
                        var np = kv.Value + delta * _revMagnetW[idx];
                        np.x = Mathf.Max(0f, np.x);
                        _revProfile[idx] = np;
                    }
                    _revP.CurrentPreset = ProfilePreset.Custom;
                    D(); RefreshRevCanvas(); RefreshRevPointUI();
                }
                e.StopPropagation(); return;
            }

            // ハンドルホバー更新（非ドラッグ中。ギズモ表示OFF/アンカー設定中は無効）
            var revHovType = (_revAnchor.Mode || !_revShowGizmo)
                                             ? Canvas2DHandle.HandleType.None
                                             : _revHandle.HitTest(cp, RevP2C(_revAnchor.Value, w, h));
            if (revHovType != _revHandle.Hovered) { _revHandle.Hovered = revHovType; RefreshRevCanvas(); }

            // ホバーエッジ更新（非ドラッグ中）
            int prevHov = _revHoverEI;
            _revHoverEI = -1;

            // 頂点近傍ならホバーなし
            bool nearPt = RevFind(_revProfile, cp, w, h, 15f) >= 0;
            if (!nearPt)
            {
                int   segCount2 = _revP.CloseLoop ? _revProfile.Count : _revProfile.Count - 1;
                float bestD     = 10f;
                for (int i = 0; i < segCount2; i++)
                {
                    int   j = (i + 1) % _revProfile.Count;
                    var   a = RevP2C(_revProfile[i], w, h);
                    var   b = RevP2C(_revProfile[j], w, h);
                    float t = Mathf.Clamp01(Vector2.Dot(cp - a, b - a) / Mathf.Max(0.0001f, (b - a).sqrMagnitude));
                    float d = Vector2.Distance(cp, Vector2.Lerp(a, b, t));
                    if (d < bestD) { bestD = d; _revHoverEI = i; }
                }
            }

            if (_revHoverEI != prevHov) RefreshRevCanvas();
        }

        private void OnRevCanvasPointerUp(PointerUpEvent e)
        {
            if (!_revCanvas.HasPointerCapture(e.pointerId)) return;
            _revCanvas.ReleasePointer(e.pointerId);
            if (_revMarqueeDrag) { ApplyRevMarquee(); _revMarquee.End(); _revMarqueeDrag = false; }
            if (_revHandleDrag) EndRevHandle();
            bool wasRevDrag = _revDrag;
            _revDrag    = false;
            _revBgDrag  = false;
            _revPanDrag = false;
            _revAnchorDrag = false;
            if (wasRevDrag) RevCommit("プロファイル点編集");
            e.StopPropagation();
        }
    }
}
