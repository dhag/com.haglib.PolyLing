// PanelCommand.ComfyUi.cs
// ComfyUI を PolyLing から操作する要求（画像の送信、ワークフローの読込・実行依頼、出力画像の取得）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
// 受け口は PlayerCommandDispatcher.ComfyUi.cs。モデルは参照しない。
// 生成されたメッシュは、ワークフローに入れた PolyLing Send (SF3D) ノードが既存の経路（putModel）で送ってくる。

namespace Poly_Ling.Data
{
    /// <summary>ワークフローを読み、ノードの一覧を返す。</summary>
    [PLCommand(Category = "external.comfyui", Writes = PLWriteScope.None, Description = "ComfyUI のワークフローを読み、実行依頼に渡す形（API 形式）にしたノードの一覧を返す。読込元は ComfyUI の workflows フォルダの名前（workflowName。ComfyUI から取得）か、作業フォルダのファイル（workflowPath）のどちらか一方。画面の「保存」で書いた形式は ComfyUI の画面と同じ規則で変換する（ノード種類の定義を ComfyUI に問い合わせる）。comfyQueuePrompt の overridesJson で書き換える入力の名前と現在の値を調べるのに使う。ComfyUI との通信を待つ間は止まる。")]
    [PLResult("format",    PLResultKind.Text,      Description = "読んだファイルの形式（ui = 画面保存形式、api = API 形式）")]
    [PLResult("nodeCount", PLResultKind.Integer,   Description = "実行されるノードの数（ミュート・バイパス・画面だけのノードを除く）")]
    [PLResult("nodes",     PLResultKind.TextArray, Description = "1 ノード 1 行。「番号 種類「題名」 入力名=値, 入力名←元ノード番号:出力番号, …」")]
    public class ComfyDescribeWorkflowCommand : PanelCommand
    {
        [PLParam(Description = "ComfyUI のユーザーフォルダ（user/default/workflows）にあるワークフローの名前（例 sf3d_example.json。.json は省ける）。workflowPath と同時には渡さない")]
        public string WorkflowName { get; }

        [PLParam(Description = "作業フォルダのワークフロー JSON のパス。workflowName と同時には渡さない")]
        public string WorkflowPath { get; }

        [PLParam(Description = "ComfyUI の接続先（例 http://127.0.0.1:8188）")]
        public string Host { get; }

        [PLParam(Description = "ComfyUI の応答を待つ時間（ミリ秒。1 回の通信ごと）", Min = 100.0)]
        public int TimeoutMs { get; }

        public ComfyDescribeWorkflowCommand(int modelIndex,
            string workflowName = "", string workflowPath = "",
            string host = "http://127.0.0.1:8188", int timeoutMs = 10000)
            : base(modelIndex)
        {
            WorkflowName = workflowName ?? "";
            WorkflowPath = workflowPath ?? "";
            Host         = host;
            TimeoutMs    = timeoutMs;
        }
    }

