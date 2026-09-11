// PanelCommand.Transform.cs
// 頂点移動・ピボット移動・スカルプト・作業軸・変形ギズモ・オブジェクト移動の操作要求。
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
    // 頂点移動
    // ================================================================

    /// <summary>
    /// 現在の選択頂点をデルタ値で移動する。Undo記録付き。
    ///
    /// 実処理は MoveToolHandler が持つ。マウス経路・数値入力経路と同じ
    /// UpdateAffectedVertices → BeginMove → ApplyDelta → EndMove を通すため、
    /// このコマンドは対象・移動量・マグネット設定だけを運ぶ。
    ///
    /// 対象頂点は「選択メッシュの選択要素」で、辺・面・線分の選択は
    /// SelectionState.Mode に従って頂点へ展開される。
    ///
    /// マグネットの 4 件はハンドラの UI 状態と同名だが、こちらが正典として
    /// 実行時に適用され、実行後に UI の値へ戻される。1 呼び出しが UI 状態に
    /// 依存しないようにするため（MCP の自己完結）。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "現在の選択頂点をデルタ値で移動する。")]
    public class MoveSelectedVerticesCommand : PanelCommand
    {
        public enum CoordSpace { Local, World }

        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]        MasterIndices      { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[]      ObjectIds          { get; }

        /// <summary>移動量</summary>
        [PLParam(TextKey = "MoveDelta",
                 Description = "選択頂点の移動量", Required = true)]
        public Vector3      Delta              { get; }

        /// <summary>
        /// Delta の座標空間。
        /// Local は MasterIndices[0] のローカル空間として解釈し、そのメッシュの
        /// WorldMatrix でワールドへ変換する。対象ごとに行列が違うため、基準は
        /// 先頭の 1 本に固定する。
        /// </summary>
        [PLParam(TextKey = "MoveCoordSpace",
                 Description = "Delta の座標空間。Local は MasterIndices[0] のローカル空間", Required = true)]
        public CoordSpace   Space              { get; }

        /// <summary>マグネットを使うか</summary>
        [PLParam(TextKey = "MoveUseMagnet",
                 Description = "選択外の周辺頂点も減衰させて引きずる。既定は false")]
        public bool         UseMagnet          { get; }

        /// <summary>マグネットの影響半径</summary>
        [PLParam(TextKey = "MoveMagnetRadius",
                 Description = "マグネットの影響半径。UseMagnet が false のときは使わない",
                 LimitKey = "Move.MagnetRadius")]
        public float        MagnetRadius       { get; }

        /// <summary>マグネットの減衰の形</summary>
        [PLParam(TextKey = "MoveMagnetFalloff",
                 Description = "マグネットの減衰の形。既定は Smooth")]
        public FalloffType  MagnetFalloff      { get; }

        /// <summary>マグネットの距離計算方式</summary>
        [PLParam(TextKey = "MoveMagnetDistanceMode",
                 Description = "マグネットの距離計算方式。Euclidean / Link。既定は Euclidean")]
        public DistanceMode MagnetDistanceMode { get; }

        /// <summary>
        /// 移動後に法線を再計算するか。
        /// マウス経路は再計算しないので、既定の false で同一結果になる。
        /// </summary>
        [PLParam(TextKey = "MoveRecalcNormals",
                 Description = "移動後に頂点法線を再計算する。既定は false")]
        public bool         RecalcNormals      { get; }

        public MoveSelectedVerticesCommand(
            int modelIndex, int[] masterIndices,
            Vector3 delta, CoordSpace space,
            bool recalcNormals = false,
            bool useMagnet = false,
            float magnetRadius = 0.5f,
            FalloffType magnetFalloff = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            Delta              = delta;
            Space              = space;
            RecalcNormals      = recalcNormals;
            UseMagnet          = useMagnet;
            MagnetRadius       = magnetRadius;
            MagnetFalloff      = magnetFalloff;
            MagnetDistanceMode = magnetDistanceMode;
        }
    }

    // ================================================================
    // ピボット移動
    // ================================================================

    /// <summary>
    /// ピボット（原点）をデルタ値で移動する。Undo記録付き。
    /// 対象の BoneTransform.Position を Delta 方向へ動かし、対象メッシュ（非スキンの
    /// MeshFilter）の頂点を「開始ワールド位置を保つ」よう再局所化する。直接の子は
    /// ワールド位置を保つよう補償される。
    ///
    /// 実処理は ObjectMoveTool（OriginOnly）が持つ。マウス経路と同じ実装を通すため、
    /// このコマンドは対象と移動量だけを運ぶ。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "ピボット（原点）をデルタ値で移動する。")]
    public class MovePivotCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]      MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[]    ObjectIds     { get; }

        /// <summary>ピボットの移動量</summary>
        [PLParam(TextKey = "PivotDelta",
                 Description = "ピボット（原点）の移動量", Required = true)]
        public Vector3    Delta         { get; }

        /// <summary>
        /// Delta の座標空間。
        /// Local は MasterIndices[0] のローカル空間として解釈し、そのメッシュの
        /// WorldMatrix でワールドへ変換する。対象ごとに行列が違うため、基準は
        /// 先頭の 1 本に固定する。
        /// </summary>
        [PLParam(TextKey = "PivotCoordSpace",
                 Description = "Delta の座標空間。Local は MasterIndices[0] のローカル空間", Required = true)]
        public MoveSelectedVerticesCommand.CoordSpace Space { get; }

        public MovePivotCommand(
            int modelIndex, int[] masterIndices,
            Vector3 delta, MoveSelectedVerticesCommand.CoordSpace space,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Delta         = delta;
            Space         = space;
        }
    }

    // ================================================================
    // スカルプトストローク
    // ================================================================

    /// <summary>
    /// スカルプトブラシを一連のワールド座標に沿って適用する。Undo記録付き。
    ///
    /// 実処理は SculptTool が持つ。マウス経路と同じ ApplyStrokeToMesh /
    /// CommitStroke を通すため、このコマンドは対象・点列・ブラシ設定だけを運ぶ。
    ///
    /// 【なぜワールド座標か】
    ///   マウス経路（ApplyBrush）は 1 点のブラシ中心を選択メッシュ全部へ掛け、
    ///   メッシュごとに WorldToLocal で変換する。点列をローカル座標 1 組で持つと
    ///   複数メッシュを 1 コマンドで表せない。ローカル化は適用側の仕事とする。
    ///
    /// 【ViewDirections】
    ///   BrushCenters と同じ並び・同じ長さのワールド視線方向。Draw モードの
    ///   反転補正（ApplyStrokeToMesh の viewDirLocal）に使う。空にすると補正を
    ///   行わないため、マウス経路と結果が変わる点に注意する。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "スカルプトブラシを一連のワールド座標に沿って適用する。")]
    public class SculptStrokeCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]        MasterIndices  { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[]      ObjectIds      { get; }

        /// <summary>ブラシ中心の列（ワールド空間）</summary>
        [PLParam(TextKey = "SculptBrushCenters",
                 Description = "ブラシ中心をストローク順に並べたもの（ワールド座標）", Required = true)]
        public Vector3[]    BrushCenters  { get; }

        /// <summary>視線方向の列（ワールド空間）。BrushCenters と同じ長さ。空で補正なし</summary>
        [PLParam(TextKey = "SculptViewDirections",
                 Description = "BrushCenters と同じ並び・同じ長さのワールド視線方向。空で Draw の反転補正を行わない")]
        public Vector3[]    ViewDirections { get; }

        /// <summary>スカルプトモード</summary>
        [PLParam(TextKey = "SculptMode",
                 Description = "ブラシの効き方", Required = true)]
        public SculptMode   Mode          { get; }

        /// <summary>ブラシ半径（ローカル空間単位）</summary>
        [PLParam(TextKey = "SculptBrushRadius",
                 Description = "ブラシ半径（対象のローカル空間単位）",
                 LimitKey = "Sculpt.BrushRadius", Required = true)]
        public float        BrushRadius   { get; }

        /// <summary>強度（0〜1）</summary>
        [PLParam(TextKey = "SculptStrength",
                 Description = "1 ストロークあたりの効きの強さ",
                 LimitKey = "Sculpt.Strength", Required = true)]
        public float        Strength      { get; }

        /// <summary>反転フラグ</summary>
        [PLParam(TextKey = "SculptInvert",
                 Description = "凹凸を反転する。既定は false")]
        public bool         Invert        { get; }

        /// <summary>フォールオフ種別</summary>
        [PLParam(TextKey = "SculptFalloff",
                 Description = "ブラシ中心からの減衰の形。既定は Gaussian")]
        public FalloffType  Falloff       { get; }

        /// <summary>ストローク終了後に法線を再計算するか</summary>
        [PLParam(TextKey = "SculptRecalcNormals",
                 Description = "ストローク後に頂点法線を再計算する。既定は true")]
        public bool         RecalcNormals { get; }

        public SculptStrokeCommand(
            int modelIndex, int[] masterIndices,
            Vector3[] brushCenters,
            SculptMode mode, float brushRadius, float strength,
            bool invert = false,
            FalloffType falloff = FalloffType.Gaussian,
            bool recalcNormals = true,
            Vector3[] viewDirections = null,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices ?? System.Array.Empty<int>();
            ObjectIds      = objectIds;
            ViewDirections = viewDirections;
            BrushCenters  = brushCenters;
            Mode          = mode;
            BrushRadius   = brushRadius;
            Strength      = strength;
            Invert        = invert;
            Falloff       = falloff;
            RecalcNormals = recalcNormals;
        }
    }

    // ================================================================
    // 作業軸
    //
    // 作業軸（WorkAxisContext）はモデルの頂点・選択を書き換えない。
    // 回転・拡大縮小・歪みのピボット源なので、どの軸を使うかが 1 呼び出しで
    // 確定するよう、差分ではなく状態の全指定にする。
    // 差分指定にすると「送る前の状態を知らないと結果が予測できない」ものになり、
    // SelectElementsCommand の Toggle と同じ問題を抱える。
    // ================================================================

    /// <summary>
    /// 作業軸の状態を指定した値へ差し替える。実処理は WorkAxisContext。
    ///
    /// 「選択重心へ移動」「ワールド軸へ整列」「リセット」も、呼び出し側で結果の
    /// 値を解決してからこのコマンドに載せる。専用コマンドを増やさず、
    /// 実行前の状態に依存しない形にそろえるため。
    ///
    /// Length は WorkAxisContext.Length が下限（MinLength）でクランプする。
    /// </summary>
    [PLCommand(Description = "作業軸の状態を指定した値へ差し替える。")]
    public class SetWorkAxisCommand : PanelCommand
    {
        [PLParam(TextKey = "WorkAxisOrigin",
                 Description = "軸の原点（ワールド座標）", Required = true)]
        public Vector3 Origin { get; }

        /// <summary>
        /// 軸の回転（度）。WorkAxisContext.EulerAngles と同じく Quaternion.Euler で解釈する。
        /// </summary>
        [PLParam(TextKey = "WorkAxisEulerAngles",
                 Description = "軸の回転（度）", Required = true)]
        public Vector3 EulerAngles { get; }

        [PLParam(TextKey = "WorkAxisLength",
                 Description = "軸長（ワールド単位）。下限は WorkAxisContext.MinLength でクランプされる")]
        public float Length { get; }

        [PLParam(TextKey = "WorkAxisVisible", Description = "ギズモを表示するか")]
        public bool IsVisible { get; }

        public SetWorkAxisCommand(
            int modelIndex,
            Vector3 origin, Vector3 eulerAngles,
            float length   = Poly_Ling.Context.WorkAxisContext.DefaultLength,
            bool isVisible = true)
            : base(modelIndex)
        {
            Origin      = origin;
            EulerAngles = eulerAngles;
            Length      = length;
            IsVisible   = isVisible;
        }
    }

    /// <summary>
    /// 作業軸ライブラリの登録名を呼び出して作業軸へ入れる。
    /// 表示フラグは変えない（WorkAxisEntry.ApplyTo と同じ）。
    /// </summary>
    [PLCommand(Description = "作業軸ライブラリの登録名を呼び出して作業軸へ入れる。")]
    public class RecallWorkAxisCommand : PanelCommand
    {
        [PLParam(TextKey = "WorkAxisName",
                 Description = "作業軸ライブラリの登録名", Required = true)]
        public string Name { get; }

        public RecallWorkAxisCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }

    // ================================================================
    // 変形ギズモ（選択頂点の回転・スケール）
    //
    // どちらも「実行時点の選択中の描画オブジェクト全件」に効く（RotateTool.cs:151 /
    // ScaleTool.cs:113 が model.SelectedDrawableMeshIndices を走査する）。
    // よって MasterIndices は選択集合と一致することを要求する。
    // ================================================================

    /// <summary>
    /// 選択頂点をピボット周りに回転させる。実処理は RotateTool。
    ///
    /// 【Snap を載せない理由】
    ///   RotateTool の UseSnap / SnapAngle（RotateTool.cs:65-66）は値を保持するだけで、
    ///   角度の丸めは入力側（RotateTool.EditorUI.cs:53,73-77 と
    ///   PlayerRotateSubPanel.Snap）が行う。回転数学は読まないので、
    ///   このコマンドは丸めたあとの最終角度だけを載せる。
    ///
    /// 【AxisMode】
    ///   false のとき Euler を、true のとき Axis と Angle を使う。
    ///   どちらの軸もワールド基準で、ピボットは対象の重心（UseOriginPivot が
    ///   true のときは基準メッシュのローカル原点）。
    /// </summary>
    [PLCommand(Description = "選択頂点をピボット周りに回転させる。")]
    public class RotateSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "RotateAxisMode",
                 Description = "true で Axis と Angle、false で Euler を使う。既定は false")]
        public bool    AxisMode { get; }

        [PLParam(TextKey = "RotateEuler",
                 Description = "回転角（度）。AxisMode が false のときだけ使う")]
        public Vector3 Euler    { get; }

        [PLParam(TextKey = "RotateAxis",
                 Description = "回転軸（ワールド）。AxisMode が true のときだけ使う")]
        public Vector3 Axis     { get; }

        [PLParam(TextKey = "RotateAngle",
                 Description = "軸まわりの回転角（度）。AxisMode が true のときだけ使う",
                 Min = -360.0, Max = 360.0)]
        public float   Angle    { get; }

        [PLParam(TextKey = "RotateUseOriginPivot",
                 Description = "基準メッシュのローカル原点をピボットにする。既定は false（選択の重心）")]
        public bool    UseOriginPivot { get; }

        [PLParam(TextKey = "RotateUseMagnet",
                 Description = "選択外の周辺頂点も減衰させて回す。既定は false")]
        public bool         UseMagnet          { get; }

        [PLParam(TextKey = "RotateMagnetRadius",
                 Description = "マグネットの影響半径。UseMagnet が false のときは使わない",
                 LimitKey = "Move.MagnetRadius")]
        public float        MagnetRadius       { get; }

        [PLParam(TextKey = "RotateMagnetFalloff",
                 Description = "マグネットの減衰の形。既定は Smooth")]
        public FalloffType  MagnetFalloff      { get; }

        [PLParam(TextKey = "RotateMagnetDistanceMode",
                 Description = "マグネットの距離計算方式。Euclidean / Link。既定は Euclidean")]
        public DistanceMode MagnetDistanceMode { get; }

        public RotateSelectionCommand(
            int modelIndex, int[] masterIndices,
            bool axisMode,
            Vector3 euler,
            Vector3 axis,
            float angle,
            bool useOriginPivot          = false,
            bool useMagnet               = false,
            float magnetRadius           = 0.5f,
            FalloffType magnetFalloff    = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds            = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            AxisMode           = axisMode;
            Euler              = euler;
            Axis               = axis;
            Angle              = angle;
            UseOriginPivot     = useOriginPivot;
            UseMagnet          = useMagnet;
            MagnetRadius       = magnetRadius;
            MagnetFalloff      = magnetFalloff;
            MagnetDistanceMode = magnetDistanceMode;
        }
    }

    /// <summary>
    /// 選択頂点をピボット中心に拡大縮小する。実処理は ScaleTool。
    ///
    /// 【UniformScale を載せない理由】
    ///   ScaleTool の UniformScale（ScaleTool.cs:57）は X の値を Y / Z へ写す
    ///   入力補助で、スケール計算は Vector3 の 3 成分しか読まない（ScaleTool.cs:228）。
    ///   このコマンドは 3 成分をそのまま載せるので、等倍かどうかは値で決まる。
    ///
    /// 【ScaleAxis】
    ///   拡大縮小を行うフレームの回転（度）。ScaleTool.cs:229 が
    ///   Quaternion.Euler で解釈し、R⁻¹ → スケール → R の順で適用する。
    /// </summary>
    [PLCommand(Description = "選択頂点をピボット中心に拡大縮小する。")]
    public class ScaleSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "ScaleFactors",
                 Description = "軸ごとの倍率。1 は等倍", Required = true)]
        public Vector3 Scale     { get; }

        [PLParam(TextKey = "ScaleAxisEuler",
                 Description = "拡大縮小を行うフレームの回転（度）。既定は 0,0,0")]
        public Vector3 ScaleAxis { get; }

        [PLParam(TextKey = "ScaleUseOriginPivot",
                 Description = "基準メッシュのローカル原点をピボットにする。既定は false（選択の重心）")]
        public bool    UseOriginPivot { get; }

        [PLParam(TextKey = "ScaleUseMagnet",
                 Description = "選択外の周辺頂点も減衰させて動かす。既定は false")]
        public bool         UseMagnet          { get; }

        [PLParam(TextKey = "ScaleMagnetRadius",
                 Description = "マグネットの影響半径。UseMagnet が false のときは使わない",
                 LimitKey = "Move.MagnetRadius")]
        public float        MagnetRadius       { get; }

        [PLParam(TextKey = "ScaleMagnetFalloff",
                 Description = "マグネットの減衰の形。既定は Smooth")]
        public FalloffType  MagnetFalloff      { get; }

        [PLParam(TextKey = "ScaleMagnetDistanceMode",
                 Description = "マグネットの距離計算方式。Euclidean / Link。既定は Euclidean")]
        public DistanceMode MagnetDistanceMode { get; }

        public ScaleSelectionCommand(
            int modelIndex, int[] masterIndices,
            Vector3 scale,
            Vector3 scaleAxis            = default,
            bool useOriginPivot          = false,
            bool useMagnet               = false,
            float magnetRadius           = 0.5f,
            FalloffType magnetFalloff    = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds            = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            Scale              = scale;
            ScaleAxis          = scaleAxis;
            UseOriginPivot     = useOriginPivot;
            UseMagnet          = useMagnet;
            MagnetRadius       = magnetRadius;
            MagnetFalloff      = magnetFalloff;
            MagnetDistanceMode = magnetDistanceMode;
        }
    }

    // ================================================================
    // オブジェクトごと移動・回転（ObjectMove ギズモ）
    //
    // どちらも実行時点の「選択中のオブジェクト」（ボーン ∪ 描画メッシュ）に効く
    // （ObjectMoveTool.AllSelectedIndices）。MasterIndices はその集合と一致すること。
    //
    // 原点だけ移動（OriginOnly）は MovePivotCommand が担当する。
    // 受け口はツールが OriginOnly 設定でないことを確かめる。
    // ================================================================

    /// <summary>
    /// 選択オブジェクト（ボーン / メッシュ）の原点を移動する。実処理は ObjectMoveTool。
    ///
    /// 【MoveWithChildren】
    ///   false のとき、直接の子はワールド位置を保つよう Position を補正する
    ///   （ObjectMoveTool.ApplyWorldDelta の子補正）。
    ///
    /// 【MoveMode】
    ///   BoneOnlyRebind は BindPose を更新してメッシュの見た目を固定する。
    ///   SkinBakeRebind は確定時に頂点を焼き込んで再バインドする。
    /// </summary>
    [PLCommand(Description = "選択オブジェクト（ボーン / メッシュ）の原点を移動する。")]
    public class MoveObjectsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象のオブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "ObjectMoveDelta",
                 Description = "原点の移動量", Required = true)]
        public Vector3 Delta { get; }

        /// <summary>
        /// Delta の座標空間。Local は MasterIndices[0] のローカル空間として解釈し、
        /// そのメッシュの WorldMatrix でワールドへ変換する。対象ごとに行列が違うため、
        /// 基準は先頭の 1 本に固定する（MovePivotCommand と同じ規則）。
        /// </summary>
        [PLParam(TextKey = "ObjectMoveCoordSpace",
                 Description = "Delta の座標空間。Local は MasterIndices[0] のローカル空間",
                 Required = true)]
        public MoveSelectedVerticesCommand.CoordSpace Space { get; }

        [PLParam(TextKey = "ObjectMoveWithChildren",
                 Description = "子を一緒に動かす。false なら直接の子のワールド位置を保つ。既定は true")]
        public bool         MoveWithChildren { get; }

        [PLParam(TextKey = "ObjectMoveMode",
                 Description = "ボーン移動の確定モード。BoneOnlyRebind / SkinBakeRebind / PoseLayer")]
        public BoneMoveMode MoveMode { get; }

        public MoveObjectsCommand(
            int modelIndex, int[] masterIndices,
            Vector3 delta,
            MoveSelectedVerticesCommand.CoordSpace space,
            bool moveWithChildren = true,
            BoneMoveMode moveMode = BoneMoveMode.BoneOnlyRebind,
            ulong[] objectIds     = null)
            : base(modelIndex)
        {
            MasterIndices    = masterIndices ?? System.Array.Empty<int>();
            ObjectIds        = objectIds;
            Delta            = delta;
            Space            = space;
            MoveWithChildren = moveWithChildren;
            MoveMode         = moveMode;
        }
    }

    /// <summary>
    /// 選択オブジェクト（ボーン / メッシュ）をピボット周りに回転させる。
    /// 実処理は ObjectMoveTool の回転リングと同じ経路。
    ///
    /// 【ピボット】
    ///   UseSelectionCentroid が true のとき Pivot を無視し、対象の原点の重心を使う
    ///   （ObjectMoveTool.UpdateGizmoCenter と同じ）。重心は MasterIndices だけで
    ///   決まるので、実行前の他の状態には依存しない。
    ///
    /// 【対象から外れるもの】
    ///   祖先チェーンに非一様スケールを持つ要素は除外される
    ///   （BoneTransform が TRS 分離保持のため、シアーを表現できない）。
    /// </summary>
    [PLCommand(Description = "選択オブジェクト（ボーン / メッシュ）をピボット周りに回転させる。")]
    public class RotateObjectsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象のオブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "ObjectRotatePivot",
                 Description = "回転の中心（ワールド座標）。UseSelectionCentroid が true なら無視される")]
        public Vector3 Pivot { get; }

        [PLParam(TextKey = "ObjectRotateUseCentroid",
                 Description = "Pivot の代わりに対象の原点の重心を使う。既定は false")]
        public bool    UseSelectionCentroid { get; }

        [PLParam(TextKey = "ObjectRotateAxis",
                 Description = "回転軸（ワールド）", Required = true)]
        public Vector3 Axis { get; }

        [PLParam(TextKey = "ObjectRotateAngle",
                 Description = "回転角（度）", Required = true,
                 Min = -360.0, Max = 360.0)]
        public float   Angle { get; }

        [PLParam(TextKey = "ObjectMoveWithChildren",
                 Description = "子を一緒に回す。false なら直接の子のワールド姿勢を保つ。既定は true")]
        public bool         MoveWithChildren { get; }

        [PLParam(TextKey = "ObjectMoveMode",
                 Description = "ボーン移動の確定モード。BoneOnlyRebind / SkinBakeRebind / PoseLayer")]
        public BoneMoveMode MoveMode { get; }

        public RotateObjectsCommand(
            int modelIndex, int[] masterIndices,
            Vector3 pivot, bool useSelectionCentroid,
            Vector3 axis, float angle,
            bool moveWithChildren = true,
            BoneMoveMode moveMode = BoneMoveMode.BoneOnlyRebind,
            ulong[] objectIds     = null)
            : base(modelIndex)
        {
            MasterIndices        = masterIndices ?? System.Array.Empty<int>();
            ObjectIds            = objectIds;
            Pivot                = pivot;
            UseSelectionCentroid = useSelectionCentroid;
            Axis                 = axis;
            Angle                = angle;
            MoveWithChildren     = moveWithChildren;
            MoveMode             = moveMode;
        }
    }
}
