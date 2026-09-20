// CommandActor.cs
// コマンドを出した操作者。操作経路統一計画.md の G-1。
//
// 【何のために要るか】
//   本体パネル・リモートクライアント・MCP の 3 経路は同じ PlayerCommandDispatcher.Dispatch に
//   集まるが、「誰の操作か」を持つのはリモート経路だけだった。担当者判定
//   （RemoteOwnership.TryAuthorize）を全経路に掛け、操作者を記録するため、
//   一番外側の Dispatch に必ず操作者を渡す。
//
// 【入れ子の Dispatch】
//   ディスパッチャは入れ子の呼び出しでは渡された操作者を使わず、外側の操作者を引き継ぐ。
//   MCP の UI 自動操作から呼ばれたパネルのコマンドも MCP の操作として扱うため。

namespace Poly_Ling.Data
{
    /// <summary>操作者の種別。</summary>
    public enum CommandActorKind
    {
        /// <summary>本体（ホスト）の画面操作。</summary>
        Host = 0,

        /// <summary>リモートクライアント。</summary>
        Remote = 1,

        /// <summary>MCP（AI）。</summary>
        Mcp = 2,
    }

    /// <summary>コマンドを出した操作者。</summary>
    public sealed class CommandActor
    {
        /// <summary>MCP の操作者名。</summary>
        public const string McpUserName = "(mcp)";

        /// <summary>ホストの既定の操作者名（RemoteServerCore.HostUserName の既定値と同じ）。</summary>
        public const string DefaultHostUserName = "(host)";

        /// <summary>担当者判定に使う名前。</summary>
        public string Name { get; }

        /// <summary>種別。</summary>
        public CommandActorKind Kind { get; }

        /// <summary>
        /// 封筒で届いた安定 ID。一番外側のコマンドの書き込み先と対にして照合する。
        /// 無ければ null（照合しない）。
        /// </summary>
        public ulong[] ObjectIds { get; }

        public CommandActor(string name, CommandActorKind kind, ulong[] objectIds = null)
        {
            Name      = name ?? "";
            Kind      = kind;
            ObjectIds = objectIds;
        }

        public static CommandActor Host(string hostUserName)
            => new CommandActor(hostUserName, CommandActorKind.Host);

        public static CommandActor Mcp()
            => new CommandActor(McpUserName, CommandActorKind.Mcp);

        public static CommandActor Remote(string userName, ulong[] objectIds)
            => new CommandActor(userName, CommandActorKind.Remote, objectIds);

        public override string ToString() => $"{Kind}:{Name}";
    }
}
