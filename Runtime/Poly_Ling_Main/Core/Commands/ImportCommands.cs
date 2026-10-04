// ImportCommands.cs
// PMX / MQO / OBJ / STL インポートのコマンド化（STL はフォルダ一括読込を含む）。
// UndoなしのICommand実装（インポートはプロジェクトロード操作であり編集操作ではないため）。
// Runtime/Poly_Ling_Main/Core/Commands/ に配置

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.OBJ;
using Poly_Ling.STL;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Commands
{
    /// <summary>
    /// PMXファイルをインポートするコマンド。
    /// Execute() が同期でインポートを実行し、onResult にModelContextを返す。
    /// 失敗時は onError にエラーメッセージを返す。
    /// </summary>
    public class ImportPmxCommand : ICommand
    {
        private readonly string             _filePath;
        private readonly PMXImportSettings  _settings;
        private readonly Action<ModelContext, PMXImportResult> _onResult;
        private readonly Action<string>     _onError;

        public string         Description  => $"Import PMX: {Path.GetFileName(_filePath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="filePath">PMXファイルパス</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）</param>
        /// <param name="onResult">成功時コールバック (ModelContext, PMXImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportPmxCommand(
            string             filePath,
            PMXImportSettings  settings,
            Action<ModelContext, PMXImportResult> onResult,
            Action<string>     onError = null)
        {
            _filePath = filePath;
            _settings = settings ?? PMXImportSettings.CreateDefault();
            _onResult = onResult;
            _onError  = onError;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                _onError?.Invoke("ファイルパスが空です");
                return;
            }

            if (!File.Exists(_filePath))
            {
                _onError?.Invoke($"ファイルが見つかりません: {_filePath}");
                return;
            }

            PMXImportResult result;
            try
            {
                result = PMXImporter.ImportFile(_filePath, _settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportPmxCommand] {e.Message}");
                _onError?.Invoke(e.Message);
                return;
            }

            if (!result.Success)
            {
                _onError?.Invoke(result.ErrorMessage);
                return;
            }

            var model = new ModelContext
            {
                Name               = Path.GetFileNameWithoutExtension(_filePath),
                FilePath           = _filePath,
                SourceDocument     = result.Document,
                BoneWorldPositions = result.BoneWorldPositions,
                PmxModelInfo       = result.ModelInfo,
            };

            // マテリアルを移送（テクスチャ・色含む）
            if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            foreach (var morph in result.MorphExpressions)
                model.MorphExpressions.Add(morph);

            foreach (var pair in result.MirrorPairs)
                model.MirrorPairs.Add(pair);

            // ボーン階層の WorldMatrix を確定させる。
            // PMXImporter は HierarchyParentIndex と BoneTransform を設定するが
            // WorldMatrix は identity のままである。
            // エディタ側は ViewportCore.Draw() が毎 Repaint で ComputeWorldMatrices() を
            // 呼ぶため問題ないが、プレーヤー側はここで明示的に計算する必要がある。
            model.ComputeWorldMatrices();

            // IK: import で構築した集約 Links から per-bone（EffectorBoneName/IKLink）を確定。
            //     以降 per-bone を正とする（規約: MeshObject.cs「ボーン付帯データ格納規約」）。
            Poly_Ling.Ops.IKChainResolver.SyncPerBoneFromLinks(model);

            _onResult?.Invoke(model, result);
        }
    }

    /// <summary>
    /// MQOファイルをインポートするコマンド。
    /// Execute() が同期でインポートを実行し、onResult にModelContextを返す。
    /// 失敗時は onError にエラーメッセージを返す。
    /// </summary>
    public class ImportMqoCommand : ICommand
    {
        private readonly string             _filePath;
        private readonly MQOImportSettings  _settings;
        private readonly Action<ModelContext, MQOImportResult> _onResult;
        private readonly Action<string>     _onError;

        public string         Description  => $"Import MQO: {Path.GetFileName(_filePath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="filePath">MQOファイルパス</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）</param>
        /// <param name="onResult">成功時コールバック (ModelContext, MQOImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportMqoCommand(
            string             filePath,
            MQOImportSettings  settings,
            Action<ModelContext, MQOImportResult> onResult,
            Action<string>     onError = null)
        {
            _filePath = filePath;
            _settings = settings ?? MQOImportSettings.CreateDefault();
            _onError  = onError;
            _onResult = onResult;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                _onError?.Invoke("ファイルパスが空です");
                return;
            }

            if (!File.Exists(_filePath))
            {
                _onError?.Invoke($"ファイルが見つかりません: {_filePath}");
                return;
            }

            // BaseDir が未設定の場合はファイルのディレクトリを使用
            var settings = _settings;
            if (string.IsNullOrEmpty(settings.BaseDir))
                settings.BaseDir = Path.GetDirectoryName(_filePath);

            MQOImportResult result;
            try
            {
                result = MQOImporter.ImportFile(_filePath, settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportMqoCommand] {e.Message}");
                _onError?.Invoke(e.Message);
                return;
            }

            if (!result.Success)
            {
                _onError?.Invoke(result.ErrorMessage);
                return;
            }

            var model = new ModelContext
            {
                Name           = Path.GetFileNameWithoutExtension(_filePath),
                FilePath       = _filePath,
                SourceDocument = result.Document,
            };

            // マテリアルを移送（テクスチャ・色含む）
            if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            foreach (var mc in result.BoneMeshContexts)
                model.Add(mc);

            foreach (var pair in result.MirrorPairs)
                model.MirrorPairs.Add(pair);

            // 下絵（BackImage チャンク）
            model.Underlay = result.Underlay;

            // ボーン階層の WorldMatrix を確定させる（PMXと同様）。
            model.ComputeWorldMatrices();

            // IK: import で構築した集約 Links から per-bone（EffectorBoneName/IKLink）を確定。
            Poly_Ling.Ops.IKChainResolver.SyncPerBoneFromLinks(model);

            _onResult?.Invoke(model, result);
        }
    }

    /// <summary>
    /// OBJファイルをインポートするコマンド。
    /// Execute() が同期でインポートを実行し、onResult にModelContextを返す。
    /// 失敗時は onError にエラーメッセージを返す。
    ///
    /// OBJ はボーン・モーフ・ミラーを持たないため、生成するのはメッシュと
    /// マテリアルだけになる。階層も無いので親子関係は設定しない。
    /// </summary>
    public class ImportObjCommand : ICommand
    {
        private readonly string            _filePath;
        private readonly ObjImportSettings _settings;
        private readonly Action<ModelContext, ObjImportResult> _onResult;
        private readonly Action<string>    _onError;

        public string         Description  => $"Import OBJ: {Path.GetFileName(_filePath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="filePath">OBJファイルパス</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）</param>
        /// <param name="onResult">成功時コールバック (ModelContext, ObjImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportObjCommand(
            string            filePath,
            ObjImportSettings settings,
            Action<ModelContext, ObjImportResult> onResult,
            Action<string>    onError = null)
        {
            _filePath = filePath;
            _settings = settings ?? ObjImportSettings.CreateDefault();
            _onError  = onError;
            _onResult = onResult;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                _onError?.Invoke("ファイルパスが空です");
                return;
            }

            if (!File.Exists(_filePath))
            {
                _onError?.Invoke($"ファイルが見つかりません: {_filePath}");
                return;
            }

            // BaseDir が未設定の場合はファイルのディレクトリを使用（MTL・テクスチャの基準）
            var settings = _settings;
            if (string.IsNullOrEmpty(settings.BaseDir))
                settings.BaseDir = Path.GetDirectoryName(_filePath);

            ObjImportResult result;
            try
            {
                result = ObjImporter.ImportFile(_filePath, settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportObjCommand] {e.Message}");
                _onError?.Invoke(e.Message);
                return;
            }

            if (!result.Success)
            {
                _onError?.Invoke(result.ErrorMessage);
                return;
            }

            var model = new ModelContext
            {
                Name     = Path.GetFileNameWithoutExtension(_filePath),
                FilePath = _filePath,
            };

            if (result.MaterialReferences != null && result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            // 階層は無いが、WorldMatrix を単位で確定させておく（描画側が参照するため）。
            model.ComputeWorldMatrices();

            _onResult?.Invoke(model, result);
        }
    }

    /// <summary>
    /// STLファイルをインポートするコマンド。
    /// Execute() が同期でインポートを実行し、onResult にModelContextを返す。
    /// 失敗時は onError にエラーメッセージを返す。
    ///
    /// STL は三角形だけを持つ（材質・ボーン・モーフ・階層なし）。
    /// 生成するのはメッシュだけで、親子関係は設定しない。
    /// </summary>
    public class ImportStlCommand : ICommand
    {
        private readonly string            _filePath;
        private readonly StlImportSettings _settings;
        private readonly Action<ModelContext, StlImportResult> _onResult;
        private readonly Action<string>    _onError;

        public string         Description  => $"Import STL: {Path.GetFileName(_filePath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="filePath">STLファイルパス</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）</param>
        /// <param name="onResult">成功時コールバック (ModelContext, StlImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportStlCommand(
            string            filePath,
            StlImportSettings settings,
            Action<ModelContext, StlImportResult> onResult,
            Action<string>    onError = null)
        {
            _filePath = filePath;
            _settings = settings ?? StlImportSettings.CreateDefault();
            _onError  = onError;
            _onResult = onResult;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                _onError?.Invoke("ファイルパスが空です");
                return;
            }

            if (!File.Exists(_filePath))
            {
                _onError?.Invoke($"ファイルが見つかりません: {_filePath}");
                return;
            }

            StlImportResult result;
            try
            {
                result = StlImporter.ImportFile(_filePath, _settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportStlCommand] {e.Message}");
                _onError?.Invoke(e.Message);
                return;
            }

            if (!result.Success)
            {
                _onError?.Invoke(result.ErrorMessage);
                return;
            }

            var model = new ModelContext
            {
                Name     = Path.GetFileNameWithoutExtension(_filePath),
                FilePath = _filePath,
            };

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            // 階層は無いが、WorldMatrix を単位で確定させておく（描画側が参照するため）。
            model.ComputeWorldMatrices();

            _onResult?.Invoke(model, result);
        }
    }

    /// <summary>フォルダ一括 STL 読み込みの結果。</summary>
    public class StlFolderImportResult
    {
        /// <summary>見つけた STL ファイルの数。</summary>
        public int FoundFiles;

        /// <summary>オブジェクトにできたファイルの数。</summary>
        public int ImportedFiles;

        /// <summary>読めなかったファイル（フォルダからの相対パスと理由）。</summary>
        public System.Collections.Generic.List<(string RelativePath, string Reason)> Failed { get; }
            = new System.Collections.Generic.List<(string, string)>();

        /// <summary>同名のため番号を付けたオブジェクトの数。</summary>
        public int Renamed;

        public int TotalVertices;
        public int TotalFaces;
        public int DroppedDegenerateFaces;
    }

    /// <summary>
    /// フォルダの下の STL をまとめて 1 つのモデルへ読み込むコマンド。
    /// 1 ファイル = 1 オブジェクト（ASCII の複数 solid も 1 つにまとめる）。
    ///
    /// 【並び】フォルダからの相対パスの順（大文字小文字は区別しない）。
    /// 【名前】ファイル名（拡張子なし）。既に使われていれば「名前_1」「名前_2」… の最小の空き。
    /// 【失敗】読めないファイルは飛ばして Failed に積む。1 つも読めなければ onError。
    /// 【対象】拡張子 .stl（大文字小文字は区別しない）だけ。それ以外のファイルは見ない。
    /// </summary>
    public class ImportStlBatchCommand : ICommand
    {
        private readonly string            _folderPath;
        private readonly bool              _includeSubfolders;
        private readonly StlImportSettings _settings;
        private readonly Action<ModelContext, StlFolderImportResult> _onResult;
        private readonly Action<string>    _onError;

        public string         Description  => $"Import STL Folder: {Path.GetFileName(_folderPath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="folderPath">読み込むフォルダ（解決済みの実経路）</param>
        /// <param name="includeSubfolders">サブフォルダの下も読むか</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）。全ファイル共通</param>
        /// <param name="onResult">成功時コールバック (ModelContext, StlFolderImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportStlBatchCommand(
            string            folderPath,
            bool              includeSubfolders,
            StlImportSettings settings,
            Action<ModelContext, StlFolderImportResult> onResult,
            Action<string>    onError = null)
        {
            _folderPath        = folderPath;
            _includeSubfolders = includeSubfolders;
            _settings          = settings ?? StlImportSettings.CreateDefault();
            _onResult          = onResult;
            _onError           = onError;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_folderPath))
            {
                _onError?.Invoke("フォルダが空です");
                return;
            }
            if (!Directory.Exists(_folderPath))
            {
                _onError?.Invoke($"フォルダが見つかりません: {_folderPath}");
                return;
            }

            string root = Path.GetFullPath(_folderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var files = new System.Collections.Generic.List<string>();
            try
            {
                var option = _includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.GetFiles(root, "*", option))
                    if (string.Equals(Path.GetExtension(f), ".stl", StringComparison.OrdinalIgnoreCase))
                        files.Add(f);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportStlBatchCommand] {e}");
                _onError?.Invoke($"フォルダを列挙できません: {e.Message}");
                return;
            }

            string Rel(string f) => f.Substring(root.Length).TrimStart(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            files.Sort((a, b) => string.Compare(Rel(a), Rel(b), StringComparison.OrdinalIgnoreCase));

            var result = new StlFolderImportResult { FoundFiles = files.Count };
            if (files.Count == 0)
            {
                _onError?.Invoke($"STL ファイルがありません: {root}");
                return;
            }

            var model = new ModelContext
            {
                Name     = Path.GetFileName(root),
                FilePath = root,
            };

            var taken = new System.Collections.Generic.HashSet<string>();
            foreach (var f in files)
            {
                string baseName = Path.GetFileNameWithoutExtension(f);
                string name     = baseName;
                if (taken.Contains(name))
                {
                    int k = 1;
                    while (taken.Contains($"{baseName}_{k}")) k++;
                    name = $"{baseName}_{k}";
                    result.Renamed++;
                }

                StlImportResult r;
                try
                {
                    r = StlImporter.ImportFileAsOneObject(f, _settings, name);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ImportStlBatchCommand] {Rel(f)}: {e}");
                    result.Failed.Add((Rel(f), e.Message));
                    continue;
                }

                if (r == null || !r.Success || r.MeshContexts.Count == 0)
                {
                    result.Failed.Add((Rel(f), r?.ErrorMessage ?? "不明"));
                    continue;
                }

                taken.Add(name);
                foreach (var mc in r.MeshContexts) model.Add(mc);
                result.ImportedFiles++;
                result.TotalVertices          += r.TotalVertices;
                result.TotalFaces             += r.TotalFaces;
                result.DroppedDegenerateFaces += r.DroppedDegenerateFaces;
            }

            if (result.ImportedFiles == 0)
            {
                _onError?.Invoke($"読み込めた STL がありません（{files.Count} 件すべて失敗。コンソール参照）");
                foreach (var (rel, reason) in result.Failed)
                    Debug.LogWarning($"[ImportStlBatchCommand] 失敗: {rel}: {reason}");
                return;
            }

            // 階層は無いが、WorldMatrix を単位で確定させておく（描画側が参照するため）。
            model.ComputeWorldMatrices();

            _onResult?.Invoke(model, result);
        }
    }

    /// <summary>
    /// VRM（1.0 / 0.x）ファイルをインポートするコマンド。
    /// Execute() が同期でインポートを実行し、onResult にModelContextを返す。
    /// 失敗時は onError にエラーメッセージを返す。
    ///
    /// 実処理は PLVrm10ImportBridge（PolyLing.Vrm10 の実装）が行う。
    /// VRM パッケージが無い環境では Vrm10ImporterNull が失敗を返す。
    /// </summary>
    public class ImportVrmCommand : ICommand
    {
        private readonly string                     _filePath;
        private readonly Poly_Ling.Vrm.Vrm10ImportSettings _settings;
        private readonly Action<ModelContext, Poly_Ling.Vrm.Vrm10ImportResult> _onResult;
        private readonly Action<string>             _onError;

        public string         Description  => $"Import VRM: {Path.GetFileName(_filePath)}";
        public MeshUpdateLevel UpdateLevel => MeshUpdateLevel.Topology;

        /// <param name="filePath">VRMファイルパス</param>
        /// <param name="settings">インポート設定（nullの場合デフォルト使用）</param>
        /// <param name="onResult">成功時コールバック (ModelContext, Vrm10ImportResult)</param>
        /// <param name="onError">失敗時コールバック (エラーメッセージ)</param>
        public ImportVrmCommand(
            string                      filePath,
            Poly_Ling.Vrm.Vrm10ImportSettings settings,
            Action<ModelContext, Poly_Ling.Vrm.Vrm10ImportResult> onResult,
            Action<string>              onError = null)
        {
            _filePath = filePath;
            _settings = settings ?? Poly_Ling.Vrm.Vrm10ImportSettings.CreateDefault();
            _onResult = onResult;
            _onError  = onError;
        }

        public void Execute()
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                _onError?.Invoke("ファイルパスが空です");
                return;
            }

            if (!File.Exists(_filePath))
            {
                _onError?.Invoke($"ファイルが見つかりません: {_filePath}");
                return;
            }

            var importer = Poly_Ling.Vrm.PLVrm10ImportBridge.I;
            if (!importer.IsAvailable)
            {
                _onError?.Invoke("VRM インポータが利用できません（VRM パッケージ未導入、または Play 中でない）");
                return;
            }

            Poly_Ling.Vrm.Vrm10ImportResult result;
            try
            {
                result = importer.Import(_filePath, _settings);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportVrmCommand] {e.Message}");
                _onError?.Invoke(e.Message);
                return;
            }

            if (result == null || !result.Success)
            {
                _onError?.Invoke(result?.ErrorMessage ?? "読み込み結果がありません");
                return;
            }

            var model = new ModelContext
            {
                Name     = Path.GetFileNameWithoutExtension(_filePath),
                FilePath = _filePath,
            };

            if (result.MaterialReferences.Count > 0)
                model.MaterialReferences = result.MaterialReferences;

            foreach (var mc in result.MeshContexts)
                model.Add(mc);

            foreach (var morph in result.MorphExpressions)
                model.MorphExpressions.Add(morph);

            model.SpringBoneColliderGroupNames = new System.Collections.Generic.List<string>(
                result.SpringBoneColliderGroupNames);
            model.VrmMeta   = result.VrmMeta;
            model.VrmLookAt = result.VrmLookAt;

            // Humanoid: per-bone（MeshObject.HumanBodyBone）が正。読込境界で Dict を再構築する
            // （HumanoidMappingResolver.cs 冒頭の同期規約）。
            Poly_Ling.Ops.HumanoidMappingResolver.RebuildMappingFromPerBone(model);

            // ボーン階層の WorldMatrix を確定させる（PMX / MQO と同じ理由）。
            model.ComputeWorldMatrices();

            _onResult?.Invoke(model, result);
        }
    }
}
