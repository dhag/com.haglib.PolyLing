// Remote/RemoteClientRole.cs
// リモートクライアントの種別（register の role）。PolyLing_クライアント種別とビルド構成計画.md
//
// 【なぜ明示するか】
//   以前はユーザー名がホストと同じかどうかで「本体と連動する窓」か「別の作業者」かを決めていた。
//   この暗黙の規則は実装を取り違えやすいため、種別は role だけで決める。ユーザー名は表示と担当ロックに使う。
//
// 【種別】
//   Host         本人連動。サーバで作業する。選択はホストの選択そのもの（選ぶと本体が動き、本体の選択が届く）。
//                操作者（担当ロック）はホスト名。
//   Collaborator 別作業者連動。サーバで作業する。選択はサーバ上の本人用の枠（本体も他人も動かない）。
//                操作者は登録したユーザー名（必須・ホスト名と別）。
//   Separate     個別作業。手元のコピーで作業し、選択は手元だけで持つ（サーバへ選択を送ると拒否）。
//                サーバへ上げるときは対象を引数で明示するコマンド（putModel など）を使う。
//
// 【規則】
//   role は register で必須。既定値は持たない（送る側が必ず選ぶ）。

namespace Poly_Ling.Remote
{
    public enum RemoteClientRole
    {
        Host,
        Collaborator,
        Separate,
    }

    public static class RemoteClientRoles
    {
        /// <summary>register の params での名前。</summary>
        public const string ParamName = "role";

        public const string HostWire         = "host";
        public const string CollaboratorWire = "collaborator";
        public const string SeparateWire     = "separate";

        public static string ToWire(RemoteClientRole role)
        {
            switch (role)
            {
                case RemoteClientRole.Host:         return HostWire;
                case RemoteClientRole.Collaborator: return CollaboratorWire;
                default:                            return SeparateWire;
            }
        }

        /// <summary>wire の値から種別を得る。空や未知の値は false。</summary>
        public static bool TryParse(string text, out RemoteClientRole role)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case HostWire:         role = RemoteClientRole.Host;         return true;
                case CollaboratorWire: role = RemoteClientRole.Collaborator; return true;
                case SeparateWire:     role = RemoteClientRole.Separate;     return true;
                default:               role = RemoteClientRole.Separate;     return false;
            }
        }

        /// <summary>画面・ログ用の呼び名。</summary>
        public static string DisplayName(RemoteClientRole role)
        {
            switch (role)
            {
                case RemoteClientRole.Host:         return "本人連動";
                case RemoteClientRole.Collaborator: return "別作業者連動";
                default:                            return "個別作業";
            }
        }
    }
}
