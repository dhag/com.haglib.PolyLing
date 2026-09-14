// PanelCommand.PmxPartialImport.cs
// PMX を元にした部分差し替え（読み込み・対応付け・転送）の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【MQO 版との違い】
//   PanelCommand.PartialImport.cs と作りは同じだが 3 点違う。
//   ・読み込みが PMXImporter.ImportFile で、Scale / FlipX / FlipZ を伴う。
//     取り込み時に座標変換が掛かるので、転送側では掛からない（MQO は逆）。
//   ・照合が名前の完全一致を先に見て、外れたら頂点数を見る
//     （PMXPartialImportOps.cs:130-152）。
//   ・転送が位置・UV に加えてボーンウェイトも選べる。
//
// 【比べる頂点数を取り違えないこと】
//   PMX は 1 頂点に UV を 1 つしか持てない。UV の切れ目で頂点が分かれている。
//   MeshObject は 1 頂点に複数の UV を持てるので、頂点はまとまったまま。
//   よって PMX の実頂点数は、モデル側を UV で展開した後の数と対応する。
//   モデル側の生の頂点数（MeshObject.VertexCount）は展開していない数なので、
//   PMX より少ないのが正常。ここを比べると正しいデータを不一致と判定する。
//
//   比べるのは
//     モデル側 … PartialMeshEntry.ExpandedVertexCount（展開後）
//     PMX 側   … PartialPMXEntry.VertexCount（実頂点数）
//   両側に VertexCount という名前があり同じ意味に見えるため、
//   実装のたびに取り違えが起きている。返す 2 本も
//   modelExpandedCounts / pmxVertexCounts と別名にしてある。
//
// 【状態を持ち越さない】
//   PMXImporter は毎回読み直す。同じファイル・同じ設定なら PMXMeshes の
//   並びは同じになるので、組は「モデル側の描画オブジェクト索引」と
//   「PMX 側の並び番号」の 2 本の整数列で表せる。
//
// 【組は 2 本の配列で表す】
//   AutoMatch は組を記録せず両側に Selected を立てるだけで、
//   ExecuteVertexAttributeImport が i 番目どうしを転送する。
//   それでは組み替えを書けないので、順序の対応する
//   modelIndices / pmxIndices で明示的に持つ。

namespace Poly_Ling.Data
{
    /// <summary>PMX ファイルの中身を一覧する。モデルは変えない。</summary>
    [PLCommand(Description = "PMX ファイルを読んで、取り込めるオブジェクトを一覧する。モデルは変えない。")]
    [PLResult("count",        PLResultKind.Integer,      Description = "取り込めるオブジェクトの数")]
    [PLResult("names",        PLResultKind.TextArray,    Description = "オブジェクトの名前。並びが pmxIndices の番号になる", Optional = true)]
    [PLResult("vertexCounts", PLResultKind.IntegerArray, Description = "PMX 側の頂点数。names と同じ並び", Optional = true)]
    public class QueryPmxSourceObjectsCommand : PanelCommand
    {
        [PLParam(Description = "読む PMX ファイルのパス", Required = true)]
        public string PmxPath { get; }

        [PLParam(Description = "読み込み時に座標へ掛ける倍率")]
        public float Scale { get; }

        [PLParam(Description = "読み込み時に X を反転する")]
        public bool FlipX { get; }

        [PLParam(Description = "読み込み時に Z を反転する")]
        public bool FlipZ { get; }

        public QueryPmxSourceObjectsCommand(
            int modelIndex, string pmxPath,
            float scale = 1f, bool flipX = false, bool flipZ = false)
            : base(modelIndex)
        {
            PmxPath = pmxPath ?? "";
            Scale   = scale;
            FlipX   = flipX;
            FlipZ   = flipZ;
        }
    }

