// PlayerNormalEditSubPanel.cs
// 法線編集サブパネル。実処理は NormalEditOps（Execute）/ NormalBrushOps。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置
//
// 対象範囲のルール（NormalEditOps.CollectTargetCorners）
//   面選択がある         → その面のコーナーのみ
//   頂点か辺の選択がある → 選択頂点と辺の両端頂点が参照する全スロット
//   選択が無い           → メッシュ全体
// スムージング角での再計算だけはスロットを作り直すためメッシュ全体が対象
// （法線再計算の除外セットが指すコーナーは元の法線を保つ）。
// 「手動の法線編集からも守る」除外セットのコーナーはどの操作の対象にもならない。
//
// 適用先のオブジェクトはディスパッチャの CollectSelectedMeshContexts と同じ規則
// （選択中の描画オブジェクト全部。無ければ編集対象メッシュ単体）。
//
// 【プレビュー】
//   「プレビューしながら調整する」を付けると、操作ボタンは確定せずプレビューを始める。
//   強度・角度・座標などを変えると、開始時の状態から計算し直して表示する
//   （NormalEditPreviewState）。「決定」で元に戻してから NormalEditCommand を送るので
//   Undo は 1 件、「取消」で開始時へ戻る。法線移植のプレビューと同じ方式。
//
// 【ビューポート操作】
//   ハンドル・直接回転・ブラシ・スポイトは NormalEditToolHandler が受け、
//   プレビューと確定はこのパネルの口（Begin/Preview/Commit）を通る。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerNormalEditSubPanel
    {
        // ================================================================
        // 配線（Viewer が設定する）
        // ================================================================

        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand> SendCommand;

        /// <summary>直近の実行結果。Viewer から設定する（PlayerCommandDispatcher が持つ）。</summary>
        public Func<NormalEditResult> GetLastResult;

        /// <summary>プレビューで書き換える実体のモデル。</summary>
        public Func<ModelContext> GetModel;
        /// <summary>法線だけ変わったメッシュの表示同期（差し替えられなければ作り直す）。</summary>
        public Action<MeshContext> OnSyncMeshNormals;
        /// <summary>スロット数が変わった・ミラーを作り直したときの表示の作り直し。</summary>
        public Action OnNotifyTopologyChanged;
        /// <summary>プレビュー開始前の担当者判定とロック取得（null なら常に許可）。</summary>
        public Func<IList<int>, bool> TryLockForPreview;
        /// <summary>プレビュー終了時のロック解除。</summary>
        public Action UnlockAfterPreview;

        /// <summary>法線表示（全ビューポート）の読み書き。</summary>
        public Func<bool> GetShowNormals;
        public Action<bool> SetShowNormals;
        /// <summary>法線表示の線を作り直させる（色分けの切り替え・選択の変化）。</summary>
        public Action RequestNormalLinesRebuild;

        /// <summary>ビューポート操作の種類を切り替える（InteractionMode.NormalEdit のサブモード）。</summary>
        public Action<NormalEditToolHandler.SubMode> SetViewportTool;
        public Func<NormalEditToolHandler.SubMode> GetViewportTool;

        // ================================================================
        // 選択肢
        // ================================================================

        private static readonly List<string> WeightNames = new List<string>
        {
            "均等", "角度", "面積", "角度×面積"
        };

        private static readonly List<string> AxisNames = new List<string> { "X", "Y", "Z" };

        private static readonly List<string> BrushModeNames = new List<string>
        {
            "方向へ寄せる", "平滑化", "範囲の平均"
        };

        // ================================================================
        // UI（自動操作の ID は "normalEdit.<下の Id>"。UiControlAttribute.cs）
        // ================================================================

        [UiControl("warning", Safety = UiSafety.ReadOnly, Description = "メッシュが選択されていないときの警告（それ以外は非表示）")]
        private Label          _warningLabel;
        [UiControl("meshName", Safety = UiSafety.ReadOnly, Description = "対象メッシュ名")]
        private Label          _meshNameLabel;
        [UiControl("currentSelection", Safety = UiSafety.ReadOnly, Description = "選択中の頂点・辺・面の数と、操作の対象範囲")]
        private Label          _currentSelLabel;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "直近の操作の結果")]
        private Label          _statusLabel;

        // 表示と保護
        [UiControl("showNormals", Description = "法線を表示する（全ビューポート）")]
        private Toggle         _showNormalsToggle;
        [UiControl("scopeColoring", Description = "法線表示を範囲で色分けする（黄=対象 橙=共有で影響 水色=自動再計算から保護 赤=手動からも保護）")]
        private Toggle         _scopeColoringToggle;
        [UiControl("preserveNormalsState", Safety = UiSafety.ReadOnly, Description = "適用先の法線維持（PreserveNormals）の状態")]
        private Label          _preserveLabel;
        [UiControl("preserveOn", Safety = UiSafety.SafeWrite, Description = "適用先の法線維持を ON にする（頂点を動かしても法線を自動で再計算しない）")]
        private Button         _preserveOnBtn;
        [UiControl("preserveOff", Safety = UiSafety.SafeWrite, Description = "適用先の法線維持を OFF にする（頂点を動かすと法線を自動で再計算する）")]
        private Button         _preserveOffBtn;
        [UiControl("protectUnselected", Description = "選択外の面の法線を変えない（共有スロットを対象側だけ分離する）")]
        private Toggle         _protectUnselectedToggle;
        [UiControl("weight", Description = "面法線を平均するときの重み付け")]
        private DropdownField  _weightDropdown;
        [UiControl("blend", Description = "強度（元の法線との混合。スライダー）")]
        private Slider         _blendSlider;
        [UiControl("blendValue", Description = "強度（元の法線との混合。数値入力）")]
        private FloatField     _blendField;
        [UiControl("preview", Description = "プレビューしながら調整する（ボタンは確定せずプレビューを始める）")]
        private Toggle         _previewToggle;
        [UiControl("previewState", Safety = UiSafety.ReadOnly, Description = "プレビュー中の操作")]
        private Label          _previewStateLabel;
        [UiControl("previewApply", Safety = UiSafety.SafeWrite, Description = "プレビュー中の操作を決定する（1 件の Undo になる）")]
        private Button         _previewApplyBtn;
        [UiControl("previewCancel", Safety = UiSafety.SafeWrite, Description = "プレビューを取り消して開始時の法線へ戻す")]
        private Button         _previewCancelBtn;
        [UiControl(Ignore = true)]
        private VisualElement  _previewBar;

        // 再計算
        [UiControl("angle", Description = "再計算のスムージング角（スライダー）")]
        private Slider         _angleSlider;
        [UiControl("angleValue", Description = "再計算のスムージング角（数値入力）")]
        private FloatField     _angleField;
        [UiControl("recalcByAngle", Safety = UiSafety.SafeWrite, Description = "スムージング角でメッシュ全体の法線を作り直す（除外セットは保つ）")]
        private Button _recalcByAngleBtn;

        // 面法線と分離
        [UiControl("break", Safety = UiSafety.SafeWrite, Description = "面ごとに分離・フラット化（面ごとに別スロットへ分けて面法線を入れる）")]
        private Button _breakBtn;
        [UiControl("splitKeepDirection", Safety = UiSafety.SafeWrite, Description = "方向を保持して分離（今の法線のまま面ごとに別スロットへ分ける）")]
        private Button _splitKeepDirectionBtn;
        [UiControl("setFromFaces", Safety = UiSafety.SafeWrite, Description = "既存スロットの方向だけ面法線にする（共有している面にも効く）")]
        private Button _setFromFacesBtn;

        // 平均・平滑
        [UiControl("averageFromFaces", Safety = UiSafety.SafeWrite, Description = "面形状から滑らかに（対象面の面法線を頂点ごとに平均する）")]
        private Button _averageFromFacesBtn;
        [UiControl("unify", Safety = UiSafety.SafeWrite, Description = "頂点ごとに方向を揃える（今の法線を平均して同じ頂点の対象スロットを同方向にする）")]
        private Button _unifyBtn;
        [UiControl("averageAll", Safety = UiSafety.SafeWrite, Description = "全体を同一方向へ（対象全体の法線を 1 本の方向へ揃える）")]
        private Button _averageAllBtn;
        [UiControl("smoothStrength", Description = "平滑強度（スライダー）")]
        private Slider         _strengthSlider;
        [UiControl("smoothStrengthValue", Description = "平滑強度（数値入力）")]
        private FloatField     _strengthField;
        [UiControl("smooth", Safety = UiSafety.SafeWrite, Description = "隣接頂点の法線と補間する")]
        private Button _smoothBtn;

        // 方向指定
        [UiControl("target.x", Description = "球状化の中心／ターゲット指向の座標 X")]
        private FloatField     _targetX;
        [UiControl("target.y", Description = "球状化の中心／ターゲット指向の座標 Y")]
        private FloatField     _targetY;
        [UiControl("target.z", Description = "球状化の中心／ターゲット指向の座標 Z")]
        private FloatField     _targetZ;
        [UiControl("worldSpace", Description = "座標・方向をワールドで指定する（オフはオブジェクトのローカル）")]
        private Toggle         _worldSpaceToggle;
        [UiControl("sphereizeUseSelectionCenter", Description = "球状化の中心に選択の重心を使う")]
        private Toggle         _useCenterToggle;
        [UiControl("pointToTargetSingleVector", Description = "ターゲット指向を 1 本のベクトルに揃える")]
        private Toggle         _alignVectorsToggle;
        [UiControl("sphereize", Safety = UiSafety.SafeWrite, Description = "中心から頂点へ向かう方向を法線にする")]
        private Button _sphereizeBtn;
        [UiControl("pointToTarget", Safety = UiSafety.SafeWrite, Description = "座標へ向かう方向を法線にする")]
        private Button _pointToTargetBtn;
        [UiControl("axis", Description = "軸（軸への整列・軸成分を 0 にするときの軸）")]
        private DropdownField  _axisDropdown;
        [UiControl("alignToAxisPositive", Safety = UiSafety.SafeWrite, Description = "選択軸の正方向へ法線を向ける")]
        private Button _alignPositiveBtn;
        [UiControl("alignToAxisNegative", Safety = UiSafety.SafeWrite, Description = "選択軸の負方向へ法線を向ける")]
        private Button _alignNegativeBtn;
        [UiControl("flattenOnAxis", Safety = UiSafety.SafeWrite, Description = "選択軸の成分をゼロにして正規化する")]
        private Button _flattenOnAxisBtn;
        [UiControl("flip", Safety = UiSafety.SafeWrite, Description = "対象法線の向きを反転する")]
        private Button _flipBtn;
        [UiControl("direction.x", Description = "方向（指定方向へ向ける・回転軸・ブラシの寄せる向き）X")]
        private FloatField     _dirX;
        [UiControl("direction.y", Description = "方向（指定方向へ向ける・回転軸・ブラシの寄せる向き）Y")]
        private FloatField     _dirY;
        [UiControl("direction.z", Description = "方向（指定方向へ向ける・回転軸・ブラシの寄せる向き）Z")]
        private FloatField     _dirZ;
        [UiControl("alignToVector", Safety = UiSafety.SafeWrite, Description = "方向欄の向きへ法線を向ける")]
        private Button _alignToVectorBtn;
        [UiControl("rotateDeg", Description = "方向を軸に回すときの角度（スライダー）")]
        private Slider         _rotateSlider;
        [UiControl("rotateDegValue", Description = "方向を軸に回すときの角度（数値入力）")]
        private FloatField     _rotateField;
        [UiControl("rotateAxisAngle", Safety = UiSafety.SafeWrite, Description = "方向欄を軸に法線を回す")]
        private Button _rotateAxisAngleBtn;

        // ビューポート操作
        [UiControl("viewportTool", Safety = UiSafety.ReadOnly, Description = "いまのビューポート操作")]
        private Label  _viewportToolLabel;
        [UiControl("toolSelect", Safety = UiSafety.SafeWrite, Description = "ビューポートでは選択だけを行う")]
        private Button _toolSelectBtn;
        [UiControl("toolHandle", Safety = UiSafety.SafeWrite, Description = "座標欄の点をビューポートのハンドルで動かす")]
        private Button _toolHandleBtn;
        [UiControl("toolRotate", Safety = UiSafety.SafeWrite, Description = "ドラッグで対象法線を直接回す（1 ドラッグ＝1 回の確定）")]
        private Button _toolRotateBtn;
        [UiControl("toolBrush", Safety = UiSafety.SafeWrite, Description = "法線ブラシで塗る（1 ストローク＝1 回の確定）")]
        private Button _toolBrushBtn;
        [UiControl("toolSpoit", Safety = UiSafety.SafeWrite, Description = "クリックした頂点の法線を方向欄へ拾う")]
        private Button _toolSpoitBtn;
        [UiControl("brushMode", Description = "ブラシの効き方（方向へ寄せる / 平滑化 / 範囲の平均）")]
        private DropdownField _brushModeDropdown;
        [UiControl("brushRadius", Description = "ブラシ半径（スライダー。対象のローカル空間単位）")]
        private Slider        _brushRadiusSlider;
        [UiControl("brushRadiusValue", Description = "ブラシ半径（数値入力）")]
        private FloatField    _brushRadiusField;
        [UiControl("brushStrength", Description = "ブラシ強度（スライダー）")]
        private Slider        _brushStrengthSlider;
        [UiControl("brushStrengthValue", Description = "ブラシ強度（数値入力）")]
        private FloatField    _brushStrengthField;
        [UiControl("brushMirrorX", Description = "ブラシをローカル X で左右対称に掛ける")]
        private Toggle        _brushMirrorToggle;

        // 継ぎ目
        [UiControl("mirrorThreshold", Description = "中央とみなす範囲（|X 座標| がこの値以下の頂点が対象）")]
        private FloatField     _mirrorThresholdField;
        [UiControl("mirrorFlattenSeamX", Safety = UiSafety.SafeWrite, Description = "中央の陰影の継ぎ目を補正（|X 座標| がしきい値以下の頂点の法線 X 成分をゼロにする）")]
        private Button _mirrorFlattenSeamXBtn;
        [UiControl("seamDistance", Description = "オブジェクト間で重なりとみなすワールド距離")]
        private FloatField     _seamDistanceField;
        [UiControl("averageAcrossObjects", Safety = UiSafety.SafeWrite, Description = "オブジェクト間の継ぎ目を揃える（重なる頂点の法線をワールドで平均する）")]
        private Button _averageAcrossObjectsBtn;

        // ================================================================
        // 値
        // ================================================================

        private float _angleDeg      = 59.5f;
        private float _strength      = 0.5f;
        private float _blend         = 1f;
        private float _rotateDeg     = 15f;
        private float _brushRadius   = 0.05f;
        private float _brushStrength = 0.3f;

        /// <summary>中央判定しきい値の既定値（高度な選択の NearAxis と同じ）。</summary>
        private const float DefaultMirrorThreshold = 0.00001f;
        private const float DefaultSeamDistance    = 0.0001f;

        private readonly NormalEditPreviewState _preview = new NormalEditPreviewState();
        private NormalEditCommand.Op _previewOp;
        private bool _previewNegative;
        private bool _previewFromPanel;   // パネルのボタンで始めたプレビューか（ツールのドラッグ中は false）
        private bool _previewLocked;

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private Poly_Ling.View.IMeshView ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMesh;

        private NormalWeightMode WeightMode
            => (NormalWeightMode)Mathf.Clamp(_weightDropdown?.index ?? 0, 0, 3);

        private int Axis
            => Mathf.Clamp(_axisDropdown?.index ?? 0, 0, NormalEditCommand.AxisCount - 1);

        private Vector3 Target => new Vector3(
            _targetX?.value ?? 0f, _targetY?.value ?? 0f, _targetZ?.value ?? 0f);

        private Vector3 Direction => new Vector3(
            _dirX?.value ?? 0f, _dirY?.value ?? 1f, _dirZ?.value ?? 0f);

        private bool WorldSpace => _worldSpaceToggle?.value ?? false;

        private float MirrorThreshold
            => Mathf.Max(MirrorThresholdMin, _mirrorThresholdField?.value ?? DefaultMirrorThreshold);

        private float SeamDistance
            => Mathf.Clamp(_seamDistanceField?.value ?? DefaultSeamDistance, SeamDistanceMin, SeamDistanceMax);

        private NormalBrushMode BrushMode
            => (NormalBrushMode)Mathf.Clamp(_brushModeDropdown?.index ?? 0, 0, 2);

        /// <summary>ブラシ半径（ツールハンドラが読む）。</summary>
        public float BrushRadius => _brushRadius;

        // ================================================================
        // レンジ（上下限。実体は ParameterLimits。PanelCommand の LimitKey と同じキー）
        // ================================================================

        private static float AngleDegMin        => ParameterLimits.GetF("NormalEdit.AngleDeg.Min");
        private static float AngleDegMax        => ParameterLimits.GetF("NormalEdit.AngleDeg.Max");
        private static float StrengthMin        => ParameterLimits.GetF("NormalEdit.Strength.Min");
        private static float StrengthMax        => ParameterLimits.GetF("NormalEdit.Strength.Max");
        private static float MirrorThresholdMin => ParameterLimits.GetF("NormalEdit.MirrorThreshold.Min");
        private static float BlendMin           => ParameterLimits.GetF("NormalEdit.Blend.Min");
        private static float BlendMax           => ParameterLimits.GetF("NormalEdit.Blend.Max");
        private static float RotateDegMin       => ParameterLimits.GetF("NormalEdit.RotateDeg.Min");
        private static float RotateDegMax       => ParameterLimits.GetF("NormalEdit.RotateDeg.Max");
        private static float SeamDistanceMin    => ParameterLimits.GetF("NormalEdit.SeamDistance.Min");
        private static float SeamDistanceMax    => ParameterLimits.GetF("NormalEdit.SeamDistance.Max");
        private static float BrushRadiusMin     => ParameterLimits.GetF("NormalBrush.BrushRadius.Min");
        private static float BrushRadiusMax     => ParameterLimits.GetF("NormalBrush.BrushRadius.Max");
        private static float BrushStrengthMin   => ParameterLimits.GetF("NormalBrush.Strength.Min");
        private static float BrushStrengthMax   => ParameterLimits.GetF("NormalBrush.Strength.Max");

        // ================================================================
        // 構築
        // ================================================================

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("法線編集"));

            var help = new HelpBox(
                "面を選択していればその面のコーナー、頂点か辺を選択していればその頂点"
                + "（辺は両端）の全スロット、選択が無ければメッシュ全体が対象。"
                + "「角度で再計算」は常にメッシュ全体が対象で、法線再計算の除外セットは保たれる。"
                + "除外セットに「手動の法線編集からも守る」を付けると、どの操作の対象にもならない。"
                + "選択中の描画オブジェクト全部に適用し、Undo は 1 回で全部戻る。"
                + "スロットが増える操作（分離・選択外の保護）は Unity Mesh の頂点数を変えるので、"
                + "モーフの展開索引との対応がずれる。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize     = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _currentSelLabel = new Label();
            _currentSelLabel.style.fontSize     = 10;
            _currentSelLabel.style.marginBottom = 4;
            // 適用先ごとに 1 行。長い行は折り返す（パネル幅で切れないように）
            _currentSelLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_currentSelLabel);

            BuildDisplayAndProtection(root);
            BuildOperations(root);
            BuildViewportTools(root);
            BuildSeams(root);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop  = 4;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);

            UpdatePreviewBar();
        }

        private void BuildDisplayAndProtection(VisualElement root)
        {
            root.Add(SecLabel("表示と保護"));

            _showNormalsToggle = MkToggle("法線を表示する", false, v =>
            {
                SetShowNormals?.Invoke(v);
            });
            root.Add(_showNormalsToggle);

            _scopeColoringToggle = MkToggle("範囲で色分け（黄=対象 橙=共有で影響 水色=自動再計算から保護 赤=手動からも保護）", false, v =>
            {
                NormalEditOps.ScopeColoringEnabled = v;
                RequestNormalLinesRebuild?.Invoke();
            });
            _scopeColoringToggle.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_scopeColoringToggle);

            _preserveLabel = new Label();
            _preserveLabel.style.fontSize   = 10;
            _preserveLabel.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_preserveLabel);

            var rowPreserve = MkRow();
            rowPreserve.Add(_preserveOnBtn = MkBtn("法線維持をON", () => SendPreserve(true),
                "頂点を動かしても法線を自動で再計算しない。法線を編集すると自動で ON になる。"));
            rowPreserve.Add(_preserveOffBtn = MkBtn("法線維持をOFF", () => SendPreserve(false),
                "頂点を動かすと法線を自動で再計算する（手で付けた法線は消える。除外セットは保たれる）。"));
            root.Add(rowPreserve);

            _protectUnselectedToggle = MkToggle("選択外の面を保護する（共有スロットを分離）", true, _ => ReapplyPreview());
            _protectUnselectedToggle.tooltip =
                "対象と選択外の面が同じ法線スロットを使っているとき、対象側だけ新しいスロットへ分けてから書く。"
                + "オフにすると共有している選択外の面の法線も変わる。";
            root.Add(_protectUnselectedToggle);

            _weightDropdown = new DropdownField("平均の重み", WeightNames, 0);
            _weightDropdown.style.marginBottom = 2;
            _weightDropdown.tooltip = "面法線を平均するときの重み付け。角度=コーナー角、面積=面の広さ。";
            _weightDropdown.RegisterValueChangedCallback(_ => ReapplyPreview());
            root.Add(_weightDropdown);

            root.Add(MkSliderRow("強度", BlendMin, BlendMax, _blend, v => { _blend = v; ReapplyPreview(); },
                out _blendSlider, out _blendField));

            _previewToggle = MkToggle("プレビューしながら調整する", false, v =>
            {
                if (!v && _preview.IsActive && _previewFromPanel) CancelPreview();
                UpdatePreviewBar();
            });
            root.Add(_previewToggle);

            _previewBar = new VisualElement();
            _previewBar.style.marginBottom = 4;
            _previewStateLabel = new Label();
            _previewStateLabel.style.fontSize = 10;
            _previewStateLabel.style.color    = new StyleColor(new Color(1f, 0.85f, 0.4f));
            _previewBar.Add(_previewStateLabel);
            var rowPv = MkRow();
            rowPv.Add(_previewApplyBtn  = MkBtn("決定", CommitPanelPreview, "プレビュー中の操作を決定する（1 件の Undo）。"));
            rowPv.Add(_previewCancelBtn = MkBtn("取消", CancelPreview,      "プレビューを取り消して開始時の法線へ戻す。"));
            _previewBar.Add(rowPv);
            root.Add(_previewBar);
        }

        private void BuildOperations(VisualElement root)
        {
            // ── 再計算 ──
            root.Add(SecLabel("再計算"));
            root.Add(MkSliderRow("角度", AngleDegMin, AngleDegMax, _angleDeg, v => { _angleDeg = v; ReapplyPreview(); },
                out _angleSlider, out _angleField));
            var rowRecalc = MkRow();
            rowRecalc.Add(_recalcByAngleBtn = MkBtn("角度で再計算", () => Run(NormalEditCommand.Op.RecalcByAngle),
                "スムージング角でメッシュ全体の法線を作り直す。ハードエッジ分スロットが増える。除外セットは保たれる。強度は効かない。"));
            root.Add(rowRecalc);

            // ── 面法線と分離 ──
            root.Add(SecLabel("面法線と分離"));
            var rowFlat = MkRow();
            rowFlat.Add(_breakBtn = MkBtn("面ごとにフラット化", () => Run(NormalEditCommand.Op.Break),
                "面ごとに別スロットへ分けて面法線を入れる。スロットが増える。強度は効かない。"));
            rowFlat.Add(_splitKeepDirectionBtn = MkBtn("方向を保持して分離", () => Run(NormalEditCommand.Op.SplitKeepDirection),
                "今の法線の向きのまま、面ごとに別スロットへ分ける。スロットが増える。"));
            root.Add(rowFlat);
            var rowSetFace = MkRow();
            rowSetFace.Add(_setFromFacesBtn = MkBtn("既存スロットの方向だけ面法線に", () => Run(NormalEditCommand.Op.SetFromFaces),
                "スロットは分けずに、対象コーナーの法線を面法線にする。選択外を保護しないと、共有している面にも効く。"));
            root.Add(rowSetFace);

            // ── 平均・平滑 ──
            root.Add(SecLabel("平均・平滑"));
            var rowAvg = MkRow();
            rowAvg.Add(_averageFromFacesBtn = MkBtn("面形状から滑らかに", () => Run(NormalEditCommand.Op.AverageFromFaces),
                "対象面の面法線を頂点ごとに平均して書き込む。"));
            rowAvg.Add(_unifyBtn = MkBtn("頂点ごとに方向を揃える", () => Run(NormalEditCommand.Op.Unify),
                "今の法線を平均し、同じ頂点の対象スロットを同じ向きにする。スロット数は変わらない。"));
            root.Add(rowAvg);
            var rowAvg2 = MkRow();
            rowAvg2.Add(_averageAllBtn = MkBtn("全体を同一方向へ", () => Run(NormalEditCommand.Op.AverageAll),
                "対象全体の法線を 1 本の方向へ揃える。凹凸の陰影を平らにする。"));
            root.Add(rowAvg2);
            root.Add(MkSliderRow("平滑強度", StrengthMin, StrengthMax, _strength, v => { _strength = v; ReapplyPreview(); },
                out _strengthSlider, out _strengthField));
            var rowSmooth = MkRow();
            rowSmooth.Add(_smoothBtn = MkBtn("平滑化", () => Run(NormalEditCommand.Op.Smooth),
                "辺で繋がった隣接頂点の法線と補間する。共有スロットは 1 回だけ補間する。"));
            root.Add(rowSmooth);

            // ── 方向指定 ──
            root.Add(SecLabel("方向指定"));
            _worldSpaceToggle = MkToggle("座標・方向をワールドで指定する（オフはオブジェクトのローカル）", false, _ => ReapplyPreview());
            _worldSpaceToggle.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_worldSpaceToggle);

            var rowTarget = MkRow();
            rowTarget.Add(MkLabel("中心/向き先", 64));
            _targetX = MkFloat(0f); rowTarget.Add(_targetX);
            _targetY = MkFloat(0f); rowTarget.Add(_targetY);
            _targetZ = MkFloat(0f); rowTarget.Add(_targetZ);
            foreach (var f in new[] { _targetX, _targetY, _targetZ })
                f.RegisterValueChangedCallback(_ => { ReapplyPreview(); RefreshGizmoFromPanel?.Invoke(); });
            root.Add(rowTarget);

            _useCenterToggle = MkToggle("球状化の中心に選択の重心を使う", true, _ => ReapplyPreview());
            root.Add(_useCenterToggle);
            _alignVectorsToggle = MkToggle("ターゲット指向を1本のベクトルに揃える", false, _ => ReapplyPreview());
            root.Add(_alignVectorsToggle);

            var rowDir = MkRow();
            rowDir.Add(_sphereizeBtn = MkBtn("球状化", () => Run(NormalEditCommand.Op.Sphereize),
                "中心から頂点へ向かう方向を法線にする。丸みのある部位向け。"));
            rowDir.Add(_pointToTargetBtn = MkBtn("ターゲット指向", () => Run(NormalEditCommand.Op.PointToTarget),
                "座標へ向かう方向を法線にする。凹んだ部位向け。"));
            root.Add(rowDir);

            _axisDropdown = new DropdownField("軸", AxisNames, 0);
            _axisDropdown.style.marginBottom = 2;
            _axisDropdown.RegisterValueChangedCallback(_ => ReapplyPreview());
            root.Add(_axisDropdown);

            var rowAxis = MkRow();
            rowAxis.Add(_alignPositiveBtn = MkBtn("軸+へ整列", () => Run(NormalEditCommand.Op.AlignToAxis, negative: false),
                "選択軸の正方向へ法線を向ける。"));
            rowAxis.Add(_alignNegativeBtn = MkBtn("軸-へ整列", () => Run(NormalEditCommand.Op.AlignToAxis, negative: true),
                "選択軸の負方向へ法線を向ける。"));
            root.Add(rowAxis);

            var rowFlat2 = MkRow();
            rowFlat2.Add(_flattenOnAxisBtn = MkBtn("軸成分を0に", () => Run(NormalEditCommand.Op.FlattenOnAxis),
                "選択軸の成分をゼロにして正規化する。"));
            rowFlat2.Add(_flipBtn = MkBtn("反転", () => Run(NormalEditCommand.Op.Flip),
                "対象法線の向きを反転する。共有スロットは 1 回だけ反転する。"));
            root.Add(rowFlat2);

            var rowVec = MkRow();
            rowVec.Add(MkLabel("方向", 64));
            _dirX = MkFloat(0f); rowVec.Add(_dirX);
            _dirY = MkFloat(1f); rowVec.Add(_dirY);
            _dirZ = MkFloat(0f); rowVec.Add(_dirZ);
            foreach (var f in new[] { _dirX, _dirY, _dirZ })
                f.RegisterValueChangedCallback(_ => ReapplyPreview());
            root.Add(rowVec);

            var rowVecOp = MkRow();
            rowVecOp.Add(_alignToVectorBtn = MkBtn("方向へ向ける", () => Run(NormalEditCommand.Op.AlignToVector),
                "方向欄の向きへ法線を向ける。スポイトで拾った向きに使う。"));
            root.Add(rowVecOp);

            root.Add(MkSliderRow("回転角", RotateDegMin, RotateDegMax, _rotateDeg, v => { _rotateDeg = v; ReapplyPreview(); },
                out _rotateSlider, out _rotateField));
            var rowRot = MkRow();
            rowRot.Add(_rotateAxisAngleBtn = MkBtn("方向を軸に回す", () => Run(NormalEditCommand.Op.RotateAxisAngle),
                "方向欄を軸に、回転角だけ法線を回す。"));
            root.Add(rowRot);
        }

        private void BuildViewportTools(VisualElement root)
        {
            root.Add(SecLabel("ビューポート操作"));

            _viewportToolLabel = new Label();
            _viewportToolLabel.style.fontSize = 10;
            root.Add(_viewportToolLabel);

            var rowTool = MkRow();
            rowTool.Add(_toolSelectBtn = MkBtn("選択", () => SwitchTool(NormalEditToolHandler.SubMode.Select),
                "ビューポートでは選択だけを行う。"));
            rowTool.Add(_toolHandleBtn = MkBtn("ハンドル", () => SwitchTool(NormalEditToolHandler.SubMode.Handle),
                "中心/向き先の点をダイヤ型のハンドルで動かす。プレビュー中なら動かすたびに計算し直す。"));
            rowTool.Add(_toolRotateBtn = MkBtn("直接回転", () => SwitchTool(NormalEditToolHandler.SubMode.Rotate),
                "ドラッグした方向へ対象法線を倒す。1 回のドラッグで 1 回確定する。"));
            root.Add(rowTool);
            var rowTool2 = MkRow();
            rowTool2.Add(_toolBrushBtn = MkBtn("ブラシ", () => SwitchTool(NormalEditToolHandler.SubMode.Brush),
                "法線ブラシで塗る。1 回のなぞりで 1 回確定する。方向へ寄せるときは方向欄の向きを使う。"));
            rowTool2.Add(_toolSpoitBtn = MkBtn("スポイト", () => SwitchTool(NormalEditToolHandler.SubMode.Spoit),
                "クリックした頂点の法線を方向欄へ拾う（ワールド指定に切り替わる）。"));
            root.Add(rowTool2);

            _brushModeDropdown = new DropdownField("ブラシ", BrushModeNames, 0);
            _brushModeDropdown.style.marginBottom = 2;
            root.Add(_brushModeDropdown);
            root.Add(MkSliderRow("半径", BrushRadiusMin, BrushRadiusMax, _brushRadius, v => _brushRadius = v,
                out _brushRadiusSlider, out _brushRadiusField));
            root.Add(MkSliderRow("ブラシ強度", BrushStrengthMin, BrushStrengthMax, _brushStrength, v => _brushStrength = v,
                out _brushStrengthSlider, out _brushStrengthField));
            _brushMirrorToggle = MkToggle("ブラシを左右対称に掛ける（ローカル X）", false, null);
            root.Add(_brushMirrorToggle);
        }

        private void BuildSeams(VisualElement root)
        {
            root.Add(SecLabel("継ぎ目"));

            var rowTh = MkRow();
            rowTh.Add(MkLabel("中央しきい値", 70));
            _mirrorThresholdField = new FloatField { value = DefaultMirrorThreshold };
            _mirrorThresholdField.style.flexGrow = 1;
            _mirrorThresholdField.tooltip = "中央とみなす範囲。|X座標| がこの値以下の頂点が対象。";
            _mirrorThresholdField.RegisterValueChangedCallback(_ => ReapplyPreview());
            rowTh.Add(_mirrorThresholdField);
            root.Add(rowTh);

            var rowMirror = MkRow();
            rowMirror.Add(_mirrorFlattenSeamXBtn = MkBtn("中央の陰影の継ぎ目を補正",
                () => Run(NormalEditCommand.Op.MirrorFlattenSeamX),
                "対象のうち |X座標| がしきい値以下の頂点だけ、法線の X 成分をゼロにして正規化する。"
                + "左右の合わせ目に出る陰影の段差を消す。左右全体の法線コピーではない。"));
            root.Add(rowMirror);

            var rowSeam = MkRow();
            rowSeam.Add(MkLabel("重なり距離", 70));
            _seamDistanceField = new FloatField { value = DefaultSeamDistance };
            _seamDistanceField.style.flexGrow = 1;
            _seamDistanceField.tooltip = "オブジェクト間でこの距離（ワールド）以内に重なる頂点を継ぎ目とみなす。";
            _seamDistanceField.RegisterValueChangedCallback(_ => ReapplyPreview());
            rowSeam.Add(_seamDistanceField);
            root.Add(rowSeam);

            var rowAcross = MkRow();
            rowAcross.Add(_averageAcrossObjectsBtn = MkBtn("オブジェクト間の継ぎ目を揃える",
                () => Run(NormalEditCommand.Op.AverageAcrossObjects),
                "選択中の描画オブジェクト（2 つ以上）で、重なる頂点の法線をワールドで平均する。"
                + "分けた頭と首の継ぎ目などに使う。同じオブジェクト内の重なりは変えない。"));
            root.Add(rowAcross);
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            var mc = ActiveMeshContext;
            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text         = "";
                _currentSelLabel.text       = "";
                _preserveLabel.text         = "";
                UpdatePreviewBar();
                UpdateToolLabel();
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;

            var targets = CollectTargetViews();
            _meshNameLabel.text = targets.Count == 1
                ? (targets[0].Name ?? "(no name)")
                : $"適用先: {targets.Count} オブジェクト";

            var lines = new List<string>();
            int on = 0, off = 0;
            foreach (var t in targets)
            {
                var parts = new List<string>();
                if (t.SelectedVertexCount > 0) parts.Add($"V:{t.SelectedVertexCount}");
                if (t.SelectedEdgeCount   > 0) parts.Add($"E:{t.SelectedEdgeCount}");
                if (t.SelectedFaceCount   > 0) parts.Add($"F:{t.SelectedFaceCount}");

                // NormalEditOps.CollectTargetCorners と同じ優先順位
                string scope = (t.SelectedFaceCount > 0) ? "面コーナー"
                             : (t.SelectedVertexCount > 0 || t.SelectedEdgeCount > 0)
                                 ? "選択頂点・辺の端点の全スロット"
                             : "メッシュ全体";

                string sel  = parts.Count > 0 ? string.Join(" ", parts) : "(選択なし)";
                string name = targets.Count > 1 ? $"{t.Name}: " : "";
                lines.Add($"{name}{sel}   対象: {scope}");

                if (t.PreserveNormals) on++; else off++;
            }
            lines.Add("角度で再計算: 常にメッシュ全体（除外セットは保持）");
            _currentSelLabel.text = string.Join("\n", lines);

            _preserveLabel.text = $"法線維持（自動再計算しない）: ON {on} / OFF {off}";

            _showNormalsToggle?.SetValueWithoutNotify(GetShowNormals?.Invoke() ?? false);
            _scopeColoringToggle?.SetValueWithoutNotify(NormalEditOps.ScopeColoringEnabled);

            UpdatePreviewBar();
            UpdateToolLabel();
        }

        /// <summary>
        /// 適用先のオブジェクト。PlayerCommandDispatcher.CollectSelectedMeshContexts と同じ規則
        /// （選択中の描画オブジェクト全部、無ければ編集対象メッシュ単体）。
        /// </summary>
        private List<Poly_Ling.View.IMeshView> CollectTargetViews()
        {
            var list  = new List<Poly_Ling.View.IMeshView>();
            var model = GetView?.Invoke()?.CurrentModel;
            if (model == null) return list;

            var sel = model.SelectedDrawableIndices;
            if (sel != null)
            {
                foreach (int idx in sel)
                {
                    var m = model.GetMesh(idx);
                    if (m != null) list.Add(m);
                }
            }
            if (list.Count == 0 && model.ActiveMesh != null) list.Add(model.ActiveMesh);
            return list;
        }

        /// <summary>プレビューで書き換える実体（CollectTargetViews と同じ規則）。</summary>
        private List<MeshContext> CollectTargetContexts(out List<int> indices)
        {
            var list = new List<MeshContext>();
            indices  = new List<int>();
            var model = GetModel?.Invoke();
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) { list.Add(mc); indices.Add(idx); }
            }
            if (list.Count == 0 && model.ActiveMeshContext?.MeshObject != null)
            {
                list.Add(model.ActiveMeshContext);
                indices.Add(model.MeshContextList.IndexOf(model.ActiveMeshContext));
            }
            return list;
        }

        // ================================================================
        // 実行・プレビュー
        // ================================================================

        /// <summary>
        /// 操作ボタン。プレビューのチェックが付いていればプレビューを始める（切り替える）。
        /// 付いていなければ直ちにコマンドを送る。
        /// </summary>
        private void Run(NormalEditCommand.Op op, bool negative = false)
        {
            if (ActiveMeshContext == null) { SetStatus("メッシュが選択されていません"); return; }

            if (_previewToggle != null && _previewToggle.value)
            {
                if (_preview.IsActive && !_previewFromPanel) CancelPreview();
                if (!_preview.IsActive && !StartPreview(true)) return;
                _previewOp       = op;
                _previewNegative = negative;
                ReapplyPreview();
                UpdatePreviewBar();
                return;
            }

            SendEdit(BuildCommand(op, negative));
        }

        private NormalEditCommand BuildCommand(
            NormalEditCommand.Op op, bool negative,
            Vector3? directionOverride = null, float? rotateOverride = null, bool? worldOverride = null)
        {
            return new NormalEditCommand(
                ModelIndex,
                op,
                angleDeg:           _angleDeg,
                strength:           _strength,
                axis:               Axis,
                negative:           negative,
                target:             Target,
                useSelectionCenter: _useCenterToggle?.value ?? true,
                alignVectors:       _alignVectorsToggle?.value ?? false,
                weightMode:         WeightMode,
                mirrorThreshold:    MirrorThreshold,
                blend:              _blend,
                protectUnselected:  _protectUnselectedToggle?.value ?? true,
                direction:          directionOverride ?? Direction,
                rotateDeg:          rotateOverride ?? _rotateDeg,
                worldSpace:         worldOverride ?? WorldSpace,
                seamDistance:       SeamDistance);
        }

        private void SendEdit(NormalEditCommand cmd)
        {
            SendCommand?.Invoke(cmd);

            // Dispatch は同期なので、直後に結果（前後比較の実測）を読める。
            Refresh();
            var r = GetLastResult?.Invoke();
            SetStatus(r != null ? r.Summary : $"実行: {cmd.Operation}（結果を取得できません）");
            RequestNormalLinesRebuild?.Invoke();
        }

        private bool StartPreview(bool fromPanel)
        {
            var model = GetModel?.Invoke();
            var targets = CollectTargetContexts(out var indices);
            if (model == null || targets.Count == 0) { SetStatus("対象がありません"); return false; }

            if (!_previewLocked && TryLockForPreview != null)
            {
                if (!TryLockForPreview(indices)) { SetStatus("他の操作者が作業中のため開始できません"); return false; }
                _previewLocked = true;
            }
            if (!_preview.Start(targets))
            {
                Unlock();
                SetStatus("プレビューを開始できません");
                return false;
            }
            _previewFromPanel = fromPanel;
            return true;
        }

        /// <summary>パラメータが変わったとき。パネルのプレビュー中なら開始時の状態から計算し直す。</summary>
        private void ReapplyPreview()
        {
            if (!_preview.IsActive || !_previewFromPanel) return;
            var model = GetModel?.Invoke();
            _preview.Apply(model, BuildCommand(_previewOp, _previewNegative), out bool slotChanged);
            SyncPreview(slotChanged);
            UpdatePreviewBar();
        }

        private void CommitPanelPreview()
        {
            if (!_preview.IsActive) return;
            var cmd = BuildCommand(_previewOp, _previewNegative);
            EndPreview();
            SendEdit(cmd);
        }

        /// <summary>プレビューを取り消して開始時へ戻す。Viewer がモードを抜けるときにも呼ぶ。</summary>
        public void CancelPreview()
        {
            if (!_preview.IsActive) { Unlock(); UpdatePreviewBar(); return; }
            EndPreview();
            SetStatus("プレビューを取り消しました");
        }

        private void EndPreview()
        {
            bool slotChanged = _preview.SlotCountDiffersFromStart();
            var targets = _preview.Targets;
            _preview.End(GetModel?.Invoke());
            SyncTargets(targets, slotChanged);
            _previewFromPanel = false;
            Unlock();
            UpdatePreviewBar();
        }

        private void Unlock()
        {
            if (_previewLocked) { _previewLocked = false; UnlockAfterPreview?.Invoke(); }
        }

        private void SyncPreview(bool slotChanged) => SyncTargets(_preview.Targets, slotChanged);

        private void SyncTargets(IReadOnlyList<MeshContext> targets, bool slotChanged)
        {
            var model = GetModel?.Invoke();
            bool hasMirror = false;
            if (model != null)
            {
                foreach (var m in model.MeshContextList)
                {
                    if (m == null || !m.MirrorGeometryDerived) continue;
                    int src = m.BakedMirrorSourceIndex;
                    if (src < 0 || src >= model.MeshContextList.Count) continue;
                    foreach (var t in targets) if (ReferenceEquals(model.MeshContextList[src], t)) hasMirror = true;
                }
            }

            if (slotChanged || hasMirror) OnNotifyTopologyChanged?.Invoke();
            else foreach (var t in targets) OnSyncMeshNormals?.Invoke(t);
            RequestNormalLinesRebuild?.Invoke();
        }

        private void UpdatePreviewBar()
        {
            if (_previewBar == null) return;
            bool show = _preview.IsActive && _previewFromPanel;
            _previewBar.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
                _previewStateLabel.text = $"プレビュー中: {OpLabel(_previewOp)}（値を変えると開始時から計算し直す）";
        }

        // ================================================================
        // ビューポート操作の口（NormalEditToolHandler が呼ぶ）
        // ================================================================

        /// <summary>ギズモの表示更新を Viewer に頼む口（座標欄を手で変えたとき）。</summary>
        public Action RefreshGizmoFromPanel;

        /// <summary>ハンドルの位置（ワールド）。</summary>
        public Vector3? GetHandleWorld()
        {
            if (WorldSpace) return Target;
            var mc = GetModel?.Invoke()?.ActiveMeshContext;
            if (mc == null) return null;
            return NormalEditOps.ObjectToWorld(mc).MultiplyPoint3x4(Target);
        }

        /// <summary>ハンドルを動かした（ワールド）。座標欄へ書き、プレビュー中なら計算し直す。</summary>
        public void OnHandleMoved(Vector3 world)
        {
            Vector3 v = world;
            if (!WorldSpace)
            {
                var mc = GetModel?.Invoke()?.ActiveMeshContext;
                if (mc != null) v = NormalEditOps.ObjectToWorld(mc).inverse.MultiplyPoint3x4(world);
            }
            _targetX.SetValueWithoutNotify(v.x);
            _targetY.SetValueWithoutNotify(v.y);
            _targetZ.SetValueWithoutNotify(v.z);
            ReapplyPreview();
        }

        /// <summary>スポイトで拾った方向（ワールド）。方向欄へ入れ、ワールド指定に切り替える。</summary>
        public void OnDirectionPicked(Vector3 world)
        {
            if (!WorldSpace)
            {
                // 座標欄も同じ空間で読むので、中心/向き先をワールドへ直してから切り替える
                var h = GetHandleWorld();
                _worldSpaceToggle.SetValueWithoutNotify(true);
                if (h.HasValue)
                {
                    _targetX.SetValueWithoutNotify(h.Value.x);
                    _targetY.SetValueWithoutNotify(h.Value.y);
                    _targetZ.SetValueWithoutNotify(h.Value.z);
                }
            }
            _dirX.SetValueWithoutNotify(world.x);
            _dirY.SetValueWithoutNotify(world.y);
            _dirZ.SetValueWithoutNotify(world.z);
            SetStatus($"方向を拾いました（ワールド）: ({world.x:F3}, {world.y:F3}, {world.z:F3})");
            ReapplyPreview();
        }

        /// <summary>回転・ブラシのドラッグ開始。パネルのプレビュー中ならそれを取り消してから始める。</summary>
        public bool BeginLivePreview()
        {
            if (_preview.IsActive) CancelPreview();
            return StartPreview(false);
        }

        public void PreviewRotate(Vector3 axisWorld, float deg)
        {
            if (!_preview.IsActive) return;
            var cmd = BuildCommand(NormalEditCommand.Op.RotateAxisAngle, false, axisWorld, deg, true);
            _preview.Apply(GetModel?.Invoke(), cmd, out bool slotChanged);
            SyncPreview(slotChanged);
        }

        public void CommitRotate(Vector3 axisWorld, float deg)
        {
            var cmd = BuildCommand(NormalEditCommand.Op.RotateAxisAngle, false, axisWorld, deg, true);
            EndPreview();
            SendEdit(cmd);
        }

        public void PreviewBrush(List<Vector3> centersWorld)
        {
            if (!_preview.IsActive) return;
            _preview.ApplyBrush(GetModel?.Invoke(), centersWorld, _brushRadius, _brushStrength,
                BrushMode, BrushDirectionWorld(), _brushMirrorToggle?.value ?? false);
            SyncPreview(false);
        }

        public void CommitBrush(List<Vector3> centersWorld)
        {
            CollectTargetContexts(out var indices);
            Vector3 dir = BrushDirectionWorld();
            EndPreview();
            if (indices.Count == 0) return;

            SendCommand?.Invoke(new NormalBrushStrokeCommand(
                ModelIndex, indices.ToArray(), centersWorld.ToArray(),
                BrushMode, _brushRadius, _brushStrength, dir, _brushMirrorToggle?.value ?? false));
            Refresh();
            SetStatus($"ブラシ: {BrushModeNames[(int)BrushMode]} / 塗り {centersWorld.Count} 回");
            RequestNormalLinesRebuild?.Invoke();
        }

        /// <summary>回転・ブラシのプレビューを捨てる。</summary>
        public void CancelLivePreview()
        {
            if (_preview.IsActive && !_previewFromPanel) EndPreview();
        }

        private Vector3 BrushDirectionWorld()
        {
            Vector3 d = Direction;
            if (!WorldSpace)
            {
                var mc = GetModel?.Invoke()?.ActiveMeshContext;
                if (mc != null)
                    d = NormalEditOps.ObjectToWorld(mc).inverse.transpose.MultiplyVector(d);
            }
            return d.sqrMagnitude < 1e-12f ? Vector3.up : d.normalized;
        }

        private void SwitchTool(NormalEditToolHandler.SubMode mode)
        {
            SetViewportTool?.Invoke(mode);
            UpdateToolLabel();
        }

        private void UpdateToolLabel()
        {
            if (_viewportToolLabel == null) return;
            var m = GetViewportTool?.Invoke() ?? NormalEditToolHandler.SubMode.Select;
            string name = m switch
            {
                NormalEditToolHandler.SubMode.Handle => "ハンドル（中心/向き先を動かす）",
                NormalEditToolHandler.SubMode.Rotate => "直接回転",
                NormalEditToolHandler.SubMode.Brush  => "ブラシ",
                NormalEditToolHandler.SubMode.Spoit  => "スポイト",
                _                                     => "選択",
            };
            _viewportToolLabel.text = $"ビューポート: {name}";
        }

        private void SendPreserve(bool value)
        {
            CollectTargetContexts(out var indices);
            if (indices.Count == 0) { SetStatus("対象がありません"); return; }
            SendCommand?.Invoke(new SetPreserveNormalsCommand(ModelIndex, indices.ToArray(), value));
            Refresh();
            SetStatus(value ? "法線維持を ON にしました" : "法線維持を OFF にしました（頂点を動かすと自動で再計算します）");
        }

        private static string OpLabel(NormalEditCommand.Op op) => op switch
        {
            NormalEditCommand.Op.RecalcByAngle        => "角度で再計算",
            NormalEditCommand.Op.SetFromFaces         => "既存スロットの方向だけ面法線に",
            NormalEditCommand.Op.AverageFromFaces     => "面形状から滑らかに",
            NormalEditCommand.Op.Unify                => "頂点ごとに方向を揃える",
            NormalEditCommand.Op.Break                => "面ごとにフラット化",
            NormalEditCommand.Op.SplitKeepDirection   => "方向を保持して分離",
            NormalEditCommand.Op.AverageAll           => "全体を同一方向へ",
            NormalEditCommand.Op.Smooth               => "平滑化",
            NormalEditCommand.Op.Sphereize            => "球状化",
            NormalEditCommand.Op.PointToTarget        => "ターゲット指向",
            NormalEditCommand.Op.AlignToAxis          => "軸へ整列",
            NormalEditCommand.Op.AlignToVector        => "方向へ向ける",
            NormalEditCommand.Op.RotateAxisAngle      => "方向を軸に回す",
            NormalEditCommand.Op.FlattenOnAxis        => "軸成分を0に",
            NormalEditCommand.Op.MirrorFlattenSeamX   => "中央の陰影の継ぎ目を補正",
            NormalEditCommand.Op.AverageAcrossObjects => "オブジェクト間の継ぎ目を揃える",
            NormalEditCommand.Op.Flip                 => "反転",
            _                                          => op.ToString(),
        };

        // ================================================================
        // UI ヘルパー
        // ================================================================

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static VisualElement MkRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            return row;
        }

        private static Button MkBtn(string text, Action onClick, string tooltip)
        {
            var b = new Button(onClick) { text = text, tooltip = tooltip };
            b.style.height      = 22;
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            return b;
        }

        private static FloatField MkFloat(float v)
        {
            var f = new FloatField { value = v };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            return f;
        }

        private static Label MkLabel(string t, float width)
        {
            var l = new Label(t);
            l.style.width          = width;
            l.style.fontSize       = 10;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            return l;
        }

        private static Toggle MkToggle(string label, bool value, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = value };
            t.style.fontSize = 10;
            if (onChange != null) t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MkSliderRow(
            string label, float min, float max, float val, Action<float> onChange,
            out Slider slider, out FloatField field)
        {
            var row = MkRow();

            var lb = new Label(label);
            lb.style.width          = 60;
            lb.style.fontSize       = 10;
            lb.style.unityTextAlign = TextAnchor.MiddleLeft;

            var sl = new Slider(min, max) { value = val };
            sl.style.flexGrow = 1;

            var nf = new FloatField { value = val };
            nf.style.width = 50;

            sl.RegisterValueChangedCallback(e =>
            {
                nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                onChange(e.newValue);
            });
            nf.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp(e.newValue, min, max);
                sl.SetValueWithoutNotify(v);
                onChange(v);
            });

            row.Add(lb); row.Add(sl); row.Add(nf);
            slider = sl;
            field  = nf;
            return row;
        }
    }
}
