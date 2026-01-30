// Assets/Editor/SimpleMeshFactory_Symmetry.cs
// 対称モード関連（UI、ミラー描画）
// Phase2: SymmetryMeshCache統合 - 正しい面反転でミラー表示

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;
using Poly_Ling.Localization;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

public partial class PolyLing
{
    // ================================================================
    // フィールド
    // ================================================================

    // 後方互換プロパティ（ModelContextに移行）
    private SymmetrySettings _symmetrySettings => _model?.SymmetrySettings;

    // ミラー描画用マテリアル
    private Material _mirrorMaterial;

    // ミラーメッシュキャッシュ（Phase2追加）
    // MeshUndoContext.SymmetryCacheを使用（各MeshContextが自身のキャッシュを持つ）

    // UI状態
    private bool _foldSymmetry = true;

    // ================================================================
    // 対称設定プロパティ
    // ================================================================

    /// <summary>
    /// 対称設定へのアクセス
    /// </summary>
    public SymmetrySettings SymmetrySettings => _model.SymmetrySettings;

    // ================================================================
    // 初期化・クリーンアップ
    // ================================================================

    /// <summary>
    /// 対称キャッシュを初期化（MeshUndoContext.SymmetryCacheで遅延初期化されるため不要）
    /// </summary>
    private void InitializeSymmetryCache()
    {
        // MeshUndoContext.SymmetryCacheプロパティで遅延初期化されるため何もしない
    }

    /// <summary>
    /// 対称キャッシュを無効化（トポロジー変更時に呼び出す）
    /// </summary>
    public void InvalidateSymmetryCache()
    {
        _model?.CurrentMeshContext?.InvalidateSymmetryCache();
    }

    /// <summary>
    /// 全メッシュの対称キャッシュを無効化
    /// </summary>
    public void InvalidateAllSymmetryCaches()
    {
        if (_meshContextList == null) return;
        foreach (var ctx in _meshContextList)
        {
            ctx?.InvalidateSymmetryCache();
        }
    }

    // ================================================================
    // UI描画
    // ================================================================

    /// <summary>
    /// 対称モードUIを描画（Displayセクション内で呼び出し）
    /// </summary>
    private void DrawSymmetryUI()
    {
        _foldSymmetry = DrawFoldoutWithUndo("Symmetry", L.Get("Symmetry"), true);
        if (!_foldSymmetry) return;
        if (_symmetrySettings == null) return;  // Phase 1: null チェック

        EditorGUI.indentLevel++;

        // 有効/無効トグル
        EditorGUI.BeginChangeCheck();
        bool newEnabled = EditorGUILayout.Toggle(L.Get("EnableMirror"), _symmetrySettings.IsEnabled);
        if (EditorGUI.EndChangeCheck())
        {
            _symmetrySettings.IsEnabled = newEnabled;
            InvalidateSymmetryCache();
            ApplySymmetryToUnifiedSystem();
            Repaint();
        }

        // 有効時のみ詳細設定を表示
        if (_symmetrySettings.IsEnabled)
        {
            EditorGUILayout.Space(2);

            // 軸選択
            EditorGUI.BeginChangeCheck();
            var newAxis = (SymmetryAxis)EditorGUILayout.EnumPopup(L.Get("Axis"), _symmetrySettings.Axis);
            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.Axis = newAxis;
                InvalidateSymmetryCache();
                ApplySymmetryToUnifiedSystem();
                Repaint();
            }

            // 平面オフセット
            EditorGUI.BeginChangeCheck();
            float newOffset = EditorGUILayout.Slider(L.Get("PlaneOffset"), _symmetrySettings.PlaneOffset, -1f, 1f);//スライダーの上限下限
            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.PlaneOffset = newOffset;
                InvalidateSymmetryCache();
                ApplySymmetryToUnifiedSystem();
                Repaint();
            }

            // オフセットリセットボタン
            if (Mathf.Abs(_symmetrySettings.PlaneOffset) > 0.001f)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L.Get("ResetOffset"), EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    _symmetrySettings.PlaneOffset = 0f;
                    InvalidateSymmetryCache();
                    ApplySymmetryToUnifiedSystem();
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(3);

