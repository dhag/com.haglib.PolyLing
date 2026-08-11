// PlayerVertexTransferSubPanel.cs
// モデル間・オブジェクト間の頂点データ転送パネル。
//
// 【手作業のしやすさを最優先にした設計】
//   既存の部分インポート UI は「左右 2 列のチェックボックス」で、実際の対応付けは
//   チェックした順序（リスト順）だった。順序が見えないため手で細かく合わせにくい。
//   ここでは「1 行 = 1 ペア」を画面に出し、各行で転送元・転送先を個別に選ぶ。
//   自動マッチは候補を埋めるだけで、埋めた後は自由に手で直せる。
//
// 【頂点IDについて】
//   IDは未設定・重複・誤付与が起きやすいので、既定はインデックス対応。
//   ID を選んだ場合は実行前に対応件数を出して確認できるようにする。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerVertexTransferSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>1 行 = 1 ペア。値は各モデルの MeshContextList インデックス。</summary>
        private class Pair
        {
            public int SourceMeshIndex = -1;
            public int TargetMeshIndex = -1;
        }

        private readonly List<Pair> _pairs = new List<Pair>();

        private int _sourceModelIndex = -1;
        private int _targetModelIndex = -1;

        private VertexMatchMode _matchMode = VertexMatchMode.Index;
        private VertexDataKind  _kinds     = VertexDataKind.Position;

        // UI
        private DropdownField _srcModelDrop, _dstModelDrop;
        private VisualElement _pairRows;
        private VisualElement _previewBox;
        private Label         _statusLabel;
        private readonly Dictionary<VertexDataKind, Toggle> _kindToggles = new Dictionary<VertexDataKind, Toggle>();

        private ProjectContext GetProject() => GetView?.Invoke();

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(PlayerIoUiKit.Title("頂点データ転送"));

            var help = new Label(
                "プロジェクト内のモデル間でメッシュを 1 対 1 に対応付け、頂点データを転送します。"
              + "メッシュの頂点数・面構成は変更しません。");
            help.style.fontSize     = 9;
            help.style.whiteSpace   = WhiteSpace.Normal;
            help.style.color        = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            help.style.marginBottom = 4;
            root.Add(help);

            // ── モデル選択 ────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("モデル"));
            _srcModelDrop = new DropdownField("転送元");
            _srcModelDrop.RegisterValueChangedCallback(_ =>
            {
                _sourceModelIndex = _srcModelDrop.index;
                RebuildPairRows();
                UpdatePreview();
            });
            root.Add(_srcModelDrop);

            _dstModelDrop = new DropdownField("転送先");
            _dstModelDrop.RegisterValueChangedCallback(_ =>
            {
                _targetModelIndex = _dstModelDrop.index;
                RebuildPairRows();
                UpdatePreview();
            });
            root.Add(_dstModelDrop);

            // ── メッシュペア ──────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("メッシュのペア（1 行 = 1 対応）"));

            var pairBtnRow = new VisualElement();
            pairBtnRow.style.flexDirection = FlexDirection.Row;
            pairBtnRow.style.marginBottom  = 2;
            AddSmallBtn(pairBtnRow, "行を追加", () => { _pairs.Add(new Pair()); RebuildPairRows(); UpdatePreview(); });
            AddSmallBtn(pairBtnRow, "自動マッチ", AutoMatch);
            AddSmallBtn(pairBtnRow, "全消去",  () => { _pairs.Clear(); RebuildPairRows(); UpdatePreview(); });
            root.Add(pairBtnRow);

            _pairRows = new VisualElement();
            root.Add(_pairRows);

            // ── 頂点の対応付け ────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("頂点の対応付け"));

            var matchDrop = new DropdownField(
                "方式",
                new List<string> { "インデックス", "頂点ID" },
                _matchMode == VertexMatchMode.Index ? 0 : 1);
            matchDrop.RegisterValueChangedCallback(_ =>
            {
                _matchMode = matchDrop.index == 0 ? VertexMatchMode.Index : VertexMatchMode.VertexId;
                UpdatePreview();
            });
            root.Add(matchDrop);

            var matchNote = new Label(
                "頂点IDは未設定・重複があると対応が取れません。"
              + "使う前に「頂点ID」パネルで状態を確認してください。");
            matchNote.style.fontSize     = 9;
            matchNote.style.whiteSpace   = WhiteSpace.Normal;
            matchNote.style.color        = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            matchNote.style.marginBottom = 2;
            root.Add(matchNote);

            // ── 転送項目 ──────────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("転送する項目"));

            AddKindToggle(root, VertexDataKind.Position,        "頂点位置", null);
            AddKindToggle(root, VertexDataKind.UVs,             "UV ※重い",
                "面が参照するUVスロット番号が範囲外になった場合は 0 に補正します。"
              + "GPUバッファの再構築を伴います");
            AddKindToggle(root, VertexDataKind.Normals,         "法線 ※重い",
                "面が参照する法線スロット番号が範囲外になった場合は 0 に補正します。"
              + "GPUバッファの再構築を伴います");
            AddKindToggle(root, VertexDataKind.Flags,           "頂点フラグ ※重い",
                "GPUバッファの再構築を伴います");
            AddKindToggle(root, VertexDataKind.BoneWeight,      "ボーンウェイト ※重い",
                "ボーン番号はモデルごとに異なるため、ボーン名で引き直します。"
              + "名前が一致しないボーンを含む頂点は転送しません。"
              + "GPUバッファの再構築を伴います");
            AddKindToggle(root, VertexDataKind.MirrorBoneWeight,"ミラーウェイト ※重い",
                "GPUバッファの再構築を伴います");
            AddKindToggle(root, VertexDataKind.VertexId,        "頂点ID",
                "インデックス対応のときだけ意味があります。転送後に重複が出れば警告します");
            AddKindToggle(root, VertexDataKind.MorphBase,       "モーフ基準データ",
                "対応の取れなかった頂点は転送先の現在値を保ちます");
            AddKindToggle(root, VertexDataKind.PartsSelectionSet, "パーツ選択辞書",
                "頂点・辺のみ引き継ぎます。面/線分ベースの辞書は読み替えられないため除外します");

            var costNote = new Label(
                "※重い項目はGPUバッファ構築時に焼き込まれるため、転送後に全体の再構築が要ります。"
              + "頂点位置のみなら差分同期で済みます。頂点ID・モーフ基準・選択辞書は描画に出ないため再構築なしです。");
            costNote.style.fontSize     = 9;
            costNote.style.whiteSpace   = WhiteSpace.Normal;
            costNote.style.color        = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            costNote.style.marginTop    = 2;
            root.Add(costNote);

            // ── プレビューと実行 ──────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("確認"));

            _previewBox = new VisualElement();
            root.Add(_previewBox);

            root.Add(PlayerIoUiKit.WideBtn("対応を確認", UpdatePreview));
            root.Add(PlayerIoUiKit.Spacer());
            root.Add(PlayerIoUiKit.WideBtn("転送を実行", Execute));

            _statusLabel = PlayerIoUiKit.StatusLabel();
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var project = GetProject();
            if (project == null || _srcModelDrop == null) return;

            var names = new List<string>();
            for (int i = 0; i < project.ModelCount; i++)
                names.Add($"[{i}] {project.GetModel(i)?.Name ?? "?"}");

            if (names.Count == 0) names.Add("(モデルなし)");

            _srcModelDrop.choices = names;
            _dstModelDrop.choices = names;

            if (_sourceModelIndex < 0 || _sourceModelIndex >= names.Count)
                _sourceModelIndex = Mathf.Clamp(project.CurrentModelIndex, 0, names.Count - 1);
            if (_targetModelIndex < 0 || _targetModelIndex >= names.Count)
                _targetModelIndex = Mathf.Clamp(project.CurrentModelIndex, 0, names.Count - 1);

            _srcModelDrop.index = _sourceModelIndex;
            _dstModelDrop.index = _targetModelIndex;

            RebuildPairRows();
            UpdatePreview();
        }

        // ================================================================
        // ペア行
        // ================================================================

        private void RebuildPairRows()
        {
            if (_pairRows == null) return;
            _pairRows.Clear();

            var srcNames = BuildMeshChoices(_sourceModelIndex);
            var dstNames = BuildMeshChoices(_targetModelIndex);

            if (srcNames.Count == 0 || dstNames.Count == 0)
            {
                var lbl = new Label("メッシュがありません");
                lbl.style.fontSize = 9;
                _pairRows.Add(lbl);
                PlayerLayoutRoot.ApplyDarkTheme(_pairRows);
                return;
            }

            for (int i = 0; i < _pairs.Count; i++)
            {
                var pair = _pairs[i];   // capture
                int rowIndex = i;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom  = 1;

                var srcDrop = new DropdownField { choices = srcNames };
                srcDrop.style.flexGrow = 1;
                srcDrop.index = IndexOfChoice(srcNames, _sourceModelIndex, pair.SourceMeshIndex);
                srcDrop.RegisterValueChangedCallback(_ =>
                {
                    pair.SourceMeshIndex = ChoiceToMeshIndex(_sourceModelIndex, srcDrop.index);
                    UpdatePreview();
                });

                var arrow = new Label(" → ");
                arrow.style.fontSize        = 9;
                arrow.style.unityTextAlign  = TextAnchor.MiddleCenter;

                var dstDrop = new DropdownField { choices = dstNames };
                dstDrop.style.flexGrow = 1;
                dstDrop.index = IndexOfChoice(dstNames, _targetModelIndex, pair.TargetMeshIndex);
                dstDrop.RegisterValueChangedCallback(_ =>
                {
                    pair.TargetMeshIndex = ChoiceToMeshIndex(_targetModelIndex, dstDrop.index);
                    UpdatePreview();
                });

                var del = new Button(() => { _pairs.RemoveAt(rowIndex); RebuildPairRows(); UpdatePreview(); })
                    { text = "×" };
                del.style.width = 20;

                row.Add(srcDrop); row.Add(arrow); row.Add(dstDrop); row.Add(del);
                _pairRows.Add(row);
            }

            if (_pairs.Count == 0)
            {
                var lbl = new Label("「行を追加」または「自動マッチ」でペアを作ってください");
                lbl.style.fontSize   = 9;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                _pairRows.Add(lbl);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_pairRows);
        }

        /// <summary>
        /// 自動マッチ。名前一致 → 頂点数一致 の順で候補を埋める。
        /// あくまで候補なので、埋めた後は各行で自由に付け替えられる。
        /// </summary>
        private void AutoMatch()
        {
            var project = GetProject();
            var srcModel = project?.GetModel(_sourceModelIndex);
            var dstModel = project?.GetModel(_targetModelIndex);
            if (srcModel == null || dstModel == null) { SetStatus("モデルが選択されていません"); return; }

            var srcList = CollectMeshIndices(srcModel);
            var dstList = CollectMeshIndices(dstModel);

            _pairs.Clear();
            var usedDst = new HashSet<int>();

            // Pass 1: 名前一致
            foreach (int si in srcList)
            {
                string sName = srcModel.GetMeshContext(si)?.Name;
                if (string.IsNullOrEmpty(sName)) continue;
                foreach (int di in dstList)
                {
                    if (usedDst.Contains(di)) continue;
                    if (dstModel.GetMeshContext(di)?.Name != sName) continue;
                    _pairs.Add(new Pair { SourceMeshIndex = si, TargetMeshIndex = di });
                    usedDst.Add(di);
                    break;
                }
            }

            // Pass 2: 頂点数一致（未マッチのみ）
            var pairedSrc = new HashSet<int>();
            foreach (var p in _pairs) pairedSrc.Add(p.SourceMeshIndex);

            foreach (int si in srcList)
            {
                if (pairedSrc.Contains(si)) continue;
                int sCount = srcModel.GetMeshContext(si)?.MeshObject?.VertexCount ?? 0;
                if (sCount == 0) continue;
                foreach (int di in dstList)
                {
                    if (usedDst.Contains(di)) continue;
                    if ((dstModel.GetMeshContext(di)?.MeshObject?.VertexCount ?? -1) != sCount) continue;
                    _pairs.Add(new Pair { SourceMeshIndex = si, TargetMeshIndex = di });
                    usedDst.Add(di);
                    break;
                }
            }

            RebuildPairRows();
            UpdatePreview();
            SetStatus($"自動マッチ: {_pairs.Count} ペアの候補を作りました（手で直せます）");
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void UpdatePreview()
        {
            if (_previewBox == null) return;
            _previewBox.Clear();

            var project  = GetProject();
            var srcModel = project?.GetModel(_sourceModelIndex);
            var dstModel = project?.GetModel(_targetModelIndex);
            if (srcModel == null || dstModel == null) return;

            int totalMatched = 0, totalUnmatched = 0, validPairs = 0;

            foreach (var pair in _pairs)
            {
                var srcMc = srcModel.GetMeshContext(pair.SourceMeshIndex);
                var dstMc = dstModel.GetMeshContext(pair.TargetMeshIndex);
                if (srcMc?.MeshObject == null || dstMc?.MeshObject == null) continue;

                var r = VertexDataTransferOps.Preview(srcMc, dstMc, _matchMode);
                totalMatched   += r.Matched;
                totalUnmatched += r.Unmatched;
                validPairs++;

                var lbl = new Label("  " + r.Summary);
                lbl.style.fontSize   = 9;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                lbl.style.color      = new StyleColor(r.Unmatched == 0
                    ? new Color(0.65f, 0.9f, 0.65f)
                    : new Color(1f, 0.7f, 0.4f));
                _previewBox.Add(lbl);
            }

            var total = new Label(
                $"{validPairs} ペア / 対応 {totalMatched} 頂点 / 未対応 {totalUnmatched} 頂点");
            total.style.fontSize    = 10;
            total.style.whiteSpace  = WhiteSpace.Normal;
            total.style.marginTop   = 2;
            total.style.color       = new StyleColor(totalUnmatched == 0
                ? new Color(0.65f, 0.9f, 0.65f)
                : new Color(1f, 0.7f, 0.4f));
            _previewBox.Add(total);

            PlayerLayoutRoot.ApplyDarkTheme(_previewBox);
        }

        // ================================================================
        // 実行
        // ================================================================

        private void Execute()
        {
            var project = GetProject();
            if (project == null) { SetStatus("プロジェクトがありません"); return; }
            if (_kinds == VertexDataKind.None) { SetStatus("転送項目を選択してください"); return; }

            var srcIdx = new List<int>();
            var dstIdx = new List<int>();
            var srcModel = project.GetModel(_sourceModelIndex);
            var dstModel = project.GetModel(_targetModelIndex);
            if (srcModel == null || dstModel == null) { SetStatus("モデルが選択されていません"); return; }

            foreach (var pair in _pairs)
            {
                if (srcModel.GetMeshContext(pair.SourceMeshIndex)?.MeshObject == null) continue;
                if (dstModel.GetMeshContext(pair.TargetMeshIndex)?.MeshObject == null) continue;
                srcIdx.Add(pair.SourceMeshIndex);
                dstIdx.Add(pair.TargetMeshIndex);
            }

            if (srcIdx.Count == 0) { SetStatus("有効なペアがありません"); return; }

            SendCommand?.Invoke(new TransferVertexDataCommand(
                _sourceModelIndex, _targetModelIndex,
                srcIdx.ToArray(), dstIdx.ToArray(),
                _matchMode, _kinds));

            UpdatePreview();

            // 転送先がカレントモデルでないときは、今の画面には反映されない
            // （描画中の adapter は別モデルのもの。モデルを切り替えた時点で作り直される）。
            if (_targetModelIndex != project.CurrentModelIndex)
                SetStatus($"{srcIdx.Count} ペアを転送しました。"
                        + "転送先はカレントモデルではないため、切り替えるまで画面には出ません");
            else
                SetStatus($"{srcIdx.Count} ペアを転送しました（詳細はログを参照）");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>転送の対象にする描画メッシュとモーフメッシュの MeshContextList インデックス。</summary>
        private List<int> CollectMeshIndices(ModelContext model)
        {
            var list = new List<int>();
            if (model?.MeshContextList == null) return list;
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var mc = model.MeshContextList[i];
                if (mc?.MeshObject == null) continue;
                // ボーン等の非メッシュは除外。モーフは対象に含める
                // （モーフ基準データの転送を手動ペアで行えるようにするため）。
                if (mc.Type != MeshType.Mesh && mc.Type != MeshType.BakedMirror && mc.Type != MeshType.Morph)
                    continue;
                list.Add(i);
            }
            return list;
        }

        private List<string> BuildMeshChoices(int modelIndex)
        {
            var names = new List<string>();
            var model = GetProject()?.GetModel(modelIndex);
            if (model == null) return names;
            foreach (int i in CollectMeshIndices(model))
            {
                var mc = model.GetMeshContext(i);
                names.Add($"[{i}] {mc?.Name ?? "?"} ({mc?.MeshObject?.VertexCount ?? 0})");
            }
            return names;
        }

        /// <summary>選択肢の並び順 → MeshContextList インデックス。</summary>
        private int ChoiceToMeshIndex(int modelIndex, int choiceIndex)
        {
            var model = GetProject()?.GetModel(modelIndex);
            if (model == null) return -1;
            var list = CollectMeshIndices(model);
            return (choiceIndex >= 0 && choiceIndex < list.Count) ? list[choiceIndex] : -1;
        }

        /// <summary>MeshContextList インデックス → 選択肢の並び順。</summary>
        private int IndexOfChoice(List<string> choices, int modelIndex, int meshIndex)
        {
            var model = GetProject()?.GetModel(modelIndex);
            if (model == null || meshIndex < 0) return 0;
            var list = CollectMeshIndices(model);
            int at = list.IndexOf(meshIndex);
            return at >= 0 ? at : 0;
        }

        private void AddKindToggle(VisualElement parent, VertexDataKind kind, string label, string tooltip)
        {
            var tog = new Toggle(label) { value = _kinds.HasFlag(kind) };
            if (!string.IsNullOrEmpty(tooltip)) tog.tooltip = tooltip;
            tog.RegisterValueChangedCallback(e =>
            {
                if (e.newValue) _kinds |= kind;
                else            _kinds &= ~kind;
            });
            _kindToggles[kind] = tog;
            parent.Add(tog);
        }

        private static void AddSmallBtn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow    = 1;
            b.style.fontSize    = 9;
            b.style.marginRight = 2;
            parent.Add(b);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
    }
}
