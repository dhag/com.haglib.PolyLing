# PolyLing 姿勢まわり ― 残件と再開メモ

最終更新: 2026-09-15。

## まず読むもの

`Packages/com.haglib.polyling/PolyLing_姿勢の規約.md`（10 章）。
設計の理由・用語・式・割り切り・実装の所在・確認済みと未確認の区別が全部そこにある。
このファイルは残件と手順の癖だけを書く。

要点だけ再掲すると、

- 出発点は「姿勢の定義が曖昧で、暗黙にバインドポーズと仮定していた／グローバルで
  制御していた」こと。バインド階層が保存されておらず、要る側が毎回撮り直すか
  組み直していた（規約 5 章）。
- 割り切り：厳密な逆スキニング（重み付き合成後の行列の逆を 1 点ずつ）はやらない。
  現在ポーズ表示中の移動は不自然でも許容する。**バインドポーズ表示中の変換だけは
  厳密に合わせる**（規約 9.1 / 10.2）。

## 完了したもの

- 規約メモ（10 章）
- `MeshContext.BindLocalMatrix` / `BindWorldMatrix` / `BindWorldMatrixInverse`、
  `ComputeWorldMatrices` の 2 系統並走（ミラー共役は両方へ同じく適用）
- `Core/Ops/BindPoseOps.cs`（`RebindToBind` / `BakeCurrentPoseToBind`）と
  呼び出し 9 箇所の置き換え
- `UnityClipVirtualSkeleton.RestWorldMatrix` を `BindWorldMatrix` 参照へ統合
- コマンド：`setBonePoseValue` / `setPoseDisplayMode` / `setActiveWorkAxis` /
  `createWorkAxisObject`
- `queryBone` に `bindPosition`（BindWorldMatrix）と `bindPosePosition`（BindPose.inverse）
- 表示切替：`ProjectContext.ShowBindPose` → 行列表 → 左ペインのトグル。
  キャプチャで腕の姿勢が切り替わることを確認済み
- `ToolContext` の書き戻し追従（`ShowBindPose` で `ActiveWorldMatrix` /
  `ActiveWorldMatrixInverse` / `ActiveVertexMatrix` を切替）
- 既存不具合の修正：`resetBonePoseLayers` の `ComputeWorldMatrices` 漏れ
- 作業軸オブジェクト化（第1〜3段）

一時ログは全て削除済み。3 プロジェクトともコンパイル 0 エラー。

## 画面へ配る規則（2026-09-15 に洗い出し）

モデルの階層行列や構造を変えたコマンドは、末尾でビューポートへ配ること。
どれを呼べばよいかは次で決まる。

| 呼ぶもの | GPU の変換行列を押し込むか | 使う場面 |
|---|---|---|
| `EnterTopologyChanged` / `EnterProjectChanged` | **押し込む**（`RebuildAdapter` → `MeshSceneRenderer.cs:446-448` で `ShowBindPose` 再配布・`UpdateTransform`・`Writeback`） | リスト構造が変わる（生成・破棄・ミラーの付け外し・焼き込み） |
| `EnterVerticesMoved` / `PresentAll` のみ | **押し込まない** | 姿勢だけ変えたときは `UpdateTransform()` を別に呼ぶ（規約 10.5） |
| `_notifyPanels` のみ | 届かない | パネル表示の更新だけ |

`ComputeWorldMatrices()` の呼び出し 38 か所（Poly_Ling_Player 配下）を上の基準で当たった結果、
取りこぼしは `setMirrorEnabled` の 1 件だけだった。次は安全と確認済み。

- `ObjectOrigin.cs` の 4 コマンド（:163 / :487 / :540 / :746）
- `BoneMorphUv.cs:735`（スキンごと確定 → :763）
- `SpringBone.cs:291`（ボーンの親を変える → :304）
- `TPoseMergeMorph.cs:45, 57`（この姿勢で確定 → :82）

未確認：`PolyLingPlayerViewerCore.*`（生成メッシュ・ブリッジ・読み込み）側は見ていない。

## 残件 A ― 検証が付いていないもの

