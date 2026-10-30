# PolyLing 作り直し整備計画（オブジェクトグループのモディファイア化）

オブジェクトグループを拡張し、Blender のモディファイアに相当する仕組みに格上げする。途中の条件（入力の形・パラメータ）を変えても、完成形まで自動で作り直せるようにする。この文書を正本にする。

進め方：1 段＝1 会話。各段の始めにコードを読み直して file:line 付きの変更計画を出し、承認を得てから実装する。終えたら「状態」を更新する。

作成: 2026-10-28（同日、初版を全面改訂）。

## 1. 目標

- 生成物（パイプ・フリル・藤壺・揺れ鎖・魔法陣など）は、入力と引数から自動で導かれる「出力」になる
- 入力を編集すると、依存の順に下流の出力まで作り直される（梯子 → 揺れ鎖 → パイプのような鎖も）
- 引数をグループ上で直接変えられる
- 逆方向を持つ生成は、出力を編集すると入力へ書き戻せる（パイプ → 梯子、藤壺 → 梯子など）
- シナリオは「手順の知識」として今のまま残し、モディファイアとは役割を分ける

## 2. 基本の仕組み

### 2.1 正本：オブジェクトグループのまま

保存・転送・Undo はグループの既存の仕組みに乗っている。項目 1 つ＝モディファイア 1 つとみなす（Action・Args・入力 MeshRefIds・出力 OutputObjectIds）。項目に足すのは「何を読むか（形・ウェイト・姿勢）」「ドラッグ中にも追随するか」「逆方向の action と引数」。入力の署名などの計算の控えは保存しない（SubdivisionSync がステンシルを保存しないのと同じ）。

### 2.2 依存：グループから毎回導く

全グループの項目から「入力オブジェクト → 項目 → 出力オブジェクト」を導く。グループの増減・変更のときだけ作り直す（毎フレームではない）。依存の順に並べ、循環は登録を拒否する。組立（魔法陣・鉄塔）の部品も項目なので同じグラフに入る。

### 2.3 きっかけ：サブディビジョンと同じ 2 か所

手本は `SubdivisionSync.cs`（つながりの正本がグループ、実行時に追随）。
- GPU バッファを作り直す入口（`MeshSceneRenderer.RebuildAdapter` の先頭）。面の追加・削除・Undo・読み込みはここを通る（`SubdivisionSync.cs` 冒頭の注記）
- 頂点ドラッグ中の位置の同期（`PlayerViewportManager.SyncMeshPositionsAndTransform`）
どちらでも、グラフに入力として載っているオブジェクトだけ署名を取り直し、違う項目を要評価にする。

### 2.4 評価：今の作り直しをそのまま使う

要評価の項目と下流を依存の順に流す。1 項目は今の `RunObjectGroupStep`（出力先へ中身だけ書き戻す）。重い項目はドラッグを離したときだけ流す。評価結果は Undo に積まない（入力の Undo で入力が戻れば、署名の変化で出力も作り直される）。

### 2.5 保護：出力は書けない（逆方向があれば書き戻す）

- 出力を書き換えるコマンドは受け口で拒否する（コマンドの書き込み宣言 `PLMeshRefAccess.Write`・`PLWriteScope` を使う）
- 逆方向を持つ項目は出力の編集を許し、確定時に「逆方向で入力を書き直す → 正方向で出力を作り直す → 下流を評価」を 1 つの処理で流す。逆方向は確定時だけ動く取り込みで、依存グラフには入れない（循環させない）
- 逆方向ごとに「保つもの・失うもの」を宣言し、確定の前に知らせる
- 「適用」＝グループ解除（出力が普通のメッシュになる）

## 3. 棚卸し（2026-10-29 確定。照会コマンド `queryModifierInventory` の結果）

照会は `ModifierInventory.cs`（型と属性だけを見る）。モデルを開かずに流せる。コマンドを足したら流し直す。
- 対象：Category が `geometry.create` で始まるもの、または `[PLModifier]` を持つもの（抽象は除く）。計 66 件
- 記録・逆方向：`PLModifierAttribute`（受け口のコードを根拠に型へ宣言。型からは導けない）
  - OnKeepAsGroup：「グループとして残す」が立っているときだけ自身を項目にする
  - Always：常に自身を項目にする
  - ViaSteps：自身は残らず、中で流した別のコマンドを項目にする
  - Assembly：組立
  - None：記録しない
- 書き戻し：`PanelCommandFactory.RebuildKeys`（`RebuildRole` の TargetIndex のキー。無ければ不可）
- 読む入力：`MeshRefKeys` から出力先（TargetIndex・`placement.*`）を除いたもの。`ProfileSourceIndex` はプロファイル（`ProfileKeys`）を持つときだけ数える
- 型：C＝Assembly、B＝読む入力かプロファイルがある、A＝それ以外

集計：A 43・B 21・C 2。記録されるもの 58、書き戻せるもの 55、逆方向を持つもの 2（パイプとその逆）。

### 3.1 型

| 型 | 中身 | コマンド |
| --- | --- | --- |
| A. 単発 | 入力を持たず引数だけで作る | 図形生成 40（基本図形・歯車・機械部品・文字・紋章・星座・リング・能面・画像の板・髪の房・スプライン・リボン・鉄塔の塔体 `createLatticeTower`）。記録は OnKeepAsGroup、書き戻しは `placement.addTargetIndex`。ほかに `addGeneratedMesh`（記録 None・書き戻し可）、`addMeshFromArrays`・`createHoleBridge`（記録 None・書き戻し不可） |
| B. 入力あり | 他のオブジェクトを読んで作る。鎖の辺になる | 下の 3.2 |
| C. 組立 | 空の親の下に部品を項目として並べる | `createMagicCircle`・`createTowerAssembly`。どちらも `ExecuteEmptyParentAssembly`。自身は書き戻し不可（部品の項目が書き戻す） |

`createHoleBridge` は穴の索引 `MeshA`・`MeshB` に `IsMeshRef` が付いていないため A に出る（`PanelCommand.PrimitiveBelt.cs` の `CreateHoleBridgeCommand`）。実際は 2 つのメッシュを読む。

### 3.2 B 型（入力あり）

| 生成 | action | 読む入力（照会の出力） | 記録 | 書き戻し | 逆方向 |
| --- | --- | --- | --- | --- | --- |
| フリル | `createFrill` | `beltSourceIndex`・`profileSourceIndex`（Read）、プロファイル `profileA` | OnKeepAsGroup | 可 | なし |
| パイプ | `createPipe` | `beltSourceIndex`・`profileSourceIndex`（Read）、プロファイル `profile` | OnKeepAsGroup | 可 | → `extractPipeBelt` |
| パイプの梯子化 | `extractPipeBelt` | `sourceMasterIndices`（Read） | OnKeepAsGroup | 可 | ← `createPipe`（パイプの逆方向そのもの） |
| 藤壺 | `createPlaceObject` | `sourceMasterIndices`・`beltSourceIndex`（Read） | OnKeepAsGroup | 可 | なし（段 6 で配置系の共通の核として作る。3.4） |
| 頂点藤壺 | `createVertexBillboardPlace` | `masterIndices`（Write）、`sourceMasterIndices`（Read） | OnKeepAsGroup | 可 | なし（同上） |
| 円周配置 | `createRadialPlace` | `sourceMasterIndices`（Read） | OnKeepAsGroup | 可 | なし（同上） |
| ネジ配置 | `createBoltPattern` | `sourceMasterIndices`（Read） | OnKeepAsGroup | 可 | なし（同上） |
| 鉄塔のデッキ | `createTowerDeck` | `sourceMasterIndices`（Read） | OnKeepAsGroup | 可 | なし（同上。部品の区別は別の方法が要る。パーツ ID 0 で置く） |
| デッキの後付け | `addTowerDeck` | `towerIndex`・`sourceMasterIndices`（Read） | ViaSteps（`createTowerDeck` を鉄塔のグループへ足す） | 不可 | なし |
| 辺の帯 | `edgeRibbonFace` | `masterIndices`（Write。辺を読む） | OnKeepAsGroup | 可 | なし |
| 辺パイプ | `createEdgePipe` | `masterIndices`（Write。辺を読む） | ViaSteps（`edgeRibbonFace`＋`createPipe` を 1 グループ） | 可（自身は項目にならない） | なし（項目の `createPipe` は → `extractPipeBelt`） |
| 回転体 | `createRevolution` | `profileSourceIndex`（Read）、プロファイル `params.profile` | OnKeepAsGroup | 可 | なし（作業空間の反映・取消しは入力側の線分 ↔ 断面で、出力からの逆方向ではない） |
| 2D押し出し | `createProfile2D` | `profileSourceIndex`（Read）、プロファイル `params.loopPointValues` | OnKeepAsGroup | 可 | なし（同上） |
| サブディビジョン | `subdivideMesh` | `cageMasterIndex`（Read） | Always | 可（`targetMasterIndex`） | なし |
| ブレンド | `applyBlend` | `sourceMasterIndices`（Read）、`destMasterIndex`（Write） | OnKeepAsGroup | **不可**（`RebuildRole` が無い。出力を持つ項目は作り直しで「対応していません」で止まる） | なし |
| 揺れ鎖 | `placeSpringBoneLadderChains` | `sourceMasterIndex`（Write。梯子へウェイトを塗る）、`attachMasterIndex`（Read） | OnKeepAsGroup | 可（`chainRootMasterIndices`） | なし |
| 歪み複製 | `createObjectArray` | `sourceMasterIndices`（Read）、`params.targetMasterIndex`（Write） | **None** | 不可 | なし |
| 点指定図形 | `createPointDefinedPrimitive` | `masterIndices`（Write。既存の頂点を読む） | **None** | 不可 | なし |
| 辺の橋 | `createEdgeBridge` | `meshIndex`（Write） | None | 不可 | なし |
| 梯子の取り込み | `acquireBeltStrips` | `masterIndex`（Read） | None | 不可 | なし |
| 断面の作業空間を開く | `openProfileEditSpace` | `masterIndex`（Write） | None | 不可 | なし（カテゴリ `geometry.create.profile` のため対象に入る。生成物ではない） |

逆方向のコマンドが実在するのはパイプ（`extractPipeBelt`）だけ。ほかの「なし」は、逆方向を担うコマンドがコード上に無いという意味。
（段 6 で、パイプ・藤壺・頂点藤壺・ネジ配置・円周配置・鉄塔のデッキに出力の編集を入力へ戻す方法を宣言した。表の「逆方向」列は段 0 時点のまま。今の宣言は `queryModifierInventory` で見る）
（段 14 のあとの `queryModifierInventory`：生成コマンド 69・A 44・B 23・C 2・記録される 60・書き戻せる 58・逆方向あり 10。段 7 でまつげ、段 14 で辺の帯・パイプの梯子化・貼り付けの逆方向を足した）

### 3.3 鎖（コードで確認したもの）

| 鎖 | 段の並び | 根拠 |
| --- | --- | --- |
| 揺れフリル | 梯子 → 揺れ鎖（梯子へウェイト）→ フリル（ウェイトを位置で引き継ぐ）→ スキンド化 → 自動更新で塗り直し | `PlayerSpringSkinScenarioSubPanel`、`BeltWeightBinder`（`PrimitiveMeshFactory.cs` の生成時の引き継ぎ） |
| 揺れパイプ | 梯子＋断面 → 揺れ鎖 → パイプ → スキンド化 → 塗り直し | `PlayerSpringSkinPipeScenarioSubPanel.cs` 冒頭 |
| パイプの梯子化 | パイプ → 梯子 → 再生成 | `ExtractPipeBeltCommand`（原型 NCSHAGLIB の HairToBelt） |
| 辺パイプ | 辺 → 帯 → パイプ | 辺パイプのコマンド |
| 藤壺・頂点藤壺 | 梯子／頂点＋配置元 → 配置 | 上表 |
| 断面系 | 断面の線分 → 回転体／2D押し出し／フリル・パイプの断面 | `PlayerPrimitiveMeshSubPanel.EditSpace.cs` |
| サブディビジョン | ケージ → 滑らかな子 | `SubdivisionSync.cs` |
| 組立 | 部品（A・B）→ 空の親の子。円周配置・デッキは組立の外とも依存を持つ | `PlayerCommandDispatcher.MagicCircle.cs`・`.LatticeTower.cs` |

### 3.4 配置系の共通の核

`PlaceObjectMeshGenerator.AppendInstance` を藤壺・頂点藤壺（`VertexBillboardPlaceOps`）・ネジ配置（`BoltPatternMeshGenerator`）・円周配置（`MagicCircleMeshGenerators`）・鉄塔のデッキ（`LatticeTowerMeshGenerator`）が共有する。部品ごとの姿勢の推定は藤壺の整形 `PlaceObjectReshapeOps`（パーツ ID ごとにアフィン／薄板スプライン）にある。逆方向の核＝「部品ごとの姿勢の推定 → 相似変換への分解」を共通部品にし、種類ごとに「枠 → 入力の引数」の戻し方だけを持たせる。

藤壺の逆方向（部品 → 梯子）：中点＝推定位置、段の向き＝推定 X 軸、段の長さ＝倍率 ÷ Scale（RungLength）、左右＝中点 ± X×長さ/2。置いていない段（間引き・長さ 0）は元の値か補間。失うもの：部品の歪み、段の向きまわりの傾き（順方向の Z は梯子の面から決まる）。

### 3.5 NCSHAGLIB からの移植対象（PolyLing に未実装）

`G:\N3G_2023_0926_当面保存\N3GN\N3GN\N3GN5\Unityプロジェクト外\Lib\NCSHAGLIB\IEditablePMX\EditablePMX\Tools\`
- まつげ等（`EPMXToolFor_LushMaker.cs`）：梯子の Z を 0 → 参照面へ貼り付け（`_PasteFlatParts`）→ レールをスプラインで補間し段を増やす（`GetSplineBeltPair`）→ 段ごとの基準三角形（底辺＝段、頂点＝次の段の中点）→ 回転を始点から終点へ線形補間（`RotationsGetMatrixBsub`）→ 原型を枠に置く（`CreateAndAddVertex1`、倍率＝段の長さ×10、位置＝中点か端、逆順可、頂点 ID 連番）。配置系の変種として、段の補間・枠の決め方・回転の補間・置く位置を引数にする
- 眉・まぶた（`EPMXToolFor_EyebrowMaker.cs`）：梯子の Z を 0 → パイプ（`CreatePipe_`）→ 参照面へ貼り付け
- 平面化＋貼り付けは独立したモディファイア（梯子＋参照面 → 貼り付けた梯子）として切り出す
- まつげの逆方向は配置系の核に乗るが、補間した段から元の梯子へは当てはめ（間引き）、回転は始点・終点の 2 値へ最小二乗で当てはめる近似になる

## 4. 難所

1. 段の順序：パイプ・フリルは生成時に梯子のウェイトを位置で引き継ぐので、揺れ鎖（梯子へ塗る）→ パイプ・フリルの順が要る
2. スキンド化後の評価空間：出力をスキンド化した後に作り直すと MeshFilter 前提の空間で作り頂点を壊す（`ObjectGroup.cs` の AutoUpdate の注記）。揺れものの鎖は必ずスキンド化を通る
3. 入力の持ち方：梯子と断面は作り直し時に元から取り直し、元が無ければ控えた点列を使う（`ObjectGroupOps.LastBeltMessage`・`LastProfileMessage`）。常に「つながり」を正本にする
4. 記録の漏れ・書き戻しの漏れ（照会で確定）
   - 歪み複製・点指定図形はグループに記録されない（None）
   - ブレンドは記録されるが、書き戻しの印（`RebuildRole`）が無いので作り直せない
   - `createHoleBridge` は索引に `IsMeshRef` が無い
5. 変更検知：今のダイジェストは頂点 ID・数・位置だけ（`ObjectGroupOps.ComputeSourceDigest`）
6. 下流への伝播が無い：自動更新はスキンド化の 2 経路からだけ（`PlayerCommandDispatcher.DeformSkin.cs`）

## 5. 段

### 段 0：棚卸しの確定 — 状態：済（2026-10-29。`queryModifierInventory` の結果で 3 章を確定）

- 生成コマンドを全件、A・B・C に分類し、B は入力と逆方向の有無を一覧にする照会コマンドを作る（グループに記録されるか・書き戻せるか・読む入力・逆方向）
- 3 章の「未確認」を埋める

### 段 1：評価器の一般化 — 状態：済（2026-10-29）

- `SubdivisionSync` をグループ共通の評価器にする（きっかけ 2 か所・署名・ドラッグ中に追随するかの印）
- サブディビジョンを載せ替え、挙動が変わらないことを確かめる
- 確かめること：数値移動などドラッグ以外の位置変更が 2 か所のどちらかを通るか

#### 実装

- `Core/Ops/ModifierEvaluator.cs`（新規）：グループ共通の評価器
  - `CollectEntries`：全グループの実行項目から、型・`FollowsDrag`・入力（`ModifierInventory.InputKeys` のキーの ObjectId を引いたもの）・出力を集める
  - 入力ごとの署名（`MeshSignature.Topology`・`Positions`）の控え。キーは出力の先頭の描画オブジェクト、出力が無い項目は項目そのもの。保存しない
  - `RefreshModel`（きっかけ 1、`MeshSceneRenderer.RebuildAdapter` の先頭）：署名が違う項目を要評価にする。処理器のある項目は毎回呼ぶ。処理器の無い項目は要評価の印を立てるだけで流さない
  - `SyncFromInput`（きっかけ 2、`PlayerViewportManager.SyncMeshPositionsAndTransform`）：動かした描画オブジェクトを入力に持つ項目のうち、`FollowsDrag` のものは処理器の `SyncDrag` を呼び、それ以外は要評価の印だけ立てる
  - `IModifierHandler`（action ごとの処理器）・`Register`・`Prime`・`Forget`・`IsDirty`
- `Core/Ops/MeshSignature.cs`（新規）：`Topology`（`SubdivisionOps.TopologySignature` の本体を移した。線分を含めるかを引数で選ぶ）・`Positions`（`WorkingPositions` 込み、1e-5 刻み）
- `SubdivisionSync.cs`：subdivideMesh の処理器にした。外部 API（`Link`・`CollectLinks`・`FindByCage`・`FindByChild`・`Prime`・`Forget`）は名前と引数を変えていない。作り直すかの判断は従来どおり `SubdivisionOps.TopologySignature`（線分を混ぜない）で行う
- `PLModifierAttribute.FollowsDrag`（新規）。`SubdivideMeshCommand` だけ true
- `ModifierInventory.InputKeys`（読む入力の規則を切り出し、棚卸しと評価器で共有）。一覧に「ドラッグ追随」の列を足した
- 触っていないもの：`ComputeSourceDigest`・`RunObjectGroupStep`・`RunAutoUpdateGroups`。Player 層の作り直しは段 4 で処理器として登録して流す

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage1/`）

サブディビジョン（直方体のケージ 168 頂点、回数 2、子 2786 頂点）
- 作成：子ができ、角が丸まる
- 頂点の移動：`setVertexPositions`（きっかけ 1 を通る）・`moveSelectedVertices`（`MoveToolHandler.ApplyDelta` → `OnSyncMeshPositions` で、マウスのドラッグと同じきっかけ 2 を通る。確定の `DragEnd` は `RebuildAdapter` を通らない）とも子が追随
- 面の削除（`deleteFaces` 4 面）：子が作り直され境界ループ 1。Undo で 2786 頂点・境界 0 に戻る
- 面の追加（`addFace` で浮いた四角形）：子に細分した面が足される。Undo で戻る
- 頂点移動の Undo（`PolyLingPlayerViewerCore.Lifecycle.cs:212-224` が `SyncMeshPositionsAndTransform` を呼ぶ）：子が戻る
- 読み込み（`saveProjectCsv` → `loadProjectCsv`）：子の形が保たれ、読み込み後の頂点移動（きっかけ 1・2 とも）にも追随
- `scaleSelection`・`rotateSelection`・`alignVertices`・`scaleObjectGeometry`・`sculptStroke`：実行直後の子の位置と、`notifyListStructureChanged` で作り直させた後の子の位置が一致（どちらかのきっかけを通っている）
- Editor.log にエラーなし

2 か所のどちらも通らない経路
- カレントでないモデルへの書き込み。画面の入口が `project.CurrentModel` しか作り直さない（`PlayerViewportManager.Render.cs:164-171` の `EnterTopologyChanged`、`:471` のドラッグ中の同期）
  - `setRawData`（`PlayerCommandDispatcher.Query.cs:547` で `c.ModelIndex` のモデルへ書き、`:582` は `EnterTopologyChanged`）：カレントでないモデル 1 のケージへ書くと子は古いまま。`switchModel` で 1 をカレントにすると（`Query.cs:603` の `EnterSceneReset` → `RebuildAdapter`）追随した（実測）
  - `transferVertexData`（`PlayerCommandDispatcher.NormalEdit.cs:315-340`）：転送先がカレントでないときは画面の入口を呼ばない
- 頂点の位置ではなく姿勢（行列）だけを変える経路。`EnterVerticesMoved(Dragging)`（`syncMc` なし）／`DragEnd` と `UpdateTransform` を通り、2 か所のどちらも通らない。ローカルの頂点位置は変わらないので、今の署名（ローカル位置）でも変化にならない。ワールドで入力を読む項目では段 5 で扱う
  - ポーズ層・バインドへの焼き込み・BoneTransform：`PlayerCommandDispatcher.BoneMorphUv.cs:126,194,213,469`
  - 表示の姿勢切替・ビルボード：`BoneMorphUv.cs:73`、`PlayerCommandDispatcher.MeshAttributes.cs:463`、`PolyLingPlayerViewerCore.EditSpace.cs:528`
  - オブジェクト移動・原点移動のドラッグ：`PolyLingPlayerViewerCore.VertexInteraction.cs:207,260`
  - Undo（ポーズ・BoneTransform・原点）：`PolyLingPlayerViewerCore.Lifecycle.cs:156`
  - モーションの当て込み：`PolyLingPlayerViewerCore.CreateCommands.FileIO.cs:685,760,825`、`PolyLingPlayerViewerCore.Layout.TestPanels.cs:51,337,348`

### 段 2：出力の保護と「適用」 — 状態：済（2026-10-29）

- 出力を書くコマンドを受け口で拒否する。`ModelWide`・未宣言のコマンドの扱いを決める

#### 決めた扱い（`Core/Ops/ModifierOutputGuard.cs`）

- 判定の場所：`PlayerCommandDispatcher.Dispatch` の担当者判定の直後で、各深さで判定する。拒否したら `OnOwnershipDenied` を呼び、`CommandErrorCodes.ModifierOutput` で失敗を返す。理由には「オブジェクト名・グループ名・項目番号・action」を出す
- 判定しないもの
  - `Writes` が None・AddOnly のもの
  - 分類がメッシュの中身を書かない族のもの：`undo`・`object.*`・`selection(.*)`・`material`・`vrm`・`ui`・`workaxis`・`morph`・`dynamics.spring`・`rig.skeleton(.*)`・`camera`・`query`・`mcp`・`scenario(.*)`・`io.project`・`io.export`・`remote`・`animation`・`tool`・`underlay`・`external.*`
  - 未知の分類・分類なしは判定する
  - 姿勢の焼き込み（`rig.skeleton.pose`）は段 5 で扱う
- 書き込み先の決め方
  - Targets：Write 引数（WriteWhen が成り立つもの）の値で 0 以上のものを書き込み先にする。値が -1 または空のときは、実行時の選択中の描画オブジェクト（無ければ編集対象）を書き込み先にする。成り立つ Write 引数が 1 つも無いもの（新規に作るモードの生成など）は判定しない。担当者判定と同じ扱い
  - ModelWide・Unspecified：Write 引数で 0 以上の値が決まればそれだけを見る（`setRawData` の masterIndex など）。決まらなければ、実行時の選択中の描画オブジェクト（無ければ編集対象）を見る
- 拒否する条件：書き込み先が、どれかのグループの実行項目の出力（`ModifierEvaluator.TryFindOutputOwner`）であるとき。逆方向を持つ項目（パイプ）も拒否する。書き戻しは段 6
- 拒否しないもの
  - 作り直しの書き戻し：`RunObjectGroupStep` の入れ子の `Dispatch` を `_modifierWriteBackDepth` で挟み、その間は判定しない。作り直し・自動更新・鉄塔のデッキ差し替えが通る
  - 評価器（処理器）：Dispatch を通らないので掛からない
  - Undo：分類 `undo` なので判定しない
- 「適用」＝`deleteObjectGroup`
  - 処理器に `IModifierHandler.Release` を足した。`ModifierEvaluator.ReleaseGroup` が項目ごとに Release を呼び、控えを捨てる
  - サブディビジョンの Release は `releaseSubdivision`（子を残す）と同じ後始末をする：控えを捨てる・ケージの面の塗りを戻す・子のロックを外す
  - 処理器を呼んだときは、Undo を一覧のスナップショットとグループ削除の 1 件で積む。Undo するとグループ・ロック・保護が戻る

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage2/`）

- サブディビジョン（直方体のケージ、回数 2）の子への書き込みは、次のどれも理由付きで拒否された
  - `setVertexPositions`
  - `moveSelectedVertices`
  - `deleteFaces`
  - `setRawData`（scope=Object、および子を選んで scope=SelectedObjects）
- ケージへの `setVertexPositions` は通り、子が追随した（01→02、04）
- パイプ（`createPipe`、グループとして残す）の出力への `setVertexPositions`・`deleteFaces`・`setRawData` は拒否された
- `rebuildObjectGroup`（サブディビジョン・パイプ）と `undo` は通った。パイプの作り直しは出力へ ReplaceExisting で書き戻す経路
- 適用
  - サブディビジョンのグループに `deleteObjectGroup` を掛けると、子への `deleteFaces` が通った
  - ケージの面が塗られ、ケージを動かしても子は追随しなくなった（05・06）
  - パイプも適用後は `setVertexPositions` が通った（08）
- 適用の Undo
  - 拒否が戻り、ケージの編集に子が追随した（07）
  - パイプも Undo 後は再び拒否された
- 最初の版では、Targets で成り立つ Write 引数が無いもの（`createPipe` の NewObject）を選択で判定し、子を選んだまま新規のパイプを作れなかった。担当者判定と同じく「判定しない」に直した
- Editor.log にエラーなし

### 段 3：引数の書き換え — 状態：済（2026-10-29）

- 項目の引数を書くコマンドとパネル。書いたら要評価。組立の部品の引数変更でその子だけ作り直されること

#### 実装

- `setObjectGroupStepArgs`（`SetObjectGroupStepArgsCommand`、`PanelCommand.MeshList.cs`）：グループ名・項目番号・キーの並び・値の並びで書く
  - 受け口は `PlayerCommandDispatcher.ObjectGroup.cs` の `ExecuteSetGroupStepArgs`。手本は `ExecuteReplaceGroupSource`（鉄塔のデッキ差し替え）
  - 流れ：検査 → 書く → `SuspendRecording` 中に `RunObjectGroupStep`（その項目だけ）→ `SourceDigest` を取り直す → `RecordSubdivisionUndo`
  - Undo は一覧のスナップショットとグループの差し替え（`ObjectGroupChangeRecord.ReplacedIndex`）の 1 件
  - 作り直しに失敗したら引数を元へ戻して失敗を返す
- `queryObjectGroupStepArgs`（`QueryObjectGroupStepArgsCommand`、`PanelCommand.Query.cs`）
  - 返すもの：項目のキー（キー順）・今の値・書けるか（1/0）・書けない理由・出力の ObjectId
- `ObjectGroupOps.cs`（`BuildCommand` の後ろ）
  - `ArgWriteBlockReason(Type, key)`：書けないキーなら理由
  - `StepArgKeys`：項目が受け付けるキー（組み直したコマンドの `ToArgs` のキー ∪ 今の Args のキー）
  - `TryValidateStepArgs`：書く前の検査。グループの写しに書き、`BuildCommand`（中で `PanelCommandFactory.Create`）が通るかを見る
  - コマンド・照会・パネルが同じ判定を使う
- パネル：既存の `PlayerObjectGroupSubPanel` に「項目の引数」欄を足した
  - 部品：項目・キー・値・「書いて作り直す」ボタン
  - UI 自動操作の ID：`objectGroup.argStep` / `argKey` / `argValue` / `argWrite`
  - キーの選択肢は書けるものだけ。選ぶと今の値が入る
  - 表示用に `ObjectGroupStepView`（`ViewInterfaces.cs`）を足し、`ObjectGroupView.Steps` に詰めた（`LiveViews.BuildObjectGroupViews`）
- 触っていないもの
  - `ModifierEvaluator`（要評価の印）
  - 依存の順・下流への伝播（段 4）

#### 決めた扱い

- 書けないキー（`ObjectGroupOps.ArgWriteBlockReason`）
  - 入力と出力先：`MeshRefKeys` のキーとその `ModelKey`、`RebuildKeys.TargetIndexKey`
  - 作り直しが上書きするもの：`RebuildKeys.ModeKey`（`placement.addMode` など）、末尾が `keepAsGroup` のキー
  - 入力の中身と取り方（入力の差し替えは段 3 の範囲外）
    - 梯子の点列・先端：`beltLeftPoints`・`beltRightPoints`・`beltStarts`・`beltClosed`・`beltFlipWinding`・`beltHeightScale`・`beltStartPoints`・`beltHasStartPoint`・`beltEndPoints`・`beltHasEndPoint`
    - 取り込み方：`acquireMethod`・`acquireCrossRows`・`acquireSetName`・`profileAcquire`
    - プロファイル：`ProfileKeys` のキーとループ開始・穴のキー
- 拒否するもの（どれもグループに何も書かない）
  - 項目に無いキー
  - 同じキーの重複
  - キーと値の数の不一致
  - 実行しない項目（説明・指示・確認）
- 型の検査は `PanelCommandFactory.Create` に任せる
  - bool の値は `IsTrue`（"true" か "1" 以外は false）で読むため、型では拒否できない
  - 範囲外の値（サブディビジョンの回数など）は受け口の側で丸める
- `string[]` で受けるので、カンマを含む値（配列など）は二重引用符で包む
- 書いた直後に作り直すので、要評価の印は立てない
- 分類は `object.group`。出力の保護（段 2）の判定外で、書き戻しは `_modifierWriteBackDepth` の中を通る

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage3/`）

