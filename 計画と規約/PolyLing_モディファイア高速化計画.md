# PolyLing モディファイア高速化計画

2026-10-31 作成。モディファイア（ModifierStack の項目）の評価が遅い件の計画。正本はこのファイル。

## 原因（コードで確認した事実）

- 書き戻し（`PrimitiveMeshReplaceExisting`）は既存オブジェクトの中身の入れ替えで、MeshObject の実体も一覧も変えない。
  それなのに末尾の `PrimitiveMeshFinalize` が新規生成用の重量級入口 `EnterSceneReset`（`RebuildAdapter`）と
  `RebuildModelList`・`NotifyPanels(ListStructure)` を呼ぶ。項目ごとに全バッファの作り直しが走る。
- `RunModifierEvaluation` はパスの最後に無条件で `EnterTopologyChanged` を呼ぶ。
  直後のコマンドの終わりの `SettleWorldPositions` が位相の有無（`BuffersMatchModel`）で判定し直している。
- `RebuildAdapter` のたびに `ModifierEvaluator.RefreshModel` が全入力の署名を取り、
  サブディビジョンの子を入力の変化に関わらず作り直す。
- 位置だけの転送経路は法線とウェイトを送らない。位相が同じ書き戻しを位置の経路へ載せるには、
  法線・ウェイトの部分転送が要る（法線の部分転送 API は無い）。
- 頂点ドラッグを離すと DragEnd が 3 回（`PresentAll` 3 回）、評価の走査が 2 回走る。

## 表示の入口（既存の規約）

位置だけ … `SettleWorldPositions`／`EnterVerticesMoved`。頂点属性だけ … `EnterVertexAttributesChanged`。
位相 … `EnterTopologyChanged`。新規生成・読み込み … `EnterSceneReset`。書き戻しもこの区分に従わせる。

## 段

- 段 0：計測。パスごとの `RebuildAdapter` 回数・`RefreshModel` 回数・経過 ms を `lastPass` に出す。
- 段 1：法線の部分転送 `UnifiedBufferManager.UpdateNormals` を足し、`EnterVertexAttributesChanged` に法線の指定を足す。
- 段 2：書き戻しの表示を位相の有無で振り分ける。`PrimitiveMeshReplaceExisting` から `PrimitiveMeshFinalize` を外し
  （材質 0 件の補充だけ残す）、位相が同じならウェイト・法線の部分転送、違えばコマンドの終わりの
  `SettleWorldPositions` に作り直しを任せる。ミラー側も同じ。
- 段 3：`RunModifierEvaluation` の無条件の `EnterTopologyChanged` を `SettleWorldPositions` に置き換える。
  一覧の通知は一覧が変わったときだけ 1 回。
- 段 4：サブディビジョンの Refresh を、入力の変化・初回・控えの不一致のときだけにする。
- 段 5：評価の走査を 1 パス 1 回にする（グラフの受け渡し、署名の 1 回取り、未確定の判定の対象を絞る、
  ダイジェストはグループごとに 1 回、宣言漏れの見張りは診断スイッチのときだけ）。
- 段 6：`CommitDragMove` 経由の `ExecuteFromCommand` が DragBegin・DragEnd を出し直すのをやめる。

## 残件（2026-11-01、段 14 のあと）

計測の条件は「経過」の段 7・段 13 と同じ（Play 後の 1 回目を捨て、2 回目以降の値）。
段 14 のあとのコマンド全体：髪 899.1 ms、roboF 298.9 ms。区間の和でほぼ説明がつく（髪の残り 約 1 ms）。

### A. 時間の残り（髪の大きい順。[髪 / roboF] ms）

