// PlayerProfile2DTestSubPanel.cs
// 2D 押し出し自動検証。ボタン 1 回で
//   輪郭（外周＋穴）を描画オブジェクト（2 頂点ライン）として置く → そこから取り込む
//   → 2D 押し出しを生成（グループとして保持）→ 輪郭の線を編集
//   → 要更新の検出 → 作り直す
// までを流す。共通部は PlayerBeltGroupTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【梯子を使わない】
//   2D 押し出しの入力は輪郭のループ群だけ。基底の梯子まわりの段は入れない。
//   基底の「ソース」は輪郭の線オブジェクトそのものになる。
//
// 【穴は巻き順で決まる。反時計回りが外周】
//   図形生成パネルが持つ 2D 押し出しの既定の外周
//   （PlayerPrimitiveMeshSubPanel.cs:2916-2923）は
//   (-r,-r)→(r,-r)→(r,r)→(-r,r) で IsHole = false。これは反時計回り。
//   つまり生成器の規約は「反時計回り＝外周 / 時計回り＝穴」。
//   LineProfileExtractor.ExtractLoops もこの向きで判定する。
//   ループの走査は最初の線の VertexIndices[0]→[1] から辿るので
//   （LineProfileExtractor.cs:329-335）、ここで置いた巻き順がそのまま効く。
//
// 【平坦な列を必ず埋めること】
//   Profile2DParams.Loops は PLParam(Ignore) なので ToArgs に出ない。
//   グループが輪郭を控えられるよう、コマンドを組むときに LoopPointValues /
//   LoopStarts / LoopIsHole も埋める（BuildProfile2DCommand と同じ扱い）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.Player
{
    public class PlayerProfile2DTestSubPanel : PlayerBeltGroupTestSubPanelBase
    {
        private const string ProfileName = "PT_Outline";
        private const string OutputName  = "PT_Plate";

        // UI 自動操作の ID は "profile2DTest.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ・書き込み先・退避）は基底クラス側で登録する。
        [UiControl("outerSize", Description = "外周の一辺（正六角形の外接半径）")]
        private FloatField   _outerSize;
        [UiControl("holeSize", Description = "穴の半径")]
        private FloatField   _holeSize;
        [UiControl("thickness", Description = "押し出しの厚み")]
        private FloatField   _thickness;
        [UiControl("edgeSize", Description = "エッジのサイズ")]
        private FloatField   _edgeSize;
        [UiControl("shrinkHole", Description = "穴を広げる量（作り直しの検証用）")]
        private FloatField   _shrinkHole;
        [UiControl("edgeSegments", Description = "エッジの分割")]
        private IntegerField _edgeSegments;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "2D押し出し自動検証";

        protected override string NoteText =>
            "輪郭（外周＋穴・描画オブジェクト）→ 取り込み → 2D 押し出しを生成（グループとして保持）\n"
          + "→ 輪郭の線を編集 → 要更新の検出 → 作り直し、までを通しで流します。\n"
          + "梯子は使いません。入力は輪郭のループ群だけです。";

        protected override string SourceObjectName  => ProfileName;
        protected override string ProfileObjectName => ProfileName;
        protected override bool   ProfileIsClosed   => true;   // 閉ループ群
        protected override string ExpectedAction    => "createProfile2D";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("輪郭"));
            _outerSize = F("外周の一辺（正六角形の外接半径）", 0.50f);
            _holeSize  = F("穴の半径",                         0.20f);
            root.Add(_outerSize); root.Add(_holeSize);

            root.Add(Sec("押し出し"));
            _thickness    = F("厚み",           0.12f);
            _edgeSegments = I("エッジの分割",   0);
            _edgeSize     = F("エッジのサイズ", 0.02f);
            root.Add(_thickness); root.Add(_edgeSegments); root.Add(_edgeSize);

            root.Add(Sec("ソースの編集（作り直しの検証用）"));
            _shrinkHole = F("穴を広げる量", 0.10f);
            root.Add(_shrinkHole);
        
            AddKeepStashToggle(root);
}

        /// <summary>
        /// 基底の StageCreateProfileObject は点列 1 本しか置けないので使わない。
        /// ここは呼ばれない（CollectStages に入れていない）。
        /// </summary>
        protected override List<Vector2> BuildProfilePoints() => null;

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. 輪郭を描画オブジェクトとして置く", StageCreateOutlineObject));
            stages.Add(("2. 輪郭の生成完了を待つ",             StageWaitProfileAsSource));
            stages.Add(("3. 描画オブジェクトから取り込む",     StageImportLoops));
            stages.Add(("4. 2D 押し出しを生成",                StageCreateProfile2D));
            stages.Add(("5. グループが出来たか検査",           StageVerifyGroup));
            stages.Add(("6. 輪郭の線を編集",                   StageEditSource));
            stages.Add(("7. 要更新になったか検査",             StageVerifyStale));
            stages.Add(("8. グループを作り直す",               StageRebuild));
            stages.Add(("9. 作り直しの結果を検査",             StageVerifyRebuild));
        }

        private StageResult StageWaitProfileAsSource()
        {
            var r = StageWaitProfile();
            if (r == StageResult.Ok) SourceIndex = ProfileIndex;
            return r;
        }

        // ================================================================
        // 段 1: 輪郭を置く
        // ================================================================

        /// <summary>
        /// 外周の正六角形と、逆回りの穴の円を 1 つの線オブジェクトへ入れる。
        /// 基底の StageCreateProfileObject は点列 1 本しか置けないので、
        /// ループを 2 本置くこちらを使う。
        /// </summary>
        private StageResult StageCreateOutlineObject()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            float outer = Mathf.Max(0.05f, _outerSize.value);
            float hole  = Mathf.Clamp(_holeSize.value, 0.01f, outer * 0.8f);

            var loops = new List<Loop>
            {
                MakeRing(outer, 6,  isHole: false),   // 外周：反時計回り
                MakeRing(hole,  16, isHole: true),    // 穴：時計回り
            };

            var mo = LineProfileExtractor.LoopsToLineMesh(loops, ProfileName);
            if (mo == null || mo.FaceCount == 0)
                return Ng("線メッシュを作れなかった", null, null);

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = false;

            SendCommand(new AddGeneratedMeshCommand(
                ModelIndex, mo, ProfileName, pl, poseAlreadyBaked: true));

            return Ok(
                $"「{ProfileName}」を 2 頂点ライン {mo.FaceCount} 本で置いた"
              + $"（外周 6 点・外接半径{outer:0.###} / 穴 16 点・半径{hole:0.###}）",
                "図形生成パネル → 図形「2D押し出し」→ 輪郭欄で点を打つ → 「反映(→メッシュ)」",
                "輪郭はコマンド上はループごとの点列だが、そのままでは目で確かめられない。"
              + "2 頂点だけの面（＝補助線）を持つ描画オブジェクトにしておくと、"
              + "ビューポートで形を見ながら頂点を動かして直せる。"
              + "穴かどうかは巻き順で決まる。反時計回りが外周、時計回りが穴"
              + "（図形生成パネルの既定の外周と同じ向き）。"
              + "逆に置くと外周と穴が入れ替わり、出来上がりが裏返って見える。");
        }

        /// <summary>
        /// 正多角形／円のループ。
        /// 外周（isHole = false）は反時計回り、穴（isHole = true）は時計回りで置く。
        /// 角度を増やしながら (cos, sin) を並べると Y 上向きの系では反時計回りになるので、
        /// 穴のときだけ並びを逆にする。
        /// </summary>
        private static Loop MakeRing(float radius, int points, bool isHole)
        {
            var lp = new Loop { IsHole = isHole };
            for (int i = 0; i < points; i++)
            {
                int k = isHole ? (points - i) % points : i;
                float a = k * Mathf.PI * 2f / points;
                lp.Points.Add(new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            return lp;
        }

        // ================================================================
        // 段 3: 取り込み
        // ================================================================

        /// <summary>
        /// 置いた描画オブジェクトからループ群を読み戻す。
        /// 基底の StageImportProfile は点列 1 本しか持ち帰らないので、
        /// ループを保持するこちらを使う。
        /// </summary>
        private List<Loop> _loops;

        private StageResult StageImportLoops()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(ProfileIndex)?.MeshObject;
            if (mo == null) return Ng("輪郭オブジェクトが見つからない", null, null);

            var got = ProfileAcquire.Acquire(
                model.GetMeshContext(ProfileIndex), ProfileAcquireMethod.LineLoops, normalize: false);
            if (!got.Ok) return Ng($"ループを取り込めなかった（{got.Message}）", null,
                "取り込みは「頂点 2 個だけの面」を線とみなして拾い、閉ループへ連結する。"
              + "線が輪になっていないと読めない。");

            _loops        = got.Loops;
            ProfilePoints = got.Points;

            // 巻き順を置き間違えると外周と穴が入れ替わり、出来上がりが裏返る。
            // 面積が最大のループが外周（IsHole = false）になっているかで見張る。
            int holes = 0, biggest = -1;
            float biggestArea = -1f;
            for (int i = 0; i < _loops.Count; i++)
            {
                if (_loops[i].IsHole) holes++;
                float a = Mathf.Abs(SignedArea(_loops[i]));
                if (a > biggestArea) { biggestArea = a; biggest = i; }
            }

            if (biggest < 0 || _loops[biggest].IsHole)
                return Ng("面積が最大のループが穴と判定された（外周と穴が入れ替わっている）",
                    null,
                    "巻き順が逆。反時計回りが外周、時計回りが穴。");

            return Ok(
                $"ループ {_loops.Count} 本（うち穴 {holes} 本）を取り込んだ。"
              + $"最大面積のループ #{biggest} は外周と判定された。{got.Message}",
                "図形生成パネル → 2D押し出し → 「取り込み(メッシュ→プロファイル)」"
              + "（取り込み元はオブジェクト一覧で選択中のもの）",
                "穴の判定は巻き順（Shoelace の符号）。反時計回りが外周、時計回りが穴。"
              + "回転体や帯系の断面と違い、2D 押し出しはモデルのローカル座標をそのまま使う"
              + "（正規化を掛けない）。Z は捨てて XY だけ使う。");
        }

        /// <summary>
        /// 符号付き面積。LineProfileExtractor.IsClockwise と同じ式で、
        /// 正なら時計回り（＝穴の巻き順）。大小の比較にだけ使う。
        /// </summary>
        private static float SignedArea(Loop lp)
        {
            if (lp?.Points == null || lp.Points.Count < 3) return 0f;
            float sum = 0f;
            for (int i = 0; i < lp.Points.Count; i++)
            {
                var p0 = lp.Points[i];
                var p1 = lp.Points[(i + 1) % lp.Points.Count];
                sum += (p1.x - p0.x) * (p1.y + p0.y);
            }
            return sum;
        }

        // ================================================================
        // 段 4: 2D 押し出しを生成
        // ================================================================

        private StageResult StageCreateProfile2D()
        {
            if (_loops == null || _loops.Count == 0)
                return Ng("輪郭が取り込めていない", null, null);

            var pp = Profile2DParams.Default;
            pp.MeshName      = OutputName;
            pp.Thickness     = _thickness.value;
            pp.SegmentsFront = Mathf.Max(0, _edgeSegments.value);
            pp.SegmentsBack  = Mathf.Max(0, _edgeSegments.value);
            pp.EdgeSizeFront = _edgeSize.value;
            pp.EdgeSizeBack  = _edgeSize.value;

            var arr = new Profile2DParams.LoopData[_loops.Count];
            for (int i = 0; i < _loops.Count; i++)
                arr[i] = new Profile2DParams.LoopData(_loops[i]);
            pp.Loops = arr;

            // Loops は PLParam(Ignore) で ToArgs に出ない。グループが輪郭を
            // 控えられるよう、平坦な列も必ず埋める（BuildProfile2DCommand と同じ）。
            Profile2DParams.SplitLoops(
                pp.Loops, out var vals, out var starts, out var holes);
            pp.LoopPointValues = vals;
            pp.LoopStarts      = starts;
            pp.LoopIsHole      = holes;

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;   // ← ここがグループを残す指定

            SendCommand(new CreateProfile2DCommand(
                ModelIndex, pp, pl,
                ProfileIndex, ProfileAcquireMethod.LineLoops));

            return Ok(
                $"CreateProfile2DCommand を送った（ループ {_loops.Count} 本 / 点 {vals.Length / 2} 個 / "
              + $"厚み{pp.Thickness:0.###} / エッジ分割{pp.SegmentsFront}）。"
              + $"取り込み元は索引 {ProfileIndex}、読み方は閉ループ群",
                "図形生成パネル → 図形「2D押し出し」→ 「取り込み(メッシュ→プロファイル)」"
              + "→ 『オブジェクトグループとして残す』をオン → 生成",
                "2D 押し出しの入力は輪郭だけなので、梯子は要らない。"
              + "Loops はスキーマに出せない形なので、外へ渡すときは平坦な 3 本"
              + "（点の連結・ループ開始位置・穴フラグ）で持つ。"
              + "生成側は ResolveLoops が Loops を優先するので、両方入れても結果は同じ。");
        }

        // ================================================================
        // 段 6: 輪郭の線を動かす
        // ================================================================

        /// <summary>
        /// 穴を広げる。コマンドではなく頂点を直接動かす検証用の段。
        /// 動かすのは輪郭の線オブジェクトなので、作り直すと板の形も変わる。
        /// </summary>
        protected override StageResult StageEditSource()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(ProfileIndex)?.MeshObject;
            if (mo == null) return Ng("輪郭オブジェクトが見つからない", null, null);

            float grow = _shrinkHole.value;
            if (Mathf.Abs(grow) < 1e-5f)
                return Ng("広げる量が 0", null, "1e-5 より大きい値にする。");

            float outer = Mathf.Max(0.05f, _outerSize.value);
            float hole  = Mathf.Clamp(_holeSize.value, 0.01f, outer * 0.8f);
            float border = (outer + hole) * 0.5f;   // 外周と穴の中間で見分ける

            int moved = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                var p = v.Position;

                var radial = new Vector2(p.x, p.y);
                float r = radial.magnitude;
                if (r >= border || r < 1e-5f) continue;   // 外周側は動かさない

                var dir = radial / r;
                v.Position = new Vector3(p.x + dir.x * grow, p.y + dir.y * grow, p.z);
                moved++;
            }

            if (moved == 0)
                return Ng("動かせる点が無かった", null,
                    $"原点からの距離が {border:0.###} 未満の点を穴とみなしている。"
                  + "穴の半径と外周の大きさが近すぎると見分けられない。");

            return Ok(
                $"穴側の点 {moved} 個を外へ {grow:0.###} 広げた（原点からの距離 {border:0.###} 未満を穴とみなした）",
                "ビューポートで輪郭の線を選び、穴の点を外へ動かすのと同じ",
                "動かしたのは入力（輪郭の線オブジェクト）だけ。押し出しの出力先はまだ古い形のまま。"
              + "グループはこの食い違いを見つけられるはず、というのが次の段の検査。");
        }
    }
}
