// CommandResultJson.cs
// コマンドの実行結果（CommandResult）を応答 JSON の欄にする処理の唯一の置き場。
// MCP の受け口（PolyLingControlProtocol の call。8770 / 8772）と、リモートの受け口
// （RemoteServerCore の command。WebSocket）が同じ欄の形で返すために使う。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// ■ 成功のとき足す欄
//   masterIndices    … int の配列（無ければ欄なし）
//   objectIds        … 数の配列（ulong。無ければ欄なし）
//   modelRevision / createdObjectIds / deletedObjectIds … wantRevision のときだけ
//   result           … コマンドが返した実データ（JSON オブジェクト。無ければ欄なし）
// ■ 失敗のとき足す欄
//   error … 人が読む理由
//   code  … 機械が判定する種類（CommandErrorCodes）

using System.Globalization;
using Poly_Ling.Remote;

namespace Poly_Ling.Data
{
    public static class CommandResultJson
    {
        /// <summary>成功の欄を jb の開いているオブジェクトへ足す。</summary>
        public static void AppendSuccess(JsonBuilder jb, CommandResult r, bool wantRevision)
        {
            AppendIntArray(jb, "masterIndices", r.MasterIndices);
            AppendUlongArray(jb, "objectIds",   r.ObjectIds);
            if (wantRevision)
            {
                jb.KeyValue("modelRevision", r.ModelRevision);
                AppendUlongArray(jb, "createdObjectIds", r.CreatedObjectIds);
                AppendUlongArray(jb, "deletedObjectIds", r.DeletedObjectIds);
            }
            if (!string.IsNullOrEmpty(r.Data)) jb.KeyRaw("result", r.Data);
        }

        /// <summary>失敗の欄（error / code）を jb の開いているオブジェクトへ足す。</summary>
        public static void AppendFailure(JsonBuilder jb, string reason, string code)
        {
            jb.KeyValue("error", reason ?? "unknown error");
            jb.KeyValue("code",  code ?? CommandErrorCodes.Failed);
        }

        /// <summary>成功の欄だけを持つ JSON オブジェクト（リモートの応答の "data" に入れる）。</summary>
        public static string BuildSuccessObject(CommandResult r, bool wantRevision)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            AppendSuccess(jb, r, wantRevision);
            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>コマンドを作れなかったときの code（action が無い／引数が合わない）。</summary>
        public static string CodeForCreateFailure(string action)
            => PanelCommandFactory.ResolveType(action) == null
                ? CommandErrorCodes.UnknownCommand
                : CommandErrorCodes.InvalidArgs;

        /// <summary>"1" / "true" を真とみなす（wantRevision などの封筒の旗）。</summary>
        public static bool IsTrue(string s)
            => s == "1" || string.Equals(s, "true", System.StringComparison.OrdinalIgnoreCase);

        private static void AppendIntArray(JsonBuilder jb, string key, int[] values)
        {
            if (values == null || values.Length == 0) return;
            jb.Key(key);
            jb.BeginArray();
            for (int i = 0; i < values.Length; i++) jb.Value(values[i]);
            jb.EndArray();
        }

        // JsonBuilder に ulong の Value が無いため、数値のまま RawValue で入れる（文字列にすると受け側で型が変わる）。
        private static void AppendUlongArray(JsonBuilder jb, string key, ulong[] values)
        {
            if (values == null || values.Length == 0) return;
            jb.Key(key);
            jb.BeginArray();
            for (int i = 0; i < values.Length; i++)
                jb.RawValue(values[i].ToString(CultureInfo.InvariantCulture));
            jb.EndArray();
        }
    }
}
