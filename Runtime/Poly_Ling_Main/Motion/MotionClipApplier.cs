// MotionClipApplier.cs
// 統合 MotionClipDTO を ModelContext のボーン・表情へ適用するアプライヤ（再生専用）。
//
// ■ 適用規約（V2a）
//   - boneName トラック: 統一 Unity 空間値に対し、旧 VMDApplier と同じ
//       ローカル軸補正 R^-1·Q·R（R = ctx.BoneModelRotation）を掛け、
//       "MotionClipVMD" レイヤーのデルタとして載せる（VMD 直接適用・リターゲットなし）。
//       値は既に Unity 化済みのため、ここで座標変換（Z 反転）は行わない。
//   - path / humanoid / muscles / body トラック: 検証済みの UnityClipApplier に委譲する
//       （二次骨＝path、baked 本体＝humanoid、リターゲットは外部 UnityBone CSV v2 の
//        ソース rest を用いる）。
//   - expressions: モデルの MorphExpression を名前（Name、無ければ NameEnglish）で引き、
//       頂点モーフの差分を基準メッシュの WorkingPositions へ重みを掛けて足す
//       （MorphPreviewState.Apply と同じ置き場所）。グループモーフは子を名前で辿る。
//       頂点モーフ以外の種類（ボーン・UV・材質など）は未対応として報告する。
//       provider は解決に使っていない（どの provider でも名前で引く）。
//
// ■ 補間
//   キーの評価は MotionCurveMath が正本。接線（tan）の無いキーは version 1 と同じ
//   線形（回転は Slerp）になる。
//   UnityClipApplier は自前で線形補間するため、委譲用ビューには「その時刻で評価済みの
//   キー 1 個」だけを載せる。ビューの器はクリップ設定・マッピング時に一度だけ作り、
//   ApplyFrame では値だけを書き換える。
//
// ■ マッスルと補助ボーンの競合
//   マッスル（または bakedBones）が駆動する Humanoid ボーンと同じノードへ解決される
//   path トラックは、二重に変換されるので委譲用ビューから外して適用しない。
//   外したトラックは BindingReport.Conflicts に載る。
//
// ■ PositionScale の適用範囲（注意）
//   PositionScale は boneName トラック（本クラスの ApplyBoneNameTrack）と、
//   path / humanoid トラック（_clip = UnityClipApplier へ委譲）の両方に同じ値が掛かる。
//   既定は 1。VMD 由来（PMX 単位）のクリップでは呼び出し側が
//   EditorState.PmxUnityRatio（既定 0.1）を設定しなければ位置が 10 倍になる。
//   MotionClipDTO は単位系を持たないため、本クラスでは自動判別できない。
//
// ■ 既知の問題（未対応・恒久メモ）
//   IK 未対応と付与親未評価。内容は下の「軸規約と IK の現状」に一本化した。
//   ここには書き写さない。
//   body（ルート）トラックは委譲先 UnityClipApplier.ApplyFrame が参照しないため、
//   本経路ではモデルへ適用されない（VRMA 書き出し経路 UnityClipRootMotion は別）。

