// Assets/Editor/PolyLing.SelectionSets.cs
// 選択セット公開API — PolyLingCoreへの委譲ラッパー
// ロジックは Runtime/Poly_Ling_Main/Core/MainCore/PolyLingCore_Selection.cs に移管済み

using System.Collections.Generic;

public partial class PolyLing
{
    public bool LoadSelectionSetByName(string setName) =>
        _core.LoadSelectionSetByName(setName);

    public List<string> GetSelectionSetNames() =>
        _core.GetSelectionSetNames();
}
