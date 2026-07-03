using System;
using System.Collections.Generic;

namespace Poly_Ling.UnityClip
{
    // ================================================================
    // UnityClipDTO （Unityモーションクリップ中間データ・純POCO）
    // ----------------------------------------------------------------
    // UnityEngine.AnimationClip を言語間で持ち運ぶための中間契約。
    // UnityEngine 型をフィールドに一切持たない（int / float / float[] / string のみ）。
    // よって Newtonsoft でそのまま JSON 化でき、Python/JS でも同じ構造をミラーできる。
    //
    // ■ VMD 系 MotionDTO との違い（恒久メモ）
    //   MotionDTO は「MMD 骨名トラック ＋ rot/pos」の VMD 用中間形式。
    //   本 DTO は AnimationClip 用で、Generic の「Transform パス階層 ＋ 位置/回転/スケール」を
    //   保持する。両者は目的が異なるため分離する（MotionDTO へは合流させない）。
    //
    // ■ 形式の基本方針（恒久メモ）
    //   - スパース: 各ボーンは「フレーム番号付きキーの列」。密な毎フレーム配列ではない。
    //   - キー内チャンネル: 1 キーに pos[3] / rot[4] / scl[3] を任意で持つ。
    //     使わないチャンネルは null（例: 位置を持たないボーンは pos = null）。
    //   - 補間: キー間は当面線形補間。接線情報は保持しない
    //           （AnimationClip の接線は抽出時に捨てる）。
    //   - 座標系: 値はすべて Unity 左手系。AnimationClip は元から Unity 左手系のため
    //             座標変換は行わない（VMD のような右手系→左手系変換は不要）。
    //   - フレーム番号: f はフレーム番号。time = f / frameRate。
    //
    // ■ clipType
    //   "Generic"  : bones（Transform パス階層カーブ）を使う。
    //   "Humanoid" : muscles / body を使う想定。※今回は器のみ（抽出は未実装）。
    // ================================================================
    [Serializable]
    public class UnityClipDTO
    {
        /// <summary>形式識別子。</summary>
        public string format = "UnityMotionSamples";

        /// <summary>形式バージョン。</summary>
        public int version = 1;

        /// <summary>クリップ名（AnimationClip.name）。</summary>
        public string name;

        /// <summary>"Generic" または "Humanoid"。</summary>
        public string clipType = "Generic";

        /// <summary>フレームレート（AnimationClip.frameRate）。time = f / frameRate。</summary>
        public float frameRate = 30f;

        /// <summary>ループ設定（AnimationClip.isLooping）。</summary>
        public bool loop = false;

        /// <summary>Generic 用: Transform パス別のキー列。</summary>
        public List<UnityBoneTrackDTO> bones = new List<UnityBoneTrackDTO>();

        // ---- 以下 Humanoid 用（今回は器のみ・抽出未実装）----

        /// <summary>Humanoid 用: Muscle 名別の重みキー列。</summary>
        public List<UnityMuscleTrackDTO> muscles = new List<UnityMuscleTrackDTO>();

        /// <summary>Humanoid 用: ルート（body）姿勢キー列。未使用時は null。</summary>
        public UnityBodyTrackDTO body = null;
    }

    // ----------------------------------------------------------------
    // ボーントラック（Generic）: 1 Transform パス分のキー列。
    // ----------------------------------------------------------------
    [Serializable]
    public class UnityBoneTrackDTO
    {
        /// <summary>Transform パス（例: "Hips/Spine/RightUpperArm"）。ルートは ""。</summary>
        public string path;

        /// <summary>キー列。フレーム昇順。</summary>
        public List<UnityBoneKeyDTO> keys = new List<UnityBoneKeyDTO>();
    }

    // ----------------------------------------------------------------
    // ボーンキー（Generic）: f と、任意の pos / rot / scl。
    // ----------------------------------------------------------------
    [Serializable]
    public class UnityBoneKeyDTO
    {
        /// <summary>フレーム番号。</summary>
        public int f;

        /// <summary>位置 [x, y, z]（Unity左手系）。使わない場合は null。</summary>
        public float[] pos;

        /// <summary>回転 [x, y, z, w]（クォータニオン, Unity左手系）。使わない場合は null。</summary>
        public float[] rot;

        /// <summary>スケール [x, y, z]。使わない場合は null。</summary>
        public float[] scl;
    }

    // ----------------------------------------------------------------
    // Muscle トラック（Humanoid・今回は器のみ）。
    // ----------------------------------------------------------------
    [Serializable]
    public class UnityMuscleTrackDTO
    {
        /// <summary>Muscle 名（HumanTrait.MuscleName）。</summary>
        public string name;

        /// <summary>重みキー。フレーム昇順。</summary>
        public List<UnityWeightKeyDTO> w = new List<UnityWeightKeyDTO>();
    }

    /// <summary>重みキー。v = Muscle 値。</summary>
    [Serializable]
    public class UnityWeightKeyDTO
    {
        public int f;
        public float v;
    }

    // ----------------------------------------------------------------
    // Body トラック（Humanoid ルート・今回は器のみ）。
    // ----------------------------------------------------------------
    [Serializable]
    public class UnityBodyTrackDTO
    {
        /// <summary>ルート姿勢キー。フレーム昇順。</summary>
        public List<UnityBodyKeyDTO> keys = new List<UnityBodyKeyDTO>();
    }

    /// <summary>ルート姿勢キー。pos = [x,y,z]、rot = [x,y,z,w]。</summary>
    [Serializable]
    public class UnityBodyKeyDTO
    {
        public int f;
        public float[] pos;
        public float[] rot;
    }
}
