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

**番号は 2026-09-15 に振り直した。**

1. **拡大縮小が未測定。** 回転とまったく同じ形に直したが、一度も動かしていない。
   `verifyBindRotate` と同じ作りで `verifyBindScale` を足せば測れる。
2. **`ctx.Project` 配線後の 5 経路は自動試験に向かない。実地で気づいたら直す。**
   面追加・新規頂点のウェイト継承・面押し出し・ナイフ・点指定図形。
   コマンド（`AddFaceCommand` ほか）は点を**メッシュローカル座標**で受け取る設計で
   （`PanelCommand.ToolConfirm.cs:354-356`）、`Active*` 系を通らない。
   `Active*` を通るのは `AddFaceTool.GetLocalPositionFromScreen`（`:846-873`）のような
   **画面のレイからローカル位置を作る箇所**で、そこはマウス経路専用。
   ナイフ・点指定図形も同じ構造。
   因果は明快（`ToolContext.cs:488` が `Project?.ShowBindPose` を返す ／
   `WireGpuWorldReaders` が `Project` を挿す）なので、測定の価値は低いと判断した。
   **バインド表示でスキンドメッシュに面を追加してずれるようなら、ここを疑うこと。**
3. **済（2026-09-15）。スカルプトの変形 4 種をワールド空間へ移した。**
   法線キャッシュは `BuildCachesForMesh` に GPU のワールド座標を渡して作る
   （面法線はその場の 3 頂点から作る派生値なので二重計算にならない。
   GPU の `_WorldNormalBuffer` は `_TransformNormals=0` で書かれていない）。
   変形は共通の作業配列（頂点索引 → ワールド座標）を Draw / Smooth / Inflate /
   Flatten が書き換える形に統一し、書き戻しは `WriteBackWorld` で
   `VertexMatrix(i, showBindPose).inverse` を通す。Smooth が隣接を読むので
   `BuildWorkWorld` は隣接も作業配列に入れる。
   `SculptTool` の前方向から `WorldMatrixInverse` / `WorldToLocal` は消えた。
   **`BrushRadius` に加えて `Strength` の尺度もワールドになった。**
   メッシュにスケールが入っていれば効き方が変わる。
4. **済（2026-09-15）。`DeformApplier` の前方向を GPU 値へ。**
   `_startWorld`（ワールド）を新設し、`_startPositions`（ローカル）はそのまま残した。
   後者は `DeformContext` の元で `LatticeDeformer.FitToSelection` が参照するため、
   意味を変えられない。書き戻しは `VertexMatrix(i, showBindPose).inverse`。
   口は `GetMeshWorldPositions` / `GetShowBindPose` で、`Begin` の引数は変えていない。
   呼び出し元は `DeformToolHandler.cs:235` と `LatticeToolHandler` の 3 か所
   （後者は `WireApplierWorldReaders()` にまとめた）。
5. **済（2026-09-15）。**左ペイン「バインドポーズ表示」トグルは
   `SetPoseDisplayModeCommand` を送るだけ（`PlayerLayoutRoot.LeftPane.cs:88, 271`）。
   そのコマンドは絵で確認済み（規約 10.5）なので、トグル単独の目視確認は要らない。
6. **MQO の階層の未確認部分。** `BakedMirror` の件数、depth 5 以上の末端、
   `obj1`（index 0・`センター` とは別ルート）の中身。
7. **臨時コマンドと調査用コードの後始末。** 検証が済んだら削除する。
    - `verifyBindMove` / `verifyBindRotate` / `verifyBindSculpt`
      （`PanelCommand.TempVerify.cs` と `PlayerCommandDispatcher.TempVerify.cs`）
    - `UnifiedBufferManager` の `Dbg*` 一式（`:550-588`）と
      `UnifiedBufferManager_Build.cs` の `DbgWrite*` / `DbgNote*` 呼び出し
    - `PlayerViewportManager.GpuSelect.cs` の `*ForVerify` 系の読み口
    - `DbgNoteWriter`（`:569`）は今日より前から仕込まれていたもの。呼び出しだけあって
      読み出し口が無かった。過去に同じ場所を疑った形跡。
8. **保険として残した CPU 経路。** `RotateTool` / `ScaleTool` / `SculptTool` /
    `DeformApplier` の `CaptureStartWorld` / `GetWorldPositions` と、その周辺。
    GPU 値が取れないときだけ
    `VertexMatrix(i, showBindPose)` に落ちる。利用者の指示で承知のうえ残したもので、
    コードに「新しいコードでこの分岐を真似しないこと」と明記済み（規約 10.6.2）。
9. 旧データ（`workaxis.csv` / `ModelDTO.workAxis` を含むプロジェクト）の移行確認。

**取り下げ**：以前ここに「`MeshInfos` が 256 固定で伸長されない」と書いたが誤り。
`EnsureCapacity`（`UnifiedBufferManager.cs:809-823`）が 256 を超えたときに
`_meshInfos` / `_meshExpandedStart` / `_meshExpandedCount` を一緒に伸長し、
`_meshInfoBuffer` も作り直している。`:648-652` の 256 は初期確保の値。
`_transformMatrices` が別の場所で伸びるのは、そちらが `contextCount`、
`_meshInfos` が `meshCount` という別の数え方だから。

## 検証が済んだもの（2026-09-15）

