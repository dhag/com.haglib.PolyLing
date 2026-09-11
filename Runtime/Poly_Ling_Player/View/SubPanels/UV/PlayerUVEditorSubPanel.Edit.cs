// PlayerUVEditorSubPanel.Edit.cs
// UV エディタ：UV 移動・ハンドルドラッグ・Fit／選択操作・一括変換・アンカー。
// Runtime/Poly_Ling_Player/View/SubPanels/UV/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public partial class PlayerUVEditorSubPanel
    {
        // ================================================================
        // UV移動
        // ================================================================

        private void BeginUVMove()
        {
            _dragStartUVs.Clear();
            _uvMagnetW.Clear();
            var mo = GetMeshObject();
            if (mo == null) return;
            var selPos = new List<Vector2>();
            foreach (var id in _selected)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                {
                    _dragStartUVs[id] = v.UVs[id.UVIndex];
                    _uvMagnetW[id]    = 1f;
                    selPos.Add(v.UVs[id.UVIndex]);
                }
            }
            // マグネット影響頂点（非選択で半径内）
            if (_uvMagnet.Enabled && selPos.Count > 0)
            {
                var tested = new HashSet<long>();
                foreach (var face in mo.Faces)
                {
                    if (face == null) continue;
                    if (_matsWithVerts.Count > 0 && face.MaterialIndex != _matsWithVerts[_selectedMatListIndex]) continue;
                    for (int ci = 0; ci < face.VertexCount; ci++)
                    {
                        int vi = face.VertexIndices[ci];
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                        long key = ((long)vi << 32) | (uint)ui;
                        if (!tested.Add(key)) continue;
                        var id = new UVVertexId(vi, ui);
                        if (_dragStartUVs.ContainsKey(id)) continue;
                        var v = mo.Vertices[vi];
                        if (ui < 0 || ui >= v.UVs.Count) continue;
                        float wt = _uvMagnet.WeightFor(v.UVs[ui], selPos);
                        if (wt > 0f) { _dragStartUVs[id] = v.UVs[ui]; _uvMagnetW[id] = wt; }
                    }
                }
            }
            _interaction = Interaction.MovingVertex;
        }

        private void ApplyUVMove(Vector2 currentPos)
        {
            var mo = GetMeshObject();
            if (mo == null || _dragStartUVs.Count == 0) return;
            var rect    = _canvas.contentRect;
            Vector2 d   = CanvasToUV(currentPos, rect) - CanvasToUV(_mouseDownPos, rect);
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                {
                    float wt = _uvMagnetW.TryGetValue(id, out var mw) ? mw : 1f;
                    v.UVs[id.UVIndex] = kv.Value + d * wt;
                }
            }
        }

        private void EndUVMove()
        {
            var mo = GetMeshObject();
            if (mo == null || _dragStartUVs.Count == 0) { _dragStartUVs.Clear(); return; }

            bool moved = false;
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count &&
                    Vector2.Distance(v.UVs[id.UVIndex], kv.Value) > 0.0001f)
                { moved = true; break; }
            }

            if (!moved) { _dragStartUVs.Clear(); return; }

            // 変更後UV を収集
            var keys = new List<UVVertexId>(_dragStartUVs.Keys);
            var afterUVs = new Vector2[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var id = keys[i];
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) { afterUVs[i] = _dragStartUVs[id]; continue; }
                var v = mo.Vertices[id.VertexIndex];
                afterUVs[i] = (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    ? v.UVs[id.UVIndex] : _dragStartUVs[id];
            }

            // ドラッグ中の変更を元に戻す（コマンドが AfterUVs を再適用する）
            foreach (var kv in _dragStartUVs)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                    v.UVs[id.UVIndex] = kv.Value;
            }

            // コマンド送信
            if (_panelContext != null)
            {
                var mc = GetMeshContext();
                var model = GetModel?.Invoke();
                int masterIdx = mc != null && model != null ? model.IndexOf(mc) : 0;
                int modelIdx  = _getModelIndex?.Invoke() ?? 0;
                var viArr     = new int   [keys.Count];
                var uiArr     = new int   [keys.Count];
                var beforeArr = new Vector2[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    viArr[i]     = keys[i].VertexIndex;
                    uiArr[i]     = keys[i].UVIndex;
                    beforeArr[i] = _dragStartUVs[keys[i]];
                }
                SendCmd(new ApplyUVChangesCommand(modelIdx, masterIdx,
                    viArr, uiArr, beforeArr, afterUVs, $"UV Move {keys.Count}V"));
            }
            else
            {
                // フォールバック（PanelContext 未設定時）
                RecordTopologyChange($"UV Move {keys.Count}V", obj =>
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var id = keys[i];
                        if (id.VertexIndex < 0 || id.VertexIndex >= obj.VertexCount) continue;
                        var v = obj.Vertices[id.VertexIndex];
                        if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                            v.UVs[id.UVIndex] = afterUVs[i];
                    }
                });
            }

            SetStatus($"UV {keys.Count}頂点を移動");
            _dragStartUVs.Clear();
        }

        // ================================================================
        // ハンドルドラッグ（回転/拡大縮小）
        // ================================================================

        /// <summary>ハンドルドラッグ開始。影響UV頂点（選択=1/マグネット=weight、選択なし=全頂点1）を記録。</summary>
        private void BeginUVHandle(Canvas2DHandle.HandleType type, Vector2 mp, Rect rect)
        {
            _interaction      = Interaction.HandleDrag;
            _uvHandleType     = type;
            _uvHandle.Active  = type;

            RefreshAnchorAuto();  // 自動モードなら重心へ
            _uvHandleAnchorC   = UVToCanvas(_anchor, rect);
            _uvHandlePrevAngle = Canvas2DHandle.AngleDeg(_uvHandleAnchorC, mp);
            _uvHandleTotalDeg  = 0f;

            _uvHandleStart.Clear(); _uvHandleW.Clear();
            var mo = GetMeshObject();
            if (mo == null) return;

            if (_selected.Count > 0)
            {
                var selPos = new List<Vector2>();
                foreach (var id in _selected)
                {
                    if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                    var v = mo.Vertices[id.VertexIndex];
                    if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                    _uvHandleStart[id] = v.UVs[id.UVIndex];
                    _uvHandleW[id]     = 1f;
                    selPos.Add(v.UVs[id.UVIndex]);
                }
                if (_uvMagnet.Enabled && selPos.Count > 0)
                {
                    foreach (var id in CollectAllUVVertices(mo))
                    {
                        if (_uvHandleStart.ContainsKey(id)) continue;
                        if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                        var v = mo.Vertices[id.VertexIndex];
                        if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                        float wt = _uvMagnet.WeightFor(v.UVs[id.UVIndex], selPos);
                        if (wt > 0f) { _uvHandleStart[id] = v.UVs[id.UVIndex]; _uvHandleW[id] = wt; }
                    }
                }
            }
            else
            {
                foreach (var id in CollectAllUVVertices(mo))
                {
                    if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                    var v = mo.Vertices[id.VertexIndex];
                    if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                    _uvHandleStart[id] = v.UVs[id.UVIndex];
                    _uvHandleW[id]     = 1f;
                }
            }
        }

        /// <summary>ハンドルドラッグ中：開始スナップショットへ変換を適用（ライブプレビュー）。</summary>
        private void ApplyUVHandle(Vector2 mp)
        {
            var mo = GetMeshObject();
            if (mo == null || _uvHandleStart.Count == 0) return;

            float sx = 1f, sy = 1f, deg = 0f;
            if (_uvHandleType == Canvas2DHandle.HandleType.Rotate)
            {
                float ang = Canvas2DHandle.AngleDeg(_uvHandleAnchorC, mp);
                _uvHandleTotalDeg += -Mathf.DeltaAngle(_uvHandlePrevAngle, ang);
                _uvHandlePrevAngle = ang;
                deg = _uvHandleTotalDeg;
            }
            else
            {
                _uvHandle.ScaleFactors(_uvHandleType, _uvHandleAnchorC, mp, out sx, out sy);
            }

            foreach (var kv in _uvHandleStart)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                // saCos=1, saSin=0（軸整列）、移動なし
                v.UVs[id.UVIndex] = CalcTransformedUV(kv.Value, _anchor, 0f, 0f, sx, sy, deg, 1f, 0f, _uvHandleW[id]);
            }
        }

        /// <summary>ハンドルドラッグ終了：before/after をコマンド（またはUndo記録）でコミット。</summary>
        private void EndUVHandle()
        {
            _uvHandle.Active = Canvas2DHandle.HandleType.None;
            _uvHandleType    = Canvas2DHandle.HandleType.None;

            var mo = GetMeshObject();
            if (mo == null || _uvHandleStart.Count == 0) { _uvHandleStart.Clear(); return; }

            bool moved = false;
            foreach (var kv in _uvHandleStart)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count &&
                    Vector2.Distance(v.UVs[id.UVIndex], kv.Value) > 0.0001f)
                { moved = true; break; }
            }
            if (!moved) { _uvHandleStart.Clear(); return; }

            var keys     = new List<UVVertexId>(_uvHandleStart.Keys);
            var afterUVs = new Vector2[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                var id = keys[i];
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) { afterUVs[i] = _uvHandleStart[id]; continue; }
                var v = mo.Vertices[id.VertexIndex];
                afterUVs[i] = (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count) ? v.UVs[id.UVIndex] : _uvHandleStart[id];
            }

            // ドラッグ中の変更を元に戻す（コマンドが after を再適用する）
            foreach (var kv in _uvHandleStart)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count) v.UVs[id.UVIndex] = kv.Value;
            }

            if (_panelContext != null)
            {
                var mc = GetMeshContext();
                var model = GetModel?.Invoke();
                int masterIdx = mc != null && model != null ? model.IndexOf(mc) : 0;
                int modelIdx  = _getModelIndex?.Invoke() ?? 0;
                var viArr     = new int   [keys.Count];
                var uiArr     = new int   [keys.Count];
                var beforeArr = new Vector2[keys.Count];
                for (int i = 0; i < keys.Count; i++)
                {
                    viArr[i]     = keys[i].VertexIndex;
                    uiArr[i]     = keys[i].UVIndex;
                    beforeArr[i] = _uvHandleStart[keys[i]];
                }
                SendCmd(new ApplyUVChangesCommand(modelIdx, masterIdx,
                    viArr, uiArr, beforeArr, afterUVs, "UV 回転/拡大縮小"));
            }
            else
            {
                RecordTopologyChange("UV 回転/拡大縮小", obj =>
                {
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var id = keys[i];
                        if (id.VertexIndex < 0 || id.VertexIndex >= obj.VertexCount) continue;
                        var v = obj.Vertices[id.VertexIndex];
                        if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count) v.UVs[id.UVIndex] = afterUVs[i];
                    }
                });
            }

            SetStatus($"UV {keys.Count}頂点を回転/拡大縮小");
            _uvHandleStart.Clear();
        }

        // ================================================================
        // Fit / 選択操作
        // ================================================================

        private void FitToUVBounds()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            Vector2 uvMin = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 uvMax = new Vector2(float.MinValue, float.MinValue);
            bool any = false;
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    var v = mo.Vertices[vi];
                    if (ui < 0 || ui >= v.UVs.Count) continue;
                    uvMin = Vector2.Min(uvMin, v.UVs[ui]);
                    uvMax = Vector2.Max(uvMax, v.UVs[ui]);
                    any   = true;
                }
            }
            if (!any) { _zoom = 1f; _panOffset = Vector2.zero; UpdateCanvasBackground(); _canvas.MarkDirtyRepaint(); return; }

            var rect = _canvas.contentRect;
            if (rect.width < 1) return;
            Vector2 ctr  = (uvMin + uvMax) * 0.5f;
            float ext    = Mathf.Max((uvMax - uvMin).x, (uvMax - uvMin).y);
            if (ext < 0.0001f) ext = 1f;
            float csize  = Mathf.Min(rect.width, rect.height);
            _zoom        = Mathf.Clamp((csize * 0.9f) / (csize * ext), MinZoom, MaxZoom);
            float sz     = csize * _zoom;
            _panOffset   = new Vector2(-(ctr.x - 0.5f) * sz, (ctr.y - 0.5f) * sz);
            UpdateCanvasBackground();
            _canvas.MarkDirtyRepaint();
        }

        private void SelectAll()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    _selected.Add(new UVVertexId(vi, ui));
                }
            }
            UpdateInfo(mo); RefreshAnchorAuto(); _canvas.MarkDirtyRepaint();
        }

        private void ClearSelection()
        {
            if (_selected.Count == 0) return;
            _selected.Clear();
            UpdateInfo(GetMeshObject()); RefreshAnchorAuto(); _canvas.MarkDirtyRepaint();
        }

        // ================================================================
        // 一括変換
        // ================================================================

        private void ApplyTransform()
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            float mu  = _moveU?.value    ?? 0f;
            float mv  = _moveV?.value    ?? 0f;
            float su  = _scaleU?.value   ?? 1f;
            float sv  = _scaleV?.value   ?? 1f;
            float deg = _rotateDeg?.value ?? 0f;
            float saDeg = _scaleAxisDeg?.value ?? 0f;

            if (Mathf.Approximately(mu, 0f) && Mathf.Approximately(mv, 0f) &&
                Mathf.Approximately(su, 1f) && Mathf.Approximately(sv, 1f) &&
                Mathf.Approximately(deg, 0f)) { SetStatus("変換パラメータが初期値です"); return; }

            var targets = _selected.Count > 0 ? new HashSet<UVVertexId>(_selected) : CollectAllUVVertices(mo);
            RefreshAnchorAuto();          // 自動モードなら重心に更新
            var pivot   = _anchor;        // 可変アンカーを基準に使用

            // 重みマップ（選択=1、マグネット影響=weight、選択なし=全点1）
            var weights = new Dictionary<UVVertexId, float>();
            if (_selected.Count > 0)
            {
                var selPos = new List<Vector2>();
                foreach (var id in _selected)
                {
                    weights[id] = 1f;
                    if (id.VertexIndex >= 0 && id.VertexIndex < mo.VertexCount)
                    {
                        var vv = mo.Vertices[id.VertexIndex];
                        if (id.UVIndex >= 0 && id.UVIndex < vv.UVs.Count) selPos.Add(vv.UVs[id.UVIndex]);
                    }
                }
                if (_uvMagnet.Enabled && selPos.Count > 0)
                {
                    foreach (var id in CollectAllUVVertices(mo))
                    {
                        if (weights.ContainsKey(id)) continue;
                        if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                        var vv = mo.Vertices[id.VertexIndex];
                        if (id.UVIndex < 0 || id.UVIndex >= vv.UVs.Count) continue;
                        float wt = _uvMagnet.WeightFor(vv.UVs[id.UVIndex], selPos);
                        if (wt > 0f) weights[id] = wt;
                    }
                }
            }
            else
            {
                foreach (var id in targets) weights[id] = 1f;
            }
            targets = new HashSet<UVVertexId>(weights.Keys);

            if (_panelContext != null)
            {
                // コマンド経由：before/after を収集してから送信
                var mc    = GetMeshContext();
                var model = GetModel?.Invoke();
                int masterIdx = mc != null && model != null ? model.IndexOf(mc) : 0;
                int modelIdx  = _getModelIndex?.Invoke() ?? 0;

                var keyList   = new List<UVVertexId>(targets);
                var beforeArr = new Vector2[keyList.Count];
                var afterArr  = new Vector2[keyList.Count];
                var viArr     = new int   [keyList.Count];
                var uiArr     = new int   [keyList.Count];

                for (int i = 0; i < keyList.Count; i++)
                {
                    var id = keyList[i];
                    viArr[i] = id.VertexIndex;
                    uiArr[i] = id.UVIndex;
                    if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) { beforeArr[i] = afterArr[i] = Vector2.zero; continue; }
                    var v = mo.Vertices[id.VertexIndex];
                    beforeArr[i] = (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count) ? v.UVs[id.UVIndex] : Vector2.zero;
                }

                // after を計算（実際には MeshObject に書き込まない）
                var tempAfter = new Dictionary<UVVertexId, Vector2>();
                ApplyUVTransformToDict(mo, weights, mu, mv, su, sv, deg, pivot, saDeg, tempAfter);
                for (int i = 0; i < keyList.Count; i++)
                    afterArr[i] = tempAfter.TryGetValue(keyList[i], out var uv) ? uv : beforeArr[i];

                SendCmd(new ApplyUVChangesCommand(modelIdx, masterIdx,
                    viArr, uiArr, beforeArr, afterArr, "UV Transform"));
            }
            else
            {
                RecordTopologyChange("UV Transform", obj =>
                    ApplyUVTransform(obj, weights, mu, mv, su, sv, deg, pivot, saDeg));
            }

            SetStatus("UV変換を適用しました");
            _canvas.MarkDirtyRepaint();
        }

        private void ResetParams()
        {
            if (_moveU    != null) _moveU.value     = 0f;
            if (_moveV    != null) _moveV.value     = 0f;
            if (_scaleU   != null) _scaleU.value    = 1f;
            if (_scaleV   != null) _scaleV.value    = 1f;
            if (_rotateDeg!= null) _rotateDeg.value = 0f;
            if (_scaleAxisDeg != null) _scaleAxisDeg.value = 0f;
        }

        private static HashSet<UVVertexId> CollectAllUVVertices(MeshObject mo)
        {
            var r = new HashSet<UVVertexId>();
            foreach (var face in mo.Faces)
            {
                if (face == null) continue;
                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    int ui = ci < face.UVIndices.Count ? face.UVIndices[ci] : 0;
                    r.Add(new UVVertexId(vi, ui));
                }
            }
            return r;
        }

        private static Vector2 ComputeUVPivot(MeshObject mo, HashSet<UVVertexId> targets)
        {
            Vector2 sum = Vector2.zero; int cnt = 0;
            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex >= 0 && id.UVIndex < v.UVs.Count)
                { sum += v.UVs[id.UVIndex]; cnt++; }
            }
            return cnt > 0 ? sum / cnt : new Vector2(0.5f, 0.5f);
        }

        // ================================================================
        // 回転/拡大縮小アンカー
        // ================================================================

        private enum AnchorPreset { Centroid, Center, TopLeft, BottomLeft }

        private VisualElement BuildAnchorRow(string label, float val,
            out Slider slider, out FloatField field, Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lb = new Label(label + ":"); lb.style.width = 16; lb.style.fontSize = 10;
            lb.style.color = new StyleColor(Color.white);
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;

            var sl = new Slider(0f, 1f) { value = Mathf.Clamp01(val) };
            sl.style.flexGrow = 1; sl.style.marginRight = 3;
            var ff = new FloatField { value = val };
            ff.style.width = 52;

            sl.RegisterValueChangedCallback(e => { if (!_anchorSuppress) onChange(e.newValue); });
            ff.RegisterValueChangedCallback(e => { if (!_anchorSuppress) onChange(e.newValue); });

            row.Add(lb); row.Add(sl); row.Add(ff);
            slider = sl; field = ff;
            return row;
        }

        private void SetAnchorMode(bool on)
        {
            _anchorMode = on;
            if (on) RefreshAnchorAuto();  // 入る時点の重心を初期表示
            RefreshAnchorModeUI();
            _canvas?.MarkDirtyRepaint();
        }

        private void RefreshAnchorModeUI()
        {
            if (_anchorEnterBtn != null) _anchorEnterBtn.style.display = _anchorMode ? DisplayStyle.None : DisplayStyle.Flex;
            if (_anchorPanel    != null) _anchorPanel.style.display    = _anchorMode ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>アンカー数値フィールド/スライダーを _anchor に同期（通知抑制）。</summary>
        private void RefreshAnchorFields()
        {
            _anchorSuppress = true;
            _anchorXSlider?.SetValueWithoutNotify(Mathf.Clamp01(_anchor.x));
            _anchorYSlider?.SetValueWithoutNotify(Mathf.Clamp01(_anchor.y));
            _anchorXField?.SetValueWithoutNotify(_anchor.x);
            _anchorYField?.SetValueWithoutNotify(_anchor.y);
            _anchorSuppress = false;
        }

        /// <summary>手動固定でなければ選択の重心へ追従。</summary>
        private void RefreshAnchorAuto()
        {
            if (_anchorManual) return;
            var mo = GetMeshObject();
            if (mo == null) return;
            var targets = _selected.Count > 0 ? _selected : CollectAllUVVertices(mo);
            _anchor = ComputeUVPivot(mo, targets);
            RefreshAnchorFields();
        }

        private void SetAnchorComponent(bool isX, float v)
        {
            if (isX) _anchor.x = v; else _anchor.y = v;
            _anchorManual = true;
            RefreshAnchorFields();
            _canvas?.MarkDirtyRepaint();
        }

        private void ApplyAnchorPreset(AnchorPreset preset)
        {
            var mo = GetMeshObject();
            if (mo == null) return;
            var targets = _selected.Count > 0 ? _selected : CollectAllUVVertices(mo);

            if (preset == AnchorPreset.Centroid)
            {
                _anchor = ComputeUVPivot(mo, targets);
                _anchorManual = false;   // 重心＝自動追従に戻す
                RefreshAnchorFields();
                _canvas?.MarkDirtyRepaint();
                return;
            }

            // バウンディングから中心/左上/左下
            bool any = false;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var id in targets)
            {
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var vv = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= vv.UVs.Count) continue;
                var uv = vv.UVs[id.UVIndex];
                minU = Mathf.Min(minU, uv.x); maxU = Mathf.Max(maxU, uv.x);
                minV = Mathf.Min(minV, uv.y); maxV = Mathf.Max(maxV, uv.y);
                any = true;
            }
            if (!any) return;

            switch (preset)
            {
                case AnchorPreset.Center:     _anchor = new Vector2((minU + maxU) * 0.5f, (minV + maxV) * 0.5f); break;
                case AnchorPreset.TopLeft:    _anchor = new Vector2(minU, maxV); break;  // UV上=大きいV
                case AnchorPreset.BottomLeft: _anchor = new Vector2(minU, minV); break;
            }
            _anchorManual = true;
            RefreshAnchorFields();
            _canvas?.MarkDirtyRepaint();
        }

        private static void ApplyUVTransform(MeshObject mo, Dictionary<UVVertexId, float> weights,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot, float saDeg)
        {
            float saRad = saDeg * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);
            foreach (var kv in weights)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                v.UVs[id.UVIndex] = CalcTransformedUV(v.UVs[id.UVIndex], pivot, mu, mv, su, sv, deg, saCos, saSin, kv.Value);
            }
        }

        /// <summary>before/after を計算するだけで MeshObject を変更しない。コマンド化用。</summary>
        private static void ApplyUVTransformToDict(MeshObject mo, Dictionary<UVVertexId, float> weights,
            float mu, float mv, float su, float sv, float deg, Vector2 pivot, float saDeg,
            Dictionary<UVVertexId, Vector2> result)
        {
            float saRad = saDeg * Mathf.Deg2Rad;
            float saCos = Mathf.Cos(saRad), saSin = Mathf.Sin(saRad);
            foreach (var kv in weights)
            {
                var id = kv.Key;
                if (id.VertexIndex < 0 || id.VertexIndex >= mo.VertexCount) continue;
                var v = mo.Vertices[id.VertexIndex];
                if (id.UVIndex < 0 || id.UVIndex >= v.UVs.Count) continue;
                result[id] = CalcTransformedUV(v.UVs[id.UVIndex], pivot, mu, mv, su, sv, deg, saCos, saSin, kv.Value);
            }
        }

        private static Vector2 CalcTransformedUV(Vector2 uv, Vector2 pivot,
            float mu, float mv, float su, float sv, float deg, float saCos, float saSin, float w)
        {
            float suw = 1f + (su - 1f) * w, svw = 1f + (sv - 1f) * w;
            float degw = deg * w * Mathf.Deg2Rad;
            float cos = Mathf.Cos(degw), sin = Mathf.Sin(degw);
            float dx = uv.x - pivot.x, dy = uv.y - pivot.y;
            // スケール軸フレームへ回転(-φ) → 重み付きスケール → 戻す(+φ)
            float rx =  dx * saCos + dy * saSin;
            float ry = -dx * saSin + dy * saCos;
            rx *= suw; ry *= svw;
            dx = rx * saCos - ry * saSin;
            dy = rx * saSin + ry * saCos;
            // 重み付き全体回転
            float ex = dx * cos - dy * sin;
            float ey = dx * sin + dy * cos;
            uv.x = pivot.x + ex + mu * w;
            uv.y = pivot.y + ey + mv * w;
            return uv;
        }
    }
}
