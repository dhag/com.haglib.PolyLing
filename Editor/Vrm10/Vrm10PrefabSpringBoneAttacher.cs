// Editor/Vrm10/Vrm10PrefabSpringBoneAttacher.cs
// ============================================================
// プレファブ書き出しへ UniVRM の揺れもの部品を付ける実装
// ============================================================
//
// 【所属】PolyLing.Vrm10.Editor（VRM パッケージがあるときだけコンパイルされる）。
//   口は PolyLing.Editor の PrefabSpringBoneHook。規約は同ファイル冒頭と
//   Runtime/Poly_Ling_Main/VRM/IVrm10Exporter.cs 冒頭（4.）を正典とする。
//
// 【やること】
//   1. VRM10Object をプレファブと同じフォルダにアセットとして置く。
//      Vrm10Instance は Start で Vrm が null だと止まる（Vrm10Instance.cs の Start）。
//      一時物（HideAndDontSave）ではプレファブ保存で参照が切れるため、アセットにする。
//   2. 揺れものの組み立ては VRM 書き出しと同じ Vrm10SceneAssembler に任せる
//      （AssembleSpringBonesForPrefab）。ここで写しは作らない。
//
// 【付けない場合】
//   Humanoid の Head が無い。Vrm10Runtime はコンストラクタで Head を引き、
//   無いと例外を投げる（Vrm10Runtime.cs）。付けると Play で必ず落ちるので付けない。
//
// ============================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using Poly_Ling.Context;
using Poly_Ling.EditorIO;
using Poly_Ling.HierarchyIO;

namespace Poly_Ling.Vrm10Impl.EditorIO
{
    internal sealed class Vrm10PrefabSpringBoneAttacher : IPrefabSpringBoneAttacher
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            PrefabSpringBoneHook.Register(new Vrm10PrefabSpringBoneAttacher());
        }

        public PrefabSpringBoneResult Attach(
            ModelContext model, HierarchyBuildResult hierarchy,
            IReadOnlyDictionary<string, Transform> humanoidTransformMap,
            string assetDir, string modelName)
        {
            var result = new PrefabSpringBoneResult();

            if (hierarchy?.Root == null)
            {
                result.SkipReason = "ヒエラルキーが無い";
                return result;
            }

            if (humanoidTransformMap == null
                || !humanoidTransformMap.TryGetValue("Head", out var head) || head == null)
            {
                result.SkipReason = "Humanoid の Head が割り当てられていない（UniVRM の揺れの仕組みは Head が要る）";
                return result;
            }

            // 繰り返し書き出しても増えないよう、同じ名前のアセットがあればそれを使う。
            //   Avatar は {modelName}.asset なので名前をずらす。
            string vrmObjectPath = $"{assetDir}/{modelName}_vrm10.asset";
            var vrmObject = AssetDatabase.LoadAssetAtPath<VRM10Object>(vrmObjectPath);
            if (vrmObject == null)
            {
                vrmObject = ScriptableObject.CreateInstance<VRM10Object>();
                vrmObject.name = $"{modelName}_vrm10";
                AssetDatabase.CreateAsset(vrmObject, vrmObjectPath);
            }

            var report = Vrm10SceneAssembler.AssembleSpringBonesForPrefab(
                model, hierarchy, humanoidTransformMap, vrmObject);

            result.Warnings.AddRange(report.Warnings);
            if (report.MissingRequiredBones.Count > 0)
            {
                result.Warnings.Add("Humanoid の必須ボーンが不足: "
                                    + string.Join(", ", report.MissingRequiredBones));
            }

            result.Attached      = true;
            result.SpringCount   = report.SpringCount;
            result.JointCount    = report.SpringBoneJointCount;
            result.ColliderCount = report.SpringBoneColliderCount;
            return result;
        }
    }
}
