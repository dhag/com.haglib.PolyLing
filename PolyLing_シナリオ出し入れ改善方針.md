# PolyLing シナリオ出し入れ 改善方針

状態：**実装済み（2026-10-23）**。確認結果は 4 節、実装と検証は 5・6 節。

目的：利用者がシナリオをアップロード（取り込み）・ダウンロード（書き出し）して PolyLing 内で使えるようにする。
パネル（ホスト）・リモート接続・MCP（Editor / Player）のどの経路からでも同じ結果になることを目指す。

---

## 1. 着手前の状況（2026-10-23 調査）

| 経路 | 書き出し | 取り込み | 根拠 |
|---|---|---|---|
| パネル（ホスト） | 可（保存ダイアログ） | 可（読込ダイアログ）。ただし `folder` を渡さず常に直下 | PlayerScenarioSubPanel.cs（OnExport / OnImport） |
| リモート接続 | 不可 | 不可（一覧・中身・実行も含め、シナリオのコマンドはすべて拒否） | RemoteOwnership.cs（TryAuthorize の IsScenario） |
| MCP（Editor） | 2 段で可（`exportScenarios` → `read_file`） | 2 段で可（`write_file` → `importScenarios`） | PlayerCommandDispatcher.Scenario.cs |
| MCP（Player） | 実質不可（ファイルは書けるが読む道具が無い） | 不可（作業フォルダへ CSV を置く道具が無い） | PolyLingMcpServer の ServerTarget.cs（HiddenInPlayer） |

原因：
- `exportScenarios` / `importScenarios` は**ファイルパスしか受けなかった**。
- 中核の `ScenarioLibrary.TryExport` は CSV 文字列を返し、`TryImport` は行の列を受ける。中核はすでに中身ベースで、ファイル化していたのはディスパッチャの受け口だけ。

---

## 2. 方針

### 2-1. 既存の 2 コマンドを「中身でも受け渡しできる」形にする

コマンド数を増やさない（MCP コスト最適化の方針と合わせる）。

**exportScenarios**
- `filePath` を任意にした。
- 渡したら：今どおりファイルへ書く（`PLSandbox.TryResolveWrite` を通す）。
- 省いたら：ファイルを書かず、戻り値 `csv`（Text）に本文を入れて返す。
- 戻り値 `path` は、ファイルへ書いたときだけ返す（Optional）。

**importScenarios**
- `csvText` 引数を足し、`filePath` を任意にした。
- `filePath` と `csvText` はどちらか一方だけ。両方・どちらも無しは失敗。
- `csvText` は `File.ReadAllLines` と同じ規則で行に割り（`StringReader.ReadLine`、先頭の BOM は落とす）、ファイル読みと同じ `ScenarioLibrary.TryImport` に渡す。
- 検査（全部か無しか・参照・循環・名前の重なり・overwrite）は今の `TryImport` のまま変えていない。

これで MCP は Editor・Player とも `polyling_call` 1 回で上げ下げできる。Player 向けにファイル道具を開ける必要はない。

### 2-2. リモート接続：読むだけ許可し、取り込みはホストの許可制にする

| 区分 | コマンド | リモートから |
|---|---|---|
| 読む | `queryScenarios`, `describeScenario`, `exportScenarios`（`filePath` 省略時のみ） | 許可 |
| 取り込む | `importScenarios`（`csvText` のみ・`overwrite=false` のみ） | ホストが許可しているときだけ |
| それ以外 | 作成・編集・削除・フォルダ操作・実行・記録・点検・許可の切り替え・ファイルパス指定の出し入れ | 今どおり拒否 |

- 許可の正本は `RemoteOwnership.AllowRemoteScenarioImport`（既定 false、保存しない）。
- 切り替えはコマンド `setRemoteScenarioImport(enabled)`。シナリオパネルのトグル「リモート接続からの取り込みを許可する」もこのコマンドを撃つ。
- 拒否の文言は、何が足りないか（許可が切／overwrite 不可／filePath 不可／対象外の操作）を区別して返す。

### 2-3. パネルの取り込みで行き先フォルダを効かせる

- 「読み込む」で、フォルダ欄（`_folderField`）の値を `folder` に渡す。空なら直下。

---

## 3. 対象外

- リモートからのシナリオ実行（`runScenario` など）。実行はホストのモデルを書き換えるため、所有権・担当者ロックとの関係を別途決める必要がある。
- シナリオの形式変更（CSV のまま。`ObjectGroupCsv` が正典）。保存項目の追加ではないので、CSV / JSON / バイナリ 3 つを同時に直す規則（残件 R-1）には当たらない。
- MCP サーバ側の専用道具の追加（`polyling_call` で足りる）。
- リモートのクライアント側の画面（4 節の 3 を参照）。

---

## 4. 着手前の確認結果

