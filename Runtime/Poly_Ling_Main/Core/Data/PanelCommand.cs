// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される
//
// このファイルは基底クラスだけを持つ。各コマンドは分類ごとに同じフォルダの
// PanelCommand.<分類>.cs にある（名前空間はすべて Poly_Ling.Data）。
//   Selection / MeshAttribute / MeshList / Bone / Morph / Normal / Vrm / Blend / UvMaterial /
//   Transform / Skin / Deform / SpringBone / SpringBoneLadder / Mirror / Topology / TopologyTargeted /
//   Primitive / PrimitiveBelt / UndoRedo / FileIO / ToolConfirm / Query

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // UV投影方式
    // ================================================================

    public enum ProjectionType
    {
        PlanarXY,
        PlanarXZ,
        PlanarYZ,
        Box,
        Cylindrical,
        Spherical,
        /// <summary>ビューからの投影（画面に写った位置から UV を決める）。</summary>
        View
    }

    /// <summary>ビューからの投影で、画面上の何を UV の 0〜1 にするか。</summary>
    public enum ViewProjectionFrame
    {
        /// <summary>そのビューに今表示されている下絵画像の矩形。</summary>
        Underlay,
        /// <summary>ビュー全体。</summary>
        Viewport,
        /// <summary>投影した頂点の範囲。</summary>
        Bounds
    }
    public abstract class PanelCommand
    {
        public int ModelIndex { get; }
        protected PanelCommand(int modelIndex) { ModelIndex = modelIndex; }
    }
}
