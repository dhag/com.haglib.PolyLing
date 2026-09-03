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

        private CommandResult(bool success, string reason, int[] masterIndices, ulong[] objectIds)
        {
            Success       = success;
            Reason        = reason;
            MasterIndices = masterIndices;
            ObjectIds     = objectIds;
        }

        public static CommandResult Ok(int[] masterIndices = null, ulong[] objectIds = null)
            => new CommandResult(true, null, masterIndices, objectIds);

        public static CommandResult Fail(string reason)
            => new CommandResult(false, reason ?? "unknown error", null, null);

        public override string ToString()
            => Success ? "ok" : $"fail: {Reason}";
    }
}
