# PolyLing MCP 化 — 現状の課題と対処方針

対象: `Runtime.zip`（Runtime 796 ファイル / Editor 20 ファイル）
確認日: 2026-09-02
根拠はすべて `ファイル:行` で示す。行番号は本 ZIP 時点のもの。

---

## 0. 前提となる経路の実態

### 生存している経路（1 本）

```
リモート受信 (WebSocket)
  → RemoteServerCore.ProcessCommandViaPanelCommand   RemoteServerCore.cs:852
  → RemoteOwnership.TryAuthorize                     RemoteOwnership.cs:92
  → DispatchCommand デリゲート                        PolyLingPlayerServer.cs:68
  → PolyLingPlayerViewerCore の λ                    PolyLingPlayerViewerCore.cs:781
  → PlayerCommandDispatcher.Dispatch                 PlayerCommandDispatcher.cs:187
```

パネル発行も同じ終端に入る（`PolyLingPlayerViewerCore.cs:2907` で `new PanelContext(DispatchPanelCommand)`、`:9905` で `_commandDispatcher.Dispatch(cmd)`）。

Editor ウィンドウもこの経路。`PolyLingEditorWindow.CreateGUI()` は `new PolyLingPlayerViewerCore()` を生成する（`PolyLingEditorWindow.cs:49-53`）。

### 死んでいる経路

`PolyLingCore` は起動されない。

- `new PolyLingCore()` の出現は `PolyLingPlayerCore.cs:39` の 1 箇所のみ
- その `PolyLingPlayerCore`（MonoBehaviour）を参照するコードは 0 件
- 唯一渡される Config は `PolyLingCoreConfig.CreateStub()`（`PolyLingPlayerCore.cs:40`）。中身は `WorldToScreenPos` → `Vector2.zero`、`FindVertexAtScreenPos` → `-1`、`SyncMesh` → 空（`PolyLingCoreConfig.cs:57-71`）
- `Editor/` 配下 20 ファイルに `PolyLingCore` / `IPolyLingCore` の参照は 0 件

### コマンド総数と処理状況

| 区分 | 件数 |
|---|---|
| `PanelCommand` 派生クラス総数（`PanelCommand.cs`） | 118 |
| 生存経路に受け口あり | 113 |
| └ うち実装なし（`Debug.LogWarning` のみ） | 6 |
| └ 実効実装 | 107 |
| 受け口なし（`default` で警告して return） | 5 |
| 死んだ経路（`PolyLingCore_Commands.cs`）の `case` | 56（51 が重複、5 が独自） |

---

## 1. 優先度 P0 — 死んだ経路の除去

### 1-1. 削除前に移設が必要な 5 コマンド

生存経路に受け口がなく、`PlayerCommandDispatcher.cs:3538-3540` の `default` で
`Unhandled PanelCommand: {型名}` を警告して何もせず返る。

| コマンド | 死んだ経路の実装 | 実装本体 |
|---|---|---|
| `SetObjectEditorCommand` | `PolyLingCore_Commands.cs:50-58` | `MeshListOps.SetObjectEditor`（`MeshListOps.cs:99`） |
| `BeginBonePoseSliderDragCommand` | 同 `:114-116` | `MeshListOps.BeginSliderDrag` |
| `EndBonePoseSliderDragCommand` | 同 `:117-119` | `MeshListOps.EndSliderDrag` |
| `NotifyListStructureChangedCommand` | 同 `:252-257` | `_model.OnListChanged` + `SyncMesh` + `NotifyPanels` |
| `NotifyDictionaryChangedCommand` | 同 `:258-260` | `NotifyPanels(ChangeKind.Attributes)` |

**`SetObjectEditorCommand` は現在バグとして表面化している。**
`RemoteServerCore.cs:1090-1097` は `setObjectEditor` を受けて本コマンドを組み立て、
`RemoteOwnership.AuthorizeSetEditor`（`RemoteOwnership.cs:145-183`）が可否を返す。
ただし `RemoteOwnership` は**判定のみで適用しない**。適用は死んだ経路にしかないため、
リモートからの担当者取得・解放は認可を通ったあと警告を出して消える。

