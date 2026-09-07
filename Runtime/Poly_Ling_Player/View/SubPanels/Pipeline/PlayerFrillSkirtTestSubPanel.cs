// PlayerFrillSkirtTestSubPanel.cs
// フリルスカート自動検証。ボタン 1 回で
//   スカート円筒を生成 → 断面プロファイルを描画オブジェクトとして作る
//   → そこから断面を取り込む → 円筒を梯子にしてフリルを生成（グループとして残す）
//   → ソースを動かして「要更新」になることを確かめる → 作り直す
// までを流す。共通部は PlayerBeltGroupTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜスカート（ふたなし円筒）か】
//   梯子は「四角形が一列に並んだ帯」。円筒の側面がそのまま帯になる。
//   ふたを張ると円形の面が混ざり、円環検出がその面まで拾ってしまう。
//   高さ分割を 2 以上にすると段グループができ、フリルの 2 プロファイル補間や
//   段ごとの高さ倍率が効く形になる。
//
// 【梯子は円環として拾う】
//   円筒側面は一周してつながった帯なので BeltRingDetector で取れる。
//   BeltStackDetector（開始タグ三角形が要る方）は使わない。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Frill;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    public class PlayerFrillSkirtTestSubPanel : PlayerBeltGroupTestSubPanelBase
    {
        private const string SkirtName   = "FT_Skirt";
        private const string ProfileName = "FT_Profile";
        private const string FrillName   = "FT_Frill";

        private FloatField   _radiusTop, _radiusBottom, _height, _frillHeight, _bulge;
        private IntegerField _radial, _lateral;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "フリルスカート自動検証";

        protected override string NoteText =>
            "スカート円筒 → 断面プロファイル（描画オブジェクト）→ フリル生成（グループとして保持）\n"
          + "→ ソースを動かす → 要更新の検出 → 作り直し、までを通しで流します。\n"
          + "各段のログに「UI でやるならどこを押すか」を書き出します。";

        protected override string SourceObjectName  => SkirtName;
        protected override string ProfileObjectName => ProfileName;
        protected override bool   ProfileIsClosed   => false;
        protected override string ExpectedAction    => "createFrill";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("スカート円筒"));
            _radiusTop    = F("上の半径",   0.30f);
            _radiusBottom = F("下の半径",   0.60f);
            _height       = F("高さ",       0.80f);
            _radial       = I("円周の分割", 16);
            _lateral      = I("高さの分割",  3);
            root.Add(_radiusTop); root.Add(_radiusBottom); root.Add(_height);
            root.Add(_radial);    root.Add(_lateral);

            root.Add(Sec("フリル"));
            _frillHeight = F("高さ倍率", 1.0f);
            root.Add(_frillHeight);

            root.Add(Sec("ソースの編集（作り直しの検証用）"));
            _bulge = F("裾を外へ広げる量", 0.25f);
            root.Add(_bulge);
        
            AddKeepStashToggle(root);
}

        /// <summary>
        /// 開いた折れ線の山形。x が rung 方向、y が基準ベルト面の法線方向。
        /// 端の y を 0 にしておくと、隣の rung と段差なくつながる。
        /// </summary>
        protected override List<Vector2> BuildProfilePoints() => new List<Vector2>
        {
            new Vector2(0.00f, 0.00f),
            new Vector2(0.25f, 0.35f),
            new Vector2(0.50f, 0.00f),
            new Vector2(0.75f, 0.35f),
            new Vector2(1.00f, 0.00f),
        };

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. スカート円筒を生成",             StageCreateSkirt));
            stages.Add(("2. 円筒の生成完了を待つ",           StageWaitSkirt));
            stages.Add(("3. 断面プロファイルを描画オブジェクトとして置く", StageCreateProfileObject));
            stages.Add(("4. プロファイルの生成完了を待つ",   StageWaitProfile));
            stages.Add(("5. 描画オブジェクトから断面を取り込む",           StageImportProfile));
            stages.Add(("6. 円筒を梯子にしてフリルを生成",   StageCreateFrill));
            stages.Add(("7. グループが出来たか検査",         StageVerifyGroup));
            stages.Add(("8. ソース（円筒）を編集",           StageEditSource));
            stages.Add(("9. 要更新になったか検査",           StageVerifyStale));
            stages.Add(("10. グループを作り直す",            StageRebuild));
            stages.Add(("11. 作り直しの結果を検査",          StageVerifyRebuild));
        }

        // ================================================================
        // 段 1-2: スカート円筒
        // ================================================================

        private StageResult StageCreateSkirt()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            var prms = CylinderMeshGenerator.CylinderParams.Default;
            prms.MeshName       = SkirtName;
            prms.RadiusTop      = _radiusTop.value;
            prms.RadiusBottom   = _radiusBottom.value;
            prms.Height         = _height.value;
            prms.RadialSegments = Mathf.Max(3, _radial.value);
            prms.HeightSegments = Mathf.Max(1, _lateral.value);
            prms.CapTop         = false;
            prms.CapBottom      = false;
            prms.EdgeRadius     = 0f;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = false;

            SendCommand(new CreateCylinderCommand(ModelIndex, prms, pl));

            return Ok(
                $"CreateCylinderCommand を送った（上{prms.RadiusTop:0.###} / 下{prms.RadiusBottom:0.###} / "
              + $"高さ{prms.Height:0.###} / 円周{prms.RadialSegments} / 高さ分割{prms.HeightSegments} / ふたなし）",
                "図形生成パネル → 図形「円柱」→ 上面のフタ・底面のフタを両方オフ → 生成",
                "梯子は「四角形が一列に並んだ帯」。円筒の側面がそのまま帯になる。"
              + "ふたを張ると円形の面が混ざり、円環の自動検索がその面まで拾ってしまう。"
              + "高さ分割を 2 以上にすると段グループが出来る。"
              + "この段の『グループとして残す』はオフ。円筒は入力であって、"
              + "作り直したいのはフリルの方だから。");
        }

        private StageResult StageWaitSkirt()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= MeshCountBefore) return StageResult.Retry;

            SourceIndex = FindByName(model, SkirtName);
            if (SourceIndex < 0) return StageResult.Retry;

            var mo = model.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            MeshCountBefore = model.MeshContextCount;

            return Ok(
                $"「{SkirtName}」が出来た（索引 {SourceIndex} / 頂点 {mo.VertexCount} / 面 {mo.FaceCount}）",
                null,
                "コマンドはキュー経由で処理されるので、送った直後にモデルを見ても間に合わない。"
              + "オブジェクトが増えるまで同じ段を待ち直している。");
        }

        // ================================================================
        // 段 6: フリル生成
        // ================================================================

        private StageResult StageCreateFrill()
        {
            var model = GetModel();
            var skirt = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (skirt == null) return Ng("スカート円筒が見つからない", null, null);

            // ── 円筒から梯子を拾う
            //    円筒側面は「一周してつながった帯」なので円環検出を使う。
            //    そのあと上下へ横断して段グループにまとめる。
            var rings = BeltRingDetector.Detect(skirt, out string ringMsg);
            if (rings == null || rings.Count == 0)
                return Ng($"円環を検出できなかった（{ringMsg}）",
                    "図形生成パネル → フリル → 「円環を自動検索」",
                    "ふたが張ってあったり、側面が四角形で一列に並んでいないと検出できない。");

            var rows = BeltStackExpander.ExpandAll(skirt, rings, cross: true, out int groupCount);
            if (rows == null || rows.Count == 0)
                return Ng("段グループを作れなかった", null, null);

            // ── 取り込み方をそのまま記録して取り直す
            //    ここが「ソースの編集に追随する」ための肝。取り込みは
            //    BeltAcquire が同じ手順で掛け直せるので、点列ではなく手順を控える。
            var acquired = BeltAcquire.Acquire(
                model.GetMeshContext(SourceIndex), BeltAcquireMethod.AutoRing, true, "");
            if (!acquired.Ok)
                return Ng($"取り込みを掛け直せなかった（{acquired.Message}）", null, null);

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var bL, out var bR, out var bStarts,
                out var bClosed, out var bFlip, out var bHeight);

            var fp = FrillParams.Default;
            fp.MeshName      = FrillName;
            fp.HeightScale   = _frillHeight.value;
            fp.ConnectShared = true;
            fp.TwoProfiles   = false;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;   // ← ここがグループを残す指定

            SendCommand(new CreateFrillCommand(
                ModelIndex, fp,
                ProfilePoints.ToArray(), null,
                bL, bR, bStarts, bClosed, bFlip, bHeight,
                BeltOrientOptions.Default, BeltSplineOptions.Default,
                pl,
                SourceIndex, BeltAcquireMethod.AutoRing, true, ""));

            OutputIdBefore = 0UL;

            return Ok(
                $"梯子 {acquired.Belts.Count} 本（グループ {groupCount}）＋断面 {ProfilePoints.Count} 点で "
              + $"CreateFrillCommand を送った。取り込み方=円環の自動検索（上下展開あり）、"
              + $"取り込み元は索引 {SourceIndex}",
                "図形生成パネル → フリル → 「円環を自動検索」で梯子を拾う → 断面を取り込む "
              + "→ 『オブジェクトグループとして残す』をオン → 生成",
                "出力先を先に作っておく必要は無い。フリルの生成コマンド自体が新しい描画オブジェクトを作り、"
              + "『グループとして残す』が立っていればディスパッチャが実行前後の ObjectId を比べて"
              + "出来たものを出力先として控える。"
              + "コマンドに載せるのは点列と『取り込み方』。作り直しでは取り込みを掛け直すので、"
              + "頂点ID や頂点番号を控える必要が無い。人が管理するのはメッシュ側だけで済む。");
        }

        // ================================================================
        // 段 8: ソースを動かす
        // ================================================================

        /// <summary>
        /// 円筒の裾を外へ広げる。コマンドではなく頂点を直接動かす検証用の段。
        /// 実際の作業ではツールで動かすが、ここで確かめたいのは
        /// 「ソースが変わったことをグループが気づけるか」だけなので経路は問わない。
        /// </summary>
        protected override StageResult StageEditSource()
        {
            var model = GetModel();
            var skirt = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (skirt == null) return Ng("スカート円筒が見つからない", null, null);

            float bulge = _bulge.value;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < skirt.VertexCount; i++)
            {
                float y = skirt.Vertices[i].Position.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float span = Mathf.Max(1e-6f, maxY - minY);

            int moved = 0;
            for (int i = 0; i < skirt.VertexCount; i++)
            {
                var v = skirt.Vertices[i];
                var p = v.Position;

                // 下ほど強く外へ。裾が広がったスカートになる。
                float t = 1f - (p.y - minY) / span;
                if (t <= 0f) continue;

                var radial = new Vector3(p.x, 0f, p.z);
                if (radial.sqrMagnitude < 1e-12f) continue;

                v.Position = p + radial.normalized * (bulge * t);
                moved++;
            }

            return Ok(
                $"円筒の頂点 {moved} 個を半径方向へ最大 {bulge:0.###} 広げた",
                "ビューポートで円筒を選び、拡大や移動のツールで裾を広げるのと同じ",
                "ここで動かしたのは入力（円筒）だけ。フリルの出力先はまだ古い形のまま。"
              + "グループはこの食い違いを見つけられるはず、というのが次の段の検査。");
        }
    }
}
