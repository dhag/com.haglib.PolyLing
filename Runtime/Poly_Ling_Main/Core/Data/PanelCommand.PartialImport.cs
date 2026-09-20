// PanelCommand.PartialImport.cs
// MQO を元にした部分差し替え（読み込み・対応付け・転送）の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【3 本に分ける理由】
//   対応付けは展開頂点数の一致だけで行う（MQOPartialMatchHelper.cs:297-317）。
//   同じ頂点数のオブジェクトが複数あると別物へ入れ替わるので、
//   自動対応をそのまま流すわけにいかない。組を人が見て直せるよう、
//   「一覧を見る」「自動で組む」「組を指定して転送する」に分ける。
//
// 【状態を持ち越さない】
//   MQOPartialMatchHelper は MQO を読み込んで一覧を持つが、その一覧は
//   ファイルと visibleOnly が同じなら何度読んでも同じ並びになる。
//   よって組は「モデル側の描画オブジェクト索引」と「MQO 側の並び番号」の
//   2 本の整数列で表せる。実行の途中状態を置く器は要らない。
//
// 【組は 2 本の配列で表す】
//   AutoMatch は組を記録せず、両側に Selected を立てるだけで、
//   組は「選ばれた者どうしを頭から順に突き合わせる」という暗黙の対応になる
//   （ExecuteVertexPositionImport が i 番目どうしを転送する）。
//   それでは A→Y, B→X のような組み替えを書けないので、
//   ここでは順序の対応する modelIndices / mqoIndices で明示的に持つ。

namespace Poly_Ling.Data
{
    /// <summary>MQO ファイルの中身を一覧する。モデルは変えない。</summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "MQO ファイルを読んで、取り込める（可視・頂点あり）オブジェクトを一覧する。モデルは変えない。")]
    [PLResult("count",                PLResultKind.Integer,      Description = "取り込めるオブジェクトの数")]
    [PLResult("names",                PLResultKind.TextArray,    Description = "オブジェクトの名前。並びが mqoIndices の番号になる", Optional = true)]
    [PLResult("expandedVertexCounts", PLResultKind.IntegerArray, Description = "展開後の頂点数。ミラー指定は 2 倍で数える。names と同じ並び", Optional = true)]
    [PLResult("mirrored",             PLResultKind.IntegerArray, Description = "ミラー指定か。1 = ミラー。names と同じ並び", Optional = true)]
    public class QueryMqoSourceObjectsCommand : PanelCommand
    {
        [PLParam(Description = "読む MQO ファイルのパス", Required = true)]
        public string MqoPath { get; }

        [PLParam(Description = "非表示オブジェクトを外す")]
        public bool VisibleOnly { get; }

        public QueryMqoSourceObjectsCommand(int modelIndex, string mqoPath, bool visibleOnly = true)
            : base(modelIndex)
        {
            MqoPath     = mqoPath ?? "";
            VisibleOnly = visibleOnly;
        }
    }

    /// <summary>展開頂点数の一致でモデルと MQO を組む。モデルは変えない。</summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "展開頂点数の一致で、モデルの描画オブジェクトと MQO のオブジェクトを組む。組を返すだけでモデルは変えない。同じ頂点数のものが複数あると別物と組むので、返った組を必ず見ること。")]
    [PLResult("pairs",         PLResultKind.Integer,      Description = "組めた数")]
    [PLResult("unmatched",     PLResultKind.Integer,      Description = "組めなかったモデル側オブジェクトの数。触らないのが正しい")]
    [PLResult("modelIndices",  PLResultKind.IntegerArray, Description = "組んだモデル側の描画オブジェクト索引。importMqoVertexPositions へそのまま渡す", Optional = true)]
    [PLResult("mqoIndices",    PLResultKind.IntegerArray, Description = "組んだ MQO 側の並び番号。modelIndices と同じ並びが 1 組", Optional = true)]
    [PLResult("modelNames",    PLResultKind.TextArray,    Description = "モデル側の名前。modelIndices と同じ並び", Optional = true)]
    [PLResult("mqoNames",      PLResultKind.TextArray,    Description = "MQO 側の名前。modelIndices と同じ並び", Optional = true)]
    [PLResult("vertexCounts",  PLResultKind.IntegerArray, Description = "組の展開頂点数。modelIndices と同じ並び", Optional = true)]
    public class MatchMqoSourceByVertexCountCommand : PanelCommand
    {
        [PLParam(Description = "読む MQO ファイルのパス", Required = true)]
        public string MqoPath { get; }