| # | 区間 | 髪 | roboF | 場所 | 分かっていること・未確認 |
|---|---|---|---|---|---|
| A1 | PresentAll の選択・可視フラグ | 198.5 | 34.2 | `MeshSceneRenderer.SyncSelectionFlags` → `UnifiedBufferManager.UpdateAllSelectionFlags` | 内訳：可視の線分 99.5・選択の線分 57.8・可視の面 18.2・選択の面 17.7・可視の頂点 4.7。線分の 2 本の走査が 157 ms。線分ごとに `ResolveMeshContext` で描画オブジェクトを引き直し、`_lines` の構造体を書き戻して全体を `SetData` している（`UnifiedBufferManager_Visibility.cs`・`_Selection.cs`）。どれが重いかは未計測 |
| A2 | PresentAll の PrepareDrawing（slot0） | 129.5 | 21.2 | `UnifiedSystemAdapter.PrepareDrawing` | 中身は未計測。slot1〜3 は 0 |
| A3 | HairRe の図形の生成 | 108.5 | （SkirtFrill 21.7） | `PipeMeshGenerator` ほか | PipeMeshGenerator 52.6・梯子の前処理 19.5・連結 19.1。コードは未確認 |
| A4 | 照合（メッシュごとの面の構成の署名） | 62.8 | 11.2 | `UnifiedBufferManager.MatchesModel` → `MeshSignature.Topology` | 載っている全メッシュの全面・UV を毎回なめる。集合の判定は 0.2。段 7 では roboF の 11 ms を見て手を入れないと決めたが、髪では 60 ms ある |
| A5 | HairRe の UnityMesh | 65.2 | 5.3 | `MeshBridgeDefault.ToUnityMesh` | 三角形分割 47.8（`face.Triangulate()` を面ごとに呼ぶ）・頂点の展開 15.9。Mesh は面の描画に使うので作ること自体は省けない |
| A6 | HairRe の組み直し | 57.4 | （SkirtFrill 21.4） | `RunModifierStackStep` の「組み直し」区間 | BuildCommand の中は 約 31（点列を文字列にする 13.6・文字列からコマンドを作る 11.2・梯子の取り直し 5.2）。**残りの 約 26（roboF 約 10）は BuildCommand の外で未計測** |
| A7 | HairRe の入れ替え | 40.5 | 4.3 | `PrimitiveMeshReplaceExisting` | 生成物を 2 回複製している（空間の変換用の `Clone` 21.4 と、頂点・面の写し 18.1） |
| A8 | HairBelt の見張り | 24.7 | 0.7 | 宣言漏れの見張り | 段 5・段 7 で切り替えを見送った（使える診断スイッチが無い）。髪では 25 ms ある。段 15 で診断スイッチの仕組み（`PLDiag` の項目＋ログパネルのトグル＋MCP のコマンド）ができたので、同じ形で切り替えを足せる |
| A9 | HairRe の実行の残り | 25.6 | （6.9） | `RunModifierStackStep` | 中身は未計測 |
| A10 | Settle の変換（UpdateTransform） | 12.1 | 8.2 | `UnifiedSystemAdapter.UpdateTransform` | GPU 変換と読み戻し |
| A11 | PresentAll のボーン | 4.1 | 34.2 | `MeshSceneRenderer.PrepareBones` | roboF はボーンが多い。全ボーンの線メッシュを毎回書き直す |
| A12 | SpringLadder の鎖の配置・評価空間 | — | 15.6 / 7.0 | `SpringBoneLadderPlacer.Place` | 中身は未計測 |
| A13 | パスの前処理（行列・署名） | 2.1 | 11.5 | 評価器 | 段 7 でグラフの受け渡しを見送った |

### B. 確かめていないこと

- B1：作り直しをしない属性の変更（ロックなど）で可視フラグを後へ回す道（段 14 の `_visibilityPending`）は、ロックの見た目が変わらないのでキャプチャで確かめていない。ロックしたオブジェクトの頂点がクリック・矩形で選べないことで確かめられる。
- B2：Play 後の最初のドラッグだけ HairRe が 2 倍以上遅い（段 13）。原因は未確認。
- B3：2 回目以降でも、HairRe の生成・入れ替え・UnityMesh だけが 2〜4 倍になる回がまれにある（段 13・段 14 で各 1 回）。原因は未確認。

### C. 計測コードの扱い

- C1（段 15 で済み）：段 10〜13 の計測は診断スイッチ `PLDiag.Timing`（既定 切・起動をまたいで覚えない）で動く。
  入れ方は MCP の `setDiagnosticTiming enabled=true`、またはログパネル「診断ログ」の「時間の計測 (Timing)」。
  切のときは lastPass に「区間:」「内訳（合計）」「区間の累計」「描画層の累計」「〔集合の判定…〕」が出ない。
  段 0〜9 の行（作り直しの数・Settle・通知・コマンド全体・PresentAll の回数）は常に出る。

### D. 監査で 0 でない既存の項目（段 15 の確認で見えたもの。段 15 の変更によるものではない）

- D1：`queryCommandAudit` の「Category が正典に無い 10」（scenario.log 4 本・external.comfyui 4 本・geometry.create.profile 2 本）と
  「スキーマに出せない 3」（AddGeneratedMesh・AddModelBundle・ReplaceModelBundle。Ignore 指定で既定値が無い）。
  `setDiagnosticTiming` はどちらにも入っていない。


2026-10-31 段 0〜6 を実施。

- 計測（stage8_パイプの梯子化、入力 SrcPipe の全頂点を移動して 2 項目を作り直し）：
  変更前 バッファ作り直し 8 回 / 216 ms → 変更後 0 回 / 30〜45 ms。
- 画面の確認：変更後の表示と、表示の切り替えで全バッファを作り直した表示が一致（キャプチャのバイト数まで同じ）。
  対象：stage8（位置だけ）、引数の書き換えで面の構成が変わる場合（ふたを外す）、藤壺（barnacle_place）、
  スキンド化後の書き戻し（ウェイトの部分転送）、Undo、サブディビジョンの子の追随。
