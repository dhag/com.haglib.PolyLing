# PolyLing 残件一覧

最終更新: 2026-08-28（2-3 実装済。2-1 / 2-2 は実装済。2-0 / 6-1 / 6-2 / 11 を新設）

このファイルは、調査の過程で見つかったが未着手・保留にした項目をまとめたもの。
新たに残件が出たら末尾の分類に追記する。

---

## 0. 追記ルール

- 1 項目につき **場所（file:line）／症状／確認済みの事実／未確認の点** を書く
- 「未確認の点」を空欄にしない。断定できないことは断定できないと書く
- 対処したら削除せず「済」に変更し、対処内容と日付を残す

---

## 1. 最優先（1-1 / 1-2 は解決済。1-3 / 1-4 が未解決）

### 1-1. Player ビルドのクラッシュ 【原因特定・回避済】 2026-08-27

**症状**
モデルを読み込んだ状態でカメラ操作を繰り返すと、数十秒後にネイティブクラッシュする。

**原因（クラッシュダンプで確定）**

Unity の D3D12 実装の不具合。**PolyLing のコードではない。**

```
UnityPlayer.dll!D3D12ScratchAllocator::DestroyScratch(int)          ← アクセス違反
UnityPlayer.dll!D3D12ScratchAllocator::ReleaseExcessScratch(uint,uint)
UnityPlayer.dll!D3D12ScratchAllocator::ReclaimMemory(bool)
UnityPlayer.dll!GfxDeviceD3D12::QueueExecute(...)
UnityPlayer.dll!GfxDeviceD3D12::FlushInternal(bool)
UnityPlayer.dll!GfxDeviceD3D12::EndCurrentRenderSubPass()
UnityPlayer.dll!GfxDeviceD3D12::EndRenderPassImpl()
UnityPlayer.dll!GfxDevice::EndRenderPass()
UnityPlayer.dll!GfxDeviceWorker::RunCommand(ThreadedStreamBuffer&)
UnityPlayer.dll!GfxDeviceWorker::RunGfxDeviceWorker(void*)          ← レンダースレッド
UnityPlayer.dll!Thread::RunThreadWrapper(void*)
```

| 項目 | 値 |
|---|---|
| 例外 | `0xC0000005` ACCESS_VIOLATION（読み取り） |
| 参照アドレス | `0x0000000000005DBD`（null 近傍 + オフセット 24,013） |
| 発生コード | `UnityPlayer.dll + 0x95B0A7` |
| 発生スレッド | `GfxDeviceWorker`（レンダースレッド。メインスレッドではない） |

**機構**

メインスレッドがフレーム途中でコマンドキューのフラッシュを起こすと、
レンダースレッド側の D3D12 一時メモリ（スクラッチ）回収処理が破綻する。

PolyLing が行っているのは「フラッシュを起こすこと」だけ。
`ComputeBuffer.GetData` はフラッシュ＋GPU 完了待ちを行うため、
ホバー判定・表示用カリングのたびにフレーム途中のフラッシュが発生していた。

**切り分けの経過（すべて実測）**

| 検証 | 結果 |
|---|---|
| `getdata=0`（GetData を全停止） | 落ちない |
| `getdata=0 flush=1`（GL.Flush のみ、待たない） | **落ちる** → 待機ではなくフラッシュが引き金 |
| `getdata=0 flush=2`（フレーム開始時に 1 回だけ） | 落ちる（頻度は下がる） → 1 回でも落ちる |
| 読み戻し回数の削減 / カメラ台数削減 / RenderMesh 置換 / ドライバ更新 | いずれも頻度が下がるだけ |
| **グラフィックス API を D3D11 に変更** | **落ちない** |

**回避策（適用済み）**

`Edit > Project Settings > Player > Other Settings`

1. `Auto Graphics API for Windows` のチェックを外す
2. `Graphics APIs for Windows` に `Direct3D11` を追加
3. `Direct3D11` を `Direct3D12` より上へ移動

`D3D12ScratchAllocator` が 1 度も実行されなくなる。

**未確認の点**

- Unity のバージョンを上げれば直るか（Unity 6000.3.12f1 で発生）
- D3D12 のまま回避する方法があるか
- Unity 側へのバグ報告の要否
- プロセスに注入されているトレンドマイクロの DLL
  （`tmmon64.dll` / `TmUmEvt64.dll` / `TmAMSIProvider64.dll`）が
  関与しているか。未検証

**副作用の確認が必要**

D3D11 に切り替えたことで、Compute シェーダー・GPU バッファ・描画に
差異が出ないかを確認すること。

---

### 1-2. エディタで時々ものすごく遅くなる（当初の相談内容）【解決・回避済】 2026-08-27

**症状**
Update が数秒に 1 度しか呼ばれなくなる。CPU 4%（≒1 コア分）、GPU 2%、
ディスク 0%、メモリ余裕あり。タスクマネージャの待機チェーンは
「ネットワーク I/O の終了を待機」と表示。
Unity エディタの再起動で改善することもあれば、PC 再起動が必要なこともあった。

**原因**
**1-1 と同一。** Unity の D3D12 実装の不具合
（`D3D12ScratchAllocator::DestroyScratch` でのアクセス違反）。

**確認の経過**

| 検証 | 結果 |
|---|---|
| `cull=0 hit=0`（フラッシュを起こす 2 経路を停止） | エディタでも軽快に動く |
| **グラフィックス API を D3D11 に変更** | **解消。快調** |

Player 側と同じ切り替えでエディタ側も解消した。
これにより、当初の相談内容と Player クラッシュが同一原因であることが確定した。

**回避策（適用済み）**
Player 側と同じ。`Project Settings > Player > Other Settings` で
`Auto Graphics API for Windows` を外し、`Direct3D11` を `Direct3D12` より上へ。

エディタが Player Settings に従わない場合は、起動オプションに
`-force-d3d11` を付ける。

**残っていた疑問（解消済み）**

- 待機チェーンが「ネットワーク I/O」と表示した理由は未解明のまま。
  ただしレンダースレッドの待機がそう分類されたものと整合する。
  実害が消えたため追わない。

---

### 1-3. 解決済み　　　ミラーの頂点移動が反映されない

**症状**
頂点を移動してもミラー側のメッシュに反映されない。

**確認済みの事実**

- `readback: false` の差し戻し後も再現するため、その変更が原因ではない
- 診断スイッチはすべて通常動作（`xform=0 rebuild=1`）
- `G15`（`GetSkinnedMirrorPositions` → `_skinnedMirrorPositionBuffer.GetData`）が
  セッション中 **0 回**。ミラー頂点のワールド座標が CPU に戻っていない
