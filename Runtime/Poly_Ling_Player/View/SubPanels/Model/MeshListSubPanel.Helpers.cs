// MeshListSubPanel.Helpers.cs
// オブジェクトリストのサブパネル：ヘルパーと UI パーツ生成。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;
using PlayerIoUiKit        = Poly_Ling.Player.PlayerIoUiKit;
using PlayerUiPrefs        = Poly_Ling.Player.PlayerUiPrefs;
using ObjectMoveSettings   = Poly_Ling.Tools.ObjectMoveSettings;
using ParameterLimits      = Poly_Ling.Core.ParameterLimits;
using RecentPaths          = Poly_Ling.Core.RecentPaths;
using PartsDictionaryPath  = Poly_Ling.Core.PartsDictionaryPath;
using MeshRenameCsvHelper  = Poly_Ling.UI.MeshRenameCsvHelper;

namespace Poly_Ling.MeshListV2
{
    public partial class MeshListSubPanel
    {
        // ================================================================
        // ヘルパー（エディタ版と同一）
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private int[] SelIndices() => _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
        private int[] SelTransformIndices() => _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

        private void RebuildSelectedAdaptersFromTreeView()
        {
            _selectedAdapters.Clear();
            if (_treeView == null) return;
            foreach (var item in _treeView.selectedItems)
                if (item is SummaryTreeAdapter a && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);
        }

