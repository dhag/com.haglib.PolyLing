// PanelCommand.Topology.cs
// 位相・頂点編集（ブーリアン・Quad減面・パラメータを持たない／持つ実行系）の操作要求。
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
    // ブーリアン演算
    // ================================================================

    /// <summary>
    /// 2 つのメッシュオブジェクトにブーリアン演算（和 / 差 / 積）を行う。
    /// 演算は A のローカル空間で行い、結果も A の姿勢を引き継ぐ。
    ///
    /// CreateNewMesh が true なら新規メッシュオブジェクトに結果を格納し、
    /// A / B はそのまま残す。false なら A の中身を結果で置き換える。
    /// DeleteSourceB が true なら B を削除する。
    ///
    /// スキンドメッシュは対象にできない（ボーンウェイトが失われるため）。
    /// </summary>
    [PLCommand(Description = "2 つのメッシュオブジェクトにブーリアン演算（和 / 差 / 積）を行う。")]
    public class BooleanMeshCommand : PanelCommand
    {
        /// <summary>左辺（基準）オブジェクトの MasterIndex。差では削られる側。</summary>
        [PLParam(TextKey = "BooleanAMasterIndex",
                 Description = "左辺（基準）オブジェクトの masterIndex。差では削られる側", Required = true)]
        public int AMasterIndex { get; }

        /// <summary>右辺オブジェクトの MasterIndex。差では削る側。</summary>
        [PLParam(TextKey = "BooleanBMasterIndex",
                 Description = "右辺オブジェクトの masterIndex。差では削る側", Required = true)]
        public int BMasterIndex { get; }

        /// <summary>演算の種類</summary>
        [PLParam(TextKey = "BooleanOpKind",
                 Description = "和 / 差 / 積のどれを行うか", Required = true)]
        public Poly_Ling.Ops.BooleanOpKind Op { get; }

        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
        [PLParam(TextKey = "BooleanCreateNewMesh",
                 Description = "結果を新規オブジェクトに入れる", Required = true)]
        public bool CreateNewMesh { get; }

        /// <summary>true: 演算後に B を削除する</summary>
        [PLParam(TextKey = "BooleanDeleteSourceB",
                 Description = "演算後に右辺オブジェクトを削除する", Required = true)]
        public bool DeleteSourceB { get; }

        /// <summary>true: 演算後に同一位置頂点をマージする</summary>
        [PLParam(TextKey = "BooleanMergeVertices",
                 Description = "演算後に同一位置の頂点を結合する", Required = true)]
        public bool MergeVertices { get; }

        /// <summary>同一位置頂点マージのしきい値</summary>
        [PLParam(TextKey = "BooleanMergeThreshold",
                 Description = "同一位置とみなす距離のしきい値",
                 LimitKey = "Boolean.MergeThreshold", Required = true)]
        public float MergeThreshold { get; }

        /// <summary>平面の同一判定の許容量（pb_CSG の epsilon）</summary>
        [PLParam(TextKey = "BooleanEpsilon",
                 Description = "平面の同一判定の許容量。0 以下を渡すと BooleanOps.DefaultEpsilon が使われる", Required = true)]
        public float Epsilon { get; }

        public BooleanMeshCommand(
            int modelIndex,
            int aMasterIndex,
            int bMasterIndex,
            Poly_Ling.Ops.BooleanOpKind op,
            bool createNewMesh,
            bool deleteSourceB,
            bool mergeVertices,
            float mergeThreshold,
            float epsilon)
            : base(modelIndex)
        {
            AMasterIndex   = aMasterIndex;
            BMasterIndex   = bMasterIndex;
            Op             = op;
            CreateNewMesh  = createNewMesh;
            DeleteSourceB  = deleteSourceB;
            MergeVertices  = mergeVertices;
            MergeThreshold = mergeThreshold;
            Epsilon        = epsilon;
        }
    }

    // ================================================================
    // Quad減面
    // ================================================================

    /// <summary>Quad保持減数化を実行して結果メッシュをモデルに追加する</summary>
    [PLCommand(Description = "Quad保持減数化を実行して結果メッシュをモデルに追加する</summary>")]
    public class QuadDecimateCommand : PanelCommand
    {
        [PLParam(TextKey = "QuadDecimateSourceMasterIndex",
                 Description = "減面する描画オブジェクトの masterIndex", Required = true)]
        public int   SourceMasterIndex { get; }

        [PLParam(TextKey = "QuadDecimateTargetRatio",
                 Description = "残す面数の比率",
                 LimitKey = "QuadDecimate.TargetRatio", Required = true)]
        public float TargetRatio       { get; }

        [PLParam(TextKey = "QuadDecimateMaxPasses",
                 Description = "減面を繰り返す回数の上限",
                 LimitKey = "QuadDecimate.MaxPasses", Required = true)]
        public int   MaxPasses         { get; }

        [PLParam(TextKey = "QuadDecimateNormalAngleDeg",
                 Description = "法線を保つ角度のしきい値（度）",
                 LimitKey = "QuadDecimate.AngleDeg", Required = true)]
        public float NormalAngleDeg    { get; }

        [PLParam(TextKey = "QuadDecimateHardAngleDeg",
                 Description = "ハードエッジとみなす角度のしきい値（度）",
                 LimitKey = "QuadDecimate.AngleDeg", Required = true)]
        public float HardAngleDeg      { get; }

        [PLParam(TextKey = "QuadDecimateUvSeamThreshold",
                 Description = "UV シームとみなす差のしきい値",
                 LimitKey = "QuadDecimate.UvSeamThreshold", Required = true)]
        public float UvSeamThreshold   { get; }

        public QuadDecimateCommand(int modelIndex, int sourceMasterIndex,
            float targetRatio, int maxPasses,
            float normalAngleDeg, float hardAngleDeg, float uvSeamThreshold)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            TargetRatio       = targetRatio;
            MaxPasses         = maxPasses;
            NormalAngleDeg    = normalAngleDeg;
            HardAngleDeg      = hardAngleDeg;
            UvSeamThreshold   = uvSeamThreshold;
        }
    }

    // ================================================================
    // 位相編集（パラメータを持たない実行系）
    //
    // 【対象の指定】
    //   MasterIndices は「実行時点の選択オブジェクトと一致すること」を要求する
    //   （照合方式）。受け口は一致しなければ失敗理由を返し、選択を書き換えない。
    //   リモート／MCP から呼ぶときは先に SelectMeshCommand で選択を作る。
    //
    // 【要素の指定】
    //   どの頂点・辺・面に効くかは各メッシュの Selection が持つ。P7 で明示化する。
    //
    // ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    // ローカル発行時は null / 空でよい（照合をスキップする）。
    // ================================================================

    /// <summary>
    /// 面結合（辺指定）。選択辺を挟む 2 枚の面を 1 枚へ結合する。
    /// 共有頂点（選択辺の両端）の扱いを DeleteVertices で選ぶ。
    ///   true  … ほかの面が使っていても新しい面から外す。どの面からも使われなくなった
    ///           頂点だけが消える（FaceMergeCollapseOps）。
    ///   false … ほかの面が使っていない共有頂点だけを外して消す（FaceMergeOps）。
    /// どちらも、外すと面にならない場合（三角形同士 → 四角形）は外さない。
    /// 実処理は FaceMergeTool。対象は選択中の描画オブジェクト全部。
    ///
    /// 【DeleteVertices を必須にしている理由】
    ///   旧 FaceMergeCommand（DeleteVertices なし）は false 相当の動作だった。既定値を置くと、
    ///   値を渡さずに呼んでいた呼び出しの動作が黙って変わる。必須にして指定漏れを失敗させる。
    ///   旧 FaceMergeCollapseCommand は DeleteVertices = true に置き換えた。
    /// </summary>
    [PLCommand(Description = "面結合（辺指定）。選択辺を挟む 2 枚の面を 1 枚へ結合する。DeleteVertices で共有頂点を新しい面から外すかを選ぶ。")]
    public class FaceMergeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices  { get; }

        [PLParam(TextKey = "DeleteVertices",
                 Description = "true: 共有頂点をほかの面が使っていても新しい面から外す（使われなくなった頂点は消える）。"
                             + "false: ほかの面が使っていない共有頂点だけを外して消す",
                 Required = true)]
        public bool    DeleteVertices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds      { get; }

        public FaceMergeCommand(int modelIndex, int[] masterIndices, bool deleteVertices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices ?? System.Array.Empty<int>();
            DeleteVertices = deleteVertices;
            ObjectIds      = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を共有する四角形 4 枚を、四隅を結ぶ四角形 1 枚へ張り替える。
    /// 実処理は Quad4To1Tool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    [PLCommand(Description = "選択頂点を共有する四角形 4 枚を、四隅を結ぶ四角形 1 枚へ張り替える。")]
    public class Quad4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        public Quad4To1Command(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択した三角形とそれを囲む三角形 3 枚を、外側の 3 頂点を結ぶ三角形 1 枚へ張り替える。
    /// 中点細分割の逆操作。実処理は Tri4To1Tool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    [PLCommand(Description = "選択した三角形とそれを囲む三角形 3 枚を、外側の 3 頂点を結ぶ三角形 1 枚へ張り替える。")]
    public class Tri4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        public Tri4To1Command(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を消して、その頂点を囲む面を 1 枚の面へ張り替える。
    /// 周りが閉じていない（境界の）頂点は対象外。
    /// 実処理は VertexDissolveTool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    [PLCommand(Description = "選択頂点を消して、その頂点を囲む面を 1 枚の面へ張り替える。")]
    public class VertexDissolveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        public VertexDissolveCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を面ごとに独立したコピーへ分離する。2 面以上に共有されている頂点が対象。
    ///
    /// 実処理（SplitVerticesTool）が編集対象メッシュ 1 本にしか効かないため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// 配列なのは他のコマンドと形をそろえて ObjectIds と対にするため。
    /// </summary>
    [PLCommand(Description = "選択頂点を面ごとに独立したコピーへ分離する。")]
    public class SplitVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        public SplitVerticesCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    // ================================================================
    // 位相・頂点編集（パラメータを持つ実行系）
    //
    // 対象の指定・要素の指定の扱いは上の「パラメータを持たない実行系」と同じ。
    // 設定値はコマンドが正典で、受け口は実行後にパネルの値へ戻す。
    // 1 呼び出しがパネルの状態に依存しないようにするため。
    // ================================================================

    /// <summary>
    /// 選択頂点を消して穴を開ける。頂点につながる各辺の上に新しい頂点を作り、
    /// 元の面を張り替える。実処理は VertexHoleTool。
    /// 対象は選択中の描画オブジェクト全部。
    /// </summary>
    [PLCommand(Description = "選択頂点を消して穴を開ける。頂点につながる各辺の上に新しい頂点を作り、 元の面を張り替える。")]
    public class VertexHoleCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        /// <summary>
        /// 新しい頂点を置く位置の比率。1 が選択頂点の位置、0 が辺の反対側（根元）。
        /// 小さいほど穴が大きくなる。
        /// </summary>
        [PLParam(TextKey = "VertexHoleRatio",
                 Description = "穴の位置比率。1 = 選択頂点の位置、0 = 辺の根元。既定は 0.5",
                 LimitKey = "VertexHole.Ratio")]
        public float   Ratio { get; }

        public VertexHoleCommand(
            int modelIndex, int[] masterIndices,
            float ratio        = 0.5f,
            ulong[] objectIds  = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Ratio         = ratio;
        }
    }

    /// <summary>
    /// 面の裏表を反転する。実処理は FlipFaceTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（FlipFaceTool.cs:93）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Description = "面の裏表を反転する。")]
    public class FlipFaceCommand : PanelCommand
    {
        /// <summary>反転する範囲。</summary>
        public enum FlipScope
        {
            /// <summary>選択されている面だけ。</summary>
            Selected,
            /// <summary>メッシュの全面。</summary>
            All
        }

        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "FlipFaceScope",
                 Description = "反転する範囲。Selected / All", Required = true)]
        public FlipScope Scope { get; }

        public FlipFaceCommand(
            int modelIndex, int[] masterIndices,
            FlipScope scope,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Scope         = scope;
        }
    }

    /// <summary>
    /// 選択頂点を軸ごとに整列する。実処理は AlignVerticesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（AlignVerticesTool.cs:141）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Description = "選択頂点を軸ごとに整列する。")]
    public class AlignVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "AlignX", Description = "X 座標をそろえる")]
        public bool      AlignX { get; }

        [PLParam(TextKey = "AlignY", Description = "Y 座標をそろえる")]
        public bool      AlignY { get; }

        [PLParam(TextKey = "AlignZ", Description = "Z 座標をそろえる")]
        public bool      AlignZ { get; }

        /// <summary>そろえる先の決め方。</summary>
        [PLParam(TextKey = "AlignMode",
                 Description = "そろえる先。Average / Min / Max", Required = true)]
        public AlignMode Mode   { get; }

        public AlignVerticesCommand(
            int modelIndex, int[] masterIndices,
            bool alignX, bool alignY, bool alignZ,
            AlignMode mode,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            AlignX        = alignX;
            AlignY        = alignY;
            AlignZ        = alignZ;
            Mode          = mode;
        }
    }

    /// <summary>
    /// 選択した辺・線分のつながりを平滑化する。実処理は SmoothEdgesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（SmoothEdgesTool.cs:116）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Description = "選択した辺・線分のつながりを平滑化する。")]
    public class SmoothEdgesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SmoothEdgesStrength",
                 Description = "平滑化の強度", LimitKey = "SmoothEdges.Strength")]
        public float Strength     { get; }

        [PLParam(TextKey = "SmoothEdgesIterations",
                 Description = "平滑化の反復回数", LimitKey = "SmoothEdges.Iterations")]
        public int   Iterations   { get; }

        [PLParam(TextKey = "SmoothEdgesFixEndpoints",
                 Description = "チェーンの端点を動かさない")]
        public bool  FixEndpoints { get; }

        [PLParam(TextKey = "SmoothEdgesLockX", Description = "X 方向の移動を禁じる")]
        public bool  LockX { get; }

        [PLParam(TextKey = "SmoothEdgesLockY", Description = "Y 方向の移動を禁じる")]
        public bool  LockY { get; }

        [PLParam(TextKey = "SmoothEdgesLockZ", Description = "Z 方向の移動を禁じる")]
        public bool  LockZ { get; }

        public SmoothEdgesCommand(
            int modelIndex, int[] masterIndices,
            float strength, int iterations,
            bool fixEndpoints = true,
            bool lockX = false, bool lockY = false, bool lockZ = false,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Strength      = strength;
            Iterations    = iterations;
            FixEndpoints  = fixEndpoints;
            LockX         = lockX;
            LockY         = lockY;
            LockZ         = lockZ;
        }
    }

    /// <summary>
    /// PMX ファイルを読み込む。
    ///
    /// 【なぜ ImportPmxCommand と別名か】
    ///   Poly_Ling.Commands.ImportPmxCommand（ICommand）が既にある。
    ///   あちらはコールバックを 2 本受け取る内部用で、外から送れない。
    ///   本コマンドは受け口でそちらを組み立てて CommandQueue へ積む。
    ///
    /// 【読込後の処理】
    ///   PlayerImportSubPanel.PostOptions の 4 項目を平坦化して持つ。
    ///   Poly_Ling.Data から View 側の入れ子クラスへ依存しないため。
    /// </summary>
    [PLCommand(Description = "PMX ファイルを読み込む。作業フォルダの下だけを読める。")]
    public class ImportPmxFileCommand : PanelCommand
    {
        [PLParam(Description = "読み込む PMX のパス。作業フォルダからの相対でも絶対でもよい",
                 Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "読み込み設定。省いた項目は既定値のまま")]
        public Poly_Ling.PMX.PMXImportSettings Settings { get; }

        [PLParam(Description = "読込後にボーン名から Humanoid の割当を自動で行う")]
        public bool HumanoidAutoMap { get; }

        [PLParam(Description = "読込後に原点 CSV を適用する")]
        public bool ApplyOriginCsv { get; }

        [PLParam(Description = "適用する原点 CSV のパス。ApplyOriginCsv が false のときは使わない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "原点 CSV の回転列（rotX,rotY,rotZ）も適用する")]
        public bool OriginCsvIncludeRotation { get; }

        public ImportPmxFileCommand(
            int modelIndex,
            string filePath,
            Poly_Ling.PMX.PMXImportSettings settings = null,
            bool humanoidAutoMap = false,
            bool applyOriginCsv = false,
            string originCsvPath = "",
            bool originCsvIncludeRotation = false)
            : base(modelIndex)
        {
            FilePath                 = filePath ?? "";
            Settings                 = settings ?? Poly_Ling.PMX.PMXImportSettings.CreateDefault();
            HumanoidAutoMap          = humanoidAutoMap;
            ApplyOriginCsv           = applyOriginCsv;
            OriginCsvPath            = originCsvPath ?? "";
            OriginCsvIncludeRotation = originCsvIncludeRotation;
        }
    }
}