        [PLParam(Description = "非表示オブジェクトを外す")]
        public bool VisibleOnly { get; }

        [PLParam(Description = "名前が + で終わるミラー指定のオブジェクトを外す")]
        public bool SkipNamedMirror { get; }

        [PLParam(Description = "焼き込み済みミラーを対にして 1 件として数える")]
        public bool PairMirrors { get; }

        public MatchMqoSourceByVertexCountCommand(
            int modelIndex, string mqoPath,
            bool visibleOnly = true, bool skipNamedMirror = false, bool pairMirrors = true)
            : base(modelIndex)
        {
            MqoPath         = mqoPath ?? "";
            VisibleOnly     = visibleOnly;
            SkipNamedMirror = skipNamedMirror;
            PairMirrors     = pairMirrors;
        }
    }

    /// <summary>指定した組で、MQO の頂点位置（と UV）をモデルへ差し替える。</summary>
    [PLCommand(Writes = PLWriteScope.ModelWide, Description = "指定した組で、MQO の頂点位置（と UV）をモデルの描画オブジェクトへ差し替える。頂点数も索引も変わらない。組は modelIndices と mqoIndices の同じ位置どうし。")]
    [PLResult("pairs",       PLResultKind.Integer, Description = "転送した組の数")]
    [PLResult("transferred", PLResultKind.Integer, Description = "書き換えた頂点の数")]
    public class ImportMqoVertexPositionsCommand : PanelCommand
    {
        [PLParam(Description = "読む MQO ファイルのパス", Required = true)]
        public string MqoPath { get; }

        [PLParam(Description = "非表示オブジェクトを外す。matchMqoSourceByVertexCount と同じ値にすること")]
        public bool VisibleOnly { get; }

        [PLParam(Description = "名前が + で終わるミラー指定のオブジェクトを外す。組んだときと同じ値にすること")]
        public bool SkipNamedMirror { get; }

        [PLParam(Description = "焼き込み済みミラーを対にする。組んだときと同じ値にすること")]
        public bool PairMirrors { get; }

        [PLParam(Description = "差し替え先のモデル側描画オブジェクト索引", Required = true)]
        public int[] ModelIndices { get; }

        [PLParam(Description = "差し替え元の MQO 側並び番号。ModelIndices と同じ長さにすること", Required = true)]
        public int[] MqoIndices { get; }

        [PLParam(Description = "MQO 座標に掛ける倍率")]
        public float ImportScale { get; }

        [PLParam(Description = "X を反転する")]
        public bool FlipX { get; }

        [PLParam(Description = "Z を反転する")]
        public bool FlipZ { get; }

        [PLParam(Description = "UV も差し替える")]
        public bool ImportUV { get; }

        [PLParam(Description = "UV の V を反転する。ImportUV が false のときは使わない")]
        public bool FlipUV_V { get; }

        public ImportMqoVertexPositionsCommand(
            int modelIndex, string mqoPath, int[] modelIndices, int[] mqoIndices,
            bool visibleOnly = true, bool skipNamedMirror = false, bool pairMirrors = true,
            float importScale = 1f, bool flipX = false, bool flipZ = false,
            bool importUV = false, bool flipUV_V = false)
            : base(modelIndex)
        {
            MqoPath         = mqoPath ?? "";
            ModelIndices    = modelIndices ?? new int[0];
            MqoIndices      = mqoIndices ?? new int[0];
            VisibleOnly     = visibleOnly;
            SkipNamedMirror = skipNamedMirror;
            PairMirrors     = pairMirrors;
            ImportScale     = importScale;
            FlipX           = flipX;
            FlipZ           = flipZ;
            ImportUV        = importUV;
            FlipUV_V        = flipUV_V;
        }
    }
}
