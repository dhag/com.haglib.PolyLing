// CommandResult.cs
// PanelCommand の実行結果。
// ディスパッチャが void を返していた頃は「対象が見つからない」「生成に失敗した」も
// 呼び出し元へは成功として見えていた。リモート／MCP は何が起きたかを返す必要があるため、
// 成否・理由・対象の識別子をこの型で運ぶ。

namespace Poly_Ling.Data
{
    /// <summary>
    /// 失敗の種類（機械が判定するための値）。応答の "code" に入る。理由の文（Reason）は人が読むためのもの。
    /// 値は外部（Python など）との契約なので、変えないこと。足すときは PolyLing_外部連携の契約.md も直す。
    /// </summary>
    public static class CommandErrorCodes
    {
        /// <summary>action（コマンド名）が無い。</summary>
        public const string UnknownCommand = "unknownCommand";
        /// <summary>引数が足りない・型にできない。</summary>
        public const string InvalidArgs    = "invalidArgs";
        /// <summary>PolyLing のパネルが開いていない・受け口の準備ができていない。</summary>
        public const string NotReady       = "notReady";
        /// <summary>担当者判定で拒否された（ほかの人の担当）。</summary>
        public const string Unauthorized   = "unauthorized";
        /// <summary>送り手の見ている構造が古い（索引と安定 ID の食い違い）。取り直してから送り直す。</summary>
        public const string StaleView      = "staleView";
        /// <summary>オブジェクトグループの出力（モディファイアの出力）へ書こうとした。グループを解除（適用）すれば書ける。</summary>
        public const string ModifierOutput = "modifierOutput";
        /// <summary>実行中に例外が出た。</summary>
        public const string Exception      = "exception";
        /// <summary>上のどれでもない失敗（対象が無い・処理が成り立たないなど）。</summary>
        public const string Failed         = "failed";
    }

    public sealed class CommandResult
    {
        /// <summary>実行できたか。</summary>
        public bool Success { get; }

        /// <summary>失敗理由。成功時は null。</summary>
        public string Reason { get; }

        /// <summary>失敗の種類（CommandErrorCodes）。成功時は null。</summary>
        public string Code { get; }

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

        /// <summary>
        /// 担当者判定のズレ検出（索引と安定 ID の食い違い）で拒否されたか。
        /// リモート経路はこれを見て、送り手へ再取得（refreshRequired）を促す。
        /// </summary>
        public bool StaleView { get; }

        /// <summary>
        /// 実行後のモデルの版（ModelContext.Revision）。書き込みのあるコマンドが成功すると 1 つ進む。
        /// 0 はモデルが無い、または版を採れなかったとき。
        /// ディスパッチャが一番外側の呼び出しの最後に入れる。
        /// </summary>
        public int ModelRevision { get; internal set; }

        /// <summary>この呼び出しで増えたオブジェクトの安定 ID。無ければ null。</summary>
        public ulong[] CreatedObjectIds { get; internal set; }

        /// <summary>この呼び出しで消えたオブジェクトの安定 ID。無ければ null。</summary>
        public ulong[] DeletedObjectIds { get; internal set; }

        private CommandResult(bool success, string reason, string code, int[] masterIndices, ulong[] objectIds,
                              string data, bool staleView = false)
        {
            Success       = success;
            Reason        = reason;
            Code          = code;
            MasterIndices = masterIndices;
            ObjectIds     = objectIds;
            Data          = data;
            StaleView     = staleView;
        }

        public static CommandResult Ok(int[] masterIndices = null, ulong[] objectIds = null,
                                       string data = null)
            => new CommandResult(true, null, null, masterIndices, objectIds, data);

        /// <summary>失敗。code は CommandErrorCodes（省くと Failed）。</summary>
        public static CommandResult Fail(string reason, string code = CommandErrorCodes.Failed)
            => new CommandResult(false, reason ?? "unknown error", code ?? CommandErrorCodes.Failed, null, null, null);

        /// <summary>担当者判定のズレ検出で拒否した結果。</summary>
        public static CommandResult FailStale(string reason)
            => new CommandResult(false, reason ?? "unknown error", CommandErrorCodes.StaleView, null, null, null, staleView: true);

        public override string ToString()
            => Success ? "ok" : $"fail[{Code}]: {Reason}";
    }
}
