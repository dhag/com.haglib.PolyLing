# PolyLing MCP 化 — ToolHandler 分類（P3 第 2 段階の前提資料）

更新日: 2026-09-04 / 基準: 受領 `Runtime.zip`（`.cs` 828 本、Phase 1 + 3-a〜3-f 反映済み）

---

## 0. 数え方と前提

- `Runtime/Poly_Ling_Player/View/ToolHandlers/` の `.cs` は 42 本。`IPlayerToolHandler` を実装するのは **39 本**
  - 非実装 3 本: `IPlayerGizmoProvider.cs` / `PrimitivePlaceSettings.cs` / `WorkAxisGizmoShape.cs`
- `SendCommand` を持つ（＝発行側がコマンド化済み）のは **4 本**
  - `AdvancedSelectToolHandler` / `MoveToolHandler` / `PivotOffsetToolHandler` / `SculptToolHandler`
- 本書の対象は残り **35 本**

### 引き継ぎ文書の訂正

| 引き継ぎ文書の記述 | 実際 | 根拠 |
|---|---|---|
| 35 本のうち 33 本が Undo 記録を持つ | **ハンドラ自身が Undo を記録するのは 2 本のみ**（`DeformToolHandler` / `LatticeToolHandler`）。他はハンドラが `ctx.UndoController` を Tool へ渡すだけで、記録は Tool 側 | `DeformToolHandler.cs:310-311`、`LatticeToolHandler.cs:355-356`。渡すだけの例は `FaceMergeToolHandler.cs:52, 74` |
| 受け口が既にあるのは `EdgeBridgeToolHandler` の 1 本だけ | **3 本**（`EdgeBridge` / `HoleRingCount` / `DeleteSelection`（面のみ）） | `PolyLingPlayerViewerCore.CreateCommands.cs:260`（EdgeBridge）、`:430`（HoleRingCount、`h.SetSeeds`→`h.Execute` へ委譲）、`:315`（DeleteFaces、`PolyLingPlayerViewerCore.cs:9266` 経由で `TriggerDelete`） |

---

## 1. 分類の切り口

- **軸1 入力経路の形**（主軸。コマンド 1 本の粒度を決める）
  - **A** マウス経路なし（`IPlayerToolHandler` の 5 コールバックが空、または Tool 側が no-op）
  - **B** クリックで種／点を積んでから確定
  - **C** ドラッグを Tool へ転送する連続変形（1 ドラッグ＝1 確定）
  - **D** ギズモ／スライダ操作
  - **E** 確定操作を持たない（メッシュデータを書き換えない）
- **軸2 実処理の所在** — Tool にある／ハンドラが抱える
- **軸3 対象の決まり方** — 明示指定／複数メッシュ（`SelectedDrawableMeshIndices`）／単一メッシュ（`ActiveMeshContext`）／ハンドラ内部状態
- **軸4 既存コマンド・受け口の有無**

### 群ごとの本数

| 群 | 本数 |
|---|---|
| A マウス経路なし | 19 |
| B クリック蓄積 | 4 |
| C ドラッグ確定 | 4 |
| D ギズモ／スライダ | 5 |
| E 確定なし | 3 |
| 計 | 35 |

---

## 2. 群 A — マウス経路なし（19 本）

パネルのボタン／ショートカット 1 発で確定する。`OnLeftClick` 以下 4 コールバックが空実装。
発行側は「パネルのボタン → コマンド」に置き換えるだけで済むため、着手コストが最も低い。

