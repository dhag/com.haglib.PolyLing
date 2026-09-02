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
using Poly_Ling.PrimitiveMesh;

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
        private BeltOrientOption   _pipeOrient = new BeltOrientOption();

        private BeltProfileEdit _pipeEdit = new BeltProfileEdit
        {
            ClosedLoop     = true,
            DefaultProfile = DefaultPipeProfile,
            UndoStackId    = "PlayerEdit/PipeProfileEdit",
            UndoTitle      = "パイプ断面編集",
            BgSectionLabel = "パイプ下絵",
            CsvRecentKey   = "Primitive.Pipe.ProfileCsv",
            CsvDefaultName = "pipe_profile.csv",
            ObjectName     = "PipeProfile",
        };

        private Label _pipeInfoLabel;

        /// <summary>厚み付けの角処理(ベベル)UI 要素。</summary>
        private SolidifyUI _pipeSolidUI;

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
            c.Add(ShapeTitle(T("Pipe")));
            c.Add(NF(() => _pipeP.MeshName, v => _pipeP.MeshName = v));

            // ── 基準はしご（取り込み〜向きまでを1つのフォールドにまとめる） ──
            c.Add(PlayerIoUiKit.Divider());
            var baseFold = new Foldout { text = T("FrillBase"), value = true };
            baseFold.style.marginBottom = 4;
            var bc = baseFold.contentContainer;
            c.Add(baseFold);

            var hint = new Label(T("FrillBaseHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 2;
            bc.Add(hint);

            bc.Add(PlayerIoUiKit.WideBtn(T("ImportBelt"), () =>
            {
                ImportBeltFromMesh(_pipeBelts, false);
                RefreshPipeInfo();
            }));

            // ── 自動検索 ──
            BuildMeshSourceRow(bc, _pipePick, T("AutoDetectSource"));

            var autoHint = new Label(T("AutoDetectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 2;
            bc.Add(autoHint);

            bc.Add(PlayerIoUiKit.WideBtn(T("AutoDetectBelts"), () =>
            {
                AutoDetectBelts(_pipeBelts, _pipePick.Current, false);
                RefreshPipeInfo();
            }));

            _pipeInfoLabel = new Label(BeltsInfoText(_pipeBelts));
            _pipeInfoLabel.style.fontSize   = 10;
            _pipeInfoLabel.style.whiteSpace = WhiteSpace.Normal;
            _pipeInfoLabel.style.marginTop  = 2;
            bc.Add(_pipeInfoLabel);

            // ── はしごCSV ──
            BuildBeltCsvUI(bc, _pipeBelts,
                "Primitive.Pipe.BeltCsv", "pipe_belt.csv", RefreshPipeInfo);

            // ── はしごの向き ──
            BuildBeltOrientUI(bc, _pipeOrient);

            c.Add(TR(T("PipeCapEnds"), () => _pipeP.CapEnds, v => { _pipeP.CapEnds = v; D(); }));

            // ── 面の向き ──
            c.Add(TR(T("FlipFaces"), () => _pipeP.FlipFaces, v => { _pipeP.FlipFaces = v; D(); }));

            // ── 厚み付け ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SR(T("Thickness"), PipeParams.ThicknessMin, PipeParams.ThicknessMax, () => _pipeP.Thickness,
                v => { _pipeP.Thickness = v; D(); RefreshPipeSolidVis(); }));

            // 角処理(ベベル)UI は常時生成し、厚み/分割数に応じて表示切替する。
            var pipeSolid = new SolidifyUI
            {
                EdgeLabel = SL(T("EdgeSettings")),
                FrontSeg  = IR(T("FrontSegments"), PipeParams.EdgeSegmentsMin, PipeParams.EdgeSegmentsMax, () => _pipeP.SegmentsFront,
                               v => { _pipeP.SegmentsFront = v; D(); RefreshPipeSolidVis(); }),
                FrontSize = SR(T("EdgeSize"), PipeParams.EdgeSizeMin, PipeParams.EdgeSizeMax, () => _pipeP.EdgeSizeFront,
                               v => { _pipeP.EdgeSizeFront = v; D(); }),
                BackSeg   = IR(T("BackSegments"), PipeParams.EdgeSegmentsMin, PipeParams.EdgeSegmentsMax, () => _pipeP.SegmentsBack,
                               v => { _pipeP.SegmentsBack = v; D(); RefreshPipeSolidVis(); }),
                BackSize  = SR(T("EdgeSize"), PipeParams.EdgeSizeMin, PipeParams.EdgeSizeMax, () => _pipeP.EdgeSizeBack,
                               v => { _pipeP.EdgeSizeBack = v; D(); }),
                Inward    = TR(T("EdgeInward"), () => _pipeP.EdgeInward,
                               v => { _pipeP.EdgeInward = v; D(); }),
            };
            _pipeSolidUI = pipeSolid;
            c.Add(pipeSolid.EdgeLabel); c.Add(pipeSolid.FrontSeg); c.Add(pipeSolid.FrontSize);
            c.Add(pipeSolid.BackSeg);   c.Add(pipeSolid.BackSize); c.Add(pipeSolid.Inward);
            RefreshPipeSolidVis();

            BuildBeltSplineUI(c, _pipeSpline);

            BuildPivotXYZ(c,
                () => _pipeP.Pivot, v => { _pipeP.Pivot = v; D(); },
                PrimitiveMeshPostProcess.PivotMin, PrimitiveMeshPostProcess.PivotMax,
                new Vector3(0, -0.5f, 0), Vector3.zero, new Vector3(0, 0.5f, 0), out _);

            BuildBeltProfileEditor(_profileEditorContainer, _pipeEdit, T("PipeAxisHint"));
        }

        private void RefreshPipeInfo()
        {
            RefreshCreateButtonState();
            if (_pipeInfoLabel != null) _pipeInfoLabel.text = BeltsInfoText(_pipeBelts);
        }

        private void RefreshPipeSolidVis()
            => UpdateSolidifyVis(_pipeSolidUI, _pipeP.Thickness, _pipeP.SegmentsFront, _pipeP.SegmentsBack);

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>各基準ベルトの rung ごとに断面を置いて rung 間をつなぐ。未取込なら空メッシュ。</summary>
        private MeshObject GeneratePipeMesh()
        {
            EnsureBeltProfile(_pipeEdit);

            var mo = new MeshObject(_pipeP.MeshName);

            // 梯子1本＝パーツ1つ。全ベルトで同じカウンタを共有し、0 から連番にする。
            var partsIds = new Poly_Ling.PrimitiveMesh.PartsIdCounter();

            foreach (var belt in _pipeBelts)
            {
                if (belt == null || !belt.HasData) continue;
                var b = ApplyBeltSpline(ApplyBeltOrient(belt, _pipeOrient), _pipeSpline);
                var part = PipeMeshGenerator.Generate(
                    b.Left, b.Right, b.Closed, b.FlipWinding,
                    _pipeEdit.Points, _pipeEdit.ClosedLoop, _pipeP.CapEnds,
                    b.StartPoint, b.EndPoint,
                    _pipeP.MeshName, partsIds);
                part = ApplySolidify(part,
                    _pipeP.Thickness, _pipeP.SegmentsFront, _pipeP.SegmentsBack,
                    _pipeP.EdgeSizeFront, _pipeP.EdgeSizeBack, _pipeP.EdgeInward,
                    _pipeP.MeshName);
                Poly_Ling.Ops.MeshObjectAppendOps.Append(mo, part);
            }
            if (_pipeP.FlipFaces)
                Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.FlipFaces(mo);
            Poly_Ling.PrimitiveMesh.PrimitiveMeshPostProcess.ApplyPivotOffset(mo, _pipeP.Pivot);
            return mo;
        }
    }
}
