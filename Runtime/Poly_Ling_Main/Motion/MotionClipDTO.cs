using System;
using System.Collections.Generic;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipDTO （統合モーション中間データ・純POCO）
    // ----------------------------------------------------------------
    // PolyLing のモーションファイル形式（*.plmotion.json）の唯一の保存形式。
    // VMD は MotionClipConverters で、Unity の AnimationClip は Editor の UnityClipExportWindow で、
    // mocopi は PolyLing_mocopi の MocopiClipBuilder で、この形式へ書き出す。
    // 外部連携（Python / ブラウザ）の契約は PolyLing_外部連携の契約.md にまとめてある。
    // キー時刻はすべて float 秒（t）。値はすべて Unity 左手系。四元数の並びは [x, y, z, w]。
    // UnityEngine 型をフィールドに一切持たない（int / float / float? / float[] / string / bool のみ）。
    //
    // ■ 版
    //   version 1 : キーは値だけ。キー間は線形（pos=Lerp / rot=Slerp）。モーフは "morphs"。
    //   version 2 : キーに接線（tan）を任意で持てる。トラックにラップモードを持てる。
    //               "morphs" は "expressions" になった。duration / metadata を追加。
    //   version 3 : Root（RootT / RootQ）は body トラックだけに置く（muscles に "RootT.x" などを入れない）。
    //   version 4 : bakedBones の意味を「正準骨格上のローカル回転」に固定した（下の Humanoid 固有）。
    //               視線（lookAt トラックと lookAtOffsetFromHeadBone）を追加。sourceRest を廃止。
    //   読込は MotionClipSerializer が version 1～3 を 4 へ引き上げる（tan は付けない＝線形のまま）。
    //   sourceRest があれば捨てて警告する。
    //   ただし muscles に Root の名前があるファイルは版によらずエラーにする（自動では移さない）。
    //   書き出しは常に version 4。
    //
    // ■ 形式の基本方針
    //   - スパース: 各トラックは「秒付きキーの列」。密な毎フレーム配列ではない。
    //   - キー内チャンネル: 1 キーに pos[3] / rot[4] / scl[3] を任意で持つ（未使用は null）。
    //   - 補間: 接線（tan）が無いキーは線形。規則の正本は MotionCurveMath。
    //   - 座標系: 値はすべて Unity 左手系（VMD の X・Z 両反転は生成側で済ませる）。
    //
    // ■ targetKind（トラックの解決方法＝適用規約）
    //   "boneName" : モデル骨名で直接解決（VMD/MMD 由来）。VMD 直接適用（リターゲットなし）。bones だけ。
    //   "path"     : Unity Transform パス末尾で対応表解決（二次骨など）。bones だけ。
    //   "humanoid" : Humanoid 正準名（HumanBodyBones の列挙名）で解決。bakedBones は必ずこれ。
    //   "body"     : body トラック専用（id も "body"）。
    //   "lookAt"   : lookAt トラック専用（id も "lookAt"）。
    //
    // ■ Humanoid 固有
    //   muscles  … HumanTrait.MuscleName の名前だけ。値は -1～1 が目安。rotationSource が "muscles" のとき再生の正本。
    //   body     … HumanPose.bodyPosition（pos）/ bodyRotation（rot）。bodyPosition は腰高を 1 とする正規化量。
    //   bakedBones … 正準骨格（VRM T ポーズ。レストの全ローカル回転が単位）上の Humanoid ローカル回転。
    //                id は HumanBodyBones の列挙名。rot だけを持つ。Hips だけ pos を持てて、
    //                pos は「レスト位置からの差 ÷ レストの腰の高さ」（腰高 1 の正規化量）。
    //                モデルに依らない量なので、Unity の外（JS など）はこれで骨を動かす。
    //                rotationSource が "muscles" なら、C# が書くファイルにはマッスルから Unity で解いた値を必ず添える。
    //   rotationSource … 本体の回転の正本。"muscles"（null も同じ）か "bakedBones"。
    //                "bakedBones" は BVH・FBX・VRMA など骨の回転を読んだクリップ。再生・BVH / FBX 書き出しは
    //                bakedBones をそのまま使い（マッスルで表せない回転＝肩・手のねじり、前腕・すねの横曲げも残る）、
    //                muscles＋body は Humanoid 向け（.anim・配信）に添える。保存でも bakedBones は作り直さない。
    //   absentMuscles … トラックを 1 本以上持つボーン（駆動されているボーン）のうち、トラックの無い
    //                dof の値。"zero" = 0（Unity のマッスル 0）、"rest" = 正準骨格の T ポーズの値。
    //                null（書いていない）は "zero"。再生・bakedBones の焼き込み・VRMA 書き出しで共通。
    //                トラックが 1 本も無いボーンは、どちらでも祖先に付いていく（正準では単位ローカル）。
    //
    // ■ 視線
    //   lookAt   … VRMA の lookAt ノードの局所回転と同じ意味の rot だけを持つ（Unity 左手系）。
    //              回転していないときの視線はモデル空間の +Z。
    //   lookAtOffsetFromHeadBone … 視線の原点の Head からの差 [x, y, z]（メートル、Unity 左手系）。任意。
    // ================================================================
    [Serializable]
    public class MotionClipDTO
    {
        /// <summary>形式識別子の正本。</summary>
        public const string FormatName = "PolyLingMotion";

        /// <summary>この実装が書き出す版。</summary>
        public const int CurrentVersion = 4;

        // ---- targetKind の値 ----
        public const string TargetBoneName = "boneName";
        public const string TargetPath     = "path";
        public const string TargetHumanoid = "humanoid";
        public const string TargetBody     = "body";
        public const string TargetLookAt   = "lookAt";

        /// <summary>body トラックの id。</summary>
        public const string BodyTrackId = "body";

        /// <summary>lookAt トラックの id。</summary>
        public const string LookAtTrackId = "lookAt";

        /// <summary>形式識別子。</summary>
        public string format = FormatName;

        /// <summary>形式バージョン。</summary>
        public int version = CurrentVersion;

        /// <summary>クリップ名。</summary>
        public string name;

        /// <summary>フレームレート（参考値）。キー時刻 t は秒。</summary>
        public float frameRate = 30f;

        /// <summary>長さ（秒）。0 のときはキー時刻の最大値を長さとみなす。</summary>
        public float duration = 0f;

        /// <summary>値の空間。"local" = ボーンローカル変換（Unity 左手系）。</summary>
        public string space = "local";

        /// <summary>ループ設定。</summary>
        public bool loop = false;

        /// <summary>ボーントラック（targetKind = "boneName" または "path"）。</summary>
        public List<MotionTrackDTO> bones = new List<MotionTrackDTO>();

        /// <summary>表情トラック（論理表情名＋重み）。</summary>
        public List<MotionExpressionTrackDTO> expressions = new List<MotionExpressionTrackDTO>();

        // ---- 以下 Humanoid 固有（案A）----

        /// <summary>Muscle 名（HumanTrait.MuscleName）別の値トラック。</summary>
        public List<MotionScalarTrackDTO> muscles = new List<MotionScalarTrackDTO>();

        /// <summary>ルート（body）姿勢トラック（id・targetKind とも "body"。pos = RootT、rot = RootQ）。未使用時は null。</summary>
        public MotionTrackDTO body = null;

        /// <summary>正準骨格上の Humanoid ローカル回転トラック（targetKind = "humanoid"）。</summary>
        public List<MotionTrackDTO> bakedBones = new List<MotionTrackDTO>();

        /// <summary>視線トラック（id・targetKind とも "lookAt"。rot だけ）。未使用時は null。</summary>
        public MotionTrackDTO lookAt = null;

        /// <summary>視線の原点の Head からの差 [x, y, z]（メートル、Unity 左手系）。未使用時は null。</summary>
        public float[] lookAtOffsetFromHeadBone = null;

        // ---- absentMuscles の値 ----
        public const string AbsentMusclesZero = "zero";
        public const string AbsentMusclesRest = "rest";

        /// <summary>駆動されているボーンのトラックの無い dof の値（"zero" / "rest"）。null は "zero"。</summary>
        public string absentMuscles = null;

        /// <summary>absentMuscles が "rest" か。</summary>
        public static bool AbsentIsRest(MotionClipDTO dto)
            => dto != null && string.Equals(dto.absentMuscles, AbsentMusclesRest, StringComparison.Ordinal);

        // ---- rotationSource の値 ----
        public const string RotationSourceMuscles    = "muscles";
        public const string RotationSourceBakedBones = "bakedBones";

        /// <summary>本体の回転の正本（"muscles" / "bakedBones"）。null は "muscles"。</summary>
        public string rotationSource = null;

        /// <summary>本体の回転の正本が bakedBones か（rotationSource が "bakedBones" で、bakedBones を 1 本以上持つ）。</summary>
        public static bool BakedBonesArePrimary(MotionClipDTO dto)
            => dto != null && string.Equals(dto.rotationSource, RotationSourceBakedBones, StringComparison.Ordinal)
               && dto.bakedBones != null && dto.bakedBones.Count > 0;

        /// <summary>付帯情報。未使用時は null。</summary>
        public MotionMetadataDTO metadata = null;
    }

    // ----------------------------------------------------------------
    // ボーントラック: 識別子＋解決方法＋ラップモード＋キー列。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionTrackDTO
    {
        /// <summary>トラック識別子（骨名 / Transform パス / Humanoid 正準名）。</summary>
        public string id;

        /// <summary>解決方法。"boneName" / "path" / "humanoid" / "body"（MotionClipDTO.Target* 定数）。</summary>
        public string targetKind = "path";

        /// <summary>先頭キーより前の扱い。"Clamp"（既定）/ "Loop" / "PingPong"。null は Clamp。</summary>
        public string preWrapMode;

        /// <summary>末尾キーより後の扱い。"Clamp"（既定）/ "Loop" / "PingPong"。null は Clamp。</summary>
        public string postWrapMode;

        /// <summary>キー列。秒昇順。</summary>
        public List<MotionKeyDTO> keys = new List<MotionKeyDTO>();
    }

    // ----------------------------------------------------------------
    // ボーンキー: t（秒）と、任意の pos / rot / scl。
    // 各チャンネルの成分ごとに接線を任意で持つ（posTan[3] / rotTan[4] / sclTan[3]）。
    // 接線配列が null のチャンネルは、そのキーの両側を線形とみなす。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionKeyDTO
    {
        /// <summary>時刻（秒）。</summary>
        public float t;

        /// <summary>位置 [x, y, z]（Unity 左手系）。使わない場合は null。</summary>
        public float[] pos;

        /// <summary>回転 [x, y, z, w]（クォータニオン, Unity 左手系）。使わない場合は null。</summary>
        public float[] rot;

        /// <summary>スケール [x, y, z]。使わない場合は null。</summary>
        public float[] scl;

        /// <summary>pos の成分ごとの接線 [x, y, z]。null は線形。</summary>
        public MotionTangentDTO[] posTan;

        /// <summary>rot の成分ごとの接線 [x, y, z, w]。null は線形（Slerp）。</summary>
        public MotionTangentDTO[] rotTan;

        /// <summary>scl の成分ごとの接線 [x, y, z]。null は線形。</summary>
        public MotionTangentDTO[] sclTan;
    }

    // ----------------------------------------------------------------
    // スカラートラック（マッスル・表情の共通部）。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionScalarTrackDTO
    {
        /// <summary>名前（Muscle 名 / 表情名）。</summary>
        public string name;

        /// <summary>先頭キーより前の扱い。null は Clamp。</summary>
        public string preWrapMode;

        /// <summary>末尾キーより後の扱い。null は Clamp。</summary>
        public string postWrapMode;

        /// <summary>値キー。秒昇順。</summary>
        public List<MotionScalarKeyDTO> keys = new List<MotionScalarKeyDTO>();
    }

    /// <summary>スカラーキー。t = 時刻（秒）、v = 値、tan = 接線（null は線形）。</summary>
    [Serializable]
    public class MotionScalarKeyDTO
    {
        public float t;
        public float v;
        public MotionTangentDTO tan;
    }

    // ----------------------------------------------------------------
    // 表情トラック。値は原則 0～1。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionExpressionTrackDTO : MotionScalarTrackDTO
    {
        /// <summary>"emotion" / "eye" / "mouth" / "custom"。省略可。</summary>
        public string category;

        /// <summary>解決方式の補助情報。"generic" / "vrm1" / "blendShape" / "custom"。省略可。</summary>
        public string provider;
    }

    // ----------------------------------------------------------------
    // 接線（1 成分・1 キー分）。Unity の Keyframe と同じ意味の値を持つ。
    //   inTangent / outTangent : 左右の傾き（値/秒）。null はモードから求める。
    //   inWeight / outWeight   : 重み（0～1）。weightedMode が該当側を含むときだけ使う。
    //   weightedMode           : "None" / "In" / "Out" / "Both"。null は None。
    //   leftTangentMode / rightTangentMode :
    //       "Free" / "Auto" / "ClampedAuto" / "Linear" / "Constant"。null は Free。
    //   Constant は無限大の接線を JSON に書けないため、数値ではなくモードで表す。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionTangentDTO
    {
        public float? inTangent;
        public float? outTangent;
        public float? inWeight;
        public float? outWeight;
        public string weightedMode;
        public string leftTangentMode;
        public string rightTangentMode;
    }

    // ----------------------------------------------------------------
    // 付帯情報。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionMetadataDTO
    {
        public string author;
        public string description;
        public string createdWith;
    }
}