- 段 1 の変更点：法線の部分転送は `EnterVertexAttributesChanged` に足さず、`SettleWorldPositions` の予約
  （`QueueVertexAttributesUpload`）にした。出力ごとに `EnterVertexAttributesChanged` を呼ぶと、出力の数だけ
  全ビューの描き直しが走るため。
- 段 4：子を書いたときの親の位置の署名（`Cache.PositionSig`）で判定する。評価器の inputsChanged は
  下流の印立て・スキンド化の後の取り直しでも更新されるので使えない。
- 段 5：署名の 1 回取り（`RetakeInputs`）、未確定の判定を要るときだけ、ダイジェストはスタックごとに 1 回。
  見送り：グラフの受け渡し（同じ構造キーなら同じグラフが返るので、省けるのは構造キーの計算だけ。
  パス中に構造が変わる項目があると古い項目を渡すことになる）、宣言漏れの見張りの切り替え
  （切り替えに使える診断スイッチが無い）。
- 段 6：`ExecuteFromCommand` の余分な DragEnd を外し、`ApplyNumericMove` が出入りを try/finally で対にした。
  ドラッグを離したときの評価の走査（中身が無ければ軽い）はそのまま。印が無ければ省く案は、
  頂点以外のドラッグ（姿勢）で行列の変化を取りこぼすので入れなかった。

2026-10-31 段 7（大きいモデルでの実測と手直し）。

- 計測の条件：シナリオ roboF_T5_SK__フリル を 89 項目（c7 の CSV 保存とプレファブ書き出しの手前）まで流し、
  円筒24C_skinned を表示して頂点 300〜302 を Perspective のギズモ Y 軸でマウスドラッグして離す
  （moveSelectedVertices のコマンドの終わりで SpringLadder_00_top#0 と SkirtFrill#0 の 2 項目が流れる）。
- 変更前：バッファ作り直し 2 回 / 1995 ms。原因は 2 つ。
  - `SpringBoneLadderPlacer.TraceChain` が、エプロンの縁の鎖（根元をスカートの関節へ付け替えたもの）の根元を
    分岐と数え、流し込みに落ちていた。作り直しのたびにボーン 624 本の鎖を新しく作り、古い鎖が残っていた。
    `SpringBoneOps`・`Vrm10SceneAssembler` と同じ「別の鎖の根元（SpringBoneChainRoot 付き）から先へ下りない」規則を入れた。
  - PlaceSpringBoneLadderChains が流し込み（Updated）でも `EnterTopologyChanged` と `ListStructure` を無条件に呼んでいた
    （作り直し 2 回）。Updated では呼ばず、行列を直してウェイトの部分転送を予約し（`QueueVertexAttributesUpload`）、
    表示は Settle に任せる。パネルへの通知は書き戻しの外のときだけ。
  - Settle は位置に依る重ね描き（選択面・ギズモ・ボーン）も描き直すようにした（作り直しの分岐は従来どおり EnterTopologyChanged が行う）。
- 変更後：バッファ作り直し 0 回 / 984 ms。内訳（lastPass の「内訳」）：前処理 11.4 / SpringLadder の作り直し 663.0 /
  SkirtFrill の作り直し 70.5 / 見張り 0.7 / ダイジェスト 0.2 / パスの終わりの Settle 108.5（照合 11.5）/
  パネルへの通知 119.2 / コマンドの終わりの Settle 109.1（照合 11.8） [ms]。
- 計測を足した：パスの記録に内訳（項目ごとの作り直し・見張り・バッファ作り直しの回数、Settle と照合、パネルへの通知、
  コマンドの終わりの Settle）。`PassInfo.Timing`、`PlayerViewportManager.LastMatchMs`。
- 画面の確認：ドラッグ後と、円筒24C の表示を切り替えて 2 回作り直したあとのキャプチャが一致（PolyLingCapture_0014 / 0015、157038 バイト）。
- 見送りの判断：宣言漏れの見張りの切り替えはやらない（計 0.7 ms）。グラフの受け渡しもやらない（省けるのは前処理 11.4 ms の一部）。
  面構成の署名（照合）は 1 回 11〜12 ms で、これも手を入れない。
- ゼロちゃんの髪（シナリオ 検証_髪パイプ往復 の 5 項目まで。髪前の頂点 300〜302 をギズモ Y 軸でドラッグ、
  extractPipeBelt → createPipe の 2 項目）：バッファ作り直し 0 回 / 1541 ms。内訳：前処理 2.2 /
  HairBelt の作り直し 159.8（見張り 26.1）/ HairRe の作り直し 425.2（見張り 3.2）/ ダイジェスト 3.4 /
  パスの終わりの Settle 416.2（照合 61.6）/ パネルへの通知 458.8 / コマンドの終わりの Settle 419.8（照合 61.8） [ms]。
  表示の切り替え 2 回の前後でキャプチャが一致（PolyLingCapture_0016 / 0017、113737 バイト）。
