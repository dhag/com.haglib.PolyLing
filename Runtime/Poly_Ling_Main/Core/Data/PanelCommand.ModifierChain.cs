// PanelCommand.ModifierChain.cs
// 処理列（ModifierStack.IsChain）の共通コマンド 6 本。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.5・段 4
//   種類名（MeshModifierRegistry の Kind）と引数は文字列で受け、種類ごとのコマンドは作らない。
//   引数と処理範囲は MeshModifierRegistry.FromArgs で検査する（未知のキー・型違い・Required の欠けは失敗）。
//   項目は ScenarioItemId で指す（位置で指さない）。
//   受け口は PlayerCommandDispatcher.ModifierChain.cs。1 コマンド＝Undo 1 件。

namespace Poly_Ling.Data
{
    /// <summary>処理列に処理を 1 つ足す。stackName が空なら sourceMasterIndex を元メッシュにした処理列を新しく作る。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Topology,
        Verification = PLCommandVerification.Visual,
        Description = "処理列（非破壊のモディファイアの積み重ね）の末尾に処理を 1 つ足す。stackName を省くと sourceMasterIndex の描画オブジェクトを元メッシュにした処理列を新しく作り、結果の描画オブジェクトを元メッシュの子として作る。種類名は queryMeshModifierKinds の kinds、引数は argKeys・argValues（処理範囲は scope.kind ほか）。未登録の種類・未知のキー・型違い・必須の欠けは何も変えずに失敗する。useAddDefaults=true は追加メニューと同じ既定値（足した直後に形が変わる値。渡した引数が優先）で足す。省くとキーの無い引数は種類の初期値になる（既存のシナリオと同じ。ベベルは辺の指定が要り、曲げは 0 度）。Undo 1 回で戻る。")]
    [PLResult("stackName",   PLResultKind.Text, Description = "処理列のスタック名")]
    [PLResult("itemId",      PLResultKind.Text, Description = "足した項目の ScenarioItemId")]
    [PLResult("resultIndex", PLResultKind.Integer, Description = "結果の描画オブジェクトの索引")]
    [PLResult("resultId",    PLResultKind.Text, Description = "結果の描画オブジェクトの ObjectId")]
    [PLResult("state",       PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（追加）", "rightPaneTop.modifierChain", "modifierChain.addMenu",
        Run = "modifierChain.addMenu",
        Note = "オブジェクトを選んでから開く。メニューの種類のボタンを押すと足され、そのカードが選択中になる。設定は足したあとカードの欄で変える")]
    [PLUiArg("kind", "modifierChain.add.{0}", ValueKind = PLUiValueKind.ModifierKind, Unlabeled = "種類")]
    [PLUiArgList("argKeys", "argValues", "modifierChain.arg.{0}", Fallback = "modifierChain.detailValue")]
    [PLUiNoField("編集対象のオブジェクトとその処理列で決まる", "stackName", "sourceMasterIndex")]
    [PLUiNoField("メニューから足すと常に立つ", "useAddDefaults")]
    public class AddMeshModifierCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "足す先の処理列のスタック名。省くと新しく作る")]
        public string StackName { get; }

        [PLParam(Description = "新しく作るときの元メッシュの索引。stackName を渡したときは使わない",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int SourceMasterIndex { get; }

        [PLParam(Description = "処理の種類名（queryMeshModifierKinds の kinds）", Required = true)]
        public string Kind { get; }

        [PLParam(Description = "引数のキー。処理範囲は scope.kind・scope.vertexIds ほか")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値。カンマを含む値は二重引用符で包む")]
        public string[] ArgValues { get; }

        [PLParam(Description = "true なら追加メニューと同じ既定値で足す（入力の大きさに合わせる）。渡した引数が優先")]
        public bool UseAddDefaults { get; }

        public AddMeshModifierCommand(int modelIndex, string kind, string stackName = null, int sourceMasterIndex = -1,
                                      string[] argKeys = null, string[] argValues = null, bool useAddDefaults = false)
            : base(modelIndex)
        {
            Kind              = kind;
            StackName         = stackName;
            SourceMasterIndex = sourceMasterIndex;
            ArgKeys           = argKeys   ?? System.Array.Empty<string>();
            ArgValues         = argValues ?? System.Array.Empty<string>();
            UseAddDefaults    = useAddDefaults;
        }
    }

    /// <summary>元メッシュの今の選択、またはパーツ選択辞書を、処理列の項目の頂点グループにする。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "元メッシュの今の選択（頂点・辺・面）か、partsSetName のパーツ選択辞書を、処理列の項目の頂点グループにする。範囲は頂点 ID で持つので、元メッシュの頂点に ID が無ければ、未設定の頂点へ ID を振ってから登録する（辞書に ID の控えが無ければ控えも取る）。元メッシュが別のモディファイアの結果なら、流し直しで ID が消えるので振らず、ID のある要素だけを登録する（パイプなど履歴の生成物には振れる）。反転はそのまま残す。Undo 1 回で ID と頂点グループが戻る。")]
    [PLResult("state",    PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLResult("assigned", PLResultKind.Integer, Description = "ID を振った頂点の数")]
    [PLResult("noId",     PLResultKind.Integer, Description = "ID が無いので登録しなかった要素の数（選択から作るとき）")]
    [PLUiRoute("モディファイアパネル（頂点グループ）", "rightPaneTop.modifierChain", "modifierChain.scope.fromSelection",
        Run = "modifierChain.scope.fromSelection",
        Note = "元メッシュで頂点・辺・面を選んでから、カードの「今の選択から作る」を押す。辞書はカードの「頂点グループ」の欄で選ぶ")]
    [PLUiArg("partsSetName", "modifierChain.scope.group")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class SetMeshModifierVertexGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "頂点グループにする元メッシュのパーツ選択辞書の名前。省くと今の選択から作る")]
        public string PartsSetName { get; }

        public SetMeshModifierVertexGroupCommand(int modelIndex, string stackName, string itemId, string partsSetName = null)
            : base(modelIndex)
        {
            StackName    = stackName;
            ItemId       = itemId;
            PartsSetName = partsSetName;
        }
    }

    /// <summary>ミラーの項目を、実体（結果に面を足す）と MQO の属性で持つ形の間で切り替える。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "ミラーの項目を切り替える。asAttribute=true で MQO の属性で持つ形（種類 mirrorDisplay。結果のメッシュは変えず、ミラー側を別オブジェクトで出す。列の最後に 1 つだけ置けるので最後へ動く）に、false で実体（種類 mirrorBake。結果のメッシュに面を足す）にする。軸と平面の位置は引き継ぎ、片方にしか無い設定は切り替えて戻すと元に戻る（初めて切り替えた先は既定値）。Undo 1 回で戻る。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（ミラーの詳細）", "rightPaneTop.modifierChain",
        "modifierChain.detailFold", "modifierChain.mirrorAsAttribute", Run = "modifierChain.mirrorAsAttribute",
        Note = "ミラーのカードの「詳細」を開いて「MQO の属性で持つ」を切り替える")]
    [PLUiArg("asAttribute", "modifierChain.mirrorAsAttribute")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class SetMeshModifierMirrorAttributeCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "true で MQO の属性で持つ形、false で実体", Required = true)]
        public bool AsAttribute { get; }

        public SetMeshModifierMirrorAttributeCommand(int modelIndex, string stackName, string itemId, bool asAttribute)
            : base(modelIndex)
        {
            StackName   = stackName;
            ItemId      = itemId;
            AsAttribute = asAttribute;
        }
    }

    /// <summary>ビューのハンドルで動かすもの。</summary>
    public enum ModifierGizmoKind
    {
        /// <summary>終わる。</summary>
        Off,
        /// <summary>作業軸を動かす。</summary>
        AxisMove,
        /// <summary>作業軸を回す。</summary>
        AxisRotate,
        /// <summary>ラティスの制御点を動かす。</summary>
        LatticePoints,
    }

    /// <summary>処理列の項目の作業軸やラティスの制御点を、ビューのハンドルで動かすモードに入る・抜ける。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.None,
        Description = "処理列のデフォーマ（曲げ・ねじり・波・回転・移動・拡大縮小・ラティス）の作業軸、またはラティスの制御点を、ビューのハンドルで動かすモードに入る。kind は AxisMove（作業軸を動かす）・AxisRotate（作業軸を回す）・LatticePoints（制御点を動かす）・Off（抜けて前のモードへ戻る）。同じ項目で同じ kind を送っても抜ける。ハンドルのドラッグは項目の引数を書き換え、Undo はドラッグ 1 回で 1 件。データはこのコマンドでは変わらない。")]
    [PLUiRoute("モディファイアパネル（作業軸・制御点のボタン）", "rightPaneTop.modifierChain", "modifierChain.gizmo.axisMove",
        Run = "modifierChain.gizmo.axisMove",
        Note = "デフォーマのカードの「作業軸を動かす」「作業軸を回す」、ラティスのカードの「制御点を動かす」を押す。押し直すか Esc で抜ける")]
    [PLUiNoField("選択中のカードと押したボタンで決まる", "stackName", "itemId", "kind")]
    public class ShowModifierGizmoCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名。kind が Off なら省ける")]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId。kind が Off なら省ける")]
        public string ItemId { get; }

        [PLParam(Description = "動かすもの（AxisMove / AxisRotate / LatticePoints / Off）", Required = true)]
        public ModifierGizmoKind Kind { get; }

        public ShowModifierGizmoCommand(int modelIndex, ModifierGizmoKind kind, string stackName = null, string itemId = null)
            : base(modelIndex)
        {
            Kind      = kind;
            StackName = stackName;
            ItemId    = itemId;
        }
    }

    /// <summary>処理列の項目 1 つの引数を書き換える。渡したキーだけを今の引数に重ねる。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "処理列の項目 1 つの引数を書き換える。渡したキーだけを今の引数に重ね、種類の宣言で検査する（未知のキー・型違いは何も変えずに失敗）。結果は流し直される。Undo 1 回で戻る。preview=true は下見（スライダーのドラッグ中）で Undo を積まず、続く preview=false の同じ項目への書き込みと合わせて Undo 1 件になる。下見のまま別の書き込みコマンドが来たら、その時点で Undo 1 件として閉じる。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（設定）", "rightPaneTop.modifierChain",
        "modifierChain.detailValue", "modifierChain.detailWrite",
        Run = "modifierChain.detailWrite",
        Note = "カードの設定欄（数値欄・スライダー・選択ボタン・チェックボックス・頂点グループ）で変える。詳細の文字列欄でも書ける")]
    [PLUiArgList("argKeys", "argValues", "modifierChain.arg.{0}", Fallback = "modifierChain.detailValue")]
    [PLUiNoField("選択中のカードで決まる。下見は欄をドラッグしている間", "stackName", "itemId", "preview")]
    public class SetMeshModifierArgsCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "書く引数のキー", Required = true)]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値。カンマを含む値は二重引用符で包む", Required = true)]
        public string[] ArgValues { get; }

        [PLParam(Description = "true なら下見（Undo を積まない。スライダーのドラッグ中）。続く false の書き込みで Undo 1 件にまとまる")]
        public bool Preview { get; }

        public SetMeshModifierArgsCommand(int modelIndex, string stackName, string itemId, string[] argKeys, string[] argValues,
                                          bool preview = false)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
            ArgKeys   = argKeys   ?? System.Array.Empty<string>();
            ArgValues = argValues ?? System.Array.Empty<string>();
            Preview   = preview;
        }
    }

    /// <summary>処理列の項目 1 つを並べ替える。beforeItemId の前へ動かす（空なら末尾）。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "処理列の項目 1 つを並べ替える。beforeItemId の項目の前へ動かす（省くと末尾）。結果は流し直される。Undo 1 回で戻る。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（↑↓）", "rightPaneTop.modifierChain",
        "modifierChain.up", "modifierChain.down", Run = "modifierChain.up",
        Note = "選択中のカードの ↑ か ↓ を押す。1 回で 1 つ動く")]
    [PLUiNoField("選択中のカードと、↑ か ↓ を押した回数で決まる", "stackName", "itemId", "beforeItemId")]
    public class MoveMeshModifierCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "動かす項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "この項目の前へ動かす（ScenarioItemId）。省くと末尾")]
        public string BeforeItemId { get; }

        public MoveMeshModifierCommand(int modelIndex, string stackName, string itemId, string beforeItemId = null)
            : base(modelIndex)
        {
            StackName    = stackName;
            ItemId       = itemId;
            BeforeItemId = beforeItemId;
        }
    }

    /// <summary>処理列の項目 1 つの有効を切り替える。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "処理列の項目 1 つを一時的に外す・戻す（無効の項目は流さない）。結果は流し直される。Undo 1 回で戻る。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（有効）", "rightPaneTop.modifierChain",
        "modifierChain.enabled", Run = "modifierChain.enabled")]
    [PLUiArg("enabled", "modifierChain.enabled")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class SetMeshModifierEnabledCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "true で有効、false で無効", Required = true)]
        public bool Enabled { get; }

        public SetMeshModifierEnabledCommand(int modelIndex, string stackName, string itemId, bool enabled)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
            Enabled   = enabled;
        }
    }

    /// <summary>処理列の項目 1 つを消す。最後の項目なら処理列ごと外し、結果の描画オブジェクトを消す。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.DeletesObject | PLCommandEffect.Topology,
        Verification = PLCommandVerification.Visual,
        Description = "処理列の項目 1 つを消す。最後の 1 つを消すと処理列ごと外し、結果の描画オブジェクトを消す（元メッシュだけが残る）。Undo 1 回で戻る。")]
    [PLResult("stackRemoved", PLResultKind.Flag, Description = "処理列ごと外したか")]
    [PLResult("state",        PLResultKind.Text, Description = "処理列の評価の結果と理由（外したときは空）")]
    [PLUiRoute("モディファイアパネル（削除）", "rightPaneTop.modifierChain",
        "modifierChain.remove", Run = "modifierChain.remove")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class RemoveMeshModifierCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "消す項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        public RemoveMeshModifierCommand(int modelIndex, string stackName, string itemId)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
        }
    }

    /// <summary>モディファイアをすべて適用する：結果の形が元の名前のオブジェクトになり、オブジェクトは 1 つになる。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.DeletesObject | PLCommandEffect.Topology,
        Verification = PLCommandVerification.Visual,
        Description = "モディファイアをすべて適用する（処理列とサブディビジョンのスタックに使える）。結果の形が元の名前のオブジェクトになり、結果の行は消え、オブジェクトは 1 つになる。元を出力に持つ生成の項目（履歴）も外す。頂点数が変わるのにモーフを持つオブジェクトは、何も変えずに失敗する。Undo 1 回で元とモディファイアが戻る。")]
    [PLResult("resultIndex", PLResultKind.Integer, Description = "適用後のオブジェクトの索引")]
    [PLUiRoute("モディファイアパネル（すべて適用）", "rightPaneTop.modifierChain", "modifierChain.apply",
        Run = "modifierChain.apply")]
    [PLUiNoField("編集対象のオブジェクトで決まる", "stackName")]
    public class ApplyMeshModifierChainCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列かサブディビジョンのスタック名", Required = true)]
        public string StackName { get; }

        public ApplyMeshModifierChainCommand(int modelIndex, string stackName)
            : base(modelIndex)
        {
            StackName = stackName;
        }
    }

    /// <summary>モディファイアを 1 個（または先頭からその項目まで）元メッシュへ適用する。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "モディファイアを元メッシュへ適用し、その項目を消す。残りのモディファイアは残る（Blender の Apply）。先頭以外の 1 個を適用すると、前の項目を飛ばして元に掛け、warning に注意を返す。upTo=true は先頭からその項目まで順に適用する（3ds Max の Collapse To）。全部の項目に当たるときは「すべて適用」と同じ。無効な項目は適用しない。元を他の項目も読んでいる形（はしごからベルトとボーンを並列に出すなど）は、並列の入力を変えないよう、何も変えずに失敗する。Undo 1 回で戻る。")]
    [PLResult("warning", PLResultKind.Text, Description = "順序についての注意。無ければ空")]
    [PLResult("state",   PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（適用）", "rightPaneTop.modifierChain", "modifierChain.menu", "modifierChain.applyOne",
        Run = "modifierChain.applyOne", Note = "カードの ∨ を押して「適用」。カーソルがパネルの上にあれば Ctrl+A でも適用する")]
    [PLUiNoField("選択中のカードで決まる。upTo はメニューの「ここまで適用」", "stackName", "itemId", "upTo")]
    public class ApplyMeshModifierCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "true なら先頭からこの項目まで適用する")]
        public bool UpTo { get; }

        public ApplyMeshModifierCommand(int modelIndex, string stackName, string itemId, bool upTo = false)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
            UpTo      = upTo;
        }
    }

    /// <summary>モディファイアを直下に複製する。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "モディファイアを同じ設定で直下に複製する（Blender の Duplicate）。MQO の属性で持つミラーは 1 つだけなので複製できない。Undo 1 回で戻る。")]
    [PLResult("itemId", PLResultKind.Text, Description = "複製した項目の ScenarioItemId")]
    [PLResult("state",  PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（複製）", "rightPaneTop.modifierChain", "modifierChain.menu", "modifierChain.duplicate",
        Run = "modifierChain.duplicate", Note = "カードの ∨ を押して「複製」。カーソルがパネルの上にあれば Shift+D でも複製する")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class DuplicateMeshModifierCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        public DuplicateMeshModifierCommand(int modelIndex, string stackName, string itemId)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
        }
    }

    /// <summary>モディファイアを他のオブジェクトへコピーする。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Topology,
        Verification = PLCommandVerification.Visual,
        Description = "モディファイアを他のオブジェクトの末尾へコピーする（Blender の Copy to Selected）。targetMasterIndices を省くと選択中の他のオブジェクトへ。選択から作った頂点グループは頂点 ID がオブジェクトごとに違うので全体に戻す。辞書の名前の頂点グループはそのまま。コピーできないオブジェクトは飛ばし、skipped に理由を返す。Undo 1 回で戻る。")]
    [PLResult("copied",  PLResultKind.Integer, Description = "コピーしたオブジェクトの数")]
    [PLResult("skipped", PLResultKind.Text, Description = "飛ばしたオブジェクトと理由。無ければ空")]
    [PLUiRoute("モディファイアパネル（他へコピー）", "rightPaneTop.modifierChain", "modifierChain.menu", "modifierChain.copyToSelected",
        Run = "modifierChain.copyToSelected", Note = "コピー先のオブジェクトも選んでから、カードの ∨ を押して「選択中の他のオブジェクトへコピー」")]
    [PLUiNoField("選択中のカードと、選択中の他のオブジェクトで決まる", "stackName", "itemId", "targetMasterIndices")]
    public class CopyMeshModifierToObjectsCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "項目の ScenarioItemId", Required = true)]
        public string ItemId { get; }

        [PLParam(Description = "コピー先の描画オブジェクトの索引。省くと選択中の他のオブジェクト",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int[] TargetMasterIndices { get; }

        public CopyMeshModifierToObjectsCommand(int modelIndex, string stackName, string itemId, int[] targetMasterIndices = null)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
            TargetMasterIndices = targetMasterIndices ?? System.Array.Empty<int>();
        }
    }

    /// <summary>「ここまで表示」。保存しない表示設定で、Undo に積まない。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "結果の表示をその項目までの形で止める（3ds Max の Show End Result を切った状態）。itemId を省くと最後までの形に戻す。保存しない表示設定で Undo に積まない。適用と書き出しの前に解除される。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("モディファイアパネル（ここまで表示）", "rightPaneTop.modifierChain", "modifierChain.menu", "modifierChain.showUpTo",
        Run = "modifierChain.showUpTo", Note = "カードの ∨ を押して「ここまで表示」。もう一度押すと最後までの形に戻る")]
    [PLUiNoField("選択中のカードで決まる", "stackName", "itemId")]
    public class SetModifierDisplayUpToCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "この項目までを表示する。省くと最後まで")]
        public string ItemId { get; }

        public SetModifierDisplayUpToCommand(int modelIndex, string stackName, string itemId = null)
            : base(modelIndex)
        {
            StackName = stackName;
            ItemId    = itemId;
        }
    }

    /// <summary>モディファイアの元の線（辺と頂点）の表示を切り替える。保存しない表示設定で、Undo に積まない。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "モディファイアの元の線（辺と頂点）の表示を切り替える。隠している間は元の頂点を選べない。保存しない表示設定で、Undo に積まない。")]
    [PLUiRoute("モディファイアパネル（元の線）", "rightPaneTop.modifierChain", "modifierChain.showCage",
        Run = "modifierChain.showCage")]
    [PLUiArg("visible", "modifierChain.showCage")]
    [PLUiNoField("編集対象のオブジェクトで決まる", "stackName")]
    public class SetModifierCageVisibleCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列かサブディビジョンのスタック名", Required = true)]
        public string StackName { get; }

        [PLParam(Description = "true で元の線を表示、false で隠す", Required = true)]
        public bool Visible { get; }

        public SetModifierCageVisibleCommand(int modelIndex, string stackName, bool visible)
            : base(modelIndex)
        {
            StackName = stackName;
            Visible   = visible;
        }
    }
}
