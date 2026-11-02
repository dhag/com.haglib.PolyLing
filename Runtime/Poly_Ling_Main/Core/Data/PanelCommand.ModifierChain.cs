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
        Description = "処理列（非破壊のモディファイアの積み重ね）の末尾に処理を 1 つ足す。stackName を省くと sourceMasterIndex の描画オブジェクトを元メッシュにした処理列を新しく作り、結果の描画オブジェクトを元メッシュの子として作る。種類名は queryMeshModifierKinds の kinds、引数は argKeys・argValues（処理範囲は scope.kind ほか）。未登録の種類・未知のキー・型違い・必須の欠けは何も変えずに失敗する。Undo 1 回で戻る。")]
    [PLResult("stackName",   PLResultKind.Text, Description = "処理列のスタック名")]
    [PLResult("itemId",      PLResultKind.Text, Description = "足した項目の ScenarioItemId")]
    [PLResult("resultIndex", PLResultKind.Integer, Description = "結果の描画オブジェクトの索引")]
    [PLResult("resultId",    PLResultKind.Text, Description = "結果の描画オブジェクトの ObjectId")]
    [PLResult("state",       PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("処理列パネル（種類を選んで追加）", "leftPane.fold.Select", "leftPane.modifierChainBtn",
        "modifierChain.kind", "modifierChain.add",
        Note = "編集対象のオブジェクトを選んでから開く。処理列が無ければ追加で新しく作る")]
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

        public AddMeshModifierCommand(int modelIndex, string kind, string stackName = null, int sourceMasterIndex = -1,
                                      string[] argKeys = null, string[] argValues = null)
            : base(modelIndex)
        {
            Kind              = kind;
            StackName         = stackName;
            SourceMasterIndex = sourceMasterIndex;
            ArgKeys           = argKeys   ?? System.Array.Empty<string>();
            ArgValues         = argValues ?? System.Array.Empty<string>();
        }
    }

    /// <summary>処理列の項目 1 つの引数を書き換える。渡したキーだけを今の引数に重ねる。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "処理列の項目 1 つの引数を書き換える。渡したキーだけを今の引数に重ね、種類の宣言で検査する（未知のキー・型違いは何も変えずに失敗）。結果は流し直される。Undo 1 回で戻る。preview=true は下見（スライダーのドラッグ中）で Undo を積まず、続く preview=false の同じ項目への書き込みと合わせて Undo 1 件になる。下見のまま別の書き込みコマンドが来たら、その時点で Undo 1 件として閉じる。")]
    [PLResult("state", PLResultKind.Text, Description = "処理列の評価の結果と理由")]
    [PLUiRoute("処理列パネル（引数欄）", "leftPane.fold.Select", "leftPane.modifierChainBtn",
        "modifierChain.items", "modifierChain.detailValue", "modifierChain.detailWrite",
        Note = "項目を選び、引数欄（数値欄・スライダー・選択ボタン・チェックボックス・処理範囲）で変える。詳細の文字列欄でも書ける")]
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
    [PLUiRoute("処理列パネル（↑↓）", "leftPane.fold.Select", "leftPane.modifierChainBtn",
        "modifierChain.items", "modifierChain.up", "modifierChain.down")]
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
    [PLUiRoute("処理列パネル（有効）", "leftPane.fold.Select", "leftPane.modifierChainBtn",
        "modifierChain.items", "modifierChain.enabled")]
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
    [PLUiRoute("処理列パネル（削除）", "leftPane.fold.Select", "leftPane.modifierChainBtn",
        "modifierChain.items", "modifierChain.remove")]
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

    /// <summary>処理列を適用する：処理列を解除し、結果を普通のメッシュとして残す。</summary>
    [PLCommand(Category = "object.modifier", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.ObjectAttribute,
        Verification = PLCommandVerification.Visual,
        Description = "処理列を適用する。処理列を解除し、結果の描画オブジェクトを普通のメッシュとして残す（ロックを外し、元メッシュの面の塗りを戻す）。Undo 1 回で処理列に戻る。")]
    [PLResult("resultIndex", PLResultKind.Integer, Description = "残した結果の描画オブジェクトの索引")]
    [PLUiRoute("処理列パネル（適用）", "leftPane.fold.Select", "leftPane.modifierChainBtn", "modifierChain.apply")]
    public class ApplyMeshModifierChainCommand : PanelCommand
    {
        [PLParam(TextKey = "ModifierStackName", Description = "処理列のスタック名", Required = true)]
        public string StackName { get; }

        public ApplyMeshModifierChainCommand(int modelIndex, string stackName)
            : base(modelIndex)
        {
            StackName = stackName;
        }
    }
}