- 残り：SpringLadder の作り直し 663 ms の中身、パスの終わりの Settle とコマンドの終わりの Settle の重複（約 109 ms）、
  パネルへの通知（約 119 ms）。どれも未着手。

2026-10-31 段 8（Settle の重複をなくす・PresentAll とパネル側の計測）。

- コマンドの終わり（DispatchAuthorized）から流したパスは、パスの終わりの Settle とパネルへの通知をしない
  （`RunModifierEvaluation(displayAtCommandEnd: true)`）。直後のコマンドの終わりの Settle の後で、通知を 1 回だけ出す
  （`FinishCommandSettle`）。ドラッグを離したとき（コマンドの外）とシナリオの項目のあと・スキンド化の直後は従来どおり。
- 計測を足した：`PlayerViewportManager.PresentCount / PresentMs / PanelNotifyMs`（PresentAll の回数と時間、
  パネル側＝`_panelContext.Notify` と区画の更新の時間）。パスの内訳と「コマンド全体」の行に出す。
- 結果（同じドラッグ）：
  - ゼロちゃんの髪：パス 660 ms（作り直し HairBelt 257.9 / HairRe 322.6、見張り 30.0）、コマンドの終わりの Settle 423.6
    （照合 63.1）、通知 465.7、コマンド全体 2413.5 ms。そのうち PresentAll 4 回 1361.1 ms、パネル側 43.0 ms。
  - roboF のフリル：パス 692 ms（SpringLadder 489.0 / SkirtFrill 178.6）、Settle 107.2（照合 11.5）、通知 121.9、
    コマンド全体 1171.6 ms。そのうち PresentAll 4 回 362.5 ms、パネル側 33.9 ms。
  - どちらもバッファ作り直し 0 回。表示の切り替え 2 回の前後でキャプチャが一致
    （髪 PolyLingCapture_0018 / 0019、113738 バイト。roboF 0020 / 0021、156202 バイト）。
- 分かったこと：パネルへの通知の時間はほとんどが PresentAll（パネル側は数十 ms）。コマンド 1 回で PresentAll が 4 回走る。
  次は 4 回の呼び元を特定して減らす。

2026-10-31 段 9（PresentAll をコマンドの終わりの 1 回にまとめる）。

- 呼び元（頂点ドラッグを離したとき）：コマンドの中で 4 回 ― ApplyNumericMove の DragEnd、moveSelectedVertices 自身の
  通知（Attributes → EnterMeshAttributesChanged）、コマンドの終わりの Settle、その後の通知。コマンドの外で 1 回 ―
  `MoveToolHandler.Press` がコマンドを送った後にもう一度 DragEnd していた。
- 変更：
  - `CommitDragMove` がコマンドを送ったかを返す。送ったときは Press で DragEnd しない
    （送らなかったとき・コマンドが拒否されてドラッグ状態が残ったとき（`IsTransformDragging`）は従来どおり抜ける）。
  - `PlayerViewportManager.BeginDeferPresent / EndDeferPresent`：一番外側の書き込みのあるコマンドの間（シナリオを流す
    コマンドは除く）、`EnterVerticesMoved(DragEnd)` と作り直しを伴わない `EnterMeshAttributesChanged` の PresentAll を
    印だけにし、コマンドの終わりの `SettleWorldPositions(present: false)` も描画準備をしない。区間を閉じるときに
    PresentAll と重ね描きの更新を 1 回行う。ほかの入口とコマンドの途中の Settle は従来どおり。
- 結果（同じドラッグ）：
  - ゼロちゃんの髪：コマンド全体 2413.5 → 1392.3 ms。PresentAll 4 回 1361.1 ms → 1 回 340.3 ms。
    Settle 88.3（照合 66.9）、通知 128.9、パス 656.5 ms。
  - roboF のフリル：コマンド全体 1171.6 → 854.8 ms。PresentAll 4 回 362.5 ms → 1 回 90.1 ms。
    Settle 18.4（照合 11.4）、通知 30.9、パス 648.7 ms。
  - 表示の切り替え 2 回の前後でキャプチャが一致（髪 PolyLingCapture_0022 / 0023、113738 バイト。
    roboF 0025 / 0026、156202 バイト。どちらも段 8 のキャプチャと同じバイト数）。
  - 矩形選択：ドラッグの直後（後へ回した描画準備のあと）と、表示の切り替えで描き直したあとに、同じ矩形で選ぶと
    どちらも 髪前 927 頂点・1671 面、ギズモの原点も同じ（画面座標の読み戻しは古くなっていない）。
