# PolyLing 姿勢まわり ― 残件と再開メモ

最終更新: 2026-09-29（済んだ記録を削り、残件だけにした。済んだ事実のうち今後も要るものは
`PolyLing_姿勢の規約.md` へ移した）。

## まず読むもの

`Packages/com.haglib.polyling/PolyLing_姿勢の規約.md`（11 章）。
設計の理由・用語・式・割り切り・実装の所在・確認済みと未確認の区別が全部そこにある。
このファイルは残件と手順の癖だけを書く。

要点だけ再掲すると、

- 出発点は「姿勢の定義が曖昧で、暗黙にバインドポーズと仮定していた／グローバルで
  制御していた」こと。バインド階層が保存されておらず、要る側が毎回撮り直すか
  組み直していた（規約 5 章）。
- 割り切り：厳密な逆スキニング（重み付き合成後の行列の逆を 1 点ずつ）はやらない。
  現在ポーズ表示中の移動は不自然でも許容する。**バインドポーズ表示中の変換だけは
  厳密に合わせる**（規約 9.1 / 10.2）。

## 画面へ配る規則（2026-09-15 に洗い出し）

モデルの階層行列や構造を変えたコマンドは、末尾でビューポートへ配ること。
どれを呼べばよいかは次で決まる。

| 呼ぶもの | GPU の変換行列を押し込むか | 使う場面 |
|---|---|---|
| `EnterTopologyChanged` / `EnterProjectChanged` | **押し込む**（`RebuildAdapter` → `MeshSceneRenderer.cs:446-448` で `ShowBindPose` 再配布・`UpdateTransform`・`Writeback`） | リスト構造が変わる（生成・破棄・ミラーの付け外し・焼き込み） |
| `EnterVerticesMoved` / `PresentAll` のみ | **押し込まない** | 姿勢だけ変えたときは `UpdateTransform()` を別に呼ぶ（規約 10.5） |
| `_notifyPanels` のみ | 届かない | パネル表示の更新だけ |

`ComputeWorldMatrices()` の呼び出し 38 か所（Poly_Ling_Player 配下）を上の基準で当たった結果、
取りこぼしは `setMirrorEnabled` の 1 件だけで、2026-09-15 に `EnterTopologyChanged` を足して直した。
次は安全と確認済み。

- `ObjectOrigin.cs` の 4 コマンド（:163 / :487 / :540 / :746）
- `BoneMorphUv.cs:735`（スキンごと確定 → :763）
- `SpringBone.cs:291`（ボーンの親を変える → :304）
- `TPoseMergeMorph.cs:45, 57`（この姿勢で確定 → :82）

`PolyLingPlayerViewerCore.*`（生成メッシュ・ブリッジ・読み込み）側は 2026-09-30 に点検した（残件 A-4）。
21 か所のうち、生成・ブリッジ・オブジェクト配列・作業軸・MQO 追加読込・ロードは
`PrimitiveMeshFinalize` / 直接の `EnterSceneReset`（→ `RebuildAdapter`）で押し込む。
移動・ピボット（`VertexInteraction.cs:203 / :256`）は `UpdateTransform` を明示している。
取りこぼしは次の 2 件で、どちらも実機（ギズモだけ動きメッシュが残る）で確かめてから直した。

- `Lifecycle.cs` の Undo MeshList 分岐。再構築・PivotMove・選択のどれにも当たらない記録
  （`MultiBonePoseChangeRecord` / `MultiBoneTransformChangeRecord` など）が
  `ComputeWorldMatrices` だけで終わっていた。`EnterVerticesMoved` ＋ `UpdateTransform` を足した。
- `PlayerCommandDispatcher.BoneMorphUv.cs` の `setBoneTransformValue`。
  `UpdateTransform` が A（スキン固定）と C（ポーズ層）だけだった。常に呼ぶようにした。

`setBoneTransformValue` は単独では Undo を記録しない（begin / end で挟む設計）。説明文に書き足した。

## `GetVertexWorldPosition` の二重配線は意図的（2026-09-15 に整理）

GPU のワールド座標の読み口は 2 系統ある。**どちらも残すこと。**