**方針**
`MeshListOps` は `UNITY_EDITOR` / `UnityEditor` を 1 箇所も含まない。
`PlayerCommandDispatcher.cs:1012` は既に `new MeshListOps(model, _undoController)` を自前生成している。
よって 5 件とも `PlayerCommandDispatcher` 側へ機械的に移設できる。

### 1-2. 削除候補ファイル

| ファイル | 行数 | 判定根拠 |
|---|---|---|
| `Core/MainCore/PolyLingCore.cs` | 469 | インスタンス生成元が 1-1 の移設後に消える |
| `Core/MainCore/PolyLingCore_Commands.cs` | 1218 | `partial class PolyLingCore` |
| `Core/MainCore/PolyLingCore_BoneInput.cs` | 351 | 同上 |
| `Core/MainCore/PolyLingCore_CameraState.cs` | 29 | 同上 |
| `Core/MainCore/PolyLingCore_Selection.cs` | 238 | 同上 |
| `Core/MainCore/PolyLingCore_UndoOperations.cs` | 516 | 同上 |
| `Core/MainCore/PolyLingCoreConfig.cs` | 73 | 実参照は `PolyLingCore.cs` と `PolyLingPlayerCore.cs` のみ |
| `Core/Interface/IPolyLingCore.cs` | 68 | 実装・参照とも `PolyLingCore.cs` のみ |
| `Poly_Ling_Player/Core/PolyLingPlayerCore.cs` | 60 | 被参照 0 |
| `Tools/Core/ToolContextReconnector.cs` | — | 呼び出しは `PolyLingCore.cs:174` の 1 箇所のみ |

上記 6 ファイルの `public static` / `internal static` は 0 件。外部から掴めるものはない。

### 1-3. 削除してはいけないファイル

- **`Core/MainCore/PolyLingCore_UvHandlers.cs`（128 行）は残す。**
  中身は `partial class PolyLingCore` ではなく `public static class PolyLingCoreUvHandlers`（`:16`）で、
  生存経路の `PlayerCommandDispatcher.cs:1188, 1341, 1386` から呼ばれている。
  ファイル名だけで判断すると生存経路を壊す。

### 1-4. 併せて整理すべき無操作フック

`IEditorBridge.SetupRemoteServer`（`IEditorBridge.cs:118`）は実装 3 つとも空。

- `PolyLingEditorBridgeImpl.cs:241-243`（コメント: 「Editor 側 RemoteServer ウィンドウは本パッケージに存在しないため無操作」）
- `EditorBridgeNull.cs:296`
- `PolyLingPlayerBridge.cs:144`

唯一の呼び出し元は `PolyLingCore.cs:172`。P0 完了後は宣言ごと消える。

`RemoteServerCore.ProcessCommandLegacy`（`RemoteServerCore.cs:1229-1260`、32 行）は
`DispatchCommand == null` のときだけ通る（`:831-836`）。
生存経路は `PolyLingPlayerViewerCore.cs:781` で非 null のラムダを渡すため到達しない。

**P0 の確認方法**: 削除後にコンパイルが通ること。加えて、リモートから `setObjectEditor` を投げて
実際に `EditorName` が入り、パネルに反映されること。

---

## 2. 優先度 P1 — 実行結果が呼び出し元へ返らない

MCP の応答は「何が起きたか」を返す必要があるが、現在その情報が経路上に存在しない。

- `PlayerCommandDispatcher.Dispatch` は `void`（`PlayerCommandDispatcher.cs:187`）
- `RemoteServerCore` は実行後に無条件で `BuildSuccessResponse(msg.Id, "true")`（`RemoteServerCore.cs:895`）
- ディスパッチャ内の `return;` は 331 箇所。うち null 判定直後の無言 return が 138 箇所
- 同ファイルの `Debug.LogError` / `throw` は 6 箇所のみ

つまり「対象が見つからない」「生成に失敗した」がすべて成功として返る。
`QuadDecimateCommand` の `if (resultMesh == null) return;`（`:2212`）が典型。

