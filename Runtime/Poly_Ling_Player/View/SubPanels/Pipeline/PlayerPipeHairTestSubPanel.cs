// PlayerPipeHairTestSubPanel.cs
// 前髪パイプ自動検証。ボタン 1 回で
//   球を生成 → 下半分と後ろ半分を削除して四分球にする
//   → 頭頂に開始タグ三角形、各房の最下段に終了三角形を足す
//   → 断面プロファイル（正八角形を Y 方向 0.25 倍）を描画オブジェクトとして作る
//   → 梯子を自動検出（20 本）→ パイプを生成（グループとして残す）
//   → 毛先を前へ垂らす → 要更新の検出 → 作り直す
// までを流す。共通部は PlayerBeltGroupTestSubPanelBase が持つ。
// Runtime/Poly_Ling_Player/View/SubPanels/Pipeline/ に配置
//
// 【なぜ四分球か】
//   房（梯子）は経線方向に走る四角形の列。球の側面がそのまま帯になる。
//   下半分を落とすと毛先が赤道で終わり、後ろ半分を落とすと前髪だけが残る。
//   経線分割を 40 にすると、前半分に経線列が 20 本＝房 20 本になる。
//
// 【なぜ頭頂の四角形が三角形になるか】
//   SphereMeshGenerator は極で全経線の頂点が 1 点に重なる（SphereMeshGenerator.cs:99-107）。
//   重複頂点の結合が連続する同一頂点を畳むので（MeshMergeHelper.cs:255-271）、
//   極の四角形 (P,P,r1,r0) は (P,r1,r0) の三角形になる。
//   つまり生成の時点で「先端が三角形の扇」になっており、
//   BeltStackDetector が期待する形（BeltStackDetector.cs:6）とそのまま一致する。
//
// 【開始タグ三角形】
//   条件は「辺を 1 本も他面と共有せず、他面と共有する頂点がちょうど 1 個」
//   （BeltStackDetector.cs:4）。極頂点 P と、新しい浮遊頂点 2 個で三角形を作れば満たす。
//   これで P が梯子の起点として確定し、P に接する 20 枚の三角形が
//   それぞれ 1 本の房の第 1 rung になる。
//
// 【終了三角形】
//   縦走査は「相手面が三角形」で終わり、その先端を終了点として保持する
//   （BeltStackDetector.cs:14-16）。各房の最下段の自由辺に三角形を 1 枚足すと、
//   房ごとに終了点が付き、スプライン分割の UseLast が毛先まで伸びる。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Pipe;
using Poly_Ling.PrimitiveMesh;

namespace Poly_Ling.Player
{
    public class PlayerPipeHairTestSubPanel : PlayerBeltGroupTestSubPanelBase
    {
        private const string HeadName    = "HT_Head";
        private const string ProfileName = "HT_Profile";
        private const string PipeName    = "HT_Hair";

        /// <summary>残す側。PMX のモデルは −Z を向くので既定は −Z。</summary>
        private enum FrontAxis { MinusZ = 0, PlusZ = 1 }

        // UI 自動操作の ID は "pipeHairTest.<下の Id>"（UiControlAttribute.cs）。
        // 共通の項目（実行・状態・ログ・書き込み先・退避）は基底クラス側で登録する。
        [UiControl("radius", Description = "四分球（頭）の半径")]
        private FloatField    _radius;
        [UiControl("profileFlatten", Description = "断面の Y 方向の倍率")]
        private FloatField    _profileFlatten;
        [UiControl("dropForward", Description = "毛先を前へ垂らす量（作り直しの検証用）")]
        private FloatField    _dropForward;
        [UiControl("strands", Description = "房の本数")]
        private IntegerField  _strands;
        [UiControl("latitude", Description = "緯線の分割（上下）")]
        private IntegerField  _latitude;
        [UiControl("splineSegments", Description = "段間の補間数（1 で rung ほぼ 2 倍）")]
        private IntegerField  _splineSegments;
        [UiControl("front", Description = "前の向き")]
        private EnumField     _front;
        [UiControl("profileClosed", Description = "断面を閉ループにする（筒にする）")]
        private Toggle        _profileClosed;
        [UiControl("capEnds", Description = "開いた梯子の両端に蓋を張る")]
        private Toggle        _capEnds;

