# PolyLing クライアント種別とビルド構成計画

1 段＝1 会話。各段の終わりにこの計画書の「状態」を更新する。

## 目的

1. リモートクライアントの種別をコード上で明示し、LLM が実装を取り違えないようにする。
2. 用途ごとに「Unity のシーン一覧・exe 名・役割・受付ポート」の組（ビルド構成）を定義し、個別またはまとめてビルドできるようにする。
3. 起動した複数のビルド版と Editor に、MCP から構成名で個別にアクセスできるようにする。

## 着手前の事実（2026-11-01 確認）

- 種別の項目は無く、サーバはクライアントのユーザー名がホストと同じかで扱いを分けていた
  （`RemoteServerCore.Messages.cs` の selectMesh 横取りと DispatchWithSelectionOf、`RemoteServerCore.Push.cs` の CheckSelectionChanged）。
- `clientType` は配信先の振り分けにだけ使う自由な文字列。
- register を送るもの：ListClientBase（端末名）、ProbeClient、PolyLingPlayerViewer のクライアント起動（"playerViewer"、名前は空）、
  HierarchyRemoteExportWindow（端末名）、Python の PolyLingClient（名前を省くとホスト名）。
- ホストと同名の窓が切断すると、その名前（＝ホスト）の担当ロックが外れていた（`RemoteServerCore.cs` の OnClientDisconnected）。
- `build_player` は Build Settings の有効なシーンから出力 1 つ（既定 `ビルド用/PolyLing.exe`）。
- MCP の接続先は起動時の `--target` で固定。ビルド版の受付ポートは 8772 固定。

## 決定

### クライアント種別（register の `role`、必須）

| role | 呼び名 | 作業の場所 | 選択 | 操作者（担当ロック） |
|---|---|---|---|---|
| `host` | 本人連動 | サーバ | ホストの選択そのもの（選ぶと本体が動き、本体の選択が届く） | ホスト名 |
| `collaborator` | 別作業者連動 | サーバ | サーバ上の本人用の枠（本体も他人も動かない） | 登録したユーザー名（必須・ホスト名と別） |
| `separate` | 個別作業 | 手元のコピー | 手元だけ（サーバへ選択を送ると拒否） | 登録したユーザー名 |

- 振り分けはユーザー名の一致ではなく role だけで決める。ユーザー名は表示と担当ロックに使う。
- role の無い register、未知の role、collaborator の名前が空またはホスト名と同じ場合は登録を拒否し、理由を返す。
- C# の `PolyLingPlayerClient.RegisterClientType` は role を必須の引数にする（既定値を持たせない）。Python の `PolyLingClient` も role を必須にする。
- 本体ビューアのクライアント起動は `separate`。頂点移動を確定するたびに位置を自動で送る動き（`SyncClientToServer`）は当面維持する。
  理由：1 つのウインドウとして使う運用も想定しており、こちらは重いため、使われずに壊れていても気づかない状態を避ける。
- 各クライアントの role：
  - ListClientBase（モデル・オブジェクト・材質リスト）… インスペクタで選ぶ。既定 `host`。
  - ProbeClient、HierarchyRemoteExportWindow … `separate`。
  - Python：Blender ブリッジ・アドオンの Session … `host`。examples/send_mesh.py・ComfyUI ノード … `separate`。
- 本人連動の窓が切断しても、ホストの担当ロックは外さない。

### ビルド構成

- 既定の構成はパッケージ側 `Packages/com.haglib.polyling/Editor/Build/既定ビルド構成.json` に置く。中身は今の動きと同じ
  （Editor 8770、`ビルド用/PolyLing.exe` 8772・Build Settings の有効なシーン）。