**方針**
1. コマンド実行結果を表す戻り値型（成否・理由・生成された対象の識別子）を 1 つ定義する
2. `Dispatch` の戻り値を差し替える
3. 138 箇所の無言 return を、失敗理由付きの返却に順次置き換える
4. `RemoteServerCore` の固定 `"true"` を実結果に差し替える

3 は件数が多いので、P3 以降で触るコマンドから順に潰す。1・2・4 を先に入れれば
以後の追加コマンドは最初から結果を返せる。

**P1 の確認方法**: 存在しない `MasterIndex` を指定したコマンドをリモートから投げ、
エラー応答が返ること。

---

## 3. 優先度 P2 — モーフ系 6 コマンドが空実装

`PlayerCommandDispatcher.cs:1083-1091`：

```csharp
// ── モーフ変換・プレビュー・セット作成（PolyLingCore が必要、Player では未実装）
case ConvertMeshToMorphCommand _:
case ConvertMorphToMeshCommand _:
case CreateMorphSetCommand _:
case StartMorphPreviewCommand _:
case ApplyMorphPreviewCommand _:
case EndMorphPreviewCommand _:
    Debug.LogWarning($"[PlayerCommandDispatcher] {cmd.GetType().Name} requires PolyLingCore (not implemented in Player).");
    return;
```

**この理由は成立していない。**
死んだ経路の実装（`PolyLingCore_Commands.cs:122-145`）はいずれも
`_meshListOps.ConvertMeshToMorph(...)` 等の 1 行呼び出し。
`MeshListOps.cs` に `UNITY_EDITOR` / `UnityEditor` は 0 件。
`PlayerCommandDispatcher.cs:1012` は既に `MeshListOps` を自前生成している。
不足しているのは `MeshListOps` インスタンスを保持していないことだけ。

**方針**: P0 の 1-1 と同じ移設。`MeshListOps` の保持方法を 1 度決めれば 11 件（5 + 6）まとめて片付く。

**P2 の確認方法**: モーフプレビューをリモートから開始・重み変更・終了して、
表示が追従し `WorkingPositions` が正しく戻ること。

---

## 4. 優先度 P3 — メッシュ編集ツール 38 本が未コマンド化

`Runtime/Poly_Ling_Player/View/ToolHandlers/` の `*ToolHandler.cs` 38 本すべてで
`SendCommand` の出現数が 0。対応する `*Tool` クラスも `PlayerCommandDispatcher.cs` から呼ばれていない
（`ObjectMoveTool` の 3 箇所を除き全て 0 件）。
`SubPanels/Edit/` 配下 33 ファイルも `SendCommand` 0 件で、`GetH()` でハンドラのフィールドを直接書き換えるだけ。

対象:
AddFace / AlignVertices / Deform / DeleteSelection / EdgeBevel / EdgeExtrude / EdgeTopology /
FaceExtrude / FaceMerge / FaceMergeCollapse / FlipFace / Knife / Lattice / LineExtrude /
MergeVertices / PipeAlign / PlaceObjectReshape / PlanarizeAlongBones / Quad4To1 / Rotate /
Scale / SkinWeightPaint / SmoothEdges / Solidify / SplitVertices / Tri4To1 / VertexDissolve /
VertexHole / WorkAxis

### 4-1. 先行事例が既にある

コマンド化済みで発行側も揃っているもの:

- `CreateEdgeBridgeCommand`（`PolyLingPlayerViewerCore.CreateCommands.cs:198`、`PlayerRobotBuildTestSubPanel.Bridge.cs:89`）
- `MatchHoleRingCountCommand`（`CreateCommands.cs:382`、`Bridge.cs:252`）
- `DeleteFacesCommand`（`PolyLingPlayerViewerCore.cs:8963`、`Bridge.cs:186`）
- `CreateObjectArrayCommand`（`CreateCommands.cs:410`）
- `CreatePrimitiveMeshCommand`（`PlayerPrimitiveMeshSubPanel.Command.cs:88-192`）

`EdgeBridgeToolHandler.cs:175` と `HoleRingCountToolHandler.cs:81` に、
マウス経路とコマンド経路で受理判定を共有させた際の設計メモが残っている。同じ型で進められる。

### 4-2. 受け口だけ作られて発行側がない 5 本