**中央配線**（`PlayerViewportManager.WireGpuWorldReaders`）
`GetCurrentToolContext` / `GetToolContextForCamera` の両方から呼ばれ、
`Project` / `GetVertexWorldPosition` / `GetMeshWorldPositions` を埋める。
新しいツールは何もしなくてもここで口が埋まる。

**個別配線**（各ツールハンドラの `ctx.GetVertexWorldPosition = GetVertexWorldPosition;`）
`AddFaceToolHandler.cs:520`、`EdgeBevelToolHandler.cs:169, 195, 228`、
`EdgeExtrudeToolHandler.cs:169, 195, 228`、`FaceExtrudeToolHandler.cs:166, 192, 227` の 10 か所。

一度は「中央配線と同じ値だから重複、消してよい」と判断したが**誤り**。
各ハンドラは

```
var ctx = GetToolContext?.Invoke() ?? new ToolContext();
```

の形で、`GetToolContext` が null のときや、カメラが取れず
`GetCurrentToolContext` が null を返したときに `new ToolContext()` へ落ちる。
その場合は中央配線を通らない。個別配線はそこを埋める**保険**として働いている。
ハンドラ側の `GetVertexWorldPosition` は `_viewportManager.TryGetVertexWorld` を
直接呼ぶので、`ToolContext` の生成失敗とは無関係に動く。

消すなら先に `?? new ToolContext()` の側を潰すこと。順序を逆にすると、
カメラが取れない状況で GPU 値が読めなくなる。

`PointDefinedToolHandler.cs:628` は別物。中央配線が `ActiveMeshContext` 固定なのに対し
`targetIndex` 指定へ差し替える**意図的な上書き**なので、これも消してはならない。

## 残件 A ― 検証が付いていないもの

**番号は 2026-09-29 に振り直した。** 拡大縮小と変形ツールの検証は 2026-09-29 に済んだ（規約 10.6.3 / 10.6.5）。

1. **済（2026-09-30）** 実地確認の結果：スキンド化した立方体のボーンにポーズ（Z 回転 45 度・X へ 1）を入れ、
   バインド表示とポーズ表示の両方で操作して、作られた頂点のローカル座標を `getRawData` で期待値と突き合わせた。
   面追加（マウス 4 点。バインド表示では画面位置どおり、ポーズ表示では逆変換どおり）・ウェイト継承（全頂点がボーン 0 に 1.0）・
   面押し出し（マウスドラッグ。x,y は不変で法線方向のローカル z だけ動く）・ナイフ（`knifeSimpleCut`、正面図）・
   点指定図形（`createPointDefinedPrimitive`。バインド表示では ローカル = ワールド、ポーズ表示では逆変換どおり、既存頂点は共有）の 5 経路とも一致。
   あわせて `PointDefinedToolHandler.WorldOfTargetVertex` の GPU 値が無いときの落ち先を
   `WorldMatrix` 直掛けから `VertexMatrix(i, showBindPose)`（描画と同じ規則）へ直した。
   以下は当時のメモ。
   **`ctx.Project` 配線後の 5 経路は自動試験に向かない。実地で気づいたら直す。**
   面追加・新規頂点のウェイト継承・面押し出し・ナイフ・点指定図形。
   コマンド（`AddFaceCommand` ほか）は点を**メッシュローカル座標**で受け取る設計で
   （`PanelCommand.ToolConfirm.cs:354-356`）、`Active*` 系を通らない。
   `Active*` を通るのは `AddFaceTool.GetLocalPositionFromScreen`（`:846-873`）のような
   **画面のレイからローカル位置を作る箇所**で、そこはマウス経路専用。
   ナイフ・点指定図形も同じ構造。
   因果は明快（`ToolContext.cs:488` が `Project?.ShowBindPose` を返す ／
   `WireGpuWorldReaders` が `Project` を挿す）なので、測定の価値は低いと判断した。
   **バインド表示でスキンドメッシュに面を追加してずれるようなら、ここを疑うこと。**
