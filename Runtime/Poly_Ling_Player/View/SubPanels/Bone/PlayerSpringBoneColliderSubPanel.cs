// PlayerSpringBoneColliderSubPanel.cs
// 当たり判定（VRM SpringBone の collider）の作成と編集。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置
//
// 【なぜ要るか】
//   「揺れもの編集」には当たり判定のまとまり（グループ）の名前を足す欄しか無く、
//   まとまりに入れる当たり判定そのものを作る手段が画面に無かった。
//   結果、当たり判定は検証用パネルの一括生成でしか作れなかった。
//
// 【対象は「選択」で決める】
//   このパネルは独自の対象指定を持たない。3D 画面・メッシュリストと同じ選択
//   （ModelContext のボーン選択）をそのまま「付ける先」にする。
//
// 【一覧はモデル全体】
//   当たり判定はボーンに付くが、探すときは「どのボーンに何が付いているか」を
//   一覧で見たい。よってモデル全体を走査して並べ、行を選ぶとそのボーンを選択する。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerSpringBoneColliderSubPanel
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
        // UI
        // ================================================================

        private Label    _targetLabel;
        private Label    _statusLabel;
        private ListView _listView;

        private DropdownField _shapeField;
        private FloatField _offX, _offY, _offZ;
        private FloatField _radiusField;
        private FloatField _tailX, _tailY, _tailZ;
        private FloatField _normX, _normY, _normZ;
        private TextField  _groupsField;

        private Label _groupListLabel;

        // ================================================================
        // 表示用の控え（Refresh のたびに作り直す）
        // ================================================================

        private readonly List<string> _rows = new List<string>();

        /// <summary>行 → (付帯先ボーンの masterIndex, そのボーン内の何番目か)。</summary>
        private readonly List<(int Master, int Slot)> _rowRefs = new List<(int, int)>();

        private int _selectedRow = -1;

        /// <summary>
        /// 形の選択肢。並びは SpringBoneColliderShape の値の順
        /// （Sphere / Capsule / InsideSphere / InsideCapsule / Plane）と
        /// 必ず一致させること。index をそのまま enum の値として使う。
        /// </summary>
        private static readonly string[] ShapeChoices =
        {
            "球",
            "カプセル",
            "内側の球",
            "内側のカプセル",
            "平面",
        };

        /// <summary>
        /// 一覧の選択変更ハンドラを止める。
        /// 行を選ぶと SelectMeshCommand を送り、その通知で Refresh が走り、
        /// Refresh が SetSelection で選択を戻すため、止めないと往復し続ける。
        /// </summary>
        private bool _suppressListEvents;

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(Sec("当たり判定の作成と編集"));

            var help = new HelpBox(
                "当たり判定は、髪やスカートが体を突き抜けないように置く「見えない体の形」です。\n"
                + "腰・脚・頭・胸などのボーンに付け、まとまりへ入れます。\n"
                + "揺れる側（鎖）は「ぶつける当たり判定のまとまり」でそのまとまりを指します。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _targetLabel = new Label();
            _targetLabel.style.fontSize   = 10;
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _targetLabel.style.marginBottom = 4;
            root.Add(_targetLabel);

            BuildListSection(root);
            BuildShapeSection(root);
            BuildActionSection(root);
            BuildGuideSection(root);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);
        }

        // ── 一覧 ──────────────────────────────────────────────────────
        private void BuildListSection(VisualElement root)
        {
            var fo = new Foldout { text = "① いまある当たり判定", value = true };

            _listView = new ListView(_rows, 20,
                () => new Label(),
                (elem, i) =>
                {
                    if (elem is Label l && i >= 0 && i < _rows.Count) l.text = _rows[i];
                });
            _listView.selectionType    = SelectionType.Single;
            _listView.style.minHeight  = 80;
            _listView.style.maxHeight  = 160;
            _listView.style.marginBottom = 4;
            _listView.selectionChanged += OnRowSelectionChanged;
            fo.Add(_listView);

            fo.Add(Hint(
                "行を選ぶと、その当たり判定が付いているボーンを選び、下の欄へ値を読み込みます。"));

            _groupListLabel = new Label();
            _groupListLabel.style.fontSize   = 9;
            _groupListLabel.style.whiteSpace = WhiteSpace.Normal;
            _groupListLabel.style.color      = new StyleColor(new Color(0.72f, 0.72f, 0.72f));
            fo.Add(_groupListLabel);

            root.Add(fo);
        }

        // ── 形 ────────────────────────────────────────────────────────
        private void BuildShapeSection(VisualElement root)
        {
            var fo = new Foldout { text = "② 形と大きさ", value = true };

            // 並びは SpringBoneColliderShape の値の順。index をそのまま
            // enum の値として使うので、並べ替えないこと。
            _shapeField = new DropdownField("形", new List<string>(ShapeChoices), 0);
            fo.Add(_shapeField);
            fo.Add(Hint(
                "球＝外へ押し出します。腕・頭など丸いところに。\n"
              + "カプセル＝中心ともう一方の端を結んだ棒状。脚・胴に。\n"
              + "内側の球 / 内側のカプセル＝その中へ閉じ込めます。\n"
              + "平面＝法線の側へ押し出します。床や壁のように使います。"));

            var d = new SpringBoneColliderData();

            _offX = new FloatField("中心 X") { value = d.Offset.x };
            _offY = new FloatField("中心 Y") { value = d.Offset.y };
            _offZ = new FloatField("中心 Z") { value = d.Offset.z };
            fo.Add(_offX); fo.Add(_offY); fo.Add(_offZ);
            fo.Add(Hint(
                "付ける先のボーンから見た位置です[m]。0,0,0 でボーンの位置そのもの。"));

            _radiusField = new FloatField("半径[m]") { value = d.Radius };
            fo.Add(_radiusField);
            fo.Add(Hint(
                $"既定 {Fmt(d.Radius)}。揺れる側の「当たり半径」と足して衝突を見ます。\n"
              + "大きすぎると服が体から浮きます。小さすぎると突き抜けます。"));

            _tailX = new FloatField("もう一方の端 X") { value = d.Tail.x };
            _tailY = new FloatField("もう一方の端 Y") { value = d.Tail.y };
            _tailZ = new FloatField("もう一方の端 Z") { value = d.Tail.z };
            fo.Add(_tailX); fo.Add(_tailY); fo.Add(_tailZ);
            fo.Add(Hint("カプセルのときだけ使います。球と平面では読みません。"));

            _normX = new FloatField("法線 X") { value = d.Normal.x };
            _normY = new FloatField("法線 Y") { value = d.Normal.y };
            _normZ = new FloatField("法線 Z") { value = d.Normal.z };
            fo.Add(_normX); fo.Add(_normY); fo.Add(_normZ);
            fo.Add(Hint("平面のときだけ使います。押し出す向きです。既定は真上 (0,1,0)。"));

            _groupsField = new TextField("入れるまとまり");
            _groupsField.tooltip = "カンマ区切りの番号。空にするとどのまとまりにも入りません。";
            fo.Add(_groupsField);
            fo.Add(Hint(
                "上の一覧に出ている [0] [1] の番号を、カンマ区切りで入れます。\n"
              + "まとまりに入っていない当たり判定は、どの鎖からも参照されないため効きません。"));

            root.Add(fo);
        }

        // ── 操作 ──────────────────────────────────────────────────────
        private void BuildActionSection(VisualElement root)
        {
            var fo = new Foldout { text = "③ 作る・直す・消す", value = true };

            var row1 = Row();
            row1.Add(Btn("選んだボーンに作る", OnAdd, grow: true));
            fo.Add(row1);
            fo.Add(Hint("メッシュリストか 3D 画面でボーンを選んでから押します。"));

            var row2 = Row();
            row2.Add(Btn("選んだ行を書き換える", OnUpdate, grow: true));
            row2.Add(Btn("選んだ行を消す",       OnDelete, grow: true));
            fo.Add(row2);

            root.Add(fo);
        }

        // ── 置き方の目安 ──────────────────────────────────────────────
        private void BuildGuideSection(VisualElement root)
        {
            var fo = new Foldout { text = "置き方の目安", value = false };

            fo.Add(Hint("スカート … 太ももに沿ってカプセルを左右 1 本ずつ。腰ボーンに付けます。"));
            fo.Add(Hint("後ろ髪 … 頭に球 1 個、首と肩に球か短いカプセル。"));
            fo.Add(Hint("前髪 … 顔の前に薄く。大きすぎると髪が浮いて額が見えます。"));
            fo.Add(Hint("胸元に垂れるもの … 胸と上半身に球。"));
            fo.Add(Hint(
                "当たり判定は体の形より少し小さめから始め、突き抜けたら大きくします。"
              + "先に大きくすると、服が体から浮いた原因が分からなくなります。"));
            fo.Add(Hint(
                "まとまりは体の部位ごとに分けます（脚・頭・胴）。"
              + "鎖ごとにぶつける相手を選べるのが、分ける理由です。"));

            root.Add(fo);
        }

        // ================================================================
        // 再表示
        // ================================================================

        public void Refresh()
        {
            if (_targetLabel == null) return;

            var model = CurrentModel;
            if (model == null)
            {
                _targetLabel.text = "モデルが読み込まれていません。";
                _rows.Clear();
                _rowRefs.Clear();
                if (_groupListLabel != null) _groupListLabel.text = "";
                RebuildList();
                return;
            }

            // 付ける先の表示
            var targets = SelectedTargets(model);
            string firstName = targets.Count > 0
                ? (model.GetMeshContext(targets[0])?.Name ?? "")
                : "";
            _targetLabel.text = targets.Count == 0
                ? "ボーンが選ばれていません。付ける先のボーンを選んでください。"
                : $"付ける先: {firstName}（選択 {targets.Count} 件のうち先頭）";

            // 一覧
            _rows.Clear();
            _rowRefs.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var list = mc?.MeshObject?.SpringBoneColliders;
                if (list == null) continue;

                for (int k = 0; k < list.Count; k++)
                {
                    var c = list[k];
                    if (c == null) continue;

                    _rowRefs.Add((i, k));
                    _rows.Add(
                        $"{mc.Name} [{k}]  {ShapeText(c.Shape)}  半径 {Fmt(c.Radius)}"
                      + $"  まとまり {JoinIndices(c.SpringBoneGroupIndices)}");
                }
            }
            if (_rows.Count == 0) _rows.Add("まだ当たり判定がありません。");

            // まとまり一覧（番号を入れるときの手引き）
            if (_groupListLabel != null)
            {
                var names = model.SpringBoneColliderGroupNames;
                if (names == null || names.Count == 0)
                {
                    _groupListLabel.text =
                        "まとまりがまだありません。「揺れもの編集 → 当たり判定のまとまり」で先に作ってください。";
                }
                else
                {
                    var sb = new System.Text.StringBuilder("まとまり: ");
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (i > 0) sb.Append("  ");
                        sb.Append($"[{i}] {names[i]}");
                    }
                    _groupListLabel.text = sb.ToString();
                }
            }

            RebuildList();
        }

        private void RebuildList()
        {
            if (_listView == null) return;

            _suppressListEvents = true;
            try
            {
                _listView.itemsSource = _rows;
                _listView.Rebuild();

                _selectedRow = Mathf.Clamp(_selectedRow, -1, _rowRefs.Count - 1);
                if (_selectedRow >= 0) _listView.SetSelection(_selectedRow);
            }
            finally
            {
                _suppressListEvents = false;
            }
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnAdd()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("付ける先のボーンを選んでください。"); return; }

            int master = targets[0];
            if (!SpringBoneOps.IsCarrier(model, master))
            { SetStatus("そのオブジェクトには当たり判定を付けられません。"); return; }

            SendCmd(new AddSpringBoneColliderCommand(
                ModelIndex, master,
                SelectedShape(),
                ReadOffset(), _radiusField.value,
                ReadTail(), ReadNormal(),
                ParseIndices(_groupsField.value)));

            SetStatus($"{model.GetMeshContext(master)?.Name} に当たり判定を作りました。");
            Refresh();
        }

        private void OnUpdate()
        {
            if (!TryGetSelectedRef(out int master, out int slot))
            { SetStatus("書き換える行を選んでください。"); return; }

            SendCmd(new UpdateSpringBoneColliderCommand(
                ModelIndex, master, slot,
                SelectedShape(),
                ReadOffset(), _radiusField.value,
                ReadTail(), ReadNormal(),
                ParseIndices(_groupsField.value)));

            SetStatus("書き換えました。");
            Refresh();
        }

        private void OnDelete()
        {
            if (!TryGetSelectedRef(out int master, out int slot))
            { SetStatus("消す行を選んでください。"); return; }

            string name = CurrentModel?.GetMeshContext(master)?.Name ?? "?";

            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "削除確認",
                $"「{name}」の当たり判定 [{slot}] を消します。\n"
                + "同じボーンの後ろの当たり判定は、番号が 1 つずつ前へ詰まります。",
                "削除", "キャンセル");
            if (!ok) return;

            SendCmd(new DeleteSpringBoneColliderCommand(ModelIndex, master, slot));

            _selectedRow = -1;
            SetStatus("消しました。");
            Refresh();
        }

        // ================================================================
        // 一覧の選択
        // ================================================================

        private void OnRowSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            _selectedRow = _listView.selectedIndex;
            if (!TryGetSelectedRef(out int master, out int slot)) return;

            var c = CurrentModel?.GetMeshContext(master)?.MeshObject?.SpringBoneColliders;
            if (c == null || slot >= c.Count) return;

            var d = c[slot];
            if (_shapeField != null) _shapeField.index = (int)d.Shape;
            _offX?.SetValueWithoutNotify(d.Offset.x);
            _offY?.SetValueWithoutNotify(d.Offset.y);
            _offZ?.SetValueWithoutNotify(d.Offset.z);
            _radiusField?.SetValueWithoutNotify(d.Radius);
            _tailX?.SetValueWithoutNotify(d.Tail.x);
            _tailY?.SetValueWithoutNotify(d.Tail.y);
            _tailZ?.SetValueWithoutNotify(d.Tail.z);
            _normX?.SetValueWithoutNotify(d.Normal.x);
            _normY?.SetValueWithoutNotify(d.Normal.y);
            _normZ?.SetValueWithoutNotify(d.Normal.z);
            _groupsField?.SetValueWithoutNotify(JoinIndices(d.SpringBoneGroupIndices));

            // 3D 画面・メッシュリストと同じ経路で選び直す。
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Bone, new[] { master }));
        }

        // ================================================================
        // 小物
        // ================================================================

        private bool TryGetSelectedRef(out int master, out int slot)
        {
            master = -1; slot = -1;
            if (_selectedRow < 0 || _selectedRow >= _rowRefs.Count) return false;
            master = _rowRefs[_selectedRow].Master;
            slot   = _rowRefs[_selectedRow].Slot;
            return true;
        }

        /// <summary>
        /// 画面で選ばれている形。ShapeChoices の並びが
        /// SpringBoneColliderShape の値の順と一致していることが前提。
        /// </summary>
        private SpringBoneColliderShape SelectedShape()
        {
            int i = _shapeField?.index ?? 0;
            if (i < 0 || i >= ShapeChoices.Length) i = 0;
            return (SpringBoneColliderShape)i;
        }

        private Vector3 ReadOffset() => new Vector3(_offX.value,  _offY.value,  _offZ.value);
        private Vector3 ReadTail()   => new Vector3(_tailX.value, _tailY.value, _tailZ.value);

        private Vector3 ReadNormal()
        {
            var n = new Vector3(_normX.value, _normY.value, _normZ.value);
            return n.sqrMagnitude > 1e-12f ? n : Vector3.up;
        }

        /// <summary>
        /// 付ける先。ボーン選択を主に見て、無ければ描画オブジェクトの選択を使う。
        /// 判定の正典は SpringBoneOps.IsCarrier。
        /// </summary>
        private static List<int> SelectedTargets(ModelContext model)
        {
            var result = new List<int>();
            if (model == null) return result;

            if (model.SelectedBoneIndices != null && model.SelectedBoneIndices.Count > 0)
            {
                result.AddRange(model.SelectedBoneIndices);
                return result;
            }

            if (model.SelectedDrawableMeshIndices != null)
                result.AddRange(model.SelectedDrawableMeshIndices);

            return result;
        }

        private static string ShapeText(SpringBoneColliderShape s)
        {
            switch (s)
            {
                case SpringBoneColliderShape.Capsule:       return "カプセル";
                case SpringBoneColliderShape.InsideSphere:  return "内側の球";
                case SpringBoneColliderShape.InsideCapsule: return "内側のカプセル";
                case SpringBoneColliderShape.Plane:         return "平面";
                default:                                    return "球";
            }
        }

        private static int[] ParseIndices(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<int>();

            var parts = text.Split(',');
            var list = new List<int>(parts.Length);
            foreach (string p in parts)
            {
                if (int.TryParse(p.Trim(), out int v) && v >= 0 && !list.Contains(v))
                    list.Add(v);
            }
            return list.ToArray();
        }

        private static string JoinIndices(List<int> indices)
        {
            if (indices == null || indices.Count == 0) return "（なし）";
            return string.Join(",", indices);
        }

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

        private static string Fmt(float v)
            => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
    }
}
