// PlayerPrimitiveMeshSubPanel.Bridge.cs
// 図形生成サブパネル：穴つなぎ（高度な図形）。
//
// 2つの穴（エッジ＝1面だけが使う辺のグループ）を選び、対応する頂点どうしに
// 面を張って橋渡しする。関節に柔らかい面を張ってからウェイトを塗る用途を想定。
//
// 【生成経路】他の図形と違い、生成物は書き込み先メッシュの既存頂点を参照する。
//   そのため OnMeshCreated（単一 MeshObject を新規追加）は通らず、
//   OnBridgeGenerate（Viewer 側の ExecuteBridge）へ流す。
//
// 【プレビュー】プレビュー用 MeshObject の座標は「ワールド空間」で作る。
//   ライブワイヤの行列は Bridge のとき単位行列にするので、そのまま実位置に出る。
//   実生成では Viewer が書き込み先のローカル空間へ変換する。
//
// Runtime/Poly_Ling_Player/View/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using static Poly_Ling.Player.PrimitiveMeshTexts;

namespace Poly_Ling.Player
{
    public partial class PlayerPrimitiveMeshSubPanel
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>
        /// 選択中の描画オブジェクトから「頂点1個 または 辺1本」の種をちょうど1つ拾う。
        /// 複数・ゼロのときは Ok=false と理由を返す。
        /// </summary>
        public Func<BridgeSeedPick> PickBridgeSeed;

        /// <summary>メッシュインデックス → MeshObject。</summary>
        public Func<int, MeshObject> GetMeshObjectAt;

        /// <summary>メッシュインデックス → 表示名。</summary>
        public Func<int, string> GetMeshNameAt;

        /// <summary>メッシュインデックス → WorldMatrix。</summary>
        public Func<int, Matrix4x4> GetMeshWorldMatrixAt;

        /// <summary>穴つなぎの「生成」。挿入と Undo は Viewer 側が持つ。</summary>
        public Action<PlayerPrimitiveMeshSubPanel> OnBridgeGenerate;

        // ================================================================
        // 種（拾った頂点／辺）
        // ================================================================

        /// <summary>選択から拾った種。</summary>
        public struct BridgeSeedPick
        {
            public bool   Ok;
            public string Message;
            public int    MeshIndex;
            public int    Vertex;
            /// <summary>辺を拾ったときの進行方向側の頂点。頂点を拾ったときは -1。</summary>
            public int    DirectionHint;
        }

        private sealed class BridgeSeed
        {
            public bool   Valid;
            public int    MeshIndex     = -1;
            public int    Vertex        = -1;
            public int    DirectionHint = -1;
            public string Info          = "";
        }

        // ================================================================
        // 状態
        // ================================================================

        private readonly BridgeSeed _bridgeA = new BridgeSeed();
        private readonly BridgeSeed _bridgeB = new BridgeSeed();

        private string _bridgeName        = "Bridge";
        private bool   _bridgeNewObject   = false;
        private bool   _bridgeFlipCorresp = false;
        private bool   _bridgeFlipFaces   = false;
        private int    _bridgeSubdiv      = 0;

        private Label _bridgeInfoA;
        private Label _bridgeInfoB;
        private Label _bridgeInfoResult;

        // ================================================================
        // 公開プロパティ（Viewer から読む）
        // ================================================================

        /// <summary>新規オブジェクトへ作るか。</summary>
        public bool BridgeNewObject => _bridgeNewObject;

        /// <summary>穴A の所属メッシュインデックス。未取り込みは -1。</summary>
        public int BridgeSeedMeshIndexA => _bridgeA.Valid ? _bridgeA.MeshIndex : -1;

        /// <summary>穴B の所属メッシュインデックス。未取り込みは -1。</summary>
        public int BridgeSeedMeshIndexB => _bridgeB.Valid ? _bridgeB.MeshIndex : -1;

        /// <summary>新規オブジェクトの名前。</summary>
        public string BridgeMeshName => string.IsNullOrWhiteSpace(_bridgeName) ? "Bridge" : _bridgeName;

        /// <summary>結果表示欄へ書く。</summary>
        public void SetBridgeStatus(string text)
        {
            if (_bridgeInfoResult != null) _bridgeInfoResult.text = text ?? "";
            if (_statusLabel != null && !string.IsNullOrEmpty(text)) _statusLabel.text = text;
        }