2. **MQO の階層の未確認部分。** 済（2026-09-30）。
   `roboF_T5_SK.mqo` を bakeMirror・setMeshHierarchyParent 有効で新規読込し、
   `queryModelStructure`（全 MeshContext の `mesh.{i}.name/type/depth/parent/visible/mirrorType/bakedFrom/vertices/faces` を出すよう拡張）で照合した。
   - 件数：MQO 170 オブジェクト＋BakedMirror 86 ＝ 256。mirror 属性 133 のうち 86 がベイク、残る 47 は頂点 0・面 0 の入れ物（ベイク対象外で正しい）。
     BakedMirror は全件、元と同じ親・同じ depth。
   - depth 5 以上：MQO 112 件と一致。実体側全件で「親＝直前の depth−1 のオブジェクト」を満たす（不一致 0）。
     最深は depth 17 の `つめ4+`：`センター→@うえ側→上半身→上半身2→上半身3→@@て_ミラー分岐ルート→左肩→左腕→@うでねじり→左ひじ→@ぜんわん→左手首→@ゆび→子_指obj59→左小指１→２→３→つめ4+`。
   - `obj1`：頂点 0・面 0・mirror 1・表示・子なしの空ルート。
   - 非表示：MQO の visible 0 は 14 件、読込後は 19 件（ベイク 5 件を含む）で一致。
   確認済みの部分：`センター`（index 19）はルートで、子は `@うえ側`（20）と `@した側`（174）。
   ポーズを入れると本体が丸ごと追従する（キャプチャ）。ミラー側は実体側と同じ親・同じ depth。
   `ワーク`（index 1）とその子は `isVisible False` なので、ポーズを入れても画面では動かない。
3. **取り下げ（2026-09-30）** 旧データ（`workaxis.csv` / `ModelDTO.workAxis` を含むプロジェクト）の移行確認。
   旧プロジェクトには対応しない方針のため確認しない。移行コードも削除した
   （`WorkAxisObjectOps.MigrateLegacy`、`CsvModelSerializer` の `workaxis.csv` 読み書き、
   `ModelDTO.workAxis` と JSON 復元側の移行処理）。
4. **`PolyLingPlayerViewerCore.*` の画面への配り。** 済（2026-09-30）。
   上の「画面へ配る規則」の節に結果を書いた。

## 残件 B ― 設計上の保留

1. **取り下げ（2026-09-30）** `UseLocalTransform` の意味分離。false の箇所は必ず値も単位にしており
   実害がない（違反 0 件）。分離はせず、「false なら値も単位」を規約 6.1 として決まりにした。
2. **済（2026-09-30）** `ComputeBindPosesFromList`（静的・インポート時用）を `BindWorldMatrix` の規則へ寄せる。
   注記の「LocalMatrix を積むのでポーズを含む」は誤りで、`CalculateWorldMatrices` は元から
   `BoneTransform` の値を直接積んでおりポーズ層は入らなかった。食い違っていたのは端の扱いで、
   (a) `UseLocalTransform` を見ない、(b) `BoneTransform == null` の要素を子孫ごと飛ばす、
   (c) 範囲外の親を持つ要素を子孫ごと飛ばす、の 3 点。ローカル行列を `ctx.BindLocalMatrix` にし、
   範囲外・自分自身・null の親はルート扱いにして、`ComputeWorldMatrices` の `BindWorldMatrix` と
   同じ規則にした（`ModelContext.MeshList.cs` の `CalculateWorldMatrices`）。
   確認：Zunko_MMD.pmx 読込 → ボーン付き MQO 書出 → `importBonesFromArmature` で読込。
   ボーン 252 本の名前・並び・BoneTransform・BindPose が PMX 読込と一致（差 1.8e-7 以内）、
   PMX 側・MQO 側とも修正前後で BindPose は完全一致。`importBonesFromArmature` は動作した。
   T ポーズ変換（`TPoseConverter`。同じ関数を使う）は実機で試していない。規約 6.1 のもとでは
   通常のデータで計算結果は変わらない。
