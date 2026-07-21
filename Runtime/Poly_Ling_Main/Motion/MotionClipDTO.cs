using System;
using System.Collections.Generic;

namespace Poly_Ling.Motion
{
    // ================================================================
    // MotionClipDTO （統合モーション中間データ・純POCO）
    // ----------------------------------------------------------------
    // VMD 系（MotionDTO）と Unity クリップ系（UnityClipDTO）を 1 本に統合した
    // 新形式。キー時刻はすべて float 秒（t）。値はすべて Unity 左手系。
    // UnityEngine 型をフィールドに一切持たない（int / float / float[] / string / bool のみ）。
    //
    // ■ 移行方針（恒久メモ）
    //   旧 MotionDTO / UnityClipDTO と旧テストパネルは比較用に残置し、
    //   本形式へは片方向コンバータ（MotionClipConverters）で順次移行する。
    //
    // ■ 形式の基本方針
    //   - スパース: 各トラックは「秒付きキーの列」。密な毎フレーム配列ではない。
    //   - キー内チャンネル: 1 キーに pos[3] / rot[4] / scl[3] を任意で持つ（未使用は null）。
    //   - 補間: キー間は線形補間（pos=Lerp / rot=Slerp）。接線は保持しない。
    //   - 座標系: 値はすべて Unity 左手系（右手系→左手系変換は生成側で済ませる）。
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
        /// <summary>形式識別子。</summary>
        public string format = "PolyLingMotion";

        /// <summary>形式バージョン。</summary>
        public int version = 1;

        /// <summary>クリップ名。</summary>
        public string name;

        /// <summary>フレームレート（参考値）。キー時刻 t は秒。</summary>
        public float frameRate = 30f;

        /// <summary>値の空間。"local" = ボーンローカル変換（Unity 左手系）。</summary>
        public string space = "local";

        /// <summary>ループ設定。</summary>
        public bool loop = false;

        /// <summary>ボーントラック（targetKind = "boneName" または "path"）。</summary>
        public List<MotionTrackDTO> bones = new List<MotionTrackDTO>();

        /// <summary>モーフ（ブレンドシェイプ）トラック。</summary>
        public List<MotionScalarTrackDTO> morphs = new List<MotionScalarTrackDTO>();

        // ---- 以下 Humanoid 固有（案A）----

        /// <summary>Muscle 名別の重みキー列。</summary>
        public List<MotionScalarTrackDTO> muscles = new List<MotionScalarTrackDTO>();

        /// <summary>ルート（body）姿勢トラック。未使用時は null。</summary>
        public MotionTrackDTO body = null;

        /// <summary>焼いた本体ボーンのローカル回転トラック（targetKind = "humanoid"）。</summary>
        public List<MotionTrackDTO> bakedBones = new List<MotionTrackDTO>();

        /// <summary>ソース rest（バインドポーズ）。通常は空＝外部 UnityBone CSV v2 を優先。</summary>
        public List<MotionSourceRestDTO> sourceRest = new List<MotionSourceRestDTO>();
    }

    // ----------------------------------------------------------------
    // ボーントラック: 識別子＋解決方法＋キー列。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionTrackDTO
    {
        /// <summary>トラック識別子（骨名 / Transform パス / Humanoid 正準名）。</summary>
        public string id;

        /// <summary>解決方法。"boneName" / "path" / "humanoid"。</summary>
        public string targetKind = "path";

        /// <summary>キー列。秒昇順。</summary>
        public List<MotionKeyDTO> keys = new List<MotionKeyDTO>();
    }

    // ----------------------------------------------------------------
    // ボーンキー: t（秒）と、任意の pos / rot / scl。
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
    }

    // ----------------------------------------------------------------
    // スカラートラック（モーフ / マッスル共用）。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionScalarTrackDTO
    {
        /// <summary>名前（モーフ名 / Muscle 名）。</summary>
        public string name;

        /// <summary>値キー。秒昇順。</summary>
        public List<MotionScalarKeyDTO> keys = new List<MotionScalarKeyDTO>();
    }

    /// <summary>スカラーキー。t = 時刻（秒）、v = 値。</summary>
    [Serializable]
    public class MotionScalarKeyDTO
    {
        public float t;
        public float v;
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