- `WritebackTransformedVertices` は `_expandedPositions`（G14）だけを使って
  `ctx.UnityMesh` を更新しており、ミラー側バッファを参照していない

**場所**
`UnifiedBufferManager_Mirror.cs:226`（`GetSkinnedMirrorPositions`）
`UnifiedSystemAdapter.cs:507-`（`WritebackTransformedVertices`）

**未確認の点**

- `GetSkinnedMirrorPositions()` の呼び出し元が存在するのか、条件を満たさないのか
- `WritebackTransformedVertices` のループが `MeshType.BakedMirror` / `MirrorSide` を
  どう扱っているか
- `_expandedPositions` にミラー頂点が含まれる設計か（`UnifiedBufferManager_Build.cs` 要確認）

----
残件：非対称の場合どうするか決めてない。現在厳密にミラー


---

### 1-4. 解決済：不可視データのワイヤだけが表示される
**症状**
MQO から読んだ不可視データのワイヤだけが表示される。2026-08-27 に発生。

**確認済みの事実**

- 空メッシュガード（`MeshSceneRenderer.cs` 3 か所）は条件を**追加**しただけなので、
  描画を増やすことはあり得ない
- `Graphics.DrawMesh` → `RenderMesh` 置換は差し戻し済み

**未確認の点**
原因未特定。差し戻し後に再現するかを確認していない。
ワイヤ側（`UnifiedRenderer` の `MeshInfo` ベースの構築）が
`IsVisible` 相当のフラグをどう見ているかを未調査。

---

## 2. 書き戻し（Writeback）の非効率と欠陥（2-1 / 2-2 / 2-3 実装済）

### 2-0. 【前提・混同禁止】頂点バッファは 2 種類ある  2026-08-28

2-1 / 2-2 を読む前に必ずここを読むこと。
**「孤立頂点は GPU に載せるのが必須」という既出の指摘は下表の A の話であり、
B とは別物である。** A と B を混同すると議論が噛み合わない。

| | A: 基本頂点バッファ | B: UV 展開バッファ |
|---|---|---|
| 実体 | `_positions` / `_worldPositions` / `_worldPositionBuffer` | `_expandedPositionBuffer` / `_expandedToOriginalBuffer` |
| インデックス空間 | グローバル頂点 index（`MeshObject.Vertices` と 1:1） | 展開 index（`(頂点, UV スロット)` 単位） |
| 孤立頂点 | **必須。含める** | 含めるべきでない（下記） |
| 載せる条件 | `ShouldIncludeInBuffers`（`UnifiedBufferManager_Build.cs:61`） | `BuildExpandedVertexMapping`（`同:986`） |
| 消費者 | 点描画・ワイヤ（`UnifiedRenderer.cs:338,467,650,758,808` の `GetDisplayPositions`）、スクリーン座標、ホバー判定、矩形／投げ縄選択、法線描画 | `GetExpandedPositions`（`UnifiedBufferManager_Update.cs:2030`）**1 か所のみ** |

**A に孤立頂点が必須である根拠**（コード内に明記済み）
`UnifiedBufferManager_Build.cs:49-55`

> 面を持たないメッシュの頂点は点として描かれ、選択もできる。ここで落とすと
> 孤立点だけのオブジェクトや、頂点を置いた直後でまだ面の無いオブジェクトが
> 画面から消えて編集不能になる。落とすのは頂点が 1 つも無いものだけ。

→ **A は今後も一切変更しない。**

**B から孤立頂点を外すべき根拠（網羅確認済み 2026-08-28）**

B の書き戻し先は `ctx.UnityMesh`。その `UnityMesh` を作る `ToUnityMesh` は
孤立頂点を持たない。展開 index 空間を扱う実装は全部で 9 か所あり、
**B だけが孤立頂点を含める唯一の例外**になっている。

| # | 場所 | 孤立頂点 |
|---|---|---|
| 1 | `MeshBridgeDefault.ToUnityMesh`　`:71-76` | 除外 |
| 2 | `MeshBridgeDefault.ToUnityMeshShared`　`:317-322` | 除外 |
| 3 | `MeshBridgeDefault.ApplyTrianglesInPlace`　`:752-758` | 除外 |
| 4 | `MeshObject.BuildExpansionMap`　`:2254-2260` | 除外 |
| 5 | `MeshObject.BuildInverseExpansionMap`　`:2285-2291` | 除外 |
| 6 | `PMXExporter.AppendExpandedVertices`　`:817-827` | 除外 |
| 7 | `PMXHelper.AppendExpandedVertices`　`:766` | 除外（ただし 6-1 の欠陥あり） |
| 8 | `PlayerViewportManager.UpdateExpandedUnityMesh`　`:2440-2445` | 除外（ただし規則が違った。**済** 2026-08-28） |
| 9 | **`UnifiedBufferManager_Build.BuildExpandedVertexMapping`　`:991-1039`** | **含めていた ← 唯一の例外。済 2026-08-28** |

**【済】2026-08-28**
上記 9 か所すべてを `MeshExpansion`（`Runtime/Poly_Ling_Main/Core/MeshBridge/MeshExpansion.cs`）
1 本に統一した（#6 `PMXExporter` は別実装のまま。規則だけ一致している）。
展開順序を新たに手書きしないこと。

**B の影響範囲（全文検索で確認。取りこぼし無し）**

- `.shader` / `.hlsl` / `.cginc` 全 18 ファイル中、`Expanded` の参照は **0 件**
- `.compute` は `ExpandVertices` カーネル（`UnifiedCompute.compute:1029-1049`）のみ。
  `_ExpandedToOriginalBuffer` を介した gather なので **シェーダー変更は不要**
- 公開プロパティ `ExpandedPositionBuffer` / `ExpandedNormalBuffer`
  （`UnifiedBufferManager.cs:332-333`）は呼出元 **0 件**
- `_expandedNormalBuffer` は書き込みのみで読み手が **0 件**
- `Poly_Ling_Remote` / `Poly_Ling_ListClient` / `Core/Serialization` に `Expanded` は **0 件**
- `TotalExpandedVertexCount` を数値として読むのは
  `UnifiedSystemAdapter.cs:523`（0 判定）、
  `UnifiedBufferManager_Update.cs:1934,1968,1972,2032-2041`（Dispatch と読み戻し）のみ

**B を変更する際に必ず直すもの【済】 2026-08-28**

`MeshSceneRenderer.cs:775` のコメント

> 線分の総数は `UnifiedBufferManager.TotalExpandedVertexCount` と一致する