- 残り：パスの中の作り直し（髪 HairRe 約 400 ms・HairBelt 約 170 ms、roboF SpringLadder 約 450 ms・SkirtFrill 約 170 ms）。
  コマンド全体からパス・Settle・通知・PresentAll を引いた残り（髪 約 180 ms、roboF 約 70 ms。内訳は未計測）。

2026-10-31 段 10（作り直しの中の区間の計測・揺れ鎖の一覧の複製をやめる）。

- placeSpringBoneLadderChains は、記録を止めているときも始めと終わりにモデル全体の一覧を深く複製していた
  （`MeshFilterToSkinnedRecord.CaptureList` と `RecordMeshListSnapshot`。記録は捨てられる）。記録しているときだけ取る。
- 計測を足した：`ModifierStepProfiler`（項目ごとに Begin・End、途中は Mark）。印の位置は `RunModifierStackStep`
  （組み直し・評価空間・実行の残り）、`PlaceGeneratedMesh`（生成）、`PrimitiveMeshReplaceExisting`（入れ替え・UnityMesh・
  ミラー側・行列）、placeSpringBoneLadderChains（一覧の控え・鎖の配置・ミラーへ写す・一覧の通知と記録）。
  パスの内訳の項目の下に「区間:」として出す。
- 結果（同じドラッグ）：
  - roboF のフリル：パス 648.7 → 150.3 ms、コマンド全体 854.8 → 358.4 ms。SpringLadder 452.0 → 57.0 ms
    （区間：組み直し 2.0 / 評価空間 6.4 / 一覧の控え 1.7 / 鎖の配置 15.2 / 一覧の通知と記録 23.7 / 実行の残り 7.8）。
    SkirtFrill 70.3 ms（組み直し 20.8 / 評価空間 7.0 / 生成 20.8 / 入れ替え 4.6 / UnityMesh 5.1 / 行列 5.4 / 実行の残り 6.7）。
  - ゼロちゃんの髪（揺れ鎖なし。変更の対象外で、計測だけ）：パス 662.2 ms、コマンド全体 1404.6 ms。
    HairBelt 157.3 ms（生成 120.5 / 入れ替え 10.5 / UnityMesh 8.9 / 実行の残り 9.3）、
    HairRe 421.4 ms（組み直し 63.4 / 生成 213.2 / 入れ替え 45.4 / UnityMesh 70.0 / 実行の残り 25.7）。
  - 表示の切り替え 2 回の前後でキャプチャが一致（髪 PolyLingCapture_0027 / 0028、113738 バイト。
    roboF 0029 / 0030、156202 バイト。どちらも段 8・段 9 と同じバイト数）。
- 次の候補（数字の大きい順）：髪 HairRe の生成 213 ms・HairBelt の生成 120 ms、HairRe の UnityMesh 70 ms・組み直し 63 ms・
  入れ替え 45 ms、roboF の一覧の通知（`model.OnListChanged`）23.7 ms・SkirtFrill の組み直し 20.8 ms。

2026-11-01 段 11（図形の生成の中の計測）。

- `PrimitiveMeshFactory.Build` に残っていた一時計測（`TEMP-TIMING` の `Debug.Log`。毎回ログを出していた）を外し、
  区間の計測（`ModifierStepProfiler.Add`。繰り返しの中の区間を合計して「内訳（合計）」に出す）に置き換えた。
  印：図形の生成・重複頂点の結合・部品番号、パイプ（取り込み元の解決・梯子の前処理・PipeMeshGenerator・厚み付け・連結）、
  梯子の取り出し（成分への分割・端の確かめ・梯子の組み立て）。
- 結果（ゼロちゃんの髪、同じドラッグ）：
  - HairBelt（extractPipeBelt）158.5 ms：図形の生成 120.3 のうち **端の確かめ 109.6**（`IsFanApex`）、成分への分割 5.0、
    梯子の組み立て 4.7。
  - HairRe（createPipe）425.4 ms：図形の生成 199.6 のうち PipeMeshGenerator 128.0、連結 39.4、梯子の前処理 31.9、
    厚み付け 0.0。ほかに組み直し 63.3・入れ替え 46.5・UnityMesh 69.9・実行の残り 24.8。
  - 表示の切り替え 2 回の前後でキャプチャが一致（PolyLingCapture_0031 / 0032、113738 バイト）。
- 端の確かめ：`IsFanApex` は梯子の両端ごとに取り込み元の全部の面を走査する（梯子の本数 × 面の数 × 2）。

2026-11-01 段 12（梯子の取り出しの端の確かめを表引きにする）。

- `PipeBeltExtractor`：取り込み元 1 つにつき面を 1 回だけ走査して、頂点ごとに「含む面の数」と「三角形でない面を含むか」の
  表（`FanTable`）を作り、扇の要の判定は表を引くだけにした。規則は以前の `IsFanApex` と同じ（含む面がすべて三角形で
  ちょうど k 枚。1 つの面に同じ頂点が 2 回あっても 1 枚）。