| コマンド | 受け口 | 発行側 |
|---|---|---|
| `SelectElementsCommand` | `PlayerCommandDispatcher.cs:339` | 全ファイル中 0 件 |
| `MoveSelectedVerticesCommand` | 同 `:363` | 0 件 |
| `MovePivotCommand` | 同 `:421` | 0 件 |
| `SculptStrokeCommand` | 同 `:501` | 0 件 |
| `AdvancedSelectCommand` | 同 `:590` | 0 件 |

UI はハンドラ経路、コマンドは受け口だけ、という二重状態。
MCP からは動くが、パネル操作は同じ道を通らないので、片方だけ壊れても気づけない。

**方針**
1. 上記 5 本について、対応するサブパネル／ハンドラの確定処理をコマンド発行に置き換え、二重状態を解消する
2. 残るツールを、依存が浅い順（選択状態のみで完結するもの → ドラッグ量が要るもの → プレビュー状態を持つもの）にコマンド化する
3. ドラッグ系（FaceExtrude / EdgeExtrude / Rotate / Scale / Deform）は、
   マウス移動の追従とコマンドの粒度が一致しない。1 ストローク＝1 コマンド（開始値と確定値を持つ）に寄せる。
   `SculptStrokeCommand` が既にその形をとっているので、これを型とする。

**P3 の確認方法**: パネル操作とリモート発行で同一メッシュが得られること。
`PlayerRobotBuildTestSubPanel` の自動検証パイプラインに各ツールを 1 手ずつ足して差分を見る。

---

## 5. 優先度 P4 — リモート露出が 23 / 118、しかも手書き

`RemoteServerCore.BuildPanelCommand`（`RemoteServerCore.cs:1001-1190`）の `case "..."` は 23 種:

```
selectMesh / toggleVisibility / setBatchVisibility / toggleLock / setBatchLock /
setMirrorEnabled / setBatchMirrorType / setObjectEditor / cycleMirrorType /
renameMesh / renameMeshes / applySelectionDictionary / addMesh / deleteMeshes /
duplicateMeshes / applyBlend / initBonePose / setBonePoseActive /
resetBonePoseLayers / bakePoseToBindPose / switchModel / renameModel / deleteModel
```

action 文字列からコンストラクタ引数への写像を 1 件ずつ手書きしている。
コマンドを 1 本足すたびに、`PanelCommand.cs` / `PlayerCommandDispatcher.cs` /
`BuildPanelCommand` / 送信側（`PanelCommandRouter.cs`）の 4 箇所を触ることになる。

**方針**
P5 と同時に解く。`PLParam` が全コマンドに付けば、
コンストラクタ引数の組み立てをリフレクションで一般化でき、`BuildPanelCommand` の手書きは不要になる。
`PanelCommandDump.cs` が既に同じ反射走査（`:47-58`）をしているので、読み出し側の型は決まっている。

---

## 6. 優先度 P5 — `PLParam` が 10 / 118

`[PLParam]` の総出現は 346 箇所だが、`PanelCommand.cs` 内は 76 箇所。
属性が付いているコマンドクラスは 10 本のみ:

```
AddGeneratedMeshCommand / CreateEdgeBridgeCommand / CreateHoleBridgeCommand /
CreateObjectArrayCommand / CreatePrimitiveMeshCommand / DeleteFacesCommand /
MatchHoleRingCountCommand / MediaPipeFaceDeformCommand / ReorderMeshesCommand /
ResetProjectCommand
```

残り 108 本にスキーマ生成の入力がない。
図形生成器側（`Tools/PrimitiveMesh/` 19 ファイル）は既に付与済み。

`PLParamAttribute.cs:9-13` の設計どおり、
「スキーマに出さないものにも `Ignore = true` を明示する」ため、未付与＝付け忘れとして検出できる。

**方針**
1. `PanelCommand.cs` の全 public プロパティに対する `PLParam` 有無を検査する仕組みを先に用意する（付け忘れの検出）
2. 検査に通してから 108 本へ付与する
3. `PanelCommandDump` と同じ走査でツールスキーマ（JSON Schema）を生成する側を作る

