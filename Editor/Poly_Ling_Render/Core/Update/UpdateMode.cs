// Assets/Editor/Poly_Ling/Core/Update/UpdateMode.cs
// 更新モード定義
// 操作の種類に応じて実行する処理を制御するためのモードとプロファイル

namespace Poly_Ling.Core
{
    /// <summary>
    /// 更新モード
    /// 各操作がどのモードに該当するかを明示的に指定し、
    /// モードに応じて実行する処理を制御する。
    /// 
    /// 使用例:
    ///   カメラドラッグ開始 → EnterCameraDragging() → CameraDragging
    ///   カメラドラッグ終了 → ExitCameraDragging() → Normal
    /// </summary>
    public enum UpdateMode
    {
        /// <summary>
        /// 通常モード（全処理を実行）
        /// デフォルト状態。何も操作していない、または操作確定後。
        /// </summary>
        Normal = 0,

        /// <summary>
        /// アイドル状態（描画のみ、再計算なし）
        /// Normalモードで1フレーム処理した後に自動降格される。
        /// キャッシュ済みメッシュを使って描画だけ行う。
        /// </summary>
        Idle,

        /// <summary>
        /// カメラドラッグ中
        /// 回転・パン・ズームのドラッグ操作中。
        /// シェーダーがカメラ行列で変換するため、CPU/GPU側の再計算は不要。
        /// </summary>
        CameraDragging,

        /// <summary>
        /// 頂点ドラッグ中（Phase 2で実装）
        /// MoveTool, ScaleTool, RotateTool, SculptTool, モーフスライダー等。
        /// 位置バッファは更新するが、ワイヤ/頂点メッシュ再構築やヒットテストは不要。
        /// </summary>
        TransformDragging,

        /// <summary>
        /// 選択変更（Phase 4で実装）
        /// 頂点/エッジ/面の選択変更後。
        /// 選択フラグの更新とワイヤ/頂点メッシュ再構築が必要。
        /// </summary>
        Selection,

        /// <summary>
        /// アクティブメッシュ変更（Phase 3で実装）
        /// メッシュリストで別のメッシュを選択した後。
        /// 選択フラグ + GPU Visibility + ワイヤ/頂点メッシュ再構築が必要。
        /// </summary>
        ActiveMesh,
    }

