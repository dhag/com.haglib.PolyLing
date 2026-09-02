// PlayerPrimitiveMeshSubPanel.Bridge.cs
// 図形生成サブパネル：穴つなぎ（高度な図形）。
//
// 2つの穴（エッジ＝1面だけが使う辺のグループ）を選び、対応する頂点どうしに
// 面を張って橋渡しする。関節に柔らかい面を張ってからウェイトを塗る用途を想定。
//
// 【生成経路】他の図形と違い、生成物は追加先メッシュの既存頂点を参照する。
//   そのため図形生成コマンド（単一 MeshObject を新規追加）は通らず、
//   OnBridgeGenerate（Viewer 側の ExecuteBridge）へ流す。
//   行き先は共通の「追加先」(PrimitiveAddMode) に従う。専用トグルは持たない。
//
// 【プレビュー】プレビュー用 MeshObject の座標は「ワールド空間」で作る。
//   ライブワイヤの行列は Bridge のとき単位行列にするので、そのまま実位置に出る。
//   実生成では Viewer が追加先のローカル空間へ変換する。
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
    public partial class PlayerPrimitiveMeshSubPanel : IHoleSeedSource
    {
        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>
        /// 選択中の描画オブジェクトから種を拾う。穴（エッジグループ）ごとに 1 つ、
        /// 最大 2 件。範囲選択で 1 つの穴に多数の頂点が入っていても 1 つだけ返る。
        /// 拾えなかったときは Ok=false の要素を 1 つだけ含むリストを返す。
        /// </summary>
        public Func<List<HoleSeedPick>> PickBridgeSeeds;

        /// <summary>メッシュインデックス → MeshObject。</summary>
        public Func<int, MeshObject> GetMeshObjectAt;

        /// <summary>メッシュインデックス → 表示名。</summary>
        public Func<int, string> GetMeshNameAt;

        /// <summary>メッシュインデックス → WorldMatrix。</summary>
        public Func<int, Matrix4x4> GetMeshWorldMatrixAt;

        /// <summary>
        /// 自動選択の対象にする描画オブジェクトのインデックス。
        /// 2 つなら別々の物体、1 つならその物体内の 2 つの穴を対象にする。
        /// </summary>
        public Func<List<int>> GetBridgeAutoMeshIndices;

        // 穴つなぎの生成は CreateHoleBridgeCommand へ移した。
        // パネルからモデルへ直接面を足す経路は残していない。

        /// <summary>
        /// 種 A / B の内容が変わったときに呼ぶ。ビューポート側の種マーカーを
        /// 即時更新させるためのもの。未配線なら何もしない。
        /// </summary>
        public Action OnBridgeSeedsChanged;

        // ================================================================
        // 種（拾った頂点／辺）
        // ================================================================

        // 選択から拾った種の型は HoleSeedPick（View/Core/HoleSeed.cs）へ移した。
        // 同じ拾い方を穴頂点数合わせツールも使うため、パネルから独立させている。

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
        private bool   _bridgeFlipCorresp = false;
        private bool   _bridgeFlipFaces   = false;
        private int    _bridgeSubdiv      = 3;

        /// <summary>直近の自動判定の説明。結果表示欄に併記する。</summary>
        private string _bridgeAutoInfo = "";

        /// <summary>直近の自動選択の説明。結果表示欄に併記する。</summary>
        private string _bridgeAutoPickInfo = "";

        private Label  _bridgeInfoA;
        private Label  _bridgeInfoB;
        private Label  _bridgeInfoResult;
        private Toggle _bridgeFlipCorrespToggle;
        private Toggle _bridgeFlipFacesToggle;

        // ================================================================
        // 公開プロパティ（Viewer から読む）
        // ================================================================

        /// <summary>穴A の所属メッシュインデックス。未取り込みは -1。</summary>
        public int BridgeSeedMeshIndexA => _bridgeA.Valid ? _bridgeA.MeshIndex : -1;

        /// <summary>穴B の所属メッシュインデックス。未取り込みは -1。</summary>
        public int BridgeSeedMeshIndexB => _bridgeB.Valid ? _bridgeB.MeshIndex : -1;

        /// <summary>穴A の種頂点。未取り込みは -1。ビューポートのマーカー描画に使う。</summary>
        public int BridgeSeedVertexA => _bridgeA.Valid ? _bridgeA.Vertex : -1;

        /// <summary>穴B の種頂点。未取り込みは -1。</summary>
        public int BridgeSeedVertexB => _bridgeB.Valid ? _bridgeB.Vertex : -1;

        /// <summary>穴A の進行方向ヒント頂点。辺で取り込んでいないときは -1。</summary>
        public int BridgeSeedDirHintA => _bridgeA.Valid ? _bridgeA.DirectionHint : -1;

        /// <summary>穴B の進行方向ヒント頂点。辺で取り込んでいないときは -1。</summary>
        public int BridgeSeedDirHintB => _bridgeB.Valid ? _bridgeB.DirectionHint : -1;

        /// <summary>新規オブジェクトの名前。</summary>
        public string BridgeMeshName => string.IsNullOrWhiteSpace(_bridgeName) ? "Bridge" : _bridgeName;

        /// <summary>名前を書き換える。SetName（非重複候補の書き戻し）から呼ぶ。</summary>
        private void SetBridgeMeshName(string name) => _bridgeName = name;

        /// <summary>種 A・B の両方が取込済みか。生成ボタンの有効判定に使う。</summary>
        private bool BridgeSeedsReady => _bridgeA.Valid && _bridgeB.Valid;

        /// <summary>結果表示欄へ書く。</summary>
        public void SetBridgeStatus(string text)
        {
            if (_bridgeInfoResult != null) _bridgeInfoResult.text = text ?? "";
            if (_statusLabel != null && !string.IsNullOrEmpty(text)) _statusLabel.text = text;
        }

        // ================================================================
        // IHoleSeedSource（ビューポートの種マーカー）
        //   ブリッジ専用だった判定を共通インタフェースへ寄せたもの。
        //   中身は上の Bridge* プロパティをそのまま返すだけ。
        // ================================================================

        public bool HoleSeedOverlayActive => BridgeOverlayActive;

        public int HoleSeedMeshIndexA => BridgeSeedMeshIndexA;
        public int HoleSeedVertexA    => BridgeSeedVertexA;
        public int HoleSeedDirHintA   => BridgeSeedDirHintA;

        public int HoleSeedMeshIndexB => BridgeSeedMeshIndexB;
        public int HoleSeedVertexB    => BridgeSeedVertexB;
        public int HoleSeedDirHintB   => BridgeSeedDirHintB;

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
            c.Add(ShapeTitle(T("Bridge")));

            var hint = new Label(T("BridgeHint"));
            hint.style.fontSize     = 10;
            hint.style.whiteSpace   = WhiteSpace.Normal;
            hint.style.marginBottom = 3;
            c.Add(hint);

            // ── 穴A / 穴B の取り込み ──
            c.Add(PlayerIoUiKit.SectionLabel(T("BridgeHoleA")));
            SB(c, T("BridgeImport"), () => ImportBridgeSeedA());
            _bridgeInfoA = BridgeInfoLabel();
            c.Add(_bridgeInfoA);

            c.Add(PlayerIoUiKit.SectionLabel(T("BridgeHoleB")));
            SB(c, T("BridgeImport"), () => ImportBridgeSeedB());
            _bridgeInfoB = BridgeInfoLabel();
            c.Add(_bridgeInfoB);

            // ── A/B 同時取り込み ──
            // 選択が2つの穴にまたがっているとき、1回で A と B を埋める。
            // 穴が1つしか無ければ A だけが埋まる。
            var bothRow = new VisualElement();
            bothRow.style.flexDirection = FlexDirection.Row;
            bothRow.style.marginTop     = 3;
            SB(bothRow, T("BridgeImportBoth"), () => { ImportBridgeSeedsBoth(); });
            c.Add(bothRow);

            var bothHint = new Label(T("BridgeImportBothHint"));
            bothHint.style.fontSize     = 10;
            bothHint.style.whiteSpace   = WhiteSpace.Normal;
            bothHint.style.marginBottom = 3;
            c.Add(bothHint);

            // ── 自動選択 ──
            // 頂点選択を使わず、選んだ物体の穴だけから A/B を決める。
            var autoRow = new VisualElement();
            autoRow.style.flexDirection = FlexDirection.Row;
            autoRow.style.marginTop     = 3;
            SB(autoRow, T("BridgeAutoSelect"), () => { AutoSelectBridgeSeeds(); });
            c.Add(autoRow);

            var autoHint = new Label(T("BridgeAutoSelectHint"));
            autoHint.style.fontSize     = 10;
            autoHint.style.whiteSpace   = WhiteSpace.Normal;
            autoHint.style.marginBottom = 3;
            c.Add(autoHint);

            // ── 名前 ──
            // 行き先は共通の「追加先」ドロップダウンが決める。ここには専用トグルを置かない。
            // 名前欄は NF が追加先に応じて TextField / ドロップダウンへ切り替える。
            c.Add(PlayerIoUiKit.Divider());
            c.Add(NF(() => _bridgeName, v => _bridgeName = v));

            var targetHint = new Label(T("BridgeAddModeHint"));
            targetHint.style.fontSize     = 10;
            targetHint.style.whiteSpace   = WhiteSpace.Normal;
            targetHint.style.marginBottom = 3;
            c.Add(targetHint);

            // ── 対応と分割 ──
            c.Add(PlayerIoUiKit.Divider());
            var flipPairRow  = TR(T("BridgeFlipPair"),  () => _bridgeFlipCorresp, v => { _bridgeFlipCorresp = v; D(); });
            var flipFacesRow = TR(T("BridgeFlipFaces"), () => _bridgeFlipFaces,   v => { _bridgeFlipFaces   = v; D(); });
            _bridgeFlipCorrespToggle = flipPairRow  as Toggle;
            _bridgeFlipFacesToggle   = flipFacesRow as Toggle;
            c.Add(flipPairRow);
            c.Add(flipFacesRow);
            c.Add(IR(T("BridgeSubdiv"), CreateHoleBridgeCommand.SubdivisionsMin, CreateHoleBridgeCommand.SubdivisionsMax, () => _bridgeSubdiv, v => { _bridgeSubdiv = v; D(); }));

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
            _bridgeAutoPickInfo = "";   // 手動取り込みでは自動選択の説明を残さない

            if (PickBridgeSeeds == null) { seed.Info = T("BridgeNoPick"); return; }

            var picks = PickBridgeSeeds();
            if (picks == null || picks.Count == 0) { seed.Info = T("BridgeNoPick"); return; }
            if (!picks[0].Ok) { seed.Info = picks[0].Message; return; }

            ApplyBridgePick(seed, picks[0]);
        }

        /// <summary>
        /// 拾った種 1 件を A または B へ入れる。エッジをたどれなければ入れない。
        /// </summary>
        private void ApplyBridgePick(BridgeSeed seed, HoleSeedPick pick)
        {
            seed.Valid = false;
            seed.Info  = "";

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

        /// <summary>
        /// 現在の選択から A と B をまとめて取り込む。
        /// 拾えた穴が 1 つのときは A だけを入れ替え、B は触らない。
        /// </summary>
        public void ImportBridgeSeedsBoth()
        {
            _bridgeAutoPickInfo = "";   // 手動取り込みでは自動選択の説明を残さない

            if (PickBridgeSeeds == null)
            {
                _bridgeA.Valid = false; _bridgeA.Info = T("BridgeNoPick");
                RefreshBridgeInfo();
                return;
            }

            var picks = PickBridgeSeeds();
            if (picks == null || picks.Count == 0 || !picks[0].Ok)
            {
                _bridgeA.Valid = false;
                _bridgeA.Info  = (picks != null && picks.Count > 0) ? picks[0].Message : T("BridgeNoPick");
                RefreshBridgeInfo();
                return;
            }

            ApplyBridgePick(_bridgeA, picks[0]);
            if (picks.Count > 1 && picks[1].Ok) ApplyBridgePick(_bridgeB, picks[1]);

            ApplyBridgeAutoFlags();
            RefreshBridgeInfo();
        }

        // ================================================================
        // 自動選択
        // ================================================================

        /// <summary>
        /// 頂点選択を使わず、対象物体の穴だけから種 A / B を決める。
        ///
        /// 【手順】穴を列挙 → 頂点数が同数の穴ペアを候補にする → 重心距離が最小の
        ///   もの（等距離はすべて）を残す → その中で穴Aと穴Bの最短距離になる
        ///   頂点ペア (A0, B0) を種にする。
        ///
        /// 【対象】GetBridgeAutoMeshIndices が返す描画オブジェクト。
        ///   2 つなら別々の物体、1 つならその物体内の 2 つの穴を対象にする。
        /// </summary>
        public bool AutoSelectBridgeSeeds()
        {
            _bridgeAutoPickInfo = "";

            // ── 対象物体を決める ──
            int indexA = -1, indexB = -1;
            var indices = GetBridgeAutoMeshIndices?.Invoke();
            if (indices != null)
            {
                var uniq = new List<int>();
                foreach (int i in indices)
                    if (i >= 0 && !uniq.Contains(i)) uniq.Add(i);

                if (uniq.Count == 1)      { indexA = uniq[0]; indexB = uniq[0]; }
                else if (uniq.Count == 2) { indexA = uniq[0]; indexB = uniq[1]; }
            }

            if (indexA < 0)
            {
                _bridgeAutoPickInfo = T("BridgeAutoNeedTarget");
                RefreshBridgeInfo();
                return false;
            }

            var meshA = GetMeshObjectAt?.Invoke(indexA);
            var meshB = GetMeshObjectAt?.Invoke(indexB);
            if (meshA == null || meshB == null)
            {
                _bridgeAutoPickInfo = T("BridgeNoMesh");
                RefreshBridgeInfo();
                return false;
            }

            Matrix4x4 wA = GetMeshWorldMatrixAt?.Invoke(indexA) ?? Matrix4x4.identity;
            Matrix4x4 wB = GetMeshWorldMatrixAt?.Invoke(indexB) ?? Matrix4x4.identity;

            bool sameMesh = (indexA == indexB);
            var holesA = BridgeAutoPairOps.CollectHoles(meshA, wA);
            var holesB = sameMesh ? holesA : BridgeAutoPairOps.CollectHoles(meshB, wB);

            var pair = BridgeAutoPairOps.SelectPair(holesA, holesB, sameMesh);
            if (!pair.Ok)
            {
                _bridgeAutoPickInfo = pair.Failure switch
                {
                    BridgeAutoPairOps.PairFailure.NoHoles         => T("BridgeAutoNoHole"),
                    BridgeAutoPairOps.PairFailure.NoSameCountPair => T("BridgeAutoNoPair"),
                    _                                             => pair.Message ?? "",
                };
                RefreshBridgeInfo();
                return false;
            }

            ApplyBridgePick(_bridgeA, new HoleSeedPick
            {
                Ok = true, MeshIndex = indexA, Vertex = pair.VertexA, DirectionHint = -1,
            });
            ApplyBridgePick(_bridgeB, new HoleSeedPick
            {
                Ok = true, MeshIndex = indexB, Vertex = pair.VertexB, DirectionHint = -1,
            });

            _bridgeAutoPickInfo = T("BridgeAutoPicked",
                holesA[pair.HoleA].Count,
                pair.CentroidDistance.ToString("0.####"),
                pair.VertexA, pair.VertexB,
                pair.VertexDistance.ToString("0.####"));

            ApplyBridgeAutoFlags();
            RefreshBridgeInfo();
            return _bridgeA.Valid && _bridgeB.Valid;
        }

        // ================================================================
        // 対応フリップ／面フリップの自動判定
        // ================================================================

        /// <summary>
        /// 種 A・B が揃っているとき、両ループの巻き方向から対応フリップと
        /// 面フリップを決めてチェックボックスへ書き戻す。
        /// 取り込みのたびに上書きする。以後の手動操作は次の取り込みまで保たれる。
        /// 判定できないときはフラグを変更しない。
        /// </summary>
        private void ApplyBridgeAutoFlags()
        {
            if (!_bridgeA.Valid || !_bridgeB.Valid)
            {
                _bridgeAutoInfo = T("BridgeAutoNotReady");
                return;
            }

            var meshA = GetMeshObjectAt?.Invoke(_bridgeA.MeshIndex);
            var meshB = GetMeshObjectAt?.Invoke(_bridgeB.MeshIndex);
            if (meshA == null || meshB == null)
            {
                _bridgeAutoInfo = T("BridgeNoMesh");
                return;
            }

            var loopA = BridgeLoopOps.OrderBoundaryLoop(
                meshA, _bridgeA.Vertex, _bridgeA.DirectionHint, out _);
            var loopB = BridgeLoopOps.OrderBoundaryLoop(
                meshB, _bridgeB.Vertex, _bridgeB.DirectionHint, out _);

            if (!BridgeLoopOps.TryAutoFlags(
                    meshA, loopA, meshB, loopB,
                    out bool fc, out bool ff, out string why))
            {
                _bridgeAutoInfo = T("BridgeAutoFailed") + (string.IsNullOrEmpty(why) ? "" : " / " + why);
                return;
            }

            _bridgeFlipCorresp = fc;
            _bridgeFlipFaces   = ff;

            _bridgeFlipCorrespToggle?.SetValueWithoutNotify(fc);
            _bridgeFlipFacesToggle?.SetValueWithoutNotify(ff);

            _bridgeAutoInfo = T("BridgeAutoApplied");
            D();
        }

        private void RefreshBridgeInfo()
        {
            RefreshCreateButtonState();

            if (_bridgeInfoA != null)
                _bridgeInfoA.text = _bridgeA.Valid ? _bridgeA.Info
                                                   : (string.IsNullOrEmpty(_bridgeA.Info) ? T("BridgeNotSet") : _bridgeA.Info);
            if (_bridgeInfoB != null)
                _bridgeInfoB.text = _bridgeB.Valid ? _bridgeB.Info
                                                   : (string.IsNullOrEmpty(_bridgeB.Info) ? T("BridgeNotSet") : _bridgeB.Info);

            if (_bridgeInfoResult != null)
            {
                TryBuildBridgePlan(out _, out string msg);
                string text = msg ?? "";
                if (!string.IsNullOrEmpty(_bridgeAutoPickInfo))
                    text = string.IsNullOrEmpty(text) ? _bridgeAutoPickInfo : text + "\n" + _bridgeAutoPickInfo;
                if (!string.IsNullOrEmpty(_bridgeAutoInfo))
                    text = string.IsNullOrEmpty(text) ? _bridgeAutoInfo : text + "\n" + _bridgeAutoInfo;
                _bridgeInfoResult.text = text;
            }

            // ビューポートの種マーカーを即時更新させる。
            OnBridgeSeedsChanged?.Invoke();
        }

        // ================================================================
        // 生成
        // ================================================================

        // ================================================================
        // 自動検証用の公開入口
        //   UI ボタンと同じ処理を、ボタンを押さずに呼べるようにする。
        //   ボタンのハンドラと本体を共有するので、経路が二重にならない。
        // ================================================================

        /// <summary>穴A を現在の選択から取り込む。成功可否を返す。</summary>
        public bool ImportBridgeSeedA()
        {
            ImportBridgeSeed(_bridgeA);
            ApplyBridgeAutoFlags();
            RefreshBridgeInfo();
            return _bridgeA.Valid;
        }

        /// <summary>穴B を現在の選択から取り込む。成功可否を返す。</summary>
        public bool ImportBridgeSeedB()
        {
            ImportBridgeSeed(_bridgeB);
            ApplyBridgeAutoFlags();
            RefreshBridgeInfo();
            return _bridgeB.Valid;
        }

        /// <summary>取り込み済みの種を捨てる。</summary>
        public void ClearBridgeSeeds()
        {
            _bridgeA.Valid = false; _bridgeA.Info = "";
            _bridgeB.Valid = false; _bridgeB.Info = "";
            _bridgeAutoInfo = "";
            _bridgeAutoPickInfo = "";
            RefreshBridgeInfo();
        }

        /// <summary>生成名を指定する。</summary>
        public void SetBridgeName(string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) _bridgeName = name;
        }

        /// <summary>穴A・穴B の取り込み状況の説明。失敗時の表示用。</summary>
        public string BridgeSeedInfoA => _bridgeA.Info ?? "";
        public string BridgeSeedInfoB => _bridgeB.Info ?? "";

        /// <summary>生成ボタンと同じ処理を呼ぶ。</summary>
        public void GenerateBridge() => InvokeBridgeGenerate();

        /// <summary>
        /// 生成ボタンから呼ぶ。コマンドへ流し、挿入と Undo はディスパッチャ側が持つ。
        ///
        /// コマンドは種・対応フリップ・分割数・追加先をすべて載せるので、
        /// 自動検証や MCP から同じコマンドを送れば同じ結果になる。
        /// </summary>
        private void InvokeBridgeGenerate()
        {
            var cmd = BuildHoleBridgeCommand();
            if (cmd == null)
            {
                SetBridgeStatus(T("BridgeNoTarget"));
                return;
            }
            if (SendCommand == null)
            {
                SetBridgeStatus("配線が足りません（SendCommand）");
                return;
            }

            SendCommand(cmd);
            RefreshBridgeInfo();
        }
    }
}
