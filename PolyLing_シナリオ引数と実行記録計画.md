# PolyLing シナリオ引数と実行記録計画

シナリオに引数を宣言して値を変えて流せるようにし、将来の学習用に実行記録を棚と別に残す。正本はこの計画書。1 段＝1 会話で進める。

## 0. 前提と方針

- 今の運用（検索・describe・runScenario）の負荷を増やさない。トークンが増えるのは引数の名前・既定値・説明の分だけ。
- 棚（`ScenarioLibrary`、流す手本、検索の対象）と実行記録（学習用、検索の対象外）は分ける。値だけ違う派生は棚に置かず、引数で流す。
- 引数の置き換えは `@prev` / `@<項目ID>` と同じ関数（`PlayerCommandDispatcher.Scenario.cs` の `TryExpandPrev` の周り）で行う。
  記号は `${名前}`（`@名前` は項目 ID と区別できないため）。値の丸ごとだけを置き換え、値の一部（`1,${n},1`）は扱わない。
- シリアライザを直すときは CSV（`ObjectGroupCsv`、正典）・JSON（`ObjectGroupDTO`）・バイナリ（`RemoteProgressiveSerializer`）を同時に直す。
- この計画に入れないもの：焼き付いた索引の参照化（学習に着手するとき）、埋め込み検索（`--metrics-search` の当たり率を見て判断）。

## 1. 段 1：引数をデータとして持つ（済 2026-10-21）

結果：`ScenarioParam`（ObjectGroup.cs）と `ObjectGroup.Params`／`FindParam`、Clone で複写。
CSV は `ga,name,default,description` 行（版 1.6、TryValidate の上限 4 列）、JSON は `scenarioParams`、
バイナリはモデルメタ版 11（由来の直後に ParamCount と 3 文字列×件数）。
監査に badParamName / duplicateParam / unknownParam / partialParamRef / unusedParam を追加（引数そのものの指摘は項目 ID 空・keys に名前）。
literalMeshIndex は `${名前}` の値を見ない。describe（outline でも）に paramNames / paramDefaults / paramDescriptions。
確認：置き場へ置いた引数付き CSV が describe・監査で期待どおり。exportScenarios で ga 行が往復。
モデルの objectgroups.csv に ga 行を置き、CSV→JSON（.mfproj）→CSV と CSV→バイナリ（.plrf）→CSV を queryCsvFolderDiff で比べて差なし。
メモ：メッシュが 0 個のモデルが CSV 読み込みで落ちていた（`CsvModelSerializer.LoadModel` が entry 0 件で null を返していた）。
model.csv が無いときだけ null にするよう直した（2026-10-21）。空のモデルとそのオブジェクトグループが CSV・JSON・バイナリの往復で残ることを確認。

- `ObjectGroup.Params`（`List<ScenarioParam>`）。1 件＝名前・既定値・説明。名前は英数字と `_`、シナリオ内で重複不可。
- 3 つのシリアライザに足す。引数の欄が無い古いファイル・データは引数なしとして読む。
- 複製（Clone / Fork）で写す。
- `queryScenarioAudit`：宣言していない `${名前}` の使用、宣言して使っていない引数を指摘する。
- `describeScenario` に `paramNames` / `paramDefaults` / `paramDescriptions` を載せる（outline でも載せる）。

## 2. 段 2：流すときに使う（済 2026-10-21。MCP サーバの再ビルド待ち）

結果：共通関数は `ScenarioParam.TryBind` / `TrySubstitute` / `TryBindRef` / `SubstituteStep`（ObjectGroup.cs）。
流し：`Frame.Params` に段ごとの値。`runScenario` の `paramKeys` / `paramValues`（宣言に無い名前は流し始める前に失敗）。
参照項目の Args を子に結ぶ（値の親 ${…} は親の値で置き換え。キーが子に無ければその項目で失敗）。
`TryExecuteRunStep` は差し替えの後・@prev の前に ${…} を置き換える。流れの記録に結んだ値を出す。
平らにする（`TryFlatten`）は起点を結ばず、子は結んだ値で置き換え。登録・取り込みの循環検査は結ばない（`bindParams=false`）。
`expandScenarioRef` は結んだ値を写した項目で置き換える。`setScenarioParam` / `removeScenarioParam` を追加。
`setScenarioItemArg` は参照項目にも書ける。監査に unknownChildParam。検索の照合本文に引数の名前と説明。
確認：親（seg, div）→ 子（seg）の 2 段で、paramValues `3,"2,3,4"` を渡して流し、生成物の頂点数が同じ値の直接実行と一致（238 / 104。既定の 1 分割なら 24）。
既定値で流すと記録に seg=2, div=1,1,1。綴り違いの引数名は流し始める前に失敗。子に無いキーの結び付けは監査で指摘・流すとその項目で失敗。
resolve は子の ${seg} を親の ${seg} に置き換えて返す。展開も同じ。検索「角丸の分割数」で引数の説明から子が当たる。

- `runScenario` に `paramKeys` / `paramValues`。渡さない引数は既定値。宣言に無い名前はエラー。
- 引数は呼び出しの段（Frame）ごとに持つ。参照項目の `Args` を子シナリオの引数に結ぶ（今は流すときに読んでいない欄）。値に親の `${名前}` も書ける。
- 項目の引数の値が `${名前}` 丸ごとなら、その段の値へ置き換える。
- 平らにする操作（describe の resolve、`expandScenarioRef`）は、結ばれた値を子の項目へ写してから平らにする。
- 編集コマンド `setScenarioParam` / `removeScenarioParam`。
- 検索の照合本文（`ScenarioLibrary.SearchText`）に引数の名前と説明を加える。
- MCP サーバの説明文（`polyling_describe` / ServerProfile）を直す。再ビルドはこの 1 回。

