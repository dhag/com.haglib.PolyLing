# PolyLing 協働編集（オブジェクト編集者）実装メモ

サーバ・クライアントでのグループワークにおいて、オブジェクト（メッシュ／ボーン／モーフ等、
すなわち `MeshContext`）ごとに「現在の編集者」を設定し、他人の担当分を書き換えられないようにする。

採用方針:

| 論点 | 決定 |
|---|---|
| 所有権の識別子 | `MeshContext.ObjectId`（ulong、位置非依存の安定ID）を新設 |
| 編集者の取得方式 | 明示ボタンによる手動 claim のみ（自動取得なし） |
| 永続化 | プロジェクト保存（CSV）に含める |

---

## 1. 設計の要点

### 1-1. 真値は `MeshContext.EditorName` 一箇所

サーバ側に所有権レジストリを**置いていない**。

永続化する（＝保存/読込を跨いで生き残る）情報を、接続と寿命の違うレジストリで
二重管理すると、保存・読込・Undo とレジストリが必ずズレる。したがって

- **担当の真値** = `MeshContext.EditorName`（空文字＝担当者なし）
- **サーバの揮発情報** = 「どのチャネルがどの名前で register したか」だけ
  （既存の `RemoteServerCore._clientRegistry` がそのまま使える）

判定規則はこれだけ:

```
書き込み可 ⟺ EditorName が空 または EditorName == 要求者名
```

この帰結として、**切断しても担当は解放されない**。手動 claim 運用と整合しており、
放置された担当はホストの強制解放（`MeshListOps.ReleaseAllByEditor`）か本人の release で外す。

### 1-2. 認可ゲートは1箇所だけ

クライアントからの書き込みは
`PanelCommandRouter.Send` → `command` → `BuildPanelCommand` → `DispatchCommand`
の一本道なので、`RemoteServerCore.ProcessCommandViaPanelCommand` の
`DispatchCommand(cmd)` 直前に `RemoteOwnership.TryAuthorize` を挟むだけで全コマンドを止められる。

### 1-3. リスト構造のズレ検出

`MasterIndex` は位置ベースなので、他人が削除・並べ替えをした直後に届いた
古いビュー由来のインデックスは**別オブジェクトを指す**。

対策として、クライアントは `masterIndices` と同じ並びの `objectIds`（安定ID）を添えて送る。
サーバは「その位置に本当にそのIDのオブジェクトがあるか」を照合し、
食い違えばコマンドを拒否して `refreshRequired` push を返す。

`PanelCommandRouter.ResolveObjectId` を設定するだけで、
既存の全コマンドに自動で `objectIds` が付く（コマンドクラスの改造は不要）。

---

## 2. 新規ファイル

### `Runtime/Poly_Ling_Main/Core/Data/ObjectIdAllocator.cs`

安定IDの発行器。

| API | 用途 |
|---|---|
| `Next()` | 新規ID発行（Interlocked、起動時刻Ticks初期値） |
| `Observe(id)` | 既存IDを観測してカウンタを追い越す（読込時） |
| `EnsureIds(contexts)` | 未割当（0）にだけ発行 |
| `ResolveDuplicates(contexts)` | 重複IDを後勝ちで振り直し（旧形式ファイル対策） |
| `IndexOfId(contexts, id)` | ID → 位置の逆引き |

IDはサーバ（＝`ProjectContext` を持つ側）でのみ発行される。
クライアントは `ProjectContext` を書き換えないので、単調増加カウンタで衝突しない。

### `Runtime/Poly_Ling_Remote/RemoteOwnership.cs`

認可判定の本体。状態を持たない純関数の集まり。

| API | 用途 |
|---|---|
| `TryAuthorize(project, cmd, requesterName, objectIds)` | コマンド実行可否 |
| `ResolveTargets(model, cmd)` | コマンド → 対象 MasterIndex[] |
| `VerifyObjectIds(model, indices, ids)` | 位置とIDの照合 |
| `BuildOwnershipJson(model, modelIndex)` | push 用の担当一覧 |
| `BuildOwnershipSignature(...)` | push 抑止用の差分検出 |

