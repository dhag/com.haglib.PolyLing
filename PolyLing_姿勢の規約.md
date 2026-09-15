# PolyLing 姿勢の規約

作成日: 2026-09-14。調査時点のコードに対する記述で、断定はすべて file:line の根拠つき。
目的は「バインドポーズ」「現在ポーズ」の定義を 1 か所に固定し、VMD / UnityClip / VRMA /
手作業のポーズを同じ土台に載せること。このメモの段階ではコードは変更していない。

---

## 1. 登場人物（オブジェクト 1 個が持つもの）

| 名前 | 型 | 置き場所 | 役割 |
|---|---|---|---|
| `BoneTransform` | 位置・回転(オイラー)・拡大 | `MeshObject.BoneTransform`（`MeshContext.Transform.cs:174`） | ローカル姿勢。**バインド側の値** |
| `UseLocalTransform` | bool | `BoneTransform.cs:71` | false のとき上を無視して単位行列にする |
| `BonePoseData` | レイヤー合成 | `MeshContext.Transform.cs:183` | バインドからの**差分＝ポーズ** |
| `WorldMatrix` | 4×4 | `MeshContext.Transform.cs` / `ComputeWorldMatrices` が書く | 親から積んだ結果（**ポーズ込み**） |
| `BindPose` | 4×4 | `MeshContext.Transform.cs:253` | 撮った瞬間の `WorldMatrix` の逆 |

## 2. 式（これがすべて）

```
LocalMatrix = (UseLocalTransform ? BoneTransform.TransformMatrix : I)
              × (BonePoseData.IsActive ? BonePoseData.LocalMatrix : I)      … MeshContext.Transform.cs:193-209

WorldMatrix = 親の WorldMatrix × LocalMatrix                                 … ModelContext.MeshList.cs:340-391

SkinningMatrix = WorldMatrix × BindPose                                      … MeshContext.Transform.cs:367
```

ミラー側だけ例外で、実効ワールドを共役 `S·H·S` にする（`ModelContext.MeshList.cs:373-387`）。

## 3. 頂点の座標系（スキンドか否かで違う）

`MeshContext.Transform.cs:68-84` に既述。

- 非スキンド … 頂点はローカル空間。ワールドへ出すには `WorldMatrix` を掛ける。
- スキンド … 頂点はバインド空間。描画は `SkinningMatrix` を通し、静止時は単位になる。
  ここへ `WorldMatrix` を掛けると二重に効く。

## 4. ポーズ層への書き手（4 つ。ここは既に統一されている）

| 書き手 | 層の名前 | 場所 |
|---|---|---|
| VMD | `"VMD"` | `VMDApplier.cs:388` |
| Unity クリップ | `LayerName` | `UnityClipApplier.Support.cs:234` |
| 統合モーション | `VmdLayerName` | `MotionClipApplier.cs:227` |
| 手作業 | `"Manual"` | `PlayerCommandDispatcher.BoneMorphUv.cs:148-152`（`BoneMoveMode.PoseLayer` のとき） |

つまり **VMD だけが独立しているという事実はない**。独立しているのはモーションの読み書きと
ボーン名の対応づけ（`VMDData` / `VMDApplier._boneNameToIndex`）まで。

ポーズ層は `MeshContext` 一般の持ちもので、ボーン専用ではない。`LocalMatrix` / `BindLocalMatrix`
（`MeshContext.Transform.cs:193-209, 263-271`）は `Type` を見ずに `BonePoseData` を評価する。
描画メッシュ自体がボーンと同じ階層構造を持つ（メッシュフィルタ相当）ため、非スキンドの描画
オブジェクトへもポーズが入る。`setBonePoseValue` は 2026-09-15 まで `MeshType.Bone` 以外を
捨てていたが、`SetBonePoseActive` / `SetBoneTransformValue` の PoseLayer 経路と同じく
型を見ない形へそろえた。

