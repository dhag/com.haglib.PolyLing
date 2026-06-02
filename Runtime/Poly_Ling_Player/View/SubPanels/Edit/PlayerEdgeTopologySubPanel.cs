// PlayerEdgeTopologySubPanel.cs
// EdgeTopologyToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerEdgeTopologySubPanel
    {
        public Func<EdgeTopologyToolHandler> GetH;
        /// <summary>
        /// サブモード (Flip/Split/Dissolve) 変更通知。ViewerCore 側で Selection.Mode を
        /// 切り替えてホバー有効範囲 (頂点/辺) を調整するために使われる。
        ///
        /// 【設計ポイント: サブパネルから ViewerCore への責務分離】
        /// サブパネルは UI ウィジェット組立てのみの責務とし、Selection / InteractionMode /
        /// Overlay 等への直接アクセスは禁じる (循環参照と責務混在を避けるため)。
        /// サブパネル内で発火したモード変更イベントは Action コールバックで ViewerCore 側に
        /// 通知し、そこから Selection.Mode 等の全体状態を触る。
        /// 同様の構造を他サブパネルでも採用すること (例: Flip/Dissolve 以外に新モードを
        /// 追加する場合は OnModeChanged の型引数だけ拡張すれば足りる)。
        /// </summary>
        public Action<EdgeTopoMode> OnModeChanged;
        private VisualElement _root;
        private HelpBox       _help;

        private static readonly string[] HelpTexts =
        {
            "共有辺をクリックして対角線を入れ替え（2つの三角形が必要）",
            "四角形の対角頂点を順にクリックして分割",
            "共有辺をクリックして2つの面を結合",
        };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Edge Topology"));
            var modeChoices = new System.Collections.Generic.List<string> { "Flip", "Split", "Dissolve" };
            var modeValues = new[] { EdgeTopoMode.Flip, EdgeTopoMode.Split, EdgeTopoMode.Dissolve };
            var modeDD = new DropdownField("Mode", modeChoices, 0);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e => {
                int idx = modeChoices.IndexOf(e.newValue);
                if (idx >= 0 && GetH() != null) GetH().ModePublic = modeValues[idx];
                UpdateHelp(idx);
                if (idx >= 0) OnModeChanged?.Invoke(modeValues[idx]);
            });
            _root.Add(modeDD);
            _help = new HelpBox(HelpTexts[0], HelpBoxMessageType.Info);
            _help.style.color = new StyleColor(Color.white);
            _help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(_help);
        }

        private void UpdateHelp(int idx)
        {
            if (_help == null) return;
            if (idx < 0 || idx >= HelpTexts.Length) return;
            _help.text = HelpTexts[idx];
        }

        public void Refresh() {}

        // ── ヘルパー ──────────────────────────────────────────────────────

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop = 4; l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 2;
            return l;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static SliderInt MakeIntSlider(string label, int min, int max, int init, Action<int> onChange)
        {
            var s = new SliderInt(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
