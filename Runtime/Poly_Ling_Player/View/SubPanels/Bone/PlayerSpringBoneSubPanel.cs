// PlayerSpringBoneSubPanel.cs
// 揺れもの（VRM SpringBone）の編集。
// Runtime/Poly_Ling_Player/View/SubPanels/Bone/ に配置
//
// 【なぜ要るか】
//   揺れデータを書き込む経路が検証用ダミー装備の一括生成しか無く、
//   既存のボーンへ揺れを付ける・外す・直す手段が無かった。
//
// 【付帯先はボーンに限らない】
//   ボーン、および非スキンドの描画オブジェクトに付けられる。
//   判定は SpringBoneOps.IsCarrier が正典。
//
// 【対象は「選択」で決める】
//   このパネルは独自の対象指定を持たない。3D 画面・メッシュリストと
//   同じ選択（ModelContext のボーン選択）をそのまま対象にする。
//   選ぶのが大変な鎖のために「選んだボーンから下をまとめて選ぶ」ボタンを置き、
//   実体は SelectBoneChainCommand へ流す（パネル内で選択を書き換えない）。
//
// 【検査を毎フレームやらない】
//   SpringBoneOps.Validate はモデル全体を走査する。
//   Refresh（パネルを開いたとき・変更通知が来たとき）でだけ呼ぶ。
//
// 【段階変化（根元→末端）】
//   揺れ方の値は段ごとに別々に持てる。根元・末端・配り方は SpringBoneTaper が
//   持ち、ここでは段ごとに SetSpringBoneJointCommand を送るだけにする。
//   考え方の出どころは SpringBoneTaper.cs 冒頭のコメント。
//
// 【3D 画面の強調表示】
//   いま触っている鎖と、その中のどのボーンかは、名前の一覧だけでは分からない。
//   ModelContext.SpringBoneHighlightIndices / …ActiveIndex へ書き、
//   MeshSceneRenderer が色と大きさを変えて描く。
//   書き換えたら OnHighlightChanged でビューポートへ作り直しを促すこと。
//
// 【画面の言葉】
//   エンドユーザー向けの表示語は「鎖」「揺れの根元（鎖の先頭）」「揺れ方」
//   「当たり判定」「当たり判定のまとまり」で統一する。
//   ノード・チェーン・ジョイント・索引は画面に出さない。

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
    public class PlayerSpringBoneSubPanel
    {
        // ================================================================
        // 外部依存（Viewer から設定）
        // ================================================================

        public Func<ProjectContext> GetProject;
        public Action<PanelCommand> SendCommand;

        /// <summary>
        /// 3D 画面の強調表示を書き換えたときに呼ぶ。
        /// ビューポートへ「描き直せ」と伝えるためだけのもの。
        /// </summary>
        public Action OnHighlightChanged;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        private ProjectContext Project      => GetProject?.Invoke();
        private ModelContext   CurrentModel => Project?.CurrentModel;
        private int            ModelIndex   => Project?.CurrentModelIndex ?? 0;

        // ================================================================
        // UI
        // ================================================================

        private Label _targetLabel;
        private Label _placeLabel;
        private Label _statusLabel;

        private ListView _chainListView;
        private ListView _groupListView;
        private ListView _issueListView;

        private TextField _chainNameField;
        private TextField _centerBoneField;
        private TextField _chainGroupsField;

        private FloatField _hitRadiusField,     _hitRadiusTipField;
        private FloatField _stiffnessField,     _stiffnessTipField;
        private FloatField _gravityPowerField,  _gravityPowerTipField;
        private FloatField _dragField,          _dragTipField;

        private FloatField _gravityDirX, _gravityDirY, _gravityDirZ;

        private DropdownField _taperShapeField;
        private Toggle        _taperEnabled;

        private DropdownField _angleLimitField;
        private FloatField _pitchDegField, _yawDegField;

        private FloatField _tailLengthField;
        private TextField  _tailSuffixField;
        private Toggle     _tailJointToggle;

        private TextField _groupNameField;

        private FloatField   _fixedDtField;
        private IntegerField _warmupField;

        private DropdownField _walkField;
        private Toggle        _additiveToggle;
        private FloatField    _minWeightField;

        // ================================================================
        // 表示用の控え（Refresh のたびに作り直す）
        // ================================================================

        private readonly List<string> _chainRows = new List<string>();
        private readonly List<int>    _chainMasters = new List<int>();
        private int _selectedChainRow = -1;

        private readonly List<string> _groupRows = new List<string>();
        private int _selectedGroupRow = -1;

        private readonly List<string> _issueRows = new List<string>();
        private readonly List<int>    _issueMasters = new List<int>();

        /// <summary>
        /// 一覧の選択変更ハンドラを止める。
        ///
        /// 【なぜ要るか】
        ///   一覧で行を選ぶと SelectMeshCommand を送る。ディスパッチャは
        ///   選択変更を通知し、通知はこのパネルの Refresh を呼ぶ。
        ///   Refresh は SetSelection で選択を戻すので、ここで再びハンドラが
        ///   走るとコマンド送信と Refresh が無限に往復する。
        /// </summary>
        private bool _suppressListEvents;

        // ================================================================
        // 配り方の選択肢（画面の文言と Gamma の対応）
        // ================================================================

        /// <summary>
        /// 揺れる向きの制限のしかた。並びは SpringBoneAngleLimitType の
        /// 値の順（None / Cone / Hinge / Spherical）と必ず一致させること。
        /// </summary>
        private static readonly string[] AngleLimitChoices =
        {
            "制限しない",
            "円錐",
            "蝶番",
            "球面",
        };

        private static readonly string[] TaperShapeChoices =
        {
            "まっすぐ変える",
            "根元の値を長く残す",
            "末端の値を長く効かせる",
            "中ほどで一気に変える",
        };

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(Sec("揺れもの（VRM SpringBone）"));

            var help = new HelpBox(
                "髪・スカート・しっぽなどを、キャラクターの動きに合わせて自動で揺らす設定です。\n"
                + "揺れの根元にするボーンを 1 本選び、「ここから揺らす」を押してください。\n"
                + "選んだボーンより下（子ボーン）が、まとめて 1 本の鎖になります。\n"
                + "ボーン以外に、スキニングしていない描画オブジェクト（アクセサリなど）も揺らせます。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _targetLabel = new Label();
            _targetLabel.style.fontSize = 10;
            _targetLabel.style.whiteSpace = WhiteSpace.Normal;
            _targetLabel.style.marginBottom = 2;
            root.Add(_targetLabel);

            // いま触っている場所。3D 画面の強調表示と同じ内容を文字でも出す。
            _placeLabel = new Label();
            _placeLabel.style.fontSize = 10;
            _placeLabel.style.whiteSpace = WhiteSpace.Normal;
            _placeLabel.style.marginBottom = 4;
            _placeLabel.style.color = new StyleColor(new Color(0.78f, 0.6f, 1f));
            root.Add(_placeLabel);

            BuildSelectSection(root);
            BuildChainSection(root);
            BuildJointSection(root);
            BuildAngleLimitSection(root);
            BuildRecipeSection(root);
            BuildWeightGuideSection(root);
            BuildTailSection(root);
            BuildGroupSection(root);
            BuildSettingsSection(root);
            BuildIssueSection(root);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);
        }

        // ── ① 揺らすボーンを選ぶ ──────────────────────────────────────
        private void BuildSelectSection(VisualElement root)
        {
            var fo = new Foldout { text = "① 揺らすボーンを選ぶ", value = true };

            // EnumField をやめて DropdownField にした理由：
            //   EnumField は enum の名前（FirstChild / AllDescendants）を
            //   そのまま出すため、画面に内部語が出てしまう。
            _walkField = new DropdownField(
                "ボーンのたどり方",
                new List<string>
                {
                    "根元から下へ一直線（枝分かれの手前で止まる）",
                    "枝分かれも全部たどる",
                },
                0);
            _walkField.tooltip =
                "鎖は枝分かれできません。ふつうは一直線のままにして、"
              + "房の根元を 1 本ずつ別の鎖にしてください。";
            fo.Add(_walkField);

            _additiveToggle = new Toggle("いま選んでいるものに追加する") { value = false };
            fo.Add(_additiveToggle);

            var rowChain = Row();
            rowChain.Add(Btn("選んだボーンから下をまとめて選ぶ", OnSelectChain, grow: true));
            fo.Add(rowChain);

            _minWeightField = new FloatField("無視するウェイトの下限") { value = 0.01f };
            fo.Add(_minWeightField);

            var rowWeight = Row();
            rowWeight.Add(Btn("選んだメッシュを動かしているボーンを選ぶ", OnSelectByWeight, grow: true));
            fo.Add(rowWeight);

            fo.Add(Hint(
                "3D 画面から選ぶこともできます。「描画オブジェクトの姿勢」ツールに切り替えて、"
              + "ボーンの印をクリックしてください。Shift で足し、Ctrl で入り切りできます。"));

            root.Add(fo);
        }

        // ── ② 揺れの根元（鎖の先頭）───────────────────────────────────
        private void BuildChainSection(VisualElement root)
        {
            var fo = new Foldout { text = "② 揺れの根元（鎖の先頭）", value = true };

            _chainListView = MakeList(_chainRows, OnChainSelectionChanged);
            fo.Add(_chainListView);

            _chainNameField  = new TextField("この揺れの名前");
            _chainNameField.tooltip = "空にするとボーン名をそのまま使います。";
            _centerBoneField = new TextField("揺れの基準にする体のボーン");
            _centerBoneField.tooltip = "空にすると、キャラクターの移動そのものが揺れに効きます。";
            _chainGroupsField = new TextField("ぶつける当たり判定のまとまり");
            _chainGroupsField.tooltip = "カンマ区切りの番号。空にすると何にもぶつかりません。";
            fo.Add(_chainNameField);
            fo.Add(_centerBoneField);
            fo.Add(_chainGroupsField);
            fo.Add(Hint(
                "選んだボーンより下が、まとめて 1 本の鎖になります。\n"
              + "鎖の形は保存せず、ボーンの親子関係と揺れ方の有無から毎回導き直します。\n\n"
              + "この揺れの名前：空欄ならボーン名をそのまま使います。\n\n"
              + "揺れの基準にする体のボーン：空欄のままだと、キャラクターが歩いただけで"
              + "髪やスカートが大きく暴れます。「腰」など体の中心にあるボーン名を入れると、"
              + "体ごとの移動では揺れなくなります。ここで言う「体」は、編集中のキャラクター自身の胴体です。\n\n"
              + "ぶつける当たり判定のまとまり：下の「当たり判定のまとまり」一覧に出ている"
              + " [0] [1] の数字を、カンマ区切りで入れます。空欄だと何にもぶつからず、体を突き抜けます。"));

            var row = Row();
            row.Add(Btn("ここから揺らす", OnSetChainRoot,   grow: true));
            row.Add(Btn("揺れをやめる",   OnClearChainRoot, grow: true));
            fo.Add(row);

            root.Add(fo);
        }

        // ── ③ 揺れ方 ──────────────────────────────────────────────────
        private void BuildJointSection(VisualElement root)
        {
            var fo = new Foldout { text = "③ 揺れ方", value = true };

            var d = new SpringBoneJointData();

            fo.Add(new HelpBox(
                "選んだボーンに、下の値を書き込みます。\n"
                + "値は段ごとに別々に持てます。全段同じでよければ「選んだところへ適用」、\n"
                + "根元から末端へ少しずつ変えたければ「根元から末端へ配って適用」を使います。",
                HelpBoxMessageType.Info));

            // ── かたさ ──────────────────────────────────────────
            fo.Add(Sec("かたさ"));
            _stiffnessField    = new FloatField("かたさ 根元") { value = d.StiffnessForce };
            _stiffnessTipField = new FloatField("かたさ 末端") { value = d.StiffnessForce };
            fo.Add(_stiffnessField);
            fo.Add(_stiffnessTipField);
            fo.Add(Hint(
                "初期姿勢へ戻ろうとする速さ。大きいほど硬く、小さいほどふにゃふにゃになります。\n"
              + $"既定 {Fmt(d.StiffnessForce)} ／ 目安 0〜4。\n"
              + "重力で大きく形が変わる薄手の布は 0.3 くらい、形を保ちたい長い髪は 1.0〜1.2 くらい。\n"
              + "根元 1.0 → 末端 0.2 のように下げると、根元が崩れずに先だけ動きます。"));

            // ── 減衰 ────────────────────────────────────────────
            fo.Add(Sec("減衰"));
            _dragField    = new FloatField("減衰 根元") { value = d.DragForce };
            _dragTipField = new FloatField("減衰 末端") { value = d.DragForce };
            fo.Add(_dragField);
            fo.Add(_dragTipField);
            fo.Add(Hint(
                "前のコマの動きをどれだけ捨てるかです。空気抵抗ではありません。\n"
              + $"既定 {Fmt(d.DragForce)} ／ 範囲 0〜1。\n"
              + "上げる: すぐ止まる。下げる: 揺れ続ける。0 にすると止まりません。"));

            // ── 重力の強さ ──────────────────────────────────────
            fo.Add(Sec("重力の強さ"));
            _gravityPowerField    = new FloatField("重力の強さ 根元") { value = d.GravityPower };
            _gravityPowerTipField = new FloatField("重力の強さ 末端") { value = d.GravityPower };
            fo.Add(_gravityPowerField);
            fo.Add(_gravityPowerTipField);
            fo.Add(Hint(
                "下の「重力の向き」へ足す量です。加速度ではないので 9.8 ではありません。\n"
              + $"既定 {Fmt(d.GravityPower)} ／ 目安 0〜2。\n"
              + "扱いが難しいので、まず 0 のまま仕上げ、垂れないときだけ 0.1 ずつ上げます。"
              + "効かせすぎると全部が真下へ集まり、横に広がる髪型ほど地味になります。\n"
              + "根元 0.3 → 末端 1.0 のように上げると、先だけ重く垂れます。"));

            // 重力の向きは FloatField 3 本で持つ。
            // 他の Player パネルが Vector3Field を使っていないので合わせる。
            _gravityDirX = new FloatField("重力の向き X") { value = d.GravityDir.x };
            _gravityDirY = new FloatField("重力の向き Y") { value = d.GravityDir.y };
            _gravityDirZ = new FloatField("重力の向き Z") { value = d.GravityDir.z };
            fo.Add(_gravityDirX);
            fo.Add(_gravityDirY);
            fo.Add(_gravityDirZ);
            fo.Add(Hint(
                "既定は (0, -1, 0)＝真下。長さ 0 を入れると真下に直します。\n"
              + "向きは段ごとには変えません。風でなびかせたいときだけ横向きにします。"));

            // ── 当たり半径 ──────────────────────────────────────
            fo.Add(Sec("当たり半径"));
            _hitRadiusField    = new FloatField("当たり半径 根元") { value = d.HitRadius };
            _hitRadiusTipField = new FloatField("当たり半径 末端") { value = d.HitRadius };
            fo.Add(_hitRadiusField);
            fo.Add(_hitRadiusTipField);
            fo.Add(Hint(
                "揺れる側の球の半径です。当たり判定の半径に足して衝突を見ます。\n"
              + $"既定 {Fmt(d.HitRadius)} ／ 目安 0〜0.5。\n"
              + "上げる: 体から離れて早く止まる。下げる: 体に近づき、突き抜けやすくなる。"));

            // ── 配り方 ──────────────────────────────────────────
            fo.Add(Sec("根元から末端への配り方"));

            _taperEnabled = new Toggle("根元と末端で値を変える") { value = false };
            fo.Add(_taperEnabled);
            fo.Add(Hint("外すと、根元の値を全段に同じだけ入れます（末端の欄は読みません）。"));

            _taperShapeField = new DropdownField(
                "変わり方", new List<string>(TaperShapeChoices), 0);
            fo.Add(_taperShapeField);
            fo.Add(Hint(
                "根元を 0、末端を 1 として、途中をどう配るかです。\n"
              + "「根元の値を長く残す」…根元側が崩れるときに使います。\n"
              + "「末端の値を長く効かせる」…先だけ重く／柔らかくしたいときに使います。\n"
              + "「中ほどで一気に変える」…根元と先をはっきり分けたいときに使います。"));

            var row = Row();
            row.Add(Btn("選んだところへ適用",   OnSetJoint,   grow: true));
            row.Add(Btn("選んだところから外す", OnClearJoint, grow: true));
            fo.Add(row);

            var row2 = Row();
            row2.Add(Btn("根元から末端へ配って適用", OnApplyJointGradient, grow: true));
            fo.Add(row2);
            fo.Add(Hint(
                "配る順は「① 揺らすボーンを選ぶ」で選んだ順（＝親から子への順）です。\n"
              + "先に「選んだボーンから下をまとめて選ぶ」を押しておいてください。"));

            root.Add(fo);
        }

        // ── 揺れる向きを制限する ──────────────────────────────────────
        private void BuildAngleLimitSection(VisualElement root)
        {
            var fo = new Foldout { text = "揺れる向きを制限する", value = false };

            fo.Add(new HelpBox(
                "決めた向きから何度まで倒れてよいかを決めます。\n"
                + "当たり判定より軽く、髪が頭にめり込むのを抑えるのに向きます。\n"
                + "対応していないビューアでは無視され、制限なしとして動きます。",
                HelpBoxMessageType.Info));

            fo.Add(new HelpBox(
                "この版では、値はプロジェクトに保存されますが VRM には書き出されません。"
                + "書き出しに必要なアセンブリ参照が未設定のためです。"
                + "VRM を書き出すと、その旨が書き出し結果の警告に出ます。",
                HelpBoxMessageType.Warning));

            // 並びは SpringBoneAngleLimitType の値の順。index をそのまま
            // enum の値として使うので、並べ替えないこと。
            _angleLimitField = new DropdownField(
                "制限のしかた", new List<string>(AngleLimitChoices), 0);
            fo.Add(_angleLimitField);
            fo.Add(Hint(
                "制限しない＝既定。倒れる向きを縛りません。\n"
              + "円錐＝初期の向きを中心に、まわり一様に開きを決めます。まず試すならこれ。\n"
              + "蝶番＝1 つの向きにだけ折れます。扇や札のように平たいものに。\n"
              + "球面＝前後と左右で別々に開きを決めます。"));

            _pitchDegField = new FloatField("開き[度]") { value = 180f };
            fo.Add(_pitchDegField);
            fo.Add(Hint(
                "0〜180。180 で制限なしと同じ広さです。\n"
              + "髪が頭にめり込むときは 60〜90 くらいから試します。"));

            _yawDegField = new FloatField("もう一方の開き[度]") { value = 0f };
            fo.Add(_yawDegField);
            fo.Add(Hint("0〜90。Spherical のときだけ読みます。"));

            fo.Add(Hint(
                "値は「③ 揺れ方」の「選んだところへ適用」「根元から末端へ配って適用」で"
              + "一緒に書き込まれます。ここだけを押すボタンはありません。"));

            root.Add(fo);
        }

        // ── 値の決め方 ────────────────────────────────────────────────
        private void BuildRecipeSection(VisualElement root)
        {
            var fo = new Foldout { text = "値の決め方", value = false };

            fo.Add(new HelpBox(
                "まず全段を既定のまま入れて動かし、出た症状から下の順に直します。\n"
                + "一度に 2 つ以上変えると、どちらが効いたか判らなくなります。",
                HelpBoxMessageType.Info));

            fo.Add(Hint("揺れが止まらない　　　→ 減衰を上げる（0.4 → 0.6〜0.8）"));
            fo.Add(Hint("元の形に戻らない　　　→ かたさを上げる（1 → 2〜4）"));
            fo.Add(Hint("垂れない　　　　　　　→ 重力の強さを 0 から 0.1 ずつ上げる"));
            fo.Add(Hint("体を突き抜ける　　　　→ 当たり半径を上げる。それでも駄目なら当たり判定の半径"));
            fo.Add(Hint("根元まで揺れて崩れる　→ かたさを「根元の値を長く残す」で配り直す"));
            fo.Add(Hint("走ると暴れる　　　　　→「揺れの基準にする体のボーン」に腰のボーン名を入れる"));
            fo.Add(Hint("末端だけ動かない　　　→ 鎖の先のボーンが無い。「鎖の先のボーン」で足す"));
            fo.Add(Hint("頭に髪がめり込む　　　→「揺れる向きを制限する」を Cone にして開きを狭める"));
            fo.Add(Hint("そもそも何も動かない　→ 重みが乗っていない。下の「重み（ウェイト）の付け方」へ"));

            root.Add(fo);
        }

        // ── 重み（ウェイト）の付け方 ──────────────────────────────────
        //
        // 【なぜパネルに書くか】
        //   揺れるボーンを作っても、メッシュの頂点がそのボーンに結び付いて
        //   いなければ何も動かない。ここでつまずく人が多いのに、
        //   「揺れもの」の画面には手掛かりが何も無かった。
        private void BuildWeightGuideSection(VisualElement root)
        {
            var fo = new Foldout { text = "重み（ウェイト）の付け方", value = false };

            fo.Add(new HelpBox(
                "揺れるボーンを作っただけでは、メッシュは動きません。\n"
                + "「どの頂点が、どのボーンについていくか」を決める重みが要ります。\n"
                + "重みは 1 頂点あたりの合計が 1 になるように配ります。",
                HelpBoxMessageType.Info));

            fo.Add(Sec("手順"));
            fo.Add(Hint(
                "1. 鎖のいちばん上のあたりは、体側のボーン（スカートなら腰、髪なら頭）へ"
              + "重みを乗せます。ここが揺れると、服や髪が体から外れて見えます。"));
            fo.Add(Hint(
                "2. そこから下は、段の順に鎖のボーンへ乗せていきます。"
              + "上から下へグラデーションで一度に引き、要らない向き（下・左・右・後ろ）から"
              + "消していくと、1 本あたり 2〜4 回で塗り終わります。"));
            fo.Add(Hint(
                "3. 隣り合う段の境目は、必ず両方のボーンへ均等に分けます。"
              + "片方に寄せると、そこだけ折れ曲がったように尖ります。"));
            fo.Add(Hint(
                "4. 塗り終わったら必ず正規化します。合計が 1 でない頂点は原点の方へ寄ります。"
              + "「スキンW数値設定」パネルから正規化できます。"));
            fo.Add(Hint(
                "5. 鎖のいちばん先のボーンには重みを乗せません。"
              + "1 つ手前の向きを決めるためだけのボーンです。"));

            fo.Add(Sec("よくある失敗"));
            fo.Add(Hint(
                "・重みを乗せていない　→ ボーンは揺れているのにメッシュが動かない。"
              + "この症状のときは、まず重みを疑います。"));
            fo.Add(Hint(
                "・体側のボーンに乗せ忘れた　→ スカートの腰まわりや髪の生え際がめくれる。"));
            fo.Add(Hint(
                "・1 頂点を 3 段以上のボーンに分けた　→ どこが動いているか追えなくなる。"
              + "柔らかい素材でも、隣り合う 2 段までにとどめるのが無難です。"));
            fo.Add(Hint(
                "・揺らしたくない短い髪や横髪まで鎖に乗せた　→ 頭のボーンへ多めに乗せ、"
              + "鎖側の重みを下げます。"));
            fo.Add(Hint(
                "・鎖の枝分かれに 1 本の鎖を通した　→ 房の根元を 1 本ずつ別の鎖にします。"
              + "鎖は枝分かれを想定していません。"));

            fo.Add(Sec("この画面からの近道"));
            fo.Add(Hint(
                "「図形生成 →『揺れものボーン（3D連携）』」で鎖を作ると、"
              + "段ごとの名前付きセット（〈接頭辞〉_段0、_段1 …）も一緒に作られます。\n"
              + "「メッシュ選択セット」パネルからその段を呼び出し、"
              + "「スキンW数値設定」でその段のボーンへ 1.0 を入れると、段ごとにまとめて塗れます。"));

            root.Add(fo);
        }

        // ── 鎖の先のボーン ────────────────────────────────────────────
        private void BuildTailSection(VisualElement root)
        {
            var fo = new Foldout { text = "鎖の先のボーン", value = false };

            var tailHelp = new HelpBox(
                "鎖のいちばん先のボーンは、1 つ手前の向きを決めるためだけに使われ、自分は揺れません。\n"
                + "子の無いボーンで鎖が終わると 1 段ぶん短くなるので、先に短いボーンを足します。",
                HelpBoxMessageType.Info);
            tailHelp.style.marginBottom = 4;
            fo.Add(tailHelp);

            _tailLengthField = new FloatField("長さ[m]") { value = SpringBoneOps.DefaultTailLength };
            _tailSuffixField = new TextField("足すボーンの名前") { value = SpringBoneOps.DefaultTailSuffix };
            _tailJointToggle = new Toggle("足したボーンにも揺れ方を入れる") { value = true };
            fo.Add(_tailLengthField);
            fo.Add(Hint(
                $"親から自分への向きへ、この長さだけ伸ばした子ボーンを足します。"
              + $"既定 {Fmt(SpringBoneOps.DefaultTailLength)}m。\n"
              + "長すぎると先が実物より外まで揺れます。髪や裾の 1 段ぶんより短くします。"));
            fo.Add(_tailSuffixField);
            fo.Add(Hint(
                "足すボーンの名前は〈元のボーン名〉＋この文字列になります。\n"
              + "_end なら、SBTest_Tail_03 の先に足したボーンは SBTest_Tail_03_end になります。"));
            fo.Add(_tailJointToggle);

            var row = Row();
            row.Add(Btn("選んだボーンの先に足す", OnAddTailBone, grow: true));
            fo.Add(row);

            root.Add(fo);
        }

        // ── 当たり判定のまとまり ──────────────────────────────────────
        private void BuildGroupSection(VisualElement root)
        {
            var fo = new Foldout { text = "当たり判定のまとまり", value = false };

            _groupListView = MakeList(_groupRows, OnGroupSelectionChanged);
            fo.Add(_groupListView);

            _groupNameField = new TextField("名前");
            fo.Add(_groupNameField);
            fo.Add(Hint(
                "当たり判定はボーンに付き、まとまりに入れて、鎖ごとに"
              + "「どのまとまりとぶつかるか」を選びます。\n"
              + "体側（腰・脚・頭・胸）にまとまりを作り、スカートや髪の鎖から指す使い方になります。\n"
              + "削除すると、後ろのまとまりの番号が 1 つずつ詰まります。\n\n"
              + "当たり判定そのものを作る・直すのは、左の「当たり判定の作成と編集」です。"));

            var row = Row();
            row.Add(Btn("追加",     OnAddGroup,    grow: true));
            row.Add(Btn("名前変更", OnRenameGroup, grow: true));
            row.Add(Btn("削除",     OnDeleteGroup, grow: true));
            fo.Add(row);

            root.Add(fo);
        }

        // ── 評価設定 ──────────────────────────────────────────────────
        private void BuildSettingsSection(VisualElement root)
        {
            var fo = new Foldout { text = "評価設定", value = false };

            _fixedDtField = new FloatField("固定タイムステップ[秒]");
            _fixedDtField.tooltip = "0 にすると実時間で評価する。";
            _warmupField  = new IntegerField("安定化フレーム数");
            fo.Add(_fixedDtField);
            fo.Add(Hint(
                "0 にすると実際の経過時間で評価します。フレームレートが揺らぐ環境で"
              + "揺れ方を一定にしたいときだけ、0.0166（60fps 相当）などを入れます。"));
            fo.Add(_warmupField);
            fo.Add(Hint(
                "評価を始めた直後に空回しするフレーム数です。"
              + "0 にすると、表示した瞬間に髪が落下してから戻る動きが見えます。"));

            var row = Row();
            row.Add(Btn("適用", OnApplySettings, grow: true));
            fo.Add(row);

            root.Add(fo);
        }

        // ── 検査 ──────────────────────────────────────────────────────
        private void BuildIssueSection(VisualElement root)
        {
            var fo = new Foldout { text = "検査", value = true };

            _issueListView = MakeList(_issueRows, OnIssueSelectionChanged);
            _issueListView.style.maxHeight = 160;
            fo.Add(_issueListView);

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
                if (_placeLabel != null) _placeLabel.text = "";
                _chainRows.Clear(); _chainMasters.Clear();
                _groupRows.Clear();
                _issueRows.Clear(); _issueMasters.Clear();
                RebuildLists();
                return;
            }

            // 対象の表示
            var targets = SelectedTargets(model);
            int carriers = 0;
            foreach (int i in targets) if (SpringBoneOps.IsCarrier(model, i)) carriers++;

            string firstName = targets.Count > 0
                ? (model.GetMeshContext(targets[0])?.Name ?? "")
                : "";
            _targetLabel.text = targets.Count == 0
                ? "選択なし。ボーンか描画オブジェクトを選んでください。"
                : $"選択 {targets.Count} 件（うち揺れを付けられる {carriers} 件）  先頭: {firstName}";

            var childrenOf = MeshHierarchyOps.BuildChildrenTable(model);

            // 鎖の一覧
            _chainRows.Clear();
            _chainMasters.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo?.SpringBoneChainRoot == null) continue;

                int members = SpringBoneOps.CollectJointMembers(model, childrenOf, i).Count;
                _chainMasters.Add(i);
                _chainRows.Add($"{SpringBoneOps.ChainName(mo, mc)}  ({mc.Name} / {members} 段)");
            }
            if (_chainRows.Count == 0) _chainRows.Add("まだ揺れる場所が登録されていません。");

            // まとまりの一覧
            _groupRows.Clear();
            var names = model.SpringBoneColliderGroupNames;
            if (names != null)
                for (int i = 0; i < names.Count; i++)
                    _groupRows.Add($"[{i}] {names[i]}");

            // 評価設定
            _fixedDtField?.SetValueWithoutNotify(model.SpringBoneFixedDeltaTime);
            _warmupField?.SetValueWithoutNotify(model.SpringBoneWarmupFrames);

            // 検査
            _issueRows.Clear();
            _issueMasters.Clear();
            foreach (var issue in SpringBoneOps.Validate(model))
            {
                _issueRows.Add((issue.IsError ? "NG  " : "注意  ") + issue.Message);
                _issueMasters.Add(issue.MasterIndex);
            }
            if (_issueRows.Count == 0) _issueRows.Add("問題は見つかりませんでした。");

            RebuildLists();
            UpdateHighlight(model, childrenOf);
        }

        private void RebuildLists()
        {
            _suppressListEvents = true;
            try
            {
                RebuildOne(_chainListView, _chainRows, ref _selectedChainRow);
                RebuildOne(_groupListView, _groupRows, ref _selectedGroupRow);

                if (_issueListView != null)
                {
                    _issueListView.itemsSource = _issueRows;
                    _issueListView.Rebuild();
                }
            }
            finally
            {
                _suppressListEvents = false;
            }
        }

        private static void RebuildOne(ListView view, List<string> rows, ref int selected)
        {
            if (view == null) return;
            view.itemsSource = rows;
            view.Rebuild();
            selected = Mathf.Clamp(selected, -1, rows.Count - 1);
            if (selected >= 0) view.SetSelection(selected);
        }

        // ================================================================
        // 3D 画面の強調表示
        // ================================================================

        /// <summary>
        /// いま触っている鎖と、その中の何段目かを 3D 画面へ伝える。
        ///
        /// 【鎖の決め方】
        ///   選択の先頭ボーンから親を辿り、いちばん上の「揺れの根元」を探す。
        ///   見つからなければ選択の先頭を根元とみなし、下だけを集める。
        /// </summary>
        private void UpdateHighlight(ModelContext model, Dictionary<int, List<int>> childrenOf)
        {
            if (model == null) return;

            var targets = SelectedTargets(model);
            int active = targets.Count > 0 ? targets[0] : -1;

            var members = new List<int>();
            int rootIndex = -1;

            if (active >= 0)
            {
                rootIndex = FindChainRoot(model, active);
                if (rootIndex >= 0)
                    members = SpringBoneOps.CollectJointMembers(model, childrenOf, rootIndex);
            }

            model.SpringBoneHighlightIndices = members;
            model.SpringBoneHighlightActiveIndex = active;

            // 文字でも同じことを出す。色だけでは何段目かまでは読めない。
            if (_placeLabel != null)
            {
                if (active < 0 || members.Count == 0)
                {
                    _placeLabel.text = "";
                }
                else
                {
                    int step = members.IndexOf(active);
                    var rootMc = model.GetMeshContext(rootIndex);
                    string chainName = SpringBoneOps.ChainName(rootMc?.MeshObject, rootMc);
                    string activeName = model.GetMeshContext(active)?.Name ?? "";

                    _placeLabel.text = step >= 0
                        ? $"いまの場所: 鎖「{chainName}」の {step + 1} / {members.Count} 段目（{activeName}）"
                        : $"いまの場所: 鎖「{chainName}」の外（{activeName}）";
                }
            }

            OnHighlightChanged?.Invoke();
        }

        /// <summary>
        /// 親を辿って、いちばん上の「揺れの根元」を返す。
        /// 見つからなければ自分自身を返す（下へ辿るぶんには同じ結果になる）。
        /// </summary>
        private static int FindChainRoot(ModelContext model, int index)
        {
            if (model == null || index < 0 || index >= model.MeshContextCount) return -1;

            var parents = MeshHierarchyOps.BuildParentIndicesFromDepth(model);

            int cur = index;
            int found = -1;
            var guard = new HashSet<int>();

            while (cur >= 0 && cur < model.MeshContextCount && guard.Add(cur))
            {
                var mo = model.GetMeshContext(cur)?.MeshObject;
                if (mo?.SpringBoneChainRoot != null) found = cur;

                cur = (parents != null && cur < parents.Length) ? parents[cur] : -1;
            }

            return found >= 0 ? found : index;
        }

        /// <summary>
        /// パネルを離れるときに強調表示を消す。
        /// 何も付いていなければ何もしない（パネル切替のたびに
        /// ビューポートを作り直させないため）。
        /// </summary>
        public void ClearHighlight()
        {
            var model = CurrentModel;
            if (model == null) return;

            bool had = model.SpringBoneHighlightActiveIndex >= 0
                    || (model.SpringBoneHighlightIndices != null &&
                        model.SpringBoneHighlightIndices.Count > 0);
            if (!had) return;

            model.ClearSpringBoneHighlight();
            if (_placeLabel != null) _placeLabel.text = "";
            OnHighlightChanged?.Invoke();
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnSelectChain()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("揺れの根元にするボーンを選んでください。"); return; }

            SendCmd(new SelectBoneChainCommand(
                ModelIndex, targets[0], SelectedWalk(), _additiveToggle.value));

            SetStatus("下のボーンまでまとめて選びました。");
        }

        private void OnSelectByWeight()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var meshes = model.SelectedDrawableMeshIndices;
            if (meshes == null || meshes.Count == 0)
            {
                SetStatus("描画オブジェクトを選んでください。");
                return;
            }

            SendCmd(new SelectBonesByVertexWeightCommand(
                ModelIndex, new List<int>(meshes).ToArray(),
                _minWeightField.value, _additiveToggle.value));

            SetStatus("重みの掛かったボーンを選びました。");
        }

        private void OnSetChainRoot()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("揺れの根元にするボーンを選んでください。"); return; }

            SendCmd(new SetSpringBoneChainRootCommand(
                ModelIndex, targets[0],
                _chainNameField.value ?? "",
                _centerBoneField.value ?? "",
                ParseIndices(_chainGroupsField.value)));

            SetStatus("ここから揺れるようにしました。");
            Refresh();
        }

        private void OnClearChainRoot()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("対象を選んでください。"); return; }

            SendCmd(new ClearSpringBoneChainRootCommand(ModelIndex, targets.ToArray()));
            SetStatus("揺れをやめました。");
            Refresh();
        }

        private void OnSetJoint()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("対象を選んでください。"); return; }

            SendCmd(BuildJointCommand(targets.ToArray(),
                _hitRadiusField.value, _stiffnessField.value,
                _gravityPowerField.value, _dragField.value));

            SetStatus($"{targets.Count} 件へ適用しました。");
            Refresh();
        }

        /// <summary>
        /// 選択順（＝親から子への順）に、4 つの値を根元→末端で配る。
        ///
        /// 【コマンドを増やさない理由】
        ///   VRM の揺れ方は段ごとに値を持てるので、既存の
        ///   SetSpringBoneJointCommand を 1 段ずつ送れば足りる。
        ///   配り方の指定をコマンドへ足すと、同じことを 2 通りで表せてしまう。
        /// </summary>
        private void OnApplyJointGradient()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("対象を選んでください。"); return; }

            if (targets.Count < 2)
            {
                SetStatus("配るには 2 段以上の選択が要ります。"
                        + "「選んだボーンから下をまとめて選ぶ」で鎖を選んでください。");
                return;
            }

            var stiff = MakeTaper(_stiffnessField.value,    _stiffnessTipField.value);
            var drag  = MakeTaper(_dragField.value,         _dragTipField.value);
            var grav  = MakeTaper(_gravityPowerField.value, _gravityPowerTipField.value);
            var hit   = MakeTaper(_hitRadiusField.value,    _hitRadiusTipField.value);

            int n = targets.Count;
            for (int i = 0; i < n; i++)
            {
                SendCmd(BuildJointCommand(
                    new[] { targets[i] },
                    hit.EvaluateStep(i, n),
                    stiff.EvaluateStep(i, n),
                    grav.EvaluateStep(i, n),
                    drag.EvaluateStep(i, n)));
            }

            SetStatus(
                $"{n} 段へ配りました。"
              + $"かたさ {Fmt(stiff.Root)}→{Fmt(stiff.Tip)} / "
              + $"減衰 {Fmt(drag.Root)}→{Fmt(drag.Tip)} / "
              + $"重力 {Fmt(grav.Root)}→{Fmt(grav.Tip)} / "
              + $"当たり半径 {Fmt(hit.Root)}→{Fmt(hit.Tip)}"
              + $"（{TaperShapeChoices[SelectedTaperShape()]}）");
            Refresh();
        }

        private void OnClearJoint()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("対象を選んでください。"); return; }

            SendCmd(new ClearSpringBoneJointCommand(ModelIndex, targets.ToArray()));
            SetStatus("揺れ方を外しました。");
            Refresh();
        }

        private void OnAddTailBone()
        {
            var model = CurrentModel;
            if (model == null) { SetStatus("モデルが読み込まれていません。"); return; }

            var targets = SelectedTargets(model);
            if (targets.Count == 0) { SetStatus("いちばん先のボーンを選んでください。"); return; }

            SendCmd(new AddSpringBoneTailBoneCommand(
                ModelIndex, targets.ToArray(),
                _tailLengthField.value, _tailSuffixField.value ?? "", _tailJointToggle.value));

            SetStatus("先のボーンを足しました。");
            Refresh();
        }

        private void OnAddGroup()
        {
            SendCmd(new AddSpringBoneColliderGroupCommand(ModelIndex, _groupNameField.value ?? ""));
            _groupNameField?.SetValueWithoutNotify("");
            SetStatus("まとまりを足しました。");
            Refresh();
        }

        private void OnRenameGroup()
        {
            if (_selectedGroupRow < 0) { SetStatus("まとまりを選んでください。"); return; }

            string newName = _groupNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("新しい名前を入れてください。"); return; }

            SendCmd(new RenameSpringBoneColliderGroupCommand(ModelIndex, _selectedGroupRow, newName));
            _groupNameField?.SetValueWithoutNotify("");
            SetStatus($"名前を {newName} に変えました。");
            Refresh();
        }

        private void OnDeleteGroup()
        {
            if (_selectedGroupRow < 0) { SetStatus("まとまりを選んでください。"); return; }

            var names = CurrentModel?.SpringBoneColliderGroupNames;
            string name = (names != null && _selectedGroupRow < names.Count)
                ? names[_selectedGroupRow] : "?";

            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "削除確認",
                $"当たり判定のまとまり「{name}」を消します。\n"
                + "入っていた当たり判定と、鎖からの指定は外れます。"
                + "後ろのまとまりの番号は詰められます。",
                "削除", "キャンセル");
            if (!ok) return;

            SendCmd(new DeleteSpringBoneColliderGroupCommand(ModelIndex, _selectedGroupRow));
            _selectedGroupRow = -1;
            SetStatus($"削除しました: {name}");
            Refresh();
        }

        private void OnApplySettings()
        {
            SendCmd(new SetSpringBoneSettingsCommand(
                ModelIndex, _fixedDtField.value, _warmupField.value));
            SetStatus("評価設定を変えました。");
        }

        // ================================================================
        // 一覧の選択
        // ================================================================

        private void OnChainSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            _selectedChainRow = _chainListView.selectedIndex;
            if (_selectedChainRow < 0 || _selectedChainRow >= _chainMasters.Count) return;

            int master = _chainMasters[_selectedChainRow];
            var mo = CurrentModel?.GetMeshContext(master)?.MeshObject;
            var chain = mo?.SpringBoneChainRoot;
            if (chain == null) return;

            _chainNameField?.SetValueWithoutNotify(chain.Name ?? "");
            _centerBoneField?.SetValueWithoutNotify(chain.CenterBoneName ?? "");
            _chainGroupsField?.SetValueWithoutNotify(JoinIndices(chain.SpringBoneColliderGroupIndices));

            // 3D 画面・メッシュリストと同じ経路で選び直す。
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Bone, new[] { master }));
        }

        private void OnGroupSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            _selectedGroupRow = _groupListView.selectedIndex;

            var names = CurrentModel?.SpringBoneColliderGroupNames;
            if (names != null && _selectedGroupRow >= 0 && _selectedGroupRow < names.Count)
                _groupNameField?.SetValueWithoutNotify(names[_selectedGroupRow]);
        }

        private void OnIssueSelectionChanged(IEnumerable<object> _)
        {
            if (_suppressListEvents) return;

            int row = _issueListView.selectedIndex;
            if (row < 0 || row >= _issueMasters.Count) return;

            int master = _issueMasters[row];
            if (master < 0) return;

            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Bone, new[] { master }));
        }

        // ================================================================
        // 値の組み立て
        // ================================================================

        /// <summary>
        /// 画面の値から揺れ方のコマンドを 1 つ作る。
        /// 角度制限は「③ 揺れ方」からの適用と一緒に書き込む。
        /// </summary>
        private SetSpringBoneJointCommand BuildJointCommand(
            int[] masters, float hitRadius, float stiffness, float gravityPower, float drag)
        {
            var dir = new Vector3(_gravityDirX.value, _gravityDirY.value, _gravityDirZ.value);

            int limitIndex = _angleLimitField?.index ?? 0;
            if (limitIndex < 0 || limitIndex >= AngleLimitChoices.Length) limitIndex = 0;
            var limitType = (SpringBoneAngleLimitType)limitIndex;

            float pitch = Mathf.Deg2Rad * Mathf.Clamp(_pitchDegField?.value ?? 180f, 0f, 180f);
            float yaw   = Mathf.Deg2Rad * Mathf.Clamp(_yawDegField?.value   ?? 0f,   0f,  90f);

            return new SetSpringBoneJointCommand(
                ModelIndex, masters,
                hitRadius, stiffness, gravityPower, dir, drag,
                limitType, Vector3.zero, pitch, yaw);
        }

        /// <summary>
        /// 画面の指定から配り方を作る。
        /// 「根元と末端で値を変える」が外れているときは全段同値にする。
        /// </summary>
        private SpringBoneTaper MakeTaper(float rootValue, float tipValue)
        {
            if (_taperEnabled == null || !_taperEnabled.value)
                return new SpringBoneTaper(rootValue, rootValue);

            var taper = new SpringBoneTaper(rootValue, tipValue);

            switch (SelectedTaperShape())
            {
                case 1:  taper.Gamma = SpringBoneTaper.GammaHoldRoot;  break;
                case 2:  taper.Gamma = SpringBoneTaper.GammaHoldTip;   break;
                case 3:  taper.Curve = SpringBoneTaper.SCurve();       break;
                default: taper.Gamma = SpringBoneTaper.GammaStraight;  break;
            }
            return taper;
        }

        private int SelectedTaperShape()
        {
            int i = _taperShapeField?.index ?? 0;
            return (i >= 0 && i < TaperShapeChoices.Length) ? i : 0;
        }

        private SpringBoneChainWalk SelectedWalk()
            => (_walkField != null && _walkField.index == 1)
                ? SpringBoneChainWalk.AllDescendants
                : SpringBoneChainWalk.FirstChild;

        // ================================================================
        // 小物
        // ================================================================

        /// <summary>
        /// 対象。ボーン選択を主に見て、無ければ描画オブジェクトの選択を使う。
        /// 非スキンドの描画オブジェクトにも揺れを付けられるため、両方拾う。
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

        /// <summary>カンマ区切りの番号を読む。数でないものは飛ばす。</summary>
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
            if (indices == null || indices.Count == 0) return "";
            return string.Join(",", indices);
        }

        private ListView MakeList(List<string> source, Action<IEnumerable<object>> onChanged)
        {
            var view = new ListView(source, 20,
                () => new Label(),
                (elem, i) =>
                {
                    if (elem is Label l && i >= 0 && i < source.Count) l.text = source[i];
                });
            view.selectionType   = SelectionType.Single;
            view.style.minHeight = 60;
            view.style.maxHeight = 120;
            view.style.marginBottom = 4;
            view.selectionChanged += onChanged;
            return view;
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

        /// <summary>
        /// 欄の下に置く小さな説明。
        /// 見た目は PlayerStagedTestSubPanelBase.Hint にそろえてあるが、
        /// このパネルはあちらを継承していないので同じものをここに持つ。
        /// </summary>
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