- **移動**：バインド・現在ポーズ表示とも表示どおり。非スキンド／スキンドの 4 組。
  `bindMaxError = 0` / `poseMaxError = 9.5e-07`。**GPU 基準でも一致**
  （`poseGpuMaxError = 1.4e-06`、`cpuGpuGap = 6.7e-07`）。
- **回転**：同じく 4 組。試験回転 X 35° で `bindLegacyError = 0.0305`（非スキンド）、
  `poseLegacyError = 0.0214`（スキンド）の条件下で誤差 1e-07 台。
- **スカルプトの当たり判定**（A-1 の範囲）：バインド・現在ポーズ表示とも中心に当たる。
  旧コードはバインド表示で 1 頂点も拾えていなかった（`bindLegacyHit = 0`）。
- **スカルプト（当たり判定・変形・法線とも）はワールド空間。** 前方向は GPU 値、
  書き戻しのみ `VertexMatrix(i, showBindPose).inverse`。MQO・PMX の 4 組で
  `bindGapCount = poseGapCount = 0`、中心に置いた頂点が両表示とも動く。
  旧コードとの差は両表示で検出できている（MQO `bindLegacyHit = 0`、
  PMX `poseLegacyHit ≠ poseHit`）。
- **`DeformApplier` の前方向も GPU 値。** 書き戻しは頂点単位の逆行列。**未測定。**
- **`MeshContext.VertexMatrix` は GPU の規則と一致する。**
  PMX スキンド `顔肌` の全 2585 頂点で、CPU の `VertexMatrix × Vertices[].Position` と
  GPU の `_worldPositions` が 1e-07 台で一致（`poseGapCount = 0`）。
  読み込み直後の入力座標も全頂点一致（`loadInputGapCount = 0`）。
  規約 10.6 に書いた「GPU と突き合わせていない」という限界は、この範囲で解消。

  **註**：この確認の途中で「CPU と GPU が 2100 頂点でずれる」と何度か報告したが、
  すべて誤りだった。原因は `verifyBindSculpt` の後始末で、`RunOneSculpt` の巻き戻しが
  `Vertices[]` を戻すだけで GPU の `_positions` へ送り直していなかったため、
  1 回目（バインド側）のブラシ結果を 2 回目（ポーズ側）の測定が拾っていた。
  **試験で状態を戻すときは GPU 側も戻すこと。**
- **MQO の階層は切れていない。** `センター`（index 19）はルート、子は `@うえ側`（20）と
  `@した側`（174）。ポーズを入れると本体が丸ごと追従する（キャプチャ）。
  ミラー側は実体側と同じ親・同じ depth。
  最初に「`ワーク` にポーズを入れても動かない」と見えたのは、`ワーク`（index 1）と
  その子が `isVisible False` だったため。階層の問題ではなかった。

## 2026-09-15 に直したもの

- **`ToolManager` / `ToolRegistry` を未使用クラスとして削除した**（2026-09-15）。
  `new ToolManager()` の呼び出しも `ToolRegistry` の参照もパッケージ全体で 0 件だった。
  `ToolRegistry.ToolFactories` に登録されていた 15 ツールは、すべて
  `PolyLingPlayerViewerCore.Layout.EditTools.cs` のハンドラ方式に置き換わっており、
  ハンドラ側に無いツールは混じっていなかった。
  参照していた注記 3 か所（`DeformerRegistry.cs:3`、`AdvancedSelectTool.cs:84`、
  `MoveTool.cs:32-34`）も直した。
- **`BuildMinimalToolCtx` に `ctx.Project` を挿した**（`PlayerCommandDispatcher.Blend.cs:603`）。
  呼び出し元は UV 展開・UV↔XYZ・スキンウェイト一括・法線移植・薄板モーフ・
  シュリンクの 8 か所。これらが `Active*` 系を使うかは未確認だが、挿しておけば害がない。
- **スカルプトの当たり判定がローカル空間だった**（`SculptTool.cs`）。ブラシ中心とレイを
  メッシュ 1 個の行列でローカルへ落としていたため、画面で指した場所と違うところに当たっていた。
  `FindBrushCenter` の交差判定と `GetVerticesInBrushRadius` をワールド空間へ移し、
  頂点座標に GPU の値を使うようにした（A-1 の範囲。変形 4 種はローカルのまま）。
  非スキンドのバインド表示では旧コードが 1 頂点も拾えていなかった（`bindLegacyHit = 0`）。
  `BrushRadius` の尺度がローカルからワールドへ変わる。
- **回転・拡大縮小がスキンドの現在ポーズ表示で壊れていた**。`RotateTool.cs:389, 392` /
  `ScaleTool.cs:289, 293` がメッシュ 1 個の `LocalToWorld` / `WorldToLocal` を使っており、
  スキンド頂点の表示位置（`Σ(w·SkinningMatrix)`）と食い違っていた。
  前方向を GPU 値（`ToolContext.GetMeshWorldPositions`）へ、書き戻しを
  `VertexMatrix(vi, showBindPose).inverse` へ変更。`_pivot` はワールド保持に変更。
  **非スキンドのバインド表示でも壊れていた**（legacy 比較を足して判明。
  試験回転 X 35° で `bindLegacyError = 0.0305`）。規約 10.6 を参照。
  拡大縮小は同じ形に直したが**未測定**。
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
