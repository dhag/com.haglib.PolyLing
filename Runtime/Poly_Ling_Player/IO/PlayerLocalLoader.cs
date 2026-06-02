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
            btnPmx.style.flexGrow     = 1;
            btnPmx.style.marginRight  = 2;
            btnPmx.style.fontSize     = 10;
            btnPmx.style.height       = 20;
            btnPmx.style.paddingTop   = 0;
            btnPmx.style.paddingBottom = 0;

            var btnMqo = new Button(() => OnMqoRequested?.Invoke()) { text = "Load MQO" };
            btnMqo.style.flexGrow     = 1;
            btnMqo.style.marginLeft   = 2;
            btnMqo.style.fontSize     = 10;
            btnMqo.style.height       = 20;
            btnMqo.style.paddingTop   = 0;
            btnMqo.style.paddingBottom = 0;

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

        /// <summary>直前の FinishLoad で追加されたモデルの index (Undo 記録用)</summary>
        public int LastAddedModelIndex { get; private set; } = -1;
        /// <summary>直前の FinishLoad 直前 (AddModel 前) の CurrentModelIndex (Undo 記録用)</summary>
        public int LastPreviousCurrentModelIndex { get; private set; } = -1;

        private void FinishLoad(string filePath, ModelContext model)
        {
            // 修正：毎回新規作成せず、既存プロジェクトにモデルを追加する
            if (_project == null)
                _project = new ProjectContext(Path.GetFileNameWithoutExtension(filePath));

            // Undo 記録用: 追加前の CurrentModelIndex を保存。
            LastPreviousCurrentModelIndex = _project.CurrentModelIndex;

            int addedIdx = _project.AddModel(model);
            LastAddedModelIndex = addedIdx;
            // 読込直後は新しく追加されたモデルを自動選択する。
            // (問題 D/E: これがないと `project.CurrentModel` が古いモデルのままで、
            // OnLoaded ハンドラ内で古いモデルに対して Record が走るバグを誘発する)
            _project.SelectModel(addedIdx);

            NotifyStatus($"読み込み完了: {model.Name}  Meshes: {model.Count}");
            Debug.Log($"[PlayerLocalLoader] ロード完了: {filePath}");

            OnLoaded?.Invoke(_project);
        }

        private void NotifyStatus(string status) => OnStatusChanged?.Invoke(status);
    }
}