        // ================================================================
        // 計画（Viewer が実生成に使う）
        // ================================================================

        /// <summary>穴つなぎの計画。座標はすべてワールド空間。</summary>
        public class BridgePlan
        {
            public int SrcMeshA, SrcMeshB;
            public List<int>     LoopA  = new List<int>();
            public List<int>     LoopB  = new List<int>();
            public List<Vector3> WorldA = new List<Vector3>();
            public List<Vector3> WorldB = new List<Vector3>();
            public BridgeLoopOps.BridgeResult Result;
        }

        /// <summary>
        /// 現在の種と設定から計画を組む。取り込み不足・エッジ不成立なら false。
        /// </summary>
        public bool TryBuildBridgePlan(out BridgePlan plan, out string message)
        {
            plan = null;
            message = null;

            if (!_bridgeA.Valid || !_bridgeB.Valid)
            {
                message = T("BridgeNeedBoth");
                return false;
            }

            var meshA = GetMeshObjectAt?.Invoke(_bridgeA.MeshIndex);
            var meshB = GetMeshObjectAt?.Invoke(_bridgeB.MeshIndex);
            if (meshA == null || meshB == null)
            {
                message = T("BridgeNoMesh");
                return false;
            }

            var loopA = BridgeLoopOps.OrderBoundaryLoop(
                meshA, _bridgeA.Vertex, _bridgeA.DirectionHint, out string msgA);
            if (loopA.Count < 3) { message = "穴A: " + msgA; return false; }

            var loopB = BridgeLoopOps.OrderBoundaryLoop(
                meshB, _bridgeB.Vertex, _bridgeB.DirectionHint, out string msgB);
            if (loopB.Count < 3) { message = "穴B: " + msgB; return false; }

            var result = BridgeLoopOps.Build(
                loopA.Count, loopB.Count, _bridgeFlipCorresp, _bridgeFlipFaces, _bridgeSubdiv);
            if (!result.Ok) { message = result.Message; return false; }

            Matrix4x4 wA = GetMeshWorldMatrixAt?.Invoke(_bridgeA.MeshIndex) ?? Matrix4x4.identity;
            Matrix4x4 wB = GetMeshWorldMatrixAt?.Invoke(_bridgeB.MeshIndex) ?? Matrix4x4.identity;

            plan = new BridgePlan
            {
                SrcMeshA = _bridgeA.MeshIndex,
                SrcMeshB = _bridgeB.MeshIndex,
                LoopA    = loopA,
                LoopB    = loopB,
                Result   = result,
            };

            foreach (int v in loopA) plan.WorldA.Add(wA.MultiplyPoint3x4(meshA.Vertices[v].Position));
            foreach (int v in loopB) plan.WorldB.Add(wB.MultiplyPoint3x4(meshB.Vertices[v].Position));

            message = result.Message;
            return true;
        }

        // ================================================================
        // プレビュー用メッシュ（ワールド座標）
        // ================================================================

        private MeshObject GenerateBridgeMesh()
        {
            if (!TryBuildBridgePlan(out var plan, out _)) return null;

            var mo = new MeshObject(BridgeMeshName);
            var r  = plan.Result;

            int total = r.InterBase + r.Inter.Count;
            for (int id = 0; id < total; id++)
                mo.AddVertex(BridgeLoopOps.ResolvePosition(r, id, plan.WorldA, plan.WorldB));

            foreach (var f in r.Faces)
            {
                var face = new Face();
                foreach (int id in f)
                {
                    face.VertexIndices.Add(id);
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }
                mo.AddFace(face);
            }

            return mo;
        }

        // ================================================================
        // UI
        // ================================================================

