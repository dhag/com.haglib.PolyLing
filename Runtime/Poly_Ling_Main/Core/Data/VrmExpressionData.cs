// Runtime/Poly_Ling_Main/Core/Data/VrmExpressionData.cs
// ============================================================
// VRM 1.0 表情（VRMC_vrm.expressions）の付帯データ（純POCO）
// ============================================================
//
// 【役割】
//   MorphExpression が持たない VRM 表情の欄を保持する。
//     ・IsBinary と Override（まばたき・視線・口）
//     ・材質色バインド（materialColorBinds）
//     ・UV バインド（textureTransformBinds）
//   MorphExpression.Vrm として付帯し、null＝VRM 固有の値なし。
//   扱いは MaterialData.Pmx と同じで、PolyLing は評価しないが
//   読み書きで失ってはいけない値。表情プレビューには反映しない。
//
// 【材質の参照は名前】
//   VRM は材質を索引で持つが、UniVRM の VRM10Expression は材質名で持ち、
//   書き出し時に名前から索引を引く（Vrm10Exporter.cs:817-828）。
//   読み込みでも索引から名前へ直す（ExpressionExtensions.cs:30-78）。
//   ここも名前で持つ。
//
// 【UV バインドの値】
//   UniVRM の MaterialUVBinding と同じ Unity 側の値（Scaling / Offset）で持つ。
//   glTF 側の値との変換（VerticalFlipScaleOffset）は I/O 境界で行う
//   （ExpressionExtensions.cs:62 / Vrm10Exporter.cs:773）。
//
// 【なぜ PolyLing 側で enum を定義するか】
//   VrmMetaData.cs と同じ理由（分離規約は IVrm10Exporter.cs 冒頭を正典とする）。
//   UniGLTF.Extensions.VRMC_vrm の enum と同じ並びで定義し、
//   境界では switch で写す。(int) キャストで写さないこと。
//
// 【依存】
//   UnityEngine.Vector2 / Vector4 のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// この表情が他の表情をどう抑えるか。VRMC_vrm の ExpressionOverrideType。
    /// </summary>
    public enum VrmExpressionOverride
    {
        /// <summary>抑えない。</summary>
        None = 0,
        /// <summary>この表情が少しでも効いていれば、対象を 0 にする。</summary>
        Block = 1,
        /// <summary>この表情の強さに応じて、対象を弱める。</summary>
        Blend = 2,
    }

    /// <summary>
    /// 材質色バインドの対象。VRMC_vrm の MaterialColorType。
    /// </summary>
    public enum VrmMaterialColorType
    {
        /// <summary>基本色。</summary>
        Color = 0,
        /// <summary>発光色。</summary>
        EmissionColor = 1,
        /// <summary>陰の色（MToon）。</summary>
        ShadeColor = 2,
        /// <summary>マットキャップ色（MToon）。</summary>
        MatcapColor = 3,
        /// <summary>リム色（MToon）。</summary>
        RimColor = 4,
        /// <summary>輪郭線の色（MToon）。</summary>
        OutlineColor = 5,
    }

    /// <summary>材質色バインド 1 件。</summary>
    [Serializable]
    public class VrmMaterialColorBind
    {
        /// <summary>対象の材質名。</summary>
        public string MaterialName { get; set; } = "";

        /// <summary>対象の色。</summary>
        public VrmMaterialColorType BindType { get; set; } = VrmMaterialColorType.Color;

        /// <summary>表情が完全に効いたときの色（RGBA）。</summary>
        public Vector4 TargetValue { get; set; } = Vector4.zero;

        /// <summary>ディープコピー。</summary>
        public VrmMaterialColorBind Clone()
        {
            return new VrmMaterialColorBind
            {
                MaterialName = this.MaterialName,
                BindType     = this.BindType,
                TargetValue  = this.TargetValue,
            };
        }
    }

    /// <summary>UV バインド 1 件（値は Unity 側の Scaling / Offset）。</summary>
    [Serializable]
    public class VrmTextureTransformBind
    {
        /// <summary>対象の材質名。</summary>
        public string MaterialName { get; set; } = "";

        /// <summary>表情が完全に効いたときの UV の拡大率。</summary>
        public Vector2 Scaling { get; set; } = Vector2.one;

        /// <summary>表情が完全に効いたときの UV のずらし量。</summary>
        public Vector2 Offset { get; set; } = Vector2.zero;

        /// <summary>ディープコピー。</summary>
        public VrmTextureTransformBind Clone()
        {
            return new VrmTextureTransformBind
            {
                MaterialName = this.MaterialName,
                Scaling      = this.Scaling,
                Offset       = this.Offset,
            };
        }
    }

    /// <summary>
    /// VRM 1.0 表情の付帯データ（純POCO）。MorphExpression.Vrm として付帯する。
    /// </summary>
    [Serializable]
    public class VrmExpressionData
    {
        /// <summary>重みを 0 か 1 に丸めて使うか。</summary>
        public bool IsBinary { get; set; } = false;

        /// <summary>まばたき系の表情をどう抑えるか。</summary>
        public VrmExpressionOverride OverrideBlink { get; set; } = VrmExpressionOverride.None;

        /// <summary>視線系の表情をどう抑えるか。</summary>
        public VrmExpressionOverride OverrideLookAt { get; set; } = VrmExpressionOverride.None;

        /// <summary>口の形の表情をどう抑えるか。</summary>
        public VrmExpressionOverride OverrideMouth { get; set; } = VrmExpressionOverride.None;

        /// <summary>材質色バインド。</summary>
        public List<VrmMaterialColorBind> MaterialColorBinds { get; set; } = new List<VrmMaterialColorBind>();

        /// <summary>UV バインド。</summary>
        public List<VrmTextureTransformBind> TextureTransformBinds { get; set; } = new List<VrmTextureTransformBind>();

        /// <summary>材質色バインドか UV バインドを 1 件以上持つか。</summary>
        public bool HasMaterialBinds =>
            (MaterialColorBinds != null && MaterialColorBinds.Count > 0) ||
            (TextureTransformBinds != null && TextureTransformBinds.Count > 0);

        /// <summary>ディープコピー。</summary>
        public VrmExpressionData Clone()
        {
            var copy = new VrmExpressionData
            {
                IsBinary       = this.IsBinary,
                OverrideBlink  = this.OverrideBlink,
                OverrideLookAt = this.OverrideLookAt,
                OverrideMouth  = this.OverrideMouth,
            };

            if (MaterialColorBinds != null)
                foreach (var b in MaterialColorBinds)
                    if (b != null) copy.MaterialColorBinds.Add(b.Clone());

            if (TextureTransformBinds != null)
                foreach (var b in TextureTransformBinds)
                    if (b != null) copy.TextureTransformBinds.Add(b.Clone());

            return copy;
        }
    }
}
