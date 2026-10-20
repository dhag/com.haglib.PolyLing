# PolyLing 外部連携の契約（コマンド・モーション・MediaPipe）

Python・ブラウザ・mocopi アプリなど、PolyLing の外のプログラムがやり取りするときの決まりをまとめる。
ここに書いた形が正本で、外側の実装はこれに合わせる。C# 側の型名・ファイル名は各節に書いた。
メッシュ・モデル本体の受け渡し（PLR 系バイナリ・CSV・ProjectDTO）の形そのものはこの文書の範囲外（配列での受け渡しは 3.6、Blender アドオンでの `.mfproj`・CSV フォルダの読み書きの決まりは 7）。

最終更新：2026-10-20（§3.7 に saveMotionClipJson、元ファイルの持ち主）／2026-10-20（§5.2 Blender とモーションを行き来させる 2 つの道筋）／2026-10-20（§3.7 モーションの受け渡し〔loadMotionClipJson・queryMotionClipJson、Blender ブリッジの送信・取得〕）／2026-10-20（Blender アドオン PolyLing IO：§5 に rotationSource、§5.1 Blender との対応、§7 プロジェクトの読み書き。旧 §7 は §8）／2026-10-11（モーション version 4：bakedBones を正準に固定、lookAt を追加、sourceRest を廃止）／2026-10-06（Root を body トラックに統一、sourceRest を基準姿勢の正本に、detectImage を要求応答に、コマンド応答の共通化とエラーコード、クライアントへの要求）

---

## 1. 共通の規約

| 項目 | 決まり |
|---|---|
| 座標系 | Unity 左手系・Y 上。人（キャラクター）は +Z を向き、本人の左手は -X 側 |
| 単位 | 位置はメートル。ただし HumanPose.bodyPosition（RootT）は腰高を 1 とする正規化量 |
| 四元数 | 並びは `[x, y, z, w]`（Blender の `(w, x, y, z)` とは違う） |
| 時刻 | 保存ファイルのキーは秒（float）。ライブの `time` は送り手の時計の秒で、送り手が違えば比べられない |
| 文字コード | UTF-8（BOM なし） |
| 数値 | 不変文化圏（小数点は `.`） |

---

## 2. 通信の口（ポート）

| ポート | 方式 | 役割 | 受け口（C#） |
|---|---|---|---|
| 8770 | TCP・1 行 JSON | MCP（Editor）。`ping` / `tools*` / `scenes` / `call` ほか Editor 操作 | `Editor/EditorControl/PolyLingEditorControlServer.cs` |
| 8772 | TCP・1 行 JSON | MCP（ビルド版）。8770 と同じ共通部 | `Runtime/Poly_Ling_Player/Remote/PolyLingPlayerControlServer.cs` |
| 8760 | TCP | リモートのサーバ一覧（マスター）。問い合わせと登録 | `Runtime/Poly_Ling_Remote/RemoteDirectory.cs` |
| 一覧で公開 | WebSocket（Duplex 封筒） | リモート（パネル・Python など）。query / command / push、PolyLing → クライアントの要求 | `Runtime/Poly_Ling_Remote/RemoteServerCore*.cs` |
| 12361（既定） | WebSocket（Duplex 封筒） | ライブ（マッスル・MediaPipe・detectImage） | `Runtime/Poly_Ling_Player/View/ToolHandlers/MotionLiveHandler.cs` |

### Duplex 封筒（WebSocket の Text フレーム）

```
{"type":"push"|"request"|"response", "id":整数, "items":[{"type":種類, "mimeType":"…", "data":"…", "encoding":"base64"?}]}
```

- `items[].type`：0 = Text、3 = Json（data は JSON 文字列）、2 = Image、それ以外はバイナリ（data は base64、`encoding:"base64"`）
- `request` には同じ `id` の `response` で返す。対応付けは部品（com.haglib.net_duplexchannel）が受け持つ。中身に自前の要求番号を入れない。
- Binary フレーム（DuplexPacket）の形は `polyling_live.js` の `decodeBinaryPacket` を参照。

---

## 3. コマンド

### 3.1 MCP の `call`（8770 / 8772）

要求：
```
{"type":"command","action":"call","params":{"command":"<コマンド名>","modelIndex":"0","wantRevision":"1"?, "<引数>":"<値>", …}}
```
値はすべて文字列。配列は `"1,2,3"`、入れ子は `"params.widthTop"`。

### 3.2 リモートの `command`（WebSocket）

要求（Duplex の request で送る）：
```
{"id":"<任意>","type":"command","action":"<コマンド名>","params":{"modelIndex":"0","wantRevision":"1"?, "<引数>":"<値>", …}}
```
query は `{"type":"query","target":"meshList" など,"fields":[…]}`。push は `{"type":"push","event":"…","data":{…}}`。

### 3.3 応答の欄（両方で共通。`Core/Data/CommandResultJson.cs`）

成功：
| 欄 | 内容 |
|---|---|
| `masterIndices` | 作った・変えた対象の位置インデックス（int の配列。無ければ欄なし） |
| `objectIds` | 同じ並びの安定 ID（数の配列） |
| `modelRevision` / `createdObjectIds` / `deletedObjectIds` | `wantRevision` を付けたときだけ |
| `result` | コマンドが返した実データ（JSON オブジェクト） |

MCP の `call` はこれらを応答の直下に、リモートの `command` は `data` の中に入れる。

失敗：
| 欄 | 内容 |
|---|---|
| `error` | 人が読む理由 |
| `code` | 機械が判定する種類（下の表） |

| code | 意味 |
|---|---|
| `unknownCommand` | コマンド名が無い |
| `invalidArgs` | 引数が足りない・型にできない |
| `notReady` | パネルが開いていない・受け口の準備ができていない・相手がつながっていない |
| `unauthorized` | 担当者判定で拒否（ほかの人の担当） |
| `staleView` | 送り手の見ている構造が古い。取り直してから送り直す |
| `exception` | 実行中に例外 |
| `failed` | 上のどれでもない失敗 |

### 3.4 登録と版（リモート）

```
{"type":"command","action":"register","params":{"clientType":"…","userName":"…","capabilities":"action1,action2"}}
```
- ack の `data` に `protocolVersion`（現在 1）が入る。`server_info` にも入る。
- `capabilities` は、PolyLing からの要求（3.5）でそのクライアントが受けられる action の一覧。

### 3.5 PolyLing → クライアントの要求

1. PolyLing はコマンド `requestClient`（userName・action・paramsJson・timeoutMs）で要求を送り、要求番号を受け取る。返事は待たない。
2. クライアントへは Duplex の request で `{"type":"request","action":"<action>","params":{…}}` が届く。
3. クライアントは同じ封筒 id の response で `{"success":true,"data":{…}}` か `{"success":false,"error":"…","code":"…"}` を返す。
4. `queryClientRequest`（requestNo）で `status`（pending / done / failed）・`data`・`error`・`code` を取り出す。
5. `queryRemoteClients` で登録済みのクライアント（clientType・userName・capabilities）を見る。