1. **済（2026-09-15）**。バインド表示中の頂点移動は掴んだ位置と厳密に一致する。
   非スキンド・スキンドとも合格。`verifyBindMove` を `delta=(7,0,0)` / `poseRotationZ=30` で実行。

   | | MQO `obj63あたま_old`（非スキンド） | PMX `顔肌+`（スキンド・ポーズは `頭`） |
   |---|---|---|
   | posedVertices / testedVertices | 50 / 50 | 50 / 50 |
   | matrixMaxDiff | 0.500 | 0.841 |
   | bindMaxError | 0 | 0 |
   | poseMaxError | 9.5e-07 | 9.5e-07 |
   | legacyBindError | 3.500（効く） | 0（効かない） |
   | legacyPoseError | 3.2e-07（効かない） | 3.500（効く） |
   | pass | true | true |

   旧コードとの差は、非スキンドならバインド表示、スキンドなら現在ポーズ表示にしか出ない。
   理由と限界は規約 10.4 を参照。OBJ を書き出して座標を検索する手順は使わなかった。
2. 左ペイン「バインドポーズ表示」トグルの目視・操作確認。**画面操作が要る**。
   `uiGetValue` では引けない（`PlayerLayoutRoot` のトグルは `[UiControl]` を持たない流儀）。
   コマンド経由（`setPoseDisplayMode`）で絵が切り替わることは確認済み（規約 10.5）。
   残るのはトグルを人が押したときに同じ経路を通るかの確認。
3. 旧データ（`workaxis.csv` / `ModelDTO.workAxis` を含むプロジェクト）の移行確認。
5. **`ctx.Project` 配線後の 5 経路の実測**。面追加・新規頂点のウェイト継承・面押し出し・
   ナイフ・点指定図形が、バインド表示で掴んだ位置と一致するか。
   いずれもクリック位置・ホバーを起点にするためコマンドから再現しにくい。
   自動試験を作るなら面追加が一番作りやすい。
6. **`GetCurrentToolContext` を経由しない `ToolContext` 2 か所**。
   `ToolManager.cs:70` と `PlayerCommandDispatcher.Blend.cs:603`（`BuildMinimalToolCtx`）は
   `Project` が入らないので `ShowBindPose` が常に false。
   バインド表示に関わる変換を使っているかは未確認。
7. **既存ハンドラに散っている `GetVertexWorldPosition` の個別配線**
   （`AddFaceToolHandler.cs:520`、`EdgeBevelToolHandler.cs:169, 195, 228`、
   `EdgeExtrudeToolHandler.cs:169, 195`、`FaceExtrudeToolHandler.cs:227` ほか）は
   中央配線と同じ値なので重複。動作確認が済んだら消す。
   ただし `PointDefinedToolHandler.cs:628` は `targetIndex` 指定への**意図的な上書き**。
   中央配線は `ActiveMeshContext` 固定なので、ここは消してはならない。
8. `SculptTool.cs:298, 430, 520` と `DeformApplier.cs:203` の前方向も
   `WorldMatrixInverse` / `WorldToLocal` のまま。回転・拡大縮小と同じ形へ寄せる。**未着手。**
4. MQO の階層の親子付け。**済（2026-09-15）。切れていない。**
   `センター` は index 19・`depth 0`・`hierarchyParentIndex -1` のルートで、子は
   `@うえ側`（20）と `@した側`（174）の 2 つ。`センター` にポーズ層で `PositionY=0.6` を
   入れると、頭・腕・胴・脚が丸ごと持ち上がり、取り残される部品は無い（キャプチャで確認）。
   ミラー側（`type,MirrorSide`）は実体側と同じ親・同じ `depth` を持ち、親に追従する
   （`MeshHierarchyOps.RecalculateParentIndicesFromDepth:47-53` の規則どおり）。
   最初に「`ワーク` にポーズを入れても動かない」と見えたのは、`ワーク`（index 1）と
   その子が `isVisible False` だったため。階層の問題ではなかった。
   未確認：`BakedMirror` の件数、depth 5 以上の末端、`obj1`（index 0・別ルート）の中身。

## 2026-09-15 に直したもの

- **回転・拡大縮小がスキンドの現在ポーズ表示で壊れていた**。`RotateTool.cs:389, 392` /
  `ScaleTool.cs:289, 293` がメッシュ 1 個の `LocalToWorld` / `WorldToLocal` を使っており、
  スキンド頂点の表示位置（`Σ(w·SkinningMatrix)`）と食い違っていた。
  前方向を GPU 値（`ToolContext.GetMeshWorldPositions`）へ、書き戻しを
  `VertexMatrix(vi, showBindPose).inverse` へ変更。`_pivot` はワールド保持に変更。
  `poseMaxError` が 0.0214 → 2.6e-07。規約 10.6 を参照。