---

## 7. 優先度 P6 — 41 コマンドが「現在の選択」依存

対象を指す `MasterIndex` / `MasterIndices` / `Indices` を持たないコマンドが 41 本。

```
ApplyTPose / RestoreTPose / BakeTPose / BakeMirror / FloodSkinWeight /
NormalizeSkinWeight / NormalizeAllSkinWeights / PruneSkinWeight / SetSkinWeightNumeric /
NormalEdit / RepairVertexIds / AddMaterialSlot / SetFaceHidden / FreezeCurrentPose /
ResolveMirrorBoneIndex / ConvertMeshFilterToSkinned / ApplyHumanoidMapping /
ClearHumanoidMapping / BuildSpringBoneTestRig / SavePartsSet / SaveNormalExcludeSet /
ExportPartsSetsCsv / ImportPartsSetCsv / SaveMeshSelSetsCsv / LoadMeshSelSetsCsv /
SaveSelectionDictionary / CreateBlendClone / RenameModel / DeleteModel /
ApplyObjectOrigins / GenerateObjectPoseWedges / AddMesh / ReorderMeshes /
EndBonePoseSliderDrag / EndBoneTransformSliderDrag / ApplyMorphPreview /
EndMorphPreview / DeselectAllMorphs / AddGeneratedMesh / ResetProject /
NotifyListStructureChanged / NotifyDictionaryChanged
```

`RemoteServerCore.cs:886-888` のコメントがこれを前提にしている:
「MasterIndex を持たないコマンド（PartsSet系・SkinWeight系など）は『今の選択』を見て動くため、
要求者の選択を一時的に流し込む」。
実装は `DispatchWithSelectionOf`（`:904-926`）で `SelectionScope.Apply` により選択を差し替える。

MCP の 1 ツール呼び出しは自己完結すべきで、「先に選択コマンド、次に本命」の 2 手は破綻しやすい。

**方針**
全 41 本に対象指定を足すのは影響が大きい。段階的に:
1. まず「対象指定があれば使い、なければ現在の選択にフォールバックする」形に受け口側を統一する
2. MCP のツールスキーマでは対象指定を必須（`Required = true`）として露出する
3. パネル側は従来どおり選択依存のまま動かす（既存挙動を変えない）

---

## 8. 優先度 P7 — 対象指定が位置インデックス

`ObjectId`（安定 ID）を持つコマンドは 3 本のみ:
`SetBatchLockCommand` / `SetBatchMirrorTypeCommand` / `SetObjectEditorCommand`。

残りは `MasterIndex` で、リスト構造が変わると指す先がずれる。
`RemoteOwnership.VerifyObjectIds` はこのズレを検出して `StaleView` を返し、
`RemoteServerCore.cs:877-881` がクライアントへ `refreshRequired` を push する仕組みが既にある。

MCP の呼び出し元は前回の一覧取得から時間が空くため、ズレの発生確率が対人操作より高い。

**方針**
`ObjectIds` の付与を、上記 3 本の実装をそのまま型として横展開する。
P6 で対象指定を足すコマンドについては、最初から `MasterIndices` と `ObjectIds` を対で持たせる。

---

## 9. 優先度 P8 — ファイル入出力と Undo が未コマンド化

### 9-1. ファイル入出力

`Action<string, ...Settings>` の直コールバックで、対応するコマンドが存在しない。

| パネル | コールバック |
|---|---|
| `PlayerImportSubPanel.cs:53, 59, 65` | `OnImportPmx` / `OnImportMqo` / `OnImportObj` |
| `PlayerExportSubPanel.cs:61, 64, 67, 70` | `OnExportPmx` / `OnExportMqo` / `OnExportObj` / `OnExportVrm` |
| `PlayerProjectFileSubPanel.cs:52, 55, 58, 61` | `OnLoad` / `OnSave` / `OnLoadCsv` / `OnSaveCsv` |

118 本中、入出力系のコマンドは `ImportPartsSetCsvCommand` / `ExportPartsSetsCsvCommand` のみ。

