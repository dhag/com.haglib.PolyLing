// PanelCommand.UndoRedo.cs
// Undo / Redo の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // Undo / Redo
    // ================================================================

    /// <summary>
    /// 直前の操作を 1 段戻す。モデル非依存なので ModelIndex は 0 固定。
    /// 戻せる履歴が無いときは失敗として返る。
    /// </summary>
    [PLCommand(Description = "直前の操作を 1 段戻す。モデル非依存なので ModelIndex は 0 固定。")]
    public class PerformUndoCommand : PanelCommand
    {
        public PerformUndoCommand() : base(0) { }
    }

    /// <summary>戻した操作を 1 段やり直す。</summary>
    [PLCommand(Description = "戻した操作を 1 段やり直す。</summary>")]
    public class PerformRedoCommand : PanelCommand
    {
        public PerformRedoCommand() : base(0) { }
    }

    /// <summary>
    /// 穴の頂点数を基準穴に合わせる。穴つなぎ（BridgeLoopOps）が要求する
    /// 「2 つの穴の頂点数が同じ」を満たすための前処理。
    ///
    /// 変更されるのは対象穴のメッシュだけで、基準穴は頂点数を読むだけ。
    /// 基準と対象が同じメッシュにあってもよい。
    /// </summary>
    [PLCommand(Description = "穴の頂点数を基準の穴に合わせる。穴つなぎは 2 つの穴の頂点数が同じであることを要求するので、その前処理に使う。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class MatchHoleRingCountCommand : PanelCommand
    {
        /// <summary>基準穴のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "HoleRingBaseMesh", Description = "基準穴のメッシュ索引", Required = true)]
        public int BaseMeshIndex { get; }

        /// <summary>基準穴の種頂点。</summary>
        [PLParam(TextKey = "HoleRingBaseVertex", Description = "基準穴の種頂点番号", Required = true)]
        public int BaseVertex { get; }

        /// <summary>基準穴の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "HoleRingBaseDirHint", Description = "基準穴の進行方向ヒント頂点。-1 で指定なし")]
        public int BaseDirectionHint { get; }

        /// <summary>対象穴のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "HoleRingTargetMesh", Description = "対象穴のメッシュ索引", Required = true)]
        public int TargetMeshIndex { get; }

        /// <summary>対象穴の種頂点。</summary>
        [PLParam(TextKey = "HoleRingTargetVertex", Description = "対象穴の種頂点番号", Required = true)]
        public int TargetVertex { get; }

        /// <summary>対象穴の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "HoleRingTargetDirHint", Description = "対象穴の進行方向ヒント頂点。-1 で指定なし")]
        public int TargetDirectionHint { get; }

        /// <summary>三角形を三角形へ分割するか。false なら四角へ分割する。</summary>
        [PLParam(TextKey = "HoleRingSplitTri", Description = "三角形を三角形へ分割する")]
        public bool SplitTriangleIntoTriangles { get; }

        public MatchHoleRingCountCommand(
            int modelIndex,
            int baseMeshIndex, int baseVertex, int baseDirectionHint,
            int targetMeshIndex, int targetVertex, int targetDirectionHint,
            bool splitTriangleIntoTriangles = true)
            : base(modelIndex)
        {
            BaseMeshIndex              = baseMeshIndex;
            BaseVertex                 = baseVertex;
            BaseDirectionHint          = baseDirectionHint;
            TargetMeshIndex            = targetMeshIndex;
            TargetVertex               = targetVertex;
            TargetDirectionHint        = targetDirectionHint;
            SplitTriangleIntoTriangles = splitTriangleIntoTriangles;
        }
    }

    /// <summary>
    /// 面を消す。面削除モードのクリック 1 回ぶんに相当するが、複数枚をまとめて渡せる。
    /// 消すのは指定メッシュの面だけで、他のオブジェクトの選択は巻き込まない。
    /// </summary>
    [PLCommand(Description = "面を消す。面削除モードのクリック 1 回ぶんに相当するが、複数枚をまとめて渡せる。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class DeleteFacesCommand : PanelCommand
    {
        /// <summary>対象メッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "DeleteFacesMesh", Description = "対象メッシュの索引", Required = true)]
        public int MeshIndex { get; }

        /// <summary>消す面の番号。</summary>
        [PLParam(TextKey = "DeleteFacesIndices", Description = "消す面の番号", Required = true)]
        public int[] FaceIndices { get; }

        public DeleteFacesCommand(int modelIndex, int meshIndex, int[] faceIndices)
            : base(modelIndex)
        {
            MeshIndex   = meshIndex;
            FaceIndices = faceIndices;
        }
    }

    /// <summary>
    /// 歪み複製。複製元を歪ませながら複数組つくり、モデルへ挿入する。
    /// 単一のメッシュを返さないので図形生成コマンドとは別系統にする。
    /// 作業軸はモデル側の状態なのでディスパッチャが解決する。
    /// </summary>
    [PLCommand(Description = "歪み複製。複製元を歪ませながら複数組つくり、モデルへ挿入する。")]
    public class CreateObjectArrayCommand : PanelCommand
    {
        /// <summary>生成パラメータ。</summary>
        [PLParam(TextKey = "ObjectArray", Description = "歪み複製のパラメータ", Required = true)]
        public Poly_Ling.Tools.ObjectArray.ObjectArrayParams Params { get; }

        /// <summary>複製元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "ObjectArraySources", Description = "複製元オブジェクトの索引", Required = true)]
        public int[] SourceMasterIndices { get; }

        /// <summary>
        /// 掛ける歪みの識別子。DeformerRegistry の検索キー
        /// （Rotate / Move / Scale / Bend / Twist / Wave）。
        ///
        /// IMeshDeformer をそのまま持つとスキーマに出せない
        /// （インターフェースなので実体を決められない）ため、名前で受ける。
        /// 実体は受け口が DeformerRegistry.Create で起こす。
        /// 歪みのパラメータは別のコマンド（ApplyDeformCommand の派生）で設定する。
        /// </summary>
        [PLParam(TextKey = "ObjectArrayDeformerName",
                 Description = "掛ける歪みの名前。Rotate / Move / Scale / Bend / Twist / Wave",
                 Required = true)]
        public string DeformerName { get; }

        /// <summary>DeformerName から起こした歪み。受け口はこちらを使う。</summary>
        public Poly_Ling.Tools.Deformers.IMeshDeformer Deformer
            => Poly_Ling.Tools.Deformers.DeformerRegistry.Create(DeformerName);

        public CreateObjectArrayCommand(
            int modelIndex,
            Poly_Ling.Tools.ObjectArray.ObjectArrayParams @params,
            int[] sourceMasterIndices,
            string deformerName)
            : base(modelIndex)
        {
            Params              = @params;
            SourceMasterIndices = sourceMasterIndices;
            DeformerName        = deformerName ?? "";
        }
    }
}
