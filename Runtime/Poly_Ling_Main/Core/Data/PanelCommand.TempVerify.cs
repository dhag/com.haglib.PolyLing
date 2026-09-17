// PanelCommand.TempVerify.cs
//
// ================================================================
// 【臨時】このファイルは検証専用である。製品の機能ではない。
// ================================================================
//   入っているのは verifyBindMove / verifyBindRotate / verifyBindSculpt の 3 本。
//   いずれも姿勢まわり（バインド表示と現在ポーズ表示）の書き戻しが
//   表示に追従しているかを測るためだけに 2026-09-15 に作ったもの。
//
//   ・製品の機能から呼ばないこと。参考実装として真似しないこと。
//   ・どれも破壊的。ResetProjectCommand を通すので開いているモデルを全部捨て、
//     頂点を動かす。人の作業中に叩いてはならない。
//   ・受け口は PlayerCommandDispatcher.TempVerify.cs。そちらも臨時。
//   ・調査用の読み口（UnifiedBufferManager の Dbg* 一式、
//     PlayerViewportManager.GpuSelect の *ForVerify 系）も臨時で、
//     このファイルと一緒に消す対象。
//
//   残件は PolyLing_姿勢_残件.md の「臨時コマンドと調査用コードの後始末」。
//   検証が済んだらこのファイルごと削除する。
// ================================================================
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
    [PLResult("bindGpuMaxError",    PLResultKind.Number,      Description = "バインド表示：GPU が出した座標で測った同じ誤差")]
    [PLResult("poseGpuMaxError",    PLResultKind.Number,      Description = "現在ポーズ表示：GPU が出した座標で測った同じ誤差")]
    [PLResult("bindCpuGpuGap",      PLResultKind.Number,      Description = "バインド表示：CPU の VertexMatrix と GPU の座標の最大距離")]
    [PLResult("poseCpuGpuGap",      PLResultKind.Number,      Description = "現在ポーズ表示：同じ距離。0 でなければ CPU の規則が GPU と食い違っている")]
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

        [PLParam(Description = "検証する描画オブジェクトの masterIndex。-1 で非スキンドのものを自動で選ぶ", IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "検査する頂点の数。姿勢が動いた頂点の中から等間隔に拾う。既定 50")]
        public int SampleCount { get; }

        [PLParam(Description = "ポーズ層を入れる対象の masterIndex。-1 で検証対象そのものへ入れる", IsMeshRef = true)]
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
    /// 【旧コードとの比較】
    ///   誤差 0 だけでは足りない。往路と復路で同じ行列を使えば、その行列が何であっても
    ///   打ち消し合って通ることがあるため（実際、Z ポーズに Z 回転を掛けると
    ///   交換可能で誤差が消える）。そこで旧コード
    ///   （meshContext.LocalToWorld / WorldToLocal＝メッシュ 1 個の WorldMatrix）
    ///   ならどうなったかを同じ指標で計算し、それが正解と違うことをもって
    ///   試験が差を検出できていると示す。bindLegacyError / poseLegacyError。
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
    [PLResult("bindLegacyError",    PLResultKind.Number,      Description = "旧コード（メッシュ 1 個の LocalToWorld / WorldToLocal）ならバインド表示でこうなった値の誤差。0 だとバインド側は差を検出できていない")]
    [PLResult("poseLegacyError",    PLResultKind.Number,      Description = "同じく現在ポーズ表示での値。スキンドはこちらでしか差が出ない")]
    [PLResult("bindDiscriminating", PLResultKind.Flag,        Description = "バインド表示で旧コードとの差を検出できているか")]
    [PLResult("poseDiscriminating", PLResultKind.Flag,        Description = "現在ポーズ表示で旧コードとの差を検出できているか")]
    [PLResult("discriminating",     PLResultKind.Flag,        Description = "検査頂点が全て姿勢の動いたもので、かつ bindDiscriminating か poseDiscriminating の少なくとも一方が立つか。false なら試験が無意味")]
    [PLResult("pass",               PLResultKind.Flag,        Description = "両表示とも誤差が tolerance 以内で、かつ試験が差を検出できる条件を満たす")]
    public class VerifyBindRotateCommand : PanelCommand
    {
        [PLParam(Description = "読み込む PMX のパス。MqoPath が空のときに使う")]
        public string PmxPath { get; }

        [PLParam(Description = "読み込む MQO のパス。指定するとこちらを使う")]
        public string MqoPath { get; }

        [PLParam(Description = "適用するローカル原点 CSV のパス。空なら適用しない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "検証する描画オブジェクトの masterIndex。-1 で非スキンドのものを自動で選ぶ", IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "検査する頂点の数。姿勢が動いた頂点の中から等間隔に拾う。既定 50")]
        public int SampleCount { get; }

        [PLParam(Description = "ポーズ層を入れる対象の masterIndex。-1 で検証対象そのものへ入れる", IsMeshRef = true)]
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

    /// <summary>
    /// 【臨時・検証用・簡易】スカルプトのブラシが、画面に見えている位置に当たるかを測る。
    ///
    /// 【何を見るか】
    ///   ブラシ中心を「検査対象頂点の表示ワールド座標」に置き、Inflate を 1 打ちする。
    ///   その頂点が動けば、狙った場所に当たっている。
    ///   旧コード（中心をメッシュ 1 個の WorldToLocal でローカル化し、ローカル距離で
    ///   拾う）なら何頂点が入ったかも併せて数え、新旧で違うことをもって
    ///   試験が差を検出できていると示す。
    ///
    /// 【簡易である点】
    ///   変形量は見ない。当たったかどうかと、拾われた頂点数だけを見る。
    ///   変形はまだローカル座標のまま（A-1 の範囲）なので、量を測る意味が薄い。
    ///
    /// 【破壊的】プロジェクトを捨てて読み直し、頂点を動かす。
    /// </summary>
    [PLCommand(Description = "【臨時・簡易】スカルプトのブラシが画面に見えている位置に当たるかを測る。開いているモデルは全部捨てる。")]
    [PLResult("targetIndex",      PLResultKind.Integer, Description = "検証に使った描画オブジェクトの masterIndex")]
    [PLResult("targetName",       PLResultKind.Text,    Description = "その名前")]
    [PLResult("anchorVertex",     PLResultKind.Integer, Description = "ブラシ中心に置いた頂点の索引")]
    [PLResult("matrixMaxDiff",    PLResultKind.Number,  Description = "現在ポーズ表示とバインド表示の頂点行列の最大要素差")]
    [PLResult("bindHit",          PLResultKind.Integer, Description = "バインド表示で実際に動いた頂点数")]
    [PLResult("bindAnchorMoved",  PLResultKind.Flag,    Description = "バインド表示で中心に置いた頂点が動いたか")]
    [PLResult("bindLegacyHit",    PLResultKind.Integer, Description = "旧コードならバインド表示で拾われた頂点数")]
    [PLResult("poseHit",          PLResultKind.Integer, Description = "現在ポーズ表示で実際に動いた頂点数")]
    [PLResult("poseAnchorMoved",  PLResultKind.Flag,    Description = "現在ポーズ表示で中心に置いた頂点が動いたか")]
    [PLResult("poseLegacyHit",    PLResultKind.Integer, Description = "旧コードなら現在ポーズ表示で拾われた頂点数")]
    [PLResult("bindCpuGpuGap",    PLResultKind.Number,  Description = "バインド表示で、中心頂点の CPU VertexMatrix による位置と GPU の値の距離")]
    [PLResult("poseCpuGpuGap",    PLResultKind.Number,  Description = "現在ポーズ表示での同じ距離。半径より大きいと中心が範囲から外れる")]
    [PLResult("bindGapCount",     PLResultKind.Integer, Description = "バインド表示で CPU と GPU が tolerance 超で食い違う頂点の数（全頂点走査）")]
    [PLResult("bindGapMax",       PLResultKind.Number,  Description = "その最大距離")]
    [PLResult("bindGapWorst",     PLResultKind.Integer, Description = "最大距離だった頂点の索引")]
    [PLResult("poseGapCount",     PLResultKind.Integer, Description = "現在ポーズ表示で食い違う頂点の数（全頂点走査）")]
    [PLResult("poseGapMax",       PLResultKind.Number,  Description = "その最大距離")]
    [PLResult("poseGapWorst",     PLResultKind.Integer, Description = "最大距離だった頂点の索引")]
    [PLResult("discriminating",   PLResultKind.Flag,    Description = "新旧で拾う頂点数が違うか。false ならこの条件では差を検出できない")]
    [PLResult("pass",             PLResultKind.Flag,    Description = "両表示とも中心の頂点が動き、かつ試験が差を検出できている")]
    public class VerifyBindSculptCommand : PanelCommand
    {
        [PLParam(Description = "読み込む PMX のパス。MqoPath が空のときに使う")]
        public string PmxPath { get; }

        [PLParam(Description = "読み込む MQO のパス。指定するとこちらを使う")]
        public string MqoPath { get; }

        [PLParam(Description = "適用するローカル原点 CSV のパス。空なら適用しない")]
        public string OriginCsvPath { get; }

        [PLParam(Description = "検証する描画オブジェクトの masterIndex。-1 で非スキンドのものを自動で選ぶ", IsMeshRef = true)]
        public int MasterIndex { get; }

        [PLParam(Description = "ポーズ層を入れる対象の masterIndex。-1 で検証対象そのものへ入れる", IsMeshRef = true)]
        public int PoseBoneMasterIndex { get; }

        [PLParam(Description = "ポーズ層へ入れる Z 回転[度]")]
        public float PoseRotationZ { get; }

        [PLParam(Description = "ブラシ半径", Required = true)]
        public float BrushRadius { get; }

        [PLParam(Description = "ブラシの強さ。既定 0.5")]
        public float Strength { get; }

        [PLParam(Description = "行列が違うとみなす閾値。既定 0.0005")]
        public float Tolerance { get; }

        public VerifyBindSculptCommand(
            int modelIndex,
            float brushRadius,
            string pmxPath = "",
            string mqoPath = "",
            string originCsvPath = "",
            int masterIndex = -1,
            int poseBoneMasterIndex = -1,
            float poseRotationZ = 0f,
            float strength = 0.5f,
            float tolerance = 0.0005f)
            : base(modelIndex)
        {
            PmxPath             = pmxPath ?? "";
            MqoPath             = mqoPath ?? "";
            OriginCsvPath       = originCsvPath ?? "";
            MasterIndex         = masterIndex;
            PoseBoneMasterIndex = poseBoneMasterIndex;
            PoseRotationZ       = poseRotationZ;
            BrushRadius         = brushRadius <= 0f ? 0.1f : brushRadius;
            Strength            = strength <= 0f ? 0.5f : strength;
            Tolerance           = tolerance <= 0f ? 0.0005f : tolerance;
        }
    }
}