//
// ================================================================
// ■ 軸規約と IK の現状（2026-09-07 実測。恒久メモ）
// ----------------------------------------------------------------
//   【旧記述の取り消し】
//   ここには以前「軸規約の変更に未追随。動作保証対象外」と書かれていたが、
//   その前提はもう成り立たない。PMXImporter.CalculateBoneModelRotation は
//   削除済みで、ボーンのレスト回転は全ボーン恒等に固定されている
//   （PMXImporter.cs の boneModelRotations と、同ファイルの
//    「ローカル軸の合成は廃止した」節）。
//   したがって R^-1·Q·R（R = ctx.BoneModelRotation）は恒等の共役、
//   すなわち素通りであり、「局所軸が X か Y か」で結果が変わることはない。
//   BoneModelRotation を非恒等へ戻さないこと。戻すとこの前提が崩れる。
//
//   【IK の収束（実測）】
//   __AちゃんH.pmx ＋ 左足ＩＫ に移動キーを持つ VMD で vmd_summary.csv を採取した。
//     左足ＩＫ  distEnd 0.05208
//     右足ＩＫ  distEnd 0.00571 （VMD にキー無し＝レスト）
//     髪ＩＫ    distEnd 0.00063
//   左足の 0.05208 は収束不良ではない。目標が脚の届く範囲の外にある。
//     股関節→目標 0.82780 / 最大リーチ 0.77776（太もも 0.37646 ＋ すね 0.40131）
//     届かない量 0.05004 が残差とほぼ一致し、収束後の伸展率は 99.73%。
//   右足も同じ関係（超過 0.00385 / 残差 0.00571）。このモデルはレスト姿勢の時点で
//   足ＩＫ が脚長よりわずかに遠い。
//   よって CCDIKSolver は正しく動いている。角度制限を外しても脚は伸びない。
//
//   【取り消した記述】
//   旧記述の「足ＩＫが収束しないフレームがある（左足首 max dist 1.14 /
//   12 of 50 サンプル）」は別モーションでの記録で、上記の測定では再現しなかった。
//   再現条件は未特定。再発したら、まず 股関節→目標 の距離と脚長を比べること。
//   届かないだけの場合は不具合ではない。
//
//   【残る未対応】
//     - 付与親（GrantParentIndex / GrantRate）は未評価。PMX の読み書きと取込では
//       保持されるが、姿勢適用側に評価コードが無い。
//     - 統合経路には IK が無い（MotionClipApplier に Solve 呼び出しなし）。
//
//   【IK が要るとき】
//   本クラスには CCDIKSolver.Solve の呼び出しが無い。VMD を読んでも
//   足ＩＫ・つま先ＩＫ・髪ＩＫは解かれない。IK が要る場合は VMDApplier 経路を使う。
// ================================================================

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    public class MotionClipApplier
    {
        private const string VmdLayerName = "MotionClipVMD";

        // path / humanoid / muscles / body は検証済みの UnityClipApplier に委譲する。
        private readonly UnityClipApplier _clip = new UnityClipApplier();

        // boneName 解決（モデル骨名 → MeshContextList インデックス）
        private ModelContext _mappedModel;
        private Dictionary<string, int> _boneNameToIndex = new Dictionary<string, int>();

        // 現在の DTO と、委譲用に構築した UnityClipDTO ビュー
        private MotionClipDTO _dto;
        private UnityClipDTO _clipView;

        // ビューの器（source → 評価済みキー 1 個）
        private sealed class ViewBone
        {
            public MotionTrackDTO    Src;
            public UnityBoneTrackDTO Dst;
            public UnityBoneKeyDTO   Key;
            public readonly float[] Pos = new float[3];
            public readonly float[] Rot = new float[4];
            public readonly float[] Scl = new float[3];
        }
        private sealed class ViewMuscle
        {
            public MotionScalarTrackDTO Src;
            public UnityMuscleTrackDTO  Dst;
            public UnityWeightKeyDTO    Key;
        }
        private readonly List<ViewBone>   _viewBones   = new List<ViewBone>();
        private readonly List<ViewBone>   _viewBaked   = new List<ViewBone>();
        private readonly List<ViewMuscle> _viewMuscles = new List<ViewMuscle>();
        private ViewBone _viewBody;
        private readonly float[] _bodyPos = new float[3];
        private readonly float[] _bodyRot = new float[4];

        // 競合で外した path トラック
        private readonly HashSet<MotionTrackDTO> _conflicted = new HashSet<MotionTrackDTO>();

        // 表情の結び付け
        private sealed class OffsetEntry
        {
            public int BaseIndex;
            public List<(int VertexIndex, Vector3 Offset)> Offsets;
            public float Weight;
        }
        private sealed class ExpressionBinding
        {
            public MotionExpressionTrackDTO Track;
            public readonly List<OffsetEntry> Entries = new List<OffsetEntry>();
        }
        private readonly List<ExpressionBinding> _expressionBindings = new List<ExpressionBinding>();
        private readonly HashSet<int> _touchedBases = new HashSet<int>();

        /// <summary>
        /// 表情適用で WorkingPositions を書き換えた基準メッシュの表示を更新する口。
        /// 通常は ToolContext.SyncMeshContextPositionsOnly を渡す。null なら表示は更新しない。
        /// </summary>
        public Action<MeshContext> SyncMeshPositions;

        /// <summary>位置スケール（Unity 空間値にそのまま乗算。既定 1）。</summary>
        public float PositionScale
        {
            get => _positionScale;
            set { _positionScale = value; _clip.PositionScale = value; }
        }
        private float _positionScale = 1f;

        /// <summary>直近の ApplyFrame で適用できたボーントラック数（表情は含めない）。</summary>
        public int MatchedTrackCount { get; private set; }

        /// <summary>直近の ApplyFrame で適用した表情トラック数。</summary>
        public int AppliedExpressionCount { get; private set; }

        /// <summary>ソース rest（バインドポーズ）読込済みなら true。</summary>
        public bool HasSourceRest => _clip.HasSourceRest;

        /// <summary>現在のクリップとモデルの結び付き状況。SetClip / BuildMapping のたびに作り直す。</summary>
        public MotionBindingReport BindingReport { get; private set; } = new MotionBindingReport();

        // ================================================================
        // 設定
        // ================================================================

        /// <summary>適用対象のクリップを設定し、結び付けと委譲用ビューを作り直す。</summary>
        public void SetClip(MotionClipDTO dto)
        {
            _dto = dto;
            RebuildBindings();
        }

        /// <summary>外部 UnityBone CSV v2（ソース rest）を読み込む。以後 humanoid はリターゲット適用。</summary>
        public int LoadSourceRestCsv(string csvText) => _clip.LoadSourceRestCsv(csvText);

        /// <summary>外部 UnityLimit CSV（マッスル可動域・実測）を委譲先へ読み込む。</summary>
        public int LoadMuscleLimitCsv(string csvText) => _clip.LoadMuscleLimitCsv(csvText);

        /// <summary>本体ボーンの適用方式（委譲先の設定）。</summary>
        public UnityClipApplier.BodySource BodyMode
        {
            get => _clip.BodyMode;
            set => _clip.BodyMode = value;
        }

        /// <summary>ソース rest を破棄。</summary>
        public void ClearSourceRest() => _clip.ClearSourceRest();

        /// <summary>UnityLimit CSV を破棄し、既定（CanonMuscleTable）へ戻す。</summary>
        public void ClearMuscleLimits() => _clip.ClearMuscleLimits();

        /// <summary>UnityLimit CSV を読み込んでいるか。</summary>
        public bool HasMuscleLimits => _clip.HasMuscleLimits;

        /// <summary>読み込んだ UnityLimit CSV が実測列を持つか。</summary>
        public bool HasMuscleMeasured => _clip.HasMuscleMeasured;

        /// <summary>BuildMapping で作った仮想骨格（VRMA 書き出しの骨格の元）。</summary>
        public UnityClipVirtualSkeleton Skeleton => _clip.Skeleton;

        /// <summary>
        /// 直近の ApplyFrame 時点のノード・ワールド行列（path / humanoid / muscles を当てたとき有効）。
        /// boneName だけのクリップでは false。そのときは VmdNodeWorldSampler でモデルから読む。
        /// </summary>
        public bool TryGetNodeWorldMatrix(int node, out Matrix4x4 world) => _clip.TryGetNodeWorldMatrix(node, out world);

        // ================================================================
        // マッピング
        // ================================================================

        public void BuildMapping(ModelContext model)
        {
            if (model == null) return;

            // 前のモデルの表情バッファは前のモデルで消す。索引は新しいモデルでは別物を指す。
            if (_mappedModel != null)
            {
                ClearExpressionBuffers(_mappedModel);
                _touchedBases.Clear();
                _expressionBindings.Clear();
            }

            _mappedModel = model;
            _boneNameToIndex.Clear();
            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;
                if (!_boneNameToIndex.ContainsKey(ctx.Name))
                    _boneNameToIndex[ctx.Name] = master;
            }

            _clip.BuildMapping(model);
            RebuildBindings();
        }

        /// <summary>トラックが現在のモデルで解決でき、かつ適用対象か（UI 表示用）。</summary>
        public bool IsTrackMatched(MotionTrackDTO track)
        {
            if (track == null) return false;
            switch (track.targetKind)
            {
                case "boneName": return _boneNameToIndex.ContainsKey(track.id ?? "");
                default:         return !_conflicted.Contains(track) && _clip.ResolveMasterIndex(track.id) >= 0;
            }
        }

        /// <summary>マッスル等との競合で適用から外した path トラックか。</summary>
        public bool IsTrackConflicted(MotionTrackDTO track) => track != null && _conflicted.Contains(track);

        // ================================================================
        // 結び付け
        // ================================================================

        private void RebuildBindings()
        {
            if (_mappedModel != null) ClearExpressionBuffers(_mappedModel);
            _conflicted.Clear();
            _expressionBindings.Clear();
            _touchedBases.Clear();

            var rep = new MotionBindingReport();
            BindingReport = rep;
            if (_dto == null) { ClearView(); return; }

            // ---- マッスル名（モデル不要）----
            var muscleNames = new HashSet<string>(HumanTrait.MuscleName, StringComparer.Ordinal);
            if (_dto.muscles != null)
            {
                foreach (var m in _dto.muscles)
                {
                    if (m == null || string.IsNullOrEmpty(m.name)) continue;
                    if (IsRootCurveName(m.name)) continue;
                    rep.MuscleTotal++;
                    if (muscleNames.Contains(m.name)) rep.MuscleKnown++;
                    else rep.Add(MotionFileCodes.UnknownMuscle, m.name);
                }
            }

            rep.HasModel = _mappedModel != null;
            if (_mappedModel != null)
            {
                var driven = ComputeDrivenNodes();

                if (_dto.bones != null)
                {
                    foreach (var t in _dto.bones)
                    {
                        if (t == null) continue;
                        if (t.targetKind == "boneName")
                        {
                            rep.BoneTotal++;
                            if (_boneNameToIndex.ContainsKey(t.id ?? "")) rep.BoneResolved++;
                            else rep.Add(MotionFileCodes.UnknownBone, t.id);
                            continue;
                        }
                        rep.BoneTotal++;
                        int node = _clip.ResolveNode(t.id);
                        if (node < 0) { rep.Add(MotionFileCodes.UnknownBone, t.id); continue; }
                        if (driven.Contains(node))
                        {
                            _conflicted.Add(t);
                            rep.Add(MotionFileCodes.AuxiliaryBoneConflict,
                                $"{t.id} → {_clip.NodeName(node)} はマッスルでも駆動されるため適用しません");
                            continue;
                        }
                        rep.BoneResolved++;
                    }
                }

                if (_dto.bakedBones != null)
                {
                    foreach (var t in _dto.bakedBones)
                    {
                        if (t == null) continue;
                        rep.BoneTotal++;
                        if (_clip.ResolveNode(t.id) >= 0) rep.BoneResolved++;
                        else rep.Add(MotionFileCodes.UnknownBone, t.id);
                    }
                }

                BuildExpressionBindings(_mappedModel, rep);
            }
            else if (_dto.expressions != null)
            {
                foreach (var e in _dto.expressions) if (e != null) rep.ExpressionTotal++;
            }

            BuildView();
        }

        private static bool IsRootCurveName(string name)
            => name == UnityClipRootMotion.NameTx || name == UnityClipRootMotion.NameTy || name == UnityClipRootMotion.NameTz
            || name == UnityClipRootMotion.NameQx || name == UnityClipRootMotion.NameQy
            || name == UnityClipRootMotion.NameQz || name == UnityClipRootMotion.NameQw;

        // マッスルまたは bakedBones が駆動する Humanoid ボーンのノード集合。
        // ノードの引き方は UnityClipApplier.ApplySelfMuscle と同じ（名前の空白を抜いて、だめなら空白入り）。
        private HashSet<int> ComputeDrivenNodes()
        {
            var set = new HashSet<int>();
            if (_dto == null) return set;

            if (_dto.muscles != null && _dto.muscles.Count > 0)
            {
                var present = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in _dto.muscles) if (m != null && !string.IsNullOrEmpty(m.name)) present.Add(m.name);

                var names = HumanTrait.MuscleName;
                for (int bi = 0; bi < HumanTrait.BoneCount; bi++)
                {
                    bool drivenBone = false;
                    for (int dof = 0; dof < 3 && !drivenBone; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi >= 0 && mi < names.Length && present.Contains(names[mi])) drivenBone = true;
                    }
                    if (!drivenBone) continue;

                    string boneName = HumanTrait.BoneName[bi];
                    int n = _clip.ResolveNode(boneName.Replace(" ", string.Empty));
                    if (n < 0) n = _clip.ResolveNode(boneName);
                    if (n >= 0) set.Add(n);
                }
            }

            if (_dto.bakedBones != null)
                foreach (var t in _dto.bakedBones)
                {
                    if (t == null) continue;
                    int n = _clip.ResolveNode(t.id);
                    if (n >= 0) set.Add(n);
                }

            return set;
        }

        private void BuildExpressionBindings(ModelContext model, MotionBindingReport rep)
        {
            if (_dto.expressions == null) return;

            foreach (var tr in _dto.expressions)
            {
                if (tr == null) continue;
                rep.ExpressionTotal++;

                var expr = FindExpression(model, tr.name);
                if (expr == null)
                {
                    rep.Add(MotionFileCodes.UnknownExpression, $"{tr.name}（同じ名前のモーフがありません）");
                    continue;
                }

                var binding = new ExpressionBinding { Track = tr };
                var visiting = new HashSet<MorphExpression>();
                CollectOffsets(model, expr, 1f, binding.Entries, visiting);
                if (binding.Entries.Count == 0)
                {
                    rep.Add(MotionFileCodes.UnknownExpression, $"{tr.name}（頂点モーフを含みません: {expr.Type}）");
                    continue;
                }

                foreach (var e in binding.Entries) _touchedBases.Add(e.BaseIndex);
                _expressionBindings.Add(binding);
                rep.ExpressionResolved++;
            }
        }

        private static MorphExpression FindExpression(ModelContext model, string name)
        {
            if (model?.MorphExpressions == null || string.IsNullOrEmpty(name)) return null;
            foreach (var e in model.MorphExpressions)
                if (e != null && string.Equals(e.Name, name, StringComparison.Ordinal)) return e;
            foreach (var e in model.MorphExpressions)
                if (e != null && string.Equals(e.NameEnglish, name, StringComparison.Ordinal)) return e;
            return null;
        }

        // 頂点モーフは差分を控え、グループモーフは子を名前で辿る（循環は打ち切る）。
        private static void CollectOffsets(ModelContext model, MorphExpression expr, float weight,
            List<OffsetEntry> outEntries, HashSet<MorphExpression> visiting)
        {
            if (expr == null || !visiting.Add(expr)) return;
            try
            {
                if (expr.Type == MorphType.Group)
                {
                    if (expr.GroupChildren == null) return;
                    foreach (var child in expr.GroupChildren)
                        CollectOffsets(model, FindExpression(model, child.Name), weight * child.Weight, outEntries, visiting);
                    return;
                }
                if (expr.Type != MorphType.Vertex) return;

                foreach (var (morphIndex, baseIndex, morphCtx, baseCtx, w) in MorphPreviewState.BuildMorphBasePairs(model, expr))
                {
                    var offsets = morphCtx.GetMorphOffsets();
                    if (offsets == null || offsets.Count == 0) continue;
                    outEntries.Add(new OffsetEntry { BaseIndex = baseIndex, Offsets = offsets, Weight = weight * w });
                }
            }
            finally
            {
                visiting.Remove(expr);
            }
        }

        // ================================================================
        // 委譲用ビュー
        // ================================================================

        private void ClearView()
        {
            _viewBones.Clear(); _viewBaked.Clear(); _viewMuscles.Clear();
            _viewBody = null;
            _clipView = null;
        }

        private void BuildView()
        {
            ClearView();
            var view = new UnityClipDTO();
            _clipView = view;
            if (_dto == null) return;

            view.name      = _dto.name;
            view.frameRate = _dto.frameRate > 0f ? _dto.frameRate : 30f;
            view.loop      = _dto.loop;

            // 二次骨（path のみ。競合で外したものは載せない）
            if (_dto.bones != null)
                foreach (var t in _dto.bones)
                    if (t != null && t.targetKind == "path" && !_conflicted.Contains(t))
                        _viewBones.Add(MakeViewBone(t, view.bones));

            // baked 本体（humanoid）
            if (_dto.bakedBones != null)
                foreach (var t in _dto.bakedBones)
                    if (t != null)
                        _viewBaked.Add(MakeViewBone(t, view.bakedBones));

            // マッスル
            if (_dto.muscles != null)
            {
                foreach (var m in _dto.muscles)
                {
                    if (m == null) continue;
                    var vm = new ViewMuscle
                    {
                        Src = m,
                        Dst = new UnityMuscleTrackDTO { name = m.name },
                        Key = new UnityWeightKeyDTO(),
                    };
                    if (m.keys != null && m.keys.Count > 0) vm.Dst.w.Add(vm.Key);
                    view.muscles.Add(vm.Dst);
                    _viewMuscles.Add(vm);
                }
            }

            // ルート（body）
            if (_dto.body != null && _dto.body.keys != null && _dto.body.keys.Count > 0)
            {
                view.body = new UnityBodyTrackDTO();
                view.body.keys.Add(new UnityBodyKeyDTO());
            }

            view.clipType = (view.muscles.Count > 0 || view.bakedBones.Count > 0) ? "Humanoid" : "Generic";
        }

        private static ViewBone MakeViewBone(MotionTrackDTO src, List<UnityBoneTrackDTO> into)
        {
            var vb = new ViewBone
            {
                Src = src,
                Dst = new UnityBoneTrackDTO { path = src.id },
                Key = new UnityBoneKeyDTO(),
            };
            if (src.keys != null && src.keys.Count > 0) vb.Dst.keys.Add(vb.Key);
            into.Add(vb.Dst);
            return vb;
        }

        /// <summary>読み込んだクリップがマッスルのトラックを 1 本以上持つか。</summary>
        public bool HasMuscleTracks
        {
            get
            {
                if (_dto?.muscles == null) return false;
                foreach (var m in _dto.muscles)
                    if (m != null && !string.IsNullOrEmpty(m.name) && m.keys != null && m.keys.Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// 読み込んだクリップのマッスルとルート（body）を timeSec で評価して into に入れる（配信用）。
        /// 評価は ApplyFrame と同じ MotionCurveMath。indexOf はマッスル名 → 番号（HumanTrait.MuscleName の添字）、
        /// count はマッスル数。トラックの無いマッスルは Valid=false。マッスルのトラックが無ければ false。
        /// </summary>
        public bool TrySampleMuscleFrame(float timeSec, IReadOnlyDictionary<string, int> indexOf, int count, MotionLiveFrame into)
        {
            if (into == null || indexOf == null || !HasMuscleTracks) return false;

            into.EnsureCount(count);
            Array.Clear(into.Muscles, 0, count);
            Array.Clear(into.Valid, 0, count);
            foreach (var m in _dto.muscles)
            {
                if (m == null || string.IsNullOrEmpty(m.name) || m.keys == null || m.keys.Count == 0) continue;
                if (!indexOf.TryGetValue(m.name, out int i) || i < 0 || i >= count) continue;
                into.Muscles[i] = MotionCurveMath.EvaluateScalar(m, timeSec);
                into.Valid[i]   = true;
            }

            into.HasRoot = false;
            if (_dto.body != null
                && MotionCurveMath.TryEvaluateVector3(_dto.body, MotionCurveMath.VectorChannel.Position, timeSec, out var p)
                && MotionCurveMath.TryEvaluateRotation(_dto.body, timeSec, out var q))
            {
                into.HasRoot = true;
                into.RootT   = p;
                into.RootQ   = q;
            }
            into.Time = timeSec;
            return true;
        }

        // ビューのキーを timeSec の評価値へ書き換える。
        private void UpdateView(float timeSec)
        {
            foreach (var vb in _viewBones) UpdateViewBone(vb, timeSec);
            foreach (var vb in _viewBaked) UpdateViewBone(vb, timeSec);

            foreach (var vm in _viewMuscles)
            {
                vm.Key.t = timeSec;
                vm.Key.v = MotionCurveMath.EvaluateScalar(vm.Src, timeSec);
            }

            if (_clipView?.body != null && _dto.body != null)
            {
                var key = _clipView.body.keys[0];
                key.t = timeSec;
                key.pos = MotionCurveMath.TryEvaluateVector3(_dto.body, MotionCurveMath.VectorChannel.Position, timeSec, out var p)
                    ? Fill(_bodyPos, p) : null;
                key.rot = MotionCurveMath.TryEvaluateRotation(_dto.body, timeSec, out var q)
                    ? Fill(_bodyRot, q) : null;
            }
        }

        private static void UpdateViewBone(ViewBone vb, float timeSec)
        {
            if (vb.Dst.keys.Count == 0) return;
            var k = vb.Key;
            k.t = timeSec;
            k.pos = MotionCurveMath.TryEvaluateVector3(vb.Src, MotionCurveMath.VectorChannel.Position, timeSec, out var p) ? Fill(vb.Pos, p) : null;
            k.rot = MotionCurveMath.TryEvaluateRotation(vb.Src, timeSec, out var q) ? Fill(vb.Rot, q) : null;
            k.scl = MotionCurveMath.TryEvaluateVector3(vb.Src, MotionCurveMath.VectorChannel.Scale, timeSec, out var s) ? Fill(vb.Scl, s) : null;
        }

        private static float[] Fill(float[] a, Vector3 v)    { a[0] = v.x; a[1] = v.y; a[2] = v.z; return a; }
        private static float[] Fill(float[] a, Quaternion q) { a[0] = q.x; a[1] = q.y; a[2] = q.z; a[3] = q.w; return a; }

        private static bool HasClipViewContent(UnityClipDTO view)
        {
            if (view == null) return false;
            return (view.bones != null && view.bones.Count > 0)
                || (view.bakedBones != null && view.bakedBones.Count > 0)
                || (view.muscles != null && view.muscles.Count > 0)
                || (view.body != null && view.body.keys != null && view.body.keys.Count > 0);
        }

        // ================================================================
        // 適用
        // ================================================================

        public void ApplyFrame(ModelContext model, float timeSec)
        {
            if (model == null || _dto == null) return;
            if (_mappedModel != model) BuildMapping(model);

            int matched = 0;

            // boneName（VMD 直接適用）
            if (_dto.bones != null)
                foreach (var track in _dto.bones)
                    if (track != null && track.targetKind == "boneName")
                        matched += ApplyBoneNameTrack(model, track, timeSec);

            // path / humanoid / muscles / body（UnityClipApplier に委譲）
            if (HasClipViewContent(_clipView))
            {
                UpdateView(timeSec);
                _clip.ApplyFrame(model, _clipView, timeSec);   // 内部で ComputeWorldMatrices
                matched += _clip.MatchedTrackCount;
            }
            else
            {
                model.ComputeWorldMatrices();
            }

            MatchedTrackCount = matched;
            AppliedExpressionCount = ApplyExpressions(model, timeSec);
        }

        // boneName トラックを timeSec で評価し、R^-1·Q·R 補正を掛けてデルタ適用。
        private int ApplyBoneNameTrack(ModelContext model, MotionTrackDTO track, float timeSec)
        {
            if (track.keys == null || track.keys.Count == 0) return 0;
            if (!_boneNameToIndex.TryGetValue(track.id ?? "", out int master)) return 0;
            if (master < 0 || master >= model.MeshContextList.Count) return 0;
            var ctx = model.MeshContextList[master];
            if (ctx == null) return 0;

            if (ctx.BonePoseData == null)
            {
                ctx.BonePoseData = new BonePoseData();
                ctx.BonePoseData.IsActive = true;
            }

            Vector3 pos = MotionCurveMath.TryEvaluateVector3(track, MotionCurveMath.VectorChannel.Position, timeSec, out var p)
                ? p : Vector3.zero;
            Quaternion rot = MotionCurveMath.TryEvaluateRotation(track, timeSec, out var q)
                ? q : Quaternion.identity;

            if (!Mathf.Approximately(PositionScale, 1f))
                pos *= PositionScale;

            // V2a: ローカル軸補正 R^-1·Q·R（R = BoneModelRotation）。
            Quaternion modelRot = ctx.BoneModelRotation;
            if (modelRot != Quaternion.identity)
            {
                Quaternion inv = Quaternion.Inverse(modelRot);
                rot = inv * rot * modelRot;
                pos = inv * pos;
            }

            ctx.BonePoseData.SetLayer(VmdLayerName, pos, rot);
            return 1;
        }

        // 表情: 触る基準メッシュの WorkingPositions を 0 にしてから、全表情の差分を足す。
        private int ApplyExpressions(ModelContext model, float timeSec)
        {
            if (_expressionBindings.Count == 0) return 0;

            foreach (int baseIndex in _touchedBases)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                var mo = baseCtx?.MeshObject;
                if (mo == null) continue;
                if (baseCtx.WorkingPositions == null || baseCtx.WorkingPositions.Length != mo.VertexCount)
                    baseCtx.WorkingPositions = new Vector3[mo.VertexCount];
                else
                    Array.Clear(baseCtx.WorkingPositions, 0, baseCtx.WorkingPositions.Length);
            }

            int applied = 0;
            foreach (var b in _expressionBindings)
            {
                float w = MotionCurveMath.EvaluateScalar(b.Track, timeSec);
                applied++;
                if (w == 0f) continue;

                foreach (var e in b.Entries)
                {
                    var wp = model.GetMeshContext(e.BaseIndex)?.WorkingPositions;
                    if (wp == null) continue;
                    float k = e.Weight * w;
                    foreach (var (vi, off) in e.Offsets)
                        if (vi >= 0 && vi < wp.Length) wp[vi] += off * k;
                }
            }

            if (SyncMeshPositions != null)
                foreach (int baseIndex in _touchedBases)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject != null) SyncMeshPositions(baseCtx);
                }

            return applied;
        }

        private void ClearExpressionBuffers(ModelContext model)
        {
            if (model == null) return;
            foreach (int baseIndex in _touchedBases)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx == null || baseCtx.WorkingPositions == null) continue;
                baseCtx.WorkingPositions = null;
                if (baseCtx.MeshObject != null) SyncMeshPositions?.Invoke(baseCtx);
            }
        }

        // ================================================================
        // リセット
        // ================================================================

        public void ResetAllBones(ModelContext model)
        {
            if (model == null) return;
            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                ctx?.BonePoseData?.ClearLayer(VmdLayerName);
            }
            ClearExpressionBuffers(model);
            _clip.ResetAllBones(model);   // 内部で ComputeWorldMatrices
            model.ComputeWorldMatrices();
        }
    }

    // ================================================================
    // 結び付き状況
    // ================================================================

    /// <summary>クリップとモデルの結び付き状況（解決数・未解決・競合）。</summary>
    public sealed class MotionBindingReport
    {
        /// <summary>モデルに対して解決したか（false ならボーン・表情の数は数えていない）。</summary>
        public bool HasModel;

        public int MuscleTotal, MuscleKnown;
        public int ExpressionTotal, ExpressionResolved;
        public int BoneTotal, BoneResolved;

        /// <summary>未解決・競合の一覧（Code = MotionFileCodes のいずれか）。</summary>
        public readonly List<MotionFileIssue> Issues = new List<MotionFileIssue>();

        public void Add(string code, string message)
            => Issues.Add(new MotionFileIssue { Code = code, Message = message, IsError = false });

        public int Count(string code)
        {
            int n = 0;
            foreach (var i in Issues) if (i.Code == code) n++;
            return n;
        }

        /// <summary>要約と、最大 max 件の項目を改行区切りで返す。</summary>
        public string ToText(int max)
        {
            var sb = new StringBuilder();
            sb.Append("マッスル: ").Append(MuscleKnown).Append('/').Append(MuscleTotal);
            if (HasModel)
            {
                sb.Append("  表情: ").Append(ExpressionResolved).Append('/').Append(ExpressionTotal);
                sb.Append("  ボーン: ").Append(BoneResolved).Append('/').Append(BoneTotal);
            }
            else
            {
                sb.Append("  （モデル未読込のため表情・ボーンは未解決）");
            }

            int shown = 0;
            foreach (var i in Issues)
            {
                if (shown >= max) { sb.Append("\n…ほか ").Append(Issues.Count - shown).Append(" 件"); break; }
                sb.Append('\n').Append(i.Code).Append(": ").Append(i.Message);
                shown++;
            }
            return sb.ToString();
        }
    }
}
