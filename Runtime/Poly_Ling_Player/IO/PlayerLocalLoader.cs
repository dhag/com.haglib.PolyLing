// PlayerLocalLoader.cs
// ローカルファイル（PMX/MQO）ロード処理と UI 構築を集約するサブクラス。
// Runtime/Poly_Ling_Player/IO/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.PMX;
using Poly_Ling.MQO;

namespace Poly_Ling.Player
{
    public class PlayerLocalLoader
    {
        // ================================================================
        // 状態
        // ================================================================

        private ProjectContext _project;

        public ProjectContext Project => _project;

        // ================================================================
        // コールバック
        // ================================================================

        public Action<ProjectContext> OnLoaded;
        public Action<string> OnStatusChanged;
        public Action OnPmxRequested;
        public Action OnMqoRequested;

        // ================================================================
        // 公開 API
        // ================================================================

        public void Clear()
        {
            _project = null;
        }

        public void Load(string filePath)
        {
            NotifyStatus($"読み込み中: {Path.GetFileName(filePath)}");
            var model = PolyLingPlayerIO.ImportAuto(filePath, out string error);
            if (model == null)
            {
                NotifyStatus($"読み込み失敗: {error}");
                Debug.LogError($"[PlayerLocalLoader] {error}");
                return;
            }
            FinishLoad(filePath, model);
        }

        public void Load(string filePath, PMXImportSettings settings)
        {
            NotifyStatus($"読み込み中: {Path.GetFileName(filePath)}");
            var model = PolyLingPlayerIO.ImportPmx(filePath, settings, out string error);
            if (model == null)
            {
                NotifyStatus($"読み込み失敗: {error}");
                Debug.LogError($"[PlayerLocalLoader] {error}");
                return;
            }
            FinishLoad(filePath, model);
        }

        public void Load(string filePath, MQOImportSettings settings)
        {
            NotifyStatus($"読み込み中: {Path.GetFileName(filePath)}");
            var model = PolyLingPlayerIO.ImportMqo(filePath, settings, out string error);
            if (model == null)
            {
                NotifyStatus($"読み込み失敗: {error}");
                Debug.LogError($"[PlayerLocalLoader] {error}");
                return;
            }
            FinishLoad(filePath, model);
        }

        // ================================================================
        // UI
        // ================================================================

        public void BuildUI(VisualElement parent)
        {
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;

            var btnPmx = new Button(() => OnPmxRequested?.Invoke()) { text = "Load PMX" };
            btnPmx.style.flexGrow    = 1;
            btnPmx.style.marginRight = 2;

            var btnMqo = new Button(() => OnMqoRequested?.Invoke()) { text = "Load MQO" };
            btnMqo.style.flexGrow   = 1;
            btnMqo.style.marginLeft = 2;

            btnRow.Add(btnPmx);
            btnRow.Add(btnMqo);
            parent.Add(btnRow);
        }

        public void LoadModel(string filePath, ModelContext model)
        {
            FinishLoad(filePath, model);
        }

        /// <summary>
        /// プロジェクトが存在しなければ空のプロジェクト+モデルを作成する。
        /// OnLoaded は発火しない。図形生成など非インポート操作用。
        /// </summary>
        public ProjectContext EnsureProject()
        {
            if (_project == null)
            {
                _project = new ProjectContext("Untitled");
                _project.AddModel(new ModelContext("Model"));
            }
            else if (_project.ModelCount == 0)
            {
                _project.AddModel(new ModelContext("Model"));
            }
            return _project;
        }

        // ================================================================
        // 内部
        // ================================================================

        private void FinishLoad(string filePath, ModelContext model)
        {
            // 修正：毎回新規作成せず、既存プロジェクトにモデルを追加する
            if (_project == null)
                _project = new ProjectContext(Path.GetFileNameWithoutExtension(filePath));

            _project.AddModel(model);

            NotifyStatus($"読み込み完了: {model.Name}  Meshes: {model.Count}");
            Debug.Log($"[PlayerLocalLoader] ロード完了: {filePath}");

            OnLoaded?.Invoke(_project);
        }

        private void NotifyStatus(string status) => OnStatusChanged?.Invoke(status);
    }
}