| ハンドラ | 入口（`ファイル:行`） | 実処理 | 対象の決まり方 | Undo 記録 | 既存コマンド／受け口 |
|---|---|---|---|---|---|
| `AlignVerticesToolHandler` | `:75 TriggerAlign` / `:76 TriggerAutoSelect` | `AlignVerticesTool` | 単一（`ActiveMeshContext`） | Tool（`AlignVerticesTool.cs:192, 223`） | なし |
| `DeleteSelectionToolHandler` | `:73 TriggerDelete` → `:85 _tool.Execute` | `DeleteSelectionTool` | 複数（`SelectedDrawableMeshIndices`） | Tool | `DeleteFacesCommand`（**面のみ**。受け口 `CreateCommands.cs:315`） |
| `FaceMergeToolHandler` | `:45 TriggerMerge` | `FaceMergeTool` | 複数 | Tool | なし |
| `FaceMergeCollapseToolHandler` | `:45 TriggerMerge` | `FaceMergeCollapseTool` | 複数 | Tool | なし |
| `FlipFaceToolHandler` | `:36 FlipSelected` / `:37 FlipAll` | `FlipFaceTool` | 単一 | Tool（`FlipFaceTool.cs:111-112, 138`） | なし |
| `HoleRingCountToolHandler` | `:89 SetSeeds` / `:122 Execute` | `HoleRingCountTool` | **明示**（種が `MeshIndex`+`Vertex`） | Tool | `MatchHoleRingCountCommand`（受け口 `CreateCommands.cs:430`）**発行済み** |
| `LineExtrudeToolHandler` | `:54 ExecuteExtrude` | `LineExtrudeTool` + ハンドラ | 単一（選択線分） | **無し** | なし |
| `MergeVerticesToolHandler` | `:45 TriggerMerge` / `:54 TriggerMergeToCentroidNow` / `:67 TriggerMergeByThresholdNow` | `MergeVerticesTool` | 単一 | Tool（`MergeVerticesTool.cs:164, 178, 208`） | なし |
| `PipeAlignToolHandler` | `:70 TriggerExecute` | `PipeAlignTool` | 複数 | Tool | なし |
| `PlaceObjectReshapeToolHandler` | `:63 TriggerExecute` | `PlaceObjectReshapeTool` | 複数 | Tool | なし |
| `PlanarizeAlongBonesToolHandler` | `:44 TriggerPlanarize` | `PlanarizeAlongBonesTool` | 単一 | Tool（`PlanarizeAlongBonesTool.cs:160, 202`） | なし |
| `Quad4To1ToolHandler` | `:45 TriggerMerge` | `Quad4To1Tool` | 複数 | Tool | なし |
| `SmoothEdgesToolHandler` | `:57 RefreshStats` / `:60 TriggerSmooth` | `SmoothEdgesTool` | 単一 | Tool（`SmoothEdgesTool.cs:237, 294`） | なし |
| `SolidifyToolHandler` | `:123 Execute` | `SolidifyTool` | 単一（選択面） | **無し**（出力は `AddGeneratedMeshCommand` 側） | 出力のみ済（`PolyLingPlayerViewerCore.cs:4424`） |
| `SplitVerticesToolHandler` | `:37 TriggerSplit` | `SplitVerticesTool` | 単一 | Tool（`SplitVerticesTool.cs:81, 130`） | なし |
| `SurfaceSnapToolHandler` | `:109 TriggerCompute` / `:110 SetSlider` / `:111 TriggerApply` / `:112 TriggerCancel` | `SurfaceSnapTool` | 複数＋参照メッシュ明示（`ReferenceIndices`） | Tool | なし |
| `Tri4To1ToolHandler` | `:45 TriggerMerge` | `Tri4To1Tool` | 複数 | Tool | なし |
| `VertexDissolveToolHandler` | `:45 TriggerDissolve` | `VertexDissolveTool` | 複数 | Tool | なし |
| `VertexHoleToolHandler` | `:51 TriggerHole` | `VertexHoleTool` | 複数 | Tool | なし |

### A 群の注意点