### 3.6 メッシュの受け渡し（Blender ブリッジなど）

配列の形は `getRawData` の結果と同じ平坦な形。座標と面の並びは PolyLing のまま（Unity 左手系・ローカル座標）。外部ツールの座標系への変換は外側が行う。

| 向き | 方法 |
|---|---|
| 外 → PolyLing（新規） | コマンド `addMeshFromArrays`（meshName・positions・faceSizes・faceVertices。面は 3 頂点以上）。応答の `masterIndices` / `objectIds` が作った描画オブジェクト |
| 外 → PolyLing（頂点の移動） | `getRawData`（scope=Object）で面を照合してから `setVertexPositions`。どちらも封筒の `objectIds` を添える（対象がずれていれば `staleView`） |
| PolyLing → 外（取り込み） | `getRawData`（scope=SelectedObjects、includeFaces）。選択は register の userName のもの |
| PolyLing → 外（押し出し） | コマンド `sendMeshToClient`（masterIndex・userName・timeoutMs）。3.5 の仕組みで action `putMesh` を送る |

`putMesh` の params：
```
{"name":"…","modelIndex":0,"masterIndex":3,"objectId":"<10 進の文字列>","positions":[x,y,z,…],"faceSizes":[…],"faceVertices":[…]}
```
返事の data は受け手が決める（Blender ブリッジは `{"object":"<Blender のオブジェクト名>"}`）。

Blender ブリッジ（`データとツール/poly_ling_py/polyling_bridge.py`）の決まり：
- clientType `blender`、userName はホストと同じ名前（PolyLing の選択をそのまま使う）、capabilities `putMesh`。
- Blender (x, y, z) ⇔ PolyLing (x, z, y)。面の頂点の並びは逆にする。Blender → PolyLing は配置（matrix_world）を頂点へ焼いて送る。PolyLing → Blender は配置を単位行列にして置く。
- PolyLing の番号はカスタムプロパティ `polyling_object_id` / `polyling_master_index` / `polyling_model_index` に持つ。同じ `polyling_object_id` のオブジェクトはメッシュを置き換える。

### 3.7 モーションの受け渡し（Blender ブリッジなど）

中身は `.plmotion.json` v4（§5）のファイルの中身そのままの JSON 文字列。受け手はモーションの試し再生（ツールの窓口 `motionClip`、`MotionClipHandler`）で、当てる先は PolyLing で今選んでいるモデル（封筒の `modelIndex` は使わない）。どちらも 3.2 の `command` で送る（PolyLing からの押し出し〔3.5〕は持たない）。

| 向き | 方法 |
|---|---|
| 外 → PolyLing（送信） | コマンド `loadMotionClipJson`（`motionJson`＝JSON 文字列、`time`＝当てる時刻〔秒、既定 0〕）。`MotionClipSerializer.Parse` で読み（version 1〜3 の引き上げ、bakedBones だけなら muscles＋body を焼いて rotationSource "bakedBones"。§5）、窓口 `motionClip` のクリップを差し替えて `time` のフレームを当てる。位置の倍率は 1。ファイルは書かない。読めなければ `invalidArgs`（今のクリップのまま）。窓口の `sourcePath` は空、`sourceKind` は 1（PolyLing モーション）になり、パネルの表示はこのクリップに合わせる（元ファイルが無いので、`saveMotionClipJson` かパネルの JSON 保存でファイルにするまでは、パネルの読み直し・VRMA・BVH・FBX 書き出しは使えない） |
| PolyLing → 外（取得） | コマンド `queryMotionClipJson`（引数なし）。窓口 `motionClip` に読み込んであるクリップを `MotionClipSerializer.ToSaveJson` で文字列にして返す。中身は `MotionClipSerializer.Save`（パネルの JSON 保存・`exportMotionJson`）がファイルに書くものと同じ（写しを整える：muscles を持つクリップは rotationSource が "bakedBones" でなければ bakedBones を作り直して添える、キーの並べ替え、検査、duration が 0 以下なら最大キー時刻）。クリップが無ければ `notReady` |
| PolyLing の中でファイルにする | コマンド `saveMotionClipJson`（`filePath`＝作業フォルダの下の .plmotion.json）。窓口のクリップを `queryMotionClipJson` と同じ中身（BOM なしの UTF-8）で書き、そのファイルを窓口の元ファイルにする（`sourcePath`＝そのファイル、`sourceKind`＝1）。パネルの JSON 保存も、元ファイルの無いクリップではこれを使う。クリップが無ければ `notReady` |

`loadMotionClipJson` の結果（`data.result`）：`name`・`durationSec`・`frameRate`・`bakedBones`・`muscles`・`expressions`（トラック数）・`matchedTracks`（モデルのボーンと対応が取れた bones トラック数）・`warnings`・`issues`。
`queryMotionClipJson` の結果：`motionJson`・`name`・`durationSec`・`sourcePath`（窓口の元ファイル。JSON 文字列から読んで保存していないクリップは空）。
`saveMotionClipJson` の結果：`path`（実際に書いた経路）・`name`・`durationSec`。
元ファイルとその種類は窓口 `motionClip` の状態 `sourcePath`・`sourceKind` だけが持ち、パネルはそれを読む。

Blender ブリッジ（`polyling_bridge.py`）の決まり：
- モーションの変換は Blender アドオン PolyLing IO（`polyling_io`、§5.1・§7）の `blender/motion.py` をそのまま使う（`export_motion_text`・`import_motion_text`。ファイルの読み書き〔`export_motion`・`import_motion`〕と同じ処理で、文字列は `.plmotion.json` のファイルとバイト単位で同じ）。PolyLing IO が入っていない・有効でなければモーションのボタンだけが失敗する。PolyLing IO はモジュール名 `polyling_io`（入れたフォルダ名）で探す。
- 対象のアーマチュアは §5.1 と同じ（選んだオブジェクトがアーマチュア・その親・Armature 修飾子の相手）。オブジェクトモードかポーズモードで使う。
- 送信（パネルの「送る」）：今のアクションを §5.1 の書き出しの決まりで文字列にして `loadMotionClipJson` で送る。`time` は今のフレームの時刻（(今のフレーム − 開始) ÷ fps。開始・fps も §5.1 の書き出しと同じ）。
- 取得（パネルの「取得」）：`queryMotionClipJson` の `motionJson` を §5.1 の読込の決まりでアクションにする（名前はクリップの `name`、無ければ結果の `name`、それも無ければ "PolyLing"）。選べること（腰の回転と位置も当てる・脚も T ポーズに合わせる・シーンの fps と範囲を合わせる）と既定値は §5.1 の読込と同じ。
- 返事の待ち時間はモーションの送受信だけ 120 秒（数 MB の JSON になる）。

---

## 4. ライブ（ポート 12361。`Motion/MotionLiveJson.cs`）

