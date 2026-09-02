// Vrm10SceneAssembler.cs
// ============================================================
// HierarchyBuilder の生成物へ VRM コンポーネントを載せる
// ============================================================
//
// 【分離規約】規約は Poly_Ling.Vrm.IVrm10Exporter.cs 冒頭のコメントを正典とする。
//   本ファイルは PolyLing.Vrm10 アセンブリに属し、VRM パッケージへの依存はここに閉じる。
//
// ============================================================
// なぜ GameObject 階層を作るのか
// ============================================================
//
//   UniVRM の Vrm10Exporter.ExportVrm は、root に Vrm10Instance が付いている
//   場合にのみ Expression / LookAt / FirstPerson / SpringBone / Constraint を出す
//   （Vrm10Exporter.cs:310-318）。しかもノード index は
//   ModelExporter.Nodes[GameObject] からしか引けない（同 :538-547, :802-816）。
//   VrmLib.Model を直に組む経路ではこの入口に触れないため、表情と揺れは出せない。
//
//   よってヒエラルキーを作って UniVRM の本線へ載せる。ヒエラルキー生成は
//   Runtime の HierarchyBuilder が持つので、ここでやるのは
//   「PolyLing のデータを VRM のコンポーネントへ写す」ことだけ。
//
// ============================================================
// Humanoid に Avatar は要らない
// ============================================================
//
//   ModelExporter.Export は root の UniHumanoid.Humanoid を見る。無ければ
//   自分で付けて AssignBonesFromAnimator() を呼ぶが、これは Animator と有効な
//   Avatar が無いと false を返す（Humanoid.cs:411-423）。
//   Humanoid.AssignBones(IEnumerable<(HumanBodyBones, Transform)>) が public
//   （同 :321）なので、こちらで割り当ててしまえば Avatar 構築は不要。
//
// ============================================================
// 座標系とスケール
// ============================================================
//
//   Unity 左手系のまま組む。VRM（右手系）への変換は
//   Vrm10Exporter.Export(settings, go, ...) の中の
//   Model.ConvertCoordinate(Coordinates.Vrm1) が行う。
//   SpringBone のオフセット・重力方向も UniVRM 側が ReverseX する
//   （Vrm10Exporter.cs:138-141, :333, :450）ので、ここでは working 空間の
//   生値をそのまま入れる。
//
//   出力スケールは root の localScale で与える。頂点を触らないため、
//   ブレンドシェイプの差分にも矛盾が生じない。
//
// ============================================================
// 後始末
// ============================================================
//
//   ここで作る VRM10Object / VRM10Expression は ScriptableObject。
//   アセットではないので、エクスポート後に必ず Dispose で破棄する。
//   放置すると Play を抜けるまで残る。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Ops;
using Poly_Ling.Vrm;

namespace Poly_Ling.Vrm10Impl
{
    /// <summary>組み立て結果の付帯情報。</summary>
    public class AssembleReport
    {
        /// <summary>Humanoid に割り当てたボーン数。</summary>
        public int HumanoidBoneCount;

        /// <summary>作った表情の数（プリセット＋カスタム）。</summary>
        public int ExpressionCount;

        /// <summary>作った揺れチェーン（Spring）の数。</summary>
        public int SpringCount;

        /// <summary>ダミー関節で補った数。補完が OFF のときは 0。</summary>
        public int SupplementedJointCount;

        /// <summary>付けたスプリングボーン・コライダーの数。</summary>
        public int SpringBoneColliderCount;

        /// <summary>載せたブレンドシェイプの総数。</summary>
        public int MorphShapeCount;

        /// <summary>警告（呼び出し側がログへ流す）。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>VRM 1.0 の必須 humanBones のうち割り当てられなかったもの。</summary>
        public readonly List<string> MissingRequiredBones = new List<string>();
    }

    /// <summary>
    /// HierarchyBuilder が作った GameObject 階層へ VRM コンポーネントを載せる。
    /// 生成した ScriptableObject の寿命を持つので、使い終わったら Dispose すること。
    /// </summary>
    public sealed class Vrm10SceneAssembler : IDisposable
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        public VRM10Object VrmObject { get; private set; }

        // ================================================================
        // エントリ
        // ================================================================