        private void BuildBridgeUI(VisualElement c)
        {
            c.Add(SL(T("Bridge")));

            var hint = new Label(T("BridgeHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 3;
            c.Add(hint);

            // ── 穴A / 穴B の取り込み ──
            c.Add(PlayerIoUiKit.SectionLabel(T("BridgeHoleA")));
            SB(c, T("BridgeImport"), () => { ImportBridgeSeed(_bridgeA); RefreshBridgeInfo(); });
            _bridgeInfoA = BridgeInfoLabel();
            c.Add(_bridgeInfoA);

            c.Add(PlayerIoUiKit.SectionLabel(T("BridgeHoleB")));
            SB(c, T("BridgeImport"), () => { ImportBridgeSeed(_bridgeB); RefreshBridgeInfo(); });
            _bridgeInfoB = BridgeInfoLabel();
            c.Add(_bridgeInfoB);

            // ── 書き込み先 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(SL(T("BridgeTarget")));
            c.Add(TR(T("BridgeNewObject"), () => _bridgeNewObject, v => { _bridgeNewObject = v; D(); }));
            c.Add(NF(() => _bridgeName, v => _bridgeName = v));

            var targetHint = new Label(T("BridgeTargetHint"));
            targetHint.style.fontSize     = 10;
            targetHint.style.whiteSpace   = WhiteSpace.Normal;
            targetHint.style.marginBottom = 3;
            c.Add(targetHint);

            // ── 対応と分割 ──
            c.Add(PlayerIoUiKit.Divider());
            c.Add(TR(T("BridgeFlipPair"),  () => _bridgeFlipCorresp, v => { _bridgeFlipCorresp = v; D(); }));
            c.Add(TR(T("BridgeFlipFaces"), () => _bridgeFlipFaces,   v => { _bridgeFlipFaces   = v; D(); }));
            c.Add(IR(T("BridgeSubdiv"), 0, 16, () => _bridgeSubdiv, v => { _bridgeSubdiv = v; D(); }));

            _bridgeInfoResult = BridgeInfoLabel();
            c.Add(_bridgeInfoResult);

            RefreshBridgeInfo();
        }

        private static Label BridgeInfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.whiteSpace   = WhiteSpace.Normal;
            l.style.marginBottom = 3;
            return l;
        }

        // ================================================================
        // 取り込み
        // ================================================================

        private void ImportBridgeSeed(BridgeSeed seed)
        {
            seed.Valid = false;
            seed.Info  = "";

            if (PickBridgeSeed == null) { seed.Info = T("BridgeNoPick"); return; }

            var pick = PickBridgeSeed();
            if (!pick.Ok) { seed.Info = pick.Message; return; }

            var mesh = GetMeshObjectAt?.Invoke(pick.MeshIndex);
            if (mesh == null) { seed.Info = T("BridgeNoMesh"); return; }

            var loop = BridgeLoopOps.OrderBoundaryLoop(
                mesh, pick.Vertex, pick.DirectionHint, out string msg);
            if (loop.Count < 3) { seed.Info = msg; return; }

            seed.Valid         = true;
            seed.MeshIndex     = pick.MeshIndex;
            seed.Vertex        = pick.Vertex;
            seed.DirectionHint = pick.DirectionHint;

            string name = GetMeshNameAt?.Invoke(pick.MeshIndex) ?? $"#{pick.MeshIndex}";
            seed.Info = $"{name} / 頂点 {pick.Vertex} / {msg}";
            D();
        }

        private void RefreshBridgeInfo()
        {
            if (_bridgeInfoA != null)
                _bridgeInfoA.text = _bridgeA.Valid ? _bridgeA.Info
                                                   : (string.IsNullOrEmpty(_bridgeA.Info) ? T("BridgeNotSet") : _bridgeA.Info);
            if (_bridgeInfoB != null)
                _bridgeInfoB.text = _bridgeB.Valid ? _bridgeB.Info
                                                   : (string.IsNullOrEmpty(_bridgeB.Info) ? T("BridgeNotSet") : _bridgeB.Info);

            if (_bridgeInfoResult != null)
            {
                if (TryBuildBridgePlan(out _, out string msg)) _bridgeInfoResult.text = msg;
                else                                          _bridgeInfoResult.text = msg ?? "";
            }
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>生成ボタンから呼ぶ。挿入と Undo は Viewer 側が持つ。</summary>
        private void InvokeBridgeGenerate()
        {
            if (OnBridgeGenerate == null)
            {
                SetBridgeStatus(T("BridgeNoTarget"));
                return;
            }

            OnBridgeGenerate(this);
            RefreshBridgeInfo();
        }
    }
}
