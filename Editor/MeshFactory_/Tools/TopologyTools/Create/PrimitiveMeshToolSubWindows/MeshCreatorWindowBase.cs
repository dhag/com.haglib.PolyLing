// Assets/Editor/MeshFactory/Tools/Creators/MeshCreatorWindowBase.cs
// メッシュ生成ウィンドウの基底クラス
// 共通処理（プレビュー、Undo、AutoMerge、ボタン）を集約
// ローカライズ対応版

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Utilities;
using static MeshFactory.Tools.Creators.MeshCreatorTexts;

namespace MeshFactory.Tools.Creators
{
    /// <summary>
    /// メッシュ生成ウィンドウの基底クラス
    /// </summary>
    /// <typeparam name="TParams">パラメータ構造体の型</typeparam>
    public abstract class MeshCreatorWindowBase<TParams> : EditorWindow
        where TParams : struct, IEquatable<TParams>
    {
        // ================================================================
        // フィールド
        // ================================================================

        /// <summary>プレビュー用ユーティリティ</summary>
        protected PreviewRenderUtility _preview;

        /// <summary>プレビュー用メッシュ</summary>
        protected Mesh _previewMesh;

        /// <summary>プレビュー用MeshObject</summary>
        protected MeshObject _previewMeshObject;

        /// <summary>プレビュー用マテリアル</summary>
        protected Material _previewMaterial;

        /// <summary>メッシュ生成完了時のコールバック</summary>
        protected Action<MeshObject, string> _onMeshObjectCreated;

        /// <summary>スクロール位置</summary>
        protected Vector2 _scrollPos;

        /// <summary>パラメータUndo管理</summary>
        protected ParameterUndoHelper<TParams> _undoHelper;

        /// <summary>現在のパラメータ</summary>
        protected TParams _params;

        // === AutoMerge設定 ===
        /// <summary>自動頂点結合を有効にするか</summary>
        protected bool _autoMergeOnCreate = true;

        /// <summary>自動頂点結合のしきい値</summary>
        protected float _autoMergeThreshold = 0.001f;

        // ================================================================
        // 抽象メソッド（派生クラスで実装）
        // ================================================================

        /// <summary>ウィンドウ識別名</summary>
        protected abstract string WindowName { get; }

        /// <summary>Undoの説明文字列</summary>
        protected abstract string UndoDescription { get; }

        /// <summary>デフォルトパラメータを取得</summary>
        protected abstract TParams GetDefaultParams();

        /// <summary>MeshObjectを生成</summary>
        protected abstract MeshObject GenerateMeshObject();

        /// <summary>パラメータUIを描画</summary>
        protected abstract void DrawParametersUI();

        /// <summary>メッシュ名を取得（パラメータから）</summary>
        protected abstract string GetMeshName();

        // ================================================================
        // 仮想メソッド（必要に応じてオーバーライド）
        // ================================================================

        /// <summary>プレビュー回転X初期値</summary>
        protected virtual float DefaultRotationX => 20f;

        /// <summary>プレビュー回転Y初期値</summary>
        protected virtual float DefaultRotationY => 30f;

        /// <summary>プレビューのカメラ距離</summary>
        protected virtual float PreviewCameraDistance => 3f;

        /// <summary>プレビューのFOV</summary>
        protected virtual float PreviewFieldOfView => 30f;

        /// <summary>追加の初期化処理</summary>
        protected virtual void OnInitialize() { }

        /// <summary>追加のクリーンアップ処理</summary>
        protected virtual void OnCleanup() { }

        /// <summary>パラメータ変更時の追加処理</summary>
        protected virtual void OnParamsChanged() { }

        // ================================================================
        // ウィンドウ管理
        // ================================================================

        protected virtual void OnEnable()
        {
            InitPreview();
            InitUndo();
            OnInitialize();
            UpdatePreviewMesh();
        }

        protected virtual void OnDisable()
        {
            OnCleanup();
            CleanupPreview();
            _undoHelper?.Dispose();
        }

        /// <summary>
        /// Undoシステムを初期化
        /// </summary>
        protected void InitUndo()
        {
            _params = GetDefaultParams();
            _undoHelper = new ParameterUndoHelper<TParams>(
                WindowName,
                UndoDescription,
                () => _params,
                (p) => { _params = p; OnParamsChanged(); UpdatePreviewMesh(); },
                () => Repaint()
            );
        }

        /// <summary>
        /// プレビューを初期化
        /// </summary>
        protected void InitPreview()
        {
            _preview = new PreviewRenderUtility();
            _preview.cameraFieldOfView = PreviewFieldOfView;
            _preview.camera.nearClipPlane = 0.01f;
            _preview.camera.farClipPlane = 100f;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard")
                         ?? Shader.Find("Unlit/Color");

            if (shader != null)
            {
                _previewMaterial = new Material(shader);
                _previewMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
                _previewMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            }
        }

        /// <summary>
        /// プレビューをクリーンアップ
        /// </summary>
        protected void CleanupPreview()
        {
            _preview?.Cleanup();
            _preview = null;
            if (_previewMesh != null) DestroyImmediate(_previewMesh);
            if (_previewMaterial != null) DestroyImmediate(_previewMaterial);
        }

        // ================================================================
        // GUI
        // ================================================================

        protected virtual void OnGUI()
        {
            _undoHelper?.HandleGUIEvents(Event.current);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(10);

            // パラメータUI（派生クラスで実装）
            DrawParametersUI();

            EditorGUILayout.Space(10);

            // AutoMerge設定
            DrawAutoMergeUI();

            EditorGUILayout.Space(10);

            // プレビュー
            DrawPreview();

            EditorGUILayout.Space(10);
            EditorGUILayout.EndScrollView();

            // ボタン
            DrawButtons();
        }

        /// <summary>
        /// AutoMerge設定UIを描画
        /// </summary>
        protected virtual void DrawAutoMergeUI()
        {
            EditorGUILayout.LabelField(T("Options"), EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            _autoMergeOnCreate = EditorGUILayout.ToggleLeft(T("AutoMergeVertices"), _autoMergeOnCreate, GUILayout.Width(140));

            EditorGUI.BeginDisabledGroup(!_autoMergeOnCreate);
            EditorGUILayout.LabelField(T("Threshold"), GUILayout.Width(60));
            _autoMergeThreshold = EditorGUILayout.FloatField(_autoMergeThreshold, GUILayout.Width(60));
            _autoMergeThreshold = Mathf.Max(0.0001f, _autoMergeThreshold);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// プレビューを描画
        /// 【重要】GUILayoutコントロールは常に実行し、描画のみを条件分岐
        /// </summary>
        protected virtual void DrawPreview()
        {
            // GUILayoutコントロールは常に実行（Layout/Repaint整合性のため）
            EditorGUILayout.LabelField(T("Preview"), EditorStyles.boldLabel);

            Rect previewRect = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true));

            // メッシュ情報（常に実行）
            if (_previewMeshObject != null)
            {
                EditorGUILayout.LabelField(
                    T("VertsFaces", _previewMeshObject.VertexCount, _previewMeshObject.FaceCount),
                    EditorStyles.miniLabel);
            }
            else
            {
                // プレースホルダー（コントロール数を一定に保つ）
                EditorGUILayout.LabelField(" ", EditorStyles.miniLabel);
            }

            // 描画処理はRepaintイベントのみ、かつリソースが有効な場合のみ
            if (Event.current.type != EventType.Repaint) return;
            if (_preview == null || _previewMesh == null) return;
            if (previewRect.width < 10 || previewRect.height < 10) return;

            // プレビュー回転値を取得
            float rotX = GetPreviewRotationX();
            float rotY = GetPreviewRotationY();

            _preview.BeginPreview(previewRect, GUIStyle.none);

            // カメラ設定
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

            Quaternion rot = Quaternion.Euler(rotX, rotY, 0);
            Vector3 camPos = rot * new Vector3(0, 0, -PreviewCameraDistance);
            _preview.camera.transform.position = camPos;
            _preview.camera.transform.LookAt(Vector3.zero);

            // ライト設定
            var lights = _preview.lights;
            if (lights != null && lights.Length > 0)
            {
                lights[0].transform.rotation = Quaternion.Euler(30, 30, 0);
                lights[0].intensity = 1f;
            }

            // メッシュ描画
            if (_previewMaterial != null)
            {
                _preview.DrawMesh(_previewMesh, Vector3.zero, Quaternion.identity, _previewMaterial, 0);
            }

            _preview.Render();
            GUI.DrawTexture(previewRect, _preview.EndPreview());
        }

        /// <summary>
        /// プレビュー回転X値を取得（派生クラスでオーバーライド）
        /// </summary>
        protected virtual float GetPreviewRotationX() => DefaultRotationX;

        /// <summary>
        /// プレビュー回転Y値を取得（派生クラスでオーバーライド）
        /// </summary>
        protected virtual float GetPreviewRotationY() => DefaultRotationY;

        /// <summary>
        /// ボタンを描画
        /// </summary>
        protected virtual void DrawButtons()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(T("Create"), GUILayout.Width(100), GUILayout.Height(30)))
            {
                CreateMesh();
            }