中身の JSON の `type` で振り分ける。

### 4.1 `muscles`（マッスル 1 フレーム）

```
{"type":"muscles","source":"mocopi"|"mediapipe"|"playback"|…,"seq":連番,"time":秒,
 "muscles":{"<HumanTrait.MuscleName>":値, …},
 "rootT":[x,y,z], "rootQ":[x,y,z,w],
 "bones":{"<HumanBodyBones 名>":[x,y,z,w], …}, "hips":[x,y,z]}
```
- `source`・`rootT`・`rootQ`・`bones`・`hips` は無いこともある。`seq` は送り手ごと。
- **`hips`** は `bones` と同じフレームだけが持つ。正準骨格の Hips の正規化位置（レスト位置からの差 ÷ レストの腰の高さ、Unity 左手系。§5 の `bakedBones` の Hips の `pos` と同じ意味）。
- **`bones`** は PolyLing が送るフレーム（`playback`・`mediapipe`、および外から受けて転送するフレーム）だけが持つ。正準骨格（§5 の `bakedBones` と同じ骨格）上の Humanoid ローカル回転（Unity 左手系）を Unity（HumanPoseHandler）で解いたもの。載るのは駆動されているボーン（マッスルを 1 本でも持つ）と Hips。載っていないボーンは単位（祖先に付いていく）。値の無いマッスルは、駆動されているボーンの dof なら 0 として解く（`absentMuscles = "rest"` のクリップの再生配信は、その dof を T ポーズの値で埋めて `muscles` に載せる）。外部の画面は骨をこれで動かし、マッスルから自前で合成しない。
- PolyLing はモデルへ当て（マッスルだけ。Root は当てない）、メインスレッドで `bones`・`hips` を添えて作り直したフレームを、送ってきた接続以外へ転送する（`source`・`seq`・`time` は送り手の値のまま。適用と同じく最新の 1 フレームだけを送る）。前の送信が終わっていない接続にはそのフレームを送らない。
- 送り手：mocopi 受信アプリ（PolyLing_mocopi の MocopiMuscleSender）、PolyLing の再生配信、MediaPipe 姿勢（mediaPipePose）。

### 4.2 `mediapipe`（MediaPipe の生データ。`MediaPipe/MediaPipeFrame.cs`）

```
{"type":"mediapipe","seq":連番,"time":秒,"image":{"w":幅,"h":高さ},
 "face":{"landmarks":[[x,y,z,visibility], …]},                      … 478 点（先頭 468 点が顔、残りは虹彩）
 "pose":{"landmarks":[…],"world":[…]},                              … 33 点
 "leftHand":{"landmarks":[…],"world":[…]}, "rightHand":{…}}           … 21 点
```
- landmarks は画像基準の正規化座標（x 右 0～1・y 下 0～1・z 奥が正）、world はメートル。visibility は無ければ null。
- PolyLing は最新の 1 件だけを持つ（当てない・転送しない）。
- leftHand / rightHand が本人の左右か画像の左右かは未確認（mediaPipePose の swapHands で入れ替える）。

### 4.3 `detectImage`（PolyLing → クライアントの要求）

- Duplex の request。items は `{"type":"detectImage"}`（Json）と PNG 画像 1 枚（Image）。
- クライアント（`mediapipe_client.html`）は同じ封筒 id の response で 4.2 の形（face だけ）を返す。顔が無ければ face を入れずに返す。
- 検出中に次の要求が来たとき・検出に失敗したときも、同じ id で `{"type":"mediapipe","seq","time","image":{"w":0,"h":0}}`（face なし）を返す。返事をしない接続は PolyLing 側の時間切れ（20 秒）で捨てる。

### 4.4 MediaPipe の座標変換（`MediaPipe/MediaPipeCoords.cs`）

- MediaPipe → Unity：`(x, y, z) → (-x, -y, -z)`（行列式 -1。鏡像にならない）
- 正規化 → 画素：`(x·w, y·h, z·w)`

---

## 5. モーションの保存形式（PolyLing モーション、`*.plmotion.json`。`Motion/MotionClipDTO.cs`）

保存・受け渡しの形式はこれ 1 つ（旧 UnityMotionSamples と MotionDTO の JSON は廃止）。

```
{"format":"PolyLingMotion","version":4,"name":"…","frameRate":30,"duration":秒,"space":"local","loop":false,
 "bones":[トラック…], "bakedBones":[トラック…], "body":トラック|null,
 "lookAt":トラック|null, "lookAtOffsetFromHeadBone":[x,y,z]|null, "absentMuscles":"zero"|"rest"|null,
 "rotationSource":"muscles"|"bakedBones"|null,
 "muscles":[{"name":"<HumanTrait.MuscleName>","keys":[{"t":秒,"v":値,"tan":…?}], "preWrapMode":…, "postWrapMode":…}],
 "expressions":[{"name":"…","category":…,"provider":…,"keys":[…]}],
 "metadata":{"author":…,"description":…,"createdWith":…}}
```
トラック：`{"id":"…","targetKind":"…","keys":[{"t":秒,"pos":[3]?,"rot":[4]?,"scl":[3]?,"posTan":…,"rotTan":…,"sclTan":…}],"preWrapMode":…,"postWrapMode":…}`

| 欄 | targetKind | id | キーの中身 |
|---|---|---|---|
| `bones` | `boneName`（モデルの骨名で直接。VMD 由来）か `path`（Transform パス末尾で対応表） | 骨名 / パス | pos / rot / scl |
| `bakedBones` | `humanoid` だけ | HumanBodyBones の列挙名 | rot。Hips だけ pos も可。scl は不可 |
| `body` | `body` だけ | `body` | pos = bodyPosition、rot = bodyRotation |
| `lookAt` | `lookAt` だけ | `lookAt` | rot だけ |

- **再生の正本は `rotationSource` で決まる**（`MotionClipDTO.cs` 冒頭、`MotionClipApplier.cs` 冒頭）。`"muscles"`（null も同じ）は `muscles`＋`body`。`"bakedBones"` は本体の骨を `bakedBones` の正準ローカル回転で当てる（BVH・FBX・VRMA の読込と、`muscles` が無く `bakedBones` だけのファイル。マッスルで表せない回転も残る）。`"bakedBones"` なのに `bakedBones` が無いとエラー。
- **`absentMuscles`**：トラックを 1 本以上持つボーン（駆動されているボーン）のうち、トラックの無い dof の値。`"zero"` は 0（Unity のマッスル 0）、`"rest"` は正準骨格の T ポーズのマッスル値。書いていなければ `"zero"`。再生・`bakedBones`・VRMA 書き出しで同じ値を使う。トラックが 1 本も無いボーンはどちらでも祖先に付いていく（正準では単位ローカル）。ライブ受信（§4.1）は値の無い dof を常に 0 で解く。
- **`bakedBones` は正準骨格上の Humanoid ローカル回転**。正準骨格は VRM T ポーズ（+Z 正面）で、レストの全ローカル回転が単位。モデルに依らない量。
  Hips の `pos` は「レスト位置からの差 ÷ レストの腰の高さ」（腰高 1 の正規化量）。
  C#（PolyLing）が書くファイルには、マッスルから Unity（HumanPoseHandler）で解いた `bakedBones` を必ず添える。
  Unity の外（JS・Python など）は骨を動かすときにこれを使い、マッスルから回転を自前で合成しない（Unity の dof 合成規則は再現できない。`UnityClip/CanonMuscleSolver.cs` 冒頭の実測）。
  `muscles` が無く `bakedBones` だけのファイルは、PolyLing が読込時に正準骨格でマッスル＋body へ焼き、`rotationSource` を `"bakedBones"` にする（`MotionClipSerializer.Parse`）。
