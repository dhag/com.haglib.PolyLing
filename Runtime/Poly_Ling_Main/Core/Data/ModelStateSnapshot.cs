// ModelStateSnapshot.cs
// モデルがいまどこまで作られているかを、実データから取り出した写し。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何に使うか】
//   利用シーンの stateAssumptions（想定するモデル状態）と突き合わせ、食い違えば警告する
//   （PolyLing_利用シーン_カテゴライズ設計方針.md 7 節）。
//   序盤・中盤・終盤のような一本の進捗値ではなく、独立した状態を並べて持つ。
//
// 【取り出し元】（すべて実データ。宣言や推測ではない）
//   hasBones          … ModelContext.HasBones
//   hasSkinWeights    … いずれかの MeshContext.IsSkinned
//   hasMorphs         … ModelContext.HasMorphExpressions、またはモーフ種別のメッシュがある
//   hasHumanoid       … HumanoidMapping が空でない
//   humanoidComplete  … HumanoidMapping.CanCreateAvatar（必須ボーンが揃っている）
//   hasCustomRig      … ボーンはあるが Humanoid 割当が空
//   hasSpringBones    … いずれかの MeshObject.SpringBoneJoint / SpringBoneChainRoot が非 null
//   hasMirrorRelation … いずれかの MeshContext.IsMirrored、または ModelContext.MirrorPairs がある
//   topologyLocked    … hasSkinWeights または hasMorphs（頂点の増減でウェイトかモーフが壊れる状態）
//
//   方針書 7.2 の hasAnimation と exportReady は、モデルに持つ欄と判定の基準が無いので扱わない。
//
// 【足すとき】
//   Names に名前を足し、Capture と TryGet に取り出しを足す。利用シーンの stateAssumptions は
//   Names にある名前だけを受ける（setScene が検証する）。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;

namespace Poly_Ling.Data
{
    public sealed class ModelStateSnapshot
    {
        /// <summary>利用シーンの stateAssumptions に書ける名前。並びは表示順。</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "hasBones", "hasSkinWeights", "hasMorphs", "hasHumanoid", "humanoidComplete",
            "hasCustomRig", "hasSpringBones", "hasMirrorRelation", "topologyLocked",
        };

        public bool HasModel;
        public bool HasBones;
        public bool HasSkinWeights;
        public bool HasMorphs;
        public bool HasHumanoid;
        public bool HumanoidComplete;
        public bool HasCustomRig;
        public bool HasSpringBones;
        public bool HasMirrorRelation;
        public bool TopologyLocked;

        /// <summary>モデルから状態を取り出す。モデルが無ければ HasModel=false で全部 false。</summary>
        public static ModelStateSnapshot Capture(ModelContext model)
        {
            var s = new ModelStateSnapshot();
            if (model == null) return s;

            s.HasModel = true;
            s.HasBones = model.HasBones;
            s.HasMorphs = model.HasMorphExpressions || (model.Morphs?.Count ?? 0) > 0;

            var mapping = model.HumanoidMapping;
            s.HasHumanoid      = mapping != null && !mapping.IsEmpty;
            s.HumanoidComplete = s.HasHumanoid && mapping.CanCreateAvatar;
            s.HasCustomRig     = s.HasBones && !s.HasHumanoid;

            s.HasMirrorRelation = (model.MirrorPairs?.Count ?? 0) > 0;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;

                if (mc.IsSkinned)  s.HasSkinWeights    = true;
                if (mc.IsMirrored) s.HasMirrorRelation = true;

                var mo = mc.MeshObject;
                if (mo != null && (mo.SpringBoneJoint != null || mo.SpringBoneChainRoot != null))
                    s.HasSpringBones = true;
            }

            s.TopologyLocked = s.HasSkinWeights || s.HasMorphs;
            return s;
        }

        /// <summary>名前で状態を引く。Names に無い名前なら false を返す。</summary>
        public bool TryGet(string name, out bool value)
        {
            switch (name)
            {
                case "hasBones":          value = HasBones;          return true;
                case "hasSkinWeights":    value = HasSkinWeights;    return true;
                case "hasMorphs":         value = HasMorphs;         return true;
                case "hasHumanoid":       value = HasHumanoid;       return true;
                case "humanoidComplete":  value = HumanoidComplete;  return true;
                case "hasCustomRig":      value = HasCustomRig;      return true;
                case "hasSpringBones":    value = HasSpringBones;    return true;
                case "hasMirrorRelation": value = HasMirrorRelation; return true;
                case "topologyLocked":    value = TopologyLocked;    return true;
                default:                  value = false;             return false;
            }
        }

        /// <summary>
        /// コマンドの危険性のうち、いまのモデル状態で実際に壊すものがある組み合わせを返す。
        /// 例：InvalidatesMorphs を持つコマンドを、モーフがあるモデルで実行する。
        /// 対応：InvalidatesSkinWeights と ChangesBindPose → hasSkinWeights、InvalidatesMorphs → hasMorphs、
        ///       BreaksMirrorRelation → hasMirrorRelation。
        /// </summary>
        public List<string> ConflictsWith(PLCommandHazard hazards)
        {
            var list = new List<string>();
            if (hazards == PLCommandHazard.None || !HasModel) return list;
            if ((hazards & PLCommandHazard.InvalidatesSkinWeights) != 0 && HasSkinWeights)
                list.Add("InvalidatesSkinWeights:hasSkinWeights");
            if ((hazards & PLCommandHazard.ChangesBindPose) != 0 && HasSkinWeights)
                list.Add("ChangesBindPose:hasSkinWeights");
            if ((hazards & PLCommandHazard.InvalidatesMorphs) != 0 && HasMorphs)
                list.Add("InvalidatesMorphs:hasMorphs");
            if ((hazards & PLCommandHazard.BreaksMirrorRelation) != 0 && HasMirrorRelation)
                list.Add("BreaksMirrorRelation:hasMirrorRelation");
            return list;
        }

        public static bool IsKnownName(string name)
        {
            foreach (var n in Names) if (string.Equals(n, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