- **`ctx.Project` が未配線で `ToolContext.ShowBindPose` が常に false だった**。
  `PlayerToolContext.ToToolContext`（`:129-147`）も `GetCurrentToolContext`
  （`PlayerViewportManager.cs:443`）も埋めておらず、埋めていたのは
  `PolyLingPlayerViewerCore.Lifecycle.cs:406` のリモート受信経路だけ。
  `ActiveWorldMatrix` / `ActiveWorldMatrixInverse` / `ActiveVertexMatrix` を使う
  面追加・新規頂点のウェイト継承・面押し出し・ナイフ・点指定図形が、
  いずれもバインド表示に追従していなかった。`WireGpuWorldReaders` で 1 行埋めて解決。
  **この 5 経路が実際に直ったかは未測定。**
- **`setMirrorEnabled` がビューポートへ配っていなかった**（`PlayerCommandDispatcher.MeshAttributes.cs:424`）。
  `ApplyMirrorEnabled`（`MirrorHumanoidVrm.cs:607-648`）は `ComputeWorldMatrices` と
  `_notifyPanels(ChangeKind.ListStructure)` しか呼ばず、ミラー側 MeshContext を作る／消しても
  GPU バッファが古いままだった。同ファイルの他のミラー系コマンド（:137, :216, :559）と
  そろえて `EnterTopologyChanged` を足した。
  キャプチャで確認：全オブジェクトのミラーを切ると体の片側が消え、戻すと復元する
  （256 → 255 → 290 contexts。全件 enabled=true は元々ミラーの無いものにも作るので増える）。
- **姿勢を変えても画面に出なかった**（最大の不具合）。`setBonePoseValue` /
  `resetBonePoseLayers` / `bakePoseToBindPose` はビューポートへ何も通知しておらず、
  `setPoseDisplayMode` は `UpdateTransform()` だけを呼んでいた。
  `PresentAll` 経路は GPU の変換行列を push しない（`PlayerCommandDispatcher.BoneMorphUv.cs:310-317`
  の既存注記）ため、`EnterVerticesMoved` と `UpdateTransform()` の**対**が要る。4 か所そろえた。
  規約 10.5 を参照。キャプチャで確認：MQO は全オブジェクト `PositionY=3` で画面外へ飛び
  表示切替で戻る、PMX は `左ひざ`（masterIndex 80）`RotationX=70` ですねが曲がり切替で戻る。
- **`setBonePoseValue` がボーン以外を捨てていた**（`PlayerCommandDispatcher.BoneMorphUv.cs:86`）。
  同じ層へ書く `SetBonePoseActive`（:41）・`SetBoneTransformValue` の PoseLayer 経路（:224）・
  `ApplyPoseLayerField`（`PlayerCommandDispatcher.cs:103-110`）はいずれも型を見ない。
  ボーンを持たない MQO モデルでは非スキンドのポーズを一切作れず、残件 A-1 が検証不能だった。
- **移動が位置キャッシュを捨てていなかった**（`VertexTransforms.cs` の
  `SimpleMoveTransform.SetTotalDelta` / `MagnetMoveTransform.SetTotalDelta`）。
  `MeshObject.Positions` は `Vertices[i].Position` を直接書いたら
  `InvalidatePositionCache()` が要る（`MeshObject.cs:118`）。呼ばないため
  `MoveToolHandler.BeginMove`（`MoveToolHandler.Move.cs:392`）の開始スナップショットが古くなり、
  **2 回目の移動が 1 回目の結果を消していた**。同じ量で 2 回動かすと 2 回目の表示差が
  ちょうど 0 になることで確認（修正前 `poseWorldDelta = (0,0,0)` / `poseMaxError = 7`）。
  `RotateToolHandler.cs:376` / `ScaleToolHandler.cs:360` / `SculptToolHandler.cs:222` /
  `ObjectMoveTool.Apply.cs:138, 405` は元から呼んでいる。
- **ギズモ中心が表示モードに追従していなかった**（`MoveToolHandler.Move.cs` の
  `UpdateGizmoState`）。`LocalToWorld(i, …)` は `VertexMatrix(i)` → `showBindPose = false` 固定
  （`MeshContext.Transform.cs:461, 529-531`）なので、バインド表示中はギズモだけが
  ポーズ込みの位置に出ていた。`VertexMatrix(i, showBindPose)` へそろえた。