        // 段をまたいで持ち回る
        private int   _expectedStrands;
        private ulong _headObjectId;

        // ================================================================
        // 派生が決めるもの
        // ================================================================

        protected override string TitleText => "前髪パイプ自動検証";

        protected override string NoteText =>
            "球 → 四分球（下半分・後ろ半分を削除）→ 開始タグ／終了三角形を追加\n"
          + "→ 断面プロファイル（描画オブジェクト）→ 梯子を自動検出 → パイプ生成（グループとして保持）\n"
          + "→ 毛先を動かす → 要更新の検出 → 作り直し、までを通しで流します。";

        protected override string SourceObjectName  => HeadName;
        protected override string ProfileObjectName => ProfileName;
        protected override bool   ProfileIsClosed   => _profileClosed == null || _profileClosed.value;
        protected override string ExpectedAction    => "createPipe";

        protected override void BuildOptionsUI(VisualElement root)
        {
            root.Add(Sec("四分球（頭）"));
            _radius   = F("半径", 0.60f);
            _strands  = I("房の本数", 20);
            _latitude = I("緯線の分割（上下）", 16);
            _front    = new EnumField("前の向き", FrontAxis.MinusZ);
            _front.style.fontSize = 11;
            root.Add(_radius); root.Add(_strands); root.Add(_latitude); root.Add(_front);

            root.Add(Sec("断面（正八角形）"));
            _profileFlatten = F("Y 方向の倍率", 0.25f);
            _profileClosed  = new Toggle("断面を閉ループにする（筒にする）") { value = true };
            _capEnds        = new Toggle("開いた梯子の両端に蓋を張る") { value = true };
            root.Add(_profileFlatten); root.Add(_profileClosed); root.Add(_capEnds);

            root.Add(Sec("スプライン分割"));
            _splineSegments = I("段間の補間数（1 で rung ほぼ 2 倍）", 1);
            root.Add(_splineSegments);

            root.Add(Sec("ソースの編集（作り直しの検証用）"));
            _dropForward = F("毛先を前へ垂らす量", 0.30f);
            root.Add(_dropForward);
        
            AddKeepStashToggle(root);
}

        /// <summary>
        /// 正八角形を Y 方向へ潰したもの。
        /// 取り込みの正規化（NormalizeToUnitSpan）は等方スケールなので、
        /// ここで付けた 1 : 倍率 の扁平はそのまま残る。
        /// </summary>
        protected override List<Vector2> BuildProfilePoints()
        {
            float flat = (_profileFlatten != null) ? _profileFlatten.value : 0.25f;
            var pts = new List<Vector2>(8);
            for (int i = 0; i < 8; i++)
            {
                // 頂点が上下左右に来ない向き（辺が水平になる向き）にする。
                float a = (i + 0.5f) * Mathf.PI * 2f / 8f;
                pts.Add(new Vector2(Mathf.Cos(a) * 0.5f, Mathf.Sin(a) * 0.5f * flat));
            }
            return pts;
        }

        protected override void CollectStages(List<(string Name, Func<StageResult> Run)> stages)
        {
            stages.Add(("1. 球を生成",                        StageCreateSphere));
            stages.Add(("2. 球の生成完了を待つ",              StageWaitSphere));
            stages.Add(("3. 下半分と後ろ半分の面を削除",      StageCutQuarter));
            stages.Add(("4. 頭頂に開始タグ三角形を追加",      StageAddStartTag));
            stages.Add(("5. 各房の最下段に終了三角形を追加",  StageAddEndTriangles));
            stages.Add(("6. 断面プロファイルを描画オブジェクトとして置く", StageCreateProfileObject));
            stages.Add(("7. プロファイルの生成完了を待つ",    StageWaitProfile));
            stages.Add(("8. 描画オブジェクトから断面を取り込む",           StageImportProfile));
            stages.Add(("9. 梯子を自動検出して本数を検査",    StageDetectLadders));
            stages.Add(("10. 四分球を梯子にしてパイプを生成", StageCreatePipe));
            stages.Add(("11. グループが出来たか検査",         StageVerifyGroup));
            stages.Add(("12. ソース（四分球）を編集",         StageEditSource));
            stages.Add(("13. 要更新になったか検査",           StageVerifyStale));
            stages.Add(("14. グループを作り直す",             StageRebuild));
            stages.Add(("15. 作り直しの結果を検査",           StageVerifyRebuild));
        }

