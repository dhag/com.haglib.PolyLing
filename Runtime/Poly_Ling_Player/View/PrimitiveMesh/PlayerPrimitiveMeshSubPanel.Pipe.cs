// PlayerPrimitiveMeshSubPanel.Pipe.cs
// 図形生成サブパネル：パイプ（高度な図形）。
// 基準ベルトの取り込み・自動検索・断面プロファイル編集は
// PlayerPrimitiveMeshSubPanel.BeltProfile.cs の共通部を使う。
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Pipe;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 状態
        // ================================================================

        private PipeParams         _pipeP     = PipeParams.Default;
        private List<BeltSnapshot> _pipeBelts = new List<BeltSnapshot>();
        private MeshSourcePick     _pipePick  = new MeshSourcePick();
        private BeltSplineOption   _pipeSpline = new BeltSplineOption();

        private BeltProfileEdit _pipeEdit = new BeltProfileEdit
        {
            ClosedLoop     = true,
            DefaultProfile = DefaultPipeProfile,
            UndoStackId    = "PlayerEdit/PipeProfileEdit",
            UndoTitle      = "パイプ断面編集",
            BgSectionLabel = "パイプ下絵",
        };

        private Label _pipeInfoLabel;

        /// <summary>パイプの既定断面。rung 長で正規化した正方形の閉ループ。</summary>
        private static List<Vector2> DefaultPipeProfile()
            => new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };

        // ================================================================
        // UI
        // ================================================================

        private void BuildPipeUI(VisualElement c)
        {
            c.Add(SL(T("Pipe")));
            c.Add(NF(() => _pipeP.MeshName, v => _pipeP.MeshName = v));

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
                ImportBeltFromMesh(_pipeBelts);
                RefreshPipeInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(c, _pipePick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            c.Add(autoHint);

            c.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_pipeBelts, _pipePick.Current);
                RefreshPipeInfo();
            }));

            _pipeInfoLabel = new Label(BeltsInfoText(_pipeBelts));
            _pipeInfoLabel.style.fontSize   = 10;
            _pipeInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _pipeInfoLabel.style.marginTop  = 2;
            c.Add(_pipeInfoLabel);

            c.Add(TR(T("PipeCapEnds"), () => _pipeP.CapEnds, v => { _pipeP.CapEnds = v; D(); }));

            BuildBeltSplineUI(c, _pipeSpline);

            BuildBeltProfileEditor(_profileEditorContainer, _pipeEdit, T("PipeAxisHint"));
        }

        private void RefreshPipeInfo()
        {
            if (_pipeInfoLabel != null) _pipeInfoLabel.text = BeltsInfoText(_pipeBelts);
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>各基準ベルトの rung ごとに断面を置いて rung 間をつなぐ。未取込なら空メッシュ。</summary>
        private MeshObject GeneratePipeMesh()
        {
            EnsureBeltProfile(_pipeEdit);

            var mo = new MeshObject(_pipeP.MeshName);
            foreach (var belt in _pipeBelts)
            {
                if (belt == null || !belt.HasData) continue;
                var b = ApplyBeltSpline(belt, _pipeSpline);
                var part = PipeMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    _pipeEdit.Points, _pipeEdit.ClosedLoop, _pipeP.CapEnds,
                    b.StartPoint, b.EndPoint,
                    _pipeP.MeshName);
                AppendMesh(mo, part);
            }
            return mo;
        }
    }
}