- サブディビジョン（直方体のケージ 168 頂点）
  - `level` を 1 → 3 に書くと、子が 698 → 11138 頂点に作り直された。ObjectId は変わらない（01→02）
  - Undo 1 回で `level`=1・698 頂点に戻った（03）。Redo で 3・11138 に戻った
- 拒否
  - `cageMasterIndex`：MeshRef として拒否
  - `levl`：項目に無いキーとして拒否
  - `level`=abc：型で読み直せないとして拒否
  - どれもグループの値・modelRevision は変わらない
- パイプ（`createPipe`、Baked、グループとして残す）
  - `params.thickness`=0.1 と `params.segmentsFront`=2 をまとめて書くと、16 → 32 頂点に作り直された。ObjectId は変わらない（04→07）
  - Undo で 16 頂点に戻った（06）
  - 書けるキーと書けないキーを混ぜた書き込み（`params.thickness`＋`placement.addTargetIndex`）は全体を拒否した
  - `params.thickness`=thick は型で拒否した。どちらも頂点数・modelRevision は変わらない
- 段 2 の保護
  - 書き換え後のサブディビジョンの子への `deleteFaces` は拒否された
  - 書き換え後のパイプの出力への `setVertexPositions` は拒否された
- 魔法陣（`createMagicCircle`、紋章 2 部品）
  - 項目 1 に `params.ring`=true・`params.size`=0.7 を書くと、部品 2 だけが 72 → 360 頂点に作り直された
  - 部品 1 は 80 頂点のまま（08→09）
- パネル（UI 自動操作で項目・キー・値を選んでボタンを押した）
  - パイプの `params.thickness`=0.25 が書かれ、出力が作り直された（10）
  - サブディビジョンの `level`=2 で子が 2786 頂点になり、Undo で 11138 に戻った（11）
- 検査
  - `queryCommandAudit` の完了条件の項目はすべて 0
  - `queryUiAutomationAudit` の登録失敗・属性の付け忘れは 0。足した 4 つは登録済み
  - Editor.log にエラーなし

### 段 4：依存の順の評価 — 状態：済（2026-10-29）

- 鎖の順（梯子 → 揺れ鎖 → パイプ・フリル）、組立の内外をまたぐ依存（例：フリルを直す → それを円周配置した魔法陣の部品が追随）

#### 実装

- `Core/Ops/ModifierGraph.cs`（新規）：依存のグラフ
  - 辺：A の出力を B が入力に持つ／A が Write で持つ入力を B が読む（両方 Write なら前の項目から）
  - Kahn 法で並べる。同順位はグループ順・項目順。並べられずに残ったものは循環
  - 状態：Handler（処理器）・Evaluate（評価器が流す）・Excluded（理由付き）・Cycle
  - `CycleReason`：登録の前の循環の判定
  - `StructureKey`：グループ・項目・入出力の ObjectId と、引けた描画オブジェクトの同一性・スキンド化の有無から作る。頂点は見ない
- `ModifierEvaluator.cs`
  - `GetGraph`：構造キーが変わったときだけグラフを作り直す。`RefreshModel`・`SyncFromInput` はグラフの順で回す
  - 控えのキーを出力の先頭の ObjectId に変え、モデルごとに持つ（一覧のスナップショットの Undo で描画オブジェクトが複製に置き換わっても効く）
  - `EvaluatePending`：要評価の Evaluate の項目を依存の順に流す。作り直しの口は引数（Player 層が渡す）
  - `MarkRebuilt`：コマンドが明示的に作り直した項目の署名を控え、下流に印を立てる
  - `MarkChangedInputs`：Undo・Redo の後に署名を取り直して印を立てる
  - `PrimeAll`：全項目の署名を取り直して印を下ろす（スキンド化の後）
  - 最後の評価の結果（Ok・Failed・Blocked・Rebuilt と理由）と、最後の流しの記録を持つ
- `PlayerCommandDispatcher.ModifierEval.cs`（新規）：`RunModifierEvaluation`（記録を止めて `RunObjectGroupStep` で流す。成功後に `SourceDigest` を取り直し、`LastBeltMessage`・`LastProfileMessage` を結果に控える）、`EvaluateModifiersAfterDrag`、照会の組み立て
- 流す契機
  - 一番外側のコマンドの終わり（`PlayerCommandDispatcher.DispatchAuthorized`）。書き込みの無いコマンドと頂点ドラッグ中は流さない。Undo・Redo のときは先に `MarkChangedInputs`
  - 頂点ドラッグを離したとき（`PlayerViewportManager.EnterVerticesMoved` の DragEnd → `OnVerticesDragEnded`）。`IsVerticesDragging` を足した
- 伝播：`ExecuteSetGroupStepArgs`・`RebuildObjectGroupCommand`・`ExecuteReplaceGroupSource` の成功後に `MarkRebuilt`
- 循環の拒否：`CaptureObjectGroup`（グループに登録せず失敗を返す）、`ExecuteReplaceGroupSource`（元へ戻して拒否）
- 自動更新：スキンド化の 2 経路で `RunAutoUpdateGroups` の後に `PrimeAll`
- `PrimitiveMeshReplaceExisting`：記録を止めているときは Undo の書き込み先を設定しない
- 照会 `queryModifierGraph`（`QueryModifierGraphCommand`）
- `ObjectGroup.AutoUpdate` と `RunAutoUpdateGroups` の注記を改めた

#### 決めた扱い

- 処理器の無い項目は、きっかけ（`RebuildAdapter`・位置の同期）の中では流さない。書き戻しが `EnterSceneReset` → `RebuildAdapter` を呼ぶので再入になる
- 評価器は `AutoUpdate` の値を見ずに流す。`AutoUpdate` は「スキンド化の直後に塗り直しで流すか」だけの意味にした。スキンド化の 2 経路からの `RunAutoUpdateGroups` は残す（段 5 の領分）
- 外す項目（Excluded）。処理器の項目（サブディビジョン）には掛けない
  - 入力か出力がスキンド化済み（評価空間は段 5）
  - 出力が無い（流すたびに新しいオブジェクトが増える）
  - 作り直しで出力へ書き戻せない（`RebuildRole` が無い。ブレンド）
  - 入力か出力の ObjectId が引けない
  - action を引けない
- 失敗した項目：入力の署名を控え、入力が再び変わるまで流さない。上流（たどった先を含む）に失敗した項目がある間は、下流を流さず印を残す（Blocked）
- 循環の拒否はグループの参照を変える 2 か所だけ。新規の出力しか作らない経路（魔法陣・鉄塔・サブディビジョン・辺パイプ・デッキの後付け）は循環を作れないので判定しない。読み込み・Undo で生じた循環は Cycle として外し、照会に出す
- Undo への記録
  - 評価結果は積まない（記録を止めて流す）
  - `PrimitiveMeshReplaceExisting` が記録を止めている間も `SetMeshObjectFor` を呼んでいた。これが頂点編集の履歴を消し（`MeshUndoController.SetMeshObjectFor`）、入力の編集の Undo が消えていた。記録を止めているときは書き込み先を設定しないようにした
  - Undo の復元経路には、きっかけ 2 か所のどちらも通らないものがある（選択中でないオブジェクトへの「既存へ追加」の Undo。`PolyLingPlayerViewerCore.Lifecycle.cs` の頂点編集の枝で `RebuildAdapter` に届かなかった）。Undo・Redo の後は署名を取り直してから流す
- 流すのはカレントモデルだけ（段 1 の「カレントでないモデルへの書き込み」と同じ範囲）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage4/`）

鎖：梯子（開始タグ・終了三角付き、`addMeshFromArrays`）→ パイプ（`AutoLadder`、グループとして残す）→ 魔法陣の円周配置（配置元＝パイプ）と紋章。別に 梯子 → 円周配置 RadialA → 円周配置 RadialB（配置元＝RadialA）
- `queryModifierGraph` の順は Pipe → MC#0（円周配置）→ MC#1（紋章）→ RadialA → RadialB。下流は Pipe→MC#0、RadialA→RadialB
- 梯子の上端を `setVertexPositions` で動かすと、Pipe・MC#0・RadialA・RadialB が依存の順に作り直された。紋章は変わらない（01→02、07）
- Undo 1 回で梯子・パイプ・円周配置が戻った（03）。Redo で進んだ
- `moveSelectedVertices` でも依存の順に作り直され（04）、Undo 1 回で戻った
- 組立の内外：配置元（組立の外のパイプ）を直すと魔法陣のうち円周配置の部品だけが作り直され、紋章の頂点は変わらない
- 引数の伝播：パイプに `params.thickness`=0.1・`params.segmentsFront`=2 を書くと、パイプ 20→33 頂点、円周配置 80→132 頂点。紋章 80 のまま（05）。Undo 1 回でパイプ 20・円周配置 80 に戻った（06）
- 循環
  - 円周配置（配置元＝パイプ）を既存へ追加で梯子へ足すと、グループに登録されず理由付きで失敗。グループは 4 件のまま。Undo で梯子が戻り、パイプ・RadialA・RadialB が作り直された
  - RadialA の配置元を RadialB へ差し替えると拒否され、RadialA の入力は梯子のまま
- 失敗：梯子の面を全部消すと、パイプは控えた点列で作り直され（Ok）、RadialA は「作り直しの結果が空になりました（42 → 0）」で Failed、RadialB は「上流の項目 RadialA#0 が失敗しているので流していない」で Blocked（要評価の印が残る）（08）。Undo で全部流れて Ok に戻った
- 保存 → 読み込み（`saveProjectCsv` → `loadProjectCsv`）のあと、梯子の編集で 4 項目が依存の順に作り直され（09）、Undo で戻った
- サブディビジョン（Handler）：ケージの `setVertexPositions`・`moveSelectedVertices` に子が追随、面 4 枚の削除で 2786→2741 頂点・境界 1、Undo で 2786・境界 0（10）
- 段 2・段 3：パイプの出力への `setVertexPositions`、サブディビジョンの子への `deleteFaces` は拒否。`setObjectGroupStepArgs` は効く
- 検査：3 つの csproj のコンパイルはエラー 0。`queryCommandAudit` の完了条件の項目はすべて 0。Editor.log にエラーなし
- 確かめていないもの：実際のマウスでの頂点ドラッグ（DragEnd で流す経路）。`moveSelectedVertices` はコマンドの終わりで流れた
- 段 4 と関係なく見つかったこと：同じ頂点に `setVertexPositions` のあと `moveSelectedVertices` を掛けると、`setVertexPositions` の書き込みが失われ、前の位置に差分を足した位置になった（グループの無い直方体でも同じ）

### 段 5：スキンド化後の評価空間 — 状態：済（2026-10-29）

- 評価をバインド姿勢で行い、揺れものの鎖を作り直せるようにする
- 段 4 から持ち越すこと
  - 入力か出力がスキンド化済みの項目は評価から外している（`queryModifierGraph` の reasons に出る）
  - スキンド化直後の塗り直しは自動更新（`RunAutoUpdateGroups`、`AutoUpdate` の立ったグループ）が担っている。評価器との二重を避けるため、2 経路で `PrimeAll` を呼んでいる
  - 姿勢（行列）だけを変える経路は署名（ローカル位置）を変えない（段 1 の確かめた結果）

#### 調べて分かったこと

- 頂点の格納空間：MeshFilter はローカル、スキンドはバインド姿勢のワールド（`SkinKindConverter`・`MeshFilterToSkinnedConverter` の Phase 4 が `WorldMatrix` を掛けて焼く）。ポーズ抜きの行列は `BindWorldMatrix`、`WorldMatrix` はポーズ込み
- 生成は空間を変換しない。梯子の格納値をそのまま点列にし（`ObjectGroupOps.RefreshBelts`）、ウェイトも格納値の位置で突き合わせ（`BeltWeightBinder`）、出力の格納値へそのまま書く（`PrimitiveMeshReplaceExisting`）。梯子と出力の格納空間が一致するときだけ正しい
- 揺れ鎖は空間を見ていたが、ポーズ込みの行列（`VertexToWorldMatrix`・親の `WorldMatrix`）を使っていた

#### 実装

- `Core/Ops/ModifierSpace.cs`（新規）
  - `Classify`：項目の評価空間（そのまま／バインド空間で読む／バインド空間へ戻して書く／評価できない＋理由）
  - `TryComputeMatrix`：生成物を梯子の格納空間から出力の格納空間へ移す行列 M ＝（出力の バインド→格納）×（梯子の 格納→バインド）。両方 MeshFilter なら掛けない
  - 書き戻しへの受け渡し（`Begin`・`Current`）と、出力に合わせる処理（`PrepareForOutput`）
- `MeshContext.Transform.cs`：`VertexToBindWorldMatrix`・`BindWorldToVertexMatrix`
- `ModifierGraph.ClassifyStatus`：スキンド化済みで一律に外すのをやめ、`ModifierSpace.Classify` で決める。`Entry.Space`・`SpaceReason` を足した
- `ModifierEvaluator.MarkInputsChanged`：指定した描画オブジェクトを入力に持つ項目へ、署名を見ずに印を立てる
- `PlayerCommandDispatcher.ObjectGroup.cs`
  - `RunObjectGroupStep`：評価できない項目は流さない。梯子を読む項目は M を求め、入れ子の `Dispatch` の間だけ渡す
  - `RunAutoUpdateGroups` を消した
- `PlayerCommandDispatcher.ModifierEval.cs`：`ReevaluateAfterSkinning`（`MarkInputsChanged` → `RunModifierEvaluation` → `PrimeAll`）。照会 `queryModifierGraph` に `spaces` を足した
- `PlayerCommandDispatcher.DeformSkin.cs`：スキンド化の 2 経路は `ReevaluateAfterSkinning` を呼ぶ
- `PrimitiveMeshReplaceExisting`：姿勢・位置を焼いたあとに M を掛け（法線は逆転置）、ウェイトの扱いと欠けの検査を中身を入れ替える前に行う
- `SpringBoneLadderPlacer`：梯子は `VertexToBindWorldMatrix`、親の位置は `BindWorldMatrix` で読む（`Place`・`UpdateChains`・`ReattachRootsToNearest`）
- 記録を止めているときに頂点編集の履歴を消さない（段 4 の `PrimitiveMeshReplaceExisting` と同じ扱い）
  - `PlayerCommandDispatcher.SpringBone.cs` の揺れ鎖：`SetMeshObjectFor` と記録を、記録を止めていないときだけ行う。ミラー側への写しは常に行う
  - `PlayerCommandDispatcher.Blend.cs` の `SyncSkinWeightToMirrors`、`ReplaceMirrorSideContents`：同じ
  - 揺れ鎖を評価器が流すようになり、梯子の編集の Undo が消えていた（直す前に確かめた）
- `ModifierOutputGuard`：種別の変換（`convertMeshFilterToSkinned`・`convertToSkinned`・`convertToMeshFilter`）は出力にも掛けてよい。出力を選んだまま一括スキンド化すると拒否されていた（直す前に確かめた）
- 注記：`ObjectGroup.AutoUpdate`、両シナリオのパネルの冒頭

#### 決めた扱い

- 評価空間
  - 梯子を読む項目（`CreateBeltPrimitiveCommand` 系で梯子の取り込み元を持つもの）は、生成物を M で出力の格納空間へ移して書く。梯子・出力ともに MeshFilter のときは掛けない（作成時と段 4 の結果を変えない）。一括スキンド化のあとは M が単位
  - 揺れ鎖は自分でバインド空間で読む
  - 外すもの（照会の reasons・spaces に出る）
    - 形として読む入力（断面・配置元など梯子以外）がスキンド化済み：元のローカル座標系が引けない（スキンド化は `BoneTransform` を単位に潰す）
    - 梯子を持たない項目で、入力か出力がスキンド化済み
    - 出力がスキンド化済みで梯子の取り込み元が無い（控えた点列の座標系が引けない）
- ウェイト
  - 引き継ぎは今のまま（梯子の格納値の位置で突き合わせる。M は突き合わせのあとに掛ける）
  - 出力がスキンド：ウェイトの無い頂点が 1 つでもあれば、書く前に失敗にして個数を出す
  - 出力が MeshFilter：生成物のウェイトを捨てる
- 自動更新は評価器へ寄せた。スキンド化した描画オブジェクトを入力に持つ項目は、`AutoUpdate` の値に関わらず依存の順に流れる。`AutoUpdate` は保存の互換とパネルの表示のために残し、評価には使わない。流すのは評価器の 1 回だけ
- 姿勢
  - ポーズ（`BonePoseData`）だけを変える操作：作り直し不要（評価はバインド空間。スキンドの出力はスキニングで追随する）
  - バインド姿勢そのものを変える操作（`BoneTransform`・バインドへの焼き込み）：MeshFilter の入力では作り直しが要るが検知しない（持ち越し）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage5/`）

シナリオ `roboF_T5_SK_initialDebug_フリル`（揺れフリル：円筒24C → 鎖 → SkirtFrill、揺れパイプ：アホ毛用はしご → 鎖 → AhogePipe）
- 項目 1〜17 を流し、`a8`（パイプのミラー）は段 2 の保護で拒否されるので飛ばし、`convertMeshFilterToSkinned` を直接流した
  - `spring_frill_skin_vrm`・`spring_pipe_skin_vrm` は読む `roboF_T5_SK.mqo` が無く、読み込みで止まる
- スキンド化：評価器が 鎖 → フリル、鎖 → パイプ の順に 4 項目を流し、失敗 0。spaces は鎖が「バインド空間で読む」、フリル・パイプが「バインド空間へ戻して書く」（01）
  - フリル 1200 頂点、パイプ 152 頂点（スキンド化の前と同じ）。全頂点にウェイトがあり、すべて鎖のボーンを指す。梯子は 601 頂点のうち 600 が鎖、1（上のふたの中心）が親のボーン
- 梯子（スキンド化済み）の裾の 3 頂点を `setVertexPositions` で動かす：鎖 → フリルの順に作り直され、パイプ側は流れない。フリルは裾の前だけが梯子に合わせて変わり、ほかは動かない（04→05）。全頂点が鎖のボーンを指す
- Undo 1 回で梯子・鎖・フリルが戻った（1200 頂点）。最初は Undo が効かず、揺れ鎖の `SetMeshObjectFor` が履歴を消していた。直したあとに確かめた（03）
- `setObjectGroupStepArgs`（SkirtFrill の `params.thickness`=0.02）：作り直され全頂点が鎖を指す（06）。Undo 1 回で 0.004・1200 頂点に戻った
- 姿勢：センターを Y 40° 回すと、フリルと鎖が体に付いて回る（07→08）。回したまま梯子を直すと、直した部分が回った体の前に出て、頂点は飛ばない（09）
- 保存 → 読み込み（`saveProjectCsv` → `loadProjectCsv`）のあと、梯子の編集で鎖 → フリルが作り直され、全頂点が鎖を指す（10）
- 段 1〜4
  - サブディビジョン：子への `deleteFaces` は拒否。ケージの頂点の移動に子が追随。`level` の書き換えが効く
  - 循環：円周配置 RadialA（配置元＝ケージ）を配置元にして、既存へ追加でケージへ足すと、グループに登録せず理由付きで失敗
  - 失敗：梯子の面を全部消すと、鎖は「円環を検出できない」で Failed、フリルは Blocked。Undo で両方 Ok に戻った
- 3 つの csproj のコンパイルはエラー 0。Editor.log にエラーなし
- 見つかったこと：同じ梯子の編集・厚みの変更で、フリルは 1200 → 1198 頂点になる（生成器の結果。M は単位で、空間の変換は掛かっていない）

#### 段 6 以降へ持ち越すこと

- バインド姿勢そのものの変化（`BoneTransform`・バインドへの焼き込み）を入力の変化として検知しない
- 作成時（`PrimitiveMeshCreateNewObject`）は梯子の行列を見ず、梯子の格納値のまま出力をルートに置く。梯子の行列が単位でないと、作成直後の出力は梯子からずれる（一括スキンド化のあとの作り直しで梯子に合う）
- 形として読む入力がスキンド化済みの項目は評価できない
- パイプへのミラー（`setMirrorEnabled`）は段 2 の保護で拒否される。シナリオ `roboF_T5_SK_initialDebug_フリル` の `a8` が通らない

### 段 6：逆方向 — 状態：済（2026-10-30）

- 配置系の共通の核（姿勢の推定・分解）、パイプ → 梯子、断面の線分 → 断面をグループの逆方向として組み込む
- 出力の編集を確定したときの流れ（逆方向 → 正方向 → 下流）と、保つもの・失うものの宣言

#### 実装

- 宣言（`PLModifierAttribute`）
  - `Inverse` を「出力の編集を入力へ戻す方法の名前」に改めた。値は `ModifierInverseMethod`（`beltFrames`・`vertexFrames`・`boltPattern`・`radialPlace`・`towerDeck`）
  - `InverseKeeps`・`InverseLoses` を足した。照会 `queryModifierInventory` の一覧に列を足した
  - 宣言した項目：`createPipe`・`createPlaceObject`（beltFrames）、`createVertexBillboardPlace`（vertexFrames）、`createBoltPattern`、`createRadialPlace`、`createTowerDeck`
  - `extractPipeBelt` はパイプの確定に使わない（並びの前提が「始点・断面×段・終点」で、現行の並びでは要の頂点が末尾に来る。断面点がレール上にある必要もある）。`InverseOf` は情報として残した
- 生成の控え `Core/Ops/ModifierFrameTrace.cs`（新規）：正方向の生成中だけ、枠ごとの頂点の並びと枠の番号・梯子の番号を控える
  - 記録：`PlaceObjectMeshGenerator.AppendInstance`（引数 `frameKey` を足した。置いた位置と倍率も控える）、`PipeMeshGenerator.Generate`（輪と先端の要。旧方式の並べ替えのあとの番号）
  - 付け替え：`MeshObjectAppendOps.Append`（部品 → 全体）、`MeshMergeHelper.MergeVerticesAtSamePosition`（重複頂点の結合の表）
  - 生成器の空間 → 出力の格納空間の行列 `ToOutput`：`PrimitiveMeshPostProcess.ApplyPivotOffset`・`PrimitiveMeshTransform.Apply`・`ModifierSpace.PrepareForOutput` が掛けた行列を掛け合わせる
  - 厚み付けで部品を作り直すと控えは全体へ届かない（逆方向は「対応が取れない」で断る）
- 前処理の段の対応 `BeltShapeOps.PreprocessTraced`・`ApplySplineTraced`（`BeltRungTrace`）：出力の段ごとに元の段の番号（小数）・左右の入れ替え・並びの反転を返す。`Preprocess`・`ApplySpline` はこれを呼ぶだけにした。`PrimitiveMeshFactory` の梯子の繰り返しが `ModifierFrameTrace.BeginBelt` で梯子を控える
- 共通の核 `Core/Ops/ModifierInverse.cs`（新規）
  - `Fit`：枠ごとに見本 G と編集後 E を `ToOutput` の逆で生成器の空間へ戻し、`PLSimilarityEstimator` で相似変換を求める。1〜2 頂点の枠は平行移動、一直線の枠は失敗
  - 戻し方：`InvertBelts`（段の左右と先端へ S を掛ける。補間した段は使わない）、`PlacedInstances` → `InvertBolt`・`InvertRadial`・`InvertDeck`
- 評価器（`ModifierEvaluator`）：逆方向を持つ項目は「入力の署名と引数 → 正方向の出力の署名」を直近 16 組控える。出力の署名が今の入力に対する控えと違えば未確定（`IsOutputEditPending`）。未確定の項目と下流は流さない（Blocked）。照会 `queryModifierGraph` に `pending` を足した
- 出力の保護（`ModifierOutputGuard`）：逆方向を持つ項目の出力には分類 `geometry.position` のコマンドを通す。`setMirrorEnabled` はどの出力にも通す
- Player 層 `PlayerCommandDispatcher.ModifierInverse.cs`（新規）
  - `queryModifierInverse`（照会）・`commitModifierOutputEdit`（確定）・`discardModifierOutputEdit`（破棄）
  - 見本はビューアの `BuildModifierTemplate`（`PolyLingPlayerViewerCore.ModifierInverse.cs`）が作る。書き戻しと同じ変換を `ApplyReplaceTransforms` に切り出して共有した（`PolyLingPlayerViewerCore.GeneratedMesh.cs`）
  - 評価空間の判定を `ComputeStepSpace` に切り出し、作り直しと逆方向で共有した
  - 梯子を取り込んでいる項目は `BeltAcquire.AcquireStrips` で段 → 頂点に引いて梯子の頂点を書く。点列を持っている項目は項目の梯子の引数を書く（`ObjectGroupOps.WriteBeltArgs` を `RefreshBelts` から切り出した）
  - 頂点へ藤壺は `VertexBillboardPlaceToolHandler.CollectVertexTargets`（置く順の頂点の列。`BuildCore` もこれを使う）で頂点を引き、置いた位置を入力の `WorldMatrix` の逆で格納値へ戻す
  - 未確定がある間は保存（`saveProjectFile`・`saveProjectCsv`・`saveProjectBinary`）を断る（`PlayerCommandDispatcher.DispatchCore`）
- パネル（`PlayerObjectGroupSubPanel`）：「出力の編集（逆方向）」欄。「項目の引数」で選んだ項目の保つもの・失うもの・未確定かを出し、確定・破棄のボタンを持つ（UI 自動操作の ID：`objectGroup.inverseInfo` / `inverseCommit` / `inverseDiscard`）。表示用に `ObjectGroupStepView` へ `InverseMethod`・`InverseKeeps`・`InverseLoses`・`OutputEditPending` を足した
- 断面の線分 → 断面
  - `openGroupProfileEditSpace`（`OpenGroupProfileEditSpaceCommand`）と `GroupProfileEditSpaceAdapter`（新規）：項目の断面から線分の代理を作って作業空間を開く
  - 反映は代理の線分を `ProfileAcquire` で断面として読み、`setObjectGroupStepArgs`（内部用の `AllowProfileKeys` で断面のキーを許す）で項目へ書いて作り直す。取消しは項目の断面から代理を作り直す。閉じると代理を消す（`EditSpaceSession.DeleteProxyOnClose`）
  - `ObjectGroupOps.ArgWriteBlockReason`・`TryValidateStepArgs` に断面のキーを許す版を足した
  - 作業空間の元・代理を ObjectId で引き直す（`RebindEditSpaceObjects`）。一覧のスナップショットの Undo が描画オブジェクトを複製で置き換えると、代理を見失って「壊れた」扱いになり、閉じても代理が消えなかった（直す前に確かめた）

#### 決めた扱い

