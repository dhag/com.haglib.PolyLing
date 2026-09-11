// CommandResult.cs
// PanelCommand の実行結果。
// ディスパッチャが void を返していた頃は「対象が見つからない」「生成に失敗した」も
// 呼び出し元へは成功として見えていた。リモート／MCP は何が起きたかを返す必要があるため、
// 成否・理由・対象の識別子をこの型で運ぶ。

namespace Poly_Ling.Data
{
    public sealed class CommandResult
    {
        /// <summary>実行できたか。</summary>
        public bool Success { get; }

        /// <summary>失敗理由。成功時は null。</summary>
        public string Reason { get; }

        /// <summary>生成／変更された対象の位置インデックス。無ければ null。</summary>
        public int[] MasterIndices { get; }

        /// <summary>同じ対象の安定ID。MasterIndices と同じ並び。無ければ null。</summary>
        public ulong[] ObjectIds { get; }

        /// <summary>
        /// コマンドが返す実データ。JSON オブジェクト 1 個の文字列。無ければ null。
        ///
        /// 【なぜ文字列か】
        ///   返す中身はコマンドごとに違う。共通の型を切ると 200 本を超える
        ///   コマンドの数だけ型が要る。器は 1 つにし、中身の定義は各コマンドが持つ。
        ///   組み立ては CommandDataJson（Poly_Ling_Player/View/Core/）が行う。
        ///
        /// 【量のあるものは入れない】
        ///   番号列・座標列は ModelContext.DataStore へ書き、ここには
        ///   名前と件数だけを載せる（PLDataStore.cs の冒頭注記）。
        ///
        /// 【そのまま応答へ差し込まれる】
        ///   PolyLingEditorControlServer.HandleCall が KeyRaw で
        ///   応答の "result" に入れる（"data" は ping / state が使う文字列の欄で、
        ///   クライアントが文字列として読むため別にしてある）。
        ///   JSON として妥当な文字列だけを入れること。
        /// </summary>
        public string Data { get; }

        private CommandResult(bool success, string reason, int[] masterIndices, ulong[] objectIds,
                              string data)
        {
            Success       = success;
            Reason        = reason;
            MasterIndices = masterIndices;
            ObjectIds     = objectIds;
            Data          = data;
        }

        public static CommandResult Ok(int[] masterIndices = null, ulong[] objectIds = null,
                                       string data = null)
            => new CommandResult(true, null, masterIndices, objectIds, data);

        public static CommandResult Fail(string reason)
            => new CommandResult(false, reason ?? "unknown error", null, null, null);

        public override string ToString()
            => Success ? "ok" : $"fail: {Reason}";
    }
}