## 5. 今の問題：バインド側の階層行列が保存されていない

`WorldMatrix` は常にポーズ込み。バインド側の 4×4 はどこにも残っていないため、必要な側が
その場で撮り直すか、その場で組み直している。

### 5.1 撮り直し（`BindPose = WorldMatrix.inverse`）

- `ModelContext.MeshList.cs:460`（ボーン一括）
- `ModelContext.MeshList.cs:503`（`ComputeMeshFilterBindPoses`。非スキンドのオブジェクト）
- `ModelContext.MeshList.cs:600`（インポート時一括）
- `MeshListOps.cs:538-546`（撮り直し＋Undo 記録）
- `PlayerCommandDispatcher.BoneMorphUv.cs:65`（ポーズ→バインドのベイク）
- `TPoseConverter.cs:92, 318` / `SkinKindConverter.cs:188, 319`
- `SpringBoneOps.cs:585` / `SpringBoneChainPlacer.cs:275` / `SpringBoneTestRigBuilder.cs:603`
- `ObjectMoveTool.Apply.cs:113, 419, 639`（ボーン移動のリバインド。`WorldMatrix.inverse × 旧値` の形も混在）
- 読み込み由来：`PMXImporter.Bone.cs:261` / `MQOImporter.MirrorBone.cs:254` /
  `ModelSerializer_Vrm.cs:365` / `CsvMeshSerializer.Read.cs:199`

### 5.2 その場で組み直し（ポーズを含めない累積）

- `UnityClipVirtualSkeleton.RestWorldMatrix`（`UnityClipVirtualSkeleton.cs:431-`）。
  `BoneTransform` だけを親から積む。
  利用側：`UnityClipApplier.cs:436` / `VmdNodeWorldSampler.cs:115` /
  `UnityClipVrmAnimationSource.cs:223`。
- なお `UnityClipApplier.cs:434` は同じ用途で `ctx.BindPose.inverse` を使う。
  **同じ「レスト」に 2 つの出どころがある。**

`UnityClipVrmAnimationSource.cs:34-45` に、`ctx.WorldMatrix` をレストに使うと
T ポーズでなくなる旨が明記されている。これが 5 の症状。

## 6. `UseLocalTransform` の意味が 3 つ同居している

`LocalMatrix` 上の効果は「ローカル行列を単位にする」だけで、`Position/Rotation/Scale` が
単位のときと区別できない。にもかかわらず、次の 3 つの意図で false が使われている。

1. 頂点をワールドへ焼いたので姿勢を持たない … `MeshFilterToSkinnedConverter.cs:709-718`、
   `SkinKindConverter.cs:310-319`
2. 既定値のまま … `BoneTransform.cs:61`（`_useLocalTransform = false`）
3. ローカルが単位でよい（親の空間にそのまま乗る） … `PartsIdSplitInserter.cs:129-135`

true 側も入り方がばらばら。`MeshListOps.cs:615-618` はメッシュリストで数値を入れた瞬間に
無条件で true にする（＝ユーザー操作で意味が変わる）。ほかに
`HierarchyReparentOps.cs:136` / `MirrorBranchOps.cs:442` / `SpringBoneOps.cs:547` /
`MeshFilterToSkinnedConverter.cs:515`。

## 7. ポーズの正典が割れている箇所

- IK（`CCDIKSolver`）は `BonePoseData` ではなく `WorldMatrix` を直接書き換える
  （`VmdNodeWorldSampler.cs:36-37` の注記）。`ComputeWorldMatrices` を呼び直すと消える。
- 付与親（`GrantParentIndex` / `GrantRate`）は読み書きと取込では保持されるが、
  姿勢適用側に評価コードが無い（`VMDApplier.cs:37-40`）。
- 統合経路（`MotionClipApplier`）には IK が無い（同上）。

## 8. 決めること（このメモの次）

