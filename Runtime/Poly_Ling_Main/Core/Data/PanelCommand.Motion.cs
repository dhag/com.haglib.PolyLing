// PanelCommand.Motion.cs
// PolyLing モーション JSON（*.plmotion.json）の書き出し・検査の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）

namespace Poly_Ling.Data
{
    /// <summary>モーション変換の読み込み元の種類。</summary>
    public enum MotionSourceKind
    {
        /// <summary>VMD（MMD モーション）。</summary>
        Vmd = 0,

        /// <summary>PolyLing モーション JSON（version 1～4）。</summary>
        PolyLingMotionJson = 1,

        /// <summary>VRM アニメーション（.vrma）。VrmaCodec で読む。</summary>
        Vrma = 2,

        /// <summary>BVH（関節の階層つきモーション）。BvhCodec で読む。</summary>
        Bvh = 3,

        /// <summary>FBX のアニメーション（テイク）。FbxMotionReader（ufbx）で読む。</summary>
        Fbx = 4,
    }

    /// <summary>BVH の関節名の割り当て表（PolyLing_BVH対応計画.md 3）。</summary>
    public enum BvhSkeletonKind
    {
        /// <summary>関節名がいちばん多く一致する表を使う。</summary>
        Auto = 0,

        /// <summary>mocopi（root / torso_1～7 / l_up_arm …）。</summary>
        Mocopi = 1,

        /// <summary>CMU（Hips / LowerBack / LeftUpLeg / LeftArm …）。</summary>
        Cmu = 2,
    }