3. **済（2026-09-30）** `UnityClipApplier.RestWorldOf` の `BindPose.inverse` を `BindWorldMatrix` へ寄せた。
   スキンド・MeshFilter 骨格とも `_skeleton.RestWorldMatrix` を返す。`BuildMapping` で
   `BuildCanonAlignment` の直前に `ComputeWorldMatrices` を通し、古い `BindWorldMatrix` を読まないようにした。
   取込直後は両者が一致する（Zunko_MMD.pmx の全 252 ボーンで差 1.8e-8）ため挙動は変わらない：
   同モデルに `キャッチボール（トマホーク）.vmd` 先頭 3 秒を `exportVmdToMotionJson` で焼いた結果が
   修正前後で完全一致（違いは name 欄だけ）。
   ずれるのはスキン固定のボーン移動（`BindPose = W⁻¹·S0`）や `BakeCurrentPoseToBind` の後で、
   その場合は新しい側（バインド階層）が正しい。この「ずれた状態」での実機比較はしていない
   （スキン固定モードをコマンドから指定できないため）。
4. **方針化（2026-09-30）** 付与親・IK は「保持と書き戻しのみ。評価は実行側」とし、不具合扱いから外す。
   - 付与親：モデル側の置き場は `PmxExtraData.GrantParentBoneName`（名前参照）と `GrantRate`。
     `GrantParentIndex` は PMX ファイル表現（`PMXDocument`）側の索引で、読み書きの境界で名前と相互変換する。VRM1.0 の相当物は `VRMC_node_constraint`
     （Roll / Rotation / Aim、weight 0〜1・負値なし・移動なし、評価順は仕様に無く実装任せ）で、
     PolyLing は `VrmNodeConstraintData` として同じく保持と書き戻しのみ。Unity Humanoid は
     捩りボーン自体を扱わず、親子の Humanoid ボーン間で捩りを割合（既定 50%）で分けるだけ。
   - IK：形式に持つのは PMX だけ。VRM に定義は無く、Unity は `OnAnimatorIK` や
     Animation Rigging でアプリ側が与える。PolyLing は PMX / VMD の経路（`VMDApplier`・
     `VMDIKBaker`・`CCDIKSolver`）だけで評価し、統合経路（`MotionClipApplier`）では評価しない。
   - PolyLing 上で捩りの見た目を確かめたくなったら、VRM の Roll / Rotation の評価を足す。
5. **済（2026-09-30）** リモート同期（`RemoteProgressiveSerializer`）の作業軸。
   メッシュ概要（PLRS）を版 5 にして作業軸の値を、モデル情報（PLRM）を版 7 にして
   使う作業軸の ID（`ActiveWorkAxisObjectId`）を、それぞれ末尾に足した（2026-09-29）。
   接続後の値の変更は push `workAxisChanged`（`RemoteWorkAxisSync.cs`）で送る（2026-09-30）。
   送信は `PolyLingPlayerViewerCore.NotifyWorkAxisChanged`（作業軸を変える確定経路はすべてここを通る。
   ギズモのドラッグは確定時の `SetWorkAxisCommand` で 1 回）、受信は `ApplyRemoteWorkAxis` が
   `RefreshWorkAxisViews`（送り返さない方）で画面を更新する。
   実地確認：Editor（クライアント）を別プロセスのサーバへつなぎ、フェッチで作業軸と使う軸が
   届くこと、サーバで値を変えると取得し直さずに受け側の値とギズモが追従することを見た。
   前提として、ボーンウェイトの電文が「ウェイトなし」を運べず受け側で Skinned に化けて
   原点に潰れていた件を先に直した（`MeshFieldFlags.BoneWeightPresence`）。
   プロジェクト共有の作業軸ライブラリ（`ProjectContext.WorkAxes`）はこの経路では送らない（未対応のまま。
   `PolyLing_残件.md` の R-1 で、バイナリを規約 4 の対称に入れる件として扱う）。

**取り下げ**：以前ここにあった「IK（`CCDIKSolver`）が `BonePoseData` ではなく `WorldMatrix` を
直接書く」は、現在のコードでは成り立たない。`SetBoneRotation`（`CCDIKSolver.cs:614-623`）が
`BonePoseData` の `"IK"` 層へ書き、その後 `ComputeWorldMatrices` を呼ぶ（`:369-372`）。

