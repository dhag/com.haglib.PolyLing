// PanelCommand.TempVerify.cs
// 【臨時】姿勢まわりの検証専用コマンド。検証が済んだらこのファイルごと削除する。
// Runtime/Poly_Ling_Main/Core/Data/ に配置

using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 【臨時】頂点移動の書き戻しが表示に追従しているかを、
    /// リセット → 読み込み → ポーズ付与 → 両表示モードでの移動 → 表示ワールド位置の照合まで
    /// 1 回の呼び出しで通す検証コマンド。
    ///
    /// 【測るもの】
    ///   表示ワールド位置 = VertexMatrix(頂点, showBindPose) × 格納値。
    ///   格納値（ローカル）は画面から測れないので指標にしない。
    ///
    /// 【検査頂点の選び方】
    ///   現在ポーズ表示とバインド表示で頂点行列が同じ頂点は、書き戻しが何であっても
    ///   誤差 0 になるため合否の材料にならない。ポーズ層を入れたあとに全頂点を走査し、
    ///   2 つの行列が tolerance を超えて違う頂点だけを候補にして、そこから等間隔に拾う。
    ///   1 つも無ければ失敗させる（ポーズの対象と検証対象が噛み合っていない）。
    ///   拾った頂点が全て候補であることは posedVertices == testedVertices で示す。
    ///
    /// 【なぜ旧コードの値を並べて返すか】
    ///   VertexMatrix × VertexMatrix⁻¹ × D = D は行列が何であっても成り立つので、
    ///   誤差 0 だけでは「正しい行列を使っている」ことの証拠にならない。
    ///   そこで旧コード（オブジェクト単位の WorldMatrixInverse 直）ならこうなった、という値を
    ///   両表示モードぶん計算し、それが D と違うことをもって試験が差を検出できていると示す。
    ///
    ///   バインド表示（legacyBindError）
    ///     非スキンドで差が出る。正しくは BindWorldMatrix、旧コードは WorldMatrix を使っていた。
    ///     スキンド頂点はバインド表示で単位行列なので、旧コードの値も D と一致して差が出ない。
    ///   現在ポーズ表示（legacyPoseError）
    ///     スキンドで差が出る。正しくは頂点単位の Σ(w·SkinningMatrix)、
    ///     旧コードはメッシュ 1 個の WorldMatrix を使っていた。
    ///
    ///   したがってスキンドの合否は legacyPoseError 側でしか判定できない。
    ///
    /// 【この試験の限界】
    ///   見ているのは MeshObject.Vertices の格納値と MeshContext.VertexMatrix だけで、
    ///   GPU が実際に描いた位置（UnifiedBufferManager.GetWorldPositions）とは突き合わせていない。
    ///   CPU 側の規則が一貫していることまでしか言えない。
    ///
    /// 【破壊的】
    ///   ResetProjectCommand を通すので、開いているモデルは全部捨てる。
    /// </summary>
    [PLCommand(Description = "【臨時】リセットから読込・ポーズ付与・両表示モードでの頂点移動・表示ワールド位置の照合までを 1 回で通す検証コマンド。開いているモデルは全部捨てる。")]
    [PLResult("targetIndex",        PLResultKind.Integer,     Description = "実際に検証した描画オブジェクトの masterIndex")]
    [PLResult("targetName",         PLResultKind.Text,        Description = "その名前")]
    [PLResult("loadedBones",        PLResultKind.Integer,     Description = "読み込み後のボーン数")]
    [PLResult("loadedDrawables",    PLResultKind.Integer,     Description = "読み込み後の描画オブジェクト数")]
    [PLResult("testedVertices",     PLResultKind.Integer,     Description = "検査した頂点の数")]
    [PLResult("posedVertices",      PLResultKind.Integer,     Description = "そのうち現在ポーズ表示とバインド表示で頂点行列が違うものの数。testedVertices と一致しないと合否の材料にならない")]
    [PLResult("matrixMaxDiff",      PLResultKind.Number,      Description = "対象メッシュ全体での、現在ポーズ表示とバインド表示の頂点行列の最大要素差")]
    [PLResult("weightedVertices",   PLResultKind.Integer,     Description = "そのうちボーンウェイトを持つ頂点の数。0 なら非スキンド")]
    [PLResult("worldMatrixT",       PLResultKind.NumberArray, Description = "対象の WorldMatrix の平行移動成分")]
    [PLResult("bindWorldMatrixT",   PLResultKind.NumberArray, Description = "対象の BindWorldMatrix の平行移動成分")]
    [PLResult("matricesDiffer",     PLResultKind.Flag,        Description = "WorldMatrix と BindWorldMatrix が違うか。false ならこの試験は差を検出できない")]
    [PLResult("bindWorldDelta",     PLResultKind.NumberArray, Description = "バインド表示：表示ワールド差（代表 1 点）")]
    [PLResult("bindMaxError",       PLResultKind.Number,      Description = "バインド表示：表示ワールド差と与えたデルタの最大絶対誤差")]
    [PLResult("poseWorldDelta",     PLResultKind.NumberArray, Description = "現在ポーズ表示：表示ワールド差（代表 1 点）")]
    [PLResult("poseMaxError",       PLResultKind.Number,      Description = "現在ポーズ表示：表示ワールド差と与えたデルタの最大絶対誤差")]
    [PLResult("legacyBindWorldDelta", PLResultKind.NumberArray, Description = "旧コード（WorldMatrixInverse 直）ならバインド表示でこうなった値")]
    [PLResult("legacyBindError",    PLResultKind.Number,      Description = "バインド表示での旧コードの値と与えたデルタの最大絶対誤差。0 だとバインド側は差を検出できていない")]
    [PLResult("legacyPoseWorldDelta", PLResultKind.NumberArray, Description = "旧コード（WorldMatrixInverse 直）なら現在ポーズ表示でこうなった値")]
    [PLResult("legacyPoseError",    PLResultKind.Number,      Description = "現在ポーズ表示での旧コードの値と与えたデルタの最大絶対誤差。スキンド頂点はこちらでしか差が出ない")]
    [PLResult("bindDiscriminating", PLResultKind.Flag,        Description = "バインド表示で旧コードとの差を検出できているか")]
    [PLResult("poseDiscriminating", PLResultKind.Flag,        Description = "現在ポーズ表示で旧コードとの差を検出できているか")]
    [PLResult("discriminating",     PLResultKind.Flag,        Description = "検査頂点が全て姿勢の動いたもので、かつ bindDiscriminating か poseDiscriminating の少なくとも一方が立つか。false なら試験が無意味")]
    [PLResult("pass",               PLResultKind.Flag,        Description = "両モードとも誤差が tolerance 以内で、かつ試験が差を検出できている")]
    public class VerifyBindMoveCommand : PanelCommand
    {
        [PLParam(Description = "読み込む PMX のパス。MqoPath が空のときだけ使う")]
        public string PmxPath { get; }

        [PLParam(Description = "読み込む MQO のパス。指定するとこちらを使う")]
        public string MqoPath { get; }

        [PLParam(Description = "適用するローカル原点 CSV のパス。空なら適用しない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "検証する描画オブジェクトの masterIndex。-1 で非スキンドのものを自動で選ぶ")]
        public int MasterIndex { get; }

        [PLParam(Description = "検査する頂点の数。姿勢が動いた頂点の中から等間隔に拾う。既定 50")]
        public int SampleCount { get; }

        [PLParam(Description = "ポーズ層を入れる対象の masterIndex。-1 で検証対象そのものへ入れる")]
        public int PoseBoneMasterIndex { get; }

        [PLParam(Description = "ポーズ層へ入れる Z 回転[度]")]
        public float PoseRotationZ { get; }

        [PLParam(Description = "与えるワールド移動量", Required = true)]
        public Vector3 Delta { get; }

        [PLParam(Description = "合格とみなす最大絶対誤差。既定 0.0005")]
        public float Tolerance { get; }

        public VerifyBindMoveCommand(
            int modelIndex,
            Vector3 delta,
            string pmxPath = "",
            string mqoPath = "",
            string originCsvPath = "",
            int masterIndex = -1,
            int sampleCount = 50,
            int poseBoneMasterIndex = -1,
            float poseRotationZ = 0f,
            float tolerance = 0.0005f)
            : base(modelIndex)
        {
            PmxPath             = pmxPath ?? "";
            MqoPath             = mqoPath ?? "";
            OriginCsvPath       = originCsvPath ?? "";
            MasterIndex         = masterIndex;
            SampleCount         = sampleCount <= 0 ? 50 : sampleCount;
            Delta               = delta;
            PoseBoneMasterIndex = poseBoneMasterIndex;
            PoseRotationZ       = poseRotationZ;
            Tolerance           = tolerance <= 0f ? 0.0005f : tolerance;
        }
    }

    /// <summary>
    /// 【臨時・検証用】バインド表示と現在ポーズ表示で、選択頂点の「回転」が
    /// 画面に見えているとおりに効くかを測る。VerifyBindMove の回転版。
    ///
    /// 【なぜ要るか】
    ///   RotateTool は meshContext.LocalToWorld / WorldToLocal（RotateTool.cs:389, 392）を
    ///   使う。これは showBindPose を取らない現在ポーズ固定の行列なので、
    ///   バインド表示中は表示に使った行列と食い違う疑いがある（規約 10.1）。
    ///
    /// 【測り方】
    ///   ピボットはツールが選択の重心から決めるため、こちらから指定できない。
    ///   そこでピボットに依らない形で測る。表示ワールド位置の重心を before / after で
    ///   それぞれ取り、重心からの相対ベクトルが R（与えた回転）で写るかを見る。
    ///     err = max| (after_i - centroidAfter) - R * (before_i - centroidBefore) |
    ///   表示どおりに回っていれば 0。行列が食い違っていれば残る。
    ///
    /// 【検査頂点の選び方】VerifyBindMove と同じ。姿勢が動いた頂点からだけ拾う。
    ///
    /// 【この試験の限界】VerifyBindMove と同じ。GPU が実際に描いた位置とは
    ///   突き合わせていない。必ずキャプチャも撮ること。
    ///
    /// 【破壊的】プロジェクトを捨てて読み直し、頂点を動かす。検証専用。
    /// </summary>
    [PLCommand(Description = "【臨時】バインド表示と現在ポーズ表示で、選択頂点の回転が表示どおりに効くかを測る。")]
    [PLResult("targetIndex",        PLResultKind.Integer,     Description = "検証に使った描画オブジェクトの masterIndex")]
    [PLResult("targetName",         PLResultKind.Text,        Description = "その名前")]
    [PLResult("testedVertices",     PLResultKind.Integer,     Description = "検査した頂点の数")]
    [PLResult("posedVertices",      PLResultKind.Integer,     Description = "そのうち現在ポーズ表示とバインド表示で頂点行列が違うものの数")]
    [PLResult("matrixMaxDiff",      PLResultKind.Number,      Description = "対象メッシュ全体での、2 つの表示の頂点行列の最大要素差")]
    [PLResult("weightedVertices",   PLResultKind.Integer,     Description = "そのうちボーンウェイトを持つ頂点の数。0 なら非スキンド")]
    [PLResult("bindMaxError",       PLResultKind.Number,      Description = "バインド表示での、表示どおりの回転からの最大絶対誤差")]
    [PLResult("poseMaxError",       PLResultKind.Number,      Description = "現在ポーズ表示での同じ誤差")]
    [PLResult("discriminating",     PLResultKind.Flag,        Description = "検査頂点が全て姿勢の動いたもので、かつ 2 つの表示の行列が実際に違うか")]
    [PLResult("pass",               PLResultKind.Flag,        Description = "両表示とも誤差が tolerance 以内で、かつ試験が差を検出できる条件を満たす")]
    public class VerifyBindRotateCommand : PanelCommand
    {
        [PLParam(Description = "読み込む PMX のパス。MqoPath が空のときに使う")]
        public string PmxPath { get; }

        [PLParam(Description = "読み込む MQO のパス。指定するとこちらを使う")]
        public string MqoPath { get; }

        [PLParam(Description = "適用するローカル原点 CSV のパス。空なら適用しない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "検証する描画オブジェクトの masterIndex。-1 で非スキンドのものを自動で選ぶ")]
        public int MasterIndex { get; }

        [PLParam(Description = "検査する頂点の数。姿勢が動いた頂点の中から等間隔に拾う。既定 50")]
        public int SampleCount { get; }

        [PLParam(Description = "ポーズ層を入れる対象の masterIndex。-1 で検証対象そのものへ入れる")]
        public int PoseBoneMasterIndex { get; }

        [PLParam(Description = "ポーズ層へ入れる Z 回転[度]")]
        public float PoseRotationZ { get; }

        [PLParam(Description = "試験で与える回転（オイラー角[度]・ワールド）", Required = true)]
        public Vector3 RotateEuler { get; }

        [PLParam(Description = "合格とみなす最大絶対誤差。既定 0.0005")]
        public float Tolerance { get; }

        public VerifyBindRotateCommand(
            int modelIndex,
            Vector3 rotateEuler,
            string pmxPath = "",
            string mqoPath = "",
            string originCsvPath = "",
            int masterIndex = -1,
            int sampleCount = 50,
            int poseBoneMasterIndex = -1,
            float poseRotationZ = 0f,
            float tolerance = 0.0005f)
            : base(modelIndex)
        {
            PmxPath             = pmxPath ?? "";
            MqoPath             = mqoPath ?? "";
            OriginCsvPath       = originCsvPath ?? "";
            MasterIndex         = masterIndex;
            SampleCount         = sampleCount <= 0 ? 50 : sampleCount;
            RotateEuler         = rotateEuler;
            PoseBoneMasterIndex = poseBoneMasterIndex;
            PoseRotationZ       = poseRotationZ;
            Tolerance           = tolerance <= 0f ? 0.0005f : tolerance;
        }
    }
}