- **視線は `lookAt`**。rot は VRMA（VRMC_vrm_animation 1.0）の lookAt ノードの局所回転と同じ意味（Unity 左手系。回転なしの視線はモデル空間の +Z。yaw・pitch は Extrinsic ZXY で読む）。`lookAtOffsetFromHeadBone` は視線の原点の Head からの差（メートル、Unity 左手系）。
- **Root（RootT / RootQ）は `body` に置く**（pos = bodyPosition、rot = bodyRotation）。muscles に `RootT.x` などを入れたファイルはエラーになる（version 1・2 でも）。旧ファイルはコマンド `convertMotionRootToBody`（フォルダ指定）で一度だけ書き換える。
- `sourceRest`（基準姿勢）と UnityBone CSV は version 4 で廃止。読込時に `sourceRest` があれば捨てて警告する。
- version 1〜3 の `bakedBones` は意味が正準と限らないため、読込時に捨てて警告する。
- 接線（tan）の無いキーは線形（回転は Slerp）。評価規則の正本は `Motion/MotionCurveMath.cs`。
- 読み込みは version 1〜3 を 4 へ引き上げる。書き出しは常に version 4。
- 検査規則の正本は `Motion/MotionClipSerializer.cs`（Validate）。
- モーションスタジオ（`データとツール/VRMビュワー/polyling_motion_studio/timeline.html`）も同じ規則に従う（PolyLing_VRMA整備計画.md 段 ⑦ で対応）。

可動域（UnityLimit CSV v1）は別ファイルのまま（`UnityClipApplier.LoadMuscleLimitCsv`）。

### 5.1 Blender との対応（アドオン PolyLing IO、`データとツール/poly_ling_py/polyling_io/blender/motion.py`）

入口は［ファイル > インポート／エクスポート > PolyLing モーション (.plmotion.json)］、または 3D ビューのサイドバーのタブ［PolyLing］の「モーションを読む」「モーションを書く」（アドオンの入れ方は §7.1）。
対象のアーマチュアは、選んだオブジェクトがアーマチュアならそれ、親がアーマチュアならその親、Armature 修飾子があればその相手。
Blender ブリッジのモーションの送信・取得（§3.7）も同じ処理（ファイルの代わりに同じ中身の文字列）を使う。
回転・位置は PolyLing 空間（Unity 左手系）で計算し、Blender とは `R_bl = C·R_pl·C`、位置は Blender (x, y, z) ⇔ PolyLing (x, z, y)（C は y と z の入れ替え。§3.6・§7.3）。

**表すもの**

| `.plmotion` の欄 | Blender |
|---|---|
| `bakedBones` | アーマチュアのアクション（名前はクリップの `name`、無ければファイル名）。割り当てのあるボーンの `rotation_quaternion`、腰を当てるときは Hips の `location` も |
| `expressions` | シェイプキーの値。メッシュの Key ごとのアクション（名前「＜クリップ名＞_＜オブジェクト名＞」） |
| `lookAt`・`lookAtOffsetFromHeadBone` | アーマチュアの子の空オブジェクト「＜アーマチュア名＞_視線」（カスタムプロパティ `polyling_lookat`、表示は矢印）の回転と位置。アクション名「＜クリップ名＞_視線」 |
| `muscles`・`body`・`bones`（二次骨）・`absentMuscles`・`metadata` ほか | 表さない。読んだファイルを丸ごとアーマチュアのアクションのカスタムプロパティ `polyling_motion`（JSON）に残し、書き出しで戻す |

アーマチュアのアクションには `polyling_frame_rate`（frameRate）・`polyling_frame_start`（0）・`polyling_apply_root`・`polyling_align_legs`（読んだときの選択）も残す。

**割り当て表**（Humanoid 名 → ボーン名）

- アーマチュアのオブジェクトの `polyling_humanoid`（各行が `humanoid`＝空白なしの Humanoid 名と `bone`＝ボーン名）。行は正準骨格の 54 本（`format/humanoid.py` の `CANON_ROWS`＝`CanonSkeleton.cs` の `Table` の写し）。`bone` が空・アーマチュアに無い名前の行は割り当て無し。サイドバー［PolyLing］の表で選び直せる。
- 「自動で割り当て」：PolyLing の再生（`UnityClipApplier.ResolveNode`）と同じ順。① ボーンの `humanBodyBone`（取り込んだボーンの `polyling_mesh`。先勝ち）② 埋め込みの別名表（`HumanoidBoneMapping.EmbeddedMapping` の写し。別名ごとに大小無視の完全一致 → 部分一致、`FindBoneByAliases` と同じ）③ Humanoid 名そのもの（指は空白ありの名前でも）を ② と同じ照合で。ボーンは PolyLing の索引順（取り込んだボーンの `polyling_index`）に並べて探す。
- 「VRM アドオンから取り込む」：VRM Add-on for Blender の `armature.data.vrm_addon_extension`。VRM 0.x は `vrm0.humanoid.human_bones` の各要素の `bone`（先頭を大文字にして Humanoid 名）と `node.bone_name`。VRM 1.0 は `vrm1.humanoid.human_bones.<名前>.node.bone_name`（`<名前>` は snake_case、例 `left_upper_arm`）。VRM 1.0 の親指は `thumbMetacarpal`・`thumbProximal`・`thumbDistal` を Unity の `ThumbProximal`・`ThumbIntermediate`・`ThumbDistal` にする。正準骨格にある名前でアーマチュアにあるボーンだけを入れる。読めないか 1 本も無ければボタンは押せない。
- どちらのボタンも表を作り直す（割り当ての無い行は空になる）。読込・書き出しの時に表が空なら自動で割り当てて表に入れ、警告する。

**レスト姿勢 P0 と T ポーズ整列 A**

