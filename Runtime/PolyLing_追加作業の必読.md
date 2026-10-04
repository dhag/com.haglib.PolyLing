# PolyLing 追加作業の必読

コマンドを 1 本足す、右ペインにパネルを 1 枚足す。そのとき**必ず読む**のはこの 1 枚だけ。
細かい話は `PolyLing_コマンド追加の参照.md` にある。要るときだけ引く。

---

## 大原則

**検査を通せ。文書を信じるな。**

この 2 本が全項目 0 でなければ、作業は終わっていない。

| 検査 | 見るもの |
|---|---|
| `queryCommandAudit` | 引数の付け忘れ、action の衝突、未対応の型、説明の壊れ |
| `queryUiAutomationAudit` | 未登録のセクション、安全度未指定のボタン、未登録の部品 |

配線を 1 か所忘れると、どちらかが必ず落ちる。落ちなければ配線は足りている。
`queryCommandAudit` の「[参考]」の行は完了条件に入れない（件数が残っていてよい）。
ここに書いていない注意事項も、検査に項目があるなら検査が拾う。
**拾えない欠陥を見つけたら、文書に書く前に検査へ項目を足すこと。**

---

## A. コマンドを 1 本足す

| # | 場所 | 内容 |
|---|---|---|
| 1 | `Core/Data/PanelCommand.*.cs` | `[PLCommand(Description=…)]` / 全プロパティに `[PLParam]` / 返すなら `[PLResult]` |
| 2 | `View/Core/PlayerCommandDispatcher.*.cs` | `case` を足す |
| 3 | `View/Core/PolyLingPlayerViewerCore.CreateCommands.cs` | Viewer 側に実体があるときだけ。受け口と `OnXxx = ExecuteXxx` の配線 |
| 4 | `Poly_Ling_Remote/RemoteOwnership.cs` | `case` を足す。読むだけなら照会の側へ |
| 5 | 検査 | `queryCommandAudit` が全項目 0 |

3 は「Viewer に実体がある」ときだけ。`ScenarioLibrary` のような静的クラスを呼ぶだけなら、
ディスパッチャで直に処理して `ReportData` してよい（`queryCommandAudit` と同じ形）。

### 外さないところ

- **コンストラクタの引数名はプロパティ名と一致させる。** 一致しないと道具一覧に出ない
- **MCP の引数キーはプロパティ名の camelCase。** `TextKey` ではない
- **コンストラクタは 1 つだけ。** 多重定義すると引数の多い方が選ばれる
- **入れ子の配列は送れない。** 可変長は平坦な列 2 本で持つ（値の列と区切りの列）
- **説明に XML タグを混ぜない。** `</summary>` の混入が過去 39 件あった
- **描画オブジェクトの索引を受ける引数には `IsMeshRef = true` を付ける。** 付けないと
  `queryScenarioAudit` が直書きを見逃す。付け忘れは `queryCommandAudit` の「[参考]」に出る
- **対象を変えたコマンドは、変えたオブジェクトを `ReportData(..., masterIndices, objectIds)` で返す。**
  返さないとシナリオの次の項目が `@prev.masterIndices` を引けない（`booleanMesh` がこれで e5 を落としていた）。
  索引を報告するときは引数の値ではなく、解決した実体から `MeshContextList.IndexOf` で引き直す
- **失敗は `Fail` で返す。** ログに警告を出して `return true` だけにすると、呼んだ側には成功に見える
- **面や頂点を書き換えるなら Undo を残す。** 形は `applyLscmUnwrap` と同じ
  （`SetMeshObject` → 前後で `CaptureMeshObjectSnapshotOf(対象の MeshContext)` → `RecordTopologyChange`）。
  引数なしの `CaptureMeshObjectSnapshot()` は捕獲元を持たず、取り消しが「その時点で先頭に選ばれているメッシュ」へ
  書き戻される。索引で対象を受けるコマンドでは選択と対象が一致しないので使わない
- **頂点 ID・面 ID を自動で振らない（`AssignMissingIds` を呼ばない）。**

### 既にある口（作る前に探す）

| したいこと | 口 |
|---|---|
| 名前から索引を引く | `selectDrawablesByName`（戻り値の `masterIndices` を `@` で渡す） |
| 描画オブジェクトの親子を名前で張る | `setParentsByName(names[], parentNames[])`。深さは自動、ワールド姿勢は保つ。ボーンは `setBoneParent` |
| 照会の対象を省く | `queryBoundaryEdges` / `queryFacesInBox` / `queryNearestBoundaryVertex` / `acquireBeltStrips` / `resolveTJunctions` / `saveSelectionToDataStore` は `masterIndex` を省くと編集対象 |
| ブーリアンで面が欠ける箇所を調べる | `diagnoseBoolean`（モデルは変えない。段階ごとの穴・BSP の分岐の回数・所要時間を返す） |

