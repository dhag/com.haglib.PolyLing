// Editor/HierarchyIO/PrefabSpringBoneHook.cs
// ============================================================
// プレファブ書き出しへ揺れもの（スプリングボーン）を付ける口
// ============================================================
//
// 【なぜ口を分けるのか】
//   揺れを Play で動かすのは UniVRM の Vrm10Instance と VRM10SpringBone 系の
//   コンポーネントで、VRM パッケージ（com.vrmc.vrm）の型。
//   PolyLing.Editor がそれらの型を直接使うと、VRM パッケージが無い環境で
//   PolyLing.Editor 全体がコンパイルできなくなる。
//   そこでここには VRM の型を出さない口だけを置き、実装は
//   PolyLing.Vrm10.Editor（VRM パッケージがあるときだけコンパイルされる）が
//   エディタ起動時に Register する。
//   規約の正典は Runtime/Poly_Ling_Main/VRM/IVrm10Exporter.cs 冒頭（とくに 4.）。
//
// 【登録が無いとき】
//   VRM パッケージが無い。揺れものだけを付けず、プレファブは今までどおり
//   通常のヒューマノイド（Avatar + Animator）として書き出す。エラーにはしない。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.HierarchyIO;

namespace Poly_Ling.EditorIO
{
    /// <summary>揺れものを付けた結果。</summary>
    public sealed class PrefabSpringBoneResult
    {
        /// <summary>付けたら true。</summary>
        public bool Attached;

        /// <summary>付けなかった理由（付けたら null）。</summary>
        public string SkipReason;

        /// <summary>揺れの鎖の数。</summary>
        public int SpringCount;

        /// <summary>揺れる関節の数。</summary>
        public int JointCount;

        /// <summary>当たり判定の数。</summary>
        public int ColliderCount;

        /// <summary>警告（呼び出し側がレポートへ流す）。</summary>
        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>プレファブの一時ルートへ揺れものを付ける実装。VRM の型を出さないこと。</summary>
    public interface IPrefabSpringBoneAttacher
    {
        /// <summary>
        /// hierarchy.Root へ揺れものを付ける。プレファブとして保存する前に呼ぶ。
        /// </summary>
        /// <param name="humanoidTransformMap">humanName→Transform。不足関節を補完済みならその結果。</param>
        /// <param name="assetDir">プレファブと同じフォルダ（Assets/...）。付随アセットの保存先。</param>
        /// <param name="modelName">付随アセットのファイル名に使う。</param>
        PrefabSpringBoneResult Attach(
            ModelContext model, HierarchyBuildResult hierarchy,
            IReadOnlyDictionary<string, Transform> humanoidTransformMap,
            string assetDir, string modelName);
    }

    /// <summary>実装の登録先。</summary>
    public static class PrefabSpringBoneHook
    {
        /// <summary>登録された実装。VRM パッケージが無ければ null。</summary>
        public static IPrefabSpringBoneAttacher Attacher { get; private set; }

        /// <summary>PolyLing.Vrm10.Editor が [InitializeOnLoadMethod] で呼ぶ。</summary>
        public static void Register(IPrefabSpringBoneAttacher attacher) => Attacher = attacher;

        /// <summary>
        /// モデルが揺れる関節を 1 つでも持つか。
        /// 持たないモデルには何も付けない（揺れものの無いプレファブは今までと同じ中身にする）。
        /// 揺れる関節の格納は MeshObject.SpringBoneJoint（非 null＝鎖のメンバー）。
        /// </summary>
        public static bool HasSpringBones(ModelContext model)
        {
            if (model == null) return false;
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc?.MeshObject?.SpringBoneJoint != null) return true;
            }
            return false;
        }
    }
}
