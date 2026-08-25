// Assets/Editor/Poly_Ling_Main/Tools/TransformTools/SkinWeightPaintTool_/SkinWeightPaintTool.cs
// スキンウェイトペイントツール（IEditTool実装）
// ブラシでドラッグしてスキンウェイトをペイントする

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Tools;
using static Poly_Ling.Gizmo.GLGizmoDrawer;
using Poly_Ling.Commands;
using Poly_Ling.UI;
using Poly_Ling.Context;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// スキンウェイトペイントツール
    /// </summary>
    public partial class SkinWeightPaintTool : IEditTool
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
        public static ISkinWeightPaintPanel ActivePanel { get; set; }

        // パネルから設定を読む（パネルがなければ自分の設定を使う）
        private SkinWeightPaintMode PaintMode => ActivePanel?.CurrentPaintMode ?? _settings.PaintMode;
        private float BrushRadius => ActivePanel?.CurrentBrushRadius ?? _settings.BrushRadius;
        private float Strength => ActivePanel?.CurrentStrength ?? _settings.Strength;
        private FalloffType Falloff => ActivePanel?.CurrentFalloff ?? _settings.Falloff;

        /// <summary>距離モード（直線 / リンク距離）。マグネット／スカルプトと共通。</summary>
        public static DistanceMode CurrentDistanceMode =>
            ActivePanel?.CurrentDistanceMode ?? DistanceMode.Euclidean;
        private float WeightValue => ActivePanel?.CurrentWeightValue ?? _settings.WeightValue;
        private int TargetBone => ActivePanel?.CurrentTargetBone ?? _settings.TargetBoneMasterIndex;

        // ペイント対象メッシュの解決は
        // Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts に一本化した。
        // ブラシは選択中の描画オブジェクト全件をまたいで塗るため、
        // 1 メッシュを返す GetTargetMeshContext は削除してある。
        // ウェイト可視化（MeshSceneRenderer.CollectWeightVisTargets）とも同じ集合になる。

        // ================================================================
        // ウェイト可視化
        // ================================================================

        /// <summary>ウェイト可視化が有効か（Preview描画で参照）</summary>
        public static bool IsVisualizationActive { get; private set; }

        /// <summary>ウェイト可視化の有効/無効を設定する（Player ビルド用）。</summary>
        public static void SetVisualizationActive(bool value) => IsVisualizationActive = value;

        /// <summary>現在の可視化ターゲットボーン（Preview描画で参照）</summary>
        public static int VisualizationTargetBone =>
            ActivePanel?.CurrentTargetBone ?? -1;

        /// <summary>
        /// 複数ボーン合算表示の対象（Blender の Multi-Paint 相当）。
        /// パネルが IMultiBoneWeightVisualization を実装していない、または
        /// 対象が空なら null。呼び出し側はそのとき VisualizationTargetBone の
        /// 単一ボーン表示へ落とすこと。
        /// </summary>
        public static IReadOnlyList<int> VisualizationTargetBones
        {
            get
            {
                var bones = (ActivePanel as IMultiBoneWeightVisualization)?.VisualizationBones;
                return (bones != null && bones.Count > 0) ? bones : null;
            }
        }

        /// <summary>ウェイト可視化用マテリアル</summary>
        private static Material _weightVisMaterial;

        /// <summary>取得失敗を一度だけ報告するためのフラグ。</summary>
        private static bool _weightVisShaderErrorLogged;

        /// <summary>
        /// 可視化用マテリアルを取得（遅延生成）。
        ///
        /// シェーダは Runtime/Resources/Shaders/PolyLing_WeightVis.shader。
        /// Resources 配下にあるためビルドに必ず含まれる。
        ///
        /// フォールバックは持たない。以前は ShaderUtil でのランタイム生成と
        /// GUI/Text Shader・Sprites/Default への差し替えを試みていたが、
        /// 前者は Editor 専用、後者は URP に存在しないため、いずれも成立しないまま
        /// 「マテリアルが null → 何も描かれない」状態を無音で作っていた。
        /// 取得できない場合は原因を明示して null を返す。
        /// </summary>
        public static Material GetVisualizationMaterial()
        {
            if (_weightVisMaterial != null) return _weightVisMaterial;

            var shader = Shader.Find("Hidden/PolyLing_WeightVis");
            if (shader == null)
            {
                if (!_weightVisShaderErrorLogged)
                {
                    _weightVisShaderErrorLogged = true;
                    Debug.LogError(
                        "[SkinWeightPaint] シェーダ \"Hidden/PolyLing_WeightVis\" が見つかりません。" +
                        "Runtime/Resources/Shaders/PolyLing_WeightVis.shader の配置を確認してください。" +
                        "ウェイト可視化は描画されません。");
                }
                return null;
            }

            _weightVisMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return _weightVisMaterial;
        }

        /// <summary>
        /// 描画直前にメッシュの頂点カラーを設定する（Preview側から毎フレーム呼ばれる）
        /// SyncMeshでmesh.Clear()されても問題ない
        /// </summary>
        /// <returns>ウェイトが 0 より大きかった頂点数。診断用。</returns>
        public static int ApplyVisualizationColors(Mesh mesh, MeshObject mo, int targetBone)
        {
            if (mesh == null || mo == null) return 0;

            int unityVertCount = mesh.vertexCount;
            var colors = new Color[unityVertCount];
            int weightedCount = 0;

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
                    if (w > 0f) weightedCount++;

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
            return weightedCount;
        }

        /// <summary>
        /// 複数ボーンのウェイトを合計してヒートマップ色を焼き込む
        /// （Blender の Multi-Paint 相当）。
        ///
        /// 同一ボーンが複数スロットに指定されていても二重計上しないよう、
        /// 重複を除いてから合算する。合計は 0..1 にクランプする
        /// （正規化されていないメッシュでは合計が 1 を超え得る）。
        /// </summary>
        /// <returns>合計ウェイトが 0 より大きかった頂点数。診断用。</returns>
        public static int ApplyVisualizationColors(
            Mesh mesh, MeshObject mo, IReadOnlyList<int> targetBones)
        {
            if (mesh == null || mo == null) return 0;

            // 対象が無ければ単一ボーン版の「未選択」表示に合わせる。
            if (targetBones == null || targetBones.Count == 0)
                return ApplyVisualizationColors(mesh, mo, -1);

            // 重複ボーンを除いた一覧を作る。最大 4 件なので線形探索でよい。
            var bones = new List<int>(targetBones.Count);
            foreach (int b in targetBones)
                if (b >= 0 && !bones.Contains(b)) bones.Add(b);

            if (bones.Count == 0) return ApplyVisualizationColors(mesh, mo, -1);

            int unityVertCount = mesh.vertexCount;
            var colors         = new Color[unityVertCount];
            int weightedCount  = 0;

            // ToUnityMeshShared と同じ展開順: 頂点順 → UV順
            int colorIdx = 0;
            for (int vIdx = 0; vIdx < mo.VertexCount && colorIdx < unityVertCount; vIdx++)
            {
                var vertex  = mo.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                float w = 0f;
                if (vertex.HasBoneWeight)
                {
                    var bw = vertex.BoneWeight.Value;
                    for (int i = 0; i < bones.Count; i++)
                        w += GetWeightForBone(bw, bones[i]);
                }
                w = Mathf.Clamp01(w);
                if (w > 0f) weightedCount++;

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

            mesh.colors = colors;
            return weightedCount;
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

        /// <summary>
        /// ドラッグ開始時のスナップショット（Undo 用）。メッシュごとに持つ。
        /// UndoController は一度に 1 メッシュしか保持できないため、
        /// 取得時・記録時ともに SetMeshObject で対象を差し替える。
        /// </summary>
        private readonly Dictionary<MeshContext, MeshObjectSnapshot> _beforeSnapshots
            = new Dictionary<MeshContext, MeshObjectSnapshot>();

        /// <summary>実際に書き換えたメッシュ。OnMouseUp で Undo 記録する対象。</summary>
        private readonly HashSet<MeshContext> _touchedMeshes = new HashSet<MeshContext>();

        /// <summary>隣接頂点キャッシュ（Smoothモード用）。メッシュごとに持つ。</summary>
        private readonly Dictionary<MeshContext, Dictionary<int, HashSet<int>>> _adjacencyCaches
            = new Dictionary<MeshContext, Dictionary<int, HashSet<int>>>();

        /// <summary>ブラシ対象のメッシュ群（＝選択中の描画オブジェクト全件）。</summary>
        private static List<MeshContext> CollectBrushTargets(ModelContext model)
            => Poly_Ling.UI.SkinWeightOperations.CollectTargetMeshContexts(model);

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            var model = ctx.Model;
            if (model == null || !model.HasMeshSelection) return false;

            // ターゲットボーンが未設定
            if (TargetBone < 0 && PaintMode != SkinWeightPaintMode.Smooth) return false;

            var targets = CollectBrushTargets(model);
            if (targets.Count == 0) return false;

            _isDragging = true;
            _currentScreenPos = mousePos;

            // Undo 用スナップショットを対象メッシュごとに取る。
            _beforeSnapshots.Clear();
            _touchedMeshes.Clear();
            _adjacencyCaches.Clear();

            var undo = ctx.UndoController;
            foreach (var mc in targets)
            {
                if (mc?.MeshObject == null) continue;

                if (undo != null)
                {
                    // SetMeshObjectFor を使うこと。SetMeshObject(MeshObject,…) は
                    // 書き込み先が先頭の選択メッシュに固定されるため、この
                    // ループの2件目以降で先頭メッシュの MeshObject が壊れる。
                    undo.MeshUndoContext.ParentModelContext = model;
                    undo.SetMeshObjectFor(mc, mc.UnityMesh);
                    var snap = undo.CaptureMeshObjectSnapshot();
                    if (snap != null) _beforeSnapshots[mc] = snap;
                }

                // Smoothモード用隣接キャッシュ
                if (PaintMode == SkinWeightPaintMode.Smooth)
                    _adjacencyCaches[mc] = BuildAdjacency(mc.MeshObject);
            }
            undo?.ClearTargetMeshContext();

            // 最初のストローク適用
            ApplyBrush(ctx);

            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;

            _currentScreenPos = mousePos;
            ApplyBrush(ctx);

            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            // Undo 記録。書き換えたメッシュごとに対象を差し替えて after を取る。
            var undo = ctx.UndoController;
            if (undo != null)
            {
                foreach (var mc in _touchedMeshes)
                {
                    if (mc?.MeshObject == null) continue;
                    if (!_beforeSnapshots.TryGetValue(mc, out var before) || before == null) continue;

                    undo.MeshUndoContext.ParentModelContext = ctx.Model;
                    undo.SetMeshObjectFor(mc, mc.UnityMesh);
                    var after = undo.CaptureMeshObjectSnapshot();
                    ctx.CommandQueue?.Enqueue(new Commands.RecordTopologyChangeCommand(
                        undo, before, after, "Paint Skin Weight"));
                }
                undo.ClearTargetMeshContext();
            }

            _beforeSnapshots.Clear();
            _touchedMeshes.Clear();
            _adjacencyCaches.Clear();

            ctx.SyncMesh?.Invoke();
            ctx.Repaint?.Invoke();

            // パネルの表示更新
            ActivePanel?.NotifyWeightChanged();

            return true;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

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
            // 頂点カラーをクリア（可視化と同じく選択中の描画オブジェクト全件）
            if (ctx?.Model != null)
            {
                foreach (var mc in CollectBrushTargets(ctx.Model))
                    if (mc?.UnityMesh != null) mc.UnityMesh.colors = null;
            }
            ctx.Repaint?.Invoke();
        }

        public void Reset()
        {
            _isDragging = false;
            _beforeSnapshots.Clear();
            _touchedMeshes.Clear();
            _adjacencyCaches.Clear();
        }

        // ================================================================
        // ブラシ適用
        // ================================================================

        private void ApplyBrush(ToolContext ctx)
        {
            var model = ctx.Model;
            if (model == null) return;

            // ブラシ範囲内の頂点をメッシュごとに収集
            // （Player の GPU hover path / スクリーン空間ブラシ）
            var hits = ctx.GetBrushVerticesMulti?.Invoke();
            if (hits == null || hits.Count == 0) return;

            bool any = false;
            foreach (var (meshCtx, affected) in hits)
            {
                if (meshCtx?.MeshObject == null) continue;
                if (affected == null || affected.Count == 0) continue;
                ApplyBrushToMesh(meshCtx, affected);
                _touchedMeshes.Add(meshCtx);
                any = true;
            }
            if (!any) return;

            // メッシュ更新
            ctx.SyncMesh?.Invoke();
            ctx.Repaint?.Invoke();
        }

        /// <summary>1 メッシュへブラシを適用する。</summary>
        private void ApplyBrushToMesh(
            MeshContext meshCtx, List<(int index, float falloff)> affected)
        {
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
                    if (_adjacencyCaches.TryGetValue(meshCtx, out var adjacency))
                        ApplySmooth(mo, affected, strength, adjacency);
                    break;
            }

            // Replace / Add / Scale は BoneWeight を持たない頂点にも書き込む
            //（vertex.BoneWeight ?? default から始める）ため、無 → 有の遷移点。
            // Smooth は既存ウェイトのある頂点しか触らないが、判定は同じ場所へ寄せる。
            mo.RecomputeSkinKind();
        }

        // ================================================================
        // Smooth モード
        // ================================================================

        private void ApplySmooth(
            MeshObject mo, List<(int index, float falloff)> affected, float strength,
            Dictionary<int, HashSet<int>> adjacencyCache)
        {
            if (adjacencyCache == null) return;

            // 各影響頂点のウェイトを、隣接頂点の平均に近づける
            // 全4スロット一括で処理
            var newWeights = new Dictionary<int, BoneWeight>();

            foreach (var (vi, falloff) in affected)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                if (!adjacencyCache.TryGetValue(vi, out var neighbors) || neighbors.Count == 0) continue;

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

        /// <summary>
        /// 頂点隣接キャッシュを構築して返す。
        /// リンク距離ブラシ（SkinWeightPaintToolHandler）からも使うため public。
        /// </summary>
        public static Dictionary<int, HashSet<int>> BuildAdjacency(MeshObject mo)
        {
            var cache = new Dictionary<int, HashSet<int>>();
            if (mo == null) return cache;

            foreach (var face in mo.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!cache.ContainsKey(v1)) cache[v1] = new HashSet<int>();
                    if (!cache.ContainsKey(v2)) cache[v2] = new HashSet<int>();

                    cache[v1].Add(v2);
                    cache[v2].Add(v1);
                }
            }
            return cache;
        }


        // ブラシ falloff の計算は Poly_Ling.Tools.FalloffHelper.Calculate に統一した。
        // 以前ここにあった ComputeFalloff（Constant/Linear/Smooth の 3 種）は削除。
        // なお実際に falloff を掛けているのは
        // SkinWeightPaintToolHandler.ComputeBrushVertices であり、この Tool 側は
        // 受け取った falloff を使うだけ。

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
                // UnityEditor_Handles 削除済み
                prevPoint = point;
            }
        }
    }
}