1. **規約**：`BoneTransform` をバインドのローカル姿勢と定義する。現在ポーズは
   `BonePoseData` の層合成だけが担う。
2. **保存**：`BindWorldMatrix`（ポーズを掛けない階層行列）を持たせ、
   `BindPose = BindWorldMatrix.inverse` を導出に変える。5.1 と 5.2 をここへ寄せる。
3. **表示**：現在ポーズ／バインドポーズの切替。非スキンドは
   `WorldMatrix` ↔ `BindWorldMatrix`、スキンドは `SkinningMatrix` ↔ 単位行列の差し替え。
4. **保留**：`UseLocalTransform` の意味の分離（6）と、IK の置き場所（7）。
   1〜3 とは独立に直せるので後回しにする。

---

## 9. 頂点の書き戻し規約（ワールド → ローカル）

画面の操作はワールドの量として入ってくるので、頂点へ書く前にローカルへ落とす。
落とし方が 2 系統あり、使い分けを次のとおり固定する。

| 経路 | 使う行列 | 場所 | 使う場面 |
|---|---|---|---|
| 頂点単位 | `VertexMatrix(i).inverse` | `ToolContext.cs:520-521`、`MeshContext.Transform.cs:426-453` | **既定。スキンド頂点は必ずこちら** |
| オブジェクト単位 | `WorldMatrixInverse` | `ToolContext.cs:476-479` | 非スキンドのみ |

`VertexMatrix` はウェイト付き頂点なら各ボーンの `SkinningMatrix` を重み付きで加算した
行列を返し、ウェイトが無ければ `WorldMatrix` を返す。オブジェクト単位で済ませると
スキンド頂点で倍率と向きが合わない（`EdgeBevelTool.cs:402-406`、
`EdgeExtrudeTool.cs:427-430` に実例の注記）。

### 9.1 割り切り：厳密な逆スキニングは行わない

厳密には「重み付き合成後の変換行列の逆行列」を 1 点ごとに解く必要がある。
現状は Σ(w·M) を組んでその逆を使う近似で通す。1 ボーン 100% の頂点は厳密、
複数ボーンにまたがる頂点は近似になる。**当面はこれで良いと決める**（将来の改善余地として残す）。

`AddFaceTool.cs:950-957` のように、オブジェクト単位で作った座標をあとから
継承元の行列で入れ直している箇所がある。基準の不一致が残っている印なので、
新規コードでは最初から頂点単位で作ること。

## 10. 表示切替（現在ポーズ / バインドポーズ）との関係

### 10.1 原則：表示に使った行列と、書き戻しに使う逆行列は同じものにする

バインド表示中に現在ポーズの行列で書き戻すと、画面で掴んだ位置と書き込む値がずれる。
切替は「行列を差し替える」操作なので、表示側と編集側の両方を同時に差し替える。

|  | 現在ポーズ表示 | バインドポーズ表示 |
|---|---|---|
| 非スキンド | `WorldMatrix` | `BindWorldMatrix`（8-2 で新設） |
| スキンド | `SkinningMatrix = WorldMatrix × BindPose` | 単位行列（頂点値がそのままワールド） |

### 10.2 精度の約束

- **バインドポーズ表示中の移動・変形はきちんと合わせる。**
  この状態では、非スキンドは単一行列 `BindWorldMatrix` の逆、スキンドは単位行列の逆であり、
  どちらも 9.1 の近似を通らない。したがって厳密に一致させられる。ここを外さないこと。
- **現在ポーズ表示中の移動・変形は、不自然になっても許容する。**
  9.1 の近似がそのまま出るため。正確さが要る作業はバインド表示に切り替えてから行う、
  という運用にする。

### 10.3 ポーズ中の頂点編集の意味

頂点の値はバインド空間にあるので、ポーズ中に頂点を動かすと「ポーズを解いた状態の形状」が
変わる。これは禁止しない（10.2 の後段のとおり許容）。ただし結果が直感と合わないことがある。