設定フラグ:

```csharp
RemoteOwnership.AllowUnownedEdit   = true;   // 担当者なしは誰でも編集可（false で claim 必須）
RemoteOwnership.AllowAnonymousEdit = false;  // 名無しクライアントの書き込みを拒否
```

**対象を特定できないコマンド**（`ResolveTargets` が `null` を返すもの）は保守的に扱う。
モデル内に他人の担当が1つでもあれば拒否する。新しいコマンドを追加した際に
`ResolveTargets` へ登録し忘れても「素通し」にはならず、安全側に倒れる。

---

## 3. 改修した既存ファイル

### データ層

| ファイル | 内容 |
|---|---|
| `MeshContext.cs` | `ObjectId` / `EditorName` / `HasEditor` / `IsEditableBy(userName)` |
| `ModelContext.cs` | `Add` / `Insert` で `ObjectId==0` のときだけ新IDを発行 |
| `ToolContext.cs` | `MeshAttributeChange.EditorName`（null=変更なし、""=解放） |

`Add`/`Insert` での「0のときだけ発行」が ID ライフサイクルの要。

- **複製**：`DuplicateMeshContentWithUndo` は `new MeshContext`（ObjectId=0）を作って
  `Insert` するので、自動的に**新IDが振られ EditorName も空**になる（＝別オブジェクト扱い）
- **Undo/Redo**：既にIDを持つ実体を挿し戻すので**IDが維持される**

### Undo 経路（担当変更は Undo 対象）

| ファイル | 内容 |
|---|---|
| `MeshListOps.cs` | `SetObjectEditor()` / `ReleaseAllByEditor()`、属性適用に `EditorName` を追加 |
| `MeshListRecords.cs` | スナップショットに `ObjectId`/`EditorName`、`CloneMeshContext` はIDを保持 |
| `PolyLingCore_UndoOperations.cs` | 旧属性更新経路にも `EditorName` を追加 |

> `CloneMeshContext` は Undo 復元（`RestoreList` / `CaptureList`）が使うため、
> **IDを引き継ぐ**のが正しい。複製にこの経路を使わないこと。

### コマンド層

| ファイル | 内容 |
|---|---|
| `PanelCommand.cs` | `SetObjectEditorCommand`（claim / release / force を1コマンドに統合） |
| `PolyLingCore_Commands.cs` | ディスパッチ追加 |

`EditorName` が空文字なら解放、自分の名前なら取得。到達時点で権限判定は済んでいる
（リモートは `TryAuthorize`、ローカルはホスト自身の操作）ので `force` で確定適用する。

### 通信層

| ファイル | 内容 |
|---|---|
| `RemoteServerCore.cs` | 認可ゲート、`ResolveUserName`、`setObjectEditor` アクション、`ownership` クエリ、`ownershipChanged`/`refreshRequired` push |
| `RemoteProgressiveSerializer.cs` | **PLRS v3**（`ObjectId` + `EditorName` を追加） |
| `PanelCommandRouter.cs` | `ResolveObjectId` 注入、全コマンドへ `objectIds` 自動付与 |
| `ListClientBase.cs` | `UserName`（空欄なら端末名）、ID解決子の結線、`ownershipChanged` の軽量反映 |

PLRS はバージョン付きなので `summaryVersion >= 3` で読む。v2 以前のクライアントとも共存できる。

`ownershipChanged` push は「担当者がいるオブジェクトのみ」を送るため、
クライアント側は反映前に一度 `EditorName` をクリアしてから入れ直す（解放を反映するため）。

### 表示層

| ファイル | 内容 |
|---|---|
| `ViewInterfaces.cs` / `MeshSummary.cs` / `LiveViews.cs` | `IMeshView` に `ObjectId` / `EditorName` |
| `SummaryTreeAdapter.cs` | 透過プロパティ + `HasEditor` |
| `MeshListSubPanel.cs` | 担当者バッジ、取得／解放ボタン、`LocalUserName`、`ClaimSelected(bool)` |
| `MeshListClient.cs` | `LocalUserName` の結線 |

