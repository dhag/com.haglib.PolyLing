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
    //   - Generic の Transform カーブのみ:
    //       m_LocalPosition.{x,y,z} / m_LocalRotation.{x,y,z,w} / m_LocalScale.{x,y,z}
    //   - 接線は捨てて線形化（キー時刻で Evaluate してサンプル化）。
    //   - 座標系変換なし（AnimationClip は Unity 左手系のまま）。
    //
    // 未対応（別途）:
    //   - Humanoid Muscle（typeof(Animator) バインディング）: clipType のみ "Humanoid" とし bones は未生成。
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
        public static UnityClipDTO Convert(AnimationClip clip)
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
            bool sawHumanoid = false;

            var bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var b in bindings)
            {
                if (b.type != typeof(Transform))
                {
                    // Humanoid Muscle 等は今回対象外
                    if (b.type == typeof(Animator)) sawHumanoid = true;
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

            if (sawHumanoid && map.Count == 0)
            {
                dto.clipType = "Humanoid";
                Debug.LogWarning(
                    "[AnimationClipToDto] Humanoid(Muscle) クリップです。" +
                    "今回は Generic 抽出のみのため bones は未生成です。");
            }

            foreach (var kv in map)
            {
                var track = BuildTrack(kv.Key, kv.Value, fps);
                if (track != null) dto.bones.Add(track);
            }

            return dto;
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
            // 全成分カーブのキー時刻を frame 量子化して union
            var frames = new SortedSet<int>();
            pc.CollectFrames(frames, fps);
            if (frames.Count == 0) return null;

            var track = new UnityBoneTrackDTO { path = path };

            foreach (int f in frames)
            {
                float t = f / fps;
                var key = new UnityBoneKeyDTO { f = f };

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

            public void CollectFrames(SortedSet<int> frames, float fps)
            {
                AddFrames(Px, frames, fps); AddFrames(Py, frames, fps); AddFrames(Pz, frames, fps);
                AddFrames(Rx, frames, fps); AddFrames(Ry, frames, fps);
                AddFrames(Rz, frames, fps); AddFrames(Rw, frames, fps);
                AddFrames(Sx, frames, fps); AddFrames(Sy, frames, fps); AddFrames(Sz, frames, fps);
            }

            private static void AddFrames(AnimationCurve c, SortedSet<int> frames, float fps)
            {
                if (c == null) return;
                var keys = c.keys;
                for (int i = 0; i < keys.Length; i++)
                    frames.Add(Mathf.RoundToInt(keys[i].time * fps));
            }
        }
    }
}
