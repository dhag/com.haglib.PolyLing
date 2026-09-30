// ProfileEditSpaceAdapter.cs
// 作業空間の断面系アダプタ（回転体・2D押し出し・フリル A/B・パイプ）。
// Runtime/Poly_Ling_Player/View/Core/EditSpace/ に配置
//
// 【元データと代理】（PolyLing_UV_Billboard_Design.md 4.3・7 章）
//   元データは図形生成パネルが持つ断面。代理は「反映（→メッシュ）」で作った常設の線分オブジェクト。
//   反映 = 代理から今の「取り込み」と同じ読み方で断面を作り、パネルの断面へ入れる（パネルの Undo に残る）。
//   取消し = パネルの断面から線分を作り直し、代理の中身を置き換える。
//   読み方・作り方はパネル（PlayerPrimitiveMeshSubPanel.EditSpace.cs）が持つ。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.EditSpace;

namespace Poly_Ling.Player
{
    public sealed class ProfileEditSpaceAdapter : IEditSpaceAdapter
    {
        /// <summary>断面を持つ図形生成パネル。</summary>
        public PlayerPrimitiveMeshSubPanel Panel { get; }

        /// <summary>断面の種類。</summary>
        public ProfileEditKind ProfileKind { get; }

        /// <summary>取り込み元として控える代理の索引を引く口（オブジェクトグループの作り直し用）。</summary>
        private readonly System.Func<MeshContext, int> _indexOf;

        public ProfileEditSpaceAdapter(PlayerPrimitiveMeshSubPanel panel, ProfileEditKind kind,
                                       System.Func<MeshContext, int> indexOf)
        {
            Panel       = panel;
            ProfileKind = kind;
            _indexOf    = indexOf;
        }

        public string Kind           => PlayerPrimitiveMeshSubPanel.KindName(ProfileKind);
        public string DisplayName    => PlayerPrimitiveMeshSubPanel.KindDisplayName(ProfileKind);
        public bool   TemporaryProxy => false;
        public EditSpacePlaneConstraint PlaneConstraint => EditSpacePlaneConstraint.DefaultOn;
        public EditSpaceBackdropSource  BackdropSource  => EditSpaceBackdropSource.UserImage;
        public UnityEngine.Vector2 DisplayUnitScale => UnityEngine.Vector2.one;

        public bool HasChanges(MeshContext source, MeshContext proxy)
            => Panel != null && proxy?.MeshObject != null
            && !Panel.ProfileMatchesMesh(ProfileKind, proxy.MeshObject);

        public string Reset(MeshContext source, MeshContext proxy)
        {
            if (Panel == null) return "図形生成パネルがありません";
            if (proxy?.MeshObject == null) return "代理がありません";
            var mo = Panel.BuildProfileLineMesh(ProfileKind, proxy.Name, out string error);
            if (mo == null) return error;

            // MeshObject ごと差し替えない。ビルボード・表示の設定など、形以外の属性は
            // MeshObject が持っている（MeshContext.Billboard の実体も MeshObject）。
            // 頂点・面・線分群だけを入れ替える。
            var dst = proxy.MeshObject;
            dst.Clear();
            dst.Vertices.AddRange(mo.Vertices);
            dst.Faces.AddRange(mo.Faces);
            dst.LineGroups = mo.LineGroups ?? new List<LineGroup>();
            dst.InvalidatePositionCache();
            return null;
        }

        public string Apply(MeshContext source, MeshContext proxy,
                            List<int> problemSourceFaces, List<int> problemProxyFaces)
        {
            if (Panel == null) return "図形生成パネルがありません";
            if (proxy?.MeshObject == null) return "代理がありません";
            return Panel.ApplyProfileFromMesh(ProfileKind, proxy.MeshObject,
                _indexOf != null ? _indexOf(proxy) : -1, "作業空間 反映");
        }
    }
}