`MeshSummary` の追加引数は**末尾のオプショナル**にしたので、既存の呼び出しは無改修で通る。

行の表示規則:

| 状態 | バッジ | ボタン |
|---|---|---|
| 担当者なし | 非表示 | ✋（押すと取得） |
| 自分が担当 | 着色表示 | ✔（押すと解放） |
| 他人が担当 | 着色表示 | 無効・行を淡色化 |

バッジ色は名前のハッシュから HSV で決めるので、同じ人は常に同じ色になる。

### 永続化

| ファイル | 内容 |
|---|---|
| `CsvMeshSerializer.cs` | `objectId` / `editorName` 行の読み書き |
| `CsvModelSerializer.cs` | 読込直後に `ResolveDuplicates` |

key-value 形式なので旧形式ファイルとの互換性あり。`objectId` 行が無い場合は 0 のまま読まれ、
`ResolveDuplicates` または `ModelContext.Add/Insert` が新IDを振る。

---

## 4. 動作の流れ

### 担当の取得

```
[クライアント] 行の ✋ を押す
  → SetObjectEditorCommand(modelIndex, [masterIndex], "hagihara", [objectId])
  → PanelCommandRouter が objectIds を添えて setObjectEditor を送信
[サーバ] BuildPanelCommand → RemoteOwnership.TryAuthorize
  ├ 要求者名が register 済みか
  ├ 自分以外の名前を設定しようとしていないか
  ├ objectIds が位置と一致するか（ズレ検出）
  └ 対象が担当者なし or 既に自分か
  → OK なら DispatchCommand → MeshListOps.SetObjectEditor → Undo記録
  → CheckOwnershipChanged → ownershipChanged push（差分があるときだけ）
[全クライアント] バッジ更新（全体再フェッチなし）
```

### 他人の担当への編集要求

```
[クライアント] 他人の担当メッシュに toggleVisibility
[サーバ] TryAuthorize → Deny("編集できません → 頭（担当: tanaka）")
  → error 応答（コマンドは実行されない）
```

### リスト構造がズレていた場合

```
[サーバ] VerifyObjectIds が不一致を検出
  → Stale 判定 → 当該クライアントにのみ refreshRequired push
[クライアント] RefreshData() で project_header を再取得
```

---

## 5. 残っている課題

### 5-1. バイナリ経路（PositionsOnly）の対象指定 → **対応済み（追補B 参照）**

PLRM ヘッダを v2（28B）へ拡張し、`ModelIndex` + `ObjectId` を載せた。

### 5-2. ホスト側UIの強制解放ボタン

`MeshListOps.ReleaseAllByEditor(editorName)` は実装済みだが、
`PlayerRemoteServerSubPanel` への導線（接続中ユーザー一覧＋強制解放ボタン）は未着手。
`_clientRegistry` を公開するアクセサを1つ足せば繋がる。

### 5-3. 構造変更コマンドの粒度

`addMesh` / `duplicateMeshes` / `switchModel` は「新規作成なので担当と無関係」として
素通しにしてある。`reorderMeshes` は `ResolveTargets` が `null` を返すため
モデル全体判定（他人の担当が1つでもあれば拒否）になる。運用してみて厳しすぎるようなら
`ResolveTargets` に個別ケースを追加して緩める。

---

## 6. 導入手順

1. ZIP を展開し、`Runtime/` 以下へ上書き
2. Unity でコンパイルを通す
3. クライアント側の GameObject（`MeshListClient` 等）の Inspector で
   **User Name を設定**（空欄なら端末名が使われる）
4. 厳格運用にする場合は起動時に一度だけ設定:
   ```csharp
   Poly_Ling.Remote.RemoteOwnership.AllowUnownedEdit   = false; // claim 必須
   Poly_Ling.Remote.RemoteOwnership.AllowAnonymousEdit = false; // 名無し禁止
   ```