### 10.4 実装（2026-09-14 時点）

切替の正典は `ProjectContext.ShowBindPose`（保存しない。起動時は常に現在ポーズ表示）。

| 役割 | 場所 |
|---|---|
| 切替コマンド | `SetPoseDisplayModeCommand`（`PanelCommand.Bone.cs`）／MCP 名 `setPoseDisplayMode` |
| 受け口 | `PlayerCommandDispatcher.BoneMorphUv.cs`。`ProjectContext` へ書き、描画へ配り、左ペインのトグルへ書き戻す |
| 描画への配り | `PlayerViewportManager.SetShowBindPose` → `MeshSceneRenderer.SetShowBindPose` → 各 `UnifiedSystemAdapter.ShowBindPose` |
| 行列表 | `UnifiedBufferManager.UpdateTransformMatrices(…, showBindPose)`。非スキンドは `BindWorldMatrix`、ボーン・スキンドは単位行列、ローカル枝は `BindLocalMatrix` |
| 書き戻し | `ToolContext.ShowBindPose` が `ActiveWorldMatrix` / `ActiveWorldMatrixInverse` / `ActiveVertexMatrix` を切替。頂点単位は `MeshContext.VertexMatrix(i, showBindPose)` |
| ギズモ中心 | `MoveToolHandler.UpdateGizmoState`。表示と同じ `VertexMatrix(i, showBindPose)` で集計する（2026-09-15 修正。以前は `LocalToWorld(i, …)` で現在ポーズ固定だった） |
| UI | 左ペインの `PlayerLayoutRoot.ShowBindPoseToggle`（「バインドポーズ表示」）。`VD_*` のグリッドには入れない（あちらはビューポート単位の表示ビットで永続化される） |
| 画面への反映 | **`EnterVerticesMoved` と `UpdateTransform()` の両方を呼ぶ**。下の 10.5 を参照 |

### 10.5 姿勢を変えたら画面へ 2 つ呼ぶ

`PresentAll` 経路は GPU の変換行列を push しない（`PlayerCommandDispatcher.BoneMorphUv.cs:310-317`
の既存注記）。行列表を作り直すのは `UnifiedSystemAdapter.UpdateTransform`
（`UnifiedSystemAdapter.cs:449-457`）だけで、そこへ届く経路は
`PlayerViewportManager.UpdateTransform()` しかない。したがって階層行列を変えたコマンドは

```
_viewportManager?.EnterVerticesMoved(project, VerticesMovedPhase.Dragging);  // 再描画準備
_viewportManager?.UpdateTransform();                                        // 行列表の作り直し
```

の対を呼ぶ。片方だけでは、`BonePoseData` も `WorldMatrix` も正しいのに
**画面はバインドのまま 1 ミリも動かない**。

2026-09-15 まで `setBonePoseValue` / `resetBonePoseLayers` / `bakePoseToBindPose` は
どちらも呼んでおらず、姿勢が画面に出ていなかった。`setPoseDisplayMode` は
`UpdateTransform()` だけを呼んでいた。4 か所とも対にそろえてある。

確認済み（2026-09-15・キャプチャ）：

- MQO（非スキンド・`roboF_T5_SK_debug.mqo`）… 全 256 オブジェクトへ `PositionY=3` を入れると
  モデルが画面外へ飛び、`showBindPose=true` で元位置へ戻り、`false` でまた飛ぶ。
  `resetBonePoseLayers` で読込直後と同一の絵に戻る。
- PMX（スキンド・`_0_ぜろちゃんN…`）… `左ひざ`（masterIndex 80）へ `RotationX=70` を入れると
  すねが曲がり、`showBindPose` の往復で曲げが消えて戻る。

