// PlayerAddFaceSubPanel.cs
// 面追加ツール用サブパネル。エディタ版 AddFaceTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerAddFaceSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public Poly_Ling.Data.IToolSurface Surface;
        private const string Tool = "addFace";

        // ── 追加先オブジェクト（単一選択） ──
        /// <summary>候補一覧。(表示名, MeshContextList インデックス) を並び順で返す。</summary>
        public Func<List<(string Label, int MasterIndex)>> GetMeshEntries;
        /// <summary>現在の追加先の MeshContextList インデックス。未解決は -1。</summary>
        public Func<int>    GetActiveMeshIndex;
        /// <summary>追加先を切り替える。</summary>
        public Action<int>  OnSelectMesh;

        // ── マテリアル ──
        /// <summary>マテリアル名一覧（スロット順）。</summary>
        public Func<List<string>> GetMaterialNames;
        /// <summary>現在のマテリアルスロット。未解決は -1。</summary>
        public Func<int>    GetCurrentMaterialIndex;
        /// <summary>マテリアルスロットを切り替える。</summary>
        public Action<int>  OnSelectMaterial;

        // UI 自動操作の ID は "addFace.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("progress", Safety = UiSafety.ReadOnly, Description = "配置済みの点の数と、面になるまでの残り")]
        private Label         _progressLabel;
        [UiControl("placedHeader", Safety = UiSafety.ReadOnly, Description = "配置済み点の見出し")]
        private Label         _placedHeader;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _placedList;
        [UiControl("continuousLine", Reveal = nameof(RevealContinuous), Description = "線を続けて引く（Mode が 線分 のときだけ表示）")]
        private Toggle        _continuousToggle;
        [UiControl("extendLineGroup", Reveal = nameof(RevealContinuous), Description = "描き始めが既存の線分群の端点なら、その群を伸ばす（OFF なら新しい群を作り始点を親にする。Mode が 線分 のときだけ表示）")]
        private Toggle        _extendLineGroupToggle;
        [UiControl(Ignore = true)]
        private VisualElement _continuousRow;
        [UiControl("snapUnselected", Description = "非選択オブジェクトの頂点にも吸着する")]
        private Toggle        _snapUnselectedToggle;
        [UiControl("snapBones", Description = "ボーンの位置にも吸着する（頂点に当たらなかったときだけ）")]
        private Toggle        _snapBonesToggle;
        [UiControl("snapObjectOrigins", Description = "描画オブジェクトの原点にも吸着する（頂点に当たらなかったときだけ）")]
        private Toggle        _snapOriginsToggle;
        [UiControl("targetMesh", Description = "面を追加する先のオブジェクト")]
        private DropdownField _meshDD;
        [UiControl("material", Description = "追加する面のマテリアル（マテリアルリストのカレントと連動）")]
        private DropdownField _materialDD;
        [UiControl("mode", Description = "線分 / 三角形 / 四角形")]
        private DropdownField _modeDropdown;
        [UiControl("clearPoints", Safety = UiSafety.SafeWrite, Description = "配置途中の点を捨てる（作成済みの面は消さない）")]
        private Button        _clearPointsBtn;

        // ドロップダウンの表示名 → 実インデックスの対応。
        // 表示名は "[3] 名前" 形式で重複し得るので、選択は index で解決する。
        private readonly List<int> _meshIndices     = new List<int>();
        private readonly List<int> _materialIndices = new List<int>();

        // 同期中のコールバック発火を止めるフラグ。
        // SetValueWithoutNotify では choices 差し替え時の index 変化を抑えられない。
        private bool _syncing;

        // モードの選択肢。表示名と値は位置で対応させる（表示名で値を決めない）。
        private const string ModeLabelLine = "線分";
        private static readonly List<string> ModeChoices =
            new List<string> { ModeLabelLine, "三角形", "四角形" };
        private static readonly AddFaceMode[] ModeValues =
            { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Add Face"));

            // モード選択
            var modeDD = new DropdownField("Mode", ModeChoices, 2);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e =>
            {
                int idx = ModeChoices.IndexOf(e.newValue);
                if (Surface == null || idx < 0) return;
                Surface.Set(Tool, "modePublic", ModeValues[idx]);
                // 送った後はツール側の実際の値で表示を合わせ直す。
                // 設定が通らなかったとき、表示だけ変わってツールが元のまま、
                // というずれを残さないため（ずれたままだと同じ項目を選び直しても
                // 値が変わらないので何も送られない）。
                SyncModeDropdown();
                UpdateConditionals();
            });
            _root.Add(modeDD);
            _modeDropdown = modeDD;

            // 追加先オブジェクト（1つだけ選ぶ）
            _meshDD = new DropdownField("追加先", new List<string>(), -1);
            _meshDD.style.color = new StyleColor(Color.white);
            _meshDD.RegisterValueChangedCallback(e =>
            {
                if (_syncing) return;
                int i = _meshDD.index;
                if (i < 0 || i >= _meshIndices.Count) return;
                OnSelectMesh?.Invoke(_meshIndices[i]);
            });
            _root.Add(_meshDD);

            // マテリアル（モデル共通のカレントマテリアル。マテリアルリストと連動する）
            _materialDD = new DropdownField("マテリアル", new List<string>(), -1);
            _materialDD.style.color = new StyleColor(Color.white);
            _materialDD.RegisterValueChangedCallback(e =>
            {
                if (_syncing) return;
                int i = _materialDD.index;
                if (i < 0 || i >= _materialIndices.Count) return;
                OnSelectMaterial?.Invoke(_materialIndices[i]);
            });
            _root.Add(_materialDD);

            // ContinuousLine（Line mode 時のみ表示）
            _continuousRow = new VisualElement();
            _continuousToggle = new Toggle("Continuous Line") { value = true };
            _continuousToggle.style.color = new StyleColor(Color.white);
            _continuousToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "continuousLinePublic", e.newValue));
            _continuousRow.Add(_continuousToggle);

            // 線分群を伸ばすか（既定 OFF = 新しい群を作り、始点を親にする）
            _extendLineGroupToggle = new Toggle("既存の線分群を伸ばす") { value = false };
            _extendLineGroupToggle.style.color = new StyleColor(Color.white);
            _extendLineGroupToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "extendLineGroupPublic", e.newValue));
            _continuousRow.Add(_extendLineGroupToggle);

            _root.Add(_continuousRow);

            // 非選択オブジェクトへの吸着
            // ON の間だけ GPU 側で追加のヒットテストが走る（頂点数ぶんの読み戻しが 1 回増える）。
            // 既定は OFF。
            _snapUnselectedToggle = new Toggle("非選択オブジェクトにも吸着") { value = false };
            _snapUnselectedToggle.style.color = new StyleColor(Color.white);
            _snapUnselectedToggle.RegisterValueChangedCallback(e =>
            {
                Surface?.Set(Tool, "snapToUnselectedObjects", e.newValue);
            });
            _root.Add(_snapUnselectedToggle);

            // ボーン位置・描画オブジェクト原点への吸着（頂点に当たらなかったときだけ）
            _snapBonesToggle = new Toggle("ボーンにも吸着") { value = false };
            _snapBonesToggle.style.color = new StyleColor(Color.white);
            _snapBonesToggle.RegisterValueChangedCallback(e =>
            {
                Surface?.Set(Tool, "snapToBones", e.newValue);
            });
            _root.Add(_snapBonesToggle);

            _snapOriginsToggle = new Toggle("オブジェクト原点にも吸着") { value = false };
            _snapOriginsToggle.style.color = new StyleColor(Color.white);
            _snapOriginsToggle.RegisterValueChangedCallback(e =>
            {
                Surface?.Set(Tool, "snapToObjectOrigins", e.newValue);
            });
            _root.Add(_snapOriginsToggle);

            // 進捗
            _progressLabel = InfoLabel(); _root.Add(_progressLabel);

            // 配置済み点
            _placedHeader = InfoLabel();
            _placedHeader.style.display = DisplayStyle.None;
            _root.Add(_placedHeader);
            _placedList = new VisualElement();
            _root.Add(_placedList);

            // Clear ボタン
            var clearBtn = new Button(() => { Surface?.Invoke(Tool, "clearPointsPublic"); Refresh(); }) { text = "Clear Points" };
            clearBtn.style.marginTop = 3;
            _root.Add(clearBtn);
            _clearPointsBtn = clearBtn;

            var helpBox = new HelpBox("クリックで点を配置して面を作成します。", HelpBoxMessageType.Info);
            helpBox.style.color = new StyleColor(Color.white);
            helpBox.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            helpBox.style.marginTop = 4;
            _root.Add(helpBox);

            UpdateConditionals();
            RefreshDropdowns();
        }

        public void Refresh()
        {
            RefreshDropdowns();

            if (Surface == null) return;
            _progressLabel.text = $"Points: {Surface.GetInt(Tool, "placedPointCount")} / {Surface.GetInt(Tool, "requiredPointsPublic")}";
            bool snapUnsel  = Surface.GetBool(Tool, "snapToUnselectedObjects");
            bool extend     = Surface.GetBool(Tool, "extendLineGroupPublic");
            bool snapBones  = Surface.GetBool(Tool, "snapToBones");
            bool snapOrigin = Surface.GetBool(Tool, "snapToObjectOrigins");
            if (_snapUnselectedToggle != null && _snapUnselectedToggle.value != snapUnsel)
                _snapUnselectedToggle.SetValueWithoutNotify(snapUnsel);
            if (_extendLineGroupToggle != null && _extendLineGroupToggle.value != extend)
                _extendLineGroupToggle.SetValueWithoutNotify(extend);
            if (_snapBonesToggle != null && _snapBonesToggle.value != snapBones)
                _snapBonesToggle.SetValueWithoutNotify(snapBones);
            if (_snapOriginsToggle != null && _snapOriginsToggle.value != snapOrigin)
                _snapOriginsToggle.SetValueWithoutNotify(snapOrigin);
            SyncModeDropdown();
            UpdateConditionals();

            // 配置済み点リスト更新
            if (_placedList != null)
            {
                _placedList.Clear();
                var labels = Surface.Get(Tool, "pointLabels", System.Array.Empty<string>());
                if (labels.Length > 0)
                {
                    if (_placedHeader != null)
                    {
                        _placedHeader.text    = "配置済み点:";
                        _placedHeader.style.display = DisplayStyle.Flex;
                    }
                    foreach (var label in labels)
                    {
                        var lbl = new Label(label);
                        lbl.style.color = new StyleColor(Color.white);
                        lbl.style.fontSize = 10;
                        _placedList.Add(lbl);
                    }
                }
                else
                {
                    if (_placedHeader != null) _placedHeader.style.display = DisplayStyle.None;
                }
            }
        }

        /// <summary>
        /// 追加先／マテリアルのドロップダウンを現在の状態へ合わせ直す。
        /// 一覧はメッシュ追加・削除やマテリアル増減で変わるため毎回作り直す。
        /// </summary>
        private void RefreshDropdowns()
        {
            _syncing = true;
            try
            {
                if (_meshDD != null)
                {
                    var entries = GetMeshEntries?.Invoke() ?? new List<(string, int)>();
                    var labels  = new List<string>();
                    _meshIndices.Clear();
                    foreach (var e in entries) { labels.Add(e.Label); _meshIndices.Add(e.MasterIndex); }

                    _meshDD.choices = labels;
                    int cur = GetActiveMeshIndex?.Invoke() ?? -1;
                    _meshDD.index = _meshIndices.IndexOf(cur);
                }

                if (_materialDD != null)
                {
                    var names = GetMaterialNames?.Invoke() ?? new List<string>();
                    var labels = new List<string>();
                    _materialIndices.Clear();
                    for (int i = 0; i < names.Count; i++)
                    {
                        labels.Add($"[{i}] {names[i]}");
                        _materialIndices.Add(i);
                    }

                    _materialDD.choices = labels;
                    int cur = GetCurrentMaterialIndex?.Invoke() ?? -1;
                    _materialDD.index = _materialIndices.IndexOf(cur);
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>
        /// モードのドロップダウンをツール側の実際の値へ合わせる。
        ///
        /// 【読めたときだけ合わせる】
        ///   値の正本はツール側（ハンドラ）にある。読み取りに対応していない窓口
        ///   （ClientToolSurface は常に読めない）で既定値へ合わせると、
        ///   表示が毎回四角形へ戻ってしまうので、そのときは触らない。
        /// </summary>
        private void SyncModeDropdown()
        {
            if (_modeDropdown == null || Surface == null) return;
            if (!Surface.TryGet(Tool, "modePublic", out string raw)) return;
            if (!PanelCommandFactory.TryParseValue(raw, typeof(AddFaceMode), out object v, out _)) return;
            if (!(v is AddFaceMode mode)) return;

            int idx = System.Array.IndexOf(ModeValues, mode);
            if (idx < 0) return;
            if (_modeDropdown.value != ModeChoices[idx])
                _modeDropdown.SetValueWithoutNotify(ModeChoices[idx]);
        }

        private void UpdateConditionals()
        {
            bool isLine = Surface != null
                && Surface.Get(Tool, "modePublic", AddFaceMode.Quad) == AddFaceMode.Line;
            if (_continuousRow != null)
                _continuousRow.style.display = isLine ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。Continuous Line は Mode が 線分 の
        /// ときだけ表示されるので、利用者と同じく Mode を 線分 にする。既に 線分 なら false。
        /// </summary>
        private bool RevealContinuous()
        {
            if (_modeDropdown == null || _modeDropdown.value == ModeLabelLine) return false;
            _modeDropdown.value = ModeLabelLine;
            return true;
        }

        private static Label Header(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