- `Assets/PolyLingBuild/ビルド構成.json` があればそちらを使う。無ければ既定を使う（削除されても今の動きに戻る）。
- 構成 1 つ：名前・シーン一覧（空なら Build Settings の有効なシーン）・exe（プロジェクト直下からの相対）・リモート役割（server / host / collaborator / separate / none）・受付ポート・ユーザー名（`user`、省略可。段 3 で追加）。
- 役割の使える場所（段 3 で決定）：本体ビューアのシーンは server / separate / none。host / collaborator はリストクライアント（ListClientBase）のシーン。
  MCP の受付を立てるのは本体ビューアだけなので、リストクライアントだけの構成の受付ポートには MCP から届かない。
- MCP サーバも同じ順（Assets → パッケージ既定）で構成を引く。

## 段

1. **クライアント種別を明示する** — 上の表のとおり。サーバの振り分け、各クライアントの登録、Python クライアント、`PolyLing_外部連携の契約.md`・ListClient の README を更新。
2. **ビルド構成を定義する** — 既定ファイル・読み込み・`build_player` に構成名、全構成を順にビルドする `build_all`。MCP の `unity_build_player` に構成名と「全部」の指定。
3. **受付ポートを構成ごとに分ける** — 起動引数 `--polyling-control-port=N`（無ければ 8772）と `--polyling-remote=` に role を渡す。
4. **MCP から構成名で個別にアクセスする** — `polyling_call`・`unity_capture`・`unity_focus`・`player_launch`・`player_stop` などに `instance`（構成名。省略時は `--target`）。
   構成名からポートと exe を構成ファイルで引く（MCP サーバは呼び出しごとに別プロセスになることがあるため、起動したものをメモリに控えない）。
   この段の後、MCP サーバの再ビルドと再起動を操作者に頼む。
5. **確認** — サーバ・本人連動・個別作業のクライアントを同時に起動し、MCP から個別に操作して振り分けを確かめる。

## 残す点（調べた事実）

- 個別作業クライアントがコマンドを送った場合、「今の選択」を見るコマンドはホストの選択で動く（本人用の枠を持たないため）。
  個別作業のアップロードは対象を引数で明示するコマンド（putModel など）で行う規約とし、契約に書く。
- ブラウザ用の RemoteHtmlClient は register を送らない（選択は「ユーザー名が未登録」で拒否される。従来どおり）。

## 状態

- 段 1：完了（2026-11-01）。Unity のコンパイル 0 件。実際に複数クライアントをつないだ確認は段 5 で行う。
  - 追加：`Runtime/Poly_Ling_Remote/RemoteClientRole.cs`
  - サーバ：`RemoteServerCore.cs`（登録の Role、本人連動の窓の切断でホストのロックを外さない）、
    `RemoteServerCore.Push.cs`（register の role 必須・拒否・選択の初期送り）、
    `RemoteServerCore.Messages.cs`（操作者名を role で決める ActorNameOf、selectMesh の振り分け、selectionChanged の送り先）、
    `RemoteServerCore.ClientRequest.cs`（一覧に role・actorName）
  - クライアント：`PolyLingPlayerClient.RegisterClientType`（role 必須）、`ListClientBase`（Role をインスペクタで選ぶ・actorName を控える）、
    `MeshListClient`（担当者名を actorName に）、`ProbeClient`・`HierarchyRemoteExportWindow`・本体ビューア（separate）
  - Python：`polyling_io/net/client.py`（role 必須・ROLE_* 定数・actor_name）、Blender ブリッジ・Session（host）、send_mesh.py・ComfyUI ノード（separate）
  - 文書：`PolyLing_外部連携の契約.md` §3.4、`Poly_Ling_ListClient/README.md`
