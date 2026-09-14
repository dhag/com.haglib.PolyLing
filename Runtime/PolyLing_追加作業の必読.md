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

## D. 手本（シナリオ）に残すとき

手順が固まったら `scenarios.csv` に残す。生成系はパネルを 1 回流してから
`saveScenarioFromGroup` で起こすのが早い。計算済みの値がそのまま焼ける。

段の間で値を渡すときは

| 書き方 | 意味 |
|---|---|
| `@prev.masterIndices` / `@prev.objectIds` | 直前の段の対象 |
| `@prev.<キー>` | 直前の段の戻り値 |
| `@<段の名前>.<キー>` | 名指しした段の戻り値。間に何段挟んでもよい |

カンマを含む値は `setScenarioStepArg` で書く。`argValues` は配列なので割れる。
