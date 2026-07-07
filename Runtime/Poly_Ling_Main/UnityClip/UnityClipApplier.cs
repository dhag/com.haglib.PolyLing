// UnityClipApplier.cs
// UnityClipDTO（Generic）を ModelContext のボーンへ適用するアプライヤ。
//
// ■ 仕様（UnityClipDTO 準拠）
//   - 値はすべて Unity 左手系。AnimationClip 由来のため座標変換は行わない
//     （VMD のような右手系→左手系変換は不要）。
//   - Generic の bones（Transform パス階層）のみ対応。Humanoid（muscles/body）は無視。
//   - スパースキーをキー間で線形補間（pos=Lerp / rot=Slerp）。接線は保持しない。
//   - scl は v1 では未適用。
//
// ■ マッピング（対応表使用）
//   Transform パス末尾（Unity 名）→ モデルボーン名 の対応は
//   HumanoidBoneMapping.EmbeddedMapping（CSV由来）で解決する。
//   AutoMapFromEmbeddedCSV でモデルのボーン名リストに対して一括構築する。
//
// ■ 適用（BonePoseData デルタ層）
//   MeshContext.LocalMatrix = BoneTransform(ベース) × BonePoseData.LocalMatrix(デルタ)。
//   clip の絶対ローカルを LocalMatrix に一致させるため、
//   delta = BoneTransform^-1 × clipLocal を "UnityClip" レイヤーに設定する。
//   （VMD と同じ層機構。ResetAllBones でレイヤーを消せば復帰する。）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip
{
    public class UnityClipApplier
    {
        private const string LayerName = "UnityClip";

        // ================================================================
        // 本体ボーンの適用方式は2種類を実装している（コード切替。public 化しない）:
        //   (a) UseBakedBones = true  … 拡張Aで焼いた dto.bakedBones
        //       （HumanBodyBones の localRotation）をそのまま適用する。既定。
        //   (b) UseBakedBones = false … dto.muscles（生マッスル）から PolyLing 側で
        //       ローカル回転を近似再構成して適用する（HumanTrait の min/max と DoF を使用）。
        //       pre/post 回転・sign を省く近似。精度は Unity 実測前提。
        // 二次骨（dto.bones：袖/髪/スカート等）は、どちらの方式でも常時適用する。
        // 切替は下記フラグの書き換えで行う。
        // ================================================================
        private const bool UseBakedBones = true;

        // マッピング状態
        private ModelContext _mappedModel;
        private HumanoidBoneMapping _mapping;          // Unity名 → boneNames のインデックス
        private List<int> _boneMasterIndices;          // boneNames のインデックス → MeshContextList インデックス
        private List<string> _boneNames;               // モデルボーン名（列挙順）

        /// <summary>位置スケール（Unity 空間値にそのまま乗算。既定 1）。</summary>
        public float PositionScale { get; set; } = 1f;

        /// <summary>対応表で解決できたトラック数（直近の ApplyFrame 時）。</summary>
        public int MatchedTrackCount { get; private set; }

        // ================================================================
        // マッピング構築
        // ================================================================

        public void BuildMapping(ModelContext model)
        {
            if (model == null) return;

            _mappedModel = model;
            _boneNames = new List<string>();
            _boneMasterIndices = new List<int>();

            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;
                _boneNames.Add(ctx.Name);
                _boneMasterIndices.Add(master);
            }

            _mapping = new HumanoidBoneMapping();
            _mapping.AutoMapFromEmbeddedCSV(_boneNames, fuzzyMatch: true);
        }

        /// <summary>Transform パス末尾（Unity 名）→ MeshContextList インデックス。無ければ -1。</summary>
        public int ResolveMasterIndex(string path)
        {
            if (_mapping == null || _boneMasterIndices == null) return -1;
            string unityName = LastSegment(path);
            if (string.IsNullOrEmpty(unityName)) return -1;

            // 1) 対応表（Unity名キー）で解決
            int k = _mapping.Get(unityName);
            // 2) フォールバック: 末尾名を直接エイリアスとしてモデルボーン名にあいまい照合
            if (k < 0)
                k = HumanoidBoneMapping.FindBoneByAliases(_boneNames, new List<string> { unityName }, fuzzyMatch: true);

            if (k < 0 || k >= _boneMasterIndices.Count) return -1;
            return _boneMasterIndices[k];
        }

        // ================================================================
        // 適用
        // ================================================================

        public void ApplyFrame(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (model == null || clip == null) return;
            if (_mappedModel != model || _mapping == null) BuildMapping(model);

            int matched = 0;

            // 二次骨（袖/髪/スカート等）: どちらの方式でも常時適用
            if (clip.bones != null)
                foreach (var track in clip.bones)
                    matched += ApplyTrackAt(model, track, timeSec);

            // 本体ボーン: (a) 焼いた使用 / (b) 自前実装（近似）
            if (UseBakedBones)
            {
                if (clip.bakedBones != null)
                    foreach (var track in clip.bakedBones)
                        matched += ApplyTrackAt(model, track, timeSec);
            }
            else
            {
                matched += ApplySelfMuscle(model, clip, timeSec);
            }

            MatchedTrackCount = matched;
            model.ComputeWorldMatrices();
        }

        // 1 トラックを timeSec でサンプルして適用。適用できたら 1。
        private int ApplyTrackAt(ModelContext model, UnityBoneTrackDTO track, float timeSec)
        {
            if (track == null || track.keys == null || track.keys.Count == 0) return 0;
            int master = ResolveMasterIndex(track.path);
            if (master < 0) return 0;
            var ctx = model.MeshContextList[master];
            if (ctx == null) return 0;

            Vector3? sPos = SamplePosition(track, timeSec);
            Quaternion? sRot = SampleRotation(track, timeSec);

            // ベース（rest ローカル）
            var bt = ctx.BoneTransform;
            Matrix4x4 baseMat = (bt != null && bt.UseLocalTransform) ? bt.TransformMatrix : Matrix4x4.identity;
            Vector3 restPos = bt != null ? bt.Position : Vector3.zero;
            Quaternion restRot = bt != null ? bt.RotationQuaternion : Quaternion.identity;

            Vector3 localPos = sPos.HasValue ? sPos.Value * PositionScale : restPos;
            Quaternion localRot = sRot.HasValue ? sRot.Value : restRot;

            // clip 絶対ローカル → デルタ（BoneTransform^-1 × clipLocal）
            Matrix4x4 clipLocal = Matrix4x4.TRS(localPos, localRot, Vector3.one);
            Matrix4x4 deltaMat = baseMat.inverse * clipLocal;
            Vector3 deltaPos = new Vector3(deltaMat.m03, deltaMat.m13, deltaMat.m23);
            SetDelta(ctx, deltaPos, deltaMat.rotation);
            return 1;
        }

        // (b) 自前実装（近似）: dto.muscles から本体ボーンのローカル回転を再構成して適用。
        //   ※ Muscle Referential の pre/post 回転・sign を省く近似。
        //     各ボーンの DoF 値を HumanTrait.GetMuscleDefaultMin/Max で角度化し、
        //     dof(0,1,2) を局所軸(X,Y,Z)へ直接対応させて Euler 合成する。
        //     rest からのデルタとして BonePoseData に載せる（muscle=0 で rest）。
        //     精度は Unity 実測前提。
        private int ApplySelfMuscle(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.muscles == null || clip.muscles.Count == 0) return 0;

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            var muscleNames = HumanTrait.MuscleName;
            int boneCount = HumanTrait.BoneCount;
            int matched = 0;

            for (int bi = 0; bi < boneCount; bi++)
            {
                string boneName = HumanTrait.BoneName[bi];          // 例 "Left Upper Arm"（空白入り）
                string key = boneName.Replace(" ", string.Empty);    // 対応表キー "LeftUpperArm"

                int k = _mapping.Get(key);
                if (k < 0)
                    k = HumanoidBoneMapping.FindBoneByAliases(
                        _boneNames, new List<string> { key, boneName }, fuzzyMatch: true);
                if (k < 0 || k >= _boneMasterIndices.Count) continue;

                var ctx = model.MeshContextList[_boneMasterIndices[k]];
                if (ctx == null) continue;

                Vector3 euler = Vector3.zero;
                bool any = false;
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi < 0 || muscleNames == null || mi >= muscleNames.Length) continue;
                    if (!muscleByName.TryGetValue(muscleNames[mi], out var mt)) continue;

                    float v = SampleWeight(mt, timeSec);                 // 正規化値 [-1,1]
                    float min = HumanTrait.GetMuscleDefaultMin(mi);
                    float max = HumanTrait.GetMuscleDefaultMax(mi);
                    euler[dof] = v >= 0f ? v * max : -v * min;           // v=+1→max, v=-1→min, v=0→0
                    any = true;
                }
                if (!any) continue;

                // rest からのデルタ（位置は変えない）
                SetDelta(ctx, Vector3.zero, Quaternion.Euler(euler));
                matched++;
            }
            return matched;
        }

        private void SetDelta(MeshContext ctx, Vector3 deltaPos, Quaternion deltaRot)
        {
            if (ctx.BonePoseData == null)
            {
                ctx.BonePoseData = new BonePoseData();
                ctx.BonePoseData.IsActive = true;
            }
            ctx.BonePoseData.SetLayer(LayerName, deltaPos, deltaRot);
        }

        /// <summary>適用した "UnityClip" レイヤーを全ボーンから除去して復帰。</summary>
        public void ResetAllBones(ModelContext model)
        {
            if (model == null) return;
            foreach (var entry in model.Bones)
            {
                int master = entry.MasterIndex;
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                var bpd = ctx?.BonePoseData;
                if (bpd == null) continue;
                var layer = bpd.GetLayer(LayerName);
                if (layer != null) layer.Clear();
            }
            model.ComputeWorldMatrices();
        }

        // ================================================================
        // サンプリング（スパースキー・線形補間）
        // ================================================================

        // マッスル重み（正規化値）を timeSec で線形補間
        private static float SampleWeight(UnityMuscleTrackDTO track, float timeSec)
        {
            if (track == null || track.w == null || track.w.Count == 0) return 0f;
            UnityWeightKeyDTO prev = null, next = null;
            foreach (var key in track.w)
            {
                if (key == null) continue;
                if (key.t <= timeSec) prev = key;
                if (key.t >= timeSec) { next = key; break; }
            }
            if (prev == null && next == null) return 0f;
            if (prev == null) return next.v;
            if (next == null) return prev.v;
            if (prev.t == next.t) return prev.v;
            float a = (timeSec - prev.t) / (next.t - prev.t);
            return Mathf.Lerp(prev.v, next.v, a);
        }

        private static Vector3? SamplePosition(UnityBoneTrackDTO track, float timeSec)
        {
            // pos を持つキーだけで補間
            UnityBoneKeyDTO prev = null, next = null;
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

        private static Quaternion? SampleRotation(UnityBoneTrackDTO track, float timeSec)
        {
            UnityBoneKeyDTO prev = null, next = null;
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

        // ================================================================
        // ヘルパ
        // ================================================================

        private static Vector3 ToVec3(float[] a) => new Vector3(a[0], a[1], a[2]);
        private static Quaternion ToQuat(float[] a) => new Quaternion(a[0], a[1], a[2], a[3]);

        private static string LastSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            int idx = path.LastIndexOf('/');
            return idx >= 0 ? path.Substring(idx + 1) : path;
        }
    }
}