    /// <summary>
    /// モーションファイルを読み、PolyLing モーション JSON（version 2）として書き出す。
    /// モデルは参照しない。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "VMD・PolyLing モーション JSON・VRM アニメーション（.vrma）・BVH を読み、検査してから PolyLing モーション JSON として書き出す。モデルは参照しない。作業フォルダが要る。")]
    [PLResult("errors",      PLResultKind.Integer, Description = "検査で見つかったエラーの数。1 以上なら書き出していない")]
    [PLResult("warnings",    PLResultKind.Integer, Description = "検査で見つかった警告の数")]
    [PLResult("bones",       PLResultKind.Integer, Description = "ボーントラックの数")]
    [PLResult("bakedBones",  PLResultKind.Integer, Description = "焼いた本体ボーントラックの数")]
    [PLResult("muscles",     PLResultKind.Integer, Description = "マッスルトラックの数")]
    [PLResult("expressions", PLResultKind.Integer, Description = "表情トラックの数")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "書き出した長さ（秒）")]
    [PLResult("issues",      PLResultKind.Text,    Description = "エラーと警告の一覧（先頭の一部）")]
    public class ExportMotionJsonCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionJsonSavePath",
                 Description = "書き出し先の .json（推奨は .plmotion.json）。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "MotionJsonSourcePath",
                 Description = "読み込むモーションファイル。作業フォルダからの相対経路", Required = true)]
        public string SourcePath { get; }

        [PLParam(TextKey = "MotionJsonSourceKind",
                 Description = "読み込むファイルの種類")]
        public MotionSourceKind SourceKind { get; }

        [PLParam(TextKey = "MotionJsonAbsentMuscles",
                 Description = "書き出すファイルの absentMuscles（駆動されているボーンのトラックの無い dof の値）。\"zero\" = 0、\"rest\" = 正準 T ポーズの値。空なら読み込んだファイルの値のまま（無ければ \"zero\"）")]
        public string AbsentMuscles { get; }

        [PLParam(TextKey = "MotionJsonBvhSkeleton",
                 Description = "BVH を読むときの関節名の割り当て表。Auto は一致する関節名がいちばん多い表を使う。BVH 以外では使わない")]
        public BvhSkeletonKind BvhSkeleton { get; }

        [PLParam(TextKey = "MotionJsonBvhMappingCsv",
                 Description = "BVH の割り当て CSV（1 行目は見出し、各行は「Humanoid 名,関節名」）。表の行を上書き・追加する。作業フォルダからの相対経路。空なら使わない")]
        public string BvhMappingCsvPath { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、レスト（T ポーズ）として使う同じ骨格のモデル FBX。そのバインドポーズを関節名で使う（モーションだけの FBX はバインドポーズを持たず、ノードの既定の姿勢が T ポーズとは限らないため）。作業フォルダからの相対経路。空なら読み込むファイル自身のバインド姿勢")]
        public string FbxRestPath { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、読むテイク（アニメーションスタック）の番号。0 から。テイクが 1 つなら 0", Min = 0.0)]
        public int FbxTake { get; }

        public ExportMotionJsonCommand(
            int modelIndex, string filePath, string sourcePath,
            MotionSourceKind sourceKind = MotionSourceKind.Vmd,
            string absentMuscles = null,
            BvhSkeletonKind bvhSkeleton = BvhSkeletonKind.Auto,
            string bvhMappingCsvPath = null,
            string fbxRestPath = null,
            int fbxTake = 0)
            : base(modelIndex)
        {
            FbxRestPath       = fbxRestPath;
            FbxTake           = fbxTake;
            FilePath          = filePath;
            SourcePath        = sourcePath;
            SourceKind        = sourceKind;
            AbsentMuscles     = absentMuscles;
            BvhSkeleton       = bvhSkeleton;
            BvhMappingCsvPath = bvhMappingCsvPath;
        }
    }

    /// <summary>
    /// モーションを現在のモデルへ当てながら、モデルの骨格の BVH を書き出す（BvhCodec.ExportFromModel）。
    /// 規則は PolyLing_BVH対応計画.md 5。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "モーション（VMD・PolyLing モーション・VRM アニメーション・BVH）を現在のモデルへ当てながら、モデルの骨格（IK ボーンを除く）の BVH を書き出す。モデルの姿勢は終わったら戻す。マッスルを持つモーションにはモデルの Humanoid 割り当てが要る。作業フォルダが要る。")]
    [PLResult("frames",         PLResultKind.Integer, Description = "書き出したフレーム数")]
    [PLResult("joints",         PLResultKind.Integer, Description = "書き出した関節の数（End Site を除く）")]
    [PLResult("positionJoints", PLResultKind.Integer, Description = "位置のチャンネルも書いた関節の数（根を含む）")]
    [PLResult("matchedTracks",  PLResultKind.Integer, Description = "モデルに当たったトラックの数")]
    [PLResult("durationSec",    PLResultKind.Number,  Description = "書き出した長さ（秒）")]
    [PLResult("rootMotion",     PLResultKind.Flag,    Description = "body（身体全体の位置と向き）を根の動きとして書いたか")]
    public class ExportMotionToBvhCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionBvhSavePath",
                 Description = "書き出し先の .bvh。作業フォルダからの相対経路。絶対経路と \"..\" は拒否される（ダイアログで選んだ直後のパスだけは例外）", Required = true)]
        public string FilePath { get; }

        [PLParam(TextKey = "MotionBvhSourcePath",
                 Description = "読み込むモーションファイル。作業フォルダからの相対経路", Required = true)]
        public string SourcePath { get; }

        [PLParam(TextKey = "MotionBvhSourceKind",
                 Description = "読み込むファイルの種類")]
        public MotionSourceKind SourceKind { get; }

        [PLParam(TextKey = "MotionBvhSkeleton",
                 Description = "読み込むファイルが BVH のときの関節名の割り当て表。BVH 以外では使わない")]
        public BvhSkeletonKind BvhSkeleton { get; }

        [PLParam(TextKey = "MotionBvhMappingCsv",
                 Description = "読み込むファイルが BVH のときの割り当て CSV（1 行目は見出し、各行は「Humanoid 名,関節名」）。作業フォルダからの相対経路。空なら使わない")]
        public string BvhMappingCsvPath { get; }

        [PLParam(TextKey = "MotionBvhFps",
                 Description = "1 秒あたりのフレーム数。0 以下にするとモーションの frameRate を使う",
                 Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(TextKey = "MotionBvhStartSec",
                 Description = "書き出す区間の開始時刻（秒）。出力では 0 秒になる", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(TextKey = "MotionBvhEndSec",
                 Description = "書き出す区間の終了時刻（秒）。StartSec 以下にするとモーションの終端まで", Min = 0.0)]
        public float EndSec { get; }

        [PLParam(TextKey = "MotionBvhPositionScale",
                 Description = "モデルの長さに掛ける倍率（OFFSET と位置のチャンネルに掛かる）。1 ならモデルの単位のまま",
                 Min = 0.000001, Max = 100000.0)]
        public float PositionScale { get; }

        [PLParam(TextKey = "MotionBvhAlignLegs",
                 Description = "モデルへ当てるとき、T ポーズ合わせを腕に加えて脚（太もも・すね）にも掛けるか。既定は false（腕だけ）")]
        public bool AlignLegs { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、レスト（T ポーズ）として使う同じ骨格のモデル FBX。そのバインドポーズを関節名で使う（モーションだけの FBX はバインドポーズを持たず、ノードの既定の姿勢が T ポーズとは限らないため）。作業フォルダからの相対経路。空なら読み込むファイル自身のバインド姿勢")]
        public string FbxRestPath { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、読むテイク（アニメーションスタック）の番号。0 から。テイクが 1 つなら 0", Min = 0.0)]
        public int FbxTake { get; }

        public ExportMotionToBvhCommand(
            int modelIndex, string filePath, string sourcePath,
            MotionSourceKind sourceKind = MotionSourceKind.PolyLingMotionJson,
            BvhSkeletonKind bvhSkeleton = BvhSkeletonKind.Auto,
            string bvhMappingCsvPath = null,
            float fps = 0f, float startSec = 0f, float endSec = 0f, float positionScale = 1f,
            bool alignLegs = false,
            string fbxRestPath = null,
            int fbxTake = 0)
            : base(modelIndex)
        {
            FbxRestPath       = fbxRestPath;
            FbxTake           = fbxTake;
            FilePath          = filePath;
            SourcePath        = sourcePath;
            SourceKind        = sourceKind;
            BvhSkeleton       = bvhSkeleton;
            BvhMappingCsvPath = bvhMappingCsvPath;
            Fps               = fps;
            StartSec          = startSec;
            EndSec            = endSec;
            PositionScale     = positionScale;
            AlignLegs         = alignLegs;
        }
    }

    /// <summary>
    /// モーションを現在のモデルへ当てながら、モデル（または骨格だけ）とアニメーションの FBX を書き出す
    /// （IFbxExporter.ExportMotion）。規則は PolyLing_FBX対応計画.md 段 3。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "モーション（VMD・PolyLing モーション・VRM アニメーション・BVH）を現在のモデルへ当てながら、モデルとアニメーション（ボーンの移動・回転、表情のブレンドシェイプ）の FBX を書き出す。IncludeMesh を切ると骨格とモーションだけ。モデルの姿勢は終わったら戻す。マッスルを持つモーションにはモデルの Humanoid 割り当てが要る。作業フォルダが要る。")]
    [PLResult("frames",               PLResultKind.Integer, Description = "書き出したフレーム数")]
    [PLResult("durationSec",          PLResultKind.Number,  Description = "書き出した長さ（秒）")]
    [PLResult("animatedNodes",        PLResultKind.Integer, Description = "曲線を書いたボーンの数")]
    [PLResult("skippedVirtualJoints", PLResultKind.Integer, Description = "FBX のノードを持たないので曲線を書けなかった関節（半身モデルの仮想ミラー）の数")]
    [PLResult("animatedBlendShapes",  PLResultKind.Integer, Description = "重みの曲線を書いたブレンドシェイプの数")]
    [PLResult("expressions",          PLResultKind.Integer, Description = "モーションの表情トラックの数")]
    [PLResult("expressionsResolved",  PLResultKind.Integer, Description = "モデルの頂点モーフへ当たった表情トラックの数")]
    [PLResult("matchedTracks",        PLResultKind.Integer, Description = "モデルに当たったトラックの数")]
    [PLResult("rootMotion",           PLResultKind.Flag,    Description = "body（身体全体の位置と向き）を根の動きとして書いたか")]
    public class ExportMotionToFbxCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先の .fbx。作業フォルダからの相対経路", Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "読み込むモーションファイル。作業フォルダからの相対経路", Required = true)]
        public string SourcePath { get; }

        [PLParam(Description = "読み込むファイルの種類")]
        public MotionSourceKind SourceKind { get; }

        [PLParam(Description = "読み込むファイルが BVH のときの関節名の割り当て表。BVH 以外では使わない")]
        public BvhSkeletonKind BvhSkeleton { get; }

        [PLParam(Description = "読み込むファイルが BVH のときの割り当て CSV（1 行目は見出し、各行は「Humanoid 名,関節名」）。作業フォルダからの相対経路。空なら使わない")]
        public string BvhMappingCsvPath { get; }

        [PLParam(Description = "1 秒あたりのフレーム数。0 以下にするとモーションの frameRate を使う", Min = 0.0, Max = 240.0)]
        public float Fps { get; }

        [PLParam(Description = "書き出す区間の開始時刻（秒）。出力では 0 秒になる", Min = 0.0)]
        public float StartSec { get; }

        [PLParam(Description = "書き出す区間の終了時刻（秒）。StartSec 以下にするとモーションの終端まで", Min = 0.0)]
        public float EndSec { get; }

        [PLParam(Description = "モデルへ当てるとき、T ポーズ合わせを腕に加えて脚（太もも・すね）にも掛けるか。既定は false（腕だけ）")]
        public bool AlignLegs { get; }

        [PLParam(Description = "メッシュ（スキン・ブレンドシェイプ・材質）も書くか。切ると骨格とモーションだけ（表情の曲線も書かない）")]
        public bool IncludeMesh { get; }

        [PLParam(Description = "モデル部分の書き出し設定。省いた項目は既定値のまま")]
        public Poly_Ling.Fbx.FbxExportSettings Settings { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、レスト（T ポーズ）として使う同じ骨格のモデル FBX。そのバインドポーズを関節名で使う（モーションだけの FBX はバインドポーズを持たず、ノードの既定の姿勢が T ポーズとは限らないため）。作業フォルダからの相対経路。空なら読み込むファイル自身のバインド姿勢")]
        public string FbxRestPath { get; }

        [PLParam(Description = "読み込むファイルが FBX のとき、読むテイク（アニメーションスタック）の番号。0 から。テイクが 1 つなら 0", Min = 0.0)]
        public int FbxTake { get; }

        public ExportMotionToFbxCommand(
            int modelIndex, string filePath, string sourcePath,
            MotionSourceKind sourceKind = MotionSourceKind.PolyLingMotionJson,
            BvhSkeletonKind bvhSkeleton = BvhSkeletonKind.Auto,
            string bvhMappingCsvPath = null,
            float fps = 0f, float startSec = 0f, float endSec = 0f,
            bool alignLegs = false,
            bool includeMesh = true,
            Poly_Ling.Fbx.FbxExportSettings settings = null,
            string fbxRestPath = null,
            int fbxTake = 0)
            : base(modelIndex)
        {
            FbxRestPath       = fbxRestPath;
            FbxTake           = fbxTake;
            FilePath          = filePath;
            SourcePath        = sourcePath;
            SourceKind        = sourceKind;
            BvhSkeleton       = bvhSkeleton;
            BvhMappingCsvPath = bvhMappingCsvPath;
            Fps               = fps;
            StartSec          = startSec;
            EndSec            = endSec;
            AlignLegs         = alignLegs;
            IncludeMesh       = includeMesh;
            Settings          = settings ?? Poly_Ling.Fbx.FbxExportSettings.CreateDefault();
        }
    }

    /// <summary>
    /// フォルダ内の PolyLing モーション（*.plmotion.json）のうち、Root を muscles に置いた旧表現のものを
    /// body トラックへ移して version 3 で上書きする（MotionClipRootUpgrade）。サブフォルダは見ない。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "フォルダ内の PolyLing モーション（*.plmotion.json）のうち、Root（RootT / RootQ）を muscles に置いた旧表現のものを body トラックへ移して上書きする。サブフォルダは見ない。モデルは参照しない。作業フォルダが要る。")]
    [PLResult("converted", PLResultKind.Integer, Description = "書き換えたファイルの数")]
    [PLResult("skipped",   PLResultKind.Integer, Description = "書き換えなかったファイルの数（旧表現でない・body が既にある）")]
    [PLResult("failed",    PLResultKind.Integer, Description = "読めない・検査でエラーになったファイルの数")]
    [PLResult("issues",    PLResultKind.Text,    Description = "失敗と書き換えなかった理由の一覧（先頭の一部）")]
    public class ConvertMotionRootToBodyCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionRootUpgradeFolder",
                 Description = "対象フォルダ。作業フォルダからの相対経路", Required = true)]
        public string FolderPath { get; }

        public ConvertMotionRootToBodyCommand(int modelIndex, string folderPath)
            : base(modelIndex)
        {
            FolderPath = folderPath;
        }
    }

    /// <summary>
    /// PolyLing モーション JSON を読んで検査する。ファイルもモデルも変えない。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "PolyLing モーション JSON を読んで検査し、トラック数と問題点を返す。ファイルもモデルも変えない。作業フォルダが要る。")]
    [PLResult("valid",       PLResultKind.Flag,    Description = "エラーが無く読み込めるか")]
    [PLResult("version",     PLResultKind.Integer, Description = "ファイルに書かれていた版（読めなかったときは 0）")]
    [PLResult("errors",      PLResultKind.Integer, Description = "エラーの数")]
    [PLResult("warnings",    PLResultKind.Integer, Description = "警告の数")]
    [PLResult("bones",       PLResultKind.Integer, Description = "ボーントラックの数")]
    [PLResult("bakedBones",  PLResultKind.Integer, Description = "焼いた本体ボーントラックの数")]
    [PLResult("muscles",     PLResultKind.Integer, Description = "マッスルトラックの数")]
    [PLResult("expressions", PLResultKind.Integer, Description = "表情トラックの数")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "長さ（秒）")]
    [PLResult("issues",      PLResultKind.Text,    Description = "エラーと警告の一覧（先頭の一部）")]
    public class QueryMotionJsonCommand : PanelCommand
    {
        [PLParam(TextKey = "MotionJsonQueryPath",
                 Description = "検査する .json。作業フォルダからの相対経路", Required = true)]
        public string FilePath { get; }

        public QueryMotionJsonCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath;
        }
    }

    /// <summary>
    /// PolyLing モーション JSON の文字列を、モーションの試し再生（ツールの窓口 motionClip）に読み込んで
    /// 今のモデルへ当てる（MotionClipHandler.LoadJson）。Blender ブリッジのモーション送信が使う。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "PolyLing モーション JSON の文字列（.plmotion.json と同じ中身）を検査して、モーションの試し再生（ツールの窓口 motionClip）に読み込み、今のモデルへ time 秒のフレームを当てる。読めなければ今のクリップのまま。ファイルは書かない。Blender ブリッジのモーション送信が使う。")]
    [PLResult("name",          PLResultKind.Text,    Description = "クリップ名")]
    [PLResult("durationSec",   PLResultKind.Number,  Description = "長さ（秒。duration とキー時刻の最大値の大きい方）")]
    [PLResult("frameRate",     PLResultKind.Number,  Description = "フレームレート")]
    [PLResult("bakedBones",    PLResultKind.Integer, Description = "焼いた本体ボーントラックの数")]
    [PLResult("muscles",       PLResultKind.Integer, Description = "マッスルトラックの数（bakedBones だけのクリップは読込で焼いたもの）")]
    [PLResult("expressions",   PLResultKind.Integer, Description = "表情トラックの数")]
    [PLResult("matchedTracks", PLResultKind.Integer, Description = "モデルのボーンと対応が取れた bones トラックの数")]
    [PLResult("warnings",      PLResultKind.Integer, Description = "検査の警告の数")]
    [PLResult("issues",        PLResultKind.Text,    Description = "検査の警告の一覧（先頭の一部）")]
    public class LoadMotionClipJsonCommand : PanelCommand
    {
        [PLParam(Description = "PolyLing モーション JSON の文字列（.plmotion.json のファイルの中身そのまま）", Required = true)]
        public string MotionJson { get; }

        [PLParam(Description = "当てるフレームの時刻（秒）", Min = 0.0)]
        public float Time { get; }

        public LoadMotionClipJsonCommand(int modelIndex, string motionJson, float time = 0f)
            : base(modelIndex)
        {
            MotionJson = motionJson ?? "";
            Time       = time;
        }
    }

    /// <summary>
    /// モーションの試し再生（ツールの窓口 motionClip）に読み込んであるクリップを、PolyLing モーション JSON の
    /// 文字列で返す（MotionClipSerializer.ToSaveJson。JSON 保存で書くものと同じ中身）。
    /// Blender ブリッジのモーション取得が使う。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "モーションの試し再生（ツールの窓口 motionClip）に読み込んであるクリップを、PolyLing モーション JSON の文字列で返す。中身は JSON 保存でファイルに書くものと同じ（muscles を持つクリップには bakedBones を添える）。クリップもモデルも変えない。Blender ブリッジのモーション取得が使う。")]
    [PLResult("motionJson",  PLResultKind.Text,    Description = "PolyLing モーション JSON（.plmotion.json と同じ中身）")]
    [PLResult("name",        PLResultKind.Text,    Description = "クリップ名")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "長さ（秒）")]
    [PLResult("sourcePath",  PLResultKind.Text,    Description = "読み込んだ元ファイル（JSON の文字列から読んだクリップは空）")]
    public class QueryMotionClipJsonCommand : PanelCommand
    {
        public QueryMotionClipJsonCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>
    /// モーションの試し再生（ツールの窓口 motionClip）に読み込んであるクリップを、PolyLing モーション JSON の
    /// ファイルに書き、そのファイルを窓口の元ファイルにする（MotionClipHandler.SaveJson。中身は queryMotionClipJson と同じ）。
    /// 外から受けたクリップ（loadMotionClipJson）をファイルにし、パネルの読み直し・VRMA・BVH・FBX 書き出しを使えるようにする。
    /// PolyLing_Blender不具合修正計画.md の B。
    /// </summary>
    [PLCommand(Category = "animation", Writes = PLWriteScope.None, Description = "モーションの試し再生（ツールの窓口 motionClip）に読み込んであるクリップを PolyLing モーション JSON（.plmotion.json）として書き、そのファイルを窓口の元ファイルにする。中身は queryMotionClipJson と同じ。Blender ブリッジから受けたクリップをファイルにして、パネルの読み直し・VRMA 書き出しなどを使えるようにする。作業フォルダの下だけへ書ける。")]
    [PLResult("path",        PLResultKind.Text,    Description = "実際に書いた経路（窓口の元ファイルになる）")]
    [PLResult("name",        PLResultKind.Text,    Description = "クリップ名")]
    [PLResult("durationSec", PLResultKind.Number,  Description = "長さ（秒）")]
    public class SaveMotionClipJsonCommand : PanelCommand
    {
        [PLParam(Description = "保存先の .plmotion.json。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        public SaveMotionClipJsonCommand(int modelIndex, string filePath)
            : base(modelIndex)
        {
            FilePath = filePath;
        }
    }
}
