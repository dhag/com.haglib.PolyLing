// PlayerRevolutionTestSubPanel.cs
// 回転体自動検証。ボタン 1 回で
//   プロファイルを描画オブジェクト（2 頂点ライン）として置く → そこから取り込む
//   → 回転体を生成（グループとして保持）→ プロファイルの線を編集
//   → 要更新の検出 → 作り直す
// までを流す。共通部は PlayerBeltGroupTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【梯子を使わない】
//   回転体の入力はプロファイル（開いた折れ線）だけ。基底の梯子まわりの段は入れない。
//   基底の「ソース」はプロファイルの線オブジェクトそのものになる。
//
// 【断面の正規化を掛けない】
//   帯系の断面は rung 長で正規化された系にあるので取り込み時に等方スケールを掛けるが、
//   回転体はモデルのローカル座標をそのまま回すので掛けない
//   （PLParam(ProfileNormalize = false)）。
//   x が半径方向、y が回転軸方向。x が 0 未満の点は軸を跨ぐので使わない。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Revolution;

namespace Poly_Ling.Player
{
    public class PlayerRevolutionTestSubPanel : PlayerBeltGroupTestSubPanelBase
    {
        private const string ProfileName = "RT_Profile";
        private const string OutputName  = "RT_Bottle";

        // UI 自動操作の ID は "revolutionTest.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ・書き込み先・退避）は基底クラス側で登録する。
        [UiControl("height", Description = "全体の高さ")]
        private FloatField   _height;
        [UiControl("bulge", Description = "胴の最大半径")]
        private FloatField   _bulge;
        [UiControl("neck", Description = "首の半径")]
        private FloatField   _neck;
        [UiControl("lipFlare", Description = "口の広がり（首の倍率）")]
        private FloatField   _lipFlare;
        [UiControl("shoulder", Description = "肩の位置（0〜1）")]
        private FloatField   _shoulder;
        [UiControl("widen", Description = "胴を横へ膨らませる量（作り直しの検証用）")]
        private FloatField   _widen;
        [UiControl("segments", Description = "円周の分割")]
        private IntegerField _segments;
        [UiControl("capEnds", Description = "上下にフタを張る")]
        private Toggle       _capEnds;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "回転体自動検証";

        protected override string NoteText =>
            "瓶のプロファイル（描画オブジェクト）→ 取り込み → 回転体を生成（グループとして保持）\n"
          + "→ プロファイルの線を編集 → 要更新の検出 → 作り直し、までを通しで流します。\n"
          + "梯子は使いません。入力はプロファイルだけです。";

        protected override string SourceObjectName  => ProfileName;
        protected override string ProfileObjectName => ProfileName;
        protected override bool   ProfileIsClosed   => false;   // 開いた折れ線
        protected override string ExpectedAction    => "createRevolution";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("プロファイル（瓶の断面）"));
            _height   = F("全体の高さ",           1.00f);
            _bulge    = F("胴の最大半径",         0.30f);
            _neck     = F("首の半径",             0.09f);
            _lipFlare = F("口の広がり（首の倍率）", 1.45f);
            _shoulder = F("肩の位置（0〜1）",      0.46f);
            root.Add(_height); root.Add(_bulge); root.Add(_neck);
            root.Add(_lipFlare); root.Add(_shoulder);

            root.Add(Sec("回転体"));
            _segments = I("円周の分割", 24);
            _capEnds  = new Toggle("上下にフタを張る") { value = true };
            root.Add(_segments); root.Add(_capEnds);

            root.Add(Sec("ソースの編集（作り直しの検証用）"));
            _widen = F("胴を横へ膨らませる量", 0.20f);
            root.Add(_widen);
        
