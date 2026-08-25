// PlayerSkinKindSubPanel.cs
// 描画オブジェクトの種別（MeshFilter 系 / SkinnedMesh 系）を切り替えるパネル。
// Runtime/Poly_Ling_Player/View/SkinKind/ に配置
//
// 【MeshFilterToSkinnedSubPanel との住み分け】
//   あちらは「モデル全体からボーン階層を生成する」一括変換で、ボーンが 1 本でも
//   あると実行できない。こちらは既にボーンがあるモデルで、選んだオブジェクトだけを
//   種別変換する。ボーンの生成・破棄は行わない。
//
// 【できること】
//   1. ウェイトを破棄して MeshFilter 系へ戻す（階層の扱いをチェックボックスで選択）
//   2. 選んだボーンへウェイト 1.0 でバインドして SkinnedMesh 系にする
//   3. ボーンの左右対応（MirrorBoneIndex）を名前から補完する
//   4. ミラーを生成する（既存の SetMirrorEnabledCommand を送るだけ）

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 描画オブジェクトの種別変換パネル。
    /// </summary>
    public class PlayerSkinKindSubPanel
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelContext;
        private System.Func<int> _getModelIndex;
        private ModelContext _model;

        public void SetContext(PanelContext ctx, System.Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        public void SetModel(ModelContext model)
        {
            _model = model;
            Refresh();
        }

        // ================================================================
        // UI
        // ================================================================

        private VisualElement _targetList;
        private Label         _statusLabel;

        // MeshFilter へ戻す
        private Toggle _keepParentToggle;
        private Button _toMeshFilterBtn;

        // スキンド化
        private DropdownField _boneDropdown;
        private Button        _toSkinnedBtn;

        // 左右ボーン対応 / ミラー
        private Label  _mirrorBoneLabel;
        private Button _resolveMirrorBoneBtn;
        private Button _createMirrorBtn;

        /// <summary>ドロップダウンの並び順に対応する MeshContextList 索引。</summary>
        private readonly List<int> _boneMasterIndices = new List<int>();

        public void Build(VisualElement root)
        {
            if (root == null) return;
            root.Clear();

            root.Add(Header("描画オブジェクトの種別"));

            AddNote(root,
                "MeshFilter 系は頂点がローカル空間で WorldMatrix を直接使い、"
                + "SkinnedMesh 系は頂点がワールド（バインド）空間で SkinningMatrix を通ります。"
                + "変換のたびに頂点を焼き直すため、見た目は静止状態で保たれます。");

            // ── 対象一覧 ────────────────────────────────────────────
            root.Add(SubHeader("対象（選択中の描画オブジェクト）"));

            _targetList = new VisualElement();
            _targetList.style.marginBottom = 4;
            root.Add(_targetList);

            // ── MeshFilter へ戻す ──────────────────────────────────
            root.Add(SubHeader("ウェイトを破棄して MeshFilter 系へ戻す"));

            _keepParentToggle = new Toggle("親（ボーン）のまま残す") { value = false };
            _keepParentToggle.style.marginBottom = 2;
            _keepParentToggle.tooltip =
                "OFF: ルート直下へ移す（既定）。ボーンから完全に切り離します。\n"
                + "ON : 現在の親のまま残す。ボーンに剛体追従する MeshFilter になります。";
            root.Add(_keepParentToggle);

            _toMeshFilterBtn = MakeBtn("MeshFilter 系へ戻す");
            _toMeshFilterBtn.clicked += OnToMeshFilterClicked;
            root.Add(_toMeshFilterBtn);

            AddNote(root,
                "ウェイトは失われます。元に戻すには Undo を使ってください。");

            // ── スキンド化 ────────────────────────────────────────
            root.Add(SubHeader("既存ボーンへバインドして SkinnedMesh 系にする"));

            _boneDropdown = new DropdownField("バインド先ボーン", new List<string>(), 0);
            _boneDropdown.style.marginBottom = 2;
            root.Add(_boneDropdown);

            _toSkinnedBtn = MakeBtn("スキンド化");
            _toSkinnedBtn.clicked += OnToSkinnedClicked;
            root.Add(_toSkinnedBtn);

            AddNote(root,
                "全頂点にウェイト 1.0 を与えます。細かい配分はウェイトペイントで行ってください。");

            // ── ミラー ───────────────────────────────────────────
            root.Add(SubHeader("スキンドメッシュのミラーリング"));

            _mirrorBoneLabel = new Label("");
            _mirrorBoneLabel.style.fontSize   = 10;
            _mirrorBoneLabel.style.whiteSpace = WhiteSpace.Normal;
            _mirrorBoneLabel.style.marginBottom = 2;
            root.Add(_mirrorBoneLabel);

            _resolveMirrorBoneBtn = MakeBtn("左右ボーン対応を名前から補完");
            _resolveMirrorBoneBtn.clicked += OnResolveMirrorBoneClicked;
            root.Add(_resolveMirrorBoneBtn);

            AddNote(root,
                "左右対応が無いままミラーを作ると、右のメッシュが左のボーンで動きます。"
                + "スキンド変換が確定させた対応は上書きしません。");

            _createMirrorBtn = MakeBtn("ミラーを生成");
            _createMirrorBtn.clicked += OnCreateMirrorClicked;
            root.Add(_createMirrorBtn);

            // ── ステータス ───────────────────────────────────────
            _statusLabel = new Label("");
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            root.Add(_statusLabel);

            Refresh();
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            if (_targetList == null) return;

            _targetList.Clear();
            RefreshBoneDropdown();
            RefreshMirrorBoneLabel();

            var targets = CollectTargets();
            if (targets.Count == 0)
            {
                AddInfo(_targetList, "描画オブジェクトが選択されていません。", warning: true);
                SetButtonsEnabled(false);
                return;
            }

            int skinned = 0, meshFilter = 0;
            foreach (int idx in targets)
            {
                var mc = _model.GetMeshContext(idx);
                if (mc == null) continue;

                bool isSkinned = mc.IsSkinned;
                if (isSkinned) skinned++; else meshFilter++;

                // 種別と、実頂点のウェイト有無を分けて出す。
                // 明示状態なので「Skinned だがウェイト 0」が正常に起こり得る。
                string weightNote = mc.MeshObject.AnyVertexHasBoneWeight()
                    ? "ウェイト有"
                    : "ウェイト無";

                AddInfo(_targetList,
                    $"  \"{mc.Name}\"  種別={(isSkinned ? "Skinned" : "MeshFilter")}  {weightNote}"
                    + $"  頂点={mc.MeshObject.VertexCount}",
                    warning: false);
            }

            SetButtonsEnabled(true);
            _toMeshFilterBtn.SetEnabled(skinned > 0);
            _toSkinnedBtn.SetEnabled(meshFilter > 0 && _boneMasterIndices.Count > 0);
            _createMirrorBtn.SetEnabled(targets.Count > 0);
        }

        private void RefreshBoneDropdown()
        {
            if (_boneDropdown == null) return;

            _boneMasterIndices.Clear();
            var names = new List<string>();

            if (_model != null)
            {
                for (int i = 0; i < _model.MeshContextCount; i++)
                {
                    var mc = _model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    _boneMasterIndices.Add(i);
                    names.Add(string.IsNullOrEmpty(mc.Name) ? $"#{i}" : mc.Name);
                }
            }

            _boneDropdown.choices = names;
            if (names.Count > 0)
            {
                int keep = _boneDropdown.index;
                _boneDropdown.index = (keep >= 0 && keep < names.Count) ? keep : 0;
            }
            else
            {
                _boneDropdown.index = -1;
                _boneDropdown.value = "（ボーンがありません）";
            }
        }

        private void RefreshMirrorBoneLabel()
        {
            if (_mirrorBoneLabel == null) return;

            if (_model == null) { _mirrorBoneLabel.text = ""; return; }

            int bones = 0, paired = 0;
            for (int i = 0; i < _model.MeshContextCount; i++)
            {
                var mc = _model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                bones++;
                if (mc.MirrorBoneIndex >= 0) paired++;
            }

            _mirrorBoneLabel.text = $"左右対応のあるボーン: {paired} / {bones}";
            _mirrorBoneLabel.style.color = new StyleColor(
                (bones > 0 && paired == 0)
                    ? new Color(1f, 0.7f, 0.4f)
                    : new Color(0.75f, 0.9f, 0.75f));
        }

        private void SetButtonsEnabled(bool on)
        {
            _toMeshFilterBtn?.SetEnabled(on);
            _toSkinnedBtn?.SetEnabled(on);
            _createMirrorBtn?.SetEnabled(on);
        }

        // ================================================================
        // 対象の収集
        // ================================================================

        /// <summary>
        /// 選択中の描画オブジェクトのうち、種別変換できるものだけを返す。
        /// ミラー側は実体側と対で扱う必要があるため SkinKindConverter が弾く。
        /// </summary>
        private List<int> CollectTargets()
        {
            var list = new List<int>();
            if (_model == null) return list;

            foreach (int idx in _model.SelectedDrawableMeshIndices)
            {
                var mc = _model.GetMeshContext(idx);
                if (SkinKindConverter.IsConvertible(mc)) list.Add(idx);
            }
            return list;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnToMeshFilterClicked()
        {
            var targets = CollectTargets();
            if (targets.Count == 0) { SetStatus("対象がありません。", true); return; }

            var mode = _keepParentToggle != null && _keepParentToggle.value
                ? UnskinParentMode.KeepParent
                : UnskinParentMode.MoveToRoot;

            Send(new ConvertToMeshFilterCommand(ModelIndex(), targets.ToArray(), mode));
            SetStatus($"MeshFilter 系へ戻しています（{targets.Count} 件、"
                      + $"{(mode == UnskinParentMode.KeepParent ? "親を維持" : "ルート直下")}）。", false);
        }

        private void OnToSkinnedClicked()
        {
            var targets = CollectTargets();
            if (targets.Count == 0) { SetStatus("対象がありません。", true); return; }

            int di = _boneDropdown?.index ?? -1;
            if (di < 0 || di >= _boneMasterIndices.Count)
            {
                SetStatus("バインド先ボーンを選んでください。", true);
                return;
            }

            int boneIdx = _boneMasterIndices[di];
            Send(new ConvertToSkinnedCommand(ModelIndex(), targets.ToArray(), boneIdx));
            SetStatus($"スキンド化しています（{targets.Count} 件 → \"{_boneDropdown.value}\"）。", false);
        }

        private void OnResolveMirrorBoneClicked()
        {
            if (_model == null) { SetStatus("モデルがありません。", true); return; }

            // 実行前に見込みを出しておく。コマンドは非同期に処理されるため、
            // ここでは結果本数を確定できない。
            Send(new ResolveMirrorBoneIndexCommand(ModelIndex()));
            SetStatus("左右ボーン対応を補完しています。", false);
        }

        private void OnCreateMirrorClicked()
        {
            var targets = CollectTargets();
            if (targets.Count == 0) { SetStatus("対象がありません。", true); return; }

            Send(new SetMirrorEnabledCommand(ModelIndex(), targets.ToArray(), true));
            SetStatus($"ミラーを生成しています（{targets.Count} 件）。", false);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private int ModelIndex() => _getModelIndex?.Invoke() ?? 0;

        private void Send(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        private void SetStatus(string text, bool warning)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text ?? "";
            _statusLabel.style.color = new StyleColor(warning
                ? new Color(1f, 0.7f, 0.4f)
                : new Color(0.75f, 0.9f, 0.75f));
        }

        private static void AddInfo(VisualElement parent, string text, bool warning)
        {
            var l = new Label(text);
            l.style.fontSize   = 10;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.color = new StyleColor(warning
                ? new Color(1f, 0.7f, 0.4f)
                : new Color(0.8f, 0.85f, 0.9f));
            parent.Add(l);
        }

        private static void AddNote(VisualElement parent, string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.color        = new StyleColor(new Color(0.65f, 0.65f, 0.65f));
            l.style.marginBottom = 4;
            parent.Add(l);
        }

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize     = 11;
            l.style.marginBottom = 4;
            return l;
        }

        private static Label SubHeader(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize     = 10;
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            return l;
        }

        private static Button MakeBtn(string text)
        {
            var b = new Button { text = text };
            b.style.marginBottom  = 2;
            b.style.fontSize      = 10;
            b.style.height        = 20;
            b.style.paddingTop    = 0;
            b.style.paddingBottom = 0;
            return b;
        }
    }
}