    /// <summary>名前と頂点数でモデルと PMX を組む。モデルは変えない。</summary>
    [PLCommand(Description = "名前の完全一致を先に見て、外れたら頂点数の一致で、モデルの描画オブジェクトと PMX のオブジェクトを組む。組を返すだけでモデルは変えない。返った組を必ず見ること。")]
    [PLResult("pairs",               PLResultKind.Integer,      Description = "組めた数")]
    [PLResult("unmatched",           PLResultKind.Integer,      Description = "組めなかったモデル側オブジェクトの数。触らないのが正しい")]
    [PLResult("mismatchedCounts",    PLResultKind.Integer,      Description = "組んだが頂点数が食い違っている組の数。0 でないなら組み直すこと")]
    [PLResult("modelIndices",        PLResultKind.IntegerArray, Description = "組んだモデル側の描画オブジェクト索引。importPmxVertexAttributes へそのまま渡す", Optional = true)]
    [PLResult("pmxIndices",          PLResultKind.IntegerArray, Description = "組んだ PMX 側の並び番号。modelIndices と同じ並びが 1 組", Optional = true)]
    [PLResult("modelNames",          PLResultKind.TextArray,    Description = "モデル側の名前。modelIndices と同じ並び", Optional = true)]
    [PLResult("pmxNames",            PLResultKind.TextArray,    Description = "PMX 側の名前。modelIndices と同じ並び", Optional = true)]
    [PLResult("modelExpandedCounts", PLResultKind.IntegerArray, Description = "モデル側の展開後頂点数。modelIndices と同じ並び", Optional = true)]
    [PLResult("pmxVertexCounts",     PLResultKind.IntegerArray, Description = "PMX 側の頂点数。この 2 つが食い違う組は転送しても合わない", Optional = true)]
    public class MatchPmxSourceCommand : PanelCommand
    {
        [PLParam(Description = "読む PMX ファイルのパス", Required = true)]
        public string PmxPath { get; }

        [PLParam(Description = "読み込み時に座標へ掛ける倍率")]
        public float Scale { get; }

        [PLParam(Description = "読み込み時に X を反転する")]
        public bool FlipX { get; }

        [PLParam(Description = "読み込み時に Z を反転する")]
        public bool FlipZ { get; }

        public MatchPmxSourceCommand(
            int modelIndex, string pmxPath,
            float scale = 1f, bool flipX = false, bool flipZ = false)
            : base(modelIndex)
        {
            PmxPath = pmxPath ?? "";
            Scale   = scale;
            FlipX   = flipX;
            FlipZ   = flipZ;
        }
    }

    /// <summary>指定した組で、PMX の頂点属性をモデルへ差し替える。</summary>
    [PLCommand(Description = "指定した組で、PMX の頂点位置・UV・ボーンウェイトをモデルの描画オブジェクトへ差し替える。頂点数も索引も変わらない。組は modelIndices と pmxIndices の同じ位置どうし。")]
    [PLResult("pairs",       PLResultKind.Integer, Description = "転送した組の数")]
    [PLResult("transferred", PLResultKind.Integer, Description = "書き換えた頂点の数")]
    public class ImportPmxVertexAttributesCommand : PanelCommand
    {
        [PLParam(Description = "読む PMX ファイルのパス", Required = true)]
        public string PmxPath { get; }

        [PLParam(Description = "差し替え先のモデル側描画オブジェクト索引", Required = true)]
        public int[] ModelIndices { get; }

        [PLParam(Description = "差し替え元の PMX 側並び番号。ModelIndices と同じ長さにすること", Required = true)]
        public int[] PmxIndices { get; }

        [PLParam(Description = "読み込み時に座標へ掛ける倍率。組んだときと同じ値にすること")]
        public float Scale { get; }

        [PLParam(Description = "読み込み時に X を反転する。組んだときと同じ値にすること")]
        public bool FlipX { get; }

        [PLParam(Description = "読み込み時に Z を反転する。組んだときと同じ値にすること")]
        public bool FlipZ { get; }

        [PLParam(Description = "頂点位置を差し替える")]
        public bool ImportPosition { get; }

        [PLParam(Description = "UV も差し替える")]
        public bool ImportUV { get; }

        [PLParam(Description = "ボーンウェイトも差し替える")]
        public bool ImportBoneWeight { get; }

        public ImportPmxVertexAttributesCommand(
            int modelIndex, string pmxPath, int[] modelIndices, int[] pmxIndices,
            float scale = 1f, bool flipX = false, bool flipZ = false,
            bool importPosition = true, bool importUV = false, bool importBoneWeight = false)
            : base(modelIndex)
        {
            PmxPath          = pmxPath ?? "";
            ModelIndices     = modelIndices ?? new int[0];
            PmxIndices       = pmxIndices ?? new int[0];
            Scale            = scale;
            FlipX            = flipX;
            FlipZ            = flipZ;
            ImportPosition   = importPosition;
            ImportUV         = importUV;
            ImportBoneWeight = importBoneWeight;
        }
    }
}