- P0 は各ボーンの「PolyLing のレスト（BindWorldMatrix）」でのポーズ行列（アーマチュア空間）。PolyLing から取り込んだアーマチュア（モデルのコレクションにあるもの）は P0 = C·(WorldMatrix·bindPose)·C·（取り込み時のレスト `polyling_rest_matrix`）。WorldMatrix はボーンの保持した姿勢から PolyLing の規則（BoneTransform の階層。ポーズ層は含めない）で組む。Blender でレストを変えたボーンは警告し、取り込み時のレストで計算する。
- PolyLing 由来でないアーマチュア・ボーンは、Blender のレスト（親の P0 にレストの相対で付ける）を P0 とする。
- レスト位置 = P0 の位置を PolyLing 空間にしたもの。腰の高さ h = Hips のレスト位置の Y。
- A（`UnityClipApplier.CanonAlignment` と同じ）：腕 8 本（左右の Shoulder・UpperArm・LowerArm・Hand）、「脚も T ポーズに合わせる」なら左右の UpperLeg・LowerLeg も、「正準の骨の向き → レストの『自分 → 正準の子』の向き」の最短弧。ほかのボーン、自分か正準の子に割り当てが無いボーンは単位。

**読込の式**（各キー時刻 t で）

1. q(t)・pos(t) は元のキーを線形（回転は Slerp）で評価、範囲の外は端の値。
2. 正準ワールド W_c：Hips は「腰の回転と位置も当てる」なら q_Hips、切れば単位。ほかは `CanonSkeleton` の親から順に W_c(子) = W_c(親)·q（トラックの無いボーンは W_c(親)。割り当ての無いボーンのトラックもここには使う。`UnityClipApplier.Apply.cs` の `ApplyCanonLocals` と同じ）。
3. 割り当てがありトラックのあるボーン（Hips は当てるときだけ）：Δ = W_c·A⁻¹、ポーズの回転 = (C·Δ·C)·回転(P0)。`matrix_basis` の回転は親の今のポーズから決め、位置・拡大は P0 のときの `matrix_basis` のまま。
4. Hips の位置（当てるとき）：レスト位置 + pos·h（`ModelMotionSampler.cs` の T = 移動(レスト位置 + 正規化位置 × 腰の高さ)·回転(Qc)·移動(−レスト位置) と同じ）。
5. それ以外のボーンは P0 のときの `matrix_basis` に戻す（キーは打たない）。トラックの無い Humanoid は祖先に付いていく。

**Hips の扱い**：読込の「腰の回転と位置も当てる」は既定オン。切ると PolyLing のパネル再生（`ApplyCanonLocals` は Hips を単位にする）と同じく Hips を当てない。その読み方のアクションを書き出すと Hips は元のトラックのまま。

**書き出しの式**（読込の逆。書くボーンの各時刻 t で）

- フレーム = 開始 + t·fps。各ボーンの `matrix_basis` を F カーブで評価し（F カーブの無いチャンネルは今のポーズの値）、親から順にポーズ行列 P を組む。
- 割り当てがあり書くボーン（Hips は腰を当てて読んだとき・元の無いアクションのとき）：Δ = C·(回転(P)·回転(P0)⁻¹)·C、W_c = Δ·A。
- 割り当ての無いボーンで元のトラックがあるもの：W_c = W_c(正準の親)·元の q(t)。
- そのほか（腰を当てずに読んだときの Hips を含む）：W_c = W_c(正準の親)（Hips は単位）。
- q = W_c(正準の親)⁻¹·W_c（Hips は W_c）。Hips の pos = (P の位置（PolyLing 空間）− レスト位置) ÷ h（h ≤ 1e-6 なら pos を書かない）。

**キーの決まり**

- 読込：骨のキーは、正準骨格にある `bakedBones` の全トラックのキー時刻を合わせたもの。駆動するボーンにはその全時刻にキーを打つ。表情はそのシェイプキーに当たるトラックのキー時刻を合わせたもの、視線は `lookAt` のキー時刻。フレーム = t × frameRate（t = 0 がフレーム 0）、補間は線形（四元数は成分ごとの線形。キーの上では元の値）。接線（`tan`）は使わず線形として読み、警告する。
- 読込の「シーンの fps と範囲を合わせる」（既定オン）：fps = frameRate（整数でなければ fps を切り上げ、fps_base = fps ÷ frameRate）、開始フレーム 0、終了フレーム = max(1, ⌈長さ × frameRate⌉)、長さ = max(`duration`, 全キー時刻の最大)。
- 書き出しの fps は、読み込んだアクションなら `polyling_frame_rate`、そうでなければシーンの fps ÷ fps_base。開始は `polyling_frame_start`（読み込んだものは 0）、そうでなければシーンの開始フレーム。
- トラックごとに、Blender のキーのフレーム（そのボーンの回転の F カーブ、Hips は位置も）が元のキー時刻と同じ数で 1e-3 フレーム以内、かつ元のキー時刻で作り直した値と元の値の差が許容差のうちなら、元のトラックをそのまま書く。許容差：回転は四元数の成分 2e-5（符号を揃えて）、Hips の位置 1e-5、表情 1e-6、視線 2e-5。
- そうでなければ frameRate の格子（t = 0, 1/fps, …）で標本化する。格子の終わり = (アーマチュア・表情を当てるシェイプキー・視線の空の全キーの最大フレーム − 開始) ÷ fps（キーが 1 つも無ければシーンの終了フレーム）、点の数 = round(終わり × fps) + 1。回転は隣のキーと符号を揃える。`preWrapMode`・`postWrapMode` は元のトラックから引き継ぐ。
- 書く `bakedBones` の並び：元のファイルの順。その後ろに、元に無く割り当てのあるボーンのうちキーのあるもの（Hips は常に）を正準骨格の順で足す。元にあって割り当ての無いトラックはそのまま書く。
- `bakedBones` を 1 本でも作り直したら、元の `muscles`・`body` は回転と合わないので書かず（`muscles` は空、`body` は無し。元にあれば警告）、`rotationSource` を `"bakedBones"` にする。PolyLing は読込で `bakedBones` からマッスル＋body を焼く。
- `duration` は元の値。元の無いとき・`bakedBones` を作り直したときは max(元の値, 格子の終わり)。
- 元の無いアクション（`polyling_motion` を持たない）は、空のクリップ（`format`・`version` 4・`name`＝アクション名（アクションが無ければアーマチュア名）・`frameRate`＝fps・`duration` 0・`space` "local"・`loop` false・空の `bones`・`expressions`・`muscles`・`bakedBones`）に `metadata` `{"createdWith":"PolyLing IO (Blender)"}`・`rotationSource` "bakedBones" を付けて書く。

**表情・視線の表し方**