## 3. 段 3：パネル（済 2026-10-21）

結果：`PlayerScenarioSubPanel` の説明の下に引数の入力欄（TextField。宣言の順、既定値を初期値、説明を下に小さく）。
欄は `UiDynamicControls _uiParams` に `param.<名前>` で載せる（MCP の `scenario.param.<名前>`）。
選んだシナリオか宣言（名前・既定値・説明）が変わったときだけ作り直す（流している間の UpdateView で値を消さない）。
「流す」は欄の値を全部渡す（`StartRun(name, keys, values)` → `PlayerScenarioGuideBar.Start` → `runScenario` prepareOnly）。
確認：親子 2 段のシナリオを選び、uiSetValue で seg=3・div=2,3,4、uiClick「流す」→ 流れの記録に結んだ値、頂点数 238 / 104（段 2 と一致）。
`queryUiAutomationAudit` にシナリオのパネルの指摘なし。

- シナリオのパネル（`PlayerScenarioSubPanel`）で、選んだシナリオの引数を入力欄で出す（初期値は既定値）。流すときに `runScenario` へ渡す。

## 4. 段 4：実行記録（済 2026-10-21。既定は入に決定）

既定：入（2026-10-21 決定）。入切は PlayerPrefs `PolyLing.ExecutionLog.Enabled` に覚え、起動時に
`PlayerCommandDispatcher.ApplyExecutionLogPreference`（ビューアがディスパッチャを作った直後）で適用する。
確認：設定の無い状態で起動して入、setExecutionLog false → Play を入り直して切のまま、true に戻して保存。

結果：`ExecutionLog`（Core/Data/ExecutionLog.cs）。コマンド `setExecutionLog` / `beginTask` / `endTask` / `queryExecutionLog`（カテゴリ scenario.log）。
控える場所は `DispatchAuthorized` の一番外側（ScenarioRecorder の隣）。切なら `ExecutionLog.Enabled` の判定 1 回だけ。
1 件：t・kind（taskBegin / command / taskEnd）・task・seq・actor・action・args・writes・ok・reason（失敗時）・ms・runLog（runScenario / continueScenario のとき、その呼び出しで流れた行）。
計画から変えた点：引数の文字列化（ToArgs）はメインスレッドで行う。パネルは諸元のオブジェクトをコマンドへ渡したあとも書き換えるので、
別スレッドで後から読むと実行時と違う値になるため。別スレッドはファイルへの書き込みだけ（同じファイルの行をまとめて追記）。
確認：切のとき beginTask は断る。タスク 1 本（runScenario＋直接の createCube 2 回）で taskBegin〜taskEnd の 5 行が書け、runScenario には流れた 6 行、引数の値（div=3,3,3）が残る。
負荷：同じシナリオ（コマンド 4 本）を 5 回流して、メインスレッドで記録に使った時間は 1 件あたり 0.21 ms（流し 1 回は 265〜617 ms）。
起動直後の 1 件目は 1 ms 弱（初回の型の読み込み）。長い座標列を持つ引数（setVertexPositions など）での時間は測っていない。
確認に使った記録ファイルは `_McpTrash` へ退避した。

- 控える場所：Dispatch の一番外側、`ScenarioRecorder.RecordCommand` の隣（`PlayerCommandDispatcher.cs` の `topLevel` の後）。入れ子は控えない。
  `runScenario` は名前と引数の 1 件。各項目の成否は流れの記録から付ける。
- 1 件：時刻・タスク ID・コマンド名・引数・書き込み範囲・成否・理由・所要時間。戻り値の本文は控えない。
- メインスレッドでは列に積むだけ。別スレッドで `runlogs/<日付>/<タスクID>.jsonl` に書く。棚・検索・describe からは読まない。
- タスクの区切り：`beginTask`（目標・条件の文）、`endTask`（判定：成功・修正・失敗、一言）。
- 記録あり・なしで同じシナリオを流して時間を比べ、既定を入にするか決める。

## 4b. 出力（戻り値）と Python アドオンの項目（済 2026-10-23）

- シナリオに出力を宣言できる（`ScenarioOutput`。`setScenarioOutput` / `removeScenarioOutput`、CSV の `gr` 行・DTO の `scenarioOutputs`）。
  流し終えたら `outputs` で返し、参照項目で呼ばれたときは親の参照項目の戻り値になる。
- `invokePyAddon` の項目は `stop=waiting` で返事を待ち、返事を控えて先へ進む。案内バーは自動で続ける。
- 文字列の配列の引数は要素ごとに `${名前}` / `@…` を置き換える。`@…` で引いた文字列はエスケープを戻して渡す。
- 確かめたこと：子の出力を親で `@p1.folder` / `@p1.tag` で引き、親の出力に返る。宣言の無い名前は監査（unknownChildOutput）と流したとき（失敗）の両方で出る。
  MediaPipe アドオンを describe → run（argValues に `${img}`）→ getRawData（`@a2.masterIndex`）と流し、MCP（continueScenario）と案内バーの両方で最後まで流れた。
- 詳しい規則は `Runtime/PolyLing_追加作業の必読.md` の D 節。

## 5. 各段の終わり

- 3 つの csproj をコンパイル、`unity_validate_changes`、Play で確かめる、キャプチャ。
- 進んだら、この計画書の該当段に「済」と結果を書く。
