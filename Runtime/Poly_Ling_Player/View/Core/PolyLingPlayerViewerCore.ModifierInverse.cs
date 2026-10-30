// PolyLingPlayerViewerCore.ModifierInverse.cs
// Player ビューアのコア：逆方向（出力の編集 → 入力。作り直し整備計画 段 6）のための見本を作る口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【何をするか】
//   作り直しと同じ生成（図形生成はファクトリ、頂点へ藤壺はハンドラ）で出力を作り、
//   書き戻し（PrimitiveMeshReplaceExisting）と同じ変換（ApplyReplaceTransforms）を掛けて、
//   出力の格納空間での「正方向の結果」を返す。モデルには何も足さない。
//   ディスパッチャが ModifierFrameTrace を立てた中で呼ぶので、枠と行列が控えられる。

using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    public partial class PolyLingPlayerViewerCore
    {
        /// <summary>
        /// 生成コマンドから、出力の格納空間での正方向の結果を作る（モデルは変えない）。
        /// 戻り値は失敗理由。
        /// </summary>
        private string BuildModifierTemplate(
            PanelCommand cmd, MeshContext target, Poly_Ling.Ops.ModifierSpace.WriteBack spaceWb, out MeshObject mo)
        {
            mo = null;
            if (cmd == null) return "コマンドがありません";
            if (target == null) return "出力がありません";

            Vector3 worldPos, poseRot, poseScl;
            switch (cmd)
            {
                case CreateImagePlateCommand _:
                    return "画像の板は逆方向を持ちません";

                case CreatePrimitiveMeshCommand c:
                    mo = PrimitiveMeshFactory.Build(
                        c, forPreview: false,
                        resolvePlaceSources: ResolvePlaceSourcesForCommand,
                        resolveBeltSource:   ResolveBeltSourceForCommand,
                        resolveSurfacePaste: ResolveSurfacePasteForCommand);
                    if (mo == null) return $"{c.ShapeName} を作り直せませんでした";
                    worldPos = c.Placement.WorldPosition;
                    poseRot  = c.PoseRotation;
                    poseScl  = c.PoseScale;
                    break;

                case CreateVertexBillboardPlaceCommand vb:
                {
                    EnsureVertexBillboardPlaceHandler();
                    var h = _vertexBillboardPlaceHandler;
                    if (h == null) return "頂点へ藤壺ハンドラがありません";
                    // 位置はバインド姿勢の CPU の値で読む（作成・作り直しと同じ。段 E・段 G）。
                    ActiveProject?.CurrentModel?.ComputeWorldMatrices();
                    if (!h.BuildFromCommand(vb, out mo, out string reason)) return reason;
                    worldPos = vb.Placement.WorldPosition;
                    poseRot  = Vector3.zero;
                    poseScl  = Vector3.one;
                    break;
                }

                case EdgeRibbonFaceCommand er:
                {
                    // 帯（段 14）：作成・作り直し（ExecuteEdgeRibbonFace）と同じく、バインド姿勢の CPU の値で読む（段 E・段 G）。
                    EnsureEdgeRibbonFaceHandler();
                    var h = _edgeRibbonFaceHandler;
                    if (h == null) return "辺から帯面ハンドラがありません";
                    ActiveProject?.CurrentModel?.ComputeWorldMatrices();
                    if (!h.BuildFromCommand(er, out mo, out string reason)) return reason;
                    worldPos = er.Placement.WorldPosition;
                    poseRot  = Vector3.zero;
                    poseScl  = Vector3.one;
                    break;
                }

                default:
                    return $"{cmd.GetType().Name} は逆方向の見本を作れません";
            }

            // 新規オブジェクト・新規モデルとして作った項目は、位置と焼かなかった回転・拡大を
            // 出力の姿勢が持っている。書き戻しと同じく頂点へは掛けない
            // （PlayerCommandDispatcher.StripPoseOwnedByOutput。段 8）。
            PrimitiveAddMode addMode =
                cmd is CreatePrimitiveMeshCommand pcm ? pcm.Placement.AddMode
              : cmd is CreateVertexBillboardPlaceCommand vbc ? vbc.Placement.AddMode
              : cmd is EdgeRibbonFaceCommand erc ? erc.Placement.AddMode
              : PrimitiveAddMode.ReplaceExisting;
            if (addMode == PrimitiveAddMode.NewObject || addMode == PrimitiveAddMode.NewModel)
            {
                worldPos = Vector3.zero;
                poseRot  = Vector3.zero;
                poseScl  = Vector3.one;
            }

            // 見本は形を比べるためのもの。スキンドの出力でもウェイトの欠けでは止めない
            // （試しの作り直しは梯子の点列で渡すので、ウェイトを位置で引けないことがある）。
            return ApplyReplaceTransforms(mo, worldPos, poseRot, poseScl, target, spaceWb, checkWeights: false);
        }
    }
}
