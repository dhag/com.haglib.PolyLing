// IVrmAnimationExporter.cs
// ============================================================
// VRM アニメーション（.vrma / VRMC_vrm_animation）書き出しの受け口
// ============================================================
//
// 【分離規約】
//   VRM パッケージ（com.vrmc.vrm / com.vrmc.gltf）との依存関係についての規約は
//   IVrm10Exporter.cs 冒頭のコメントを正典とする。ここには書き写さない。
//   本ファイルはその規約に従う 2 本目のインターフェースである。
//
// 【このインターフェースが姿勢を作らない理由】
//   UniVRM の VrmAnimationExporter は、AddFrame を呼んだ時点の
//   Transform の現在値を読む（VrmAnimationExporter.cs:71-79）。
//   したがって「フレーム i の姿勢を骨格へ作る」責務は呼び出し側に残す。
//   VrmAnimationMenu.cs:114-120 が BVH で行っているのと同じ形。
//
// 【骨格に求めること】
//   ・skeletonRoot 自身は書き出しの対象外。
//     gltfExporter.Export が Traverse().Skip(1) で根を外すため
//     （gltfExporter.cs:247-249）、Humanoid ボーンは必ず根の子孫に置く。
//   ・ノード名は階層内で一意。
//     VrmAnimationExporter がノード索引を名前で逆引きする
//     （VrmAnimationExporter.cs:97, 117, 139）。
//   ・レスト姿勢では全ノードのローカル回転が単位であること。
//     VRMA の回転は正規化 Humanoid のローカル回転として解釈されるため、
//     レストで単位でないと受け側でずれる。
//   ・メッシュ（Renderer）を持たせない。
//     VrmAnimationExporter.Export は base.Export()
//     （VrmAnimationExporter.cs:83）でメッシュとマテリアルまで載せる。
//
// 【設定を引数に取らない理由】
//   倍率などは骨格を組む側（PolyLing.Runtime）で吸収済みの値として渡る。
//   実装側に渡すべき設定が現状 1 つも無いので、引数を置かない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Vrm
{
    /// <summary>
    /// VRM アニメーション（.vrma）エクスポータのインターフェース。
    /// 規約は IVrm10Exporter.cs 冒頭のコメントを正典とする。
    /// </summary>
    public interface IVrmAnimationExporter
    {
        /// <summary>
        /// 実際に書き出せる実装が登録されているか。
        /// false のとき UI は VRMA 出力を選ばせないこと。
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 骨格とフレーム列から .vrma（GLB）を書き出す。
        /// </summary>
        /// <param name="skeletonRoot">骨格の根。書き出しには含まれない。</param>
        /// <param name="humanBones">Humanoid ボーン → 骨格上の Transform。</param>
        /// <param name="hipsPositionParent">Hips の平行移動を測る基準。通常は skeletonRoot の Transform。</param>
        /// <param name="timesSec">各フレームの時刻（秒・昇順）。</param>
        /// <param name="poseFrame">フレーム番号を受け取り、骨格をその姿勢にする。</param>
        /// <param name="outputPath">出力先ファイルパス。</param>
        VrmAnimationExportResult Export(
            GameObject skeletonRoot,
            IReadOnlyDictionary<HumanBodyBones, Transform> humanBones,
            Transform hipsPositionParent,
            IReadOnlyList<float> timesSec,
            Action<int> poseFrame,
            string outputPath);
    }
}
