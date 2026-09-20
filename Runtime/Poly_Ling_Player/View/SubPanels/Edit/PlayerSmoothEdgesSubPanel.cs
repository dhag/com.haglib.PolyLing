// PlayerSmoothEdgesSubPanel.cs
// SmoothEdgesTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerSmoothEdgesSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public IToolSurface                 Surface;
        public Func<Poly_Ling.View.IProjectView>         GetView;
        public Action<PanelCommand>         SendCommand;

        private const string Tool = "smoothEdges";

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null（呼び出し側が送信を止める）。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            int idx = GetView?.Invoke()?.CurrentModel?.ActiveMeshIndex ?? -1;
            return idx >= 0 ? new[] { idx } : null;
        }

        // ================================================================
        // UI 要素
        // ================================================================

        // UI 自動操作の ID は "smoothEdges.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("stats.segments", Safety = UiSafety.ReadOnly, Description = "選択中の辺・線分の本数とチェーン頂点数")]
        private Label         _segmentLabel;
        [UiControl("stats.vertices", Safety = UiSafety.ReadOnly, Description = "端点の数と移動対象の頂点数")]
        private Label         _vertexLabel;
        [UiControl("strength", Description = "1 反復あたり隣接平均へ寄せる量。0 で変化なし")]
        private Slider        _strengthSlider;
        [UiControl("iterations", Description = "反復回数")]
        private SliderInt     _iterationsSlider;
        [UiControl("fixEndpoints", Description = "選択チェーンの開始点・終了点を動かさない")]
        private Toggle        _fixEndpointsToggle;
        [UiControl("lock.x", Description = "X 方向へは動かさない")]
        private Toggle        _lockX;
        [UiControl("lock.y", Description = "Y 方向へは動かさない")]
        private Toggle        _lockY;
        [UiControl("lock.z", Description = "Z 方向へは動かさない")]
        private Toggle        _lockZ;
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "平滑化を実行する（Undo できる）。辺か線分の選択が要る")]
        private Button        _smoothBtn;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Smooth Edges / 辺を滑らかに"));
            _root.Add(new HelpBox(
                "選択した辺・線分に沿って頂点を滑らかにします。隣接は選択したチェーンだけを辿ります。",
                HelpBoxMessageType.Info));

            // 統計
            _segmentLabel = InfoLabel();
            _root.Add(_segmentLabel);

            _vertexLabel = InfoLabel();
            _root.Add(_vertexLabel);

            // 強度
            float sMin = ParameterLimits.GetF("SmoothEdges.Strength.Min");
            float sMax = ParameterLimits.GetF("SmoothEdges.Strength.Max");
            _strengthSlider = new Slider("強度", sMin, sMax) { value = 0.5f };
            _strengthSlider.style.marginBottom = 3;
            _strengthSlider.tooltip = "1 反復あたり隣接平均へ寄せる量。0 で変化なし。";
            _strengthSlider.RegisterValueChangedCallback(e => Surface.Set(Tool, "strength", e.newValue));
            _root.Add(_strengthSlider);

            // 反復回数
            int iMin = ParameterLimits.GetI("SmoothEdges.Iterations.Min");
            int iMax = ParameterLimits.GetI("SmoothEdges.Iterations.Max");
            _iterationsSlider = new SliderInt("反復回数", iMin, iMax) { value = 1 };
            _iterationsSlider.style.marginBottom = 3;
            _iterationsSlider.RegisterValueChangedCallback(e => Surface.Set(Tool, "iterations", e.newValue));
            _root.Add(_iterationsSlider);

            // 端点固定
            _fixEndpointsToggle = new Toggle("開始点・終了点を固定") { value = true };
            _fixEndpointsToggle.style.marginBottom = 3;
            _fixEndpointsToggle.tooltip =
                "選択チェーン内で次数1の頂点を動かしません。閉ループには端点が無いため影響しません。";
            _fixEndpointsToggle.RegisterValueChangedCallback(e =>
            {
                if (Surface == null) return;
                Surface.Set(Tool, "fixEndpoints", e.newValue);
                Surface.Invoke(Tool, "refreshStats");
                UpdateStats();
            });
            _root.Add(_fixEndpointsToggle);

            // 軸ロック
            _root.Add(SmallHeader("軸ロック:"));
            var lockRow = new VisualElement();
            lockRow.style.flexDirection = FlexDirection.Row;
            lockRow.style.marginBottom  = 4;

            _lockX = MakeToggle("X", v => Surface.Set(Tool, "lockX", v));
            _lockY = MakeToggle("Y", v => Surface.Set(Tool, "lockY", v));
            _lockZ = MakeToggle("Z", v => Surface.Set(Tool, "lockZ", v));
            lockRow.Add(_lockX);
            lockRow.Add(_lockY);
            lockRow.Add(_lockZ);
            _root.Add(lockRow);

            // 実行
            _smoothBtn = new Button(() =>
            {
                var targets = ActiveMasterIndices();
                if (Surface == null || targets == null) return;

                SendCommand?.Invoke(new SmoothEdgesCommand(
                    ModelIndex, targets,
                    Surface.GetFloat(Tool, "strength"), Surface.GetInt(Tool, "iterations"),
                    Surface.GetBool(Tool, "fixEndpoints"),
                    Surface.GetBool(Tool, "lockX"), Surface.GetBool(Tool, "lockY"), Surface.GetBool(Tool, "lockZ")));
                Refresh();
            })
            { text = "平滑化実行" };
            _smoothBtn.style.height    = 30;
            _smoothBtn.style.marginTop = 6;
            _root.Add(_smoothBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (Surface == null) return;

            Surface.Invoke(Tool, "refreshStats");

            _strengthSlider?.SetValueWithoutNotify(Surface.GetFloat(Tool, "strength"));
            _iterationsSlider?.SetValueWithoutNotify(Surface.GetInt(Tool, "iterations"));
            _fixEndpointsToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "fixEndpoints"));
            _lockX?.SetValueWithoutNotify(Surface.GetBool(Tool, "lockX"));
            _lockY?.SetValueWithoutNotify(Surface.GetBool(Tool, "lockY"));
            _lockZ?.SetValueWithoutNotify(Surface.GetBool(Tool, "lockZ"));

            UpdateStats();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateStats()
        {
            if (Surface == null) return;
            int segments = Surface.GetInt(Tool, "segmentCount");
            int movable  = Surface.GetInt(Tool, "movableVertexCount");

            if (!Surface.GetBool(Tool, "statsCalculated") || segments == 0)
            {
                if (_segmentLabel != null) _segmentLabel.text = "辺または線分を選択してください";
                if (_vertexLabel  != null) _vertexLabel.text  = "";
                _smoothBtn?.SetEnabled(false);
                return;
            }

            if (_segmentLabel != null)
                _segmentLabel.text = $"辺・線分: {segments} 本  /  チェーン頂点: {Surface.GetInt(Tool, "chainVertexCount")}";

            if (_vertexLabel != null)
                _vertexLabel.text = $"端点: {Surface.GetInt(Tool, "endpointCount")}  /  移動対象: {movable} 頂点";

            _smoothBtn?.SetEnabled(movable > 0);
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Toggle MakeToggle(string label, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = false };
            t.style.marginRight = 8;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }
    }
}