- 表情の割り当ては PolyLing と同じ展開（`MotionClipApplier.FindExpression`・`ExpandToVertexMorphs`）。モデルの表情（取り込んだモデルの `morphExpressions`）を名前 → 英語名の順に完全一致で引き、頂点モーフ（type 1）なら `meshEntries`（無ければ `meshIndices` を重み 1）のモーフのシェイプキーへ「値 × 重み」を当てる。グループ（type 0）は `GroupChildren` を辿るが、保存データに `GroupChildren` は無いので何にも当たらない（PolyLing と同じ）。頂点モーフとグループ以外の種類も当てない。
- 表情の定義に無い名前は、同じ名前のシェイプキー（モデルのメッシュのどれでも）へ重み 1 で当てる。当たるものが無い表情は警告する。1 つのシェイプキーに複数の表情が当たれば値を足し、警告する。値がスライダーの範囲を外れれば範囲を広げる（±10 まで）。
- 読込のたびに、そのモデルのシェイプキーに付いていたアクションを外す（書き出しが今のクリップの表情だけを読むように）。
- 書き出し：元の表情トラックは、当たるシェイプキー（重みが 0 でない最初のもの）の値 ÷ 重み（キーの決まりは上と同じ。当たるシェイプキーが無ければ元のトラックのまま）。元に無くアクションで動かしたシェイプキーは、そのモーフだけを重み 1 で持つ頂点モーフの表情があればその名前、無ければシェイプキー名のトラックにする。新しく足すトラックは `name` と `keys` だけを書く（`provider`・`category` は書かない。PolyLing は provider で引かない）。
- 視線の空：位置 = Head のレスト位置 + `lookAtOffsetFromHeadBone`（無ければ 0）、回転 = C·R(q)·C·Rx(−90°)（空の矢印＝ローカル +Z が視線の向き）。目のボーンは視線で動かさない（目は `bakedBones` の LeftEye・RightEye）。
- 書き出し：視線の空に F カーブがあるときだけ書く。q = C·(R_空·Rx(+90°))·C。`lookAtOffsetFromHeadBone` = 空の位置（PolyLing 空間）− Head のレスト位置。元に無く 1e-5 以内なら書かず、元と 1e-5 以内なら元のまま。`lookAt` が無いファイルを読むと前の視線のアクションを外す（空は残る）。

**読めないもの**：検査（`format/plmotion.py` の `validate`）でエラーのファイル。`bakedBones` が無く `muscles` だけのファイル（マッスルから回転は作れないので、PolyLing で保存し直してから読む。C# が書くファイルには `bakedBones` が付く）。version 1〜3 は C# と同じく 4 へ引き上げる（`bakedBones` は捨てる）。

**書式**（`format/plmotion.py`。`MotionClipSerializer.ToJson` と同じ形）

- Newtonsoft の Indented（2 字下げ、`": "`）、null の欄は書かない（配列の要素の null は残す）、BOM なし UTF-8、改行 CRLF。書く前に検査し、エラーがあれば書かない。
- 欄の順は DTO の宣言順（クリップは format・version・name・frameRate・duration・space・loop・bones・expressions・muscles・body・bakedBones・lookAt・lookAtOffsetFromHeadBone・absentMuscles・rotationSource・metadata）。表情トラックは category・provider が先。知らない欄は既知の欄の後ろに元の順で書く（C# は読まないので捨てる）。
- DTO が初期値を持つ欄（format・version・frameRate・duration・space・loop・bones・expressions・muscles・bakedBones、トラックの targetKind・keys、キーの t・v）は無くても書く。version は常に 4。
- float の欄：float32 に丸め、G7 で戻ればその 10 進、だめなら G9（ちょうど半分は 0 から遠い側へ）。整数値には `.0` を付ける。0 と −0 は `0.0`。知らない欄の数は最短往復の 10 進。

### 5.2 Blender とモーションを行き来させる 2 つの道筋

| | 道筋 A：VRM Add-on 経由の `.vrma` | 道筋 B：PolyLing IO・PolyLing Bridge の `.plmotion.json` |
|---|---|---|
| Blender に要るもの | VRM Add-on for Blender と、VRM 1.0 のアーマチュア（VRM モデル） | PolyLing IO（§5.1・§7）。送受信は PolyLing Bridge も（§3.7） |
| PolyLing → Blender | `.plmotion` を `.vrma` に書く（左の「その他」→「モーション」の「VRMA 書き出し」、またはコマンド `convertMotionToVrma`）→ Blender の［ファイル > インポート > VRM Animation (.vrma)］ | `.plmotion.json` を［ファイル > インポート > PolyLing モーション (.plmotion.json)］で読む。またはブリッジの「取得」（PolyLing のモーションのパネルに読み込んであるクリップ） |
| Blender → PolyLing | ［ファイル > エクスポート > VRM Animation (.vrma)］→ PolyLing のモーションのパネルで種類「VRM アニメーション (.vrma)」で開く。`.plmotion` にするには「JSON保存」、またはコマンド `exportMotionJson`（sourceKind = Vrma） | ［ファイル > エクスポート > PolyLing モーション (.plmotion.json)］→ PolyLing のモーションのパネルで種類「PolyLing モーション」で開く。またはブリッジの「送る」（パネルに読み込まれて今のフレームが当たる） |
| 対象のモデル | VRM 1.0 のモデルだけ | PolyLing から取り込んだモデルと、割り当て表を作ったアーマチュア（§5.1） |
| 残るもの | Humanoid の回転・Hips の位置・モデルにある表情 | `bakedBones`・表情・視線は Blender で表す。`muscles`・`body`・`bones`（二次骨）・`metadata` などは丸ごと残して戻す（§5.1） |

道筋 A の制約（VRM Add-on のソース〔`importer/vrm_animation_importer.py`・`exporter/vrm_animation_exporter.py`・`importer/import_scene.py`・`exporter/export_scene.py`、2026-10-19 の main〕で確認。実測は `PolyLing_Blender連携計画.md` の段 ① と 6）：
- 読込・書き出しとも VRM 1.0 のアーマチュアが要る（VRM 0.x のモデルには使えない）。
- 長さとフレームレート：読込はシーンの fps で毎フレームにキーを打ち、シーンのフレーム範囲を変えない。書き出しはシーンの開始〜終了フレームを毎フレーム標本化する。シーンの fps と範囲をクリップに合わせてから書かないと、長さが変わり（既定は 1〜250 フレーム）、キーの時刻がシーンの fps の格子に打ち直される。
- 視線：読込は lookAt ノードの位置しか見ない。PolyLing は VRMA の仕様どおり視線を回転で書くので、道筋 A を通すと視線が消える。
- 表情：モデルにある表情だけを読み書きする（カスタムの表情は消える）。値は VRMA の仕様どおり 0〜1 に丸まる。
- ボーン：モデルに割り当てのあるボーンだけを書く（モデルに無い UpperChest などは消える）。目のボーンは回転を書かない。
- PolyLing の読込は回転からマッスルを作る（`GetHumanPose` と `CanonBoneMotion.RefineMuscles`）。マッスルが 2 本だけのボーン（前腕・すね）は、Blender がキーの間を打ち直した回転を表しきれないことがある（段 ① の実測で最大 2.4°）。

道筋 B は `.plmotion.json` の値をそのまま行き来させる（読んでそのまま書けばバイト一致、Blender で変えたトラックだけを作り直す。§5.1 のキーの決まり）。VRM モデルでなくても使え、PolyLing 由来のモデルなら割り当てを自動で作る。