- **`LineExtrudeToolHandler` に Undo が無い。** `LineExtrudeTool` 側にも `MeshUndoContext` / `MeshObjectSnapshot` の参照が 0 件。生成した `MeshContext` を `LineExtrudeToolHandler.cs:112` の `ctx.AddMeshContext` で直接足しており、`AddGeneratedMeshCommand` を通していない
- **`SolidifyToolHandler` は出力だけ既にコマンド化されている。** `SolidifyToolHandler.cs:51-55` の `OnMeshCreated` が `PolyLingPlayerViewerCore.cs:4424` で `AddGeneratedMeshCommand` に流れる。残るのは「選択面＋パラメータから厚み付けメッシュを作る」部分だけ
- **`HoleRingCountToolHandler` は既に完了している。** 発行（`CreateCommands.cs:484`）・受け口（`:430`）・ハンドラ委譲がそろっている。数え直すと未着手は実質 18 本
- **`DeleteSelectionToolHandler` は面だけコマンド化済み。** `DeleteFacesCommand` は面インデックスしか持たず、頂点・辺・線分の削除経路はコマンドが無い
- `LineExtrudeToolHandler.OnLeftClick`（`:139-144`）は `_tool.OnMouseDown` / `OnMouseUp` を呼ぶが、`LineExtrudeTool.cs:50, 52` がどちらも `=> false` の no-op。実質マウス経路なしとして A 群に入れた

---

## 3. 群 B — クリックで種／点を積んでから確定（4 本）

クリックのたびにハンドラ／Tool 内部に状態がたまり、条件がそろった時点で確定する。
**1 コマンド＝1 確定**にするには、たまった状態（点列・ピック辺・開始頂点）をコマンド引数に載せる必要がある。3-f の `ShortestPath` が発行できなかったのと同じ形。

| ハンドラ | 内部状態 | 実処理 | 対象の決まり方 | Undo 記録 | 既存コマンド／受け口 |
|---|---|---|---|---|---|
| `AddFaceToolHandler` | 配置点列（`:99 PlacedPointCount` / `:102 GetPointLabels`）、`:109 FinishAsTriangle`、`:121 RemoveLastPoint` | `AddFaceTool` | 単一＋スナップ先メッシュ | Tool（`AddFaceTool.cs:823 RecordAddFaceOperation`） | なし |
| `EdgeBridgeToolHandler` | ピック辺（`:189 SetPicks` / `:159 ClearPicks` / `:318 TryBuildPlan`） | **ハンドラが抱える**（`BoundaryEdgeOps` / `BridgeLoopOps` / `EdgeChainOps`） | **明示**（`PickedMeshIndex` + 辺集合） | — | `CreateEdgeBridgeCommand`（受け口 `CreateCommands.cs:260`）**発行済み**、`CreateHoleBridgeCommand`（`:146`） |
| `EdgeTopologyToolHandler` | ホバー辺、`Split` 時の第 1 頂点（`:39 ModePublic`、`:80`） | `EdgeTopologyTool` | 単一 | Tool（`EdgeTopologyTool.cs:733, 768, 784`） | なし |
| `KnifeToolHandler` | 開始頂点／セグメント（`:87 StageText` / `:177 Cancel`）、4 モード（`LadderCut`/`SimpleCut`/`BeltLoop`/`Erase`） | `KnifeTool` + `KnifeToolSub/*` | 単一 | Tool のサブ実行器（`LadderCutExecutor.cs:57-58, 116` ほか） | なし |

### B 群の注意点

- `EdgeBridgeToolHandler` は既に発行・受け口ともある。残るのは `SetPicks` を経ないクリック蓄積経路の扱い
- `KnifeToolHandler` はモードごとに確定条件も引数も違う。1 コマンドにまとめると `SelectElementsCommand` と同じ「平行配列＋実行時検証」（妥協点 E-1）を繰り返すことになる。**モードごとに別コマンドへ分けるのが妥当**
- `AddFaceToolHandler` の点は画面座標ではなくワールド／頂点参照で確定する。`SculptStrokeCommand`（妥協点 E-2）と同じくワールド固定にできる

---

## 4. 群 C — ドラッグ確定（4 本）

`OnLeftDragBegin` → `OnLeftDrag` → `OnLeftDragEnd` を Tool の `OnMouseDown/Drag/Up` へ転送する。
**3-d / 3-e で確立した「総量を保持し、確定時にプレビューを戻してコマンド 1 本を発行」の型がそのまま使える。**

