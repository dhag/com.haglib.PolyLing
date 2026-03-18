// Assets/Editor/Poly_Ling/Core/UnifiedSystemAdapter_UpdateMode.cs
// 統合システムアダプター - 更新モード管理（パーシャルクラス）
// 全ての操作はここで定義された入口メソッドを経由すること。
//
// 【ルール】
// - モードを直接操作してはならない。必ずこのファイルの入口メソッドを使用する。
// - 新しい操作パターンが必要な場合はここに入口メソッドを追加する。
// - PrepareUnifiedDrawing等の描画コードは CurrentProfile を参照して分岐する。
//
// 【ワンショット方式】
// デフォルト状態は Idle（再計算なし、キャッシュ描画のみ）。
// データ変更が起きると RequestNormal() で Normal に昇格。
// PrepareUnifiedDrawing が1回処理した後、ConsumeNormalMode() で Idle に自動降格。
// これにより「何もしていない時」は一切の重い処理が走らない。
//
// ★★★ 禁忌（絶対厳守） ★★★
//
// 1. TransformDragging中にRequestNormal()のガードを迂回してはならない。
//    UpdateFrame() → ProcessUpdates/ExecuteUpdates の全パイプラインが
//    毎ドラッグフレームで走ると1FPS以下に落ちる。
//
// 2. AllowMeshRebuild=true のプロファイルをTransformDragging中に適用してはならない。
//    UpdateWireframeMesh/UpdatePointMeshは全ライン・全頂点を走査する重い処理。
//
// 3. ドラッグ中のリアルタイム表示更新が必要な場合:
//    - トポロジ変更を伴わない表示専用入口を使用すること
//      → UnifiedMeshSystem.ProcessTransformUpdate()
//        （_bufferManager.UpdatePositions: Array.Copy + SetData のみ）
//    - ホバーチェック無効化はUpdateModeProfile.AllowHitTest=falseで制御済み
//      （TransformDraggingプロファイルで既にfalse）
//    - RequestNormal経由の一時昇格は禁止
//
// 過去の障害: AllowMeshRebuild=true + RequestNormal迂回 → 1FPS
// ★★★★★★★★★★★★★★★★★★★★

using UnityEngine;

namespace Poly_Ling.Core
{
    public partial class UnifiedSystemAdapter
    {
        // ============================================================
        // 更新モード状態
        // ============================================================

        private UpdateMode _currentMode = UpdateMode.Normal;
        private UpdateModeProfile _currentProfile = UpdateModeProfile.Normal;

        /// <summary>現在の更新モード</summary>
        public UpdateMode CurrentMode => _currentMode;

        /// <summary>現在の更新モードプロファイル（各処理の許可/禁止を参照）</summary>
        public UpdateModeProfile CurrentProfile => _currentProfile;

        // ============================================================
        // リアルタイムトランスフォーム更新（未実装・フラグのみ）
        // ============================================================

        /// <summary>
        /// ドラッグ中にワイヤーフレーム・頂点オーバーレイをリアルタイム更新するか。
        /// true（デフォルト）だが、現時点では動作に影響しない。
        /// 今後の軽量パス実装で使用予定。
        /// 
        /// 【禁忌】このフラグを理由にAllowMeshRebuild=trueのプロファイルを
        /// TransformDragging中に適用してはならない。1FPS以下に落ちる。
        /// </summary>
        public bool RealtimeTransformUpdate { get; set; } = true;

        // ============================================================
        // ワンショット制御
        // ============================================================

        /// <summary>
        /// Normalモードを1フレーム要求する。
        /// 次のPrepareUnifiedDrawingで全処理が1回実行され、その後Idleに自動降格。
        /// 
        /// 呼び出し元:
        ///   - NotifyTopologyChanged, NotifyTransformChanged, NotifySelectionChanged
        ///   - ExitCameraDragging, ExitTransformDragging
        ///   - その他データ変更を行う全ての操作
        /// </summary>
        public void RequestNormal()
        {
            // CameraDragging/TransformDragging中は無視（Exitで改めて呼ばれる）
            if (_currentMode == UpdateMode.CameraDragging ||
                _currentMode == UpdateMode.TransformDragging)
                return;

            SetMode(UpdateMode.Normal);
        }

        /// <summary>
        /// Normalモードの処理を消費してIdleに降格する。
        /// PrepareUnifiedDrawing()の末尾で呼ぶこと。
        /// </summary>
        public void ConsumeNormalMode()
        {
            if (_currentMode == UpdateMode.Normal)
            {
                SetMode(UpdateMode.Idle);
            }
        }

        // ============================================================
        // 入口メソッド - カメラ操作
        // ============================================================

        /// <summary>
        /// カメラドラッグ開始
        /// 呼び出し元: PolyLing.BeginCameraDrag()
        /// </summary>
        public void EnterCameraDragging()
        {
            SetMode(UpdateMode.CameraDragging);
        }

        /// <summary>
        /// カメラドラッグ終了 → Normal(1フレーム) → Idle
        /// 呼び出し元: PolyLing.EndCameraDrag()
        /// </summary>
        public void ExitCameraDragging()
        {
            SetMode(UpdateMode.Normal);
        }

        // ============================================================
        // 入口メソッド - 頂点ドラッグ
        // ============================================================

        /// <summary>
        /// 頂点ドラッグ開始
        /// 呼び出し元: MoveTool, SculptTool, SimpleMorphPanel等
        /// </summary>
        public void EnterTransformDragging()
        {
            SetMode(UpdateMode.TransformDragging);
        }

        /// <summary>
        /// 頂点ドラッグ終了 → Normal(1フレーム) → Idle
        /// 呼び出し元: MoveTool, SculptTool, SimpleMorphPanel等
        /// </summary>
        public void ExitTransformDragging()
        {
            SetMode(UpdateMode.Normal);
        }

        // ============================================================
        // モード設定（内部）
        // ============================================================

        /// <summary>
        /// モードを切り替え、プロファイルに基づいてSkipフラグを一括設定
        /// </summary>
        private void SetMode(UpdateMode mode)
        {
            _currentMode = mode;
            _currentProfile = UpdateModeProfile.FromMode(mode);
        }
    }
}