---

## 6. MediaPipe の顔ファイル（`MediaPipe/MediaPipeFaceFile.cs`）

ランドマーク JSON（schema `mediapipe.face_landmarker`）：
```
{"schema":"mediapipe.face_landmarker","num_faces_detected":1,
 "faces":[{"face_index":0,"image":{"path":"…","width":W,"height":H},
           "landmarks":[{"index":0,"x":…,"y":…,"z":…,"pixel_x":…,"pixel_y":…}, …]}]}
```
- x, y, z はライブの landmarks と同じ正規化座標。
- 三角形 JSON：`{"triangle_count":n,"vertex_count":n,"triangles":[[i0,i1,i2,…], …]}`（3 点以上の多角形も可）。
- 注意：能面 JSON の書き出し（NohMaskMeshExporter）は任意メッシュの頂点位置をそのまま x, y, z に書く。そのファイルの x, y, z は正規化座標ではない。

---

## 7. Blender アドオン PolyLing IO（プロジェクトの読み書き）

正本は C# の読み書き（`ProjectSerializer.cs`・`FolderSerializer/` の CSV）。アドオンはその規則を写したもの。決定の経緯は `PolyLing_Blender入出力計画.md` 2.2〜2.4。

### 7.1 置き場所・入れ方・入口

- 置き場所 `データとツール/poly_ling_py/polyling_io/`（Blender ブリッジ `polyling_bridge.py` とは別のアドオン）。名前「PolyLing IO」、Blender 3.6 以上、分類 Import-Export。
  - `format/`：bpy を使わない層。`.mfproj`・CSV フォルダ ⇔ `ProjectDTO` と同じ形の辞書（JSON の欄名そのまま。CSV にしか無い情報は `_csv` で始まる欄）、`.plmotion.json` ⇔ `MotionClipDTO` と同じ形の辞書。知らない欄・行は捨てずに書き戻す。素の Python でも使える。
  - `blender/`：辞書 ⇔ Blender。
- 入れ方：`polyling_io` フォルダをフォルダごと zip にし、Blender の［編集 > プリファレンス > アドオン］で入れて有効にする（3.6〜4.1 は「インストール…」、4.2 以降は右上のメニューの「Install from Disk」）。`polyling_io` フォルダを Blender のアドオンのフォルダに置いてもよい。
- 入口：

| 場所 | 演算子 | 選べるもの |
|---|---|---|
| ［ファイル > インポート > PolyLing プロジェクト (.mfproj/.csv)］ | `import_scene.polyling_project` | `.mfproj`、CSV フォルダの `project.csv`（任意の名前）か `model.csv` |
| ［ファイル > エクスポート > PolyLing プロジェクト (.mfproj/.csv)］ | `export_scene.polyling_project` | 拡張子 `.mfproj` なら JSON、`.csv` なら CSV フォルダ（どちらでもなければ `.mfproj` を付ける）。「名前で参照（CSV）」（既定オフ。読んだ CSV が名前モードのモデルは常に名前） |
| ［ファイル > インポート > PolyLing モーション (.plmotion.json)］ | `import_anim.polyling_motion` | 「腰の回転と位置も当てる」（既定オン）・「脚も T ポーズに合わせる」（既定オフ）・「シーンの fps と範囲を合わせる」（既定オン） |
| ［ファイル > エクスポート > PolyLing モーション (.plmotion.json)］ | `export_anim.polyling_motion` | 名前が `.json` で終わらなければ `.plmotion.json` を付ける |
| 3D ビューのサイドバーのタブ［PolyLing］ | | 「モーションを読む」「モーションを書く」、「Humanoid の割り当て」の「自動で割り当て」「VRM アドオンから取り込む」と表（§5.1） |

- プロジェクトの書き出しは、選んだオブジェクトが入っている取り込み済みのプロジェクト（無ければ最初のもの）を書く。CSV は指定した名前のプロジェクトファイルと、同じフォルダにモデルごとのフォルダを作る。CSV のテクスチャは、読んだ CSV の `textures` を写す。`.mfproj` から読んだものは、マテリアルのパスで見つかる画像を写す。

### 7.2 Blender への置き方（取り込み）

| PolyLing | Blender | 残すもの（カスタムプロパティ） |
|---|---|---|
| プロジェクト | コレクション「PolyLing ＜名前＞」 | `polyling_project`（models 以外の欄の JSON）、`polyling_source`（読んだファイル） |
| モデル | その子のコレクション（モデル名） | `polyling_model`（meshDTOList 以外の欄の JSON）、`polyling_raw_meshes`（下の「表さない要素」の MeshDTO）、`polyling_mesh_count`、`polyling_model_index` |
| Bone・Morph・Group・Helper・RigidBody・RigidBodyJoint・WorkAxis 以外の種類（Mesh・BakedMirror・MirrorSide など）で頂点か面を持つもの | メッシュのオブジェクト | `polyling_mesh`（vertices・faces を除いた MeshDTO）ほか下の控え |
| Group・Helper、頂点も面も無いメッシュ | 空オブジェクト | `polyling_mesh`（MeshDTO 丸ごと） |
| ボーン（Bone） | モデルごとに 1 つのアーマチュア「＜モデル名＞_Armature」のボーン | `polyling_mesh`（丸ごと）、`polyling_local_rotation`（元のローカル回転）、`polyling_rest_matrix`（取り込み時のレスト） |
| モーフ（Morph） | 親メッシュのシェイプキー（親は `MorphPreviewState.FindBaseMeshIndex` と同じ規則。見つからなければ独立したメッシュ） | 親の `polyling_morphs`（シェイプキー名・索引・vertices と faces を除いた MeshDTO・控え） |
| マテリアル | Principled BSDF のマテリアル（使う番号だけをスロットに） | `polyling_material_index`、`polyling_material`（MaterialReferenceDTO の JSON）。オブジェクトの `polyling_slot_mi`（スロット → 番号） |
| RigidBody・RigidBodyJoint・WorkAxis | 表さない | モデルの `polyling_raw_meshes` |