        private void RebuildSelectedAdaptersFromCurrentModel()
        {
            _selectedAdapters.Clear();
            if (_treeRoot == null || CurrentModel == null) return;
            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
            };
            if (sel == null) return;
            foreach (int idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a != null && !a.IsSelectionBlocked)
                    _selectedAdapters.Add(a);
            }
        }

        /// <summary>
        /// _selectedAdapters の IMeshView を CurrentModel から最新スナップショットで更新する。
        /// BonePoseData 等が後から変化した場合に HasPose 等を正しく反映するため。
        /// </summary>
        private void RefreshSelectedAdapterViews()
        {
            if (CurrentModel == null || _selectedAdapters.Count == 0) return;
            var freshList = _currentTab == TabType.Bone
                ? CurrentModel.BoneList
                : CurrentModel.DrawableList;
            if (freshList == null) return;
            var freshMap = new System.Collections.Generic.Dictionary<int, IMeshView>();
            foreach (var v in freshList) freshMap[v.MasterIndex] = v;
            foreach (var a in _selectedAdapters)
                if (freshMap.TryGetValue(a.MasterIndex, out var fresh))
                    a.UpdateView(fresh);
        }

        // ツリー内の全アダプタのビュー（名前等）を現在のモデルから最新化する。
        // 名前変更等の属性変更を即座にリストへ反映するため。
        private void RefreshAllAdapterViews()
        {
            if (CurrentModel == null || _treeRoot == null) return;
            var freshList = _currentTab == TabType.Bone
                ? CurrentModel.BoneList
                : CurrentModel.DrawableList;
            if (freshList == null) return;
            foreach (var v in freshList)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(v.MasterIndex);
                if (a != null) a.UpdateView(v);
            }
        }

        private string FindDrawableName(int mi)
        {
            if (CurrentModel?.DrawableList != null) foreach (var s in CurrentModel.DrawableList) if (s.MasterIndex == mi) return s.Name;
            if (CurrentModel?.BoneList     != null) foreach (var s in CurrentModel.BoneList)     if (s.MasterIndex == mi) return s.Name;
            return null;
        }

        private void SL(Label l, string t)    { if (l != null) l.text = t; }
        private void Log(string m)            { if (_statusLabel != null) _statusLabel.text = m; }
        private void ML(string m)             { if (_morphStatusLabel != null) _morphStatusLabel.text = m; Log(m); }

        private static float NormAngle(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }
        private static void SMV(Toggle t, bool m) { if (t != null) t.showMixedValue = m; }

        private void MixFT(FloatField f, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.0001f);
            f.SetValueWithoutNotify(same ? (float)System.Math.Round(v0, 4) : 0f);
            f.showMixedValue = !same; f.SetEnabled(true);
        }

        private void MixRTF(FloatField f, Slider s, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.01f);
            float val = same ? v0 : 0f;
            f.SetValueWithoutNotify((float)System.Math.Round(val, 4)); f.showMixedValue = !same; f.SetEnabled(true);
            if (s != null) { s.SetValueWithoutNotify(same ? NormAngle(val) : 0f); s.SetEnabled(same); }
        }

        private static void SF(FloatField f, float v, bool e) { if (f == null) return; f.SetValueWithoutNotify((float)System.Math.Round(v, 4)); f.showMixedValue = false; f.SetEnabled(e); }
        private static void SS(Slider s, float v, bool e)     { if (s != null) { s.SetValueWithoutNotify(v); s.SetEnabled(e); } }

        // ================================================================
        // UIパーツ生成ヘルパー
        // ================================================================

        private T Q<T>(string name) where T : VisualElement => _root?.Q<T>(name);

        private static Button MakeTabBtn(string label, string name)
        {
            var b = new Button { text = label, name = name };
            b.style.flexGrow = 1; b.style.height = 20; b.style.marginRight = 2; b.style.fontSize = 10;
            return b;
        }

        private static Button MakeSmallBtn(string label, string name, string tooltip = null)
        {
            var b = new Button { text = label, name = name };
            b.style.height = 18; b.style.marginRight = 2; b.style.marginBottom = 2; b.style.fontSize = 10;
            b.style.paddingLeft = 4; b.style.paddingRight = 4; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            if (!string.IsNullOrEmpty(tooltip)) b.tooltip = tooltip;
            return b;
        }

        private static Label MakeInfoLabel(string name = "")
        {
            var l = new Label { name = name };
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 1;
            return l;
        }

        private static Label SectionHeader(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 1;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        // ツリー/リストの下端ドラッグリサイズ用ハンドル。
        // PlayerPrimitiveMeshSubPanel.AddProfileResizeHandle と同方式:
        // 6px バーを PointerDown/Move/Up + CapturePointer でドラッグし、Mathf.Clamp で高さ変更。
        private static void AddListResizeHandle(
            VisualElement container, VisualElement target,
            Func<float> getHeight, Action<float> setHeight,
            float min)
        {
            var handle = new VisualElement();
            handle.style.width           = new StyleLength(new Length(100, LengthUnit.Percent));
            handle.style.height          = 6;
            handle.style.marginTop       = 2;
            handle.style.marginBottom    = 4;
            handle.style.backgroundColor = new StyleColor(new Color(0.30f, 0.30f, 0.36f));
            handle.pickingMode           = PickingMode.Position;

            bool  dragging    = false;
            float startY      = 0f;
            float startHeight = 0f;

            handle.RegisterCallback<PointerDownEvent>(e =>
            {
                handle.CapturePointer(e.pointerId);
                dragging    = true;
                startY      = e.position.y;
                startHeight = getHeight();
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!dragging || !handle.HasPointerCapture(e.pointerId)) return;
                float delta = e.position.y - startY;
                float h = Mathf.Max(min, startHeight + delta);   // 上限なし
                setHeight(h);
                // TreeView は height を無視するため min/max も同値にして高さを厳密固定する。
                target.style.height    = h;
                target.style.minHeight = h;
                target.style.maxHeight = h;
                e.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!handle.HasPointerCapture(e.pointerId)) return;
                handle.ReleasePointer(e.pointerId);
                dragging = false;
                e.StopPropagation();
            });

            container.Add(handle);
        }

        private static VisualElement LabeledRow(string label, VisualElement content)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2; row.style.alignItems = Align.Center;
            var lbl = new Label(label); lbl.style.width = 70; lbl.style.fontSize = 10;
            lbl.style.color = new StyleColor(Color.white);
            row.Add(lbl); content.style.flexGrow = 1; row.Add(content);
            return row;
        }

        private static StyleColor Col(float v) => new StyleColor(new Color(v, v, v));

        private static void AddXYZFields(VisualElement parent, out FloatField fx, out FloatField fy, out FloatField fz, string prefix)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            fx = new FloatField("X") { name = $"{prefix}-x" }; fx.style.flexGrow = 1;
            fy = new FloatField("Y") { name = $"{prefix}-y" }; fy.style.flexGrow = 1;
            fz = new FloatField("Z") { name = $"{prefix}-z" }; fz.style.flexGrow = 1;
            row.Add(fx); row.Add(fy); row.Add(fz); parent.Add(row);
        }

        private void AddRotFields(
            VisualElement parent,
            out FloatField fx, out Slider sx, SetBoneTransformValueCommand.Field tfx,
            out FloatField fy, out Slider sy, SetBoneTransformValueCommand.Field tfy,
            out FloatField fz, out Slider sz, SetBoneTransformValueCommand.Field tfz,
            bool isPose)
        {
            var frow = new VisualElement(); frow.style.flexDirection = FlexDirection.Row; frow.style.marginBottom = 1;
            fx = new FloatField("X"); fx.style.flexGrow = 1;
            fy = new FloatField("Y"); fy.style.flexGrow = 1;
            fz = new FloatField("Z"); fz.style.flexGrow = 1;
            frow.Add(fx); frow.Add(fy); frow.Add(fz); parent.Add(frow);

            var srow = new VisualElement(); srow.style.flexDirection = FlexDirection.Row; srow.style.marginBottom = 2;
            sx = new Slider(-180f, 180f); sx.style.flexGrow = 1;
            sx.style.color = new StyleColor(Color.white);
            sy = new Slider(-180f, 180f); sy.style.flexGrow = 1;
            sy.style.color = new StyleColor(Color.white);
            sz = new Slider(-180f, 180f); sz.style.flexGrow = 1;
            sz.style.color = new StyleColor(Color.white);
            srow.Add(sx); srow.Add(sy); srow.Add(sz); parent.Add(srow);

            if (isPose)
            {
                RegRestRotField(fx, sx, tfx); RegRestRotField(fy, sy, tfy); RegRestRotField(fz, sz, tfz);
                RegRestRotSlider(sx, fx, tfx); RegRestRotSlider(sy, fy, tfy); RegRestRotSlider(sz, fz, tfz);
            }
            else
            {
                RegTRotField(fx, sx, tfx); RegTRotField(fy, sy, tfy); RegTRotField(fz, sz, tfz);
                RegTRotSlider(sx, fx, tfx); RegTRotSlider(sy, fy, tfy); RegTRotSlider(sz, fz, tfz);
            }
        }
    }
}