法線描画の実体（`PrepareNormals`）は `mo.Vertices` を全走査しており、
孤立頂点の法線も描く。B から孤立頂点を外したのでこの記述は**偽になった**。
コード自体は `TotalExpandedVertexCount` を読んでいないので動作に影響は無いが、
放置すると次に読む人が誤るため「一致しない」と書き換えた。

---

### 2-1. 意図的に残されたリーク 【済】 2026-08-28

**場所**
`UnifiedSystemAdapter.cs:637-642`、`:703-704`

```
// 【ここでは破棄しない】
// ここで旧 Mesh を破棄すると矩形選択が一部頂点で効かなくなる不具合が出た。
// リークは残るが、破棄は個別操作の経路 (ReplaceUnityMesh) に限定する。
ctx.UnityMesh = regenerated;
```

**問題**
リークを容認する対処は不可。

**原因【確定】 2026-08-28**

2-2 と**同一原因**。2-0 の表の #9 が孤立頂点を含めるため、
孤立頂点を持つメッシュでは `unityMesh.vertexCount == expandedVertexCount`
（`UnifiedSystemAdapter.cs:606`）が必ず偽になり、毎回 else 分岐へ落ちて
`ToUnityMesh()` が新しい `Mesh` を作る（`MeshBridgeDefault.cs:46`）。
それを `:650` で直接代入するので、**呼ばれるたびに 1 個ずつ漏れる。**

**頻度は「毎ドラッグフレーム」**

`PlayerViewportManager.cs:855`（`VerticesMovedPhase.Dragging`, `syncMc != null`）
→ `:2394` `UpdateTransform()` → `WritebackTransformedVertices()`。
頂点ドラッグ中は毎フレームこの経路を通る。

**確実に該当する既知のデータ**

- `ObjImporter.cs:317-324` — 面も線も無い OBJ（点群）は全頂点を孤立頂点として取り込む
- 補助線（2 頂点面）だけで構成されたメッシュ
  （`ToUnityMesh` は `face.VertexCount < 3` を `nonIsolatedVerts` に入れない）

**確認済みの事実**
`MeshContext.cs:44-51` にも同じ経緯が記載されている。
2026-08-27 の計測セッションでは条件を満たさず、この経路は発動していなかった
（`mesh=152` が一定だった）。つまり **そのモデルに孤立頂点が無かっただけ**で、
経路が安全だったわけではない。

**「破棄すると矩形選択が効かなくなる」について**

矩形選択・投げ縄選択のコード（`MoveToolHandler.cs:1664-1783` /
`SelectionOperations.cs`）に `UnityMesh` の出現は **0 件**。
スクリーン座標の GPU 読み戻しと `IsVertexVisible` しか使っていない。
`ctx.UnityMesh` を破棄しても選択結果に届く経路はコード上に存在しない。
※ 当時の症状の因果は**未確認**。上記は「選択コードが `UnityMesh` を
参照していない」という事実のみ。

**対処【済】 2026-08-28**

1. 発生源を断った。2-0 の #9 を `MeshExpansion` に合わせ、孤立頂点を
   GPU 展開バッファから除外した。孤立頂点だけが原因だった不一致は消える。
2. 書き戻しが展開頂点数を自前で数えるのをやめた。メッシュごとの
   展開開始位置・頂点数は `BuildExpandedVertexMapping` が記録し、
   書き戻しは `UnifiedBufferManager.TryGetExpandedRange` で読むだけにした。
   数える主体が 1 つになったので、数え方が割れて別メッシュを書くことは無くなる。
3. リークを止めた。`ctx.UnityMesh = regenerated` の直接代入をやめ、
   `MeshContext.ReplaceUnityMeshDeferred` を通す。
4. 同一フレーム内破棄をやめた。旧 `Mesh` は `MeshContext` の**退避キュー**へ積み、
   次の 3 地点でまとめて破棄する。
   - `WritebackTransformedVertices` の先頭
   - `MeshSceneRenderer.RebuildAdapter` の入口
   - `UnifiedSystemAdapter.Dispose`

   `Graphics.DrawMesh` は「そのフレームの描画に使う」提出であり、実際に読まれるのは
   レンダースレッドがコマンドを処理するとき。提出後・描画前に破棄すると
   レンダラーから見て解放済みオブジェクトの参照になる。1 フレーム遅らせて回避する。

**未確認の点**

- 当時「矩形選択が一部頂点で効かなくなった」実際の因果
  （選択コードが `UnityMesh` を参照していないことは確認済みだが、
  破棄が症状を引き起こした経路そのものは特定していない）
- 上記 4 の退避キューが実機で症状を再発させないか。**動作確認は未実施**

---

### 2-2. `WritebackTransformedVertices` の早期スキップ 【済】 2026-08-28

**場所**
`UnifiedSystemAdapter.cs:692-693`

```csharp
if (meshObject.VertexCount != vertexCount)
    continue;
```

同 `:622-623`

```csharp
if (regenerated != null && regenerated.vertexCount == expandedVertexCount)
```

**問題**
条件を満たさない場合、`UnityMesh` が古いまま残る、または頂点を書き戻さないまま
`ctx.UnityMesh` に代入される。

**原因【確定】 2026-08-28**

2-1 と同一。2-0 の #9 と #1 で展開頂点の数え方が違うため、
孤立頂点を持つメッシュでは `regenerated.vertexCount < expandedVertexCount` が
**必ず**成立し、`:631` が偽になる。
その結果 `SetVertices` を通らず、`MeshObject.Vertices` の**ローカル座標のまま**の
`Mesh` が `:650` で代入される。

描画は `MeshSceneRenderer.cs:504` の
`Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, cam, sub)` なので、
ワールド変換が適用されないまま原点基準で描かれる。
これが「でたらめなデータ」の正体。**可能性ではなく確定。**

**フォールバックを 1 度でも通ると恒久化する**

`UnifiedSystemAdapter.cs:712` は `ToUnityMesh(xform)` を使う。
この実装（`MeshBridgeDefault.cs:201-230`）は**面駆動**で
`(頂点, UV スロット, 法線スロット)` の組で名寄せする**別順序**であり、
`ToUnityMesh()`（頂点順 → UV 順）とは頂点数も並びも一致しない。
一度これで作られた `UnityMesh` は、以後 `:606` の一致判定も永久に偽になり、
2-1 のリークと本項の座標未反映が恒久化する。

`HierarchyExportWindow.cs:1694-1700` のコメントにも
「UnifiedSystemAdapter が `ToUnityMesh(xform)` で作る」と記載があり、
この経路の存在自体は既知だった。

**対処【済】 2026-08-28**