- 結果（ゼロちゃんの髪、同じドラッグ）：端の確かめ 109.6 → 3.8 ms（表を作る時間を含む）。HairBelt 158.5 → 50.4 ms。
  パス 664.3 → 445.2 ms、コマンド全体 1415.7 → 1180.1 ms。
  - この回は HairRe の図形の生成も 199.6 → 95.8 ms（PipeMeshGenerator 128.0 → 55.4、連結 39.4 → 20.4、
    梯子の前処理 31.9 → 19.8）だった。パイプ生成のコードは変えていないので、回ごとの振れの幅は確かめていない。
- 取り出した梯子：HairBelt の頂点 4233・面 2115、梯子 47 本で、変更前と同じ。
- 表示：表示の切り替え 2 回の前後でキャプチャが一致（PolyLingCapture_0036 / 0037、113737 バイト。マウスをギズモから
  外した状態）。ギズモにマウスが乗った状態では 0033 / 0034 が一致（113868 バイト）。変更前の回（0031）とはマウスの
  状態が違うのでバイト数では比べていない。

2026-11-01 段 13（振れの幅・描画準備とコマンドの外枠の計測・揺れ鎖の一覧の通知）。

- 振れの幅（ゼロちゃんの髪、Play を入れ直してから同じドラッグを 3 回）：1 回目だけ遅い（HairRe 713.7 ms、生成 228.4、
  入れ替え 207.4、UnityMesh 178.9、コマンド全体 1569.4）。2・3 回目はほぼ同じ（HairRe 301.9 / 302.4、生成 107.8 / 108.1、
  コマンド全体 1115.2 / 1129.4）。段 11 の生成 199.6 は 1 回目、段 12 の 95.8 は 2 回目以降の値に当たる。
  段 12 の生成の減りはコードの変更によるものではない。以後は Play 後の 1 回目を捨て、2 回目以降で比べる。
  2 回目以降でも、まれに入れ替え・UnityMesh が数倍になる回がある（頂点・面の写し 116.3、UnityMesh 219.7 の回があった）。
- 計測を足した（動作は変えない）：
  - `ModifierStackOps.BuildCommand`：組み直し:複製・参照 / 梯子の取り直し / 点列の書き込み / プロファイル / コマンド生成。
  - `PrimitiveMeshReplaceExisting`：入れ替えを 複製・空間の変換・材質 / 頂点・面の写し / 線分・ID に分けた。
  - `MeshBridgeDefault.ToUnityMesh(MeshObject,int)`：頂点の展開 / 三角形分割 / Mesh への設定。
  - `UnifiedBufferManager.MatchesModel`：集合の判定とメッシュごとの照合（`LastMatchMembershipMs / LastMatchTopologyMs`）。
  - 区間の累計（`PlayerViewportManager.AddViewTiming`、描画層は `Poly_Ling.Core.RenderTiming`）。一番外側のコマンドの始めに空にし、
    パスの内訳の最後に「区間の累計」「描画層の累計」として出す。P: は PresentAll の中（下絵・ホバー / ウェイトの色 / カリング /
    法線 / ワイヤと頂点 / ボーン / コライダ / 読み戻し）、属性: は `EnterMeshAttributesChanged`、Settle: は Settle の中、
    C: はコマンドの外枠（実行前の控え / コマンド本体 / ObjectId の点検 / パーツ辞書の引き直し / 構造通知の確かめ）、
    描画層は可視フラグ（頂点・線分・面）、選択フラグ（線分・面）、`PrepareWireframeAndVertices` の選択フラグと PrepareDrawing（slot ごと）。
- 直した：`placeSpringBoneLadderChains` は鎖へ位置を流し込むだけ（Updated）のときも `model.OnListChanged` を呼んでいた。
  受け手は `RemoteServerCore.OnModelListChanged` だけで、通知のたびにモデル全体の一覧の JSON（`QueryMeshList`）を作る。
  鎖を作ったとき（一覧が変わったとき）だけ呼ぶようにした。
