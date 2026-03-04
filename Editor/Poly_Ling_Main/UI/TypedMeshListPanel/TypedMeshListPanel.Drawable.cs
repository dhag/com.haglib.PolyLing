// TypedMeshListPanel.Drawable.cs
// Drawable - symmetry toggle

using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public partial class TypedMeshListPanel
    {
        private void OnSymmetryToggle(TypedTreeAdapter adapter)
        {
            if (adapter.IsBakedMirror)
            {
                Log("ベイクドミラーは対称設定を変更できません");
                return;
            }

            int index = adapter.MasterIndex;
            if (index < 0) return;

            int newMirrorType = (adapter.MirrorType + 1) % 4;
            ToolCtx?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, MirrorType = newMirrorType }
            });

            string[] mirrorNames = { "なし", "X軸", "Y軸", "Z軸" };
            Log($"対称: {adapter.DisplayName} → {mirrorNames[newMirrorType]}");
        }
    }
}