### 動作確認の観点

- 2クライアントを別名で接続し、片方が claim したオブジェクトを他方が操作 → 拒否されるか
- claim → 解放 → 他方が claim できるか
- 担当設定後に Undo → 担当が戻るか
- 保存 → 読込 → 担当が復元されるか
- 一方が複製したオブジェクトに担当が付いていないか（新ID・空担当になっているか）
- 一方が削除した直後に他方が古いインデックスで操作 → `refreshRequired` が飛ぶか

---

# 追補: ユーザーごとの選択（案B）

所有権だけでは「書き込みの衝突」は防げても「作業対象の奪い合い」は防げない。
選択が共有 `ModelContext` 1組だったため、A が選択した瞬間 B の選択も飛ばされ、
担当を分けても同時作業ができなかった。これを是正する。

## A-1. 採用した方式

`PanelCommand` のうち約48種は `MasterIndex` を持たず「今の選択」を見て動く
（PartsSet 系・SkinWeight 系・Morph 系・TPose 系など）。
これら全部に明示ターゲットを持たせる案（案A）ではなく、**選択スコープの一時差し替え**を採った。

```
1. 選択は共有 ModelContext ではなくユーザー名ごとのスロットに持つ
2. コマンド実行の直前だけ、要求者の選択を ModelContext へ流し込む
3. 実行後にホストの選択へ戻す
```

差し替えは `using` スコープ1つで済み、今後追加されるコマンドも自動的に正しい選択を見る。

## A-2. 新規ファイル

### `Runtime/Poly_Ling_Remote/RemoteSelectionStore.cs`

| 型 | 役割 |
|---|---|
| `UserSelection` | ユーザー1人分の選択（modelIndex / category / drawable / bone / morph） |
| `RemoteSelectionStore` | ユーザー名 → 選択 の保管庫 |
| `SelectionScope` | `IDisposable`。using の間だけ選択を差し替え、Dispose で必ず復帰 |

キーが**ユーザー名**なので、同一ユーザーが meshList / modelList / materialList を
同時に開いていても選択が共有される（同じ人の画面は連動する）。

`UserSelection.ApplyTo` は範囲外インデックスを落とす。
他ユーザーがメッシュを削除した後の古い選択が紛れ込むため。

**要素選択（頂点・辺・面 = `MeshContext.Selection`）は含めない。**
所有権により1オブジェクト＝1編集者が保証されるので、共有のままで書き込み衝突が起きない。

## A-3. `RemoteServerCore` の変更

| 箇所 | 変更 |
|---|---|
| `ProcessCommandViaPanelCommand` | `SelectMeshCommand` を横取りし `HandleRemoteSelect` へ。本体へは流さない |
| 同上 | 所有権ゲート通過後、`DispatchWithSelectionOf` でスコープ差し替えして実行 |
| `HandleRemoteSelect` | 要求者のスロットを更新し、**本人のチャネルにだけ** selectionChanged を返す |
| `NotifySelectionChanged` | 全体配信をやめ、ホスト自身のスロット更新のみに変更 |
| `TryHandleRegister` | 接続時にスロットを用意（初回はホストの現在選択を種にする）→ ack 後に本人へ送信 |
| `ProcessQuery` | project_header / model_meta の応答**後**に本人の選択を送り直す予約 |
| `BroadcastSelectionToAll` | 旧来の一斉配信。協働編集では使わないが単独利用向けに残置 |

### 送出順序に注意

`SerializeModelMeta` は `SelectedDrawableMeshIndices`（＝ホストの選択）を載せる。
そのままだとクライアントの選択がホストのもので上書きされる。

WebSocket は FIFO なので、選択 push を**先に**送ると応答が後着して上書きされてしまう。
そこで `_pendingSelectionUser` に予約を立て、`SendReply` の**後**に
`FlushPendingSelection` で送る（既存の `_pendingBinaryResponses` と同じ流儀）。

### ホストUIのチラつき対策