1. 2-1 の 1・2 と同じ。展開 index 空間を一致させ、権威値を 1 つにした。
2. 不一致を握り潰すのをやめた。`UnityMesh` が無い／位相が食い違う場合は
   **(A) 作り直す**。`ToUnityMesh()` で再生成し、展開ワールド座標を書き込んでから
   `ReplaceUnityMeshDeferred` で差し替える。
3. 再生成しても頂点数が合わない場合は、その `Mesh` を表に出さずその場で破棄し、
   メッシュ名・両方の頂点数つきで 1 回だけ警告する
   （`WarnWritebackOnce`。毎フレーム走る経路なので同一内容は 1 回のみ）。
   この警告が出たら呼び出し側の再構築漏れ。握り潰さず原因を直すこと。
4. フォールバック（`WritebackTransformedVerticesFallback`）が
   `ToUnityMesh(xform)` で**別順序**の `Mesh` を作るのをやめた。
   `ToUnityMesh()` で作り、`ApplyTransformToMeshVertices` で座標だけ変換する。
   これで「一度フォールバックを通ると恒久的に不一致になる」経路が消える。

**(B) スキップして警告のみ を選ばなかった理由**

位相 Undo / Redo は `MeshObjectSnapshot.ApplyTo`（`MeshObjectSnapshot.cs:177`）が
`MeshObject` を差し替えるだけで `UnityMesh` を触らない。`UnityMesh` を作り直すのは
`PlayerViewportManager.RebuildSelectedUnityMeshes`（`:595`）だが対象は
`SelectedDrawableMeshIndices` に入っているメッシュだけ。
非選択メッシュはこの再生成分岐が唯一の復旧経路であり、
(B) にすると位相 Undo で表示が古いまま固まる。

将来 (B) へ変えるなら、先に Undo レコード側へ `UnityMesh` 再構築の責務を持たせること
（`MultiMeshTopologySnapshotRecord.cs` の冒頭コメントに明記した）。

**未確認の点**
- 実データで警告が何件出るか。**動作確認は未実施**

---

### 2-3. `UnityMesh` への直接代入 【済】 2026-08-28

**確認済みの事実**

- `MeshContext.cs:53` で `DestroyReplacedUnityMesh = true`
- `MeshContext.cs:127-139` に危険性が自分のコードで明記されている
  > 直接代入すると旧 Mesh が到達不能なまま常駐し、Undo / ミラー再構築 / 変換の
  > たびに積み上がる（長時間の編集で目に見えて重くなる原因）

**全数調査の結果（2026-08-28）**

`UnityMesh` への代入は全 45 行
（`MeshContext.cs` の API 実装 2 行・同名ローカル変数 4 行を除いた実数）。
「41 か所」は旧記述で、根拠が残っていなかったため数え直した。
分類は「旧 Mesh が非 null の状態に別インスタンスを代入しているか」で行う。

| 分類 | 件数 | 内容 |
|---|---|---|
| A | 31 | 新規 `MeshContext` の初期化子・生成直後。旧 Mesh が存在しないので漏れない |
| B | 5 | 直前に `UnityEngine.Object.Destroy` している。play mode では漏れないが edit mode では破棄されない（5-1 と同一） |
| C | 7 | 既存 `MeshContext` へ旧 Mesh を残したまま代入。**確実に漏れる** |
| D | 1 | `MeshUndoContext.cs:109` の setter。呼出元が全て同一インスタンスを渡すため漏れない |

**A（漏れない。変更しない）31 か所**

`HierarchyImportWindow.cs:622,766` / `PolyLingCore_Commands.cs:886` /
`PolyLingCore_UndoOperations.cs:182,471` / `PolyLingCore_UvHandlers.cs:77` /
`MeshFilterToSkinnedConverter.cs:523` / `MeshListOps.cs:188` / `MirrorBranchOps.cs:753` /
`QuadDecimatorOperation.cs:40` / `CsvMeshSerializer.cs:1009` /
`ModelSerializer.cs:504,754` / `ModelSerializer_Selection.cs:202` /
`MQOImporter.cs:1425` / `ObjImporter.cs:432` / `PMXImporter.cs:1373,2221,2345` /
`ObjectArrayInserter.cs:257` / `ObjectPoseWedgeInserter.cs:119` /
`ShrinkOperation.cs:201` / `BlendOperation.cs:172` /
`PlayerCommandDispatcher.cs:225,2058,2116,3928` /
`PolyLingPlayerViewerCore.cs:3519,7324` /
`PlayerMediaPipeFaceDeformSubPanel.cs:202` / `LineExtrudeToolHandler.cs:111`

※ 旧記述が「主な箇所」に挙げていた `MirrorBranchOps.cs:753` はここ。
直前の `new MeshContext`（`:717`）に対する代入で旧 Mesh は無い。旧記述の誤り。

**C：確実に漏れていた 7 か所【済】 → `ReplaceUnityMesh`**

| 場所 | 対象 | 漏れる根拠 |
|---|---|---|
| `PolyLingCore_Commands.cs:500` | `destCtx` | `CreateNewMesh=false` のとき `destCtx = baseCtx`（既存） |
| `PlayerCommandDispatcher.cs:1172` | `remMc` | 条件が `remMc?.UnityMesh != null`。旧 Mesh は必ず非 null |
| `:1204` | `matMc` | `model` から取得した既存 |
| `:1242` | `lscmMc` | 同上 |
| `:1266` | `mc` | `PolyLingCore_UvHandlers.cs:77` が初期化子で既に `ToUnityMesh()` 済み。**二重生成**だった |
| `:2580` | `destCtx` | `CreateNewMesh=false` のとき `destCtx = baseCtx` |
| `:5389` | `mc` | `SyncMeshContextAfterMirrorEdit`。呼出元 `:2219`(`srcMc`) / `:2293`(`ubMc`) はどちらも既存 |

`ReplaceUnityMeshDeferred` を使わなかった理由：
`MeshSceneRenderer.RebuildAdapter` は入口（`:294`）で退避キューをフラッシュする。
上記 7 か所はいずれも直後に `EnterTopologyChanged` → `RebuildAdapter` へ入るため、
Deferred にしても同一フレームで破棄され即時と変わらない。
下表の「個別操作＝`ReplaceUnityMesh`」に揃えた。

`:1266` だけは差し替えではなく**行の削除**。生成主体を `HandleUvToXyz` 側 1 つに寄せた。
エディタ側の呼出元（`PolyLingCore_Commands.cs:268` → `AddMeshContextWithUndo`）は
元から作り直していないので、これで両経路の挙動が揃う。