---

## B. 右ペインにパネルを 1 枚足す

8 か所。1 つでも抜けると挙動が欠ける。

| # | 場所 | 内容 | 忘れると |
|---|---|---|---|
| 1 | `PlayerLayoutRoot.LeftPaneButtons.cs` | ボタンのプロパティと生成 | ボタンが出ない |
| 2 | `PlayerLayoutRoot.RightPane.cs` | セクションのプロパティと `AddSection(visible:false)` | 置き場が無い |
| 3 | `PolyLingPlayerViewerCore.cs` | サブパネルのフィールド | — |
| 4 | `PolyLingPlayerViewerCore.Layout.TestPanels.cs` | 生成と `Build(セクション)` | 中身が空 |
| 5 | `PolyLingPlayerViewerCore.WorkAxis.cs` | `ShowXxxPanel`（`SetInteractionMode` → `ShowRightPanel` → `Refresh`） | 開けない |
| 6 | `PolyLingPlayerViewerCore.Layout.cs` | `clicked += ShowXxxPanel` と `_sectionRefreshPairs` へ追加 | 押せない／開いても更新されない |
| 7 | `PolyLingPlayerViewerCore.ViewAids.cs` | `Hide(セクション)` の一覧へ追加 | 他のパネルと重なる |
| 8 | `PolyLingPlayerViewerCore.UiAutomation.cs` | `RegisterUiPanel` と、各部品への `[UiControl]` | 監査が落ちる／MCP から操作できない |

### 外さないところ

- **ボタンの `[UiControl]` には `Safety` が要る。** 無いと監査の「安全度未指定のボタン」に出る
  - `ReadOnly` 表示のみ / `SafeWrite` 戻せる / `Destructive` モデルを変える / `UserOnly` ダイアログ
- **`Refresh` は開くたびに呼ばれる。** 外部で変わるデータはここで読み直す

---

## C. Play で試すとき

- **`unity_focus` を先に。** Unity が前面に無いと Play が止まり、段が進まない
- **重い処理は `timeout_ms` を伸ばす。** 既定 30 秒。ブーリアンは超えることがある
- **索引は毎回引き直す。** オブジェクトが増減すると `masterIndex` がずれる。
  `selectDrawablesByName` か `queryBone` で名前から引く

---

## D. シナリオに残すとき

手順が固まったらシナリオに残す。置き場は `@settings/PolyLing/PolyLing/scenarios/<フォルダ>/<名前>.csv`（1 シナリオ 1 ファイル）。

### フォルダと親子

- **フォルダ＝置き場所**（整理用。1 本は 1 か所。順番は持たない）。`createScenarioFolder` / `moveScenarios` / `renameScenarioFolder` / `deleteScenarioFolder`
- **親子＝使い方**（別のシナリオを呼ぶ項目。順番を持ち、1 本の子を何本の親からでも呼べる）
- 断片をフォルダに集めて並べ、`createScenarioFromFolder(folder, name, names?)` で順に呼ぶ親にする。親どうしをさらに上の親でまとめてよい
- 親は名前で子を呼ぶので、フォルダを移しても親子関係は切れない

### 起こし方

| 元 | 手 |
|---|---|
| 検証パネル・手で撃った一連の操作 | `startScenarioRecording` → パネルを流す／撃つ（入れたくない操作は `pauseScenarioRecording` 〜 `resumeScenarioRecording` で挟む）→ `stopScenarioRecording` → `saveScenarioRecording(name)`（やめるなら `discardScenarioRecording`）。状態は `queryScenarioRecording`。シナリオパネルの「シナリオを記録する」でも同じことができる |
| 生成系 1 本 | パネルを 1 回流してから `saveScenarioFromGroup` |

記録は `Dispatch` の一番外側を通ったコマンドを、計算済みの引数ごと控える。
**パネルの C# を読んで何を送ったか組み立て直す必要は無い。**
検証パネル（`PlayerStagedTestSubPanelBase` 派生）なら、段の「UI でやるなら」が
Instruction、「なぜ」が Note として入る。

記録に入らないもの。

- シナリオコマンド・UI 自動操作・`queryCommandAudit`
- **`Dispatch` を通らずにモデルを直接書き換えた処理**（例：フリル検証の段 8
  `StageEditSource` は頂点を直に動かしている）。下書きに項目が抜けるので、
  コマンドで同じことをする項目を足す
- 戻り値（項目に欄が無い）。失敗した項目は Note に残る
- 文字列の引数に直せない型を持つコマンド（`addGeneratedMesh` など）は、
  控えはするが Note で「撃ち直しても同じにならない」と出る