            // 表示オプション
            EditorGUILayout.LabelField(L.Get("DisplayOptions"), EditorStyles.miniLabel);

            EditorGUI.BeginChangeCheck();
            bool showMesh = EditorGUILayout.Toggle(L.Get("MirrorMesh"), _symmetrySettings.ShowMirrorMesh);
            bool showWire = EditorGUILayout.Toggle(L.Get("MirrorWireframe"), _symmetrySettings.ShowMirrorWireframe);
            bool showPlane = EditorGUILayout.Toggle(L.Get("SymmetryPlane"), _symmetrySettings.ShowSymmetryPlane);
            float alpha = EditorGUILayout.Slider(L.Get("MirrorAlpha"), _symmetrySettings.MirrorAlpha, 0.1f, 1f);//スライダーの上限下限

            if (EditorGUI.EndChangeCheck())
            {
                _symmetrySettings.ShowMirrorMesh = showMesh;
                _symmetrySettings.ShowMirrorWireframe = showWire;
                _symmetrySettings.ShowSymmetryPlane = showPlane;
                _symmetrySettings.MirrorAlpha = alpha;
                UpdateMirrorMaterialAlpha();
                Repaint();
            }
        }

        EditorGUI.indentLevel--;
    }

    // ================================================================
    // ミラーメッシュ描画（Phase2: SymmetryMeshCache使用）
    // ================================================================

    /// <summary>
    /// メッシュコンテキストがミラー表示対象かどうか判定
    /// MeshContextにIsMirroredフラグがあれば表示対象
    /// </summary>
    private bool ShouldDrawMirror(MeshContext meshContext)
    {
        if (meshContext == null) return false;

        // MeshContextにミラーフラグがないメッシュは対象外
        if (!meshContext.IsMirrored)
            return false;

        // グローバル設定で「ミラーメッシュ」をOFFにしている場合は表示しない
        // ただし、グローバルミラーが無効でもMeshContextごとのミラーは表示する
        if (_symmetrySettings != null && !_symmetrySettings.ShowMirrorMesh)
            return false;

        // BakedMirrorの子が存在する場合はSymmetryCacheを使わない（二重表示防止）
        if (HasBakedMirrorChild(meshContext))
            return false;

        return true;
    }

    /// <summary>
    /// 指定されたMeshContextをソースとするBakedMirrorが存在するか確認
    /// MeshContext.HasBakedMirrorChildプロパティを参照
    /// </summary>
    private bool HasBakedMirrorChild(MeshContext meshContext)
    {
        return meshContext?.HasBakedMirrorChild ?? false;
    }

    /// <summary>
    /// ミラーワイヤーフレーム表示対象かどうか判定
    /// MeshContextにIsMirroredフラグがあれば表示対象
    /// </summary>
    private bool ShouldDrawMirrorWireframe(MeshContext meshContext)
    {
        if (meshContext == null) return false;

        // MeshContextにミラーフラグがないメッシュは対象外
        if (!meshContext.IsMirrored)
            return false;

        // グローバル設定で「ミラーワイヤーフレーム」をOFFにしている場合は表示しない
        // ただし、グローバルミラーが無効でもMeshContextごとのミラーは表示する
        if (_symmetrySettings != null && !_symmetrySettings.ShowMirrorWireframe)
            return false;

        // BakedMirrorの子が存在する場合はSymmetryCacheを使わない（二重表示防止）
        if (HasBakedMirrorChild(meshContext))
            return false;

        return true;
    }

    /// <summary>
    /// 対称平面表示対象かどうか判定
    /// </summary>
    private bool ShouldDrawSymmetryPlane()
    {
        // グローバル「ミラー有効」がOFFなら表示しない
        if (_symmetrySettings == null || !_symmetrySettings.IsEnabled)
            return false;

        // 「対称平面」がOFFなら表示しない
        if (!_symmetrySettings.ShowSymmetryPlane)
            return false;

        return true;
    }

    /// <summary>
    /// MeshContextに適用する対称設定を取得
    /// MeshContextごとの設定があればそれを優先、なければグローバル設定
    /// </summary>
    private SymmetrySettings GetEffectiveSymmetrySettings(MeshContext meshContext)
    {
        if (meshContext == null) return _symmetrySettings;

        // MeshContextにミラー設定がある場合は一時的なSymmetrySettingsを作成
        if (meshContext.IsMirrored)
        {
            var settings = new SymmetrySettings
            {
                IsEnabled = true,
                Axis = meshContext.GetMirrorSymmetryAxis(),
                PlaneOffset = meshContext.MirrorDistance,
                ShowMirrorMesh = true,
                ShowMirrorWireframe = _symmetrySettings?.ShowMirrorWireframe ?? true,
                ShowSymmetryPlane = false,  // MeshContextごとの場合は平面表示しない
                MirrorAlpha = _symmetrySettings?.MirrorAlpha ?? 0.5f
            };
            return settings;
        }

        // グローバル設定を使用
        return _symmetrySettings;
    }

    /// <summary>
    /// ミラーメッシュを描画（正しい面反転付き）
    /// </summary>
    private void DrawMirroredMesh(MeshContext meshContext, Mesh mesh)
    {
        // デバッグ：詳細情報
        var dbgEditorState = _undoController?.EditorState;
        bool dbgUseWorld = dbgEditorState?.ShowWorldTransform ?? false;
        var dbgBufMgr = _unifiedAdapter?.BufferManager;
        bool dbgMirrorEnabled = dbgBufMgr?.MirrorEnabled ?? false;
        var dbgMirrorPos = dbgBufMgr?.GetMirrorPositions();

        Debug.Log($"[MIRROR] useWorld={dbgUseWorld}, mirrorEnabled={dbgMirrorEnabled}, mirrorPos={dbgMirrorPos != null}, count={dbgMirrorPos?.Length ?? 0}");

        // ミラー表示対象かチェック（グローバル設定 OR MeshContextごとの設定）
        if (!ShouldDrawMirror(meshContext))
            return;
        if (meshContext?.MeshObject == null)
            return;

        // 有効な対称設定を取得
        var effectiveSettings = GetEffectiveSymmetrySettings(meshContext);
        if (effectiveSettings == null)
            return;

        // MeshContextのキャッシュを更新（必要な場合のみ再構築）
        meshContext.SymmetryCache.Update(meshContext.MeshObject, effectiveSettings);

        // スキニング有効時はGPU計算済みミラー座標で位置を更新
        var editorState = _undoController?.EditorState;
        bool useWorldTransform = editorState?.ShowWorldTransform ?? false;

        // デバッグ（1フレームに1回だけ）
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[MirrorSkinning] useWorldTransform={useWorldTransform}, adapter={_unifiedAdapter != null}, bufferMgr={_unifiedAdapter?.BufferManager != null}");
        }

        if (useWorldTransform && _unifiedAdapter?.BufferManager != null)
        {
            var bufferManager = _unifiedAdapter.BufferManager;
            var mirrorPositions = bufferManager.GetMirrorPositions();

            // デバッグ
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[MirrorSkinning] mirrorPositions={mirrorPositions != null}, count={mirrorPositions?.Length ?? 0}, mirrorEnabled={bufferManager.MirrorEnabled}");
            }

            if (mirrorPositions != null)
            {
                // MeshContextのインデックスを取得
                int contextIndex = _meshContextList?.IndexOf(meshContext) ?? -1;

                // デバッグ
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[MirrorSkinning] contextIndex={contextIndex}, meshContextListCount={_meshContextList?.Count ?? 0}");
                }

                if (contextIndex >= 0)
                {
                    int unifiedMeshIndex = bufferManager.ContextToUnifiedMeshIndex(contextIndex);

                    // デバッグ
                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"[MirrorSkinning] unifiedMeshIndex={unifiedMeshIndex}");
                    }

                    if (unifiedMeshIndex >= 0)
                    {
                        var meshInfos = bufferManager.MeshInfos;
                        if (meshInfos != null && unifiedMeshIndex < meshInfos.Length)
                        {
                            int vertexOffset = (int)meshInfos[unifiedMeshIndex].VertexStart;

                            // デバッグ
                            if (Time.frameCount % 60 == 0)
                            {
                                Debug.Log($"[MirrorSkinning] Calling UpdateWithSkinnedPositions: vertexOffset={vertexOffset}, vertexCount={meshContext.MeshObject?.VertexCount ?? 0}");
                            }

                            meshContext.SymmetryCache.UpdateWithSkinnedPositions(
                                mirrorPositions, meshContext.MeshObject, vertexOffset, effectiveSettings);
                        }
                    }
                }
            }
        }

        Mesh mirrorMesh = meshContext.SymmetryCache.MirrorMesh;
        if (mirrorMesh == null || mirrorMesh.vertexCount == 0)
            return;

        // マテリアルを準備
        Material mirrorMat = GetMirrorMaterial();

        int subMeshCount = mirrorMesh.subMeshCount;
        for (int i = 0; i < subMeshCount; i++)
        {
            Material baseMat = null;
            if (_model.MaterialCount > 0 && i < _model.MaterialCount)
            {
                baseMat = _model.GetMaterial(i);
            }
            Material mat = (baseMat != null) ? baseMat : mirrorMat;

            // 反転済みメッシュを単位行列で描画（頂点は既にミラー位置）
            Graphics.DrawMesh(mirrorMesh, Matrix4x4.identity, mat, 0, _preview.camera, i);
        }
    }

    /// <summary>
    /// 頂点位置のみ更新（移動操作中の軽量更新）
    /// </summary>
    public void UpdateSymmetryPositionsOnly()
    {
        if (_symmetrySettings == null || !_symmetrySettings.IsEnabled)
            return;

        var meshContext = _model?.CurrentMeshContext;
        if (meshContext?.MeshObject == null)
            return;

        // 有効な対称設定を取得
        var effectiveSettings = GetEffectiveSymmetrySettings(meshContext);
        if (effectiveSettings == null)
            return;

        meshContext.SymmetryCache.UpdatePositionsOnly(meshContext.MeshObject, effectiveSettings);
    }

    /// <summary>
    /// ミラー用マテリアルを取得
    /// </summary>
    private Material GetMirrorMaterial()
    {
        if (_mirrorMaterial != null)
            return _mirrorMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _mirrorMaterial = new Material(shader);
            // 片面描画（正しい法線方向なのでカリング有効）
            _mirrorMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
            UpdateMirrorMaterialAlpha();
        }
        return _mirrorMaterial;
    }

    /// <summary>
    /// ミラーマテリアルの透明度を更新
    /// </summary>
    private void UpdateMirrorMaterialAlpha()
    {
        if (_mirrorMaterial == null || _symmetrySettings == null) return;

        float alpha = _symmetrySettings.MirrorAlpha;
        Color col = new Color(0.6f, 0.6f, 0.7f, alpha);

        _mirrorMaterial.SetColor("_BaseColor", col);
        _mirrorMaterial.SetColor("_Color", col);

        // 透明度が1未満の場合は透明設定
        if (alpha < 0.99f)
        {
            _mirrorMaterial.SetFloat("_Surface", 1); // Transparent
            _mirrorMaterial.SetFloat("_Blend", 0);   // Alpha
            _mirrorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mirrorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            // ZWriteを有効にしてミラーワイヤフレームのZTestを機能させる
            _mirrorMaterial.SetInt("_ZWrite", 1);
            _mirrorMaterial.renderQueue = 2450;  // Geometry（2000）より後、Transparent（3000）より前
        }
        else
        {
            // 不透明の場合
            _mirrorMaterial.SetFloat("_Surface", 0); // Opaque
            _mirrorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _mirrorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _mirrorMaterial.SetInt("_ZWrite", 1);
            _mirrorMaterial.renderQueue = -1;
        }
    }

    // ================================================================
    // ミラーワイヤーフレーム描画
    // ================================================================

    /// <summary>
    /// ミラーワイヤーフレームを描画
    /// </summary>
    private void DrawMirroredWireframe(Rect previewRect, MeshContext meshContext, Vector3 camPos, Vector3 lookAt, int meshIndex = -1)
    {
        // ミラーワイヤーフレーム表示対象かチェック
        if (!ShouldDrawMirrorWireframe(meshContext))
            return;

        var meshObject = meshContext?.MeshObject;
        if (meshObject == null)
            return;

        // MeshContextのミラー設定を使用
        var effectiveSettings = GetEffectiveSymmetrySettings(meshContext);
        if (effectiveSettings == null)
            return;

        Matrix4x4 mirrorMatrix = effectiveSettings.GetMirrorMatrix();

        // 表示用トランスフォーム行列を取得し、ミラー行列と合成
        Matrix4x4 displayMatrix = GetDisplayMatrix(meshIndex);
        Matrix4x4 combinedMatrix = displayMatrix * mirrorMatrix;

        float alpha = effectiveSettings.MirrorAlpha * 0.7f;  // ワイヤーフレームはやや薄く

        var edges = new HashSet<(int, int)>();
        var lines = new List<(int, int)>();

        // 各面からエッジを抽出
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount == 2)
            {
                lines.Add((face.VertexIndices[0], face.VertexIndices[1]));
            }
            else if (face.VertexCount >= 3)
            {
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % face.VertexCount];
                    AddEdge(edges, a, b);
                }
            }
        }

        UnityEditor_Handles.BeginGUI();

        // 通常のエッジを描画（シアン、やや薄く）
        UnityEditor_Handles.color = new Color(0f, 0.8f, 0.8f, alpha);
        foreach (var edge in edges)
        {
            Vector3 p1World = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item1].Position);
            Vector3 p2World = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        // 補助線を描画（ピンク、やや薄く）
        UnityEditor_Handles.color = new Color(1f, 0.5f, 0.8f, alpha);
        foreach (var line in lines)
        {
            if (line.Item1 < 0 || line.Item1 >= meshObject.VertexCount ||
                line.Item2 < 0 || line.Item2 >= meshObject.VertexCount)
                continue;

            Vector3 p1World = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[line.Item1].Position);
            Vector3 p2World = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[line.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawAAPolyLine(3f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        UnityEditor_Handles.EndGUI();
    }

    /// <summary>
    /// 旧シグネチャ（後方互換用）- MeshObjectのみ渡された場合
    /// </summary>
    private void DrawMirroredWireframe(Rect previewRect, MeshObject meshObject, Vector3 camPos, Vector3 lookAt)
    {
        // 旧コードからの呼び出し - グローバル設定でフィルター
        if (_symmetrySettings == null || !_symmetrySettings.IsEnabled || !_symmetrySettings.ShowMirrorWireframe)
            return;

        if (meshObject == null)
            return;

        Matrix4x4 mirrorMatrix = _symmetrySettings.GetMirrorMatrix();
        float alpha = _symmetrySettings.MirrorAlpha * 0.7f;

        var edges = new HashSet<(int, int)>();
        var lines = new List<(int, int)>();

        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount == 2)
            {
                lines.Add((face.VertexIndices[0], face.VertexIndices[1]));
            }
            else if (face.VertexCount >= 3)
            {
                for (int i = 0; i < face.VertexCount; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % face.VertexCount];
                    AddEdge(edges, a, b);
                }
            }
        }

        UnityEditor_Handles.BeginGUI();

        UnityEditor_Handles.color = new Color(0f, 0.8f, 0.8f, alpha);
        foreach (var edge in edges)
        {
            Vector3 p1World = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item1].Position);
            Vector3 p2World = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[edge.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        UnityEditor_Handles.color = new Color(1f, 0.5f, 0.8f, alpha);
        foreach (var line in lines)
        {
            if (line.Item1 < 0 || line.Item1 >= meshObject.VertexCount ||
                line.Item2 < 0 || line.Item2 >= meshObject.VertexCount)
                continue;

            Vector3 p1World = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[line.Item1].Position);
            Vector3 p2World = mirrorMatrix.MultiplyPoint3x4(meshObject.Vertices[line.Item2].Position);

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                UnityEditor_Handles.DrawAAPolyLine(3f,
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // 対称平面描画
    // ================================================================

    /// <summary>
    /// 対称平面を描画
    /// </summary>
    private void DrawSymmetryPlane(Rect previewRect, Vector3 camPos, Vector3 lookAt, Bounds meshBounds)
    {
        // グローバル設定でフィルター
        if (!ShouldDrawSymmetryPlane())
            return;

        Vector3 normal = _symmetrySettings.GetPlaneNormal();
        Vector3 planePoint = _symmetrySettings.GetPlanePoint();
        Color planeColor = _symmetrySettings.GetAxisColor();

        // 平面のサイズをメッシュバウンドに合わせる
        float planeSize = Mathf.Max(meshBounds.size.magnitude, 0.5f) * 0.6f;

        // 平面上の2つの軸を計算
        Vector3 axis1, axis2;
        switch (_symmetrySettings.Axis)
        {
            case SymmetryAxis.X:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Y:
                axis1 = Vector3.right;
                axis2 = Vector3.forward;
                break;
            case SymmetryAxis.Z:
                axis1 = Vector3.right;
                axis2 = Vector3.up;
                break;
            default:
                axis1 = Vector3.up;
                axis2 = Vector3.forward;
                break;
        }

        UnityEditor_Handles.BeginGUI();

        // 平面の四隅
        Vector3[] corners = new Vector3[4];
        corners[0] = planePoint + (-axis1 - axis2) * planeSize * 0.5f;
        corners[1] = planePoint + (axis1 - axis2) * planeSize * 0.5f;
        corners[2] = planePoint + (axis1 + axis2) * planeSize * 0.5f;
        corners[3] = planePoint + (-axis1 + axis2) * planeSize * 0.5f;

        Vector2[] screenCorners = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            screenCorners[i] = WorldToPreviewPos(corners[i], previewRect, camPos, lookAt);
        }

        // 半透明の平面を描画
        Color fillColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.15f);
        DrawFilledPolygon(screenCorners, fillColor);

        // 枠線を描画
        UnityEditor_Handles.color = new Color(planeColor.r, planeColor.g, planeColor.b, 0.6f);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            UnityEditor_Handles.DrawAAPolyLine(2f,
                new Vector3(screenCorners[i].x, screenCorners[i].y, 0),
                new Vector3(screenCorners[next].x, screenCorners[next].y, 0));
        }

        // 中心線（対称軸）を点線で描画
        Vector2 center = WorldToPreviewPos(planePoint, previewRect, camPos, lookAt);
        Vector2 axis1End = WorldToPreviewPos(planePoint + axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis1Start = WorldToPreviewPos(planePoint - axis1 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2End = WorldToPreviewPos(planePoint + axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);
        Vector2 axis2Start = WorldToPreviewPos(planePoint - axis2 * planeSize * 0.4f, previewRect, camPos, lookAt);

        Color lineColor = new Color(planeColor.r, planeColor.g, planeColor.b, 0.5f);
        DrawDottedLine(axis1Start, axis1End, lineColor);
        DrawDottedLine(axis2Start, axis2End, lineColor);

        // 中心マーカー
        if (previewRect.Contains(center))
        {
            float markerSize = 6f;
            UnityEditor_Handles.DrawRect(new Rect(
                center.x - markerSize / 2,
                center.y - markerSize / 2,
                markerSize,
                markerSize), planeColor);
        }

        UnityEditor_Handles.EndGUI();
    }

    // ================================================================
    // クリーンアップ
    // ================================================================

    /// <summary>
    /// ミラー関連リソースをクリーンアップ
    /// </summary>
    private void CleanupMirrorResources()
    {
        if (_mirrorMaterial != null)
        {
            DestroyImmediate(_mirrorMaterial);
            _mirrorMaterial = null;
        }

        // 全MeshContextのキャッシュをクリア
        if (_meshContextList != null)
        {
            foreach (var ctx in _meshContextList)
            {
                ctx?.ClearSymmetryCache();
            }
        }
    }
}