- 確定したときは、正方向で作り直した結果に置き換える（編集は丸められる）。出力は入力と引数から導くもの（2.1・2.4）で、編集した形を残すと次に入力が変わったときに黙って消えるため。丸められる量は照会 `queryModifierInverse` の `maxDeviation`・`meanDeviation` で示す
- 確定は「入力を書く → その項目を作り直す（`RunObjectGroupStep`）→ `MarkRebuilt` → 下流はコマンドの終わりに依存の順に流れる」。Undo は一覧のスナップショットとグループの差し替えの 1 件（段 3 と同じ形）。Undo 1 回で入力・出力・下流が戻り、出力は編集したまま未確定に戻る
- 未確定の判定は控え（保存しない）による。読み込み直後は出力を正方向の結果として控える。未確定のまま保存すると読み込み後に編集した形が正方向の結果として残るので、保存を断る
- 書き直す梯子が別の項目の出力のときは断る（上流の逆方向へは辿らない）
- 頂点へ藤壺は、ボーン・原点へ置いたものと、スキンド化済みの入力は断る（置いた位置が姿勢込みの GPU のワールド座標で、格納値へ戻す行列が一意でない）
- 試しの作り直し（照会のずれ）は梯子を点列で渡すので、スキンドの出力でもウェイトの欠けでは止めない（`ModifierSpace.PrepareForOutput` の `checkWeights`）。確定の作り直しは今までどおり検査する
- 藤壺のパーツ ID：採番は生成器（置いた順、梯子をまたいで 0 から。`PrimitiveMeshFactory.GeneratePlaceObject` の `PartsIdCounter`）を正とする。逆方向はパーツ ID に頼らず、生成の控えで対応を取る
- 断面の作業空間（グループの項目）は、断面を線分オブジェクトから取り込んでいる項目を対象にしない（その線分を編集すれば段 4 の評価で追随する）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage6/`）

- パイプ（梯子 5 段・断面 4 点の閉ループ、AutoLadder、グループとして残す）
  - 輪 2 を相似でなく、輪 3 を平行移動、輪 4 を 2 倍＋平行移動に編集すると未確定（`pending`=1）になり、照会で書き直す入力（梯子の頂点 12 個）とずれ（最大 0.010）が出た（01→02）
  - 確定で梯子の段 3・4 が書き直され（段 4 は長さ 0.2）、パイプが作り直された（03）
  - Undo 1 回で梯子が戻り、出力は編集したまま未確定に戻り、評価器は流さない（Blocked）（04）
  - 破棄で今の入力から作り直され、未確定が外れた（05）
  - 編集の直後に Undo（頂点の編集の Undo）すると未確定が外れた
  - 未確定のまま `saveProjectCsv` は理由付きで断られた
- 下流：円周配置（配置元＝パイプ）を足し、パイプの出力を未確定にすると、円周配置は「上流の項目 Pipe#0 の出力の編集が未確定」で止まった。確定でパイプ → 円周配置の順に作り直された
- 藤壺（梯子＝同じ梯子、AutoLadder、RungLength）：部品 1 個を (0.1, 0, 0.05) 動かして確定すると、梯子の段 0 が同じ量だけ平行移動し、長さは変わらなかった（06→07）。同じ梯子を読むパイプ・円周配置も流れた。Undo 1 回で梯子が戻った
- 円周配置：形を 1.5 倍にして確定すると `params.radius`=1.5・`params.scale`=1.5、ずれ 1.3e-7。Undo 1 回で引数が戻り未確定に戻った
- ネジ配置（円・XZ・6 個）：1.2 倍で `params.pcdRadius`=0.6・`params.scale`=0.24、ずれ 1.3e-7
- 鉄塔のデッキ：Y 軸まわり 30°・上へ 0.3 で `params.rotationY`=45（元 15）・`params.height`=1.5、ずれ 1.3e-7（08）
- 頂点へ藤壺（頂点 2 個）：1 個目を (0, −0.1, 0) 動かして確定すると、入力の頂点 0 が同じ量だけ動いた。ずれ 0
- 断面の線分 → 断面（パイプの断面）：`openGroupProfileEditSpace` で代理を作り（未反映の変更なし）、代理の点を動かして反映すると項目の `profile` が書き換わりパイプが作り直された（11）。Undo 1 回で断面が戻った。取消し・閉じる（代理を消す）が通った
- 保存 → 読み込みのあと、パイプの出力の編集が未確定と判定され、確定で梯子の段 4 が書き直された（12）
- 揺れパイプ（シナリオ `roboF_T5_SK_initialDebug_フリル`）
  - `a8`（パイプへのミラー）が通った。一括スキンド化で 4 項目が流れ、失敗 0
  - AhogePipe（スキンド化済み、評価空間「バインド空間へ戻して書く」）を X 軸まわり 25° 回して照会するとずれ最大 0.018。確定で梯子「アホ毛用はしご_skinned」の頂点 18 個が書き直され、パイプの 152 頂点すべてに 9 本の鎖のボーンのウェイトが入った（13→14→15）。Undo 1 回で未確定に戻り、破棄で作り直された
  - パネルから確定（`objectGroup.inverseCommit`）でも同じく確定した（16→17→18）
- 段 1〜5：サブディビジョンの子への `setVertexPositions`・`deleteFaces` は拒否、`level` の書き換えで 698 頂点に作り直し、ケージの編集に追随（評価器の状態 Handler）
- 3 つの csproj のコンパイルはエラー 0。`queryCommandAudit` の完了条件の項目はすべて 0。`queryUiAutomationAudit` の登録失敗・属性の付け忘れ・重複は 0。Editor.log にエラーなし
- 段 6 と関係なく見つかったこと
  - Undo は担当の履歴（MeshList と頂点編集）ごとに分かれていて、確定（MeshList）の Undo のあと続けて Undo しても、その前の頂点の編集（頂点編集の履歴）は戻らなかった
  - 項目を作った直後（画面の更新の前）に入力を編集すると、評価器が初めて見たときの入力が編集後の値になり、その編集を変化として扱わない（段 4 の「初めて見た項目は控えるだけ」による）

#### 段 7 以降へ持ち越すこと

- 上流の項目の出力である入力（辺パイプの帯など）へは逆方向を辿らない
- 厚み付けありのパイプ・フリルは逆方向を持たない（部品を作り直すので頂点の対応が取れない）。フリル・回転体・2D押し出しの出力からの逆方向は宣言していない（断面の作業空間だけ）
- まつげの逆方向（補間した段 → 元の梯子の当てはめ、回転の始点・終点への当てはめ）
- 藤壺のパーツ ID が新規作成と作り直しで一致するかは確かめていない
- 段 5 から持ち越した 3 件（バインド姿勢そのものの変化の検知、作成時に梯子の行列を見ない、形として読む入力がスキンド化済みの項目）は手を付けていない

### 段 7：NCSHAGLIB からの移植 — 状態：済（2026-10-30）

- 平面化＋貼り付けのモディファイア
- まつげ（配置系の変種）、眉・まぶた（パイプ＋貼り付け）

#### 移植元で確かめたこと（`…\Lib\NCSHAGLIB\`）

- 貼り付け `EPMXToolFor__LocalAttach.cs` の `_PasteFlatParts`
  - 頂点を「原点を通り方向に直交する平面」へ射影する
  - 射影点から方向の直線で参照面の三角形と交差させる。`Geometry/Geometry.cs` の `crosspos1` は直線（線分ではない）
  - `offset＋符号付き距離` が min〜max に入る交点のうち近いものへ移し、平面からの高さを足す
- 回転 `EPMXToolFor__LocalRotation.cs`
  - 段 i の角度は 始点＋(終点−始点)×i/n
  - ローカル軸を X→Y→Z の順に回す
  - `Helper/HagDXHelper_MatrixHelper.cs` の `RotationAxis`（行ベクトル）は、Unity の `AngleAxis` と数値が同じ
- 枠・倍率 `EPMXToolFor_HairMaker_Common.cs` の `GetAxis`・`CreateBaseTriangles`・`CreateAndAddVertex1`
- 眉 `EPMXToolFor_EyebrowMaker.cs`：梯子の Z を 0 → `CreatePipe_` → 貼り付け
- 旧コードの不具合（移植では意図どおりにした）
  - `ResetLocalMatrix` がローカル変数を作るだけで軸を戻さず、回転が段・梯子をまたいで積み重なる
  - `_PasteFlatParts` が |offset＋距離| で比べて、直線の始点からの距離を控える（offset≠0 で最も近い交点を選べない）

#### 実装

- 平面化＋貼り付け `createSurfacePaste`（`CreateSurfacePasteCommand`、`PanelCommand.PrimitiveBelt.cs`）
  - 入力：`sourceIndex`（Read）、参照面 `referenceIndex`（Read。-1 で平面化だけ）
  - 引数：方向（既定 (0,0,−1)）・平面化・offset・min・max
  - 本体は `Tools/PrimitiveMesh/SurfacePasteOps.cs`（新規）
    - 入力の格納値をモデル空間（`VertexToBindWorldMatrix`）へ出して処理し、入力の格納空間へ戻す
    - 面構成・UV・パーツ ID・サブ ID は入力のまま
    - 三角形の内外は重心座標で判定し、縁は `EdgeEpsilon`（1e-5）で拾う
  - `PrimitiveMeshFactory.Build` に解決口 `SurfacePasteResolver` を足した。ビューアの `ResolveSurfacePasteForCommand`（`PolyLingPlayerViewerCore.CreateCommands.cs`）が入力と、モデル空間へ移した参照面の複製を渡す。見本（`BuildModifierTemplate`）にも渡す
  - 記録は基底の `OnKeepAsGroup`。依存の辺は `IsMeshRef` から出る（2 入力）。逆方向は持たない（出力の編集は段 2 の保護で拒否）
- まつげ `createLush`（`CreateLushCommand : CreateBeltPrimitiveCommand`）
  - 入力：梯子、原型 `sourceMasterIndices`（複数なら結合）
  - 段の補間：既存の `BeltSplineOptions`
  - `Tools/PrimitiveMesh/LushMeshGenerator.cs`（新規）の引数
    - 枠の決め方：`NextRungMidpoint`（旧ツール）／`BeltNormal`（藤壺の面の法線。`PlaceObjectMeshGenerator.RungNormal` を公開した）
    - 置く位置：中点／左／右
    - 回転：始点・終点（度）、補間 `Linear`（i/n＝旧ツール）／`LinearInclusive`（i/(n−1)）／`Constant`
    - 倍率：`PlaceScaleMode`＋`Scale`（旧ツールの ×10 は `Scale`=10）
    - 長さを測る面の法線（0 で 3D。旧ツールは (0,0,1)）
    - 枠の反転（旧 rotatePipe）、逆順
  - 最後の段の頂点は終点。終点が無いときは直前の段の向きを延ばした点
  - 置くのは `PlaceObjectMeshGenerator.AppendInstance`（`frameKey`＝補間後の段の番号）。`EachPreprocessedBelt` が `ModifierFrameTrace.BeginBelt` を立てる（段 6 の控えに乗る）
  - 頂点 ID は振らない。置いた 1 個＝パーツ 1 つ、サブ ID は原型のまま（`AssignPartsIds` に `Lush`・`SurfacePaste` を足した）
- まつげの逆方向 `lushFrames`
  - `PLModifierAttribute`（`ModifierInverseMethod.LushFrames`）
  - 本体 `ModifierInverse.InvertLush`
  - 振り分け：`PlayerCommandDispatcher.ModifierInverse.cs`。梯子の頂点（または点列）と `params.rotationStart`・`params.rotationEnd` を両方書く
- 眉・まぶた：コードの追加なし。次の 3 項目の鎖で作る
  1. `createSurfacePaste`（平面化だけ、参照面 -1）
  2. その出力を梯子（AutoLadder）にした `createPipe`
  3. パイプの出力を参照面へ `createSurfacePaste`（平面化なし）
- 計画で挙げた `BeltSplineSubdivider.Basis` は作らなかった。前処理全体（向き補正・スプライン・切り詰め・残す段）へ単位入力を通して重みを取る方式にした。規則は `BeltShapeOps` の 1 か所のまま

#### 決めた扱い

- まつげの逆方向
  - 1 本の向きの変化は、段の向きでも回転の引数でも表せて一意に決まらない。そこで次のように割り振る
    - 位置：置いた点（アンカー）の移動を段へ。補間した段の左右をアンカーの移動だけ平行移動した点を目標にし、元の段の左右と先端（開始・終了。スプラインの制御点）を最小二乗で当てはめる
    - 向き：回転の引数へ。新しい梯子で枠を作り直し、残る回転 Bn⁻¹·R·Bo·q を X→Y→Z の角度にほどく（元の角度の近くへ 360° 単位で寄せる）。角度 ≈ 始点·(1−t)＋終点·t を軸ごとに最小二乗で解く
  - 置いていない段（切り詰め・残さなかった段）が決まらないぶんは、元の値へ弱く寄せる（λ＝対角の平均 × 1e-6）
  - 保つもの：梯子の面構成・頂点 ID・ウェイト、原型、段の補間・置く位置・倍率の引数
  - 失うもの：部品の歪み、段の向き（1 本の向きの変化は回転の引数へ寄せる）、段ごとの回転のうち線形の補間で表せない分、補間した段だけの動き、倍率の変化
  - 梯子が別の項目の出力（貼り付けた梯子など）のときは段 6 の決まりどおり断る
- 貼り付け・まつげはスキンド化済みの入力では評価しない（段 5 の `ModifierSpace.Classify` のまま）
- 作り直しの書き戻し（`PrimitiveMeshReplaceExisting`）はサブ ID を振り直さない。図形ごとの決まり（`PrimitiveMeshFactory.AssignPartsIds`）で生成物が持つ値を使い、新規作成と同じにした
  - 直す前は書き戻しが `AssignSubIdByPartsId` を掛けていた
  - そのため藤壺のサブ ID が、新規作成では原型のまま（0）、作り直しでは 0〜3 になっていた（直す前に確かめた）
- 未確定の判定（`ModifierEvaluator.IsOutputEditPending`）は、今の出力が控えたどれかの正方向の結果なら未確定にしない
  - 直す前は、入力の編集を Undo すると、出力がまだ前の入力の結果のまま残る。これを未確定と見て止めていた（段 6 の判定。直す前に確かめた）
  - 確定の Undo のあとの未確定（編集した出力に戻る）はこれまでどおり
- 段 6 の持ち越しの扱い
  - 段 7 で扱った：まつげの逆方向、藤壺のパーツ ID の一致
  - 段 7 では扱わず 6 章へ移した
    - 上流への逆方向
    - 厚み付け・フリル・回転体・2D押し出しの逆方向
    - 段 5 からの 3 件

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage7/`）

試験の組
- 球（半径 0.5）を参照面にした
- 梯子 LashLadder：z=0.3、5 段、開始タグ・先端付き
- 原型 LashProto：長さ 1 の四角

平面化＋貼り付け
- 平面化＋貼り付け（方向 (0,0,−1)、max=0）で、梯子が球の前面に乗った（14 頂点）
- それを梯子にしたまつげ（補間 2、17 本）が依存の順に流れた（01）
- 球の全頂点を z+0.06 動かすと、貼り付けた梯子（z がちょうど +0.06）→ まつげの順に作り直された（02）
- Undo 1 回で元に戻った（03）
- 貼り付けの出力への `setVertexPositions` は段 2 の保護で拒否された

まつげ
- 梯子の頂点の編集で 貼り付け → まつげ が作り直された（04）。Undo 1 回で戻った（05）
- `setObjectGroupStepArgs` で回転を書き換えると作り直された。Undo 1 回で前の回転に戻り（06）、Redo で進んだ
- 逆方向（梯子を直接読むまつげ Lush2）
  - 回転の引数だけ違う形（始点 (45,10,−50)・終点 (75,−10,−130)）を出力へ書いた
  - 照会で、梯子は書き直さず、回転が始点 (45.00,10.00,−50.00)・終点 (75.00,−10.00,−130.00) に戻ることが出た（ずれ 7.8e-8）
  - 確定した（07→08）。Undo 1 回で引数が元に戻り、出力は未確定に戻った
  - 出力全体を (0,0.03,0.02) 動かして確定すると、梯子の段 5・先端 2 がちょうど同じ量動き、回転は変わらなかった（ずれ 9.8e-8）。下流（貼り付け → まつげ）も流れた

眉・まぶた
- 平面化（BrowFlat）→ パイプ（BrowPipe）→ 貼り付け（Brow）の鎖ができ、球の前面に乗った（09）
- 梯子の段 2 を上げると、依存の順に 6 項目が作り直された（10）。Undo 1 回で戻った

保存 → 読み込み（`saveProjectCsv` → `loadProjectCsv`）
- 読み込んだあとに梯子を編集すると、6 項目が依存の順に作り直された（11）
- 未確定のまま保存すると理由付きで断られた

藤壺のパーツ ID
- 梯子＋原型、段の間引き 2、補間 1 で試した
- 新規作成と作り直し（`rebuildObjectGroup`）で、パーツ ID（0〜4）とサブ ID（原型のまま 0）が一致した
- パイプも作り直しの前後で一致した

段 1〜6
- サブディビジョン：子への `deleteFaces` は拒否、`level` の書き換えが効いた（状態 Handler）
- 確定・Undo・破棄・未確定の保存の拒否は段 6 と同じ

検査
- 3 つの csproj のコンパイルはエラー 0
- `queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型）はすべて 0
- Editor.log にエラーなし

段 7 と関係なく見つかったこと
- 作った直後（画面の更新の前）に出力を編集すると、評価器が初めて見た出力が編集後の形になり、未確定と判定しない（段 6 の「段 6 と関係なく見つかったこと」の 2 つ目と同じ仕組み。照会 `queryModifierGraph` を 1 回挟むと控えが取れる）
- 球を正面から見る向きのとき、まつげの板は裏向きで描かれなかった（片面表示）

### 段 8：鎖ごとの通し確認 — 状態：済（2026-10-30）

- 揺れフリル・揺れパイプ・パイプの梯子化・辺パイプ・藤壺・頂点藤壺・断面系・ブレンド・魔法陣（部品の引数変更、円周配置の配置元の変更）・鉄塔（デッキの取り替え）・まつげ・眉

#### 進め方と試験データ

- 確かめたもの：作成 → 上流の編集 → 依存の順の作り直し（`queryModifierGraph`）→ Undo 1 回 → 引数の書き換え → 逆方向（持つものだけ）→ 保存 → 読み込みのあとの編集。出力の保護も鎖ごとに確かめた
- 鎖ごとに Play を入れ直した。キャプチャは `SandBox/Captures/stage8/`、保存は `SandBox/stage8/<鎖>/`
- 揺れフリル・揺れパイプ：シナリオ `roboF_T5_SK_initialDebug_フリル` の c1〜h2 を流した。c7（既存のプレファブ入力を上書きする保存）と i1（プレファブの書き出し）は流していない。読み込みで止まる項目は無かった
- 藤壺：シナリオ `barnacle_place` の e1〜e21
- 断面系の回転体（断面を値で持つもの）：シナリオ `revolution_bottle`
- ほかは段 8 のシナリオを作った（`@settings/PolyLing/PolyLing/scenarios/stage8/`）：`stage8_パイプの梯子化`・`stage8_辺パイプ`・`stage8_頂点藤壺`・`stage8_断面系`・`stage8_ブレンド`・`stage8_魔法陣`・`stage8_鉄塔`・`stage8_まつげ眉`
  - `検証_髪パイプ往復.csv` は読む `SandBox/HairPipeTest/zero.pmx` が無いので使っていない
- 不具合の扱い：段 1〜7 で「済」とした扱いがその鎖で成り立たないもの、原因が評価器・作り直しの経路・生成器の書き戻しにあるものは段 8 で直した（直す前に再現を確かめた）。段 1〜7 で外すと決めたもの、新しい機能が要るもの、鎖と関係なく見つかったことは 6 章へ回した

#### 直したもの

1. 一覧のスナップショットの Undo のあと、揺れ鎖の作り直しが鎖を作り足していた（揺れパイプ）
   - 症状：引数の書き換えを Undo したあとにアホ毛用はしごを編集すると、作り直しのたびにボーンが 10 本増えた。ログは「取り込み元がミラーペアの実体側ではない」「既にある鎖へは流し込めなかったので作り直しました（ミラーぶんの鎖が控えてあるが、ミラーの設定が引けない）。古い鎖は残っています」
   - 原因：`MeshFilterToSkinnedRecord.RestoreList`（`Core/Records/MeshListRecords.cs`）が全描画オブジェクトを複製に置き換えるが、`ModelContext.MirrorPairs` は置き換え前のオブジェクトを指したまま残る。`GetMirrorPair` が複製を引けず、`PlaceSpringBoneLadderChains` がミラーの写し行列を作れない（`PlayerCommandDispatcher.SpringBone.cs` の `sblPair`）
   - 直し方：Undo・Redo の前に今のミラー対を ObjectId の組で控え、置き換えたあとに複製へ引き直して `Build` する。最初の Undo では後の対のうち両方が前の一覧に居るものを使い、最初の Redo で前の対を控える
2. 新規オブジェクトとして作った項目の作り直しで、置き方の位置が二重に掛かっていた
   - 症状：`placement.worldPosition` が 0 でない項目（2D押し出し・パイプ・取り出した梯子からのパイプ）を作り直すたびに、出力がその位置の分だけずれた（2D押し出しは画面から外れた）
   - 原因：作成（`PrimitiveMeshCreateNewObject` → `BuildPrimitiveMeshContext`）は位置と焼かなかった回転・拡大を出力の姿勢（BoneTransform）へ入れるが、書き戻し（`PrimitiveMeshReplaceExisting` → `ApplyReplaceTransforms`）は同じ値を頂点へ焼いていた
   - 直し方：`RunObjectGroupStep` で、作ったときの追加先モードが新規オブジェクト・新規モデルなら、書き戻しの引数の位置を 0、焼かなかった回転・拡大を単位にする（`PlayerCommandDispatcher.StripPoseOwnedByOutput`）。逆方向の見本（`BuildModifierTemplate`）も同じ扱いにした。既存へ追加で作ったもの（頂点へ焼いてある）は変えない
3. 作業空間の反映を、選択で出力の保護に掛けていた（断面系）
   - 症状：グループの項目の断面の作業空間（`openGroupProfileEditSpace`）で、回転体の出力を選んだまま `applyEditSpace` すると「出力は編集できません」で拒否された
   - 原因：`applyEditSpace` は Writes=ModelWide で、`ModifierOutputGuard` が選択中の描画オブジェクトを書き込み先とみなしていた。作業空間が実際に書くのは元データ（UV なら元の描画オブジェクト、線分の断面なら線分オブジェクト）で、グループの項目の断面は項目の引数だけを書く
   - 直し方：`ModifierOutputGuard.DenyReason` に書き込み先を明示する口（explicitTargets）を足し、反映・取消し・閉じるはビューアの作業空間が書く先で判定する（`PlayerCommandDispatcher.OnEditSpaceWriteTargets` → `PolyLingPlayerViewerCore.EditSpaceWriteTargets`）。反映は元データ、取消し・閉じるは代理だけ（空）
   - 確かめた：出力を選んだままでも断面の反映が通り、回転体が作り直された。UV の作業空間を出力（回転体）で開いて反映すると、代理を選んでいても拒否された
4. 隠した入力を `moveSelectedVertices`・`scaleSelection` で動かしても下流が流れなかった（まつげ・眉）
   - 症状：参照面の球を隠してから動かすと、貼り付け・まつげ・眉が作り直されず、表示に戻した時点で流れた
   - 原因：きっかけ 2（`PlayerViewportManager.SyncMeshPositionsAndTransform`）が、描画バッファに載っていないオブジェクト（unifiedIdx -1）で評価器へ知らせる前に戻っていた
   - 直し方：描画バッファに載っていないときも評価器へ知らせてから戻る（`NotifyModifierInputMoved` に切り出して両方から呼ぶ）
5. まつげの逆方向で、スプライン分割を切っていると平行移動が回転の引数へ漏れていた
   - 症状：梯子を直接読むまつげ（スプライン分割なし）の出力全体を平行移動して照会すると、梯子の段は動くが先端は動かず、回転の始点・終点が 2〜8° ずれ、ずれ最大 0.016 だった
   - 原因：スプライン分割を切っていると先端は段の位置に効かず、正規方程式の列が 0 で元の値へ寄せられる。ところが終点は最後の段の枠の向きを決める（`LushMeshGenerator.TryComputeFrame` が `EndPoint` を読む）ので、動かない終点が向きの変化として回転へ割り振られた（`ModifierInverse.InvertLush`）
   - 直し方：段の位置に効かない先端は、隣の段のアンカーの移動だけ平行移動する（開始は最初の段、終了は最後の段）
   - 確かめた：同じ編集で先端 2 個も (0, 0.03, 0.02) 動き、回転は (45,10,−50)・(75,−10,−130) のまま、ずれ 7.6e-8

#### 決めた扱い

- 位置・回転・拡大の扱い（直したもの 2）：新規オブジェクト・新規モデルとして作った項目の置き方は出力の姿勢が持つ。作り直しは姿勢を変えず中身だけを入れ替える。作ったあとに出力を動かしても、作り直しで戻らない
- 作業空間の反映・取消し・閉じるの書き込み先は、作業空間が書く先で決める（直したもの 3）
- ブレンドは段 4 の決めた扱いのまま（作り直せない。照会の reasons に「RebuildRole が無い」）。記録の誤り（下の確かめた結果）は作り直しの設計と一緒に 6 章で扱う
- 断面を線分オブジェクトから取り込む項目をコマンドで作るときは、断面の値も渡す。作成は値を使い、取り込み元は作り直しで読む（`ObjectGroupOps.RefreshProfile`）。値を渡さないと作成に失敗する（2D押し出し）か既定の断面で作られる（回転体）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage8/`）

揺れフリル（円筒24C → 鎖 SpringLadder_00_top → SkirtFrill）
- 作成：一括スキンド化で 4 項目が流れ、失敗 0。フリル 1200 頂点すべてに鎖のボーンのウェイト（c1_frill_01）
- 梯子の裾の前 3 頂点を `setVertexPositions` で動かすと、鎖 → フリルの順に作り直された（c1_frill_03→04）。Undo 1 回で梯子が戻り、鎖 → フリルが流れた
- `params.thickness`=0.02 で作り直され（05）、Undo 1 回で 0.004 に戻った
- 出力への `setVertexPositions`・`deleteFaces` は拒否（フリルは逆方向を持たない）
- 保存 → 読み込みのあと、梯子の編集で鎖 → フリルが流れた（c12_07）

揺れパイプ（アホ毛用はしご → 鎖 SpringAhoge_top（ミラー付き）→ AhogePipe）
- 梯子の段 8 と先端を動かすと鎖 → パイプが流れた（c2_pipe_01→02）。直したもの 1 のあと、引数の Undo を挟んでもボーン数は 931 のまま（鎖へ流し込み）
- Undo 1 回で梯子が戻った。`params.thickness`=0.01 で 152 → 304 頂点、Undo で 152
- 逆方向：出力全体を (0.02, 0, 0.01) 動かして照会（ずれ最大 0.0098）、確定で梯子の段 9 個の頂点 18 個がちょうど同じ量動いた（05）。Undo 1 回で梯子が戻り出力は未確定、破棄で作り直し（06）。`deleteFaces` は「位置の編集だけ」で拒否
- 保存 → 読み込みのあとも鎖 → パイプが流れ、ボーン数は変わらない

パイプの梯子化（梯子 → 旧互換パイプ（グループなし）→ `extractPipeBelt` HairBelt → `createPipe` HairRe）
- 元のパイプの上の輪を動かすと HairBelt → HairRe の順に作り直された（c3_extract_06→07。直したもの 2 のあと）。Undo 1 回で戻った
- `params.rungStep`=2 で HairBelt が 14 → 8 頂点、HairRe も流れた。Undo で 14
- HairRe の逆方向は「梯子 HairBelt は別の項目の出力」で断られた（6 章の上流への逆方向）。HairBelt への書き込みは拒否
- 保存 → 読み込みのあと、元のパイプの編集で 2 項目が流れた

辺パイプ（板の辺 3 本 → `edgeRibbonFace` → `createPipe`）
- 辺の頂点を動かすと帯 → パイプの順に作り直された（c4_edgepipe_01→02）。Undo 1 回で戻った
- パイプの `params.thickness`=0.3 で作り直された（04）
- パイプの逆方向は「梯子 EdgeRibbon は別の項目の出力」で断られた。帯への `deleteFaces` は拒否
- 保存 → 読み込みのあと、辺の編集で 2 項目が流れた（05）

藤壺（球の梯子＋円錐・土台 → BT_Barnacle、108 個）
- 円錐の先端を伸ばす（02）・球を `scaleSelection` で縦に縮める（04）のどちらでも作り直された。円錐の編集は Undo 1 回で戻った（03）
- `params.scale`=2 で作り直された（05）
- 逆方向：部品 1 個を動かして確定（ずれ最大 0.010）。Undo 1 回で未確定に戻り、評価器は「出力の編集が未確定」で止めた（Blocked）。破棄で作り直し
- 保存 → 読み込みのあと、円錐の編集で作り直された（08）

頂点藤壺（球の頂点 3 個＋原型の板 → VB_Out）
- 頂点 20 を外へ動かすと、置いた板がその頂点へ付いて作り直された（c6_vbill_01→02）。Undo 1 回で戻った（03）
- `scale`=3 で作り直された（04）
- 逆方向：1 個目の板を (0, −0.1, 0) 動かして確定すると、球の頂点 20 がちょうど同じ量動いた（ずれ 0）。Undo 1 回で頂点が戻った
- 保存 → 読み込みのあと、頂点の編集で作り直された（06）

断面系（折れ線 → 回転体 Rev、閉ループ → 2D押し出し P2D・パイプ ProfPipe、断面を値で持つ RT_Bottle）
- 折れ線の点を動かすと Rev だけ、閉ループの点を動かすと P2D・ProfPipe が作り直された（c7_profile_05→06。直したもの 2 のあと）。Undo 1 回で戻った（07）
- `params.radialSegments`=5 で Rev が作り直された（08）。このあとの Undo 2 回では、その前の折れ線の編集は戻らなかった（段 6 の「Undo は担当の履歴ごとに分かれている」と同じ現れ）
- 線分から取り込む Rev は `openGroupProfileEditSpace` を「その線分を編集すれば追随」で断った。RT_Bottle は作業空間を開き、代理の点を動かして反映すると作り直された（12→13。直したもの 3 のあと）
- 保存 → 読み込みのあと、折れ線・閉ループの編集で 3 項目が流れた（09）

ブレンド（BlA・BlB → 宛先 BlC、宛先を書き換える設定）
- グループは記録されるが、照会の出力は BlC ではなく BlC_backup（ブレンド前の形の控え）だった。BlC は入力に並ぶ。状態は Excluded（RebuildRole が無い）
  - 原因：`BlendOperation.ApplyBlend` は宛先を書き換える設定でも `<宛先>_backup` を足す。`CaptureObjectGroup` は増えた ObjectId の最大を出力にするので、`PlayerCommandDispatcher.Blend.cs` の「新規オブジェクトを作らない設定では宛先そのものが出力先」という意図と違う
  - そのため BlC への書き込みは通り、BlC_backup への書き込みが「出力は編集できません」で拒否される
- BlA を編集しても作り直されない（要評価の印だけ立つ）。Undo 1 回で戻った

魔法陣（リング・紋章・円周配置（配置元＝直方体））
- 紋章の `params.size`=0.5 で紋章だけ作り直され（c9_magic_01→02）、Undo 1 回で戻った（03）
- `replaceObjectGroupSource` で配置元を球へ差し替えると円周配置だけ作り直された（04）。直後の Undo 1 回で配置元が戻った
- 差し替えたあとは球の編集で円周配置が流れ（05）、元の直方体の編集では流れない
- 逆方向：円周配置を 1.5 倍にして確定すると `params.radius`=1.2・`params.scale`=1.5（ずれ 3e-7）。Undo 1 回で未確定に戻った
- 保存 → 読み込みのあと、球の編集で円周配置が流れた（11）

鉄塔（塔体＋デッキ（配置元＝四角の板））
- `replaceObjectGroupSource` でデッキの配置元を三角の板へ取り替えた（c10_tower_01→02）
- 三角の板の頂点を伸ばすとデッキが作り直された（03）。Undo 1 回で戻った（04）
- `params.height`=2.5 で作り直された（05）
- 逆方向：デッキを 0.5 上げて確定すると `params.height`=1.5（ずれ 0）（06）。Undo・破棄が通った
- 保存 → 読み込みのあと、三角の板の編集でデッキが流れた（07）

まつげ・眉（球、梯子 LashLadder、原型 → 貼り付け → まつげ、梯子 → まつげ Lush2、平面化 → パイプ → 貼り付け（眉））
- 梯子の段 2 を上げると 6 項目が依存の順に作り直された（c11_lush_02→03）。Undo 1 回で戻った（04）
- 隠した球を動かすと 貼り付け → まつげ・眉が流れた（直したもの 4 のあと）
- Lush2 の `params.rotationEnd` の書き換えは通った（まつげの板は片面表示で画面では見えず、形は確かめていない）
- 逆方向（Lush2）：直したもの 5 のとおり。確定・Undo 1 回（梯子と先端が戻る）・破棄が通った
- 貼り付けの出力への書き込みは拒否
- 保存 → 読み込みのあと、梯子の編集で 6 項目が流れた（08）

