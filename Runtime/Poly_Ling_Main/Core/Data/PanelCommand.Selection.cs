// PanelCommand.Selection.cs
// 選択（オブジェクト・頂点／辺／面・詳細選択）と選択辞書・パーツ辞書の操作要求。
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
    // 選択
    // ================================================================

    [PLCommand(Description = "リスト内のオブジェクトを選択し直す。分類ごとに索引の集合を差し替える。")]
    public class SelectMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectMeshCategory",
                 Description = "選択するリストの分類", Required = true)]
        public MeshCategory Category { get; }

        [PLParam(TextKey = "SelectMeshIndices",
                 Description = "Category のリスト内での索引", Required = true)]
        public int[] Indices { get; }
        public SelectMeshCommand(int modelIndex, MeshCategory category, int[] indices)
            : base(modelIndex) { Category = category; Indices = indices; }
    }

    // ================================================================
    // パーツ選択辞書
    // ================================================================

    /// <summary>現在のパーツ選択をセットとして保存</summary>
    [PLCommand(Description = "現在のパーツ選択をセットとして保存</summary>")]
    public class SavePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetName",
                 Description = "保存するパーツ選択セットの名前", Required = true)]
        public string SetName { get; }
        public SavePartsSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>選択辞書エントリを現在の選択に適用（置き換え）</summary>
    [PLCommand(Description = "選択辞書エントリを現在の選択に適用（置き換え）</summary>")]
    public class LoadPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "適用するパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public LoadPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを現在の選択に追加（Union）</summary>
    [PLCommand(Description = "選択辞書エントリを現在の選択に追加（Union）</summary>")]
    public class AddPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "現在の選択へ足すパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public AddPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>現在の選択から辞書エントリを除外（Subtract）</summary>
    [PLCommand(Description = "現在の選択から辞書エントリを除外（Subtract）</summary>")]
    public class SubtractPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "現在の選択から引くパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public SubtractPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    [PLCommand(Description = "選択辞書エントリを削除</summary>")]
    public class DeletePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "削除するパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public DeletePartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    [PLCommand(Description = "選択辞書エントリの名前を変更</summary>")]
    public class RenamePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "名前を変えるパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "PartsSetNewName",
                 Description = "パーツ選択セットの新しい名前", Required = true)]
        public string NewName { get; }
        public RenamePartsSetCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    /// <summary>
    /// 選択辞書をCSVフォルダへエクスポート。
    /// FolderPath が空のときは実行側でダイアログを開く（メインエディタ経路）。
    ///
    /// パスは PLSandbox が作業フォルダの下へ閉じ込める。
    /// </summary>
    [PLCommand(Description = "選択辞書をCSVフォルダへエクスポート。")]
    public class ExportPartsSetsCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetExportFolder",
                 Description = "書き出し先フォルダ。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）")]
        public string FolderPath { get; }
        public ExportPartsSetsCsvCommand(int modelIndex) : base(modelIndex) { FolderPath = null; }
        public ExportPartsSetsCsvCommand(int modelIndex, string folderPath)
            : base(modelIndex) { FolderPath = folderPath; }
    }

    /// <summary>
    /// CSVフォルダから選択辞書をインポート。
    /// FolderPath が空のときは実行側でダイアログを開く（メインエディタ経路・単一ファイル）。
    /// ByObjectName が true のときはファイル内の "# object" 名と一致するオブジェクトへ読み込む。
    /// </summary>
    [PLCommand(Description = "CSVフォルダから選択辞書をインポート。")]
    public class ImportPartsSetCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetImportFolder",
                 Description = "読み込み元フォルダ。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）")]
        public string FolderPath   { get; }

        [PLParam(TextKey = "PartsSetImportByObjectName",
                 Description = "ファイル内の \"# object\" 名と一致するオブジェクトへ読み込む。既定は false")]
        public bool   ByObjectName { get; }
        public ImportPartsSetCsvCommand(int modelIndex)
            : base(modelIndex) { FolderPath = null; ByObjectName = false; }
        public ImportPartsSetCsvCommand(int modelIndex, string folderPath, bool byObjectName)
            : base(modelIndex) { FolderPath = folderPath; ByObjectName = byObjectName; }
    }

    // ================================================================
    // 選択辞書
    // ================================================================

    /// <summary>選択中のメッシュを選択辞書エントリとして保存</summary>
    [PLCommand(Description = "選択中のメッシュを選択辞書エントリとして保存</summary>")]
    public class SaveSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionaryCategory",
                 Description = "保存する選択辞書エントリの分類", Required = true)]
        public MeshCategory Category { get; }

        [PLParam(TextKey = "SelectionDictionarySetName",
                 Description = "保存する選択辞書エントリの名前", Required = true)]
        public string SetName { get; }

        [PLParam(TextKey = "SelectionDictionaryMeshNames",
                 Description = "エントリに含める描画オブジェクトの名前", Required = true)]
        public string[] MeshNames { get; }
        public SaveSelectionDictionaryCommand(int modelIndex, MeshCategory category, string setName, string[] meshNames)
            : base(modelIndex) { Category = category; SetName = setName; MeshNames = meshNames; }
    }

    /// <summary>選択辞書エントリを選択に適用（置き換えまたは追加）</summary>
    [PLCommand(Description = "選択辞書エントリを選択に適用（置き換えまたは追加）</summary>")]
    public class ApplySelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "適用する選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "SelectionDictionaryAddToExisting",
                 Description = "現在の選択へ足す。false で置き換える。既定は false")]
        public bool AddToExisting { get; }
        public ApplySelectionDictionaryCommand(int modelIndex, int setIndex, bool addToExisting = false)
            : base(modelIndex) { SetIndex = setIndex; AddToExisting = addToExisting; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    [PLCommand(Description = "選択辞書エントリを削除</summary>")]
    public class DeleteSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "削除する選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }
        public DeleteSelectionDictionaryCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    [PLCommand(Description = "選択辞書エントリの名前を変更</summary>")]
    public class RenameSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "名前を変える選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "SelectionDictionaryNewName",
                 Description = "選択辞書エントリの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameSelectionDictionaryCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    /// <summary>
    /// パネル側でモデルを直接変更した後、全パネルにリスト構造変更を通知する。
    /// Paste / LoadCSV 等で使用。
    /// </summary>
    [PLCommand(Description = "パネル側でモデルを直接変更した後、全パネルにリスト構造変更を通知する。")]
    public class NotifyListStructureChangedCommand : PanelCommand
    {
        public NotifyListStructureChangedCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// パネル側で辞書メタデータを直接変更した後、全パネルに Attributes 変更を通知する。
    /// OnLoadDicFile 等で使用。
    /// </summary>
    [PLCommand(Description = "パネル側で辞書メタデータを直接変更した後、全パネルに Attributes 変更を通知する。")]
    public class NotifyDictionaryChangedCommand : PanelCommand
    {
        public NotifyDictionaryChangedCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // 頂点・辺・面の選択
    // ================================================================

    /// <summary>
    /// 頂点・辺・面・線分をインデックス指定で選択する。
    ///
    /// 実処理は MoveToolHandler / PlayerSelectionOps が持つ。クリック経路と同じ
    /// 「スナップショット → 選択の書き換え → 頂点への展開 → Undo 記録」を通すため、
    /// このコマンドは対象と選ばせる要素だけを運ぶ。
    ///
    /// 【要素とメッシュの対応】
    ///   要素の索引はメッシュ内ローカル番号なので、どのメッシュのものかを
    ///   同じ並び・同じ長さの *MeshIndices で対にして渡す。
    ///     VertexIndices[i] は VertexMeshIndices[i] のメッシュの頂点
    ///     FaceIndices[i]   は FaceMeshIndices[i]   のメッシュの面
    ///     LineIndices[i]   は LineMeshIndices[i]   のメッシュの線分
    ///   辺だけは [v1a, v2a, v1b, v2b, ...] と 2 個 1 組で平坦化してあるので、
    ///   EdgeMeshIndices の長さは EdgePairs の半分になる。
    ///     EdgePairs[2i], EdgePairs[2i+1] は EdgeMeshIndices[i] のメッシュの辺
    ///   入れ子の配列を持てないための形。PanelCommandFactory は平坦な int[] しか
    ///   組み立てられない。
    ///
    /// 【操作の種類】
    ///   Op = Replace のとき、MasterIndices に挙げたメッシュの選択を先に消す。
    ///   1 メッシュだけ消すとほかのメッシュに残った選択が画面に出たままになる
    ///   （GPU のフラグは MeshContext 単位で立つため）。クリック経路の
    ///   ClearAllTargetsSilent と同じ範囲を明示で受ける形。
    ///   Op = Remove は列挙要素を選択から外す。Ctrl クリックが既選択に当たったとき
    ///   （ApplyElementClick の解除分岐）がこれに落ちる。
    ///   Op = Toggle は列挙要素を 1 個ずつ反転する。Ctrl の矩形・投げ縄選択が
    ///   これに落ちる（範囲内の既選択は外れ、未選択は入る）。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "頂点・辺・面・線分をインデックス指定で選択する。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている頂点の数")]
    [PLResult("edges",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている辺の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている面の数")]
    [PLResult("lines",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている線分の数")]
    public class SelectElementsCommand : PanelCommand
    {
        /// <summary>
        /// 選択の書き換え方。
        /// Replace = MasterIndices の選択を消してから列挙要素を選ぶ。
        /// Add     = 列挙要素を足す。
        /// Remove  = 列挙要素を外す。
        /// Toggle  = 列挙要素を 1 個ずつ反転する。
        /// </summary>
        public enum SelectOp { Replace, Add, Remove, Toggle }

        /// <summary>Replace のときに選択を消す対象メッシュの範囲</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "非加算のときに選択を消す対象の masterIndex 配列", Required = true)]
        public int[]   MasterIndices     { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds         { get; }

        /// <summary>選択する頂点の索引</summary>
        [PLParam(TextKey = "SelectVertexIndices",
                 Description = "選択する頂点の索引。省くか空にすると頂点を足さない", Required = true)]
        public int[]   VertexIndices     { get; }

        /// <summary>VertexIndices と同じ並び・同じ長さ。各頂点が属する masterIndex</summary>
        [PLParam(TextKey = "SelectVertexMeshIndices",
                 Description = "VertexIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   VertexMeshIndices { get; }

        /// <summary>選択する辺のフラット配列 [v1a, v2a, v1b, v2b, ...]</summary>
        [PLParam(TextKey = "SelectEdgePairs",
                 Description = "選択する辺を [v1, v2] の並びで平坦化したもの。省くか空にすると辺を足さない", Required = true)]
        public int[]   EdgePairs         { get; }

        /// <summary>EdgePairs の組ごとの masterIndex。長さは EdgePairs の半分</summary>
        [PLParam(TextKey = "SelectEdgeMeshIndices",
                 Description = "EdgePairs の組ごとの masterIndex。長さは EdgePairs の半分", Required = true)]
        public int[]   EdgeMeshIndices   { get; }

        /// <summary>選択する面の索引</summary>
        [PLParam(TextKey = "SelectFaceIndices",
                 Description = "選択する面の索引。省くか空にすると面を足さない", Required = true)]
        public int[]   FaceIndices       { get; }

        /// <summary>FaceIndices と同じ並び・同じ長さ。各面が属する masterIndex</summary>
        [PLParam(TextKey = "SelectFaceMeshIndices",
                 Description = "FaceIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   FaceMeshIndices   { get; }

        /// <summary>選択する線分の索引（MeshObject.Faces[] の添字。VertexCount==2）</summary>
        [PLParam(TextKey = "SelectLineIndices",
                 Description = "選択する線分の索引。省くか空にすると線分を足さない", Required = true)]
        public int[]   LineIndices       { get; }

        /// <summary>LineIndices と同じ並び・同じ長さ。各線分が属する masterIndex</summary>
        [PLParam(TextKey = "SelectLineMeshIndices",
                 Description = "LineIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   LineMeshIndices   { get; }

        /// <summary>選択の書き換え方</summary>
        [PLParam(TextKey = "SelectOp",
                 Description = "選択の書き換え方。Replace / Add / Remove / Toggle。既定は Replace")]
        public SelectOp Op                { get; }

        public SelectElementsCommand(
            int modelIndex, int[] masterIndices,
            int[] vertexIndices, int[] vertexMeshIndices,
            int[] edgePairs,     int[] edgeMeshIndices,
            int[] faceIndices,   int[] faceMeshIndices,
            int[] lineIndices,   int[] lineMeshIndices,
            SelectOp op = SelectOp.Replace,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices ?? System.Array.Empty<int>();
            ObjectIds         = objectIds;
            VertexIndices     = vertexIndices;
            VertexMeshIndices = vertexMeshIndices;
            EdgePairs         = edgePairs;
            EdgeMeshIndices   = edgeMeshIndices;
            FaceIndices       = faceIndices;
            FaceMeshIndices   = faceMeshIndices;
            LineIndices       = lineIndices;
            LineMeshIndices   = lineMeshIndices;
            Op                = op;
        }
    }

    // ================================================================
    // 詳細選択（Advanced Select）
    // ================================================================

    /// <summary>
    /// トポロジーベースの詳細選択を実行する。
    /// Mode に応じて使用する Seed フィールドが異なる。
    ///   Connected   : SeedVertexIndex >= 0 → 頂点起点
    ///                 SeedEdgeV1/V2  >= 0 → 辺起点
    ///                 SeedFaceIndex  >= 0 → 面起点
    ///   Belt        : SeedEdgeV1/V2（辺ペア必須）
    ///   EdgeLoop    : SeedEdgeV1/V2（辺ペア必須）
    ///   ShortestPath: SeedVertexIndex（始点）+ EndVertexIndex（終点）
    /// </summary>
    [PLCommand(Description = "トポロジーベースの詳細選択を実行する。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている頂点の数")]
    [PLResult("edges",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている辺の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている面の数")]
    [PLResult("lines",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている線分の数")]
    public class AdvancedSelectCommand : PanelCommand
    {
        /// <summary>
        /// 対象 MeshContext の MasterIndex 配列。
        /// 実処理（AdvancedSelectTool）は編集対象メッシュ 1 本にしか効かないため、
        /// 受け口は「1 個で、それが編集対象と一致すること」を要求する。
        /// 配列にしてあるのは他コマンドと形を揃えて ObjectIds と対にするため。
        /// </summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個", Required = true)]
        public int[]              MasterIndices     { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[]            ObjectIds         { get; }

        /// <summary>選択モード</summary>
        [PLParam(TextKey = "AdvancedSelectMode",
                 Description = "選択の広げ方。使う Seed がモードごとに変わる", Required = true)]
        public AdvancedSelectMode Mode              { get; }

        // ── Seed ──────────────────────────────────────────────────
        /// <summary>頂点起点インデックス（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedVertexIndex",
                 Description = "起点にする頂点の索引。-1 で不使用")]
        public int                SeedVertexIndex   { get; }

        /// <summary>辺起点 V1（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedEdgeV1",
                 Description = "起点にする辺の片側の頂点索引。-1 で不使用")]
        public int                SeedEdgeV1        { get; }

        /// <summary>辺起点 V2（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedEdgeV2",
                 Description = "起点にする辺のもう片側の頂点索引。-1 で不使用")]
        public int                SeedEdgeV2        { get; }

        /// <summary>面起点インデックス（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedFaceIndex",
                 Description = "起点にする面の索引。-1 で不使用")]
        public int                SeedFaceIndex     { get; }

        /// <summary>ShortestPath 終点インデックス（他モードでは無視）</summary>
        [PLParam(TextKey = "EndVertexIndex",
                 Description = "ShortestPath の終点頂点索引。他モードでは無視。-1 で不使用")]
        public int                EndVertexIndex    { get; }

        // ── 出力フラグ ──────────────────────────────────────────────
        //
        // モードによっては効かないものがある。実処理を持つ AdvancedSelectTool 側が
        // 意図的に外しているためで、EdgeLoop は頂点（EdgeLoopSelectMode.cs:28）、
        // ShortestPath は辺（ShortestPathSelectMode.cs:42）が対象外。
        [PLParam(TextKey = "AdvancedSelectVertices",
                 Description = "結果を頂点選択へ入れる。既定は true")]
        public bool               SelectVertices    { get; }

        [PLParam(TextKey = "AdvancedSelectEdges",
                 Description = "結果を辺選択へ入れる。既定は false")]
        public bool               SelectEdges       { get; }

        [PLParam(TextKey = "AdvancedSelectFaces",
                 Description = "結果を面選択へ入れる。既定は false")]
        public bool               SelectFaces       { get; }

        /// <summary>false = 既存選択をクリアしてから選択</summary>
        [PLParam(TextKey = "AdvancedSelectAdditive",
                 Description = "既存の選択へ足す。false で置き換える。既定は false")]
        public bool               Additive          { get; }

        /// <summary>
        /// EdgeLoop モードの方向一致閾値（cos値）。
        /// 既定は AdvancedSelectSettings.cs:41 の実既定と同じ 0.7。
        /// </summary>
        [PLParam(TextKey = "EdgeLoopThreshold",
                 Description = "EdgeLoop の方向一致しきい値（cos 値）。既定は 0.5",
                 LimitKey = "AdvancedSelect.EdgeLoopThreshold")]
        public float              EdgeLoopThreshold { get; }

        public AdvancedSelectCommand(
            int modelIndex, int[] masterIndices,
            AdvancedSelectMode mode,
            int seedVertexIndex   = -1,
            int seedEdgeV1        = -1,
            int seedEdgeV2        = -1,
            int seedFaceIndex     = -1,
            int endVertexIndex    = -1,
            bool selectVertices   = true,
            bool selectEdges      = false,
            bool selectFaces      = false,
            bool additive         = false,
            float edgeLoopThreshold = 0.7f,
            ulong[] objectIds       = null)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices ?? System.Array.Empty<int>();
            ObjectIds         = objectIds;
            Mode              = mode;
            SeedVertexIndex   = seedVertexIndex;
            SeedEdgeV1        = seedEdgeV1;
            SeedEdgeV2        = seedEdgeV2;
            SeedFaceIndex     = seedFaceIndex;
            EndVertexIndex    = endVertexIndex;
            SelectVertices    = selectVertices;
            SelectEdges       = selectEdges;
            SelectFaces       = selectFaces;
            Additive          = additive;
            EdgeLoopThreshold = edgeLoopThreshold;
        }
    }

    /// <summary>
    /// 属性で頂点を選ぶ（クリック非依存）。Undo記録付き。
    ///
    /// 実処理は AdvancedSelectTool.ExecuteAttributeSelect が持つ。パネルの「実行」
    /// ボタンと同じ経路を通すため、このコマンドは対象とモードとしきい値だけを運ぶ。
    ///
    /// 【AdvancedSelectCommand との違い】
    ///   あちらは起点（Seed）から選択を広げるモード用で、GPU ホバーが返した要素を
    ///   種にする。こちらは種を持たず、メッシュ全体の属性を走査する。
    ///   AdvancedSelectTool.IsAttributeMode が true を返すモードだけを受け付ける。
    ///
    /// 【LimitToCurrentSelection の効き方】
    ///   OFF … 判定に一致した頂点を AddToSelection に従って追加／削除する。
    ///   ON かつ AddToSelection = true  … 現在の選択のうち一致しなかった頂点を解除する（絞り込み）。
    ///   ON かつ AddToSelection = false … 現在の選択のうち一致した頂点を解除する。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    [PLCommand(Description = "属性で頂点を選ぶ（クリック非依存）。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている頂点の数")]
    [PLResult("edges",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている辺の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている面の数")]
    [PLResult("lines",    PLResultKind.Integer, Description = "実行後にモデル全体で選ばれている線分の数")]
    public class AdvancedSelectByAttributeCommand : PanelCommand
    {
        /// <summary>
        /// 対象 MeshContext の MasterIndex 配列。
        /// 実処理は編集対象メッシュ 1 本にしか効かないため、受け口は
        /// 「1 個で、それが編集対象と一致すること」を要求する。
        /// </summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個", Required = true)]
        public int[]              MasterIndices           { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[]            ObjectIds               { get; }

        /// <summary>属性モード。UvNormalCount / NearAxis のいずれか</summary>
        [PLParam(TextKey = "AttributeSelectMode",
                 Description = "属性の種類。UvNormalCount / NearAxis", Required = true)]
        public AdvancedSelectMode Mode                    { get; }

        /// <summary>true = 選択に追加、false = 選択から削除</summary>
        [PLParam(TextKey = "AttributeSelectAdd",
                 Description = "一致した頂点を選択へ足す。false で選択から外す。既定は true")]
        public bool               AddToSelection          { get; }

        /// <summary>
        /// UvNormalCount モードのしきい値。
        /// max(Vertex.UVs.Count, Vertex.Normals.Count) がこの値より大きい頂点を選ぶ。
        /// </summary>
        [PLParam(TextKey = "AttributeUvNormalCountThreshold",
                 Description = "UvNormalCount のしきい値。UV／法線の本数がこれを超える頂点を選ぶ")]
        public int                UvNormalCountThreshold  { get; }

        /// <summary>NearAxis モードの対称軸</summary>
        [PLParam(TextKey = "AttributeAxisKind",
                 Description = "NearAxis の基準軸。X / Y / Z")]
        public SymmetryAxis       AxisKind                { get; }

        /// <summary>NearAxis モードの距離しきい値（軸平面からの距離）</summary>
        [PLParam(TextKey = "AttributeAxisDistanceThreshold",
                 Description = "NearAxis の距離しきい値。軸平面からこの距離以内の頂点を選ぶ")]
        public float              AxisDistanceThreshold   { get; }

        /// <summary>現在の選択の中だけを対象にするか</summary>
        [PLParam(TextKey = "AttributeLimitToCurrentSelection",
                 Description = "現在の選択の中だけを対象にする。既定は false")]
        public bool               LimitToCurrentSelection { get; }

        public AdvancedSelectByAttributeCommand(
            int modelIndex, int[] masterIndices,
            AdvancedSelectMode mode,
            bool addToSelection             = true,
            int uvNormalCountThreshold      = 0,
            SymmetryAxis axisKind           = SymmetryAxis.X,
            float axisDistanceThreshold     = 0.00001f,
            bool limitToCurrentSelection    = false,
            ulong[] objectIds               = null)
            : base(modelIndex)
        {
            MasterIndices           = masterIndices ?? System.Array.Empty<int>();
            ObjectIds               = objectIds;
            Mode                    = mode;
            AddToSelection          = addToSelection;
            UvNormalCountThreshold  = uvNormalCountThreshold;
            AxisKind                = axisKind;
            AxisDistanceThreshold   = axisDistanceThreshold;
            LimitToCurrentSelection = limitToCurrentSelection;
        }
    }

    // ================================================================
    // 選択部品辞書の識別子
    // ================================================================

    /// <summary>
    /// 選択部品辞書が指している頂点から、識別子（頂点ID / 部品ID / サブID）を
    /// 控え直す。辞書化のときにも自動で控えるので、これは控え直したいときの手動口。
    /// </summary>
    [PLCommand(Description = "選択部品辞書の指す頂点から、頂点ID・部品ID・サブIDを控え直す。")]
    public class CapturePartsSetVertexIdsCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "辞書エントリの番号", Min = 0, Required = true)]
        public int SetIndex { get; }

        public CapturePartsSetVertexIdsCommand(int modelIndex, int setIndex)
            : base(modelIndex)
        {
            SetIndex = setIndex;
        }
    }

    /// <summary>
    /// 控えてある頂点IDから、選択部品辞書の頂点インデックスを引き直す。
    /// 頂点の挿入・削除で索引がずれたときの復旧口。
    /// 部品ID / サブID は引き当てには使わない。
    /// </summary>
    [PLCommand(Description = "控えた頂点IDから、選択部品辞書の頂点インデックスを引き直す。索引がずれたときの復旧。")]
    public class ResolvePartsSetByVertexIdCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "辞書エントリの番号", Min = 0, Required = true)]
        public int SetIndex { get; }

        public ResolvePartsSetByVertexIdCommand(int modelIndex, int setIndex)
            : base(modelIndex)
        {
            SetIndex = setIndex;
        }
    }
}