- どのオブジェクト・ボーンも `polyling_index`（MeshContextList の索引）と `polyling_object_id` を持つ。オブジェクトは `polyling_model_index` も。`isVisible` が偽なら隠す。
- 頂点：位置、頂点 ID・サブ ID・部品 ID・フラグは点の属性 `pl_vertex_id`・`pl_vertex_sid`・`pl_vertex_pid`・`pl_vertex_flags`（振り直さない）。ウェイトは頂点グループ（名前はボーン名。ボーンでない要素を指す番号はその要素の名前）。元の `bw` は `pl_bw_index`・`pl_bw_weight`、ミラー側ウェイト `mbw` は `pl_mirror_bone_index`・`pl_mirror_bone_weight`、取り込み時の位置は `pl_co`。
- 面：3 頂点以上の面はポリゴン（頂点の並びは逆）。2 頂点の面は辺（`pl_line_face_index` に元の面番号）。作れない面は `polyling_unbuilt_faces`。面の属性 `pl_face_index`（元の面番号）・`pl_face_id`・`pl_face_flags`。
- UV・法線：面の角ごと（頂点の複数の UV・法線を面の `uvi`・`ni` で選ぶ）。UV はレイヤー「UVMap」、法線はカスタム法線。元の `uvi`・`ni` は角の属性 `pl_uvi`・`pl_ni`、元の法線は `pl_normal`（PolyLing 座標）。
- その他の控え：制御点 `polyling_control_points`、2 頂点の面 `polyling_line_faces`。取り込みの直後に書き出しと同じ読み方で読み直し、戻らない頂点の欄・面だけを `polyling_vertex_extra`・`polyling_face_extra` に残す。頂点数・面のつながりの指紋 `polyling_topology`（頂点数、面の頂点の並びと元の面番号、線分の辺）。
- Blender で表さないもの（IK・剛体・揺れもの・VRM 設定・表情の定義・選択セット・オブジェクトグループ・T ポーズの控えなど）は `polyling_model`・`polyling_mesh` の JSON のまま。

### 7.3 空間と姿勢

- 座標：Blender (x, y, z) ⇔ PolyLing (x, z, y)。行列は `M_bl = C·M_pl·C`（C は y と z の入れ替え。自分自身が逆）。
- PolyLing のローカル行列は TRS(position, Quaternion.Euler(rotation), scale)（`BoneTransform.cs` の `TransformMatrix`）。bindPose は 16 値・行優先。
- MeshFilter 系（skinKind 0）と空：頂点はローカル。`matrix_basis` = C·ローカル·C、親とのずれは `matrix_parent_inverse` で吸収し、ワールドを PolyLing の WorldMatrix（生成ミラー側は S·H·S）に合わせる。
- Skinned 系（skinKind 1。skinKind が無ければウェイトを持つ頂点があるもの）：頂点はバインド（ワールド）空間のまま。単位行列でアーマチュアの子にし、Armature 修飾子を付ける。
- ボーン：レストは保存された bindPose の逆（null は単位）。向きは子の頭の平均へ（見た目だけ）。WorldMatrix·bindPose が単位でないボーンは、ポーズ行列 = C·(WorldMatrix·bindPose)·C·レスト になるようにポーズを付ける（PolyLing の SkinningMatrix = WorldMatrix·BindPose、`UnifiedBufferManager_Update.cs`）。

### 7.4 書き出し（Blender → `.mfproj`・CSV）

**元の値を使う決まり**

- Blender に表れない欄は保持した JSON の値。
- Blender に表れる欄（頂点・面・姿勢・ボーン・ウェイト・シェイプキー）は Blender から作り、作った値が元の値で作ったものと許容差のうちなら元の値を使う。許容差は行列の要素ごとに |差| ≤ 1e-4 × max(1, |元|)、ボーンのレストは 1e-5。
- float は float32 にしてから、C# の書き方（`.mfproj` は G7 で戻れば G7・だめなら G9、CSV は G9）と同じ 10 進になる値にする。

**Blender から作るもの**

| 欄 | 作り方 |
|---|---|
| 頂点の位置 | 頂点の座標（PolyLing 座標へ）。Skinned 系のオブジェクトがアーマチュアに対して動いていれば頂点に焼き込み、警告 |
| 頂点の ID・フラグ、面の ID・フラグ | 属性 `pl_vertex_*`・`pl_face_id`・`pl_face_flags` |
| 面 | ポリゴン（並びを逆に戻す）と線分の辺。面の順は元の面番号の順（新しい面は後ろ） |
| 面の材質 `mi` | スロットのマテリアルの `polyling_material_index`。PolyLing 由来でないマテリアルは取り込み時のそのスロットの番号、それも無いスロットは 0（警告） |
| UV | レイヤー「UVMap」の角の値。頂点ごとの並びは `pl_uvi` の番号で元へ戻す（番号の食い違い・欠けがあれば出てきた順に振り直す） |
| 法線 | 角の `pl_normal`。ただし「動いた頂点」（位置が `pl_co` と違う）と、それと面を共有する頂点の角は Blender が計算した法線（警告）。並びは `pl_ni` で元へ戻す |
| ウェイト `bw` | 頂点グループの中身が元の `bw` と同じなら元の `bw`。違えば頂点グループから（重みの大きい順に 4 つ、4 つを超えたら 4 つで正規化）。ボーンに当たらない頂点グループは書かない（警告） |
| モーフ | シェイプキーの差分（基準キーとの差）を basePositions に足した位置。ほかの欄は親の頂点の写し＋控え |
| 名前 | オブジェクト・ボーンの名前（Blender が付けた「.001」などは元の名前とみなす） |
| 表示 | `hide_viewport` → `isVisible` |
| MeshFilter 系・空の姿勢 | ローカル = C·`matrix_basis`·C。変わっていれば `exportSettingsDTO` の position・rotation（度）・scale を書き換え、useLocalTransform を真にする（警告） |
| ボーン | レストが取り込み時と違えば bindPose を作り直す（取り込みで向きを子へ向けた分を戻す）。ポーズから WorldMatrix（ポーズ行列 = C·(WorldMatrix·bindPose)·C·レスト）を戻し、ローカル = hierarchy[親]⁻¹·WorldMatrix |

**頂点数・面のつながりが変わったメッシュ**（`polyling_topology` と違うもの）

- 外して警告するもの：選択（頂点・辺・面・線分）・選択セット・法線除外セット・線分群・制御点・作れなかった面・頂点と面の控え・モデルの T ポーズの頂点控えのうちそのメッシュの分。
- モーフは要素を残し（番号がずれないように）、保持した基準（basePositions・baseNormals・baseUVs・頂点の控え）を外して、今の親の形とシェイプキーの差分で作り直す（警告）。

**書き出さないもの**

- PolyLing 由来でないオブジェクト・ボーン（警告）。
- 親子の付け替え（`parentIndex` は保持した値）。
- シェイプキーの名前の変更（モーフの名前は保持した値。名前を変えたシェイプキーは見つからず、そのモーフは差分 0 で書き、警告する）。
- マテリアル自身の設定の変更（`materialReferences` は保持した値。面に当てるマテリアルの付け替えは書く）。
- カスタム法線だけの編集。
- 生成ミラー側だけの移動（PolyLing では実体側から決まる。`ModelContext.MeshList.cs` の `SyncDerivedMirrorTransforms`。警告）。
- Blender で消した要素は、番号がずれないように空の要素で埋めて警告する。

---

## 8. 未確定・未対応

- ライブ・クリップとも Root（rootT / rootQ、body）はモデルへ当てていない（VRMA 書き出しは当てる）。
- MediaPipe の leftHand / rightHand の左右（4.2）。
- メッシュ・モデル本体の契約（3.6 の配列の受け渡しと 7 の Blender アドオン以外）。
