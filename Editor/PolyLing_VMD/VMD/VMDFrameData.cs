// VMDFrameData.cs
// VMDモーションのキーフレームデータ構造
// HagLib.VMDMotion から移植・Unity対応版

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Poly_Ling.VMD
{
    // ================================================================
    // インターフェース
    // ================================================================

    /// <summary>
    /// フレームデータの共通インターフェース
    /// </summary>
    public interface IFrameData : IComparable
    {
        uint FrameNumber { get; set; }
        string Name { get; }
    }

    // ================================================================
    // ベジェ曲線（補間用）
    // ================================================================

    /// <summary>
    /// VMD補間用ベジェ曲線
    /// (0,0) - (v1.x, v1.y) - (v2.x, v2.y) - (1,1) の3次ベジェ
    /// </summary>
    [Serializable]
    public class BezierCurve
    {
        private const float Epsilon = 1.0e-3f;

        public Vector2 v1;
        public Vector2 v2;

        public BezierCurve()
        {
            v1 = new Vector2(0.25f, 0.25f);
            v2 = new Vector2(0.75f, 0.75f);
        }

        public BezierCurve(Vector2 control1, Vector2 control2)
        {
            v1 = control1;
            v2 = control2;
        }

        /// <summary>
        /// 進行度(0-1)から補間値(0-1)を計算
        /// ニュートン法による近似
        /// </summary>
        public float Evaluate(float progress)
        {
            progress = Mathf.Clamp01(progress);
            float t = progress;
            float dt;

            // ニュートン法で t を求める
            int maxIterations = 10;
            for (int i = 0; i < maxIterations; i++)
            {
                dt = -(Fx(t) - progress) / DFx(t);
                if (float.IsNaN(dt)) break;
                t += Mathf.Clamp(dt, -1f, 1f);
                if (Mathf.Abs(dt) <= Epsilon) break;
            }

            return Mathf.Clamp01(Fy(t));
        }

        // fy(t) = 3(1-t)²t·v1.y + 3(1-t)t²·v2.y + t³
        private float Fy(float t)
        {
            float u = 1f - t;
            return 3f * u * u * t * v1.y + 3f * u * t * t * v2.y + t * t * t;
        }

        // fx(t) = 3(1-t)²t·v1.x + 3(1-t)t²·v2.x + t³
        private float Fx(float t)
        {
            float u = 1f - t;
            return 3f * u * u * t * v1.x + 3f * u * t * t * v2.x + t * t * t;
        }

        // dfx/dt
        private float DFx(float t)
        {
            float u = 1f - t;
            return -6f * u * t * v1.x + 3f * u * u * v1.x
                   - 3f * t * t * v2.x + 6f * u * t * v2.x + 3f * t * t;
        }

        /// <summary>
        /// 線形補間用のデフォルトカーブ
        /// </summary>
        public static BezierCurve Linear => new BezierCurve(
            new Vector2(0.25f, 0.25f),
            new Vector2(0.75f, 0.75f));
    }

    // ================================================================
    // ボーンフレームデータ
    // ================================================================

    /// <summary>
    /// ボーンキーフレームデータ
    /// </summary>
    [Serializable]
    public class BoneFrameData : IFrameData
    {
        public string BoneName;
        public uint FrameNumber { get; set; }
        public Vector3 Position;
        public Quaternion Rotation;

        /// <summary>
        /// 補間曲線 [X位置, Y位置, Z位置, 回転]
        /// </summary>
        public BezierCurve[] Curves;

        /// <summary>
        /// 生の補間データ [4][4][4]
        /// </summary>
        public byte[][][] Interpolation;

        public string Name => BoneName;

        public BoneFrameData()
        {
            BoneName = string.Empty;
            Position = Vector3.zero;
            Rotation = Quaternion.identity;
            InitializeInterpolation();
        }

        public BoneFrameData(string boneName, uint frameNumber, Vector3 position, Quaternion rotation)
        {
            BoneName = boneName;
            FrameNumber = frameNumber;
            Position = position;
            Rotation = rotation;
            InitializeInterpolation();
        }

        private void InitializeInterpolation()
        {
            Interpolation = new byte[4][][];
            for (int i = 0; i < 4; i++)
            {
                Interpolation[i] = new byte[4][];
                for (int j = 0; j < 4; j++)
                {
                    Interpolation[i][j] = new byte[4];
                }
            }

            Curves = new BezierCurve[4];
            for (int i = 0; i < 4; i++)
            {
                Curves[i] = BezierCurve.Linear;
            }
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public BoneFrameData Clone()
        {
            var clone = new BoneFrameData
            {
                BoneName = BoneName,
                FrameNumber = FrameNumber,
                Position = Position,
                Rotation = Rotation
            };

            for (int i = 0; i < 4; i++)
            {
                clone.Curves[i] = new BezierCurve(Curves[i].v1, Curves[i].v2);
                for (int j = 0; j < 4; j++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        clone.Interpolation[i][j][k] = Interpolation[i][j][k];
                    }
                }
            }

            return clone;
        }

        /// <summary>
        /// 2つのフレーム間を補間
        /// </summary>
        public static (Vector3 position, Quaternion rotation) Interpolate(
            BoneFrameData from, BoneFrameData to, float t)
        {
            if (to == null) return (from.Position, from.Rotation);
            if (from == null) return (to.Position, to.Rotation);

            // 各軸の補間率を計算
            float tx = to.Curves[0].Evaluate(t);
            float ty = to.Curves[1].Evaluate(t);
            float tz = to.Curves[2].Evaluate(t);
            float tr = to.Curves[3].Evaluate(t);

            Vector3 pos = new Vector3(
                Mathf.Lerp(from.Position.x, to.Position.x, tx),
                Mathf.Lerp(from.Position.y, to.Position.y, ty),
                Mathf.Lerp(from.Position.z, to.Position.z, tz));

            Quaternion rot = Quaternion.Slerp(from.Rotation, to.Rotation, tr);

            return (pos, rot);
        }
    }

    // ================================================================
    // モーフフレームデータ
    // ================================================================

    /// <summary>
    /// モーフ（表情）キーフレームデータ
    /// </summary>
    [Serializable]
    public class MorphFrameData : IFrameData
    {
        public string MorphName;
        public uint FrameNumber { get; set; }
        public float Weight;

        public string Name => MorphName;

        public MorphFrameData()
        {
            MorphName = string.Empty;
            Weight = 0f;
        }

        public MorphFrameData(string morphName, uint frameNumber, float weight)
        {
            MorphName = morphName;
            FrameNumber = frameNumber;
            Weight = weight;
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public MorphFrameData Clone()
        {
            return new MorphFrameData(MorphName, FrameNumber, Weight);
        }

        /// <summary>
        /// 2つのフレーム間を線形補間
        /// </summary>
        public static float Interpolate(MorphFrameData from, MorphFrameData to, float t)
        {
            if (to == null) return from?.Weight ?? 0f;
            if (from == null) return to.Weight;
            return Mathf.Lerp(from.Weight, to.Weight, t);
        }
    }

    // ================================================================
    // カメラフレームデータ
    // ================================================================

    /// <summary>
    /// カメラキーフレームデータ
    /// </summary>
    [Serializable]
    public class CameraFrameData : IFrameData
    {
        public uint FrameNumber { get; set; }
        public float Distance;
        public Vector3 Position;
        public Vector3 EulerRotation;
        public uint FieldOfView;
        public bool Perspective;

        /// <summary>
        /// 補間曲線 [X位置, Y位置, Z位置, 回転, 距離, 視野角]
        /// </summary>
        public BezierCurve[] Curves;

        public byte[][] Interpolation;

        public string Name => "Camera";

        public CameraFrameData()
        {
            Distance = 10f;
            Position = Vector3.zero;
            EulerRotation = Vector3.zero;
            FieldOfView = 45;
            Perspective = true;
            InitializeInterpolation();
        }

        private void InitializeInterpolation()
        {
            Interpolation = new byte[4][];
            for (int i = 0; i < 4; i++)
            {
                Interpolation[i] = new byte[6];
            }

            Curves = new BezierCurve[6];
            for (int i = 0; i < 6; i++)
            {
                Curves[i] = BezierCurve.Linear;
            }
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public CameraFrameData Clone()
        {
            var clone = new CameraFrameData
            {
                FrameNumber = FrameNumber,
                Distance = Distance,
                Position = Position,
                EulerRotation = EulerRotation,
                FieldOfView = FieldOfView,
                Perspective = Perspective
            };

            for (int i = 0; i < 6; i++)
            {
                clone.Curves[i] = new BezierCurve(Curves[i].v1, Curves[i].v2);
            }
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 6; j++)
                {
                    clone.Interpolation[i][j] = Interpolation[i][j];
                }
            }

            return clone;
        }
    }

    // ================================================================
    // ライトフレームデータ
    // ================================================================

    /// <summary>
    /// ライトキーフレームデータ
    /// </summary>
    [Serializable]
    public class LightFrameData : IFrameData
    {
        public uint FrameNumber { get; set; }
        public Color3 LightColor;
        public Vector3 LightPosition;

        public string Name => "Light";

        public LightFrameData()
        {
            LightColor = new Color3(1f);
            LightPosition = Vector3.zero;
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public LightFrameData Clone()
        {
            return new LightFrameData
            {
                FrameNumber = FrameNumber,
                LightColor = LightColor,
                LightPosition = LightPosition
            };
        }
    }

    // ================================================================
    // セルフシャドウフレームデータ
    // ================================================================

    /// <summary>
    /// セルフシャドウキーフレームデータ
    /// </summary>
    [Serializable]
    public class SelfShadowFrameData : IFrameData
    {
        public uint FrameNumber { get; set; }
        public byte Mode;
        public float Distance;

        public string Name => "SelfShadow";

        public SelfShadowFrameData()
        {
            Mode = 0;
            Distance = 0f;
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public SelfShadowFrameData Clone()
        {
            return new SelfShadowFrameData
            {
                FrameNumber = FrameNumber,
                Mode = Mode,
                Distance = Distance
            };
        }
    }

    // ================================================================
    // IK表示フレームデータ
    // ================================================================

    /// <summary>
    /// IK情報
    /// </summary>
    [Serializable]
    public struct IKInfo
    {
        public string BoneName;
        public bool Enabled;

        public IKInfo(string boneName, bool enabled)
        {
            BoneName = boneName;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// モデル表示・IK ON/OFFキーフレームデータ
    /// </summary>
    [Serializable]
    public class ShowIKFrameData : IFrameData
    {
        public uint FrameNumber { get; set; }
        public bool Show;
        public List<IKInfo> IKList;

        public string Name => "ShowIK";

        public ShowIKFrameData()
        {
            Show = true;
            IKList = new List<IKInfo>();
        }

        public int CompareTo(object obj)
        {
            if (obj is IFrameData other)
                return (int)FrameNumber - (int)other.FrameNumber;
            return 0;
        }

        public ShowIKFrameData Clone()
        {
            var clone = new ShowIKFrameData
            {
                FrameNumber = FrameNumber,
                Show = Show,
                IKList = new List<IKInfo>()
            };

            foreach (var ik in IKList)
            {
                clone.IKList.Add(new IKInfo(ik.BoneName, ik.Enabled));
            }

            return clone;
        }
    }
}
