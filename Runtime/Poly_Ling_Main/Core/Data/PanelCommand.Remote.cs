// PanelCommand.Remote.cs
// リモートのクライアント（Python など）への要求と一覧の操作要求。
// 実処理は RemoteServerCore.ClientRequest（PolyLingPlayerServer 経由）。形は PolyLing_外部連携の契約.md。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）

namespace Poly_Ling.Data
{
    /// <summary>
    /// クライアントへ要求を送る（Duplex の request）。返事は待たずに要求番号を返す。
    /// 返事は queryClientRequest で取り出す。
    /// </summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "リモートのクライアントへ要求（action と引数）を送り、要求番号を返す。返事は待たない（queryClientRequest で取り出す）。リモートサーバが起動していて、その action を capabilities に申告したクライアントが接続している必要がある。")]
    [PLResult("requestNo", PLResultKind.Integer, Description = "要求番号（queryClientRequest に渡す）")]
    public class RequestClientCommand : PanelCommand
    {
        [PLParam(TextKey = "RemoteRequestUserName",
                 Description = "宛先のユーザー名（register の userName）。空なら action を受けられる最初の接続")]
        public string UserName { get; }

        [PLParam(TextKey = "RemoteRequestAction",
                 Description = "要求の action（クライアントが register の capabilities で申告したもの）", Required = true)]
        public string Action { get; }

        [PLParam(TextKey = "RemoteRequestParamsJson",
                 Description = "要求の引数（JSON オブジェクトの文字列。空なら {}）")]
        public string ParamsJson { get; }

        [PLParam(TextKey = "RemoteRequestTimeoutMs",
                 Description = "返事を待つ上限（ミリ秒）。過ぎると failed になる", Min = 100, Max = 600000, Step = 100)]
        public int TimeoutMs { get; }

        public RequestClientCommand(int modelIndex, string action, string userName = "", string paramsJson = "",
                                    int timeoutMs = 30000)
            : base(modelIndex)
        {
            Action     = action;
            UserName   = userName ?? "";
            ParamsJson = paramsJson ?? "";
            TimeoutMs  = timeoutMs;
        }
    }

    /// <summary>requestClient で送った要求の状態と返事を照会する。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "requestClient で送った要求の状態（pending / done / failed）と、クライアントが返した data を照会する。")]
    [PLResult("status", PLResultKind.Text,    Description = "pending（返事待ち）/ done（成功）/ failed（失敗・時間切れ）")]
    [PLResult("action", PLResultKind.Text,    Description = "要求の action")]
    [PLResult("data",   PLResultKind.Text,    Description = "done のときクライアントが返した data（JSON オブジェクトの文字列）", Optional = true)]
    [PLResult("error",  PLResultKind.Text,    Description = "failed のときの理由", Optional = true)]
    [PLResult("code",   PLResultKind.Text,    Description = "failed のときの種類", Optional = true)]
    public class QueryClientRequestCommand : PanelCommand
    {
        [PLParam(TextKey = "RemoteRequestNo", Description = "requestClient が返した要求番号", Required = true, Min = 1, Step = 1)]
        public int RequestNo { get; }

        public QueryClientRequestCommand(int modelIndex, int requestNo)
            : base(modelIndex)
        {
            RequestNo = requestNo;
        }
    }

    /// <summary>
    /// 描画オブジェクト 1 つをクライアントへ押し出す（Duplex の request、action は putMesh）。
    /// 返事は待たずに要求番号を返す。返事は queryClientRequest で取り出す。
    /// 要求の params は getRawData の結果と同じ平坦な形（座標は PolyLing のまま）：
    ///   {name, modelIndex, masterIndex, objectId, positions:[…], faceSizes:[…], faceVertices:[…]}
    /// </summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "描画オブジェクト 1 つの頂点座標と面を、putMesh を受けられるクライアント（Blender ブリッジなど）へ押し出し、要求番号を返す。返事は待たない（queryClientRequest で取り出す）。")]
    [PLResult("requestNo", PLResultKind.Integer, Description = "要求番号（queryClientRequest に渡す）")]
    public class SendMeshToClientCommand : PanelCommand
    {
        /// <summary>クライアントへ送る要求の action。受ける側は register の capabilities にこれを入れる。</summary>
        public const string ClientAction = "putMesh";

        [PLParam(Description = "送る描画オブジェクトの masterIndex", Required = true, IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "RemoteRequestUserName",
                 Description = "宛先のユーザー名（register の userName）。空なら putMesh を受けられる最初の接続")]
        public string UserName { get; }

        [PLParam(TextKey = "RemoteRequestTimeoutMs",
                 Description = "返事を待つ上限（ミリ秒）。過ぎると failed になる", Min = 100, Max = 600000, Step = 100)]
        public int TimeoutMs { get; }

        public SendMeshToClientCommand(int modelIndex, int masterIndex, string userName = "", int timeoutMs = 30000)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            UserName    = userName ?? "";
            TimeoutMs   = timeoutMs;
        }
    }

    /// <summary>
    /// 作業フォルダ（PLSandbox。コマンドのファイル入出力が読み書きできる根）の絶対パスを照会する。
    /// 外のプログラム（Python のアドオンなど）がファイルを置く場所を決めるのに使う。
    /// 根の直下には新しいファイル・フォルダを作れないので、置き場所は既存の直下フォルダの中にする。
    /// </summary>
    [PLCommand(Category = "io.project", Writes = PLWriteScope.None, NoUi = true, Description = "作業フォルダ（コマンドのファイル入出力が読み書きできる根）の絶対パスを照会する。根の直下には新しいファイル・フォルダを作れないので、外のプログラムは既存の直下フォルダ（topFolders）の中にファイルを置く。")]
    [PLResult("isSet",      PLResultKind.Flag,      Description = "作業フォルダが設定されているか")]
    [PLResult("path",       PLResultKind.Text,      Description = "作業フォルダの絶対パス。未設定なら空")]
    [PLResult("topFolders", PLResultKind.TextArray, Description = "作業フォルダ直下の既存フォルダの名前（この中にファイルを置ける）")]
    public class QueryWorkFolderCommand : PanelCommand
    {
        public QueryWorkFolderCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>リモートに登録済みのクライアントの一覧を照会する。</summary>
    [PLCommand(Category = "remote", Writes = PLWriteScope.None, Description = "リモートに登録済みのクライアントの一覧（clientType・userName・capabilities）を照会する。")]
    [PLResult("count",   PLResultKind.Integer, Description = "登録済みのクライアントの数")]
    [PLResult("clients", PLResultKind.Text,    Description = "一覧（JSON 配列の文字列。要素は {clientType, userName, capabilities:[…]}）")]
    public class QueryRemoteClientsCommand : PanelCommand
    {
        public QueryRemoteClientsCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }
}