        /// <summary>
        /// root へ Humanoid / Vrm10Instance / VRM10Object を付け、
        /// 表情とスプリングボーンを組む。
        /// </summary>
        public AssembleReport Assemble(
            ModelContext model, HierarchyBuildResult hierarchy,
            VRM10ObjectMeta meta, Vrm10ExportSettings settings)
        {
            var report = new AssembleReport();
            if (model == null || hierarchy?.Root == null) return report;

            settings = settings ?? Vrm10ExportSettings.CreateDefault();
            var root = hierarchy.Root;

            report.MorphShapeCount = hierarchy.MorphShapeCount;

            // ----------------------------------------------------------------
            // 出力スケール（頂点は触らない）
            // ----------------------------------------------------------------
            if (!Mathf.Approximately(settings.Scale, 1f))
                root.transform.localScale = Vector3.one * settings.Scale;

            // ----------------------------------------------------------------
            // Humanoid
            // ----------------------------------------------------------------
            var humanoidMap = HumanoidTransformMap.Build(model, hierarchy);
            report.Warnings.AddRange(humanoidMap.Warnings);

            // 必須関節の補完。
            //   VRM 1.0 は humanoid を必須とし、必須ボーンが1つでも欠けると
            //   ビューアが読み込みを拒否する。上半身だけ・片側だけのモデルは
            //   そのままでは必ず欠けるので、空ノードで補ってから割り当てる。
            //   補完は割当済みのものを動かさず、足りない分だけ足す。
            //   プレファブ書き出し（HierarchyExportWindow）と同じ実装を通す。
            var transformMap = humanoidMap.TransformMap;
            if (settings.SupplementHumanoid && humanoidMap.Map.Count > 0)
            {
                var supplemented = new Dictionary<string, Transform>();
                report.SupplementedJointCount = HumanoidSupplementBuilder.Supplement(
                    root, humanoidMap.Map,
                    m => report.Warnings.Add(m),
                    m => report.Warnings.Add(m),
                    supplemented);

                if (supplemented.Count > 0) transformMap = supplemented;
            }

            var humanoid = root.GetComponent<UniHumanoid.Humanoid>();
            if (humanoid == null) humanoid = root.AddComponent<UniHumanoid.Humanoid>();

            var pairs = new List<(HumanBodyBones, Transform)>();
            var assignedTraits = new HashSet<string>();
            foreach (var kv in transformMap)
            {
                var bone = HumanoidTransformMap.ToHumanBodyBones(kv.Key);
                if (bone == HumanBodyBones.LastBone || kv.Value == null) continue;
                pairs.Add((bone, kv.Value));
                assignedTraits.Add(kv.Key);
            }
            humanoid.AssignBones(pairs);
            report.HumanoidBoneCount = pairs.Count;
            report.MissingRequiredBones.AddRange(CollectMissingRequiredBones(assignedTraits));

            // ----------------------------------------------------------------
            // Vrm10Instance + VRM10Object
            //   Start/Update/LateUpdate が SpringBone ランタイムを起こすので無効にする
            //   （Vrm10Instance.cs:193-221）。無効でも TryGetComponent では取れるため、
            //   Vrm10Exporter は問題なく読める。
            // ----------------------------------------------------------------
            var instance = root.GetComponent<Vrm10Instance>();
            if (instance == null) instance = root.AddComponent<Vrm10Instance>();
            instance.enabled = false;

            VrmObject = ScriptableObject.CreateInstance<VRM10Object>();
            VrmObject.name = "__polyling_vrm10_object__";
            VrmObject.hideFlags = HideFlags.HideAndDontSave;
            _created.Add(VrmObject);

            if (meta != null) VrmObject.Meta = meta;
            instance.Vrm = VrmObject;

            // ----------------------------------------------------------------
            // 表情
            // ----------------------------------------------------------------
            if (settings.ExportExpressions)
                report.ExpressionCount = BuildExpressions(model, hierarchy, settings, report);

            // ----------------------------------------------------------------
            // スプリングボーン
            // ----------------------------------------------------------------
            if (settings.ExportSpringBones)
                BuildSpringBones(model, hierarchy, instance, report);

            return report;
        }