### 起こしたら必ず点検

`queryScenarioAudit(name)` の指摘を 0 にする。

| 種類 | 直し方 |
|---|---|
| `literalMeshIndex` | 索引が直に入っている。`selectDrawablesByName` などの照会項目を前に置き、`@<項目 ID>.masterIndices` に置き換える |
| `unknownAction` | action 名を直す |
| `badRef` / `missingRef` / `forwardRef` | `@` 参照の書き方・指す項目・順序を直す |

**点検が見るのは `IsMeshRef` の印が付いた引数だけ。** 印の無い索引
（`queryCommandAudit` の「[参考] 索引の印が無い引数」に並ぶもの）と、
頂点・面の番号は素通りする。ここは自分で見る。値の判断基準は次のとおり。

- 入力だけで決まる値（寸法・断面の点列・絶対座標）は焼いてよい
- 実データで決まる値（描画オブジェクトの索引・頂点番号・面番号）は焼かない。照会で引いて `@` で渡す

### 項目の間で値を渡す

| 書き方 | 意味 |
|---|---|
| `@prev.masterIndices` / `@prev.objectIds` | 直前の項目の対象 |
| `@prev.<キー>` | 直前の項目の戻り値 |
| `@<項目 ID>.<キー>` | 名指しした項目の戻り値。間に何項目挟んでもよい |

どれも**同じ流れの中で実行した項目の結果**だけを指す。別の流れや前回の結果は混ざらない。

カンマを含む値は `setScenarioItemArg` で書く。`argValues` は配列なので割れる。

### 流す

途中の項目だけを選んで実行する口は無い。流し方は 2 通り。

| 口 | すること |
|---|---|
| `runScenario(name)` / パネルの「流す」 | 先頭の項目から流す |
| `continueScenario` / パネルの「続きを流す」 | 止まった所から続ける |

止まる所と、続けたときの動き。

| 止まる所 | 続けると |
|---|---|
| 指示（Instruction）の項目 | その項目を越えて進む。`continueScenario(argKeys, argValues)` で次に実行する項目の値を差し替えられる |
| 確認（Observe）の項目 | その項目を越えて進む |
| 失敗した項目 | 同じ項目をやり直す |

注意（Note）の項目は止まらずに読み飛ばす。別のシナリオを呼ぶ項目（ScenarioRef）では、
呼ばれたシナリオをその場で流し、終わったら元のシナリオへ戻る。
止める条件は `ObjectGroupStep.RequiresJudgment` の 1 か所だけにある。

いまの状態は `queryScenarioRun`、やめるのは `stopScenarioRun`。
流す前に `queryScenarioAudit` を 0 にしておくこと。索引の直書きは、
モデルに先に別のオブジェクトがあると別物を掴んで途中で失敗する。

---

## E. シリアライザを変えるとき

保存形式は 3 つある。**1 つに項目を足したら、3 つとも足す。**

| 形式 | 場所 | 使い道 |
|---|---|---|
| CSV | `CsvProjectSerializer` / `CsvModelSerializer.*` | 保存・断片のマージ |
| JSON（.mfproj） | `ProjectSerializer` | 保存 |
| バイナリ（.plrf） | `RemoteProgressiveSerializer` | リモート送信（断片・丸ごと） |

バイナリは版番号を上げ、旧版も読めるようにする（`RemoteBinaryProtocol.cs` の先頭に版の履歴がある）。
バイナリの置き場所：プロジェクト見出し＝`SerializeProjectHeader`、モデル単位＝`WriteModelExtras`、
メッシュ単位の付帯＝`WriteMeshExtras`（どちらも `RemoteProgressiveSerializer.Extras.cs`）、
頂点・面とメッシュ単位の索引付きデータ＝`RemoteBinarySerializer`（`MeshFieldFlags`）。
MeshContext の欄の多くは MeshObject に置かれている。ジオメトリで MeshObject を差し替えても消えないよう、
MeshObject に欄を足したら `CopySummaryAttributes` にも足す。

### 確認（ファイルで行う）

1. `saveProjectCsv` で A を保存
2. `saveProjectBinary` で .plrf を保存
3. `loadProjectBinary` で読み戻す
4. `saveProjectCsv` で B を保存
5. `queryCsvFolderDiff(folderA, folderB)` で比べる。「差なし」以外なら、その項目がバイナリに入っていない
   （大きさだけでは比べない。-1 と 0、True と False が打ち消し合って大きさが同じになることがある）

断片（`saveProjectBinary` の `scope=meshes`）とモデル単位（`scope=model`）も同じ手順で確かめる。
断片は同じモデルへ読み戻す（名前が同じものは置き換わる）と、A と比べて「差なし」になるはず。