- 結果（2 回目以降のドラッグ）：
  - ゼロちゃんの髪：コマンド全体 1133.8 ms。PresentAll 1 回 340.1（うち ワイヤと頂点 324.1 ＝ 選択フラグ 196.5 ＋ PrepareDrawing 127.5。
    PrepareDrawing と選択フラグは slot0 だけで走る。slot1〜3 は `ConsumeNormalMode` のあと Idle なので 0）。
    **可視フラグの更新（`UpdateAllVisibilityFlags`）がコマンド 1 回で 3 回走り、計 351.1 ms**（線分 282.0・面 55.2・頂点 13.9。1 回 約 117 ms）。
    3 回の内訳：`moveSelectedVertices` 本体の属性の通知、コマンドの終わりのパネルへの通知（どちらも `EnterMeshAttributesChanged`）、
    PresentAll の中の `UpdateAllSelectionFlags`。照合 57.6（すべてメッシュごとの照合。集合の判定 0.2）。
    HairRe：組み直し 60.1（BuildCommand の中は 点列の書き込み 14.5・コマンド生成 11.3・梯子の取り直し 5.6 で計 約 33）、
    生成 107.9、入れ替え 40.7（複製・空間の変換・材質 21.5 / 頂点・面の写し 18.3）、UnityMesh 67.0（三角形分割 49.3 / 頂点の展開 16.1）。
  - roboF のフリル：SpringLadder 57.0 → 34.8 ms（一覧の通知と記録 23.7 → 0.0）、コマンド全体 358.4 → 341.3 ms。
    SkirtFrill 72.9（組み直し 21.6。BuildCommand の中は 梯子 7.1・コマンド生成 4.1）。PresentAll 97.4（ワイヤと頂点 54.9・ボーン 34.9）、
    可視フラグ 3 回 計 60.5 ms。
- 表示：roboF で表示の切り替えの前後のキャプチャが一致（speed13/PolyLingCapture_0004 / 0005、156808 バイト）。
  髪は動作を変えていない（計測だけ）。

2026-11-01 段 14（選択・可視フラグの更新を描画準備 1 回につき 1 回、カリングの前にする）。

- `MeshSceneRenderer.SyncSelectionFlags(project, visibilityPending)` を足し、`PrepareWireframeAndVertices` の中にあった
  選択の同期と `UpdateAllSelectionFlags`（可視を含む）を移した。走る条件は従来どおり `profile.AllowSelectedDrawableMeshSync`。
  走らないモードでも `visibilityPending` なら `UpdateAllVisibilityFlags` だけ行う。
- `PresentAllCore` はホバーの判定の直後、面ごとのカリング（`PrepareViewport`）より前に 1 回呼ぶ。4 面の準備の間は
  `BeginPresent / EndPresent` で `PrepareWireframeAndVertices` に同期させない（単独で呼ばれたときは従来どおり同期する）。
  以前は最初の面（slot0）のカリングの後で同期していた。
- `EnterMeshAttributesChanged` の作り直しをしない枝は `UpdateAllVisibilityFlags` を呼ばず、`_visibilityPending` を立てるだけにした。
  続く PresentAll（その場か、コマンドの中ならコマンドの終わり）がカリングの前に写す。
- 結果（2 回目以降のドラッグ）：
  - ゼロちゃんの髪：コマンド全体 1133.8 → 899.1 / 919.3 ms。可視フラグの更新 3 回 351.1 ms → 1 回 約 110 ms。
    コマンドの終わりのパネルへの通知 126.6 → 11.6 / 12.8 ms、コマンド本体 168.1 → 50.4 / 48.8 ms。
    PresentAll 340.1 → 343.4 / 348.8 ms（中身は 選択・可視フラグ 約 190 ＋ ワイヤと頂点 約 130 で、合計は変わらない）。
    別の回に HairRe の生成・入れ替え・UnityMesh だけが 2〜4 倍になった回があった（コマンド全体 1351.1。段 13 と同じ振れ）。
  - roboF のフリル：コマンド全体 341.3 → 298.9 ms。パネルへの通知 32.6 → 12.9、コマンド本体 57.8 → 37.0。
- 表示：
  - 髪：表示の切り替えの前後のキャプチャが一致（speed14/PolyLingCapture_0002 / 0004、113771 バイト）。非表示にした回（0003）は
    髪前の頂点・辺が消えた。面の非表示（setFaceHidden HideSelected → ShowAll）の後と、続けて表示の切り替えで作り直した後も一致
    （0009 / 0010、113629 バイト）。
  - roboF：表示の切り替えの前後のキャプチャが一致（speed14/PolyLingCapture_0011 / 0012、156808 バイト。段 13 と同じバイト数）。
- 次の候補（髪、数字の大きい順）：選択・可視フラグ 約 190（うち可視の線分 約 90・選択の線分 約 60）、PrepareDrawing 約 130、
  HairRe の生成 約 110（PipeMeshGenerator 約 53）、照合 約 60、UnityMesh の三角形分割 約 50、組み直しの点列の文字列往復 約 25。

2026-11-01 段 15（計測を診断スイッチで切れるようにする。エンドユーザーの評価に出す版）。

