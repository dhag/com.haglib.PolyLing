// PlayerPartsIdSubPanel.cs
// パーツID / サブID の採番ツール（診断 + 一括採番）。
//
// 【頂点IDとの分離】
//   このパネルは Vertex.Id を一切触らない。頂点IDは「頂点ID」パネル
//   （PlayerVertexIdSubPanel）が持つ。両者は独立して掛けられる。
//
// 【対象とリファレンス】
//   ・対象は 1 オブジェクトだけ。リファレンスも 1 オブジェクトだけ。
//   ・どちらもこのパネルのドロップダウンで選ぶ。ビューポートの「オブジェクト選択」
//     とは無関係で、選択状態を読まないし書き換えもしない。
//   ・藤壺の配置元が複数オブジェクトだった場合は、あらかじめ 1 つへ結合したものを
//     リファレンスに指定すること（結合はこのパネルの仕事ではない）。
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
    public class PlayerPartsIdSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        /// <summary>対象・リファレンスの候補一覧。Viewer から設定する。</summary>
        public Func<List<(string Label, int MasterIndex, MeshObject Mesh)>> GetDrawableMeshEntryList;

        /// <summary>直近の採番結果。Viewer から設定する（PlayerCommandDispatcher が持つ）。</summary>
        public Func<PartsIdAssignResult> GetLastResult;

        // ── UI ───────────────────────────────────────────────────────

        private DropdownField _targetDrop;
        private DropdownField _referenceDrop;
        private RadioButtonGroup _isolatedGroup;

        private Label _diagLabel;
        private Label _referenceLabel;
        private Label _statusLabel;

        // ドロップダウンの表示名 → masterIndex。表示名は "[masterIndex] 名前" 形式で
        // 一意になるが、辞書で持って添字ずれを起こさないようにする。
        private readonly List<int> _targetIndices    = new List<int>();
        private readonly List<int> _referenceIndices = new List<int>();

        private static readonly List<string> IsolatedChoices =
            new List<string> { "まとめて1パーツ", "1つずつ独立" };

        private ProjectContext GetProject() => GetView?.Invoke();
        private int ModelIndex => GetProject()?.CurrentModelIndex ?? 0;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(PlayerIoUiKit.Title("パーツID / サブID"));

            root.Add(new HelpBox(
                "パーツID（Vertex.PartsId）はオブジェクト内の部品の区別、"
              + "サブID（Vertex.SubId）はパーツ内の通し番号です。\n"
              + "頂点ID（Vertex.Id）とは別物で、このパネルは頂点IDに一切触りません。\n"
              + "対象・リファレンスはここで選びます。ビューポートのオブジェクト選択とは無関係です。",
                HelpBoxMessageType.Info));

            // ── 対象 ─────────────────────────────────────────────────
            root.Add(PlayerIoUiKit.SectionLabel("対象オブジェクト（1つだけ）"));

            _targetDrop = new DropdownField("対象");
            _targetDrop.style.marginBottom = 2;
            _targetDrop.RegisterValueChangedCallback(_ => RefreshDiagnosis());
            root.Add(_targetDrop);

            _diagLabel = Info();
            root.Add(_diagLabel);

            root.Add(PlayerIoUiKit.WideBtn("一覧を再取得 / 再診断", Refresh));

            // ── つながりで採番 ────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("① つながり（独立性）で採番"));

            root.Add(new HelpBox(
                "面と線でつながっている頂点を 1 パーツにします。"
              + "パーツ番号はパーツ内の最小頂点インデックスの昇順で 0 から振ります。\n"
              + "パイプは梯子ごと、藤壺は配置ごとに分かれます。"
              + "フリル（融合あり）は全体が 1 つにつながっているため分かれません。",
                HelpBoxMessageType.Info));

            root.Add(Note("面にも線にも属さない孤立頂点の扱い："));
            _isolatedGroup = new RadioButtonGroup(null, IsolatedChoices) { value = 0 };
            _isolatedGroup.style.marginBottom = 3;
            root.Add(_isolatedGroup);

            root.Add(PlayerIoUiKit.WideBtn("つながりで採番",
                () => Run(AssignPartsIdsCommand.PartsIdMode.Connectivity, "つながりで採番")));

            // ── リファレンスの頂点数で採番 ──────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("② リファレンスの頂点数で採番"));

            root.Add(new HelpBox(
                "1 パーツの頂点数をリファレンスオブジェクトから取り、対象の頂点列を"
              + "先頭から等分してパーツIDを振ります。\n"
              + "対象の頂点数がリファレンスの頂点数で割り切れないときは実行しません。\n"
              + "藤壺の配置元が複数オブジェクトだった場合は、先に 1 つへ結合してください。",
                HelpBoxMessageType.Info));

            _referenceDrop = new DropdownField("リファレンス");
            _referenceDrop.style.marginBottom = 2;
            _referenceDrop.RegisterValueChangedCallback(_ => RefreshReferenceInfo());
            root.Add(_referenceDrop);

            _referenceLabel = Info();
            root.Add(_referenceLabel);

            root.Add(PlayerIoUiKit.WideBtn("リファレンスの頂点数で採番",
                () => Run(AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount,
                          "リファレンスの頂点数で採番")));

            // ── その他 ───────────────────────────────────────────────
            root.Add(PlayerIoUiKit.Divider());
            root.Add(PlayerIoUiKit.SectionLabel("その他"));

            root.Add(PlayerIoUiKit.WideBtn("サブIDだけ振り直し",
                () => Run(AssignPartsIdsCommand.PartsIdMode.SubIdOnly, "サブIDだけ振り直し")));
            root.Add(PlayerIoUiKit.WideBtn("パーツID / サブIDを消去",
                () => Run(AssignPartsIdsCommand.PartsIdMode.Clear, "パーツID / サブIDを消去")));

            _statusLabel = PlayerIoUiKit.StatusLabel();
            root.Add(_statusLabel);

            Refresh();
            PlayerLayoutRoot.ApplyDarkTheme(root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            RebuildDropdowns();
            RefreshDiagnosis();
            RefreshReferenceInfo();
        }

        /// <summary>
        /// 一覧を組み直す。選んでいた masterIndex が残っていればそのまま選び直す。
        /// </summary>
        private void RebuildDropdowns()
        {
            if (_targetDrop == null || _referenceDrop == null) return;

            int keepTarget    = CurrentTargetMasterIndex();
            int keepReference = CurrentReferenceMasterIndex();

            var entries = GetDrawableMeshEntryList?.Invoke()
                       ?? new List<(string, int, MeshObject)>();

            var labels = new List<string>();
            _targetIndices.Clear();
            _referenceIndices.Clear();

            foreach (var e in entries)
            {
                labels.Add(e.Label);
                _targetIndices.Add(e.MasterIndex);
                _referenceIndices.Add(e.MasterIndex);
            }

            _targetDrop.choices    = new List<string>(labels);
            _referenceDrop.choices = new List<string>(labels);

            // 候補が 0 件のときに index を触ると版によって挙動が割れるので、
            // 空のときは表示だけ空にして index には代入しない。
            if (labels.Count == 0)
            {
                _targetDrop.SetValueWithoutNotify("");
                _referenceDrop.SetValueWithoutNotify("");
                return;
            }

            _targetDrop.index    = IndexOfMaster(_targetIndices,    keepTarget);
            _referenceDrop.index = IndexOfMaster(_referenceIndices, keepReference);
        }

        private static int IndexOfMaster(List<int> list, int masterIndex)
        {
            if (list.Count == 0) return -1;
            int at = list.IndexOf(masterIndex);
            return at >= 0 ? at : 0;
        }

        private void RefreshDiagnosis()
        {
            if (_diagLabel == null) return;

            var mo = GetMeshObject(CurrentTargetMasterIndex());
            if (mo == null)
            {
                _diagLabel.text = "対象オブジェクトがありません";
                return;
            }

            var report = PartsIdAssignOps.Inspect(mo, CurrentTargetName());
            _diagLabel.text = report.Summary;
            _diagLabel.style.color = new StyleColor(
                (report.CurrentPartCount == report.ConnectedComponentCount && report.SubIdIsSequential)
                    ? new Color(0.65f, 0.9f, 0.65f)
                    : new Color(1f, 0.7f, 0.4f));
        }

        private void RefreshReferenceInfo()
        {
            if (_referenceLabel == null) return;

            var target = GetMeshObject(CurrentTargetMasterIndex());
            var mo     = GetMeshObject(CurrentReferenceMasterIndex());
            if (mo == null)
            {
                _referenceLabel.text = "リファレンスオブジェクトがありません";
                return;
            }

            int per = mo.VertexCount;
            if (target == null || per <= 0)
            {
                _referenceLabel.text = $"1 パーツの頂点数 {per}";
                return;
            }

            int amari = target.VertexCount % per;
            _referenceLabel.text =
                $"1 パーツの頂点数 {per} / 対象の頂点数 {target.VertexCount} → "
              + (amari == 0 ? $"{target.VertexCount / per} パーツ" : $"割り切れません（余り {amari}）");
            _referenceLabel.style.color = new StyleColor(
                amari == 0 ? new Color(0.65f, 0.9f, 0.65f) : new Color(1f, 0.7f, 0.4f));
        }

        // ================================================================
        // 実行
        // ================================================================

        private void Run(AssignPartsIdsCommand.PartsIdMode mode, string label)
        {
            int target = CurrentTargetMasterIndex();
            if (target < 0) { SetStatus("対象オブジェクトを選んでください"); return; }

            int reference = (mode == AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount)
                ? CurrentReferenceMasterIndex()
                : -1;

            if (mode == AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount)
            {
                if (reference < 0) { SetStatus("リファレンスオブジェクトを選んでください"); return; }
                if (reference == target)
                {
                    SetStatus("リファレンスに対象と同じオブジェクトは指定できません");
                    return;
                }
            }

            var isolated = (_isolatedGroup != null && _isolatedGroup.value == 1)
                ? IsolatedVertexPolicy.SeparateParts
                : IsolatedVertexPolicy.SingleGroup;

            SendCommand?.Invoke(new AssignPartsIdsCommand(
                ModelIndex, target, mode, reference, isolated));

            // Dispatch は同期なので、実行後の状態をそのまま診断し直せる。
            Refresh();

            var r = GetLastResult != null ? GetLastResult() : default;
            SetStatus(r.Success
                ? $"{label}: パーツ {r.PartCount} / 頂点 {r.VertexCount}"
                  + (r.IsolatedVertexCount > 0 ? $" / 孤立頂点 {r.IsolatedVertexCount}" : "")
                : $"{label}: 実行しませんでした"
                  + $"（{(string.IsNullOrEmpty(r.Reason) ? "理由不明" : r.Reason)}）");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private int CurrentTargetMasterIndex()
        {
            if (_targetDrop == null) return -1;
            int i = _targetDrop.index;
            if (i < 0 || i >= _targetIndices.Count) return -1;
            return _targetIndices[i];
        }

        private int CurrentReferenceMasterIndex()
        {
            if (_referenceDrop == null) return -1;
            int i = _referenceDrop.index;
            if (i < 0 || i >= _referenceIndices.Count) return -1;
            return _referenceIndices[i];
        }

        private string CurrentTargetName()
        {
            var mc = GetMeshContext(CurrentTargetMasterIndex());
            return mc?.Name ?? "(no name)";
        }

        private MeshContext GetMeshContext(int masterIndex)
        {
            if (masterIndex < 0) return null;
            return GetProject()?.CurrentModel?.GetMeshContext(masterIndex);
        }

        private MeshObject GetMeshObject(int masterIndex) => GetMeshContext(masterIndex)?.MeshObject;

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label Info()
        {
            var l = new Label("");
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            return l;
        }

        private static Label Note(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 9;
            l.style.color        = new StyleColor(new Color(0.6f, 0.8f, 1f));
            l.style.marginTop    = 2;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
