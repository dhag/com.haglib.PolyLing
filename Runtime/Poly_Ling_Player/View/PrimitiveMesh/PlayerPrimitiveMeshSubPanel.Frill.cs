// PlayerPrimitiveMeshSubPanel.Frill.cs
// 図形生成サブパネル：フリル（高度な図形）。
// 基準ベルトの取り込み・自動検索・断面プロファイル編集は
// PlayerPrimitiveMeshSubPanel.BeltProfile.cs の共通部を使う。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Frill;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private FrillParams        _frillP     = FrillParams.Default;
        private List<BeltSnapshot> _frillBelts = new List<BeltSnapshot>();
        private MeshSourcePick     _frillPick  = new MeshSourcePick();
        private BeltSplineOption   _frillSpline = new BeltSplineOption();

        private BeltProfileEdit _frillEdit = new BeltProfileEdit
        {
            ClosedLoop     = false,
            DefaultProfile = DefaultFrillProfile,
            UndoStackId    = "PlayerEdit/FrillProfileEdit",
            UndoTitle      = "フリル断面編集",
            BgSectionLabel = "フリル下絵",
        };

        private Label _frillInfoLabel;

        /// <summary>フリルの既定断面。1ステップぶんの平坦な区間（＝基準ベルトと同一形状）。</summary>
        private static List<Vector2> DefaultFrillProfile()
            => new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f) };

        // ================================================================
        // UI
        // ================================================================

        private void BuildFrillUI(VisualElement c)
        {
            c.Add(SL(T("Frill")));
            c.Add(NF(() => _frillP.MeshName, v => _frillP.MeshName = v));

            // ── 基準ベルト（手動取り込み） ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(PlayerIoUiKit.SectionLabel(T("FrillBase")));

            var hint = new Label(T("FrillBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            c.Add(hint);

            c.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_frillBelts);
                RefreshFrillInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(c, _frillPick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            c.Add(autoHint);

            c.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_frillBelts, _frillPick.Current);
                RefreshFrillInfo();
            }));

            _frillInfoLabel = new Label(BeltsInfoText(_frillBelts));
            _frillInfoLabel.style.fontSize   = 10;
            _frillInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _frillInfoLabel.style.marginTop  = 2;
            c.Add(_frillInfoLabel);

            BuildBeltSplineUI(c, _frillSpline);

            BuildBeltProfileEditor(_profileEditorContainer, _frillEdit, T("FrillAxisHint"));
        }

        private void RefreshFrillInfo()
        {
            if (_frillInfoLabel != null) _frillInfoLabel.text = BeltsInfoText(_frillBelts);
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>各基準ベルトのステップに同じ波形を繰り返す。未取込なら空メッシュ。</summary>
        private MeshObject GenerateFrillMesh()
        {
            EnsureBeltProfile(_frillEdit);

            var mo = new MeshObject(_frillP.MeshName);
            foreach (var belt in _frillBelts)
            {
                if (belt == null || !belt.HasData) continue;
                var b = ApplyBeltSpline(belt, _frillSpline);
                var part = FrillMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    _frillEdit.Points, _frillP.MeshName);
                AppendMesh(mo, part);
            }
            return mo;
        }
    }
}