        // ================================================================
        // 段 1-2: 球
        // ================================================================

        private StageResult StageCreateSphere()
        {
            var model = GetModel();
            MeshCountBefore = model?.MeshContextCount ?? 0;

            _expectedStrands = Mathf.Max(2, _strands.value);

            var prms = SphereMeshGenerator.SphereParams.Default;
            prms.MeshName          = HeadName;
            prms.Radius            = _radius.value;
            prms.CubeSphere        = false;
            // 前半分だけ残すので、経線分割は房の本数の 2 倍にする。
            prms.LongitudeSegments = _expectedStrands * 2;
            prms.LatitudeSegments  = Mathf.Max(4, _latitude.value);

            var pl = PrimitivePlacement.Default;
            pl.AddMode                = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup            = false;
            pl.MergeDuplicateVertices = true;   // 極の四角形を三角形へ畳むために要る

            SendCommand(new CreateSphereCommand(ModelIndex, prms, pl));

            return Ok(
                $"CreateSphereCommand を送った（半径{prms.Radius:0.###} / 経線{prms.LongitudeSegments} / "
              + $"緯線{prms.LatitudeSegments} / 重複頂点の結合オン）",
                "図形生成パネル → 図形「球」→ 経線・緯線の分割を決めて生成",
                $"前半分だけ残すので経線分割は房の本数の 2 倍（{_expectedStrands}×2）にする。"
              + "重複頂点の結合は必須。球の極では全経線の頂点が 1 点に重なっており、"
              + "結合を通すと極の四角形が三角形へ畳まれる。"
              + "この『先端が三角形の扇』が、あとで梯子を自動検出するときの足場になる。");
        }

        private StageResult StageWaitSphere()
        {
            var model = GetModel();
            if (model == null) return StageResult.Retry;
            if (model.MeshContextCount <= MeshCountBefore) return StageResult.Retry;

            SourceIndex = FindByName(model, HeadName);
            if (SourceIndex < 0) return StageResult.Retry;

            var ctx = model.GetMeshContext(SourceIndex);
            var mo  = ctx?.MeshObject;
            if (mo == null || mo.FaceCount == 0) return StageResult.Retry;

            _headObjectId   = ctx.ObjectId;
            MeshCountBefore = model.MeshContextCount;

            int tri = 0, quad = 0;
            for (int i = 0; i < mo.FaceCount; i++)
            {
                int n = mo.Faces[i].VertexCount;
                if (n == 3) tri++; else if (n == 4) quad++;
            }

            return Ok(
                $"「{HeadName}」が出来た（索引 {SourceIndex} / 頂点 {mo.VertexCount} / "
              + $"三角形 {tri} / 四角形 {quad}）",
                null,
                "三角形は上下の極の扇。四角形が帯の本体になる。"
              + "三角形が 0 なら重複頂点の結合が効いていない。");
        }

        // ================================================================
        // 段 3: 四分球にする
        // ================================================================

        private StageResult StageCutQuarter()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("球が見つからない", null, null);

            bool frontIsMinusZ = (FrontAxis)_front.value == FrontAxis.MinusZ;

            var doomed = new List<int>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount < 3) continue;

