// PanelCommand.ScenarioTutorial.cs
// シナリオを流して撮影し、画像を BASE64 で埋め込んだチュートリアルの MD を作る要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【正本】PolyLing_チュートリアル計画.md 3.3・3.4・段 4
//
// 【2 本に分ける】
//   撮影はフレーム終端で行われ、シナリオの実行は MCP の応答待ちを超えることがある。
//   uiCapture と uiCaptureStatus の関係と同じく、受付番号を返して状態を別のコマンドで読む。
//
// 【BASE64 を応答に載せない】
//   AI は BASE64 を書き写せず、読むと文脈を消費する。応答はパス・枚数・バイト数だけにする。
//
// 【実体は Viewer】
//   カメラ・撮影・赤枠は Viewer が持つので、受け口は PolyLingPlayerViewerCore.ScenarioTutorial.cs にある。

namespace Poly_Ling.Data
{
    /// <summary>原稿とシナリオからチュートリアルの MD を作り始める。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, NoUi = true, Description = "原稿（MD）とシナリオからチュートリアルの MD を作り始める。シナリオを先頭から 1 項目ずつ流し、原稿の ![説明](shot:e1?when=before&target=Window&fit=名前&cursor=route&maxLongEdge=1568&format=png) の所で撮影して、画像を BASE64 で埋め込む。普通の ![説明](パス) も埋め込む。[[route:e4]]（折り畳みとボタン）、[[args:e4]]（入力欄と値。初期値のままの欄を除く）、[[run:e4]]（実行のボタン）は画面の文字へ置き換える。呼ばれたシナリオの項目は シナリオ名:e4。宣言が足りない差し込みは実行前に失敗にする。http(s)、既に data: のリンク、コードブロックとインラインコードの中は変えない。before の画像にはシナリオの値を入力欄へ入れて写し、作成の終わりに戻す。when は before（実行前に経路へ赤枠）/ after（実行後）で既定は after。target は MainView / TriView / Window / パネル ID で、既定は before が Window、after が MainView。cursor は route / run / control:ID / position:x,y / vertex:オブジェクト名,頂点番号 / none で、既定は before が route、after が none。実行前に項目 ID と指定を確かめ、解決できなければ何も実行せず失敗にする。流しの間、指示・確認の項目は止まらずに通過し、Python アドオンの項目は返事を待つ。同じ項目を複数回通るときは最初の通過で撮る。fit のカメラは撮影の後で元へ戻す。失敗すると MD は書かず、撮影済みの画像は残す。受付番号を返し、進行は queryScenarioTutorialBuild で読む。Unity が前面に無いとフレームが進まず終わらない。リモート接続からは使えない。")]
    [PLResult("buildId",    PLResultKind.Text, Description = "受付番号")]
    [PLResult("status",     PLResultKind.Text, Description = "pending / failed。実行前の検査で断ったときは failed")]
    [PLResult("failedItem", PLResultKind.Text, Description = "最初に解決できなかった項目 ID か画像のパス。failed のときだけ", Optional = true)]
    [PLResult("error",      PLResultKind.Text, Description = "失敗理由。問題が複数あれば 1 行に 1 つ。failed のときだけ", Optional = true)]
    public sealed class BuildScenarioTutorialCommand : PanelCommand
    {
        [PLParam(Description = "流すシナリオの名前", Required = true)]
        public string ScenarioName { get; }

        [PLParam(Description = "書き出す MD のパス。作業フォルダからの相対で、拡張子は .md。置き場は データとツール/サンドボックス_シナリオ用/ の下。画像は同じフォルダの <名前>_images/ に残る", Required = true)]
        public string OutPath { get; }

        [PLParam(Description = "原稿のファイルのパス。作業フォルダからの相対。markdownText とどちらか一方")]
        public string FilePath { get; }

        [PLParam(Description = "原稿の本文。filePath とどちらか一方。普通の画像の相対パスは、filePath なら原稿のフォルダ、本文なら作業フォルダから引く")]
        public string MarkdownText { get; }

        [PLParam(Description = "シナリオに渡す引数の名前。paramValues と同じ長さにすること")]
        public string[] ParamKeys { get; }

        [PLParam(Description = "paramKeys と同じ並びの値")]
        public string[] ParamValues { get; }

        [PLParam(Description = "shot: で maxLongEdge を省いたときの長辺の上限[px]。0 なら等倍", Min = 0)]
        public int MaxLongEdge { get; }

        [PLParam(Description = "shot: で format を省いたときの形式。png / jpeg。省くと png")]
        public string Format { get; }

        public BuildScenarioTutorialCommand(
            int modelIndex, string scenarioName, string outPath, string filePath = "", string markdownText = "",
            string[] paramKeys = null, string[] paramValues = null, int maxLongEdge = 0, string format = "png")
            : base(modelIndex)
        {
            ScenarioName = scenarioName ?? "";
            OutPath      = outPath ?? "";
            FilePath     = filePath ?? "";
            MarkdownText = markdownText ?? "";
            ParamKeys    = paramKeys ?? System.Array.Empty<string>();
            ParamValues  = paramValues ?? System.Array.Empty<string>();
            MaxLongEdge  = maxLongEdge < 0 ? 0 : maxLongEdge;
            Format       = string.IsNullOrEmpty(format) ? "png" : format;
        }
    }

