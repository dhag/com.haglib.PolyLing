// Remote/PolyLingLaunchArgs.cs
// ビルド版の起動引数（PolyLing_クライアント種別とビルド構成計画.md 段 3）
//
// 【引数】
//   --polyling-remote=<役割>      server / host / collaborator / separate / none。
//                                 ビルド構成の role と同じ値。シーン・インスペクタの設定より優先する。
//                                   本体ビューア（PolyLingPlayerViewer）… server=サーバ、separate=クライアント、none=リモートなし。
//                                     host / collaborator は本体ビューアでは扱えない（本体ビューアのクライアントは separate 固定）
//                                     ので、エラーを出してリモートなしにする。
//                                   リストクライアント（ListClientBase）… host / collaborator / separate をその種別で登録する。
//                                     server / none はエラーを出して接続しない。
//   --polyling-user=<名前>        リストクライアントが登録するユーザー名（インスペクタより優先）。
//                                 同じ PC で collaborator を起動するときに要る（ホスト名は端末名なので、空欄だと同名で拒否される）。
//   --polyling-control-port=<N>   MCP の受付ポート（PolyLingPlayerControlServer）。無ければ 8772。
//
// 同じ引数が複数あれば先頭を使う。値は前後の空白を除く。
// Runtime/Poly_Ling_Remote/ に配置。#if UNITY_EDITOR 不使用。

using System;

namespace Poly_Ling.Remote
{
    /// <summary>ビルド版の起動引数で指定するリモート役割。ビルド構成の role と同じ値。</summary>
    public enum LaunchRemoteRole
    {
        Server,
        Host,
        Collaborator,
        Separate,
        None,
    }

    public static class PolyLingLaunchArgs
    {
        public const string RemoteArg      = "--polyling-remote=";
        public const string UserArg        = "--polyling-user=";
        public const string ControlPortArg = "--polyling-control-port=";

        /// <summary>引数の値。無ければ false。</summary>
        public static bool TryGet(string prefix, out string value)
        {
            value = null;
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch { return false; }

            foreach (var arg in args)
            {
                if (arg == null || !arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                value = arg.Substring(prefix.Length).Trim();
                return true;
            }
            return false;
        }

        /// <summary>
        /// --polyling-remote= の役割。引数が無ければ present=false。
        /// 値が不明なら present=true・戻り値 false・error に理由。
        /// </summary>
        public static bool TryGetRemoteRole(out bool present, out LaunchRemoteRole role, out string error)
        {
            role = LaunchRemoteRole.None;
            error = null;
            present = TryGet(RemoteArg, out string text);
            if (!present) return false;

            switch (text.ToLowerInvariant())
            {
                case "server":       role = LaunchRemoteRole.Server;       return true;
                case "host":         role = LaunchRemoteRole.Host;         return true;
                case "collaborator": role = LaunchRemoteRole.Collaborator; return true;
                case "separate":     role = LaunchRemoteRole.Separate;     return true;
                case "none":         role = LaunchRemoteRole.None;         return true;
                default:
                    error = $"{RemoteArg}{text} は不明な値です（server / host / collaborator / separate / none）";
                    return false;
            }
        }

        /// <summary>
        /// --polyling-control-port= のポート。引数が無ければ defaultPort。
        /// 値が 1〜65535 の整数でなければ false と理由（ポートは defaultPort）。
        /// </summary>
        public static bool TryGetControlPort(int defaultPort, out int port, out string error)
        {
            port = defaultPort;
            error = null;
            if (!TryGet(ControlPortArg, out string text)) return true;

            if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int p)
                && p >= 1 && p <= 65535)
            {
                port = p;
                return true;
            }
            error = $"{ControlPortArg}{text} は 1〜65535 の整数ではありません";
            return false;
        }
    }
}