- `PLDiag.Timing`（既定 false。`SetAll` にも入れた）。起動をまたいで覚えない。
- 切のとき止めるもの：`ModifierStepProfiler.Begin` が計測を始めない（Mark・Add・End は何もしない）。
  `PlayerViewportManager.AddViewTiming` と `RenderTiming.Add`（`RenderTiming.Enabled`）は時刻を返すだけ。
  `PrepareWireframeAndVertices` の「W:PrepareDrawing slot」＋番号の文字列は入のときだけ作る。
  `MatchesModel` の時間の取り分けは入のときだけ（本体は `MatchesModelPerMesh` に分けた）。
  lastPass の「〔集合の判定…〕」「区間の累計」「描画層の累計」は入のときだけ出す（「区間:」は End が空を返すので出ない）。
- 切り替える口：ログパネル「診断ログ」の「時間の計測 (Timing)」（`PlayerLogSubPanel`。Refresh で値を写し直す）と、
  コマンド `setDiagnosticTiming`（`SetDiagnosticTimingCommand`、分類 verify、Writes None、NoUi。戻り値 enabled。
  `PlayerCommandDispatcher.Scenario.cs` で受け、`RemoteOwnership.IsScenario` に足した＝リモート接続からは実行できない）。
- 確認：
  - dotnet build（PolyLing.Runtime / PolyLing.Editor / Assembly-CSharp）と unity_validate_changes でエラー 0。
  - `queryCommandAudit`：`setDiagnosticTiming` に当たる項目は無い（0 でない項目は既存のものだけ。残件 D1）。
  - 髪の同じドラッグ：切のまま（既定）は lastPass に「区間:」「区間の累計」「描画層の累計」「〔集合の判定…〕」が出ない
    （コマンド全体 966.1 ms）。`setDiagnosticTiming enabled=true` の後は出る（コマンド全体 1119.6 ms。この回は HairBelt の
    実行の残りが 48.2 ms で、段 13・14 と同じ振れ）。
  - 切に戻して表示の切り替えの前後でキャプチャが一致（speed15/PolyLingCapture_0002 / 0003、238250 バイト）。
    この回から Unity の窓の大きさが変わり、MainView は 616×625（以前は 276×625）。

## 次の会話へのプロンプト

```
PolyLing モディファイア高速化の続き。正本は Packages/com.haglib.polyling/計画と規約/PolyLing_モディファイア高速化計画.md
（段 0〜15 は 2026-10-31〜11-01 に実施済み。先に「残件」の節と「経過」の段 13〜15 を読むこと）。

計測の手順（前回と同じ条件で測る）：
- 計測の前に setDiagnosticTiming enabled=true を入れる（既定は切。起動をまたいで覚えない）。
- ゼロちゃんの髪：Play を入れ直し、シナリオ「検証_髪パイプ往復」を maxSteps 5（CSV 保存の手前）まで流す。
  selectDrawablesByName 髪前 → selectElements で髪前（masterIndex 235）の頂点 300〜302 → fitCameraToSelection（views=All）。
  Perspective のギズモ Y 軸をマウスでドラッグして離す。
- roboF のフリル：シナリオ「roboF_T5_SK__フリル」を maxSteps 89（c7 の CSV 保存の手前）まで流す（60 秒を超えるので
  unity_state が返るまで待つ）。setBatchVisibility で円筒24C_skinned（509）を表示し、selectElements の masterIndices に
  392,394,509,569 を渡して他の選択を消し、頂点 300〜302 で同じドラッグをする。
- Play 後の 1 回目のドラッグは遅いので捨て、2 回目以降で比べる。ドラッグ後の 1 回目のマウス操作は焦点が移るだけで
  効かないことがあるので、querySelectionState の modelRevision が増えたかで確かめる。効かないまま次のドラッグをすると
  空いた所をクリックして選択が消える。
- ギズモの位置は querySelectionState の origin（MainView のキャプチャ座標）で読み、unity_capture target=MainView で
  確かめてから画面座標へ直す。Unity の窓の位置と大きさは変わることがある（段 15 で MainView が 616×625 になった）。
  計測の値を前の段と比べるときは、窓の大きさが同じかも確かめる。
- queryModifierGraph の lastPass の「内訳」を読む。項目ごとの区間、「内訳（合計）」、コマンド全体の PresentAll、
  「区間の累計」（P: PresentAll の中 / C: コマンドの外枠 / 属性: / Settle:）、「描画層の累計」が出る。
- 表示の確認は表示の切り替え（setBatchVisibility を false→true）の前後でキャプチャを撮り、一致を見る。
  撮る前にマウスをビューの空いた所へ動かす。

今回やること：
1. 残件 A1（PresentAll の選択・可視フラグ、髪 約 190 ms。線分の 2 本の走査が 約 157 ms）と
   A2（PrepareDrawing、髪 約 130 ms）の中身を、コードと計測で特定する。
2. 推測で決めず、計測とコードの確認で決める。変更する場合は、変更計画を箇条書きで出して承認を得てから行う。
   変更後は 2 つのモデルで測り直し、キャプチャの一致を確かめ、計画書の「経過」に段 16 以降として記録し、残件の節を更新する。
```