- 段 2：完了（2026-11-01）。Unity のコンパイル 0 件。Assets 側の構成ファイルが無い状態で、既定構成のビルドが成功（config=player、ビルド用/PolyLing.exe、サンプルシーンA）。
  - 構成ファイルの形（Unity は JsonUtility、MCP サーバは段 4 で同じファイルを読む）：
    `{"version":1, "editor":{"name":"editor","port":8770}, "builds":[{"name","scenes","exe","role","port"}]}`
    - 名前は editor と builds の間でも重複不可。名前 `editor` / `player` は `--target editor|player` に合わせた（段 4 の instance 省略時の対応に使える）。
    - port・exe も重複不可。exe はプロジェクト直下からの相対。scenes は Assets/... .unity（空なら Build Settings の有効なシーン）。
    - 既定の role は `server`（Build Settings の唯一のシーン サンプルシーンA で有効な PolyLingPlayerViewer の _remoteMode が Server）。
      role と port は保持するだけで、exe へ渡すのは段 3。
  - 追加：`Editor/Build/既定ビルド構成.json`、`Editor/Build/PolyLingBuildConfig.cs`（読み込み・検査。Assets → パッケージ既定の順）
  - `PolyLingEditorControlServer.cs`：`build_player` に `config`（省略時は builds の先頭。`outputPath` はこの回だけの差し替え）、
    `build_all`（並びの順に全構成。失敗で止め、未実行の構成名を返す。`config`・`outputPath` は拒否）。応答の先頭に `configFile=`。
  - MCP：`UnityPlayerBuildTools.cs` の `unity_build_player` に `config`・`all`。コンパイルは通過（exe のコピーだけ失敗）。
    MCP サーバ再起動後に確認済み：`config=player` 成功、`all=true` 成功（builds=1）、
    `config=nosuch` は構成名の一覧付きで拒否、`all=true` と `config` の同時指定は MCP 側で拒否。
  - Assets 側の構成（player と player2：シーン指定・`ビルド用2/PolyLing2.exe`・separate・8773）を置いて `all=true` → `configFile=Assets/PolyLingBuild/ビルド構成.json`、2 つとも成功。
    構成ファイルを除いた後の `all=true` はパッケージ既定に戻ることを確認。試験の出力 `ビルド用2/` は操作者が削除済み。
- 段 3：完了（2026-11-01）。Unity のコンパイル 0 件。MCP サーバはコンパイル通過（exe のコピーだけ失敗）、再ビルド・再起動は未実施。
  - 追加：`Runtime/Poly_Ling_Remote/PolyLingLaunchArgs.cs`（`--polyling-remote=` 役割 5 値、`--polyling-user=`、`--polyling-control-port=`）
  - `PolyLingPlayerViewer.ResolveRemoteMode`：役割を受ける。server / separate（クライアント）/ none。host / collaborator と不明な値はエラーでリモートなし。`client` は廃止。
  - `ListClientBase`：起動引数の種別（host / collaborator / separate）とユーザー名をインスペクタより優先。server / none・不明な値はエラーを出して接続しない（画面に理由）。
  - `PolyLingPlayerControlServer`：`DefaultPort`=8772、`Port` は実際に待ち受けたポート。不正な値なら受付を立てない。`state` に `port=`。
  - ビルド構成に `user`（省略可）。既定ファイルにも `"user": ""`。
  - MCP：`player_launch` の `remote_mode` を 5 値に（`client` は拒否）。
  - 文書：`PolyLing_外部連携の契約.md` §2（ポート表・起動引数の節）。
  - ビルド版で確認（Player.log）：
    - `--polyling-control-port=8773 --polyling-remote=none` → 受付 8773、リモートなし。
    - 引数なし → 受付 8772、シーンの設定（サーバ）のまま。
    - `--polyling-remote=host --polyling-control-port=abc` → 本体ビューアはエラーでリモートなし、受付は立たない。
    - サンプルシーンB（リストクライアント）のビルドに `--polyling-remote=collaborator --polyling-user=bob` → 種別 collaborator・ユーザー名 bob。受付（PolyLingPlayerControl）は立たない。
  - 未確認：collaborator がサーバへ実際に登録される動き（段 5 で確認）。
  - 試験出力 `SandBox/ビルド試験/` は delete_path が「Access denied」で移せず残っている。
