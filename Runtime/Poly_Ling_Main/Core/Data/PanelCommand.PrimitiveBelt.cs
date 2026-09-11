// PanelCommand.PrimitiveBelt.cs
// 図形生成（基準ベルトを使う図形・単一メッシュを返さない生成）の操作要求。
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
    // ── 基準ベルトを使う図形 ─────────────────────────────────────

    /// <summary>
    /// 基準ベルト（梯子状データ）を入力に取る図形の共通部分。
    ///
    /// ベルトは取り込み元メッシュのローカル座標を持つ点列で、
    /// パネルでは選択メッシュから拾う。コマンドにはその結果を載せる。
    /// 向き補正とスプライン分割は生成側で掛けるので、拾ったままの値を渡す。
    /// </summary>
    public abstract class CreateBeltPrimitiveCommand : CreatePrimitiveMeshCommand
    {
        /// <summary>
        /// 基準ベルトを平坦な列で持つ。1 本が梯子 1 本にあたる。
        ///
        /// BeltCsvEntry[] のまま持つとスキーマに出せない
        /// （要素が可変長の点列を 2 本持つクラスの配列）ため、
        /// 全ベルトの点列を連結し、ベルトごとの開始位置を別の配列で持つ。
        /// SkinWeightPaintCommand.StepStarts と同じ形。
        ///
        /// BeltCsvEntry の残りのフィールド（StartPoint / EndPoint / GroupId /
        /// RowIndex / RowCount）は CSV 読み込み時の付帯情報と自動検索の結果で、
        /// 外から指定するものではないので載せない。既定値のまま残る。
        /// </summary>
        [PLParam(TextKey = "BeltLeftPoints",
                 Description = "全ベルトの左点列を連結したもの。x,y,z を 3 個ずつ並べる",
                 Required = true)]
        public float[] BeltLeftPoints { get; }

        [PLParam(TextKey = "BeltRightPoints",
                 Description = "全ベルトの右点列。BeltLeftPoints と同じ点数にすること",
                 Required = true)]
        public float[] BeltRightPoints { get; }

        [PLParam(TextKey = "BeltStarts",
                 Description = "ベルト i が何点目から始まるか。単調増加。長さがベルト本数",
                 Required = true)]
        public int[] BeltStarts { get; }

        [PLParam(TextKey = "BeltClosed",
                 Description = "ベルトごとに閉じているか。BeltStarts と同じ長さ")]
        public bool[] BeltClosed { get; }

        [PLParam(TextKey = "BeltFlipWinding",
                 Description = "ベルトごとに巻き順を反転するか。BeltStarts と同じ長さ")]
        public bool[] BeltFlipWinding { get; }

        [PLParam(TextKey = "BeltHeightScale",
                 Description = "ベルトごとの高さ倍率。BeltStarts と同じ長さ。フリル以外では使われない")]
        public float[] BeltHeightScale { get; }

        /// <summary>
        /// 梯子の取り込み元オブジェクトの索引。-1 = ひも付けなし。
        /// </summary>
        [PLParam(TextKey = "BeltSourceIndex",
                 Description = "梯子の取り込み元オブジェクトの索引。-1 でひも付けなし",
                 IsMeshRef = true)]
        public int BeltSourceIndex { get; }

        /// <summary>
        /// 上の点列をどうやって取り込んだか。
        ///
        /// 【なぜ点列ではなく取り方を持つか】
        ///   ObjectGroup が作り直すとき、控えた点列をそのまま使うとソースを
        ///   直しても出力先は古いままになる。かといって頂点ID や頂点番号を
        ///   控えると、手作業で作る人がそれを管理する羽目になる。
        ///   取り方（自動検索の種類・上下展開の有無・選択辞書の名前）だけを
        ///   控えておけば、作り直しのたびにソースへ取り込みを掛け直せる。
        ///   人が管理するのはメッシュ側の目印（開始タグ三角形）か、
        ///   面を入れた選択辞書だけで済む。
        ///
        /// 生成そのものには使わない（点列はもう載っている）。作り直しでだけ読む。
        /// </summary>
        [PLParam(TextKey = "BeltAcquireMethod",
                 Description = "梯子の取り込み方。作り直しのときに同じ手順を掛け直す")]
        public Poly_Ling.PrimitiveMesh.BeltAcquireMethod AcquireMethod { get; }

        /// <summary>取り込み時に上下（左右レール側）へ横断して段グループにまとめたか。</summary>
        [PLParam(TextKey = "BeltAcquireCrossRows",
                 Description = "取り込み時に上下へ横断して段グループにまとめたか")]
        public bool AcquireCrossRows { get; }

        /// <summary>
        /// SelectionSet のときの、取り込み元オブジェクトが持つパーツ選択辞書の名前。
        /// 手で面を選んで取り込んだ場合、選択そのものは残らないので、
        /// 辞書に入れておかないと作り直しで同じ面を選び直せない。
        /// </summary>
        [PLParam(TextKey = "BeltAcquireSetName",
                 Description = "選択辞書から取り込むときの辞書名")]
        public string AcquireSetName { get; }

        /// <summary>平坦な列から起こしたベルト。受け口はこちらを使う。</summary>
        public Poly_Ling.PrimitiveMesh.BeltCsvEntry[] Belts
        {
            get
            {
                int n = BeltStarts?.Length ?? 0;
                if (n == 0) return System.Array.Empty<Poly_Ling.PrimitiveMesh.BeltCsvEntry>();

                int totalPoints = (BeltLeftPoints?.Length ?? 0) / 3;
                var result = new Poly_Ling.PrimitiveMesh.BeltCsvEntry[n];
                for (int i = 0; i < n; i++)
                {
                    int from = BeltStarts[i];
                    int to   = (i + 1 < n) ? BeltStarts[i + 1] : totalPoints;
                    if (from < 0) from = 0;
                    if (to > totalPoints) to = totalPoints;

                    var e = new Poly_Ling.PrimitiveMesh.BeltCsvEntry();
                    for (int k = from; k < to; k++)
                    {
                        e.Left.Add(new Vector3(
                            BeltLeftPoints[k * 3], BeltLeftPoints[k * 3 + 1], BeltLeftPoints[k * 3 + 2]));
                        if (BeltRightPoints != null && (k * 3 + 2) < BeltRightPoints.Length)
                            e.Right.Add(new Vector3(
                                BeltRightPoints[k * 3], BeltRightPoints[k * 3 + 1], BeltRightPoints[k * 3 + 2]));
                    }
                    e.Closed      = BeltClosed      != null && i < BeltClosed.Length      && BeltClosed[i];
                    e.FlipWinding = BeltFlipWinding != null && i < BeltFlipWinding.Length && BeltFlipWinding[i];
                    e.HeightScale = (BeltHeightScale != null && i < BeltHeightScale.Length)
                        ? BeltHeightScale[i] : 1f;
                    result[i] = e;
                }
                return result;
            }
        }

        /// <summary>
        /// BeltCsvEntry[] を平坦な列へ分ける。呼び出し側の書き換えを短くするための補助。
        /// </summary>
        public static void SplitBelts(
            Poly_Ling.PrimitiveMesh.BeltCsvEntry[] belts,
            out float[] leftPoints, out float[] rightPoints, out int[] starts,
            out bool[] closed, out bool[] flipWinding, out float[] heightScale)
        {
            int n = belts?.Length ?? 0;
            starts      = new int[n];
            closed      = new bool[n];
            flipWinding = new bool[n];
            heightScale = new float[n];

            var left  = new System.Collections.Generic.List<float>();
            var right = new System.Collections.Generic.List<float>();

            int cursor = 0;
            for (int i = 0; i < n; i++)
            {
                var e = belts[i];
                starts[i]      = cursor;
                closed[i]      = e?.Closed      ?? false;
                flipWinding[i] = e?.FlipWinding ?? false;
                heightScale[i] = e?.HeightScale ?? 1f;

                int count = e?.Left?.Count ?? 0;
                for (int k = 0; k < count; k++)
                {
                    var l = e.Left[k];
                    left.Add(l.x); left.Add(l.y); left.Add(l.z);

                    var r = (e.Right != null && k < e.Right.Count) ? e.Right[k] : Vector3.zero;
                    right.Add(r.x); right.Add(r.y); right.Add(r.z);
                }
                cursor += count;
            }

            leftPoints  = left.ToArray();
            rightPoints = right.ToArray();
        }

        /// <summary>向き補正。</summary>
        [PLParam(TextKey = "BeltOrient", Description = "梯子の向き補正")]
        public Poly_Ling.PrimitiveMesh.BeltOrientOptions Orient { get; }

        /// <summary>スプライン分割。</summary>
        [PLParam(TextKey = "BeltSpline", Description = "梯子のスプライン分割")]
        public Poly_Ling.PrimitiveMesh.BeltSplineOptions Spline { get; }

        /// <summary>
        /// beltSourceIndex / acquireMethod / acquireCrossRows / acquireSetName は
        /// 後から足した引数なので末尾に既定値付きで置く。従来の呼び出しはそのまま通り、
        /// 「取り込み方の記録なし」＝作り直しでは控えた点列をそのまま使う扱いになる。
        /// </summary>
        protected CreateBeltPrimitiveCommand(
            int modelIndex, PrimitivePlacement placement,
            float[] beltLeftPoints, float[] beltRightPoints, int[] beltStarts,
            bool[] beltClosed, bool[] beltFlipWinding, float[] beltHeightScale,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            int beltSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.BeltAcquireMethod acquireMethod
                = Poly_Ling.PrimitiveMesh.BeltAcquireMethod.Baked,
            bool acquireCrossRows = false,
            string acquireSetName = "",
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement, profileSourceIndex, profileAcquire)
        {
            BeltLeftPoints  = beltLeftPoints  ?? System.Array.Empty<float>();
            BeltRightPoints = beltRightPoints ?? System.Array.Empty<float>();
            BeltStarts      = beltStarts      ?? System.Array.Empty<int>();
            BeltClosed      = beltClosed      ?? System.Array.Empty<bool>();
            BeltFlipWinding = beltFlipWinding ?? System.Array.Empty<bool>();
            BeltHeightScale = beltHeightScale ?? System.Array.Empty<float>();
            BeltSourceIndex  = beltSourceIndex;
            AcquireMethod    = acquireMethod;
            AcquireCrossRows = acquireCrossRows;
            AcquireSetName   = acquireSetName ?? "";
            Orient = orient;
            Spline = spline;
        }
    }

    /// <summary>
    /// フリル。断面プロファイルは A / B の2本まで持てる。
    /// TwoProfiles が false のときは A だけを使う。
    /// </summary>
    [PLCommand(Description = "フリル。断面プロファイルは A / B の2本まで持てる。")]
    public sealed class CreateFrillCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "Frill", Description = "フリルのパラメータ", Required = true)]
        public Poly_Ling.Frill.FrillParams Params { get; }

        /// <summary>断面プロファイル A。</summary>
        [PLParam(TextKey = "FrillProfileA", Description = "断面プロファイル A", Required = true,
                 ProfileRole = PLProfileRole.Points, ProfileNormalize = true)]
        public Vector2[] ProfileA { get; }

        /// <summary>断面プロファイル B。TwoProfiles が false なら使わない。</summary>
        [PLParam(TextKey = "FrillProfileB", Description = "断面プロファイル B")]
        public Vector2[] ProfileB { get; }

        public override string ShapeName => "Frill";
        public override string MeshName  => Params.MeshName;

        public CreateFrillCommand(
            int modelIndex,
            Poly_Ling.Frill.FrillParams @params,
            Vector2[] profileA, Vector2[] profileB,
            float[] beltLeftPoints, float[] beltRightPoints, int[] beltStarts,
            bool[] beltClosed, bool[] beltFlipWinding, float[] beltHeightScale,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement,
            int beltSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.BeltAcquireMethod acquireMethod
                = Poly_Ling.PrimitiveMesh.BeltAcquireMethod.Baked,
            bool acquireCrossRows = false,
            string acquireSetName = "",
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement,
                   beltLeftPoints, beltRightPoints, beltStarts,
                   beltClosed, beltFlipWinding, beltHeightScale, orient, spline,
                   beltSourceIndex, acquireMethod, acquireCrossRows, acquireSetName,
                   profileSourceIndex, profileAcquire)
        {
            Params   = @params;
            ProfileA = profileA;
            ProfileB = profileB;
        }
    }

    /// <summary>パイプ。断面プロファイルは1本で、閉ループかどうかを別に持つ。</summary>
    [PLCommand(Description = "パイプ。断面プロファイルは1本で、閉ループかどうかを別に持つ。")]
    public sealed class CreatePipeCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "Pipe", Description = "パイプのパラメータ", Required = true)]
        public Poly_Ling.Pipe.PipeParams Params { get; }

        [PLParam(TextKey = "PipeProfile", Description = "断面プロファイル", Required = true,
                 ProfileRole = PLProfileRole.Points, ProfileNormalize = true)]
        public Vector2[] Profile { get; }

        [PLParam(TextKey = "PipeProfileClosed", Description = "断面を閉ループとして扱う")]
        public bool ProfileClosed { get; }

        public override string ShapeName => "Pipe";
        public override string MeshName  => Params.MeshName;

        public CreatePipeCommand(
            int modelIndex,
            Poly_Ling.Pipe.PipeParams @params,
            Vector2[] profile, bool profileClosed,
            float[] beltLeftPoints, float[] beltRightPoints, int[] beltStarts,
            bool[] beltClosed, bool[] beltFlipWinding, float[] beltHeightScale,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement,
            int beltSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.BeltAcquireMethod acquireMethod
                = Poly_Ling.PrimitiveMesh.BeltAcquireMethod.Baked,
            bool acquireCrossRows = false,
            string acquireSetName = "",
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement,
                   beltLeftPoints, beltRightPoints, beltStarts,
                   beltClosed, beltFlipWinding, beltHeightScale, orient, spline,
                   beltSourceIndex, acquireMethod, acquireCrossRows, acquireSetName,
                   profileSourceIndex, profileAcquire)
        {
            Params        = @params;
            Profile       = profile;
            ProfileClosed = profileClosed;
        }
    }

    /// <summary>
    /// 藤壺（配置）。配置元はモデル内の描画オブジェクトなので索引で指す。
    /// 索引から MeshObject への解決はディスパッチャ側が行う。
    /// </summary>
    [PLCommand(Description = "藤壺（配置）。配置元はモデル内の描画オブジェクトなので索引で指す。")]
    public sealed class CreatePlaceObjectCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "PlaceObject", Description = "配置のパラメータ", Required = true)]
        public Poly_Ling.PlaceObject.PlaceObjectParams Params { get; }

        /// <summary>配置元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "PlaceSourceIndices", Description = "配置元オブジェクトの索引", Required = true,
                 IsMeshRef = true)]
        public int[] SourceMasterIndices { get; }

        public override string ShapeName => "PlaceObject";
        public override string MeshName  => Params.MeshName;

        public CreatePlaceObjectCommand(
            int modelIndex,
            Poly_Ling.PlaceObject.PlaceObjectParams @params,
            int[] sourceMasterIndices,
            float[] beltLeftPoints, float[] beltRightPoints, int[] beltStarts,
            bool[] beltClosed, bool[] beltFlipWinding, float[] beltHeightScale,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement,
            int beltSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.BeltAcquireMethod acquireMethod
                = Poly_Ling.PrimitiveMesh.BeltAcquireMethod.Baked,
            bool acquireCrossRows = false,
            string acquireSetName = "",
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement,
                   beltLeftPoints, beltRightPoints, beltStarts,
                   beltClosed, beltFlipWinding, beltHeightScale, orient, spline,
                   beltSourceIndex, acquireMethod, acquireCrossRows, acquireSetName,
                   profileSourceIndex, profileAcquire)
        {
            Params              = @params;
            SourceMasterIndices = sourceMasterIndices;
        }
    }

    /// <summary>
    /// 出来上がった MeshObject をそのままモデルへ置く。
    ///
    /// 【なぜ図形パラメータではなくメッシュを載せるか】
    ///   プロファイル編集の「メッシュへ反映」や厚み付け（ソリッド化）の結果は、
    ///   図形パラメータから決まるのではなく、編集中の点列や選択面から出来る。
    ///   パラメータ化できないので、そのままメッシュを渡す。
    ///
    /// 【MCP には出さない】
    ///   Mesh はスキーマにできないため Ignore を付けてある。
    ///   MCP からの生成には図形ごとの CreatePrimitiveMeshCommand を使う。
    /// </summary>
    [PLCommand(Description = "出来上がったメッシュをそのままモデルへ置く。内部用で、外からは使えない。")]
    public class AddGeneratedMeshCommand : PanelCommand
    {
        /// <summary>置くメッシュ。呼出し側が作った実体をそのまま渡す。</summary>
        [PLParam(Ignore = true, Description = "出来上がったメッシュ。スキーマには出さない")]
        public MeshObject Mesh { get; }

        /// <summary>描画オブジェクトの名前。</summary>
        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName { get; }

        /// <summary>配置と後処理の指定。</summary>
        [PLParam(TextKey = "PrimitivePlacement", Description = "配置と後処理の指定", Required = true)]
        public PrimitivePlacement Placement { get; }

        /// <summary>
        /// 回転・拡大は呼出し側で頂点へ焼き込み済みか。
        /// true のとき、ディスパッチャは Placement の回転・拡大を頂点へ入れ直さない。
        /// </summary>
        [PLParam(Ignore = true, Description = "回転・拡大を呼出し側が頂点へ入れ済みか")]
        public bool PoseAlreadyBaked { get; }

        public AddGeneratedMeshCommand(
            int modelIndex, MeshObject mesh, string meshName,
            PrimitivePlacement placement, bool poseAlreadyBaked)
            : base(modelIndex)
        {
            Mesh             = mesh;
            MeshName         = meshName;
            Placement        = placement;
            PoseAlreadyBaked = poseAlreadyBaked;
        }
    }

    // ================================================================
    // 単一メッシュを返さない生成
    // ================================================================

    /// <summary>
    /// 穴つなぎ。2つの穴（境界辺の連結成分）の縁どうしに面を張る。
    /// 穴は種頂点で指す。種から縁を復元するのは生成側。
    /// </summary>
    [PLCommand(Description = "穴つなぎ。2つの穴（境界辺の連結成分）の縁どうしに面を張る。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class CreateHoleBridgeCommand : PanelCommand
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの穴つなぎ UI の双方がここを参照する。

        /// <summary>橋の中間分割数の下限・上限。</summary>
        public const int SubdivisionsMin = 0;
        public const int SubdivisionsMax = 16;

        /// <summary>穴A のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "BridgeHoleA", Description = "穴A のメッシュ索引", Required = true)]
        public int MeshA { get; }

        /// <summary>穴A の種頂点。</summary>
        [PLParam(TextKey = "BridgeHoleAVertex", Description = "穴A の種頂点番号", Required = true)]
        public int VertexA { get; }

        /// <summary>穴B のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "BridgeHoleB", Description = "穴B のメッシュ索引", Required = true)]
        public int MeshB { get; }

        /// <summary>穴B の種頂点。</summary>
        [PLParam(TextKey = "BridgeHoleBVertex", Description = "穴B の種頂点番号", Required = true)]
        public int VertexB { get; }

        /// <summary>
        /// 穴A の進行方向ヒント頂点。縁をどちら回りに辿るかを決める。
        /// 辺で取り込んでいないときは -1（縁の並び順そのままで辿る）。
        /// </summary>
        [PLParam(TextKey = "BridgeHoleADirHint", Description = "穴A の進行方向ヒント頂点。-1 で指定なし")]
        public int DirectionHintA { get; }

        /// <summary>穴B の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "BridgeHoleBDirHint", Description = "穴B の進行方向ヒント頂点。-1 で指定なし")]
        public int DirectionHintB { get; }

        /// <summary>新規オブジェクトにするときの名前。</summary>
        [PLParam(TextKey = "MeshName", Description = "生成物の名前")]
        public string Name { get; }

        /// <summary>行き先。図形生成と同じ「追加先」に従う。</summary>
        [PLParam(TextKey = "AddMode", Description = "新規オブジェクト / 既存へ追加 / 新規モデル")]
        public Poly_Ling.Player.PrimitiveAddMode AddMode { get; }

        [PLParam(TextKey = "AddTargetIndex", Description = "追加先の索引。-1 で選択の先頭")]
        public int AddTargetIndex { get; }

        /// <summary>
        /// 対応フリップと面フリップを、両穴の巻き方向から自動で決めるか。
        ///
        /// true のとき FlipCorrespondence / FlipFaces は使わず、
        /// BridgeLoopOps.TryAutoFlags の判定結果を使う。裏返りとねじれを避けるための既定。
        /// 判定できなかったときはフラグを変えない（TryAutoFlags の仕様）。
        ///
        /// false のときはコマンドの値をそのまま使う。手作業でチェックを
        /// 外した状態を再現したいときに使う。
        /// </summary>
        [PLParam(TextKey = "BridgeAutoFlags", Description = "対応と面の向きを自動で決める")]
        public bool AutoFlags { get; }

        /// <summary>縁どうしの対応をずらす。AutoFlags が true のときは使わない。</summary>
        [PLParam(TextKey = "BridgeFlipPair", Description = "縁どうしの対応を反転する")]
        public bool FlipCorrespondence { get; }

        /// <summary>張った面を裏返す。AutoFlags が true のときは使わない。</summary>
        [PLParam(TextKey = "BridgeFlipFaces", Description = "張った面を裏返す")]
        public bool FlipFaces { get; }

        /// <summary>橋の中間分割数。</summary>
        [PLParam(TextKey = "BridgeSubdiv", Description = "橋の中間分割数",
                 Min = SubdivisionsMin, Max = SubdivisionsMax, Step = 1)]
        public int Subdivisions { get; }

        public CreateHoleBridgeCommand(
            int modelIndex, int meshA, int vertexA, int meshB, int vertexB, string name,
            Poly_Ling.Player.PrimitiveAddMode addMode, int addTargetIndex,
            bool flipCorrespondence, bool flipFaces, int subdivisions,
            int directionHintA = -1, int directionHintB = -1,
            bool autoFlags = true)
            : base(modelIndex)
        {
            AutoFlags          = autoFlags;
            MeshA              = meshA;
            VertexA            = vertexA;
            MeshB              = meshB;
            VertexB            = vertexB;
            DirectionHintA     = directionHintA;
            DirectionHintB     = directionHintB;
            Name               = name;
            AddMode            = addMode;
            AddTargetIndex     = addTargetIndex;
            FlipCorrespondence = flipCorrespondence;
            FlipFaces          = flipFaces;
            Subdivisions       = subdivisions;
        }
    }

    /// <summary>
    /// 辺群ブリッジ。拾った辺そのものを辺群として、その間に面を張る。
    /// 開いた辺の連なりも扱える点が穴つなぎと違う。
    /// 辺は同一メッシュのものに限る（生成側が2群へ分けるため）。
    /// </summary>
    [PLCommand(Description = "辺群ブリッジ。拾った辺そのものを辺群として、その間に面を張る。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class CreateEdgeBridgeCommand : PanelCommand
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と EdgeBridgeToolHandler.Subdivisions の丸めの双方がここを参照する。
        // 穴つなぎ（CreateHoleBridgeCommand）とは上限が違う。辺群ブリッジは
        // 開いた辺の連なりも扱うため、もとから広い範囲を許している。

        /// <summary>橋の中間分割数の下限・上限。</summary>
        public const int SubdivisionsMin = 0;
        public const int SubdivisionsMax = 32;

        /// <summary>辺のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "EdgeBridgeMesh", Description = "対象メッシュの索引", Required = true)]
        public int MeshIndex { get; }

        /// <summary>拾った辺。両端の頂点番号の組で表す。</summary>
        [PLParam(TextKey = "EdgeBridgeEdges", Description = "拾った辺の列", Required = true)]
        public Poly_Ling.Selection.VertexPair[] Edges { get; }

        /// <summary>面の向きを自動で決めるか。</summary>
        [PLParam(TextKey = "BridgeAutoSelect", Description = "対応と面の向きを自動で決める")]
        public bool AutoCorrespondence { get; }

        [PLParam(TextKey = "BridgeFlipPair", Description = "縁どうしの対応を反転する")]
        public bool FlipCorrespondence { get; }

        [PLParam(TextKey = "BridgeFlipFaces", Description = "張った面を裏返す")]
        public bool FlipFaces { get; }

        [PLParam(TextKey = "BridgeSubdiv", Description = "橋の中間分割数",
                 Min = SubdivisionsMin, Max = SubdivisionsMax, Step = 1)]
        public int Subdivisions { get; }

        public CreateEdgeBridgeCommand(
            int modelIndex, int meshIndex, Poly_Ling.Selection.VertexPair[] edges,
            bool autoCorrespondence, bool flipCorrespondence, bool flipFaces, int subdivisions)
            : base(modelIndex)
        {
            MeshIndex          = meshIndex;
            Edges              = edges;
            AutoCorrespondence = autoCorrespondence;
            FlipCorrespondence = flipCorrespondence;
            FlipFaces          = flipFaces;
            Subdivisions       = subdivisions;
        }
    }

    /// <summary>
    /// プロジェクトを空にして、モデルを 1 つだけ作り直す。
    ///
    /// 【何のためか】
    ///   自動検証は系統ごとに「まっさらな状態」から積み上げる。
    ///   モデルを足すだけだと前の系統のモデルが残り、保存したフォルダに
    ///   関係ないモデルが同梱される（CsvProjectSerializer はプロジェクト内の
    ///   全モデルをフォルダへ書く）。何をどの順番でやった結果なのかが
    ///   読み取れなくなるので、明示的に捨てる口を用意する。
    ///
    /// 【破壊的】
    ///   開いているモデルを全部捨てる。Undo では戻せない。
    ///   UI のボタンには出さず、自動検証とリモートからのみ使う。
    /// </summary>
    [PLCommand(Description = "プロジェクトを空にして、モデルを 1 つだけ作り直す。")]
    public class ResetProjectCommand : PanelCommand
    {
        /// <summary>作り直すモデルの名前。空なら "Model"。</summary>
        [PLParam(TextKey = "ResetProjectModelName", Description = "作り直すモデルの名前")]
        public string ModelName { get; }

        public ResetProjectCommand(string modelName = null)
            : base(0) { ModelName = modelName; }
    }
}
