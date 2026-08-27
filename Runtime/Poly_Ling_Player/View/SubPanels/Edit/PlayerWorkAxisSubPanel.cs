// PlayerWorkAxisSubPanel.cs
// 作業用ローカル軸 (WorkAxisContext) のサブパネル。
// 原点 / 回転の数値入力と、サブモード切替・整列コマンドを提供する。
//
// モデルの頂点には一切触れない。読み書きするのは WorkAxisContext だけ。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Serialization;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    /// <summary>作業軸サブパネル。</summary>
    public class PlayerWorkAxisSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>操作対象の作業軸。null なら入力を無視する。</summary>
        public Func<WorkAxisContext> GetWorkAxis;

        /// <summary>ハンドラ（サブモード切替用）。</summary>
        public Func<WorkAxisToolHandler> GetH;

        /// <summary>値が変わったときに呼ぶ。ギズモ再描画に使う。</summary>
        public Action OnValueChanged;

        /// <summary>選択頂点の重心（ワールド座標）。取得できないときは null。</summary>
        public Func<Vector3?> GetSelectionCentroidWorld;

        /// <summary>
        /// 見出しと説明文を出すか。Build の前に設定する。
        /// 他パネルへ埋め込むときは false にして、見出しの重複を避ける。
        /// </summary>
        public bool ShowHeader = true;

        /// <summary>
        /// 名前付き作業軸の辞書。Viewer が用意した1個を全パネルで共有する。
        /// null なら辞書 UI を出さない。
        /// </summary>
        public Func<WorkAxisLibrary> GetLibrary;

        /// <summary>辞書の中身が変わったときに呼ぶ。他パネルの一覧を揃えるため。</summary>
        public Action OnLibraryChanged;

        // ================================================================
        // ウィジェット
        // ================================================================

        private VisualElement _root;
        private FloatField _posX, _posY, _posZ;
        private FloatField _rotX, _rotY, _rotZ;
        private FloatField _lengthField;
        private Toggle     _visibleToggle;
        private FloatField _snapField;
        private Toggle     _snapToggle;
        private Button     _modeMoveBtn, _modeRotateBtn;
        private Label      _infoLabel;

        // 吸着対象
        private Toggle _snapVertexToggle, _snapBoneToggle, _snapObjectToggle;

        // 辞書
        private TextField    _libNameField;
        private DropdownField _libDropdown;
        private readonly List<string> _libNames = new List<string>();

        private static readonly Color ActiveBtnColor   = new Color(0.20f, 0.45f, 0.25f);
        private static readonly Color InactiveBtnColor = new Color(0.25f, 0.25f, 0.25f);

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            if (ShowHeader)
            {
                _root.Add(Header("作業軸 (Work Axis)"));

                var help = new HelpBox(
                    "回転・曲げの基準となるローカル軸です。\n" +
                    "原点はワールド座標で保持され、プロジェクトに保存されます。\n" +
                    "モデルを移動しても軸は追従しません。\n" +
                    "3D画面の六角錐は「長さ」で描かれます（X は 0.5 倍、Z は 0.3 倍）。\n" +
                    "原点ハンドルと Y 先端ハンドルは頂点／ボーンへ重ねると吸着し、\n" +
                    "外せば自由に動きます。\n" +
                    "Y 先端ハンドルは向きだけを変えます（長さは変わりません）。",
                    HelpBoxMessageType.Info);
                help.style.color = new StyleColor(Color.white);
                help.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
                _root.Add(help);
            }

            // ── サブモード切替 ────────────────────────────────────────
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginTop     = 6;
            modeRow.style.marginBottom  = 4;
            _modeMoveBtn   = new Button(() => SetMode(WorkAxisToolHandler.WorkAxisGizmoMode.Move))   { text = "移動" };
            _modeRotateBtn = new Button(() => SetMode(WorkAxisToolHandler.WorkAxisGizmoMode.Rotate)) { text = "回転" };
            _modeMoveBtn.style.flexGrow   = 1; _modeMoveBtn.style.marginRight = 2;
            _modeRotateBtn.style.flexGrow = 1;
            modeRow.Add(_modeMoveBtn); modeRow.Add(_modeRotateBtn);
            _root.Add(modeRow);

            // ── 原点 ─────────────────────────────────────────────────
            _root.Add(Header("原点 (ワールド)"));
            var posRow = new VisualElement();
            posRow.style.flexDirection = FlexDirection.Row;
            posRow.style.marginBottom  = 3;
            _posX = MakeField("X", v => SetOrigin(0, v));
            _posY = MakeField("Y", v => SetOrigin(1, v));
            _posZ = MakeField("Z", v => SetOrigin(2, v));
            posRow.Add(_posX); posRow.Add(_posY); posRow.Add(_posZ);
            _root.Add(posRow);

            // ── 回転 ─────────────────────────────────────────────────
            _root.Add(Header("回転 (オイラー角・度)"));
            var rotRow = new VisualElement();
            rotRow.style.flexDirection = FlexDirection.Row;
            rotRow.style.marginBottom  = 3;
            _rotX = MakeField("X", v => SetEuler(0, v));
            _rotY = MakeField("Y", v => SetEuler(1, v));
            _rotZ = MakeField("Z", v => SetEuler(2, v));
            rotRow.Add(_rotX); rotRow.Add(_rotY); rotRow.Add(_rotZ);
            _root.Add(rotRow);

            // ── 長さ ─────────────────────────────────────────────────
            // 六角錐ギズモのワールド長の基準。Y 軸先端ハンドルの定位置を決める。
            _root.Add(Header("長さ (ワールド)"));
            var lenRow = new VisualElement();
            lenRow.style.flexDirection = FlexDirection.Row;
            lenRow.style.marginBottom  = 3;
            _lengthField = MakeField("L", SetLength);
            lenRow.Add(_lengthField);
            _root.Add(lenRow);

            // ── 回転スナップ ──────────────────────────────────────────
            var snapRow = new VisualElement();
            snapRow.style.flexDirection = FlexDirection.Row;
            snapRow.style.marginBottom  = 3;
            _snapToggle = new Toggle("角度スナップ") { value = false };
            _snapToggle.style.color = new StyleColor(Color.white);
            _snapToggle.RegisterValueChangedCallback(_ => ApplySnapToHandler());
            _snapField = new FloatField { value = 15f };
            _snapField.style.color = new StyleColor(Color.black);
            _snapField.style.width = 50; _snapField.style.marginLeft = 4;
            _snapField.RegisterValueChangedCallback(_ => ApplySnapToHandler());
            snapRow.Add(_snapToggle); snapRow.Add(_snapField);
            _root.Add(snapRow);

            // ── 吸着対象 ─────────────────────────────────────────────
            // 原点ハンドルと Y 先端ハンドルが何へ吸い付くかを選ぶ。
            // 頂点は GPU ヒットテストが要るので、要らないときは切っておくと軽い。
            _root.Add(Header("吸着対象"));

            _snapVertexToggle = MakeSnapToggle("頂点にスナップ",
                (h, v) => h.SnapToVertex = v);
            _snapBoneToggle   = MakeSnapToggle("ボーンにスナップ",
                (h, v) => h.SnapToBone = v);
            _snapObjectToggle = MakeSnapToggle("描画オブジェクトにスナップ",
                (h, v) => h.SnapToObject = v);

            _root.Add(_snapVertexToggle);
            _root.Add(_snapBoneToggle);
            _root.Add(_snapObjectToggle);

            // ── 表示 ─────────────────────────────────────────────────
            _visibleToggle = new Toggle("ギズモを表示") { value = true };
            _visibleToggle.style.color = new StyleColor(Color.white);
            _visibleToggle.RegisterValueChangedCallback(e =>
            {
                var wa = GetWorkAxis?.Invoke();
                if (wa == null) return;
                wa.IsVisible = e.newValue;
                OnValueChanged?.Invoke();
            });
            _root.Add(_visibleToggle);

            // ── コマンド ──────────────────────────────────────────────
            var cmdRow1 = new VisualElement();
            cmdRow1.style.flexDirection = FlexDirection.Row;
            cmdRow1.style.marginTop     = 6;
            var toCentroid = new Button(MoveToSelectionCentroid) { text = "選択頂点の重心へ" };
            toCentroid.style.flexGrow = 1;
            cmdRow1.Add(toCentroid);
            _root.Add(cmdRow1);

            var cmdRow2 = new VisualElement();
            cmdRow2.style.flexDirection = FlexDirection.Row;
            cmdRow2.style.marginTop     = 2;
            var alignWorld = new Button(AlignToWorld) { text = "ワールド軸へ整列" };
            alignWorld.style.flexGrow = 1; alignWorld.style.marginRight = 2;
            var resetBtn = new Button(ResetAxis) { text = "リセット" };
            resetBtn.style.flexGrow = 1;
            cmdRow2.Add(alignWorld); cmdRow2.Add(resetBtn);
            _root.Add(cmdRow2);

            BuildLibrarySection();

            _infoLabel = new Label();
            _infoLabel.style.fontSize    = 10;
            _infoLabel.style.marginTop   = 4;
            _infoLabel.style.color       = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _root.Add(_infoLabel);

            RepaintModeButtons();
        }

        private Toggle MakeSnapToggle(string label, Action<WorkAxisToolHandler, bool> set)
        {
            var t = new Toggle(label);
            t.style.color = new StyleColor(Color.white);
            t.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h == null) return;
                set(h, e.newValue);
            });
            return t;
        }

        // ================================================================
        // 辞書
        // ================================================================

        private void BuildLibrarySection()
        {
            if (GetLibrary == null) return;

            _root.Add(Header("作業軸の辞書"));

            // 登録
            var addRow = new VisualElement();
            addRow.style.flexDirection = FlexDirection.Row;
            addRow.style.marginTop     = 2;

            _libNameField = new TextField { value = "" };
            _libNameField.style.flexGrow = 1;
            _libNameField.style.marginRight = 2;
            addRow.Add(_libNameField);

            var addBtn = new Button(RegisterCurrent) { text = "登録" };
            addBtn.style.width = 56;
            addRow.Add(addBtn);
            _root.Add(addRow);

            // 呼び出し / 削除
            _libDropdown = new DropdownField("登録済み", new List<string>(), -1);
            _libDropdown.style.color     = new StyleColor(Color.white);
            _libDropdown.style.marginTop = 2;
            _root.Add(_libDropdown);

            var useRow = new VisualElement();
            useRow.style.flexDirection = FlexDirection.Row;
            useRow.style.marginTop     = 2;
            var recallBtn = new Button(RecallSelected) { text = "呼び出し" };
            recallBtn.style.flexGrow = 1; recallBtn.style.marginRight = 2;
            var delBtn = new Button(RemoveSelected) { text = "削除" };
            delBtn.style.flexGrow = 1;
            useRow.Add(recallBtn); useRow.Add(delBtn);
            _root.Add(useRow);

            // CSV
            var csvRow = new VisualElement();
            csvRow.style.flexDirection = FlexDirection.Row;
            csvRow.style.marginTop     = 2;
            var saveBtn = new Button(SaveLibraryCsv) { text = "CSV保存" };
            saveBtn.style.flexGrow = 1; saveBtn.style.marginRight = 2;
            var loadBtn = new Button(LoadLibraryCsv) { text = "CSV読込" };
            loadBtn.style.flexGrow = 1;
            csvRow.Add(saveBtn); csvRow.Add(loadBtn);
            _root.Add(csvRow);

            RefreshLibraryList();
        }

        /// <summary>ドロップダウンの中身を辞書から作り直す。選択はできる限り保つ。</summary>
        public void RefreshLibraryList()
        {
            if (_libDropdown == null) return;

            var lib = GetLibrary?.Invoke();
            string prev = _libDropdown.index >= 0 && _libDropdown.index < _libNames.Count
                ? _libNames[_libDropdown.index] : null;

            _libNames.Clear();
            if (lib != null) _libNames.AddRange(lib.Names);

            _libDropdown.choices = new List<string>(_libNames);

            int idx = prev != null ? _libNames.IndexOf(prev) : -1;
            if (idx < 0 && _libNames.Count > 0) idx = 0;
            _libDropdown.index = idx;
        }

        private void RegisterCurrent()
        {
            var lib = GetLibrary?.Invoke();
            var wa  = GetWorkAxis?.Invoke();
            if (lib == null || wa == null) return;

            string name = WorkAxisLibrary.Normalize(_libNameField?.value);
            if (name.Length == 0)
            {
                SetInfo("名前を入れてください。");
                return;
            }

            bool overwrite = lib.Contains(name);
            lib.Set(name, WorkAxisEntry.FromContext(wa));

            RefreshLibraryList();
            if (_libDropdown != null) _libDropdown.index = _libNames.IndexOf(name);

            SetInfo(overwrite ? $"「{name}」を上書きしました。" : $"「{name}」を登録しました。");
            OnLibraryChanged?.Invoke();
        }

        private void RecallSelected()
        {
            var lib = GetLibrary?.Invoke();
            var wa  = GetWorkAxis?.Invoke();
            if (lib == null || wa == null || _libDropdown == null) return;

            int i = _libDropdown.index;
            if (i < 0 || i >= _libNames.Count) return;

            if (!lib.TryGet(_libNames[i], out var e)) return;

            e.ApplyTo(wa);
            Refresh();
            OnValueChanged?.Invoke();
            SetInfo($"「{_libNames[i]}」を呼び出しました。");
        }

        private void RemoveSelected()
        {
            var lib = GetLibrary?.Invoke();
            if (lib == null || _libDropdown == null) return;

            int i = _libDropdown.index;
            if (i < 0 || i >= _libNames.Count) return;

            string name = _libNames[i];
            if (!lib.Remove(name)) return;

            RefreshLibraryList();
            SetInfo($"「{name}」を削除しました。");
            OnLibraryChanged?.Invoke();
        }

        private void SaveLibraryCsv()
        {
            var lib = GetLibrary?.Invoke();
            if (lib == null) return;

            if (lib.Count == 0) { SetInfo("辞書が空です。"); return; }

            string path = PLEditorBridge.I.SaveFilePanel(
                "作業軸辞書を保存", Application.dataPath, "workaxis_library.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            SetInfo(WorkAxisLibraryCsvIO.Save(path, lib)
                ? $"{lib.Count} 件を保存しました： {Path.GetFileName(path)}"
                : "保存に失敗しました。");
        }

        private void LoadLibraryCsv()
        {
            var lib = GetLibrary?.Invoke();
            if (lib == null) return;

            string path = PLEditorBridge.I.OpenFilePanel(
                "作業軸辞書を読み込み", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(path)) return;

            // 既存へ足す。同名は上書き。
            var r = WorkAxisLibraryCsvIO.Load(path, lib, true);

            RefreshLibraryList();
            OnLibraryChanged?.Invoke();

            SetInfo(r.Success
                ? $"{r.Loaded} 件を読み込みました" + (r.Skipped > 0 ? $"（{r.Skipped} 行を読み飛ばし）" : "")
                : $"読み込みに失敗しました： {r.ErrorMessage}");
        }

        private void SetInfo(string text)
        {
            if (_infoLabel != null) _infoLabel.text = text;
        }

        // ================================================================
        // Refresh（Viewer から呼ぶ）
        // ================================================================

        public void Refresh()
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null)
            {
                if (_infoLabel != null) _infoLabel.text = "作業軸なし（モデル未選択）";
                return;
            }

            var p = wa.Origin;
            _posX?.SetValueWithoutNotify(p.x);
            _posY?.SetValueWithoutNotify(p.y);
            _posZ?.SetValueWithoutNotify(p.z);

            var e = wa.EulerAngles;
            _rotX?.SetValueWithoutNotify(e.x);
            _rotY?.SetValueWithoutNotify(e.y);
            _rotZ?.SetValueWithoutNotify(e.z);

            _visibleToggle?.SetValueWithoutNotify(wa.IsVisible);
            _lengthField?.SetValueWithoutNotify(wa.Length);

            var h = GetH?.Invoke();
            if (h != null)
            {
                _snapVertexToggle?.SetValueWithoutNotify(h.SnapToVertex);
                _snapBoneToggle  ?.SetValueWithoutNotify(h.SnapToBone);
                _snapObjectToggle?.SetValueWithoutNotify(h.SnapToObject);
            }

            RefreshLibraryList();

            if (_infoLabel != null)
            {
                var ax = wa.AxisX; var ay = wa.AxisY; var az = wa.AxisZ;
                _infoLabel.text =
                    $"X ({ax.x:F2}, {ax.y:F2}, {ax.z:F2})\n" +
                    $"Y ({ay.x:F2}, {ay.y:F2}, {ay.z:F2})\n" +
                    $"Z ({az.x:F2}, {az.y:F2}, {az.z:F2})";
            }

            RepaintModeButtons();
        }

        // ================================================================
        // 入力反映
        // ================================================================

        private void SetOrigin(int component, float value)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            var o = wa.Origin;
            if      (component == 0) o.x = value;
            else if (component == 1) o.y = value;
            else                     o.z = value;
            wa.Origin = o;

            OnValueChanged?.Invoke();
        }

        private void SetEuler(int component, float value)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            // オイラー角はフィールド3つの表示値をそのまま採用する。
            // wa.EulerAngles を読み直すと 0..360 へ正規化された値が返り、
            // 入力中の -30 などが 330 に化けてしまうため。
            var e = new Vector3(
                _rotX?.value ?? 0f,
                _rotY?.value ?? 0f,
                _rotZ?.value ?? 0f);
            if      (component == 0) e.x = value;
            else if (component == 1) e.y = value;
            else                     e.z = value;
            wa.EulerAngles = e;

            OnValueChanged?.Invoke();
        }

        private void SetLength(float value)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            // 下限のクランプは WorkAxisContext.Length に集約されている。
            wa.Length = value;
            _lengthField?.SetValueWithoutNotify(wa.Length);

            OnValueChanged?.Invoke();
        }

        private void ApplySnapToHandler()
        {
            var h = GetH?.Invoke();
            if (h == null) return;
            bool on = _snapToggle?.value ?? false;
            h.RotateSnapDeg = on ? Mathf.Max(0.1f, _snapField?.value ?? 15f) : 0f;
        }

        // ================================================================
        // コマンド
        // ================================================================

        private void MoveToSelectionCentroid()
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            var c = GetSelectionCentroidWorld?.Invoke();
            if (!c.HasValue)
            {
                if (_infoLabel != null) _infoLabel.text = "選択された頂点がありません";
                return;
            }

            wa.Origin = c.Value;
            OnValueChanged?.Invoke();
            Refresh();
        }

        private void AlignToWorld()
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;
            wa.AlignToWorld();
            OnValueChanged?.Invoke();
            Refresh();
        }

        private void ResetAxis()
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;
            wa.Reset();
            OnValueChanged?.Invoke();
            Refresh();
        }

        private void SetMode(WorkAxisToolHandler.WorkAxisGizmoMode mode)
        {
            var h = GetH?.Invoke();
            if (h != null) h.Mode = mode;
            RepaintModeButtons();
            OnValueChanged?.Invoke();
        }

        private void RepaintModeButtons()
        {
            var cur = GetH?.Invoke()?.Mode ?? WorkAxisToolHandler.WorkAxisGizmoMode.Move;
            if (_modeMoveBtn != null)
                _modeMoveBtn.style.backgroundColor =
                    (cur == WorkAxisToolHandler.WorkAxisGizmoMode.Move) ? ActiveBtnColor : InactiveBtnColor;
            if (_modeRotateBtn != null)
                _modeRotateBtn.style.backgroundColor =
                    (cur == WorkAxisToolHandler.WorkAxisGizmoMode.Rotate) ? ActiveBtnColor : InactiveBtnColor;
        }

        // ================================================================
        // ウィジェットヘルパー
        // ================================================================

        private static FloatField MakeField(string label, Action<float> onChange)
        {
            var f = new FloatField(label) { value = 0f };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.style.color       = new StyleColor(Color.black);
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(Color.white);
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
