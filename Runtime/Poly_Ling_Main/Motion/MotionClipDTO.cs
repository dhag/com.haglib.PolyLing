using System;
using System.Collections.Generic;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipDTO （統合モーション中間データ・純POCO）
    // ----------------------------------------------------------------
    // VMD 系（MotionDTO）と Unity クリップ系（UnityClipDTO）を 1 本に統合した
    // PolyLing 独自のモーションファイル形式（*.plmotion.json）。
    // キー時刻はすべて float 秒（t）。値はすべて Unity 左手系。
    // UnityEngine 型をフィールドに一切持たない（int / float / float? / float[] / string / bool のみ）。
    //
    // ■ 移行方針（恒久メモ）
    //   旧 MotionDTO / UnityClipDTO と旧テストパネルは比較用に残置し、
    //   本形式へは片方向コンバータ（MotionClipConverters）で順次移行する。
    //
    // ■ 版
    //   version 1 : キーは値だけ。キー間は線形（pos=Lerp / rot=Slerp）。モーフは "morphs"。
    //   version 2 : キーに接線（tan）を任意で持てる。トラックにラップモードを持てる。
    //               "morphs" は "expressions" になった。duration / metadata を追加。
    //   読込は MotionClipSerializer が version 1 を version 2 へ引き上げる（tan は付けない＝線形のまま）。
    //   書き出しは常に version 2。
    //
    // ■ 形式の基本方針
    //   - スパース: 各トラックは「秒付きキーの列」。密な毎フレーム配列ではない。
    //   - キー内チャンネル: 1 キーに pos[3] / rot[4] / scl[3] を任意で持つ（未使用は null）。
    //   - 補間: 接線（tan）が無いキーは線形。規則の正本は MotionCurveMath。
    //   - 座標系: 値はすべて Unity 左手系（VMD の X・Z 両反転は生成側で済ませる）。
    //
    // ■ targetKind（トラックの解決方法＝適用規約）
    //   "boneName" : モデル骨名で直接解決（VMD/MMD 由来）。VMD 直接適用（リターゲットなし）。
    //   "path"     : Unity Transform パス末尾で対応表解決（二次骨など）。
    //   "humanoid" : Humanoid 正準名で対応表解決（baked 本体ボーン／ルート）。
    //
    // ■ Humanoid 固有（案A: 本形式に含める）
    //   muscles / body / bakedBones を保持する。sourceRest（バインドポーズ）は
    //   フィールドとして持つが、通常は空のまま外部 UnityBone CSV v2 を優先する。
    // ================================================================
    [Serializable]
    public class MotionClipDTO
    {
        /// <summary>形式識別子の正本。</summary>
        public const string FormatName = "PolyLingMotion";

        /// <summary>この実装が書き出す版。</summary>
        public const int CurrentVersion = 2;

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

        /// <summary>ルート（body）姿勢トラック。未使用時は null。</summary>
        public MotionTrackDTO body = null;

        /// <summary>焼いた本体ボーンのローカル回転トラック（targetKind = "humanoid"）。</summary>
        public List<MotionTrackDTO> bakedBones = new List<MotionTrackDTO>();

        /// <summary>ソース rest（バインドポーズ）。通常は空＝外部 UnityBone CSV v2 を優先。</summary>
        public List<MotionSourceRestDTO> sourceRest = new List<MotionSourceRestDTO>();

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

        /// <summary>解決方法。"boneName" / "path" / "humanoid"。</summary>
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

    // ----------------------------------------------------------------
    // ソース rest（バインドポーズ）。外部 UnityBone CSV v2 の代替保持枠。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionSourceRestDTO
    {
        /// <summary>Humanoid 正準名。</summary>
        public string humanoid;

        /// <summary>rest 位置 [x, y, z]。</summary>
        public float[] pos;

        /// <summary>rest ワールド回転 [x, y, z, w]。</summary>
        public float[] restW;

        /// <summary>rest ローカル回転 [x, y, z, w]。未使用時は null。</summary>
        public float[] restL;
    }
}