## 残件 C ― 後始末

残件なし。臨時コマンド（`verifyBindMove` / `verifyBindRotate` / `verifyBindScale` /
`verifyBindDeform` / `verifyBindSculpt`）と調査用コード（`UnifiedBufferManager` の `Dbg*` 一式、
`PlayerViewportManager.GpuSelect.cs` の `*ForVerify` 系）は 2026-09-29 に削除した。
消したファイル（`PanelCommand.TempVerify.cs` / `PlayerCommandDispatcher.TempVerify.cs`）は
`_McpTrash/20260929_2148*/` に移してある。再び検証が要るときはそこから戻すこと。
下の「検証手順の癖」に出てくるコマンド名は、この削除済みのコマンドのこと。

保険として残した CPU 経路（`RotateTool` / `ScaleTool` / `DeformApplier` の
`CaptureStartWorld` ほか）は残件ではない。利用者の指示で残すもので、規約 10.6.2 に書いてある。

## 検証手順の癖（はまった点）

臨時コマンドは削除済み（上の残件 C）。以下は、同じ種類の検証を組み直すときの注意として残す。

- **数値だけで合否を書かない。必ずキャプチャを撮る。** `verifyBindMove` の誤差が 0 でも、
  画面に出ているとは限らない。実際 2026-09-15 まで、姿勢は CPU 側で正しく計算されていたのに
  GPU の行列表が古いままで、画面は 1 ミリも動いていなかった。
  「合格」と書く前に、動かした部位が見える絵を 2 枚（変更前・変更後）撮って比べること。
- **試験で状態を戻すときは GPU 側も戻す。** `verifyBindSculpt` の巻き戻しが `Vertices[]` を
  戻すだけで GPU の `_positions` へ送り直していなかったため、1 回目（バインド側）のブラシ結果を
  2 回目（ポーズ側）の測定が拾い、「CPU と GPU が 2100 頂点でずれる」と何度も誤って報告した。
- **視点はコマンドで動かせる**。`fitCameraToSelection`（選択した描画オブジェクトへ寄せる）・
  `fitCameraToModel`・`setCamera`・`queryCamera`・`resetCamera` がある。以前ここに
  「視点を動かすコマンドが無い」と書いてあったが、2026-09-29 時点では誤り。
  動かした部位が見える位置へ寄せてから撮ること。
- **読み込んだモデルは索引 0 に来ない**。`ResetProject` のあと読み込むと、空のモデルが索引 0 に
  残り、読み込んだモデルは索引 1 になる。`queryModelStructure` は
  `project.GetModel(c.ModelIndex)`（`PlayerCommandDispatcher.Query.cs:49`）を引くので、
  既定の 0 では空モデルを見て `meshContexts: 0` を返す。`model_index` を 1 にすれば見える。
  以前ここに書いてあった「既存モデルがあるとカレントモデルが切り替わらない」は症状の取り違え。
  実行系のコマンドはカレントモデル（`project.CurrentModel`）を見るので索引指定の影響を受けない。
- **`verifyBindMove` はプロジェクトが無い状態からは呼べない**。`createsOwnProject`
  に入っていないため `no project` で弾かれる。
  Play を入れ直した直後は、先に `importMqoFile` などで 1 本読んでから呼ぶ。
- **検査頂点は「姿勢が動いた頂点」から選ぶ**。現在ポーズ表示とバインド表示で頂点行列が
  同じ頂点は、書き戻しが何であっても誤差 0 になる。そこを測って「合った」と言ってはならない。
  `verifyBindMove` はポーズを入れたあと全頂点を走査し、2 つの行列が tolerance を超えて違う
  頂点だけを候補にする（候補が 0 なら失敗）。拾った頂点が全て候補であることは
  `posedVertices == testedVertices` で確認する。