- 段 4：完了（2026-11-01）。MCP サーバはコンパイル通過、操作者が再ビルド・再起動済み。
  - 追加：`データとツール/PolyLingMcpServer/PolyLingMcpServer/BuildInstances.cs`
    - `BuildConfigLoader`：Editor 側 `PolyLingBuildConfig.cs` と同じ順（Assets → パッケージ既定）・同じ形・同じ検査（System.Text.Json、キー名を固定、初期値を JsonUtility 側に合わせた）。
    - `InstanceResolver`：`instance` を接続先にする。省略時は `--target` のまま（構成ファイルを読まない）。editor の name は Editor（editor.port、プロセス Unity）、builds の name はその port と exe。呼び出しごとにファイルを読む。
    - MCP の受付の有無：構成のシーン（空なら EditorBuildSettings.asset の有効なシーンを guid で引く）と、そこから m_SourcePrefab で入れ子に参照されるプレファブに、`PolyLingPlayerViewer.cs.meta` の guid が無ければ、送る前に理由付きのエラーにする。調べられないときは送る。
  - `instance` を足した道具：`polyling_call`・`polyling_tools`・`polyling_search`・`polyling_describe`・`polyling_scenes`・`unity_capture`・`unity_focus`・`unity_ping`・`unity_state`・`player_launch`・`player_stop`。
    - `player_launch(instance)`：`--polyling-remote=<role> --polyling-control-port=<port>`、user が空でなければ `--polyling-user=<user>`（空白などを含めば引用符で囲む）。exe_path・remote_mode との同時指定は拒否。instance=editor は拒否。
    - `player_stop(instance)`：構成の exe のプロセスだけ。`unity_focus(instance)`：構成の exe と実行ファイルのパスが一致するプロセスだけ。
  - `EditorControlClient.SendToAsync` を公開、例外に送った先のポート（接続失敗の文言に実際のポートが出る）。`ServerState.HasProject`（--project が無ければ instance は使えない）。
  - 確認（Editor を Play、ビルド版を起動して）：
    - 既定の構成で `instance=editor` と `instance=player` の両方に届く。player だけに createCube → queryRevisions は player が models=1・editor が models=0。unity_capture もそれぞれの画面（player に立方体、editor は空）。
    - `player_launch(instance=player)`（既定構成）→ 引数 `--polyling-remote=server --polyling-control-port=8772`、Player.log「起動引数でサーバ」「起動: port=8772」。
    - Assets 側の試験構成（player：role none・port 8774）→ 引数 `--polyling-remote=none --polyling-control-port=8774`、Player.log「起動引数でリモートなし」「起動: port=8774」、`unity_state(instance=player)` が port=8774、`unity_focus(instance=player)` がそのプロセスを前面へ。
    - 同じ試験構成の listB（サンプルシーンB のみ・collaborator）へ polyling_call・unity_capture → 送る前に「PolyLingPlayerViewer が無いので MCP の受付が無い」のエラー。未知の名前は構成名の一覧付きで拒否。player_launch(instance=editor) は拒否。
    - 段 3 の残り：`player_launch` の `remote_mode` 5 値。Player.log が server「起動引数でサーバ」、separate「起動引数でクライアント（個別作業）」、none「起動引数でリモートなし」、host / collaborator「本体ビューアでは扱えません…リモートなしで起動します」。`client` は拒否。
    - 試験構成を除いた後、`instance=player` はパッケージ既定（8772）に戻る。
  - 未確認：`user` を渡す起動の実機確認（試験構成の listB は exe をビルドしていない）。段 5 でリストクライアントを起動するときに確かめる。
  - 注意：Editor の受付ポートは `PolyLingEditorControlServer.Port`（8770）固定で、構成の editor.port は読まない。instance=editor は構成の editor.port へ送るので、editor.port を 8770 以外にすると届かない。
  - 試験構成 `Assets/PolyLingBuild/` は delete_path で `_McpTrash/20261101_105156/` へ移した。キャプチャは `SandBox/Captures/段4確認/`。
