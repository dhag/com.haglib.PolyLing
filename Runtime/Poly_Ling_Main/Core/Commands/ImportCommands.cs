// ImportCommands.cs
// PMX / MQO / OBJ インポートのコマンド化。
// UndoなしのICommand実装（インポートはプロジェクトロード操作であり編集操作ではないため）。
// Runtime/Poly_Ling_Main/Core/Commands/ に配置

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.OBJ;
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
