// PanelCommand.Deform.cs
// 特殊な変形（シュリンカー・TPSモーフ・MediaPipe・デフォーマ・格子変形）の操作要求。
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
    // シュリンカー
    // ================================================================

    /// <summary>
    /// ビフォーオブジェクトの頂点をアフターオブジェクトへ向けて移動する。
    /// 衝突対象オブジェクト群と交差した頂点はその位置で停止する。
    /// バックアップ作成 + Undo 記録付き。
    /// </summary>
    [PLCommand(Description = "ビフォーオブジェクトの頂点をアフターオブジェクトへ向けて移動する。")]
    public class ApplyShrinkCommand : PanelCommand
    {
        /// <summary>ビフォー（変形対象）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ShrinkBeforeMasterIndex",
                 Description = "変形させる描画オブジェクトの masterIndex", Required = true)]
        public int   BeforeMasterIndex     { get; }

        /// <summary>アフター（目標形状）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ShrinkAfterMasterIndex",
                 Description = "目標形状の描画オブジェクトの masterIndex", Required = true)]
        public int   AfterMasterIndex      { get; }

        /// <summary>衝突対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "ShrinkColliderMasterIndices",
                 Description = "衝突対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] ColliderMasterIndices { get; }

        /// <summary>シュリンク量 [0, 1]</summary>
        [PLParam(TextKey = "ShrinkSlider",
                 Description = "ビフォーからアフターへの進行量",
                 LimitKey = "Shrink.Slider", Required = true)]
        public float Slider                { get; }

        /// <summary>コライダー面から手前に残す距離（ワールド単位）</summary>
        [PLParam(TextKey = "ShrinkSurfaceOffset",
                 Description = "コライダー面から手前に残す距離（ワールド単位）。既定は 0",
                 LimitKey = "Shrink.SurfaceOffset")]
        public float SurfaceOffset         { get; }
        /// <summary>
        /// true : 進行方向に対して表を向いた面のみを衝突とみなす（裏面は素通り）
        /// false: 表裏を問わず衝突とみなす（既定）
        /// </summary>
        [PLParam(TextKey = "ShrinkFrontFaceOnly",
                 Description = "進行方向に表を向いた面だけを衝突とみなす。既定は false")]
        public bool  FrontFaceOnly         { get; }

        /// <summary>適用後に法線を再計算するか</summary>
        [PLParam(TextKey = "ShrinkRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool  RecalculateNormals    { get; }
        /// <summary>
        /// true : 結果を新規オブジェクトとして追加し、ビフォー／アフターを非表示にする（既定）
        /// false: ビフォーを上書きし、元形状を &lt;名前&gt;_backup として追加する
        /// </summary>
        [PLParam(TextKey = "ShrinkCreateNewObject",
                 Description = "結果を新規オブジェクトとして追加する。false でビフォーを上書きする。既定は true")]
        public bool  CreateNewObject       { get; }
        /// <summary>
        /// 衝突判定の単位。
        /// VertexSegment … 頂点のビフォー→アフター線分とコライダー三角形の交差（既定）
        /// FacePair      … ビフォー面を三角形に割り、面どうしの接触時刻を求める
        /// </summary>
        [PLParam(TextKey = "ShrinkCollisionMode",
                 Description = "衝突判定の単位。VertexSegment / FacePair。既定は VertexSegment")]
        public Poly_Ling.UI.ShrinkCollisionMode CollisionMode { get; }
        /// <summary>
        /// 面方式の反復上限。頂点方式では使わない。
        /// 停止値は単調減少するので必ず収束するが、上限で打ち切ることもできる。
        /// </summary>
        [PLParam(TextKey = "ShrinkMaxPasses",
                 Description = "面方式の反復上限。頂点方式では使わない。既定は 8",
                 LimitKey = "Shrink.MaxPasses")]
        public int   MaxPasses             { get; }

        public ApplyShrinkCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex,
            int[] colliderMasterIndices,
            float slider,
            float surfaceOffset      = 0f,
            bool  frontFaceOnly      = false,
            bool  recalculateNormals = true,
            bool  createNewObject    = true,
            Poly_Ling.UI.ShrinkCollisionMode collisionMode = Poly_Ling.UI.ShrinkCollisionMode.VertexSegment,
            int   maxPasses          = 8)
            : base(modelIndex)
        {
            BeforeMasterIndex     = beforeMasterIndex;
            AfterMasterIndex      = afterMasterIndex;
            ColliderMasterIndices = colliderMasterIndices;
            Slider                = slider;
            SurfaceOffset         = surfaceOffset;
            FrontFaceOnly         = frontFaceOnly;
            RecalculateNormals    = recalculateNormals;
            CreateNewObject       = createNewObject;
            CollisionMode         = collisionMode;
            MaxPasses             = maxPasses;
        }
    }

    // ================================================================
    // TPSモーフ
    // ================================================================

    /// <summary>
    /// TPSモーフの制御点の選び方。
    ///
    /// Global 以外は「ターゲット頂点ごとに独立に係数を求める」局所モードで、
    /// 近傍の選び方が 4 通りある。局所モードは 1 頂点ごとに LU 分解を行うため、
    /// 制御点数 N に対して「ターゲット頂点数 × (N+4)^3 / 3」の積和が要る。
    /// 半径モード（EuclideanRadius / LinkRadius）は制御点数が入力依存で
    /// 上限が無いため、必ず制御点数の上限で頭打ちにすること。
    ///
    /// リンク距離モード（LinkCount / LinkRadius）は、制御点候補だけの
    /// 誘導部分グラフをたどる。選択が飛び地になっていると到達できず
    /// 制御点が減る。ビフォーに面が無い場合は使えない。
    /// </summary>
    public enum ThinPlateLocalMode
    {
        /// <summary>全域モード。全制御点で 1 度だけ係数を求める（従来動作）。</summary>
        Global          = 0,
        /// <summary>ターゲット頂点位置から直線距離で近い順に N 個。</summary>
        EuclideanCount  = 1,
        /// <summary>最も近い候補点を始点に、リンク距離で近い順に N 個。</summary>
        LinkCount       = 2,
        /// <summary>ターゲット頂点位置から直線距離 L 以下。</summary>
        EuclideanRadius = 3,
        /// <summary>最も近い候補点を始点に、リンク距離 L 以下。</summary>
        LinkRadius      = 4,
    }

    /// <summary>
    /// ビフォー／アフター2オブジェクトの頂点対応から 3D Thin Plate Spline を解き、
    /// ターゲットオブジェクトを変形した結果を新規オブジェクトとして追加する。
    /// ターゲット自身は変更しない。Undo 記録付き。
    ///
    /// ビフォーとアフターは頂点インデックスで対応させるため、頂点数が一致していること。
    ///
    /// スキニング無しを前提とする。空間変換はオブジェクト単位の
    /// MeshContext.WorldMatrix だけを使う。
    /// </summary>
    [PLCommand(Description = "ビフォー／アフター2オブジェクトの頂点対応から 3D Thin Plate Spline を解き、 ターゲットオブジェクトを変形した結果を新規オブジェクトとして追加する。")]
    public class ApplyThinPlateMorphCommand : PanelCommand
    {
        /// <summary>ビフォー（変形前の対応点）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateBeforeMasterIndex",
                 Description = "変形前の対応点を持つオブジェクトの masterIndex", Required = true)]
        public int   BeforeMasterIndex         { get; }

        /// <summary>アフター（変形後の対応点）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateAfterMasterIndex",
                 Description = "変形後の対応点を持つオブジェクトの masterIndex", Required = true)]
        public int   AfterMasterIndex          { get; }

        /// <summary>変形させる MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateSourceMasterIndex",
                 Description = "変形させる描画オブジェクトの masterIndex", Required = true)]
        public int   TargetMasterIndex         { get; }

        /// <summary>平滑化係数。K 行列の対角に加算される。0 で厳密補間。</summary>
        [PLParam(TextKey = "ThinPlateLambda",
                 Description = "平滑化係数。0 で厳密補間。既定は 0.001",
                 LimitKey = "ThinPlateMorph.Lambda")]
        public float Lambda                    { get; }
        /// <summary>
        /// true : ビフォー／アフターの選択頂点（両者の和集合）だけを制御点にする
        /// false: 全頂点を制御点にする（既定）
        /// </summary>
        [PLParam(TextKey = "ThinPlateSelectedControlPointsOnly",
                 Description = "ビフォー／アフターの選択頂点だけを制御点にする。既定は false")]
        public bool  SelectedControlPointsOnly { get; }

        /// <summary>結果の法線を再計算するか</summary>
        [PLParam(TextKey = "ThinPlateMorphRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool  RecalculateNormals        { get; }

        public ApplyThinPlateMorphCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex, int targetMasterIndex,
            float lambda                        = 0.001f,
            bool  selectedControlPointsOnly     = false,
            bool  recalculateNormals            = true)
            : base(modelIndex)
        {
            BeforeMasterIndex         = beforeMasterIndex;
            AfterMasterIndex          = afterMasterIndex;
            TargetMasterIndex         = targetMasterIndex;
            Lambda                    = lambda;
            SelectedControlPointsOnly = selectedControlPointsOnly;
            RecalculateNormals        = recalculateNormals;
        }
    }

    /// <summary>
    /// 算出済みの変形結果を新規オブジェクトとして追加する。
    /// ターゲット自身は変更しない。Undo 記録付き。
    ///
    /// 局所モードの TPS は 1 頂点ごとに LU 分解を行うため実行時間が長く、
    /// 中止できるようバックグラウンドスレッドで走らせる。計算が終わった後に
    /// メインスレッドへ戻すのがこのコマンドで、変形そのものは行わない。
    /// 全域モードは ApplyThinPlateMorphCommand が同期実行するため、
    /// このコマンドを経由しない。
    /// </summary>
    [PLCommand(Description = "算出済みの変形結果を新規オブジェクトとして追加する。")]
    public class ApplyThinPlateMorphResultCommand : PanelCommand
    {
        /// <summary>変形させた MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateTargetMasterIndex",
                 Description = "変形結果を書き込む描画オブジェクトの masterIndex", Required = true)]
        public int       TargetMasterIndex  { get; }

        /// <summary>ターゲットのローカル座標での変形後位置。ターゲットの頂点数と同数であること。</summary>
        [PLParam(TextKey = "ThinPlateLocalPositions",
                 Description = "変形後の頂点位置（ターゲットのローカル座標）。頂点数と同数", Required = true)]
        public Vector3[] LocalPositions     { get; }

        /// <summary>結果の法線を再計算するか</summary>
        [PLParam(TextKey = "ThinPlateRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool      RecalculateNormals { get; }

        public ApplyThinPlateMorphResultCommand(
            int modelIndex, int targetMasterIndex,
            Vector3[] localPositions, bool recalculateNormals = true)
            : base(modelIndex)
        {
            TargetMasterIndex  = targetMasterIndex;
            LocalPositions     = localPositions;
            RecalculateNormals = recalculateNormals;
        }
    }

    // ================================================================
    // MediaPipe フェイス変形
    // ================================================================

    /// <summary>MediaPipe ランドマークJSONを使ってカレントメッシュを変形した新メッシュを追加する</summary>
    [PLCommand(Description = "MediaPipe ランドマークJSONを使ってカレントメッシュを変形した新メッシュを追加する</summary>")]
    public class MediaPipeFaceDeformCommand : PanelCommand
    {
        [PLParam(TextKey = "MediaPipeSourceMaster",
                 Description = "変形元の描画オブジェクトの masterIndex", Required = true)]
        public int    SourceMasterIndex { get; }

        [PLParam(TextKey = "MediaPipeBeforePath",
                 Description = "変形前ランドマークの JSON。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string BeforePath        { get; }

        [PLParam(TextKey = "MediaPipeAfterPath",
                 Description = "変形後ランドマークの JSON。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string AfterPath         { get; }

        [PLParam(TextKey = "MediaPipeTrianglesPath",
                 Description = "三角形定義の JSON。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string TrianglesPath     { get; }

        public MediaPipeFaceDeformCommand(int modelIndex, int sourceMasterIndex,
            string beforePath, string afterPath, string trianglesPath)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            BeforePath        = beforePath;
            AfterPath         = afterPath;
            TrianglesPath     = trianglesPath;
        }
    }

    // ================================================================
    // デフォーマ（作業軸を基準にした頂点変形）
    //
    // 【対象】
    //   DeformApplier.Begin が model.SelectedDrawableMeshIndices のうち選択を
    //   持つメッシュを走査する（DeformApplier.cs:87-91）。MasterIndices は
    //   実行時点の選択と一致すること。
    //
    // 【作業軸が前提】
    //   変形はすべて WorkAxisContext のローカル空間で定義される
    //   （+Y がライン方向、原点が WorkAxisContext.Origin）。
    //   作業軸はこのコマンドには載せない。先に SetWorkAxisCommand か
    //   RecallWorkAxisCommand で決めておくこと。
    //
    // 【デフォーマごとに別コマンドにする理由】
    //   IDeformerParams をそのまま載せると PanelCommandFactory.TryParse の
    //   対応型に無く、スキーマに出せない。パラメータは種類ごとに違うので、
    //   平坦な float / bool を持つ派生を種類ぶん用意する
    //   （CreatePrimitiveMeshCommand の派生と同じ形）。
    // ================================================================

    /// <summary>
    /// 作業軸を基準に選択頂点を変形する。実処理は DeformApplier と IMeshDeformer。
    /// 種類ごとの派生がパラメータを持つ。
    /// </summary>
    public abstract class ApplyDeformCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "DeformUseMagnet",
                 Description = "選択外の周辺頂点も減衰させて変形する。既定は false")]
        public bool         UseMagnet          { get; }

        [PLParam(TextKey = "DeformMagnetRadius",
                 Description = "マグネットの影響半径。UseMagnet が false のときは使わない",
                 LimitKey = "Move.MagnetRadius")]
        public float        MagnetRadius       { get; }

        [PLParam(TextKey = "DeformMagnetFalloff",
                 Description = "マグネットの減衰の形。既定は Smooth")]
        public FalloffType  MagnetFalloff      { get; }

        [PLParam(TextKey = "DeformMagnetDistanceMode",
                 Description = "マグネットの距離計算方式。Euclidean / Link。既定は Euclidean")]
        public DistanceMode MagnetDistanceMode { get; }

        /// <summary>デフォーマの識別子。DeformerRegistry の検索キーと同じ文字列。</summary>
        [PLParam(Ignore = true)]
        public abstract string DeformerName { get; }

        protected ApplyDeformCommand(
            int modelIndex, int[] masterIndices,
            bool useMagnet, float magnetRadius,
            FalloffType magnetFalloff, DistanceMode magnetDistanceMode,
            ulong[] objectIds)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            UseMagnet          = useMagnet;
            MagnetRadius       = magnetRadius;
            MagnetFalloff      = magnetFalloff;
            MagnetDistanceMode = magnetDistanceMode;
        }
    }

    /// <summary>作業軸まわりに一様回転させる。RotateDeformer。</summary>
    [PLCommand(Description = "作業軸まわりに一様回転させる。RotateDeformer。")]
    public sealed class ApplyRotateDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Rotate";

        [PLParam(TextKey = "DeformRotateAngleX",
                 Description = "作業軸ローカル X まわりの回転角（度）", Min = -360.0, Max = 360.0)]
        public float AngleX { get; }

        [PLParam(TextKey = "DeformRotateAngleY",
                 Description = "作業軸ローカル Y まわりの回転角（度）", Min = -360.0, Max = 360.0)]
        public float AngleY { get; }

        [PLParam(TextKey = "DeformRotateAngleZ",
                 Description = "作業軸ローカル Z まわりの回転角（度）", Min = -360.0, Max = 360.0)]
        public float AngleZ { get; }

        public ApplyRotateDeformCommand(
            int modelIndex, int[] masterIndices,
            float angleX, float angleY, float angleZ,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            AngleX = angleX;
            AngleY = angleY;
            AngleZ = angleZ;
        }
    }

    /// <summary>作業軸ローカルで平行移動させる。MoveDeformer。</summary>
    [PLCommand(Description = "作業軸ローカルで平行移動させる。MoveDeformer。")]
    public sealed class ApplyMoveDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Move";

        [PLParam(TextKey = "DeformMoveOffsetX", Description = "作業軸ローカル X 方向の移動量")]
        public float OffsetX { get; }

        [PLParam(TextKey = "DeformMoveOffsetY", Description = "作業軸ローカル Y 方向の移動量")]
        public float OffsetY { get; }

        [PLParam(TextKey = "DeformMoveOffsetZ", Description = "作業軸ローカル Z 方向の移動量")]
        public float OffsetZ { get; }

        public ApplyMoveDeformCommand(
            int modelIndex, int[] masterIndices,
            float offsetX, float offsetY, float offsetZ,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
        }
    }

    /// <summary>作業軸ローカルで拡大縮小させる。ScaleDeformer。</summary>
    [PLCommand(Description = "作業軸ローカルで拡大縮小させる。ScaleDeformer。")]
    public sealed class ApplyScaleDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Scale";

        [PLParam(TextKey = "DeformScaleX",
                 Description = "作業軸ローカル X 方向の倍率。下限 0.01 でクランプされる", Min = 0.01)]
        public float ScaleX { get; }

        [PLParam(TextKey = "DeformScaleY",
                 Description = "作業軸ローカル Y 方向の倍率。下限 0.01 でクランプされる", Min = 0.01)]
        public float ScaleY { get; }

        [PLParam(TextKey = "DeformScaleZ",
                 Description = "作業軸ローカル Z 方向の倍率。下限 0.01 でクランプされる", Min = 0.01)]
        public float ScaleZ { get; }

        public ApplyScaleDeformCommand(
            int modelIndex, int[] masterIndices,
            float scaleX, float scaleY, float scaleZ,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
        }
    }

    /// <summary>
    /// 作業軸ラインに沿って曲げる。BendDeformer。
    ///
    /// 【UseCameraBendPlane を載せない理由】
    ///   true のとき DeformToolHandler.SyncCameraBendPlane がカメラ向きから
    ///   BendPlaneAngleDeg を上書きするため、同じコマンドでも視点が違えば
    ///   結果が変わる。ここには解決済みの角度だけを載せ、受け口は
    ///   UseCameraBendPlane を false にして実行する。
    /// </summary>
    [PLCommand(Description = "作業軸ラインに沿って曲げる。BendDeformer。")]
    public sealed class ApplyBendDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Bend";

        [PLParam(TextKey = "DeformBendTotalAngle",
                 Description = "選択範囲の全長で曲げる合計角（度）", Required = true,
                 Min = -360.0, Max = 360.0)]
        public float TotalAngleDeg { get; }

        [PLParam(TextKey = "DeformBendPlaneAngle",
                 Description = "たわみ方向。作業軸ローカル +X を 0 度として Y まわりに回した角（度）",
                 Min = -360.0, Max = 360.0)]
        public float BendPlaneAngleDeg { get; }

        [PLParam(TextKey = "DeformPivotAtAxisOrigin",
                 Description = "作業軸の原点を曲げの起点にする。false なら選択範囲の下端。既定は false")]
        public bool  PivotAtAxisOrigin { get; }

        public ApplyBendDeformCommand(
            int modelIndex, int[] masterIndices,
            float totalAngleDeg,
            float bendPlaneAngleDeg         = 0f,
            bool pivotAtAxisOrigin          = false,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            TotalAngleDeg     = totalAngleDeg;
            BendPlaneAngleDeg = bendPlaneAngleDeg;
            PivotAtAxisOrigin = pivotAtAxisOrigin;
        }
    }

    /// <summary>作業軸ラインまわりにねじる。TwistDeformer。</summary>
    [PLCommand(Description = "作業軸ラインまわりにねじる。TwistDeformer。")]
    public sealed class ApplyTwistDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Twist";

        [PLParam(TextKey = "DeformTwistTotalAngle",
                 Description = "選択範囲の全長でねじる合計角（度）", Required = true,
                 Min = -360.0, Max = 360.0)]
        public float TotalAngleDeg { get; }

        [PLParam(TextKey = "DeformPivotAtAxisOrigin",
                 Description = "作業軸の原点をねじりの起点にする。false なら選択範囲の下端。既定は false")]
        public bool  PivotAtAxisOrigin { get; }

        public ApplyTwistDeformCommand(
            int modelIndex, int[] masterIndices,
            float totalAngleDeg,
            bool pivotAtAxisOrigin          = false,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            TotalAngleDeg     = totalAngleDeg;
            PivotAtAxisOrigin = pivotAtAxisOrigin;
        }
    }

    /// <summary>作業軸ラインに沿って波打たせる。WaveDeformer。</summary>
    [PLCommand(Description = "作業軸ラインに沿って波打たせる。WaveDeformer。")]
    public sealed class ApplyWaveDeformCommand : ApplyDeformCommand
    {
        public override string DeformerName => "Wave";

        [PLParam(TextKey = "DeformWaveAmplitudeX", Description = "+X 方向の振幅。作業軸ローカルの長さ")]
        public float AmplitudeX { get; }

        [PLParam(TextKey = "DeformWaveCyclesX", Description = "+X 方向の周期数。選択範囲の全長で何周ぶん波打つか")]
        public float CyclesX { get; }

        [PLParam(TextKey = "DeformWavePhaseX", Description = "+X 方向の位相（度）", Min = -360.0, Max = 360.0)]
        public float PhaseXDeg { get; }

        [PLParam(TextKey = "DeformWaveAmplitudeZ", Description = "+Z 方向の振幅。0 なら Z へは振らない")]
        public float AmplitudeZ { get; }

        [PLParam(TextKey = "DeformWaveCyclesZ", Description = "+Z 方向の周期数")]
        public float CyclesZ { get; }

        [PLParam(TextKey = "DeformWavePhaseZ", Description = "+Z 方向の位相（度）", Min = -360.0, Max = 360.0)]
        public float PhaseZDeg { get; }

        [PLParam(TextKey = "DeformPivotAtAxisOrigin",
                 Description = "作業軸の原点を波の起点にする。false なら選択範囲の下端。既定は false")]
        public bool  PivotAtAxisOrigin { get; }

        public ApplyWaveDeformCommand(
            int modelIndex, int[] masterIndices,
            float amplitudeX, float cyclesX, float phaseXDeg,
            float amplitudeZ                = 0f,
            float cyclesZ                   = 1f,
            float phaseZDeg                 = 0f,
            bool pivotAtAxisOrigin          = false,
            bool useMagnet                  = false,
            float magnetRadius              = 0.5f,
            FalloffType magnetFalloff       = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds               = null)
            : base(modelIndex, masterIndices, useMagnet, magnetRadius,
                   magnetFalloff, magnetDistanceMode, objectIds)
        {
            AmplitudeX        = amplitudeX;
            CyclesX           = cyclesX;
            PhaseXDeg         = phaseXDeg;
            AmplitudeZ        = amplitudeZ;
            CyclesZ           = cyclesZ;
            PhaseZDeg         = phaseZDeg;
            PivotAtAxisOrigin = pivotAtAxisOrigin;
        }
    }

    // ================================================================
    // 格子変形
    // ================================================================

    /// <summary>
    /// 作業軸を格子フレームとして選択頂点を格子変形する。
    /// 実処理は LatticeDeformer と DeformApplier。
    ///
    /// 【対象】
    ///   DeformApplier.Begin が model.SelectedDrawableMeshIndices のうち選択を
    ///   持つメッシュを走査する。MasterIndices は実行時点の選択と一致すること。
    ///
    /// 【作業軸が前提】
    ///   Center / Size / 制御点はすべて作業軸ローカル座標。作業軸は載せていないので、
    ///   先に SetWorkAxisCommand か RecallWorkAxisCommand で決めておくこと。
    ///
    /// 【基準格子はセル数と範囲から決まる】
    ///   LatticeGrid.Rebuild が CellsX/Y/Z と Center / Size から基準制御点を
    ///   等間隔で作り直す。よって変形量は「基準からのずれ」だけで表せる。
    ///
    /// 【制御点を疎に持つ理由】
    ///   セル数の上限は 32 なので制御点は最大 33×33×33 = 35937 個ある。
    ///   全点を載せると 10 万個の実数になり、リモートの文字列経路に載らない。
    ///   実際に動かす点はふつう数点なので、動かした点だけを平行配列で渡す。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・PointOffsets.Length == PointIndices.Length * 3
    ///   ・PointIndices の各要素が 0 〜 制御点数-1 の範囲内
    /// </summary>
    [PLCommand(Description = "作業軸を格子フレームとして選択頂点を格子変形する。")]
    public class ApplyLatticeDeformCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択と集合として一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "LatticeCellsX",
                 Description = "X 方向のセル数。1〜32 でクランプされる", Min = 1, Max = 32)]
        public int CellsX { get; }

        [PLParam(TextKey = "LatticeCellsY",
                 Description = "Y 方向のセル数。1〜32 でクランプされる", Min = 1, Max = 32)]
        public int CellsY { get; }

        [PLParam(TextKey = "LatticeCellsZ",
                 Description = "Z 方向のセル数。1〜32 でクランプされる", Min = 1, Max = 32)]
        public int CellsZ { get; }

        [PLParam(TextKey = "LatticeCenter",
                 Description = "基準格子の中心（作業軸ローカル）", Required = true)]
        public Vector3 Center { get; }

        [PLParam(TextKey = "LatticeSize",
                 Description = "基準格子の大きさ（作業軸ローカル）。薄すぎる軸は格子側で広げられる",
                 Required = true)]
        public Vector3 Size { get; }

        /// <summary>
        /// 動かした制御点の索引。0 が (ix,iy,iz) = (0,0,0) で、
        /// index = ix + iy * (CellsX+1) + iz * (CellsX+1) * (CellsY+1)。
        /// </summary>
        [PLParam(TextKey = "LatticePointIndices",
                 Description = "動かした制御点の索引。index = ix + iy*(CellsX+1) + iz*(CellsX+1)*(CellsY+1)",
                 Required = true)]
        public int[]   PointIndices { get; }

        [PLParam(TextKey = "LatticePointOffsets",
                 Description = "PointIndices と同じ並びの基準位置からのずれ。x,y,z の順に 3 個ずつ並べる",
                 Required = true)]
        public float[] PointOffsets { get; }

        public ApplyLatticeDeformCommand(
            int modelIndex, int[] masterIndices,
            int cellsX, int cellsY, int cellsZ,
            Vector3 center, Vector3 size,
            int[] pointIndices, float[] pointOffsets,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            CellsX        = cellsX;
            CellsY        = cellsY;
            CellsZ        = cellsZ;
            Center        = center;
            Size          = size;
            PointIndices  = pointIndices ?? System.Array.Empty<int>();
            PointOffsets  = pointOffsets ?? System.Array.Empty<float>();
        }
    }
}