**B：`Object.Destroy` 直呼び 5 か所【済】 → `ReplaceUnityMesh`**

| 場所 | 変更前 |
|---|---|
| `PolyLingPlayerViewerCore.cs:6554-6555` | `if (…!= null) Object.Destroy(…); targetMc.UnityMesh = newUnityMesh;` |
| `:6833-6834` | 同上 |
| `:7465-7467` | 同上（3 行） |
| `RemoteProjectReceiver.cs:164-177` | 事前破棄ブロック → 末尾で `mc.ReplaceUnityMesh(rebuilt)` に集約。`mesh == null` / 頂点 0 のときは `null` を渡し旧 Mesh だけ破棄 |
| `:212-216` | `mc.ReplaceUnityMesh(null)` |

`MeshContext.DestroyMesh` が `Application.isPlaying` で
`Destroy` / `DestroyImmediate` を使い分けるため、edit mode でも解放される。
`PLResStat.LiveMesh` の減算も入る。

**D：`MeshUndoContext.cs:109`（変更しない）**

`TargetMesh` の setter が `ResolvedMeshContext.UnityMesh` へ書き込む。
呼出元（`MeshUndoController.SetMeshObject` / `SetMeshObjectFor`）は全て
`ctx.UnityMesh` を渡しており、同一インスタンスなので漏れない。
`ResolvedMeshContext` が対象と食い違うと別コンテキストの Mesh を潰す問題は
`MeshUndoContext.cs:60-69`（`ExplicitMeshContext`）に既記載。2-3 とは別件なので混ぜない。

**挙動が変わる点（承知の上で採用）**

- B の 5 か所は `MeshContext.DestroyReplacedUnityMesh` の影響下に入る。
  `false` にすると破棄されなくなる。従来は無条件破棄だった。
- B の 5 か所は edit mode で `DestroyImmediate` に変わる。
  今まで edit mode では破棄されていなかったので、実質「漏れていたものが解放される」。

**差し替え手段の使い分け（2026-08-28 に追加）**

| 手段 | 破棄 | 使う場面 |
|---|---|---|
| `ReplaceUnityMesh` | 即時 | 個別操作。描画提出と同一フレームにならない経路 |
| `ReplaceUnityMeshDeferred` | 次フレーム（退避キュー） | `WritebackTransformedVertices` のように `Graphics.DrawMesh` 提出と同一フレーム内で走り得る経路 |
| 直接代入 | しない | **使わないこと**（A の新規 `MeshContext` 初期化子は例外） |

**未確認の点**

- C の 7 か所が実測でどれだけ積み上がっていたか（`PLResStat` の `mesh=` の増分）は未計測
- `ReplaceUnityMesh` は即時破棄なので、対象の操作が `EnterTopologyChanged` より前に
  `Graphics.DrawMesh` へ提出済みだと解放済み参照になる。
  UI イベント処理と描画提出の前後関係は未検証。**動作確認は未実施**
- B の 5 か所を edit mode で破棄するようにしたことによる副作用。**動作確認は未実施**

---

## 3. 同期 GPU 読み戻しの構造（1-1 の周辺）

### 3-1. ホバー 1 イベントあたり 5〜6 回の同期読み戻し

**場所**

| 記号 | 場所 | 内容 |
|---|---|---|
| G1 | `UnifiedBufferManager_Update.cs:1032` | `ComputeScreenPositionsGPU` |
| G2 | `:1062` | `DispatchVertexHitTestGPU` |
| G3 | `:1098` | `DispatchVertexSnapHitTestGPU`（現在無効） |
| G4 | `:1127` | `DispatchLineHitTestGPU` |
| G5/G6 | `:1321,1326` | `DispatchFaceHitTestGPU` |

`PlayerViewportPanel.cs:789` が `PointerMoveEvent` ごとに無条件でホバー経路を起動する。
間引きも同一座標判定もない。

**改善案（未着手）**

1. `AsyncGPUReadback` への置き換え
2. 5 つの `Dispatch` をまとめ、結果バッファを 1 本にして読み戻しを 1 回に集約
3. 最近接判定を GPU 側で完結させ、`int` 数個だけ読み戻す（転送量が最小）

現在は「全頂点の距離配列を CPU へ転送 → CPU ループで最小値を探す」形で、
転送量と CPU ループの両方が無駄。

---

### 3-2. 表示用カリングの読み戻しは結果が使われていない

**場所**
`UnifiedSystemAdapter.cs:885`（`cullDisplay`）、`MeshSceneRenderer.cs:563`（`cullSubmit`）

**確認済みの事実**

- 両方とも後続は `DispatchFaceVisibilityGPU` / `DispatchLineVisibilityGPU` /
  `DispatchApplyMirrorCullGPU` の 3 つだけで、GPU 内で完結する
- `_screenPositions` は単一のグローバル配列。4 ビューポート分読み戻しても
  最後の 1 回しか残らない（`PlayerViewportManager.cs:1065-1074` の記述が裏付け）
- **1,381 回のうち約 4 分の 3 は結果が捨てられている**

**注意**
2026-08-27 に `readback: false` で削減を試みたが、クラッシュは直らず、
副作用の疑いが出たため**差し戻した**。効率改善としては有効だが、
クラッシュ対策にはならない。再着手する場合は副作用の検証を先に行うこと。

---

### 3-3. 同一イベント内でスクリーン座標を 2 回計算している

**場所**
`UnifiedMeshSystem_Process.cs:129`（CPU 版 `ComputeScreenPositions`）
→ 直後に `ProcessMouseUpdate` が GPU 版で再計算して読み戻す

全頂点ぶんの投影計算を CPU と GPU で二重に行っている。

---

## 4. データ分類の欠陥

### 4-1. 空オブジェクトが `DrawableMeshes` に載る

**確認済みの事実**

- `TypedMeshIndices.cs:260-310` の分類は `MeshType` のみを見ており、頂点数を見ていない
- `MeshType.Group`（値 6）は `Drawable` に入らない設計だが、
  **`MeshType.Group` を設定する箇所がコード全体に 0 件**
- `MQOImporter.cs:1175` で `meshObject.Type = MeshType.Mesh;` と固定している
- MQO の階層グループ（頂点 0 の入れ物）が `MeshType.Mesh` のまま `Drawable` に載る
- 実測 46 件（頂点 18,992 のモデル）。`moVtx=0 moFace=0`

**対処済み**
描画側にガードを追加（`MeshSceneRenderer.cs:409, 1043, 1111` に `vertexCount <= 0`）。
**発生源は未対処。**

**修正案（未決定）**

