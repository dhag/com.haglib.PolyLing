// PanelCommand.Query.cs
// 照会（モデルを変えない）と生データの取得・送信の操作要求。
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
    // 照会（モデルを変えない）
    //
    // 【編集系と分けている点】
    //   Undo に記録しない。RemoteOwnership の判定対象にしない
    //   （RemoteOwnership.IsOwnershipExempt へ足す）。
    //   ComputeWorldMatrices を呼ばない。
    //
    // 【結果の返し方】
    //   量のあるものは ModelContext.DataStore へ書き、戻り値には
    //   辞書の名前と件数だけを載せる（PLDataStore.cs の冒頭注記）。
    // ================================================================

    /// <summary>種を探す方法。QuerySeedElementCommand.Mode が使う。</summary>
    public enum PLSeedMode
    {
        /// <summary>基準点に最も近い頂点。</summary>
        NearestVertex = 0,

        /// <summary>面の重心が基準点に最も近い面。</summary>
        NearestFace = 1,

        /// <summary>基準点に最も近い境界頂点。</summary>
        NearestBoundaryVertex = 2,

        /// <summary>基準点に最も近い境界辺。頂点 2 個を返す。</summary>
        NearestBoundaryEdge = 3,

        /// <summary>最初の境界ループの先頭頂点。基準点は使わない。</summary>
        FirstBoundaryVertex = 4,
    }

    /// <summary>
    /// 生データの取得・送信が対象にする範囲。
    /// GetRawDataCommand / SetRawDataCommand が使う。
    /// </summary>
    public enum PLRawScope
    {
        /// <summary>masterIndex で指した描画オブジェクト 1 個。</summary>
        Object = 0,

        /// <summary>選択されている頂点・面だけ。辞書名があれば辞書の選択を使う。</summary>
        SelectedParts = 1,

        /// <summary>選択されている描画オブジェクト全部。辞書名があれば辞書の対象を使う。</summary>
        SelectedObjects = 2,

        /// <summary>モデル内の描画オブジェクト全部。</summary>
        Model = 3,
    }

    /// <summary>
    /// モデルの構成を数え、描画オブジェクトの索引・安定 ID・名前の対応を
    /// 結果辞書へ書く。モデルは変えない。
    /// </summary>
    [PLCommand(Description = "モデルの構成を数え、描画オブジェクトの索引・安定 ID・名前の対応を結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",        PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("modelIndex",   PLResultKind.Integer, Description = "読んだモデルの索引")]
    [PLResult("meshContexts", PLResultKind.Integer, Description = "モデルが持つ要素の総数")]
    [PLResult("drawables",    PLResultKind.Integer, Description = "描画オブジェクトの数")]
    [PLResult("bones",        PLResultKind.Integer, Description = "ボーンの数")]
    [PLResult("morphs",       PLResultKind.Integer, Description = "モーフの数")]
    public class QueryModelStructureCommand : PanelCommand
    {
        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QueryModelStructureCommand(int modelIndex, string resultName = null)
            : base(modelIndex)
        {
            ResultName = resultName;
        }
    }

    /// <summary>
    /// オブジェクトグループの状態を結果辞書へ書く。モデルは変えない。
    ///
    /// 【何のためにあるか】
    ///   自動更新が流れなかった理由は、グループ側の 3 つで決まる
    ///   （自動更新が立っているか / ソースを引けるか / 要更新か）。
    ///   外から読めないと、パネルの画面を見るしか確かめる手が無い。
    /// </summary>
    [PLCommand(Description = "オブジェクトグループの状態（自動更新・要更新・参照の生死）を結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",  PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("groups", PLResultKind.Integer, Description = "グループの数")]
    [PLResult("stale",  PLResultKind.Integer, Description = "要更新のグループの数")]
    public class QueryObjectGroupsCommand : PanelCommand
    {
        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QueryObjectGroupsCommand(int modelIndex, string resultName = null)
            : base(modelIndex)
        {
            ResultName = resultName;
        }
    }

    /// <summary>
    /// 描画オブジェクト 1 個のボーンウェイトの行き先を数え、結果辞書へ書く。モデルは変えない。
    ///
    /// 【何のためにあるか】
    ///   「塗り直されたか」は頂点数では判らない。どのボーンを指しているかで決まる。
    ///   接頭辞を渡すと、その名前で始まるボーンを指す頂点だけを別に数える。
    /// </summary>
    [PLCommand(Description = "描画オブジェクト 1 個のボーンウェイトの行き先を数え、結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",         PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("vertices",      PLResultKind.Integer, Description = "頂点数")]
    [PLResult("weighted",      PLResultKind.Integer, Description = "ウェイトを持つ頂点の数")]
    [PLResult("multiBone",     PLResultKind.Integer, Description = "2 本以上のボーンへ配られた頂点の数")]
    [PLResult("toPrefix",      PLResultKind.Integer, Description = "接頭辞に一致するボーンを指す頂点の数")]
    [PLResult("prefixBones",   PLResultKind.Integer, Description = "接頭辞に一致したボーンの本数")]
    [PLResult("distinctBones", PLResultKind.Integer, Description = "指されているボーンの種類数")]
    public class QuerySkinWeightSummaryCommand : PanelCommand
    {
        [PLParam(Description = "読む描画オブジェクトの masterIndex", Required = true, IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "別に数えるボーン名の接頭辞。空にすると数えない")]
        public string BonePrefix { get; }

        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QuerySkinWeightSummaryCommand(
            int modelIndex, int masterIndex, string bonePrefix = "", string resultName = null)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            BonePrefix  = bonePrefix ?? "";
            ResultName  = resultName;
        }
    }

    /// <summary>
    /// 描画オブジェクト 1 個の規模を数え、結果辞書へ書く。モデルは変えない。
    /// </summary>
    [PLCommand(Description = "描画オブジェクト 1 個の頂点数・面数・材質数・境界ループ数・バウンディングボックスを数え、結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",         PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("masterIndex",   PLResultKind.Integer, Description = "読んだ描画オブジェクトの masterIndex")]
    [PLResult("name",          PLResultKind.Text,    Description = "描画オブジェクトの名前")]
    [PLResult("objectId",      PLResultKind.Text,    Description = "安定 ID。10 進の文字列")]
    [PLResult("vertices",      PLResultKind.Integer, Description = "頂点数")]
    [PLResult("faces",         PLResultKind.Integer, Description = "面数")]
    [PLResult("triangles",     PLResultKind.Integer, Description = "三角形換算の面数")]
    [PLResult("materialsUsed", PLResultKind.Integer, Description = "面が実際に使っている材質スロットの異なり数")]
    [PLResult("boundaryLoops", PLResultKind.Integer, Description = "境界ループ（穴）の数")]
    public class QueryDrawableStatsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true,
                 IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QueryDrawableStatsCommand(int modelIndex, int masterIndex, string resultName = null)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            ResultName  = resultName;
        }
    }

    /// <summary>
    /// 描画オブジェクトの穴（境界ループ）を集め、結果辞書へループ群として書く。
    /// モデルは変えない。
    /// </summary>
    [PLCommand(Description = "描画オブジェクトの穴（境界ループ）を集め、各穴の頂点列と重心を結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",            PLResultKind.Entry,        Description = "書き込んだ結果辞書の見出し")]
    [PLResult("masterIndex",      PLResultKind.Integer,      Description = "読んだ描画オブジェクトの masterIndex")]
    [PLResult("holes",            PLResultKind.Integer,      Description = "穴の数")]
    [PLResult("holeVertexCounts", PLResultKind.IntegerArray, Description = "穴ごとの頂点数。並びは結果辞書のループの並びと同じ")]
    public class QueryHolesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true,
                 IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QueryHolesCommand(int modelIndex, int masterIndex, string resultName = null)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            ResultName  = resultName;
        }
    }

    /// <summary>
    /// 条件に合う要素を 1 個だけ返す。位相変更コマンドの種を決めるために使う。
    /// モデルは変えない。
    /// </summary>
    [PLCommand(Description = "条件に合う頂点・面・境界辺を 1 個だけ返す。位相変更コマンドの種を決めるために使う。モデルは変えない。")]
    [PLResult("entry",       PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("masterIndex", PLResultKind.Integer, Description = "読んだ描画オブジェクトの masterIndex")]
    [PLResult("mode",        PLResultKind.Text,    Description = "使った探し方")]
    [PLResult("found",       PLResultKind.Flag,    Description = "見つかったか")]
    [PLResult("vertex",      PLResultKind.Integer, Description = "見つかった頂点番号。見つからなければ -1", Optional = true)]
    [PLResult("vertex2",     PLResultKind.Integer, Description = "辺のもう一方の頂点番号。辺以外では -1", Optional = true)]
    [PLResult("face",        PLResultKind.Integer, Description = "見つかった面番号。面以外では -1", Optional = true)]
    [PLResult("distance",    PLResultKind.Number,  Description = "基準点からのワールド距離", Optional = true)]
    public class QuerySeedElementCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true,
                 IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "QuerySeedMode",
                 Description = "探し方", Required = true)]
        public PLSeedMode Mode { get; }

        [PLParam(TextKey = "QuerySeedPoint",
                 Description = "基準点（ワールド座標）。先頭を取る探し方では使わない")]
        public Vector3 WorldPosition { get; }

        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QuerySeedElementCommand(
            int modelIndex, int masterIndex, PLSeedMode mode,
            Vector3 worldPosition = default, string resultName = null)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            Mode          = mode;
            WorldPosition = worldPosition;
            ResultName    = resultName;
        }
    }

    /// <summary>
    /// ボーン階層とスキンウェイトの分布を数え、結果辞書へ書く。モデルは変えない。
    /// </summary>
    [PLCommand(Description = "ボーン階層と Humanoid 割当、スキンウェイトの分布を数え、結果辞書へ書く。モデルは変えない。")]
    [PLResult("entry",            PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("bones",            PLResultKind.Integer, Description = "ボーンの数")]
    [PLResult("roots",            PLResultKind.Integer, Description = "親を持たないボーンの数")]
    [PLResult("maxDepth",         PLResultKind.Integer, Description = "階層の最大の深さ。根が 0")]
    [PLResult("humanoidAssigned", PLResultKind.Integer, Description = "Humanoid の骨名が入っているボーンの数")]
    [PLResult("skinnedDrawables", PLResultKind.Integer, Description = "ウェイトを持つ描画オブジェクトの数")]
    [PLResult("weightedVertices", PLResultKind.Integer, Description = "ウェイトを持つ頂点の数")]
    [PLResult("usedBones",        PLResultKind.Integer, Description = "ウェイトから参照されているボーンの異なり数")]
    public class QueryBoneSkinCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "ウェイトを数える描画オブジェクトの masterIndex。省くとモデル全体",
                 IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        public QueryBoneSkinCommand(int modelIndex, int masterIndex = -1, string resultName = null)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            ResultName  = resultName;
        }
    }

    /// <summary>
    /// コマンド定義の検査（PanelCommandFactoryAudit.RunAll）を回して結果を返す。
    /// モデルもプロジェクトも見ない。
    /// </summary>
    [PLCommand(Description = "コマンド定義の検査を回し、PLParam の付け忘れ・action 衝突・未対応の型・道具として出せた数を返す。モデルもプロジェクトも見ない。")]
    [PLResult("report",       PLResultKind.Text,    Description = "検査結果の全文。複数行")]
    [PLResult("toolsUsable",  PLResultKind.Integer, Description = "道具として出せた数")]
    [PLResult("toolsSkipped", PLResultKind.Integer, Description = "道具として出せなかった数")]
    public class QueryCommandAuditCommand : PanelCommand
    {
        public QueryCommandAuditCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    /// <summary>
    /// 現在の選択（またはパーツ選択セット）を結果辞書へ写す。形状は変えない。
    /// </summary>
    [PLCommand(Description = "現在の選択を結果辞書へ IndexSet として写す。getRawData / setRawData の setName から引ける。形状は変えない。")]
    [PLResult("entry",       PLResultKind.Entry,   Description = "書き込んだ結果辞書の見出し")]
    [PLResult("masterIndex", PLResultKind.Integer, Description = "写した選択が属する描画オブジェクトの masterIndex")]
    [PLResult("vertices",    PLResultKind.Integer, Description = "写した頂点の数")]
    [PLResult("edges",       PLResultKind.Integer, Description = "写した辺の数")]
    [PLResult("faces",       PLResultKind.Integer, Description = "写した面の数")]
    [PLResult("lines",       PLResultKind.Integer, Description = "写した線分の数")]
    public class SaveSelectionToDataStoreCommand : PanelCommand
    {
        [PLParam(TextKey = "QueryResultName",
                 Description = "結果を書き込む辞書の名前。省くと自動で付ける")]
        public string ResultName { get; }

        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex。省くと現在の編集対象",
                 IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "PartsSetIndex",
                 Description = "写すパーツ選択セットの索引。省くと現在の選択を写す")]
        public int PartsSetIndex { get; }

        public SaveSelectionToDataStoreCommand(
            int modelIndex, string resultName = null, int masterIndex = -1, int partsSetIndex = -1)
            : base(modelIndex)
        {
            ResultName    = resultName;
            MasterIndex   = masterIndex;
            PartsSetIndex = partsSetIndex;
        }
    }

    // ================================================================
    // 生データの取得・送信
    //
    // 【原則の例外】
    //   量のあるもの（番号列・座標列）は回線に乗せない、が原則。
    //   この 2 本だけは生データそのものを載せる。
    //
    // 【平坦化】
    //   可変長のものは「個数の列」と「連結した値の列」の 2 本で持つ。
    //   入れ子の配列は JsonParser.ParseFlat が受け取れない
    //   （'[' で始まる値を捨てる。RemoteProtocol.cs:227）。
    //
    // 【送信は位相を変えない】
    //   SetRawData は座標・UV・法線・ID・ウェイト・フラグの書き戻しだけ。
    //   頂点数・面数が合わなければ拒否する。面の作り替えは位相変更の
    //   コマンド（knife* / deleteFaces / createHoleBridge ほか）を使うこと。
    // ================================================================

    /// <summary>生データを取得する。モデルは変えない。</summary>
    [PLCommand(Description = "描画オブジェクトの生データ（座標・UV・法線・ID・面・ウェイト）を取得する。量があるので取る種別と範囲を絞って呼ぶこと。モデルは変えない。")]
    [PLResult("objects",       PLResultKind.Integer,      Description = "読んだ描画オブジェクトの数")]
    [PLResult("masterIndices", PLResultKind.IntegerArray, Description = "読んだ描画オブジェクトの masterIndex")]
    [PLResult("objectIds",     PLResultKind.TextArray,    Description = "同じ並びの安定 ID。10 進の文字列")]
    [PLResult("names",         PLResultKind.TextArray,    Description = "同じ並びの名前")]
    [PLResult("vertexCounts",  PLResultKind.IntegerArray, Description = "オブジェクトごとに返した頂点の数")]
    [PLResult("faceCounts",    PLResultKind.IntegerArray, Description = "オブジェクトごとに返した面の数")]
    [PLResult("vertexIndices", PLResultKind.IntegerArray, Description = "返した頂点の元の番号。全オブジェクトぶんを連結")]
    [PLResult("faceIndices",   PLResultKind.IntegerArray, Description = "返した面の元の番号。全オブジェクトぶんを連結", Optional = true)]
    [PLResult("positions",     PLResultKind.NumberArray,  Description = "頂点座標。x,y,z の順に 3 個ずつ", Optional = true)]
    [PLResult("vertexIds",     PLResultKind.IntegerArray, Description = "Id, PartsId, SubId の順に 3 個ずつ", Optional = true)]
    [PLResult("vertexFlags",   PLResultKind.IntegerArray, Description = "頂点フラグ。1 頂点 1 個", Optional = true)]
    [PLResult("uvCounts",      PLResultKind.IntegerArray, Description = "頂点ごとの UV スロット数", Optional = true)]
    [PLResult("uvs",           PLResultKind.NumberArray,  Description = "UV。u,v の順に 2 個ずつ連結", Optional = true)]
    [PLResult("normalCounts",  PLResultKind.IntegerArray, Description = "頂点ごとの法線スロット数", Optional = true)]
    [PLResult("normals",       PLResultKind.NumberArray,  Description = "法線。x,y,z の順に 3 個ずつ連結", Optional = true)]
    [PLResult("weightHas",     PLResultKind.IntegerArray, Description = "頂点がウェイトを持つか。1 頂点 1 個の 0/1", Optional = true)]
    [PLResult("weightBones",   PLResultKind.IntegerArray, Description = "ウェイトのボーン masterIndex。1 頂点 4 個", Optional = true)]
    [PLResult("weightValues",  PLResultKind.NumberArray,  Description = "ウェイトの重み。1 頂点 4 個", Optional = true)]
    [PLResult("faceSizes",     PLResultKind.IntegerArray, Description = "面ごとの頂点数", Optional = true)]
    [PLResult("faceVertices",  PLResultKind.IntegerArray, Description = "面の頂点番号。faceSizes の数だけ連結", Optional = true)]
    [PLResult("faceUVs",       PLResultKind.IntegerArray, Description = "面の UV スロット番号。同じ並び", Optional = true)]
    [PLResult("faceNormals",   PLResultKind.IntegerArray, Description = "面の法線スロット番号。同じ並び", Optional = true)]
    [PLResult("faceMaterials", PLResultKind.IntegerArray, Description = "面の材質スロット番号。1 面 1 個", Optional = true)]
    [PLResult("faceFlags",     PLResultKind.IntegerArray, Description = "面のフラグ。1 面 1 個", Optional = true)]
    [PLResult("truncated",     PLResultKind.Flag,         Description = "limit で打ち切ったか")]
    public class GetRawDataCommand : PanelCommand
    {
        [PLParam(TextKey = "RawScope", Description = "対象の範囲", Required = true)]
        public PLRawScope Scope { get; }

        [PLParam(TextKey = "MasterIndex",
                 Description = "Scope が Object のときの対象。省くと -1", IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "RawSetName",
                 Description = "選択の代わりに使う結果辞書の項目名。省くと現在の選択を使う")]
        public string SetName { get; }

        [PLParam(TextKey = "RawIncludePositions", Description = "頂点座標を返す。既定は true")]
        public bool IncludePositions { get; }

        [PLParam(TextKey = "RawIncludeUVs", Description = "UV を返す")]
        public bool IncludeUVs { get; }

        [PLParam(TextKey = "RawIncludeNormals", Description = "法線を返す")]
        public bool IncludeNormals { get; }

        [PLParam(TextKey = "RawIncludeIds", Description = "頂点 ID・部品 ID・サブ ID を返す")]
        public bool IncludeIds { get; }

        [PLParam(TextKey = "RawIncludeFaces", Description = "面を返す")]
        public bool IncludeFaces { get; }

        [PLParam(TextKey = "RawIncludeWeights", Description = "ボーンウェイトを返す")]
        public bool IncludeWeights { get; }

        [PLParam(TextKey = "RawIncludeFlags", Description = "頂点と面のフラグを返す")]
        public bool IncludeFlags { get; }

        [PLParam(TextKey = "RawOffset", Description = "頂点・面を返し始める位置。既定は 0", Min = 0)]
        public int Offset { get; }

        [PLParam(TextKey = "RawLimit",
                 Description = "オブジェクトごとに返す頂点・面の上限。0 で全件", Min = 0)]
        public int Limit { get; }

        public GetRawDataCommand(
            int modelIndex, PLRawScope scope,
            int masterIndex       = -1,
            string setName        = null,
            bool includePositions = true,
            bool includeUVs       = false,
            bool includeNormals   = false,
            bool includeIds       = false,
            bool includeFaces     = false,
            bool includeWeights   = false,
            bool includeFlags     = false,
            int offset            = 0,
            int limit             = 0)
            : base(modelIndex)
        {
            Scope            = scope;
            MasterIndex      = masterIndex;
            SetName          = setName;
            IncludePositions = includePositions;
            IncludeUVs       = includeUVs;
            IncludeNormals   = includeNormals;
            IncludeIds       = includeIds;
            IncludeFaces     = includeFaces;
            IncludeWeights   = includeWeights;
            IncludeFlags     = includeFlags;
            Offset           = offset;
            Limit            = limit;
        }
    }

    /// <summary>
    /// 生データを書き戻す。位相は変えない。
    /// 対象は描画オブジェクト 1 個だけ。数が合わなければ拒否する。
    /// </summary>
    [PLCommand(Description = "描画オブジェクト 1 個へ生データ（座標・UV・法線・ID・ウェイト・フラグ・材質）を書き戻す。位相は変えない。数が合わなければ拒否する。")]
    [PLResult("masterIndex",     PLResultKind.Integer, Description = "書き戻した描画オブジェクトの masterIndex")]
    [PLResult("vertices",        PLResultKind.Integer, Description = "書き戻した頂点の数")]
    [PLResult("faces",           PLResultKind.Integer, Description = "書き戻した面の数")]
    [PLResult("positionsWritten", PLResultKind.Flag,   Description = "座標を書いたか")]
    [PLResult("uvsWritten",      PLResultKind.Flag,    Description = "UV を書いたか")]
    [PLResult("normalsWritten",  PLResultKind.Flag,    Description = "法線を書いたか")]
    [PLResult("idsWritten",      PLResultKind.Flag,    Description = "ID を書いたか")]
    [PLResult("weightsWritten",  PLResultKind.Flag,    Description = "ウェイトを書いたか")]
    public class SetRawDataCommand : PanelCommand
    {
        [PLParam(TextKey = "RawScope", Description = "対象の範囲。解決した結果が 1 個でなければ拒否する", Required = true)]
        public PLRawScope Scope { get; }

        [PLParam(TextKey = "MasterIndex",
                 Description = "Scope が Object のときの対象。省くと -1", IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "RawSetName",
                 Description = "選択の代わりに使う結果辞書の項目名。省くと現在の選択を使う")]
        public string SetName { get; }

        [PLParam(TextKey = "RawVertexIndices",
                 Description = "書き戻す頂点の番号。省くと範囲が決めた並びをそのまま使う")]
        public int[] VertexIndices { get; }

        [PLParam(TextKey = "RawPositions",
                 Description = "頂点座標。x,y,z の順に 3 個ずつ。数が合わなければ拒否する")]
        public float[] Positions { get; }

        [PLParam(TextKey = "RawVertexIds",
                 Description = "Id, PartsId, SubId の順に 3 個ずつ")]
        public int[] VertexIds { get; }

        [PLParam(TextKey = "RawVertexFlags", Description = "頂点フラグ。1 頂点 1 個")]
        public int[] VertexFlags { get; }

        [PLParam(TextKey = "RawUvCounts", Description = "頂点ごとの UV スロット数")]
        public int[] UvCounts { get; }

        [PLParam(TextKey = "RawUvs", Description = "UV。u,v の順に 2 個ずつ連結")]
        public float[] Uvs { get; }

        [PLParam(TextKey = "RawNormalCounts", Description = "頂点ごとの法線スロット数")]
        public int[] NormalCounts { get; }

        [PLParam(TextKey = "RawNormals", Description = "法線。x,y,z の順に 3 個ずつ連結")]
        public float[] Normals { get; }

        [PLParam(TextKey = "RawWeightHas", Description = "頂点がウェイトを持つか。1 頂点 1 個の 0/1")]
        public int[] WeightHas { get; }

        [PLParam(TextKey = "RawWeightBones", Description = "ウェイトのボーン masterIndex。1 頂点 4 個")]
        public int[] WeightBones { get; }

        [PLParam(TextKey = "RawWeightValues", Description = "ウェイトの重み。1 頂点 4 個")]
        public float[] WeightValues { get; }

        [PLParam(TextKey = "RawFaceIndices",
                 Description = "書き戻す面の番号。省くと材質・フラグは書かない")]
        public int[] FaceIndices { get; }

        [PLParam(TextKey = "RawFaceMaterials", Description = "面の材質スロット番号。1 面 1 個")]
        public int[] FaceMaterials { get; }

        [PLParam(TextKey = "RawFaceFlags", Description = "面のフラグ。1 面 1 個")]
        public int[] FaceFlags { get; }

        public SetRawDataCommand(
            int modelIndex, PLRawScope scope,
            int masterIndex      = -1,
            string setName       = null,
            int[] vertexIndices  = null,
            float[] positions    = null,
            int[] vertexIds      = null,
            int[] vertexFlags    = null,
            int[] uvCounts       = null,
            float[] uvs          = null,
            int[] normalCounts   = null,
            float[] normals      = null,
            int[] weightHas      = null,
            int[] weightBones    = null,
            float[] weightValues = null,
            int[] faceIndices    = null,
            int[] faceMaterials  = null,
            int[] faceFlags      = null)
            : base(modelIndex)
        {
            Scope         = scope;
            MasterIndex   = masterIndex;
            SetName       = setName;
            VertexIndices = vertexIndices;
            Positions     = positions;
            VertexIds     = vertexIds;
            VertexFlags   = vertexFlags;
            UvCounts      = uvCounts;
            Uvs           = uvs;
            NormalCounts  = normalCounts;
            Normals       = normals;
            WeightHas     = weightHas;
            WeightBones   = weightBones;
            WeightValues  = weightValues;
            FaceIndices   = faceIndices;
            FaceMaterials = faceMaterials;
            FaceFlags     = faceFlags;
        }
    }
}