1. **書き出しの長さの上限**
   - 転送路に上限は無い。TCP の枠は 32 bit の長さを持ち（net_duplexchannel の DuplexPacket.TryParseHeader）、MCP サーバは本文を切らない（UnityProjectTools.SendEditorRaw → McpContent.TextWithImages）。
   - 上限はクライアント側にある。道具の応答全体でおよそ 1MB（PolyLingMcpServer の McpContent.cs の注記）。MCP サーバは非 ASCII を `\uXXXX` で送るので、文字数の割に膨らむ。
   - 対応：戻り値に載せる本文に上限 `ExportScenariosCommand.InlineCsvMaxChars = 400000` 文字（MCP サーバの文字列出力の上限にそろえた）を置き、超えたら理由付きで失敗する（黙って切らない）。
2. **取り込みの長さの上限**
   - 転送路に上限は無い。文字列引数は `JsonParser.ParseFlat` が引用符の中身をそのまま戻し（値が `[` `{` で始まるものを落とすのは JSON の値の 1 文字目の話で、文字列の中身は関係しない）、`PanelCommandFactory` は string をそのまま渡す。
   - 上限は置かなかった。
3. **リモートのクライアント側の画面**
   - 無い。シナリオパネルは `RunCommand = cmd => DispatchHost(cmd)`（PolyLingPlayerViewerCore.Layout.TestPanels.cs）で、クライアント側でも手元の置き場を扱う。
   - 今回の範囲は「コマンドの受け口まで」。リモート接続のコマンドの応答は MCP の call と同じ `result` を返す（RemoteServerCore.Messages.cs の ProcessCommandViaPanelCommand）ので、プログラムからは使える。
4. **Player のパネルのダイアログ**
   - 動く実装がある。`PolyLingPlayerBridge` が Win32 のダイアログで `SaveFilePanel` / `OpenFilePanel` を持つ。
5. **許可の持ち方**
   - 既存の許可（`RemoteOwnership.AllowAnonymousEdit`、ライブ受信の受け入れ許可）はどちらも保存しないセッション限りの状態。新しい許可もそれにそろえた。

---

## 5. 変更したファイル

| ファイル | 変更 |
|---|---|
| Runtime/Poly_Ling_Main/Core/Data/PanelCommand.Scenario.cs | `ExportScenariosCommand`：`filePath` 任意化・戻り値 `csv`・`InlineCsvMaxChars`。`ImportScenariosCommand`：`csvText` 追加・`filePath` 任意化。`SetRemoteScenarioImportCommand` 新設 |
| Runtime/Poly_Ling_Player/View/Core/PlayerCommandDispatcher.Scenario.cs | 書き出し・取り込みの分岐（ファイル／本文）、`SplitLinesLikeReadAllLines`、`RunSetRemoteScenarioImport` |
| Runtime/Poly_Ling_Remote/RemoteOwnership.cs | `AllowRemoteScenarioImport`、`RemoteScenarioDenyReason`（一律拒否を 2-2 の区分に分けた）、`IsScenario` に新コマンド |
| Runtime/Poly_Ling_Player/View/SubPanels/Model/PlayerScenarioSubPanel.cs | 取り込みに `folder` を渡す。トグル `scenario.remoteImport`（`Refresh` で正本を写す） |

`ScenarioLibrary.cs` は変更していない。

---

## 6. 検証（2026-10-23、Editor の Play で MCP から実施）

- コンパイル：エラー 0。
- `queryCommandAudit`：今回の変更による指摘なし（「正典に無い 5」は既存の `scenario.log` などで、今回と無関係）。
- `queryUiAutomationAudit`：シナリオパネルの指摘なし（並ぶものは既存の左ペインなど）。
- 往復：`exportScenarios`（filePath 無し）で受けた本文を名前を変えて `importScenarios csvText`（folder=_tmp）で取り込み、書き出し直した本文が一致。フォルダは `_tmp` に入った。
- 本文の割り方：BOM 付き・LF 改行・末尾改行なしでも取り込めた。
- 異常系：filePath と csvText の両方／どちらも無し／同名で overwrite 無し（2 本目も登録されない＝全部か無し）／シナリオの無い本文、いずれも理由付きで失敗。
- ファイル経路（従来）：`filePath` での書き出し・取り込み（overwrite）とも従来どおり。
- リモートの判定（`queryOwnershipVerdict`、操作者＝Remote）：
  - 許可：queryScenarios、exportScenarios（filePath 無し）、許可入で importScenarios（csvText）。
  - 拒否：exportScenarios（filePath）、許可切で importScenarios、overwrite=true、filePath での取り込み、runScenario、setRemoteScenarioImport。
  - 操作者＝Mcp は今までどおり許可。
- パネル：MCP で入れた許可がトグルに写る。トグル（uiSetValue）で切ると判定が拒否に戻る。
- 未検証：上限 400000 文字を超える書き出し（手元の置き場では届かない）。パネルの「読み込む」のフォルダ指定（ダイアログが要るため。コマンド側の `folder` は検証済み）。
