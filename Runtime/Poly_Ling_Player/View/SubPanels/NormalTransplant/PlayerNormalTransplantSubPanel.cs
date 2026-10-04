// PlayerNormalTransplantSubPanel.cs
// 法線移植 サブパネル（Player ビルド用）。
// ビフォー／アフターの2オブジェクトが作るシェル（プリズム群）から、
// ターゲットオブジェクトの各頂点へ法線を移植する。
// Runtime/Poly_Ling_Player/View/SubPanels/NormalTransplant/ に配置
//
// 【設計】
// 移植法線はビフォー・アフター・ターゲットが動かない限り不変なので、
// 「法線を計算」ボタン押下時に1回だけ算出する。適用率スライダーの操作は
// 退避値との Slerp のみで、再計算も GPU 読み戻しも発生しない。
//
// 【前提】
// スキニング無し。法線の空間変換はオブジェクト単位の WorldMatrix だけを使う
// （NormalTransplantOperation の冒頭コメントを参照）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerNormalTransplantSubPanel
    {
        // ================================================================
        // レンジ（上下限）
        //
        // 実体は ParameterLimits（persistentDataPath の CSV）にあり、ここでは
        // キーを引くだけにする。同じキーを PanelCommand の PLParam(LimitKey) が
        // 指すので、UI とスキーマで範囲の定義が1箇所になる。
        // ================================================================

        private static float StrengthMin => ParameterLimits.GetF("NormalTransplant.Strength.Min");
        private static float StrengthMax => ParameterLimits.GetF("NormalTransplant.Strength.Max");

        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>法線更新後の Unity Mesh / GPU 反映。</summary>
        public Action<MeshContext> OnSyncMeshNormals;

        /// <summary>トポロジー変更後の再構築。</summary>
        public Action OnNotifyTopologyChanged;

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>Undo記録用の UndoController 取得。</summary>
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>Undo記録用の CommandQueue 取得。</summary>
        public Func<Poly_Ling.Commands.CommandQueue> GetCommandQueue;

        /// <summary>
        /// 指定 MeshContext の全頂点ワールド座標を返す。
        /// GPU が計算した値（GetDisplayPositions）を参照する経路を配線すること。
        /// </summary>
        public Func<MeshContext, Vector3[]> GetWorldPositions;

        /// <summary>
        /// ワールド座標の再計算要求（UpdateTransform）。
        /// 法線計算の直前に1回だけ呼ぶ。毎フレーム呼んではならない。
        /// </summary>
        public Action OnRequestUpdateTransform;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int> _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int _beforeIndex = -1;
        private int _afterIndex = -1;
        private readonly HashSet<int> _targetIndices = new HashSet<int>();

        private float _strength = 1f;
        private bool _spherical = false;
        private bool _allowNearest = false;

        private readonly NormalTransplantPreviewState _preview = new NormalTransplantPreviewState();

        /// <summary>直近の「法線を計算」に掛かった時間（ミリ秒）。</summary>
        public double LastComputeMilliseconds { get; private set; }

        private readonly List<(int index, string name, int vertexCount)> _candidates
            = new List<(int, string, int)>();

        // ================================================================
        // ボタンや入力欄
        // ================================================================

        // UI 自動操作の ID は "normalTransplant.<下の Id>"（UiControlAttribute.cs）。
        // ビフォー・アフター・対象のチェックはオブジェクトに合わせて作り直す行（Rows）。
        // 強さ・決定・キャンセルは「法線を計算」の後だけ表示される。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "警告（出ていないときは非表示）")]
        private Label _warningLabel;
        [UiControl(Ignore = true)]
        private VisualElement _mainContent;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _beforeListContainer;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _afterListContainer;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _targetListContainer;
        [UiControl("blendMode", Description = "法線の混ぜ方（選択肢は uiGetValue の choices）")]
        private RadioButtonGroup _blendModeGroup;
        [UiControl("allowNearest", Description = "プリズム外の頂点は最も近いプリズムへ寄せる")]
        private Toggle _toggleAllowNearest;
        [UiControl("compute", Safety = UiSafety.SafeWrite, Description = "法線を計算してプレビューを出す")]
        private Button _btnCompute;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label _statusLabel;
        [UiControl(Ignore = true)]
        private VisualElement _applySection;
        [UiControl("strength", Description = "法線を移す強さ（「法線を計算」の後だけ表示）")]
        private Slider _sliderStrength;
        [UiControl("strengthText", Safety = UiSafety.ReadOnly, Description = "強さの表示")]
        private Label _sliderValueLabel;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "法線の移植を決定する（「法線を計算」の後だけ表示）")]
        private Button _btnApply;
        [UiControl("cancel", Safety = UiSafety.SafeWrite, Description = "法線の移植のプレビューを取り消す（「法線を計算」の後だけ表示）")]
        private Button _btnCancel;
        [UiControl("makePair", Safety = UiSafety.SafeWrite, Description = "編集対象のオブジェクトを 2 つ複製し、ビフォー・アフター・ターゲットに設定する")]
        private Button _btnMakePair;
        [UiControl("pairCheck", Safety = UiSafety.ReadOnly, Description = "ビフォー／アフターの条件（面数・コーナー数の一致）の検査結果")]
        private Label  _pairCheckLabel;
        [UiControl("selectUnresolved", Safety = UiSafety.SafeWrite, Description = "移植法線が求まらなかった頂点を選択する（「法線を計算」の後だけ表示）")]
        private Button _btnSelectUnresolved;
        [UiControl("selectNearest", Safety = UiSafety.SafeWrite, Description = "最近傍のプリズムで補った頂点を選択する（「法線を計算」の後だけ表示）")]
        private Button _btnSelectNearest;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = 4;
            _root.style.paddingRight = 4;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.display = DisplayStyle.None;
            _warningLabel.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            // ── 準備の補助（オブジェクトが足りないときも使えるよう _mainContent の外に置く）
            var prepHelp = new Label(
                "参照ペアを作る：編集対象のオブジェクトを 2 つ複製し、1 つ目をビフォー、2 つ目をアフター、"
                + "元をターゲットにする。アフターを膨らませる・削るなどして形を作り、「法線を計算」する。"
                + "同じ頂点構成の別オブジェクトから法線をそのまま写すだけなら「頂点データ転送」パネルを使う。");
            prepHelp.style.fontSize     = 9;
            prepHelp.style.whiteSpace   = WhiteSpace.Normal;
            prepHelp.style.color        = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            prepHelp.style.marginBottom = 2;
            _root.Add(prepHelp);

            _btnMakePair = new Button(OnMakePairClicked) { text = "編集対象から参照ペアを作る" };
            _btnMakePair.style.height       = 22;
            _btnMakePair.style.fontSize     = 10;
            _btnMakePair.style.marginBottom = 4;
            _root.Add(_btnMakePair);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            // ── ビフォー
            _mainContent.Add(SecLabel("ビフォー（内側の面）"));
            _beforeListContainer = new VisualElement();
            _beforeListContainer.style.marginBottom = 4;
            _mainContent.Add(_beforeListContainer);

            // ── アフター
            _mainContent.Add(SecLabel("アフター（外側の面）"));
            _afterListContainer = new VisualElement();
            _afterListContainer.style.marginBottom = 4;
            _mainContent.Add(_afterListContainer);

            // ビフォー／アフターの条件（同一トポロジ）を計算の前に示す
            _pairCheckLabel = new Label();
            _pairCheckLabel.style.fontSize     = 10;
            _pairCheckLabel.style.whiteSpace   = WhiteSpace.Normal;
            _pairCheckLabel.style.marginBottom = 4;
            _mainContent.Add(_pairCheckLabel);

            _mainContent.Add(Sep());

            // ── ターゲット
            _mainContent.Add(SecLabel("ターゲット（法線を差し替える／複数可）"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 4;
            _mainContent.Add(_targetListContainer);

            _mainContent.Add(Sep());

            // ── 三角形内の補間
            _mainContent.Add(SecLabel("三角形内の補間"));
            var blendChoices = new List<string>
            {
                "線形補間（既定）",
                "球面補間",
            };
            _blendModeGroup = new RadioButtonGroup(null, blendChoices) { value = _spherical ? 1 : 0 };
            _blendModeGroup.style.marginBottom = 4;
            _blendModeGroup.RegisterValueChangedCallback(e =>
            {
                bool newSpherical = (e.newValue == 1);
                if (_spherical == newSpherical) return;
                _spherical = newSpherical;
                // 結果が変わるため、計算済みのプレビューは破棄する
                CancelComputation();
                _applySection.style.display = DisplayStyle.None;
            });
            _mainContent.Add(_blendModeGroup);

            _toggleAllowNearest = new Toggle("プリズム外の頂点は最も近いプリズムへ寄せる")
            {
                value = _allowNearest
            };
            _toggleAllowNearest.style.fontSize = 10;
            _toggleAllowNearest.style.marginBottom = 4;
            _toggleAllowNearest.RegisterValueChangedCallback(e =>
            {
                if (_allowNearest == e.newValue) return;
                _allowNearest = e.newValue;
                CancelComputation();
                _applySection.style.display = DisplayStyle.None;
            });
            _mainContent.Add(_toggleAllowNearest);

            _btnCompute = new Button(OnComputeClicked) { text = "法線を計算" };
            _btnCompute.style.height = 24;
            _btnCompute.style.fontSize = 10;
            _btnCompute.style.marginBottom = 4;
            _mainContent.Add(_btnCompute);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.style.marginBottom = 4;
            _mainContent.Add(_statusLabel);

            _mainContent.Add(Sep());

            // ── 適用率スライダー
            _applySection = new VisualElement();
            _applySection.style.display = DisplayStyle.None;
            _mainContent.Add(_applySection);

            _applySection.Add(SecLabel("適用率"));

            var slRow = new VisualElement();
            slRow.style.flexDirection = FlexDirection.Row;
            slRow.style.marginBottom = 4;
            _sliderStrength = new Slider(StrengthMin, StrengthMax) { value = StrengthMax };
            _sliderStrength.style.flexGrow = 1;
            _sliderStrength.RegisterValueChangedCallback(e => OnSliderChanged(e.newValue));
            _sliderValueLabel = new Label("1.00");
            _sliderValueLabel.style.width = 32;
            _sliderValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            slRow.Add(_sliderStrength);
            slRow.Add(_sliderValueLabel);
            _applySection.Add(slRow);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _applySection.Add(btnRow);

            _btnApply = new Button(OnApplyClicked) { text = "決定" };
            _btnApply.style.flexGrow = 1;
            _btnApply.style.marginRight = 4;
            _btnApply.style.height = 24;
            _btnApply.style.fontSize = 10;
            var btnCancel = new Button(OnCancelClicked) { text = "キャンセル" };
            btnCancel.style.flexGrow = 1;
            btnCancel.style.height = 24;
            btnCancel.style.fontSize = 10;
            btnRow.Add(_btnApply);
            btnRow.Add(btnCancel);
            _btnCancel = btnCancel;

            // 頂点ごとの結果の確認（プリズム内で求まった／最近傍で補った／求まらなかった）
            var selRow = new VisualElement();
            selRow.style.flexDirection = FlexDirection.Row;
            selRow.style.marginTop     = 4;
            _applySection.Add(selRow);
            _btnSelectUnresolved = new Button(() => SelectByKind(unresolved: true)) { text = "求まらなかった頂点を選択" };
            _btnSelectNearest    = new Button(() => SelectByKind(unresolved: false)) { text = "最近傍で補った頂点を選択" };
            foreach (var b in new[] { _btnSelectUnresolved, _btnSelectNearest })
            {
                b.style.flexGrow = 1;
                b.style.height   = 22;
                b.style.fontSize = 10;
                selRow.Add(b);
            }
        }

        // ================================================================
        // 準備の補助
        // ================================================================

        /// <summary>
        /// 編集対象のオブジェクトを 2 つ複製し、1 つ目をビフォー、2 つ目をアフター、元をターゲットにする。
        /// 複製はコマンド（DuplicateMeshesCommand）で行うので Undo できる。
        /// </summary>
        private void OnMakePairClicked()
        {
            if (_model == null) return;
            var src = _model.ActiveMeshContext;
            if (src?.MeshObject == null)
            {
                SetStatusColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = "編集対象のオブジェクトがありません";
                return;
            }

            CancelComputation();
            int mi = _getModelIndex?.Invoke() ?? 0;

            // 2 つの複製を 1 回のコマンドで作る（同じ索引を 2 回渡す）。
            // こうすると Undo 1 回で 2 つとも消える。
            MeshContext before = null, after = null;
            int srcIdx = _model.MeshContextList.IndexOf(src);
            if (srcIdx >= 0)
            {
                var known = new HashSet<MeshContext>(_model.MeshContextList);
                _panelContext?.SendCommand(new DuplicateMeshesCommand(mi, new[] { srcIdx, srcIdx }));
                foreach (var m in _model.MeshContextList)
                {
                    if (m == null || known.Contains(m)) continue;
                    if (before == null) before = m;
                    else if (after == null) after = m;
                }
            }
            if (before == null || after == null)
            {
                SetStatusColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = "複製できませんでした";
                Refresh();
                return;
            }

            _beforeIndex = _model.MeshContextList.IndexOf(before);
            _afterIndex  = _model.MeshContextList.IndexOf(after);
            _targetIndices.Clear();
            _targetIndices.Add(_model.MeshContextList.IndexOf(src));

            Refresh();
            SetStatusColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.text =
                $"参照ペアを作りました（ビフォー「{before.Name}」／アフター「{after.Name}」／ターゲット「{src.Name}」）。"
                + "アフターを変形してから「法線を計算」してください。";
        }

        /// <summary>計算済みのサンプルから、求まらなかった頂点、または最近傍で補った頂点を選択する。</summary>
        private void SelectByKind(bool unresolved)
        {
            var samples = _preview.Samples;
            if (samples == null || _model == null) return;

            var masters = new List<int>();
            var verts   = new List<int>();
            var meshes  = new List<int>();
            foreach (var s in samples)
            {
                masters.Add(s.MasterIndex);
                for (int vi = 0; vi < s.VertexCount; vi++)
                {
                    bool resolved = s.Resolved != null && vi < s.Resolved.Length && s.Resolved[vi];
                    bool inside   = s.Inside   != null && vi < s.Inside.Length   && s.Inside[vi];
                    bool hit = unresolved ? !resolved : (resolved && !inside);
                    if (!hit) continue;
                    verts.Add(vi);
                    meshes.Add(s.MasterIndex);
                }
            }

            var empty = System.Array.Empty<int>();
            _panelContext?.SendCommand(new SelectElementsCommand(
                _getModelIndex?.Invoke() ?? 0, masters.ToArray(),
                verts.ToArray(), meshes.ToArray(),
                empty, empty, empty, empty, empty, empty));
            _statusLabel.text = unresolved
                ? $"求まらなかった頂点 {verts.Count} 個を選択しました"
                : $"最近傍で補った頂点 {verts.Count} 個を選択しました";
            OnRepaint?.Invoke();
        }

        private void SetStatusColor(Color c)
        {
            if (_statusLabel != null) _statusLabel.style.color = new StyleColor(c);
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            if (_preview.IsActive) EndPreview();
            _model = model;
            _beforeIndex = -1;
            _afterIndex = -1;
            _targetIndices.Clear();
            _strength = 1f;
            _candidates.Clear();
            Refresh();
        }

        /// <summary>選択変更・属性変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            if (_preview.IsActive) EndPreview();
            _strength = 1f;
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            if (_model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            BuildCandidates(_model);
            if (_candidates.Count < 3)
            {
                ShowWarning("メッシュが3つ以上必要です（ビフォー・アフター・ターゲット）");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            RefreshPickList(_beforeListContainer, _beforeIndex, idx =>
            {
                if (_beforeIndex == idx) return;
                CancelComputation();
                _beforeIndex = idx;
                if (_afterIndex == idx) _afterIndex = -1;
                _targetIndices.Remove(idx);
                Refresh();
            });

            RefreshPickList(_afterListContainer, _afterIndex, idx =>
            {
                if (_afterIndex == idx) return;
                CancelComputation();
                _afterIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                _targetIndices.Remove(idx);
                Refresh();
            });

            RefreshTargetList();

            // ビフォー／アフターの条件を計算の前に示す（不一致なら計算させない）
            bool pairOk = true;
            if (_beforeIndex >= 0 && _afterIndex >= 0 && _beforeIndex != _afterIndex)
            {
                pairOk = NormalTransplantOperation.CheckPairTopology(
                    _model.GetMeshContext(_beforeIndex)?.MeshObject,
                    _model.GetMeshContext(_afterIndex)?.MeshObject,
                    out string why);
                _pairCheckLabel.text = pairOk
                    ? "ビフォー／アフター：面数と各面のコーナー数が一致しています"
                    : $"ビフォー／アフターが条件を満たしません：{why}";
                _pairCheckLabel.style.color = pairOk
                    ? new StyleColor(new Color(0.5f, 0.9f, 0.5f))
                    : new StyleColor(new Color(1f, 0.4f, 0.4f));
            }
            else
            {
                _pairCheckLabel.text = "ビフォーとアフターを選んでください（同じ面数・同じコーナー数が条件）";
                _pairCheckLabel.style.color = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            }

            _btnCompute.SetEnabled(
                pairOk &&
                _beforeIndex >= 0 && _afterIndex >= 0 &&
                _beforeIndex != _afterIndex && _targetIndices.Count > 0);

            _applySection.style.display = _preview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;

            if (_preview.IsActive)
                _sliderStrength.SetValueWithoutNotify(_strength);
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        private void BuildCandidates(ModelContext model)
        {
            _candidates.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.BakedMirror &&
                    ctx.Type != MeshType.MirrorSide) continue;
                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            if (_beforeIndex >= 0 && !ContainsCandidate(_beforeIndex)) _beforeIndex = -1;
            if (_afterIndex >= 0 && !ContainsCandidate(_afterIndex)) _afterIndex = -1;
            _targetIndices.RemoveWhere(i => !ContainsCandidate(i));
        }

        private bool ContainsCandidate(int index)
        {
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].index == index) return true;
            return false;
        }

        // ================================================================
        // リスト描画
        // ================================================================

        private void RefreshPickList(VisualElement container, int selectedIndex, Action<int> onPick)
        {
            container.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                int idx = c.index;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft = 4;
                row.style.fontSize = 10;

                if (idx == selectedIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ => onPick(idx));
                container.Add(row);
            }

            PlayerLayoutRoot.ApplyDarkTheme(container);
        }

        private void RefreshTargetList()
        {
            _targetListContainer.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                int idx = c.index;

                if (idx == _beforeIndex || idx == _afterIndex) continue;

                var tg = new Toggle($"{c.name}  [V:{c.vertexCount}]")
                {
                    value = _targetIndices.Contains(idx)
                };
                tg.style.fontSize = 10;
                tg.style.marginBottom = 1;
                tg.RegisterValueChangedCallback(e =>
                {
                    CancelComputation();
                    if (e.newValue) _targetIndices.Add(idx);
                    else _targetIndices.Remove(idx);
                    _applySection.style.display = DisplayStyle.None;
                    _btnCompute.SetEnabled(
                        _beforeIndex >= 0 && _afterIndex >= 0 &&
                        _beforeIndex != _afterIndex && _targetIndices.Count > 0);
                });
                _targetListContainer.Add(tg);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_targetListContainer);
        }

        // ================================================================
        // 法線計算
        // ================================================================

        private void OnComputeClicked()
        {
            if (_model == null) return;
            if (_beforeIndex < 0 || _afterIndex < 0 || _beforeIndex == _afterIndex) return;
            if (_targetIndices.Count == 0) return;

            CancelComputation();

            // ワールド座標が要るのはこの時点だけ。毎フレームは呼ばない。
            OnRequestUpdateTransform?.Invoke();

            // 計算時間を測って表示する（連続再計算の可否を判断する材料。文書 5.5）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var samples = NormalTransplantOperation.ComputeSamples(
                _model, _beforeIndex, _afterIndex, new List<int>(_targetIndices),
                _spherical
                    ? NormalPrismSolver.TriangleBlendMode.Spherical
                    : NormalPrismSolver.TriangleBlendMode.Linear,
                _allowNearest, GetWorldPositions, out string error);
            sw.Stop();
            LastComputeMilliseconds = sw.Elapsed.TotalMilliseconds;

            if (samples == null)
            {
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = error ?? "法線を計算できませんでした";
                _applySection.style.display = DisplayStyle.None;
                return;
            }

            // プレビューは対象の法線を直接書き換えるので、始める前に担当者判定とロック取得（H-2）。
            if (!_previewLocked && TryLockForPreview != null)
            {
                if (!TryLockForPreview(new List<int>(_targetIndices)))
                {
                    _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                    _statusLabel.text = "他の操作者が作業中のため開始できません";
                    _applySection.style.display = DisplayStyle.None;
                    return;
                }
                _previewLocked = true;
            }

            if (!_preview.Start(_model, samples))
            {
                if (_previewLocked) { _previewLocked = false; UnlockAfterPreview?.Invoke(); }
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = "プレビューを開始できません";
                _applySection.style.display = DisplayStyle.None;
                return;
            }

            _strength = 1f;
            _sliderStrength.SetValueWithoutNotify(1f);
            _sliderValueLabel.text = "1.00";
            ApplyPreview(1f);

            int total = _preview.TotalVertexCount();
            int resolved = _preview.TotalResolvedCount();
            int inside = _preview.TotalInsideCount();

            bool allResolved = resolved >= total;
            _statusLabel.style.color = allResolved
                ? new StyleColor(new Color(0.4f, 0.8f, 1f))
                : new StyleColor(new Color(1f, 0.7f, 0.3f));
            _statusLabel.text =
                $"移植: {resolved} / {total} 頂点（プリズム内 {inside} / 最近傍で補った {resolved - inside} / "
                + $"求まらなかった {total - resolved}）  計算 {LastComputeMilliseconds:F1} ms";

            _applySection.style.display = DisplayStyle.Flex;
            OnRepaint?.Invoke();
        }

        /// <summary>プレビュー中なら破棄して法線を戻す。</summary>
        private void CancelComputation()
        {
            if (_preview.IsActive) EndPreview();
        }

        // ================================================================
        // スライダー
        // ================================================================

        private void OnSliderChanged(float newValue)
        {
            if (_model == null || !_preview.IsActive) return;

            _strength = newValue;
            _sliderValueLabel.text = newValue.ToString("F2");
            ApplyPreview(_strength);
        }

        /// <summary>プレビュー値を書き込み、ターゲットの表示へ反映する。</summary>
        private void ApplyPreview(float strength)
        {
            _preview.Apply(_model, strength);
            SyncTargets();
            OnRepaint?.Invoke();
        }

        private void SyncTargets()
        {
            var samples = _preview.Samples;
            if (samples == null || _model == null) return;

            foreach (var s in samples)
            {
                var ctx = _model.GetMeshContext(s.MasterIndex);
                if (ctx?.MeshObject == null) continue;
                OnSyncMeshNormals?.Invoke(ctx);
            }
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            if (_model == null || !_preview.IsActive) return;

            int beforeIndex = _beforeIndex;
            int afterIndex = _afterIndex;
            float strength = _strength;
            var targets = new List<int>(_targetIndices).ToArray();

            // コマンド側で同じ計算をやり直すため、プレビューは先に破棄して
            // 元法線へ戻しておく（二重適用の防止）。
            EndPreview();

            // 確定はコマンドだけで行う（本体も SetCommandContext を渡す。操作経路統一計画.md J）。
            _panelContext?.SendCommand(new ApplyNormalTransplantCommand(
                _getModelIndex?.Invoke() ?? 0,
                beforeIndex, afterIndex, targets,
                strength, _spherical, _allowNearest));

            _strength = 1f;
            Refresh();
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _strength = 1f;
            _sliderStrength?.SetValueWithoutNotify(1f);
            if (_sliderValueLabel != null) _sliderValueLabel.text = "1.00";
            if (_statusLabel != null) _statusLabel.text = string.Empty;
            Refresh();
        }

        private void EndPreview()
        {
            if (_previewLocked) { _previewLocked = false; UnlockAfterPreview?.Invoke(); }
            if (!_preview.IsActive) return;

            _preview.Restore(_model);
            SyncTargets();
            _preview.End(_model);
            OnRepaint?.Invoke();
        }

        // ================================================================
        // プレビュー中のロック（操作経路統一計画.md H-2）
        // ================================================================

        /// <summary>プレビューを始める前に、対象の担当者判定とロック取得を行う。null なら常に許可。</summary>
        public Func<IList<int>, bool> TryLockForPreview;

        /// <summary>プレビューが終わったときに呼ぶ（ロックを外す）。</summary>
        public Action UnlockAfterPreview;

        private bool _previewLocked;

        // ================================================================
        // ToolContext 生成（最小構成）
        // ================================================================

        private Poly_Ling.Tools.ToolContext BuildToolCtx()
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model = _model;
            ctx.Repaint = OnRepaint;
            ctx.UndoController = GetUndoController?.Invoke();
            ctx.CommandQueue = GetCommandQueue?.Invoke();
            ctx.NotifyTopologyChanged = OnNotifyTopologyChanged;
            return ctx;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Label SecLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginTop = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height = 1;
            v.style.marginTop = 3;
            v.style.marginBottom = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