- **試験の軸はポーズと交換可能にしない**。往路と復路で同じ行列を使う実装は、
  その行列が変形と交換可能なら打ち消し合って通る。ポーズが Z 回転のときに試験も
  Z 回転にすると、旧コードでも誤差が消えて `legacyError` が 0 になり、
  何も測れていないのに合格に見える。ポーズ Z 30° なら試験は X 軸で回すこと。
- **`discriminating` は構造で効く側が入れ替わる**。旧コードとの差は、非スキンドなら
  `legacyBindError`（バインド表示）、スキンドなら `legacyPoseError`（現在ポーズ表示）にしか
  出ない。片方が 0 でも異常ではない。`bindDiscriminating` / `poseDiscriminating` を見ること。
- **誤差 0 だけを根拠にしない**。`VertexMatrix × VertexMatrix⁻¹ × D = D` は行列が何であっても
  成り立つ。`posedVertices == testedVertices` と `discriminating` が両方立っていて初めて
  `bindMaxError` / `poseMaxError` が意味を持つ。
- **PMX の masterIndex の見当**。`_0_ぜろちゃんN…` では 0 がルート（`ステージ`）、
  低い側がボーン（`頭` = 20）、188 以降が描画オブジェクト（190 = `顔肌+`、2585 頂点）。
  総当りせず `queryBone`（名前）と `queryDrawableStats`（masterIndex）で当たりを取る。
- **`selectElements` は空の配列パラメータも全部明示する**。
  `masterIndices` / `vertexIndices` / `vertexMeshIndices` / `edgePairs` / `edgeMeshIndices` /
  `faceIndices` / `faceMeshIndices` / `lineIndices` / `lineMeshIndices` / `additive`。
  一つでも欠けると「パラメータ "…" が要る」で弾かれる。
- **`setPoseDisplayMode` を挟むと頂点選択が消える**。移動を試すときは切替の**後**に選択する。
  原因は未確認。
- **`queryDrawableStats` はバウンディングボックスを返さない**。説明には書いてあるが
  実際の戻りは頂点数・面数・材質数・境界ループ数のみ。判定基準に使えない。
- **OBJ は約 15 MB あり `read_lines` では開けない**（上限 8 MB）。`search_text` の正規表現で
  特定の座標値を引く。頂点は先頭にまとめて出力され、オブジェクト名は `o 顔肌` の形で
  面定義の直前に出る。頂点の並びが `MeshObject` の索引順とは限らない点に注意。
- テスト用 PMX:
  `データとツール/だいじなデータ/テスト用データ/PMXデータ_壊し用/_0_ぜろちゃんN_UVもーふ減らしたAB.pmx`
  （694 meshContexts / 186 bones / 88 drawables / 約 9 万頂点）。
  VMD は同フォルダ。`exportVmdToVrma` は Humanoid 割当が要るので
  `importPmxFile` に `humanoidAutoMap=true` を付ける。

## 途中で撤回した判定（繰り返さないため）

- 「`DispatchTransformVertices` が展開を呼ばないのが原因」と述べたが誤り。
  `UnifiedSystemAdapter.UpdateTransform` が `:457` で `DispatchExpandVertices` を呼んでいる。
- 表示切替が「効いていない」と何度か判定したが、実際は**効いていた**。
  カメラが正面・遠景で、ポーズを入れた腕が体に重なる角度だったため差が見えなかっただけ。
  カメラを寄せて背面寄りにしたら明確に切り替わった。**キャプチャで差を見るときは、
  動かした部位がはっきり見える角度にしてから撮ること。**
- `ObjectMoveTool.Apply.cs:639` を「ポーズを含めない側」と分類したが、直前で
  `TPoseConverter.BakeSkinnedVertices` が頂点を現在姿勢で焼いているため、
  ポーズ込み（`BakeCurrentPoseToBind`）が正しい。
- 「`MeshInfos` が 256 固定で伸長されない」と書いたが誤り。
  `EnsureCapacity`（`UnifiedBufferManager.cs:809-823`）が 256 を超えたときに
  `_meshInfos` / `_meshExpandedStart` / `_meshExpandedCount` を一緒に伸長し、
  `_meshInfoBuffer` も作り直している。`:648-652` の 256 は初期確保の値。
