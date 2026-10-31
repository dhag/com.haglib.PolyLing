// PanelCommand.MotionEdit.cs
// モーションの試し再生（ツールの窓口 motionClip）のクリップの編集の構造 edit（version 5）を照会・編集するコマンド。
// PolyLing_モーション編集構造計画.md 段 4。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【照会】edit の無いクリップは、写しに MotionEditOps.EnsureEdit を当てて答える（窓口の状態は変えない）。
//   写しの id は、最初の書き込みで作られる id と同じ。結果の editHeld が窓口が edit を持つかを表す。
//   入れ子の配列は返せないので、並行配列（個数の列＋連結した値の列）で返す。
// 【書き込み】窓口の編集の口（MotionClipHandler.Edit.cs）を呼ぶだけ。取り消しは口が積む記録を使う。
//   口が失敗したら Fail。time を省く（-1）と直前に当てた時刻のフレームを当て直す。
// 【id】edit の今の id の数字の最大値＋1 で振る（MotionEditOps.Ids）。最大の id のものを除いた直後に作ると、
//   同じ id が再び出る。

namespace Poly_Ling.Data
{
    // ================================================================
    // 照会
    // ================================================================

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, NoUi = true, Description = "モーションの試し再生（ツールの窓口 motionClip）のクリップの編集の構造 edit のトラックとクリップの一覧を返す。edit の無いクリップは、最初の書き込みで作られる形（トラックごとにクリップ 1 本）を返し、クリップは変えない。id は edit の今の最大値＋1 で振るので、作る・除くコマンドの後は引き直す。")]
    [PLResult("editHeld",        PLResultKind.Flag,         Description = "窓口のクリップが edit を持つか。false のときの一覧は最初の書き込みで作られる形")]
    [PLResult("frameRate",       PLResultKind.Number,       Description = "フレームレート")]
    [PLResult("trackIds",        PLResultKind.TextArray,    Description = "トラックの id（edit の順）", Optional = true)]
    [PLResult("trackRoles",      PLResultKind.TextArray,    Description = "トラックの role（bones / bakedBones / body / lookAt / expressions / muscles）", Optional = true)]
    [PLResult("trackTargetKinds",PLResultKind.TextArray,    Description = "トラックの targetKind（無ければ空）", Optional = true)]
    [PLResult("trackTargets",    PLResultKind.TextArray,    Description = "当たる平らなトラックの id（スカラーは name）", Optional = true)]
    [PLResult("trackNames",      PLResultKind.TextArray,    Description = "トラックの表示名", Optional = true)]
    [PLResult("trackUnames",     PLResultKind.TextArray,    Description = "トラックの正規名（無ければ空）", Optional = true)]
    [PLResult("trackCategories", PLResultKind.TextArray,    Description = "表情の category（無ければ空）", Optional = true)]
    [PLResult("trackProviders",  PLResultKind.TextArray,    Description = "表情の provider（無ければ空）", Optional = true)]
    [PLResult("trackPreWrap",    PLResultKind.TextArray,    Description = "トラックの preWrapMode（未指定は空）", Optional = true)]
    [PLResult("trackPostWrap",   PLResultKind.TextArray,    Description = "トラックの postWrapMode（未指定は空）", Optional = true)]
    [PLResult("trackDerived",    PLResultKind.IntegerArray, Description = "1 なら別のトラックから作り直す派生のトラック（書き込みのコマンドはエラーになる）", Optional = true)]
    [PLResult("trackClipCounts", PLResultKind.IntegerArray, Description = "トラックごとのクリップの数（clip の列はトラックの順に連結）", Optional = true)]
    [PLResult("clipIds",         PLResultKind.TextArray,    Description = "クリップの id", Optional = true)]
    [PLResult("clipNames",       PLResultKind.TextArray,    Description = "クリップ名", Optional = true)]
    [PLResult("clipStarts",      PLResultKind.NumberArray,  Description = "クリップの開始（フレーム）", Optional = true)]
    [PLResult("clipLens",        PLResultKind.NumberArray,  Description = "クリップの長さ（フレーム）", Optional = true)]
    [PLResult("clipLoops",       PLResultKind.IntegerArray, Description = "1 ならループ", Optional = true)]
    [PLResult("clipWeights",     PLResultKind.NumberArray,  Description = "クリップの重み", Optional = true)]
    [PLResult("clipBundleIds",   PLResultKind.TextArray,    Description = "クリップが直接属するバンドルの id（無ければ空）", Optional = true)]
    [PLResult("clipRotKeys",     PLResultKind.IntegerArray, Description = "rot のキーの数", Optional = true)]
    [PLResult("clipPosKeys",     PLResultKind.IntegerArray, Description = "pos のキーの数", Optional = true)]
    [PLResult("clipSclKeys",     PLResultKind.IntegerArray, Description = "scl のキーの数", Optional = true)]
    [PLResult("clipWKeys",       PLResultKind.IntegerArray, Description = "w のキーの数", Optional = true)]
    [PLResult("uncoveredFlat",   PLResultKind.TextArray,    Description = "edit に入っていない平らなトラック（role:target。派生のトラックと path を持つ表情）", Optional = true)]
    public class QueryMotionEditTracksCommand : PanelCommand
    {
        public QueryMotionEditTracksCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, NoUi = true, Description = "モーションの試し再生のクリップの edit の、クリップ 1 本のキー（値・接線）を返す。edit の無いクリップは最初の書き込みで作られる形で答え、クリップは変えない。キーはチャンネル（rot / pos / scl / w）ごとに保存の並び。値と接線は成分ごとに連結する。")]
    [PLResult("editHeld",         PLResultKind.Flag,         Description = "窓口のクリップが edit を持つか")]
    [PLResult("keyChannels",      PLResultKind.TextArray,    Description = "キーのチャンネル", Optional = true)]
    [PLResult("keyFrames",        PLResultKind.NumberArray,  Description = "クリップの先頭からのフレーム f", Optional = true)]
    [PLResult("keySeconds",       PLResultKind.NumberArray,  Description = "評価に使うキーの秒（t0 があり同じフレームなら t0、無ければ round(start + f) / frameRate）", Optional = true)]
    [PLResult("keyT0",            PLResultKind.NumberArray,  Description = "読み込んだときの元の秒 t0（無ければ 0）", Optional = true)]
    [PLResult("keyHasT0",         PLResultKind.IntegerArray, Description = "1 なら t0 を持つ", Optional = true)]
    [PLResult("keyValueCounts",   PLResultKind.IntegerArray, Description = "キーごとの成分の数（rot 4 / pos・scl 3 / w 1）", Optional = true)]
    [PLResult("keyValues",        PLResultKind.NumberArray,  Description = "値をキーの順に連結（回転は x,y,z,w）", Optional = true)]
    [PLResult("tanFlags",         PLResultKind.IntegerArray, Description = "成分ごとの接線の有無（keyValues と同じ並び）。1 接線あり、2 inTangent、4 outTangent、8 inWeight、16 outWeight を持つ", Optional = true)]
    [PLResult("tanIn",            PLResultKind.NumberArray,  Description = "inTangent（値/秒。無ければ 0）", Optional = true)]
    [PLResult("tanOut",           PLResultKind.NumberArray,  Description = "outTangent（値/秒。無ければ 0）", Optional = true)]
    [PLResult("tanInWeight",      PLResultKind.NumberArray,  Description = "inWeight（無ければ 0）", Optional = true)]
    [PLResult("tanOutWeight",     PLResultKind.NumberArray,  Description = "outWeight（無ければ 0）", Optional = true)]
    [PLResult("tanLeftModes",     PLResultKind.TextArray,    Description = "leftTangentMode（無ければ空）", Optional = true)]
    [PLResult("tanRightModes",    PLResultKind.TextArray,    Description = "rightTangentMode（無ければ空）", Optional = true)]
    [PLResult("tanWeightedModes", PLResultKind.TextArray,    Description = "weightedMode（無ければ空）", Optional = true)]
    public class QueryMotionEditKeysCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id（queryMotionEditTracks の clipIds）", Required = true)]
        public string ClipId { get; }

        [PLParam(Description = "チャンネル（rot / pos / scl / w）。空にすると全チャンネル")]
        public string Channel { get; }

        public QueryMotionEditKeysCommand(int modelIndex, string clipId, string channel = "")
            : base(modelIndex)
        {
            ClipId  = clipId ?? "";
            Channel = channel ?? "";
        }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, NoUi = true, Description = "モーションの試し再生のクリップの edit のバンドルとレイヤーの一覧（中身・親子・レイヤーの設定・実効の重みとミュート・重ねる順）を返す。edit の無いクリップはバンドルが無い。クリップは変えない。")]
    [PLResult("editHeld",          PLResultKind.Flag,         Description = "窓口のクリップが edit を持つか")]
    [PLResult("bundleIds",         PLResultKind.TextArray,    Description = "バンドルの id（edit の順）", Optional = true)]
    [PLResult("bundleNames",       PLResultKind.TextArray,    Description = "バンドル名", Optional = true)]
    [PLResult("bundleParentIds",   PLResultKind.TextArray,    Description = "親のバンドルの id（上位は空）", Optional = true)]
    [PLResult("bundleClipCounts",  PLResultKind.IntegerArray, Description = "直接属するクリップの数", Optional = true)]
    [PLResult("bundleClipIds",     PLResultKind.TextArray,    Description = "直接属するクリップの id をバンドルの順に連結", Optional = true)]
    [PLResult("bundleChildCounts", PLResultKind.IntegerArray, Description = "子のバンドルの数", Optional = true)]
    [PLResult("bundleChildIds",    PLResultKind.TextArray,    Description = "子のバンドルの id をバンドルの順に連結（重ねる順）", Optional = true)]
    [PLResult("layerEnabled",      PLResultKind.IntegerArray, Description = "1 ならレイヤー", Optional = true)]
    [PLResult("layerModes",        PLResultKind.TextArray,    Description = "レイヤーの方式（override / additive。レイヤーでなければ空）", Optional = true)]
    [PLResult("layerWeights",      PLResultKind.NumberArray,  Description = "レイヤーの重み（レイヤーでなければ 0）", Optional = true)]
    [PLResult("layerMutes",        PLResultKind.IntegerArray, Description = "1 ならミュート", Optional = true)]
    [PLResult("layerSolos",        PLResultKind.IntegerArray, Description = "1 ならソロ", Optional = true)]
    [PLResult("layerOrder",        PLResultKind.TextArray,    Description = "レイヤーの重ねる順（バンドルの id）", Optional = true)]
    [PLResult("layerEffWeights",   PLResultKind.NumberArray,  Description = "layerOrder の各レイヤーの実効の重み（祖先のレイヤーの重みを掛けたもの）", Optional = true)]
    [PLResult("layerEffMutes",     PLResultKind.IntegerArray, Description = "layerOrder の各レイヤーの実効のミュート（ソロ・祖先のミュートを含む）", Optional = true)]
    public class QueryMotionEditBundlesCommand : PanelCommand
    {
        public QueryMotionEditBundlesCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, NoUi = true, Description = "モーションの試し再生のクリップの edit のトラックを、指定したフレームで評価した値（クリップのクロスフェード・レイヤー合成を含む。Motion Studio の Track.sample と同じ）を返す。edit の無いクリップは最初の書き込みで作られる形で評価し、クリップは変えない。")]
    [PLResult("editHeld",          PLResultKind.Flag,         Description = "窓口のクリップが edit を持つか")]
    [PLResult("sampleTrackIds",    PLResultKind.TextArray,    Description = "行のトラックの id", Optional = true)]
    [PLResult("sampleChannels",    PLResultKind.TextArray,    Description = "行のチャンネル", Optional = true)]
    [PLResult("sampleFrames",      PLResultKind.NumberArray,  Description = "行のフレーム", Optional = true)]
    [PLResult("sampleValueCounts", PLResultKind.IntegerArray, Description = "行ごとの成分の数", Optional = true)]
    [PLResult("sampleValues",      PLResultKind.NumberArray,  Description = "値を行の順に連結（回転は x,y,z,w）", Optional = true)]
    public class SampleMotionEditCommand : PanelCommand
    {
        [PLParam(Description = "評価するトラックの id。空にすると全トラック")]
        public string[] TrackIds { get; }

        [PLParam(Description = "評価するフレーム（タイムラインのフレーム。小数可）", Required = true)]
        public float[] Frames { get; }

        public SampleMotionEditCommand(int modelIndex, float[] frames, string[] trackIds = null)
            : base(modelIndex)
        {
            Frames   = frames   ?? System.Array.Empty<float>();
            TrackIds = trackIds ?? System.Array.Empty<string>();
        }
    }

    // ================================================================
    // 書き込み（窓口 motionClip の編集の口を 1 つずつ呼ぶ）
    // ================================================================

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit にトラックを足す。id は edit の今の最大値＋1 で振る（最大の id のものを除いた直後は同じ id が出る）。取り消せる。")]
    [PLResult("createdId", PLResultKind.Text, Description = "作ったトラックの id")]
    [PLUiRoute("モーションパネルの編集（トラックを足す）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.newTrack.role", "motionClipTest.edit.newTrack.targetKind", "motionClipTest.edit.newTrack.target",
        "motionClipTest.edit.newTrack.name", "motionClipTest.edit.newTrack.add")]
    public class MotionEditAddTrackCommand : PanelCommand
    {
        [PLParam(Description = "役割（bones / bakedBones / body / lookAt / expressions / muscles）", Required = true)]
        public string Role { get; }
        [PLParam(Description = "骨名・Humanoid 名・表情名・マッスル名（body・lookAt では使わない）")]
        public string Target { get; }
        [PLParam(Description = "bones だけ：boneName / path。空は boneName")]
        public string TargetKind { get; }
        [PLParam(Description = "表示名。空は target")]
        public string Name { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditAddTrackCommand(int modelIndex, string role, string target = "", string targetKind = "", string name = "", float time = -1f)
            : base(modelIndex)
        { Role = role ?? ""; Target = target ?? ""; TargetKind = targetKind ?? ""; Name = name ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit からトラックを除く（平らなトラックも除く）。除いた後は id を引き直す。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（トラックを除く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.track.remove")]
    public class MotionEditRemoveTrackCommand : PanelCommand
    {
        [PLParam(Description = "トラックの id", Required = true)]
        public string TrackId { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditRemoveTrackCommand(int modelIndex, string trackId, float time = -1f)
            : base(modelIndex) { TrackId = trackId ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のトラックのラップ（pre / post）を変える。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（ラップ）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.track.preWrap", "motionClipTest.edit.track.postWrap")]
    public class MotionEditSetWrapCommand : PanelCommand
    {
        [PLParam(Description = "トラックの id", Required = true)]
        public string TrackId { get; }
        [PLParam(Description = "先頭より前（Clamp / Loop / PingPong など）。空は未指定")]
        public string Pre { get; }
        [PLParam(Description = "末尾より後。空は未指定（ループのクリップがあれば Loop）")]
        public string Post { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetWrapCommand(int modelIndex, string trackId, string pre = "", string post = "", float time = -1f)
            : base(modelIndex) { TrackId = trackId ?? ""; Pre = pre ?? ""; Post = post ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のトラックに空のクリップを足す。id は edit の今の最大値＋1 で振る（最大の id のものを除いた直後は同じ id が出る）。取り消せる。")]
    [PLResult("createdId", PLResultKind.Text, Description = "作ったクリップの id")]
    [PLUiRoute("モーションパネルの編集（クリップを足す）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.newClip.start", "motionClipTest.edit.newClip.len", "motionClipTest.edit.newClip.add")]
    public class MotionEditAddClipCommand : PanelCommand
    {
        [PLParam(Description = "トラックの id", Required = true)]
        public string TrackId { get; }
        [PLParam(Description = "開始（フレーム、0 以上）")]
        public int Start { get; }
        [PLParam(Description = "長さ（フレーム、1 以上）")]
        public int Len { get; }
        [PLParam(Description = "クリップ名。空は既定の名前")]
        public string Name { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditAddClipCommand(int modelIndex, string trackId, int start = 0, int len = 30, string name = "", float time = -1f)
            : base(modelIndex) { TrackId = trackId ?? ""; Start = start; Len = len; Name = name ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit からクリップを除く（バンドルからも除く）。除いた後は id を引き直す。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（クリップを除く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.clip.remove")]
    public class MotionEditRemoveClipCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditRemoveClipCommand(int modelIndex, string clipId, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップの開始・長さ・ループ・重みを変える（重みは 0～1 に丸める）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（クリップ）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.clip.start", "motionClipTest.edit.clip.len",
        "motionClipTest.edit.clip.loop", "motionClipTest.edit.clip.weight")]
    public class MotionEditSetClipCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "開始（フレーム、0 以上）", Required = true)]
        public int Start { get; }
        [PLParam(Description = "長さ（フレーム、1 以上）", Required = true)]
        public int Len { get; }
        [PLParam(Description = "ループするか", Required = true)]
        public bool Loop { get; }
        [PLParam(Description = "重み（0～1）", Required = true)]
        public float Weight { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetClipCommand(int modelIndex, string clipId, int start, int len, bool loop, float weight, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Start = start; Len = len; Loop = loop; Weight = weight; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップの先頭からのフレーム f に、全チャンネルの今の評価値でキーを打つ（Motion Studio の「キー追加」）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（キーを打つ）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.frame", "motionClipTest.edit.key.capture")]
    public class MotionEditCaptureKeyCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "クリップの先頭からのフレーム（0 以上、長さ未満）", Required = true)]
        public int F { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditCaptureKeyCommand(int modelIndex, string clipId, int f, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; F = f; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のキーの値を変える（接線・t0 は残す）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（キーの値）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.key.value.x", "motionClipTest.edit.key.value.y", "motionClipTest.edit.key.value.z", "motionClipTest.edit.key.value.w")]
    public class MotionEditSetKeyValueCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "クリップの先頭からのフレーム", Required = true)]
        public int F { get; }
        [PLParam(Description = "値。回転は x,y,z,w の 4 個、位置・拡大は x,y,z の 3 個、w は 1 個", Required = true)]
        public float[] Value { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetKeyValueCommand(int modelIndex, string clipId, string channel, int f, float[] value, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Value = value ?? System.Array.Empty<float>(); Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップで、1 つのチャンネルのフレーム f にキーを作る（あれば値だけ変え、接線・t0 は残す）。ほかのチャンネルは変えない。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（チャンネルのキーを作る）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.frame",
        "motionClipTest.edit.key.value.x", "motionClipTest.edit.key.value.y", "motionClipTest.edit.key.value.z", "motionClipTest.edit.key.value.w",
        "motionClipTest.edit.key.setChannel")]
    public class MotionEditSetChannelKeyCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "クリップの先頭からのフレーム（0 以上、長さ未満）", Required = true)]
        public int F { get; }
        [PLParam(Description = "値。回転は x,y,z,w の 4 個、位置・拡大は x,y,z の 3 個、w は 1 個", Required = true)]
        public float[] Value { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetChannelKeyCommand(int modelIndex, string clipId, string channel, int f, float[] value, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Value = value ?? System.Array.Empty<float>(); Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップで、1 つのチャンネルのフレーム f のキーだけを除く。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（チャンネルのキーを除く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.key.removeChannel")]
    public class MotionEditRemoveChannelKeyCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "クリップの先頭からのフレーム", Required = true)]
        public int F { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditRemoveChannelKeyCommand(int modelIndex, string clipId, string channel, int f, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップのフレーム f のキーを全チャンネルで除く。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（キーを除く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.frame", "motionClipTest.edit.key.remove")]
    public class MotionEditRemoveKeyCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "クリップの先頭からのフレーム", Required = true)]
        public int F { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditRemoveKeyCommand(int modelIndex, string clipId, int f, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; F = f; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のクリップのフレーム f のキーを newF へ動かす（移り先にキーがあればエラー）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（キーを動かす）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.frame", "motionClipTest.edit.key.moveTo",
        "motionClipTest.edit.key.move")]
    public class MotionEditMoveKeyCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "動かすキーのフレーム（クリップの先頭から）", Required = true)]
        public int F { get; }
        [PLParam(Description = "移り先のフレーム（クリップの範囲に丸める）", Required = true)]
        public int NewF { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditMoveKeyCommand(int modelIndex, string clipId, int f, int newF, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; F = f; NewF = newF; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のキーの入側か出側の接線モード（Constant / Linear / Auto / ClampedAuto / Free）を全成分に当てる。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（接線のモード）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.tan.inMode", "motionClipTest.edit.tan.outMode")]
    public class MotionEditSetTangentModeCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "キーのフレーム（クリップの先頭から）", Required = true)]
        public int F { get; }
        [PLParam(Description = "in（入側）か out（出側）", Required = true)]
        public string Side { get; }
        [PLParam(Description = "Constant / Linear / Auto / ClampedAuto / Free", Required = true)]
        public string Mode { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetTangentModeCommand(int modelIndex, string clipId, string channel, int f, string side, string mode, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Side = side ?? ""; Mode = mode ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のキーの成分 comp の入側か出側の傾き（値/秒）を書く（Auto・ClampedAuto・Linear は Free になる）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（接線の傾き）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.tan.comp", "motionClipTest.edit.tan.inSlope", "motionClipTest.edit.tan.outSlope")]
    public class MotionEditSetTangentSlopeCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "キーのフレーム（クリップの先頭から）", Required = true)]
        public int F { get; }
        [PLParam(Description = "成分の番号（0 から）", Required = true)]
        public int Comp { get; }
        [PLParam(Description = "in（入側）か out（出側）", Required = true)]
        public string Side { get; }
        [PLParam(Description = "傾き（値/秒）", Required = true)]
        public float Slope { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetTangentSlopeCommand(int modelIndex, string clipId, string channel, int f, int comp, string side, float slope, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Comp = comp; Side = side ?? ""; Slope = slope; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のキーの成分 comp の重み方式（None / In / Out / Both）を書く。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（接線の重み方式）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.tan.comp", "motionClipTest.edit.tan.weightedMode")]
    public class MotionEditSetWeightedModeCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "キーのフレーム（クリップの先頭から）", Required = true)]
        public int F { get; }
        [PLParam(Description = "成分の番号（0 から）", Required = true)]
        public int Comp { get; }
        [PLParam(Description = "None / In / Out / Both", Required = true)]
        public string WeightedMode { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetWeightedModeCommand(int modelIndex, string clipId, string channel, int f, int comp, string weightedMode, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Comp = comp; WeightedMode = weightedMode ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のキーの成分 comp の入側か出側の重み（0～1）を書く。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（接線の重み）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.track", "motionClipTest.edit.clip", "motionClipTest.edit.key.channel", "motionClipTest.edit.key.select",
        "motionClipTest.edit.tan.comp", "motionClipTest.edit.tan.inWeight", "motionClipTest.edit.tan.outWeight")]
    public class MotionEditSetTangentWeightCommand : PanelCommand
    {
        [PLParam(Description = "クリップの id", Required = true)]
        public string ClipId { get; }
        [PLParam(Description = "チャンネル（rot / pos / scl / w）", Required = true)]
        public string Channel { get; }
        [PLParam(Description = "キーのフレーム（クリップの先頭から）", Required = true)]
        public int F { get; }
        [PLParam(Description = "成分の番号（0 から）", Required = true)]
        public int Comp { get; }
        [PLParam(Description = "in（入側）か out（出側）", Required = true)]
        public string Side { get; }
        [PLParam(Description = "重み（0～1）", Required = true)]
        public float Weight { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetTangentWeightCommand(int modelIndex, string clipId, string channel, int f, int comp, string side, float weight, float time = -1f)
            : base(modelIndex) { ClipId = clipId ?? ""; Channel = channel ?? ""; F = f; Comp = comp; Side = side ?? ""; Weight = weight; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit で、クリップ（clipIds）とバンドル（bundleIds。入れ子にする）を束ねた新しいバンドルを作る。id は edit の今の最大値＋1 で振る（最大の id のものを除いた直後は同じ id が出る）。取り消せる。")]
    [PLResult("createdId", PLResultKind.Text, Description = "作ったバンドルの id")]
    [PLUiRoute("モーションパネルの編集（バンドルを作る）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.newBundle.clipIds", "motionClipTest.edit.newBundle.bundleIds", "motionClipTest.edit.newBundle.name",
        "motionClipTest.edit.newBundle.make")]
    public class MotionEditMakeBundleCommand : PanelCommand
    {
        [PLParam(Description = "束ねるクリップの id")]
        public string[] ClipIds { get; }
        [PLParam(Description = "束ねるバンドルの id（子にする）")]
        public string[] BundleIds { get; }
        [PLParam(Description = "バンドル名。空は既定の名前")]
        public string Name { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditMakeBundleCommand(int modelIndex, string[] clipIds = null, string[] bundleIds = null, string name = "", float time = -1f)
            : base(modelIndex)
        {
            ClipIds   = clipIds   ?? System.Array.Empty<string>();
            BundleIds = bundleIds ?? System.Array.Empty<string>();
            Name = name ?? ""; Time = time;
        }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のバンドルだけを解く（クリップと子は残し、子は親へ上げる）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（バンドルを解く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.bundle", "motionClipTest.edit.bundle.ungroup")]
    public class MotionEditUngroupCommand : PanelCommand
    {
        [PLParam(Description = "バンドルの id", Required = true)]
        public string BundleId { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditUngroupCommand(int modelIndex, string bundleId, float time = -1f)
            : base(modelIndex) { BundleId = bundleId ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のバンドルを中身ごと複製し、クリップの開始を dStart フレームずらす。id は edit の今の最大値＋1 から振る（最大の id のものを除いた直後は同じ id が出る）。取り消せる。")]
    [PLResult("createdId", PLResultKind.Text, Description = "作ったバンドル（複製の最上位）の id")]
    [PLUiRoute("モーションパネルの編集（バンドルの複製）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.bundle", "motionClipTest.edit.bundle.cloneShift", "motionClipTest.edit.bundle.clone")]
    public class MotionEditCloneBundleCommand : PanelCommand
    {
        [PLParam(Description = "バンドルの id", Required = true)]
        public string BundleId { get; }
        [PLParam(Description = "クリップの開始をずらすフレーム")]
        public int DStart { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditCloneBundleCommand(int modelIndex, string bundleId, int dStart = 0, float time = -1f)
            : base(modelIndex) { BundleId = bundleId ?? ""; DStart = dStart; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のバンドルを、中身のクリップと子ごと除く。除いた後は id を引き直す。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（バンドルを除く）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.bundle", "motionClipTest.edit.bundle.delete")]
    public class MotionEditDeleteBundleCommand : PanelCommand
    {
        [PLParam(Description = "バンドルの id", Required = true)]
        public string BundleId { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditDeleteBundleCommand(int modelIndex, string bundleId, float time = -1f)
            : base(modelIndex) { BundleId = bundleId ?? ""; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のバンドルのレイヤーを設定する（enabled false でレイヤーでなくする）。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（レイヤー）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.bundle", "motionClipTest.edit.layer.enabled", "motionClipTest.edit.layer.mode",
        "motionClipTest.edit.layer.weight", "motionClipTest.edit.layer.mute", "motionClipTest.edit.layer.solo")]
    public class MotionEditSetLayerCommand : PanelCommand
    {
        [PLParam(Description = "バンドルの id", Required = true)]
        public string BundleId { get; }
        [PLParam(Description = "レイヤーにするか", Required = true)]
        public bool Enabled { get; }
        [PLParam(Description = "override / additive。空は override")]
        public string Mode { get; }
        [PLParam(Description = "重み（0～1）")]
        public float Weight { get; }
        [PLParam(Description = "ミュート")]
        public bool Mute { get; }
        [PLParam(Description = "ソロ")]
        public bool Solo { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSetLayerCommand(int modelIndex, string bundleId, bool enabled, string mode = "override", float weight = 1f, bool mute = false, bool solo = false, float time = -1f)
            : base(modelIndex) { BundleId = bundleId ?? ""; Enabled = enabled; Mode = mode ?? ""; Weight = weight; Mute = mute; Solo = solo; Time = time; }
    }

    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Effects = PLCommandEffect.Animation | PLCommandEffect.Pose, Description = "モーションの試し再生のクリップの edit のレイヤーの重ねる順を、同じ入れ物の dir（-1 / 1）方向の隣のレイヤーと入れ替える。取り消せる。")]
    [PLUiRoute("モーションパネルの編集（重ねる順）", "leftPane.fold.Other", "leftPane.motionClipTestBtn",
        "motionClipTest.edit.bundle", "motionClipTest.edit.layer.up", "motionClipTest.edit.layer.down")]
    public class MotionEditSwapLayerCommand : PanelCommand
    {
        [PLParam(Description = "バンドルの id", Required = true)]
        public string BundleId { get; }
        [PLParam(Description = "-1 か 1", Required = true)]
        public int Dir { get; }
        [PLParam(Description = "当て直すフレームの時刻（秒）。-1 は直前に当てた時刻")]
        public float Time { get; }

        public MotionEditSwapLayerCommand(int modelIndex, string bundleId, int dir, float time = -1f)
            : base(modelIndex) { BundleId = bundleId ?? ""; Dir = dir; Time = time; }
    }
}