| ハンドラ | 確定の単位 | 実処理 | 対象の決まり方 | Undo 記録 | パラメータ |
|---|---|---|---|---|---|
| `EdgeBevelToolHandler` | ホバー辺 1 本 + ドラッグ量 | `EdgeBevelTool`（`:109 OnMouseDown` / `:140 OnMouseDrag` / `:169 OnMouseUp`） | 単一（`ActiveMeshObject`）+ `_hitEdgeOnMouseDown` | Tool | `Amount` / `Segments` / `Fillet` / `DragSensitivity` |
| `EdgeExtrudeToolHandler` | ホバー辺 + ドラッグ量 | `EdgeExtrudeTool`（`:102` / `:130` / `:160`） | 単一 + `HoverEdge` | Tool | `Mode` / `SnapToAxis` / `DragSensitivity` |
| `FaceExtrudeToolHandler` | ホバー面 + ドラッグ量 | `FaceExtrudeTool`（`:110` / `:137` / `:167`） | 単一 + `HoverFace` | Tool | `Type` / `BevelScale` / `IndividualNormals` |
| `SkinWeightPaintToolHandler` | 1 ストローク | `SkinWeightPaintTool`（`:313` / `:360` / `:370`） | 単一（スキン付きメッシュ） | Tool（`SkinWeightPaintTool.cs:344, 387`） | ブラシ半径・強度・対象ボーン |

### C 群の注意点

- 3 つの押し出し系は「1 要素（辺／面）＋スカラー量」で表せる。**軸3 が明示指定へ最も素直に移せる群**
- `SkinWeightPaintToolHandler` は `SculptStrokeCommand` と同じ形（点列＋ブラシ）にできる。ただしメモリ記載のとおり**ブラシ塗りのミラー同期は保留対象**なので、コマンド化の際もミラー同期は入れない

---

## 5. 群 D — ギズモ／スライダ操作（5 本）

`IPlayerGizmoProvider` を実装し、`GizmoHitTest` / `BeginGizmoDrag` / `GizmoDrag` / `EndGizmoDrag` で確定する。
`IPlayerToolHandler` のドラッグコールバックは（`ObjectMove` / `Lattice` を除き）空。

| ハンドラ | 確定の入口 | 実処理 | 対象の決まり方 | Undo 記録 |
|---|---|---|---|---|
| `RotateToolHandler` | `:55 EndSliderDrag` / `:181 EndGizmoDrag` | `RotateTool`（`:91 EndSliderDrag` → `:93 ApplyRotation`、記録 `:417-421`） | 選択頂点（複数メッシュ）+ ピボット選択 + マグネット | Tool |
| `ScaleToolHandler` | `EndSliderDrag` / `EndGizmoDrag` | `ScaleTool` | 同上 | Tool |
| `ObjectMoveToolHandler` | `:181 OnLeftDragEnd` → `:189 _tool.OnMouseUp` | `ObjectMoveTool`（`PivotOffsetToolHandler` と**同じ Tool を共有**） | `ObjectMoveSettings` の Pick 条件（ボーン／非スキン／スキン／ミラー側） | Tool（5 箇所） |
| `DeformToolHandler` | `:301 Commit` | **ハンドラが抱える**（`_applier` + `IMeshDeformer`） | 選択頂点 + マグネット | **ハンドラ**（`:310-311`） |
| `LatticeToolHandler` | `:344 Commit` | **ハンドラが抱える**（`_deformer` + `_applier`） | `:230 FitToSelection` で決めた格子内の頂点 | **ハンドラ**（`:355-356`） |

### D 群の注意点