- A: インポータで頂点 0・面 0 のオブジェクトに `MeshType.Group` を設定
- B: `TypedMeshIndices` の分類に頂点数を加える
- C: 描画側ガードのみで済ませる（GPU バッファは空オブジェクト分を確保し続ける）

**未確認の点**

- `MeshCategory.Group` の UI 表示が実装されているか
- MQO 以外（PMX / OBJ）でも同じ空オブジェクトが発生するか
- `MeshContext.Type`（`MeshContext.cs:774`）が `MeshObject.Type` に委譲しているか

---

## 5. 初期調査で見つけて保留にしたもの

### 5-1. edit mode で効かない `Object.Destroy`

**場所**

| ファイル:行 | 対象 |
|---|---|
| `PlayerViewport.cs:96` | `_camGo`（`Cam.enabled = true` のカメラ） |
| `PlayerViewport.cs:177` | `RT` |
| `PrimitivePreviewViewport.cs:58,119,147,149` | 各種 |
| ~~`PolyLingPlayerViewerCore.cs:6554,6833,7465`~~ | ~~`Mesh`~~ **済 2026-08-28。2-3 で `ReplaceUnityMesh` へ** |
| ~~`RemoteProjectReceiver.cs:166,214`~~ | ~~`Mesh`~~ **済 2026-08-28。2-3 で `ReplaceUnityMesh` へ** |

**問題**
`PolyLingEditorWindow.cs:15` は `EditorWindow` なので Player コアは edit mode でも動く。
edit mode の `Object.Destroy` はオブジェクトを破棄しない。

`MeshContext.cs:150-157` に `isPlaying` で分岐して `DestroyImmediate` を使う正しい実装
（`DestroyMesh`）があるのに、上記はそれを使っていない。
Mesh の 5 か所は 2-3 で対処済み。残りは `RT` / `_camGo` / プレビュー系で未対処。

---

### 5-2. 死んだ経路の無条件 `Debug.LogError`

**場所**
`UnifiedMeshSystem_Process.cs:189`

```csharp
public void ProcessHoverOnly(Vector2 mousePosition)
{
    _mousePosition = mousePosition;
    ProcessMouseUpdate(cpuOnly: true);
    Debug.LogError("cpuOnly: trueは禁止");   // ← 無条件
}
```

Player 経路からは呼ばれていない（`PlayerViewportManager.cs:1391-1395` に
不使用の理由が明記）。現状は死んだ経路。

---

### 5-3. `PolyLingEditorWindow.cs:62` の無条件 `Repaint()`

`OnEditorUpdate` が毎回 `Repaint()` を呼んでいる。

---

### 5-4. `Debug.Log` が 813 か所

うち改行を含むものが 25 か所。

**関連事象**
Unity Console の `StacktraceWithHyperlinks` が `ArgumentOutOfRangeException` を投げ、
それ自体が Console に記録されて自己増殖するループが観測された。
Console をクリアすると一時的に解消するが、発生は止まらない。

**未確認の点**
どのログ行がパーサ破綻の引き金かは特定していない。

---

## 6. 死んだコード・未使用・展開順序の不統一

| 場所 | 内容 |
|---|---|
| `UnifiedBufferManager_Update.cs:1262,1277` | `DebugPrintCullingStats` — 呼び出し元 0 件。`GetData` を 2 回持つ |
| `BufferManagerBase.cs:52,68` | `CreateBuffer<T>` / `CreateBuffer` — 呼び出し元 0 件 |
| `UnifiedBufferManager_Mirror.cs:262` | `_mirrorScreenPosBuffer` が `ReleaseAllBuffers` の一覧から漏れている（専用の解放処理はある） |
| `UnifiedBufferManager.cs:332-333` | `ExpandedPositionBuffer` / `ExpandedNormalBuffer` — 呼び出し元 0 件 |
| `UnifiedBufferManager.cs:117` | `_expandedNormalBuffer` — 書き込みのみ。読み手 0 件 |
| `PMXHelper.cs:666` | `AppendMeshContextToDocument` — 呼び出し元 0 件（下記 6-1 の欠陥を抱えたまま死んでいる） |

### 6-1. `PMXHelper.AppendExpandedVertices` の自己矛盾（現在は死んだ経路） 2026-08-28

**場所** `PMXHelper.cs:759`（マップ構築）と `:766-780`（頂点追加ループ）

`localMap = meshObject.BuildExpansionMap()` は孤立頂点を**除外**した番号を返すのに、
直後の頂点追加ループには孤立頂点の判定が**無く**、孤立頂点も PMX へ追加している。
`vertexMapping` の値と実際の PMX 頂点番号がずれる。

同じ役割の `PMXExporter.AppendExpandedVertices`（`:817-827`）は
追加ループでも除外しており、**そちらが正しい**。
実際の PMX 出力は `PolyLingPlayerViewerCore.cs:7626` → `PMXExporter.Export` を通るため、
現状ユーザーには影響しない。復活させる場合は必ず直すこと。

### 6-2. 孤立頂点の定義が割れている（3 通り → 2 通りに縮小） 2026-08-28

| 実装 | 「孤立」の定義 |
|---|---|
| `MeshExpansion.BuildNonIsolatedSet`（2-0 の #1〜#5, #7, #8, #9 が使用） | `face.VertexCount >= 3` の面から一度も参照されない頂点。`IsHidden` は見ない |
| ~~`PlayerViewportManager.UpdateExpandedUnityMesh`~~ | ~~上に加えて `face.IsHidden` の面も除外~~ **済 2026-08-28。`MeshExpansion` へ統一** |
| `MQOVertexExpandHelper.GetIsolatedVertices` `:17-38` | **面の頂点数を見ない**（2 頂点の補助線でも「使用済み」扱い）**← 未対処** |

補助線だけに使われている頂点は、1 番目では孤立、3 番目では非孤立になる。
`MQOVertexExpandHelper.CalculateExpandedVertexCount` は部分インポート／
エクスポートのマッチング判定（`MQOPartialMatchHelper.cs:103,150,200,274` /
`PMXPartialImportOps.cs:67`）に使われるため、
補助線を含むメッシュでマッチングが外れる。

**なぜ今回まとめて直さなかったか**
マッチング判定はファイル入出力の互換に直結する。展開バッファの修正と
同時に変えると、不具合が出たときにどちらが原因か切り分けられない。

**未確認の点**
実データで何件外れるかは未計測。

---

## 7. シェーダー関連（Player ビルド）

### 7-1. `Shader.Find` のフォールバックに null チェックが無い

**場所**
`MaterialDataConverter.cs:125-128`

```csharp
shader = Shader.Find(SHADER_URP_LIT) ?? Shader.Find(SHADER_STANDARD);
// ↓ null チェック無し
var mat = new Material(shader);
```

