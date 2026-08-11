// PartsDictionaryPath.cs
// 選択辞書 CSV の受け渡し用フォルダ（partsDictionary）の場所を決める。
//
// 【なぜ固定フォルダにするか】
//   選択辞書そのものはプロジェクトに保存済み（CSV プロジェクトなら *.mesh.csv の
//   ss 行、.mfproj なら JSON）。Selected_*.csv / meshselsets.csv は永続化ではなく、
//   オブジェクト間・モデル間で辞書を持ち回るための「受け渡しファイル」。
//   受け渡しのたびにフォルダ選択ダイアログ（Player では SHBrowseForFolder）を
//   開かせるのは操作が重いため、プロジェクトの隣に決め打ちのフォルダを置く。
//
// 【解決規則】
//   1. RecentPaths["Project.CsvPath"]  … CSV プロジェクトファイルのパス
//   2. RecentPaths["Project.JsonPath"] … .mfproj のパス
//   いずれかが設定されていれば、その親ディレクトリ直下の partsDictionary。
//   どちらも未設定なら <persistentDataPath>/PolyLing/partsDictionary へ退避する
//   （プロジェクト未保存でも辞書の受け渡しはできるようにするため）。
//
// Runtime/Poly_Ling_Main/Core/Config/ に配置

using System.IO;
using UnityEngine;

namespace Poly_Ling.Core
{
    /// <summary>選択辞書 CSV の受け渡しフォルダを解決する。</summary>
    public static class PartsDictionaryPath
    {
        /// <summary>プロジェクトフォルダ直下に作る辞書フォルダ名。</summary>
        public const string FolderName = "partsDictionary";

        /// <summary>オブジェクト選択辞書の既定ファイル名。</summary>
        public const string MeshSelSetsFileName = "meshselsets.csv";

        // アンカーに使う RecentPaths のキー。
        // どちらも「ファイルパス」を保持する（フォルダではない）。
        private const string ProjectCsvPathKey  = "Project.CsvPath";
        private const string ProjectJsonPathKey = "Project.JsonPath";

        /// <summary>
        /// 辞書フォルダのパスを返す。フォルダの作成は行わない。
        /// </summary>
        public static string Resolve()
        {
            string baseDir = ResolveProjectDir();
            return Path.Combine(baseDir, FolderName);
        }

        /// <summary>
        /// 辞書フォルダのパスを返し、無ければ作成する。書き出し前に使う。
        /// 作成に失敗した場合は例外を投げずに警告を出し、パスだけ返す
        /// （後段の書き出しが失敗して個別のエラーを出すため）。
        /// </summary>
        public static string ResolveForWrite()
        {
            string path = Resolve();
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[PartsDictionaryPath] フォルダを作成できません: {path} ({e.Message})");
            }
            return path;
        }

        /// <summary>オブジェクト選択辞書の既定ファイルパス。</summary>
        public static string ResolveMeshSelSetsFile()
            => Path.Combine(Resolve(), MeshSelSetsFileName);

        /// <summary>
        /// プロジェクトが未保存で、退避先（persistentDataPath）を使っているか。
        /// UI で注意表示を出すために使う。
        /// </summary>
        public static bool IsFallback() => string.IsNullOrEmpty(GetProjectFilePath());

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>アンカーに使うプロジェクトファイルのパス。未設定なら空文字。</summary>
        private static string GetProjectFilePath()
        {
            string csv = RecentPaths.Get(ProjectCsvPathKey);
            if (!string.IsNullOrEmpty(csv)) return csv;
            return RecentPaths.Get(ProjectJsonPathKey);
        }

        private static string ResolveProjectDir()
        {
            string projectFile = GetProjectFilePath();
            if (!string.IsNullOrEmpty(projectFile))
            {
                string dir = Path.GetDirectoryName(projectFile);
                if (!string.IsNullOrEmpty(dir)) return dir;
            }
            return Path.Combine(Application.persistentDataPath, "PolyLing");
        }
    }
}