        public void Dispose()
        {
            foreach (var o in _created)
            {
                if (o == null) continue;
                if (Application.isPlaying) UnityEngine.Object.Destroy(o);
                else                       UnityEngine.Object.DestroyImmediate(o);
            }
            _created.Clear();
            VrmObject = null;
        }

        // ================================================================
        // 表情
        // ================================================================

        /// <summary>
        /// MorphExpressions を VRM10Expression へ写す。
        ///
        /// バインド先は HierarchyBuildResult.MorphShapes（モーフ MeshContext 索引 →
        /// レンダラ Transform ＋ ブレンドシェイプ index）。
        /// MorphTargetBinding.RelativePath は Vrm10Exporter.cs:804 が
        /// GetFromPath で引くため、root からの相対パスで作る。
        /// </summary>
        private int BuildExpressions(
            ModelContext model, HierarchyBuildResult hierarchy,
            Vrm10ExportSettings settings, AssembleReport report)
        {
            var expressions = model.MorphExpressions;
            if (expressions == null || expressions.Count == 0) return 0;

            // モーフ索引 → 載せたブレンドシェイプ（実体側とミラー枝の両方に載ることがある）
            var shapesByMorph = new Dictionary<int, List<MorphShapeSlot>>();
            foreach (var slot in hierarchy.MorphShapes)
            {
                if (!shapesByMorph.TryGetValue(slot.MorphContextIndex, out var list))
                {
                    list = new List<MorphShapeSlot>();
                    shapesByMorph[slot.MorphContextIndex] = list;
                }
                list.Add(slot);
            }

            var target = VrmObject.Expression;
            var usedPresets = new HashSet<ExpressionPreset>();
            var usedCustomNames = new HashSet<string>();
            int count = 0;

            foreach (var expr in expressions)
            {
                if (expr == null || !expr.IsValid) continue;

                // 頂点モーフとグループモーフだけを扱う。
                // UV・材質・ボーンモーフは PolyLing 側に対応するデータが無い。
                if (expr.Type != MorphType.Vertex && expr.Type != MorphType.Group)
                {
                    report.Warnings.Add(
                        $"表情 \"{expr.Name}\" は種別 {expr.Type} のため出力しません"
                        + "（頂点モーフ／グループモーフのみ対応）。");
                    continue;
                }

                var bindings = new List<MorphTargetBinding>();
                foreach (var entry in expr.MeshEntries)
                {
                    if (!shapesByMorph.TryGetValue(entry.MeshIndex, out var slots)) continue;

                    float weight = Mathf.Clamp01(entry.Weight);
                    foreach (var slot in slots)
                    {
                        if (slot.Renderer == null) continue;
                        string path = UniGLTF.UnityExtensions.RelativePathFrom(
                            slot.Renderer, hierarchy.Root.transform);
                        bindings.Add(new MorphTargetBinding(path, slot.ShapeIndex, weight));
                    }
                }

                if (bindings.Count == 0)
                {
                    report.Warnings.Add(
                        $"表情 \"{expr.Name}\" に対応するブレンドシェイプが見つかりません"
                        + "（モーフの親メッシュが出力対象外の可能性があります）。");
                    continue;
                }

                var clip = ScriptableObject.CreateInstance<VRM10Expression>();
                clip.name = string.IsNullOrEmpty(expr.Name) ? "Expression" : expr.Name;
                clip.hideFlags = HideFlags.HideAndDontSave;
                clip.MorphTargetBindings = bindings.ToArray();
                _created.Add(clip);

                ExpressionPreset preset = ExpressionPreset.custom;
                if (settings.MapExpressionPresets)
                    preset = ResolvePreset(expr, usedPresets);

                if (preset != ExpressionPreset.custom && AssignPreset(target, preset, clip))
                {
                    usedPresets.Add(preset);
                }
                else
                {
                    // プリセットに載せられなかったものはカスタムへ。
                    // Custom は名前がキーになる（Vrm10Exporter.cs:953）ので一意にする。
                    clip.name = MakeUniqueName(clip.name, usedCustomNames);
                    target.CustomClips.Add(clip);
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// 表情名から VRM のプリセットを決める。決まらなければ custom。
        ///
        /// NameEnglish がプリセット名（happy / aa / blink …）と一致すればそれを使う。
        /// 一致しなければ PMX の標準モーフ名で引く。
        /// 既に埋まっているプリセットは先勝ちで、後から来たものは custom へ落とす。
        /// </summary>
        private static ExpressionPreset ResolvePreset(
            MorphExpression expr, HashSet<ExpressionPreset> used)
        {
            ExpressionPreset preset = ExpressionPreset.custom;

            if (!string.IsNullOrEmpty(expr.NameEnglish) &&
                Enum.TryParse<ExpressionPreset>(expr.NameEnglish.Trim(), true, out var byEnglish) &&
                byEnglish != ExpressionPreset.custom)
            {
                preset = byEnglish;
            }
            else if (!string.IsNullOrEmpty(expr.Name) &&
                     PresetByName.TryGetValue(expr.Name.Trim(), out var byName))
            {
                preset = byName;
            }

            if (preset == ExpressionPreset.custom) return ExpressionPreset.custom;
            if (used.Contains(preset)) return ExpressionPreset.custom;

            return preset;
        }

        /// <summary>PMX の標準モーフ名 → VRM プリセット。</summary>
        private static readonly Dictionary<string, ExpressionPreset> PresetByName =
            new Dictionary<string, ExpressionPreset>
            {
                { "あ",           ExpressionPreset.aa },
                { "い",           ExpressionPreset.ih },
                { "う",           ExpressionPreset.ou },
                { "え",           ExpressionPreset.ee },
                { "お",           ExpressionPreset.oh },
                { "まばたき",     ExpressionPreset.blink },
                { "ウィンク",     ExpressionPreset.blinkLeft },
                { "ウィンク右",   ExpressionPreset.blinkRight },
                { "笑い",         ExpressionPreset.happy },
                { "怒り",         ExpressionPreset.angry },
                { "困る",         ExpressionPreset.sad },
                { "リラックス",   ExpressionPreset.relaxed },
                { "驚き",         ExpressionPreset.surprised },
                { "上",           ExpressionPreset.lookUp },
                { "下",           ExpressionPreset.lookDown },
                { "左",           ExpressionPreset.lookLeft },
                { "右",           ExpressionPreset.lookRight },
                { "Neutral",      ExpressionPreset.neutral },
            };

        /// <summary>
        /// プリセット枠へ差し込む。既に埋まっていれば false を返す。
        /// VRM10ObjectExpression のフィールドは固定名なので switch で書く
        /// （反射で書くとメンバ名変更を静かに取りこぼす）。
        /// </summary>
        private static bool AssignPreset(
            VRM10ObjectExpression target, ExpressionPreset preset, VRM10Expression clip)
        {
            switch (preset)
            {
                case ExpressionPreset.happy:      if (target.Happy      != null) return false; target.Happy      = clip; return true;
                case ExpressionPreset.angry:      if (target.Angry      != null) return false; target.Angry      = clip; return true;
                case ExpressionPreset.sad:        if (target.Sad        != null) return false; target.Sad        = clip; return true;
                case ExpressionPreset.relaxed:    if (target.Relaxed    != null) return false; target.Relaxed    = clip; return true;
                case ExpressionPreset.surprised:  if (target.Surprised  != null) return false; target.Surprised  = clip; return true;
                case ExpressionPreset.aa:         if (target.Aa         != null) return false; target.Aa         = clip; return true;
                case ExpressionPreset.ih:         if (target.Ih         != null) return false; target.Ih         = clip; return true;
                case ExpressionPreset.ou:         if (target.Ou         != null) return false; target.Ou         = clip; return true;
                case ExpressionPreset.ee:         if (target.Ee         != null) return false; target.Ee         = clip; return true;
                case ExpressionPreset.oh:         if (target.Oh         != null) return false; target.Oh         = clip; return true;
                case ExpressionPreset.blink:      if (target.Blink      != null) return false; target.Blink      = clip; return true;
                case ExpressionPreset.blinkLeft:  if (target.BlinkLeft  != null) return false; target.BlinkLeft  = clip; return true;
                case ExpressionPreset.blinkRight: if (target.BlinkRight != null) return false; target.BlinkRight = clip; return true;
                case ExpressionPreset.lookUp:     if (target.LookUp     != null) return false; target.LookUp     = clip; return true;
                case ExpressionPreset.lookDown:   if (target.LookDown   != null) return false; target.LookDown   = clip; return true;
                case ExpressionPreset.lookLeft:   if (target.LookLeft   != null) return false; target.LookLeft   = clip; return true;
                case ExpressionPreset.lookRight:  if (target.LookRight  != null) return false; target.LookRight  = clip; return true;
                case ExpressionPreset.neutral:    if (target.Neutral    != null) return false; target.Neutral    = clip; return true;
                default: return false;
            }
        }

        // ================================================================
        // スプリングボーン
        // ================================================================

        /// <summary>
        /// SpringBone 付帯データを VRM のコンポーネントへ写す。
        ///
        /// 格納規約は MeshObject.cs「ボーン付帯データ格納規約」を正典とする。
        ///   ・コライダー … MeshObject.SpringBoneColliders（1ボーンに複数可）
        ///   ・ジョイント … MeshObject.SpringBoneJoint（非null＝揺れチェーンのメンバー）
        ///   ・チェーン   … MeshObject.SpringBoneChainRoot（非null＝チェーン起点）
        /// チェーンの形状はボーン階層＋SpringBoneJoint の有無から導出する
        /// （SpringBoneChainData.cs:15-20）。
        /// </summary>
        private void BuildSpringBones(
            ModelContext model, HierarchyBuildResult hierarchy,
            Vrm10Instance instance, AssembleReport report)
        {
            var boneTf = hierarchy.BoneTransformByIndex;
            if (boneTf.Count == 0) return;

            // ---- コライダー ----------------------------------------------
            // グループ名リストはモデルレベルに1つ。所属は index 参照。
            var groupNames = model.SpringBoneColliderGroupNames ?? new List<string>();
            var collidersByGroup = new Dictionary<int, List<VRM10SpringBoneCollider>>();
            int colliderCount = 0;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var list = mc.MeshObject?.SpringBoneColliders;
                if (list == null || list.Count == 0) continue;
                if (!boneTf.TryGetValue(i, out var tf) || tf == null) continue;

                foreach (var c in list)
                {
                    if (c == null) continue;

                    var comp = tf.gameObject.AddComponent<VRM10SpringBoneCollider>();
                    comp.ColliderType = ToColliderType(c.Shape);
                    comp.Offset = c.Offset;
                    comp.Radius = c.Radius;
                    comp.Tail   = c.Tail;
                    comp.Normal = c.Normal;
                    colliderCount++;

                    var groups = c.SpringBoneGroupIndices;
                    if (groups == null || groups.Count == 0) continue;

                    foreach (int g in groups)
                    {
                        if (g < 0) continue;
                        if (!collidersByGroup.TryGetValue(g, out var bucket))
                        {
                            bucket = new List<VRM10SpringBoneCollider>();
                            collidersByGroup[g] = bucket;
                        }
                        bucket.Add(comp);
                    }
                }
            }
            report.SpringBoneColliderCount = colliderCount;

            // ---- コライダーグループ --------------------------------------
            // グループ index → コンポーネント。ルートへまとめて付ける
            // （VRM10SpringBoneColliderGroup は付帯先を問わない）。
            var groupComp = new Dictionary<int, VRM10SpringBoneColliderGroup>();
            foreach (var kv in collidersByGroup)
            {
                var comp = hierarchy.Root.AddComponent<VRM10SpringBoneColliderGroup>();
                comp.Name = (kv.Key >= 0 && kv.Key < groupNames.Count)
                    ? groupNames[kv.Key]
                    : $"ColliderGroup_{kv.Key}";
                comp.Colliders = new List<VRM10SpringBoneCollider>(kv.Value);

                groupComp[kv.Key] = comp;
                instance.SpringBone.ColliderGroups.Add(comp);
            }

            // ---- ジョイント ----------------------------------------------
            var jointComp = new Dictionary<int, VRM10SpringBoneJoint>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var j = mc.MeshObject?.SpringBoneJoint;
                if (j == null) continue;
                if (!boneTf.TryGetValue(i, out var tf) || tf == null) continue;

                var comp = tf.gameObject.AddComponent<VRM10SpringBoneJoint>();
                comp.m_jointRadius    = j.HitRadius;
                comp.m_stiffnessForce = j.StiffnessForce;
                comp.m_gravityPower   = j.GravityPower;
                comp.m_gravityDir     = j.GravityDir;
                comp.m_dragForce      = j.DragForce;

                jointComp[i] = comp;
            }

            // ---- チェーン ------------------------------------------------
            // 親子表は HierarchyBuilder と同じものを使う（Depth 由来）。
            var parentIndices = MeshHierarchyOps.BuildParentIndicesFromDepth(model);
            var childrenOf = BuildChildrenTable(model, parentIndices);

            // ボーン名 → 索引（center 解決用。先勝ち）
            var boneIndexByName = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (!string.IsNullOrEmpty(mc.Name) && !boneIndexByName.ContainsKey(mc.Name))
                    boneIndexByName[mc.Name] = i;
            }

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var chain = mc.MeshObject?.SpringBoneChainRoot;
                if (chain == null) continue;

                if (!jointComp.ContainsKey(i))
                {
                    report.Warnings.Add(
                        $"揺れチェーン \"{ChainName(chain, mc)}\" のルート \"{mc.Name}\" に "
                        + "SpringBoneJoint がないため出力しません。");
                    continue;
                }

                // ルートから葉までの経路を列挙する。分岐は別チェーンとして出す
                // （VRM の joints は先頭以外が直前の子孫であることを要求するため）。
                var paths = new List<List<int>>();
                CollectJointPaths(i, childrenOf, jointComp, new List<int>(), paths);
                if (paths.Count == 0) continue;

                if (paths.Count > 1)
                {
                    report.Warnings.Add(
                        $"揺れチェーン \"{ChainName(chain, mc)}\" が {paths.Count} 本に分岐しています。"
                        + "VRM のチェーンは分岐できないため、経路ごとに別チェーンとして出力します。");
                }

                Transform center = null;
                if (!string.IsNullOrEmpty(chain.CenterBoneName) &&
                    boneIndexByName.TryGetValue(chain.CenterBoneName, out int centerIdx))
                {
                    boneTf.TryGetValue(centerIdx, out center);
                }

                for (int p = 0; p < paths.Count; p++)
                {
                    string name = ChainName(chain, mc);
                    if (paths.Count > 1) name = $"{name}#{p + 1}";

                    var spring = new Vrm10InstanceSpringBone.Spring(name) { Center = center };

                    foreach (int bi in paths[p])
                        if (jointComp.TryGetValue(bi, out var jc)) spring.Joints.Add(jc);

                    if (chain.SpringBoneColliderGroupIndices != null)
                    {
                        foreach (int g in chain.SpringBoneColliderGroupIndices)
                            if (groupComp.TryGetValue(g, out var gc)) spring.ColliderGroups.Add(gc);
                    }

                    if (spring.Joints.Count == 0) continue;

                    instance.SpringBone.Springs.Add(spring);
                    report.SpringCount++;
                }
            }
        }

        private static string ChainName(SpringBoneChainData chain, MeshContext mc)
            => !string.IsNullOrEmpty(chain.Name) ? chain.Name
             : (!string.IsNullOrEmpty(mc.Name) ? mc.Name : "Spring");

        /// <summary>親索引配列から子リストを作る（ボーンのみ）。</summary>
        private static Dictionary<int, List<int>> BuildChildrenTable(
            ModelContext model, int[] parentIndices)
        {
            var table = new Dictionary<int, List<int>>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;

                int parent = (parentIndices != null && i < parentIndices.Length)
                    ? parentIndices[i]
                    : mc.HierarchyParentIndex;
                if (parent < 0) continue;

                if (!table.TryGetValue(parent, out var list))
                {
                    list = new List<int>();
                    table[parent] = list;
                }
                list.Add(i);
            }
            return table;
        }

        /// <summary>
        /// current を起点に、SpringBoneJoint を持つ子孫だけを辿って
        /// ルート→葉の経路をすべて集める。分岐は経路ごとに分ける。
        /// </summary>
        private static void CollectJointPaths(
            int current, Dictionary<int, List<int>> childrenOf,
            Dictionary<int, VRM10SpringBoneJoint> jointComp,
            List<int> path, List<List<int>> result)
        {
            path.Add(current);

            var next = new List<int>();
            if (childrenOf.TryGetValue(current, out var children))
                foreach (int c in children)
                    if (jointComp.ContainsKey(c)) next.Add(c);

            if (next.Count == 0)
            {
                result.Add(new List<int>(path));
            }
            else
            {
                foreach (int c in next)
                    CollectJointPaths(c, childrenOf, jointComp, path, result);
            }

            path.RemoveAt(path.Count - 1);
        }

        /// <summary>PolyLing の形状 → VRM のコライダー種別。</summary>
        private static VRM10SpringBoneColliderTypes ToColliderType(SpringBoneColliderShape shape)
        {
            switch (shape)
            {
                case SpringBoneColliderShape.Capsule:       return VRM10SpringBoneColliderTypes.Capsule;
                case SpringBoneColliderShape.InsideSphere:  return VRM10SpringBoneColliderTypes.SphereInside;
                case SpringBoneColliderShape.InsideCapsule: return VRM10SpringBoneColliderTypes.CapsuleInside;
                case SpringBoneColliderShape.Plane:         return VRM10SpringBoneColliderTypes.Plane;
                default:                                    return VRM10SpringBoneColliderTypes.Sphere;
            }
        }

        // ================================================================
        // 必須ボーンの検査
        // ================================================================

        /// <summary>
        /// VRM 1.0 が必須とする humanBones のうち、未割当のものを返す。
        ///
        /// 【正典は VrmLib 側】一覧を自前で持たない。
        ///   VrmLib.HumanoidBones の各メンバに付いた BoneRequiredAttribute が正典で、
        ///   VrmLib.Model.CheckVrmHumanoid() も同じ属性から必須集合を導いている。
        /// </summary>
        private static VrmLib.HumanoidBones[] _requiredBonesCache;

        private static VrmLib.HumanoidBones[] GetRequiredHumanoidBones()
        {
            if (_requiredBonesCache != null) return _requiredBonesCache;

            var list = new List<VrmLib.HumanoidBones>();
            var type = typeof(VrmLib.HumanoidBones);

            foreach (VrmLib.HumanoidBones bone in Enum.GetValues(type))
            {
                var field = type.GetField(bone.ToString());
                if (field == null) continue;

                var attrs = field.GetCustomAttributes(typeof(VrmLib.BoneRequiredAttribute), false);
                if (attrs != null && attrs.Length > 0) list.Add(bone);
            }

            _requiredBonesCache = list.ToArray();
            return _requiredBonesCache;
        }

        /// <summary>
        /// 割当済みの humanName（HumanTrait.BoneName 形式）から、
        /// 不足している VRM 必須ボーンを列挙する。
        ///
        /// 【親指の段ずれ】
        ///   Unity: ThumbProximal / ThumbIntermediate / ThumbDistal
        ///   VRM  : thumbMetacarpal / thumbProximal   / thumbDistal
        ///   UniVRM の ModelExporter.cs:50-53 と同じ規則で読み替える。
        /// </summary>
        private static List<string> CollectMissingRequiredBones(HashSet<string> assignedTraits)
        {
            var assigned = new HashSet<VrmLib.HumanoidBones>();

            foreach (string trait in assignedTraits)
            {
                string compact = trait.Replace(" ", string.Empty);

                switch (compact)
                {
                    case "LeftThumbProximal":      assigned.Add(VrmLib.HumanoidBones.leftThumbMetacarpal);  continue;
                    case "LeftThumbIntermediate":  assigned.Add(VrmLib.HumanoidBones.leftThumbProximal);    continue;
                    case "RightThumbProximal":     assigned.Add(VrmLib.HumanoidBones.rightThumbMetacarpal); continue;
                    case "RightThumbIntermediate": assigned.Add(VrmLib.HumanoidBones.rightThumbProximal);   continue;
                }

                if (Enum.TryParse<VrmLib.HumanoidBones>(compact, true, out var bone) &&
                    bone != VrmLib.HumanoidBones.unknown)
                    assigned.Add(bone);
            }

            var missing = new List<string>();
            foreach (var b in GetRequiredHumanoidBones())
                if (!assigned.Contains(b)) missing.Add(b.ToString());
            return missing;
        }

        // ================================================================
        // 名前の一意化
        // ================================================================

        private static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Expression" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }
    }
}
