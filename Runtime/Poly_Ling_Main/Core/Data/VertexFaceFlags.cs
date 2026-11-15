// VertexFaceFlags.cs
// 頂点・面のフラグ定義（MeshObject.cs から分離）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（MeshObject.cs と同じ名前空間。MeshObject.cs から分割）

using Poly_Ling.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.Data
{
    // ============================================================
    // フラグ定義
    // ============================================================

    /// <summary>
    /// 頂点フラグ（永続的な属性）
    /// </summary>
    [Flags]
    public enum VertexFlags : byte
    {
        /// <summary>フラグなし</summary>
        None = 0,

        /// <summary>ミラー平面上（中央頂点）</summary>
        OnMirrorPlane = 1 << 0,

        /// <summary>ミラー操作で生成された頂点</summary>
        MirrorGenerated = 1 << 1,

        /// <summary>編集ロック</summary>
        Locked = 1 << 2,

        /// <summary>補助点（表示用・非メッシュ）</summary>
        Auxiliary = 1 << 3,

        // 将来の拡張用に 4-7 を予約
    }

    /// <summary>
    /// 面フラグ（永続的な属性）
    /// </summary>
    [Flags]
    public enum FaceFlags : byte
    {
        /// <summary>フラグなし</summary>
        None = 0,

        /// <summary>ミラー操作で生成された面</summary>
        MirrorGenerated = 1 << 0,

        /// <summary>補助線/補助面</summary>
        Auxiliary = 1 << 1,

        /// <summary>非表示</summary>
        Hidden = 1 << 2,

        /// <summary>
        /// 曲線で見せる弦（ハンドルを持つ線分群の区間の 2 頂点の面）。線は描かず線分としても当てないが、
        /// 非表示（Hidden）とは別で、端点の頂点の表示・当たり判定は折れ線の弦と同じに扱う。
        /// 付け外しは LineGroupEditOps.SyncChordVisibility だけが行う。
        /// </summary>
        CurveChord = 1 << 3,

        // 将来の拡張用に 4-7 を予約
    }
}
