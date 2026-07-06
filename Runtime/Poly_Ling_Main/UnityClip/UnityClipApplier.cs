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
            if (clip.bones == null) return;

            int matched = 0;

            foreach (var track in clip.bones)
            {
                if (track == null || track.keys == null || track.keys.Count == 0) continue;

                int master = ResolveMasterIndex(track.path);
                if (master < 0) continue;

                var ctx = model.MeshContextList[master];
                if (ctx == null) continue;

                matched++;

                // ベース（rest ローカル）
                var bt = ctx.BoneTransform;
                Matrix4x4 baseMat = (bt != null && bt.UseLocalTransform)
                    ? bt.TransformMatrix
                    : Matrix4x4.identity;
                Vector3 restPos = bt != null ? bt.Position : Vector3.zero;
                Quaternion restRot = bt != null ? bt.RotationQuaternion : Quaternion.identity;

                // サンプリング（null チャンネルは rest を使用）
                Vector3? sPos = SamplePosition(track, timeSec);
                Quaternion? sRot = SampleRotation(track, timeSec);

                Vector3 localPos = sPos.HasValue ? sPos.Value * PositionScale : restPos;
                Quaternion localRot = sRot.HasValue ? sRot.Value : restRot;

                // clip 絶対ローカル → デルタ（BoneTransform^-1 × clipLocal）
                Matrix4x4 clipLocal = Matrix4x4.TRS(localPos, localRot, Vector3.one);
                Matrix4x4 deltaMat = baseMat.inverse * clipLocal;
                Vector3 deltaPos = new Vector3(deltaMat.m03, deltaMat.m13, deltaMat.m23);
                Quaternion deltaRot = deltaMat.rotation;

                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
                ctx.BonePoseData.SetLayer(LayerName, deltaPos, deltaRot);
            }

            MatchedTrackCount = matched;
            model.ComputeWorldMatrices();
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