**確認済みの事実**

- `SHADER_STANDARD`（`"Standard"`）は URP ビルドでは必ず null（`:196`）
- Player ビルドでは `Shader.Find` はビルドに含まれるシェーダーしか返さない

**対処済み（設定側）**
`Project Settings > Graphics > Always Included Shaders` に URP の
`Lit` / `Unlit` / `SimpleLit` を追加して起動するようになった。

**未対処（コード側）**
フォールバックが null のまま `new Material(shader)` に渡す欠陥は残っている。

---

## 8. 診断コードの撤去（調査完了後に実施）

以下は調査専用。恒久コードではない。**クラッシュ解決後にすべて削除すること。**

| ファイル | 内容 |
|---|---|
| `PLCamDbg.cs`（新規） | ログ出力とスイッチ読み込み |
| `PLResStat.cs`（新規） | リソース生存数カウント |
| `PLMeshValidator.cs`（新規） | メッシュ検証 |
| `UnifiedBufferManager_Update.cs` | `G1`〜`G14` のマーク、`getdata` ガード、`NewCB` 包み |
| `UnifiedBufferManager_Mirror.cs` | `G15`、`NewCB`、`LiveCB--` |
| `BufferManagerBase.cs` | `G16`、`NewCB`、`LiveCB--` |
| `UnifiedBufferManager.cs` | `NewCB` 40 か所、`LiveCB--`、`PLResStat` 計上 |
| `UnifiedSystemAdapter.cs` | `C1`〜`C6`、`WB`、`PLResStat` 計上、`cull` スイッチ |
| `UnifiedMeshSystem_Process.cs` | `dbgSrc` 引数 |
| `MeshSceneRenderer.cs` | `SubM`、`EmptyMesh`、`adapter`/`xform` スイッチ、`PLResStat` 計上 |
| `UnifiedRenderer.cs` | `SubW`、`PLMeshValidator.Check` |
| `PlayerViewportManager.cs` | `S0`〜`S6`、`cams`/`wire` スイッチ |
| `PolyLingPlayerViewer.cs` | `R1`/`R2`、`F1`/`F2`、`ReportStat` |
| `PolyLingPlayerViewerCore.cs` | `LoadDbg 01`〜`18`、`_loadDbgSubmitLeft` |
| `PlayerViewport.cs` | `RT create` / `RT release` |
| `UpdateMode.cs` | `FromMode` のスイッチ上書き |
| `MeshBridgeDefault.cs` | `LiveMesh++` 4 か所 |
| `MeshContext.cs` | `LiveMesh--` |

**残すべきもの（恒久修正）**

- `MeshSceneRenderer.cs:409,1043,1111` の `vertexCount <= 0` ガード
  ※ ただし 4-1 の発生源を直せば不要になる可能性がある

**削除済み**

- `PLRenderMeshHelper.cs`（`RenderMesh` 差し戻しに伴い不要）

---

## 9. 差し戻した変更（記録）

| 変更 | 理由 | 日付 |
|---|---|---|
| `Graphics.DrawMesh` → `Graphics.RenderMesh`（10 か所） | クラッシュに効果なし。副作用の疑い | 2026-08-27 |
| `ComputeScreenPositionsGPU` の `readback` 引数 | クラッシュに効果なし。副作用の疑い | 2026-08-27 |

---

## 10. 環境情報（参考）

| 項目 | 値 |
|---|---|
| Unity | 6000.3.12f1 |
| GPU | NVIDIA GeForce RTX 4070 Laptop GPU |
| ドライバ | 更新済み（更新前は 31.0.15.3141 / 2023-03-16） |
| CPU 論理コア数 | 24 |
| RAM | 64 GB |
| VRAM | 8 GB |
| Managed Stripping Level | Disabled |
| 常駐 | トレンドマイクロ系 8 プロセス |



忘備録

・ランタイムで漢字がでないという不具合あり：解決。
　ただしエディタ拡張で下記は未対応
----
Editor/CreateRuntime/CreatePlayerViewer.cs:288 は今後も Text Settings 未設定の PanelSettings を作り続ける。ここに、プロジェクト内の PanelTextSettings を検索して自動割当する処理を入れれば同じ問題は再発しない。

手順2のメニュー表記が不明な件も含めて、FontAsset と PanelTextSettings をプログラムから生成する Editor スクリプト（FontAsset.CreateFontAsset を使用）を用意することもできる。この場合メニュー探しは不要になる。
----

・「図形生成」ツールででマテリアルを設定できるようにしたい。：解決

・「図形生成」「高度な図形」「文字」でフォントが存在するフォルダを指定したい。複数のフォルダを指定したい。：解決


・頂点IDの拡張。従来のIDに加え、SubID,PartsID、をつける。MQOにも対応できるようにする。：解決

・辞書。作業軸辞書をプロジェクト内に保存する。独立CSVで。：解決

解決
・不可視オブジェクトもGPUに送って計算だけしてるようだ（未確認）。効率悪い。空オブジェクトが DrawableMeshes に載る、はブロック済みだが別件。ブロックの効率も不明。

---

## 11. 実施した変更の記録

### 11-1. 展開頂点 index 空間の統一と書き戻しの修正 2026-08-28

**対象** 2-1 / 2-2（2-0 に前提を記載）

**新規 1 ファイル**

| ファイル | 内容 |
|---|---|
| `Runtime/Poly_Ling_Main/Core/MeshBridge/MeshExpansion.cs` | 展開順序の唯一の実装。`BuildNonIsolatedSet` / `SlotCount` / `CountExpanded` / `Enumerate` |

**変更 9 ファイル**

| ファイル | 内容 |
|---|---|
| `MeshBridgeDefault.cs` | 展開ループ 3 重複を `MeshExpansion` へ（挙動不変） |
| `MeshObject.cs` | `BuildExpansionMap` / `BuildInverseExpansionMap` を同上 |
| `PlayerViewportManager.cs` | `UpdateExpandedUnityMesh` の独自規則（`IsHidden` を含んでいた）を統一。位相が食い違うときは書かずに return |
| `UnifiedBufferManager_Build.cs` | 展開バッファから孤立頂点を除外。容量計算 2 か所を同じ数え方に。メッシュごとの展開範囲を記録 |
| `UnifiedBufferManager.cs` | `_meshExpandedStart` / `_meshExpandedCount` と `TryGetExpandedRange` を追加 |
| `UnifiedSystemAdapter.cs` | 自前オフセット計算を廃止。再生成は `ReplaceUnityMeshDeferred`。フォールバックを `ToUnityMesh()` + 座標変換へ |
| `MeshContext.cs` | 退避キュー（`RetireUnityMesh` / `FlushRetiredMeshes` / `ReplaceUnityMeshDeferred`） |
| `MeshSceneRenderer.cs` | `RebuildAdapter` 入口でフラッシュ。法線描画の誤コメント訂正 |
| `MultiMeshTopologySnapshotRecord.cs` | 「RebuildAdapter が UnityMesh を作り直す」という誤記の訂正 |

