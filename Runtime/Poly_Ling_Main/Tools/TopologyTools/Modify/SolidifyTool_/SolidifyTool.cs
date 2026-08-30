// SolidifyTool.cs
// 薄い面群に厚みを付けるツール。
//
// 選択面群のコピーを2枚（表＝元の巻き順 / 裏＝反転）作り、頂点法線方向へ
// ±厚み/2 移動し、孤立エッジを側面で接続して閉じた立体を作る。
// オリジナルの面には触れない（生成された立体の内側に残る）。
//
// マウス操作は持たない。実行はサブパネルのボタン経由（FlipFaceTool と同型）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 選択面群に厚みを付けるツール
    /// </summary>
    public partial class SolidifyTool : IEditTool
    {
        public string Name => "Solidify";
        public string DisplayName => "Solidify";

        private readonly SolidifySettings _settings = new SolidifySettings();
        public IToolSettings Settings => _settings;

        // ================================================================
        // 公開パラメータ
        // ================================================================

        /// <summary>総厚み</summary>
        public float Thickness
        {
            get => _settings.Thickness;
            set => _settings.Thickness = value;
        }

        /// <summary>true = 既存メッシュに追加 / false = 新規オブジェクト</summary>
        public bool AddToExisting
        {
            get => _settings.AddToExisting;
            set => _settings.AddToExisting = value;
        }

        /// <summary>生成メッシュ名</summary>
        public string MeshName
        {
            get => _settings.MeshName;
            set => _settings.MeshName = value;
        }

        /// <summary>表側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int SegmentsFront
        {
            get => _settings.SegmentsFront;
            set => _settings.SegmentsFront = value;
        }

        /// <summary>裏側エッジ分割数（0=無効 / 1=面取り / 2以上=ラウンド）</summary>
        public int SegmentsBack
        {
            get => _settings.SegmentsBack;
            set => _settings.SegmentsBack = value;
        }

        /// <summary>表側エッジサイズ</summary>
        public float EdgeSizeFront
        {
            get => _settings.EdgeSizeFront;
            set => _settings.EdgeSizeFront = value;
        }

        /// <summary>裏側エッジサイズ</summary>
        public float EdgeSizeBack
        {
            get => _settings.EdgeSizeBack;
            set => _settings.EdgeSizeBack = value;
        }

        /// <summary>ラウンドの曲率方向を入れ替える</summary>
        public bool EdgeInward
        {
            get => _settings.EdgeInward;
            set => _settings.EdgeInward = value;
        }

        /// <summary>
        /// 生成結果の受け渡し先。(生成メッシュ, メッシュ名, 既存に追加するか)
        /// 追加処理そのものは Player / Editor 側の既存経路が担う。
        /// </summary>
        public System.Action<MeshObject, string, bool> OnMeshCreated;

        /// <summary>直近の実行結果メッセージ</summary>
        public string LastMessage => _lastMessage;

        /// <summary>現在の選択面数（3頂点以上のみ）</summary>
        public int SelectedFaceCount
        {
            get
            {
                var mesh = _context?.ActiveMeshObject;
                var faces = _context?.SelectionState?.Faces;
                if (mesh == null || faces == null) return 0;

                int count = 0;
                foreach (int fi in faces)
                {
                    if (fi < 0 || fi >= mesh.Faces.Count) continue;
                    if (mesh.Faces[fi].VertexCount < 3) continue;
                    count++;
                }
                return count;
            }
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 選択面群を厚み付けする。
        /// </summary>
        public void Execute()
        {
            _lastMessage = "";

            var mesh = _context?.ActiveMeshObject;
            if (mesh == null)
            {
                _lastMessage = T("NoMesh");
                return;
            }

            var faces = _context.SelectionState?.Faces;
            if (faces == null || faces.Count == 0)
            {
                _lastMessage = T("NoFaces");
                return;
            }

            var targetList = new List<int>(faces);
            var buildParams = new FaceGroupSolidifier.Params
            {
                Thickness     = Thickness,
                SegmentsFront = SegmentsFront,
                SegmentsBack  = SegmentsBack,
                EdgeSizeFront = EdgeSizeFront,
                EdgeSizeBack  = EdgeSizeBack,
                EdgeInward    = EdgeInward,
            };
            var result = FaceGroupSolidifier.Build(mesh, targetList, buildParams, MeshName);

            if (!result.Ok)
            {
                _lastMessage = T("Failed", result.Error ?? "");
                return;
            }

            // 厚み付けの結果は 1 つの部品として渡す。
            // 元頂点から複写された部品IDをここで潰す。既存オブジェクトへ追加するときの
            // 番号のずらしは追加側（PrimitiveMeshAddToExisting）が行う。
            Poly_Ling.Ops.PartsIdOps.SetPartsId(result.Mesh, 0);
            Poly_Ling.Ops.PartsIdOps.AssignSubIdByPartsId(result.Mesh);

            OnMeshCreated?.Invoke(result.Mesh, MeshName, AddToExisting);

            _lastMessage = result.BoundaryEdgeCount > 0
                ? T("Done", result.TargetFaceCount, result.BoundaryEdgeCount, result.SideFaceCount)
                : T("DoneNoBoundary", result.TargetFaceCount);

            _context.Repaint?.Invoke();
        }

        // ================================================================
        // IEditTool 実装（マウス操作なし）
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos) => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos) => false;

        public void DrawGizmo(ToolContext ctx)
        {
            // 選択表示で十分なため描画しない。
        }

        public void OnActivate(ToolContext ctx)
        {
            _context = ctx;

            if (ctx?.SelectionState != null && !ctx.SelectionState.Mode.Has(MeshSelectMode.Face))
                _lastMessage = T("SwitchToFaceMode");
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _context = null;
        }

        public void Reset()
        {
            _lastMessage = "";
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ToolContext _context;
        private string      _lastMessage = "";
    }
}