確認済み（2026-09-15）：**バインド表示中の頂点移動は掴んだ位置と厳密に一致する**（10.2 前段の約束）。
以下は `verifyBindMove` による数値だけの確認で、**画面に出ているかは別に見ること**（10.5）。
`verifyBindMove` を MQO（非スキンド・`roboF_T5_SK_debug.mqo` + 原点 CSV・対象 `obj63あたま_old`）で
`delta=(7,0,0)` / `poseRotationZ=30` として実行し、

- `bindMaxError = 0`、`poseMaxError = 9.5e-07`（float の丸め）
- `legacyBindError = 3.5`（旧コードならこうなった値）＝ `discriminating = true` なので試験は差を検出できている
- `pass = true`

PMX（スキンド・対象 `顔肌+`、ポーズは `頭` ボーンへ 30°）も合格。
`posedVertices = 50 / testedVertices = 50`・`matrixMaxDiff = 0.84`・
`bindMaxError = 0` / `poseMaxError = 9.5e-07`・`legacyPoseError = 3.5`・
`poseDiscriminating = true` / `pass = true`。

比較対象は構造ごとに効く側が入れ替わる。旧コード（オブジェクト単位の `WorldMatrixInverse` 直）
との差は、非スキンドならバインド表示、スキンドなら現在ポーズ表示にしか出ない。

| | 非スキンド（MQO `obj63あたま_old`） | スキンド（PMX `顔肌+`） |
|---|---|---|
| legacyBindError | 3.5（効く） | 0（効かない） |
| legacyPoseError | 3.2e-07（効かない） | 3.5（効く） |

スキンド頂点はバインド表示で単位行列になるため旧コードの値も与えたデルタと一致し、
非スキンド頂点は現在ポーズ表示で正しい行列がメッシュ自身の `WorldMatrix` なので
旧コードと同じ値になる。`discriminating` はどちらか一方が立てばよい。

検査頂点は必ず「姿勢が動いた頂点」から選ぶこと。2 つの表示で行列が同じ頂点は
書き戻しが何であっても誤差 0 になり、そこを測って合ったと言ってはならない
（`verifyBindMove` は候補が 1 つも無ければ失敗させる）。

**この試験の限界。** 見ているのは `MeshObject.Vertices` の格納値と `MeshContext.VertexMatrix`
だけで、GPU が実際に描いた位置（`UnifiedBufferManager.GetWorldPositions`）とは突き合わせていない。
CPU 側の規則が一貫していることまでしか言えない。**画面に出ているかは別問題で、
実際 2026-09-15 まで出ていなかった（10.5）。数値で合否を書く前に必ずキャプチャを撮ること。**

## 10.6 前方向は GPU、逆方向だけ CPU

ローカル→ワールド（前方向）は **GPU が `_worldPositionBuffer` に出した値を読む**。
CPU で計算し直してはならない（`MeshContext.Transform.cs:436-455` の既存注記）。

| 向き | 使うもの |
|---|---|
| 前方向（表示位置） | `ToolContext.GetVertexWorldPosition(vi)` … 操作対象メッシュの 1 頂点<br>`ToolContext.GetMeshWorldPositions(ctxIdx)` … 指定メッシュの全頂点（一括）<br>実体は `PlayerViewportManager.TryGetVertexWorld` / `TryGetMeshWorldPositions`（`GpuSelect.cs:288-366`） |
| 逆方向（書き戻し） | `MeshContext.VertexMatrix(vi, showBindPose).inverse`。**GPU 側に逆行列は無い**ので、ここだけ CPU 行列を使う |

読み口は `GetDisplayPositions()` を読むだけで GPU 同期を伴わない。鮮度が要るときは
呼び出し前に `UpdateTransform()` を 1 回だけ呼ぶ。毎フレーム呼ばない。

配線は `PlayerViewportManager.WireGpuWorldReaders` の 1 か所に集約してある。
`GetCurrentToolContext` と `GetToolContextForCamera` の両方から呼ぶ。
ツール側で同じラムダを個別に代入しないこと。

