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
    /// <summary>
    /// ローカルファイルロードを担うサブクラス。
    /// ロード結果の保持・UI 構築・コールバック通知を行う。
    /// </summary>
    public class PlayerLocalLoader
    {
        // ================================================================
        // 状態
        // ================================================================

        private ProjectContext _project;

        /// <summary>ロード済みプロジェクト。未ロード時は null。</summary>
        public ProjectContext Project => _project;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// ロード成功時に呼ばれる。引数は生成された ProjectContext。
        /// </summary>
        public Action<ProjectContext> OnLoaded;

        /// <summary>
        /// ステータス文字列が変化したときに呼ばれる。
        /// </summary>
        public Action<string> OnStatusChanged;

        /// <summary>
        /// LOAD PMX ボタンが押されたときに呼ばれる。
        /// 右ペインへの PMX サブパネル表示切替を Viewer から登録する。
        /// </summary>
        public Action OnPmxRequested;

        /// <summary>
        /// LOAD MQO ボタンが押されたときに呼ばれる。
        /// 右ペインへの MQO サブパネル表示切替を Viewer から登録する。
        /// </summary>
        public Action OnMqoRequested;

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>保持しているプロジェクトを破棄する。</summary>
        public void Clear()
        {
            _project = null;
        }

        /// <summary>
        /// 拡張子から PMX / MQO を判定してデフォルト設定でロードする。
        /// </summary>
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

        /// <summary>
        /// PMX ファイルを指定設定でロードする。
        /// </summary>
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

        /// <summary>
        /// MQO ファイルを指定設定でロードする。
        /// </summary>
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
        // UI（UIToolkit）
        // ================================================================

        /// <summary>
        /// LOAD PMX / LOAD MQO ボタンを parent に追加する。
        /// ボタン押下は OnPmxRequested / OnMqoRequested に委譲する。
        /// </summary>
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

        /// <summary>
        /// 既にインポート済みの ModelContext を受け取りプロジェクトとして保持する。
        /// ImportPmxCommand / ImportMqoCommand の onResult から呼ぶ。
        /// </summary>
        public void LoadModel(string filePath, ModelContext model)
        {
            FinishLoad(filePath, model);
        }

        // ================================================================
        // 内部
        // ================================================================

        private void FinishLoad(string filePath, ModelContext model)
        {
            var project = new ProjectContext(Path.GetFileNameWithoutExtension(filePath));
            project.AddModel(model);
            _project = project;

            NotifyStatus($"読み込み完了: {model.Name}  Meshes: {model.Count}");
            Debug.Log($"[PlayerLocalLoader] ロード完了: {filePath}");

            OnLoaded?.Invoke(project);
        }

        private void NotifyStatus(string status) => OnStatusChanged?.Invoke(status);
    }
}