            AddKeepStashToggle(root);
}

        /// <summary>
        /// 瓶の断面。x が半径方向、y が回転軸方向。
        ///
        /// 【形の作り】
        ///   底面 → 高台の立ち上がり（小さい面取り）→ 胴のふくらみ →
        ///   肩の絞り（S 字）→ 首 → 口のフレア、の 6 区間。
        ///   肩は 1 本の直線ではなく、外側へ張った点と内側へ寄った点を
        ///   交互に置いて S 字にする。ここが直線だと円錐に見えて安っぽくなる。
        ///
        /// 【x が 0 の点は底の中心だけ】
        ///   回転軸を跨ぐ点があると面が裏返る。立ち上がり以降は必ず正の値にする。
        /// </summary>
        protected override List<Vector2> BuildProfilePoints()
        {
            float h  = Mathf.Max(0.05f, _height.value);
            float b  = Mathf.Max(0.02f, _bulge.value);
            float n  = Mathf.Clamp(_neck.value, 0.005f, b * 0.9f);
            float lf = Mathf.Clamp(_lipFlare.value, 1f, 3f);
            float sh = Mathf.Clamp(_shoulder.value, 0.25f, 0.75f);

            // 肩の始まりから首の付け根までを S 字で降ろす。
            // t は肩区間の進み具合、w は「胴 → 首」の寄り具合。
            // smoothstep を使うと両端の傾きが 0 になり、胴とも首とも滑らかにつながる。
            float NeckAt(float t)
            {
                float w = t * t * (3f - 2f * t);         // smoothstep
                return Mathf.Lerp(b, n, w);
            }

            var pts = new List<Vector2>
            {
                // ── 底
                new Vector2(0f,          0f),
                new Vector2(b * 0.52f,   0f),
                new Vector2(b * 0.62f,   h * 0.008f),    // 高台の面取り

                // ── 胴（下から広がって最大へ）
                new Vector2(b * 0.86f,   h * 0.035f),
                new Vector2(b * 0.97f,   h * 0.090f),
                new Vector2(b,           h * 0.180f),
                new Vector2(b,           h * 0.300f),
                new Vector2(b * 0.985f,  h * (sh - 0.06f)),
            };

            // ── 肩（S 字で首まで絞る）
            const int shoulderSteps = 5;
            float shoulderTop = sh + 0.22f;
            for (int i = 1; i <= shoulderSteps; i++)
            {
                float t = (float)i / shoulderSteps;
                float y = Mathf.Lerp(sh, shoulderTop, t);
                pts.Add(new Vector2(NeckAt(t), h * y));
            }

            // ── 首（ほぼ垂直。わずかに絞ってから口へ）
            pts.Add(new Vector2(n * 0.98f, h * (shoulderTop + 0.10f)));
            pts.Add(new Vector2(n * 0.97f, h * (shoulderTop + 0.22f)));

            // ── 口のフレア（外へ張り出して、上面はわずかに内へ返す）
            pts.Add(new Vector2(n * lf * 0.80f, h * 0.955f));
            pts.Add(new Vector2(n * lf,         h * 0.980f));
            pts.Add(new Vector2(n * lf * 0.92f, h));

            return pts;
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. プロファイルを描画オブジェクトとして置く", StageCreateProfileObject));
            stages.Add(("2. プロファイルの生成完了を待つ",             StageWaitProfileAsSource));
            stages.Add(("3. 描画オブジェクトから取り込む",             StageImportProfile));
            stages.Add(("4. 回転体を生成",                             StageCreateRevolution));
            stages.Add(("5. グループが出来たか検査",                   StageVerifyGroup));
            stages.Add(("6. プロファイルの線を編集",                   StageEditSource));
            stages.Add(("7. 要更新になったか検査",                     StageVerifyStale));
            stages.Add(("8. グループを作り直す",                       StageRebuild));
            stages.Add(("9. 作り直しの結果を検査",                     StageVerifyRebuild));
        }

        /// <summary>
        /// 基底の StageWaitProfile はプロファイル索引だけを埋める。
        /// 回転体ではプロファイルの線オブジェクトがそのまま「ソース」なので、
        /// SourceIndex にも同じ値を入れる（要更新の判定がこれを見る）。
        /// </summary>
        private StageResult StageWaitProfileAsSource()
        {
            var r = StageWaitProfile();
            if (r == StageResult.Ok) SourceIndex = ProfileIndex;
            return r;
        }

        // ================================================================
        // 段 4: 回転体を生成
        // ================================================================

        private StageResult StageCreateRevolution()
        {
            if (ProfilePoints == null || ProfilePoints.Count < 2)
                return Ng("断面が取り込めていない", null, null);

            var rp = RevolutionParams.Default;
            rp.MeshName      = OutputName;
            rp.Profile       = ProfilePoints.ToArray();
            rp.RadialSegments = Mathf.Clamp(_segments.value,
                                            RevolutionParams.RadialSegmentsMin,
                                            RevolutionParams.RadialSegmentsMax);
            rp.CloseTop       = _capEnds.value;
            rp.CloseBottom    = _capEnds.value;
            rp.CloseLoop      = false;
            rp.CurrentPreset  = ProfilePreset.Custom;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;   // ← ここがグループを残す指定

            SendCommand(new CreateRevolutionCommand(
                ModelIndex, rp, pl,
                ProfileIndex, ProfileAcquireMethod.LinePolyline));

            return Ok(
                $"CreateRevolutionCommand を送った（断面 {ProfilePoints.Count} 点 / "
              + $"円周分割 {rp.RadialSegments} / フタ={(_capEnds.value ? "あり" : "なし")}）。"
              + $"取り込み元は索引 {ProfileIndex}、読み方は開いた折れ線",
                "図形生成パネル → 図形「回転体」→ 「取り込み(メッシュ→プロファイル)」"
              + "→ 『オブジェクトグループとして残す』をオン → 生成",
                "回転体の入力はプロファイルだけなので、梯子は要らない。"
              + "断面は x が半径方向・y が回転軸方向で、モデルのローカル座標をそのまま回す"
              + "（帯系の断面と違って正規化は掛けない）。"
              + "取り込み元と読み方を載せておくと、作り直しのときに同じ線オブジェクトから"
              + "読み直すので、線を動かせば壺の形も変わる。");
        }

        // ================================================================
        // 段 6: プロファイルの線を動かす
        // ================================================================

        /// <summary>
        /// 胴（高さの中ほど）を横へ膨らませる。コマンドではなく頂点を直接動かす検証用の段。
        /// 動かすのはプロファイルの線オブジェクトなので、作り直すと壺の形が変わる。
        /// </summary>
        protected override StageResult StageEditSource()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(ProfileIndex)?.MeshObject;
            if (mo == null) return Ng("プロファイルオブジェクトが見つからない", null, null);

            float widen = _widen.value;
            if (Mathf.Abs(widen) < 1e-5f)
                return Ng("膨らませる量が 0", null, "1e-5 より大きい値にする。");

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                float y = mo.Vertices[i].Position.y;
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float span = Mathf.Max(1e-6f, maxY - minY);

            int moved = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                var p = v.Position;

                // 高さの中ほどが最も強い山型の重み。底と口は動かさない。
                float t = (p.y - minY) / span;
                float w = Mathf.Sin(t * Mathf.PI);
                if (w <= 0f) continue;

                // 軸上（x≒0）の点は動かさない。動かすと底が抜ける。
                if (p.x < 1e-4f) continue;

                v.Position = new Vector3(p.x + widen * w, p.y, p.z);
                moved++;
            }

            if (moved == 0)
                return Ng("動かせる点が無かった", null,
                    "断面の点がすべて軸上（x≒0）か、高さが 0 になっている。");

            return Ok(
                $"プロファイルの点 {moved} 個を横へ最大 {widen:0.###} 膨らませた（胴が最も強い山型の重み）",
                "ビューポートでプロファイルの線を選び、胴の点を外へ動かすのと同じ",
                "動かしたのは入力（プロファイルの線オブジェクト）だけ。回転体の出力先はまだ古い形のまま。"
              + "グループはこの食い違いを見つけられるはず、というのが次の段の検査。");
        }
    }
}