- 段 5：完了（2026-11-01）。ソースは変更なし。
  - 試験構成（Assets/PolyLingBuild/ビルド構成.json）：server（A・server・8772）、sep（A・separate・8773）、hostB（B・host・8775）、collabB（B・collaborator・8776・user "bob smith"）。exe は `SandBox/段5ビルド/<名前>/PolyLing.exe`。`unity_build_player all=true` で 4 つとも成功。
  - 4 つを `player_launch instance=…` で同時に起動（各自 `-logFile SandBox/段5ビルド/<名前>.log`）。
    - 引数どおり：collabB は `"--polyling-user=bob smith"`（空白入りを引用）で起動し、ログに「起動引数でユーザー名: bob smith」「種別: collaborator」、register に userName "bob smith"・role collaborator（段 4 の未確認を解消）。
  - サーバの `queryRemoteClients`：playerViewer（role separate・actorName 空）、meshList HAGLABNOTE10（host・actorName HAGLABNOTE10）、meshList bob smith（collaborator・actorName bob smith）。段 3 の未確認（collaborator の登録）を解消。
  - 振り分け（MCP の querySelectionState と画面で確認）：
    - 個別作業（sep、プロジェクト取得で手元へ写した後）：sep で selectMesh Drawable=1 → sep は 1、サーバは 0 のまま。サーバで 0,1 → sep は 1 のまま。
    - 本人連動（hostB）：hostB の一覧で RoundedCube をクリック → サーバの選択が 0:RoundedCube に。サーバで selectMesh Drawable=1 → hostB の一覧が Sphere に追従。
    - 別作業者連動（collabB）：起動直後は何も選んでいない（サーバ・hostB は RoundedCube）。collabB で Sphere をクリック → サーバは 0 のまま、hostB も RoundedCube のまま。その後サーバで 0 を選び直しても collabB は Sphere のまま。
    - push の受け取りもログで一致：hostB は選択のたびに selectionChanged を受け、collabB は登録直後の分だけ。
  - 見つかった不具合（2026-11-01 修正済み。下の「修正」）：リストクライアント（hostB・collabB）を、サーバのモデルに描画オブジェクトが無いうちに接続すると、起動時の project_header 取得が「project_header 失敗」になり、その後サーバで createCube・createSphere をしても meshListChanged の push が届かず（両ログに selectionChanged のみ）、一覧が空のまま。クライアントを起動し直すと一覧が出た（確認はその状態で行った）。
    原因（コードとログで確認）：
    1. 一覧変更の push は `RemoteServerCore.SubscribeModel` が購読した `Context.Model.OnListChanged` から出るが、購読し直すのはサーバ起動時と `NotifyProjectReplaced`（`ChangeKind.ModelSwitch`）だけ。
       サーバ起動時にプロジェクトが無いと何も購読せず（リストクライアントの project_header には "No project"）、createCube が `PlayerLocalLoader.EnsureProject` で黙ってプロジェクトとモデルを作っても付け替わらない。`ExecuteCreateEmptyModel`（ListStructure で通知）も同じ。
    2. `OnListChanged` を呼ぶのは Undo/Redo の記録と一部のコマンドだけ。図形生成（`PrimitiveMeshCreateNewObject` → `PrimitiveMeshFinalize`）は呼ばず、`NotifyPanels(ChangeKind.ListStructure)` だけで知らせる。購読できていても meshListChanged が出ない。
    修正：
    - `RemoteServerCore.Push.cs`：`FollowCurrentModel`（監視先が今の現在モデルと違えば付け替え、refreshRequired を送る）を `NotifySelectionChanged` の頭で呼ぶ（NotifyPanels から毎回通る）。
      `NotifyListStructureChanged` を追加。一覧変更の送出は印を立てて `RunOnMainThread` で 1 回にまとめる（OnListChanged と NotifyPanels の両方が出ても 1 回。refreshRequired を送ったときは印を下ろす）。
    - `PolyLingPlayerServer.cs`：`NotifyListStructureChanged` を中継。
    - `PolyLingPlayerViewerCore.ModelSync.cs`：`NotifyPanels` の ListStructure で `_playerServer.NotifyListStructureChanged()`。
    確認（Unity のコンパイル 0 件。server・hostB をビルドし直し、空の server に hostB をつないでから）：
    - createCube → hostB に refreshRequired と meshListChanged、一覧 1 行（RoundedCube）。
    - createSphere → meshListChanged 1 回、一覧 2 行。
    - undo → meshListChanged 1 回、一覧 1 行（画面でも 1 行）。
    - ログは `SandBox/段5ビルド/fix2_hostB.log`・`fix2_server.log`。試験構成は delete_path で `_McpTrash` へ。
  - 片付け：4 プロセスを player_stop。試験構成は delete_path で `_McpTrash/20261101_110702/` へ。ビルド出力とログは `SandBox/段5ビルド/` に残す。
