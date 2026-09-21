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
    [PLCommand(Category = "geometry.topology", Effects = PLCommandEffect.Topology | PLCommandEffect.CreatesObject, Hazards = PLCommandHazard.ChangesVertexOrder | PLCommandHazard.InvalidatesMorphs, Verification = PLCommandVerification.Topology | PLCommandVerification.VertexCount | PLCommandVerification.Visual, Writes = PLWriteScope.Targets, Description = "2 つのメッシュオブジェクトにブーリアン演算（和 / 差 / 積）を行う。結果を入れたオブジェクトを対象として返す（新規なら追加したもの、置き換えなら A）。失敗したら失敗を返す。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "結果の頂点数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "結果の面数")]
    [PLResult("actualMergeThreshold", PLResultKind.Number, Description = "実際に採用した頂点結合距離。結合しない場合は0")]
    [PLResult("postprocessAttempts", PLResultKind.Integer, Description = "後処理の試行回数。接続不良が残れば距離を1/10にして最大4回試す。結合しない場合は0")]
    public class BooleanMeshCommand : PanelCommand
    {
        /// <summary>左辺（基準）オブジェクトの MasterIndex。差では削られる側。</summary>
        [PLParam(TextKey = "BooleanAMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 WriteWhen = "createNewMesh=false",
                 Description = "左辺（基準）オブジェクトの masterIndex。差では削られる側", Required = true)]
        public int AMasterIndex { get; }

        /// <summary>右辺オブジェクトの MasterIndex。差では削る側。</summary>
        [PLParam(TextKey = "BooleanBMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 WriteWhen = "deleteSourceB=true",
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
                 Description = "演算後に同一位置の頂点を結合し、T字解消と辺の接続検証を行う", Required = true)]
        public bool MergeVertices { get; }

        /// <summary>同一位置頂点マージのしきい値</summary>
        [PLParam(TextKey = "BooleanMergeThreshold",
                 Description = "同一位置とみなす距離の上限。接続が壊れる場合は距離を小さくして再試行する",
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

    /// <summary>
    /// ブーリアン演算で面が欠ける箇所を段階ごとに数える。モデルは変えない。
    /// 計測の中身は BooleanDiagnostics.cs の冒頭注記を参照。
    /// </summary>
    [PLCommand(Category = "query", Writes = PLWriteScope.None, Description = "ブーリアン演算を結果を捨てて実行し、面が欠ける箇所を段階ごとに数える。モデルは変えない。入力の穴、BSP の手順ごとの多角形数、多角形の平面の質（無効・先頭 3 頂点の法線のずれ）、結果の穴（頂点をほぼ完全一致でまとめた場合と mergeThreshold でまとめた場合。どちらも T 字解消後）、結果の頂点数を返す。")]
    [PLResult("inputHolesA",         PLResultKind.Integer,      Description = "入力 A の穴の数")]
    [PLResult("inputHolesB",         PLResultKind.Integer,      Description = "入力 B の穴の数")]
    [PLResult("polygonsA",           PLResultKind.Integer,      Description = "入力 A の多角形数")]
    [PLResult("polygonsB",           PLResultKind.Integer,      Description = "入力 B の多角形数")]
    [PLResult("invalidPlanesA",      PLResultKind.Integer,      Description = "入力 A で平面が無効な多角形の数")]
    [PLResult("invalidPlanesB",      PLResultKind.Integer,      Description = "入力 B で平面が無効な多角形の数")]
    [PLResult("stepNames",           PLResultKind.TextArray,    Description = "BSP の手順名")]
    [PLResult("stepCounts",          PLResultKind.IntegerArray, Description = "stepNames の各手順の直後に木に残る多角形の数")]
    [PLResult("resultPolygons",      PLResultKind.Integer,      Description = "結果の多角形数")]
    [PLResult("resultInvalidPlanes", PLResultKind.Integer,      Description = "結果で平面が無効な多角形の数")]
    [PLResult("resultSkewedPlanes",  PLResultKind.Integer,      Description = "結果で先頭 3 頂点の法線が多角形全体の法線から 1 度以上ずれた多角形の数")]
    [PLResult("resultVertices",      PLResultKind.Integer,      Description = "結果の頂点数（三角形ごとにばらばら）")]
    [PLResult("holesExact",          PLResultKind.Integer,      Description = "頂点をほぼ完全一致（1e-7）でまとめ、T 字解消した後の穴の数")]
    [PLResult("holesMerged",         PLResultKind.Integer,      Description = "頂点を mergeThreshold でまとめ、T 字解消した後の穴の数")]
    [PLResult("topologyStages", PLResultKind.TextArray, Description = "exact/merged の頂点結合直後とT字解消後。辺カウント各配列と添字が対応")]
    [PLResult("boundaryEdgeCounts", PLResultKind.IntegerArray, Description = "段階ごとの使用1回の境界辺数")]
    [PLResult("nonManifoldEdgeCounts", PLResultKind.IntegerArray, Description = "段階ごとの使用3回以上の辺数")]
    [PLResult("inconsistentWindingEdgeCounts", PLResultKind.IntegerArray, Description = "段階ごとの2面が同方向に使う辺数")]
    [PLResult("holeExactSizes",      PLResultKind.IntegerArray, Description = "holesExact の穴ごとの頂点数（先頭 40 個まで）", Optional = true)]
    [PLResult("holeExactCentroids",  PLResultKind.NumberArray,  Description = "holesExact の穴ごとの重心。A のローカル。x,y,z を 3 個ずつ", Optional = true)]
    [PLResult("holeMergedSizes",     PLResultKind.IntegerArray, Description = "holesMerged の穴ごとの頂点数（先頭 40 個まで）", Optional = true)]
    [PLResult("holeMergedCentroids", PLResultKind.NumberArray,  Description = "holesMerged の穴ごとの重心。A のローカル。x,y,z を 3 個ずつ", Optional = true)]
    [PLResult("timingNames",         PLResultKind.TextArray,    Description = "段階名")]
    [PLResult("timingMs",            PLResultKind.IntegerArray, Description = "timingNames の各段階の所要時間（ミリ秒）")]
    [PLResult("holeExactAreas",        PLResultKind.NumberArray,  Description = "holesExact の穴ごとの面積（輪をたどれないときは -1）", Optional = true)]
    [PLResult("holeExactInvalidTouch", PLResultKind.IntegerArray, Description = "holesExact の穴ごとに、平面が無効な多角形の頂点と重なる穴の頂点の数", Optional = true)]
    [PLResult("replicaPolygons",       PLResultKind.Integer,      Description = "写した BSP の結果の多角形数")]
    [PLResult("replicaInvalidPlanes",  PLResultKind.Integer,      Description = "写した BSP の結果で平面が無効な多角形の数")]
    [PLResult("replicaMatches",        PLResultKind.Flag,         Description = "写した BSP の結果が元の結果と一致したか（多角形数・無効数・頂点位置の和）。false なら下の分岐の数は信用できない")]
    [PLResult("leafDiscardValid",      PLResultKind.Integer,      Description = "葉で捨てた多角形のうち平面が有効なもの")]
    [PLResult("leafDiscardInvalid",    PLResultKind.Integer,      Description = "葉で捨てた多角形のうち平面が無効なもの")]
    [PLResult("coplanarBackInvalid",   PLResultKind.Integer,      Description = "平面が無効なまま同一平面・裏向きに振り分けられた多角形の数")]
    [PLResult("invalidNodeReturns",    PLResultKind.Integer,      Description = "平面が無効な節で下の節を見ずに返した回数")]
    [PLResult("invalidNodePassed",     PLResultKind.Integer,      Description = "そのとき素通りした多角形の数")]
    [PLResult("fixedPolygons",         PLResultKind.Integer,      Description = "無効な平面を作り直して演算した結果の多角形数（fixInvalidPlanes のとき）")]
    [PLResult("fixedInvalidPlanes",    PLResultKind.Integer,      Description = "そのとき残った無効な平面の数")]
    [PLResult("fixedPlanesRebuilt",    PLResultKind.Integer,      Description = "作り直した平面の数")]
    [PLResult("fixedHolesExact",       PLResultKind.Integer,      Description = "そのときの穴（ほぼ完全一致でまとめる）。試していなければ -1")]
    [PLResult("fixedHolesMerged",      PLResultKind.Integer,      Description = "そのときの穴（mergeThreshold でまとめる）。試していなければ -1")]
    public class DiagnoseBooleanCommand : PanelCommand
    {
        [PLParam(Description = "左辺（基準）オブジェクトの masterIndex。差では削られる側", Required = true, IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int AMasterIndex { get; }

        [PLParam(Description = "右辺オブジェクトの masterIndex。差では削る側", Required = true, IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int BMasterIndex { get; }

        [PLParam(Description = "和 / 差 / 積のどれを行うか", Required = true)]
        public Poly_Ling.Ops.BooleanOpKind Op { get; }

        [PLParam(Description = "頂点をまとめる距離のしきい値（booleanMesh の mergeThreshold と同じ）", Required = true)]
        public float MergeThreshold { get; }

        [PLParam(Description = "平面の同一判定の許容量。0 以下で BooleanOps.DefaultEpsilon", Required = true)]
        public float Epsilon { get; }

        [PLParam(Description = "T 字解消の許容量（resolveTJunctions の tolerance と同じ）")]
        public float TJunctionTolerance { get; }

        [PLParam(Description = "計測用の試し。平面が無効な多角形の法線を頂点全体から求め直して演算し、穴の数が変わるかを返す。本体の演算は変えない")]
        public bool FixInvalidPlanes { get; }

        public DiagnoseBooleanCommand(
            int modelIndex, int aMasterIndex, int bMasterIndex,
            Poly_Ling.Ops.BooleanOpKind op, float mergeThreshold, float epsilon,
            float tJunctionTolerance = 0.0001f, bool fixInvalidPlanes = false)
            : base(modelIndex)
        {
            AMasterIndex       = aMasterIndex;
            BMasterIndex       = bMasterIndex;
            Op                 = op;
            MergeThreshold     = mergeThreshold;
            Epsilon            = epsilon;
            TJunctionTolerance = tJunctionTolerance;
            FixInvalidPlanes   = fixInvalidPlanes;
        }
    }

    // ================================================================
    // T 字接合の解消
    // ================================================================

    /// <summary>
    /// 辺の途中に乗っている頂点を、その辺を持つ面へ挿入する。
    ///
    /// ブーリアン（pb_CSG）は BSP で多角形を切るとき、切った側にだけ
    /// 頂点を足して隣の面に知らせないため、辺の途中に頂点が乗った状態が残る。
    /// その辺は隣と共有されないので境界として扱われ、結果が水密にならない。
    /// 立方体から立方体を引くだけでも境界ループが 1 つ残る。
    ///
    /// ブーリアン専用ではない。同じ状態は穴つなぎや面削除の後にも起きる。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "境界辺を、端点でつながる逆向きの境界辺に合わせて分割し、T字接合を解消する。正常な共有辺や無関係な頂点は対象外。頂点位置・頂点数・面数は変えず、UV/法線の隅参照を補間する。真の欠損面は補わない。")]
    [PLResult("inserted",      PLResultKind.Integer, Description = "挿入した点の数")]
    [PLResult("touchedFaces",  PLResultKind.Integer, Description = "点を挿入した面の数")]
    [PLResult("vertices",      PLResultKind.Integer, Description = "解消後の頂点数")]
    [PLResult("faces",         PLResultKind.Integer, Description = "解消後の面数")]
    [PLResult("boundaryLoops", PLResultKind.Integer, Description = "解消後の境界辺の連結成分数。閉じたループとは限らない")]
    public class ResolveTJunctionsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex。省くと現在の編集対象",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write)]
        public int MasterIndex { get; }

        [PLParam(Description = "辺に乗っているとみなす距離[m]。大きくすると乗っていない頂点まで拾って面がねじれる", Min = 0.0)]
        public float Tolerance { get; }

        public ResolveTJunctionsCommand(int modelIndex, int masterIndex = -1, float tolerance = 1e-4f)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            Tolerance   = tolerance;
        }
    }

    // ================================================================
    // Quad減面
    // ================================================================

    /// <summary>Quad保持減数化を実行して結果メッシュをモデルに追加する</summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.AddOnly, Description = "四角面を保ったまま面数を減らし、結果のメッシュをモデルへ足す。")]
    public class QuadDecimateCommand : PanelCommand
    {
        [PLParam(TextKey = "QuadDecimateSourceMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "面結合（辺指定）。選択辺を挟む 2 枚の面を 1 枚へ結合する。DeleteVertices で共有頂点を新しい面から外すかを選ぶ。")]
    public class FaceMergeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "選択頂点を共有する四角形 4 枚を、四隅を結ぶ四角形 1 枚へ張り替える。")]
    public class Quad4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "選択した三角形とそれを囲む三角形 3 枚を、外側の 3 頂点を結ぶ三角形 1 枚へ張り替える。")]
    public class Tri4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "選択頂点を消して、その頂点を囲む面を 1 枚の面へ張り替える。")]
    public class VertexDissolveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Effects = PLCommandEffect.Topology, Hazards = PLCommandHazard.MaySplitVertices | PLCommandHazard.InvalidatesMorphs, Verification = PLCommandVerification.VertexCount | PLCommandVerification.Normals | PLCommandVerification.UVSeams, Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.Targets, Description = "選択頂点を面ごとに独立したコピーへ分離する。")]
    public class SplitVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "選択頂点を消して穴を開ける。頂点につながる各辺の上に新しい頂点を作り、 元の面を張り替える。")]
    public class VertexHoleCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets, Description = "面の裏表を反転する。")]
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

        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.position", Writes = PLWriteScope.Targets, Description = "選択頂点を軸ごとに整列する。")]
    public class AlignVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "geometry.position", Writes = PLWriteScope.Targets, Description = "選択した辺・線分のつながりを平滑化する。")]
    public class SmoothEdgesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Category = "io.import", Writes = PLWriteScope.AddOnly, Description = "PMX ファイルを読み込む。作業フォルダの下だけを読める。")]
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