### 10.6.1 `ctx.Project` は必ず埋める

`ToolContext.ShowBindPose` は `Project?.ShowBindPose ?? false`（`ToolContext.cs:488`）。
**`ctx.Project` を埋め忘れると常に false になり**、これを読む次の 3 つが
バインド表示に追従しなくなる。

- `ActiveWorldMatrix`（`:497`） / `ActiveWorldMatrixInverse`（`:507`） / `ActiveVertexMatrix(vi)`（`:551`）

2026-09-15 まで未配線で、面追加・新規頂点のウェイト継承・面押し出し・ナイフ・
点指定図形がバインド表示に追従していなかった。`WireGpuWorldReaders` で埋めている。

`GetCurrentToolContext` を経由しない `ToolContext` は `ToolManager.cs:70` と
`PlayerCommandDispatcher.Blend.cs:603`（`BuildMinimalToolCtx`）の 2 か所。
どちらも `Project` が入らないので、バインド表示に関わる変換を使うなら埋めること。

### 10.6.2 保険として残した CPU 経路

`RotateTool.CaptureStartWorld` / `ScaleTool.CaptureStartWorld` と、その周辺 3 か所に
「GPU 値が取れないときだけ `VertexMatrix(i, showBindPose)` に落ちる」分岐がある。
規則は GPU と同じで表示モードにも追従するが、**CPU 独自計算であることを承知のうえで
保険として置いている**（利用者の指示による）。外すと GPU 値が無い条件で無反応になる。
**新しいコードでこの分岐を真似しないこと。**

### 10.6.3 変形ツールのピボットはワールド保持

`RotateTool._pivot` / `ScaleTool._pivot` は 2026-09-15 から**ワールド座標**で持つ。
以前は基準メッシュのローカル座標で持ち、使うたびに `LocalToWorld` で戻していたが、
その往復に入るのはメッシュ 1 個の `WorldMatrix` で、スキンド頂点の表示位置とは別物だった。
`PivotWorld()` と `RotateToolHandler.WorldPivot()` / `ScaleToolHandler.WorldPivot()` は
素通しにしてある。`UseOriginPivot` は「基準メッシュのローカル原点をワールドへ直した点」。

確認済み（2026-09-15・`verifyBindRotate`）：

| 試験 | 対象 | bindMaxError | poseMaxError | pass |
|---|---|---|---|---|
| 回転 | PMX スキンド `顔肌+`（ポーズは `頭`） | 1.8e-07 | 2.6e-07 | true |
| 回転 | MQO 非スキンド `obj63あたま_old` | 1.8e-07 | 2.0e-07 | true |

修正前のスキンド・現在ポーズ表示は `poseMaxError = 0.0214`（試験回転 X 35°）だった。
`RotateTool` が `meshContext.LocalToWorld` でメッシュ 1 個の行列を使っていたため。

## 11. 頂点を直接書いたら位置キャッシュを捨てる

`MeshObject.Positions` はキャッシュで、`Vertices[i].Position` を直接書いたら
`InvalidatePositionCache()` を呼ばなければならない（`MeshObject.cs:118`）。

`SimpleMoveTransform.SetTotalDelta` / `MagnetMoveTransform.SetTotalDelta` はこれを呼んでおらず、
`MoveToolHandler.BeginMove` が開始スナップショットに使う `MeshObject.Positions.Clone()` が
古いままだった。結果、**2 回目の移動が 1 回目より前の位置を開始値に取り、1 回目の結果を消していた**
（2026-09-15 修正。`VertexTransforms.cs` の両 `SetTotalDelta` の末尾で無効化する）。

同じ形の他ツールは元から呼んでいる：`RotateToolHandler.cs:376` / `ScaleToolHandler.cs:360` /
`SculptToolHandler.cs:222` / `ObjectMoveTool.Apply.cs:138, 405`。
新しく頂点を直接書くコードを足すときは、この節にならうこと。