検査
- 3 つの csproj のコンパイルはエラー 0
- `queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型）はすべて 0
- Editor.log（最後の Play 以降）にエラー・例外なし。Unity での反映（`unity_validate_changes`）もエラー・例外 0

#### 段 8 と関係なく見つかったこと

- 頂点編集の Undo の履歴は、書き込み先のオブジェクトが替わると消える（`MeshUndoController.SetMeshObjectFor` の `_vertexEditStack.Clear()`）。BlA を編集し、続けて BlC を編集して Undo を 2 回すると、BlA の編集は戻らず、2 回目は一覧の履歴へ進んでブレンドのグループが消えた。段 6 の「Undo は担当の履歴ごとに分かれている」と同じ仕組みの別の現れ
- スキンド化済みのパイプの出力を `moveSelectedVertices` で動かすと、データは動くが画面は次の更新まで古い形のまま（揺れパイプ c2_pipe_04）。原因は調べていない
- `runScenario stage8_パイプの梯子化` を、モデル 0 に回転体があるまま流すと、応答の deletedObjectIds にその回転体の ObjectId が入った。原因は調べていない

### 段 9 以降の決めた扱い（2026-10-30。6 章からの段分けで承認）

- ブレンド（段 13）：基準の形を読む入力キー（例 `baseMasterIndex`、Read）を足し、宛先を出力にする。宛先を書き換える設定では控え `<宛先>_backup` が基準、複製を作る設定では元の宛先が基準。作り直しは出力へ書き、控えは作らない
- 作成時の梯子の行列（段 10）：梯子の 格納→バインド を常に生成物へ掛け、出力をモデル空間に置く。段 5 の「梯子・出力ともに MeshFilter なら M を掛けない」を改める。保存済みのグループのうち梯子の行列が単位でないものは、次の作り直しで出力の位置が変わる
- ローカルで読む入力（原型・断面）がスキンド化済みのとき（段 10）：スキンド化のときに元の ローカル→バインド の行列を描画オブジェクトに控えて保存し、それで読む（新しい保存項目）
- Undo の履歴（段 12）：履歴は今の 2 本（一覧・頂点編集）のまま、書き込み先が替わったときに頂点編集の履歴を消すのをやめる。順序は今の時系列の調停（`MeshUndoController.cs` の OperationLog、`UndoGroup.cs` の PerformUndo）に任せる。1 本にまとめるかは今後の課題（6 章）

### 段 9：控えと差分の取り違え — 状態：済（2026-10-30）

- 範囲
  - 作った直後の編集を変化・未確定として扱わない件：グループを足した直後に、その項目の入力の署名と正方向の出力を控える口（例 `ModifierEvaluator.PrimeGroup`）を足し、`CaptureObjectGroup`（`PlayerCommandDispatcher.ObjectGroup.cs`）と、組立（魔法陣・鉄塔。`CaptureObjectGroup` を通らない）がグループを作る箇所で呼ぶ。今は `RefreshModel`・`MarkChangedInputs` が初めて見た時点の値を控えるだけで、サブディビジョンだけが作成時に控えている（`SubdivisionSync.cs`）
  - runScenario の deletedObjectIds：原因は、実行前と実行後の安定 ID をどちらもその時のカレントモデルから取っていること（`PlayerCommandDispatcher.Revision.cs` の `CaptureObjectIds`・`FinishRevision`、控えは `PlayerCommandDispatcher.cs` の `DispatchAuthorized`）。シナリオ先頭の `createEmptyModel` がカレントを新しいモデルへ替えるので、前のモデルの全 ID が消えた扱いになる。控えをモデルごと（モデルの参照と ID の組）に持ち、同じモデル同士で比べる。新しく増えたモデルの ID は created に入れる
  - スキンド化済みの出力を `moveSelectedVertices` で動かしたとき画面が古いまま：原因は未特定。読んだ経路は `MoveToolHandler.Commands.cs` の `ExecuteFromCommand` → `ApplyNumericMove` → `MoveToolHandler.Move.cs` の `ApplyDelta` → `OnSyncMeshPositions` → `PlayerViewportManager.Renderer.cs` の `SyncMeshPositionsAndTransform`（位置バッファへ書く）。面は `UnityMesh` を描き（`MeshSceneRenderer.cs`）、`UnityMesh` を書き直すのは `UpdateTransform` から呼ぶ `WritebackTransformedVertices`（`UnifiedSystemAdapter.cs`）。出力の編集が未確定のときは評価が止まり、`RunModifierEvaluation` は `EnterTopologyChanged` の前で戻る。再現して原因を特定してから直す
- 確かめ方
  - `stage8_パイプの梯子化` を流し、照会を挟まずに HairRe の出力を `setVertexPositions` で動かす → `pending`=1。`stage8_魔法陣` の円周配置でも同じ
  - モデル 0 に図形を置いたまま `stage8_パイプの梯子化` を流す → 応答の deletedObjectIds が空
  - `roboF_T5_SK_initialDebug_フリル` の c2 で AhogePipe を `moveSelectedVertices` で動かし、変更前・直後のキャプチャで比べる
- 完了の条件
  - 作成直後の入力・出力の編集が、要評価・未確定として扱われる
  - deletedObjectIds に他のモデルの ID が入らない
  - 画面が古いままになる件の原因が file:line 付きで書けていて、直後のキャプチャに動いた形が出る

#### 調べて分かったこと（画面が古いまま）

- 再現の条件：グループを足したあとに出力を動かすだけでは画面は動いた。`setObjectGroupStepArgs`（AhogePipe の `params.thickness`=0.01）→ Undo → `moveSelectedVertices` の順で再現した。データは動く（`getRawData` の y が 1.0229 → 0.9729）が画面は変わらない（`SandBox/Captures/stage9/c2_repro_06→07→08`、`10→11` は同じ画像）。段 8 の c2_pipe_04 も引数の書き換えの Undo の後だった
- Editor.log：`[WritebackTransformedVertices] 再生成した Mesh の頂点数が展開頂点数と一致しない mesh='AhogePipe_skinned' regenerated=576 expected=1152`（AhogePipe+_skinned も同じ）
- 原因
  - 引数の書き換え・作り直しの Undo は 1 つのグループで、一覧のスナップショット `MeshFilterToSkinnedRecord` とグループの差し替え `ObjectGroupChangeRecord` を積む（`PlayerCommandDispatcher.Subdivision.cs` の `RecordSubdivisionUndo`）
  - `UndoStack.PerformUndo`・`PerformRedo` は `LastExecutedRecord` にグループの先頭の 1 件だけを入れる（`UndoStack.cs`）。Undo では `ObjectGroupChangeRecord` が入る（ログ `lastRecord=ObjectGroupChangeRecord, needsRebuild=False`）
  - `PolyLingPlayerViewerCore.Lifecycle.cs` の一覧の Undo の枝は `needsRebuild` を `lastRecord` だけで判定し、型の一覧に `MeshFilterToSkinnedRecord` が無かった。`EnterUndoApplied`（`RebuildAdapter`）を通らず、行列の更新（`EnterVerticesMoved`・`UpdateTransform`）だけが走った
  - GPU バッファは Undo 前の面構成（304 頂点・展開 1152）のまま、メッシュは 152 頂点（展開 576）に戻る。以後の頂点移動の書き戻しは `UnifiedSystemAdapter.WritebackTransformedVertices` の頂点数の照合で捨てられ、`UnityMesh` が更新されない
  - Undo の直後に形が正しく見えたのは、`MeshFilterToSkinnedRecord.CloneMeshContext` が `UnityMesh` を `MeshObject` から作り直しているため（`MeshListRecords.cs`）
  - スキンド化済みの出力に限らず、一覧のスナップショットの Undo・Redo で面構成が変わるもの全部で起きる

#### 実装

- 作った直後の控え
  - `ModifierEvaluator.PrimeGroup(model, group)`（グループの全項目）・`PrimeStep(model, step)`（1 項目）を足した。中身は既存の `Prime(model, entry)`（入力の署名・要評価の印を下ろす・正方向の出力の控え）
  - 呼ぶ箇所：`CaptureObjectGroup` の `AddObjectGroup` の後（`PlayerCommandDispatcher.ObjectGroup.cs`）、組立の `ExecuteEmptyParentAssembly` の `AddObjectGroup` の後（`PlayerCommandDispatcher.MagicCircle.cs`。魔法陣・鉄塔の塔体）、デッキの後付けは `AppendStep` が返す項目に `PrimeStep`（`PlayerCommandDispatcher.LatticeTower.cs`）
  - `ModifierEvaluator.cs` 冒頭の注記に「作った直後の控え」を足した
- 版の差分（`PlayerCommandDispatcher.Revision.cs`）：控えを「モデルの参照 → ID の集合」に変え、プロジェクトの全モデルから取る。同じモデル同士で比べ、新しく増えたモデルの ID は created、無くなったモデルの ID は deleted に入れる。版はカレントモデルのまま
- Undo の後の作り直し
  - `UndoStack.LastExecutedRecords`（その回に戻した・進めた記録の全部）を足し、`PerformUndo`・`PerformRedo` で入れる
  - `PolyLingPlayerViewerCore.Lifecycle.cs`：`needsRebuild`・`isReorder` を全記録で判定し、型の一覧に `MeshFilterToSkinnedRecord` を足した。`PivotMoveRecord`・`MeshSelectionChangeRecord` の枝は `lastRecord` のまま
- 試験用のシナリオ（`@settings/PolyLing/PolyLing/scenarios/stage9/`）：同じ流しの中で作成直後に編集する。`runScenario` の中ではきっかけ（`RebuildAdapter`）を挟まないので、照会で控えの取り違えが見える
  - `stage9_作成直後の編集`（`stage8_パイプの梯子化` の鎖＋HairRe の頂点 0 を動かす）
  - `stage9_作成直後の入力編集`（HairBelt を作った直後に入力 SrcPipe を動かす）
  - `stage9_魔法陣_作成直後の編集`（`stage8_魔法陣` ＋円周配置 MC_Radial の頂点 0 を動かす）
  - `stage9_鉄塔_デッキ後付け直後の編集`（`stage8_鉄塔` の元のデッキ Tw_Deck を動かして未確定にしてから、デッキ Tw_Deck2 を `addTowerDeck` で足し、その頂点 0 を動かす）

#### 決めた扱い

- 作成直後の控えはグループ・項目を足したコマンドがその場で取る。初めて見たときに控える扱い（`RefreshModel`・`MarkChangedInputs`・`IsOutputEditPending`）は、読み込み・Undo で現れた項目のために残す
- 既存のグループへ項目を足すときは、足した項目だけ控える。ほかの項目を控え直すと、未確定の出力の編集を正方向の結果として上書きする
- 版の差分で無くなったモデルの ID は deleted に入れる
- 一覧の Undo・Redo の作り直しの判定は、その回に戻した・進めた記録の全部で行う

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage9/`）

- 作った直後の編集
  - 変更前：`stage9_作成直後の編集` のあと HairRe の `pending`=0（作成直後の出力の編集を見落とす）。なお MCP の呼び出しを分けて `stage8_パイプの梯子化` のあとに HairRe を動かすと、間で画面の更新が走るので変更前でも `pending`=1 だった
  - 変更後：同じシナリオで HairRe の `pending`=1。`stage9_魔法陣_作成直後の編集` で MC#2（円周配置）の `pending`=1。`stage9_作成直後の入力編集` で HairBelt の `dirty`=1
- deletedObjectIds
  - 変更前：モデル 0 に直方体を置いたまま `stage8_パイプの梯子化` を流すと、deletedObjectIds に直方体の ObjectId が入った
  - 変更後：同じ手順で deletedObjectIds は無く、createdObjectIds は新しいモデルの 4 個だけ（`stage9_作成直後の編集`・`stage8_パイプの梯子化` とも）
- 画面が古いまま（`roboF_T5_SK_initialDebug_フリル` を h2 まで流した c2）
  - 変更後：引数の書き換え → Undo → `moveSelectedVertices`（0, 0.05, 0）で、パイプが上へ動いた形が画面に出た（`c2_fixed_01→02`）。ログは `needsRebuild=True`、`WritebackTransformedVertices` の警告なし
  - 引数の書き換え → Undo → Redo → `moveSelectedVertices` でも動いた形が出た（`c2_fixed_04→05`）。警告なし
- 検査：3 つの csproj のコンパイルはエラー 0。Unity での反映（`unity_validate_changes`）もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型）はすべて 0。Editor.log（c2 を確かめた Play の間）にエラーなし
- 鉄塔のデッキの後付け（`PrimeStep`）：`stage9_鉄塔_デッキ後付け直後の編集` のあと、Tw#2（足したデッキ）の `pending`=1、Tw#1（足す前に動かした元のデッキ）の `pending`=1 のまま（後付けで既存の項目を控え直していない）。Editor.log にエラー・警告なし
- 段 9 と関係なく見つかったことは 6 章の「段 9 から回したもの」へ回した

### 段 10：入力を読む空間の宣言 — 状態：済（2026-10-30）

- 範囲
  - MeshRef の入力ごとに読む空間（Local／Model）を宣言する（新しい宣言。例 `PLParam` に読む空間）。Model：平面化＋貼り付けの入力・参照面（`VertexToBindWorldMatrix`）、揺れ鎖の取り付け先（`BindWorldMatrix`）、頂点へ藤壺の頂点（`WorldMatrix`）。Local：梯子・原型・断面
  - バインド姿勢そのものの変化の検知：Model の入力は署名に行列を混ぜる（今の署名は頂点 ID と格納値の位置だけ。`MeshSignature.Positions`、`ModifierEvaluator.Take`）。書き込みのあるコマンドの終わりとオブジェクト移動のドラッグを離したときに、行列の署名を取り直す（今は Undo・Redo の後だけ取り直す）
  - スキンド化済みの入力：Model の入力はスキンドでも評価する（今は `ModifierSpace.Classify` が梯子以外のスキンドの入力を一律に外す）。Local の入力は、スキンド化のときに控えた元の ローカル→バインド の行列で読む（上の決めた扱い）
  - 作成時の梯子の行列：上の決めた扱いのとおり、梯子の行列を常に掛ける（作成は `PolyLingPlayerViewerCore.GeneratedMesh.cs` の `PrimitiveMeshCreateNewObject`・`BuildPrimitiveMeshContext`、作り直しは `ModifierSpace.TryComputeMatrix`）。段 8 の `StripPoseOwnedByOutput` と揃える
- 確かめ方
  - `stage8_まつげ眉`：球の BoneTransform を変える → 貼り付け → まつげ・眉が流れる。球をスキンド化したあとも流れる
  - `stage8_パイプの梯子化`：梯子を位置付きで作る → 作成直後の出力が梯子に重なり、作り直しても動かない
  - `barnacle_place`：一括スキンド化のあと、原型の編集で藤壺が流れる
- 完了の条件：照会の reasons から「形として読む入力がスキンド化済み」が消え、上の 3 つが流れる。保存 → 読み込みのあとも同じ

#### 調べて分かったこと（入力の読み方。コードで確かめた）

- Model（モデル空間で読む）
  - `createSurfacePaste` の入力・参照面：`VertexToBindWorldMatrix`（`PolyLingPlayerViewerCore.CreateCommands.cs` の `ResolveSurfacePasteForCommand`）
  - 揺れ鎖の梯子・取り付け先：`VertexToBindWorldMatrix`・`BindWorldMatrix`（`SpringBoneLadderPlacer.cs`）
  - `edgeRibbonFace.masterIndices`：GPU のワールド座標（`EdgeRibbonFaceToolHandler` → ツールの `GetWorldPositions`）
  - `createVertexBillboardPlace.masterIndices`：GPU のワールド座標・`WorldMatrix`（`VertexBillboardPlaceToolHandler.BuildCore`）
  - `applyBlend.sourceMasterIndices`：`VertexToWorldMatrix`（`BlendPreviewState.BlendVertices`）
- Local（格納値のまま読む）：梯子 `beltSourceIndex`（`ObjectGroupOps.RefreshBelts`・`BeltAcquire`）、断面 `profileSourceIndex`（`ProfileAcquire`）、原型 `sourceMasterIndices`（`ResolvePlaceSourcesForCommand` → `MeshSourceMultiPick`）、ケージ `cageMasterIndex`、`applyBlend.destMasterIndex`
- スキンド化の 2 経路はどちらも `WorldMatrix`（ポーズ込み）で頂点を焼いている（`MeshFilterToSkinnedConverter` Phase 4 の `savedWorldMatrices`、`SkinKindConverter.ToSkinned` の `VertexToWorldMatrix`）。控える行列はこの焼いた行列そのもの
- オブジェクト移動のドラッグを離すと `MoveObjectsCommand`・`RotateObjectsCommand` を `DispatchPanelCommand` へ送る（`ObjectMoveToolHandler.OnLeftDragEnd`、`PolyLingPlayerViewerCore.VertexInteraction.cs`）。書き込みのあるコマンドの終わりと同じ口を通る

#### 実装

- 宣言
  - `PLParamAttribute.cs`：enum `PLMeshRefSpace { Unspecified, Local, Model }` と `MeshRefSpace` を足した
  - `PanelCommandMeshRefs.cs`：`MeshRefKey.Space` を足した
  - 付け先：上の「調べて分かったこと」のとおり（`PanelCommand.PrimitiveBelt.cs`・`Primitive.cs`・`MagicCircle.cs`・`LatticeTower.cs`・`SpringBoneLadder.cs`・`Subdivision.cs`・`TopologyTargeted.cs`・`Blend.cs`）
  - `PanelCommandFactoryAudit.cs`：グループに残るコマンド（Capture が OnKeepAsGroup・Always）の入力キーで空間が未指定のものを数え、`queryCommandAudit` に出す（完了条件）
- 署名に行列（`ModifierEvaluator.cs`・`MeshSignature.cs`）
  - `MeshSignature.Matrix`（1e-5 に丸める）。`Sig.Matrix` を足した
  - `Entry.ModelInputs`・`LocalInputs`・`MatrixInputs`。行列を混ぜるのは `MatrixInputs`＝Model の入力と梯子（梯子はローカルで読むが、生成物へ梯子の 格納→バインド を掛けるので行列が結果に効く）
  - `MarkMatrixChanged`：Evaluate の項目の `MatrixInputs` の行列の署名だけを取り直し、違えば印を立てる。`RunModifierEvaluation` の先頭で `ComputeWorldMatrices` のあとに呼ぶ（`PlayerCommandDispatcher.ModifierEval.cs`）
- スキンド化済みの入力
  - `MeshObject.HasSkinBakeMatrix`・`SkinBakeMatrix`。`MeshObject.Clone`（2 か所）で写す
  - 控える：`MeshFilterToSkinnedConverter` Phase 4、`SkinKindConverter.ToSkinned`。下ろす：`SkinKindConverter.ToMeshFilter`
  - 保存：CSV（`skinBakeMatrix` 行。`CsvMeshSerializer.Write.cs`・`Read.cs`）、JSON（`MeshDTO.skinBakeMatrix`・`MeshGeoDTO.skinBakeMatrix`。`ModelSerializer.cs`）、バイナリ（PLRS v10。`RemoteProgressiveSerializer.cs`、受け側の写しは `CopySummaryAttributes`）
  - `ModifierSpace.LocalView`：スキンドで控えがあれば、その逆でローカルへ戻した複製を返す。原型（`ResolvePlaceSourcesForCommand`・パネルの `ResolvePlaceSources`）と断面（`ProfileAcquire.Acquire`）が通す。作成時も同じに通す
  - `ModifierSpace.Classify`：Model の入力はスキンドでも評価する。Local の入力（梯子以外）がスキンドで控えが無ければ外す。梯子を持つ項目は梯子があれば常に Transform。梯子を持たない項目で出力がスキンドなら外す
- 梯子の行列
  - `ModifierSpace.TryComputeMatrix(belt, output, poseOwnedByOutput)`：常に M を返す。M ＝（出力の枠の バインド→格納）×（梯子の 格納→バインド）。出力の枠は、新規オブジェクト・新規モデルとして作った項目では出力の親の `BindWorldMatrix`（出力自身の姿勢は入れない）、それ以外は出力の `VertexToBindWorldMatrix`。スキンドの出力は単位（`OutputFrameBindToVertex`）
  - `ComputeStepSpace`（`PlayerCommandDispatcher.ObjectGroup.cs`）：作ったときの追加先モードを項目の引数から読む（`IsPoseOwnedByOutput`。`StripPoseOwnedByOutput` と同じ判定）
  - 作成：`ExecuteCreatePrimitiveMesh` が梯子コマンドで新規オブジェクト・既存へ追加のとき `_createBeltSource` を立てる。`PrimitiveMeshCreateNewObject` は生成物へ梯子の `VertexToBindWorldMatrix` を掛ける。`PrimitiveMeshAddToExisting` は姿勢・位置を焼いたあとに M を掛ける（作り直しの `PrimitiveMeshReplaceExisting` と同じ順）
  - 行列を掛ける処理は `ModifierSpace.ApplyMatrix` にまとめた（`PrepareForOutput` も使う）
- 照会 `queryModifierGraph` の spaces の Transform の名前を「梯子の行列で出力の格納空間へ移して書く」にした
- 試験用のシナリオ：`@settings/PolyLing/PolyLing/scenarios/stage10/stage10_パイプの梯子化_位置付き`（`stage8_パイプの梯子化` の梯子に BoneTransform の位置 (0.3, 0.2, 0.1) を付け、HairRe の置く位置を外したもの）

#### 決めた扱い

