// PlayerAddFaceSubPanel.cs
// 面追加ツール用サブパネル。エディタ版 AddFaceTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerAddFaceSubPanel
    {
        public Func<AddFaceToolHandler> GetH;

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

        private VisualElement _root;
        private Label         _progressLabel;
        private Label         _placedHeader;
        private VisualElement _placedList;
        private Toggle        _continuousToggle;
        private VisualElement _continuousRow;
        private Toggle        _snapUnselectedToggle;
        private DropdownField _meshDD;
        private DropdownField _materialDD;

        // ドロップダウンの表示名 → 実インデックスの対応。
        // 表示名は "[3] 名前" 形式で重複し得るので、選択は index で解決する。
        private readonly List<int> _meshIndices     = new List<int>();
        private readonly List<int> _materialIndices = new List<int>();

        // 同期中のコールバック発火を止めるフラグ。
        // SetValueWithoutNotify では choices 差し替え時の index 変化を抑えられない。
        private bool _syncing;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Add Face"));

            // モード選択
            var modeChoices = new List<string> { "Line", "Triangle", "Quad" };
            var modeValues  = new[] { AddFaceMode.Line, AddFaceMode.Triangle, AddFaceMode.Quad };
            var modeDD = new DropdownField("Mode", modeChoices, 2);
            modeDD.style.color = new StyleColor(Color.white);
            modeDD.RegisterValueChangedCallback(e =>
            {
                int idx = modeChoices.IndexOf(e.newValue);
                var h = GetH(); if (h == null || idx < 0) return;
                h.ModePublic = modeValues[idx];
                UpdateConditionals();
            });
            _root.Add(modeDD);

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
            _continuousToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.ContinuousLinePublic = e.newValue; });
            _continuousRow.Add(_continuousToggle);
            _root.Add(_continuousRow);

            // 非選択オブジェクトへの吸着
            // ON の間だけ GPU 側で追加のヒットテストが走る（頂点数ぶんの読み戻しが 1 回増える）。
            // 既定は OFF。
            _snapUnselectedToggle = new Toggle("非選択オブジェクトにも吸着") { value = false };
            _snapUnselectedToggle.style.color = new StyleColor(Color.white);
            _snapUnselectedToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h != null) h.SnapToUnselectedObjects = e.newValue;
            });
            _root.Add(_snapUnselectedToggle);

            // 進捗
            _progressLabel = InfoLabel(); _root.Add(_progressLabel);

            // 配置済み点
            _placedHeader = InfoLabel();
            _placedHeader.style.display = DisplayStyle.None;
            _root.Add(_placedHeader);
            _placedList = new VisualElement();
            _root.Add(_placedList);

            // Clear ボタン
            var clearBtn = new Button(() => { GetH()?.ClearPointsPublic(); Refresh(); }) { text = "Clear Points" };
            clearBtn.style.marginTop = 3;
            _root.Add(clearBtn);

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

            var h = GetH(); if (h == null) return;
            _progressLabel.text = $"Points: {h.PlacedPointCount} / {h.RequiredPointsPublic}";
            if (_snapUnselectedToggle != null
                && _snapUnselectedToggle.value != h.SnapToUnselectedObjects)
                _snapUnselectedToggle.SetValueWithoutNotify(h.SnapToUnselectedObjects);
            UpdateConditionals();

            // 配置済み点リスト更新
            if (_placedList != null)
            {
                _placedList.Clear();
                var labels = h.GetPointLabels();
                if (labels.Count > 0)
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

        private void UpdateConditionals()
        {
            var h = GetH();
            bool isLine = h?.ModePublic == AddFaceMode.Line;
            if (_continuousRow != null)
                _continuousRow.style.display = isLine ? DisplayStyle.Flex : DisplayStyle.None;
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
