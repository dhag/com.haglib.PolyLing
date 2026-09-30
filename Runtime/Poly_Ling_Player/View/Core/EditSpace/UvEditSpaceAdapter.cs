// UvEditSpaceAdapter.cs
// 作業空間の UV アダプタ。代理と元メッシュの対応（UvEditSpaceBinding）を持ち、
// 変更の有無・取消し・反映を UvEditSpaceOps へ渡す。
// Runtime/Poly_Ling_Player/View/Core/EditSpace/ に配置
//
// 【控えは取消しで作り直す】
//   取消しは今の元メッシュから代理を作り直すので、対応の控え（Binding）も作り直したものに替える。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.EditSpace;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public sealed class UvEditSpaceAdapter : IEditSpaceAdapter
    {
        /// <summary>代理と元メッシュの対応。</summary>
        public UvEditSpaceBinding Binding { get; private set; }

        public UvEditSpaceAdapter(UvEditSpaceBinding binding) { Binding = binding; }

        public string Kind        => "uv";
        public string DisplayName => "UV";
        public bool   TemporaryProxy => true;
        public EditSpacePlaneConstraint PlaneConstraint => EditSpacePlaneConstraint.Required;
        public EditSpaceBackdropSource  BackdropSource  => EditSpaceBackdropSource.SourceMaterialTexture;
        public Vector2 DisplayUnitScale => Binding != null && Binding.ScaleU > 0f && Binding.ScaleV > 0f
            ? Binding.Scale : Vector2.one;

        public bool HasChanges(MeshContext source, MeshContext proxy)
            => UvEditSpaceOps.HasChanges(source?.MeshObject, proxy?.MeshObject, Binding);

        public string Reset(MeshContext source, MeshContext proxy)
        {
            var b = Binding;
            string err = UvEditSpaceOps.ResetProxy(source?.MeshObject, proxy?.MeshObject, ref b);
            if (err != null) return err;
            Binding = b;
            // 頂点・面を作り直したので、古い番号の選択は捨てる。
            proxy?.Selection?.ClearAll();
            return null;
        }

        /// <summary>
        /// 作業倍率を (sU, sV) に替え、今の元メッシュから代理を作り直す（画像の縦横比の切替）。
        /// 未反映の変更の有無は呼び出し側で確かめること（作り直すと代理の編集は消える）。
        /// </summary>
        public string Rescale(MeshContext source, MeshContext proxy, Vector2 scale)
        {
            var b = Binding;
            string err = UvEditSpaceOps.ResetProxy(source?.MeshObject, proxy?.MeshObject, ref b, scale);
            if (err != null) return err;
            Binding = b;
            proxy?.Selection?.ClearAll();
            return null;
        }

        public string Apply(MeshContext source, MeshContext proxy,
                            List<int> problemSourceFaces, List<int> problemProxyFaces)
            => UvEditSpaceOps.ApplyProxy(source?.MeshObject, proxy?.MeshObject, Binding,
                                         problemSourceFaces, problemProxyFaces);
    }
}
