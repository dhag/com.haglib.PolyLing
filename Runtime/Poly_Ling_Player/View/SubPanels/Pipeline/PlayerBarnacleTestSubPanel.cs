// PlayerBarnacleTestSubPanel.cs
// 藤壺（オブジェクト配置）自動検証。ボタン 1 回で
//   球を生成 → 頭頂に開始タグ三角形 → 小さい円錐と土台の円筒を作る
//   → 梯子を自動検出 → 各 rung へ円錐＋土台を配置（グループとして保持）
//   → 球と円錐を編集 → 要更新の検出 → 作り直す
// までを流す。共通部は PlayerBeltGroupTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【断面プロファイルを使わない】
//   藤壺は rung ごとに既存オブジェクトを複製配置する図形で、断面を持たない。
//   基底の断面 3 段（作る / 待つ / 取り込む）は段リストに入れない。
//
// 【配置フレームと配置物の向き】
//   rung ごとの系は 位置 = rung 中心 / Z 軸 = rung 法線 / X 軸 = rung 方向
//   （PlaceObjectMeshGenerator.cs:5-13）。つまり配置物のローカル +Z が
//   面の外向きになる。円柱は Y 軸で生成されるので、円錐も土台も
//   PlaceRotation = (90,0,0) を頂点へ焼き込んで +Y を +Z へ倒す。
//
// 【上下の極には終了三角形が要らない】
//   球は上下とも極で三角形の扇になる（重複頂点の結合が極の四角形を畳む）。
//   縦走査は「相手面が三角形」で止まるので、頭頂に開始タグを 1 枚置けば
//   下の極まで走って自然に終わる。
//
// 【3 つのソースが全部追随する】
//   CreatePlaceObjectCommand.SourceMasterIndices には IsMeshRef が付いているので、
//   円錐と土台も ObjectId で控えられる。梯子の取り込み元（球）と合わせて 3 つが
//   グループの入力になり、どれを直しても「要更新」になる。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.PlaceObject;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    public class PlayerBarnacleTestSubPanel : PlayerBeltGroupTestSubPanelBase
    {
        private const string BallName    = "BT_Ball";
        private const string ConeName    = "BT_Cone";
        private const string BaseName    = "BT_Base";
        private const string PlaceName   = "BT_Barnacle";

        // UI 自動操作の ID は "barnacleTest.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ・書き込み先・退避）は基底クラス側で登録する。
        [UiControl("ballRadius", Description = "球（梯子の元）の半径")]
        private FloatField   _ballRadius;
        [UiControl("coneRadius", Description = "円錐の底面半径")]
        private FloatField   _coneRadius;
        [UiControl("coneHeight", Description = "円錐の高さ")]
        private FloatField   _coneHeight;
        [UiControl("baseHeight", Description = "土台の高さ")]
        private FloatField   _baseHeight;
        [UiControl("placeScale", Description = "配置の倍率")]
        private FloatField   _placeScale;
        [UiControl("squash", Description = "球を上下に潰す率（0 で潰さない。作り直しの検証用）")]
        private FloatField   _squash;
        [UiControl("coneGrow", Description = "円錐を伸ばす率（0 で伸ばさない。作り直しの検証用）")]
        private FloatField   _coneGrow;
        [UiControl("longitude", Description = "経線の分割（＝房の本数）")]
        private IntegerField _longitude;
        [UiControl("latitude", Description = "緯線の分割")]
        private IntegerField _latitude;
        [UiControl("rungStride", Description = "rung の間引き間隔（1 で全部）")]
        private IntegerField _rungStride;
        [UiControl("rollSteps", Description = "ロール段数（90°単位・0〜3）")]
        private IntegerField _rollSteps;
        [UiControl("uniformScale", Description = "倍率を一定にする（rung 長に比例させない）")]
        private Toggle       _uniformScale;

        private int _coneIndex = -1;
        private int _baseIndex = -1;
        private int _expectedLadders;

        private List<BeltAutoStrip> _strips;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "藤壺自動検証";

        protected override string NoteText =>
            "球 → 頭頂に開始タグ三角形 → 小さい円錐＋土台の円筒\n"
          + "→ 梯子を自動検出 → 各 rung へ配置（グループとして保持）\n"
          + "→ 球と円錐を編集 → 要更新の検出 → 作り直し、までを通しで流します。";

        protected override string SourceObjectName  => BallName;
        protected override string ExpectedAction    => "createPlaceObject";

        // ── 断面を使わないので読まれない（基底の CollectStages の注記を参照）
        protected override string ProfileObjectName => "";
        protected override bool   ProfileIsClosed   => false;
        protected override List<Vector2> BuildProfilePoints() => null;

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("球（梯子の元）"));
            _ballRadius = F("半径", 0.60f);
            _longitude  = I("経線の分割（＝房の本数）", 12);
            _latitude   = I("緯線の分割", 10);
            root.Add(_ballRadius); root.Add(_longitude); root.Add(_latitude);

            root.Add(Sec("配置する物体"));
            _coneRadius = F("円錐の底面半径", 0.10f);
            _coneHeight = F("円錐の高さ",     0.16f);
            _baseHeight = F("土台の高さ",     0.04f);
            root.Add(_coneRadius); root.Add(_coneHeight); root.Add(_baseHeight);

            root.Add(Sec("配置"));
            _placeScale   = F("倍率", 1.0f);
            _uniformScale = new Toggle("倍率を一定にする（rung 長に比例させない）") { value = false };
            _rungStride   = I("rung の間引き間隔（1 で全部）", 1);
            _rollSteps    = I("ロール段数（90°単位・0〜3）", 0);
            root.Add(_placeScale); root.Add(_uniformScale); root.Add(_rungStride); root.Add(_rollSteps);

            root.Add(Sec("ソースの編集（作り直しの検証用）"));
            _squash   = F("球を上下に潰す率（0 で潰さない）", 0.35f);
            _coneGrow = F("円錐を伸ばす率（0 で伸ばさない）", 0.60f);
            root.Add(_squash); root.Add(_coneGrow);
        
            AddKeepStashToggle(root);
}

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. 球を生成",                       StageCreateBall));
            stages.Add(("2. 球の生成完了を待つ",             StageWaitBall));
            stages.Add(("3. 頭頂に開始タグ三角形を追加",     StageAddStartTag));
            stages.Add(("4. 小さい円錐を生成",               StageCreateCone));
            stages.Add(("5. 円錐の生成完了を待つ",           StageWaitCone));
            stages.Add(("6. 土台の円筒を生成",               StageCreateBase));
            stages.Add(("7. 土台の生成完了を待つ",           StageWaitBase));
            stages.Add(("8. 梯子を自動検出して本数を検査",   StageDetectLadders));
            stages.Add(("9. 梯子へ円錐＋土台を配置",         StagePlaceObjects));
            stages.Add(("10. グループが出来たか検査",        StageVerifyGroup));
            stages.Add(("11. ソース（球と円錐）を編集",      StageEditSource));
            stages.Add(("12. 要更新になったか検査",          StageVerifyStale));
            stages.Add(("13. グループを作り直す",            StageRebuild));
            stages.Add(("14. 作り直しの結果を検査",          StageVerifyRebuild));
        }

        // ================================================================
        // 段 1-2: 球
        // ================================================================

        private StageResult StageCreateBall()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            _expectedLadders = Mathf.Max(4, _longitude.value);

            var prms = SphereMeshGenerator.SphereParams.Default;
            prms.MeshName          = BallName;
            prms.Radius            = _ballRadius.value;
            prms.CubeSphere        = false;
            prms.LongitudeSegments = _expectedLadders;
            prms.LatitudeSegments  = Mathf.Max(4, _latitude.value);

            var pl = PrimitivePlacement.Default;
            pl.AddMode                = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup            = false;
            pl.MergeDuplicateVertices = true;   // 極の四角形を三角形へ畳むために要る

            SendCommand(new CreateSphereCommand(ModelIndex, prms, pl));

            return Ok(
                $"CreateSphereCommand を送った（半径{prms.Radius:0.###} / 経線{prms.LongitudeSegments} / "
              + $"緯線{prms.LatitudeSegments}）",
                "図形生成パネル → 図形「球」→ 経線・緯線の分割を決めて生成",
                "経線 1 列がそのまま房（梯子）1 本になるので、経線分割が房の本数になる。"
              + "重複頂点の結合は必須。球の極では全経線の頂点が 1 点に重なっており、"
              + "結合を通すと極の四角形が三角形へ畳まれて『先端が三角形の扇』になる。"
              + "上下とも扇になるので、下側には終了三角形を置かなくても縦走査が止まる。");
        }

        private StageResult StageWaitBall()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= MeshCountBefore) return StageResult.Retry;

            SourceIndex = FindByName(model, BallName);
            if (SourceIndex < 0) return StageResult.Retry;

            var mo = model.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            MeshCountBefore = model.MeshContextCount;

            int tri = 0;
            for (int i = 0; i < mo.FaceCount; i++)
                if (mo.Faces[i].VertexCount == 3) tri++;

            return Ok(
                $"「{BallName}」が出来た（索引 {SourceIndex} / 頂点 {mo.VertexCount} / "
              + $"面 {mo.FaceCount} / うち三角形 {tri}）",
                null,
                $"三角形は上下の極の扇。{_expectedLadders} × 2 枚あるはず。"
              + "0 なら重複頂点の結合が効いていない。");
        }

        // ================================================================
        // 段 3: 開始タグ三角形
        // ================================================================

        private StageResult StageAddStartTag()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("球が見つからない", null, null);

            // 極頂点 = 面に使われている頂点のうち最も Y が高いもの。
            int pole = -1;
            float bestY = float.MinValue;
            var used = new HashSet<int>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null) continue;
                for (int k = 0; k < face.VertexIndices.Count; k++) used.Add(face.VertexIndices[k]);
            }
            foreach (int vi in used)
            {
                if (vi < 0 || vi >= mo.VertexCount) continue;
                float y = mo.Vertices[vi].Position.y;
                if (y > bestY) { bestY = y; pole = vi; }
            }
            if (pole < 0) return Ng("頭頂の頂点を特定できなかった", null, null);

            int fanCount = 0;
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount != 3) continue;
                if (face.VertexIndices.Contains(pole)) fanCount++;
            }

            Vector3 p = mo.Vertices[pole].Position;
            float r = Mathf.Max(0.01f, _ballRadius.value);

            // 浮遊頂点 2 個。既存面と辺も頂点も共有しない位置に置く。
            Vector3 a = p + new Vector3( 0.06f * r, 0.20f * r, 0f);
            Vector3 b = p + new Vector3(-0.06f * r, 0.20f * r, 0f);

            // 面追加は「編集対象メッシュ」にしか効かない（AddFaceToolHandler.cs:270）。
            SendCommand(new SelectMeshCommand(ModelIndex, MeshCategory.Drawable, new[] { SourceIndex }));

            SendCommand(new AddFaceCommand(
                ModelIndex, new[] { SourceIndex },
                Poly_Ling.Tools.AddFaceMode.Triangle,
                new[] { pole, -1, -1 },
                new[] { p.x, p.y, p.z, a.x, a.y, a.z, b.x, b.y, b.z },
                materialIndex: 0,
                viewPosition: p + Vector3.up * (10f * r)));

            return Ok(
                $"頭頂の頂点（索引 {pole} / Y={p.y:0.###}）に開始タグ三角形を 1 枚追加した。"
              + $"極に接する三角形は {fanCount} 枚",
                "面追加ツールで、頭頂の頂点と空中の 2 点を結んで三角形を作るのと同じ",
                "開始タグ三角形の条件は「辺を 1 本も他面と共有せず、他面と共有する頂点がちょうど 1 個」。"
              + "頭頂の頂点 P と新しい浮遊頂点 2 個で作れば満たす。"
              + "これで P が梯子の起点として確定し、P に接する扇の三角形が"
              + "それぞれ 1 本の房の第 1 rung になる。タグ三角形自体は梯子に含まれない。");
        }

        // ================================================================
        // 段 4-7: 配置する物体
        // ================================================================

        /// <summary>
        /// 配置物は Y 軸で作って +Z へ倒す。
        /// 配置フレームの Z 軸が rung 法線（面の外向き）なので、
        /// ローカル +Z を上向きにしておかないと球の表面へ寝てしまう。
        /// 回転は頂点へ焼き込む（BakeRotation）。姿勢に残すと配置元の解決で無視される。
        /// </summary>
        private static PrimitivePlacement UprightPlacement()
        {
            var pl = PrimitivePlacement.Default;
            pl.AddMode       = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup   = false;
            pl.PlaceRotation = new Vector3(90f, 0f, 0f);
            pl.BakeRotation  = true;
            return pl;
        }

        private StageResult StageCreateCone()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            var prms = CylinderMeshGenerator.CylinderParams.Default;
            prms.MeshName       = ConeName;
            prms.RadiusTop      = 0f;                    // 0 で円錐になる
            prms.RadiusBottom   = _coneRadius.value;
            prms.Height         = _coneHeight.value;
            prms.RadialSegments = 8;
            prms.HeightSegments = 1;
            prms.CapTop         = false;
            prms.CapBottom      = true;
            prms.EdgeRadius     = 0f;
            // 底面を原点へ。ピボットは -Pivot.y × 高さ の平行移動になる。
            prms.Pivot          = new Vector3(0f, -0.5f, 0f);

            SendCommand(new CreateCylinderCommand(ModelIndex, prms, UprightPlacement()));

            return Ok(
                $"CreateCylinderCommand を送った（上の半径 0 ＝円錐 / 底面半径{prms.RadiusBottom:0.###} / "
              + $"高さ{prms.Height:0.###} / 底フタあり）。ピボットで底面を原点へ寄せ、"
              + "(90,0,0) を焼き込んで +Y を +Z へ倒した",
                "図形生成パネル → 図形「円柱」→ 上面の半径を 0 にする → ピボット Y を -0.5 "
              + "→ 姿勢の回転 X を 90 にして「回転を焼き込む」をオン → 生成",
                "配置フレームは Z 軸が rung 法線なので、配置物のローカル +Z が球の外向きになる。"
              + "円柱は Y 軸で作られるため倒す必要がある。"
              + "回転を姿勢に残すと配置元の解決で無視される（配置元は MeshObject だけを読む）ので、"
              + "頂点へ焼き込む。");
        }

        private StageResult StageWaitCone() => WaitObject(ConeName, i => _coneIndex = i);

        private StageResult StageCreateBase()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            var prms = CylinderMeshGenerator.CylinderParams.Default;
            prms.MeshName       = BaseName;
            prms.RadiusTop      = _coneRadius.value * 2f;   // 円錐の底面の 2 倍の直径
            prms.RadiusBottom   = _coneRadius.value * 2f;
            prms.Height         = _baseHeight.value;
            prms.RadialSegments = 8;
            prms.HeightSegments = 1;
            prms.CapTop         = true;
            prms.CapBottom      = true;
            prms.EdgeRadius     = 0f;
            // 上面を原点へ。円錐の底に接する位置になる。
            prms.Pivot          = new Vector3(0f, 0.5f, 0f);

            SendCommand(new CreateCylinderCommand(ModelIndex, prms, UprightPlacement()));

            return Ok(
                $"CreateCylinderCommand を送った（半径{prms.RadiusBottom:0.###}＝円錐の底面の 2 倍の直径 / "
              + $"高さ{prms.Height:0.###} / 両面フタあり）。ピボットで上面を原点へ寄せた",
                "図形生成パネル → 図形「円柱」→ 半径を円錐の 2 倍 → ピボット Y を +0.5 "
              + "→ 姿勢の回転 X を 90 にして「回転を焼き込む」をオン → 生成",
                "円錐は底面が原点、土台は上面が原点。どちらも同じ向きへ倒してあるので、"
                + "結合すると土台の上に円錐が乗った 1 個の藤壺になる。"
              + "土台の本体は原点より -Z 側（球の内側）へ入るので、球面から浮かない。");
        }

        private StageResult StageWaitBase() => WaitObject(BaseName, i => _baseIndex = i);

        /// <summary>生成の完了を待つ共通形。名前で引けて面を持つまで繰り返す。</summary>
        private StageResult WaitObject(string name, Action<int> setIndex)
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= MeshCountBefore) return StageResult.Retry;

            int idx = FindByName(model, name);
            if (idx < 0) return StageResult.Retry;

            var mo = model.GetMeshContext(idx)?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            setIndex(idx);
            MeshCountBefore = model.MeshContextCount;

            // 倒したあとの Z 範囲を出す。0 をまたいでいれば向きは合っている。
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                float z = mo.Vertices[i].Position.z;
                minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
            }

            return Ok(
                $"「{name}」が出来た（索引 {idx} / 頂点 {mo.VertexCount} / "
              + $"Z 範囲 {minZ:0.###}〜{maxZ:0.###}）",
                null,
                "Z 範囲が 0 を含む向きになっていれば、配置フレームの外向き（+Z）に立つ。");
        }

        // ================================================================
        // 段 8: 梯子の自動検出
        // ================================================================

        private StageResult StageDetectLadders()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("球が見つからない", null, null);

            // 房どうしは独立させたいので上下展開はしない（cross = false）。
            _strips = BeltStackDetector.Detect(mo, crossRows: false, out string msg);

            if (_strips == null || _strips.Count == 0)
                return Ng($"梯子を検出できなかった（{msg}）",
                    "図形生成パネル → 藤壺 → 「はしごを自動検索」",
                    "開始タグ三角形が無い、辺を他面と共有している、"
                  + "頂点を 2 個以上共有している、のいずれかだと起点が決まらない。");

            if (_strips.Count != _expectedLadders)
                return Ng($"梯子が {_strips.Count} 本。{_expectedLadders} 本になるはず（{msg}）",
                    null,
                    "本数は『極に接する三角形の数』で決まる。段 3 のログに出した扇の枚数と"
                  + "食い違うなら、経線分割の指定か重複頂点の結合が効いていない。");

            int rungMin = int.MaxValue, rungMax = 0, total = 0;
            foreach (var st in _strips)
            {
                rungMin = Mathf.Min(rungMin, st.RungCount);
                rungMax = Mathf.Max(rungMax, st.RungCount);
                total  += st.RungCount;
            }

            return Ok(
                $"梯子 {_strips.Count} 本を検出（rung {rungMin}〜{rungMax} / 合計 {total}）。{msg}",
                "図形生成パネル → 藤壺 → 「はしごを自動検索」",
                "縦走査は四角形の対辺を辿り、下の極の三角形に当たって止まる。"
              + $"間引きが 1 なら配置は合計 {total} 個になる。");
        }

        // ================================================================
        // 段 9: 配置
        // ================================================================

        private StageResult StagePlaceObjects()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("球が見つからない", null, null);
            if (_coneIndex < 0 || _baseIndex < 0) return Ng("配置元が揃っていない", null, null);

            var acquired = BeltAcquire.Acquire(
                model.GetMeshContext(SourceIndex), BeltAcquireMethod.AutoLadder, false, "");
            if (!acquired.Ok)
                return Ng($"取り込みを掛け直せなかった（{acquired.Message}）", null, null);

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var bL, out var bR, out var bStarts,
                out var bClosed, out var bFlip, out var bHeight);

            var pp = PlaceObjectParams.Default;
            pp.MeshName        = PlaceName;
            pp.Mode            = PlaceSourceMode.Combine;   // 円錐＋土台を 1 個に結合する
            pp.ScaleMode       = _uniformScale.value ? PlaceScaleMode.Uniform : PlaceScaleMode.RungLength;
            pp.Scale           = Mathf.Max(PlaceObjectParams.ScaleMin, _placeScale.value);
            pp.RungStride      = Mathf.Clamp(_rungStride.value,
                                             PlaceObjectParams.StrideMin, PlaceObjectParams.StrideMax);
            pp.RollSteps       = Mathf.Clamp(_rollSteps.value,
                                             PlaceObjectParams.RollStepsMin, PlaceObjectParams.RollStepsMax);
            pp.IncludeChildren = false;   // 子を持たせていないので広げない

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;   // ← ここがグループを残す指定

            SendCommand(new CreatePlaceObjectCommand(
                ModelIndex, pp,
                new[] { _coneIndex, _baseIndex },
                bL, bR, bStarts, bClosed, bFlip, bHeight,
                BeltOrientOptions.Default, BeltSplineOptions.Default,
                pl,
                SourceIndex, BeltAcquireMethod.AutoLadder, false, ""));

            return Ok(
                $"梯子 {acquired.Belts.Count} 本へ CreatePlaceObjectCommand を送った。"
              + $"配置元は索引 {_coneIndex}（円錐）と {_baseIndex}（土台）を結合、"
              + $"倍率{pp.Scale:0.###}（{(pp.ScaleMode == PlaceScaleMode.Uniform ? "一定" : "rung 長に比例")}）、"
              + $"rung 間引き {pp.RungStride}、ロール {pp.RollSteps}",
                "図形生成パネル → 藤壺 → 「はしごを自動検索」で梯子を拾う → 配置元に円錐と土台を"
              + "チェック → 割り当て方式を「結合」→ 『オブジェクトグループとして残す』をオン → 生成",
                "配置元は索引で指すが、グループは PLParam(IsMeshRef) を見て ObjectId へ置き換えて控える。"
              + "だから梯子の元（球）だけでなく、円錐と土台もグループの入力になる。"
              + "どれを直しても『要更新』になり、作り直しで反映される。"
              + "rung 長に比例させると、極に近いほど rung が短いので藤壺も小さくなる。");
        }

        // ================================================================
        // 段 11: ソースを動かす
        // ================================================================

        /// <summary>
        /// 球を上下に潰し、円錐を伸ばす。コマンドではなく頂点を直接動かす検証用の段。
        /// 梯子の元と配置元の両方を触るので、どちらの変更も追随することを見せられる。
        /// </summary>
        protected override StageResult StageEditSource()
        {
            var model = GetModel();
            var ball  = model?.GetMeshContext(SourceIndex)?.MeshObject;
            var cone  = model?.GetMeshContext(_coneIndex)?.MeshObject;
            if (ball == null) return Ng("球が見つからない", null, null);
            if (cone == null) return Ng("円錐が見つからない", null, null);

            float squash = Mathf.Clamp01(_squash.value);
            float grow   = Mathf.Max(0f, _coneGrow.value);

            int ballMoved = 0;
            if (squash > 0f)
            {
                float k = 1f - squash;
                for (int i = 0; i < ball.VertexCount; i++)
                {
                    var v = ball.Vertices[i];
                    var p = v.Position;
                    v.Position = new Vector3(p.x, p.y * k, p.z);
                    ballMoved++;
                }
            }

            // 円錐は先端（+Z 側）だけを伸ばす。底面（z=0）は動かさない。
            int coneMoved = 0;
            if (grow > 0f)
            {
                float maxZ = float.MinValue;
                for (int i = 0; i < cone.VertexCount; i++)
                    maxZ = Mathf.Max(maxZ, cone.Vertices[i].Position.z);

                if (maxZ > 1e-6f)
                {
                    for (int i = 0; i < cone.VertexCount; i++)
                    {
                        var v = cone.Vertices[i];
                        var p = v.Position;
                        if (p.z <= 1e-6f) continue;
                        v.Position = new Vector3(p.x, p.y, p.z * (1f + grow));
                        coneMoved++;
                    }
                }
            }

            if (ballMoved == 0 && coneMoved == 0)
                return Ng("どちらも動かさなかった", null,
                    "潰す率と伸ばす率が両方 0。1e-5 より大きい値にする。");

            return Ok(
                $"球の頂点 {ballMoved} 個を上下に {squash:P0} 潰し、"
              + $"円錐の先端側 {coneMoved} 個を {grow:P0} 伸ばした",
                "ビューポートで球を選んで Y 方向へ縮め、円錐を選んで先端を引き伸ばすのと同じ",
                "動かしたのは入力（梯子の元と配置元）だけ。藤壺の出力先はまだ古い形のまま。"
              + "グループはどちらの食い違いも見つけられるはず、というのが次の段の検査。");
        }
    }
}
