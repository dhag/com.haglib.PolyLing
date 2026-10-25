// PanelCommand.PyAddon.cs
// Python アドオン（登録簿のアドオンを子プロセスとして起動し、リモート経由で呼ぶ）の操作要求。
// 実処理は PyAddonHost（Runtime/Poly_Ling_Player/PyAddon/）。形は PolyLing_外部連携の契約.md §7b。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【設定はコマンドにしない】
//   Python の実行ファイル・リポジトリのフォルダ・利用者のアドオンの登録は、どのプログラムを起動するかを決める設定なので、
//   作業フォルダと同じく画面（ダイアログ）からだけ変える。コマンドは登録済みのアドオンの起動・停止・呼び出しだけ。

namespace Poly_Ling.Data
{
    /// <summary>登録済みの Python アドオンの一覧と状態を照会する。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, NoUi = true, Description = "登録済みの Python アドオン（公式と利用者が足したもの）の一覧と状態（stopped / starting / running / failed）、Python 環境の設定を照会する。")]
    [PLResult("python", PLResultKind.Text,    Description = "使う python の実行ファイル（設定が空なら poly_ling_py の .venv）")]
    [PLResult("root",   PLResultKind.Text,    Description = "poly_ling_py リポジトリのフォルダ")]
    [PLResult("count",  PLResultKind.Integer, Description = "アドオンの数")]
    [PLResult("addons", PLResultKind.Text,    Description = "一覧（JSON 配列の文字列。要素は {id, title, official, script, state, lastError, pid, hasSpec, args:[引数の宣言], outputs:[出力の宣言]}。宣言は describe で受けたもの）")]
    public class QueryPyAddonsCommand : PanelCommand
    {
        public QueryPyAddonsCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>Python アドオンを起動する（起動済みなら何もしない）。register を待たずに戻る。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "登録済みの Python アドオンを、venv の python で子プロセスとして起動する（--serve で常駐し、リモートサーバにつなぐ）。起動済みなら何もしない。つながるのを待たずに戻るので、状態は queryPyAddons で見る。リモートサーバが起動している必要がある。")]
    [PLUiRoute("Python アドオンの起動", "leftPane.fold.PyAddon", "leftPane.pyAddonBtn", "pyAddon.addon", "pyAddon.start")]
    [PLResult("state", PLResultKind.Text, Description = "戻った時点の状態（starting / running）")]
    public class StartPyAddonCommand : PanelCommand
    {
        [PLParam(Description = "アドオンの id（queryPyAddons の id）", Required = true)]
        public string AddonId { get; }

        public StartPyAddonCommand(int modelIndex, string addonId)
            : base(modelIndex)
        {
            AddonId = addonId ?? "";
        }
    }

    /// <summary>Python アドオンを止める。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "Python アドオンを止める。常駐していれば quit を送り、PolyLing が起動した子プロセスなら少し待って終わらなければ終わらせる。")]
    [PLUiRoute("Python アドオンの停止", "leftPane.fold.PyAddon", "leftPane.pyAddonBtn", "pyAddon.addon", "pyAddon.stop")]
    [PLResult("state", PLResultKind.Text, Description = "戻った時点の状態")]
    public class StopPyAddonCommand : PanelCommand
    {
        [PLParam(Description = "アドオンの id（queryPyAddons の id）", Required = true)]
        public string AddonId { get; }

        public StopPyAddonCommand(int modelIndex, string addonId)
            : base(modelIndex)
        {
            AddonId = addonId ?? "";
        }
    }

    /// <summary>
    /// Python アドオンを呼ぶ。起動していなければ起動してから呼ぶ。返事は待たずに呼び出しの番号を返す。
    /// 返事は queryPyAddonCall で取り出す。
    /// </summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "Python アドオンを呼ぶ（action run は処理の実行、describe は引数の宣言の取得）。起動していなければ起動してから呼ぶ。返事は待たずに呼び出しの番号を返す（queryPyAddonCall で取り出す）。run の引数はアドオンが describe で宣言したもの（名前 → 値の JSON オブジェクト）。アドオンはモデルをリモートのコマンドで書き換える。")]
    [PLUiRoute("Python アドオンの実行", "leftPane.fold.PyAddon", "leftPane.pyAddonBtn", "pyAddon.addon", "pyAddon.run")]
    [PLResult("callNo", PLResultKind.Integer, Description = "呼び出しの番号（queryPyAddonCall に渡す）")]
    [PLResult("status", PLResultKind.Text,    Description = "戻った時点の状態（starting = 起動待ち / pending = 返事待ち / failed）")]
    public class InvokePyAddonCommand : PanelCommand
    {
        [PLParam(Description = "アドオンの id（queryPyAddons の id）", Required = true)]
        public string AddonId { get; }

        [PLParam(Description = "run（処理の実行）か describe（引数の宣言の取得）")]
        public string Action { get; }

        [PLParam(Description = "run の引数（JSON オブジェクトの文字列。アドオンが宣言した名前 → 値）。省いた引数は既定値")]
        public string ArgsJson { get; }

        [PLParam(Description = "返事を待つ上限（ミリ秒）。起動を待つ時間は含まない", Min = 1000, Max = 3600000, Step = 1000)]
        public int TimeoutMs { get; }

        [PLParam(Description = "run の引数の名前を 1 つずつ渡す（argValues と同じ長さ）。argsJson に重ねる。シナリオの項目で値を ${引数} や @prev.<キー> にしたいときに使う（argsJson の中は置き換わらない）")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値（文字列。アドオンが宣言した型へ直す）。カンマを含む値は引用符で囲む")]
        public string[] ArgValues { get; }

        public InvokePyAddonCommand(int modelIndex, string addonId, string action = "run", string argsJson = "",
                                    int timeoutMs = 300000, string[] argKeys = null, string[] argValues = null)
            : base(modelIndex)
        {
            AddonId   = addonId ?? "";
            Action    = string.IsNullOrEmpty(action) ? "run" : action;
            ArgsJson  = argsJson ?? "";
            TimeoutMs = timeoutMs;
            ArgKeys   = argKeys   ?? new string[0];
            ArgValues = argValues ?? new string[0];
        }
    }

    /// <summary>invokePyAddon の呼び出しの状態と返事を照会する。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, NoUi = true, Description = "invokePyAddon の呼び出しの状態（starting / pending / done / failed）と、アドオンが返した data を照会する。run の data は {message, log:[…], result:{…}}、describe の data は引数の宣言 {id, title, args:[…]}。")]
    [PLResult("status",  PLResultKind.Text, Description = "starting（起動待ち）/ pending（返事待ち）/ done（成功）/ failed（失敗・時間切れ）")]
    [PLResult("addonId", PLResultKind.Text, Description = "アドオンの id")]
    [PLResult("action",  PLResultKind.Text, Description = "run / describe")]
    [PLResult("data",    PLResultKind.Text, Description = "done のときアドオンが返した data（JSON オブジェクトの文字列）", Optional = true)]
    [PLResult("error",   PLResultKind.Text, Description = "failed のときの理由", Optional = true)]
    [PLResult("code",    PLResultKind.Text, Description = "failed のときの種類", Optional = true)]
    public class QueryPyAddonCallCommand : PanelCommand
    {
        [PLParam(Description = "invokePyAddon が返した呼び出しの番号", Required = true, Min = 1, Step = 1)]
        public int CallNo { get; }

        public QueryPyAddonCallCommand(int modelIndex, int callNo)
            : base(modelIndex)
        {
            CallNo = callNo;
        }
    }
}
