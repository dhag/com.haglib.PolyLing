// MotionClipApplier.cs
// 統合 MotionClipDTO を ModelContext のボーンへ適用するアプライヤ（再生専用）。
//
// ■ 適用規約（V2a）
//   - boneName トラック: 統一 Unity 空間値に対し、旧 VMDApplier と同じ
//       ローカル軸補正 R^-1·Q·R（R = ctx.BoneModelRotation）を掛け、
//       "MotionClipVMD" レイヤーのデルタとして載せる（VMD 直接適用・リターゲットなし）。
//       値は既に Unity 化済みのため、ここで座標変換（Z 反転）は行わない。
//   - path / humanoid / muscles / body トラック: 検証済みの UnityClipApplier に委譲する
//       （二次骨＝path、baked 本体＝humanoid、リターゲットは外部 UnityBone CSV v2 の
//        ソース rest を用いる）。委譲用の UnityClipDTO ビューはクリップ設定時に一度だけ構築する。
//
// ■ 座標変換は新規に足さない。回転補間 = Slerp、位置補間 = Lerp（接線は保持しない）。
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

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
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

        /// <summary>位置スケール（Unity 空間値にそのまま乗算。既定 1）。</summary>
        public float PositionScale
        {
            get => _positionScale;
            set { _positionScale = value; _clip.PositionScale = value; }
        }
        private float _positionScale = 1f;

        /// <summary>直近の ApplyFrame で適用できたトラック数。</summary>
        public int MatchedTrackCount { get; private set; }

        /// <summary>ソース rest（バインドポーズ）読込済みなら true。</summary>
        public bool HasSourceRest => _clip.HasSourceRest;

        // ================================================================
        // 設定
        // ================================================================

        /// <summary>適用対象のクリップを設定し、委譲用ビューを構築する。</summary>
        public void SetClip(MotionClipDTO dto)
        {
            _dto = dto;
            _clipView = BuildClipView(dto);
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

        // ================================================================
        // マッピング
        // ================================================================

        public void BuildMapping(ModelContext model)
        {
            if (model == null) return;

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
        }

        /// <summary>トラックが現在のモデルで解決できるか（UI 表示用）。</summary>
        public bool IsTrackMatched(MotionTrackDTO track)
        {
            if (track == null) return false;
            switch (track.targetKind)
            {
                case "boneName": return _boneNameToIndex.ContainsKey(track.id ?? "");
                default:         return _clip.ResolveMasterIndex(track.id) >= 0;
            }
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
                _clip.ApplyFrame(model, _clipView, timeSec);   // 内部で ComputeWorldMatrices
                matched += _clip.MatchedTrackCount;
            }
            else
            {
                model.ComputeWorldMatrices();
            }

            MatchedTrackCount = matched;
        }

        // boneName トラックを timeSec でサンプルし、R^-1·Q·R 補正を掛けてデルタ適用。
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

            Vector3 pos = SamplePosition(track, timeSec) ?? Vector3.zero;
            Quaternion rot = SampleRotation(track, timeSec) ?? Quaternion.identity;

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
            _clip.ResetAllBones(model);   // 内部で ComputeWorldMatrices
            model.ComputeWorldMatrices();
        }

        // ================================================================
        // 委譲用 UnityClipDTO ビュー構築
        // ================================================================

        private static UnityClipDTO BuildClipView(MotionClipDTO dto)
        {
            var view = new UnityClipDTO();
            if (dto == null) return view;

            view.name      = dto.name;
            view.frameRate = dto.frameRate > 0f ? dto.frameRate : 30f;
            view.loop      = dto.loop;

            // 二次骨（path のみ）
            if (dto.bones != null)
                foreach (var t in dto.bones)
                    if (t != null && t.targetKind == "path")
                        view.bones.Add(ToUnityTrack(t.id, t.keys));

            // baked 本体（humanoid）
            if (dto.bakedBones != null)
                foreach (var t in dto.bakedBones)
                    if (t != null)
                        view.bakedBones.Add(ToUnityTrack(t.id, t.keys));

            // マッスル
            if (dto.muscles != null)
            {
                foreach (var m in dto.muscles)
                {
                    if (m == null) continue;
                    var mt = new UnityMuscleTrackDTO { name = m.name };
                    if (m.keys != null)
                        foreach (var k in m.keys)
                            if (k != null) mt.w.Add(new UnityWeightKeyDTO { t = k.t, v = k.v });
                    view.muscles.Add(mt);
                }
            }

            // ルート（body）
            if (dto.body != null && dto.body.keys != null && dto.body.keys.Count > 0)
            {
                var body = new UnityBodyTrackDTO();
                foreach (var k in dto.body.keys)
                    if (k != null) body.keys.Add(new UnityBodyKeyDTO { t = k.t, pos = k.pos, rot = k.rot });
                view.body = body;
            }

            view.clipType = (view.muscles.Count > 0 || view.bakedBones.Count > 0) ? "Humanoid" : "Generic";
            return view;
        }

        private static UnityBoneTrackDTO ToUnityTrack(string path, List<MotionKeyDTO> keys)
        {
            var track = new UnityBoneTrackDTO { path = path };
            if (keys != null)
                foreach (var k in keys)
                    if (k != null)
                        track.keys.Add(new UnityBoneKeyDTO { t = k.t, pos = k.pos, rot = k.rot, scl = k.scl });
            return track;
        }

        private static bool HasClipViewContent(UnityClipDTO view)
        {
            if (view == null) return false;
            return (view.bones != null && view.bones.Count > 0)
                || (view.bakedBones != null && view.bakedBones.Count > 0)
                || (view.muscles != null && view.muscles.Count > 0)
                || (view.body != null && view.body.keys != null && view.body.keys.Count > 0);
        }

        // ================================================================
        // サンプリング（スパースキー・線形補間）
        // ================================================================

        private static Vector3? SamplePosition(MotionTrackDTO track, float timeSec)
        {
            MotionKeyDTO prev = null, next = null;
            foreach (var key in track.keys)
            {
                if (key == null || key.pos == null || key.pos.Length < 3) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return null;
            if (prev == null) return ToVec3(next.pos);
            if (next == null) return ToVec3(prev.pos);
            if (prev.t == next.t) return ToVec3(prev.pos);
            float w = (timeSec - prev.t) / (next.t - prev.t);
            return Vector3.Lerp(ToVec3(prev.pos), ToVec3(next.pos), w);
        }

        private static Quaternion? SampleRotation(MotionTrackDTO track, float timeSec)
        {
            MotionKeyDTO prev = null, next = null;
            foreach (var key in track.keys)
            {
                if (key == null || key.rot == null || key.rot.Length < 4) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return null;
            if (prev == null) return ToQuat(next.rot);
            if (next == null) return ToQuat(prev.rot);
            if (prev.t == next.t) return ToQuat(prev.rot);
            float w = (timeSec - prev.t) / (next.t - prev.t);
            return Quaternion.Slerp(ToQuat(prev.rot), ToQuat(next.rot), w);
        }

        private static Vector3 ToVec3(float[] a) => new Vector3(a[0], a[1], a[2]);
        private static Quaternion ToQuat(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);
    }
}