                Vector3 c = Vector3.zero;
                int n = 0;
                for (int k = 0; k < face.VertexIndices.Count; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= mo.VertexCount) continue;
                    c += mo.Vertices[vi].Position;
                    n++;
                }
                if (n == 0) continue;
                c /= n;

                bool keepUpper = c.y > 0f;
                bool keepFront = frontIsMinusZ ? (c.z < 0f) : (c.z > 0f);
                if (!keepUpper || !keepFront) doomed.Add(f);
            }

            if (doomed.Count == 0)
                return Ng("削除対象の面が 1 枚も無い", null, null);
            if (doomed.Count >= mo.FaceCount)
                return Ng("全部の面が削除対象になった", null,
                    "前の向きの判定が逆になっている可能性がある。「前の向き」を切り替えて試す。");

            SendCommand(new DeleteFacesCommand(ModelIndex, SourceIndex, doomed.ToArray()));

            return Ok(
                $"面 {mo.FaceCount} 枚のうち {doomed.Count} 枚を削除（残り {mo.FaceCount - doomed.Count} 枚）。"
              + $"下半分（重心 Y ≦ 0）と{(frontIsMinusZ ? "＋Z 側" : "−Z 側")}（重心が後ろ）を落とした",
                "ビューポートで面選択モードにして裏側と下側を選び、削除するのと同じ",
                "面の重心で判定する。四角形は経線 1 区間・緯線 1 区間ぶんなので、"
              + "重心の符号がそのまま『上下』『前後』と一致する。"
              + "PMX のモデルは −Z を向くので、前髪は −Z 側に残す。");
        }

        // ================================================================
        // 段 4: 開始タグ三角形
        // ================================================================

        private StageResult StageAddStartTag()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("四分球が見つからない", null, null);

            // 極頂点 = 面に使われている頂点のうち最も Y が高いもの。
            // 削除で孤立した頂点は面に現れないので拾わない。
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

            // 極に接する三角形の数 ＝ 房の本数になるはず。ここで確かめておくと、
            // 段 9 で本数が合わなかったときに原因を切り分けやすい。
            int fanCount = 0;
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount != 3) continue;
                if (face.VertexIndices.Contains(pole)) fanCount++;
            }

            Vector3 p = mo.Vertices[pole].Position;
            float r = Mathf.Max(0.01f, _radius.value);

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
              + "それぞれ 1 本の房の第 1 rung になる。"
              + "タグ三角形自体は梯子に含まれない。");
        }

        // ================================================================
        // 段 5: 終了三角形
        // ================================================================

        private StageResult StageAddEndTriangles()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("四分球が見つからない", null, null);

            // 最下段の自由辺を集める。自由辺 = 1 枚の面にしか属さない辺。
            // そのうち両端の Y が最も低い群が毛先側になる。
            var edgeUse = new Dictionary<(int, int), int>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount < 3) continue;
                var vi = face.VertexIndices;
                for (int k = 0; k < vi.Count; k++)
                {
                    int u = vi[k], v = vi[(k + 1) % vi.Count];
                    if (u == v) continue;
                    var key = u < v ? (u, v) : (v, u);
                    edgeUse.TryGetValue(key, out int c);
                    edgeUse[key] = c + 1;
                }
            }

            float minY = float.MaxValue;
            foreach (var kv in edgeUse)
            {
                if (kv.Value != 1) continue;
                float y = Mathf.Min(mo.Vertices[kv.Key.Item1].Position.y,
                                    mo.Vertices[kv.Key.Item2].Position.y);
                minY = Mathf.Min(minY, y);
            }
            if (minY == float.MaxValue) return Ng("自由辺が 1 本も無い", null, null);

            float r   = Mathf.Max(0.01f, _radius.value);
            float tol = 0.02f * r;

            var bottom = new List<(int A, int B)>();
            foreach (var kv in edgeUse)
            {
                if (kv.Value != 1) continue;
                var pa = mo.Vertices[kv.Key.Item1].Position;
                var pb = mo.Vertices[kv.Key.Item2].Position;
                // 最下段のリングは両端とも最低 Y にある（縦に走る切断面の辺は片端が上）。
                if (Mathf.Abs(pa.y - minY) > tol) continue;
                if (Mathf.Abs(pb.y - minY) > tol) continue;
                bottom.Add((kv.Key.Item1, kv.Key.Item2));
            }

            if (bottom.Count == 0)
                return Ng("最下段の自由辺を拾えなかった", null, null);

            // 面追加は「編集対象メッシュ」にしか効かない。
            SendCommand(new SelectMeshCommand(ModelIndex, MeshCategory.Drawable, new[] { SourceIndex }));

            foreach (var (ia, ib) in bottom)
            {
                Vector3 pa = mo.Vertices[ia].Position;
                Vector3 pb = mo.Vertices[ib].Position;
                Vector3 mid = (pa + pb) * 0.5f;

                // 先端は真下へ少し伸ばす。梯子の向きの延長になる位置に置く。
                Vector3 tip = mid + new Vector3(0f, -0.15f * r, 0f);

                SendCommand(new AddFaceCommand(
                    ModelIndex, new[] { SourceIndex },
                    Poly_Ling.Tools.AddFaceMode.Triangle,
                    new[] { ia, ib, -1 },
                    new[] { pa.x, pa.y, pa.z, pb.x, pb.y, pb.z, tip.x, tip.y, tip.z },
                    materialIndex: 0,
                    viewPosition: mid + new Vector3(0f, 0f, -10f * r)));
            }

            return Ok(
                $"最下段の自由辺 {bottom.Count} 本それぞれに終了三角形を追加した（Y≒{minY:0.###}）",
                "面追加ツールで、毛先の辺と少し下の 1 点を結んで三角形を作るのと同じ",
                "縦走査は「相手面が三角形」に当たると終わり、その先端を終了点として保持する。"
              + "終了三角形を置くと房ごとに終了点が付き、スプライン分割の"
              + "『末尾の rung を制御点に使う』が毛先まで効く。"
              + "終了三角形は四角形と辺を共有するので、開始タグ三角形とは区別される。");
        }

        // ================================================================
        // 段 9: 梯子の自動検出
        // ================================================================

        private List<BeltAutoStrip> _strips;

        private StageResult StageDetectLadders()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("四分球が見つからない", null, null);

            // 房どうしは独立させたいので上下展開はしない（cross = false）。
            _strips = BeltStackDetector.Detect(mo, crossRows: false, out string msg);

            if (_strips == null || _strips.Count == 0)
                return Ng($"梯子を検出できなかった（{msg}）",
                    "図形生成パネル → パイプ → 「はしごを自動検索」",
                    "開始タグ三角形が無い、辺を他面と共有している、"
                  + "頂点を 2 個以上共有している、のいずれかだと起点が決まらない。");

            if (_strips.Count != _expectedStrands)
                return Ng($"梯子が {_strips.Count} 本。{_expectedStrands} 本になるはず（{msg}）",
                    null,
                    "本数は『極に接する三角形の数』で決まる。段 4 のログに出した扇の枚数と"
                  + "食い違うなら、削除の判定か経線分割の指定が合っていない。");

            int rungMin = int.MaxValue, rungMax = 0, withEnd = 0;
            foreach (var st in _strips)
            {
                rungMin = Mathf.Min(rungMin, st.RungCount);
                rungMax = Mathf.Max(rungMax, st.RungCount);
                if (st.EndPoint >= 0) withEnd++;
            }

            return Ok(
                $"梯子 {_strips.Count} 本を検出（rung {rungMin}〜{rungMax} / 終了点あり {withEnd} 本）。{msg}",
                "図形生成パネル → パイプ → 「はしごを自動検索」",
                "開始タグ三角形が指す頂点に接する三角形が、それぞれ 1 本の房の入口になる。"
              + "縦走査は四角形の対辺を辿り、終了三角形に当たって止まる。"
              + "終了点を持つ本数が房の本数と一致していれば、段 5 が全房に効いている。");
        }

        // ================================================================
        // 段 10: パイプ生成
        // ================================================================

        private StageResult StageCreatePipe()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("四分球が見つからない", null, null);
            if (_strips == null || _strips.Count == 0) return Ng("梯子が無い", null, null);

            var acquired = BeltAcquire.Acquire(
                model.GetMeshContext(SourceIndex), BeltAcquireMethod.AutoLadder, false, "");
            if (!acquired.Ok)
                return Ng($"取り込みを掛け直せなかった（{acquired.Message}）", null, null);

            bool capEnds = _capEnds != null && _capEnds.value;

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var bL, out var bR, out var bStarts,
                out var bClosed, out var bFlip, out var bHeight);

            var pp = PipeParams.Default;
            pp.MeshName = PipeName;
            pp.CapEnds  = capEnds;

            var spline = BeltSplineOptions.Default;
            spline.Enabled  = true;
            spline.Segments = Mathf.Clamp(_splineSegments.value,
                                          BeltSplineOptions.SegmentsMin,
                                          BeltSplineOptions.SegmentsMax);
            spline.UseFirst = true;   // 起点（頭頂）を制御点に使う
            spline.UseLast  = true;   // 終了点（毛先）を制御点に使う

            var pl = PrimitivePlacement.Default;
            pl.AddMode     = PrimitiveAddMode.NewObject;
            pl.KeepAsGroup = true;   // ← ここがグループを残す指定

            SendCommand(new CreatePipeCommand(
                ModelIndex, pp,
                ProfilePoints.ToArray(), ProfileIsClosed,
                bL, bR, bStarts, bClosed, bFlip, bHeight,
                BeltOrientOptions.Default, spline,
                pl,
                SourceIndex, BeltAcquireMethod.AutoLadder, false, ""));

            OutputIdBefore = 0UL;

            int rungBefore = _strips[0].RungCount;
            int rungAfter  = (rungBefore + 2 - 1) * (spline.Segments + 1) + 1;

            return Ok(
                $"梯子 {acquired.Belts.Count} 本＋断面 {ProfilePoints.Count} 点（{(ProfileIsClosed ? "閉ループ" : "開いた折れ線")}）で "
              + $"CreatePipeCommand を送った。スプライン分割 {spline.Segments}（rung {rungBefore} → 約 {rungAfter}）、"
              + $"蓋={(capEnds ? "張る" : "張らない")}、取り込み方=はしごの自動検索、取り込み元は索引 {SourceIndex}",
                "図形生成パネル → パイプ → 「はしごを自動検索」→ 断面を取り込む "
              + "→ スプライン分割をオンにする → 『オブジェクトグループとして残す』をオン → 生成",
                "rung 数は (点数-1)×(段間の補間数+1)+1 で増える。起点と終了点を制御点に含めるので"
              + "点数は梯子の rung 数＋2 になる。"
              + "断面座標は rung 長で正規化された系。x が rung 方向、y が帯の法線方向で、"
              + "正八角形を Y 方向へ潰してあるので毛束が扁平になる。"
              + "蓋は開いた梯子の両端にだけ効く。");
        }

        // ================================================================
        // 段 12: ソースを動かす
        // ================================================================

        /// <summary>
        /// 毛先を前へ垂らす。コマンドではなく頂点を直接動かす検証用の段。
        /// 確かめたいのは「ソースが変わったことをグループが気づけるか」だけなので経路は問わない。
        /// </summary>
        protected override StageResult StageEditSource()
        {
            var model = GetModel();
            var mo    = model?.GetMeshContext(SourceIndex)?.MeshObject;
            if (mo == null) return Ng("四分球が見つからない", null, null);

            bool frontIsMinusZ = (FrontAxis)_front.value == FrontAxis.MinusZ;
            float amount = _dropForward.value * (frontIsMinusZ ? -1f : 1f);

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                float y = mo.Vertices[i].Position.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float span = Mathf.Max(1e-6f, maxY - minY);

            int moved = 0;
            for (int i = 0; i < mo.VertexCount; i++)
            {
                var v = mo.Vertices[i];
                var p = v.Position;

                // 下ほど強く前へ。毛先が顔の前へ垂れる。
                float t = 1f - (p.y - minY) / span;
                if (t <= 0f) continue;

                v.Position = p + new Vector3(0f, 0f, amount * t * t);
                moved++;
            }

            return Ok(
                $"四分球の頂点 {moved} 個を前へ最大 {Mathf.Abs(amount):0.###} 倒した"
              + $"（{(frontIsMinusZ ? "−Z" : "+Z")} 方向 / 下ほど強く）",
                "ビューポートで四分球を選び、移動や曲げのツールで毛先を前へ倒すのと同じ",
                "ここで動かしたのは入力（四分球）だけ。パイプの出力先はまだ古い形のまま。"
              + "グループはこの食い違いを見つけられるはず、というのが次の段の検査。");
        }
    }
}
