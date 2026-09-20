// LineHandle.cs
// 線分群の点ごとの方向ハンドル（ベジェ）と、その拘束。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【どこに持つか】
//   LineGroup.PointHandles に Order と同順・同数で持つ（頂点ではなく線分群の点ごと）。
//   同じ頂点が複数の線分群に属する（枝分かれの起点）ため、ハンドルは群ごとに要る。
//   PointHandles が空の群は折れ線（ハンドル無し）とみなす。
//
// 【ハンドルの持ち方】
//   InOffset / OutOffset は点の位置からのずれ（ローカル座標）。点を動かすとハンドルも付いて動く。
//   入り = 前の点の側、出 = 後の点の側。
//
// 【拘束】入りと出のそれぞれに、向きと長さを独立に持たせる。
//   向き：Free（自由）/ Chord（その側の隣の点を向く）/ Tangent（反対側のハンドルと一直線）
//   長さ：Free（自由）/ ChordRatio（その側の弦の長さ × Ratio）/ EqualOpposite（反対側と等長）
//         / Group（LineGroup.LengthGroups の共有値。群の中だけで共有）
//   解くのは LineHandleSolver。

using System;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>ハンドルの向きの拘束。</summary>
    public enum HandleDirection : byte
    {
        /// <summary>自由。</summary>
        Free = 0,
        /// <summary>その側の隣の点を向く（弦の上）。</summary>
        Chord = 1,
        /// <summary>反対側のハンドルと一直線（点をはさんで逆向き）。</summary>
        Tangent = 2,
    }

    /// <summary>ハンドルの長さの拘束。</summary>
    public enum HandleLength : byte
    {
        /// <summary>自由。</summary>
        Free = 0,
        /// <summary>その側の弦の長さ × Ratio。</summary>
        ChordRatio = 1,
        /// <summary>反対側のハンドルと等長。</summary>
        EqualOpposite = 2,
        /// <summary>長さの組（LineGroup.LengthGroups）の共有値。</summary>
        Group = 3,
    }

    /// <summary>ハンドル 1 本ぶんの拘束。</summary>
    [Serializable]
    public struct HandleConstraint
    {
        public HandleDirection Direction;
        public HandleLength    Length;
        /// <summary>Length == ChordRatio のときの比率。</summary>
        public float Ratio;
        /// <summary>Length == Group のときの長さの組の ID。</summary>
        public int   LengthGroupId;

        public static HandleConstraint Default => new HandleConstraint
        {
            Direction = HandleDirection.Free,
            Length    = HandleLength.Free,
            Ratio     = 1f / 3f,
            LengthGroupId = 0,
        };
    }

    /// <summary>線分群の点 1 つぶんのハンドル（入り・出）と拘束。</summary>
    [Serializable]
    public class LinePointHandle
    {
        /// <summary>入りハンドル（前の点の側）の、点からのずれ。</summary>
        public Vector3 InOffset;
        /// <summary>出ハンドル（後の点の側）の、点からのずれ。</summary>
        public Vector3 OutOffset;

        public HandleConstraint InConstraint  = HandleConstraint.Default;
        public HandleConstraint OutConstraint = HandleConstraint.Default;

        /// <summary>ずれ 0・拘束は既定（折れ線と同じ形）。</summary>
        public static LinePointHandle CreateDefault() => new LinePointHandle();

        public LinePointHandle Clone() => new LinePointHandle
        {
            InOffset      = InOffset,
            OutOffset     = OutOffset,
            InConstraint  = InConstraint,
            OutConstraint = OutConstraint,
        };
    }

    /// <summary>長さの組（線分群の中だけで共有する長さ）。</summary>
    [Serializable]
    public class LineLengthGroup
    {
        public int   Id;
        public float Length;

        public LineLengthGroup Clone() => new LineLengthGroup { Id = Id, Length = Length };
    }
}
