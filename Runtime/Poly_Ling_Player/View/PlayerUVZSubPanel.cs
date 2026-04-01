// PlayerUVZSubPanel.cs
// UVZPanel の Player 版サブパネル。
// UXML/SceneView 依存を排除し、カメラ方向は ToolContext から取得する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerUVZSubPanel
    {
        // ── コールバック ──────────────────────────────────────────────────
        public Func<ModelContext>   GetModel;
        public Action<PanelCommand> SendCommand;
        public Func<int>            GetModelIndex;
        /// <summary>現在のカメラ位置を返す。</summary>
        public Func<Vector3>        GetCameraPosition;
        /// <summary>現在のカメラ前向きを返す。</summary>
        public Func<Vector3>        GetCameraForward;

        // ── 設定値 ────────────────────────────────────────────────────────
        private float _uvScale    = 10f;
        private float _depthScale = 1f;

        // ── UI ────────────────────────────────────────────────────────────
        private Label          _warningLabel;
        private Label          _targetInfo;
        private DropdownField  _writebackTarget;
        private Label          _statusLabel;

        // 書き戻し候補: (masterIndex, label)
        private readonly List<(int, string)> _writebackCandidates = new List<(int, string)>();

        // ── Build ─────────────────────────────────────────────────────────
        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _targetInfo = new Label();
            _targetInfo.style.fontSize    = 10;
            _targetInfo.style.marginBottom = 4;
            root.Add(_targetInfo);

            // スケール
            root.Add(SecLabel("スケール"));
            root.Add(MakeFloatRow("UV スケール", 10f, v => _uvScale = Mathf.Max(0.001f, v)));
            root.Add(MakeFloatRow("深度スケール", 1f, v => _depthScale = Mathf.Max(0.001f, v)));

            // UV → XYZ
            root.Add(SecLabel("UV → XYZ（展開）"));
            root.Add(new HelpBox("選択メッシュのUV(u,v)をXY、カメラ深度をZとする新メッシュを生成", HelpBoxMessageType.None));
            var uvToXyzBtn = new Button(OnUvToXyz) { text = "UVZ メッシュ生成" };
            uvToXyzBtn.style.height = 28; uvToXyzBtn.style.marginTop = 4; uvToXyzBtn.style.marginBottom = 8;
            root.Add(uvToXyzBtn);

            // XYZ → UV
            root.Add(SecLabel("XYZ → UV（書き戻し）"));
            root.Add(new HelpBox("選択メッシュ(UVZ)のXY座標をターゲットのUVとして書き戻す", HelpBoxMessageType.None));

            _writebackTarget = new DropdownField("ターゲット", new List<string> { "(なし)" }, 0);
            _writebackTarget.style.marginBottom = 4;
            root.Add(_writebackTarget);

            var xyzToUvBtn = new Button(OnXyzToUv) { text = "XYZ→UV 書き戻し" };
            xyzToUvBtn.style.height = 28; xyzToUvBtn.style.marginBottom = 4;
            root.Add(xyzToUvBtn);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            root.Add(_statusLabel);
        }

        // ── Refresh ───────────────────────────────────────────────────────
        public void Refresh()
        {
            var model = GetModel?.Invoke();
            if (_warningLabel == null) return;

            if (model == null)
            {
                _warningLabel.text          = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _targetInfo.text            = "";
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            var mc = model.FirstSelectedDrawableMesh;
            _targetInfo.text = mc != null ? $"対象: {mc.Name ?? "?"}" : "メッシュ未選択";

            // 書き戻し候補を再構築
            _writebackCandidates.Clear();
            foreach (var entry in model.DrawableMeshes)
            {
                var drawMc = model.GetMeshContext(entry.MasterIndex);
                string name = drawMc?.Name ?? $"[{entry.MasterIndex}]";
                _writebackCandidates.Add((entry.MasterIndex, $"[{entry.MasterIndex}] {name}"));
            }

            var choices = new List<string> { "(なし)" };
            foreach (var (_, label) in _writebackCandidates) choices.Add(label);
            if (_writebackTarget != null)
            {
                _writebackTarget.choices = choices;
                if (_writebackTarget.index >= choices.Count) _writebackTarget.index = 0;
            }
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnUvToXyz()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルなし"); return; }
            var mc = model.FirstSelectedDrawableMesh;
            if (mc == null) { SetStatus("メッシュが選択されていません"); return; }
            int masterIdx = model.IndexOf(mc);
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            Vector3 camPos = GetCameraPosition?.Invoke() ?? Vector3.zero;
            Vector3 camFwd = GetCameraForward?.Invoke()  ?? Vector3.forward;
            SendCommand?.Invoke(new UvToXyzCommand(modelIdx, masterIdx, _uvScale, _depthScale, camPos, camFwd));
            SetStatus($"UV→XYZ コマンド送信: '{mc.Name}'");
        }

        private void OnXyzToUv()
        {
            var model = GetModel?.Invoke();
            if (model == null) { SetStatus("モデルなし"); return; }
            var mc = model.FirstSelectedDrawableMesh;
            if (mc == null) { SetStatus("ソースメッシュが選択されていません"); return; }
            int masterIdx = model.IndexOf(mc);
            int targetIdx = GetWritebackTargetIndex();
            if (targetIdx < 0) { SetStatus("書き戻し先が選択されていません"); return; }
            int modelIdx = GetModelIndex?.Invoke() ?? 0;
            SendCommand?.Invoke(new XyzToUvCommand(modelIdx, masterIdx, targetIdx, _uvScale));
            SetStatus($"XYZ→UV コマンド送信 → [{targetIdx}]");
        }

        private int GetWritebackTargetIndex()
        {
            if (_writebackTarget == null) return -1;
            string val = _writebackTarget.value;
            if (string.IsNullOrEmpty(val) || val == "(なし)") return -1;
            int bracketEnd = val.IndexOf(']');
            if (bracketEnd < 2) return -1;
            if (int.TryParse(val.Substring(1, bracketEnd - 1), out int idx)) return idx;
            return -1;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color    = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeFloatRow(string label, float initVal, Action<float> onChange)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;
            var lbl = new Label(label); lbl.style.width = 80; lbl.style.fontSize = 10;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            var f = new FloatField { value = initVal }; f.style.flexGrow = 1;
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            row.Add(lbl); row.Add(f);
            return row;
        }
    }
}
