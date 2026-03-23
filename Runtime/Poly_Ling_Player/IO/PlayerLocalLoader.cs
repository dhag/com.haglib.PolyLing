// PlayerLocalLoader.cs
// ローカルファイル（PMX/MQO）ロード処理と UI 構築を集約するサブクラス。
// PolyLingPlayerViewer から分離し、将来の拡張（ドラッグ＆ドロップ等）も
// このクラスに閉じ込める。
// Runtime/Poly_Ling_Player/IO/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;

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
        private TextField      _pathField;

        /// <summary>ロード済みプロジェクト。未ロード時は null。</summary>
        public ProjectContext Project => _project;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// ロード成功時に呼ばれる。引数は生成された ProjectContext。
        /// カメラ初期化・レンダラークリア等を登録する。
        /// </summary>
        public Action<ProjectContext> OnLoaded;

        /// <summary>
        /// ステータス文字列が変化したときに呼ばれる。
        /// 引数は新しいステータス文字列。
        /// </summary>
        public Action<string> OnStatusChanged;

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>保持しているプロジェクトを破棄する。</summary>
        public void Clear()
        {
            _project = null;
        }

        /// <summary>
        /// 拡張子から PMX / MQO を判定してロードする。
        /// 成功時は <see cref="OnLoaded"/> を呼ぶ。
        /// 失敗時は <see cref="OnStatusChanged"/> にエラーを通知する。
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

            var project = new ProjectContext(Path.GetFileNameWithoutExtension(filePath));
            project.AddModel(model);
            _project = project;

            NotifyStatus($"読み込み完了: {model.Name}  Meshes: {model.Count}");
            Debug.Log($"[PlayerLocalLoader] ロード完了: {filePath}");

            OnLoaded?.Invoke(project);
        }

        // ================================================================
        // UI（UIToolkit）
        // ================================================================

        /// <summary>
        /// パス入力フィールドと Load PMX / Load MQO ボタンを <paramref name="parent"/> に追加する。
        /// 内部で <see cref="Load"/> へ配線済み。
        /// </summary>
        public void BuildUI(VisualElement parent)
        {
            // パス入力行
            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.marginBottom  = 4;

            var pathLabel = new Label("Path:");
            pathLabel.style.width          = 36;
            pathLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            pathLabel.style.flexShrink     = 0;

            _pathField = new TextField();
            _pathField.style.flexGrow = 1;

            pathRow.Add(pathLabel);
            pathRow.Add(_pathField);
            parent.Add(pathRow);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 4;

            var btnPmx = new Button(() => Load(ResolveExtension(".pmx"))) { text = "Load PMX" };
            btnPmx.style.flexGrow    = 1;
            btnPmx.style.marginRight = 2;

            var btnMqo = new Button(() => Load(ResolveExtension(".mqo"))) { text = "Load MQO" };
            btnMqo.style.flexGrow   = 1;
            btnMqo.style.marginLeft = 2;

            btnRow.Add(btnPmx);
            btnRow.Add(btnMqo);
            parent.Add(btnRow);
        }

        // ================================================================
        // 内部
        // ================================================================

        private string ResolveExtension(string ext)
        {
            var path = _pathField?.value ?? "";
            return Path.ChangeExtension(path, null) + ext;
        }

        private void NotifyStatus(string status) => OnStatusChanged?.Invoke(status);
    }
}