## 残件 B ― 設計上の保留

4. `UseLocalTransform` の意味分離。false に 3 つの意図が同居している（規約 6 章）。
5. IK（`CCDIKSolver`）が `BonePoseData` ではなく `WorldMatrix` を直接書く扱い。
   `ComputeWorldMatrices` を呼び直すと消える（規約 7 章）。
6. `ComputeBindPosesFromList`（静的・インポート時用）を `BindWorldMatrix` 経由へ寄せる。
   現状は `ModelContext.MeshList.cs` に注記のみ。この静的経路は `BindWorldMatrix` を
   書かないので、寄せるにはメソッド自体をバインド階層で組み直す必要がある。
7. `UnityClipApplier.RestWorldOf` の `BindPose.inverse` を `BindWorldMatrix` へ寄せる（6 の後）。
   印は同ファイルのコメントに入れてある。
8. 付与親（`GrantParentIndex` / `GrantRate`）の評価コードが無い。
   統合経路（`MotionClipApplier`）に IK が無い（`VMDApplier.cs:37-40`）。
9. リモート同期（`RemoteProgressiveSerializer`）が作業軸の値を転送しない。

## 残件 C ― 後始末

10. 済（2026-09-15）。`SandBox` の検証用 OBJ（`wb_before` / `wb_after` / `wb2_after`）を削除した。
    `delete_path` はフォルダも受ける（`recursive=true`）。以前「エラーを返すので手動」と
    書いてあったのは誤り。ただし消去ではなく `<ルート>/_McpTrash/<日時>/` への移動なので、
    ディスクを空けるにはそちらを手で捨てること。
    2026-09-15 時点で `_McpTrash` に約 190 MB（うち `_0_ぜろちゃんN…` の
    `.morph.csv` が 152 MB）が残っている。
11. 中断時点のモデルは**頂点を動かした状態**。再開時は PMX を読み込み直すこと。

## 検証手順の癖（はまった点）

- **数値だけで合否を書かない。必ずキャプチャを撮る。** `verifyBindMove` の誤差が 0 でも、
  画面に出ているとは限らない。実際 2026-09-15 まで、姿勢は CPU 側で正しく計算されていたのに
  GPU の行列表が古いままで、画面は 1 ミリも動いていなかった。
  「合格」と書く前に、動かした部位が見える絵を 2 枚（変更前・変更後）撮って比べること。
- **視点を動かすコマンドが無い**。`PanelCommand*.cs` にカメラを操作するコマンドは 1 つも無い
  （`Camera` の出現は `SurfaceSnapCameraKind`、UV-Z の投影パラメータ、
  `PanelCommand.Deform.cs:461` のコメントだけ）。角度を変えた確認は人手が要る。
  正面固定で見えない部位を動かすと「変化なし」に見えるので、対象は正面から見える部位を選ぶ。

- **読み込んだモデルは索引 0 に来ない**。`ResetProject` のあと読み込むと、空のモデルが索引 0 に
  残り、読み込んだモデルは索引 1 になる。`queryModelStructure` は
  `project.GetModel(c.ModelIndex)`（`PlayerCommandDispatcher.Query.cs:49`）を引くので、
  既定の 0 では空モデルを見て `meshContexts: 0` を返す。`model_index` を 1 にすれば見える。
  以前ここに書いてあった「既存モデルがあるとカレントモデルが切り替わらない」は症状の取り違え。
  実行系のコマンドはカレントモデル（`project.CurrentModel`）を見るので索引指定の影響を受けない。
- **`verifyBindMove` はプロジェクトが無い状態からは呼べない**。`createsOwnProject`
  （`PlayerCommandDispatcher.cs:733-737`）に入っていないため `no project` で弾かれる。
  Play を入れ直した直後は、先に `importMqoFile` などで 1 本読んでから呼ぶ。
- **検査頂点は「姿勢が動いた頂点」から選ぶ**。現在ポーズ表示とバインド表示で頂点行列が
  同じ頂点は、書き戻しが何であっても誤差 0 になる。そこを測って「合った」と言ってはならない。
  `verifyBindMove` はポーズを入れたあと全頂点を走査し、2 つの行列が tolerance を超えて違う
  頂点だけを候補にする（候補が 0 なら失敗）。拾った頂点が全て候補であることは
  `posedVertices == testedVertices` で確認する。
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
  原因は未確認（`_notifyPanels(ChangeKind.Attributes)` の疑い）。
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