- 入力キーごとに読む空間を宣言する。Model はモデル空間（バインド姿勢のワールド）で読む入力、Local は格納値で読む入力
- 署名の行列は `VertexToBindWorldMatrix`（ポーズを含まない）。ポーズだけを変える操作では流さない（段 5 の扱いのまま）
- 梯子は Local だが、行列の署名を取る（梯子の位置・回転を変えると、梯子を読む項目が流れて出力が付いてくる）
- スキンド化で控える行列が無いスキンド（PMX・VRM などで読み込んだもの）を Local の入力に持つ項目は、理由付きで外す
- 梯子を持たない項目で出力がスキンド化済みのものは外したまま（生成物を出力の格納空間へ移す読み方を持たない）
- 作成時の梯子の行列は、新規オブジェクトと既存へ追加で掛ける。新規モデルは掛けない（出力が別のモデルにあり、作り直しの対象にならない）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage10/`、保存は `SandBox/stage10/`）

- `stage8_まつげ眉`
  - 球の BoneTransform の PositionZ を 0.1 にすると、評価器が LashPasted → Lush → Brow を流した（Lush2・BrowFlat・BrowPipe は球を読まないので流れない）。LashPasted の頂点 0 の z が 0.4426 → 0.5426（`lash_bt_01_before_win` → `lash_bt_02_after_win`。この画面の向きでは形の変化は見えないので、値で確かめた）
  - 一括スキンド化のあと、出力の LashPasted・Brow・Lush・BrowPipe を MeshFilter へ戻し、スキンドの球の全頂点を z へ 0.05 動かすと、LashPasted → Lush が流れ、LashPasted の頂点 0 の z が 0.05 動いた。Lush は原型 LashProto（スキンド、Local）を `LocalView` で読んだ
  - 保存 → 読み込み：CSV（`SandBox/stage10/lash/`）では球の頂点の移動で LashPasted → Lush → Brow が流れた。CSV には球の `skinBakeMatrix`（z に 0.1）が書かれていた。.mfproj（`SandBox/stage10/proj/stage10_b.mfproj`）では球の BoneTransform の PositionZ を 0.2 にすると同じ 3 項目が流れ、頂点 0 の z が 0.5426 → 0.6426
- `stage10_パイプの梯子化_位置付き`
  - 作成直後の SrcPipe と HairRe の最初の輪は、梯子の最初の段のワールド位置（x 0.2〜0.4、y 0.2、z 0.1）に重なった（`pipe_belt_01_created`）
  - `rebuildObjectGroup`（HairBelt・HairRe）のあと HairRe・HairBelt の頂点の値は変わらない（`pipe_belt_02_rebuilt`）
  - HairBelt の BoneTransform の PositionX を 0.1 にすると HairRe が流れ、x が 0.1 動いた。.mfproj の読み込みのあと（`stage9_作成直後の編集` のモデル）でも同じに流れた
- `barnacle_place`
  - 一括スキンド化のあと、照会の reasons に「形として読む入力がスキンド化済み」は出ない。藤壺の出力（スキンド）は作り直しで失敗する（下の「段 10 と関係なく見つかったこと」の 1）。出力を MeshFilter へ戻して原型の円錐の先端を動かすと、藤壺が流れた
  - 原型の読み方：円錐の BoneTransform の PositionX を 1.0 にしてから、MeshFilter のまま先端を (0,0,0.35) にした藤壺の頂点 0 と、一括スキンド化（円錐の控えの行列に x 1.0）→ 藤壺の出力を MeshFilter へ戻す → 円錐の先端を (1,0,0.35) にした藤壺の頂点 0 が一致した（(0.18351, 0.40269, 0.04917)）
  - .mfproj（`SandBox/stage10/proj/stage10.mfproj`）の読み込みのあとも同じ値になった
  - `queryProjectSyncDiff`（via=csv・binary）に `skinBakeMatrix` の違いは出ない
- 段 1〜9
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随した。子への `deleteFaces` は出力の保護で拒否された
  - 逆方向：藤壺の出力全体を y 0.05 動かして `commitModifierOutputEdit` → 梯子 BT_Ball の 108 頂点が書き直され、ずれ最大 1.2e-7。Undo 1 回で梯子と藤壺が戻った
  - 作成直後の pending：`stage9_作成直後の編集` で HairRe の `pending`=1、`stage9_作成直後の入力編集` で HairBelt の `dirty`=1、`stage9_魔法陣_作成直後の編集` で MC#2 の `pending`=1
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型・読む空間の未指定）はすべて 0。Editor.log にエラーなし

#### 段 10 と関係なく見つかったこと（6 章へ回した）

- 藤壺（`createPlaceObject`）・まつげ（`createLush`）は梯子のウェイトを引き継がない。パイプ・フリルは `InheritBeltWeights` のときだけ引き継ぐ（`PrimitiveMeshFactory.cs` の `ResolveBeltSource`）。一括スキンド化のあと出力がスキンドのこれらの項目は、段 5 の扱い（ウェイトの無い頂点があれば失敗）で作り直しが失敗する
- バイナリの頂点の位置が、`setVertexPositions`・`moveSelectedVertices` のあとの頂点の位置と違う。`queryProjectSyncDiff` via=binary で位置の違いが出て、`loadProjectBinary` のあとの円錐の先端は編集の前の値だった。書いているのは `MeshObject.Positions`（`RemoteBinarySerializer.cs` の `WritePositions`）。原因は調べていない。同じ照会で法線の要素数の違い（A=0・B=1）も出た
- `queryProjectSyncDiff` via=csv で、一括スキンド化で作ったボーンの objectId が保存の JSON では 0、CSV の往復では採番されていた。`convertMeshFilterToSkinned` の応答の createdObjectIds も [0]。原因は調べていない

### 段 11：シナリオとグループの整合 — 状態：済（2026-10-30）

- 前提（2026-10-30 にコードで確かめた）
  - シナリオとグループは同じ型（`ObjectGroup`・`ObjectGroupStep`）で、違いは置き場と参照の埋まり方だけ（`ObjectGroup.cs` 冒頭の「手順の知識として使う」、`ScenarioLibrary.cs` 冒頭の「型は分けない」）
  - 生成の中身はどちらもコマンド（Action＋Args）で、段 1〜10 の宣言（読む空間・逆方向・記録の種類・出力の保護）はコマンド側に付くので、シナリオから流しても同じに効く。流すと、グループとして残す生成項目は `CaptureObjectGroup` でグループになる
  - 両者の違いは本来「グループは上流と下流が密でリアルタイムに反映する／シナリオは比較的疎で処理時間のかかる処理が多い」だけのはずだが、次の 3 点はそれでは説明できない乖離になっている
- 範囲
  1. つながりの表し方を相互に変換できるようにする
     - グループは `MeshRefIds`（ObjectId）でつながり、依存は `ModifierGraph` が全グループから導く。シナリオは Args の `@prev`・`@<項目 ID>`（流したときの戻り値）でつながり、`MeshRefIds` は空（`PanelCommand.Scenario.cs` 冒頭の「シナリオは実体に縛らない」）
     - `saveScenarioFromGroup` は `MeshRefIds`・`OutputObjectIds` を消すだけで `@` 参照へ置き換えない（`PlayerCommandDispatcher.Scenario.cs` の `RunSaveScenarioFromGroup`）。項目が 2 つ以上のグループから作ったシナリオは項目同士がつながらない
     - 同じグループの前の項目の出力を指す参照は `@<項目 ID>.masterIndices` に書き換える。グループの外を指す参照の扱いは段の始めにコードを読んで決める
  2. 流しの項目の区切りで評価器を通す
     - 評価器が流れるのは一番外側の、書き込みのあるコマンドの終わりだけ（`PlayerCommandDispatcher.cs` の `DispatchAuthorized`、`PlayerCommandDispatcher.ModifierEval.cs` の `ShouldEvaluateModifiersAfter`）。`runScenario` は `Writes = PLWriteScope.None` で、中の項目は入れ子の Dispatch なので、流している途中も流し終えたあとも評価器が流れない。流しの中ではきっかけ（`RebuildAdapter`）も挟まない（段 9 の実装の注記）
     - 同じコマンドの並びでも、1 つずつ撃つかシナリオで流すかで結果が変わる。書き込みのある項目を実行したあとに、署名を取り直してから評価する
  3. `ObjectGroup.cs` 冒頭の「項目の間に依存グラフは持たない。実行の順はリストの並びそのもの」は段 4 以降と食い違っているので直す
- 確かめ方（段の始めに改めた。外の入力が無い空のモデルでは流せないため）
  - `stage9_作成直後の入力編集` を流し終えたあと、HairBelt の `dirty`=0 で、下流が作り直されている
  - `stage8_辺パイプ` のグループ（`createEdgePipe` の 2 項目）を `saveScenarioFromGroup` でシナリオにし、同じモデルで既定値のまま流すと、新しくできた項目の action・入出力のつながり・状態が元と一致する（グループ名は比べない）
  - `stage8_パイプの梯子化` の HairBelt・HairRe を `mergeObjectGroup` で 1 つにしてシナリオにすると、HairRe の梯子が `@` 参照、SrcPipe が引数になる。入力を変えて流すと出力がそれに追随する
  - stage8・stage9・stage10・`barnacle_place`・`roboF_T5_SK_initialDebug_フリル` が最後まで流れ、照会の状態と出力の頂点 0 の `getRawData` が変更前と一致する
- 完了の条件：上の 4 つが通る

#### 調べて分かったこと

- MeshRef のキーは今の索引で引き直せる（`ObjectGroupOps.RewriteMeshRefs`。モデル索引のキーも扱う）。外の参照の既定値はこれで取れる
- 生成コマンドは出力を `MasterIndices[0]` で返す（例 `PlayerCommandDispatcher.EdgePipe.cs` の帯・パイプ）。同じグループの前の項目の出力は `@<項目 ID>.masterIndices` で引ける
- `mergeObjectGroup` は項目を `Clone()` で写し、`AddStep` は ID が空のときだけ振るので、まとめたグループでは項目 ID が重なる（HairBelt＋HairRe はどちらも e1）
- グループの作り直しは梯子・断面の点列を取り込み元から読み直す（`BuildCommand` の `RefreshBelts`・`RefreshProfile`）が、シナリオの項目は Args の点列をそのまま撃っていた。グループから写したシナリオの点列は写した時点の控えなので、外の入力を引数で替えて流すと点列が古いまま残る
- `stage9_作成直後の入力編集` の変更前：HairBelt の `dirty`=1（流し終えても評価器が流れない）

#### 実装

- `ObjectGroupOps.cs`
  - `BuildScenarioSteps`：グループの項目をシナリオの項目へ写す。写す前に `EnsureScenarioItemIds`。MeshRef のキーごとに、前の項目の出力（1 個）→ `@<項目 ID>.masterIndices`、グループの外 → シナリオの引数 `<項目 ID>_<キー>`（既定値は今の索引。モデル索引のキーも同じ）、0 だけ → そのまま。内と外が混じる・出力が複数の項目を指す・モデル索引付きの参照が内を指す、は断る。`MeshRefIds`・`OutputObjectIds` は空にする
  - `RefreshInputs`：`RefreshBelts`・`RefreshProfile` をまとめた公開の口。`BuildCommand` もこれを使う
- `PlayerCommandDispatcher.Scenario.cs` の `RunSaveScenarioFromGroup`：`BuildScenarioSteps` の項目と引数で登録する。応答に `params`。`PanelCommand.Scenario.cs` の説明と結果の宣言も直した
- `PlayerCommandDispatcher.ScenarioRun.cs` の `TryExecuteRunStep`
  - コマンドを組む前に `RefreshInputs` を掛ける（取り込み方が Baked・取り込み元が -1 なら書いてある点列のまま）
  - 項目が成功し、そのコマンドが書き込みありなら `RunModifierEvaluation("シナリオ <名前>/<項目 ID>", recheckInputs: true)`
- 注記：`ObjectGroup.cs` 冒頭（依存の順は `ModifierGraph` が導く、つながりの表し方と相互の変換）、`PlayerCommandDispatcher.ScenarioRun.cs` 冒頭、`PlayerCommandDispatcher.ModifierEval.cs` 冒頭（いつ流すか）、`PlayerCommandDispatcher.Scenario.cs` 冒頭
- 試験用のシナリオ（`@settings/PolyLing/PolyLing/scenarios/stage11/`）：`stage11_辺パイプ_グループから`・`stage11_パイプの梯子化_グループから`（どちらも `saveScenarioFromGroup` で作った）

#### 決めた扱い

- シナリオは疎なグループとして扱う。流すときの項目は、グループの項目と同じに入力を読み直し、書き込みのある項目のあとに評価器を流す。違いは密か疎か・処理時間だけにする
- グループからシナリオへ写すとき、グループの外の参照はシナリオの引数にする（グループの入力＝疎なグループの入力）
- シナリオからグループへは流すことで戻る。流すと項目ごとにグループになる（`createEdgePipe` のように 1 グループへまとめる処理はシナリオに写らない）
- 書いてある点列と取り込み元の両方を持つ項目は、取り込み元を正とする（作り直しと同じ）

#### 確かめた結果（MCP で Play）

変更前の値は、変更を入れたまま新しい動きだけを一時に止めて取った（同じ Play の手順で、モデル番号も同じ）。
- `stage9_作成直後の入力編集`：変更前 HairBelt `dirty`=1 → 変更後 `dirty`=0、`lastPass` は「シナリオ stage9_作成直後の入力編集/e5: 作り直し 1」
- `stage8_辺パイプ` のグループから写したシナリオ：引数は `e1_masterIndices`（EdgeSrc、既定値 0）の 1 つ、帯 → パイプは `@e1.masterIndices`。同じモデルで流すと、新しい項目 EdgeRibbon_1#0（edgeRibbonFace、入力 EdgeSrc）→ EdgePipe_1#0（createPipe、入力 EdgeRibbon_1）が元の 2 項目と同じ action・つながり・状態（Evaluate）になり、出力の頂点 0・1 も元と一致した
- `stage8_パイプの梯子化` の HairBelt・HairRe をまとめて写したシナリオ：項目 ID は e1・e2 に振り直され、HairRe の `beltSourceIndex` は `@e1.masterIndices`、SrcPipe は引数 `e1_sourceMasterIndices` になった
  - `stage9_作成直後の入力編集` のモデルで流すと、HairBelt_1（入力 SrcPipe）→ HairRe（入力 HairBelt_1）がつながった
  - SrcPipe のレールの頂点 10 個を z 0.2 へ動かして流し直すと、新しい HairRe_1 の頂点 1 の z は 0.2835（写した時点の点列のままなら 0.0835）。梯子が取り込み元から読み直された。先に作った HairBelt・HairBelt_1・HairRe も評価器で追随した
- 既存のシナリオ（変更前と同じ順に同じ Play で流した）
  - stage8 の 8 本・stage9 の 4 本・stage10：照会の状態・`dirty`・`pending`・出力の頂点 0 がすべて変更前と一致（`stage9_作成直後の編集` の HairRe `pending`=1、`stage9_鉄塔_デッキ後付け直後の編集` の Tw#1・Tw#2 `pending`=1、`stage9_魔法陣_作成直後の編集` の MC#2 `pending`=1 も同じ）
  - `barnacle_place`：BT_Barnacle の頂点は変更前と一致。退避 BT_Barnacle_stash は変更前と違う（変更前は入力の編集前の形、変更後は作り直した形）。入力の編集（e26・e27）のあとに評価器が藤壺を作り直し、そのあとの `rebuildObjectGroup` が作り直した形を退避したため。項目を 1 つずつ撃ったときと同じ動き
  - `roboF_T5_SK_initialDebug_フリル`（c7 の保存の前まで）：照会の 4 項目の状態・結果・説明が変更前と一致
- 段 1〜10
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随（子の頂点 0 の z が -0.005 → 0.095）。子への `deleteSelection` は出力の保護で拒否された
  - 逆方向：藤壺の出力全体を y 0.05 動かして `commitModifierOutputEdit` → 梯子 BT_Ball の 108 頂点を書き直し（ずれ最大 1.2e-7）。Undo 1 回で藤壺の頂点 0 の y が 0.444 → 0.394 に戻った
  - 作成直後の pending：上の stage9 の 3 本で変わらず
  - 入力の BoneTransform：`stage8_まつげ眉` で球の PositionZ を 0.1 にすると LashPasted の頂点 0 の z が 0.4426 → 0.5426
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目はすべて 0。Editor.log にエラーなし

#### 段 11 と関係なく見つかったこと（6 章へ回した）

- `roboF_T5_SK_initialDebug_フリル` の c7（`saveProjectCsv`）が、別のモデルの未確定の出力の編集（stage9 のモデルの HairRe）で失敗した。変更前も同じ
- `barnacle_place` の注意の項目（e25・e29）は「入力を編集しても出力は古いまま／編集のたびに印を立てるフックは置いていない」と書いているが、段 4 以降は入力の編集で評価器が流れる

### 段 12：Undo の履歴 — 状態：済（2026-10-30）

- 範囲
  - 原因：`MeshUndoController.SetMeshObject` と `SetMeshObjectFor` が、呼ばれるたびに頂点編集の履歴を消す（`_vertexEditStack.Clear()`）。`setVertexPositions` も書く前に `SetMeshObject` を呼ぶ（`PlayerCommandDispatcher.MeshAttributes.cs`）。履歴の間は時系列で調停している（OperationLog）ので、消された記録の分だけ一覧の履歴へ進む
  - 頂点編集の履歴に積む記録の型を洗い出し、それぞれが自分の書き込み先を持つようにしてから、消すのをやめる
- 確かめ方
  - `stage8_ブレンド`：BlA を編集 → BlC を編集 → Undo 2 回で両方戻り、グループが残る
  - `stage8_断面系`：折れ線の編集 → `params.radialSegments` の書き換え → Undo 2 回で両方戻る
  - 段 6 のパイプの確定 → Undo 2 回で、その前の頂点の編集まで戻る
- 完了の条件：上の 3 つが時系列どおりに 1 回ずつ戻る

#### 調べて分かったこと

- 消している所は `MeshUndoController.cs` の SetMeshObject・SetMeshObjectFor の 2 か所（LoadMesh の Clear は読み込み直しなので残す）。呼び出しは 19 ファイル・39 か所（範囲に書いた「16 ファイル・27 か所」は数え違い）。どれも記録の前に書き込み先を設定するだけで、消すことに頼っていないので、呼び出し元は変えない
- 頂点編集の履歴に積む記録の型と書き込み先
  - `MeshSnapshotRecord`：`MeshObjectSnapshot.SourceMeshContext`（参照）を持っていた。ただし一覧のスナップショットの Undo（`MeshFilterToSkinnedRecord.RestoreList` など）は描画オブジェクトを複製に置き換えるので、参照は置き換え前のオブジェクトを指し、Undo はそちらへ書いていた。材質は書き込み先のモデルでなく `ctx.ParentModelContext`（今のモデル）へ戻していた
  - `MeshUndoRecord.PartsSetsBackup.Owner`（パーツ選択辞書の控え）：参照。上と同じく古くなる
  - `MultiMeshVertexMoveRecord`・`MultiMeshSelectionChangeRecord`：索引だけ。モデルも持たず、Undo の時点の `ctx.ParentModelContext` で引いていた（`PolyLingPlayerViewerCore.Lifecycle.cs`）。一覧の Undo で並びが変わると別のオブジェクトを指す
  - `SelectionChangeRecord`：持たない。Undo の時点の `ActiveMeshContext` へ戻していた
  - `AddFaceOperationRecord`：持たない。Undo の時点の `ctx.MeshObject`（先頭の選択）へ書いていた。記録側は `ctx.ActiveMeshObject` を編集する（`AddFaceTool.cs`）
  - 積まれない型（生成する所が無い）：`VertexMoveRecord`・`VertexGroupMoveRecord`（`EndVertexDragCommand`・`RecordVertexGroupMoveCommand` を作る所も、`EndVertexDrag`・`RecordVertexGroupMove` の呼び出しも無い）、`VertexUVChangeRecord`・`BoneWeightChangeRecord`（new する所が無い）、`FaceAddRecord`・`FaceDeleteRecord`・`VertexAddRecord`・`KnifeCutOperationRecord`（`RecordFaceAdd` などの呼び出し元が無い）。手を入れていない
- Player の Undo の後処理（`Lifecycle.cs` の OnUndoRedoPerformed）は、スナップショットの記録で作り直す相手を `ctx.MeshObject`（今の選択）から逆引きしていた。書き込み先が選択と違うと、書いたオブジェクトの表面メッシュが作り直されない
- 変更前の再現（同じ Play、キャプチャは `SandBox/Captures/stage12/*_before_*`）
  - `stage8_ブレンド`：BlA（y 0.5→0.9）→ BlC（y 0.75→1.2）→ Undo 2 回で、BlC は戻ったが BlA は 0.9 のまま、2 回目でブレンドのグループが消えた（`queryModifierGraph` の entries=0）
  - `stage8_断面系`：ProfLine の頂点 3（x 0.3→0.4）→ Rev の `params.radialSegments`=8（66→34 頂点）→ Undo 2 回で、Rev は 66 頂点に戻ったが ProfLine は 0.4 のまま
  - 藤壺（`barnacle_place`）：出力 BT_Barnacle の頂点 0 を `setVertexPositions` で y 0.394→0.444 → `commitModifierOutputEdit` → Undo 2 回。1 回目（確定の Undo）で 0.444・未確定に戻り、2 回目の頂点編集の Undo では 0.444 のまま（書き込み先の参照が確定の Undo で置き換えられたオブジェクトを指していた）

#### 実装

- 新規 `Core/Records/MeshUndoTarget.cs`：書き込み先（MeshContext・ModelContext・ObjectId）。Undo・Redo の時点で「モデルの一覧に同じ参照 → ObjectId で引く → 無ければ見つからない」の順に引き直す。複数メッシュの要素用に `ResolveIndex(model, index, objectId)`
- `MeshUndoController.cs`：SetMeshObject・SetMeshObjectFor の `_vertexEditStack.Clear()` を消した
- `MeshUndoContext.cs`：後処理への受け渡し口を足した（`AppliedMeshContexts`＝スナップショット・面追加の記録が書いた相手、`CurrentSelectionTarget`、`PendingTargetModel`）
- `MeshObjectSnapshot.cs`：`SourceMeshContext` を `Source`（MeshUndoTarget）に替え、全 Capture で控える。適用（`WriteTo`）は引き直した相手へ書き、見つからなければ何も書かない（選択も戻さない）。材質は書き込み先のモデルへ戻す。書いた相手を `AppliedMeshContexts` へ入れる
- `MeshUndoRecord_Base.cs`：`PartsSetsBackup.OwnerTarget`、`MeshMoveEntry.ObjectId`、`MultiMeshVertexMoveRecord.Model`。適用時に ObjectId から今の索引へ引き直し（一覧に無ければ -1 で後処理が飛ばす）、モデルを `PendingTargetModel` へ
- `MeshUndoRecord_Selection.cs`：`SelectionChangeRecord.Target`（引き直して `CurrentSelectionTarget` へ。控えてあるのに見つからなければ戻さない）、`MeshSelectionEntry.ObjectId`、`MultiMeshSelectionChangeRecord.Model`（上と同じに引き直す）
- `MeshUndoRecord_Topology.cs`：`AddFaceOperationRecord.Target`。引き直した相手の MeshObject を書き、相手を `AppliedMeshContexts` へ。`ApplyToMesh` は書き込み先が ctx の解決先と同じときだけ
- `MeshUndoStack.cs`：`FillTargets`（OnRecording）が記録側の渡していない書き込み先を記録の時点で詰める。複数メッシュの記録はその時の `ParentModelContext` と各要素の ObjectId、`SelectionChangeRecord` はその時の `ActiveMeshContext`、`AddFaceOperationRecord` はその時の解決先。パーツ選択辞書の控えの持ち主も控え、OnUndone・OnRedone で引き直す
- `MeshUndoController_Topology.cs` の `RecordAddFaceOperation` に書き込み先の引数を足し、`AddFaceTool.cs` から `ctx.ActiveMeshContext` を渡す
- `PolyLingPlayerViewerCore.Lifecycle.cs`（後処理）：複数メッシュの記録は `PendingTargetModel` で索引を引く。単一の選択は `CurrentSelectionTarget` へ戻し、画面の選択は今の `ActiveMeshContext` のもので塗り直す。`AppliedMeshContexts` があればその相手の表面メッシュを作り直し（`ReplaceUnityMesh`・ミラーの法線・`EnterUndoApplied`）、無い記録は今までの逆引きのまま

#### 決めた扱い

- 履歴は今の 2 本（一覧・頂点編集）のまま。書き込み先が替わっても頂点編集の履歴を消さない。順序は今の時系列の調停（OperationLog）に任せる
- 頂点編集の記録は、Undo・Redo の時点の選択や解決先ではなく、記録したときの相手（参照・モデル・ObjectId）へ書き戻す。相手が消えていれば書かない（別のオブジェクトへ書かない）
- 記録側が書き込み先を渡さない記録は、記録の時点の状態から控える（今まで Undo の時点にしていた同じ仮定を、記録の時点へ移した）
- 積まれない記録の型は直さない

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage12/`）

- `stage8_ブレンド`：BlA（y 0.5→0.9）→ BlC（y 0.75→1.2）→ Undo 1 回で BlC が 0.75、2 回目で BlA が 0.5 に戻り、グループ BlC_backup#0 は残った（`blend_after_00→01`）。Redo 2 回で BlA 0.9・BlC 1.2 に進んだ（`blend_after_02`）
- `stage8_断面系`：ProfLine の頂点 3（x 0.3→0.4、Rev が追随）→ Rev の `params.radialSegments`=8（34 頂点）→ Undo 1 回で Rev が 66 頂点、2 回目で ProfLine が 0.3 に戻り Rev も追随して 0.3（`prof_after_00→01`）。Redo 2 回で ProfLine 0.4・Rev 34 頂点（`prof_after_02`）
- 逆方向の確定（藤壺）：出力の頂点 0 を `setVertexPositions` で y 0.394→0.444 → 確定（0.397）→ Undo 1 回で 0.444・未確定（`pending`=1、Blocked）、2 回目で 0.394・`pending`=0（`commit_after_00→02`）。Redo 2 回で 0.444（未確定）→ 確定後の 0.397（`commit_after_03`）
- 段 1〜11
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随（子の頂点 0 の z が -0.005 → 0.095）。子への `deleteSelection` は出力の保護で拒否。移動の Undo で子が -0.005 に戻った
  - 逆方向の確定と Undo 1 回：藤壺の出力全体を y 0.05 動かして確定 → 梯子 BT_Ball の頂点 20 の y が 0.3155 → 0.3655、Undo 1 回で梯子が 0.3155・藤壺の頂点 0 が 0.444 → 0.394 に戻った（段 10・11 と同じ）
  - 作成直後の pending：`stage9_作成直後の編集` で HairRe の `pending`=1、`stage9_魔法陣_作成直後の編集` で MC#2 の `pending`=1
  - 一覧のスナップショットの Undo のあとの頂点移動：藤壺の `params.scale` の書き換え → Undo → 出力を `moveSelectedVertices`（y 0.3）で、動いた形が画面に出た（`s9_listundo_00→01`）。`WritebackTransformedVertices` の警告なし
  - 入力の BoneTransform：`stage8_まつげ眉` で球の PositionZ を 0.1 にすると LashPasted の頂点 0 の z が 0.4426 → 0.5426
  - シナリオ：`stage9_作成直後の入力編集` のあと HairBelt の `dirty`=0。`stage11_パイプの梯子化_グループから`（そのモデル）で HairBelt_1 → HairRe がつながり状態は Evaluate、`stage11_辺パイプ_グループから`（`stage8_辺パイプ` のモデル）で EdgeRibbon_1 → EdgePipe_1 がつながった
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型・読む空間の未指定）はすべて 0。Editor.log（2 回の Play の間）にエラーなし

#### 段 12 と関係なく見つかったこと（6 章へ回した）

- 藤壺の出力全体を `moveSelectedVertices` で y 0.05 動かして確定し Undo 1 回すると、出力は編集した形（未確定）ではなく、確定前の入力に対する正方向の形に戻る。変更前も同じ。原因は 6 章の「段 12 から回したもの」に書いた（`moveSelectedVertices` ではなく、確定で丸められない編集だったことによる）

### 段 13：ブレンドの作り直し — 状態：済（2026-10-30）

- 範囲（上の決めた扱い）
  - 基準の形を読む入力キーを足す。書き戻しの印（`RebuildRole`：`DestMasterIndex` を TargetIndex、`CreateNewObject` を TargetMode=false）
  - `BlendOperation.ApplyBlend` に、基準を入力から読み控えを作らない分岐を足す
  - 記録のあとに項目の MeshRef（基準・出力）を差し替える。今は控え `_backup` が出力として記録され、宛先は入力に並ぶ（`CaptureObjectGroup` が増えた ObjectId の最大を出力にするため）
- 確かめ方：`stage8_ブレンド`（宛先を書き換える設定と、複製を作る設定の両方）。BlA の編集で流れる → Undo 1 回 → 重みの書き換え → 保存 → 読み込みのあとの編集
- 完了の条件：照会の状態が Evaluate。作り直しを繰り返しても結果が同じ（混ぜた結果へ重ねて混ぜない）。出力の保護が宛先に掛かる

#### 調べて分かったこと

- 受け口（`PlayerCommandDispatcher.Blend.cs` の `ApplyBlendCommand`）は `BlendPreviewState.Start` で宛先の今の位置・法線を控え（`Backup`・`NormalBackup`）、`BlendOperation.ApplyBlend` がそれを基準に混ぜる。宛先を書き換える設定では基準の複製 `<宛先>_backup` を足し、複製を作る設定では `<宛先>_blend` を足して書く
- 記録は `CaptureObjectGroup(c, 前の ObjectId, 複製なら -1・書き換えなら宛先)`。増えた ObjectId の最大を出力にするので、書き換える設定では控え `_backup` が出力になり、fallback の宛先は使われなかった
- `RebuildRole` が無いので `ModifierInventory.InputKeys` は宛先も入力に数え、照会は Excluded（「RebuildRole が無い」）だった
- 合成は `BlendPreviewState.BlendVertices`。ソースは `VertexToWorldMatrix` でワールドへ出し宛先の `WorldToVertexMatrix` で戻す。基準（backup）は宛先の格納空間のまま混ぜる（読む空間は Local）
- 選択頂点だけ（`SelectedVerticesOnly`）は書き込み先の今の選択を読む。選択は引数に残らない
- 保存の読み込みは 3 経路（`ObjectGroupCsv.cs` の `CloseGroup`、`ObjectGroupDTO.cs` の項目、`RemoteProgressiveSerializer.cs` の version 4 以降と version 3）
- 変更前（同じ手順、`SandBox/Captures/stage13/inplace_before_*`）
  - `stage8_ブレンド`：照会は BlC_backup#0、入力 BlA・BlB・BlC、出力 BlC_backup、Excluded。BlA の頂点 0 を y 0.5 → 0.9 にしても BlC の頂点 0 は y 0.75 のまま（`dirty`=1）
  - 複製の設定（下の `stage13_ブレンド_複製`）：照会は BlC_blend#0、入力 BlA・BlB・BlC、出力 BlC_blend、Excluded
  - この 2 つを旧形の保存として残した（`SandBox/stage13/old/` の .mfproj と CSV）

#### 実装

- `PanelCommand.Blend.cs`
  - `BaseMasterIndex`（int、既定 -1。`IsMeshRef`・Read・`MeshRefSpace = Local`）を足した。0 以上なら基準をその描画オブジェクトの頂点の位置・法線から読み、控えを作らない。コンストラクタの末尾に `baseMasterIndex = -1`
  - `DestMasterIndex` に `RebuildRole = TargetIndex`、`CreateNewObject` に `RebuildRole = TargetMode`（`RebuildModeValue = "false"`）。入力はソースと基準、出力は書き込み先になり、宛先は入力に並ばない
  - クラスの注記に基準と作り直しの形を書いた
- `BlendOperation.ApplyBlend`：引数 `MeshContext baseCtx`・`out int baseIndex` を足した
  - `baseCtx` があれば基準の位置・法線を控えの代わりに使い、書き込み先を決めて Undo の before を取ったあと、書き込み先を基準の形へ戻してから混ぜる（選択外・対応の取れない頂点も基準の形になる）。宛先を書き換える設定でも控えを作らない。頂点数が違えば -1
  - 宛先はどちらの場合もプレビュー開始時の形（`preview.Backup`）へ戻す（複製の設定で元の宛先を基準で書き換えない）
  - `baseIndex`：渡した基準、渡さないときは書き換える設定なら控え、複製の設定なら元の宛先
  - 記録を止めているとき（作り直し）は頂点編集・一覧の Undo を積まない（`PrimitiveMeshReplaceExisting` と同じ判定）
  - 基準を渡したときは書き込み先の表示も元に戻す（作り直しのたびに隠した出力が表示に戻らない）
- 受け口（`PlayerCommandDispatcher.Blend.cs`）
  - `BaseMasterIndex` を引き、無い・頂点数が違えば理由付きで失敗
  - `keepAsGroup` と `selectedVerticesOnly` が両方立っていれば断る
  - `ApplyBlend` の失敗を Fail にした（プレビューを終えて隠したソースの表示を戻す）
  - グループとして残すとき、基準＝`baseIndex`・宛先＝書き込み先にしたコマンドを組み直して `CaptureObjectGroup` へ渡し、出力は `explicitOutputIds` で書き込み先の ObjectId を渡す（記録のあとに差し替えるのではなく、記録する前に正しい形にする。循環の判定と `PrimeGroup` も正しい形で通る）
- 旧形の変換：`ObjectGroupOps.UpgradeLegacyBlendRefs`（項目・グループ）。applyBlend で基準のキーが無く、出力と宛先が 1 個ずつの項目を直す。書き換える設定は 基準＝旧出力・宛先＝出力＝旧宛先、複製の設定は 基準＝旧宛先・宛先＝旧出力。呼ぶ所は `ObjectGroupCsv.cs`・`ObjectGroupDTO.cs`（`UpgradeLegacyProfileArgs` の隣）、`RemoteProgressiveSerializer.cs`（version 4 以降と version 3）
- 注記：`PanelCommandMeshRefs.cs` 冒頭
- 試験用のシナリオ：`@settings/PolyLing/PolyLing/scenarios/stage13/stage13_ブレンド_複製`（`stage8_ブレンド` の e4 に `createNewObject=true`）

#### 決めた扱い

- 基準から読むのは頂点の位置と法線。面構成は書き込み先のまま。基準と書き込み先の頂点数が違えば作り直しは失敗する
- 選択頂点だけのブレンドはグループとして残さない（断る）
- 段 13 より前に保存したブレンドのグループは、読み込みのときに今の形へ直す
- 6 章の「段 10 から回したもの」のうちブレンドがポーズ込みの座標でソースを読む件は、段 13 では変えていない（6 章に残した）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage13/`、保存は `SandBox/stage13/`）

- 宛先を書き換える設定（`stage8_ブレンド`）
  - 照会：BlC#0、入力 BlA・BlB・BlC_backup、出力 BlC、Evaluate
  - BlA の頂点 0 を y 2.0 にすると BlC の頂点 0 が 0.75 → 1.5（`inplace_after_00 → 01_editA`、`lastPass` は「setVertexPositions: 作り直し 1」）
  - `rebuildObjectGroup` を 2 回流しても BlC は 1.5 のまま、オブジェクトは 4 個のまま（控えが増えない）
  - BlA を y 3.0（BlC 2.0）→ Undo 1 回で BlA 2.0・BlC 1.5
  - `sourceWeights`=1,0 で BlC が 2.0（BlA と同じ。`inplace_after_02_weights10`）。0.5,0 にして基準 BlC_backup の頂点 0 を y 0 にすると BlC が 1.0（0.5×0＋0.5×2）
  - BlC への `setVertexPositions`・`deleteSelection` は「出力は編集できません」で拒否
  - 保存 → 読み込み：.mfproj・CSV とも照会は同じ形で Evaluate。読み込みのあと BlA の編集で BlC が流れた（3.0 で 1.5、4.0 で 2.0。重み 0.5,0・基準 0）。CSV の objectgroups には `baseMasterIndex` の参照が書かれる
- 複製を作る設定（`stage13_ブレンド_複製`）
  - 照会：BlC_blend#0、入力 BlA・BlB・BlC、出力 BlC_blend、Evaluate
  - BlA の頂点 0 を y 2.0 にすると BlC_blend が 1.5、元の BlC は 0.5 のまま。`rebuildObjectGroup` 2 回でも 1.5
  - BlA を y 3.0 → Undo 1 回で BlC_blend 1.5
  - `sourceWeights`=0,1 で BlC_blend が 1.0（BlB と同じ）
  - BlC_blend への `setVertexPositions`・`deleteSelection` は拒否
  - .mfproj の保存 → 読み込みのあと、BlB を y 3.0 にすると BlC_blend が 3.0
- 旧形の読み込み（変更前に保存した 4 つ）
  - 書き換える設定（.mfproj・CSV）：照会は 入力 BlA・BlB・BlC_backup、出力 BlC、Evaluate。BlA を y 2.0 にすると BlC が 1.5
  - 複製の設定（.mfproj・CSV）：照会は 入力 BlA・BlB・BlC、出力 BlC_blend、Evaluate。BlA を y 2.0 にすると BlC_blend が 1.5、BlC は 0.5
- 選択頂点だけ＋グループとして残すは理由付きで断られた
- 段 1〜12
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随（子の頂点 0 の z が -0.005 → 0.095）。子への `deleteSelection` は出力の保護で拒否
  - 逆方向の確定と Undo 1 回：藤壺の出力全体を y 0.05 動かして確定 → 梯子 BT_Ball の頂点 20 の y が 0.3155 → 0.3655（ずれ最大 1.2e-7）、Undo 1 回で 0.3155・藤壺の頂点 0 が 0.394（段 12 と同じ）
  - 作成直後の pending：`stage9_作成直後の編集` で HairRe の `pending`=1、`stage9_魔法陣_作成直後の編集` で MC#2 の `pending`=1
  - 一覧のスナップショットの Undo のあとの頂点移動：藤壺の `params.scale` の書き換え → Undo → 出力を `moveSelectedVertices`（y 0.3）で、動いた形が画面に出た（`s9_listundo_00→01`）。`WritebackTransformedVertices` の警告なし
  - 入力の BoneTransform：`stage8_まつげ眉` で球の PositionZ を 0.1 にすると LashPasted の頂点 0 の z が 0.4426 → 0.5426
  - シナリオ：`stage9_作成直後の入力編集` のあと HairBelt の `dirty`=0。`stage11_パイプの梯子化_グループから`（そのモデル）で HairBelt_1 → HairRe がつながり Evaluate、`stage11_辺パイプ_グループから`（`stage8_辺パイプ` のモデル）で EdgeRibbon_1 → EdgePipe_1 がつながった
  - Undo 2 回：`stage8_断面系` で ProfLine の頂点 3（x 0.3→0.4）→ Rev の `params.radialSegments`=8（34 頂点）→ Undo 2 回で Rev 66 頂点・ProfLine 0.3。藤壺の出力の頂点 0 を y 0.394→0.444 → 確定 → Undo 1 回で 0.444、2 回目で 0.394
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型・読む空間の未指定・WriteWhen の不正）はすべて 0。Editor.log（変更後の 3 回の Play の間）にエラーなし

### 段 14：上流への逆方向 — 状態：済（2026-10-30）

- 範囲
  - 逆方向を新しく作る：`edgeRibbonFace`（帯 → 辺）、`extractPipeBelt`（梯子 → 元のパイプの輪の 2 頂点）、`createSurfacePaste`（貼り付けた形 → 入力）。それぞれ保つもの・失うものを宣言する
  - 確定を上流へ連ねる。今は入力が別の項目の出力なら断っている（`PlayerCommandDispatcher.ModifierInverse.cs`）。途中に逆方向を持たない項目があれば断る。Undo は 1 件
- 確かめ方
  - `stage8_辺パイプ`（パイプの出力 → 帯 → 辺）
  - `stage8_パイプの梯子化`（HairRe → HairBelt → 元のパイプ）
  - `stage8_まつげ眉`（貼り付けた梯子を読むまつげ → 梯子）
- 完了の条件：3 つとも確定が通り、Undo 1 回で全部戻る。保つもの・失うものが照会 `queryModifierInverse` に出る

#### 調べて分かったこと

- 断っていた所：`PlayerCommandDispatcher.ModifierInverse.cs` の `BeltSourceWrites`（梯子が別の項目の出力）と、頂点へ藤壺の振り分け（書き直す頂点のオブジェクトが別の項目の出力）の 2 か所
- 3 つの生成の頂点の対応（コードで確かめた）
  - `extractPipeBelt`（`PipeBeltExtractor.AppendBelt`）：梯子の始点・左右のレール・終点は元のパイプの頂点（始点・`1+r*K+RailA`・`1+r*K+RailB`・終点）の位置をそのまま写す。開始タグ 2 点は始点と最初の段から導く
  - `edgeRibbonFace`（`EdgeRibbonFaceOps.BuildInto`）：辺の頂点ごとに左右の 2 頂点を、GPU のワールド位置 ± 幅方向 × 幅/2 に置く（左右の中点＝元の頂点のワールド位置）。先端・タグは端の頂点から導く
  - `createSurfacePaste`（`SurfacePasteOps.Build`）：出力の頂点 i は入力の頂点 i を 1 点ずつ写す。平面内の位置は変えず、方向の成分だけを 参照面の交点の方向の成分＋平面からの高さ（平面化なら 0）にする
- 変更前（同じ Play、`SandBox/Captures/stage14/edgepipe_before_00_edited`）：`stage8_辺パイプ` のパイプ、`stage8_パイプの梯子化` の HairRe、`stage8_まつげ眉` の Lush を動かして照会すると、どれも「梯子「…」は別の項目（…）の出力です。上流の逆方向へは辿らない」で `canCommit`=0。`queryModifierInventory` の逆方向ありは 8

#### 実装

- 生成の控え（`ModifierFrameTrace.cs`）：枠の種類 `Source`（入力の頂点 1 個から作った頂点。枠の頂点の平均が入力の頂点に当たる）、`Frame.SourceMesh`・`SourceVertex`、`RecordSource`。記録は `PipeBeltExtractor.AppendBelt`（始点・レール・終点）、`EdgeRibbonFaceOps.BuildInto`（左右の 2 頂点）、`SurfacePasteOps.Build`（全頂点）
- 宣言（`PLModifierAttribute.cs` の `ModifierInverseMethod`）：`copiedVertices`（`extractPipeBelt`）・`ribbonCenters`（`edgeRibbonFace`）・`surfacePaste`（`createSurfacePaste`）。保つもの・失うものは下の決めた扱い
- 共通の核 `ModifierInverse.InvertSources`：`Source` の枠ごとに見本と編集後の平均を生成器の空間で比べ、入力の頂点ごとの古い位置・新しい位置を返す（1e-6 以下の移動は動いていない扱い）
- 貼り付け（`SurfacePasteOps.cs`）：1 点の正方向 `PastePoint`、逆 `InvertPoint`、入力を組む `ResolveInput`（ビューアの `ResolveSurfacePasteForCommand` から移して共有）
- 見本（`PolyLingPlayerViewerCore.ModifierInverse.cs`）：帯の場合を頂点へ藤壺と同じ形で足した（GPU のワールド位置から組む）
- 振り分け（`PlayerCommandDispatcher.ModifierInverse.cs`。ファイルを書き直した）
  - `SourceVertexWrites`：3 つの方法で入力の頂点の新しい格納値を作る。`copiedVertices` は新しい位置そのもの、`ribbonCenters` は中点の移動を元の頂点の `WorldMatrix` の逆で格納空間へ、`surfacePaste` は `InvertPoint`。元のメッシュがモデル内に無い（スキンド化済みの入力を `LocalView` の複製で読んだ）ときと、帯の元がスキンドのときは断る。ずれの試算は、入力から作った頂点を新しい入力に対する位置へ動かした見本と編集後で取る（入力から導くだけの頂点は含まない）
  - 上流へ連ねる：`BuildInversePlanCore`（項目 1 つ。編集後の出力を外から渡せる）と `ResolveInverseChain`。書く入力が別の項目の出力なら、その出力の複製に書く値を入れたものを「その項目の出力の編集」として逆方向を組む（再帰）。逆方向を持たない項目に当たったら「「…」は別の項目（…）の出力で、その項目は逆方向を持たないので書き直せません（上流の逆方向を連ねられない）」で断る。同じ項目へ 2 つの経路から書く・32 段を超える、も断る
  - 確定：一番上流の入力の頂点と各段の引数を書き、上流から順に `RunObjectGroupStep` → `MarkRebuilt`。失敗したら全部戻す。Undo は一覧のスナップショットと、引数を書き換えたグループごとの差し替えをまとめた 1 件
  - 確定で入力の頂点を書いたあと、GPU の位置を `SyncMeshPositionsAndTransform` で合わせる（下の決めた扱い）
  - 照会・確定の応答に `chainCount`・`chain`（上流から順に、項目・action・方法・書くもの・保つもの・失うもの・その段のずれ）。`writes` は経路全体で実際に書くもの。確定の応答に `actualMaxDeviation`・`actualMeanDeviation`（作り直した実際の出力と編集した出力のずれ）
- 結果の宣言：`queryModifierInverse` に `chainCount`・`chain`、method の説明に方法を足した（`PanelCommand.Query.cs`）。`commitModifierOutputEdit` の説明（`PanelCommand.MeshList.cs`）

#### 決めた扱い

- 3 つの逆方向の保つもの・失うもの
  - `copiedVertices`：保つもの＝パイプの面構成・頂点 ID・ウェイト、輪のレール以外の頂点、引数／失うもの＝開始タグの位置、輪のレール以外の頂点の追随（レールの 2 頂点と始点・終点だけを書く）
  - `ribbonCenters`：保つもの＝辺の面構成・頂点 ID・ウェイト、帯の幅、辺の辞書／失うもの＝帯の幅と向きの変化（左右の中点の移動だけを戻す）、先端・タグの動き
  - `surfacePaste`：保つもの＝入力の面構成・頂点 ID・ウェイト、参照面、引数／失うもの＝平面化するとき方向に沿った成分の変化（元の入力の値を保ち、参照面へ貼り直す）。平面化しないときは参照面の交点を新しい平面内の位置で求め直すので丸めは無い。交点の無い頂点は出力の位置がそのまま入力になる
- 上流へ連ねる：書く入力が別の項目の出力なら、その出力の編集とみなして同じ手順で逆方向を組む。逆方向を持たない項目があれば断る（段 6 の「上流の逆方向へは辿らない」を改めた）。照会の各段のずれはその段だけの試算なので、連ねた鎖で失うもの（帯の幅・平面化の方向の成分など）は確定の応答の `actualMaxDeviation` で見る
- 3 つの出力は逆方向を持つので、段 2 の保護で位置の編集（分類 `geometry.position`）が通るようになった（段 7・段 8 の「貼り付けの出力・HairBelt への書き込みは拒否」は位置の編集については通る。面の削除などは今までどおり拒否）。直接編集して確定すると、その項目から上流へ連ねる
- 確定で入力の頂点を書いたら GPU の位置も合わせる。帯・頂点へ藤壺の作り直しは GPU のワールド位置から入力を読むので、合わせないと書く前の入力から組んでしまう（帯で確かめた：変更直後は辺 EdgeSrc の頂点は動いたが、帯とパイプが元の位置で作り直され、`actualMaxDeviation`=0.1 だった。合わせたあと 1.5e-8）
- 3 章の集計は段 0 の確定値のまま書き換えず、段 14 のあとの数を注記で足した

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage14/`）

- `stage8_辺パイプ`（パイプ → 帯 → 辺）
  - パイプ全体を `moveSelectedVertices` で y 0.1 動かして照会：`canCommit`=1、`chainCount`=2（EdgeRibbon#0 edgeRibbonFace（ribbonCenters）→ EdgeRibbon#1 createPipe（beltFrames））、`writes` は「EdgeSrc の頂点 4 個（帯の左右の中点の移動）」、各段の保つもの・失うものが出た（`edgepipe_00_created` → `01_edited`）
  - 確定：EdgeSrc の頂点 0〜3 の y が 0・0・0.1・0 → 0.1・0.1・0.2・0.1、帯・パイプが作り直され、`actualMaxDeviation`=1.5e-8。照会の状態は 2 項目とも Rebuilt・`pending`=0（`02_committed`）
  - Undo 1 回：EdgeSrc・帯・パイプとも確定前の値に戻った（`03_undo`）。パイプは編集した形ではなく確定前の入力に対する正方向の形（`pending`=0）。6 章「段 12 から回したもの」の件と同じ現れ（編集が正方向で再現できる）
- `stage8_パイプの梯子化`（HairRe → HairBelt → 元のパイプ）
  - HairRe 全体を z 0.1 動かして照会：`chainCount`=2（HairBelt#0 extractPipeBelt（copiedVertices）→ HairRe#0 createPipe（beltFrames））、ずれ 0
  - 確定：SrcPipe の始点（頂点 0）と最初の輪のレール（頂点 4・5）の z が 0 → 0.1、輪のほかの頂点は変わらない。HairBelt・HairRe が作り直され `actualMaxDeviation`=0（`pipebelt_01_edited` → `02_committed`）。Undo 1 回で SrcPipe・HairBelt・HairRe とも確定前の値
  - HairRe の頂点 1 だけを `setVertexPositions` で z 0.083 → 0.183（正方向で再現できない編集）にして確定（`actualMaxDeviation`=0.078）→ Undo 1 回：SrcPipe・HairBelt は確定前、HairRe は編集した形（z 0.183）・`pending`=1・Blocked。破棄で作り直された
- `stage8_まつげ眉`（Lush → 貼り付けた梯子 LashPasted → LashLadder）
  - Lush 全体を y 0.03 動かして照会：`chainCount`=2（LashPasted#0 createSurfacePaste（surfacePaste）→ Lush#0 createLush（lushFrames））。Lush の段のずれ 1.9e-7、LashPasted の段のずれ 0.011（平面化で方向の成分を失う分）
  - 確定：LashLadder の y が 0.03 動き（0.10 → 0.13）、z は 0.3 のまま。LashPasted・Lush が作り直され（`actualMaxDeviation`=0.011）、下流の Lush2・BrowFlat・BrowPipe・Brow がコマンドの終わりに流れた（`lush_00_created` → `01_committed`）
  - Undo 1 回：LashLadder・LashPasted・Lush2・眉の鎖が確定前の値、Lush は編集した形・`pending`=1・Blocked（編集が正方向で再現できないため）
- 逆方向を持たない項目で止まる鎖（`stage8_断面系` のモデル）
  - 回転体 Rev（逆方向なし）を平面化だけで貼り付けた RevFlat の頂点 0 を動かす → 照会 `canCommit`=0、確定も「「Rev」は別の項目（Rev#0 createRevolution）の出力で、その項目は逆方向を持たないので書き直せません」で断られた
  - RevFlat をさらに貼り付けた RevFlat2 を編集 → RevFlat（逆方向あり）を辿ったうえで Rev で同じ理由で断られた
- `queryModifierInventory`：逆方向あり 8 → 10（`edgeRibbonFace`・`createSurfacePaste` が増えた。`extractPipeBelt` は段 0 から `←createPipe` で数えていて、→copiedVertices になった）
- 段 1〜13
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随（子の頂点 0 の z が -0.005 → 0.095）。子への `deleteSelection` は出力の保護で拒否
  - 逆方向の確定と Undo（藤壺）：出力の頂点 0 を y 0.394 → 0.444 → 確定（0.397）→ Undo 1 回で 0.444・`pending`=1・Blocked、2 回目で 0.394
  - 作成直後の pending：`stage9_作成直後の編集` で HairRe の `pending`=1、`stage9_魔法陣_作成直後の編集` で MC#2 の `pending`=1
  - 一覧のスナップショットの Undo のあとの頂点移動：藤壺の `params.scale`=2 → Undo → 出力を `moveSelectedVertices`（y 0.3）で、動いた形が画面に出た（`s9_listundo_00 → 01`）
  - 入力の BoneTransform：`stage8_まつげ眉` で球の PositionZ を 0.1 にすると LashPasted の頂点 0 の z が 0.4426 → 0.5426
  - シナリオ：`stage9_作成直後の入力編集` のあと HairBelt の `dirty`=0。`stage11_パイプの梯子化_グループから`（そのモデル）で HairBelt_1 → HairRe、`stage11_辺パイプ_グループから`（`stage8_辺パイプ` のモデル）で EdgeRibbon_1 → EdgePipe_1 がつながり Evaluate
  - Undo 2 回：`stage8_断面系` で ProfLine の頂点 3（x 0.3 → 0.4）→ Rev の `params.radialSegments`=8 → Undo 2 回で Rev が 16 分割・ProfLine 0.3
  - ブレンド：`stage8_ブレンド` で BlA の頂点 0 を y 2.0 にすると BlC が 0.75 → 1.5、BlC への `setVertexPositions` は拒否。`stage13_ブレンド_複製`（Play を入れ直して流した）で BlC_blend が 1.5、BlC は 0.5 のまま、BlC_blend への書き込みは拒否
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型・読む空間の未指定・WriteWhen の不正）はすべて 0。Editor.log（変更後の Play の間）にエラーなし

#### 段 14 と関係なく見つかったこと（6 章へ回した）

- `stage8_辺パイプ` のモデルで `stage11_辺パイプ_グループから` を流したあとの照会 `queryModifierGraph` で、EdgeRibbon#0 の downstream が「1,2」と出た（2 は EdgeRibbon_1#0 で、入力は EdgeSrc。EdgeRibbon は読まない）。原因は調べていない

### 段 15：厚み付け・フリルの逆方向 — 状態：済（2026-10-30）

- 範囲
  - 厚み付け：`BeltShapeOps.ApplySolidify`（呼び出しは `PrimitiveMeshFactory.cs` の 3 か所）の中で、殻の頂点 → 元の頂点の対応を生成の控え（`ModifierFrameTrace`）へ足す。今は控えが全体へ届かず、逆方向は「枠がありません」で断る
  - フリル：生成器に枠の控えを足し、`BeltFrames` を宣言する。生成器の頂点の並びは段の始めに読み、足せるかを決める
- 確かめ方：`roboF_T5_SK_initialDebug_フリル`（SkirtFrill の `params.thickness`=0.02 の状態で確定）、厚み付きのパイプ
- 完了の条件：両方で確定・Undo 1 回・破棄が通る

#### 調べて分かったこと

- 厚み付け（`BeltShapeOps.ApplySolidify` → `FaceGroupSolidifier.Build`）は新しいメッシュを作る。元の頂点 1 個から、表の殻・裏の殻と、境界の頂点ならベベル帯の縦列ができる。控えの枠は元の部品を指したまま残るので、その後の連結（`MeshObjectAppendOps.Append`）で全体へ届かなかった
- 呼び出しは `PrimitiveMeshFactory.cs` の 3 か所（フリルの融合あり・融合なし、パイプ）。厚み 0 のときは元の部品をそのまま返す
- フリル（`FrillMeshGenerator`）は、段 s→s+1 の区間ごとに左右のレールへ波形の頂点を置く。1 つの頂点はその区間の両端の段から決まる。融合ありでは複数の梯子のステップが同じレールの頂点を共有する。枠の控えは無く、記録は基底の OnKeepAsGroup だった
- 変更前（同じ Play）：`stage8_パイプの梯子化` の HairRe を `params.thickness`=0.01 にして出力を z 0.1 動かすと、照会は「生成の控えに枠がありません」で `canCommit`=0。フリルは `queryModifierInventory` で逆方向なし

#### 実装

- `FaceGroupSolidifier.Result.SourceToVertices`：元の頂点番号 → 作った頂点（表・帯・裏）の並び
- `ModifierFrameTrace.Expanded(元, 新, 対応)`：元の部品を持ち主とする枠を新しいメッシュへ付け替え、枠の頂点を対応する頂点の並びへ広げる。`BeltShapeOps.ApplySolidify` が成功したときに呼ぶ
- `ModifierFrameTrace.RecordVerticesOfBelt`：梯子の番号を指定して枠を控える
- フリル
  - `FrillBeltInput.TraceBelt`（控えの梯子の番号）。`PrimitiveMeshFactory.ToFrillInput` が今の梯子（`EachPreprocessedBelt` が立てた番号）を入れる
  - `StepInfo` に段の両端の番号 `RungFrom`・`RungTo` と `TraceBelt`
  - 面を張る前に、段 r の枠＝段 r を端に持つステップ（前後 2 区間）の左右のレールの頂点を控える
  - 逆方向は段 6 の `InvertBelts`（枠ごとの相似変換をその段の左右へ掛ける）をそのまま使う
- 宣言：`CreateFrillCommand` に `beltFrames`。パイプの失うものに「厚みの変化」を足した
- 試験用のシナリオ `@settings/PolyLing/PolyLing/scenarios/stage15/stage15_フリル`（梯子 → 融合なし・厚み 0.02 のフリル FrSolid と、融合あり・厚みなしのフリル FrJoined）

#### 決めた扱い

- 厚み付けした出力の枠は、元の頂点から作った頂点を全部（表・帯・裏）含める。殻ごと動いた形は相似変換で戻り、厚みの変化は失う（入力に厚みを書き戻さない）
- フリルの枠は段ごと。隣り合う段の枠は区間の頂点を共有してよい（枠ごとに相似変換を求め、段ごとに 1 つの変換を左右へ掛ける）
- `Source` の枠（梯子化・帯・貼り付け）は厚み付けを通らないので、広げる処理の対象にしない（広げると平均が変わる）
- フリルの出力も、段 2 の保護で位置の編集だけ通るようになった（段 8 の「フリルの出力への `setVertexPositions` は拒否」は位置の編集については通る）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stage15/`）

- 厚み付きのパイプ（`stage8_パイプの梯子化` の HairRe、`params.thickness`=0.01）
  - 出力を z 0.1 動かして照会：`canCommit`=1、枠 7（輪 5・先端 2）。段 14 の連なり（HairBelt → SrcPipe）も通り `chainCount`=2
  - 確定：SrcPipe の始点・レールの z が 0 → 0.1、HairBelt・HairRe が作り直され `actualMaxDeviation`=2.1e-8（`pipe_thick_00_before_edit` → `01_committed`）。Undo 1 回で全部確定前の値
  - 頂点 0 だけを z 0.15 にして確定（`actualMaxDeviation`=0.055）→ Undo 1 回で SrcPipe・HairBelt は確定前、HairRe は編集した形・`pending`=1 → 破棄で作り直され z 0.088
- フリル（`stage15_フリル`）
  - 融合なし・厚み 0.02 の FrSolid を z 0.1 動かして照会：`canCommit`=1、枠 5（梯子の段 5）。確定で梯子 FrLadder の z が 0 → 0.1、同じ梯子を読む FrJoined も流れた（`actualMaxDeviation`=1.5e-8。`frill_00_created` → `01_committed`）。Undo 1 回で梯子・FrSolid・FrJoined とも確定前
  - 融合あり・厚みなしの FrJoined の波の頂点 1 つを z 0.06 → 0.1 にして確定（ずれ 0.030）→ 梯子の段が書き直された。Undo 1 回で梯子は確定前、FrJoined は編集した形・`pending`=1・Blocked → 破棄で z 0.06 に作り直された
- `roboF_T5_SK_initialDebug_フリル`（c1〜h2。SkirtFrill はスキンド化済み、融合あり・2 プロファイル・鎖の配り）
  - `params.thickness`=0.02 にして出力全体を z 0.05 動かして照会：`canCommit`=1、枠 576。確定で梯子「円筒24C_skinned」の頂点 600 個が書き直され（頂点 1 の z が 0.1144 → 0.1644）、`actualMaxDeviation`=1.8e-7（`skirt_00_thick002` → `skirt_01_committed`）。下流の揺れ鎖 SpringLadder も流れた
  - Undo 1 回で梯子が 0.1144 に戻り、SkirtFrill は編集した形（頂点 0 の z 0.1718）のまま。破棄で作り直された
  - `params.thickness`=0.03 にしてから同じ編集・確定でも梯子の頂点 1 が 0.1644 になった
  - 照会の各段のずれ（試算）は -1（出ない）。下の「段 15 と関係なく見つかったこと」
- `queryModifierInventory`：逆方向あり 10 → 11（`createFrill`）
- 段 1〜14
  - サブディビジョン：BT_Base をケージにした子が、ケージの全頂点の z 0.1 の移動に追随（子の頂点 0 の z が -0.005 → 0.095）。子への `deleteSelection` は出力の保護で拒否
  - 逆方向の確定と Undo（藤壺）：出力の頂点 0 を y 0.394 → 0.444 → 確定（0.397）→ Undo 1 回で 0.444、2 回目で 0.394
  - 作成直後の pending：`stage9_作成直後の編集` で HairRe の `pending`=1。`stage9_作成直後の入力編集` のあと HairBelt の `dirty`=0
  - Undo 2 回：`stage8_断面系` で ProfLine の頂点 3（x 0.3→0.4）→ Rev の `params.radialSegments`=8 → Undo 2 回で Rev が 16 分割・ProfLine 0.3
  - 入力の BoneTransform：`stage8_まつげ眉` で球の PositionZ を 0.1 にすると LashPasted の頂点 0 の z が 0.4426 → 0.5426
  - ブレンド：`stage8_ブレンド`（Play を入れ直して流した）で BlA の頂点 0 を y 2.0 にすると BlC が 0.75 → 1.5、BlC への `setVertexPositions` は拒否
  - 上流への逆方向（段 14）：`stage8_辺パイプ` のパイプを y 0.1 動かして確定 → EdgeSrc の頂点 0〜3 が y 0.1 上がり（`actualMaxDeviation`=1.5e-8）、Undo 1 回で戻った
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目（付け忘れ・action の衝突・引数の対応なし・未対応の型・読む空間の未指定・WriteWhen の不正）はすべて 0。Editor.log にエラーなし

#### 段 15 と関係なく見つかったこと（6 章へ回した）

- SkirtFrill（融合あり・2 プロファイル・鎖の配り）の照会で、各段のずれ（試算）が -1 だった。試算は梯子を点列（Baked）で渡して作り直した形と今の出力を比べ、頂点数が違うと出さない（`ModifierInverse.Deviation`）。試算の作り直しの頂点数は確かめていない。確定の `actualMaxDeviation` は出る（1.8e-7）
- roboF の最初の確定では、確定の直後に読んだ梯子の頂点 1 の z が 0.1144 のままで、Undo・Redo のあとも 0.1144 だった。同じ手順を 2 回やり直すと 2 回とも 0.1644 に書き直された。最初の 1 回で書き直されなかった原因は特定していない

### 段 A：実体を替えない Undo — 状態：済（2026-10-30）

段 6・8・9・12・14 で同じ仕組みの現れを個別に直していたことを受け、仕組みそのものを全部読んで（参照を持つ所・引き直し・位置の同期の全部を file:line で洗い出し）から直した段。

#### 調べて分かったこと

- 描画オブジェクトを別の実体に替える Undo：`MeshFilterToSkinnedRecord.RestoreList`（一覧を全部複製で置き換える）、`MorphCreateRecord.RestoreState`（同じ。ミラー対は引き直さない）、`MeshListChangeRecord`（消したものを新しい実体で戻す）。`MeshReorderChangeRecord` は控えた実体をそのまま一覧へ戻すので、前の 2 つのあとに戻すと古い実体が一覧に戻る
- 実体を持ち続けていて差し替えで古くなる所：ミラー対、選択の状態（`MeshSceneRenderer._selectionState` ほか。`EnterUndoApplied` は付け替えない）、評価器の控えのグラフ（`CachedGraph`。`SyncFromInput` は入力を参照で比べる）、サブディビジョンの控え、並び替えの記録、ツールのプレビュー（Undo はプレビューを終わらせない）
- 複製の決まりが 2 つあった：`MeshContextCloneOps.Clone` と `MeshContextSnapshot`。前者は作業軸の値（`WorkAxis`）を写さず、後者は `PMXMaterialNames`・`BoneModelRotation`・`WorldMatrix` を写さない
- Undo のあとに作り直すかは記録の種類の並び（`Lifecycle.cs`）で決めており、`MorphCreateRecord` が漏れていた
- 変更前（同じ Play）：直方体と作業軸 WA1 を作り、直方体をサブディビジョン → Undo → `setActiveWorkAxis`（WA1）が「作業軸オブジェクトではありません」で失敗（作業軸の値が落ちた）

#### 実装

- `MeshContextCloneOps`：中身を写す `CopyState(src, dst, kind)` を分け、`Clone` は空の実体に `CopyState`。`WorkAxis`（複製）・`IsMirrorFromMemo` を写す。既にある実体へ写すときは作業中の位置・要素の選択・ミラー表示の控え・パーツ辞書の控えを新品と同じにする。NewObject は ObjectId・担当・パーツ辞書・ミラーの対の参照を新品の値にする
- `MeshContextSnapshot`：中身は `MeshContextCloneOps.Clone(StateSnapshot)` の控え（`State`）に一本化。要素の選択だけ別に控える
- `MeshFilterToSkinnedRecord.RestoreListInPlace`：控えの各オブジェクトについて、同じ ObjectId の実体があればそこへ `CopyState`、無ければ複製を足す。並びは控えの順。`RestoreList`（ミラー対の組み直しはそのまま）と `MorphCreateRecord.RestoreState` が使う
- `UnifiedBufferManager.MatchesModel`：組んだときの描画オブジェクト・MeshObject の実体・頂点数・面の構成の署名を控え、今のモデルと合っているかを返す。`MeshSceneRenderer.AdapterMatchesModel`・`PlayerViewportManager.BuffersMatchModel`
- `Lifecycle.cs`：一覧の Undo のあとに作り直すかを、記録の種類ではなく `BuffersMatchModel` で決める

#### 決めた扱い

- 消したものを戻す Undo（`MeshListChangeRecord`）は消えた実体を作り直すので、`MeshUndoTarget` の ObjectId での引き直しと `RebindEditSpaceObjects` は残す
- 作業軸を NewObject で複製すると、複製も作業軸になる（以前は種別だけ作業軸で値が無かった）

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stageA/`）

- 作業軸：上の手順の Undo のあと `setActiveWorkAxis` が通り、Redo のあとも通った
- モーフ作成の Undo：`createMorphFromDiff` → Undo で作り直しの判定が true（以前は記録の種類の並びに無く false）
- 一覧のスナップショットの Undo のあとの頂点移動：頂点へ藤壺の `scale`=2 → Undo → 出力を `moveSelectedVertices`（y 0.3）で、動いた形が画面に出た（`vb_listundo_moved`）。書き戻しの警告なし
- 段 1〜15：辺パイプの確定（`actualMaxDeviation`=1.5e-8）と Undo 1 回、パイプの梯子化の確定（SrcPipe の頂点 0・4・5 の z 0 → 0.1）と Undo 1 回、`stage9_作成直後の編集` で HairRe の `pending`=1、`stage8_断面系` の Undo 2 回（Rev 16 分割・ProfLine 0.3）、`stage8_ブレンド`（BlA の y 2.0 で BlC 1.5）
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。`queryCommandAudit` の完了条件の項目はすべて 0。Editor.log にエラーなし

### 段 B：CPU と GPU の位置 — 状態：済（2026-10-30）

#### 調べて分かったこと

- `MeshObject.Positions` は控えを持ち、頂点の位置を直接書いたら `InvalidatePositionCache` を呼ぶ約束だった。書く所は 136 行、捨てる呼び出しは約 99
- GPU へ位置を送るのは呼び出し側の役目で、書き手ごとに `SyncMeshPositionsAndTransform`・`EnterVerticesMoved`・`UpdateTransform` を呼び分けていた。`UpdateTransform` は行列だけ更新し、CPU の位置は送らない
- GPU のワールド位置を読んで計算する所（帯・頂点へ藤壺・縮み・法線移植・吸着・UV 投影ほか）は、読む前に `UpdateTransform` だけを呼んでいた。段 14 の帯の確定が書く前の入力から組んだのはこのため
- モディファイアの流しは最後に 1 回だけ作り直す。途中で GPU がそろうかは各コマンド次第だった

#### 実装

- `MeshObject.Positions`：読むたびに今の位置を写す（配列は使い回す）。`InvalidatePositionCache` は何もしない
- `PlayerViewportManager.SettleWorldPositions(project)`：GPU を CPU に合わせる唯一の入口。バッファが合っていなければ `EnterTopologyChanged`、合っていれば全メッシュの位置を送り、変換し、画面へ配る。頂点ドラッグの最中は何もしない
- 呼ぶ所：書き込みのあるコマンドの終わり（`PlayerCommandDispatcher.DispatchAuthorized`）と、GPU のワールド位置を読む直前（DeformSkin・ObjectGroup の縮み・帯・頂点へ藤壺・逆方向の見本・UV 投影・パネルの `OnRequestUpdateTransform` 4 か所。いずれも `UpdateTransform` から替えた）
- 外したもの：段 14 の確定で入力を書いたあとの `SyncMeshPositionsAndTransform`（2 か所）。段 A で残していた「位置だけを戻す記録は作り直しへ回す」の分け方

#### 確かめた結果（MCP で Play、キャプチャは `SandBox/Captures/stageB/`）

- 辺パイプの確定：段 14 の手当てを外しても `actualMaxDeviation`=1.5e-8（段 14 の手当て前は 0.1）。Undo 1 回で戻り、画面も戻った（`edgepipe_committed` → `edgepipe_undo`）
- まつげ眉の確定：LashLadder の y 0.10 → 0.13、`actualMaxDeviation`=0.011。Undo 1 回で LashLadder 0.10、Lush は `pending`=1・Blocked（段 14 と同じ）
- サブディビジョン：ケージの全頂点の z 0.1 の移動に子が追随（頂点 0 の z 0.3714 → 0.4714）、子への `deleteSelection` は拒否（`subdiv_follow`）
- `stage9_作成直後の編集`（`pending`=1）、`stage8_断面系` の Undo 2 回、`stage8_ブレンド`：段 A と同じ結果
- 検査：3 つの csproj のコンパイルはエラー 0。`unity_validate_changes` もエラー・例外 0。Editor.log にエラー・警告なし

#### この 2 段に入れなかったもの（6 章へ回した）

- モデルごと入れ替える Undo（`ModelReplaceRecord.Swap`）：`ModelContext` を鍵にした控え（評価器のグラフ・状態）と、モデルへの参照（`EditSpaceSession.Model`・`MeshUndoTarget.Model`）が古くなる。`ModelBundle.ToModel` を読んでいないので直し方は決めていない
- 描画バッファに載っていないオブジェクト（隠したもの・空のもの）は GPU のワールド位置を読めない。帯などがこれを読むと失敗する
- Undo はプレビューを終わらせない。段 A で置き換え前のオブジェクトへ書くことは無くなったが、プレビューの戻しが Undo のあとの中身へ上書きする

### 段 C 以降の段分け（2026-10-30。旧段 16〜20 を原因の仕組みで分け直して承認）

旧段 16〜20 は 6 章から症状ごとに集めた段分けだった。段 A・段 B の洗い出しに照らして同じ原因から出ている症状をまとめ、原因を 1 か所直して複数の症状を消す段分けに組み直した。上から順に進める。

### 段 C：ObjectId を必ず振る — 状態：済（2026-10-30）

- 原因（コードで確認）
  - ObjectId は `ModelContext.Add`・`Insert` でしか振らない（`ModelContext.MeshList.cs`:35-36、56-57）
  - 一覧へ直接足す箇所は採番を通らない：`MeshFilterToSkinnedConverter.cs`:618-625、`RemoteProgressiveSerializer.cs`:354、`RemoteProjectReceiver.cs`:147・150・167、`Import.cs`:1381、`ModelUndoRecords.cs`:116・165
- 直し方：一覧へ足す道を、採番する 1 か所に揃える。形は段の始めに上の箇所を読んで決める
- 消える症状：一括スキンド化で作ったボーンの objectId が 0 のまま（旧段 18。6 章「段 10 から回したもの」）
- 確かめ方：`convertMeshFilterToSkinned` の応答と保存した JSON のボーンの objectId
- 完了の条件：ボーンに 0 でない objectId が振られる。上の箇所がすべて採番を通る
- 実装
  - `ModelContext.Admit`（未割当なら `ObjectIdAllocator.Next()`、親のモデルを張る）を一覧へ入れる道の 1 か所にした。`Add`・`Insert` と、新しく足した `ReplaceMeshContexts`（一覧の入れ替え）・`SetMeshContextAt`（1 つの差し替え）がこれを通る（`ModelContext.MeshList.cs`）
  - サーバ側の直接の書き込み 7 か所を置き換えた：一括スキンド化、CSV のマージ（`Import.cs`）、モデルのスナップショットの戻し 2 か所（`ModelUndoRecords.cs`）、並べ替え 3 か所（`MeshListOps.cs`・`MeshAttributeChangeRecord.cs`・`MeshListRecords.cs` の `RestoreListInPlace`）
  - `MeshContextList` の set を private にした
  - 見張り：書き込みのあるコマンドの終わりに、全モデルの ObjectId の 0・重複を `ModelContext.CheckObjectIds` で点検し、あればコマンド名付きでエラーログ（`PlayerCommandDispatcher.CheckObjectIdsAfter`）
  - 呼ばれていなかった `ObjectIdAllocator.EnsureIds` を消し、冒頭の説明を実際の採番の場所へ直した
- 決めた扱い：リモートの受信側（`RemoteProgressiveSerializer.cs`:354、`RemoteProjectReceiver.cs`:147・150・167）は置き換えない。ID はサーバだけが振り、受信側は送り手の ID をそのまま持つ（`ObjectIdAllocator.cs` の設計、`RemoteProgressiveSerializer.cs`:840-842）
- 確かめた結果
  - Unity のコンパイル・例外 0
  - `barnacle_place` を流して `convertMeshFilterToSkinned`：`createdObjectIds` がボーン 5 本ぶんの 0 でない ID（前は [0]）。保存した JSON（`SandBox/Captures/stageB/stageC_bones.mfproj`）に `"objectId": 0` は無い
  - スキンド化の Undo・Redo：同じ 5 個の ID が消えて戻る
  - `reorderMeshes` → Undo → Redo：並びと ID が戻る
  - `stage8_辺パイプ` の出力を動かして確定（actualMax 1.5e-8）→ Undo で作り直し 2
  - 流している間、見張りの `[ObjectId]` エラーは出なかった

### 段 D：書く・読むの宣言を実際と合わせる — 状態：済（2026-10-30）

- 原因（コードで確認）
  - `EdgeRibbonFaceCommand.MasterIndices` は読むだけなのに `MeshRefAccess = PLMeshRefAccess.Write`（`PanelCommand.TopologyTargeted.cs`:288）
  - `ModifierGraph.Build`（69-103）は Write の入力を書き手として、同じオブジェクトを読む項目すべてへつなぐ
- 直し方：宣言を直す。全コマンドの Write の宣言を実際の書き込みと照らす検査を `queryCommandAudit` に足す
- 消える症状：`queryModifierGraph` の downstream に入力を読まない項目が出る（旧段 17。6 章「段 14 から回したもの」）
- 確かめ方：`stage8_辺パイプ` のモデルで `stage11_辺パイプ_グループから` を流したあとの downstream。`queryCommandAudit` の新しい検査が 0 件
- 完了の条件：downstream が入力の関係どおり
- 調べて分かったこと
  - Write の宣言を読むのは 3 か所。担当者判定（`RemoteOwnership.cs`:524）と出力の保護（`ModifierOutputGuard.cs`:138）は `IsWriteActive` で書き込み条件（WriteWhen）をコマンドの引数で判定する。依存グラフ（`ModifierEvaluator.cs`:391）だけが `Access == Write` を見て条件を無視していた。揺れ鎖（`paintWeights=true` のときだけ書く）も、塗らない設定で書き手として線を引いていた
  - 辺から帯・頂点へ藤壺・辺をパイプにするが入力へ書くのは、辞書名が空のとき選択を辞書として保存する場合だけ（`PlayerCommandDispatcher.EditTools.cs`:359-402、`VertexBillboard.cs`:38-39、`EdgePipe.cs`:131-135）。グループの項目は辞書名を持つので読むだけ。今の書き込み条件の書き方（キー 1 つ・bool か enum）では表せなかった
- 実装
  - 依存グラフも書き込み条件を項目の引数で判定する（`ModifierEvaluator.IsWriteKeyActive` → `PanelCommandFactory.IsWriteActive`）。3 か所が同じ判定になった
  - グラフの構造キーに項目ごとの「書き込みが有効な入力」を混ぜた（`ModifierEvaluator.WriteActiveSignature`、`ModifierGraph.StructureKey`）
  - 書き込み条件の書き方を広げた：`&` で節を並べる（すべて満たすとき）、文字列は空を `key=` で書く。入れ子の型では各節に接頭辞を付ける。`InvalidWriteWhens` も合わせた（`PanelCommandMeshRefs.cs`、`PLParamAttribute.cs` の説明）
  - 宣言：辺から帯 `placement.keepAsGroup=true&edgeSetName=`、頂点へ藤壺 `placement.keepAsGroup=true&vertexSetName=&target=Vertices`、辺をパイプにする `edgeSetName=`
  - 見張り：作り直しで項目を 1 つ流すたびに、書き込みが有効でない入力の形・位置の署名を前後で比べ、変わっていれば `[ModifierWrite]` のエラーログ（`ModifierEvaluator.SnapshotReadOnlyInputs`・`UndeclaredWrites`、`PlayerCommandDispatcher.ModifierEval.cs`）。「宣言したのに書かない」は条件しだいで正しく起こるので見張らない
- 確かめた結果
  - Unity のコンパイル・例外 0。`queryCommandAudit` の書き込み条件の不正 0
  - `stage8_辺パイプ` → `stage11_辺パイプ_グループから`：EdgeRibbon#0 の downstream が「1」（前は「1,2」）
  - `roboF_T5_SK_initialDebug_フリル`：揺れ鎖 → フリル、揺れ鎖 → パイプの線がある。`setObjectGroupStepArgs` で `paintWeights=false` にすると揺れ鎖 → フリルの線が消え、Undo で戻る（構造キーで作り直された）
  - 辺パイプの出力を動かして確定（actualMax 1.5e-8）→ Undo で作り直し 4、`barnacle_place` 最後まで、`stage8_まつげ眉` の梯子を動かして作り直し 6・失敗 0
  - 流している間、`[ModifierWrite]`・`[ObjectId]` のエラーは出なかった。見張りが宣言漏れを拾うこと自体は、宣言漏れの項目が無いので実際には確かめていない

### 段 E：作り直しの結果を左右するものを署名に入れる — 状態：済（2026-10-30）

- 原因（コードで確認）
  - 生成器がポーズ込みの GPU の表示位置を読む（`PlayerViewportManager.GpuSelect.cs`:333-366 の `TryGetMeshWorldPositions` → `GetDisplayPositions`）。署名はバインド姿勢で取る（`ModifierEvaluator.cs`:576）
  - 行列 M は出力の枠（出力の `BindWorldToVertexMatrix` か親の `BindWorldMatrixInverse`）を使う（`ModifierSpace.cs`:144-161）。署名の行列は入力（`MatrixInputs`）だけ（`ModifierEvaluator.cs`:571-579、589-615）、出力の署名は形と位置だけ（666-677）
- 直し方：生成器が読む位置をバインド姿勢の CPU の値に揃える。出力の枠を署名に入れる。形は段の始めにコードを読んで決める
- 消える症状
  - 帯・頂点へ藤壺・ブレンドがポーズで結果が変わる（旧段 17）
  - 出力の親を付け替えても作り直されない（旧段 17）
  - 描画バッファに載っていないオブジェクトの位置を読めない（6 章「段 A・段 B から回したもの」）
- 確かめ方：ポーズを付けた状態で帯を作り直し、ポーズ無しのときと比べる。新規オブジェクトとして作ったパイプの出力の親を付け替える。隠したオブジェクトを入力に帯を作る
- 完了の条件：ポーズで結果が変わらない。親の付け替えで作り直される。隠したオブジェクトから作れる
- 決めた扱い：モデル空間で読む入力は、作成でも作り直しでも、ポーズを含まないバインド姿勢のワールド（`VertexToBindWorldMatrix × 格納値`）で読む。署名と同じ行列。CPU で求めるので描画バッファに依らない
- 実装
  - `ModifierSpace.BindWorldPositions` を足し、辺から帯・頂点へ藤壺の位置の読み口をこれにした（`PolyLingPlayerViewerCore.ObjectArrayBridge.cs`）。生成前の GPU の同期（段 B で足した `SettleWorldPositions`）は `ComputeWorldMatrices` に替えた（`CreateCommands.Topology.cs`）
  - 頂点へ藤壺のボーン・原点は `BindWorldMatrix` の平行移動成分（`VertexBillboardPlaceToolHandler.cs`）
  - ブレンドの行列を `BindWorldToVertexMatrix × VertexToBindWorldMatrix` にした（`BlendPreviewState.cs`）
  - 逆方向（帯の中点・頂点へ藤壺）は `BindWorldToVertexMatrix` で格納値へ戻す。スキンドの入力を断るのをやめた（`PlayerCommandDispatcher.ModifierInverse.cs`）
  - `IsPoseOwnedByOutput` を `ModifierSpace` へ移し、作り直しと評価器が同じ判定を使う
  - 評価器の控えに梯子の行列 M の出力の枠の署名（`State.OutputFrame`、`OutputFrameSig`。Transform・出力 1 つの項目だけ）を足し、`TakeInputs`・`InputsChanged`・`MarkMatrixChanged`・`ForwardKey` に入れた（`ModifierEvaluator.cs`）
- 確かめた結果
  - Unity のコンパイル・例外 0、`queryCommandAudit` の完了条件の不足 0
  - `stage8_辺パイプ`：EdgeSrc にポーズ（PositionY 0.5）を付けて `rebuildObjectGroup` → 帯・パイプの位置はポーズ無しと同じ
  - EdgeSrc を隠して頂点を動かす → 作り直し 2・失敗 0、帯が追随
  - 新規オブジェクトとして作った EdgePipe の親を、位置を持つ直方体へ付け替える（`setParentsByName`）→ EdgePipe の項目が作り直された（前は作り直されなかった）
  - 回帰：`stage8_ブレンド` の BlC の高さ 1.5、`stage8_頂点藤壺` の scale=2 → Undo で元の位置、出力を動かして確定（ずれ 3.3e-8）→ Undo、辺パイプの確定（actualMax 1.5e-8）→ Undo で作り直し 2、`barnacle_place` 最後まで、`stage8_まつげ眉` の梯子を動かして作り直し 6・失敗 0。エラー・警告のログ無し

### 段 F：未確定の状態をはっきり持つ — 状態：済（2026-10-31）

- 原因（コードで確認）
  - 未確定かどうかを、メモリ上の署名の控え（`State.Forward`）から推測している（`ModifierEvaluator.cs`:698-715。707 で今の出力が控えのどれかと同じなら未確定と見ない）
  - 控えは保存しない（`ModifierEvaluator.cs`:184-200）。保存の可否は全モデルの `FirstPendingOutputEdit` で決め、未確定の状態を保存しないので断る（`PlayerCommandDispatcher.cs`:1103-1115）
- 直し方：未確定かどうかを状態として持ち、Undo の記録と保存に含める。形は段の始めにコードを読んで決める
- 消える症状
  - 確定の Undo で編集した形・未確定に戻らない（旧段 16。6 章「段 12 から回したもの」）
  - `saveProjectCsv` がカレント以外のモデルの未確定でも失敗する（旧段 19。6 章「段 11 から回したもの」）
- 確かめ方：藤壺・パイプの出力全体を平行移動して確定 → Undo 1 回で編集した形・`pending`=1。別のモデルに未確定の出力を残したまま `saveProjectCsv` → 読み込みで未確定が戻る
- 完了の条件：上の 2 件が起きない
- 決めた扱い
  - 出力の描画オブジェクトが「最後に作り直しで書いた中身の署名」を持つ（`MeshContext.ModifierForwardSig`）。0 は未記録、1 は未確定のまま読み込んだ印。中身の署名（`ModifierEvaluator.OutputContentSig`。面構成と位置）は 0・1 を使わない
  - 未確定 ＝ 記録があり、今の中身の署名と違う。出力が複数なら 1 つでも違えば未確定。記録の無い出力は今の中身を記録して未確定ではない
  - 記録は中身と一緒に動く。一覧のスナップショットの Undo・Redo は `CopyState`（StateSnapshot）で写す。頂点の位置だけを戻す Undo は記録に触らない（戻した中身と記録を比べる）
  - 保存は未確定の真偽だけを書く（署名の計算方法を保存形式に持ち込まない）。読み込みは真なら 1、偽なら 0
- 実装
  - `MeshContext.ModifierForwardSig`・`ModifierForwardSigEditedMark` を足した（`MeshContext.Attributes.cs`）。`CopyState` で写す（`MeshContextCloneOps.cs`。複製では 0）
  - `ModifierEvaluator.cs`：`State.Forward`・`ForwardOrder`・`ForwardMemory`・`ForwardKey`・`OutputSig`・`RecordForward`・呼ばれていなかった `RecordForwardOf` を消した。`OutputContentSig`・`RecordForwardOutputs`（作り直しのあと出力ごとに記録）・`RecordUnrecordedOutputs`（`RefreshModel` で記録の無い出力だけ記録。前の「初めて見た入力の組」の扱いの置き換え）・`IsOutputEdited`（保存用）を足し、`IsOutputEditPending` を記録と今の中身の比較にした
  - 保存：JSON は `MeshDTO.modifierOutputEdited`（`ModelDTO.Mesh.cs`、`ModelSerializer_ModelContext.cs`）、CSV は未確定のときだけ `modifierOutputEdited,True` の行（`CsvMeshSerializer.Write.cs`・`Read.cs`）、バイナリはメッシュ概要を v11 にして末尾に 1 バイト（`RemoteProgressiveSerializer.cs`）
  - 保存の門（未確定があると保存を断る）を外した（`PlayerCommandDispatcher.cs`）
- 確かめた結果
  - Unity のコンパイル・例外 0
  - `stage9_作成直後の編集`：HairRe の `pending`=1 → 確定で 0 → Undo で 1（Blocked）→ Redo で 0
  - 未確定のまま `saveProjectFile`・`saveProjectCsv`・`saveProjectBinary` が通る。JSON は HairRe だけ `"modifierOutputEdited": true`、CSV は HairRe だけ行がある。3 形式とも読み込むと HairRe の `pending`=1
  - 読み込んだあと破棄で `pending`=0 → Undo で 1
  - 出力の頂点を動かす → `pending`=1 → Undo で 0。入力（SrcPipe）を動かす → HairBelt が作り直し → Undo で作り直し、`pending` は 0 のまま
  - 6 章「段 12 から回したもの」の再現：`stage8_まつげ眉` の Lush の出力全体を `moveSelectedVertices` で y 0.05 動かす（`pending`=1）→ 確定（試算のずれ 1.4e-7）→ Undo 1 回で `pending`=1・Lush は Blocked（前は 0 で作り直されていた）
  - `queryProjectSyncDiff`：via=csv・via=binary とも `modifierOutputEdited` の違いは無い（残る違いは materialReferences と法線の要素数。法線は段 H）
  - 回帰：`barnacle_place` 最後まで、`stage8_まつげ眉`・`stage8_ブレンド`（Play 直後）の `pending` は全部 0、ブレンドは Evaluate。エラーのログ無し

### 段 G：試算と確定で同じ作り直しを使う — 状態：済（2026-10-31）

- 原因（コードで確認）
  - 梯子の試算は取り込み方を Baked にし、梯子を点列で渡す（`PlayerCommandDispatcher.ModifierInverse.cs`:222-227）。確定で取り込み元へ書くときは元の取り込み方のまま（229-234）
  - 試算のずれは、作り直しの失敗（401-402）か頂点数の違い（`ModifierInverse.cs`:139）で -1 になる。SkirtFrill でどちらかは未確認
- 直し方：試算を確定と同じコマンドで作り直す
- 消える症状：SkirtFrill の照会で各段のずれ（試算）が -1（旧段 16。6 章「段 15 から回したもの」）
- そのあと：roboF の SkirtFrill の最初の確定で梯子が書き直されなかった件（同上）を、再現の手順を探すところから調べる
- 確かめ方：`roboF_T5_SK_initialDebug_フリル` の c1〜h2 で、厚み 0.02 にしたあと照会の試算のずれが出る。1 回目の確定で梯子が書き直される
- 完了の条件：上の 2 件が起きない
- 調べて分かったこと
  - 直す前のコードで `roboF_T5_SK_initialDebug_フリル` を流し、厚み 0.02 → 照会、出力全体を y 0.02 動かす → 照会 → 確定、を行った。試算のずれは 0（-1 にならない）、確定の実際のずれも 0。-1 と最初の確定で梯子が書き直されない件は、段 C〜F のあとの今のコードでは再現しなかった
  - 試算と確定の違いはコードに残っていた：梯子・まつげの試算は取り込み方を Baked にした別のコマンド、頂点へ藤壺・帯・梯子化・貼り付けの試算は作り直さずに見本の頂点をずらした近似（入力から導くだけの頂点は数えない）
  - 見本を作る口（`PolyLingPlayerViewerCore.ModifierInverse.cs`）は頂点へ藤壺・帯で GPU の同期（`SettleWorldPositions`）を呼んでいた。生成器は段 E から CPU のバインド姿勢の値を読むので要らない
- 実装
  - 試算を 1 本にした（`PlayerCommandDispatcher.TrialRebuild`）：入力の頂点と項目の引数を書く → `ObjectGroupOps.BuildCommand` で組み直す（確定の `RunObjectGroupStep` と同じ。梯子は記録した取り込み方で取り直す）→ 見本の口で出力の形を作る → 編集後の出力とのずれ → 書いたものを戻す。上流の出力へ書く分もここで書くので、各段のずれは今までどおり「その段だけ」
  - 方法ごとの試算（Baked のコマンド、見本ずらし）を消した
  - 試算できなかったときは理由を経路の行に出す（`InversePlan.TrialError`。「その段のずれ（試算できない: 理由）」）
  - 見本の口の `SettleWorldPositions` を `ComputeWorldMatrices` にした
- 確かめた結果
  - Unity のコンパイル・例外 0
  - 1 段の項目で試算と確定の実際のずれが一致：頂点へ藤壺（出力の頂点 0 を動かす。0.5913 / 0.5913）、まつげ Lush2（0.4660 / 0.4660）、藤壺（0.7621 / 0.7621）、`roboF_T5_SK_initialDebug_フリル` の SkirtFrill（厚み 0.02 のあと頂点 0 を動かす。0.2125 / 0.2125。1 回目の確定）
  - 照会を繰り返しても同じ値（照会が入力・引数を書き換えたまま残さない）
  - 連ねた鎖：辺パイプは各段 0.0585・0.1513、確定の実際 0.2606（各段はその段だけの試算なので、上流の丸めが重なる分だけ実際が大きい）。`stage9_作成直後の編集` の HairBelt#0 の段のずれが 0 から 1.237 になった。前の近似は入力から導くだけの頂点（梯子化の開始タグなど）を数えていなかった。HairRe の確定の実際のずれは 0
  - 回帰：`barnacle_place` 最後まで、確定の Undo。エラーのログ無し

### 段 H：バイナリを JSON と揃える — 状態：済（2026-10-31）

- 原因（コードで確認）
  - バイナリは頂点ごとに法線を 1 本（`Normals[0]`）しか書かず、読む側も 1 本だけ入れる（`RemoteBinarySerializer.cs`:506-517、667-678）
  - 位置は `MeshObject.Positions` を書く（494-504）。段 B で `Positions` は毎回頂点から作り直す。観測した位置の違いがこれだったかは未確認
- 直し方：法線を頂点ごとに全部運ぶ。位置は流して確かめ、違いが残れば原因を調べる
- 消える症状：バイナリの頂点の位置・法線の要素数が違う（旧段 19。6 章「段 10 から回したもの」）
- 確かめ方：頂点を動かしたあと `queryProjectSyncDiff` via=binary、`loadProjectBinary` のあとの値
- 完了の条件：バイナリの往復で位置・法線が一致する
- 調べて分かったこと（上の原因の 1 つ目は不正確だった）
  - 2 本目以降の法線は追加の欄（`NormalsExtra`。`RemoteBinarySerializer.cs` の `WriteExtras`・`ReadExtras`）で運んでいた
  - 違いは「法線を 1 本も持たない頂点が往復で 1 本になる」だけだった（`vertices[].n` 要素数 A=0 B=1）。法線の欄は頂点ごとに 12B 固定で、無い頂点にも `Vector3.up` を書き、読む側はそれを入れる。追加の欄は「本数-1」を書くので、0 本と 1 本の区別が付かなかった
  - 法線の欄（12B 固定）はブラウザのクライアントも読む形（`RemoteBinaryProtocol.cs` の `All`）なので変えない
  - 位置の違いは、段 C〜G のあとの今のコードでは出なかった
- 実装
  - 追加の欄の本数 -1 を「法線を 1 本も持たない」にした。読む側は法線の欄の仮の 1 本を捨てる。今までのファイルに -1 は無いので読み方は変わらない。-1 を知らない読み手は今までどおり仮の 1 本を入れる（`RemoteBinarySerializer.cs`、`RemoteBinaryProtocol.cs` の `NormalsExtra` の説明）
- 確かめた結果
  - Unity のコンパイル・例外 0
  - `stage9_作成直後の編集` のあと `queryProjectSyncDiff` via=binary が「差なし」（前は `vertices[].n` の違い 98 件）
  - `setVertexPositions`・`moveSelectedVertices`（全頂点 y 0.1）のあとも via=binary が「差なし」。via=csv の違いは `materialReferences` の 1 件だけ（前からある）
  - `saveProjectBinary` → `loadProjectBinary` の前後で保存した JSON の `"n": []`（法線 0 本の頂点）がどちらも 98 個、大きさも同じ

### 段 I：残り（原因の仕組みではないもの） — 状態：済（2026-10-31）

- 範囲
  - 藤壺・まつげは梯子のウェイトを引き継がない（旧段 18）。`InheritBeltWeights` はパイプ・フリルだけ（`PrimitiveMeshFactory.cs`:354、447）。スキンドの出力でウェイトの無い頂点は書き戻しで断る（`ModifierSpace.cs`:275-283）。引き継ぎを足すか、部品ごとのウェイトの決め方を段の始めに決める
  - `barnacle_place` の注意の項目（e25・e29）の説明が段 4 以降の動きと合っていない（旧段 20）
  - 一覧のスナップショットの Undo のあと、頂点を選んでも移動のギズモが出ない（旧段 20。段 A のあとも `vb_listundo_moved` で出なかった）。ギズモの経路は毎回モデルと選択を読み直し、古い参照は持たない（`PolyLingPlayerViewerCore.Overlay.cs`:321-352、`MoveToolHandler.Move.cs`:63-77、230-279）。原因は未特定。`GizmoDebugLog` を付けて流し、Undo を挟まないときと比べて切り分けてから直す
- 確かめ方
  - `barnacle_place`・`stage8_まつげ眉` を一括スキンド化し、出力をスキンドのまま入力を編集して作り直す
  - `barnacle_place` の説明を直して流し、最後まで通る
  - 藤壺の引数の書き換え → Undo → 出力の頂点を選んでギズモが出るかを画面で見る
- 完了の条件：作り直しが通る。説明が今の動きと合う。ギズモが出る
- 決めた扱い（ウェイト）：藤壺・まつげにもパイプ・フリルと同じ「はしごのウェイトを引き継ぐ」（`InheritBeltWeights`、既定はオフ）を足す。部品 1 個の全頂点に、その段の左右の点のウェイトを混ぜた 1 つの値を入れる（藤壺は半々、まつげは置く位置＝中点で半々・左で左・右で右）。部品を曲げずに段と一緒に動かすため。パイプの端の蓋（`FillCapWeights`）と同じ混ぜ方
- 実装（ウェイト）
  - `PlaceObjectParams.InheritBeltWeights`、`LushParams.InheritBeltWeights`
  - `PlaceObjectMeshGenerator.AppendInstance` に部品のウェイトを渡す口、`Generate` に梯子の左右のウェイトを渡す口
  - `PrimitiveMeshFactory.GeneratePlaceObject` は前処理の手前で梯子へウェイトを載せる（`BeltWeightBinder`。`EachPreprocessedBelt` と同じ）。`GenerateLush` は `EachPreprocessedBelt` へ取り込み元を渡す。どちらも `resolveBeltSource` を受け取る
  - `LushMeshGenerator.AppendBelt` は置く位置で混ぜたウェイトを渡す
  - 図形生成パネルの藤壺に「はしごのウェイトを引き継ぐ」のチェックと説明（まつげはパネルが無い）
- 確かめた結果（ウェイト）
  - Unity のコンパイル・例外 0
  - `barnacle_place` のあと藤壺の引き継ぎをオン → 一括スキンド化で藤壺が作り直し 1・失敗 0 → 球の頂点を動かすと作り直し 1・失敗 0。引き継ぎをオフに戻すと「頂点のうち 2916 個にウェイトが無い」で断られる（前と同じ）
  - `stage8_まつげ眉` で Lush・Lush2・BrowPipe の引き継ぎをオン → 一括スキンド化で 3 つとも作り直し → 梯子の頂点を動かすと Lush2 が作り直し 1・失敗 0
- 実装・確かめた結果（説明）：`barnacle_place` の e20・e25・e28・e29・e31 を `setScenarioItem` で今の動き（入力の編集で評価器がその場で作り直す。e32 の作り直しは結果を変えない）に直した。最後まで流れる
- ギズモ（調べている途中）
  - MCP で流すときは `setCurrentView` でカレントビューを立てないと、Undo を挟まなくてもギズモが出ない（`UpdateGizmoOverlay` が `_activePanel` の無いとき何もしない。画面ではポインタが乗ったビューがカレントになる）
  - カレントビューを立てて `stage8_頂点藤壺` → `selectAllElements`（VB_Out）でギズモが出る。`setObjectGroupStepArgs`（scale=2）→ Undo → `selectAllElements` では出ず、選んだ頂点の色も付かない。そのあと `selectMesh`（Drawable 2）を足すと出る
  - Undo のあとは VB_Out がオブジェクトとして選ばれていない。`selectAllElements` は頂点・面の選択だけを書き、オブジェクトの選択は変えない。Undo の前後で、選ばれていたオブジェクトと球の頂点の選択（15 → 12 個）が変わっている。一覧のスナップショットの Undo が選択の状態をコマンドの前に戻しているかは、まだ確かめていない
  - 画面の取り込みは状態を見分けるのに使えなかった：コマンドで選択したとき、描画側には選択が変わった印を立てるだけで（`PlayerSelectionOps.ApplyElementSet` → `UnifiedSystemAdapter.NotifySelectionChanged`）、ギズモの描き直し（`OnRefreshGizmoOverlay`）は呼ばれない。描き直しはビューの更新（`PlayerViewportManager.Render.cs`）とマウスの移動（`EnterHoverChanged`）から来る。同じ手順でも出る・出ないが揃わなかった
- ギズモ（原因）：一覧のスナップショットを戻す Undo・Redo（`MeshFilterToSkinnedRecord.RestoreList`）が、戻すたびにオブジェクトとボーンの選択を空にしていた（`ClearMeshSelection`・`ClearBoneSelection`）。一括スキンド化で実体を入れ替えていたころの扱いで、段 A のあとは実体を替えないので要らない。Undo のあと出力がオブジェクトとして選ばれず、頂点を選んでも選択の色とギズモが出なかった
- 実装（ギズモ）
  - `RestoreList` は、戻す前にオブジェクトとボーンの選択を ObjectId で控え、戻したあと一覧に居るものを今の索引で選び直す（`SelectionObjectIds`・`ReselectByObjectIds`）。頂点・面の選択は今までどおり空にする（`CopyState`。中身が変わると番号が別の頂点を指す）
  - 切り分け用に照会 `querySelectionState`（カレントのモデル・選択カテゴリ・オブジェクトとボーンの選択・オブジェクトごとの頂点・辺・面・線分の選択の数・操作モード・カレントビュー・今の選択からギズモを組めるか）と、`setGizmoDebugLog`（`AxisGizmo.GizmoDebugLog` を入り切り）を足した。ギズモは `UpdateGizmoOverlay` と同じ手順でその場で組み直して返すので、画面の描き直しの時点に左右されない（`PolyLingPlayerViewerCore.DescribeViewState`、`PlayerCommandDispatcher.OnDescribeViewState`）
- 確かめた結果（ギズモ）
  - Unity のコンパイル・例外 0、`queryCommandAudit` の付け忘れ・不正 0
  - `stage8_頂点藤壺` → `selectAllElements`（VB_Out）：選択 VB_Out、ギズモを組める → `setObjectGroupStepArgs`（scale=2）→ Undo：選択 VB_Out のまま（頂点の選択は空）→ `selectAllElements`：ギズモを組める。画面の取り込みでもギズモと選択の色が出た
  - Redo のあとも選択 VB_Out のまま
  - `barnacle_place` → 一括スキンド化 → Undo で BT_Barnacle（6）、Redo で BT_Barnacle_skinned（14）が選ばれたまま（索引が変わっても同じ実体を選び直す）。エラーのログ無し
- 段 I で見つけたもの（扱う段は未定）
  - ~~貼り付け（`createSurfacePaste`）は、出力がスキンド化済みだと評価から外れる（「生成物を出力の格納空間へ移す読み方を持たない」）。`stage8_まつげ眉` の LashPasted・BrowFlat・Brow~~ → 済（2026-10-31）
    - 実装
      - `PLParamAttribute.OutputSpaceSource`（新規）：生成物がその入力の格納空間で出来る入力に付ける。`CreateSurfacePasteCommand.SourceIndex` に付けた（`PanelCommand.PrimitiveBelt.cs`）
      - `ModifierSpace.SpaceSourceKey`（`BeltSourceKey` を置き換え）：梯子を読む型は梯子の取り込み元、それ以外は `OutputSpaceSource` を付けた int の引数のキー。評価器の分類（`Classify`）・評価器の署名（`ModifierEvaluator` の `BeltKey`）・作り直しの行列（`PlayerCommandDispatcher.ObjectGroup.cs` の `ComputeStepSpace`）・作成時の行列（`PolyLingPlayerViewerCore.CreateCommands.cs` の `ResolveCreateBeltSource`。追加先が新規オブジェクト・既存へ追加のとき）が同じこの関数を使う
      - 行列は梯子と同じ M ＝（出力の枠の バインド→格納）×（入力の 格納→バインド）。照会の空間の名前を「生成物を入力（梯子・貼り付け元）の格納空間から出力の格納空間へ移して書く」にした
      - ~~`extractPipeBelt` には付けていない。読む入力 `SourceMasterIndices` が int の配列で、`MeshRefSpace.Local`（スキンドなら控えた行列でローカルへ戻して読む）なので、1 つの入力の格納空間で出来る形に当たらない。出力がスキンド化済みの `extractPipeBelt` は今も外れる~~ → 済（2026-10-31）
        - 実装：`SourceMasterIndices` を `MeshRefSpace.Model`・`OutputSpaceSource` にした。`SpaceSourceKey` は int の配列も受け、先頭を生成物の座標系とする（`Classify` は並びの 格納→バインド の行列が違えば理由を付けて外す）。作成時の `ResolveCreateBeltSource` も先頭を読む。生成器へはパイプを格納値のまま渡す（`PrimitiveMeshFactory.ResolveStoredSources`。`LocalView` で戻さない）。写した頂点は元のパイプの頂点のウェイトを持つ（`PipeBeltExtractor.AddVertex`。開始タグは始点のウェイト）
        - 確かめた結果：`stage8_パイプの梯子化` の HairBelt の 14 頂点の位置は変更の前後で同じ。一括スキンド化のあと HairBelt#0 は Evaluate で「作り直した」。SrcPipe_skinned の始点を動かすと「作り直し 2 / 失敗 0」（HairBelt の始点が追随）。HairBelt_skinned の頂点 3 を動かして確定すると SrcPipe_skinned の頂点 1 個を書き直した（ずれ 最大 0.0283、試算と実際が一致）。エラーのログ 0
        - 見たこと：一括スキンド化の流しでは、下流の HairRe#0（createPipe、`inheritBeltWeights`=true）が「出力の編集が未確定」で止められた。流しのあとの照会では `pending`=0 で、続く入力の編集では作り直された
    - 確かめた結果
      - スキンド化の前（`stage8_まつげ眉` を流し直したモデル）：LashPasted・BrowFlat・Brow の照会で「入力から作った頂点が動いていない」（作り直しの見本＝作成時の出力）。LashLadder の頂点 1 を動かすと「作り直し 6 / 失敗 0」。Brow の頂点 0 を動かして確定すると、BrowFlat → BrowPipe → Brow の 3 段を連ねて LashLadder の頂点 2 個を書き直し、実際のずれ 最大 0.0109
      - 一括スキンド化（まつげ・Lush2・BrowPipe は `inheritBeltWeights`=true）：6 項目とも Evaluate、「作り直し 6 / 失敗 0」。貼り付け 3 つは作り直しの見本＝今の出力。LashLadder_skinned の頂点 1 を動かすと「作り直し 6 / 失敗 0」
      - Editor.log のエラー 0
  - ~~`polyling_call` で `model_index` を省くと 0 になり、`setObjectGroupStepArgs` がカレントでないモデルの参照として断る（「参照先が別のモデルにあります」）~~ → 済（2026-10-31。MCP サーバの作り直しが要る）
    - 原因：MCP サーバ（PolyLingMcpServer の `polyling_call`・`pl_` 個別ツール）が `model_index` の既定を 0 にして常に送り、Unity 側の受け口（`PolyLingControlProtocol.HandleCall`）も省かれたら 0 にしていた
    - 実装：Unity 側は `PolyLingCommandGateway.CurrentModelIndex`（パネルが `Dispatch` と対で設定・解除）を足し、`HandleCall` は `modelIndex` が無いか負ならカレントのモデル（モデルが無ければ 0）にする。サーバ側は `model_index` の既定を -1 にし、説明を「省くとカレントのモデル」にした（`UnityProjectTools.cs`・`GenericCommandTools.cs`・`ServerProfile.cs`。ZIP で渡し、作り直しは利用者）
    - 確かめた結果（サーバは作り直す前）：カレントをモデル 2 にして `setObjectGroupStepArgs`（VB_Out#0、scale=2）を `model_index` を省いて送ると今まで通り断られ、`model_index`=-1 で送ると通った。エラーのログ 0
  - ~~コマンドで頂点・面を選んだとき（`selectElements`・`selectAllElements`）、ギズモの描き直しが呼ばれない。画面ではマウスを動かすと描き直されるが、MCP から流すと次に描き直されるまで前のギズモが残る~~ → 済（2026-10-31）
    - 原因：要素の選択はクリック・コマンドとも `PlayerSelectionOps.ApplyElementSet` を通り、最後の `OnSelectionChanged` の受け側（`PolyLingPlayerViewerCore.VertexInteraction.cs`）が描画への通知とセクションの再描画しかしていなかった。ギズモは次のホバー（`EnterHoverChanged` → `OnRefreshGizmoOverlay`）で描き直されていた。オブジェクトの選択は `EnterTopologyChanged` で描き直される
    - 実装：その受け側の最後で `UpdateGizmoOverlay` を呼ぶ
    - 確かめた結果：`stage8_頂点藤壺` → `selectAllElements`（VB_Out）の直後の取り込みでギズモが出る。`selectElements`（Replace・空）の直後の取り込みでギズモが消える（`querySelectionState` は「組めない」）。もう一度 `selectAllElements` で出て、取り込みのギズモの位置が `querySelectionState` の原点 (228.83, 204.33) と合う。エラーのログ 0

## 6. 未決事項

- ~~逆方向を持つ出力を編集して確定したとき、正方向で作り直した結果に置き換えるか、編集した形を残すか~~ → 決定（段 6）：正方向で作り直した結果に置き換える。丸められる量は確定の前に照会で示す
- ~~藤壺を新規オブジェクトとして作ったとき、パーツ ID が段の順に入っているか~~ → 決定（段 6）：採番は生成器（置いた順、梯子をまたいで 0 から）を正とし、逆方向はパーツ ID に頼らず生成の控えで対応を取る。新規作成と作り直しの一致は段 7 で確かめた（サブ ID の振り直しを書き戻しから外した）
- 段 6・段 7 から持ち越したもの
  - ~~上流の項目の出力である入力（辺パイプの帯、貼り付けた梯子を読むまつげ など）へは逆方向を辿らない → 段 14~~ → 済（段 14。帯・パイプの梯子化・貼り付けに逆方向を足し、書く入力が別の項目の出力ならその項目の逆方向へ連ねる。逆方向を持たない項目があれば断る。Undo は 1 件）
  - ~~厚み付けありのパイプ・フリル、フリルの出力からの逆方向 → 段 15~~ → 済（段 15。厚み付けで控えの枠を作った頂点へ広げ、フリルは段ごとの枠を控えて beltFrames を宣言した）
  - 回転体・2D押し出しの出力からの逆方向 → 扱わない。同じ編集は段 6 の断面の作業空間で入力側からできる。出力からの逆方向は、角度ごと・押し出しの段ごとの違いを 1 本の断面へ平均して捨てる当てはめにしかならない
  - ~~バインド姿勢そのものの変化（`BoneTransform`・バインドへの焼き込み）を入力の変化として検知しない → 段 10~~ → 済（段 10。Model の入力と梯子は署名に `VertexToBindWorldMatrix` を混ぜ、書き込みのあるコマンドの終わりに取り直す）
  - ~~作成時（`PrimitiveMeshCreateNewObject`）は梯子の行列を見ない → 段 10~~ → 済（段 10。作成・作り直しとも梯子の行列を常に掛ける）
  - ~~形として読む入力がスキンド化済みの項目は評価できない（貼り付け・まつげの参照面・原型も同じ） → 段 10~~ → 済（段 10。Model の入力はスキンドでも読み、Local の入力はスキンド化で控えた行列でローカルへ戻して読む）
  - ~~項目を作った直後（評価器が初めて見る前）の入力・出力の編集を変化・未確定として扱わない → 段 9~~ → 済（段 9。グループ・項目を足したコマンドがその場で控える）
- 段 8 から回したもの
  - ~~ブレンドの作り直し（→ 段 13）：宛先を書き換える設定では、ブレンド前の形の控え（`<宛先>_backup`）が出力として記録され、宛先は入力に並ぶ（`CaptureObjectGroup` が増えた ObjectId の最大を出力にするため）。宛先は保護されず、控えが保護される。宛先を出力にすると宛先が入力でもあるので循環になる。控えを入力・宛先を出力とする形にして書き戻しの印（`RebuildRole`）を足すかを、作り直しの設計と一緒に決める → 決定（段 9 以降の決めた扱い）：基準の形を読む入力キーを足し、宛先を出力にする~~ → 済（段 13。`baseMasterIndex` を入力、書き込み先を出力にし、記録する前にコマンドをその形へ組み直す。旧形の保存は読み込みで直す）
  - ~~Undo の履歴（→ 段 12）：頂点編集の履歴は書き込み先のオブジェクトが替わると消え（`MeshUndoController.SetMeshObject`・`SetMeshObjectFor`）、一覧の履歴とも分かれている。続けて Undo すると、手前の編集を飛ばして別の履歴の操作（グループの追加など）を戻すことがある~~ → 済（段 12。消すのをやめ、頂点編集の記録が書き込み先（参照・モデル・ObjectId）を持って Undo・Redo の時点に引き直す）
  - ~~スキンド化済みの出力を `moveSelectedVertices` で動かしたとき、画面が次の更新まで古い形のまま（→ 段 9）~~ → 済（段 9。一覧のスナップショットの Undo・Redo で GPU バッファを作り直していなかった）
  - ~~`runScenario` の応答の deletedObjectIds に、前のモデルの描画オブジェクトの ObjectId が入ったことがある（→ 段 9）~~ → 済（段 9。控えをモデルごとに持つ）
- 段 9 から回したもの（扱う段は未定）
  - ~~`runScenario` は書き込み宣言が None なので、流し終えても評価器が流れない（`PlayerCommandDispatcher.ModifierEval.cs` の `ShouldEvaluateModifiersAfter`）。`stage9_作成直後の入力編集` のあと HairBelt は `dirty`=1 のまま残った → 段 11~~ → 済（段 11。項目ごとに評価器を流す）
  - ~~一覧のスナップショットの Undo のあと `selectAllElements` で頂点を選んでも、移動のギズモが画面に出なかった（段 9 の変更のあとも同じ。`c2_fixed_01`・`c2_fixed_04`。原因は調べていない）→ 段 I~~ → 済（段 I。Undo・Redo がオブジェクトの選択を空にしていたので、ObjectId で選び直すようにした）
- 段 10 から回したもの（扱う段は未定）
  - ~~藤壺（`createPlaceObject`）・まつげ（`createLush`）は梯子のウェイトを引き継がない。パイプ・フリルは `InheritBeltWeights` のときだけ引き継ぐ。一括スキンド化のあと、出力がスキンドのこれらの項目は作り直しが失敗する（段 5 の扱い）。`barnacle_place` の段 10 の確かめは、藤壺の出力を MeshFilter へ戻して行った → 段 I~~ → 済（段 I。藤壺・まつげにも引き継ぎの指定を足した）
  - ~~`edgeRibbonFace`・頂点へ藤壺（`createVertexBillboardPlace`）・ブレンドの生成器は、ポーズ込みの座標（GPU のワールド座標・`WorldMatrix`・`VertexToWorldMatrix`）で入力を読む。評価器の署名はポーズを含めない（`VertexToBindWorldMatrix`）ので、ポーズを付けたまま作り直すと結果がポーズに依存する → 段 E~~ → 済（段 E。バインド姿勢のワールドで読む）
  - ~~梯子の行列 M の出力の枠（新規オブジェクトとして作った項目では出力の親の `BindWorldMatrix`）は署名に入らない。出力の親を付け替えても作り直されない → 段 E~~ → 済（段 E。出力の枠の署名を控えに足した）
  - ~~バイナリの頂点の位置が、`setVertexPositions`・`moveSelectedVertices` のあとの頂点の位置と違う（`RemoteBinarySerializer.WritePositions` は `MeshObject.Positions` を書く）。法線の要素数の違いも出た。原因は調べていない → 段 H~~ → 済（段 H。法線 0 本の頂点を運べるようにした。位置の違いは今のコードでは出なかった）
  - ~~一括スキンド化で作ったボーンの objectId が 0 のまま（`convertMeshFilterToSkinned` の createdObjectIds が [0]、保存の JSON でも 0）。原因は調べていない → 段 C~~ → 済（段 C。一覧へ入れる道を 1 か所にして採番する）
- 段 11 から回したもの（扱う段は未定）
  - ~~`saveProjectCsv` は、カレント以外のモデルの未確定の出力の編集でも失敗する（`roboF_T5_SK_initialDebug_フリル` の c7 が stage9 のモデルの HairRe で止まった）。原因は調べていない → 段 F~~ → 済（段 F。未確定を保存するようにして、保存の門を外した）
  - ~~`barnacle_place` の注意の項目（e25・e29）の説明が段 4 以降の動き（入力の編集で評価器が流れる）と合っていない → 段 I~~ → 済（段 I。e20・e25・e28・e29・e31 を直した）
- 段 12 から回したもの（扱う段は未定）
  - ~~逆方向を持つ出力を、正方向で丸められずに再現できる形に編集して確定すると、Undo 1 回で出力が編集した形・未確定に戻らず、確定前の入力に対する正方向の形まで戻る（段 6 の決めた扱い「Undo 1 回で出力は編集したまま未確定に戻る」と食い違う）→ 段 F~~ → 済（段 F。未確定の判定を出力ごとの記録と今の中身の比較にし、記録を中身と一緒に Undo する）
    - 再現：藤壺の出力全体を `moveSelectedVertices` で y 0.05 動かす（確定のずれ 1.2e-7）→ 確定 → Undo 1 回で出力の頂点 0 の y が 0.444 でなく 0.394、`pending`=0、`lastPass` は「undo: 作り直し 1」。同じ `moveSelectedVertices` でも頂点 0 だけを動かすと（ずれ 0.044）Undo 1 回で 0.444・`pending`=1・Blocked。`setVertexPositions` で頂点 0 だけ動かしたときも 0.444・`pending`=1。`moveSelectedVertices` かどうかではなく、編集を正方向で再現できるかで分かれる
    - 原因（`ModifierEvaluator.cs`）
      - 確定は作り直したあと `MarkRebuilt` → `RecordForward` で「確定後の入力の鍵 → 作り直した出力の署名」を控える（`PlayerCommandDispatcher.ModifierInverse.cs` の `ExecuteModifierOutputEdit`）
      - 出力の署名は位置を 1e-5 単位に丸める（`MeshSignature.Positions`）。ずれが 1e-5 より小さいと、作り直した出力の署名は編集した出力の署名と同じになる
      - 確定の Undo（一覧のスナップショット）で入力は確定前に、出力は編集した形に戻る。`IsOutputEditPending` は、今の入力の鍵の控え（確定前の正方向の出力）と今の出力が違っても、今の出力の署名が控えのどれか（`st.Forward.ContainsValue(now)`）なら未確定と見ない（「入力の Undo の直後は出力がまだ前の入力に対する結果のまま残る」ための扱い。段 7）。ここでは編集した形が確定後の正方向の結果と同じ署名なので、この扱いに当たって未確定でなくなる
      - 入力が確定前に戻ったので要評価（`MarkChangedInputs`）になり、`EvaluatePending` が作り直して、出力を確定前の入力に対する正方向の形にする
    - 署名が同じものは見分けられないので、「確定の Undo のあとの出力」と「入力の Undo の直後に残った前の入力の結果」を、署名の控えだけでは区別できない
- 段 14 から回したもの（扱う段は未定）
  - ~~`stage8_辺パイプ` のモデルで `stage11_辺パイプ_グループから` を流したあと、`queryModifierGraph` の EdgeRibbon#0 の downstream が「1,2」と出た（2 の EdgeRibbon_1#0 は EdgeSrc を読み、EdgeRibbon は読まない）。原因は調べていない → 段 D~~ → 済（段 D。依存グラフも書き込み条件を引数で判定し、宣言を実際の書き込みに合わせた）
- 段 15 から回したもの（扱う段は未定）
  - ~~SkirtFrill（融合あり・2 プロファイル・鎖の配り）の `queryModifierInverse` で、各段のずれ（試算）が -1。試算は梯子を点列で渡して作り直した形と比べ、頂点数が違うと出さない。試算の作り直しの頂点数は確かめていない → 段 G~~ → 済（段 G。今のコードでは再現しなかった。試算を確定と同じ手順にし、試算できないときは理由を出す）
  - ~~roboF の SkirtFrill の最初の確定で、梯子の頂点が書き直されていなかった（確定の直後の値が確定前と同じ）。同じ手順のやり直し 2 回では書き直された。原因は特定していない → 段 G~~ → 再現しなかった（段 G。1 回目の確定の実際のずれが、梯子を書いた試算と一致）
- 段 A・段 B から回したもの（扱う段は未定）
  - ~~モデルごと入れ替える Undo（`ModelReplaceRecord.Swap`、`ModelUndoRecords.cs`）で、`ModelContext` を鍵にした評価器の控え（グラフ・状態）とモデルへの参照（`EditSpaceSession.Model`・`MeshUndoTarget.Model`）が古くなる。`ModelBundle.ToModel` を読んでいないので直し方は決めていない~~ → 済（2026-10-31）
    - 原因：置き換え（`ExecuteReplaceModelBundle`・PMX/MQO の置換読込 `ReplaceCurrentModelWithImported`）とその Undo・Redo（`Swap`）は、`ModelBundle.ToModel` で新しい実体を作り `RemoveModelAt` → `InsertModel` で差し替えていた。外した実体は `Clear(true)` で空になる。頂点編集の記録（`MeshUndoTarget`・複数メッシュの記録の `Model`）は外した実体の一覧で引き直すので見つからず、置き換えより前の頂点編集は Undo しても書き戻されない。DTO に入らない `SourceDocument`・`PmxModelInfo` も Undo・Redo で落ちていた
    - 同じ形のもの（同日に一緒に直した）
      - モデルの追加（読み込み）の Redo：控え `ModelContextSnapshot`（名前・メッシュ・材質・選択だけ）から実体を作り直していた。頂点編集の記録が古い実体を指すうえ、オブジェクトグループ・表情・Humanoid などが落ちる
      - モデルの削除（`DeleteModelCommand`）：`RemoveModelAt` で消すだけで Undo に積んでいなかった（`RecordModelRemove` は `MeshUndoController_ProjectStack.cs` の注記の中にしか無かった）
    - 決めた扱い：Undo の記録が外した実体そのものを持ち、Undo・Redo で同じ実体を外し・入れ直す。実体が替わらないので、頂点編集の記録・評価器の控え・オブジェクトグループ・表情・`SourceDocument` などがそのまま当たる。外した実体は記録が持つ間メモリに残り、記録が捨てられても `UnityMesh` を破棄しない（Undo の記録に捨てるときの口が無い。終了まで残す。2026-10-31 に了承）
      - 取らなかった形：実体を作り直して外した実体から辿る（置き換え先の印）は、DTO・スナップショットに入らないもの（`SourceDocument`・`PmxModelInfo`、追加ではオブジェクトグループなど）が落ちる。既存の実体へ DTO を組み直す形は、DTO に値が無いときだけ書かない `TPoseBackup` が前のモデルの値のまま残る
    - 実装
      - `ProjectContext.DetachModelAt`（一覧から外すだけで Clear しない）・`ReplaceModelAt`（同じ位置で入れ替え、外した実体を返す。Clear しない）
      - `ModelReplaceRecord`：置き換え前と後の `ModelContext` を持つ（以前は `ModelDTO`）。Undo・Redo は実体で位置を引いて入れ替える。置き換えの 2 か所（`ExecuteReplaceModelBundle`・`ReplaceCurrentModelWithImported`）は `ReplaceModelAt` で外した実体を記録へ渡す
      - `ModelOperationRecord` の Add・Remove：`ModelContext Model`（実体）を持つ（以前は `ModelContextSnapshot`）。外すときは `DetachModelAt`、入れ直すときは同じ実体を `InsertModel`
      - `DeleteModelCommand`：`DetachModelAt` で外し、`RecordModelRemove`（`MeshUndoController_MeshList.cs`、新規）で積む
    - 確かめた結果
      - 置き換え：`stage8_頂点藤壺` の VB_Ball を `moveSelectedVertices` で y +0.1 → `importMqoFile`（Replace、`chest_side.mqo`）→ Undo でモデルが戻り、オブジェクトグループの項目 VB_Out#0 が残る → Undo で頂点 20 の y が 0.4536 → 0.3536
      - 追加：`importMqoFile`（NewModel）→ 頂点を y +0.1（0.8475 → 0.9475）→ Undo 3 回でモデルが無くなる → Redo 3 回でモデルが戻り頂点が 0.9475
      - 削除：`stage8_頂点藤壺` の VB_Ball を y +0.1 → `deleteModel` → Undo でモデルが戻り、VB_Out#0 が残る（評価器の控えも削除前のまま）→ Undo で頂点 20 が 0.3536 → Redo で 0.4536 → Redo でモデルが再び外れる
      - エラーのログ 0
    - ~~見つけたもの：MCP（リモート扱い）からの `redo` は、モデルが 0 個だと担当者判定で断られる（「モデルがありません: 0」、`RemoteOwnership.cs:343`）。読み込みを Undo してモデルが 0 個になると、MCP からは Redo できない~~ → 済（2026-10-31。Undo・Redo の担当者判定は ModelIndex 0 ではなくプロジェクトの全モデルについて行い、モデルが 0 個なら通す。モデルを 2 つとも削除して 0 個 → Undo・Redo・Undo が通った）
  - ~~`queryRevisions` の `activeModes` が、シナリオを流し終えたあとも `scenarioRun` を出し続ける~~ → 済（2026-10-31。流し終えて `stopScenarioRun` するまでは `scenarioRunEnded` を出す。`PlayerCommandDispatcher.Scene.cs`、説明は `PanelCommand.Scene.cs`）
  - ~~`polyling_call`（MCP サーバ）の手元のソースを読まずに GitHub から取ってきた（会話の要約で、セッション最初の案内 `McpGuide/primer.md` の中身が消えていた）~~ → 対策（2026-10-31）：MCP サーバの `list_files`・`read_file`・`write_file`・`apply_patch` の説明に、サーバ自身のソースの場所・手元を直接書き換えること・要約のあとは primer.md を読み直すことを足した（`UnityProjectTools.cs` の `LocalSourceNote`。反映はサーバの作り直しが要る）
  - ~~描画バッファに載っていないオブジェクト（隠したもの・空のもの）は GPU のワールド位置を読めない（`PlayerViewportManager.GpuSelect.cs` の `TryGetMeshWorldPositions`）。帯・頂点へ藤壺などの生成がこれを読むと失敗する → 段 E~~ → 済（段 E。生成器は CPU でバインド姿勢のワールドを読む。シュリンク・法線の移植など、生成器でない操作は今も GPU の表示位置を読む）
  - ~~Undo はプレビュー（なめらかにする・吸着・ブレンド・モーフ・変形）を終わらせない。プレビューの戻しが Undo のあとの中身へ上書きする（`SmoothEdgesTool.RestorePreviewPositions` ほか）~~ → 済（2026-10-31）
    - 実装
      - Undo・Redo の入口（ディスパッチャの `OnUndo`・`OnRedo`、`PolyLingPlayerViewerCore.CreateCommands.cs`。ボタン・ショートカット・コマンドとも `PerformUndoCommand`・`PerformRedoCommand` でここを通る）で、`PerformUndo/PerformRedo` の前に `CancelPreviewsBeforeUndo` を呼ぶ。プレビューを元へ戻してから Undo する
      - `CancelUncommittedPreviews`（`PolyLingPlayerViewerCore.ViewAids.cs`）：パネル切替（`HideRightArea`）が取り消していた 5 つ（ブレンド・シュリンカー 2・面に張り付け・なめらかにする）をまとめたもの。パネル切替はこれを呼ぶ
      - `CancelPreviewsBeforeUndo`：上の 5 つに、モーフ（`PlayerMorphSubPanel.CancelIfActive`、新規）・変形（`DeformToolHandler.Revert`）・法線編集（`PlayerNormalEditSubPanel.CancelPreview`）・法線移植（`PlayerNormalTransplantSubPanel.CancelIfActive`、新規）を足したもの
    - 確かめた結果（どれも「確定した編集 → プレビュー → Undo」。Undo のあとプレビューが終わり、編集した頂点が編集前の値に戻り、ほかの頂点もプレビュー前の値）
      - なめらかにする：VB_Ball の辺 4 本（頂点 48-36-34-32-44）、頂点 34 の y を 0.3 にしてからプレビュー → Undo で `isPreviewing`=false、頂点 32〜36 が編集前の値
      - 変形（回転 30°、86 頂点）：頂点 36 を動かしてからプレビュー → Undo で `isPreviewing`=false、頂点 36 が編集前、ほかは回転前の値
      - ブレンド（宛先 VB_Ball・ソース VB_Ball_copy）：頂点 33 を動かしてからプレビュー → Undo で `isPreviewing`=false、頂点 33 が編集前、頂点 36 はブレンド前の値
      - モーフ（`Zunko_MMD.pmx` の表情 0、ウェイト 1）：顔の頂点 0 を動かしてからプレビュー → Undo で `activeModes` から morphPreview が消え、頂点 0 が編集前の値
      - パネル切替（`uiShowPanel`）でなめらかにするのプレビューが終わる（動かす頂点 0 個の状態で確かめた）。エラーのログ 0
      - 面に張り付け：VB_Proto の頂点 0 を動かしてから VB_Ball へ張り付けのプレビュー（スライダー 1）→ Undo で `isPreviewing`=false、4 頂点とも編集前の値
      - 法線編集：VB_Proto の法線を反転（確定。(0,0,1)→(0,0,-1)）→ プレビューの欄を入れて反転のプレビュー（(0,0,1)）→ Undo でプレビューの欄が消え（取り消しのボタンが表示されない）、法線は反転前の (0,0,1)
      - 法線移植：参照ペアを作る → アフターの頂点を動かす → 法線を計算（81/86 頂点）→ Undo でプレビューの欄が消え、アフターの頂点が動かす前に戻る。エラーのログ 0
      - シュリンカー：ビフォー・アフターの選び先を行ごとのラベルから `RadioButtonGroup`（`shrink.before`・`shrink.after`。値は候補の行の番号）に替えて、UI 自動操作から選べるようにした（`PlayerShrinkSubPanel.cs`。頂点方式・面方式とも）。VB_Ball の頂点 20 を動かす（y 0.5）→ ビフォー VB_Ball・アフター VB_Ball_copy で衝突計算・シュリンク量 1（頂点 20 が 0.3536）→ Undo でプレビューの欄が消え、頂点 20 が 0.3536（編集前）。`queryUiAutomationAudit` の付け忘れ・登録失敗 0
      - ~~見つけたもの：法線編集のプレビューの状態の文字（`normalEdit.previewState`）は、プレビューが終わって欄が隠れても前の文のまま残る（`UpdatePreviewBar` は表示するときだけ書く）~~ → 済（隠すときに文字も消す。プレビューを終えたあと `uiGetValue` で空）
- 今後の課題（扱う段は未定）
  - Undo の履歴（一覧・頂点編集）を 1 本にまとめるか。段 12 では 2 本のまま、頂点編集の履歴を消すのをやめた（順序は時系列の調停に任せる）。1 本にまとめると、一覧の記録・頂点編集の記録の積み先が全部変わる
