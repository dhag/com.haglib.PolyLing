// Assets/Editor/Poly_Ling_Main/Tools/TransformTools/SkinWeightPaintTool_/SkinWeightPaintTool.cs
// スキンウェイトペイントツール（IEditTool実装）
// ブラシでドラッグしてスキンウェイトをペイントする

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.UI;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// スキンウェイトペイントツール
    /// </summary>
    public class SkinWeightPaintTool : IEditTool
    {
        public string Name => "SkinWeightPaint";
        public string DisplayName => "Skin Weight Paint";
        public string GetLocalizedDisplayName() => "スキンウェイトペイント";

        // ================================================================
        // 設定
        // ================================================================

        private SkinWeightPaintSettings _settings = new SkinWeightPaintSettings();
        public IToolSettings Settings => _settings;

        // ================================================================
        // パネル連携（static参照）
        // ================================================================

        /// <summary>
        /// アクティブなSkinWeightPaintPanel（パネル側から設定される）
        /// パネルが開いていない場合はnull → ツール内の_settingsを使用
        /// </summary>
        public static SkinWeightPaintPanel ActivePanel { get; set; }

        // パネルから設定を読む（パネルがなければ自分の設定を使う）
        private SkinWeightPaintMode PaintMode => ActivePanel?.CurrentPaintMode ?? _settings.PaintMode;
        private float BrushRadius => ActivePanel?.CurrentBrushRadius ?? _settings.BrushRadius;
        private float Strength => ActivePanel?.CurrentStrength ?? _settings.Strength;
        private BrushFalloff Falloff => ActivePanel?.CurrentFalloff ?? _settings.Falloff;
        private float WeightValue => ActivePanel?.CurrentWeightValue ?? _settings.WeightValue;
        private int TargetBone => ActivePanel?.CurrentTargetBone ?? _settings.TargetBoneMasterIndex;

        // ================================================================
        // ウェイト可視化
        // ================================================================

        /// <summary>ウェイト可視化が有効か（Preview描画で参照）</summary>
        public static bool IsVisualizationActive { get; private set; }

        /// <summary>現在の可視化ターゲットボーン（Preview描画で参照）</summary>
        public static int VisualizationTargetBone =>
            ActivePanel?.CurrentTargetBone ?? -1;

        /// <summary>ウェイト可視化用マテリアル</summary>
        private static Material _weightVisMaterial;

        /// <summary>可視化用マテリアルを取得（遅延生成、カスタムシェーダー）</summary>
        public static Material GetVisualizationMaterial()
        {
            if (_weightVisMaterial != null) return _weightVisMaterial;

            // 1. プロジェクト内のシェーダーファイルを検索
            var shader = Shader.Find("Hidden/PolyLing_WeightVis");

            // 2. フォールバック: ShaderUtilでランタイム生成
            if (shader == null)
            {
                string src =
                    "Shader \"Hidden/PolyLing_WeightVis\" {\n" +
                    "  SubShader {\n" +
                    "    Tags { \"RenderType\"=\"Opaque\" }\n" +
                    "    Cull Off ZWrite On ZTest LEqual\n" +
                    "    Pass {\n" +
                    "      CGPROGRAM\n" +
                    "      #pragma vertex vert\n" +
                    "      #pragma fragment frag\n" +
                    "      #include \"UnityCG.cginc\"\n" +
                    "      struct appdata { float4 vertex : POSITION; float4 color : COLOR; };\n" +
                    "      struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };\n" +
                    "      v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }\n" +
                    "      float4 frag(v2f i) : SV_Target { return i.color; }\n" +
                    "      ENDCG\n" +
                    "    }\n" +
                    "  }\n" +
                    "}\n";
                shader = ShaderUtil.CreateShaderAsset(src, false);
            }

            // 3. フォールバック: 組み込みの頂点カラー対応シェーダー
            if (shader == null) shader = Shader.Find("GUI/Text Shader");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader != null)
                _weightVisMaterial = new Material(shader);

            return _weightVisMaterial;
        }

        /// <summary>
        /// 描画直前にメッシュの頂点カラーを設定する（Preview側から毎フレーム呼ばれる）
        /// SyncMeshでmesh.Clear()されても問題ない
        /// </summary>
        public static void ApplyVisualizationColors(Mesh mesh, MeshObject mo, int targetBone)
        {
            if (mesh == null || mo == null) return;

            int unityVertCount = mesh.vertexCount;
            var colors = new Color[unityVertCount];

            if (targetBone < 0)
            {
                // ターゲット未選択: 暗いグレー
                var grey = new Color(0.3f, 0.3f, 0.3f, 1f);
                for (int i = 0; i < unityVertCount; i++)
                    colors[i] = grey;
            }
            else
            {
                // ToUnityMeshShared と同じ展開順: 頂点順 → UV順
                int colorIdx = 0;
                for (int vIdx = 0; vIdx < mo.VertexCount && colorIdx < unityVertCount; vIdx++)
                {
                    var vertex = mo.Vertices[vIdx];
                    int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                    float w = 0f;
                    if (vertex.HasBoneWeight)
                        w = GetWeightForBone(vertex.BoneWeight.Value, targetBone);

                    Color col = WeightToHeatmapColor(w);

                    for (int uvIdx = 0; uvIdx < uvCount && colorIdx < unityVertCount; uvIdx++)
                    {
                        colors[colorIdx] = col;
                        colorIdx++;
                    }
                }

                // 残りはグレー
                var greyFill = new Color(0.3f, 0.3f, 0.3f, 1f);
                for (; colorIdx < unityVertCount; colorIdx++)
                    colors[colorIdx] = greyFill;
            }

            mesh.colors = colors;
        }

        /// <summary>
        /// ウェイト値 [0,1] → MAYA風ヒートマップカラー
        /// 0.0 = 青, 0.25 = シアン, 0.5 = 緑, 0.75 = 黄, 1.0 = 赤
        /// </summary>
        public static Color WeightToHeatmapColor(float weight)
        {
            weight = Mathf.Clamp01(weight);

            if (weight < 0.001f) return new Color(0.0f, 0.0f, 0.2f, 1f);

            float r, g, b;
            if (weight < 0.25f)
            {
                float t = weight / 0.25f;
                r = 0f; g = t; b = 1f;
            }
            else if (weight < 0.5f)
            {
                float t = (weight - 0.25f) / 0.25f;
                r = 0f; g = 1f; b = 1f - t;
            }
            else if (weight < 0.75f)
            {
                float t = (weight - 0.5f) / 0.25f;
                r = t; g = 1f; b = 0f;
            }
            else
            {
                float t = (weight - 0.75f) / 0.25f;
                r = 1f; g = 1f - t; b = 0f;
            }

            return new Color(r, g, b, 1f);
        }

        // ================================================================
        // ドラッグ状態
        // ================================================================

        private bool _isDragging;
        private Vector2 _currentScreenPos;

        /// <summary>ドラッグ開始時のMeshObjectスナップショット（Undo用）</summary>
        private MeshObjectSnapshot _beforeSnapshot;

        /// <summary>隣接頂点キャッシュ（Smoothモード用）</summary>
        private Dictionary<int, HashSet<int>> _adjacencyCache;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            var model = ctx.Model;
            if (model == null || !model.HasMeshSelection) return false;

            // ターゲットボーンが未設定
            if (TargetBone < 0 && PaintMode != SkinWeightPaintMode.Smooth) return false;

            var meshCtx = model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return false;

            _isDragging = true;
            _currentScreenPos = mousePos;

            // Undo用スナップショット
            _beforeSnapshot = ctx.UndoController?.CaptureMeshObjectSnapshot();

            // Smoothモード用隣接キャッシュ
            if (PaintMode == SkinWeightPaintMode.Smooth)
                BuildAdjacencyCache(meshCtx.MeshObject);

            // 最初のストローク適用
            ApplyBrush(ctx, mousePos);

            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;

            _currentScreenPos = mousePos;
            ApplyBrush(ctx, mousePos);

            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            // Undo記録
            if (ctx.UndoController != null && _beforeSnapshot != null)
            {
                var afterSnapshot = ctx.UndoController.CaptureMeshObjectSnapshot();
                ctx.CommandQueue?.Enqueue(new Commands.RecordTopologyChangeCommand(
                    ctx.UndoController, _beforeSnapshot, afterSnapshot, "Paint Skin Weight"));
            }

            _beforeSnapshot = null;
            _adjacencyCache = null;

            ctx.SyncMesh?.Invoke();
            ctx.Repaint?.Invoke();

            // パネルの表示更新
            ActivePanel?.NotifyWeightChanged();

            return true;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.Model == null || !ctx.Model.HasMeshSelection) return;

            var meshCtx = ctx.Model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            UnityEditor_Handles.BeginGUI();

            // ブラシカラー: モード別（ボーン未選択時はグレー）
            Color brushColor;
            bool noBone = TargetBone < 0 && PaintMode != SkinWeightPaintMode.Smooth;
            if (noBone)
                brushColor = new Color(0.6f, 0.6f, 0.6f, 0.3f);
            else
                brushColor = GetBrushColor();
            UnityEditor_Handles.color = brushColor;

            Vector2 centerScreen = Event.current.mousePosition;
            float screenRadius = EstimateBrushScreenRadius(ctx);

            // ブラシ円
            DrawCircle(centerScreen, screenRadius, 32);

            // 中心ドット
            UnityEditor_Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.8f);
            DrawCircle(centerScreen, 2f, 8);

            // モード・ボーン名テキスト
            GUI.color = Color.white;
            string modeName = PaintMode.ToString();
            string boneName = GetTargetBoneName(ctx);
            string label;
            if (TargetBone < 0 && PaintMode != SkinWeightPaintMode.Smooth)
            {
                GUI.color = new Color(1f, 0.8f, 0.3f);
                label = "← パネルでボーンを選択してください";
            }
            else
            {
                label = $"{modeName}  [{boneName}]  V={WeightValue:F2}";
            }
            GUI.Label(new Rect(centerScreen.x + screenRadius + 5, centerScreen.y - 10, 280, 20), label);
            GUI.color = Color.white;

            UnityEditor_Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Skin Weight Paint", EditorStyles.boldLabel);

            if (ActivePanel != null)
            {
                EditorGUILayout.HelpBox("設定はSkin Weight Paintパネルで変更してください。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Skin Weight Paintパネルを開くと詳細設定が使えます。", MessageType.Info);

                // 最低限の設定UI
                _settings.BrushRadius = EditorGUILayout.Slider("Radius", _settings.BrushRadius,
                    SkinWeightPaintSettings.MIN_BRUSH_RADIUS, SkinWeightPaintSettings.MAX_BRUSH_RADIUS);
                _settings.Strength = EditorGUILayout.Slider("Strength", _settings.Strength,
                    SkinWeightPaintSettings.MIN_STRENGTH, SkinWeightPaintSettings.MAX_STRENGTH);
                _settings.WeightValue = EditorGUILayout.Slider("Value", _settings.WeightValue, 0f, 1f);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
            IsVisualizationActive = true;
            ctx.SetSuppressHover?.Invoke(true);
            ctx.Repaint?.Invoke();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
            IsVisualizationActive = false;
            ctx.SetSuppressHover?.Invoke(false);
            // 頂点カラーをクリア
            if (ctx?.Model != null)
            {
                var meshCtx = ctx.Model.FirstSelectedDrawableMeshContext;
                if (meshCtx?.UnityMesh != null)
                    meshCtx.UnityMesh.colors = null;
            }
            ctx.Repaint?.Invoke();
        }

        public void Reset()
        {
            _isDragging = false;
            _beforeSnapshot = null;
            _adjacencyCache = null;
        }

        // ================================================================
        // ブラシ適用
        // ================================================================

        private void ApplyBrush(ToolContext ctx, Vector2 mousePos)
        {
            var model = ctx.Model;
            if (model == null) return;

            var meshCtx = model.FirstSelectedDrawableMeshContext;
            if (meshCtx?.MeshObject == null) return;

            // マウス位置からレイを取得
            Ray ray = ctx.ScreenPosToRay(mousePos);

            // ブラシ中心のワールド座標を計算
            Vector3 brushCenter = FindBrushCenter(ctx, meshCtx.MeshObject, ray);

            // ブラシ範囲内の頂点を収集
            var affected = GetVerticesInBrushRadius(meshCtx.MeshObject, brushCenter);
            if (affected.Count == 0) return;

            int targetBone = TargetBone;
            float strength = Strength;
            float value = WeightValue;

            var mo = meshCtx.MeshObject;

            switch (PaintMode)
            {
                case SkinWeightPaintMode.Replace:
                    foreach (var (vi, falloff) in affected)
                    {
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vertex = mo.Vertices[vi];
                        BoneWeight bw = vertex.BoneWeight ?? default;

                        // falloff × strengthで補間
                        float t = falloff * strength;
                        float currentWeight = GetWeightForBone(bw, targetBone);
                        float newWeight = Mathf.Lerp(currentWeight, value, t);

                        bw = SetBoneWeight(bw, targetBone, newWeight);
                        bw = NormalizeBoneWeight(bw);
                        vertex.BoneWeight = bw;
                    }
                    break;

                case SkinWeightPaintMode.Add:
                    foreach (var (vi, falloff) in affected)
                    {
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vertex = mo.Vertices[vi];
                        BoneWeight bw = vertex.BoneWeight ?? default;

                        float amount = falloff * strength * value * 0.1f; // ドラッグ毎に少量加算
                        bw = AddBoneWeight(bw, targetBone, amount);
                        bw = NormalizeBoneWeight(bw);
                        vertex.BoneWeight = bw;
                    }
                    break;

                case SkinWeightPaintMode.Scale:
                    foreach (var (vi, falloff) in affected)
                    {
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vertex = mo.Vertices[vi];
                        BoneWeight bw = vertex.BoneWeight ?? default;

                        float scale = Mathf.Lerp(1f, value, falloff * strength);
                        bw = ScaleBoneWeight(bw, targetBone, scale);
                        bw = NormalizeBoneWeight(bw);
                        vertex.BoneWeight = bw;
                    }
                    break;

                case SkinWeightPaintMode.Smooth:
                    ApplySmooth(mo, affected, strength);
                    break;
            }

            // メッシュ更新
            ctx.SyncMesh?.Invoke();
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // Smooth モード
        // ================================================================

        private void ApplySmooth(MeshObject mo, List<(int index, float falloff)> affected, float strength)
        {
            if (_adjacencyCache == null) return;

            // 各影響頂点のウェイトを、隣接頂点の平均に近づける
            // 全4スロット一括で処理
            var newWeights = new Dictionary<int, BoneWeight>();

            foreach (var (vi, falloff) in affected)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                if (!_adjacencyCache.TryGetValue(vi, out var neighbors) || neighbors.Count == 0) continue;

                var vertex = mo.Vertices[vi];
                if (!vertex.HasBoneWeight) continue;

                // 隣接頂点のウェイトの平均を計算
                // ボーンIndex → 合計ウェイト
                var boneWeightSum = new Dictionary<int, float>();
                int neighborCount = 0;

                foreach (int ni in neighbors)
                {
                    if (ni < 0 || ni >= mo.VertexCount) continue;
                    var nv = mo.Vertices[ni];
                    if (!nv.HasBoneWeight) continue;

                    var nbw = nv.BoneWeight.Value;
                    AccumulateBoneWeight(boneWeightSum, nbw.boneIndex0, nbw.weight0);
                    AccumulateBoneWeight(boneWeightSum, nbw.boneIndex1, nbw.weight1);
                    AccumulateBoneWeight(boneWeightSum, nbw.boneIndex2, nbw.weight2);
                    AccumulateBoneWeight(boneWeightSum, nbw.boneIndex3, nbw.weight3);
                    neighborCount++;
                }

                if (neighborCount == 0) continue;

                // 上位4ボーンを選択
                var sorted = new List<KeyValuePair<int, float>>(boneWeightSum);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

                BoneWeight avgBw = default;
                if (sorted.Count > 0) { avgBw.boneIndex0 = sorted[0].Key; avgBw.weight0 = sorted[0].Value / neighborCount; }
                if (sorted.Count > 1) { avgBw.boneIndex1 = sorted[1].Key; avgBw.weight1 = sorted[1].Value / neighborCount; }
                if (sorted.Count > 2) { avgBw.boneIndex2 = sorted[2].Key; avgBw.weight2 = sorted[2].Value / neighborCount; }
                if (sorted.Count > 3) { avgBw.boneIndex3 = sorted[3].Key; avgBw.weight3 = sorted[3].Value / neighborCount; }

                avgBw = NormalizeBoneWeight(avgBw);

                // 現在のウェイトとの補間
                float t = falloff * strength;
                BoneWeight currentBw = vertex.BoneWeight.Value;
                BoneWeight blended = LerpBoneWeight(currentBw, avgBw, t);

                newWeights[vi] = blended;
            }

            // 一括適用
            foreach (var kv in newWeights)
            {
                mo.Vertices[kv.Key].BoneWeight = kv.Value;
            }
        }

        private static void AccumulateBoneWeight(Dictionary<int, float> dict, int boneIndex, float weight)
        {
            if (weight <= 0f) return;
            if (dict.ContainsKey(boneIndex))
                dict[boneIndex] += weight;
            else
                dict[boneIndex] = weight;
        }

        /// <summary>
        /// 2つのBoneWeightを補間（スロット単位ではなくボーンID基準で合成）
        /// </summary>
        private static BoneWeight LerpBoneWeight(BoneWeight a, BoneWeight b, float t)
        {
            // 両方のボーンIDを集約し、ウェイトを補間
            var merged = new Dictionary<int, float>();

            AddLerped(merged, a.boneIndex0, a.weight0, t);
            AddLerped(merged, a.boneIndex1, a.weight1, t);
            AddLerped(merged, a.boneIndex2, a.weight2, t);
            AddLerped(merged, a.boneIndex3, a.weight3, t);

            AddLerpedTarget(merged, b.boneIndex0, b.weight0, t);
            AddLerpedTarget(merged, b.boneIndex1, b.weight1, t);
            AddLerpedTarget(merged, b.boneIndex2, b.weight2, t);
            AddLerpedTarget(merged, b.boneIndex3, b.weight3, t);

            // 上位4つ選択
            var sorted = new List<KeyValuePair<int, float>>(merged);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            BoneWeight result = default;
            if (sorted.Count > 0) { result.boneIndex0 = sorted[0].Key; result.weight0 = sorted[0].Value; }
            if (sorted.Count > 1) { result.boneIndex1 = sorted[1].Key; result.weight1 = sorted[1].Value; }
            if (sorted.Count > 2) { result.boneIndex2 = sorted[2].Key; result.weight2 = sorted[2].Value; }
            if (sorted.Count > 3) { result.boneIndex3 = sorted[3].Key; result.weight3 = sorted[3].Value; }

            return NormalizeBoneWeight(result);
        }

        private static void AddLerped(Dictionary<int, float> dict, int bone, float weight, float t)
        {
            if (weight <= 0f) return;
            float v = weight * (1f - t);
            if (dict.ContainsKey(bone)) dict[bone] += v; else dict[bone] = v;
        }

        private static void AddLerpedTarget(Dictionary<int, float> dict, int bone, float weight, float t)
        {
            if (weight <= 0f) return;
            float v = weight * t;
            if (dict.ContainsKey(bone)) dict[bone] += v; else dict[bone] = v;
        }

        // ================================================================
        // 隣接キャッシュ構築
        // ================================================================

        private void BuildAdjacencyCache(MeshObject mo)
        {
            _adjacencyCache = new Dictionary<int, HashSet<int>>();

            foreach (var face in mo.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!_adjacencyCache.ContainsKey(v1)) _adjacencyCache[v1] = new HashSet<int>();
                    if (!_adjacencyCache.ContainsKey(v2)) _adjacencyCache[v2] = new HashSet<int>();

                    _adjacencyCache[v1].Add(v2);
                    _adjacencyCache[v2].Add(v1);
                }
            }
        }

        // ================================================================
        // ブラシ中心・範囲
        // ================================================================

        private Vector3 FindBrushCenter(ToolContext ctx, MeshObject mo, Ray ray)
        {
            float closestDist = float.MaxValue;
            Vector3 closestPoint = ray.origin + ray.direction * 5f;

            foreach (var face in mo.Faces)
            {
                if (face.VertexIndices.Count < 3) continue;

                Vector3 v0 = mo.Vertices[face.VertexIndices[0]].Position;

                for (int i = 1; i < face.VertexIndices.Count - 1; i++)
                {
                    Vector3 v1 = mo.Vertices[face.VertexIndices[i]].Position;
                    Vector3 v2 = mo.Vertices[face.VertexIndices[i + 1]].Position;

                    if (RayTriangleIntersection(ray, v0, v1, v2, out float t))
                    {
                        if (t < closestDist)
                        {
                            closestDist = t;
                            closestPoint = ray.origin + ray.direction * t;
                        }
                    }
                }
            }

            return closestPoint;
        }

        private List<(int index, float falloff)> GetVerticesInBrushRadius(MeshObject mo, Vector3 brushCenter)
        {
            var result = new List<(int, float)>();
            float radius = BrushRadius;
            var falloffType = Falloff;

            for (int i = 0; i < mo.VertexCount; i++)
            {
                float dist = Vector3.Distance(mo.Vertices[i].Position, brushCenter);
                if (dist <= radius)
                {
                    float normalizedDist = dist / radius;
                    float weight = ComputeFalloff(normalizedDist, falloffType);
                    result.Add((i, weight));
                }
            }

            return result;
        }

        private static float ComputeFalloff(float normalizedDist, BrushFalloff type)
        {
            float t = 1f - normalizedDist;
            switch (type)
            {
                case BrushFalloff.Constant:
                    return 1f;
                case BrushFalloff.Linear:
                    return t;
                case BrushFalloff.Smooth:
                    return t * t * (3f - 2f * t); // Hermite smoothstep
                default:
                    return t;
            }
        }

        // ================================================================
        // レイキャスト
        // ================================================================

        private static bool RayTriangleIntersection(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            t = 0;
            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (Mathf.Abs(a) < 1e-6f) return false;

            float f = 1f / a;
            Vector3 s = ray.origin - v0;
            float u = f * Vector3.Dot(s, h);

            if (u < 0 || u > 1) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);

            if (v < 0 || u + v > 1) return false;

            t = f * Vector3.Dot(edge2, q);
            return t > 1e-6f;
        }

        // ================================================================
        // BoneWeight操作
        // ================================================================

        private static float GetWeightForBone(BoneWeight bw, int boneIndex)
        {
            if (bw.boneIndex0 == boneIndex) return bw.weight0;
            if (bw.boneIndex1 == boneIndex) return bw.weight1;
            if (bw.boneIndex2 == boneIndex) return bw.weight2;
            if (bw.boneIndex3 == boneIndex) return bw.weight3;
            return 0f;
        }

        private static BoneWeight SetBoneWeight(BoneWeight bw, int boneIndex, float weight)
        {
            weight = Mathf.Clamp01(weight);

            var slots = ExtractSlots(bw);

            int targetSlot = -1;
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex && slots[i].weight > 0f)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot < 0)
                targetSlot = FindSlotForNewBone(slots);

            float otherTotal = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (i != targetSlot) otherTotal += slots[i].weight;
            }

            slots[targetSlot] = (boneIndex, weight);

            float remaining = 1f - weight;
            if (otherTotal > 0.0001f)
            {
                float scale = remaining / otherTotal;
                for (int i = 0; i < 4; i++)
                {
                    if (i != targetSlot)
                        slots[i].weight *= scale;
                }
            }

            return PackSlots(slots);
        }

        private static BoneWeight AddBoneWeight(BoneWeight bw, int boneIndex, float amount)
        {
            var slots = ExtractSlots(bw);

            int targetSlot = -1;
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex && slots[i].weight > 0f)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot < 0)
                targetSlot = FindSlotForNewBone(slots);

            slots[targetSlot] = (boneIndex, Mathf.Clamp01(slots[targetSlot].weight + amount));

            return PackSlots(slots);
        }

        private static BoneWeight ScaleBoneWeight(BoneWeight bw, int boneIndex, float scale)
        {
            var slots = ExtractSlots(bw);

            for (int i = 0; i < 4; i++)
            {
                if (slots[i].index == boneIndex)
                {
                    slots[i].weight = Mathf.Clamp01(slots[i].weight * scale);
                    break;
                }
            }

            return PackSlots(slots);
        }

        private static BoneWeight NormalizeBoneWeight(BoneWeight bw)
        {
            float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (total < 0.0001f) return bw;

            float inv = 1f / total;
            bw.weight0 *= inv;
            bw.weight1 *= inv;
            bw.weight2 *= inv;
            bw.weight3 *= inv;
            return bw;
        }

        // ================================================================
        // スロット操作ヘルパー
        // ================================================================

        private static (int index, float weight)[] ExtractSlots(BoneWeight bw)
        {
            return new (int, float)[]
            {
                (bw.boneIndex0, bw.weight0),
                (bw.boneIndex1, bw.weight1),
                (bw.boneIndex2, bw.weight2),
                (bw.boneIndex3, bw.weight3),
            };
        }

        private static BoneWeight PackSlots((int index, float weight)[] slots)
        {
            return new BoneWeight
            {
                boneIndex0 = slots[0].index, weight0 = slots[0].weight,
                boneIndex1 = slots[1].index, weight1 = slots[1].weight,
                boneIndex2 = slots[2].index, weight2 = slots[2].weight,
                boneIndex3 = slots[3].index, weight3 = slots[3].weight,
            };
        }

        private static int FindSlotForNewBone((int index, float weight)[] slots)
        {
            for (int i = 0; i < 4; i++)
            {
                if (slots[i].weight <= 0f) return i;
            }

            int minSlot = 0;
            float minWeight = slots[0].weight;
            for (int i = 1; i < 4; i++)
            {
                if (slots[i].weight < minWeight)
                {
                    minWeight = slots[i].weight;
                    minSlot = i;
                }
            }
            return minSlot;
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        private Color GetBrushColor()
        {
            switch (PaintMode)
            {
                case SkinWeightPaintMode.Replace: return new Color(0.3f, 0.7f, 1.0f, 0.5f);
                case SkinWeightPaintMode.Add:     return new Color(0.3f, 1.0f, 0.5f, 0.5f);
                case SkinWeightPaintMode.Scale:   return new Color(1.0f, 0.8f, 0.3f, 0.5f);
                case SkinWeightPaintMode.Smooth:  return new Color(0.8f, 0.5f, 1.0f, 0.5f);
                default: return new Color(1f, 1f, 1f, 0.5f);
            }
        }

        private string GetTargetBoneName(ToolContext ctx)
        {
            int bone = TargetBone;
            if (bone < 0) return "未選択";

            var model = ctx.Model;
            if (model == null || bone >= model.MeshContextCount) return $"[{bone}]";

            var boneCtx = model.GetMeshContext(bone);
            return boneCtx?.Name ?? $"[{bone}]";
        }

        private float EstimateBrushScreenRadius(ToolContext ctx)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f)
                camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * BrushRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private void DrawCircle(Vector2 center, float radius, int segments)
        {
            Vector2 prevPoint = center + new Vector2(radius, 0);

            for (int i = 1; i <= segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector2 point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                UnityEditor_Handles.DrawAAPolyLine(2f, prevPoint, point);
                prevPoint = point;
            }
        }
    }
}
