// Runtime/Poly_Ling_Main/Core/Serialization/WorkAxisDTO.cs
// 作業用ローカル軸 (WorkAxisContext) のシリアライズ用データ構造。
//
// ModelDTO.workAxis に保持され、.mfproj (JSON) と workaxis.csv (フォルダ形式) の
// 両方で同じ値を書き出す（規約4: CSV/JSON 対称）。
//
// 値はすべて Unity ワールド座標系のまま保存する。WorkPlaneDTO と同じく
// 変換は行わない。

using System;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// 作業用ローカル軸のDTO。
    /// </summary>
    [Serializable]
    public class WorkAxisDTO
    {
        /// <summary>原点（ワールド座標）[x, y, z]</summary>
        public float[] origin;

        /// <summary>回転（クォータニオン）[x, y, z, w]</summary>
        public float[] rotation;

        /// <summary>ギズモ表示</summary>
        public bool isVisible = true;

        public static WorkAxisDTO CreateDefault()
        {
            return new WorkAxisDTO
            {
                origin    = new float[] { 0, 0, 0 },
                rotation  = new float[] { 0, 0, 0, 1 },
                isVisible = true
            };
        }
    }
}
