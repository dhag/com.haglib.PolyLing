// MeshFilterToSkinnedSubPanel.cs
// MeshFilter → Skinned 変換パネル（Player / UIToolkit 版）。
// Runtime/Poly_Ling_Player/View/MeshFilterToSkinned/ に配置

using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using static Poly_Ling.Ops.MeshFilterToSkinnedTexts;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 変換完了後に呼ばれるコールバック。
    /// Viewer 側でGPUバッファ再構築・パネル通知を行う。
    /// </summary>
    public class MeshFilterToSkinnedSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        public Action OnConversionComplete;

        // ================================================================
        // 内部状態
        // ================================================================

        private bool _swapAxisForRotated = false;
        private bool _setAxisForIdentity = false;

        // UI
        private VisualElement _hierarchyContainer;
        private Label         _statusLabel;
        private Button        _convertBtn;

        // 現在表示対象のモデル
        private ModelContext  _model;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            // タイトル
            var title = new Label(T("WindowTitle"));
            title.style.fontSize = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;            title.style.marginBottom = 6;
            parent.Add(title);

            parent.Add(Sep());

            // ── 階層プレビュー（Foldout）
            var foldout = new Foldout { text = T("Preview"), value = true };
            foldout.style.marginBottom = 6;
            _hierarchyContainer = new VisualElement();
            foldout.Add(_hierarchyContainer);
            parent.Add(foldout);

            parent.Add(Sep());

            // ── ボーン軸設定
            var axisLabel = new Label(T("BoneAxisSettings"));
            axisLabel.style.fontSize = 10;
            axisLabel.style.color    = new StyleColor(new Color(0.65f, 0.8f, 1f));
            axisLabel.style.marginBottom = 3;
            parent.Add(axisLabel);

            var swapToggle = new Toggle(T("SwapAxisRotated")) { value = _swapAxisForRotated };
            swapToggle.style.marginBottom = 2;            swapToggle.RegisterValueChangedCallback(e => _swapAxisForRotated = e.newValue);
            parent.Add(swapToggle);

            var identToggle = new Toggle(T("SetAxisIdentity")) { value = _setAxisForIdentity };
            identToggle.style.marginBottom = 6;            identToggle.RegisterValueChangedCallback(e => _setAxisForIdentity = e.newValue);
            parent.Add(identToggle);

            parent.Add(Sep());

            // ── ステータスラベル
            _statusLabel = new Label("");
            _statusLabel.style.color     = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 6;
            parent.Add(_statusLabel);

            // ── 変換ボタン
            _convertBtn = new Button(OnConvertClicked) { text = T("Convert") };
            _convertBtn.style.height = 30;
            _convertBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            _convertBtn.style.backgroundColor = new StyleColor(new Color(0.55f, 0.22f, 0.22f));
            parent.Add(_convertBtn);
        }

        // ================================================================
        // モデル設定（表示切替時に呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            _model = model;
            RefreshHierarchy();
        }

        // ================================================================
        // 階層プレビュー更新
        // ================================================================

        private void RefreshHierarchy()
        {
            if (_hierarchyContainer == null) return;
            _hierarchyContainer.Clear();
            SetStatus("");

            if (_model == null)
            {
                AddInfoLabel(T("ModelNotAvailable"), warning: true);
                EnableConvert(false);
                return;
            }

            var entries = MeshFilterToSkinnedConverter.CollectMeshEntries(_model);

            if (entries.Count == 0)
            {
                AddInfoLabel(T("NoMeshFound"), warning: true);
                EnableConvert(false);
                return;
            }

            bool hasBones = _model.MeshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
            if (hasBones)
                AddInfoLabel(T("AlreadyHasBones"), warning: true);

            // ルートボーン表示
            var rootRow = new VisualElement();
            rootRow.style.flexDirection = FlexDirection.Row;
            rootRow.style.marginBottom  = 4;
            var rootKey = new Label(T("RootBone") + ": ");            rootKey.style.fontSize = 10;
            var rootVal = new Label(entries[0].Context.Name);            rootVal.style.fontSize = 10;
            rootRow.Add(rootKey); rootRow.Add(rootVal);
            _hierarchyContainer.Add(rootRow);

            // 階層リスト
            var hierLabel = new Label(T("BoneHierarchy") + ":");            hierLabel.style.fontSize = 10;
            hierLabel.style.marginBottom = 2;
            _hierarchyContainer.Add(hierLabel);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry    = entries[i];
                int   depth  = MeshFilterToSkinnedConverter.CalculateDepth(entry.Index, _model);
                string indent = new string(' ', depth * 3);
                string vertInfo = entry.Context.MeshObject?.VertexCount > 0
                    ? $" ({entry.Context.MeshObject.VertexCount}V)"
                    : " (empty)";

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.marginBottom  = 1;

                var nameLabel = new Label($"{indent}[{i}] {entry.Context.Name}{vertInfo}");
                nameLabel.style.fontSize = 9;
                nameLabel.style.flexGrow = 1;
                row.Add(nameLabel);

                // IgnorePose トグル（ローカル変数にキャプチャ）
                var ctx = entry.Context;
                var toggle = new Toggle("姿勢無視") { value = ctx.IgnorePoseInArmature };
                toggle.style.fontSize   = 8;
                toggle.style.marginLeft = 4;
                toggle.RegisterValueChangedCallback(e =>
                {
                    ctx.IgnorePoseInArmature = e.newValue;
                    if (e.newValue && ctx.BoneTransform != null)
                        ctx.BoneTransform.Rotation = Vector3.zero;
                    RefreshHierarchy();
                });
                row.Add(toggle);

                _hierarchyContainer.Add(row);
            }

            EnableConvert(!hasBones);
            PlayerLayoutRoot.ApplyDarkTheme(_hierarchyContainer);
        }

        // ================================================================
        // 変換実行
        // ================================================================

        private void OnConvertClicked()
        {
            if (_model == null) return;

            var entries = MeshFilterToSkinnedConverter.CollectMeshEntries(_model);
            if (entries.Count == 0)
            {
                SetStatus(T("NoMeshFound"));
                return;
            }

            bool hasBones = _model.MeshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
            if (hasBones)
            {
                SetStatus(T("AlreadyHasBones"));
                return;
            }

            try
            {
                int boneCount = MeshFilterToSkinnedConverter.Execute(
                    _model, entries, _swapAxisForRotated, _setAxisForIdentity);

                SetStatus(T("ConvertSuccess", boneCount));
                RefreshHierarchy();
                OnConversionComplete?.Invoke();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                Debug.LogException(ex);
            }
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private void AddInfoLabel(string text, bool warning)
        {
            var l = new Label(text);
            l.style.color     = warning
                ? new StyleColor(new Color(1f, 0.7f, 0.4f))
                : new StyleColor(new Color(0.7f, 0.9f, 0.7f));
            l.style.fontSize  = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 4;
            _hierarchyContainer?.Add(l);
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        private void EnableConvert(bool enable)
        {
            if (_convertBtn != null) _convertBtn.SetEnabled(enable);
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 4;
            v.style.marginBottom    = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
