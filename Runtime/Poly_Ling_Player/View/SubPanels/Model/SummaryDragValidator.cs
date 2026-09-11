// SummaryDragValidator.cs
// 概要ツリーの D&D 検証（MeshListSubPanel から分離）。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置（MeshListSubPanel.cs と同じ名前空間。MeshListSubPanel.cs から分割）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Diagnostics;
using UIList.UIToolkitExtensions;
using PlayerIoUiKit        = Poly_Ling.Player.PlayerIoUiKit;
using PlayerUiPrefs        = Poly_Ling.Player.PlayerUiPrefs;
using ObjectMoveSettings   = Poly_Ling.Tools.ObjectMoveSettings;
using ParameterLimits      = Poly_Ling.Core.ParameterLimits;
using RecentPaths          = Poly_Ling.Core.RecentPaths;
using PartsDictionaryPath  = Poly_Ling.Core.PartsDictionaryPath;
using MeshRenameCsvHelper  = Poly_Ling.UI.MeshRenameCsvHelper;

namespace Poly_Ling.MeshListV2
{
    public class SummaryDragValidator : IDragDropValidator<SummaryTreeAdapter>
    {
        public bool CanDrag(SummaryTreeAdapter item) => true;
        public bool CanDrop(SummaryTreeAdapter dragged, SummaryTreeAdapter target, DropPosition position) => true;
    }
}