差し替え中に実行されたコマンドが `NotifyPanels` を呼ぶと、
ホストのUIが一時的に他ユーザーの選択で描画される。
復帰後に `RequestPanelRefresh`（未設定なら `OnRepaint`）を呼んで再同期する。

`PolyLingPlayerServer.Initialize` に省略可能引数を追加した:

```csharp
Initialize(port, autoStart, getToolContext, dispatchCommand,
           requestPanelRefresh: () => NotifyPanels(ChangeKind.Selection),
           hostUserName: "hagihara");
```

**既存の呼び出しは無改修で通る**（両方とも既定値あり）。
未指定でも動くが、ホストUIの再同期精度を上げるには `requestPanelRefresh` の指定を推奨。

## A-4. `ListClientBase` の変更

push の振り分けを `if/else` から明示的な `switch` に変更した。

| イベント | 動作 |
|---|---|
| `selectionChanged` | インライン反映（自分宛にしか飛んでこない） |
| `ownershipChanged` | バッジ更新のみ |
| `meshListChanged` / `refreshRequired` | `RefreshData()` で再取得 |
| **未知** | **無視** |

以前は未知イベントが全部 `RefreshData()` に落ちていた。
将来 push を追加するたびに全クライアントが `project_header` を引き直して重くなるため、
明示ルーティングに変えた。

## A-5. 切断時の扱い

選択スロットは**消さない**。ユーザー名をキーに保持し続ける。
再接続時に作業対象が消えていると使い勝手が悪いため、担当（`EditorName`）と同じ扱いにした。

## A-6. ホスト名の重複に注意

`RemoteServerCore.HostUserName`（既定 `"(host)"`）とクライアントの `UserName` が
**同名だと選択スロットを共有してしまう**。運用上は重複しない名前を割り当てること。

## A-7. 未実装（プレゼンス表示）

「誰がどこを見ているか」の可視化は入れていない。
`_selectionStore.All` を走査して push すれば実装できるが、
`SelectionFlags` への追加と描画側の対応が要るため分離した。

## A-8. 動作確認の観点（追加分）

- 2クライアントを別名で接続し、A が選択 → **B の選択が動かない**か
- 同一ユーザー名で2パネル開き、片方で選択 → **もう片方も連動する**か
- A が選択した状態で PartsSet 保存など ambient 依存コマンドを実行 → A の選択が対象になるか
- 上記の実行後、**ホストのUIの選択が元に戻る**か
- 接続直後、クライアントの選択がホストの選択に化けないか
- 切断→再接続で選択が保たれるか

---

# 追補B: PLRM ヘッダ v2（PositionsOnly の対象指定）

## B-1. 何が問題だったか

`BinaryMessageType.PositionsOnly` は対象メッシュを運べず、受信側は
`FirstDrawableMeshContext`（サーバ）/ `ActiveMeshContext`（クライアント）決め打ちで
適用していた。複数人が同時に頂点を動かすと**全員の編集が同じメッシュへ流れ込む**。

所有権チェックも「対象が分からない」以上は原理的に書けない。

## B-2. ヘッダ拡張

```
v1 (20B): Magic(4) Version(1) MsgType(1) FieldFlags(4) VertexCount(4) FaceCount(4) Reserved(2)
v2 (28B): Magic(4) Version(1) MsgType(1) FieldFlags(4) VertexCount(4) FaceCount(4)
          ModelIndex(2) ObjectId(8)
                ↑ v1 の Reserved を転用    ↑ 新規追加
```

`BinaryHeader` に追加したもの:

| メンバ | 内容 |
|---|---|
| `SizeV2 = 28` | v2 ヘッダ長（`Size = 20` は v1 として残置） |
| `CurrentVersion = 2` | 新規送信時のバージョン |
| `ModelIndex` / `ObjectId` | 対象。ObjectId==0 は「未指定」 |
| `HeaderSize` / `SizeOf(version)` | バージョンから本体オフセットを求める |
| `HasTarget` | `Version >= 2 && ObjectId != 0` |

