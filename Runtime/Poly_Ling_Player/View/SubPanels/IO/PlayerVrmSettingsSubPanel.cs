// PlayerVrmSettingsSubPanel.cs
// VRM 1.0 の出力設定（メタ情報・視線・一人称）の編集。
// Runtime/Poly_Ling_Player/View/SubPanels/IO/ に配置
//
// 【なぜ要るか】
//   メタ情報は出力パネルが持つ Vrm10ExportSettings にしか入らず、
//   アプリを閉じると消えていた。視線と一人称は PolyLing 側に値そのものが
//   無く、UniVRM の既定だけが出ていた。ここで決めた値はモデルに保存される。
//
// 【出力パネルとの関係】
//   「エクスポート」の VRM 欄は出力ごとの上書きで、空欄なら
//   ここで決めた値が使われる（Vrm10ExporterImpl.BuildMeta が正典）。
//   恒久的に持たせたい値はこちらへ入れる。
//
// 【一人称の対象は「選択」で決める】
//   3D 画面・メッシュリストと同じ描画オブジェクトの選択を対象にする。
//   ボーンには一人称の意味がないので対象外（VrmSettingsOps.IsFirstPersonCarrier）。
//
// 【入力欄を Refresh で書き換えない】
//   Refresh は選択やモデル変更のたびに走る。入力中の欄を毎回上書きすると
//   打ち込んだ値が消えるため、欄へ値を入れるのは
//   「読み込みボタンを押したとき」と「モデルが変わったとき」だけにする。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerVrmSettingsSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ProjectContext> GetProject;
        public Action<PanelCommand> SendCommand;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private ProjectContext GetProj      => GetProject?.Invoke();
        private ModelContext   CurrentModel => GetProj?.CurrentModel;
        private int            ModelIndex   => GetProj?.CurrentModelIndex ?? 0;

        // ================================================================
        // UI（メタ情報）
        // ================================================================

        private Label     _statusLabel;
        private Label     _stateLabel;

        private TextField _nameField, _versionField, _authorsField;
        private TextField _copyrightField, _contactField, _referencesField;
        private TextField _thirdPartyField, _thumbnailField;

        private DropdownField _permissionField, _commercialField;
        private DropdownField _creditField, _modificationField;
        private Toggle _violentToggle, _sexualToggle, _politicalToggle, _antisocialToggle;
        private Toggle _redistributionToggle;
        private TextField _otherLicenseField;

        // ================================================================
        // UI（視線）
        // ================================================================

        private FloatField _offX, _offY, _offZ;
        private DropdownField _lookAtTypeField;
        private FloatField _hiIn, _hiOut, _hoIn, _hoOut;
        private FloatField _vdIn, _vdOut, _vuIn, _vuOut;
        private Label _lookAtStateLabel;

        // ================================================================
        // UI（一人称）
        // ================================================================

        private Label         _fpTargetLabel;
        private DropdownField _fpTypeField;
        private ListView      _fpListView;

        private readonly List<string> _fpRows = new List<string>();
        private readonly List<int>    _fpRowRefs = new List<int>();
        private int  _fpSelectedRow = -1;
        private bool _suppressListEvents;

        /// <summary>直前に欄へ読み込んだモデル。変わったら読み直す。</summary>
        private ModelContext _loadedModel;

        // ================================================================
        // 選択肢（並びは Poly_Ling.Data の各 enum の値の順と一致させること）
        // ================================================================

        private static readonly string[] PermissionChoices =
        { "作者のみ", "別途許諾を得た人のみ", "誰でも" };

        private static readonly string[] CommercialChoices =
        { "個人・非営利のみ", "個人の営利まで可", "法人の利用まで可" };

        private static readonly string[] CreditChoices =
        { "クレジット表記が必要", "クレジット表記は不要" };

        private static readonly string[] ModificationChoices =
        { "改変禁止", "改変可・再配布不可", "改変可・改変物の再配布も可" };

        private static readonly string[] LookAtTypeChoices =
        { "目ボーンを回す", "表情で表す" };

        private static readonly string[] FirstPersonChoices =
        { "自動", "両方で描く", "三人称のみ", "一人称のみ" };

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(Sec("VRM 出力設定"));

            var help = new HelpBox(
                "VRM ファイルに載せる作者・ライセンス、視線の動き、一人称カメラでの見え方を決めます。\n"
                + "ここで決めた値はモデルと一緒に保存されます。\n"
                + "「エクスポート」の VRM 欄は出力ごとの上書きで、空欄ならここの値が使われます。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _stateLabel = new Label();
            _stateLabel.style.fontSize     = 10;
            _stateLabel.style.whiteSpace   = WhiteSpace.Normal;
            _stateLabel.style.marginBottom = 4;
            root.Add(_stateLabel);

            BuildMetaSection(root);
            BuildLicenseSection(root);
            BuildLookAtSection(root);
            BuildFirstPersonSection(root);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);
        }

        // ── ① 作者情報 ────────────────────────────────────────────────
        private void BuildMetaSection(VisualElement root)
        {
            var fo = new Foldout { text = "① 作者情報", value = true };

            _nameField    = new TextField("モデル名");
            _versionField = new TextField("バージョン");
            _authorsField = new TextField("作者");
            _authorsField.tooltip = "複数いるときはカンマ区切り。";
            fo.Add(_nameField); fo.Add(_versionField); fo.Add(_authorsField);
            fo.Add(Hint(
                "モデル名を空にするとモデルの名前が入ります。作者を空にすると Unknown になります。\n"
              + "作者が複数いるときは「山田, 田中」のようにカンマで区切ります。"));

            _copyrightField  = new TextField("著作権表記");
            _contactField    = new TextField("連絡先");
            _referencesField = new TextField("参照元");
            _referencesField.tooltip = "複数あるときはカンマ区切り。";
            _thirdPartyField = new TextField("第三者ライセンス");
            fo.Add(_copyrightField); fo.Add(_contactField);
            fo.Add(_referencesField); fo.Add(_thirdPartyField);
            fo.Add(Hint("参照元には、使った素材の配布元などを書きます。"));

            _thumbnailField = new TextField("サムネイル画像");
            _thumbnailField.tooltip = "画像ファイルのパス。空ならサムネイルなし。";
            fo.Add(_thumbnailField);
            fo.Add(Hint(
                "画像ファイルの場所を入れます。出力のときだけ読み込み、プロジェクトには入れません。\n"
              + "見つからなくても出力は止まりません（警告だけ出ます）。"));

            root.Add(fo);
        }

        // ── ② 許諾 ────────────────────────────────────────────────────
        private void BuildLicenseSection(VisualElement root)
        {
            var fo = new Foldout { text = "② 許諾", value = true };

            _permissionField = new DropdownField(
                "演じてよい人", new List<string>(PermissionChoices), 0);
            fo.Add(_permissionField);
            fo.Add(Hint("このアバターを使って動いてよいのは誰か、という指定です。"));

            _violentToggle    = new Toggle("暴力表現に使ってよい");
            _sexualToggle     = new Toggle("性的表現に使ってよい");
            _politicalToggle  = new Toggle("政治・宗教用途に使ってよい");
            _antisocialToggle = new Toggle("反社会的・憎悪表現に使ってよい");
            fo.Add(_violentToggle); fo.Add(_sexualToggle);
            fo.Add(_politicalToggle); fo.Add(_antisocialToggle);

            _commercialField = new DropdownField(
                "商用利用", new List<string>(CommercialChoices), 0);
            fo.Add(_commercialField);

            _creditField = new DropdownField(
                "クレジット表記", new List<string>(CreditChoices), 0);
            _modificationField = new DropdownField(
                "改変", new List<string>(ModificationChoices), 0);
            _redistributionToggle = new Toggle("再配布してよい");
            fo.Add(_creditField); fo.Add(_modificationField); fo.Add(_redistributionToggle);

            _otherLicenseField = new TextField("その他ライセンス URL");
            fo.Add(_otherLicenseField);
            fo.Add(Hint(
                "既定はいちばん厳しい側（作者のみ・非営利・改変禁止・再配布不可）です。\n"
              + "配布するなら、意図した許諾に合わせて必ず見直してください。"));

            var row = Row();
            row.Add(Btn("モデルから読み込む", OnLoadFromModel, grow: true));
            row.Add(Btn("モデルへ書き込む", OnApplyMeta, grow: true));
            fo.Add(row);

            var row2 = Row();
            row2.Add(Btn("作者情報を未設定に戻す", OnClearMeta, grow: true));
            fo.Add(row2);

            root.Add(fo);
        }

        // ── ③ 視線 ────────────────────────────────────────────────────
        private void BuildLookAtSection(VisualElement root)
        {
            var fo = new Foldout { text = "③ 視線", value = false };

            _lookAtStateLabel = new Label();
            _lookAtStateLabel.style.fontSize   = 9;
            _lookAtStateLabel.style.whiteSpace = WhiteSpace.Normal;
            _lookAtStateLabel.style.color      = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            fo.Add(_lookAtStateLabel);

            var d = new VrmLookAtData();

            _offX = new FloatField("目の位置 X") { value = d.OffsetFromHead.x };
            _offY = new FloatField("目の位置 Y") { value = d.OffsetFromHead.y };
            _offZ = new FloatField("目の位置 Z") { value = d.OffsetFromHead.z };
            fo.Add(_offX); fo.Add(_offY); fo.Add(_offZ);
            fo.Add(Hint("頭ボーンから見た、両目の中心の位置です[m]。既定は少し上の (0, 0.06, 0)。"));

            _lookAtTypeField = new DropdownField(
                "表し方", new List<string>(LookAtTypeChoices), 0);
            fo.Add(_lookAtTypeField);
            fo.Add(Hint(
                "目ボーン＝目のボーンを回します。表情＝lookUp などの表情で表します。\n"
              + "目のボーンが無いモデルは表情を選びます。"));

            fo.Add(Hint("以下は「頭を何度回したところで目が振り切るか」と「振り切ったときの量」です。"));

            _hiIn  = new FloatField("鼻側 振り切る角度[度]")  { value = d.HorizontalInner.InputMaxDegrees };
            _hiOut = new FloatField("鼻側 出力量")            { value = d.HorizontalInner.OutputScale };
            _hoIn  = new FloatField("外側 振り切る角度[度]")  { value = d.HorizontalOuter.InputMaxDegrees };
            _hoOut = new FloatField("外側 出力量")            { value = d.HorizontalOuter.OutputScale };
            _vdIn  = new FloatField("下 振り切る角度[度]")    { value = d.VerticalDown.InputMaxDegrees };
            _vdOut = new FloatField("下 出力量")              { value = d.VerticalDown.OutputScale };
            _vuIn  = new FloatField("上 振り切る角度[度]")    { value = d.VerticalUp.InputMaxDegrees };
            _vuOut = new FloatField("上 出力量")              { value = d.VerticalUp.OutputScale };
            fo.Add(_hiIn); fo.Add(_hiOut); fo.Add(_hoIn); fo.Add(_hoOut);
            fo.Add(_vdIn); fo.Add(_vdOut); fo.Add(_vuIn); fo.Add(_vuOut);
            fo.Add(Hint(
                "出力量は、表し方が「目ボーン」なら目の角度[度]、「表情」なら表情の重み(0〜1)です。\n"
              + "既定は 90 度で振り切って 10 度動く、という設定です。"));

            var row = Row();
            row.Add(Btn("モデルへ書き込む", OnApplyLookAt, grow: true));
            row.Add(Btn("未設定に戻す",     OnClearLookAt, grow: true));
            fo.Add(row);

            root.Add(fo);
        }

        // ── ④ 一人称 ──────────────────────────────────────────────────
        private void BuildFirstPersonSection(VisualElement root)
        {
            var fo = new Foldout { text = "④ 一人称カメラでの見え方", value = false };

            fo.Add(Hint(
                "VR で自分の視点から見たときに、どのメッシュを描くかの指定です。\n"
              + "自動＝頭の子孫なら自分の視界から消します。ふつうは自動のままで足ります。\n"
              + "顔まわりだけ「三人称のみ」にすると、自分の視界から顔が消えます。"));

            _fpTargetLabel = new Label();
            _fpTargetLabel.style.fontSize     = 10;
            _fpTargetLabel.style.whiteSpace   = WhiteSpace.Normal;
            _fpTargetLabel.style.marginBottom = 4;
            fo.Add(_fpTargetLabel);

            _fpTypeField = new DropdownField(
                "扱い", new List<string>(FirstPersonChoices), 0);
            fo.Add(_fpTypeField);

            var row = Row();
            row.Add(Btn("選んだメッシュに設定", OnApplyFirstPerson, grow: true));
            fo.Add(row);

            _fpListView = new ListView(_fpRows, 20,
                () => new Label(),
                (elem, i) =>
                {
                    if (elem is Label l && i >= 0 && i < _fpRows.Count) l.text = _fpRows[i];
                });
            _fpListView.selectionType      = SelectionType.Single;
            _fpListView.style.minHeight    = 80;
            _fpListView.style.maxHeight    = 160;
            _fpListView.style.marginBottom = 4;
            _fpListView.selectionChanged  += OnFpRowSelectionChanged;
            fo.Add(_fpListView);
            fo.Add(Hint("自動以外を指定したメッシュだけ並びます。行を選ぶとそのメッシュを選択します。"));

            root.Add(fo);
        }

        // ================================================================
        // 再表示
        // ================================================================

        public void Refresh()
        {
            if (_stateLabel == null) return;

            var model = CurrentModel;
            if (model == null)
            {
                _stateLabel.text = "モデルが読み込まれていません。";
                _fpRows.Clear();
                _fpRowRefs.Clear();
                RebuildFpList();
                return;
            }

            // モデルが変わったときだけ欄を読み直す（入力中の値を消さないため）。
            if (!ReferenceEquals(_loadedModel, model))
            {
                _loadedModel = model;
                LoadMetaFields(model);
                LoadLookAtFields(model);
            }

            _stateLabel.text =
                (model.VrmMeta != null ? "作者情報: 設定済み" : "作者情報: 未設定（VRM の既定で出ます）")
                + " / "
                + (model.VrmLookAt != null ? "視線: 設定済み" : "視線: 未設定（VRM の既定で出ます）");

            if (_lookAtStateLabel != null)
            {
                _lookAtStateLabel.text = (model.VrmLookAt != null)
                    ? "このモデルは視線の設定を持っています。"
                    : "未設定です。書き込むと、以下の値が VRM に載ります。";
            }

            // 一人称の対象
            var targets = SelectedDrawables(model);
            _fpTargetLabel.text = (targets.Count == 0)
                ? "メッシュが選ばれていません。設定する先を選んでください。"
                : $"設定先: {model.GetMeshContext(targets[0])?.Name}（選択 {targets.Count} 件のうち先頭）";

            // 一人称の一覧
            _fpRows.Clear();
            _fpRowRefs.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                if (mo.VrmFirstPerson == VrmFirstPersonType.Auto) continue;

                _fpRowRefs.Add(i);
                _fpRows.Add($"{mc.Name}  {FirstPersonText(mo.VrmFirstPerson)}");
            }
            if (_fpRows.Count == 0) _fpRows.Add("すべて自動です（指定なし）。");

            RebuildFpList();
        }

        private void RebuildFpList()
        {
            if (_fpListView == null) return;

            _suppressListEvents = true;
            try
            {
                _fpListView.itemsSource = _fpRows;
                _fpListView.Rebuild();

                _fpSelectedRow = Mathf.Clamp(_fpSelectedRow, -1, _fpRowRefs.Count - 1);
                if (_fpSelectedRow >= 0) _fpListView.SetSelection(_fpSelectedRow);
            }
            finally
            {
                _suppressListEvents = false;
            }
        }

        // ================================================================
        // 欄への読み込み
        // ================================================================

        private void LoadMetaFields(ModelContext model)
        {
            var m = VrmSettingsOps.GetMetaOrNew(model);

            _nameField?.SetValueWithoutNotify(m.Name ?? "");
            _versionField?.SetValueWithoutNotify(m.Version ?? "");
            _authorsField?.SetValueWithoutNotify(JoinList(m.Authors));
            _copyrightField?.SetValueWithoutNotify(m.CopyrightInformation ?? "");
            _contactField?.SetValueWithoutNotify(m.ContactInformation ?? "");
            _referencesField?.SetValueWithoutNotify(JoinList(m.References));
            _thirdPartyField?.SetValueWithoutNotify(m.ThirdPartyLicenses ?? "");
            _thumbnailField?.SetValueWithoutNotify(m.ThumbnailPath ?? "");

            if (_permissionField   != null) _permissionField.index   = (int)m.AvatarPermission;
            if (_commercialField   != null) _commercialField.index   = (int)m.CommercialUsage;
            if (_creditField       != null) _creditField.index       = (int)m.CreditNotation;
            if (_modificationField != null) _modificationField.index = (int)m.Modification;

            _violentToggle?.SetValueWithoutNotify(m.ViolentUsage);
            _sexualToggle?.SetValueWithoutNotify(m.SexualUsage);
            _politicalToggle?.SetValueWithoutNotify(m.PoliticalOrReligiousUsage);
            _antisocialToggle?.SetValueWithoutNotify(m.AntisocialOrHateUsage);
            _redistributionToggle?.SetValueWithoutNotify(m.Redistribution);
            _otherLicenseField?.SetValueWithoutNotify(m.OtherLicenseUrl ?? "");
        }

        private void LoadLookAtFields(ModelContext model)
        {
            var l = VrmSettingsOps.GetLookAtOrNew(model);

            _offX?.SetValueWithoutNotify(l.OffsetFromHead.x);
            _offY?.SetValueWithoutNotify(l.OffsetFromHead.y);
            _offZ?.SetValueWithoutNotify(l.OffsetFromHead.z);
            if (_lookAtTypeField != null) _lookAtTypeField.index = (int)l.LookAtType;

            _hiIn?.SetValueWithoutNotify(l.HorizontalInner.InputMaxDegrees);
            _hiOut?.SetValueWithoutNotify(l.HorizontalInner.OutputScale);
            _hoIn?.SetValueWithoutNotify(l.HorizontalOuter.InputMaxDegrees);
            _hoOut?.SetValueWithoutNotify(l.HorizontalOuter.OutputScale);
            _vdIn?.SetValueWithoutNotify(l.VerticalDown.InputMaxDegrees);
            _vdOut?.SetValueWithoutNotify(l.VerticalDown.OutputScale);
            _vuIn?.SetValueWithoutNotify(l.VerticalUp.InputMaxDegrees);
            _vuOut?.SetValueWithoutNotify(l.VerticalUp.OutputScale);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnLoadFromModel()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            LoadMetaFields(model);
            LoadLookAtFields(model);
            SetStatus("モデルの値を読み込みました。");
        }

        private void OnApplyMeta()
        {
            if (CurrentModel == null) { SetStatus("モデルが読み込まれていません。"); return; }

            SendCmd(new SetVrmMetaCommand(
                ModelIndex,
                _nameField.value, _versionField.value, SplitList(_authorsField.value),
                _copyrightField.value, _contactField.value,
                SplitList(_referencesField.value), _thirdPartyField.value,
                _thumbnailField.value,
                Index(_permissionField), _violentToggle.value, _sexualToggle.value,
                Index(_commercialField), _politicalToggle.value, _antisocialToggle.value,
                Index(_creditField), _redistributionToggle.value, Index(_modificationField),
                _otherLicenseField.value));

            SetStatus("作者情報をモデルへ書き込みました。");
            Refresh();
        }

        private void OnClearMeta()
        {
            if (CurrentModel == null) { SetStatus("モデルが読み込まれていません。"); return; }

            SendCmd(new ClearVrmMetaCommand(ModelIndex));
            SetStatus("作者情報を未設定へ戻しました。");
            Refresh();
        }

        private void OnApplyLookAt()
        {
            if (CurrentModel == null) { SetStatus("モデルが読み込まれていません。"); return; }

            SendCmd(new SetVrmLookAtCommand(
                ModelIndex,
                new Vector3(_offX.value, _offY.value, _offZ.value),
                Index(_lookAtTypeField),
                new Vector2(_hiIn.value, _hiOut.value),
                new Vector2(_hoIn.value, _hoOut.value),
                new Vector2(_vdIn.value, _vdOut.value),
                new Vector2(_vuIn.value, _vuOut.value)));

            SetStatus("視線設定をモデルへ書き込みました。");
            Refresh();
        }

        private void OnClearLookAt()
        {
            if (CurrentModel == null) { SetStatus("モデルが読み込まれていません。"); return; }

            SendCmd(new ClearVrmLookAtCommand(ModelIndex));
            SetStatus("視線設定を未設定へ戻しました。");
            Refresh();
        }

        private void OnApplyFirstPerson()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedDrawables(model);
            if (targets.Count == 0) { SetStatus("設定するメッシュを選んでください。"); return; }

            SendCmd(new SetVrmFirstPersonCommand(
                ModelIndex, targets.ToArray(), Index(_fpTypeField)));

            SetStatus($"{targets.Count} 件に設定しました。");
            Refresh();
        }

        private void OnFpRowSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            _fpSelectedRow = _fpListView.selectedIndex;
            if (_fpSelectedRow < 0 || _fpSelectedRow >= _fpRowRefs.Count) return;

            int master = _fpRowRefs[_fpSelectedRow];

            var mo = CurrentModel?.GetMeshContext(master)?.MeshObject;
            if (mo != null && _fpTypeField != null)
                _fpTypeField.index = (int)mo.VrmFirstPerson;

            // 3D 画面・メッシュリストと同じ経路で選び直す。
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Drawable, new[] { master }));
        }

        // ================================================================
        // 小物
        // ================================================================

        /// <summary>
        /// 一人称を設定する先。描画オブジェクトの選択だけを見る。
        /// 判定の正典は VrmSettingsOps.IsFirstPersonCarrier。
        /// </summary>
        private static List<int> SelectedDrawables(ModelContext model)
        {
            var result = new List<int>();
            if (model?.SelectedDrawableMeshIndices == null) return result;

            foreach (int i in model.SelectedDrawableMeshIndices)
                if (VrmSettingsOps.IsFirstPersonCarrier(model, i)) result.Add(i);

            return result;
        }

        private static string FirstPersonText(VrmFirstPersonType t)
        {
            switch (t)
            {
                case VrmFirstPersonType.Both:            return "両方で描く";
                case VrmFirstPersonType.ThirdPersonOnly: return "三人称のみ";
                case VrmFirstPersonType.FirstPersonOnly: return "一人称のみ";
                default:                                 return "自動";
            }
        }

        private static int Index(DropdownField f)
        {
            int i = f?.index ?? 0;
            return (i < 0) ? 0 : i;
        }

        /// <summary>カンマ区切り → 配列。空要素は落とす。</summary>
        private static string[] SplitList(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

            var parts = text.Split(',');
            var list = new List<string>(parts.Length);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (!string.IsNullOrEmpty(t)) list.Add(t);
            }
            return list.ToArray();
        }

        private static string JoinList(List<string> list)
            => (list == null || list.Count == 0) ? "" : string.Join(", ", list);

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button Btn(string text, Action action, bool grow = false)
        {
            var b = new Button(action) { text = text };
            b.style.height = 22;
            if (grow) b.style.flexGrow = 1;
            return b;
        }

        private static Label Sec(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label Hint(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            l.style.fontSize     = 9;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            return l;
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
    }
}