- 段 6（残りの片付け）：完了（2026-11-01）。Unity のコンパイル 0 件。MCP サーバはコンパイル通過（exe のコピーだけ失敗）、操作者が再ビルド・再起動済み。
  - Editor の受付ポート：Editor 側の `PolyLingEditorControlServer.Port`（8770）固定に寄せ、構成の `editor.port` を無くした。
    理由（コードで確認）：`unity_play`・`unity_stop`・`unity_refresh`・`unity_recompile`・`unity_validate_changes` は `--target` の接続先（既定 8770）へ、
    `unity_prefab_*`・`unity_build_player` は `SendToEditorAsync` で 8770 へ、どれも構成を読まずに送る。Editor の受付は `[InitializeOnLoad]` で立つ。
    ポートを構成で決めると、これら全部が構成に依存し、構成が検査に落ちている間は Editor へ届く道（refresh・play を含む）も受付も止まる。
    - `Editor/Build/PolyLingBuildConfig.cs`：`PolyLingEditorInstance.port` の初期値を -1（書かれていない印 `NotWritten`）にし、-1 以外なら拒否。builds の port の重複検査に 8770 を入れる。
    - `Editor/Build/既定ビルド構成.json`：editor から port を除いた。
    - MCP `BuildInstances.cs`：`EditorInstanceConfig.Port` を `int?`（null＝書かれていない）にし、値があれば拒否。重複検査に `EditorControlClient.Port`。instance=editor の接続先は `EditorControlClient.Port`。`EditorControlClient.cs` は注記だけ。
  - 文書：`MCP操作手順書.md` に「1-6. ビルド版と複数の構成（instance）」（構成ファイル・ビルド・instance を持つ 11 の道具・instance を持たない道具の送り先・リストクライアントだけの構成）。
    `McpGuide/primer.md` に「複数の構成を扱うとき」（3 行）。
  - 確認（MCP サーバ再起動後）：
    - 既定の構成で `unity_state instance=editor` → host=editor、`player_launch instance=player`（引数 `--polyling-remote=server --polyling-control-port=8772`）→ `unity_state instance=player` → host=player・port=8772。両方へ届く。
    - 試験構成 `Assets/PolyLingBuild/ビルド構成.json` に editor.port=8779 → `unity_state instance=editor`（MCP 側）と `unity_build_player config=player`（Unity 側）の両方が「editor.port は書けません（…8770 固定）」で拒否。
    - 同じく editor の port を除き builds の port を 8770 → 両方が「port が editor か他の構成と重複」で拒否。
    - 試験構成は delete_path で `_McpTrash/20261101_125040/` へ。除いた後 `unity_state instance=editor` は既定で通る。
  - 残り：`player_launch` の `extra_args` に相対の `-logFile SandBox/段6確認/player.log` を渡したところ、exe のフォルダが作業フォルダのため `ビルド用/SandBox/段6確認/player.log` ができた。
    `ビルド用/` は MCP の書き込み範囲の外で delete_path できない（操作者が削除）。手順書 1-6 に「-logFile は絶対パスで」を書いた。