- `ObjectMoveTool` は `PivotOffsetToolHandler`（`MovePivotCommand`、コマンド化済み）と同一インスタンス型を共有する。`OriginOnly` 以外の経路（`BoneMoveMode.BoneOnlyRebind` / `SkinBakeRebind`）が未コマンド化。**`ApplyOriginOnlyFromCommand`（`ObjectMoveTool.cs:392`）と同じ形で受け口を足せる**
- `Rotate` / `Scale` は絶対値（`RotX/Y/Z`、`ScaleX/Y/Z`）を持つため、`Toggle`（妥協点 E-4）のような「送る前の状態に依存する」問題が出ない。**MCP から見て最も自己完結させやすい群**
- `Deform` / `Lattice` は実処理がハンドラにある。Phase 1 と同じく「第 2 実装を作らない」ためには、コマンド受け口もハンドラへ委譲する形にする
- 妥協点 F-1（複数選択での原点移動が非アクティブ側に描画反映されない）の該当箇所は `PivotOffsetToolHandler.cs:221 BuildToolContext` と `:241-244`（`ctx.SyncMesh` が `ActiveMeshContext` 1 本しか同期しない）。妥協点文書の `:140` は `OnLeftDragEnd` の行番号で、`BuildToolContext` の定義位置ではない

---

## 6. 群 E — 確定操作なし（3 本）

メッシュデータを書き換えない。`Undo` の出現が 0 件。

| ハンドラ | 何を書き換えるか | 備考 |
|---|---|---|
| `CameraToolHandler` | カメラ姿勢（`GetOrbit` / `GetTri` / `GetTriViews`） | `RecordCameraChangeCommand : ICommand` は Undo 層に別途ある |
| `WorkAxisToolHandler` | `WorkAxisContext`（作業軸の位置・向き） | 他ツールのピボット源。コマンド化すると `Rotate`/`Scale`/`Deform` の結果が変わるため、D 群より先に決める必要がある |
| `PrimitivePlaceToolHandler` | 図形生成パネルの `Position`/`Rotation`/`Scale` 値（`:50-59` の `Get/Set*`） | メッシュ生成自体は `CreatePrimitiveMeshCommand` で済んでいる |

---

## 7. 着手順（提案）

軸1 の A→B→C→D の順を基本にしつつ、**依存があるものを前に出す**。

| 段 | 対象 | 理由 |
|---|---|---|
| 4-a | A 群のうち「引数がパラメータだけ」の 12 本（`FaceMerge` / `FaceMergeCollapse` / `Quad4To1` / `Tri4To1` / `VertexDissolve` / `VertexHole` / `SplitVertices` / `FlipFace` / `AlignVertices` / `SmoothEdges` / `PlanarizeAlongBones` / `MergeVertices`） | 形が同じ。1 段でまとめて片付く。P6/P7 の `MasterIndices`+`ObjectIds` を最初から持たせる |
| 4-b | A 群の残り（`PipeAlign` / `PlaceObjectReshape` / `SurfaceSnap` / `Solidify` / `LineExtrude` / `DeleteSelection` の非面経路） | 参照メッシュ・プレビュー段・生成出力があり、1 本ずつ形が違う。`LineExtrude` の Undo 欠落もここで扱う |
| 4-c | C 群 4 本 | 3-d / 3-e の型がそのまま使える |
| 4-d | `WorkAxisToolHandler`（E 群） | D 群のピボット源。先に確定させる |
| 4-e | D 群 5 本 | `ObjectMove` は `ApplyOriginOnlyFromCommand` の形を踏襲 |
| 4-f | B 群 4 本 | 内部状態をコマンド引数へ出す設計が要る。最後 |
| 4-g | `Camera` / `PrimitivePlace`（E 群） | メッシュを書き換えないため MCP の優先度が低い |

---

## 8. 未確認事項

- `DeleteSelectionTool` が頂点・辺・線分をどの範囲で削除するか（面以外の削除経路のコマンド引数を決めるのに必要）
- `SurfaceSnapTool` のプレビュー段（`TriggerCompute` → `SetSlider` → `TriggerApply`）を 1 コマンドに畳めるか、段ごとに分けるか
- `WorkAxisContext` の書き換えを Undo に積むべきかどうか（現状は積んでいない）