    /// <summary>シナリオからチュートリアルの原稿の下書きを作る。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, NoUi = true, Description = "シナリオからチュートリアルの原稿の下書き（MD）を返す。項目ごとの理由、画面の操作、経路の有無を書き、呼ばれたシナリオも展開する。画面の操作は差し込み [[route:e4]]・[[args:e4]]・[[run:e4]] と撮影 shot: の指定で書く（画面の文字は buildScenarioTutorial の流しの中で読む）。本文にコマンド名と引数名を出さない。そのまま buildScenarioTutorial の原稿にできる。読者向けの文章に直すのは呼ぶ側。シナリオは流さず、モデルは変えない。")]
    [PLResult("markdown",     PLResultKind.Text,      Description = "下書きの MD")]
    [PLResult("outPath",      PLResultKind.Text,      Description = "書いたファイルのパス。outPath を渡さなければ空")]
    [PLResult("itemCount",    PLResultKind.Integer,   Description = "実行する項目の数（呼ばれたシナリオを含む）")]
    [PLResult("routedCount",  PLResultKind.Integer,   Description = "画面の経路がある項目の数")]
    [PLResult("manualCount",  PLResultKind.Integer,   Description = "人の操作への置き換えの項目の数")]
    [PLResult("noRouteCount", PLResultKind.Integer,   Description = "画面の経路が無い項目の数")]
    [PLResult("gaps",         PLResultKind.TextArray, Description = "宣言が足りない所。1 行に項目 ID と、足りないもの")]
    public sealed class ExportScenarioTutorialDraftCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string ScenarioName { get; }

        [PLParam(Description = "下書きを書くファイルのパス。作業フォルダからの相対で、拡張子は .md。省くと書かない")]
        public string OutPath { get; }

        public ExportScenarioTutorialDraftCommand(int modelIndex, string scenarioName, string outPath = "")
            : base(modelIndex)
        {
            ScenarioName = scenarioName ?? "";
            OutPath      = outPath ?? "";
        }
    }

    /// <summary>チュートリアルの作成の状態を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, NoUi = true, Description = "buildScenarioTutorial で始めた作成の状態を返す。進行状況、出力パス、枚数、バイト数、失敗理由を返す。画像の中身（BASE64）は返さない。画像は imagePaths を read_image で見る。")]
    [PLResult("buildId",      PLResultKind.Text,      Description = "受付番号")]
    [PLResult("status",       PLResultKind.Text,      Description = "pending / completed / failed")]
    [PLResult("stage",        PLResultKind.Text,      Description = "今していること。running（項目を流している）/ reading（差し込みの文字を読んでいる）/ waitingAddon（Python アドオンの返事待ち）/ shooting（撮影中）/ writing（MD を書いている）/ done")]
    [PLResult("currentItem",  PLResultKind.Text,      Description = "今の項目 ID。別のシナリオの中なら シナリオ名:項目 ID")]
    [PLResult("shotsDone",    PLResultKind.Integer,   Description = "撮り終えた枚数")]
    [PLResult("shotsTotal",   PLResultKind.Integer,   Description = "原稿の shot: の数")]
    [PLResult("outPath",      PLResultKind.Text,      Description = "MD の出力パス。completed でなければファイルはこの作成では書いていない")]
    [PLResult("imagesFolder", PLResultKind.Text,      Description = "撮影した画像のフォルダ")]
    [PLResult("imagePaths",   PLResultKind.TextArray, Description = "撮影した画像のパス。撮った順（before と after、呼ばれたシナリオの都合で原稿の順とは限らない。名前の頭の番号が原稿の順）", Optional = true)]
    [PLResult("warnings",     PLResultKind.TextArray, Description = "作成は続けたが知らせること（入力欄へ値を入れられなかった欄など）", Optional = true)]
    [PLResult("imageCount",   PLResultKind.Integer,   Description = "MD に埋め込んだ画像の数（撮影と普通の画像の合計）。completed のときだけ", Optional = true)]
    [PLResult("byteCount",    PLResultKind.Integer,   Description = "MD のバイト数。completed のときだけ", Optional = true)]
    [PLResult("failedItem",   PLResultKind.Text,      Description = "失敗した項目 ID か画像のパス。failed のときだけ", Optional = true)]
    [PLResult("error",        PLResultKind.Text,      Description = "失敗理由。failed のときだけ", Optional = true)]
    public sealed class QueryScenarioTutorialBuildCommand : PanelCommand
    {
        [PLParam(Description = "buildScenarioTutorial が返した buildId", Required = true)]
        public string BuildId { get; }

        public QueryScenarioTutorialBuildCommand(int modelIndex, string buildId)
            : base(modelIndex)
        {
            BuildId = buildId ?? "";
        }
    }
}
