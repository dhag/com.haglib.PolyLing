using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.UnityClip.Editor
{
    // ================================================================
    // AnimationClipToDto
    // ----------------------------------------------------------------
    // UnityEngine.AnimationClip（Generic）から UnityClipDTO を抽出する。
    // AnimationUtility が UnityEditor 名前空間のため Editor アセンブリ専用。
    //
    // 対応範囲（今回）:
    //   - Generic の Transform カーブ:
    //       m_LocalPosition.{x,y,z} / m_LocalRotation.{x,y,z,w} / m_LocalScale.{x,y,z}
    //       → dto.bones（path 別トラック）。
    //   - Humanoid の Animator バインディング（マッスル/ルート等）:
    //       生のまま dto.muscles（name = propertyName, w = 疎キー[{t,v}]）に格納する。
    //       分類・マッスル→ボーン変換はしない。muscles があれば clipType="Humanoid"。
    //   - 接線は捨てて線形化（キー時刻で Evaluate してサンプル化）。キー時刻 t は秒（丸めなし）。
    //   - 座標系変換なし（AnimationClip は Unity 左手系のまま）。
    //
    // 未対応（別途）:
    //   - localEulerAnglesRaw.* 等の Euler 回転プロパティ。
    // ================================================================
    public static class AnimationClipToDto
    {
        // ------------------------------------------------------------
        // .anim アセットパス -> UnityClipDTO
        // ------------------------------------------------------------
        public static UnityClipDTO LoadFromAsset(string assetPath)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                Debug.LogError($"[AnimationClipToDto] AnimationClip not found: {assetPath}");
                return null;
            }
            return Convert(clip);
        }

        // ------------------------------------------------------------
        // AnimationClip -> UnityClipDTO
        // ------------------------------------------------------------
        /// <summary>アバターなし（焼き込みなし）。bones（二次骨）＋ muscles（生）のみ。</summary>
        public static UnityClipDTO Convert(AnimationClip clip)
        {
            return Convert(clip, null);
        }

        /// <summary>
        /// avatar（Humanoid Animator）を渡すと、AnimationMode.SampleAnimationClip で
        /// 各キー時刻にサンプルし、GetBoneTransform(HumanBodyBones).localRotation を
        /// dto.bakedBones へ焼き込む（本体ボーンのローカル回転）。
        /// avatar が null / 非 Humanoid のときは焼き込みをスキップする。
        /// </summary>
        public static UnityClipDTO Convert(AnimationClip clip, Animator avatar)
        {
            if (clip == null) return null;

            float fps = clip.frameRate > 0f ? clip.frameRate : 30f;

            var dto = new UnityClipDTO
            {
                name = clip.name,
                clipType = "Generic",
                frameRate = fps,
                loop = clip.isLooping
            };

            // Transform パス別に成分カーブを集約
            var map = new Dictionary<string, PathCurves>();

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var b in bindings)
            {
                if (b.type != typeof(Transform))
                {
                    // Humanoid の Animator バインディング（マッスル/ルート等）は
                    // 生のまま dto.muscles に格納する（分類・変換はしない）。
                    if (b.type == typeof(Animator))
                    {
                        var mcurve = AnimationUtility.GetEditorCurve(clip, b);
                        var mtrack = BuildMuscleTrack(b.propertyName, mcurve);
                        if (mtrack != null) dto.muscles.Add(mtrack);
                    }
                    continue;
                }

                int ch = ClassifyProperty(b.propertyName, out int comp);
                if (ch < 0) continue; // 未対応プロパティ（Euler 等）

                var curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null) continue;

                if (!map.TryGetValue(b.path, out var pc))
                {
                    pc = new PathCurves();
                    map[b.path] = pc;
                }
                pc.Set(ch, comp, curve);
            }

            // マッスル（Animator）カーブがあれば Humanoid とする。
            // Transform カーブ（二次骨）＝ bones はそのまま併存させる。
            if (dto.muscles.Count > 0)
                dto.clipType = "Humanoid";

            foreach (var kv in map)
            {
                var track = BuildTrack(kv.Key, kv.Value, fps);
                if (track != null) dto.bones.Add(track);
            }

            // アバターが Humanoid なら、本体ボーンのローカル回転を焼き込む。
            BakeBodyBones(clip, avatar, fps, dto);

            return dto;
        }

        // ------------------------------------------------------------
        // 本体ボーン焼き込み:
        //   AnimationMode.SampleAnimationClip でアバターにサンプルし、
        //   各 HumanBodyBones の localRotation を dto.bakedBones へ格納。
        //   path = HumanBodyBones 名（例 "LeftUpperArm"）、key.t=秒、rot=[x,y,z,w]。
        //   ※ Editor 専用 API（AnimationMode）を使用。
        // ------------------------------------------------------------
        private static void BakeBodyBones(AnimationClip clip, Animator avatar, float fps, UnityClipDTO dto)
        {
            if (avatar == null || avatar.avatar == null || !avatar.avatar.isHuman) return;

            // 焼き込み対象の Humanoid ボーンと、その localRotation キー列
            var bones = new List<HumanBodyBones>();
            var trackByBone = new Dictionary<HumanBodyBones, UnityBoneTrackDTO>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var hbb = (HumanBodyBones)i;
                var tr = avatar.GetBoneTransform(hbb);
                if (tr == null) continue;
                bones.Add(hbb);
                trackByBone[hbb] = new UnityBoneTrackDTO { path = hbb.ToString() };
            }
            if (bones.Count == 0) return;

            // キー時刻（秒）を、全 Animator/Transform カーブから union（丸めなし）
            var times = new SortedSet<float>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null) continue;
                var keys = c.keys;
                for (int i = 0; i < keys.Length; i++) times.Add(keys[i].time);
            }
            if (times.Count == 0) return;

            var go = avatar.gameObject;
            try
            {
                AnimationMode.StartAnimationMode();
                foreach (float t in times)
                {
                    float time = Mathf.Clamp(t, 0f, clip.length);
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(go, clip, time);
                    AnimationMode.EndSampling();

                    foreach (var hbb in bones)
                    {
                        var tr = avatar.GetBoneTransform(hbb);
                        if (tr == null) continue;
                        Quaternion q = tr.localRotation;
                        trackByBone[hbb].keys.Add(new UnityBoneKeyDTO
                        {
                            t = t,
                            rot = new[] { q.x, q.y, q.z, q.w }
                        });
                    }
                }
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }

            foreach (var hbb in bones)
            {
                var tr = trackByBone[hbb];
                if (tr.keys.Count > 0) dto.bakedBones.Add(tr);
            }
        }

        // ------------------------------------------------------------
        // 単一 float カーブ（マッスル等）を疎キー {t,v} 列に変換
        //   t = Keyframe.time（秒・丸めなし）、v = Evaluate(t)
        // ------------------------------------------------------------
        private static UnityMuscleTrackDTO BuildMuscleTrack(string name, AnimationCurve curve)
        {
            if (curve == null) return null;
            var keys = curve.keys;
            if (keys == null || keys.Length == 0) return null;

            var track = new UnityMuscleTrackDTO { name = name };
            for (int i = 0; i < keys.Length; i++)
            {
                float t = keys[i].time;
                track.w.Add(new UnityWeightKeyDTO { t = t, v = curve.Evaluate(t) });
            }
            return track;
        }

        // ------------------------------------------------------------
        // propertyName -> (チャンネル: 0=pos,1=rot,2=scl, 成分index)
        // ------------------------------------------------------------
        private static int ClassifyProperty(string prop, out int comp)
        {
            comp = -1;
            switch (prop)
            {
                case "m_LocalPosition.x": comp = 0; return 0;
                case "m_LocalPosition.y": comp = 1; return 0;
                case "m_LocalPosition.z": comp = 2; return 0;

                case "m_LocalRotation.x": comp = 0; return 1;
                case "m_LocalRotation.y": comp = 1; return 1;
                case "m_LocalRotation.z": comp = 2; return 1;
                case "m_LocalRotation.w": comp = 3; return 1;

                case "m_LocalScale.x": comp = 0; return 2;
                case "m_LocalScale.y": comp = 1; return 2;
                case "m_LocalScale.z": comp = 2; return 2;

                default: return -1;
            }
        }

        // ------------------------------------------------------------
        // 1 パス分のトラック生成
        // ------------------------------------------------------------
        private static UnityBoneTrackDTO BuildTrack(string path, PathCurves pc, float fps)
        {
            // 全成分カーブのキー時刻（秒）を union（丸めなし）
            var times = new SortedSet<float>();
            pc.CollectTimes(times);
            if (times.Count == 0) return null;

            var track = new UnityBoneTrackDTO { path = path };

            foreach (float t in times)
            {
                var key = new UnityBoneKeyDTO { t = t };

                if (pc.HasPos)
                {
                    key.pos = new[]
                    {
                        Eval(pc.Px, t, 0f),
                        Eval(pc.Py, t, 0f),
                        Eval(pc.Pz, t, 0f)
                    };
                }

                if (pc.HasRot)
                {
                    var q = new Quaternion(
                        Eval(pc.Rx, t, 0f),
                        Eval(pc.Ry, t, 0f),
                        Eval(pc.Rz, t, 0f),
                        Eval(pc.Rw, t, 1f));

                    // Evaluate 誤差の正規化
                    float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
                    if (sq > 1e-8f) q.Normalize();

                    key.rot = new[] { q.x, q.y, q.z, q.w };
                }

                if (pc.HasScl)
                {
                    key.scl = new[]
                    {
                        Eval(pc.Sx, t, 1f),
                        Eval(pc.Sy, t, 1f),
                        Eval(pc.Sz, t, 1f)
                    };
                }

                track.keys.Add(key);
            }

            return track;
        }

        private static float Eval(AnimationCurve c, float t, float fallback)
        {
            return c != null ? c.Evaluate(t) : fallback;
        }

        // ------------------------------------------------------------
        // path 単位の成分カーブ束
        // ------------------------------------------------------------
        private class PathCurves
        {
            public AnimationCurve Px, Py, Pz;
            public AnimationCurve Rx, Ry, Rz, Rw;
            public AnimationCurve Sx, Sy, Sz;

            public bool HasPos => Px != null || Py != null || Pz != null;
            public bool HasRot => Rx != null || Ry != null || Rz != null || Rw != null;
            public bool HasScl => Sx != null || Sy != null || Sz != null;

            public void Set(int ch, int comp, AnimationCurve c)
            {
                if (ch == 0)
                {
                    if (comp == 0) Px = c; else if (comp == 1) Py = c; else Pz = c;
                }
                else if (ch == 1)
                {
                    if (comp == 0) Rx = c; else if (comp == 1) Ry = c;
                    else if (comp == 2) Rz = c; else Rw = c;
                }
                else
                {
                    if (comp == 0) Sx = c; else if (comp == 1) Sy = c; else Sz = c;
                }
            }

            public void CollectTimes(SortedSet<float> times)
            {
                AddTimes(Px, times); AddTimes(Py, times); AddTimes(Pz, times);
                AddTimes(Rx, times); AddTimes(Ry, times);
                AddTimes(Rz, times); AddTimes(Rw, times);
                AddTimes(Sx, times); AddTimes(Sy, times); AddTimes(Sz, times);
            }

            private static void AddTimes(AnimationCurve c, SortedSet<float> times)
            {
                if (c == null) return;
                var keys = c.keys;
                for (int i = 0; i < keys.Length; i++)
                    times.Add(keys[i].time);
            }
        }
    }
}
