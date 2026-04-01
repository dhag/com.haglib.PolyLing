// Assets/Editor/MeshCreators/Revolution/RevolutionProfileGenerator.cs
// 回転体メッシュ用のプロファイルプリセット生成

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Revolution
{
    /// <summary>
    /// プロファイルプリセット生成ユーティリティ
    /// </summary>
    public static class RevolutionProfileGenerator
    {
        /// <summary>
        /// デフォルトプロファイル（シンプルな壺形状）
        /// </summary>
        public static List<Vector2> CreateDefault()
        {
            return new List<Vector2>
            {
                new Vector2(0.3f, 0f),
                new Vector2(0.5f, 0.2f),
                new Vector2(0.5f, 0.6f),
                new Vector2(0.35f, 0.8f),
                new Vector2(0.4f, 1f),
            };
        }

        /// <summary>
        /// プリセットに応じたプロファイルを生成
        /// </summary>
        public static List<Vector2> CreatePreset(ProfilePreset preset, ref RevolutionParams p)
        {
            switch (preset)
            {
                case ProfilePreset.Donut:
                    return CreateDonut(p.DonutMajorRadius, p.DonutMinorRadius, p.DonutTubeSegments, out p.CloseLoop);
                case ProfilePreset.RoundedPipe:
                    return CreateRoundedPipe(ref p, out p.CloseLoop);
                case ProfilePreset.Vase:
                    return CreateVase(out p.CloseLoop);
                case ProfilePreset.Goblet:
                    return CreateGoblet(out p.CloseLoop);
                case ProfilePreset.Bell:
                    return CreateBell(out p.CloseLoop);
                case ProfilePreset.Hourglass:
                    return CreateHourglass(out p.CloseLoop);
                default:
                    return CreateDefault();
            }
        }

        /// <summary>
        /// ドーナツ（トーラス）プロファイル
        /// </summary>
        public static List<Vector2> CreateDonut(float majorRadius, float minorRadius, int tubeSegments, out bool closeLoop)
        {
            var profile = new List<Vector2>();
            float centerY = minorRadius + 0.1f;

            for (int i = 0; i < tubeSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / tubeSegments;
                float x = majorRadius + minorRadius * Mathf.Cos(angle);
                float y = centerY + minorRadius * Mathf.Sin(angle);
                profile.Add(new Vector2(x, y));
            }

            closeLoop = true;
            return profile;
        }

        /// <summary>
        /// 角丸パイププロファイル
        /// </summary>
        public static List<Vector2> CreateRoundedPipe(ref RevolutionParams p, out bool closeLoop)
        {
            var profile = new List<Vector2>();

            float halfH = p.PipeHeight * 0.5f;
            float innerR = p.PipeInnerRadius;
            float outerR = p.PipeOuterRadius;
            float iR = p.PipeInnerCornerRadius;
            int iSeg = p.PipeInnerCornerSegments;
            float oR = p.PipeOuterCornerRadius;
            int oSeg = p.PipeOuterCornerSegments;

            // 内側下角
            if (iR > 0.001f && iSeg > 0)
            {
                Vector2 center = new Vector2(innerR + iR, -halfH + iR);
                for (int i = 0; i <= iSeg; i++)
                {
                    float t = (float)i / iSeg;
                    float angle = Mathf.PI + t * Mathf.PI * 0.5f;
                    float x = center.x + iR * Mathf.Cos(angle);
                    float y = center.y + iR * Mathf.Sin(angle);
                    profile.Add(new Vector2(x, y));
                }
            }
            else
            {
                profile.Add(new Vector2(innerR, -halfH));
            }

            // 外側下角
            if (oR > 0.001f && oSeg > 0)
            {
                Vector2 center = new Vector2(outerR - oR, -halfH + oR);
                for (int i = 0; i <= oSeg; i++)
                {
                    float t = (float)i / oSeg;
                    float angle = Mathf.PI * 1.5f + t * Mathf.PI * 0.5f;
                    float x = center.x + oR * Mathf.Cos(angle);
                    float y = center.y + oR * Mathf.Sin(angle);
                    profile.Add(new Vector2(x, y));
                }
            }
            else
            {
                profile.Add(new Vector2(outerR, -halfH));
            }

            // 外側上角
            if (oR > 0.001f && oSeg > 0)
            {
                Vector2 center = new Vector2(outerR - oR, halfH - oR);
                for (int i = 0; i <= oSeg; i++)
                {
                    float t = (float)i / oSeg;
                    float angle = t * Mathf.PI * 0.5f;
                    float x = center.x + oR * Mathf.Cos(angle);
                    float y = center.y + oR * Mathf.Sin(angle);
                    profile.Add(new Vector2(x, y));
                }
            }
            else
            {
                profile.Add(new Vector2(outerR, halfH));
            }

            // 内側上角
            if (iR > 0.001f && iSeg > 0)
            {
                Vector2 center = new Vector2(innerR + iR, halfH - iR);
                for (int i = 0; i <= iSeg; i++)
                {
                    float t = (float)i / iSeg;
                    float angle = Mathf.PI * 0.5f + t * Mathf.PI * 0.5f;
                    float x = center.x + iR * Mathf.Cos(angle);
                    float y = center.y + iR * Mathf.Sin(angle);
                    profile.Add(new Vector2(x, y));
                }
            }
            else
            {
                profile.Add(new Vector2(innerR, halfH));
            }

            closeLoop = true;
            return profile;
        }

        /// <summary>
        /// 花瓶プロファイル
        /// </summary>
        public static List<Vector2> CreateVase(out bool closeLoop)
        {
            closeLoop = false;
            return new List<Vector2>
            {
                new Vector2(0.3f, 0f),
                new Vector2(0.5f, 0.1f),
                new Vector2(0.6f, 0.3f),
                new Vector2(0.5f, 0.6f),
                new Vector2(0.3f, 0.8f),
                new Vector2(0.25f, 0.9f),
                new Vector2(0.3f, 1f),
            };
        }

        /// <summary>
        /// ゴブレット（杯）プロファイル
        /// </summary>
        public static List<Vector2> CreateGoblet(out bool closeLoop)
        {
            closeLoop = false;
            return new List<Vector2>
            {
                new Vector2(0.3f, 0f),
                new Vector2(0.35f, 0.02f),
                new Vector2(0.08f, 0.1f),
                new Vector2(0.08f, 0.5f),
                new Vector2(0.15f, 0.55f),
                new Vector2(0.4f, 0.7f),
                new Vector2(0.45f, 1f),
            };
        }

        /// <summary>
        /// ベル（鐘）プロファイル
        /// </summary>
        public static List<Vector2> CreateBell(out bool closeLoop)
        {
            closeLoop = false;
            return new List<Vector2>
            {
                new Vector2(0.5f, 0f),
                new Vector2(0.45f, 0.1f),
                new Vector2(0.35f, 0.3f),
                new Vector2(0.2f, 0.6f),
                new Vector2(0.1f, 0.85f),
                new Vector2(0.05f, 1f),
            };
        }

        /// <summary>
        /// 砂時計プロファイル
        /// </summary>
        public static List<Vector2> CreateHourglass(out bool closeLoop)
        {
            closeLoop = false;
            return new List<Vector2>
            {
                new Vector2(0.4f, 0f),
                new Vector2(0.35f, 0.15f),
                new Vector2(0.15f, 0.4f),
                new Vector2(0.1f, 0.5f),
                new Vector2(0.15f, 0.6f),
                new Vector2(0.35f, 0.85f),
                new Vector2(0.4f, 1f),
            };
        }
    }
}
