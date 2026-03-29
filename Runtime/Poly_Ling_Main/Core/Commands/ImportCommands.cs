// ImportCommands.cs
// PMX / MQO インポートのコマンド化。
// UndoなしのICommand実装（インポートはプロジェクトロード操作であり編集操作ではないため）。
// Runtime/Poly_Ling_Main/Core/Commands/ に配置

using System;
using System.IO;
using UnityEngine;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
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

            _onResult?.Invoke(model, result);
        }
    }
}