    /// <summary>
    /// 更新モードプロファイル
    /// 各モードで何を許可/禁止するかを定義する。
    /// </summary>
    public readonly struct UpdateModeProfile
    {
        // ============================================================
        // フラグ
        // ============================================================

        /// <summary>ヒットテスト（マウスホバー判定）を実行するか</summary>
        public readonly bool AllowHitTest;

        /// <summary>GPU頂点フラグのCPU読み戻しを実行するか</summary>
        public readonly bool AllowVertexFlagsReadback;

        /// <summary>GPU可視性計算（背面カリング等）を実行するか</summary>
        public readonly bool AllowGpuVisibility;

        /// <summary>非選択メッシュのワイヤーフレーム/頂点を描画するか</summary>
        public readonly bool AllowUnselectedOverlay;

        /// <summary>ワイヤ/頂点メッシュの再構築を実行するか</summary>
        public readonly bool AllowMeshRebuild;

        /// <summary>選択状態の同期・フラグ更新を実行するか</summary>
        public readonly bool AllowSelectionSync;

        // ============================================================
        // コンストラクタ
        // ============================================================

        public UpdateModeProfile(
            bool allowHitTest,
            bool allowVertexFlagsReadback,
            bool allowGpuVisibility,
            bool allowUnselectedOverlay,
            bool allowMeshRebuild,
            bool allowSelectionSync)
        {
            AllowHitTest = allowHitTest;
            AllowVertexFlagsReadback = allowVertexFlagsReadback;
            AllowGpuVisibility = allowGpuVisibility;
            AllowUnselectedOverlay = allowUnselectedOverlay;
            AllowMeshRebuild = allowMeshRebuild;
            AllowSelectionSync = allowSelectionSync;
        }

        // ============================================================
        // プリセット
        // ============================================================

        /// <summary>
        /// 通常モード: 全処理を実行
        /// </summary>
        public static UpdateModeProfile Normal => new UpdateModeProfile(
            allowHitTest: true,
            allowVertexFlagsReadback: true,
            allowGpuVisibility: true,
            allowUnselectedOverlay: true,
            allowMeshRebuild: true,
            allowSelectionSync: true
        );

        /// <summary>
        /// アイドル状態: 再計算なし、キャッシュ済みメッシュで描画のみ
        /// 非選択メッシュのオーバーレイは表示を維持する。
        /// </summary>
        public static UpdateModeProfile Idle => new UpdateModeProfile(
            allowHitTest: false,
            allowVertexFlagsReadback: false,
            allowGpuVisibility: false,
            allowUnselectedOverlay: true,
            allowMeshRebuild: false,
            allowSelectionSync: false
        );

        /// <summary>
        /// カメラドラッグ中: 全スキップ
        /// シェーダーがカメラ行列で変換するため再計算不要。
        /// </summary>
        public static UpdateModeProfile CameraDragging => new UpdateModeProfile(
            allowHitTest: false,
            allowVertexFlagsReadback: false,
            allowGpuVisibility: false,
            allowUnselectedOverlay: false,
            allowMeshRebuild: false,
            allowSelectionSync: false
        );

        /// <summary>
        /// 頂点ドラッグ中: 位置更新のみ、重い処理はスキップ
        /// 
        /// ★★★ 禁忌（絶対厳守） ★★★
        /// AllowMeshRebuild, AllowHitTest, AllowGpuVisibility, AllowSelectionSync を
        /// true にしてはならない。ドラッグ中に毎フレーム走ると1FPS以下に落ちる。
        /// 
        /// ドラッグ中の表示更新が必要な場合:
        /// - トポロジ変更を伴わない表示専用入口を使用すること
        ///   → UnifiedMeshSystem.ProcessTransformUpdate()
        ///     （_bufferManager.UpdatePositions: Array.Copy + SetData のみ）
        /// - ホバーチェック無効化はこのプロファイルのAllowHitTest=falseで制御済み
        /// - AllowMeshRebuild=true のプロファイルでRequestNormalを通してはならない
        /// 
        /// 過去の障害:
        /// - AllowMeshRebuild=true → 1FPS（ワイヤー全再構築が毎ドラッグフレーム実行）
        /// ★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        public static UpdateModeProfile TransformDragging => new UpdateModeProfile(
            allowHitTest: false,           // ★禁忌: trueにしてはならない
            allowVertexFlagsReadback: false,
            allowGpuVisibility: false,     // ★禁忌: trueにしてはならない
            allowUnselectedOverlay: false,
            allowMeshRebuild: false,       // ★禁忌: trueにしてはならない（1FPS障害の原因）
            allowSelectionSync: false      // ★禁忌: trueにしてはならない
        );

        /// <summary>
        /// 選択変更: 選択フラグとメッシュ再構築のみ
        /// （Phase 4で使用開始）
        /// </summary>
        public static UpdateModeProfile SelectionChange => new UpdateModeProfile(
            allowHitTest: false,
            allowVertexFlagsReadback: true,
            allowGpuVisibility: false,
            allowUnselectedOverlay: true,
            allowMeshRebuild: true,
            allowSelectionSync: true
        );

        /// <summary>
        /// アクティブメッシュ変更: 選択 + Visibility + メッシュ再構築
        /// （Phase 3で使用開始）
        /// </summary>
        public static UpdateModeProfile ActiveMeshChange => new UpdateModeProfile(
            allowHitTest: false,
            allowVertexFlagsReadback: true,
            allowGpuVisibility: true,
            allowUnselectedOverlay: true,
            allowMeshRebuild: true,
            allowSelectionSync: true
        );

        // ============================================================
        // UpdateMode → Profile 変換
        // ============================================================

        /// <summary>
        /// UpdateModeから対応するプロファイルを取得
        /// </summary>
        public static UpdateModeProfile FromMode(UpdateMode mode)
        {
            return mode switch
            {
                UpdateMode.Normal => Normal,
                UpdateMode.Idle => Idle,
                UpdateMode.CameraDragging => CameraDragging,
                UpdateMode.TransformDragging => TransformDragging,
                UpdateMode.Selection => SelectionChange,
                UpdateMode.ActiveMesh => ActiveMeshChange,
                _ => Normal,
            };
        }
    }
}
