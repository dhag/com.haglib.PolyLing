// PanelCommand.Motion.cs
// PolyLing モーション JSON（*.plmotion.json）の書き出し・検査の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）

namespace Poly_Ling.Data
{
    /// <summary>モーション変換の読み込み元の種類。</summary>
    public enum MotionSourceKind
    {
        /// <summary>VMD（MMD モーション）。</summary>
        Vmd = 0,

        /// <summary>Unity クリップの JSON（UnityClipDTO）。</summary>
        UnityClipJson = 1,

        /// <summary>PolyLing モーション JSON（version 1 / 2）。</summary>
        PolyLingMotionJson = 2,
    }

    /// <summary>
    /// モーションファイルを読み、PolyLing モーション JSON（version 2）として書き出す。
    /// モデルは参照しない。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "VMD・Unity クリップ JSON・PolyLing モーション JSON を読み、検査してから PolyLing モーション JSON として書き出す。モデルは参照しない。作業フォルダが要る。")]
    [PLResult("errors",      PLResultKind.Integer, Description = "検査で見つかったエラーの数。1 以上なら書き出していない")]
    [PLResult("warnings",    PLResultKind.Integer, Description = "検査で見つかった警告の数")]
    [PLResult("bones",       PLResultKind.Integer, Description = "ボーントラックの数")]
    [PLResult("bakedBones",  PLResultKind.Integer, Description = "焼いた本体ボーントラックの数")]
    [PLResult("muscles",     PLResultKind.Integer, Description = "マッスルトラックの数")]
    [PLResult("expressions", PLResultKind.Integer, Description = "表情トラックの数")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "書き出した長さ（秒）")]
    [PLResult("issues",      PLResultKind.Text,    Description = "エラーと警告の一覧（先頭の一部）")]
    public class ExportMotionJsonCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionJsonSavePath",
                 Description = "書き出し先の .json（推奨は .plmotion.json）。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "MotionJsonSourcePath",
                 Description = "読み込むモーションファイル。作業フォルダからの相対経路", Required = true)]
        public string SourcePath { get; }

        [PLParam(TextKey = "MotionJsonSourceKind",
                 Description = "読み込むファイルの種類")]
        public MotionSourceKind SourceKind { get; }

        public ExportMotionJsonCommand(
            int modelIndex, string filePath, string sourcePath,
            MotionSourceKind sourceKind = MotionSourceKind.Vmd)
            : base(modelIndex)
        {
            FilePath   = filePath;
            SourcePath = sourcePath;
            SourceKind = sourceKind;
        }
    }

    /// <summary>
    /// PolyLing モーション JSON を読んで検査する。ファイルもモデルも変えない。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "PolyLing モーション JSON を読んで検査し、トラック数と問題点を返す。ファイルもモデルも変えない。作業フォルダが要る。")]
    [PLResult("valid",       PLResultKind.Flag,    Description = "エラーが無く読み込めるか")]
    [PLResult("version",     PLResultKind.Integer, Description = "ファイルに書かれていた版（読めなかったときは 0）")]
    [PLResult("errors",      PLResultKind.Integer, Description = "エラーの数")]
    [PLResult("warnings",    PLResultKind.Integer, Description = "警告の数")]
    [PLResult("bones",       PLResultKind.Integer, Description = "ボーントラックの数")]
    [PLResult("bakedBones",  PLResultKind.Integer, Description = "焼いた本体ボーントラックの数")]
    [PLResult("muscles",     PLResultKind.Integer, Description = "マッスルトラックの数")]
    [PLResult("expressions", PLResultKind.Integer, Description = "表情トラックの数")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "長さ（秒）")]
    [PLResult("issues",      PLResultKind.Text,    Description = "エラーと警告の一覧（先頭の一部）")]
    public class QueryMotionJsonCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionJsonQueryPath",
                 Description = "検査する .json。作業フォルダからの相対経路", Required = true)]
        public string FilePath { get; }

        public QueryMotionJsonCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath;
        }
    }
}
