// MediaPipeFrame.cs
// ライブ受信（motionLive）で届く MediaPipe の 1 フレーム {type:'mediapipe'} の型と読み取り。
// 読み取りはここだけで行い、使う側（姿勢・表情転写）は読み取り済みのこの型を使う。
// Runtime/Poly_Ling_Main/MediaPipe/ に配置
//
// ■ 形（送り手は データとツール/VRMビュワー/polyling_motion_studio/mediapipe_client.html）
//   {type:'mediapipe', seq, time, image:{w,h},
//    face:     {landmarks:[[x,y,z,visibility], …]},                       … 478 点（先頭 468 点が顔、残りは虹彩）
//    pose:     {landmarks:[[x,y,z,visibility], …], world:[[x,y,z,visibility], …]}   … 33 点
//    leftHand: {landmarks:[…], world:[…]},                               … 21 点
//    rightHand:{landmarks:[…], world:[…]}}                               … 21 点
//   landmarks は画像基準の正規化座標、world はメートル（座標の向きは MediaPipeCoords）。
//   visibility は無ければ null（ここでは NaN にする）。検出されなかった部位・送らない部位は入らない。
//   time は送り手の時計の秒（ほかの送り手の time とは比べられない）。
//   leftHand / rightHand が本人の左右か画像の左右かは未確認（MediaPipePoseHandler の swapHands で入れ替える）。

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>1 部位の点。Landmarks は正規化座標、World はメートル（無ければ null）。</summary>
    public sealed class MediaPipePart
    {
        public Vector3[] Landmarks = Array.Empty<Vector3>();
        /// <summary>Landmarks と同じ並びの visibility。無い点は NaN。</summary>
        public float[]   Visibility = Array.Empty<float>();
        public Vector3[] World;
    }

    public sealed class MediaPipeFrame
    {
        public const string MessageType = "mediapipe";

        public const string KeyFace      = "face";
        public const string KeyPose      = "pose";
        public const string KeyLeftHand  = "leftHand";
        public const string KeyRightHand = "rightHand";
        public static readonly string[] PartKeys = { KeyFace, KeyPose, KeyLeftHand, KeyRightHand };

        /// <summary>顔の点のうちメッシュの点（残りは虹彩）。</summary>
        public const int FaceMeshPoints = 468;
        public const int PosePoints     = 33;
        public const int HandPoints     = 21;

        public uint   Seq;
        public double Time;
        public int    ImageWidth;
        public int    ImageHeight;

        public MediaPipePart Face;
        public MediaPipePart Pose;
        public MediaPipePart LeftHand;
        public MediaPipePart RightHand;

        /// <summary>部位を名前で引く（face / pose / leftHand / rightHand）。無ければ null。</summary>
        public MediaPipePart Get(string key)
        {
            switch (key)
            {
                case KeyFace:      return Face;
                case KeyPose:      return Pose;
                case KeyLeftHand:  return LeftHand;
                case KeyRightHand: return RightHand;
                default:           return null;
            }
        }

        /// <summary>入っている部位の名前（カンマ区切り）。</summary>
        public string PartsText
        {
            get
            {
                var list = new List<string>(4);
                foreach (var k in PartKeys) if (Get(k) != null) list.Add(k);
                return string.Join(",", list);
            }
        }

        /// <summary>画像の大きさを持つか。</summary>
        public bool HasImageSize => ImageWidth > 0 && ImageHeight > 0;

        /// <summary>
        /// JSON（type が mediapipe のもの）を読む。形が違えば false（error に理由）。
        /// 部位の点の数は検査しない（使う側が要る点数を見る）。
        /// </summary>
        public static bool TryParse(JObject o, out MediaPipeFrame frame, out string error)
        {
            frame = null;
            error = "";
            if (o == null) { error = "JSON がありません"; return false; }
            if ((string)o["type"] != MessageType) { error = "type が mediapipe でない"; return false; }

            var f = new MediaPipeFrame();
            try
            {
                if (o["seq"] != null && o["seq"].Type != JTokenType.Null)   f.Seq  = (uint)o["seq"];
                if (o["time"] != null && o["time"].Type != JTokenType.Null) f.Time = (double)o["time"];
                if (o["image"] is JObject img)
                {
                    f.ImageWidth  = (int?)img["w"] ?? 0;
                    f.ImageHeight = (int?)img["h"] ?? 0;
                }
                f.Face      = ReadPart(o[KeyFace]);
                f.Pose      = ReadPart(o[KeyPose]);
                f.LeftHand  = ReadPart(o[KeyLeftHand]);
                f.RightHand = ReadPart(o[KeyRightHand]);
            }
            catch (Exception e)
            {
                error = "mediapipe の形が違う: " + e.Message;
                return false;
            }
            frame = f;
            return true;
        }

        private static MediaPipePart ReadPart(JToken tok)
        {
            if (!(tok is JObject o)) return null;
            var part = new MediaPipePart();
            if (o["landmarks"] is JArray lm) ReadPoints(lm, out part.Landmarks, out part.Visibility);
            if (o["world"] is JArray wd) ReadPoints(wd, out part.World, out _);
            return part;
        }

        private static void ReadPoints(JArray arr, out Vector3[] points, out float[] visibility)
        {
            points     = new Vector3[arr.Count];
            visibility = new float[arr.Count];
            for (int i = 0; i < arr.Count; i++)
            {
                var a = (JArray)arr[i];
                points[i] = new Vector3((float)a[0], (float)a[1], a.Count > 2 ? (float)a[2] : 0f);
                visibility[i] = a.Count > 3 && a[3].Type != JTokenType.Null ? (float)a[3] : float.NaN;
            }
        }
    }
}
