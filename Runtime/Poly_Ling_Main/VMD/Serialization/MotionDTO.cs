using System;
using System.Collections.Generic;

namespace Poly_Ling.VMD.Serialization
{
    // ================================================================
    // MotionDTO （モーション中間データ・純POCO）
    // ----------------------------------------------------------------
    // VMD などのモーションを、Unity空間・スパースキー形式で表す言語間データ契約。
    // UnityEngine 型をフィールドに一切持たない（float / float[] / string のみ）。
    // よって Newtonsoft でそのまま JSON 化でき、Python/JS でも同じ構造をミラーできる。
    //
    // ■ 形式の基本方針（恒久メモ）
    //   - スパース: 各チャンネルは「フレーム番号付きキーの列」。密な毎フレーム配列ではない。
    //   - チャンネル別: 回転(rot)・位置(pos)・モーフ重み(w) を別々のキー列で持つ。
    //   - 補間: キー間は当面チャンネルごとに線形補間する想定（補間情報は持たない）。
    //           将来ベジェ厳密化する場合はキーに任意の補間フィールドを足して拡張する
    //           （形式は分裂させない）。
    //   - 座標系: 値はすべて Unity 左手系。VMD(右手系)からの変換は生成側(Serializer)で行う。
    //   - ボーン名: PolyLing モデルの骨名(=VMDボーン名)をそのままトラック名にする。
    //     Humanoid/アバターへの名前対応は将来、消費側で行う。
    //
    // ■ MotionTimeline との関係
    //   既存 MotionTimeline のネイティブ表現（チャンネル別の {f, v} キー列）と整合する
    //   最小形。MotionTimeline の clip/group/bundle 等の編集構造は持たず、
    //   「トラック→チャンネル→キー」だけに絞った素のモーション契約。
    // ================================================================
    [Serializable]
    public class MotionDTO
    {
        /// <summary>フレームレート（VMD は 30）。time = frame / fps。</summary>
        public float fps = 30f;

        /// <summary>
        /// 値の空間。"local" = 各値はボーンローカル変換（Unity左手系）で、
        /// モデル骨へ localRotation/localPosition として適用する想定。
        /// （bind-pose やローカル軸の補正が要る場合はクリップ生成側で行う。）
        /// </summary>
        public string space = "local";

        /// <summary>ボーントラック一覧。</summary>
        public List<MotionBoneTrackDTO> bones = new List<MotionBoneTrackDTO>();

        /// <summary>モーフ（ブレンドシェイプ）トラック一覧。</summary>
        public List<MotionMorphTrackDTO> morphs = new List<MotionMorphTrackDTO>();
    }

    // ----------------------------------------------------------------
    // ボーントラック: 1 ボーン分の回転・位置キー列。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionBoneTrackDTO
    {
        /// <summary>モデル骨名（VMD ボーン名そのまま）。</summary>
        public string name;

        /// <summary>回転キー（クォータニオン）。フレーム昇順。</summary>
        public List<RotKeyDTO> rot = new List<RotKeyDTO>();

        /// <summary>位置キー。位置を使わないボーンでは空。フレーム昇順。</summary>
        public List<PosKeyDTO> pos = new List<PosKeyDTO>();
    }

    // ----------------------------------------------------------------
    // モーフトラック: 1 モーフ分の重みキー列。
    // ----------------------------------------------------------------
    [Serializable]
    public class MotionMorphTrackDTO
    {
        /// <summary>モーフ名。</summary>
        public string name;

        /// <summary>重みキー（0..1）。フレーム昇順。</summary>
        public List<WeightKeyDTO> w = new List<WeightKeyDTO>();
    }

    // ----------------------------------------------------------------
    // キー型（端的フィールド: f=フレーム番号, v=値）
    // ----------------------------------------------------------------

    /// <summary>回転キー。v = [x, y, z, w]（クォータニオン, Unity左手系）。</summary>
    [Serializable]
    public class RotKeyDTO
    {
        public int f;
        public float[] v;
    }

    /// <summary>位置キー。v = [x, y, z]（Unity左手系）。</summary>
    [Serializable]
    public class PosKeyDTO
    {
        public int f;
        public float[] v;
    }

    /// <summary>重みキー。v = 重み（0..1）。</summary>
    [Serializable]
    public class WeightKeyDTO
    {
        public int f;
        public float v;
    }
}