**読み取りは両バージョン対応**。書き出しは常に v2 に統一した
（`WriteHeaderV2` に集約。`MeshData` / `PositionsOnly` / `RawFile` すべて）。

v1 を受信した場合は `ObjectId == 0` として扱い、従来のフォールバック動作に落ちる。
旧クライアントが混ざっても壊れない。

## B-3. 追加した API

```csharp
// 対象付き（協働編集ではこちらを使う）
RemoteBinarySerializer.SerializePositionsOnly(MeshContext mc, int modelIndex = 0);
RemoteBinarySerializer.Serialize(MeshContext mc, MeshFieldFlags flags, int modelIndex = 0);
RemoteServerCore.BroadcastPositions(MeshContext mc, int modelIndex = -1);
PolyLingPlayerServer.BroadcastPositions(MeshContext mc, int modelIndex = -1);

// 対象なし（非推奨・v1互換）
RemoteBinarySerializer.SerializePositionsOnly(MeshObject mesh, ...);
RemoteServerCore.BroadcastPositions(MeshObject mesh);
```

`MeshObject` を取る旧シグネチャは残してあるので既存呼び出しは壊れないが、
XMLコメントに非推奨である旨を書いた。

## B-4. 対象解決のルール

送信側と受信側で `CurrentModelIndex` がずれていても当たるよう、2段階で探す。

```
1. ヘッダの ModelIndex のモデルを ObjectId で検索
2. 外れたら全モデルを走査
3. それでも無ければ「未知のオブジェクト」として無視
```

サーバ側は `RemoteServerCore.ResolveBinaryTarget`、
クライアント側は `PolyLingPlayerViewerCore.FindMeshByObjectId` が担う。

## B-5. 適用前の3つのガード

対象が確定したことで、以下が書けるようになった。

| ガード | 内容 |
|---|---|
| 対象解決 | 未知の ObjectId は無視 |
| **所有権** | `IsEditableBy(requesterName)` が false なら拒否（担当者以外の書き込みを弾く） |
| **頂点数一致** | `h.VertexCount != mesh.VertexCount` なら拒否 |

頂点数チェックは、他ユーザーがトポロジを変えた直後に古い編集が届いて
メッシュが壊れるのを防ぐ。ジオメトリを破壊しうる唯一の経路なので入れておく。

## B-6. 変更ファイル（追補B分）

| ファイル | 内容 |
|---|---|
| `RemoteBinaryProtocol.cs` | `BinaryHeader` v2 化、フォーマット仕様コメント更新 |
| `RemoteBinarySerializer.cs` | `WriteHeaderV2` 集約、対象付きオーバーロード、読み取りのバージョン対応 |
| `RemoteServerCore.cs` | `ResolveBinaryTarget` / `FindByObjectId`、3ガード、対象付き `BroadcastPositions` |
| `PolyLingPlayerServer.cs` | 対象付き `BroadcastPositions` を委譲 |
| `PolyLingPlayerViewerCore.cs` | 送信側で `mc` を渡す、`ApplyRemotePositions` を ObjectId 解決に変更 |

## B-7. 検証済み事項

ヘッダのバイト配置は数値検証で確認した。

- v1 = 20B / v2 = 28B
- 両バージョンとも本体（頂点位置列）のオフセットが正しく求まる
- `ModelIndex = -1`（未指定）が int16 で往復する
- `ObjectId` が 64bit フルレンジで往復する

## B-8. 動作確認の観点（追加分）

- 2クライアントが**別々のメッシュ**を同時に頂点編集 → それぞれ正しいメッシュに反映されるか
- 他人の担当メッシュへ位置更新を送る → サーバログに拒否が出るか
- 一方がトポロジを変えた直後に他方の古い位置更新が届く → 頂点数不一致で弾かれるか
- 送信側と受信側で選択モデルが違う状態でも正しいメッシュに当たるか
- v1 のみ喋る旧クライアントを繋いでも従来どおり動くか（フォールバック確認）