            if (GUILayout.Button(T("Cancel"), GUILayout.Width(80), GUILayout.Height(30)))
            {
                Close();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================
        // メッシュ生成
        // ================================================================

        /// <summary>
        /// プレビューメッシュを更新
        /// </summary>
        protected virtual void UpdatePreviewMesh()
        {
            _previewMeshObject = GenerateMeshObject();
            if (_previewMeshObject == null) return;

            if (_previewMesh != null) DestroyImmediate(_previewMesh);
            _previewMesh = _previewMeshObject.ToUnityMesh();
            _previewMesh.name = "Preview";
            _previewMesh.hideFlags = HideFlags.HideAndDontSave;

            Repaint();
        }

        /// <summary>
        /// メッシュを生成してコールバックを呼ぶ
        /// </summary>
        protected virtual void CreateMesh()
        {
            var meshObject = GenerateMeshObject();
            if (meshObject == null)
            {
                Debug.LogWarning($"[{WindowName}] {T("FailedToGenerate")}");
                return;
            }

            string meshName = GetMeshName();

            // AutoMerge適用
            if (_autoMergeOnCreate && meshObject.VertexCount >= 2)
            {
                var result = MeshMergeHelper.MergeAllVerticesAtSamePosition(meshObject, _autoMergeThreshold);
                if (result.RemovedVertexCount > 0)
                {
                    Debug.Log($"[{WindowName}] {T("AutoMergedVertices", result.RemovedVertexCount)}");
                }
            }

            // コールバック呼び出し
            _onMeshObjectCreated?.Invoke(meshObject, meshName);

            Debug.Log($"[{WindowName}] {T("CreatedMesh", meshName, meshObject.VertexCount, meshObject.FaceCount)}");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// Undo/Redoボタンを描画
        /// </summary>
        protected void DrawUndoRedoButtons()
        {
            _undoHelper?.DrawUndoRedoButtons();
        }

        /// <summary>
        /// パラメータ変更を開始（EditorGUI.BeginChangeCheck()の前に呼ぶ）
        /// </summary>
        protected void BeginParamChange()
        {
            EditorGUI.BeginChangeCheck();
        }

        /// <summary>
        /// パラメータ変更を終了（EditorGUI.EndChangeCheck()の代わりに呼ぶ）
        /// </summary>
        /// <returns>変更があったか</returns>
        protected bool EndParamChange()
        {
            if (EditorGUI.EndChangeCheck())
            {
                UpdatePreviewMesh();
                return true;
            }
            return false;
        }
    }
}