シェーダー（`.compute` / `.shader` / `.hlsl`）は無変更。`MeshInfo` 構造体も無変更。

**実施した検証**

| 項目 | 結果 |
|---|---|
| Roslyn 構文チェック（.NET SDK 8.0.424 / `LanguageVersion.CSharp9`） | 10 ファイル error 0 件 |
| 括弧バランス（文字列・コメント除去後の `{}` `()` `[]`） | 全ファイル 0。途中で負にならず |
| 展開順序の同値検証（ランダム 20,000 ケース） | 新実装は旧 `ToUnityMesh` パス1 と完全一致。旧 GPU マッピングとの差は毎回ちょうど「孤立頂点のスロット数合計」。65.7% のケースで旧 GPU マッピングと不一致だった |
| 未統一の展開ループの残存 | 0 件（`VertexCount < 3) continue` の残り 2 件はいずれも展開ループではない） |
| 型名衝突 | `MeshExpansion` の既存定義 0 件 |
| 改行・BOM | 全ファイル LF、CR 0 バイト、BOM なし |
| `.meta` | 2 行・末尾改行なし。既存ファイルの GUID は変更なし |

**計画からの追加判断**

- 再生成後に頂点数が合わなかった `Mesh` は退避キューを経由せず即時破棄する。
  一度も `Graphics.DrawMesh` へ提出していないため。
- `BuildExpandedVertexMapping` で非孤立集合を 1 メッシュにつき 1 回だけ構築する。
  素直に書くと 1 メッシュあたり 3 回作ることになる。

**動作確認（未実施）**

以下は実機で確認すること。

- 孤立頂点を持つメッシュ（面なし OBJ、補助線のみのオブジェクト、MQO 由来データ）を
  読み込んで頂点ドラッグ → 位置ずれとリークが消えているか
- 位相 Undo / Redo を**非選択**メッシュに対して行い、表示が追随するか
- Console に `[WritebackTransformedVertices]` の警告が出るか。
  出た場合はメッシュ名と頂点数が記録されるので、呼び出し側の再構築漏れとして追うこと
- `PLResStat` の `mesh=` カウントがドラッグ中に単調増加しないか
- 退避キュー（1 フレーム遅延破棄）で、以前の「矩形選択が一部頂点で効かない」症状が
  再発しないか

---

### 11-2. `UnityMesh` 直接代入の解消（2-3） 2026-08-28

**対象** 2-3

**新規ファイル** なし

**変更 4 ファイル**

| ファイル | 変更 |
|---|---|
| `PolyLingCore_Commands.cs` | `:500` を `ReplaceUnityMesh` へ（1 か所） |
| `PlayerCommandDispatcher.cs` | `:1172` `:1204` `:1242` `:2580` `:5389` を `ReplaceUnityMesh` へ。`:1266` は二重生成のため行を削除（6 か所） |
| `PolyLingPlayerViewerCore.cs` | `:6554-6555` `:6833-6834` `:7465-7467` の `Object.Destroy` + 直接代入を `ReplaceUnityMesh` へ（3 か所） |
| `RemoteProjectReceiver.cs` | `ReceiveMeshData` の事前破棄ブロックを廃し末尾で `ReplaceUnityMesh(rebuilt)` に集約。`DestroyModelRuntimeObjects` を `ReplaceUnityMesh(null)` へ（2 か所） |

`RemoteProjectReceiver` の `rebuilt` は `UnityEngine.Mesh` で修飾した
（`MeshObject` と隣り合う文脈のため。`Poly_Ling.Data` に `Mesh` 型の定義は 0 件）。

**実施した検証**

| 項目 | 結果 |
|---|---|
| Roslyn 構文チェック（.NET SDK 8.0.424 / `LanguageVersion.CSharp9`） | 変更 4 ファイル error 0。変更前も 0（基準一致） |
| 括弧バランス（文字列・コメント除去後の `{}` `()` `[]`） | 変更前後で同値。途中で負にならず |
| `ReplaceUnityMesh` のオーバーロード | 1 個のみ（`MeshContext.cs:140`）。`null` 渡しで曖昧にならない |
| 変更後の直接代入の再 grep | 残存は A（新規 `MeshContext`）と D のみ。B・C は 0 件 |
| 改行・BOM | 全ファイル LF、CR 0 バイト、BOM なし、末尾改行あり |
| diff | 意図した 12 か所のみ。他の差分なし |

**動作確認（未実施）**

- 材質スロット削除／材質適用／LSCM 展開／UV→XYZ／メッシュ結合／
  ミラー実体化・解除 の直後に表示が欠けないか
- 上記を繰り返して `PLResStat` の `mesh=` が単調増加しないか
- リモート受信（PLRD）でメッシュ差し替え後に表示が残るか
- 異常が出たら `MeshContext.DestroyReplacedUnityMesh = false` で切り分ける。
  false なら変更前と同じ挙動（漏れるが破棄しない）に戻る




マテリアルのUNDO対応。
計画から1点外しました。Undo 記録は入れていません。
MaterialDataSnapshot.ApplyTo（MaterialUndoRecords.cs:83）が matRef.InvalidateCache() を呼びます。描画は MeshSceneRenderer.cs:514 が毎フレーム model.GetMaterial(sub) を叩くため、キャッシュ破棄直後に GetOrCreateMaterial（MaterialReference.cs:118-147）が ToMaterial で材質を作り直します。Player では SetTexture（MaterialDataConverter.cs:788）が EditorBridgeNull.LoadAssetAtPath（EditorBridgeNull.cs:21-25）を呼んで null を返すため、Undo するたびにテクスチャが消えて LogError が出ます。この既存レコードをそのまま使うと不具合を作るので見送りました。

スプリングボーンの編集機能。
スプリングボーンのシミュレーション機能
スプリングボーンのパラメータ最大最小など不足してるもの


パイプ群などの特殊な対称化:一部完成


LOOKAT


質問：(A) の MirrorBaker（一時ミラーで頂点ID・PartsId を保存するよう MirrorBaker.cs:424,428 を直す）を今回の作業に含めますか、それとも振り直しツールだけ先に作りますか。