MCP から「読み込む → 加工する → 書き出す」が閉じないので、AI 主導の資産生成が成立しない。
`RemoteFileBundle`（PLRF、`RemoteFileBundle.cs`）でフォルダ一式の転送路は既にある。

### 9-2. Undo / Redo

`PolyLingPlayerViewerCore.cs:4922`（ボタン）と `:5187`（ショートカット）が
`_editOps.PerformUndo()` を直接呼ぶ。Undo / Redo コマンドは存在しない。

MCP 側が試行して戻す、ができない。

**方針**
1. Undo / Redo コマンドを先に足す（実装は 1 行の委譲で済み、P1 の結果返却の検証にも使える）
2. 入出力は、パスの扱い（`PLParam(Ignore = true)` の対象になっている）とサンドボックス境界を決めてからコマンド化する

---

## 10. 優先度 P9 — Editor 側に受信口がない

`RemoteServerCore` の実体化は `PolyLingPlayerServer.cs:66` の 1 箇所のみ。
`PolyLingPlayerServer` を生成するのは `PolyLingPlayerViewerCore.cs:430`、
`RemoteMode.Server` のときだけ（`:428-431`、Initialize は `:756`）。

`PolyLingEditorWindow` は `PolyLingPlayerViewerCore.RemoteConfig.Default` で初期化する
（`PolyLingEditorWindow.cs:52`）。この既定値がどのモードかを確認したうえで、
Editor ウィンドウからサーバを起動できるようにする必要がある。

**方針**: P0〜P3 が済んでから着手する。経路が 1 本になっていれば、
Editor / Player のどちらで起動しても同じ結果になることが前提として使える。

---

## 11. MCP 本体は未着手

`mcp` を含むのはコメント 11 箇所のみ（`PLParamAttribute.cs:2`、`PanelCommand.cs:2159, 2656-2658`、
`PlayerCommandDispatcher.cs:156`、`HoleRingCountToolHandler.cs:81`、`EdgeBridgeToolHandler.cs:175`、
`PlayerPrimitiveMeshSubPanel.Command.cs:6, 269`、`.Bridge.cs:640`、`CreateCommands.cs:396`）。
スキーマ生成器・JSON-RPC 層ともにコードは存在しない。

---

## 12. 着手順序

| 順 | 項目 | 理由 |
|---|---|---|
| 1 | P0 死んだ経路の除去（1-1 の移設 → 1-2 の削除） | 以後すべての作業で「どちらの経路か」を考えずに済む。`setObjectEditor` の実バグも消える |
| 2 | P2 モーフ系 6 本 | P0 の 1-1 と同一手法。`MeshListOps` の保持方法を 1 度決めれば 11 件が片付く |
| 3 | P1 結果返却（型定義と `Dispatch` の戻り値まで） | 以後追加するコマンドが最初から結果を返せる。無言 return 138 箇所は後追いで潰す |
| 4 | P8-2 Undo / Redo コマンド | 実装が軽く、P1 の検証材料になる |
| 5 | P5 `PLParam` 全付与 + 付け忘れ検査 | P4 の前提 |
| 6 | P4 `BuildPanelCommand` の一般化 | 手書き 4 箇所同時更新が消え、以後の追加コストが下がる |
| 7 | P3 編集ツールのコマンド化 | 量が最大。上記が済んでいれば 1 本ごとの手順が定型化する |
| 8 | P6 / P7 対象指定の明示化 | P3 で新設するコマンドは最初から対象指定付きで作る |
| 9 | P8-1 ファイル入出力 | サンドボックス方針の決定が要る |
| 10 | P9 Editor 側サーバ | 経路統一後 |
| 11 | MCP スキーマ生成 + JSON-RPC 層 | 上記の成果を束ねる |

---

## 13. 未確認事項

- `PolyLingPlayerViewerCore.RemoteConfig.Default` の既定モード（P9 の判断に要る）
- `PolyLingCore_UndoOperations.cs`（516 行）に、生存経路へ移すべきロジックが残っていないか。
  `public static` は 0 件だが、内容の突き合わせは未実施
- Editor アセンブリが本 ZIP の `PolyLing.Editor.asmdef` 1 本のみか（`PolyLing.Lite.Editor` は本 ZIP に含まれない）