    /// <summary>画像を ComfyUI の入力フォルダへ送る。</summary>
    [PLCommand(Category = "external.comfyui", Writes = PLWriteScope.None, Description = "作業フォルダの画像を ComfyUI の入力フォルダへ送る。返す name を、画像読込（LoadImage）ノードの image へ comfyQueuePrompt の overridesJson で入れる。ComfyUI との通信を待つ間は止まる。")]
    [PLResult("name", PLResultKind.Text, Description = "ComfyUI 側の画像名（LoadImage の image に入れる値）")]
    public class ComfyUploadImageCommand : PanelCommand
    {
        [PLParam(Description = "送る画像のパス。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "同じ名前の画像があれば上書きする（切ると ComfyUI が別名にする）")]
        public bool Overwrite { get; }

        [PLParam(Description = "ComfyUI の接続先（例 http://127.0.0.1:8188）")]
        public string Host { get; }

        [PLParam(Description = "ComfyUI の応答を待つ時間（ミリ秒）", Min = 100.0)]
        public int TimeoutMs { get; }

        public ComfyUploadImageCommand(int modelIndex, string filePath,
            bool overwrite = true, string host = "http://127.0.0.1:8188", int timeoutMs = 10000)
            : base(modelIndex)
        {
            FilePath  = filePath ?? "";
            Overwrite = overwrite;
            Host      = host;
            TimeoutMs = timeoutMs;
        }
    }

    /// <summary>ワークフローを読み、入力を書き換えて実行を依頼する。</summary>
    [PLCommand(Category = "external.comfyui", Writes = PLWriteScope.None, Description = "ComfyUI のワークフローを読み（comfyDescribeWorkflow と同じ読込元・変換）、overridesJson で入力を書き換えてから実行を依頼し、依頼番号（promptId）を返す。生成の完了は待たない。完了と出力画像は comfyFetchOutputs で取る。ComfyUI との通信を待つ間は止まる。")]
    [PLResult("promptId",  PLResultKind.Text,    Description = "依頼番号（comfyFetchOutputs に渡す）")]
    [PLResult("nodeCount", PLResultKind.Integer, Description = "依頼したノードの数")]
    public class ComfyQueuePromptCommand : PanelCommand
    {
        [PLParam(Description = "ComfyUI のユーザーフォルダ（user/default/workflows）にあるワークフローの名前（例 sf3d_example.json）。workflowPath と同時には渡さない")]
        public string WorkflowName { get; }

        [PLParam(Description = "作業フォルダのワークフロー JSON のパス。workflowName と同時には渡さない")]
        public string WorkflowPath { get; }

        [PLParam(Description = "入力の書き換え。JSON の文字列 {\"ノード番号\": {\"入力名\": 値}}（例 {\"1\":{\"image\":\"axe.png\"},\"6\":{\"text\":\"a cat\"}}）。空なら書き換えない")]
        public string OverridesJson { get; }

        [PLParam(Description = "ComfyUI の接続先（例 http://127.0.0.1:8188）")]
        public string Host { get; }

        [PLParam(Description = "ComfyUI の応答を待つ時間（ミリ秒。1 回の通信ごと）", Min = 100.0)]
        public int TimeoutMs { get; }

        public ComfyQueuePromptCommand(int modelIndex,
            string workflowName = "", string workflowPath = "", string overridesJson = "",
            string host = "http://127.0.0.1:8188", int timeoutMs = 10000)
            : base(modelIndex)
        {
            WorkflowName  = workflowName ?? "";
            WorkflowPath  = workflowPath ?? "";
            OverridesJson = overridesJson ?? "";
            Host          = host;
            TimeoutMs     = timeoutMs;
        }
    }

    /// <summary>依頼の状況を 1 回問い合わせ、終わっていれば出力画像を保存する。</summary>
    [PLCommand(Category = "external.comfyui", Writes = PLWriteScope.None, Description = "comfyQueuePrompt の依頼の状況を 1 回だけ問い合わせる（待たない）。終わっていれば、出力の画像（SaveImage・PreviewImage などの images）を作業フォルダの outputFolder へ ComfyUI 側と同じ名前で保存する。テクスチャにするには続けて setMaterialTexture を使う。ComfyUI との通信を待つ間は止まる。")]
    [PLResult("status",  PLResultKind.Text,      Description = "pending（待ち）／running（実行中）／success（完了）／error（失敗）／unknown（ComfyUI が知らない番号）")]
    [PLResult("files",   PLResultKind.TextArray, Description = "保存した画像のパス（作業フォルダからの相対ではなく実際のパス）")]
    [PLResult("skipped", PLResultKind.TextArray, Description = "保存しなかった出力（拡張子が書き込みを許されていないものなど）と理由")]
    [PLResult("outputs", PLResultKind.TextArray, Description = "出力のあったノードと中身の種類（例「13: text」「9: glbs」）。画像以外も載る")]
    [PLResult("message", PLResultKind.Text,      Description = "失敗のときの ComfyUI のエラー内容")]
    public class ComfyFetchOutputsCommand : PanelCommand
    {
        [PLParam(Description = "comfyQueuePrompt が返した依頼番号", Required = true)]
        public string PromptId { get; }

        [PLParam(Description = "画像の保存先フォルダ。作業フォルダからの相対（無ければ作る。作業フォルダ直下の既存フォルダの中に限る）", Required = true)]
        public string OutputFolder { get; }

        [PLParam(Description = "ComfyUI の接続先（例 http://127.0.0.1:8188）")]
        public string Host { get; }

        [PLParam(Description = "ComfyUI の応答を待つ時間（ミリ秒。1 回の通信ごと）", Min = 100.0)]
        public int TimeoutMs { get; }

        public ComfyFetchOutputsCommand(int modelIndex, string promptId,
            string outputFolder = "",
            string host = "http://127.0.0.1:8188", int timeoutMs = 10000)
            : base(modelIndex)
        {
            PromptId     = promptId ?? "";
            OutputFolder = outputFolder ?? "";
            Host         = host;
            TimeoutMs    = timeoutMs;
        }
    }
}
